-- Sticky Wheel (parts map batch 6): a wheel whose tread oozes a chemical gel that holds it to what it rolls on, so a
-- creation can drive up walls and across ceilings. Every other step it casts properties.rays rays out round its tread
-- (in its rolling plane, the part's X-Z plane) up to properties.reach m past its rim; when any finds a surface it pulls
-- its Body toward the surfaces it found with up to properties.force N, scaled by its "stickiness" setting (1 to 10,
-- in tenths of full).
-- It sticks while it has gel: it burns properties.chemical from the Chemical Containers linked into it (or from any of
-- the Creation's containers when none is linked), one per properties.chemical_time seconds at full stickiness and
-- less at lower settings; Creative needs none. With a switch or button linked into it, it sticks only while that is
-- on; with none, always.
-- Sounds: "pump" when it starts sticking and the "loop" while it holds. Its model plays "on" and "off" on its "pose"
-- readout (the gel glistens and the pump's pip lights), and its menu shows "gel" (what is left of the chemical in use)
-- and "chemicals" (left in its containers).

-- The last pull found (a world direction) and the share of rays that found a surface; kept out of `state`, which
-- holds only plain data for saves.
local toward, touching

local function clamp(v, lo, hi)
  return math.max(lo, math.min(hi, v))
end

local function tanks()
  local ids
  for _, input in ipairs(part.inputs()) do
    if input.storage then
      ids = ids or {}
      ids[#ids + 1] = input.part
    end
  end
  return ids
end

-- True while a trigger linked into it is on, or when none is linked.
local function wanted()
  local any, sum = false, 0
  for _, input in ipairs(part.inputs()) do
    if not input.storage then
      any = true
      sum = sum + input.value
    end
  end
  return not any or sum > 0
end

local function draw(share, dt)
  if world.creative() then
    return true
  end
  state.gel = (state.gel or 0) - share * dt
  if state.gel > 0 then
    return true
  end
  if creation.take(properties.chemical, 1, tanks()) > 0 then
    state.gel = state.gel + properties.chemical_time
    return true
  end
  state.gel = 0
  return false
end

-- The pull toward the surfaces round the tread: a vector of length 0 to 1 (the share of rays that found one), or nil.
local function surfaces()
  local centre = part.position()
  if not centre then
    return nil
  end
  local sum, found = vec3(0, 0, 0), 0
  local reach = properties.radius + properties.reach
  for i = 0, properties.rays - 1 do
    local a = 2 * math.pi * i / properties.rays
    local d = part.to_world(vec3(math.cos(a), 0, math.sin(a)))
    local hit = world.raycast(centre, centre + d * reach)
    if hit and hit.distance > 0 then
      sum = sum + d
      found = found + 1
    end
  end
  if found == 0 then
    return nil
  end
  local length = sum:length()
  if length < 1e-6 then
    return nil
  end
  return sum * (1 / length), found / properties.rays
end

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

local function hold(on)
  if on and not state.loop then
    local pump = part.sound("pump")
    if pump then sound.play(pump) end
    if part.sound("loop") then state.loop = sound.start(part.sound("loop")) end
  elseif not on and state.loop then
    sound.stop(state.loop)
    state.loop = nil
  end
end

function on_create()
  state.pose = nil
  state.tick = 0
end

function on_update(dt)
  local share = clamp((part.setting("stickiness") or 5) / 10, 0.1, 1)
  local on = wanted() and draw(share, dt)
  state.tick = (state.tick or 0) + 1
  if on and state.tick % 2 == 0 then
    toward, touching = surfaces()
  elseif not on then
    toward, touching = nil, nil
  end
  if on and toward then
    local f = properties.force * share * (0.5 + 0.5 * touching)
    part.apply_force(toward * f, part.position())
  end
  hold(on and toward ~= nil)
  show(on and "on" or "off")
  part.display("gel", world.creative() and 1 or clamp((state.gel or 0) / properties.chemical_time, 0, 1))
  part.display("chemicals", creation.count(properties.chemical, tanks()))
end
