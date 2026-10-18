-- Driver's seat: the sitting player's throttle (W/S, -1 to 1) is the seat's output, so link it to engines.
-- Linked bearings that turn about an upright axis steer: A/D turns them up to properties.steer_angle
-- degrees, at properties.steer_speed rad/s with at most properties.steer_torque N·m. When the seat is empty
-- the output is 0 and the wheels straighten. A steering bearing's "drive" display turns while it steers.
-- Its "seated" display is 1 while someone sits in it, and "rattle" is 1 while they do and an engine of the
-- Creation shows "running" (a seat whose model has clips sags, settles and rattles with them). A seat that binds a
-- "creak" sound creaks now and then while someone sits in it, twice as often while an engine runs.

local RESEND = 30
local ENGINE_EVERY = 10

local function steering(id, up)
  local joint = creation.joint(id)
  if not joint or joint.type ~= "rotational" or not joint.axis then
    return nil
  end
  local upright = joint.axis:dot(up)
  if math.abs(upright) < 0.7 then
    return nil
  end
  local sign = upright > 0 and 1 or -1
  return joint.reversed and -sign or sign
end

local function steer(x)
  local up = part.to_world(vec3(0, 1, 0))
  local resend = world.tick() % RESEND == 0
  for _, id in ipairs(part.links()) do
    local sign = steering(id, up)
    if sign then
      -- Steering right turns the wheels clockwise seen from above: a negative angle about up.
      local angle = -x * math.rad(properties.steer_angle) * sign
      local key = tostring(id)
      if resend or state.steer[key] ~= angle then
        creation.set_bearing_angle(id, angle, properties.steer_speed, properties.steer_torque)
        creation.display(id, "drive", x ~= 0 and -x * sign or nil)
        state.steer[key] = angle
      end
    end
  end
end

-- True when any part of the Creation shows its "running" display (an engine that drives).
local function engines_running()
  for _, p in ipairs(creation.parts()) do
    local running = creation.displayed(p.id, "running")
    if running and running ~= 0 then
      return true
    end
  end
  return false
end

-- Seconds to the next creak: 6 to 12, varied by the seat and the count so far, halved while the engines run.
local function creak_gap()
  state.creaks = (state.creaks or 0) + 1
  local spread = ((part.id() * 7919 + state.creaks * 104729) % 100) / 100
  return (6 + 6 * spread) * (state.engines and 0.5 or 1)
end

local function creak()
  local id = part.sound("creak")
  if not id then
    return
  end
  if not state.next_creak then
    state.next_creak = world.time() + creak_gap()
  elseif world.time() >= state.next_creak then
    sound.play(id)
    state.next_creak = world.time() + creak_gap()
  end
end

local function show(id, on)
  if state.shown[id] ~= on then
    part.display(id, on)
    state.shown[id] = on
  end
end

function on_create()
  state.steer = state.steer or {}
  state.shown = {}
  state.throttle = 0
end

function on_reload()
  state.steer = {}
  state.shown = {}
end

function on_update(dt)
  local input = part.occupant_input()
  local x, z = 0, 0
  if input then
    x, z = input.x, input.z
  end
  state.throttle = math.max(-1, math.min(1, z))
  steer(math.max(-1, math.min(1, x)))
  local seated = part.occupant() ~= nil
  show("seated", seated)
  if not seated then
    state.engines = false
    state.next_creak = nil
  else
    if world.tick() % ENGINE_EVERY == 0 then
      state.engines = engines_running()
    end
    creak()
  end
  show("rattle", seated and state.engines == true)
end

function get_outputs()
  return state.throttle
end
