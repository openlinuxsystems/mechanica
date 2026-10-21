-- Feeding Trough: holds properties.food (carrots) for tame animals; tame Grazers make it their home (bots/grazer.json,
-- tame.homes). This script only shows how full it is, for its model:
--   "fill"   how full it is, 0 to 1 (what it holds of what it can hold), easing over properties.ease seconds
--            when the amount changes; the hay in the tub follows it.

local CHECK_EVERY = 5

local function capacity(held)
  return math.max(1, held + creation.space(properties.food, { part.id() }))
end

local function held()
  return creation.count(properties.food, { part.id() })
end

function on_create()
  state.held = held()
  state.capacity = capacity(state.held)
  state.fill = state.held / state.capacity
  part.display("fill", state.fill)
end

function on_reload()
  state.capacity = capacity(held())
end

function on_update(dt)
  local target = state.held / state.capacity
  if state.fill == target and world.tick() % CHECK_EVERY ~= 0 then
    return
  end
  local now = held()
  if now ~= state.held then
    state.held = now
    state.capacity = capacity(now)
    target = now / state.capacity
  end
  if state.fill ~= target then
    local step = dt / math.max(0.01, properties.ease)
    if state.fill < target then
      state.fill = math.min(target, state.fill + step)
    else
      state.fill = math.max(target, state.fill - step)
    end
    -- in steps of 1/200 so it syncs seldom
    part.display("fill", math.floor(state.fill * 200 + 0.5) / 200)
  end
end
