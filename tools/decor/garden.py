"""Potted plants (parts map batch 16): Settler Patchwork pots in one pot language, and the Big Pot in Old Stone.

Every settler pot is the same fired-clay pot (brick red, a rolled rim painted faded sea teal, a darker foot ring, soil
inside), turned on one profile at two sizes: the small pot on a 1 x 1 footprint (Small Potted Plant, Potted Cactus,
Potted Blooming Cactus, Potted Blue Flower) and the large pot on 2 x 2 (Potted Plant, Potted Vine, Potted Seedling).
The Big Pot is an urn of dressed stone from before the colony on a square plinth, with moss on its top edges
and a fern in it (docs/art-direction.md: stone #8F9289, dark stone #5F645C). Flowers are blue and pink, never orange.
tools/part_kit/decor_kit.py has the shared materials and the runner; build_decor.py --batch 16 runs them.
"""

from __future__ import annotations

import math
import random

from mathutils import Matrix, Vector

import decor_kit as D
from decor_kit import Geo, Part, box, cylinder, dome, lathe, sweep

BLOCK = D.BLOCK

# The settler pot, as fractions of the footprint's half width `hw`: top radius, foot radius, height.
POT_TOP, POT_FOOT, POT_TALL = 0.86, 0.62, 1.2


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def pot(g, hw, floor):
    """The settler clay pot standing on `floor`, sized to a footprint of half width `hw`. Returns (soil height, inner
    radius at the soil)."""
    rt, rb, h = hw * POT_TOP, hw * POT_FOOT, hw * POT_TALL
    rim = h * 0.2                       # the rolled rim's depth
    wall = max(0.006, hw * 0.06)
    z0, zt = floor, floor + h
    zr = zt - rim
    soil = zt - h * 0.12
    foot = h * 0.07
    r_soil = rt - wall - 0.004
    # one solid of clay: the foot, a straight taper up under the rim, the rim stepping out, its lip and the inner wall
    # down to the soil (the pot is closed at the soil, so nothing shows through)
    lathe(g, [(0.0, z0), (rb - 0.004, z0), (rb, z0 + 0.003), (rb, z0 + foot), (rt - 0.014, zr), (rt - 0.002, zr + 0.002),
              (rt, zr + 0.006), (rt, zt - 0.006), (rt - 0.004, zt), (rt - wall + 0.002, zt), (rt - wall, zt - 0.004),
              (r_soil, soil), (0.0, soil)], "clay", steps=40)
    # the foot ring, darker, and the rim's faded sea-teal band, each a skin just proud of the clay
    lathe(g, [(rb + 0.0004, z0 + 0.003), (rb + 0.0012, z0 + 0.003), (rb + 0.0012, z0 + foot - 0.002), (rb + 0.0004, z0 + foot - 0.002)],
          "clay_dark", steps=40)
    lathe(g, [(rt + 0.0004, zr + 0.009), (rt + 0.0012, zr + 0.009), (rt + 0.0012, zt - 0.008), (rt + 0.0004, zt - 0.008)],
          "sea_clay", steps=40)
    # the soil, a little domed
    lathe(g, [(0.0, soil + 0.0005), (r_soil + 0.001, soil + 0.0005), (r_soil * 0.9, soil + 0.005), (0.0, soil + 0.008)], "soil", steps=40)
    return soil + 0.006, r_soil


def leaf(g, base, direction, side, length, width, droop, mat, segs=7, fold=0.25, tip=0.8, back=None):
    """A leaf as a two-sided mesh: from `base` along `direction` (bending down by `droop` at its tip), `width` across
    `side` at its widest, folded along its midrib by `fold` (a share of the half width). `back` paints the underside."""
    d = Vector(direction).normalized()
    s = Vector(side)
    s = (s - d * s.dot(d)).normalized()
    up = s.cross(d).normalized()
    if up.z < 0:
        up = -up
    pts = []
    for k in range(segs + 1):
        t = k / segs
        c = Vector(base) + d * length * t - Vector((0, 0, droop * t * t))
        w = width * 0.5 * math.sin(math.pi * min(1.0, t ** tip)) if k < segs else 0.0
        pts.append((c, w))
    verts, faces = [], []
    for c, w in pts:
        verts += [tuple(c - s * w + up * w * fold), tuple(c), tuple(c + s * w + up * w * fold)]
    for k in range(segs):
        a, b = 3 * k, 3 * (k + 1)
        faces += [(a, b, b + 1, a + 1), (a + 1, b + 1, b + 2, a + 2)]
    g.add(verts, faces, mat)
    g.add(verts, [tuple(reversed(f)) for f in faces], back or mat)


