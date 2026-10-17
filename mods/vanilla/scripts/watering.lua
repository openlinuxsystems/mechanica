-- Watering (chunk 20b): pouring the item on soil gives it properties.seconds of water, up to what the
-- soil holds, and uses the item up. Aimed at a fire (D13) it puts that out instead: every fire within
-- properties.splash metres of the first fire along the aim, up to properties.range metres away, loses
-- properties.douse strength (1 puts a fire out).

-- Burning fires within properties.splash metres of the aim, nearest first.
local function fires_in_aim(target)
  local found = {}
  for _, f in ipairs(fire.near(target.from, properties.range + properties.splash)) do
    local to = f.position - target.from
    local along = to:dot(target.direction)
    if along > 0 and along <= properties.range + properties.splash
        and (to - target.direction * along):length() <= properties.splash then
      found[#found + 1] = f
    end
  end
  return found
end

function can_use(target)
  if #fires_in_aim(target) > 0 then
    return nil
  end
  if not target.part then
    return "Aim at soil or a fire to use it."
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
  local fires = fires_in_aim(target)
  if #fires > 0 then
    local doused, why = fire.douse(fires[1].position, properties.splash, properties.douse)
    if not doused then
      error(why)
    end
    item.consume(1)
    return { fires = doused }
  end
  local soil, why = farm.water(target.creation, target.part, properties.seconds)
  if not soil then
    error(why)
  end
  item.consume(1)
  return soil
end
