-- Lights (chunk 23): headlights, spotlights, lamps, warning lights, glowsticks, path lights. A light shines
-- while its input is on; with nothing linked to it, it shines on its own. A light with "use" in its logic
-- section is switched off and on again with F. The look of the light (colour, range, cone) is the part's
-- light section; its behaviour is these properties:
--   blink (s, optional): one on-and-off cycle; duty (0 to 1, default 0.5): the share of it spent lit.

function on_create()
  state.off = state.off or false
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
  return 1
end

function on_update(dt)
  local level = wanted()
  if level == part.light() then
    return
  end
  part.set_light(level)
  -- A blinking light would click every cycle, so only steady lights sound when they switch.
  if not properties.blink then
    local id = part.sound(level > 0 and "on" or "off")
    if id then
      sound.play(id)
    end
  end
end
