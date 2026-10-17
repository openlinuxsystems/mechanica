-- The built-in behaviour library (chunk 21). It runs a bot's behaviour tree: nested nodes, each a
-- table with a `type` and, for composites and decorators, `children`. Every node returns
-- "success", "failure" or "running", and the tree runs again from the root on every think.
-- Nodes only use the generic bot primitives (bot.*, world.*), so everything here is plain Lua a mod
-- could write itself. docs/bot-tutorial.md lists the nodes; Mechanica.Core.Bots.BotBehavior mirrors
-- the list for the data checker and the Bot Behavior Editor.
--
-- Mods add leaves to `behavior.leaves` (shared by the mod's bots), or define a global function with
-- the leaf's name in the bot's own script: function my_leaf(node, dt) ... return "success" end.

local SUCCESS, FAILURE, RUNNING = "success", "failure", "running"

behavior = { SUCCESS = SUCCESS, FAILURE = FAILURE, RUNNING = RUNNING, leaves = {} }
local leaves = behavior.leaves

-- Per-node memory, kept per bot and per node table.
local function memory(node)
  local all = bot.memory()
  local m = all[node]
  if m == nil then
    m = {}
    all[node] = m
  end
  return m
end

local function status(value)
  if value == true then return SUCCESS end
  if value == false or value == nil then return FAILURE end
  return value
end

local run

-- ---- Composites and decorators ----

function leaves.sequence(node, dt)
  for _, child in ipairs(node.children) do
    local s = run(child, dt)
    if s ~= SUCCESS then return s end
  end
  return SUCCESS
end

function leaves.selector(node, dt)
  for _, child in ipairs(node.children) do
    local s = run(child, dt)
    if s ~= FAILURE then return s end
  end
  return FAILURE
end

function leaves.invert(node, dt)
  local s = run(node.children[1], dt)
  if s == SUCCESS then return FAILURE end
  if s == FAILURE then return SUCCESS end
  return s
end

function leaves.succeed(node, dt)
  local s = run(node.children[1], dt)
  if s == RUNNING then return RUNNING end
  return SUCCESS
end

function leaves.cooldown(node, dt)
  local m = memory(node)
  if m.until_time and world.time() < m.until_time then return FAILURE end
  local s = run(node.children[1], dt)
  if s == SUCCESS then m.until_time = world.time() + (node.seconds or 5) end
  return s
end

-- ---- Targets ----

-- The bot's target lives in its memory: { player = peer } or { creation = c, part = p } or { bot = id }.
function behavior.target() return bot.memory().target end
function behavior.set_target(t) bot.memory().target = t end

function leaves.has_target()
  local t = behavior.target()
  if t ~= nil and world.locate(t) ~= nil then return SUCCESS end
  behavior.set_target(nil)
  return FAILURE
end

function leaves.clear_target()
  behavior.set_target(nil)
  return SUCCESS
end

function leaves.find_player(node)
  local range = node.range or bot.sight()
  local nearest = world.players(range)[1]
  if nearest == nil then return FAILURE end
  local t = behavior.target()
  if t == nil or t.player ~= nearest.peer then
    behavior.set_target({ player = nearest.peer })
    bot.alert()
  end
  return SUCCESS
end

function leaves.find_crop(node)
  local range = node.range or bot.sight()
  local raid = bot.raid()
  local crops
  if raid ~= nil then
    crops = world.crops_near(raid.target, math.max(range, 16))
  else
    crops = world.crops(range)
  end
  local nearest = crops[1]
  if nearest == nil then return FAILURE end
  behavior.set_target({ creation = nearest.creation, part = nearest.part })
  return SUCCESS
end

-- ---- Moving ----

local function attack_range(id)
  if id == nil then return 1.5 end
  local a = bot.attack_info(id)
  if a == nil then error("the bot has no attack '" .. tostring(id) .. "'") end
  return a.range
end

function leaves.target_in_range(node)
  local t = behavior.target()
  if t == nil then return FAILURE end
  local reach = bot.reach(t)
  if reach ~= nil and reach <= attack_range(node.attack) then return SUCCESS end
  return FAILURE
end

function leaves.chase(node)
  local t = behavior.target()
  if t == nil then return FAILURE end
  local at = world.locate(t)
  local reach = bot.reach(t)
  if at == nil or reach == nil then
    bot.stop()
    return FAILURE
  end
  if reach <= attack_range(node.attack) * 0.8 then
    bot.stop()
    bot.face(at)
    return SUCCESS
  end
  bot.move_to(at, bot.speed() * (node.speed or 1))
  return RUNNING
end

function leaves.go_to_raid(node)
  local raid = bot.raid()
  if raid == nil then return FAILURE end
  local away = raid.target - bot.position()
  away.y = 0
  if away:length() <= (node.radius or 6) then
    bot.stop()
    return SUCCESS
  end
  bot.move_to(raid.target, bot.speed())
  return RUNNING
end

function leaves.flee(node)
  local t = behavior.target()
  local at = t and world.locate(t)
  if at == nil then return FAILURE end
  local away = bot.position() - at
  away.y = 0
  if away:length() >= (node.range or 15) then
    bot.stop()
    return SUCCESS
  end
  if away:length() < 0.01 then away = vec3(1, 0, 0) end
  bot.move(away, bot.speed())
  return RUNNING
end

function leaves.wander(node)
  local m = memory(node)
  local radius = node.radius or 8
  local here = bot.position()
  if m.goal == nil or (m.goal - here):length() < 0.75 or world.time() > (m.give_up or 0) then
    local angle = bot.random() * 2 * math.pi
    local distance = bot.random() * radius
    local home = bot.home()
    m.goal = vec3(home.x + math.sin(angle) * distance, here.y, home.z + math.cos(angle) * distance)
    m.give_up = world.time() + 10
  end
  bot.move_to(m.goal, bot.speed() * (node.speed or 0.4))
  return RUNNING
end

function leaves.wait(node)
  local m = memory(node)
  local now = world.time()
  if m.started == nil or now - m.last > 0.5 then m.started = now end
  m.last = now
  bot.stop()
  if now - m.started >= (node.seconds or 1) then
    m.started = nil
    return SUCCESS
  end
  return RUNNING
end

function leaves.stop()
  bot.stop()
  return SUCCESS
end

-- ---- Fighting and conditions ----

function leaves.attack(node)
  if node.attack == nil then error("an attack node needs an attack") end
  local info = bot.attack_info(node.attack)
  if info == nil then error("the bot has no attack '" .. tostring(node.attack) .. "'") end
  if not info.ready then return FAILURE end
  local ok
  if info.kind == "area" then
    ok = bot.attack(node.attack)
  else
    local t = behavior.target()
    if t == nil then return FAILURE end
    ok = bot.attack(node.attack, t)
  end
  return ok and SUCCESS or FAILURE
end

function leaves.in_raid() return bot.raid() ~= nil and SUCCESS or FAILURE end

function leaves.in_water() return bot.in_water() and SUCCESS or FAILURE end

function leaves.health_below(node)
  return bot.health() < bot.max_health() * (node.fraction or 0.3) and SUCCESS or FAILURE
end

function leaves.call(node, dt)
  if node["function"] == nil then error("a call node needs a function") end
  return status(bot.custom(node["function"])(node, dt))
end

-- ---- Running ----

run = function(node, dt)
  local leaf = leaves[node.type]
  if leaf ~= nil then return status(leaf(node, dt)) end
  -- A custom leaf: a global function of that name in the bot's script.
  return status(bot.custom(node.type)(node, dt))
end

behavior.node = run

--- Runs a tree once (one think). Returns the root's status.
function behavior.run(tree, dt)
  if tree == nil then return FAILURE end
  return run(tree, dt or 0)
end
