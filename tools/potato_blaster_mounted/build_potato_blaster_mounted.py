"""Builds the Mounted Potato Blaster (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/potato_blaster_mounted/build_potato_blaster_mounted.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/potato_blaster_mounted.glb`` (with its ``fire`` clips) and
``mods/vanilla/textures/items/potato_blaster_mounted_icon.png``; with ``--render`` the review views of each state and the sheet.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("potato_blaster_mounted", W.build_potato_blaster_mounted, half=(0.25, 0.375, 0.25), ortho=0.85, icon_target=(0, 0, -0.05),
          title="Mounted Potato Blaster (2 x 2 x 3 blocks)",
          lines=["Salvager Tech: a breech block over a steel air tank on a turret foot, a ported steel barrel, the potato hopper the linked",
                 "Potato Ammo Containers feed (its hose and the logic jack at the back). The fire clip kicks the barrel and puffs at the muzzle."])
