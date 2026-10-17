-- Driver's seat: the sitting player's throttle (W/S, -1 to 1) is the seat's output, so link it to engines.
-- Linked bearings that turn about an upright axis steer: A/D turns them up to properties.steer_angle
-- degrees, at properties.steer_speed rad/s with at most properties.steer_torque N·m. When the seat is empty
-- the output is 0 and the wheels straighten.

local RESEND = 30

local function steering(id, up)
  local joint = creation.joint(id)
  if not joint or joint.type ~= "rotational" or not joint.axis then
    return nil
  end
  local upright = joint.axis:dot(up)
  if math.abs(upright) < 0.7 then
    return nil
  end
  return upright > 0 and 1 or -1
end

local function steer(x)
  local up = part.to_world(vec3(0, 1, 0))
  local resend = world.tick() % RESEND == 0
  for _, id in ipairs(part.links()) do
    local sign = steering(id, up)
    if sign then
      -- Steering right turns the wheels clockwise seen from above: a negative angle about up.
      local angle = -x * math.rad(properties.steer_angle) * sign
      local key = tostring(id)
      if resend or state.steer[key] ~= angle then
        creation.set_bearing_angle(id, angle, properties.steer_speed, properties.steer_torque)
        state.steer[key] = angle
      end
    end
  end
end

function on_create()
  state.steer = state.steer or {}
  state.throttle = 0
end

function on_reload()
  state.steer = {}
end

function on_update(dt)
  local input = part.occupant_input()
  local x, z = 0, 0
  if input then
    x, z = input.x, input.z
  end
  state.throttle = math.max(-1, math.min(1, z))
  steer(math.max(-1, math.min(1, x)))
end

function get_outputs()
  return state.throttle
end
