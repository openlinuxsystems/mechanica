"""Props of the Old Stone ruins (docs/world-generation.md, Scenes), in the Old Stone family of docs/art-direction.md.

Whoever lived here before the colony built in dressed stone and carved it: a small arch, a standing fluted column, an
empty pedestal, a chipped urn, a cold brazier, an altar, a glyph tablet, a relief wall, a guardian beast, an obelisk, a
sealed tomb, a sarcophagus with its lid pushed askew, a broken fountain, the last span of a bridge, a pair of gate
pillars, the stump of a round tower, a spiral stair round a broken core, a patch of flagstones, a round dais, a ring of
standing stones, a carved face slab, a stone lantern and a giant stone hand. All are the rock material with dark carved
bands and cracks and moss on their top faces. Cyan appears only on the colony's probe crystals: an orb set on a
pedestal and a cluster growing out of a stone socket.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh

import kit as K
from families import M, glow, shell_panel, stone, STONE, GRAPHITE, RUST, CYAN
from props import _root, CHAR, CHAR_D
from props_stone import dstone, moss, tapered, rock, empty, earth


# ---------------------------------------------------------------- helpers
def lathe(name, prof, mat, segs=20, arc=360, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, sharp=40):
    """A turned piece (urn, bowl, baluster): the (r, z) profile, starting and ending on the axis, swept round Z.
    An arc under 360 degrees gives a broken piece with its cut faces closed."""
    bm = bmesh.new()
    closed = arc >= 360
    n = segs if closed else segs + 1
    rings = []
    for r, z in prof:
        if r < 1e-4:
            v = bm.verts.new((0, 0, z))
            rings.append([v] * n)
            continue
        ring = []
        for j in range(n):
            a = math.radians(arc) * j / segs
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        A, B = rings[i], rings[i + 1]
        for j in range(n if closed else n - 1):
            k = (j + 1) % n
            vs = []
            for v in (A[j], A[k], B[k], B[j]):
                if v not in vs:
                    vs.append(v)
            if len(vs) >= 3:
                bm.faces.new(vs)
    if not closed:
        for j in (0, n - 1):
            vs = []
            for ring in rings:
                if ring[j] not in vs:
                    vs.append(ring[j])
            bm.faces.new(vs)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(sharp))
    return K.place(ob, loc, rot, parent)


def arc_block(name, r0, r1, a0, a1, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, sub=3, bev=0.04):
    """A curved block of a round wall, rim or step: the ring sector between radii r0 and r1 and angles a0..a1
    (degrees), h tall from its base."""
    bm = bmesh.new()
    cols = []
    for i in range(sub + 1):
        a = math.radians(a0 + (a1 - a0) * i / sub)
        c, s = math.cos(a), math.sin(a)
        cols.append([bm.verts.new((r * c, r * s, z)) for r, z in ((r0, 0), (r1, 0), (r1, h), (r0, h))])
    for i in range(sub):
        A, B = cols[i], cols[i + 1]
        for q in range(4):
            p = (q + 1) % 4
            bm.faces.new((A[q], B[q], B[p], A[p]))
    bm.faces.new(cols[0])
    bm.faces.new(list(reversed(cols[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2, angle=40)
    return K.place(ob, loc, rot, parent)


def profile_slab(name, pts, t, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, bev=0.03):
    """A slab cut to an outline: the (x, z) points, counter-clockwise seen from the front, t thick along Y."""
    bm = bmesh.new()
    f = [bm.verts.new((x, -t / 2, z)) for x, z in pts]
    b = [bm.verts.new((x, t / 2, z)) for x, z in pts]
    bm.faces.new(f)
    bm.faces.new(list(reversed(b)))
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((f[j], f[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2, angle=40)
    return K.place(ob, loc, rot, parent)


def fluted(name, r0, r1, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, flutes=14):
    """A column drum with shallow flutes, r0 at its base tapering to r1 at its top."""
    bm = bmesh.new()
    n = flutes * 2
    ring = lambda r, z: [bm.verts.new((r * (1 if i % 2 == 0 else 0.92) * math.cos(2 * math.pi * i / n),
                                       r * (1 if i % 2 == 0 else 0.92) * math.sin(2 * math.pi * i / n), z)) for i in range(n)]
    b, t = ring(r0, 0), ring(r1, h)
    bm.faces.new(list(reversed(b)))
    bm.faces.new(t)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((b[i], b[j], t[j], t[i]))
    ob = K.from_bm(name, bm, mat)
    K.bevel(ob, 0.02, segs=2, angle=60)
    return K.place(ob, loc, rot, parent)


def crystal(P, name, base, yaw, pitch, L, r, mat):
    """A six-sided probe crystal from base, pointing along yaw/pitch (degrees), its last fifth a point."""
    bm = bmesh.new()
    ring = lambda rr, z: [bm.verts.new((rr * math.cos(math.pi * i / 3), rr * math.sin(math.pi * i / 3), z)) for i in range(6)]
    b, t = ring(r, 0), ring(r * 0.9, L * 0.8)
    tip = bm.verts.new((0, 0, L))
    bm.faces.new(list(reversed(b)))
    for i in range(6):
        j = (i + 1) % 6
        bm.faces.new((b[i], b[j], t[j], t[i]))
        bm.faces.new((t[i], t[j], tip))
    ob = K.from_bm(name, bm, mat)
    return K.place(ob, base, (0, 90 - pitch, yaw), P)


def glyph(P, name, kind, loc, s, mat, rot=(0, 0, 0)):
    """A carved sign on a face looking towards -Y (turn it with rot): ring, eye, dot, tri, bar, cross, arrow or steps,
    about s tall."""
    G = empty(P, name, loc, rot)
    d = 0.02
    if kind in ("ring", "eye"):
        K.torus(f"{name}_ring", s * 0.4, s * 0.075, mat, rot=(90, 0, 0), segs=14, parent=G)
    if kind in ("dot", "eye"):
        K.cyl(f"{name}_dot", s * 0.14, d, mat, rot=(90, 0, 0), segs=8, bev=0, parent=G)
    if kind == "tri":
        K.cyl(f"{name}_tri", s * 0.5, d, mat, rot=(90, 0, 90), segs=3, bev=0, parent=G)
    if kind in ("bar", "cross", "arrow"):
        K.box(f"{name}_bar", (s * 0.14, d, s), mat, bev=0, parent=G)
    if kind == "cross":
        K.box(f"{name}_hbar", (s * 0.7, d, s * 0.14), mat, loc=(0, 0, s * 0.12), bev=0, parent=G)
    if kind == "arrow":
        for sx in (-1, 1):
            K.box(f"{name}_arm{sx}", (s * 0.12, d, s * 0.45), mat, loc=(sx * s * 0.16, 0, -s * 0.3), rot=(0, sx * 40, 0), bev=0, parent=G)
    if kind == "steps":
        for k in range(3):
            K.box(f"{name}_st{k}", (s * (0.9 - 0.25 * k), d, s * 0.18), mat, loc=(0, 0, -s * 0.35 + k * s * 0.33), bev=0, parent=G)
    return G


GLYPHS = ("ring", "eye", "dot", "tri", "bar", "cross", "arrow", "steps")


def crack(P, name, loc, L, rot=(0, 0, 0), w=0.03):
    """A dark crack line on a face looking towards -Y (turn it with rot)."""
    return K.box(name, (w, 0.02, L), dstone(), loc=loc, rot=rot, bev=0, parent=P)


def panel_frame(P, name, c, w, h, mat, yaw=0):
    """A carved rectangular frame on a face looking towards -Y: four thin strips round a panel w by h."""
    G = empty(P, name, c, (0, 0, yaw))
    b = 0.04
    for k, (x, z, sx, sz) in enumerate(((0, h / 2, w, b), (0, -h / 2, w, b), (-w / 2, 0, b, h), (w / 2, 0, b, h))):
        K.box(f"{name}_{k}", (sx, 0.02, sz), mat, loc=(x, 0, z), bev=0, parent=G)
    return G


def flag():
    """Stone walked on or worn smooth (floors, dais tops, a basin bowl): moss only in patches, not a carpet."""
    return K.mat("flagstone", STONE, rough=0.9, wear=0.0, grime=0.35, moss=0.35, cracks=0.7, noise_scale=3.0)


def cyan(strength=1.2):
    """The probe crystals: the one thing in Old Stone that glows."""
    return glow(CYAN, strength)


# ================================================================== Old Stone
def stone_arch_small(seed=41):
    """A single arch 3 m tall on two piers of stacked blocks, its keystone slipped a hand's width."""
    rnd = random.Random(seed)
    P = _root("arch_small")
    s, sd = stone(), dstone()
    ri, t, spring, D = 0.85, 0.42, 1.55, 0.6
    R = ri + t
    for sx in (-1, 1):
        x = sx * (ri + 0.25)
        K.box(f"plinth{sx}", (0.72, 0.82, 0.2), s, loc=(x, 0, 0.08), bev=0.04, parent=P)
        z = 0.18
        hs = (0.5, 0.45, spring - 0.12 - 0.18 - 0.95)
        for k, h in enumerate(hs):
            K.box(f"pier{sx}{k}", (0.52, D, h - 0.02), sd if rnd.random() < 0.15 else s,
                  loc=(x + rnd.uniform(-0.015, 0.015), 0, z + h / 2), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.04, parent=P)
            z += h
        K.box(f"impost{sx}", (0.62, D + 0.08, 0.12), sd, loc=(x - sx * 0.03, 0, spring - 0.06), bev=0.02, parent=P)
    n = 7
    for k in range(n):
        if k == 3:
            continue
        a0 = 180 * k / n + 1.0
        a1 = 180 * (k + 1) / n - 1.0
        v = shell_panel(f"vous{k}", R, math.radians(a0), math.radians(a1), -D / 2, D / 2, s, t=t, parent=P, loc=(0, 0, spring))
        v.data.polygons.foreach_set("use_smooth", [False] * len(v.data.polygons))
        K.bevel(v, 0.035, segs=2, angle=30)
    # the keystone has dropped a little and stands proud of the faces
    K.box("key", (0.3, D + 0.08, t + 0.12), s, loc=(0, 0, spring + ri + t / 2 - 0.06), rot=(0, 2, 0), bev=0.035, parent=P)
    crack(P, "crack0", (-1.0, -D / 2 - 0.005, 0.75), 0.6, rot=(0, 15, 0))
    crack(P, "crack1", (0.45, -D / 2 - 0.005, spring + 0.85), 0.35, rot=(0, -40, 0))
    moss(P, "moss0", (0.0, 0.05, spring + R + 0.02), 0.16, flat=0.3)
    moss(P, "moss1", (-0.7, -0.05, spring + 0.98), 0.14, flat=0.3)
    moss(P, "moss2", (1.25, -0.45, 0.0), 0.22, flat=0.4)
    rock(P, "chip0", 0.14, 0.12, (0.6, -0.7, 0.05), rnd)
    rock(P, "chip1", 0.1, 0.08, (-1.5, 0.45, 0.03), rnd)
    return P


