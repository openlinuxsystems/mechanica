-- Piston: pushes out to the end of its stroke while its input is on, and pulls back when it is off.
-- Properties: "speed" (m/s) and "force" (N). The stroke is the head joint's max, in the part file.
-- Until something is on its head there is no joint to drive, so it keeps trying each step.
-- Its "extension" display (the bar shown with the Connect Tool) is how far out it is, 0 to 1.

local SHOW_EVERY = 5

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
  if world.tick() % SHOW_EVERY == 0 then
    local joint = part.joint()
    local extension = 0
    if joint and joint.position and joint.max and joint.max > (joint.min or 0) then
      extension = (joint.position - (joint.min or 0)) / (joint.max - (joint.min or 0))
    end
    part.display("extension", math.floor(math.max(0, math.min(1, extension)) * 50 + 0.5) / 50)
  end
end

function on_power(amount)
  state.extended = amount > 0
  move()
end

function on_destroy()
  part.set_piston(nil)
end
