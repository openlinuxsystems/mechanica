"""Review renders of the trees, logs and rod (run through ``build_trees.py --render OUT_DIR``).

Renders each model alone from a three-quarter view on a neutral backdrop, lays them out on one labelled sheet, and
renders a small forest (every tree, a few far versions behind, logs and rods lying about) under a sky. Cycles on the
CPU; nothing is downloaded.
"""

from __future__ import annotations

import math
import os
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFont

import build_trees as bt

SAMPLES = int(os.environ.get("TREES_SAMPLES", "32"))
INK = (28, 30, 34)


def font(size, bold=False):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",):
        p = Path("/usr/share/fonts/truetype/dejavu") / name
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def plain(name, hex_colour, rough=0.95):
    mat = bpy.data.materials.new(name)
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*bt.srgb(hex_colour), 1)
    b.inputs["Roughness"].default_value = rough
    return mat


def setup(sky):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("World")
    sc.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (*bt.srgb("#8cc4f0" if sky else "#9a9ca0"), 1)
    bg.inputs["Strength"].default_value = 0.9 if sky else 0.6
    me = bpy.data.meshes.new("Ground")
    s = 200
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    me.materials.append(plain("ground", "#5f9a34" if sky else "#8a8c90"))
    ground = bpy.data.objects.new("Ground", me)
    sc.collection.objects.link(ground)
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2
    sun.angle = math.radians(3)
    ob = bpy.data.objects.new("Sun", sun)
    ob.rotation_euler = (math.radians(48), 0, math.radians(-35))
    sc.collection.objects.link(ob)
    return ground


def camera(loc, target, lens=50, ortho=None):
    cam = bpy.data.cameras.new("Camera")
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens = lens
    cam.clip_end = 1000
    ob = bpy.data.objects.new("Camera", cam)
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    return ob


def shoot(path, w, h):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def render(out: Path, built):
    out.mkdir(parents=True, exist_ok=True)
    ground = setup(sky=False)
    obs = {k: ob for k, (ob, _) in built.items()}
    shots = {}
    view = Vector((1.0, -1.3, 0.55)).normalized()
    for key, ob in obs.items():
        for o in obs.values():
            o.hide_render = o is not ob
        bpy.context.view_layer.update()
        co = np.array([ob.matrix_world @ Vector(c) for c in ob.bound_box])
        lo, hi = co.min(0), co.max(0)
        ground.location.z = lo[2]
        centre = Vector(((lo + hi) / 2).tolist())
        size = float(np.max(hi - lo))
        camera(centre + view * size * 3, centre, ortho=size * 1.25)
        shots[key] = out / f"{key}.png"
        shoot(shots[key], 480, 480)
    cols, tile, gap = 4, 480, 24
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tile + (cols + 1) * gap, 110 + rows * (tile + 80)), (238, 238, 236))
    d = ImageDraw.Draw(sheet)
    d.text((gap, 24), "MECHANICA TREES - TREES, FAR VERSIONS, LOGS AND THE WOOD ROD", fill=INK, font=font(34, True))
    for i, (key, path) in enumerate(shots.items()):
        x = gap + (i % cols) * (tile + gap)
        y = 90 + (i // cols) * (tile + 80)
        sheet.paste(Image.open(path).convert("RGB"), (x, y))
        tris, lo, hi = bt.glb_info(built[key][1])
        size = hi - lo
        d.text((x, y + tile + 8), key, fill=INK, font=font(24, True))
        d.text((x, y + tile + 40), f"{size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, {tris} tris", fill=INK, font=font(18))
    sheet.save(out / "trees_review_sheet.png")

    # ---- a small forest ---------------------------------------------------------------------------
    for o in obs.values():
        o.hide_render = True
    ground.data.materials[0] = plain("grass", "#5f9a34")
    bpy.context.scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (*bt.srgb("#8cc4f0"), 1)
    rng = np.random.default_rng(5)

    def put(key, loc, rz=0.0, rx=0.0, scale=1.0):
        ob = obs[key].copy()
        bpy.context.scene.collection.objects.link(ob)
        ob.location = loc
        ob.rotation_euler = (rx, 0, rz)
        ob.scale = (scale, scale, scale)
        ob.hide_render = False

    put("birch_tree", (-6, 2, 0), 0.3)
    put("birch_tree", (-9, 6, 0), 2.0, scale=0.85)
    put("leafy_tree", (0, 6, 0), 1.0)
    put("spruce_tree", (6, 4, 0), 0.4)
    put("pine_tree", (12, 10, 0), 0.2)
    put("leafy_tree", (-3, 14, 0), 2.5, scale=1.15)
    for k in range(18):
        key = ["birch_tree_lod", "leafy_tree_lod", "spruce_tree_lod", "pine_tree_lod"][k % 4]
        put(key, (rng.uniform(-30, 34), rng.uniform(22, 60), 0), rng.uniform(0, 6.3), scale=rng.uniform(0.85, 1.2))
    put("wood_log", (1.5, -1.5, 0.24), 0.4)
    put("wood_log", (2.4, -0.6, 0.24), 0.9)
    put("pine_log", (5.5, -1.0, 0.35), -0.3)
    put("birch_log", (-4.0, -1.2, 0.12), 1.4)
    put("wood_rod", (0.2, -2.6, 0.13), 0.2)
    put("wood_rod", (0.8, -3.0, 0.13), 1.1)
    camera((2, -16, 4.0), (2, 6, 4.2), lens=30)
    shoot(out / "trees_forest.png", 1600, 900)
    print(f"rendered the review sheet and the forest into {out}")