def stone_column_standing(seed=42):
    """An intact fluted column 5 m tall: square plinth, moulded base, three drums and a capital with its abacus."""
    rnd = random.Random(seed)
    P = _root("column_up")
    s, sd = stone(), dstone()
    K.box("plinth", (1.05, 1.05, 0.32), s, loc=(0, 0, 0.14), bev=0.05, parent=P)
    K.cyl("torus", 0.45, 0.14, s, loc=(0, 0, 0.37), segs=20, bev=0.04, parent=P)
    K.cyl("scotia", 0.39, 0.06, sd, loc=(0, 0, 0.47), segs=20, bev=0.01, parent=P)
    z, r = 0.5, 0.36
    for k in range(3):
        h = 1.27
        r1 = r - 0.022
        fluted(f"drum{k}", r, r1, h - 0.015, s, loc=(0, 0, z), rot=(0, 0, rnd.uniform(0, 12)), parent=P)
        z += h
        r = r1
    K.torus("necking", r + 0.005, 0.025, sd, loc=(0, 0, z - 0.1), segs=24, parent=P)
    K.cyl("echinus", r, 0.28, s, loc=(0, 0, z + 0.14), segs=20, r2=0.5, bev=0.02, parent=P)
    K.box("abacus", (1.05, 1.05, 0.26), s, loc=(0, 0, z + 0.41), rot=(0, 0, 2), bev=0.04, parent=P)
    K.box("abacus_band", (1.07, 1.07, 0.05), sd, loc=(0, 0, z + 0.36), rot=(0, 0, 2), bev=0.01, parent=P)
    # a corner knocked off the abacus lies at the foot
    K.box("corner", (0.26, 0.22, 0.2), s, loc=(0.75, -0.55, 0.1), rot=(10, -6, 28), bev=0.03, parent=P)
    crack(P, "crack0", (0.12, -0.33, 1.6), 0.7, rot=(0, 10, 0))
    crack(P, "crack1", (-0.3, -0.535, 0.16), 0.25, rot=(0, -30, 0))
    moss(P, "moss0", (-0.25, 0.15, z + 0.55), 0.22, flat=0.3)
    moss(P, "moss1", (0.3, 0.3, 0.3), 0.18, flat=0.3)
    moss(P, "moss2", (-0.6, -0.5, 0.0), 0.25, flat=0.4)
    return P


def _pedestal(P, rnd, h=0.75, w=0.6):
    """The shared pedestal: base, step, a die with carved bands and a panel, a moulded cap. Returns the top height."""
    s, sd = stone(), dstone()
    K.box("base", (w + 0.4, w + 0.4, 0.2), s, loc=(0, 0, 0.08), bev=0.04, parent=P)
    K.box("step", (w + 0.2, w + 0.2, 0.12), s, loc=(0, 0, 0.24), bev=0.03, parent=P)
    tapered("die", (w, w), (w - 0.04, w - 0.04), h, s, loc=(0, 0, 0.3), bev=0.03, parent=P)
    K.box("band_lo", (w + 0.02, w + 0.02, 0.05), sd, loc=(0, 0, 0.4), bev=0.01, parent=P)
    K.box("band_hi", (w - 0.02, w - 0.02, 0.05), sd, loc=(0, 0, 0.3 + h - 0.08), bev=0.01, parent=P)
    fy = -(w - 0.02) / 2 - 0.006
    panel_frame(P, "panel", (0, fy, 0.3 + h * 0.5), w - 0.2, h * 0.5, sd)
    tapered("cap_ogee", (w - 0.04, w - 0.04), (w + 0.16, w + 0.16), 0.1, s, loc=(0, 0, 0.3 + h), bev=0.015, parent=P)
    K.box("cap", (w + 0.2, w + 0.2, 0.12), s, loc=(0, 0, 0.3 + h + 0.16), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.03, parent=P)
    return 0.3 + h + 0.22


def stone_pedestal(seed=43):
    """An empty pedestal 1.3 m tall: whatever stood on it is gone, two dowel holes left in its top."""
    rnd = random.Random(seed)
    P = _root("pedestal")
    sd = dstone()
    top = _pedestal(P, rnd)
    glyph(P, "glyph", "eye", (0, -0.296, 0.68), 0.2, sd)
    for k, x in enumerate((-0.14, 0.14)):
        K.box(f"dowel{k}", (0.07, 0.07, 0.02), K.mat("hole", GRAPHITE, rough=1.0), loc=(x, 0.03, top + 0.001), bev=0, parent=P)
        K.box(f"dowel_stain{k}", (0.11, 0.11, 0.01), M(RUST, metal=0.2, wear=0.1), loc=(x, 0.03, top - 0.001), bev=0, parent=P)
    crack(P, "crack", (0.18, -0.305, 0.85), 0.35, rot=(0, 25, 0))
    moss(P, "moss0", (0.22, 0.22, top), 0.12, flat=0.3)
    moss(P, "moss1", (-0.45, 0.4, 0.0), 0.22, flat=0.4)
    rock(P, "chip", 0.1, 0.08, (0.6, -0.55, 0.03), rnd)
    return P


def stone_urn(seed=44):
    """A big carved urn 1.5 m tall on a square foot: carved bands round its belly and shoulder, the lip chipped."""
    rnd = random.Random(seed)
    P = _root("urn")
    s, sd = stone(), dstone()
    K.box("foot", (0.66, 0.66, 0.14), s, loc=(0, 0, 0.05), bev=0.03, parent=P)
    prof = ((0, 0.12), (0.27, 0.12), (0.22, 0.17), (0.16, 0.24), (0.16, 0.32), (0.28, 0.4), (0.42, 0.58), (0.48, 0.8), (0.46, 0.98),
            (0.36, 1.12), (0.27, 1.2), (0.26, 1.33), (0.2, 1.33), (0.2, 1.22), (0, 1.2))
    lathe("body", prof, s, segs=22, parent=P, rot=(0, 0, 8))
    K.torus("belly_band", 0.48, 0.025, sd, loc=(0, 0, 0.8), segs=28, parent=P)
    K.torus("waist_band", 0.165, 0.02, sd, loc=(0, 0, 0.29), segs=16, parent=P)
    K.torus("shoulder_band", 0.375, 0.02, sd, loc=(0, 0, 1.115), segs=24, parent=P)
    K.torus("belly_band2", 0.455, 0.018, sd, loc=(0, 0, 0.66), segs=28, parent=P)
    for sx in (-1, 1):
        K.torus(f"handle{sx}", 0.11, 0.035, s, loc=(sx * 0.44, 0, 1.04), rot=(90, 0, 0), segs=14, parent=P)
    # the lip: a roll with a piece gone from its front
    K.torus("lip", 0.29, 0.05, s, loc=(0, 0, 1.36), rot=(0, 0, -45), arc=300, segs=26, parent=P)
    K.sphere("lip_end0", 0.05, sd, loc=(0.29 * math.cos(math.radians(-45)), 0.29 * math.sin(math.radians(-45)), 1.36), parent=P)
    K.sphere("lip_end1", 0.05, sd, loc=(0.29 * math.cos(math.radians(-105)), 0.29 * math.sin(math.radians(-105)), 1.36), parent=P)
    K.disc("mouth", 0.22, K.mat("hole", GRAPHITE, rough=1.0), (0, 0), z=1.3, segs=16).parent = P
    crack(P, "crack0", (-0.05, -0.5, 0.72), 0.32, rot=(0, 18, 0))
    rock(P, "shard", 0.1, 0.05, (0.55, -0.5, 0.025), rnd, mat=s)
    moss(P, "moss0", (-0.3, 0.3, 0.06), 0.2, flat=0.4)
    moss(P, "moss1", (0.2, 0.25, 1.15), 0.1, flat=0.25)
    return P


def stone_brazier(seed=45):
    """A stone bowl brazier on a turned stem, 1.3 m tall and long cold: old ash and a few charred sticks in the bowl."""
    rnd = random.Random(seed)
    P = _root("brazier")
    s, sd = stone(), dstone()
    K.box("base", (0.62, 0.62, 0.16), s, loc=(0, 0, 0.06), bev=0.03, parent=P)
    prof = ((0, 0.14), (0.22, 0.14), (0.2, 0.2), (0.13, 0.28), (0.11, 0.36), (0.1, 0.86), (0.15, 0.9), (0.15, 0.95), (0.2, 0.98),
            (0.45, 1.1), (0.56, 1.22), (0.58, 1.3), (0.52, 1.3), (0.46, 1.22), (0.3, 1.16), (0, 1.14))
    lathe("bowl", prof, s, segs=22, parent=P)
    K.torus("rim_band", 0.565, 0.02, sd, loc=(0, 0, 1.22), segs=28, parent=P)
    K.torus("stem_band", 0.105, 0.02, sd, loc=(0, 0, 0.6), segs=14, parent=P)
    K.sphere("ash", 0.42, K.mat("ash", CHAR, rough=1.0, noise_scale=8.0), loc=(0, 0, 1.15), scale=(1, 1, 0.14), parent=P)
    for k in range(5):
        a = rnd.uniform(0, 6.28)
        rr = rnd.uniform(0.05, 0.25)
        K.box(f"stick{k}", (rnd.uniform(0.18, 0.3), 0.04, 0.035), K.mat("charred", CHAR_D, rough=1.0), loc=(rr * math.cos(a), rr * math.sin(a), 1.2),
              rot=(rnd.uniform(-8, 8), rnd.uniform(-10, 10), rnd.uniform(0, 180)), bev=0.008, parent=P)
    # soot stains the rim on one side
    K.torus("soot", 0.55, 0.012, K.mat("soot", CHAR_D, rough=1.0), loc=(0, 0, 1.298), arc=120, segs=20, parent=P)
    crack(P, "crack", (0.25, -0.5, 1.17), 0.18, rot=(30, 30, 0))
    moss(P, "moss0", (0.25, 0.2, 0.14), 0.12, flat=0.3)
    moss(P, "moss1", (-0.4, -0.35, 0.0), 0.2, flat=0.4)
    return P


def stone_altar(seed=46):
    """A long altar block on a step: carved panels and glyphs round its sides, a channel cut in its top slab."""
    rnd = random.Random(seed)
    P = _root("altar")
    s, sd = stone(), dstone()
    K.box("step", (2.5, 1.5, 0.2), s, loc=(0, 0, 0.08), bev=0.04, parent=P)
    K.box("block", (2.0, 1.0, 0.8), s, loc=(0, 0, 0.58), bev=0.04, parent=P)
    K.box("band", (2.03, 1.03, 0.06), sd, loc=(0, 0, 0.26), bev=0.01, parent=P)
    K.box("slab", (2.25, 1.2, 0.18), s, loc=(0, 0, 1.06), rot=(0, 0, 0.6), bev=0.04, parent=P)
    for k, (x, kind) in enumerate(((-0.62, "ring"), (0, "eye"), (0.62, "arrow"))):
        panel_frame(P, f"front{k}", (x, -0.506, 0.6), 0.5, 0.48, sd)
        glyph(P, f"fg{k}", kind, (x, -0.506, 0.6), 0.3, sd)
    for sx in (-1, 1):
        panel_frame(P, f"end{sx}", (sx * 1.006, 0, 0.6), 0.7, 0.48, sd, yaw=-sx * 90)
        glyph(P, f"eg{sx}", "steps", (sx * 1.006, 0, 0.6), 0.32, sd, rot=(0, 0, -sx * 90))
    K.box("channel", (1.6, 0.07, 0.02), sd, loc=(0, 0, 1.152), bev=0, parent=P)
    K.torus("dish", 0.2, 0.02, sd, loc=(0.35, 0.2, 1.15), segs=18, parent=P)
    crack(P, "crack0", (0.85, -0.51, 0.55), 0.55, rot=(0, 22, 0))
    crack(P, "crack_back", (-0.4, 0.51, 0.6), 0.6, rot=(0, -15, 0))
    K.box("slab_chip", (0.22, 0.2, 0.15), s, loc=(1.4, -0.85, 0.075), rot=(6, 4, 30), bev=0.03, parent=P)
    moss(P, "moss0", (-0.85, 0.35, 1.16), 0.16, flat=0.3)
    moss(P, "moss1", (1.1, 0.55, 0.19), 0.18, flat=0.3)
    moss(P, "moss2", (-1.3, -0.65, 0.0), 0.22, flat=0.4)
    rock(P, "chip", 0.1, 0.08, (-1.0, 0.95, 0.03), rnd)
    return P


