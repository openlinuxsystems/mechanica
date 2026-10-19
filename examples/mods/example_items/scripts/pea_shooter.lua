-- Pea Shooter (the Items Tutorial's gun). Each shot is a projectile that flies in an arc on the host; every
-- client draws it, and on_hit runs where it lands. The gun holds properties.magazine peas; Reload (R)
-- loads it again from the Dried Peas the player carries. The numbers are the item's properties:
--   ammo (item id), damage, push (N·s), interval (s), speed and aimed_speed (m/s), lifetime (s),
--   magazine (shots) and reload (s).

-- Share of the interval the host insists on, to allow for network jitter.
local JITTER = 0.8

local function carried()
  return inventory.count(properties.ammo)
end

-- Peas loaded. A new gun comes loaded with what the player carries, up to a full magazine.
local function loaded()
  if state.loaded == nil then
    state.loaded = math.min(properties.magazine, carried())
  end
  return state.loaded
end

-- Seconds left of a reload.
local function busy()
  return state.ready_at and math.max(0, state.ready_at - world.time()) or 0
end

-- Shows "loaded / magazine" and the peas carried on the holder's hotbar slot and HUD.
local function show()
  item.counter({ value = loaded(), max = properties.magazine, ammo = properties.ammo, busy = busy() })
end

function on_equip()
  show()
end

function can_use(target)
  if state.last_shot and world.time() - state.last_shot < properties.interval * JITTER then
    return "The " .. item.name() .. " is not ready yet."
  end
  if busy() > 0 then
    return "The " .. item.name() .. " is still reloading."
  end
  if loaded() < 1 then
    return "The " .. item.name() .. " is empty. Reload it."
  end
end

function on_use(target)
  state.last_shot = world.time()
  -- Each shot uses one pea from the inventory; the magazine counts how many are ready to fire.
  inventory.take(properties.ammo, 1)
  state.loaded = math.min(loaded() - 1, carried())
  show()
  local fire = item.sound("fire")
  if fire then
    sound.play(fire)
  end
  -- Aiming (right click held) shoots faster and flatter.
  local speed = target.aiming and properties.aimed_speed or properties.speed
  local id = combat.projectile(target.from, target.direction * speed, {
    lifetime = properties.lifetime,
    -- data comes back in on_hit. Keep it plain data: numbers, strings, booleans and tables of them.
    data = { damage = properties.damage, x = target.direction.x, y = target.direction.y, z = target.direction.z },
  })
  return { projectile = id }
end

-- A projectile this script launched landed: hit is missing when it flew its whole lifetime.
function on_hit(landing)
  if landing.hit then
    local d = landing.data
    -- Push the part it hit along the shot's direction with properties.push N·s.
    combat.hit(landing.hit, d.damage, vec3(d.x, d.y, d.z) * properties.push)
  end
end

function can_reload_item()
  if busy() > 0 then
    return "The " .. item.name() .. " is already reloading."
  end
  if carried() < 1 then
    return "You have no " .. item.name(properties.ammo) .. " to load."
  end
  if loaded() >= math.min(properties.magazine, carried()) then
    return "The " .. item.name() .. " is already loaded."
  end
end

function on_reload_item()
  state.loaded = math.min(properties.magazine, carried())
  state.ready_at = world.time() + properties.reload
  local reload = item.sound("reload")
  if reload then
    sound.play(reload)
  end
  show()
end
