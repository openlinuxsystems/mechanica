"""Review renders of the Scrap Driver's Seat (run through ``build_scrap_seat.py --render OUT_DIR``).

Renders the reference sheet's views (front three-quarter, side, rear, top, a steering detail and a fabric detail), a
scale view beside a 4 x 4 x 6 block outline, and the enter, seated-rattle and exit clips as strips, then lays them out
on one labelled sheet with dimensions in blocks and metres. Cycles on the CPU; nothing is downloaded.
"""

from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

import render_lift as rl

INK = rl.INK


def outline_box(name, half, centre):
    """A wireframe of the part's 4 x 4 x 6 block box, drawn as thin dark bars, with the 0.25 m grid on its faces."""
    import bmesh  # noqa: PLC0415
    bm = bmesh.new()
    hx, hy, hz = half
    r = 0.003

    def bar(a, b):
        a, b = Vector(a), Vector(b)
        ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=r, radius2=r, depth=(b - a).length)
        rot = (b - a).to_track_quat("Z", "Y").to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=ret["verts"])
        bmesh.ops.translate(bm, vec=(a + b) / 2, verts=ret["verts"])
    xs = [-hx + 0.25 * i for i in range(int(round(2 * hx / 0.25)) + 1)]
    ys = [-hy + 0.25 * i for i in range(int(round(2 * hy / 0.25)) + 1)]
    zs = [-hz + 0.25 * i for i in range(int(round(2 * hz / 0.25)) + 1)]
    for x in xs:
        for z in (-hz, hz):
            bar((x, -hy, z), (x, hy, z))
        for y in (-hy, hy):
            bar((x, y, -hz), (x, y, hz))
    for y in ys:
        for z in (-hz, hz):
            bar((-hx, y, z), (hx, y, z))
        for x in (-hx, hx):
            bar((x, y, -hz), (x, y, hz))
    for z in zs:
        for x in (-hx, hx):
            bar((x, -hy, z), (x, hy, z))
        for y in (-hy, hy):
            bar((-hx, y, z), (hx, y, z))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("RenderOnly" + name, me)
    ob.location = centre
    mat = bpy.data.materials.new(name)
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.05, 0.06, 0.07, 1)
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    for ob in bpy.context.scene.objects:      # the baked clips would override the poses set below
        ob.animation_data_clear()
    ground = rl.setup_world()
    ground.location.z = -0.5
    sc = bpy.context.scene
    sc.cycles.samples = 64
    sc.render.film_transparent = False
    build.rest(rig)
    bpy.context.view_layer.update()
    W, H = 900, 640
    shots, dims = {}, {}
    f_dim = rl.font(20)

    cam = rl.camera("ThreeQuarter", (2.7, 2.5, 1.55), (0, 0.05, -0.12), lens=50)
    shots["three_quarter"] = rl.render(cam, out / "scrap_seat_three_quarter.png", W, H)
    dims["three_quarter"] = []
    cam = rl.camera("Side", (10, 0, 0.0), (0, 0, 0.0), ortho=2.1)
    shots["side"] = rl.render(cam, out / "scrap_seat_side.png", W, H)
    dims["side"] = [(rl.project(cam, (0.5, -0.75, -0.56), W, H), rl.project(cam, (0.5, 0.75, -0.56), W, H), "6 BLOCKS (1.50 m)", (0, 10), False),
                    (rl.project(cam, (0.5, 0.8, -0.5), W, H), rl.project(cam, (0.5, 0.8, 0.5), W, H), "4 BLOCKS\n(1.00 m)", (0, 0), True)]
    cam = rl.camera("Rear", (0, -10, 0.0), (0, 0, 0.0), ortho=2.1)
    shots["rear"] = rl.render(cam, out / "scrap_seat_rear.png", W, H)
    dims["rear"] = [(rl.project(cam, (-0.5, -0.8, -0.56), W, H), rl.project(cam, (0.5, -0.8, -0.56), W, H), "4 BLOCKS (1.00 m)", (0, 10), False)]
    cam = rl.camera("Top", (0, 0.0001, 10), (0, 0, 0), ortho=2.0)
    cam.rotation_euler.z = math.radians(90)
    shots["top"] = rl.render(cam, out / "scrap_seat_top.png", W, H)
    dims["top"] = []
    hub = build.HUB
    cam = rl.camera("Steering", tuple(hub + Vector((0.75, 0.55, 0.3))), tuple(hub + Vector((0, 0.18, -0.28))), lens=55)
    shots["steering"] = rl.render(cam, out / "scrap_seat_steering.png", W, H)
    dims["steering"] = []
    cam = rl.camera("Fabric", (0.38, 0.32, 0.42), (0.05, -0.25, 0.02), lens=70)
    shots["fabric"] = rl.render(cam, out / "scrap_seat_fabric.png", W, H)
    dims["fabric"] = []
    box = outline_box("Blocks", (0.5, 0.75, 0.5), (0, 0, 0))
    sil = rl.silhouette(-1.05, 0.2)
    cam = rl.camera("Scale", (3.6, 3.0, 1.6), (-0.35, 0, 0.1), lens=40)
    shots["scale"] = rl.render(cam, out / "scrap_seat_scale.png", W, H)
    dims["scale"] = []
    box.hide_render = True
    sil.hide_render = True

    # the clips, a frame strip each
    strips = []
    cam = rl.camera("Clip", (2.2, 1.0, 0.9), (0, -0.2, -0.05), lens=50)
    for clip, times in (("enter", (0.0, 0.05, 0.1, 0.2, 0.4, 0.8)), ("exit", (0.0, 0.06, 0.12, 0.25, 0.45, 0.7)),
                        ("seated_rattle", (0.0, 0.03, 0.07, 0.1, 0.13, 0.17))):
        frames = []
        for t in times:
            build.pose(rig, clip, t)
            bpy.context.view_layer.update()
            frames.append((t, rl.render(cam, out / f"scrap_seat_{clip}_{int(t * 100):03d}.png", 480, 360)))
        strips.append((clip, frames))
    build.rest(rig)

    title_f, head_f = rl.font(44, True), rl.font(24, True)
    sheet = Image.new("RGB", (2860, 3020), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((50, 36), "MECHANICA SCRAP DRIVER'S SEAT", fill=INK, font=title_f)
    info = build.br.glb_info(build.OUT)
    d.text((50, 96), f"4 x 4 x 6 blocks (1.00 m wide, 1.00 m high, 1.50 m deep). Clean scrapyard: light-rust pipe, raw pallet wood, "
           f"olive-tan canvas with simple patches, light metal plates. {info['tris']} triangles, {info['kib']:.0f} KiB.",
           fill=INK, font=rl.font(24))

    def place(key, title, x, y):
        im = Image.open(shots[key]).convert("RGB")
        sheet.paste(im, (x, y))
        dd = ImageDraw.Draw(sheet)
        dd.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        dd.text((x, y - 34), title, fill=INK, font=head_f)
        for a, b, text, off, vert in dims[key]:
            rl.dim_line(dd, (x + a[0], y + a[1]), (x + b[0], y + b[1]), text, f_dim, off, vert)

    place("three_quarter", "FRONT THREE-QUARTER VIEW", 50, 190)
    place("side", "SIDE VIEW", 980, 190)
    place("rear", "REAR VIEW", 1910, 190)
    place("top", "TOP VIEW", 50, 900)
    place("steering", "STEERING DETAIL (pipe rim, column with collar, bolted mount plate)", 980, 900)
    place("fabric", "FABRIC DETAIL (puffy stitched canvas, clean patches)", 1910, 900)
    place("scale", "SCALE (4 x 4 x 6 block box, 1.5 m figure)", 50, 1610)
    # the clips beside the scale view, a row each
    y = 1610
    for clip, frames in strips:
        d.text((980, y - 34), {"enter": "ENTER (0.8 s): cushions sink, frame rattles, pallet creaks",
                               "exit": "EXIT (0.7 s): cushions spring back, last rattle",
                               "seated_rattle": "SEATED WITH AN ENGINE RUNNING (2 s loop): low rattle"}[clip], fill=INK, font=head_f)
        for i, (t, p) in enumerate(frames):
            im = Image.open(p).convert("RGB").resize((300, 225), Image.LANCZOS)
            sheet.paste(im, (980 + i * 305, y))
            d.text((986 + i * 305, y + 6), f"{t:.2f} s", fill=INK, font=rl.font(20, True))
        y += 290
    sheet = sheet.crop((0, 0, 2860, 2520))
    sheet.save(out / "scrap_seat_review_sheet.png")
    print(f"rendered the review sheet into {out}")
