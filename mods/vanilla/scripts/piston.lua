-- Piston: pushes out to its "length" setting (in blocks) while its input is on, and pulls back when it is off,
-- at its "speed" setting (blocks/s). Both settings top out at the part's level (range 7 to 15 blocks, speed 2 to
-- 10). Property: "force" (N), which grows with the level. The stroke's end is the head joint's max, in the part file.
-- Until something is on its head there is no joint to drive, so it keeps trying each step.
-- While a Controller (a part with a sequence setting) is linked into it, the Controller drives the joint and the
-- piston leaves it alone; its menu is locked too (menu.lock_when_sequenced).
-- Its "extension" display (the bar shown with the Connect Tool, and its menu's readout) is how far out it is, 0 to 1.
-- Sounds: "extend" and "retract" when it starts moving, "stall" when something holds it short of its target.

local BLOCK = 0.25
local SHOW_EVERY = 5
local STALL_STEPS = 30

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

-- True when a part with a sequence setting (a Controller) is linked into this one.
local function sequenced()
  local types = nil
  for _, input in ipairs(part.inputs()) do
    if not types then
      types = {}
      for _, p in ipairs(creation.parts()) do types[p.id] = p.type end
    end
    local settings = types[input.part] and world.item(types[input.part], "settings")
    for _, setting in ipairs(settings or {}) do
      if setting.type == "sequence" then return true end
    end
  end
  return false
end

-- Where it is heading now: its length setting while extended, else fully in.
local function target(joint)
  if not state.extended then return joint.min or 0 end
  local length = (part.setting("length") or 1) * BLOCK
  return math.max(joint.min or 0, math.min(joint.max, length))
end

local function move()
  local joint = part.joint()
  if not joint then
    state.holding = false
    return
  end
  local speed = (part.setting("speed") or 1) * BLOCK
  state.goal = target(joint)
  state.holding = part.set_piston(state.goal, speed, properties.force) == true
  state.still = 0
end

function on_create()
  state.extended = state.extended or false
  state.controlled = sequenced()
  if not state.controlled then move() end
end

function on_reload()
  state.controlled = sequenced()
  if not state.controlled then move() end
end

function on_update(dt)
  local controlled = state.controlled
  if world.tick() % SHOW_EVERY == 0 then controlled = sequenced() end
  if controlled ~= state.controlled then
    state.controlled = controlled
    state.holding = false
  end
  if not controlled then
    local joint = part.joint()
    -- A changed length or speed setting (or a new level) takes effect at once.
    local wanted = joint and target(joint)
    if not state.holding or (wanted and state.goal and math.abs(wanted - state.goal) > 1e-4)
        or state.speed ~= part.setting("speed") then
      state.speed = part.setting("speed")
      move()
    end
    -- Stalled: still short of the goal and not moving for a while (something heavy on its head).
    if joint and joint.position and state.goal then
      local moved = state.last and math.abs(joint.position - state.last) > 1e-4
      state.last = joint.position
      if math.abs(joint.position - state.goal) > 0.02 and not moved then
        state.still = (state.still or 0) + 1
        if state.still == STALL_STEPS then play("stall") end
      else
        state.still = 0
      end
    end
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
  local extended = amount > 0
  if extended == state.extended then return end
  state.extended = extended
  if state.controlled then return end
  if part.joint() then play(extended and "extend" or "retract") end
  move()
end

function on_destroy()
  part.set_piston(nil)
end
