"""Review renders of the rocks (run through ``build_rocks.py --render OUT_DIR``).

Renders each model alone from a three-quarter orthographic view under a studio light on a neutral grey backdrop and
lays them out on one labelled sheet (name, size in metres, triangles). Whole deposits are captured in game
(tools/capture-rocks.sh). Cycles on the CPU; nothing is downloaded.
"""

from __future__ import annotations

import math
import os
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFont

SAMPLES = int(os.environ.get("ROCKS_SAMPLES", "48"))
INK = (28, 30, 34)

LABELS = {
    **{f"rock_{size}_{v}": f"Rock Block, {size} ({v})" for size in ("small", "medium", "large") for v in "ab"},
    **{f"rock_metal_{size}_{v}": f"Metal-rich Block, {size} ({v})" for size in ("small", "medium", "large") for v in "ab"},
    "stone_boulder": "Stone Boulder",
    "metal_boulder": "Metal-Rich Boulder",
    "stone_chunk": "Stone Chunk",
    "metal_chunk": "Metal Chunk",
    "stone_rod": "Stone Rod",
    "metal_rod": "Metal Rod",
}


def font(size, bold=False):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", "LiberationSans-Bold.ttf" if bold else "LiberationSans-Regular.ttf"):
        for root in ("/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/truetype/liberation"):
            p = Path(root) / name
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def plain_material(name, rgb, rough=0.9):
    mat = bpy.data.materials.new(name)
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    return mat


def setup_world():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    world = bpy.data.worlds.new("World")
    sc.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.42, 0.43, 0.45, 1)
    bg.inputs["Strength"].default_value = 0.45

    ground = bpy.data.meshes.new("Ground")
    s = 60
    ground.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    gob = bpy.data.objects.new("RenderOnlyGround", ground)
    sc.collection.objects.link(gob)
    ground.materials.append(plain_material("backdrop", (0.36, 0.365, 0.375)))

    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 2.4
    sun.angle = math.radians(4)
    sob = bpy.data.objects.new("RenderOnlySun", sun)
    sob.rotation_euler = (math.radians(42), 0, math.radians(-38))
    sc.collection.objects.link(sob)
    for name, loc, energy, size in (("Key", (-3, -4, 5), 300, 4), ("Fill", (5, -2, 2.5), 90, 5), ("Rim", (1, 5, 3), 200, 3)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.size = energy, size
        ob = bpy.data.objects.new("RenderOnly" + name, light)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(ob)
    return gob


def camera(name, loc, target, ortho=None, lens=50):
    cam = bpy.data.cameras.new(name)
    if ortho is not None:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens = lens
    ob = bpy.data.objects.new("RenderOnly" + name, cam)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
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


def bounds(ob):
    co = np.array([ob.matrix_world @ Vector(c) for c in ob.bound_box])
    return co.min(0), co.max(0)


def render_all(models, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = setup_world()
    obs = {k: ob for k, (ob, _) in models.items()}
    shots = {}
    view = Vector((1.0, -1.25, 0.95)).normalized()
    for key, ob in obs.items():
        for o in obs.values():
            o.hide_render = o is not ob
        ob.location = (0, 0, 0)
        bpy.context.view_layer.update()
        lo, hi = bounds(ob)
        ground.location.z = lo[2]
        centre = Vector(((lo + hi) / 2).tolist())
        diag = float(np.linalg.norm(hi - lo))
        cam = camera("Shot_" + key, centre + view * 10, centre, ortho=diag * 1.22)
        shots[key] = render(cam, out / f"{key}.png", 640, 640)

    title_f, head_f, small_f = font(40, True), font(26, True), font(20)
    cols, tile, gap = 6, 640, 30
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tile + (cols + 1) * gap, 150 + rows * (tile + 110)), (238, 238, 236))
    d = ImageDraw.Draw(sheet)
    d.text((gap, 30), "MECHANICA ROCKS - DEPOSIT BLOCKS, BOULDERS, CHUNKS AND RODS", fill=INK, font=title_f)
    d.text((gap, 86), "Review renders from tools/rocks/build_rocks.py (three-quarter orthographic views, each framed to "
           "its own size). Sizes are the glTF bounds in metres (x, y up, z).", fill=INK, font=small_f)
    for i, (key, path) in enumerate(shots.items()):
        x = gap + (i % cols) * (tile + gap)
        y = 150 + (i // cols) * (tile + 110)
        sheet.paste(Image.open(path).convert("RGB"), (x, y))
        d.rectangle([x, y, x + tile - 1, y + tile - 1], outline=(190, 190, 190), width=2)
        info = build.glb_info(models[key][1])
        sz = " x ".join(f"{v:.3f}" for v in info["size"])
        d.text((x, y + tile + 10), LABELS[key], fill=INK, font=head_f)
        d.text((x, y + tile + 46), f"{models[key][1].name}   {sz} m   {info['tris']} tris   {info['kib']:.0f} KiB",
               fill=INK, font=small_f)
    sheet.save(out / "rocks_review_sheet.png")

    print(f"rendered the review sheet into {out}")
