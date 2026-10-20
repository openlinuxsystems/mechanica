"""Props of the Old Stone ruins and of the wild between places (docs/world-generation.md, Scenes), in the families of
docs/art-direction.md.

Old Stone: whoever lived here before the colony left dressed stone, cut by time: a dry-stone wall, a ruined wall, a
flight of steps, a standing stone, a fallen column, a bench, a cairn, a trough, a sunken statue head, a boundary marker,
a well and a lone doorway, all in the rock material with moss on their top faces. Nature: a fallen log, a dead snag, a
mushroom cluster, a hollow stump, an upturned root plate and a desert termite mound.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy

import kit as K
from families import M, stone, STONE_D, BONE, GRAPHITE, ZINC, RUST
from props import _root, wood, post, BARK, HEART

MOSS = "#6f9a3a"
WATER = "#26343a"
EARTH, EARTH_D = "#6b5a48", "#4a3e32"
SNAG, ROT = "#8a8278", "#4e3e30"
CAP, STEM, GILL = "#d8ccb0", "#cbbfa4", "#9a8e76"
TERMITE = "#9a8470"


def dstone():
    """The dark stone of carved bands, glyphs and the odd darker block."""
    return K.mat("stone_d", STONE_D, rough=0.9, grime=0.3, moss=0.6, cracks=0.8, noise_scale=3.0)


def moss(P, name, loc, r, flat=0.22, stretch=1.3):
    """A cushion of moss sitting on a top face."""
    return K.sphere(name, r, K.mat("moss_pad", MOSS, rough=1.0, noise_scale=10.0), loc=loc, scale=(stretch, 1.0, flat), parent=P)


def bark(mossy=0.0):
    return K.mat(f"bark{mossy}", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5, moss=mossy)


def heart():
    return K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)


def earth():
    return K.mat("earth", EARTH, rough=1.0, grime=0.2, noise_scale=7.0, cracks=0.4)


def tapered(name, base, top, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.05, parent=None, shift=(0, 0), slope=0.0):
    """A block from its base up, narrower at the top (base and top are (width, depth)); slope tips the top face."""
    bm = bmesh.new()
    (w0, d0), (w1, d1) = base, top
    q = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    b = [bm.verts.new((x * w0 / 2, y * d0 / 2, 0)) for x, y in q]
    t = [bm.verts.new((shift[0] + x * w1 / 2, shift[1] + y * d1 / 2, h + slope * x * w1 / 2)) for x, y in q]
    bm.faces.new(list(reversed(b)))
    bm.faces.new(t)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((b[i], b[j], t[j], t[i]))
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev)
    return K.place(ob, loc, rot, parent)


def rock(P, name, r, t, loc, rnd, mat=None, rot=None):
    """A field stone: an uneven seven-sided slab, narrower on top, its centre at loc."""
    bm = bmesh.new()
    n = 7
    rr = [r * rnd.uniform(0.75, 1.15) for _ in range(n)]
    sq = rnd.uniform(0.65, 0.9)
    ring = lambda k, z: [bm.verts.new((rr[i] * k * math.cos(2 * math.pi * i / n), rr[i] * k * sq * math.sin(2 * math.pi * i / n), z)) for i in range(n)]
    b = ring(1.0, -t / 2)
    top = ring(rnd.uniform(0.7, 0.85), t / 2)
    bm.faces.new(list(reversed(b)))
    bm.faces.new(top)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((b[i], b[j], top[j], top[i]))
    ob = K.from_bm(name, bm, mat or stone())
    K.bevel(ob, min(0.05, t * 0.3), segs=2, angle=30)
    if rot is None:
        rot = (rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180))
    return K.place(ob, loc, rot, P)


def limb(P, name, base, yaw, pitch, L, r0, r1, mat, segs=8, bev=0.01):
    """A tapering round piece (branch, root, splinter) from base, pointing along yaw/pitch in degrees."""
    cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    d = (cp * math.cos(math.radians(yaw)), cp * math.sin(math.radians(yaw)), sp)
    c = tuple(base[i] + d[i] * L / 2 for i in range(3))
    return K.cyl(name, r0, L, mat, loc=c, rot=(0, 90 - pitch, yaw), segs=segs, r2=r1, bev=bev, parent=P)


def lumpy_cone(name, r, h, mat, rnd, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, rings=6, segs=10, lump=0.12, tip=0.04):
    """An organic cone (spire, mound): rings of uneven radius whose centres wander, smooth shaded."""
    bm = bmesh.new()
    ph = [rnd.uniform(0, 6.28) for _ in range(rings + 1)]
    cx = cy = 0.0
    prev = None
    first = None
    for k in range(rings + 1):
        f = k / rings
        rr = (r - tip) * (1 - f) ** 0.8 + tip
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            q = rr * (1 + lump * math.sin(2 * a + ph[k]) + lump * 0.6 * math.sin(3 * a - ph[k]))
            ring.append(bm.verts.new((cx + q * math.cos(a), cy + q * math.sin(a), h * f)))
        if prev:
            for i in range(segs):
                j = (i + 1) % segs
                bm.faces.new((prev[i], prev[j], ring[j], ring[i]))
        else:
            first = ring
        prev = ring
        cx += rnd.uniform(-0.03, 0.03) * r * 2
        cy += rnd.uniform(-0.03, 0.03) * r * 2
    bm.faces.new(prev)
    bm.faces.new(list(reversed(first)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, loc, rot, parent)


def empty(P, name, loc=(0, 0, 0), rot=(0, 0, 0)):
    e = K.link(bpy.data.objects.new(name, None))
    return K.place(e, loc, rot, P)


# ================================================================== Old Stone
def stone_wall_low(seed=21):
    """A 4 m dry-stone wall about a metre high: courses of uneven blocks, flat coping stones, moss along the top."""
    rnd = random.Random(seed)
    P = _root("drywall")
    s, sd = stone(), dstone()
    z = 0.0
    for k in range(3):
        h = rnd.uniform(0.24, 0.3)
        th = 0.62 - k * 0.06
        x = -2.0 + k * 0.04 + rnd.uniform(0, 0.15)
        while x < 1.95 - k * 0.04:
            L = min(rnd.uniform(0.35, 0.75), 2.0 - k * 0.04 - x)
            if L < 0.2:
                break
            K.box(f"c{k}_{x:.2f}", (L - 0.03, th + rnd.uniform(-0.05, 0.05), h - rnd.uniform(0.0, 0.05)), sd if rnd.random() < 0.2 else s,
                  loc=(x + L / 2, rnd.uniform(-0.03, 0.03), z + h / 2 - 0.02), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(-4, 4)),
                  bev=0.04, parent=P)
            x += L
        z += h - 0.02
    # coping: flat stones laid along the top
    x = -1.92
    k = 0
    while x < 1.85:
        L = rnd.uniform(0.4, 0.6)
        K.box(f"cope{k}", (L - 0.03, 0.52, 0.1), s, loc=(x + L / 2, 0, z + 0.04), rot=(rnd.uniform(-4, 4), rnd.uniform(-4, 4), rnd.uniform(-5, 5)),
              bev=0.035, parent=P)
        x += L
        k += 1
    for k, (x, r) in enumerate(((-1.4, 0.22), (-0.4, 0.17), (0.7, 0.25), (1.6, 0.15))):
        moss(P, f"moss{k}", (x, rnd.uniform(-0.1, 0.1), z + 0.1), r, flat=0.3)
    # one stone fallen out of the top course, a tuft at the foot
    rock(P, "fallen", 0.22, 0.2, (0.9, -0.62, 0.08), rnd)
    moss(P, "foot", (-1.2, -0.38, 0.0), 0.25, flat=0.35)
    return P


def stone_wall_ruin(seed=22):
    """A 5 m ruined wall of chiselled blocks in running courses: 2.5 m high at one end, crumbling to rubble at the other."""
    rnd = random.Random(seed)
    P = _root("ruinwall")
    s, sd = stone(), dstone()
    th, ch = 0.7, 0.5
    K.box("plinth", (5.0, th + 0.16, 0.25), s, loc=(0, 0, 0.08), bev=0.05, parent=P)
    for k in range(5):
        z = 0.2 + k * ch
        end = 2.5 - 5.0 * (k + 1) / 5.6 + rnd.uniform(-0.25, 0.25)
        x = -2.5 + (0.5 if k % 2 else 0.0) - (0.5 if k % 2 else 0)
        first = True
        while x < end - 0.15:
            L = rnd.uniform(0.85, 1.1) if not (first and k % 2) else 0.5
            first = False
            last = x + L >= end
            if last:
                L = max(0.3, end - x)
            yaw = rnd.uniform(-6, 9) if last else rnd.uniform(-1, 1)
            dy = rnd.uniform(-0.12, 0.08) if last else 0
            K.box(f"blk{k}_{x:.2f}", (L - 0.03, th - rnd.uniform(0, 0.04), ch - 0.03), sd if rnd.random() < 0.15 else s,
                  loc=(x + L / 2, dy, z + ch / 2), rot=(0, rnd.uniform(-4, 0) if last else 0, yaw), bev=0.05, parent=P)
            x += L
            if last:
                break
    # tumbled blocks and chips spilling from the low end
    for k in range(8):
        x = rnd.uniform(1.4, 3.3)
        y = rnd.uniform(-1.1, 0.9)
        big = k < 4
        sz = (rnd.uniform(0.6, 0.9), 0.55, 0.45) if big else (rnd.uniform(0.25, 0.4), rnd.uniform(0.2, 0.3), rnd.uniform(0.15, 0.25))
        K.box(f"rubble{k}", sz, s if rnd.random() < 0.8 else sd, loc=(x, y, sz[2] * 0.4),
              rot=(rnd.uniform(-15, 15), rnd.uniform(-20, 20), rnd.uniform(0, 90)), bev=0.04, parent=P)
    for k, (x, z, r) in enumerate(((-2.0, 2.7, 0.3), (-1.2, 2.2, 0.22), (0.1, 1.7, 0.25), (1.2, 1.2, 0.2))):
        moss(P, f"moss{k}", (x, rnd.uniform(-0.12, 0.12), z), r, flat=0.3)
    return P


def stone_steps(seed=23):
    """Four worn stone steps up to a small landing that leads nowhere, a broken balustrade post at its corner."""
    rnd = random.Random(seed)
    P = _root("steps")
    s, sd = stone(), dstone()
    W, D, R = 2.2, 0.42, 0.2
    for k in range(4):
        h = (k + 1) * R
        if k == 2:
            # this step split long ago; its right half has shifted
            K.box("step2a", (W * 0.6 - 0.02, D, h), s, loc=(-W * 0.2, -0.9 + k * D, h / 2), rot=(0, 0, rnd.uniform(-1, 1)), bev=0.05, parent=P)
            K.box("step2b", (W * 0.4 - 0.02, D, h - 0.03), s, loc=(W * 0.3 + 0.02, -0.92 + k * D, h / 2 - 0.015), rot=(0, 1.5, -3), bev=0.06, parent=P)
        else:
            K.box(f"step{k}", (W, D, h), s, loc=(rnd.uniform(-0.02, 0.02), -0.9 + k * D, h / 2), rot=(0, 0, rnd.uniform(-1, 1)), bev=0.05, parent=P)
    K.box("landing", (W, 0.9, 1.0), s, loc=(0, 0.99, 0.5), bev=0.05, parent=P)
    # side cheeks
    for sx in (-1, 1):
        K.box(f"cheek{sx}", (0.3, 2.6, 0.3), sd, loc=(sx * (W / 2 + 0.15), 0.15, 0.15), bev=0.05, parent=P)
    # the balustrade: a base, a post snapped at half height, its top in the grass
    K.box("bal_base", (0.4, 0.4, 0.15), s, loc=(W / 2 - 0.25, 1.2, 1.07), bev=0.03, parent=P)
    tapered("bal_post", (0.26, 0.26), (0.22, 0.22), 0.45, s, loc=(W / 2 - 0.25, 1.2, 1.14), bev=0.03, parent=P, slope=0.3)
    K.box("bal_band", (0.3, 0.3, 0.06), sd, loc=(W / 2 - 0.25, 1.2, 1.25), bev=0.015, parent=P)
    K.box("bal_top", (0.24, 0.24, 0.42), s, loc=(1.6, 0.2, 0.12), rot=(80, 0, 35), bev=0.03, parent=P)
    K.box("bal_cap", (0.34, 0.34, 0.1), s, loc=(1.75, -0.35, 0.05), rot=(0, 4, 20), bev=0.025, parent=P)
    K.box("bal_stub", (0.4, 0.4, 0.15), s, loc=(-W / 2 + 0.25, 1.2, 1.07), bev=0.03, parent=P)
    for k, (x, y, z, r) in enumerate(((-0.8, -0.71, 0.2, 0.18), (0.95, -0.27, 0.4, 0.14), (-0.3, 0.98, 1.0, 0.25), (-1.0, 0.6, 0.8, 0.15))):
        moss(P, f"moss{k}", (x, y, z), r, flat=0.25)
    rock(P, "chip", 0.12, 0.1, (-0.6, -1.4, 0.04), rnd)
    return P


def standing_stone(seed=24):
    """A single standing stone 3.5 m tall, leaning a little, two carved bands and a ring glyph near the top."""
    rnd = random.Random(seed)
    P = _root("menhir")
    s, sd = stone(), dstone()
    G = empty(P, "lean", (0, 0, -0.3), (3, -5, 0))
    H = 3.85
    w0, d0, w1, d1 = 1.05, 0.62, 0.68, 0.42
    tapered("stone", (w0, d0), (w1, d1), H, s, bev=0.1, parent=G, shift=(0.05, 0.02), slope=0.25)

    def width(z):
        f = z / H
        return w0 + (w1 - w0) * f + 0.04, d0 + (d1 - d0) * f + 0.04, 0.05 * f

    for k, (z, bh) in enumerate(((2.75, 0.12), (3.05, 0.07))):
        wa, da, sx = width(z)
        wb, db, _ = width(z + bh)
        tapered(f"band{k}", (wa, da), (wb, db), bh, sd, loc=(sx, 0.02 * z / H, z), bev=0.01, parent=G)
    # the glyph: a ring with a line through it, cut into the face
    gy = -(d0 + (d1 - d0) * 2.3 / H) / 2 - 0.005
    K.torus("glyph_ring", 0.13, 0.022, sd, loc=(0.04, gy, 2.3), rot=(90, 0, 0), segs=20, parent=G)
    K.box("glyph_line", (0.04, 0.03, 0.55), sd, loc=(0.04, gy, 2.15), bev=0, parent=G)
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        rock(P, f"pack{k}", rnd.uniform(0.15, 0.22), 0.16, (math.cos(a) * 0.65, math.sin(a) * 0.45, 0.04), rnd)
    moss(P, "moss0", (-0.45, -0.3, 0.0), 0.3, flat=0.4)
    moss(P, "moss1", (0.4, 0.3, 0.0), 0.25, flat=0.4)
    return P


def fallen_column(seed=25):
    """A toppled column: its base still on the plinth, three drums lying in a line, the capital at the end."""
    rnd = random.Random(seed)
    P = _root("column")
    s, sd = stone(), dstone()
    r, L = 0.45, 0.95
    x0 = -2.7
    K.box("plinth", (1.25, 1.25, 0.35), s, loc=(x0, 0, 0.15), bev=0.06, parent=P)
    K.cyl("torus_base", r + 0.08, 0.14, s, loc=(x0, 0, 0.39), segs=20, bev=0.04, parent=P)
    # the bottom drum still stands, its top broken off at a slant
    K.cyl("stub", r, 0.5, s, loc=(x0, 0, 0.66), rot=(0, 9, 0), segs=20, bev=0.03, parent=P)
    x = x0 + 0.95
    for k in range(3):
        yaw = rnd.uniform(-9, 9)
        y = rnd.uniform(-0.15, 0.15)
        K.cyl(f"drum{k}", r, L, s, loc=(x + L / 2, y, r - 0.04), rot=(0, 90, yaw), segs=20, bev=0.04, parent=P)
        K.torus(f"ring{k}", r - 0.01, 0.025, sd, loc=(x + 0.12, y, r - 0.04), rot=(0, 90, yaw), segs=24, parent=P)
        x += L + rnd.uniform(0.08, 0.25)
    # the capital lies tipped on its side: echinus and the square abacus
    cap = empty(P, "capital", (x + 0.45, 0.25, 0.6), (rnd.uniform(-6, 6), 62, 18))
    K.cyl("echinus", r, 0.35, s, loc=(0, 0, -0.2), segs=20, r2=r + 0.17, bev=0.03, parent=cap)
    K.box("abacus", (1.3, 1.3, 0.28), s, loc=(0, 0, 0.12), bev=0.05, parent=cap)
    K.box("abacus_band", (1.32, 1.32, 0.05), sd, loc=(0, 0, 0.06), bev=0.01, parent=cap)
    rock(P, "chip0", 0.18, 0.12, (0.2, -0.75, 0.05), rnd)
    rock(P, "chip1", 0.12, 0.1, (-1.5, 0.7, 0.04), rnd)
    moss(P, "moss0", (x0, 0, 0.82), 0.22, flat=0.25)
    moss(P, "moss1", (-0.5, 0.0, 0.88), 0.2, flat=0.3)
    moss(P, "moss2", (x0 - 0.4, -0.55, 0.0), 0.3, flat=0.4)
    return P


def stone_bench(seed=26):
    """A long carved stone bench: a seat slab cracked through, on two carved blocks."""
    rnd = random.Random(seed)
    P = _root("bench")
    s, sd = stone(), dstone()
    for k, x in enumerate((-0.8, 0.8)):
        K.box(f"leg{k}", (0.42, 0.48, 0.44), s, loc=(x, 0, 0.2), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.05, parent=P)
        K.box(f"panel{k}", (0.26, 0.02, 0.22), sd, loc=(x, -0.243, 0.22), bev=0, parent=P)
    # the seat broke over the right leg; the short piece has dropped a little
    K.box("seat_l", (1.38, 0.56, 0.14), s, loc=(-0.44, 0, 0.49), rot=(0, -0.6, 0), bev=0.04, parent=P)
    K.box("seat_r", (0.95, 0.56, 0.14), s, loc=(0.74, 0.02, 0.475), rot=(1, 3.0, -2), bev=0.04, parent=P)
    for k, (x, Lb) in enumerate(((-0.44, 1.3), (0.74, 0.88))):
        K.box(f"groove{k}", (Lb, 0.02, 0.03), sd, loc=(x, -0.283, 0.47 - k * 0.015), rot=(0, (-0.6, 3.0)[k], 0), bev=0, parent=P)
    rock(P, "crumb", 0.08, 0.06, (0.35, -0.42, 0.03), rnd)
    moss(P, "moss0", (-0.95, 0.12, 0.56), 0.15, flat=0.3)
    moss(P, "moss1", (0.8, 0.25, 0.0), 0.22, flat=0.4)
    return P


def cairn(seed=27):
    """A cone of stacked field stones about 1.5 m high, a flat stone laid on top."""
    rnd = random.Random(seed)
    P = _root("cairn")
    s, sd = stone(), dstone()
    z = 0.0
    k = 0
    while z < 1.3:
        R = 0.78 * (1 - z / 1.65)
        t = rnd.uniform(0.15, 0.2)
        ring = R * 0.62
        n = max(1, int(2 * math.pi * ring / 0.42)) if ring > 0.12 else 0
        rs = min(0.26, R * 0.45 + 0.06)
        a0 = rnd.uniform(0, 6.28)
        for i in range(n):
            a = a0 + 2 * math.pi * i / n
            rock(P, f"r{k}_{i}", rs, t, (math.cos(a) * ring, math.sin(a) * ring, z + t / 2), rnd, mat=sd if rnd.random() < 0.2 else s,
                 rot=(rnd.uniform(-6, 6), rnd.uniform(-10, 10) - 6, rnd.uniform(0, 180)))
        rock(P, f"core{k}", max(0.14, R * 0.55), t, (0, 0, z + t / 2), rnd)
        z += t * 0.85
        k += 1
    K.box("top", (0.5, 0.36, 0.08), s, loc=(0.02, 0, z + 0.03), rot=(2, -3, 25), bev=0.03, parent=P)
    moss(P, "moss0", (-0.55, 0.3, 0.05), 0.22, flat=0.4)
    moss(P, "moss1", (0.2, 0.62, 0.05), 0.18, flat=0.4)
    return P


def stone_trough(seed=28):
    """A long trough carved from one stone, on two footing stones, rainwater standing in it and moss on the rim."""
    rnd = random.Random(seed)
    P = _root("strough")
    s, sd = stone(), dstone()
    L, W, H, t = 2.2, 0.8, 0.5, 0.13
    z0 = 0.12
    for k, x in enumerate((-0.7, 0.7)):
        K.box(f"foot{k}", (0.45, 0.95, z0 + 0.02), sd, loc=(x, 0, z0 / 2), rot=(0, 0, rnd.uniform(-3, 3)), bev=0.03, parent=P)
    K.box("bottom", (L, W, 0.16), s, loc=(0, 0, z0 + 0.08), bev=0.04, parent=P)
    for sy in (-1, 1):
        K.box(f"side{sy}", (L, t, H), s, loc=(0, sy * (W - t) / 2, z0 + H / 2), bev=0.04, parent=P)
    K.box("end_l", (t, W - 2 * t + 0.02, H), s, loc=(-(L - t) / 2, 0, z0 + H / 2), bev=0.03, parent=P)
    # the right end has a chunk broken from its top; the piece lies at the foot
    K.box("end_r", (t, W - 2 * t + 0.02, H - 0.14), s, loc=((L - t) / 2, 0, z0 + (H - 0.14) / 2), bev=0.03, parent=P)
    K.box("chunk", (0.18, 0.32, 0.13), s, loc=(1.35, -0.55, 0.06), rot=(8, 4, 30), bev=0.03, parent=P)
    K.box("lip", (L * 0.9, 0.02, 0.03), sd, loc=(0, -W / 2 - 0.005, z0 + H - 0.08), bev=0, parent=P)
    K.box("water", (L - 2 * t, W - 2 * t, 0.02), K.mat("stone_water", WATER, rough=0.05), loc=(0, 0, z0 + H - 0.2), bev=0, parent=P)
    moss(P, "moss0", (-0.9, 0.33, z0 + H), 0.14, flat=0.3)
    moss(P, "moss1", (0.4, -0.34, z0 + H), 0.12, flat=0.3, stretch=2.0)
    moss(P, "moss2", (-1.1, -0.35, 0.0), 0.22, flat=0.4)
    return P


def statue_head(seed=29):
    """The head of a huge broken statue, sunk to the chin in the ground and tipped back: brow, nose, closed eyes, lips."""
    rnd = random.Random(seed)
    P = _root("statuehead")
    s, sd = stone(), dstone()
    G = empty(P, "head", (0, 0.1, 1.0), (-14, 6, 28))
    hd = 1.95
    fy = -hd / 2 + 0.04
    # cranium wider than the jaw, the face plane a little forward of it
    tapered("skull", (1.6, 1.75), (2.0, hd), 2.5, s, loc=(0, 0.05, -1.3), bev=0.3, parent=G)
    K.box("crown", (1.7, 1.65, 0.3), s, loc=(0, 0.1, 1.22), bev=0.12, parent=G)
    K.box("face", (1.4, 0.1, 1.9), s, loc=(0, fy + 0.02, -0.25), bev=0.05, parent=G)
    K.box("band", (2.06, 2.04, 0.3), s, loc=(0, 0.04, 0.9), bev=0.06, parent=G)
    K.box("band_line", (1.7, 0.02, 0.05), sd, loc=(0, -hd / 2 - 0.035, 0.9), bev=0, parent=G)
    tapered("brow", (1.5, 0.24), (1.45, 0.12), 0.2, s, loc=(0, fy - 0.1, 0.3), bev=0.04, parent=G, shift=(0, 0.06))
    # the nose: a wedge from the brow down, its tip standing well proud of the face
    bm = bmesh.new()
    top = bm.verts.new((0, fy - 0.08, 0.32))
    l, r = bm.verts.new((-0.22, fy - 0.02, -0.5)), bm.verts.new((0.22, fy - 0.02, -0.5))
    tip = bm.verts.new((0, fy - 0.38, -0.46))
    for f in ((top, l, tip), (top, tip, r), (l, r, tip), (top, r, l)):
        bm.faces.new(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    nose = K.from_bm("nose", bm, s)
    K.bevel(nose, 0.03, segs=2, angle=30)
    K.place(nose, parent=G)
    for sx in (-1, 1):
        # closed eyes: a heavy upper lid over a cut line
        K.box(f"lid{sx}", (0.46, 0.12, 0.14), s, loc=(sx * 0.4, fy - 0.06, 0.12), rot=(0, sx * -6, 0), bev=0.05, parent=G)
        K.box(f"lash{sx}", (0.42, 0.03, 0.03), sd, loc=(sx * 0.4, fy - 0.12, 0.05), rot=(0, sx * -6, 0), bev=0, parent=G)
        K.box(f"ear{sx}", (0.14, 0.4, 0.75), s, loc=(sx * 0.93, -0.05, -0.15), bev=0.05, parent=G)
    K.box("lip_u", (0.6, 0.12, 0.11), s, loc=(0, fy - 0.06, -0.72), bev=0.04, parent=G)
    K.box("mouth", (0.54, 0.03, 0.025), sd, loc=(0, fy - 0.12, -0.79), bev=0, parent=G)
    K.box("lip_l", (0.48, 0.1, 0.1), s, loc=(0, fy - 0.05, -0.86), bev=0.04, parent=G)
    # weather: cracks down the face, a chunk broken off the crown lying by the head
    K.box("crack", (0.035, 0.02, 0.9), sd, loc=(0.5, fy - 0.04, 0.5), rot=(0, 18, 0), bev=0, parent=G)
    K.box("crack2", (0.03, 0.02, 0.5), sd, loc=(0.62, fy - 0.04, -0.15), rot=(0, -10, 0), bev=0, parent=G)
    K.box("crown_chunk", (0.8, 0.6, 0.45), s, loc=(1.65, 0.6, 0.18), rot=(10, -14, 35), bev=0.06, parent=P)
    rock(P, "chip", 0.18, 0.12, (-1.4, -0.4, 0.05), rnd)
    # the ground heaped round its neck
    K.sphere("heap", 1.0, earth(), loc=(0, 0.25, 0.0), scale=(1.5, 1.45, 0.3), parent=P)
    moss(P, "moss0", (-1.0, -0.7, 0.1), 0.3, flat=0.4)
    moss(P, "moss1", (0.9, 1.0, 0.12), 0.35, flat=0.35)
    return P


def boundary_stone(seed=30):
    """A small squared marker stone a metre high with a carved glyph on its face."""
    rnd = random.Random(seed)
    P = _root("marker")
    s, sd = stone(), dstone()
    G = empty(P, "lean", (0, 0, -0.12), (4, 2, rnd.uniform(-5, 5)))
    tapered("post", (0.44, 0.34), (0.38, 0.3), 0.98, s, bev=0.05, parent=G)
    tapered("cap", (0.38, 0.3), (0.16, 0.1), 0.14, s, loc=(0, 0, 0.98), bev=0.02, parent=G)
    fy = -0.155
    K.torus("glyph_ring", 0.07, 0.012, sd, loc=(0, fy - 0.004, 0.86), rot=(90, 0, 0), segs=16, parent=G)
    K.box("glyph_stem", (0.022, 0.02, 0.3), sd, loc=(0, fy - 0.004, 0.62), bev=0, parent=G)
    for sx in (-1, 1):
        K.box(f"glyph_arm{sx}", (0.022, 0.02, 0.14), sd, loc=(sx * 0.05, fy - 0.004, 0.52), rot=(0, sx * 40, 0), bev=0, parent=G)
    moss(P, "moss0", (0.15, 0.1, 1.0), 0.09, flat=0.3)
    moss(P, "moss1", (-0.2, 0.1, 0.0), 0.18, flat=0.4)
    rock(P, "pack", 0.1, 0.08, (0.28, -0.18, 0.03), rnd)
    return P


def stone_well(seed=31):
    """An old round well of stone blocks, 2 m across, its timber windlass broken: one post snapped, the axle fallen."""
    rnd = random.Random(seed)
    P = _root("well")
    s, sd = stone(), dstone()
    n, Rm = 12, 0.85
    for c in range(2):
        for i in range(n):
            a = 2 * math.pi * (i + 0.5 * c) / n
            K.box(f"blk{c}_{i}", (0.43, 0.3, 0.37), sd if rnd.random() < 0.15 else s,
                  loc=(Rm * math.cos(a), Rm * math.sin(a), 0.185 + c * 0.38), rot=(0, 0, math.degrees(a) + 90 + rnd.uniform(-2, 2)), bev=0.04, parent=P)
    for i in range(n):
        if i == 4:
            continue
        a = 2 * math.pi * i / n
        K.box(f"cope{i}", (0.46, 0.38, 0.1), s, loc=(Rm * math.cos(a), Rm * math.sin(a), 0.81), rot=(rnd.uniform(-2, 2), 0, math.degrees(a) + 90),
              bev=0.03, parent=P)
    K.box("cope_down", (0.46, 0.38, 0.1), s, loc=(-0.6, -1.4, 0.05), rot=(6, 0, 70), bev=0.03, parent=P)
    K.disc("dark", 0.72, K.mat("well_dark", GRAPHITE, rough=1.0), (0, 0), z=0.55, segs=24).parent = P
    K.disc("water", 0.72, K.mat("stone_water", WATER, rough=0.05), (0, 0), z=0.3, segs=24).parent = P
    # the windlass: the left post stands, the right has snapped and the axle dropped onto the rim
    post(P, "post_l", (-1.15, 0), 1.75, r=0.08)
    post(P, "post_r", (1.15, 0), 0.95, r=0.08)
    limb(P, "splinter0", (1.13, 0.02, 0.8), 0, 80, 0.3, 0.05, 0.005, wood(3), segs=4, bev=0)
    limb(P, "splinter1", (1.18, -0.03, 0.8), 180, 75, 0.2, 0.04, 0.005, wood(3), segs=4, bev=0)
    a = math.degrees(math.atan2(1.55 - 0.92, 2.3))
    K.cyl("axle", 0.08, 2.35, wood(1), loc=(-0.02, 0.0, (1.58 + 0.92) / 2), rot=(0, 90 + a, 0), segs=12, bev=0.01, parent=P)
    K.torus("rope", 0.1, 0.03, K.mat("rope", "#8c7a5c", rough=0.95), loc=(-0.3, 0, 1.17), rot=(0, 90 - a, 0), segs=14, parent=P)
    K.box("crank_a", (0.05, 0.05, 0.4), M(RUST, metal=0.5, wear=0.6), loc=(-1.3, 0, 1.42), bev=0.005, parent=P)
    K.box("crank_b", (0.05, 0.25, 0.05), M(RUST, metal=0.5, wear=0.6), loc=(-1.3, -0.12, 1.24), bev=0.005, parent=P)
    K.cyl("bucket", 0.17, 0.3, wood(2), loc=(1.4, -0.75, 0.15), rot=(80, 0, 30), segs=12, r2=0.2, bev=0.01, parent=P)
    K.torus("bucket_hoop", 0.19, 0.012, M(ZINC, metal=0.5, wear=0.6), loc=(1.4, -0.75, 0.15), rot=(80, 0, 30), segs=16, parent=P)
    moss(P, "moss0", (0.7, 0.45, 0.87), 0.14, flat=0.3)
    moss(P, "moss1", (-0.9, -0.85, 0.0), 0.25, flat=0.4)
    return P


def stone_doorway(seed=32):
    """A stone door frame standing alone, about 3 m tall: two jambs of stacked blocks under a lintel, nothing around it."""
    rnd = random.Random(seed)
    P = _root("doorway")
    s, sd = stone(), dstone()
    K.box("sill", (2.5, 1.0, 0.14), s, loc=(0, 0, 0.05), bev=0.04, parent=P)
    top = 0
    for sx in (-1, 1):
        z = 0.12
        for k in range(4):
            h = rnd.uniform(0.55, 0.65)
            K.box(f"jamb{sx}{k}", (0.6, 0.72, h - 0.02), sd if rnd.random() < 0.15 else s,
                  loc=(sx * 0.9 + rnd.uniform(-0.02, 0.02), 0, z + h / 2), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.05, parent=P)
            z += h
        top = max(top, z)
    K.box("lintel", (2.6, 0.82, 0.55), s, loc=(0, 0, top + 0.27), rot=(0, 0.8, 0), bev=0.06, parent=P)
    K.box("lintel_band", (2.2, 0.02, 0.07), sd, loc=(0, -0.42, top + 0.3), rot=(0, 0.8, 0), bev=0, parent=P)
    K.box("lintel_crack", (0.035, 0.02, 0.5), sd, loc=(0.25, -0.42, top + 0.25), rot=(0, 12, 0), bev=0, parent=P)
    moss(P, "moss0", (-0.8, 0.1, top + 0.56), 0.25, flat=0.3)
    moss(P, "moss1", (0.9, -0.1, top + 0.56), 0.18, flat=0.3)
    moss(P, "moss2", (-1.25, -0.4, 0.0), 0.25, flat=0.4)
    rock(P, "chip", 0.16, 0.12, (1.4, -0.7, 0.05), rnd)
    return P


# ================================================================== Nature
def fallen_log(seed=33):
    """A 6 m trunk lying in the grass: bark gone mossy on top, a few snapped branch stubs, the broken end splintered."""
    rnd = random.Random(seed)
    P = _root("log")
    b = bark(0.8)
    G = empty(P, "trunk", (0, 0, 0), (0, 0, 4))
    K.cyl("trunk", 0.38, 6.0, b, loc=(0, 0, 0.34), rot=(0, 90, 0), segs=16, r2=0.3, bev=0.02, parent=G)
    K.cyl("old_end", 0.33, 0.02, K.mat("heart_old", SNAG, rough=0.95, cracks=0.6), loc=(-3.005, 0, 0.34), rot=(0, 90, 0), segs=16, bev=0, parent=G)
    K.cyl("break", 0.26, 0.03, heart(), loc=(3.0, 0, 0.34), rot=(0, 90, 0), segs=16, bev=0, parent=G)
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.2, 0.2)
        rr = rnd.uniform(0.06, 0.2)
        limb(G, f"splinter{k}", (3.0, math.cos(a) * rr, 0.34 + math.sin(a) * rr), rnd.uniform(-12, 12), rnd.uniform(-10, 14),
             rnd.uniform(0.25, 0.6), rnd.uniform(0.05, 0.09), 0.005, heart(), segs=4, bev=0)
    for k, (x, yaw, pitch, L) in enumerate(((-1.6, 80, 30, 0.5), (0.3, -70, 20, 0.4), (1.5, 100, 50, 0.6), (2.3, -100, 10, 0.3))):
        y = 0.25 * math.copysign(1, math.sin(math.radians(yaw)))
        st = limb(G, f"stub{k}", (x, y * 0.9, 0.4), yaw, pitch, L, 0.085, 0.06, b, segs=8)
        cp = math.cos(math.radians(pitch))
        tip = (x + cp * math.cos(math.radians(yaw)) * L, y * 0.9 + cp * math.sin(math.radians(yaw)) * L, 0.4 + math.sin(math.radians(pitch)) * L)
        K.cyl(f"stub_end{k}", 0.058, 0.02, heart(), loc=tip, rot=(0, 90 - pitch, yaw), segs=8, bev=0, parent=G)
    for k, (x, r) in enumerate(((-2.2, 0.28), (-0.8, 0.22), (0.9, 0.2))):
        moss(G, f"moss{k}", (x, rnd.uniform(-0.06, 0.06), 0.68), r, flat=0.25, stretch=1.6)
    moss(P, "moss_foot", (-2.6, 0.45, 0.0), 0.3, flat=0.4)
    return P


def dead_snag(seed=34):
    """A dead tree still standing, about 6 m: bark sloughing off to grey wood, the top snapped, two bare branches."""
    rnd = random.Random(seed)
    P = _root("snag")
    b = bark(0.3)
    grey = K.mat("snag", SNAG, rough=0.9, grime=0.2, noise_scale=6.0, cracks=0.6)
    K.cyl("lower", 0.34, 2.6, b, loc=(0, 0, 1.25), segs=14, r2=0.27, bev=0.02, parent=P)
    up = empty(P, "upper", (0, 0, 2.5), (3, -4, 0))
    K.cyl("upper", 0.27, 3.3, grey, loc=(0, 0, 1.6), segs=14, r2=0.17, bev=0.02, parent=up)
    for k in range(3):
        a = rnd.uniform(0, 360)
        K.box(f"bark_strip{k}", (0.03, 0.22, rnd.uniform(0.4, 0.8)), b, loc=(0.26 * math.cos(math.radians(a)), 0.26 * math.sin(math.radians(a)), rnd.uniform(0.6, 1.8)),
              rot=(0, 0, a), bev=0.01, parent=up)
    for k in range(7):
        a = 2 * math.pi * k / 7 + rnd.uniform(-0.3, 0.3)
        rr = rnd.uniform(0.03, 0.12)
        limb(up, f"splinter{k}", (math.cos(a) * rr, math.sin(a) * rr, 3.2), rnd.uniform(0, 360), rnd.uniform(70, 88),
             rnd.uniform(0.25, 0.75), rnd.uniform(0.05, 0.08), 0.005, grey, segs=4, bev=0)
    K.cyl("break", 0.17, 0.02, K.mat("heart_old", SNAG, rough=0.95, cracks=0.6), loc=(0, 0, 3.24), segs=14, bev=0, parent=up)
    # two bare branches, each with a twig, and a snapped stub
    for k, (z, yaw, pitch, L) in enumerate(((1.2, 30, 40, 1.7), (2.2, 200, 52, 1.3))):
        limb(up, f"branch{k}", (0, 0, z), yaw, pitch, L, 0.09, 0.025, grey, segs=8)
        f = 0.55
        cp = math.cos(math.radians(pitch))
        mid = (cp * math.cos(math.radians(yaw)) * L * f, cp * math.sin(math.radians(yaw)) * L * f, z + math.sin(math.radians(pitch)) * L * f)
        limb(up, f"twig{k}", mid, yaw + 50, pitch + 20, 0.6, 0.04, 0.012, grey, segs=6)
    limb(P, "stub", (0, 0, 1.9), 290, 15, 0.4, 0.1, 0.08, b, segs=8)
    K.cyl("hole", 0.07, 0.02, K.mat("hole", "#1c1a17", rough=1.0), loc=(0.05, -0.31, 1.7), rot=(90, 0, 8), segs=10, bev=0, parent=P)
    for k in range(4):
        a = 2 * math.pi * k / 4 + rnd.uniform(-0.3, 0.3)
        K.cyl(f"root{k}", 0.13, 0.75, b, loc=(math.cos(a) * 0.42, math.sin(a) * 0.42, 0.08), rot=(0, 75, math.degrees(a)), segs=8, r2=0.04, bev=0.01, parent=P)
    return P


def mushroom_cluster(seed=35):
    """A clump of big pale mushrooms, 0.3 to 0.8 m tall, on a mossy mound."""
    rnd = random.Random(seed)
    P = _root("mushrooms")
    mm = K.mat("moss_mound", MOSS, rough=1.0, noise_scale=8.0, grime=0.3)
    K.sphere("mound", 0.55, mm, loc=(0, 0, 0), scale=(1.5, 1.25, 0.32), parent=P)
    K.sphere("mound2", 0.4, mm, loc=(0.55, 0.3, 0), scale=(1.3, 1.0, 0.3), parent=P)
    K.sphere("mound3", 0.35, mm, loc=(-0.6, -0.25, 0), scale=(1.2, 1.1, 0.28), parent=P)
    limb(P, "twig", (-0.75, 0.45, 0.04), -20, 3, 0.9, 0.035, 0.02, bark(), segs=6)
    rock(P, "stone", 0.12, 0.1, (0.75, -0.45, 0.03), rnd)
    cap_m = K.mat("cap", CAP, rough=0.75, grime=0.35, noise_scale=4.0, cracks=0.15)
    stem_m = K.mat("stem", STEM, rough=0.85, grime=0.35, noise_scale=9.0)
    gill_m = K.mat("gill", GILL, rough=0.9, noise_scale=20.0)
    for k, (x, y, h, cr) in enumerate(((0.0, 0.05, 0.8, 0.3), (0.38, 0.18, 0.56, 0.23), (-0.34, 0.22, 0.45, 0.2), (0.18, -0.3, 0.32, 0.14),
                                       (-0.5, -0.18, 0.36, 0.16), (0.6, -0.12, 0.24, 0.1))):
        m = empty(P, f"m{k}", (x, y, 0.1 if abs(x) < 0.4 else 0.04), (rnd.uniform(-10, 10) + y * 20, -x * 20 + rnd.uniform(-5, 5), 0))
        K.cyl(f"stem{k}", cr * 0.26, h, stem_m, loc=(0, 0, h / 2 - 0.05), segs=10, r2=cr * 0.2, bev=0.01, parent=m)
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=cr)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
        cph = rnd.uniform(0, 6.28)
        for v in bm.verts:
            v.co.z *= 0.5
            w = 1 + 0.07 * math.sin(3 * math.atan2(v.co.y, v.co.x) + cph)
            v.co.x *= w
            v.co.y *= w
        cap = K.place(K.from_bm(f"cap{k}", bm, cap_m, smooth=True), (0, 0, h - 0.05 - cr * 0.12), (0, 0, 0), m)
        K.cyl(f"gills{k}", cr * 0.97, 0.03, gill_m, loc=(0, 0, h - 0.05 - cr * 0.12), segs=12, r2=cr * 0.3, bev=0, parent=m)
        K.torus(f"ring{k}", cr * 0.26, 0.012, stem_m, loc=(0, 0, h * 0.62), segs=12, parent=m)
    for k in range(3):
        a = rnd.uniform(0, 6.28)
        K.sphere(f"button{k}", 0.05, cap_m, loc=(math.cos(a) * 0.55, math.sin(a) * 0.45, 0.1), scale=(1, 1, 0.7), parent=P)
    return P


def stump_hollow(seed=36):
    """A big hollow stump 1.5 m across, rotted open on one side, bracket fungus on its bark."""
    rnd = random.Random(seed)
    P = _root("hollowstump")
    b = bark(0.4)
    rot_m = K.mat("rot", ROT, rough=1.0, noise_scale=12.0, cracks=0.5)
    bm = bmesh.new()
    n = 22
    a0, a1 = math.radians(-50), math.radians(230)
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    rings = []
    for i in range(n + 1):
        f = i / n
        a = a0 + (a1 - a0) * f
        ro = 0.75 * (1 + 0.05 * math.sin(3 * a + ph[0]) + 0.03 * math.sin(7 * a + ph[1]))
        ri = ro - 0.13
        edge = min(f, 1 - f)
        h = 0.95 + 0.25 * math.sin(2 * a + ph[2]) + rnd.uniform(-0.12, 0.12)
        h = min(h, 0.35 + edge * 4.0)
        ob_, ot = bm.verts.new((ro * 1.12 * math.cos(a), ro * 1.12 * math.sin(a), -0.05)), bm.verts.new((ro * math.cos(a), ro * math.sin(a), h))
        ib, it = bm.verts.new((ri * math.cos(a), ri * math.sin(a), -0.05)), bm.verts.new((ri * math.cos(a), ri * math.sin(a), h - rnd.uniform(0.02, 0.1)))
        rings.append((ob_, ot, it, ib))
    for i in range(n):
        A, B = rings[i], rings[i + 1]
        fo = bm.faces.new((A[0], B[0], B[1], A[1]))
        fo.smooth = True
        ft = bm.faces.new((A[1], B[1], B[2], A[2]))
        ft.material_index = 1
        fi = bm.faces.new((A[2], B[2], B[3], A[3]))
        fi.material_index = 1
        fi.smooth = True
    for R_ in (rings[0], rings[-1]):
        f = bm.faces.new(R_)
        f.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm("shell", bm, b)
    ob.data.materials.append(rot_m)
    K.place(ob, (0, 0, 0), (0, 0, 0), P)
    K.disc("floor", 0.62, rot_m, (0, 0), z=0.12, segs=20, wobble=0.1).parent = P
    for k in range(4):
        rock(P, f"debris{k}", rnd.uniform(0.06, 0.1), 0.05, (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.2), 0.15), rnd, mat=rot_m)
    for k in range(5):
        a = math.radians(-50) + k * math.radians(280) / 4 + rnd.uniform(-0.15, 0.15)
        K.cyl(f"root{k}", 0.17, 0.9, b, loc=(math.cos(a) * 0.82, math.sin(a) * 0.82, 0.07), rot=(0, 76, math.degrees(a)), segs=8, r2=0.05, bev=0.01, parent=P)
    fung = K.mat("bracket", CAP, rough=0.8, grime=0.3, noise_scale=6.0)
    for k, (a, z, r) in enumerate(((60, 0.55, 0.15), (75, 0.4, 0.11), (170, 0.7, 0.13))):
        ar = math.radians(a)
        K.cyl(f"fungus{k}", r, 0.05, fung, loc=(math.cos(ar) * 0.8, math.sin(ar) * 0.8, z), rot=(0, 0, a), segs=12, r2=r * 0.85, bev=0.01, parent=P)
    moss(P, "moss0", (-0.55, 0.45, 1.0), 0.13, flat=0.35)
    moss(P, "moss1", (0.9, -0.5, 0.0), 0.25, flat=0.4)
    return P


def root_tangle(seed=37):
    """The root plate of a fallen tree, about 3 m across, stood on edge with earth clinging, a hollow where it tore out."""
    rnd = random.Random(seed)
    P = _root("rootplate")
    b = bark(0.2)
    soil = earth()
    G = empty(P, "plate", (0, 0, 1.0), (-10, 0, 0))
    bm = bmesh.new()
    n = 20
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    front, back = [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        r = 1.4 * (1 + 0.12 * math.sin(3 * a + ph[0]) + 0.07 * math.sin(5 * a + ph[1])) + rnd.uniform(-0.06, 0.06)
        x, z = r * math.cos(a), r * math.sin(a) * 0.85
        front.append(bm.verts.new((x, -0.28 + rnd.uniform(-0.08, 0.08), z)))
        back.append(bm.verts.new((x * 0.85, 0.22, z * 0.85)))
    mid = [bm.verts.new((v.co.x * 0.55, rnd.uniform(-0.55, -0.35), v.co.z * 0.55)) for v in front]
    cf = bm.verts.new((0, -0.5, 0))
    cb = bm.verts.new((0, 0.3, 0))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((cf, mid[j], mid[i]))
        bm.faces.new((mid[i], mid[j], front[j], front[i]))
        bm.faces.new((cb, back[i], back[j]))
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    pl = K.from_bm("plate", bm, soil)
    K.bevel(pl, 0.06, segs=2, angle=30)
    K.place(pl, (0, 0, 0), (0, 0, 0), G)

    def root(name, base, d, L, r0, r1):
        dx, dy, dz = d
        m = math.sqrt(dx * dx + dy * dy + dz * dz)
        return limb(G, name, base, math.degrees(math.atan2(dy, dx)), math.degrees(math.asin(dz / m)), L, r0, r1, b, segs=7, bev=0)

    def seg(name, p0, p1, r0, r1):
        # each piece starts a little back inside the one before, so the bends stay closed
        d = [p1[i] - p0[i] for i in range(3)]
        L = math.sqrt(sum(c * c for c in d))
        back = [p0[i] - d[i] / L * r0 * 0.8 for i in range(3)]
        return root(name, back, d, L + r0 * 0.8, r0, r1)

    # the big roots run out over the torn face to the rim and bend back round it towards the tree
    angs = sorted(rnd.uniform(0, 2 * math.pi) for _ in range(9))
    for k, a in enumerate(angs):
        ca, sa = math.cos(a), math.sin(a) * 0.85
        r0 = rnd.uniform(0.75, 1.1)
        rim = 1.4 * rnd.uniform(1.0, 1.15)
        pts = [(ca * r0, -0.42, sa * r0), (ca * rim, -0.38, sa * rim), (ca * (rim + 0.25), 0.1, sa * (rim + 0.25)),
               (ca * (rim + 0.3), rnd.uniform(0.6, 0.9), sa * (rim + 0.3))]
        rad = [rnd.uniform(0.11, 0.14), 0.09, 0.065, 0.045]
        for q in range(3 if k % 3 else 2):
            seg(f"root{k}_{q}", pts[q], pts[q + 1], rad[q], rad[q + 1])
    # smaller roots lying across the face, and snapped ends standing out of it
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.3, 0.8)
        base = (math.cos(a) * r, -0.5, math.sin(a) * r * 0.8)
        t = a + rnd.uniform(1.2, 1.9)
        root(f"face{k}", base, (math.cos(t), 0.0, math.sin(t)), rnd.uniform(0.5, 0.8), 0.065, 0.045)
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.2, 0.9)
        root(f"stub{k}", (math.cos(a) * r, -0.38, math.sin(a) * r * 0.8), (rnd.uniform(-0.4, 0.4), -1, rnd.uniform(-0.4, 0.2)), rnd.uniform(0.15, 0.25), 0.05, 0.045)
    for k in range(7):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.1, 0.9)
        rock(G, f"clod{k}", rnd.uniform(0.22, 0.34), 0.2, (math.cos(a) * r, -0.45, math.sin(a) * r * 0.8), rnd, mat=soil, rot=(90, rnd.uniform(0, 180), 0))
    # the trunk going back from the plate, broken off short
    tr = empty(P, "trunk", (0, 0.3, 0.75), (0, 0, 0))
    limb(tr, "trunk", (0, 0, 0), 90, -14, 2.0, 0.42, 0.36, b, segs=14, bev=0.02)
    tip = (0, 2.0 * math.cos(math.radians(14)), -2.0 * math.sin(math.radians(14)))
    K.cyl("trunk_end", 0.35, 0.03, heart(), loc=tip, rot=(0, 90 + 14, 90), segs=14, bev=0, parent=tr)
    K.disc("pit", 1.1, K.mat("earth_d", EARTH_D, rough=1.0, noise_scale=6.0), (0, -1.0), z=0.012, segs=24, wobble=0.15, seed=seed).parent = P
    for k in range(3):
        K.sphere(f"spoil{k}", rnd.uniform(0.2, 0.3), soil, loc=(rnd.uniform(-1.2, 1.2), -1.0 + rnd.uniform(-0.6, 0.6), 0.0), scale=(1.3, 1.0, 0.45), parent=P)
    return P


def termite_mound(seed=38):
    """A desert termite mound about 2 m tall: lumpy spires of baked earth fused round a main chimney."""
    rnd = random.Random(seed)
    P = _root("termites")
    mud = K.mat("termite", TERMITE, rough=1.0, grime=0.25, noise_scale=3.0, cracks=0.15)

    def spire(name, base, h, r, tilt, n=4):
        return lumpy_cone(name, r, h, mud, rnd, loc=base, rot=tilt, parent=P, rings=n + 2, segs=11, tip=0.05)

    K.sphere("skirt", 1.0, mud, loc=(0, 0, 0), scale=(1.15, 1.0, 0.22), parent=P)
    spire("main", (0, 0, 0), 2.05, 0.62, (2, -3, 0), n=5)
    for k, (x, y, h, r) in enumerate(((0.42, 0.25, 1.35, 0.34), (-0.4, 0.22, 1.05, 0.3), (0.12, -0.42, 0.75, 0.28), (-0.32, -0.3, 1.55, 0.27),
                                       (0.55, -0.2, 0.55, 0.22))):
        spire(f"spire{k}", (x, y, 0.05), h, r, (y * 20 + rnd.uniform(-4, 4), -x * 20 + rnd.uniform(-4, 4), 0), n=3)
    for k in range(3):
        rock(P, f"clod{k}", rnd.uniform(0.08, 0.14), 0.08, (rnd.uniform(-1.3, 1.3), rnd.uniform(-1.2, -0.8), 0.03), rnd, mat=mud)
    return P


PROPS = {
    "stone_wall_low": stone_wall_low,
    "stone_wall_ruin": stone_wall_ruin,
    "stone_steps": stone_steps,
    "standing_stone": standing_stone,
    "fallen_column": fallen_column,
    "stone_bench": stone_bench,
    "cairn": cairn,
    "stone_trough": stone_trough,
    "statue_head": statue_head,
    "boundary_stone": boundary_stone,
    "stone_well": stone_well,
    "stone_doorway": stone_doorway,
    "fallen_log": fallen_log,
    "dead_snag": dead_snag,
    "mushroom_cluster": mushroom_cluster,
    "stump_hollow": stump_hollow,
    "root_tangle": root_tangle,
    "termite_mound": termite_mound,
}
