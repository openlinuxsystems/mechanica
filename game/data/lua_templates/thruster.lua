-- Thruster: pushes toward its base (along the part's -Y) at the part
-- {{name}}. Properties: "thrust" (N).

function on_update(dt)
  local thrust = properties.thrust or 0
  if thrust == 0 then return end
  local direction = part.to_world(vec3(0, -1, 0))
  part.apply_force(direction * thrust, part.position())
end
