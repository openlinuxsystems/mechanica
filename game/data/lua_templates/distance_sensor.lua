-- Distance sensor: measures how far the nearest thing is along the part's +Y
-- {{name}}. Properties: "range" (m). Its reading is state.distance (nil when nothing is in range).

function on_update(dt)
  local range = properties.range or 0
  if range <= 0 then return end
  local from = part.position()
  local hit = world.raycast(from, from + part.to_world(vec3(0, range, 0)))
  state.distance = hit and hit.distance or nil
end

function get_outputs()
  return state.distance
end
