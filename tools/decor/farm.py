"""Farm and shack decor (parts map batches 14a and 14b), in the Settler Patchwork look (scrap wood, faded sea teal,
zinc, rust, cotton and burlap), with the Vending Machine in Colony Works. The produce boxes carry the game's own crop
models (``meshes/items/<crop>.glb``) and the Grazer Head Topper the Grazer's own head (``meshes/bots/grazer.glb``), so
they always match. tools/part_kit/decor_kit.py has the materials and the runner; build_decor.py runs them.
"""

from __future__ import annotations

import math
import random

from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

import decor_kit as D
from decor_kit import Geo, Part, box, cylinder, dome, lathe, sweep

ITEMS = D.REPO / "mods" / "vanilla" / "meshes" / "items"
BOTS = D.REPO / "mods" / "vanilla" / "meshes" / "bots"


def plank(g, c, half, mat, rng=None, rot=None):
    """A scrap plank, a little out of true."""
    if rng is not None:
        tilt = Matrix.Rotation(rng.uniform(-0.012, 0.012), 3, "X") @ Matrix.Rotation(rng.uniform(-0.01, 0.01), 3, "Y")
        rot = tilt if rot is None else Matrix(rot) @ tilt
    box(g, c, half, mat, bevel=min(0.004, min(half) * 0.8), rot=rot)


def nails(g, pts, n):
    for p in pts:
        dome(g, p, n, 0.0045, "zinc", segs=6)


WOODS = ("wood", "wood_b", "wood_c")


# =====================================================================================================
# Produce boxes: one slatted crate, a crop heaped in it and a kraft label with the crop on it
# =====================================================================================================

CRATE = 0.5          # half size of the 4 x 4 x 4 crate
RIM = 0.3            # the crate walls' top (the crop heaps above it)


def crate(g, rng, rim=RIM, label=None):
    """The settlers' produce crate: corner posts, three side slats a face with gaps, a solid floor, hand holes,
    zinc corner brackets; `label` is a material carried on a kraft card nailed to the front."""
    h = CRATE - 0.012
    post = 0.035
    for sx in (-1, 1):
        for sy in (-1, 1):
            plank(g, (sx * (h - post), sy * (h - post), (rim - h) / 2), (post, post, (rim + h) / 2), "wood_b", rng)
    slat_h = (rim + h - 0.03) / 3
    for face in range(4):
        n = Vector(((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0))[face])
        along = 0 if n.x == 0 else 1
        for k in range(3):
            z = -h + 0.012 + slat_h * (k + 0.5) + 0.01 * k
            c = n * (h - 0.012)
            c.z = z
            half = [0.0, 0.0, slat_h / 2 - 0.012]
            half[along] = h - 2 * post
            half[1 - along] = 0.012
            plank(g, tuple(c), tuple(half), WOODS[(face + k) % 3], rng)
            if k == 2 and along == 1:
                # a hand hole in the top slat of each end
                hole = n * (h - 0.0005)
                hole.z = z
                box(g, tuple(hole), (0.0006, 0.07, 0.02), "hole", bevel=0.0)
    box(g, (0, 0, -h + 0.012), (h - 0.03, h - 0.03, 0.012), "wood_b", bevel=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (-h + 0.04, rim - 0.04):
                box(g, (sx * (h - 0.002), sy * (h - 0.03), z), (0.002, 0.03, 0.025), "zinc", bevel=0.001)
                box(g, (sx * (h - 0.03), sy * (h - 0.002), z), (0.03, 0.002, 0.025), "zinc", bevel=0.001)
    if label is not None:
        # the kraft label on the front, two nails
        z = -h + 0.012 + slat_h * 1.5 + 0.01
        box(g, (0, -h - 0.002, z), (0.17, 0.003, 0.1), "card", bevel=0.002)
        D.decal(g, (0, -h - 0.0055, z), (1, 0, 0), (0, 0, 1), 0.16, 0.09, label)
        nails(g, [(sx * 0.14, -h - 0.006, z + 0.07) for sx in (-1, 1)], (0, -1, 0))


def label_image(crop):
    """A kraft crop label in the seed packets' style: the crop drawn plainly in its colours and a row of pips."""
    w, h = 320, 180
    im = Image.new("RGB", (w, h), (176, 138, 92))
    d = ImageDraw.Draw(im)
    d.rectangle([6, 6, w - 7, h - 7], outline=(84, 62, 40), width=4)
    cx, cy = w // 2, h // 2 - 6
    if crop == "carrot":
        for k in range(3):
            dx = (k - 1) * 46
            d.polygon([(cx + dx - 16, cy - 40), (cx + dx + 16, cy - 40), (cx + dx + 2, cy + 52), (cx + dx - 2, cy + 52)], fill=(214, 112, 40))
            for a in (-1, 0, 1):
                d.line([(cx + dx, cy - 40), (cx + dx + a * 16, cy - 70)], fill=(78, 142, 50), width=7)
    elif crop == "tomato":
        for k, (dx, dy, r) in enumerate(((-40, 8, 38), (38, 12, 34), (0, -18, 40))):
            d.ellipse([cx + dx - r, cy + dy - r, cx + dx + r, cy + dy + r], fill=(196, 52, 40))
            for a in range(5):
                ang = a * 2 * math.pi / 5 - math.pi / 2
                d.line([(cx + dx, cy + dy - r + 6), (cx + dx + 16 * math.cos(ang), cy + dy - r + 6 + 10 * math.sin(ang))],
                       fill=(70, 120, 46), width=6)
    elif crop == "potato":
        for dx, dy, rx, ry in ((-48, 10, 40, 30), (40, 14, 44, 28), (-2, -20, 38, 30)):
            d.ellipse([cx + dx - rx, cy + dy - ry, cx + dx + rx, cy + dy + ry], fill=(176, 134, 82), outline=(110, 80, 48), width=4)
            for e in range(3):
                ex, ey = cx + dx + (e - 1) * rx * 0.5, cy + dy + ((e % 2) - 0.5) * ry * 0.6
                d.ellipse([ex - 4, ey - 3, ex + 4, ey + 3], fill=(110, 80, 48))
    for k in range(3):
        d.ellipse([cx - 40 + k * 32, h - 30, cx - 28 + k * 32, h - 18], fill=(84, 62, 40))
    return im


def heap(crop, rng, rim=RIM):
    """Copies of the crop's own model heaped in the crate: a packed layer at the rim and a looser one on top."""
    path = ITEMS / f"{crop}.glb"
    tris, scale = {"tomato": (110, 1.45), "carrot": (190, 1.35), "potato": (110, 1.5)}[crop]
    tpl = D.asset(path, tris)
    out = []
    h = CRATE - 0.05
    if crop == "carrot":
        # carrots lie flat along x, two to a row, alternate rows head to tail, a looser second layer on top
        for layer, z in ((0, rim - 0.045), (1, rim + 0.025)):
            rows = 9 if layer == 0 else 8
            for r in range(rows):
                y = -h + 0.07 + (2 * h - 0.14) * (r + 0.5 * layer) / (rows - 1 + 0.5 * layer)
                for c in range(2):
                    if layer and rng.random() < 0.3:
                        continue
                    flip = (r + c + layer) % 2
                    x = (c - 0.5) * 0.4 + rng.uniform(-0.03, 0.03)
                    m = (Matrix.Translation((x, y, z)) @ Matrix.Rotation(rng.uniform(-0.18, 0.18) + math.pi * flip, 4, "Z")
                         @ Matrix.Rotation(rng.uniform(-0.12, 0.12), 4, "Y") @ Matrix.Rotation(rng.uniform(0, 6.28), 4, "X")
                         @ Matrix.Scale(scale, 4))
                    out.append(D.place(tpl, m, f"Carrot{len(out)}"))
        return out
    step = {"tomato": 0.13, "potato": 0.15}[crop]
    n = int((2 * h) / step)
    for layer in range(2):
        z = rim - 0.06 + layer * 0.085
        cnt = n if layer == 0 else n - 1
        for i in range(cnt):
            for j in range(cnt):
                if layer and rng.random() < 0.45:
                    continue
                x = -h + step / 2 + (i + 0.5 * layer) * step + rng.uniform(-0.012, 0.012)
                y = -h + step / 2 + (j + 0.5 * layer) * step + rng.uniform(-0.012, 0.012)
                lean = Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, "X") @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 4, "Y")
                if crop == "potato":
                    lean = Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "X") @ Matrix.Rotation(rng.uniform(1.2, 1.9), 4, "Y")
                m = (Matrix.Translation((x, y, z)) @ Matrix.Rotation(rng.uniform(0, 6.28), 4, "Z") @ lean
                     @ Matrix.Scale(scale * rng.uniform(0.9, 1.08), 4))
                out.append(D.place(tpl, m, f"{crop.capitalize()}{len(out)}"))
    return out


