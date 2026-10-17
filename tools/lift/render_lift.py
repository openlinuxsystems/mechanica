"""Review renders of the Lift (run through ``build_lift.py --render OUT_DIR``).

Renders the concept sheet's views (front, side, top, three-quarter collapsed and three-quarter partly
extended), full extension, and the deploy sequence, each with a 1.5 m human silhouette for scale, and lays
them out on one labelled sheet with dimensions in units and metres. Cycles on the CPU; nothing is downloaded.
"""

from __future__ import annotations

import math
import os
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFont

SILHOUETTE_HEIGHT = 1.5
SAMPLES = int(os.environ.get("LIFT_SAMPLES", "96"))


# =====================================================================================================
# Stage: ground, lights, silhouette
# =====================================================================================================

def setup_world():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.82, 0.83, 0.85, 1)
    bg.inputs["Strength"].default_value = 0.55

    ground = bpy.data.meshes.new("Ground")
    s = 40
    ground.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    gob = bpy.data.objects.new("RenderOnlyGround", ground)
    sc.collection.objects.link(gob)
    mat = bpy.data.materials.new("ground")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.78, 0.79, 0.8, 1)
    b.inputs["Roughness"].default_value = 0.9
    ground.materials.append(mat)

    def area(name, loc, energy, size, rot):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = size
        ob = bpy.data.objects.new(name, light)
        ob.location = loc
        ob.rotation_euler = rot
        sc.collection.objects.link(ob)
        return ob

    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2
    sun.angle = math.radians(6)
    sob = bpy.data.objects.new("RenderOnlySun", sun)
    sob.rotation_euler = (math.radians(50), 0, math.radians(-35))
    sc.collection.objects.link(sob)
    area("RenderOnlyKey", (-3, -4, 4), 900, 4, (math.radians(50), 0, math.radians(-35)))
    area("RenderOnlyRim", (3, 4, 3), 500, 3, (math.radians(-55), 0, math.radians(145)))
    return gob


def silhouette(x, y):
    """A flat dark human figure 1.5 m tall, facing the front, built from soft capsules."""
    import bmesh  # noqa: PLC0415
    bm = bmesh.new()
    h = SILHOUETTE_HEIGHT
    k = h / 1.5

    def capsule(p0, p1, r):
        p0, p1 = Vector(p0) * 1.0, Vector(p1) * 1.0
        d = p1 - p0
        length = d.length
        ret = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r)
        for v in ret["verts"]:
            v.co.z = v.co.z + (length / 2 if v.co.z > 0 else -length / 2)
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=rot, verts=ret["verts"])
        bmesh.ops.translate(bm, vec=(p0 + p1) / 2, verts=ret["verts"])

    def s(x_, z_):
        return (x_ * k, 0, z_ * k)

    capsule(s(0, 1.33), s(0, 1.43), 0.085 * k)                 # head
    capsule(s(0, 0.86), s(0, 1.20), 0.15 * k)                  # torso
    capsule(s(-0.1, 1.17), s(0.1, 1.17), 0.07 * k)              # shoulders
    for side in (-1, 1):
        capsule(s(side * 0.2, 1.16), s(side * 0.26, 0.86), 0.045 * k)   # upper arm
        capsule(s(side * 0.26, 0.86), s(side * 0.28, 0.62), 0.04 * k)   # forearm
        capsule(s(side * 0.085, 0.82), s(side * 0.1, 0.44), 0.07 * k)   # thigh
        capsule(s(side * 0.1, 0.44), s(side * 0.11, 0.07), 0.055 * k)   # shin
        capsule(s(side * 0.11, 0.035), s(side * 0.11, 0.035), 0.045 * k)
    capsule(s(0, 1.25), s(0, 1.3), 0.05 * k)                   # neck
    me = bpy.data.meshes.new("Silhouette")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("RenderOnlySilhouette", me)
    ob.location = (x, y, 0)
    mat = bpy.data.materials.new("silhouette")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.012, 0.012, 0.014, 1)
    b.inputs["Roughness"].default_value = 0.8
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    bpy.context.scene.collection.objects.link(ob)
    return ob


def camera(name, loc, target, ortho=None, lens=50):
    cam = bpy.data.cameras.new(name)
    if ortho is not None:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens = lens
    ob = bpy.data.objects.new("RenderOnly" + name, cam)
    ob.location = loc
    d = Vector(target) - Vector(loc)
    ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(ob)
    return ob


def render(cam, path, w, h):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    return path


