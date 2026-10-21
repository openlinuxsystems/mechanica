"""Review renders of the batch 1 crafting-material items (build_materials.py --render OUT_DIR).

For each item it writes, into OUT_DIR: the item three-quarter from the front right, from the back left and from the
top, and ``<id>_sheet.png`` with the three views and the icon on a dark and a light tile. ``lineup.png`` stands every
batch 1 item (and the three remodelled consumables) side by side at true size, each with its icon under it.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

import render_lift as rl

# Everything in batch 1 plus the consumables that got models here, in lineup order: (id, label).
LINEUP = (("glue", "Glue"), ("circuit_board", "Circuit Board"), ("beeswax", "Beeswax"), ("ember", "Ember"),
          ("crude_oil", "Crude Oil"), ("soil_bag", "Soil Bag"), ("water_bucket", "Water Bucket"),
          ("master_battery", "Master Battery"), ("warehouse_key", "Warehouse Key"), ("paint_ammo", "Paint Ammo"),
          ("explosive_remnant", "Explosive Remnant"), ("chemical", "Chemical"))


def sheet(item, title, root, out: Path, build, stats):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for view, loc, ortho in (("three_quarter", (1.45, -1.95, 1.5), 0.36), ("back", (-1.6, 1.8, 1.2), 0.36),
                             ("top", (0.0, -0.0001, 3.0), 0.3)):
        cam = rl.camera(f"{item}_{view}", loc, (0, 0, -0.07 if view != "top" else 0), ortho=ortho)
        shots[view] = rl.render(cam, out / f"{item}_{view}.png", 700, 700)
    ground.hide_render = True
    lo, hi, tris = stats
    F, INK = rl.font, rl.INK
    im = Image.new("RGB", (2560, 980), (246, 246, 244))
    d = ImageDraw.Draw(im)
    d.text((40, 24), f"{title.upper()}: design review", fill=INK, font=F(44, True))
    size = hi - lo
    d.text((40, 82), f"{item}.glb: {size.x * 100:.1f} x {size.y * 100:.1f} x {size.z * 100:.1f} cm inside the 25 cm "
           f"block, {tris} triangles", fill=INK, font=F(24))
    for i, view in enumerate(("three_quarter", "back", "top")):
        im.paste(Image.open(shots[view]).convert("RGB").resize((680, 680), Image.LANCZOS), (40 + i * 710, 140))
        d.text((54 + i * 710, 150), {"three_quarter": "THREE-QUARTER", "back": "BACK", "top": "TOP"}[view], fill=INK,
               font=F(24, True))
    ic = Image.open(build.ICONS / f"{item}_icon.png").convert("RGBA")
    d.text((2180, 110), "ICON", fill=INK, font=F(24, True))
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(ic, (0, 0), ic)
        im.paste(tile, (2180, 140 + i * 290))
    path = out / f"{item}_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def lineup(out: Path, build):
    build.bbe.reset_scene()
    roots, x = [], 0.0
    gap = 0.12
    for item, _ in LINEUP:
        root = build.import_glb(build.MESHES / f"{item}.glb")
        bpy.context.view_layer.update()
        lo, hi = build.bounds(build.bbe.mesh_tree(root))
        width = hi.x - lo.x
        root.location = Vector((x - lo.x, -(lo.y + hi.y) / 2, -lo.z))
        roots.append((item, root, x + width / 2))
        x += width + gap
    total = x - gap
    for _, root, _ in roots:
        root.location.x -= total / 2
    bpy.context.view_layer.update()
    rl.setup_world()
    cam = rl.camera("lineup", (1.2, -4.0, 2.2), (0, 0, 0.25), ortho=total * 1.04)
    w, h = 3000, 1100
    shot = rl.render(cam, out / "lineup_render.png", w, h)
    F, INK = rl.font, rl.INK
    im = Image.new("RGB", (w, h + 140 + w // len(LINEUP) + 40), (246, 246, 244))
    im.paste(Image.open(shot).convert("RGB"), (0, 120))
    d = ImageDraw.Draw(im)
    d.text((40, 24), "PARTS BATCH 1: crafting materials and items, at true size", fill=INK, font=F(48, True))
    d.text((40, 84), "Each item stands on the ground in its own model; its icon is below it. Crude Oil, Soil Bag and the "
           "Master Battery are placeable 2-5 block items; the rest are 1 block.", fill=INK, font=F(24))
    cell = w // len(roots)
    for n, (item, root, cx) in enumerate(roots, 1):
        lo, hi = build.bounds(build.bbe.mesh_tree(root))
        sx, sy = rl.project(cam, ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, hi.z), w, h)
        d.text((sx, 120 + sy - 30), str(n), fill=INK, font=F(34, True), anchor="mm")
        ic = Image.open(build.ICONS / f"{item}_icon.png").convert("RGBA").resize((cell - 30, cell - 30), Image.LANCZOS)
        tile = Image.new("RGB", ic.size, (40, 42, 46))
        tile.paste(ic, (0, 0), ic)
        px = (n - 1) * cell + 15
        im.paste(tile, (px, h + 140))
        d.text((px + (cell - 30) // 2, h + 140 + cell), f"{n} {dict(LINEUP)[item]}", fill=INK, font=F(22, True), anchor="mm")
    path = out / "lineup.png"
    im.save(path)
    print(f"wrote {path}")
