"""Builds the Passenger Seat at its five levels (parts map batch 4; tools/part_kit/seat_kit.py has the design).

    python3 tools/passenger_seat/build_passenger_seat.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/passenger_seat_<level>.glb`` (with the seat clips ``enter``, ``seated_loop``,
``seated_rattle_loop`` and ``exit``) and ``mods/vanilla/textures/items/passenger_seat_<level>_icon.png``; with
``--render`` the review views and sheet. Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402
import seat_kit as S  # noqa: E402

if __name__ == "__main__":
    K.run("passenger_seat", lambda mats, level: S.build_seat(mats, level, driver=False), half=(0.5, 0.375, 0.5),
          ortho=1.4, icon_target=(0, 0, -0.07), title="Passenger Seat (4 x 4 x 3 blocks)", bake=K.bake_seat_clips,
          lines=["The Driver's Seat's bucket seat without the wheel: its logic outputs are the passenger's hotbar.",
                 "Hips 0.04 m behind centre, cushion 0.39 m above the plinth (the sit clip); the link box front left."])
