-- Beacon (chunk 23): press F to switch it on. While on it flashes its light and pings every
-- properties.ping seconds, so a base or a wreck can be found again; flash (s) is how long each flash lasts.

function on_create()
  state.on = state.on or false
  state.next_ping = state.next_ping or 0
end

function on_activate(peer)
  state.on = not state.on
  if state.on then
    state.next_ping = world.time()
    local id = part.sound("activate")
    if id then
      sound.play(id)
    end
  end
end

function on_update(dt)
  if not state.on then
    part.set_light(0)
    return
  end
  local now = world.time()
  if now >= state.next_ping then
    state.next_ping = now + properties.ping
    state.flash_until = now + properties.flash
    local id = part.sound("ping")
    if id then
      sound.play(id)
    end
  end
  part.set_light(now < (state.flash_until or 0) and 1 or 0.15)
end
