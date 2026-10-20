"""Settler Patchwork props, salvage set (docs/world-generation.md, Scenes): what survivors build from the colony's
leftovers, in the Settler Patchwork family of docs/art-direction.md (colony parts reused, mismatched plates on rusty
ribs, plank and sheet metal, rope, and the faded colony colours).

Shelter and defence: a sheet-metal lean-to, a scrap hut, a lookout platform, a timber watchtower, a log palisade and
its gate, a scrap barricade and fences of car doors and of corrugated sheet. Yard things: a tyre stack, a tyre swing,
a painted signboard, a salvaged wind turbine, a solar rig, an antenna pole, a rain catcher, a water still, a generator
shed, a battery rack, a scrap heap, a junk shelf, the anchor posts of a rope bridge, a bucket shower, a latrine screen
and a bell post.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
from mathutils import Vector

import kit as K
import frame as FR
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, GUNMETAL, CONCRETE
from props import _root, wood as _pwood, post, BARK, HEART
from props_settler_a import rod, beam, prism, lump, sack, rope_mat, _orient
from props_settler_b import corrugated as corr, _lash
from props_colony_a import dish_mesh, nut
from props_colony_b import _grp, lathe, plate_xz, rust_patch, tyre

TYRE = "#26282a"
GLASS = "#5f7880"          # old car and lamp glass, grey-blue with dirt
CELL, CELL_L, CRACK = "#1b2a3a", "#3c4e60", "#b9c6cc"   # solar cells, their bus lines, a crazed pane
BATT = "#2a2d31"           # battery cases
SOOT = "#1f1d1b"
WATER = "#3d6f78"
SOIL = "#5a3f2a"
ROCK = "#80827a"
BELL = "#7d6a46"           # old bronze, dulled
TIP = "#b39368"            # sharpened log ends, weathered
LEAF = "#5fae32"
MAT_G = "#6f7a5a"          # bedroll and canvas green
JAR = "#8fa89a"
WORN = "#7d6b52"           # trodden bare earth
CU_OLD = "#8e6a4e"         # tarnished copper


# ---------------------------------------------------------------- materials
def wood(shade=0, wear=0.25):
    """props.wood with less edge wear: on thin boards and round sticks the chipped-edge paint would cover them pale."""
    return _pwood(shade, wear=wear)


def _sheet(col=ZINC, **kw):
    """Sheet metal: wear stays low or the chipped-edge paint turns the whole corrugated surface pale."""
    kw.setdefault("metal", 0.35)
    kw.setdefault("wear", 0.15)
    kw.setdefault("grime", 0.45)
    kw.setdefault("scratches", 0.4)
    return M(col, **kw)


def _paint(col, **kw):
    kw.setdefault("wear", 0.3)
    kw.setdefault("grime", 0.45)
    kw.setdefault("scratches", 0.4)
    return M(col, **kw)


def _bolt(P, loc, rot=(90, 0, 0)):
    """A bolt head through sheet or board (six-sided: a few hundred of them stay cheap)."""
    K.cyl("bolt", 0.024, 0.03, M(ZINC, metal=0.6, wear=0.2), loc=loc, rot=rot, segs=6, bev=0, parent=P)


def _iron():
    return M(GRAPHITE, metal=0.5, wear=0.2, grime=0.4)


def _bark():
    return K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)


def _tip():
    return K.mat("logtip", TIP, rough=0.9, grime=0.35, noise_scale=10.0, cracks=0.2)


def _cut():
    return K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)


def _stone():
    return K.mat("fieldstone", ROCK, rough=0.9, grime=0.3, moss=0.4, cracks=0.3, noise_scale=4.0)


def _rubber():
    return K.mat("tyre", TYRE, rough=0.9, grime=0.4)


def _glass():
    return K.mat("oldglass", GLASS, rough=0.15, metal=0.2, grime=0.3)


def _concrete():
    return K.mat("concrete", CONCRETE, rough=0.9, wear=0.4, grime=0.5, cracks=0.2, moss=0.15)


def _copper():
    """Copper gone brown with years of smoke: COPPER itself reads orange in sunlight."""
    return K.mat("copper_still", CU_OLD, rough=0.45, metal=0.75, wear=0.2, grime=0.5)


# ---------------------------------------------------------------- shapes
def _log(P, name, a, b, r, segs=8):
    """A barked pole from a to b."""
    return rod(P, name, a, b, r, _bark(), segs=segs)


def _logpost(P, name, x, y, h, r=0.09, lean=(0, 0), segs=8, top=True):
    """A barked post sunk into the ground, its sawn top pale."""
    _log(P, name, (x, y, -0.15), (x + lean[0], y + lean[1], h), r, segs)
    if top:
        K.cyl(f"{name}_top", r * 0.88, 0.012, _cut(), loc=(x + lean[0], y + lean[1], h + 0.002), segs=segs, bev=0, parent=P)


def _stake(P, name, x, y, h, r, lean=(0, 0), segs=8, z0=-0.2):
    """A log sharpened to a point: barked shaft, a pale axe-cut cone."""
    base = Vector((x, y, z0))
    top = Vector((x + lean[0], y + lean[1], h))
    d = (top - base).normalized()
    mid = top - d * r * 2.4
    rod(P, name, base, mid, r, _bark(), segs=segs)
    tip = K.cyl(f"{name}_tip", r, r * 2.4, _tip(), segs=segs, bev=0, parent=P, r2=r * 0.12)
    _orient(tip, mid, top)


def _line(P, name, pts, r, mat, segs=6):
    """A rope, cable or wire through points."""
    for k in range(len(pts) - 1):
        rod(P, f"{name}{k}", pts[k], pts[k + 1], r, mat, segs=segs)


def _sag(a, b, sag, n=6):
    """Points along a rope from a to b hanging sag metres at its middle."""
    a, b = Vector(a), Vector(b)
    return [a + (b - a) * (i / n) - Vector((0, 0, sag * 4 * (i / n) * (1 - i / n))) for i in range(n + 1)]


def _bar(P, name, a, b, w, mat, d=None, bev=0.0):
    """A square bar from a to b (frames, straps)."""
    a, b = Vector(a), Vector(b)
    ob = K.box(name, (w, d or w, (b - a).length), mat, loc=(a + b) / 2, bev=bev, parent=P)
    ob.rotation_euler = Vector((0, 0, 1)).rotation_difference(b - a).to_euler()
    return ob


def tyre_ring(P, name, loc, rot=(0, 0, 0), r=0.32, w=0.2, segs=18):
    """A car tyre off its rim, its axis along local z: a squared-off ring with rounded shoulders."""
    ri = r * 0.6
    prof = [(ri, -w * 0.32), (ri + 0.03, -w / 2), (r - 0.035, -w / 2), (r, -w * 0.3),
            (r, w * 0.3), (r - 0.035, w / 2), (ri + 0.03, w / 2), (ri, w * 0.32)]
    return lathe(name, prof, _rubber(), segs=segs, loc=loc, rot=rot, parent=P)


def car_door(P, name, loc, rot, col, glass=0, frame=True):
    """A salvaged car door, outer skin towards -y, hinge edge at x = 0 and foot at z = 0: a 1.08 m skin with a swage
    line, a handle and rust along the sill, the window frame above (glass 0 none, 1 whole, 2 a shard left in it)."""
    G = _grp(P, name, loc, rot)
    paint = M(col, metal=0.35, wear=0.3, grime=0.45, scratches=0.5)
    pts = [(0.0, 0.0), (1.0, 0.0), (1.06, 0.1), (1.08, 0.4), (1.04, 0.66), (0.0, 0.66)]
    plate_xz(f"{name}_skin", pts, 0.07, paint, parent=G, bev=0.012)
    K.box(f"{name}_trim", (0.94, 0.02, 0.5), M(GRAPHITE, wear=0.1, rough=0.85), loc=(0.5, 0.04, 0.36), bev=0, parent=G)
    K.box(f"{name}_swage", (1.0, 0.012, 0.025), paint, loc=(0.52, -0.038, 0.46), bev=0, parent=G)
    K.box(f"{name}_handle", (0.15, 0.03, 0.035), _iron(), loc=(0.2, -0.05, 0.55), bev=0.006, parent=G)
    rust_patch(G, (0.62, -0.037, 0.1), (0.34, 0.004, 0.1))
    if frame:
        fm = M(GRAPHITE, metal=0.4, wear=0.15)
        _bar(G, f"{name}_bpost", (0.04, 0, 0.64), (0.04, 0, 1.08), 0.05, fm)
        _bar(G, f"{name}_top", (0.04, 0, 1.08), (0.68, 0, 1.08), 0.05, fm)
        _bar(G, f"{name}_apost", (0.68, 0, 1.08), (1.02, 0, 0.64), 0.05, fm)
        if glass == 1:
            plate_xz(f"{name}_glass", [(0.07, 0.66), (0.99, 0.66), (0.67, 1.05), (0.07, 1.05)], 0.008, _glass(), parent=G, bev=0)
        elif glass == 2:
            plate_xz(f"{name}_shard", [(0.07, 0.66), (0.62, 0.66), (0.3, 0.84), (0.07, 0.97)], 0.008, _glass(), parent=G, bev=0)
    return G


def _tri_slab(P, name, a, b, c, t, mat):
    """A triangular plate of thickness t under the face a, b, c (a roof facet)."""
    a, b, c = Vector(a), Vector(b), Vector(c)
    n = (b - a).cross(c - a).normalized()
    bm = bmesh.new()
    top = [bm.verts.new(v) for v in (a, b, c)]
    bot = [bm.verts.new(v - n * t) for v in (a, b, c)]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((top[j], top[i], bot[i], bot[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat), parent=P)


def _surface(P, name, fn, x0, x1, y0, y1, nx, ny, t, mat):
    """A cloth or tarp sheet over a grid, its height fn(x, y), t thick."""
    bm = bmesh.new()
    top, bot = [], []
    for i in range(nx + 1):
        rt, rb = [], []
        for j in range(ny + 1):
            x, y = x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny
            z = fn(x, y)
            rt.append(bm.verts.new((x, y, z)))
            rb.append(bm.verts.new((x, y, z - t)))
        top.append(rt)
        bot.append(rb)
    for i in range(nx):
        for j in range(ny):
            bm.faces.new((top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]))
            bm.faces.new((bot[i][j], bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j]))
    edge = [(i, 0) for i in range(nx)] + [(nx, j) for j in range(ny)] + [(i, ny) for i in range(nx, 0, -1)] + [(0, j) for j in range(ny, 0, -1)]
    for k in range(len(edge)):
        (i0, j0), (i1, j1) = edge[k], edge[(k + 1) % len(edge)]
        bm.faces.new((top[i0][j0], bot[i0][j0], bot[i1][j1], top[i1][j1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat, smooth=True), parent=P)


def _bay(P, name, kind, c, yaw, w, h, col, rnd, z0=0.14, pitch=0.15):
    """One bay of a patchwork wall, built facing -y with its wall line at y = 0 and turned by yaw: a corrugated sheet,
    a run of mismatched boards or a flat colony plate in rails."""
    G = _grp(P, name, (c[0], c[1], 0), (0, 0, yaw))
    zc = z0 + h / 2
    if kind == "sheet":
        corr(G, f"{name}_sheet", w + 0.06, h, _sheet(col), loc=(0, -0.03, zc), rot=(90, rnd.uniform(-0.6, 0.6), 0), pitch=pitch)
        for x, z in ((-w / 2 + 0.1, z0 + 0.3), (w / 2 - 0.1, z0 + h - 0.3)):
            _bolt(G, (x, -0.055, z))
    elif kind == "planks":
        n = max(3, round(w / 0.2))
        bw = w / n
        for k in range(n):
            hh = h - rnd.uniform(0, 0.08)
            m = _paint(rnd.choice((SEA, BRICK, BONE)), rough=0.85) if rnd.random() < 0.18 else wood(rnd.randrange(4))
            K.box(f"{name}_b{k}", (bw - 0.012, 0.025, hh), m, loc=(-w / 2 + bw * (k + 0.5), -0.02, z0 + hh / 2), bev=0, parent=G)
        for z in (z0 + 0.35, z0 + h - 0.35):
            K.box(f"{name}_batten", (w - 0.04, 0.025, 0.1), wood(3), loc=(0, -0.045, z), rot=(0, rnd.uniform(-1.5, 1.5), 0), bev=0.006, parent=G)
    else:
        paint = _paint(col)
        rail = _paint(GRAPHITE if col != GRAPHITE else STEEL, metal=0.3, wear=0.2)
        K.box(f"{name}_plate", (w - 0.02, 0.03, h - 0.02), paint, loc=(0, -0.03, zc), bev=0.01, parent=G)
        for s in (-1, 1):
            K.box(f"{name}_rh", (w, 0.04, 0.07), rail, loc=(0, -0.055, zc + s * (h / 2 - 0.035)), bev=0, parent=G)
            K.box(f"{name}_rv", (0.07, 0.04, h - 0.1), rail, loc=(s * (w / 2 - 0.035), -0.055, zc), bev=0, parent=G)
            for z in (z0 + 0.25, z0 + h - 0.25):
                nut(G, (s * (w / 2 - 0.035), -0.08, z), r=0.02, rot=(90, 0, 0))
        rust_patch(G, (rnd.uniform(-0.2, 0.2), -0.047, z0 + 0.3), (0.32, 0.004, 0.2))
    return G


def _grass(P, name, loc, n, rnd, h=0.3):
    """A tuft of grass blades."""
    x, y, z = loc
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.4, 0.4)
        t = rnd.uniform(20, 48)
        hh = h * rnd.uniform(0.6, 1.2)
        d = 0.05 + math.sin(math.radians(t)) * hh / 2
        K.cyl(f"{name}{k}", 0.012, hh, M(rnd.choice((LEAF, "#6fbf3a", "#4f9a2a")), wear=0, grime=0, scratches=0, rough=0.8),
              loc=(x + d * math.cos(a), y + d * math.sin(a), z + math.cos(math.radians(t)) * hh / 2 - 0.01),
              rot=(-t * math.sin(a), t * math.cos(a), 0), segs=3, bev=0, parent=P, r2=0.001)


def _jerrycan(P, name, loc, yaw, col):
    G = _grp(P, name, loc, (0, 0, yaw))
    K.box(f"{name}_body", (0.17, 0.35, 0.45), _paint(col, metal=0.3), loc=(0, 0, 0.225), bev=0.02, parent=G)
    K.box(f"{name}_x", (0.175, 0.2, 0.012), _paint(col, metal=0.3), loc=(0, 0.02, 0.24), rot=(40, 0, 0), bev=0, parent=G)
    for y in (-0.06, 0.02, 0.1):
        K.box(f"{name}_h", (0.025, 0.025, 0.07), _paint(col, metal=0.3), loc=(0, y, 0.48), bev=0.004, parent=G)
    K.box(f"{name}_grip", (0.025, 0.2, 0.025), _paint(col, metal=0.3), loc=(0, 0.02, 0.515), bev=0.004, parent=G)
    K.cyl(f"{name}_cap", 0.03, 0.05, _iron(), loc=(0, -0.13, 0.47), rot=(-30, 0, 0), segs=8, bev=0, parent=G)
    return G


def _bucket(P, name, loc, col=ZINC, r=0.15, h=0.28):
    x, y, z = loc
    K.cyl(f"{name}_body", r, h, _sheet(col, wear=0.2), loc=(x, y, z + h / 2), segs=14, r2=r * 0.82, bev=0.008, parent=P)
    K.cyl(f"{name}_in", r * 0.92, 0.01, M(GRAPHITE, wear=0, rough=0.9), loc=(x, y, z + h + 0.001), segs=14, bev=0, parent=P)
    K.torus(f"{name}_bail", r * 0.98, 0.006, _iron(), loc=(x, y, z + h), rot=(0, 90, 0), arc=180, segs=12, parent=P)


# ================================================================== lean-to
def lean_to_metal(seed=41):
    """A lean-to of round poles under three odd corrugated sheets falling to the back, two sheets walling its windward
    end and boards along the back, stones on the roof to hold it down, a bedroll, a crate and a jerrycan in its shade."""
    rnd = random.Random(seed)
    P = _root("leanto")
    yf, yb, zf, zb = -0.95, 0.95, 2.25, 1.5

    def roof(y):
        return zf + (zb - zf) * (y - yf) / (yb - yf)

    ang = math.degrees(math.atan2(zf - zb, yb - yf))
    for k, x in enumerate((-1.45, 0.05, 1.45)):
        _logpost(P, f"front{k}", x, yf, roof(yf) - 0.12, r=0.07, lean=(rnd.uniform(-0.04, 0.04), 0), top=False)
    for k, x in enumerate((-1.45, 1.45)):
        _logpost(P, f"back{k}", x, yb, roof(yb) - 0.12, r=0.065, top=False)
    for nm, y in (("beam_f", yf), ("beam_b", yb)):
        _log(P, nm, (-1.7, y, roof(y) - 0.1), (1.7, y + 0.02, roof(y) - 0.1), 0.065)
    L = 2.5 / math.cos(math.radians(ang))
    for k, x in enumerate((-1.25, 0.0, 1.25)):
        K.box(f"rafter{k}", (0.06, L, 0.1), wood(3), loc=(x, 0, roof(0) + 0.015), rot=(-ang, 0, 0), bev=0.01, parent=P)
    for k, (x, col) in enumerate(((-1.0, ZINC), (0.02, RUST), (1.04, SEA))):
        corr(P, f"roof{k}", 1.1, L + 0.1, _sheet(col), loc=(x, 0, roof(0) + 0.085 + k * 0.012), rot=(-ang, rnd.uniform(-0.8, 0.8), 0))
        for y in (-0.95, 0.0, 0.95):
            _bolt(P, (x + 0.0 * k, y, roof(y) + 0.11 + k * 0.012), rot=(-ang, 0, 0))
    for k, (x, y, r) in enumerate(((-1.3, -0.6, 0.13), (-0.2, 0.75, 0.16), (1.1, 0.2, 0.12))):
        lump(P, f"weight{k}", r, (x, y, roof(y) + 0.1 + r * 0.25), (1.25, 1.0, 0.7), _stone(), rnd, flat=0.45, rot=(-ang, 0, 0), u=8, v=6)
    # the windward end: two sheets on rails
    for z in (0.45, 1.2):
        _log(P, f"siderail{z}", (1.45, yf, z), (1.45, yb, z), 0.05)
    corr(P, "side0", 0.98, 1.45, _sheet(BRICK), loc=(1.53, -0.47, 0.72), rot=(90, 0.8, 90))
    corr(P, "side1", 0.98, 1.38, _sheet(ZINC), loc=(1.545, 0.45, 0.69), rot=(90, -0.5, 90))
    for y in (-0.8, -0.15, 0.15, 0.8):
        for z in (0.45, 1.2):
            _bolt(P, (1.57, y, z), rot=(0, 90, 0))
    # boards along the back
    for k, z in enumerate((0.3, 0.62, 0.94)):
        K.box(f"backboard{k}", (3.0, 0.025, 0.24), wood(k), loc=(rnd.uniform(-0.05, 0.05), yb + 0.08, z), rot=(0, rnd.uniform(-1.2, 1.2), 0),
              bev=0.006, parent=P)
    # what lives under it
    K.box("bedroll", (0.68, 1.55, 0.11), M(MAT_G, rough=0.95, grime=0.4), loc=(0.45, 0.05, 0.055), rot=(0, 0, 4), bev=0.045, parent=P)
    K.cyl("pillow", 0.1, 0.62, M(MAT_G, rough=0.95, grime=0.4), loc=(0.45, 0.72, 0.12), rot=(0, 90, 4), segs=10, bev=0.03, parent=P)
    K.box("crate", (0.6, 0.5, 0.45), wood(1, wear=0.6), loc=(-0.85, 0.35, 0.225), rot=(0, 0, -8), bev=0.02, parent=P)
    for z in (0.12, 0.33):
        K.box("crate_slat", (0.62, 0.52, 0.03), wood(3), loc=(-0.85, 0.35, z), rot=(0, 0, -8), bev=0.005, parent=P)
    _jerrycan(P, "can", (1.1, 0.55, 0), 15, BRICK)
    return P


# ================================================================== scrap hut
def scrap_hut(seed=42):
    """A 3 x 3 m hut on concrete blocks: a timber frame walled bay by bay in whatever came to hand (corrugated sheets,
    boards, a flat colony panel with a window), a colony hatch hung for a door, a mono-pitch roof of three odd sheets
    weighed down with stones and a tyre, and a stove pipe up the side under a rain cap."""
    rnd = random.Random(seed)
    P = _root("hut")
    hw = 1.5
    zf, zb = 2.5, 2.15

    def roof(y):
        return zf + (zb - zf) * (y + hw) / (2 * hw)

    ang = math.degrees(math.atan2(zf - zb, 2 * hw))
    for x in (-1.38, 1.38):
        for y in (-1.38, 0, 1.38):
            K.box("block", (0.3, 0.3, 0.18), _concrete(), loc=(x, y, 0.07), rot=(0, 0, rnd.uniform(-6, 6)), bev=0, parent=P)
    K.box("floor", (2.9, 2.9, 0.04), wood(3, wear=0.3), loc=(0, 0, 0.17), bev=0, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            zt = roof(sy * 1.42) - 0.12
            K.box("post", (0.12, 0.12, zt - 0.14), wood(3), loc=(sx * 1.42, sy * 1.42, 0.14 + (zt - 0.14) / 2), bev=0.012, parent=P)
    for sy in (-1, 1):
        K.box("plate", (3.0, 0.12, 0.12), wood(2), loc=(0, sy * 1.42, roof(sy * 1.42) - 0.06), bev=0.012, parent=P)
    for sx in (-1, 1):
        K.box("plate_side", (0.12, 3.0 / math.cos(math.radians(ang)), 0.12), wood(2), loc=(sx * 1.42, 0, roof(0) - 0.06), rot=(-ang, 0, 0),
              bev=0.012, parent=P)
    # walls, bay by bay (front wall: boards, the door, a sheet)
    hf = roof(-1.48) - 0.12 - 0.14
    _bay(P, "fl", "planks", (-1.0, -1.48), 0, 0.95, hf, None, rnd, pitch=0.2)
    _bay(P, "fr", "sheet", (1.0, -1.48), 0, 0.95, hf, RUST, rnd, pitch=0.2)
    hs = zb - 0.14 - 0.14
    win = _bay(P, "lm", "plate", (-1.48, 0.0), -90, 1.0, hs, BONE, rnd, pitch=0.2)
    _bay(P, "lf", "sheet", (-1.48, -1.0), -90, 0.95, hs, ZINC, rnd, pitch=0.2)
    _bay(P, "lb", "planks", (-1.48, 1.0), -90, 0.95, hs, None, rnd, pitch=0.2)
    _bay(P, "rf", "plate", (1.48, -1.0), 90, 0.95, hs, STEEL, rnd, pitch=0.2)
    _bay(P, "rm", "sheet", (1.48, 0.0), 90, 1.0, hs, SEA, rnd, pitch=0.2)
    _bay(P, "rb", "sheet", (1.48, 1.0), 90, 0.95, hs, ZINC, rnd, pitch=0.2)
    hb = roof(1.48) - 0.12 - 0.14
    _bay(P, "bl", "planks", (-0.75, 1.48), 180, 1.45, hb, None, rnd, pitch=0.2)
    _bay(P, "br", "sheet", (0.75, 1.48), 180, 1.45, hb, BRICK, rnd, pitch=0.2)
    # the gable strips above the side bays
    for sx in (-1, 1):
        for k, (y0, y1) in enumerate(((-1.5, 0.0), (0.0, 1.5))):
            prism(P, f"gable{sx}{k}", [(y0, zb - 0.16), (y1, zb - 0.16), (y1, roof(y1) - 0.02), (y0, roof(y0) - 0.02)], 0.025, wood(k + 1, wear=0.5),
                  loc=(sx * 1.52, 0, 0), plane="yz", bev=0)
    # the window in the colony panel: frame, cross bars, dirty glass
    K.box("win_glass", (0.55, 0.02, 0.45), _glass(), loc=(0, -0.06, 1.3), bev=0, parent=win)
    for s in (-1, 1):
        K.box("win_fh", (0.65, 0.04, 0.06), wood(3), loc=(0, -0.08, 1.3 + s * 0.255), bev=0.008, parent=win)
        K.box("win_fv", (0.06, 0.04, 0.45), wood(3), loc=(s * 0.3, -0.08, 1.3), bev=0.008, parent=win)
    K.box("win_bar", (0.03, 0.03, 0.45), wood(3), loc=(0, -0.08, 1.3), bev=0, parent=win)
    K.box("win_bar2", (0.55, 0.03, 0.03), wood(3), loc=(0, -0.08, 1.3), bev=0, parent=win)
    # the doorway and a colony hatch hung as the door, standing ajar
    for s in (-1, 1):
        K.box("jamb", (0.1, 0.1, 2.06), wood(3), loc=(s * 0.5, -1.45, 0.16 + 1.03), bev=0.01, parent=P)
    K.box("header", (1.1, 0.1, 0.12), wood(3), loc=(0, -1.45, 2.27), bev=0.01, parent=P)
    K.box("over_door", (0.98, 0.025, roof(-1.48) - 0.12 - 2.33), wood(1), loc=(0, -1.5, (2.33 + roof(-1.48) - 0.12) / 2), bev=0.006, parent=P)
    D = _grp(P, "door", (-0.45, -1.53, 0.17), (0, 0, -24))
    K.box("hatch", (0.88, 0.05, 1.98), _paint(SEA, metal=0.3), loc=(0.44, 0, 0.99), bev=0.015, parent=D)
    for s in (-1, 1):
        K.box("hatch_rh", (0.9, 0.06, 0.07), _paint(BONE, metal=0.3), loc=(0.44, -0.01, 0.99 + s * 0.95), bev=0.01, parent=D)
        K.box("hatch_rv", (0.07, 0.06, 1.9), _paint(BONE, metal=0.3), loc=(0.44 + s * 0.415, -0.01, 0.99), bev=0.01, parent=D)
    K.cyl("port", 0.14, 0.06, _glass(), loc=(0.44, -0.01, 1.45), rot=(90, 0, 0), segs=12, bev=0, parent=D)
    K.torus("port_ring", 0.15, 0.025, _paint(BONE, metal=0.3), loc=(0.44, -0.04, 1.45), rot=(90, 0, 0), segs=14, parent=D)
    K.box("hatch_bar", (0.035, 0.05, 0.3), _iron(), loc=(0.78, -0.06, 1.0), bev=0.006, parent=D)
    for z in (0.35, 1.65):
        K.box("hinge", (0.22, 0.012, 0.05), _iron(), loc=(0.08, -0.03, z), bev=0, parent=D)
    K.box("step", (0.9, 0.42, 0.16), _concrete(), loc=(0.05, -1.82, 0.06), rot=(0, 0, 3), bev=0.02, parent=P)
    # a bulb under a tin shade over the door
    K.box("lamp_arm", (0.03, 0.32, 0.03), _iron(), loc=(0, -1.66, 2.42), bev=0, parent=P)
    K.cyl("lamp_shade", 0.13, 0.08, _sheet(ZINC), loc=(0, -1.8, 2.38), segs=12, r2=0.04, bev=0, parent=P)
    K.cyl("lamp_bulb", 0.04, 0.07, glow("#ffe9a8", 6), loc=(0, -1.8, 2.31), segs=8, bev=0, parent=P)
    # the roof: rafters and three sheets that do not match, falling to the back
    L = 3.5 / math.cos(math.radians(ang))
    for x in (-1.2, 0.0, 1.2):
        K.box("rafter", (0.08, L - 0.2, 0.1), wood(3), loc=(x, 0, roof(0) + 0.05), rot=(-ang, 0, 0), bev=0.01, parent=P)
    for k, (x, col) in enumerate(((-1.12, ZINC), (0.0, RUST), (1.12, STEEL))):
        corr(P, f"roof{k}", 1.22, L, _sheet(col), loc=(x, 0, roof(0) + 0.12 + (k % 2) * 0.012), rot=(-ang, rnd.uniform(-0.6, 0.6), 0), pitch=0.2)
        for y in (-1.3, 1.3):
            _bolt(P, (x, y, roof(y) + 0.14 + (k % 2) * 0.012), rot=(-ang, 0, 0))
    corr(P, "roof_patch", 0.6, 0.7, _sheet(SEA), loc=(-0.55, -0.6, roof(-0.6) + 0.16), rot=(-ang, 6, 0))
    for k, (x, y, r) in enumerate(((-1.1, 0.7, 0.14), (0.55, -0.9, 0.12))):
        lump(P, f"weight{k}", r, (x, y, roof(y) + 0.15 + r * 0.25), (1.2, 1.0, 0.7), _stone(), rnd, flat=0.45, rot=(-ang, 0, 0), u=8, v=6)
    tyre_ring(P, "roof_tyre", (0.75, 0.6, roof(0.6) + 0.24), rot=(-ang, 0, 0), r=0.3, w=0.18, segs=12)
    # the stove pipe up the right side
    pipe = M(GRAPHITE, metal=0.5, wear=0.15, grime=0.5)
    px, py = 1.85, 0.75
    rod(P, "flue_in", (1.45, py, 1.0), (px + 0.075, py, 1.0), 0.075, pipe, segs=10)
    rod(P, "flue", (px, py, 0.93), (px, py, 3.15), 0.075, pipe, segs=10)
    K.cyl("flue_cap", 0.17, 0.1, pipe, loc=(px, py, 3.3), segs=12, r2=0.03, bev=0, parent=P)
    for k in range(3):
        a = 2 * math.pi * k / 3
        rod(P, f"flue_leg{k}", (px + 0.06 * math.cos(a), py + 0.06 * math.sin(a), 3.13), (px + 0.1 * math.cos(a), py + 0.1 * math.sin(a), 3.26), 0.008, pipe, segs=4)
    for z in (1.7, 2.45):
        K.box("flue_strap", (0.4, 0.04, 0.04), _iron(), loc=(1.68, py, z), bev=0, parent=P)
    K.box("soot", (0.01, 0.26, 0.5), K.mat("soot", SOOT, rough=1.0), loc=(1.535, py, 1.25), bev=0, parent=P)
    return P


# ================================================================== lookout platform
def lookout_platform(seed=43):
    """A plank deck on four poles 2.75 m up, X-braced on three sides, a rail round it with a sheet screen at the front,
    a ladder up to a gap in the front rail, a crate and a sack on top and a hauling rope with its bucket down the side."""
    rnd = random.Random(seed)
    P = _root("lookout")
    a, zd, zr = 0.95, 2.75, 3.8
    for sx in (-1, 1):
        for sy in (-1, 1):
            _logpost(P, f"pole{sx}{sy}", sx * a, sy * a, zr + 0.06, r=0.09)
    for sx in (-1, 1):
        _log(P, f"bearer{sx}", (sx * (a + 0.13), -1.2, zd - 0.12), (sx * (a + 0.13), 1.2, zd - 0.12), 0.075)
    for k in range(10):
        y = -1.08 + k * 0.24
        K.box(f"deck{k}", (2.5 + rnd.uniform(-0.06, 0.06), 0.22, 0.04), wood(rnd.randrange(4)), loc=(rnd.uniform(-0.03, 0.03), y, zd - 0.025),
              bev=0.006, parent=P)
    # rails: sides and back whole, the front open over the ladder
    o = a + 0.11
    for sx in (-1, 1):
        for z in (zd + 0.5, zr - 0.05):
            K.box("rail_s", (0.05, 2.15, 0.1), wood(2), loc=(sx * o, 0, z), bev=0.01, parent=P)
    for z in (zd + 0.5, zr - 0.05):
        K.box("rail_b", (2.15, 0.05, 0.1), wood(2), loc=(0, o, z), bev=0.01, parent=P)
        K.box("rail_f", (1.25, 0.05, 0.1), wood(2), loc=(-0.45, -o, z), bev=0.01, parent=P)
    corr(P, "screen", 1.15, 0.85, _sheet(RUST), loc=(-0.45, -o - 0.045, zd + 0.48), rot=(90, 1.0, 0))
    for x in (-0.95, 0.05):
        for z in (zd + 0.2, zd + 0.75):
            _bolt(P, (x + 0.05, -o - 0.075, z))
    # X-braces on three sides
    for k in range(2):
        za, zb = (0.35, 1.55)[k], (1.55, zd - 0.2)[k]
        for sx in (-1, 1):
            beam(P, "brace_s", (sx * o, -a, za), (sx * o, a, zb), 0.035, 0.12, wood(1))
            beam(P, "brace_s2", (sx * o, a, za), (sx * o, -a, zb), 0.035, 0.12, wood(2))
        beam(P, "brace_b", (-a, o, za), (a, o, zb), 0.035, 0.12, wood(1))
        beam(P, "brace_b2", (a, o, za), (-a, o, zb), 0.035, 0.12, wood(2))
    # the ladder
    lx, y0, y1 = 0.45, -a - 0.8, -a - 0.13

    def lpt(s, t, ext=0.0):
        z = t * (zd + 0.95)
        return (lx + s * 0.22, y0 + (y1 - y0) * t, z)

    for s in (-1, 1):
        beam(P, f"lrail{s}", lpt(s, 0), lpt(s, 1), 0.05, 0.08, wood(3))
    n = 9
    for k in range(1, n + 1):
        t = k * 0.3 / (zd + 0.95)
        rod(P, f"rung{k}", lpt(-1, t), lpt(1, t), 0.022, wood(2), segs=6)
    # on the deck
    K.box("crate", (0.55, 0.45, 0.42), wood(1, wear=0.6), loc=(-0.45, 0.45, zd + 0.21), rot=(0, 0, 10), bev=0.02, parent=P)
    sack(P, "sack", (0.7, 0.42, 0.3), (0.35, 0.5, zd), (0, 0, -70), M("#c2a878", rough=0.95, grime=0.4), rnd, tied=False)
    # the hauling rope and its bucket
    _line(P, "haul", [(o + 0.04, -0.2, zr - 0.05), (o + 0.07, -0.2, 1.62)], 0.012, rope_mat())
    _bucket(P, "bucket", (o + 0.07, -0.2, 1.3), col=BRICK, r=0.14, h=0.26)
    return P


# ================================================================== watchtower
def watchtower_wood(seed=44):
    """An 8 m timber watchtower: four splayed log legs X-braced in two tiers, a plank platform at 5.2 m behind half
    walls of sheet and board, corner posts carrying a pyramid roof of four mismatched sheets and a red rag flying from
    its peak, a ladder up the inside to a hatch."""
    rnd = random.Random(seed)
    P = _root("watchtower")
    b0, b1, zp, ze, za = 1.6, 1.15, 5.2, 7.15, 8.25

    def at(sx, sy, z, out=0.0):
        f = z / zp
        b = b0 + (b1 - b0) * f + out
        return (sx * b, sy * b, z)

    for sx in (-1, 1):
        for sy in (-1, 1):
            _log(P, f"leg{sx}{sy}", (sx * b0 * 1.008, sy * b0 * 1.008, -0.2), at(sx, sy, zp + 0.05), 0.13, segs=10)
    faces = (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1)))
    for k, ((ax, ay), (bx, by)) in enumerate(faces):
        nx, ny = (ax + bx) / 2, (ay + by) / 2

        def pt(sx, sy, z):
            x, y, z = at(sx, sy, z)
            return (x + nx * 0.14, y + ny * 0.14, z)

        for z in (0.5, 2.75):
            beam(P, f"girt{k}", pt(ax, ay, z), pt(bx, by, z), 0.04, 0.16, wood(k % 4))
        for za_, zb_ in ((0.5, 2.75), (2.75, zp - 0.25)):
            beam(P, f"brace{k}", pt(ax, ay, za_), pt(bx, by, zb_), 0.04, 0.14, wood((k + 1) % 4))
            beam(P, f"brace{k}b", pt(bx, by, za_), pt(ax, ay, zb_), 0.04, 0.14, wood((k + 2) % 4))
    # platform: bearers and boards, a hatch left open over the ladder
    for s in (-1, 1):
        _log(P, f"bearer{s}", (-1.55, s * b1, zp - 0.1), (1.55, s * b1, zp - 0.1), 0.1)
    for k in range(8):
        y = -1.3 + k * 0.371
        if k == 1:
            K.box("deck_l", (1.73, 0.36, 0.05), wood(k % 4), loc=(-0.615, y, zp + 0.025), bev=0.008, parent=P)
            K.box("deck_r", (0.63, 0.36, 0.05), wood(k % 4), loc=(1.165, y, zp + 0.025), bev=0.008, parent=P)
            continue
        K.box(f"deck{k}", (2.96, 0.36, 0.05), wood(k % 4), loc=(0, y, zp + 0.025), bev=0.008, parent=P)
    # corner posts to the roof and plates
    c = 1.38
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("cpost", (0.14, 0.14, ze - zp), wood(3), loc=(sx * c, sy * c, (zp + ze) / 2), bev=0.012, parent=P)
    for s in (-1, 1):
        K.box("eplate", (2.92, 0.14, 0.14), wood(2), loc=(0, s * c, ze - 0.07), bev=0.012, parent=P)
        K.box("eplate_s", (0.14, 2.92, 0.14), wood(2), loc=(s * c, 0, ze - 0.07), bev=0.012, parent=P)
    # half walls: sheet on the front and right, boards on the back and left, capped
    hz = zp + 0.06 + 0.55
    corr(P, "wall_f", 2.9, 1.1, _sheet(RUST), loc=(0, -c - 0.09, hz), rot=(90, 0, 0), pitch=0.2)
    corr(P, "wall_r", 2.9, 1.1, _sheet(SEA), loc=(c + 0.09, 0, hz), rot=(90, 0, 90), pitch=0.2)
    for k, z in enumerate((zp + 0.25, zp + 0.6, zp + 0.95)):
        K.box("wall_b", (2.9, 0.03, 0.3), wood(k), loc=(0, c + 0.085, z), rot=(0, rnd.uniform(-1, 1), 0), bev=0.006, parent=P)
        K.box("wall_l", (0.03, 2.9, 0.3), wood(k + 1), loc=(-c - 0.085, 0, z), rot=(rnd.uniform(-1, 1), 0, 0), bev=0.006, parent=P)
    for s in (-1, 1):
        K.box("cap", (3.0, 0.16, 0.05), wood(2), loc=(0, s * (c + 0.03), zp + 1.2), bev=0.008, parent=P)
        K.box("cap_s", (0.16, 3.0, 0.05), wood(2), loc=(s * (c + 0.03), 0, zp + 1.2), bev=0.008, parent=P)
    for x in (-1.1, -0.35, 0.35, 1.1):
        _bolt(P, (x, -c - 0.13, zp + 0.35))
        _bolt(P, (x, -c - 0.13, zp + 0.95))
    # the pyramid roof: four facets of different sheet, hip battens, a rag on a pole at the peak
    e = 1.75
    corners = [(-e, -e, ze), (e, -e, ze), (e, e, ze), (-e, e, ze)]
    apex = (0, 0, za)
    for k, col in enumerate((RUST, SEA, ZINC, BRICK)):
        _tri_slab(P, f"roof{k}", corners[k], corners[(k + 1) % 4], apex, 0.05, _sheet(col, wear=0.2))
        a_, b_ = Vector(corners[k]), Vector(corners[(k + 1) % 4])
        n = ((a_ + b_) / 2 - Vector((0, 0, ze))).normalized()
        nz = Vector((n.x * 0.42, n.y * 0.42, 0.92)).normalized() * 0.03
        for f in (0.33, 0.66):
            p = a_ + (Vector(apex) - a_) * f + nz
            q = b_ + (Vector(apex) - b_) * f + nz
            beam(P, f"batten{k}", p, q, 0.025, 0.08, wood(3), bev=0)
    for k in range(4):
        cx, cy, cz = corners[k]
        rod(P, f"hip{k}", (cx * 1.02, cy * 1.02, cz + 0.02), (0, 0, za + 0.03), 0.05, wood(3), segs=6)
    rod(P, "flagpole", (0, 0, za - 0.3), (0, 0, za + 1.3), 0.03, wood(3), segs=6)
    flag = _grp(P, "flag", (0.03, 0, za + 1.05), (0, 0, 12))
    corr(flag, "rag", 0.7, 0.36, M(SIGNAL, wear=0.2, rough=0.9, grime=0.3), loc=(0.35, 0, 0), rot=(90, 90, 0), pitch=0.35, amp=0.04, t=0.01)
    # the ladder up the inside, through the hatch
    lx, ly0, ly1 = 0.55, -0.95, -0.62

    def lp(s, t):
        return (lx + s * 0.23, ly0 + (ly1 - ly0) * t, -0.05 + t * (zp + 1.0))

    for s in (-1, 1):
        beam(P, f"lrail{s}", lp(s, 0), lp(s, 1), 0.05, 0.08, wood(3))
    for k in range(1, 18):
        t = k * 0.3 / (zp + 1.0)
        rod(P, f"rung{k}", lp(-1, t), lp(1, t), 0.022, wood(2), segs=6)
    return P


# ================================================================== palisade
def _berm(P, name, x0, x1, rnd, n=7):
    """Earth heaped along the foot of a wall: a row of low uneven mounds on both faces."""
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    for k in range(n):
        x = x0 + (x1 - x0) * (k + 0.5) / n
        for s in (-1, 1):
            r = rnd.uniform(0.28, 0.38)
            lump(P, f"{name}{k}{s}", r, (x + rnd.uniform(-0.08, 0.08), s * rnd.uniform(0.12, 0.2), -0.04), (1.4, 1.0, rnd.uniform(0.35, 0.5)), soil, rnd,
                 jitter=0.2, rot=(0, 0, rnd.uniform(-15, 15)), u=8, v=5)


def palisade_section(seed=45):
    """Three metres of palisade: eleven sharpened logs side by side, spiked to two rail logs behind and shored by two
    raking props, earth heaped along its foot."""
    rnd = random.Random(seed)
    P = _root("palisade")
    for k in range(11):
        x = -1.36 + k * 0.272
        _stake(P, f"log{k}", x + rnd.uniform(-0.02, 0.02), rnd.uniform(-0.03, 0.03), rnd.uniform(2.45, 2.85), rnd.uniform(0.12, 0.145),
               lean=(rnd.uniform(-0.06, 0.06), rnd.uniform(-0.04, 0.04)), segs=9)
    for z in (0.75, 1.95):
        _log(P, f"rail{z}", (-1.6, 0.24, z), (1.6, 0.26, z + 0.04), 0.08)
        for k in range(0, 11, 2):
            nut(P, (-1.36 + k * 0.272, 0.33, z + 0.02), r=0.025, rot=(90, 0, 0))
    for k, x in enumerate((-0.95, 1.0)):
        _log(P, f"shore{k}", (x, 1.45, -0.1), (x + 0.05, 0.3, 1.88), 0.07)
        _stake(P, f"shorepeg{k}", x + 0.15, 1.58, 0.25, 0.04, lean=(0, 0.04), segs=6)
    _berm(P, "berm", -1.55, 1.55, rnd)
    return P


def palisade_gate(seed=46):
    """A gate in the palisade: two thick sharpened gate posts and a lashed lintel log over a 3 m opening, two leaves of
    sharpened logs on battens and strap hinges, the right one pushed open inwards, stubs of the wall either side."""
    rnd = random.Random(seed)
    P = _root("palgate")
    gw = 3.0
    for s in (-1, 1):
        _stake(P, f"gatepost{s}", s * (gw / 2 + 0.2), 0, 3.45, 0.2, segs=10)
        for k in range(2):
            _stake(P, f"stub{s}{k}", s * (gw / 2 + 0.53 + k * 0.27), rnd.uniform(-0.03, 0.03), rnd.uniform(2.5, 2.8), rnd.uniform(0.12, 0.14),
                   lean=(rnd.uniform(-0.04, 0.04), 0), segs=9)
        _lash(P, (s * (gw / 2 + 0.2), 0, 3.05), 0.2, turns=2, step=0.05)
    _log(P, "lintel", (-gw / 2 - 0.5, -0.24, 3.05), (gw / 2 + 0.5, -0.24, 3.08), 0.12, segs=10)
    tops = [rnd.uniform(2.3, 2.5) for _ in range(12)]

    def leaf(G, sgn, off):
        """sgn +1 builds towards +x from the hinge, -1 towards -x."""
        for k in range(6):
            x = sgn * (0.13 + k * 0.245)
            _stake(G, f"glog{k}", x, 0, tops[off + k], 0.11, segs=8, z0=0.1)
        for z in (0.55, 1.85):
            K.box("batten", (1.45, 0.05, 0.16), wood(2), loc=(sgn * 0.74, 0.14, z), bev=0.01, parent=G)
            K.box("strap", (0.55, 0.012, 0.06), _iron(), loc=(sgn * 0.26, -0.115, z), bev=0.002, parent=G)
            K.cyl("pintle", 0.03, 0.12, _iron(), loc=(0, -0.08, z), segs=8, bev=0, parent=G)
        beam(G, "zbrace", (sgn * 0.15, 0.15, 0.6), (sgn * 1.35, 0.15, 1.8), 0.05, 0.14, wood(3))

    L = _grp(P, "leaf_l", (-gw / 2 + 0.02, 0, 0))
    leaf(L, 1, 0)
    R = _grp(P, "leaf_r", (gw / 2 - 0.02, 0, 0), (0, 0, -32))
    leaf(R, -1, 6)
    # the bar that shuts it, leaning on the left post, and its brackets on the left leaf
    beam(P, "bar", (-gw / 2 - 0.15, 0.55, 0.02), (-gw / 2 + 0.05, 0.3, 1.6), 0.09, 0.09, wood(3))
    for x in (0.9, 1.3):
        K.box("bracket", (0.06, 0.1, 0.14), _iron(), loc=(-gw / 2 + x, 0.21, 1.2), bev=0.005, parent=P)
    for s in (-1, 1):
        _berm(P, f"berm{s}", s * (gw / 2), s * (gw / 2 + 1.05), rnd, n=3)
    return P


# ================================================================== barricade
def barricade_scrap(seed=47):
    """A road barricade of whatever was to hand: two car doors stood on edge against a timber waler and raking props,
    planks nailed across them, a stack of tyres at one end and a tyre and a corrugated sheet at the other, sandbags."""
    rnd = random.Random(seed)
    P = _root("barricade")
    car_door(P, "door_a", (-1.12, 0.0, 0.0), (-10, 0, 0), SEA, glass=2)
    car_door(P, "door_b", (0.02, 0.03, 0.0), (-8, 0, 3), BRICK, glass=0)
    _log(P, "waler", (-1.3, 0.24, 0.9), (1.25, 0.24, 0.92), 0.06)
    for k, x in enumerate((-0.8, 0.6)):
        beam(P, f"prop{k}", (x, 1.15, 0.0), (x, 0.3, 0.95), 0.06, 0.1, wood(3))
    for k, (z, ry, ln) in enumerate(((0.62, 5, 2.5), (0.3, -3, 2.1))):
        y = z * math.tan(math.radians(9)) - 0.06
        K.box(f"plank{k}", (ln, 0.03, 0.18), wood(k + 1, wear=0.6), loc=(-0.05 + k * 0.1, y, z), rot=(-9, ry, 0), bev=0.006, parent=P)
        for x in (-0.9, 0.0, 0.9):
            _bolt(P, (x - 0.05 + k * 0.1, y - 0.02, z + x * math.tan(math.radians(-ry))))
    for k in range(3):
        tyre_ring(P, f"stack{k}", (1.72 + rnd.uniform(-0.04, 0.04), 0.12 + rnd.uniform(-0.04, 0.04), 0.1 + k * 0.2), r=0.33, w=0.2)
    tyre_ring(P, "upright", (-1.58, -0.18, 0.34), rot=(80, 0, 8), r=0.33, w=0.2)
    corr(P, "sheet", 0.85, 1.25, _sheet(ZINC), loc=(-1.6, 0.18, 0.6), rot=(82, 0, 12))
    sm = M("#c2a878", rough=0.95, grime=0.45)
    sack(P, "sandbag0", (0.6, 0.36, 0.22), (0.55, -0.32, 0.0), (0, 0, 4), sm, rnd, tied=False)
    sack(P, "sandbag1", (0.6, 0.36, 0.22), (0.0, -0.3, 0.0), (0, 0, -6), sm, rnd, tied=False)
    sack(P, "sandbag2", (0.6, 0.36, 0.2), (0.3, -0.31, 0.18), (0, 0, 2), sm, rnd, tied=False)
    return P


# ================================================================== tyres
def tire_stack(seed=48):
    """Five old tyres stacked a little askew, a sixth leaning on them and a seventh flat in the grass, a tuft of grass
    growing out of the top one."""
    rnd = random.Random(seed)
    P = _root("tyres")
    z = 0.0
    for k in range(5):
        r, w = (0.33, 0.21) if k < 4 else (0.3, 0.19)
        tyre_ring(P, f"t{k}", (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), z + w / 2), rot=(rnd.uniform(-2, 2), rnd.uniform(-2, 2), 0), r=r, w=w)
        z += w - 0.004
    tyre_ring(P, "lean", (0.5, -0.12, 0.35), rot=(0, 68, 8), r=0.33, w=0.21)
    tyre_ring(P, "flat", (-0.5, -0.5, 0.1), rot=(0, 0, 0), r=0.33, w=0.2)
    K.cyl("soil_in", 0.17, 0.02, K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), loc=(0.0, 0.0, z - 0.06), segs=12, bev=0, parent=P)
    _grass(P, "grass", (0.0, 0.0, z - 0.06), 7, rnd, h=0.28)
    _grass(P, "grass_b", (-0.5, -0.5, 0.0), 6, rnd, h=0.22)
    return P


def tire_swing(seed=49):
    """A tyre on a rope from the crossbar of a frame of two lashed log A-frames with spreaders, the grass worn to dirt
    under it."""
    rnd = random.Random(seed)
    P = _root("tyreswing")
    zt = 2.6
    for s in (-1, 1):
        for sy in (-1, 1):
            _log(P, f"leg{s}{sy}", (s * 1.3, sy * 0.85, -0.1), (s * 1.2, sy * 0.05, zt + 0.12), 0.075)
        beam(P, f"spreader{s}", (s * 1.3 + s * 0.08, -0.62, 0.65), (s * 1.3 + s * 0.08, 0.62, 0.65), 0.035, 0.12, wood(2))
        _lash(P, (s * 1.2, 0, zt), 0.1, rot=(0, 90, 0), turns=2, step=0.05)
    _log(P, "bar", (-1.5, 0, zt), (1.5, 0.02, zt), 0.09, segs=10)
    for s in (-1, 1):
        K.cyl(f"bar_end{s}", 0.08, 0.01, _cut(), loc=(s * 1.505, 0.01, zt), rot=(0, 90, 0), segs=10, bev=0, parent=P)
    for k in range(3):
        K.torus(f"knot{k}", 0.1, 0.014, rope_mat(), loc=(0.05 + (k - 1) * 0.03, 0, zt), rot=(0, 90, 0), segs=14, parent=P)
    _line(P, "rope", [(0.05, 0, zt - 0.09), (0.05, 0.0, 1.05)], 0.014, rope_mat())
    tyre_ring(P, "tyre", (0.05, 0, 0.73), rot=(90, 0, 18), r=0.33, w=0.2)
    K.torus("tie", 0.07, 0.014, rope_mat(), loc=(0.05, 0, 1.0), rot=(0, 90, 18), segs=14, parent=P)
    dirt = K.disc("dirt", 0.65, K.mat("worn", WORN, rough=1.0, noise_scale=3.0), (0.05, 0.0), z=0.008, segs=20, wobble=0.18, seed=seed)
    dirt.parent = P
    return P


# ================================================================== fences
def car_door_fence(seed=50):
    """A fence of car doors bolted between log posts, three bays: one door hung upside down, a strand of wire along the
    top and twisted wire ties at the posts."""
    rnd = random.Random(seed)
    P = _root("doorfence")
    xs = (-1.7, -0.57, 0.57, 1.7)
    for k, x in enumerate(xs):
        _logpost(P, f"post{k}", x, 0.02, 1.45 + rnd.uniform(-0.05, 0.05), r=0.07, lean=(rnd.uniform(-0.03, 0.03), 0))
    for k, (col, glass, flip) in enumerate(((SEA, 1, False), (MUSTARD, 0, True), (BRICK, 2, False))):
        c = -1.135 + k * 1.135
        if flip:
            car_door(P, f"door{k}", (c + 0.54, -0.11, 1.22), (0, 180, 0), col, glass=glass)
        else:
            car_door(P, f"door{k}", (c - 0.54, -0.11, 0.13 + rnd.uniform(0, 0.04)), (0, rnd.uniform(-1.5, 1.5), 0), col, glass=glass)
    for x in xs:
        for z in (0.35, 0.95):
            K.box("tie", (0.2, 0.17, 0.012), _iron(), loc=(x, -0.06, z), bev=0, parent=P)
    for k in range(3):
        pts = _sag((xs[k], -0.07, 1.38), (xs[k + 1], -0.07, 1.38), 0.05, n=4)
        _line(P, f"wire{k}", pts, 0.005, _iron(), segs=4)
    return P


def sheet_metal_fence(seed=51):
    """Four metres of fence of corrugated sheets of every colour nailed to two rails on square posts, uneven along the
    top, a small patch over a hole."""
    rnd = random.Random(seed)
    P = _root("sheetfence")
    for k, x in enumerate((-1.95, 0.0, 1.95)):
        post(P, f"post{k}", (x, 0.02), 2.0, r=0.05, tilt=(rnd.uniform(-1.5, 1.5), rnd.uniform(-1.5, 1.5)))
    for z in (0.4, 1.55):
        K.box("rail", (4.1, 0.06, 0.1), wood(2), loc=(0, -0.06, z), bev=0.01, parent=P)
    cols = (ZINC, RUST, ZINC, SEA, BRICK)
    for k, col in enumerate(cols):
        x = -1.62 + k * 0.81
        h = rnd.uniform(1.75, 1.95)
        y = -0.12 - (k % 2) * 0.012
        corr(P, f"sheet{k}", 0.9, h, _sheet(col), loc=(x, y, 0.04 + h / 2), rot=(90, rnd.uniform(-1.2, 1.2), 0))
        for z in (0.4, 1.55):
            for dx in (-0.3, 0.3):
                _bolt(P, (x + dx, y - 0.025, z))
    corr(P, "patch", 0.42, 0.38, _sheet(MUSTARD), loc=(-0.75, -0.165, 0.8), rot=(90, 7, 0))
    for dx in (-0.15, 0.15):
        _bolt(P, (-0.75 + dx, -0.19, 0.9))
    return P


# ================================================================== signboard
def signboard_painted(seed=52):
    """A settler sign on two posts: four boards painted bone (one a bare replacement), a teal water drop, a red arrow
    and three dots for the distance, a little board roof over it and a brace behind."""
    rnd = random.Random(seed)
    P = _root("signboard")
    for s in (-1, 1):
        post(P, f"post{s}", (s * 0.8, 0.02), 2.3, r=0.06)
    for k, z in enumerate((1.12, 1.35, 1.58, 1.81)):
        m = wood(1, wear=0.5) if k == 2 else _paint(BONE, wear=0.45, rough=0.85, scratches=0.5)
        K.box(f"board{k}", (1.85, 0.03, 0.22), m, loc=(rnd.uniform(-0.02, 0.02), -0.08, z), rot=(0, rnd.uniform(-0.8, 0.8), 0), bev=0.006, parent=P)
    for s in (-1, 1):
        for z in (1.2, 1.75):
            _bolt(P, (s * 0.8, -0.1, z))
    paint = dict(wear=0.35, rough=0.85, grime=0.3, scratches=0.4)
    r, d = 0.16, 0.36
    th = math.acos(r / d)
    drop = [(0.0, d)] + [(r * math.cos(a), r * math.sin(a)) for a in
                         (math.pi / 2 + th + (2 * math.pi - 2 * th) * i / 14 for i in range(15))]
    prism(P, "drop", drop, 0.006, M(SEA, **paint), loc=(-0.5, -0.097, 1.38), plane="xz", bev=0)
    arrow = [(-0.3, -0.07), (0.08, -0.07), (0.08, -0.17), (0.32, 0.0), (0.08, 0.17), (0.08, 0.07), (-0.3, 0.07)]
    prism(P, "arrow", arrow, 0.006, M(BRICK, **paint), loc=(0.3, -0.097, 1.58), plane="xz", bev=0)
    for k in range(3):
        K.cyl(f"dot{k}", 0.045, 0.006, M(GRAPHITE, **paint), loc=(0.12 + k * 0.17, -0.097, 1.24), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    K.box("roof", (2.0, 0.3, 0.025), wood(3), loc=(0, -0.06, 2.22), rot=(15, 0, 0), bev=0.005, parent=P)
    beam(P, "brace", (-0.8, 0.07, 0.4), (0.8, 0.07, 1.6), 0.03, 0.1, wood(2))
    return P


# ================================================================== power and water
def scrap_windmill_turbine(seed=53):
    """A home-made wind turbine on a two-piece steel pipe set in a concrete-filled drum and guyed three ways: a finned
    salvaged motor on a yaw post, three blades cut from sheet (one a red replacement), a tail vane cut from an old
    teal sign, the cable clipped down the pipe to a junction box and off along the grass."""
    rnd = random.Random(seed)
    P = _root("turbine")
    zh = 5.0
    K.cyl("drum", 0.3, 0.88, _paint(BRICK, metal=0.35, wear=0.25, grime=0.55), loc=(0, 0, 0.44), segs=18, bev=0.02, parent=P)
    for z in (0.1, 0.44, 0.78):
        K.cyl("hoop", 0.306, 0.03, M(RUST, metal=0.4, wear=0.2), loc=(0, 0, z), segs=18, bev=0, parent=P)
    K.cyl("fill", 0.27, 0.02, _concrete(), loc=(0, 0, 0.885), segs=18, bev=0, parent=P)
    steel = M(STEEL, metal=0.45, wear=0.2, grime=0.4)
    zinc = M(ZINC, metal=0.5, wear=0.2, grime=0.4)
    rod(P, "pipe_lo", (0, 0, 0.85), (0, 0, 2.9), 0.06, steel, segs=10)
    K.cyl("collar", 0.08, 0.16, _iron(), loc=(0, 0, 2.9), segs=10, bev=0, parent=P)
    rod(P, "pipe_hi", (0, 0, 2.9), (0, 0, zh - 0.1), 0.05, zinc, segs=10)
    K.cyl("guyring", 0.075, 0.05, _iron(), loc=(0, 0, 3.8), segs=10, bev=0, parent=P)
    for k in range(3):
        a = math.radians(105 + 120 * k)
        gx, gy = 2.4 * math.cos(a), 2.4 * math.sin(a)
        rod(P, f"guy{k}", (0.07 * math.cos(a), 0.07 * math.sin(a), 3.8), (gx, gy, 0.18), 0.007, _iron(), segs=4)
        _bar(P, f"peg{k}", (gx * 0.99, gy * 0.99, -0.1), (gx * 1.03, gy * 1.03, 0.22), 0.035, _iron())
    H = _grp(P, "head", (0, 0, zh))
    K.cyl("yaw", 0.075, 0.16, _iron(), loc=(0, 0, -0.06), segs=10, bev=0, parent=H)
    K.box("saddle", (0.14, 0.32, 0.06), _iron(), loc=(0, 0, 0.03), bev=0.005, parent=H)
    K.cyl("motor", 0.16, 0.44, _paint(BONE, metal=0.3, wear=0.25), loc=(0, -0.02, 0.2), rot=(90, 0, 0), segs=16, bev=0.02, parent=H)
    for y in (-0.12, 0.0, 0.12):
        K.cyl("fin", 0.178, 0.022, _iron(), loc=(0, y, 0.2), rot=(90, 0, 0), segs=16, bev=0, parent=H)
    K.cyl("endcap", 0.12, 0.06, _iron(), loc=(0, 0.22, 0.2), rot=(90, 0, 0), segs=12, bev=0, parent=H)
    rod(H, "shaft", (0, -0.24, 0.2), (0, -0.38, 0.2), 0.025, _iron(), segs=8)
    K.cyl("hub", 0.11, 0.1, _paint(MUSTARD, metal=0.3, wear=0.25), loc=(0, -0.38, 0.2), rot=(90, 0, 0), segs=14, bev=0.01, parent=H)
    K.cyl("nose", 0.1, 0.12, _paint(MUSTARD, metal=0.3, wear=0.25), loc=(0, -0.49, 0.2), rot=(90, 0, 0), segs=14, r2=0.025, bev=0, parent=H)
    for k, col in enumerate((BONE, BRICK, BONE)):
        B = _grp(H, f"blade{k}", (0, -0.4, 0.2), (0, 120 * k + 10, 0))
        prism(B, f"blade{k}", [(-0.09, 0.07), (0.09, 0.07), (0.075, 1.12), (-0.02, 1.15)], 0.018, _paint(col, metal=0.3, wear=0.25),
              rot=(0, 0, 14), plane="xz", bev=0.004)
        for z in (0.1, 0.18):
            nut(B, (0, -0.02, z), r=0.018, rot=(90, 0, 0))
    rod(H, "boom", (0, 0.24, 0.2), (0, 1.0, 0.24), 0.022, _iron(), segs=6)
    prism(H, "vane", [(0.7, -0.16), (1.32, -0.24), (1.32, 0.46), (0.86, 0.26)], 0.012, _paint(SEA, metal=0.3, wear=0.3), loc=(0, 0, 0.24), plane="yz",
          bev=0.003)
    prism(H, "vane_stripe", [(0.86, 0.02), (1.3, -0.02), (1.3, 0.08), (0.86, 0.12)], 0.016, _paint(BONE, wear=0.3), loc=(0, 0, 0.24), plane="yz", bev=0)
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    _line(P, "cable", [(0.09, 0.05, zh + 0.12), (0.075, 0.06, zh - 0.25), (0.075, 0.06, 1.42)], 0.012, cab)
    for z in (1.9, 2.6, 3.4, 4.3):
        K.box("clip", (0.05, 0.13, 0.03), _iron(), loc=(0.05, 0.03, z), bev=0, parent=P)
    K.box("jbox", (0.18, 0.12, 0.26), _paint(GRAPHITE, metal=0.3), loc=(0.13, 0.0, 1.25), bev=0.01, parent=P)
    _line(P, "cable_out", [(0.13, -0.03, 1.12), (0.3, -0.25, 0.03), (0.9, -0.6, 0.02), (1.6, -0.75, 0.02)], 0.012, cab)
    return P


def _panel(T, name, x, w, h, frame_col, cracked, yaw, rnd):
    """A salvaged solar panel on the rack: frame, cells and their bus lines, a junction box behind, maybe crazed."""
    Q = _grp(T, name, (x, rnd.uniform(-0.03, 0.03), 0.07), (0, 0, yaw))
    K.box(f"{name}_frame", (w, h, 0.04), _paint(frame_col, metal=0.4, wear=0.2), bev=0.008, parent=Q)
    cell = K.mat("cells", CELL, rough=0.3, metal=0.3, grime=0.35)
    K.box(f"{name}_cells", (w - 0.07, h - 0.07, 0.012), cell, loc=(0, 0, 0.018), bev=0, parent=Q)
    line = K.mat("busline", CELL_L, rough=0.4, metal=0.4)
    nx, ny = max(2, int(w / 0.16)), max(3, int(h / 0.16))
    for i in range(1, nx):
        K.box(f"{name}_lv", (0.008, h - 0.08, 0.003), line, loc=(-w / 2 + 0.035 + (w - 0.07) * i / nx, 0, 0.025), bev=0, parent=Q)
    for j in range(1, ny):
        K.box(f"{name}_lh", (w - 0.08, 0.008, 0.003), line, loc=(0, -h / 2 + 0.035 + (h - 0.07) * j / ny, 0.025), bev=0, parent=Q)
    K.box(f"{name}_jbox", (0.14, 0.09, 0.05), _iron(), loc=(0, h * 0.3, -0.04), bev=0.005, parent=Q)
    if cracked:
        cm = M(CRACK, wear=0, grime=0.2, scratches=0, rough=0.3)
        cx, cy = w * 0.12, -h * 0.15
        for k in range(6):
            a = math.radians(k * 60 + rnd.uniform(-20, 20))
            L = rnd.uniform(0.12, 0.3)
            K.box(f"{name}_crack", (0.006, L, 0.004), cm, loc=(cx - math.sin(a) * L / 2, cy + math.cos(a) * L / 2, 0.027), rot=(0, 0, math.degrees(a)),
                  bev=0, parent=Q)
    return Q


def solar_scrap_rig(seed=54):
    """Three solar panels that never came as a set (one crazed) on a sloped timber rack, X-braced behind; a plank
    battery box under it with its lid thrown back on two car batteries, cables down to them and a dead charge meter on
    a post."""
    rnd = random.Random(seed)
    P = _root("solarrig")
    yf, yb, zf, zb = -0.55, 0.55, 0.72, 1.52
    ang = math.degrees(math.atan2(zb - zf, yb - yf))
    slope = (zb - zf) / (yb - yf)

    def zl(y):
        return zf + (y - yf) * slope

    for sx in (-1, 1):
        post(P, f"pf{sx}", (sx * 1.3, yf), zf + 0.1, r=0.05)
        post(P, f"pb{sx}", (sx * 1.3, yb), zb + 0.1, r=0.05)
        beam(P, f"rail{sx}", (sx * 1.3, yf - 0.12, zl(yf - 0.12) + 0.04), (sx * 1.3, yb + 0.12, zl(yb + 0.12) + 0.04), 0.06, 0.08, wood(2))
    beam(P, "xb0", (-1.3, yb + 0.06, 0.2), (1.3, yb + 0.06, zb - 0.15), 0.03, 0.1, wood(1))
    beam(P, "xb1", (1.3, yb + 0.06, 0.2), (-1.3, yb + 0.06, zb - 0.15), 0.03, 0.1, wood(3))
    K.box("lowrail", (2.7, 0.03, 0.1), wood(2), loc=(0, yf - 0.06, 0.3), bev=0.006, parent=P)
    a = math.radians(ang)
    T = _grp(P, "rack", (0, -0.08 * math.sin(a), zl(0) + 0.08 * math.cos(a)), (ang, 0, 0))
    for y in (-0.42, 0.42):
        K.box("purlin", (2.75, 0.06, 0.05), wood(3), loc=(0, y, 0.025), bev=0.006, parent=T)
    _panel(T, "pa", -0.86, 0.95, 1.4, ZINC, False, 0, rnd)
    _panel(T, "pb", 0.04, 0.72, 1.12, BONE, True, 2.5, rnd)
    _panel(T, "pc", 0.9, 0.86, 1.3, GUNMETAL, False, -1.2, rnd)
    # the battery box
    bx, by = 0.25, -0.02
    W, D, Hb = 0.72, 0.46, 0.38
    K.box("box_floor", (W, D, 0.03), wood(2), loc=(bx, by, 0.015), bev=0, parent=P)
    for s in (-1, 1):
        K.box("box_fb", (W, 0.03, Hb), wood(1 + (s > 0)), loc=(bx, by + s * (D / 2 - 0.015), Hb / 2), bev=0.006, parent=P)
        K.box("box_side", (0.03, D - 0.06, Hb), wood(1), loc=(bx + s * (W / 2 - 0.015), by, Hb / 2), bev=0.006, parent=P)
    L = _grp(P, "lid", (bx, by + D / 2, Hb), (-104, 0, 0))
    K.box("lid", (W + 0.02, D + 0.02, 0.025), wood(3), loc=(0, -D / 2, 0.012), bev=0.006, parent=L)
    for k, x in enumerate((-0.17, 0.17)):
        K.box(f"batt{k}", (0.3, 0.18, 0.21), M(BATT, rough=0.6, wear=0.1, grime=0.3), loc=(bx + x, by - 0.03, 0.135), bev=0.01, parent=P)
        for t, m in ((-0.09, M(SIGNAL, wear=0.1)), (0.09, M(GRAPHITE, wear=0.1))):
            K.cyl(f"term{k}", 0.018, 0.035, m, loc=(bx + x + t, by - 0.08, 0.255), segs=8, bev=0, parent=P)
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    _line(P, "cable_a", [(0.04, 0.05, zl(0.05) - 0.02), (0.15, -0.05, 0.75), (bx - 0.08, by - 0.08, 0.27)], 0.01, cab)
    _line(P, "cable_b", [(-0.86, 0.0, zl(0.0) - 0.02), (-0.3, -0.1, 0.7), (bx + 0.08, by - 0.08, 0.27)], 0.01, cab)
    # the charge meter on the left front post, its needle at nothing
    K.box("meter", (0.16, 0.07, 0.2), _paint(BONE, metal=0.3), loc=(-1.3, yf - 0.09, 0.45), bev=0.01, parent=P)
    K.cyl("dial", 0.05, 0.01, M(GRAPHITE, wear=0), loc=(-1.3, yf - 0.13, 0.48), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.box("needle", (0.004, 0.004, 0.045), M(SIGNAL, wear=0), loc=(-1.31, yf - 0.137, 0.47), rot=(0, 55, 0), bev=0, parent=P)
    _line(P, "cable_m", [(-1.3, yf - 0.06, 0.36), (-0.9, -0.4, 0.03), (bx - 0.36, by - 0.1, 0.05)], 0.008, cab)
    return P


def antenna_scrap(seed=55):
    """A tall pole of salvaged antennas: a barked log with a steel mast clamped to it, guyed at two heights, a yagi on
    top with a whip above it, a small dish on an arm, a cross arm with two car aerials and a loose wire, the down-lead
    clipped to a radio box at chest height and trailing off in the grass."""
    rnd = random.Random(seed)
    P = _root("antenna")
    _logpost(P, "pole", 0, 0, 4.4, r=0.11, segs=9)
    mx = 0.15
    steel = M(STEEL, metal=0.45, wear=0.2, grime=0.4)
    zinc = M(ZINC, metal=0.5, wear=0.2, grime=0.35)
    rod(P, "mast", (mx, 0, 3.5), (mx, 0, 8.0), 0.045, steel, segs=8)
    K.cyl("mast_cap", 0.05, 0.03, _iron(), loc=(mx, 0, 8.01), segs=8, bev=0, parent=P)
    for z in (3.7, 4.0, 4.3):
        K.box("clamp", (0.38, 0.25, 0.045), _iron(), loc=(0.06, 0, z), bev=0.005, parent=P)
    for k in range(3):
        a = math.radians(40 + 120 * k)
        gx, gy = 3.0 * math.cos(a), 3.0 * math.sin(a)
        for z0, x0 in ((6.6, mx), (4.2, 0.0)):
            rod(P, f"guy{k}{z0}", (x0, 0, z0), (gx, gy, 0.3), 0.006, _iron(), segs=4)
        _stake(P, f"peg{k}", gx, gy, 0.38, 0.05, lean=(-0.08 * math.cos(a), -0.08 * math.sin(a)), segs=6)
    # yagi and whip
    zy = 7.35
    rod(P, "boom", (mx, -0.85, zy), (mx, 0.85, zy), 0.025, zinc, segs=6)
    K.box("boom_clamp", (0.1, 0.1, 0.12), _iron(), loc=(mx, 0, zy), bev=0.005, parent=P)
    for k, (y, ln) in enumerate(((-0.78, 0.66), (-0.48, 0.72), (-0.18, 0.78), (0.12, 0.84), (0.45, 0.92), (0.78, 1.04))):
        rod(P, f"elem{k}", (mx - ln / 2, y, zy), (mx + ln / 2, y, zy), 0.013, zinc, segs=4)
    K.cyl("whip_base", 0.025, 0.12, _iron(), loc=(mx, 0, 8.08), segs=8, bev=0, parent=P)
    rod(P, "whip", (mx, 0, 8.12), (mx + 0.05, 0, 9.35), 0.007, _iron(), segs=4)
    # dish on an arm
    rod(P, "dish_arm", (mx, 0, 5.75), (mx + 0.36, -0.08, 5.72), 0.025, steel, segs=6)
    D = _grp(P, "dish", (mx + 0.42, -0.12, 5.72), (72, 0, 28))
    K.place(dish_mesh("dish", 0.55, 0.15, 0.025, _paint(BONE, metal=0.3, wear=0.25), rings=4, segs=16), parent=D)
    rod(D, "feed", (0, 0, 0.0), (0, 0, 0.45), 0.014, _iron(), segs=4)
    K.box("lnb", (0.07, 0.07, 0.12), _paint(GRAPHITE), loc=(0, 0, 0.5), bev=0.005, parent=D)
    # a colony panel antenna on the other side, its bracket rusted
    rod(P, "panel_arm", (mx, 0, 6.75), (mx - 0.3, -0.02, 6.75), 0.025, steel, segs=6)
    K.box("panel_ant", (0.1, 0.32, 1.0), _paint(BONE, metal=0.3, wear=0.25), loc=(mx - 0.36, -0.02, 6.75), rot=(0, 0, 20), bev=0.02, parent=P)
    K.box("panel_band", (0.11, 0.33, 0.12), _paint(STEEL, metal=0.3, wear=0.25), loc=(mx - 0.36, -0.02, 6.45), rot=(0, 0, 20), bev=0.005, parent=P)
    # a rag tied on to see the wind by
    corr(P, "rag", 0.12, 0.5, M(SIGNAL, wear=0.1, rough=0.9, grime=0.3), loc=(mx + 0.05, -0.1, 7.85), rot=(90, 75, 30), pitch=0.12, amp=0.02, t=0.008)
    # cross arm with two car aerials and a loose wire
    za = 6.25
    K.box("xarm", (1.6, 0.07, 0.08), wood(2), loc=(mx + 0.08, -0.07, za), bev=0.008, parent=P)
    for k, x in enumerate((mx - 0.66, mx + 0.82)):
        K.cyl(f"aerial_base{k}", 0.025, 0.05, _iron(), loc=(x, -0.07, za + 0.06), segs=8, bev=0, parent=P)
        rod(P, f"aerial{k}", (x, -0.07, za + 0.08), (x + (0.06 if k else -0.04), -0.07, za + 0.95), 0.006, zinc, segs=4)
    _line(P, "loose", [(mx + 0.82, -0.07, za - 0.04), (mx + 0.86, -0.1, za - 0.6), (mx + 0.78, -0.16, za - 1.2), (mx + 0.84, -0.12, za - 1.5)], 0.006, _iron(), segs=4)
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    _line(P, "downlead", [(mx + 0.05, 0.05, zy - 0.05), (mx + 0.06, 0.06, 4.5), (0.13, 0.1, 4.25), (0.1, 0.1, 1.6)], 0.011, cab)
    for z in (5.0, 6.0, 7.0):
        K.box("tape", (0.12, 0.13, 0.03), _iron(), loc=(mx + 0.02, 0.03, z), bev=0, parent=P)
    for z in (2.2, 3.1):
        K.box("tape2", (0.25, 0.25, 0.03), _iron(), loc=(0.02, 0.0, z), bev=0, parent=P)
    # the radio box
    K.box("radio", (0.32, 0.18, 0.34), _paint(BONE, metal=0.3), loc=(0.0, -0.2, 1.4), bev=0.015, parent=P)
    K.box("radio_face", (0.26, 0.01, 0.12), M(GRAPHITE, wear=0.1), loc=(0.0, -0.293, 1.47), bev=0, parent=P)
    for k, x in enumerate((-0.08, 0.06)):
        K.cyl(f"knob{k}", 0.025, 0.03, M(GRAPHITE, wear=0.1), loc=(x, -0.3, 1.32), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    K.box("radio_shade", (0.4, 0.26, 0.02), _sheet(ZINC), loc=(0.0, -0.21, 1.6), rot=(-10, 0, 0), bev=0, parent=P)
    _line(P, "trail", [(0.1, -0.15, 1.24), (0.3, -0.35, 0.03), (1.1, -0.5, 0.02), (1.8, -0.3, 0.02)], 0.011, cab)
    return P


def rain_catcher(seed=56):
    """A tarp tied to four leaning poles and dipped into a funnel, its low point spouting into a ribbed tank on a pallet;
    guy ropes to pegs, a tap at the foot of the tank over a bucket."""
    rnd = random.Random(seed)
    P = _root("raincatcher")
    e, zc, zlow = 1.25, 2.3, 1.48
    for sx in (-1, 1):
        for sy in (-1, 1):
            _logpost(P, f"pole{sx}{sy}", sx * 1.32, sy * 1.32, zc + 0.12, r=0.06, lean=(sx * 0.05, sy * 0.05))

    def fn(x, y):
        u, v = abs(x) / e, abs(y) / e
        rr = max(u, v)
        return zlow + (zc - zlow) * rr ** 1.15 - 0.18 * (1 - min(u, v) ** 2) * rr ** 4

    _surface(P, "tarp", fn, -e, e, -e, e, 10, 10, 0.012, M(SEA, wear=0.1, rough=0.9, grime=0.35, scratches=0))
    for sx in (-1, 1):
        for sy in (-1, 1):
            _line(P, f"tie{sx}{sy}", [(sx * e, sy * e, zc - 0.01), (sx * 1.36, sy * 1.36, zc + 0.06)], 0.012, rope_mat())
            _line(P, f"guy{sx}{sy}", [(sx * 1.37, sy * 1.37, zc + 0.08), (sx * 1.95, sy * 1.95, 0.08)], 0.01, rope_mat(), segs=4)
            _stake(P, f"peg{sx}{sy}", sx * 2.0, sy * 2.0, 0.2, 0.035, lean=(sx * 0.05, sy * 0.05), segs=6)
    # pallet and tank
    for k in range(3):
        K.box(f"bearer{k}", (0.1, 1.1, 0.1), wood(3), loc=(-0.45 + k * 0.45, 0, 0.05), bev=0.008, parent=P)
    for k in range(5):
        K.box(f"slat{k}", (1.1, 0.18, 0.03), wood(k), loc=(0, -0.44 + k * 0.22, 0.115), bev=0.004, parent=P)
    tank = _sheet(ZINC, wear=0.2)
    K.cyl("tank", 0.55, 1.0, tank, loc=(0, 0, 0.63), segs=28, bev=0.02, parent=P)
    for z in (0.3, 0.5, 0.7, 0.9):
        K.cyl("rib", 0.56, 0.03, tank, loc=(0, 0, z), segs=28, bev=0, parent=P)
    K.cyl("lid", 0.5, 0.03, _sheet(STEEL), loc=(0, 0, 1.145), segs=24, bev=0.008, parent=P)
    rod(P, "spout", (0, 0, zlow + 0.02), (0, 0, 1.15), 0.05, _iron(), segs=10)
    K.cyl("collar", 0.08, 0.04, _iron(), loc=(0, 0, zlow - 0.01), segs=10, bev=0, parent=P)
    rod(P, "tap", (0, -0.54, 0.32), (0, -0.7, 0.32), 0.022, _iron(), segs=8)
    rod(P, "tap_down", (0, -0.7, 0.33), (0, -0.7, 0.26), 0.02, _iron(), segs=8)
    K.box("tap_handle", (0.12, 0.02, 0.02), M(SIGNAL, wear=0.2), loc=(0, -0.64, 0.37), bev=0, parent=P)
    _bucket(P, "bucket", (0.0, -0.78, 0.0), col=SEA, r=0.15, h=0.24)
    return P


def water_still(seed=57):
    """A still: an oil drum on field stones for a fire box, its end cut open and wood pushed in, a riveted copper pot on
    it under an onion head, the swan neck running over to a coil standing in an open barrel of water, the spirit
    dripping from a spout into a jar; split wood beside it."""
    rnd = random.Random(seed)
    P = _root("still")
    fx = -0.4
    for k, (dx, dy) in enumerate(((-0.27, -0.3), (0.27, -0.28), (-0.27, 0.3), (0.27, 0.32))):
        lump(P, f"stone{k}", 0.13, (fx + dx, dy, 0.06), (1.2, 1.0, 0.8), _stone(), rnd, flat=0.4, u=8, v=6)
    drum = _paint(STEEL, metal=0.4, wear=0.25, grime=0.6)
    K.cyl("firebox", 0.32, 0.8, drum, loc=(fx, 0, 0.44), rot=(90, 0, 0), segs=18, bev=0.02, parent=P)
    for y in (-0.3, 0.3):
        K.cyl("fb_hoop", 0.326, 0.03, M(RUST, metal=0.4, wear=0.2), loc=(fx, y, 0.44), rot=(90, 0, 0), segs=18, bev=0, parent=P)
    char = K.mat("char", "#2a2622", rough=0.95, grime=0.2)
    K.cyl("mouth", 0.26, 0.012, char, loc=(fx, -0.404, 0.42), rot=(90, 0, 0), segs=16, bev=0, parent=P)
    for k, (x0, x1, z) in enumerate(((-0.08, -0.12, 0.33), (0.07, 0.12, 0.36))):
        rod(P, f"brand{k}", (fx + x0, -0.1, z), (fx + x1, -0.62, z - 0.12), 0.045, _bark(), segs=7)
    K.cyl("fb_door", 0.24, 0.02, drum, loc=(fx + 0.36, -0.52, 0.38), rot=(90, 0, -70), segs=16, bev=0, parent=P)
    rod(P, "flue", (fx, 0.3, 0.7), (fx, 0.3, 1.55), 0.05, _iron(), segs=8)
    K.cyl("flue_cap", 0.1, 0.06, _iron(), loc=(fx, 0.3, 1.6), segs=10, r2=0.02, bev=0, parent=P)
    K.box("soot", (0.2, 0.01, 0.12), K.mat("soot", SOOT, rough=1.0), loc=(fx, -0.405, 0.74), bev=0, parent=P)
    for x in (-0.18, 0.18):
        K.box("cradle", (0.04, 0.55, 0.04), _iron(), loc=(fx + x, -0.05, 0.76), bev=0, parent=P)
    cu = _copper()
    z0 = 0.77
    lathe("pot", [(0.01, 0.0), (0.2, 0.0), (0.28, 0.07), (0.3, 0.24), (0.26, 0.4), (0.13, 0.48), (0.01, 0.49)], cu, segs=18, loc=(fx, -0.05, z0), parent=P)
    K.cyl("pot_band", 0.303, 0.03, _iron(), loc=(fx, -0.05, z0 + 0.24), segs=18, bev=0, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        K.sphere(f"rivet{k}", 0.012, cu, loc=(fx + 0.3 * math.cos(a), -0.05 + 0.3 * math.sin(a), z0 + 0.17), parent=P)
    zh = z0 + 0.46
    lathe("helmet", [(0.01, 0.0), (0.11, 0.0), (0.16, 0.08), (0.15, 0.2), (0.08, 0.28), (0.01, 0.29)], cu, segs=16, loc=(fx, -0.05, zh), parent=P)
    K.cyl("gauge", 0.045, 0.025, _paint(BONE, wear=0.1), loc=(fx, -0.05 - 0.17, zh + 0.12), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.box("gauge_needle", (0.004, 0.004, 0.035), M(SIGNAL, wear=0), loc=(fx, -0.05 - 0.184, zh + 0.125), rot=(0, 30, 0), bev=0, parent=P)
    cx = 0.65
    zt, zb_, R, turns, n = 1.18, 0.72, 0.15, 2.5, 26
    coil = [(cx + R * math.cos(math.pi + 2 * math.pi * turns * i / n), R * math.sin(math.pi + 2 * math.pi * turns * i / n), zt + (zb_ - zt) * i / n)
            for i in range(n + 1)]
    _line(P, "neck", [(fx + 0.06, -0.05, zh + 0.24), (fx + 0.22, -0.04, zh + 0.36), (0.1, -0.02, zh + 0.3), coil[0]], 0.024, cu, segs=8)
    _line(P, "coil", coil, 0.022, cu, segs=6)
    K.cyl("tub", 0.3, 0.9, wood(2, wear=0.5), loc=(cx, 0, 0.45), segs=18, bev=0.01, parent=P)
    for z in (0.15, 0.75):
        K.cyl("tub_hoop", 0.306, 0.035, _iron(), loc=(cx, 0, z), segs=18, bev=0, parent=P)
    K.cyl("water", 0.275, 0.012, K.mat("trough_water", WATER, rough=0.05, metal=0.0), loc=(cx, 0, 0.9), segs=18, bev=0, parent=P)
    _line(P, "outlet", [(cx, -0.29, 0.22), (cx, -0.45, 0.22), (cx, -0.47, 0.17)], 0.018, cu, segs=8)
    K.cyl("jar", 0.07, 0.17, K.mat("jarglass", JAR, rough=0.2, metal=0.1, grime=0.2), loc=(cx, -0.48, 0.085), segs=12, bev=0.01, parent=P)
    for k in range(3):
        rod(P, f"split{k}", (-1.0 + k * 0.13, -0.2, 0.06 + (k == 1) * 0.1), (-1.0 + k * 0.13 + 0.03, 0.45, 0.06 + (k == 1) * 0.1), 0.06, _bark(), segs=7)
    return P


# ================================================================== the generator and its batteries
def generator_shed(seed=58):
    """A tiny shed on a plank floor (board walls, one corrugated side, a mono-pitch tin roof) with its sheet door
    hanging open on a salvaged colony generator in a tube cage: engine, alternator, a mustard tank, the exhaust out
    through the side wall and up past the roof under a flap, soot round it, a cable out to the grass, a jerrycan."""
    rnd = random.Random(seed)
    P = _root("genshed")
    hw, hd = 0.8, 0.6
    zf, zb = 1.78, 1.5

    def roof(y):
        return zf + (zb - zf) * (y + hd) / (2 * hd)

    ang = math.degrees(math.atan2(zf - zb, 2 * hd))
    for y in (-0.5, 0.0, 0.5):
        K.box("bearer", (1.6, 0.1, 0.1), wood(3), loc=(0, y, 0.05), bev=0.008, parent=P)
    K.box("floor", (1.62, 1.22, 0.04), wood(2, wear=0.3), loc=(0, 0, 0.12), bev=0.004, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            zt = roof(sy * 0.56) - 0.08
            K.box("post", (0.08, 0.08, zt - 0.14), wood(3), loc=(sx * 0.76, sy * 0.56, 0.14 + (zt - 0.14) / 2), bev=0.01, parent=P)
    for sy in (-1, 1):
        K.box("plate", (1.6, 0.08, 0.08), wood(2), loc=(0, sy * 0.56, roof(sy * 0.56) - 0.04), bev=0.008, parent=P)
    hs = zb - 0.1 - 0.14
    _bay(P, "back", "planks", (0, 0.6), 180, 1.56, roof(0.6) - 0.08 - 0.14, None, rnd)
    _bay(P, "left", "planks", (-0.8, 0), -90, 1.16, hs, None, rnd)
    _bay(P, "right", "sheet", (0.8, 0), 90, 1.16, hs, ZINC, rnd)
    for sx in (-1, 1):
        prism(P, f"gable{sx}", [(-0.62, zb - 0.12), (0.62, zb - 0.12), (0.62, roof(0.62)), (-0.62, roof(-0.62))], 0.025, wood(1, wear=0.5),
              loc=(sx * 0.835, 0, 0), plane="yz")
    L = 1.6 / math.cos(math.radians(ang))
    for k, (x, col) in enumerate(((-0.45, RUST), (0.45, ZINC))):
        corr(P, f"roof{k}", 0.98, L, _sheet(col), loc=(x, 0, roof(0) + 0.02 + k * 0.012), rot=(-ang, rnd.uniform(-0.8, 0.8), 0))
        for y in (-0.6, 0.6):
            _bolt(P, (x, y, roof(y) + 0.045 + k * 0.012), rot=(-ang, 0, 0))
    lump(P, "weight", 0.13, (-0.3, 0.2, roof(0.2) + 0.07), (1.2, 1.0, 0.7), _stone(), rnd, flat=0.45, rot=(-ang, 0, 0), u=8, v=6)
    # the door: a corrugated sheet on a Z of boards, hung on the right front post and swung wide
    Dg = _grp(P, "door", (0.79, -0.64, 0.16), (0, 0, 118))
    corr(Dg, "door_sheet", 1.48, 1.45, _sheet(SEA), loc=(-0.76, -0.01, 0.78), rot=(90, 0, 0))
    for z in (0.2, 1.36):
        K.box("door_ledge", (1.44, 0.025, 0.1), wood(3), loc=(-0.76, 0.03, z), bev=0.004, parent=Dg)
    beam(Dg, "door_brace", (-0.1, 0.03, 0.26), (-1.4, 0.03, 1.3), 0.025, 0.1, wood(3))
    for z in (0.2, 1.36):
        K.box("door_hinge", (0.25, 0.01, 0.04), _iron(), loc=(-0.12, -0.04, z), bev=0, parent=Dg)
    # the generator
    G = _grp(P, "gen", (0.0, 0.05, 0.14))
    for y in (-0.2, 0.2):
        K.box("skid", (0.9, 0.06, 0.06), _iron(), loc=(0, y, 0.03), bev=0.004, parent=G)
    K.box("engine", (0.38, 0.36, 0.32), M(GRAPHITE, metal=0.45, wear=0.25), loc=(-0.18, 0, 0.22), bev=0.02, parent=G)
    K.box("engine_head", (0.28, 0.3, 0.1), M(GUNMETAL, metal=0.5, wear=0.25), loc=(-0.2, 0, 0.43), bev=0.01, parent=G)
    K.cyl("alternator", 0.16, 0.36, _paint(BONE, metal=0.3, wear=0.25), loc=(0.2, 0, 0.24), rot=(0, 90, 0), segs=16, bev=0.015, parent=G)
    K.cyl("alt_cap", 0.12, 0.04, _iron(), loc=(0.39, 0, 0.24), rot=(0, 90, 0), segs=12, bev=0, parent=G)
    K.cyl("starter", 0.09, 0.06, M(SIGNAL, wear=0.25), loc=(-0.4, 0, 0.26), rot=(0, 90, 0), segs=12, bev=0.008, parent=G)
    K.box("starter_grip", (0.03, 0.1, 0.025), _iron(), loc=(-0.45, 0, 0.26), bev=0, parent=G)
    K.slab("tank", 0.62, 0.34, 0.15, 0.06, _paint(MUSTARD, metal=0.3, wear=0.25), loc=(0.0, 0, 0.6), bev=0.02, parent=G)
    K.cyl("tank_cap", 0.035, 0.03, _iron(), loc=(-0.15, -0.05, 0.69), segs=8, bev=0, parent=G)
    for sx in (-1, 1):
        for sy in (-1, 1):
            rod(G, "cage", (sx * 0.44, sy * 0.22, 0.06), (sx * 0.44, sy * 0.22, 0.7), 0.016, _iron(), segs=6)
        rod(G, "cage_top", (-0.44, sx * 0.22, 0.7), (0.44, sx * 0.22, 0.7), 0.016, _iron(), segs=6)
    K.box("panel", (0.2, 0.03, 0.13), _paint(BONE, wear=0.2), loc=(0.2, -0.17, 0.44), bev=0.005, parent=G)
    K.box("switch", (0.03, 0.02, 0.04), M(SIGNAL, wear=0.1), loc=(0.15, -0.19, 0.44), bev=0, parent=G)
    K.cyl("muffler", 0.065, 0.3, M(RUST, metal=0.4, wear=0.2), loc=(-0.12, 0.25, 0.45), rot=(0, 90, 0), segs=10, bev=0.01, parent=G)
    pipe = M(GRAPHITE, metal=0.5, wear=0.15, grime=0.6)
    _line(P, "exhaust", [(0.03, 0.3, 0.59), (0.55, 0.3, 0.6), (0.97, 0.3, 0.62)], 0.035, pipe, segs=8)
    K.sphere("ex_elbow", 0.036, pipe, loc=(0.97, 0.3, 0.62), parent=P)
    rod(P, "stack", (0.97, 0.3, 0.62), (0.97, 0.3, 2.05), 0.035, pipe, segs=8)
    K.box("flap", (0.1, 0.1, 0.008), pipe, loc=(0.99, 0.3, 2.07), rot=(0, -25, 0), bev=0, parent=P)
    K.box("stack_strap", (0.18, 0.03, 0.03), _iron(), loc=(0.9, 0.3, 1.25), bev=0, parent=P)
    soot = K.mat("soot", SOOT, rough=1.0)
    K.box("soot", (0.01, 0.3, 0.5), soot, loc=(0.865, 0.3, 0.75), bev=0, parent=P)
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    _line(P, "cable", [(0.2, -0.13, 0.62), (-0.4, -0.3, 0.95), (-0.86, -0.3, 0.95), (-1.0, -0.35, 0.5), (-1.15, -0.4, 0.03), (-1.8, -0.7, 0.02)], 0.012, cab)
    _jerrycan(P, "can", (1.15, 0.35, 0), -20, BRICK)
    return P


def battery_rack_settler(seed=59):
    """A settler's battery bank: a three-shelf timber rack under a tin roof, car batteries of every make jumped
    together with red and black leads, colony cells on the top shelf, a fuse box on the post and the main lead out."""
    rnd = random.Random(seed)
    P = _root("batteryrack")
    W, D, H = 1.4, 0.5, 1.65
    for sx in (-1, 1):
        for sy in (-1, 1):
            post(P, f"post{sx}{sy}", (sx * 0.67, sy * 0.22), H + 0.1, r=0.035)
    shelves = (0.18, 0.72, 1.24)
    for k, z in enumerate(shelves):
        for sy in (-1, 1):
            K.box(f"shelf{k}", (W + 0.06, 0.24, 0.03), wood(k + (sy > 0)), loc=(rnd.uniform(-0.02, 0.02), sy * 0.12, z), bev=0.004, parent=P)
    beam(P, "xb0", (-0.67, 0.25, 0.2), (0.67, 0.25, 1.2), 0.025, 0.08, wood(2))
    for sy in (-1, 1):
        K.box("batten", (W + 0.1, 0.06, 0.05), wood(3), loc=(0, sy * 0.22, H + 0.025 - sy * 0.03), bev=0.004, parent=P)
    corr(P, "roof", W + 0.3, D + 0.4, _sheet(RUST), loc=(0, 0.0, H + 0.08), rot=(-8, 0, 0))
    red, black = M(SIGNAL, wear=0.1, rough=0.6), M(GRAPHITE, wear=0.1, rough=0.6)
    for k, z in enumerate(shelves[:2]):
        zt = z + 0.015
        x = -0.66
        prev = None
        for j in range(4):
            w = rnd.uniform(0.26, 0.31)
            col = rnd.choice((BATT, BATT, BONE, SEA))
            x += w / 2 + 0.02
            h = rnd.uniform(0.18, 0.22)
            K.box(f"batt{k}{j}", (w, 0.17, h), M(col, rough=0.6, wear=0.15, grime=0.35), loc=(x, -0.02, zt + h / 2), bev=0.01, parent=P)
            pos = (x - w * 0.3, -0.06, zt + h + 0.015)
            neg = (x + w * 0.3, -0.06, zt + h + 0.015)
            K.cyl("term_p", 0.018, 0.03, red, loc=pos, segs=8, bev=0, parent=P)
            K.cyl("term_n", 0.018, 0.03, black, loc=neg, segs=8, bev=0, parent=P)
            if prev is not None:
                mid = ((prev[0] + pos[0]) / 2, -0.07, zt + h + 0.08)
                _line(P, f"jump{k}{j}", [prev, mid, pos], 0.007, red if j % 2 else black, segs=5)
            prev = neg
            x += w / 2
    for j in range(3):
        x = -0.4 + j * 0.2
        K.cyl(f"cell{j}", 0.07, 0.3, _paint(BONE, metal=0.3, wear=0.2), loc=(x, -0.02, 1.255 + 0.15), segs=14, bev=0.01, parent=P)
        K.cyl(f"cellcap{j}", 0.05, 0.03, M(STEEL, metal=0.5, wear=0.2), loc=(x, -0.02, 1.255 + 0.315), segs=12, bev=0, parent=P)
        K.cyl(f"cellband{j}", 0.072, 0.04, M(MUSTARD, wear=0.2), loc=(x, -0.02, 1.255 + 0.22), segs=14, bev=0, parent=P)
    K.box("tin", (0.18, 0.14, 0.12), _sheet(ZINC), loc=(0.35, 0.0, 1.255 + 0.06), bev=0.008, parent=P)
    K.box("fusebox", (0.16, 0.09, 0.24), _paint(GRAPHITE, metal=0.3), loc=(0.76, -0.2, 1.0), bev=0.01, parent=P)
    K.box("fuse_lever", (0.02, 0.06, 0.02), M(SIGNAL, wear=0.1), loc=(0.8, -0.27, 1.05), rot=(30, 0, 0), bev=0, parent=P)
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    _line(P, "lead_in", [(0.55, -0.06, 0.95), (0.7, -0.22, 0.92)], 0.01, cab)
    _line(P, "lead_out", [(0.76, -0.2, 0.88), (0.85, -0.3, 0.3), (1.0, -0.45, 0.02), (1.6, -0.6, 0.02)], 0.012, cab)
    return P


# ================================================================== heaps and shelves
def _pipe(P, name, a, b, r, mat):
    """A length of pipe with its dark bore showing at both ends."""
    rod(P, name, a, b, r, mat, segs=10)
    bore = K.mat("bore", "#1d1c1a", rough=1.0)
    for k, (p, q) in enumerate(((a, b), (b, a))):
        p, q = Vector(p), Vector(q)
        d = (q - p).normalized()
        ob = K.cyl(f"{name}_bore{k}", r * 0.75, 0.01, bore, segs=10, bev=0, parent=P)
        _orient(ob, p - d * 0.003, p + d * 0.007)


def scrap_heap_mixed(seed=60):
    """A heap of mixed scrap on a rust-stained mound: sheets and plates, a colony pipe and two smaller ones, an
    I-beam, a car door half buried, a wheel leaning against it all and a drum on its side."""
    rnd = random.Random(seed)
    P = _root("scrapheap")
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    rust = M(RUST, rough=0.95, wear=0.0, grime=0.6, scratches=0.0)
    lump(P, "mound", 1.0, (0, 0.05, 0), (1.2, 0.95, 0.36), soil, rnd, jitter=0.22, u=12, v=7)
    lump(P, "mound2", 0.55, (0.6, -0.35, 0), (1.0, 1.0, 0.5), soil, rnd, jitter=0.25, u=9, v=6)
    for k, (x, y) in enumerate(((-0.7, 0.45), (0.5, 0.55), (-0.2, -0.5), (0.95, -0.2))):
        lump(P, f"flakes{k}", rnd.uniform(0.22, 0.3), (x, y, 0.08), (1.4, 1.0, 0.35), rust, rnd, jitter=0.3, rot=(0, 0, rnd.uniform(0, 180)), u=8, v=5)
    car_door(P, "door", (-1.15, -0.45, 0.0), (-58, 0, 18), SEA, glass=0, frame=False)
    K.box("crate", (0.5, 0.4, 0.3), wood(1, wear=0.6), loc=(0.55, 0.62, 0.3), rot=(12, -8, 25), bev=0.015, parent=P)
    K.box("crate_board", (0.6, 0.12, 0.025), wood(2, wear=0.6), loc=(0.85, -0.85, 0.03), rot=(0, 4, 60), bev=0.004, parent=P)
    K.torus("spring", 0.07, 0.012, _iron(), loc=(-0.15, -0.85, 0.06), rot=(70, 0, 30), segs=12, parent=P)
    for k, (size, col, loc, rot) in enumerate((((0.9, 0.6, 0.025), SEA, (-0.55, -0.3, 0.33), (35, 10, 20)),
                                               ((0.75, 0.5, 0.02), BONE, (0.35, 0.25, 0.48), (-48, 15, 60)),
                                               ((1.0, 0.4, 0.03), STEEL, (0.1, -0.62, 0.22), (62, 0, -15)))):
        K.box(f"plate{k}", size, _paint(col, metal=0.35, wear=0.3, grime=0.5), loc=loc, rot=rot, bev=0.006, parent=P)
    corr(P, "sheet", 0.8, 0.65, _sheet(MUSTARD), loc=(-0.25, 0.45, 0.48), rot=(-32, 12, 30))
    _pipe(P, "pipe_big", (-1.15, -0.25, 0.1), (0.3, 0.2, 0.78), 0.075, _paint(BONE, wear=0.25, grime=0.55))
    _pipe(P, "pipe_r", (0.95, -0.65, 0.05), (-0.15, 0.25, 0.62), 0.05, M(RUST, metal=0.4, wear=0.2))
    _pipe(P, "pipe_g", (-0.6, 0.75, 0.05), (0.65, 0.45, 0.58), 0.04, M(GRAPHITE, metal=0.4, wear=0.2))
    I = _grp(P, "ibeam", (-0.25, -0.05, 0.55), (10, -22, 72))
    steel = M(STEEL, metal=0.4, wear=0.25, grime=0.5)
    K.box("web", (1.6, 0.02, 0.2), steel, bev=0, parent=I)
    for s in (-1, 1):
        K.box("flange", (1.6, 0.14, 0.02), steel, loc=(0, 0, s * 0.1), bev=0, parent=I)
    W = _grp(P, "wheel", (1.05, 0.28, 0.4), (0, -22, 32))
    tyre(W, "wheel", (0, 0, 0), 0.38, 0.2, axis="x")
    K.cyl("drum", 0.28, 0.85, _paint(BRICK, metal=0.35, wear=0.25, grime=0.55), loc=(-1.05, 0.55, 0.28), rot=(0, 90, 25), segs=16, bev=0.02, parent=P)
    for k, (x, y) in enumerate(((1.25, -0.7), (-1.3, -0.5), (0.2, 1.05))):
        _grass(P, f"grass{k}", (x, y, 0.0), 6, rnd, h=0.26)
    return P


def junk_shelf(seed=61):
    """An outdoor shelf under a tin roof: preserving jars on one shelf, tins with faded labels on another, and the
    useful parts on the rest (a gear, a coil of wire, a fan, a funnel, a gauge), a coil of rope on a side hook."""
    rnd = random.Random(seed)
    P = _root("junkshelf")
    W, D, H = 1.5, 0.42, 1.8
    for sx in (-1, 1):
        for sy in (-1, 1):
            post(P, f"post{sx}{sy}", (sx * 0.72, sy * 0.19), H + 0.1, r=0.035)
    shelves = (0.2, 0.62, 1.04, 1.46)
    for k, z in enumerate(shelves):
        K.box(f"shelf{k}", (W + 0.06, D, 0.03), wood(k), loc=(0, 0, z), rot=(0, rnd.uniform(-0.6, 0.6), 0), bev=0.004, parent=P)
    beam(P, "xb0", (-0.72, 0.22, 0.2), (0.72, 0.22, 1.46), 0.025, 0.08, wood(2))
    for sy in (-1, 1):
        K.box("batten", (W + 0.1, 0.06, 0.05), wood(3), loc=(0, sy * 0.19, H + 0.025 - sy * 0.03), bev=0.004, parent=P)
    corr(P, "roof", W + 0.3, D + 0.4, _sheet(ZINC), loc=(0, 0.0, H + 0.08), rot=(-8, 0, 0))
    ztop = [z + 0.015 for z in shelves]
    # bottom: parts
    z = ztop[0]
    K.cyl("gear", 0.13, 0.04, M(GUNMETAL, metal=0.5, wear=0.25), loc=(-0.5, -0.02, z + 0.02), segs=16, bev=0.005, parent=P)
    for k in range(12):
        a = 2 * math.pi * k / 12
        K.box("tooth", (0.04, 0.035, 0.04), M(GUNMETAL, metal=0.5, wear=0.25), loc=(-0.5 + 0.145 * math.cos(a), -0.02 + 0.145 * math.sin(a), z + 0.02),
              rot=(0, 0, math.degrees(a)), bev=0, parent=P)
    K.cyl("gear_hole", 0.03, 0.045, K.mat("bore", "#1d1c1a", rough=1.0), loc=(-0.5, -0.02, z + 0.02), segs=8, bev=0, parent=P)
    K.torus("wire_coil", 0.12, 0.03, _copper(), loc=(-0.05, 0.0, z + 0.03), segs=18, parent=P)
    K.box("bolt_tin", (0.2, 0.15, 0.1), _sheet(STEEL), loc=(0.35, -0.02, z + 0.05), bev=0.008, parent=P)
    for k in range(5):
        K.cyl("bolt", 0.012, 0.06, _iron(), loc=(0.29 + k * 0.03, -0.02 + (k % 2) * 0.03, z + 0.11), rot=(rnd.uniform(-30, 30), 50, 0), segs=6, bev=0, parent=P)
    # second: tins
    z = ztop[1]
    x = -0.66
    for k in range(7):
        r = rnd.uniform(0.045, 0.065)
        h = rnd.uniform(0.1, 0.16)
        x += r + 0.015
        y = rnd.uniform(-0.06, 0.04)
        K.cyl(f"tin{k}", r, h, _sheet(ZINC, wear=0.2), loc=(x, y, z + h / 2), segs=10, bev=0, parent=P)
        K.cyl(f"label{k}", r + 0.002, h * 0.55, M(rnd.choice((SEA, BRICK, MUSTARD, BONE)), wear=0.3, rough=0.8, scratches=0.2),
              loc=(x, y, z + h / 2), segs=10, bev=0, parent=P)
        if k in (1, 4):
            K.cyl(f"tin_up{k}", r, h * 0.8, _sheet(ZINC, wear=0.2), loc=(x, y, z + h + h * 0.4), segs=10, bev=0, parent=P)
        x += r
    # third: jars
    z = ztop[2]
    for k in range(7):
        x = -0.6 + k * 0.19 + rnd.uniform(-0.02, 0.02)
        h = rnd.uniform(0.13, 0.18)
        col = rnd.choice(("#8a3a32", "#b8913f", "#5e7f3a", "#6f4a52", JAR))
        y = rnd.uniform(-0.05, 0.05)
        K.cyl(f"jar{k}", 0.055, h, K.mat(f"jar{col}", col, rough=0.2, metal=0.1, grime=0.25), loc=(x, y, z + h / 2), segs=10, bev=0, parent=P)
        K.cyl(f"jarlid{k}", 0.05, 0.025, _sheet(ZINC, wear=0.2), loc=(x, y, z + h + 0.012), segs=10, bev=0, parent=P)
    # top: a fan, a gauge, a funnel
    z = ztop[3]
    F = _grp(P, "fan", (-0.45, 0.02, z + 0.2), (0, 0, 15))
    K.torus("fan_ring", 0.17, 0.015, _paint(BONE, wear=0.2), rot=(90, 0, 0), segs=20, parent=F)
    K.cyl("fan_hub", 0.04, 0.06, _iron(), rot=(90, 0, 0), segs=10, bev=0, parent=F)
    for k in range(4):
        K.box("fan_blade", (0.07, 0.01, 0.14), _paint(BRICK, wear=0.25), loc=(0.085 * math.sin(math.radians(90 * k)), 0, 0.085 * math.cos(math.radians(90 * k))),
              rot=(0, 90 * k, 20), bev=0, parent=F)
    K.box("fan_foot", (0.18, 0.12, 0.03), _iron(), loc=(-0.45, 0.02, z + 0.015), bev=0, parent=P)
    rod(P, "fan_stem", (-0.45, 0.02, z + 0.02), (-0.45, 0.02, z + 0.2), 0.012, _iron(), segs=6)
    K.cyl("gauge", 0.08, 0.05, _iron(), loc=(0.05, -0.02, z + 0.08), rot=(80, 0, 0), segs=14, bev=0.005, parent=P)
    K.cyl("gauge_face", 0.068, 0.01, _paint(BONE, wear=0.1), loc=(0.05, -0.05, z + 0.085), rot=(80, 0, 0), segs=14, bev=0, parent=P)
    K.cyl("funnel", 0.13, 0.15, _sheet(SEA, wear=0.2), loc=(0.45, 0.0, z + 0.14), segs=12, r2=0.02, rot=(180, 0, 0), bev=0, parent=P)
    rod(P, "funnel_spout", (0.45, 0.0, z + 0.06), (0.45, 0.0, z), 0.018, _sheet(SEA, wear=0.2), segs=6)
    K.torus("rope_coil", 0.16, 0.025, rope_mat(), loc=(0.8, 0.0, 1.2), rot=(0, 90, 0), segs=18, parent=P)
    K.box("hook", (0.06, 0.02, 0.02), _iron(), loc=(0.77, 0.0, 1.37), bev=0, parent=P)
    return P


# ================================================================== camp fittings
def rope_bridge_post(seed=62):
    """The near end of a rope bridge that is gone: two thick anchor posts in a heap of stones, guyed back to stakes,
    a sill log lashed across them, the hand ropes lashed round their tops and the foot ropes with a few boards still
    on them lying slack in the grass."""
    rnd = random.Random(seed)
    P = _root("bridgepost")
    for s in (-1, 1):
        _logpost(P, f"post{s}", s * 0.7, 0, 1.55, r=0.15, lean=(0, -0.06), segs=10)
        _lash(P, (s * 0.7, -0.052, 1.35), 0.15, turns=3, step=0.035)
        _lash(P, (s * 0.7, -0.02, 0.48), 0.15, turns=2, step=0.04)
        for k in range(3):
            a = math.radians(rnd.uniform(0, 360))
            lump(P, f"stone{s}{k}", rnd.uniform(0.13, 0.18), (s * 0.7 + 0.24 * math.cos(a), 0.24 * math.sin(a), 0.05), (1.2, 1.0, 0.8), _stone(), rnd,
                 flat=0.4, u=8, v=6)
        _stake(P, f"stake{s}", s * 0.85, -1.4, 0.45, 0.06, lean=(0, 0.12), segs=7)
        _line(P, f"guy{s}", [(s * 0.7, -0.15, 1.36), (s * 0.82, -1.27, 0.4)], 0.018, rope_mat())
    _log(P, "sill", (-0.95, 0.15, 0.46), (0.95, 0.15, 0.48), 0.09)
    for s in (-1, 1):
        hand = [(s * 0.7, 0.1, 1.35), (s * 0.68, 0.55, 1.05), (s * 0.63, 0.95, 0.6), (s * 0.6, 1.2, 0.15), (s * 0.56, 1.55, 0.03), (s * 0.5, 2.05, 0.03)]
        _line(P, f"hand{s}", hand, 0.022, rope_mat())
        K.cyl(f"fray{s}", 0.03, 0.08, rope_mat(), loc=(s * 0.5, 2.11, 0.03), rot=(-90, 0, 0), segs=6, r2=0.012, bev=0, parent=P)
        foot = [(s * 0.45, 0.22, 0.48), (s * 0.45, 0.6, 0.05), (s * 0.43, 1.3, 0.04), (s * 0.4, 1.95, 0.035)]
        _line(P, f"foot{s}", foot, 0.022, rope_mat())
    for k, (y, rz, ln) in enumerate(((0.8, 3, 1.05), (1.15, -4, 1.05), (1.5, 18, 0.7))):
        K.box(f"board{k}", (ln, 0.2, 0.03), wood(k, wear=0.6), loc=(0.02 * k - (0.12 if k == 2 else 0), y, 0.075), rot=(0, 0, rz), bev=0.006, parent=P)
    return P


def outdoor_shower(seed=63):
    """A bucket shower in a three-sided plank screen: four posts and a top frame, a holed bucket on the cross bar with
    a rose and a pull cord, a pallet floor on stones, a curtain half drawn across the front, a towel over the side and
    soap on a little shelf."""
    rnd = random.Random(seed)
    P = _root("shower")
    a, H = 0.6, 2.4
    for sx in (-1, 1):
        for sy in (-1, 1):
            post(P, f"post{sx}{sy}", (sx * a, sy * a), H + 0.1, r=0.045)
    for s in (-1, 1):
        K.box("top_x", (1.32, 0.07, 0.07), wood(2), loc=(0, s * a, H - 0.035), bev=0.006, parent=P)
        K.box("top_y", (0.07, 1.32, 0.07), wood(2), loc=(s * a, 0, H - 0.1), bev=0.006, parent=P)
    K.box("crossbar", (1.32, 0.09, 0.09), wood(3), loc=(0, 0, H + 0.01), bev=0.008, parent=P)
    _bay(P, "back", "planks", (0, a + 0.02), 180, 1.2, 1.65, None, rnd, z0=0.28)
    _bay(P, "left", "planks", (-a - 0.02, 0), -90, 1.2, 1.65, None, rnd, z0=0.28)
    _bay(P, "right", "planks", (a + 0.02, 0), 90, 1.2, 1.65, None, rnd, z0=0.28)
    # bucket, rose and cord
    K.torus("hook", 0.035, 0.008, _iron(), loc=(0, 0, H - 0.07), rot=(90, 0, 0), segs=10, parent=P)
    K.torus("bail", 0.16, 0.008, _iron(), loc=(0, 0, 2.08), rot=(90, 0, 0), arc=180, segs=14, parent=P)
    K.cyl("bucket", 0.135, 0.3, _sheet(ZINC, wear=0.2), loc=(0, 0, 1.93), segs=16, r2=0.16, bev=0.008, parent=P)
    K.cyl("bucket_rim", 0.163, 0.02, _sheet(ZINC, wear=0.2), loc=(0, 0, 2.08), segs=16, bev=0, parent=P)
    rod(P, "rose_pipe", (0, 0, 1.78), (0, 0, 1.7), 0.02, _iron(), segs=8)
    K.cyl("rose", 0.07, 0.04, _sheet(STEEL), loc=(0, 0, 1.68), segs=14, r2=0.03, rot=(180, 0, 0), bev=0, parent=P)
    K.box("valve_arm", (0.16, 0.015, 0.015), _iron(), loc=(0.07, 0, 1.76), bev=0, parent=P)
    _line(P, "cord", [(0.15, 0, 1.76), (0.15, -0.02, 1.25)], 0.006, rope_mat(), segs=4)
    K.torus("cord_ring", 0.03, 0.008, _iron(), loc=(0.15, -0.02, 1.22), rot=(90, 0, 0), segs=10, parent=P)
    # pallet floor on stones
    for k, x in enumerate((-0.45, 0.45)):
        for y in (-0.4, 0.4):
            lump(P, f"stone{k}{y}", 0.1, (x, y, 0.03), (1.2, 1.0, 0.7), _stone(), rnd, flat=0.4, u=8, v=6)
        K.box(f"bearer{k}", (0.09, 1.1, 0.08), wood(3), loc=(x, 0, 0.12), bev=0.006, parent=P)
    for k in range(6):
        K.box(f"slat{k}", (1.1, 0.14, 0.025), wood(k % 4), loc=(0, -0.45 + k * 0.18, 0.172), bev=0.004, parent=P)
    # curtain on a rail, half drawn
    rod(P, "curtain_rail", (-a, -a - 0.02, 2.05), (a, -a - 0.02, 2.05), 0.012, _iron(), segs=6)
    corr(P, "curtain", 0.62, 1.55, M(SEA, wear=0.1, rough=0.9, grime=0.35, scratches=0), loc=(-0.3, -a - 0.05, 1.27), rot=(90, 0, 0), pitch=0.2,
         amp=0.045, t=0.008)
    # towel and soap
    for s in (-1, 1):
        K.box(f"towel{s}", (0.02, 0.4, 0.42), M(BONE, wear=0.1, rough=0.95, grime=0.35), loc=(-a - 0.02 + s * 0.05, 0.15, 1.75), bev=0.008, parent=P)
    K.box("towel_top", (0.12, 0.4, 0.03), M(BONE, wear=0.1, rough=0.95, grime=0.35), loc=(-a - 0.02, 0.15, 1.95), bev=0.01, parent=P)
    K.box("soap_shelf", (0.08, 0.25, 0.025), wood(2), loc=(a - 0.07, 0.25, 1.2), bev=0.004, parent=P)
    K.box("soap", (0.06, 0.09, 0.035), M(MUSTARD, wear=0, rough=0.4, grime=0.1), loc=(a - 0.07, 0.25, 1.23), bev=0.012, parent=P)
    return P


def latrine_screen(seed=64):
    """A pit latrine behind a screen of four corrugated sheets on stakes, the way in round the open corner: a board
    cover over the pit with its hole and lid, a bucket of ash, a soil heap with the spade stuck in it."""
    rnd = random.Random(seed)
    P = _root("latrine")
    e, H = 0.7, 1.72
    for k, (x, y) in enumerate(((-e, -e), (-e, e), (e, e), (e, -0.18), (0.1, -e))):
        _logpost(P, f"stake{k}", x, y, H + 0.06, r=0.045, lean=(rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03)))
    zc = 0.05 + H / 2
    corr(P, "back", 1.48, H, _sheet(ZINC), loc=(0, e + 0.055, zc), rot=(90, 0.8, 180))
    corr(P, "left", 1.48, H - 0.05, _sheet(RUST), loc=(-e - 0.055, 0, zc - 0.02), rot=(90, -0.6, -90))
    corr(P, "right", 0.95, H - 0.08, _sheet(SEA), loc=(e + 0.055, 0.27, zc - 0.03), rot=(90, 1.0, 90))
    corr(P, "front", 0.9, H - 0.12, _sheet(BRICK), loc=(-0.3, -e - 0.055, zc - 0.05), rot=(90, -1.2, 0))
    for z in (0.4, 1.5):
        K.box("tie_b", (0.06, 0.14, 0.012), _iron(), loc=(-e, e + 0.03, z), bev=0, parent=P)
        K.box("tie_c", (0.14, 0.06, 0.012), _iron(), loc=(e + 0.03, e, z), bev=0, parent=P)
        K.box("tie_f", (0.06, 0.14, 0.012), _iron(), loc=(0.1, -e - 0.03, z), bev=0, parent=P)
    for k, y in enumerate((-0.3, 0.0, 0.3)):
        K.box(f"cover{k}", (1.0, 0.28, 0.04), wood(k, wear=0.5), loc=(0.0, y + 0.1, 0.06), rot=(0, 0, rnd.uniform(-1.5, 1.5)), bev=0.006, parent=P)
    K.cyl("hole", 0.12, 0.012, K.mat("pit", "#151311", rough=1.0), loc=(0.0, 0.1, 0.081), segs=14, bev=0, parent=P)
    K.box("lid", (0.32, 0.32, 0.025), wood(3), loc=(0.3, 0.32, 0.095), rot=(0, 0, 14), bev=0.006, parent=P)
    K.box("lid_grip", (0.12, 0.03, 0.03), wood(2), loc=(0.3, 0.32, 0.12), rot=(0, 0, 14), bev=0.004, parent=P)
    _bucket(P, "ash", (-0.42, 0.38, 0.0), col=GRAPHITE, r=0.14, h=0.26)
    K.cyl("ash_in", 0.125, 0.01, K.mat("ash", "#5c5852", rough=1.0), loc=(-0.42, 0.38, 0.24), segs=14, bev=0, parent=P)
    rod(P, "scoop", (-0.42, 0.38, 0.2), (-0.36, 0.3, 0.42), 0.012, wood(3), segs=5)
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    lump(P, "spoil", 0.35, (1.05, -0.7, 0.0), (1.3, 1.0, 0.45), soil, rnd, jitter=0.25, u=10, v=6)
    lump(P, "spoil2", 0.22, (1.3, -0.45, 0.0), (1.2, 1.0, 0.5), soil, rnd, jitter=0.25, u=8, v=5)
    S = _grp(P, "spade", (1.05, -0.72, 0.12), (12, -10, 20))
    K.box("spade_blade", (0.2, 0.02, 0.26), _iron(), loc=(0, 0, 0.0), bev=0.004, parent=S)
    rod(S, "spade_shaft", (0, 0, 0.12), (0, 0, 0.95), 0.018, wood(3), segs=6)
    K.box("spade_grip", (0.14, 0.03, 0.03), wood(3), loc=(0, 0, 0.97), bev=0.004, parent=S)
    return P


def bell_post(seed=65):
    """A settlement bell: an old bronze bell on a yoke under the braced arm of a square timber post with a little board
    roof, its clapper rope hanging to hand height, stones packed round the post's foot."""
    rnd = random.Random(seed)
    P = _root("bellpost")
    K.box("post", (0.2, 0.2, 2.75), wood(3), loc=(0, 0, 1.3), bev=0.015, parent=P)
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        lump(P, f"stone{k}", rnd.uniform(0.12, 0.16), (0.24 * math.cos(a), 0.24 * math.sin(a), 0.04), (1.2, 1.0, 0.8), _stone(), rnd, flat=0.4, u=8, v=6)
    K.box("arm", (0.95, 0.14, 0.16), wood(2), loc=(0.42, 0, 2.5), bev=0.012, parent=P)
    beam(P, "strut", (0.1, 0, 1.85), (0.62, 0, 2.42), 0.1, 0.1, wood(1))
    for s in (-1, 1):
        K.box(f"cap{s}", (1.2, 0.2, 0.025), wood(1), loc=(0.35, s * 0.085, 2.64), rot=(-s * 25, 0, 0), bev=0.005, parent=P)
    bx = 0.65
    bronze = M(BELL, metal=0.7, rough=0.35, wear=0.2, grime=0.45)
    K.box("yoke", (0.12, 0.18, 0.08), _iron(), loc=(bx, 0, 2.38), bev=0.008, parent=P)
    for s in (-1, 1):
        FR.screw(P, (bx, s * 0.075, 2.58), (90, 0, 0), r=0.022, mat=_iron())
    for s in (-1, 1):
        K.box("yoke_strap", (0.03, 0.02, 0.3), _iron(), loc=(bx, s * 0.08, 2.48), bev=0, parent=P)
    zb = 1.88
    outer = [(0.25, 0.0), (0.245, 0.035), (0.205, 0.1), (0.17, 0.2), (0.155, 0.3), (0.14, 0.38), (0.09, 0.43), (0.03, 0.445)]
    inner = [(0.02, 0.405), (0.08, 0.395), (0.12, 0.35), (0.135, 0.28), (0.15, 0.18), (0.185, 0.09), (0.215, 0.02), (0.22, 0.0)]
    lathe("bell", outer + inner, bronze, segs=22, loc=(bx, 0, zb), parent=P)
    K.cyl("canon", 0.045, 0.05, bronze, loc=(bx, 0, zb + 0.46), segs=10, bev=0, parent=P)
    rod(P, "clapper_rod", (bx, 0, zb + 0.4), (bx, 0, zb - 0.02), 0.012, _iron(), segs=6)
    K.sphere("clapper", 0.045, _iron(), loc=(bx, 0, zb - 0.03), parent=P)
    _line(P, "pull", [(bx, 0, zb - 0.07), (bx - 0.02, -0.02, 1.08)], 0.012, rope_mat())
    K.sphere("pull_knot", 0.03, rope_mat(), loc=(bx - 0.02, -0.02, 1.06), parent=P)
    K.box("cleat", (0.04, 0.05, 0.16), wood(2), loc=(0.12, 0, 1.25), bev=0.006, parent=P)
    return P


PROPS = {
    "lean_to_metal": lean_to_metal,
    "scrap_hut": scrap_hut,
    "lookout_platform": lookout_platform,
    "watchtower_wood": watchtower_wood,
    "palisade_section": palisade_section,
    "palisade_gate": palisade_gate,
    "barricade_scrap": barricade_scrap,
    "tire_stack": tire_stack,
    "tire_swing": tire_swing,
    "car_door_fence": car_door_fence,
    "sheet_metal_fence": sheet_metal_fence,
    "signboard_painted": signboard_painted,
    "scrap_windmill_turbine": scrap_windmill_turbine,
    "solar_scrap_rig": solar_scrap_rig,
    "antenna_scrap": antenna_scrap,
    "rain_catcher": rain_catcher,
    "water_still": water_still,
    "generator_shed": generator_shed,
    "battery_rack_settler": battery_rack_settler,
    "scrap_heap_mixed": scrap_heap_mixed,
    "junk_shelf": junk_shelf,
    "rope_bridge_post": rope_bridge_post,
    "outdoor_shower": outdoor_shower,
    "latrine_screen": latrine_screen,
    "bell_post": bell_post,
}
