"""Settler Patchwork props, set A, for the scenes between places (docs/world-generation.md, Scenes): the things a
farmstead or a camp leaves about, in the Settler Patchwork family of docs/art-direction.md (scavenged wood,
corrugated sheet patches, rope, sacking and the faded colony colours).

A sack pile on a pallet, a wheelbarrow, an outhouse, a hand-pump well, a wind pump, a chicken coop, a picket fence, a
five-bar farm gate, a campfire ring, a cooking tripod, a washing line and a rain barrel.

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
from families import M, BONE, MUSTARD, GRAPHITE, SEA, BRICK, RUST, ZINC, GUNMETAL, shell_panel, stone
from props import _root, wood, plank, bolt, CHAR, BARK, HEART

SACK, SACK_D, SACK_L = "#c2a878", "#a8916a", "#cdb98e"
GRAIN = "#cfb878"
ROPE = "#b59b6a"
SOIL = "#5a3f2a"
ASH = "#3a3530"
ROCK = "#80827a"


# ---------------------------------------------------------------- helpers
def _orient(ob, p0, p1, axis="Z"):
    """Centres ob between p0 and p1 with its local axis along them."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    ob.location = (a + b) / 2
    if axis == "Z":
        up = "Y" if abs(d.normalized().y) < 0.9 else "X"
    else:
        up = "Z"
    ob.rotation_euler = d.to_track_quat(axis, up).to_euler()
    return ob


def rod(P, name, p0, p1, r, mat, segs=8, bev=0.0):
    """A round bar (tube, stick, rope) from p0 to p1."""
    return _orient(K.cyl(name, r, math.dist(p0, p1), mat, segs=segs, bev=bev, parent=P), p0, p1)


def beam(P, name, p0, p1, w, t, mat, bev=0.01):
    """A sawn timber from p0 to p1, w across its local x and t across its local y."""
    return _orient(K.box(name, (w, t, math.dist(p0, p1)), mat, bev=bev, parent=P), p0, p1)


def prism(P, name, pts, t, mat, loc=(0, 0, 0), rot=(0, 0, 0), plane="xz", bev=0.006):
    """A flat board cut to an outline: pts in the plane, t thick across the remaining axis."""
    bm = bmesh.new()

    def v(a, b, s):
        return bm.verts.new((a, s, b) if plane == "xz" else (s, a, b))

    lo = [v(a, b, -t / 2) for a, b in pts]
    hi = [v(a, b, t / 2) for a, b in pts]
    if plane == "xz":
        bm.faces.new(list(reversed(hi)))
        bm.faces.new(lo)
    else:
        bm.faces.new(list(reversed(lo)))
        bm.faces.new(hi)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2, angle=40)
    return K.place(ob, loc, rot, P)