def stem(g, pts, r, mat="stem", segs=5):
    sweep(g, [Vector(p) for p in pts], r, mat, segs=segs)


def arc(p0, p1, lift, n=6):
    """Points from p0 to p1 bowed up by `lift` in the middle (a stem)."""
    p0, p1 = Vector(p0), Vector(p1)
    return [p0.lerp(p1, k / n) + Vector((0, 0, lift * math.sin(math.pi * k / n))) for k in range(n + 1)]


def ribbed(g, z0, z1, radius, mat, ribs=8, depth=0.12, rings=10, top=0.55, cx=0.0, cy=0.0):
    """A ribbed cactus body: `radius(t)` (t 0 at z0 to 1 at z1) times a rib wave, closed with a rounded crown."""
    verts, faces = [], []
    steps = ribs * 6
    for i in range(rings + 1):
        t = i / rings
        z = z0 + (z1 - z0) * t
        r = radius(t)
        for k in range(steps):
            a = 2 * math.pi * k / steps
            rr = r * (1 - depth * (0.5 - 0.5 * math.cos(ribs * a)))
            verts.append((cx + rr * math.cos(a), cy + rr * math.sin(a), z))
    for i in range(rings):
        for k in range(steps):
            k1 = (k + 1) % steps
            faces.append((i * steps + k, i * steps + k1, (i + 1) * steps + k1, (i + 1) * steps + k))
    centre = len(verts)
    verts.append((cx, cy, z1 + radius(1.0) * top * 0.25))
    for k in range(steps):
        faces.append((rings * steps + k, rings * steps + (k + 1) % steps, centre))
    g.add(verts, faces, mat)


def spines(g, rng, z0, z1, radius, ribs=8, per_rib=6, cx=0.0, cy=0.0, length=0.008):
    """Little white spines in tufts along the ribs' crests."""
    for k in range(ribs):
        a = 2 * math.pi * (k + 0.5) / ribs + math.pi / ribs
        for i in range(per_rib):
            t = (i + 0.5) / per_rib
            z = z0 + (z1 - z0) * t
            r = radius(t)
            out = Vector((math.cos(a), math.sin(a), 0))
            p = Vector((cx, cy, z)) + out * r
            for j in range(3):
                dvec = (out + Vector((rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6), rng.uniform(-0.3, 0.6)))).normalized()
                cylinder(g, p, p + dvec * length, 0.0008, "spine", segs=3, r1=0.0002)


def flower(g, centre, normal, r, petals, petal_mat, eye_mat, cup=0.3, turn=0.0):
    """A flower head: `petals` rounded petals round a domed eye, cupped up by `cup`, facing `normal`."""
    n = Vector(normal).normalized()
    u, w = D.bl.frame_of(n)
    for k in range(petals):
        a = 2 * math.pi * k / petals + turn
        d = u * math.cos(a) + w * math.sin(a)
        leaf(g, Vector(centre) + d * r * 0.15, d + n * cup, d.cross(n), r, r * 0.75, 0.0, petal_mat, segs=4, fold=0.1, tip=0.7)
    dome(g, centre, n, r * 0.28, eye_mat, segs=8, height=r * 0.18)


# =====================================================================================================
# Settler pots
# =====================================================================================================

def clamp_box(p, hw, margin=0.012):
    """`p` pulled in to stay inside a footprint of half width `hw`."""
    return Vector((max(-hw + margin, min(hw - margin, p.x)), max(-hw + margin, min(hw - margin, p.y)), p.z))


def fit(g, first, hw, top, base, margin=0.004):
    """Squeezes the vertices added since index `first` (a plant's leaves) into the part's box: across about the
    centre to within `hw`, and up from `base` to under `top`, so tips never poke out of the grid box."""
    verts = g.v[first:]
    if not verts:
        return
    reach = max(max(abs(x), abs(y)) for x, y, _ in verts)
    high = max(z for _, _, z in verts)
    sx = min(1.0, (hw - margin) / reach) if reach > 0 else 1.0
    sz = min(1.0, (top - margin - base) / (high - base)) if high > base else 1.0
    g.v[first:] = [(x * sx, y * sx, base + (z - base) * sz if z > base else z) for x, y, z in verts]


