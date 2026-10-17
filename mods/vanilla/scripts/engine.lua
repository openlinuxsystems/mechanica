-- Engine: turns the wheel bearings its output is linked to. Its input is a throttle from -1 (back) to 1
-- (forward), usually a driver's seat. Wheels roll toward the engine's front, so place it facing the way the
-- car should drive. With no throttle it holds the wheels with properties.brake N·m.
-- Properties: "speed" (rad/s at the top gear), "torque" (N·m), "brake" (N·m), "fuel" (item id) and
-- "fuel_time" (seconds of full throttle per item). In Creative it needs no fuel.
-- Setting "gear" (1 to 5): each gear turns a fifth of the top speed.

local RESEND = 30

local function clamp(v)
  return math.max(-1, math.min(1, v))
end

-- +1 when a positive velocity on the bearing rolls its wheel toward the engine's front, -1 when it rolls it
-- back, nil when the link does not go to a bearing.
local function direction(id)
  local joint = creation.joint(id)
  if not joint or joint.type ~= "rotational" or not joint.axis then
    return nil
  end
  local forward = part.to_world(vec3(0, 0, -1))
  local up = part.to_world(vec3(0, 1, 0))
  local roll = joint.axis:cross(up):dot(forward)
  if math.abs(roll) < 0.5 then
    return nil -- it turns about an upright or lengthwise axis: a steering or other bearing, not a wheel
  end
  return roll > 0 and 1 or -1
end

-- Burns fuel for dt seconds at this throttle; false when the engine is out of fuel.
local function burn(throttle, dt)
  if world.creative() or throttle == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - math.abs(throttle) * dt
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

local function drive(throttle)
  local gear = part.setting("gear") or 1
  local speed = throttle * properties.speed * gear / 5
  local resend = world.tick() % RESEND == 0
  local sent = state.sent
  for _, id in ipairs(part.links()) do
    local sign = direction(id)
    if sign then
      local velocity, torque = sign * speed, properties.torque
      if throttle == 0 then
        torque = properties.brake
      end
      local key = tostring(id)
      if resend or sent[key] ~= velocity or state.torque ~= torque then
        creation.set_bearing(id, velocity, torque)
        sent[key] = velocity
      end
    end
  end
  state.torque = throttle == 0 and properties.brake or properties.torque
end

function on_create()
  state.sent = state.sent or {}
  state.fuel = state.fuel or 0
end

function on_reload()
  state.sent = {}
end

function on_update(dt)
  local throttle = clamp(part.input())
  if not burn(throttle, dt) then
    throttle = 0
  end
  state.throttle = throttle
  drive(throttle)
end

function get_outputs()
  return state.throttle or 0
end

function on_destroy()
  for _, id in ipairs(part.links() or {}) do
    if direction(id) then
      creation.set_bearing(id, nil)
    end
  end
end
