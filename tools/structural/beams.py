"""Batch 8a and 8b of the parts map: beams, frames and supports (Colony Works steelwork, steel-blue primer).

Our I-beams, Metal Column and Support Pillar keep their sizes; Scrap Mechanic's sizes are new parts beside them
(I-Beam (Stub) for its 1-block Short I-Beam, Metal Column (Short) for its 5-block column, Lattice Pillar for its
5x5x5 Support Pillar). Built by build_structural.py.
"""

from __future__ import annotations

import math

import structural_kit as S
from mathutils import Vector

Geo, box, cylinder = S.Geo, S.box, S.cylinder
B = S.BLOCK
E = S.EPS
IBEAM = S.i_profile(0.11, 0.118, 0.012, 0.022)


def bar(g, a, b, hu, hv, mat, up=(0, 0, 1), chamfer=0.0):
    """A rectangular bar from a to b (a flat strap or a square tube)."""
    S.sweep(g, S.rect(hu, hv, chamfer), [Vector(a), Vector(b)], up, mat)


def plate(g, c, half, mat="graphite", bevel=0.004):
    box(g, c, half, mat, bevel=bevel)


# =====================================================================================================
# I-beams
# =====================================================================================================

def ibeam_run(length, bolts=True):
    def build():
        g = Geo()
        L = length * B / 2 - E
        pts, pieces = IBEAM
        S.sweep(g, pts, [(0, -L, 0), (0, L, 0)], (0, 0, 1), "blue", pieces=pieces)
        if bolts:
            for k in range(length):
                y = -length * B / 2 + B * (k + 0.5)
                for x in (-0.062, 0.062):
                    S.bolt(g, (x, y, 0.118), (0, 0, 1), 0.007)
                    S.bolt(g, (x, y, -0.118), (0, 0, -1), 0.007)
            # a web stiffener every metre, as on the world's beams
            for k in range(1, max(length // 4, 1) + (0 if length >= 4 else 0)):
                y = -length * B / 2 + 1.0 * k
                if abs(y) < L - 0.02:
                    for s in (-1, 1):
                        box(g, (s * 0.06, y, 0), (0.047, 0.006, 0.094), "blue_dark", bevel=0.002)
        return g
    return build


def ibeam_corner():
    g = Geo()
    pts, pieces = IBEAM
    path = S.arc((B / 2, -B / 2, 0), B / 2, math.pi, math.pi / 2, n=18)
    S.sweep(g, pts, path, (0, 0, 1), "blue", pieces=pieces)
    # nudge the ends inside the footprint
    for i, v in enumerate(g.v):
        g.v[i] = (min(max(v[0], -B / 2 + E), B / 2 - E), min(max(v[1], -B / 2 + E), B / 2 - E), v[2])
    a = math.radians(135)
    p = Vector((B / 2, -B / 2, 0)) + Vector((math.cos(a), math.sin(a), 0)) * B / 2
    for z in (0.118, -0.118):
        S.bolt(g, (p.x, p.y, z), (0, 0, 1 if z > 0 else -1), 0.007)
    return g


def ibeam_end():
    g = Geo()
    pts, pieces = IBEAM
    L = B / 2 - E
    S.sweep(g, pts, [(0, -L, 0), (0, L - 0.014, 0)], (0, 0, 1), "blue", pieces=pieces)
    box(g, (0, L - 0.007, 0), (0.11, 0.007, 0.118), "blue_dark", bevel=0.003)
    for x in (-0.05, 0.05):
        for z in (-0.06, 0.06):
            S.bolt(g, (x, L - 0.014, z), (0, -1, 0), 0.008)
    for x in (-0.062, 0.062):
        S.bolt(g, (x, 0, 0.118), (0, 0, 1), 0.007)
    return g


def ibeam_holder():
    """A beam seat: a bolted foot plate, a round post with collars and a cap plate a beam sits on (1 x 2 x 1)."""
    g = Geo()
    z0, z1 = -B + E, B - E
    plate(g, (0, 0, z0 + 0.012), (0.105, 0.105, 0.012), "blue_dark")
    for x in (-0.072, 0.072):
        for y in (-0.072, 0.072):
            S.bolt(g, (x, y, z0 + 0.024), (0, 0, 1), 0.009)
    cylinder(g, (0, 0, z0 + 0.024), (0, 0, z1 - 0.024), 0.032, "blue", segs=20)
    for z, r in ((z0 + 0.04, 0.046), (z1 - 0.04, 0.046)):
        cylinder(g, (0, 0, z - 0.014), (0, 0, z + 0.014), r, "graphite", segs=6, phase=math.pi / 6)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        box(g, (0.055 * math.cos(a), 0.055 * math.sin(a), z0 + 0.046), (0.022, 0.003, 0.022), "blue",
            rot=S.Matrix.Rotation(a, 3, "Z"))
    plate(g, (0, 0, z1 - 0.02), (0.11, 0.11, 0.012), "blue_dark")
    for x in (-0.075, 0.075):
        S.bolt(g, (x, 0, z1 - 0.008), (0, 0, 1), 0.007)
    return g


# =====================================================================================================
# Columns, posts and channels
# =====================================================================================================

def column(height):
    def build():
        g = Geo()
        z0, z1 = -height * B / 2 + E, height * B / 2 - E
        S.sweep(g, S.rect(0.095, 0.095, 0.01), [(0, 0, z0 + 0.016), (0, 0, z1 - 0.016)], (0, 1, 0), "blue")
        for z, n in ((z0 + 0.008, -1), (z1 - 0.008, 1)):
            plate(g, (0, 0, z), (0.112, 0.112, 0.008), "graphite", bevel=0.003)
            for x in (-0.085, 0.085):
                for y in (-0.085, 0.085):
                    S.bolt(g, (x, y, z - n * 0.008), (0, 0, -n), 0.006)
        # a splice band every metre
        for k in range(1, int(height * B)):
            z = z0 + k * 1.0
            S.sweep(g, S.rect(0.099, 0.099, 0.012), [(0, 0, z - 0.03), (0, 0, z + 0.03)], (0, 1, 0), "blue_dark")
        return g
    return build


def support_pillar():
    """Our 2 x 8 x 2 post: a square box column on a gusseted foot plate, a cap plate, a mustard band low down."""
    g = Geo()
    z0, z1 = -1.0 + E, 1.0 - E
    S.sweep(g, S.rect(0.16, 0.16, 0.014), [(0, 0, z0 + 0.03), (0, 0, z1 - 0.03)], (0, 1, 0), "blue")
    for z, n in ((z0 + 0.015, -1), (z1 - 0.015, 1)):
        plate(g, (0, 0, z), (0.24, 0.24, 0.015), "graphite", bevel=0.005)
        for x in (-0.2, 0.2):
            for y in (-0.2, 0.2):
                S.bolt(g, (x, y, z - n * 0.015), (0, 0, -n), 0.012)
        for k in range(4):
            a = k * math.pi / 2
            box(g, (0.195 * math.cos(a), 0.195 * math.sin(a), z - n * 0.06), (0.035, 0.004, 0.045), "blue_dark",
                rot=S.Matrix.Rotation(a, 3, "Z"))
    S.sweep(g, S.rect(0.163, 0.163, 0.015), [(0, 0, z0 + 0.28), (0, 0, z0 + 0.37)], (0, 1, 0), "mustard")
    return g


def u_beam():
    """A 1 x 6 x 2 channel standing up, open to the front, twelve bolts down its back."""
    g = Geo()
    pts, pieces = S.u_profile(0.112, 0.235, 0.022)
    z0, z1 = -0.75 + E, 0.75 - E
    S.sweep(g, pts, [(0, 0, z0), (0, 0, z1)], (0, 1, 0), "blue", pieces=pieces)
    for k in range(6):
        z = z0 + 0.125 + k * 0.25
        for x in (-0.05, 0.05):
            S.bolt(g, (x, -0.235 + 0.022, z), (0, 1, 0), 0.008)
    for z in (z0 + 0.012, z1 - 0.012):
        box(g, (0, -0.02, z), (0.09, 0.19, 0.008), "blue_dark", bevel=0.002)
    return g


def metal_support():
    """A 2 x 4 x 2 box post: three channel walls open to the front, stiffener plates and eight bolts."""
    g = Geo()
    pts, pieces = S.u_profile(0.23, 0.23, 0.026)
    z0, z1 = -0.5 + E, 0.5 - E
    S.sweep(g, pts, [(0, 0, z0 + 0.024), (0, 0, z1 - 0.024)], (0, 1, 0), "blue", pieces=pieces)
    for z in (z0 + 0.012, z1 - 0.012):
        plate(g, (0, 0, z), (0.245, 0.245, 0.012), "graphite", bevel=0.004)
    for z in (-0.17, 0.17):
        box(g, (0, 0.0, z), (0.2, 0.2, 0.01), "blue_dark", bevel=0.003)
    for s in (-1, 1):
        for z in (-0.33, -0.11, 0.11, 0.33):
            S.bolt(g, (s * 0.23, -0.1, z), (s, 0, 0), 0.011)
    return g


def support_bracket():
    """A 1 x 2 x 2 wall bracket: a back plate, an arm along the top and a diagonal strut, all bolted."""
    g = Geo()
    y0, y1, z0, z1 = -0.25 + E, 0.25 - E, -0.25 + E, 0.25 - E
    plate(g, (0, y0 + 0.012, 0), (0.1, 0.012, 0.24), "blue_dark")
    pts, pieces = S.u_profile(0.08, 0.035, 0.012)
    S.sweep(g, pts, [(0, y0 + 0.024, z1 - 0.035), (0, y1, z1 - 0.035)], (0, 0, -1), "blue", pieces=pieces)
    a, b = Vector((0, y0 + 0.03, z0 + 0.06)), Vector((0, y1 - 0.05, z1 - 0.075))
    cylinder(g, a, b, 0.018, "zinc", segs=14)
    for p, n in ((a, Vector((0, 1, 0))), (b, Vector((0, 0, -1)))):
        cylinder(g, p - (b - a).normalized() * 0.01, p + (b - a).normalized() * 0.02, 0.026, "graphite", segs=6)
    for z in (z0 + 0.05, 0.0, z1 - 0.1):
        for x in (-0.06, 0.06):
            S.bolt(g, (x, y0 + 0.024, z), (0, 1, 0), 0.008)
    return g


def stand_support():
    """A 2 x 1 x 2 round foot: a mustard drum with a graphite rim and a bone pad on top."""
    g = Geo()
    z0, z1 = -B / 2 + E, B / 2 - E
    lathe(g, [(0.0, z0), (0.235, z0), (0.245, z0 + 0.012), (0.245, z0 + 0.03), (0.235, z0 + 0.03), (0.0, z0 + 0.03)], "graphite", steps=40)
    lathe(g, [(0.0, z0 + 0.03), (0.225, z0 + 0.03), (0.228, z1 - 0.03), (0.0, z1 - 0.03)], "mustard", steps=40)
    lathe(g, [(0.0, z1 - 0.03), (0.24, z1 - 0.03), (0.24, z1 - 0.012), (0.205, z1 - 0.012), (0.0, z1 - 0.012)], "graphite", steps=40)
    lathe(g, [(0.0, z1 - 0.012), (0.19, z1 - 0.012), (0.195, z1 - 0.004), (0.0, z1 - 0.004)], "bone", steps=40)
    S.bolt_ring(g, (0, 0, z1 - 0.012), (0, 0, 1), 0.222, 8, 0.008)
    for k in range(3):
        a = k * 2 * math.pi / 3
        box(g, (0.229 * math.cos(a), 0.229 * math.sin(a), -0.005), (0.004, 0.03, 0.05), "graphite",
            rot=S.Matrix.Rotation(a, 3, "Z"))
    return g


def lathe(g, profile, mat, steps=40):
    return S.K.lathe(g, profile, mat, steps=steps)


# =====================================================================================================
# Lattice frames, trusses and the tower mast
# =====================================================================================================

def gusset(g, c, n, size, mat="blue_dark"):
    n = Vector(n)
    hu = size if abs(n.x) < 0.5 else 0.004
    hv = size if abs(n.y) < 0.5 else 0.004
    hw = size if abs(n.z) < 0.5 else 0.004
    box(g, c, (hu, hv, hw), mat, bevel=0.002)


def truss(width, height):
    """A flat lattice frame `width` x `height` blocks, one block deep: square-tube chords and posts, Warren diagonals,
    gusset plates and bolts at every node (in the x-z plane)."""
    def build():
        g = Geo()
        W, H = width * B / 2 - E, height * B / 2 - E
        t = 0.04
        for z in (-H + t, H - t):
            bar(g, (-W, 0, z), (W, 0, z), t, t, "blue", up=(0, 1, 0), chamfer=0.006)
        panels = max(1, round(width / height)) if width >= height else 1
        xs = [(-W + t) + (2 * (W - t)) * k / panels for k in range(panels + 1)]
        for x in xs:
            bar(g, (x, 0, -H + 2 * t), (x, 0, H - 2 * t), t * 0.9, t * 0.9, "blue", up=(0, 1, 0), chamfer=0.006)
        for k in range(panels):
            a, b = xs[k], xs[k + 1]
            p0, p1 = (a + t, 0, -H + 2 * t), (b - t, 0, H - 2 * t)
            q0, q1 = (a + t, 0, H - 2 * t), (b - t, 0, -H + 2 * t)
            bar(g, p0, p1, 0.018, 0.024, "blue_dark", up=(0, 1, 0))
            if height >= 8 or k % 2 == 1 or panels == 1:
                bar(g, q0, q1, 0.018, 0.024, "blue_dark", up=(0, 1, 0))
        if height >= 8:
            bar(g, (-W + 2 * t, 0, 0), (W - 2 * t, 0, 0), t * 0.8, t * 0.8, "blue", up=(0, 1, 0), chamfer=0.005)
        gs = 0.065
        for x in xs:
            for z in (-H + t, H - t):
                gx = min(max(x, -W + gs), W - gs)
                gz = min(max(z, -H + gs), H - gs)
                for s in (-1, 1):
                    gusset(g, (gx, s * (t + 0.004), gz), (0, 1, 0), gs)
                    S.bolt(g, (gx - 0.03, s * (t + 0.008), gz), (0, s, 0), 0.007)
                    S.bolt(g, (gx + 0.03, s * (t + 0.008), gz), (0, s, 0), 0.007)
        return g
    return build


def lattice_pillar():
    """Scrap Mechanic's 5 x 5 x 5 Support Pillar: four box posts, X-braced sides, girts and a bone deck on top."""
    g = Geo()
    h = 0.625 - E
    p = 0.55
    for x in (-p, p):
        for y in (-p, p):
            bar(g, (x, y, -h + 0.03), (x, y, h - 0.064), 0.055, 0.055, "blue", up=(0, 1, 0), chamfer=0.008)
            plate(g, (x, y, -h + 0.015), (0.075, 0.075, 0.015), "graphite")
            for sx in (-1, 1):
                S.bolt(g, (x + sx * 0.055, y + sx * 0.055, -h + 0.03), (0, 0, 1), 0.008)
    for side in range(4):
        a = side * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0))
        s = Vector((-d.y, d.x, 0))
        for z in (-h + 0.1, h - 0.15):
            bar(g, d * p + s * -(p - 0.055) + Vector((0, 0, z)), d * p + s * (p - 0.055) + Vector((0, 0, z)), 0.04, 0.04,
                "blue", up=(0, 0, 1), chamfer=0.006)
        for sgn in (-1, 1):
            a0 = d * p + s * (-sgn * (p - 0.06)) + Vector((0, 0, -h + 0.14))
            a1 = d * p + s * (sgn * (p - 0.06)) + Vector((0, 0, h - 0.19))
            bar(g, a0, a1, 0.032, 0.01, "blue_dark", up=d)
        gusset(g, d * (p + 0.012) + Vector((0, 0, 0.0)), d, 0.07)
        S.bolt(g, d * (p + 0.016), d, 0.01)
    top = h - 0.014
    plate(g, (0, 0, top - 0.025), (h, h, 0.025), "blue_dark", bevel=0.008)
    box(g, (0, 0, top - 0.002), (h - 0.06, h - 0.06, 0.005), "bone", bevel=0.003)
    for x in (-h + 0.03, h - 0.03):
        for y in (-h + 0.03, h - 0.03):
            S.bolt(g, (x, y, top), (0, 0, 1), 0.014)
    return g


