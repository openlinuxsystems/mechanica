"""Builds the Refiner (parts map batch 7; tools/part_kit/station_kit.py has the design).

    python3 tools/refiner/build_refiner.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/refiner.glb`` (its ``idle_loop``, ``start``, ``work_loop`` and ``complete`` clips) and ``mods/vanilla/textures/items/refiner_icon.png``;
with ``--render`` the review views of each state and the sheet. Blender's Python module (bpy) runs it; nothing is
downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("refiner", S.build_refiner, half=(0.5, 0.5, 0.75), ortho=1.75, icon_target=(0, 0, 0),
          title="Refiner (4 x 6 x 4 blocks)",
          lines=['Salvager Tech: a copper-banded refining drum with a window on a framed case, a hopper and stirrer on top, an exhaust',
                 'stack, a screen, gauge and output hatch on the front, input port left and output port right. Refining: the window glows.'])
