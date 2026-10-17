-- Watering (chunk 20b): pouring the item on soil gives it properties.seconds of water, up to what the
-- soil holds, and uses the item up.

function can_use(target)
  if not target.part then
    return "Aim at soil to water it."
  end
  local soil, why = farm.soil(target.creation, target.part)
  if not soil then
    return why
  end
  if soil.water >= soil.capacity then
    return "The soil is already full of water."
  end
end

function on_use(target)
  local soil, why = farm.water(target.creation, target.part, properties.seconds)
  if not soil then
    error(why)
  end
  item.consume(1)
  return soil
end
