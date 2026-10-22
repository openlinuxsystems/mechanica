-- The Chemical Bucket (Parts map E11): an empty Water Bucket dipped in a chemical pool (CollectWater; the pool's liquid
-- names this item as its bucket). Used on a container that takes properties.item (a Chemical Container), it pours
-- properties.count of it in and turns back into properties.empty; used anywhere else it throws the chemical in a lob
-- that lands where the player aims, like a full water bucket, and splashes green. Chemical puts out no fire and waters
-- no soil.

-- The launch velocity that lands the chemical on what the player aims at, or a plain lob along the aim.
local function throw_velocity(target, from)
  local aimed = combat.cast(target.from, target.direction, properties.range)
  if aimed then
    local to = aimed.position - from
    local seconds = math.min(math.max(to:length() / properties.throw_speed, 0.2), 1.2)
    return to / seconds - world.gravity() * (0.5 * seconds)
  end
  return (target.direction + vec3(0, properties.throw_up, 0)):normalized() * properties.throw_speed
end

-- How much of the chemical the container aimed at still takes, or nil when nothing aimed at takes it.
local function room(target)
  if target.creation and target.part then
    return inventory.space(target.creation, target.part, properties.item)
  end
  return nil
end

function can_use(target)
  local space = room(target)
  if space and space < properties.count then
    return "There is no room for it there."
  end
end

function on_use(target)
  if room(target) then
    local ok, why = inventory.put(target.creation, target.part, properties.item, properties.count)
    if not ok then
      error(why)
    end
    item.replace(properties.empty)
    local pour = item.sound("pour")
    if pour then
      sound.play(pour, target.from + target.direction)
    end
    return { poured = properties.count }
  end
  local from = target.from + target.direction * 0.4
  local id, why = combat.projectile(from, throw_velocity(target, from), { look = "chemical", lifetime = 3, size = properties.splash })
  if not id then
    error(why)
  end
  item.replace(properties.empty)
  local throw = item.sound("throw")
  if throw then
    sound.play(throw, from)
  end
  return { thrown = id }
end

function on_hit(landing)
  local splash = item.sound("splash")
  if splash then
    sound.play(splash, landing.position)
  end
  return { splashed = true }
end
