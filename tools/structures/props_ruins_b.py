"""More props of the Old Stone ruins (docs/world-generation.md, Scenes), in the Old Stone family of docs/art-direction.md.

Old Stone: the people before the colony built in dressed stone blocks with carved bands and ring glyphs; time has cracked
them and moss grows on their top edges. Here: a rune stone, a curved bench, a planter, an aqueduct section, a wall
corner, a wall with a window, a rubble pile, a headless statue, wide ceremonial steps, a basin, a milestone, a capped
well, a fallen slab, a broken pillar stump, a ring gate, a glyph pillar, a carved boulder, a small statue head, a stone
chest, a ruined platform, a bollard, a ring of marker stones, a fallen lintel, a threshold and a gargoyle. Cyan appears
only on the probe crystals the colony's probes left growing out of the stone.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
from mathutils import Vector

import kit as K
from families import CYAN, GRAPHITE, glow, stone
from props import _root
from props_stone import SNAG, WATER, dstone, earth, empty, limb, moss, rock, tapered

GRAVEL = "#6d7068"


# ------------------------------------------------------------------ helpers
def dark():
    """The dark of a hole or an open lid."""
    return K.mat("ruin_dark", GRAPHITE, rough=1.0)


def gravel():
    """Broken stone and grit packed round fallen blocks."""
    return K.mat("ruin_gravel", GRAVEL, rough=1.0, grime=0.3, noise_scale=12.0, cracks=0.3)


def arc_block(name, r0, r1, a0, a1, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, n=4, bev=0.04):
    """A curved block: the ring sector between radii r0 and r1 from angle a0 to a1 (degrees) in XY, h tall about z=0."""
    bm = bmesh.new()
    rings = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        c, s = math.cos(a), math.sin(a)
        rings.append([bm.verts.new((r * c, r * s, z)) for r, z in ((r0, -h / 2), (r1, -h / 2), (r1, h / 2), (r0, h / 2))])
    for i in range(n):
        A, B = rings[i], rings[i + 1]
        for j in range(4):
            k = (j + 1) % 4
            bm.faces.new((A[j], A[k], B[k], B[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2)
    return K.place(ob, loc, rot, parent)


def prism(name, pts, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, bev=0.03):
    """An outline in XY (any shape, convex or not) stood up h tall from z=0."""
    bm = bmesh.new()
    b = [bm.verts.new((x, y, 0)) for x, y in pts]
    t = [bm.verts.new((x, y, h)) for x, y in pts]
    bm.faces.new(b)
    bm.faces.new(t)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((b[i], b[j], t[j], t[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2, angle=30)
    return K.place(ob, loc, rot, parent)


def lathe(name, prof, mat, segs=24, loc=(0, 0, 0), parent=None):
    """A turned stone: the profile [(radius, z), ...] from the bottom up, swept round Z and capped at both ends."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segs), r * math.sin(2 * math.pi * i / segs), z)) for i in range(segs)])
    for A, B in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((A[i], A[j], B[j], B[i]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(35))
    return K.place(ob, loc, (0, 0, 0), parent)


def boulder(name, radii, mat, rnd, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, cut_front=None, sub=2):
    """A field boulder: a lumpy ellipsoid, flat underneath; cut_front shears the -Y side flat for a carved face."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    ph = [rnd.uniform(0, 6.28) for _ in range(6)]
    for v in bm.verts:
        d = v.co.normalized()
        f = 1 + 0.08 * math.sin(3 * d.x + ph[0]) * math.cos(2 * d.y + ph[1]) + 0.06 * math.sin(4 * d.z + ph[2]) + 0.04 * math.sin(5 * d.x + 3 * d.y + ph[3])
        v.co = Vector((d.x * radii[0] * f, d.y * radii[1] * f, d.z * radii[2] * f))
        v.co.z = max(v.co.z, -radii[2] * 0.6)
        if cut_front is not None:
            v.co.y = max(v.co.y, -cut_front)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(40))
    return K.place(ob, loc, rot, parent)


def face(P, name, loc, rot=(0, 0, 0)):
    """An empty on a carved face: local x runs across it, z up it, and -y points out of it."""
    return empty(P, name, loc, rot)


def glyph(F, name, kind, x, z, s, mat):
    """One glyph about s across, cut into the face F at (x, z): ring, eye, stem, fork, bars, chevron or steps."""
    y = -0.006
    w = max(0.014, s * 0.13)

    def bar(n, bx, bz, L, ang=0):
        K.box(f"{name}_{n}", (w, 0.02, L), mat, loc=(x + bx, y, z + bz), rot=(0, ang, 0), bev=0, parent=F)

    if kind in ("ring", "eye", "stem"):
        R = s * (0.3 if kind == "stem" else 0.4)
        zc = s * 0.2 if kind == "stem" else 0
        K.torus(f"{name}_ring", R, w * 0.5, mat, loc=(x, y, z + zc), rot=(90, 0, 0), segs=12, parent=F)
        if kind == "ring":
            K.box(f"{name}_dot", (w * 1.5, 0.02, w * 1.5), mat, loc=(x, y, z), bev=0, parent=F)
        elif kind == "eye":
            bar("line", 0, 0, s * 1.05, 90)
        else:
            bar("stem", 0, -s * 0.3, s * 0.5)
    elif kind == "fork":
        bar("stem", 0, -s * 0.1, s * 0.75)
        bar("armL", -s * 0.17, s * 0.28, s * 0.4, -35)
        bar("armR", s * 0.17, s * 0.28, s * 0.4, 35)
    elif kind == "bars":
        for i, L in enumerate((0.9, 0.65, 0.4)):
            bar(f"bar{i}", 0, s * (0.3 - 0.3 * i), s * L, 90)
    elif kind == "chevron":
        bar("l", -s * 0.18, 0, s * 0.6, 35)
        bar("r", s * 0.18, 0, s * 0.6, -35)
        bar("dot", 0, -s * 0.35, w * 1.5)
    else:  # steps
        bar("v0", -s * 0.3, -s * 0.2, s * 0.4)
        bar("h0", -s * 0.15, 0.0, s * 0.3, 90)
        bar("v1", 0.0, s * 0.15, s * 0.3)
        bar("h1", s * 0.15, s * 0.3, s * 0.3, 90)


KINDS = ("ring", "fork", "eye", "bars", "stem", "chevron", "steps")


def glyph_row(F, name, x0, x1, z, s, mat, rnd, n=None):
    """A row of glyphs evenly spaced from x0 to x1."""
    n = n or max(1, int((x1 - x0) / (s * 1.5)) + 1)
    for i in range(n):
        x = (x0 + x1) / 2 if n == 1 else x0 + (x1 - x0) * i / (n - 1)
        glyph(F, f"{name}{i}", rnd.choice(KINDS), x, z, s, mat)


def courses(P, name, x0, x1, z0, nc, ch, th, top, rnd, s, sd, y=0.0, yaw=0.0, holes=()):
    """Courses of dressed blocks in running bond along X from x0 to x1, each course stopping at top(x); blocks are cut
    back round each hole (x0, x1, z0, z1), a window opening or the slot of its lintel."""
    G = empty(P, name, (0, y, 0), (0, 0, yaw))
    for k in range(nc):
        z = z0 + k * ch
        x = x0
        first = True
        while x < x1 - 0.1:
            L = 0.45 if (first and k % 2) else rnd.uniform(0.8, 1.05)
            first = False
            L = min(L, x1 - x)
            a, b = x, x + L
            x += L
            if z + ch > top((a + b) / 2) + 0.01:
                continue
            parts = [(a, b)]
            for h0, h1, hz0, hz1 in holes:
                if z + ch > hz0 + 0.02 and z < hz1 - 0.02:
                    parts = [(p2, q2) for p, q in parts for p2, q2 in ((p, min(q, h0)), (max(p, h1), q)) if q2 - p2 > 0.12]
            for p, q in parts:
                K.box(f"{name}{k}_{p:.2f}", (q - p - 0.03, th - rnd.uniform(0, 0.04), ch - 0.03), sd if rnd.random() < 0.15 else s,
                      loc=((p + q) / 2, rnd.uniform(-0.015, 0.015), z + ch / 2), rot=(0, 0, rnd.uniform(-1, 1)), bev=0.05, parent=G)
    return G


def crystals(P, loc, rnd, n=3, h=0.7):
    """A small cluster of colony probe crystal growing out of the stone."""
    for k in range(n):
        K.cyl(f"crystal{k}", 0.1 + 0.02 * k, h * (0.7 + 0.25 * k), glow(CYAN, 3), loc=(loc[0] + 0.12 * k - 0.12, loc[1] + rnd.uniform(-0.08, 0.08), loc[2] + 0.2),
              rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), 0), segs=6, bev=0, parent=P, r2=0.02)


# ================================================================== props
def rune_stone(seed=41):
    """A squat stone a metre high, broader than tall, a carved ring glyph with four ticks on its face and a band round it."""
    rnd = random.Random(seed)
    P = _root("runestone")
    s, sd = stone(), dstone()
    G = empty(P, "lean", (0, 0, -0.1), (3, -2, rnd.uniform(-6, 6)))
    tapered("stone", (1.3, 0.8), (1.0, 0.62), 1.05, s, bev=0.08, parent=G, shift=(0.03, 0.02), slope=0.12)
    tapered("band", (1.08, 0.69), (1.06, 0.68), 0.08, sd, loc=(0.025, 0.017, 0.82), bev=0.01, parent=G)
    F = face(G, "face", (0.0, -0.37, 0.48), (-6.5, 0, 0))
    K.torus("glyph_ring", 0.2, 0.024, sd, loc=(0, -0.006, 0), rot=(90, 0, 0), segs=20, parent=F)
    K.torus("glyph_inner", 0.08, 0.018, sd, loc=(0, -0.006, 0), rot=(90, 0, 0), segs=12, parent=F)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        K.box(f"tick{k}", (0.03, 0.02, 0.12), sd, loc=(math.cos(a) * 0.3, -0.006, math.sin(a) * 0.3), rot=(0, -math.degrees(a) + 90, 0), bev=0, parent=F)
    K.box("crack", (0.03, 0.02, 0.45), sd, loc=(0.42, -0.36, 0.3), rot=(-6, 15, 0), bev=0, parent=G)
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        rock(P, f"pack{k}", rnd.uniform(0.12, 0.18), 0.12, (math.cos(a) * 0.75, math.sin(a) * 0.5, 0.03), rnd)
    moss(P, "moss0", (-0.2, 0.05, 0.98), 0.22, flat=0.25)
    moss(P, "moss1", (0.55, 0.35, 0.0), 0.25, flat=0.4)
    return P


def stone_bench_curved(seed=42):
    """A curved stone bench about 3 m across: three seat stones on a ring of legs, a low back, the right end cracked and sunk."""
    rnd = random.Random(seed)
    P = _root("curvedbench")
    s, sd = stone(), dstone()
    R, cy = 2.2, -1.83
    C = empty(P, "circle", (0, cy, 0))
    a_lo, a_hi = 42, 138
    span = (a_hi - a_lo) / 3
    for k in range(3):
        a0, a1 = a_lo + span * k + 0.5, a_lo + span * (k + 1) - 0.5
        sunk = k == 0
        arc_block(f"seat{k}", R - 0.26, R + 0.26, a0, a1, 0.13, s, loc=(0, 0, 0.42 - (0.04 if sunk else 0)),
                  rot=((2, -3, 0) if sunk else (0, 0, 0)), parent=C, bev=0.035)
        arc_block(f"lip{k}", R - 0.275, R - 0.26, a0 + 1, a1 - 1, 0.03, sd, loc=(0, 0, 0.4 - (0.04 if sunk else 0)), rot=((2, -3, 0) if sunk else (0, 0, 0)),
                  parent=C, n=3, bev=0)
        hb = 0.5 if k else 0.3
        arc_block(f"back{k}", R + 0.27, R + 0.45, a0, a1, hb, s, loc=(0, 0, 0.48 + hb / 2), parent=C, bev=0.035)
        if k:
            arc_block(f"backband{k}", R + 0.262, R + 0.27, a0 + 1, a1 - 1, 0.05, sd, loc=(0, 0, 0.86), parent=C, n=3, bev=0)
    # the broken top of the right back lies behind it
    K.box("backchunk", (0.6, 0.2, 0.25), s, loc=(1.75, 0.15, 0.1), rot=(70, 0, -40), bev=0.03, parent=P)
    for k in range(4):
        a = a_lo + span * k + (3 if k == 0 else -3 if k == 3 else 0)
        ar = math.radians(a)
        K.box(f"leg{k}", (0.38, 0.5, 0.36), s, loc=(math.cos(ar) * R, math.sin(ar) * R, 0.18), rot=(0, 0, a + 90 + rnd.uniform(-2, 2)), bev=0.04, parent=C)
        K.box(f"back_foot{k}", (0.3, 0.2, 0.5), s, loc=(math.cos(ar) * (R + 0.36), math.sin(ar) * (R + 0.36), 0.24), rot=(0, 0, a + 90), bev=0.04, parent=C)
    rock(P, "crumb", 0.1, 0.07, (-0.3, -0.75, 0.03), rnd)
    moss(P, "moss0", (0.4, 0.45, 0.98), 0.14, flat=0.3)
    moss(P, "moss1", (-1.0, 0.2, 0.74), 0.13, flat=0.3)
    moss(P, "moss2", (1.4, -0.7, 0.0), 0.22, flat=0.4)
    return P


def stone_planter(seed=43):
    """A carved planter box 1.3 m square on a plinth, a ring glyph on its face, a dead bush standing in the dry earth."""
    rnd = random.Random(seed)
    P = _root("planter")
    s, sd = stone(), dstone()
    K.box("plinth", (1.5, 1.5, 0.14), s, loc=(0, 0, 0.06), bev=0.04, parent=P)
    W, H, t = 1.3, 0.62, 0.16
    for sy in (-1, 1):
        K.box(f"side_y{sy}", (W, t, H), s, loc=(0, sy * (W - t) / 2, 0.12 + H / 2), bev=0.04, parent=P)
        K.box(f"side_x{sy}", (t, W - 2 * t + 0.02, H), s, loc=(sy * (W - t) / 2, 0, 0.12 + H / 2), bev=0.04, parent=P)
    K.box("band", (W + 0.02, W + 0.02, 0.05), sd, loc=(0, 0, 0.6), bev=0, parent=P)
    for sy in (-1, 1):
        K.box(f"rim_y{sy}", (W + 0.1, 0.24, 0.08), s, loc=(0, sy * (W - 0.14) / 2, 0.78), bev=0.025, parent=P)
        K.box(f"rim_x{sy}", (0.24, W - 0.38, 0.08), s, loc=(sy * (W - 0.14) / 2, 0, 0.78), bev=0.025, parent=P)
    F = face(P, "face", (0, -W / 2, 0.38))
    glyph(F, "g0", "ring", 0, 0, 0.26, sd)
    glyph(F, "g1", "bars", -0.38, 0, 0.18, sd)
    glyph(F, "g2", "bars", 0.38, 0, 0.18, sd)
    K.box("crack", (0.03, 0.02, 0.4), sd, loc=(0.45, -W / 2 - 0.004, 0.55), rot=(0, -20, 0), bev=0, parent=P)
    K.box("soil", (W - 2 * t, W - 2 * t, 0.04), earth(), loc=(0, 0, 0.66), bev=0, parent=P)
    # the dead bush: grey stems spreading from one root, each with a twig or two
    grey = K.mat("snag", SNAG, rough=0.9, grime=0.2, noise_scale=6.0, cracks=0.6)
    for k in range(6):
        yaw = 60 * k + rnd.uniform(-20, 20)
        pitch = rnd.uniform(52, 78)
        L = rnd.uniform(0.55, 0.95)
        base = (0.05, 0.05, 0.66)
        limb(P, f"stem{k}", base, yaw, pitch, L, 0.035, 0.012, grey, segs=5, bev=0)
        f = rnd.uniform(0.45, 0.65)
        cp = math.cos(math.radians(pitch))
        mid = (base[0] + cp * math.cos(math.radians(yaw)) * L * f, base[1] + cp * math.sin(math.radians(yaw)) * L * f, base[2] + math.sin(math.radians(pitch)) * L * f)
        limb(P, f"twig{k}", mid, yaw + rnd.choice((-45, 45)), pitch - 15, L * 0.4, 0.016, 0.006, grey, segs=4, bev=0)
    rock(P, "chip", 0.1, 0.08, (0.85, -0.7, 0.03), rnd)
    moss(P, "moss0", (-0.55, -0.55, 0.82), 0.13, flat=0.3)
    moss(P, "moss1", (-0.85, 0.5, 0.0), 0.22, flat=0.4)
    return P


def aqueduct_section(seed=44):
    """An 8 m section of aqueduct about 4.5 m high: three piers, two arches of voussoirs, the stone channel on top with
    water still standing in it, its end broken off."""
    rnd = random.Random(seed)
    P = _root("aqueduct")
    s, sd = stone(), dstone()
    D = 1.3
    z0 = 2.0
    Ri, Ro = 1.3, 1.72
    xs = (-3.6, 0.0, 3.6)
    for i, px in enumerate(xs):
        w = 1.0 if i == 1 else 1.2
        cx = px + (0.1 if i == 0 else -0.1 if i == 2 else 0)
        K.box(f"footing{i}", (w + 0.35, D + 0.35, 0.3), s, loc=(cx, 0, 0.1), bev=0.05, parent=P)
        z = 0.25
        k = 0
        while z < z0 - 0.3:
            h = min(rnd.uniform(0.5, 0.62), z0 - 0.15 - z)
            K.box(f"pier{i}_{k}", (w + rnd.uniform(-0.03, 0.03), D, h - 0.02), sd if rnd.random() < 0.15 else s,
                  loc=(cx + rnd.uniform(-0.02, 0.02), 0, z + h / 2), rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.05, parent=P)
            z += h
            k += 1
        K.box(f"impost{i}", (w + 0.16, D + 0.12, 0.17), sd, loc=(cx, 0, z0 - 0.07), bev=0.03, parent=P)
    deck = z0 + Ro + 0.12
    # the spandrel wall over both arches, a little behind the arch rings
    pts = [(-4.2, z0)]
    for cx in (-1.8, 1.8):
        pts += [(cx + Ro * math.cos(math.radians(a)), z0 + Ro * math.sin(math.radians(a))) for a in range(180, -1, -20)]
    pts += [(4.2, z0), (4.2, deck), (-4.2, deck)]
    prism("spandrel", pts, D - 0.12, s, loc=(0, (D - 0.12) / 2, 0), rot=(90, 0, 0), parent=P, bev=0.03)
    for j, cx in enumerate((-1.8, 1.8)):
        n = 9
        for k in range(n):
            a0, a1 = 180 * k / n + 0.6, 180 * (k + 1) / n - 0.6
            arc_block(f"vous{j}_{k}", Ri, Ro, a0, a1, D, sd if (k == 4 or rnd.random() < 0.12) else s, loc=(cx, 0, z0), rot=(90, 0, 0), parent=P, n=2, bev=0.04)
    # the cornice band and the channel: a floor and two side walls, the right end broken away
    K.box("cornice", (8.5, D + 0.2, 0.16), sd, loc=(0, 0, deck + 0.02), bev=0.03, parent=P)
    K.box("floor", (8.4, D, 0.2), s, loc=(0, 0, deck + 0.2), bev=0.04, parent=P)
    for sy in (-1, 1):
        x = -4.2
        k = 0
        while x < 4.1:
            L = min(rnd.uniform(1.2, 1.5), 4.2 - x)
            missing = sy == -1 and x > 2.6
            if not missing:
                hh = 0.5 if x + L < 3.9 else 0.32
                K.box(f"wall{sy}_{k}", (L - 0.03, 0.26, hh), s, loc=(x + L / 2, sy * (D / 2 - 0.13), deck + 0.3 + hh / 2), rot=(0, 0, rnd.uniform(-0.8, 0.8)),
                      bev=0.04, parent=P)
            x += L
            k += 1
    K.box("water", (8.2, D - 0.56, 0.02), K.mat("stone_water", WATER, rough=0.05), loc=(-0.1, 0, deck + 0.42), bev=0, parent=P)
    # the fallen wall stones at the foot of the broken end
    K.box("fallen0", (1.3, 0.26, 0.5), s, loc=(3.4, -1.5, 0.13), rot=(88, 0, 12), bev=0.04, parent=P)
    K.box("fallen1", (0.6, 0.3, 0.26), s, loc=(4.3, -1.05, 0.12), rot=(10, 6, 40), bev=0.04, parent=P)
    rock(P, "chip0", 0.2, 0.14, (2.6, -1.2, 0.05), rnd)
    rock(P, "chip1", 0.14, 0.1, (-2.2, 1.2, 0.04), rnd)
    for k, (x, y) in enumerate(((-3.2, 0.5), (-1.2, -0.52), (0.8, 0.52), (2.2, 0.52))):
        moss(P, f"moss{k}", (x, y, deck + 0.8), 0.22, flat=0.3, stretch=1.8)
    moss(P, "moss_floor", (3.6, -0.3, deck + 0.3), 0.3, flat=0.25)
    moss(P, "moss_foot0", (-3.0, -0.95, 0.0), 0.35, flat=0.4)
    moss(P, "moss_foot1", (0.6, 0.95, 0.0), 0.3, flat=0.4)
    crystals(P, (-3.0, -1.05, 0.0), rnd)
    return P


def stone_wall_corner(seed=45):
    """The L-shaped corner of a ruined building: two walls of dressed courses meeting 2.3 m high, crumbling away from it."""
    rnd = random.Random(seed)
    P = _root("wallcorner")
    s, sd = stone(), dstone()
    th, ch = 0.6, 0.45
    cx, cy = -1.3, 1.0
    K.box("plinth_a", (3.6, th + 0.14, 0.22), s, loc=(cx - 0.3 + 1.8, cy, 0.07), bev=0.05, parent=P)
    K.box("plinth_b", (th + 0.14, 2.86, 0.22), s, loc=(cx, cy - 0.3 - 1.13, 0.07), bev=0.05, parent=P)
    # wall A runs from the corner towards +X, wall B towards -Y; their courses lap at the corner
    def top_a(x):
        return 2.5 - 0.6 * max(0.0, x) - (0.3 if 1.2 < x < 1.9 else 0)

    def top_b(x):
        return 2.5 - 0.62 * x

    A = courses(P, "a", -0.3, 3.3, 0.18, 5, ch, th, top_a, rnd, s, sd)
    A.location = (cx, cy, 0)
    B = courses(P, "b", 0.3, 2.85, 0.18, 5, ch, th, top_b, rnd, s, sd, yaw=-90)
    B.location = (cx, cy, 0)
    for k in range(7):
        x = rnd.uniform(0.6, 2.4)
        y = rnd.uniform(-1.8, 0.0)
        big = k < 3
        sz = (rnd.uniform(0.7, 0.95), 0.55, 0.42) if big else (rnd.uniform(0.25, 0.4), rnd.uniform(0.2, 0.3), rnd.uniform(0.15, 0.25))
        K.box(f"rubble{k}", sz, s if rnd.random() < 0.8 else sd, loc=(x, y, sz[2] * 0.4), rot=(rnd.uniform(-15, 15), rnd.uniform(-20, 20), rnd.uniform(0, 90)),
              bev=0.04, parent=P)
    for k, (x, y, z, r) in enumerate(((cx + 0.15, cy, top_a(0.5), 0.26), (cx + 1.0, cy, top_a(1.1), 0.22), (cx, cy - 0.8, top_b(1.0), 0.2))):
        moss(P, f"moss{k}", (x, y, 0.18 + ch * int((z + 0.01 - 0.18) / ch) - 0.03), r, flat=0.3)
    moss(P, "foot", (cx + 0.6, cy - 0.7, 0.0), 0.35, flat=0.4)
    return P


def stone_wall_window(seed=46):
    """A 3.6 m stretch of ruined wall, about 3 m high, with a window opening: a projecting sill, a carved lintel, ragged top."""
    rnd = random.Random(seed)
    P = _root("wallwindow")
    s, sd = stone(), dstone()
    th, ch = 0.62, 0.48
    wx0, wx1, wz0, wz1 = -0.5, 0.45, 1.14, 2.1
    K.box("plinth", (3.8, th + 0.16, 0.22), s, loc=(0, 0, 0.07), bev=0.05, parent=P)

    def top(x):
        return 3.15 - 0.3 * abs(x + 0.9) - (0.5 if x > 1.2 else 0)

    def course_top(x):
        return 0.18 + ch * min(6, int((top(x) + 0.01 - 0.18) / ch))

    courses(P, "c", -1.8, 1.8, 0.18, 6, ch, th, top, rnd, s, sd, holes=((wx0, wx1, wz0, wz1), (wx0 - 0.4, wx1 + 0.4, wz1, wz1 + ch)))
    K.box("sill", (wx1 - wx0 + 0.4, th + 0.12, 0.12), s, loc=((wx0 + wx1) / 2, -0.06, wz0 + 0.05), bev=0.03, parent=P)
    K.box("lintel", (wx1 - wx0 + 0.77, th + 0.02, ch - 0.03), s, loc=((wx0 + wx1) / 2, 0, wz1 + ch / 2), rot=(0, 0.6, 0), bev=0.05, parent=P)
    F = face(P, "lintel_face", ((wx0 + wx1) / 2, -th / 2 - 0.01, wz1 + ch / 2))
    glyph_row(F, "lg", -0.45, 0.45, 0, 0.16, sd, rnd, n=4)
    K.box("band", (3.5, 0.02, 0.05), sd, loc=(0, -th / 2 - 0.006, 0.86), bev=0, parent=P)
    K.box("crack", (0.035, 0.02, 0.8), sd, loc=(1.0, -th / 2 - 0.006, 1.6), rot=(0, 14, 0), bev=0, parent=P)
    for k in range(5):
        sz = (rnd.uniform(0.3, 0.8), rnd.uniform(0.3, 0.5), rnd.uniform(0.2, 0.4))
        K.box(f"rubble{k}", sz, s, loc=(rnd.uniform(0.6, 2.2), rnd.uniform(-1.4, -0.6), sz[2] * 0.4), rot=(rnd.uniform(-15, 15), rnd.uniform(-20, 20), rnd.uniform(0, 90)),
              bev=0.04, parent=P)
    for k, (x, r) in enumerate(((-1.3, 0.26), (-0.6, 0.2), (0.75, 0.22), (1.5, 0.2))):
        moss(P, f"moss{k}", (x, rnd.uniform(-0.1, 0.1), course_top(x) - 0.03), r, flat=0.3, stretch=1.6)
    moss(P, "moss_sill", (-0.02, -0.05, wz0 + 0.1), 0.14, flat=0.3, stretch=1.6)
    moss(P, "foot", (-1.3, -0.6, 0.0), 0.3, flat=0.4)
    return P


def stone_rubble_pile(seed=47):
    """A heap of broken dressed blocks about 2.6 m across and a metre high, some still carrying a band, on a bed of grit."""
    rnd = random.Random(seed)
    P = _root("rubble")
    s, sd = stone(), dstone()
    K.sphere("bed", 1.0, gravel(), loc=(0, 0, -0.05), scale=(1.35, 1.1, 0.5), parent=P)
    Rp, Hp = 1.35, 1.05
    placed = 0
    k = 0
    while placed < 20 and k < 80:
        k += 1
        r = Rp * math.sqrt(rnd.random())
        a = rnd.uniform(0, 2 * math.pi)
        x, y = math.cos(a) * r, math.sin(a) * r * 0.82
        hz = Hp * max(0.0, 1 - (r / Rp) ** 2)
        sz = (rnd.uniform(0.35, 0.75), rnd.uniform(0.28, 0.45), rnd.uniform(0.2, 0.34))
        rot = (rnd.uniform(-25, 25), rnd.uniform(-25, 25), rnd.uniform(0, 180))
        b = K.box(f"blk{placed}", sz, sd if rnd.random() < 0.18 else s, loc=(x, y, max(sz[2] * 0.35, hz - sz[2] * 0.3)), rot=rot, bev=0.04, parent=P)
        if placed % 4 == 1:
            K.box(f"band{placed}", (sz[0] + 0.005, sz[1] + 0.012, 0.05), sd, loc=(0, 0, sz[2] * 0.15), bev=0, parent=b)
        placed += 1
    K.cyl("drum", 0.32, 0.5, s, loc=(0.9, -0.85, 0.3), rot=(90, 0, 25), segs=14, bev=0.03, parent=P)
    K.torus("drum_ring", 0.31, 0.02, sd, loc=(0.9, -0.85, 0.3), rot=(90, 0, 25), segs=18, parent=P)
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        rock(P, f"chip{k}", rnd.uniform(0.08, 0.14), 0.08, (math.cos(a) * 1.6, math.sin(a) * 1.25, 0.03), rnd)
    moss(P, "moss0", (-0.3, 0.2, 0.95), 0.2, flat=0.3)
    moss(P, "moss1", (-1.2, -0.5, 0.1), 0.25, flat=0.4)
    return P


def broken_statue_body(seed=48):
    """A robed statue about 1.4 times life on a carved plinth, 3.6 m high: the head gone, one arm broken at the shoulder,
    the other at the elbow with its forearm in the grass."""
    rnd = random.Random(seed)
    P = _root("statuebody")
    s, sd = stone(), dstone()
    K.box("step", (1.7, 1.7, 0.22), s, loc=(0, 0, 0.09), bev=0.05, parent=P)
    K.box("die", (1.25, 1.25, 0.86), s, loc=(0, 0, 0.62), bev=0.05, parent=P)
    K.box("die_band", (1.27, 1.27, 0.06), sd, loc=(0, 0, 0.9), bev=0, parent=P)
    K.box("cap", (1.42, 1.42, 0.14), s, loc=(0, 0, 1.11), bev=0.04, parent=P)
    F = face(P, "die_face", (0, -0.625, 0.56))
    glyph(F, "g0", "ring", 0, 0, 0.3, sd)
    glyph(F, "g1", "fork", -0.36, 0, 0.2, sd)
    glyph(F, "g2", "fork", 0.36, 0, 0.2, sd)
    z = 1.18
    S = empty(P, "statue", (0, 0.05, z), (0, 0, rnd.uniform(-4, 4)))
    tapered("robe", (0.98, 0.68), (0.78, 0.5), 1.25, s, bev=0.06, parent=S)
    tapered("hem", (1.02, 0.72), (1.0, 0.7), 0.12, s, bev=0.03, parent=S)
    for k, x in enumerate((-0.3, -0.12, 0.08, 0.26)):
        K.box(f"fold{k}", (0.035, 0.03, 0.95), sd, loc=(x, -0.3 + 0.08 * 0.95 / 1.25 * 0.5, 0.6), rot=(-4.6, 0, (x * 6)), bev=0, parent=S)
    K.box("belt", (0.82, 0.54, 0.09), sd, loc=(0, 0, 1.24), bev=0.02, parent=S)
    tapered("torso", (0.78, 0.48), (0.98, 0.54), 0.8, s, loc=(0, 0, 1.25), bev=0.06, parent=S)
    K.box("shoulders", (1.12, 0.56, 0.22), s, loc=(0, 0, 2.12), bev=0.08, parent=S)
    K.box("collar", (0.5, 0.04, 0.04), sd, loc=(0, -0.285, 2.0), bev=0, parent=S)
    Fc = face(S, "chest", (0, -0.26, 1.72), (-4, 0, 0))
    glyph(Fc, "pendant", "eye", 0, 0, 0.18, sd)
    # the neck snapped off: a stump with a slanted break, the crack running down the chest
    K.cyl("neck", 0.17, 0.2, s, loc=(0, 0.02, 2.3), rot=(9, -4, 0), segs=12, bev=0.02, parent=S)
    K.box("crack", (0.03, 0.02, 0.6), sd, loc=(0.18, -0.29, 1.55), rot=(0, 18, 0), bev=0, parent=S)
    # left arm hangs, broken at the elbow; right arm gone at the shoulder
    limb(S, "upper_l", (-0.6, 0.0, 2.1), 180, -82, 0.66, 0.13, 0.11, s, segs=10, bev=0.02)
    K.cyl("elbow_l", 0.11, 0.02, sd, loc=(-0.69, 0.0, 1.45), rot=(0, 8, 0), segs=10, bev=0, parent=S)
    K.cyl("stump_r", 0.13, 0.12, s, loc=(0.6, 0.0, 2.04), rot=(0, 75, 0), segs=10, bev=0.02, parent=S)
    K.cyl("stump_r_end", 0.125, 0.02, sd, loc=(0.66, 0.0, 2.02), rot=(0, 75, 0), segs=10, bev=0, parent=S)
    # the forearm and hand in the grass
    A = empty(P, "forearm", (-1.25, -0.75, 0.1), (0, 90, 30))
    K.cyl("forearm", 0.1, 0.55, s, segs=10, r2=0.085, bev=0.02, parent=A)
    K.box("hand", (0.2, 0.12, 0.22), s, loc=(0, 0, 0.36), bev=0.04, parent=A)
    for k in range(3):
        K.box(f"finger{k}", (0.05, 0.05, 0.12), s, loc=(-0.06 + 0.06 * k, 0.03, 0.52), bev=0.015, parent=A)
    rock(P, "chip", 0.12, 0.08, (0.9, -0.95, 0.03), rnd)
    moss(P, "moss0", (0.35, 0.0, 2.28), 0.14, flat=0.3)
    moss(P, "moss1", (-0.55, 0.5, 1.2), 0.16, flat=0.3)
    moss(P, "moss2", (0.8, 0.8, 0.0), 0.28, flat=0.4)
    return P


def stone_steps_wide(seed=49):
    """Wide shallow ceremonial steps: five 16 cm risers 6 m wide up to a paved landing, framed by carved cheek blocks."""
    rnd = random.Random(seed)
    P = _root("widesteps")
    s, sd = stone(), dstone()
    W, D, R = 6.0, 0.6, 0.16
    y0 = -2.1
    for k in range(5):
        h = (k + 1) * R
        x = -W / 2
        j = 0
        while x < W / 2 - 0.1:
            L = min(rnd.uniform(1.0, 1.7), W / 2 - x)
            sunk = (k == 1 and j == 2)
            K.box(f"step{k}_{j}", (L - 0.025, D, h - (0.05 if sunk else 0)), sd if rnd.random() < 0.1 else s,
                  loc=(x + L / 2, y0 + D / 2 + k * D, h / 2 - (0.025 if sunk else 0)), rot=((2, -1.5, 1) if sunk else (0, 0, rnd.uniform(-0.4, 0.4))),
                  bev=0.04, parent=P)
            x += L
            j += 1
    yl = y0 + 5 * D
    for i in range(4):
        for j in range(2):
            if (i, j) == (3, 1):
                continue
            K.box(f"land{i}_{j}", (1.48, 0.58, 0.8), s, loc=(-W / 2 + 0.75 + i * 1.5, yl + 0.3 + j * 0.6, 0.4), rot=(0, 0, rnd.uniform(-0.5, 0.5)),
                  bev=0.04, parent=P)
    K.box("land_core", (1.4, 0.55, 0.7), gravel(), loc=(W / 2 - 0.75, yl + 0.9, 0.35), bev=0.02, parent=P)
    for sx in (-1, 1):
        K.box(f"cheek{sx}", (0.5, 4.2, 0.46), s, loc=(sx * (W / 2 + 0.25), 0, 0.23), bev=0.05, parent=P)
        K.box(f"cheek_up{sx}", (0.5, 1.2, 0.46), s, loc=(sx * (W / 2 + 0.25), 1.5, 0.68), bev=0.05, parent=P)
        K.box(f"cheek_band{sx}", (0.012, 4.1, 0.05), sd, loc=(sx * (W / 2 + 0.5), 0, 0.3), bev=0, parent=P)
        K.box(f"post{sx}", (0.62, 0.62, 0.9), s, loc=(sx * (W / 2 + 0.25), -1.8, 0.45), bev=0.05, parent=P)
        K.box(f"post_cap{sx}", (0.7, 0.7, 0.1), s, loc=(sx * (W / 2 + 0.25), -1.8, 0.94), rot=(0, 0, sx * 4), bev=0.03, parent=P)
        F = face(P, f"post_face{sx}", (sx * (W / 2 + 0.25), -2.11, 0.55))
        glyph(F, f"pg{sx}", "ring", 0, 0, 0.26, sd)
    K.box("post_cap_fallen", (0.7, 0.7, 0.1), s, loc=(3.5, -2.55, 0.05), rot=(6, 0, 30), bev=0.03, parent=P)
    for k, (x, y, z, r) in enumerate(((-2.1, -1.6, 0.16, 0.2), (0.8, -1.0, 0.32, 0.16), (1.9, 0.2, 0.64, 0.22), (-0.9, 1.4, 0.8, 0.3),
                                      (2.25, 1.45, 0.72, 0.4), (-3.25, 0.8, 0.46, 0.2))):
        moss(P, f"moss{k}", (x, y, z), r, flat=0.25, stretch=1.8)
    rock(P, "chip", 0.12, 0.09, (-1.0, -2.5, 0.04), rnd)
    return P


def stone_basin(seed=50):
    """A big round stone basin 2.4 m across on a short turned pedestal and a square footing, rainwater in the bowl."""
    rnd = random.Random(seed)
    P = _root("basin")
    s, sd = stone(), dstone()
    K.box("footing", (1.5, 1.5, 0.18), s, loc=(0, 0, 0.07), bev=0.04, parent=P)
    prof = [(0.55, 0.14), (0.55, 0.22), (0.4, 0.3), (0.36, 0.5), (0.5, 0.58), (0.95, 0.72), (1.18, 0.86), (1.2, 1.02), (1.08, 1.06), (1.0, 0.96),
            (0.78, 0.74), (0.3, 0.66)]
    lathe("bowl", prof, s, segs=28, parent=P)
    K.torus("band", 1.19, 0.03, sd, loc=(0, 0, 0.93), segs=32, parent=P)
    K.torus("foot_band", 0.37, 0.022, sd, loc=(0, 0, 0.42), segs=16, parent=P)
    K.disc("water", 0.92, K.mat("stone_water", WATER, rough=0.05), (0, 0), z=0.9, segs=28).parent = P
    K.box("crack", (0.035, 0.02, 0.25), sd, loc=(0.42, -1.12, 0.9), rot=(0, 12, -20), bev=0, parent=P)
    # a chunk of the rim broke off and lies against the pedestal
    K.box("rimchunk", (0.4, 0.16, 0.18), s, loc=(-0.95, -0.85, 0.08), rot=(5, 10, 40), bev=0.03, parent=P)
    rock(P, "chip", 0.1, 0.07, (1.15, 0.9, 0.03), rnd)
    moss(P, "moss0", (-0.8, 0.75, 1.02), 0.14, flat=0.3)
    moss(P, "moss1", (0.6, 0.7, 0.0), 0.28, flat=0.4)
    return P


def stone_milestone(seed=51):
    """A carved roadside milestone 1.1 m high with a rounded head: a ring glyph, a tally of bars and an arrow chevron."""
    rnd = random.Random(seed)
    P = _root("milestone")
    s, sd = stone(), dstone()
    K.box("base", (0.66, 0.5, 0.14), s, loc=(0, 0, 0.05), rot=(0, 0, 3), bev=0.03, parent=P)
    G = empty(P, "lean", (0, 0.02, 0.1), (4, -3, rnd.uniform(-4, 4)))
    tapered("shaft", (0.48, 0.3), (0.44, 0.28), 0.78, s, bev=0.03, parent=G)
    K.cyl("head", 0.22, 0.28, s, loc=(0, 0, 0.78), rot=(90, 0, 0), segs=16, bev=0.03, parent=G)
    K.box("band", (0.46, 0.3, 0.05), sd, loc=(0, 0, 0.62), bev=0, parent=G)
    F = face(G, "face", (0, -0.145, 0.0), (-1.5, 0, 0))
    glyph(F, "ring", "ring", 0, 0.82, 0.18, sd)
    for k in range(3):
        K.box(f"tally{k}", (0.025, 0.02, 0.18), sd, loc=(-0.1 + 0.07 * k, -0.006, 0.42), bev=0, parent=F)
    K.box("tally_cross", (0.025, 0.02, 0.26), sd, loc=(-0.03, -0.006, 0.42), rot=(0, 60, 0), bev=0, parent=F)
    glyph(F, "arrow", "chevron", 0, 0.18, 0.16, sd)
    moss(P, "moss0", (0.02, 0.06, 1.0), 0.09, flat=0.3)
    moss(P, "moss1", (-0.3, 0.2, 0.0), 0.18, flat=0.4)
    rock(P, "pack", 0.08, 0.07, (0.3, -0.25, 0.03), rnd)
    return P


def stone_well_cover(seed=52):
    """A low round well of stone blocks 1.9 m across, capped by a carved round slab that has cracked in two; one half has
    shifted and shows the dark below."""
    rnd = random.Random(seed)
    P = _root("wellcover")
    s, sd = stone(), dstone()
    n, Rm = 11, 0.8
    for i in range(n):
        a = 2 * math.pi * i / n
        K.box(f"blk{i}", (0.44, 0.32, 0.5), sd if rnd.random() < 0.15 else s, loc=(Rm * math.cos(a), Rm * math.sin(a), 0.25),
              rot=(0, 0, math.degrees(a) + 90 + rnd.uniform(-2, 2)), bev=0.04, parent=P)
    K.disc("dark", 0.68, dark(), (0, 0), z=0.42, segs=20).parent = P
    Rl, hl = 0.98, 0.16
    semi = [(Rl * math.cos(math.pi * i / 10), Rl * math.sin(math.pi * i / 10)) for i in range(11)]
    for k, (loc, rot) in enumerate((((0, 0.02, 0.5), (0, 0, 0)), ((0.1, -0.3, 0.5), (0, 2.0, 192)))):
        H = empty(P, f"half{k}", loc, rot)
        prism(f"lid{k}", semi, hl, s, parent=H, bev=0.03)
        K.torus(f"ring{k}", 0.55, 0.025, sd, loc=(0, 0, hl), arc=180, segs=24, parent=H)
        K.box(f"spoke{k}", (0.03, 0.4, 0.04), sd, loc=(0, 0.75, hl), bev=0, parent=H)
        K.box(f"bar{k}", (0.4, 0.03, 0.04), sd, loc=(0, 0.2, hl), bev=0, parent=H)
        K.cyl(f"liftHole{k}", 0.05, 0.02, dark(), loc=(0.55, 0.38, hl), segs=10, bev=0, parent=H)
        K.box(f"edge_band{k}", (0.03, 0.03, 0.04), sd, loc=(-0.94, 0.12, hl * 0.5), bev=0, parent=H)
    rock(P, "chip", 0.1, 0.07, (1.15, -0.55, 0.03), rnd)
    moss(P, "moss0", (-0.4, 0.4, 0.68), 0.16, flat=0.25)
    moss(P, "moss1", (-0.95, -0.7, 0.0), 0.25, flat=0.4)
    return P


def stone_slab_fallen(seed=53):
    """A huge carved slab, 3.6 m long, fallen at an angle: one end in the grass, the other resting on a block a metre high."""
    rnd = random.Random(seed)
    P = _root("slab")
    s, sd = stone(), dstone()
    x0, xb, hb = -1.75, 0.35, 0.95
    K.box("block", (1.0, 1.1, hb), s, loc=(xb + 0.5, 0.05, hb / 2), rot=(0, 0, 4), bev=0.05, parent=P)
    K.box("block_band", (1.02, 1.12, 0.05), sd, loc=(xb + 0.5, 0.05, hb - 0.18), rot=(0, 0, 4), bev=0, parent=P)
    th = math.degrees(math.atan2(hb, xb - x0))
    L, W, T = 3.6, 1.8, 0.38
    G = empty(P, "slab", (x0, 0.0, -0.04), (0, -th, 2))
    K.box("slab", (L, W, T), s, loc=(L / 2, 0, T / 2), bev=0.06, parent=G)
    for sy in (-1, 1):
        K.box(f"edge{sy}", (L - 0.2, 0.02, 0.06), sd, loc=(L / 2, sy * (W / 2 + 0.005), T * 0.6), bev=0, parent=G)
    F = face(G, "top", (L / 2, 0, T + 0.004), (-90, 0, 0))
    K.box("frame_a", (2.8, 0.02, 0.04), sd, loc=(0, -0.006, 0.6), bev=0, parent=F)
    K.box("frame_b", (2.8, 0.02, 0.04), sd, loc=(0, -0.006, -0.6), bev=0, parent=F)
    glyph_row(F, "ga", -1.1, 1.1, 0.25, 0.3, sd, rnd, n=4)
    glyph_row(F, "gb", -1.1, 1.1, -0.25, 0.3, sd, rnd, n=4)
    K.box("crack", (0.04, 0.02, 1.2), sd, loc=(0.6, -0.006, 0.1), rot=(0, 70, 0), bev=0, parent=F)
    K.box("wedge", (0.4, 0.3, 0.2), s, loc=(x0 + 0.1, -1.1, 0.08), rot=(10, 0, 25), bev=0.03, parent=P)
    rock(P, "chip", 0.14, 0.1, (1.6, 0.9, 0.04), rnd)
    moss(P, "moss0", (-0.6, -0.4, 0.95), 0.25, flat=0.25)
    moss(P, "moss1", (-1.7, 0.75, 0.0), 0.3, flat=0.4)
    return P


def stone_pillar_broken_short(seed=54):
    """The stump of a square pillar 1.6 m high on its base, the top block snapped at a slant, a broken piece beside it."""
    rnd = random.Random(seed)
    P = _root("pillarstump")
    s, sd = stone(), dstone()
    K.box("base", (1.6, 1.6, 0.3), s, loc=(0, 0, 0.13), bev=0.06, parent=P)
    K.box("base_band", (1.45, 1.45, 0.12), sd, loc=(0, 0, 0.32), bev=0.03, parent=P)
    K.box("drum0", (1.2, 1.2, 0.72), s, loc=(0.02, 0, 0.74), rot=(0, 0, 3), bev=0.08, parent=P)
    tapered("drum1", (1.2, 1.2), (1.2, 1.2), 0.55, s, loc=(0.0, 0.02, 1.11), rot=(0, 0, -3), bev=0.06, parent=P, slope=0.28)
    K.box("band", (1.22, 1.22, 0.06), sd, loc=(0.0, 0.02, 1.25), rot=(0, 0, -3), bev=0, parent=P)
    F = face(P, "face", (0.02, -0.605, 0.72))
    glyph(F, "g0", "ring", 0, 0.05, 0.3, sd)
    rock(P, "shard", 0.32, 0.22, (-0.35, 0.1, 1.68), rnd, rot=(4, -14, 30))
    K.box("piece", (1.1, 1.1, 0.45), s, loc=(1.75, 0.55, 0.24), rot=(12, 18, 32), bev=0.07, parent=P)
    rock(P, "chip0", 0.14, 0.1, (-1.1, -0.9, 0.04), rnd)
    moss(P, "moss0", (0.35, 0.3, 1.38), 0.22, flat=0.3)
    moss(P, "moss1", (-0.9, 0.7, 0.05), 0.3, flat=0.4)
    return P


def stone_ring_gate(seed=55):
    """A round stone ring gate about 4 m high: twelve voussoirs set on edge in a carved cradle on a long base, banded and
    glyphed; probe crystal grows at its foot."""
    rnd = random.Random(seed)
    P = _root("ringgate")
    s, sd = stone(), dstone()
    D = 0.62
    K.box("step", (3.6, 1.6, 0.18), s, loc=(0, 0, 0.07), bev=0.04, parent=P)
    K.box("base", (3.0, 1.1, 0.36), s, loc=(0, 0, 0.32), bev=0.05, parent=P)
    K.box("base_band", (3.02, 1.12, 0.05), sd, loc=(0, 0, 0.36), bev=0, parent=P)
    Ri, Ro = 1.22, 1.7
    zc = 0.5 + Ro - 0.05
    for sx in (-1, 1):
        # the cradle blocks hug the ring's lower curve
        pts = [(sx * 0.35, 0.5), (sx * 1.35, 0.5), (sx * 1.35, 0.95)]
        pts += [(sx * Ro * math.cos(math.radians(a)), zc - Ro * math.sin(math.radians(a))) for a in (35, 50, 65, 78)]
        if sx > 0:
            pts = list(reversed(pts))
        prism(f"cradle{sx}", pts, D + 0.16, s, loc=(0, (D + 0.16) / 2, 0), rot=(90, 0, 0), parent=P, bev=0.03)
    n = 12
    for k in range(n):
        a0, a1 = 360 * k / n + 0.7 - 90, 360 * (k + 1) / n - 0.7 - 90
        slip = 0.04 if k == 7 else 0
        arc_block(f"vous{k}", Ri, Ro, a0, a1, D, sd if k in (3, 9) else s, loc=(0, slip, zc - slip * 0.5), rot=(90, 0, 0), parent=P, n=3, bev=0.04)
        am = math.radians((a0 + a1) / 2)
        Rm = (Ri + Ro) / 2
        F = face(P, f"vf{k}", (Rm * math.cos(am), -D / 2 - 0.004, zc + Rm * math.sin(am)), (0, 90 - math.degrees(am), 0))
        glyph(F, f"g{k}", KINDS[k % len(KINDS)], 0, 0, 0.22, sd)
    for R in (Ri + 0.06, Ro - 0.06):
        K.torus(f"band{R:.2f}", R, 0.022, sd, loc=(0, -D / 2 - 0.004, zc), rot=(90, 0, 0), segs=36, parent=P)
    crystals(P, (1.65, -0.75, 0.0), rnd)
    rock(P, "chip", 0.12, 0.09, (-1.6, -0.9, 0.03), rnd)
    moss(P, "moss0", (0.2, 0.05, zc + Ro), 0.18, flat=0.3)
    moss(P, "moss1", (-1.1, 0.2, 0.52), 0.2, flat=0.3)
    moss(P, "moss2", (-1.5, 0.75, 0.0), 0.3, flat=0.4)
    return P


def ancient_glyph_pillar(seed=56):
    """A square pillar 3.6 m high: a moulded base, three drums with carved bands between rows of glyphs on every face,
    a cap knocked askew."""
    rnd = random.Random(seed)
    P = _root("glyphpillar")
    s, sd = stone(), dstone()
    K.box("base", (1.2, 1.2, 0.3), s, loc=(0, 0, 0.13), bev=0.05, parent=P)
    K.box("base2", (1.0, 1.0, 0.16), s, loc=(0, 0, 0.36), bev=0.04, parent=P)
    w = 0.8
    z = 0.44
    for k, h in enumerate((1.05, 1.0, 0.95)):
        K.box(f"drum{k}", (w - 0.02 * k, w - 0.02 * k, h - 0.02), s, loc=(rnd.uniform(-0.01, 0.01), 0, z + h / 2), rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.05, parent=P)
        K.box(f"band{k}", (w + 0.04 - 0.02 * k, w + 0.04 - 0.02 * k, 0.08), sd, loc=(0, 0, z + h - 0.12), bev=0.015, parent=P)
        hw = (w - 0.02 * k) / 2
        for f in range(4):
            yaw = 90 * f
            ya = math.radians(yaw)
            F = face(P, f"face{k}_{f}", (math.sin(ya) * hw, -math.cos(ya) * hw, z + h * 0.45), (0, 0, yaw))
            if k == 2:
                glyph_row(F, f"g{k}_{f}_", -0.22, 0.22, 0.12, 0.17, sd, rnd, n=2)
                glyph(F, f"gb{k}_{f}", rnd.choice(KINDS), 0, -0.2, 0.2, sd)
            elif k == 1:
                glyph_row(F, f"g{k}_{f}_", -0.22, 0.22, 0.15, 0.17, sd, rnd, n=3)
                glyph_row(F, f"h{k}_{f}_", -0.22, 0.22, -0.18, 0.17, sd, rnd, n=3)
            else:
                glyph_row(F, f"g{k}_{f}_", -0.22, 0.22, 0.12, 0.2, sd, rnd, n=3)
        z += h
    K.box("cap", (1.0, 1.0, 0.24), s, loc=(0.06, -0.04, z + 0.1), rot=(4, -5, 8), bev=0.05, parent=P)
    K.box("cap_chunk", (0.4, 0.35, 0.22), s, loc=(0.9, -0.8, 0.1), rot=(14, 20, 40), bev=0.04, parent=P)
    K.box("crack", (0.03, 0.02, 0.7), sd, loc=(0.25, -0.401, 1.1), rot=(0, 15, 0), bev=0, parent=P)
    moss(P, "moss0", (-0.2, 0.15, z + 0.22), 0.2, flat=0.3)
    moss(P, "moss1", (0.4, 0.45, 0.45), 0.13, flat=0.3)
    moss(P, "moss2", (-0.7, 0.6, 0.0), 0.28, flat=0.4)
    return P


def moss_boulder_carved(seed=57):
    """A natural boulder 2.5 m across and 1.5 m high whose front was dressed flat and carved with a framed grid of glyphs,
    moss thick on its back and crown."""
    rnd = random.Random(seed)
    P = _root("carvedboulder")
    s, sd = stone(), dstone()
    G = empty(P, "rock", (0, 0, 0.0), (0, 0, rnd.uniform(-6, 6)))
    boulder("boulder", (1.25, 0.95, 0.9), s, rnd, loc=(0, 0.05, 0.5), parent=G, cut_front=0.76, sub=3)
    F = face(G, "face", (0.0, 0.05 - 0.76, 0.0))
    zc = 0.56
    for k, (x, z, L, ang) in enumerate(((0, zc + 0.3, 0.9, 90), (0, zc - 0.3, 0.9, 90), (-0.44, zc, 0.62, 0), (0.44, zc, 0.62, 0))):
        K.box(f"frame{k}", (0.04, 0.02, L), sd, loc=(x, -0.006, z), rot=(0, ang, 0), bev=0, parent=F)
    glyph_row(F, "ga", -0.27, 0.27, zc + 0.13, 0.18, sd, rnd, n=3)
    glyph_row(F, "gb", -0.27, 0.27, zc - 0.14, 0.18, sd, rnd, n=3)
    K.box("crack", (0.035, 0.02, 0.5), sd, loc=(0.38, -0.006, zc - 0.05), rot=(0, 25, 0), bev=0, parent=F)
    mm = K.mat("moss_pad", "#6f9a3a", rough=1.0, noise_scale=10.0)
    K.sphere("moss_crown", 0.55, mm, loc=(-0.1, 0.2, 1.22), scale=(1.3, 1.0, 0.3), parent=G)
    K.sphere("moss_back", 0.4, mm, loc=(0.55, 0.5, 1.0), scale=(1.2, 0.9, 0.35), parent=G)
    moss(P, "moss_foot0", (-1.1, -0.45, 0.0), 0.3, flat=0.4)
    moss(P, "moss_foot1", (0.9, 0.7, 0.0), 0.35, flat=0.4)
    rock(P, "chip", 0.12, 0.1, (1.25, -0.75, 0.04), rnd)
    return P


def stone_head_small(seed=58):
    """A small statue head about a metre high, fallen in the grass: tipped onto the back of its skull and rolled to one
    side, the face to the sky, half buried."""
    rnd = random.Random(seed)
    P = _root("smallhead")
    s, sd = stone(), dstone()
    k = 0.4
    G = empty(P, "head", (0, 0.12, 0.3), (-38, 22, 30))
    hd = 1.95 * k
    fy = -hd / 2 + 0.04 * k
    tapered("skull", (1.6 * k, 1.75 * k), (2.0 * k, hd), 2.5 * k, s, loc=(0, 0.05 * k, -1.3 * k), bev=0.12, parent=G)
    K.box("crown", (1.7 * k, 1.65 * k, 0.3 * k), s, loc=(0, 0.1 * k, 1.22 * k), bev=0.05, parent=G)
    K.box("face", (1.4 * k, 0.1 * k, 1.9 * k), s, loc=(0, fy + 0.02 * k, -0.25 * k), bev=0.02, parent=G)
    K.box("band", (2.06 * k, 2.04 * k, 0.3 * k), s, loc=(0, 0.04 * k, 0.9 * k), bev=0.025, parent=G)
    K.box("band_line", (1.7 * k, 0.02, 0.05 * k), sd, loc=(0, -hd / 2 - 0.035 * k, 0.9 * k), bev=0, parent=G)
    tapered("brow", (1.5 * k, 0.24 * k), (1.45 * k, 0.12 * k), 0.2 * k, s, loc=(0, fy - 0.1 * k, 0.3 * k), bev=0.015, parent=G, shift=(0, 0.06 * k))
    bm = bmesh.new()
    top = bm.verts.new((0, fy - 0.08 * k, 0.32 * k))
    l, r = bm.verts.new((-0.22 * k, fy - 0.02 * k, -0.5 * k)), bm.verts.new((0.22 * k, fy - 0.02 * k, -0.5 * k))
    tip = bm.verts.new((0, fy - 0.36 * k, -0.46 * k))
    for f in ((top, l, tip), (top, tip, r), (l, r, tip), (top, r, l)):
        bm.faces.new(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    nose = K.from_bm("nose", bm, s)
    K.bevel(nose, 0.012, segs=2, angle=30)
    K.place(nose, parent=G)
    for sx in (-1, 1):
        K.box(f"lid{sx}", (0.46 * k, 0.12 * k, 0.14 * k), s, loc=(sx * 0.4 * k, fy - 0.06 * k, 0.12 * k), rot=(0, sx * -6, 0), bev=0.02, parent=G)
        K.box(f"lash{sx}", (0.42 * k, 0.02, 0.03 * k), sd, loc=(sx * 0.4 * k, fy - 0.12 * k, 0.05 * k), rot=(0, sx * -6, 0), bev=0, parent=G)
        K.box(f"ear{sx}", (0.14 * k, 0.4 * k, 0.75 * k), s, loc=(sx * 0.93 * k, -0.05 * k, -0.15 * k), bev=0.02, parent=G)
    K.box("lip_u", (0.6 * k, 0.12 * k, 0.11 * k), s, loc=(0, fy - 0.06 * k, -0.72 * k), bev=0.015, parent=G)
    K.box("mouth", (0.54 * k, 0.02, 0.03 * k), sd, loc=(0, fy - 0.12 * k, -0.79 * k), bev=0, parent=G)
    K.box("lip_l", (0.48 * k, 0.1 * k, 0.1 * k), s, loc=(0, fy - 0.05 * k, -0.86 * k), bev=0.015, parent=G)
    K.box("crack", (0.02, 0.02, 0.9 * k), sd, loc=(-0.45 * k, fy - 0.04 * k, 0.4 * k), rot=(0, -18, 0), bev=0, parent=G)
    K.sphere("heap", 0.6, earth(), loc=(0.0, 0.3, 0.0), scale=(1.3, 1.2, 0.28), parent=P)
    rock(P, "chip", 0.09, 0.07, (0.65, -0.5, 0.03), rnd)
    moss(P, "moss0", (-0.55, 0.45, 0.06), 0.22, flat=0.4)
    moss(P, "moss1", (0.45, 0.55, 0.1), 0.18, flat=0.4)
    return P


def stone_chest(seed=59):
    """A chest cut from stone, 1.4 m long, on four squat feet: a framed and glyphed front, its hipped lid slid half off."""
    rnd = random.Random(seed)
    P = _root("stonechest")
    s, sd = stone(), dstone()
    L, W, H = 1.4, 0.8, 0.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box(f"foot{sx}{sy}", (0.24, 0.24, 0.12), s, loc=(sx * (L / 2 - 0.12), sy * (W / 2 - 0.12), 0.05), bev=0.03, parent=P)
    zb = 0.1
    K.box("body", (L, W, H), s, loc=(0, 0, zb + H / 2), bev=0.04, parent=P)
    K.box("open", (L - 0.24, W - 0.24, 0.02), dark(), loc=(0, 0, zb + H - 0.005), bev=0, parent=P)
    K.box("rim_band", (L + 0.02, W + 0.02, 0.05), sd, loc=(0, 0, zb + H - 0.1), bev=0, parent=P)
    F = face(P, "front", (0, -W / 2, zb + H * 0.42))
    for k, (x, z, Ln, ang) in enumerate(((0, 0.17, 1.1, 90), (0, -0.17, 1.1, 90), (-0.54, 0, 0.36, 0), (0.54, 0, 0.36, 0))):
        K.box(f"frame{k}", (0.03, 0.02, Ln), sd, loc=(x, -0.006, z), rot=(0, ang, 0), bev=0, parent=F)
    glyph(F, "g0", "ring", 0, 0, 0.22, sd)
    glyph(F, "g1", "steps", -0.3, 0, 0.18, sd)
    glyph(F, "g2", "fork", 0.3, 0, 0.18, sd)
    lid = empty(P, "lid", (0.42, -0.05, zb + H), (0, 4.5, 9))
    K.box("lid", (L + 0.08, W + 0.08, 0.14), s, loc=(0, 0, 0.07), bev=0.03, parent=lid)
    tapered("lid_top", (L - 0.02, W - 0.02), (L - 0.4, W - 0.4), 0.14, s, loc=(0, 0, 0.14), bev=0.025, parent=lid)
    K.box("lid_band", (L + 0.09, W + 0.09, 0.03), sd, loc=(0, 0, 0.07), bev=0, parent=lid)
    rock(P, "chip", 0.08, 0.06, (-0.9, -0.55, 0.03), rnd)
    moss(P, "moss0", (0.6, 0.0, zb + H + 0.3), 0.12, flat=0.3)
    moss(P, "moss1", (-0.8, 0.45, 0.0), 0.22, flat=0.4)
    return P


def stone_platform_ruin(seed=60):
    """A raised square platform 5 m across and 0.8 m high: kerb blocks round a paved top, one corner broken away to grit,
    a step up at the front."""
    rnd = random.Random(seed)
    P = _root("platform")
    s, sd = stone(), dstone()
    S, H, kw = 5.0, 0.8, 0.5
    hs = S / 2
    corner = (hs, hs)

    def near(x, y, d):
        return math.hypot(x - corner[0], y - corner[1]) < d

    K.box("core", (S - 2 * kw, S - 2 * kw, H - 0.14), gravel(), loc=(0, 0, (H - 0.14) / 2), bev=0.02, parent=P)
    for side in range(4):
        yaw = 90 * side
        ya = math.radians(yaw)
        x = -hs
        j = 0
        while x < hs - kw - 0.05:
            Lb = min(rnd.uniform(0.9, 1.25), hs - kw - x)
            t = x + Lb / 2
            # local (t, -hs+kw/2) rotated by yaw
            px, py = t * math.cos(ya) - (-hs + kw / 2) * math.sin(ya), t * math.sin(ya) + (-hs + kw / 2) * math.cos(ya)
            if near(px, py, 1.4):
                if rnd.random() < 0.6:
                    K.box(f"kerb{side}_{j}", (Lb - 0.03, kw, H * 0.6), s, loc=(px * 1.08, py * 1.08, H * 0.25), rot=(rnd.uniform(-12, 12), rnd.uniform(-12, 12), yaw + rnd.uniform(-20, 20)),
                          bev=0.05, parent=P)
            else:
                K.box(f"kerb{side}_{j}", (Lb - 0.03, kw, H), sd if rnd.random() < 0.12 else s, loc=(px, py, H / 2), rot=(0, 0, yaw + rnd.uniform(-0.8, 0.8)),
                      bev=0.05, parent=P)
            x += Lb
            j += 1
    n = 4
    ps = (S - 2 * kw) / n
    for i in range(n):
        for j in range(n):
            x = -hs + kw + ps * (i + 0.5)
            y = -hs + kw + ps * (j + 0.5)
            if near(x, y, 1.6) or (i, j) == (1, 2):
                continue
            K.box(f"pave{i}_{j}", (ps - 0.03, ps - 0.03, 0.14), s, loc=(x, y, H - 0.07 + rnd.uniform(-0.01, 0.01)),
                  rot=(rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), rnd.uniform(-1, 1)), bev=0.03, parent=P)
    K.box("band_front", (S - 0.1, 0.02, 0.06), sd, loc=(0, -hs - 0.006, H - 0.2), bev=0, parent=P)
    K.box("band_left", (0.02, S - 0.1, 0.06), sd, loc=(-hs - 0.006, 0, H - 0.2), bev=0, parent=P)
    K.box("step", (1.8, 0.5, 0.4), s, loc=(-0.4, -hs - 0.25, 0.2), bev=0.04, parent=P)
    F = face(P, "step_face", (-0.4, -hs - 0.5, 0.2))
    glyph_row(F, "sg", -0.6, 0.6, 0, 0.16, sd, rnd, n=4)
    for k in range(5):
        a = rnd.uniform(0, math.pi / 2)
        rock(P, f"chip{k}", rnd.uniform(0.1, 0.18), 0.1, (hs + 0.3 + math.cos(a) * 0.5, hs + 0.3 + math.sin(a) * 0.5, 0.04), rnd)
    for k, (x, y, z, r) in enumerate(((-1.5, -1.2, H, 0.35), (0.4, 0.9, H, 0.3), (-2.25, 1.5, H, 0.22), (1.2, -2.25, H, 0.2), (-1.6, -2.7, 0.0, 0.3))):
        moss(P, f"moss{k}", (x, y, z), r, flat=0.25, stretch=1.6)
    return P


def stone_bollard(seed=61):
    """A short carved bollard 0.85 m high: an octagonal shaft on a square footing, a carved band and a capped head."""
    rnd = random.Random(seed)
    P = _root("bollard")
    s, sd = stone(), dstone()
    K.box("footing", (0.48, 0.48, 0.14), s, loc=(0, 0, 0.05), bev=0.03, parent=P)
    G = empty(P, "lean", (0, 0, 0.1), (3, 2, rnd.uniform(0, 45)))
    K.cyl("shaft", 0.18, 0.56, s, loc=(0, 0, 0.28), segs=8, r2=0.16, bev=0.02, parent=G)
    K.cyl("band", 0.175, 0.05, sd, loc=(0, 0, 0.47), segs=8, bev=0, parent=G)
    K.cyl("collar", 0.2, 0.08, s, loc=(0, 0, 0.58), segs=8, bev=0.02, parent=G)
    K.cyl("head", 0.18, 0.13, s, loc=(0, 0, 0.68), segs=8, r2=0.07, bev=0.02, parent=G)
    F = face(G, "face", (0, -0.163, 0.24), (-2, 0, 22.5))
    glyph(F, "g", "ring", 0, 0, 0.13, sd)
    moss(P, "moss0", (0.0, 0.03, 0.82), 0.07, flat=0.35)
    moss(P, "moss1", (0.22, 0.2, 0.0), 0.14, flat=0.4)
    return P


def stone_marker_ring(seed=62):
    """A flat ring of dressed stones 3.4 m across set into the ground round a glyphed centre stone; one stone is missing
    and two have heaved. Walked over, not into."""
    rnd = random.Random(seed)
    P = _root("markerring")
    s, sd = stone(), dstone()
    n = 12
    for k in range(n):
        if k == 8:
            continue
        a0, a1 = 360 * k / n + 1.5, 360 * (k + 1) / n - 1.5
        heave = k in (2, 3)
        arc_block(f"ring{k}", 1.3, 1.72, a0, a1, 0.16, sd if k % 4 == 0 else s, loc=(0, 0, 0.03 + (0.04 if heave else 0)),
                  rot=((rnd.uniform(-5, 5), rnd.uniform(-5, 5), rnd.uniform(-2, 2)) if heave else (0, 0, 0)), parent=P, n=3, bev=0.03)
    K.box("lost", (0.4, 0.3, 0.12), s, loc=(-1.0, -2.1, 0.03), rot=(4, 6, 35), bev=0.03, parent=P)
    K.cyl("centre", 0.42, 0.12, s, loc=(0, 0, 0.03), segs=16, bev=0.03, parent=P)
    F = face(P, "top", (0, 0, 0.09 + 0.002), (-90, 0, 0))
    glyph(F, "g0", "eye", 0, 0, 0.5, sd)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        F2 = face(P, f"rt{k}", (1.51 * math.cos(a), 1.51 * math.sin(a), 0.11 + 0.002 + (0.04 if k == 0 else 0)), (-90, 0, math.degrees(a) - 90))
        glyph(F2, f"rg{k}", ("bars", "fork", "stem", "chevron")[k], 0, 0, 0.22, sd)
    moss(P, "moss0", (1.2, 0.95, 0.06), 0.2, flat=0.3)
    moss(P, "moss1", (-1.55, 0.3, 0.06), 0.16, flat=0.3)
    return P


def stone_lintel_fallen(seed=63):
    """A long carved lintel, 3.4 m in all, fallen and broken in two: the long piece face up in the grass, the short piece
    rolled onto its side beyond it, the break jagged, crumbs between."""
    rnd = random.Random(seed)
    P = _root("lintel")
    s, sd = stone(), dstone()
    Dp, Hp = 0.6, 0.55

    def piece(name, L, loc, rot, jag_right):
        G = empty(P, name, loc, rot)
        j = [(0.0, -Dp / 2), (0.08, -Dp / 4), (-0.05, 0.0), (0.07, Dp / 4), (-0.02, Dp / 2)]
        if jag_right:
            pts = [(-L / 2, -Dp / 2)] + [(L / 2 + dx, y) for dx, y in j] + [(-L / 2, Dp / 2)]
        else:
            pts = [(-L / 2 - dx, y) for dx, y in reversed(j)] + [(L / 2, -Dp / 2), (L / 2, Dp / 2)]
        prism(f"{name}_stone", pts, Hp, s, loc=(0, 0, -Hp / 2), parent=G, bev=0.035)
        F = face(G, f"{name}_face", (0, -Dp / 2, 0))
        x0, x1 = (-L / 2 + 0.1, L / 2 - 0.15) if jag_right else (-L / 2 + 0.15, L / 2 - 0.1)
        for k, z in enumerate((0.19, -0.19)):
            K.box(f"{name}_band{k}", (x1 - x0, 0.02, 0.045), sd, loc=((x0 + x1) / 2, -0.006, z), bev=0, parent=F)
        glyph_row(F, f"{name}_g", x0 + 0.15, x1 - 0.15, 0, 0.2, sd, rnd)
        return G

    # face up: the carved front (-y) rotated to the sky
    piece("long", 2.0, (-0.75, 0.0, Dp / 2 - 0.03), (-90, 0, 6), True)
    piece("short", 1.4, (1.15, 0.35, Hp / 2 - 0.02), (0, 0, -24), False)
    for k in range(4):
        rock(P, f"crumb{k}", rnd.uniform(0.06, 0.12), 0.07, (0.35 + rnd.uniform(-0.15, 0.15), rnd.uniform(-0.4, 0.4), 0.03), rnd)
    moss(P, "moss0", (-1.5, -0.1, 0.6), 0.18, flat=0.3)
    moss(P, "moss1", (1.4, 0.55, 0.55), 0.13, flat=0.3)
    moss(P, "moss2", (-0.2, 0.6, 0.0), 0.25, flat=0.4)
    return P


def stone_threshold(seed=64):
    """The doorstep of a vanished doorway: a worn sill of two slabs with a door stop and pivot holes, the broken bases of
    both jambs, a fallen jamb block and a little paving in front."""
    rnd = random.Random(seed)
    P = _root("threshold")
    s, sd = stone(), dstone()
    K.box("sill_l", (1.32, 0.9, 0.2), s, loc=(-0.66, 0, 0.08), rot=(0, 0, 0.5), bev=0.04, parent=P)
    K.box("sill_r", (1.25, 0.9, 0.2), s, loc=(0.66, 0.01, 0.075), rot=(0, -0.8, -0.8), bev=0.04, parent=P)
    K.box("stop", (1.6, 0.08, 0.06), s, loc=(0, 0.2, 0.2), bev=0.015, parent=P)
    K.box("wear", (0.9, 0.35, 0.01), sd, loc=(0, -0.15, 0.181), bev=0, parent=P)
    for sx in (-1, 1):
        K.cyl(f"pivot{sx}", 0.05, 0.01, dark(), loc=(sx * 0.85, 0.2, 0.181), segs=10, bev=0, parent=P)
    # jamb bases: the left keeps one and a half blocks, the right only its first, the next block tumbled out in front
    K.box("jamb_l0", (0.6, 0.72, 0.55), s, loc=(-1.6, 0, 0.27), rot=(0, 0, 2), bev=0.05, parent=P)
    tapered("jamb_l1", (0.6, 0.72), (0.6, 0.72), 0.35, s, loc=(-1.6, 0.0, 0.54), rot=(0, 0, -1), bev=0.04, parent=P, slope=0.5)
    K.box("jamb_r0", (0.6, 0.72, 0.5), sd, loc=(1.6, 0, 0.25), rot=(0, 0, -2), bev=0.05, parent=P)
    K.box("jamb_band", (0.62, 0.74, 0.05), sd, loc=(-1.6, 0, 0.4), rot=(0, 0, 2), bev=0, parent=P)
    K.box("jamb_fallen", (0.6, 0.72, 0.58), s, loc=(1.9, -1.0, 0.3), rot=(85, 0, 25), bev=0.05, parent=P)
    for k, (x, y, w, d) in enumerate(((-0.6, -0.95, 1.0, 0.8), (0.5, -1.0, 0.9, 0.7), (-0.1, -1.85, 1.1, 0.7))):
        K.box(f"pave{k}", (w, d, 0.1), s, loc=(x, y, 0.02), rot=(rnd.uniform(-1.5, 1.5), rnd.uniform(-1.5, 1.5), rnd.uniform(-5, 5)), bev=0.03, parent=P)
    rock(P, "chip", 0.12, 0.08, (1.2, -1.75, 0.03), rnd)
    moss(P, "moss0", (-1.75, 0.15, 0.8), 0.18, flat=0.3)
    moss(P, "moss1", (1.0, -1.45, 0.05), 0.25, flat=0.4)
    moss(P, "moss2", (-1.25, 0.6, 0.0), 0.25, flat=0.4)
    return P


def stone_gargoyle(seed=65):
    """A crouching gargoyle about a metre high on a carved block: haunches, forelegs on the front edge, folded wings, a
    horned head with deep eyes and a snarl, its tail wrapped round its feet."""
    rnd = random.Random(seed)
    P = _root("gargoyle")
    s, sd = stone(), dstone()
    K.box("block", (1.0, 1.0, 0.86), s, loc=(0, 0, 0.43), bev=0.05, parent=P)
    K.box("block_band", (1.02, 1.02, 0.06), sd, loc=(0, 0, 0.7), bev=0, parent=P)
    K.box("block_cap", (1.12, 1.12, 0.12), s, loc=(0, 0, 0.91), bev=0.04, parent=P)
    F = face(P, "block_face", (0, -0.5, 0.38))
    glyph_row(F, "bg", -0.28, 0.28, 0, 0.2, sd, rnd, n=3)
    z0 = 0.97
    G = empty(P, "beast", (0, 0.05, z0), (0, 0, rnd.uniform(-5, 5)))
    for sx in (-1, 1):
        # haunches and hind feet
        K.sphere(f"haunch{sx}", 0.22, s, loc=(sx * 0.24, 0.12, 0.22), scale=(0.85, 1.25, 1.05), parent=G)
        K.box(f"hindfoot{sx}", (0.17, 0.3, 0.09), s, loc=(sx * 0.27, -0.05, 0.045), bev=0.03, parent=G)
        # forelegs from the shoulders down to the front edge, claws over it
        limb(G, f"foreleg{sx}", (sx * 0.17, -0.18, 0.62), 90 + sx * 4, -78, 0.6, 0.075, 0.06, s, segs=8, bev=0.015)
        K.box(f"forefoot{sx}", (0.16, 0.2, 0.08), s, loc=(sx * 0.15, -0.36, 0.04), bev=0.025, parent=G)
        for c in range(3):
            K.cyl(f"claw{sx}{c}", 0.022, 0.1, sd, loc=(sx * 0.15 + (c - 1) * 0.05, -0.47, 0.03), rot=(-70, 0, 0), segs=5, r2=0.005, bev=0, parent=G)
    B = empty(G, "body", (0, 0.08, 0.1), (24, 0, 0))
    tapered("body", (0.5, 0.42), (0.44, 0.32), 0.62, s, bev=0.07, parent=B)
    K.box("chest_band", (0.46, 0.36, 0.05), sd, loc=(0, 0, 0.5), bev=0.01, parent=B)
    # folded wings: flat triangles down the back, ribbed
    for sx in (-1, 1):
        W = empty(B, f"wing{sx}", (sx * 0.16, 0.2, 0.62), (8, sx * 14, sx * -12))
        prism(f"wing{sx}", [(0, 0), (sx * 0.3, 0.3), (sx * 0.1, -0.55)] if sx < 0 else [(0, 0), (sx * 0.1, -0.55), (sx * 0.3, 0.3)], 0.05, s,
              rot=(90, 0, 0), parent=W, bev=0.012)
        W.children[-1].location = (0, 0.05, 0)
        for r in range(2):
            K.box(f"rib{sx}{r}", (0.02, 0.02, 0.55), sd, loc=(sx * (0.09 + 0.07 * r), -0.006, -0.08), rot=(0, sx * (12 + 10 * r), 0), bev=0, parent=W)
    # the head: skull, brow, snout, horns, ears, eyes and mouth
    Hh = empty(G, "head", (0, -0.2, 0.74), (8, 0, 0))
    K.box("skull", (0.34, 0.32, 0.28), s, loc=(0, 0.02, 0.0), bev=0.07, parent=Hh)
    tapered("snout", (0.24, 0.16), (0.16, 0.12), 0.18, s, loc=(0, -0.13, -0.06), rot=(90, 0, 0), bev=0.03, parent=Hh)
    K.box("brow", (0.36, 0.08, 0.06), s, loc=(0, -0.14, 0.07), bev=0.02, parent=Hh)
    K.box("mouth", (0.18, 0.02, 0.025), dark(), loc=(0, -0.3, -0.1), bev=0, parent=Hh)
    K.box("jaw", (0.2, 0.15, 0.06), s, loc=(0, -0.2, -0.15), rot=(-8, 0, 0), bev=0.02, parent=Hh)
    for sx in (-1, 1):
        K.box(f"eye{sx}", (0.06, 0.02, 0.035), dark(), loc=(sx * 0.08, -0.157, 0.025), bev=0, parent=Hh)
        K.cyl(f"fang{sx}", 0.012, 0.05, sd, loc=(sx * 0.06, -0.29, -0.115), rot=(180, 0, 0), segs=5, r2=0.002, bev=0, parent=Hh)
        limb(Hh, f"horn{sx}", (sx * 0.12, 0.04, 0.12), 90 - sx * 35, 30, 0.26, 0.04, 0.008, sd, segs=6, bev=0)
        K.box(f"ear{sx}", (0.12, 0.03, 0.08), s, loc=(sx * 0.2, 0.02, 0.06), rot=(0, sx * 25, sx * -20), bev=0.01, parent=Hh)
    K.torus("tail", 0.32, 0.04, s, loc=(0.0, -0.05, 0.03), rot=(0, 0, -30), arc=200, segs=18, parent=G)
    moss(P, "moss0", (0.05, -0.15, z0 + 0.88), 0.07, flat=0.35)
    moss(P, "moss1", (-0.4, 0.4, 0.97), 0.13, flat=0.3)
    moss(P, "moss2", (0.6, 0.55, 0.0), 0.22, flat=0.4)
    return P


PROPS = {
    "rune_stone": rune_stone,
    "stone_bench_curved": stone_bench_curved,
    "stone_planter": stone_planter,
    "aqueduct_section": aqueduct_section,
    "stone_wall_corner": stone_wall_corner,
    "stone_wall_window": stone_wall_window,
    "stone_rubble_pile": stone_rubble_pile,
    "broken_statue_body": broken_statue_body,
    "stone_steps_wide": stone_steps_wide,
    "stone_basin": stone_basin,
    "stone_milestone": stone_milestone,
    "stone_well_cover": stone_well_cover,
    "stone_slab_fallen": stone_slab_fallen,
    "stone_pillar_broken_short": stone_pillar_broken_short,
    "stone_ring_gate": stone_ring_gate,
    "ancient_glyph_pillar": ancient_glyph_pillar,
    "moss_boulder_carved": moss_boulder_carved,
    "stone_head_small": stone_head_small,
    "stone_chest": stone_chest,
    "stone_platform_ruin": stone_platform_ruin,
    "stone_bollard": stone_bollard,
    "stone_marker_ring": stone_marker_ring,
    "stone_lintel_fallen": stone_lintel_fallen,
    "stone_threshold": stone_threshold,
    "stone_gargoyle": stone_gargoyle,
}
