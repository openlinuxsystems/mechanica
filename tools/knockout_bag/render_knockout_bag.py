"""Review renders of the knockout bag (run through ``build_knockout_bag.py --render OUT_DIR``).

Renders the bag from the reference sheet's six views (front, right side, left side, back, top, three-quarter) on a
neutral backdrop under a studio light and lays them out on one labelled sheet like the reference. Cycles on the CPU;
nothing is downloaded.
"""

from __future__ import annotations

import sys
from pathlib import Path

from mathutils import Vector
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rocks"))
import render_rocks as rr  # noqa: E402

VIEWS = [("front view", (0, -1, 0.06)), ("right side", (1, 0, 0.06)), ("left side", (-1, 0, 0.06)),
         ("back view", (0, 1, 0.06)), ("top view", (0, -0.25, 1)), ("3/4 perspective view", (0.9, -1.2, 0.55))]


def render_all(outputs, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    rr.setup_world()
    bag, path = outputs["knockout_bag"]
    centre = (0, 0, 0.215)
    shots = []
    for i, (label, d) in enumerate(VIEWS):
        cam = rr.camera(f"Bag{i}", Vector(centre) + Vector(d).normalized() * 6, centre, ortho=0.62)
        shots.append((label, rr.render(cam, out / f"knockout_bag_{i}.png", 600, 600)))
    cols, tw, th, gap = 3, 600, 600, 24
    sheet = Image.new("RGB", (cols * tw + (cols + 1) * gap, 110 + 2 * (th + 60)), (238, 238, 236))
    dr = ImageDraw.Draw(sheet)
    dr.text((gap, 24), "MECHANICA KNOCKOUT BAG", fill=rr.INK, font=rr.font(36, True))
    i = build.glb_info(path)
    dr.text((gap, 70), f"{' x '.join(f'{v:.3f}' for v in i['size'])} m, {i['tris']} tris, {i['materials']} materials, "
            f"{i['kib']:.0f} KiB.", fill=rr.INK, font=rr.font(20))
    for k, (label, p) in enumerate(shots):
        x, y = gap + (k % cols) * (tw + gap), 110 + (k // cols) * (th + 60)
        sheet.paste(Image.open(p).convert("RGB"), (x, y))
        dr.text((x + tw // 2 - 12 * len(label) // 2, y + th + 12), label, fill=rr.INK, font=rr.font(24))
    sheet.save(out / "knockout_bag_review_sheet.png")
    print(f"wrote {out / 'knockout_bag_review_sheet.png'}")
