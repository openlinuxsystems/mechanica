"""The parts map's batch 5 review pictures for the whole set of lights (see light_kit.py).

    python3 tools/part_kit/light_review.py OUT_DIR

Run it after the part scripts' ``--render OUT_DIR`` (it reads their frames). It writes:

* ``OUT_DIR/batch5_dusk.png`` - every light switched on at dusk: the hanging ones on a gantry, the signs on a wall, the
  rest on the ground, each shining with its own colour and cone;
* ``OUT_DIR/batch5_lineup.png`` - every light's three-quarter view, off beside on, in one sheet.

Cycles on the CPU (``KIT_SAMPLES``, default 64 here); nothing is downloaded.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import light_kit as L  # noqa: E402
import render_kit as rk  # noqa: E402

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

rl = rk.rl
PARTS = [
    ("headlight", "Headlight"), ("spotlight", "Spotlight"), ("warehouse_spotlight", "Warehouse Spotlight"),
    ("interior_light", "Interior Light"), ("construction_light", "Construction Light"), ("warning_light", "Warning Light"),
    ("frame_beam_light", "Frame Beam Light (new)"), ("fluorescent_light", "Fluorescent Light (new)"),
    ("packing_lamp", "Packing Lamp (new)"), ("protector_sign", "Protector Sign (new)"), ("neon_sign", "Neon Sign (new)"),
    ("shack_light", "Shack Light (new)"), ("glowstick", "Glowstick"),
]

# Where each light stands in the dusk scene (Blender axes: the lights face +y, towards the camera), and its turn (deg).
GANTRY = 2.4
WALL = -1.2
LAYOUT = {
    "interior_light": ((-2.6, 0.2, GANTRY - 0.125), 0),
    "shack_light": ((-1.2, 0.2, GANTRY - 0.25), 0),
    "fluorescent_light": ((0.4, 0.2, GANTRY - 0.125), 0),
    "frame_beam_light": ((2.2, 0.2, GANTRY - 0.125), 0),
    "neon_sign": ((-1.6, WALL + 0.125, 1.45), 0),
    "protector_sign": ((1.0, WALL + 0.125, 1.35), 0),
    "warehouse_spotlight": ((-3.1, -0.5, 0.375), 25),
    "spotlight": ((3.1, -0.3, 0.25), -30),
    "construction_light": ((-2.0, -0.4, 0.5), 15),
    "warning_light": ((2.2, -0.6, 0.25), 0),
    "packing_lamp": ((-0.2, -0.3, 0.125), 0),
    "headlight": ((1.0, -0.65, 0.375), -10),
    "glowstick": ((0.6, 0.6, 0.02), 70),
}


def mat(name, color, rough=0.85, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*[c ** 2.2 for c in K.lt.srgb(color)], 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def slab(name, center, half, material):
    g = K.Geo()
    K.box(g, center, half, "m")
    return K.make_object(name, g, {"m": material}, None)


def dusk(out: Path):
    K.reset_scene()
    mats = L.materials()
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("KIT_SAMPLES", "64"))
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("Dusk")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.035, 0.055, 0.13, 1)
    bg.inputs["Strength"].default_value = 0.9
    sun = bpy.data.lights.new("Dusk", "SUN")
    sun.energy = 0.25
    sun.color = (0.55, 0.62, 0.95)
    sob = bpy.data.objects.new("Dusk", sun)
    sob.rotation_euler = (math.radians(75), 0, math.radians(150))
    sc.collection.objects.link(sob)
    # the yard: a concrete slab, a back wall, a gantry beam on two posts, a block under the headlight
    slab("Ground", (0, 0, -0.05), (6, 5, 0.05), mat("concrete", "#6f716d"))
    slab("Wall", (0, WALL - 0.1, 1.6), (4.2, 0.1, 1.6), mat("wall", "#8a8579"))
    steel = mat("gantry", "#3e6a8c", 0.5, 0.4)
    slab("Gantry", (0, 0.2, GANTRY + 0.1), (3.6, 0.15, 0.1), steel)
    for x in (-3.55, 3.55):
        slab(f"Post{x}", (x, 0.2, GANTRY / 2), (0.08, 0.08, GANTRY / 2 + 0.1), steel)
    slab("Crate", (1.0, -0.65, 0.125), (0.2, 0.2, 0.125), mat("crate", "#9c7650"))
    for part, (at, turn) in LAYOUT.items():
        rig = getattr(L, f"build_{part}")(mats)
        rig["root"].location = at
        rig["root"].rotation_euler = (0, 0, math.radians(turn))
        L.glow_pose(rig)(1.0)
        L.add_lamps(rig, rig["lamps"], scale=2.5)
    cam = rl.camera("Dusk", Vector((0.4, 6.6, 2.1)), Vector((0.1, -0.3, 1.0)), lens=30)
    path = out / "batch5_dusk.png"
    rl.render(cam, path, 1600, 900)
    print(f"rendered {path}")


def lineup(out: Path):
    cell_w, cell_h, head = 300, 266, 40
    cols = 3
    rows = math.ceil(len(PARTS) / cols)
    pair = 2 * cell_w
    im = Image.new("RGB", (cols * (pair + 30) + 30, 90 + rows * (cell_h + head + 20)), (246, 246, 244))
    d = ImageDraw.Draw(im)
    d.text((30, 26), "PARTS BATCH 5 LINEUP: EVERY LIGHT, OFF AND ON", fill=rl.INK, font=rl.font(36, True))
    for i, (part, name) in enumerate(PARTS):
        x = 30 + (i % cols) * (pair + 30)
        y = 90 + (i // cols) * (cell_h + head + 20)
        d.text((x, y + 6), name, fill=rl.INK, font=rl.font(22, True))
        for k, state in enumerate(("off", "on")):
            f = out / "frames" / f"{part}_{state}_three_quarter.png"
            pic = Image.open(f).convert("RGB")
            pic = pic.resize((cell_w, int(pic.height * cell_w / pic.width)), Image.LANCZOS)
            im.paste(pic.crop((0, 0, cell_w, cell_h)), (x + k * cell_w, y + head))
            d.text((x + k * cell_w + 8, y + head + 6), state.upper(), fill=rl.INK, font=rl.font(18, True))
    path = out / "batch5_lineup.png"
    im.save(path)
    print(f"rendered {path}")


if __name__ == "__main__":
    target = Path(sys.argv[1])
    target.mkdir(parents=True, exist_ok=True)
    lineup(target)
    dusk(target)
