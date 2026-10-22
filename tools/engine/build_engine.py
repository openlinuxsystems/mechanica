"""Builds the models of the parts map's engine batches (batch 17a: the climbable Ladder, E2) with the Colony Works kit
in tools/part_kit/structural_kit.py (read only here).

    python3 tools/engine/build_engine.py 17a [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png`` for every part of the
batch; with ``--render`` each part's review sheet and a batch overview (``batch_<batch>.png``). Blender's Python
module runs it (``pip install bpy==5.2.2 numpy pillow``); nothing is downloaded. The Chemical Bucket (E11) is the water
bucket's model with green chemical in it: ``tools/characters/build_characters.py items`` builds it and
``tools/materials/build_materials.py`` draws its icon.
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
import structural_kit as S  # noqa: E402  (first: it imports bpy)

Geo, box, cylinder = S.Geo, S.box, S.cylinder
E = S.EPS

# The Ladder (2 x 8 x 1 blocks: 0.5 m wide, 2 m tall, 0.25 m deep). Its rungs are every 0.25 m, as the climb clip's
# hands and feet expect (tools/characters/build_characters.py, CLIMB_*), and sit in the middle of its depth so it
# climbs the same from either face.
LADDER_HALF = (0.25, 0.125, 1.0)      # Blender x (across), y (depth), z (up)
RAIL_X = 0.215                        # the rails' centres
RAIL = (0.022, 0.05)                  # rail half width (x) and half depth (y): a steel channel
RUNG_R = 0.017                        # the round rungs
RUNGS = [-0.75 + 0.25 * k for k in range(7)]


def ladder():
    g = Geo()
    hx, hy, hz = LADDER_HALF
    top, bottom = hz - E, -hz + E
    for s in (-1, 1):
        x = s * RAIL_X
        # the rail: a steel-blue channel, its open side facing out, with a darker web line down the inside
        box(g, (x, 0, 0.012), (RAIL[0], RAIL[1], hz - 0.03), "blue", bevel=0.004)
        box(g, (x + s * (RAIL[0] - 0.003), 0, 0.012), (0.004, RAIL[1] - 0.008, hz - 0.045), "blue_dark", bevel=0.001)
        # mustard paint on the top 15 cm, where hands reach over the top
        box(g, (x, 0, top - 0.03 - 0.075), (RAIL[0] + 0.0015, RAIL[1] + 0.0015, 0.075), "mustard", bevel=0.004)
        # a zinc end cap on top and a rubber foot under it
        box(g, (x, 0, top - 0.009), (RAIL[0] + 0.004, RAIL[1] + 0.004, 0.009), "zinc", bevel=0.003)
        box(g, (x, 0, bottom + 0.015), (RAIL[0] + 0.006, min(RAIL[1] + 0.02, hy - E), 0.015), "rubber", bevel=0.005)
        # brackets at the top and bottom, reaching back and front to the footprint, to bolt it to a wall or a deck
        for z in (bottom + 0.16, top - 0.28):
            box(g, (x, 0, z), (0.018, hy - E, 0.022), "graphite", bevel=0.003)
            for y in (-1, 1):
                S.bolt(g, (x + s * 0.018, y * (hy - 0.035), z), (s, 0, 0), 0.0065)
        # each rung bolted through the rail
        for z in RUNGS:
            S.bolt(g, (x + s * RAIL[0], 0, z), (s, 0, 0), 0.0075)
    for z in RUNGS:
        cylinder(g, (-RAIL_X + RAIL[0], 0, z), (RAIL_X - RAIL[0], 0, z), RUNG_R, "zinc", segs=12)
        # anti-slip grip: three raised ribbed bands on the step
        for u in (-0.09, 0.0, 0.09):
            cylinder(g, (u - 0.03, 0, z), (u + 0.03, 0, z), RUNG_R + 0.0012, "zinc_dark", segs=12)
    return g


def parts_17a():
    return [
        S.Part("ladder", [2, 8, 1], ladder, "Ladder (2x8x1)", [
            "Colony Works steel ladder, 0.5 m by 2 m: steel-blue channel rails, mustard tops,",
            "seven zinc rungs every 0.25 m with ribbed grips, wall brackets, rubber feet.",
            "Climbable (part data `climbable`): walk into it, climb with the move keys.",
        ]),
    ]


BATCHES = {"17a": ("Batch 17a: the Ladder", parts_17a)}


def main():
    args = list(sys.argv[1:])
    out = Path(args[args.index("--render") + 1]) if "--render" in args else None
    names = [a for a in args if a in BATCHES]
    if not names:
        raise SystemExit(f"name a batch: {', '.join(BATCHES)}")
    for name in names:
        title, parts = BATCHES[name]
        S.run_parts(parts(), out, sheet_name=f"batch_{name}", sheet_title=title)


if __name__ == "__main__":
    main()
