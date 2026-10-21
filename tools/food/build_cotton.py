"""Build Cotton's game files (block list batch 2: the Carpet Block is woven from it), as build_roots.py does the
carrot's and the potato's.

It writes, procedurally:

* ``mods/vanilla/meshes/items/cotton.glb`` - the harvested bundle, lying, origin at the middle of its base.
* ``mods/vanilla/meshes/items/cotton_seed.glb`` - the shared seed packet with the cotton's label.
* ``mods/vanilla/meshes/crops/cotton_plant.glb`` - the plant with its ``grow``, ``wilt`` and ``sway`` clips
  (crop_rig.py), stake and crop tag included.
* ``mods/vanilla/meshes/world/props/wild_cotton.glb`` - a ripe wild clump for the Autumn Forest.
* ``mods/vanilla/textures/items/cotton_icon.png`` and ``cotton_seed_icon.png`` - 256 x 256 icons.

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_cotton.py                  # the game files
    python tools/food/build_cotton.py --render OUT_DIR # also review renders (the plant's stages, the wild clump)

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import build_tomato as bt  # noqa: E402,I001
import cotton  # noqa: E402
import crop_rig  # noqa: E402
import food_lib as fl  # noqa: E402
import seed_packet as sp  # noqa: E402


def export_lying(root, path):
    c, _ = fl.bounds(fl.tree(root))
    root.location.x -= c.x
    root.location.y -= c.y
    bt.export(fl.tree(root), path)


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    fl.reset()
    whole = cotton.build("Cotton")
    export_lying(whole, bt.MESHES / "items" / "cotton.glb")
    fl.auto_icon(fl.tree(whole), bt.ICONS / "cotton_icon.png", bt.ICON_VIEW, fill=1.15)
    if out:
        out.mkdir(parents=True, exist_ok=True)
        fl.render_view(fl.tree(whole), out / "cotton.png", (0.5, -0.7, 0.45), ortho=0.2, target=(0, 0, 0.06), size=700)

    fl.reset()
    label = Path(tempfile.mkdtemp()) / "label_cotton.png"
    sp.label_image(bt.ICONS / "cotton_icon.png", cotton.LABEL_COLOR, label)
    packet = sp.build("CottonSeed", label)
    bt.export(fl.tree(packet), bt.MESHES / "items" / "cotton_seed.glb")
    fl.auto_icon(fl.tree(packet), bt.ICONS / "cotton_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)

    fl.reset()
    rig = cotton.build_rig(label)
    if out:
        soil = fl.soil()
        for g in (0.5, 1.5, 2.5, 3.2, 4.0):
            crop_rig.pose(rig, g)
            fl.render_view(fl.tree(rig.root) + soil, out / f"plant_stage_{g:.1f}.png", (1.0, -1.6, 0.9), ortho=1.0,
                           target=(0, 0, 0.32), size=600)
        crop_rig.pose(rig, 4.0, 1.0)
        fl.render_view(fl.tree(rig.root) + soil, out / "plant_wilted.png", (1.0, -1.6, 0.9), ortho=1.0,
                       target=(0, 0, 0.32), size=600)
        crop_rig.pose(rig, 4.0, 0.0)
        for o in soil:
            bpy_remove(o)
    crop_rig.bake(rig)
    bt.export(fl.tree(rig.root), bt.MESHES / "crops" / "cotton_plant.glb", animations=True)

    fl.reset()
    wild = cotton.build_wild()
    bt.export(fl.tree(wild), bt.MESHES / "world" / "props" / "wild_cotton.glb")
    if out:
        fl.render_view(fl.tree(wild), out / "wild_cotton.png", (1.2, -1.8, 1.0), ortho=1.4, target=(0, 0, 0.4), size=700)

    print("held cotton_seed:", json.dumps(bt.held_entry(bt.packet_placement())))


def bpy_remove(ob):
    import bpy
    if ob.name in bpy.data.objects:
        for o in [*ob.children_recursive, ob]:
            bpy.data.objects.remove(o)


if __name__ == "__main__":
    main(sys.argv[1:])
