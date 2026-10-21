-- Soil (chunk 20, D2): grows the crop planted in it and hands it to the player who harvests it (Use).
-- A crop grows only while the soil holds water, using one second of water per second of growth, at the
-- soil's soil.growth speed, through soil.get().stages stages; it is ripe after its seed's grow_time. With nothing
-- planted the soil dries out instead, as in Scrap Mechanic: full soil is dry after soil.dry_time seconds. The host
-- stores the result (seed, grown, water, stage) on the soil, where clients, bots and saves see it, and works out from
-- the water how wet the soil looks (dry, watered, wet or drying).
--
-- Growth is worked out from the world clock (world.time(), which carries on across saves) since the soil
-- was last brought up to date (its `updated` stamp), not step by step. A parked Creation (no player near)
-- runs no on_update, so when it is next updated it catches up on the whole time it was parked; growing in
-- one long stretch gives the same crop as growing in many short ones.
--
-- Soil built onto a Creation (the Growbed, E9) runs this script too. Its crop rides along with the Creation, and its
-- data can set two more things: soil.yield, a fixed number of crops every harvest gives (seeds back still follow the
-- seed), and which seeds it takes (soil.seeds, checked by the host when one is planted). With properties.charge_item
-- it needs power: a crop only grows while one charge_item (a battery) from the Battery Containers linked into it (or
-- from any of its Creation's containers when none is linked) lasts, properties.charge_time seconds of growing each;
-- empty soil and a ripe crop use none, and Creative needs none. Its model then shows "on" on its "pose" readout
-- while the crop grows (the cyan power pip) and "off" otherwise.

local next_check = 0

-- A bare item id in a seed's data belongs to the seed's own mod.
local function qualify(id, owner)
  if string.find(id, ":", 1, true) then
    return id
  end
  return string.match(owner, "^[^:]+") .. ":" .. id
end

-- The containers linked into the part (Battery Containers), or nil for all of the Creation's.
local function cells()
  local ids
  for _, input in ipairs(part.inputs()) do
    if input.storage then
      ids = ids or {}
      ids[#ids + 1] = input.part
    end
  end
  return ids
end

-- True when the soil needs power to grow (properties.charge_item) and this is not Creative.
local function needs_power()
  return properties.charge_item ~= nil and not world.creative()
end

-- Seconds of growing the power at hand lasts: the battery in use and those in the linked containers. Changes nothing.
local function power_left()
  if not needs_power() then
    return math.huge
  end
  return (state.charge or 0) + creation.count(properties.charge_item, cells()) * properties.charge_time
end

-- Uses `seconds` of growing power, taking batteries as the one in use runs out.
local function draw(seconds)
  if not needs_power() or seconds <= 0 then
    return
  end
  local charge = (state.charge or 0) - seconds
  if charge < 0 then
    local wanted = math.ceil(-charge / properties.charge_time)
    charge = charge + creation.take(properties.charge_item, wanted, cells()) * properties.charge_time
  end
  state.charge = math.max(0, charge)
end

-- grown, water and stage of soil `s` once it has grown until `now`, with `power` seconds of growing power at hand,
-- and the seconds it grew for. Changes nothing.
local function grown_by(s, now, power)
  if not s.seed then
    -- Nothing planted: the water dries out, full soil in soil.dry_time seconds (none set: it keeps).
    local data = world.item(part.type(), "soil") or {}
    local elapsed = math.max(0, now - (s.updated or now))
    if s.water > 0 and data.dry_time and data.dry_time > 0 then
      return s.grown, math.max(0, s.water - elapsed * (data.capacity or 0) / data.dry_time), 0, 0
    end
    return s.grown, s.water, 0, 0
  end
  local seed = world.item(s.seed, "seed")
  local grow_time = seed and seed.grow_time
  if not grow_time or grow_time <= 0 then
    -- The seed's mod is gone or it says nothing about growing: leave it as it is.
    return s.grown, s.water, s.stage, 0
  end
  local grown, water = s.grown, s.water
  local elapsed = math.max(0, now - (s.updated or now))
  local used = 0
  if elapsed > 0 and water > 0 and grown < grow_time then
    local speed = (world.item(part.type(), "soil") or {}).growth or 1
    -- Water (and power) go only on the growing still to do.
    used = math.min(elapsed, water, power or math.huge)
    if speed > 0 then
      used = math.min(used, (grow_time - grown) / speed)
    end
    water = water - used
    grown = math.min(grow_time, grown + used * speed)
  end
  return grown, water, math.min(s.stages, math.floor(grown / grow_time * s.stages)), used
end

-- Grows the crop up to `now`, using power for it, and stores it. Returns the soil as it is now, and whether the crop
-- grew all that time (false when it ran out of water or power, or is ripe).
local function settle(now)
  local s = soil.get()
  local grown, water, stage, used = grown_by(s, now, power_left())
  draw(used)
  soil.set({ grown = grown, water = water, stage = stage, updated = now })
  local elapsed = math.max(0, now - (s.updated or now))
  return soil.get(), s.seed ~= nil and stage < s.stages and used >= elapsed and water > 0
end

-- The "pose" readout of soil that needs power: "on" while its crop grows, "off" otherwise.
local function show(growing)
  if properties.charge_item == nil then
    return
  end
  local pose = growing and "on" or "off"
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

-- What a harvest gives: the crop times its yield, and seeds back. A seed with seeds_max gives from seeds to seeds_max
-- back, rolled per harvest; `most` takes the most it could give (to check there is room for it).
local function harvest_of(seed_id, seed, most)
  local fixed = (world.item(part.type(), "soil") or {}).yield
  local items = { { item = qualify(seed.crop, seed_id), count = fixed or seed.yield or 1 } }
  local least = seed.seeds or 0
  local count = least
  if seed.seeds_max and seed.seeds_max > least then
    count = most and seed.seeds_max or math.random(least, seed.seeds_max)
  end
  if count > 0 then
    items[#items + 1] = { item = seed_id, count = count }
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
  local _, growing = settle(now)
  show(growing)
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
  local _, _, stage = grown_by(s, world.time(), power_left())
  if stage < s.stages then
    return "The " .. name_of(qualify(seed.crop, s.seed)) .. " is not ripe yet."
  end
  if not player.can_give(peer, harvest_of(s.seed, seed, true)) then
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
