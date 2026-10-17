-- Sensor: looks out of its face (the part's +Y) for up to the "range" setting in m, and its output is 1
-- while something is there. It checks every properties.interval steps.

function on_create()
  state.seen = state.seen or false
end

function on_update(dt)
  if world.tick() % (properties.interval or 1) ~= 0 then
    return
  end
  local face = part.to_world(vec3(0, 1, 0))
  local from = part.position()
  state.seen = world.raycast(from, from + face * (part.setting("range") or 4)) ~= nil
end

function get_outputs()
  return state.seen
end
