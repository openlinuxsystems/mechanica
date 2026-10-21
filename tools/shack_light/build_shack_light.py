"""Builds the Shack Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/shack_light/build_shack_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/shack_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/shack_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("shack_light", L.build_shack_light, half=(0.25, 0.25, 0.25), ortho=0.65,
                title="Shack Light (2 x 2 x 2 blocks, new)",
                lines=['Settler Patchwork: a bare bulb under a zinc shade (enamel inside, a riveted rust patch) on a scrap-wood block.',
                       'Hangs from its top; warm yellow (omni) with a brightness setting. Shown from below.'])
