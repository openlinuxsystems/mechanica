-- Battery Container: holds properties.charge_item (batteries, item battery_pack) for the creation's electric engines (they take
-- them themselves, see engine.lua). This script only shows how charged it is, for its model:
--   "charge"   how full it is, 0 to 1 (what it holds of what it can hold), easing over properties.ease seconds
--              when the amount changes; the gauge cells on its sides follow it.

local CHECK_EVERY = 5

local function capacity(held)
  return math.max(1, held + creation.space(properties.charge_item, { part.id() }))
end

local function held()
  return creation.count(properties.charge_item, { part.id() })
end

function on_create()
  state.held = held()
  state.capacity = capacity(state.held)
  state.charge = state.held / state.capacity
  part.display("charge", state.charge)
end

function on_reload()
  state.capacity = capacity(held())
end

function on_update(dt)
  local target = state.held / state.capacity
  if state.charge == target and world.tick() % CHECK_EVERY ~= 0 then
    return
  end
  local now = held()
  if now ~= state.held then
    state.held = now
    state.capacity = capacity(now)
    target = now / state.capacity
  end
  if state.charge ~= target then
    local step = dt / math.max(0.01, properties.ease)
    if state.charge < target then
      state.charge = math.min(target, state.charge + step)
    else
      state.charge = math.max(target, state.charge - step)
    end
    -- in steps of 1/200 so it syncs seldom
    part.display("charge", math.floor(state.charge * 200 + 0.5) / 200)
  end
end
