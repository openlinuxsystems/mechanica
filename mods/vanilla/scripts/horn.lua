-- Horn (chunk 23): it sounds while its input is on, and for properties.honk seconds after a player
-- presses F on it.

local loop

function on_create()
  state.until_time = state.until_time or 0
end

function on_activate(peer)
  state.until_time = world.time() + properties.honk
end

function on_update(dt)
  local on = part.input() > 0 or world.time() < state.until_time
  if on and not loop then
    local id = part.sound("honk")
    if id then
      loop = sound.start(id)
    end
  elseif not on and loop then
    sound.stop(loop)
    loop = nil
  end
end
