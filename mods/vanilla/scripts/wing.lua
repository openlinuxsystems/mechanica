-- Wing and control surface: lift and drag from the air flowing over the part, every physics step. The
-- surface is the part's XZ plane, so lift acts along its +Y or -Y, whichever side the air comes from.
-- Properties: "area" (m²), "air_density" (kg/m³), "camber_lift" (lift coefficient with the air along the
-- surface: a cambered wing lifts toward its +Y even then), "lift_slope" (lift coefficient per radian of
-- angle of attack), "max_lift" (the lift coefficient where it stalls), "stall_lift" (lift coefficient of a stalled
-- plate at 45°), "drag" (drag coefficient at no lift), "induced_drag" (extra drag per lift coefficient
-- squared), "stall_drag" (drag of a plate square to the air) and "whoosh_speed" (m/s where the whoosh is
-- loudest).
-- A control surface also has "max_deflection" (degrees) and the "control" and "invert" settings: its
-- command (-1 to 1) tilts the flow it sees by up to max_deflection, as a hinged flap would. "control" picks
-- where the command comes from: "link" is the sum of its logic inputs, "w_s" and "a_d" are the keys of the
-- pilot in a seat linked to it, and "space_shift" is a lever the pilot moves with Space and Shift, at
-- "lever_rate" per second, that stays where they leave it.
-- Sounds: "whoosh" loops while air flows over it, "stall" plays when it stalls, "move" when the pilot
-- moves it and "lock" when it comes back to the middle.

local MIN_SPEED = 2 -- m/s; slower air makes no noticeable force

local function clamp(v, lo, hi)
  return math.max(lo, math.min(hi, v))
end

local function play(key)
  if part.sound(key) then sound.play(part.sound(key)) end
end

-- The controls of the pilot in a seat linked to the part, or nil.
local function pilot()
  for _, input in ipairs(part.inputs()) do
    local controls = creation.occupant_input(input.part)
    if controls then return controls end
  end
end

-- The surface's command from -1 to 1, from its "control" setting.
local function command(dt)
  local control = part.setting("control") or "link"
  local value
  if control == "link" then
    value = part.input()
  elseif control == "space_shift" then
    local controls = pilot()
    local move = controls and ((controls.jump and 1 or 0) - (controls.sprint and 1 or 0)) or 0
    state.lever = clamp((state.lever or 0) + move * (properties.lever_rate or 1) * dt, -1, 1)
    value = state.lever
  else
    local controls = pilot()
    value = controls and (control == "w_s" and controls.z or controls.x) or 0
  end
  if part.setting("invert") then value = -value end
  return clamp(value, -1, 1)
end

-- Lift and drag coefficients at angle of attack a (radians, -π/2 to π/2).
local function coefficients(a)
  local cl = (properties.camber_lift or 0) + properties.lift_slope * a
  if math.abs(cl) > properties.max_lift then
    -- Past the stall the wing is a flat plate: little lift and a lot of drag.
    cl = properties.stall_lift * math.sin(2 * a)
    local s = math.sin(a)
    return cl, properties.drag + properties.stall_drag * s * s, true
  end
  return cl, properties.drag + properties.induced_drag * cl * cl, false
end

-- Starts, bends and stops the whoosh with the airspeed.
local function whoosh(speed)
  if not part.sound("whoosh") then return end
  local level = clamp(speed / properties.whoosh_speed, 0, 1)
  if level < 0.1 then
    if state.whoosh then
      sound.stop(state.whoosh)
      state.whoosh = nil
    end
    return
  end
  if not state.whoosh then
    state.whoosh = sound.start(part.sound("whoosh"), { airspeed = level })
    state.level = level
  elseif math.abs(level - (state.level or 0)) > 0.05 then
    sound.set_parameter(state.whoosh, "airspeed", level)
    state.level = level
  end
end

function on_update(dt)
  local deflection = 0
  if properties.max_deflection then
    local c = command(dt)
    deflection = c * math.rad(properties.max_deflection)
    local moving = math.abs(c - (state.command or 0)) > 0.01
    if moving and not state.moving then play("move") end
    if c == 0 and (state.command or 0) ~= 0 then play("lock") end
    state.moving, state.command = moving, c
  end

  local v = part.velocity()
  local vx, vy, vz = v.x, v.y, v.z
  local speed2 = vx * vx + vy * vy + vz * vz
  if speed2 < MIN_SPEED * MIN_SPEED then
    whoosh(0)
    return
  end
  local speed = math.sqrt(speed2)
  -- The air flows past the part opposite to how it moves: w is the flow direction.
  local wx, wy, wz = -vx / speed, -vy / speed, -vz / speed
  local n = part.to_world(vec3(0, 1, 0))
  local nx, ny, nz = n.x, n.y, n.z
  local along = clamp(wx * nx + wy * ny + wz * nz, -1, 1)
  -- Air hitting the underside (+along) pushes toward +Y; a deflected flap adds to the angle.
  local cl, cd, stalled = coefficients(clamp(math.asin(along) + deflection, -math.pi / 2, math.pi / 2))
  if stalled and not state.stalled then play("stall") end
  state.stalled = stalled

  -- Lift is square to the flow, on the side of the surface's normal; drag is along the flow.
  local lx, ly, lz = nx - along * wx, ny - along * wy, nz - along * wz
  local ll = math.sqrt(lx * lx + ly * ly + lz * lz)
  local q = 0.5 * properties.air_density * speed2 * properties.area
  local lift, drag = q * cl, q * cd
  if ll > 1e-6 then
    lx, ly, lz = lx / ll * lift, ly / ll * lift, lz / ll * lift
  else
    lx, ly, lz = 0, 0, 0 -- the air hits it face on: drag only
  end
  part.apply_force(vec3(lx + wx * drag, ly + wy * drag, lz + wz * drag), part.position())
  whoosh(speed)
end
