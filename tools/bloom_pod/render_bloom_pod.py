"""Review renders of a Bloom Pod (build_bloom_pod.py --render OUT_DIR).

Writes, into OUT_DIR, the pod of one rarity shut (three-quarter and front), half settled, hit and broken, and
review_<rarity>.png laying them out in a row. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lift"))
import render_lift as rl  # noqa: E402


def render_all(rig, out: Path, rarity, build):
    out.mkdir(parents=True, exist_ok=True)
    for ob in build.moving(rig):                    # the baked clips would override the poses set below
        if ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data.use_nla = False
    rl.setup_world()
    target = (0, 0, 0.45)
    shots = []
    for name, pose, t, loc in (("shut", build.pose_idle, 0.0, (2.0, -2.6, 1.85)),
                               ("front", build.pose_idle, 0.0, (0.0, -3.2, 0.62)),
                               ("settling", build.pose_settle, 0.5, (2.3, -3.0, 2.1)),
                               ("hit", build.pose_hit, 0.07, (2.0, -2.6, 1.85)),
                               ("broken", build.pose_break, build.BREAK, (2.6, -3.4, 2.4))):
        pose(rig, t)
        bpy.context.view_layer.update()
        cam = rl.camera(f"{rarity}_{name}", loc, target, lens=55 if name in ("settling", "broken") else 68)
        shots.append((name, rl.render(cam, out / f"bloom_pod_{rarity}_{name}.png", 600, 600)))
    build.rest(rig)
    img = Image.new("RGB", (600 * len(shots), 650), (255, 255, 255))
    d = ImageDraw.Draw(img)
    for i, (name, path) in enumerate(shots):
        img.paste(Image.open(path).convert("RGB"), (600 * i, 50))
        d.text((600 * i + 16, 12), f"{rarity.upper()}: {name.upper()}", fill=rl.INK, font=rl.font(26, bold=True))
    img.save(out / f"review_{rarity}.png")
