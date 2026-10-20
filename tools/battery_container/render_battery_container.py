"""Review renders of the Battery Container (build_battery.py --render OUT_DIR).

Writes, into OUT_DIR: the container three-quarter from the front right and from the back left, a close-up of the front,
front and top views, the container on the ground beside a 0.25 m block, and review_sheet.png with them and the icon.
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
    for view, loc, target, ortho in (("three_quarter", (1.45, -1.95, 1.35), (0, 0, -0.02), 0.78),
                                     ("back", (-1.6, 1.8, 1.2), (0, 0, -0.02), 0.78),
                                     ("window", (0.5, -2.2, 0.5), (0, -0.2, -0.02), 0.42),
                                     ("front", (0, -3, 0), (0, 0, -0.02), 0.62),
                                     ("top", (0, -0.0001, 3), (0, 0, 0), 0.6)):
        cam = rl.camera(f"bc_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"battery_{view}.png", 700, 700)
    # beside a 0.25 m block for scale
    kit["root"].location = (-0.3, 0, 0)
    me = bpy.data.meshes.new("ScaleBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    blk = bpy.data.objects.new("RenderOnlyBlock", me)
    blk.location = (0.25, 0, build.FLOOR + 0.125)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.42, 0.28, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(blk)
    cam = rl.camera("bc_pair", (1.3, -2.7, 1.4), (-0.03, 0, -0.1), ortho=1.05)
    shots["pair"] = rl.render(cam, out / "battery_pair.png", 900, 700)
    blk.hide_render = True
    kit["root"].location = (0, 0, 0)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2200, 1560), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "BATTERY CONTAINER: design review", fill=INK, font=F(44, True))
    d.text((40, 82), "A 2 x 2 x 2 block part (50 cm) in the Gas Container's style. Five-cell teal charge gauge (3 of 5 lit), copper "
           "terminals with a red +, red breaker, two power sockets.", fill=INK, font=F(22))
    for i, view in enumerate(("three_quarter", "back", "window")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((680, 680), Image.LANCZOS), (40 + i * 710, 130))
        d.text((54 + i * 710, 140), {"three_quarter": "THREE-QUARTER", "back": "BACK", "window": "FRONT CLOSE-UP"}[view], fill=INK,
               font=F(24, True))
    y = 840
    for i, view in enumerate(("front", "top")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((420, 420), Image.LANCZOS), (40 + i * 440, y))
        d.text((52 + i * 440, y + 8), view.upper(), fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["pair"]).convert("RGB").resize((700, 544), Image.LANCZOS), (940, y))
    d.text((952, y + 8), "BESIDE A 25 CM BLOCK", fill=INK, font=F(22, True))
    icon = Image.open(build.ICONS / "battery_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        sheet.paste(tile, (1680, y + i * 280))
    d.text((1680, y - 30), "ICON", fill=INK, font=F(22, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
