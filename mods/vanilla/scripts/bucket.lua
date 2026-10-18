-- The empty water bucket. Aimed at water, the Item tool fills it (CollectWater, which turns it into the full
-- bucket in the same slot); used anywhere else it only says so.

function can_use(target)
  return "Aim at water to fill the bucket."
end

function on_use(target)
end
