"""Builds the Neon Sign (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/neon_sign/build_neon_sign.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/neon_sign.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/neon_sign_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("neon_sign", L.build_neon_sign, half=(0.375, 0.125, 0.375), ortho=0.9,
                title="Neon Sign (3 x 3 x 1 blocks, new)",
                lines=['Settler Patchwork: neon tubes bent into a sprout in a ring (fresh produce, a pictogram, no text) on a plank',
                       'board with rusty rails and a rusty transformer. Lit mint; brightness setting. Its base is its back.'])
