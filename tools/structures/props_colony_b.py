"""More props for the scenes between places (docs/world-generation.md, Scenes), in the families of docs/art-direction.md.

Colony Works: a strapped pallet, the old colony's vehicles left where they died (a forklift, a hatchback, a flatbed
truck and a farm tractor), a long I-beam on blocks, an elbow of the big pipe, a road cone, three road signs and a
broken belt conveyor. Salvager Tech, from the SV-04 coming down: a crashed drop pod, a torn hull plate, a cargo pod,
a wing fragment, a thruster bell, a supply cache, a solar array and a salvage beacon.

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
from families import M, glow, shell_panel, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, RUST, ZINC, CYAN, COPPER
from props import wood, plank, bolt, _root

TYRE, CARDBOARD, SOIL, SCORCH = "#26282a", "#b08d5a", "#5a3f2a", "#3d342b"
PLATE, PLATE_D, DARK = "#757d87", "#2f343b", "#15171a"   # the SV-04's hull plates, its frames and its shadowed gaps
GLASS, CELL = "#22303a", "#1b2a3a"


def label(P, text, loc, size, mat, rot=(90, 0, 0), extrude=0.01):
    """FR.label with a coarser curve: stencil letters read the same at prop size and cost a fifth of the triangles."""
    ob = FR.label(P, text, loc, size, mat, rot=rot, extrude=extrude)
    ob.data.resolution_u = 2
    return ob


def _grp(P, name, loc=(0, 0, 0), rot=(0, 0, 0)):
    g = K.link(bpy.data.objects.new(name, None))
    g.parent = P
    g.location = loc
    g.rotation_euler = [math.radians(a) for a in rot]
    return g


def _between(ob, a, b):
    a, b = Vector(a), Vector(b)
    ob.location = (a + b) / 2
    ob.rotation_euler = Vector((0, 0, 1)).rotation_difference(b - a).to_euler()
    return ob


def beam(P, name, a, b, w, mat, d=None, bev=0.01):
    """A square bar from a to b."""
    L = (Vector(b) - Vector(a)).length
    ob = K.box(name, (w, d or w, L), mat, bev=bev, parent=P)
    return _between(ob, a, b)


def rod(P, name, a, b, r, mat, segs=8):
    """A round bar, pipe or cable from a to b."""
    L = (Vector(b) - Vector(a)).length
    ob = K.cyl(name, r, L, mat, segs=segs, bev=0, parent=P)
    return _between(ob, a, b)


def cable(P, name, pts, r, mat):
    for k in range(len(pts) - 1):
        rod(P, f"{name}{k}", pts[k], pts[k + 1], r, mat, segs=6)
        if 0 < k:
            K.sphere(f"{name}_joint{k}", r, mat, loc=pts[k], parent=P)


def plate_xz(name, pts, t, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, bev=0.01):
    """A flat polygon in the XZ plane, t thick along Y (sign faces, panels)."""
    bm = bmesh.new()
    a = [bm.verts.new((x, -t / 2, z)) for x, z in pts]
    b = [bm.verts.new((x, t / 2, z)) for x, z in pts]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((a[j], a[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2)
    return K.place(ob, loc, rot, parent)


def lathe(name, prof, mat, segs=24, loc=(0, 0, 0), rot=(0, 0, 0), parent=None):
    """A closed (r, z) profile turned about Z: bells, domes and rings."""
    bm = bmesh.new()
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * j / segs), r * math.sin(2 * math.pi * j / segs), z)) for j in range(segs)] for r, z in prof]
    n = len(prof)
    for i in range(n):
        for j in range(segs):
            i2, j2 = (i + 1) % n, (j + 1) % segs
            bm.faces.new((rings[i][j], rings[i][j2], rings[i2][j2], rings[i2][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, loc, rot, parent)


def tyre(P, name, loc, r, w, flat=0.0, rim=None, axis="y", lugs=0):
    """A wheel on an axle along y (or x). flat squashes the tyre onto the ground; loc is its hub, so a flat tyre's hub
    sits at r * (1 - flat)."""
    rot = {"y": (90, 0, 0), "x": (0, 90, 0), "z": (0, 0, 0)}[axis]
    t = K.cyl(f"{name}_tyre", r, w, K.mat("tyre", TYRE, rough=0.9, grime=0.4), loc=loc, rot=rot, segs=16, bev=min(0.06, w * 0.25), parent=P)
    if axis != "z":
        t.scale = (1 + flat * 0.3, 1 - flat, 1) if axis == "y" else (1 - flat, 1 + flat * 0.3, 1)
    rim = rim or M(BONE, metal=0.3, wear=0.7, grime=0.5)
    K.cyl(f"{name}_rim", r * 0.55, w + 0.02, rim, loc=loc, rot=rot, segs=12, bev=0.015, parent=P)
    K.cyl(f"{name}_hub", r * 0.18, w + 0.06, M(GRAPHITE, metal=0.5, wear=0.5), loc=loc, rot=rot, segs=8, bev=0, parent=P)
    for k in range(lugs):
        a = 2 * math.pi * k / lugs
        rr = r * (1 - flat * 0.5 * (1 - math.sin(a) ** 2 if math.sin(a) < 0 else 0)) - 0.005
        x, z = math.cos(a) * rr, math.sin(a) * rr
        for s in (-1, 1):
            off = (x, s * w * 0.25, z) if axis == "y" else (s * w * 0.25, x, z)
            K.box(f"{name}_lug", (0.07, w * 0.5, 0.08) if axis == "y" else (w * 0.5, 0.07, 0.08), K.mat("tyre", TYRE, rough=0.9, grime=0.4),
                  loc=(loc[0] + off[0], loc[1] + off[1], loc[2] + off[2]),
                  rot=(0, -math.degrees(a) + 90 + s * 20, 0) if axis == "y" else (math.degrees(a) - 90 + s * 20, 0, 0), bev=0, parent=P)


def rust_patch(P, loc, size, rot=(0, 0, 0)):
    """A thin patch of rust on a panel with two smaller blooms off its edges, so it reads as rot, not a sticker."""
    rm = M(RUST, metal=0.3, wear=0.2, grime=0.6, rough=0.9)
    ob = K.box("rust", size, rm, loc=loc, rot=rot, bev=0, parent=P)
    thin = min(range(3), key=lambda i: size[i])
    u, v = [i for i in range(3) if i != thin]
    h = int(sum(abs(c) for c in loc) * 1000)
    for k, (fu, fv) in enumerate(((0.5, 0.3), (-0.45, -0.35))):
        off, sz = [0.0, 0.0, 0.0], list(size)
        off[u], off[v] = size[u] * fu, size[v] * (fv if (h + k) % 2 else -fv)
        sz[u], sz[v] = size[u] * 0.45, size[v] * 0.55
        sz[thin] = size[thin] * 0.9
        sub = K.box("rust_bloom", sz, rm, loc=off, bev=0, parent=ob)
        sub.rotation_euler[thin] = math.radians(25 + 20 * k)
    return ob


def mound(P, name, loc, r, h, seed=1, n=6, sx=1.0):
    """A low ring of turned-up soil where something hit the ground (sx stretches it along x)."""
    rnd = random.Random(seed)
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    x, y = loc
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.25, 0.25)
        s = rnd.uniform(0.14, 0.26) * r * 2
        rr = r * rnd.uniform(0.85, 1.2)
        ob = K.sphere(f"{name}{k}", s, soil, loc=(x + math.cos(a) * rr * sx, y + math.sin(a) * rr, -h * 0.35),
                      scale=(rnd.uniform(1.4, 2.0), 1.0, h / s * rnd.uniform(0.6, 1.0)), parent=P)
        ob.rotation_euler = (0, 0, a + math.pi / 2)


def _hm():
    return M(PLATE, metal=0.05, rough=0.6, wear=0.6, grime=0.4)


def _gm():
    return M(PLATE_D, metal=0.4, rough=0.45, wear=0.4, grime=0.4)


def _copper():
    return K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)


# ================================================================== Colony Works
def pallet(seed=21):
    """A shipping pallet with a colony crate and cardboard boxes strapped down on it, one more box loose on top."""
    rnd = random.Random(seed)
    P = _root("pallet")
    L, W = 1.2, 1.0
    for k, x in enumerate((-0.53, 0.0, 0.53)):
        plank(P, f"bottom{k}", (0.14, W, 0.022), (x, 0, 0.011), shade=k)
        for j, y in enumerate((-0.43, 0.0, 0.43)):
            plank(P, f"block{k}{j}", (0.14, 0.12, 0.078), (x, y, 0.061), shade=3)
        plank(P, f"stringer{k}", (0.14, W, 0.022), (x, 0, 0.111), shade=1)
    for k in range(5):
        plank(P, f"deck{k}", (L, 0.14, 0.022), (0, -0.43 + k * 0.215, 0.133), rot=(0, 0, rnd.uniform(-0.8, 0.8)), shade=k + 1)
    top = 0.144
    card = K.mat("cardboard", CARDBOARD, rough=0.95, grime=0.35, noise_scale=4.0)
    tape = K.mat("tape", "#c9b48a", rough=0.6)
    FR.framed_box(P, "crate", (-0.29, -0.24, top + 0.225), (0.58, 0.46, 0.45), M(BONE, wear=0.5, grime=0.4), M(STEEL, metal=0.3), fw=0.045, screws=M(ZINC, metal=0.6))
    for nm, (x, y, sx, sy) in (("box_a", (0.29, -0.24, 0.56, 0.46)), ("box_b", (0.0, 0.24, 1.14, 0.46))):
        K.box(nm, (sx, sy, 0.45), card, loc=(x, y, top + 0.225), bev=0.015, parent=P)
        K.box(f"{nm}_tape", (sx + 0.004, 0.06, 0.03), tape, loc=(x, y, top + 0.442), bev=0, parent=P)
    K.box("box_loose", (0.42, 0.36, 0.28), card, loc=(0.02, 0.05, top + 0.45 + 0.14), rot=(0, 0, 9), bev=0.015, parent=P)
    K.box("box_loose_tape", (0.06, 0.364, 0.284), tape, loc=(0.02, 0.05, top + 0.45 + 0.14), rot=(0, 0, 9), bev=0, parent=P)
    strap = M(MUSTARD, wear=0.3, grime=0.3, rough=0.6)
    zt = top + 0.452
    for x in (-0.38, 0.38):
        K.box(f"strap_top{x}", (0.045, 0.94, 0.006), strap, loc=(x, 0, zt), bev=0, parent=P)
        for s in (-1, 1):
            K.box(f"strap_side{x}{s}", (0.045, 0.006, 0.45), strap, loc=(x, s * 0.472, top + 0.226), bev=0, parent=P)
            K.box(f"strap_under{x}{s}", (0.045, 0.06, 0.006), strap, loc=(x, s * 0.47, top - 0.025), bev=0, parent=P)
        K.box(f"buckle{x}", (0.07, 0.012, 0.05), M(ZINC, metal=0.6), loc=(x, -0.478, top + 0.3), bev=0.003, parent=P)
    return P


def forklift_wreck(seed=22):
    """A colony forklift dead on flat tyres: mustard body, steel overhead guard, the mast bent over and the forks off
    in the grass in front of it."""
    rnd = random.Random(seed)
    P = _root("forklift")
    paint = M(MUSTARD, wear=0.85, grime=0.65, scratches=0.7, moss=0.1)
    steel = M(STEEL, metal=0.4, wear=0.6, grime=0.5)
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.4)
    zinc = M(ZINC, metal=0.6, wear=0.5)
    # sagging a little towards the flat front-left tyre
    B = _grp(P, "body", (0, 0, 0), (2, -1.5, 0))
    fl = 0.3
    K.slab("chassis", 2.0, 1.1, 0.55, 0.1, paint, loc=(-0.05, 0, 0.5), bev=0.025, parent=B)
    K.slab("counterweight", 0.5, 1.12, 0.8, 0.1, dark, loc=(-0.95, 0, 0.62), bev=0.03, parent=B)
    FR.hazard(B, (-1.205, 0, 0.62), 1.0, 0.2, M(MUSTARD), dark, n=6, axis="x")
    K.box("hood", (0.8, 0.9, 0.12), paint, loc=(-0.45, 0, 0.83), rot=(0, -4, 0), bev=0.02, parent=B)
    FR.vent(B, (-0.2, -0.56, 0.5), 0.6, 0.3, steel, dark)
    K.box("seat", (0.45, 0.5, 0.12), K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4), loc=(-0.45, 0, 0.96), bev=0.03, parent=B)
    K.box("seat_back", (0.1, 0.5, 0.45), K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4), loc=(-0.7, 0, 1.18), rot=(0, -10, 0), bev=0.03, parent=B)
    K.box("dash", (0.25, 0.9, 0.35), paint, loc=(0.38, 0, 0.92), bev=0.02, parent=B)
    beam(B, "column", (0.3, 0, 1.05), (0.1, 0, 1.35), 0.05, dark)
    K.torus("steer", 0.16, 0.018, dark, loc=(0.08, 0, 1.37), rot=(0, -60, 0), segs=20, parent=B)
    for nm, x, y in (("lever0", 0.4, 0.15), ("lever1", 0.4, 0.25)):
        beam(B, nm, (x, y, 1.08), (x - 0.05, y, 1.3), 0.02, dark)
    # overhead guard: four posts and a grid roof
    for x0, x1 in ((0.45, 0.32), (-0.75, -0.75)):
        for s in (-1, 1):
            beam(B, f"guard_post{x0}{s}", (x0, s * 0.48, 0.75), (x1, s * 0.48, 2.1), 0.07, steel)
    for s in (-1, 1):
        K.box(f"guard_rail{s}", (1.2, 0.07, 0.07), steel, loc=(-0.2, s * 0.48, 2.12), bev=0.01, parent=B)
    for k in range(5):
        K.box(f"guard_bar{k}", (0.05, 1.0, 0.04), steel, loc=(-0.72 + k * 0.26, 0, 2.15), bev=0.008, parent=B)
    K.box("lamp", (0.08, 0.14, 0.1), M(BONE), loc=(0.37, -0.52, 2.0), bev=0.01, parent=B)
    K.box("lamp_lens", (0.012, 0.1, 0.07), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(0.415, -0.52, 2.0), bev=0, parent=B)
    # wheels: drive wheels at the front, steer wheels under the counterweight, both pairs flat
    tyre(B, "wheel_fl", (0.55, -0.5, 0.3 * (1 - fl)), 0.3, 0.24, flat=fl)
    tyre(B, "wheel_fr", (0.55, 0.5, 0.3 * (1 - 0.15)), 0.3, 0.24, flat=0.15)
    tyre(B, "wheel_rl", (-0.8, -0.48, 0.22 * 0.88), 0.22, 0.18, flat=0.12)
    tyre(B, "wheel_rr", (-0.8, 0.48, 0.22 * 0.88), 0.22, 0.18, flat=0.12)
    # the mast: lower channels upright, the upper section bent over to one side
    for s in (-1, 1):
        K.box(f"mast_lo{s}", (0.1, 0.08, 1.2), steel, loc=(0.92, s * 0.32, 0.72), bev=0.012, parent=B)
    K.box("mast_tie", (0.1, 0.72, 0.1), steel, loc=(0.92, 0, 1.28), bev=0.012, parent=B)
    beam(B, "tilt_cyl", (0.3, -0.42, 0.55), (0.88, -0.36, 0.85), 0.06, dark)
    beam(B, "tilt_cyl2", (0.3, 0.42, 0.55), (0.88, 0.36, 0.85), 0.06, dark)
    U = _grp(B, "mast_up", (0.92, 0, 1.25), (-24, 18, 4))
    for s in (-1, 1):
        K.box(f"mast_hi{s}", (0.08, 0.07, 1.25), steel, loc=(0.02, s * 0.27, 0.6), bev=0.012, parent=U)
    K.box("mast_head", (0.1, 0.62, 0.1), steel, loc=(0.02, 0, 1.2), bev=0.012, parent=U)
    K.cyl("lift_ram", 0.05, 1.1, zinc, loc=(-0.05, 0, 0.55), segs=10, bev=0, parent=U)
    for y in (-0.12, 0.12):
        K.box(f"chain{y}", (0.015, 0.03, 1.0), dark, loc=(0.02, y, 0.65), bev=0, parent=U)
    # the carriage dropped to the ground and the forks off it in the grass
    K.box("carriage", (0.08, 0.95, 0.42), dark, loc=(1.06, 0, 0.21), rot=(0, 6, 0), bev=0.015, parent=P)
    for k, (x, y, yaw) in enumerate(((1.75, -0.3, -4), (1.65, 0.45, 14))):
        f = _grp(P, f"fork{k}", (x, y, 0), (0, 0, yaw))
        K.box("tine", (1.1, 0.11, 0.045), dark, loc=(0, 0, 0.0225), bev=0.008, parent=f)
        K.box("tine_tip", (0.06, 0.11, 0.02), dark, loc=(0.56, 0, 0.012), bev=0.005, parent=f)
        K.box("shank", (0.045, 0.11, 0.45), dark, loc=(-0.55, 0, 0.07), rot=(0, 75, 0), bev=0.008, parent=f)
    for k in range(4):
        rust_patch(B, (rnd.uniform(-0.6, 0.6), -0.553, rnd.uniform(0.35, 0.65)), (rnd.uniform(0.1, 0.25), 0.004, rnd.uniform(0.06, 0.15)))
    label(B, "FL-3", (-0.55, -0.556, 0.62), 0.12, M(GRAPHITE), rot=(90, 0, 0))
    return P


def car_wreck(seed=23):
    """A colony runabout gone to rust: doors gone, glass crazed, the wheels off one side so it sits on the hubs."""
    rnd = random.Random(seed)
    P = _root("car")
    paint = M(STEEL, wear=0.9, grime=0.7, scratches=0.7, moss=0.15)
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.5)
    glass = K.mat("darkglass", GLASS, rough=0.15, metal=0.2)
    cloth = K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4)
    rw, flat = 0.3, 0.12
    zr, zl = rw * (1 - flat), 0.14
    roll = math.degrees(math.asin((zr - zl) / 1.6))
    B = _grp(P, "body", (0, 0, (zr + zl) / 2), (roll, -1.0, 0))
    # body local: z = 0 on the axle line, x forward
    K.box("floor", (3.4, 1.48, 0.07), dark, loc=(0, 0, 0.0), bev=0.01, parent=B)
    K.slab("front", 0.95, 1.56, 0.56, 0.1, paint, loc=(1.32, 0, 0.33), bev=0.025, parent=B)
    K.slab("rear", 1.22, 1.56, 0.62, 0.1, paint, loc=(-1.13, 0, 0.36), bev=0.025, parent=B)
    K.box("hood_seam", (0.02, 1.3, 0.01), dark, loc=(1.0, 0, 0.615), bev=0, parent=B)
    for s in (-1, 1):
        K.box(f"sill{s}", (1.36, 0.1, 0.2), paint, loc=(0.15, s * 0.73, 0.12), bev=0.02, parent=B)
        beam(B, f"a_pillar{s}", (0.8, s * 0.72, 0.6), (0.28, s * 0.68, 1.24), 0.08, paint)
        beam(B, f"b_pillar{s}", (-0.52, s * 0.74, 0.2), (-0.52, s * 0.7, 1.24), 0.1, paint)
        K.box(f"quarter{s}", (0.66, 0.05, 0.58), paint, loc=(-0.87, s * 0.745, 0.93), rot=(s * 4, 0, 0), bev=0.015, parent=B)
        K.box(f"quarter_glass{s}", (0.4, 0.02, 0.28), glass, loc=(-0.85, s * 0.775, 1.0), rot=(s * 4, 0, 0), bev=0.005, parent=B)
        K.box(f"stripe{s}", (0.95, 0.01, 0.06), M(MUSTARD, wear=0.7), loc=(1.3, s * 0.785, 0.42), bev=0, parent=B)
        K.box(f"stripe_r{s}", (1.2, 0.01, 0.06), M(MUSTARD, wear=0.7), loc=(-1.13, s * 0.785, 0.42), bev=0, parent=B)
        for z in (0.35, 0.75):
            K.box(f"hinge{s}{z}", (0.04, 0.05, 0.08), dark, loc=(0.77 - (z - 0.35) * 0.4, s * 0.76, z), bev=0, parent=B)
        K.box(f"headlight{s}", (0.02, 0.3, 0.14), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(1.795, s * 0.52, 0.45), bev=0.005, parent=B)
        K.box(f"taillight{s}", (0.02, 0.22, 0.12), M(SIGNAL, wear=0.3), loc=(-1.74, s * 0.6, 0.5), bev=0.005, parent=B)
    K.slab("roof", 1.56, 1.44, 0.06, 0.08, paint, loc=(-0.46, 0, 1.25), bev=0.02, parent=B)
    K.box("hatch", (0.05, 1.48, 0.78), paint, loc=(-1.485, 0, 0.95), rot=(0, 40, 0), bev=0.015, parent=B)
    K.box("hatch_glass", (0.02, 1.2, 0.5), glass, loc=(-1.515, 0, 0.99), rot=(0, 40, 0), bev=0.005, parent=B)
    K.box("windshield", (0.02, 1.34, 0.78), glass, loc=(0.54, 0, 0.92), rot=(0, -39.5, 0), bev=0.005, parent=B)
    for k in range(3):  # a star of cracks in the glass
        K.box(f"crack{k}", (0.01, 0.4, 0.008), K.mat("crack", "#9fb3bd", rough=0.3), loc=(0.528, 0.25, 1.0), rot=(k * 60 + 15, -39.5, 0), bev=0, parent=B)
    K.box("bumper_f", (0.1, 1.6, 0.14), dark, loc=(1.8, 0, 0.2), rot=(0, 0, 3), bev=0.02, parent=B)
    K.box("bumper_r", (0.1, 1.6, 0.14), dark, loc=(-1.76, 0, 0.22), bev=0.02, parent=B)
    K.box("grille", (0.02, 0.6, 0.14), dark, loc=(1.795, 0, 0.42), bev=0, parent=B)
    # inside, seen through the door holes
    K.box("dash", (0.3, 1.4, 0.25), dark, loc=(0.7, 0, 0.62), bev=0.02, parent=B)
    beam(B, "column", (0.66, 0.35, 0.65), (0.42, 0.35, 0.8), 0.04, dark)
    K.torus("steer", 0.17, 0.018, dark, loc=(0.42, 0.35, 0.82), rot=(0, -65, 0), segs=20, parent=B)
    for s in (-1, 1):
        K.box(f"seat{s}", (0.48, 0.48, 0.14), cloth, loc=(0.05, s * 0.35, 0.3), bev=0.04, parent=B)
        K.box(f"seat_back{s}", (0.12, 0.48, 0.6), cloth, loc=(-0.2, s * 0.35, 0.62), rot=(0, -12, 0), bev=0.04, parent=B)
    K.box("bench", (0.45, 1.3, 0.14), cloth, loc=(-0.85, 0, 0.4), bev=0.04, parent=B)
    # rust through the paint
    for k in range(9):
        side = rnd.choice((-1, 1))
        x = rnd.uniform(-1.6, 1.7)
        if -1.1 < x < 0.15:
            rust_patch(B, (x, 0, 1.283), (rnd.uniform(0.15, 0.4), rnd.uniform(0.2, 0.6), 0.004), rot=(0, 0, rnd.uniform(0, 90)))
        else:
            rust_patch(B, (x, side * 0.787, rnd.uniform(0.15, 0.55)), (rnd.uniform(0.12, 0.35), 0.004, rnd.uniform(0.08, 0.2)))
    rust_patch(B, (1.35, 0.1, 0.618), (0.5, 0.6, 0.006), rot=(0, 0, 20))
    # wheels: two flat tyres on the far side, bare brake drums on the ground on the near side
    for x in (1.12, -1.12):
        tyre(B, f"wheel{x}", (x, 0.83, 0), rw, 0.2, flat=flat, rim=M(ZINC, metal=0.5, wear=0.7, grime=0.6))
        K.cyl(f"drum{x}", 0.15, 0.12, M(RUST, metal=0.5, wear=0.6), loc=(x, -0.72, 0), rot=(90, 0, 0), segs=14, bev=0.015, parent=B)
        for s in (-1, 1):
            K.torus(f"arch{x}{s}", 0.36, 0.03, dark, loc=(x, s * 0.785, 0.0), rot=(90, 0, 0), arc=180, segs=14, parent=B)
    return P


def truck_wreck(seed=24):
    """A colony flatbed truck, its cab-over cab caved in, a crate tipped on the bed against the spare tyre."""
    rnd = random.Random(seed)
    P = _root("truck")
    bone = M(BONE, wear=0.75, grime=0.65, scratches=0.6, moss=0.15)
    steel = M(STEEL, metal=0.4, wear=0.7, grime=0.6)
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.5)
    glass = K.mat("darkglass", GLASS, rough=0.15, metal=0.2)
    rw = 0.5
    for s in (-1, 1):
        K.box(f"rail{s}", (6.6, 0.14, 0.26), dark, loc=(-0.1, s * 0.48, 0.78), bev=0.02, parent=P)
    for k in range(5):
        K.box(f"cross{k}", (0.12, 0.96, 0.18), dark, loc=(-3.0 + k * 1.4, 0, 0.78), bev=0, parent=P)
    # wheels: singles at the front, wide twin-tread tyres at the back; the near rear one flat
    for x, flats, tw in ((2.3, (0.06, 0.06), 0.32), (-2.27, (0.25, 0.08), 0.5)):
        for s, f in zip((-1, 1), flats):
            tyre(P, f"wheel{x}{s}", (x, s * (0.98 + tw / 2 - 0.16), rw * (1 - f)), rw, tw, flat=f, rim=M(STEEL, metal=0.4, wear=0.7, grime=0.6))
        K.cyl(f"axle{x}", 0.06, 1.7, dark, loc=(x, 0, 0.5), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    for x in (2.3, -2.27):
        for s in (-1, 1):
            shell_panel(f"fender{x}{s}", 0.62, math.radians(10), math.radians(170), s * 0.8, s * (1.22 if x > 0 else 1.32), M(GRAPHITE, metal=0.3, wear=0.6),
                        t=0.04, parent=P, loc=(x, 0, 0.5))
    # cab: framed lower half, crushed upper half
    C = _grp(P, "cab", (2.75, 0, 0), (0, 5, 0))
    FR.framed_box(C, "cab_lo", (0, 0, 1.45), (1.35, 2.3, 1.1), bone, steel, fw=0.1)
    FR.vent(C, (0.7, 0, 1.25), 1.0, 0.45, steel, dark, axis="x")
    for s in (-1, 1):
        beam(C, f"pillar_b{s}", (-0.6, s * 1.08, 2.0), (-0.6, s * 1.06, 2.82), 0.1, steel)
        beam(C, f"pillar_a{s}", (0.6, s * 1.08, 2.0), (0.52, s * (1.04 if s > 0 else 0.98), 2.38), 0.1, steel)
        K.box(f"side_glass{s}", (0.6, 0.03, 0.55), glass, loc=(-0.25, s * 1.1, 2.3), rot=(s * 3, 0, 0), bev=0.005, parent=C)
        K.box(f"mirror_arm{s}", (0.05, 0.3, 0.05), dark, loc=(0.62, s * 1.25, 2.0), bev=0.005, parent=C)
    K.box("mirror", (0.06, 0.18, 0.3), dark, loc=(0.6, -1.4, 1.75), rot=(15, 0, 10), bev=0.01, parent=C)
    K.box("back_wall", (0.08, 2.2, 0.86), bone, loc=(-0.64, 0, 2.43), bev=0.02, parent=C)
    K.box("back_glass", (0.02, 1.0, 0.36), glass, loc=(-0.6, 0, 2.5), bev=0.005, parent=C)
    K.box("roof_back", (0.75, 2.24, 0.07), bone, loc=(-0.3, 0, 2.86), rot=(0, 4, 0), bev=0.02, parent=C)
    K.box("roof_front", (0.78, 2.2, 0.07), bone, loc=(0.26, 0, 2.62), rot=(0, 38, 2), bev=0.02, parent=C)
    K.box("screen_top", (0.1, 2.1, 0.1), steel, loc=(0.54, 0, 2.38), rot=(6, 0, 0), bev=0.015, parent=C)
    K.box("dent", (0.2, 1.4, 0.06), M(RUST, metal=0.3, wear=0.4), loc=(0.02, 0.1, 2.83), rot=(0, 20, 0), bev=0.01, parent=C)
    K.box("seat", (0.5, 1.6, 0.15), K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4), loc=(-0.35, 0, 2.1), bev=0.04, parent=C)
    K.torus("steer", 0.2, 0.02, dark, loc=(0.25, -0.5, 2.25), rot=(0, -50, 0), segs=14, parent=C)
    K.box("bumper", (0.18, 2.4, 0.2), dark, loc=(0.78, 0.05, 0.75), rot=(0, 8, -6), bev=0.02, parent=C)
    K.box("headlight", (0.04, 0.3, 0.16), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(0.69, 0.75, 1.15), bev=0.005, parent=C)
    K.box("headlight_hung", (0.12, 0.3, 0.16), M(BONE), loc=(0.82, -0.8, 0.85), rot=(30, 50, 0), bev=0.01, parent=C)
    cable(C, "lamp_wire", [(0.69, -0.75, 1.1), (0.78, -0.78, 0.98), (0.8, -0.8, 0.93)], 0.012, dark)
    label(C, "CW-14", (0.03, -1.155, 1.55), 0.16, M(STEEL), rot=(90, 0, 0))
    # bed: a steel-framed deck of boards, a ladder rack behind the cab
    D = 1.12
    for k in range(7):
        plank(P, f"bed_board{k}", (5.0, 0.32, 0.06), (-0.6, -0.96 + k * 0.32, D), shade=k)
    for s in (-1, 1):
        K.box(f"bed_edge{s}", (5.1, 0.08, 0.18), steel, loc=(-0.6, s * 1.15, D - 0.03), bev=0.015, parent=P)
        for k in range(5):
            K.box(f"pocket{s}{k}", (0.1, 0.06, 0.16), dark, loc=(-2.8 + k * 1.1, s * 1.2, D - 0.04), bev=0, parent=P)
    K.box("bed_tail", (0.08, 2.38, 0.18), steel, loc=(-3.12, 0, D - 0.03), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box(f"rack_post{s}", (0.08, 0.08, 1.3), steel, loc=(1.92, s * 1.08, D + 0.65), bev=0.012, parent=P)
    for k in range(3):
        K.box(f"rack_bar{k}", (0.06, 2.2, 0.06), steel, loc=(1.92, 0, D + 0.4 + k * 0.42), bev=0, parent=P)
    # the spare lying flat and a colony crate tipped onto it
    K.cyl("spare", 0.45, 0.28, K.mat("tyre", TYRE, rough=0.9, grime=0.4), loc=(-1.0, 0.15, D + 0.17), segs=20, bev=0.05, parent=P)
    K.cyl("spare_rim", 0.25, 0.3, M(STEEL, metal=0.4, wear=0.7), loc=(-1.0, 0.15, D + 0.17), segs=14, bev=0.01, parent=P)
    T = _grp(P, "crate_tip", (-1.05, -0.92, D + 0.03), (17, 0, 4))
    K.box("crate", (1.2, 1.2, 1.2), M(BONE, wear=0.6, grime=0.5), loc=(0, 0.6, 0.6), bev=0.03, parent=T)
    for k, (sx, sy, sz, lx, ly, lz) in enumerate(((1.24, 0.08, 0.1, 0, -0.02, 0.08), (1.24, 0.08, 0.1, 0, -0.02, 1.12), (0.1, 0.08, 1.2, -0.56, -0.02, 0.6),
                                                  (0.1, 0.08, 1.2, 0.56, -0.02, 0.6), (1.24, 1.24, 0.08, 0, 0.6, 1.2))):
        K.box(f"crate_frame{k}", (sx, sy, sz), M(STEEL, metal=0.3, wear=0.6), loc=(lx, ly, lz), bev=0.015, parent=T)
    FR.hazard(T, (0, -0.02, 0.9), 0.9, 0.14, M(MUSTARD), dark, n=5)
    for k in range(5):
        rust_patch(P, (rnd.uniform(-3.0, 1.6), -1.195, D - 0.03), (rnd.uniform(0.2, 0.5), 0.004, 0.12))
    return P


def tractor_wreck(seed=25):
    """An old colony farm tractor, mustard gone to rust, big lugged rear wheels, one front wheel gone so the swing axle
    has dropped onto its stub."""
    rnd = random.Random(seed)
    P = _root("tractor")
    paint = M(MUSTARD, wear=0.95, grime=0.75, scratches=0.8, moss=0.2)
    dark = M(GRAPHITE, metal=0.4, wear=0.6, grime=0.6)
    rust = M(RUST, metal=0.4, wear=0.5, grime=0.5)
    rim = M(BONE, metal=0.3, wear=0.85, grime=0.6)
    xr, xf = -0.75, 1.35
    tyre(P, "rear_l", (xr, -0.78, 0.76), 0.78, 0.42, flat=0.03, rim=rim, lugs=14)
    tyre(P, "rear_r", (xr, 0.78, 0.76), 0.78, 0.42, flat=0.03, rim=rim, lugs=14)
    # body pitched a little down at the front where the axle dropped
    B = _grp(P, "body", (0, 0, 0), (-3, 2.5, 0))
    K.slab("hood", 1.45, 0.62, 0.5, 0.08, paint, loc=(0.85, 0, 1.08), bev=0.025, parent=B)
    K.box("hood_seam", (1.4, 0.02, 0.01), dark, loc=(0.85, 0, 1.335), bev=0, parent=B)
    K.box("engine", (1.2, 0.5, 0.42), dark, loc=(0.85, 0, 0.66), bev=0.03, parent=B)
    K.box("grille", (0.08, 0.6, 0.55), dark, loc=(1.6, 0, 0.98), bev=0.02, parent=B)
    for k in range(5):
        K.box(f"grille_bar{k}", (0.03, 0.5, 0.025), paint, loc=(1.645, 0, 0.78 + k * 0.1), bev=0, parent=B)
    K.box("housing", (0.9, 0.7, 0.6), dark, loc=(-0.5, 0, 0.82), bev=0.04, parent=B)
    K.box("platform", (0.55, 1.0, 0.06), M(STEEL, metal=0.4, wear=0.7), loc=(-0.6, 0, 1.15), bev=0.01, parent=B)
    beam(B, "seat_post", (-0.75, 0, 1.15), (-0.85, 0, 1.45), 0.06, dark)
    K.box("seat", (0.42, 0.45, 0.08), dark, loc=(-0.88, 0, 1.48), rot=(0, -6, 0), bev=0.03, parent=B)
    K.box("seat_back", (0.06, 0.45, 0.3), dark, loc=(-1.1, 0, 1.62), rot=(0, -15, 0), bev=0.02, parent=B)
    beam(B, "column", (0.1, 0, 1.3), (-0.3, 0, 1.75), 0.06, dark)
    K.torus("steer", 0.2, 0.02, dark, loc=(-0.32, 0, 1.77), rot=(0, -40, 0), segs=20, parent=B)
    K.box("dash", (0.12, 0.5, 0.35), paint, loc=(0.1, 0, 1.22), bev=0.02, parent=B)
    rod(B, "stack", (1.15, -0.2, 1.3), (1.18, -0.2, 2.05), 0.05, rust, segs=10)
    K.cyl("stack_cap", 0.065, 0.04, rust, loc=(1.18, -0.2, 2.06), rot=(0, 25, 0), segs=10, bev=0, parent=B)
    rod(B, "air_intake", (0.95, 0.18, 1.3), (0.95, 0.18, 1.6), 0.07, dark, segs=10)
    for s in (-1, 1):
        K.box(f"lamp{s}", (0.1, 0.14, 0.12), dark, loc=(1.45, s * 0.33, 1.2), bev=0.015, parent=B)
        K.box(f"lens{s}", (0.01, 0.1, 0.08), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(1.505, s * 0.33, 1.2), bev=0, parent=B)
        shell_panel(f"fender{s}", 0.9, math.radians(15), math.radians(165), s * 0.53, s * 1.02, M(MUSTARD, wear=0.05, grime=0.5, scratches=0.5, moss=0.2), t=0.05,
                    parent=B, loc=(xr, 0, 0.78))
        K.box(f"fender_tie{s}", (0.06, 0.2, 0.5), dark, loc=(xr, s * 0.45, 1.25), bev=0.01, parent=B)
    for k in range(6):
        s = rnd.choice((-1, 1))
        rust_patch(B, (rnd.uniform(0.3, 1.45), s * 0.312, rnd.uniform(0.95, 1.25)), (rnd.uniform(0.12, 0.3), 0.004, rnd.uniform(0.08, 0.18)))
    rust_patch(B, (0.7, 0.05, 1.336), (0.5, 0.35, 0.004), rot=(0, 0, 15))
    # front axle: pivoted in the middle, the left end down on its stub, the right wheel still on
    A = _grp(P, "front_axle", (xf, 0, 0.5), (-14, 0, 0))
    K.box("axle_beam", (0.14, 1.1, 0.12), dark, loc=(0, 0, 0), bev=0.02, parent=A)
    K.cyl("pivot", 0.06, 0.2, dark, loc=(0, 0, 0), rot=(0, 90, 0), segs=10, bev=0, parent=A)
    tyre(A, "front_r", (0, 0.58, 0), 0.38, 0.18, flat=0.0, rim=rim)
    K.cyl("stub", 0.08, 0.16, rust, loc=(0, -0.6, 0), rot=(90, 0, 0), segs=10, bev=0.01, parent=A)
    K.cyl("stub_hub", 0.12, 0.05, rust, loc=(0, -0.67, 0), rot=(90, 0, 0), segs=12, bev=0.01, parent=A)
    # the lost wheel lying in the grass ahead
    tyre(P, "front_off", (2.25, -1.05, 0.09), 0.38, 0.18, rim=rim, axis="z")
    return P


def ibeam_long(seed=26):
    """A 6 m steel I-beam in steel-blue primer, laid across two timber blocks, rust along the flanges."""
    P = _root("ibeam")
    steel = M(STEEL, metal=0.45, wear=0.7, grime=0.55, scratches=0.6)
    L, H, F, tw, tf = 6.0, 0.36, 0.2, 0.025, 0.03
    z0 = 0.16
    G = _grp(P, "beam", (0, 0, z0), (0, 0, 2))
    K.box("web", (L, tw, H - 2 * tf), steel, loc=(0, 0, H / 2), bev=0.004, parent=G)
    for z in (tf / 2, H - tf / 2):
        K.box("flange", (L, F, tf), steel, loc=(0, 0, z), bev=0.006, parent=G)
    for s in (-1, 1):
        K.box(f"end_plate{s}", (0.02, F, H), steel, loc=(s * (L / 2 + 0.01), 0, H / 2), bev=0.004, parent=G)
        for z in (0.08, H - 0.08):
            for y in (-0.06, 0.06):
                bolt(G, (s * (L / 2 + 0.025), y, z), rot=(0, 90, 0))
        for y in (-1, 1):
            K.box(f"stiffener{s}{y}", (0.02, F / 2 - tw / 2, H - 2 * tf), steel, loc=(s * 1.2, y * (F / 4 + tw / 4), H / 2), bev=0.002, parent=G)
    for k, x in enumerate((-2.3, -0.4, 0.9, 2.1)):
        rust_patch(G, (x, -F / 2 + 0.03, H + 0.002), (0.3 + 0.1 * k, 0.05, 0.004))
        rust_patch(G, (x + 0.3, -tw / 2 - 0.002, H / 2 + 0.05 * (k % 2)), (0.4, 0.004, 0.1))
    label(G, "W360", (-1.8, -tw / 2 - 0.003, H / 2), 0.08, M(BONE, wear=0.6), rot=(90, 0, 0))
    for k, x in enumerate((-1.9, 1.9)):
        K.box(f"block{k}", (0.22, 0.6, z0), wood(k + 2, wear=0.6), loc=(x, 0.05 * k, z0 / 2), rot=(0, 0, 6 - 9 * k), bev=0.015, parent=P)
    return P


def large_pipe_corner(seed=27):
    """A 90-degree elbow of the big bone-white colony pipe, half sunk like the straight runs, steel flanges both ends."""
    P = _root("pipe_elbow")
    shell = M(BONE, wear=0.55, grime=0.6, moss=0.15)
    steel = M(STEEL, metal=0.4, wear=0.5)
    r, R = 0.6, 1.5
    zc = r * 0.55
    c = (-R / 2, -R / 2)
    K.torus("elbow", R, r, shell, loc=(c[0], c[1], zc), arc=90, segs=96, parent=P)
    K.torus("band", R, r + 0.012, M(MUSTARD, wear=0.5), loc=(c[0], c[1], zc), rot=(0, 0, 38), arc=14, segs=96, parent=P)
    for end, (px, py, rot, axis) in enumerate(((c[0] + R, c[1], (90, 0, 0), Vector((0, 1, 0))), (c[0], c[1] + R, (0, 90, 0), Vector((1, 0, 0))))):
        inward = axis  # the elbow runs on into +y from the first end and into +x from the second
        K.cyl(f"bore{end}", r - 0.01, 0.02, K.mat("pipe_in", "#1d1f22", rough=0.9), loc=Vector((px, py, zc)) + inward * 0.25, rot=rot, segs=24, bev=0, parent=P)
        ring = lathe(f"flange{end}", [(r - 0.06, -0.08), (r + 0.08, -0.08), (r + 0.08, 0.08), (r - 0.06, 0.08)], steel, segs=24, rot=rot, parent=P)
        ring.location = Vector((px, py, zc)) + inward * 0.07
        ring.data.polygons.foreach_set("use_smooth", [False] * len(ring.data.polygons))
        K.bevel(ring, 0.015, segs=2, angle=60)
        for k in range(8):
            a = 2 * math.pi * k / 8
            side = Vector((axis.y, axis.x, 0))
            p = Vector((px, py, zc)) + side * math.cos(a) * (r + 0.04) + Vector((0, 0, 1)) * math.sin(a) * (r + 0.04)
            FR.screw(P, p, rot, r=0.035, mat=M(ZINC, metal=0.6))
    return P


def traffic_cone(seed=28):
    """A colony road cone: mustard with two bone reflective bands, on a square graphite foot."""
    P = _root("cone")
    plastic = M(MUSTARD, wear=0.35, grime=0.5, scratches=0.3, rough=0.5)
    band = M(BONE, wear=0.2, grime=0.4, rough=0.4)
    K.box("foot", (0.38, 0.38, 0.035), M(GRAPHITE, rough=0.7, wear=0.3, grime=0.4), loc=(0, 0, 0.0175), bev=0.012, parent=P)
    r0, r1, h, z0 = 0.15, 0.03, 0.66, 0.035

    def rad(z):
        return r0 + (r1 - r0) * (z - z0) / h

    K.cyl("cone", r0, h, plastic, loc=(0, 0, z0 + h / 2), segs=24, r2=r1, bev=0.006, parent=P)
    for a, b in ((0.3, 0.38), (0.48, 0.55)):
        za, zb = z0 + a, z0 + b
        K.cyl(f"band{a}", rad(za) + 0.004, zb - za, band, loc=(0, 0, (za + zb) / 2), segs=24, r2=rad(zb) + 0.004, bev=0, parent=P)
    K.torus("lip", r0, 0.012, plastic, loc=(0, 0, z0 + 0.01), segs=24, parent=P)
    return P


def _sign_post(P, h, mat):
    K.box("post", (0.06, 0.06, h + 0.2), mat, loc=(0, 0.04, h / 2 - 0.1), bev=0.008, parent=P)
    K.box("post_cap", (0.07, 0.07, 0.02), mat, loc=(0, 0.04, h - 0.005), bev=0.004, parent=P)
    for k in range(int(h / 0.15)):
        K.box("post_hole", (0.022, 0.002, 0.022), K.mat("hole", "#141517", rough=0.9), loc=(0, 0.009, 0.25 + k * 0.15), bev=0, parent=P)


def _sign_back(P, pts, zc, w):
    """The galvanised back of a sign and its two clamps to the post."""
    plate_xz("sign_back", pts, 0.015, M(ZINC, metal=0.6, wear=0.5, grime=0.4), loc=(0, 0.004, zc), parent=P, bev=0.003)
    for dz in (-0.18, 0.18):
        K.box("clamp", (min(w * 0.6, 0.3), 0.025, 0.04), M(ZINC, metal=0.6), loc=(0, 0.075, zc + dz), bev=0.005, parent=P)
        bolt(P, (0, -0.012, zc + dz))


def _poly(n, r, a0=0.0):
    return [(r * math.cos(a0 + 2 * math.pi * k / n), r * math.sin(a0 + 2 * math.pi * k / n)) for k in range(n)]


def stop_sign(seed=29):
    """A colony stop sign: a signal-red octagon with a bone border and STOP stencilled across it, on a steel post."""
    P = _root("stop")
    _sign_post(P, 2.2, M(STEEL, metal=0.4, wear=0.6, grime=0.4))
    zc = 2.05
    a0 = math.pi / 8
    _sign_back(P, _poly(8, 0.39, a0), zc, 0.75)
    plate_xz("border", _poly(8, 0.38, a0), 0.012, M(BONE, wear=0.4, grime=0.4, rough=0.5), loc=(0, -0.01, zc), parent=P, bev=0.002)
    plate_xz("face", _poly(8, 0.345, a0), 0.012, M(SIGNAL, wear=0.5, grime=0.35, rough=0.5, scratches=0.4), loc=(0, -0.016, zc), parent=P, bev=0.002)
    label(P, "STOP", (0, -0.024, zc), 0.25, M(BONE, wear=0.3, grime=0.2, scratches=0), rot=(90, 0, 0), extrude=0.003)
    return P


def caution_sign(seed=30):
    """A colony caution sign: a mustard triangle with a graphite border and an exclamation mark."""
    P = _root("caution")
    _sign_post(P, 2.1, M(STEEL, metal=0.4, wear=0.6, grime=0.4))
    zc = 1.92
    tri = lambda r: _poly(3, r, math.pi / 2)  # noqa: E731
    _sign_back(P, tri(0.5), zc, 0.8)
    plate_xz("border", tri(0.49), 0.012, M(GRAPHITE, wear=0.3, grime=0.3, rough=0.5), loc=(0, -0.01, zc), parent=P, bev=0.002)
    plate_xz("face", tri(0.4), 0.012, M(MUSTARD, wear=0.5, grime=0.4, rough=0.5, scratches=0.4), loc=(0, -0.016, zc), parent=P, bev=0.002)
    ink = M(GRAPHITE, wear=0.2, scratches=0)
    plate_xz("bang", [(-0.03, 0.12), (0.03, 0.12), (0.018, -0.04), (-0.018, -0.04)], 0.004, ink, loc=(0, -0.024, zc), parent=P, bev=0)
    K.box("dot", (0.042, 0.004, 0.042), ink, loc=(0, -0.024, zc - 0.085), bev=0.003, parent=P)
    return P


def speed_limit_sign(seed=31):
    """A colony speed-limit sign: a bone disc in a signal-red ring, 40 in graphite."""
    P = _root("speed")
    _sign_post(P, 2.2, M(STEEL, metal=0.4, wear=0.6, grime=0.4))
    zc = 2.0
    _sign_back(P, _poly(32, 0.36), zc, 0.72)
    plate_xz("ring", _poly(32, 0.35), 0.012, M(SIGNAL, wear=0.5, grime=0.35, rough=0.5), loc=(0, -0.01, zc), parent=P, bev=0.002)
    plate_xz("face", _poly(32, 0.28), 0.012, M(BONE, wear=0.4, grime=0.4, rough=0.5, scratches=0.4), loc=(0, -0.016, zc), parent=P, bev=0.002)
    label(P, "40", (0, -0.024, zc), 0.3, M(GRAPHITE, wear=0.2, scratches=0), rot=(90, 0, 0), extrude=0.003)
    return P


def conveyor_section(seed=32):
    """A 4 m colony belt conveyor on A-frame legs: the belt torn off the last metre so the rollers show, and one leg
    buckled so that end sags."""
    rnd = random.Random(seed)
    P = _root("conveyor")
    steel = M(STEEL, metal=0.4, wear=0.6, grime=0.5)
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.4)
    zinc = M(ZINC, metal=0.6, wear=0.5, grime=0.4)
    belt = K.mat("belt", "#232527", rough=0.85, grime=0.3, cracks=0.2)
    L, Wd, zt = 4.0, 0.76, 0.86
    piv, tilt = -1.6, 4.0
    T = _grp(P, "top", (piv, 0, zt), (0, tilt, 0))   # +X end drops
    for s in (-1, 1):
        K.box(f"rail{s}", (L, 0.05, 0.2), steel, loc=(-piv, s * Wd / 2, 0), bev=0.01, parent=T)
        K.box(f"lip{s}", (L, 0.09, 0.03), steel, loc=(-piv, s * (Wd / 2 - 0.02), 0.1), bev=0.005, parent=T)
    for k in range(int(L / 0.36)):
        x = -piv - L / 2 + 0.25 + k * 0.36
        K.cyl(f"roller{k}", 0.045, Wd - 0.08, zinc, loc=(x, 0, 0.05), rot=(90, 0, 0), segs=10, bev=0, parent=T)
    for k in range(3):
        K.cyl(f"return{k}", 0.04, Wd - 0.08, zinc, loc=(-piv - 1.3 + k * 1.3, 0, -0.18), rot=(90, 0, 0), segs=10, bev=0, parent=T)
    for s in (-1, 1):
        K.cyl(f"drum{s}", 0.12, Wd - 0.04, dark, loc=(-piv + s * (L / 2 - 0.1), 0, 0.0), rot=(90, 0, 0), segs=16, bev=0.01, parent=T)
    bl = 2.9
    K.box("belt", (bl, Wd - 0.12, 0.012), belt, loc=(-piv - L / 2 + bl / 2, 0, 0.1), bev=0, parent=T)
    K.box("belt_wrap", (0.012, Wd - 0.12, 0.24), belt, loc=(-piv - L / 2 - 0.02, 0, -0.01), bev=0, parent=T)
    K.box("belt_flap", (0.5, Wd - 0.16, 0.012), belt, loc=(-piv - L / 2 + bl + 0.12, 0.03, -0.12), rot=(4, 62, 0), bev=0, parent=T)
    K.box("belt_return", (L - 0.3, Wd - 0.12, 0.012), belt, loc=(-piv, 0, -0.23), rot=(0, 0.6, 0), bev=0, parent=T)
    # drive at the high end: a framed motor case, a guard with hazard stripes
    FR.framed_box(T, "motor", (-piv - L / 2 + 0.15, -Wd / 2 - 0.22, -0.02), (0.36, 0.34, 0.3), M(BONE, wear=0.6, grime=0.5), steel, fw=0.04)
    K.cyl("shaft", 0.03, 0.2, dark, loc=(-piv - L / 2 + 0.15, -Wd / 2 - 0.02, 0.0), rot=(90, 0, 0), segs=8, bev=0, parent=T)
    FR.hazard(T, (-piv - L / 2 + 0.55, -Wd / 2 - 0.03, 0.0), 0.7, 0.14, M(MUSTARD), dark, n=6)
    label(T, "CV-2", (0.7 - piv - L / 2 + 0.9, -Wd / 2 - 0.03, 0.0), 0.1, M(BONE), rot=(90, 0, 0))
    # legs: straight pairs at the drive end and the middle, the far pair buckled
    tz = lambda x: zt - (x - piv) * math.sin(math.radians(tilt)) - 0.1  # noqa: E731
    for k, x in enumerate((-1.6, 0.0, 1.6)):
        for s in (-1, 1):
            top = (x, s * (Wd / 2 - 0.05), tz(x))
            if k == 2 and s < 0:
                knee = (x + 0.16, s * (Wd / 2 + 0.12), 0.32)
                beam(P, "leg_lo", (x + 0.04, s * (Wd / 2 - 0.02), 0.0), knee, 0.06, steel)
                beam(P, "leg_hi", knee, top, 0.06, steel)
                K.box("leg_kink", (0.08, 0.08, 0.08), M(RUST, metal=0.4), loc=knee, rot=(20, 30, 0), bev=0.01, parent=P)
            else:
                beam(P, f"leg{k}{s}", (x, s * (Wd / 2 + 0.04), 0.0), top, 0.06, steel)
            K.box(f"foot{k}{s}", (0.14, 0.14, 0.015), dark, loc=(x, s * (Wd / 2 + 0.04), 0.0075), bev=0, parent=P)
        beam(P, f"tie{k}", (x, -Wd / 2 + 0.02, 0.28), (x, Wd / 2 - 0.02, 0.28), 0.04, dark) if k < 2 else None
    for k in range(4):
        rust_patch(T, (-piv + rnd.uniform(-1.8, 1.8), -Wd / 2 - 0.027, rnd.uniform(-0.06, 0.06)), (rnd.uniform(0.15, 0.4), 0.004, 0.06))
    return P


# ================================================================== Salvager Tech
def drop_pod_crashed(seed=33):
    """A one-person Salvager drop pod come in at an angle and half buried: octagonal frustum of hull plates, scorched
    heat shield, its hatch thrown open on a dim cyan cockpit."""
    rnd = random.Random(seed)
    P = _root("droppod")
    hm, gm = _hm(), _gm()
    char = K.mat("char", "#2a2622", rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    dark = K.mat("hazk", DARK, rough=0.6)
    K.disc("scorch", 2.4, K.mat("charred", "#5d5a2c", rough=1.0, noise_scale=1.2), (0, 0.4), z=0.01, wobble=0.25, seed=seed).parent = P
    K.disc("scorch_core", 1.5, K.mat("burnt", SCORCH, rough=1.0, noise_scale=1.5), (0, 0.5), z=0.014, wobble=0.25, seed=seed + 1).parent = P
    G = _grp(P, "pod", (0, 0.55, -0.75), (-32, 0, 8))
    r0, r1, h0, h1 = 1.05, 0.6, 0.3, 2.3

    def rad(z):
        return r0 + (r1 - r0) * (z - h0) / (h1 - h0)

    rot8 = (0, 0, 22.5)
    K.cyl("shield", 1.12, 0.3, char, loc=(0, 0, 0.15), rot=rot8, segs=8, bev=0.03, parent=G)
    K.cyl("body", r0, h1 - h0, hm, loc=(0, 0, (h0 + h1) / 2), rot=rot8, segs=8, r2=r1, bev=0.03, parent=G)
    K.cyl("cap", r1, 0.25, gm, loc=(0, 0, h1 + 0.125), rot=rot8, segs=8, r2=0.4, bev=0.03, parent=G)
    K.cyl("chute", 0.25, 0.25, gm, loc=(0, 0, h1 + 0.37), segs=12, bev=0.02, parent=G)
    for z in (0.7, 1.9):
        K.cyl(f"ring{z}", rad(z) + 0.035, 0.12, gm, loc=(0, 0, z), rot=rot8, segs=8, r2=rad(z + 0.12) + 0.035, bev=0.015, parent=G)
    # the hatch on the -y face: a dark opening in a frame, the door swung out on its hinge
    slant = math.degrees(math.atan((r0 - r1) / (h1 - h0)))
    ap = lambda z: rad(z) * math.cos(math.radians(22.5))  # noqa: E731
    zc, hh, hw = 1.3, 0.95, 0.56
    H = _grp(G, "hatch", (0, -ap(zc), zc), (-slant, 0, 0))
    K.box("opening", (hw, 0.03, hh), dark, loc=(0, -0.005, 0), bev=0.005, parent=H)
    for s in (-1, 1):
        K.box(f"frame_v{s}", (0.07, 0.07, hh + 0.14), gm, loc=(s * (hw / 2 + 0.035), -0.02, 0), bev=0.012, parent=H)
        K.box(f"frame_h{s}", (hw, 0.07, 0.07), gm, loc=(0, -0.02, s * (hh / 2 + 0.035)), bev=0.012, parent=H)
    K.box("seat", (0.36, 0.02, 0.5), K.mat("mattress", "#4f6b62", rough=0.9, ao=0.4), loc=(0, -0.025, -0.1), bev=0.008, parent=H)
    K.box("headrest", (0.22, 0.02, 0.16), K.mat("mattress", "#4f6b62", rough=0.9, ao=0.4), loc=(0, -0.03, 0.24), bev=0.008, parent=H)
    K.box("screen", (0.26, 0.015, 0.08), glow(CYAN, 0.8), loc=(0.0, -0.025, 0.4), bev=0.003, parent=H)
    # the hatch was blown off its hinges and lies inside-up in the dirt in front
    D = _grp(P, "door", (-1.15, -1.25, 0.06), (82, 0, 35))
    K.box("door", (hw + 0.06, 0.06, hh + 0.06), hm, bev=0.015, parent=D)
    for x in (-0.15, 0.15):
        K.box("door_rib", (0.05, 0.05, hh - 0.1), gm, loc=(x, 0.05, 0), bev=0.01, parent=D)
    K.box("door_rib_h", (hw - 0.1, 0.05, 0.05), gm, loc=(0, 0.05, 0), bev=0.01, parent=D)
    K.box("door_handle", (0.05, 0.05, 0.2), gm, loc=(-hw / 2 + 0.05, -0.05, 0), bev=0.01, parent=D)
    for s in (-1, 1):
        K.cyl(f"hinge{s}", 0.03, 0.12, gm, loc=(hw / 2 + 0.07, -0.04, s * 0.32), segs=8, bev=0, parent=H)
    FR.hazard(H, (0, -0.06, -hh / 2 - 0.13), hw + 0.1, 0.07, glow(CYAN, 0.7), dark, n=6)
    # plate seams and copper lines on the other faces
    cu = _copper()
    for k in range(8):  # edge rails up the octagon's corners
        a = math.radians(22.5 + 45 * k)
        p0 = Vector((math.cos(a) * rad(0.4), math.sin(a) * rad(0.4), 0.4))
        p1 = Vector((math.cos(a) * rad(2.2), math.sin(a) * rad(2.2), 2.2))
        beam(G, f"edge{k}", p0, p1, 0.035, gm)
    for k in (1, 3):  # copper lines down two faces
        n = Vector((math.cos(math.radians(45 * k)), math.sin(math.radians(45 * k)), 0))
        rod(G, f"line{k}", n * (ap(0.5) + 0.04) + Vector((0, 0, 0.5)), n * (ap(2.1) + 0.04) + Vector((0, 0, 2.1)), 0.035, cu)
    label(G, "SV-04", (ap(1.4) + 0.006, 0, 1.4), 0.18, M(BONE, wear=0.4, grime=0.3), rot=(90 - slant, 0, 90))
    mound(P, "dirt", (0, 0.5), 1.15, 0.3, seed=seed, n=11)
    for k in range(4):
        K.box(f"clod{k}", (0.3, 0.25, 0.15), K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), loc=(rnd.uniform(-2, 2), rnd.uniform(-1.8, -1.2), 0.04),
              rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), rnd.uniform(0, 90)), bev=0.06, parent=P)
    return P


def hull_panel_debris(seed=34):
    """A torn plate off the SV-04's hull, 3 x 2 m, driven edge-first into the ground, its ribs showing behind."""
    P = _root("hullplate")
    hm, gm = _hm(), _gm()
    dark = K.mat("hazk", DARK, rough=0.6)
    G = _grp(P, "plate", (0, 0.2, -0.3), (-28, 0, 10))
    w, h, t = 3.0, 2.0, 0.12
    # the outline: square along the buried edge, jagged where it tore
    pts = [(-w / 2, 0), (w / 2, 0), (w / 2, h * 0.55), (w / 2 - 0.25, h * 0.7), (w / 2 - 0.15, h * 0.85), (w / 2 - 0.6, h * 0.92),
           (0.6, h), (0.25, h * 0.86), (-0.2, h * 0.97), (-0.7, h * 0.88), (-1.0, h), (-w / 2, h * 0.9)]
    plate_xz("skin", pts, t, hm, loc=(0, 0, 0), parent=G, bev=0.02)
    # framed construction: an edge rail on the intact sides, ribs and a stringer behind
    K.box("rail_l", (0.12, 0.12, h * 0.9), gm, loc=(-w / 2 + 0.06, 0.1, h * 0.45), bev=0.02, parent=G)
    K.box("rail_b", (w, 0.12, 0.12), gm, loc=(0, 0.1, 0.06 + 0.3), bev=0.02, parent=G)
    for k, x in enumerate((-0.75, 0.0, 0.75)):
        top = h * (0.9 if k != 1 else 0.85) - 0.1
        K.box(f"rib{k}", (0.1, 0.22, top), gm, loc=(x, 0.17, top / 2), bev=0.015, parent=G)
    K.box("stringer", (w * 0.75, 0.16, 0.08), gm, loc=(-0.3, 0.14, h * 0.62), bev=0.015, parent=G)
    K.box("rib_bent", (0.1, 0.22, 0.6), gm, loc=(1.42, 0.25, h * 0.5), rot=(35, 0, -25), bev=0.015, parent=G)
    # front: seams, rivets, a cyan running-light strip gone dark and the end of the ship's letters
    for x in (-0.5, 0.55):
        K.box(f"seam{x}", (0.03, 0.01, h * 0.85), dark, loc=(x, -t / 2 - 0.004, h * 0.43), bev=0, parent=G)
        for k in range(5):
            FR.screw(G, (x + 0.07, -t / 2 - 0.006, 0.45 + k * 0.28), (90, 0, 0), r=0.025, mat=gm)
    K.box("run_light", (2.0, 0.03, 0.06), glow(CYAN, 0.6), loc=(-0.3, -t / 2 - 0.01, h * 0.72), bev=0.008, parent=G)
    label(G, "04", (0.1, -t / 2 - 0.006, h * 0.4), 0.42, M(BONE, wear=0.5, grime=0.3), rot=(90, 0, 0))
    FR.hazard(G, (-0.95, -t / 2 - 0.02, 0.5), 0.8, 0.16, M(MUSTARD), dark, n=5)
    cu = _copper()
    for k in range(3):
        x = 0.2 + k * 0.22
        cable(G, f"wire{k}", [(x, 0.2, h * 0.85), (x + 0.1, 0.1 - k * 0.05, h * 0.95 + 0.05 * k), (x + 0.35, -0.15, h * 0.9 - 0.12 * k)], 0.03, cu)
    mound(P, "dirt", (0, 0.15), 0.45, 0.14, seed=seed, n=12, sx=3.4)
    return P


