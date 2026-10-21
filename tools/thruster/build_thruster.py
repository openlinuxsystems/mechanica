"""Builds the Thruster at its five levels (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/thruster/build_thruster.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/thruster_<level>.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/thruster_<level>_icon.png``; with ``--render`` the review views and sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run_levels("thruster", W.build_thruster, W.thruster_bake, half=(0.125, 0.125, 0.125), ortho=0.36, icon_target=(0, 0, -0.02),
                 title="Thruster (1 block, five levels)",
                 lines=["On the Switch's base plate: a turned chamber and exhaust bell (wider with each level's power steps), copper cooling rings,",
                        "a fuel line and the logic jack; level 2 a third ring, 3 steel fins, 4 a bell collar and a cyan running light, 5 black chrome.",
                        "Firing (last two), the throat and the inside of the bell glow pale blue-white (the pose readout's on and off clips)."])
