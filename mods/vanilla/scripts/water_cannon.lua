-- Water cannon (chunk 23, parts map batch 6): fires a jet of water out of its front when its input turns on, or when
-- a player presses E on it, at most once every properties.interval seconds. Its water comes from the Water
-- Containers linked into it with the Connect Tool: one properties.water_item (a full water bucket) gives
-- properties.jets jets, and with none linked or left it only splutters (properties.dry, a click). Creative needs no
-- water. Fires within properties.splash metres of the jet lose properties.douse strength (D13). Soil it hits within
-- properties.range metres gets properties.water seconds of water; anything else it hits is pushed with
-- properties.impulse N·s.
-- Its model plays "fire" on its "clip" readout (the barrel kicks back and the nozzle flares) for every jet.

function on_create()
  state.ready_at = state.ready_at or 0
  state.was_on = state.was_on or false
  state.jets = state.jets or 0
  state.shots = state.shots or 0
end

-- The Water Containers linked into the cannon.
local function tanks()
  local ids = {}
  for _, input in ipairs(part.inputs()) do
    if input.storage then
      ids[#ids + 1] = input.part
    end
  end
  return ids
end

-- Uses one jet of water; false when there is none.
local function draw()
  if world.creative() then
    return true
  end
  if state.jets > 0 then
    state.jets = state.jets - 1
    return true
  end
  local linked = tanks()
  if #linked > 0 and creation.take(properties.water_item, 1, linked) > 0 then
    state.jets = properties.jets - 1
    return true
  end
  return false
end

-- Puts water on every fire within properties.splash metres of the jet, up to length metres out.
local function douse_along(from, direction, length)
  for _, f in ipairs(fire.near(from, length + properties.splash)) do
    local to = f.position - from
    local along = to:dot(direction)
    if along > 0 and along <= length + properties.splash
        and (to - direction * along):length() <= properties.splash then
      fire.douse(f.position, 0.1, properties.douse)
    end
  end
end

local function fire_jet()
  local now = world.time()
  if now < state.ready_at then
    return
  end
  state.ready_at = now + properties.interval
  if not draw() then
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
  local muzzle = part.point_to_world(vec3(0, 0, properties.muzzle))
  local hit = combat.cast(muzzle, direction, properties.range)
  douse_along(muzzle, direction, hit and hit.distance or properties.range)
  if not hit then
    return
  end
  if hit.creation and hit.part and farm.water(hit.creation, hit.part, properties.water) then
    return
  end
  combat.hit(hit, 0, direction * properties.impulse)
end

function on_activate(peer)
  fire_jet()
end

function on_update(dt)
  local on = part.input() > 0
  if on and not state.was_on then
    fire_jet()
  end
  state.was_on = on
end
