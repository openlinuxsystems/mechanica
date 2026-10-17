-- Food (chunk 20b): eating one heals the holder by properties.health.

function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
end

function on_use(target)
  item.consume(1)
  return { health = holder.heal(properties.health) }
end
