-- Logic gate: its output is 1 or 0 from its inputs, each of which is on when it is not 0.
-- Setting "mode": and, or, xor (an odd number on), nand, nor or xnor. With no inputs everything is off,
-- so nand, nor and xnor are on.

local modes = {
  ["and"] = function(on, total) return total > 0 and on == total end,
  ["or"] = function(on) return on > 0 end,
  xor = function(on) return on % 2 == 1 end,
}
modes.nand = function(on, total) return not modes["and"](on, total) end
modes.nor = function(on, total) return not modes["or"](on, total) end
modes.xnor = function(on, total) return not modes.xor(on, total) end

function get_outputs()
  local on, total = 0, 0
  for _, input in ipairs(part.inputs()) do
    total = total + 1
    if input.value ~= 0 then
      on = on + 1
    end
  end
  local mode = modes[part.setting("mode")] or modes["and"]
  return mode(on, total)
end