def potted_plant(m):
    """2 x 4 x 2: a broad-leafed plant (big glossy leaves on long arching stalks, the young ones standing up in the
    middle) in the large pot."""
    rng = random.Random(161)
    g = Geo()
    hw, floor = BLOCK, -0.5
    soil, rs = pot(g, hw, floor)
    first = len(g.v)
    count = 17
    for k in range(count):
        a = 2.39996 * k + rng.uniform(-0.1, 0.1)
        inner = k / count                     # 0: the outer, older leaves; 1: the young ones in the middle
        d = Vector((math.cos(a), math.sin(a), 0))
        side = d.cross(Vector((0, 0, 1)))
        tall = 0.12 + 0.4 * inner + rng.uniform(-0.04, 0.04)
        out = 0.14 * (1 - inner) + 0.02
        start = Vector((0, 0, soil)) + d * rng.uniform(0.0, rs * 0.3)
        end = clamp_box(start + d * out + Vector((0, 0, tall)), hw, 0.05)
        pts = [start.lerp(end, t) + d * 0.04 * math.sin(math.pi * t) * (1 - inner) for t in (i / 6 for i in range(7))]
        stem(g, pts, 0.0055 - 0.0015 * inner)
        length = 0.3 - 0.1 * inner + rng.uniform(-0.02, 0.02)
        lean = d * (1.0 - 0.55 * inner) + Vector((0, 0, 0.1 + 0.8 * inner))
        reach = (end + lean.normalized() * length)
        # keep the tip inside the footprint: shorten the leaf where it would poke out
        for _ in range(8):
            if abs(reach.x) < hw - 0.012 and abs(reach.y) < hw - 0.012:
                break
            length *= 0.9
            reach = end + lean.normalized() * length
        leaf(g, end, lean, side, length, length * 0.58, 0.07 * (1 - inner) + 0.01, "leaf" if k % 3 else "leaf_light",
             segs=9, fold=0.28, tip=0.75, back="leaf_back")
    fit(g, first, hw, 0.5, soil)
    return g


def potted_plant_small(m):
    """1 x 2 x 1: a leafy rosette (oval leaves spiralling up, the outer ones spreading) in the small pot."""
    rng = random.Random(162)
    g = Geo()
    hw, floor = BLOCK / 2, -0.25
    soil, rs = pot(g, hw, floor)
    count = 18
    for k in range(count):
        a = 2.39996 * k
        tier = k / count
        d = Vector((math.cos(a), math.sin(a), 0))
        base = Vector((0, 0, soil + 0.01 + 0.2 * tier))
        stem(g, [Vector((0, 0, soil)), base + d * 0.004], 0.0035)
        rise = 0.35 + 1.4 * tier + rng.uniform(-0.1, 0.1)
        length = 0.14 - 0.04 * tier
        lean = d + Vector((0, 0, rise))
        for _ in range(8):
            tip = base + lean.normalized() * length
            if abs(tip.x) < hw - 0.006 and abs(tip.y) < hw - 0.006:
                break
            length *= 0.9
        leaf(g, base, lean, d.cross(Vector((0, 0, 1))), length, length * 0.55, 0.025 * (1 - tier),
             "leaf" if k % 4 else "leaf_light", segs=7, fold=0.35, back="leaf_back")
    return g


