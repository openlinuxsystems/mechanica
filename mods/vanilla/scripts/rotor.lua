-- Rotor: a powered propeller that pulls its Creation away from its base (along its +Y) with up to
-- properties.thrust N at full speed, and twists it the other way from its spin with up to
-- properties.counter_torque N·m, so a single rotor turns a helicopter: pair clockwise and counter-clockwise
-- rotors (the "spin" setting, as seen from the rotor's top) to cancel it. Power scales its speed, which
-- follows the command at properties.spool_rate per second, and thrust and twist follow the speed. Air
-- already flowing through the disc takes thrust away: climbing along its axis at properties.inflow_speed
-- m/s it pulls nothing, and sinking it pulls more, up to properties.max_inflow_gain times as much. That
-- keeps a helicopter's climb gentle and makes it settle into a hover.
-- The command, 0 to 1, comes from the "control" setting: "link" is the sum of its logic inputs, "w_s" and
-- "a_d" are the keys of the pilot in a seat linked to it, and "space_shift" is a collective lever the pilot
-- raises with Space and lowers with Shift, at properties.lever_rate per second, that stays where they leave
-- it. "invert" flips the command.
-- It is an electric motor: while it is driven it burns properties.fuel (batteries) from the containers linked into it
-- with the Connect Tool (Battery Containers), or from every container of the Creation when none is, one item per
-- properties.fuel_time seconds at full command; with none left it spools down and gives no thrust. In Creative it needs
-- none.
-- Sounds: "spin_up" when it starts turning, the "loop" while it turns (its "speed" parameter follows the
-- speed) and "spin_down" when it is told to stop. Its "power" display (the downwash glow) follows the speed. Its model spins at up to properties.spin_rate rad/s.
-- Its "blades" readout plays the model's "cw" or "ccw" clip, pitching the blades for its spin, and its menu shows
-- "fuel" (what is left of the battery in use) and "fuel_items" (batteries left in its containers).

local SHOW_EVERY = 10

-- The spin the model was last given (not saved: a loaded rotor shows its spin afresh).
local shown_spin

local function clamp(v, lo, hi)
  return math.max(lo, math.min(hi, v))
end

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

local function pilot()
  for _, input in ipairs(part.inputs()) do
    local controls = creation.occupant_input(input.part)
    if controls then return controls end
  end
end

-- The command from -1 to 1, from the "control" setting.
local function command(dt)
  local control = part.setting("control") or "link"
  local value
  if control == "link" then
    value = part.input()
  elseif control == "space_shift" then
    local controls = pilot()
    local move = controls and ((controls.jump and 1 or 0) - (controls.sprint and 1 or 0)) or 0
    state.lever = clamp((state.lever or 0) + move * properties.lever_rate * dt, 0, 1)
    value = state.lever
  else
    local controls = pilot()
    value = controls and (control == "w_s" and controls.z or controls.x) or 0
  end
  if part.setting("invert") then value = -value end
  return clamp(value, -1, 1)
end

-- The containers linked into the rotor (its Battery Containers), or nil when none is: then it burns from all of the
-- Creation's.
local function tanks()
  local ids
  for _, input in ipairs(part.inputs()) do
    if input.storage then
      ids = ids or {}
      ids[#ids + 1] = input.part
    end
  end
  return ids
end

-- Burns charge for dt seconds at this command; false when the Creation has no batteries left.
local function burn(target, dt)
  if world.creative() or target == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - target * dt
  if state.fuel > 0 then
    return true
  end
  if creation.take(properties.fuel, 1, tanks()) > 0 then
    state.fuel = state.fuel + properties.fuel_time
    return true
  end
  state.fuel = 0
  return false
end

local function show()
  local blades = part.setting("spin") == "counterclockwise" and "ccw" or "cw"
  if state.blades ~= blades then
    state.blades = blades
    part.display("blades", blades)
  end
  if world.tick() % SHOW_EVERY == 0 then
    part.display("fuel", world.creative() and 1 or clamp((state.fuel or 0) / properties.fuel_time, 0, 1))
    part.display("fuel_items", creation.count(properties.fuel, tanks()))
  end
end

local function noise(target, speed)
  if target > 0 and not state.loop then
    play("spin_up")
    if part.sound("loop") then state.loop = sound.start(part.sound("loop"), { speed = speed }) end
    state.level = speed
  elseif state.loop then
    if speed < 0.02 and target == 0 then
      sound.stop(state.loop)
      state.loop = nil
    elseif math.abs(speed - state.level) > 0.05 then
      sound.set_parameter(state.loop, "speed", speed)
      state.level = speed
    end
  end
  if target == 0 and (state.target or 0) > 0 then play("spin_down") end
  state.target = target
end

function on_create()
  state.blades = nil
end

function on_update(dt)
  local target = clamp(command(dt), 0, 1)
  if not burn(target, dt) then
    target = 0
  end
  show()
  local speed = state.speed or 0
  local step = properties.spool_rate * dt
  speed = speed + clamp(target - speed, -step, step)
  state.speed = speed
  part.display("power", math.floor(speed * 20 + 0.5) / 20)
  noise(target, speed)
  -- Clockwise seen from the top is about -Y. Small changes while spooling are not worth sending.
  local spin = speed * properties.spin_rate * (part.setting("spin") == "counterclockwise" and 1 or -1)
  local settled = speed == target and spin ~= shown_spin
  if shown_spin == nil or settled or math.abs(spin - shown_spin) > 0.02 * properties.spin_rate then
    part.animate(nil, spin)
    shown_spin = spin
  end
  if speed <= 0 then return end
  local axis = part.to_world(vec3(0, 1, 0))
  local ax, ay, az = axis.x, axis.y, axis.z
  local v = part.velocity()
  local inflow = (v.x * ax + v.y * ay + v.z * az) / properties.inflow_speed
  local f = properties.thrust * speed * clamp(1 - inflow, 0, properties.max_inflow_gain)
  part.apply_force(vec3(ax * f, ay * f, az * f), part.position())
  -- A blade turning clockwise seen from the top spins about -Y, so the Creation is twisted about +Y.
  local t = properties.counter_torque * speed
  if part.setting("spin") == "counterclockwise" then t = -t end
  part.apply_torque(vec3(ax * t, ay * t, az * t))
end

function get_outputs()
  return state.speed or 0
end
