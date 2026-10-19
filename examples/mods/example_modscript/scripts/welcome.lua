-- Welcome (the mod scripts cookbook's example). mod.json lists this file under "scripts", so it runs on the
-- host for the whole world, after the mods load. It has no part: it listens to events, sets timers and
-- calls actions. Its state is saved with the world.

local gifts = require("scripts/lib/gifts.lua")

local ITEM = "trail_mix"

-- The numbers live in data: the Trail Mix item's properties (welcome_gift, snack_interval).
local function settings()
  return world.item(ITEM, "properties")
end

-- Every few minutes, a snack for every living player. actions.call runs an Action API action as the host;
-- GetPlayers only reads, and returns a list of players ({peer, name, dead, ...}).
local function snack_time()
  local ok, players = actions.call("GetPlayers")
  if not ok then
    return
  end
  for _, p in ipairs(players) do
    if not p.dead then
      gifts.give(p.peer, ITEM, 1)
    end
  end
  state.snacks = (state.snacks or 0) + 1
end

-- Timers end on a reload and are not saved, so start them from both on_create and on_reload.
local function start()
  timer.every(settings().snack_interval, snack_time)
end

-- Listening is per script and a reload starts with none: listen at the top level, which runs again on reload.
events.listen("players")

function on_create()
  state.greeted = state.greeted or {}
  start()
end

function on_reload()
  start()
end

-- A player joined or left: data is {kind = "joined" or "left", peer, name}.
function on_event(topic, data)
  if topic == "players" and data.kind == "joined" then
    state.greeted = state.greeted or {}
    if not state.greeted[data.name] then
      state.greeted[data.name] = true
      gifts.give(data.peer, ITEM, settings().welcome_gift)
      print("Welcomed " .. data.name .. " with " .. settings().welcome_gift .. " Trail Mix")
    end
  end
end
