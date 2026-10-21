"""Batch 10a and 10b of the parts map: ducts, tubes, conduits and cables (Colony Works services).

* Duct: galvanised square air duct, 1 x 1 block, a seamed half collar at every face so runs join on one seam.
* Metal Tube: a 1 x 1 x 2 galvanised round tube with bolted end rings and a bone band.
* Conduit: four mustard tubes in a steel-blue channel tray (2 blocks wide, 1 high), strapped every block.
* Cable: three rubber cables on a bone plate, clipped every block; bends turn in the plate's plane, round an inside
  corner (floor to wall) and over an outside edge.

Built by build_structural.py.
"""

from __future__ import annotations

import math

import structural_kit as S
from mathutils import Matrix, Vector

Geo, box, cylinder = S.Geo, S.box, S.cylinder
B = S.BLOCK
E = S.EPS
H = B / 2 - E


# =====================================================================================================
# Ducts
# =====================================================================================================

DO, DI = 0.104, 0.097          # duct half size outside and inside
DC, DCT = 0.119, 0.012         # seam collar half size and thickness (half a seam at each face)


def duct_collar(g, face, d):
    face, d = Vector(face), Vector(d)
    S.sweep_hollow(g, S.rect(DC, DC, 0.004), _rect8(DI), [face - d * DCT, face],
                   S._up_for(d), "zinc_dark", inner_mat="hole")


def _rect8(h):
    """A square as eight points (to pair with a chamfered square in sweep_hollow)."""
    c = 0.0005
    return [(-h + c, -h), (h - c, -h), (h, -h + c), (h, h - c), (h - c, h), (-h + c, h), (-h, h - c), (-h, -h + c)]


def duct_run(g, a, b, rims=(True, True)):
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    S.sweep_hollow(g, S.rect(DO, DO), S.rect(DI, DI), [a, b], S._up_for(d), "zinc", inner_mat="hole", rims=rims)


def duct_straight(length):
    def build():
        g = Geo()
        L = length * B / 2 - E
        duct_run(g, (0, -L + DCT, 0), (0, L - DCT, 0))
        for s in (-1, 1):
            duct_collar(g, (0, s * L, 0), (0, s, 0))
        # stiffening beads across each face
        for k in range(length):
            y = -L + (k + 0.5) * 2 * L / length
            for ax in ((1, 0, 0), (0, 0, 1)):
                for sgn in (-1, 1):
                    c = Vector(ax) * sgn * (DO + 0.001) + Vector((0, y, 0))
                    hx, hz = (0.002, 0.08) if ax[0] else (0.08, 0.002)
                    box(g, tuple(c), (hx, 0.006, hz), "zinc_dark")
        return g
    return build


def duct_corner():
    g = Geo()
    c = Vector((B / 2, -B / 2, 0))
    R = B / 2
    da = DCT / R
    S.sweep_hollow(g, S.rect(DO, DO), S.rect(DI, DI), S.arc(c, R, math.pi - da, math.pi / 2 + da, n=14), (0, 0, 1), "zinc")
    duct_collar(g, (0, -H, 0), (0, -1, 0))
    duct_collar(g, (H, 0, 0), (1, 0, 0))
    return g


def duct_tee():
    g = Geo()
    duct_run(g, (0, -H + DCT, 0), (0, H - DCT, 0))
    duct_run(g, (DO - 0.002, 0, 0), (H - DCT, 0, 0), rims=(False, True))
    for d in ((0, -1, 0), (0, 1, 0), (1, 0, 0)):
        duct_collar(g, Vector(d) * H, d)
    return g


def duct_end():
    """A vent terminal: a duct stub from the back face into a louvred hood."""
    g = Geo()
    duct_collar(g, (0, -H, 0), (0, -1, 0))
    duct_run(g, (0, -H + DCT, 0), (0, -0.02, 0), rims=(False, False))
    box(g, (0, 0.03, 0), (0.115, 0.06, 0.115), "zinc", bevel=0.008)
    box(g, (0, 0.093, 0), (0.1, 0.004, 0.1), "hole")
    for k in range(6):
        z = -0.085 + k * 0.034
        box(g, (0, 0.096, z), (0.098, 0.016, 0.004), "zinc_dark", rot=Matrix.Rotation(math.radians(-35), 3, "X"))
    for x in (-0.1, 0.1):
        for z in (-0.1, 0.1):
            S.bolt(g, (x, 0.09, z), (0, 1, 0), 0.006)
    return g