def support_pillar_stand():
    """A 7 x 1 x 7 foundation: a chamfered graphite slab, a steel-blue plinth, a bone deck, big corner bolts."""
    g = Geo()
    h = 0.875 - E
    z0, z1 = -B / 2 + E, B / 2 - E
    box(g, (0, 0, z0 + 0.05), (h, h, 0.05), "graphite", bevel=0.02)
    box(g, (0, 0, (z0 + 0.1 + z1 - 0.02) / 2), (0.69, 0.69, (z1 - 0.02 - z0 - 0.1) / 2), "blue", bevel=0.015)
    box(g, (0, 0, z1 - 0.012), (0.64, 0.64, 0.012), "bone", bevel=0.006)
    for x in (-h + 0.09, h - 0.09):
        for y in (-h + 0.09, h - 0.09):
            S.bolt(g, (x, y, z0 + 0.1), (0, 0, 1), 0.03)
    for side in range(4):
        a = side * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0))
        for k in (-1, 0, 1):
            s = Vector((-d.y, d.x, 0))
            S.bolt(g, d * 0.69 + s * k * 0.4 + Vector((0, 0, 0.0)), d, 0.012)
    return g


def tower_pole(height, top=False):
    """A square lattice mast, 2 x 2 blocks, painted in signal-red and bone half-metre bands (aviation marking).
    The top section tapers to a cap with an unlit warning lamp."""
    def build():
        g = Geo()
        z0, z1 = -height * B / 2 + E, height * B / 2 - E
        span = z1 - z0
        r0 = 0.2
        r1 = 0.07 if top else r0
        ztop = z1 - (0.09 if top else 0.0)

        def leg(sx, sy, z):
            f = (z - z0) / (ztop - z0)
            r = r0 + (r1 - r0) * f
            return Vector((sx * r, sy * r, z))

        legs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
        bands = max(1, round(span / 0.5))
        for sx, sy in legs:
            for k in range(bands):
                za, zb = z0 + (ztop - z0) * k / bands, z0 + (ztop - z0) * (k + 1) / bands
                cylinder(g, leg(sx, sy, za), leg(sx, sy, zb), 0.021, "signal" if k % 2 == 0 else "bone", segs=12)
        levels = max(2, round(span / 0.25))
        for k in range(levels + 1):
            z = z0 + 0.02 + (ztop - z0 - 0.04) * k / levels
            for i in range(4):
                a, b = leg(*legs[i], z), leg(*legs[(i + 1) % 4], z)
                cylinder(g, a, b, 0.009, "steel", segs=8)
            if k < levels:
                zn = z0 + 0.02 + (ztop - z0 - 0.04) * (k + 1) / levels
                for i in range(4):
                    if (i + k) % 2:
                        a, b = leg(*legs[i], z), leg(*legs[(i + 1) % 4], zn)
                    else:
                        a, b = leg(*legs[(i + 1) % 4], z), leg(*legs[i], zn)
                    cylinder(g, a, b, 0.007, "steel", segs=8)
        for sx, sy in legs:
            p = leg(sx, sy, z0)
            cylinder(g, p, p + Vector((0, 0, 0.02)), 0.036, "graphite", segs=6, phase=math.pi / 6)
            if not top:
                q = leg(sx, sy, z1)
                cylinder(g, q - Vector((0, 0, 0.02)), q, 0.036, "graphite", segs=6, phase=math.pi / 6)
        if top:
            box(g, (0, 0, ztop + 0.01), (0.1, 0.1, 0.012), "graphite", bevel=0.004)
            cylinder(g, (0, 0, ztop + 0.02), (0, 0, ztop + 0.05), 0.03, "graphite", segs=16)
            S.K.lathe(g, [(0.0, ztop + 0.05), (0.034, ztop + 0.05), (0.03, ztop + 0.075), (0.016, z1 - 0.003), (0.0, z1)], "lens", steps=20)
            for k in range(4):
                a = k * math.pi / 2 + math.pi / 4
                cylinder(g, (0.032 * math.cos(a), 0.032 * math.sin(a), ztop + 0.05),
                         (0.022 * math.cos(a), 0.022 * math.sin(a), z1 - 0.004), 0.003, "graphite", segs=6)
        return g
    return build


