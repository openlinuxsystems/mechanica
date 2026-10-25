"""Review renders of the batch D5 site signs (build_site_signs.py --review OUT_DIR): per part a 1600 px JPEG sheet
with three-quarter views from the front right and the back left beside a 0.25 m cube, and the icon on dark and on
light; then a lineup of the four with a 1.5 m figure and the cube. Cycles on the CPU; KIT_SAMPLES sets the samples.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lift"))
import render_lift as rl  # noqa: E402

import building_kit as B  # noqa: E402
import part_kit as K  # noqa: E402

SAMPLES = int(os.environ.get("KIT_SAMPLES", "48"))
VW, VH = 760, 760
SHEET_W = 1600
CUBE = 0.25


def _cube(x, y, z0):
    """A grey 0.25 m reference cube standing on z0."""
    me = bpy.data.meshes.new("ScaleCube")
    h = CUBE / 2
    v = [(x + sx * h, y + sy * h, z0 + (sz + 1) * h) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    me.from_pydata(v, [], f)
    ob = bpy.data.objects.new("RenderOnlyScaleCube", me)
    mat = bpy.data.materials.new("scale_cube")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.32, 0.33, 0.35, 1)
    b.inputs["Roughness"].default_value = 0.7
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def _roots():
    return [o for o in bpy.context.scene.objects if o.parent is None and o.type == "EMPTY"]


def _stage():
    ground = rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    sc.cycles.seed = 3
    return ground


def _label(im, text, sub=None):
    d = ImageDraw.Draw(im)
    d.text((16, 12), text, fill=rl.INK, font=rl.font(26, True))
    if sub:
        d.text((16, 46), sub, fill=rl.INK, font=rl.font(20))
    return im


def sheet(part, title, lines, half, out: Path):
    """The part (already built at the origin by building_kit.run) from the front right and back left with the cube."""
    out.mkdir(parents=True, exist_ok=True)
    frames = out / "frames"
    frames.mkdir(exist_ok=True)
    ground = _stage()
    lo, hi = K.world_bounds(_roots()[0])     # glTF axes: y up
    zmin = lo[1]
    ground.location.z = zmin - 0.0005
    cube_x = -(half[0] + 0.22)                # to the part's right as you face it
    cube = _cube(cube_x, 0.0, zmin)
    span = max(half[0] * 2 + 0.5, half[2] * 2)
    target = Vector((cube_x / 2 + 0.0, 0, zmin + half[2] * 0.95))
    dist = span * (2.3 if half[2] >= half[0] else 2.0)     # a wide part (the Barrier Tape) closer in
    views = {
        "front": rl.camera("Q", target + Vector((-0.75, 1.25, 0.55)).normalized() * dist, target, lens=50),
        "back": rl.camera("B", target + Vector((0.6, -1.25, 0.55)).normalized() * dist, target, lens=50),
    }
    shots = {}
    for v, cam in views.items():
        shots[v] = rl.render(cam, frames / f"d5_{part}_{v}.png", VW, VH)
        bpy.data.objects.remove(cam)
    bpy.data.objects.remove(cube)

    head = 100 + 34 * (len(lines) + 1)
    icon_px = 360
    h = head + VH + 60 + icon_px + 80
    im = Image.new("RGB", (SHEET_W, h), (246, 246, 244))
    d = ImageDraw.Draw(im)
    d.text((40, 24), title, fill=rl.INK, font=rl.font(42, True))
    for i, t in enumerate(lines + ["Grey cube: 0.25 m (one block)."]):
        d.text((40, 84 + 34 * i), t, fill=rl.INK, font=rl.font(24))
    x = 40
    for v, name in (("front", "THREE-QUARTER (front right)"), ("back", "BACK (three-quarter, back left)")):
        fr = _label(Image.open(shots[v]).convert("RGB"), name)
        im.paste(fr, (x, head))
        x += VW + 0
    y = head + VH + 30
    d.text((40, y), "ICON on dark and on light (hotbar, inventory, Fabricator)", fill=rl.INK, font=rl.font(26, True))
    icon = Image.open(K.ICONS / f"{part}_icon.png").convert("RGBA").resize((icon_px - 40, icon_px - 40), Image.LANCZOS)
    for i, bg in enumerate(((30, 33, 38), (225, 228, 232))):
        tile = Image.new("RGBA", (icon_px, icon_px), bg + (255,))
        tile.alpha_composite(icon, (20, 20))
        im.paste(tile.convert("RGB"), (40 + i * (icon_px + 30), y + 44))
    path = out / f"d5_{part}_sheet.jpg"
    im.save(path, quality=90)
    print(f"rendered {path}")
    return path


def lineup(parts, out: Path):
    """All the parts in a row on the ground with a 1.5 m figure and the cube."""
    K.reset_scene()
    mats = B.materials()
    ground = _stage()
    ground.location.z = -0.0005
    # Blender +x is the viewer's left from the front: the figure and cube on the right, then the parts leftward
    rl.silhouette(-0.75, 0.1)
    _cube(-0.25, 0.0, 0.0)
    x = 0.05
    for part, build, half, *_ in reversed(parts):
        x += half[0] + 0.2
        root = build(mats)
        root.location = (x, 0, half[2])
        x += half[0] + 0.2
    target = Vector(((x - 0.75) / 2, 0, 0.62))
    cam = rl.camera("L", target + Vector((0.12, 1.0, 0.16)).normalized() * 11.0, target, lens=50)
    p = rl.render(cam, out / "frames" / "d5_lineup.png", 1600, 800)
    im = Image.open(p).convert("RGB")
    d = ImageDraw.Draw(im)
    d.text((24, 18), "Batch D5 signs and site: Workshop Sign, Warehouse Key Sign, Speed Limit Sign, Barrier Tape", fill=rl.INK,
           font=rl.font(28, True))
    d.text((24, 56), "Figure 1.5 m, cube 0.25 m", fill=rl.INK, font=rl.font(22))
    path = out / "d5_lineup.jpg"
    im.save(path, quality=90)
    print(f"rendered {path}")
    return path
