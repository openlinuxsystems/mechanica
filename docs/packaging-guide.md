# Packaging Guide

How to lay out, version, package and share a Mechanica mod, and how players manage the mods they
install. Vanilla content is packaged exactly the same way: open `mods/vanilla/` next to the game
executable to see a complete example.

## 1. Folder layout

A mod is a plain folder inside `mods/`. The folder name is the mod id.

```
mods/
  better_wheels/
    mod.json            required: identity, version, dependencies, conflicts
    parts/              part and item definitions (*.json, any subfolders)
      wheels/sport_wheel.json
    overrides/          changes to other mods' content, one folder per mod id: part files, recipes/, bots/,
                        quests/, pois/, characters/, *.patch.json merge patches and remove.json
      vanilla/wheel.json
    scripts/            Lua behaviour (*.lua): part and item scripts, mod scripts listed in mod.json, require()d modules
    meshes/             glTF models (*.glb, *.gltf)
    textures/           icons and textures (*.png)
    sounds/             audio (*.ogg, *.wav)
    sounds/**/*.json    sound events, see sound-events.md
    recipes/            crafting recipes (*.json), see part-data-schema.md
    world/worldgen.json world generation rules; the last mod in load order that ships one wins
    world/worldgen.patch.json  additions and changes to it that stack across mods
    pois/               POI prefabs (*.json), see world-generation.md
    bots/               bot types (*.json), see bot-tutorial.md
    raids/raids.json    the raid table; the last mod in load order that ships one wins
    raids/raids.patch.json  additions and changes to it that stack across mods
    quests/             quests and dialogue (*.json), see quests.md
    characters/         player characters and skins (*.json), see characters.md
    rules/              game rules: survival.patch.json, fire.patch.json, building.patch.json (see Game rules)
    lang/               string tables (<code>.json) for the mod's text and the game's, see translating.md
```

Rules the loader enforces:

- The mod id is lowercase snake_case (a letter, then letters, digits or `_`) and equals the folder name.
- Data files are JSON. Comments (`//`, `/* */`) and trailing commas are allowed.
- Asset paths in data are relative to the mod root: `"mesh": "meshes/sport_wheel.glb"`.
  To use a file from another mod, write `"other_mod:meshes/x.glb"` and list `other_mod` as a
  dependency. Absolute paths, `..`, backslashes, `res://` and URLs are rejected.
- Only the formats above are read. Do not ship Godot-imported files (`.import`, `.tres`, `.mesh`).

## 2. mod.json

```json
{
  "format_version": 1,
  "id": "better_wheels",
  "name": "Better Wheels",
  "version": "1.2.0",
  "description": "Sport and off-road wheels, and a grippier vanilla wheel.",
  "authors": ["Ada"],
  "dependencies": [
    { "id": "vanilla", "version": "^0.1.0" }
  ],
  "conflicts": [
    { "id": "old_wheel_pack" }
  ]
}
```

