-- Driver's seat: the sitting player's throttle (W/S, -1 to 1) is the seat's output, so link it to engines.
-- Linked bearings that turn about an upright axis steer: A/D turns them up to properties.steer_angle
-- degrees, at properties.steer_speed rad/s with at most properties.steer_torque N·m. When the seat is empty
-- the output is 0 and the wheels straighten. A steering bearing's "drive" display turns while it steers. A seat with
-- no properties.steer_angle is a passenger seat: it drives nothing (its output stays 0) and steers nothing, but its
-- links still put switches and buttons on the rider's hotbar, and it shows "passenger" (1) so an engine linked to it
-- does not start when someone sits in it.
-- A driving seat shows how far its wheel is turned as "wheel" (0 full left, 0.5 straight, 1 full right) and its pedals
-- as "gas" and "brake" (0 to 1): its model turns its wheel and presses its pedals by them, and the driver's hands and
-- feet follow.
-- Its "seated" display is 1 while someone sits in it, and "rattle" is 1 while they do and an engine of the
-- Creation shows "running". Its "clip" display names the model clip to play (part data display.clips): "enter" as
-- someone sits, then "seated_loop", or "seated_rattle_loop" while an engine runs, and "exit" as they get up; a model
-- without those clips ignores it. ENTER_TIME is how long "enter" plays before the loop takes over. A seat that binds a
-- "creak" sound creaks now and then while someone sits in it, twice as often while an engine runs.
-- A turret seat (properties.aim_speed, and an aimed socket: part.aim_limits) turns its shell toward where the seated
-- player looks: yaw about its upright axis and pitch about its side axis, within the limits of its level, at up to
-- properties.aim_speed rad/s about each with at most properties.aim_torque N·m. Whatever is built on its top turns
-- with the shell (part.set_aim drives the turret's joint; the host does it for everyone). It shows how far the shell
-- has turned as "aim_yaw" and "aim_pitch" (rad), which every peer draws the shell and its rider at while nothing is
-- built on it. An empty turret holds where it was left.

local RESEND = 30
local ENGINE_EVERY = 10
local ENTER_TIME = 0.8

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

-- The yaw and pitch (rad, in the seat's own frame: yaw about +Y from its front -Z, pitch up about +X) that point the
-- shell where the seated player looks, within the turret's limits.
local function look_angles(input, limits)
  local cp = math.cos(input.pitch)
  local d = part.to_local(vec3(-math.sin(input.yaw) * cp, math.sin(input.pitch), -math.cos(input.yaw) * cp))
  local yaw = math.atan2(-d.x, -d.z)
  local pitch = math.atan2(d.y, math.sqrt(d.x * d.x + d.z * d.z))
  if limits.min then
    yaw = math.max(limits.min, math.min(limits.max, yaw))
  end
  return yaw, math.max(limits.pitch_min, math.min(limits.pitch_max, pitch))
end

-- One step from `from` toward `to`, at most `step`; `wrap` goes the short way round a circle.
local function toward(from, to, step, wrap)
  local d = to - from
  if wrap then
    d = (d + math.pi) % (2 * math.pi) - math.pi
  end
  if math.abs(d) <= step then
    return to
  end
  local next = from + (d > 0 and step or -step)
  if wrap and next > math.pi then
    next = next - 2 * math.pi
  elseif wrap and next <= -math.pi then
    next = next + 2 * math.pi
  end
  return next
end

local function aim(input, dt)
  local limits = part.aim_limits()
  if not limits then
    return
  end
  local a = state.aim
  local joint = part.joint()
  local live = joint and joint.type == "aimed" and not joint.locked
  -- Built on and loaded (or just placed on): start from where the turret stands.
  if live and not a.synced and joint.position and joint.pitch then
    a.yaw, a.pitch, a.yaw_target, a.pitch_target = joint.position, joint.pitch, joint.position, joint.pitch
  end
  a.synced = live and joint.position ~= nil
  if input then
    a.yaw_target, a.pitch_target = look_angles(input, limits)
  end
  local step = properties.aim_speed * dt
  a.yaw = toward(a.yaw, a.yaw_target, step, limits.min == nil)
  a.pitch = toward(a.pitch, a.pitch_target, step, false)
  local shown_yaw = math.floor(a.yaw * 1000 + 0.5) / 1000
  local shown_pitch = math.floor(a.pitch * 1000 + 0.5) / 1000
  show("aim_yaw", shown_yaw)
  show("aim_pitch", shown_pitch)
  if live then
    local key = a.yaw_target .. ":" .. a.pitch_target
    if key ~= a.sent or world.tick() % RESEND == 0 then
      -- Refused (the Creation is not moving yet): try again next step.
      a.sent = part.set_aim(a.yaw_target, a.pitch_target, properties.aim_speed, properties.aim_torque) and key or nil
    end
  else
    a.sent = nil
  end
end

-- Plays a model clip; the count after "#" restarts a clip shown again.
local function clip(name)
  state.clips = (state.clips or 0) + 1
  state.clip = name
  part.display("clip", name .. "#" .. state.clips)
end

local function clips(seated)
  local was = state.shown.seated
  if seated and not was then
    clip("enter")
    state.settled = world.time() + ENTER_TIME
  elseif was and not seated then
    clip("exit")
  elseif seated and world.time() >= state.settled then
    local loop = state.engines and "seated_rattle_loop" or "seated_loop"
    if state.clip ~= loop then
      clip(loop)
    end
  end
end

function on_create()
  state.steer = state.steer or {}
  state.shown = {}
  state.throttle = 0
  state.aim = state.aim or { yaw = 0, pitch = 0, yaw_target = 0, pitch_target = 0 }
  -- Loaded from a save: aim the turret again where it was left straight away.
  state.aim.sent = nil
end

function on_reload()
  state.steer = {}
  state.shown = {}
  state.aim = state.aim or { yaw = 0, pitch = 0, yaw_target = 0, pitch_target = 0 }
  state.aim.sent = nil
end

function on_update(dt)
  local input = part.occupant_input()
  local x, z = 0, 0
  if input then
    x, z = input.x, input.z
  end
  if properties.steer_angle then
    state.throttle = math.max(-1, math.min(1, z))
    steer(math.max(-1, math.min(1, x)))
    -- The wheel and pedals: the model turns and presses them (display.clips.scrub) and the driver's hands and feet follow.
    show("wheel", math.floor((math.max(-1, math.min(1, x)) + 1) * 50 + 0.5) / 100)
    show("gas", math.floor(math.max(0, state.throttle) * 100 + 0.5) / 100)
    show("brake", math.floor(math.max(0, -state.throttle) * 100 + 0.5) / 100)
  end
  if properties.aim_speed then
    aim(input, dt)
  end
  show("passenger", not properties.steer_angle)
  local seated = part.occupant() ~= nil
  clips(seated)
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
