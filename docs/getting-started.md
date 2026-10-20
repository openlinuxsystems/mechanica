# Getting Started

This guide walks you through making your first part in the **Part Editor**. You'll make a mod, build a
crate block in it, give the crate a recipe, try it in the **Test Sandbox** with local clients, and then play
with it in a real world. The editor is built into the game: open it from the main menu under **Mod tools**
with **Part Editor**, and press **Help** in it to read this page again.

Every vanilla block and part is authored the same way, so anything you see in the base game you can
make yourself. When you have made a part, [Your first mod](first-mod.md) carries on with items, scripts,
translations and packaging, and [Mod tools](mod-tools.md) explains every tool on the menu.

## What a part is

A part is one JSON file in a mod's `parts/` folder, plus the meshes, textures, scripts and sounds it
points at. The editor writes that file for you. The [Part Data Schema Reference](part-data-schema.md)
explains every key in it.

A mod is a folder in the game's `mods/` folder:

```
mods/my_mod/
  mod.json                  id, name, version, dependencies
  parts/crate.json          the part
  meshes/crate.glb          its model, baked by the editor
  recipes/crate.json        how players craft it
  editor/parts/crate.json   the editor's working copy (shapes and pieces)
```

## 0. Make a mod

1. On the main menu, under **Mod tools**, press **Mod Manager**.
2. Press **New mod…**. Type a **Mod id** (the folder name: lower case letters, digits and `_`, for example
   `my_mod`) and a **Name** players see (`My Mod`), then press **OK**.
3. The Mod Manager makes `mods/my_mod/` with a `mod.json` that depends on vanilla, and empty `parts/` and
   `scripts/` folders. The new mod is selected and turned on.
4. Press **Edit info…** to add a description, your name under **Authors**, or other mods it **Needs**. Each
   save rewrites `mod.json` and keeps everything else in it.
5. Press **Apply** if you changed the load order or turned mods on or off, then close the window.

If you skip this step, the Part Editor makes the mod folder and its `mod.json` the first time you export
into a mod that doesn't exist yet. Making it in the Mod Manager first lets you give it a proper name and
check it later with **Check all content**.

## 1. Start a part

1. Press **New…**.
2. Fill in the dialog:
   - **Mod** is the mod folder to write into, for example `my_mod`.
   - **Part id** is the part's id inside the mod. Use lower case and underscores, for example `crate`.
   - **Name** is what players see.
   - **Template** is what to start from (below).
3. Press **OK**. The preview shows the part on a 0.25 m grid. Right-drag to orbit, and use the mouse
   wheel to zoom.

The templates are the files in `game/data/part_templates/`. Each one starts with sensible physics and the
sockets, logic and settings its kind of part needs:

| Template | What it starts as |
|----------|-------------------|
| `block` | A solid 1×1×1 wooden block that attaches on every face. |
| `decor` | A 2×2×2 decorative prop with no behaviour. |
| `interactive` | A 1×1×1 part with a socket to attach by, ready for a script. |
| `item` | An inventory item that is never placed: a resource, food, a weapon. |
| `bearing` | A bearing: a base socket and a `rotor` socket with a free hinge joint. |
| `piston` | A piston: a base socket and a `head` socket with a slider joint. |
| `suspension` | A suspension mount: a spring joint that a wheel bearing rides on. |
| `wheel` | A 3×1×3 wheel that goes on a bearing's rotor by its hub. |
| `engine` | A 2×2×2 engine that turns the wheel bearings it is linked to. |
| `seat` | A 2×2×2 seat whose output is the driver's throttle. |
| `thruster` | A thruster that pushes its creation toward its base. |
| `wing` | A 4×1×4 wing that makes lift and drag from the air over it. |
| `logic` | A node of the logic bus that turns its inputs into an output. |

For the crate, pick `block`.

To change an existing part, press **Open…** and pick its file in a mod's `parts/` folder.

