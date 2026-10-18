-- The full water bucket (chunk 20b, D13): using it throws the water in a lob that lands where the player aims
-- (up to properties.range metres away; further, or at nothing, it is a plain throw at properties.throw_speed),
-- and the bucket turns back into properties.empty in the same slot. Where the water lands, every fire within
-- properties.splash metres loses properties.douse strength (1 puts a fire out) and every soil part within
-- properties.splash metres gets properties.seconds of water, up to what it holds.

-- The launch velocity that lands the water on what the player aims at, or a plain lob along the aim.
local function throw_velocity(target, from)
  local aimed = combat.cast(target.from, target.direction, properties.range)
  if aimed then
    local to = aimed.position - from
    local seconds = math.min(math.max(to:length() / properties.throw_speed, 0.2), 1.2)
    return to / seconds - world.gravity() * (0.5 * seconds)
  end
  return (target.direction + vec3(0, properties.throw_up, 0)):normalized() * properties.throw_speed
end

function on_use(target)
  local from = target.from + target.direction * 0.4
  local velocity = throw_velocity(target, from)
  local id, why = combat.projectile(from, velocity, { look = "water", lifetime = 3 })
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
  local doused = fire.douse(landing.position, properties.splash, properties.douse)
  local watered = 0
  for _, soil in ipairs(farm.near(landing.position, properties.splash)) do
    if farm.water(soil.creation, soil.part, properties.seconds) then
      watered = watered + 1
    end
  end
  return { fires = doused, watered = watered }
end
