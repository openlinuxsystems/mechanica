"""Settler Patchwork props, second set (docs/world-generation.md, Scenes): the yard of a settler homestead, built from
scavenged wood, corrugated sheet patches, rope and sacking in the family's faded colours (docs/art-direction.md).

Beehives on a pallet, a compost bay with a fork in it, an open woodshed, a tarped plank pile, a ladder on trestles, an
outdoor table, a milking stool, a herb drying rack, a tin mailbox, a lantern post, a small water tower and a run of
hand-split pole fence.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, STONE
from props import _root, wood, plank, post, bolt, STRAW, STRAW_D, BARK, HEART

ROPE, SACK = "#b8a57a", "#c2a878"
COMPOST = "#3b2b1f"
HERB, HERB_D, HERB_DRY, CORN, HUSK = "#5e7f3a", "#4a6a30", "#8a7d4e", "#d6b448", "#cdbd8c"
GLASS = "#2a3a40"


# ---------------------------------------------------------------- helpers
def _beam(P, name, a, b, w, mat, h=None, bev=0.012, round_=False, segs=6, r2=None):
    """A box (or a pole) from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if round_:
        ob = K.cyl(name, w, d.length, mat, loc=(a + b) / 2, segs=segs, bev=0, parent=P, r2=r2)
    else:
        ob = K.box(name, (w, h or w, d.length), mat, loc=(a + b) / 2, bev=bev, parent=P)
    ob.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    return ob


def _rope():
    return M(ROPE, rough=0.95, wear=0.1, scratches=0.0)


def _lash(P, loc, r, rot=(0, 0, 0), turns=3, step=0.024):
    """A few turns of rope round a post where a pole crosses it."""
    x, y, z = loc
    for k in range(turns):
        K.torus("lash", r + 0.01, 0.009, _rope(), loc=(x, y, z + (k - (turns - 1) / 2) * step), rot=(rot[0], rot[1] + (k - 1) * 9, rot[2]),
                segs=12, parent=P)


def _tin(col=ZINC, **kw):
    kw.setdefault("metal", 0.5)
    kw.setdefault("wear", 0.65)
    kw.setdefault("grime", 0.45)
    return M(col, **kw)


