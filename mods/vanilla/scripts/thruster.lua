-- Thruster: a jet that pushes its Creation toward its base (its exhaust is the +Y face), with up to
-- properties.thrust N at full power. Its power, 0 to 1, comes from the "control" setting: "link" is the
-- sum of its logic inputs (a switch, a seat's throttle), "w_s" and "a_d" are the keys of the pilot in a seat
-- linked to it, and "space_shift" is a throttle lever the pilot opens with Space and closes with Shift, at
-- properties.lever_rate per second, that stays where they leave it. "invert" flips the command, so S or A
-- fires it instead.
-- Its "power" setting (the menu's lever) picks a power step from 1 to the top its level unlocks (3 at level 1, one more
-- each upgrade): each step above the first multiplies the push and the fuel it burns by properties.step_gain.
-- It burns properties.fuel from the containers linked into it with the Connect Tool (gas containers), or from every
-- container of the Creation when none is, one item per properties.fuel_time seconds at full power on step 1 (a higher
-- level's fuel_time is longer, so it burns less); in Creative it needs none.
-- Sounds: "ignite" when it lights, the "loop" while it burns (its "thrust" parameter follows the power)
-- and "cut" when it goes out. Its "power" display (a glow at the exhaust) follows the power, its "pose" readout plays
-- the model's "on" and "off" clips (the burner glows while it fires), and its menu shows "fuel" (what is left of the
-- item burning now) and "fuel_items" (gasoline left in its tanks).

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

-- The containers linked into the thruster (its gas containers), or nil when none is: then it burns from all of the
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

-- The push and fuel multiplier of the "power" step.
local function boost()
  local step = part.setting("power") or 1
  return (properties.step_gain or 1.5) ^ (step - 1)
end

-- Burns fuel for dt seconds at this power; false when the Creation has none left.
local function burn(power, dt)
  if world.creative() or power == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - power * boost() * dt
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

local function flame(power)
  if power > 0 and not state.loop then
    play("ignite")
    if part.sound("loop") then state.loop = sound.start(part.sound("loop"), { thrust = power }) end
    state.level = power
  elseif power == 0 and state.loop then
    sound.stop(state.loop)
    state.loop = nil
    play("cut")
  elseif state.loop and math.abs(power - state.level) > 0.05 then
    sound.set_parameter(state.loop, "thrust", power)
    state.level = power
  end
end

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

function on_create()
  state.pose = nil
end

function on_update(dt)
  local power = clamp(command(dt), 0, 1)
  if not burn(power, dt) then
    power = 0
  end
  state.power = power
  part.display("power", power)
  part.display("fuel", world.creative() and 1 or clamp((state.fuel or 0) / properties.fuel_time, 0, 1))
  part.display("fuel_items", creation.count(properties.fuel, tanks()))
  show(power > 0 and "on" or "off")
  flame(power)
  if power > 0 then
    -- The exhaust leaves through +Y, so the push is along -Y, at the part.
    local d = part.to_world(vec3(0, -1, 0))
    local f = properties.thrust * boost() * power
    part.apply_force(vec3(d.x * f, d.y * f, d.z * f), part.position())
  end
end

function get_outputs()
  return state.power or 0
end
