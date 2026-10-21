"""Builds the Driver's Saddle at its five levels (parts map batch 4; tools/part_kit/seat_kit.py has the design).

    python3 tools/drivers_saddle/build_drivers_saddle.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/drivers_saddle_<level>.glb`` (with the seat clips ``enter``, ``seated_loop``,
``seated_rattle_loop`` and ``exit``) and ``mods/vanilla/textures/items/drivers_saddle_<level>_icon.png``; with
``--render`` the review views and sheet. Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402
import seat_kit as S  # noqa: E402

if __name__ == "__main__":
    K.run("drivers_saddle", lambda mats, level: S.build_saddle(mats, level, driver=True), half=(0.375, 0.5, 0.5),
          ortho=1.4, icon_target=(0, 0, -0.07), title="Driver's Saddle (3 x 4 x 4 blocks)", bake=K.bake_seat_clips,
          lines=["Ridden astride (the sit_saddle clip): a saddle on a narrow body, footboards either side, handlebars.",
                 "Feet on the footboards 0.05 m above the bottom, hips 0.12 m behind centre; grips where the clip's hands are."])
