-- Bearing motor: spins this bearing at a set speed
-- {{name}}. Properties: "speed" (rad/s), "torque" (N·m, optional; the motion.json default otherwise).

function on_create()
  local ok, why = part.set_bearing(properties.speed or 0, properties.torque)
  if not ok then log("can't drive the bearing: " .. why) end
end

function on_reload()
  on_create()
end

function on_destroy()
  part.set_bearing(nil)
end
