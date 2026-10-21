"""Builds the Warning Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/warning_light/build_warning_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/warning_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/warning_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("warning_light", L.build_warning_light, half=(0.125, 0.125, 0.25), ortho=0.6,
                title="Warning Light (1 x 2 x 1 blocks)",
                lines=['Salvager Tech: a signal-red dome in a gunmetal cage on a hull drum with copper bands.',
                       'Blinks red (no longer orange): the on and off clips follow each blink.'])
