-- Engine: turns the wheel bearings its output is linked to. Its input is a throttle from -1 (back) to 1
-- (forward), usually a driver's seat. Wheels roll toward the engine's front, so place it facing the way the
-- car should drive. With no throttle it holds the wheels with properties.brake N·m.
-- Properties: "speed" (rad/s at the top gear), "torque" (N·m), "brake" (N·m), "fuel" (item id) and
-- "fuel_time" (seconds of full throttle per item). In Creative it needs no fuel.
-- Setting "gear" (shown as Power, 1 to properties.gears, default 5): each step turns that share of the top speed. Setting "on": with
-- nothing linked into it, the engine runs forward at full throttle while it is on.
-- Its menu shows "fuel" (how much of the current item is left, 0 to 1; full in Creative), "fuel_items"
-- (items left in the Creation), "rpm" (the first wheel's speed) and "torque" (N·m it drives with). Its
-- "running" display is on while it drives, and each wheel bearing's "drive" display turns with it.
-- Sounds: "start" when it starts, the "loop" while it runs (its properties.loop_parameter follows the
-- throttle), and "stop" when it runs out of fuel.
--
-- With properties.ignition the engine has to be switched on (the "on" setting, also when linked): it cranks for
-- properties.start_time s (now and then a false start of properties.false_start_time s first, at the
-- properties.false_start chance), then idles and drives while its input asks; switched off or out of fuel it winds
-- down for properties.stop_time s. Its speed wobbles by properties.uneven of itself, it backfires now and then
-- (properties.backfire a second) and a power change rattles it for properties.gear_time s. It shows what it does
-- for its model: "clip" (off_loop, false_start, start, run_loop, run_hard_loop from properties.hard_gear up, gear,
-- stop), "lever" (the power setting, 0 to 1), "heat" (how hard it works, 0 to 1, easing over properties.heat_time s),
-- "smoke", "sparks" and "oil" (0 to 1). Sounds "sputter" (false starts, backfires) and "rev" (power changes) when bound.

local RESEND = 30
local SHOW_EVERY = 10

local function clamp(v)
  return math.max(-1, math.min(1, v))
end

-- +1 when a positive velocity on the bearing rolls its wheel toward the engine's front, -1 when it rolls it
-- back (the other way round on a bearing the player reversed), nil when the link does not go to a bearing.
local function direction(id)
  local joint = creation.joint(id)
  if not joint or joint.type ~= "rotational" or not joint.axis then
    return nil
  end
  local forward = part.to_world(vec3(0, 0, -1))
  local up = part.to_world(vec3(0, 1, 0))
  local roll = joint.axis:cross(up):dot(forward)
  if math.abs(roll) < 0.5 then
    return nil -- it turns about an upright or lengthwise axis: a steering or other bearing, not a wheel
  end
  local sign = roll > 0 and 1 or -1
  return joint.reversed and -sign or sign
end

-- Burns fuel for dt seconds at this throttle; false when the engine is out of fuel.
local function burn(throttle, dt)
  if world.creative() or throttle == 0 then
    return true
  end
  state.fuel = (state.fuel or 0) - math.abs(throttle) * dt
  if state.fuel > 0 then
    return true
  end
  if creation.take(properties.fuel, 1) > 0 then
    state.fuel = state.fuel + properties.fuel_time
    return true
  end
  state.fuel = 0
  return false
end

local function drive(throttle)
  local gear = part.setting("gear") or 1
  local speed = throttle * properties.speed * gear / (properties.gears or 5)
  local resend = world.tick() % RESEND == 0
  local sent = state.sent
  for _, id in ipairs(part.links()) do
    local sign = direction(id)
    if sign then
      local velocity, torque = sign * speed, properties.torque
      if throttle == 0 then
        torque = properties.brake
      end
      local key = tostring(id)
      if resend or sent[key] ~= velocity or state.torque ~= torque then
        creation.set_bearing(id, velocity, torque)
        creation.display(id, "drive", velocity ~= 0 and velocity or nil)
        sent[key] = velocity
      end
    end
  end
  state.torque = throttle == 0 and properties.brake or properties.torque
end

function on_create()
  state.angles = {}
  state.sent = state.sent or {}
  state.fuel = state.fuel or 0
  if properties.ignition then
    state.phase = "off"
    part.display("clip", "off_loop")
  end
end

function on_reload()
  state.angles = {}
  state.sent = {}
end

-- Starts the running loop on the first throttle, follows the throttle with it, and stops it on empty.
local function hum(throttle, fueled)
  if not fueled then
    if state.hum then
      sound.stop(state.hum)
      state.hum = nil
      if part.sound("stop") then sound.play(part.sound("stop")) end
    end
    return
  end
  if not state.hum and throttle ~= 0 and part.sound("loop") then
    if part.sound("start") then sound.play(part.sound("start")) end
    state.hum = sound.start(part.sound("loop"))
  end
  if state.hum and properties.loop_parameter then
    sound.set_parameter(state.hum, properties.loop_parameter, math.abs(throttle))
  end
end

-- What the menu shows, every SHOW_EVERY steps: fuel, items left, wheel speed and torque.
local function show(throttle, dt)
  state.shown = (state.shown or 0) + dt
  if world.tick() % SHOW_EVERY ~= 0 then
    return
  end
  local elapsed = state.shown
  state.shown = 0
  part.display("running", throttle ~= 0)
  part.display("fuel", world.creative() and 1 or math.max(0, math.min(1, (state.fuel or 0) / properties.fuel_time)))
  part.display("fuel_items", creation.count(properties.fuel))
  part.display("torque", throttle ~= 0 and properties.torque or 0)
  local rpm = 0
  for _, id in ipairs(part.links()) do
    if direction(id) then
      local joint = creation.joint(id)
      local angle = joint and joint.position
      local last = state.angles[tostring(id)]
      if angle and last and elapsed > 0 then
        local turned = (angle - last + math.pi) % (2 * math.pi) - math.pi
        rpm = math.abs(turned) / elapsed * 60 / (2 * math.pi)
      end
      state.angles[tostring(id)] = angle
      break
    end
  end
  part.display("rpm", math.floor(rpm + 0.5))
end

-- A repeatable 0..1 number for this engine at this step (no randomness, so every replay agrees).
local function roll(salt)
  local x = (world.tick() * 7919 + part.id() * 104729 + salt * 1299709) % 2147483647
  x = (x * 48271) % 2147483647
  return x / 2147483647
end

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

local function clip(name)
  state.clips = (state.clips or 0) + 1
  part.display("clip", name .. "#" .. state.clips)
end

-- 1 for `seconds` from now, then fading over as long again: a puff.
local function pulse(key, seconds)
  state.pulses = state.pulses or {}
  state.pulses[key] = seconds
end

local function pulsed(key, dt)
  local left = state.pulses and state.pulses[key]
  if not left then return 0 end
  left = left - dt
  state.pulses[key] = left > -0.3 and left or nil
  return left > 0 and 1 or math.max(0, 1 + left / 0.3)
end

local function fuel_left()
  return world.creative() or (state.fuel or 0) > 0 or creation.count(properties.fuel) > 0
end

local function release()
  for _, id in ipairs(part.links()) do
    if direction(id) and state.sent[tostring(id)] ~= nil then
      creation.set_bearing(id, nil)
      creation.display(id, "drive", nil)
      state.sent[tostring(id)] = nil
    end
  end
end

local function stop_hum()
  if state.hum then
    sound.stop(state.hum)
    state.hum = nil
  end
end

-- The ignition engine's steps: off, starting (with a false start first now and then), running, stopping.
local function ignition(dt)
  local on = part.setting("on") and true or false
  local gear = part.setting("gear") or 1
  local top = properties.gears or 5
  part.display("lever", top > 1 and (gear - 1) / (top - 1) or 0)
  state.timer = (state.timer or 0) - dt
  local phase = state.phase or "off"
  local throttle = 0
  if phase == "off" then
    if on and not state.failed then
      if not fuel_left() then
        state.phase, state.timer, state.failed = "false", properties.false_start_time, true
        clip("false_start")
        play("sputter")
        pulse("smoke", 0.2)
      elseif roll(1) < (properties.false_start or 0) and not state.tried then
        state.phase, state.timer, state.tried = "false", properties.false_start_time, true
        clip("false_start")
        play("sputter")
        pulse("smoke", 0.2)
      else
        state.phase, state.timer = "starting", properties.start_time
        clip("start")
        play("start")
        pulse("smoke", 0.4)
        pulse("oil", 0.3)
      end
    elseif not on then
      state.failed, state.tried = nil, nil
    end
  elseif phase == "false" then
    if state.timer <= 0 then
      state.phase = "off"
      clip("off_loop")
    end
  elseif phase == "starting" then
    if not on then
      state.phase, state.timer = "stopping", properties.stop_time
      clip("stop")
    elseif state.timer <= 0 then
      state.phase, state.gear = "running", gear
      clip(gear >= (properties.hard_gear or top + 1) and "run_hard_loop" or "run_loop")
      if part.sound("loop") then state.hum = sound.start(part.sound("loop")) end
    end
  elseif phase == "running" then
    throttle = clamp(part.input())
    if #part.inputs() == 0 then
      throttle = 1
    end
    if not burn(throttle, dt) or not on then
      throttle = 0
      state.phase, state.timer = "stopping", properties.stop_time
      clip("stop")
      stop_hum()
      play("stop")
      pulse("smoke", 0.3)
      pulse("oil", 0.4)
    elseif gear ~= state.gear then
      state.gear, state.timer = gear, properties.gear_time
      clip("gear")
      play("rev")
      pulse("smoke", properties.gear_time)
      state.after_gear = true
    elseif state.after_gear and state.timer <= 0 then
      state.after_gear = nil
      clip(gear >= (properties.hard_gear or top + 1) and "run_hard_loop" or "run_loop")
    elseif roll(2) < (properties.backfire or 0) * dt then
      play("sputter")
      pulse("sparks", 0.2)
      pulse("smoke", 0.15)
    end
  elseif phase == "stopping" then
    if state.timer <= 0 then
      state.phase = "off"
      clip("off_loop")
    end
  end
  local running = state.phase == "running"
  if running then
    -- uneven speed: the throttle wobbles a little, settled every few steps so the bearings are not resent each one
    if world.tick() % 6 == 0 then
      state.wobble = 1 + (properties.uneven or 0) * (roll(3) * 2 - 1)
    end
    drive(throttle * (state.wobble or 1))
    if state.hum and properties.loop_parameter then
      sound.set_parameter(state.hum, properties.loop_parameter, math.abs(throttle))
    end
  else
    stop_hum()
    release()
  end
  state.throttle = running and throttle or 0
  local power = running and (0.3 + 0.7 * math.abs(throttle) * gear / top) or 0
  -- heat follows the power setting (and a little the throttle), easing in and out, in steps of 0.05 so it syncs seldom
  local want = running and (gear / top) * (0.6 + 0.4 * math.abs(throttle)) or 0
  local step = dt / (properties.heat_time or 0.4)
  local heat = state.heat or 0
  state.heat = heat < want and math.min(want, heat + step) or math.max(want, heat - step)
  part.display("heat", math.floor(state.heat * 20 + 0.5) / 20)
  part.display("smoke", math.max(power * (properties.smoke or 1), pulsed("smoke", dt)))
  part.display("sparks", math.max(running and math.abs(throttle) * gear / top > 0.7 and 0.3 or 0, pulsed("sparks", dt)))
  part.display("oil", math.max(0.15, pulsed("oil", dt)))
  show(state.throttle, dt)
  part.display("running", running)
end

function on_update(dt)
  if properties.ignition then
    ignition(dt)
    return
  end
  local throttle = clamp(part.input())
  if #part.inputs() == 0 then
    throttle = part.setting("on") and 1 or 0
  end
  local fueled = burn(throttle, dt)
  if not fueled then
    throttle = 0
  end
  state.throttle = throttle
  drive(throttle)
  hum(throttle, fueled)
  show(throttle, dt)
end

function get_outputs()
  return state.throttle or 0
end

function on_destroy()
  for _, id in ipairs(part.links() or {}) do
    if direction(id) then
      creation.set_bearing(id, nil)
      creation.display(id, "drive", nil)
    end
  end
end