def corrugated(P, name, w, d, mat, loc, rot=(0, 0, 0), pitch=0.15, amp=0.018, t=0.006):
    """A corrugated sheet: ribs run along y, w wide (x) and d long (y), centred on loc."""
    bm = bmesh.new()
    n = max(4, round(w / pitch * 3))
    top, bot = [], []
    for i in range(n + 1):
        x = -w / 2 + w * i / n
        z = amp * math.sin(2 * math.pi * x / pitch)
        top.append((bm.verts.new((x, -d / 2, z + t / 2)), bm.verts.new((x, d / 2, z + t / 2))))
        bot.append((bm.verts.new((x, -d / 2, z - t / 2)), bm.verts.new((x, d / 2, z - t / 2))))
    for i in range(n):
        bm.faces.new((top[i][0], top[i + 1][0], top[i + 1][1], top[i][1]))
        bm.faces.new((bot[i][0], bot[i][1], bot[i + 1][1], bot[i + 1][0]))
        bm.faces.new((top[i][0], bot[i][0], bot[i + 1][0], top[i + 1][0]))
        bm.faces.new((top[i][1], top[i + 1][1], bot[i + 1][1], bot[i][1]))
    bm.faces.new((top[0][0], top[0][1], bot[0][1], bot[0][0]))
    bm.faces.new((top[-1][0], bot[-1][0], bot[-1][1], top[-1][1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, loc, rot, P)


def _split_log(P, name, loc, r, length, roll, bark, cut, segs):
    """A split piece of firewood lying along y: a wedge or half-round of bark with a pale cut end towards -y."""
    G = K.link(bpy.data.objects.new(name, None))
    G.parent = P
    G.location = loc
    K.cyl(name + "_log", r, length, bark, rot=(90, roll, 0), segs=segs, bev=0, parent=G)
    K.cyl(name + "_end", r * 0.9, 0.012, cut, loc=(0, -length / 2 - 0.004, 0), rot=(90, roll, 0), segs=segs, bev=0, parent=G)
    return G


# ================================================================== Settler Patchwork
def beehive_boxes(seed=21):
    """Two hives of stacked boxes on a pallet raised on bricks, each box painted a different faded colour."""
    rnd = random.Random(seed)
    P = _root("beehives")
    brick = M(BRICK, wear=0.5, grime=0.5, rough=0.9, cracks=0.3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for k in range(3):
                K.box(f"brick{sx}{sy}{k}", (0.22, 0.11, 0.07), brick, loc=(sx * 0.55 + rnd.uniform(-0.02, 0.02), sy * 0.33, 0.035 + k * 0.07),
                      rot=(0, 0, 90 * (k % 2) + rnd.uniform(-6, 6)), bev=0.008, parent=P)
    # the pallet: three stringers and a deck of slats
    zb = 0.21
    for k, y in enumerate((-0.33, 0.0, 0.33)):
        plank(P, f"stringer{k}", (1.3, 0.08, 0.1), (0, y, zb + 0.05), shade=3)
    for k in range(7):
        plank(P, f"slat{k}", (0.12, 0.8, 0.022), (-0.57 + k * 0.19, 0, zb + 0.111), rot=(0, 0, rnd.uniform(-1.5, 1.5)), shade=k % 3)
    zt = zb + 0.122
    pal = [SEA, MUSTARD, BONE, BRICK, ZINC, STEEL]
    rnd.shuffle(pal)
    for h, (hx, boxes) in enumerate(((-0.32, (0.24, 0.24, 0.16)), (0.33, (0.24, 0.16)))):
        yaw = rnd.uniform(-4, 4)
        hive = K.link(bpy.data.objects.new(f"hive{h}", None))
        hive.parent = P
        hive.location = (hx, 0.02, zt)
        hive.rotation_euler = (0, 0, math.radians(yaw))
        # floor with the entrance slot and a sloping landing board
        plank(hive, "floor", (0.52, 0.46, 0.04), (0, 0, 0.02), shade=1)
        plank(hive, "landing", (0.4, 0.14, 0.02), (0, -0.28, 0.025), rot=(-12, 0, 0), shade=2)
        K.box("entrance", (0.3, 0.02, 0.022), M(GRAPHITE, rough=0.9, wear=0, scratches=0), loc=(0, -0.205, 0.051), bev=0, parent=hive)
        z = 0.04
        for b, bh in enumerate(boxes):
            col = pal[(h * 3 + b) % len(pal)]
            off = (rnd.uniform(-0.012, 0.012), rnd.uniform(-0.012, 0.012))
            K.box(f"box{b}", (0.5, 0.42, bh - 0.004), M(col, wear=0.85, grime=0.45, scratches=0.5, rough=0.85),
                  loc=(off[0], off[1], z + bh / 2), rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.01, parent=hive)
            for s in (-1, 1):  # hand-hold cleats
                K.box(f"cleat{b}{s}", (0.025, 0.16, 0.03), wood(3), loc=(off[0] + s * 0.262, off[1], z + bh * 0.62), bev=0.006, parent=hive)
            z += bh
        # telescoping lid with a tin cap and a stone to hold it down in the wind
        plank(hive, "lid", (0.56, 0.48, 0.07), (0, 0, z + 0.035), shade=0)
        K.box("cap", (0.58, 0.5, 0.012), _tin(), loc=(0, 0, z + 0.076), rot=(0, 0, rnd.uniform(-2, 2)), bev=0.004, parent=hive)
        K.sphere("weight", 0.09, M(STONE, rough=0.9, moss=0.4), loc=(rnd.uniform(-0.1, 0.1), rnd.uniform(-0.05, 0.08), z + 0.12), scale=(1.2, 1.0, 0.6),
                 parent=hive)
        z += 0.09
    # a zinc patch nailed over a rotten corner of one box
    K.box("patch", (0.16, 0.012, 0.12), _tin(RUST), loc=(-0.48, -0.193, zt + 0.15), rot=(0, 6, 0), bev=0.003, parent=P)
    for x in (-0.54, -0.42):
        bolt(P, (x, -0.2, zt + 0.15))
    return P


def compost_heap(seed=22):
    """A three-sided plank bay full of dark compost, straw on top and a fork stuck in it."""
    rnd = random.Random(seed)
    P = _root("compost")
    W, D, H = 1.5, 1.3, 0.9
    for sx in (-1, 1):
        for sy in (-1, 1):
            post(P, f"post{sx}{sy}", (sx * W / 2, sy * D / 2), H + 0.15, r=0.05, tilt=(rnd.uniform(-2, 2), rnd.uniform(-2, 2)))
    for k in range(5):
        z = 0.12 + k * 0.17
        if k == 3:  # a salvaged board in the back wall
            K.box("back_patch", (W, 0.025, 0.15), M(SEA, wear=0.75, grime=0.45, rough=0.85), loc=(0, D / 2 - 0.06, z), bev=0.008, parent=P)
        else:
            plank(P, f"back{k}", (W, 0.025, 0.15), (0, D / 2 - 0.06, z), rot=(0, rnd.uniform(-1, 1), 0), shade=k)
        for sx in (-1, 1):
            if sx == 1 and k == 3:
                continue  # a board gone from the right side
            plank(P, f"side{sx}{k}", (0.025, D, 0.15), (sx * (W / 2 + 0.06), 0, z), rot=(rnd.uniform(-1, 1), 0, 0), shade=k + 1)
    plank(P, "kick", (W, 0.025, 0.15), (0, -D / 2 - 0.06, 0.08), shade=3)

    def heap(x, y):
        u, v = x / 0.95, (y - 0.35) / 1.25
        h = 0.78 * (1 - 0.55 * u * u - v * v)
        h += 0.035 * math.sin(x * 11 + y * 4) * math.cos(y * 9 - x * 3) + 0.02 * math.sin(x * 23 + y * 17)
        edge = min(max((-0.55 - y) / 0.3, 0), 1)
        return max(h * (1 - (abs(x) / 0.72) ** 2 * edge), 0.0) + 0.01

    bm = bmesh.new()
    nu, nv = 14, 14
    vs = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            y = -0.98 + 1.6 * j / nv
            x = (-0.72 + 1.44 * i / nu) * (1 - 0.45 * min(max((-0.62 - y) / 0.36, 0), 1)) + (0.03 * math.sin(j * 2.1) if y < -0.62 else 0)
            row.append(bm.verts.new((x, y, heap(x, y))))
        vs.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    K.place(K.from_bm("heap", bm, K.mat("compost", COMPOST, rough=1.0, grime=0.2, noise_scale=7.0, cracks=0.25), smooth=True), parent=P)
    straw = K.mat("straw", STRAW, rough=0.95, grime=0.3, noise_scale=12.0)
    straws = (straw, K.mat("strawd", STRAW_D, rough=0.95, noise_scale=20.0))
    for k in range(40):
        x, y = rnd.uniform(-0.6, 0.6), rnd.uniform(-0.55, 0.5)
        K.cyl(f"straw{k}", 0.006, rnd.uniform(0.15, 0.3), straws[k % 2], loc=(x, y, heap(x, y) + 0.01), rot=(90 + rnd.uniform(-8, 8), 0, rnd.uniform(0, 180)),
              segs=4, bev=0, parent=P)
    # a pitchfork driven into the heap, leaning towards the front
    x, y = 0.25, 0.05
    f = K.link(bpy.data.objects.new("fork", None))
    f.parent = P
    f.location = (x, y, heap(x, y) - 0.02)
    f.rotation_euler = (math.radians(10), math.radians(24), 0)
    iron = M(GRAPHITE, metal=0.6, wear=0.5)
    K.cyl("handle", 0.017, 1.05, wood(2, wear=0.05), loc=(0, 0, 0.65), segs=8, bev=0, parent=f)
    K.cyl("ferrule", 0.022, 0.12, iron, loc=(0, 0, 0.13), segs=8, r2=0.017, bev=0, parent=f)
    K.box("bridge", (0.2, 0.025, 0.03), iron, loc=(0, 0, 0.06), bev=0.006, parent=f)
    for k in range(4):
        K.cyl(f"tine{k}", 0.007, 0.3, iron, loc=(-0.09 + k * 0.06, 0, -0.08), segs=6, r2=0.003, bev=0, parent=f)
    return P


def woodshed(seed=23):
    """An open-fronted lean-to of posts and planks under patched corrugated sheets, split firewood stacked inside."""
    rnd = random.Random(seed)
    P = _root("woodshed")
    W, D, HF, HB = 3.0, 1.5, 2.2, 1.8
    for x in (-W / 2, 0, W / 2):
        post(P, f"front{x}", (x, -D / 2), HF + 0.1, r=0.06)
    for x in (-W / 2, W / 2):
        post(P, f"back{x}", (x, D / 2), HB + 0.1, r=0.06)
    plank(P, "plate_f", (W + 0.2, 0.1, 0.14), (0, -D / 2, HF - 0.05), shade=3)
    plank(P, "plate_b", (W + 0.2, 0.1, 0.14), (0, D / 2, HB - 0.05), shade=3)
    slope = math.degrees(math.atan2(HF - HB, D))
    for k, x in enumerate((-W / 2 + 0.05, -0.5, 0.5, W / 2 - 0.05)):
        _beam(P, f"rafter{k}", (x, -D / 2 - 0.2, HF + 0.08 + 0.2 * math.tan(math.radians(slope))), (x, D / 2 + 0.2, HB + 0.02 - 0.2 * math.tan(math.radians(slope)) + 0.06),
              0.06, wood(1), h=0.1)
    # roof: three mismatched corrugated sheets, one a rusty replacement, one an old sea-green panel
    zc = (HF + HB) / 2 + 0.16
    for k, (x, col, w) in enumerate(((-1.05, ZINC, 1.2), (0.0, RUST, 1.1), (1.05, SEA, 1.2))):
        corrugated(P, f"sheet{k}", w, D + 0.6, _tin(col, wear=0.75, grime=0.5), (x, 0, zc + k * 0.008), rot=(-slope, rnd.uniform(-0.6, 0.6), rnd.uniform(-1.5, 1.5)))
    for x in (-1.45, 1.45):
        bolt(P, (x, -D / 2 - 0.15, zc + 0.08 + 0.004), rot=(-slope, 0, 0))
    # back wall of planks with gaps, left side planks, right side a corrugated sheet
    for k in range(6):
        z = 0.2 + k * 0.28
        plank(P, f"back{k}", (W, 0.025, 0.2), (0, D / 2 + 0.07, z), rot=(0, rnd.uniform(-0.8, 0.8), 0), shade=k)
        plank(P, f"left{k}", (0.025, D, 0.2), (-W / 2 - 0.07, 0, z), shade=k + 1)
    corrugated(P, "side_r", 1.45, 1.75, _tin(ZINC, wear=0.8, grime=0.55), (W / 2 + 0.075, 0.0, 0.98), rot=(0, 90, 0))
    # split firewood on two runners: the left bay full, the right bay half used
    for y in (-0.45, 0.4):
        plank(P, f"runner{y}", (W - 0.15, 0.08, 0.08), (0, y, 0.04), shade=3)
    bark = K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    cut = K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)
    for bay, (x0, x1, rows) in enumerate(((-1.38, -0.12, 7), (0.12, 1.38, 4))):
        for r in range(rows):
            z = 0.17 + r * 0.155
            n = int((x1 - x0) / 0.2)
            for i in range(n):
                x = x0 + 0.09 + i * (x1 - x0 - 0.18) / max(n - 1, 1) + (0.045 if r % 2 else -0.02)
                if x > x1 - 0.06:
                    continue
                if bay == 1 and r == rows - 1 and i > n - 3:
                    continue
                rr = rnd.uniform(0.085, 0.1)
                _split_log(P, f"log{bay}{r}{i}", (x, -0.42 + rnd.uniform(-0.04, 0.04), z), rr, 0.5, rnd.uniform(0, 360), bark, cut, rnd.choice((3, 3, 4)))
        # the row behind, shown only on top
        top = 0.17 + rows * 0.155
        K.box(f"stack_back{bay}", (x1 - x0 - 0.04, 0.6, top - 0.2), bark, loc=((x0 + x1) / 2, 0.15, 0.08 + (top - 0.2) / 2), bev=0, parent=P)
        for i in range(int((x1 - x0) / 0.2)):
            _split_log(P, f"logtop{bay}{i}", (x0 + 0.12 + i * 0.2, 0.15, top - 0.15), 0.09, 0.55, rnd.uniform(0, 360), bark, cut, 4)
    # a chopping block out front with an axe in it, a few splits on the ground, a sack on a nail
    K.cyl("block", 0.26, 0.45, bark, loc=(0.9, -1.35, 0.225), segs=12, bev=0.01, parent=P)
    K.cyl("block_top", 0.24, 0.012, cut, loc=(0.9, -1.35, 0.452), segs=12, bev=0, parent=P)
    axe = K.link(bpy.data.objects.new("axe", None))
    axe.parent = P
    axe.location = (0.95, -1.35, 0.44)
    axe.rotation_euler = (0, math.radians(-35), math.radians(20))
    K.box("axe_head", (0.06, 0.025, 0.16), M(GRAPHITE, metal=0.6, wear=0.6), loc=(0, 0, 0.03), bev=0.006, parent=axe)
    K.cyl("axe_handle", 0.018, 0.7, wood(2, wear=0.05), loc=(-0.36, 0, 0.12), rot=(0, 90, 0), segs=8, bev=0, parent=axe)
    for k in range(3):
        g = _split_log(P, f"loose{k}", (0.3 + k * 0.28, -1.2 + rnd.uniform(-0.15, 0.15), 0.07), 0.085, 0.45, rnd.uniform(0, 360), bark, cut, 4)
        g.rotation_euler[2] = math.radians(rnd.uniform(40, 120))
    sack = M(SACK, rough=0.95, grime=0.45, wear=0.2, scratches=0)
    K.sphere("sack", 0.2, sack, loc=(-W / 2 + 0.02, -D / 2 - 0.12, 1.18), scale=(1.0, 0.55, 1.5), parent=P)
    K.cyl("sack_neck", 0.05, 0.14, sack, loc=(-W / 2 + 0.02, -D / 2 - 0.1, 1.52), segs=8, r2=0.03, bev=0, parent=P)
    K.cyl("sack_tie", 0.04, 0.03, _rope(), loc=(-W / 2 + 0.02, -D / 2 - 0.1, 1.55), segs=8, bev=0, parent=P)
    K.cyl("nail", 0.008, 0.08, M(GRAPHITE, metal=0.6), loc=(-W / 2 + 0.02, -D / 2 - 0.08, 1.6), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    return P


def plank_pile(seed=24):
    """Sawn planks stacked on bearers with stickers between the layers, a tarp half pulled over, two bricks on it."""
    rnd = random.Random(seed)
    P = _root("planks")
    for k, x in enumerate((-1.2, 0.0, 1.2)):
        plank(P, f"bearer{k}", (0.1, 0.95, 0.1), (x, 0, 0.05), rot=(0, 0, rnd.uniform(-3, 3)), shade=3)
    z = 0.1
    layers = 6
    for L in range(layers):
        for i in range(5):
            ln = rnd.choice((3.0, 3.0, 2.8, 2.6))
            plank(P, f"plank{L}{i}", (ln, 0.15, 0.04), (rnd.uniform(-0.08, 0.08) + (3.0 - ln) / 2 * rnd.choice((-1, 1)), -0.34 + i * 0.17, z + 0.02),
                  rot=(0, 0, rnd.uniform(-0.6, 0.6)), shade=rnd.randint(0, 3))
        z += 0.04
        if L < layers - 1:
            for x in (-1.2, 0.0, 1.2):
                plank(P, f"sticker{L}{x}", (0.035, 0.9, 0.025), (x + rnd.uniform(-0.04, 0.04), 0, z + 0.0125), shade=1)
            z += 0.025
    top = z
    # the tarp: over the right two thirds, down the front and back, bunched where it was pulled back
    path = [(-0.47, top - 0.3), (-0.465, top - 0.12), (-0.445, top - 0.01), (-0.39, top + 0.012), (0.0, top + 0.012), (0.39, top + 0.012),
            (0.445, top - 0.01), (0.465, top - 0.12), (0.47, top - 0.26)]
    x0, x1 = 0.1, 1.54
    bm = bmesh.new()
    nu = 16
    grid = []
    for i in range(nu + 1):
        x = x0 + (x1 - x0) * i / nu
        row = []
        for j, (y, zz) in enumerate(path):
            wob = 0.006 * math.sin(x * 13 + j * 1.7) + (0.035 * math.sin(x * 7 + j) if j in (0, len(path) - 1) else 0)
            row.append(bm.verts.new((x, y, zz + wob)))
        grid.append(row)
    for i in range(nu):
        for j in range(len(path) - 1):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    tarp_m = M(SEA, wear=0.45, grime=0.45, rough=0.9, scratches=0.1)
    tarp = K.place(K.from_bm("tarp", bm, tarp_m, smooth=True), parent=P)
    sol = tarp.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = 0.008
    # the bunched-up edge, and the bricks holding the tarp down
    _beam(P, "tarp_roll_a", (0.1, -0.5, top + 0.035), (0.15, 0.0, top + 0.055), 0.05, tarp_m, round_=True, segs=8)
    _beam(P, "tarp_roll_b", (0.15, 0.0, top + 0.055), (0.08, 0.5, top + 0.03), 0.045, tarp_m, round_=True, segs=8)
    brick = M(BRICK, wear=0.5, grime=0.5, rough=0.9, cracks=0.3)
    K.box("brick_a", (0.22, 0.11, 0.065), brick, loc=(0.5, -0.18, top + 0.05), rot=(0, 0, 18), bev=0.008, parent=P)
    K.box("brick_b", (0.22, 0.11, 0.065), brick, loc=(1.25, 0.2, top + 0.05), rot=(0, 0, -64), bev=0.008, parent=P)
    return P


def ladder(seed=25):
    """A long wooden ladder lying across two trestles, one rung replaced with a square batten, a rag tied on."""
    rnd = random.Random(seed)
    P = _root("ladder")
    zt = 0.72
    for k, x in enumerate((-1.05, 1.1)):
        tr = K.link(bpy.data.objects.new(f"trestle{k}", None))
        tr.parent = P
        tr.location = (x, 0, 0)
        tr.rotation_euler = (0, 0, math.radians(rnd.uniform(-6, 6)))
        plank(tr, "beam", (0.1, 0.9, 0.09), (0, 0, zt + 0.045), shade=3)
        for sy in (-1, 1):
            for sx in (-1, 1):
                _beam(tr, f"leg{sx}{sy}", (sx * 0.03, sy * 0.36, zt + 0.03), (sx * 0.27, sy * 0.44, 0), 0.055, wood(2), h=0.04)
            plank(tr, f"brace{sy}", (0.42, 0.025, 0.08), (0, sy * 0.41, 0.28), shade=1)
    L = K.link(bpy.data.objects.new("ladder_body", None))
    L.parent = P
    L.location = (0.05, -0.02, zt + 0.09 + 0.035)
    L.rotation_euler = (0, math.radians(-0.4), math.radians(4))
    length, w = 3.5, 0.44
    for s in (-1, 1):
        plank(L, f"stile{s}", (length, 0.04, 0.07), (0, s * w / 2, 0), shade=0)
    n = int(length / 0.3)
    for i in range(n):
        x = -length / 2 + 0.2 + i * (length - 0.4) / (n - 1)
        if i == 7:
            continue  # a rung gone
        if i == 3:
            plank(L, "rung_patch", (0.035, w + 0.02, 0.03), (x, 0, 0.012), shade=1)
            continue
        K.cyl(f"rung{i}", 0.017, w + 0.03, wood(2, wear=0.05), loc=(x, 0, 0), rot=(90, 0, 0), segs=8, bev=0, parent=L)
    # a rag tied round one stile
    rag = M(SIGNAL, wear=0.5, grime=0.6, rough=0.95, scratches=0)
    K.box("rag", (0.07, 0.06, 0.09), rag, loc=(1.1, -w / 2, 0.0), bev=0.015, parent=L)
    K.box("rag_tail", (0.03, 0.012, 0.16), rag, loc=(1.12, -w / 2 - 0.03, -0.09), rot=(14, 10, 0), bev=0.004, parent=L)
    return P


def wooden_table(seed=26):
    """A rough outdoor trestle table, one salvaged painted board in its top, a tin cup and plate left on it."""
    rnd = random.Random(seed)
    P = _root("table")
    H = 0.76
    for k in range(4):
        y = -0.3 + k * 0.2
        if k == 2:
            K.box("board_patch", (2.0, 0.19, 0.045), M(BONE, wear=0.85, grime=0.45, rough=0.85), loc=(0.02, y, H - 0.0225), bev=0.01, parent=P)
        else:
            plank(P, f"board{k}", (2.0 + rnd.uniform(-0.04, 0.04), 0.19, 0.045), (rnd.uniform(-0.02, 0.02), y, H - 0.0225), rot=(0, 0, rnd.uniform(-0.5, 0.5)),
                  shade=k)
    for s in (-1, 1):
        plank(P, f"cleat{s}", (0.08, 0.76, 0.05), (s * 0.72, 0, H - 0.07), shade=3)
        for sy in (-1, 1):
            _beam(P, f"leg{s}{sy}", (s * 0.72, sy * 0.24, H - 0.09), (s * 0.72, sy * 0.36, 0), 0.07, wood(2), h=0.05)
        plank(P, f"foot{s}", (0.05, 0.62, 0.07), (s * 0.72 + s * 0.05, 0, 0.22), shade=1)
    plank(P, "stretcher", (1.5, 0.06, 0.08), (0, 0, 0.3), shade=2)
    for s in (-1, 1):
        for y in (-0.2, 0.2):
            FR.screw(P, (s * 0.72, y, H + 0.002), (0, 0, 0), r=0.01, mat=M(GRAPHITE, metal=0.6, wear=0.2))
    tin = _tin(ZINC, wear=0.5, grime=0.3)
    K.cyl("cup", 0.04, 0.09, M(SEA, metal=0.3, wear=0.7, grime=0.3), loc=(-0.45, -0.1, H + 0.045), segs=14, bev=0.004, parent=P)
    K.torus("cup_rim", 0.04, 0.004, tin, loc=(-0.45, -0.1, H + 0.09), segs=14, parent=P)
    K.torus("cup_handle", 0.028, 0.006, M(SEA, metal=0.3, wear=0.7), loc=(-0.405, -0.1, H + 0.05), rot=(90, 0, 0), arc=200, segs=12, parent=P).rotation_euler[2] = math.radians(-90)
    K.cyl("plate", 0.12, 0.012, tin, loc=(-0.15, 0.05, H + 0.006), segs=20, bev=0.003, parent=P)
    K.torus("plate_rim", 0.115, 0.007, tin, loc=(-0.15, 0.05, H + 0.013), segs=20, parent=P)
    K.box("crust", (0.08, 0.05, 0.03), K.mat("crust", "#a8834f", rough=0.9, noise_scale=10.0), loc=(-0.13, 0.07, H + 0.025), rot=(0, 0, 30), bev=0.012, parent=P)
    return P


def stool(seed=27):
    """A three-legged milking stool: a round seat with its legs wedged through it."""
    P = _root("stool")
    H = 0.43
    K.cyl("seat", 0.16, 0.045, wood(0), loc=(0, 0, H - 0.0225), segs=18, bev=0.01, parent=P)
    for k in range(3):
        a = math.radians(90 + k * 120)
        top = (math.cos(a) * 0.09, math.sin(a) * 0.09, H - 0.03)
        foot = (math.cos(a) * 0.2, math.sin(a) * 0.2, 0)
        _beam(P, f"leg{k}", foot, top, 0.022, wood(3, wear=0.05), round_=True, segs=8, r2=0.018)
        K.cyl(f"wedge{k}", 0.018, 0.004, wood(1, wear=0.0), loc=(top[0] * 1.03, top[1] * 1.03, H + 0.001), segs=8, bev=0, parent=P)
    return P


def drying_rack(seed=28):
    """A timber A-frame rack, lashed with rope, hung with bundles of herbs and corn cobs."""
    rnd = random.Random(seed)
    P = _root("rack")
    pole = wood(3, wear=0.05)
    zx = 1.62  # where the legs cross
    for x in (-0.95, 0.95):
        for s in (-1, 1):
            _beam(P, f"leg{x}{s}", (x + rnd.uniform(-0.02, 0.02), s * 0.62, 0), (x, -s * 0.09, zx + 0.22), 0.035, pole, round_=True, segs=7)
        _lash(P, (x, 0, zx), 0.04, rot=(90, 0, 0), turns=2, step=0.03)
    _beam(P, "ridge", (-1.2, 0, zx + 0.07), (1.2, 0.01, zx + 0.06), 0.035, pole, round_=True, segs=7)
    zr = 1.0
    yr = 0.62 * (1 - zr / zx) + 0.075
    for s in (-1, 1):
        _beam(P, f"rail{s}", (-1.12, s * yr, zr), (1.12, s * yr, zr + rnd.uniform(-0.02, 0.02)), 0.028, pole, round_=True, segs=7)
        for x in (-0.95, 0.95):
            K.torus("lash", 0.04, 0.009, _rope(), loc=(x, s * (yr - 0.03), zr), rot=(0, 90, 0), segs=12, parent=P)
    herbs = [M(c, wear=0.0, grime=0.15, scratches=0.0, rough=0.9) for c in (HERB, HERB_D, HERB_DRY)]
    corn = M(CORN, wear=0.1, grime=0.15, scratches=0.0, rough=0.7)
    husk = M(HUSK, wear=0.0, grime=0.2, scratches=0.0, rough=0.9)
    twine = _rope()

    def herb(x, y, z):
        drop = rnd.uniform(0.04, 0.1)
        K.cyl("string", 0.004, drop, twine, loc=(x, y, z - drop / 2), segs=4, bev=0, parent=P)
        L = rnd.uniform(0.28, 0.38)
        K.cyl("tie", 0.022, 0.03, twine, loc=(x, y, z - drop - 0.015), segs=8, bev=0, parent=P)
        m = rnd.choice(herbs)
        for k in range(4):
            a = math.radians(k * 90 + rnd.uniform(-30, 30))
            t = rnd.uniform(8, 16)
            g = K.link(bpy.data.objects.new("sprig", None))
            g.parent = P
            g.location = (x, y, z - drop - 0.02)
            g.rotation_euler = (math.radians(t) * math.sin(a), -math.radians(t) * math.cos(a), 0)
            ln = L * rnd.uniform(0.75, 1.0)
            K.cyl("herbs", rnd.uniform(0.03, 0.04), ln, m, loc=(0, 0, -ln / 2), segs=5, bev=0, parent=g, r2=0.008)
        K.cyl("stems", 0.018, 0.06, M(HERB_DRY, wear=0, scratches=0), loc=(x, y, z - drop + 0.02), segs=6, bev=0, parent=P)

    def cobs(x, y, z):
        K.cyl("string", 0.004, 0.06, twine, loc=(x, y, z - 0.03), segs=4, bev=0, parent=P)
        for k in range(3):
            a = math.radians(k * 120 + rnd.uniform(-20, 20))
            g = K.link(bpy.data.objects.new("cob", None))
            g.parent = P
            g.location = (x, y, z - 0.06)
            g.rotation_euler = (math.radians(14) * math.sin(a), -math.radians(14) * math.cos(a), 0)
            K.cyl("husk", 0.03, 0.12, husk, loc=(0, 0, -0.06), segs=5, bev=0, parent=g, r2=0.006)
            K.cyl("corn", 0.032, 0.19, corn, loc=(0, 0, -0.21), segs=8, bev=0, parent=g, r2=0.02).rotation_euler = (math.pi, 0, 0)

    for k, x in enumerate((-0.75, -0.42, -0.1, 0.25, 0.58)):
        herb(x + rnd.uniform(-0.04, 0.04), 0, zx + 0.035)
    for s in (-1, 1):
        for k, x in enumerate((-0.7, -0.25, 0.2, 0.65)):
            if (k + (s > 0)) % 2:
                cobs(x + rnd.uniform(-0.05, 0.05), s * yr, zr - 0.028)
            else:
                herb(x + rnd.uniform(-0.05, 0.05), s * yr, zr - 0.028)
    return P


def mailbox_post(seed=29):
    """A dented tin mailbox on a crooked post, its little red flag up."""
    P = _root("mailbox")
    post(P, "post", (0, 0.05), 1.18, r=0.05, tilt=(4, -3))
    top = K.link(bpy.data.objects.new("top", None))
    top.parent = P
    top.location = (-0.03, 0.0, 1.06)
    top.rotation_euler = (math.radians(4), math.radians(-3), math.radians(5))
    plank(top, "shelf", (0.16, 0.5, 0.035), (0, 0, 0.0175), shade=1)
    _beam(top, "brace", (0, 0.08, -0.33), (0, -0.2, -0.0), 0.04, wood(2), h=0.03)
    # the box: a flat-sided body with a round roof, pushed in where something hit it
    bm = bmesh.new()
    prof = [(-0.09, 0.0), (0.09, 0.0), (0.09, 0.045), (0.09, 0.09)]
    prof += [(0.09 * math.cos(math.radians(a)), 0.13 + 0.09 * math.sin(math.radians(a))) for a in range(0, 181, 18)]
    prof += [(-0.09, 0.09), (-0.09, 0.045)]
    rings = []
    ny = 8
    for j in range(ny + 1):
        y = -0.24 + 0.48 * j / ny
        ring = []
        for (x, z) in prof:
            dx, dz, dy = x - 0.075, z - 0.17, y - 0.06
            d = math.exp(-(dx * dx + dz * dz) / 0.0016 - dy * dy / 0.006)
            ring.append(bm.verts.new((x - 0.022 * d * (1 if x > 0 else 0), y, z - 0.012 * d)))
        rings.append(ring)
    n = len(prof)
    for j in range(ny):
        for i in range(n):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % n], rings[j + 1][(i + 1) % n], rings[j + 1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    tin = _tin(ZINC, wear=0.75, grime=0.55, scratches=0.6)
    box = K.place(K.from_bm("mailbox", bm, tin, smooth=False), (0, 0, 0.036), parent=top)
    K.bevel(box, 0.004, segs=1, angle=50)
    # the door at the front, hinged at the bottom, and its latch
    bm = bmesh.new()
    front = [bm.verts.new((x * 1.06, -0.25, z * 1.03 - 0.002)) for (x, z) in prof]
    back = [bm.verts.new((x * 1.06, -0.262, z * 1.03 - 0.002)) for (x, z) in prof]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        bm.faces.new((front[i], back[i], back[(i + 1) % n], front[(i + 1) % n]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    K.place(K.from_bm("door", bm, M(BONE, metal=0.3, wear=0.8, grime=0.5)), (0, 0, 0.036), parent=top)
    K.box("latch", (0.03, 0.02, 0.04), M(GRAPHITE, metal=0.6), loc=(0, -0.27, 0.036 + 0.2), bev=0.004, parent=top)
    K.cyl("hinge", 0.008, 0.16, M(GRAPHITE, metal=0.6), loc=(0, -0.255, 0.04), rot=(0, 90, 0), segs=6, bev=0, parent=top)
    # the flag on its arm, standing up
    K.cyl("pivot", 0.012, 0.012, M(GRAPHITE, metal=0.6), loc=(0.098, 0.1, 0.13), rot=(0, 90, 0), segs=8, bev=0, parent=top)
    K.box("flag_arm", (0.008, 0.02, 0.24), M(SIGNAL, metal=0.3, wear=0.6), loc=(0.104, 0.1, 0.24), rot=(6, 0, 0), bev=0.002, parent=top)
    K.box("flag", (0.008, 0.08, 0.06), M(SIGNAL, metal=0.3, wear=0.6), loc=(0.104, 0.13, 0.33), rot=(6, 0, 0), bev=0.002, parent=top)
    for y in (-0.15, 0.15):
        bolt(top, (0.0, y, 0.04), rot=(0, 0, 0))
    # stones packed round the foot of the post
    rnd = random.Random(seed)
    for k in range(5):
        a = k * 72 + rnd.uniform(-20, 20)
        K.sphere(f"stone{k}", rnd.uniform(0.06, 0.09), M(STONE, rough=0.9, moss=0.5), loc=(math.cos(math.radians(a)) * 0.13, 0.05 + math.sin(math.radians(a)) * 0.13, 0.02),
                 scale=(1.2, 1.0, 0.65), parent=P)
    return P


def lantern_post(seed=30):
    """A timber post with a braced arm and an unlit iron lantern hanging from a short chain, a hook lower down."""
    P = _root("lanternpost")
    rnd = random.Random(seed)
    post(P, "post", (0, 0), 2.7, r=0.07)
    zarm = 2.42
    plank(P, "arm", (0.82, 0.08, 0.1), (0.44, 0, zarm), shade=1)
    _beam(P, "brace", (0.07, 0, zarm - 0.5), (0.45, 0, zarm - 0.04), 0.06, wood(2), h=0.05)
    for z in (zarm, zarm - 0.5):
        bolt(P, (0.0, -0.075, z), rot=(90, 0, 0))
    iron = M(GRAPHITE, metal=0.6, wear=0.45, grime=0.4)
    K.box("strap", (0.12, 0.09, 0.012), iron, loc=(0.75, 0, zarm - 0.054), bev=0.002, parent=P)
    # hook and chain
    K.torus("hook", 0.025, 0.006, iron, loc=(0.75, 0, zarm - 0.085), rot=(90, 0, 0), arc=270, segs=12, parent=P).rotation_euler[1] = math.radians(135)
    z = zarm - 0.12
    for k in range(3):
        K.torus(f"link{k}", 0.022, 0.005, iron, loc=(0.75, 0, z), rot=(90 * (k % 2), 90, 0), segs=10, parent=P)
        z -= 0.038
    # the lantern: base, corner posts, dark glass, a pyramid cap, chimney and bail ring
    L = K.link(bpy.data.objects.new("lantern", None))
    L.parent = P
    L.location = (0.75, 0, z - 0.06)
    L.rotation_euler = (0, 0, math.radians(rnd.uniform(10, 25)))
    K.torus("bail", 0.05, 0.005, iron, loc=(0, 0, 0.02), rot=(90, 0, 0), segs=12, parent=L)
    K.cyl("chimney", 0.03, 0.06, iron, loc=(0, 0, -0.04), segs=8, bev=0, parent=L)
    K.cyl("vent", 0.045, 0.02, iron, loc=(0, 0, -0.005), segs=8, bev=0, parent=L)
    K.cyl("cap", 0.13, 0.09, iron, loc=(0, 0, -0.11), rot=(0, 0, 45), segs=4, bev=0.004, parent=L, r2=0.035)
    K.box("top_plate", (0.17, 0.17, 0.015), iron, loc=(0, 0, -0.16), bev=0.003, parent=L)
    K.box("base", (0.18, 0.18, 0.03), iron, loc=(0, 0, -0.42), bev=0.004, parent=L)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("corner", (0.015, 0.015, 0.25), iron, loc=(sx * 0.078, sy * 0.078, -0.29), bev=0, parent=L)
    K.box("glass", (0.15, 0.15, 0.24), K.mat("lantern_glass", GLASS, rough=0.15, metal=0.1, grime=0.4), loc=(0, 0, -0.29), bev=0, parent=L)
    K.cyl("candle", 0.022, 0.07, M(BONE, wear=0.2, scratches=0), loc=(0, 0, -0.37), segs=8, bev=0, parent=L)
    for sx in (-1, 1):
        K.box("guard", (0.008, 0.17, 0.008), iron, loc=(sx * 0.08, 0, -0.26), rot=(0, 0, 0), bev=0, parent=L)
    # a small cup hook on the post with a coil of rope hung on it
    K.cyl("hook_stem", 0.006, 0.05, iron, loc=(0, -0.095, 1.45), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    K.torus("cup_hook", 0.02, 0.006, iron, loc=(0, -0.12, 1.43), rot=(90, 0, 90), arc=200, segs=10, parent=P).rotation_euler = (0, math.radians(90), math.radians(90))
    K.torus("coil", 0.12, 0.016, _rope(), loc=(0, -0.13, 1.32), rot=(90, 0, 0), segs=20, parent=P)
    K.torus("coil2", 0.11, 0.016, _rope(), loc=(0.01, -0.15, 1.33), rot=(84, 6, 0), segs=20, parent=P)
    for k in range(4):
        a = k * 90 + rnd.uniform(-25, 25)
        K.sphere(f"stone{k}", rnd.uniform(0.07, 0.1), M(STONE, rough=0.9, moss=0.5), loc=(math.cos(math.radians(a)) * 0.15, math.sin(math.radians(a)) * 0.15, 0.02),
                 scale=(1.2, 1.0, 0.6), parent=P)
    return P


def water_tower_small(seed=31):
    """A settler water tank: a stave tank in steel hoops with a tin roof on a braced four-leg timber stand, a ladder up."""
    rnd = random.Random(seed)
    P = _root("watertower")
    HP = 3.3
    leg = wood(3, wear=0.4)
    corners = [(sx, sy) for sx in (-1, 1) for sy in (-1, 1)]
    for sx, sy in corners:
        _beam(P, f"leg{sx}{sy}", (sx * 0.9, sy * 0.9, -0.05), (sx * 0.68, sy * 0.68, HP), 0.16, leg, bev=0.015)
        K.box(f"pad{sx}{sy}", (0.34, 0.34, 0.12), M(STONE, rough=0.9, moss=0.5, cracks=0.3), loc=(sx * 0.9, sy * 0.9, 0.04), rot=(0, 0, rnd.uniform(-8, 8)), bev=0.03,
              parent=P)

    def at(s, z):
        return 0.9 - 0.22 * z / HP

    for z in (1.6, HP - 0.1):
        r = at(1, z) + 0.1
        for s in (-1, 1):
            plank(P, f"girt_x{z}{s}", (2 * r + 0.1, 0.05, 0.14), (0, s * r, z), shade=1)
            plank(P, f"girt_y{z}{s}", (0.05, 2 * r + 0.1, 0.14), (s * r, 0, z), shade=1)
    # X braces on the sides and back, a single brace on the front so the ladder stands clear
    for face in ("x-", "x+", "y+", "y-"):
        for (z0, z1) in ((0.3, 1.55), (1.65, HP - 0.15)):
            r0, r1 = at(1, z0) + 0.11, at(1, z1) + 0.11
            for d in ((-1, 1), (1, -1)):
                if face == "y-" and d == (1, -1):
                    continue
                if face[0] == "x":
                    s = -1 if face[1] == "-" else 1
                    a, b = (s * r0, d[0] * r0, z0), (s * r1, d[1] * r1, z1)
                else:
                    s = -1 if face[1] == "-" else 1
                    a, b = (d[0] * r0, s * r0, z0), (d[1] * r1, s * r1, z1)
                _beam(P, f"brace{face}", a, b, 0.1, wood(2), h=0.04)
    # platform: joists and boards
    for k in (-0.55, 0.55):
        plank(P, f"joist{k}", (1.8, 0.1, 0.12), (0, k, HP + 0.06), shade=3)
    for k in range(8):
        plank(P, f"deck{k}", (0.22, 1.9, 0.04), (-0.84 + k * 0.24, 0, HP + 0.14), rot=(0, 0, rnd.uniform(-1, 1)), shade=k)
    # the tank: staves in three hoops, a tin roof with a hatch
    zt, TH, TR = HP + 0.16, 1.45, 0.82
    n = 16
    for k in range(n):
        a = 2 * math.pi * k / n
        K.box(f"stave{k}", (2 * math.pi * TR / n + 0.01, 0.06, TH - rnd.uniform(0, 0.03)), wood(k % 4, wear=0.35),
              loc=(math.cos(a) * TR, math.sin(a) * TR, zt + TH / 2), rot=(0, 0, math.degrees(a) + 90), bev=0.008, parent=P)
    hoop = M(RUST, metal=0.5, wear=0.55, grime=0.5)
    for z in (0.2, 0.75, 1.3):
        K.torus(f"hoop{z}", TR + 0.04, 0.018, hoop, loc=(0, 0, zt + z), segs=20, parent=P)
    K.cyl("floor", TR, 0.05, wood(3), loc=(0, 0, zt + 0.06), segs=18, bev=0, parent=P)
    K.cyl("roof", TR + 0.14, 0.42, _tin(ZINC, wear=0.7, grime=0.5), loc=(0, 0, zt + TH + 0.2), segs=18, bev=0.01, parent=P, r2=0.1)
    K.box("roof_patch", (0.35, 0.3, 0.01), _tin(SEA, wear=0.7), loc=(0.42, 0.25, zt + TH + 0.15), rot=(-15, 14, 30), bev=0.002, parent=P)
    K.cyl("finial", 0.1, 0.08, _tin(GRAPHITE), loc=(0, 0, zt + TH + 0.44), segs=10, bev=0.01, parent=P)
    K.box("hatch", (0.3, 0.26, 0.04), wood(1), loc=(0, -0.48, zt + TH + 0.12), rot=(-27, 0, 0), bev=0.008, parent=P)
    # outlet pipe down a leg to a tap over a bucket
    iron = M(GRAPHITE, metal=0.6, wear=0.5, grime=0.4)
    px, py = 0.55, -0.55
    K.cyl("pipe_out", 0.03, 0.4, iron, loc=(px - 0.1, py + 0.1, zt - 0.05), segs=8, bev=0, parent=P)
    _beam(P, "pipe_down", (px - 0.1, py + 0.1, zt - 0.2), (px + 0.22, py - 0.22, 0.75), 0.03, iron, round_=True, segs=8)
    K.cyl("tap", 0.04, 0.08, M(BRICK, metal=0.3, wear=0.6), loc=(px + 0.22, py - 0.22, 0.73), segs=8, bev=0.005, parent=P)
    K.box("tap_handle", (0.14, 0.02, 0.02), M(SIGNAL, metal=0.3, wear=0.6), loc=(px + 0.22, py - 0.22, 0.79), bev=0.004, parent=P)
    K.cyl("bucket", 0.14, 0.26, _tin(ZINC), loc=(px + 0.24, py - 0.26, 0.13), segs=10, r2=0.16, bev=0.01, parent=P)
    K.torus("bucket_bail", 0.15, 0.005, iron, loc=(px + 0.24, py - 0.26, 0.26), rot=(90, 0, 20), arc=180, segs=12, parent=P)
    # the ladder up the front, between the front legs
    lad = K.link(bpy.data.objects.new("ladder", None))
    lad.parent = P
    lad.location = (-0.12, -1.18, 0)
    lad.rotation_euler = (math.radians(-5.5), 0, 0)
    LH = 3.9
    for s in (-1, 1):
        K.box(f"stile{s}", (0.04, 0.07, LH), wood(0), loc=(s * 0.22, 0, LH / 2), bev=0.008, parent=lad)
    for i in range(11):
        K.cyl(f"rung{i}", 0.017, 0.47, wood(2, wear=0.05), loc=(0, 0, 0.3 + i * 0.3), rot=(0, 90, 0), segs=6, bev=0, parent=lad)
    return P


def pole_fence(seed=32):
    """A 3 m run of crooked hand-split pole fence: three posts, three cross poles a bay lashed on with rope, tin cans on
    a string to scare the birds."""
    rnd = random.Random(seed)
    P = _root("polefence")
    bark = K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    split = wood(3, wear=0.05)
    posts = []
    for k, x in enumerate((-1.45, 0.0, 1.45)):
        tx, ty = rnd.uniform(-4, 4), rnd.uniform(-5, 5)
        h = rnd.uniform(1.25, 1.38)
        a = Vector((x, 0, -0.1))
        b = Vector((x + math.sin(math.radians(ty)) * h, -math.sin(math.radians(tx)) * h, h - 0.1))
        _beam(P, f"post{k}", a, b, rnd.uniform(0.055, 0.065), split if k != 1 else bark, round_=True, segs=6, r2=0.05)
        posts.append((a, b))

    def on_post(k, z):
        a, b = posts[k]
        return a + (b - a) * ((z - a.z) / (b.z - a.z))

    for bay in range(2):
        for j, z in enumerate((0.38, 0.72, 1.06)):
            pa = on_post(bay, z + rnd.uniform(-0.04, 0.04)) + Vector((-0.12, -0.09, 0))
            pb = on_post(bay + 1, z + rnd.uniform(-0.04, 0.04)) + Vector((0.12, -0.09, 0))
            mid = (pa + pb) / 2 + Vector((0, rnd.uniform(-0.04, 0.03), rnd.uniform(-0.05, 0.04)))
            r = rnd.uniform(0.035, 0.045)
            m = bark if rnd.random() < 0.5 else split
            _beam(P, f"pole{bay}{j}a", pa, mid, r, m, round_=True, segs=6, r2=r * 0.95)
            _beam(P, f"pole{bay}{j}b", mid, pb, r * 0.95, m, round_=True, segs=6, r2=r * 0.85)
    for k in range(3):
        for z in (0.38, 0.72, 1.06):
            c = on_post(k, z)
            _lash(P, (c.x, c.y - 0.02, c.z), 0.065, rot=(0, 25 if k % 2 else -25, 0), turns=2, step=0.03)
    # tin cans hung on twine from the top pole to rattle in the wind, a strip of rag beside them
    for k, x in enumerate((-0.55, 0.85)):
        c = on_post(0, 1.06)
        y = -0.1
        drop = 0.18 + 0.06 * k
        K.cyl(f"twine{k}", 0.003, drop, _rope(), loc=(x, y, 1.06 - drop / 2), segs=4, bev=0, parent=P)
        K.cyl(f"can{k}", 0.037, 0.11, _tin(ZINC if k else RUST, wear=0.7), loc=(x, y, 1.06 - drop - 0.05), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), 0),
              segs=12, bev=0.004, parent=P)
    K.box("rag", (0.06, 0.008, 0.3), M(SIGNAL, wear=0.5, grime=0.6, rough=0.95, scratches=0), loc=(0.2, -0.1, 0.92), rot=(0, 8, 0), bev=0.002, parent=P)
    return P


PROPS = {
    "beehive_boxes": beehive_boxes,
    "compost_heap": compost_heap,
    "woodshed": woodshed,
    "plank_pile": plank_pile,
    "ladder": ladder,
    "wooden_table": wooden_table,
    "stool": stool,
    "drying_rack": drying_rack,
    "mailbox_post": mailbox_post,
    "lantern_post": lantern_post,
    "water_tower_small": water_tower_small,
    "pole_fence": pole_fence,
}
