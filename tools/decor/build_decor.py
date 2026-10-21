"""Builds the furniture and decor parts of the parts map's batches 13a-15b and their icons.

    pip install bpy==5.2.2 numpy pillow
    python tools/decor/build_decor.py                    # every batch: writes the .glb files and the icons
    python tools/decor/build_decor.py --batch 13a        # one batch
    python tools/decor/build_decor.py --only mug,calendar --render OUT_DIR   # these, with review sheets

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png``. The models are in
office.py (13a, 13b); tools/part_kit/decor_kit.py has the shared materials, shapes and runner. The outputs are
committed; re-run this after changing a model and commit the result. The build is deterministic (seeded noise only).
"""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE))
import decor_kit as D  # noqa: E402  (first: it imports bpy)
import office  # noqa: E402

BATCHES = {
    "13a": (office.BATCH_13A, office.materials_13),
    "13b": (office.BATCH_13B, office.materials_13),
}

if __name__ == "__main__":
    picked = sys.argv[sys.argv.index("--batch") + 1].split(",") if "--batch" in sys.argv else list(BATCHES)
    registry, extras = {}, []
    for b in picked:
        parts, extra = BATCHES[b]
        registry.update(parts)
        if extra not in extras:
            extras.append(extra)
    D.run(registry, title=f"Decor batches {', '.join(picked)}", extra_materials=extras)