def potted_seedling(m):
    """2 x 2 x 2: a row of fresh sprouts (two seed leaves, the taller ones a first true pair) in the large pot, the
    tallest tied to a twig stake with twine."""
    rng = random.Random(163)
    g = Geo()
    hw, floor = BLOCK, -0.25
    soil, rs = pot(g, hw, floor)
    spots = [(0.0, 0.0), (0.1, 0.05), (-0.09, 0.08), (0.05, -0.11), (-0.11, -0.06), (0.13, -0.05), (-0.02, 0.14), (0.08, 0.13),
             (-0.13, 0.03)]
    for i, (x, y) in enumerate(spots):
        tall = rng.uniform(0.09, 0.15) * (1.5 if i == 0 else 1.0)
        base = Vector((x, y, soil))
        top = base + Vector((rng.uniform(-0.012, 0.012), rng.uniform(-0.012, 0.012), tall))
        mid = base.lerp(top, 0.5) + Vector((rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01), 0))
        stem(g, [base, mid, top], 0.004, "stem_light")
        a = rng.uniform(0, math.pi)
        d = Vector((math.cos(a), math.sin(a), 0))
        for s in (-1, 1):
            leaf(g, top, d * s + Vector((0, 0, 0.3)), d.cross(Vector((0, 0, 1))), 0.06, 0.04, 0.008, "leaf_light", segs=5, fold=0.25)
        if tall > 0.12:
            e = Vector((-d.y, d.x, 0))
            for s in (-1, 1):
                leaf(g, top + Vector((0, 0, 0.004)), e * s + Vector((0, 0, 1.1)), d, 0.05, 0.03, 0.0, "leaf", segs=5, fold=0.35,
                     back="leaf_back")
    # the twig stake beside the tallest, tied with twine
    stake = Vector((0.03, -0.015, soil - 0.03))
    cylinder(g, stake, stake + Vector((0.004, 0.0, 0.26)), 0.005, "twig", segs=6, r1=0.0035)
    tie = Vector((0.016, -0.008, soil + 0.13))
    cylinder(g, tie - Vector((0.019, 0.008, 0)), tie + Vector((0.019, 0.008, 0)), 0.003, "twine", segs=6)
    return g


def potted_vine(m):
    """2 x 3 x 2: a vine with heart-shaped leaves winding up a scrap-wood stake, and runners trailing over the large
    pot's rim and down its sides."""
    rng = random.Random(164)
    g = Geo()
    hw, floor = BLOCK, -0.375
    soil, rs = pot(g, hw, floor)
    first = len(g.v)
    top = 0.34
    # the stake: a square batten of scrap wood, a zinc nail at its top
    box(g, (0, 0.01, (soil - 0.05 + top) / 2), (0.014, 0.014, (top - soil + 0.05) / 2), "wood_b", bevel=0.003)
    dome(g, (0, 0.01 - 0.0145, top - 0.02), (0, -1, 0), 0.005, "zinc", segs=6)
    # two climbing stems: helices round the stake, a leaf at every node
    for phase in (0.0, math.pi):
        pts = []
        turns = 2.6
        for k in range(41):
            t = k / 40
            a = 2 * math.pi * turns * t + phase
            r = 0.026 + 0.006 * math.sin(7 * t + phase)
            pts.append(Vector((r * math.cos(a), 0.01 + r * math.sin(a), soil + (top - soil - 0.02) * t * (0.85 if phase else 1.0))))
        stem(g, pts, 0.0038, "stem", segs=5)
        for k in range(2, 41, 2):
            p = pts[k]
            out = Vector((p.x, p.y - 0.01, 0)).normalized()
            side = out.cross(Vector((0, 0, 1)))
            length = rng.uniform(0.06, 0.085) * (1 - 0.3 * k / 40)
            leaf(g, p, out + Vector((0, 0, rng.uniform(-0.2, 0.5))), side, length, length * 0.95, 0.012,
                 "leaf" if k % 4 else "leaf_dark", segs=6, fold=0.2, tip=0.55, back="leaf_back")
    # runners: from the soil over the rim and down the outside of the pot, staying inside the footprint
    rt = hw * POT_TOP
    zt = floor + hw * POT_TALL
    for j in range(7):
        a = 2 * math.pi * j / 7 + 0.3
        d = Vector((math.cos(a), math.sin(a), 0))
        hang = rng.uniform(0.1, 0.27)
        run = [Vector((0, 0, soil)) + d * rs * 0.5, Vector((0, 0, zt + 0.014)) + d * (rt - 0.01), Vector((0, 0, zt - 0.008)) + d * (rt + 0.014)]
        for k in range(1, 6):
            z = zt - 0.008 - hang * k / 5
            # the pot tapers in, so the runner hangs just off its wall
            r = min(hw - 0.008, hw * POT_FOOT + (rt - hw * POT_FOOT) * max(0.0, (z - floor) / (zt - floor)) + 0.016)
            run.append(Vector((0, 0, z)) + d * r + d.cross(Vector((0, 0, 1))) * 0.012 * math.sin(k * 1.7 + j))
        stem(g, run, 0.003, "stem", segs=4)
        for k in range(1, len(run)):
            p = run[k]
            side = d.cross(Vector((0, 0, 1))) * (1 if k % 2 else -1)
            length = rng.uniform(0.04, 0.055)
            leaf(g, p + d * 0.002, side * 0.6 + d * 0.5 + Vector((0, 0, -0.5)), d.cross(Vector((0, 0, 1))), length, length * 0.95, 0.0,
                 "leaf" if k % 2 else "leaf_dark", segs=5, fold=0.15, tip=0.55, back="leaf_back")
    fit(g, first, hw, 0.375, floor)
    return g