def inside(ob, h, top):
    """True when a placed copy stays within the crate's walls (x, y within h) and under `top`."""
    vs = ob.data.vertices
    return all(abs(v.co.x) <= h and abs(v.co.y) <= h and v.co.z <= top for v in vs)


def produce_box(crop):
    def build(m):
        rng = random.Random(sum(map(ord, crop)))
        g = Geo()
        crate(g, rng, label=f"label_{crop}")
        # the bed under the crop, so the gaps show produce, not the floor
        box(g, (0, 0, RIM - 0.09), (CRATE - 0.03, CRATE - 0.03, 0.006), "wood_b")
        keep = []
        for ob in heap(crop, rng):
            if inside(ob, CRATE - 0.02, CRATE - 0.002):
                keep.append(ob)
            else:
                D.bpy.data.objects.remove(ob)
        return [("Body", g), *keep]
    return build


def produce_box_mixed(m):
    """The unlabelled box: the same crate packed to the top under a tied burlap cover, a stencilled outpost code."""
    rng = random.Random(7)
    g = Geo()
    crate(g, rng, rim=0.36)
    h = CRATE - 0.012
    # the cover: a burlap sheet bulging over the load, its edges hanging over the slats
    secs = []
    for k in range(11):
        t = -1 + 2 * k / 10
        secs.append((t * (h - 0.01), 0.0, h - 0.01, 0.28, 0.43 + 0.06 * (1 - t * t)))
    D.squircle_loft(g, secs, "burlap", ring_pts=28, power=6.0)
    for sx in (-1, 1):
        box(g, (sx * (h + 0.004), 0, 0.3), (0.004, h - 0.01, 0.06), "burlap", bevel=0.002)
        box(g, (0, sx * (h + 0.004), 0.3), (h - 0.01, 0.004, 0.06), "burlap", bevel=0.002)
    # the rope round it
    for x in (-0.22, 0.22):
        pts = [Vector((x, -h - 0.002, 0.27)), Vector((x, -h, 0.4)), Vector((x, -h + 0.08, 0.475)),
               Vector((x, 0, 0.488)), Vector((x, h - 0.08, 0.475)), Vector((x, h, 0.4)), Vector((x, h + 0.002, 0.27))]
        sweep(g, pts, 0.007, "twine", segs=6)
    D.pixel_text(g, "OP-7", (0, -h + 0.011, -0.1), (1, 0, 0), (0, 0, 1), 0.018, "faded_dark")
    return g


