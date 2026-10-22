-- Vacuum Pump (Parts batch 17c, E1): while its logic input is on it works its mouth (its intake) every
-- properties.interval seconds. Set to "suck" (its mode setting) it takes in the nearest thing in reach that fits: a
-- pile on the ground, a loose resource piece (a log, a chunk), a ripe crop (the soil's own on_collect harvests it) or
-- water, into the containers its back port reaches through Vacuum Pipes. Set to "eject" it throws properties.eject
-- items at a time from those containers out of its mouth as a pile. Everything goes through the host's CollectItems
-- and EjectItems actions; the network is worked out from the build, so a pipe taken away cuts it at once.
-- Its model shows "on" (the cyan ring lit) on its "pose" readout while it runs, "off" otherwise, and its "work" sound
-- (a loop) rushes while it runs.

local loop

local function show(pose)
  if state.pose ~= pose then
    state.pose = pose
    part.display("pose", pose)
  end
  if pose == "on" and not loop then
    local id = part.sound("work")
    loop = id and sound.start(id, { airspeed = properties.rush }) or nil
  elseif pose ~= "on" and loop then
    sound.stop(loop)
    loop = nil
  end
end

-- Takes in the nearest pickup that fits.
local function suck()
  for _, pickup in ipairs(part.pickups()) do
    if part.collect(pickup) then
      return
    end
  end
end

function on_create()
  state.next = 0
  state.pose = nil
  show(part.input() > 0 and "on" or "off")
end

function on_power(amount)
  show(amount > 0 and "on" or "off")
end

function on_update(dt)
  if part.input() <= 0 then
    show("off")
    return
  end
  show("on")
  local now = world.time()
  if now < (state.next or 0) then
    return
  end
  state.next = now + properties.interval
  if part.setting("mode") == "eject" then
    part.eject(nil, properties.eject)
  else
    suck()
  end
end
