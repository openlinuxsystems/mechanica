-- Wing: lift and drag from the airflow over the part
-- {{name}}. Properties: "area" (m²), "air_density" (kg/m³), "lift_slope" (per rad),
-- "drag_min" and "drag_slope". The wing's surface faces the part's +Y.

function on_update(dt)
  local v = part.velocity()
  local vx, vy, vz = v.x, v.y, v.z
  local speed2 = vx * vx + vy * vy + vz * vz
  if speed2 < 1e-6 then return end
  local speed = math.sqrt(speed2)
  local fx, fy, fz = vx / speed, vy / speed, vz / speed
  local n = part.to_world(vec3(0, 1, 0))
  local sin_aoa = -(fx * n.x + fy * n.y + fz * n.z)
  local q = 0.5 * (properties.air_density or 0) * speed2 * (properties.area or 0)
  local lift = q * (properties.lift_slope or 0) * sin_aoa
  local drag = -q * ((properties.drag_min or 0) + (properties.drag_slope or 0) * sin_aoa * sin_aoa)
  part.apply_force(vec3(n.x * lift + fx * drag, n.y * lift + fy * drag, n.z * lift + fz * drag), part.position())
end
