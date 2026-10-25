"""Builds the parts audit's batch D5 models and icons: the Workshop Sign (``outpost_sign``), the Warehouse Key Sign,
the Speed Limit Sign and the Barrier Tape (the models are in site_signs.py, on the batch 12a sign kit).

    pip install bpy==5.2.2 numpy pillow
    python tools/decor/build_site_signs.py [--only id,id] [--review OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png``. With ``--review``
it also writes a 1600 px review sheet per part into OUT_DIR (three-quarter front and back views beside a 0.25 m
cube, the icon on dark and on light) and ``site_signs_lineup.jpg``. The outputs are committed; re-run this after
changing a model and commit the result. The build is deterministic (seeded noise only).
"""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE.parent / "parts_building"))
sys.path.insert(0, str(HERE))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import building_kit as B  # noqa: E402
import site_signs  # noqa: E402

if __name__ == "__main__":
    picked = B.selected(sys.argv, site_signs.PARTS)
    out = Path(sys.argv[sys.argv.index("--review") + 1]) if "--review" in sys.argv else None
    if out is not None:
        import review_site_signs as R
    for part, build, half, ortho, title, lines in picked:
        B.run(part, build, half=half, ortho=ortho, title=title, lines=lines, faces_builder=True)
        if out is not None:
            R.sheet(part, title, lines, half, out)
    if out is not None and len(picked) > 1:
        R.lineup(picked, out)
