-- Resource Collector (Parts batch 17c, E1): every properties.interval seconds it pulls the nearest loose resource
-- piece or pile within its reach (its intake, 12 blocks) into its own inventory, through the host's CollectItems
-- action. A Refiner whose input port is piped to its output port refines from it; crops and water it leaves alone.

local next_check = 0

function on_update(dt)
  local now = world.time()
  if now < next_check then
    return
  end
  next_check = now + properties.interval
  for _, pickup in ipairs(part.pickups()) do
    if (pickup.kind == "loose" or pickup.kind == "drop") and part.collect(pickup) then
      return
    end
  end
end
