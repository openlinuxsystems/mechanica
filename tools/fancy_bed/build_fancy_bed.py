"""Builds the Fancy Bed (parts map batch 7; tools/part_kit/station_kit.py has the design).

    python3 tools/fancy_bed/build_fancy_bed.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/fancy_bed.glb`` (no clips) and ``mods/vanilla/textures/items/fancy_bed_icon.png``;
with ``--render`` the review views of each state and the sheet. Blender's Python module (bpy) runs it; nothing is
downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("fancy_bed", S.build_fancy_bed, half=(0.625, 1.0, 0.375), ortho=2.4, icon_target=(0, 0, -0.05),
          title="Fancy Bed (5 x 3 x 8 blocks)",
          lines=['Colony Works: a painted steel bed (it does not burn), graphite posts with chrome caps, steel-blue rails and boards',
                 'round bone panels with mustard pinstripes, a thick piped mattress, quilted duvet, two pillows and a mustard throw.'])
