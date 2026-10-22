-- Lights (chunk 23, parts map batch 5): headlights, spotlights, lamps, signs, warning lights, glowsticks. A light
-- shines while its input is on; with nothing linked to it, it shines on its own. A light with "use" in its logic
-- section is switched off and on again with E. The look of the light (colour, range, cone) is the part's light
-- section, and a painted light shines in its paint colour; its behaviour is these properties:
--   blink (s, optional): one on-and-off cycle; duty (0 to 1, default 0.5): the share of it spent lit.
-- A "brightness" setting (1 to 10), where the part has one, dims it to that many tenths of full.
-- Its model shows whether it is lit through the "pose" readout: "on" or "off" (the clips its lamp's glow and power
-- pip follow; part data display.clips).

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

function on_create()
  state.off = state.off or false
  state.pose = nil
  show(part.light() > 0 and "on" or "off")
end

function on_reload()
  state.pose = nil
  show(part.light() > 0 and "on" or "off")
end

function on_activate(peer)
  state.off = not state.off
end

local function wanted()
  if state.off then
    return 0
  end
  if #part.inputs() > 0 and part.input() <= 0 then
    return 0
  end
  if properties.blink then
    local phase = (world.time() % properties.blink) / properties.blink
    if phase >= (properties.duty or 0.5) then
      return 0
    end
  end
  return (part.setting("brightness") or 10) / 10
end

function on_update(dt)
  local level = wanted()
  local was = part.light()
  if level ~= was then
    part.set_light(level)
  end
  show(level > 0 and "on" or "off")
  -- A blinking light would click every cycle, and a dimmer only changes how bright it is, so only a steady light
  -- going on or off sounds.
  if not properties.blink and (level > 0) ~= (was > 0) then
    local id = part.sound(level > 0 and "on" or "off")
    if id then
      sound.play(id)
    end
  end
end
