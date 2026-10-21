"""Builds the Small and Large Explosive Canisters (parts map batch 6; tools/part_kit/working_kit.py has the design).

    python3 tools/explosive_canister/build_explosive_canister.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/explosive_canister_<small|large>.glb`` (with the ``fuse_loop`` clip) and
``mods/vanilla/textures/items/explosive_canister_<small|large>_icon.png``; with ``--render`` the review views and sheets.
Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import working_kit as W  # noqa: E402

if __name__ == "__main__":
    W.run("explosive_canister_small", lambda m: W.build_canister(m, large=False), half=(0.125, 0.125, 0.25), ortho=0.6, icon_target=(0, 0, 0.0),
          title="Small Explosive Canister (1 x 2 x 1 blocks)",
          lines=["Colony Works signal red with a yellow-black hazard band, steel rolled rims, a gunmetal fuse cap and a caged red fuse lamp,",
                 "a mustard warning plate. Fuse lit: the red lamp blinks (fuse_loop)."])
    W.run("explosive_canister_large", lambda m: W.build_canister(m, large=True), half=(0.25, 0.25, 0.375), ortho=0.95, icon_target=(0, 0, 0.0),
          title="Large Explosive Canister (2 x 3 x 2 blocks)",
          lines=["The Small Canister's drum at twice the size: two hazard bands, ribbed rims, carry handles on top. Fuse lit: the red lamp",
                 "blinks (fuse_loop)."])
