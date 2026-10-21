"""Builds the Bathtub (parts map batch 4; tools/part_kit/furniture_kit.py has the design).

    python3 tools/bathtub/build_bathtub.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/bathtub.glb`` and ``mods/vanilla/textures/items/bathtub_icon.png``; with
``--render`` the review views and sheet. Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import furniture_kit as F  # noqa: E402

if __name__ == "__main__":
    K.run("bathtub", F.build_bathtub, levels=(None,), half=(0.375, 0.875, 0.375), ortho=1.9, icon_target=(0, 0, -0.05),
          title="Bathtub (3 x 3 x 7 blocks)",
          lines=["Settler Patchwork: a chipped clawfoot tub with a plank seat across it, zinc taps and a mustard towel.",
                 "Feet on the tub's floor, the seat 0.17 m above it (the sit_low clip, legs out); 10 links into the junction box."])