def duct_holder():
    """A hanger: a square collar of angle iron round the duct, two threaded rods up to a bolted ceiling plate."""
    g = Geo()
    w = 0.03
    for z in (-DO - 0.004, DO + 0.004):
        box(g, (0, 0, z), (DO + 0.008, w, 0.004), "blue", bevel=0.0015)
    for x in (-DO - 0.004, DO + 0.004):
        box(g, (x, 0, 0), (0.004, w, DO + 0.008), "blue", bevel=0.0015)
    for x in (-0.113, 0.113):
        cylinder(g, (x, 0, DO + 0.003), (x, 0, H - 0.012), 0.005, "steel", segs=8)
        S.hexnut(g, (x, 0, DO + 0.014), (0, 0, 1), 0.0085, 0.008, "zinc")
    box(g, (0, 0, H - 0.006), (0.12, 0.05, 0.006), "graphite", bevel=0.002)
    for x in (-0.09, 0.09):
        S.bolt(g, (x, 0, H - 0.012), (0, 0, -1), 0.007)
    duct_run(g, (0, -H, 0), (0, H, 0))
    return g


def metal_tube():
    g = Geo()
    L = B - E
    r = 0.098
    S.pipe_run(g, (0, -L + 0.014, 0), (0, L - 0.014, 0), r, "zinc", wall=0.01, rims=(False, False))
    for s in (-1, 1):
        S.flange(g, (0, s * L, 0), (0, s, 0), r - 0.01, 0.116, 0.014, "zinc_dark", bolts=6, bolt_r=0.006)
        S.grate(g, (0, s * L, 0), (0, s, 0), r - 0.01, bars=2, depth=0.03)
    S.band(g, (0, 0.06, 0), (0, 1, 0), r + 0.0015, 0.08, "bone")
    S.band(g, (0, -0.12, 0), (0, 1, 0), r + 0.0015, 0.02, "graphite")
    return g


# =====================================================================================================
# Conduits: four tubes in a tray (2 wide, 1 high)
# =====================================================================================================

TX = (-0.15, -0.05, 0.05, 0.15)     # the tubes' offsets across the tray
TR = 0.038
TZ = -0.125 + 0.016 + TR + 0.002
TRAY = S.u_profile(0.244, 0.05, 0.012)


def tray_run(g, a, b):
    a, b = Vector(a), Vector(b)
    pts, pieces = TRAY
    S.sweep(g, pts, [a + Vector((0, 0, -0.125 + 0.05 + E)), b + Vector((0, 0, -0.125 + 0.05 + E))], (0, 0, 1), "blue",
            pieces=pieces)


def strap(g, c, d):
    """A graphite strap across the tubes at c (d along the run), bolted to the tray's legs."""
    c, d = Vector(c), Vector(d).normalized()
    side = d.cross(Vector((0, 0, 1)))
    m = Matrix((side, d, (0, 0, 1))).transposed()
    box(g, tuple(c + Vector((0, 0, TZ + TR + 0.004))), (0.235, 0.014, 0.004), "graphite", bevel=0.002, rot=m)
    for s in (-1, 1):
        S.bolt(g, c + side * s * 0.215 + Vector((0, 0, TZ + TR + 0.008)), (0, 0, 1), 0.006)


def tubes_straight(length):
    def build():
        g = Geo()
        L = length * B / 2 - E
        tray_run(g, (0, -L, 0), (0, L, 0))
        for x in TX:
            S.pipe_run(g, (x, -L, TZ), (x, L, TZ), TR, "mustard", wall=0.008)
        for k in range(length):
            strap(g, (0, -L + (k + 0.5) * 2 * L / length, 0), (0, 1, 0))
        return g
    return build


def tubes_corner():
    g = Geo()
    h = B - E
    zf = -0.125 + E
    box(g, (0, 0, zf + 0.006), (h, h, 0.006), "blue", bevel=0.002)
    for wall in (((-h + 0.006, 0, zf + 0.05), (0.006, h, 0.05)), ((0, h - 0.006, zf + 0.05), (h, 0.006, 0.05))):
        box(g, wall[0], wall[1], "blue", bevel=0.002)
    box(g, (h - 0.03, -h + 0.03, zf + 0.05), (0.03, 0.03, 0.05), "blue", bevel=0.004)
    c = Vector((B, -B, TZ))
    for x in TX:
        R = B - x
        da = 0.0005
        S.pipe_elbow(g, c, R, TR, math.pi - da, math.pi / 2 + da, "z", "mustard", (0, 0, 1), wall=0.008, steps=18)
    a = math.radians(135)
    strap(g, (B + math.cos(a) * B, -B + math.sin(a) * B, 0), (math.sin(a), -math.cos(a), 0))
    return g


