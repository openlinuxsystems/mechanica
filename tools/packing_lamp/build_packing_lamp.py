"""Builds the Packing Lamp (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/packing_lamp/build_packing_lamp.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/packing_lamp.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/packing_lamp_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("packing_lamp", L.build_packing_lamp, half=(0.5, 0.125, 0.125), ortho=1.1,
                title="Packing Lamp (4 x 1 x 1 blocks, new)",
                lines=['Colony Works: a frosted tube in a steel-blue cage with rubber bumpers, on two clip feet.',
                       'Its light takes its paint colour (warm white unpainted). Omni.'])