To change a part that belongs to another mod, such as vanilla, open it and press **Override in my mod…**.
Name your mod, and the editor starts an override in `overrides/<other_mod>/` of your mod with the same id,
so it replaces the original everywhere while the original file stays untouched. **Copy as new part…** does
the same but gives the copy its own id, for a new part based on an existing one. Both point the copy's
meshes, textures and script back at the original mod (`vanilla:meshes/x.glb`) and add that mod to your
`mod.json` dependencies. Saving the script on the **Script** tab saves a copy in your mod and switches the
part to it, and a new recipe on the **Recipes** tab of an override starts as an override of the original's
recipe. Exporting a vanilla part in place asks first, since players without the same change can't join you.

Every change can be undone with **Undo** (Ctrl+Z) and redone with **Redo** (Ctrl+Shift+Z or Ctrl+Y).

## 2. The Part tab

The **Part** tab holds the part's identity, physics and the built-in features a part can switch on. It is
split into sections. Placeable parts (blocks, interactive parts, containers, stations, lights, decor) show
every section; items show only Identity, Physics and Survival. Units are SI: kilograms, metres, seconds.

### Identity

**Id**, **Name**, **Description** and **Category** write `id`, `name`, `description` and `category`. The
category decides whether the part is placed on the build grid or stays an item. See
[Top-level keys](part-data-schema.md#top-level-keys) and [Categories](part-data-schema.md#categories).

### Physics

- **Mass** (`mass`) is in kg. Our crate is wood, so try `4`.
- **Durability** (`durability`), **Friction** (`friction`) and **Buoyancy** (`buoyancy`) use the scales
  described in the schema reference.
- **Flammable** (`flammable`) lets fire spread through it.
- **Stack size** (`stack_size`) is how many fit in one inventory slot.
- **Sound material** (`sound_material`) picks the fallback place, break and impact sounds (`wood`, `metal`, …).
- **Centre of mass (m)** (`centre_of_mass`) moves where the mass sits, from the part's centre.

### Footprint and icon

- **Size** (`size`) is the footprint in 0.25 m grid cells. A 1×1×1 block is a 0.25 m cube. Make the crate
  `2, 2, 2` for a 0.5 m cube.
- **Icon** (`icon`) is a `.png` for the inventory; **Import…** copies one into the mod's `textures/` folder.

### Storage and crafting

- **Inventory slots** (`inventory.slots`) gives the part an inventory, like a chest. With slots set,
  **Accepts** (`inventory.accepts`) limits it to some items and **Carry contents** (`inventory.carry_contents`)
  lets a player pick it up full.
- **Crafting station** (`crafting`) makes it a station that runs every recipe naming it. **Crafting speed**,
  **Queue size** and **Link range** write `crafting.speed`, `crafting.queue_size` and `crafting.link_range`;
  **Start clip**, **Work clip** and **Complete clip** write `crafting.clips`.
- **Upgrades** (`upgrades`) is a JSON list of levels, each with a `cost` and the keys it `set`s.

See [inventory and crafting](part-data-schema.md#inventory-and-crafting) and
[upgrades](part-data-schema.md#upgrades).

### Survival

What shows here depends on the part:

- Loose world items (a resource): **Loose** (`loose`), and once loose, **Refines into** (`refine`, items and
  counts such as `scrap_stone_block 20`).
- Placeable parts: **Sledge damage** (`sledge_damage`), **Breaks into (count)**, **Breaks into (most)** and
  **Breaks into (parts)** (`breaks_into`), **Soil** (`soil`, with **Water capacity**, **Growth speed**,
  **Dries in** and **Looks wet from**), **Respawn point** (`respawn_point`), **Ground only** (`ground_only`,
  with **Steepest ground**, `max_slope`), **On the ground places** (`ground_part`) and **Placed from item**
  (`placed_from`).
- Items: **Seed** (`seed`, with **Grows into**, **Grow time**, **Yield**, **Seeds back** and **Raid value**),
  **Aims** (`aim`, with **Aim zoom** and **Fire on release**) and **Held model** (`held`, with **Held mesh**,
  **Held offset (m)**, **Held rotation (°)** and **Held scale**).

Food and weapons are items with a script and its numbers in Properties; see the
[Items Tutorial](items-tutorial.md). The keys are in [survival](part-data-schema.md#survival) and
[held](part-data-schema.md#held).

### Logic and seats

**Input links** and **Output links** (`logic.inputs`, `logic.outputs`) are how many Connect Tool links may
end at and start from the part. **Use (E)** (`logic.use`) is what pressing E on it does (`toggle`, `press`, …).
**Node kind** (`logic.kind`), **Takes links from** (`logic.accepts`) and **Reverse setting** (`logic.reverse`)
shape how it joins the logic bus. **Seat** (`seat`) makes it a seat, with **Seat height**, **Cushion height**,
**Cushion back**, **Seat hands** and **Seat bounce**. **Settings** (`settings`) is a JSON list of what players
change on each placed part, and **Menu** and **Display** (`menu`, `display`) are JSON objects for its menu and
what it shows on itself. See [logic, settings and seats](part-data-schema.md#logic-settings-and-seats) and
[menu and display](part-data-schema.md#menu-and-display), and the
[Logic and Power Tutorial](logic-power-tutorial.md).

### Light

**Gives light** (`light`) adds a light the part's script switches with `part.set_light`. **Kind**, **Colour**,
**Energy**, **Range**, **Spot angle**, **Offset** and **Direction** set how it looks at full level. See
[light and animation](part-data-schema.md#light-and-animation).

### Animation

**Animated** (`animation`) lets the script turn the part's model (not its collision) with `part.animate`, about
**Axis** through **Pivot**. See [light and animation](part-data-schema.md#light-and-animation).

## 3. Build the model

The **Model** tab lists the pieces that make up the mesh. You can import a glTF model, build one
from shapes, or mix both.

- **+ Box**, **+ Wedge**, **+ Cylinder** and **+ Sphere** add a shape. Select a piece to set its
  size, **Position (m)**, **Rotation (°)** and **Scale**.
- Each piece has an **Operation**:
  - `union` adds the piece.
  - `subtract` cuts it out of the pieces above it.
  - `intersect` keeps only the overlap.
  **Up** and **Down** change the order, **Duplicate** copies a piece and **Remove** deletes it.
- **Import mesh…** copies a `.glb` or `.gltf` file (and the buffers and textures a `.gltf` uses) into
  the mod's `meshes/` folder and adds it as a piece.

For the crate, set the box to `0.5, 0.5, 0.5`. Then add a second, slightly smaller box, set it to
`subtract`, and move it up a little to hollow out the top.

When you export, the pieces are baked into one `meshes/<part id>.glb`. A part made from a single
imported mesh with no changes just points at that mesh. The pieces themselves are kept in the
[editor source file](part-data-schema.md#the-editor-source-file).

## 4. Collision

The **Collision** tab sets the shape physics uses (`collision`). It is often simpler than the model.

- With no shape set, the part collides as a box filling its footprint. That is right for most blocks.
- `box`, `sphere`, `cylinder` and `capsule` take sizes in metres and an **Offset (m)**.
- `convex` uses a mesh's convex hull.

Turn on **Collision** above the preview to see the shape drawn over the model. See
[collision](part-data-schema.md#collision).

## 5. Sockets and joints

Sockets are where other parts attach (`sockets`). Plain blocks don't need any, because a block attaches to
neighbours on every face. Parts with joints (bearings, pistons, suspension) use typed sockets.

- Pick a side (**+X**, **-X**, **+Y**, **-Y**, **+Z** or **-Z**) and press **+ Socket on side** to add
  a socket at the centre of that face.
- Select a socket to set its **Id**, **Type**, **Position (m)** and **Normal**. **Snap to face** moves
  it onto the nearest outer face, and **Remove** deletes it.
- Turn on **Sockets** above the preview to see them, and **Connections** to see how each face of the
  part would join a neighbouring block. Green is a rigid weld; blue is a bearing, teal a piston and
  violet a spring; red means nothing would attach there.

### Joints

When a socket's **Type** is `rotational`, `linear` or `spring`, a joint section appears under it: "Joint:
hinge", "Joint: slider" or "Joint: spring". It writes the socket's `joint` object:

- **Axis** is the grid direction it turns about or slides along (the socket's normal by default).
- **Min** and **Max** are its limits, in rad for a hinge and m for a slider or spring. A hinge with neither
  spins freely.
- A spring also has **Stiffness** (N/m) and **Damping** (N·s/m).
- A slider or spring has **Shaft radius** (m), which draws the extended rod.

Every change is one undo step ("Edit joint"). Turn on **Joints** above the preview to see each joint's
axis, its range and the part's centre of mass. The [Socket and Joint Guide](socket-joint-guide.md#the-joint-object)
explains how to choose the numbers, and [joint](part-data-schema.md#joint) lists the keys. The `bearing`,
`piston` and `suspension` templates start with working joints.

## 6. Look

The **Look** tab sets the part's material (`visual`):

- **Colour**, **Roughness** and **Metallic**.
- **Opacity**: below 1 the part is see-through, like glass.
- **Glow**: a colour the surface gives off, like a path light. It does not light anything else.
- **Texture** and **Glow texture**: `.png` images, imported into the mod's `textures/` folder.
- **Paintable**: whether players can paint the part.
- **Colour variants**: named colours a player can pick, each with an id. Press **+ Variant** to add
  one.

See [visual](part-data-schema.md#visual).

## 7. Behaviour and script

The **Behaviour** tab links a Lua **Script** from the mod's `scripts/` folder (`script`) and free-form
**Properties** (`properties`) that the script reads. All of this is JSON, and balance numbers belong here
rather than in code. A plain block needs none of it. The [Lua API Reference](lua-api.md) lists what scripts
can call.

The **Script** tab edits that script. Pick a template (empty, thruster, bearing motor, wing or
distance sensor) and press **New from template**: a part without a script gets
`scripts/<part id>.lua`. The **API** list under the code shows everything a script can call;
double-click an entry to insert it. **Save & reload** (Ctrl+S in the code) writes the file and
checks it, and **Revert** throws away unsaved changes. A compile error is marked on its line. When the
Test Sandbox is running the script, the save reloads it into the running parts, keeping their `state`, and
the **Console** shows what the scripts print and every error. Export the part once so the sandbox knows it
has a script.

## 8. Sounds

The **Sounds** tab is the Sound Event Binder. Its top half lists the part's sound names: `place`,
`remove`, `break` and `impact`, which the game plays, plus names suggested for the part's category and
any you add with **+ Sound name** for its script. Pick an event for each from every mod's sounds, or
leave it on the material fallback shown in brackets (`place_wood` for a `wood` part). Press ▶ to hear
what it plays.

The bottom half edits your mod's own sound events, saved to `sounds/<mod id>.json`:

- **+ Event from clip…** imports an `.ogg` or `.wav` into the mod's `sounds/` folder and makes an
  event for it. **Import…** next to **Clips** adds more clips; each play picks one at random.
- Set the bus, whether it loops, volume and pitch with their random variation, how far it carries, and
  for a loop its parameters.
- **▶ Preview** plays the event as it stands, before you save. **Save** checks it with the game's
  rules and writes the file; problems are listed under it.

Saved events can be bound straight away. See [Sound Events](sound-events.md) for every field.

## 9. Give it a recipe

Players craft parts at stations. The **Recipes** tab lists the recipes in your mod that make the open part.

1. Press **New recipe**. The fields start with the part as the output. You don't need to export first: the
   recipe is checked against the part as it stands in the editor.
2. Fill them in:
   - **Id** is the recipe's id, usually the part's id (`crate`).
   - **Name** is optional; empty uses the output's name.
   - **Stations** lists the stations that can craft it, separated by commas: `fabricator, portable_fabricator`.
   - **Time** is the seconds one craft takes at speed 1.
   - **Order** places it in the station's list. Take the number from its group in
     [Recipe order](part-data-schema.md#recipe-order): a crate is storage (1700), so after vanilla's
     Dispenser (1780) it gets `1790`.
3. Under **Inputs**, press **+ Input** for each item it uses up: type the item id (`scrap_wood_block`) and the
   count. Under **Outputs**, **+ Output** adds more items it makes. **×** removes a row.
4. Press **Save recipe**. It is checked exactly as the game loads it and written to `recipes/<id>.json`;
   problems show under the preview. **Delete** removes the recipe's file after asking.

In a recipe, a bare item or station id is vanilla's (`scrap_wood_block`, `fabricator`); your own items are
written with your mod id (`my_mod:crate`). See [Recipes](part-data-schema.md#recipes). To edit recipes
without opening a part, or to change another mod's recipe, use the [Recipe Editor](mod-tools.md#recipe-editor).

## 10. Export

Press **Export** (Ctrl+S). The editor checks the part with the same rules the game uses when it
loads mods. Problems appear in the list under the preview, with errors first. Fix them and export
again.

A successful export writes `parts/<part id>.json`, the baked mesh, and the editor's working copy.
If you rename a part's id, the old part file is replaced. Meshes are kept, because another part may
use them.

## 11. Test in the sandbox

Press **Test in Sandbox**. The editor exports, reloads mods and opens the Test Sandbox, where your part
drops in front of you. Click the world to look around; WASD moves and Space jumps. Esc frees the mouse,
and Esc again (or the window's X) closes the sandbox and returns you to the editor with your part still open.

- Pick any loaded part from the list (type in **Filter parts** to narrow it) and press **Spawn**.
- Pick a bot from the list under it and press **Spawn bot** to put it in front of you.
- **Clear all** removes every Creation and bot, after asking. **Reload mods** reads every mod again, so
  exports and edits made since show up.
- **Show collision and centre of mass** draws each body's collision shapes and a marker at its centre of
  mass. **Show joints** draws each joint's axis, range and state. **Show forces** draws the forces and
  torques parts put on their bodies.
- **Add local client** hosts the sandbox and starts another copy of the game in the same window,
  joined as a client. You can add up to three, and **Remove client** disconnects the last one. Press
  **F6** to switch which one you control (the key is "Test Sandbox: switch player" in Options, Controls).
  The host simulates everything, and clients send their spawns to the host to validate, exactly as in a
  real multiplayer game. Use this to check that your part works for everyone, not just the host.

You can also open the sandbox from the main menu under **Mod tools** with **Test Sandbox**.

## 12. Play it

The sandbox is for quick checks. To try your part the way players will:

1. On the main menu, open **Mod Manager**. Make sure your mod is ticked, then press **Check all content**:
   it loads every kind of content the way a world starts and lists each problem with its file and line.
   Press **Apply** if you changed anything.
2. Press **New Game**, choose **Creative**, and press **Create**. In Creative, open the inventory (**I**) and
   drag your part onto the hotbar. In **Survival**, craft it with the recipe you made at your Portable Fabricator (every player starts with one) or a Fabricator.
3. To check multiplayer, press Esc, then **Open to LAN**, and join from a second copy of the game with
   **Join Game**. The second copy needs the same mods in the same order.

When it works, the [Packaging Guide](packaging-guide.md) shows how to export your mod as a package to share.

## 13. Many similar parts: batches

When a mod has many parts that differ only in a few numbers, such as a set of blocks and their
wedges, describe them once in a **batch file** and let the editor make them all. A batch lives in
your mod at `editor/batches/<name>.json`, and the game never reads it.

```json
{
  "format_version": 1,
  "parts": [
    {
      "file": "parts/blocks/oak_block.json",
      "template": "block",
      "set": { "name": "Oak Block", "mass": 4, "visual": { "color": "#a07040" } },
      "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }]
    },
    {
      "file": "parts/blocks/oak_wedge.json",
      "base": "oak_block",
      "set": { "name": "Oak Wedge", "mass": 2 },
      "model": [{ "kind": "wedge", "size": [0.25, 0.25, 0.25] }],
      "collision_from_model": true
    }
  ]
}
```

Each entry is one part, made exactly as you would by hand:

| Key | Meaning |
|-----|---------|
| `file` | The part file to write, under `parts/`. Its name is the part id. |
| `template` | A template from the **New…** dialog to start from: any of the 13 in [Start a part](#1-start-a-part) (`block`, `decor`, `item`, `bearing`, …). |
| `base` | Or an earlier part in the same batch to start from, so a wedge inherits everything from its block. |
| `set` | Part keys to change. Objects such as `visual` merge key by key, other values replace, and `null` removes a key. |
| `model` | Model pieces, as in the editor source (see the [schema reference](part-data-schema.md#the-editor-source-file)). Omitted means the part has no model of its own. |
| `collision_from_model` | `true` makes the collision shape the convex hull of the part's own baked model. |
| `recipes` | Recipes that make the part, as in a `recipes/*.json` file. Each is saved to `recipes/<id>.json` after the parts. |

Press **Run batch…** and pick the file. Bot Behavior Editor and story batches run here too. The editor makes every part, bakes its model and exports it
with the same checks as **Export**, saves the recipes as the **Recipes** tab would, then opens the last
part. Problems are listed with the entry they come from. A mistake in the batch itself (a missing
template, a bad file name) stops it before anything is written; a part or recipe that fails its own
checks is skipped and the rest are still written. Running a batch again updates the parts and recipes
it made, so keep the batch as the place you edit them.

A batch can also run without opening the game window:

```sh
godot --headless --path game -- --part-batch mods/my_mod/editor/batches/blocks.json
```

It prints every problem and exits with 0 when every part and recipe exported. Vanilla's blocks and wedges are
made this way from `mods/vanilla/editor/batches/structural_blocks.json` (`tools/author-vanilla.sh`). The
batch's mod must sit in a mods folder next to the mods it uses (vanilla), because ids such as
`vanilla:meshes/…` and recipe stations are checked against them.

## Where to go next

- [Your first mod](first-mod.md): a whole mod, from a part and its recipe to an item script, a mod script,
  a translation, a rules patch and a package.
- [Mod tools](mod-tools.md): every tool under **Mod tools** on the main menu.
- [Part Data Schema Reference](part-data-schema.md): every key in a part file.
- [Lua API Reference](lua-api.md): everything a script can call.
- [Items Tutorial](items-tutorial.md): food, a gun with ammo and a thrown item.
- [Mod Scripts Cookbook](mod-scripts.md): events, timers and actions for the whole world.
- [Socket and Joint Guide](socket-joint-guide.md), [Logic and Power Tutorial](logic-power-tutorial.md) and
  [Aero and Propulsion Tutorial](aero-tutorial.md): moving machines.
- [Bot Tutorial](bot-tutorial.md): bots, behaviour trees and raids.
- [Quests and Dialogue](quests.md), [World Generation and POI Prefabs](world-generation.md) and
  [Player Characters](characters.md): story, places and skins.
- [Sound Events](sound-events.md) and [Translating](translating.md): sounds and language files.
- [Packaging Guide](packaging-guide.md): sharing your mod, overrides and game rules.
- The example mods in `examples/mods/`: `example_bearing`, `example_thruster`, `example_bots`,
  `example_items` and `example_modscript`. Copy one into your `mods/` folder to try it.
