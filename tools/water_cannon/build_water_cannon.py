"""Builds the Water Cannon (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/water_cannon/build_water_cannon.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/water_cannon.glb`` (with its ``fire`` clips) and
``mods/vanilla/textures/items/water_cannon_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("water_cannon", W.build_water_cannon, half=(0.25, 0.375, 0.25), ortho=0.85, icon_target=(0, 0, -0.05),
          title="Water Cannon (2 x 2 x 3 blocks)",
          lines=["Salvager Tech: a pressure tank with glass windows showing its water on a turret foot, a copper-banded nozzle barrel,",
                 "the hose and logic jack at the back. The fire clip kicks the barrel back and throws a burst of water."])