def lump(P, name, r, loc, scale, mat, rnd, jitter=0.12, rot=(0, 0, 0), u=10, v=7, flat=None):
    """A lumpy round thing (a field stone, a heap of soil or grain); flat cuts its underside level."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    for vt in bm.verts:
        vt.co *= 1 + rnd.uniform(-jitter, jitter)
        if flat is not None and vt.co.z < -flat * r:
            vt.co.z = -flat * r
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.scale = scale
    return K.place(ob, loc, rot, P)


def corrugated(P, name, w, d, mat, loc=(0, 0, 0), rot=(0, 0, 0), basis=None, pitch=0.1, amp=0.014, t=0.004):
    """A corrugated sheet w by d in its local xy, ridges along y, the outside towards +z."""
    bm = bmesh.new()
    n = max(4, int(round(w / pitch * 4)))
    top0, top1, bot0, bot1 = [], [], [], []
    for i in range(n + 1):
        x = -w / 2 + w * i / n
        z = amp * math.sin(2 * math.pi * x / pitch)
        top0.append(bm.verts.new((x, -d / 2, z)))
        top1.append(bm.verts.new((x, d / 2, z)))
        bot0.append(bm.verts.new((x, -d / 2, z - t)))
        bot1.append(bm.verts.new((x, d / 2, z - t)))
    for i in range(n):
        bm.faces.new((top0[i], top0[i + 1], top1[i + 1], top1[i]))
        bm.faces.new((bot0[i], bot1[i], bot1[i + 1], bot0[i + 1]))
        bm.faces.new((top0[i], bot0[i], bot0[i + 1], top0[i + 1]))
        bm.faces.new((top1[i], top1[i + 1], bot1[i + 1], bot1[i]))
    bm.faces.new((top0[0], top1[0], bot1[0], bot0[0]))
    bm.faces.new((top0[-1], bot0[-1], bot1[-1], top1[-1]))
    ob = K.from_bm(name, bm, mat, smooth=True)
    K.place(ob, loc, rot, P)
    if basis is not None:
        xv, yv, zv = (Vector(c) for c in basis)
        ob.rotation_euler = Matrix((xv, yv, zv)).transposed().to_euler()
    return ob


def _offset(P, loc):
    """An inner empty that shifts a prop so its root sits at the centre of what was built."""
    G = K.link(bpy.data.objects.new(f"{P.name}_offset", None))
    G.parent = P
    G.location = loc
    return G


def tin(color=ZINC, **kw):
    kw.setdefault("metal", 0.45)
    kw.setdefault("wear", 0.6)
    kw.setdefault("grime", 0.45)
    return M(color, **kw)


def rope_mat():
    return K.mat("rope", ROPE, rough=0.95, grime=0.3, noise_scale=14.0)


def stone_ring(P, name, c, r, n, size, rnd, mat=None):
    """Field stones laid in a ring round a fire."""
    mat = mat or K.mat("fieldstone", ROCK, rough=0.9, grime=0.3, moss=0.4, cracks=0.3, noise_scale=4.0)
    cx, cy = c
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.12, 0.12)
        s = size * rnd.uniform(0.8, 1.2)
        lump(P, f"{name}{k}", s, (cx + math.cos(a) * r, cy + math.sin(a) * r, s * 0.35), (1.2, 0.95, 0.85), mat, rnd,
             jitter=0.14, rot=(0, 0, math.degrees(a) + rnd.uniform(-20, 20)), u=8, v=6, flat=0.45)


# ================================================================== sack pile
def sack(P, name, size, loc, rot, mat, rnd, tied=True):
    """A grain sack lying on its side: a lumpy pillow, its origin at the middle of its underside, the neck at +x."""
    sx, sy, sz = size
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=1.0)
    ph = [rnd.uniform(0, 6.3) for _ in range(3)]
    for vt in bm.verts:
        x, y, z = vt.co
        px = math.copysign(abs(x) ** 0.6, x)
        py = math.copysign(abs(y) ** 0.8, y)
        lumpy = 1 + 0.07 * math.sin(3 * x + ph[0]) * math.cos(4 * y + ph[1]) + 0.05 * math.sin(5 * z + ph[2])
        zz = z if z > -0.45 else -0.45 + (z + 0.45) * 0.25  # flattened where it rests
        vt.co = Vector((px * sx / 2 * lumpy, py * sy / 2 * lumpy, (zz + 0.6) / 1.6 * sz * lumpy))
    ob = K.place(K.from_bm(name, bm, mat, smooth=True), loc, rot, P)
    if tied:
        # the gathered neck and its twine
        K.cyl(f"{name}_neck", sz * 0.28, 0.12, mat, loc=(sx / 2 + 0.03, 0, sz * 0.5), rot=(0, 90, 0), segs=10, r2=sz * 0.16, bev=0.01, parent=ob)
        K.torus(f"{name}_tie", sz * 0.2, 0.012, rope_mat(), loc=(sx / 2 + 0.06, 0, sz * 0.5), rot=(0, 90, 0), segs=12, parent=ob)
        K.cyl(f"{name}_tuft", sz * 0.12, 0.07, mat, loc=(sx / 2 + 0.11, 0, sz * 0.5), rot=(0, 90, 0), segs=8, r2=sz * 0.24, bev=0, parent=ob)
    return ob


def sack_pile(seed=21):
    """Six tied grain sacks heaped on a pallet, a seventh slumped open beside it with grain spilling."""
    rnd = random.Random(seed)
    R = _root("sacks")
    P = _offset(R, (-0.22, 0, 0))
    # the pallet: runners, blocks, bearers and a deck, one deck board split short
    for y in (-0.35, 0, 0.35):
        plank(P, "runner", (1.2, 0.1, 0.022), (0, y, 0.011), shade=3)
        for x in (-0.55, 0, 0.55):
            K.box("block", (0.1, 0.1, 0.078), wood(1), loc=(x, y, 0.061), bev=0.008, parent=P)
    for x in (-0.55, 0, 0.55):
        plank(P, "bearer", (0.1, 0.8, 0.022), (x, 0, 0.111), shade=2)
    for k, y in enumerate((-0.34, -0.17, 0, 0.17, 0.34)):
        L = 0.7 if k == 3 else 1.2
        plank(P, f"deck{k}", (L, 0.12, 0.022), (-(1.2 - L) / 2, y, 0.133), shade=k)
    top = 0.144
    mats = [K.mat(f"sack{k}", c, rough=0.95, grime=0.4, noise_scale=9.0) for k, c in enumerate((SACK, SACK_D, SACK_L))]
    # bottom row of three lengthwise, two on them, one across the top
    for k, x in enumerate((-0.4, 0.0, 0.4)):
        sack(P, f"sack_b{k}", (0.78, 0.46, 0.24), (x, rnd.uniform(-0.04, 0.04), top - 0.01), (rnd.uniform(-3, 3), rnd.uniform(-4, 4), 90 + rnd.uniform(-6, 6)),
             mats[k % 3], rnd)
    for k, x in enumerate((-0.2, 0.22)):
        sack(P, f"sack_m{k}", (0.76, 0.45, 0.24), (x, rnd.uniform(-0.05, 0.05), top + 0.17), (rnd.uniform(-5, 5), (-1) ** k * 9, 90 + rnd.uniform(-10, 10)),
             mats[(k + 1) % 3], rnd)
    s = sack(P, "sack_top", (0.74, 0.44, 0.23), (0.02, 0.02, top + 0.34), (0, rnd.uniform(-4, 4), 8), mats[2], rnd)
    # a patch of teal canvas sewn on the top sack
    K.box("patch", (0.15, 0.13, 0.012), M(SEA, wear=0.4, rough=0.95), loc=(0.08, 0.03, 0.205), rot=(0, 0, 14), bev=0.003, parent=s)
    # the open one, slumped against the pile, its mouth rolled down over the grain
    ox, oy = 0.78, -0.4
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=9, radius=1.0)
    for vt in bm.verts:
        x, y, z = vt.co
        sag = 1 + 0.18 * max(0.0, -z)  # it bellies out where it slumped
        vt.co = Vector((x * 0.19 * sag, y * 0.17 * sag, (z + 1) * 0.25 if z > -0.6 else (0.4 + (z + 0.6) * 0.2) * 0.25))
    body = K.place(K.from_bm("sack_open", bm, mats[0], smooth=True), (ox, oy, 0), (8, -10, 20), P)
    K.torus("mouth", 0.16, 0.03, mats[1], loc=(0, 0, 0.48), rot=(0, 0, 0), segs=16, parent=body)
    grain = K.mat("grain", GRAIN, rough=0.95, grime=0.15, noise_scale=30.0)
    lump(P, "grain_in", 0.15, (0, 0, 0.46), (1.0, 0.95, 0.35), grain, rnd, jitter=0.05, flat=0.2).parent = body
    lump(P, "spill", 0.22, (ox + 0.08, oy - 0.3, 0.0), (1.0, 0.7, 0.25), grain, rnd, jitter=0.1, flat=0.0)
    return R


# ================================================================== wheelbarrow
def wheelbarrow(seed=22):
    """A wooden-bed barrow on a steel tube frame, one wheel, resting on its legs and settled a little into the
    ground, some soil left in the bed."""
    rnd = random.Random(seed)
    P = _root("barrow")
    B = K.link(bpy.data.objects.new("barrow_body", None))
    B.parent = P
    B.location = (0, 0.1, -0.014)
    B.rotation_euler = (0, math.radians(3), math.radians(7))
    tube = M(SEA, metal=0.5, wear=0.75, grime=0.45, scratches=0.6)
    dark = M(GRAPHITE, metal=0.5, wear=0.4)
    # the wheel at the front
    wy, wz = -0.68, 0.2
    K.torus("tyre", 0.165, 0.042, K.mat("tyre", "#26282a", rough=0.9, grime=0.3), loc=(0, wy, wz), rot=(0, 90, 0), segs=24, parent=B)
    K.cyl("rim", 0.13, 0.06, tin(ZINC), loc=(0, wy, wz), rot=(0, 90, 0), segs=16, bev=0.008, parent=B)
    K.cyl("hub", 0.04, 0.12, dark, loc=(0, wy, wz), rot=(0, 90, 0), segs=10, bev=0.005, parent=B)
    rod(B, "axle", (-0.11, wy, wz), (0.11, wy, wz), 0.012, dark)
    # handles: one bent tube each side from the axle back past the bed to the grips
    for s in (-1, 1):
        a = (s * 0.1, wy, wz)
        b = (s * 0.24, -0.3, 0.33)
        c = (s * 0.27, 0.62, 0.5)
        rod(B, f"handle_a{s}", a, b, 0.017, tube)
        rod(B, f"handle_b{s}", b, c, 0.017, tube)
        K.sphere(f"bend{s}", 0.017, tube, loc=b, parent=B)
        rod(B, f"grip{s}", c, (s * 0.28, 0.82, 0.54), 0.022, dark, segs=10)
        # legs down from the handle to splayed feet, braced back to the handle
        top = (s * 0.26, 0.28, 0.44)
        foot = (s * 0.27, 0.36, 0.01)
        rod(B, f"leg{s}", top, foot, 0.015, tube)
        K.box(f"foot{s}", (0.05, 0.1, 0.012), tube, loc=(foot[0], foot[1], 0.006), bev=0.003, parent=B)
        rod(B, f"stay{s}", (s * 0.265, 0.34, 0.08), (s * 0.245, -0.12, 0.36), 0.01, tube)
    # bearers across the handles carry the bed level
    plank(B, "bearer_f", (0.5, 0.06, 0.08), (0, -0.27, 0.42), shade=3)
    plank(B, "bearer_r", (0.6, 0.06, 0.05), (0, 0.3, 0.465), shade=3)
    fz = 0.5
    for k, x in enumerate((-0.19, -0.065, 0.065, 0.19)):
        plank(B, f"floor{k}", (0.12, 0.62, 0.025), (x, 0, fz), shade=k)
    t = 0.025
    for s in (-1, 1):
        prism(B, f"side{s}", [(-0.31, 0), (0.31, 0), (0.34, 0.28), (-0.55, 0.28)], t, wood(rnd.randrange(4)),
              loc=(s * 0.26, 0, fz), rot=(0, s * 15, 0), plane="yz")
        K.box(f"strap{s}", (0.006, 0.04, 0.2), M(RUST, metal=0.5, wear=0.6), loc=(s * 0.29, -0.27, fz + 0.1), rot=(0, s * 15, 0), bev=0.002, parent=B)
    prism(B, "front", [(-0.27, 0), (0.27, 0), (0.35, 0.37), (-0.35, 0.37)], t, wood(1), loc=(0, -0.31, fz), rot=(41, 0, 0))
    # a salvaged board of another colour for the back
    prism(B, "back", [(-0.27, 0), (0.27, 0), (0.34, 0.28), (-0.34, 0.28)], t, M(BRICK, wear=0.75, grime=0.4, rough=0.85),
          loc=(0, 0.32, fz), rot=(-6, 0, 0))
    for x in (-0.2, 0.2):
        bolt(B, (x, 0.30, fz + 0.12), rot=(96, 0, 0))
    lump(B, "soil", 0.3, (0, -0.06, fz + 0.012), (0.72, 0.85, 0.4), K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), rnd, jitter=0.1, flat=0.0)
    return P


# ================================================================== outhouse
def outhouse(seed=23):
    """A narrow plank privy on skids: framed walls of mismatched boards, a zinc patch, a slanted roof of two odd
    corrugated sheets and a door with a crescent cut standing ajar."""
    rnd = random.Random(seed)
    P = _root("outhouse")
    hw = 0.6  # half width and depth
    zf, zb = 2.2, 1.95  # roof line front and back

    def roof(y):
        return zf + (zb - zf) * (y + hw) / (2 * hw)

    for x in (-0.45, 0.45):
        plank(P, "skid", (0.12, 1.36, 0.1), (x, 0, 0.05), shade=3)
    plank(P, "floor", (1.2, 1.2, 0.04), (0, 0, 0.12), shade=1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = roof(sy * 0.55) - 0.14
            K.box("post", (0.09, 0.09, h), wood(3), loc=(sx * 0.55, sy * 0.55, 0.14 + h / 2), bev=0.01, parent=P)
    for sy in (-1, 1):
        plank(P, "plate", (1.2, 0.09, 0.09), (0, sy * 0.55, roof(sy * 0.55) - 0.05), shade=3)
    t, step = 0.022, 0.152
    odd = {5: M(SEA, wear=0.75, grime=0.45, rough=0.85), 14: M(BRICK, wear=0.75, grime=0.45, rough=0.85), 3: M(SEA, wear=0.75, grime=0.45, rough=0.85)}
    n = 0

    def board(name, x, y, zt, along):
        nonlocal n
        n += 1
        h = zt - 0.1
        m = odd.get(n) or wood(rnd.randrange(4), wear=0.6)
        size = (step - 0.012, t, h) if along == "x" else (t, step - 0.012, h)
        K.box(name, size, m, loc=(x, y, 0.1 + h / 2), rot=(0, 0, 0), bev=0.006, parent=P)

    o = hw + 0.011 - 0.0  # boards outside the frame
    for k in range(8):
        y = -0.532 + k * step
        board("side_l", -o, y, roof(y) - 0.02, "y")
        board("side_r", o, y, roof(y) - 0.02, "y")
        board("back", -0.532 + k * step, o, zb - 0.02, "x")
    for x in (-0.532, -0.38, 0.38, 0.532):
        board("front", x, -o, zf - 0.02, "x")
    plank(P, "header", (0.62, t, 0.2), (0, -o, 2.06), shade=2)
    for s in (-1, 1):
        plank(P, "jamb", (0.05, 0.03, 1.92), (s * 0.32, -o - 0.02, 1.08), shade=3)
    # a zinc sheet nailed over rotten boards on the left wall
    corrugated(P, "patch", 0.55, 0.7, tin(ZINC, wear=0.1), loc=(-o - 0.025, 0.12, 0.7), basis=((0, -1, 0), (0, 0, 1), (-1, 0, 0)))
    for y in (-0.1, 0.34):
        for z in (0.4, 1.0):
            bolt(P, (-o - 0.04, y, z), rot=(0, 90, 0))
    # the roof: two sheets that do not match, falling to the back, on purlins
    ang = math.degrees(math.atan2(zf - zb, 2 * hw))
    for k, (x, col) in enumerate(((-0.36, ZINC), (0.37, RUST))):
        corrugated(P, f"roof{k}", 0.78, 1.5, tin(col, metal=0.3, wear=0.1, scratches=0.5), loc=(x, -0.02, roof(-0.02) + 0.055 + k * 0.012), rot=(-ang, 0, 0))
    for k in range(3):
        y = -0.5 + k * 0.5
        bolt(P, (-0.36, y, roof(y) + 0.07), rot=(-ang, 0, 0))
        bolt(P, (0.37, y, roof(y) + 0.082), rot=(-ang, 0, 0))
    # inside: the seat box with its hole
    plank(P, "seat", (1.1, 0.5, 0.45), (0, 0.3, 0.365), shade=2)
    K.cyl("hole", 0.12, 0.01, K.mat("pit", "#151311", rough=1.0), loc=(0, 0.28, 0.588), segs=16, bev=0, parent=P)
    # the door, hung on the left jamb and standing ajar
    D = K.link(bpy.data.objects.new("door", None))
    D.parent = P
    D.location = (-0.3, -o - 0.035, 0)
    D.rotation_euler = (0, 0, math.radians(-28))
    dw = 0.6
    for k in range(4):
        plank(D, f"door{k}", (dw / 4 - 0.006, t, 1.84), (dw / 8 + k * dw / 4, 0, 0.16 + 0.92), shade=(k + 1) % 4)
    for z in (0.45, 1.75):
        plank(D, "ledge", (dw - 0.04, t, 0.1), (dw / 2, t, z), shade=3)
    beam(D, "brace", (0.08, t, 0.5), (dw - 0.08, t, 1.7), 0.09, t, wood(3), bev=0.006)
    # the crescent: a dark cut through the boards
    r, dx = 0.085, 0.045
    ia = math.acos(dx / (2 * r))
    pts = [(r * math.cos(a), r * math.sin(a)) for a in (ia + (2 * math.pi - 2 * ia) * i / 12 for i in range(13))]
    ib = math.atan2(r * math.sin(ia), dx / 2 - dx)
    pts += [(dx + r * math.cos(a), r * math.sin(a)) for a in (2 * math.pi - ib - (2 * math.pi - 2 * ib) * i / 10 for i in range(1, 10))]
    prism(D, "crescent", pts, t + 0.004, K.mat("pit", "#151311", rough=1.0), loc=(dw / 2 - 0.02, 0, 1.52), plane="xz", bev=0)
    for z in (0.4, 1.7):
        K.box("hinge", (0.28, 0.006, 0.04), M(GRAPHITE, metal=0.5, wear=0.5), loc=(0.14, -t / 2 - 0.003, z), bev=0.002, parent=D)
        K.cyl("pintle", 0.012, 0.08, M(GRAPHITE, metal=0.5), loc=(-0.005, -t / 2, z), segs=8, bev=0, parent=D)
    K.box("handle", (0.04, 0.04, 0.14), wood(3), loc=(dw - 0.07, -t / 2 - 0.02, 1.05), bev=0.008, parent=D)
    return P


# ================================================================== hand pump well
def hand_pump_well(seed=24):
    """A low round well head of old field stones capped with planks, a cast iron hand pump on it and a bucket hung
    on the spout's hook."""
    rnd = random.Random(seed)
    P = _root("well")
    s = stone()
    R0, R1, H = 0.5, 0.7, 0.56
    for c in range(2):
        n = 11
        h = H / 2
        for k in range(n):
            a = 2 * math.pi * (k + 0.5 * c) / n + rnd.uniform(-0.03, 0.03)
            ln = 2 * math.pi * (R0 + R1) / 2 / n * 0.93
            K.box(f"stone{c}{k}", (R1 - R0 + rnd.uniform(-0.02, 0.03), ln, h - 0.015), s,
                  loc=(math.cos(a) * (R0 + R1) / 2, math.sin(a) * (R0 + R1) / 2, c * h + h / 2), rot=(rnd.uniform(-2, 2), 0, math.degrees(a)), bev=0.03, parent=P)
    K.cyl("mortar", R0 + 0.12, H - 0.02, K.mat("mortar", "#6f6c63", rough=1.0, grime=0.4), loc=(0, 0, H / 2), segs=16, bev=0, parent=P)
    K.cyl("dark", R0 - 0.02, 0.02, K.mat("pit", "#151311", rough=1.0), loc=(0, 0, H - 0.08), segs=16, bev=0, parent=P)
    # plank cap, one board short so the dark shows through
    zc = H + 0.02
    for k in range(8):
        y = -0.6 + k * 0.17
        L = 2 * math.sqrt(max(0.72 ** 2 - y * y, 0.04))
        off = 0
        if k == 6:
            L, off = L * 0.55, -L * 0.225
        plank(P, f"cap{k}", (L, 0.16, 0.04), (off, y, zc), shade=(k * 3) % 4)
    for x in (-0.3, 0.3):
        plank(P, "batten", (0.08, 1.3, 0.04), (x, 0, zc + 0.04), shade=3)
    # the pump: flange, barrel, spout, handle on its pivot
    iron = M(GRAPHITE, metal=0.55, wear=0.55, grime=0.4, scratches=0.5)
    paint = M(SEA, metal=0.4, wear=0.75, grime=0.45)
    px, py, pz = 0.0, -0.36, zc + 0.06
    K.cyl("flange", 0.13, 0.03, iron, loc=(px, py, pz + 0.015), segs=16, bev=0.006, parent=P)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        bolt(P, (px + math.cos(a) * 0.1, py + math.sin(a) * 0.1, pz + 0.035), rot=(0, 0, 0))
    K.cyl("barrel", 0.075, 0.62, paint, loc=(px, py, pz + 0.34), segs=16, bev=0.01, parent=P)
    for z in (0.06, 0.6):
        K.cyl("band", 0.085, 0.04, iron, loc=(px, py, pz + z), segs=16, bev=0.006, parent=P)
    K.cyl("cap", 0.07, 0.08, iron, loc=(px, py, pz + 0.69), segs=16, r2=0.045, bev=0.01, parent=P)
    sz = pz + 0.48
    rod(P, "spout", (px, py - 0.05, sz), (px, py - 0.44, sz - 0.04), 0.03, iron, segs=12, bev=0.005)
    rod(P, "spout_tip", (px, py - 0.44, sz - 0.02), (px, py - 0.46, sz - 0.13), 0.032, iron, segs=12, bev=0.005)
    K.torus("hook", 0.03, 0.007, iron, loc=(px, py - 0.4, sz - 0.06), rot=(0, 90, 0), segs=12, parent=P)
    pv = (px, py + 0.05, pz + 0.74)
    K.box("pivot", (0.05, 0.12, 0.1), iron, loc=(px, py + 0.02, pz + 0.72), bev=0.01, parent=P)
    rod(P, "handle", (px, py - 0.08, pz + 0.7), (px, py + 0.62, pz + 0.98), 0.018, iron, segs=10)
    K.sphere("knob", 0.03, iron, loc=(px, py + 0.62, pz + 0.98), parent=P)
    # the bucket on the hook
    zinc = tin(ZINC, wear=0.6)
    bx, by, rim = px, py - 0.4, sz - 0.09 - 0.14
    K.torus("bail", 0.14, 0.006, iron, loc=(bx, by, rim), rot=(90, 0, 0), arc=180, segs=16, parent=P)
    K.cyl("bucket", 0.11, 0.26, zinc, loc=(bx, by, rim - 0.13), segs=16, r2=0.14, bev=0.008, parent=P)
    K.torus("bucket_rim", 0.14, 0.008, zinc, loc=(bx, by, rim), segs=20, parent=P)
    return P


