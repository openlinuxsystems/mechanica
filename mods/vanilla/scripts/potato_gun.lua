-- Potato guns and thrown splash weapons (chunk 20b). A shot is one or more hitscan pellets from the
-- holder's eyes; with a radius it bursts where it hits (or at the end of its range) and hurts everything
-- near. The numbers are the item's properties:
--   damage, interval (s), range (m), ammo (item id, optional), pellets (default 1),
--   spread (degrees, default 0), radius (m, optional), impulse (N·s, default 0).

-- Share of the interval the host insists on between shots (network jitter).
local JITTER = 0.8

function can_use(target)
  if state.last_shot and world.time() - state.last_shot < properties.interval * JITTER then
    return "The " .. item.name() .. " is not ready yet."
  end
  if properties.ammo and inventory.count(properties.ammo) < 1 then
    return "You have no " .. item.name(properties.ammo) .. " to fire."
  end
end

function on_use(target)
  state.last_shot = world.time()
  if properties.ammo then
    inventory.take(properties.ammo, 1)
  end
  local hits = {}
  local damage, range, impulse = properties.damage, properties.range, properties.impulse or 0
  for _, direction in ipairs(combat.pellets(target.direction, properties.pellets or 1, properties.spread or 0)) do
    if properties.radius then
      local hit = combat.cast(target.from, direction, range)
      local at = hit and hit.position or target.from + direction * range
      for _, h in ipairs(combat.splash(at, properties.radius, damage, impulse)) do
        hits[#hits + 1] = h
      end
    else
      hits[#hits + 1] = combat.hitscan(target.from, direction, range, damage, impulse)
    end
  end
  return { hits = hits }
end
