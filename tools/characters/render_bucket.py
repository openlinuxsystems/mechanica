"""Review renders of the water bucket, laid out like its reference sheets (front three-quarter, side and top views of the
empty and the full bucket). Renders the committed item models, so run ``build_characters.py items`` first.

    python tools/characters/render_bucket.py OUT_DIR

Cycles on the CPU; nothing is downloaded. Writes one PNG per view and ``water_bucket_sheet.png``.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[2]
ITEMS = REPO / "mods" / "vanilla" / "meshes" / "items"
SAMPLES = int(os.environ.get("BUCKET_SAMPLES", "64"))
VIEWS = {
    # name: (camera direction from the bucket, elevation degrees, label)
    "front3q": (35.0, 18.0, "FRONT THREE-QUARTER"),
    "side": (90.0, 4.0, "SIDE VIEW"),
    "top": (0.0, 89.0, "TOP VIEW"),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x = 900
    sc.render.resolution_y = 900
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.95, 0.95, 0.96, 1)
    bg.inputs["Strength"].default_value = 0.35
    ground = bpy.data.meshes.new("Ground")
    s = 10
    ground.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    gob = bpy.data.objects.new("Ground", ground)
    sc.collection.objects.link(gob)
    mat = bpy.data.materials.new("ground")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.92, 0.92, 0.93, 1)
    ground.materials.append(mat)
    for name, loc, energy, size in (("key", (-1.2, -1.6, 2.0), 120, 1.5), ("fill", (1.8, -0.6, 1.0), 40, 2.0),
                                    ("rim", (0.6, 1.8, 1.6), 70, 1.2)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = size
        ob = bpy.data.objects.new(name, light)
        ob.location = loc
        sc.collection.objects.link(ob)
        direction = Vector((0, 0, 0.15)) - ob.location
        ob.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def render(model, view, out):
    reset()
    bpy.ops.import_scene.gltf(filepath=str(ITEMS / f"{model}.glb"))
    azimuth, elevation, _ = VIEWS[view]
    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 0.62
    ob = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    target = Vector((0, 0, 0.2 if view != "top" else 0.0))
    a, e = math.radians(azimuth), math.radians(elevation)
    # the bucket's front faces -Y; azimuth turns the camera from the front towards +X
    ob.location = target + 3.0 * Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    ob.rotation_euler = (target - ob.location).to_track_quat("-Z", "Y").to_euler()
    if view == "top":
        ob.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)


def sheet(out_dir):
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    big = ImageFont.truetype(font_path, 34) if Path(font_path).exists() else ImageFont.load_default()
    small = ImageFont.truetype(font_path, 22) if Path(font_path).exists() else ImageFont.load_default()
    cell = 600
    img = Image.new("RGB", (cell * 3, cell * 2 + 140), (244, 244, 246))
    d = ImageDraw.Draw(img)
    for row, (model, title) in enumerate((("water_bucket", "WATER BUCKET - EMPTY"), ("water_bucket_full", "WATER BUCKET - FULL"))):
        y0 = 70 + row * (cell + 70) - (70 if row else 0)
        d.text((20, y0 - 50 if row == 0 else y0 - 4), title, fill=(40, 40, 44), font=big)
        for col, view in enumerate(VIEWS):
            tile = Image.open(out_dir / f"{model}_{view}.png").convert("RGB").resize((cell, cell))
            img.paste(tile, (col * cell, y0 + (40 if row else 0)))
            d.text((col * cell + 20, y0 + (40 if row else 0) + cell - 40), VIEWS[view][2], fill=(70, 70, 76), font=small)
    img.save(out_dir / "water_bucket_sheet.png")


def main(out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    for model in ("water_bucket", "water_bucket_full"):
        for view in VIEWS:
            render(model, view, out_dir / f"{model}_{view}.png")
    sheet(out_dir)


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