# ================================================================== wind pump
def wind_pump(seed=25):
    """A farm water windmill: a timber lattice tower on stone footings, a many-bladed steel rotor with a tail vane, a
    pump rod down the middle to a pump at the base and a small tank beside it."""
    rnd = random.Random(seed)
    P = _root("windpump")
    zt, b0, b1 = 5.6, 0.9, 0.28

    def half(z):
        return b0 + (b1 - b0) * z / zt

    timber = [wood(k, wear=0.6) for k in range(4)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(P, "leg", (sx * b0, sy * b0, -0.05), (sx * b1, sy * b1, zt), 0.13, 0.13, timber[3], bev=0.015)
            K.box("footing", (0.38, 0.38, 0.3), stone(), loc=(sx * b0, sy * b0, 0.1), bev=0.04, parent=P)
    levels = (0.35, 1.45, 2.65, 3.95, zt - 0.05)
    for i, z in enumerate(levels):
        h = half(z)
        for s in (-1, 1):
            beam(P, "girt_x", (-h - 0.06, s * h, z), (h + 0.06, s * h, z), 0.08, 0.06, timber[i % 3], bev=0)
            beam(P, "girt_y", (s * h, -h - 0.06, z), (s * h, h + 0.06, z), 0.08, 0.06, timber[(i + 1) % 3], bev=0)
    for i in range(len(levels) - 1):
        z0, z1 = levels[i], levels[i + 1]
        h0, h1 = half(z0), half(z1)
        for s in (-1, 1):
            for d in (-1, 1):
                o = 0.035 * d
                beam(P, "brace", (-d * h0, s * (h0 + o), z0), (d * h1, s * (h1 + o), z1), 0.06, 0.03, timber[2], bev=0)
                beam(P, "brace", (s * (h0 + o), -d * h0, z0), (s * (h1 + o), d * h1, z1), 0.03, 0.06, timber[2], bev=0)
    # rod guides across the middle
    for z in levels[2:4]:
        beam(P, "guide", (-half(z), 0, z + 0.07), (half(z), 0, z + 0.07), 0.06, 0.06, timber[1])
    # the platform and head
    for k in range(5):
        plank(P, "deck", (0.9, 0.17, 0.04), (0, -0.36 + k * 0.18, zt + 0.02), shade=k)
    dark = M(GRAPHITE, metal=0.5, wear=0.5, grime=0.4)
    steel = tin(ZINC, wear=0.55)
    K.cyl("turntable", 0.24, 0.1, dark, loc=(0, 0, zt + 0.09), segs=16, bev=0.01, parent=P)
    hz = zt + 0.42
    K.box("gearbox", (0.34, 0.6, 0.38), M(SEA, metal=0.4, wear=0.7, grime=0.4), loc=(0, 0.05, hz), bev=0.03, parent=P)
    K.box("gear_lid", (0.38, 0.64, 0.05), dark, loc=(0, 0.05, hz + 0.2), bev=0.01, parent=P)
    rod(P, "shaft", (0, -0.25, hz), (0, -0.78, hz), 0.035, dark, segs=10)
    # rotor: rings carrying pitched blades, a few blades replaced with painted sheet
    R = K.link(bpy.data.objects.new("rotor", None))
    R.parent = P
    R.location = (0, -0.8, hz)
    R.rotation_euler = (0, math.radians(rnd.uniform(0, 30)), 0)
    K.cyl("hub", 0.13, 0.2, dark, rot=(90, 0, 0), segs=14, bev=0.015, parent=R)
    K.cyl("nose", 0.1, 0.12, dark, loc=(0, -0.16, 0), rot=(90, 0, 0), segs=14, r2=0.03, bev=0.01, parent=R)
    for rr in (0.55, 1.12):
        K.torus("ring", rr, 0.016, dark, rot=(90, 0, 0), segs=20, parent=R)
    n = 16
    painted = {3: SEA, 9: BRICK, 12: MUSTARD}
    for k in range(n):
        arm = K.link(bpy.data.objects.new("blade_arm", None))
        arm.parent = R
        arm.rotation_euler = (0, 2 * math.pi * k / n, 0)
        m = tin(painted[k], metal=0.35, wear=0.7) if k in painted else steel
        prism(arm, f"blade{k}", [(0.3, -0.05), (1.22, -0.13), (1.25, 0.12), (0.3, 0.05)], 0.008, m, rot=(28, 0, 0), plane="xz", bev=0)
    for k in range(4):
        arm = K.link(bpy.data.objects.new("strut_arm", None))
        arm.parent = R
        arm.rotation_euler = (0, math.pi / 4 + k * math.pi / 2, 0)
        rod(arm, "strut", (0.1, 0.0, 0), (1.12, 0, 0), 0.014, dark)
    # tail boom and vane
    rod(P, "boom", (0, 0.3, hz), (0, 1.75, hz + 0.08), 0.03, dark, segs=10)
    rod(P, "boom_stay", (0, 0.3, hz + 0.18), (0, 1.5, hz + 0.32), 0.012, dark)
    vane = tin(SEA, metal=0.35, wear=0.75)
    prism(P, "vane", [(1.35, -0.25), (2.35, -0.42), (2.35, 0.48), (1.35, 0.3)], 0.012, vane, loc=(0, 0, hz + 0.05), plane="yz", bev=0)
    K.box("vane_patch", (0.016, 0.34, 0.3), tin(BRICK, wear=0.6), loc=(0, 2.0, hz + 0.12), bev=0.002, parent=P)
    for y, z in ((1.6, 0.2), (2.15, -0.15)):
        bolt(P, (0.012, y, hz + z), rot=(0, 90, 0))
    # pump rod down the middle to the pump, and a pipe to the tank
    rod(P, "pump_rod", (0, 0, 1.15), (0, 0, zt + 0.1), 0.014, dark)
    K.cyl("pump", 0.08, 0.9, M(GRAPHITE, metal=0.5, wear=0.6), loc=(0, 0, 0.55), segs=14, bev=0.01, parent=P)
    K.cyl("pump_cap", 0.1, 0.06, dark, loc=(0, 0, 1.02), segs=14, bev=0.01, parent=P)
    K.box("pump_base", (0.5, 0.5, 0.1), stone(), loc=(0, 0, 0.05), bev=0.03, parent=P)
    tx, ty = 1.3, -0.5
    rod(P, "pipe", (0.06, -0.03, 0.88), (tx - 0.45, ty + 0.18, 0.97), 0.03, tin(RUST), segs=10)
    K.cyl("tank", 0.55, 0.9, tin(ZINC, wear=0.6, moss=0.15), loc=(tx, ty, 0.5), segs=20, bev=0.01, parent=P)
    K.cyl("tank_base", 0.6, 0.06, stone(), loc=(tx, ty, 0.03), segs=16, bev=0.01, parent=P)
    for z in (0.12, 0.5, 0.92):
        K.torus("hoop", 0.555, 0.012, tin(RUST), loc=(tx, ty, z), segs=20, parent=P)
    K.cyl("water", 0.53, 0.01, K.mat("tank_water", "#3d6f78", rough=0.05), loc=(tx, ty, 0.86), segs=24, bev=0, parent=P)
    return P


# ================================================================== chicken coop
def chicken_coop(seed=26):
    """A small plank coop raised on legs, a cleated ramp from its pop-hole down into a wire run, a nest box on the
    far end and a roof patched with odd sheets."""
    rnd = random.Random(seed)
    P = _root("coop")
    ox = -0.6
    fz, H = 0.55, 0.62  # floor height, wall height
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("leg", (0.07, 0.07, fz + 0.1), wood(3), loc=(ox + sx * 0.5, sy * 0.38, (fz - 0.1) / 2 + 0.02), bev=0.008, parent=P)
            K.box("corner", (0.05, 0.05, H), wood(3), loc=(ox + sx * 0.54, sy * 0.42, fz + H / 2), bev=0.006, parent=P)
    plank(P, "floor", (1.14, 0.9, 0.04), (ox, 0, fz), shade=2)
    # weatherboards, overlapping, one of salvaged teal
    rows = 5
    bh = H / rows
    for r in range(rows):
        z = fz + 0.02 + bh * (r + 0.5)
        for sy in (-1, 1):
            m = M(SEA, wear=0.75, grime=0.4, rough=0.85) if (r, sy) == (2, -1) else wood(rnd.randrange(4), wear=0.6)
            K.box("board", (1.16, 0.018, bh + 0.02), m, loc=(ox, sy * 0.455, z), rot=(sy * 6, 0, 0), bev=0.004, parent=P)
        for sx in (-1, 1):
            if sx == 1 and r in (0, 1, 2):
                # boards cut short round the pop-hole
                for y0, y1 in ((-0.46, -0.13), (0.13, 0.46)):
                    K.box("board", (0.018, y1 - y0, bh + 0.02), wood(r % 4, wear=0.6), loc=(ox + sx * 0.575, (y0 + y1) / 2, z), rot=(0, -sx * 6, 0), bev=0.004, parent=P)
                continue
            K.box("board", (0.018, 0.92, bh + 0.02), wood((r + 1) % 4, wear=0.6), loc=(ox + sx * 0.575, 0, z), rot=(0, -sx * 6, 0), bev=0.004, parent=P)
    K.box("pophole", (0.01, 0.26, 0.34), K.mat("pit", "#151311", rough=1.0), loc=(ox + 0.57, 0, fz + 0.2), bev=0, parent=P)
    # gables and the roof: planks with a corrugated patch and a painted sheet
    rz, ang = 0.3, math.degrees(math.atan2(0.3, 0.5))
    top = fz + H + 0.02
    for sx in (-1, 1):
        prism(P, f"gable{sx}", [(-0.46, 0), (0.46, 0), (0, rz)], 0.018, wood(1), loc=(ox + sx * 0.575, 0, top), plane="yz")
    sl = math.hypot(0.55, rz + 0.03)
    for sy in (-1, 1):
        S = K.link(bpy.data.objects.new("slope", None))
        S.parent = P
        S.location = (ox, sy * 0.275, top + rz / 2 - 0.015)
        S.rotation_euler = (math.radians(-sy * ang), 0, 0)
        for k in range(4):
            plank(S, "roofboard", (1.38, sl / 4 - 0.008, 0.025), (0, -sl / 2 + sl / 8 + k * sl / 4, 0), shade=(k + sy) % 4)
        if sy == -1:
            corrugated(S, "patch", 0.5, sl * 0.8, tin(ZINC, wear=0.1), loc=(0.3, 0.04, 0.03))
            for x in (0.12, 0.48):
                bolt(S, (x, 0.04, 0.05), rot=(0, 0, 0))
        else:
            K.box("sheet", (0.42, 0.4, 0.008), tin(BRICK, metal=0.3, wear=0.7), loc=(-0.35, 0.05, 0.018), rot=(0, 0, 4), bev=0.002, parent=S)
    K.box("ridge", (1.4, 0.1, 0.04), tin(RUST), loc=(ox, 0, top + rz + 0.005), bev=0.006, parent=P)
    # nest box on the far end with its lid, and the front hatch
    nx = ox - 0.585 - 0.17
    K.box("nest", (0.34, 0.6, 0.3), wood(2), loc=(nx, 0, fz + 0.2), bev=0.01, parent=P)
    K.box("nest_lid", (0.4, 0.66, 0.025), M(SEA, wear=0.7, rough=0.85), loc=(nx - 0.01, 0, fz + 0.38), rot=(0, -14, 0), bev=0.006, parent=P)
    K.box("hatch", (0.42, 0.02, 0.36), wood(1), loc=(ox - 0.1, -0.475, fz + 0.32), bev=0.006, parent=P)
    for z in (0.2, 0.44):
        K.box("hatch_hinge", (0.12, 0.006, 0.03), M(GRAPHITE, metal=0.5), loc=(ox - 0.27, -0.487, fz + z), bev=0, parent=P)
    K.box("turn_button", (0.03, 0.02, 0.1), wood(3), loc=(ox + 0.14, -0.49, fz + 0.32), rot=(0, 30, 0), bev=0.004, parent=P)
    # the ramp
    p0, p1 = (ox + 0.6, 0, fz + 0.04), (ox + 1.32, 0, 0.0)
    beam(P, "ramp", p0, p1, 0.02, 0.24, wood(2), bev=0.004)
    for k in range(5):
        f = (k + 0.5) / 5
        x, z = p0[0] + (p1[0] - p0[0]) * f, p0[2] + (p1[2] - p0[2]) * f
        beam(P, "cleat", (x - 0.012, -0.11, z + 0.02), (x - 0.012, 0.11, z + 0.02), 0.022, 0.022, wood(3), bev=0)
    # the run: a light frame with wire mesh on its sides and top
    x0, x1, hy, rh = ox + 0.62, ox + 2.12, 0.45, 0.85
    frame = wood(3)
    for x in (x0, (x0 + x1) / 2, x1):
        for sy in (-1, 1):
            K.box("run_post", (0.05, 0.05, rh + 0.08), frame, loc=(x, sy * hy, (rh + 0.08) / 2 - 0.08), bev=0.006, parent=P)
    for z in (0.03, rh):
        for sy in (-1, 1):
            K.box("run_rail", (x1 - x0, 0.04, 0.05), frame, loc=((x0 + x1) / 2, sy * hy, z), bev=0.006, parent=P)
        K.box("run_end", (0.04, 2 * hy, 0.05), frame, loc=(x1, 0, z), bev=0.006, parent=P)
        K.box("run_start", (0.04, 2 * hy, 0.05), frame, loc=(x0, 0, rh), bev=0.006, parent=P) if z == rh else None
    wire = tin(ZINC, wear=0.3, grime=0.2)
    g, w = 0.12, 0.005
    nxw = int(round((x1 - x0) / g))
    for i in range(1, nxw):
        x = x0 + i * (x1 - x0) / nxw
        for sy in (-1, 1):
            K.box("wire_v", (w, w, rh), wire, loc=(x, sy * (hy + 0.025), rh / 2), bev=0, parent=P)
        K.box("wire_t", (w, 2 * hy, w), wire, loc=(x, 0, rh + 0.03), bev=0, parent=P)
    for j in range(1, 8):
        z = j * rh / 8
        for sy in (-1, 1):
            K.box("wire_h", (x1 - x0, w, w), wire, loc=((x0 + x1) / 2, sy * (hy + 0.025), z), bev=0, parent=P)
        K.box("wire_e", (w, 2 * hy, w), wire, loc=(x1 + 0.025, 0, z), bev=0, parent=P)
    for i in range(1, 8):
        y = -hy + i * 2 * hy / 8
        K.box("wire_ev", (w, w, rh), wire, loc=(x1 + 0.025, y, rh / 2), bev=0, parent=P)
        K.box("wire_tl", (x1 - x0, w, w), wire, loc=((x0 + x1) / 2, y, rh + 0.03), bev=0, parent=P)
    K.disc("scratched", 0.62, K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), (ox + 1.4, 0), z=0.006, segs=16, wobble=0.25, seed=seed).parent = P
    return P


