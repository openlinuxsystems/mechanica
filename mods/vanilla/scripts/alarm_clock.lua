-- Alarm Clock (parts map batch 6): rings once a day at the time set in its menu (E): the "hour" and "minute"
-- settings, in the world's time of day (world.time_of_day: a world starts at 06:00). While "armed" is on and the
-- clock passes that time it rings for properties.ring seconds, playing the sound its "alarm" setting picks ("bell",
-- "beep" or "chime", bound under those keys in its sounds) every properties.repeat_time seconds, and its logic output is on, so it
-- can start an engine, open a door or fire a cannon at dawn. A linked input turning on (a button) or disarming it
-- stops the ringing. Changing its time or arming it in the menu ticks (its "set" sound).
-- Its menu shows the time now ("now") and when it rings ("rings"). Its model shows the "pose" readout: "off"
-- (disarmed), "armed" (a cyan pip lit) and "ring_loop" (the hammer drums the bells).

-- "hh:mm", to the nearest minute (7 + 5/60 is a hair under 07:05 in floating point).
local function clock(hours)
  local minutes = math.floor(hours * 60 + 0.5) % (24 * 60)
  return string.format("%02d:%02d", math.floor(minutes / 60), minutes % 60)
end

local function target()
  return ((part.setting("hour") or 7) + (part.setting("minute") or 0) / 60) % 24
end

-- True when the clock went from `from` to `to` (hours, wrapping at 24) across `at`.
local function crossed(from, to, at)
  if from <= to then
    return from < at and at <= to
  end
  return at > from or at <= to
end

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
end

local function stop()
  state.ringing_until = nil
end

-- What the menu has set; a change ticks ("set").
local function setting()
  return string.format("%s:%s:%s", part.setting("hour") or 7, part.setting("minute") or 0, tostring(part.setting("armed")))
end

function on_create()
  state.pose = nil
  state.was_on = state.was_on or false
  state.set = setting()
end

function on_update(dt)
  local set = setting()
  if set ~= state.set then
    state.set = set
    local id = part.sound("set")
    if id then
      sound.play(id)
    end
  end
  local now = world.time_of_day()
  local armed = part.setting("armed")
  if armed == nil then
    armed = true
  end
  local last = state.last or now
  state.last = now
  if armed and crossed(last, now, target()) then
    state.ringing_until = world.time() + properties.ring
    state.next_ring = 0
  end
  local on = part.input() > 0
  if (on and not state.was_on) or not armed then
    stop()
  end
  state.was_on = on
  local ringing = state.ringing_until ~= nil and world.time() < state.ringing_until
  if not ringing then
    state.ringing_until = nil
  elseif world.time() >= (state.next_ring or 0) then
    state.next_ring = world.time() + properties.repeat_time
    local id = part.sound(part.setting("alarm") or "bell")
    if id then
      sound.play(id)
    end
  end
  state.ringing = ringing
  show(ringing and "ring_loop" or (armed and "armed" or "off"))
  part.display("now", clock(now))
  part.display("rings", clock(target()))
end

function get_outputs()
  return state.ringing and 1 or 0
end