def tubes_tee():
    g = Geo()
    h = B - E
    zf = -0.125 + E
    box(g, (0, 0, zf + 0.006), (h, h, 0.006), "blue", bevel=0.002)
    box(g, (-h + 0.006, 0, zf + 0.05), (0.006, h, 0.05), "blue", bevel=0.002)
    for x in TX[:2]:
        S.pipe_run(g, (x, -h, TZ), (x, h, TZ), TR, "mustard", wall=0.008)
    c = Vector((B, -B, TZ))
    for x in TX[2:]:
        S.pipe_elbow(g, c, B - x, TR, math.pi, math.pi / 2, "z", "mustard", (0, 0, 1), wall=0.008, steps=18)
    for x in TX[2:]:
        S.pipe_run(g, (x, h - 0.12, TZ), (x, h, TZ), TR * 0.6, "graphite", wall=0.006)
    strap(g, (0, -0.12, 0), (0, 1, 0))
    strap(g, (0.12, 0, 0), (1, 0, 0))
    return g


# =====================================================================================================
# Cables: three rubber cables on a plate (1 x 1)
# =====================================================================================================

CX = (-0.06, 0.0, 0.06)
CR = 0.021
PT = 0.012                      # plate thickness
CZ = -H + PT + CR               # cable centre height above the plate


def clip(g, c, d, h=CZ):
    c, d = Vector(c), Vector(d).normalized()
    side = d.cross(Vector((0, 0, 1)))
    m = Matrix((side, d, (0, 0, 1))).transposed()
    box(g, tuple(c + Vector((0, 0, h + CR + 0.003))), (0.095, 0.01, 0.003), "zinc", bevel=0.001, rot=m)
    for s in (-1, 1):
        box(g, tuple(c + side * s * 0.092 + Vector((0, 0, (h + CR - H) / 2 + 0.003))), (0.003, 0.01, (h + CR + H) / 2), "zinc", rot=m)
        S.bolt(g, c + side * s * 0.105 + Vector((0, 0, -H + PT)), (0, 0, 1), 0.005)


def wires_straight(length):
    def build():
        g = Geo()
        L = length * B / 2 - E
        box(g, (0, 0, -H + PT / 2), (0.115, L, PT / 2), "bone", bevel=0.002)
        for x in CX:
            S.sweep(g, S.circle(CR, 12), [(x, -L, CZ), (x, L, CZ)], (0, 0, 1), "rubber")
        for k in range(length):
            clip(g, (0, -L + (k + 0.5) * 2 * L / length, 0), (0, 1, 0))
        return g
    return build


def wires_bend():
    g = Geo()
    c = Vector((B / 2, -B / 2, CZ))
    box(g, (0, 0, -H + PT / 2), (0.115, 0.115, PT / 2), "bone", bevel=0.002)
    box(g, (0.12, -0.0, -H + PT / 2), (0.005, 0.115, PT / 2), "bone")
    box(g, (0, -0.12, -H + PT / 2), (0.115, 0.005, PT / 2), "bone")
    for x in CX:
        R = B / 2 - x
        S.sweep(g, S.circle(CR, 12), S.arc(c, R, math.pi, math.pi / 2, n=14), (0, 0, 1), "rubber")
    a = math.radians(135)
    clip(g, (B / 2 + math.cos(a) * B / 2, -B / 2 + math.sin(a) * B / 2, 0), (math.sin(a), -math.cos(a), 0))
    return g


def wires_concave():
    """Along the floor plate and up the wall plate at the back, round the inside corner."""
    g = Geo()
    box(g, (0, 0, -H + PT / 2), (0.115, H, PT / 2), "bone", bevel=0.002)
    box(g, (0, H - PT / 2, 0), (0.115, PT / 2, H), "bone", bevel=0.002)
    R = 0.07
    yw = H - PT - CR
    c = Vector((0, yw - R, CZ + R))
    for x in CX:
        path = [Vector((x, -H, CZ))] + [p + Vector((x, 0, 0)) for p in S.arc(c, R, -math.pi / 2, 0, n=12, axis="x")] + \
               [Vector((x, yw, H))]
        S.sweep(g, S.circle(CR, 12), path, (1, 0, 0), "rubber")
    clip(g, (0, -0.06, 0), (0, 1, 0))
    return g