def stone_tablet(seed=47):
    """An upright round-topped tablet 2.4 m tall in a slotted base, its face cut with rows of glyphs."""
    rnd = random.Random(seed)
    P = _root("tablet")
    s, sd = stone(), dstone()
    K.box("base", (1.6, 0.75, 0.36), s, loc=(0, 0, 0.15), bev=0.04, parent=P)
    K.box("base_band", (1.62, 0.77, 0.05), sd, loc=(0, 0, 0.27), bev=0.01, parent=P)
    G = empty(P, "lean", (0, 0, 0.25), (-3, 2, 2))
    W, Hs, t = 1.1, 1.55, 0.24
    pts = [(-W / 2, 0), (W / 2, 0)] + [(W / 2 * math.cos(math.pi * i / 10), Hs + W / 2 * math.sin(math.pi * i / 10)) for i in range(11)]
    profile_slab("slab", pts, t, s, parent=G, bev=0.03)
    fy = -t / 2 - 0.004
    # a border cut round the face, then six rows of glyphs
    K.torus("border_arc", W / 2 - 0.08, 0.014, sd, loc=(0, fy, Hs), rot=(90, 0, 0), arc=180, segs=20, parent=G)
    for sx in (-1, 1):
        K.box(f"border{sx}", (0.028, 0.02, Hs - 0.1), sd, loc=(sx * (W / 2 - 0.08), fy, Hs / 2 + 0.05), bev=0, parent=G)
    K.box("border_b", (W - 0.16, 0.02, 0.028), sd, loc=(0, fy, 0.1), bev=0, parent=G)
    glyph(G, "sun", "eye", (0, fy, Hs + 0.18), 0.3, sd)
    for row in range(6):
        z = Hs - 0.15 - row * 0.23
        for c in range(4):
            if row == 5 and c > 1:
                continue
            glyph(G, f"g{row}{c}", rnd.choice(GLYPHS), (-0.33 + c * 0.22, fy, z), 0.14, sd)
    crack(G, "crack0", (0.3, fy - 0.003, 1.0), 0.8, rot=(0, -20, 0))
    crack(G, "crack1", (0.42, fy - 0.003, 0.5), 0.35, rot=(0, 30, 0))
    moss(G, "moss0", (-0.1, 0.0, Hs + W / 2 - 0.02), 0.13, flat=0.3)
    moss(P, "moss1", (0.6, 0.15, 0.33), 0.14, flat=0.3)
    moss(P, "moss2", (-0.85, -0.45, 0.0), 0.22, flat=0.4)
    rock(P, "chip", 0.11, 0.08, (0.95, -0.5, 0.03), rnd)
    return P


def _figure(P, name, x, y, z, mat, h=0.9, step=1, arms="up"):
    """One figure of a relief, walking left: a disc head, a tapered body, arms and striding legs, proud of the wall."""
    G = empty(P, name, (x, y, z))
    d = 0.06
    K.cyl("head", 0.085, d, mat, loc=(0, 0, h * 0.9), rot=(90, 0, 0), segs=12, bev=0.01, parent=G)
    profile_slab("body", [(-0.11, h * 0.38), (0.11, h * 0.38), (0.14, h * 0.78), (-0.14, h * 0.78)], d, mat, parent=G, bev=0.01)
    for sx in (-1, 1):
        K.box(f"leg{sx}", (0.06, d, h * 0.42), mat, loc=(sx * 0.07 * step, 0, h * 0.2), rot=(0, sx * 12 * step, 0), bev=0.01, parent=G)
    if arms == "up":
        K.box("arm_f", (0.05, d, h * 0.38), mat, loc=(-0.2, 0, h * 0.86), rot=(0, 35, 0), bev=0.01, parent=G)
        K.box("arm_b", (0.05, d, h * 0.34), mat, loc=(0.16, 0, h * 0.6), rot=(0, 20, 0), bev=0.01, parent=G)
    else:
        K.box("arm_f", (0.05, d, h * 0.36), mat, loc=(-0.22, 0, h * 0.68), rot=(0, 70, 0), bev=0.01, parent=G)
        K.box("bowl", (0.16, d, 0.06), mat, loc=(-0.37, 0, h * 0.72), bev=0.01, parent=G)
        K.box("arm_b", (0.05, d, h * 0.34), mat, loc=(0.15, 0, h * 0.6), rot=(0, 15, 0), bev=0.01, parent=G)
    return G


def carved_relief_wall(seed=48):
    """A wall 3.3 m long of three big slabs, its face carved with a procession of figures under a sun; the right slab
    broke off at shoulder height and lies in pieces at its foot."""
    rnd = random.Random(seed)
    P = _root("relief")
    s, sd = stone(), dstone()
    T = 0.5
    K.box("footing", (3.6, 0.85, 0.24), s, loc=(0, 0, 0.1), bev=0.04, parent=P)
    for k, (x, h) in enumerate(((-1.1, 2.2), (0.0, 2.2), (1.1, 1.45))):
        K.box(f"slab{k}", (1.08, T, h), s, loc=(x, 0, 0.22 + h / 2), rot=(0, 0, rnd.uniform(-0.5, 0.5)), bev=0.04, parent=P)
    K.box("coping", (2.32, T + 0.14, 0.18), s, loc=(-0.55, 0, 2.5), rot=(0, 0.4, 0), bev=0.04, parent=P)
    K.box("broken_top", (0.55, T, 0.3), s, loc=(0.92, 0.0, 1.82), rot=(0, 0, 0), bev=0.04, parent=P)
    fy = -T / 2
    # the carved field: a dark ground with bands above and below, the figures standing proud of it
    K.box("field", (3.1, 0.02, 1.3), sd, loc=(0, fy - 0.005, 1.15), bev=0, parent=P)
    K.box("band_lo", (3.2, 0.05, 0.08), s, loc=(0, fy - 0.02, 0.46), bev=0.01, parent=P)
    K.box("band_hi", (2.2, 0.05, 0.08), s, loc=(-0.55, fy - 0.02, 1.84), bev=0.01, parent=P)
    K.box("band_hi_r", (0.5, 0.05, 0.08), s, loc=(0.82, fy - 0.02, 1.84), bev=0.01, parent=P)
    K.torus("sun", 0.2, 0.035, s, loc=(-1.15, fy - 0.03, 2.1), rot=(90, 0, 0), segs=18, parent=P)
    for i in range(8):
        a = 2 * math.pi * i / 8
        K.box(f"ray{i}", (0.03, 0.04, 0.12), s, loc=(-1.15 + 0.32 * math.cos(a), fy - 0.03, 2.1 + 0.32 * math.sin(a)), rot=(0, -math.degrees(a) + 90, 0),
              bev=0, parent=P)
    for k, (x, arms) in enumerate(((-1.15, "up"), (-0.45, "bowl"), (0.25, "bowl"), (0.95, "bowl"))):
        _figure(P, f"fig{k}", x, fy - 0.035, 0.52, s, h=1.15, step=1 if k % 2 else -1, arms=arms)
    # the right slab's top: big pieces at the foot
    K.box("piece0", (0.95, 0.5, 0.6), s, loc=(1.75, -0.75, 0.28), rot=(70, 8, 18), bev=0.04, parent=P)
    K.box("piece1", (0.45, 0.4, 0.3), s, loc=(1.2, -1.15, 0.14), rot=(10, -6, 50), bev=0.04, parent=P)
    rock(P, "chip0", 0.12, 0.1, (0.6, -0.9, 0.04), rnd)
    crack(P, "crack0", (-0.6, fy - 0.03, 1.45), 0.9, rot=(0, 18, 0))
    crack(P, "crack1", (0.5, fy - 0.03, 0.9), 0.5, rot=(0, -25, 0))
    for k, (x, z, r) in enumerate(((-1.3, 2.6, 0.2), (-0.2, 2.6, 0.16), (1.05, 1.98, 0.14))):
        moss(P, f"moss{k}", (x, rnd.uniform(-0.1, 0.1), z), r, flat=0.3)
    moss(P, "moss_foot", (-1.5, -0.55, 0.0), 0.25, flat=0.4)
    return P


def stone_lion(seed=49):
    """A seated guardian beast on a plinth, 2.4 m tall: blocky haunches, straight forelegs, a squared maned head facing
    out. Weathered: one ear broken off, cracks, moss down its back."""
    rnd = random.Random(seed)
    P = _root("lion")
    s, sd = stone(), dstone()
    K.box("plinth", (1.2, 2.1, 0.5), s, loc=(0, 0.05, 0.23), bev=0.05, parent=P)
    K.box("plinth_band", (1.22, 2.12, 0.06), sd, loc=(0, 0.05, 0.38), bev=0.01, parent=P)
    z0 = 0.48
    # haunches and body
    K.box("body", (0.8, 1.05, 0.6), s, loc=(0, 0.42, z0 + 0.3), bev=0.12, parent=P)
    for sx in (-1, 1):
        K.box(f"haunch{sx}", (0.24, 0.85, 0.58), s, loc=(sx * 0.44, 0.48, z0 + 0.3), rot=(-6, 0, 0), bev=0.1, parent=P)
        K.box(f"hpaw{sx}", (0.24, 0.36, 0.14), s, loc=(sx * 0.44, -0.05, z0 + 0.07), bev=0.04, parent=P)
        K.box(f"fleg{sx}", (0.22, 0.26, 0.85), s, loc=(sx * 0.24, -0.5, z0 + 0.48), rot=(-4, 0, 0), bev=0.05, parent=P)
        K.box(f"fpaw{sx}", (0.26, 0.4, 0.15), s, loc=(sx * 0.24, -0.68, z0 + 0.075), bev=0.04, parent=P)
        for t in (-1, 0, 1):
            K.box(f"toe{sx}{t}", (0.012, 0.04, 0.1), sd, loc=(sx * 0.24 + t * 0.07, -0.88, z0 + 0.07), bev=0, parent=P)
    K.box("chest", (0.7, 0.6, 1.0), s, loc=(0, -0.2, z0 + 0.95), rot=(-14, 0, 0), bev=0.12, parent=P)
    # mane: a squared collar of two stepped blocks round the neck
    tapered("mane", (1.0, 0.8), (0.86, 0.66), 0.62, s, loc=(0, -0.35, z0 + 1.15), bev=0.08, parent=P, shift=(0, -0.06))
    K.box("mane_band", (0.98, 0.04, 0.05), sd, loc=(0, -0.76, z0 + 1.3), rot=(-6, 0, 0), bev=0, parent=P)
    K.box("mane_band2", (0.92, 0.04, 0.05), sd, loc=(0, -0.73, z0 + 1.5), rot=(-6, 0, 0), bev=0, parent=P)
    H = empty(P, "head", (0, -0.55, z0 + 1.82), (4, 0, rnd.uniform(-4, 4)))
    K.box("skull", (0.6, 0.58, 0.48), s, bev=0.1, parent=H)
    K.box("muzzle", (0.4, 0.3, 0.27), s, loc=(0, -0.38, -0.1), bev=0.07, parent=H)
    K.box("nose", (0.18, 0.08, 0.1), sd, loc=(0, -0.53, -0.02), bev=0.02, parent=H)
    K.box("jaw", (0.34, 0.26, 0.1), s, loc=(0, -0.37, -0.26), bev=0.03, parent=H)
    K.box("mouth", (0.3, 0.02, 0.025), sd, loc=(0, -0.533, -0.205), bev=0, parent=H)
    K.box("brow", (0.58, 0.14, 0.1), s, loc=(0, -0.28, 0.12), bev=0.03, parent=H)
    for sx in (-1, 1):
        K.box(f"eye{sx}", (0.13, 0.03, 0.05), sd, loc=(sx * 0.15, -0.3, 0.04), bev=0, parent=H)
    tapered("ear_l", (0.16, 0.12), (0.06, 0.06), 0.18, s, loc=(-0.22, 0.05, 0.22), bev=0.02, parent=H)
    tapered("ear_r", (0.16, 0.12), (0.13, 0.1), 0.06, s, loc=(0.22, 0.05, 0.22), bev=0.02, parent=H, slope=0.3)
    # tail curled along the plinth, the broken ear in the grass
    K.box("tail", (0.12, 0.7, 0.1), s, loc=(0.48, 0.75, z0 + 0.05), rot=(0, 0, 6), bev=0.04, parent=P)
    K.box("tail_tip", (0.14, 0.22, 0.14), s, loc=(0.5, 0.28, z0 + 0.07), bev=0.04, parent=P)
    tapered("ear_piece", (0.16, 0.12), (0.06, 0.06), 0.13, s, loc=(0.85, -0.85, 0.06), rot=(80, 10, 40), bev=0.02, parent=P)
    crack(P, "crack0", (-0.2, -0.64, z0 + 0.5), 0.5, rot=(0, 15, 0))
    crack(P, "crack1", (0.3, -1.0, 0.25), 0.3, rot=(0, -30, 0))
    moss(P, "moss0", (0, 0.55, z0 + 0.62), 0.25, flat=0.25, stretch=1.0)
    moss(P, "moss1", (0, -0.5, z0 + 2.08), 0.13, flat=0.3)
    moss(P, "moss2", (-0.45, 0.85, 0.48), 0.14, flat=0.3)
    moss(P, "moss3", (-0.55, -0.85, 0.0), 0.25, flat=0.4)
    return P


