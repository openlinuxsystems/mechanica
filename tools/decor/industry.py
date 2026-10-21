"""Industrial and site decor (parts map batches 15a and 15b): tanks, drums, generator coils, the crane, vents, dishes
and broken concrete in the Colony Works look (steel-blue rails, bone panels, graphite trim, mustard safety paint,
cyan only on what still has power), the SV-04's Hull Door, Nut and Screw in Salvager Tech (hull, gunmetal, copper)
and the scrap Reflector Antenna in Settler Patchwork. tools/part_kit/decor_kit.py has the materials and the runner;
build_decor.py runs them.
"""

from __future__ import annotations

import math
import random

import bmesh
import bpy  # noqa: F401
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

import decor_kit as D
from decor_kit import Geo, Part, box, cylinder, dome, lathe, ring

TAU = 2 * math.pi

# =====================================================================================================
# Shapes
# =====================================================================================================


def solid(g, verts, faces, mat):
    """Adds a closed mesh with its normals made consistent (outward) and its n-gons ear-clipped, so concave caps and
    lofts of any winding come out right."""
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in verts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big, quad_method="BEAUTY", ngon_method="EAR_CLIP")
    bm.verts.ensure_lookup_table()
    g.add([tuple(v.co) for v in bm.verts], [tuple(v.index for v in f.verts) for f in bm.faces], mat)
    bm.free()
    return g


def loft(g, rings, mat, caps=True):
    """A solid through `rings` (lists of points, all the same length), capped at both ends."""
    n = len(rings[0])
    verts = [tuple(p) for r in rings for p in r]
    faces = []
    for j in range(len(rings) - 1):
        for i in range(n):
            i1 = (i + 1) % n
            faces.append((j * n + i, (j + 1) * n + i, (j + 1) * n + i1, j * n + i1))
    if caps:
        faces.append(tuple(range(n)))
        faces.append(tuple((len(rings) - 1) * n + i for i in range(n)))
    return solid(g, verts, faces, mat)


def rrect(hw, hh, r, steps=3):
    """A rounded rectangle's outline (u, v), counter-clockwise."""
    pts = []
    for cx, cy, a0 in ((hw - r, -hh + r, -90), (hw - r, hh - r, 0), (-hw + r, hh - r, 90), (-hw + r, -hh + r, 180)):
        for k in range(steps + 1):
            a = math.radians(a0 + 90 * k / steps)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def chamfered(hw, hh, c):
    """A rectangle with its corners cut by `c`, counter-clockwise (u, v)."""
    return [(-hw + c, -hh), (hw - c, -hh), (hw, -hh + c), (hw, hh - c), (hw - c, hh), (-hw + c, hh), (-hw, hh - c), (-hw, -hh + c)]


def prism(g, outline, y0, y1, mat, x=0.0, z=0.0):
    """An (x, z) outline extruded along y from y0 to y1 (a front-facing plate)."""
    return loft(g, [[(x + u, y, z + v) for u, v in outline] for y in (y0, y1)], mat)


def frame_plate(g, outer, inner, y0, y1, mat, x=0.0, z=0.0):
    """A flat frame between two outlines with the same point count (the door surround), y0 to y1 deep."""
    n = len(outer)
    loops = [[(x + u, y, z + v) for u, v in pts] for pts, y in ((outer, y0), (outer, y1), (inner, y1), (inner, y0))]
    verts = [p for lp in loops for p in lp]
    faces = []
    for a in range(4):
        b = (a + 1) % 4
        for i in range(n):
            i1 = (i + 1) % n
            faces.append((a * n + i, b * n + i, b * n + i1, a * n + i1))
    return solid(g, verts, faces, mat)


def tube(g, path, radius, mat, segs=10, radii=None):
    """A round tube along a polyline in any plane, its radius per point from `radii` if given."""
    path = [Vector(p) for p in path]
    rings = []
    u = None
    for k, p in enumerate(path):
        t = (path[min(k + 1, len(path) - 1)] - path[max(k - 1, 0)]).normalized()
        if u is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            u = t.cross(ref).normalized()
        else:   # parallel transport: keep the last frame, minus its part along the new tangent (no twist)
            u = (u - t * u.dot(t)).normalized()
        w = t.cross(u).normalized()
        r = radius if radii is None else radii[k]
        rings.append([p + (u * math.cos(TAU * i / segs) + w * math.sin(TAU * i / segs)) * r for i in range(segs)])
    return loft(g, rings, mat)


def torus(g, c, axis, R, r, mat, segs=32, tsegs=10):
    """A ring (shackle eye, hand wheel) of radius R around `axis` through c, its section radius r."""
    c, a = Vector(c), Vector(axis).normalized()
    u, w = D.bl.frame_of(a)
    verts, faces = [], []
    for i in range(segs):
        th = TAU * i / segs
        d = u * math.cos(th) + w * math.sin(th)
        for j in range(tsegs):
            ph = TAU * j / tsegs
            verts.append(tuple(c + d * (R + r * math.cos(ph)) + a * r * math.sin(ph)))
    for i in range(segs):
        for j in range(tsegs):
            i1, j1 = (i + 1) % segs, (j + 1) % tsegs
            faces.append((i * tsegs + j, i1 * tsegs + j, i1 * tsegs + j1, i * tsegs + j1))
    return solid(g, verts, faces, mat)


def label_band(g, R, z0, z1, a0, a1, mat, segs=16):
    """A printed label wrapped round a drum (radius R) from angle a0 to a1, the image upright and facing out."""
    verts, faces, uvs = [], [], []
    for k in range(segs + 1):
        a = a0 + (a1 - a0) * k / segs
        verts += [(R * math.cos(a), R * math.sin(a), z0), (R * math.cos(a), R * math.sin(a), z1)]
    for k in range(segs):
        faces.append((2 * k, 2 * k + 2, 2 * k + 3, 2 * k + 1))
        uvs.append(((k / segs, 0), ((k + 1) / segs, 0), ((k + 1) / segs, 1), (k / segs, 1)))
    return g.add(verts, faces, mat, uvs=uvs)


def front_decal(g, x, z, y, hw, hh, mat):
    """A decal on a front (-y) face at y."""
    return D.decal(g, (x, y - 0.0006, z), (1, 0, 0), (0, 0, 1), hw, hh, mat)


# =====================================================================================================
# Images
# =====================================================================================================

def hazard_image():
    """Mustard and graphite safety chevrons."""
    w, h = 512, 64
    im = Image.new("RGB", (w, h), (43, 47, 53))
    d = ImageDraw.Draw(im)
    for k in range(-2, 18):
        x = k * 40
        d.polygon([(x, h), (x + 20, h), (x + 20 + h, 0), (x + h, 0)], fill=(224, 181, 48))
    return im


