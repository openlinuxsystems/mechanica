-- Saw blades and drills (chunk 23). While its input is on, the part spins and, every properties.interval
-- seconds, cuts whatever is within properties.reach metres of its cutting edge: parts, bots, players
-- and world objects such as trees and rocks, for properties.damage each. Its edge is a list of rays in
-- the part's frame, properties.edge = { {from = {x, y, z}, direction = {x, y, z}}, ... }, each starting
-- just outside the part so it never cuts its own creation.

local loop

function on_create()
  state.next_cut = state.next_cut or 0
end

local function spin(on)
  if on and not loop then
    local id = part.sound("spin")
    if id then
      loop = sound.start(id, { load = 0 })
    end
  elseif not on and loop then
    sound.stop(loop)
    loop = nil
  end
end

function on_update(dt)
  local on = part.input() > 0
  spin(on)
  if not on or world.time() < state.next_cut then
    return
  end
  state.next_cut = world.time() + properties.interval
  local cut = false
  for _, ray in ipairs(properties.edge) do
    local from = part.point_to_world(vec3(ray.from[1], ray.from[2], ray.from[3]))
    local direction = part.to_world(vec3(ray.direction[1], ray.direction[2], ray.direction[3]))
    local hit = combat.cast(from, direction, properties.reach)
    if hit then
      combat.hit(hit, properties.damage)
      cut = true
    end
  end
  if loop then
    sound.set_parameter(loop, "load", cut and 1 or 0)
  end
  if cut then
    local id = part.sound("hit")
    if id then
      sound.play(id)
    end
  end
end

function on_destroy()
  spin(false)
end