def stone_obelisk(seed=50):
    """A 6 m obelisk on a stepped base: a tapering shaft with carved bands, a column of glyphs and a pyramid tip."""
    rnd = random.Random(seed)
    P = _root("obelisk")
    s, sd = stone(), dstone()
    K.box("step0", (2.1, 2.1, 0.3), s, loc=(0, 0, 0.12), bev=0.05, parent=P)
    K.box("step1", (1.55, 1.55, 0.3), s, loc=(0, 0, 0.4), rot=(0, 0, 1), bev=0.04, parent=P)
    K.box("die", (1.08, 1.08, 0.45), s, loc=(0, 0, 0.77), bev=0.04, parent=P)
    K.box("die_band", (1.1, 1.1, 0.05), sd, loc=(0, 0, 0.92), bev=0.01, parent=P)
    z0, H, w0, w1 = 0.99, 4.55, 0.78, 0.5
    tapered("shaft", (w0, w0), (w1, w1), H, s, loc=(0, 0, z0), bev=0.03, parent=P)
    tapered("tip", (w1, w1), (0.02, 0.02), 0.46, s, loc=(0, 0, z0 + H), bev=0.015, parent=P)

    def w(z):
        return w0 + (w1 - w0) * (z - z0) / H

    for k, (z, bh) in enumerate(((z0 + 0.25, 0.07), (z0 + H - 0.5, 0.1), (z0 + H - 0.3, 0.05))):
        tapered(f"band{k}", (w(z) + 0.03, w(z) + 0.03), (w(z + bh) + 0.03, w(z + bh) + 0.03), bh, sd, loc=(0, 0, z), bev=0.005, parent=P)
    tilt = math.degrees(math.atan((w0 - w1) / 2 / H))
    kinds = ["eye", "steps", "arrow", "ring", "tri", "cross", "dot", "bar"]
    for i in range(8):
        z = z0 + 0.7 + i * 0.42
        glyph(P, f"g{i}", kinds[(i + seed) % 8], (0, -w(z) / 2 - 0.006, z), 0.24, sd, rot=(-tilt, 0, 0))
    crack(P, "crack0", (0.2, -w(z0 + 1.2) / 2 - 0.01, z0 + 1.2), 0.9, rot=(-tilt, 0, 12))
    crack(P, "crack1", (-0.6, -0.55, 0.77), 0.3, rot=(0, 25, 0))
    rock(P, "chip0", 0.13, 0.1, (1.25, -0.8, 0.04), rnd)
    for k, (x, y, z, r) in enumerate(((-0.8, -0.85, 0.27, 0.16), (0.55, 0.6, 0.55, 0.14), (0.4, -0.45, 1.0, 0.1), (-1.1, 0.9, 0.0, 0.25))):
        moss(P, f"moss{k}", (x, y, z), r, flat=0.3)
    return P


def stone_tomb(seed=51):
    """A small tomb house 2.6 m by 3.2 m with a pitched stone roof, its door sealed by a slab and a carved bar."""
    rnd = random.Random(seed)
    P = _root("tomb")
    s, sd = stone(), dstone()
    W, D, Hw = 2.4, 3.0, 1.8
    K.box("plinth", (W + 0.4, D + 0.4, 0.26), s, loc=(0, 0, 0.1), bev=0.05, parent=P)
    zb = 0.23
    K.box("walls", (W, D, Hw), s, loc=(0, 0, zb + Hw / 2), bev=0.04, parent=P)
    # coursing: joint lines on the faces, staggered course to course
    for c in range(1, 4):
        z = zb + c * Hw / 4
        for sy in (-1, 1):
            K.box(f"hj{c}{sy}", (W + 0.004, 0.02, 0.02), sd, loc=(0, sy * (D / 2 + 0.002), z), bev=0, parent=P)
        for sx in (-1, 1):
            K.box(f"hjs{c}{sx}", (0.02, D + 0.004, 0.02), sd, loc=(sx * (W / 2 + 0.002), 0, z), bev=0, parent=P)
    for c in range(4):
        z = zb + (c + 0.5) * Hw / 4
        off = 0.5 if c % 2 else 0.0
        for sx in (-1, 1):
            for k in range(3):
                y = -D / 2 + (k + 0.5 + off * 0.8) * D / 3.4
                if y < D / 2 - 0.1:
                    K.box(f"vj{c}{sx}{k}", (0.02, 0.02, Hw / 4), sd, loc=(sx * (W / 2 + 0.002), y, z), bev=0, parent=P)
        if c % 2 == 0:
            K.box(f"vjb{c}", (0.02, 0.02, Hw / 4), sd, loc=(0.3, D / 2 + 0.002, z), bev=0, parent=P)
        else:
            for x in (-0.6, 0.85):
                K.box(f"vjb{c}{x}", (0.02, 0.02, Hw / 4), sd, loc=(x, D / 2 + 0.002, z), bev=0, parent=P)
    zt = zb + Hw
    K.box("cornice", (W + 0.2, D + 0.2, 0.16), s, loc=(0, 0, zt + 0.06), bev=0.04, parent=P)
    zr = zt + 0.14
    pitch = 26
    rise = (W / 2 + 0.05) * math.tan(math.radians(pitch))
    profile_slab("gable", [(-W / 2 - 0.05, 0), (W / 2 + 0.05, 0), (0, rise)], D, s, loc=(0, 0, zr), rot=(0, 0, 0), parent=P, bev=0.03)
    L = (W / 2 + 0.35) / math.cos(math.radians(pitch))
    for sx in (-1, 1):
        xc = (W / 2 + 0.35) / 2
        K.box(f"roof{sx}", (L, D + 0.4, 0.16), s, loc=(sx * xc, 0, zr + rise * (1 - xc / (W / 2 + 0.05)) + 0.09),
              rot=(0, sx * pitch, 0), bev=0.04, parent=P)
    K.box("ridge", (0.26, D + 0.44, 0.16), s, loc=(0, 0, zr + rise + 0.17), bev=0.04, parent=P)
    glyph(P, "gable_glyph", "eye", (0, -D / 2 - 0.006, zr + 0.22), 0.28, sd)
    # the door: jambs and lintel proud of the face, a dark slab sealed with a carved bar
    fy = -D / 2
    for sx in (-1, 1):
        K.box(f"jamb{sx}", (0.26, 0.14, 1.5), s, loc=(sx * 0.58, fy - 0.05, zb + 0.75), bev=0.03, parent=P)
    K.box("lintel", (1.5, 0.16, 0.3), s, loc=(0, fy - 0.06, zb + 1.65), bev=0.03, parent=P)
    K.box("door", (0.9, 0.04, 1.5), sd, loc=(0, fy - 0.015, zb + 0.75), bev=0.01, parent=P)
    K.box("seal_bar", (1.3, 0.12, 0.16), s, loc=(0, fy - 0.08, zb + 0.85), rot=(0, 1.5, 0), bev=0.03, parent=P)
    glyph(P, "door_glyph", "ring", (0, fy - 0.04, zb + 1.25), 0.3, s)
    K.box("threshold", (1.4, 0.4, 0.06), s, loc=(0, fy - 0.3, 0.2), bev=0.02, parent=P)
    crack(P, "crack0", (-0.85, fy - 0.003, 1.25), 0.7, rot=(0, -15, 0))
    crack(P, "crack1", (W / 2 + 0.005, 0.6, 0.9), 0.6, rot=(-20, 0, 90))
    # a corner of the roof has broken away
    K.box("roof_piece", (0.6, 0.5, 0.15), s, loc=(1.75, -1.3, 0.08), rot=(4, -8, 35), bev=0.03, parent=P)
    for k, (x, y, z, r) in enumerate(((0.0, -0.7, zr + rise + 0.25, 0.2), (0.0, 0.9, zr + rise + 0.25, 0.17), (-0.85, 1.5, zt + 0.15, 0.15),
                                      (-1.5, -0.9, 0.0, 0.25))):
        moss(P, f"moss{k}", (x, y, z), r, flat=0.3)
    return P


