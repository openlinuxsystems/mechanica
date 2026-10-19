"""Review renders of the Button (build_button.py --render OUT_DIR).

Writes, into OUT_DIR, the reference sheet's views of the button raised (released) and pressed: three-quarter, top,
front and side; the button on a block; and review_sheet.png laying them out like the reference sheet, labelled.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    for ob in build.be.mesh_tree(rig["root"]):      # the baked clips would override the poses set below
        if ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data.use_nla = False
    ground = rl.setup_world()
    ground.location.z = -0.125 - 0.0005
    sc = bpy.context.scene
    target = (0, 0, -0.1)
    views = {
        "three_quarter": ((0.55, -0.7, 0.62), 0.34),
        "top": ((0.0, -0.0001, 1.2), 0.30),
        "front": ((0.0, -1.2, 0.2), 0.30),
        "side": ((1.2, 0.0, 0.2), 0.30),
    }
    shots = {}
    for state, down in (("released", 0.0), ("pressed", 1.0)):
        build.pose(rig, down)
        bpy.context.view_layer.update()
        for name, (loc, ortho) in views.items():
            cam = rl.camera(f"{state}_{name}", loc, target, ortho=ortho)
            shots[(state, name)] = rl.render(cam, out / f"button_{state}_{name}.png", 800, 800)
    # on a block, as placed in the world
    build.pose(rig, 0.0)
    import bmesh  # noqa: PLC0415
    block = bpy.data.meshes.new("Block")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(block)
    bm.free()
    bob = bpy.data.objects.new("RenderOnlyBlock", block)
    bob.location = (0, 0, -0.25)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.44, 0.47, 1)
    block.materials.append(mat)
    sc.collection.objects.link(bob)
    ground.location.z = -0.375
    cam = rl.camera("on_block", (0.9, -1.1, 0.75), (0, 0, -0.17), ortho=0.6)
    rl.render(cam, out / "button_on_block.png", 800, 800)
    bob.hide_render = True
    sheet(out, shots)


def sheet(out, shots):
    w = 400
    title = rl.font(34, bold=True)
    label = rl.font(22, bold=True)
    small = rl.font(18)
    img = Image.new("RGB", (w * 4, 90 + (w + 50) * 2 + 70), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.text((24, 22), "BUTTON: RELEASED (RAISED) AND PRESSED", fill=rl.INK, font=title)
    for row, state in enumerate(("released", "pressed")):
        y = 90 + row * (w + 50)
        for col, name in enumerate(("three_quarter", "top", "front", "side")):
            tile = Image.open(shots[(state, name)]).convert("RGB").resize((w, w), Image.LANCZOS)
            img.paste(tile, (col * w, y + 40))
            d.text((col * w + 16, y + 8), f"{state.upper()}: {name.replace('_', '-').upper()}", fill=rl.INK, font=label)
    d.text((24, img.height - 56),
           "1 x 1 x 1 block (0.25 m). Plate 24.4 cm square, 2.2 cm thick. Face raised 4.0 cm, travel 2.2 cm. "
           "Gunmetal / teal / copper.", fill=rl.INK, font=small)
    img.save(out / "review_sheet.png")
