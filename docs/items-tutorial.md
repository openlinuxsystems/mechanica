# Items Tutorial

This tutorial makes three handheld items: a food, a gun with ammo, and a thrown bomb. Along the way it covers
what an item is, the item hooks, aiming, the model in the player's hand, the hotbar counter, projectiles and
what a player's item `state` keeps.

The finished items are in the example mod `examples/mods/example_items`: the Ration Bar, the Pea Shooter with
its Dried Pea ammo, and the Fizz Bomb, each with a recipe. Copy the folder into your `mods/` folder to play with
them, and open their files beside this page. If you have not made a mod yet, start with
[Your first mod](first-mod.md).

## What an item is

An item is a part that is never placed on the build grid. It has an item category (`resource`, `consumable`,
`tool` or `weapon`), lives in inventories and on the hotbar, and is made in the Part Editor from the `item`
template like any other part. Picking a hotbar slot (1 to 0, or the mouse wheel) puts its item in your hand.

An item that does something has a Lua `script`. The script is the item's behaviour, and its numbers (damage,
healing, speed) are the item's `properties`, so balance stays in data and one script can serve many items.
Vanilla's food, guns, seeds and buckets all work this way; their scripts are in `mods/vanilla/scripts/`.

How item scripts run:

- **On the host only.** Clients send their use as the `UseItem` action, the host checks it and runs the script,
  and every client sees the result.
- **One copy per player and item type.** Each player gets their own copy, with their own `state`, the first time
  they take the item in hand or use it.
- **`state` is saved with the world** under the player, and comes back when they return. A gun's loaded rounds or
  a count of meals eaten carry on across sessions. Keep it plain data: numbers, strings, booleans and tables of
  them.
- **The check hooks only read.** `can_use` and `can_reload_item` decide whether something is allowed; changing the
  world in them is an error.