def stone_sarcophagus(seed=52):
    """A carved stone coffin on a low plinth, its ridged lid pushed askew so a dark gap shows at one end."""
    rnd = random.Random(seed)
    P = _root("sarcophagus")
    s, sd = stone(), dstone()
    K.box("plinth", (2.55, 1.25, 0.15), s, loc=(0, 0, 0.055), bev=0.03, parent=P)
    L, W, H, t = 2.2, 0.9, 0.78, 0.13
    zb = 0.13
    K.box("bottom", (L - 2 * t + 0.04, W - 2 * t + 0.04, 0.2), s, loc=(0, 0, zb + 0.1), bev=0.03, parent=P)
    for sy in (-1, 1):
        K.box(f"side{sy}", (L, t, H), s, loc=(0, sy * (W - t) / 2, zb + H / 2), bev=0.03, parent=P)
    for sx in (-1, 1):
        K.box(f"end{sx}", (t, W - 2 * t + 0.02, H), s, loc=(sx * (L - t) / 2, 0, zb + H / 2), bev=0.03, parent=P)
    K.box("inside", (L - 2 * t, W - 2 * t, 0.02), K.mat("hole", GRAPHITE, rough=1.0), loc=(0, 0, zb + 0.25), bev=0, parent=P)
    K.box("band", (L + 0.02, W + 0.02, 0.05), sd, loc=(0, 0, zb + H - 0.1), bev=0.01, parent=P)
    for k, x in enumerate((-0.68, 0, 0.68)):
        panel_frame(P, f"panel{k}", (x, -W / 2 - 0.006, zb + 0.36), 0.52, 0.38, sd)
    glyph(P, "glyph0", "eye", (0, -W / 2 - 0.006, zb + 0.36), 0.24, sd)
    glyph(P, "glyph1", "arrow", (-0.68, -W / 2 - 0.006, zb + 0.36), 0.24, sd)
    glyph(P, "glyph2", "steps", (0.68, -W / 2 - 0.006, zb + 0.36), 0.24, sd)
    for sx in (-1, 1):
        panel_frame(P, f"endp{sx}", (sx * (L / 2 + 0.006), 0, zb + 0.36), 0.6, 0.38, sd, yaw=-sx * 90)
    # the lid, slid towards +X and turned: a slab with a low ridge along it
    Lg = empty(P, "lid", (0.3, -0.06, zb + H), (0, 1.2, 8))
    K.box("lid_slab", (2.3, 1.0, 0.13), s, loc=(0, 0, 0.065), bev=0.03, parent=Lg)
    tapered("lid_ridge", (2.2, 0.85), (2.0, 0.28), 0.13, s, loc=(0, 0, 0.13), bev=0.02, parent=Lg)
    K.box("lid_band", (2.32, 1.02, 0.03), sd, loc=(0, 0, 0.11), bev=0, parent=Lg)
    crack(P, "crack0", (0.4, -W / 2 - 0.01, zb + 0.45), 0.45, rot=(0, 30, 0))
    rock(P, "chip", 0.1, 0.08, (-1.45, -0.55, 0.03), rnd)
    moss(P, "moss0", (-0.3, 0.1, zb + H + 0.26), 0.15, flat=0.3, stretch=2.0)
    moss(P, "moss1", (1.1, 0.5, 0.13), 0.15, flat=0.3)
    moss(P, "moss2", (-1.2, 0.6, 0.0), 0.22, flat=0.4)
    return P


def stone_fountain_ruin(seed=53):
    """A round fountain basin 3.6 m across of curved blocks, one fallen out; the centre column's bowl broken and tipped,
    the top tier lying in the dry basin by a puddle."""
    rnd = random.Random(seed)
    P = _root("fountain")
    s, sd = stone(), dstone()
    n, r0, r1 = 10, 1.48, 1.78
    K.cyl("floor", 1.55, 0.14, flag(), loc=(0, 0, 0.05), segs=24, bev=0.02, parent=P)
    for i in range(n):
        a0 = 360 * i / n + 0.6
        a1 = 360 * (i + 1) / n - 0.6
        if i == 7:
            continue
        h = 0.55 if i not in (6, 8) else 0.55 - rnd.uniform(0.12, 0.2)
        arc_block(f"rim{i}", r0, r1, a0, a1, h, sd if rnd.random() < 0.15 else s, loc=(0, 0, -0.02), parent=P)
        if h > 0.5:
            K.torus(f"rim_band{i}", r1 + 0.005, 0.018, sd, loc=(0, 0, 0.36), rot=(0, 0, a0 + 0.3), arc=a1 - a0 - 0.6, segs=8, parent=P)
    # the missing block lies tipped outside its gap
    am = math.radians(360 * 7.5 / n)
    E = empty(P, "rim_fallen", (2.3 * math.cos(am), 2.3 * math.sin(am), 0.17), (0, 0, math.degrees(am) + 15))
    arc_block("rim_fallen", r0, r1, -18, 18, 0.55, s, loc=(0, -(r0 + r1) / 2, 0.0), rot=(0, 0, 90), parent=empty(E, "tip", (0, 0, 0), (-75, 0, 0)))
    K.box("rim_chunk", (0.3, 0.22, 0.2), s, loc=(1.7 * math.cos(am - 0.15), 1.7 * math.sin(am - 0.15), 0.1), rot=(8, 6, 40), bev=0.03, parent=P)
    # centre column with its broken bowl
    lathe("column", ((0, 0.11), (0.45, 0.11), (0.45, 0.28), (0.33, 0.36), (0.25, 0.48), (0.21, 1.15), (0.28, 1.25), (0.28, 1.32), (0, 1.32)),
          s, segs=18, parent=P)
    K.torus("col_band", 0.215, 0.018, sd, loc=(0, 0, 0.8), segs=14, parent=P)
    bowl = ((0, 0), (0.2, 0.0), (0.45, 0.14), (0.66, 0.3), (0.68, 0.38), (0.61, 0.38), (0.43, 0.24), (0.0, 0.12))
    lathe("bowl", bowl, flag(), segs=12, arc=230, loc=(0.05, 0, 1.3), rot=(4, -7, 40), parent=P)
    K.box("bowl_shard", (0.45, 0.3, 0.07), s, loc=(-0.85, 0.55, 0.16), rot=(4, -8, 30), bev=0.02, parent=P)
    # the top tier: a small bowl on its side in the basin
    lathe("top_bowl", ((0, 0), (0.12, 0), (0.12, 0.22), (0.38, 0.32), (0.4, 0.38), (0.34, 0.38), (0.1, 0.3), (0, 0.3)), s, segs=16,
          loc=(-0.6, -0.65, 0.5), rot=(-70, 0, 25), parent=P)
    K.disc("puddle", 0.55, K.mat("stone_water", "#26343a", rough=0.05), (0.55, -0.55), z=0.125, segs=20, wobble=0.25, seed=seed).parent = P
    crack(P, "crack0", (0, -0.21, 0.75), 0.4, rot=(0, 20, 0))
    for k in range(5):
        a = rnd.uniform(0, 6.28)
        rr, zz, mr = ((r0 + r1) / 2, 0.53, 0.13) if k < 3 else (2.0, 0.0, 0.25)
        moss(P, f"moss{k}", (rr * math.cos(a), rr * math.sin(a), zz), mr, flat=0.3 if k < 3 else 0.4)
    return P


def stone_bridge_fragment(seed=54):
    """What is left of a stone bridge: one pier with cutwaters, the arch springing from it to one side and broken off
    before its crown, a stub of the other arch, the deck and parapet above, the fallen stones below."""
    rnd = random.Random(seed)
    P = _root("bridge")
    s, sd = stone(), dstone()
    G = empty(P, "shift", (0.4, 0, 0))
    D = 2.4
    zs, ri, t = 2.0, 1.8, 0.5
    R = ri + t
    # pier: courses of two blocks, a pointed cutwater at each face
    z = 0.0
    for c in range(4):
        h = 0.5
        split = 0.15 if c % 2 else -0.15
        for k, (y0, y1) in enumerate(((-D / 2, split), (split, D / 2))):
            K.box(f"pier{c}{k}", (1.2, y1 - y0 - 0.02, h - 0.02), sd if rnd.random() < 0.15 else s,
                  loc=(-1.0 + rnd.uniform(-0.01, 0.01), (y0 + y1) / 2, z + h / 2 - 0.02), bev=0.04, parent=G)
        z += h
    for sy in (-1, 1):
        K.box(f"cutwater{sy}", (0.85, 0.85, 1.7), s, loc=(-1.0, sy * D / 2, 0.83), rot=(0, 0, 45), bev=0.04, parent=G)
        tapered(f"cutcap{sy}", (0.85, 0.85), (0.1, 0.1), 0.45, s, loc=(-1.0, sy * D / 2, 1.68), rot=(0, 0, 45), bev=0.02, parent=G)
    K.box("impost", (1.3, D + 0.1, 0.12), sd, loc=(-1.0, 0, zs - 0.04), bev=0.02, parent=G)
    # the arch to +X: six voussoirs from the springing, the last one slipped
    cx = -0.4 + ri
    step = 180 / 14
    for k in range(6):
        a1 = 180 - k * step - 0.6
        a0 = 180 - (k + 1) * step + 0.6
        v = shell_panel(f"vous{k}", R, math.radians(a0), math.radians(a1), -D / 2, D / 2, s, t=t, parent=G,
                        loc=(cx, 0, zs - (0.06 if k == 5 else 0)))
        v.data.polygons.foreach_set("use_smooth", [False] * len(v.data.polygons))
        K.bevel(v, 0.05, segs=2, angle=30)
        if k == 5:
            v.rotation_euler = (0, math.radians(4), 0)
    # the other arch: a stub of two voussoirs towards -X
    cl = -1.6 - ri
    for k in range(2):
        a0 = k * step + 0.6
        a1 = (k + 1) * step - 0.6
        v = shell_panel(f"stub{k}", R, math.radians(a0), math.radians(a1), -D / 2, D / 2, s, t=t, parent=G, loc=(cl, 0, zs))
        v.data.polygons.foreach_set("use_smooth", [False] * len(v.data.polygons))
        K.bevel(v, 0.05, segs=2, angle=30)
    # spandrel: one slab cut to the arches' backs below and the deck above, broken off at both ends
    zd = zs + R
    pts = [(cl + R * math.cos(a), zs + R * math.sin(a)) for a in (0, step * math.pi / 180, 2 * step * math.pi / 180)]
    pts += [(-1.45, 3.45), (-1.62, zd), (0.72, zd), (0.86, zd - 0.12)]
    pts += [(cx + R * math.cos(math.radians(a)), zs + R * math.sin(math.radians(a))) for a in (180 - 6 * step + 1, 117, 130, 143, 156, 168, 180)]
    profile_slab("spandrel", pts, D - 0.08, s, parent=G, bev=0.03)
    for k, (x, z, L) in enumerate(((-1.25, 3.85, 1.3), (-0.3, 4.05, 1.0))):
        K.box(f"course{k}", (L, 0.02, 0.025), sd, loc=(x, -D / 2 + 0.035, z), bev=0, parent=G)
    # deck in three strips, each broken at a different length; the parapet on both edges
    for k, (y, x1) in enumerate(((-0.85, 0.85), (0.0, 0.55), (0.85, 0.7))):
        x0 = -2.0
        K.box(f"deck{k}", (x1 - x0, 0.78, 0.24), s, loc=((x0 + x1) / 2, y, zd + 0.1), rot=(0, 0, 0), bev=0.04, parent=G)
    for sy in (-1, 1):
        x = -1.95
        k = 0
        end = 0.6 if sy < 0 else 0.2
        while x < end:
            L = min(0.75, end - x + 0.1)
            if not (sy > 0 and k == 1):
                K.box(f"par{sy}{k}", (L - 0.03, 0.3, 0.5), sd if rnd.random() < 0.2 else s, loc=(x + L / 2, sy * (D / 2 - 0.15), zd + 0.47),
                      rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.04, parent=G)
            x += L
            k += 1
    K.box("deck_band", (2.6, 0.03, 0.08), sd, loc=(-0.7, -D / 2 - 0.01, zd - 0.05), bev=0, parent=G)
    # fallen stones below the break
    K.box("fallen0", (0.75, 0.6, 0.5), s, loc=(1.8, -0.4, 0.24), rot=(12, 4, 25), bev=0.05, parent=G)
    K.box("fallen1", (0.7, 0.5, 0.3), s, loc=(2.1, 0.7, 0.14), rot=(-6, 10, -20), bev=0.04, parent=G)
    E = empty(G, "fallen_vous", (1.35, 0.3, 0.32), (0, 18, 12))
    v = shell_panel("fallen_vous", R, math.radians(80), math.radians(92), -0.9, 0.9, s, t=t, parent=E, loc=(0, 0, -R + t / 2))
    v.data.polygons.foreach_set("use_smooth", [False] * len(v.data.polygons))
    K.bevel(v, 0.05, segs=2, angle=30)
    K.box("par_fallen", (0.7, 0.3, 0.5), s, loc=(0.9, 1.6, 0.15), rot=(80, 0, 15), bev=0.04, parent=G)
    rock(G, "chip0", 0.15, 0.12, (1.3, -1.3, 0.05), rnd)
    crack(G, "crack0", (-1.0, -D / 2 - 0.44, 1.0), 0.6, rot=(0, 10, 0))
    for k, (x, y, z, r) in enumerate(((-1.3, -0.75, zd + 0.23, 0.2), (0.1, 0.1, zd + 0.23, 0.22), (-0.6, -D / 2 + 0.15, zd + 0.73, 0.14),
                                      (-1.4, 1.6, 0.0, 0.3), (-1.0, -1.85, 0.0, 0.25))):
        moss(G, f"moss{k}", (x, y, z), r, flat=0.3)
    return P


