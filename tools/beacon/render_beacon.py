"""Review renders of the Beacon (build_beacon.py --render OUT_DIR).

Writes, into OUT_DIR, the Beacon dark, on (between flashes) and flashing from three-quarter, front and side; the
Beacon on a block, lit at dusk; and beacon_sheet.png laying them out, labelled with its size and triangle count.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl

STATES = ("off", "on", "flash")
VIEWS = {
    "three_quarter": ((0.75, -1.0, 0.75), 0.62),
    "front": ((0.0, -1.4, 0.0), 0.6),
    "side": ((1.4, 0.0, 0.0), 0.6),
}


def triangles(objects):
    count = 0
    for ob in objects:
        if ob.type == "MESH":
            count += sum(len(p.vertices) - 2 for p in ob.data.polygons)
    return count


def bounds(objects):
    from mathutils import Vector  # noqa: PLC0415
    lo, hi = Vector((9, 9, 9)), Vector((-9, -9, -9))
    for ob in objects:
        if ob.type != "MESH" or ob.name in ("Band", "Flash", "Pip"):
            continue
        for v in ob.data.vertices:
            p = ob.matrix_world @ v.co
            lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
    return lo, hi


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    objects = build.be.mesh_tree(rig["root"])
    for ob in objects:
        if ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data.use_nla = False
    tris = triangles(objects)
    lo, hi = bounds(objects)
    ground = rl.setup_world()
    ground.location.z = -0.25 - 0.0005
    target = (0, 0, 0)
    shots = {}
    for state in STATES:
        build.pose(rig, state)
        bpy.context.view_layer.update()
        for name, (loc, ortho) in VIEWS.items():
            cam = rl.camera(f"{state}_{name}", loc, target, ortho=ortho)
            shots[(state, name)] = rl.render(cam, out / f"beacon_{state}_{name}.png", 700, 700)
    # on a block at dusk, flashing, so the lamp's light shows
    import bmesh  # noqa: PLC0415
    block = bpy.data.meshes.new("Block")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(block)
    bm.free()
    bob = bpy.data.objects.new("RenderOnlyBlock", block)
    bob.location = (0, 0, -0.375)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.44, 0.47, 1)
    block.materials.append(mat)
    bpy.context.scene.collection.objects.link(bob)
    ground.location.z = -0.5
    lamp = bpy.data.lights.new("RenderOnlyLamp", "POINT")
    lamp.color = (0.22, 0.78, 1.0)
    lamp.energy = 6
    lamp.shadow_soft_size = 0.02
    lob = bpy.data.objects.new("RenderOnlyLamp", lamp)
    lob.location = (0, 0, build.LENS_MID + 0.06)
    bpy.context.scene.collection.objects.link(lob)
    world = bpy.context.scene.world.node_tree.nodes["Background"]
    world.inputs["Strength"].default_value = 0.08
    for ob in bpy.context.scene.objects:
        if ob.type == "LIGHT" and ob.name != "RenderOnlyLamp":
            ob.data.energy *= 0.12
    build.pose(rig, "flash")
    cam = rl.camera("dusk", (1.0, -1.3, 0.75), (0, 0, -0.12), ortho=0.95)
    rl.render(cam, out / "beacon_dusk_on_block.png", 700, 700)
    build.pose(rig, "off")
    sheet(out, shots, tris, lo, hi)
    return tris, lo, hi


def sheet(out, shots, tris, lo, hi):
    w = 360
    title = rl.font(32, bold=True)
    label = rl.font(20, bold=True)
    small = rl.font(18)
    cols = len(VIEWS) + 1
    img = Image.new("RGB", (w * cols, 80 + (w + 44) * len(STATES) + 70), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.text((22, 20), "BEACON (SALVAGER TECH): DARK, ON, FLASHING", fill=rl.INK, font=title)
    for row, state in enumerate(STATES):
        y = 80 + row * (w + 44)
        for col, name in enumerate(VIEWS):
            tile = Image.open(shots[(state, name)]).convert("RGB").resize((w, w), Image.LANCZOS)
            img.paste(tile, (col * w, y + 36))
            d.text((col * w + 14, y + 8), f"{state.upper()}: {name.replace('_', '-').upper()}", fill=rl.INK, font=label)
    dusk = Image.open(out / "beacon_dusk_on_block.png").convert("RGB").resize((w, w), Image.LANCZOS)
    img.paste(dusk, (len(VIEWS) * w, 80 + 36))
    d.text((len(VIEWS) * w + 14, 88), "FLASHING, ON A BLOCK, DUSK", fill=rl.INK, font=label)
    size = hi - lo
    d.text((22, img.height - 56),
           f"1 x 2 x 1 blocks (0.25 x 0.5 x 0.25 m). Model {size.x * 100:.1f} x {size.z * 100:.1f} x {size.y * 100:.1f} cm "
           f"(w x h x d), {tris} triangles. Hull / gunmetal / copper, cyan only when on.", fill=rl.INK, font=small)
    img.save(out / "beacon_sheet.png")
