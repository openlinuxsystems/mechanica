"""Builds the Beehive (parts map batch 16; tools/part_kit/station_kit.py has the design).

    python3 tools/beehive/build_beehive.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/beehive.glb`` (its ``idle_loop``, ``start``, ``work_loop`` and ``complete`` clips: the
bees) and ``mods/vanilla/textures/items/beehive_icon.png``; with ``--render`` the review views of each state and the
sheet. Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("beehive", S.build_beehive, half=(0.375, 0.375, 0.5), ortho=1.25, icon_target=(0, 0, 0),
          title="Beehive (3 x 4 x 3 blocks)",
          lines=['Settler Patchwork: sea-teal, bone and mustard hive boxes on a scrap-plank stand with a feeder hatch, a zinc gable',
                 'roof and the standard two-way IO port on the right. Idle: two bees crawl on the landing board. Working: seven fly loops.'])
