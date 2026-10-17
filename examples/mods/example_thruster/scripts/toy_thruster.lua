-- Toy Thruster (the Aero Tutorial's example thruster). It runs on the host only, once per physics step,
-- and pushes the creation it is on. The game does the rest: the push goes through the host's Action API,
-- the physics moves the body, and every client sees the result.

local function clamp(v, lo, hi)
  return math.max(lo, math.min(hi, v))
end

-- The jet's roar, louder with more power ("thrust" is a parameter of the vanilla loop).
local function loop(power)
  local id = part.sound("loop")
  if not id then return end
  if power > 0 and not state.loop then
    state.loop = sound.start(id, { thrust = power })
  elseif power == 0 and state.loop then
    sound.stop(state.loop)
    state.loop = nil
  elseif state.loop then
    sound.set_parameter(state.loop, "thrust", power)
  end
end

function on_update(dt)
  -- The input is the sum of the outputs of every part linked to it. Never trust it to be small: a player
  -- can link ten switches. properties.max_power caps it.
  local power = clamp(part.input(), 0, properties.max_power)
  loop(power)
  if power == 0 then return end
  -- part.to_world turns a direction in the part's own frame into the world's. The base socket is on the
  -- part's -Y side, so -Y points from the nozzle into the creation: that is the way it pushes.
  local direction = part.to_world(vec3(0, -1, 0))
  -- A force in newtons at a world point: pushing at the part, not the centre of mass, also turns the
  -- creation when the thruster is off-centre. F9 shows the push as an orange arrow.
  part.apply_force(direction * (properties.thrust * power), part.position())
end
