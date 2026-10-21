"""Builds the Sticky Wheel (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/sticky_wheel/build_sticky_wheel.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/sticky_wheel.glb`` (with its ``on`` and ``off`` clips) and
``mods/vanilla/textures/items/sticky_wheel_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("sticky_wheel", W.build_sticky_wheel, half=(0.375, 0.375, 0.125), ortho=1.0, icon_target=(0, 0, 0.0),
          title="Sticky Wheel (3 x 1 x 3 blocks)",
          lines=["A ribbed rubber tyre coated in green gel on a Salvager rim, a glass chemical reservoir and pump on its hub with copper",
                 "feed pipes out to the tread. On: the gel glistens and the cyan pump pip lights (the pose readout's on and off clips)."])
