-- Fabricator: animates its model from its crafting queue (the crafting itself, and its start, work and complete
-- sounds, are the station's own). Its "clip" display plays idle_loop while nothing is crafting, start
-- (properties.start_time s) when it starts crafting, work_loop while it crafts (from the top for each new craft) and
-- complete (properties.complete_time s) when the last craft finishes or the queue stops (blocked or empty), then idles,
-- or starts again if more was queued meanwhile. The "weld" and "drill" displays (0 or 1) throw sparks at the arms'
-- tools while it works.

local function clip(name)
  state.clips = (state.clips or 0) + 1
  part.display("clip", name .. "#" .. state.clips)
end

local function working(on)
  local v = on and 1 or 0
  part.display("weld", v)
  part.display("drill", v)
end

local function enter(phase, seconds)
  state.phase = phase
  state.left = seconds
  if phase == "idle" then
    clip("idle_loop")
  elseif phase == "work" then
    clip("work_loop")
  else
    clip(phase)
  end
  working(phase == "work")
end

function on_create()
  state.finished = nil
  enter("idle", 0)
end

function on_update(dt)
  local q = part.crafting()
  local busy = q ~= nil and q.working
  local finished = q and q.finished or 0
  if state.finished == nil then
    state.finished = finished
  end
  local done = finished > state.finished
  state.finished = finished
  state.left = (state.left or 0) - dt
  local phase = state.phase
  if done and busy and phase == "work" then
    enter("work", 0)
  elseif done and (phase == "work" or phase == "start") then
    enter("complete", properties.complete_time)
  elseif phase == "idle" then
    if busy then enter("start", properties.start_time) end
  elseif phase == "start" then
    if not busy then
      enter("complete", properties.complete_time)
    elseif state.left <= 0 then
      enter("work", 0)
    end
  elseif phase == "work" then
    if not busy then enter("complete", properties.complete_time) end
  elseif phase == "complete" and state.left <= 0 then
    enter(busy and "start" or "idle", properties.start_time)
  end
end
