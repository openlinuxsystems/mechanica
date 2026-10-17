-- Timer: its output is its input, "delay" seconds later (a setting). Changes in between are kept in
-- order, so a short pulse comes out as the same pulse.

function on_create()
  state.queue = state.queue or {}
  state.output = state.output or 0
end

function on_power(amount)
  table.insert(state.queue, { at = world.time() + (part.setting("delay") or 1), value = amount })
end

function on_update(dt)
  local now = world.time()
  while state.queue[1] and state.queue[1].at <= now + 1e-9 do
    state.output = table.remove(state.queue, 1).value
  end
end

function get_outputs()
  return state.output
end
