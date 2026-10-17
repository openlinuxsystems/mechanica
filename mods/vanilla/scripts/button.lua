-- Button: use it and its output is on for properties.pulse seconds.

function on_create()
  state.until_time = state.until_time or 0
end

function on_activate(peer)
  state.until_time = world.time() + properties.pulse
end

function get_outputs()
  return world.time() < state.until_time
end