def stone_gate_pillars(seed=55):
    """Two gate pillars 4 m tall, 3 m apart: plinths, coursed shafts, carved bands and pyramid caps; the gate is long
    gone, two rusted hinge pins still in the left pillar."""
    rnd = random.Random(seed)
    P = _root("gate_pillars")
    s, sd = stone(), dstone()
    for sx in (-1, 1):
        x = sx * 1.9
        K.box(f"plinth{sx}", (1.15, 1.15, 0.32), s, loc=(x, 0, 0.14), bev=0.04, parent=P)
        z = 0.3
        for k in range(4):
            h = 0.68 + rnd.uniform(-0.04, 0.04)
            K.box(f"shaft{sx}{k}", (0.82, 0.82, h - 0.02), sd if rnd.random() < 0.12 else s, loc=(x + rnd.uniform(-0.01, 0.01), 0, z + h / 2),
                  rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.04, parent=P)
            z += h
        K.box(f"band{sx}", (0.86, 0.86, 0.06), sd, loc=(x, 0, z - 0.2), bev=0.01, parent=P)
        K.box(f"cornice{sx}", (1.02, 1.02, 0.16), s, loc=(x, 0, z + 0.08), bev=0.03, parent=P)
        lean = (0, 0, 0) if sx < 0 else (3, -4, 6)
        C = empty(P, f"cap{sx}", (x + (0.04 if sx > 0 else 0), 0, z + 0.16), lean)
        K.box(f"cap_base{sx}", (0.9, 0.9, 0.12), s, loc=(0, 0, 0.06), bev=0.02, parent=C)
        tapered(f"cap_pyr{sx}", (0.9, 0.9), (0.12, 0.12), 0.45, s, loc=(0, 0, 0.12), bev=0.02, parent=C)
        glyph(P, f"glyph{sx}", "eye" if sx < 0 else "arrow", (x, -0.416, 2.2), 0.3, sd)
        crack(P, f"crack{sx}", (x + 0.2 * sx, -0.416, 1.2), 0.6, rot=(0, 15 * sx, 0))
        moss(C, f"moss_cap{sx}", (0.2, 0.25, 0.13), 0.12, flat=0.3)
        moss(P, f"moss_foot{sx}", (x - 0.55, 0.4, 0.0), 0.25, flat=0.4)
    for k, z in enumerate((0.75, 2.3)):
        K.cyl(f"pin{k}", 0.03, 0.16, M(RUST, metal=0.4, wear=0.1), loc=(-1.43, -0.2, z), rot=(0, 90, 0), segs=8, bev=0.005, parent=P)
        K.cyl(f"pin_up{k}", 0.025, 0.1, M(RUST, metal=0.4, wear=0.1), loc=(-1.37, -0.2, z + 0.05), segs=8, bev=0.005, parent=P)
        K.box(f"rust_stain{k}", (0.005, 0.06, 0.3), M(RUST, metal=0.1, wear=0.05), loc=(-1.489, -0.2, z - 0.15), bev=0, parent=P)
    rock(P, "chip", 0.12, 0.1, (2.5, -0.8, 0.04), rnd)
    return P


def stone_tower_stump(seed=56):
    """The base of a round tower 4.7 m across: courses of curved blocks, broken down to knee height at the front and
    standing about 4 m at the back, rubble inside and out."""
    rnd = random.Random(seed)
    P = _root("tower_stump")
    s, sd = stone(), dstone()
    r0, r1, n, ch = 1.75, 2.3, 9, 0.6
    for i in range(12):
        a0, a1 = 360 * i / 12 + 0.4, 360 * (i + 1) / 12 - 0.4
        arc_block(f"footing{i}", r0 - 0.05, r1 + 0.12, a0, a1, 0.25, s, loc=(0, 0, -0.03), parent=P)
    for c in range(7):
        for i in range(n):
            a0 = 360 * (i + 0.5 * (c % 2)) / n + 0.5
            a1 = 360 * (i + 1 + 0.5 * (c % 2)) / n - 0.5
            am = math.radians((a0 + a1) / 2)
            top = 0.9 + 3.4 * ((0.5 + 0.5 * math.sin(am)) ** 0.9) + rnd.uniform(-0.35, 0.35)
            z = 0.22 + c * ch
            if z + ch > top + 0.2:
                continue
            last = z + 2 * ch > top + 0.2
            arc_block(f"blk{c}_{i}", r0 + rnd.uniform(-0.02, 0.02), r1, a0, a1, ch - 0.02, sd if rnd.random() < 0.15 else s,
                      loc=(0, 0, z), rot=(rnd.uniform(-2, 2) if last else 0, rnd.uniform(-2, 2) if last else 0, rnd.uniform(-1, 1) if last else 0),
                      parent=P)
    # an arrow slit high in the back wall, a band at the second course
    K.box("slit", (0.1, 0.62, 0.9), K.mat("hole", GRAPHITE, rough=1.0), loc=(0, (r0 + r1) / 2, 3.0), bev=0, parent=P)
    for k in range(3):
        a = math.radians(55 + k * 35)
        K.torus(f"band{k}", r1 + 0.01, 0.025, sd, loc=(0, 0, 1.4), rot=(0, 0, 40 + k * 35), arc=30, segs=10, parent=P)
    K.disc("floor", r0, earth(), (0, 0), z=0.04, segs=20, wobble=0.05).parent = P
    for k in range(10):
        inside = k < 4
        a = rnd.uniform(math.radians(200), math.radians(340)) if not inside else rnd.uniform(0, 6.28)
        rr = rnd.uniform(2.6, 3.4) if not inside else rnd.uniform(0.3, 1.3)
        sz = (rnd.uniform(0.45, 0.75), rnd.uniform(0.35, 0.5), rnd.uniform(0.3, 0.5))
        K.box(f"rubble{k}", sz, sd if rnd.random() < 0.2 else s, loc=(rr * math.cos(a), rr * math.sin(a), sz[2] * 0.4),
              rot=(rnd.uniform(-15, 15), rnd.uniform(-20, 20), rnd.uniform(0, 90)), bev=0.04, parent=P)
    for k in range(5):
        a = math.radians(30 + k * 30)
        top = 0.9 + 3.4 * ((0.5 + 0.5 * math.sin(a)) ** 0.9)
        moss(P, f"moss{k}", (2.02 * math.cos(a), 2.02 * math.sin(a), 0.22 + int((top + 0.2 - 0.22) / ch) * ch), 0.2, flat=0.3)
    moss(P, "moss_foot", (-2.4, -1.0, 0.0), 0.3, flat=0.4)
    return P


def stone_stair_spiral_ruin(seed=57):
    """A spiral stair whose tower has gone: wedge steps winding round a stone core that ends broken above them, a curve
    of the outer wall still at the back and two fallen steps in the grass."""
    rnd = random.Random(seed)
    P = _root("spiral")
    s, sd, f = stone(), dstone(), flag()
    K.cyl("base", 1.45, 0.16, f, loc=(0, 0, 0.05), segs=20, bev=0.03, parent=P)
    z = 0.13
    for k, h in enumerate((1.1, 1.1, 0.75)):
        K.cyl(f"core{k}", 0.3, h - 0.02, s, loc=(0, 0, z + h / 2), rot=((7, 0, 0) if k == 2 else (0, 0, 0)), segs=14, bev=0.03, parent=P)
        z += h
    K.cyl("core_band", 0.31, 0.06, sd, loc=(0, 0, 1.24), segs=14, bev=0.01, parent=P)
    rise, turn = 0.2, 27
    for k in range(13):
        if k in (11,):
            continue
        a0 = -110 + k * turn
        outer = 1.3 if k not in (9, 12) else rnd.uniform(0.7, 0.85)
        arc_block(f"step{k}", 0.25, outer, a0, a0 + turn + 3, rise + 0.04, sd if rnd.random() < 0.1 else f,
                  loc=(0, 0, 0.13 + k * rise - 0.04), parent=P, sub=2, bev=0.03)
    # the curve of outer wall that still stands at the back
    for c in range(3):
        for i in range(4 - c):
            a0 = 70 + i * 28 + c * 10
            arc_block(f"wall{c}_{i}", 1.35, 1.7, a0, a0 + 27, 0.48, s, loc=(0, 0, 0.13 + c * 0.5), parent=P, sub=2)
    for k, (x, y, yaw) in enumerate(((1.7, -1.3, 30), (-1.9, -0.9, -70))):
        arc_block(f"fallen{k}", 0.25, 1.3, -14, 14, rise + 0.04, f, loc=(x - 0.8 * math.cos(math.radians(yaw)), y - 0.8 * math.sin(math.radians(yaw)), 0.0),
                  rot=(rnd.uniform(-6, 6), rnd.uniform(-6, 6), yaw), parent=P, sub=2, bev=0.03)
    crack(P, "crack", (0.0, -0.305, 2.0), 0.5, rot=(0, 12, 0))
    moss(P, "moss0", (0.0, 0.0, 2.95), 0.18, flat=0.3)
    moss(P, "moss1", (-0.9, 0.6, 0.21), 0.2, flat=0.3)
    moss(P, "moss2", (-0.5, 1.45, 1.61), 0.15, flat=0.3)
    moss(P, "moss3", (1.6, 0.4, 0.0), 0.25, flat=0.4)
    return P


