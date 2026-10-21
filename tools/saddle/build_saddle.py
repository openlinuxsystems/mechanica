"""Builds the Saddle at its five levels (parts map batch 4; tools/part_kit/seat_kit.py has the design).

    python3 tools/saddle/build_saddle.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/saddle_<level>.glb`` (with the seat clips ``enter``, ``seated_loop``,
``seated_rattle_loop`` and ``exit``) and ``mods/vanilla/textures/items/saddle_<level>_icon.png``; with ``--render``
the review views and sheet. Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402
import seat_kit as S  # noqa: E402

if __name__ == "__main__":
    K.run("saddle", lambda mats, level: S.build_saddle(mats, level, driver=False), half=(0.375, 0.375, 0.375),
          ortho=1.1, icon_target=(0, 0, -0.06), title="Saddle (3 x 3 x 3 blocks)", bake=K.bake_seat_clips,
          lines=["The passenger's saddle (the sit_saddle_ride clip, hands free): a grab rail behind, footboards either side.",
                 "Feet on the footboards 0.03 m above the bottom, hips 0.03 m behind centre; its logic outputs are the rider's hotbar."])
