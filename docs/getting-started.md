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
- **Paintable**: whether players can paint the part.
- **Colour variants**: named colours a player can pick, each with an id. Press **+ Variant** to add
  one.

## 7. Behaviour

The **Behaviour** tab links a Lua **Script** from the mod's `scripts/` folder and free-form
**Properties** that the script reads. All of this is JSON, and balance numbers belong here rather
than in code. A plain block needs none of it.

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

## 9. Export

Press **Export** (Ctrl+S). The editor checks the part with the same rules the game uses when it
loads mods. Problems appear in the list under the preview, with errors first. Fix them and export
again.

A successful export writes `parts/<part id>.json`, the baked mesh, and the editor's working copy.
If you rename a part's id, the old part file is replaced. Meshes are kept, because another part may
use them.

## 10. Test in the sandbox

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

## Where to go next

- [Part Data Schema Reference](part-data-schema.md): every key in a part file.
- [Sound Events](sound-events.md): sound event files and part sounds.
- [Packaging Guide](packaging-guide.md): sharing your mod.
