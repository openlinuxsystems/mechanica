"""Build Corn's game files (crops map, batch K5), as build_crops.py does the farmed crops'. Corn has no seed: it grows
wild, so there is no packet and no crop rig, and the wild plant is a world prop.

It writes, procedurally (no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/corn.glb`` - the ear on the ground or placed, lying, origin at the middle of its base.
* ``mods/vanilla/meshes/items/corn_held.glb`` - in the hand: ``eat_0`` (whole), ``eat_1`` and ``eat_2`` (after the
  first and second bite), which the game swaps while the eat gesture plays.
* ``mods/vanilla/meshes/world/props/wild_corn.glb`` - the wild clump of stalks, one mesh (food_lib.merged()).
* ``mods/vanilla/textures/items/corn_icon.png`` - the 256 x 256 icon.
* the ``held`` entry for corn.json, printed (it goes in the consumables batch).

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_corn.py                  # the game files
    python tools/food/build_corn.py --render OUT_DIR # also review sheets: the ear (three-quarter, back, icon, bites, in
                                                     # hand, first person) and the wild clump

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))
sys.path.insert(0, str(HERE.parent / "consumables"))

import food_lib as fl  # noqa: E402,I001  (first: it imports bpy, which makes bmesh and mathutils importable)
import build_crops as bcr  # noqa: E402
import build_tomato as bt  # noqa: E402
import corn  # noqa: E402

WILD_DECIMATE = 1.0     # the clump is already light (some 7k triangles): kept whole

# The ear lies across the palm like the banana, its bitten tip past the little finger and its husk bundle by the thumb.
bcr.CROPS["corn"] = corn
bcr.NAMES["corn"] = "Corn"
bcr.BATCH["corn"] = "K5"
bcr.HOLD["corn"] = (("-w", "d"), (0.004, 0.02, 0.012))


def build():
    fl.reset()
    whole = bcr.centred(corn.build("Corn"))
    bt.export(fl.tree(whole), bt.MESHES / "items" / "corn.glb")
    fl.auto_icon(fl.tree(whole), bt.ICONS / "corn_icon.png", bt.ICON_VIEW, fill=1.1)

    fl.reset()
    held = fl.empty("CornHeld")
    shift = None
    for k in range(3):
        stage = corn.build(f"eat_{k}", bites=k)
        if shift is None:
            before = stage.location.copy()
            shift = bcr.centred(stage).location - before
        else:
            stage.location += shift     # every stage where the whole ear is, so the bites do not move it in the hand
        stage.parent = held
    bt.export(fl.tree(held), bt.MESHES / "items" / "corn_held.glb")

    fl.reset()
    wild = fl.merged(corn.build_wild(), "WildCorn", ratio=WILD_DECIMATE)
    bt.export([wild], bt.MESHES / "world" / "props" / "wild_corn.glb")
    print("held corn:", json.dumps(bt.held_entry(bcr.hold_matrix("corn"))))


def render_wild(out):
    """The wild clump: three-quarter, side and from above, and close on an ear."""
    fl.reset()
    wild = fl.merged(corn.build_wild(), "WildCorn", ratio=WILD_DECIMATE)
    tris = sum(len(p.vertices) - 2 for p in wild.data.polygons)
    objs = [wild]
    files = [fl.render_view(objs, out / "wild_corn_3q.png", (1.2, -1.8, 0.9), ortho=2.7, target=(0, 0, 1.1), size=900),
             fl.render_view(objs, out / "wild_corn_side.png", (-1.8, -0.4, 0.25), ortho=2.7, target=(0, 0, 1.1), size=900),
             fl.render_view(objs, out / "wild_corn_top.png", (0.15, -0.2, 1.8), ortho=2.4, target=(0, 0, 0.8), size=900),
             fl.render_view(objs, out / "wild_corn_ear.png", (1.0, -1.6, 0.5), ortho=0.7, target=(0.05, 0.0, 1.0), size=900)]
    path = out / "wild_corn_sheet.png"
    fl.sheet(files, ["three-quarter", "side, low", "from above", "an ear and its silk"], path, 2,
             f"WILD CORN (wild_corn.glb): one mesh, {tris} triangles, about 2 m tall", cell=760)
    print(f"wrote {path}")


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    build()
    if out:
        out.mkdir(parents=True, exist_ok=True)
        bcr.render_sheet("corn", out)
        render_wild(out)


if __name__ == "__main__":
    main(sys.argv[1:])
