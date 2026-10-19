-- Switch: use it to turn its output on, and again to turn it off. It stays as it was left: its rocker tips over and
-- its light turns from red to teal while on.

local function show()
  part.display("on", state.on)
  part.display("pose", state.on and "on" or "off")
end

function on_create()
  state.on = state.on or false
  show()
end

function on_activate(peer)
  state.on = not state.on
  show()
end

function get_outputs()
  return state.on
end
