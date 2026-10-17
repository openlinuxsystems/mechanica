-- Potato guns and thrown splash weapons (chunk 20b). A shot is one or more pellets from the holder's
-- eyes. Without a speed each pellet is a hitscan ray; with a radius it bursts where the ray hits (or at
-- the end of its range) and hurts everything near. With a speed each pellet is lobbed (D14): a projectile
-- that arcs under gravity and bursts where it lands, or when its fuse runs out. The numbers are the
-- item's properties:
--   damage, interval (s), range (m, hitscan), speed (m/s, lobbed), fuse (s, lobbed, optional),
--   ammo (item id, optional), pellets (default 1), spread (degrees, default 0), radius (m, optional),
--   impulse (N·s, default 0), aim_spread (degrees while aiming with right click, default spread),
--   magazine (shots between reloads, optional) and reload (s a reload takes, default 1).
-- With a magazine the gun fires that many shots, then Reload (R) loads it again from the ammo carried. The
-- holder's hotbar slot and HUD show the rounds loaded and the ammo carried (item.counter).

-- Share of the interval (and the reload time) the host insists on (network jitter).
local JITTER = 0.8

local function carried()
  return properties.ammo and inventory.count(properties.ammo) or 0
end

-- Shots loaded: a gun comes loaded the first time it is taken in hand (or fired).
local function loaded()
  if state.loaded == nil then
    state.loaded = math.min(properties.magazine, carried())
  end
  return state.loaded
end

local function busy()
  return state.ready_at and math.max(0, state.ready_at - world.time()) or 0
end

-- Tells the holder's HUD what is loaded and carried.
local function show()
  if properties.magazine or properties.ammo then
    item.counter({ value = properties.magazine and loaded() or nil, max = properties.magazine, ammo = properties.ammo, busy = busy() })
  end
end

function on_equip()
  show()
end

function can_use(target)
  if state.last_shot and world.time() - state.last_shot < properties.interval * JITTER then
    return "The " .. item.name() .. " is not ready yet."
  end
  if busy() > (properties.reload or 1) * (1 - JITTER) then
    return "The " .. item.name() .. " is still reloading."
  end
  if properties.ammo and carried() < 1 then
    return "You have no " .. item.name(properties.ammo) .. " to fire."
  end
  if properties.magazine and loaded() < 1 then
    return "The " .. item.name() .. " is empty. Reload it."
  end
end

function on_use(target)
  state.last_shot = world.time()
  if properties.ammo then
    inventory.take(properties.ammo, 1)
  end
  if properties.magazine then
    state.loaded = math.min(loaded() - 1, carried())
  end
  show()
  local fire = item.sound("fire")
  if fire then
    sound.play(fire)
  end
  local hits = {}
  local damage, range, impulse = properties.damage, properties.range, properties.impulse or 0
  local spread = properties.spread or 0
  if target.aiming and properties.aim_spread then
    spread = properties.aim_spread
  end
  local directions = combat.pellets(target.direction, properties.pellets or 1, spread)
  if properties.speed then
    local shots = {}
    for _, direction in ipairs(directions) do
      shots[#shots + 1] = combat.projectile(target.from, direction * properties.speed, { lifetime = properties.fuse })
    end
    return { projectiles = shots }
  end
  for _, direction in ipairs(directions) do
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

function can_reload_item()
  if not properties.magazine then
    return "The " .. item.name() .. " does not reload."
  end
  if busy() > 0 then
    return "The " .. item.name() .. " is already reloading."
  end
  if loaded() >= math.min(properties.magazine, carried()) then
    if carried() < 1 then
      return "You have no " .. item.name(properties.ammo) .. " to load."
    end
    return "The " .. item.name() .. " is already loaded."
  end
end

-- Loads as many shots as the magazine holds and the ammo carried allows; they are taken as they are fired.
function on_reload_item()
  state.loaded = math.min(properties.magazine, carried())
  state.ready_at = world.time() + (properties.reload or 1)
  local sound_id = item.sound("reload") or item.sound("pump")
  if sound_id then
    sound.play(sound_id)
  end
  show()
end

-- A lobbed shot landed (or its fuse ran out): it bursts there, or hits what it struck.
function on_hit(landing)
  local damage, impulse = properties.damage, properties.impulse or 0
  if properties.radius then
    combat.splash(landing.position, properties.radius, damage, impulse)
  elseif landing.hit then
    combat.hit(landing.hit, damage)
  end
end
