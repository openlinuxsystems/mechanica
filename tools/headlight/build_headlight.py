"""Builds the Headlight (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/headlight/build_headlight.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/headlight.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/headlight_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("headlight", L.build_headlight, half=(0.125, 0.125, 0.125), ortho=0.36,
                title="Headlight (1 block)",
                lines=['Salvager Tech: a hull-dark can on a copper-screwed back plate, a hood over a frosted lens in a gunmetal bezel.',
                       'Shines out of its front (spot). The lens and the cyan pip light with the on clip; the jack on its left side takes links.'])
