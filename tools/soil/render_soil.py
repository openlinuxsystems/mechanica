"""Review renders of the Soil Bag and the soil mound (run through ``build_soil.py --render OUT_DIR``).

Renders the bag from the turnaround sheet's five views (front, short side, top, three-quarter, back) and the mound
from a three-quarter view, on a neutral backdrop under a studio light, and lays them out on one labelled sheet.
Cycles on the CPU; nothing is downloaded. The mound's four wetness looks are the game's (captured in Godot).
"""

from __future__ import annotations

import sys
from pathlib import Path

from mathutils import Vector
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rocks"))
import render_rocks as rr  # noqa: E402


def render_all(outputs, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rr.setup_world()
    bag, mound = outputs["soil_bag"][0], outputs["soil"][0]
    shots = []
    views = [("Front", (0, -1, 0.08), 1.5), ("Side (short)", (1, 0, 0.08), 1.5), ("Top", (0, -0.001, 1), 1.5),
             ("Three-quarter", (1.0, -1.25, 0.95), 1.6), ("Back", (0, 1, 0.08), 1.5)]
    mound.hide_render = True
    ground.location.z = -0.125
    for label, d, scale in views:
        cam = rr.camera("Bag" + label, Vector(d).normalized() * 8, (0, 0, 0), ortho=scale)
        shots.append((f"Soil Bag - {label}", rr.render(cam, out / f"bag_{len(shots)}.png", 640, 480)))
    bag.hide_render, mound.hide_render = True, False
    cam = rr.camera("Mound", Vector((1.0, -1.25, 0.95)).normalized() * 8, (0, 0, -0.07), ortho=1.1)
    shots.append(("Soil mound (dry) - three-quarter", rr.render(cam, out / "mound.png", 640, 480)))
    cols, tw, th, gap = 3, 640, 480, 24
    sheet = Image.new("RGB", (cols * tw + (cols + 1) * gap, 110 + 2 * (th + 60)), (238, 238, 236))
    dr = ImageDraw.Draw(sheet)
    dr.text((gap, 24), "MECHANICA SOIL BAG AND SOIL MOUND", fill=rr.INK, font=rr.font(36, True))
    bi, mi = build.glb_info(outputs["soil_bag"][1]), build.glb_info(outputs["soil"][1])
    dr.text((gap, 70), f"Bag {' x '.join(f'{v:.3f}' for v in bi['size'])} m, {bi['tris']} tris, {bi['kib']:.0f} KiB.   "
            f"Mound {' x '.join(f'{v:.3f}' for v in mi['size'])} m, {mi['tris']} tris, {mi['kib']:.0f} KiB.",
            fill=rr.INK, font=rr.font(20))
    for i, (label, path) in enumerate(shots):
        x, y = gap + (i % cols) * (tw + gap), 110 + (i // cols) * (th + 60)
        sheet.paste(Image.open(path).convert("RGB"), (x, y))
        dr.text((x, y + th + 10), label, fill=rr.INK, font=rr.font(22, True))
    sheet.save(out / "soil_review_sheet.png")
    print(f"wrote {out / 'soil_review_sheet.png'}")