def juice_image():
    """The juice drum's label: Colony Works berry juice concentrate, a bunch of purple berries on bone."""
    w, h = 768, 256
    im = Image.new("RGB", (w, h), (217, 209, 188))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w, 34], fill=(62, 106, 140))
    d.rectangle([0, h - 34, w, h], fill=(62, 106, 140))
    cx, cy = 150, 132
    for k, (dx, dy) in enumerate(((-34, -26), (0, -34), (34, -26), (-18, 6), (18, 6), (0, 40), (-40, 26), (40, 26))):
        d.ellipse([cx + dx - 24, cy + dy - 24, cx + dx + 24, cy + dy + 24], fill=(108, 72, 150))
        d.ellipse([cx + dx - 12, cy + dy - 16, cx + dx - 2, cy + dy - 6], fill=(170, 142, 200))
    d.polygon([(cx - 6, cy - 60), (cx + 6, cy - 60), (cx + 40, cy - 92), (cx + 20, cy - 96)], fill=(93, 140, 56))
    f = D.font(54)
    d.text((270, 70), "BERRY JUICE", font=f, fill=(43, 47, 53))
    d.text((272, 136), "CONCENTRATE  ·  200 L", font=D.font(30, bold=False), fill=(62, 106, 140))
    d.text((272, 178), "COLONY WORKS", font=D.font(30), fill=(43, 47, 53))
    return im


def warn_image():
    """A mustard high-voltage triangle with a graphite bolt."""
    s = 256
    im = Image.new("RGB", (s, s), (217, 209, 188))
    d = ImageDraw.Draw(im)
    d.polygon([(128, 18), (240, 226), (16, 226)], fill=(43, 47, 53))
    d.polygon([(128, 46), (216, 210), (40, 210)], fill=(224, 181, 48))
    d.polygon([(140, 80), (100, 150), (128, 150), (112, 196), (160, 124), (132, 124), (150, 80)], fill=(43, 47, 53))
    return im


# =====================================================================================================
# Batch 15a
# =====================================================================================================