def potted_cactus(m):
    """1 x 2 x 1: a ribbed column cactus with two arms turning up, white spines, in the small pot."""
    rng = random.Random(165)
    g = Geo()
    hw, floor = BLOCK / 2, -0.25
    soil, rs = pot(g, hw, floor)
    z1 = 0.2

    def body(t):
        return 0.036 * (0.92 + 0.12 * math.sin(math.pi * min(1, t * 1.3))) * (1 - 0.35 * t ** 6)
    ribbed(g, soil - 0.01, z1, body, "cactus", ribs=8, depth=0.16, rings=12)
    spines(g, rng, soil + 0.01, z1 - 0.01, body, ribs=8, per_rib=7)
    # the arms: out, then up, each ribbed
    for sx, zb, up in ((1, soil + 0.11, 0.1), (-1, soil + 0.16, 0.07)):
        elbow = Vector((sx * 0.072, 0, zb + 0.01))
        stem(g, [Vector((sx * 0.02, 0, zb)), Vector((sx * 0.05, 0, zb - 0.004)), elbow + Vector((0, 0, 0.008))], 0.021, "cactus", segs=12)

        def arm(t):
            return 0.021 * (1 - 0.35 * t ** 5)
        ribbed(g, elbow.z, elbow.z + up, arm, "cactus", ribs=7, depth=0.16, rings=7, cx=elbow.x, cy=elbow.y)
        spines(g, rng, elbow.z + 0.01, elbow.z + up - 0.005, arm, ribs=7, per_rib=4, cx=elbow.x, cy=elbow.y)
    # a few pebbles on the soil
    for k in range(7):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.045, rs - 0.012)
        dome(g, (r * math.cos(a), r * math.sin(a), soil), (0, 0, 1), rng.uniform(0.006, 0.01), "pebble", segs=6)
    return g


def potted_cactus_bloom(m):
    """1 x 2 x 1: a round ribbed barrel cactus crowned with pink flowers, in the small pot."""
    rng = random.Random(166)
    g = Geo()
    hw, floor = BLOCK / 2, -0.25
    soil, rs = pot(g, hw, floor)
    zb, zt = soil - 0.01, soil + 0.15

    def body(t):
        return 0.07 * math.sin(math.pi * (0.2 + 0.7 * t)) ** 0.7 + 0.008
    ribbed(g, zb, zt, body, "cactus_dark", ribs=11, depth=0.12, rings=12, top=0.2)
    spines(g, rng, zb + 0.02, zt - 0.01, body, ribs=11, per_rib=5, length=0.007)
    # a ring of flowers round the crown, buds in the middle
    for k in range(5):
        a = 2 * math.pi * k / 5
        d = Vector((math.cos(a), math.sin(a), 0))
        c = Vector((0, 0, zt + 0.004)) + d * 0.03
        flower(g, c, d * 0.5 + Vector((0, 0, 1)), 0.028, 9, "bloom", "bloom_eye", cup=0.55, turn=a)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        c = Vector((0.012 * math.cos(a), 0.012 * math.sin(a), zt + 0.008))
        cylinder(g, c, c + Vector((0, 0, 0.018)), 0.007, "bloom", segs=8, r1=0.002)
    return g


def potted_blue_flower(m):
    """1 x 2 x 1: a clump of strap leaves with blue flowers on wiry stems, in the small pot."""
    rng = random.Random(167)
    g = Geo()
    hw, floor = BLOCK / 2, -0.25
    soil, rs = pot(g, hw, floor)
    for k in range(20):
        a = 2 * math.pi * k / 20 + rng.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        leaf(g, Vector((0, 0, soil)) + d * 0.01, d * 0.4 + Vector((0, 0, 1)), d.cross(Vector((0, 0, 1))),
             rng.uniform(0.11, 0.16), 0.018, rng.uniform(0.04, 0.07), "leaf", segs=6, fold=0.4, tip=0.5, back="leaf_back")
    for k in range(9):
        a = 2.39996 * k
        d = Vector((math.cos(a), math.sin(a), 0))
        r = 0.015 + 0.06 * (k / 9)
        tall = rng.uniform(0.2, 0.3)
        base = Vector((0, 0, soil)) + d * 0.01
        head = Vector((0, 0, soil + tall)) + d * min(r, hw - 0.045)
        stem(g, arc(base, head, 0.0, 5), 0.0025, "stem")
        flower(g, head, d * 0.4 + Vector((0, 0, 1)), 0.034, 5, "petal_blue" if k % 3 else "petal_blue_light", "flower_eye", cup=0.25,
               turn=rng.uniform(0, 1))
    return g


