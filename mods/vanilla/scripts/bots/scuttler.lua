-- Scuttler family leaves, called from their behaviour trees with { "type": "call", "function": ... }.
-- Everything here uses the generic bot primitives (bot.*, world.*, behavior.*); see docs/bot-tutorial.md.

-- The Blue Scuttler's drill charge: it shrieks once at each new target, then runs at it flat out.
function charge(node)
  local t = behavior.target()
  if t == nil then return false end
  local m = bot.memory()
  local key = tostring(t.player) .. ":" .. tostring(t.creation) .. ":" .. tostring(t.part) .. ":" .. tostring(t.bot)
  if m.charging ~= key then
    m.charging = key
    bot.sound("scuttler_charge")
  end
  return true
end
