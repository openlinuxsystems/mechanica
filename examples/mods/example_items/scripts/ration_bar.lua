-- Ration Bar (the Items Tutorial's food). An item script runs on the host, one copy per player and item
-- type, while that player holds and uses the item.
--
-- The globals an item script gets:
--   item        the item in hand: its slot, count, sounds; consume or replace it
--   holder      the player holding it: health, eyes, where they look
--   properties  the "properties" object from the item's JSON (properties.health)
--   state       this player's own table, saved with the world under their player

-- Before every use. Return a string to refuse it (the player sees why); return nothing to allow it.
-- can_use only reads: changing the world here is an error.
function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
end

-- The player used the item (left click with it in hand).
function on_use(target)
  item.consume(1)
  local health = holder.heal(properties.health)
  local eat = item.sound("eat")
  if eat then
    sound.play(eat)
  end
  -- state is per player and saved with the world, so this count carries on across sessions.
  state.eaten = (state.eaten or 0) + 1
  -- What on_use returns is the UseItem action's result.
  return { health = health, eaten = state.eaten }
end
