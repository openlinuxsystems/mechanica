-- Piston: pushes out to the end of its stroke while its input is on, and pulls back when it is off.
-- Properties: "speed" (m/s) and "force" (N). The stroke is the head joint's max, in the part file.
-- Until something is on its head there is no joint to drive, so it keeps trying each step.

local function move()
  local joint = part.joint()
  if not joint then
    state.holding = false
    return
  end
  local length = state.extended and joint.max or joint.min
  state.holding = part.set_piston(length, properties.speed, properties.force) == true
end

function on_create()
  state.extended = state.extended or false
  move()
end

function on_reload()
  move()
end

function on_update(dt)
  if not state.holding then
    move()
  end
end

function on_power(amount)
  state.extended = amount > 0
  move()
end

function on_destroy()
  part.set_piston(nil)
end
