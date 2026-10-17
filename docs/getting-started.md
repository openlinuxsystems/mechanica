# Getting Started

This guide walks you through making your first part in the **Part Editor**. You'll make a crate
block, export it into a mod, and try it in the **Test Sandbox** with local clients. The editor is
built into the game. Open it from the main menu with **Part Editor**, and press **Help** in it to
read this page again.

Every vanilla block and part is authored the same way, so anything you see in the base game you can
make yourself.

## What a part is

A part is one JSON file in a mod's `parts/` folder, plus the meshes, textures, scripts and sounds it
points at. The editor writes that file for you. The [Part Data Schema Reference](part-data-schema.md)
explains every key in it.

A mod is a folder in the game's `mods/` folder:

```
mods/my_mod/
  mod.json              id, name, version, dependencies
  parts/crate.json      the part
  meshes/crate.glb      its model, baked by the editor
  editor/parts/crate.json   the editor's working copy (shapes and pieces)
```

When you export into a mod that doesn't exist yet, the editor creates the folder and a `mod.json`
for it.

## 1. Start a part

1. Press **New…**.
2. Fill in the dialog:
   - **Mod** is the mod folder to write into, for example `my_mod`.
   - **Part id** is the part's id inside the mod. Use lower case and underscores, for example `crate`.
   - **Name** is what players see.
   - **Template** is what to start from: `block` (a solid 1×1×1 block), `interactive` (a part with
     a socket to attach by), `decor`, or `item` (an inventory item that isn't placed).
3. Press **OK**. The preview shows the part on a 0.25 m grid. Right-drag to orbit, and use the mouse
   wheel to zoom.

To change an existing part, press **Open…** and pick its file in a mod's `parts/` folder.

## 2. Set its physical properties

The **Part** tab holds the part's identity and physics. Units are SI: kilograms, metres, seconds.

- **Mass** is in kg. Our crate is wood, so try `4`.
- **Durability**, **Friction** and **Buoyancy** use the scales described in the schema reference.
- **Size** is the footprint in 0.25 m grid cells. A 1×1×1 block is a 0.25 m cube. Make the crate
  `2, 2, 2` for a 0.5 m cube.

Every change can be undone with **Undo** (Ctrl+Z) and redone with **Redo** (Ctrl+Shift+Z or Ctrl+Y).

## 3. Build the model

The **Model** tab lists the pieces that make up the mesh. You can import a glTF model, build one
from shapes, or mix both.

- **+ Box**, **+ Wedge**, **+ Cylinder** and **+ Sphere** add a shape. Select a piece to set its
  size, **Position**, **Rotation** (degrees) and **Scale**.
- Each piece has an **Operation**:
  - `union` adds the piece.
  - `subtract` cuts it out of the pieces above it.
  - `intersect` keeps only the overlap.
  **Up** and **Down** change the order.
- **Import mesh…** copies a `.glb` or `.gltf` file (and the buffers and textures a `.gltf` uses) into
  the mod's `meshes/` folder and adds it as a piece.

For the crate, set the box to `0.5, 0.5, 0.5`. Then add a second, slightly smaller box, set it to
`subtract`, and move it up a little to hollow out the top.

When you export, the pieces are baked into one `meshes/<part id>.glb`. A part made from a single
imported mesh with no changes just points at that mesh.

## 4. Collision

The **Collision** tab sets the shape physics uses. It is often simpler than the model.

- With no shape set, the part collides as a box filling its footprint. That is right for most blocks.
- `box`, `sphere`, `cylinder` and `capsule` take sizes in metres and an **Offset**.
- `convex` uses a mesh's convex hull.

Turn on **Collision** above the preview to see the shape drawn over the model.

## 5. Sockets

Sockets are where other parts attach. Plain blocks don't need any, because a block attaches to
neighbours on every face. Parts with joints (bearings, pistons, suspension) use typed sockets.

- Pick a side (**+X**, **-X**, **+Y**, **-Y**, **+Z** or **-Z**) and press **+ Socket on side** to add
  a socket at the centre of that face.
- Select a socket to set its **Id**, **Type**, **Position** and **Normal**. **Snap to face** moves
  it onto the nearest outer face.
- Turn on **Sockets** above the preview to see them, and **Connections** to see how each face of the
  part would join a neighbouring block. Green is a rigid weld; blue, orange and purple are joints;
  red means nothing would attach there.

## 6. Look

The **Look** tab sets the part's material:

- **Colour**, **Roughness** and **Metallic**.
- **Opacity**: below 1 the part is see-through, like glass.
- **Glow**: a colour the surface gives off, like a path light. It does not light anything else.
- **Paintable**: whether players can paint the part.
- **Colour variants**: named colours a player can pick, each with an id. Press **+ Variant** to add
  one.

## 7. Behaviour

The **Behaviour** tab links a Lua **Script** from the mod's `scripts/` folder, the part's **Sounds**,
and free-form **Properties** that the script reads. All of this is JSON, and balance numbers belong
here rather than in code. A plain block needs none of it. The [Lua API Reference](lua-api.md) lists
what scripts can call.

The **Script** tab edits that script. Pick a template (empty, thruster, bearing motor, wing or
distance sensor) and press **New from template**: a part without a script gets
`scripts/<part id>.lua`. The **API** list under the code shows everything a script can call;
double-click an entry to insert it. **Save & reload** (Ctrl+S in the code) writes the file and
checks it. A compile error is marked on its line. When the Test Sandbox is running the script, the
save reloads it into the running parts, keeping their `state`, and the **Console** shows what the
scripts print and every error. Export the part once so the sandbox knows it has a script.

## 8. Export

Press **Export** (Ctrl+S). The editor checks the part with the same rules the game uses when it
loads mods. Problems appear in the list under the preview, with errors first. Fix them and export
again.

A successful export writes `parts/<part id>.json`, the baked mesh, and the editor's working copy.
If you rename a part's id, the old part file is replaced. Meshes are kept, because another part may
use them.

## 9. Test in the sandbox

Press **Test in Sandbox**. The editor reloads mods and opens the Test Sandbox, where your part drops
in front of you.

- Pick any loaded part from the list (type in **Filter parts** to narrow it) and press **Spawn**.
  **Clear all** removes everything, and **Reload mods** picks up new exports.
- **Show collision and centre of mass** draws each part's physics shape and a marker at its centre
  of mass.
- **Add local client** hosts the sandbox and starts another copy of the game in the same window,
  joined as a client. You can add up to three. Press **Tab** to switch which one you control. The
  host simulates everything, and clients send their spawns to the host to validate, exactly as in a
  real multiplayer game. Use this to check that your part works for everyone, not just the host.
- **Back** returns to the editor with your part still open.

You can also open the sandbox from the main menu with **Test Sandbox**.

## 10. Many similar parts: batches

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
| `template` | A template from the **New…** dialog to start from (`block`, `decor`, `interactive`, `item`). |
| `base` | Or an earlier part in the same batch to start from, so a wedge inherits everything from its block. |
| `set` | Part keys to change. Objects such as `visual` merge key by key, other values replace, and `null` removes a key. |
| `model` | Model pieces, as in the editor source (see the [schema reference](part-data-schema.md#the-editor-source-file)). |
| `collision_from_model` | `true` makes the collision shape the convex hull of the part's own baked model. |
| `recipes` | Recipes that make the part, as in a `recipes/*.json` file. Each is saved to `recipes/<id>.json` after the parts. |

Press **Run batch…** and pick the file. The editor makes every part, bakes its model and exports it
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
made this way from `mods/vanilla/editor/batches/structural_blocks.json` (`tools/author-vanilla.sh`).

## Where to go next

- [Part Data Schema Reference](part-data-schema.md): every key in a part file.
- [Packaging Guide](packaging-guide.md): sharing your mod.
