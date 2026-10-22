-- Fertilizer (consumables batch C2): used on soil with a crop growing in it, it makes that crop grow
-- properties.boost times faster until it is harvested (the soil's script multiplies the soil's boost into growth). The
-- crop still needs water: growing uses one second of water per second, however fast it grows. One is used up.

function can_use(target)
  if not target.part then
    return "Aim at a growing crop to use the " .. item.name() .. "."
  end
  local soil, why = farm.soil(target.creation, target.part)
  if not soil then
    return why
  end
  if not soil.seed then
    return "Plant something there first."
  end
  if soil.ripe then
    return "That crop is already ripe."
  end
  if soil.boost >= properties.boost then
    return "That crop is already fertilized."
  end
end

function on_use(target)
  local soil, why = farm.boost(target.creation, target.part, properties.boost)
  if not soil then
    error(why)
  end
  item.consume(1)
  local used = item.sound("use")
  if used then
    local hit = combat.cast(target.from, target.direction, 8)
    sound.play(used, hit and hit.position or target.from + target.direction)
  end
  return soil
end