def wires_convex():
    """Over an outside edge: a guard block rounded on its front top edge, the cables lying over it."""
    g = Geo()
    R = 0.1
    top, front = H - 2 * CR - 0.002, H - 2 * CR - 0.002
    prof = [(-H, -H), (front, -H), (front, top - R)]
    for k in range(1, 8):
        a = k / 8 * math.pi / 2
        prof.append((front - R + R * math.cos(a), top - R + R * math.sin(a)))
    prof += [(front - R, top), (-H, top)]
    S.sweep(g, prof, [(-0.115, 0, 0), (0.115, 0, 0)], (0, 0, 1), "bone")
    for x in CX:
        c = Vector((x, front - R, top - R))
        path = [Vector((x, -H, top + CR))] + S.arc(c, R + CR, math.pi / 2, 0, n=12, axis="x") + [Vector((x, front + CR, -H))]
        S.sweep(g, S.circle(CR, 12), path, (1, 0, 0), "rubber")
    for x in (-0.1, 0.1):
        S.bolt(g, (x, -0.08, top), (0, 0, 1), 0.006)
    return g


# =====================================================================================================
# The batches
# =====================================================================================================

def parts_10a():
    P = S.Part
    return [
        P("duct_short", (1, 1, 1), duct_straight(1), "Duct (Short) (1 x 1 x 1 blocks)",
          ["Galvanised square duct, a half seam collar at each face so runs join on one seam."]),
        P("duct_long", (1, 1, 2), duct_straight(2), "Duct (Long) (1 x 1 x 2 blocks)", ["Two blocks of duct."]),
        P("duct_corner", (1, 1, 1), duct_corner, "Duct (Corner) (1 x 1 x 1 blocks)", ["A swept 90-degree bend."]),
        P("duct_tee", (1, 1, 1), duct_tee, "Duct (Tee) (1 x 1 x 1 blocks)", ["A branch to the right."]),
        P("duct_end", (1, 1, 1), duct_end, "Duct (End) (1 x 1 x 1 blocks)", ["A louvred vent at the end of a run."]),
        P("duct_holder", (1, 1, 1), duct_holder, "Duct (Holder) (1 x 1 x 1 blocks)",
          ["A block of duct hung from a ceiling plate on two rods and a steel-blue collar."]),
        P("metal_tube", (1, 1, 2), metal_tube, "Metal Tube (1 x 1 x 2 blocks)",
          ["Scrap Mechanic's Metallic Tube Straight: a round galvanised tube, bolted end rings, a bone band."]),
        P("tubes_short", (2, 1, 2), tubes_straight(2), "Conduit (Short) (2 x 1 x 2 blocks)",
          ["Four mustard tubes in a steel-blue channel tray, a strap every block."]),
    ]


def parts_10b():
    P = S.Part
    return [
        P("tubes_long", (2, 1, 4), tubes_straight(4), "Conduit (Long) (2 x 1 x 4 blocks)", ["Four blocks of conduit."]),
        P("tubes_corner", (2, 1, 2), tubes_corner, "Conduit (Corner) (2 x 1 x 2 blocks)",
          ["The tubes turn a right angle in a corner tray."]),
        P("tubes_tee", (2, 1, 2), tubes_tee, "Conduit (Tee) (2 x 1 x 2 blocks)",
          ["Two tubes run on, two turn off to the right."]),
        P("wires_short", (1, 1, 1), wires_straight(1), "Cable (Short) (1 x 1 x 1 blocks)",
          ["Three rubber cables on a bone plate under a zinc clip."]),
        P("wires_long", (1, 1, 4), wires_straight(4), "Cable (Long) (1 x 1 x 4 blocks)", ["Four blocks of cable run."]),
        P("wires_bend", (1, 1, 1), wires_bend, "Cable (Bend) (1 x 1 x 1 blocks)", ["The cables turn a right angle on the plate."]),
        P("wires_concave_bend", (1, 1, 1), wires_concave, "Cable (Inner Bend) (1 x 1 x 1 blocks)",
          ["From the floor up the wall, round an inside corner."]),
        P("wires_convex_bend", (1, 1, 1), wires_convex, "Cable (Outer Bend) (1 x 1 x 1 blocks)",
          ["Over an outside edge on a rounded guard."]),
    ]