# ================================================================== picket fence
def picket_fence(seed=27):
    """A 2.5 m run of pointed pickets between two posts, the white paint weathered off, the pickets uneven, one gone
    and one replaced with bare wood."""
    rnd = random.Random(seed)
    P = _root("picket")
    paint = M(BONE, wear=0.8, grime=0.5, scratches=0.6, rough=0.85)
    for s in (-1, 1):
        K.box(f"post{s}", (0.1, 0.1, 1.25), paint, loc=(s * 1.25, 0, 0.525), bev=0.012, parent=P)
        prism(P, f"cap{s}", [(-0.06, 0), (0.06, 0), (0, 0.06)], 0.12, paint, loc=(s * 1.25, 0, 1.15), plane="xz")
    for z in (0.28, 0.78):
        K.box("rail", (2.4, 0.04, 0.08), wood(3, wear=0.6), loc=(0, -0.07, z), bev=0.008, parent=P)
    n = 15
    for k in range(n):
        x = -1.08 + k * 2.16 / (n - 1)
        if k == 9:
            continue
        h = 0.98 + rnd.uniform(-0.05, 0.03)
        lean = rnd.uniform(-2.5, 2.5)
        m = wood(2, wear=0.4) if k == 4 else paint
        bx, bz = x, 0.05
        if k == 10:
            # hanging off its top nail, its foot swung into the gap
            lean = 12
            bx, bz = x - math.sin(math.radians(lean)) * 0.73, 0.78 - math.cos(math.radians(lean)) * 0.73
        prism(P, f"picket{k}", [(-0.038, 0), (0.038, 0), (0.038, h - 0.07), (0, h), (-0.038, h - 0.07)], 0.02, m,
              loc=(bx, -0.1, bz), rot=(0, lean, 0), plane="xz", bev=0.004)
        for z in (0.28, 0.78):
            if k == 10:
                if z < 0.5:
                    continue
                lean = 0
            K.cyl("nail", 0.006, 0.01, M(GRAPHITE, metal=0.6), loc=(x + math.sin(math.radians(lean)) * (z - 0.05), -0.112, z), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    return P


# ================================================================== farm gate
def farm_gate(seed=28):
    """A five-bar field gate on strap hinges between two thick posts, swung a little open, one bar a painted
    replacement and a rope loop for a latch."""
    rnd = random.Random(seed)
    P = _root("gate")
    for s in (-1, 1):
        K.box(f"post{s}", (0.2, 0.2, 1.6), wood(3, wear=0.5), loc=(s * 1.6, 0, 0.7), rot=(0, 0, rnd.uniform(-3, 3)), bev=0.02, parent=P)
        prism(P, f"cap{s}", [(-0.11, 0), (0.11, 0), (0.11, 0.03), (-0.11, 0.09)], 0.22, wood(3, wear=0.5), loc=(s * 1.6, 0, 1.49), plane="yz")
    G = K.link(bpy.data.objects.new("leaf", None))
    G.parent = P
    G.location = (-1.47, 0, 0)
    G.rotation_euler = (0, 0, math.radians(20))
    L = 2.94
    K.box("heel", (0.1, 0.08, 1.2), wood(1), loc=(0.05, 0, 0.68), bev=0.012, parent=G)
    K.box("head", (0.08, 0.06, 1.08), wood(1), loc=(L - 0.04, 0, 0.66), bev=0.01, parent=G)
    for k, z in enumerate((0.22, 0.44, 0.66, 0.88)):
        m = M(SEA, wear=0.75, grime=0.4, rough=0.85) if k == 2 else wood(k % 3)
        K.box(f"bar{k}", (L - 0.1, 0.04, 0.09), m, loc=(L / 2, 0, z), bev=0.008, parent=G)
    K.box("top_rail", (L - 0.06, 0.07, 0.12), wood(2), loc=(L / 2 + 0.01, 0, 1.12), bev=0.012, parent=G)
    beam(G, "brace", (0.12, -0.04, 0.2), (1.45, -0.04, 1.08), 0.09, 0.04, wood(1), bev=0.008)
    K.box("strut", (0.08, 0.04, 0.96), wood(3), loc=(1.5, -0.04, 0.66), bev=0.008, parent=G)
    for x, z in ((0.05, 0.22), (0.05, 0.88), (1.5, 0.44), (1.5, 0.66), (0.62, 0.66)):
        bolt(G, (x, -0.065, z))
    iron = M(GRAPHITE, metal=0.5, wear=0.5)
    for z in (0.3, 1.0):
        K.box("strap", (0.42, 0.008, 0.05), iron, loc=(0.21, -0.045, z), bev=0.002, parent=G)
        K.cyl("pin", 0.018, 0.1, iron, loc=(-1.49, -0.03, z), segs=8, bev=0, parent=P)
        K.box("pin_plate", (0.012, 0.12, 0.08), iron, loc=(-1.5, -0.03, z - 0.03), bev=0.002, parent=P)
    # latch side: a hook on the post and the rope loop that holds the gate shut, hanging free
    K.torus("rope_loop", 0.07, 0.014, rope_mat(), loc=(1.49, -0.02, 0.92), rot=(90, 0, 90), segs=16, parent=P)
    K.torus("rope_wrap", 0.105, 0.016, rope_mat(), loc=(1.6, 0, 1.02), rot=(0, 0, 0), segs=16, parent=P)
    return P


# ================================================================== campfire ring
def campfire_ring(seed=29):
    """A ring of field stones round a few charred logs in their ash, a split log beside it to sit on."""
    rnd = random.Random(seed)
    P = _root("campfire")
    fy = -0.35
    stone_ring(P, "ring", (0, fy), 0.56, 11, 0.13, rnd)
    K.disc("ash", 0.48, K.mat("ash", ASH, rough=1.0, noise_scale=6.0), (0, fy), z=0.012, segs=20, wobble=0.15, seed=seed).parent = P
    char = K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    for k, (a, tilt, L, r) in enumerate(((10, 14, 0.7, 0.065), (75, 9, 0.62, 0.06), (140, 17, 0.66, 0.07), (215, 6, 0.55, 0.055))):
        ar = math.radians(a)
        c = (math.cos(ar) * 0.1, fy + math.sin(ar) * 0.1, r + 0.03 + math.sin(math.radians(tilt)) * L * 0.2)
        d = (math.cos(ar) * L / 2, math.sin(ar) * L / 2, -math.sin(math.radians(tilt)) * L / 2)
        rod(P, f"brand{k}", (c[0] - d[0], c[1] - d[1], c[2] - d[2]), (c[0] + d[0], c[1] + d[1], c[2] + d[2]), r, char, segs=8, bev=0.01)
    lump(P, "embers", 0.16, (0.02, fy, 0.02), (1.3, 1.1, 0.25), char, rnd, jitter=0.2, flat=0.0)
    # the seat: a log on two chocks
    bark = K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    cut = K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)
    sy = 0.72
    K.cyl("seat", 0.17, 1.35, bark, loc=(0.05, sy, 0.19), rot=(0, 90, 4), segs=12, bev=0.01, parent=P)
    for s in (-1, 1):
        K.cyl(f"seat_end{s}", 0.145, 0.02, cut, loc=(0.05 + s * 0.675 * math.cos(math.radians(4)), sy + s * 0.675 * math.sin(math.radians(4)), 0.19),
              rot=(0, 90, 4), segs=12, bev=0, parent=P)
        K.box(f"chock{s}", (0.1, 0.28, 0.08), wood(3), loc=(0.05 + s * 0.45, sy, 0.04), rot=(0, 0, 4), bev=0.01, parent=P)
    return P


