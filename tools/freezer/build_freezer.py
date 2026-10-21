"""Builds the Freezer (parts map batch 7; tools/part_kit/station_kit.py has the design).

    python3 tools/freezer/build_freezer.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/freezer.glb`` (its ``idle_loop``, ``start``, ``work_loop`` and ``complete`` clips) and ``mods/vanilla/textures/items/freezer_icon.png``;
with ``--render`` the review views of each state and the sheet. Blender's Python module (bpy) runs it; nothing is
downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("freezer", S.build_freezer, half=(0.375, 0.375, 0.5), ortho=1.25, icon_target=(0, 0, 0),
          title="Freezer (3 x 4 x 3 blocks)",
          lines=['Colony Works: a bone-enamel cabinet, steel-blue door with a frost window and a chrome lever, a dial and a cyan pip,',
                 'the compressor fan on the back and the standard two-way IO port on the right. Freezing: the window glows, the pip lights.'])
