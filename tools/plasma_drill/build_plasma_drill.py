"""Builds the Plasma Drill (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/plasma_drill/build_plasma_drill.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/plasma_drill.glb`` (with its ``on`` and ``off`` clips) and
``mods/vanilla/textures/items/plasma_drill_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("plasma_drill", W.build_plasma_drill, half=(0.25, 0.375, 0.25), ortho=0.9, icon_target=(0, 0.05, -0.05),
          title="Plasma Drill (2 x 2 x 3 blocks)",
          lines=["Salvager Tech: a vented capacitor case and an emitter barrel wound with five copper coils on a turret foot, the lens at the front.",
                 "On: the coils, the emitter and its beam glow pale blue-white and the cyan power pip lights (the pose readout's on and off clips)."])
