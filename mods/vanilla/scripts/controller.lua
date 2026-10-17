-- Controller: while its input is on, steps the bearings and pistons its output is linked to through the
-- "frames" setting (up to 10), one frame every "frame_time" seconds, and starts over when the "loop" setting
-- is on. Each frame gives every joint a target (degrees for a bearing, the opposite on a reversed one; m of
-- extension for a piston) and a speed (rad/s or m/s). An older frame that is a single angle turns every
-- linked bearing to it at properties.speed. When the input goes off it stops where it is. Its output is 1
-- while it runs. It sends the current frame again every RESEND steps, so a piston it drives holds the
-- controller's target rather than the one its own input gives it.
-- The menu's Play button previews frames that are not applied yet: on_command("preview", {playing, frames})
-- runs them in a loop, input or not, until Pause.
-- Properties: "speed" (rad/s, for single-angle frames), "torque" (N·m) for the bearings and "force" (N) for
-- the pistons. Sounds: "start", "tick" on each frame and "stop".

local RESEND = 30

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

local function go(frame)
  if type(frame) == "number" then
    local angle = math.rad(frame)
    for _, id in ipairs(part.links()) do
      local joint = creation.joint(id)
      if joint and joint.type == "rotational" then
        creation.set_bearing_angle(id, joint.reversed and -angle or angle, properties.speed, properties.torque)
      end
    end
    return
  end
  for _, step in ipairs(frame or {}) do
    local joint = creation.joint(step.part)
    if joint and joint.type == "rotational" then
      local angle = math.rad(step.target)
      creation.set_bearing_angle(step.part, joint.reversed and -angle or angle, step.speed, properties.torque)
    elseif joint and joint.type == "linear" then
      creation.set_piston(step.part, step.target, step.speed, properties.force)
    end
  end
end

-- The frames running now: the preview's while it plays, else the setting.
local function frames()
  return state.preview or part.setting("frames") or {}
end

function on_create()
  state.index = state.index or 0
  state.running = state.running or false
  state.next = state.next or 0
end

function on_reload()
  state.assert = true
end

function on_power(amount)
  local running = amount ~= 0
  if running ~= state.running and not state.preview then
    play(running and "start" or "stop")
  end
  if running and not state.running then
    state.next = world.time()
    if state.index >= #frames() then
      state.index = 0
    end
  end
  state.running = running
end

function on_command(name, args, peer)
  if name ~= "preview" then return end
  if args and args.playing and args.frames and #args.frames > 0 then
    state.preview = args.frames
    state.index = 0
    state.next = world.time()
  else
    state.preview = nil
    state.index = 0
  end
end

function on_update(dt)
  local list = frames()
  local active = state.running or state.preview ~= nil
  -- Hold the current frame against anything else driving the joints.
  if active and state.index > 0 and list[state.index] and (state.assert or world.tick() % RESEND == 0) then
    go(list[state.index])
  end
  state.assert = false
  if not active or world.time() + 1e-9 < state.next or #list == 0 then
    return
  end
  if state.index >= #list then
    if not part.setting("loop") and not state.preview then
      state.running = false
      play("stop")
      return
    end
    state.index = 0
  end
  state.index = state.index + 1
  go(list[state.index])
  play("tick")
  -- Re-send it next step too: a piston's own input may answer this step's change.
  state.assert = true
  state.next = world.time() + (part.setting("frame_time") or 1)
end

function get_outputs()
  return state.running
end
