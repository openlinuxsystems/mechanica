-- Button: on only while it is held down (logic.hold): E on it, or its key on a linked seat's bar. Each player who holds
-- it keeps it down until they let go (on_release, which the host also calls when they stand up, die or leave).

function on_create()
  -- Nobody holds a button that was just put down or loaded.
  state.held = {}
  part.display("on", false)
end

function on_reload()
  state.held = state.held or {}
end

local function down()
  return next(state.held) ~= nil
end

function on_activate(peer)
  local was = down()
  state.held[tostring(peer)] = true
  part.display("on", true)
  if not was then
    part.display("clip", "press")
  end
end

function on_release(peer)
  state.held[tostring(peer)] = nil
  if not down() then
    part.display("on", false)
    part.display("clip", "release")
  end
end

function get_outputs()
  return down()
end
