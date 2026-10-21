"""Builds the Toilet (parts map batch 4; tools/part_kit/furniture_kit.py has the design).

    python3 tools/toilet/build_toilet.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/toilet.glb`` and ``mods/vanilla/textures/items/toilet_icon.png``; with
``--render`` the review views and sheet. Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import furniture_kit as F  # noqa: E402

if __name__ == "__main__":
    K.run("toilet", F.build_toilet, levels=(None,), half=(0.375, 0.5, 0.5), ortho=1.2, icon_target=(0, 0, -0.02),
          title="Toilet (3 x 4 x 4 blocks)",
          lines=["A classic US two-piece toilet in glossy white porcelain: elongated bowl, closed seat and lid, the tank with a",
                 "chrome flush handle on its left front, a shutoff valve and braided supply line at the wall side, bolt caps.",
                 "The lid's top 0.39 m above the floor the feet stand on (the sit clip); 10 links into the link box behind it."])
