-- Seeds (chunk 20b): planting one in the soil under the crosshair uses it up. The crop grows while the
-- soil is watered; growth and harvest follow the item's seed section.

function can_use(target)
  if not target.part then
    return "Aim at soil to plant the " .. item.name() .. "."
  end
  local soil, why = farm.soil(target.creation, target.part)
  if not soil then
    return why
  end
  if soil.seed then
    return "Something is already growing there."
  end
end

function on_use(target)
  local soil, why = farm.plant(target.creation, target.part)
  if not soil then
    error(why)
  end
  item.consume(1)
  return soil
end
