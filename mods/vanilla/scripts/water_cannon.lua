-- Water cannon (chunk 23): fires a jet of water out of its front when its input turns on, or when a
-- player presses F on it, at most once every properties.interval seconds. Soil it hits within
-- properties.range metres gets properties.water seconds of water; anything else it hits is pushed with
-- properties.impulse N·s.

function on_create()
  state.ready_at = state.ready_at or 0
  state.was_on = state.was_on or false
end

local function fire()
  local now = world.time()
  if now < state.ready_at then
    return
  end
  state.ready_at = now + properties.interval
  local id = part.sound("fire")
  if id then
    sound.play(id)
  end
  local direction = part.to_world(vec3(0, 0, -1))
  local hit = combat.cast(part.point_to_world(vec3(0, 0, properties.muzzle)), direction, properties.range)
  if not hit then
    return
  end
  if hit.creation and hit.part and farm.water(hit.creation, hit.part, properties.water) then
    return
  end
  combat.hit(hit, 0, direction * properties.impulse)
end

function on_activate(peer)
  fire()
end

function on_update(dt)
  local on = part.input() > 0
  if on and not state.was_on then
    fire()
  end
  state.was_on = on
end
