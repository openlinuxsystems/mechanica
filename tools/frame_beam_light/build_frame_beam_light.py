"""Builds the Frame Beam Light (parts map batch 5; tools/part_kit/light_kit.py has the design).

    python3 tools/frame_beam_light/build_frame_beam_light.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/frame_beam_light.glb`` (with the ``on`` and ``off`` clips its ``pose`` readout plays) and
``mods/vanilla/textures/items/frame_beam_light_icon.png``; with ``--render`` the review views (off and on) and sheet. Blender's
Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import light_kit as L  # noqa: E402

if __name__ == "__main__":
    L.run_light("frame_beam_light", L.build_frame_beam_light, half=(0.5, 0.125, 0.125), ortho=1.1,
                title="Frame Beam Light (4 x 1 x 1 blocks, new)",
                lines=['Colony Works: a steel-blue channel beam with graphite end caps and a mustard tag; a bone light strip of',
                       'four frosted lenses in its underside. Hangs from its top and shines down (wide spot). Shown from below.'])
