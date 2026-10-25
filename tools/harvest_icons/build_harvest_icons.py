"""Render the icons of the harvest pieces (parts audit D7): the logs, the rods and the mining pieces.

Their models already exist (tools/trees/build_trees.py, tools/rocks/build_rocks.py); this imports each ``.glb`` and
renders it with the shared item icon renderer (``build_bearing_engine.render_icon``: the same three-light studio,
orthographic camera, 512 px render filtered down to 256 x 256 on a clear background as every other item icon),
framed like the crafting-material icons (``build_materials.icon``).

* ``mods/vanilla/textures/items/<id>_icon.png`` for ``wood_log``, ``birch_log``, ``pine_log``, ``wood_rod``,
  ``pine_rod``, ``stone_rod``, ``metal_rod``, ``stone_boulder``, ``metal_boulder``, ``stone_chunk`` and
  ``metal_chunk``.

The logs and rods lie along glTF Z (Blender -Y); each is turned on the ground so it runs corner to corner across the
tile, the same way for every piece, and the rods are drawn a little larger than the logs so their grain reads at
64 px. ``soil`` has no icon: its inventory form is the Soil Bag.

    pip install bpy==5.2.2 numpy pillow
    python tools/harvest_icons/build_harvest_icons.py              # every icon
    python tools/harvest_icons/build_harvest_icons.py pine_rod     # only the ones named

Re-run after rebuilding any of the models and commit the result. The render is deterministic (fixed Cycles seed).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Euler

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "materials"))
import build_materials as bm  # noqa: E402  (icon framing and glb import; its imports bring the icon renderer)

bbe = bm.bbe
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

ICON_VIEW = bm.ICON_VIEW       # three-quarter from the front right, from above, as the crafting materials
LONG_TURN = -12                # degrees about Z for the long pieces: lying corner to corner across the tile

# id: (mesh, turn about Z in degrees, fill: the frame's size over the piece's silhouette)
PIECES = {
    "wood_log": ("wood_log", LONG_TURN, 1.12),
    "birch_log": ("birch_log", LONG_TURN, 1.12),
    "pine_log": ("pine_log", LONG_TURN, 1.12),
    "wood_rod": ("wood_rod", LONG_TURN, 1.08),
    "pine_rod": ("pine_rod", LONG_TURN, 1.08),
    "stone_rod": ("stone_rod", LONG_TURN, 1.08),
    "metal_rod": ("metal_rod", LONG_TURN, 1.08),
    "stone_boulder": ("stone_boulder", 0, 1.18),
    "metal_boulder": ("metal_boulder", 0, 1.18),
    "stone_chunk": ("stone_chunk", 0, 1.18),
    "metal_chunk": ("metal_chunk", 0, 1.18),
}


def render(item):
    mesh, turn, fill = PIECES[item]
    bbe.reset_scene()
    root = bm.import_glb(MESHES / f"{mesh}.glb")
    root.rotation_euler = Euler((0.0, 0.0, math.radians(turn)))
    bpy.context.view_layer.update()
    path = ICONS / f"{item}_icon.png"
    bm.icon(bbe.mesh_tree(root), path, fill=fill, view=ICON_VIEW)
    print(f"wrote {path.relative_to(REPO)} ({path.stat().st_size // 1024} KiB)")


def main(argv):
    unknown = [a for a in argv if a not in PIECES]
    if unknown:
        sys.exit(f"unknown piece(s): {', '.join(unknown)}; known: {', '.join(PIECES)}")
    for item in argv or list(PIECES):
        render(item)


if __name__ == "__main__":
    main(sys.argv[1:])