# =====================================================================================================
# The batches
# =====================================================================================================

def parts_8a():
    P = S.Part
    return [
        P("ibeam_stub", (1, 1, 1), ibeam_run(1), "I-Beam (Stub) (1 x 1 x 1 blocks)",
          ["New: Scrap Mechanic's 1-block Short I-Beam. Steel-blue primer, bolts through both flanges."]),
        P("ibeam_short", (1, 1, 4), ibeam_run(4), "I-Beam (Short) (1 x 1 x 4 blocks)",
          ["Ours, size kept (1 m): Scrap Mechanic's Long I-Beam length. Real model replaces the placeholder."]),
        P("ibeam_long", (1, 1, 8), ibeam_run(8), "I-Beam (Long) (1 x 1 x 8 blocks)",
          ["Ours, size kept (2 m). Web stiffeners every metre."]),
        P("ibeam_corner", (1, 1, 1), ibeam_corner, "I-Beam (Corner) (1 x 1 x 1 blocks)",
          ["New: a quarter-round bend joining two beams at right angles, flanges level."]),
        P("ibeam_end", (1, 1, 1), ibeam_end, "I-Beam (End) (1 x 1 x 1 blocks)",
          ["New: a beam end closed by a bolted plate (four bolts inside, as in Scrap Mechanic)."]),
        P("ibeam_holder", (1, 2, 1), ibeam_holder, "I-Beam (Holder) (1 x 2 x 1 blocks)",
          ["New: a bolted foot plate, a round post with nut collars and a cap plate for a beam to sit on."]),
        P("metal_column", (1, 8, 1), column(8), "Metal Column (1 x 8 x 1 blocks)",
          ["Ours, size kept (2 m). Square box column, bolted end plates, a splice band every metre."]),
        P("support_pillar", (2, 8, 2), support_pillar, "Support Pillar (2 x 8 x 2 blocks)",
          ["Ours, size kept: a box post on gusseted, bolted foot and cap plates, a mustard band low down."]),
        P("support_bracket", (1, 2, 2), support_bracket, "Support Bracket (1 x 2 x 2 blocks)",
          ["Ours (Scrap Mechanic's Small Steel Bracket): back plate, top arm and a zinc diagonal strut."]),
        P("support_truss_large", (8, 8, 1), truss(8, 8), "Large Truss (8 x 8 x 1 blocks)",
          ["New: Scrap Mechanic's Large Support Structure. Square-tube frame, X-braced, gussets bolted both sides."]),
    ]


