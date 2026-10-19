"""Review renders of the Portable Fabricator (build_portable_fabricator.py --render OUT_DIR).

Writes, into OUT_DIR: the reference sheet's front, side, top and three-quarter views at rest, the three-quarter view
while crafting, the back, frames of the idle, start, craft and complete clips, and review_sheet.png laying them out over
the icon. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl

W = 800


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.BOTTOM
    target = (0, 0, -0.03)
    views = {
        "front": ((0, 4, -0.03), 1.3),
        "side": ((-4, 0, -0.03), 1.3),
        "top": ((0, -0.001, 4), 1.3),
        "three_quarter": ((-1.6, 2.1, 1.3), 1.55),
        "back": ((1.6, -2.1, 1.1), 1.55),
    }
    shots = {}
    build.apply_pose(rig, build.pose_at("idle_loop", 1.5))
    for name, (loc, ortho) in views.items():
        cam = rl.camera(name, loc, target, ortho=ortho)
        shots[name] = rl.render(cam, out / f"portable_fabricator_{name}.png", W, W)
    cam = rl.camera("work", (-1.6, 2.1, 1.3), target, ortho=1.55)
    build.apply_pose(rig, build.pose_at("craft_loop", 0.42))
    shots["crafting"] = rl.render(cam, out / "portable_fabricator_crafting.png", W, W)
    close = rl.camera("close", (-0.9, 1.25, 0.95), (-0.05, -0.05, 0.12), ortho=0.8)
    frames = []
    for clip, t in (("idle_loop", 1.5), ("start", 0.12), ("start", 0.45), ("craft_loop", 0.2), ("craft_loop", 0.62),
                    ("craft_loop", 1.3), ("complete", 0.04), ("complete", 0.7)):
        build.apply_pose(rig, build.pose_at(clip, t))
        p = rl.render(close, out / f"portable_fabricator_{clip}_{t:.2f}.png", 600, 600)
        frames.append((f"{clip} {t:.2f} s", p))
    build.apply_pose(rig, build.rest_pose())

    f = rl.font(30, bold=True)
    small = rl.font(22)
    sheet = Image.new("RGB", (W * 3, W * 2 + 600 + 120), (255, 255, 255))
    d = ImageDraw.Draw(sheet)
    d.text((24, 20), "PORTABLE FABRICATOR  ·  4 x 3 x 3 blocks (1.00 x 0.75 x 0.75 m)", fill=rl.INK, font=f)
    order = ["front", "side", "top", "three_quarter", "crafting", "back"]
    for i, name in enumerate(order):
        x, y = (i % 3) * W, 80 + (i // 3) * W
        sheet.paste(Image.open(shots[name]).convert("RGB"), (x, y))
        d.text((x + 16, y + 12), name.replace("_", "-").upper(), fill=rl.INK, font=small)
    y0 = 80 + 2 * W
    for i, (label, p) in enumerate(frames):
        im = Image.open(p).convert("RGB").resize((280, 280))
        x = 10 + i * 290
        sheet.paste(im, (x, y0 + 30))
        d.text((x + 6, y0 + 4), label, fill=rl.INK, font=small)
    icon = Image.open(build.ICON).convert("RGBA")
    sheet.paste(icon, (W * 3 - 280, y0 + 330), icon)
    d.text((W * 3 - 280, y0 + 300), "ICON", fill=rl.INK, font=small)
    sheet.save(out / "review_sheet.png")
