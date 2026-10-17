-- Thruster: a jet that pushes its Creation toward its base (its exhaust is the +Y face), with up to
-- properties.thrust N at full power. Its power, 0 to 1, comes from the "control" setting: "link" is the
-- sum of its logic inputs (a switch, a seat's throttle), "w_s" and "a_d" are the keys of the pilot in a seat
-- linked to it, and "space_shift" is a throttle lever the pilot opens with Space and closes with Shift, at
-- properties.lever_rate per second, that stays where they leave it. "invert" flips the command, so S or A
-- fires it instead.
-- It burns properties.fuel from the Creation's containers, one item per properties.fuel_time seconds at
-- full power; in Creative it needs none.
-- Sounds: "ignite" when it lights, the "loop" while it burns (its "thrust" parameter follows the power)
-- and "cut" when it goes out.

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

-- Burns fuel for dt seconds at this power; false when the Creation has none left.
local function burn(power, dt)
  if world.creative() or power == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - power * dt
  if state.fuel > 0 then
    return true
  end
  if creation.take(properties.fuel, 1) > 0 then
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

function on_update(dt)
  local power = clamp(command(dt), 0, 1)
  if not burn(power, dt) then
    power = 0
  end
  state.power = power
  flame(power)
  if power > 0 then
    -- The exhaust leaves through +Y, so the push is along -Y, at the part.
    local d = part.to_world(vec3(0, -1, 0))
    local f = properties.thrust * power
    part.apply_force(vec3(d.x * f, d.y * f, d.z * f), part.position())
  end
end

function get_outputs()
  return state.power or 0
end
