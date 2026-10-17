-- Radio (chunk 23): use it to turn it on or off. It plays the station picked in its settings (1 to 6)
-- from where it stands; changing the station while it plays retunes it.

local loop, playing

function on_create()
  state.on = state.on or false
end

function on_activate(peer)
  state.on = not state.on
  local id = part.sound(state.on and "on" or "off")
  if id then
    sound.play(id)
  end
end

local function stop()
  if loop then
    sound.stop(loop)
  end
  loop, playing = nil, nil
end

function on_update(dt)
  local station = math.floor(part.setting("station") or 1)
  if not state.on then
    stop()
    return
  end
  if loop and playing == station then
    return
  end
  if loop then
    local id = part.sound("tune")
    if id then
      sound.play(id)
    end
    stop()
  end
  local id = part.sound("station_" .. station)
  if id then
    loop = sound.start(id)
    playing = station
  end
end

function on_destroy()
  stop()
end
