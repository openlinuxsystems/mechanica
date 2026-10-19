# Your First Mod

This walkthrough makes a whole mod from start to finish. You will make the mod, add a part and its recipe,
add a food item with a Lua script, add a mod script that welcomes players and hands out snacks, translate the
mod into French, change a game rule, check everything, test it with a host and a client, and export a package
to share.

Everything in Mechanica, the base game included, is a mod made of the same files, so each step here is how
vanilla does it too. Two finished example mods in `examples/mods/` hold the same pieces: `example_items`
(food, a gun and a thrown item) and `example_modscript` (a mod script, a translation and a rules patch).
Copy either into your `mods/` folder to try it.

You need only the game. The Part Editor writes the parts and recipes; the scripts and the few other files are
plain text you write in any editor.

## 1. Make the mod

1. Start the game. On the main menu, under **Mod tools**, press **Mod Manager**.
2. Press **New mod…**. Set **Mod id** to `my_mod` and **Name** to `My Mod`, and press **OK**.
3. With `my_mod` selected, press **Edit info…**. Add a **Description** and your name under **Authors**, and
   press **OK**.

You now have `mods/my_mod/` (press **Open mods folder** to see it) with this `mod.json`:

```json
{
  "format_version": 1,
  "id": "my_mod",
  "name": "My Mod",
  "version": "0.1.0",
  "description": "My first mod: a crate, a ration bar and a welcome for new players.",
  "authors": ["Ada"],
  "dependencies": [
    { "id": "vanilla" }
  ]
}
```

