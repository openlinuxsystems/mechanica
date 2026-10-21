"""Builds the parts map's batch 11 and 12 models: stairs, railings, the Net Fence, windows, windshields, the Concave
Wedge, signs, the Antenna, the Traffic Cone, the Banner Holder and the Billboard.

    pip install bpy==5.2.2 numpy pillow
    python tools/parts_building/build_parts.py [--only id,id] [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png`` for each part (only the
ones named after ``--only``); with ``--render`` each part's review views and sheet into OUT_DIR. The outputs are
committed: re-run this after changing the models and commit the result. Blender runs as a Python module; nothing is
downloaded. The design notes are in tools/part_kit/building_kit.py.
"""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import building_kit as B  # noqa: E402
import stairs_windows  # noqa: E402
import signs  # noqa: E402

PARTS = stairs_windows.PARTS + signs.PARTS
# Parts whose +y is the front of what they are built on (vehicle glass, the Ramp Block, which rises toward its builder as
# it always has); the rest, the Concave Wedge too (high at local -Z, as the block wedges), turn their face toward the
# player who builds them.
VEHICLE_FRONT = {"windshield", "windshield_large", "side_window", "ramp_block", "stair_banister"}

if __name__ == "__main__":
    for part, build, half, ortho, title, lines in B.selected(sys.argv, PARTS):
        B.run(part, build, half=half, ortho=ortho, title=title, lines=lines, faces_builder=part not in VEHICLE_FRONT)
