-- Mounted Potato Blaster (chunk 23, parts map batch 6): a potato gun built into a creation. It fires out of its front
-- while its input is on (up to once every properties.interval seconds) and once each time a player presses E on it,
-- with one properties.ammo from the Potato Ammo Containers linked into it with the Connect Tool (none in Creative;
-- with none linked or left it only clicks, properties "dry" sound). Each shot is a hitscan of properties.range metres
-- for properties.damage, pushing what it hits with properties.impulse N·s.
-- Its model plays "fire" on its "clip" readout (the barrel kicks back) for every shot.

function on_create()
  state.ready_at = state.ready_at or 0
  state.shots = state.shots or 0
end

-- The Potato Ammo Containers linked into the gun.
local function hoppers()
  local ids = {}
  for _, input in ipairs(part.inputs()) do
    if input.storage then
      ids[#ids + 1] = input.part
    end
  end
  return ids
end

local function load()
  if not properties.ammo or world.creative() then
    return true
  end
  local linked = hoppers()
  return #linked > 0 and creation.take(properties.ammo, 1, linked) > 0
end

local function fire()
  local now = world.time()
  if now < state.ready_at then
    return
  end
  state.ready_at = now + properties.interval
  if not load() then
    local dry = part.sound("dry")
    if dry then
      sound.play(dry)
    end
    return
  end
  local id = part.sound("fire")
  if id then
    sound.play(id)
  end
  state.shots = state.shots + 1
  part.display("clip", "fire#" .. state.shots)
  local direction = part.to_world(vec3(0, 0, -1))
  combat.hitscan(part.point_to_world(vec3(0, 0, properties.muzzle)), direction, properties.range, properties.damage, properties.impulse)
end

function on_activate(peer)
  fire()
end

function on_update(dt)
  -- A trigger held on keeps firing at the gun's rate (a linked seat button or switch).
  local signal = 0
  for _, input in ipairs(part.inputs()) do
    if not input.storage then
      signal = signal + input.value
    end
  end
  if signal > 0 then
    fire()
  end
end
