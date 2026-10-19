"""Review renders of the Gas Tank and Oil Canister (build_fuel_oil.py --render OUT_DIR).

Writes, into OUT_DIR: each item three-quarter from the front right and from the back left, front, side and top views
with dimensions, the two side by side on the ground with a 0.25 m block for scale, close-ups of the level windows,
and review_sheet.png with them and the icons on a hotbar and inventory mock. Cycles on the CPU; LIFT_SAMPLES sets the
samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(tank, drum, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    target = (0, 0, -0.02)
    items = {"gasoline": tank, "crude_oil": drum}
    shots = {}

    def solo(name):
        for k, rig in items.items():
            rig["root"].location = (0, 0, 0) if k == name else (40, 0, 0)

    for name in items:
        solo(name)
        for view, loc in (("three_quarter", (1.45, -1.95, 1.2)), ("back", (-1.6, 1.8, 1.1))):
            cam = rl.camera(f"{name}_{view}", loc, target, ortho=0.82)
            shots[(name, view)] = rl.render(cam, out / f"{name}_{view}.png", 800, 800)
        for view, loc in (("front", (0, -3, 0)), ("side", (3, 0, 0)), ("top", (0, -0.0001, 3))):
            cam = rl.camera(f"{name}_{view}", loc, (0, 0, 0), ortho=0.72)
            shots[(name, view)] = (rl.render(cam, out / f"{name}_{view}.png", 600, 600), cam)
    # close-ups of the windows
    solo("gasoline")
    cam = rl.camera("tank_strip", (1.4, -0.55, 0.15), (build.TANK_HALF[0], build.STRIP_Y, build.TANK_Z), ortho=0.42)
    shots[("gasoline", "window")] = rl.render(cam, out / "gasoline_level.png", 500, 500)
    solo("crude_oil")
    cam = rl.camera("drum_window", (1.55, -0.35, 0.3), (0.2, 0.06, -0.04), ortho=0.42)
    shots[("crude_oil", "window")] = rl.render(cam, out / "crude_oil_window.png", 500, 500)
    # the pair side by side with a block for scale
    tank["root"].location = (-0.33, 0, 0)
    drum["root"].location = (0.33, 0, 0)
    import bmesh  # noqa: PLC0415
    me = bpy.data.meshes.new("ScaleBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    blk = bpy.data.objects.new("RenderOnlyBlock", me)
    blk.location = (0.0, -0.45, build.FLOOR + 0.125)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.42, 0.28, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(blk)
    cam = rl.camera("pair", (1.3, -2.7, 1.25), (0, -0.1, -0.05), ortho=1.45)
    shots["pair"] = rl.render(cam, out / "pair.png", 1000, 760)
    blk.hide_render = True

    F, INK = rl.font, rl.INK
    W = 2900
    sheet = Image.new("RGB", (W, 3800), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "GAS TANK (Gasoline) and OIL CANISTER (Crude Oil): design review", fill=INK, font=F(46, True))
    d.text((60, 98), "Both are 2 x 2 x 2 block items (50 x 50 x 50 cm), the collision box. Gunmetal, copper hardware and teal "
           "indicators like the Gas Container; red gas-can body on the Gas Tank, red cap on the Oil Canister (as the samples).",
           fill=INK, font=F(24))
    sheet.paste(Image.open(shots["pair"]).convert("RGB"), (60, 160))
    d.text((76, 172), "SIDE BY SIDE (tan cube: one 25 cm block)", fill=INK, font=F(26, True))

    # icon mocks: a hotbar strip and an inventory grid
    icons = {k: Image.open(build.ICONS / f"{k}_icon.png").convert("RGBA") for k in items}
    x0, y0 = 1120, 170
    d.text((x0, y0), "HOTBAR", fill=INK, font=F(28, True))
    bar = Image.new("RGB", (10 * 118 + 12, 130), (24, 26, 29))
    bd = ImageDraw.Draw(bar)
    for i in range(10):
        bx = 6 + i * 118
        bd.rounded_rectangle([bx, 6, bx + 112, 118], radius=8, fill=(44, 47, 52), outline=(43, 138, 146) if i == 3 else (70, 74, 80),
                             width=4 if i == 3 else 2)
        if i in (3, 4):
            ic = icons["gasoline" if i == 3 else "crude_oil"].resize((100, 100), Image.LANCZOS)
            bar.paste(ic, (bx + 6, 12), ic)
            bd.text((bx + 80, 90), "20" if i == 3 else "12", fill=(230, 232, 234), font=F(20, True))
    sheet.paste(bar, (x0, y0 + 44))
    y1 = y0 + 210
    d.text((x0, y1), "INVENTORY", fill=INK, font=F(28, True))
    inv = Image.new("RGB", (6 * 150 + 12, 2 * 150 + 12), (24, 26, 29))
    idr = ImageDraw.Draw(inv)
    for r in range(2):
        for c in range(6):
            bx, by = 6 + c * 150, 6 + r * 150
            idr.rounded_rectangle([bx, by, bx + 144, by + 144], radius=8, fill=(44, 47, 52), outline=(70, 74, 80), width=2)
            k = {(0, 0): "gasoline", (0, 1): "crude_oil", (1, 2): "gasoline"}.get((r, c))
            if k:
                ic = icons[k].resize((132, 132), Image.LANCZOS)
                inv.paste(ic, (bx + 6, by + 4), ic)
                idr.text((bx + 104, by + 114), {(0, 0): "20", (0, 1): "20", (1, 2): "7"}[(r, c)], fill=(230, 232, 234), font=F(22, True))
    sheet.paste(inv, (x0, y1 + 44))
    y2 = y1 + 44 + inv.height + 30
    d.text((x0, y2), "ICONS (256 px, clear background)", fill=INK, font=F(28, True))
    for i, k in enumerate(items):
        tile = Image.new("RGB", (276, 276), (40, 42, 46))
        tile.paste(icons[k], (10, 10), icons[k])
        sheet.paste(tile, (x0 + i * 300, y2 + 44))
        light = Image.new("RGB", (276, 276), (226, 228, 230))
        light.paste(icons[k], (10, 10), icons[k])
        sheet.paste(light, (x0 + 620 + i * 300, y2 + 44))

    y = y2 + 44 + 276 + 60
    for name, label in (("gasoline", "GAS TANK (Gasoline)"), ("crude_oil", "OIL CANISTER (Crude Oil)")):
        d.text((60, y), label, fill=INK, font=F(38, True))
        x = 60
        for view in ("three_quarter", "back"):
            sheet.paste(Image.open(shots[(name, view)]).convert("RGB").resize((640, 640), Image.LANCZOS), (x, y + 56))
            d.text((x + 12, y + 66), {"three_quarter": "THREE-QUARTER", "back": "BACK"}[view], fill=INK, font=F(24, True))
            x += 660
        sheet.paste(Image.open(shots[(name, "window")]).convert("RGB").resize((420, 420), Image.LANCZOS), (x, y + 56))
        d.text((x + 10, y + 66), "LEVEL WINDOW", fill=INK, font=F(22, True))
        x2, yv = x + 440, y + 56
        for i, view in enumerate(("front", "side", "top")):
            p, cam = shots[(name, view)]
            sc = 0.5
            im = Image.open(p).convert("RGB").resize((300, 300), Image.LANCZOS)
            px, py = x2 + (i % 2) * 320, yv + (i // 2) * 320
            sheet.paste(im, (px, py))
            dd = ImageDraw.Draw(sheet)
            dd.text((px + 8, py + 6), view.upper(), fill=INK, font=F(20, True))
            if view == "front":
                a = rl.project(cam, (-0.25, -0.3, build.FLOOR - 0.02), 600, 600)
                b = rl.project(cam, (0.25, -0.3, build.FLOOR - 0.02), 600, 600)
                rl.dim_line(dd, (px + a[0] * sc, py + a[1] * sc), (px + b[0] * sc, py + b[1] * sc), "50 cm", F(18))
            if view == "side":
                a = rl.project(cam, (0.3, 0.27, -0.25), 600, 600)
                b = rl.project(cam, (0.3, 0.27, 0.25), 600, 600)
                rl.dim_line(dd, (px + a[0] * sc, py + a[1] * sc), (px + b[0] * sc, py + b[1] * sc), "50 cm", F(18), vertical=True)
        y += 56 + 660 + 60
    notes = [
        "Gas Tank: a classic red jerrycan with a welded seam flange, X pressings on both faces, a three-loop handle, a tilted",
        "spout with a cam-lock cap at one end, a small flame plate on the front and a teal level strip on the +X end (drops as fuel is used).",
        "Oil Canister: squat oily drum (rolled rims, two copper hoops, drips), red screw cap with a turn arrow on a copper collar,",
        "lifting lugs, a window onto the dark oil on the right side and a teal droplet + CRUDE OIL stencil.",
    ]
    for i, t in enumerate(notes):
        d.text((60, y + i * 36), t, fill=INK, font=F(26))
    sheet = sheet.crop((0, 0, W, y + len(notes) * 36 + 50))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
