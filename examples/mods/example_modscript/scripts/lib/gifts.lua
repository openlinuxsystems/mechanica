-- A small library the mod's scripts share with require("scripts/lib/gifts.lua"). It runs once per mod and
-- every script that requires it gets the same table. It has no state or properties of its own: pass in
-- what it needs.
local M = {}

-- Gives a player count of an item (a bare id is this mod's). Logs why when it does not fit.
function M.give(peer, item, count)
  local ok, why = player.give(peer, { { item = item, count = count } })
  if not ok then
    log("Could not give " .. count .. " " .. item .. " to player " .. peer .. ": " .. tostring(why))
  end
  return ok
end

return M