# ================================================================== cooking tripod
def cooking_tripod(seed=30):
    """Three lashed sticks over a small stone fire ring, a short chain down to a dented pot on its bail."""
    rnd = random.Random(seed)
    P = _root("tripod")
    stone_ring(P, "ring", (0, 0), 0.34, 8, 0.1, rnd)
    K.disc("ash", 0.28, K.mat("ash", ASH, rough=1.0, noise_scale=6.0), (0, 0), z=0.01, segs=16, wobble=0.15, seed=seed).parent = P
    char = K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    for k, a in enumerate((30, 150)):
        ar = math.radians(a)
        rod(P, f"stick{k}", (-math.cos(ar) * 0.2, -math.sin(ar) * 0.2, 0.04), (math.cos(ar) * 0.2, math.sin(ar) * 0.2, 0.06), 0.035, char, bev=0.008)
    bark = K.mat("stick", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.3)
    apex = Vector((0, 0, 1.38))
    for k in range(3):
        a = math.radians(90 + 120 * k + rnd.uniform(-8, 8))
        foot = Vector((math.cos(a) * 0.68, math.sin(a) * 0.68, -0.02))
        d = (apex - foot).normalized()
        rod(P, f"leg{k}", tuple(foot), tuple(apex + d * 0.16), 0.028, bark, segs=8, bev=0.006)
    rope = rope_mat()
    for k in range(2):
        K.torus("lashing", 0.05, 0.014, rope, loc=(0, 0, apex.z - 0.02 + k * 0.035), rot=(rnd.uniform(-10, 10), rnd.uniform(-10, 10), 0), segs=14, parent=P)
    iron = M(GRAPHITE, metal=0.6, wear=0.5, grime=0.4)
    z = apex.z - 0.06
    for k in range(6):
        K.torus("link", 0.03, 0.006, iron, loc=(0, 0, z - k * 0.05), rot=(90, 0, 90 * (k % 2)), segs=10, parent=P)
    hz = z - 6 * 0.05 + 0.01
    K.torus("hook", 0.025, 0.007, iron, loc=(0, 0, hz), rot=(90, 0, 0), segs=10, parent=P)
    # the pot, dented on one side, its bail up to the hook
    pz = hz - 0.32
    pot = K.cyl("pot", 0.17, 0.22, M(GUNMETAL, metal=0.6, wear=0.6, grime=0.6), loc=(0, 0, pz), segs=20, r2=0.18, bev=0.02, parent=P)
    dent = Vector((0.12, -0.12, 0.02))
    for v in pot.data.vertices:
        dd = (v.co - dent).length
        if dd < 0.1:
            v.co.xy *= 1 - 0.18 * (1 - dd / 0.1) ** 2
    K.torus("pot_rim", 0.18, 0.012, M(GUNMETAL, metal=0.6, wear=0.7), loc=(0, 0, pz + 0.11), segs=24, parent=P)
    K.disc("stew", 0.165, K.mat("stew", "#5b4632", rough=0.4), (0, 0), z=pz + 0.085, segs=16).parent = P
    K.torus("bail", 0.185, 0.006, iron, loc=(0, 0, pz + 0.11), rot=(90, 0, 0), arc=180, segs=16, parent=P)
    for s in (-1, 1):
        K.box(f"lug{s}", (0.03, 0.02, 0.05), iron, loc=(s * 0.185, 0, pz + 0.09), bev=0.004, parent=P)
    rod(P, "bail_up", (0, 0, pz + 0.295), (0, 0, hz - 0.02), 0.005, iron)
    return P


