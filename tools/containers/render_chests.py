"""Review renders of the five chests (build_chests.py --render OUT_DIR).

Writes, into OUT_DIR: for each chest its three-quarter view (front right), back view (back left), front and top views
(orthographic) and <id>_review.png, a sheet of them with the icon on dark and light tiles; then lineup.png, all five
standing in a row beside a 25 cm block, tier order left to right. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

import render_lift as rl


def show_only(built, keep):
    for cid, root in built.items():
        for ob in [root, *root.children_recursive]:
            ob.hide_render = cid != keep


def block(build, mats, loc):
    g = build.Geo()
    build.box(g, (0, 0, 0.125), (0.125, 0.125, 0.125), "scale_block", bevel=0.004)
    return build.bl.make_object("RenderOnlyBlock", g, mats, None, loc)


def render_all(built, mats, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    mats["scale_block"] = build.pt.plain("scale_block", "#d9d1bc", 0.7, 0.0)
    F, INK = rl.font, rl.INK
    for cid, root in built.items():
        name, blocks, _, family = build.CHESTS[cid]
        ports = sum(1 for o in root.children if o.name.startswith("IOPort"))
        hx, hy, hz = build.half_size(cid)
        show_only(built, cid)
        ground.location.z = -hz
        size = max(hx, hy, hz) * 2
        o3 = build.icon_ortho((hx, hy, hz)) * 1.02
        shots = {}
        for view, loc, target, ortho, w, h in (
                ("three_quarter", (1.55 * size, -2.6 * size, 1.05 * size + hz), (0, 0, -0.02 * size), o3, 800, 800),
                ("back", (-1.75 * size, 2.4 * size, 1.05 * size + hz), (0, 0, -0.02 * size), o3, 800, 800),
                ("front", (0, -3 * size, 0), (0, 0, 0), size * 1.25, 600, 520),
                ("top", (0, -0.0001, 3 * size), (0, 0, 0), size * 1.25, 600, 520)):
            cam = rl.camera(f"{cid}_{view}", loc, target, ortho=ortho)
            shots[view] = rl.render(cam, out / f"{cid}_{view}.png", w, h)
        sheet = Image.new("RGB", (2340, 1500), (246, 246, 244))
        d = ImageDraw.Draw(sheet)
        d.text((40, 24), f"{name.upper()} ({cid}): design review", fill=INK, font=F(44, True))
        d.text((40, 84), f"{blocks[0]} x {blocks[1]} x {blocks[2]} blocks ({blocks[0] * 25} x {blocks[1] * 25} x {blocks[2] * 25} cm, "
               f"X wide x Y tall x Z deep), {family}. Front toward the viewer; the lid hinges at the back."
               + (f" Item ports: {ports}, two-way, centred 37.5 cm above the floor." if ports else " No item port."),
               fill=INK, font=F(24))
        for i, view in enumerate(("three_quarter", "back")):
            sheet.paste(Image.open(shots[view]).convert("RGB"), (40 + i * 830, 140))
            d.text((54 + i * 830, 150), {"three_quarter": "THREE-QUARTER (FRONT RIGHT)", "back": "BACK (BACK LEFT)"}[view],
                   fill=INK, font=F(24, True))
        for i, view in enumerate(("front", "top")):
            y = 140 + i * 560
            sheet.paste(Image.open(shots[view]).convert("RGB"), (1700, y))
            d.text((1712, y + 8), view.upper(), fill=INK, font=F(22, True))
        icon = Image.open(build.ICONS / f"{cid}_icon.png").convert("RGBA")
        y = 970
        d.text((40, y + 10), "ICON (256 px)", fill=INK, font=F(24, True))
        for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
            tile = Image.new("RGB", (256, 256), bg)
            tile.paste(icon, (0, 0), icon)
            sheet.paste(tile, (40 + i * 280, y + 50))
        gas = Image.open(build.ICONS / "gas_container_icon.png").convert("RGBA")
        tile = Image.new("RGB", (256, 256), (40, 42, 46))
        tile.paste(gas, (0, 0), gas)
        sheet.paste(tile, (600, y + 50))
        d.text((600, y + 312), "Gas Container icon, for framing", fill=INK, font=F(20))
        sheet.save(out / f"{cid}_review.png")
        print(f"rendered {cid}_review.png")

    # the lineup: all five in tier order on the ground, beside a 25 cm block
    order = [c for c in ("chest", "small_chest", "large_chest", "cargo_chest", "ruin_chest") if c in built]
    x = 0.6                                    # the 25 cm block stands at the left end
    gap = 0.3
    placed = {}
    for cid in order:
        hx, hy, hz = build.half_size(cid)
        placed[cid] = (x + hx, hz)
        x += 2 * hx + gap
    width = x - gap
    for cid in order:
        cx, hz = placed[cid]
        built[cid].location = (cx - width / 2, 0, hz)
        for ob in [built[cid], *built[cid].children_recursive]:
            ob.hide_render = False
    cube = block(build, mats, (-width / 2 + 0.25, 0, 0))
    ground.location.z = 0
    cam = rl.camera("lineup", (width * 0.45, -width * 1.2, width * 0.55), (0, 0, 0.5), ortho=width * 1.0)
    p = rl.render(cam, out / "lineup_render.png", 1800, 900)
    cam2 = rl.camera("lineup_front", (0, -12, 0.75), (0, 0, 0.75), ortho=width * 1.04)
    p2 = rl.render(cam2, out / "lineup_front.png", 1800, 600)
    sheet = Image.new("RGB", (1880, 1700), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "CHESTS: lineup beside a 25 cm block", fill=INK, font=F(44, True))
    d.text((40, 84), "Left to right: a 25 cm block (bone), Scrap Chest 3x2x3, Small Chest 3x3x3, Large Chest 5x3x4, Cargo Chest 8x6x6, "
           "Ruin Chest 4x3x3.", fill=INK, font=F(22))
    sheet.paste(Image.open(p).convert("RGB"), (40, 130))
    sheet.paste(Image.open(p2).convert("RGB"), (40, 1060))
    d.text((52, 1068), "FRONT (ORTHOGRAPHIC)", fill=INK, font=F(22, True))
    sheet.save(out / "lineup.png")
    for cid in order:
        built[cid].location = (0, 0, 0)
    for ob in (cube,):
        bpy.data.objects.remove(ob)
    ground.hide_render = True
    print(f"rendered the review sheets into {out}")
