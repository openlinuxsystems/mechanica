-- Beacon (chunk 23): use it to switch it on. While on it flashes its light and pings every
-- properties.ping seconds, so a base or a wreck can be found again; flash (s) is how long each flash lasts.
-- Its model shows what it does through the "pose" readout: "off", "on" (the lamp's band and the screen's
-- chevron lit) and "flash" (the whole lamp lit), clips built by tools/beacon/build_beacon.py.

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

function on_create()
  state.on = state.on or false
  state.next_ping = state.next_ping or 0
  state.pose = nil
  show(state.on and "on" or "off")
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
    show("off")
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
  local flashing = now < (state.flash_until or 0)
  part.set_light(flashing and 1 or 0.15)
  show(flashing and "flash" or "on")
end
