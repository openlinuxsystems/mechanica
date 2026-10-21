"""Builds the Controller at its five levels (parts map batch 4; tools/part_kit/logic_kit.py has the design).

    python3 tools/controller/build_controller.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/controller_<level>.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout
plays) and ``mods/vanilla/textures/items/controller_<level>_icon.png``; with ``--render`` the review views and sheet.
Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import logic_kit as L  # noqa: E402

if __name__ == "__main__":
    K.run("controller", L.build_controller, half=(0.125, 0.125, 0.125), ortho=0.35, icon_target=(0, 0, -0.05),
          title="Controller (1 block)", bake=L.bake,
          lines=["On the Switch's base plate: ten step cells on top, one fitted per output its level drives (1, 2, 4, 8, 10),",
                 "lit teal with the status lamp while it runs (the pose readout's on and off clips)."])
