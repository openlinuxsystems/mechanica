# Mod Tools

Mechanica's mod tools are part of the game. They ship in every build, so anyone who plays can make mods,
and there is nothing else to install. Open them from the main menu: the **Mod tools** column on the right
holds every tool on this page. Each tool is a window with a title and an X; Esc or the X closes it and asks
first when something is not saved. Press **Help** in a tool, or **H** (or **F1**) anywhere, for the Handbook.

| Tool | What it is for | What it writes |
|------|----------------|----------------|
| [Part Editor](#part-editor) | Parts and items: model, physics, sockets, look, script, sounds and recipes | `parts/`, `meshes/`, `textures/`, `scripts/`, `sounds/`, `recipes/`, `editor/` |
| [Bot Behavior Editor](#bot-behavior-editor) | Bots, their behaviour trees and scripts, and the raid table | `bots/`, `scripts/bots/`, `raids/raids.json` |
| [Recipe Editor](#recipe-editor) | Every recipe of every mod: new ones, copies and overrides | `recipes/`, `overrides/<mod>/recipes/` |
| [Sound Editor](#sound-editor) | A mod's sound events, without a part | `sounds/` |
| [Test Sandbox](#test-sandbox) | Trying parts, bots and Creations, with local clients | nothing |
| [Mod Manager](#mod-manager) | Making mods, turning them on and off, load order, checking and packages | `mod.json`, `user://mods.json`, packages |

The [Lua script editor](#the-lua-script-editor), [batches](#batches-run-batch-and---part-batch) and the
[Handbook](#the-handbook) are shared by several tools and have their own sections below.

Every tool writes plain JSON, Lua and asset files into a mod folder under `mods/`, so you can also edit those
files by hand. The game checks them the same way whichever way they were made. Where a tool lets you pick
the mod to edit, vanilla is either not offered (Recipe Editor, Sound Editor) or asks before writing (Part
Editor), because players without the same change can't join you. Vanilla's own content is authored through
batches in `mods/vanilla/editor/batches/`. To change a vanilla part or recipe for your mod, override it in
your own mod instead.

## Part Editor

The Part Editor makes and edits parts and items. [Getting Started](getting-started.md) walks through it tab
by tab with a worked example; this is the short version.

1. Press **New…**, pick the **Mod**, a **Part id**, a **Name** and a **Template** (one of 13: block, decor,
   interactive, item, bearing, piston, suspension, wheel, engine, seat, thruster, wing, logic), and press **OK**.
   Or press **Open…** and pick a part file from a mod's `parts/` folder.
2. Edit it on the tabs:
   - **Part**: identity, physics, footprint, and the built-in features (storage and crafting, survival,
     logic and seats, light, animation).
   - **Model**: shapes (**+ Box**, **+ Wedge**, **+ Cylinder**, **+ Sphere**) combined with `union`,
     `subtract` and `intersect`, and **Import mesh…** for a glTF model.
   - **Collision**: the physics shape. A sphere or cylinder (a wheel) also has **Climb**: the step height it drives up.
   - **Sockets**: attachment points and their joints.
   - **Look**: colour, surface, textures and colour variants.
   - **Behaviour**: the part's script and its **Properties**.
   - **Recipes**: the recipes in the mod that make the part.
   - **Script**: the [Lua script editor](#the-lua-script-editor).
   - **Sounds**: the Sound Event Binder.
3. Use the overlay toggles over the preview (**Collision**, **Sockets**, **Connections**, **Joints**) to see
   what the part will do.
4. Press **Export** (Ctrl+S) to check the part with the game's own rules and write it. Problems are listed
   under the preview, errors first.
5. Press **Test in Sandbox** to export and try it at once.

**Override in my mod…** starts a copy of another mod's part in `overrides/<other_mod>/` of your mod, with the
same id, so it replaces the original. **Copy as new part…** starts a copy with its own id. Both add the other
mod to your dependencies. **Undo** and **Redo** (Ctrl+Z, Ctrl+Shift+Z) cover every edit.

What it writes: `parts/<id>.json`, the baked `meshes/<id>.glb`, imported files in `meshes/`, `textures/`,
`scripts/` and `sounds/`, recipes in `recipes/`, sound events in `sounds/<mod id>.json`, and the editor's
working copy in `editor/parts/<id>.json` (the model pieces; the game never reads it). Exporting into a mod that
does not exist yet creates its folder and `mod.json`.

## Bot Behavior Editor

The Bot Behavior Editor makes bots: their stats, attacks and drops, their behaviour tree, their script, and the
mod's raid table. The [Bot Tutorial](bot-tutorial.md) explains every field and node.

1. Pick the **Mod** in the toolbar. Its bots are listed under **Bots in this mod**; click one to open it.
2. To start a new bot, pick a template (`melee`, `ranged` or `passive`, from `game/data/bot_templates/`),
   type its id in the box next to it, and press **New**.
3. Edit it on the tabs:
   - **Bot**: **Name**, **Description**, **Family (sounds)**, **Health**, **Speed**, **Water speed**,
     **Swim depth**, **Sight**, **Radius**, **Height**, **Colour**, **Melee immune**, **Passive**, **Raid cost**, **Script**,
     **Attacks and drops**, **Properties** and **Wild**.
   - **Behavior**: the behaviour tree. Pick a node type from the palette and press **Add** to put it under the
     selected node; **Up**, **Down** and **Remove** rearrange it. Select a node to set its parameters, and
     **+ parameter** adds one your own leaf reads.
   - **Script**: the bot's Lua (hooks `on_spawn`, `on_think`, `on_damaged`, `on_death`, and global functions
     the tree uses as leaves). **Save script** checks the Lua and saves it; **Lua API** lists what it can call.
   - **Raids**: the mod's raid table as JSON, with **Preview crop value** and **Preview players** to see what
     a raid would send. **Save raid table** checks it against every mod's bots and saves it.
4. Press **Export** to check the bot as the game loads it and write it, or **Test in Sandbox** to export it
   and spawn it in the [Test Sandbox](#test-sandbox).

What it writes: `bots/<id>.json`, the bot's script in `scripts/bots/<id>.lua`, and `raids/raids.json` (the last
mod in load order with a raid table wins). The editor lists every mod, vanilla included, but vanilla's bots and
raid table are made from the batch `mods/vanilla/editor/batches/bots.json`; edit that and re-run it instead.

## Recipe Editor

The Recipe Editor shows every recipe of every installed mod in one list, and edits the recipes of one mod at a
time. Use it for recipes that belong to no part you are editing, and to change another mod's recipe.

1. Pick the **Mod to edit**: the mod new recipes, copies and overrides are saved into. Vanilla is not offered.
   If only vanilla is installed, it says to make a mod in the [Mod Manager](#mod-manager) first.
2. Type in the filter box to narrow the list by recipe, item, station or mod. The columns are **Recipe**,
   **Makes** and **From** (the mod it comes from); a recipe that another mod replaces shows which one wins in
   load order.
3. Pick a recipe and choose what to do:
   - **New recipe** starts an empty recipe in the mod being edited.
   - **Copy to mod** starts a new recipe in your mod from the picked one, with its own id.
   - **Override** starts your mod's own version of the picked recipe, with the same id, so it replaces the
     original everywhere.
4. Edit **Id**, **Name**, **Stations**, **Time** and **Order**, and the rows under **Inputs** and **Outputs**
   (**+ Input**, **+ Output**, **×** to remove). Give every recipe an `order` from its group in
   [Recipe order](part-data-schema.md#recipe-order).
5. Press **Save recipe**. It is checked exactly as the game loads it; problems are listed under **Problems**.
   **Delete** removes the recipe's file from your mod, after asking.

What it writes: `recipes/<id>.json` for your own recipes, and `overrides/<other_mod>/recipes/<id>.json` for an
override. An override needs the other mod as a dependency (the Mod Manager's **Edit info…**, **Needs**). See
[Overriding recipes](packaging-guide.md#overriding-recipes) for patches and removals.

## Sound Editor

The Sound Editor edits a mod's sound events without opening a part: sounds that scripts, bots, quests or other
mods play. It is the Part Editor's Sound Event Binder without the part's own bindings.

1. Pick the **Mod to edit** (vanilla is not offered) and the **Bank file**, the sound bank in the mod's
   `sounds/` folder to edit.
2. Press **+ Event from clip…** to import an `.ogg` or `.wav` into the mod's `sounds/` folder and make an event
   for it, or pick an event to change.
3. Set its bus, whether it loops, volume and pitch with their random variation, how far it carries, and a
   loop's parameters. **Import…** next to **Clips** adds more clips; each play picks one at random.
4. Press **▶ Preview** to hear it as it stands, then **Save** to check it and write the bank.

What it writes: the bank file in `sounds/` (`sounds/<mod id>.json` by default) and the imported clips. See
[Sound Events](sound-events.md) for every field.

## Test Sandbox

The Test Sandbox is a small flat world for trying content without starting a game. Open it from the main
menu, or with **Test in Sandbox** in the Part Editor or Bot Behavior Editor, which exports first and returns
to the editor when you close the sandbox.

- Click the world to look around. WASD moves and Space jumps. Esc frees the mouse, and Esc again (or the X)
  closes the sandbox.
- Pick a part from the list (type in **Filter parts** to narrow it) and press **Spawn** to put it in front of
  you. Pick a bot from the list under it and press **Spawn bot**.
- **Clear all** removes every Creation and bot, after asking.
- **Reload mods** reads every mod again, so files you changed since show up.
- **Show collision and centre of mass**, **Show joints** and **Show forces** draw each body's collision
  shapes and centre of mass, each joint's axis, range and state, and the forces and torques parts put on
  their bodies.
- **Add local client** hosts the sandbox and joins another copy of the game to it in the same window, up to
  three. **Remove client** disconnects the last one, after asking. Press **F6** to switch which player you
  control ("Test Sandbox: switch player" in Options, Controls). The host simulates everything and checks every
  client's actions, as in a real multiplayer game, so this is how you check a feature works for a client too.

A part script you save in the Part Editor's **Script** tab reloads into the sandbox's running parts at once,
keeping their `state`, and the editor's **Console** shows what they print.

## Mod Manager

The Mod Manager lists every installed mod in load order and is where you make mods, check them and package
them. The [Packaging Guide](packaging-guide.md) explains load order, overrides and packages in depth.

The list shows each mod with a checkbox, its **Version** and its **Status** (loaded, disabled, not loaded and
why, or how many parts it overrides). Select a mod to see its details: authors, what it needs and what it
conflicts with. **Problems** lists what the setup on screen would load with.

| Button | What it does |
|--------|--------------|
| checkbox | Turns a mod on or off. Turning one on also turns on what it needs; turning one off also turns off the mods that need it. Vanilla can't be turned off. |
| **Move up**, **Move down** | Load the selected mod earlier or later. A later mod's overrides win. A mod never moves above a mod it needs. |
| **Rescan** | Looks for new and changed mods in the mods folder. Asks first if the setup has unsaved changes. |
| **Open mods folder** | Shows the mods folder in your file manager. |
| **Export…** | Checks the selected mod with every kind of content and saves it as a package (`<id>-<version>.zip`), by default in `user://mod_packages`. Any error refuses the export. |
| **Install…** | Installs a mod package (`.zip`) after checking its paths, size, hashes and dependencies. Asks **Install** or **Replace**. |
| **Uninstall** | Deletes the selected mod from the mods folder, after asking and naming the mods that need it. |
| **New mod…** | Makes an empty mod: a **Mod id** (the folder name) and a **Name**. It writes `mods/<id>/mod.json` depending on vanilla, with empty `parts/` and `scripts/` folders. |
| **Edit info…** | Changes the selected mod's **Name**, **Version**, **Authors**, **Needs** (dependencies, comma separated) and **Description** in its `mod.json`, keeping every other key. Not for vanilla. |
| **Check all content** | Loads every kind of content of the setup on screen the way a world starts (parts, sounds, world generation, recipes, bots, raids, quests, characters, language files), compiles every Lua script, and lists each problem with its area, file and line. |
| **Apply** | Saves the setup (which mods are on, and their order) to `user://mods.json`. It loads with the next world. |
| **Revert** | Goes back to the saved setup. |

The **Packages** box under the buttons reports what Export, Install, Uninstall, New mod and Edit info did.
Changes to the setup apply the next time you start or host a game, and players who join need the same mods in
the same order.

## The Lua script editor

The Part Editor's **Script** tab edits the open part's Lua script with highlighting. (The Bot Behavior Editor's
**Script** tab is a simpler editor for a bot's script, with **Save script** and **Lua API**; see
[above](#bot-behavior-editor).)

1. Pick a **Script template** (empty, thruster, bearing_motor, wing or distance_sensor, from
   `game/data/lua_templates/`) and press **New from template**. A part with no script gets
   `scripts/<part id>.lua`; one that has a script is asked before it is replaced.
2. Write the script. Double-click an entry in the **API** list under the code to insert it.
3. Press **Save & reload** (Ctrl+S in the code). It compiles the script the way the host does, marks the line
   of a compile error, writes the file, and reloads it into every part running it in the Test Sandbox,
   keeping their `state`. **Revert** throws away unsaved changes.
4. Read the **Console** for what the sandbox's scripts print, and every error with its file and line.
   **Lua API Reference** opens the [Lua API Reference](lua-api.md) in the Handbook.

For an override or a copy whose script is another mod's (`vanilla:scripts/x.lua`), saving writes a copy into
your mod and points the part at it; the original is never changed.

The API list holds every entry of the Lua API Reference, grouped as the reference is:

| Group | What it holds |
|-------|---------------|
| Hooks | `on_create`, `on_update`, `on_reload`, `on_destroy`, `on_activate`, `on_release`, `can_activate`, `on_command`, `on_power`, `on_collision`, `get_outputs`, `on_enter`, `on_leave`, `on_event`, and the item hooks `on_equip`, `on_unequip`, `on_aim`, `can_use`, `on_use`, `on_hit`, `can_reload_item`, `on_reload_item` |
| Globals | `state`, `properties`, `log`, `require` |
| `part`, `creation` | The part running the script and its Creation: position, forces, joints, logic, settings, light, display |
| `world` | Gravity, time, raycasts, overlaps, item data |
| `sound` | One-shots and loops |
| `soil`, `farm` | Farming |
| `player`, `item`, `holder`, `inventory` | Inventories and the item in hand |
| `combat`, `fire` | Hits, splashes, projectiles; fires |
| `events`, `actions`, `timer` | Mod scripts: events, Action API calls, timers |
| `vec3` | Vectors |

Mod scripts (listed in `mod.json` `scripts`) have no part, so there is no in-game editor for them: edit them in
any text editor. Check all content in the Mod Manager compiles them, and a running host reloads a saved change
within `reload_poll_seconds` (half a second), as it does part scripts. See the [Mod Scripts Cookbook](mod-scripts.md).

## Batches: Run batch and --part-batch

A batch file describes many parts, bots or quests at once, and the editor makes them all exactly as you would
by hand. A batch lives in a mod at `editor/batches/<name>.json`; the game never reads it.

- **Part batches** (`"parts": [...]`) make parts and their recipes. See
  [Getting Started](getting-started.md#13-many-similar-parts-batches).
- **Bot batches** (`"bots": [...]`) make bots and the raid table. See [Bot Tutorial](bot-tutorial.md#batches).
- **Story batches** (`"pois": [...]`, `"quests": [...]`) make quest POIs and quests. See
  [Story batches](quests.md#story-batches).

To run one, press **Run batch…** in the Part Editor and pick the file. Every kind of batch runs from there. It
asks first, then writes every entry with the same checks as Export, lists each problem with the entry it comes
from, and opens the last part. A mistake in the batch itself stops it before anything is written; an entry that
fails its own checks is skipped and the rest are written. Running it again updates what it made.

A batch also runs from the command line, without opening the game window:

```sh
godot --headless --path game -- --part-batch mods/my_mod/editor/batches/blocks.json
```

It prints every problem and exits with 0 only when every entry was written. `tools/author-vanilla.sh` runs every
vanilla and example batch this way.

## The Handbook

The Handbook is these docs, in the game. Press **H** or **F1** anywhere (on the menu, in a world or in a tool),
or **Help** in a tool. It shows every page in the game's `docs/` folder, the same files as in the repository.

- The list on the left holds every page; the box above it searches every page's title and text for all the
  words you type.
- Links to other pages open in place, and web links open in your browser. Links into sub-folders of `docs/`
  don't open in the Handbook.
- The arrow keys (or Page Up and Page Down) and the **◀ Previous** and **Next ▶** buttons turn pages in list
  order.
- It opens at the page you read last. Esc, or the key that opened it, closes it.

A new page you add to `docs/` shows up in the Handbook by itself.
