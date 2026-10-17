-- Benchmark wing (D2): thin-plate lift and drag from the airflow at the part, every physics step.
-- Reads the part's velocity and orientation and pushes through ApplyForce, as a real wing would.
local up = vec3(0, 1, 0)

function on_create()
  state.steps = 0
end

function on_update(dt)
  state.steps = state.steps + 1
  local v = part.velocity()
  local speed2 = v:length_squared()
  if speed2 < 1e-8 then return end
  local speed = math.sqrt(speed2)
  local flow = v / speed
  local normal = part.to_world(up)
  local sin_aoa = -flow:dot(normal)
  local q = 0.5 * properties.air_density * speed2 * properties.area
  local lift = normal * (q * properties.lift_slope * sin_aoa)
  local drag = flow * (-q * (properties.drag_min + properties.drag_slope * sin_aoa * sin_aoa))
  part.apply_force(lift + drag)
end
