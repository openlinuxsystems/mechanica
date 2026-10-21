"""Builds the Construction Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/construction_light/build_construction_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/construction_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/construction_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("construction_light", L.build_construction_light, half=(0.25, 0.25, 0.5), ortho=1.15,
                title="Construction Light (2 x 4 x 2 blocks)",
                lines=['Colony Works: a mustard work lamp with a wire guard on a steel-blue tripod mast, pitched down 21.8 degrees.',
                       'Lights the ground in front of it (wide spot); the cable runs down the mast to the jack on the hub.'])