# ================================================================== washing line
def cloth(P, name, w, h, x0, line, y, mat, rnd, blow=0.07, nx=8, nz=5):
    """A cloth pegged on a line: its top follows the line's sag, its body bellies a little in the wind."""
    bm = bmesh.new()
    ph = rnd.uniform(0, 6.3)
    grid = []
    for i in range(nx + 1):
        u = i / nx
        x = x0 - w / 2 + w * u
        zt = line(x) - 0.008
        hh = h * (1 + 0.04 * math.sin(7 * u + ph))
        col = []
        for j in range(nz + 1):
            v = j / nz
            yy = y + blow * v * math.sin(math.pi * u) + 0.018 * math.sin(9 * u + ph) * v
            col.append(bm.verts.new((x, yy, zt - hh * v)))
        grid.append(col)
    for i in range(nx):
        for j in range(nz):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.modifiers.new("thick", "SOLIDIFY").thickness = 0.006
    K.place(ob, parent=P)
    peg = wood(2, wear=0.2)
    for px in (x0 - w / 2 + 0.05, x0 + w / 2 - 0.05):
        K.box("peg", (0.014, 0.022, 0.075), peg, loc=(px, y, line(px) - 0.02), bev=0.003, parent=P)
    return ob


def washing_line(seed=31):
    """Two T posts 4 m apart with two sagging lines, a sheet, a towel and a couple of cloths pegged out, a basket
    underneath."""
    rnd = random.Random(seed)
    P = _root("washing")
    hx, top, arm = 2.0, 1.95, 0.36
    for s in (-1, 1):
        K.box(f"post{s}", (0.09, 0.09, top + 0.15), wood(3, wear=0.6), loc=(s * hx, 0, (top + 0.15) / 2 - 0.15), rot=(0, s * 1.2, 0), bev=0.01, parent=P)
        K.box(f"cross{s}", (0.07, 2 * arm + 0.1, 0.08), wood(2, wear=0.6), loc=(s * hx, 0, top - 0.06), bev=0.008, parent=P)
        for sy in (-1, 1):
            beam(P, f"knee{s}{sy}", (s * hx, sy * 0.03, top - 0.42), (s * hx, sy * 0.26, top - 0.1), 0.05, 0.035, wood(3, wear=0.6), bev=0.005)
            K.cyl(f"eye{s}{sy}", 0.008, 0.06, M(GRAPHITE, metal=0.6), loc=(s * (hx - 0.03), sy * arm, top - 0.02), rot=(0, 90, 0), segs=6, bev=0, parent=P)
    line_z = top - 0.02
    sag = (0.22, 0.27)
    lines = []
    rope = K.mat("line", "#d8d2c0", rough=0.9, grime=0.2, noise_scale=14.0)
    for k, sy in enumerate((-1, 1)):
        y = sy * arm

        def z_at(x, s=sag[k]):
            return line_z - s * (1 - (x / (hx - 0.03)) ** 2)

        lines.append((y, z_at))
        n = 12
        for i in range(n):
            xa = -(hx - 0.03) + 2 * (hx - 0.03) * i / n
            xb = -(hx - 0.03) + 2 * (hx - 0.03) * (i + 1) / n
            rod(P, f"line{k}{i}", (xa, y, z_at(xa)), (xb, y, z_at(xb)), 0.005, rope, segs=6)
    fy, fz = lines[0]
    by, bz = lines[1]
    cloth(P, "towel", 0.55, 0.75, -1.15, fz, fy - 0.004, M(SEA, wear=0.2, rough=0.95, scratches=0), rnd)
    cloth(P, "shirt", 0.5, 0.62, -0.35, fz, fy - 0.004, M(MUSTARD, wear=0.2, rough=0.95, scratches=0, grime=0.2), rnd)
    cloth(P, "rag", 0.36, 0.42, 0.45, fz, fy - 0.004, M(BRICK, wear=0.2, rough=0.95, scratches=0), rnd)
    cloth(P, "sheet", 1.7, 1.1, 0.35, bz, by - 0.004, M(BONE, wear=0.1, rough=0.95, scratches=0, grime=0.15), rnd, blow=0.12, nx=12, nz=6)
    # a wicker basket under the line, and the peg bag in it
    K.box("basket", (0.55, 0.4, 0.22), K.mat("wicker", "#b8955c", rough=0.95, grime=0.3, noise_scale=20.0), loc=(1.25, -0.75, 0.11), rot=(0, 0, -12), bev=0.03, parent=P)
    K.box("basket_in", (0.47, 0.32, 0.02), M(BONE, wear=0.1, rough=0.95), loc=(1.25, -0.75, 0.2), rot=(0, 0, -12), bev=0.01, parent=P)
    return P


