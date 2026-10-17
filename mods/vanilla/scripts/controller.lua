-- Controller: while its input is on, steps the bearings its output is linked to through the "frames"
-- setting (angles in degrees, up to 10), one frame every "frame_time" seconds, and starts over when the
-- "loop" setting is on. Every linked bearing goes to the same angle. When the input goes off it stops
-- where it is. Its output is 1 while it runs.
-- Properties: "speed" (rad/s) and "torque" (N·m) for the bearings. Sounds: "start", "tick" on each frame
-- and "stop".

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

local function go(frame)
  local angle = math.rad(frame)
  for _, id in ipairs(part.links()) do
    local joint = creation.joint(id)
    if joint and joint.type == "rotational" then
      creation.set_bearing_angle(id, angle, properties.speed, properties.torque)
    end
  end
end

function on_create()
  state.index = state.index or 0
  state.running = state.running or false
  state.next = state.next or 0
end

function on_power(amount)
  local running = amount ~= 0
  if running ~= state.running then
    play(running and "start" or "stop")
  end
  if running and not state.running then
    state.next = world.time()
    if state.index >= #(part.setting("frames") or {}) then
      state.index = 0
    end
  end
  state.running = running
end

function on_update(dt)
  if not state.running or world.time() + 1e-9 < state.next then
    return
  end
  local frames = part.setting("frames") or {}
  if #frames == 0 then
    return
  end
  if state.index >= #frames then
    if not part.setting("loop") then
      state.running = false
      play("stop")
      return
    end
    state.index = 0
  end
  state.index = state.index + 1
  go(frames[state.index])
  play("tick")
  state.next = world.time() + (part.setting("frame_time") or 1)
end

function get_outputs()
  return state.running
end
