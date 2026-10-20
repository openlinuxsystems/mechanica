"""Colony Works props for the scenes between places (docs/world-generation.md, Scenes): the old colony's roadside power,
transport and utility hardware, left standing or broken where it was when the colony fell (docs/art-direction.md).

Power: a lattice pylon tall enough to read from the air, a concrete pole with its junction box, a transformer cabinet,
a generator trailer and a rack of gas cylinders. Road: a broken street lamp, a bus shelter and a torn billboard.
Freight: a shipping container, shut and opened. Fuel and signal: a fuel tank on its saddles, a satellite dish and a
guyed radio mast with its hut.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import kit as K
import frame as FR
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, RUST, ZINC, CONCRETE, shell_panel
from props import _root, wood, bolt

GLAZE = "#6f8a86"       # glazed insulator porcelain
GLASS = "#5f7880"       # old shelter and lamp glass, gone grey-blue with dirt
GLASS_D = "#2c3a42"     # dark glass behind a cover
TYRE = "#26282a"
CABLE = "#2b2d30"


def _concrete(cracks=0.15):
    return K.mat("concrete", CONCRETE, rough=0.9, wear=0.5, grime=0.6, cracks=cracks, moss=0.15)


def _steel():
    return M(STEEL, metal=0.4, wear=0.5, grime=0.4)


def _dark():
    return M(GRAPHITE, metal=0.4, wear=0.4)


def _zinc():
    return M(ZINC, metal=0.6, wear=0.5, grime=0.4)


def stencil(P, text, loc, size, mat, rot=(90, 0, 0), extrude=0.006):
    """A stencilled label with its curves kept coarse: painted letters, not a font specimen."""
    ob = FR.label(P, text, loc, size, mat, rot=rot, extrude=extrude)
    ob.data.resolution_u = 2
    return ob


def nut(P, loc, r=0.03, rot=(0, 0, 0)):
    """A hex nut or bolt head (cheaper than a screw where there are many)."""
    return K.cyl("nut", r, 0.035, _zinc(), loc=loc, rot=rot, segs=6, bev=0, parent=P)


def beam(P, name, a, b, w, mat, d=None, bev=0.0):
    """A straight member of section w x d from point a to point b (lattice members, braces, guy wires)."""
    a, b = Vector(a), Vector(b)
    v = b - a
    ob = K.box(name, (w, d or w, v.length), mat, loc=(a + b) / 2, bev=bev, parent=P)
    up = "X" if abs(v.normalized().y) > 0.95 else "Y"
    ob.rotation_euler = v.to_track_quat("Z", up).to_euler()
    return ob


def rod(P, name, a, b, r, mat, segs=8):
    """A round bar or pipe from point a to point b."""
    a, b = Vector(a), Vector(b)
    v = b - a
    ob = K.cyl(name, r, v.length, mat, loc=(a + b) / 2, segs=segs, bev=0, parent=P)
    ob.rotation_euler = v.to_track_quat("Z", "Y").to_euler()
    return ob


def _empty(P, name, loc=(0, 0, 0), rot=(0, 0, 0)):
    e = K.link(bpy.data.objects.new(name, None))
    return K.place(e, loc, rot, P)


def _plate(name, pts, t, mat, P, loc=(0, 0, 0), rot=(90, 0, 0)):
    """A flat plate of outline pts (x, y), t thick, standing on its face towards -Y by default (pictograms, shards)."""
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, -t / 2)) for x, y in pts]
    hi = [bm.verts.new((x, y, t / 2)) for x, y in pts]
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    return K.place(K.from_bm(name, bm, mat), loc, rot, P)


def warning_plate(P, loc, size, rot=(90, 0, 0)):
    """A bone plate with a mustard triangle and a graphite lightning bolt: high voltage, in any language."""
    G = _empty(P, "warn", loc, rot)
    s = size
    _plate("warn_plate", K.rounded_rect(s, s * 1.1, s * 0.06, segs=3), 0.012, M(BONE, wear=0.6, grime=0.4), G, rot=(0, 0, 0))
    tri = [(-s * 0.4, -s * 0.3), (s * 0.4, -s * 0.3), (0, s * 0.42)]
    _plate("warn_tri", tri, 0.008, M(MUSTARD, wear=0.5), G, loc=(0, 0, 0.009), rot=(0, 0, 0))
    bolt_pts = [(0.03, 0.26), (-0.07, 0.0), (0.0, 0.0), (-0.05, -0.2), (0.08, 0.05), (0.01, 0.05), (0.08, 0.26)]
    _plate("warn_bolt", [(x * s, y * s) for x, y in bolt_pts], 0.006, _dark(), G, loc=(0, -0.02 * s, 0.015), rot=(0, 0, 0))
    return G


def insulator_string(P, top, n=6, r=0.13, pitch=0.16):
    """A suspension insulator string hanging from top: a rod, glazed discs and a clamp yoke."""
    x, y, z = top
    glaze = K.mat("glaze", GLAZE, rough=0.2, metal=0.0, wear=0.2, grime=0.3)
    L = n * pitch + 0.25
    K.cyl("ins_rod", 0.02, L, _dark(), loc=(x, y, z - L / 2), segs=6, bev=0, parent=P)
    for k in range(n):
        K.cyl("ins_disc", r, 0.045, glaze, loc=(x, y, z - 0.15 - k * pitch), segs=8, bev=0, r2=r * 0.45, parent=P)
    K.box("ins_yoke", (0.32, 0.05, 0.06), _dark(), loc=(x, y, z - L), bev=0, parent=P)
    K.box("ins_clamp", (0.3, 0.09, 0.07), _zinc(), loc=(x, y, z - L - 0.07), bev=0, parent=P)


def pin_insulator(P, loc, glaze=None):
    """A pin insulator on top of a cross arm: a steel pin and a two-skirted glazed body."""
    x, y, z = loc
    glaze = glaze or K.mat("glaze", GLAZE, rough=0.2, metal=0.0, wear=0.2, grime=0.3)
    K.cyl("pin", 0.025, 0.14, _dark(), loc=(x, y, z + 0.07), segs=8, bev=0, parent=P)
    K.cyl("skirt_lo", 0.11, 0.12, glaze, loc=(x, y, z + 0.18), segs=12, bev=0.01, r2=0.07, parent=P)
    K.cyl("skirt_hi", 0.09, 0.1, glaze, loc=(x, y, z + 0.27), segs=12, bev=0.01, r2=0.05, parent=P)
    K.cyl("crown", 0.055, 0.06, glaze, loc=(x, y, z + 0.34), segs=12, bev=0.015, parent=P)


def wheel(P, name, loc, r=0.38, w=0.2, axis="y"):
    """A trailer wheel: tyre, steel rim and hub nuts, its axle along y."""
    G = _empty(P, name, loc, (90, 0, 0) if axis == "y" else (0, 90, 0))
    K.cyl(f"{name}_tyre", r, w, K.mat("tyre", TYRE, rough=0.9, grime=0.3), segs=20, bev=0.05, parent=G)
    K.cyl(f"{name}_rim", r * 0.6, w + 0.02, M(BONE, metal=0.3, wear=0.5), segs=16, bev=0.015, parent=G)
    K.cyl(f"{name}_hub", r * 0.22, w + 0.07, _dark(), segs=10, bev=0.01, parent=G)
    for k in range(5):
        a = 2 * math.pi * k / 5
        K.cyl(f"{name}_nut", 0.018, w + 0.09, _zinc(), loc=(math.cos(a) * r * 0.32, math.sin(a) * r * 0.32, 0), segs=6, bev=0, parent=G)
    return G


def dish_mesh(name, R, depth, t, material, rings=7, segs=28):
    """A closed paraboloid dish shell, concave towards +Z, its vertex at the origin."""
    bm = bmesh.new()
    front, back = [], []
    for i in range(1, rings + 1):
        rr = R * i / rings
        z = depth * (rr / R) ** 2
        front.append([bm.verts.new((rr * math.cos(2 * math.pi * j / segs), rr * math.sin(2 * math.pi * j / segs), z)) for j in range(segs)])
        back.append([bm.verts.new((rr * math.cos(2 * math.pi * j / segs), rr * math.sin(2 * math.pi * j / segs), z - t)) for j in range(segs)])
    cf, cb = bm.verts.new((0, 0, 0)), bm.verts.new((0, 0, -t))
    for j in range(segs):
        k = (j + 1) % segs
        bm.faces.new((cf, front[0][j], front[0][k]))
        bm.faces.new((cb, back[0][k], back[0][j]))
        for i in range(rings - 1):
            bm.faces.new((front[i][j], front[i + 1][j], front[i + 1][k], front[i][k]))
            bm.faces.new((back[i][j], back[i][k], back[i + 1][k], back[i + 1][j]))
        bm.faces.new((front[-1][j], back[-1][j], back[-1][k], front[-1][k]))
    return K.from_bm(name, bm, material, smooth=True)


# ================================================================== power
def power_pylon(seed=21):
    """A steel lattice transmission pylon, 22 m: tapering legs on concrete footings, X-braced faces, two levels of cross
    arms and an earth-wire peak, insulator strings hanging empty since the lines came down."""
    P = _root("pylon")
    leg = M(STEEL, metal=0.4, wear=0.55, grime=0.4)
    brace = M(ZINC, metal=0.55, wear=0.5, grime=0.35)
    levels = [0.0, 3.2, 6.0, 8.5, 10.7, 12.7, 14.5, 16.3, 18.0, 19.6, 21.0, 22.0]

    def hw(z):
        return 2.6 - 1.7 * z / 14.5 if z <= 14.5 else 0.9 - 0.4 * (z - 14.5) / 7.5

    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box(f"footing{sx}{sy}", (0.9, 0.9, 0.7), _concrete(0.3), loc=(sx * 2.6, sy * 2.6, 0.15), bev=0.04, parent=P)
            nut(P, (sx * 2.6, sy * 2.6 - 0.3, 0.5), r=0.04)
    for i in range(len(levels) - 1):
        z0, z1 = levels[i], levels[i + 1]
        h0, h1 = hw(z0), hw(z1)
        lw = 0.2 if z1 <= 14.5 else 0.16
        for sx in (-1, 1):
            for sy in (-1, 1):
                beam(P, "leg", (sx * h0, sy * h0, z0), (sx * h1, sy * h1, z1), lw, leg)
        # four faces: a horizontal at the top of the panel and an X of braces
        for face in range(4):
            def pt(s, h, z):
                return [(s * h, -h, z), (h, s * h, z), (-s * h, h, z), (-h, -s * h, z)][face]
            beam(P, "horiz", pt(-1, h1, z1), pt(1, h1, z1), 0.09, brace)
            if i == 0:
                continue  # the bottom panel is open, so the base reads as four legs
            beam(P, "diag", pt(-1, h0, z0), pt(1, h1, z1), 0.07, brace)
            beam(P, "diag", pt(1, h0, z0), pt(-1, h1, z1), 0.07, brace)
    # base: a plan brace at the first panel and K-braces up from the footings
    for face in range(4):
        h = hw(3.2)
        a = [(-2.6, -2.6), (2.6, -2.6), (2.6, 2.6), (-2.6, 2.6)][face]
        b = [(2.6, -2.6), (2.6, 2.6), (-2.6, 2.6), (-2.6, -2.6)][face]
        mid = ((a[0] + b[0]) / 2 * h / 2.6, (a[1] + b[1]) / 2 * h / 2.6, 3.2)
        beam(P, "kbrace", (a[0] * 0.98, a[1] * 0.98, 0.3), mid, 0.08, brace)
        beam(P, "kbrace", (b[0] * 0.98, b[1] * 0.98, 0.3), mid, 0.08, brace)

    def arm(z, length, tag):
        """Cross arms both sides at height z: two bottom chords and a top chord to the tip, verticals and diagonals."""
        w = hw(z)
        rise = 1.5
        for s in (-1, 1):
            tip = s * (w + length)
            for sy in (-1, 1):
                beam(P, f"{tag}_bot", (s * w, sy * w, z), (tip, sy * 0.2, z), 0.12, leg)
                beam(P, f"{tag}_top", (s * w, sy * w, z + rise), (tip, 0, z + 0.12), 0.1, leg)
                for t in (0.33, 0.66):
                    bx, by = s * w + (tip - s * w) * t, sy * (w + (0.2 - w) * t)
                    tx, ty, tz = s * w + (tip - s * w) * t, sy * w * (1 - t), z + rise + (0.12 - rise) * t
                    beam(P, f"{tag}_v", (bx, by, z), (tx, ty, tz), 0.06, brace)
                    t2 = t - 0.33
                    beam(P, f"{tag}_d", (s * w + (tip - s * w) * t2, sy * (w + (0.2 - w) * t2), z), (tx, ty, tz), 0.06, brace)
            for t in (0.33, 0.66, 1.0):
                bx, by = s * w + (tip - s * w) * t, w + (0.2 - w) * t
                beam(P, f"{tag}_tie", (bx, -by, z), (bx, by, z), 0.06, brace)
            insulator_string(P, (tip - s * 0.15, 0, z - 0.05), n=7)

    arm(14.5, 4.4, "arm_lo")
    arm(18.0, 3.2, "arm_hi")
    # earth-wire peak: a short arm and a clamp at each end
    w = hw(22.0)
    for s in (-1, 1):
        beam(P, "peak_arm", (s * w, 0, 22.0), (s * (w + 1.3), 0, 22.05), 0.12, leg)
        beam(P, "peak_brace", (s * w, 0, 21.0), (s * (w + 1.3), 0, 22.0), 0.07, brace)
        K.box("peak_clamp", (0.18, 0.1, 0.16), _zinc(), loc=(s * (w + 1.25), 0, 21.92), bev=0.01, parent=P)
    K.box("peak_cap", (2 * w + 0.2, 2 * w + 0.2, 0.12), leg, loc=(0, 0, 22.06), bev=0.02, parent=P)
    # anti-climb band, number plate and warning plate on the front face
    h = hw(4.0)
    for face in range(4):
        a = [(-h, -h), (h, -h), (h, h), (-h, h)][face]
        b = [(h, -h), (h, h), (-h, h), (-h, -h)][face]
        for k in range(3):
            beam(P, "guard", (a[0] * 1.03, a[1] * 1.03, 3.9 + k * 0.18), (b[0] * 1.03, b[1] * 1.03, 3.9 + k * 0.18), 0.04, _dark())
    h = hw(2.6)
    K.box("plate_back", (1.0, 0.03, 0.5), M(BONE, wear=0.6, grime=0.5), loc=(0, -h - 0.04, 2.6), bev=0.01, parent=P)
    stencil(P, "PL-17", (0, -h - 0.06, 2.6), 0.28, _dark())
    beam(P, "plate_bar", (-h, -h - 0.02, 2.6), (h, -h - 0.02, 2.6), 0.06, brace)
    warning_plate(P, (0, -hw(4.6) - 0.06, 4.6), 0.55)
    return P


def power_pole(seed=22):
    """A 9 m concrete line pole leaning a few degrees: a steel cross arm on braces, three pin insulators, step bolts and a
    junction box strapped to it with its conduit."""
    rnd = random.Random(seed)
    P = _root("pole")
    conc = _concrete(0.35)
    K.cyl("collar", 0.42, 0.14, K.mat("concrete_in", "#8e8b82", rough=0.95, cracks=0.6, grime=0.5), loc=(0, 0, 0.04), segs=14, bev=0.03, parent=P)
    L = _empty(P, "lean", (0, 0, 0), (3.5, -2.0, 0))
    H = 9.0
    K.cyl("shaft", 0.17, H + 0.3, conc, loc=(0, 0, (H + 0.3) / 2 - 0.3), segs=14, r2=0.11, bev=0.02, parent=L)
    K.cyl("cap", 0.12, 0.08, _dark(), loc=(0, 0, H + 0.03), segs=14, bev=0.02, parent=L)

    def rad(z):
        return 0.17 - 0.06 * (z + 0.3) / (H + 0.3)

    # cross arm (a steel channel) bolted through the pole with a clamp, and two raked braces
    za = 8.3
    ya = -rad(za) - 0.06
    K.box("arm", (2.6, 0.1, 0.14), _steel(), loc=(0, ya, za), bev=0.015, parent=L)
    K.box("arm_clamp", (0.3, 0.32, 0.2), _dark(), loc=(0, -0.02, za), bev=0.02, parent=L)
    for s in (-1, 1):
        beam(L, "arm_brace", (s * 0.8, ya, za - 0.06), (s * 0.06, ya + 0.02, za - 0.75), 0.06, _zinc(), d=0.03)
        pin_insulator(L, (s * 1.15, ya, za + 0.07))
        bolt(L, (s * 0.8, ya - 0.06, za))
    # the third insulator on a bracket on top of the pole
    K.box("top_bracket", (0.08, 0.3, 0.06), _steel(), loc=(0, -0.08, H - 0.15), bev=0.01, parent=L)
    pin_insulator(L, (0, -0.18, H - 0.12))
    # step bolts up the side
    for k in range(9):
        z = 2.6 + k * 0.5
        s = 1 if k % 2 else -1
        a = math.radians(s * 60 + 90)
        r = rad(z)
        K.cyl("step", 0.018, 0.22, _zinc(), loc=(math.cos(a) * (r + 0.1), math.sin(a) * (r + 0.1), z), rot=(90, 0, s * 60), segs=6, bev=0, parent=L)
    # hazard band and number plate low down
    for k in range(4):
        z = 1.1 + k * 0.14
        K.cyl("band", rad(z) + 0.008, 0.14, M(MUSTARD if k % 2 == 0 else GRAPHITE, wear=0.6), loc=(0, 0, z), segs=14, bev=0, parent=L)
    K.box("num_plate", (0.28, 0.02, 0.14), M(BONE, wear=0.6), loc=(0, -rad(1.9) - 0.01, 1.9), bev=0.004, parent=L)
    stencil(L, "P-204", (0, -rad(1.9) - 0.025, 1.9), 0.075, _dark(), extrude=0.004)
    # junction box on two straps, its conduit down into the ground and up towards the arm
    jz = 2.75
    jy = -rad(jz) - 0.15
    FR.framed_box(L, "jbox", (0, jy, jz), (0.46, 0.24, 0.62), M(BONE, wear=0.55, grime=0.5), _steel(), fw=0.05, screws=_zinc())
    warning_plate(L, (0, jy - 0.125, jz + 0.08), 0.2)
    for z in (jz - 0.2, jz + 0.2):
        K.cyl("strap", rad(z) + 0.012, 0.04, _zinc(), loc=(0, 0, z), segs=14, bev=0, parent=L)
    K.cyl("conduit_dn", 0.035, jz - 0.3, _zinc(), loc=(0.12, jy + 0.05, (jz - 0.3) / 2 - 0.1), segs=8, bev=0, parent=L)
    K.cyl("conduit_up", 0.03, za - jz - 0.4, _zinc(), loc=(0.08, -rad(5) - 0.03, (za + jz) / 2), segs=8, bev=0, parent=L)
    for k in range(4):
        z = jz + 0.7 + k * 1.2
        K.box("clip", (0.1, 0.06, 0.04), _dark(), loc=(0.08, -rad(z) - 0.03, z), bev=0, parent=L)
    # a broken-off line end hanging from one insulator
    rod(L, "line_end", (-1.15, ya, za + 0.33), (-1.3, ya - 0.15, za - 1.2 - rnd.uniform(0, 0.2)), 0.012, K.mat("cable", CABLE, rough=0.6), segs=6)
    return P


def street_lamp_broken(seed=23):
    """A colony street lamp, 6 m: square post on a framed base, its arm bent down by something heavy, the head
    hanging with its cover cracked and a shard gone, the pieces in the grass below."""
    rnd = random.Random(seed)
    P = _root("lamp")
    gra = M(GRAPHITE, metal=0.3, wear=0.5, grime=0.4)
    x0 = -0.6
    K.box("footing", (0.8, 0.8, 0.2), _concrete(0.3), loc=(x0, 0, 0.06), bev=0.03, parent=P)
    FR.framed_box(P, "base", (x0, 0, 0.55), (0.5, 0.5, 0.8), M(STEEL, wear=0.6, grime=0.5), gra, fw=0.07, screws=_zinc())
    K.box("hatch", (0.26, 0.02, 0.4), M(STEEL, wear=0.7), loc=(x0, -0.255, 0.55), bev=0.005, parent=P)
    FR.hazard(P, (x0, -0.27, 0.22), 0.44, 0.1, M(MUSTARD, wear=0.6), gra, n=5)
    post = _empty(P, "post", (x0, 0, 0.95), (0, 2.5, 0))
    H = 5.0
    K.box("pole", (0.2, 0.2, H), gra, loc=(0, 0, H / 2), bev=0.025, parent=post)
    K.box("collar", (0.26, 0.26, 0.12), _steel(), loc=(0, 0, 0.06), bev=0.02, parent=post)
    K.box("arm1", (0.9, 0.16, 0.16), gra, loc=(0.4, 0, H - 0.1), bev=0.025, parent=post)
    K.box("arm_knee", (0.2, 0.2, 0.2), _steel(), loc=(0.85, 0, H - 0.1), bev=0.03, parent=post)
    bend = _empty(post, "bend", (0.9, 0, H - 0.1), (8, 58, 0))
    K.box("arm2", (0.95, 0.16, 0.14), gra, loc=(0.45, 0, 0), bev=0.025, parent=bend)
    K.box("kink", (0.12, 0.18, 0.18), gra, loc=(0.02, 0, -0.01), rot=(0, 20, 0), bev=0.02, parent=bend)
    # the head, twisted on the broken arm so its cover faces the road
    head = _empty(bend, "head", (1.05, 0, -0.05), (-55, 0, 0))
    K.slab("hood", 0.8, 0.44, 0.15, 0.05, M(BONE, wear=0.65, grime=0.5, scratches=0.6), loc=(0.15, 0, 0.0), bev=0.02, parent=head)
    K.box("hood_ridge", (0.7, 0.12, 0.05), _steel(), loc=(0.15, 0, 0.09), bev=0.01, parent=head)
    K.box("hood_cap", (0.08, 0.3, 0.18), gra, loc=(-0.24, 0, 0.0), bev=0.015, parent=head)
    K.box("hood_rim", (0.84, 0.48, 0.05), _steel(), loc=(0.15, 0, -0.1), bev=0.012, parent=head)
    # the cover: three shards left in the rim, one gone, crack lines across them
    cover = K.mat("lampglass", GLASS, rough=0.12, metal=0.0, wear=0.2, grime=0.4)
    for k, (cx, w) in enumerate(((-0.12, 0.22), (0.1, 0.2), (0.36, 0.2))):
        if k == 1:
            continue
        K.box(f"cover{k}", (w, 0.38, 0.03), cover, loc=(cx, 0, -0.15), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.004, parent=head)
    _plate("cover_shard", [(0, -0.19), (0.18, -0.19), (0.02, 0.1)], 0.03, cover, head, loc=(0.0, 0, -0.15), rot=(0, 0, 0))
    for k in range(3):
        K.box("crack", (0.22, 0.008, 0.032), _dark(), loc=(-0.12 + (k % 2) * 0.48, 0.05 * (k - 1), -0.15), rot=(0, 0, 35 + k * 50), bev=0, parent=head)
    K.box("reflector", (0.7, 0.4, 0.02), K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(0.15, 0, -0.105), bev=0, parent=head)
    K.sphere("bulb", 0.07, K.mat("deadlamp", "#7d8f96", rough=0.2, metal=0.3), loc=(0.12, 0, -0.14), scale=(1, 1, 0.8), parent=head)
    # glass on the ground under the head
    for k in range(6):
        sx = 0.75 + rnd.uniform(-0.5, 0.6)
        sy = rnd.uniform(-0.6, 0.5)
        s = rnd.uniform(0.06, 0.13)
        _plate(f"shard{k}", [(0, 0), (s, rnd.uniform(-0.02, 0.03)), (rnd.uniform(0.2, 0.7) * s, s * 0.8)], 0.01, cover, P,
               loc=(sx, sy, 0.01), rot=(rnd.uniform(-6, 6), rnd.uniform(-6, 6), rnd.uniform(0, 360)))
    return P


def bus_shelter(seed=24):
    """A colony bus shelter, 4 x 1.6 x 2.6 m: steel posts, a pill-cornered roof, glass panels cracked and one smashed,
    a slatted bench and a faded route blade sign."""
    rnd = random.Random(seed)
    P = _root("shelter")
    steel = _steel()
    gra = _dark()
    glass = K.mat("shelterglass", GLASS, rough=0.1, metal=0.1, wear=0.2, grime=0.45)
    W, D, H = 4.0, 1.6, 2.6
    K.box("pad", (W + 0.4, D + 0.5, 0.12), _concrete(0.25), loc=(0, 0.05, 0.02), bev=0.03, parent=P)
    K.box("kerb", (W + 0.6, 0.25, 0.18), _concrete(0.4), loc=(0, -D / 2 - 0.45, 0.05), bev=0.03, parent=P)
    xs = (-W / 2, -W / 6, W / 6, W / 2)
    for x in xs:
        K.box("post_b", (0.1, 0.1, H - 0.15), steel, loc=(x, D / 2 - 0.05, (H - 0.15) / 2 + 0.08), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box("post_f", (0.1, 0.1, H - 0.15), steel, loc=(s * W / 2, -D / 2 + 0.05, (H - 0.15) / 2 + 0.08), bev=0.015, parent=P)
        K.box("rail_side", (0.08, D, 0.08), steel, loc=(s * W / 2, 0, 0.3), bev=0.01, parent=P)
        K.box("rail_side_top", (0.08, D, 0.08), steel, loc=(s * W / 2, 0, 2.3), bev=0.01, parent=P)
    for z in (0.3, 2.3):
        K.box("rail_back", (W, 0.08, 0.08), steel, loc=(0, D / 2 - 0.05, z), bev=0.01, parent=P)
    K.slab("roof", W + 0.3, D + 0.3, 0.12, 0.12, M(BONE, wear=0.55, grime=0.5, moss=0.25), loc=(0, 0.0, H - 0.04), rot=(-3, 0, 0), bev=0.03, parent=P)
    K.box("fascia", (W + 0.3, 0.06, 0.16), steel, loc=(0, -D / 2 - 0.15, H - 0.08), bev=0.012, parent=P)
    # back panes: left cracked, middle intact with a timetable frame, right smashed to a few shards
    for k, x in enumerate((-W / 3, 0, W / 3)):
        if k == 2:
            for j in range(4):
                bx = x - 0.55 + j * 0.3
                _plate(f"pane_shard{j}", [(0, 0), (0.3, 0), (0.15 + rnd.uniform(-0.1, 0.1), rnd.uniform(0.2, 0.6))], 0.012, glass, P,
                       loc=(bx, D / 2 - 0.05, 0.34), rot=(90, 0, 0))
            continue
        K.box(f"pane{k}", (W / 3 - 0.12, 0.012, 1.92), glass, loc=(x, D / 2 - 0.05, 1.3), bev=0, parent=P)
    for j in range(4):
        a = 30 + j * 85 + rnd.uniform(-15, 15)
        L = rnd.uniform(0.35, 0.6)
        K.box("crack", (L, 0.016, 0.012), gra, loc=(-W / 3 + math.cos(math.radians(a)) * L / 2, D / 2 - 0.06, 1.4 + math.sin(math.radians(a)) * L / 2),
              rot=(0, -a, 0), bev=0, parent=P)
    K.box("timetable", (0.5, 0.03, 0.7), M(BONE, wear=0.7), loc=(0.3, D / 2 - 0.08, 1.5), bev=0.01, parent=P)
    for j in range(6):
        K.box("tt_line", (0.38, 0.01, 0.025), M(STEEL, wear=0.8), loc=(0.3, D / 2 - 0.1, 1.72 - j * 0.09), bev=0, parent=P)
    # side panes: the left one whole but crazed, the right one with only its lower half
    K.box("side_l", (0.012, D - 0.12, 1.92), glass, loc=(-W / 2, 0, 1.3), bev=0, parent=P)
    for j in range(3):
        a = 20 + j * 120
        K.box("crack_s", (0.016, 0.5, 0.012), gra, loc=(-W / 2 - 0.01, -0.2 + math.cos(math.radians(a)) * 0.25, 1.1 + math.sin(math.radians(a)) * 0.25),
              rot=(-a, 0, 0), bev=0, parent=P)
    _plate("side_r", [(-0.7, 0), (0.7, 0), (0.7, 0.8), (0.3, 0.55), (0.05, 0.95), (-0.35, 0.5), (-0.7, 0.7)], 0.012, glass, P,
           loc=(W / 2, 0, 0.34), rot=(90, 0, 90))
    # bench: bone slats on steel brackets
    for x in (-1.1, 0, 1.1):
        K.box("bench_leg", (0.06, 0.4, 0.45), steel, loc=(x, D / 2 - 0.3, 0.3), bev=0.01, parent=P)
    for j in range(3):
        K.box(f"slat{j}", (2.6, 0.11, 0.04), M(BONE, wear=0.65, grime=0.5), loc=(0, D / 2 - 0.45 + j * 0.13, 0.54), bev=0.01, parent=P)
    # route blade sign sticking out from the front right post, faded
    sg = _empty(P, "sign", (W / 2 + 0.05, -D / 2 - 0.05, 2.05))
    K.box("sign_arm", (0.5, 0.05, 0.05), steel, loc=(0.25, 0, 0.3), bev=0.008, parent=sg)
    K.box("sign_board", (0.04, 0.55, 0.6), M(MUSTARD, wear=0.75, grime=0.5, scratches=0.7), loc=(0.5, 0, 0.0), rot=(0, 0, 90), bev=0.01, parent=sg)
    for s, yaw in ((1, -90), (-1, 90)):
        G = _empty(sg, "face", (0.5, s * 0.03, 0.0), (0, 0, 0 if s < 0 else 180))
        stencil(G, "7", (0.1, 0, 0.1), 0.32, M(GRAPHITE, wear=0.6), extrude=0.006)
        # a bus pictogram: body, windows and two wheels
        K.box("pict_bus", (0.24, 0.01, 0.12), M(GRAPHITE, wear=0.6), loc=(-0.08, 0, -0.17), bev=0.01, parent=G)
        K.box("pict_win", (0.18, 0.012, 0.04), M(MUSTARD, wear=0.5), loc=(-0.08, 0, -0.15), bev=0, parent=G)
        for wx in (-0.16, 0.0):
            K.cyl("pict_wheel", 0.03, 0.012, M(GRAPHITE), loc=(wx, 0, -0.24), rot=(90, 0, 0), segs=10, bev=0, parent=G)
    return P


def billboard_frame(seed=25):
    """A roadside billboard, 8 x 4 m on two steel I-beam legs: a catwalk and ladder, the board's panels partly torn
    away so the steel frame shows behind, one hanging by a corner and one down in the grass."""
    rnd = random.Random(seed)
    P = _root("billboard")
    steel = _steel()
    zinc = _zinc()
    W, Hb, z0 = 8.0, 4.0, 3.4
    for s in (-1, 1):
        x = s * 2.4
        K.box(f"footing{s}", (1.0, 1.0, 0.5), _concrete(0.3), loc=(x, 0.15, 0.1), bev=0.04, parent=P)
        K.box(f"web{s}", (0.04, 0.34, z0 + Hb), steel, loc=(x, 0.15, (z0 + Hb) / 2), bev=0, parent=P)
        for f in (-1, 1):
            K.box(f"flange{s}{f}", (0.3, 0.035, z0 + Hb), steel, loc=(x, 0.15 + f * 0.17, (z0 + Hb) / 2), bev=0.008, parent=P)
        K.box(f"base_plate{s}", (0.5, 0.5, 0.04), _dark(), loc=(x, 0.15, 0.37), bev=0.008, parent=P)
        for bx in (-0.18, 0.18):
            for by in (-0.18, 0.18):
                nut(P, (x + bx, 0.15 + by, 0.4))
    # the frame behind the board: rails, studs and back braces to the legs
    fy = 0.12
    for k in range(4):
        z = z0 + k * Hb / 3
        K.box("rail", (W, 0.1, 0.12), steel, loc=(0, fy, z), bev=0.01, parent=P)
    for k in range(5):
        K.box("stud", (0.1, 0.1, Hb), steel, loc=(-W / 2 + k * W / 4, fy + 0.02, z0 + Hb / 2), bev=0.01, parent=P)
    for s in (-1, 1):
        beam(P, "kicker", (s * 2.4, 0.3, z0 - 1.6), (s * 3.9, 0.17, z0 + 0.05), 0.08, zinc)
        beam(P, "kicker", (s * 2.4, 0.3, z0 - 1.6), (s * 0.9, 0.17, z0 + 0.05), 0.08, zinc)
    # catwalk with brackets and a rail, the ladder up the right leg
    K.box("catwalk", (W - 0.4, 0.7, 0.05), M(ZINC, metal=0.6, wear=0.6, grime=0.5), loc=(0, -0.45, z0 - 0.2), bev=0.008, parent=P)
    for k in range(9):
        K.box("grate", (0.03, 0.68, 0.06), _dark(), loc=(-3.6 + k * 0.9, -0.45, z0 - 0.19), bev=0, parent=P)
    for x in (-3.4, -1.2, 1.2, 3.4):
        beam(P, "cw_bracket", (x, 0.05, z0 - 0.95), (x, -0.75, z0 - 0.22), 0.06, steel)
        K.box("cw_post", (0.05, 0.05, 1.0), steel, loc=(x, -0.78, z0 + 0.3), bev=0.01, parent=P)
    K.box("cw_rail", (W - 0.4, 0.05, 0.05), steel, loc=(0, -0.78, z0 + 0.78), bev=0.01, parent=P)
    for s in (-1, 1):
        K.box("ladder_rail", (0.05, 0.05, z0 - 0.2), zinc, loc=(2.95 + s * 0.22, -0.78, (z0 - 0.2) / 2), bev=0, parent=P)
    for k in range(10):
        K.box("rung", (0.44, 0.04, 0.04), zinc, loc=(2.95, -0.78, 0.3 + k * 0.3), bev=0, parent=P)
    # the board: 4 x 2 panels of a faded poster; the top row torn
    pw, ph = W / 4, Hb / 2
    blue = M(STEEL, wear=0.75, grime=0.45, scratches=0.6)
    bone = M(BONE, wear=0.7, grime=0.5, scratches=0.6)
    for col in range(4):
        for row in range(2):
            cx, cz = -W / 2 + pw * (col + 0.5), z0 + ph * (row + 0.5)
            m = blue if row else bone
            if row == 1 and col == 1:
                continue  # gone: the frame shows through
            if row == 1 and col == 0:
                _plate("torn", [(-pw / 2, -ph / 2), (pw / 2, -ph / 2), (pw / 2, ph * 0.1), (0.1, -ph * 0.05), (-0.3, ph * 0.3),
                                (-pw / 2, ph * 0.15)], 0.04, m, P, loc=(cx, -0.0, cz))
                continue
            if row == 1 and col == 3:
                hang = _empty(P, "hang", (cx - pw / 2, -0.06, cz + ph / 2), (0, 0, 0))
                K.box("hanging", (pw, 0.04, ph), m, loc=(pw / 2, 0, -ph / 2), bev=0.008, parent=hang)
                hang.rotation_euler = (math.radians(-6), math.radians(34), 0)
                continue
            K.box(f"panel{col}{row}", (pw - 0.03, 0.04, ph - 0.03), m, loc=(cx, -0.0, cz), rot=(0, rnd.uniform(-0.4, 0.4), 0), bev=0.008, parent=P)
    # the poster: a mustard sun on the blue (on the panel that is still there) and COLONY across the bottom
    K.cyl("sun", 0.75, 0.02, M(MUSTARD, wear=0.7, grime=0.4), loc=(1.0, -0.035, z0 + ph * 1.5), rot=(90, 0, 0), segs=20, bev=0, parent=P)
    K.box("band", (W - 0.1, 0.02, 0.3), M(MUSTARD, wear=0.7, grime=0.4), loc=(0, -0.035, z0 + ph - 0.35), bev=0, parent=P)
    stencil(P, "COLONY", (0, -0.04, z0 + 0.8), 1.1, M(STEEL, wear=0.7), extrude=0.008)
    # the fallen panel, leaning against the left leg
    K.box("fallen", (pw, 0.04, ph), blue, loc=(-3.1, -0.9, 0.9), rot=(-65, 8, 18), bev=0.008, parent=P)
    return P


# ================================================================== freight
def _container(P, body, frame, door, opened=False, seed=1):
    """An ISO 20 ft container, 6.06 x 2.44 x 2.59 m along X, doors at +X: corrugated walls between corner posts and
    rails, corner castings, door bars and cams."""
    rnd = random.Random(seed)
    L, W, H = 6.06, 2.44, 2.59
    cast = M(GRAPHITE, metal=0.4, wear=0.6, grime=0.5)
    t = 0.16
    for sy in (-1, 1):
        for z in (t / 2, H - t / 2):
            K.box("side_rail", (L, t, t), frame, loc=(0, sy * (W / 2 - t / 2), z), bev=0.012, parent=P)
    for sx in (-1, 1):
        for z in (t / 2, H - t / 2):
            K.box("end_rail", (t, W - 2 * t, t), frame, loc=(sx * (L / 2 - t / 2), 0, z), bev=0.012, parent=P)
        for sy in (-1, 1):
            K.box("corner_post", (t, t, H - 2 * t), frame, loc=(sx * (L / 2 - t / 2), sy * (W / 2 - t / 2), H / 2), bev=0.012, parent=P)
            for z in (0.09, H - 0.09):
                K.box("casting", (0.2, 0.18, 0.18), cast, loc=(sx * (L / 2 - 0.1), sy * (W / 2 - 0.09), z), bev=0.012, parent=P)
                K.box("cast_hole", (0.1, 0.01, 0.06), K.mat("hole", "#141618", rough=0.9), loc=(sx * (L / 2 - 0.1), sy * (W / 2 + 0.001), z), bev=0, parent=P)
    # long walls: a flat sheet with vertical ribs pressed out
    n = 22
    span = L - 2 * t
    for sy in (-1, 1):
        K.box("wall", (span, 0.03, H - 2 * t), body, loc=(0, sy * (W / 2 - 0.07), H / 2), bev=0, parent=P)
        for k in range(n):
            x = -span / 2 + span * (k + 0.5) / n
            K.box("rib", (0.13, 0.05, H - 2 * t), body, loc=(x, sy * (W / 2 - 0.04), H / 2), bev=0, parent=P)
    K.box("roof", (span, W - 2 * t + 0.04, 0.03), body, loc=(0, 0, H - 0.06), bev=0, parent=P)
    for k in range(12):
        x = -span / 2 + span * (k + 0.5) / 12
        K.box("roof_rib", (0.2, W - 2 * t, 0.025), body, loc=(x, 0, H - 0.04), bev=0, parent=P)
    # the blind end at -X
    K.box("end_wall", (0.03, W - 2 * t, H - 2 * t), body, loc=(-L / 2 + 0.07, 0, H / 2), bev=0, parent=P)
    for k in range(5):
        y = -(W - 2 * t) / 2 + (W - 2 * t) * (k + 0.5) / 5
        K.box("end_rib", (0.05, 0.16, H - 2 * t), body, loc=(-L / 2 + 0.04, y, H / 2), bev=0, parent=P)
    # doors at +X, each hung on its outer corner post
    dw = W / 2 - t
    for sy in (-1, 1):
        ang = (-158 if sy < 0 else 150) if opened else 0
        hinge = _empty(P, "door_hinge", (L / 2 - 0.03, sy * (W / 2 - t), H / 2), (0, 0, ang))
        G = hinge
        yd = -sy
        K.box("door", (0.04, dw, H - 2 * t), door, loc=(0, yd * dw / 2, 0), bev=0.008, parent=G)
        for k in range(4):
            K.box("door_rib", (0.04, dw - 0.06, 0.12), door, loc=(0.025, yd * dw / 2, -0.9 + k * 0.6), bev=0, parent=G)
        for j, f in enumerate((0.3, 0.75)):
            yb = yd * dw * f
            K.cyl("lock_bar", 0.025, H - 0.1, _zinc(), loc=(0.07, yb, 0), segs=8, bev=0, parent=G)
            for zz in (-(H / 2 - 0.12), H / 2 - 0.12):
                K.box("cam", (0.06, 0.08, 0.1), cast, loc=(0.06, yb, zz), bev=0, parent=G)
            K.box("handle", (0.04, 0.35, 0.05), _zinc(), loc=(0.11, yb + yd * 0.17, -0.25 - j * 0.1), bev=0.005, parent=G)
            K.box("keeper", (0.06, 0.06, 0.08), cast, loc=(0.09, yb + yd * 0.3, -0.25 - j * 0.1), bev=0, parent=G)
        for zz in (-0.9, 0.0, 0.9):
            K.cyl("hinge_pin", 0.03, 0.14, cast, loc=(0.04, 0, zz), segs=6, bev=0, parent=G)
    if opened:
        # inside: a plank floor and a dark back
        for k in range(8):
            K.box("floor", (span, (W - 2 * t) / 8 - 0.01, 0.04), wood(k, wear=0.6), loc=(0, -(W - 2 * t) / 2 + (W - 2 * t) * (k + 0.5) / 8, t + 0.02),
                  bev=0, parent=P)
    return rnd


def shipping_container(seed=26):
    """A 6 m ISO container in steel blue, shut: corrugated sides, door bars and cams, a faded COLONY stencil."""
    P = _root("container")
    body = M(STEEL, metal=0.3, wear=0.12, grime=0.55, scratches=0.3, moss=0.1)
    frame = M(STEEL, metal=0.3, wear=0.45, grime=0.6)
    _container(P, body, frame, body, seed=seed)
    paint = M(BONE, wear=0.8, grime=0.5, scratches=0.8)
    stencil(P, "COLONY", (-0.3, -1.212, 1.4), 0.62, paint, extrude=0.006)
    stencil(P, "COLONY", (0.3, 1.212, 1.4), 0.62, paint, rot=(90, 0, 180), extrude=0.006)
    stencil(P, "CW 0417", (3.075, -0.62, 2.12), 0.14, paint, rot=(90, 0, 90), extrude=0.004)
    # rust streaks running down from the roof rail
    for k, x in enumerate((-2.2, -0.6, 1.7)):
        K.box("streak", (0.08, 0.012, 0.7 + k * 0.2), M(RUST, wear=0.4, grime=0.5), loc=(x, -1.245, 2.1 - k * 0.1), bev=0, parent=P)
    return P


def shipping_container_open(seed=27):
    """The same container in bone with mustard doors and rails, its doors swung wide and a few crates left inside."""
    rnd = random.Random(seed)
    P = _root("container_open")
    body = M(BONE, metal=0.2, wear=0.3, grime=0.55, scratches=0.4, moss=0.15)
    frame = M(MUSTARD, metal=0.2, wear=0.7, grime=0.5)
    _container(P, body, frame, M(MUSTARD, metal=0.2, wear=0.75, grime=0.5), opened=True, seed=seed)
    stencil(P, "COLONY", (-0.3, -1.212, 1.4), 0.62, M(STEEL, wear=0.8, grime=0.4), extrude=0.006)
    # crates: two settler wood crates, one colony case, and one dragged half out of the door
    for k, (x, y, s, yaw) in enumerate(((-2.2, 0.45, 0.9, 4), (-2.2, -0.5, 0.8, -6), (-2.15, 0.4, 0.7, 12), (0.8, 0.5, 0.75, 20))):
        z = 0.2 + s / 2 + (0.9 if k == 2 else 0)
        c = _empty(P, f"crate{k}", (x, y, z), (0, 0, yaw))
        K.box("crate", (s, s, s), wood(k, wear=0.6), bev=0.02, parent=c)
        for sx in (-1, 1):
            for sz in (-1, 1):
                K.box("batten", (s + 0.02, 0.06, 0.06), wood(3), loc=(0, sx * (s / 2 - 0.02), sz * (s / 2 - 0.02)), bev=0, parent=c)
        if k != 2:
            K.box("brace", (0.06, s * 1.4, 0.06), wood(2), loc=(s / 2 + 0.01, 0, 0), rot=(45, 0, 0), bev=0.008, parent=c)
    K.box("case", (1.0, 0.7, 0.65), M(STEEL, wear=0.6), loc=(2.4, -0.5, 0.52), bev=0.03, parent=P)
    for x in (2.05, 2.75):
        K.box("case_strap", (0.06, 0.74, 0.69), M(BONE, wear=0.5), loc=(x, -0.5, 0.52), bev=0.01, parent=P)
    FR.hazard(P, (2.4, -0.86, 0.3), 0.6, 0.1, M(MUSTARD), _dark(), n=4)
    c = _empty(P, "crate_out", (3.9, 0.3, 0.32), (rnd.uniform(-4, 4), 0, 32))
    K.box("crate", (0.65, 0.65, 0.65), wood(1, wear=0.6), bev=0.02, parent=c)
    for sx in (-1, 1):
        K.box("batten", (0.67, 0.06, 0.06), wood(3), loc=(0, sx * 0.3, 0.3), bev=0, parent=c)
    return P


# ================================================================== fuel and gas
def fuel_tank(seed=28):
    """A horizontal fuel tank, 5 m long, on two concrete saddles with steel straps: a ladder up to a manway hatch on top,
    a hazard band, a draw-off pipe and valve."""
    P = _root("fuel_tank")
    shell = M(BONE, metal=0.2, wear=0.55, grime=0.55, scratches=0.5, moss=0.15)
    steel = _steel()
    zinc = _zinc()
    R, L, zc = 1.1, 4.4, 1.78
    K.cyl("shell", R, L, shell, loc=(0, 0, zc), rot=(0, 90, 0), segs=28, bev=0.02, parent=P)
    for s in (-1, 1):
        K.sphere(f"head{s}", R, shell, loc=(s * L / 2, 0, zc), scale=(0.3, 1, 1), parent=P)
        K.cyl(f"seam{s}", R + 0.015, 0.06, steel, loc=(s * L / 2, 0, zc), rot=(0, 90, 0), segs=28, bev=0, parent=P)
        x = s * 1.4
        K.box(f"saddle{s}", (0.55, 1.7, 1.0), _concrete(0.25), loc=(x, 0, 0.5), bev=0.04, parent=P)
        K.box(f"plinth{s}", (0.8, 2.0, 0.15), _concrete(0.4), loc=(x, 0, 0.03), bev=0.03, parent=P)
        K.torus(f"strap{s}", R + 0.02, 0.025, steel, loc=(x, 0, zc), rot=(0, 90, 0), segs=24, parent=P)
        for sy in (-1, 1):
            K.box(f"strap_foot{s}{sy}", (0.12, 0.2, 0.06), steel, loc=(x, sy * 0.86, 1.0), bev=0.008, parent=P)
            nut(P, (x, sy * 0.86, 1.04))
    # hazard band: mustard round the shell with graphite diagonal stripes on it
    bx = 0.5
    K.cyl("band", R + 0.01, 0.45, M(MUSTARD, wear=0.55, grime=0.4), loc=(bx, 0, zc), rot=(0, 90, 0), segs=28, bev=0, parent=P)
    n = 16
    for k in range(n):
        a = 2 * math.pi * k / n
        ob = K.box("haz", (0.1, 0.5, 0.012), _dark(), loc=(bx, (R + 0.018) * math.sin(a), zc + (R + 0.018) * math.cos(a)), bev=0, parent=P)
        ob.rotation_euler = (Matrix.Rotation(-a, 4, "X") @ Matrix.Rotation(math.radians(55), 4, "Z")).to_euler()
    # label plate on the front
    K.box("plate", (0.9, 0.03, 0.36), steel, loc=(-1.3, -R - 0.03, zc + 0.15), bev=0.008, parent=P)
    stencil(P, "FUEL", (-1.3, -R - 0.05, zc + 0.15), 0.26, M(BONE, wear=0.5), extrude=0.006)
    warning_plate(P, (0.05, -R - 0.04, zc + 0.15), 0.26)
    # manway on top, with a lid, bolts and a handle; a vent pipe
    top = zc + R
    K.cyl("manway", 0.32, 0.3, steel, loc=(0.6, 0, top + 0.05), segs=20, bev=0.015, parent=P)
    K.cyl("lid", 0.36, 0.05, steel, loc=(0.6, 0, top + 0.22), segs=20, bev=0.015, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        nut(P, (0.6 + math.cos(a) * 0.31, math.sin(a) * 0.31, top + 0.25), r=0.025)
    K.torus("lid_handle", 0.1, 0.015, _dark(), loc=(0.6, 0, top + 0.25), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    K.cyl("vent", 0.05, 0.6, zinc, loc=(-1.3, 0.3, top + 0.2), segs=10, bev=0, parent=P)
    K.torus("vent_bend", 0.12, 0.05, zinc, loc=(-1.18, 0.3, top + 0.5), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    # ladder up the front to a small platform beside the manway
    ly = -R - 0.35
    for s in (-1, 1):
        K.box("lad_rail", (0.05, 0.05, top + 0.9), zinc, loc=(-0.3 + s * 0.22, ly, (top + 0.9) / 2), bev=0, parent=P)
        beam(P, "lad_stay", (-0.3 + s * 0.22, ly, 1.8), (-0.3 + s * 0.22, -R + 0.02, 1.8), 0.04, zinc)
        beam(P, "lad_stay", (-0.3 + s * 0.22, ly, top - 0.05), (-0.3 + s * 0.22, -0.55, top - 0.05), 0.04, zinc)
    for k in range(int((top - 0.1) / 0.3)):
        K.box("rung", (0.44, 0.035, 0.035), zinc, loc=(-0.3, ly, 0.3 + k * 0.3), bev=0, parent=P)
    for s in (-1, 1):
        beam(P, "lad_hand", (-0.3 + s * 0.22, ly, top + 0.88), (-0.3 + s * 0.22, -0.75, top + 0.88), 0.05, zinc)
        K.box("lad_hand_post", (0.05, 0.05, 0.9), zinc, loc=(-0.3 + s * 0.22, -0.75, top + 0.43), bev=0, parent=P)
    K.box("platform", (0.9, 0.9, 0.04), M(ZINC, metal=0.6, wear=0.6, grime=0.5), loc=(-0.3, -0.45, top - 0.01), bev=0.008, parent=P)
    # draw-off pipe from the +X head down to a valve
    rod(P, "drain", (L / 2 + 0.1, 0, zc - 0.7), (L / 2 + 0.55, 0, zc - 0.7), 0.06, steel, segs=12)
    rod(P, "drain_dn", (L / 2 + 0.55, 0, zc - 0.7), (L / 2 + 0.55, 0, 0.45), 0.06, steel, segs=12)
    K.box("valve", (0.2, 0.2, 0.2), _dark(), loc=(L / 2 + 0.55, 0, 0.6), bev=0.02, parent=P)
    K.torus("valve_wheel", 0.13, 0.015, M(SIGNAL, metal=0.3, wear=0.5), loc=(L / 2 + 0.55, -0.16, 0.6), rot=(90, 0, 0), segs=16, parent=P)
    K.cyl("gauge", 0.09, 0.05, M(BONE, wear=0.4), loc=(L / 2 + 0.25, -0.5, zc + 0.3), rot=(0, 90, 0), segs=14, bev=0.01, parent=P)
    return P


def _gas_cylinder(P, name, loc, col, band, rot=(0, 0, 0)):
    """A tall gas cylinder: body, shoulder, colour band, valve and guard cap."""
    G = _empty(P, name, loc, rot)
    r, h = 0.115, 1.3
    paint = M(col, metal=0.3, wear=0.6, grime=0.5, scratches=0.6)
    K.cyl("body", r, h, paint, loc=(0, 0, 0.08 + h / 2), segs=10, bev=0.015, parent=G)
    K.sphere("shoulder", r, paint, loc=(0, 0, 0.08 + h), scale=(1, 1, 0.65), parent=G)
    K.cyl("band", r + 0.004, 0.12, M(band, wear=0.5), loc=(0, 0, 0.08 + h - 0.12), segs=10, bev=0, parent=G)
    K.cyl("foot", r * 0.95, 0.1, _dark(), loc=(0, 0, 0.05), segs=10, bev=0, parent=G)
    K.cyl("neck", 0.035, 0.12, _zinc(), loc=(0, 0, h + 0.18), segs=8, bev=0, parent=G)
    K.box("valve", (0.07, 0.05, 0.06), M(ZINC, metal=0.7), loc=(0.03, 0, h + 0.2), bev=0, parent=G)
    return G


def gas_cylinder_rack(seed=29):
    """A steel cage rack of six tall gas cylinders under a little roof, chained in, a seventh lying where it fell."""
    rnd = random.Random(seed)
    P = _root("gas_rack")
    steel = _steel()
    zinc = _zinc()
    W, D, H = 2.0, 0.9, 1.9
    K.box("pad", (W + 0.5, D + 0.6, 0.12), _concrete(0.3), loc=(0, 0.1, 0.02), bev=0.03, parent=P)
    xs = (-W / 2, -W / 6, W / 6, W / 2)
    for x in xs:
        for sy in (-1, 1):
            K.box("post", (0.07, 0.07, H), steel, loc=(x, sy * D / 2, H / 2 + 0.08), bev=0.01, parent=P)
    for z in (0.2, 0.75, 1.35):
        for sy in (-1, 1):
            K.box("rail", (W, 0.05, 0.05), steel, loc=(0, sy * D / 2, z), bev=0, parent=P)
        for x in (-W / 2, W / 2):
            K.box("rail_s", (0.05, D, 0.05), steel, loc=(x, 0, z), bev=0, parent=P)
    K.box("floor", (W, D, 0.05), M(ZINC, metal=0.6, wear=0.6, grime=0.6), loc=(0, 0, 0.12), bev=0.008, parent=P)
    # mesh on the back and sides
    for k in range(13):
        x = -W / 2 + W * (k + 0.5) / 13
        K.box("mesh_v", (0.012, 0.012, H - 0.15), zinc, loc=(x, D / 2 + 0.03, H / 2 + 0.1), bev=0, parent=P)
    for j in range(8):
        z = 0.25 + j * 0.22
        K.box("mesh_h", (W, 0.012, 0.012), zinc, loc=(0, D / 2 + 0.03, z), bev=0, parent=P)
        for x in (-W / 2 - 0.03, W / 2 + 0.03):
            K.box("mesh_hs", (0.012, D, 0.012), zinc, loc=(x, 0, z), bev=0, parent=P)
    for x in (-W / 2 - 0.03, W / 2 + 0.03):
        for k in range(6):
            K.box("mesh_vs", (0.012, 0.012, H - 0.15), zinc, loc=(x, -D / 2 + D * (k + 0.5) / 6, H / 2 + 0.1), bev=0, parent=P)
    # roof sloping back, a red placard
    K.slab("roof", W + 0.35, D + 0.4, 0.06, 0.06, M(BONE, wear=0.6, grime=0.45, moss=0.25), loc=(0, 0.0, H + 0.13), rot=(-5, 0, 0), bev=0.015, parent=P)
    pl = _empty(P, "placard", (W / 2 - 0.35, -D / 2 - 0.05, H - 0.15), (90, 45, 0))
    K.box("placard_edge", (0.34, 0.34, 0.012), M(BONE, wear=0.5), parent=pl, bev=0.004)
    K.box("placard", (0.3, 0.3, 0.012), M(SIGNAL, wear=0.5), loc=(0, 0, 0.006), parent=pl, bev=0.004)
    # six cylinders in two rows of three, chained along the front
    cols = [SEA, STEEL, MUSTARD, ZINC, SIGNAL, GRAPHITE]
    bands = [BONE, MUSTARD, GRAPHITE, SIGNAL, BONE, MUSTARD]
    k = 0
    for row, y in enumerate((0.2, -0.2)):
        for i, x in enumerate((-W / 3, 0, W / 3)):
            _gas_cylinder(P, f"cyl{k}", (x + rnd.uniform(-0.05, 0.05), y, 0.14), cols[k], bands[k],
                          rot=(rnd.uniform(-2, 2), rnd.uniform(-2, 2), rnd.uniform(0, 360)))
            k += 1
    for i, x in enumerate((-W / 3, 0, W / 3)):
        rod(P, "chain", (x - W / 6 + 0.04, -D / 2 - 0.02, 1.05), (x, -D / 2 - 0.04, 0.98), 0.012, _dark(), segs=6)
        rod(P, "chain", (x, -D / 2 - 0.04, 0.98), (x + W / 6 - 0.04, -D / 2 - 0.02, 1.05), 0.012, _dark(), segs=6)
    # the one that fell out, and its cap
    _gas_cylinder(P, "fallen", (0.0, -1.0, 0.115), SEA, MUSTARD, rot=(0, -90, 12))
    K.cyl("cap", 0.07, 0.12, _dark(), loc=(0.95, -0.85, 0.07), rot=(70, 0, 30), segs=12, bev=0.01, parent=P)
    return P


# ================================================================== machines
def generator_trailer(seed=30):
    """A boxy generator set on a two-wheel trailer: framed case with vents, an exhaust stack and silencer, the control
    door hanging ajar on its panel, the drawbar down on a jockey wheel."""
    P = _root("generator")
    steel = _steel()
    gra = _dark()
    zinc = _zinc()
    bone = M(BONE, wear=0.6, grime=0.55, scratches=0.55, moss=0.1)
    # chassis
    zr = 0.6
    for sy in (-1, 1):
        K.box("chassis", (3.3, 0.1, 0.16), gra, loc=(0.1, sy * 0.6, zr), bev=0.012, parent=P)
        beam(P, "drawbar", (-1.5, sy * 0.6, zr), (-2.75, 0, zr - 0.08), 0.1, gra, d=0.12)
        K.box("stand", (0.08, 0.08, zr), gra, loc=(1.6, sy * 0.55, zr / 2), bev=0.01, parent=P)
        K.box("stand_foot", (0.2, 0.2, 0.03), gra, loc=(1.6, sy * 0.55, 0.015), bev=0.005, parent=P)
    for x in (-1.45, -0.4, 0.6, 1.65):
        K.box("cross", (0.08, 1.2, 0.12), gra, loc=(x, 0, zr), bev=0, parent=P)
    K.box("deck", (3.3, 1.35, 0.04), M(ZINC, metal=0.6, wear=0.6, grime=0.6), loc=(0.1, 0, zr + 0.1), bev=0.008, parent=P)
    K.box("hitch", (0.35, 0.18, 0.14), steel, loc=(-2.85, 0, zr - 0.08), bev=0.02, parent=P)
    K.cyl("jockey", 0.045, 0.6, zinc, loc=(-2.3, 0, zr - 0.25), segs=10, bev=0, parent=P)
    K.cyl("jockey_crank", 0.06, 0.12, gra, loc=(-2.3, 0, zr + 0.1), segs=10, bev=0.01, parent=P)
    K.cyl("jockey_wheel", 0.1, 0.06, K.mat("tyre", TYRE, rough=0.9, grime=0.3), loc=(-2.3, 0, 0.1), rot=(90, 0, 0), segs=14, bev=0.01, parent=P)
    # axle, wheels and guards
    K.cyl("axle", 0.04, 2.0, gra, loc=(0.4, 0, 0.38), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    for sy in (-1, 1):
        wheel(P, f"wheel{sy}", (0.4, sy * 0.95, 0.38))
        shell_panel(f"guard{sy}", 0.48, 0.1, math.pi - 0.1, -0.13, 0.13, M(STEEL, wear=0.6, grime=0.5), t=0.03, parent=P,
                    loc=(0.4, sy * 0.95, 0.38))
        K.box(f"guard_stay{sy}", (0.06, 0.3, 0.05), gra, loc=(0.4, sy * 0.78, zr + 0.06), bev=0.005, parent=P)
    # the case: framed bone panels on steel rails
    cx, cz = -0.05, zr + 0.12 + 0.75
    FR.framed_box(P, "case", (cx, 0, cz), (2.8, 1.3, 1.5), bone, steel, fw=0.1)
    FR.vent(P, (cx - 0.65, -0.66, cz - 0.05), 1.0, 0.7, steel, gra, n=5)
    FR.vent(P, (cx - 0.15, 0.66, cz + 0.05), 1.6, 0.8, steel, gra, n=4)
    FR.hazard(P, (cx, -0.67, zr + 0.24), 2.6, 0.12, M(MUSTARD, wear=0.5), gra, n=7)
    stencil(P, "GEN-2", (cx - 0.65, -0.616, cz + 0.48), 0.15, gra, extrude=0.006)
    # control door ajar over a panel of dead gauges
    K.box("panel", (0.76, 0.02, 0.9), gra, loc=(cx + 0.75, -0.63, cz), bev=0.005, parent=P)
    for k, (gx, gz) in enumerate(((-0.2, 0.25), (0.0, 0.25), (0.2, 0.25))):
        K.cyl("gauge", 0.07, 0.03, M(BONE, wear=0.3), loc=(cx + 0.75 + gx, -0.645, cz + gz), rot=(90, 0, 0), segs=10, bev=0, parent=P)
        K.box("needle", (0.008, 0.008, 0.06), M(SIGNAL), loc=(cx + 0.75 + gx, -0.665, cz + gz + 0.015), rot=(0, 30 - k * 40, 0), bev=0, parent=P)
    K.box("screen", (0.36, 0.015, 0.2), K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(cx + 0.75, -0.645, cz - 0.05), bev=0.004, parent=P)
    K.cyl("stop", 0.05, 0.05, M(SIGNAL, wear=0.3), loc=(cx + 0.95, -0.66, cz - 0.3), rot=(90, 0, 0), segs=12, bev=0.01, parent=P)
    for k in range(3):
        K.box("switch", (0.04, 0.03, 0.07), M(BONE), loc=(cx + 0.55 + k * 0.1, -0.65, cz - 0.3), bev=0, parent=P)
    hinge = _empty(P, "door_hinge", (cx + 0.35, -0.67, cz), (0, 0, -38))
    K.box("door", (0.82, 0.04, 1.0), M(STEEL, wear=0.65, grime=0.5), loc=(0.41, -0.02, 0), bev=0.01, parent=hinge)
    K.box("door_handle", (0.04, 0.05, 0.16), _zinc(), loc=(0.74, -0.06, 0), bev=0.005, parent=hinge)
    K.box("door_window", (0.4, 0.01, 0.25), K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(0.41, -0.045, 0.2), bev=0.003, parent=hinge)
    # exhaust: a silencer on the roof and a stack with a flap
    K.cyl("silencer", 0.15, 0.8, gra, loc=(cx - 0.6, 0.25, cz + 0.92), rot=(0, 90, 0), segs=14, bev=0.02, parent=P)
    for s in (-1, 1):
        K.box("sil_strap", (0.05, 0.34, 0.2), steel, loc=(cx - 0.6 + s * 0.25, 0.25, cz + 0.85), bev=0.008, parent=P)
    K.cyl("stack", 0.065, 0.9, M(RUST, metal=0.4, wear=0.6), loc=(cx - 1.05, 0.25, cz + 1.3), segs=12, bev=0, parent=P)
    rod(P, "stack_bend", (cx - 1.0, 0.25, cz + 0.92), (cx - 1.05, 0.25, cz + 0.88), 0.07, gra, segs=12)
    K.cyl("flap", 0.08, 0.015, gra, loc=(cx - 1.02, 0.25, cz + 1.78), rot=(0, 35, 0), segs=12, bev=0, parent=P)
    for x in (cx - 1.1, cx + 1.1):
        K.torus("lift_eye", 0.07, 0.018, zinc, loc=(x, 0, cz + 0.8), rot=(90, 0, 0), arc=180, segs=10, parent=P)
    K.cyl("fuel_cap", 0.07, 0.05, gra, loc=(cx + 1.0, 0.4, cz + 0.77), segs=12, bev=0.01, parent=P)
    return P


def transformer_box(seed=31):
    """A ground-mounted transformer cabinet on a concrete pad: steel-blue case with cooling fins down both sides,
    double doors with a hasp, a high-voltage plate, cables into the pad."""
    P = _root("transformer")
    gra = _dark()
    zinc = _zinc()
    K.box("pad", (2.4, 1.9, 0.22), _concrete(0.3), loc=(0, 0.05, 0.07), bev=0.03, parent=P)
    FR.hazard(P, (0, -0.91, 0.12), 2.3, 0.1, M(MUSTARD, wear=0.6), gra, n=12)
    cz = 0.18 + 0.8
    FR.framed_box(P, "case", (0, 0.05, cz), (1.3, 1.1, 1.6), M(STEEL, metal=0.3, wear=0.6, grime=0.5, moss=0.1), gra, fw=0.08)
    K.slab("lid", 1.45, 1.25, 0.08, 0.06, M(STEEL, metal=0.3, wear=0.6, grime=0.4, moss=0.3), loc=(0, 0.05, cz + 0.84), bev=0.02, parent=P)
    # fins on both sides between header pipes
    for s in (-1, 1):
        x = s * 0.65
        for k in range(10):
            y = -0.35 + k * 0.085
            K.box("fin", (0.26, 0.025, 1.15), M(STEEL, metal=0.4, wear=0.5, grime=0.5), loc=(x + s * 0.13, y + 0.05, cz - 0.05), bev=0, parent=P)
        for z in (cz - 0.6, cz + 0.52):
            K.cyl("header", 0.04, 0.9, gra, loc=(x + s * 0.13, 0.05, z), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    # double doors on the front
    fy = 0.05 - 0.56
    for s in (-1, 1):
        K.box("door", (0.53, 0.03, 1.3), M(STEEL, metal=0.3, wear=0.65, grime=0.5), loc=(s * 0.28, fy, cz - 0.02), bev=0.008, parent=P)
        for z in (cz - 0.45, cz + 0.4):
            K.box("hinge", (0.04, 0.05, 0.12), gra, loc=(s * 0.56, fy - 0.01, z), bev=0.005, parent=P)
    K.box("hasp", (0.14, 0.04, 0.05), zinc, loc=(0, fy - 0.03, cz - 0.1), bev=0.005, parent=P)
    K.box("padlock", (0.07, 0.03, 0.08), M(MUSTARD, metal=0.5, wear=0.4), loc=(0, fy - 0.05, cz - 0.17), bev=0.008, parent=P)
    K.torus("shackle", 0.025, 0.006, zinc, loc=(0, fy - 0.05, cz - 0.12), rot=(90, 0, 0), arc=180, segs=8, parent=P)
    warning_plate(P, (0, fy - 0.025, cz + 0.3), 0.32)
    K.box("rating", (0.28, 0.01, 0.16), M(ZINC, metal=0.6, wear=0.3), loc=(0.3, fy - 0.02, cz + 0.5), bev=0.003, parent=P)
    stencil(P, "TX-9", (-0.3, fy - 0.02, cz + 0.5), 0.12, M(BONE, wear=0.5), extrude=0.004)
    # three cables from the back into the pad, a lifting lug and an oil gauge
    for k in range(3):
        y0 = 0.6
        rod(P, "cable", (-0.25 + k * 0.25, y0, 0.55), (-0.25 + k * 0.25, y0 + 0.15, 0.25), 0.04, K.mat("cable", CABLE, rough=0.6), segs=8)
        K.cyl("gland", 0.06, 0.08, gra, loc=(-0.25 + k * 0.25, y0 - 0.02, 0.58), rot=(90, 0, 0), segs=10, bev=0.01, parent=P)
    for x in (-0.45, 0.45):
        K.torus("lug", 0.06, 0.016, zinc, loc=(x, 0.05, cz + 0.88), rot=(90, 0, 0), arc=180, segs=10, parent=P)
    K.cyl("oil_gauge", 0.06, 0.04, M(BONE, wear=0.3), loc=(0.45, fy - 0.03, cz + 0.6), rot=(90, 0, 0), segs=12, bev=0.008, parent=P)
    return P


# ================================================================== signal
def satellite_dish(seed=32):
    """A 3 m dish on a steel pedestal, tilted up at the sky: ribbed back, a hub on an elevation yoke, a three-strut feed
    arm and its receiver, the cable down the post to a box."""
    P = _root("dish")
    steel = _steel()
    gra = _dark()
    zinc = _zinc()
    K.box("pad", (1.5, 1.5, 0.3), _concrete(0.3), loc=(0, 0.25, 0.1), bev=0.04, parent=P)
    K.cyl("flange", 0.32, 0.06, gra, loc=(0, 0.25, 0.28), segs=16, bev=0.01, parent=P)
    for k in range(6):
        a = 2 * math.pi * k / 6
        nut(P, (math.cos(a) * 0.26, 0.25 + math.sin(a) * 0.26, 0.31), r=0.025)
    K.cyl("post", 0.16, 1.9, steel, loc=(0, 0.25, 1.25), segs=16, bev=0.02, parent=P)
    for k in range(4):
        a = k * 90 + 45
        g = K.box("gusset", (0.3, 0.02, 0.3), steel, loc=(math.cos(math.radians(a)) * 0.2, 0.25 + math.sin(math.radians(a)) * 0.2, 0.42),
                  rot=(0, 0, a), bev=0.004, parent=P)
    K.box("turntable", (0.5, 0.5, 0.18), gra, loc=(0, 0.25, 2.25), bev=0.03, parent=P)
    for s in (-1, 1):
        K.box("yoke", (0.07, 0.3, 0.5), gra, loc=(s * 0.3, 0.25, 2.55), bev=0.015, parent=P)
    K.cyl("trunnion", 0.08, 0.72, zinc, loc=(0, 0.25, 2.65), rot=(0, 90, 0), segs=12, bev=0.01, parent=P)
    # the dish: pointing 40 degrees above the horizon towards the front, a little to the left
    D = _empty(P, "dish", (0, 0.25, 2.65), (52, 0, -18))
    R, depth, t = 1.5, 0.45, 0.04
    shell = dish_mesh("reflector", R, depth, t, M(BONE, metal=0.2, wear=0.5, grime=0.45, moss=0.2))
    K.place(shell, (0, 0, 0.32), (0, 0, 0), D)
    K.torus("rim", R + 0.01, 0.03, steel, loc=(0, 0, 0.32 + depth - 0.02), segs=36, parent=D)
    K.cyl("hub", 0.3, 0.3, gra, loc=(0, 0, 0.12), segs=16, bev=0.02, parent=D)
    for k in range(8):
        a = 2 * math.pi * k / 8
        c, s = math.cos(a), math.sin(a)
        pts = [0.25, 0.85, R - 0.05]
        for i in range(2):
            r0, r1 = pts[i], pts[i + 1]
            z0 = 0.32 + depth * (r0 / R) ** 2 - t - 0.04
            z1 = 0.32 + depth * (r1 / R) ** 2 - t - 0.04
            beam(D, "rib", (c * r0, s * r0, z0), (c * r1, s * r1, z1), 0.05, steel, d=0.07)
    f = depth + R * R / (4 * depth) * 0.0 + 1.25
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        rod(D, "feed_strut", (math.cos(a) * (R - 0.05), math.sin(a) * (R - 0.05), 0.32 + depth), (0, 0, 0.32 + f - 0.1), 0.025, zinc, segs=8)
    K.cyl("lnb", 0.09, 0.3, gra, loc=(0, 0, 0.32 + f), segs=14, bev=0.02, parent=D)
    K.cyl("horn", 0.12, 0.1, M(BONE, wear=0.4), loc=(0, 0, 0.32 + f - 0.19), segs=14, bev=0.01, r2=0.08, parent=D)
    # cable down the post into a box
    K.cyl("cable_dn", 0.02, 1.7, K.mat("cable", CABLE, rough=0.6), loc=(0.18, 0.25, 1.3), segs=6, bev=0, parent=P)
    FR.framed_box(P, "box", (0.0, -0.05, 0.62), (0.4, 0.2, 0.5), M(BONE, wear=0.55), steel, fw=0.04)
    K.box("box_strap", (0.06, 0.36, 0.04), zinc, loc=(0.0, 0.1, 0.8), bev=0, parent=P)
    return P


def radio_mast(seed=33):
    """A 12 m guyed triangular lattice mast, painted in red and bone bands: guy wires at two heights to three anchors,
    whips and a dead beacon on top, a small framed equipment hut at its foot with a cable tray across."""
    P = _root("mast")
    zinc = _zinc()
    gra = _dark()
    red = M(SIGNAL, metal=0.3, wear=0.6, grime=0.4)
    white = M(BONE, metal=0.2, wear=0.6, grime=0.45)
    K.box("pad", (1.3, 1.3, 0.3), _concrete(0.3), loc=(0, 0, 0.1), bev=0.04, parent=P)
    a_r = 0.5 / math.sqrt(3)
    angs = [math.radians(a) for a in (90, 210, 330)]
    corners = [(math.cos(a) * a_r, math.sin(a) * a_r) for a in angs]
    z0, H = 0.25, 12.0
    sec = 1.0
    n = int((H - z0) / sec)
    top = z0 + n * sec
    for i in range(n + 1):
        za = z0 + i * sec
        zb = min(z0 + (i + 1) * sec, H)
        if i < n and i % 2 == 0:
            # legs in 2 m bands of red and bone
            m = red if (i // 2) % 2 == 0 else white
            for (cx, cy) in corners:
                beam(P, "leg", (cx, cy, za), (cx, cy, min(za + 2 * sec, top)), 0.05, m)
        for f in range(3):
            (ax, ay), (bx, by) = corners[f], corners[(f + 1) % 3]
            beam(P, "horiz", (ax, ay, za), (bx, by, za), 0.03, zinc)
            if i < n:
                beam(P, "diag", (ax, ay, za), (bx, by, zb), 0.025, zinc)
    K.box("top_plate", (0.55, 0.55, 0.05), gra, loc=(0, 0, top + 0.02), bev=0.01, parent=P)
    K.cyl("beacon_base", 0.08, 0.1, gra, loc=(0, 0, top + 0.1), segs=12, bev=0.01, parent=P)
    K.sphere("beacon", 0.1, K.mat("deadlamp_red", "#5a3a3a", rough=0.3), loc=(0, 0, top + 0.2), scale=(1, 1, 1.3), parent=P)
    for k, (cx, cy) in enumerate(corners):
        K.cyl("whip", 0.015, 1.6 + k * 0.3, zinc, loc=(cx * 1.3, cy * 1.3, top + 0.8 + k * 0.15), segs=6, bev=0, parent=P)
    for k, a in enumerate(angs[:2]):
        K.box("panel_ant", (0.18, 0.08, 0.8), white, loc=(math.cos(a + 0.6) * 0.4, math.sin(a + 0.6) * 0.4, top - 0.8), rot=(0, 0, math.degrees(a + 0.6) + 90),
              bev=0.02, parent=P)
    K.sphere("mw_dish", 0.35, white, loc=(0.0, -0.4, 8.2), scale=(1, 0.35, 1), parent=P)
    K.box("mw_mount", (0.1, 0.3, 0.1), gra, loc=(0.0, -0.2, 8.2), bev=0.01, parent=P)
    # guys: three anchors, two wires each
    Rg = 6.5
    for k, a in enumerate(angs):
        ax, ay = math.cos(a) * Rg, math.sin(a) * Rg
        K.box("anchor", (0.7, 0.7, 0.4), _concrete(0.4), loc=(ax, ay, 0.1), rot=(0, 0, math.degrees(a)), bev=0.04, parent=P)
        K.box("anchor_plate", (0.3, 0.12, 0.25), gra, loc=(ax - math.cos(a) * 0.2, ay - math.sin(a) * 0.2, 0.42), rot=(0, 0, math.degrees(a) + 90), bev=0.01, parent=P)
        cx, cy = corners[k]
        for zh in (5.25, 10.25):
            start = (ax - math.cos(a) * 0.25, ay - math.sin(a) * 0.25, 0.5)
            end = (cx, cy, zh)
            beam(P, "guy", start, end, 0.022, zinc)
            tb = Vector(start).lerp(Vector(end), 0.06)
            beam(P, "turnbuckle", tb - (Vector(end) - Vector(start)).normalized() * 0.15, tb + (Vector(end) - Vector(start)).normalized() * 0.15,
                 0.05, gra)
    # the hut: bone framed panels, steel-blue door, a vent and an air-conditioning box, a cable tray to the mast
    hx, hy = 2.4, 1.1
    K.box("hut_pad", (2.5, 2.1, 0.15), _concrete(0.25), loc=(hx, hy, 0.04), bev=0.03, parent=P)
    FR.framed_box(P, "hut", (hx, hy, 0.12 + 1.2), (2.2, 1.8, 2.4), M(BONE, wear=0.55, grime=0.5, moss=0.1), _steel(), fw=0.1)
    K.slab("hut_roof", 2.5, 2.1, 0.1, 0.08, M(STEEL, wear=0.6, grime=0.4, moss=0.3), loc=(hx, hy, 2.6), bev=0.02, parent=P)
    dy = hy - 0.92
    K.box("door", (0.95, 0.05, 2.0), M(STEEL, wear=0.65, grime=0.5), loc=(hx + 0.4, dy, 0.15 + 1.0), bev=0.01, parent=P)
    K.box("door_handle", (0.12, 0.05, 0.04), zinc, loc=(hx + 0.05, dy - 0.04, 1.15), bev=0.005, parent=P)
    warning_plate(P, (hx + 0.4, dy - 0.03, 1.75), 0.26)
    FR.vent(P, (hx - 0.55, dy, 1.75), 0.6, 0.4, _steel(), gra)
    stencil(P, "RM-3", (hx - 0.55, hy - 0.865, 1.2), 0.2, gra, extrude=0.006)
    ac = (hx + 1.25, hy + 0.2, 1.0)
    K.box("ac", (0.35, 0.8, 0.7), M(BONE, wear=0.5, grime=0.5), loc=ac, bev=0.025, parent=P)
    K.torus("ac_ring", 0.25, 0.025, gra, loc=(ac[0] + 0.18, ac[1], ac[2]), rot=(0, 90, 0), segs=14, parent=P)
    for k in range(3):
        K.box("ac_blade", (0.02, 0.44, 0.08), gra, loc=(ac[0] + 0.17, ac[1], ac[2]), rot=(k * 60, 0, 0), bev=0, parent=P)
    tray_z = 2.25
    K.box("tray", (hx - 1.1 - 0.25, 0.25, 0.04), M(ZINC, metal=0.6, wear=0.5), loc=((hx - 1.1 + 0.25) / 2, hy - 0.4, tray_z), bev=0, parent=P)
    for s in (-1, 1):
        K.box("tray_side", (hx - 1.1 - 0.25, 0.02, 0.08), zinc, loc=((hx - 1.1 + 0.25) / 2, hy - 0.4 + s * 0.12, tray_z + 0.04), bev=0, parent=P)
    K.box("tray_post", (0.06, 0.06, tray_z), gra, loc=(0.7, hy - 0.4, tray_z / 2), bev=0.008, parent=P)
    rod(P, "feeder", (0.25, hy - 0.4, tray_z + 0.05), (0.05, 0.1, 6.0), 0.025, K.mat("cable", CABLE, rough=0.6), segs=6)
    return P


PROPS = {
    "power_pylon": power_pylon,
    "power_pole": power_pole,
    "street_lamp_broken": street_lamp_broken,
    "bus_shelter": bus_shelter,
    "billboard_frame": billboard_frame,
    "shipping_container": shipping_container,
    "shipping_container_open": shipping_container_open,
    "fuel_tank": fuel_tank,
    "gas_cylinder_rack": gas_cylinder_rack,
    "generator_trailer": generator_trailer,
    "transformer_box": transformer_box,
    "satellite_dish": satellite_dish,
    "radio_mast": radio_mast,
}
