-- Scuttler Head (parts map batch 6): a Scuttler's head, salvaged, that plays one note each time its input turns on.
-- Its "note" setting (0 to 24) picks the note in semitones from two octaves: 12 is the voice's own pitch (C4), 0 an
-- octave down and 24 an octave up. Its "voice" setting picks the sound: "blip", "bass", "drum" or "voice", each bound
-- to a sound under that key in its sounds. E opens its menu; changing the note or voice there plays it once.
-- Its lens flashes with every note: the "pose" readout plays the model's "note" clip (a restart is "note#n").

local function play()
  local voice = part.setting("voice") or "blip"
  local id = part.sound(voice)
  if not id then
    return
  end
  local note = part.setting("note") or 12
  sound.play(id, nil, { pitch = 2 ^ ((note - 12) / 12) })
  state.notes = (state.notes or 0) + 1
  part.display("pose", "note#" .. state.notes)
end

function on_create()
  state.was_on = state.was_on or false
  state.heard = (part.setting("note") or 12) .. ":" .. (part.setting("voice") or "blip")
end

function on_update(dt)
  local on = part.input() > 0
  if on and not state.was_on then
    play()
  end
  state.was_on = on
  -- A new note or voice picked in the menu plays once, so the player hears what they set.
  local heard = (part.setting("note") or 12) .. ":" .. (part.setting("voice") or "blip")
  if heard ~= state.heard then
    state.heard = heard
    play()
  end
end
