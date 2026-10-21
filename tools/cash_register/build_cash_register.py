"""Builds the Cash Register (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/cash_register/build_cash_register.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/cash_register.glb`` (with its ``open`` and ``close`` clips) and
``mods/vanilla/textures/items/cash_register_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("cash_register", W.build_cash_register, half=(0.375, 0.25, 0.25), ortho=1.0, icon_target=(0, 0, -0.03),
          title="Cash Register (3 x 2 x 2 blocks)",
          lines=["Settler Patchwork: a bone-enamel till with three rows of brass keys and a sale-flag tower on a scrap-wood drawer cabinet,",
                 "zinc trim and a crank. Open: the zinc drawer with its wooden coin trays springs out and the crank turns."])