# =====================================================================================================
# Batch 14a, the rest
# =====================================================================================================

def hay_bale(m):
    """4 x 2 x 2: a bale of straw tied with two twine bands, loose stalks at its cut ends."""
    rng = random.Random(3)
    g = Geo()
    hx, hy, hz = 0.5, 0.25, 0.25
    box(g, (0, 0, 0), (hx - 0.012, hy - 0.012, hz - 0.012), "straw", bevel=0.035)
    for x in (-0.24, 0.24):
        pts = []
        for k in range(41):
            a = 2 * math.pi * k / 40
            c, s = math.cos(a), math.sin(a)
            e = 2 / 7.0
            pts.append(Vector((x, (hy - 0.008) * math.copysign(abs(c) ** e, c), (hz - 0.008) * math.copysign(abs(s) ** e, s))))
        sweep(g, pts, 0.0045, "twine", segs=6)
    # loose stalks poking out of the ends and the top
    for k in range(70):
        side = rng.choice((-1, 1))
        y, z = rng.uniform(-hy + 0.03, hy - 0.03), rng.uniform(-hz + 0.03, hz - 0.03)
        p = Vector((side * (hx - 0.03), y, z))
        d = Vector((side * rng.uniform(0.6, 1.0), rng.uniform(-0.4, 0.4), rng.uniform(-0.3, 0.5))).normalized()
        cylinder(g, p, p + d * rng.uniform(0.02, 0.028), 0.0016, "straw", segs=3)
    return g


def duck_statuette(m):
    """2 x 2 x 2: a carved wooden duckling painted yellow, a brick-red beak and black eyes, on a log slice."""
    g = Geo()
    hz = 0.25
    base_top = -hz + 0.07
    lathe(g, [(0.0, -hz), (0.19, -hz), (0.205, -hz + 0.01), (0.205, base_top - 0.008), (0.19, base_top), (0.0, base_top)], "wood_b", steps=28)
    lathe(g, [(0.0, base_top - 0.001), (0.186, base_top - 0.001), (0.186, base_top + 0.002), (0.0, base_top + 0.002)], "wood_c", steps=28)
    for r in (0.05, 0.1, 0.15):
        lathe(g, [(r - 0.003, base_top + 0.0021), (r + 0.003, base_top + 0.0021), (r, base_top + 0.0024)], "wood_b", steps=28)
    # the body: an egg lying along y (front -y), the breast low, the tail tipped up behind
    body = []
    for k in range(13):
        t = k / 12
        y = 0.13 - 0.27 * t
        r = 0.105 * math.sin(math.pi * (0.06 + 0.9 * t)) ** 0.7
        zc = base_top + 0.1 + 0.03 * (1 - t) ** 3
        body.append((y, 0.0, r * 1.0, zc - r * 0.92, zc + r * 0.85))
    D.squircle_loft(g, body, "duck_paint", ring_pts=24, power=2.1)
    # wings folded on the sides
    for sx in (-1, 1):
        wing = [(0.07 - 0.15 * k / 6, sx * 0.095, 0.012, base_top + 0.08, base_top + 0.08 + 0.07 * math.sin(math.pi * (0.1 + 0.8 * k / 6)))
                for k in range(7)]
        D.squircle_loft(g, wing, "duck_wing", ring_pts=12, power=2.2)
    # the tail tuft
    cylinder(g, (0, 0.12, base_top + 0.13), (0, 0.17, base_top + 0.19), 0.03, "duck_paint", segs=10, r1=0.004)
    # the head on top at the front
    hc = Vector((0, -0.075, base_top + 0.25))
    head = [(hc.y + 0.075 - 0.15 * k / 10, 0.0, 0.075 * math.sin(math.pi * (0.04 + 0.92 * k / 10)) ** 0.6,
             hc.z - 0.07 * math.sin(math.pi * (0.04 + 0.92 * k / 10)) ** 0.6, hc.z + 0.07 * math.sin(math.pi * (0.04 + 0.92 * k / 10)) ** 0.6)
            for k in range(11)]
    D.squircle_loft(g, head, "duck_paint", ring_pts=20, power=2.0)
    cylinder(g, (0, -0.075, base_top + 0.12), (0, -0.075, base_top + 0.2), 0.055, "duck_paint", segs=14, r1=0.05)
    # beak: a flat rounded bill
    bill = [(hc.y - 0.06 - 0.07 * k / 6, 0.0, 0.032 - 0.008 * k / 6, hc.z - 0.03, hc.z - 0.008) for k in range(7)]
    D.squircle_loft(g, bill, "beak_paint", ring_pts=14, power=3.0)
    for sx in (-1, 1):
        e = hc + Vector((sx * 0.052, -0.04, 0.018))
        dome(g, e, Vector((sx * 0.8, -0.6, 0.1)), 0.013, "hole", segs=10, height=0.008)
        dome(g, e + Vector((sx * 0.002, -0.004, 0.006)), Vector((sx * 0.8, -0.6, 0.3)), 0.003, "bone", segs=6, height=0.002)
    # a tuft of carved hair on the crown
    for k in range(3):
        a = (k - 1) * 0.35
        p = hc + Vector((0, 0.0, 0.065))
        cylinder(g, p, p + Vector((math.sin(a) * 0.02, 0.02, 0.035)), 0.006, "duck_paint", segs=6, r1=0.001)
    return g