def air_conditioner(m):
    """4 x 3 x 3: a Colony Works condenser unit: a bone cabinet in steel-blue rails, a round fan grille and a louvred
    panel on the front, graphite skids and the refrigerant lines out of the side."""
    g = Geo()
    hx, hy, hz = 0.5, 0.375, 0.375
    zb = -hz + 0.05
    box(g, (0, 0, (zb + hz - 0.012) / 2), (hx - 0.03, hy - 0.035, (hz - 0.012 - zb) / 2), "bone", bevel=0.012)
    D.edge_frame(g, (0, 0, (zb + hz - 0.008) / 2), (hx - 0.022, hy - 0.026, (hz - 0.008 - zb) / 2), 0.016, "steel", 0.005,
                 corner=0.02, cmat="graphite", cbevel=0.005)
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.12), 0, -hz + 0.025), (0.05, hy - 0.01, 0.025), "graphite", bevel=0.006)
    fy = -hy + 0.035
    # fan: a dark well, three graphite blades, concentric steel rings and a cross of spokes
    fx, fz, fr = -0.17, 0.02, 0.24
    loc = Geo()
    cylinder(loc, (0, 0, -0.04), (0, 0, 0.0), fr, "hole", segs=40)
    for k in range(3):
        rot = Matrix.Rotation(TAU * k / 3, 3, "Z") @ Matrix.Rotation(0.35, 3, "Y")
        box(loc, tuple(Matrix.Rotation(TAU * k / 3, 3, "Z") @ Vector((0.11, 0, -0.022))), (0.1, 0.045, 0.004), "graphite",
            bevel=0.003, rot=rot)
    cylinder(loc, (0, 0, -0.03), (0, 0, -0.005), 0.04, "steel_dark", segs=20)
    for rr in (0.07, 0.12, 0.17, 0.22):
        torus(loc, (0, 0, 0.004), (0, 0, 1), rr, 0.004, "steel_dark", segs=40, tsegs=6)
    torus(loc, (0, 0, 0.004), (0, 0, 1), fr, 0.008, "steel", segs=48, tsegs=8)
    for a in (0, math.pi / 2):
        box(loc, (0, 0, 0.006), (fr, 0.004, 0.004), "steel_dark", rot=Matrix.Rotation(a + math.pi / 4, 3, "Z"))
    cylinder(loc, (0, 0, 0.0), (0, 0, 0.012), 0.03, "graphite", segs=20)
    g.extend(loc, Matrix.Translation((fx, fy, fz)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    # louvres
    lx0, lx1 = 0.12, hx - 0.06
    box(g, ((lx0 + lx1) / 2, fy + 0.004, 0.02), ((lx1 - lx0) / 2, 0.004, 0.25), "hole")
    for k in range(9):
        z = -0.2 + k * 0.05
        box(g, ((lx0 + lx1) / 2, fy - 0.006, z), ((lx1 - lx0) / 2, 0.006, 0.018), "steel", bevel=0.002,
            rot=Matrix.Rotation(-0.6, 3, "X"))
    # badge, power light
    box(g, (0.25, fy - 0.003, 0.3), (0.105, 0.003, 0.025), "graphite", bevel=0.002)
    D.pixel_text(g, "CW-AC", (0.25, fy - 0.006, 0.3), (1, 0, 0), (0, 0, 1), 0.006, "bone")
    cylinder(g, (0.4, fy - 0.004, 0.3), (0.4, fy - 0.009, 0.3), 0.008, "cyan_glow", segs=12)
    # refrigerant lines out of the right side, down into the skid
    for k, (z, r, mat) in enumerate(((0.1, 0.022, "rubber"), (-0.02, 0.014, "copper"))):
        y = 0.12 - k * 0.09
        path = [(hx - 0.035, y, z), (hx - r - 0.004, y, z), (hx - r - 0.004, y, z - 0.03), (hx - r - 0.004, y, -hz + 0.1)]
        tube(g, D.bend(path, 0.03, 4), r, mat, segs=10)
        cylinder(g, (hx - 0.036, y, z), (hx - 0.026, y, z), r + 0.01, "chrome", segs=14)
    # side vent slots
    for k in range(6):
        box(g, (-hx + 0.028, 0, -0.18 + k * 0.07), (0.0015, hy - 0.12, 0.012), "hole")
    D.bolts(g, [(sx * (hx - 0.05), fy - 0.002, sz * (hz - 0.05)) for sx in (-1, 1) for sz in (-0.4, 1)], (0, -1, 0), 0.007)
    return g


def juice_drum(m):
    """2 x 3 x 2: a 200 litre steel drum of berry juice concentrate in steel-blue enamel, two rolling hoops, a bone lid
    with its bungs and our own Colony Works label."""
    g = Geo()
    hz, R = 0.375, 0.235
    prof = [(0.001, -hz + 0.006), (R - 0.012, -hz + 0.006), (R, -hz + 0.02), (R, hz - 0.03), (R - 0.012, hz - 0.016),
            (0.001, hz - 0.016)]
    lathe(g, prof, "enamel_steel", steps=40)
    for z in (-hz + 0.012, hz - 0.022):
        ring(g, z - 0.008, z + 0.008, R - 0.004, R + 0.007, "steel_dark", segs=40)
    for z in (-0.12, 0.12):
        ring(g, z - 0.012, z + 0.012, R - 0.004, R + 0.012, "steel", segs=40)
    cylinder(g, (0, 0, hz - 0.03), (0, 0, hz - 0.012), R - 0.016, "bone", segs=40)
    for (x, y), r in (((0.11, 0.06), 0.03), ((-0.12, -0.05), 0.02)):
        cylinder(g, (x, y, hz - 0.013), (x, y, hz - 0.004), r, "graphite", segs=6)
        cylinder(g, (x, y, hz - 0.004), (x, y, hz - 0.001), r * 0.6, "graphite", segs=16)
    label_band(g, R + 0.0015, -0.095, 0.095, -math.pi / 2 - 1.05, -math.pi / 2 + 1.05, "label_juice", segs=18)
    return g


def concrete_chunk(g, outline, hy, seed, rebars, top):
    """A slab of broken cast concrete: `outline` (x, z) extruded through the part's depth, both faces chipped, with
    rusty rebar ends bent out of the break."""
    rng = random.Random(seed)
    # four rings through the depth: the faces chipped back from the edges, every point nudged so no side is flat
    umax = max(abs(u) for u, _ in outline)
    rings = []
    for y, shrink in ((-hy + 0.015, 0.035), (-hy + 0.055, 0.0), (hy - 0.055, 0.0), (hy - 0.015, 0.035)):
        ring_pts = []
        for u, v in outline:
            on_floor = v < -0.36
            du = -math.copysign(shrink, u) * (abs(u) > 0.05) + rng.uniform(-0.012, 0.012)
            dv = 0.0 if on_floor else -shrink * 0.7 + rng.uniform(-0.012, 0.012)
            ring_pts.append((max(-umax, min(umax, u + du)), y + rng.uniform(-0.01, 0.01), v + dv))
        rings.append(ring_pts)
    loft(g, rings, "concrete_site")
    for (x, y), lean in rebars:
        z0 = top(x) - 0.08
        tip = Vector((x + lean * 0.09, y, min(0.355, z0 + 0.25)))
        path = [(x, y, z0), (x, y, z0 + 0.12), (x + lean * 0.03, y, z0 + 0.18), tuple(tip)]
        tube(g, D.bend(path, 0.05, 4), 0.015, "rebar", segs=8)
        for k in range(5):
            z = z0 + 0.04 + k * 0.024
            cylinder(g, (x, y, z), (x, y, z + 0.006), 0.018, "rebar", segs=8)
    # rubble at the foot
    for k in range(5):
        x = rng.uniform(-0.3, 0.3)
        y = rng.choice((-1, 1)) * rng.uniform(hy - 0.08, hy - 0.05)
        s = rng.uniform(0.02, 0.035)
        box(g, (x, y, -0.375 + s * 1.8), (s * 1.3, s, s), "concrete_site", bevel=0.006,
            rot=Matrix.Rotation(rng.uniform(0, 3), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 3, "X"))


def broken_concrete(hx, seed):
    def build(m):
        g = Geo()
        hy, hz = 0.25, 0.375
        rng = random.Random(seed)
        # the outline: a flat foot, two broken flanks and a jagged top, higher at one end
        lo, hi = 0.08 if hx > 0.4 else 0.05, 0.26
        n = 9
        topline = []
        for k in range(n + 1):
            x = -hx + 0.03 + (2 * hx - 0.06) * k / n
            base = lo + (hi - lo) * (1 - k / n) ** 1.4
            topline.append((x, base + rng.uniform(-0.04, 0.04)))
        outline = [(-hx + 0.03, -hz + 0.005), (hx - 0.03, -hz + 0.005)]
        outline += [(hx - 0.01, -hz + 0.12)] + list(reversed(topline)) + [(-hx + 0.012, -hz + 0.16)]

        def top(x):
            pts = sorted(topline)
            for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
                if x0 <= x <= x1:
                    return z0 + (z1 - z0) * (x - x0) / (x1 - x0)
            return pts[-1][1]

        bars = [((-hx * 0.55, -0.08), -1), ((-hx * 0.2, 0.09), 1), ((hx * 0.35, -0.02), 1)][: 3 if hx > 0.4 else 2]
        concrete_chunk(g, outline, hy, seed, bars, top)
        return g
    return build


def crane_body(m):
    """4 x 4 x 4: a Colony Works hoist: a steel-blue frame with bone side panels, the cable drum and its mustard motor
    inside, hazard chevrons on the front rail, lifting eyes on top and the cable out of the bottom."""
    g = Geo()
    h = 0.5
    D.edge_frame(g, (0, 0, 0), (h - 0.03, h - 0.03, h - 0.03), 0.024, "steel", 0.006, corner=0.028, cmat="graphite",
                 cbevel=0.006)
    for sx in (-1, 1):
        D.panel(g, (sx * (h - 0.03), 0, 0), (0.008, h - 0.05, h - 0.05), 0, 0.03, "steel_dark", "bone", bevel=0.004)
        D.pixel_text(g, "SWL 2T", (sx * (h - 0.0215), 0, 0.2), (0, sx, 0), (0, 0, 1), 0.012, "graphite")
    box(g, (0, 0, h - 0.035), (h - 0.05, h - 0.05, 0.01), "steel_dark", bevel=0.004)
    box(g, (0, 0, -h + 0.035), (h - 0.05, h - 0.05, 0.01), "steel_dark", bevel=0.004)
    # drum with its cable wraps between two flanges, on bearings
    dz = 0.06
    cylinder(g, (-0.34, 0, dz), (0.3, 0, dz), 0.17, "graphite", segs=32)
    for x in (-0.34, 0.3):
        cylinder(g, (x - 0.012, 0, dz), (x + 0.012, 0, dz), 0.23, "steel", segs=32)
    for k in range(18):
        x = -0.31 + k * 0.034
        torus(g, (x, 0, dz), (1, 0, 0), 0.175, 0.013, "chrome", segs=16, tsegs=5)
    cylinder(g, (-0.45, 0, dz), (-0.355, 0, dz), 0.05, "steel_dark", segs=16)
    # motor and gearbox on the right
    cylinder(g, (0.32, 0, dz), (0.44, 0, dz), 0.11, "mustard", segs=28)
    for k in range(5):
        x = 0.34 + k * 0.022
        cylinder(g, (x, 0, dz), (x + 0.008, 0, dz), 0.122, "mustard", segs=20)
    box(g, (0.38, 0, dz - 0.16), (0.06, 0.09, 0.05), "graphite", bevel=0.008)
    # cable off the drum, down through a guide in the floor
    cylinder(g, (0, -0.175, dz), (0, -0.175, -h + 0.002), 0.012, "chrome", segs=10)
    ring(g, -h + 0.02, -h + 0.06, 0.02, 0.05, "graphite", segs=20, center=(0, -0.175))
    # lifting eyes on top
    for sx in (-1, 1):
        box(g, (sx * 0.25, 0, h - 0.045), (0.05, 0.02, 0.012), "steel", bevel=0.004)
        torus(g, (sx * 0.25, 0, h - 0.015 - 0.035), (0, 1, 0), 0.035, 0.01, "steel_dark", segs=20, tsegs=8)
    # hazard chevrons on the front and back bottom rails
    for sy in (-1, 1):
        D.decal(g, (0, sy * (h - 0.0005), -h + 0.06), (-sy, 0, 0), (0, 0, 1), h - 0.07, 0.018, "hazard")
    return g


def crane_hook(m):
    """2 x 4 x 2: the hoist's hook block: a shackle eye, the sheave block with mustard cheeks, a swivel and a forged
    hook painted mustard, with its safety latch."""
    g = Geo()
    torus(g, (0, 0, 0.43), (0, 1, 0), 0.045, 0.013, "chrome", segs=24, tsegs=8)
    box(g, (0, 0, 0.33), (0.11, 0.06, 0.06), "graphite", bevel=0.01)
    for sy in (-1, 1):
        box(g, (0, sy * 0.07, 0.31), (0.14, 0.012, 0.09), "mustard", bevel=0.008)
        cylinder(g, (0, sy * 0.082, 0.3), (0, sy * 0.09, 0.3), 0.025, "steel_dark", segs=6)
    cylinder(g, (0, -0.058, 0.31), (0, 0.058, 0.31), 0.085, "steel_dark", segs=28)
    cylinder(g, (0, 0, 0.235), (0, 0, 0.16), 0.035, "chrome", segs=20)
    D.hexnut(g, (0, 0, 0.16), (0, 0, -1), 0.05, 0.035, "steel_dark")
    # the hook: a tapered round bar down the shank and round the bowl
    cx, cz, rb = 0.06, -0.27, 0.115
    path = [(cx - rb, 0, 0.125), (cx - rb, 0, cz + 0.06)]
    for k in range(1, 15):
        a = math.pi + (math.pi + math.radians(40)) * k / 14
        path.append((cx + rb * math.cos(a), 0, cz + rb * math.sin(a)))
    radii = [0.04, 0.042] + [0.044 - 0.024 * (k / 14) ** 1.5 for k in range(1, 15)]
    tube(g, path, 0.04, "mustard", segs=14, radii=radii)
    cylinder(g, (cx - rb, 0, 0.13), (cx - rb, 0, 0.115), 0.047, "steel_dark", segs=20)
    # the latch from the shank to the tip
    tip = Vector(path[-1])
    a = Vector((cx - rb + 0.03, 0, cz + 0.11))
    box(g, tuple((a + tip) / 2), ((tip - a).length / 2, 0.012, 0.004), "chrome", bevel=0.002,
        rot=Matrix.Rotation(-math.atan2(tip.z - a.z, tip.x - a.x), 3, "Y"))
    return g


def fuse_box(m):
    """2 x 3 x 1: a Colony Works wall fuse box: a graphite back plate, a steel-blue cabinet with a bone door, the
    fuses behind a window, a high-voltage label, a cyan power light and the conduit out of the top."""
    g = Geo()
    hx, hy, hz = 0.25, 0.125, 0.375
    box(g, (0, hy - 0.01, -0.02), (hx - 0.01, 0.01, hz - 0.06), "graphite", bevel=0.004)
    box(g, (0, 0.03, -0.02), (hx - 0.035, 0.08, hz - 0.09), "steel", bevel=0.01)
    fy = -0.05
    D.panel(g, (0, fy - 0.004, -0.02), (hx - 0.045, 0.006, hz - 0.1), 1, 0.022, "steel_dark", "bone", bevel=0.003)
    # window and the fuses behind it
    box(g, (0, fy - 0.006, 0.12), (0.15, 0.002, 0.06), "dark")
    for k in range(5):
        box(g, (-0.12 + k * 0.06, fy - 0.009, 0.12), (0.016, 0.004, 0.04), "mustard" if k % 2 else "plastic_bone", bevel=0.003)
        box(g, (-0.12 + k * 0.06, fy - 0.011, 0.12), (0.008, 0.002, 0.044), "chrome")
    box(g, (0, fy - 0.013, 0.12), (0.155, 0.002, 0.065), "glass")
    front_decal(g, 0.07, -0.08, fy - 0.011, 0.06, 0.06, "warn")
    # latch and hinges
    box(g, (-0.15, fy - 0.016, -0.09), (0.012, 0.008, 0.05), "chrome", bevel=0.004)
    for z in (-0.25, 0.2):
        D.hinge(g, (hx - 0.05, fy - 0.01, z), 0.06, 0.009, "chrome")
    cylinder(g, (0.13, fy - 0.011, 0.25), (0.13, fy - 0.016, 0.25), 0.01, "cyan_glow", segs=12)
    D.pixel_text(g, "FUSES", (-0.05, fy - 0.011, 0.24), (1, 0, 0), (0, 0, 1), 0.007, "graphite")
    # conduit
    for x in (-0.1, 0.1):
        cylinder(g, (x, 0.03, hz - 0.09), (x, 0.03, hz - 0.002), 0.022, "steel_dark", segs=14)
        cylinder(g, (x, 0.03, hz - 0.09), (x, 0.03, hz - 0.075), 0.03, "graphite", segs=6)
    D.bolts(g, [(sx * (hx - 0.03), hy - 0.02 - 0.001, sz * (hz - 0.09)) for sx in (-1, 1) for sz in (-1, 1)], (0, -1, 0), 0.008)
    return g


COIL = 0.375


def coil_section(f):
    """The generator coil's cross section (u across the run, 0 inside to 0.75 outside, v up), `f` the winding ridge
    scale: a graphite plinth, steel-blue cheeks and the copper winding between them."""
    return {
        "plinth": [(0.0, -COIL), (2 * COIL, -COIL), (2 * COIL, -COIL + 0.07), (0.0, -COIL + 0.07)],
        "wind": [(0.07 - 0.02 * f, -COIL + 0.07), (2 * COIL - 0.07 + 0.02 * f, -COIL + 0.07),
                 (2 * COIL - 0.07 + 0.02 * f, 0.22 + 0.02 * f), (2 * COIL - 0.16, 0.3 + 0.02 * f),
                 (0.16, 0.3 + 0.02 * f), (0.07 - 0.02 * f, 0.22 + 0.02 * f)],
    }


def coil(place, length, steps, bands):
    """A generator coil along a run: `place(u, v, t)` maps the section to space at t in 0..1. The windings ridge
    every few centimetres; steel-blue clamp bands at `bands`."""
    g = Geo()
    sec = coil_section(0)
    e = 0.003 / length   # the plinth, windings and core stop 3 mm short of the ends, inside the end bands' faces
    inner = place
    place = lambda u, v, t: inner(u, v, e + (1 - 2 * e) * t)  # noqa: E731
    loft(g, [[place(u, v, k / steps) for u, v in sec["plinth"]] for k in range(steps + 1)], "graphite")
    turns = max(4, round(length / 0.028))
    for k in range(turns):
        t0, t1 = k / turns, (k + 0.86) / turns
        sub = 1 if steps == 1 else 2
        rings = [[place(u, v, t0 + (t1 - t0) * j / sub) for u, v in coil_section(1)["wind"]] for j in range(sub + 1)]
        loft(g, rings, "copper_wire")
    core = [[place(u, v, k / steps) for u, v in coil_section(-0.6)["wind"]] for k in range(steps + 1)]
    loft(g, core, "copper_dark")
    for t in bands:
        band = [(-0.0, -COIL + 0.065), (2 * COIL, -COIL + 0.065), (2 * COIL, 0.25), (2 * COIL - 0.15, 0.335),
                (0.15, 0.335), (0.0, 0.25)]
        dt = 0.02 / length
        t0, t1 = max(t - dt, 0.0), min(t + dt, 1.0)
        if t1 - t0 < dt * 1.5:   # an end band: the full width, flush with the end
            t0, t1 = (0.0, 2 * dt) if t < 0.5 else (1.0 - 2 * dt, 1.0)
        loft(g, [[inner(u, v, tt) for u, v in band] for tt in (t0, t1)], "steel")
        for u in (2 * COIL - 0.06,):   # outer side only: an inner bolt would stand out of the corner's box
            tc = (t0 + t1) / 2
            p = Vector(place(u, -COIL + 0.035, tc))
            q = Vector(place(u + (0.01 if u < 0.1 else -0.01), -COIL + 0.035, tc))
            D.hexnut(g, tuple(p), tuple((p - q).normalized()), 0.014, 0.006, "steel_dark")
    return g


def coil_segment(m):
    """3 x 3 x 2: a straight run of generator coil: copper windings on a graphite plinth with steel-blue clamp bands;
    it lines up with the Coil Corner."""
    hy = 0.25
    return coil(lambda u, v, t: (u - COIL, -hy + 2 * hy * t, v), 2 * hy, 1, (0.0, 1.0))


def coil_corner(m):
    """3 x 3 x 3: the generator coil turning a quarter round, the same section as the Coil Segment swept about the
    part's back-left edge."""
    # the run starts on the left face (-x is the inside edge) going round to the front; keep it in the box
    def p2(u, v, t):
        a = math.pi / 2 * t
        r = max(u, 0.0005)
        return (-COIL + r * math.sin(a), COIL - r * math.cos(a), v)

    return coil(p2, COIL * math.pi / 2, 12, (0.0, 0.5, 1.0))


def tank(hx, hz, ladder, stencil):
    """A Colony Works storage tank: a steel-blue drum with a domed top, bone bands, weld seams, a graphite skirt, a
    manhole hatch, a dial gauge, a drain valve and (on the large one) a ladder."""
    def build(m):
        g = Geo()
        R = hx - (0.085 if ladder else 0.03)
        zb, zt = -hz + 0.05, hz - 0.1 * (R / 0.8) - 0.02
        prof = [(0.001, zb), (R, zb), (R, zt)]
        dome_h = hz - 0.02 - zt
        for k in range(1, 9):
            a = math.pi / 2 * k / 8
            prof.append((max(R * math.cos(a), 0.001), zt + dome_h * math.sin(a)))
        lathe(g, prof, "steel", steps=48)
        cylinder(g, (0, 0, -hz), (0, 0, zb + 0.004), R - 0.03, "graphite", segs=48)
        ring(g, -hz, -hz + 0.02, R - 0.06, R - 0.02, "steel_dark", segs=48)
        for z in (zb + 0.12, zt - 0.06):
            ring(g, z - 0.025, z + 0.025, R - 0.004, R + 0.01, "bone", segs=48)
        for z in ((zb + zt) / 2,):
            ring(g, z - 0.004, z + 0.004, R - 0.002, R + 0.004, "steel_dark", segs=48)
        # hatch on top
        hr = min(0.12, R * 0.3)
        cylinder(g, (0, 0, hz - 0.035), (0, 0, hz - 0.012), hr, "steel_dark", segs=28)
        cylinder(g, (0, 0, hz - 0.012), (0, 0, hz - 0.002), hr * 0.8, "graphite", segs=28)
        for k in range(8):
            a = TAU * k / 8
            dome(g, (hr * 0.9 * math.cos(a), hr * 0.9 * math.sin(a), hz - 0.012), (0, 0, 1), 0.007, "bolt")
        box(g, (0, 0, hz - 0.008), (hr * 0.5, 0.008, 0.006), "chrome", bevel=0.003)
        # gauge and drain valve on the front
        gz = zb + (zt - zb) * 0.62
        cylinder(g, (0.0, -R + 0.01, gz), (0.0, -R - 0.02, gz), 0.045, "chrome", segs=24)
        cylinder(g, (0.0, -R - 0.02, gz), (0.0, -R - 0.024, gz), 0.038, "bone", segs=24)
        box(g, (0.01, -R - 0.026, gz + 0.008), (0.018, 0.0015, 0.003), "graphite",
            rot=Matrix.Rotation(0.6, 3, "Y"))
        vx = R * 0.45
        vy = -math.sqrt(R * R - vx * vx)
        cylinder(g, (vx, vy + 0.02, zb + 0.07), (vx, vy - 0.025, zb + 0.07), 0.026, "steel_dark", segs=16)
        box(g, (vx, vy - 0.03, zb + 0.07), (0.03, 0.012, 0.03), "graphite", bevel=0.006)
        torus(g, (vx, vy - 0.045, zb + 0.07), (0, 1, 0), 0.03, 0.006, "mustard", segs=20, tsegs=6)
        # stencil on a bone plate on the front left
        phi = -math.pi / 2 - 0.45
        n = Vector((math.cos(phi), math.sin(phi), 0))
        u = Vector((-math.sin(phi), math.cos(phi), 0))
        sz = zb + 0.24
        p = n * (R + 0.004) + Vector((0, 0, sz))
        box(g, tuple(p), (0.075, 0.004, 0.03), "bone", bevel=0.002, rot=Matrix((u, n, u.cross(n))).transposed())
        D.pixel_text(g, stencil, tuple(p + n * 0.004), tuple(u), (0, 0, 1), 0.007, "graphite")
        if ladder:
            lx = R + 0.045
            for sy in (-1, 1):
                cylinder(g, (lx, sy * 0.1, zb + 0.02), (lx, sy * 0.1, zt + 0.02), 0.012, "steel_dark", segs=10)
                box(g, (R + 0.02, sy * 0.1, zt - 0.05), (0.03, 0.01, 0.01), "steel_dark")
                box(g, (R + 0.02, sy * 0.1, zb + 0.1), (0.03, 0.01, 0.01), "steel_dark")
            for k in range(int((zt - zb) / 0.12)):
                z = zb + 0.09 + k * 0.12
                cylinder(g, (lx, -0.1, z), (lx, 0.1, z), 0.009, "zinc", segs=8)
        return g
    return build


BATCH_15A = {
    "air_conditioner": Part((4, 3, 3), air_conditioner, "Colony Works condenser: bone cabinet in steel-blue rails, round fan grille, louvres, refrigerant lines, cyan power light."),
    "juice_drum": Part((2, 3, 2), juice_drum, "Colony Works 200 L drum of berry juice concentrate: steel-blue enamel, rolling hoops, bone lid with bungs, our own label."),
    "broken_concrete_large": Part((4, 3, 2), broken_concrete(0.5, 113), "A broken slab of cast concrete with rusted rebar bent out of the break and rubble at its foot."),
    "broken_concrete_medium": Part((3, 3, 2), broken_concrete(0.375, 114), "A smaller broken concrete slab with two rebar ends."),
    "crane_body": Part((4, 4, 4), crane_body, "Colony Works hoist: steel-blue frame, bone panels, cable drum, mustard motor, hazard chevrons, lifting eyes."),
    "crane_hook": Part((2, 4, 2), crane_hook, "The hoist's hook block: shackle eye, sheave block with mustard cheeks, swivel, mustard forged hook with latch."),
    "fuse_box": Part((2, 3, 1), fuse_box, "Colony Works wall fuse box: bone door in a steel-blue cabinet, fuses behind glass, high-voltage label, cyan light, conduit."),
    "coil_corner": Part((3, 3, 3), coil_corner, "Generator coil turning a quarter round: copper windings, graphite plinth, steel-blue clamp bands."),
    "coil_segment": Part((3, 3, 2), coil_segment, "A straight run of generator coil that lines up with the Coil Corner."),
    "tank_large": Part((7, 5, 7), tank(0.875, 0.625, True, "CW-T7"), "Colony Works storage tank: steel-blue drum, domed top, bone bands, hatch, gauge, drain valve, ladder."),
}


# =====================================================================================================
# Batch 15b
# =====================================================================================================


def hull_door(m):
    """5 x 10 x 1: the SV-04's maintenance hatch: a chamfered hull surround with copper trim, a gunmetal leaf with a
    porthole, a hand wheel, heavy hinges, a hazard kick plate and a cyan status light."""
    g = Geo()
    hx, hy, hz = 0.625, 0.125, 1.25
    outer, inner = chamfered(hx - 0.005, hz - 0.005, 0.16), chamfered(hx - 0.1, hz - 0.1, 0.11)
    frame_plate(g, outer, inner, -hy + 0.01, hy - 0.01, "hull")
    frame_plate(g, chamfered(hx - 0.085, hz - 0.085, 0.12), inner, -hy + 0.005, -hy + 0.012, "copper")
    leaf = chamfered(hx - 0.105, hz - 0.105, 0.1)
    prism(g, leaf, -0.04, 0.06, "gun")
    # leaf ribs, hazard kick plate, stencil
    for z in (-0.55, 0.05):
        box(g, (0, -0.045, z), (hx - 0.16, 0.008, 0.035), "gun", bevel=0.006)
    D.decal(g, (0, -0.0415, -hz + 0.2), (1, 0, 0), (0, 0, 1), hx - 0.2, 0.06, "hazard")
    D.pixel_text(g, "MAINT 04", (0, -0.0425, -0.3), (1, 0, 0), (0, 0, 1), 0.014, "bone_tech")
    # porthole
    pz = 0.6
    cylinder(g, (0, -0.041, pz), (0, -0.0, pz), 0.12, "hole", segs=32)
    torus(g, (0, -0.045, pz), (0, 1, 0), 0.13, 0.022, "copper", segs=36, tsegs=8)
    cylinder(g, (0, -0.03, pz), (0, -0.034, pz), 0.122, "glass", segs=32)
    for k in range(8):
        a = TAU * k / 8
        dome(g, (0.165 * math.cos(a), -0.042, pz + 0.165 * math.sin(a)), (0, -1, 0), 0.009, "bolt")
    # hand wheel on a hub
    wz = -0.12
    cylinder(g, (0, -0.04, wz), (0, -0.09, wz), 0.035, "gun", segs=20)
    torus(g, (0, -0.095, wz), (0, 1, 0), 0.13, 0.014, "chrome", segs=40, tsegs=8)
    for k in range(3):
        a = TAU * k / 3 + 0.3
        box(g, (0.065 * math.cos(a), -0.093, wz + 0.065 * math.sin(a)), (0.065, 0.007, 0.008), "chrome",
            rot=Matrix.Rotation(-a, 3, "Y"))
    cylinder(g, (0, -0.09, wz), (0, -0.1, wz), 0.03, "copper", segs=20)
    # hinges on the left, dogs on the right
    for z in (-0.7, 0.75):
        box(g, (-hx + 0.11, -0.06, z), (0.08, 0.02, 0.07), "gun", bevel=0.01)
        cylinder(g, (-hx + 0.05, -0.07, z - 0.09), (-hx + 0.05, -0.07, z + 0.09), 0.03, "hull", segs=16)
    for z in (-0.6, 0.15, 0.7):
        box(g, (hx - 0.13, -0.052, z), (0.03, 0.012, 0.025), "copper", bevel=0.005)
    # status light in the surround
    box(g, (0, -hy + 0.008, hz - 0.05), (0.12, 0.004, 0.016), "graphite", bevel=0.003)
    box(g, (0, -hy + 0.004, hz - 0.05), (0.1, 0.002, 0.008), "cyan_glow")
    D.bolts(g, [(x, -hy + 0.009, z) for x in (-hx + 0.05, hx - 0.05) for z in (-0.9, -0.3, 0.3, 0.9)], (0, -1, 0), 0.01)
    return g


def threads(g, r, z0, z1, mat, pitch=0.028):
    """A threaded shank: a core with a ridge every `pitch`."""
    cylinder(g, (0, 0, z0), (0, 0, z1), r - 0.008, mat, segs=28)
    k, z = 0, z0 + pitch * 0.5
    while z < z1 - pitch * 0.4:
        prof = [(r - 0.009, z - pitch * 0.42), (r, z - 0.003), (r, z + 0.003), (r - 0.009, z + pitch * 0.42)]
        lathe(g, prof, mat, steps=24)
        z += pitch
        k += 1


def nut(m):
    """3 x 3 x 3: a giant hex nut run onto a threaded bolt end, the SV-04's own: gunmetal with chamfers and a copper
    washer."""
    g = Geo()
    h = 0.375
    threads(g, 0.15, -h, h - 0.02, "gun")
    cylinder(g, (0, 0, h - 0.02), (0, 0, h - 0.002), 0.14, "gun", segs=28, r1=0.12)
    R = 0.36
    for z0, z1, r0, r1 in ((-0.14, -0.11, R * 0.88, R), (-0.11, 0.11, R, R), (0.11, 0.14, R, R * 0.88)):
        cylinder(g, (0, 0, z0), (0, 0, z1), r0, "gun", segs=6, r1=r1, phase=math.pi / 6)
    ring(g, -0.17, -0.14, 0.152, 0.3, "copper", segs=40)
    D.pixel_text(g, "M300", (0, -R * math.cos(math.pi / 6) - 0.0004, 0.0), (1, 0, 0), (0, 0, 1), 0.012, "bone_tech")
    return g


def screw(m):
    """3 x 3 x 3: a giant slotted cheese-head screw, the SV-04's: gunmetal head split by its slot, a copper washer and
    a threaded shank to a point."""
    g = Geo()
    h = 0.375
    R = 0.34
    prof = [(0.001, 0.15), (R, 0.15), (R, h - 0.07), (R - 0.04, h - 0.018), (R - 0.09, h - 0.003), (0.001, h - 0.003)]
    slot = 0.032
    lathe(g, [(max(r, 0.001), z) for r, z in prof], "gun", a0=-math.pi / 2, a1=math.pi / 2, steps=24, center=(slot, 0))
    lathe(g, [(max(r, 0.001), z) for r, z in prof], "gun", a0=math.pi / 2, a1=3 * math.pi / 2, steps=24, center=(-slot, 0))
    box(g, (0, 0, 0.19), (slot + 0.002, R - 0.03, 0.04), "hole")
    ring(g, 0.12, 0.15, 0.15, 0.32, "copper", segs=40)
    threads(g, 0.15, -h + 0.12, 0.12, "gun")
    cylinder(g, (0, 0, -h + 0.12), (0, 0, -h + 0.004), 0.142, "gun", segs=28, r1=0.02)
    return g


def reflector_antenna(m):
    """3 x 6 x 2: a settler-built antenna mast: a zinc pole in a wooden block, a beam of crossbars, a wire-mesh
    reflector framed in sea-teal scrap, twine lashings and a rag tied at the top."""
    g = Geo()
    hz = 0.75
    box(g, (0, 0.02, -hz + 0.05), (0.16, 0.14, 0.05), "wood", bevel=0.01)
    box(g, (0.02, 0.0, -hz + 0.105), (0.08, 0.06, 0.008), "zinc", bevel=0.003)
    cylinder(g, (0, 0.02, -hz + 0.1), (0, 0.02, hz - 0.06), 0.018, "zinc", segs=12)
    cylinder(g, (0, 0.02, hz - 0.06), (0, 0.02, hz - 0.002), 0.006, "zinc", segs=8)
    # beam with crossbars, leaning a touch forward
    bz = 0.42
    cylinder(g, (-0.34, 0.02, bz), (0.34, 0.02, bz), 0.012, "zinc", segs=10)
    for k in range(7):
        x = -0.3 + k * 0.1
        half = 0.21 - k * 0.018
        cylinder(g, (x, 0.02 - half, bz), (x, 0.02 + half, bz), 0.005, "zinc", segs=6)
    ring(g, bz - 0.03, bz + 0.03, 0.017, 0.026, "twine", segs=12, center=(0, 0.02))
    # mesh reflector on its own arm
    rz = 0.06
    D.edge_frame(g, (0, 0.17, rz), (0.3, 0.012, 0.18), 0.012, "sea_paint", 0.003)
    for k in range(9):
        x = -0.27 + k * 0.0675
        cylinder(g, (x, 0.17, rz - 0.17), (x, 0.17, rz + 0.17), 0.003, "zinc", segs=4)
    for k in range(5):
        z = rz - 0.14 + k * 0.07
        cylinder(g, (-0.29, 0.17, z), (0.29, 0.17, z), 0.003, "zinc", segs=4)
    cylinder(g, (0, 0.035, rz), (0, 0.158, rz), 0.01, "rust", segs=8)
    ring(g, rz - 0.03, rz + 0.03, 0.017, 0.026, "twine", segs=12, center=(0, 0.02))
    # rag
    box(g, (0.05, 0.02, hz - 0.11), (0.04, 0.003, 0.03), "cotton_brick", bevel=0.002, rot=Matrix.Rotation(0.15, 3, "Y"))
    ring(g, hz - 0.14, hz - 0.08, 0.017, 0.024, "twine", segs=12, center=(0, 0.02))
    return g


def dish(g, R, depth, mat, back_mat, place):
    """A parabolic dish of radius R and `depth`, its axis local +z, shell 1.5 cm, a steel rim; `place` (4x4) sets it."""
    loc = Geo()
    n = 10
    outer = [(R * k / n, depth * (k / n) ** 2) for k in range(n + 1)]
    inner = [(r, z + 0.015) for r, z in reversed(outer)]
    prof = [(max(r, 0.001), z) for r, z in outer] + [(R + 0.004, depth + 0.006)] + [(max(r, 0.001), z) for r, z in inner]
    lathe(loc, prof, mat, steps=40)
    torus(loc, (0, 0, depth + 0.008), (0, 0, 1), R, 0.012, back_mat, segs=40, tsegs=6)
    g.extend(loc, place)


def dish_antenna(m):
    """4 x 4 x 2: a Colony Works satellite dish on a wall bracket: a bone dish, a steel-blue arm, the feed on three
    struts and a cyan link light."""
    g = Geo()
    hy, hz = 0.25, 0.5
    box(g, (0, hy - 0.012, -0.12), (0.14, 0.012, 0.2), "steel_dark", bevel=0.005)
    D.bolts(g, [(sx * 0.1, hy - 0.024, z) for sx in (-1, 1) for z in (-0.28, 0.04)], (0, -1, 0), 0.009)
    box(g, (0, 0.13, -0.12), (0.035, 0.11, 0.035), "steel", bevel=0.008)
    tilt = math.radians(14)
    c = Vector((0, 0.06, 0.03))
    place = Matrix.Translation(c) @ Matrix.Rotation(math.pi / 2 - tilt, 4, "X")
    R, depth = 0.42, 0.09
    dish(g, R, depth, "bone", "steel", place)
    box(g, (0, 0.1, -0.06), (0.05, 0.03, 0.07), "graphite", bevel=0.01)
    # the feed: three struts to a graphite horn at the focus
    axis = (place.to_3x3() @ Vector((0, 0, 1))).normalized()
    focus = c + axis * 0.2
    for k in range(3):
        a = TAU * k / 3 + math.pi / 2
        rim = place @ Vector((R * 0.92 * math.cos(a), R * 0.92 * math.sin(a), depth * 0.85))
        cylinder(g, tuple(rim), tuple(focus + axis * 0.02), 0.006, "steel_dark", segs=8)
    cylinder(g, tuple(focus - axis * 0.01), tuple(focus + axis * 0.05), 0.035, "graphite", segs=16, r1=0.025)
    cylinder(g, tuple(focus - axis * 0.013), tuple(focus - axis * 0.01), 0.03, "plastic_bone", segs=16)
    cylinder(g, (0.0, hy - 0.025, 0.04), (0.0, hy - 0.03, 0.04), 0.008, "cyan_glow", segs=10)
    return g


def reflector_dish(m):
    """4 x 4 x 3: a Colony Works offset-fed dish on a pedestal: a bone reflector, a steel-blue post on a base plate,
    a mustard feed boom from the bottom of the rim to the graphite feed and a cyan link light."""
    g = Geo()
    hy, hz = 0.375, 0.5
    box(g, (0, 0.18, -hz + 0.015), (0.16, 0.16, 0.015), "steel_dark", bevel=0.005)
    D.bolts(g, [(sx * 0.12, 0.18 + sy * 0.12, -hz + 0.03) for sx in (-1, 1) for sy in (-1, 1)], (0, 0, 1), 0.009)
    cylinder(g, (0, 0.18, -hz + 0.03), (0, 0.18, -0.02), 0.04, "steel", segs=20)
    box(g, (0, 0.18, -0.02), (0.06, 0.05, 0.05), "graphite", bevel=0.01)
    tilt = math.radians(20)
    c = Vector((0, 0.13, 0.07))
    place = Matrix.Translation(c) @ Matrix.Rotation(math.pi / 2 - tilt, 4, "X")
    R, depth = 0.38, 0.11
    dish(g, R, depth, "bone", "steel", place)
    axis = (place.to_3x3() @ Vector((0, 0, 1))).normalized()
    low = place @ Vector((0, -R * 0.95, depth * 0.9))
    feed = c + axis * 0.4 + Vector((0, 0, -0.02))
    cylinder(g, tuple(low), tuple(feed), 0.014, "mustard", segs=10)
    for sx in (-1, 1):
        side = place @ Vector((sx * R * 0.9, 0, depth * 0.8))
        cylinder(g, tuple(side), tuple(feed), 0.006, "steel_dark", segs=8)
    cylinder(g, tuple(feed - axis * 0.02), tuple(feed + axis * 0.09), 0.04, "graphite", segs=16, r1=0.03)
    cylinder(g, tuple(feed - axis * 0.023), tuple(feed - axis * 0.02), 0.034, "plastic_bone", segs=16)
    cylinder(g, (0.0, 0.18 - 0.051, -0.02), (0.0, 0.18 - 0.056, -0.02), 0.009, "cyan_glow", segs=10)
    return g


def steel_pallet(m):
    """5 x 1 x 5: a Colony Works steel pallet: pressed steel-blue deck channels on three graphite runners with fork
    pockets, mustard corner tags."""
    g = Geo()
    h, hz = 0.625, 0.125
    for x in (-0.52, 0.0, 0.52):
        box(g, (x, 0, -hz + 0.045), (0.07, h - 0.01, 0.045), "graphite", bevel=0.008)
        for y in (-0.3, 0.3):
            box(g, (x, y, -hz + 0.035), (0.0715, 0.12, 0.022), "hole")
    for k in range(9):
        y = -h + 0.07 + k * (2 * h - 0.14) / 8
        box(g, (0, y, hz - 0.02), (h - 0.01, 0.055, 0.018), "steel", bevel=0.006)
        box(g, (0, y, hz - 0.002), (h - 0.03, 0.03, 0.002), "steel_dark")
    for y in (-h + 0.06, 0, h - 0.06):
        box(g, (0, y, -hz + 0.096), (h - 0.012, 0.05, 0.006), "steel_dark", bevel=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (h - 0.06), sy * (h - 0.07), hz - 0.0015), (0.04, 0.03, 0.0015), "mustard")
    D.pixel_text(g, "CW-P5", (0.52, -h + 0.0085, -hz + 0.06), (1, 0, 0), (0, 0, 1), 0.006, "bone")
    return g


def vent_grid(m):
    """5 x 3 x 1: a Colony Works ventilation grille: a steel-blue frame with corner bolts, angled bone louvres over
    a graphite mesh, a mustard flow tag."""
    g = Geo()
    hx, hy, hz = 0.625, 0.125, 0.375
    D.edge_frame(g, (0, 0, 0), (hx - 0.035, hy - 0.035, hz - 0.035), 0.03, "steel", 0.008, corner=0.034, cmat="graphite",
                 cbevel=0.006)
    box(g, (0, hy - 0.06, 0), (hx - 0.05, 0.004, hz - 0.05), "hole")
    for k in range(13):
        x = -hx + 0.08 + k * (2 * hx - 0.16) / 12
        box(g, (x, hy - 0.07, 0), (0.002, 0.004, hz - 0.05), "graphite")
    for k in range(9):
        z = -hz + 0.075 + k * (2 * hz - 0.15) / 8
        box(g, (0, -0.02, z), (hx - 0.045, 0.045, 0.006), "bone", bevel=0.003, rot=Matrix.Rotation(-0.75, 3, "X"))
    box(g, (0, -0.02, 0), (0.008, 0.06, hz - 0.045), "steel_dark", bevel=0.003)
    box(g, (0.42, -0.1215, -hz + 0.035), (0.07, 0.0015, 0.014), "mustard")
    D.pixel_text(g, "AIR IN", (0.42, -0.123, -hz + 0.035), (1, 0, 0), (0, 0, 1), 0.004, "graphite")
    D.bolts(g, [(sx * (hx - 0.035), -0.1195, sz * (hz - 0.035)) for sx in (-1, 1) for sz in (-1, 1)], (0, -1, 0), 0.007)
    return g


BATCH_15B = {
    "hull_door": Part((5, 10, 1), hull_door, "Salvager Tech: the SV-04 maintenance hatch, hull surround with copper trim, gunmetal leaf, porthole, hand wheel, hazard kick plate, cyan light."),
    "tank_medium": Part((5, 4, 5), tank(0.625, 0.5, False, "CW-T5"), "The Colony Works storage tank, medium: steel-blue drum, domed top, bone bands, hatch, gauge, drain valve."),
    "nut": Part((3, 3, 3), nut, "Salvager Tech: a giant gunmetal hex nut run onto a threaded bolt end, copper washer."),
    "reflector_antenna": Part((3, 6, 2), reflector_antenna, "Settler-built mast: zinc pole in a wooden block, crossbar beam, wire-mesh reflector in sea-teal scrap, twine, a rag."),
    "dish_antenna": Part((4, 4, 2), dish_antenna, "Colony Works satellite dish on a wall bracket: bone dish, steel-blue arm, feed on three struts, cyan light.", name="Satellite Dish"),
    "reflector_dish": Part((4, 4, 3), reflector_dish, "Colony Works offset-fed dish on a pedestal: bone reflector, steel-blue post, mustard feed boom, cyan light."),
    "screw": Part((3, 3, 3), screw, "Salvager Tech: a giant slotted gunmetal screw, copper washer, threaded to a point."),
    "tank_small": Part((3, 4, 3), tank(0.375, 0.5, False, "T3"), "The Colony Works storage tank, small: very buoyant, a good float for boats."),
    "steel_pallet": Part((5, 1, 5), steel_pallet, "Colony Works steel pallet: steel-blue deck channels on graphite runners with fork pockets, mustard tags."),
    "vent_grid": Part((5, 3, 1), vent_grid, "Colony Works ventilation grille: steel-blue frame, angled bone louvres over graphite mesh."),
}


def materials_15(m):
    m["hazard"] = D.image_mat("dk_hazard", hazard_image(), rough=0.6)
    m["concrete_site"] = D.lt.textured("dk_concrete_site", D.gc.half_res(D.concrete_surface(1067, "#8e897d")), normal_strength=1.8)
    m["label_juice"] = D.image_mat("dk_label_juice", juice_image(), rough=0.5)
    m["warn"] = D.image_mat("dk_warn", warn_image(), rough=0.5)
    # copper kept deep and brown so it never reads orange
    m["copper_wire"] = D.fm.metal("dk_copper_wire", 1061, "#8c5c3e", 0.32, 0.95, wear=0.4)
    m["copper_dark"] = D.fm.metal("dk_copper_dark", 1063, "#7a5038", 0.4, 0.9, wear=0.3)
    m["copper"] = D.fm.metal("dk_copper_trim", 1065, "#7e5444", 0.34, 0.95, wear=0.5)
