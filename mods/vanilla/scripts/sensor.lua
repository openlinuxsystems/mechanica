-- Sensor: looks out of its face (the part's +Y) for up to the "range" setting in m, and its output is 1
-- while something is there. It checks every properties.interval steps, and plays its "detect" and "lose"
-- sounds when that changes.

function on_create()
  state.seen = state.seen or false
end

function on_update(dt)
  if world.tick() % (properties.interval or 1) ~= 0 then
    return
  end
  local face = part.to_world(vec3(0, 1, 0))
  local from = part.position()
  local seen = world.raycast(from, from + face * (part.setting("range") or 4)) ~= nil
  if seen ~= state.seen then
    local key = seen and part.sound("detect") or part.sound("lose")
    if key then sound.play(key) end
  end
  state.seen = seen
end

function get_outputs()
  return state.seen
end
