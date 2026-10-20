"""Settler Patchwork farm props for the scenes between places (docs/world-generation.md, Scenes): what the survivors
who went back to the land keep about their fields and yards, in the Settler Patchwork family of docs/art-direction.md
(scavenged wood, corrugated sheet patches, rope, sacking and the faded colony colours).

A small corrugated silo, an open hay loft, a feed trough, a pig pen, a goat shed, milk churns on their stand, a butter
churn, a hand cart of seed sacks, an old plough, a rusty harrow, a tool rack of scythes and rakes, pitchforks leaning on
a barrel, crates of vegetables, a pile of pumpkins and gourds, a corn crib, a hand-cranked irrigation pump, a sprinkler
pipe, a cold frame, a hoop greenhouse, a potting bench, water butts on a stand, a dovecote, a rabbit hutch, straw bee
skeps on a shelf and a loose haystack round its pole.

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
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, STONE, shell_panel
from props import _root, wood, plank, post, bolt, STRAW, STRAW_D
from props_settler_a import rod, beam, prism, lump, corrugated, sack, staves, tin, rope_mat, SACK, SACK_D, SACK_L, GRAIN, SOIL
from props_colony_a import stencil
from props_colony_b import cable

HAY = "#cdb36a"
MUD, MUD_D = "#4f3a28", "#3a2a1d"
HOSE = "#46553f"
CLAY = "#9a5a44"
LEAF, LEAF_D = "#5e7f3a", "#46642c"
POTATO, TOMATO, BEET = "#a88a5c", "#b8443a", "#6e3046"
OLIVE, GOURD_G, GOURD_W, GOURD_D = "#7d7a3a", "#a9b98a", "#d6cfb4", "#4f6a3a"
CORN = "#d6b448"
GLASS, PLASTIC = "#8fa9ae", "#dfe5d8"
DARK = "#151311"


# ---------------------------------------------------------------- helpers
def _grp(P, name, loc=(0, 0, 0), rot=(0, 0, 0)):
    g = K.link(bpy.data.objects.new(name, None))
    g.parent = P
    return K.place(g, loc, rot)


def _aim(P, name, a, b):
    """An empty at a whose local z points at b (tools leaning on things); returns it and the distance."""
    a, b = Vector(a), Vector(b)
    g = _grp(P, name)
    g.location = a
    g.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return g, (b - a).length


def _iron(col=GRAPHITE, **kw):
    kw.setdefault("metal", 0.55)
    kw.setdefault("wear", 0.25)
    kw.setdefault("grime", 0.45)
    return M(col, **kw)


def _rust():
    return M(RUST, metal=0.45, wear=0.25, grime=0.5, rough=0.8)


def _straw(dark=False):
    return K.mat("strawd" if dark else "straw", STRAW_D if dark else STRAW, rough=0.95, grime=0.25, noise_scale=20.0 if dark else 12.0)


def _straw_bed():
    """Loose hay trodden flat on the ground: pale, so a wide patch never reads as an orange mat."""
    return K.mat("hay_bed", HAY, rough=1.0, grime=0.35, noise_scale=14.0, cracks=0.2)


def _soil():
    return K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)


def _dark():
    return K.mat("pit", DARK, rough=1.0)


def turned(P, name, prof, mat, segs=16, loc=(0, 0, 0), rot=(0, 0, 0)):
    """A solid of revolution: an open (r, z) profile from bottom to top turned about Z, a point where r is 0, its ends
    capped."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-4:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * j / segs), r * math.sin(2 * math.pi * j / segs), z)) for j in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for j in range(segs):
            j2 = (j + 1) % segs
            if len(a) == 1 and len(b) > 1:
                bm.faces.new((a[0], b[j], b[j2]))
            elif len(b) == 1 and len(a) > 1:
                bm.faces.new((a[j], a[j2], b[0]))
            elif len(a) > 1:
                bm.faces.new((a[j], a[j2], b[j2], b[j]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(50))
    return K.place(ob, loc, rot, P)


def corrugated_tube(P, name, R, z0, z1, mat, a0=0.0, a1=2 * math.pi, ribs=40, amp=0.018):
    """A corrugated sheet rolled round Z (a silo wall, or a patch on one): ribs run up it, its face outwards."""
    bm = bmesh.new()
    full = abs(a1 - a0 - 2 * math.pi) < 1e-6
    n = max(8, int(round(ribs * (a1 - a0) / (2 * math.pi) * 4)))
    m = n if full else n + 1
    cols = []
    for i in range(m):
        a = a0 + (a1 - a0) * i / n
        r = R + amp * math.sin(ribs * a)
        cols.append((bm.verts.new((r * math.cos(a), r * math.sin(a), z0)), bm.verts.new((r * math.cos(a), r * math.sin(a), z1))))
    for i in range(n):
        j = (i + 1) % m
        bm.faces.new((cols[i][0], cols[j][0], cols[j][1], cols[i][1]))
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, parent=P)


def boards(P, name, a0, a1, c, z0, top, axis="x", w=0.15, t=0.022, rnd=None, odd=None, skip=()):
    """Vertical boards side by side from a0 to a1 along x (or y) at c on the other axis, from z0 up to top (a number,
    or a function of the position along the wall for a sloping roof)."""
    n = max(1, round((a1 - a0) / w))
    bw = (a1 - a0) / n
    for k in range(n):
        if k in skip:
            continue
        a = a0 + bw * (k + 0.5)
        zt = top(a) if callable(top) else top
        h = zt - z0 + (rnd.uniform(-0.025, 0.005) if rnd else 0)
        m = (odd or {}).get(k) or wood((k * 3 + (rnd.randrange(4) if rnd else 0)) % 4, wear=0.55)
        size = (bw - 0.01, t, h) if axis == "x" else (t, bw - 0.01, h)
        loc = (a, c, z0 + h / 2) if axis == "x" else (c, a, z0 + h / 2)
        K.box(f"{name}{k}", size, m, loc=loc, bev=0.006, parent=P)


def wire_panel(P, name, w, h, loc, rot=(0, 0, 0), g=0.06, mat=None):
    """Welded wire mesh w wide and h high in its local xz plane, centred on loc."""
    G = _grp(P, name, loc, rot)
    mat = mat or tin(ZINC, wear=0.2, grime=0.25)
    nx, nz = max(1, round(w / g)), max(1, round(h / g))
    for i in range(1, nx):
        K.box(f"{name}_v", (0.004, 0.004, h), mat, loc=(-w / 2 + i * w / nx, 0, 0), bev=0, parent=G)
    for j in range(1, nz):
        K.box(f"{name}_h", (w, 0.004, 0.004), mat, loc=(0, 0.003, -h / 2 + j * h / nz), bev=0, parent=G)
    return G


def tapered_staves(P, name, n, r0, r1, h, t, mats, loc, rnd):
    """A coopered vessel narrowing (or widening) from r0 at the bottom to r1 at the top: n straight staves."""
    x0, y0, z0 = loc
    tilt = math.degrees(math.atan2(r0 - r1, h))
    L = math.hypot(h, r0 - r1)
    rm = (r0 + r1) / 2
    for k in range(n):
        a = 360 * k / n
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        K.box(f"{name}{k}", (2 * math.pi * max(r0, r1) / n + 0.004, t, L + rnd.uniform(-0.01, 0.01)), mats[k % len(mats)],
              loc=(x0 + ca * (rm - t / 2), y0 + sa * (rm - t / 2), z0 + h / 2), rot=(-tilt, 0, a + 90), bev=0.005, parent=P)


def spoked_wheel(P, name, loc, r, n=8, yaw=0.0, felloe=None):
    """A cart wheel on an axle along x: an iron tyre on a wooden felloe, n spokes and a hub."""
    G = _grp(P, name, loc, (0, 0, yaw))
    felloe = felloe or wood(3, wear=0.4)
    K.torus(f"{name}_tyre", r - 0.02, 0.022, _iron(RUST), rot=(0, 90, 0), segs=20, parent=G)
    K.torus(f"{name}_felloe", r - 0.06, 0.03, felloe, rot=(0, 90, 0), segs=16, parent=G)
    K.cyl(f"{name}_hub", 0.07, 0.16, wood(1), rot=(0, 90, 0), segs=10, bev=0.01, parent=G)
    K.cyl(f"{name}_cap", 0.045, 0.2, _iron(), rot=(0, 90, 0), segs=8, bev=0, parent=G)
    for k in range(n):
        a = 360 * k / n
        K.box(f"{name}_spoke", (0.03, 0.04, r - 0.1), wood(2, wear=0.3), loc=(0, math.sin(math.radians(a)) * (r - 0.02) / 2,
              math.cos(math.radians(a)) * (r - 0.02) / 2), rot=(-a, 0, 0), bev=0, parent=G)
    return G


def long_tool(P, name, butt, head_to, kind="fork", L=None):
    """A long-handled tool from its butt towards head_to: fork, rake, shovel or hoe, the head at the far end."""
    G, d = _aim(P, name, butt, head_to)
    L = L or d
    iron = _iron(wear=0.2)
    K.cyl(f"{name}_handle", 0.016, L, wood(2, wear=0.05), loc=(0, 0, L / 2), segs=6, bev=0, parent=G)
    if kind == "fork":
        K.cyl(f"{name}_ferrule", 0.02, 0.12, iron, loc=(0, 0, L + 0.04), segs=6, r2=0.014, bev=0, parent=G)
        K.box(f"{name}_bridge", (0.16, 0.022, 0.025), iron, loc=(0, 0, L + 0.1), bev=0.004, parent=G)
        for k in range(3):
            x = (k - 1) * 0.07
            K.cyl(f"{name}_tine", 0.006, 0.3, iron, loc=(x, 0.008 * abs(k - 1), L + 0.25), rot=(-4 * abs(k - 1), 0, 0), segs=5, r2=0.002, bev=0, parent=G)
    elif kind == "rake":
        K.box(f"{name}_head", (0.55, 0.035, 0.04), wood(1, wear=0.3), loc=(0, 0, L + 0.02), bev=0.006, parent=G)
        for k in range(9):
            K.cyl(f"{name}_tooth", 0.006, 0.09, wood(3, wear=0.1), loc=(-0.24 + k * 0.06, -0.045, L + 0.02), rot=(90, 0, 0), segs=4, bev=0, parent=G)
        for s in (-1, 1):
            rod(G, f"{name}_stay{s}", (0, 0, L - 0.2), (s * 0.16, 0, L), 0.007, wood(3, wear=0.1), segs=4)
    elif kind == "shovel":
        K.cyl(f"{name}_ferrule", 0.022, 0.16, iron, loc=(0, 0, L + 0.05), segs=6, bev=0, parent=G)
        prism(G, f"{name}_blade", [(-0.12, 0), (0.12, 0), (0.12, 0.24), (0, 0.31), (-0.12, 0.24)], 0.006, _iron(ZINC, wear=0.2), loc=(0, 0, L + 0.11),
              rot=(-8, 0, 0), plane="xz", bev=0)
    elif kind == "hoe":
        K.box(f"{name}_neck", (0.02, 0.1, 0.02), iron, loc=(0, -0.05, L), bev=0, parent=G)
        K.box(f"{name}_blade", (0.17, 0.006, 0.11), iron, loc=(0, -0.1, L - 0.04), bev=0.002, parent=G)
    return G


