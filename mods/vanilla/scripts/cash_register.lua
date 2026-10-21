-- Cash Register (parts map batch 6): a shop till whose drawer springs open with a ring. E opens or shuts it, and
-- its input opens it while it is on (a trigger, a button on the counter) and shuts it when it goes off. Its logic
-- output is on while the drawer is open. Sounds: "open" (the bell and the drawer) and "close". Its model plays the
-- "open" and "close" clips on its "clip" readout.

local function set(open)
  if state.open == open then
    return
  end
  state.open = open
  local id = part.sound(open and "open" or "close")
  if id then
    sound.play(id)
  end
  part.display("clip", open and "open" or "close")
end

function on_create()
  state.open = state.open or false
  state.was_on = state.was_on or false
  part.display("clip", state.open and "open" or "close")
end

function on_activate(peer)
  set(not state.open)
  return { open = state.open }
end

function on_update(dt)
  local on = part.input() > 0
  if on ~= state.was_on then
    set(on)
  end
  state.was_on = on
end

function get_outputs()
  return state.open and 1 or 0
end