def cargo_pod(seed=35):
    """A Salvager cargo pod thrown out of the hold: a banded capsule on its side, landing feet in the air, latches down
    its seam and a dim cyan status strip still on."""
    P = _root("cargopod")
    hm, gm = _hm(), _gm()
    dark = K.mat("hazk", DARK, rough=0.6)
    r, L = 0.7, 2.1
    G = _grp(P, "pod", (0, 0, r - 0.04), (12, 0, 0))
    K.cyl("body", r, L, hm, loc=(0, 0, 0), rot=(0, 90, 0), segs=16, bev=0.02, parent=G)
    for s in (-1, 1):
        K.cyl(f"end{s}", r, 0.35, hm, loc=(s * (L / 2 + 0.175), 0, 0), rot=(0, 90, 0), segs=16, r2=r * 0.66 if s > 0 else r, bev=0.02, parent=G)
        if s < 0:
            K.cyl("end_cone", r, 0.3, hm, loc=(-(L / 2 + 0.5), 0, 0), rot=(0, -90, 0), segs=16, r2=r * 0.6, bev=0.02, parent=G)
        K.cyl(f"endplate{s}", r * 0.55, 0.06, hm, loc=(s * (L / 2 + 0.38 if s > 0 else L / 2 + 0.66), 0, 0), rot=(0, 90, 0), segs=16, bev=0.015, parent=G)
    for x in (-0.6, 0.0, 0.6):
        K.cyl(f"band{x}", r + 0.03, 0.12, gm, loc=(x, 0, 0), rot=(0, 90, 0), segs=16, bev=0.015, parent=G)
    # the lengthwise seam with its latches, on top
    K.box("seam", (L, 0.03, 0.02), dark, loc=(0, 0, r + 0.005), bev=0, parent=G)
    for k, x in enumerate((-0.85, -0.3, 0.3, 0.85)):
        K.box(f"latch{k}", (0.12, 0.14, 0.05), gm, loc=(x, 0, r + 0.02), bev=0.01, parent=G)
        K.box(f"latch_lever{k}", (0.04, 0.2, 0.03), M(MUSTARD, wear=0.5), loc=(x, -0.04, r + 0.05), rot=(18, 0, 0), bev=0.006, parent=G)
    # the status strip on the side facing -y and a stencilled label
    K.box("status", (0.4, 0.03, 0.05), glow(CYAN, 0.9), loc=(-0.3, -(r + 0.005) * math.cos(math.radians(25)), (r + 0.005) * math.sin(math.radians(25))),
          rot=(-25, 0, 0), bev=0.006, parent=G)
    K.box("status_frame", (0.46, 0.02, 0.1), gm, loc=(-0.3, -(r - 0.005) * math.cos(math.radians(25)), (r - 0.005) * math.sin(math.radians(25))),
          rot=(-25, 0, 0), bev=0.006, parent=G)
    label(G, "CARGO", (-0.3, -(r + 0.01), -0.02), 0.12, M(BONE, wear=0.5, grime=0.3), rot=(90, 0, 0))
    FR.hazard(G, (0.6, -(r + 0.015), -0.05), 0.45, 0.12, M(MUSTARD), dark, n=4)
    for k in range(4):  # grab rails
        a = math.radians(45 + 90 * k)
        y, z = math.cos(a) * (r + 0.07), math.sin(a) * (r + 0.07)
        if z < -0.3:
            continue
        K.box(f"rail{k}", (0.8, 0.04, 0.04), gm, loc=(0.0, y, z), bev=0.008, parent=G)
    # landing feet on the narrow end, sticking out sideways now
    for k in range(3):
        a = math.radians(90 + 120 * k)
        y, z = math.cos(a) * 0.35, math.sin(a) * 0.35
        beam(G, f"foot{k}", (L / 2 + 0.3, y, z), (L / 2 + 0.75, y * 1.6, z * 1.6), 0.07, gm)
        K.cyl(f"pad{k}", 0.1, 0.04, gm, loc=(L / 2 + 0.77, y * 1.6, z * 1.6), rot=(0, 90, 0), segs=10, bev=0.01, parent=G)
    return P


