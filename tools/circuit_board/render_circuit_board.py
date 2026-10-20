"""Review renders of the Circuit Board (build_circuit_board.py --render OUT_DIR).

Writes, into OUT_DIR: the kit three-quarter from the front right and from the back left, a close-up of the board,
front and top views, the kit on the ground beside a 0.25 m block, and review_sheet.png with them and the icon.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bmesh
import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(kit, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for view, loc, target, ortho in (("three_quarter", (1.45, -1.95, 1.5), (0, 0, -0.07), 0.36),
                                     ("back", (-1.6, 1.8, 1.2), (0, 0, -0.07), 0.36),
                                     ("window", (0.5, -0.9, 1.6), (0, 0, build.TOP + 0.01), 0.22),
                                     ("front", (0, -3, 0), (0, 0, -0.08), 0.3),
                                     ("top", (0, -0.0001, 3), (0, 0, 0), 0.3)):
        cam = rl.camera(f"cb_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"circuit_board_{view}.png", 700, 700)
    # beside a 0.25 m block for scale
    kit["root"].location = (-0.17, 0, 0)
    me = bpy.data.meshes.new("ScaleBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    blk = bpy.data.objects.new("RenderOnlyBlock", me)
    blk.location = (0.17, 0, build.FLOOR + 0.125)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.42, 0.28, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(blk)
    cam = rl.camera("cb_pair", (1.3, -2.7, 1.4), (0, 0, -0.05), ortho=0.75)
    shots["pair"] = rl.render(cam, out / "circuit_board_pair.png", 900, 700)
    blk.hide_render = True
    kit["root"].location = (0, 0, 0)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2200, 1560), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "CIRCUIT BOARD: design review", fill=INK, font=F(44, True))
    d.text((40, 82), "A 1 x 1 x 1 block item (22 cm board). Green board screwed to a gunmetal tray: capacitor with a cyan band, chip, "
           "relay, mustard connector with copper pins, orange and blue wires, lit LED.", fill=INK, font=F(22))
    for i, view in enumerate(("three_quarter", "back", "window")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((680, 680), Image.LANCZOS), (40 + i * 710, 130))
        d.text((54 + i * 710, 140), {"three_quarter": "THREE-QUARTER", "back": "BACK", "window": "CLOSE-UP"}[view], fill=INK,
               font=F(24, True))
    y = 840
    for i, view in enumerate(("front", "top")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((420, 420), Image.LANCZOS), (40 + i * 440, y))
        d.text((52 + i * 440, y + 8), view.upper(), fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["pair"]).convert("RGB").resize((700, 544), Image.LANCZOS), (940, y))
    d.text((952, y + 8), "BESIDE A 25 CM BLOCK", fill=INK, font=F(22, True))
    icon = Image.open(build.ICONS / "circuit_board_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        sheet.paste(tile, (1680, y + i * 280))
    d.text((1680, y - 30), "ICON", fill=INK, font=F(22, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