| Key | Required | Meaning |
|-----|----------|---------|
| `format_version` | no | Data format of the mod's files. Omit it or write `1`. |
| `id` | yes | Mod id, equal to the folder name. Every part's game-wide id is `id:part_id`. |
| `name` | yes | Name shown in the Mod Manager. |
| `version` | yes | Semantic version, `major.minor.patch`, optionally `-prerelease` (`1.0.0-beta.2`). |
| `description` | no | One paragraph shown in the Mod Manager. |
| `authors` | no | List of names. |
| `dependencies` | no | Mods that must be installed, enabled and loaded before this one. |
| `conflicts` | no | Mods this one cannot run with. |
| `scripts` | no | Mod scripts that run on the host for the whole world, in order: game rules, events, timers ([Lua API: Mod scripts](lua-api.md#mod-scripts)). Paths like `scripts/rules.lua`; each file must exist. |

Unknown keys are kept but produce a warning, with a suggestion when they look like a typo.

## 3. Versioning

Use [semantic versioning](https://semver.org):

- **Patch** (`1.2.0` → `1.2.1`): fixes that change no ids and break no Creations.
- **Minor** (`1.2.1` → `1.3.0`): new parts or new optional behaviour.
- **Major** (`1.3.0` → `2.0.0`): anything that can break a mod depending on yours or a saved Creation,
  such as removing or renaming a part id, changing a part's size or sockets, or removing an asset other
  mods reference.

Never reuse a part id for a different part. Saved Creations store part ids, so renaming one breaks
every save that uses it.

### Version ranges

`dependencies` and `conflicts` entries take an optional `version` range. Omitted means any version.
Terms separated by spaces must all hold.

| Range | Accepts |
|-------|---------|
| `*` | any version |
| `1.2.0` | exactly 1.2.0 |
| `>=1.2.0 <2.0.0` | 1.2.0 up to, not including, 2.0.0 |
| `^1.2.0` | 1.2.0 and later with the same major (for `^0.x.y`, the same minor) |
| `~1.2.0` | 1.2.0 and later with the same minor |

Depend on the lowest version you tested with, usually with `^`: `{ "id": "vanilla", "version": "^0.1.0" }`.

## 4. Dependencies

A dependency guarantees three things: the mod is installed and enabled, its version matches the
range, and it loads before yours. You must also list a mod as a dependency before you can reference its
assets or override its parts.

If a dependency is missing, disabled, the wrong version, broken, or part of a cycle, your mod does not
load and the Mod Manager says why. Mods that depend on yours do not load either; everything else does.

## 5. Conflicts

List a mod under `conflicts` when the two cannot work together, for example two mods that replace the
same system in incompatible ways. A version range limits it to some versions:

```json
"conflicts": [{ "id": "old_wheel_pack", "version": "<2.0.0" }]
```

When a conflicting mod is enabled, **your mod is the one left out** (error `MD0112`), and the Mod
Manager tells the player to disable one of the two. Prefer overrides or a dependency over a conflict
whenever your mod can coexist with the other one.

## 6. Overriding another mod's parts

To change a part another mod defines (including vanilla), put a complete part definition with the same
`id` in `overrides/<mod_id>/`. It replaces the original everywhere, and the part keeps its game-wide id
(`vanilla:wheel`), so saved Creations keep working.

```
mods/better_wheels/overrides/vanilla/wheel.json   replaces vanilla:wheel
```

- The target mod must be in your `dependencies` (error `MD0203` otherwise), and the part must exist in
  it (error `MD0204`).
- An override is a whole definition, validated like any part. In the Part Editor, open the original
  and press **Override in my mod…**: it copies the part into the right folder, points its assets back at
  the original mod and adds the dependency for you.
- Relative asset paths in an override resolve inside **your** mod. To keep the original mesh, write
  `"mesh": "vanilla:meshes/wheel.glb"`.
- When two mods override the same part, the one later in the load order wins and the Mod Manager shows
  a warning (`MD0205`). If your mod deliberately builds on another mod's override, depend on that mod:
  then yours always loads later and no warning is shown.

### Overriding recipes

A recipe is replaced the same way: put a complete recipe with the same `id` in
`overrides/<mod_id>/recipes/`. It keeps its game-wide id (`vanilla:chest`), so crafting queues and
saves keep working.

```
mods/cheap_chests/overrides/vanilla/recipes/chest.json   replaces the recipe vanilla:chest
```

- The same dependency rule applies (`MD0203`), and the recipe must exist in that mod (`MD0703`).
- It is checked like any recipe (`MD0701`). A recipe override with errors is left out and the original
  recipe stays.
- When two mods override the same recipe, the later one in the load order wins with a warning
  (`MD0704`), unless one depends on the other.

The **Recipe Editor** (main menu, Mod tools) does this without writing JSON. It lists every recipe of every installed
mod, with which version wins in load order. Pick **Mod to edit**, then pick any recipe: **Override** starts your mod's
version of it in `overrides/<mod_id>/recipes/`, **Copy to mod** starts a new recipe of your own from it, and
**New recipe** starts a blank one. **Save recipe** checks it exactly as loading would and lists the problems; your
mod's own recipes and overrides open for editing and **Delete** removes them after asking. Add the owner to your
`dependencies` (Mod Manager, **Edit info…**) so the override loads.

### Overriding bots, quests, POIs and characters

The same works for every other kind of content: put a complete file with the same id in the matching
folder of `overrides/<mod_id>/`.

```
mods/tough_bots/overrides/vanilla/bots/scuttler_red.json        replaces the bot vanilla:scuttler_red
mods/my_story/overrides/vanilla/quests/first_farm.json          replaces the quest vanilla:first_farm
mods/my_world/overrides/vanilla/pois/camp.json                  replaces the POI vanilla:camp
mods/my_look/overrides/vanilla/characters/scrapper_male.json    replaces the character vanilla:scrapper_male
```

A quest's id is its file name, as in `quests/`; the others use their `id`. Relative asset paths resolve
inside your mod, as for part overrides.

### Patching instead of replacing

A whole-file override stops picking up the original mod's fixes. To change only a few values, write a
**merge patch** instead: a file ending in `.patch.json`, named for the entry it changes (or with an `id`),
holding only the keys to change. It works in every override folder, parts included:

```
mods/tough_bots/overrides/vanilla/bots/scuttler_red.patch.json   { "health": 400 }
mods/grippy/overrides/vanilla/wheel.patch.json                    { "name": "Grippy Wheel", "visual": { "roughness": 1 } }
mods/fast_craft/overrides/vanilla/recipes/barrel.patch.json       { "time": 1 }
```

Objects merge key by key, `null` removes a key, and anything else (numbers, strings, lists) replaces the
original value ([RFC 7386](https://www.rfc-editor.org/rfc/rfc7386)). The result is checked like any
entry. Patches stack: a mod that depends on another mod's override or patch builds on top of it.

### Removing content

`overrides/<mod_id>/remove.json` takes entries out of the game, listed by id under `parts`, `recipes`,
`bots`, `quests`, `pois` and `characters`:

```json
{ "parts": ["antenna"], "recipes": ["barrel"], "bots": ["scuttler_blue"] }
```

A recipe that makes, uses or runs at a removed part is removed with it. Other content that still names
a removed entry (a quest reward, a raid, a POI site) reports an error, so remove or patch that too.
Saved Creations with a removed part fail to load that part.

When two mods change the same entry without one depending on the other, the later one in the load order
wins and the Mod Manager shows a warning (`MD1502`). Changing an entry that doesn't exist, or was removed
by an earlier mod, is an error (`MD1501`); removing one that isn't there is a warning.

### Game rules

The game's balance numbers ship with the build in `game/data/`, and a mod changes them with a merge patch in
`rules/`. Patches from several mods stack in load order, so the last mod wins per key; lists with `id` keys merge
like `worldgen.patch.json`, other lists (like `starting_items`) are replaced whole.

```json
// rules/survival.patch.json: a harder start
{ "max_health": 60, "respawn_delay": 15, "starting_items": [{ "item": "carrot", "count": 2 }],
  "difficulties": { "normal": { "bot_damage": 1.5, "raid_budget": 1.4, "raid_cooldown": 0.7 } } }
```

| File | Patches | Rules (everything else is locked) |
|------|---------|-----------------------------------|
| `rules/survival.patch.json` | `game/data/survival.json` | Health, respawn, drops on death, refining, buckets, starting items, the easy and normal difficulties' multipliers, fall damage. Locked: `max_drops`, `aim_tolerance`. |
| `rules/fire.patch.json` | `game/data/fire.json` | How fires grow, spread and hurt. Locked: `tick`, `max_fires`. |
| `rules/building.patch.json` | `game/data/building.json` | `paint` (palette and ammo), `sledge`, `breaking`, `lift`. |

A patch that changes a locked key is an error and the mod is not loaded (`MD1602`); a file with another name in
`rules/` is ignored with a warning (`MD1601`). A patch whose result is invalid (a negative health) is left out when
the world starts, with an error in the log (`MD1603`), and the rules before it still apply. Rules apply when a world
starts or joins, and host and clients check they run the same files.

### Game rules reference

Every key a rules patch may change, with the value the build ships and what it does. Keys inside an object
are written with dots: `fall.min_speed` is `{ "fall": { "min_speed": ... } }` in the patch. Units are SI.
The locked keys are listed after each table; a patch may not touch them, or anything inside them.

#### `survival.json`

| Key | Default | Meaning |
|-----|---------|---------|
| `max_health` | 100 | A player's full health. Health is the only survival meter. |
| `respawn_delay` | 5 | Seconds a dead player waits before they may respawn. |
| `low_health` | 0.25 | Share of full health below which the HUD warns and the low-health heartbeat plays. |
| `reach` | 7.5 | How far from a player's eyes soil, beds and water can be used, in m. |
| `pickup_reach` | 3 | How far from a player's eyes items on the ground can be picked up, in m. |
| `drop_lifetime` | 900 | Seconds items lie on the ground before they are gone. |
| `drop_inventory_on_death` | true | Whether a dead player's inventory falls where they died (in the knockout bag). |
| `knockout_bag` | `vanilla:meshes/knockout_bag.glb` | The model a knocked-out player's items lie in (`mod:path`). |
| `bucket_item` | `water_bucket` | The empty bucket `CollectWater` fills (every one the player carries, in one dip). |
| `water_item` | `water_bucket_full` | The full bucket an empty one turns into at water, in the same slot. |
| `water_seconds` | 1 | Shortest time between one player's fills at water, in s. |
| `refine_seconds` | 5.2 | Seconds E is held on a resource node (a tree, a rock node) or a loose part to refine it. |
| `refine_cancel_distance` | 1.5 | How far a player may move while refining before it stops, in m. |
| `starting_items` | 4 Carrot Seeds, 4 Potato Seeds, 8 Soil Bags, 2 full Water Buckets, 3 Carrots, a Portable Fabricator, a Connect Tool | What each player starts a Survival world with: a list of `{item, count}`. A patch replaces the whole list. |
| `difficulties.easy.bot_damage` | 0.5 | On Easy: multiplier on the damage bots do to players. |
| `difficulties.easy.raid_budget` | 0.6 | On Easy: multiplier on a raid's budget (how many bots it sends). |
| `difficulties.easy.raid_cooldown` | 1.5 | On Easy: multiplier on the wait after a raid before the next can start. |
| `difficulties.normal.bot_damage` | 1 | The same three multipliers on Normal. |
| `difficulties.normal.raid_budget` | 1 | |
| `difficulties.normal.raid_cooldown` | 1 | |
| `fall.min_speed` | 11 | Landing speed (m/s, straight down) that starts to hurt. |
| `fall.damage_per_speed` | 8 | Health lost per m/s above `fall.min_speed`. |
| `hits.light` | 1 | Health a blow must take for the player to flinch away from it (`hit_front`, `hit_back`, `hit_left`, `hit_right`). |
| `hits.heavy` | 25 | Health from which a blow staggers them back (`hit_heavy`). |
| `hits.knockdown` | 50 | Health from which it throws them off their feet (`knockdown`, then `get_up`); they can't move while down. |
| `hits.causes` | `["bot", "sledge", "weapon", "explosion"]` | The causes of harm that are blows; fire, falls and liquids make no reaction. |

Locked: `max_drops` (most piles of items on the ground at once) and `aim_tolerance` (how far a shot may point from
where the host sees the shooter look, an allowance for network lag).

#### `fire.json`

| Key | Default | Meaning |
|-----|---------|---------|
| `grow` | 0.08 | Strength a fire gains per second, up to 1. |
| `ignite` | 0.2 | Strength a part that just caught starts burning at. |
| `spread_at` | 0.6 | Strength at which a burning part sets its flammable neighbours alight. |
| `part_damage` | 4 | Health per second a full-strength fire takes off the part it burns (less while weaker). |
| `player_damage` | 10 | Health per second a full-strength fire takes off a player standing in it (less while weaker). |
| `player_radius` | 1.2 | How close to a fire a player's feet must be for it to burn them, in m. |
| `site_marker` | `fire` | POI marker type where fires burn when a world is made (the crash site's). |
| `site_radius` | 1.5 | How close to such a fire a flammable part catches, in m. |
| `sound` | `burn_wood` | Looping sound event every fire plays, its intensity following the fire's strength (a bare id is vanilla's). |

Locked: `tick` (seconds between the host's fire updates) and `max_fires` (most fires burning at once).

#### `building.json`

| Key | Default | Meaning |
|-----|---------|---------|
| `lift.platform_size` | 1.0 | Width and depth of the Lift's platform, in m. |
| `lift.base_height` | 0.5 | Height of the platform's top above the ground at level 0, in m. |
| `lift.level_height` | 0.25 | How far one level raises the platform, in m. |
| `lift.max_level` | 20 | Highest level. |
| `lift.model` | `vanilla:meshes/lift/lift.glb` | The Lift's model (`mod:path`); its `deploy` clip plays when it is put down and its `extend` clip follows the deck height. |
| `lift.raise_speed` | 0.9 | Fastest the platform moves between levels, in m/s. |
| `lift.raise_acceleration` | 4.0 | How quickly the platform speeds up and slows down, in m/s². |
| `lift.end_stop_sound` | `impact_metal` | Sound event played when the platform comes to rest at level 0 or the top. |
| `lift.remove_hold_seconds` | 0.5 | Seconds right click is held on the Lift before it is taken away. |
| `lift.refuse_stored_items` | true | When true, a Creation whose storage holds items can't be picked up onto the Lift. |
| `lift.refuse_grounded` | true | When true, a Creation built onto the ground can't be captured (as in Scrap Mechanic); it comes free once the parts touching the ground are gone. False lets the Lift pull it free. |
| `lift.refuse_categories` | `["resource"]` | Part categories that keep a Creation off the Lift. |
| `lift.refuse_parts` | `["produce_crate"]` | Parts (qualified or vanilla ids) that keep a Creation off the Lift. |
| `lift.max_extent` | 32.0 | Longest side, in m, of the box round a Creation the Lift will capture. |
| `lift.max_blueprints` | 50 | Most blueprints the blueprint menu lists, newest first. |
| `lift.blueprint_max_bytes` | 1048576 | Longest blueprint, in characters of JSON, that `LoadBlueprint` accepts. |
| `paint.palette` | 40 colours | The colours the Paint Tool offers, `"#rrggbb"`, base colour by base colour, each from light to dark. The host accepts any colour. |
| `paint.shades` | 4 | Shades of each base colour in the palette. |
| `paint.default_color` | 25 | The palette index the tool starts with. |
| `paint.max_parts_per_action` | 256 | Most parts one Paint action, or one drag, may colour. |
| `paint.drag_flush_seconds` | 0.1 | While dragging, how often the parts passed over are sent to the host, in s. |
| `paint.ammo` | `paint_ammo` | The item a stroke uses in Survival; leave it out (`null`) to make painting free. |
| `paint.ammo_per_stroke` | 1 | How many of `paint.ammo` one stroke uses. |
| `paint.stroke_seconds` | 1.0 | Longest gap between the parts of one stroke reaching the host, in s; a longer gap starts a new stroke, which pays again. |
| `breaking.spread_speed` | 1.5 | Outward speed each loose piece starts with when something falls apart (a rock into chunks), in m/s. |
| `breaking.lift_speed` | 1.5 | Upward speed each piece starts with, in m/s. |
| `breaking.fell_gap` | 0.05 | Metres between a felled tree's log segments stacked up its trunk, and under the lowest. |
| `sledge.damage` | 10 | Health one Sledgehammer hit takes from a part (unless the part has its own `sledge_damage`). |
| `sledge.health_per_durability` | 10 | A part's full health per point of its `durability`. |
| `sledge.swing_seconds` | 0.6 | Shortest time between one player's hits: one whole swing, in s. |
| `sledge.strike_seconds` | 0 | Seconds from the click to the hit (0: the hit lands at the click). |
| `sledge.reach` | 3.0 | How far a swing reaches, in m. |
| `sledge.impulse` | 5000 | Impulse a hit gives what it lands on, along the swing, in N·s. |
| `sledge.max_knockback_speed` | 4.0 | Fastest a hit sets anything moving, in m/s, so a light Body isn't thrown across the map. |
| `sledge.character_mass` | 80 | The mass a player or bot counts as when a hit knocks it back, in kg. |
| `sledge.knockback_lift` | 2.5 | Upward speed a knocked-back player or bot gets on top of the push, in m/s. |
| `sledge.knockback_drag` | 8 | How fast a knocked-back bot slows to its own walk again, in m/s². |
| `sledge.shake_metres` | 0.03 | How far the swinging player's camera jolts when a hit lands, in m (0: no shake). |
| `sledge.shake_seconds` | 0.2 | How long that jolt takes to settle, in s. |
| `sledge.combat_damage` | 20 | Health one hit takes from a player or bot. |
| `sledge.object_damage` | 10 | Health one hit takes from a tree, rock or other world object without its own `sledge_damage`. |

Locked: `reach`, `max_parts_per_creation`, `clearance_scale`, `anchor_margin`, `placement`, `connect` and `weld`
(build reach, Creation size, placement timings, the Connect Tool's drawing and the Weld Tool's seam).

## 7. Load order and the Mod Manager

Players manage mods in the in-game **Mod Manager** (under **Mod tools** on the main menu). It lists
every installed mod in load order and shows, for the setup on screen:

- a checkbox per mod. Enabling a mod also enables what it depends on. Disabling a mod also disables the
  mods that depend on it. Vanilla cannot be disabled.
- **Move up** and **Move down**. A mod always stays below the mods it depends on, so a move stops there.
  Mods with no dependency between them load in the order shown, which decides which override wins.
- each mod's status (loaded, disabled, not loaded and why, overrides won or lost) and details.
- every error, warning and conflict the setup would load with, before anything is saved.
- **Export…**, **Install…** and **Uninstall** for mod packages (§9).
- **Open mods folder** shows the `mods/` folder (next to the game) in your file manager, and **Rescan** looks
  for new and changed mods in it, for example after you copied a mod in by hand. Rescan reloads the saved setup,
  so if you changed the list without pressing **Apply** it asks before it drops those changes.
- **New mod…** makes an empty mod (a `mod.json` that depends on vanilla, with `parts/` and `scripts/`) to fill
  with the Part Editor, Lua and data files. **Edit info…** changes the selected mod's name, version, description,
  authors and dependencies in its `mod.json`, keeping everything else.
- **Check all content** loads every kind of content of the setup on screen the way a world starts (parts, sounds,
  world generation, recipes, bots, raids, quests, characters, languages) and compiles every Lua script, and lists each problem
  with the content area, file and line. The quick list under **Problems** only covers mods and parts.

**Apply** saves the setup to `mods.json` in the user data folder (`user://mods.json`; on Linux
`~/.local/share/godot/app_userdata/Mechanica/`). The game reads it the next time it starts or hosts.
The file looks like this:

```json
{
  "format_version": 1,
  "mods": [
    { "id": "vanilla", "enabled": true },
    { "id": "better_wheels", "enabled": true },
    { "id": "old_wheel_pack", "enabled": false }
  ]
}
```

Newly installed mods that are not in the file are enabled and load after the listed ones, by id. Entries
for uninstalled mods are kept, so a reinstalled mod gets its place back. Without the file every mod is
enabled. A broken file is reported and ignored.

## 8. Multiplayer

The host's content is the game's content. Every player who joins needs the **same mods, same
versions, enabled in the same order**. The game compares a fingerprint of every loaded manifest, part and
override on join, so any change to a loaded value counts (formatting and comments do not). Bump your
version for every release so players can tell which copy they have.

## 9. Packaging and sharing

Share a mod as a **mod package**: a zip named `<id>-<version>.zip`, made and installed by the Mod Manager.
The format is built for a future mod store, so the store, the game and the AI endpoint all check packages
with the same code (`Mechanica.Core.Packages.ModPackages`).

### Exporting

1. Open the Mod Manager, select your mod and press **Export…**, then pick a folder (it starts in
   `user://mod_packages`).
2. The game checks the mod the way it loads it, with its dependencies and every kind of content (parts,
   recipes, sounds, bots, raids, world generation, quests, characters). **Any error refuses the export**
   and the Packages panel lists the problems. Problems in other mods do not count.
3. Editor leftovers are left out and listed: hidden files and folders (`.git/`, `.godot/`), `*.import`,
   `*.uid`, `*.bak`, `*.tmp`, `*.orig`, `*~`, `Thumbs.db`, `desktop.ini`. A symbolic link refuses the export.
4. The archive holds the mod folder itself (`better_wheels/mod.json`, …) plus `better_wheels/package.json`.
   Exporting the same files twice gives the same bytes.

In the release notes, list the dependencies with versions, any conflicts, and every part you override.

### package.json

```json
{
  "package_format": 1,
  "id": "better_wheels",
  "name": "Better Wheels",
  "version": "1.2.0",
  "description": "Sport and off-road wheels.",
  "authors": ["Ada"],
  "dependencies": [{ "id": "vanilla", "version": "^0.1.0" }],
  "conflicts": [{ "id": "old_wheel_pack" }],
  "min_game_version": "0.1.0",
  "min_protocol_version": 1,
  "files": [
    { "path": "mod.json", "size": 212, "sha256": "9f2c…" },
    { "path": "parts/wheels/sport_wheel.json", "size": 1830, "sha256": "41aa…" }
  ],
  "sha256": "c07e…",
  "signature": ""
}
```

| Key | Meaning |
|-----|---------|
| `package_format` | Package format, `1`. A newer format is refused (`MD1306`). |
| `id`, `name`, `version`, `description`, `authors`, `dependencies`, `conflicts` | Copied from `mod.json`. `id` and `version` must match it and the archive's folder. |
| `min_game_version` | Game version that exported the package; older games refuse it (`MD1306`). |
| `min_protocol_version` | AI endpoint protocol version at export; older games refuse it (`MD1306`). |
| `files` | Every file except `package.json`, sorted by path: path relative to the mod folder, size in bytes, SHA-256 (lowercase hex). |
| `sha256` | The package hash: SHA-256 of the text `"<sha256>  <path>\n"` for each file, sorted by path. That is exactly what `sha256sum` prints, so `find . -type f ! -name package.json -printf '%P\n' \| LC_ALL=C sort \| xargs sha256sum \| sha256sum` inside the mod folder reproduces it. |
| `signature` | Empty. Reserved for a mod store to sign `sha256`; the game does not check it yet. |

### Installing

Press **Install…** in the Mod Manager and pick a `.zip`. The game checks the archive before it changes
anything:

- **Safe paths only**: one top folder named after the mod; no absolute paths, drive letters, `:`,
  backslashes, `..`, `.` or empty segments, names ending in `.` or a space, two paths that differ only in
  letter case, or symbolic links (`MD1302`).
- **Size limits** against zip bombs: 512 MiB archive, 20 000 entries, 256 MiB per file, 1 GiB in total, and
  no file over 1 MiB that compresses more than 200 to 1 (`MD1303`).
- **Integrity**: the files in the archive and in `files` must be the same set (`MD1305`), every file's size
  and SHA-256 must match, and the package hash must match the file list (`MD1304`). Extraction never reads
  more than the listed size, whatever the zip header claims.
- **Compatibility**: `mod.json` must agree with `package.json`, and the game must be new enough (`MD1306`).
- **Dependencies**: each must be installed in a version in range, or the install is refused (`MD1307`);
  install them first. Conflicts with installed mods, and installed mods that need a different version of
  this one, are warnings shown in the question (`MD1308`, `MD1307`).

When everything checks out the game asks **Install** (new mod) or **Replace** (an upgrade, the same
version again, an older version, or a broken folder with the mod's id). Files are extracted into a hidden
staging folder and hashed again as they are written; the old folder is swapped out only at the end and put
back if the swap fails, so a failed install changes nothing. The installed mod keeps its `package.json`.
A new mod is enabled and loads after the others with the next world; Apply is not needed. Vanilla ships
with the game and cannot be installed or uninstalled.

You can still install by hand: unzip into `mods/` so that `mods/better_wheels/mod.json` exists.

### Uninstalling

Select a mod and press **Uninstall**. The question names the installed mods that need it, which stop
loading. Confirming deletes the mod's folder. Its entry in `mods.json` is kept, so a reinstall gets its
place in the load order back.

## 10. Packaging diagnostics

| Code | Meaning |
|------|---------|
| `MD0101` | Folder has no `mod.json`; it is skipped (warning). |
| `MD0102` | `mod.json` is invalid (missing id, name or version, bad dependency or conflict entry). |
| `MD0103` | Mod id does not match the folder name. |
| `MD0104` | Two folders use the same mod id; the second is skipped. |
| `MD0105` | A dependency is not installed. |
| `MD0106` | A dependency is installed, but its version is outside the range. |
| `MD0107` | Dependency cycle. |
| `MD0108` | A dependency failed to load. |
| `MD0109` | `format_version` is newer than this build reads. |
| `MD0110` | The mod has errors in its files and was not loaded. |
| `MD0111` | A dependency is disabled in the Mod Manager. |
| `MD0112` | An enabled mod is listed under this mod's `conflicts`. |
| `MD0113` | `mods.json` is invalid. |
| `MD0203` | Overrides target a mod that is not a dependency. |
| `MD0204` | Override for a part the target mod does not have. |
| `MD0205` | Two mods override the same part; the later one wins (warning). |
| `MD1501` | An override, patch or removal names an entry that doesn't exist (an error; a warning for removals). |
| `MD1502` | Two mods change the same bot, quest, POI or character; the later one wins (warning). |
| `MD1301` | Not a mod package: unreadable zip, no `package.json`, invalid manifest, or vanilla. |
| `MD1302` | Unsafe path or symbolic link in the archive (or the mod, on export). |
| `MD1303` | Archive or a file is over the size limits, or looks like a zip bomb. |
| `MD1304` | A file's SHA-256 or size, or the package hash, does not match `package.json`. |
| `MD1305` | The archive's files and `package.json`'s `files` list differ. |
| `MD1306` | Needs a newer game, package format or AI protocol. |
| `MD1307` | A dependency is missing or the wrong version (error), or an installed mod needs another version (warning). |
| `MD1308` | Conflicts with an installed mod (warning). |
| `MD1309` | Export refused: the mod has errors. |
| `MD1310` | An installed mod's file was changed, removed or added since it was installed (warning). |
