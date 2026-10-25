-- Horn (chunk 23): it sounds while its input is on, and for properties.honk seconds after a player
-- presses E on it. Its "pitch" setting (1 to 9, 5 as recorded) tunes the honk through its sound's "pitch"
-- parameter, also while it sounds. A honk sound without that parameter plays as recorded. Its "pose" readout is
-- "on" while it sounds and "off" otherwise, so the model lights its pip.

local loop, tuned, shown

function on_create()
  state.until_time = state.until_time or 0
end

function on_activate(peer)
  state.until_time = world.time() + properties.honk
end

function on_update(dt)
  local on = part.input() > 0 or world.time() < state.until_time
  local pitch = part.setting("pitch") or 5
  if on ~= shown then
    part.display("pose", on and "on" or "off")
    shown = on
  end
  if on and not loop then
    local id = part.sound("honk")
    if id then
      loop = sound.start(id, { pitch = pitch }) or sound.start(id)
      tuned = pitch
    end
  elseif not on and loop then
    sound.stop(loop)
    loop = nil
  elseif loop and pitch ~= tuned then
    sound.set_parameter(loop, "pitch", pitch)
    tuned = pitch
  end
end
