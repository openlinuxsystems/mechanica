"""Builds the Fluorescent Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/fluorescent_light/build_fluorescent_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/fluorescent_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/fluorescent_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("fluorescent_light", L.build_fluorescent_light, half=(0.5, 0.25, 0.125), ortho=1.1,
                title="Fluorescent Light (4 x 1 x 2 blocks, new)",
                lines=['Colony Works: a bone sheet-steel batten with steel-blue trims, two frosted tubes in graphite end blocks',
                       'under a reflector. Hangs from its top; lights the room (omni). Shown from below.'])
