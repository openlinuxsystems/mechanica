-- Food (chunk 20b; consumables batch C2): eating one heals the holder by properties.health. properties.eat_seconds is
-- how long one takes: the host refuses the next one until it is over (with some slack for network jitter). A food with
-- properties.drink is drunk: the game plays the drink clip and the drink's sound instead of bites, and the player can
-- sprint while drinking.

-- Share of eat_seconds the host insists on between two (network jitter).
local JITTER = 0.8

function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
  if state.done_at and world.time() < state.done_at then
    return properties.drink and "You are still drinking." or "You are still eating."
  end
end

function on_use(target)
  item.consume(1)
  if properties.eat_seconds then
    state.done_at = world.time() + properties.eat_seconds * JITTER
  end
  return { health = holder.heal(properties.health), drink = properties.drink == true }
end
