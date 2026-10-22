"""Build the crafting-material item models and icons of Parts batch 1 (PARTS_MAP.md, batch 1).

What it builds (procedurally, from scratch: no .blend inputs, no downloads), each a 1 x 1 x 1 block item (inside the
0.25 m cube centred on the origin, front -Y, Z up, standing on z = -0.125):

* ``meshes/items/glue.glb`` - a squat squeeze bottle of glue: bone plastic body with a dark wrap label (a drop
  pictogram and GL-2), a tapered shoulder, a gunmetal screw collar, a signal-red nozzle and a run of amber glue.
* ``meshes/items/warehouse_key.glb`` - a Colony Works keycard lying flat: bone card with a steel-blue grip end, a
  graphite grommet and a steel split ring, a copper contact chip, the WH key print (roll-up door pictogram, stripe,
  barcode).
* ``meshes/items/explosive_remnant.glb`` - what is left of a Red Scuttler's pressure tank: a dented red tank on its
  side, a mustard and graphite hazard band, a cracked gauge, a dead warning lamp, a valve and a torn mount bracket with
  a frayed copper wire.
* ``meshes/items/chemical.glb`` - a sealed chemical cell: a glass tube of glowing green chemical with bubbles, held
  between gunmetal caps by four rails, a copper valve on top and a hazard plate on the base.
* ``meshes/items/paint_ammo.glb`` - a paint tin (Scrap Mechanic's Paint Bucket): a zinc tin with rolled rims and a
  blue wrap label (a paint splat and PT-1), a lid, blue paint run over the rim, and a wire bail handle laid to one side.
* ``textures/items/<id>_icon.png`` for each, plus ``water_bucket_icon.png``, ``water_bucket_full_icon.png`` and
  ``chemical_bucket_icon.png`` drawn from the bucket models that already exist (tools/characters/build_characters.py).

    pip install bpy==5.2.2 numpy pillow
    python tools/materials/build_materials.py                    # the game files
    python tools/materials/build_materials.py --render OUT_DIR   # also review renders, a sheet per item and a lineup

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
VANILLA = REPO / "mods" / "vanilla"
MESHES = VANILLA / "meshes" / "items"
ICONS = VANILLA / "textures" / "items"

Geo, box, cylinder, dome = bl.Geo, bl.box, bl.cylinder, bl.dome
lathe = bbe.lathe

HALF = 0.125                  # the item's half size: one block
FLOOR = -HALF
ICON_VIEW = (1.25, -1.6, 1.45)

# Palettes (docs/art-direction.md)
HULL, GUNMETAL, CYAN, COPPER = "#24282E", "#5A6068", "#38C8FF", "#B8733A"
BONE, STEEL_BLUE, MUSTARD, GRAPHITE, SIGNAL_RED = "#D9D1BC", "#3E6A8C", "#E0B530", "#2B2F35", "#C8423A"
ZINC = "#9AA3AB"
SCUTTLER_RED = "#d0402e"       # the Red Scuttler's paint (tools/scuttler)
CHEMICAL_GREEN = "#7ee03c"     # chemical.json's colour and the chemical pools'
PAINT_BLUE = "#3874dc"         # paint_ammo.json's colour


# =====================================================================================================================
# Shared pieces
# =====================================================================================================================

def metal(name, seed, color, rough=0.46, metal_=0.75, wear=0.5):
    return lt.textured(name, gc.half_res(lt.metal_surface(seed, color, rough, metal_, wear=wear, edge_light=0.3)),
                       normal_strength=0.7)


def paint(name, seed, color, under=GUNMETAL):
    return lt.textured(name, gc.half_res(lt.paint_surface(seed, color, under)), normal_strength=0.8)


def font(size, bold=True):
    names = ("DejaVuSans-Bold.ttf", "DejaVuSans.ttf") if bold else ("DejaVuSans.ttf",)
    for n in names:
        p = Path("/usr/share/fonts/truetype/dejavu") / n
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def as_rgb(im):
    return np.asarray(im.convert("RGB"), float) / 255.0


def rgb255(hex_color):
    h = hex_color.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def wrap(g, center, axis, r, t0, t1, a0, a1, mat, segs=16, ref=None):
    """A decal band on a cylinder: the patch of the cylinder of radius ``r`` about ``axis`` through ``center`` from
    ``t0`` to ``t1`` along the axis and ``a0`` to ``a1`` radians around it, with fixed UVs (u round, v along)."""
    c, a = Vector(center), Vector(axis).normalized()
    u_dir = Vector(ref) if ref is not None else bl.frame_of(a)[0]
    u_dir = (u_dir - a * u_dir.dot(a)).normalized()
    w_dir = a.cross(u_dir)
    verts, faces, uvs = [], [], []
    for k in range(segs + 1):
        th = a0 + (a1 - a0) * k / segs
        d = u_dir * math.cos(th) + w_dir * math.sin(th)
        verts += [tuple(c + a * t0 + d * r), tuple(c + a * t1 + d * r)]
    for k in range(segs):
        i = 2 * k
        faces.append((i, i + 2, i + 3, i + 1))
        uvs.append(((k / segs, 0), ((k + 1) / segs, 0), ((k + 1) / segs, 1), (k / segs, 1)))
    base = len(g.v)
    g.v.extend(tuple(map(float, p)) for p in verts)
    for f, uv in zip(faces, uvs):
        g.f.append(tuple(base + q for q in f))
        g.m.append(mat)
        g.uv.append(uv)
    return g


def tube_x(g, x0, x1, r, mat, cz, rings=12, segs=32, y=0.0):
    """A cylinder along X built with ``rings`` rings (so it can be dented), uncapped. Returns its vertex range."""
    start = len(g.v)
    pts = [(x0 + (x1 - x0) * k / (rings - 1), y, cz) for k in range(rings)]
    bl.sweep(g, pts, r, mat, segs=segs)
    # sweep caps both ends; the caps are hidden under the end caps of the tank anyway
    return start, len(g.v)


def dent(g, rng, centre, radius, depth, axis_z):
    """Pushes the vertices of an X-axis tube (``rng``, about the line y = 0, z = ``axis_z``) within ``radius`` of
    ``centre`` in toward its axis, by up to ``depth``."""
    c = Vector(centre)
    for i in range(*rng):
        p = Vector(g.v[i])
        d = (p - c).length
        if d < radius:
            k = (1 - d / radius) ** 2
            inward = Vector((0, p.y, p.z - axis_z)).normalized()
            g.v[i] = tuple(p - inward * depth * k)


def triangles(root):
    dg = bpy.context.evaluated_depsgraph_get()
    n = 0
    for ob in bbe.mesh_tree(root):
        if ob.type == "MESH":
            me = ob.evaluated_get(dg).to_mesh()
            me.calc_loop_triangles()
            n += len(me.loop_triangles)
            ob.evaluated_get(dg).to_mesh_clear()
    return n


def bounds(objects):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objects:
        if o.type == "MESH" and not o.hide_render:
            ev = o.evaluated_get(dg)
            pts += [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def icon(objects, path, fill=1.22, view=ICON_VIEW):
    lo, hi = bounds(objects)
    c = (lo + hi) / 2
    # the ortho frame must hold the whole silhouette from the icon's view: project the box's corners
    v = Vector(view).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(v).normalized()
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    extent = max(max(abs((p - c).dot(side)), abs((p - c).dot(up))) for p in corners) * 2
    return bbe.render_icon(objects, path, view=view, ortho=extent * fill, target=tuple(c))


# =====================================================================================================================
# Glue
# =====================================================================================================================

def glue_label():
    w, h = 512, 448
    im = Image.new("RGB", (w, h), rgb255(HULL))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, 30], fill=rgb255(BONE))
    d.rectangle([0, h - 31, w - 1, h - 1], fill=rgb255(BONE))
    # a big amber drop
    cx, cy, r = w // 2, 205, 92
    d.ellipse([cx - r, cy - r + 30, cx + r, cy + r + 30], fill=(231, 169, 42))
    d.polygon([(cx, cy - 140), (cx + r - 6, cy + 10), (cx - r + 6, cy + 10)], fill=(231, 169, 42))
    d.ellipse([cx - 44, cy + 2, cx - 10, cy + 60], fill=(246, 214, 130))
    d.text((cx, h - 72), "GL-2", fill=rgb255(BONE), font=font(64), anchor="mm")
    return as_rgb(im)


def build_glue():
    mats = {
        "body": pt.plain("glue_body", BONE, 0.48, 0.0),
        "foot": pt.plain("glue_foot", GRAPHITE, 0.6, 0.1),
        "label": lt.decal("glue_label", glue_label(), rough=0.55),
        "band": pt.plain("glue_band", HULL, 0.55, 0.1),
        "collar": metal("glue_collar", 811, GUNMETAL),
        "nozzle": pt.plain("glue_nozzle", SIGNAL_RED, 0.4, 0.0),
        "glue": pt.plain("glue_glue", "#D08A10", 0.12, 0.0),
    }
    g = Geo()
    hx, hy = 0.056, 0.044
    z0, z1 = FLOOR + 0.008, FLOOR + 0.142
    box(g, (0, 0, FLOOR + 0.0045), (hx - 0.004, hy - 0.004, 0.0045), "foot", bevel=0.003)
    box(g, (0, 0, (z0 + z1) / 2), (hx, hy, (z1 - z0) / 2), "body", bevel=0.012)
    # the wrap label: a dark band round the body, its front printed
    lz0, lz1 = FLOOR + 0.03, FLOOR + 0.115
    box(g, (0, 0, (lz0 + lz1) / 2), (hx + 0.0012, hy + 0.0012, (lz1 - lz0) / 2), "band", bevel=0.0105)
    lw = hx - 0.01
    bl.quad(g, [(-lw, -hy - 0.0016, lz0), (lw, -hy - 0.0016, lz0), (lw, -hy - 0.0016, lz1), (-lw, -hy - 0.0016, lz1)],
            "label")
    # the shoulder: a frustum from the body's top to the collar
    s0, s1 = z1 - 0.004, FLOOR + 0.17
    rx, ry, rt = hx - 0.012, hy - 0.012, 0.024
    n = 24
    top = [(rt * math.cos(-3 * math.pi / 4 + 2 * math.pi * k / n), rt * math.sin(-3 * math.pi / 4 + 2 * math.pi * k / n), s1)
           for k in range(n)]
    faces = []
    # map each top vertex to the point of the body's top rectangle at the same angle
    ring0 = []
    for k in range(n):
        a = -3 * math.pi / 4 + 2 * math.pi * k / n
        dx, dy = math.cos(a), math.sin(a)
        t = min(rx / abs(dx) if abs(dx) > 1e-6 else 9, ry / abs(dy) if abs(dy) > 1e-6 else 9)
        ring0.append((dx * t, dy * t, s0))
    mid = [((p[0] + q[0]) / 2 * 1.02, (p[1] + q[1]) / 2 * 1.02, s0 + (s1 - s0) * 0.45) for p, q in zip(ring0, top)]
    verts = ring0 + mid + top
    for r in range(2):
        for k in range(n):
            k1 = (k + 1) % n
            faces.append((r * n + k, r * n + k1, (r + 1) * n + k1, (r + 1) * n + k))
    g.add(verts, faces, "body")
    # the collar, with thread ridges, and the nozzle
    cylinder(g, (0, 0, s1 - 0.002), (0, 0, FLOOR + 0.19), 0.027, "collar", segs=28)
    for k in range(3):
        z = FLOOR + 0.174 + k * 0.0055
        bl.ring(g, z, z + 0.0025, 0.026, 0.0292, "collar", segs=28)
    lathe(g, [(0.0, FLOOR + 0.19), (0.019, FLOOR + 0.19), (0.019, FLOOR + 0.196), (0.012, FLOOR + 0.204),
              (0.0045, FLOOR + 0.232), (0.0028, FLOOR + 0.236), (0.0, FLOOR + 0.236)], "nozzle", steps=24)
    # a bead of glue at the tip and a run down the shoulder and the body
    dome(g, (0, 0, FLOOR + 0.2355), (0, 0, 1), 0.0034, "glue", segs=10, height=0.003)
    # the run hugs the front: down the nozzle, over the collar and the shoulder, onto the body, pooling in a drip
    gx = 0.026
    path = [(0.0036, FLOOR + 0.231), (0.0075, FLOOR + 0.218), (0.0115, FLOOR + 0.205), (0.0195, FLOOR + 0.197),
            (0.0305, FLOOR + 0.186), (0.0305, FLOOR + 0.172), (0.027, FLOOR + 0.168), (0.0305, FLOOR + 0.155),
            (0.0365, FLOOR + 0.145), (hy + 0.0005, FLOOR + 0.132), (hy + 0.0016, FLOOR + 0.122),
            (hy + 0.0016, FLOOR + 0.112)]
    rr = 0.0042
    pts = []
    for k, (y, z) in enumerate(path):
        x = gx * min(1.0, k / 6) ** 1.5 + 0.0012 * math.sin(k * 1.3)
        if k < 6:                                   # round parts: stay on the surface as the run moves off-centre
            y = math.sqrt(max(y * y - x * x, 0.0))
        pts.append((x, -(y + rr * 0.55), z))
    bl.sweep(g, pts, rr, "glue", segs=10)
    dome(g, pts[-1], (0, -0.3, -1), rr * 1.5, "glue", segs=12, height=rr * 1.3)
    root = bl.make_empty("Glue")
    bl.make_object("Bottle", g, mats, root)
    return root


# =====================================================================================================================
# Warehouse Key
# =====================================================================================================================

def key_print():
    w, h = 640, 480
    im = Image.new("RGB", (w, h), rgb255(BONE))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, 92], fill=rgb255(STEEL_BLUE))
    d.text((24, 46), "WH", fill=rgb255(BONE), font=font(64), anchor="lm")
    d.text((w - 24, 46), "07", fill=rgb255(MUSTARD), font=font(64), anchor="rm")
    d.rectangle([0, 92, w - 1, 118], fill=rgb255(MUSTARD))
    # the warehouse pictogram: a roof and a roll-up door
    ink = rgb255(GRAPHITE)
    x0, y0, x1, y1 = 330, 160, 610, 420
    d.polygon([(x0 - 10, y0 + 70), ((x0 + x1) // 2, y0), (x1 + 10, y0 + 70)], fill=ink)
    d.rectangle([x0 + 10, y0 + 70, x1 - 10, y1], fill=ink)
    d.rectangle([x0 + 50, y0 + 130, x1 - 50, y1], fill=rgb255(BONE))
    for k in range(6):
        y = y0 + 145 + k * 24
        d.rectangle([x0 + 58, y, x1 - 58, y + 9], fill=ink)
    # a barcode
    rnd = np.random.default_rng(11)
    x = 36
    while x < 290:
        bw = int(rnd.integers(3, 12))
        d.rectangle([x, 330, x + bw, 430], fill=ink)
        x += bw + int(rnd.integers(4, 10))
    return as_rgb(im)


def build_warehouse_key():
    mats = {
        "card": paint("key_card", 821, BONE),
        "grip": paint("key_grip", 823, STEEL_BLUE),
        "print": lt.decal("key_print", key_print(), rough=0.5),
        "grommet": pt.plain("key_grommet", GRAPHITE, 0.5, 0.4),
        "hole": pt.plain("key_hole", "#0c0d0f", 0.9, 0.2),
        "chip": metal("key_chip", 825, "#c9924f", rough=0.3, metal_=0.95),
        "chip_line": pt.plain("key_chip_line", "#2a1c12", 0.6, 0.2),
        "ring": metal("key_ring", 827, "#b9bcc1", rough=0.28, metal_=0.95),
    }
    g = Geo()
    t = 0.0085                                      # half the card's thickness
    cz = FLOOR + t + 0.0022                         # the grommet stands proud of the card's underside
    top = cz + t
    box(g, (0.012, 0, cz), (0.098, 0.064, t), "card", bevel=0.004)
    # the grip end: a thicker steel-blue overmould at -X with the lanyard grommet
    box(g, (-0.07, 0, cz + 0.0006), (0.032, 0.0665, t + 0.0016), "grip", bevel=0.005)
    gx = -0.08
    bl.ring(g, cz - t - 0.0018, top + 0.0024, 0.0075, 0.0125, "grommet", segs=24, center=(gx, 0.0))
    cylinder(g, (gx, 0, cz - t - 0.001), (gx, 0, top + 0.0015), 0.0076, "hole", segs=20)
    # the print on the top face
    pz = top + 0.0003
    bl.quad(g, [(-0.034, -0.058, pz), (0.104, -0.058, pz), (0.104, 0.058, pz), (-0.034, 0.058, pz)], "print")
    # the copper contact chip
    box(g, (0.0, -0.034, pz + 0.0004), (0.013, 0.011, 0.0005), "chip", bevel=0.0006)
    for k in (-1, 0, 1):
        box(g, (k * 0.0045, -0.034, pz + 0.001), (0.00045, 0.0098, 0.0002), "chip_line")
    box(g, (0.0, -0.034, pz + 0.001), (0.0118, 0.00045, 0.0002), "chip_line")
    # a steel split ring through the grommet, lying on the grip
    ring_c, ring_r = Vector((gx - 0.006, 0.0, top + 0.004)), 0.019
    pts = []
    for k in range(41):
        a = 0.25 + (2 * math.pi - 0.15) * k / 40
        pts.append(tuple(ring_c + Vector((ring_r * math.cos(a), ring_r * math.sin(a), 0.0012 * math.sin(a * 0.5)))))
    bl.sweep(g, pts, 0.0021, "ring", segs=10)
    root = bl.make_empty("WarehouseKey")
    bl.make_object("Card", g, mats, root)
    return root


# =====================================================================================================================
# Explosive Remnant
# =====================================================================================================================

def hazard_band():
    w, h = 1024, 128
    im = Image.new("RGB", (w, h), rgb255(GRAPHITE))
    d = ImageDraw.Draw(im)
    step = 64
    for x in range(-h, w + h, 2 * step):
        d.polygon([(x, h), (x + step, h), (x + step + h, 0), (x + h, 0)], fill=rgb255(MUSTARD))
    return as_rgb(im)


def gauge_face():
    s = 256
    im = Image.new("RGB", (s, s), (226, 222, 208))
    d = ImageDraw.Draw(im)
    c = s // 2
    d.arc([22, 22, s - 22, s - 22], 150, 390, fill=(30, 30, 32), width=8)
    d.arc([22, 22, s - 22, s - 22], 330, 390, fill=rgb255(SIGNAL_RED), width=18)
    for k in range(9):
        a = math.radians(150 + k * 30)
        d.line([(c + 92 * math.cos(a), c + 92 * math.sin(a)), (c + 110 * math.cos(a), c + 110 * math.sin(a))],
               fill=(30, 30, 32), width=6)
    a = math.radians(372)                                    # pinned in the red
    d.line([(c, c), (c + 96 * math.cos(a), c + 96 * math.sin(a))], fill=rgb255(SIGNAL_RED), width=9)
    d.ellipse([c - 14, c - 14, c + 14, c + 14], fill=(30, 30, 32))
    # the cracked glass
    for pts in (((60, 40), (100, 90), (96, 140), (130, 190)), ((100, 90), (150, 70), (210, 80)),
                ((96, 140), (60, 170), (40, 200))):
        d.line(pts, fill=(250, 250, 250), width=4)
    return as_rgb(im)


def build_explosive_remnant():
    mats = {
        "tank": paint("rem_tank", 831, SCUTTLER_RED),
        "hazard": lt.decal("rem_hazard", hazard_band(), rough=0.55),
        "cap": metal("rem_cap", 833, GUNMETAL, wear=0.8),
        "steel": metal("rem_steel", 835, "#a7abb0", rough=0.32, metal_=0.9),
        "gauge": lt.decal("rem_gauge", gauge_face(), rough=0.15),
        "lamp": pt.plain("rem_lamp", "#5a1612", 0.25, 0.0),
        "dark": pt.plain("rem_dark", "#141517", 0.8, 0.2),
        "copper": metal("rem_copper", 837, COPPER, rough=0.32, metal_=0.95),
        "wire": pt.plain("rem_wire", SIGNAL_RED, 0.45, 0.0),
        "dome": pt.plain("rem_dome", "#a7abb0", 0.3, 0.9),
    }
    g = Geo()
    r = 0.05
    cz = FLOOR + r + 0.006
    x0, x1 = -0.074, 0.07
    rng = tube_x(g, x0, x1, r, "tank", cz, rings=14, segs=36)
    # two dents: a big one on the upper front, a small one on the back
    dent(g, rng, (0.02, -0.035, cz + 0.035), 0.04, 0.011, cz)
    dent(g, rng, (-0.035, 0.04, cz + 0.02), 0.025, 0.006, cz)
    # end caps: a gunmetal collar and a steel dome at +X; at -X a collar, a flat plate and the valve
    for xa, xb in ((x1 - 0.004, x1 + 0.01), (x0 - 0.01, x0 + 0.004)):
        cylinder(g, (xa, 0, cz), (xb, 0, cz), r + 0.0045, "cap", segs=36)
    lathe_pts = [(0.0, 0.0), (r - 0.002, 0.0), (r - 0.008, 0.016), (r * 0.55, 0.03), (0.0, 0.035)]
    m = Matrix(((0, 0, 1), (0, 1, 0), (-1, 0, 0)))            # lathe Z -> +X
    sub = Geo()
    lathe(sub, lathe_pts, "dome", steps=36)
    g.extend(sub, Matrix.Translation((x1 + 0.01, 0, cz)) @ m.to_4x4())
    for k in range(8):
        a = 2 * math.pi * k / 8
        dome(g, (x1 + 0.0105, (r - 0.004) * math.cos(a), cz + (r - 0.004) * math.sin(a)), (1, 0, 0), 0.0035, "steel")
    cylinder(g, (x0 - 0.013, 0, cz), (x0 - 0.01, 0, cz), r - 0.004, "cap", segs=36)
    cylinder(g, (x0 - 0.03, 0, cz), (x0 - 0.013, 0, cz), 0.011, "steel", segs=18)
    bl.hexnut(g, (x0 - 0.024, 0, cz), (-1, 0, 0), 0.016, 0.009, "cap")
    cylinder(g, (x0 - 0.043, 0, cz), (x0 - 0.03, 0, cz), 0.006, "copper", segs=12)
    # the hazard band
    wrap(g, (-0.01, 0, cz), (1, 0, 0), r + 0.0012, -0.02, 0.02, 0, 2 * math.pi, "hazard", segs=36, ref=(0, 0, 1))
    # the gauge on the top: a gunmetal boss, its cracked face, a steel bezel
    gx = 0.035
    gz = cz + r - 0.004
    cylinder(g, (gx, 0.008, gz), (gx, 0.008, gz + 0.012), 0.009, "cap", segs=16)
    cylinder(g, (gx, 0.008, gz + 0.012), (gx, 0.008, gz + 0.02), 0.022, "cap", segs=28)
    bl.ring(g, gz + 0.02, gz + 0.024, 0.0185, 0.0225, "steel", segs=28, center=(gx, 0.008))
    face_r = 0.0186
    verts = [(gx + face_r * math.cos(2 * math.pi * k / 28), 0.008 + face_r * math.sin(2 * math.pi * k / 28), gz + 0.0212)
             for k in range(28)]
    uv = [(0.5 + 0.5 * math.cos(2 * math.pi * k / 28), 0.5 + 0.5 * math.sin(2 * math.pi * k / 28)) for k in range(28)]
    base = len(g.v)
    g.v.extend(verts)
    for k in range(1, 27):
        g.f.append((base, base + k, base + k + 1))
        g.m.append("gauge")
        g.uv.append((uv[0], uv[k], uv[k + 1]))
    # the dead warning lamp beside the gauge, in a steel cage
    lx, ly = -0.03, -0.01
    lz = cz + r - 0.006
    cylinder(g, (lx, ly, lz), (lx, ly, lz + 0.008), 0.013, "cap", segs=20)
    dome(g, (lx, ly, lz + 0.008), (0, 0, 1), 0.0105, "lamp", segs=16, height=0.012)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.3
        pts = bl.bezier((lx + 0.012 * math.cos(a), ly + 0.012 * math.sin(a), lz + 0.008),
                        (lx + 0.013 * math.cos(a), ly + 0.013 * math.sin(a), lz + 0.02),
                        (lx + 0.004 * math.cos(a), ly + 0.004 * math.sin(a), lz + 0.024), (lx, ly, lz + 0.0245), n=8)
        bl.sweep(g, pts, 0.0012, "steel", segs=6)
    # the torn mount bracket: a bent gunmetal strap round the back, its broken tab sticking up, rivets, soot
    strap_x = 0.005
    pts = [(strap_x, (r + 0.003) * math.sin(a), cz + (r + 0.003) * math.cos(a)) for a in np.linspace(0.4, 2.8, 14)]
    for p, q in zip(pts, pts[1:]):
        mid = (Vector(p) + Vector(q)) / 2
        dvec = Vector(q) - Vector(p)
        normal = (mid - Vector((strap_x, 0, cz))).normalized()
        side = dvec.normalized()
        rot = Matrix((Vector((1, 0, 0)), side, normal)).transposed()
        box(g, tuple(mid), (0.009, dvec.length / 2 + 0.0006, 0.0018), "cap", rot=rot)
    tab = Matrix.Rotation(math.radians(-35), 3, "X")
    box(g, (strap_x, 0.048, cz + 0.06), (0.0095, 0.004, 0.017), "cap", bevel=0.0012, rot=tab)
    dome(g, (strap_x, 0.046, cz + 0.064), tuple(tab @ Vector((0, -1, 0))), 0.003, "steel")
    # the frayed wire from the lamp, torn off past the strap
    pts = bl.bezier((lx, ly + 0.01, lz + 0.004), (lx + 0.002, 0.035, cz + r + 0.02), (-0.06, 0.05, cz + r + 0.01),
                    (-0.075, 0.062, cz + 0.035), n=16)
    bl.sweep(g, pts, 0.0026, "wire", segs=8)
    end = Vector(pts[-1])
    for k, dv in enumerate(((-0.01, 0.006, -0.004), (-0.008, 0.0, -0.01), (-0.012, -0.004, 0.001))):
        bl.sweep(g, [tuple(end), tuple(end + Vector(dv) * 0.5), tuple(end + Vector(dv))], 0.0008, "copper", segs=5)
    root = bl.make_empty("ExplosiveRemnant")
    bl.make_object("Tank", g, mats, root)
    return root


# =====================================================================================================================
# Chemical
# =====================================================================================================================

def chem_plate():
    w, h = 384, 160
    im = Image.new("RGB", (w, h), rgb255(MUSTARD))
    d = ImageDraw.Draw(im)
    ink = rgb255(GRAPHITE)
    d.rectangle([4, 4, w - 5, h - 5], outline=ink, width=8)
    # a hazard diamond with a flask in it
    cx, cy, s = 84, h // 2, 58
    d.polygon([(cx, cy - s), (cx + s, cy), (cx, cy + s), (cx - s, cy)], fill=ink)
    d.polygon([(cx - 10, cy - 30), (cx + 10, cy - 30), (cx + 10, cy - 8), (cx + 26, cy + 24), (cx - 26, cy + 24),
               (cx - 10, cy - 8)], fill=rgb255(MUSTARD))
    d.polygon([(cx - 17, cy + 8), (cx + 17, cy + 8), (cx + 26, cy + 24), (cx - 26, cy + 24)], fill=rgb255(CHEMICAL_GREEN))
    d.text((250, cy), "CH-4", fill=ink, font=font(60), anchor="mm")
    return as_rgb(im)


def build_chemical():
    mats = {
        "cap": metal("chem_cap", 841, GUNMETAL),
        "rail": metal("chem_rail", 843, "#4f5257", wear=0.7),
        "glass": gc.glass_material(),
        "liquid": pt.plain("chem_liquid", "#4fa82a", 0.15, 0.0, emission=CHEMICAL_GREEN, strength=1.4),
        "bubble": pt.plain("chem_bubble", "#c8f7a8", 0.1, 0.0, emission="#b6f58c", strength=2.0),
        "copper": metal("chem_copper", 845, COPPER, rough=0.32, metal_=0.95),
        "steel": metal("chem_steel", 847, "#a7abb0", rough=0.32, metal_=0.9),
        "plate": lt.decal("chem_plate", chem_plate(), rough=0.55),
        "seal": pt.plain("chem_seal", "#141517", 0.7, 0.0),
    }
    g = Geo()
    R = 0.06
    b0, b1 = FLOOR, FLOOR + 0.04                 # base cap
    t0, t1 = FLOOR + 0.178, FLOOR + 0.204        # top cap
    lathe(g, [(0, b0), (R - 0.004, b0), (R, b0 + 0.004), (R, b1 - 0.004), (R - 0.004, b1), (0, b1)], "cap", steps=40)
    lathe(g, [(0, t0), (R - 0.004, t0), (R, t0 + 0.004), (R, t1 - 0.006), (R - 0.008, t1), (0, t1)], "cap", steps=40)
    bl.ring(g, b1, b1 + 0.004, 0.044, 0.052, "seal", segs=36)
    bl.ring(g, t0 - 0.004, t0, 0.044, 0.052, "seal", segs=36)
    # the glass tube and the chemical in it
    cylinder(g, (0, 0, b1), (0, 0, t0), 0.048, "glass", segs=36)
    lz = FLOOR + 0.152
    cylinder(g, (0, 0, b1 + 0.001), (0, 0, lz), 0.0455, "liquid", segs=36)
    bl.ring(g, lz - 0.0015, lz + 0.0025, 0.038, 0.0458, "liquid", segs=36)       # the meniscus
    for x, y, z, rr in ((0.012, -0.03, FLOOR + 0.09, 0.0045), (-0.02, -0.032, FLOOR + 0.118, 0.0032),
                        (0.026, -0.028, FLOOR + 0.135, 0.0026), (-0.004, -0.04, FLOOR + 0.07, 0.003)):
        dome(g, (x, y, z), (x, y, 0), rr, "bubble", segs=10, height=rr * 0.9)
    # four rails between the caps
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        x, y = 0.054 * math.cos(a), 0.054 * math.sin(a)
        rot = Matrix.Rotation(a, 3, "Z")
        box(g, (x, y, (b1 + t0) / 2), (0.0055, 0.0065, (t0 - b1) / 2 + 0.003), "rail", bevel=0.0015, rot=rot)
        for z in (b1 - 0.012, t0 + 0.012):
            dome(g, (x * 1.11, y * 1.11, z), (math.cos(a), math.sin(a), 0), 0.0034, "steel")
    # the valve on top: a copper stem, a gunmetal hex body, a steel wheel
    cylinder(g, (0, 0, t1), (0, 0, t1 + 0.008), 0.012, "copper", segs=18)
    bl.hexnut(g, (0, 0, t1 + 0.004), (0, 0, 1), 0.014, 0.007, "cap")
    cylinder(g, (0, 0, t1 + 0.011), (0, 0, t1 + 0.016), 0.004, "steel", segs=10)
    bl.ring(g, t1 + 0.014, t1 + 0.018, 0.0145, 0.019, "steel", segs=24)
    for k in range(3):
        a = 2 * math.pi * k / 3
        box(g, (0.0085 * math.cos(a), 0.0085 * math.sin(a), t1 + 0.016), (0.0075, 0.0015, 0.0015), "steel",
            rot=Matrix.Rotation(a, 3, "Z"))
    # the hazard plate on the front of the base, curved round it
    wrap(g, (0, 0, 0), (0, 0, 1), R + 0.0008, b0 + 0.008, b1 - 0.008, -math.pi / 2 - 0.62, -math.pi / 2 + 0.62, "plate",
         segs=12, ref=(1, 0, 0))
    root = bl.make_empty("Chemical")
    bl.make_object("Cell", g, mats, root)
    return root


# =====================================================================================================================
# Paint Ammo
# =====================================================================================================================

def paint_label():
    w, h = 1024, 256
    im = Image.new("RGB", (w, h), rgb255(PAINT_BLUE))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, 18], fill=rgb255(HULL))
    d.rectangle([0, h - 19, w - 1, h - 1], fill=rgb255(HULL))
    # a bone panel with a paint splat pictogram and the code, centred on the front
    px0, px1 = 352, 672
    d.rectangle([px0, 40, px1, h - 40], fill=rgb255(BONE))
    blue, ink = rgb255(PAINT_BLUE), rgb255(HULL)
    cx, cy = px0 + 100, h // 2 - 6
    d.ellipse([cx - 58, cy - 50, cx + 58, cy + 50], fill=blue)
    for dx, dy, rr_ in ((-62, -40, 16), (64, -30, 12), (58, 40, 14), (-48, 52, 10), (8, -66, 11)):
        d.ellipse([cx + dx - rr_, cy + dy - rr_, cx + dx + rr_, cy + dy + rr_], fill=blue)
    d.text((px1 - 82, cy), "PT-1", fill=ink, font=font(44), anchor="mm")
    return as_rgb(im)


def build_paint_ammo():
    mats = {
        "tin": metal("paint_tin", 851, ZINC, rough=0.34, metal_=0.85, wear=0.6),
        "label": lt.decal("paint_label", paint_label(), rough=0.5),
        "paint": pt.plain("paint_paint", PAINT_BLUE, 0.18, 0.0),
        "lid": pt.plain("paint_lid", "#8d949b", 0.3, 0.9),
        "wire": metal("paint_wire", 855, "#b9bcc1", rough=0.28, metal_=0.95),
        "grip": pt.plain("paint_grip", GRAPHITE, 0.6, 0.05),
    }
    g = Geo()
    R = 0.072
    z0, z1 = FLOOR, FLOOR + 0.16
    lathe(g, [(0, z0 + 0.002), (R - 0.004, z0), (R + 0.002, z0 + 0.003), (R + 0.002, z0 + 0.009), (R, z0 + 0.011),
              (R, z1 - 0.012), (R + 0.003, z1 - 0.009), (R + 0.003, z1 - 0.002), (R - 0.004, z1), (0, z1)], "tin", steps=48)
    for z in (z0 + 0.03, z1 - 0.032):                                                    # rolled beads
        bl.ring(g, z, z + 0.004, R - 0.004, R + 0.0016, "tin", segs=48)
    # the wrap label (the paint's colour), printed with a splat on the front
    wrap(g, (0, 0, 0), (0, 0, 1), R + 0.0005, z0 + 0.038, z1 - 0.04, -math.pi / 2 - math.pi, -math.pi / 2 + math.pi,
         "label", segs=48, ref=(1, 0, 0))
    # the lid with its groove and blue paint round the rim
    lathe(g, [(0, z1), (R - 0.006, z1), (R - 0.006, z1 + 0.003), (R - 0.012, z1 + 0.003), (R - 0.014, z1 + 0.0015),
              (0, z1 + 0.0015)], "lid", steps=48)
    bl.ring(g, z1 + 0.0005, z1 + 0.0035, R - 0.0062, R + 0.0018, "paint", segs=48)
    # paint run over the rim and down the side
    for a, length, rr in ((-1.95, 0.05, 0.0042), (-1.65, 0.028, 0.0034), (-1.25, 0.072, 0.0046), (-0.9, 0.02, 0.003),
                          (2.4, 0.04, 0.004)):
        c, s_ = math.cos(a), math.sin(a)
        # a tapered run: from over the rim bead down the wall, thinning, to a round drop
        prof = [(R + 0.0032, z1 + 0.001, rr * 1.15), (R + 0.0034, z1 - 0.006, rr)]
        prof += [(R + rr * 0.45, z1 - 0.008 - length * k / 5, rr * (1 - 0.35 * k / 5)) for k in range(6)]
        for (r0, za, ra), (r1, zb, rb) in zip(prof, prof[1:]):
            cylinder(g, (r0 * c, r0 * s_, za), (r1 * c, r1 * s_, zb), ra, "paint", segs=10, r1=rb)
        rl, zl, rr_end = prof[-1]
        dome(g, (rl * c, rl * s_, zl + rr_end * 0.4), (0, 0, -1), rr_end * 1.25, "paint", segs=12, height=rr_end * 1.6)
    # the bail: two ears and a wire laid down to the back, with a grip
    ear_z = z1 - 0.022
    for sx in (-1, 1):
        box(g, (sx * (R + 0.002), 0, ear_z), (0.003, 0.009, 0.011), "tin", bevel=0.0018)
        cylinder(g, (sx * (R + 0.005), 0, ear_z + 0.004), (sx * (R - 0.001), 0, ear_z + 0.004), 0.0028, "wire", segs=8)
    tilt = math.radians(62)                                                             # laid down toward +Y
    pts = []
    for k in range(25):
        a = math.pi * k / 24
        x = -(R + 0.005) * math.cos(a)
        up = (R + 0.006) * 0.82 * math.sin(a)
        pts.append((x, up * math.sin(tilt), ear_z + 0.004 + up * math.cos(tilt)))
    bl.sweep(g, pts, 0.0019, "wire", segs=8)
    mid = Vector(pts[12])
    cylinder(g, tuple(mid - Vector((0.022, 0, 0))), tuple(mid + Vector((0.022, 0, 0))), 0.0062, "grip", segs=14)
    root = bl.make_empty("PaintAmmo")
    bl.make_object("Tin", g, mats, root)
    return root


# =====================================================================================================================
# Main
# =====================================================================================================================

ITEMS = {
    "glue": ("Glue", build_glue),
    "warehouse_key": ("Warehouse Key", build_warehouse_key),
    "explosive_remnant": ("Explosive Remnant", build_explosive_remnant),
    "chemical": ("Chemical", build_chemical),
    "paint_ammo": ("Paint Ammo", build_paint_ammo),
}


def import_glb(path):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    obs = [o for o in bpy.context.scene.objects if o not in before]
    root = bl.make_empty(path.stem)
    for o in obs:
        if o.parent is None:
            o.parent = root
    return root


def bucket_icons():
    for name in ("water_bucket", "water_bucket_full", "chemical_bucket"):
        bbe.reset_scene()
        root = import_glb(MESHES / f"{name}.glb")
        icon(bbe.mesh_tree(root), ICONS / f"{name}_icon.png", fill=1.18)
        print(f"wrote {(ICONS / f'{name}_icon.png').relative_to(REPO)}")


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    only = [a for a in argv if a in ITEMS] or list(ITEMS)
    stats = {}
    for item in only:
        title, build = ITEMS[item]
        bbe.reset_scene()
        root = build()
        objs = bbe.mesh_tree(root)
        lo, hi = bounds(objs)
        stats[item] = (lo, hi, triangles(root))
        assert all(abs(c) <= HALF + 1e-4 for c in (*lo, *hi)), f"{item} leaves its 0.25 m cube: {lo} {hi}"
        bbe.export(root, MESHES / f"{item}.glb", animations=False)
        icon(objs, ICONS / f"{item}_icon.png")
        for p in (MESHES / f"{item}.glb", ICONS / f"{item}_icon.png"):
            print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
        print(f"{item}: bounds {tuple(round(v, 4) for v in lo)} .. {tuple(round(v, 4) for v in hi)}, "
              f"{stats[item][2]} triangles")
        if out:
            import render_materials
            render_materials.sheet(item, title, root, out, sys.modules[__name__], stats[item])
    if "--no-buckets" not in argv and len(only) == len(ITEMS):
        bucket_icons()
    if out:
        import render_materials
        render_materials.lineup(out, sys.modules[__name__])


if __name__ == "__main__":
    main(sys.argv[1:])
