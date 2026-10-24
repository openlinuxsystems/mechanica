"""Build the Field Kitchen's burgers (consumables batch C4, consumables/CONSUMABLES_MAP.md items 9 and 10): the Garden Burger
and the Colony Burger (tools/food/burger.py has the design).

For each it writes, procedurally (no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/<id>.glb`` - the burger on the ground or in a pile, standing on z = -0.125 inside the
  0.25 m cube (as the other consumables);
* ``mods/vanilla/meshes/items/<id>_held.glb`` - in the hand, its base at the origin: ``eat_0`` (whole), ``eat_1`` and
  ``eat_2`` (after the first and second bite), which the game swaps while the eat gesture plays, as the produce's;
* ``mods/vanilla/textures/items/<id>_icon.png`` - 256 x 256;
* the ``held`` offset and rotation of each, printed (they go in mods/vanilla/editor/batches/consumables.json).

    pip install bpy==5.2.2 numpy pillow
    python tools/consumables/build_burgers.py [garden_burger] [colony_burger]
    python tools/consumables/render_burgers.py OUT_DIR       # review sheets (model, icon, held, bite stages)

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(REPO / "tools" / "food"))
sys.path.insert(0, str(REPO / "tools" / "characters"))

import bpy  # noqa: E402,I001
from mathutils import Matrix, Vector  # noqa: E402

import build_tomato as bt  # noqa: E402
import burger  # noqa: E402
import food_lib as fl  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FLOOR = -0.125
ICON_VIEW = (1.25, -1.6, 1.15)
ITEMS = {"garden_burger": "Garden Burger", "colony_burger": "Colony Burger"}
# How each sits in the right palm (render_roots_design.hold_matrix's frame, as the potato): its x along -d, its base on
# the palm (z the palm's normal), at (along d, along w, off the palm) from the grip point.
HOLD = {"garden_burger": (0.024, 0.0, 0.012), "colony_burger": (0.024, 0.0, 0.012)}


def placement(item):
    """Rest-pose (Blender world) placement of the burger's base in the right palm, its bitten +X edge to the mouth."""
    import build_characters as bc
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    x = -d
    z = n
    y = z.cross(x)
    a, b, c = HOLD[item]
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(bc.grip_point("r")) + d * a + w * b + n * c
    return m


def build(item):
    fl.reset()
    whole = burger.build(item, "".join(p.title() for p in item.split("_")))
    whole.location.z = FLOOR
    bpy.context.view_layer.update()
    objs = fl.tree(whole)
    c, size = fl.bounds(objs)
    bt.export(objs, MESHES / f"{item}.glb")
    fl.auto_icon(objs, ICONS / f"{item}_icon.png", ICON_VIEW, fill=1.12 if item == "garden_burger" else 1.3)
    fl.reset()
    held = fl.empty("".join(p.title() for p in item.split("_")) + "Held")
    m = burger.mats()
    for k in range(3):
        stage = burger.build(item, f"eat_{k}", bites=k, m=m)
        stage.parent = held
    bt.export(fl.tree(held), MESHES / f"{item}_held.glb")
    print(f"{item}: {size * 100:.1f} cm across at most")
    return bt.held_entry(placement(item))


def main(argv):
    only = [a for a in argv if a in ITEMS] or list(ITEMS)
    held = {item: build(item) for item in only}
    for item, entry in held.items():
        print(f"held {item}:", json.dumps(entry))


if __name__ == "__main__":
    main(sys.argv[1:])
