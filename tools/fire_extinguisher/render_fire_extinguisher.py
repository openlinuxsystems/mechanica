"""Review renders of the Fire Extinguisher (build_fire_extinguisher.py --render OUT_DIR).

Writes, into OUT_DIR: the item three-quarter from the front left and from the back right, its side, a close-up of the
valve head, the held model, the item beside a 0.25 m block, and review_sheet.png with them and the icon. Cycles on the
CPU; LIFT_SAMPLES sets the samples. The in-game views (held in first and third person, spraying) are
``tools/capture-extinguisher.sh``.
"""

from __future__ import annotations

from pathlib import Path

import bmesh
import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(item, held, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for ob in [held["root"], *held["root"].children_recursive]:
        ob.hide_render = True
    for view, loc, target, ortho in (("three_quarter", (-1.5, -2.0, 1.1), (0, 0, -0.01), 0.62),
                                     ("back", (1.6, 1.8, 0.9), (0, 0, -0.01), 0.62),
                                     ("side", (-3, 0, 0), (0, 0, -0.01), 0.58),
                                     ("front", (0, -3, 0), (0, 0, -0.01), 0.58),
                                     ("head", (-0.7, -1.0, 0.9), (0, 0, 0.18), 0.2)):
        cam = rl.camera(f"fx_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"extinguisher_{view}.png", 700, 700)
    # the held model (hose forward, nozzle along -Y), alone
    for ob in [item["root"], *item["root"].children_recursive]:
        ob.hide_render = True
    for ob in [held["root"], *held["root"].children_recursive]:
        ob.hide_render = False
    cam = rl.camera("fx_held", (-1.6, -1.6, 1.0), (0, -0.06, 0.02), ortho=0.66)
    shots["held"] = rl.render(cam, out / "extinguisher_held_model.png", 700, 700)
    for ob in [held["root"], *held["root"].children_recursive]:
        ob.hide_render = True
    for ob in [item["root"], *item["root"].children_recursive]:
        ob.hide_render = False
    # beside a 0.25 m block for scale
    item["root"].location = (-0.15, 0, 0)
    me = bpy.data.meshes.new("ScaleBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    blk = bpy.data.objects.new("RenderOnlyBlock", me)
    blk.location = (0.15, 0, build.FLOOR + 0.125)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.42, 0.28, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(blk)
    cam = rl.camera("fx_pair", (-1.3, -2.7, 1.0), (0, 0, -0.06), ortho=0.85)
    shots["pair"] = rl.render(cam, out / "extinguisher_pair.png", 900, 700)
    blk.hide_render = True
    item["root"].location = (0, 0, 0)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2200, 1600), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "FIRE EXTINGUISHER: design review", fill=INK, font=F(44, True))
    d.text((40, 82), "A 1 x 1 x 2 block item (50 cm), Colony Works: signal red cylinder (#C8423A, not orange), white label with the colony "
           "strip and a foam-over-flame pictogram, steel valve head with gauge, lever and pin, black hose and nozzle.", fill=INK, font=F(21))
    for i, view in enumerate(("three_quarter", "back", "side", "head")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((520, 520), Image.LANCZOS), (40 + i * 540, 130))
        d.text((54 + i * 540, 140), {"three_quarter": "THREE-QUARTER", "back": "BACK", "side": "SIDE", "head": "VALVE HEAD"}[view],
               fill=INK, font=F(24, True))
    y = 690
    sheet.paste(Image.open(shots["held"]).convert("RGB").resize((520, 520), Image.LANCZOS), (40, y))
    d.text((52, y + 8), "HELD MODEL (HOSE FORWARD)", fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["front"]).convert("RGB").resize((520, 520), Image.LANCZOS), (580, y))
    d.text((592, y + 8), "FRONT", fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["pair"]).convert("RGB").resize((680, 529), Image.LANCZOS), (1120, y))
    d.text((1132, y + 8), "BESIDE A 25 CM BLOCK", fill=INK, font=F(22, True))
    icon = Image.open(build.ICONS / "fire_extinguisher_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        sheet.paste(tile, (1830, y + 20 + i * 280))
    d.text((1830, y - 14), "ICON", fill=INK, font=F(22, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
