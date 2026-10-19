-- The Drill (Drill spec, 2026-10-18). Like the Saw Blade it has no input of its own: a bearing (driven by an engine or a
-- controller) spins it, and it drills only while it really turns about its axis, the part's up (tip end), at
-- properties.min_spin rad/s or more. Every properties.interval seconds at that speed it bites whatever its bit touches:
-- rock deposits (only a drill breaks them), the boulders and chunks they break into, parts, bots and players.
--
-- Its rays start inside the bit and run out past it, so they skip the drill itself and still find what is pressed
-- against it: properties.tip = {from, reach} casts one ray straight ahead from `from` m up the axis, and
-- properties.flank = {at, from, reach, rays, lean} casts `rays` rays round the bit from `from` m off the axis at `at` m up
-- it, leaning `lean` of the way forward (0 straight out, 1 straight ahead), so a rock it is pushed into sideways bites too.
-- A hit on the drill's own Creation is skipped, and each thing is bitten once per turn however many rays find it.
--
-- Each bite deals properties.damage, or properties.damage_to[type] for what it hit (a part's qualified type, or a world
-- object's kind). properties.kind ("drill") is the damage kind. Under load it shakes its Body with properties.shake N
-- (more the faster it turns), its base ring glows with the spin up to drilling speed (the "spin" readout, 0 to 1), and
-- its whine rises with it.

local loop
local cached

function on_create()
  state.next_cut = state.next_cut or 0
  state.shake = state.shake or 0
end

-- How fast the drill turns about its own axis, in rad/s.
local function spin_rate()
  return math.abs(part.angular_velocity():dot(part.to_world(vec3(0, 1, 0))))
end

local function whine(rate)
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

-- Every ray of the drill, in its own frame: {from, direction, reach}.
local function rays()
  if cached then
    return cached
  end
  local tip, flank = properties.tip, properties.flank
  local list = { { from = vec3(0, tip.from, 0), direction = vec3(0, 1, 0), reach = tip.reach } }
  for i = 0, flank.rays - 1 do
    local a = 2 * math.pi * i / flank.rays
    local out = vec3(math.cos(a), 0, math.sin(a))
    local direction = (out * (1 - flank.lean) + vec3(0, flank.lean, 0)):normalized()
    list[#list + 1] = { from = out * flank.from + vec3(0, flank.at, 0), direction = direction, reach = flank.reach }
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

-- A small sideways kick to the drill's Body, turning round the axis each bite, harder the faster it spins.
local function shake(rate)
  local force = (properties.shake or 0) * math.min(2, rate / properties.min_spin)
  if force <= 0 then
    return
  end
  state.shake = (state.shake + 2.4) % (2 * math.pi)
  part.apply_force(part.to_world(vec3(math.cos(state.shake), 0, math.sin(state.shake))) * force)
end

function on_update(dt)
  local rate = spin_rate()
  whine(rate)
  if rate < properties.min_spin or world.time() < state.next_cut then
    if loop and world.time() >= state.next_cut then
      sound.set_parameter(loop, "load", 0)
    end
    return
  end
  state.next_cut = world.time() + properties.interval
  local cut, bot = bite()
  if loop then
    sound.set_parameter(loop, "load", (cut or bot) and 1 or 0)
  end
  if cut or bot then
    shake(rate)
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
