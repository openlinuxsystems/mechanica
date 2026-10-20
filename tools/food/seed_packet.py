"""The seed packet every crop's seeds come in: one kraft-paper pouch for all crops, told apart by its label.

The label is the crop's own icon on a cream card, over a band in the crop's colour, with a row of seed marks under it.
Z up, metres, origin at the middle of the packet's base, front -Y.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

import bmesh
from mathutils import Vector
from PIL import Image, ImageDraw

import food_lib as fl

W, D, H = 0.085, 0.034, 0.118          # packet width, depth (belly) and height to the top of the fold
KRAFT = "#B88C5A"


def mats():
    paper = fl.material("packet_paper", KRAFT, rough=0.85)
    nt = paper.node_tree
    p = nt.nodes["Principled BSDF"]
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 140.0
    noise.inputs["Detail"].default_value = 6.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.08
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    return {
        "paper": paper,
        "paper_dark": fl.material("packet_paper_fold", "#A87F50", rough=0.88),
        "tie": fl.material("packet_tie", "#9AA3AB", rough=0.35, metal=1.0),
        "tie_paper": fl.material("packet_tie_paper", "#E8E1CF", rough=0.8),
    }


def label_image(icon_path, color, out_path, size=512):
    """The front label: a cream card, the crop's picture, a band in its colour and three seed marks."""
    w, h = size, int(size * 1.18)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    card = (236, 228, 205, 255)
    d.rounded_rectangle((4, 4, w - 5, h - 5), radius=34, fill=card, outline=(120, 96, 64, 255), width=6)
    c = tuple(int(color.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4))
    band = int(h * 0.74)
    d.rounded_rectangle((4, band, w - 5, h - 5), radius=34, fill=(*c, 255), outline=(120, 96, 64, 255), width=6)
    d.rectangle((10, band, w - 11, band + 40), fill=(*c, 255))
    d.line((10, band, w - 11, band), fill=(120, 96, 64, 255), width=6)
    # seed marks: three small seeds, the language for "these are seeds"
    seed = (247, 238, 212, 255)
    cy = (band + h - 5) // 2
    for k in (-1, 0, 1):
        cx = w // 2 + k * 70
        d.ellipse((cx - 17, cy - 24, cx + 17, cy + 24), fill=seed, outline=(90, 70, 46, 255), width=4)
    icon = Image.open(icon_path).convert("RGBA").resize((int(w * 0.82), int(w * 0.82)), Image.LANCZOS)
    img.alpha_composite(icon, ((w - icon.width) // 2, int(h * 0.05)))
    img.save(out_path)
    return out_path


def _body_bm(seed):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=14, use_grid_fill=True)
    top = H * 0.80                                    # the body ends where the top is folded over
    for v in bm.verts:
        x, y, z = v.co                                # -0.5 .. 0.5
        t = z + 0.5                                   # 0 bottom .. 1 top of the body
        belly = math.sin(math.pi * min(1, t * 1.05)) ** 0.6
        pinch = 1 - 0.85 * max(0, (t - 0.72) / 0.28) ** 1.6     # flattens into the folded seal
        depth = D * (0.62 + 0.38 * belly) * pinch
        width = W * (1 - 0.04 * belly * (2 * abs(y)))
        xx = x * width * (1 - 0.06 * (abs(y) * 2) ** 2)
        yy = y * depth
        # crumples
        n = 0.0009 * math.sin(41 * x + 7 * seed) * math.sin(33 * z + 3) + 0.0006 * math.sin(77 * z + 13 * x)
        v.co = Vector((xx, yy + (n if y < 0 else -n), t * top))
    bmesh.ops.bevel(bm, geom=[e for e in bm.edges if e.calc_face_angle(0) > 1.2], offset=0.002, segments=2)
    return bm


def build(name, label_png, parent=None, location=(0, 0, 0), seed=1, m=None):
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", _body_bm(seed), [m["paper"]], root)
    bvh = fl.bvh_of(body)
    top = H * 0.80
    # the folded-over top: two layers of paper turned toward the front
    fold = bmesh.new()
    bmesh.ops.create_cube(fold, size=1.0)
    bmesh.ops.subdivide_edges(fold, edges=fold.edges[:], cuts=6, use_grid_fill=True)
    for v in fold.verts:
        x, y, z = v.co
        v.co = Vector((x * W * 0.99, y * 0.0045 - D * 0.1, top + (z + 0.5) * 0.024 - 0.004
                       + 0.0008 * math.sin(30 * x)))
    bmesh.ops.bevel(fold, geom=[e for e in fold.edges if e.calc_face_angle(0) > 1.2], offset=0.0015, segments=2)
    fl.from_bm(name + "_Fold", fold, [m["paper_dark"]], root)
    # the zinc tin tie across the fold, its ends bent back round the sides
    zt = top + 0.013
    yf = -D * 0.1 - 0.0024 - 0.0006
    pts = [(-W * 0.5 - 0.003, yf + 0.009, zt), (-W * 0.5 - 0.0025, yf + 0.002, zt), (-W * 0.47, yf, zt),
           (W * 0.47, yf, zt), (W * 0.5 + 0.0025, yf + 0.002, zt), (W * 0.5 + 0.003, yf + 0.009, zt)]
    norms = []
    for i in range(len(pts)):
        t = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
        norms.append(Vector((0, 0, 1)).cross(t))        # horizontal, so the strip stands on edge
    tie = fl.from_bm(name + "_Tie", fl.strip(pts, [0.0042] * len(pts), norms), [m["tie"]], root, smooth=False)
    fl.solidify(tie, 0.0007)
    # label: a grid laid on the front face
    lab = bmesh.new()
    uv = lab.loops.layers.uv.new("UVMap")
    nx, nz = 12, 14
    lw, lh = W * 0.80, W * 0.80 * 1.18
    z0 = 0.008
    grid = []
    for j in range(nz + 1):
        row = []
        for i in range(nx + 1):
            x = -lw / 2 + lw * i / nx
            z = z0 + lh * j / nz
            hit = bvh.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)))
            y = (hit[0].y if hit[0] is not None else -D / 2) - 0.0016
            row.append(lab.verts.new((x, y, z)))
        grid.append(row)
    for j in range(nz):
        for i in range(nx):
            f = lab.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            for lp, (u, v) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uv].uv = (u / nx, v / nz)
    lm = fl.image_material("packet_label_" + Path(label_png).stem, label_png, under="#ECE4CD")
    fl.from_bm(name + "_Label", lab, [lm], root)
    return root