The [Lua API Reference](lua-api.md#item-scripts) lists every function; the hooks used here are:

| Hook | When |
|------|------|
| `on_equip()` | The player took the item in hand. |
| `on_unequip()` | The item left the player's hand. |
| `can_use(target)` | Before every use. Return a string to refuse it, with that reason; return nothing to allow it. |
| `on_use(target)` | The player used the item (left click). `target` is `{from, direction, aiming, creation?, part?}`. |
| `on_aim(on)` | The player started (`true`) or stopped (`false`) holding right click to aim. |
| `on_hit(landing)` | A projectile the script launched landed: `{id, position, hit?, data}`. |
| `can_reload_item()`, `on_reload_item()` | Before and on Reload (**R**). An item without `on_reload_item` cannot be reloaded. |
| `on_update(dt)` | Every step while a living player holds the item. |

## 1. Food: the Ration Bar

Make the item in the Part Editor: **New…** with the `item` template, then on the **Part** tab set **Category**
to `consumable`, and on the **Behaviour** tab set **Script** and **Properties**. The part file it writes,
`parts/ration_bar.json`:

```json
{
  "id": "ration_bar",
  "name": "Ration Bar",
  "description": "Pressed oats and dried carrot. Eat it to heal 25 health.",
  "category": "consumable",
  "mass": 0.2,
  "stack_size": 10,
  "visual": {
    "color": "#c49a5a"
  },
  "script": "scripts/ration_bar.lua",
  "sounds": {
    "eat": "vanilla:eat_crunch"
  },
  "properties": {
    "health": 25
  }
}
```

`sounds` binds the name `eat` to vanilla's crunch, so the script plays it by name and the sound id stays in
data. The script, `scripts/ration_bar.lua`:

```lua
function can_use(target)
  if holder.health() >= holder.max_health() then
    return "You are already at full health."
  end
end

function on_use(target)
  item.consume(1)
  local health = holder.heal(properties.health)
  local eat = item.sound("eat")
  if eat then
    sound.play(eat)
  end
  -- state is per player and saved with the world, so this count carries on across sessions.
  state.eaten = (state.eaten or 0) + 1
  return { health = health, eaten = state.eaten }
end
```

- `holder` is the player holding the item: `holder.health()`, `holder.heal(amount)`, `holder.eye()`, `holder.look()`.
- `item` is the item in hand: `item.consume(count)` uses some up, `item.replace(id)` turns one into another item
  (a full bucket becoming an empty one).
- What `on_use` returns is the `UseItem` action's result, which agents and tests read.

If eating something should only ever heal, vanilla's `scripts/food.lua` already does this; an item can use it
with `"script": "vanilla:scripts/food.lua"` and its own `health` property.

## 2. A gun: the Pea Shooter

The Pea Shooter fires Dried Peas. Each shot is a projectile that flies in an arc and hits what it lands on. It
holds 12 shots, Reload (**R**) loads it again from the peas you carry, and right click aims.

### The ammo

The ammo is a plain `resource` item with no script, `parts/dried_pea.json`:

```json
{
  "id": "dried_pea",
  "name": "Dried Pea",
  "description": "Ammo for the Pea Shooter.",
  "category": "resource",
  "mass": 0.01,
  "stack_size": 100,
  "visual": {
    "color": "#8fae4a"
  }
}
```

### The gun's data

```json
{
  "id": "pea_shooter",
  "name": "Pea Shooter",
  "description": "Fires dried peas in an arc. Holds 12; Reload refills it from the peas you carry.",
  "category": "weapon",
  "mass": 1.5,
  "stack_size": 1,
  "visual": {
    "color": "#5a7a3a"
  },
  "script": "scripts/pea_shooter.lua",
  "sounds": {
    "fire": "vanilla:blaster_fire",
    "reload": "vanilla:blaster_reload"
  },
  "aim": {
    "zoom": 0.7
  },
  "held": {
    "mesh": "vanilla:meshes/decor/small_pipe_short.glb",
    "offset": [0, 0.02, -0.08],
    "scale": 0.6
  },
  "properties": {
    "ammo": "dried_pea",
    "damage": 8,
    "push": 4,
    "interval": 0.3,
    "speed": 40,
    "aimed_speed": 60,
    "lifetime": 3,
    "magazine": 12,
    "reload": 1.2
  }
}
```

Two built-in sections do work without any script:

- **`aim`** (the Part tab's **Aims**, Survival section): while the player holds right click, the field of view is
  multiplied by `zoom` and the crosshair tightens. The script sees `target.aiming` and `holder.aiming()`. See
  [survival](part-data-schema.md#survival).
- **`held`** (the Part tab's **Held model**): the model the player carries in the right hand, seen by every
  player. Here it borrows a vanilla pipe; `offset` moves it from the palm in metres, `rotation` turns it in
  degrees, and `scale` sizes it. Without `held`, nothing shows in the hand. See [held](part-data-schema.md#held).

`ammo` is a bare id, which in an item script means an item of the script's own mod (`example_items:dried_pea`).

### The script

`scripts/pea_shooter.lua` keeps how many peas are loaded in `state.loaded`, and when a reload finishes in
`state.ready_at` (on the world clock, `world.time()`):

```lua
local JITTER = 0.8

local function carried()
  return inventory.count(properties.ammo)
end

local function loaded()
  if state.loaded == nil then
    state.loaded = math.min(properties.magazine, carried())
  end
  return state.loaded
end

local function busy()
  return state.ready_at and math.max(0, state.ready_at - world.time()) or 0
end

local function show()
  item.counter({ value = loaded(), max = properties.magazine, ammo = properties.ammo, busy = busy() })
end

function on_equip()
  show()
end
```

**`item.counter`** sets what the holder's hotbar slot and HUD show: `value` rounds loaded out of `max`, the `ammo`
item (the HUD counts how many the player carries), and `busy` seconds while it reloads. Only the holder is sent
it, so call it whenever the numbers change: on equip, after a shot and after a reload.

Firing:

```lua
function can_use(target)
  if state.last_shot and world.time() - state.last_shot < properties.interval * JITTER then
    return "The " .. item.name() .. " is not ready yet."
  end
  if busy() > 0 then
    return "The " .. item.name() .. " is still reloading."
  end
  if loaded() < 1 then
    return "The " .. item.name() .. " is empty. Reload it."
  end
end

function on_use(target)
  state.last_shot = world.time()
  inventory.take(properties.ammo, 1)
  state.loaded = math.min(loaded() - 1, carried())
  show()
  local fire = item.sound("fire")
  if fire then
    sound.play(fire)
  end
  local speed = target.aiming and properties.aimed_speed or properties.speed
  local id = combat.projectile(target.from, target.direction * speed, {
    lifetime = properties.lifetime,
    data = { damage = properties.damage, x = target.direction.x, y = target.direction.y, z = target.direction.z },
  })
  return { projectile = id }
end
```

- The interval check allows a little less than `interval` (`JITTER`), because uses arrive over the network.
- `target.from` is the player's eyes and `target.direction` where they aim; the host checks the direction is close
  to where the player really looks.
- **`combat.projectile(from, velocity, options)`** launches a projectile at `velocity` m/s. It flies on the host
  under gravity (`options.gravity`, default `world.gravity()`) for at most `lifetime` seconds, every client draws it
  along its arc, and it deals no damage by itself. `options.data` is plain data handed back in `on_hit`. It returns
  the projectile's id, or `false` and why when too many are in flight.

Landing:

```lua
function on_hit(landing)
  if landing.hit then
    local d = landing.data
    combat.hit(landing.hit, d.damage, vec3(d.x, d.y, d.z) * properties.push)
  end
end
```

`landing` is `{id, position, hit?, data}`. `hit` is missing when the projectile flew its whole lifetime without
touching anything. When it is there, it is a hit table (`kind` is `player`, `part`, `object`, `bot` or `ground`)
that **`combat.hit(hit, damage, push?, kind?)`** damages; a part's Body also gets `push`, an impulse in N·s. The
shot's direction travelled in `data` as plain numbers, so it is rebuilt with `vec3`.

Damage follows the world's rules: nothing hurts players in Creative, and with friendly fire off (the default) a
shot or splash never hurts another player (a player's own splash still hurts them). Bots, parts and world
objects still take it.

Reloading:

```lua
function can_reload_item()
  if busy() > 0 then
    return "The " .. item.name() .. " is already reloading."
  end
  if carried() < 1 then
    return "You have no " .. item.name(properties.ammo) .. " to load."
  end
  if loaded() >= math.min(properties.magazine, carried()) then
    return "The " .. item.name() .. " is already loaded."
  end
end

function on_reload_item()
  state.loaded = math.min(properties.magazine, carried())
  state.ready_at = world.time() + properties.reload
  local reload = item.sound("reload")
  if reload then
    sound.play(reload)
  end
  show()
end
```

Because `state` is saved per player, a player who leaves with 5 peas loaded comes back to 5.

For a hitscan gun (an instant ray instead of a flying shot), use `combat.hitscan(from, direction, range, damage,
impulse)`, or `combat.cast` and `combat.hit` separately; `combat.pellets(direction, count, spread)` gives the
directions of a shotgun's pellets. Vanilla's `scripts/potato_gun.lua` does all of these from properties alone.

## 3. A thrown item: the Fizz Bomb

The Fizz Bomb is its own ammo: each throw uses one. The player holds right click to wind up and lets go to throw.

```json
{
  "id": "fizz_bomb",
  "name": "Fizz Bomb",
  "description": "Hold right click to wind up and let go to throw. It bursts where it lands, hurting everything close by.",
  "category": "weapon",
  "mass": 0.3,
  "stack_size": 10,
  "visual": {
    "color": "#4ab0c0"
  },
  "script": "scripts/fizz_bomb.lua",
  "sounds": {
    "explode": "vanilla:popper_explode"
  },
  "aim": {
    "zoom": 1,
    "fire_on_release": true
  },
  "properties": {
    "damage": 35,
    "radius": 3,
    "impulse": 200,
    "speed": 10,
    "aimed_speed": 16,
    "fuse": 2.5,
    "interval": 0.8
  }
}
```

`aim.fire_on_release` uses the item when right click is let go, as well as on left click while aiming, and
`zoom` 1 keeps the view as it is. The script, `scripts/fizz_bomb.lua`:

```lua
function can_use(target)
  if state.last_throw and world.time() - state.last_throw < properties.interval * 0.8 then
    return "You are still winding up."
  end
end

function on_use(target)
  state.last_throw = world.time()
  item.consume(1)
  local speed = target.aiming and properties.aimed_speed or properties.speed
  local direction = (target.direction + vec3(0, 0.2, 0)):normalized()
  local id = combat.projectile(holder.eye(), direction * speed, { lifetime = properties.fuse })
  return { projectile = id }
end

function on_hit(landing)
  local hits = combat.splash(landing.position, properties.radius, properties.damage, properties.impulse)
  local boom = item.sound("explode")
  if boom then
    sound.play(boom, landing.position)
  end
  state.hits = (state.hits or 0) + #hits
end
```

- A throw from right click's release still has `target.aiming` true, so a wound-up throw flies at `aimed_speed`.
- Tilting the direction up a little (`+ vec3(0, 0.2, 0)`) makes a lob.
- `lifetime` is the fuse: `on_hit` runs where the bomb lands, or in the air when the fuse runs out (then `hit` is
  missing, but `position` is still where it is).
- **`combat.splash(at, radius, damage, impulse)`** hurts every player, part and world object within `radius` m,
  falling off from `damage` at the centre to nothing at the edge, and pushes parts away. It returns the list of hits.
- `sound.play(id, at)` plays at a world point; without `at`, an item's sound plays at its holder's eyes.

To react to the wind-up itself (a sound, a charge), define `on_aim(on)`: it runs with `true` when the player starts
holding right click and `false` when they let go.

## 4. Recipes

Each item has a recipe in `recipes/`, made on the Part Editor's **Recipes** tab. Every recipe has an `order` that
puts it in its group in the station list ([Recipe order](part-data-schema.md#recipe-order)): the Pea Shooter goes
after vanilla's Popper in Weapons (`2550`), its peas and the Fizz Bomb follow (`2560`, `2570`), and food starts a
new group after vanilla's last one (`2700`).

```json
{
  "id": "pea_shooter",
  "stations": [
    "fabricator"
  ],
  "inputs": [
    {
      "item": "scrap_metal",
      "count": 2
    },
    {
      "item": "component_kit",
      "count": 1
    }
  ],
  "outputs": [
    {
      "item": "example_items:pea_shooter",
      "count": 1
    }
  ],
  "time": 8,
  "order": 2550
}
```

In a recipe, bare ids are vanilla's; your own items carry your mod id.

## 5. Try it

1. Copy `examples/mods/example_items` into your `mods/` folder, open the **Mod Manager**, tick it and press
   **Check all content**, then **Apply**.
2. Start a **Creative** world, open the inventory (**I**) and drag the items onto your hotbar, or craft them at a
   Fabricator in Survival.
3. Pick the Pea Shooter's slot: it shows 12 / 12 and the peas you carry. Fire, aim with right click, and press
   **R** to reload.
4. Hold right click with a Fizz Bomb and let go to throw it.
5. To test with a client, press Esc, **Open to LAN**, and join from a second copy of the game; or use the
   **Test Sandbox**'s **Add local client**.

The scripts' errors and `log` lines go to the game's log. Edit a script while the world runs and the host reloads
it within half a second, keeping every player's `state`.

## Where to go next

- [Lua API Reference](lua-api.md#item-scripts): every item function, and `farm` and `fire` for seeds and buckets.
- [Part Data Schema Reference](part-data-schema.md#survival): the vanilla item scripts' properties.
- [Mod Scripts Cookbook](mod-scripts.md): scripts that run for the whole world.
