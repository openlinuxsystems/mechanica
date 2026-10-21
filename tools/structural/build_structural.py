"""Builds the parts map's structural batches (8a and 8b beams and frames, 9a and 9b pipes, 10a and 10b ducts,
conduits and cables): Colony Works models in tools/part_kit/structural_kit.py, parts in beams.py, pipes.py and
ducts.py.

    python3 tools/structural/build_structural.py BATCH [BATCH ...] [--only ID,ID] [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png`` for every part of
each batch; with ``--render`` each part's review sheet and a batch overview (``<batch>.png``). Blender's Python
module runs it (``pip install bpy==5.2.2 numpy pillow``); nothing is downloaded.
"""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE))
import structural_kit as S  # noqa: E402  (first: it imports bpy)
import beams  # noqa: E402

BATCHES = {
    "8a": ("Batch 8a: beams, frames and supports", beams.parts_8a),
    "8b": ("Batch 8b: beams, frames and supports", beams.parts_8b),
}
try:
    import pipes  # noqa: E402
    BATCHES.update({"9a": ("Batch 9a: pipes and valves", pipes.parts_9a), "9b": ("Batch 9b: pipes", pipes.parts_9b)})
except ImportError:
    pass
try:
    import ducts  # noqa: E402
    BATCHES.update({"10a": ("Batch 10a: ducts and tubes", ducts.parts_10a),
                    "10b": ("Batch 10b: conduits and cables", ducts.parts_10b)})
except ImportError:
    pass


def main():
    args = [a for a in sys.argv[1:]]
    out = Path(args[args.index("--render") + 1]) if "--render" in args else None
    only = set(args[args.index("--only") + 1].split(",")) if "--only" in args else None
    names = [a for a in args if a in BATCHES]
    if not names:
        raise SystemExit(f"name a batch: {', '.join(BATCHES)}")
    for name in names:
        title, parts = BATCHES[name]
        parts = [p for p in parts() if only is None or p.id in only]
        S.run_parts(parts, out, sheet_name=f"batch_{name}", sheet_title=title)


if __name__ == "__main__":
    main()