def project(cam, p, w, h):
    v = world_to_camera_view(bpy.context.scene, cam, Vector(p))
    return (v.x * w, (1 - v.y) * h)


# =====================================================================================================
# Sheet
# =====================================================================================================

def font(size, bold=False):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", "LiberationSans-Bold.ttf" if bold else "LiberationSans-Regular.ttf"):
        for root in ("/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/truetype/liberation"):
            p = Path(root) / name
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


INK = (28, 30, 34)


def dim_line(d, a, b, label, f, offset=(0, 0), vertical=False):
    """A dimension line between pixel points a and b with end ticks and a label."""
    ax, ay = a[0] + offset[0], a[1] + offset[1]
    bx, by = b[0] + offset[0], b[1] + offset[1]
    d.line([(ax, ay), (bx, by)], fill=INK, width=2)
    for (x, y) in ((ax, ay), (bx, by)):
        if vertical:
            d.line([(x - 9, y), (x + 9, y)], fill=INK, width=2)
        else:
            d.line([(x, y - 9), (x, y + 9)], fill=INK, width=2)
    lines = label.split("\n")
    mx, my = (ax + bx) / 2, (ay + by) / 2
    tw = max(d.textlength(t, font=f) for t in lines)
    th = f.size * 1.25 * len(lines)
    if vertical:
        x0, y0 = mx + 12, my - th / 2
        d.rectangle([x0 - 4, y0 - 2, x0 + tw + 4, y0 + th], fill=(255, 255, 255))
    else:
        x0, y0 = mx - tw / 2, my + 10
    for i, t in enumerate(lines):
        d.text((x0 + (tw - d.textlength(t, font=f)) / 2 if not vertical else x0, y0 + i * f.size * 1.25), t, fill=INK, font=f)


def units(m):
    u = m / 0.25
    return f"{u:g} UNITS\n({m:.2f} m)"


