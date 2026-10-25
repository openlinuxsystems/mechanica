"""Review sheets for the D2 logic and sound parts (run through ``tools/logic/build_<part>.py --render OUT_DIR``).

One 1600 px JPEG per part, ``OUT_DIR/<part>_sheet.jpg``: the three-quarter view from the front right, the back, the
part beside a 0.25 m cube for scale, the icon on dark and light, and the part's states (lit, counting, modes) as the
game's clips pose them. The single frames go to ``OUT_DIR/frames``. Cycles on the CPU (``KIT_SAMPLES``, default 40);
nothing is downloaded.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lift"))
import render_lift as rl  # noqa: E402  (stage, cameras, fonts)

SAMPLES = int(os.environ.get("KIT_SAMPLES", "40"))
WIDTH = 1600
BG = (246, 246, 244)


def bounds(root):
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for ob in [root, *root.children_recursive]:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            p = ob.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def scale_cube(at):
    """A plain grey 0.25 m cube standing on the ground at `at` (its bottom centre), for scale."""
    import bmesh  # noqa: PLC0415
    me = bpy.data.meshes.new("ScaleCube")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("RenderOnlyScaleCube", me)
    ob.location = Vector(at) + Vector((0, 0, 0.125))
    mat = bpy.data.materials.new("scale_cube")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.55, 0.56, 0.58, 1)
    b.inputs["Roughness"].default_value = 0.7
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def shoot(name, cam, out, w, h):
    p = out / "frames" / f"{name}.png"
    p.parent.mkdir(parents=True, exist_ok=True)
    rl.render(cam, p, w, h)
    bpy.data.objects.remove(cam)
    return Image.open(p).convert("RGB")


def review(part, title, lines, rig, icon_path, info, out: Path, half):
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = SAMPLES
    states = {k: v for k, v in rig["states"].items() if k != "icon"}
    first = next(iter(states.values()))
    first()
    bpy.context.view_layer.update()
    lo, hi = bounds(root)
    ground.location.z = lo.z - 0.0005
    size = max(half) * 2
    target = Vector((0, 0, lo.z + (hi.z - lo.z) * 0.45))
    # the root is turned half a turn, so the part's front looks along Blender's -y
    front = Vector((1.0, -1.25, 0.75)).normalized()
    back = Vector((-1.0, 1.25, 0.75)).normalized()
    shots = {
        "front": shoot(f"{part}_front", rl.camera("Q", target + front * size * 3.3, target, lens=50), out, 800, 640),
        "back": shoot(f"{part}_back", rl.camera("B", target + back * size * 3.3, target, lens=50), out, 800, 640),
    }
    cube = scale_cube((lo.x - 0.1 - 0.125, 0, lo.z))
    mid = Vector(((lo.x - 0.35) / 2 + 0.02, 0, lo.z + max(hi.z - lo.z, 0.25) * 0.45))
    span = max(hi.x - lo.x + 0.45, hi.z - lo.z + 0.1)
    # orthographic, so the cube and the part are drawn at the same scale
    cam = rl.camera("S", mid + Vector((0.25, -1.0, 0.45)).normalized() * 4.0, mid, ortho=span * 1.35)
    shots["scale"] = shoot(f"{part}_scale", cam, out, 800, 520)
    bpy.data.objects.remove(cube)
    tiles = []
    for name, apply in states.items():
        apply()
        bpy.context.view_layer.update()
        cam = rl.camera("T", target + front * size * 2.6, target, lens=50)
        tiles.append((name, shoot(f"{part}_state_{name.replace(' ', '_').replace(',', '')}", cam, out, 560, 480)))
    first()
    return sheet(part, title, lines, shots, tiles, icon_path, info, out)


def sheet(part, title, lines, shots, tiles, icon_path, info, out):
    per_row = min(4, max(1, len(tiles)))
    tw = WIDTH // per_row
    th = int(tw * 480 / 560)
    rows = (len(tiles) + per_row - 1) // per_row
    head = 80 + 34 * len(lines)
    label = 40
    height = head + (640 + label) + (520 + label) + rows * (th + label) + 70
    im = Image.new("RGB", (WIDTH, height), BG)
    d = ImageDraw.Draw(im)
    d.text((30, 22), title, fill=rl.INK, font=rl.font(42, True))
    for i, t in enumerate(lines):
        d.text((30, 76 + 34 * i), t, fill=rl.INK, font=rl.font(23))
    y = head
    for x, (key, text) in zip((0, 800), (("front", "THREE-QUARTER (front right)"), ("back", "BACK (three-quarter)"))):
        d.text((x + 16, y + 8), text, fill=rl.INK, font=rl.font(24, True))
        im.paste(shots[key], (x, y + label))
    y += 640 + label
    d.text((16, y + 8), "SCALE: beside a 0.25 m cube (one block)", fill=rl.INK, font=rl.font(24, True))
    im.paste(shots["scale"], (0, y + label))
    d.text((816, y + 8), "ICON (hotbar, inventory, Fabricator)", fill=rl.INK, font=rl.font(24, True))
    icon = Image.open(icon_path).convert("RGBA").resize((340, 340), Image.LANCZOS)
    for k, (bg, name) in enumerate((((27, 31, 36), "on dark"), ((230, 232, 235), "on light"))):
        x = 830 + k * 380
        tile = Image.new("RGBA", (360, 400), (*bg, 255))
        tile.alpha_composite(icon, (10, 10))
        ImageDraw.Draw(tile).text((14, 360), name, fill=(230, 232, 235) if k == 0 else rl.INK, font=rl.font(22, True))
        im.paste(tile.convert("RGB"), (x, y + label + 50))
    y += 520 + label
    for i, (name, tile) in enumerate(tiles):
        r, c = divmod(i, per_row)
        x, yy = c * tw, y + r * (th + label)
        d.text((x + 14, yy + 8), name.upper(), fill=rl.INK, font=rl.font(22, True))
        im.paste(tile.resize((tw, th), Image.LANCZOS), (x, yy + label))
    y += rows * (th + label)
    lo, hi = info["min"], info["max"]
    sz = hi - lo
    d.text((30, y + 18), f"{info['tris']} triangles, {info['kib']:.0f} KiB, {sz[0]:.3f} x {sz[1]:.3f} x {sz[2]:.3f} m (glTF x, y, z)"
           + (f"; clips {', '.join(info['clips'])}" if info["clips"] else ""), fill=rl.INK, font=rl.font(21))
    path = out / f"{part}_sheet.jpg"
    im.save(path, quality=90)
    print(f"rendered {path}")
    return path