def grass_planter(m):
    """4 x 1 x 4: a plank tray of soil with a lawn of grass growing in it, zinc corner straps."""
    rng = random.Random(11)
    g = Geo()
    h, hz = 0.5, 0.125
    wall_top = hz - 0.04
    for face in range(4):
        n = Vector(((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0))[face])
        along = 0 if n.x == 0 else 1
        c = n * (h - 0.015)
        c.z = (wall_top - hz) / 2
        half = [0.0, 0.0, (wall_top + hz) / 2]
        half[along] = h - (0.03 if along == 1 else 0.0)
        half[1 - along] = 0.015
        plank(g, tuple(c), tuple(half), WOODS[face % 3])
    box(g, (0, 0, -hz + 0.01), (h - 0.03, h - 0.03, 0.01), "wood_b")
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (h - 0.0016), sy * (h - 0.04), -0.02), (0.0015, 0.04, 0.05), "zinc", bevel=0.001)
            box(g, (sx * (h - 0.04), sy * (h - 0.0016), -0.02), (0.04, 0.0015, 0.05), "zinc", bevel=0.001)
    soil = wall_top - 0.025
    box(g, (0, 0, (soil - hz + 0.02) / 2), (h - 0.03, h - 0.03, (soil + hz - 0.02) / 2), "soil", bevel=0.0)
    # grass blades: thin three-sided spikes, leaning every way, two greens
    for k in range(520):
        x, y = rng.uniform(-h + 0.04, h - 0.04), rng.uniform(-h + 0.04, h - 0.04)
        ht = rng.uniform(0.05, 0.1)
        top = Vector((x + rng.uniform(-0.03, 0.03), y + rng.uniform(-0.03, 0.03), soil + ht))
        top.z = min(top.z, hz - 0.002)
        a = rng.uniform(0, 2 * math.pi)
        w = 0.006
        base = [Vector((x + w * math.cos(a + i * 2.1), y + w * math.sin(a + i * 2.1), soil - 0.002)) for i in range(3)]
        mat = "grass" if rng.random() < 0.75 else "grass_dry"
        g.add([*base, top], [(0, 1, 3), (1, 2, 3), (2, 0, 3)], mat)
    return g


def mannequin_boot(m):
    """2 x 2 x 3: a mannequin's leather work boot on a thick rubber sole, laced, with the wooden socket on top where it
    fits a leg (the long flat sole grips well)."""
    g = Geo()
    hx, hy, hz = 0.25, 0.375, 0.25
    floor = -hz
    toe, heel = -hy + 0.01, hy - 0.06
    sole = [(heel - (heel - toe) * k / 12, 0.0, 0.1 - 0.02 * abs(math.sin(math.pi * k / 12 * 0.7)) + 0.012 * math.sin(math.pi * k / 12),
             floor, floor + 0.04) for k in range(13)]
    D.squircle_loft(g, sole, "rubber", ring_pts=24, power=5.0)
    for k in range(8):
        y = heel - 0.04 - k * 0.085
        box(g, (0, y, floor + 0.0032), (0.085, 0.012, 0.003), "hole")
    # the upper: low over the toes, rising into the shaft at the heel
    upper = []
    for k in range(13):
        t = k / 12
        y = toe + 0.025 + (heel - 0.03 - toe - 0.025) * t
        top = floor + 0.04 + 0.09 + 0.1 * max(0.0, t - 0.35) ** 1.2
        upper.append((y, 0.0, 0.088 - 0.012 * (1 - math.sin(math.pi * min(t * 1.4, 1.0))), floor + 0.035, top))
    D.squircle_loft(g, upper, "leather", ring_pts=26, power=2.6)
    sc = Vector((0, heel - 0.09, 0))
    lathe(g, [(0.0, floor + 0.05), (0.098, floor + 0.05), (0.104, floor + 0.12), (0.1, hz - 0.075), (0.106, hz - 0.06), (0.0, hz - 0.06)],
          "leather", steps=28, center=(sc.x, sc.y))
    # toe cap and heel counter seams, the tongue and the laces
    lathe(g, [(0.0, floor + 0.04), (0.0, floor + 0.04)], "leather", steps=4)
    tongue = [(sc.y - 0.06 - 0.01 * k, 0.0, 0.05, floor + 0.13 + 0.17 * k / 6, floor + 0.15 + 0.2 * k / 6) for k in range(7)]
    D.squircle_loft(g, [(y, x, w, zb, zt) for (y, x, w, zb, zt) in tongue], "leather", ring_pts=12, power=3.0)
    for k in range(6):
        z = floor + 0.17 + k * 0.045
        y = sc.y - 0.095 - 0.0 * k
        for sx in (-1, 1):
            cylinder(g, (sx * 0.045, y + 0.005, z), (sx * 0.045, y - 0.006, z), 0.007, "zinc", segs=8)
        cylinder(g, (-0.045, y - 0.006, z), (0.045, y - 0.006, z + 0.03), 0.0035, "lace", segs=5)
        cylinder(g, (0.045, y - 0.006, z), (-0.045, y - 0.006, z + 0.03), 0.0035, "lace", segs=5)
    # the wooden socket and zinc collar on top
    cylinder(g, (sc.x, sc.y, hz - 0.065), (sc.x, sc.y, hz - 0.04), 0.1, "zinc", segs=24)
    cylinder(g, (sc.x, sc.y, hz - 0.04), (sc.x, sc.y, hz - 0.002), 0.085, "wood_c", segs=24)
    cylinder(g, (sc.x, sc.y, hz - 0.0019), (sc.x, sc.y, hz - 0.0015), 0.03, "hole", segs=12)
    return g


