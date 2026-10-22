"""Builds the Interior Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/interior_light/build_interior_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/interior_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/interior_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("interior_light", L.build_interior_light, half=(0.25, 0.25, 0.125), ortho=0.62,
                title="Interior Light (2 x 1 x 2 blocks)",
                lines=['Salvager Tech: a square ceiling panel, four frosted cells in a gunmetal cross on a hull housing with a copper line.',
                       'Hangs from its top; lights the room round it (omni). Shown from below.'])