def parts_8b():
    P = S.Part
    return [
        P("metal_column_short", (1, 5, 1), column(5), "Metal Column (Short) (1 x 5 x 1 blocks)",
          ["New: Scrap Mechanic's 5-block Metal Column, beside our 8-block one."]),
        P("lattice_pillar", (5, 5, 5), lattice_pillar, "Lattice Pillar (5 x 5 x 5 blocks)",
          ["New: Scrap Mechanic's Support Pillar (a 5x5x5 lattice). Stacks on the Pillar Stand."]),
        P("support_pillar_stand", (7, 1, 7), support_pillar_stand, "Pillar Stand (7 x 1 x 7 blocks)",
          ["New: the foundation under a Lattice Pillar (or our Support Pillar). Big corner bolts."]),
        P("metal_support", (2, 4, 2), metal_support, "Metal Support (2 x 4 x 2 blocks)",
          ["New: a heavy box post, three channel walls open to the front, eight side bolts."]),
        P("stand_support", (2, 1, 2), stand_support, "Stand Support (2 x 1 x 2 blocks)",
          ["New: Scrap Mechanic's Stand Support A. A round mustard foot with a bone pad."]),
        P("support_truss_small", (4, 4, 1), truss(4, 4), "Small Truss (4 x 4 x 1 blocks)",
          ["New: Scrap Mechanic's Small Support Structure. One braced panel."]),
        P("support_truss", (8, 4, 1), truss(8, 4), "Truss (8 x 4 x 1 blocks)",
          ["New: Scrap Mechanic's Support Structure. Two braced panels."]),
        P("tower_pole", (2, 8, 2), tower_pole(8), "Tower Pole (2 x 8 x 2 blocks)",
          ["New: a lattice mast section in signal-red and bone aviation bands. Stacks end to end."]),
        P("tower_pole_top", (2, 4, 2), tower_pole(4, top=True), "Tower Pole Top (2 x 4 x 2 blocks)",
          ["New: the tapered top section with a cap plate and an unlit red warning lamp."]),
        P("u_beam", (1, 6, 2), u_beam, "U-Beam (1 x 6 x 2 blocks)",
          ["New: a standing channel open to the front, twelve bolts down its back."]),
    ]
