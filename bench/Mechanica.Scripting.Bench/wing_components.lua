-- The benchmark wing with its vector maths written out per component (same API, fewer vec3 tables).
local up = vec3(0, 1, 0)

function on_create()
  state.steps = 0
end

function on_update(dt)
  state.steps = state.steps + 1
  local v = part.velocity()
  local vx, vy, vz = v.x, v.y, v.z
  local speed2 = vx * vx + vy * vy + vz * vz
  if speed2 < 1e-8 then return end
  local speed = math.sqrt(speed2)
  local fx, fy, fz = vx / speed, vy / speed, vz / speed
  local n = part.to_world(up)
  local sin_aoa = -(fx * n.x + fy * n.y + fz * n.z)
  local q = 0.5 * properties.air_density * speed2 * properties.area
  local l = q * properties.lift_slope * sin_aoa
  local d = -q * (properties.drag_min + properties.drag_slope * sin_aoa * sin_aoa)
  part.apply_force(vec3(n.x * l + fx * d, n.y * l + fy * d, n.z * l + fz * d))
end
