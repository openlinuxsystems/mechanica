-- The Saw Blade (Saw Blade spec, 2026-10-18). It has no input of its own: a bearing (driven by an engine or a
-- controller) spins it, and it cuts only while it really turns about its axis, the part's up, at properties.min_spin
-- rad/s or more. Every properties.interval seconds at that speed it bites whatever its teeth or its flat faces touch:
-- parts, bots, players and world objects such as trees.
--
-- Its rays start inside the disc and run out past it, so they skip the blade itself and still find what is pressed
-- against it: properties.rim = {from, reach, rays} casts `rays` rays outward in the disc's plane from `from` m off the
-- axis, and properties.faces = {from, reach, rays} casts `rays` rays each way along the axis from a ring `from` m out
-- (a face pressed flat against a log cuts too). A hit on the saw's own Creation is skipped.
--
-- Each bite deals properties.damage, or properties.damage_to[type] for what it hit (a part's qualified type, or a world
-- object's kind), so a felled log cuts faster than a standing tree. properties.kind ("saw") is the damage kind: only a
-- saw fells a Pine. Its hub glow follows the spin up to cutting speed (the "spin" readout, 0 to 1), and its whir rises
-- with it.

local loop
local cached

function on_create()
  state.next_cut = state.next_cut or 0
end

-- How fast the blade turns about its own axis, in rad/s.
local function spin_rate()
  return math.abs(part.angular_velocity():dot(part.to_world(vec3(0, 1, 0))))
end

local function whir(rate)
  local share = math.min(1, rate / properties.min_spin)
  if share > 0.1 and not loop then
    local id = part.sound("spin")
    if id then
      loop = sound.start(id, { speed = share, load = 0 })
    end
  elseif share <= 0.1 and loop then
    sound.stop(loop)
    loop = nil
  end
  if loop then
    sound.set_parameter(loop, "speed", share)
  end
  -- in quarter steps so it syncs seldom
  part.display("spin", math.floor(share * 4) / 4)
end

-- Every ray of the blade, in its own frame: {from, direction, reach}.
local function rays()
  if cached then
    return cached
  end
  local list = {}
  local rim, faces = properties.rim, properties.faces
  for i = 0, rim.rays - 1 do
    local a = 2 * math.pi * i / rim.rays
    local d = vec3(math.cos(a), 0, math.sin(a))
    list[#list + 1] = { from = d * rim.from, direction = d, reach = rim.reach }
  end
  for i = 0, faces.rays - 1 do
    local a = 2 * math.pi * (i + 0.5) / faces.rays
    local at = vec3(math.cos(a), 0, math.sin(a)) * faces.from
    list[#list + 1] = { from = at, direction = vec3(0, 1, 0), reach = faces.reach }
    list[#list + 1] = { from = at, direction = vec3(0, -1, 0), reach = faces.reach }
  end
  cached = list
  return list
end

local function bite()
  local own = part.creation()
  local cut, bot = false, false
  local bitten = {}
  for _, ray in ipairs(rays()) do
    local hit = combat.cast(part.point_to_world(ray.from), part.to_world(ray.direction), ray.reach)
    -- one bite per thing per turn, however many rays find it
    local key = hit and (hit.object or hit.bot and ("b" .. hit.bot) or hit.player and ("p" .. hit.player)
      or hit.part and (hit.creation .. ":" .. hit.part))
    if hit and hit.creation ~= own and key and not bitten[key] then
      bitten[key] = true
      local damage = (properties.damage_to or {})[hit.type or ""] or properties.damage
      local done = combat.hit(hit, damage, nil, properties.kind)
      if done.damage > 0 then
        if hit.kind == "bot" then bot = true else cut = true end
      end
    end
  end
  return cut, bot
end

function on_update(dt)
  local rate = spin_rate()
  whir(rate)
  if rate < properties.min_spin or world.time() < state.next_cut then
    if loop then
      sound.set_parameter(loop, "load", 0)
    end
    return
  end
  state.next_cut = world.time() + properties.interval
  local cut, bot = bite()
  if loop then
    sound.set_parameter(loop, "load", (cut or bot) and 1 or 0)
  end
  local key = bot and "bot" or cut and "hit" or nil
  local id = key and part.sound(key)
  if id then
    sound.play(id)
  end
end

function on_destroy()
  if loop then
    sound.stop(loop)
    loop = nil
  end
end