def mannequin_hand(m):
    """1 x 2 x 1: a jointed wooden mannequin hand pointing up, dowel knuckles, on a zinc wrist socket."""
    g = Geo()
    hz = 0.25
    floor = -hz
    cylinder(g, (0, 0, floor), (0, 0, floor + 0.03), 0.05, "zinc", segs=20)
    cylinder(g, (0, 0, floor + 0.03), (0, 0, floor + 0.09), 0.036, "wood_c", segs=18, r1=0.04)
    # the palm: a rounded slab, front -y
    palm_b, palm_t = floor + 0.09, floor + 0.21
    D.squircle_loft(g, [(0.02, 0.0, 0.05, palm_b, palm_t - 0.01), (0.0, 0.0, 0.056, palm_b - 0.004, palm_t), (-0.02, 0.0, 0.052, palm_b, palm_t - 0.012)],
                    "wood_c", ring_pts=20, power=3.5, matrix=None)

    def finger(base, dirs, r, segs_len):
        p = Vector(base)
        for d, L in zip(dirs, segs_len):
            d = Vector(d).normalized()
            dome(g, p, d, r * 1.05, "wood", segs=8, height=r * 0.9)
            q = p + d * L
            cylinder(g, p + d * 0.004, q, r, "wood_c", segs=10, r1=r * 0.93)
            p = q
            r *= 0.92
        dome(g, p, d, r, "wood_c", segs=8, height=r)

    # the index finger straight up, the others curled in toward the palm, the thumb across them
    finger((-0.036, 0, palm_t), [(0, 0, 1)] * 3, 0.0125, (0.045, 0.032, 0.026))
    for k, x in enumerate((-0.01, 0.014, 0.036)):
        finger((x, -0.005, palm_t - 0.004), [(0, -0.4, 1), (0, -1, 0.2), (0, -0.3, -1)], 0.012 - 0.001 * k, (0.032, 0.026, 0.02))
    finger((-0.05, -0.02, palm_b + 0.03), [(-0.6, -0.6, 0.6), (0.5, -1, 0.3)], 0.0135, (0.035, 0.03))
    return g


# =====================================================================================================
# Batch 14b
# =====================================================================================================

def plant_box(open_top):
    def build(m):
        """3 x 3 x 3: a settler's octagonal card planter box, tapered, taped, a leaf stencil on two faces; the open
        one has its flaps folded out and soil with sprouts inside."""
        rng = random.Random(5)
        g = Geo()
        h = 0.375
        r0, r1 = 0.31, 0.36
        top = h - 0.006 if not open_top else h - 0.09
        ph = math.pi / 8
        # an eight-sided box: lathe with eight steps, turned so a flat faces the front
        verts = []
        for z, r in ((-h, r0), (top, r1)):
            for k in range(8):
                a = ph + k * math.pi / 4 - math.pi / 2 - math.pi / 8
                verts.append((r * math.cos(a), r * math.sin(a), z))
        faces = [(k, (k + 1) % 8, 8 + (k + 1) % 8, 8 + k) for k in range(8)] + [tuple(reversed(range(8)))]
        if not open_top:
            faces.append(tuple(range(8, 16)))
        g.add(verts, faces, "card")
        flat = r1 * math.cos(math.pi / 8)
        for k, a in enumerate((-math.pi / 2, 0.0)):
            n = Vector((math.cos(a), math.sin(a), 0))
            u = Vector((-n.y, n.x, 0))
            c = n * ((r0 + r1) / 2 * math.cos(math.pi / 8) + 0.0035) + Vector((0, 0, 0.0))
            tilt = math.atan2(r1 - r0, top + h)
            v = Vector((0, 0, 1)) * math.cos(tilt) + n * math.sin(tilt)
            D.decal(g, c, u, v, 0.1, 0.1, "leaf")
        if not open_top:
            # tape across the lid and down two sides
            box(g, (0, 0, top + 0.002), (0.05, flat, 0.002), "tape", bevel=0.0)
            for s in (-1, 1):
                box(g, (0, s * (flat + 0.0005), top - 0.05), (0.05, 0.002, 0.05), "tape", rot=Matrix.Rotation(s * math.atan2(r1 - r0, top + h), 3, "X"))
        else:
            # the flaps folded out and down, soil and sprouts
            for k in range(8):
                a = ph + k * math.pi / 4 - math.pi / 2 - math.pi / 8
                b = a + math.pi / 4
                p0 = Vector((r1 * math.cos(a), r1 * math.sin(a), top))
                p1 = Vector((r1 * math.cos(b), r1 * math.sin(b), top))
                mid = (p0 + p1) / 2
                out = Vector((mid.x, mid.y, 0)).normalized()
                fold = 0.07
                q0, q1 = p0 + out * fold * 0.12 + Vector((0, 0, fold)), p1 + out * fold * 0.12 + Vector((0, 0, fold))
                if k % 2:
                    q0, q1 = p0 + out * fold * 0.2 + Vector((0, 0, fold * 0.95)), p1 + out * fold * 0.2 + Vector((0, 0, fold * 0.95))
                g.add([p0, p1, q1, q0], [(0, 1, 2, 3), (3, 2, 1, 0)], "card")
            verts = []
            for k in range(8):
                a = ph + k * math.pi / 4 - math.pi / 2 - math.pi / 8
                verts.append((r1 * 0.985 * math.cos(a), r1 * 0.985 * math.sin(a), top - 0.03))
            g.add(verts, [tuple(range(8))], "soil")
            for k in range(26):
                x, y = rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2)
                base = Vector((x, y, top - 0.03))
                stem = base + Vector((rng.uniform(-0.01, 0.01), rng.uniform(-0.01, 0.01), rng.uniform(0.03, 0.06)))
                cylinder(g, base, stem, 0.0025, "grass", segs=4)
                for s in (-1, 1):
                    a = rng.uniform(0, math.pi)
                    leaf = stem + Vector((s * 0.02 * math.cos(a), s * 0.02 * math.sin(a), 0.006))
                    g.add([stem, leaf + Vector((0, 0, 0.006)), leaf - Vector((0, 0, 0.004))], [(0, 1, 2), (2, 1, 0)], "grass")
        return g
    return build


