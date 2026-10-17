-- Mounted Potato Blaster (chunk 23): a potato gun built into a creation. It fires out of its front when
-- its input turns on, or when a player presses F on it, at most once every properties.interval seconds,
-- with one properties.ammo from the creation's containers (none in Creative). Each shot is a hitscan of
-- properties.range metres for properties.damage, pushing what it hits with properties.impulse N·s.

function on_create()
  state.ready_at = state.ready_at or 0
  state.was_on = state.was_on or false
end

local function fire()
  local now = world.time()
  if now < state.ready_at then
    return
  end
  if properties.ammo and not world.creative() and creation.take(properties.ammo, 1) < 1 then
    return
  end
  state.ready_at = now + properties.interval
  local id = part.sound("fire")
  if id then
    sound.play(id)
  end
  local direction = part.to_world(vec3(0, 0, -1))
  combat.hitscan(part.point_to_world(vec3(0, 0, properties.muzzle)), direction, properties.range, properties.damage, properties.impulse)
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