# =====================================================================================================
# Old Stone
# =====================================================================================================

def big_pot(m):
    """3 x 3 x 3: an urn of dressed stone on a square plinth, carved bands round its belly, moss on its top
    edges and a fern in it."""
    rng = random.Random(168)
    g = Geo()
    h = 0.375
    floor = -h
    # the plinth: a bevelled square slab with a chamfered step
    box(g, (0, 0, floor + 0.045), (h - 0.03, h - 0.03, 0.045), "old_stone_dark", bevel=0.02)
    box(g, (0, 0, floor + 0.1), (h - 0.08, h - 0.08, 0.012), "old_stone", bevel=0.008)
    # the urn: a foot, a swelling belly, a waist and a flared lip, turned on 12 sides (dressed, not round)
    z0 = floor + 0.112
    sz = 0.72                           # the urn's heights, squat enough to leave room for the fern
    prof = [(0.0, z0), (0.16, z0), (0.17, z0 + 0.02 * sz), (0.13, z0 + 0.06 * sz), (0.15, z0 + 0.09 * sz), (0.25, z0 + 0.2 * sz),
            (0.285, z0 + 0.3 * sz), (0.27, z0 + 0.4 * sz), (0.22, z0 + 0.5 * sz), (0.215, z0 + 0.53 * sz), (0.29, z0 + 0.6 * sz),
            (0.3, z0 + 0.625 * sz), (0.27, z0 + 0.635 * sz), (0.22, z0 + 0.6 * sz), (0.0, z0 + 0.6 * sz)]
    lathe(g, prof, "old_stone", steps=12)
    # two carved bands round the belly, a little proud of it
    for z, r in ((z0 + 0.2 * sz, 0.25), (z0 + 0.4 * sz, 0.27)):
        D.bl.ring(g, z - 0.012, z + 0.012, r - 0.03, r + 0.008, "old_stone_dark", segs=12)
    # soil, and moss on the lip's top edge and in the plinth's step
    soil = z0 + 0.6 * sz
    lathe(g, [(0.0, soil), (0.222, soil), (0.215, soil + 0.006), (0.0, soil + 0.008)], "soil", steps=24)
    for k in range(26):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.25, 0.29)
        dome(g, (r * math.cos(a), r * math.sin(a), z0 + 0.628 * sz), (0, 0, 1), rng.uniform(0.012, 0.022), "moss", segs=7, height=0.006)
    for k in range(18):
        x, y = rng.uniform(-h + 0.05, h - 0.05), rng.choice((-1, 1)) * (h - 0.05)
        if rng.random() < 0.5:
            x, y = y, x
        dome(g, (x, y, floor + 0.09), (0, 0, 1), rng.uniform(0.012, 0.02), "moss", segs=7, height=0.006)
    # the fern: arching fronds of paired leaflets from the middle
    first = len(g.v)
    for k in range(13):
        a = 2 * math.pi * k / 13 + rng.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        reach = rng.uniform(0.26, 0.34) * (0.7 if k % 2 else 1.0)
        rise = rng.uniform(0.18, 0.3) * (1.3 if k % 2 else 1.0)
        base = Vector((0, 0, soil)) + d * 0.02
        ctrl = [base, base + d * reach * 0.35 + Vector((0, 0, rise)), base + d * reach * 0.75 + Vector((0, 0, rise * 0.9)),
                base + d * reach + Vector((0, 0, rise * 0.45))]
        pts = [Vector(p) for p in D.bl.bezier(*ctrl, n=10)]
        stem(g, pts, 0.0025, "stem", segs=4)
        side = d.cross(Vector((0, 0, 1)))
        for i in range(2, 10):
            t = i / 9
            p = pts[i]
            tangent = (pts[min(i + 1, 9)] - pts[i - 1]).normalized()
            size = 0.07 * (1 - t) + 0.016
            for s in (-1, 1):
                leaf(g, p, side * s * 0.9 + tangent * 0.5 + Vector((0, 0, 0.1)), tangent, size, size * 0.38, size * 0.2,
                     "fern" if i % 2 else "leaf_dark", segs=3, fold=0.2, tip=0.6)
    fit(g, first, h, h, soil)
    return g


