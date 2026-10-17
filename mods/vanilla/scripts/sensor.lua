-- Sensor: looks out of its face (the part's +Y) for up to the "range" setting in m, and its output is 1
-- while something it is set to detect is there: the "players", "bots", "blocks" (the ground and the world's
-- objects) and "creations" toggles. It casts a ray straight out and a ring of RING rays properties.cone
-- degrees off it, so it sees a narrow cone; each ray stops at the first thing it hits. It checks every
-- properties.interval steps, and plays its "detect" and "lose" sounds when that changes.

local RING = 6

function on_create()
  state.seen = state.seen or false
end

local function wanted(hit)
  if hit.player then return part.setting("players") ~= false end
  if hit.bot then return part.setting("bots") ~= false end
  if hit.creation then return part.setting("creations") ~= false end
  return part.setting("blocks") ~= false
end

local function look(from, direction, range)
  local hit = world.raycast(from, from + direction * range)
  return hit ~= nil and wanted(hit)
end

function on_update(dt)
  if world.tick() % (properties.interval or 1) ~= 0 then
    return
  end
  local face = part.to_world(vec3(0, 1, 0))
  local side = part.to_world(vec3(1, 0, 0))
  local other = part.to_world(vec3(0, 0, 1))
  local from = part.position()
  local range = part.setting("range") or 4
  local seen = look(from, face, range)
  local cone = math.rad(properties.cone or 0)
  if not seen and cone > 0 then
    local c, s = math.cos(cone), math.sin(cone)
    for i = 0, RING - 1 do
      local a = i * 2 * math.pi / RING
      local ring = side * math.cos(a) + other * math.sin(a)
      if look(from, face * c + ring * s, range) then
        seen = true
        break
      end
    end
  end
  if seen ~= state.seen then
    local key = seen and part.sound("detect") or part.sound("lose")
    if key then sound.play(key) end
  end
  state.seen = seen
end

function get_outputs()
  return state.seen
end