def leaf_image():
    im = Image.new("RGB", (160, 160), (176, 138, 92))
    d = ImageDraw.Draw(im)
    d.ellipse([18, 18, 142, 142], outline=(70, 120, 46), width=9)
    d.polygon([(80, 38), (112, 74), (98, 112), (80, 122), (62, 112), (48, 74)], fill=(70, 120, 46))
    d.line([(80, 50), (80, 130)], fill=(176, 138, 92), width=5)
    return im


def shack_awning(m):
    """4 x 2 x 3: a lean-to awning, a corrugated sheet in faded sea teal on a plank batten, held out by two zinc
    struts, with a rusty drip edge."""
    g = Geo()
    hx, hy, hz = 0.5, 0.375, 0.25
    y0, y1 = hy - 0.05, -hy + 0.01
    z0, z1 = hz - 0.03, -0.02
    n_x, n_l = 48, 8
    amp, waves = 0.011, 8
    verts, faces = [], []
    for j in range(n_l + 1):
        t = j / n_l
        y, z = y0 + (y1 - y0) * t, z0 + (z1 - z0) * t
        for i in range(n_x + 1):
            x = -hx + 0.01 + (2 * hx - 0.02) * i / n_x
            verts.append((x, y, z + amp * math.sin(2 * math.pi * waves * i / n_x)))
    for j in range(n_l):
        for i in range(n_x):
            a = j * (n_x + 1) + i
            faces.append((a, a + 1, a + n_x + 2, a + n_x + 1))
    g.add(verts, faces, "sea_paint")
    g.add([(x, y, z - 0.004) for (x, y, z) in verts], [tuple(reversed(f)) for f in faces], "zinc")
    # the batten along the back and the wall plank under it
    plank(g, (0, hy - 0.03, z0 - 0.02), (hx - 0.005, 0.03, 0.025), "wood_b")
    plank(g, (0, hy - 0.012, -0.08), (hx - 0.04, 0.012, 0.16), "wood")
    nails(g, [(x, hy - 0.025, z0 + 0.008) for x in (-0.4, -0.15, 0.15, 0.4)], (0, 0, 1))
    # struts from the wall plank out under the sheet
    for sx in (-1, 1):
        x = sx * 0.36
        a = Vector((x, hy - 0.025, -hz + 0.02))
        b = Vector((x, -0.12, z0 + (z1 - z0) * ((y0 + 0.12) / (y0 - y1)) - 0.02))
        box(g, tuple((a + b) / 2), (0.01, (a - b).length / 2, 0.006), "zinc", bevel=0.002,
            rot=Matrix.Rotation(math.atan2(b.z - a.z, a.y - b.y), 3, "X") @ Matrix.Identity(3))
        box(g, tuple(a + Vector((0, -0.006, 0.02))), (0.025, 0.006, 0.03), "zinc", bevel=0.002)
        dome(g, a + Vector((0, -0.012, 0.02)), (0, -1, 0), 0.006, "rust")
    # the rusty drip edge along the front
    cylinder(g, (-hx + 0.01, y1 + 0.004, z1 - 0.006), (hx - 0.01, y1 + 0.004, z1 - 0.006), 0.008, "rust", segs=8)
    return g


def stacked_crates(m):
    """3 x 4 x 3: three ribbed plastic crates in faded sea teal, stacked and held by a mustard ratchet strap."""
    g = Geo()
    h, hz = 0.35, 0.5
    ch = (2 * hz - 0.02) / 3
    for k in range(3):
        zb = -hz + k * (ch + 0.004)
        zc = zb + ch / 2
        inset = 0.004 * (k % 2)
        box(g, (0, 0, zc), (h - 0.01 - inset, h - 0.01 - inset, ch / 2 - 0.004), "plastic_sea", bevel=0.012)
        box(g, (0, 0, zb + ch - 0.012), (h - inset, h - inset, 0.012), "plastic_sea_dark", bevel=0.006)
        box(g, (0, 0, zb + 0.012), (h - 0.016 - inset, h - 0.016 - inset, 0.012), "plastic_sea_dark", bevel=0.006)
        for face in range(4):
            n = Vector(((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0))[face])
            u = Vector((-n.y, n.x, 0))
            # vent slots in two rows, a hand hole at the top of the ends
            for row in range(2):
                for col in range(5):
                    p = n * (h - 0.01 - inset + 0.0006) + u * (col - 2) * 0.11 + Vector((0, 0, zb + ch * (0.32 + 0.28 * row)))
                    hh = [0.0006, 0.0006, 0.04]
                    hh[0 if n.x else 1] = 0.0006
                    hh[1 if n.x else 0] = 0.016
                    box(g, tuple(p), tuple(hh), "hole")
            for col in range(4):
                p = n * (h - 0.01 - inset + 0.002) + u * (col - 1.5) * 0.15 + Vector((0, 0, zc))
                hh = [0.003, 0.003, ch / 2 - 0.02]
                hh[1 if n.x else 0] = 0.008
                box(g, tuple(p), tuple(hh), "plastic_sea_dark", bevel=0.002)
    # the strap: up the front, over the top and down the back, and its ratchet
    for sy in (-1, 1):
        box(g, (0.08, sy * (h + 0.0), 0), (0.024, 0.0015, hz - 0.004), "webbing", bevel=0.001)
    box(g, (0.08, 0, hz - 0.0015), (0.024, h, 0.0015), "webbing", bevel=0.001)
    box(g, (0.08, -h - 0.008, 0.05), (0.03, 0.007, 0.05), "zinc", bevel=0.003)
    cylinder(g, (0.045, -h - 0.016, 0.03), (0.115, -h - 0.016, 0.03), 0.009, "zinc", segs=10)
    box(g, (0.08, -h - 0.02, 0.1), (0.012, 0.005, 0.045), "rust", bevel=0.003)
    return g


