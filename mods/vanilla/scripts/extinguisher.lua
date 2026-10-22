-- The Fire Extinguisher (Consumables batch C2a, engine item C8): hold Use to spray foam in a cone in front of the
-- player. The item's held.hold_use makes the press a held use: on_use runs once and holder.using() stays true every step
-- until Use is let go, the item leaves the hand or the player dies. While it sprays, every fire whose centre is within
-- properties.range metres of the player's eyes and inside the cone (properties.angle degrees either side of where they
-- look, plus properties.pad metres for a burning part's size) loses properties.douse strength a second through the
-- fire system (fire.douse; a fire is out at 0). One extinguisher sprays for properties.spray_seconds and is used up, then
-- the next in the stack carries on; a part-used one keeps its time. The spray sound (its "spray" sound) is played again
-- every properties.sound_interval seconds. Every player sees the foam: it is the held model's spray (held.spray).

function on_equip()
  state.spent = state.spent or 0
  state.sound_in = 0
end

function can_use(target)
  if item.count() < 1 then
    return "You have no " .. item.name() .. " left."
  end
end

function on_use(target)
  state.spent = state.spent or 0
  state.sound_in = 0
  return { spraying = true }
end

function on_use_release()
  state.sound_in = 0
end

-- Foam on every fire in the cone from `from` along `direction`; returns how many fires it reached.
local function douse_cone(from, direction, amount)
  local tan = math.tan(math.rad(properties.angle))
  local reached = 0
  for _, f in ipairs(fire.near(from, properties.range + properties.pad)) do
    local to = f.position - from
    local along = to:dot(direction)
    if along > 0 and along <= properties.range + properties.pad
        and (to - direction * along):length() <= along * tan + properties.pad then
      local doused = fire.douse(f.position, 0.1, amount)
      if doused then
        reached = reached + #doused
      end
    end
  end
  return reached
end

function on_update(dt)
  if not holder.using() or item.count() < 1 then
    return
  end
  local from, direction = holder.eye(), holder.look()
  if not from or not direction then
    return
  end
  douse_cone(from, direction, properties.douse * dt)
  state.sound_in = (state.sound_in or 0) - dt
  if state.sound_in <= 0 then
    state.sound_in = properties.sound_interval
    local spray = item.sound("spray")
    if spray then
      sound.play(spray, from + direction * 0.6)
    end
  end
  -- Used up over time: one item every spray_seconds of spraying, the next in the stack carrying on.
  state.spent = (state.spent or 0) + dt
  if state.spent >= properties.spray_seconds then
    state.spent = state.spent - properties.spray_seconds
    item.consume(1)
  end
end
