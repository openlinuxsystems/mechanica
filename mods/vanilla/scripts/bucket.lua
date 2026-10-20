-- The empty water bucket. Aimed at water, the Item tool fills it and every other empty one carried (CollectWater,
-- which turns each into the full bucket in the same slot); used anywhere else it only says so.

function can_use(target)
  return "Aim at water to fill the bucket."
end

function on_use(target)
end
