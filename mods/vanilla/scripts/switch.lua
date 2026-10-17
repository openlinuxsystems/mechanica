-- Switch: use it to turn its output on, and again to turn it off. It stays as it was left, pressed in while on.

function on_create()
  state.on = state.on or false
  part.display("on", state.on)
end

function on_activate(peer)
  state.on = not state.on
  part.display("on", state.on)
end

function get_outputs()
  return state.on
end
