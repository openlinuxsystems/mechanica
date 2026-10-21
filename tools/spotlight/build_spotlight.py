"""Builds the Spotlight (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/spotlight/build_spotlight.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/spotlight.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/spotlight_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("spotlight", L.build_spotlight, half=(0.25, 0.25, 0.25), ortho=0.7,
                title="Spotlight (2 x 2 x 2 blocks)",
                lines=['Salvager Tech: a lamp can with copper cooling fins on a swivel yoke, a grille across its lens.',
                       'Shines out of its front (narrow spot). Lens and pip light with the on clip; the jack is on the base plate.'])
