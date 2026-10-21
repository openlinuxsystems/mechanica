-- Plasma Drill (parts map batch 6): while its input is on it throws a plasma beam out of its front, up to its "range"
-- setting in metres. Every properties.interval seconds the beam bites the first thing it meets for properties.damage
-- as drill damage (kind "drill"), so it mines rock deposits, boulders and chunks from a distance as a spinning Drill
-- does up close, and grinds parts and bots; a flammable part it touches catches fire at properties.burn strength.
-- It runs on batteries: one properties.charge_item from the Battery Containers linked into it (or from any of the
-- Creation's containers when none is linked) lasts properties.charge_time seconds of beam. Creative needs none.
-- Sounds: "start", the "loop" while it fires (its "load" parameter rises while the beam bites) and "stop". Its model
-- plays "on" and "off" on its "pose" readout (the coils and the emitter light up), its "beam" display is the beam's
-- glow, and its menu shows "charge" (what is left of the battery in use) and "batteries" (left in its containers).

local function clamp(v, lo, hi)
  return math.max(lo, math.min(hi, v))
end

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

local function signal()
  local sum = 0
  for _, input in ipairs(part.inputs()) do
    if not input.storage then
      sum = sum + input.value
    end
  end
  return sum
end

-- Uses dt seconds of charge; false when there is none left.
local function draw(dt)
  if world.creative() then
    return true
  end
  state.charge = (state.charge or 0) - dt
  if state.charge > 0 then
    return true
  end
  if creation.take(properties.charge_item, 1, cells()) > 0 then
    state.charge = state.charge + properties.charge_time
    return true
  end
  state.charge = 0
  return false
end

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

local function hum(on, load)
  if on and not state.loop then
    local start = part.sound("start")
    if start then sound.play(start) end
    if part.sound("loop") then state.loop = sound.start(part.sound("loop"), { load = load }) end
  elseif not on and state.loop then
    sound.stop(state.loop)
    state.loop = nil
    local stop = part.sound("stop")
    if stop then sound.play(stop) end
  elseif state.loop and load ~= state.load then
    sound.set_parameter(state.loop, "load", load)
  end
  state.load = load
end

local function bite()
  local direction = part.to_world(vec3(0, 0, -1))
  local muzzle = part.point_to_world(vec3(0, 0, properties.muzzle))
  local range = clamp(part.setting("range") or properties.range, 0.5, properties.range)
  local hit = combat.cast(muzzle, direction, range)
  if not hit then
    return false
  end
  combat.hit(hit, properties.damage, direction * properties.impulse, properties.kind)
  if hit.creation and hit.part then
    fire.ignite(hit.creation, hit.part, properties.burn)
  end
  return true
end

function on_create()
  state.pose = nil
  state.next_bite = state.next_bite or 0
end

function on_update(dt)
  local on = signal() > 0 and draw(dt)
  local now = world.time()
  if on and now >= state.next_bite then
    state.next_bite = now + properties.interval
    state.biting = bite()
  elseif not on then
    state.biting = false
  end
  hum(on, state.biting and 1 or 0)
  show(on and "on" or "off")
  part.display("beam", on and 1 or 0)
  part.display("charge", world.creative() and 1 or clamp((state.charge or 0) / properties.charge_time, 0, 1))
  part.display("batteries", creation.count(properties.charge_item, cells()))
end