def vend_image():
    """The vending machine's header: Grazer milk, a bottle beside a Grazer's face, bone on sea teal."""
    w, h = 640, 128
    im = Image.new("RGB", (w, h), (79, 154, 147))
    d = ImageDraw.Draw(im)
    bone = (217, 209, 188)
    # bottle
    d.rounded_rectangle([60, 40, 110, 118], 12, fill=bone)
    d.rectangle([74, 18, 96, 42], fill=bone)
    d.rectangle([70, 12, 100, 22], fill=(200, 66, 58))
    # grazer face: head, snout, ears, horns
    cx, cy = 200, 70
    d.ellipse([cx - 44, cy - 40, cx + 44, cy + 40], fill=bone)
    d.ellipse([cx - 30, cy + 2, cx + 30, cy + 44], fill=(232, 168, 168))
    for s in (-1, 1):
        d.ellipse([cx + s * 44 - 22, cy - 26, cx + s * 44 + 22, cy - 6], fill=bone)
        d.ellipse([cx + s * 18 - 7, cy - 20, cx + s * 18 + 7, cy - 6], fill=(43, 47, 53))
        d.arc([cx + s * 22 - 16, cy - 64, cx + s * 22 + 16, cy - 32], 180 if s < 0 else 270, 270 if s < 0 else 360, fill=(232, 168, 168), width=8)
        d.ellipse([cx + s * 12 - 5, cy + 18, cx + s * 12 + 5, cy + 28], fill=(150, 90, 90))
    # drops
    for k in range(5):
        x = 300 + k * 64
        d.polygon([(x, 38), (x + 18, 74), (x - 18, 74)], fill=bone)
        d.ellipse([x - 18, 58, x + 18, 94], fill=bone)
    return im


def vending_machine(m):
    """4 x 7 x 3: a Colony vending machine for Grazer milk: a bone cabinet in steel-blue rails, a sea-teal header with
    the bottle and Grazer pictogram, shelves of milk bottles behind the glass, a graphite panel with a lit cyan screen,
    a coin slot and the delivery flap."""
    rng = random.Random(9)
    g = Geo()
    hx, hy, hz = 0.5, 0.375, 0.875
    floor = -hz
    box(g, (0, 0.02, floor + 0.03), (hx - 0.04, hy - 0.06, 0.03), "graphite", bevel=0.006)
    box(g, (0, 0.02, 0.03), (hx - 0.02, hy - 0.04, hz - 0.03), "bone", bevel=0.012)
    fy = -hy + 0.056
    # the front frame
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.025), fy, 0.02), (0.025, 0.035, hz - 0.02), "steel", bevel=0.008)
    for z in (floor + 0.075, hz - 0.025):
        box(g, (0, fy, z), (hx, 0.035, 0.025), "steel", bevel=0.008)
    # header
    hzc = hz - 0.13
    box(g, (0, fy - 0.01, hzc), (hx - 0.05, 0.03, 0.08), "graphite", bevel=0.006)
    D.decal(g, (0, fy - 0.0405, hzc), (1, 0, 0), (0, 0, 1), 0.4, 0.08, "vend_header")
    # the window: dark inside, four shelves of bottles, the glass
    wx0, wx1, wz0, wz1 = -hx + 0.06, 0.18, -0.38, hzc - 0.12
    box(g, ((wx0 + wx1) / 2, fy + 0.2, (wz0 + wz1) / 2), ((wx1 - wx0) / 2, 0.004, (wz1 - wz0) / 2), "hole")
    for k in range(4):
        z = wz0 + 0.04 + k * (wz1 - wz0 - 0.06) / 4
        box(g, ((wx0 + wx1) / 2, fy + 0.11, z), ((wx1 - wx0) / 2, 0.09, 0.006), "steel_dark")
        for j in range(5):
            if rng.random() < 0.2:
                continue
            x = wx0 + 0.06 + j * (wx1 - wx0 - 0.12) / 4
            for row in range(2):
                y = fy + 0.06 + row * 0.08
                lathe(g, [(0.0, z + 0.006), (0.03, z + 0.006), (0.032, z + 0.1), (0.016, z + 0.13), (0.016, z + 0.15), (0.0, z + 0.15)],
                      "bottle", steps=10, center=(x, y))
                lathe(g, [(0.0, z + 0.148), (0.018, z + 0.148), (0.018, z + 0.16), (0.0, z + 0.16)], "signal" if j % 2 else "sea_paint", steps=10, center=(x, y))
    for (a, b, c, d) in ((wx0, wx1, wz1, hzc - 0.08), (wx0, wx1, -0.46, wz0)):
        box(g, ((a + b) / 2, fy, (c + d) / 2), ((b - a) / 2 + 0.01, 0.03, (d - c) / 2), "bone", bevel=0.006)
    box(g, ((wx0 + wx1) / 2, fy - 0.005, (wz0 + wz1) / 2), ((wx1 - wx0) / 2, 0.004, (wz1 - wz0) / 2), "glass")
    # the panel on the right: screen, keypad, coin slot, coin return
    px = (wx1 + hx - 0.05) / 2
    box(g, (px, fy - 0.004, (wz0 + wz1) / 2), ((hx - 0.05 - wx1) / 2 - 0.01, 0.03, (wz1 - wz0) / 2), "graphite", bevel=0.006)
    box(g, (px, fy - 0.035, wz1 - 0.1), (0.08, 0.002, 0.045), "cyan_glow")
    for r in range(4):
        for c in range(3):
            box(g, (px - 0.05 + c * 0.05, fy - 0.038, wz1 - 0.22 - r * 0.05), (0.018, 0.006, 0.018), "steel" if (r, c) != (3, 2) else "mustard", bevel=0.004)
    box(g, (px, fy - 0.036, wz0 + 0.2), (0.012, 0.003, 0.035), "hole")
    box(g, (px, fy - 0.036, wz0 + 0.08), (0.035, 0.003, 0.025), "hole")
    # the delivery flap
    box(g, (-0.1, fy - 0.004, -0.62), (0.3, 0.03, 0.1), "graphite", bevel=0.008)
    box(g, (-0.1, fy - 0.036, -0.62), (0.26, 0.003, 0.07), "steel_dark", bevel=0.003)
    cylinder(g, (-0.22, fy - 0.045, -0.565), (0.02, fy - 0.045, -0.565), 0.006, "mustard", segs=8)
    # side stripes
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.018), 0.02, 0.2), (0.002, hy - 0.06, 0.04), "steel", bevel=0.001)
    return g


