"""Review renders of the Gas Container (build_gas_container.py --render OUT_DIR).

Writes, into OUT_DIR: the container three-quarter as the reference shows it, from the far end (the filler cap), side,
top and end views with dimensions, the gauge at empty, two of five slots, four and full, frames of the pulse and supply
clips, and review_sheet.png with them over the icon. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.Z_BOT
    sc = bpy.context.scene
    target = (0, 0, -0.02)
    shots = {}

    def pose(fill, p=None):
        build.set_fill(rig, fill)
        build.apply_pose(rig, {**build.rest_pose(), **(p or {})})

    pose(0.6)
    cam = rl.camera("tq", (1.55, -2.6, 1.05), target, ortho=1.45)
    shots["three_quarter"] = rl.render(cam, out / "gas_container.png", 900, 900)
    cam = rl.camera("back", (-1.75, 2.4, 1.05), target, ortho=1.45)
    shots["back"] = rl.render(cam, out / "gas_container_back.png", 900, 900)
    views = {}
    for name, loc, w, h in (("side", (0, -3, -0.0), 900, 700), ("top", (0, -0.0001, 3), 900, 700), ("end", (3, 0, 0), 700, 700)):
        cam = rl.camera(name, loc, (0, 0, 0), ortho=1.25)
        views[name] = (rl.render(cam, out / f"gas_container_{name}.png", w, h), cam, w, h)
    gauge = rl.camera("gauge", (0.75, -1.9, 0.25), (build.GAUGE_X, -build.BY, -0.04), ortho=0.62)
    fills = []
    for fill in (0.0, 0.4, 0.8, 1.0):
        pose(fill)
        fills.append((fill, rl.render(gauge, out / f"gas_container_fill_{int(fill * 100):03d}.png", 500, 500)))
    clips = []
    for name, t, p in (("pulse", 0.15, build.clip_pulse()[1](0.15)), ("supply", 0.3, build.clip_supply()[1](0.3))):
        pose(0.6, p)
        clips.append((name, t, rl.render(gauge, out / f"gas_container_{name}_{t:.2f}.png", 500, 500)))
    socket_cam = rl.camera("sockets", (1.9, -1.2, 0.35), (0.45, 0, -0.04), ortho=0.75)
    pose(0.6, build.clip_supply()[1](0.3))
    clips.append(("supply sockets", 0.3, rl.render(socket_cam, out / "gas_container_supply_sockets_0.30.png", 500, 500)))
    pose(0.6)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2900, 2420), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "GAS CONTAINER (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "4 x 3 x 3 blocks (100 x 75 x 75 cm). Gunmetal body, copper fittings, teal gauge that fills with the five "
           "slots of gasoline. Handle and sockets stay inside the box.", fill=INK, font=F(24))
    for i, key in enumerate(("three_quarter", "back")):
        x = 60 + i * 940
        sheet.paste(Image.open(shots[key]).convert("RGB"), (x, 170))
        d.text((x + 16, 182), {"three_quarter": "THREE-QUARTER", "back": "FAR END (FILLER CAP)"}[key], fill=INK, font=F(30, True))
    icon = Image.open(build.ICONS / "gas_container_icon.png").convert("RGBA")
    tile = Image.new("RGB", (276, 276), (40, 42, 46))
    tile.paste(icon, (10, 10), icon)
    sheet.paste(tile, (2000, 170))
    d.text((2000, 460), "hotbar / inventory / Fabricator icon (256 px)", fill=INK, font=F(22))
    for i, (fill, p) in enumerate(fills):
        x, y = 2000 + (i % 2) * 440, 520 + (i // 2) * 470
        sheet.paste(Image.open(p).convert("RGB").resize((420, 420), Image.LANCZOS), (x, y))
        d.text((x + 10, y + 8), f"{int(fill * 5)} of 5 slots", fill=INK, font=F(24, True))
    y = 1100
    x = 60
    for name in ("side", "top", "end"):
        p, cam, w, h = views[name]
        scale = 0.72
        im = Image.open(p).convert("RGB").resize((int(w * scale), int(h * scale)), Image.LANCZOS)
        sheet.paste(im, (x, y))
        dd = ImageDraw.Draw(sheet)
        dd.text((x + 12, y + 10), name.upper(), fill=INK, font=F(24, True))
        if name == "side":
            a, b = rl.project(cam, (-0.5, -0.4, build.Z_BOT - 0.03), w, h), rl.project(cam, (0.5, -0.4, build.Z_BOT - 0.03), w, h)
            rl.dim_line(dd, (x + a[0] * scale, y + a[1] * scale), (x + b[0] * scale, y + b[1] * scale), "100 cm (4 blocks)", F(22))
            a, b = rl.project(cam, (0.53, -0.4, -0.375), w, h), rl.project(cam, (0.53, -0.4, 0.375), w, h)
            rl.dim_line(dd, (x + a[0] * scale, y + a[1] * scale), (x + b[0] * scale, y + b[1] * scale), "75 cm\n(3 blocks)", F(22), vertical=True)
        if name == "end":
            a, b = rl.project(cam, (0.6, -0.375, build.Z_BOT - 0.03), w, h), rl.project(cam, (0.6, 0.375, build.Z_BOT - 0.03), w, h)
            rl.dim_line(dd, (x + a[0] * scale, y + a[1] * scale), (x + b[0] * scale, y + b[1] * scale), "75 cm (3 blocks)", F(22))
        x += im.width + 30
    y = 1640
    d.text((60, y), "CLIPS: pulse (fuel went in) and supply (a linked engine drew fuel)", fill=INK, font=F(30, True))
    for i, (name, t, p) in enumerate(clips):
        x = 60 + i * 520
        sheet.paste(Image.open(p).convert("RGB").resize((500, 500), Image.LANCZOS), (x, y + 60))
        d.text((x + 10, y + 68), f"{name} at {t:.2f} s", fill=INK, font=F(24, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
