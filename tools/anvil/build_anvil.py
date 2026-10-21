"""Builds the Anvil (parts map batch 7; tools/part_kit/station_kit.py has the design).

    python3 tools/anvil/build_anvil.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/anvil.glb`` (no clips) and ``mods/vanilla/textures/items/anvil_icon.png``;
with ``--render`` the review views of each state and the sheet. Blender's Python module (bpy) runs it; nothing is
downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

if __name__ == "__main__":
    S.run("anvil", S.build_anvil, half=(0.375, 0.375, 0.375), ortho=1.05, icon_target=(0, 0, 0),
          title="Anvil (3 x 3 x 3 blocks)",
          lines=["Settler Patchwork: the camp's anvil_stump as a part, a graphite anvil with a steel face and horn stapled onto",
                 'a strapped hardwood stump, a hammer resting on it. Heavy and grippy (friction 0.8).'])