def stone_floor_tiles(seed=58):
    """A 4 m patch of flagstone floor flush with the grass: worn slabs in rows, some lifted or cracked, a few gone."""
    rnd = random.Random(seed)
    P = _root("flagstones")
    s, sd = flag(), dstone()
    y = -1.8
    k = 0
    while y < 1.75:
        dw = rnd.uniform(0.6, 0.85)
        x = -2.0 + rnd.uniform(0, 0.3)
        edge = 1.8 - abs(y) * 0.25
        while x < edge:
            L = rnd.uniform(0.6, 1.0)
            cx, cy = x + L / 2, y + dw / 2
            far = math.hypot(cx / 2.1, cy / 1.9)
            if rnd.random() > 0.12 and far < 1.0 + rnd.uniform(-0.1, 0.05):
                lift = rnd.random() < 0.12
                rot = (rnd.uniform(3, 7) * rnd.choice((-1, 1)), rnd.uniform(-3, 3), rnd.uniform(-3, 3)) if lift else (rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-2, 2))
                mat = sd if rnd.random() < 0.15 else s
                if rnd.random() < 0.1:
                    for h in (-1, 1):
                        K.box(f"tile{k}{h}", (L / 2 - 0.06, dw - 0.05, 0.12), mat, loc=(cx + h * L / 4, cy, 0.0), rot=rot, bev=0.03, parent=P)
                else:
                    K.box(f"tile{k}", (L - 0.05, dw - 0.05, 0.12), mat, loc=(cx, cy, 0.0 + (0.03 if lift else 0)), rot=rot, bev=0.03, parent=P)
                k += 1
            x += L
        y += dw
    for i in range(6):
        moss(P, f"moss{i}", (rnd.uniform(-1.6, 1.6), rnd.uniform(-1.5, 1.5), 0.04), rnd.uniform(0.1, 0.18), flat=0.25)
    return P


def stone_dais(seed=59):
    """A round dais 5.4 m across in three tiers of curved blocks, each tier a step; an inlaid ring on the top, one block
    of the lowest tier pushed out."""
    rnd = random.Random(seed)
    P = _root("dais")
    s, sd = flag(), dstone()
    tiers = ((2.7, 2.15, 14), (2.15, 1.6, 12), (1.6, 0.0, 0))
    h = 0.28
    for t, (ro, rin, n) in enumerate(tiers):
        z = t * h - 0.02
        if n:
            K.cyl(f"core{t}", rin + 0.02, h, s, loc=(0, 0, z + h / 2), segs=24, bev=0, parent=P)
            for i in range(n):
                a0, a1 = 360 * (i + 0.5 * t) / n + 0.4, 360 * (i + 1 + 0.5 * t) / n - 0.4
                if t == 0 and i == 10:
                    am = math.radians((a0 + a1) / 2)
                    arc_block(f"tier{t}_{i}", rin, ro, a0, a1, h, s, loc=(0.35 * math.cos(am), 0.35 * math.sin(am), z), rot=(0, 0, 5), parent=P)
                    continue
                arc_block(f"tier{t}_{i}", rin, ro, a0, a1, h, sd if rnd.random() < 0.12 else s, loc=(0, 0, z), parent=P)
        else:
            K.cyl("top", ro, h, s, loc=(0, 0, z + h / 2), segs=28, bev=0.04, parent=P)
    zt = 3 * h - 0.02
    K.torus("inlay", 1.05, 0.03, sd, loc=(0, 0, zt - 0.012), segs=32, parent=P)
    K.cyl("centre", 0.42, 0.02, sd, loc=(0, 0, zt), segs=16, bev=0, parent=P)
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        K.box(f"spoke{i}", (0.55, 0.05, 0.02), sd, loc=(0.75 * math.cos(a), 0.75 * math.sin(a), zt), rot=(0, 0, math.degrees(a)), bev=0, parent=P)
    for k, (x, y) in enumerate(((0.6, 0.9), (-1.2, -0.5))):
        crack(P, f"crack{k}", (x, y, zt - 0.005), 0.7, rot=(90, 0, 30 + k * 70))
    for k in range(5):
        a = rnd.uniform(0, 6.28)
        rr, zz = ((2.4, h), (1.85, 2 * h), (1.2, 3 * h))[k % 3]
        moss(P, f"moss{k}", (rr * math.cos(a), rr * math.sin(a), zz - 0.02), 0.18, flat=0.3)
    rock(P, "chip", 0.13, 0.1, (2.9, -1.2, 0.04), rnd)
    return P


def stone_circle_ring(seed=60):
    """A ring of eight short standing stones 4.6 m across, leaning this way and that, one fallen flat; a flat stone at
    the centre."""
    rnd = random.Random(seed)
    P = _root("stone_circle")
    s, sd = stone(), dstone()
    Rr = 2.2
    for i in range(8):
        a = 2 * math.pi * i / 8 + rnd.uniform(-0.08, 0.08)
        x, y = Rr * math.cos(a), Rr * math.sin(a)
        h = rnd.uniform(0.9, 1.45)
        w = rnd.uniform(0.5, 0.7)
        dd = rnd.uniform(0.32, 0.42)
        yaw = math.degrees(a) + 90
        if i == 5:
            G = empty(P, f"fallen{i}", (x + 0.4 * math.cos(a), y + 0.4 * math.sin(a), dd / 2 + 0.02), (90, 0, yaw - 90 + 10))
            tapered(f"stone{i}", (w, dd), (w * 0.7, dd * 0.8), h, s, loc=(0, 0, -h / 2), bev=0.06, parent=G, slope=0.2)
            continue
        G = empty(P, f"st{i}", (x, y, -0.15), (rnd.uniform(-7, 7), rnd.uniform(-7, 7), yaw))
        tapered(f"stone{i}", (w, dd), (w * rnd.uniform(0.6, 0.8), dd * 0.8), h + 0.15, sd if i == 2 else s, bev=0.06, parent=G,
                slope=rnd.uniform(-0.2, 0.2))
        if i in (0, 4):
            tapered(f"band{i}", (w * 0.93 + 0.03, dd * 0.92 + 0.03), (w * 0.9 + 0.03, dd * 0.9 + 0.03), 0.06, sd, loc=(0, 0, h * 0.62), bev=0.005, parent=G)
        moss(P, f"moss{i}", (x * 1.08, y * 1.08, 0.0), 0.2, flat=0.4)
        rock(P, f"pack{i}", 0.12, 0.1, (x * 0.88, y * 0.88, 0.03), rnd)
    rock(P, "centre", 0.55, 0.16, (0, 0, 0.06), rnd, rot=(0, 0, 20))
    glyph(P, "centre_glyph", "ring", (0.0, 0.0, 0.142), 0.35, sd, rot=(-90, 0, 0))
    return P


def stone_face_mask(seed=61):
    """A big carved face slab 2.3 m tall leaning back against rocks: heavy brow, half-lidded eyes, flat nose and set mouth,
    a crack through one cheek."""
    rnd = random.Random(seed)
    P = _root("face_mask")
    s, sd = stone(), dstone()
    G = empty(P, "slab", (0, -0.1, -0.08), (-22, 0, 4))
    W, Hh, T = 1.8, 2.35, 0.4
    tapered("slab", (W, T), (W * 0.82, T), Hh, s, bev=0.06, parent=G)
    fy = -T / 2

    def fw(z):
        return (W + (W * 0.82 - W) * z / Hh) / 2

    tapered("border_l", (0.06, 0.03), (0.06, 0.03), Hh - 0.3, sd, loc=(-fw(0) + 0.12, fy - 0.003, 0.15), rot=(0, 2.0, 0), bev=0, parent=G)
    tapered("border_r", (0.06, 0.03), (0.06, 0.03), Hh - 0.3, sd, loc=(fw(0) - 0.12, fy - 0.003, 0.15), rot=(0, -2.0, 0), bev=0, parent=G)
    K.box("brow", (1.25, 0.16, 0.16), s, loc=(0, fy - 0.06, 1.62), bev=0.05, parent=G)
    for sx in (-1, 1):
        eye = K.cyl(f"socket{sx}", 0.17, 0.02, sd, loc=(sx * 0.33, fy - 0.003, 1.38), rot=(90, 0, 0), segs=14, bev=0, parent=G)
        eye.scale = (1.45, 0.75, 1)
        K.box(f"lid{sx}", (0.4, 0.07, 0.1), s, loc=(sx * 0.33, fy - 0.03, 1.43), bev=0.025, parent=G)
        for k in range(3):
            K.box(f"mark{sx}{k}", (0.03, 0.02, 0.28), sd, loc=(sx * (0.46 + k * 0.07), fy - 0.003, 0.95), bev=0, parent=G)
    bm = bmesh.new()
    top = bm.verts.new((0, fy - 0.04, 1.5))
    l, r = bm.verts.new((-0.2, fy, 0.92)), bm.verts.new((0.2, fy, 0.92))
    tip = bm.verts.new((0, fy - 0.24, 0.96))
    for f in ((top, l, tip), (top, tip, r), (l, r, tip), (top, r, l)):
        bm.faces.new(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    nose = K.from_bm("nose", bm, s)
    K.bevel(nose, 0.025, segs=2, angle=30)
    K.place(nose, parent=G)
    K.box("lip_u", (0.52, 0.09, 0.09), s, loc=(0, fy - 0.03, 0.66), bev=0.03, parent=G)
    K.box("mouth", (0.48, 0.02, 0.03), sd, loc=(0, fy - 0.07, 0.6), bev=0, parent=G)
    K.box("lip_l", (0.42, 0.08, 0.08), s, loc=(0, fy - 0.03, 0.54), bev=0.03, parent=G)
    K.box("chin", (0.5, 0.06, 0.22), s, loc=(0, fy - 0.02, 0.33), bev=0.03, parent=G)
    glyph(G, "brow_glyph", "eye", (0, fy - 0.003, 1.92), 0.22, sd)
    crack(G, "crack0", (0.48, fy - 0.01, 1.1), 0.9, rot=(0, 25, 0))
    crack(G, "crack1", (0.3, fy - 0.01, 0.45), 0.4, rot=(0, -20, 0))
    # the rocks it rests on, and a piece of its top corner in the grass
    rock(P, "prop0", 0.45, 0.6, (0.25, 0.85, 0.3), rnd, rot=(4, -6, 20))
    rock(P, "prop1", 0.35, 0.45, (-0.55, 0.7, 0.22), rnd, rot=(-5, 6, 70))
    rock(P, "foot0", 0.22, 0.2, (-0.7, -0.5, 0.08), rnd)
    rock(P, "foot1", 0.18, 0.16, (0.75, -0.45, 0.06), rnd)
    K.box("corner", (0.35, 0.3, 0.3), s, loc=(1.15, -0.2, 0.14), rot=(15, 10, 40), bev=0.04, parent=P)
    moss(G, "moss0", (-0.3, 0.0, Hh), 0.2, flat=0.3)
    moss(P, "moss1", (0.3, 0.85, 0.62), 0.25, flat=0.3)
    moss(P, "moss2", (-1.0, 0.2, 0.0), 0.25, flat=0.4)
    return P


def stone_orb_pedestal(seed=62):
    """A pedestal holding a probe orb: a faceted cyan crystal ball in a four-clawed stone cup, faintly lit, two small
    crystals pushing out of the cracked base."""
    rnd = random.Random(seed)
    P = _root("orb_pedestal")
    s, sd = stone(), dstone()
    top = _pedestal(P, rnd, h=0.6, w=0.55)
    glyph(P, "glyph", "ring", (0, -0.271, 0.6), 0.18, sd)
    lathe("cup", ((0, top), (0.16, top), (0.12, top + 0.06), (0.14, top + 0.1), (0.24, top + 0.18), (0.25, top + 0.22), (0.2, top + 0.22),
                  (0.0, top + 0.12)), s, segs=16, parent=P)
    rc = 0.27
    zc = top + 0.12 + rc * 0.8
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        K.box(f"claw{i}", (0.07, 0.07, 0.3), s, loc=(0.25 * math.cos(a), 0.25 * math.sin(a), top + 0.3),
              rot=(-18 * math.sin(a), 18 * math.cos(a), 0), bev=0.02, parent=P)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=rc)
    orb = K.from_bm("orb", bm, cyan(0.6))
    K.place(orb, (0, 0, zc), (10, 20, 0), P)
    m = cyan(1.0)
    crystal(P, "shard0", (0.36, -0.42, 0.18), -60, 55, 0.35, 0.05, m)
    crystal(P, "shard1", (0.42, -0.35, 0.18), -20, 70, 0.22, 0.035, m)
    crack(P, "crack", (0.33, -0.48, 0.12), 0.25, rot=(0, 35, 0))
    moss(P, "moss0", (-0.25, 0.25, top), 0.1, flat=0.3)
    moss(P, "moss1", (-0.45, 0.4, 0.0), 0.2, flat=0.4)
    return P


