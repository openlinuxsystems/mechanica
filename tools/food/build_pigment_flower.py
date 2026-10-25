"""Build the Pigment Flower's game files (crops map, batch K2), as build_cotton.py does Cotton's.

It writes, procedurally:

* ``mods/vanilla/meshes/items/pigment_flower.glb`` - the cut flower, lying, origin at the middle of its base.
* ``mods/vanilla/meshes/items/pigment_flower_held.glb`` - the cut flower standing, origin at the foot of its stem, which
  the game puts in the fist.
* ``mods/vanilla/meshes/items/pigment_flower_seed.glb`` - the shared seed packet with the flower's label.
* ``mods/vanilla/meshes/crops/pigment_flower_plant.glb`` - the plant with its ``grow``, ``wilt`` and ``sway`` clips
  (crop_rig.py), stake and crop tag included.
* ``mods/vanilla/meshes/world/props/wild_pigment_flower.glb`` - a ripe wild clump for meadows and forests, one decimated
  mesh (merged()).
* ``mods/vanilla/textures/items/pigment_flower_icon.png`` and ``pigment_flower_seed_icon.png`` - 256 x 256 icons.
* the ``held`` entries for pigment_flower.json and pigment_flower_seed.json, printed (they go in the consumables batch).

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_pigment_flower.py                  # the game files
    python tools/food/build_pigment_flower.py --render OUT_DIR # also review renders (the flower, the packet, the
                                                               # plant's stages and wilt, the wild clump)

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import math
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))

import build_tomato as bt  # noqa: E402,I001
import crop_rig  # noqa: E402
import food_lib as fl  # noqa: E402
import pigment_flower as pf  # noqa: E402
import seed_packet as sp  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

ICON_VIEW = (0.35, -1.6, 1.25)
WILD_DECIMATE = 0.1           # the wild clump keeps a tenth of its triangles (some 8k, as a scattered prop)
HELD_NOD = 0.0                # in hand the head nods toward +X: toward the holder's eyes (placement())
GRIP = 0.06                   # the fist round the stem this far up from its foot (m)


def icon_pose(name="PigmentFlowerIcon"):
    """The flower for its icon: its head on a short stalk with one leaf, aslant, the head turned to the camera."""
    root = fl.empty(name)
    up = pf.build_upright(name + "_Flower", root, stem=0.075)
    up.rotation_euler = (math.radians(8), math.radians(38), 0)
    return root


def placement():
    """Rest-pose (Blender world) placement of the standing flower's foot in the right fist: the stem up the thumb (as the
    hand holds a handle), the fist round it GRIP up from its foot, the head nodding away from the palm."""
    import build_characters as bc
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    z = w.normalized()
    y = (n - z * n.dot(z)).normalized()
    x = y.cross(z)
    basis = Matrix((x, y, z)).transposed().to_4x4()
    return Matrix.Translation(Vector(bc.grip_point("r")) - z * GRIP) @ basis @ Matrix.Rotation(math.pi, 4, "Z")


def label_path():
    label = Path(tempfile.mkdtemp()) / "label_pigment_flower.png"
    sp.label_image(bt.ICONS / "pigment_flower_icon.png", pf.LABEL_COLOR, label)
    return label


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    if out:
        out.mkdir(parents=True, exist_ok=True)
    fl.reset()
    whole = pf.build("PigmentFlower")
    bt.export(fl.tree(whole), bt.MESHES / "items" / "pigment_flower.glb")
    if out:
        c, s = fl.bounds(fl.tree(whole))
        fl.render_view(fl.tree(whole), out / "flower_lying.png", (0.8, -1.0, 0.7), ortho=s * 1.25, target=tuple(c), size=900)

    fl.reset()
    icon = icon_pose()
    fl.auto_icon(fl.tree(icon), bt.ICONS / "pigment_flower_icon.png", ICON_VIEW, fill=1.08)

    fl.reset()
    held = pf.build_upright("PigmentFlowerHeld", nod=HELD_NOD)
    bt.export(fl.tree(held), bt.MESHES / "items" / "pigment_flower_held.glb")
    if out:
        c, s = fl.bounds(fl.tree(held))
        fl.render_view(fl.tree(held), out / "flower_three_quarter.png", (0.7, -1.3, 0.9), ortho=s * 1.2, target=tuple(c), size=900)
        # from behind and below the nodding head: the sepal cup and the petals' undersides
        fl.render_view(fl.tree(held), out / "flower_back.png", (-0.3, 1.0, -0.35), ortho=s * 1.2, target=tuple(c), size=900,
                       ground=False)
        fl.render_view(fl.tree(held), out / "flower_head.png", (0.1, -0.55, 1.4), ortho=0.16, target=(0, -0.03, 0.28), size=900)

    fl.reset()
    label = label_path()
    packet = sp.build("PigmentFlowerSeed", label)
    bt.export(fl.tree(packet), bt.MESHES / "items" / "pigment_flower_seed.glb")
    fl.auto_icon(fl.tree(packet), bt.ICONS / "pigment_flower_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)
    if out:
        c, s = fl.bounds(fl.tree(packet))
        fl.render_view(fl.tree(packet), out / "seed_front.png", (0.5, -1.6, 0.6), ortho=s * 1.25, target=tuple(c), size=900)
        fl.render_view(fl.tree(packet), out / "seed_back.png", (-0.5, 1.6, 0.6), ortho=s * 1.25, target=tuple(c), size=900)

    fl.reset()
    rig = pf.build_rig(label)
    if out:
        soil = fl.soil()
        for g in (0.5, 1.5, 2.5, 3.2, 3.6, 4.0):
            crop_rig.pose(rig, g)
            fl.render_view(fl.tree(rig.root) + soil, out / f"plant_stage_{g:.1f}.png", (1.0, -1.6, 0.9), ortho=1.0,
                           target=(0, 0, 0.32), size=700)
        crop_rig.pose(rig, 4.0, 1.0)
        fl.render_view(fl.tree(rig.root) + soil, out / "plant_wilted.png", (1.0, -1.6, 0.9), ortho=1.0,
                       target=(0, 0, 0.32), size=700)
        crop_rig.pose(rig, 4.0, 0.0)
        for o in soil:
            _remove(o)
    crop_rig.bake(rig)
    bt.export(fl.tree(rig.root), bt.MESHES / "crops" / "pigment_flower_plant.glb", animations=True)

    fl.reset()
    wild = merged(pf.build_wild(), "WildPigmentFlower", ratio=WILD_DECIMATE)
    bt.export([wild], bt.MESHES / "world" / "props" / "wild_pigment_flower.glb")
    if out:
        c, s = fl.bounds([wild])
        fl.render_view([wild], out / "wild_pigment_flower.png", (1.2, -1.8, 1.0), ortho=s * 1.2, target=tuple(c), size=900)

    print("held pigment_flower:", json.dumps(bt.held_entry(placement())))
    print("held pigment_flower_seed:", json.dumps(bt.held_entry(bt.packet_placement())))


def merged(root, name, ratio=1.0):
    """food_lib.merged (kept here for the K2 callers)."""
    return fl.merged(root, name, ratio)


def _remove(ob):
    import bpy
    if ob.name in bpy.data.objects:
        for o in [*ob.children_recursive, ob]:
            bpy.data.objects.remove(o)


if __name__ == "__main__":
    main(sys.argv[1:])