The id is permanent: it prefixes every id in your mod (`my_mod:crate`), and saved worlds store those ids.
See [mod.json](packaging-guide.md#2-modjson) for every key.

## 2. Add a part

Close the Mod Manager and open the **Part Editor**.

1. Press **New…**. Set **Mod** to `my_mod`, **Part id** to `crate`, **Name** to `Crate`, **Template** to
   `block`, and press **OK**.
2. On the **Part** tab, set **Mass** to `4` and **Size** to `2, 2, 2` (a 0.5 m cube).
3. On the **Model** tab, set the box to `0.5, 0.5, 0.5`.
4. Press **Export**. The editor writes `parts/crate.json` and `meshes/crate.glb`.

[Getting Started](getting-started.md) goes through every tab of the Part Editor in detail.

## 3. Give the part a recipe

Players craft parts at stations. Still in the Part Editor with the crate open, go to the **Recipes** tab.

1. Press **New recipe**. The output is already the crate.
2. Set **Stations** to `fabricator, portable_fabricator` and **Time** to `4`.
3. Set **Order** to `1790`. Every recipe has an order that puts it in its group in the station list; the
   groups are in [Recipe order](part-data-schema.md#recipe-order). A crate is storage (the 1700s), and vanilla's
   last storage recipe, the Dispenser, is 1780.
4. Under **Inputs**, press **+ Input**, type `scrap_wood` and set the count to `4`.
5. Press **Save recipe**.

It writes `recipes/crate.json`:

```json
{
  "id": "crate",
  "stations": [
    "fabricator",
    "portable_fabricator"
  ],
  "inputs": [
    {
      "item": "scrap_wood",
      "count": 4
    }
  ],
  "outputs": [
    {
      "item": "my_mod:crate",
      "count": 1
    }
  ],
  "time": 4,
  "order": 1790
}
```

In a recipe, a bare id is vanilla's (`scrap_wood`, `fabricator`), so your own items carry your mod id
(`my_mod:crate`).

## 4. Add an item with a script

Items are parts that are never placed: food, weapons, ammo. Their behaviour is a Lua script and their numbers
are the item's `properties`. This ration bar heals whoever eats it.

1. In the Part Editor press **New…**: **Mod** `my_mod`, **Part id** `ration_bar`, **Name** `Ration Bar`,
   **Template** `item`.
2. On the **Part** tab, set **Category** to `consumable`, **Mass** to `0.2` and **Stack size** to `10`.
3. On the **Behaviour** tab, set **Properties** to `{ "health": 25, "welcome_gift": 2, "snack_interval": 600 }`.
   The script reads `health`; the mod script in step 5 reads the other two.
4. On the **Sounds** tab, press **+ Sound name**, name it `eat`, and pick the event `vanilla:eat_crunch`.
5. On the **Script** tab, pick the `empty` template and press **New from template**. The part gets
   `scripts/ration_bar.lua`. Replace the code with this and press **Save & reload**:

```lua
-- Ration Bar: eating one heals properties.health. Item scripts run on the host,
-- one copy per player, while that player holds and uses the item.

-- Before every use: return a string to refuse it, nothing to allow it. It may only read.
function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
end

-- The player used the item (left click with it in hand).
function on_use(target)
  item.consume(1)
  local health = holder.heal(properties.health)
  local eat = item.sound("eat")
  if eat then
    sound.play(eat)
  end
  -- state is this player's own and is saved with the world.
  state.eaten = (state.eaten or 0) + 1
  return { health = health, eaten = state.eaten }
end
```

6. Press **Export**. Then on the **Recipes** tab, press **New recipe** and give it **Stations**
   `fabricator, portable_fabricator`, **Time** `4`, **Order** `2700`, inputs `carrot` 2 and `potato` 1, and
   set the output count to `2`. Food has no vanilla group, so it starts a new one after vanilla's last group
   (Refined materials, 2600). Press **Save recipe**.

The item's file, `parts/ration_bar.json`, now reads:

```json
{
  "id": "ration_bar",
  "name": "Ration Bar",
  "category": "consumable",
  "mass": 0.2,
  "stack_size": 10,
  "script": "scripts/ration_bar.lua",
  "sounds": {
    "eat": "vanilla:eat_crunch"
  },
  "properties": {
    "health": 25,
    "welcome_gift": 2,
    "snack_interval": 600
  }
}
```

To show a model in the player's hand, turn on **Held model** on the **Part** tab and pick a mesh (another mod's
is written `vanilla:meshes/...`). The [Items Tutorial](items-tutorial.md) adds a gun and a thrown item, and
explains aiming, ammo and projectiles.

## 5. Add a mod script

A mod script runs on the host for the whole world, with no part: it listens to events, sets timers and calls
actions. This one gives every new player two ration bars, and every player a snack every ten minutes.

Make `mods/my_mod/scripts/welcome.lua` in a text editor:

```lua
-- Welcome: runs on the host for the whole world (mod.json "scripts").

local ITEM = "ration_bar"

-- The numbers live in data: the Ration Bar's properties.
local function settings()
  return world.item(ITEM, "properties")
end

local function give(peer, count)
  local ok, why = player.give(peer, { { item = ITEM, count = count } })
  if not ok then
    log("Could not give " .. ITEM .. " to player " .. peer .. ": " .. tostring(why))
  end
end

-- Every snack_interval seconds, one for every living player.
local function snack_time()
  local ok, players = actions.call("GetPlayers")
  if not ok then
    return
  end
  for _, p in ipairs(players) do
    if not p.dead then
      give(p.peer, 1)
    end
  end
end

-- Timers end on a reload, so start them from on_create and on_reload.
local function start()
  timer.every(settings().snack_interval, snack_time)
end

-- Listening starts again on every reload, so listen at the top level.
events.listen("players")

function on_create()
  state.greeted = state.greeted or {}
  start()
end

function on_reload()
  start()
end

function on_event(topic, data)
  if topic == "players" and data.kind == "joined" then
    state.greeted = state.greeted or {}
    if not state.greeted[data.name] then
      state.greeted[data.name] = true
      give(data.peer, settings().welcome_gift)
    end
  end
end
```

Then list it in `mod.json`, which you edit by hand (Edit info… keeps the key):

```json
{
  "format_version": 1,
  "id": "my_mod",
  "name": "My Mod",
  "version": "0.1.0",
  "description": "My first mod: a crate, a ration bar and a welcome for new players.",
  "authors": ["Ada"],
  "dependencies": [
    { "id": "vanilla" }
  ],
  "scripts": ["scripts/welcome.lua"]
}
```

- `world.item(id, key)` reads an item's data; a bare id is your mod's.
- `state` is saved with the world, so a player is greeted once, ever.
- `actions.call("GetPlayers")` runs an Action API action as the host and returns `true` and its value.
- The `players` event's data is `{kind, peer, name}`.

The [Mod Scripts Cookbook](mod-scripts.md) has more recipes: chat commands, damage, saving, sharing code with
`require`. The [Lua API Reference](lua-api.md#mod-scripts) lists everything a mod script can call.

## 6. Translate it

A mod ships its own translations in `lang/<code>.json`. The keys are the English text: your part and item names
and descriptions, or any of the game's own strings. Make `mods/my_mod/lang/fr.json`:

```json
{
  "code": "fr",
  "name": "Français",
  "strings": {
    "Crate": "Caisse",
    "Ration Bar": "Barre de ration"
  }
}
```

Players who pick Français in Options, Interface, Language see your names in French. A language the game does not
ship shows up in that list too. See [Translating](translating.md#translating-a-mod-or-the-game-in-a-mod).

## 7. Change a game rule

Balance numbers in `game/data/survival.json`, `fire.json` and `building.json` ship with the game, and a mod
changes their gameplay keys with a merge patch in `rules/`. Make `mods/my_mod/rules/survival.patch.json`:

```json
{
  "respawn_delay": 8
}
```

Players now wait 8 s to respawn instead of 5. Only the keys you write change, and the last mod in load order
wins per key. Some keys are locked (a patch that touches one stops the mod loading). See
[Game rules](packaging-guide.md#game-rules) for which files and keys a mod may patch.

## 8. Check all content

Open the **Mod Manager**, make sure `my_mod` is ticked, and press **Check all content**. It loads every kind of
content the way a world starts (parts, recipes, sounds, world generation, bots, raids, quests, characters and
language files), compiles every Lua script, and lists each problem with its area, file and line. Fix what it
finds and press it again until it says every kind of content loads. Press **Apply** to save the setup.

## 9. Test it with a host and a client

Every feature must work for a client as well as the host, because the host simulates everything and clients
only see the result.

1. Open the **Test Sandbox** from the main menu. Pick `my_mod:crate` in the part list and press **Spawn**.
2. Press **Add local client**. A second copy of the game joins the sandbox in the same window.
3. Press **F6** to switch to the client and spawn a crate from there too: the client asks the host, which
   checks and places it.

For the items and the mod script, play a real world:

1. Press **New Game**, pick **Survival**, and press **Create**.
2. You join as a new player, so the welcome script gives you two Ration Bars. Get hurt, put one in your hotbar
   and left click to eat it.
3. Press Esc, then **Open to LAN**. Start a second copy of the game and press **Join Game**. The second player is
   greeted too, and ten minutes later both get a snack.

Script errors and `log` lines go to the game's log, with the file and line.

## 10. Export a package

1. Open the **Mod Manager**, select `my_mod`, press **Edit info…** and set **Version** to `1.0.0`.
2. Press **Export…** and pick a folder (it starts in `user://mod_packages`). The game checks the mod with every kind
   of content; any error refuses the export and is listed under **Packages**.
3. Share `my_mod-1.0.0.zip`. Players install it with **Install…** in their Mod Manager.

Bump the version for every release, so players can tell which copy they have; players who join a game need the
same mods and versions as the host. The [Packaging Guide](packaging-guide.md) covers versions, dependencies,
conflicts, overrides and the package format.

## Where to go next

- [Mod tools](mod-tools.md): every tool on the main menu.
- [Items Tutorial](items-tutorial.md): a gun with ammo and a thrown item.
- [Mod Scripts Cookbook](mod-scripts.md): more mod scripts.
- [Part Data Schema Reference](part-data-schema.md) and [Lua API Reference](lua-api.md).
- [Bot Tutorial](bot-tutorial.md), [Quests and Dialogue](quests.md), [World Generation](world-generation.md) and
  [Player Characters](characters.md).
