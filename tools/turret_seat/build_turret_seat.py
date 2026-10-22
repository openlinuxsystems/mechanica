"""Builds the Turret Seat at its five levels (parts map batch 17, row 143; tools/part_kit/turret_kit.py has the design).

    python3 tools/turret_seat/build_turret_seat.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/turret_seat_<level>.glb`` (with the seat clips ``enter``, ``seated_loop``,
``seated_rattle_loop`` and ``exit``, and the ``turretYaw`` and ``turretPitch`` nodes the game turns to the rider's aim)
and ``mods/vanilla/textures/items/turret_seat_<level>_icon.png``; with ``--render`` the review views and sheet.
Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402
import turret_kit as T  # noqa: E402

if __name__ == "__main__":
    K.run("turret_seat", T.build_turret_seat, half=T.HALF, ortho=1.9, icon_target=(0, 0, -0.05),
          title="Turret Seat (4 x 6 x 4 blocks)", bake=K.bake_seat_clips,
          lines=["A gunner's bucket seat on a low armoured turret ring: the upper race and yoke cheeks turn (yaw), the cradle",
                 "with the shell, footrest, spade grips and roll hoop tilts on the trunnions (pitch), both about the hips.",
                 "What is built on the hardpoint plate on top turns with it. Hips centred over the ring, cushion 0.28 m below centre."])
