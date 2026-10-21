"""Builds the Protector Sign (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/protector_sign/build_protector_sign.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/protector_sign.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/protector_sign_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("protector_sign", L.build_protector_sign, half=(0.375, 0.125, 0.25), ortho=0.85,
                title="Protector Sign (3 x 2 x 1 blocks, new)",
                lines=['Colony Works: a graphite lightbox in a steel-blue frame on wall brackets; a steel-blue shield and a signal-red',
                       "band on its bone face. Lit, the face and the shield's chevron glow (omni, warm white). Its base is its back."])
