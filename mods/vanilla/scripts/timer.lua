-- Timer: with the "mode" setting "once" its output is its input, "delay" seconds later (a setting). Changes
-- in between are kept in order, so a short pulse comes out as the same pulse. With "repeat" its output turns
-- on and off every "delay" seconds while its input is on, and goes off when the input does. Its "complete"
-- sound plays when the output turns on. Its "pose" readout is "count_loop" while a change waits or it blinks and
-- "rest" otherwise (the model's sweep hand), and its "lit" readout 1 while the output is on.

local shown_pose, shown_lit

local function show()
  local pose = (state.blink or #state.queue > 0) and "count_loop" or "rest"
  if pose ~= shown_pose then
    part.display("pose", pose)
    shown_pose = pose
  end
  local lit = state.output ~= 0 and 1 or 0
  if lit ~= shown_lit then
    part.display("lit", lit)
    shown_lit = lit
  end
end

local function turn_on(value)
  if value ~= 0 and state.output == 0 and part.sound("complete") then
    sound.play(part.sound("complete"))
  end
  state.output = value
end

function on_create()
  state.queue = state.queue or {}
  state.output = state.output or 0
  state.input = state.input or 0
end

function on_power(amount)
  state.input = amount
  if part.setting("mode") == "repeat" then
    if amount == 0 then
      state.output = 0
      state.blink = nil
    elseif not state.blink then
      state.blink = world.time() + (part.setting("delay") or 1)
    end
    return
  end
  table.insert(state.queue, { at = world.time() + (part.setting("delay") or 1), value = amount })
end

local function step()
  local now = world.time()
  if part.setting("mode") == "repeat" then
    state.queue = {}
    if state.input == 0 then
      state.output = 0
      state.blink = nil
      return
    end
    state.blink = state.blink or now + (part.setting("delay") or 1)
    if state.blink <= now + 1e-9 then
      turn_on(state.output == 0 and state.input or 0)
      state.blink = now + (part.setting("delay") or 1)
    end
    return
  end
  state.blink = nil
  while state.queue[1] and state.queue[1].at <= now + 1e-9 do
    turn_on(table.remove(state.queue, 1).value)
  end
end

function on_update(dt)
  step()
  show()
end

function get_outputs()
  return state.output
end