def pumpkin(P, name, r, h, mat, loc, rnd, ribs=8, rot=(0, 0, 0), stem=True):
    """A ribbed pumpkin r round and h tall, its origin on its underside."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=ribs * 2, v_segments=8, radius=1.0)
    ph = rnd.uniform(0, 6.28)
    for vt in bm.verts:
        x, y, z = vt.co
        a = math.atan2(y, x)
        k = (1 - 0.07 * (0.5 + 0.5 * math.cos(ribs * a))) * (1 + 0.04 * math.sin(2 * a + ph))
        zz = (z + 1) / 2 * h
        if abs(z) > 0.99:
            zz += -0.12 * h if z > 0 else 0.06 * h
        vt.co = Vector((x * r * k, y * r * k, zz))
    ob = K.place(K.from_bm(name, bm, mat, smooth=True), loc, rot, P)
    if stem:
        K.cyl(f"{name}_stem", 0.024, 0.09, K.mat("pstem", "#6a6234", rough=0.9, grime=0.3), loc=(0, 0, h * 0.9 + 0.035),
              rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), 0), segs=5, r2=0.016, bev=0, parent=ob)
    return ob


def crate(P, name, loc, yaw=0.0, rot=None, shade=0):
    """A slatted vegetable crate 0.6 x 0.4 x 0.3 m, its origin on its underside: slatted sides, solid ends with hand
    holes."""
    G = _grp(P, name, loc, rot or (0, 0, yaw))
    W, D, H = 0.6, 0.4, 0.3
    slat = wood(shade % 4, wear=0.25)
    for s in (-1, 1):
        for k in range(3):
            K.box(f"{name}_side", (W, 0.014, 0.07), slat, loc=(0, s * (D / 2 - 0.007), 0.045 + k * 0.1), bev=0, parent=G)
        K.box(f"{name}_end", (0.02, D, H), wood((shade + 1) % 4, wear=0.25), loc=(s * (W / 2 - 0.01), 0, H / 2), bev=0.004, parent=G)
        K.box(f"{name}_hole", (0.004, 0.11, 0.035), _dark(), loc=(s * (W / 2 + 0.001), 0, H - 0.06), bev=0, parent=G)
        for t in (-1, 1):
            K.box(f"{name}_corner", (0.03, 0.03, H - 0.01), wood(3), loc=(s * (W / 2 - 0.035), t * (D / 2 - 0.03), H / 2), bev=0, parent=G)
    for k in range(3):
        K.box(f"{name}_floor", (W - 0.04, 0.1, 0.012), slat, loc=(0, -0.13 + k * 0.13, 0.012), bev=0, parent=G)
    return G


# ================================================================== grain silo
def grain_silo_small(seed=41):
    """A 4 m settler grain silo: a corrugated zinc drum in rusty bands on a plank deck, a conical roof with a vent cap,
    a caged ladder up the side, a chute and slide gate at the bottom, a teal patch and the colony's old field code."""
    rnd = random.Random(seed)
    P = _root("silo")
    R = 1.0
    for x in (-1.0, 0.0, 1.0):
        plank(P, "sleeper", (0.18, 2.7, 0.14), (x, 0, 0.07), shade=3)
    for k in range(11):
        y = -1.25 + k * 0.25
        plank(P, f"deck{k}", (2.7 + rnd.uniform(-0.06, 0.04), 0.24, 0.045), (rnd.uniform(-0.03, 0.03), y, 0.1625), shade=k)
    z0, z1 = 0.185, 3.1
    K.cyl("footring", R + 0.04, 0.12, _iron(), loc=(0, 0, z0 + 0.06), segs=32, bev=0.01, parent=P)
    corrugated_tube(P, "wall", R, z0 + 0.1, z1, M(ZINC, metal=0.45, wear=0.2, grime=0.5, moss=0.1))
    for z in (1.15, 2.15):
        K.cyl("band", R + 0.028, 0.06, _rust(), loc=(0, 0, z), segs=32, bev=0, parent=P)
    K.cyl("topring", R + 0.035, 0.08, _iron(), loc=(0, 0, z1 - 0.02), segs=32, bev=0.01, parent=P)
    # a teal sheet riveted over a split low on the front right
    a0, a1 = math.radians(-75), math.radians(-38)
    corrugated_tube(P, "patch", R + 0.012, 0.55, 1.35, M(SEA, metal=0.4, wear=0.2, grime=0.45), a0=a0, a1=a1)
    for a in (a0 + 0.04, a1 - 0.04):
        for z in (0.62, 1.28):
            bolt(P, (math.cos(a) * (R + 0.03), math.sin(a) * (R + 0.03), z), rot=(90, 0, math.degrees(a) + 90))
    # roof: a cone of brick-red sheet with seams, a vent cap and a fill hatch
    roof = M(BRICK, metal=0.3, wear=0.25, grime=0.4)
    rh = 0.72
    K.cyl("roof", R + 0.12, rh, roof, loc=(0, 0, z1 + rh / 2), segs=24, r2=0.14, bev=0.01, parent=P)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        rod(P, f"seam{k}", (math.cos(a) * (R + 0.1), math.sin(a) * (R + 0.1), z1 + 0.02), (math.cos(a) * 0.16, math.sin(a) * 0.16, z1 + rh - 0.02),
            0.012, _iron(RUST), segs=4)
    K.cyl("vent", 0.13, 0.22, _iron(ZINC), loc=(0, 0, z1 + rh + 0.08), segs=12, bev=0.008, parent=P)
    K.cyl("vent_cap", 0.27, 0.13, tin(ZINC, wear=0.2), loc=(0, 0, z1 + rh + 0.24), segs=12, r2=0.04, bev=0.008, parent=P)
    hatch = _grp(P, "hatch", (0.42, -0.42, z1 + 0.3), (math.degrees(math.atan2(rh, R - 0.14)) * 0.95, 0, 45))
    K.box("hatch_lid", (0.36, 0.36, 0.04), tin(ZINC, wear=0.2), bev=0.008, parent=hatch)
    K.box("hatch_handle", (0.16, 0.02, 0.03), _iron(), loc=(0, 0, 0.035), bev=0.004, parent=hatch)
    # ladder up the right side on standoff brackets
    lx = R + 0.22
    LH = z1 + 0.2 - z0
    for s in (-1, 1):
        K.box(f"stile{s}", (0.05, 0.05, LH), _iron(ZINC), loc=(lx, s * 0.2, z0 + LH / 2), bev=0.008, parent=P)
    for i in range(11):
        K.cyl(f"rung{i}", 0.014, 0.4, _iron(), loc=(lx, 0, z0 + 0.25 + i * 0.28), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    for z in (0.7, 1.9, 2.95):
        for s in (-1, 1):
            K.box("standoff", (0.24, 0.03, 0.04), _iron(), loc=(R + 0.1, s * 0.2, z), bev=0, parent=P)
    for z in (z1 - 0.2, z1 + 0.1):
        rod(P, "grab", (lx, -0.2, z), (R + 0.02, -0.3, z + 0.12), 0.014, _iron(ZINC), segs=6)
    # chute and slide gate at the foot of the front, a sack waiting under it
    K.box("chute", (0.34, 0.36, 0.22), tin(ZINC, wear=0.2), loc=(0, -R - 0.12, 0.62), rot=(-32, 0, 0), bev=0.012, parent=P)
    K.box("gate", (0.4, 0.03, 0.26), _iron(RUST), loc=(0.02, -R - 0.27, 0.56), rot=(-32, 0, 0), bev=0.006, parent=P)
    K.box("gate_handle", (0.04, 0.12, 0.04), _iron(), loc=(0.02, -R - 0.35, 0.52), rot=(-32, 0, 0), bev=0.006, parent=P)
    sack(P, "sack", (0.6, 0.38, 0.22), (0.0, -R - 0.15, 0.185), (0, 0, 8), K.mat("sack0", SACK, rough=0.95, grime=0.4, noise_scale=9.0), rnd)
    # the field code on a plate bolted to the front
    K.box("plate", (0.7, 0.03, 0.3), M(BONE, wear=0.5, grime=0.45), loc=(0, -R - 0.045, 2.45), bev=0.008, parent=P)
    stencil(P, "AG-3", (0, -R - 0.062, 2.45), 0.2, M(GRAPHITE, wear=0.3), rot=(90, 0, 0))
    for x in (-0.31, 0.31):
        bolt(P, (x, -R - 0.062, 2.45))
    return P


# ================================================================== hay loft
def hay_loft_frame(seed=42):
    """An open timber frame on stone pads: a plank loft at head height heaped with loose hay and a few bales, a lean-to
    roof of odd corrugated sheets and a ladder up to the loft."""
    rnd = random.Random(seed)
    P = _root("hayloft")
    hx, hy = 1.4, 1.1
    zf, zb = 3.5, 3.1
    timber = [wood(k, wear=0.5) for k in range(4)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = zf if sy < 0 else zb
            K.box("post", (0.16, 0.16, h), timber[3], loc=(sx * hx, sy * hy, h / 2), bev=0.015, parent=P)
            K.box("pad", (0.34, 0.34, 0.16), K.mat("pad", STONE, rough=0.9, grime=0.4, moss=0.4, cracks=0.3), loc=(sx * hx, sy * hy, 0.04),
                  rot=(0, 0, rnd.uniform(-10, 10)), bev=0.03, parent=P)
            # knee braces under the loft bearers
            beam(P, "knee", (sx * hx - sx * 0.06, sy * hy - sy * 0.09, 1.45), (sx * (hx - 0.6), sy * hy - sy * 0.09, 1.98), 0.08, 0.06, timber[2])
    zl = 2.05
    for sy in (-1, 1):
        plank(P, f"bearer{sy}", (2 * hx + 0.3, 0.14, 0.2), (0, sy * (hy - 0.15), zl), shade=3)
    for k in range(6):
        x = -1.25 + k * 0.5
        plank(P, f"joist{k}", (0.08, 2 * hy, 0.14), (x, 0, zl + 0.17), shade=2)
    zt = zl + 0.26
    for k in range(9):
        y = -0.98 + k * 0.245
        L = 2 * hx + 0.25 + rnd.uniform(-0.1, 0.05)
        plank(P, f"floor{k}", (L, 0.235, 0.04), (rnd.uniform(-0.04, 0.04), y, zt), shade=k)
    # top plates and rafters, then the roof falling to the back
    for sy, z in ((-1, zf), (1, zb)):
        plank(P, f"plate{sy}", (2 * hx + 0.3, 0.14, 0.14), (0, sy * hy, z - 0.07), shade=3)
    ang = math.degrees(math.atan2(zf - zb, 2 * hy))
    for x in (-hx, -0.47, 0.47, hx):
        beam(P, "rafter", (x, -hy - 0.3, zf + 0.05 + 0.3 * (zf - zb) / (2 * hy)), (x, hy + 0.3, zb + 0.05 - 0.3 * (zf - zb) / (2 * hy)), 0.08, 0.12, timber[1])
    for k, (x, col) in enumerate(((-1.03, ZINC), (0.0, RUST), (1.02, ZINC))):
        corrugated(P, f"roof{k}", 1.08, 2 * hy + 0.75, tin(col, metal=0.3, wear=0.15, scratches=0.5),
                   loc=(x, 0, (zf + zb) / 2 + 0.15 + k % 2 * 0.012), rot=(-ang, rnd.uniform(-1.5, 1.5), 0), pitch=0.15)
    for x in (-1.3, -0.5, 0.5, 1.3):
        bolt(P, (x, -hy - 0.1, zf + 0.15 + 0.1 * (zf - zb) / (2 * hy) + 0.02), rot=(-ang, 0, 0))
    # the hay: a loose heap with a ragged front edge and three bales
    hay = K.mat("hay", HAY, rough=0.95, grime=0.2, noise_scale=12.0)
    lump(P, "heap", 1.0, (-0.2, 0.15, zt + 0.02), (1.15, 0.95, 0.85), hay, rnd, jitter=0.09, u=14, v=9, flat=0.0)
    # loose hay slumped over the front edge of the loft
    for k, x in enumerate((-0.9, -0.2, 0.35)):
        lump(P, f"slump{k}", 0.3, (x, -hy + 0.12, zt), (1.1, 0.7, 0.45), hay, rnd, jitter=0.15, u=8, v=5, rot=(-20, 0, rnd.uniform(-10, 10)))
    for k, (x, y, z, yaw) in enumerate(((0.85, -0.45, 0, 4), (0.85, 0.15, 0, -3), (0.9, -0.15, 0.5, 80))):
        K.box(f"bale{k}", (0.9, 0.48, 0.48), _straw(), loc=(x, y, zt + 0.26 + z), rot=(0, 0, yaw), bev=0.05, parent=P)
        for t in (-0.22, 0.22):
            c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
            K.box(f"twine{k}", (0.02, 0.5, 0.5), M(SIGNAL, rough=0.7, wear=0.2), loc=(x + t * c, y + t * s_, zt + 0.26 + z), rot=(0, 0, yaw), bev=0, parent=P)
    # loose hay on the ground below, and a ladder up the front left
    K.disc("spill", 1.1, _straw_bed(), (0.2, -0.3), z=0.008, segs=16, wobble=0.3, seed=seed).parent = P
    lad = _grp(P, "ladder", (-0.75, -hy - 0.85, 0), (-17, 0, 0))
    LH = 2.75
    for s in (-1, 1):
        K.box(f"lstile{s}", (0.05, 0.07, LH), timber[0], loc=(s * 0.21, 0, LH / 2), bev=0.008, parent=lad)
    for i in range(8):
        K.cyl(f"lrung{i}", 0.017, 0.44, timber[2], loc=(0, 0, 0.3 + i * 0.3), rot=(0, 90, 0), segs=6, bev=0, parent=lad)
    return P


# ================================================================== feed trough
def feed_trough(seed=43):
    """A long, low V trough of planks on splayed legs, zinc straps over its ends and a scatter of feed inside."""
    rnd = random.Random(seed)
    P = _root("feedtrough")
    L = 2.4
    for s in (-1, 1):
        plank(P, f"side{s}", (L, 0.03, 0.28), (0, s * 0.1475, 0.435), rot=(-s * 34, 0, 0), shade=1 + s)
    plank(P, "bottom", (L, 0.16, 0.03), (0, 0, 0.315), shade=3)
    for s in (-1, 1):
        prism(P, f"end{s}", [(-0.09, 0.3), (0.09, 0.3), (0.26, 0.58), (-0.26, 0.58)], 0.03, wood(2), loc=(s * (L / 2 - 0.03), 0, 0), plane="yz")
        K.box(f"strap{s}", (0.05, 0.56, 0.006), tin(ZINC, wear=0.25), loc=(s * (L / 2 - 0.03), 0, 0.583), bev=0.002, parent=P)
        for t in (-1, 1):
            bolt(P, (s * (L / 2 - 0.03), t * 0.24, 0.59), rot=(0, 0, 0))
    for x in (-0.85, 0.85):
        for t in (-1, 1):
            beam(P, "leg", (x, t * 0.33, 0.0), (x, -t * 0.02, 0.5), 0.06, 0.06, wood(3))
        plank(P, f"cradle{x}", (0.07, 0.5, 0.06), (x, 0, 0.27), shade=3)
    plank(P, "stretcher", (1.8, 0.06, 0.06), (0, 0, 0.13), shade=2)
    # feed: a thin bed along the bottom and two heaps
    feed = K.mat("grain", GRAIN, rough=0.95, grime=0.15, noise_scale=30.0)
    K.box("feed", (1.9, 0.2, 0.05), feed, loc=(0.1, 0, 0.36), bev=0.01, parent=P)
    for k, x in enumerate((-0.45, 0.55)):
        lump(P, f"heap{k}", 0.16, (x, 0, 0.37), (1.8, 0.85, 0.5), feed, rnd, jitter=0.1, flat=0.0, u=8, v=5)
    # a tin patch nailed over a split in the front board
    K.box("patch", (0.3, 0.008, 0.16), tin(RUST, wear=0.25), loc=(-0.5, -0.172, 0.45), rot=(34, 0, 3), bev=0.002, parent=P)
    return P


# ================================================================== pig pen
def pig_pen(seed=44):
    """A small pen of plank rails on posts round a wallow of mud: a lean-to of boards and a sheet roof in the back
    corner bedded with straw, a half-drum trough and a braced gate, one rail broken and hanging."""
    rnd = random.Random(seed)
    P = _root("pigpen")
    X, Y = 1.5, 1.3
    posts = [(-X, -Y), (-0.25, -Y), (0.45, -Y), (X, -Y), (-X, 0), (X, 0), (-X, Y), (0, Y), (X, Y)]
    for k, (x, y) in enumerate(posts):
        post(P, f"post{k}", (x, y), 1.1, r=0.05, tilt=(rnd.uniform(-2, 2), rnd.uniform(-2, 2)))
    rails = (0.25, 0.55, 0.85)
    odd = {(1, 1): M(SEA, wear=0.6, grime=0.45, rough=0.85), (2, 2): M(BRICK, wear=0.6, grime=0.45, rough=0.85)}
    for j, z in enumerate(rails):
        plank(P, f"front{j}", (X + 0.45 + 0.08, 0.025, 0.14), ((-X + 0.45) / 2, -Y - 0.065, z), shade=j)
        m = odd.get((0, j))
        K.box(f"back{j}", (2 * X + 0.08, 0.025, 0.14), m or wood((j + 1) % 4), loc=(0, Y + 0.065, z), rot=(0, rnd.uniform(-0.8, 0.8), 0), bev=0.012, parent=P)
        K.box(f"left{j}", (0.025, 2 * Y + 0.08, 0.14), odd.get((1, j)) or wood((j + 2) % 4), loc=(-X - 0.065, 0, z), bev=0.012, parent=P)
        if j == 1:
            # the right middle rail is broken: one half still nailed, the other hanging from its nail
            plank(P, "right_half", (0.025, 1.4, 0.14), (X + 0.065, -0.6, z), shade=3)
            K.box("right_hang", (0.025, 1.2, 0.14), wood(0), loc=(X + 0.07, 0.62, 0.3), rot=(-24, 0, 0), bev=0.012, parent=P)
        else:
            K.box(f"right{j}", (0.025, 2 * Y + 0.08, 0.14), odd.get((2, j)) or wood(j % 4), loc=(X + 0.065, 0, z), bev=0.012, parent=P)
    for x in (-X, X):
        for y in (-Y, Y):
            bolt(P, (x + (0.08 if x > 0 else -0.08), y, 0.85), rot=(0, 90, 0))
    # the gate, hung on the right corner post, closed with an iron hook
    G = _grp(P, "gate", (X - 0.06, -Y - 0.07, 0))
    gw = 0.95
    for z in rails:
        plank(G, "gate_rail", (gw, 0.025, 0.12), (-gw / 2, 0, z), shade=1)
    for x in (-0.03, -gw + 0.03):
        plank(G, "gate_stile", (0.06, 0.03, 0.82), (x, -0.02, 0.55), shade=3)
    beam(G, "gate_brace", (-0.06, -0.025, 0.25), (-gw + 0.06, -0.025, 0.85), 0.07, 0.02, wood(2), bev=0.006)
    for z in (0.3, 0.8):
        K.box("gate_hinge", (0.18, 0.006, 0.03), _iron(), loc=(-0.1, -0.04, z), bev=0, parent=G)
    K.torus("hook", 0.05, 0.006, _iron(), loc=(-gw - 0.02, -0.03, 0.62), rot=(90, 0, 0), arc=200, segs=10, parent=G)
    # the lean-to in the back left corner
    lx0, lx1, ly0, ly1 = -X + 0.05, -0.12, 0.25, Y - 0.06
    zf, zb = 1.3, 1.02
    for x in (lx0, lx1):
        K.box("lt_post", (0.08, 0.08, zf), wood(3), loc=(x, ly0, zf / 2), bev=0.01, parent=P)
    K.box("lt_post_b", (0.08, 0.08, zb), wood(3), loc=(lx1, ly1, zb / 2), bev=0.01, parent=P)

    def roof_z(y):
        return zf + (zb - zf) * (y - ly0) / (ly1 - ly0)

    boards(P, "lt_back", lx0, lx1 + 0.04, ly1 - 0.03, 0.0, zb - 0.02, axis="x", rnd=rnd)
    boards(P, "lt_side", ly0, ly1, lx0 - 0.01, 0.0, roof_z, axis="y", rnd=rnd, odd={2: M(SEA, wear=0.6, grime=0.45, rough=0.85)})
    plank(P, "lt_plate", (lx1 - lx0 + 0.1, 0.08, 0.1), ((lx0 + lx1) / 2, ly0, zf - 0.04), shade=3)
    ang = math.degrees(math.atan2(zf - zb, ly1 - ly0))
    corrugated(P, "lt_roof", lx1 - lx0 + 0.3, ly1 - ly0 + 0.45, tin(ZINC, metal=0.3, wear=0.15), loc=((lx0 + lx1) / 2, (ly0 + ly1) / 2 - 0.05, (zf + zb) / 2 + 0.06),
               rot=(-ang, 0, 0), pitch=0.15)
    K.box("lt_sheet", (0.5, 0.6, 0.008), tin(BRICK, metal=0.3, wear=0.25), loc=(-0.55, 0.55, (zf + zb) / 2 + 0.12), rot=(-ang, 0, 6), bev=0.002, parent=P)
    K.disc("bedding", 0.6, _straw_bed(), ((lx0 + lx1) / 2, (ly0 + ly1) / 2), z=0.012, segs=14, wobble=0.25, seed=seed).parent = P
    lump(P, "bed_heap", 0.4, (-0.95, 0.85, 0.0), (1.3, 0.9, 0.35), _straw(), rnd, jitter=0.12, flat=0.0, u=10, v=6)
    # mud: a wet wallow inside a wider patch of trodden earth
    K.disc("trodden", 1.35, K.mat("trodden", MUD, rough=0.95, noise_scale=4.0), (0.3, -0.35), z=0.006, segs=20, wobble=0.18, seed=seed + 1).parent = P
    K.disc("wallow", 0.75, K.mat("wallow", MUD_D, rough=0.25, noise_scale=5.0), (0.45, -0.4), z=0.012, segs=18, wobble=0.3, seed=seed + 2).parent = P
    for k in range(5):
        lump(P, f"clod{k}", 0.1, (0.45 + rnd.uniform(-0.8, 0.8), -0.4 + rnd.uniform(-0.6, 0.6), 0.0), (1.4, 1.0, 0.35), K.mat("trodden", MUD, rough=0.95, noise_scale=4.0),
             rnd, jitter=0.2, flat=0.0, u=6, v=4)
    # a half drum trough on two bricks
    tx, ty, R = 0.95, 0.75, 0.28
    trough = shell_panel("trough", R, math.pi, 2 * math.pi, -0.42, 0.42, M(STEEL, metal=0.4, wear=0.3, grime=0.6), t=0.01, parent=P, loc=(tx, ty, R + 0.08))
    trough.rotation_euler = (0, 0, math.radians(90))
    for s in (-1, 1):
        K.box("brick", (0.22, 0.11, 0.08), M(BRICK, rough=0.9, wear=0.4, cracks=0.3), loc=(tx + s * 0.3, ty, 0.04), bev=0.008, parent=P)
        prism(P, f"trough_end{s}", [(R * math.cos(a), R * math.sin(a)) for a in (math.pi + math.pi * i / 8 for i in range(9))], 0.01,
              M(STEEL, metal=0.4, wear=0.3, grime=0.6), loc=(tx + s * 0.42, ty, R + 0.08), rot=(0, 0, 90), plane="xz", bev=0)
    K.box("slop", (0.8, 0.42, 0.01), K.mat("slop", "#6b5a3a", rough=0.3), loc=(tx, ty, 0.2), bev=0, parent=P)
    return P


# ================================================================== goat shed
def goat_shed(seed=45):
    """A tiny shed of vertical boards on skids under a single slant of corrugated sheet, a stable door with its top
    leaf swung open, a hay rack on the side and a zinc patch over rotten boards."""
    rnd = random.Random(seed)
    P = _root("goatshed")
    hw, hd = 0.9, 0.7
    zf, zb = 1.85, 1.45

    def roof(y):
        return zf + (zb - zf) * (y + hd) / (2 * hd)

    for y in (-0.55, 0.55):
        plank(P, "skid", (2.0, 0.12, 0.1), (0, y, 0.05), shade=3)
    plank(P, "floor", (2 * hw, 2 * hd, 0.04), (0, 0, 0.12), shade=1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = roof(sy * (hd - 0.04)) - 0.14
            K.box("corner", (0.08, 0.08, h), wood(3), loc=(sx * (hw - 0.04), sy * (hd - 0.04), 0.14 + h / 2), bev=0.01, parent=P)
    for sy in (-1, 1):
        plank(P, f"plate{sy}", (2 * hw, 0.08, 0.08), (0, sy * (hd - 0.04), roof(sy * (hd - 0.04)) - 0.04), shade=3)
    t = 0.022
    o = hd + t / 2
    d0, d1, dz = -0.55, 0.15, 1.45  # door opening
    boards(P, "front_l", -hw - 0.01, d0, -o, 0.1, zf - 0.02, rnd=rnd, w=0.2)
    boards(P, "front_r", d1, hw + 0.01, -o, 0.1, zf - 0.02, rnd=rnd, w=0.2, odd={1: M(SEA, wear=0.6, grime=0.45, rough=0.85)})
    plank(P, "header", (d1 - d0 + 0.06, t, zf - dz - 0.02), ((d0 + d1) / 2, -o, (zf + dz) / 2 - 0.01), shade=2)
    for x in (d0, d1):
        plank(P, "jamb", (0.05, 0.03, dz - 0.12), (x + (0.025 if x == d0 else -0.025), -o - 0.025, (dz + 0.12) / 2), shade=3)
    boards(P, "back", -hw - 0.01, hw + 0.01, o, 0.1, zb - 0.02, rnd=rnd, w=0.2, odd={3: M(BRICK, wear=0.6, grime=0.45, rough=0.85)})
    for sx in (-1, 1):
        boards(P, f"side{sx}", -hd - 0.02, hd + 0.02, sx * (hw + t / 2), 0.1, lambda y: roof(y) - 0.02, axis="y", rnd=rnd, w=0.2)
    # the dark inside behind the door
    K.box("inside", (d1 - d0, 0.02, dz - 0.14), _dark(), loc=((d0 + d1) / 2, -hd + 0.12, (dz + 0.14) / 2), bev=0, parent=P)
    K.box("hay_in", (d1 - d0, 0.3, 0.25), _straw(True), loc=((d0 + d1) / 2, -hd + 0.3, 0.26), bev=0.04, parent=P)
    # stable door: the bottom leaf shut, the top leaf swung back against the wall
    dw = d1 - d0 - 0.02

    def leaf(G, h):
        for k in range(4):
            plank(G, "leaf_board", (dw / 4 - 0.006, t, h), (dw / 8 + k * dw / 4, 0, h / 2), shade=(k + 2) % 4)
        for z in (0.1, h - 0.1):
            plank(G, "leaf_ledge", (dw - 0.04, t, 0.08), (dw / 2, -t, z), shade=3)
        beam(G, "leaf_brace", (0.08, -t, 0.12), (dw - 0.08, -t, h - 0.12), 0.07, t, wood(3), bev=0.006)
        for z in (0.12, h - 0.12):
            K.box("leaf_hinge", (0.2, 0.006, 0.03), _iron(), loc=(0.1, -t * 1.6, z), bev=0, parent=G)

    lower = _grp(P, "door_low", (d0 + 0.01, -o - 0.03, 0.14))
    leaf(lower, 0.72)
    K.box("bolt_bar", (0.12, 0.02, 0.025), _iron(), loc=(dw - 0.04, -0.05, 0.55), bev=0.004, parent=lower)
    upper = _grp(P, "door_up", (d0 + 0.01, -o - 0.03, 0.88), (0, 0, -165))
    leaf(upper, dz - 0.9)
    # a zinc sheet over rotten boards on the left wall
    corrugated(P, "patch", 0.6, 0.62, tin(ZINC, wear=0.15), loc=(-hw - 0.035, 0.15, 0.55), basis=((0, -1, 0), (0, 0, 1), (-1, 0, 0)))
    for y in (-0.1, 0.4):
        for z in (0.3, 0.8):
            bolt(P, (-hw - 0.05, y, z), rot=(0, 90, 0))
    # roof: two sheets that do not match, overhanging the door
    ang = math.degrees(math.atan2(zf - zb, 2 * hd))
    for k, (x, col) in enumerate(((-0.47, RUST), (0.47, ZINC))):
        corrugated(P, f"roof{k}", 1.0, 2 * hd + 0.5, tin(col, metal=0.3, wear=0.15, scratches=0.5), loc=(x, -0.06, roof(-0.06) + 0.05 + k * 0.012), rot=(-ang, 0, 0),
                   pitch=0.15)
    # a hay rack on the right wall
    rx = hw + 0.2
    for y in (-0.38, 0.38):
        beam(P, "rack_arm", (hw + 0.02, y, 1.15), (rx + 0.08, y, 1.2), 0.04, 0.05, wood(3), bev=0)
        beam(P, "rack_stay", (hw + 0.02, y, 0.75), (rx, y, 1.15), 0.035, 0.035, wood(3), bev=0)
    for k in range(7):
        y = -0.36 + k * 0.12
        beam(P, "rack_slat", (hw + 0.04, y, 0.8), (rx + 0.08, y, 1.2), 0.025, 0.02, wood(2), bev=0)
    K.box("rack_rail", (0.04, 0.84, 0.05), wood(3), loc=(rx + 0.08, 0, 1.2), bev=0.006, parent=P)
    lump(P, "rack_hay", 0.3, (rx - 0.02, 0, 1.17), (0.5, 1.3, 0.75), K.mat("hay", HAY, rough=0.95, grime=0.2, noise_scale=12.0), rnd, jitter=0.2, u=10, v=6)
    K.cyl("bucket", 0.13, 0.26, tin(ZINC, wear=0.25), loc=(0.45, -hd - 0.35, 0.13), segs=12, r2=0.15, bev=0.006, parent=P)
    return P


# ================================================================== milk churns
def _churn(P, name, loc, rot=(0, 0, 0), band=None, lid=True):
    G = _grp(P, name, loc, rot)
    steel = M(ZINC, metal=0.6, wear=0.2, grime=0.4, scratches=0.4)
    prof = [(0, 0), (0.165, 0), (0.175, 0.012), (0.18, 0.04), (0.172, 0.06), (0.172, 0.4), (0.162, 0.45), (0.115, 0.53), (0.1, 0.58), (0.1, 0.6), (0, 0.6)]
    turned(G, f"{name}_body", prof, steel, segs=14)
    if band:
        K.cyl(f"{name}_band", 0.175, 0.07, M(band, metal=0.3, wear=0.25, grime=0.4), loc=(0, 0, 0.3), segs=14, bev=0, parent=G)
    if lid:
        turned(G, f"{name}_lid", [(0, 0.585), (0.122, 0.585), (0.122, 0.625), (0.1, 0.65), (0.04, 0.665), (0, 0.665)], steel, segs=14)
        K.torus(f"{name}_knob", 0.03, 0.007, _iron(ZINC), loc=(0, 0, 0.67), rot=(90, 0, 0), arc=180, segs=8, parent=G)
    for s in (-1, 1):
        K.torus(f"{name}_handle", 0.05, 0.01, _iron(ZINC), loc=(s * 0.15, 0, 0.47), rot=(90, 0, 90 + s * 90), arc=180, segs=10, parent=G)
    return G


def milk_churns(seed=46):
    """A roadside churn stand of planks on four posts at cart height, three steel churns on it and a fourth on the
    ground beside it, one lid off and leant against its churn."""
    rnd = random.Random(seed)
    P = _root("churns")
    W, D, H = 1.5, 0.65, 0.8
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("leg", (0.1, 0.1, H), wood(3), loc=(sx * (W / 2 - 0.08), sy * (D / 2 - 0.08), H / 2 - 0.05), bev=0.012, parent=P)
        beam(P, "brace", (sx * (W / 2 - 0.08), -D / 2 + 0.12, 0.1), (sx * (W / 2 - 0.08), D / 2 - 0.12, H - 0.15), 0.04, 0.08, wood(2))
    for sy in (-1, 1):
        plank(P, f"bearer{sy}", (W, 0.08, 0.12), (0, sy * (D / 2 - 0.08), H - 0.11), shade=3)
    for k in range(5):
        plank(P, f"deck{k}", (0.29, D + 0.05, 0.04), (-W / 2 + 0.15 + k * 0.3, 0, H - 0.03), rot=(0, 0, rnd.uniform(-1.5, 1.5)), shade=k)
    plank(P, "kick", (W, 0.025, 0.1), (0, -D / 2 - 0.02, H + 0.0), shade=2)
    zt = H - 0.01
    _churn(P, "churn0", (-0.47, 0.03, zt), (0, 0, 20), band=SEA)
    _churn(P, "churn1", (-0.06, -0.02, zt), (0, 0, 70))
    _churn(P, "churn2", (0.4, 0.04, zt), (0, 0, -15), band=BRICK, lid=False)
    turned(P, "loose_lid", [(0, 0.0), (0.122, 0.0), (0.122, 0.04), (0.1, 0.065), (0.04, 0.08), (0, 0.08)], M(ZINC, metal=0.6, wear=0.2, grime=0.4),
           segs=14, loc=(0.16, -0.22, zt), rot=(0, 4, 0))
    _churn(P, "churn3", (1.05, -0.2, 0.0), (0, 0, 130))
    stencil(P, "7", (0.4, 0.04 - 0.177, zt + 0.3), 0.09, M(BONE, wear=0.2), rot=(90, 0, 0))
    return P


# ================================================================== butter churn
def butter_churn(seed=47):
    """A tall stave churn in iron hoops on a low plank stool, the dasher's handle up through its lid, a wooden pail on
    the ground beside it."""
    rnd = random.Random(seed)
    P = _root("butterchurn")
    sh = 0.28
    plank(P, "seat", (0.5, 0.45, 0.04), (0, 0, sh - 0.02), shade=1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(P, "leg", (sx * 0.24, sy * 0.21, 0.0), (sx * 0.17, sy * 0.15, sh - 0.04), 0.04, 0.04, wood(3))
        plank(P, f"rail{sx}", (0.04, 0.34, 0.04), (sx * 0.205, 0, 0.11), shade=3)
    z0, H = sh, 0.78
    tapered_staves(P, "stave", 14, 0.19, 0.145, H, 0.022, [wood(k, wear=0.4) for k in range(4)], (0, 0, z0), rnd)
    K.cyl("floor", 0.17, 0.03, wood(2), loc=(0, 0, z0 + 0.04), segs=14, bev=0, parent=P)
    for f in (0.1, 0.5, 0.9):
        r = 0.19 + (0.145 - 0.19) * f + 0.004
        K.torus("hoop", r, 0.007, _iron(RUST), loc=(0, 0, z0 + H * f), segs=24, parent=P)
    K.cyl("lid", 0.15, 0.03, wood(1), loc=(0, 0, z0 + H + 0.01), segs=14, bev=0.006, parent=P)
    K.cyl("cup", 0.05, 0.06, wood(2), loc=(0, 0, z0 + H + 0.05), segs=10, r2=0.04, bev=0.005, parent=P)
    rod(P, "dasher", (0, 0, z0 + H - 0.1), (0.02, 0.01, z0 + H + 0.45), 0.016, wood(0, wear=0.05), segs=6)
    # a pail of buttermilk by the stool
    px, py = 0.42, -0.22
    tapered_staves(P, "pail", 10, 0.12, 0.14, 0.24, 0.016, [wood(k, wear=0.4) for k in (1, 2)], (px, py, 0), rnd)
    K.cyl("milk", 0.125, 0.01, K.mat("milk", "#e9e3d0", rough=0.3), loc=(px, py, 0.2), segs=10, bev=0, parent=P)
    for f in (0.2, 0.8):
        K.torus("pail_hoop", 0.12 + 0.02 * f + 0.004, 0.005, _iron(RUST), loc=(px, py, 0.24 * f), segs=16, parent=P)
    K.torus("pail_bail", 0.14, 0.005, _iron(), loc=(px, py, 0.22), rot=(70, 0, 30), arc=180, segs=12, parent=P)
    return P


# ================================================================== seed sacks cart
def seed_sacks_cart(seed=48):
    """A two-wheeled hand cart with a plank bed and low sides, its shafts resting on a prop leg, loaded with tied seed
    sacks."""
    rnd = random.Random(seed)
    P = _root("seedcart")
    bz, W, L = 0.6, 0.9, 1.3
    for x in (-0.3, 0.3):
        plank(P, "runner", (0.08, L + 0.1, 0.08), (x, 0.05, bz - 0.06), shade=3)
    for k in range(6):
        plank(P, f"bed{k}", (W, 0.21, 0.035), (0, -L / 2 + 0.11 + k * 0.22, bz), shade=k)
    for s in (-1, 1):
        plank(P, f"side{s}", (0.03, L, 0.18), (s * (W / 2 - 0.015), 0.0, bz + 0.1), shade=s + 1)
        K.box(f"stake{s}", (0.05, 0.05, 0.3), wood(3), loc=(s * (W / 2 + 0.01), -0.4, bz + 0.08), bev=0.008, parent=P)
    plank(P, "tailboard", (W, 0.03, 0.18), (0, L / 2 + 0.0, bz + 0.1), shade=2)
    # shafts out to the front, a cross bar to push by, a prop leg holding the bed level
    for s in (-1, 1):
        beam(P, f"shaft{s}", (s * 0.3, 0.3, bz - 0.06), (s * 0.27, -1.45, bz - 0.02), 0.06, 0.06, wood(3), bev=0.008)
    rod(P, "crossbar", (-0.32, -1.38, bz - 0.02), (0.32, -1.38, bz - 0.02), 0.022, wood(2, wear=0.1), segs=8)
    for s in (-1, 1):
        beam(P, f"prop{s}", (s * 0.29, -0.62, bz - 0.08), (s * 0.31, -0.7, 0.0), 0.05, 0.05, wood(3))
    beam(P, "prop_x", (-0.3, -0.66, 0.15), (0.3, -0.66, 0.15), 0.04, 0.04, wood(2))
    K.cyl("axle", 0.03, 1.2, _iron(), loc=(0, 0.12, 0.42), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    for s in (-1, 1):
        K.box(f"axle_block{s}", (0.1, 0.12, 0.14), wood(3), loc=(s * 0.3, 0.12, 0.5), bev=0.01, parent=P)
        spoked_wheel(P, f"wheel{s}", (s * 0.56, 0.12, 0.44), 0.44)
    mats = [K.mat(f"sack{k}", c, rough=0.95, grime=0.4, noise_scale=9.0) for k, c in enumerate((SACK, SACK_D, SACK_L))]
    top = bz + 0.018
    for k, (x, y, yaw) in enumerate(((-0.2, -0.3, 2), (0.2, -0.3, -3), (-0.2, 0.3, -2), (0.2, 0.32, 4))):
        sack(P, f"sack{k}", (0.62, 0.38, 0.22), (x, y, top), (0, 0, 90 + yaw), mats[k % 3], rnd)
    s = sack(P, "sack_top", (0.6, 0.38, 0.22), (0.0, 0.0, top + 0.17), (rnd.uniform(-4, 4), 0, 6), mats[2], rnd)
    K.box("patch", (0.13, 0.12, 0.01), M(SEA, wear=0.2, rough=0.95), loc=(-0.06, 0.02, 0.2), rot=(0, 0, 12), bev=0.002, parent=s)
    return P


# ================================================================== plough
def plough_old(seed=49):
    """A horse-drawn walking plough left in the field: an iron beam with a clevis and a little gauge wheel at the front,
    coulter, share and a twisted mouldboard under it, two long wooden handles behind."""
    rnd = random.Random(seed)
    P = _root("plough")
    iron = M(RUST, metal=0.45, wear=0.25, grime=0.5, rough=0.75)
    dark = _iron(wear=0.2)
    # the beam: front piece and the rise up to the clevis
    beam(P, "beam", (0, 0.35, 0.42), (0, -0.85, 0.5), 0.07, 0.09, iron, bev=0.008)
    beam(P, "beam_nose", (0, -0.85, 0.5), (0, -1.15, 0.44), 0.06, 0.08, iron, bev=0.008)
    K.torus("clevis", 0.06, 0.012, dark, loc=(0, -1.2, 0.44), rot=(0, 90, 0), segs=12, parent=P)
    # the standard down to the frog
    beam(P, "standard", (0, 0.2, 0.44), (0.0, 0.08, 0.08), 0.06, 0.1, iron, bev=0.008)
    # share: a pointed blade at the front of the body, running just in the soil
    prism(P, "share", [(-0.02, -0.38), (0.02, -0.38), (0.22, -0.02), (0.24, 0.05), (-0.02, 0.05)], 0.015, dark, loc=(0, 0, 0.03), rot=(-90, 0, 0), bev=0)
    prism(P, "share_top", [(-0.02, -0.38), (0.02, -0.38), (0.22, -0.02), (0.24, 0.05), (-0.02, 0.05)], 0.012, iron, loc=(0, 0, 0.045), rot=(-90, 0, 0),
          bev=0)
    # mouldboard: a twisted sheet rising off the share and curling to the right
    bm = bmesh.new()
    nu, nv = 6, 4
    vs = []
    for i in range(nu + 1):
        u = i / nu
        row = []
        for j in range(nv + 1):
            v = j / nv
            y = -0.05 + 0.55 * u + 0.05 * v
            x = 0.02 + (0.12 + 0.3 * v) * u ** 1.4 + 0.03 * v
            z = 0.05 + 0.32 * v * (0.75 + 0.25 * u)
            row.append(bm.verts.new((x, y, z)))
        vs.append(row)
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    mb = K.from_bm("mouldboard", bm, M(GRAPHITE, metal=0.6, wear=0.25, grime=0.3, scratches=0.6), smooth=True)
    sol = mb.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = 0.012
    K.place(mb, parent=P)
    K.box("landside", (0.015, 0.62, 0.11), iron, loc=(-0.01, 0.2, 0.065), bev=0.003, parent=P)
    K.box("frog", (0.07, 0.3, 0.14), iron, loc=(0.02, 0.12, 0.1), bev=0.01, parent=P)
    # coulter: a knife down through the beam ahead of the share
    prism(P, "coulter", [(-0.52, 0.56), (-0.46, 0.56), (-0.34, 0.08), (-0.4, 0.06)], 0.012, dark, loc=(0.0, 0, 0), plane="yz", bev=0)
    K.box("coulter_clamp", (0.1, 0.08, 0.1), iron, loc=(0, -0.48, 0.48), bev=0.008, parent=P)
    # gauge wheel on its stem
    rod(P, "wheel_stem", (0.0, -1.0, 0.48), (0.0, -1.0, 0.15), 0.018, dark)
    K.box("wheel_fork", (0.12, 0.03, 0.03), dark, loc=(0, -1.0, 0.15), bev=0, parent=P)
    K.torus("wheel", 0.13, 0.02, iron, loc=(0, -1.0, 0.15), rot=(0, 90, 0), segs=16, parent=P)
    for k in range(4):
        K.box("wheel_spoke", (0.012, 0.012, 0.24), iron, loc=(0, -1.0, 0.15), rot=(45 * k, 0, 0), bev=0, parent=P)
    # handles: two long wooden stilts back to the grips, a rung and an iron stay between them
    for s in (-1, 1):
        rod(P, f"handle{s}", (s * 0.03, 0.1, 0.12), (s * 0.3, 1.2, 0.92), 0.022, wood(s + 1, wear=0.2), segs=8)
        rod(P, f"grip{s}", (s * 0.3, 1.2, 0.92), (s * 0.32, 1.32, 0.9), 0.026, wood(3, wear=0.1), segs=8)
        rod(P, f"stay{s}", (0, 0.32, 0.43), (s * 0.15, 0.7, 0.56), 0.012, dark)
    rod(P, "rung", (-0.2, 0.85, 0.66), (0.2, 0.85, 0.66), 0.018, wood(2, wear=0.1), segs=6)
    rod(P, "rung2", (-0.12, 0.5, 0.39), (0.12, 0.5, 0.39), 0.014, dark, segs=6)
    for s in (-1, 1):
        bolt(P, (s * 0.04, 0.32, 0.43), rot=(0, 90, 0))
    lump(P, "clod", 0.14, (0.25, 0.3, 0.0), (1.6, 1.0, 0.45), _soil(), rnd, jitter=0.2, flat=0.0, u=8, v=5)
    return P


# ================================================================== harrow
def harrow_rusty(seed=50):
    """A spike harrow lying flat in the grass, its iron frame gone to rust: bars in a grid, rows of tines into the
    ground, one corner propped on a stone so the spikes show, a chain from the drawbar."""
    rnd = random.Random(seed)
    P = _root("harrow")
    iron = M(RUST, metal=0.45, wear=0.25, grime=0.55, rough=0.8)
    G = _grp(P, "frame", (0, 0, 0.12), (6, -4, 0))
    W, D = 1.6, 1.4
    xs = [-W / 2 + W * k / 4 for k in range(5)]
    for k, x in enumerate(xs):
        K.box(f"bar{k}", (0.05, D, 0.05), iron, loc=(x, 0, 0), bev=0.006, parent=G)
        for j in range(5):
            y = -D / 2 + 0.1 + j * (D - 0.2) / 4 + (0.08 if k % 2 else 0)
            if (k, j) == (3, 2):
                continue  # a tine snapped off
            K.cyl("tine", 0.018, 0.2, iron, loc=(x, y, -0.1), segs=4, r2=0.004, rot=(rnd.uniform(-6, 6), rnd.uniform(-6, 6), 45), bev=0, parent=G)
    for y in (-D / 2 + 0.03, 0.0, D / 2 - 0.03):
        K.box("cross", (W + 0.1, 0.05, 0.04), iron, loc=(0, y, 0.045), bev=0.006, parent=G)
    for x in xs:
        for y in (-D / 2 + 0.03, D / 2 - 0.03):
            K.cyl("rivet", 0.02, 0.02, _iron(), loc=(x, y, 0.07), segs=6, bev=0, parent=G)
    beam(G, "diag", (-W / 2, -D / 2, 0.08), (W / 2, D / 2, 0.08), 0.04, 0.03, iron, bev=0.004)
    # drawbar and chain off the front
    K.box("drawbar", (0.8, 0.06, 0.05), iron, loc=(0, -D / 2 - 0.12, 0.03), bev=0.006, parent=G)
    for s in (-1, 1):
        beam(G, "drawstay", (s * 0.35, -D / 2 - 0.12, 0.03), (s * 0.6, -D / 2 + 0.02, 0.03), 0.035, 0.035, iron, bev=0)
    chain = _iron(wear=0.2)
    for k in range(6):
        K.torus("link", 0.035, 0.008, chain, loc=(0.05 + k * 0.055 * 0.7, -D / 2 - 0.2 - k * 0.055, -0.1 + 0.004 * k), rot=(90 * (k % 2), 0, 35), segs=10,
                parent=G)
    # the stone the back corner rests on
    lump(P, "stone", 0.17, (0.62, 0.6, 0.0), (1.2, 1.0, 0.9), K.mat("fieldstone", "#80827a", rough=0.9, grime=0.3, moss=0.4, cracks=0.3, noise_scale=4.0),
         rnd, jitter=0.14, flat=0.3, u=8, v=6)
    for k in range(6):
        x, y = rnd.uniform(-0.8, 0.8), rnd.uniform(-0.7, 0.7)
        K.cyl(f"weed{k}", 0.05, rnd.uniform(0.15, 0.3), K.mat("weed", LEAF, rough=0.9, grime=0.2), loc=(x, y, 0.08), segs=4, r2=0.005, bev=0, parent=P)
    return P


# ================================================================== scythe rack
def _scythe(P, name, loc, flip=1):
    """A scythe hung by its snath: the shaft upright, two hand grips, the blade curving off sideways at the top."""
    G = _grp(P, name, loc)
    snath = wood(0, wear=0.05)
    rod(G, f"{name}_snath", (0, 0, 0), (0.05 * flip, 0, 0.9), 0.018, snath, segs=6)
    rod(G, f"{name}_snath2", (0.05 * flip, 0, 0.9), (0.02 * flip, 0, 1.45), 0.018, snath, segs=6)
    for z, L in ((0.55, 0.14), (1.05, 0.12)):
        rod(G, f"{name}_grip", (0.03 * flip, 0, z), (0.03 * flip, -L, z + 0.03), 0.014, wood(3, wear=0.05), segs=6)
    # the blade: a curved strip in the xz plane, sharp edge down
    pts = []
    for i in range(9):
        f = i / 8
        x = flip * (0.03 + 0.68 * f)
        z = 1.42 - 0.09 * f - 0.12 * f * f
        w = 0.09 * (1 - f) + 0.012
        pts.append((x, z, w))
    top = [(x, z) for x, z, w in pts]
    bot = [(x, z - w) for x, z, w in reversed(pts)]
    prism(G, f"{name}_blade", top + bot, 0.004, _iron(GRAPHITE, wear=0.3, scratches=0.6), loc=(0, 0, 0), plane="xz", bev=0)
    K.box(f"{name}_ring", (0.05, 0.05, 0.06), _iron(), loc=(0.025 * flip, 0, 1.4), bev=0.004, parent=G)
    return G


def scythe_rack(seed=51):
    """A free-standing tool rack of two posts on cross feet, a rail with pegs under a narrow board cap: two scythes and
    two hay rakes hung on it, a fork and a hoe leaning against the end."""
    rnd = random.Random(seed)
    P = _root("toolrack")
    hx, H = 0.95, 1.95
    for s in (-1, 1):
        K.box(f"post{s}", (0.1, 0.1, H), wood(3), loc=(s * hx, 0, H / 2), bev=0.012, parent=P)
        plank(P, f"foot{s}", (0.1, 0.8, 0.08), (s * hx, 0, 0.04), shade=2)
        for t in (-1, 1):
            beam(P, f"strut{s}{t}", (s * hx, t * 0.32, 0.08), (s * hx, t * 0.05, 0.5), 0.06, 0.04, wood(2))
    plank(P, "rail", (2 * hx + 0.2, 0.08, 0.12), (0, -0.09, 1.72), shade=1)
    plank(P, "rail_low", (2 * hx, 0.06, 0.08), (0, 0.0, 0.45), shade=2)
    for s in (-1, 1):
        plank(P, f"cap{s}", (2 * hx + 0.35, 0.16, 0.025), (0, s * 0.07, H + 0.04), rot=(s * 18, 0, 0), shade=s + 1)
    pegs = (-0.78, -0.55, -0.12, 0.12, 0.42, 0.7)
    for x in pegs:
        rod(P, "peg", (x, -0.13, 1.72), (x, -0.27, 1.75), 0.016, wood(3, wear=0.05), segs=6)
    # scythes hang by their snaths between the posts, blades curving out over the ends
    _scythe(P, "scythe0", (-0.66, -0.2, 0.32), flip=-1)
    _scythe(P, "scythe1", (0.56, -0.2, 0.28), flip=1)
    # hay rakes hang head-up from pairs of pegs
    for k, x in enumerate((-0.0, 0.26)):
        long_tool(P, f"rake{k}", (x + 0.02 * k, -0.24 - 0.04 * k, 0.15 + 0.06 * k), (x, -0.25 - 0.04 * k, 1.85), kind="rake")
    long_tool(P, "fork", (1.35, -0.45, 0.0), (1.05, -0.1, 1.55), kind="fork")
    long_tool(P, "hoe", (1.45, -0.05, 0.0), (1.05, 0.08, 1.45), kind="hoe")
    return P


# ================================================================== pitchfork stack
def pitchfork_stack(seed=52):
    """A coopered barrel with its lid on, four pitchforks and a shovel leant round it tines up, loose hay at its foot."""
    rnd = random.Random(seed)
    P = _root("forkstack")
    r0, bulge, h = 0.28, 0.035, 0.86
    staves(P, "stave", 14, r0, bulge, h, 0.025, [wood(k, wear=0.25) for k in range(4)], (0, 0, 0), rnd, levels=5)
    for f in (0.08, 0.32, 0.68, 0.92):
        K.torus("hoop", r0 + bulge * math.sin(math.pi * f) + 0.004, 0.008, _iron(RUST), loc=(0, 0, h * f), segs=24, parent=P)
    K.cyl("lid", r0 + 0.005, 0.03, wood(2), loc=(0, 0, h - 0.02), segs=14, bev=0.005, parent=P)
    K.box("lid_cleat", (0.05, 0.4, 0.025), wood(3), loc=(0, 0, h + 0.005), bev=0.004, parent=P)
    for k, (a, kind, L) in enumerate(((-125, "fork", 1.3), (-55, "shovel", 1.1), (20, "fork", 1.25), (110, "fork", 1.3))):
        ar = math.radians(a)
        d, rim = 0.46, 0.315
        butt = Vector((math.cos(ar) * d, math.sin(ar) * d, 0.0))
        rest = Vector((math.cos(ar) * rim, math.sin(ar) * rim, h + 0.01))
        g = long_tool(P, f"tool{k}", butt, butt + (rest - butt) * 2, kind=kind, L=L)
        g.rotation_euler.rotate_axis("Z", math.radians(rnd.uniform(-30, 30)))
    K.disc("spill", 0.55, _straw_bed(), (0.25, -0.3), z=0.006, segs=14, wobble=0.35, seed=seed).parent = P
    return P


# ================================================================== vegetable crates
def _fill(G, kind, rnd, z=0.25):
    """Fill a crate (0.6 x 0.4 inside) to z with potatoes, tomatoes or carrots heaped a little above it."""
    if kind == "potato":
        m = K.mat("potato", POTATO, rough=0.9, grime=0.4, noise_scale=8.0)
        K.box("fill", (0.54, 0.35, 0.02), m, loc=(0, 0, z - 0.01), bev=0, parent=G)
        for k in range(11):
            lump(G, "potato", 0.05, (rnd.uniform(-0.23, 0.23), rnd.uniform(-0.13, 0.13), z - 0.02), (1.3, 1.0, 0.8), m, rnd, jitter=0.15, u=6, v=4,
                 rot=(0, 0, rnd.uniform(0, 180)))
    elif kind == "tomato":
        m = K.mat("tomato", TOMATO, rough=0.35, grime=0.1, noise_scale=6.0)
        K.box("fill", (0.54, 0.35, 0.02), m, loc=(0, 0, z - 0.01), bev=0, parent=G)
        for k in range(10):
            lump(G, "tomato", 0.045, (rnd.uniform(-0.23, 0.23), rnd.uniform(-0.13, 0.13), z), (1.0, 1.0, 0.85), m, rnd, jitter=0.04, u=7, v=5)
        for k in range(3):
            K.cyl("calyx", 0.02, 0.01, K.mat("leaf", LEAF, rough=0.8), loc=(rnd.uniform(-0.2, 0.2), rnd.uniform(-0.1, 0.1), z + 0.045), segs=5, bev=0,
                  parent=G)
    else:
        m = K.mat("beetroot", BEET, rough=0.6, grime=0.3, noise_scale=6.0)
        leaf = K.mat("leaf", LEAF, rough=0.8, grime=0.2)
        K.box("fill", (0.54, 0.35, 0.02), m, loc=(0, 0, z - 0.01), bev=0, parent=G)
        for k in range(10):
            y = rnd.uniform(-0.14, 0.14)
            x = rnd.uniform(-0.2, 0.1)
            yaw = rnd.uniform(-15, 15)
            c = _grp(G, "carrot", (x, y, z + 0.015), (0, 0, yaw))
            K.cyl("root", 0.022, 0.18, m, loc=(0.0, 0, 0), rot=(0, 90, 0), segs=5, r2=0.004, bev=0, parent=c)
            K.cyl("top", 0.012, 0.16, leaf, loc=(-0.16, 0, 0.02), rot=(0, 75, 0), segs=4, r2=0.035, bev=0, parent=c)


def vegetable_crates(seed=53):
    """Slatted crates stacked two and one, full of potatoes, tomatoes and purple carrots, a fourth on the ground in
    front and an empty one upturned as a seat."""
    rnd = random.Random(seed)
    P = _root("vegcrates")
    a = crate(P, "crate_a", (-0.32, 0.05, 0.0), yaw=2, shade=0)
    _fill(a, "potato", rnd)
    b = crate(P, "crate_b", (0.32, 0.02, 0.0), yaw=-3, shade=1)
    _fill(b, "tomato", rnd)
    c = crate(P, "crate_c", (0.02, 0.08, 0.302), yaw=8, shade=2)
    _fill(c, "carrot", rnd)
    d = crate(P, "crate_d", (0.55, -0.6, 0.0), yaw=-24, shade=3)
    _fill(d, "potato", rnd, z=0.24)
    crate(P, "crate_e", (-0.6, -0.62, 0.3), rot=(180, 0, 14), shade=1)
    # a few potatoes and a tomato spilled in the grass
    m = K.mat("potato", POTATO, rough=0.9, grime=0.4, noise_scale=8.0)
    for k in range(4):
        lump(P, "spilt", 0.05, (0.15 + rnd.uniform(-0.25, 0.25), -0.5 + rnd.uniform(-0.15, 0.15), 0.03), (1.3, 1.0, 0.8), m, rnd, jitter=0.15, u=6, v=4)
    lump(P, "spilt_t", 0.045, (-0.1, -0.35, 0.035), (1.0, 1.0, 0.85), K.mat("tomato", TOMATO, rough=0.35, grime=0.1, noise_scale=6.0), rnd, jitter=0.04,
         u=7, v=5)
    return P


# ================================================================== pumpkin pile
def pumpkin_pile(seed=54):
    """A heap of squashes on a bed of straw: mustard, olive, pale green and white pumpkins, a few bottle gourds, none
    of them orange."""
    rnd = random.Random(seed)
    P = _root("pumpkins")
    K.disc("bed", 1.05, _straw_bed(), (0, 0), z=0.008, segs=16, wobble=0.25, seed=seed).parent = P
    cols = [MUSTARD, OLIVE, GOURD_G, GOURD_W, GOURD_D]
    mats = [K.mat(f"squash{k}", c, rough=0.55, grime=0.3, noise_scale=5.0, wear=0.1) for k, c in enumerate(cols)]
    # (x, y, z, r, h, colour)
    heap = [(-0.45, -0.1, 0, 0.3, 0.42, 1), (0.15, -0.25, 0, 0.32, 0.4, 0), (0.6, 0.15, 0, 0.26, 0.34, 2), (-0.15, 0.4, 0, 0.3, 0.38, 4),
            (0.3, 0.55, 0, 0.24, 0.3, 3), (-0.75, 0.35, 0, 0.22, 0.3, 0), (-0.05, 0.08, 0.27, 0.27, 0.36, 3), (0.42, -0.05, 0.22, 0.2, 0.28, 1),
            (-0.4, 0.2, 0.25, 0.2, 0.28, 2), (0.08, 0.15, 0.56, 0.2, 0.26, 0), (0.55, -0.6, 0, 0.17, 0.24, 4), (-0.7, -0.55, 0, 0.15, 0.2, 2)]
    for k, (x, y, z, r, h, c) in enumerate(heap):
        pumpkin(P, f"pumpkin{k}", r, h, mats[c], (x, y, z), rnd, ribs=7 + k % 3, rot=(rnd.uniform(-8, 8) if z else 0, rnd.uniform(-8, 8) if z else 0,
                                                                                     rnd.uniform(0, 180)))
    # bottle gourds lying on their sides
    for k, (x, y, yaw, c) in enumerate(((0.0, -0.75, 20, 2), (-0.3, -0.62, 100, 3), (0.9, -0.25, -40, 0))):
        prof = [(0, 0), (0.07, 0.01), (0.1, 0.06), (0.1, 0.12), (0.07, 0.18), (0.04, 0.23), (0.045, 0.29), (0.035, 0.33), (0, 0.34)]
        turned(P, f"gourd{k}", prof, mats[c], segs=10, loc=(x, y, 0.1), rot=(90, 0, yaw))
    return P


# ================================================================== corn crib
def corn_crib(seed=55):
    """A long, narrow crib of spaced slats on stilts with tin rat guards, full of yellow cobs that show between the
    slats, under a gable roof of corrugated sheet."""
    rnd = random.Random(seed)
    P = _root("corncrib")
    hx, hy = 1.1, 0.45
    zf, H = 0.65, 1.55
    zt = zf + H
    for x in (-hx + 0.05, 0.0, hx - 0.05):
        for s in (-1, 1):
            y = s * (hy - 0.05)
            K.box("stilt", (0.12, 0.12, zf), wood(3), loc=(x, y, zf / 2), bev=0.012, parent=P)
            K.box("pad", (0.26, 0.26, 0.1), K.mat("pad", STONE, rough=0.9, grime=0.4, moss=0.4, cracks=0.3), loc=(x, y, 0.03), rot=(0, 0, rnd.uniform(-10, 10)),
                  bev=0.025, parent=P)
            K.cyl("guard", 0.17, 0.06, tin(ZINC, wear=0.2), loc=(x, y, zf - 0.17), segs=12, r2=0.07, bev=0, parent=P)
    for s in (-1, 1):
        plank(P, f"sill{s}", (2 * hx + 0.1, 0.12, 0.12), (0, s * (hy - 0.05), zf + 0.06), shade=3)
    plank(P, "floor", (2 * hx, 2 * hy, 0.04), (0, 0, zf + 0.14), shade=1)
    for x in (-hx + 0.04, 0.0, hx - 0.04):
        for s in (-1, 1):
            K.box("stud", (0.08, 0.08, H), wood(3), loc=(x, s * (hy - 0.04), zf + 0.12 + H / 2), bev=0.01, parent=P)
    # the slats: long sides and ends, a gap between each
    n = 10
    for j in range(n):
        z = zf + 0.22 + j * (H - 0.12) / n
        for s in (-1, 1):
            K.box("slat", (2 * hx + 0.04, 0.02, 0.06), wood((j + s) % 4, wear=0.2), loc=(0, s * (hy + 0.01), z), rot=(0, rnd.uniform(-0.6, 0.6), 0), bev=0,
                  parent=P)
            K.box("end_slat", (0.02, 2 * hy + 0.04, 0.06), wood((j + 2) % 4, wear=0.2), loc=(s * (hx + 0.01), 0, z), bev=0, parent=P)
    # corn: a mass of cobs behind the slats, single cobs pushed against the gaps
    corn = K.mat("corn", CORN, rough=0.8, grime=0.25, noise_scale=40.0, cracks=0.2)
    K.box("corn", (2 * hx - 0.12, 2 * hy - 0.1, H - 0.2), corn, loc=(0, 0, zf + 0.16 + (H - 0.2) / 2), bev=0.03, parent=P)
    for k in range(26):
        s = rnd.choice((-1, 1))
        if k % 3 == 0:
            x, y = s * (hx - 0.07), rnd.uniform(-hy + 0.1, hy - 0.1)
            rot = (90, 0, rnd.uniform(-20, 20))
        else:
            x, y = rnd.uniform(-hx + 0.15, hx - 0.15), s * (hy - 0.06)
            rot = (rnd.uniform(-20, 20), 90, rnd.uniform(-25, 25))
        K.cyl("cob", 0.035, 0.2, corn, loc=(x, y, zf + 0.25 + rnd.uniform(0, H - 0.35)), rot=rot, segs=6, r2=0.025, bev=0, parent=P)
    # gable ends and the roof
    rz = 0.42
    for s in (-1, 1):
        prism(P, f"gable{s}", [(-hy - 0.03, 0), (hy + 0.03, 0), (0, rz)], 0.022, wood(1, wear=0.5), loc=(s * (hx + 0.02), 0, zt), plane="yz")
    plank(P, "ridge_beam", (2 * hx + 0.1, 0.08, 0.1), (0, 0, zt + rz - 0.05), shade=3)
    ang = math.degrees(math.atan2(rz, hy + 0.03))
    sl = math.hypot(hy + 0.25, rz + 0.13)
    for s in (-1, 1):
        for k, (x, col) in enumerate(((-0.6, ZINC), (0.6, RUST if s < 0 else ZINC))):
            corrugated(P, f"roof{s}{k}", 1.24, sl, tin(col, metal=0.3, wear=0.15, scratches=0.5),
                       loc=(x, s * (hy + 0.22) / 2, zt + rz / 2 + 0.04 + k * 0.01), rot=(-s * ang, 0, 0), pitch=0.15)
    K.box("ridge_cap", (2 * hx + 0.5, 0.18, 0.03), tin(ZINC, wear=0.2), loc=(0, 0, zt + rz + 0.06), bev=0.006, parent=P)
    # a hatch in the left end
    K.box("hatch", (0.03, 0.5, 0.6), wood(2), loc=(-hx - 0.04, 0, zf + 0.9), bev=0.008, parent=P)
    K.box("hatch_bar", (0.03, 0.6, 0.06), _iron(), loc=(-hx - 0.06, 0, zf + 0.9), bev=0.004, parent=P)
    return P


# ================================================================== irrigation pump
def irrigation_pump(seed=56):
    """A settler pump on a plank skid: a teal-painted cylinder worked by a spoked flywheel and crank, its suction pipe
    down into a sump and its hose wound on a reel beside it, the free end trailing off over the grass."""
    rnd = random.Random(seed)
    P = _root("irrpump")
    for y in (-0.32, 0.32):
        plank(P, "skid", (1.7, 0.12, 0.1), (0, y, 0.05), shade=3)
    for k in range(7):
        plank(P, f"deck{k}", (0.2, 0.84, 0.035), (-0.72 + k * 0.24, 0, 0.118), shade=k)
    zd = 0.135
    paint = M(SEA, metal=0.4, wear=0.3, grime=0.45)
    iron = _iron()
    px, py = -0.4, 0.05
    K.box("flange", (0.3, 0.3, 0.04), iron, loc=(px, py, zd + 0.02), bev=0.006, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bolt(P, (px + sx * 0.12, py + sy * 0.12, zd + 0.045), rot=(0, 0, 0))
    K.cyl("barrel", 0.11, 0.5, paint, loc=(px, py, zd + 0.29), segs=16, bev=0.01, parent=P)
    for z in (0.08, 0.5):
        K.cyl("band", 0.125, 0.04, iron, loc=(px, py, zd + z), segs=16, bev=0.005, parent=P)
    K.cyl("head", 0.09, 0.08, iron, loc=(px, py, zd + 0.58), segs=12, bev=0.008, parent=P)
    rod(P, "outlet", (px + 0.1, py, zd + 0.42), (px + 0.38, py, zd + 0.42), 0.032, iron, segs=10)
    # suction pipe down off the back of the skid into a sump
    rod(P, "suction", (px, py + 0.1, zd + 0.15), (px, 0.62, zd + 0.15), 0.03, _iron(RUST), segs=8)
    K.sphere("elbow", 0.032, _iron(RUST), loc=(px, 0.62, zd + 0.15), parent=P)
    rod(P, "suction_down", (px, 0.62, zd + 0.15), (px, 0.66, -0.1), 0.03, _iron(RUST), segs=8)
    K.disc("sump", 0.32, K.mat("sump_water", "#2f4f55", rough=0.05), (px, 0.68), z=0.01, segs=14, wobble=0.2, seed=seed).parent = P
    K.disc("sump_mud", 0.45, K.mat("trodden", MUD, rough=0.95, noise_scale=4.0), (px, 0.68), z=0.005, segs=14, wobble=0.25, seed=seed + 1).parent = P
    # the flywheel and crank on two iron uprights
    wx, wz = -0.4, 0.82
    for s in (-1, 1):
        beam(P, f"upright{s}", (wx + s * 0.18, -0.24, zd), (wx + s * 0.04, -0.24, wz), 0.04, 0.02, iron, bev=0)
    rod(P, "crankshaft", (wx, -0.2, wz), (wx, -0.42, wz), 0.02, iron)
    W = _grp(P, "flywheel", (wx, -0.36, wz), (0, rnd.uniform(0, 40), 0))
    K.torus("rim", 0.34, 0.03, iron, rot=(90, 0, 0), segs=24, parent=W)
    K.cyl("hub", 0.06, 0.08, iron, rot=(90, 0, 0), segs=10, bev=0.006, parent=W)
    for k in range(5):
        a = 72 * k
        K.box("spoke", (0.025, 0.02, 0.3), iron, loc=(math.sin(math.radians(a)) * 0.16, 0, math.cos(math.radians(a)) * 0.16), rot=(0, a, 0), bev=0, parent=W)
    rod(W, "crank_pin", (0.0, 0, 0.3), (0.0, -0.12, 0.3), 0.012, iron)
    K.cyl("crank_grip", 0.022, 0.12, wood(0, wear=0.05), loc=(0.0, -0.2, 0.3), rot=(90, 0, 0), segs=8, bev=0.004, parent=W)
    rod(P, "conrod", (px, py - 0.02, zd + 0.62), (wx, -0.28, wz - 0.1), 0.015, iron)
    # the hose reel
    rx = 0.4
    for s in (-1, 1):
        for t in (-1, 1):
            beam(P, f"reel_leg{s}{t}", (rx + t * 0.22, s * 0.25, zd), (rx, s * 0.25, 0.62), 0.04, 0.04, wood(2))
    rod(P, "reel_axle", (rx, -0.32, 0.6), (rx, 0.32, 0.6), 0.018, iron)
    hose = K.mat("hose", HOSE, rough=0.6, grime=0.3)
    for s in (-1, 1):
        K.cyl(f"reel_flange{s}", 0.3, 0.02, wood(1), loc=(rx, s * 0.2, 0.6), rot=(90, 0, 0), segs=16, bev=0.004, parent=P)
    K.cyl("reel_hose", 0.24, 0.36, hose, loc=(rx, 0, 0.6), rot=(90, 0, 0), segs=16, bev=0.03, parent=P)
    for y in (-0.12, 0.0, 0.12):
        K.torus("coil", 0.24, 0.022, hose, loc=(rx, y, 0.6), rot=(90, 0, 0), segs=16, parent=P)
    rod(P, "reel_crank", (rx, -0.32, 0.6), (rx + 0.14, -0.32, 0.72), 0.012, iron)
    rod(P, "reel_grip", (rx + 0.14, -0.32, 0.72), (rx + 0.14, -0.42, 0.72), 0.018, wood(0, wear=0.05))
    # hose from the outlet to the reel hub, and the free end off the reel over the ground
    cable(P, "hose_in", [(px + 0.38, py, zd + 0.42), (0.1, 0.1, 0.5), (rx - 0.05, 0.25, 0.6)], 0.024, hose)
    cable(P, "hose_out", [(rx + 0.2, -0.05, 0.42), (rx + 0.45, -0.25, 0.04), (rx + 0.9, -0.55, 0.025), (rx + 1.2, -1.0, 0.025)], 0.024, hose)
    return P


# ================================================================== sprinkler pipe
def sprinkler_pipe(seed=57):
    """A 4 m run of zinc pipe on crossed-stick stands, three risers with impact sprinkler heads, a rag-bound joint, an
    end cap and a hose to the supply at the other end, damp ground under the heads."""
    rnd = random.Random(seed)
    P = _root("sprinkler")
    zp = 0.55
    pipe = M(ZINC, metal=0.55, wear=0.2, grime=0.4)
    iron = _iron()
    rod(P, "pipe_a", (-2.0, 0, zp), (0.0, 0, zp), 0.034, pipe, segs=10)
    rod(P, "pipe_b", (0.0, 0, zp), (1.85, 0, zp), 0.034, pipe, segs=10)
    K.cyl("coupling", 0.045, 0.12, iron, loc=(0, 0, zp), rot=(0, 90, 0), segs=10, bev=0.005, parent=P)
    K.cyl("rag", 0.05, 0.14, M(BRICK, wear=0.2, grime=0.5, rough=0.95, scratches=0), loc=(-1.0, 0, zp), rot=(0, 90, 0), segs=8, bev=0.01, parent=P)
    K.cyl("cap", 0.045, 0.06, iron, loc=(-2.02, 0, zp), rot=(0, 90, 0), segs=10, bev=0.008, parent=P)
    for k, x in enumerate((-1.7, -0.55, 0.6, 1.6)):
        lean = rnd.uniform(-4, 4)
        for s in (-1, 1):
            rod(P, f"stick{k}{s}", (x + lean * 0.005, s * 0.3, -0.05), (x, -s * 0.07, zp + 0.13), 0.022, wood(3, wear=0.1), segs=6)
        K.torus(f"lash{k}", 0.05, 0.008, rope_mat(), loc=(x, 0, zp + 0.02), rot=(0, 90, 0), segs=10, parent=P)
    for k, x in enumerate((-1.2, 0.0, 1.15)):
        if k == 1:
            x = 0.2
        zr = zp + 0.45
        rod(P, f"riser{k}", (x, 0, zp), (x, 0, zr), 0.016, pipe, segs=8)
        K.cyl(f"tee{k}", 0.042, 0.08, iron, loc=(x, 0, zp + 0.02), segs=8, bev=0.004, parent=P)
        H = _grp(P, f"head{k}", (x, 0, zr), (0, 0, rnd.uniform(0, 360)))
        K.cyl("body", 0.022, 0.06, iron, loc=(0, 0, 0.03), segs=8, bev=0.004, parent=H)
        rod(H, "nozzle", (0, 0, 0.05), (0.1, 0, 0.1), 0.01, M(ZINC, metal=0.6, wear=0.2), segs=6)
        rod(H, "arm", (-0.06, 0, 0.09), (0.09, 0, 0.12), 0.006, iron, segs=4)
        K.box("deflector", (0.02, 0.025, 0.03), iron, loc=(0.1, 0, 0.12), bev=0, parent=H)
        K.box("spring", (0.03, 0.01, 0.04), iron, loc=(-0.05, 0, 0.11), bev=0, parent=H)
        K.disc(f"damp{k}", 0.7, K.mat("damp", "#4f7a38", rough=0.9, noise_scale=3.0), (x, rnd.uniform(-0.2, 0.2)), z=0.004 + 0.002 * k, segs=16, wobble=0.2,
               seed=seed + k).parent = P
    # supply end: an elbow down to a hose coupling and a hose away through the grass
    rod(P, "drop", (1.85, 0, zp), (1.9, 0, 0.12), 0.034, pipe, segs=10)
    K.sphere("elbow", 0.04, iron, loc=(1.85, 0, zp), parent=P)
    K.cyl("valve", 0.05, 0.08, iron, loc=(1.89, 0, 0.3), segs=8, bev=0.006, parent=P)
    K.box("valve_handle", (0.16, 0.025, 0.02), M(SIGNAL, metal=0.3, wear=0.3), loc=(1.89, 0, 0.355), rot=(0, 0, 30), bev=0.004, parent=P)
    cable(P, "hose", [(1.9, 0, 0.12), (1.98, -0.05, 0.03), (2.2, -0.3, 0.025), (2.35, -0.8, 0.025)], 0.025, K.mat("hose", HOSE, rough=0.6, grime=0.3))
    return P


# ================================================================== cold frame
def cold_frame(seed=58):
    """A low plank box sloping to the front, two old window sashes for a lid, one propped open on a stick, a pane
    gone from the other, seedlings in rows inside."""
    rnd = random.Random(seed)
    P = _root("coldframe")
    W, D, zb, zf = 1.6, 0.9, 0.45, 0.28

    def top(y):
        return zf + (zb - zf) * (y + D / 2) / D

    for s in (-1, 1):
        plank(P, f"wall{s}", (W, 0.03, (zb if s > 0 else zf)), (0, s * D / 2, (zb if s > 0 else zf) / 2), shade=s + 1)
        prism(P, f"end{s}", [(-D / 2, 0), (D / 2, 0), (D / 2, zb), (-D / 2, zf)], 0.03, wood(2), loc=(s * W / 2, 0, 0), plane="yz")
    for x in (-W / 2 + 0.04, 0.0, W / 2 - 0.04):
        for s in (-1, 1):
            K.box("corner", (0.05, 0.05, top(s * (D / 2 - 0.04)) - 0.02), wood(3), loc=(x, s * (D / 2 - 0.04), (top(s * (D / 2 - 0.04)) - 0.02) / 2), bev=0.006,
                  parent=P)
    K.box("soil", (W - 0.06, D - 0.06, 0.12), _soil(), loc=(0, 0, 0.06), bev=0.01, parent=P)
    leaf = K.mat("leaf", LEAF, rough=0.8, grime=0.2)
    for row in range(3):
        y = -0.25 + row * 0.25
        for k in range(7):
            x = -0.66 + k * 0.22 + rnd.uniform(-0.03, 0.03)
            g = _grp(P, "seedling", (x, y, 0.12), (0, 0, rnd.uniform(0, 180)))
            for s in (-1, 1):
                K.cyl("leaf", 0.022, 0.08, leaf, loc=(s * 0.025, 0, 0.04), rot=(0, s * 50, 0), segs=4, r2=0.004, bev=0, parent=g)
    # the sashes, hinged at the back
    ang = math.degrees(math.atan2(zb - zf, D))
    paint = M(BONE, wear=0.6, grime=0.45, rough=0.8)
    glass = K.mat("glass", GLASS, rough=0.05, metal=0.2)

    def sash(name, x, opened, missing=None):
        H = _grp(P, name, (x, D / 2 + 0.01, zb + 0.02), (ang - opened, 0, 0))
        w, d = W / 2 - 0.01, D + 0.06
        for s in (-1, 1):
            K.box("stile", (0.05, d, 0.035), paint, loc=(s * (w / 2 - 0.025), -d / 2, 0), bev=0.006, parent=H)
        for y in (-0.025, -d + 0.025):
            K.box("rail", (w, 0.05, 0.035), paint, loc=(0, y, 0), bev=0.006, parent=H)
        K.box("bar", (0.025, d - 0.08, 0.03), paint, loc=(0, -d / 2, 0), bev=0.004, parent=H)
        K.box("bar_x", (w - 0.08, 0.025, 0.03), paint, loc=(0, -d / 2, 0), bev=0.004, parent=H)
        for i, (px, py) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
            if i == missing:
                prism(H, "shard", [(-0.08, 0), (0.1, 0), (-0.02, 0.15)], 0.004, glass, loc=(px * w / 4 - 0.05, -d / 2 + py * d / 4 - d / 4 + 0.03, 0.0),
                      rot=(-90, 0, 0), bev=0)
                continue
            K.box("pane", (w / 2 - 0.05, d / 2 - 0.05, 0.004), glass, loc=(px * w / 4, -d / 2 + py * d / 4, 0.0), bev=0, parent=H)
        return H

    sash("sash_l", -W / 4, 0, missing=1)
    sash("sash_r", W / 4, 32)
    rod(P, "prop", (W / 4 + 0.15, -D / 2 + 0.03, zf + 0.005), (W / 4 + 0.15, -0.31, 0.75), 0.012, wood(3, wear=0.1), segs=5)
    return P


# ================================================================== greenhouse
def _profile(R, base, wall, u):
    """A point and its outward normal on a hoop house's section: up the right wall, over the arch, down the left."""
    L = 2 * wall + math.pi * R
    d = u * L
    if d <= wall:
        return (R, base + d), (1, 0)
    if d <= wall + math.pi * R:
        a = (d - wall) / R
        return (R * math.cos(a), base + wall + R * math.sin(a)), (math.cos(a), math.sin(a))
    return (-R, base + wall - (d - wall - math.pi * R)), (-1, 0)


def _hoop_sheet(P, name, R, base, wall, u0, u1, y0, y1, mat, hoops, off=0.0, nu=16, nv=12, sag=0.03, thick=0.006):
    """Plastic sheet over the hoops from u0 to u1 round the section and y0 to y1 along the house, sagging between
    the hoops."""
    L = 2 * wall + math.pi * R
    us = sorted({u0, u1} | {u for u in (wall / L, (L - wall) / L) if u0 < u < u1} | {u0 + (u1 - u0) * i / nu for i in range(nu + 1)})
    bm = bmesh.new()
    rows = []
    for j in range(nv + 1):
        y = y0 + (y1 - y0) * j / nv
        gap = min(abs(y - h) for h in hoops)
        bay = min(abs(b - a) for a, b in zip(hoops, hoops[1:])) / 2
        s = sag * math.sin(math.pi / 2 * min(gap / bay, 1.0))
        row = []
        for u in us:
            (x, z), (nx, nz) = _profile(R, base, wall, u)
            k = off - s * (0.3 if abs(nz) < 0.01 else 1.0)
            row.append(bm.verts.new((x + nx * k, y, z + nz * k)))
        rows.append(row)
    for j in range(nv):
        for i in range(len(us) - 1):
            bm.faces.new((rows[j][i], rows[j + 1][i], rows[j + 1][i + 1], rows[j][i + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    if thick:
        sol = ob.modifiers.new("solid", "SOLIDIFY")
        sol.thickness = thick
    return K.place(ob, parent=P)


def greenhouse_small(seed=59):
    """A hoop greenhouse of bent tube over a timber base: a milky plastic sheet patched with tape and a teal offcut, a
    framed door end with its flap rolled up, a slatted bench of pots inside and sandbags holding the skirt down."""
    rnd = random.Random(seed)
    P = _root("greenhouse")
    R, base, wall, hl = 1.2, 0.15, 0.55, 1.5
    hoops = [-hl + 2 * hl * k / 4 for k in range(5)]
    tube = M(ZINC, metal=0.55, wear=0.2, grime=0.4)
    for s in (-1, 1):
        plank(P, f"base{s}", (0.1, 2 * hl + 0.1, 0.15), (s * R, 0, 0.075), shade=3)
        plank(P, f"base_end{s}", (2 * R + 0.1, 0.1, 0.15), (0, s * hl, 0.075), shade=3)
    for y in hoops:
        K.torus("hoop", R, 0.02, tube, loc=(0, y, base + wall), rot=(90, 0, 0), arc=180, segs=28, parent=P)
        for s in (-1, 1):
            rod(P, "hoop_leg", (s * R, y, 0.05), (s * R, y, base + wall), 0.02, tube, segs=6)
    rod(P, "ridge", (0, -hl - 0.05, base + wall + R - 0.02), (0, hl + 0.05, base + wall + R - 0.02), 0.018, tube, segs=6)
    plastic = K.mat("plastic", PLASTIC, rough=0.35, grime=0.35, noise_scale=3.0)
    _hoop_sheet(P, "sheet", R, base, wall, 0.0, 1.0, -hl - 0.02, hl + 0.02, plastic, hoops, off=0.03)
    # patches: grey tape strips over splits and a teal offcut
    tape = M("#b9bdb5", rough=0.6, wear=0.1, grime=0.3, scratches=0)
    for k, (u, y) in enumerate(((0.35, -0.4), (0.62, 0.55), (0.2, 1.0))):
        _hoop_sheet(P, f"tape{k}", R, base, wall, u - 0.012, u + 0.012, y - 0.35, y + 0.35, tape, hoops, off=0.04, nu=2, nv=4, thick=0)
    _hoop_sheet(P, "offcut", R, base, wall, 0.12, 0.27, 0.15, 0.95, M(SEA, rough=0.5, wear=0.1, grime=0.35, scratches=0), hoops, off=0.042, nu=4, nv=4,
                thick=0)
    # back end: a flat sheet; front end: door frame with the flap rolled up over it
    arch = [_profile(R, base, wall, i / 20)[0] for i in range(21)]
    prism(P, "back", arch, 0.006, plastic, loc=(0, hl + 0.01, 0), plane="xz", bev=0)
    dx, dh = 0.42, 1.8
    door = [(-dx, base), (-R, base)] + [_profile(R, base, wall, 1 - i / 20)[0] for i in range(21)][1:-1] + [(R, base), (dx, base), (dx, dh), (-dx, dh)]
    prism(P, "front", list(reversed(door)), 0.006, plastic, loc=(0, -hl - 0.01, 0), plane="xz", bev=0)
    for s in (-1, 1):
        K.box(f"jamb{s}", (0.07, 0.07, dh + 0.02), wood(3), loc=(s * (dx + 0.035), -hl - 0.04, dh / 2), bev=0.008, parent=P)
    K.box("lintel", (2 * dx + 0.14, 0.07, 0.07), wood(3), loc=(0, -hl - 0.04, dh + 0.035), bev=0.008, parent=P)
    K.cyl("roll", 0.06, 2 * dx + 0.1, plastic, loc=(0, -hl - 0.12, dh - 0.06), rot=(0, 90, 0), segs=10, bev=0.01, parent=P)
    for s in (-1, 1):
        K.cyl(f"tie{s}", 0.064, 0.02, rope_mat(), loc=(s * 0.25, -hl - 0.12, dh - 0.06), rot=(0, 90, 0), segs=10, bev=0, parent=P)
    # inside: a slatted bench along the left with pots of greens, soil floor
    K.box("floor", (2 * R - 0.1, 2 * hl - 0.1, 0.02), _soil(), loc=(0, 0, 0.01), bev=0, parent=P)
    bx = -0.75
    for y in (-1.1, 0.0, 1.1):
        for s in (-1, 1):
            K.box("bench_leg", (0.05, 0.05, 0.7), wood(3), loc=(bx + s * 0.22, y, 0.35), bev=0.006, parent=P)
    for k in range(4):
        plank(P, "bench_slat", (0.11, 2.4, 0.025), (bx - 0.2 + k * 0.135, 0, 0.71), shade=k)
    clay = M(CLAY, rough=0.85, wear=0.4, grime=0.4)
    leaf = K.mat("leaf", LEAF, rough=0.8, grime=0.2)
    for k in range(6):
        y = -1.0 + k * 0.4
        x = bx + rnd.uniform(-0.12, 0.12)
        turned(P, "pot", [(0, 0), (0.07, 0), (0.09, 0.14), (0, 0.14)], clay, segs=8, loc=(x, y, 0.72))
        lump(P, "greens", 0.1, (x, y, 0.84), (1.0, 1.0, 0.8), leaf, rnd, jitter=0.25, u=6, v=4)
    for k, (x, y) in enumerate(((-R + 0.1, -1.4), (R - 0.05, -0.5), (R - 0.05, 0.9))):
        sack(P, f"sandbag{k}", (0.45, 0.28, 0.16), (x + (0.12 if x < 0 else 0.1), y, 0.0), (0, 0, 90 if k else 10), K.mat("sack1", SACK_D, rough=0.95, grime=0.4,
             noise_scale=9.0), rnd, tied=False)
    return P


# ================================================================== potting bench
def potting_bench(seed=60):
    """A potting bench with a back board and a shelf: clay pots and a nested stack, seed trays of seedlings, a trowel
    in spilt compost, a compost sack on the bottom shelf and a zinc watering can on the ground."""
    rnd = random.Random(seed)
    P = _root("pottingbench")
    W, D, H = 1.5, 0.6, 0.88
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = 1.45 if sy > 0 else H
            K.box("leg", (0.07, 0.07, h), wood(3), loc=(sx * (W / 2 - 0.06), sy * (D / 2 - 0.05), h / 2), bev=0.008, parent=P)
        plank(P, f"bearer{sx}", (0.06, D, 0.08), (sx * (W / 2 - 0.06), 0, H - 0.06), shade=3)
        plank(P, f"low_bearer{sx}", (0.06, D, 0.06), (sx * (W / 2 - 0.06), 0, 0.22), shade=3)
    for k in range(4):
        plank(P, f"top{k}", (W, 0.145, 0.035), (0, -D / 2 + 0.075 + k * 0.15, H), shade=k)
    for k in range(3):
        plank(P, f"low{k}", (W - 0.1, 0.17, 0.025), (0, -D / 2 + 0.1 + k * 0.2, 0.26), shade=k + 1)
    for k in range(3):
        plank(P, f"back{k}", (W, 0.025, 0.16), (0, D / 2 - 0.005, H + 0.1 + k * 0.165), shade=(k + 2) % 4)
    plank(P, "shelf", (W, 0.2, 0.03), (0, D / 2 - 0.12, 1.42), shade=1)
    for s in (-1, 1):
        prism(P, f"bracket{s}", [(0.0, 0.0), (0.18, 0.0), (0.0, -0.18)], 0.03, wood(2), loc=(s * 0.55, D / 2 - 0.02, 1.405), rot=(0, 0, 90), plane="xz")
    clay = M(CLAY, rough=0.85, wear=0.4, grime=0.4)
    leaf = K.mat("leaf", LEAF, rough=0.8, grime=0.2)
    pot = [(0, 0), (0.055, 0), (0.075, 0.1), (0.085, 0.102), (0.085, 0.125), (0, 0.125)]
    for k, x in enumerate((-0.5, -0.25, 0.05, 0.35)):
        turned(P, "shelf_pot", pot, clay, segs=10, loc=(x, D / 2 - 0.12, 1.435))
        if k != 2:
            lump(P, "shelf_green", 0.08, (x, D / 2 - 0.12, 1.55), (1.0, 1.0, 0.8), leaf, rnd, jitter=0.25, u=6, v=4)
    # a nested stack of empty pots, two trays of seedlings, a trowel in a heap of compost
    for k in range(5):
        turned(P, "stack_pot", pot, clay, segs=10, loc=(0.52, 0.1, H + 0.02 + k * 0.035), rot=(0, rnd.uniform(-3, 3), 0))
    turned(P, "pot_lying", pot, clay, segs=10, loc=(0.25, -0.15, H + 0.1), rot=(90, 0, 60))
    for k, (x, y, yaw) in enumerate(((-0.45, 0.05, 3), (-0.05, 0.1, -4))):
        T = _grp(P, f"tray{k}", (x, y, H + 0.018), (0, 0, yaw))
        K.box("tray", (0.36, 0.24, 0.06), M(GRAPHITE, rough=0.7, wear=0.1), loc=(0, 0, 0.03), bev=0.006, parent=T)
        K.box("tray_soil", (0.34, 0.22, 0.01), _soil(), loc=(0, 0, 0.058), bev=0, parent=T)
        for i in range(4):
            for j in range(3):
                K.cyl("sprout", 0.018, 0.05, leaf, loc=(-0.13 + i * 0.087, -0.07 + j * 0.07, 0.085), segs=4, r2=0.003, bev=0, parent=T)
    lump(P, "compost", 0.2, (0.15, -0.12, H + 0.018), (1.2, 0.9, 0.3), _soil(), rnd, jitter=0.12, flat=0.0, u=8, v=5)
    tr = _grp(P, "trowel", (0.18, -0.15, H + 0.07), (30, 0, 50))
    K.box("trowel_blade", (0.08, 0.13, 0.006), _iron(ZINC, wear=0.2), loc=(0, 0.06, 0), bev=0.002, parent=tr)
    K.cyl("trowel_handle", 0.016, 0.12, wood(0, wear=0.05), loc=(0, -0.1, 0.02), rot=(90, 0, 0), segs=6, bev=0.003, parent=tr)
    # a compost sack on the bottom shelf
    sack(P, "compost_sack", (0.6, 0.4, 0.22), (-0.3, 0.0, 0.275), (0, 0, 8), K.mat("sack0", SACK, rough=0.95, grime=0.4, noise_scale=9.0), rnd)
    # a watering can beside the bench
    wx, wy = 0.95, -0.15
    zinc = tin(ZINC, wear=0.25)
    K.cyl("can", 0.12, 0.26, zinc, loc=(wx, wy, 0.13), segs=14, bev=0.01, parent=P)
    rod(P, "can_spout", (wx + 0.08, wy, 0.07), (wx + 0.36, wy, 0.33), 0.015, zinc, segs=6)
    K.cyl("can_rose", 0.03, 0.035, zinc, loc=(wx + 0.37, wy, 0.345), rot=(0, 45, 0), segs=8, r2=0.045, bev=0, parent=P)
    K.torus("can_handle", 0.1, 0.01, zinc, loc=(wx - 0.02, wy, 0.26), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    return P


# ================================================================== water butts
def water_butt_stand(seed=61):
    """Two water butts on a raised timber stand, a coopered barrel and a teal steel drum joined by an overflow pipe,
    each with a tap low on the front, a bucket under one."""
    rnd = random.Random(seed)
    P = _root("waterbutts")
    W, D, H = 1.5, 0.75, 0.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("leg", (0.12, 0.12, H), wood(3), loc=(sx * (W / 2 - 0.08), sy * (D / 2 - 0.08), H / 2 - 0.04), bev=0.012, parent=P)
        beam(P, f"brace{sx}", (sx * (W / 2 - 0.08), -D / 2 + 0.12, 0.05), (sx * (W / 2 - 0.08), D / 2 - 0.12, H - 0.12), 0.05, 0.1, wood(2))
    for sy in (-1, 1):
        plank(P, f"bearer{sy}", (W + 0.1, 0.1, 0.14), (0, sy * (D / 2 - 0.08), H - 0.12), shade=3)
        beam(P, f"xbrace{sy}", (-W / 2 + 0.12, sy * (D / 2 - 0.02), 0.05), (W / 2 - 0.12, sy * (D / 2 - 0.02), H - 0.14), 0.1, 0.04, wood(1))
    for k in range(6):
        plank(P, f"deck{k}", (0.24, D + 0.06, 0.04), (-W / 2 + 0.13 + k * 0.248, 0, H - 0.03), rot=(0, 0, rnd.uniform(-1.5, 1.5)), shade=k)
    zt = H - 0.01
    # the barrel
    bx = -0.37
    staves(P, "stave", 16, 0.3, 0.04, 0.9, 0.025, [wood(k, wear=0.25) for k in range(4)], (bx, 0.02, zt), rnd, levels=5)
    for f in (0.07, 0.3, 0.7, 0.93):
        K.torus("hoop", 0.3 + 0.04 * math.sin(math.pi * f) + 0.004, 0.008, _iron(RUST), loc=(bx, 0.02, zt + 0.9 * f), segs=24, parent=P)
    K.cyl("barrel_lid", 0.31, 0.03, wood(2), loc=(bx, 0.02, zt + 0.905), segs=16, bev=0.005, parent=P)
    K.box("lid_cleat", (0.4, 0.05, 0.025), wood(3), loc=(bx, 0.02, zt + 0.93), bev=0.004, parent=P)
    # the drum
    dx = 0.38
    drum = M(SEA, metal=0.4, wear=0.3, grime=0.5, scratches=0.5)
    K.cyl("drum", 0.29, 0.88, drum, loc=(dx, 0.0, zt + 0.44), segs=20, bev=0.015, parent=P)
    for z in (0.29, 0.59):
        K.cyl("drum_rib", 0.297, 0.025, drum, loc=(dx, 0.0, zt + z), segs=20, bev=0, parent=P)
    K.cyl("drum_lid", 0.27, 0.02, _iron(RUST), loc=(dx, 0.0, zt + 0.885), segs=20, bev=0, parent=P)
    K.cyl("drum_bung", 0.04, 0.03, _iron(), loc=(dx + 0.12, 0.08, zt + 0.9), segs=8, bev=0, parent=P)
    # overflow pipe between them, near the top
    rod(P, "overflow", (bx + 0.3, 0.0, zt + 0.78), (dx - 0.28, 0.0, zt + 0.78), 0.025, _iron(ZINC), segs=8)
    # taps
    for k, (x, r) in enumerate(((bx, 0.31), (dx, 0.29))):
        y = -r - 0.02
        rod(P, f"tap{k}", (x, y + 0.04, zt + 0.1), (x, y - 0.07, zt + 0.1), 0.017, _iron(ZINC), segs=8)
        rod(P, f"tap_spout{k}", (x, y - 0.07, zt + 0.1), (x, y - 0.08, zt + 0.04), 0.015, _iron(ZINC), segs=8)
        K.box(f"tap_handle{k}", (0.1, 0.018, 0.018), M(SIGNAL, metal=0.3, wear=0.3), loc=(x, y - 0.05, zt + 0.14), rot=(0, 20, 0), bev=0.003, parent=P)
    # the bucket under the barrel's tap
    K.cyl("bucket", 0.13, 0.26, tin(ZINC, wear=0.25), loc=(bx + 0.05, -0.7, 0.13), segs=12, r2=0.155, bev=0.006, parent=P)
    K.torus("bucket_bail", 0.155, 0.005, _iron(), loc=(bx + 0.05, -0.7, 0.26), rot=(80, 0, 20), arc=180, segs=12, parent=P)
    K.disc("wet", 0.4, K.mat("trodden", MUD, rough=0.95, noise_scale=4.0), (bx, -0.55), z=0.005, segs=14, wobble=0.3, seed=seed).parent = P
    return P


# ================================================================== dovecote
def dovecote(seed=62):
    """A two-tier dovecote on a braced post: a weathered bone-white box with rows of entry holes and landing ledges,
    a rusty pyramid roof with a finial and a few stones round the foot of the post."""
    rnd = random.Random(seed)
    P = _root("dovecote")
    hp = 2.45
    K.box("pole", (0.14, 0.14, hp + 0.1), wood(3, wear=0.4), loc=(0, 0, (hp + 0.1) / 2 - 0.1), bev=0.015, parent=P)
    for k in range(4):
        a = math.radians(90 * k)
        beam(P, "knee", (math.cos(a) * 0.06, math.sin(a) * 0.06, hp - 0.55), (math.cos(a) * 0.38, math.sin(a) * 0.38, hp - 0.02), 0.06, 0.06, wood(2))
    for k in range(7):
        a = 2 * math.pi * k / 7
        lump(P, f"stone{k}", 0.11, (math.cos(a) * 0.24, math.sin(a) * 0.24, 0.0), (1.2, 1.0, 0.7), K.mat("fieldstone", "#80827a", rough=0.9, grime=0.3, moss=0.4,
             cracks=0.3, noise_scale=4.0), rnd, jitter=0.14, flat=0.3, u=7, v=5)
    plank(P, "platform", (0.98, 0.98, 0.05), (0, 0, hp + 0.025), shade=1)
    paint = M(BONE, wear=0.6, grime=0.5, rough=0.8)
    w = 0.78
    z0 = hp + 0.05
    for tier in range(2):
        zc = z0 + 0.2 + tier * 0.42
        K.box(f"tier{tier}", (w, w, 0.4), paint, loc=(0, 0, zc), bev=0.012, parent=P)
        for k in range(4):
            yaw = 90 * k
            F = _grp(P, f"face{tier}{k}", (0, 0, zc), (0, 0, yaw))
            for x in (-0.19, 0.19):
                K.box("hole", (0.12, 0.02, 0.15), _dark(), loc=(x, -w / 2 - 0.002, -0.01), bev=0, parent=F)
                K.cyl("arch", 0.06, 0.02, _dark(), loc=(x, -w / 2 - 0.002, 0.065), rot=(90, 0, 0), segs=8, bev=0, parent=F)
            plank(F, "ledge", (w - 0.04, 0.12, 0.025), (0, -w / 2 - 0.05, -0.1), shade=2)
        plank(P, f"trim{tier}", (w + 0.06, w + 0.06, 0.04), (0, 0, z0 + 0.42 * (tier + 1) - 0.01), shade=3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("corner", (0.04, 0.04, 0.84), wood(3, wear=0.4), loc=(sx * (w / 2 + 0.005), sy * (w / 2 + 0.005), z0 + 0.42), bev=0.006, parent=P)
    zr = z0 + 0.84
    K.cyl("roof", 0.68, 0.42, M(RUST, metal=0.4, wear=0.25, grime=0.5), loc=(0, 0, zr + 0.21), rot=(0, 0, 45), segs=4, r2=0.04, bev=0.01, parent=P)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        rod(P, "hip", (math.cos(a) * 0.66, math.sin(a) * 0.66, zr + 0.02), (math.cos(a) * 0.05, math.sin(a) * 0.05, zr + 0.41), 0.016, tin(ZINC, wear=0.2), segs=5)
    K.cyl("finial_post", 0.03, 0.22, wood(3), loc=(0, 0, zr + 0.5), segs=8, bev=0, parent=P)
    K.sphere("finial", 0.05, tin(ZINC, wear=0.2), loc=(0, 0, zr + 0.63), parent=P)
    K.box("roof_patch", (0.3, 0.008, 0.2), tin(ZINC, wear=0.2), loc=(0.15, -0.33, zr + 0.14), rot=(-58, 0, 0), bev=0.002, parent=P)
    return P


# ================================================================== rabbit hutch
def rabbit_hutch(seed=63):
    """A two-level hutch on legs: a closed nest end and a wire-mesh run on each level behind plank-framed doors, straw
    inside, a felt-and-tin roof sloping to the back."""
    rnd = random.Random(seed)
    P = _root("hutch")
    W, D = 1.4, 0.65
    z1, z2, z3 = 0.38, 0.88, 1.38
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = z3 + (0.04 if sy < 0 else -0.08)
            K.box("leg", (0.06, 0.06, h), wood(3), loc=(sx * (W / 2 - 0.03), sy * (D / 2 - 0.03), h / 2), bev=0.008, parent=P)
    for z in (z1, z2):
        plank(P, f"floor{z}", (W, D, 0.03), (0, 0, z - 0.015), shade=1)
        K.box(f"straw{z}", (W - 0.1, D - 0.12, 0.05), _straw(True), loc=(0, 0.02, z + 0.02), bev=0.02, parent=P)
    # the walls: back and ends of horizontal boards
    for k in range(8):
        z = z1 + 0.06 + k * 0.125
        plank(P, f"back{k}", (W, 0.02, 0.12), (0, D / 2 + 0.01, z), shade=k)
        for sx in (-1, 1):
            K.box(f"end{sx}{k}", (0.02, D, 0.12), M(SEA, wear=0.6, grime=0.45, rough=0.85) if (sx, k) == (1, 5) else wood((k + 1) % 4, wear=0.5),
                  loc=(sx * (W / 2 + 0.01), 0, z), bev=0.006, parent=P)
    # front: on each level a solid nest door at the left and a mesh door at the right
    split = -W / 2 + 0.45
    for lv, (za, zb) in enumerate(((z1, z2), (z2, z3))):
        h = zb - za - 0.04
        zc = (za + zb) / 2
        K.box("divider", (0.05, D, h), wood(2), loc=(split, 0, zc), bev=0.006, parent=P)
        for k in range(3):
            plank(P, "nest_door", (0.135, 0.02, h - 0.02), (-W / 2 + 0.08 + k * 0.14, -D / 2 - 0.01, zc), shade=(k + lv) % 4)
        K.box("turn", (0.025, 0.02, 0.09), wood(3), loc=(split - 0.06, -D / 2 - 0.035, zc), rot=(0, 25, 0), bev=0.004, parent=P)
        mx0, mx1 = split + 0.03, W / 2 - 0.02
        for x in (mx0 + 0.02, mx1 - 0.02):
            K.box("door_stile", (0.04, 0.03, h), wood(0), loc=(x, -D / 2 - 0.015, zc), bev=0.006, parent=P)
        for z in (za + 0.04, zb - 0.04):
            K.box("door_rail", (mx1 - mx0, 0.03, 0.04), wood(0), loc=((mx0 + mx1) / 2, -D / 2 - 0.015, z), bev=0.006, parent=P)
        wire_panel(P, f"mesh{lv}", mx1 - mx0 - 0.06, h - 0.06, ((mx0 + mx1) / 2, -D / 2 - 0.035, zc), g=0.05)
        for z in (za + 0.08, zb - 0.08):
            K.box("hinge", (0.1, 0.006, 0.025), _iron(), loc=(mx1 - 0.05, -D / 2 - 0.035, z), bev=0, parent=P)
        K.box("latch", (0.025, 0.02, 0.08), _iron(), loc=(mx0 + 0.03, -D / 2 - 0.04, zc), bev=0, parent=P)
    K.box("dark0", (W - 0.5, 0.02, z2 - z1 - 0.06), _dark(), loc=(0.2, D / 2 - 0.02, (z1 + z2) / 2), bev=0, parent=P)
    K.box("dark1", (W - 0.5, 0.02, z3 - z2 - 0.06), _dark(), loc=(0.2, D / 2 - 0.02, (z2 + z3) / 2), bev=0, parent=P)
    # the roof: boards under a tin sheet, overhanging, falling to the back
    ang = math.degrees(math.atan2(0.12, D))
    plank(P, "roof_boards", (W + 0.2, D + 0.25, 0.025), (0, 0.0, z3 + 0.0), rot=(-ang, 0, 0), shade=2)
    corrugated(P, "roof", W + 0.24, D + 0.3, tin(ZINC, metal=0.3, wear=0.15, scratches=0.5), loc=(0, 0.0, z3 + 0.03), rot=(-ang, 0, 0), pitch=0.12)
    K.box("roof_strip", (0.5, D + 0.32, 0.006), tin(RUST, wear=0.2), loc=(-0.35, 0, z3 + 0.055), rot=(-ang, 0, 0), bev=0.001, parent=P)
    K.cyl("bowl", 0.08, 0.04, tin(ZINC, wear=0.2), loc=(0.6, -D / 2 - 0.25, 0.02), segs=10, r2=0.1, bev=0, parent=P)
    return P


# ================================================================== bee skeps
def _skep(P, name, loc, rnd, rot=(0, 0, 0), r=0.22, h=0.36):
    """A coiled straw skep: a dome of rolled straw, its coils standing out, an entrance notch at the foot."""
    G = _grp(P, name, loc, rot)
    n = 21
    coils = 7
    prof = [(r + 0.012, 0.0)]
    for i in range(n + 1):
        t = i / n
        rr = r * math.sqrt(max(1 - t ** 2.2, 0.0)) + 0.012 * math.cos(2 * math.pi * coils * t)
        prof.append((max(rr, 0.0) if i < n else 0.0, h * t))
    turned(G, f"{name}_dome", prof, K.mat("skep", STRAW_D, rough=0.95, grime=0.35, noise_scale=25.0), segs=14)
    K.box(f"{name}_door", (0.08, 0.06, 0.05), _dark(), loc=(0, -r + 0.005, 0.025), bev=0, parent=G)
    return G


def bee_skep(seed=64):
    """A skep bench: a plank shelf on two posts under a small pent roof, three coiled straw skeps on it, one with a
    straw rain hackle, and an old skep fallen in the grass."""
    rnd = random.Random(seed)
    P = _root("skeps")
    hx, zs = 0.8, 0.62
    for s in (-1, 1):
        K.box(f"post{s}", (0.1, 0.1, 1.55), wood(3, wear=0.4), loc=(s * hx, 0.12, 0.775), bev=0.012, parent=P)
        K.box(f"post_f{s}", (0.08, 0.08, zs), wood(3, wear=0.4), loc=(s * hx, -0.22, zs / 2), bev=0.01, parent=P)
        plank(P, f"bearer{s}", (0.08, 0.5, 0.08), (s * hx, -0.05, zs - 0.06), shade=3)
        beam(P, f"rafter{s}", (s * hx, -0.42, 1.48), (s * hx, 0.4, 1.36), 0.06, 0.06, wood(2))
    for k in range(3):
        plank(P, f"shelf{k}", (2 * hx + 0.2, 0.16, 0.04), (0, -0.21 + k * 0.165, zs), shade=k)
    plank(P, "back_rail", (2 * hx + 0.1, 0.05, 0.1), (0, 0.18, 1.4), shade=1)
    for k in range(4):
        plank(P, f"roof{k}", (2 * hx + 0.4, 0.22, 0.025), (0, -0.4 + k * 0.22, 1.505 - k * 0.032), rot=(-8, 0, rnd.uniform(-1, 1)), shade=k)
    K.box("roof_tin", (0.6, 0.6, 0.006), tin(ZINC, wear=0.2), loc=(0.45, -0.07, 1.48), rot=(-8, 0, 3), bev=0.001, parent=P)
    zt = zs + 0.02
    for k, x in enumerate((-0.55, 0.0, 0.55)):
        _skep(P, f"skep{k}", (x, -0.05, zt), rnd, rot=(0, 0, rnd.uniform(-12, 12)), r=0.21 + 0.015 * (k % 2), h=0.34 + 0.03 * (k % 2))
    # a hackle: a straw cap thatched over the middle skep
    _hay_band(P, "hackle", 0.03, zt + 0.52, 0.26, zt + 0.3, _straw(), rnd, segs=14, drop=0.05)
    _skep(P, "skep_fallen", (0.95, -0.55, 0.2), rnd, rot=(80, 0, -60))
    return P


# ================================================================== loose haystack
def _hay_band(P, name, r0, z0, r1, z1, mat, rnd, segs=24, drop=0.12):
    """A ragged band of hay draped round a stack: a frustum from (r0, z0) at the top to (r1, z1), its lower edge cut
    into uneven locks."""
    bm = bmesh.new()
    top, bot = [], []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        rb = r1 * (1 + rnd.uniform(-0.03, 0.05))
        zb = z1 - (drop if i % 2 else 0) * rnd.uniform(0.5, 1.2)
        top.append(bm.verts.new((r0 * math.cos(a), r0 * math.sin(a), z0)))
        bot.append(bm.verts.new((rb * math.cos(a), rb * math.sin(a), zb)))
    for i in range(segs):
        j = (i + 1) % segs
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    ob = K.from_bm(name, bm, mat, smooth=True)
    sol = ob.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = 0.03
    return K.place(ob, parent=P)


def haystack_loose(seed=65):
    """A tall loose haystack built round a centre pole: bellied low and drawn to a point, its hay laid in ragged
    overlapping courses like thatch, a trodden skirt of loose hay round its foot."""
    rnd = random.Random(seed)
    P = _root("haystack")
    hay = K.mat("hay", HAY, rough=0.95, grime=0.2, noise_scale=12.0)
    hay_d = K.mat("hay_d", "#bfa45c", rough=0.95, grime=0.25, noise_scale=16.0)
    H = 3.4
    prof = [(1.3, 0.0), (1.48, 0.25), (1.58, 0.6), (1.56, 0.95), (1.46, 1.3), (1.3, 1.65), (1.08, 2.0), (0.84, 2.35), (0.58, 2.7), (0.32, 3.05),
            (0.12, 3.3), (0.04, H)]
    segs = 22
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    bm = bmesh.new()
    rings = []
    for k, (r, z) in enumerate(prof):
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            q = r * (1 + 0.05 * math.sin(3 * a + ph[0] + k) + 0.03 * math.sin(5 * a + ph[1] - k * 0.7) + rnd.uniform(-0.035, 0.035))
            ring.append(bm.verts.new((q * math.cos(a), q * math.sin(a), z + (rnd.uniform(-0.04, 0.04) if 0 < k < len(prof) - 1 else 0))))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    K.place(K.from_bm("stack", bm, hay, smooth=True), parent=P)
    # courses of hay draped over the stack, each a little proud of the one below, their locks hanging loose
    for k, (r0, z0, r1, z1) in enumerate(((0.36, 3.02, 0.72, 2.6), (0.82, 2.38, 1.14, 1.95), (1.2, 1.75, 1.5, 1.2), (1.52, 1.0, 1.6, 0.5))):
        _hay_band(P, f"course{k}", r0 + 0.02, z0, r1 + 0.04, z1, hay_d if k % 2 else hay, rnd, segs=22 + 2 * k, drop=0.1 + 0.03 * k)
    rod(P, "pole", (0, 0, 3.0), (0.04, -0.03, 4.1), 0.055, wood(3, wear=0.1), segs=8)
    K.disc("skirt", 2.0, _straw_bed(), (0.1, -0.1), z=0.008, segs=20, wobble=0.25, seed=seed).parent = P
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        lump(P, f"loose{k}", 0.3, (math.cos(a) * 1.65, math.sin(a) * 1.65, 0.0), (1.4, 0.9, 0.35), hay_d, rnd, jitter=0.15, flat=0.0, u=8, v=5,
             rot=(0, 0, math.degrees(a) + 90))
    return P


PROPS = {
    "grain_silo_small": grain_silo_small,
    "hay_loft_frame": hay_loft_frame,
    "feed_trough": feed_trough,
    "pig_pen": pig_pen,
    "goat_shed": goat_shed,
    "milk_churns": milk_churns,
    "butter_churn": butter_churn,
    "seed_sacks_cart": seed_sacks_cart,
    "plough_old": plough_old,
    "harrow_rusty": harrow_rusty,
    "scythe_rack": scythe_rack,
    "pitchfork_stack": pitchfork_stack,
    "vegetable_crates": vegetable_crates,
    "pumpkin_pile": pumpkin_pile,
    "corn_crib": corn_crib,
    "irrigation_pump": irrigation_pump,
    "sprinkler_pipe": sprinkler_pipe,
    "cold_frame": cold_frame,
    "greenhouse_small": greenhouse_small,
    "potting_bench": potting_bench,
    "water_butt_stand": water_butt_stand,
    "dovecote": dovecote,
    "rabbit_hutch": rabbit_hutch,
    "bee_skep": bee_skep,
    "haystack_loose": haystack_loose,
}
