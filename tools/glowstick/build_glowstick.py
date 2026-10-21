"""Builds the Glowstick (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/glowstick/build_glowstick.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/glowstick.glb`` and
``mods/vanilla/textures/items/glowstick_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("glowstick", L.build_glowstick, half=(0.125, 0.125, 0.125), ortho=0.3, clips=False,
                title="Glowstick (1 block, consumable)",
                lines=['A green snap-light with graphite caps and a lanyard loop. Always lit, so it has no clips.'])
