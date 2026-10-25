-- Hover jet: pushes its Creation toward its base (its jet is the +Y face) with up to properties.thrust N, so
-- one under a Creation lifts it. Its power, 0 to 1, comes from the "control" setting: "link" is the sum of
-- its logic inputs, "w_s" and "a_d" are the keys of the pilot in a seat linked to it, and "space_shift" is a
-- lever the pilot raises with Space and lowers with Shift, at properties.lever_rate per second, that stays
-- where they leave it. "invert" flips the command.
-- With the "stabilize" setting on it also keeps its Body level while it runs: it twists the Body back
-- toward the jet pointing straight down with properties.level_torque N·m per radian of tilt, less
-- properties.level_damping N·m per rad/s of rolling or pitching, at most properties.max_level_torque N·m.
-- It never turns the Body about the vertical, so the pilot keeps the heading.
-- It is an electric fan: it burns properties.fuel (batteries) from the containers linked into it with the Connect Tool
-- (Battery Containers), or from every container of the Creation when none is, one item per properties.fuel_time
-- seconds at full power; with none left it gives no thrust. In Creative it needs none.
-- Sounds: "ignite" when it starts, the "loop" while it runs (its "thrust" parameter follows the power)
-- and "cut" when it stops. Its "power" display (a glow under the jet) follows the power, its "pose" readout plays the
-- model's "run_loop" clip (the fan spins and the lip ring glows) while it runs and "off" when it stops, and its menu
-- shows "fuel" (what is left of the battery in use) and "fuel_items" (batteries left in its containers).

local SHOW_EVERY = 10

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

-- The containers linked into the jet (its Battery Containers), or nil when none is: then it burns from all of the
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

-- Burns charge for dt seconds at this power; false when the Creation has no batteries left.
local function burn(power, dt)
  if world.creative() or power == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - power * dt
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

local function show(power)
  local pose = power > 0 and "run_loop" or "off"
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
  if world.tick() % SHOW_EVERY == 0 then
    part.display("fuel", world.creative() and 1 or clamp((state.fuel or 0) / properties.fuel_time, 0, 1))
    part.display("fuel_items", creation.count(properties.fuel, tanks()))
  end
end

local function hum(power)
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

-- The twist that turns the Body so up (the push direction) points to the sky, without any yaw.
local function level(ux, uy, uz)
  -- Tilt axis and angle from the push direction to straight up: up × sky.
  local tx, tz = -uz, ux
  local s = math.sqrt(tx * tx + tz * tz)
  local angle = math.atan2(s, uy)
  local w = part.angular_velocity()
  -- Only the rolling and pitching part of the spin is damped, about the horizontal.
  local kx, kz
  if s > 1e-6 then
    kx, kz = tx / s * angle, tz / s * angle
  else
    kx, kz = 0, 0
  end
  local px = properties.level_torque * kx - properties.level_damping * w.x
  local pz = properties.level_torque * kz - properties.level_damping * w.z
  local m = math.sqrt(px * px + pz * pz)
  if m > properties.max_level_torque then
    px, pz = px / m * properties.max_level_torque, pz / m * properties.max_level_torque
  end
  part.apply_torque(vec3(px, 0, pz))
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
  show(power)
  hum(power)
  if power == 0 then return end
  local d = part.to_world(vec3(0, -1, 0))
  local f = properties.thrust * power
  part.apply_force(vec3(d.x * f, d.y * f, d.z * f), part.position())
  if part.setting("stabilize") then
    level(d.x, d.y, d.z)
  end
end

function get_outputs()
  return state.power or 0
end