def grazer_head_topper(m):
    """4 x 3 x 3: a Grazer's head on a bone mounting cap, made from the Grazer's own model (as the farmers top their
    vending machines with one)."""
    tpl = D.asset(BOTS / "grazer.glb", 7200, keep=lambda n: n.split(".")[0] in ("Head",))
    lo = Vector(map(min, *[v.co for v in tpl.data.vertices]))
    hi = Vector(map(max, *[v.co for v in tpl.data.vertices]))
    hx, hy, hz = 0.5, 0.375, 0.375
    size = hi - lo
    s = min((2 * hx - 0.02) / size.x, (2 * hy - 0.02) / size.y, (2 * hz - 0.07) / size.z)
    c = (lo + hi) / 2
    # the Grazer faces +y in Blender axes (its root is turned half round); turn the head to face the part's front
    mat = (Matrix.Translation((0, 0, -hz + 0.05 - (lo.z - c.z) * s)) @ Matrix.Rotation(math.pi, 4, "Z")
           @ Matrix.Scale(s, 4) @ Matrix.Translation(-c))
    head = D.place(tpl, mat, "Head")
    g = Geo()
    box(g, (0, 0, -hz + 0.025), (hx * 0.6, hy * 0.7, 0.025), "bone", bevel=0.01)
    box(g, (0, 0, -hz + 0.006), (hx * 0.62, hy * 0.72, 0.006), "graphite", bevel=0.004)
    return [("Base", g), head]


def materials_14(m):
    for crop in ("carrot", "tomato", "potato"):
        m[f"label_{crop}"] = D.image_mat(f"dk_label_{crop}", label_image(crop), rough=0.85)
    m["leaf"] = D.image_mat("dk_leaf", leaf_image(), rough=0.85)
    m["tape"] = D.pt.plain("dk_tape", "#c9ad7a", 0.4, 0.0)
    m["vend_header"] = D.image_mat("dk_vend_header", vend_image(), rough=0.4, emission=0.6)


BATCH_14A = {
    "hay_bale": Part((4, 2, 2), hay_bale, "Settler hay bale: straw tied with two twine bands, loose stalks at the ends. Replaces the box placeholder."),
    "duck_statuette": Part((2, 2, 2), duck_statuette, "Settler carving: a yellow-painted wooden duckling with a brick-red bill on a log slice."),
    "produce_box_mixed": Part((4, 4, 4), produce_box_mixed, "Settler produce crate packed under a tied burlap cover, no label (SM's other fruit boxes)."),
    "produce_box_carrot": Part((4, 4, 4), produce_box("carrot"), "Settler produce crate of our Carrot models, the kraft crop label on the front."),
    "produce_box_tomato": Part((4, 4, 4), produce_box("tomato"), "Settler produce crate of our Tomato models, the kraft crop label on the front."),
    "grass_planter": Part((4, 1, 4), grass_planter, "Settler plank tray of soil with grass growing in it, zinc corner straps."),
    "mannequin_boot": Part((2, 2, 3), mannequin_boot, "Mannequin work boot: leather upper, long rubber sole, laces, wooden leg socket on top."),
}
BATCH_14B = {
    "mannequin_hand": Part((1, 2, 1), mannequin_hand, "Jointed wooden mannequin hand pointing up, dowel knuckles, zinc wrist socket."),
    "plant_container_open": Part((3, 3, 3), plant_box(True), "Settler card planter, octagonal and tapered, flaps out, soil and sprouts inside."),
    "plant_container": Part((3, 3, 3), plant_box(False), "Settler card planter box, closed and taped, a leaf stencil on two faces."),
    "shack_awning": Part((4, 2, 3), shack_awning, "Settler lean-to awning: corrugated sheet in faded sea teal on a batten, zinc struts."),
    "stacked_crates": Part((3, 4, 3), stacked_crates, "Three ribbed plastic crates in faded sea teal, held by a mustard ratchet strap."),
    "vending_machine": Part((4, 7, 3), vending_machine, "Colony vending machine for Grazer milk: bottles behind glass, lit cyan screen, keypad."),
    "grazer_head_topper": Part((4, 3, 3), grazer_head_topper, "The Grazer's own head on a bone cap: the farmers' vending machine topper."),
    "produce_box_potato": Part((4, 4, 4), produce_box("potato"), "Settler produce crate of our Potato models, the kraft crop label on the front."),
}
