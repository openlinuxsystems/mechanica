"""Builds the Alarm Clock (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/alarm_clock/build_alarm_clock.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/alarm_clock.glb`` (with its ``off``, ``armed`` and ``ring_loop`` clips) and
``mods/vanilla/textures/items/alarm_clock_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("alarm_clock", W.build_alarm_clock, half=(0.25, 0.125, 0.25), ortho=0.75, icon_target=(0, 0, 0.0),
          title="Alarm Clock (2 x 2 x 1 blocks)",
          lines=["Salvager Tech: a round dial case on two splayed feet, twin copper bells and a hammer on top, winding key, knob and logic jack",
                 "behind. Armed: the cyan pip lights. Ringing: the hammer drums the bells and the case rattles (ring_loop)."])
