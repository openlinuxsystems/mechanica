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
-- Sounds: "spin_up" when it starts turning, the "loop" while it turns (its "speed" parameter follows the
-- speed) and "spin_down" when it is told to stop.

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

function on_update(dt)
  local target = clamp(command(dt), 0, 1)
  local speed = state.speed or 0
  local step = properties.spool_rate * dt
  speed = speed + clamp(target - speed, -step, step)
  state.speed = speed
  noise(target, speed)
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
