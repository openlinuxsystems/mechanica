-- Sentry Drone (the Bot Tutorial's example). Its behaviour tree is data in bots/sentry_drone.json; this
-- script adds hooks and one custom leaf, `patrol`, that the tree uses by name. Everything goes through the
-- bot.* and world.* primitives, so the host checks every move and shot like a player's.

-- Runs once when the drone appears. `state` survives a script reload; `properties` comes from the bot file.
function on_spawn()
  state.next_point = 1
  state.hits_taken = 0
  log("sentry drone " .. bot.id() .. " on patrol")
end

-- Called with how much health it just lost.
function on_damaged(amount)
  state.hits_taken = state.hits_taken + 1
  bot.alert()
end

function on_death()
  log("sentry drone " .. bot.id() .. " destroyed after " .. state.hits_taken .. " hits")
end

-- A custom leaf: walk the square of waypoints in properties.patrol (offsets from home, in m).
-- Running while it walks; it never fails, so it works as the tree's last resort.
function patrol(node, dt)
  local points = properties.patrol
  if points == nil or #points == 0 then
    bot.stop()
    return "success"
  end
  local offset = points[state.next_point]
  local home = bot.home()
  local goal = vec3(home.x + offset[1], home.y, home.z + offset[2])
  local away = goal - bot.position()
  away.y = 0
  if away:length() < 0.75 then
    state.next_point = state.next_point % #points + 1
  end
  bot.move_to(goal, bot.speed() * (node.speed or 0.5))
  return "running"
end