def crystal_probe_cluster(seed=63):
    """A cluster of cyan probe crystals, the tallest 1.7 m, that has grown out of a carved stone socket and split it."""
    rnd = random.Random(seed)
    P = _root("crystal_cluster")
    s, sd = stone(), dstone()
    K.cyl("socket", 0.72, 0.5, s, loc=(0, 0, 0.2), rot=(0, 0, 22.5), segs=8, r2=0.64, bev=0.04, parent=P)
    K.cyl("socket_band", 0.7, 0.06, sd, loc=(0, 0, 0.3), rot=(0, 0, 22.5), segs=8, bev=0.01, parent=P)
    K.cyl("socket_lip", 0.68, 0.1, s, loc=(0, 0, 0.48), rot=(0, 0, 22.5), segs=8, r2=0.66, bev=0.02, parent=P)
    K.disc("socket_hole", 0.5, K.mat("hole", GRAPHITE, rough=1.0), (0, 0), z=0.535, segs=8).parent = P
    # a split piece of the socket wall pushed out
    K.box("split", (0.5, 0.18, 0.45), s, loc=(0.78, -0.42, 0.2), rot=(0, 18, -30), bev=0.03, parent=P)
    crack(P, "crack0", (-0.2, -0.66, 0.3), 0.45, rot=(-8, 0, 0), w=0.035)
    m = cyan(1.2)
    for k, (dx, dy, yaw, pitch, L, r) in enumerate(((0.0, 0.05, 30, 86, 1.7, 0.17), (0.15, -0.12, -40, 68, 1.15, 0.13), (-0.2, -0.08, 200, 64, 1.0, 0.12),
                                                    (-0.05, 0.25, 110, 60, 0.85, 0.1), (0.25, 0.18, 40, 52, 0.7, 0.09), (-0.25, 0.15, 150, 45, 0.55, 0.08),
                                                    (0.1, -0.3, -80, 48, 0.6, 0.08))):
        crystal(P, f"crystal{k}", (dx, dy, 0.4), yaw, pitch, L, r, m)
    for k, (x, y, yaw, pitch, L) in enumerate(((0.95, 0.3, 10, 60, 0.32), (1.0, 0.45, 60, 70, 0.22), (-0.9, -0.5, 200, 55, 0.3), (-0.2, 0.95, 100, 65, 0.25))):
        crystal(P, f"ground{k}", (x, y, -0.02), yaw, pitch, L, 0.05, m)
    moss(P, "moss0", (-0.55, 0.45, 0.52), 0.12, flat=0.3)
    moss(P, "moss1", (0.6, 0.7, 0.0), 0.22, flat=0.4)
    rock(P, "chip", 0.12, 0.1, (-0.95, 0.35, 0.04), rnd)
    return P


def stone_hand_fragment(seed=64):
    """The hand of a giant statue, broken at the wrist and standing in the ground 2.7 m tall, fingers up and a little
    curled, the little finger snapped off and lying at its foot."""
    rnd = random.Random(seed)
    P = _root("hand")
    s, sd, hs = stone(), dstone(), flag()
    G = empty(P, "hand", (0, 0, -0.25), (-5, 4, 15))
    tapered("wrist", (0.85, 0.6), (0.95, 0.5), 0.75, hs, bev=0.12, parent=G)
    K.box("palm", (1.08, 0.48, 1.0), hs, loc=(0, 0, 1.2), bev=0.12, parent=G)
    K.box("heel", (0.4, 0.2, 0.42), hs, loc=(-0.3, -0.22, 0.92), bev=0.1, parent=G)
    crack(G, "line0", (0.1, -0.245, 1.25), 0.6, rot=(0, 55, 0), w=0.025)
    crack(G, "line1", (0.0, -0.245, 1.45), 0.5, rot=(0, 75, 0), w=0.025)
    fingers = ((-0.39, 1.15, 0.25), (-0.13, 1.3, 0.26), (0.13, 1.22, 0.26), (0.38, 0.95, 0.23))
    for f, (x, L, w) in enumerate(fingers):
        broken = f == 3
        E = empty(G, f"finger{f}", (x, 0.02, 1.62), (4 + f * 2, 0, (x * -6)))
        for k, frac in enumerate((0.44, 0.31, 0.25)):
            if broken and k > 0:
                break
            h = L * frac
            K.box(f"f{f}_{k}", (w - k * 0.02, 0.36 - k * 0.03, h + 0.03), hs, loc=(0, 0, h / 2), bev=0.08 if k == 2 else 0.06, parent=E)
            if k < 2:
                E = empty(E, f"joint{f}{k}", (0, 0, h), (10 + k * 6, 0, 0))
        if not broken:
            K.box(f"nail{f}", (w * 0.5, 0.02, 0.14), sd, loc=(0, 0.16 - 0.015, L * 0.25 - 0.12), bev=0, parent=E)
        else:
            K.box("break", (w - 0.04, 0.3, 0.02), sd, loc=(0, 0, L * 0.44 + 0.012), bev=0, parent=E)
    T = empty(G, "thumb", (-0.45, -0.12, 0.95), (12, -40, -20))
    K.box("thumb0", (0.28, 0.33, 0.5), hs, loc=(0, 0, 0.22), bev=0.08, parent=T)
    T2 = empty(T, "thumb_j", (0, 0, 0.46), (14, 0, 0))
    K.box("thumb1", (0.25, 0.3, 0.4), hs, loc=(0, 0, 0.18), bev=0.08, parent=T2)
    # the snapped finger in the grass, the earth heaped round the wrist
    Fz = empty(P, "fallen_finger", (0.95, -0.7, 0.13), (90, 0, 35))
    for k, (h, dz) in enumerate(((0.21, 0.0), (0.17, 0.2))):
        K.box(f"ff{k}", (0.2, 0.3, h), s, loc=(0, 0, dz), rot=(k * 15, 0, 0), bev=0.06, parent=Fz)
    K.sphere("heap", 0.8, earth(), loc=(0, 0.05, 0.0), scale=(1.25, 1.0, 0.3), parent=P)
    crack(G, "crack0", (0.3, -0.31, 0.5), 0.5, rot=(0, -20, 0))
    moss(P, "moss1", (0.5, 0.45, 0.2), 0.25, flat=0.35)
    moss(P, "moss2", (-0.8, -0.3, 0.12), 0.25, flat=0.35)
    return P


def stone_lantern(seed=65):
    """A stone lantern post 2.3 m tall: plinth, square shaft, a firebox open on four sides and long dark, a hipped cap
    with a knob finial."""
    rnd = random.Random(seed)
    P = _root("lantern")
    s, sd = stone(), dstone()
    G = empty(P, "lean", (0, 0, 0), (1.5, -2, rnd.uniform(-5, 5)))
    K.box("plinth", (0.72, 0.72, 0.18), s, loc=(0, 0, 0.07), bev=0.03, parent=G)
    tapered("base", (0.56, 0.56), (0.4, 0.4), 0.14, s, loc=(0, 0, 0.16), bev=0.02, parent=G)
    tapered("shaft", (0.32, 0.32), (0.27, 0.27), 1.0, s, loc=(0, 0, 0.3), bev=0.02, parent=G)
    K.box("shaft_band", (0.33, 0.33, 0.04), sd, loc=(0, 0, 0.45), bev=0.005, parent=G)
    glyph(G, "glyph", "steps", (0, -0.15, 0.9), 0.2, sd, rot=(-1.4, 0, 0))
    tapered("collar", (0.3, 0.3), (0.6, 0.6), 0.12, s, loc=(0, 0, 1.3), bev=0.02, parent=G)
    K.box("floor", (0.62, 0.62, 0.08), s, loc=(0, 0, 1.46), bev=0.02, parent=G)
    zf = 1.5
    hb = 0.42
    for i, (x, y) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        K.box(f"corner{i}", (0.11, 0.11, hb), s, loc=(x * 0.25, y * 0.25, zf + hb / 2), bev=0.015, parent=G)
    K.box("soot_box", (0.36, 0.36, hb - 0.02), K.mat("soot", CHAR_D, rough=1.0), loc=(0, 0, zf + hb / 2), bev=0, parent=G)
    K.box("inner_floor", (0.5, 0.5, 0.02), K.mat("hole", GRAPHITE, rough=1.0), loc=(0, 0, zf + 0.005), bev=0, parent=G)
    K.box("lintel", (0.64, 0.64, 0.08), s, loc=(0, 0, zf + hb + 0.04), bev=0.02, parent=G)
    tapered("eaves", (0.66, 0.66), (0.9, 0.9), 0.06, s, loc=(0, 0, zf + hb + 0.08), bev=0.01, parent=G)
    tapered("roof", (0.9, 0.9), (0.14, 0.14), 0.32, s, loc=(0, 0, zf + hb + 0.14), bev=0.02, parent=G)
    K.cyl("neck", 0.06, 0.06, s, loc=(0, 0, zf + hb + 0.49), segs=10, bev=0.01, parent=G)
    K.sphere("finial", 0.09, s, loc=(0, 0, zf + hb + 0.58), scale=(1, 1, 1.2), parent=G)
    crack(G, "crack", (0.06, -0.15, 0.7), 0.35, rot=(0, 18, 0))
    moss(G, "moss0", (0.2, 0.15, zf + hb + 0.2), 0.12, flat=0.35)
    moss(G, "moss1", (-0.22, -0.2, zf + hb + 0.2), 0.08, flat=0.35)
    moss(P, "moss2", (0.35, 0.35, 0.0), 0.2, flat=0.4)
    rock(P, "chip", 0.08, 0.06, (-0.5, -0.4, 0.02), rnd)
    return P


PROPS = {
    "stone_arch_small": stone_arch_small,
    "stone_column_standing": stone_column_standing,
    "stone_pedestal": stone_pedestal,
    "stone_urn": stone_urn,
    "stone_brazier": stone_brazier,
    "stone_altar": stone_altar,
    "stone_tablet": stone_tablet,
    "carved_relief_wall": carved_relief_wall,
    "stone_lion": stone_lion,
    "stone_obelisk": stone_obelisk,
    "stone_tomb": stone_tomb,
    "stone_sarcophagus": stone_sarcophagus,
    "stone_fountain_ruin": stone_fountain_ruin,
    "stone_bridge_fragment": stone_bridge_fragment,
    "stone_gate_pillars": stone_gate_pillars,
    "stone_tower_stump": stone_tower_stump,
    "stone_stair_spiral_ruin": stone_stair_spiral_ruin,
    "stone_floor_tiles": stone_floor_tiles,
    "stone_dais": stone_dais,
    "stone_circle_ring": stone_circle_ring,
    "stone_face_mask": stone_face_mask,
    "stone_orb_pedestal": stone_orb_pedestal,
    "crystal_probe_cluster": crystal_probe_cluster,
    "stone_hand_fragment": stone_hand_fragment,
    "stone_lantern": stone_lantern,
}
