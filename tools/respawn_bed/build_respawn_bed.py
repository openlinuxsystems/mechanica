"""Builds the Respawn Bed (parts map batch 7; tools/part_kit/station_kit.py has the design).

    python3 tools/respawn_bed/build_respawn_bed.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/respawn_bed.glb`` (no clips) and ``mods/vanilla/textures/items/respawn_bed_icon.png``;
with ``--render`` the review views of each state and the sheet. Blender's Python module (bpy) runs it; nothing is
downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("respawn_bed", S.build_respawn_bed, half=(0.5, 1.0, 0.25), ortho=2.3, icon_target=(0, 0, -0.05),
          title="Respawn Bed (4 x 2 x 8 blocks)",
          lines=["Colony Works: the colony's wooden bunk, plank posts and rails with steel-blue corner brackets and a bunk tag,",
                 'bone ticking mattress, a steel-blue wool blanket with a mustard stripe and a turned-down sheet, one pillow.'])
