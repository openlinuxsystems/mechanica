-- Motor Bearing (the Aero Tutorial's example bearing). It runs on the host only, once per physics step.
--
-- The globals a part script gets:
--   part        this part: its input, its joint, its sounds (see docs/lua-api.md)
--   properties  the "properties" object from the part's JSON (properties.speed, properties.torque)
--   state       a table kept between steps and across a script reload
--   sound       one-shots and loops

-- The spin loop plays while the motor runs. part.sound("spin_loop") is the sound id from the part's JSON.
local function hum(speed)
  local id = part.sound("spin_loop")
  if not id then return end
  if speed ~= 0 and not state.loop then
    state.loop = sound.start(id)
  elseif speed == 0 and state.loop then
    sound.stop(state.loop)
    state.loop = nil
  end
end

-- Called every physics step with dt in seconds (1/60).
function on_update(dt)
  -- The input is the sum of the outputs of every part linked to this one: 1 for a switch that is on,
  -- -1 to 1 for a seat's throttle.
  local speed = part.input() * properties.speed
  -- Only tell the joint when the speed changes: the motor keeps its target until it is changed.
  if speed == state.speed then return end
  state.speed = speed
  if speed == 0 then
    part.set_bearing(nil) -- free: it turns with whatever pushes it
  else
    part.set_bearing(speed, properties.torque)
  end
  hum(speed)
end

-- After an edit to this file in game the script reloads with its state kept; tell the joint again.
function on_reload()
  state.speed = nil
end