# ================================================================== rain barrel
def staves(P, name, n, r0, bulge, h, t, mats, loc, rnd, levels=7):
    """A coopered barrel's staves: n curved boards round a bulging profile."""
    x0, y0, z0 = loc
    gap = 0.006
    for k in range(n):
        a0 = 2 * math.pi * k / n + gap / r0
        a1 = 2 * math.pi * (k + 1) / n - gap / r0
        dz = rnd.uniform(-0.01, 0.01)
        bm = bmesh.new()
        O, I = [], []
        for i in range(levels + 1):
            z = h * i / levels
            r = r0 + bulge * math.sin(math.pi * i / levels)
            O.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z + dz)) for a in (a0, (a0 + a1) / 2, a1)])
            I.append([bm.verts.new(((r - t) * math.cos(a), (r - t) * math.sin(a), z + dz)) for a in (a0, (a0 + a1) / 2, a1)])
        for i in range(levels):
            for j in range(2):
                bm.faces.new((O[i][j], O[i][j + 1], O[i + 1][j + 1], O[i + 1][j]))
                bm.faces.new((I[i][j], I[i + 1][j], I[i + 1][j + 1], I[i][j + 1]))
            bm.faces.new((O[i][0], O[i + 1][0], I[i + 1][0], I[i][0]))
            bm.faces.new((O[i][2], I[i][2], I[i + 1][2], O[i + 1][2]))
        for j in range(2):
            bm.faces.new((O[0][j], I[0][j], I[0][j + 1], O[0][j + 1]))
            bm.faces.new((O[-1][j], O[-1][j + 1], I[-1][j + 1], I[-1][j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = K.from_bm(f"{name}{k}", bm, mats[k % len(mats)], smooth=True)
        K.place(ob, (x0, y0, z0), (0, 0, 0), P)


def rain_barrel(seed=32):
    """A coopered barrel on two bearers under a short gutter chute bracketed to a post, its lid slid half off."""
    rnd = random.Random(seed)
    R = _root("rainbarrel")
    P = _offset(R, (0.1, -0.2, 0))
    bx, by = -0.12, 0.0
    for x in (-0.18, 0.18):
        plank(P, "bearer", (0.1, 0.8, 0.08), (bx + x, by, 0.04), shade=3)
    r0, bulge, h, z0 = 0.3, 0.045, 0.92, 0.08
    staves(P, "stave", 16, r0, bulge, h, 0.025, [wood(k, wear=0.5) for k in range(4)], (bx, by, z0), rnd)
    K.cyl("barrel_floor", r0 - 0.01, 0.03, wood(1), loc=(bx, by, z0 + 0.05), segs=16, bev=0, parent=P)
    K.cyl("water", r0 + 0.01, 0.01, K.mat("barrel_water", "#2f4f55", rough=0.05), loc=(bx, by, z0 + h - 0.12), segs=20, bev=0, parent=P)
    hoop = tin(RUST, wear=0.5)
    for f in (0.07, 0.3, 0.7, 0.93):
        r = r0 + bulge * math.sin(math.pi * f) + 0.004
        K.torus("hoop", r, 0.008, hoop, loc=(bx, by, z0 + h * f), segs=28, parent=P)
    # the lid slid half off, resting on the rim
    K.cyl("lid", r0 + 0.01, 0.03, wood(2), loc=(bx - 0.25, by - 0.06, z0 + h + 0.022), rot=(0, 1.5, 0), segs=20, bev=0.006, parent=P)
    K.box("lid_cleat", (0.06, 0.4, 0.03), wood(3), loc=(bx - 0.27, by - 0.06, z0 + h + 0.052), rot=(0, 1.5, 0), bev=0.005, parent=P)
    # a short gutter chute coming down from behind, bracketed to a post, its mouth over the barrel
    px, py = bx + 0.22, by + 0.45
    K.box("post", (0.1, 0.1, 1.85), wood(3, wear=0.6), loc=(px, py, 0.825), bev=0.012, parent=P)
    g0, g1 = (bx + 0.04, by + 0.98, 1.66), (bx, by + 0.04, 1.24)
    L = math.dist(g0, g1)
    zinc = tin(ZINC, wear=0.6)
    ch = shell_panel("chute", 0.08, math.pi, 2 * math.pi, -L / 2, L / 2, zinc, t=0.006)
    ch.parent = P
    _orient(ch, g0, g1, axis="Y")
    end = K.box("chute_end", (0.16, 0.006, 0.08), zinc, bev=0, parent=P)
    end.location = Vector(g0) + (Vector(g1) - Vector(g0)).normalized() * 0.003 + Vector((0, 0, -0.04))
    end.rotation_euler = ch.rotation_euler
    iron = M(GRAPHITE, metal=0.5, wear=0.5)
    for f in (0.42, 0.66):
        p = Vector(g0).lerp(Vector(g1), f)
        rod(P, "bracket", (px - 0.05, p.y, p.z - 0.085), (p.x, p.y, p.z - 0.085), 0.008, iron)
        st = K.torus("strap", 0.086, 0.006, iron, loc=tuple(p), segs=12, arc=180, parent=P)
        st.rotation_euler = (ch.rotation_euler.to_matrix() @ Matrix.Rotation(math.radians(-90), 3, "X")).to_euler()
        bolt(P, (px - 0.055, p.y, p.z - 0.085), rot=(0, 90, 0))
    return R


PROPS = {
    "sack_pile": sack_pile,
    "wheelbarrow": wheelbarrow,
    "outhouse": outhouse,
    "hand_pump_well": hand_pump_well,
    "wind_pump": wind_pump,
    "chicken_coop": chicken_coop,
    "picket_fence": picket_fence,
    "farm_gate": farm_gate,
    "campfire_ring": campfire_ring,
    "cooking_tripod": cooking_tripod,
    "washing_line": washing_line,
    "rain_barrel": rain_barrel,
}
