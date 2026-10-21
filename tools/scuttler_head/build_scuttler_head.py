"""Builds the Scuttler Head (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/scuttler_head/build_scuttler_head.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/scuttler_head.glb`` (with its ``note`` clips) and
``mods/vanilla/textures/items/scuttler_head_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("scuttler_head", W.build_scuttler_head, half=(0.25, 0.25, 0.25), ortho=0.75, icon_target=(0, 0, 0.0),
          title="Scuttler Head (2 x 2 x 2 blocks)",
          lines=["The Green Scuttler's eye head (tools/scuttler/build_scuttler.py) in its battle paint, on the stump of its stalk with cut",
                 "cables on a salvaged mount. Its green lens flashes and the head bobs with every note (the note clip)."])
