"""Builds the Warehouse Spotlight (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/warehouse_spotlight/build_warehouse_spotlight.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/warehouse_spotlight.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/warehouse_spotlight_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("warehouse_spotlight", L.build_warehouse_spotlight, half=(0.375, 0.375, 0.375), ortho=1.05,
                title="Warehouse Spotlight (3 x 3 x 3 blocks)",
                lines=["Salvager Tech: the Spotlight's big brother, a floodlight drum with a carry handle on a heavy yoke and gusseted plate.",
                       'Shines out of its front (wide spot). Lens and pip light with the on clip.'])
