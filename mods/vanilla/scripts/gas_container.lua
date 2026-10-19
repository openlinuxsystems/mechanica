-- Gas Container: holds properties.fuel (gasoline) for the engines and thrusters it is linked to with the Connect Tool
-- (they take it themselves, see engine.lua). This script only shows how full it is, for its model:
--   "fill"   how full it is, 0 to 1 (what it holds of what it can hold), easing over properties.ease seconds
--            when the amount changes; the model's gauge windows follow it.
--   "clip"   idle_loop (a faint shimmer on the gauge while it is partly full), rest (empty or full), pulse (a short
--            glow when fuel goes in) and supply (a pulse down the gauge and a glow on the sockets when a linked part
--            draws fuel).

local CHECK_EVERY = 5

local function clip(name)
  state.clips = (state.clips or 0) + 1
  part.display("clip", name .. "#" .. state.clips)
end

local function capacity(held)
  return math.max(1, held + creation.space(properties.fuel, { part.id() }))
end

local function held()
  return creation.count(properties.fuel, { part.id() })
end

-- The clip it rests in: the shimmer only while it is partly full.
local function settle()
  local full = state.held / state.capacity
  clip(full > 0 and full < 1 and "idle_loop" or "rest")
end

function on_create()
  state.held = held()
  state.capacity = capacity(state.held)
  state.fill = state.held / state.capacity
  state.timer = 0
  part.display("fill", state.fill)
  settle()
end

function on_reload()
  state.capacity = capacity(held())
end

function on_update(dt)
  local target = state.held / state.capacity
  local easing = state.fill ~= target
  if state.timer > 0 then
    state.timer = state.timer - dt
    if state.timer <= 0 then
      settle()
    end
  end
  if not easing and world.tick() % CHECK_EVERY ~= 0 then
    return
  end
  local now = held()
  if now ~= state.held then
    -- more went in, or a linked engine or thruster drew some
    clip(now > state.held and "pulse" or "supply")
    state.timer = now > state.held and properties.pulse_time or properties.supply_time
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
