-- Soil (chunk 20, D2): grows the crop planted in it and hands it to the player who harvests it (Use).
-- A crop grows only while the soil holds water, using one second of water per second of growth, at the
-- soil's soil.growth speed, through soil.get().stages stages; it is ripe after its seed's grow_time. The
-- host stores the result (seed, grown, water, stage) on the soil, where clients, bots and saves see it.
--
-- Growth is worked out from the world clock (world.time(), which carries on across saves) since the soil
-- was last brought up to date (its `updated` stamp), not step by step. A parked Creation (no player near)
-- runs no on_update, so when it is next updated it catches up on the whole time it was parked; growing in
-- one long stretch gives the same crop as growing in many short ones.

local next_check = 0

-- A bare item id in a seed's data belongs to the seed's own mod.
local function qualify(id, owner)
  if string.find(id, ":", 1, true) then
    return id
  end
  return string.match(owner, "^[^:]+") .. ":" .. id
end

-- grown, water and stage of soil `s` once it has grown until `now`. Changes nothing.
local function grown_by(s, now)
  if not s.seed then
    return s.grown, s.water, 0
  end
  local seed = world.item(s.seed, "seed")
  local grow_time = seed and seed.grow_time
  if not grow_time or grow_time <= 0 then
    -- The seed's mod is gone or it says nothing about growing: leave it as it is.
    return s.grown, s.water, s.stage
  end
  local grown, water = s.grown, s.water
  local elapsed = math.max(0, now - (s.updated or now))
  if elapsed > 0 and water > 0 and grown < grow_time then
    local speed = (world.item(part.type(), "soil") or {}).growth or 1
    -- Water goes only on the growing still to do.
    local used = math.min(elapsed, water)
    if speed > 0 then
      used = math.min(used, (grow_time - grown) / speed)
    end
    water = water - used
    grown = math.min(grow_time, grown + used * speed)
  end
  return grown, water, math.min(s.stages, math.floor(grown / grow_time * s.stages))
end

-- Grows the crop up to `now` and stores it. Returns the soil as it is now.
local function settle(now)
  local s = soil.get()
  local grown, water, stage = grown_by(s, now)
  soil.set({ grown = grown, water = water, stage = stage, updated = now })
  return soil.get()
end

-- What a harvest gives: the crop times its yield, and seeds back.
local function harvest_of(seed_id, seed)
  local items = { { item = qualify(seed.crop, seed_id), count = seed.yield or 1 } }
  if (seed.seeds or 0) > 0 then
    items[#items + 1] = { item = seed_id, count = seed.seeds }
  end
  return items
end

local function name_of(id)
  return world.item(id, "name") or id
end

function on_update(dt)
  -- Settling is exact whenever it runs, so every properties.interval seconds is enough.
  local now = world.time()
  if now < next_check then
    return
  end
  next_check = now + properties.interval
  settle(now)
end

function can_activate(peer)
  local s = soil.get()
  if not s.seed then
    return "Nothing is growing there."
  end
  local seed = world.item(s.seed, "seed")
  if not seed then
    return "The " .. s.seed .. " grows nothing."
  end
  local _, _, stage = grown_by(s, world.time())
  if stage < s.stages then
    return "The " .. name_of(qualify(seed.crop, s.seed)) .. " is not ripe yet."
  end
  if not player.can_give(peer, harvest_of(s.seed, seed)) then
    return "Make room in your inventory first."
  end
end

function on_activate(peer)
  local s = settle(world.time())
  local items = harvest_of(s.seed, world.item(s.seed, "seed"))
  local ok, why = player.give(peer, items)
  if not ok then
    error(why)
  end
  soil.clear()
  return { items = items }
end