def wing_fragment(seed=36):
    """A 5 m piece of a wing broken off at the root: a tapered airfoil of hull plates, nav light at the tip, ribs and
    copper cables hanging out of the tear."""
    rnd = random.Random(seed)
    P = _root("wing")
    hm, gm = _hm(), _gm()
    dark = K.mat("hazk", DARK, rough=0.6)
    span, c0, c1, t0, t1 = 5.0, 2.2, 1.2, 0.32, 0.16
    G = _grp(P, "wing", (0, 0, 0.0), (6, 3, 4))

    def section(x, c, t, sweep):
        y0 = sweep - c / 2
        return [(x, y0, 0.0), (x, y0 + 0.18 * c, t / 2), (x, y0 + 0.65 * c, t * 0.4), (x, y0 + c, 0.02), (x, y0 + 0.65 * c, -t * 0.3),
                (x, y0 + 0.18 * c, -t * 0.35)]

    bm = bmesh.new()
    s0 = [bm.verts.new(p) for p in section(-span / 2, c0, t0, 0.0)]
    s1 = [bm.verts.new(p) for p in section(span / 2, c1, t1, 0.65)]
    bm.faces.new(s0)
    bm.faces.new(list(reversed(s1)))
    n = len(s0)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((s0[i], s0[j], s1[j], s1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm("skin", bm, hm)
    K.bevel(ob, 0.02, segs=2)
    K.place(ob, (0, 0, t0 * 0.35 + 0.02), parent=G)
    zc = t0 * 0.35 + 0.02

    def surf(x):
        f = (x + span / 2) / span
        return c0 + (c1 - c0) * f, t0 + (t1 - t0) * f, 0.65 * f

    # panel lines and rivet rows across the top, a hazard band near the root
    for k, x in enumerate((-1.2, 0.2, 1.4)):
        c, t, sw = surf(x)
        K.box(f"panel{k}", (0.03, c * 0.8, 0.01), dark, loc=(x, sw - c * 0.05, zc + t * 0.45), bev=0, parent=G)
    c, t, sw = surf(-1.9)
    for k in range(6):  # a hazard band across the top near the root
        K.box(f"haz{k}", (0.25, c * 0.7 / 6 * 1.05, 0.006), M(MUSTARD) if k % 2 == 0 else dark,
              loc=(-1.9, sw - c * 0.45 + c * 0.7 / 6 * (k + 0.5), zc + t * 0.46), bev=0, parent=G)
    c, t, sw = surf(0.8)
    label(G, "SV-04", (0.8, sw - 0.1, zc + t * 0.46), 0.3, M(BONE, wear=0.5, grime=0.3), rot=(0, 0, 0))
    # the tip: a fairing and the dead nav light
    c, t, sw = surf(span / 2)
    K.box("tip_cap", (0.08, c * 0.9, t * 0.8), gm, loc=(span / 2 + 0.03, sw + 0.02, zc), bev=0.02, parent=G)
    K.sphere("nav", 0.07, glow(CYAN, 0.6), loc=(span / 2 + 0.07, sw - c / 2 + 0.2, zc), parent=G)
    # the root: torn plates, rib ends and cables
    c, t, sw = surf(-span / 2)
    for k in range(7):
        K.box(f"torn{k}", (0.35, rnd.uniform(0.2, 0.4), 0.03), hm if k % 2 else gm, loc=(-span / 2 - 0.12, -c / 2 + 0.2 + k * 0.3, zc + rnd.uniform(-0.1, 0.1)),
              rot=(rnd.uniform(-25, 25), rnd.uniform(-35, 35), rnd.uniform(-30, 30)), bev=0.01, parent=G)
    for k in range(3):
        K.box(f"spar{k}", (0.5, 0.06, t * 0.6), gm, loc=(-span / 2 - 0.15, -c / 2 + 0.5 + k * 0.6, zc), rot=(0, 0, rnd.uniform(-8, 8)), bev=0.01, parent=G)
    cu = _copper()
    for k in range(4):
        y = -0.6 + k * 0.35
        cable(G, f"cable{k}", [(-span / 2, y, zc), (-span / 2 - 0.35, y - 0.05, zc - 0.05), (-span / 2 - 0.6 - 0.1 * k, y + 0.15, 0.03 - zc + 0.0)], 0.03, cu)
    return P


def thruster_cone(seed=37):
    """A main thruster bell, 2 m across the exit, fallen on its side: gunmetal outside with cooling bands, scorched
    black inside, the combustion chamber and copper feed lines at its throat."""
    P = _root("thruster")
    hm, gm = _hm(), _gm()
    char = K.mat("char", "#2a2622", rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    soot = K.mat("soot", "#1d1b19", rough=1.0)
    Re, Rt, Lb = 1.0, 0.42, 1.9
    # bell axis along x, exit at +x; it rests on its lip (r Re + 0.08 at x = Lb) and its chamber band (r 0.53 at x = -0.25)
    t = math.atan((Re + 0.08 - 0.53) / (Lb + 0.25))
    zc = 0.53 * math.cos(t) + 0.25 * math.sin(t) - 0.01
    G = _grp(P, "bell", (-0.7, 0, zc), (0, -math.degrees(t), 0))

    def r_at(f):
        return Rt + (Re - Rt) * (f ** 1.6)

    n = 8
    outer = [(r_at(i / n) + 0.04, Lb * i / n) for i in range(n + 1)]
    inner = [(r_at(i / n) - 0.0, Lb * i / n) for i in range(n, -1, -1)]
    lathe("bell", outer + inner, hm, segs=16, loc=(0, 0, 0), rot=(0, 90, 0), parent=G)
    liner = [(r_at(i / n) - 0.002, Lb * i / n) for i in range(n + 1)] + [(r_at(i / n) - 0.02, Lb * i / n) for i in range(n, -1, -1)]
    lathe("liner", liner, char, segs=16, rot=(0, 90, 0), parent=G)
    K.torus("lip", Re + 0.03, 0.05, gm, loc=(Lb, 0, 0), rot=(0, 90, 0), segs=40, parent=G)
    for f in (0.35, 0.7):
        K.torus(f"band{f}", r_at(f) + 0.05, 0.035, gm, loc=(Lb * f, 0, 0), rot=(0, 90, 0), segs=28, parent=G)
    for k in range(10):  # cooling channels following the bell's curve
        a = 2 * math.pi * k / 10
        d = Vector((0, math.cos(a), math.sin(a)))
        fs = (0.02, 0.4, 0.7, 0.97)
        pts = [Vector((Lb * f, 0, 0)) + d * (r_at(f) + 0.055) for f in fs]
        for j in range(3):
            rod(G, f"channel{k}_{j}", pts[j], pts[j + 1], 0.022, gm, segs=6)
    # the throat: a framed combustion chamber, an injector dome and feed lines
    K.cyl("chamber", 0.5, 0.5, gm, loc=(-0.25, 0, 0), rot=(0, 90, 0), segs=16, bev=0.03, parent=G)
    K.cyl("chamber_band", 0.53, 0.1, hm, loc=(-0.3, 0, 0), rot=(0, 90, 0), segs=16, bev=0.015, parent=G)
    K.sphere("dome", 0.42, hm, loc=(-0.5, 0, 0), scale=(0.55, 1, 1), parent=G)
    K.torus("gimbal", 0.6, 0.05, gm, loc=(-0.1, 0, 0), rot=(0, 90, 0), segs=28, parent=G)
    for s in (-1, 1):
        K.box(f"gimbal_lug{s}", (0.18, 0.12, 0.2), gm, loc=(-0.1, s * 0.66, 0), bev=0.015, parent=G)
    cu = _copper()
    for k in range(3):
        a = math.radians(30 + 120 * k)
        d = Vector((0, math.cos(a), math.sin(a)))
        cable(G, f"feed{k}", [Vector((-0.6, 0, 0)) + d * 0.2, Vector((-0.6, 0, 0)) + d * 0.6, Vector((0.15, 0, 0)) + d * 0.56], 0.04, cu)
    K.torus("coil", 0.5, 0.03, cu, loc=(-0.15, 0, 0), rot=(0, 90, 0), segs=20, parent=G)
    # soot streaks round the exit and a burnt patch in the grass in front of it
    K.disc("burn", 1.4, K.mat("charred", "#5d5a2c", rough=1.0, noise_scale=1.2), (1.5, 0), z=0.01, wobble=0.3, seed=seed).parent = P
    K.disc("burn_core", 0.7, K.mat("burnt", SCORCH, rough=1.0, noise_scale=1.5), (1.4, 0), z=0.014, wobble=0.3, seed=seed + 1).parent = P
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.4
        d = Vector((0, math.cos(a), math.sin(a)))
        streak = K.box(f"streak{k}", (0.5, 0.2, 0.005), soot, loc=Vector((Lb - 0.3, 0, 0)) + d * (r_at(0.85) + 0.062), parent=G, bev=0)
        streak.rotation_euler = d.to_track_quat("Z", "X").to_euler()
    return P


def supply_cache(seed=38):
    """A Salvager supply crate dropped for the player: a framed hull-grey case with a lid, handles and a cyan beacon
    blinking on top."""
    P = _root("cache")
    hm, gm = _hm(), _gm()
    dark = K.mat("hazk", DARK, rough=0.6)
    w, d, h = 1.2, 0.8, 0.62
    for s in (-1, 1):
        K.box(f"skid{s}", (w, 0.1, 0.08), gm, loc=(0, s * (d / 2 - 0.08), 0.04), bev=0.012, parent=P)
    FR.framed_box(P, "case", (0, 0, 0.08 + h / 2), (w, d, h), hm, gm, fw=0.07)
    FR.framed_box(P, "lid", (0, 0, 0.08 + h + 0.07), (w + 0.04, d + 0.04, 0.14), gm, gm, fw=0.06)
    K.box("lid_seal", (w - 0.02, d - 0.02, 0.025), dark, loc=(0, 0, 0.08 + h + 0.005), bev=0, parent=P)
    zt = 0.08 + h + 0.14
    K.box("lid_panel", (w - 0.2, d - 0.2, 0.02), hm, loc=(0, 0, zt + 0.005), bev=0.008, parent=P)
    # the beacon: a short mast with a caged cyan lamp
    K.cyl("beacon_base", 0.1, 0.05, gm, loc=(0.38, 0.2, zt + 0.03), segs=12, bev=0.01, parent=P)
    K.cyl("beacon_lamp", 0.06, 0.12, glow(CYAN, 2.5), loc=(0.38, 0.2, zt + 0.11), segs=12, bev=0.01, parent=P)
    K.cyl("beacon_top", 0.075, 0.025, gm, loc=(0.38, 0.2, zt + 0.185), segs=12, bev=0.005, parent=P)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        K.box(f"cage{k}", (0.015, 0.015, 0.14), gm, loc=(0.38 + math.cos(a) * 0.08, 0.2 + math.sin(a) * 0.08, zt + 0.11), bev=0, parent=P)
    K.point("beacon_light", (0.38, 0.2, zt + 0.3), CYAN, 15, 0.05).parent = P
    # front: latches, label, cyan hazard band and a small status screen; handles on the ends
    for x in (-0.38, 0.38):
        K.box(f"latch{x}", (0.1, 0.05, 0.14), gm, loc=(x, -d / 2 - 0.04, 0.08 + h - 0.02), bev=0.01, parent=P)
        K.box(f"latch_lever{x}", (0.06, 0.03, 0.1), M(MUSTARD, wear=0.5), loc=(x, -d / 2 - 0.07, 0.08 + h - 0.04), rot=(-12, 0, 0), bev=0.005, parent=P)
    label(P, "SUPPLY", (-0.15, -d / 2 - 0.005, 0.44), 0.11, M(BONE, wear=0.4, grime=0.2), rot=(90, 0, 0))
    FR.screen(P, (0.32, -d / 2 - 0.02, 0.44), 0.2, 0.1, gm, glow(CYAN, 1.5))
    FR.hazard(P, (0, -d / 2 - 0.005, 0.22), w - 0.3, 0.08, glow(CYAN, 0.8), dark, n=8)
    for s in (-1, 1):
        K.box(f"handle{s}", (0.04, 0.34, 0.04), gm, loc=(s * (w / 2 + 0.08), 0, 0.5), bev=0.01, parent=P)
        for y in (-0.15, 0.15):
            K.box(f"handle_post{s}{y}", (0.08, 0.04, 0.04), gm, loc=(s * (w / 2 + 0.04), y, 0.5), bev=0, parent=P)
    return P


def solar_panel_array(seed=39):
    """Four Salvager solar panels on a tilted gunmetal rack, one cracked across and slipped in its frame, a junction
    box with a dim status light."""
    rnd = random.Random(seed)
    P = _root("solar")
    gm, hm = _gm(), _hm()
    cell = K.mat("cell", CELL, rough=0.15, metal=0.4, grime=0.25)
    line = K.mat("cell_line", "#8fa3b0", rough=0.4, metal=0.5)
    pw, ph, tilt = 1.0, 1.6, 30
    n = 4
    W = n * (pw + 0.06)
    z0 = 0.75
    T = _grp(P, "rack", (0, 0, z0), (tilt, 0, 0))   # top edge towards +y, faces the camera's side (-y) and up
    for s in (-1, 1):
        K.box(f"rail{s}", (W + 0.1, 0.06, 0.08), gm, loc=(0, s * ph / 2, -0.05), bev=0.01, parent=T)
    for k in range(n + 1):
        K.box(f"purlin{k}", (0.05, ph + 0.06, 0.08), gm, loc=(-W / 2 + k * (pw + 0.06), 0, -0.05), bev=0.01, parent=T)
    for k in range(n):
        x = -W / 2 + (pw + 0.06) * (k + 0.5)
        cracked = k == 2
        Q = _grp(T, f"panel{k}", (x, 0, 0), (0, 4, -3) if cracked else (0, 0, 0))
        K.box("frame", (pw, ph, 0.045), M(ZINC, metal=0.6, wear=0.4), loc=(0, 0, 0.0), bev=0.008, parent=Q)
        K.box("cells", (pw - 0.06, ph - 0.06, 0.012), cell, loc=(0, 0, 0.022), bev=0, parent=Q)
        for i in range(1, 4):
            K.box("grid_v", (0.008, ph - 0.07, 0.004), line, loc=(-pw / 2 + 0.03 + i * (pw - 0.06) / 4, 0, 0.029), bev=0, parent=Q)
        for i in range(1, 6):
            K.box("grid_h", (pw - 0.07, 0.008, 0.004), line, loc=(0, -ph / 2 + 0.03 + i * (ph - 0.06) / 6, 0.029), bev=0, parent=Q)
        if cracked:
            cx, cy = 0.1, 0.15
            for j in range(6):
                a = rnd.uniform(0, 180) + j * 30
                L = rnd.uniform(0.35, 0.75)
                K.box(f"crack{j}", (L, 0.01, 0.004), K.mat("crack", "#c9d6dc", rough=0.3),
                      loc=(cx + math.cos(math.radians(a)) * L / 2, cy + math.sin(math.radians(a)) * L / 2, 0.031), rot=(0, 0, a), bev=0, parent=Q)
            K.box("chip", (0.2, 0.25, 0.013), gm, loc=(pw / 2 - 0.13, ph / 2 - 0.15, 0.023), rot=(0, 0, 20), bev=0, parent=Q)
    # legs: short at the front, tall at the back, braced
    hb = z0 + (ph / 2) * math.sin(math.radians(tilt)) - 0.07
    hf = z0 - (ph / 2) * math.sin(math.radians(tilt)) - 0.07
    yb, yf = (ph / 2) * math.cos(math.radians(tilt)) + 0.04, -(ph / 2) * math.cos(math.radians(tilt)) + 0.04
    for k, x in enumerate((-W / 2 + 0.1, 0.0, W / 2 - 0.1)):
        beam(P, f"leg_f{k}", (x, yf, 0), (x, yf, hf), 0.07, gm)
        beam(P, f"leg_b{k}", (x, yb, 0), (x, yb, hb), 0.07, gm)
        beam(P, f"brace{k}", (x, yf, 0.08), (x, yb, hb - 0.1), 0.04, gm)
        for y in (yf, yb):
            K.box(f"foot{k}{y}", (0.18, 0.18, 0.03), hm, loc=(x, y, 0.015), bev=0.006, parent=P)
    # the junction box on the middle back leg, a cable to the ground
    FR.framed_box(P, "junction", (0.0, yb + 0.12, 0.6), (0.3, 0.14, 0.36), hm, gm, fw=0.04)
    K.box("junction_led", (0.04, 0.02, 0.04), glow(CYAN, 1.0), loc=(0.08, yb + 0.04, 0.7), bev=0.004, parent=P)
    cable(P, "lead", [(0.0, yb + 0.12, 0.42), (0.05, yb + 0.25, 0.12), (0.3, yb + 0.6, 0.03), (0.9, yb + 0.7, 0.03)], 0.025, K.mat("cable", "#2b2d30", rough=0.6))
    return P


def salvage_beacon(seed=40):
    """A Salvager marker beacon on a tripod, about 2 m tall: framed head, dim cyan lamp in a cage, whip antenna."""
    P = _root("beacon")
    hm, gm = _hm(), _gm()
    dark = K.mat("hazk", DARK, rough=0.6)
    zh = 1.15
    for k in range(3):
        a = math.radians(90 + 120 * k)
        foot = (math.cos(a) * 0.65, math.sin(a) * 0.65, 0.02)
        beam(P, f"leg{k}", foot, (math.cos(a) * 0.08, math.sin(a) * 0.08, zh - 0.15), 0.05, gm)
        K.cyl(f"pad{k}", 0.08, 0.03, hm, loc=foot, segs=10, bev=0.005, parent=P)
        K.cyl(f"spike{k}", 0.02, 0.15, gm, loc=(foot[0], foot[1], -0.04), segs=6, r2=0.003, bev=0, parent=P)
        beam(P, f"brace{k}", (math.cos(a) * 0.38, math.sin(a) * 0.38, 0.45), (0, 0, 0.62), 0.025, gm)
    K.cyl("collar", 0.11, 0.1, gm, loc=(0, 0, zh - 0.12), segs=12, bev=0.01, parent=P)
    FR.framed_box(P, "head", (0, 0, zh + 0.15), (0.36, 0.3, 0.42), hm, gm, fw=0.045)
    FR.screen(P, (0, -0.17, zh + 0.22), 0.18, 0.08, gm, glow(CYAN, 1.2))
    FR.hazard(P, (0, -0.155, zh + 0.03), 0.28, 0.05, glow(CYAN, 0.7), dark, n=6)
    label(P, "SV-04", (0, -0.165, zh + 0.1), 0.05, M(BONE, wear=0.3), rot=(90, 0, 0))
    cu = _copper()
    K.cyl("coil", 0.05, 0.1, cu, loc=(0.2, 0, zh + 0.1), rot=(0, 90, 0), segs=10, bev=0.01, parent=P)
    for k in range(3):
        K.box(f"coil_wrap{k}", (0.012, 0.11, 0.11), M(GRAPHITE), loc=(0.17 + k * 0.03, 0, zh + 0.1), bev=0, parent=P)
    # the lamp on top: a dim cyan lens under a wire cage
    zt = zh + 0.36
    K.cyl("lamp_base", 0.09, 0.05, gm, loc=(0, 0, zt + 0.025), segs=12, bev=0.008, parent=P)
    K.cyl("lamp", 0.065, 0.14, glow(CYAN, 1.0), loc=(0, 0, zt + 0.12), segs=12, bev=0.01, parent=P)
    K.cyl("lamp_cap", 0.08, 0.025, gm, loc=(0, 0, zt + 0.205), segs=12, bev=0.005, parent=P)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        K.box(f"cage{k}", (0.012, 0.012, 0.16), gm, loc=(math.cos(a) * 0.085, math.sin(a) * 0.085, zt + 0.12), bev=0, parent=P)
    # whip antenna off the back corner
    rod(P, "antenna_base", (0.12, 0.1, zt), (0.12, 0.1, zt + 0.1), 0.02, gm)
    rod(P, "antenna", (0.12, 0.1, zt + 0.1), (0.15, 0.13, zt + 0.95), 0.007, M(ZINC, metal=0.6), segs=6)
    K.sphere("antenna_tip", 0.015, gm, loc=(0.15, 0.13, zt + 0.95), parent=P)
    return P


PROPS = {
    "pallet": pallet,
    "forklift_wreck": forklift_wreck,
    "car_wreck": car_wreck,
    "truck_wreck": truck_wreck,
    "tractor_wreck": tractor_wreck,
    "ibeam_long": ibeam_long,
    "large_pipe_corner": large_pipe_corner,
    "traffic_cone": traffic_cone,
    "stop_sign": stop_sign,
    "caution_sign": caution_sign,
    "speed_limit_sign": speed_limit_sign,
    "conveyor_section": conveyor_section,
    "drop_pod_crashed": drop_pod_crashed,
    "hull_panel_debris": hull_panel_debris,
    "cargo_pod": cargo_pod,
    "wing_fragment": wing_fragment,
    "thruster_cone": thruster_cone,
    "supply_cache": supply_cache,
    "solar_panel_array": solar_panel_array,
    "salvage_beacon": salvage_beacon,
}
