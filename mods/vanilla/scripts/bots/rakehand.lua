-- Rakehand leaves, called from its behaviour tree with { "type": "call", "function": ... }.

-- A battle cry when it starts chasing someone (the tree puts this under a cooldown).
function cry(node)
  bot.sound("rakehand_chase")
  return true
end
