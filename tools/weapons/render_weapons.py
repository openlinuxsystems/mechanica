"""Review renders of the potato weapons and the Popper (build_weapons.py --render OUT_DIR).

Writes, into OUT_DIR: each item three-quarter from the front left and from the side, all four in a row beside a
0.25 m block, and review_sheet.png with them and the icons on dark and light tiles. Cycles on the CPU; LIFT_SAMPLES
sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bmesh
import bpy
from PIL import Image, ImageDraw

import render_lift as rl

NAMES = {"potato_blaster": "Potato Blaster", "potato_scattergun": "Potato Scattergun", "potato_launcher": "Potato Launcher",
         "popper": "Popper"}
FLOOR = -0.12


def render_all(out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    build.bbe.reset_scene()
    mats = build.make_materials()
    ground = rl.setup_world()
    ground.location.z = FLOOR - 0.03
    roots = {i: build.build_item(i, mats) for i in build.ITEMS}
    shots = {}
    for item_id, root in roots.items():
        for other in roots.values():
            for ob in build.bbe.mesh_tree(other):
                ob.hide_render = other is not root
        size = build.ITEMS[item_id][2]
        for view, loc in (("three_quarter", (1.2, -1.9, 0.9)), ("side", (0.0001, -3, 0.0))):
            cam = rl.camera(f"wp_{item_id}_{view}", loc, (0, 0, 0), ortho=size * 1.15)
            shots[item_id, view] = rl.render(cam, out / f"{item_id}_{view}.png", 700, 520)
    # all four in a row beside a 0.25 m block, standing on the ground
    xs = {"popper": -1.05, "potato_scattergun": -0.72, "potato_blaster": -0.12, "potato_launcher": 0.68}
    for item_id, root in roots.items():
        for ob in build.bbe.mesh_tree(root):
            ob.hide_render = False
        root.location = (xs[item_id], 0, 0)
    me = bpy.data.meshes.new("ScaleBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    blk = bpy.data.objects.new("RenderOnlyBlock", me)
    blk.location = (1.3, 0, FLOOR - 0.03 + 0.125)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.42, 0.28, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(blk)
    cam = rl.camera("wp_row", (0.15 + 1.2, -3.2, 1.5), (0.15, 0, -0.02), ortho=2.8)
    shots["row"] = rl.render(cam, out / "weapons_row.png", 1400, 640)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2400, 1470), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "POTATO WEAPONS AND POPPER: design review", fill=INK, font=F(44, True))
    d.text((40, 82), "One family: a bright painted tank (Blaster blue, Scattergun red, Launcher yellow), dark caps and bands, steel barrels, "
           "orange muzzles. The Popper is a corn cob with a fuse band and a pull ring.", fill=INK, font=F(22))
    for row, item_id in enumerate(build.ITEMS):
        y = 130 + row * 330
        d.text((40, y + 8), NAMES[item_id].upper(), fill=INK, font=F(24, True))
        for col, view in enumerate(("three_quarter", "side")):
            sheet.paste(Image.open(shots[item_id, view]).convert("RGB").resize((420, 312), Image.LANCZOS),
                        (40 + col * 440, y + 40 - 30))
        icon = Image.open(build.ICONS / f"{item_id}_icon.png").convert("RGBA")
        for i, bg in enumerate(((40, 42, 46), (75, 155, 230))):
            tile = Image.new("RGB", (256, 256), bg)
            tile.paste(icon, (0, 0), icon)
            sheet.paste(tile, (940 + i * 276, y + 30))
    d.text((940, 104), "ICONS (DARK, INVENTORY BLUE)", fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["row"]).convert("RGB").resize((820, 375), Image.LANCZOS), (1540, 160))
    d.text((1550, 130), "SIDE BY SIDE, WITH A 25 CM BLOCK", fill=INK, font=F(22, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
