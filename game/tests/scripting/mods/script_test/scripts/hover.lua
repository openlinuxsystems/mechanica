-- Hovers its Creation at properties.height: a spring-damper on height, plus the weight.
function on_create()
  state.steps = 0
  state.target = properties.height
end

function on_update(dt)
  state.steps = state.steps + 1
  local p, v = part.position(), part.velocity()
  local g = -world.gravity().y
  local accel = g + properties.stiffness * (state.target - p.y) - properties.damping * v.y
  part.apply_force(vec3(0, creation.mass() * accel, 0))
end