# =====================================================================================================
# Materials and registry
# =====================================================================================================

def materials_16(m):
    tex, plain = D.lt.textured, D.pt.plain
    gc = D.gc
    m.update({
        "clay": tex("g16_clay", gc.half_res(D.concrete_surface(1601, "#8c4036")), normal_strength=0.9),
        "clay_dark": tex("g16_clay_dark", gc.half_res(D.concrete_surface(1603, "#5e3029")), normal_strength=0.9),
        "sea_clay": tex("g16_sea_clay", gc.half_res(D.concrete_surface(1605, "#5a9690")), normal_strength=0.7),
        "leaf": plain("g16_leaf", "#3f7a34", 0.45, 0.0),
        "leaf_light": plain("g16_leaf_light", "#5d9440", 0.5, 0.0),
        "leaf_dark": plain("g16_leaf_dark", "#2f5e2c", 0.5, 0.0),
        "leaf_back": plain("g16_leaf_back", "#6d9a5a", 0.7, 0.0),
        "fern": plain("g16_fern", "#4f8a3a", 0.6, 0.0),
        "stem": plain("g16_stem", "#4d6b2e", 0.7, 0.0),
        "stem_light": plain("g16_stem_light", "#8aa85a", 0.7, 0.0),
        "cactus": plain("g16_cactus", "#4f7d45", 0.6, 0.0),
        "cactus_dark": plain("g16_cactus_dark", "#3e6b48", 0.6, 0.0),
        "spine": plain("g16_spine", "#ece6d2", 0.5, 0.0),
        "pebble": plain("g16_pebble", "#b9b2a2", 0.8, 0.0),
        "bloom": plain("g16_bloom", "#d65a96", 0.5, 0.0),
        "bloom_eye": plain("g16_bloom_eye", "#f1d25a", 0.6, 0.0),
        "petal_blue": plain("g16_petal_blue", "#4466c8", 0.5, 0.0),
        "petal_blue_light": plain("g16_petal_blue_light", "#6e8ee0", 0.5, 0.0),
        "flower_eye": plain("g16_flower_eye", "#f0d050", 0.6, 0.0),
        "twig": plain("g16_twig", "#7a5a3a", 0.85, 0.0),
        "old_stone": tex("g16_old_stone", gc.half_res(D.concrete_surface(1607, "#8f9289")), normal_strength=1.8),
        "old_stone_dark": tex("g16_old_stone_dark", gc.half_res(D.concrete_surface(1609, "#5f645c")), normal_strength=1.8),
        "moss": plain("g16_moss", "#5d7a3a", 0.95, 0.0),
    })


BATCH_16 = {
    "big_pot": Part((3, 3, 3), big_pot, "Old Stone urn on a square plinth: dressed 12-sided belly, carved bands, moss, a fern."),
    "potted_plant": Part((2, 4, 2), potted_plant, "Large settler clay pot (brick red, sea-teal rim) with a broad-leafed plant on arching stalks."),
    "potted_plant_small": Part((1, 2, 1), potted_plant_small, "Small settler clay pot with a leafy rosette."),
    "potted_seedling": Part((2, 2, 2), potted_seedling, "Large settler clay pot with fresh sprouts, one tied to a twig stake."),
    "potted_vine": Part((2, 3, 2), potted_vine, "Large settler clay pot, a vine up a scrap-wood stake and runners trailing over the rim."),
    "potted_cactus_bloom": Part((1, 2, 1), potted_cactus_bloom, "Small settler clay pot with a barrel cactus crowned with pink flowers."),
    "potted_blue_flower": Part((1, 2, 1), potted_blue_flower, "Small settler clay pot with strap leaves and blue flowers on wiry stems."),
    "potted_cactus": Part((1, 2, 1), potted_cactus, "Small settler clay pot with a ribbed column cactus, two arms, spines and pebbles."),
}
