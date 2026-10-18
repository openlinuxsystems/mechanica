-- Saw blades and drills (chunk 23). While its input is on, the part spins and, every properties.interval
-- seconds, cuts whatever is within properties.reach metres of its cutting edge: parts, bots, players
-- and world objects such as trees and rocks, for properties.damage each. Its edge is a list of rays in
-- the part's frame, properties.edge = { {from = {x, y, z}, direction = {x, y, z}}, ... }, each starting
-- just outside the part so it never cuts its own creation. Its model spins at properties.spin rad/s.
-- properties.kind ("drill" or "saw") is the damage kind it deals: only a spinning drill breaks rock outcrops.
-- It takes properties.spin_up seconds (optional) to reach full spin from rest, in four steps, and runs down
-- as fast when its input goes off; it cuts only while spinning at properties.min_spin rad/s (optional) or
-- more, so a saw has to get up to speed before it bites (Tree Harvesting spec: a minimum RPM).

local loop
local STEPS = 4

function on_create()
  state.next_cut = state.next_cut or 0
  state.speed = state.speed or 0
end

-- The spin rate now: full at once without spin_up, else up or down a quarter of the way each quarter of it.
local function step_speed(on, dt)
  local full = properties.spin
  local up = properties.spin_up or 0
  local target = on and full or 0
  if up <= 0 then
    return target
  end
  state.ramp = (state.ramp or 0) + (on and dt or -dt) / up
  if state.ramp > 1 then state.ramp = 1 end
  if state.ramp < 0 then state.ramp = 0 end
  return full * math.floor(state.ramp * STEPS + 0.5) / STEPS
end

local function spin(speed)
  if speed > 0 and not loop then
    local id = part.sound("spin")
    if id then
      loop = sound.start(id, { load = 0 })
    end
  elseif speed <= 0 and loop then
    sound.stop(loop)
    loop = nil
  end
  if speed ~= state.speed then
    state.speed = speed
    part.animate(nil, speed)
  end
end

function on_update(dt)
  local on = part.input() > 0
  spin(step_speed(on, dt))
  if state.speed <= 0 or state.speed < (properties.min_spin or 0) or world.time() < state.next_cut then
    return
  end
  state.next_cut = world.time() + properties.interval
  local cut = false
  for _, ray in ipairs(properties.edge) do
    local from = part.point_to_world(vec3(ray.from[1], ray.from[2], ray.from[3]))
    local direction = part.to_world(vec3(ray.direction[1], ray.direction[2], ray.direction[3]))
    local hit = combat.cast(from, direction, properties.reach)
    if hit then
      combat.hit(hit, properties.damage, nil, properties.kind)
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
  spin(0)
end
