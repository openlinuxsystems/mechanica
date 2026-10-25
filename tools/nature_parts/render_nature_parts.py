"""Review renders of the nature props (build_nature_parts.py --render OUT_DIR), sized for a phone: for each part
<id>_review.jpg (1600 px wide) with the three-quarter view from the front right and the view from the back left, a
25 cm block beside the part in both, and the icon on dark and on light at 256 and 64 px; then lineup.jpg with the eight
beside the block. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import itertools
from pathlib import Path

import bpy
import render_lift as rl
from mathutils import Vector
from PIL import Image, ImageDraw

import decor_kit as D

BG = (246, 246, 244)
VIEW_W, VIEW_H = 780, 620
BACK_VIEW = (-1.3, 2.1, 0.95)


def show_only(roots, keep):
    for pid, root in roots.items():
        for ob in [root, *root.children_recursive]:
            ob.hide_render = pid not in keep


def scale_block():
    """A plain 25 cm block, render only."""
    me = bpy.data.meshes.new("ScaleBlock")
    s = 0.125
    verts = [(sx * s, sy * s, sz * s + s) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    me.from_pydata(verts, [], faces)
    me.validate()
    mat = bpy.data.materials.new("scale_block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.72, 0.62, 0.48, 1)
    me.materials.append(mat)
    block = bpy.data.objects.new("RenderOnlyScaleBlock", me)
    bpy.context.scene.collection.objects.link(block)
    return block


def framing(points, view):
    """The target and ortho scale that frame `points` seen along -`view`."""
    v = Vector(view).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(v)
    us = [p.dot(side) for p in points]
    vs = [p.dot(up) for p in points]
    ws = [p.dot(v) for p in points]
    target = side * (min(us) + max(us)) / 2 + up * (min(vs) + max(vs)) / 2 + v * (min(ws) + max(ws)) / 2
    aspect = VIEW_W / VIEW_H
    return target, 1.12 * max(max(us) - min(us), (max(vs) - min(vs)) * aspect)


def wrap(d, text, f, width):
    lines, cur = [], ""
    for word in text.split():
        nxt = f"{cur} {word}".strip()
        if d.textlength(nxt, font=f) > width and cur:
            lines.append(cur)
            cur = word
        else:
            cur = nxt
    return lines + ([cur] if cur else [])


def render_part(pid, part, out, block, ground):
    hx, hy, hz = part.half
    ground.location.z = -hz
    block.location = (hx + 0.22, 0, -hz)
    corners = [Vector((sx * hx, sy * hy, sz * hz)) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    corners += [Vector((hx + 0.22 + sx * 0.125, sy * 0.125, -hz + 0.125 + sz * 0.125)) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    shots = {}
    for view, d in (("three_quarter", D.ICON_VIEW), ("back", BACK_VIEW)):
        target, ortho = framing(corners, d)
        cam = rl.camera(f"{pid}_{view}", tuple(target + Vector(d).normalized() * 8), tuple(target), ortho=ortho)
        shots[view] = rl.render(cam, out / "frames" / f"{pid}_{view}.png", VIEW_W, VIEW_H)
        bpy.data.objects.remove(cam)
    return shots


def sheet(pid, part, info, shots, out):
    F, INK = rl.font, rl.INK
    name = part.name or pid.replace("_", " ").title()
    x, y, z = part.size
    head = Image.new("RGB", (1600, 10), BG)
    d = ImageDraw.Draw(head)
    note = f"{x} x {y} x {z} blocks ({x * 25} x {y * 25} x {z * 25} cm), {info['tris']} triangles, {info['kib']:.0f} KiB. {part.note}"
    lines = wrap(d, note, F(24), 1530)
    top = 96 + 32 * len(lines)
    height = top + VIEW_H + 24 + 40 + 256 + 30
    im = Image.new("RGB", (1600, height), BG)
    d = ImageDraw.Draw(im)
    d.text((34, 22), f"{name}  ({pid})", fill=INK, font=F(46, True))
    for i, t in enumerate(lines):
        d.text((34, 88 + 32 * i), t, fill=INK, font=F(24))
    for i, view in enumerate(("three_quarter", "back")):
        px = 13 + i * (VIEW_W + 14)
        im.paste(Image.open(shots[view]).convert("RGB"), (px, top))
        d.text((px + 14, top + 10), {"three_quarter": "THREE-QUARTER (front right)", "back": "BACK (left)"}[view],
               fill=INK, font=F(24, True))
    d.text((13 + 14, top + VIEW_H - 36), "Block: 25 cm", fill=INK, font=F(22))
    y0 = top + VIEW_H + 24
    d.text((34, y0), "ICON", fill=INK, font=F(26, True))
    icon = Image.open(D.ICONS / f"{pid}_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        im.paste(tile, (34 + i * 290, y0 + 40))
        small = Image.new("RGB", (64, 64), bg)
        ic = icon.resize((64, 64), Image.LANCZOS)
        small.paste(ic, (0, 0), ic)
        im.paste(small, (640 + i * 100, y0 + 40))
    d.text((640, y0 + 116), "64 px", fill=INK, font=F(20))
    p = out / f"{pid}_review.jpg"
    im.save(p, quality=90)
    return p


# Where each part stands in the lineup (Blender x, y of its centre): rocks on the left, the trunk and log at the back,
# the flora in front.
LINEUP = {
    "boulder": (-2.1, 1.0), "large_rock": (-1.05, 0.9), "small_rock": (-0.55, 0.0), "tree_trunk": (0.25, 1.6),
    "fallen_log": (1.55, 1.3), "bush": (0.6, -0.1), "grass_tuft": (-1.25, -0.2), "flower_patch": (1.55, -0.3),
}
BLOCK_AT = (-0.15, -0.35)


def lineup(registry, roots, out, block, ground):
    """The eight on the floor, with the 25 cm block."""
    ground.location.z = 0
    for pid, root in roots.items():
        x, y = LINEUP[pid]
        root.location = (x, y, registry[pid].half[2])
    block.location = (*BLOCK_AT, 0)
    show_only(roots, set(roots))
    cam = rl.camera("lineup", (0.0, -5.6, 3.4), (-0.15, 0.6, 0.45), lens=32)
    w, h = 1600, 900
    p = rl.render(cam, out / "frames" / "lineup.png", w, h)
    im = Image.open(p).convert("RGB")
    d = ImageDraw.Draw(im)
    f = rl.font(24, True)
    labels = [(registry[pid].name or pid, LINEUP[pid], registry[pid].half) for pid in roots]
    for label, (x, y), (hx, hy, _) in labels + [("25 cm block", BLOCK_AT, (0.125, 0.125, 0.125))]:
        a = rl.project(cam, (x, y - hy - 0.06, 0), w, h)
        d.text((a[0] - d.textlength(label, font=f) / 2, a[1] + 6), label, fill=rl.INK, font=f)
    q = out / "lineup.jpg"
    im.save(q, quality=90)
    for root in roots.values():
        root.location = (0, 0, 0)
    return q


def render_all(registry, roots, infos, out: Path):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    block = scale_block()
    for pid, root in roots.items():
        show_only(roots, {pid})
        shots = render_part(pid, registry[pid], out, block, ground)
        print(f"rendered {sheet(pid, registry[pid], infos[pid], shots, out)}")
    if len(roots) > 1:
        print(f"rendered {lineup(registry, roots, out, block, ground)}")
    show_only(roots, set(roots))
    bpy.data.objects.remove(block)
    ground.hide_render = True
