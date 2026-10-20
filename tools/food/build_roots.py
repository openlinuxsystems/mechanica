"""Build the carrot's and the potato's game files (Food design thread, approved by Chase 2026-10-20), as
build_tomato.py does the tomato's.

For each crop (``carrot``, ``potato``) it writes, procedurally:

* ``mods/vanilla/meshes/items/<crop>.glb`` - the crop on the ground or placed, lying, origin at the middle of its base.
* ``mods/vanilla/meshes/items/<crop>_held.glb`` - in the hand: ``eat_0`` (whole), ``eat_1`` and ``eat_2`` (after the
  first and second bite), which the game swaps while the eat gesture plays.
* ``mods/vanilla/meshes/items/<crop>_seed.glb`` - the shared seed packet with the crop's label.
* ``mods/vanilla/meshes/crops/<crop>_plant.glb`` - the plant with its ``grow``, ``wilt`` and ``sway`` clips
  (crop_rig.py), stake and crop tag included.
* ``mods/vanilla/textures/items/<crop>_icon.png`` and ``<crop>_seed_icon.png`` - 256 x 256 icons.
* the ``held`` entries for <crop>.json and <crop>_seed.json, printed (they go in the survival batch).

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_roots.py [carrot] [potato]

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))

import build_characters as bc  # noqa: E402,I001
import build_tomato as bt  # noqa: E402
import carrot  # noqa: E402
import crop_rig  # noqa: E402
import food_lib as fl  # noqa: E402
import potato  # noqa: E402
import render_roots_design as rrd  # noqa: E402
import seed_packet as sp  # noqa: E402

CROPS = {"carrot": carrot, "potato": potato}


def build(crop):
    mod = CROPS[crop]
    fl.reset()
    whole = mod.build(crop.title())
    export_lying(whole, bt.MESHES / "items" / f"{crop}.glb")
    fl.auto_icon(fl.tree(whole), bt.ICONS / f"{crop}_icon.png", bt.ICON_VIEW, fill=1.15)

    fl.reset()
    held = fl.empty(crop.title() + "Held")
    for k in range(3):
        stage = mod.build(f"eat_{k}", bites=k)
        stage.parent = held
    bt.export(fl.tree(held), bt.MESHES / "items" / f"{crop}_held.glb")

    fl.reset()
    label = Path(tempfile.mkdtemp()) / f"label_{crop}.png"
    sp.label_image(bt.ICONS / f"{crop}_icon.png", mod.LABEL_COLOR, label)
    packet = sp.build(crop.title() + "Seed", label)
    bt.export(fl.tree(packet), bt.MESHES / "items" / f"{crop}_seed.glb")
    fl.auto_icon(fl.tree(packet), bt.ICONS / f"{crop}_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)

    fl.reset()
    rig = mod.build_rig(label)
    crop_rig.bake(rig)
    bt.export(fl.tree(rig.root), bt.MESHES / "crops" / f"{crop}_plant.glb", animations=True)

    print(f"held {crop}:", json.dumps(bt.held_entry(rrd.hold_matrix(bc, crop))))
    print(f"held {crop}_seed:", json.dumps(bt.held_entry(bt.packet_placement())))


def export_lying(root, path):
    """The crop as ``build`` lays it (on its side, base at z = 0), centred on its footprint."""
    c, _ = fl.bounds(fl.tree(root))
    root.location.x -= c.x
    root.location.y -= c.y
    bt.export(fl.tree(root), path)


def main(argv):
    for crop in argv or list(CROPS):
        build(crop)


if __name__ == "__main__":
    main(sys.argv[1:])
