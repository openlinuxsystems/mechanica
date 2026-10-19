-- Fizz Bomb (the Items Tutorial's thrown item). The item's "aim" section has fire_on_release, so the player
-- holds right click to wind up and lets go to throw (left click while aiming throws too). The bomb is the
-- item itself: each throw uses one. It flies as a projectile and bursts where it lands, or when its fuse
-- runs out. The numbers are the item's properties:
--   damage (at the centre), radius (m), impulse (N·s), speed and aimed_speed (m/s), fuse (s), interval (s).

function can_use(target)
  if state.last_throw and world.time() - state.last_throw < properties.interval * 0.8 then
    return "You are still winding up."
  end
end

function on_use(target)
  state.last_throw = world.time()
  item.consume(1)
  -- A thrown bomb flies faster when wound up; a little upward tilt makes a lob.
  local speed = target.aiming and properties.aimed_speed or properties.speed
  local direction = (target.direction + vec3(0, 0.2, 0)):normalized()
  local id = combat.projectile(holder.eye(), direction * speed, { lifetime = properties.fuse })
  return { projectile = id }
end

-- It burst: hurt and push everything within the radius, falling off to nothing at the edge.
function on_hit(landing)
  local hits = combat.splash(landing.position, properties.radius, properties.damage, properties.impulse)
  local boom = item.sound("explode")
  if boom then
    sound.play(boom, landing.position)
  end
  -- Count this player's hits across sessions (state is saved with the world).
  state.hits = (state.hits or 0) + #hits
end
