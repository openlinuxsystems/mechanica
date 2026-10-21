"""Builds the Sensor at its five levels (parts map batch 4; tools/part_kit/logic_kit.py has the design).

    python3 tools/sensor/build_sensor.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/sensor_<level>.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout
plays) and ``mods/vanilla/textures/items/sensor_<level>_icon.png``; with ``--render`` the review views and sheet.
Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import logic_kit as L  # noqa: E402

if __name__ == "__main__":
    K.run("sensor", L.build_sensor, half=(0.125, 0.125, 0.125), ortho=0.35, icon_target=(0, 0, -0.05),
          title="Sensor (1 block)", bake=L.bake,
          lines=["On the Switch's base plate, the lens on its top (the part's +y face, the way it looks); the shroud grows",
                 "with the range (1 to 5 m). The ring round the lens lights teal while it sees something (pose on and off)."])
