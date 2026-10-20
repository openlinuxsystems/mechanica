# Mod Scripts Cookbook

A mod script is Lua that runs on the host for the whole world, with no part: game rules, a welcome for new
players, a timer, a chat command, a reaction to something that happened. This page is a set of short recipes.
Each one is a complete script you can drop into a mod. The [Lua API Reference](lua-api.md#mod-scripts) lists
everything a mod script can call.

The example mod `examples/mods/example_modscript` is a finished mod built from these recipes: a welcome script
that uses events, a timer, `actions.call`, a shared library and saved state, plus a French translation and a
rules patch.

## How mod scripts run

List each script in your `mod.json` under `scripts`. Paths are relative to the mod root, or `other_mod:path`
for a script in a mod you depend on:

```json
{
  "id": "my_mod",
  "name": "My Mod",
  "version": "1.0.0",
  "dependencies": [{ "id": "vanilla" }],
  "scripts": ["scripts/welcome.lua", "scripts/chat_commands.lua"]
}
```

- **The host runs them**, in mod load order and then in the order listed, after the mods load. Clients run no
  scripts; they see what the host does.
- **Each script has its own globals** and its own `state`, a table saved with the world. Keep plain data in it:
  numbers, strings, booleans and tables of them.
- **Hooks**: `on_create()` once when the script first starts, `on_reload()` instead of it after a hot reload,
  `on_update(dt)` every physics step, `on_event(topic, data)` for the events it listens to, `on_hit(landing)` for
  its projectiles, and `on_destroy()` when it stops.
- **The top level runs on every start and reload**, before `on_create` or `on_reload`.
- **Libraries**: `world`, `player`, `sound.play` (with a position), `combat`, `farm`, `fire`, `events`, `actions`,
  `timer`, `require`, `log` and `print`. `part.*` and `creation.*` are errors here: there is no part.
- **Errors stop the script** with the file and line in the log. Fix it and save; the host reloads a changed script
  within half a second, keeping its `state`.
- **Bare ids are your mod's**: `world.item("trail_mix")` and `player.give(peer, {{ item = "trail_mix" }})` mean
  `my_mod:trail_mix`. Write `vanilla:carrot` for vanilla's.

There is no in-game editor for mod scripts; edit them in any text editor. The Mod Manager's **Check all
content** compiles every script and lists syntax errors with their line.

## Welcome new players

Listen to the `players` topic. Its data is `{kind, peer, name}`, with `kind` `joined` or `left`.

```lua
events.listen("players")

function on_event(topic, data)
  if data.kind == "joined" then
    state.greeted = state.greeted or {}
    if not state.greeted[data.name] then
      state.greeted[data.name] = true
      player.give(data.peer, { { item = "vanilla:carrot", count = 3 } })
      print("Welcomed " .. data.name)
    end
  end
end
```

- Call `events.listen` at the top level (or in `on_create` and `on_reload`): a reload starts with no topics.
- `state` is saved with the world, so each player name is greeted once, ever. Peer numbers change every session;
  names don't.
- `player.give` puts every item in the player's inventory, or nothing when they don't all fit. It returns `true`,
  or `false` and why.

## Do something every few minutes

`timer.every(seconds, fn)` calls `fn(id)` until it is cancelled; `timer.after(seconds, fn)` calls it once. Timers
count game time and are not saved, and a reload ends them, so start them from both `on_create` and `on_reload`.

```lua
local function tick(id)
  state.minutes = (state.minutes or 0) + 1
  print(state.minutes .. " minutes played in this world")
end

local function start()
  timer.every(60, tick)
  timer.after(5, function() print("The world is five seconds old in this session.") end)
end

function on_create()
  start()
end

function on_reload()
  start()
end
```

`timer.cancel(id)` stops a timer and `timer.count()` says how many of this script's timers are waiting. A script
runs at most `max_timers_per_script` at once.

## Keep the numbers in data

Balance numbers belong in data, never in code. A mod script has no `properties` of its own, so read them from one
of your items with `world.item(id, key)`:

```json
"properties": { "health": 15, "welcome_gift": 3, "snack_interval": 600 }
```

```lua
local function settings()
  return world.item("trail_mix", "properties")
end

function on_create()
  timer.every(settings().snack_interval, function()
    print("Snack time")
  end)
end
```

`example_modscript` keeps its numbers on its Trail Mix item this way.

## Call an action

Anything a player or agent can do goes through the Action API, and a mod script can call it too with
`actions.call(name, args)`. It runs the action as the host and returns `true` and the action's value, or `false`
and the reason. The arguments are a table in the action's snake_case JSON form. Mod scripts may call every action
except the ones that move a Creation (`ApplyForce`, `SetBearingTarget`, `SetPistonTarget`).

Read every player, then give the living ones a snack:

```lua
local function snack_time()
  local ok, players = actions.call("GetPlayers")
  if not ok then
    return
  end
  for _, p in ipairs(players) do
    if not p.dead then
      player.give(p.peer, { { item = "vanilla:carrot", count = 1 } })
    end
  end
end

function on_create()
  timer.every(600, snack_time)
end
```

Each player in the list has `peer`, `name`, `position` (`{x, y, z}` as a list), `health`, `max_health`, `dead` and
more. Change the world, here by letting a Grazer wander in near the first player every twenty minutes:

```lua
local function visitor()
  local ok, players = actions.call("GetPlayers")
  if not ok or #players == 0 then
    return
  end
  local at = players[1].position
  local spawned, why = actions.call("SpawnBot", { bot = "vanilla:grazer", position = { at[1] + 8, at[2], at[3] } })
  if not spawned then
    log("No visitor: " .. tostring(why))
  end
end

function on_create()
  timer.every(1200, visitor)
end
```

`actions.list()` returns every action name `actions.call` accepts. The [AI endpoint](ai-endpoint.md) docs describe
the actions and their arguments; they are the same calls an outside agent makes.

## A chat command

The `chat` topic's data is the chat line: `{id, peer, name, text}`.

```lua
events.listen("chat")

local COOLDOWN = 300 -- seconds; better kept in an item's properties, as above

function on_event(topic, data)
  if data.text ~= "!kit" then
    return
  end
  state.kits = state.kits or {}
  local last = state.kits[data.name]
  if last and world.time() - last < COOLDOWN then
    return
  end
  local ok = player.give(data.peer, { { item = "vanilla:scrap_wood_block", count = 40 } })
  if ok then
    state.kits[data.name] = world.time()
  end
end
```

`world.time()` is the seconds the world has been simulated. Saves keep it, so times stored in `state` stay
meaningful after a load.

## React to damage and death

`hurt` is `{peer, amount, cause}` each time a player takes damage. `vitals` is the player's vitals whenever they
change: `{peer, health, max_health, dead, respawn_in, has_bed, mode, ...}`.

```lua
events.listen("hurt")
events.listen("vitals")

local down = {}

function on_event(topic, data)
  if topic == "hurt" then
    state.damage = (state.damage or 0) + data.amount
  elseif topic == "vitals" then
    if data.dead and not down[data.peer] then
      down[data.peer] = true
      local ok, who = actions.call("GetPlayer", { peer = data.peer })
      if ok then
        state.deaths = state.deaths or {}
        state.deaths[who.name] = (state.deaths[who.name] or 0) + 1
        print(who.name .. " has died " .. state.deaths[who.name] .. " time(s)")
      end
    elseif not data.dead then
      down[data.peer] = nil
    end
  end
end
```

`down` is a plain local, not `state`: it only needs to last this session, and it starts empty again on a reload.

## Share code with require

Put shared functions in a file of their own and `require` it. The file runs once per mod and every script that
requires it gets the same result, usually a table of functions. It runs in its own environment, so it has no
`state` or `properties` of its own: pass in what it needs.

```lua
-- scripts/lib/gifts.lua
local M = {}

function M.give(peer, item, count)
  local ok, why = player.give(peer, { { item = item, count = count } })
  if not ok then
    log("Could not give " .. count .. " " .. item .. " to player " .. peer .. ": " .. tostring(why))
  end
  return ok
end

return M
```

```lua
-- scripts/welcome.lua
local gifts = require("scripts/lib/gifts.lua")

events.listen("players")

function on_event(topic, data)
  if data.kind == "joined" then
    gifts.give(data.peer, "vanilla:carrot", 3)
  end
end
```

A mod can require its own files and those of the mods in its `dependencies` (`"vanilla:scripts/lib/x.lua"`). A
library file is not listed in `mod.json` `scripts`; only the scripts that run are. When you change a library,
reload (save) the script that requires it.

## Saved state

- A mod script's `state` is saved with the world, by script, and comes back when the world loads. Code that
  sets defaults should keep what is there (`state.greeted = state.greeted or {}`), and a hook that reads `state`
  should not assume `on_create` set it up, as `on_event` in the welcome recipe shows.
- A hot reload keeps `state` and runs `on_reload` instead of `on_create`.
- Locals, timers and listened topics are not saved; set them up again in `on_create` and `on_reload` (or at the
  top level).
- Tables keyed by strings save as JSON objects, and lists as JSON arrays. Functions, `vec3` metatables and
  userdata are not plain data: store `{x, y, z}` numbers instead.

## Event topics

`events.topics()` returns every topic a script may listen to. An unknown topic is an error.

| Topic | When | Data |
|-------|------|------|
| `players` | A player joined or left the world. | `{kind, peer, name}`, `kind` is `joined` or `left` |
| `chat` | A chat line was said. | `{id, peer, name, text}` |
| `hurt` | A player took damage. | `{peer, amount, cause}` |
| `vitals` | A player's vitals changed (health, death, respawn). | `{peer, health, max_health, dead, respawn_in, has_bed, mode, ...}` |
| `drop` | An item pile appeared, changed or was picked up. | `{kind, drop}`, `kind` is `changed` or `removed` |
| `projectiles` | A projectile was launched or landed. | `{kind, projectile}`, `kind` is `launched` or `landed` |
| `bots` | A bot spawned, was removed, attacked, or destroyed a crop. | `{kind, ...}` with the bot or attack |
| `raid` | A raid started, advanced or ended. | the raid's state |
| `quest` | Quest progress changed. | the quest state |
| `dialogue` | A quest character spoke. | `{quest, speaker, text, choices}` |
| `fire` | A fire went out. | `{kind, fire}` |
| `seats` | Someone sat down or stood up. | `{seat, seated}` |
| `world` | The world was saved or loaded. | `{kind, ...}`, `kind` is `saved` or `loaded` |
| `actions` | A player, bot or agent made a state-changing action (scripts' own are left out). | `{peer, source, action, args, ok, code, message, value}` |
| `mods` | Mod content was validated, reloaded, installed or uninstalled. | depends on what happened |
| `reports` | An agent filed a note, recommendation or bug report. | the report's summary |

Events wait until the start of the next step and arrive in order, at most `max_queued_events` of them. Part
scripts may listen too.

## Testing

1. Turn the mod on in the Mod Manager and press **Check all content** to compile every script.
2. Start or load a world and watch the game's log: `print` and `log` lines and errors appear there with the script
   and line.
3. Change the script and save it while the world runs: the host reloads it, keeping `state`.
4. For anything about players joining, open the world to the LAN (Esc, **Open to LAN**) and join from a second copy
   of the game, so the host and a client both see the result.

## Where to go next

- [Lua API Reference](lua-api.md): every function and hook.
- [Your first mod](first-mod.md): a mod script as part of a whole mod.
- [Items Tutorial](items-tutorial.md): item scripts, with `on_hit` and per-player state.
- [Packaging Guide](packaging-guide.md#game-rules): changing balance numbers with rules patches instead of code.