def render_all(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    for ob in bpy.context.scene.objects:      # the baked clips would override the poses set below
        ob.animation_data_clear()
    setup_world()
    sil = silhouette(-1.15, 0.0)
    build.apply_deploy(rig, build.DEPLOY_SECONDS)
    build.apply_extend(rig, build.H_REST)
    bpy.context.view_layer.update()
    W, H = 1100, 760
    shots = {}
    labels = {}
    f_dim = font(22)

    # front (ortho, from -Y)
    cam = camera("Front", (0, -10, 0.45), (0, 0, 0.45), ortho=3.2)
    sil.location = (-1.25, 0.3, 0)
    shots["front"] = render(cam, out / "lift_front.png", W, H)
    labels["front"] = [(project(cam, (-0.5, -0.6, 0), W, H), project(cam, (0.5, -0.6, 0), W, H), units(1.0), (0, 40), False),
                       (project(cam, (0.55, 0, 0), W, H), project(cam, (0.55, 0, 0.5), W, H), units(0.5), (130, 0), True)]
    # side (ortho, from +X)
    cam = camera("Side", (10, 0, 0.45), (0, 0, 0.45), ortho=3.2)
    sil.location = (0.3, 1.25, 0)
    sil.rotation_euler = (0, 0, math.radians(90))
    shots["side"] = render(cam, out / "lift_side.png", W, H)
    labels["side"] = [(project(cam, (0.6, 0.5, 0), W, H), project(cam, (0.6, -0.5, 0), W, H), units(1.0), (0, 40), False),
                      (project(cam, (0, -1.05, 0), W, H), project(cam, (0, -1.05, 0.5), W, H), units(0.5), (0, 0), True)]
    # top (ortho, straight down)
    sil.location = (-1.25, 0, 0)
    sil.rotation_euler = (0, 0, 0)
    cam = camera("Top", (0, -0.0001, 10), (0, 0, 0), ortho=2.4)
    shots["top"] = render(cam, out / "lift_top.png", W, H)
    labels["top"] = [(project(cam, (-0.5, -0.56, 0.6), W, H), project(cam, (0.5, -0.56, 0.6), W, H), units(1.0), (0, 30), False),
                     (project(cam, (0.56, -0.5, 0.6), W, H), project(cam, (0.56, 0.5, 0.6), W, H), units(1.0), (110, 0), True)]
    # three-quarter, collapsed (resting)
    sil.location = (-1.3, 0.6, 0)
    sil.rotation_euler = (0, 0, math.radians(-20))
    cam = camera("ThreeQuarter", (3.4, -4.0, 2.2), (-0.35, 0, 0.55), lens=50)
    shots["three_quarter"] = render(cam, out / "lift_three_quarter_collapsed.png", W, H)
    labels["three_quarter"] = []
    # three-quarter, partly extended (8 units, 2 m deck)
    build.apply_extend(rig, 2.0)
    cam = camera("ThreeQuarterUp", (4.2, -5.2, 2.6), (-0.3, 0, 1.0), lens=45)
    shots["extended"] = render(cam, out / "lift_three_quarter_extended.png", W, H)
    labels["extended"] = [(project(cam, (0.75, -0.75, 0), W, H), project(cam, (0.75, -0.75, 2.0), W, H), units(2.0), (60, 0), True)]
    # full extension, front ortho with the silhouette
    build.apply_extend(rig, build.H_MAX)
    sil.location = (-1.25, 0.3, 0)
    sil.rotation_euler = (0, 0, 0)
    cam = camera("Full", (0, -20, build.H_MAX / 2 + 0.3), (0, 0, build.H_MAX / 2 + 0.3), ortho=build.H_MAX * 1.16)
    shots["full"] = render(cam, out / "lift_full_extension.png", 760, H)
    labels["full"] = [(project(cam, (0.6, 0, 0), 760, H), project(cam, (0.6, 0, build.H_MAX), 760, H), units(build.H_MAX), (40, 0), True)]
    # deploy sequence
    build.apply_extend(rig, build.H_TRANSPORT)
    sil.hide_render = True
    seq = []
    cam = camera("Deploy", (2.4, -2.8, 1.6), (0, 0, 0.22), lens=50)
    for t in (0.0, 0.4, 0.7, 0.95, 1.3, 1.9):
        build.apply_deploy(rig, t)
        build.apply_extend(rig, rig["column_drive"].location.z)
        bpy.context.view_layer.update()
        seq.append((t, render(cam, out / f"lift_deploy_{int(t * 100):03d}.png", 560, 420)))
    sil.hide_render = False

    # one labelled sheet
    title_f, head_f = font(44, True), font(24, True)
    sheet = Image.new("RGB", (3400, 2700), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 40), "MECHANICA LIFT - INDUSTRIAL HYDRAULIC PLATFORM", fill=INK, font=title_f)
    d.text((60, 100), "Review renders from tools/lift/build_lift.py. 1 unit = 0.25 m. Collapsed 4 x 4 x 2 units "
           "(1.00 x 1.00 x 0.50 m), extends in 1-unit steps to " + f"{build.H_MAX / 0.25:g} units ({build.H_MAX:.2f} m). Silhouette 1.5 m.", fill=INK, font=font(24))

    def place(key, title, x, y, scale=1.0):
        im = Image.open(shots[key]).convert("RGB")
        if scale != 1.0:
            im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
        sheet.paste(im, (x, y))
        dd = ImageDraw.Draw(sheet)
        dd.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        dd.text((x, y - 34), title, fill=INK, font=head_f)
        for a, b, text, off, vert in labels[key]:
            dim_line(dd, (x + a[0] * scale, y + a[1] * scale), (x + b[0] * scale, y + b[1] * scale), text, f_dim, off, vert)

    place("front", "FRONT VIEW (resting, 2 units)", 60, 190)
    place("side", "SIDE VIEW", 1170, 190)
    place("top", "TOP-DOWN VIEW (4 x 4 unit grated deck)", 2280, 190)
    place("three_quarter", "THREE-QUARTER (collapsed / resting)", 60, 1010)
    place("extended", "THREE-QUARTER (partly extended, 8 units)", 1170, 1010)
    place("full", f"FULL EXTENSION ({build.H_MAX / 0.25:g} units)", 2280, 1010, scale=1.0)
    d.text((60, 1800), "DEPLOY SEQUENCE (1.9 s): settle, outriggers swing out and lock, column rises to 2 units, platform locks, lights flash",
           fill=INK, font=head_f)
    for i, (t, p) in enumerate(seq):
        im = Image.open(p).convert("RGB")
        x = 60 + i * 555
        sheet.paste(im.resize((540, 405), Image.LANCZOS), (x, 1850))
        d.text((x + 10, 1860), f"t = {t:.2f} s", fill=INK, font=font(24, True))
    d.text((60, 2290), "Animations in the .glb: 'deploy' (played once on placement) and 'extend' (time in seconds = deck height in metres;"
           " the game scrubs it as the Lift raises and lowers one unit at a time).", fill=INK, font=font(24))
    sheet.save(out / "lift_review_sheet.png")
    print(f"rendered the review sheet into {out}")
