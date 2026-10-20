"""Colony Works street props for the scenes between places (docs/world-generation.md, Scenes): the old colony town's
street furniture, left standing, toppled or gutted where it was when the colony fell (docs/art-direction.md).

Lights and control: a street lamp still standing, a traffic light upright and toppled, a checkpoint boom barrier and a
toll booth. Furniture: a bench, a bin with its lid, a row of recycling bins, a news kiosk with its shutters down, a
comm booth, a dead vending machine, a hydrant, an open manhole, a planter, a row of bollards, a bike rack, a parking
meter and an information board. Civic: the colony flag, the founder's statue, a dry fountain and a dead tree in its
planter. Road: an overhead sign gantry, a crossing sign and a gutted colony bus.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy

import kit as K
import frame as FR
from families import M, shell_panel, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, RUST, ZINC, CONCRETE, GUNMETAL
from props import _root, wood, bolt
from props_colony_a import stencil, nut, _plate, _concrete, _steel, _dark, _zinc, GLASS, GLASS_D
from props_colony_b import beam, rod, plate_xz, lathe, tyre, rust_patch, _grp, _sign_post, _sign_back, _poly
from props_stone import limb, moss, earth, tapered

RED_D, AMBER_D, GREEN_D = "#5a2422", "#5e4d22", "#21463a"   # dead signal lenses
CRACK = "#9fb3bd"        # crazed glass
SOIL, LEAF_D = "#5a3f2a", "#8a6a44"   # planter soil and dry leaves
DEADWOOD = "#7d7468"     # a dead tree gone grey
MAP_BG, MAP_LAND = "#cfcab4", "#8fa36a"   # the information board's map


def _glass():
    return K.mat("streetglass", GLASS, rough=0.1, metal=0.1, wear=0.2, grime=0.45)


def _paint(col, wear=0.5, **kw):
    kw.setdefault("grime", 0.5)
    kw.setdefault("scratches", 0.5)
    return M(col, wear=wear, **kw)


def _cracks(P, loc, rnd, n=3, L=0.3, face="y", mat=None):
    """A star of cracks on a pane facing -y (or -x)."""
    mat = mat or K.mat("crack", CRACK, rough=0.3)
    x, y, z = loc
    for k in range(n):
        a = rnd.uniform(0, 360) if k == 0 else a + 360 / n + rnd.uniform(-25, 25)
        ll = L * rnd.uniform(0.6, 1.1)
        c, s = math.cos(math.radians(a)) * ll / 2, math.sin(math.radians(a)) * ll / 2
        if face == "y":
            K.box("crack", (ll, 0.006, 0.008), mat, loc=(x + c, y, z + s), rot=(0, -a, 0), bev=0, parent=P)
        else:
            K.box("crack", (0.006, ll, 0.008), mat, loc=(x, y + c, z + s), rot=(a, 0, 0), bev=0, parent=P)


def _shards(P, c, r, n, rnd, mat=None):
    """Broken glass lying in the grass round c."""
    mat = mat or _glass()
    for k in range(n):
        s = rnd.uniform(0.05, 0.12)
        _plate(f"shard{k}", [(0, 0), (s, rnd.uniform(-0.02, 0.03)), (rnd.uniform(0.2, 0.7) * s, s * 0.8)], 0.008, mat, P,
               loc=(c[0] + rnd.uniform(-r, r), c[1] + rnd.uniform(-r, r), 0.008), rot=(rnd.uniform(-5, 5), rnd.uniform(-5, 5), rnd.uniform(0, 360)))


def _leaves(P, c, r, n, rnd, z=0.01):
    leaf = K.mat("dryleaf", LEAF_D, rough=0.95, grime=0.2)
    for k in range(n):
        s = rnd.uniform(0.04, 0.07)
        _plate(f"leaf{k}", [(0, 0), (s, s * 0.3), (s * 1.6, 0), (s, -s * 0.3)], 0.004, leaf, P,
               loc=(c[0] + rnd.uniform(-r, r), c[1] + rnd.uniform(-r, r), z), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 360)))


# ================================================================== lights and control
def street_lamp(seed=41):
    """A colony street lamp, 6 m, still standing: a square post on a framed base, a braced arm and a pill-cornered
    head, its cover grey with dirt and its lamp long dead."""
    rnd = random.Random(seed)
    P = _root("lamp")
    gra = M(GRAPHITE, metal=0.3, wear=0.3, grime=0.4)
    x0, H = -0.55, 5.0
    K.box("footing", (0.8, 0.8, 0.2), _concrete(0.3), loc=(x0, 0, 0.06), bev=0.03, parent=P)
    FR.framed_box(P, "base", (x0, 0, 0.55), (0.5, 0.5, 0.8), M(STEEL, wear=0.3, grime=0.5), gra, fw=0.07, screws=_zinc())
    K.box("hatch", (0.26, 0.02, 0.4), M(STEEL, wear=0.3), loc=(x0, -0.255, 0.55), bev=0.005, parent=P)
    stencil(P, "L-14", (x0, -0.266, 0.68), 0.06, M(BONE, wear=0.3, scratches=0), extrude=0.003)
    FR.hazard(P, (x0, -0.27, 0.22), 0.44, 0.1, M(MUSTARD, wear=0.3), gra, n=5)
    K.box("pole", (0.2, 0.2, H), gra, loc=(x0, 0, 0.95 + H / 2), bev=0.025, parent=P)
    K.box("collar", (0.26, 0.26, 0.12), _steel(), loc=(x0, 0, 1.01), bev=0.02, parent=P)
    K.box("band", (0.205, 0.205, 0.1), M(MUSTARD, wear=0.3), loc=(x0, 0, 2.6), bev=0.01, parent=P)
    zt = 0.95 + H - 0.12
    K.box("cap", (0.24, 0.24, 0.06), _steel(), loc=(x0, 0, 0.95 + H + 0.03), bev=0.012, parent=P)
    K.box("arm", (1.3, 0.16, 0.16), gra, loc=(x0 + 0.6, 0, zt), bev=0.025, parent=P)
    beam(P, "brace", (x0 + 0.1, 0, zt - 0.65), (x0 + 0.75, 0, zt - 0.08), 0.06, gra)
    K.box("knee", (0.2, 0.2, 0.2), _steel(), loc=(x0 + 1.2, 0, zt), bev=0.03, parent=P)
    hx, hz = x0 + 1.55, zt - 0.02
    bone = M(BONE, wear=0.3, grime=0.5, scratches=0.5)
    K.slab("hood", 0.8, 0.44, 0.15, 0.05, bone, loc=(hx, 0, hz), bev=0.02, parent=P)
    K.box("hood_ridge", (0.7, 0.12, 0.05), _steel(), loc=(hx, 0, hz + 0.09), bev=0.01, parent=P)
    K.box("hood_rim", (0.84, 0.48, 0.05), _steel(), loc=(hx, 0, hz - 0.1), bev=0.012, parent=P)
    cover = K.mat("lampglass", GLASS, rough=0.12, metal=0.0, wear=0.2, grime=0.4)
    K.box("cover", (0.74, 0.38, 0.03), cover, loc=(hx, 0, hz - 0.14), bev=0.004, parent=P)
    K.box("reflector", (0.7, 0.4, 0.02), K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(hx, 0, hz - 0.105), bev=0, parent=P)
    for k in range(2):
        K.box("crack", (0.25, 0.008, 0.032), _dark(), loc=(hx + 0.12 - k * 0.1, 0.02 * k, hz - 0.15), rot=(0, 0, 30 + k * 70), bev=0, parent=P)
    for k in range(3):
        nut(P, (hx - 0.3 + k * 0.3, -0.245, hz - 0.1), r=0.018, rot=(90, 0, 0))
    _leaves(P, (x0, 0), 0.6, 4, rnd)
    return P


def _signal(P, H, base=True, zb=1.15, fallen=False):
    """A colony traffic signal standing on P's origin: square steel pole, push-button box, a graphite three-lamp head on
    a bone backplate, the lenses dark under their visors."""
    paint = M(STEEL, metal=0.3, wear=0.3, grime=0.45)
    gra = _dark()
    if base:
        K.box("base_plate", (0.4, 0.4, 0.03), gra, loc=(0, 0, 0.015), bev=0.006, parent=P)
        for a in (-1, 1):
            for b in (-1, 1):
                nut(P, (a * 0.15, b * 0.15, 0.045))
        K.box("collar", (0.21, 0.21, 0.25), paint, loc=(0, 0, 0.155), bev=0.02, parent=P)
    K.box("pole", (0.15, 0.15, H), paint, loc=(0, 0, H / 2), bev=0.02, parent=P)
    K.box("pole_cap", (0.19, 0.19, 0.04), gra, loc=(0, 0, H + 0.02), bev=0.008, parent=P)
    K.box("button_box", (0.14, 0.09, 0.22), M(MUSTARD, wear=0.3, grime=0.4), loc=(0, -0.12, zb), bev=0.015, parent=P)
    K.cyl("button", 0.03, 0.02, gra, loc=(0, -0.17, zb + 0.03), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    K.box("button_plate", (0.08, 0.006, 0.05), M(BONE, wear=0.3), loc=(0, -0.166, zb - 0.06), bev=0, parent=P)
    zc = H - 0.55
    for dz in (0.38, -0.38):
        K.box("bracket", (0.08, 0.07, 0.08), gra, loc=(0, -0.11, zc + dz), bev=0.01, parent=P)
    K.box("backplate", (0.52, 0.03, 1.22), M(BONE, wear=0.3, grime=0.5), loc=(0, -0.16, zc), bev=0.02, parent=P)
    K.box("housing", (0.34, 0.24, 1.04), gra, loc=(0, -0.295, zc), bev=0.03, parent=P)
    for k, (dz, col) in enumerate(((0.33, RED_D), (0.0, AMBER_D), (-0.33, GREEN_D))):
        z = zc + dz
        if fallen and k == 1:   # this lens smashed: a dark hole and its socket
            K.cyl("socket", 0.1, 0.02, K.mat("hole", "#141517", rough=0.9), loc=(0, -0.42, z), rot=(90, 0, 0), segs=14, bev=0, parent=P)
        else:
            K.cyl(f"lens{k}", 0.105, 0.03, K.mat(f"lens{col}", col, rough=0.15, metal=0.1, grime=0.3), loc=(0, -0.43, z), rot=(90, 0, 0), segs=16, bev=0.008, parent=P)
        K.torus(f"lens_rim{k}", 0.115, 0.012, gra, loc=(0, -0.42, z), rot=(90, 0, 0), segs=16, parent=P)
        if not (fallen and k == 1):
            shell_panel(f"visor{k}", 0.135, 0.0, math.pi, -0.6, -0.415, gra, t=0.012, parent=P, loc=(0, 0, z))
    for dz in (-0.5, 0.5):
        nut(P, (0.12, -0.42, zc + dz), r=0.015, rot=(90, 0, 0))
        nut(P, (-0.12, -0.42, zc + dz), r=0.015, rot=(90, 0, 0))


def traffic_light(seed=42):
    """A colony traffic light, 3.9 m: a square steel pole on a bolted plate, a push-button box at hand height and a
    three-lamp head, dark since the fall."""
    P = _root("signal")
    _signal(P, 3.4)
    K.box("pad", (0.6, 0.6, 0.04), _concrete(0.3), loc=(0, 0, 0.0), bev=0.01, parent=P)
    return P


def traffic_light_fallen(seed=43):
    """The same traffic light snapped off above its collar and lying in the road, its head on top, one lens smashed,
    the cable hanging out of the stump."""
    rnd = random.Random(seed)
    P = _root("signal_fallen")
    paint = M(STEEL, metal=0.3, wear=0.3, grime=0.45)
    sx = -1.6
    S = _grp(P, "stump", (sx, 0, 0))
    K.box("pad", (0.6, 0.6, 0.04), _concrete(0.35), loc=(0, 0, 0.0), bev=0.01, parent=S)
    K.box("base_plate", (0.4, 0.4, 0.03), _dark(), loc=(0, 0, 0.015), bev=0.006, parent=S)
    for a in (-1, 1):
        for b in (-1, 1):
            nut(S, (a * 0.15, b * 0.15, 0.045))
    K.box("collar", (0.21, 0.21, 0.25), paint, loc=(0, 0, 0.155), bev=0.02, parent=S)
    K.box("stub", (0.15, 0.15, 0.36), paint, loc=(0, 0, 0.3), bev=0.02, parent=S)
    # torn tongues of the pole wall bent outwards
    for k, (dx, dy, ry, rx) in enumerate(((0.07, 0, 25, 0), (-0.07, 0, -35, 0), (0, -0.07, 0, -20))):
        _plate(f"tongue{k}", [(-0.06, 0), (0.06, 0), (0.02, 0.12), (-0.01, 0.08)], 0.01, paint, S, loc=(dx, dy, 0.47),
               rot=(90 + rx, ry, 90 if dx else 0))
    rod(S, "cable0", (0.0, 0.0, 0.4), (0.12, -0.15, 0.62), 0.012, K.mat("cable", "#2b2d30", rough=0.6), segs=6)
    rod(S, "cable1", (0.12, -0.15, 0.62), (0.3, -0.25, 0.05), 0.012, K.mat("cable", "#2b2d30", rough=0.6), segs=6)
    # the pole lies along +x on its side, propped up a little by the head, which faces the road
    G = _grp(P, "fallen", (sx + 0.42, 0.04, 0.076), (0, 86.3, -2))
    _signal(G, 3.0, base=False, zb=0.7, fallen=True)
    for k, (dx, ry) in enumerate(((0.07, -30), (-0.07, 40))):
        _plate(f"torn{k}", [(-0.06, 0), (0.06, 0), (0.0, -0.1)], 0.01, paint, G, loc=(dx, 0, 0.0), rot=(90, ry, 90))
    _shards(P, (sx + 2.6, -0.3), 0.5, 6, rnd, mat=K.mat(f"lens{AMBER_D}", AMBER_D, rough=0.15, metal=0.1, grime=0.3))
    return P


def road_barrier_arm(seed=44):
    """A checkpoint boom barrier: a framed control box with its pivot and counterweight, a 4.4 m striped boom down
    across the lane, resting in a forked post."""
    rnd = random.Random(seed)
    P = _root("boom")
    steel = _steel()
    gra = _dark()
    bx = -2.0
    K.box("pad", (0.9, 0.8, 0.12), _concrete(0.3), loc=(bx, 0.05, 0.04), bev=0.02, parent=P)
    FR.framed_box(P, "box", (bx, 0.05, 0.65), (0.55, 0.45, 1.06), M(BONE, wear=0.3, grime=0.5), steel, fw=0.07, screws=_zinc())
    FR.hazard(P, (bx, -0.19, 0.3), 0.42, 0.12, M(MUSTARD, wear=0.3), gra, n=5)
    FR.vent(P, (bx, -0.19, 0.62), 0.3, 0.22, steel, gra, n=3)
    K.box("hood", (0.62, 0.52, 0.06), steel, loc=(bx, 0.05, 1.21), bev=0.015, parent=P)
    K.cyl("beacon", 0.07, 0.08, gra, loc=(bx, 0.05, 1.28), segs=12, bev=0.005, parent=P)
    K.sphere("beacon_dome", 0.065, K.mat(f"lens{RED_D}", RED_D, rough=0.15, metal=0.1, grime=0.3), loc=(bx, 0.05, 1.32), scale=(1, 1, 0.8), parent=P)
    # pivot, counterweight and the boom
    zb, yb = 1.0, -0.3
    K.cyl("pivot", 0.13, 0.12, gra, loc=(bx, -0.21, zb), rot=(90, 0, 0), segs=16, bev=0.01, parent=P)
    K.cyl("pivot_cap", 0.06, 0.18, steel, loc=(bx, -0.24, zb), rot=(90, 0, 0), segs=10, bev=0.008, parent=P)
    K.box("clamp", (0.3, 0.12, 0.2), steel, loc=(bx + 0.05, yb, zb), bev=0.02, parent=P)
    K.box("counterweight", (0.5, 0.14, 0.24), gra, loc=(bx - 0.4, yb, zb), bev=0.02, parent=P)
    n, L = 9, 4.4
    for k in range(n):
        K.box(f"boom{k}", (L / n, 0.08, 0.12), M(SIGNAL if k % 2 == 0 else BONE, wear=0.3, grime=0.45, scratches=0.5),
              loc=(bx + 0.2 + L / n * (k + 0.5), yb, zb), bev=0.008, parent=P)
    K.box("boom_tip", (0.04, 0.09, 0.13), gra, loc=(bx + 0.2 + L + 0.02, yb, zb), bev=0.005, parent=P)
    # forked rest post at the far end
    rx = 2.2
    K.box("rest_foot", (0.3, 0.3, 0.04), _concrete(0.3), loc=(rx, yb, 0.02), bev=0.01, parent=P)
    K.box("rest_post", (0.08, 0.08, 0.88), steel, loc=(rx, yb, 0.48), bev=0.01, parent=P)
    K.box("rest_fork", (0.08, 0.2, 0.03), steel, loc=(rx, yb, 0.93), bev=0.005, parent=P)
    for s in (-1, 1):
        K.box(f"rest_tine{s}", (0.08, 0.02, 0.16), steel, loc=(rx, yb + s * 0.07, 1.0), bev=0.004, parent=P)
    K.box("rest_band", (0.085, 0.085, 0.1), M(MUSTARD, wear=0.3), loc=(rx, yb, 0.65), bev=0.005, parent=P)
    for k in range(2):
        rust_patch(P, (bx + 1.2 + k * 1.9, yb - 0.042, zb + rnd.uniform(-0.03, 0.03)), (0.15, 0.004, 0.06))
    return P


def toll_booth(seed=45):
    """A one-person colony booth on a concrete island: steel corner posts, bone panels below a band of windows (one
    pane cracked, the service slider half open), a door at the side and a pill-cornered roof with a dead beacon."""
    rnd = random.Random(seed)
    P = _root("booth")
    steel = _steel()
    gra = _dark()
    bone = M(BONE, wear=0.3, grime=0.5, scratches=0.5)
    glass = _glass()
    K.box("island", (2.4, 3.0, 0.15), _concrete(0.25), loc=(0, 0, 0.075), bev=0.03, parent=P)
    for s in (-1, 1):
        FR.hazard(P, (0, s * 1.51, 0.075), 2.3, 0.11, M(MUSTARD, wear=0.3), gra, n=9)
    z0, W, D, H = 0.15, 1.6, 1.4, 2.4
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("post", (0.1, 0.1, H), steel, loc=(a * W / 2, b * D / 2, z0 + H / 2), bev=0.015, parent=P)

    def wall(name, c, L, along_x, kind):
        cx, cy = c
        sz = (lambda l, t, h: (l, t, h)) if along_x else (lambda l, t, h: (t, l, h))
        n = -1 if (along_x and cy < 0) or (not along_x and cx < 0) else 1
        K.box(f"{name}_low", sz(L - 0.1, 0.06, 1.0), bone, loc=(cx, cy, z0 + 0.5), bev=0.01, parent=P)
        for z in (1.0, 2.05):
            K.box(f"{name}_rail", sz(L, 0.08, 0.08), steel, loc=(cx, cy, z0 + z), bev=0.01, parent=P)
        K.box(f"{name}_top", sz(L - 0.1, 0.06, H - 2.09), bone, loc=(cx, cy, z0 + (H + 2.09) / 2), bev=0.01, parent=P)
        if kind == "window":
            K.box(f"{name}_glass", sz(L - 0.1, 0.012, 1.0), glass, loc=(cx, cy, z0 + 1.525), bev=0, parent=P)
            K.box(f"{name}_mullion", sz(0.05, 0.05, 1.0), steel, loc=(cx, cy, z0 + 1.525), bev=0.005, parent=P)
        elif kind == "slider":
            K.box(f"{name}_glass", sz(L * 0.5 - 0.05, 0.012, 1.0), glass, loc=(cx, cy + 0.25, z0 + 1.525) if not along_x else (cx, cy, z0 + 1.525), bev=0, parent=P)
            K.box(f"{name}_slide", sz(L * 0.5 - 0.05, 0.012, 1.0), glass, loc=(cx + n * 0.02, cy + 0.05, z0 + 1.525), bev=0, parent=P)
            K.box(f"{name}_tray", sz(0.6, 0.3, 0.04), steel, loc=(cx + n * 0.15, cy - 0.2, z0 + 1.02), bev=0.008, parent=P)
        elif kind == "vent":
            K.box(f"{name}_glass", sz(L - 0.1, 0.06, 1.0), bone, loc=(cx, cy, z0 + 1.525), bev=0.01, parent=P)

    wall("front", (0, -D / 2), W, True, "window")
    wall("back", (0, D / 2), W, True, "vent")
    wall("left", (-W / 2, 0), D, False, "slider")
    FR.vent(P, (0.4, D / 2 + 0.04, z0 + 1.6), 0.5, 0.4, steel, gra, n=4)
    _cracks(P, (-0.4, -D / 2 - 0.008, z0 + 1.6), rnd, n=4, L=0.4)
    # door on the right side: steel-blue leaf with a small window and a handle
    K.box("door", (0.05, D - 0.12, H - 0.06), M(STEEL, wear=0.3, grime=0.5, scratches=0.5), loc=(W / 2, 0, z0 + H / 2), bev=0.012, parent=P)
    K.box("door_win", (0.012, 0.4, 0.5), glass, loc=(W / 2 + 0.026, 0, z0 + 1.7), bev=0, parent=P)
    K.box("door_handle", (0.05, 0.14, 0.03), gra, loc=(W / 2 + 0.05, -0.45, z0 + 1.05), bev=0.005, parent=P)
    K.box("door_plate", (0.012, 0.4, 0.12), M(MUSTARD, wear=0.3), loc=(W / 2 + 0.026, 0, z0 + 2.15), bev=0, parent=P)
    stencil(P, "T3", (W / 2 + 0.034, 0, z0 + 2.15), 0.08, gra, rot=(90, 0, 90), extrude=0.003)
    # roof, fascia and a dead beacon
    K.slab("roof", W + 0.5, D + 0.5, 0.14, 0.12, M(BONE, wear=0.3, grime=0.5, moss=0.3), loc=(0, 0, z0 + H + 0.07), bev=0.03, parent=P)
    K.box("fascia", (W + 0.3, D + 0.3, 0.12), steel, loc=(0, 0, z0 + H - 0.04), bev=0.012, parent=P)
    K.cyl("beacon", 0.08, 0.1, gra, loc=(0, 0.3, z0 + H + 0.19), segs=12, bev=0.005, parent=P)
    K.sphere("beacon_dome", 0.075, K.mat(f"lens{AMBER_D}", AMBER_D, rough=0.15, metal=0.1, grime=0.3), loc=(0, 0.3, z0 + H + 0.25), scale=(1, 1, 0.8), parent=P)
    # inside: a desk under the front window and a stool
    K.box("desk", (W - 0.2, 0.45, 0.05), steel, loc=(0, -D / 2 + 0.3, z0 + 0.95), bev=0.01, parent=P)
    K.box("console", (0.5, 0.3, 0.12), gra, loc=(0.3, -D / 2 + 0.3, z0 + 1.03), rot=(-15, 0, 0), bev=0.015, parent=P)
    K.cyl("stool", 0.18, 0.05, gra, loc=(-0.2, 0.0, z0 + 0.65), segs=12, bev=0.01, parent=P)
    K.cyl("stool_leg", 0.03, 0.62, steel, loc=(-0.2, 0.0, z0 + 0.32), segs=8, bev=0, parent=P)
    return P


# ================================================================== furniture
def bench_colony(seed=46):
    """A colony street bench, 1.9 m: two steel-blue end frames with armrests, weathered slats, one seat slat split
    and sagging."""
    P = _root("bench")
    steel = M(STEEL, metal=0.3, wear=0.3, grime=0.45)
    for s in (-1, 1):
        x = s * 0.85
        K.box(f"foot{s}", (0.1, 0.6, 0.02), _dark(), loc=(x, 0, 0.01), bev=0.004, parent=P)
        beam(P, f"leg_f{s}", (x, -0.22, 0.02), (x, -0.2, 0.44), 0.06, steel, d=0.06)
        beam(P, f"leg_b{s}", (x, 0.24, 0.02), (x, 0.24, 0.44), 0.06, steel, d=0.06)
        beam(P, f"seat_rail{s}", (x, -0.26, 0.45), (x, 0.26, 0.42), 0.06, steel, d=0.05)
        beam(P, f"back_rail{s}", (x, 0.22, 0.42), (x, 0.36, 0.88), 0.06, steel, d=0.05)
        beam(P, f"arm{s}", (x + s * 0.015, -0.26, 0.66), (x + s * 0.015, 0.24, 0.66), 0.07, steel, d=0.04)
        beam(P, f"arm_post{s}", (x + s * 0.015, -0.22, 0.45), (x + s * 0.015, -0.22, 0.66), 0.05, steel, d=0.05)
        for y in (-0.22, 0.24):
            nut(P, (x, y, 0.025), r=0.02)
    beam(P, "stretcher", (-0.85, 0.02, 0.2), (0.85, 0.02, 0.2), 0.05, steel, d=0.05)
    for k, y in enumerate((-0.2, -0.08, 0.04, 0.16)):
        if k == 1:
            # the split slat: two halves sagging to the middle
            for s in (-1, 1):
                K.box(f"slat_half{s}", (0.98, 0.09, 0.035), wood(k, wear=0.3), loc=(s * 0.48, y, 0.455), rot=(0, s * -3.5, 0), bev=0.008, parent=P)
            continue
        K.box(f"slat{k}", (1.92, 0.09, 0.035), wood(k, wear=0.3), loc=(0, y, 0.47 - y * 0.06), bev=0.008, parent=P)
    for k, t in enumerate((0.45, 0.8)):
        y, z = 0.22 + 0.14 * t, 0.42 + 0.46 * t
        K.box(f"back_slat{k}", (1.92, 0.035, 0.11), wood(k + 2, wear=0.3), loc=(0, y - 0.04, z), rot=(-17, 0, 0), bev=0.008, parent=P)
    for x in (-0.85, 0.85):
        for y in (-0.2, 0.04, 0.16):
            bolt(P, (x, y, 0.49), rot=(0, 0, 0))
    return P


def trash_bin(seed=47):
    """A colony street bin, 1 m: a pill-cornered steel-blue body on a graphite plinth, a mustard band, a bone lid
    with its flap pushed in, a number plate and a crushed can beside it."""
    rnd = random.Random(seed)
    P = _root("bin")
    body = M(STEEL, wear=0.3, grime=0.55, scratches=0.5)
    gra = _dark()
    K.box("plinth", (0.46, 0.46, 0.06), gra, loc=(0, 0, 0.03), bev=0.01, parent=P)
    K.slab("body", 0.52, 0.52, 0.86, 0.06, body, loc=(0, 0, 0.06 + 0.43), bev=0.015, parent=P)
    K.slab("band", 0.53, 0.53, 0.06, 0.065, M(MUSTARD, wear=0.3), loc=(0, 0, 0.82), bev=0.005, parent=P)
    K.slab("lid", 0.58, 0.58, 0.07, 0.07, M(BONE, wear=0.3, grime=0.5), loc=(0, 0, 0.955), bev=0.02, parent=P)
    K.box("lid_cap", (0.3, 0.3, 0.04), M(BONE, wear=0.3, grime=0.5), loc=(0, 0.06, 1.005), bev=0.015, parent=P)
    K.box("slot", (0.34, 0.012, 0.13), K.mat("hole", "#141517", rough=0.9), loc=(0, -0.29, 0.93), bev=0, parent=P)
    K.box("flap", (0.32, 0.012, 0.12), gra, loc=(0, -0.24, 0.93), rot=(-35, 0, 0), bev=0.003, parent=P)
    K.box("plate", (0.16, 0.008, 0.1), M(BONE, wear=0.3), loc=(0, -0.262, 0.6), bev=0, parent=P)
    stencil(P, "03", (0, -0.267, 0.6), 0.06, gra, extrude=0.003)
    for x in (-0.18, 0.18):
        nut(P, (x, -0.266, 0.82), r=0.015, rot=(90, 0, 0))
    rust_patch(P, (0.12, -0.262, 0.22), (0.15, 0.004, 0.1))
    K.cyl("can", 0.033, 0.08, M(SIGNAL, metal=0.4, wear=0.4), loc=(0.42, -0.25, 0.033), rot=(90, 0, rnd.uniform(0, 90)), segs=10, bev=0.004, parent=P)
    _leaves(P, (0, -0.1), 0.45, 4, rnd)
    return P


def recycling_bins(seed=48):
    """Three colony recycling bins in a row on a pad: steel-blue, mustard and sea teal, each with a pictogram plate,
    the middle one's lid thrown back."""
    rnd = random.Random(seed)
    P = _root("recycling")
    gra = _dark()
    K.box("pad", (2.3, 1.05, 0.08), _concrete(0.25), loc=(0, 0.05, 0.04), bev=0.02, parent=P)
    z0 = 0.08
    for k, (col, x) in enumerate(((STEEL, -0.72), (MUSTARD, 0.0), (SEA, 0.72))):
        paint = M(col, wear=0.3, grime=0.5, scratches=0.5)
        tapered(f"body{k}", (0.56, 0.66), (0.62, 0.72), 0.9, paint, loc=(x, 0, z0 + 0.04), bev=0.025, parent=P)
        K.box(f"rim{k}", (0.65, 0.75, 0.05), paint, loc=(x, 0, z0 + 0.95), bev=0.012, parent=P)
        K.box(f"handle_bar{k}", (0.5, 0.04, 0.04), gra, loc=(x, 0.4, z0 + 0.92), bev=0.008, parent=P)
        for s in (-1, 1):
            K.box(f"handle_arm{k}", (0.04, 0.06, 0.04), gra, loc=(x + s * 0.24, 0.37, z0 + 0.92), bev=0, parent=P)
            K.cyl(f"wheel{k}", 0.09, 0.05, K.mat("tyre", "#26282a", rough=0.9, grime=0.3), loc=(x + s * 0.23, 0.33, 0.09), rot=(0, 90, 0), segs=12, bev=0.01, parent=P)
        K.cyl(f"axle{k}", 0.015, 0.52, gra, loc=(x, 0.33, 0.09), rot=(0, 90, 0), segs=6, bev=0, parent=P)
        lid = M(GRAPHITE, wear=0.3, grime=0.4)
        if k == 1:
            L = _grp(P, f"lid{k}", (x, 0.38, z0 + 0.99), (-115, 0, 0))
            K.box(f"lid{k}", (0.66, 0.78, 0.05), lid, loc=(0, -0.39, 0.02), bev=0.012, parent=L)
            K.box(f"inside{k}", (0.56, 0.64, 0.01), K.mat("hole", "#141517", rough=0.9), loc=(x, 0, z0 + 0.97), bev=0, parent=P)
        else:
            K.box(f"lid{k}", (0.66, 0.78, 0.05), lid, loc=(x, 0, z0 + 1.0), rot=(rnd.uniform(-1, 1), 0, 0), bev=0.012, parent=P)
        # a bone plate with a simple pictogram: circle, triangle or square
        y = -0.345
        K.box(f"plate{k}", (0.26, 0.008, 0.26), M(BONE, wear=0.3), loc=(x, y - 0.012, z0 + 0.62), rot=(-1.9, 0, 0), bev=0.004, parent=P)
        pts = [_poly(16, 0.08), _poly(3, 0.095, math.pi / 2), _poly(4, 0.1, math.pi / 4)][k]
        plate_xz(f"pict{k}", pts, 0.006, gra, loc=(x, y - 0.02, z0 + 0.61), rot=(-1.9, 0, 0), parent=P, bev=0)
    rust_patch(P, (0.72 + 0.1, -0.337, 0.4), (0.14, 0.004, 0.09))
    _leaves(P, (0, -0.3), 1.0, 6, rnd, z=0.085)
    return P


def newspaper_kiosk(seed=49):
    """A colony news kiosk, 2.2 x 1.6 m: framed bone walls, a counter under a roll-up shutter left half down, the side
    hatch shuttered, a mustard fascia with a newspaper pictogram and a wire rack of faded papers."""
    rnd = random.Random(seed)
    P = _root("kiosk")
    steel = _steel()
    gra = _dark()
    bone = M(BONE, wear=0.3, grime=0.5, scratches=0.5)
    W, D, H, z0 = 2.2, 1.6, 2.5, 0.1
    K.box("pad", (W + 0.5, D + 0.6, 0.1), _concrete(0.25), loc=(0, -0.05, 0.05), bev=0.02, parent=P)
    FR.framed_box(P, "body", (0, 0, z0 + H / 2), (W, D, H), bone, steel, fw=0.1)
    K.box("front_rail", (W - 0.1, 0.1, 0.08), steel, loc=(0, -D / 2 + 0.06, z0 + 1.0), bev=0.01, parent=P)
    K.box("counter", (W + 0.1, 0.4, 0.05), steel, loc=(0, -D / 2 - 0.04, z0 + 1.03), bev=0.01, parent=P)
    FR.hazard(P, (0, -D / 2 - 0.01, z0 + 0.12), W - 0.3, 0.1, M(MUSTARD, wear=0.3), gra, n=7)
    # roll-up shutter: housing at the top, slats down to 1.75 m, guides either side
    K.box("shutter_box", (W - 0.2, 0.2, 0.3), steel, loc=(0, -D / 2 - 0.06, z0 + H - 0.15), bev=0.02, parent=P)
    zt, zb = z0 + H - 0.3, z0 + 1.72
    n = int((zt - zb) / 0.075)
    shut = M(ZINC, metal=0.5, wear=0.3, grime=0.5)
    for k in range(n):
        K.box(f"slat{k}", (W - 0.28, 0.03, 0.068), shut, loc=(0, -D / 2 - 0.02, zt - 0.0375 - k * 0.075), rot=(4, 0, 0), bev=0, parent=P)
    K.box("shutter_bar", (W - 0.26, 0.05, 0.05), gra, loc=(0, -D / 2 - 0.02, zb), bev=0.008, parent=P)
    K.box("shutter_handle", (0.16, 0.04, 0.03), gra, loc=(0, -D / 2 - 0.05, zb - 0.02), bev=0.005, parent=P)
    for s in (-1, 1):
        K.box(f"guide{s}", (0.05, 0.06, H - 1.05), gra, loc=(s * (W / 2 - 0.12), -D / 2 - 0.02, z0 + 1.05 + (H - 1.05) / 2), bev=0.006, parent=P)
    # the dark serving hatch under the shutter, and the last papers left on the counter
    K.box("hatch_dark", (W - 0.3, 0.02, zb - z0 - 1.06), K.mat("hole", "#141517", rough=0.9), loc=(0, -D / 2 - 0.005, (zb + z0 + 1.06) / 2), bev=0, parent=P)
    for j in range(5):
        col = rnd.choice((BONE, "#bfb8a4", SEA, MUSTARD))
        K.box(f"paper{j}", (0.3, 0.24, rnd.uniform(0.02, 0.08)), M(col, wear=0.2, grime=0.6, rough=0.9), loc=(-0.8 + j * 0.38, -D / 2 - 0.05, z0 + 1.07),
              rot=(0, 0, rnd.uniform(-10, 10)), bev=0, parent=P)
    # side hatch on +x, its shutter fully down
    sx = W / 2 + 0.005
    K.box("hatch_box", (0.2, 0.9, 0.18), steel, loc=(sx + 0.05, 0, z0 + 2.0), bev=0.015, parent=P)
    for k in range(9):
        K.box(f"hatch_slat{k}", (0.03, 0.8, 0.068), shut, loc=(sx + 0.02, 0, z0 + 1.87 - k * 0.075), rot=(0, -4, 0), bev=0, parent=P)
    # roof and fascia
    K.slab("roof", W + 0.6, D + 0.6, 0.15, 0.12, M(BONE, wear=0.3, grime=0.5, moss=0.3), loc=(0, -0.08, z0 + H + 0.075), bev=0.03, parent=P)
    K.box("fascia", (W + 0.4, 0.06, 0.3), M(MUSTARD, wear=0.3, grime=0.45, scratches=0.5), loc=(0, -D / 2 - 0.36, z0 + H - 0.08), bev=0.012, parent=P)
    for s in (-1, 1):
        K.box(f"fascia_arm{s}", (0.06, 0.36, 0.06), steel, loc=(s * W / 2, -D / 2 - 0.18, z0 + H - 0.08), bev=0.008, parent=P)
    fy = -D / 2 - 0.395
    K.box("pict_paper", (0.36, 0.008, 0.2), M(BONE, wear=0.3), loc=(-0.4, fy, z0 + H - 0.08), rot=(0, 6, 0), bev=0, parent=P)
    for j in range(3):
        K.box(f"pict_line{j}", (0.24 if j else 0.28, 0.006, 0.025), gra, loc=(-0.41, fy - 0.005, z0 + H - 0.02 - j * 0.055), rot=(0, 6, 0), bev=0, parent=P)
    stencil(P, "K7", (0.45, fy, z0 + H - 0.08), 0.16, gra, extrude=0.004)
    # wire rack of faded papers on the left side
    rx = -W / 2 - 0.25
    for s in (-1, 1):
        beam(P, f"rack_leg{s}", (rx, s * 0.3, 0.1), (rx, s * 0.3, 1.3), 0.025, gra)
    for k, z in enumerate((0.5, 0.85, 1.2)):
        K.box(f"rack_shelf{k}", (0.22, 0.62, 0.02), gra, loc=(rx, 0, z), rot=(0, 20, 0), bev=0, parent=P)
        for j in range(2):
            if rnd.random() < 0.7:
                K.box(f"rack_paper{k}{j}", (0.02, 0.26, 0.32), M(rnd.choice((BONE, "#bfb8a4", SEA)), wear=0.2, grime=0.6, rough=0.9),
                      loc=(rx - 0.04, -0.15 + j * 0.3, z + 0.15), rot=(0, -12, 0), bev=0, parent=P)
    _leaves(P, (0, -1.2), 1.0, 5, rnd, z=0.105)
    return P


def phone_booth(seed=50):
    """A colony comm booth, 2.5 m: steel corner posts, a bone back panel carrying a dead terminal with a cracked screen
    and keypad, glass sides (one smashed), the handset hanging on its cord, a comm pictogram on the header."""
    rnd = random.Random(seed)
    P = _root("comm")
    steel = _steel()
    gra = _dark()
    bone = M(BONE, wear=0.3, grime=0.5, scratches=0.5)
    glass = _glass()
    W, H, z0 = 1.0, 2.3, 0.08
    K.box("plinth", (1.1, 1.1, 0.08), _concrete(0.3), loc=(0, 0, 0.04), bev=0.02, parent=P)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("post", (0.08, 0.08, H), steel, loc=(a * W / 2, b * W / 2, z0 + H / 2), bev=0.012, parent=P)
    K.box("header", (W + 0.04, W + 0.04, 0.28), steel, loc=(0, 0, z0 + H - 0.14), bev=0.015, parent=P)
    K.slab("roof", W + 0.2, W + 0.2, 0.1, 0.08, M(BONE, wear=0.3, grime=0.5, moss=0.3), loc=(0, 0, z0 + H + 0.05), bev=0.025, parent=P)
    # pictogram on the header: a mustard plate with signal arcs and a dot
    hy = -W / 2 - 0.025
    K.box("pict", (0.36, 0.01, 0.22), M(MUSTARD, wear=0.3), loc=(0, hy, z0 + H - 0.14), bev=0.003, parent=P)
    K.cyl("pict_dot", 0.025, 0.012, gra, loc=(-0.07, hy - 0.006, z0 + H - 0.2), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    for k, R in enumerate((0.07, 0.12)):
        K.torus(f"pict_arc{k}", R, 0.012, gra, loc=(-0.07, hy - 0.006, z0 + H - 0.2), rot=(90, 0, 0), arc=90, segs=12, parent=P)
    # back wall and the terminal
    K.box("back", (W - 0.06, 0.05, H - 0.3), bone, loc=(0, W / 2 - 0.02, z0 + (H - 0.3) / 2), bev=0.01, parent=P)
    ty = W / 2 - 0.13
    FR.framed_box(P, "terminal", (0, ty, z0 + 1.35), (0.5, 0.16, 0.72), M(STEEL, wear=0.3, grime=0.4), gra, fw=0.05)
    FR.screen(P, (0, ty - 0.09, z0 + 1.55), 0.34, 0.2, gra, K.mat("deadscreen", "#1d2a30", rough=0.12, metal=0.2))
    _cracks(P, (0.05, ty - 0.135, z0 + 1.57), rnd, n=4, L=0.18)
    for r in range(4):
        for c in range(3):
            K.box("key", (0.045, 0.02, 0.035), M(BONE, wear=0.4), loc=(-0.1 + c * 0.06, ty - 0.09, z0 + 1.32 - r * 0.05), bev=0.003, parent=P)
    K.box("card_slot", (0.1, 0.02, 0.015), gra, loc=(0.12, ty - 0.09, z0 + 1.32), bev=0, parent=P)
    K.box("cradle", (0.06, 0.06, 0.24), gra, loc=(0.29, ty - 0.02, z0 + 1.4), bev=0.01, parent=P)
    K.box("shelf", (0.6, 0.24, 0.03), steel, loc=(0, W / 2 - 0.16, z0 + 0.9), bev=0.006, parent=P)
    # the handset hanging off the shelf on its cord
    cab = K.mat("cable", "#2b2d30", rough=0.6)
    pts = [(0.29, ty - 0.03, z0 + 1.27), (0.3, ty - 0.12, z0 + 1.0), (0.26, ty - 0.2, z0 + 0.78)]
    for k in range(len(pts) - 1):
        rod(P, f"cord{k}", pts[k], pts[k + 1], 0.01, cab, segs=6)
    hs = _grp(P, "handset", (0.25, ty - 0.21, z0 + 0.66), (8, 12, 0))
    K.box("hs_grip", (0.04, 0.04, 0.2), gra, bev=0.01, parent=hs)
    for s in (-1, 1):
        K.box(f"hs_cup{s}", (0.055, 0.07, 0.06), gra, loc=(0, -0.02, s * 0.1), bev=0.012, parent=hs)
    # sides: the left pane whole but crazed, the right one smashed to its lower edge
    for s in (-1, 1):
        K.box(f"rail{s}", (0.05, W - 0.08, 0.05), steel, loc=(s * W / 2, 0, z0 + 0.95), bev=0.006, parent=P)
    K.box("pane_l_lo", (0.012, W - 0.08, 0.86), glass, loc=(-W / 2, 0, z0 + 0.5), bev=0, parent=P)
    K.box("pane_l_hi", (0.012, W - 0.08, 0.98), glass, loc=(-W / 2, 0, z0 + 1.46), bev=0, parent=P)
    _cracks(P, (-W / 2 - 0.008, 0.1, z0 + 1.4), rnd, n=4, L=0.45, face="x")
    K.box("pane_r_lo", (0.012, W - 0.08, 0.86), glass, loc=(W / 2, 0, z0 + 0.5), bev=0, parent=P)
    _plate("pane_r_shard", [(-0.46, 0), (0.46, 0), (0.46, 0.22), (0.2, 0.08), (-0.05, 0.3), (-0.3, 0.12), (-0.46, 0.2)], 0.012, glass, P,
           loc=(W / 2, 0, z0 + 0.98), rot=(90, 0, 90))
    _shards(P, (W / 2 + 0.4, 0.0), 0.3, 5, rnd)
    return P


def vending_machine(seed=51):
    """A dead colony vending machine, 1.95 m: a bone cabinet in a steel frame, its glass front smashed out below and
    cracked above, shelves of faded cans behind, a dark control panel and the delivery flap."""
    rnd = random.Random(seed)
    P = _root("vending")
    steel = _steel()
    gra = _dark()
    bone = M(BONE, wear=0.3, grime=0.55, scratches=0.5)
    W, D, H = 1.0, 0.8, 1.85
    G = _grp(P, "cabinet", (0, 0.02, 0.0), (-1.5, 0, 0))
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("foot", (0.08, 0.08, 0.06), gra, loc=(a * 0.4, b * 0.3, 0.03), bev=0.006, parent=G)
    z0 = 0.06
    # the cabinet: a deep back box and a 0.3 m front frame round the window, so the shelves sit in a real recess
    K.box("body", (W, D - 0.3, H), bone, loc=(0, 0.15, z0 + H / 2), bev=0.02, parent=G)
    fy = -D / 2
    for s in (-1, 1):
        K.box(f"frame_v{s}", (0.06, 0.06, H + 0.02), steel, loc=(s * (W / 2 - 0.02), fy, z0 + H / 2), bev=0.01, parent=G)
        K.box(f"stripe{s}", (0.006, D - 0.1, 0.08), M(STEEL, wear=0.3), loc=(s * (W / 2 + 0.003), 0.03, z0 + 0.4), bev=0, parent=G)
    for z in (z0 + 0.03, z0 + H - 0.03):
        K.box("frame_h", (W + 0.02, 0.06, 0.06), steel, loc=(0, fy, z), bev=0.01, parent=G)
    # header with a can pictogram
    K.box("header", (W - 0.1, 0.03, 0.2), M(MUSTARD, wear=0.3, grime=0.45), loc=(0, fy - 0.01, z0 + H - 0.16), bev=0.006, parent=G)
    K.box("pict_can", (0.07, 0.008, 0.12), gra, loc=(-0.3, fy - 0.03, z0 + H - 0.16), bev=0.004, parent=G)
    K.box("pict_lid", (0.05, 0.008, 0.015), gra, loc=(-0.3, fy - 0.03, z0 + H - 0.09), bev=0, parent=G)
    # behind the glass: a dark interior and four shelves of cans and packs, some sold out
    gx0, gx1, gz0, gz1 = -0.44, 0.2, z0 + 0.62, z0 + 1.6
    K.box("interior", (gx1 - gx0, 0.02, gz1 - gz0), K.mat("hole", "#141517", rough=0.9), loc=((gx0 + gx1) / 2, fy + 0.288, (gz0 + gz1) / 2), bev=0, parent=G)
    fd = M(BONE, wear=0.3, grime=0.55, scratches=0.5)
    for nm, (x0, x1, za_, zb_) in (("front_low", (-W / 2, W / 2, z0, gz0)), ("front_top", (-W / 2, W / 2, gz1, z0 + H)),
                                   ("front_l", (-W / 2, gx0, gz0, gz1)), ("front_r", (gx1, W / 2, gz0, gz1))):
        K.box(nm, (x1 - x0, 0.3, zb_ - za_), fd, loc=((x0 + x1) / 2, fy + 0.15, (za_ + zb_) / 2), bev=0.01, parent=G)
    for k in range(4):
        z = gz0 + 0.06 + k * 0.25
        K.box(f"shelf{k}", (gx1 - gx0, 0.26, 0.015), steel, loc=((gx0 + gx1) / 2, fy + 0.16, z), bev=0, parent=G)
        col = (SIGNAL, SEA, MUSTARD, STEEL)[k]
        for j in range(5):
            if rnd.random() < 0.3:
                continue
            K.cyl(f"can{k}{j}", 0.035, 0.12, M(col, metal=0.3, wear=0.3, grime=0.4), loc=(gx0 + 0.07 + j * 0.125, fy + 0.1, z + 0.068), segs=8, bev=0, parent=G)
    gl = K.mat("vendglass", "#3a4c55", rough=0.06, metal=0.1, wear=0.1, grime=0.2)
    gw, gh = gx1 - gx0, gz1 - gz0
    plate_xz("glass", [(0, gh * 0.42), (0.12, gh * 0.5), (0.22, gh * 0.4), (0.36, gh * 0.55), (0.5, gh * 0.47), (gw, gh * 0.58), (gw, gh - 0.32),
                         (gw - 0.12, gh - 0.2), (gw - 0.08, gh - 0.1), (gw - 0.22, gh), (0, gh)], 0.012, gl,
             loc=(gx0, fy - 0.012, gz0), parent=G, bev=0)
    _cracks(G, (gx1 - 0.2, fy - 0.02, gz1 - 0.3), rnd, n=4, L=0.35)
    for k, (bx, bz) in enumerate(((gx0, (gz0 + gz1) / 2), (gx1, (gz0 + gz1) / 2))):
        K.box(f"glass_rail{k}", (0.03, 0.04, gz1 - gz0 + 0.04), steel, loc=(bx, fy - 0.01, bz), bev=0.005, parent=G)
    for z in (gz0, gz1):
        K.box("glass_rail_h", (gw + 0.03, 0.04, 0.03), steel, loc=((gx0 + gx1) / 2, fy - 0.01, z), bev=0.005, parent=G)
    # control column on the right
    cx = 0.33
    K.box("panel", (0.22, 0.02, 1.0), M(STEEL, wear=0.3, grime=0.45), loc=(cx, fy - 0.01, z0 + 1.1), bev=0.006, parent=G)
    K.box("display", (0.16, 0.012, 0.07), K.mat("deadscreen", "#1d2a30", rough=0.12, metal=0.2), loc=(cx, fy - 0.022, z0 + 1.48), bev=0, parent=G)
    for r in range(4):
        for c in range(2):
            K.box("button", (0.05, 0.012, 0.035), M(BONE, wear=0.4), loc=(cx - 0.035 + c * 0.07, fy - 0.022, z0 + 1.36 - r * 0.06), bev=0.002, parent=G)
    K.box("coin_slot", (0.012, 0.012, 0.05), gra, loc=(cx, fy - 0.022, z0 + 1.0), bev=0, parent=G)
    K.box("return_cup", (0.09, 0.03, 0.06), gra, loc=(cx, fy - 0.03, z0 + 0.8), bev=0.006, parent=G)
    # delivery flap and kick plate
    K.box("kick", (W - 0.1, 0.02, 0.5), M(STEEL, wear=0.3, grime=0.6), loc=(0, fy - 0.01, z0 + 0.3), bev=0.006, parent=G)
    K.box("flap_hole", (0.6, 0.015, 0.2), K.mat("hole", "#141517", rough=0.9), loc=(-0.1, fy - 0.02, z0 + 0.36), bev=0, parent=G)
    K.box("flap", (0.6, 0.015, 0.2), gra, loc=(-0.1, fy - 0.03, z0 + 0.365), rot=(-12, 0, 0), bev=0.003, parent=G)
    rust_patch(G, (0.25, fy - 0.022, z0 + 0.12), (0.2, 0.004, 0.07))
    _shards(P, (0.05, -0.75), 0.35, 6, rnd, mat=gl)
    return P


def fire_hydrant(seed=52):
    """A colony fire hydrant, 0.85 m: a mustard barrel turned round its axis, bolted bonnet, pentagon nut, two hose
    outlets with chained caps and a big pumper outlet facing the road."""
    P = _root("hydrant")
    paint = M(MUSTARD, metal=0.2, wear=0.3, grime=0.5, scratches=0.5)
    gra = _dark()
    K.cyl("collar", 0.26, 0.05, _concrete(0.3), loc=(0, 0, 0.0), segs=16, bev=0.01, parent=P)
    prof = [(0.001, 0.02), (0.17, 0.02), (0.17, 0.08), (0.125, 0.1), (0.115, 0.13), (0.115, 0.56), (0.14, 0.58), (0.14, 0.63),
            (0.12, 0.645), (0.12, 0.68), (0.1, 0.73), (0.065, 0.765), (0.001, 0.775)]
    lathe("barrel", prof, paint, segs=16, parent=P)
    K.cyl("nut", 0.035, 0.06, gra, loc=(0, 0, 0.8), segs=5, bev=0.004, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        nut(P, (math.cos(a) * 0.128, math.sin(a) * 0.128, 0.625), r=0.012)
        nut(P, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.085), r=0.014)
    chain = M(ZINC, metal=0.6, wear=0.3)
    for s in (-1, 1):
        x = s * 0.115
        K.cyl(f"outlet{s}", 0.045, 0.1, paint, loc=(x + s * 0.04, 0, 0.45), rot=(0, 90, 0), segs=12, bev=0.006, parent=P)
        K.cyl(f"cap{s}", 0.055, 0.04, gra, loc=(x + s * 0.1, 0, 0.45), rot=(0, 90, 0), segs=6, bev=0.004, parent=P)
        for j in range(3):
            K.torus(f"link{s}{j}", 0.012, 0.003, chain, loc=(x + s * 0.09, -0.02 - j * 0.004, 0.4 - j * 0.02), rot=(0, 90 if j % 2 else 0, 0), segs=8, parent=P)
    K.cyl("pumper", 0.065, 0.1, paint, loc=(0, -0.15, 0.36), rot=(90, 0, 0), segs=14, bev=0.006, parent=P)
    K.cyl("pumper_cap", 0.075, 0.045, gra, loc=(0, -0.21, 0.36), rot=(90, 0, 0), segs=6, bev=0.004, parent=P)
    K.cyl("pumper_nut", 0.02, 0.03, gra, loc=(0, -0.245, 0.36), rot=(90, 0, 0), segs=5, bev=0, parent=P)
    rust_patch(P, (0.0, -0.116, 0.2), (0.06, 0.004, 0.08))
    return P


def manhole_cover_open(seed=53):
    """An open manhole in the road: a concrete collar, a steel frame ring round the black shaft with the top rung of
    its ladder, and the ribbed cover levered off and lying askew on the edge."""
    rnd = random.Random(seed)
    P = _root("manhole")
    steel = M(GRAPHITE, metal=0.5, wear=0.3, grime=0.5)
    lathe("collar", [(0.4, 0.0), (0.62, 0.0), (0.58, 0.035), (0.42, 0.045)], _concrete(0.4), segs=24, parent=P)
    lathe("frame", [(0.33, 0.02), (0.41, 0.02), (0.41, 0.06), (0.33, 0.06)], steel, segs=24, parent=P)
    K.cyl("shaft", 0.335, 0.02, K.mat("hole", "#141517", rough=0.9), loc=(0, 0, 0.035), segs=24, bev=0, parent=P)
    # the top rung of the ladder just inside the rim
    rung = M(RUST, metal=0.4, wear=0.3)
    rod(P, "rung", (-0.14, 0.24, 0.07), (0.14, 0.24, 0.07), 0.016, rung)
    for s in (-1, 1):
        rod(P, f"rung_leg{s}", (s * 0.14, 0.24, 0.07), (s * 0.14, 0.3, 0.045), 0.016, rung)
    # the cover: a ribbed disc, one edge on the frame, the other in the grass
    C = _grp(P, "cover", (0.55, -0.42, 0.06), (8, -4, 25))
    K.cyl("lid", 0.32, 0.04, steel, segs=24, bev=0.006, parent=C)
    for R in (0.12, 0.24):
        K.torus(f"lid_ring{R}", R, 0.01, steel, loc=(0, 0, 0.02), segs=20, parent=C)
    for k in range(4):
        K.box(f"lid_rib{k}", (0.6, 0.02, 0.016), steel, loc=(0, 0, 0.022), rot=(0, 0, k * 45), bev=0, parent=C)
    K.cyl("lid_hole", 0.025, 0.045, K.mat("hole", "#141517", rough=0.9), loc=(0.27, 0, 0.0), segs=8, bev=0, parent=C)
    for k in range(3):
        rust_patch(C, (rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 0.031), (0.1, 0.08, 0.003), rot=(0, 0, rnd.uniform(0, 90)))
    return P


def street_planter(seed=54):
    """A colony street planter, 1.6 x 0.8 m: cast concrete walls with a steel-blue capping rail, dry soil and a
    shrub long dead, its twigs bare."""
    rnd = random.Random(seed)
    P = _root("planter")
    conc = _concrete(0.35)
    steel = _steel()
    L, W, H, t = 1.6, 0.8, 0.55, 0.1
    for s in (-1, 1):
        K.box(f"wall_l{s}", (L, t, H), conc, loc=(0, s * (W - t) / 2, H / 2), bev=0.025, parent=P)
        K.box(f"wall_e{s}", (t, W - 2 * t, H), conc, loc=(s * (L - t) / 2, 0, H / 2), bev=0.025, parent=P)
        K.box(f"cap_l{s}", (L + 0.03, t + 0.04, 0.04), steel, loc=(0, s * (W - t) / 2, H + 0.02), bev=0.008, parent=P)
        K.box(f"cap_e{s}", (t + 0.04, W - 2 * t, 0.04), steel, loc=(s * (L - t) / 2, 0, H + 0.02), bev=0.008, parent=P)
        K.box(f"foot{s}", (0.3, W + 0.04, 0.06), conc, loc=(s * 0.55, 0, 0.03), bev=0.015, parent=P)
    K.box("soil", (L - 2 * t, W - 2 * t, 0.04), earth(), loc=(0, 0, H - 0.08), bev=0, parent=P)
    dead = K.mat("deadwood", DEADWOOD, rough=0.95, grime=0.2, noise_scale=6.0, cracks=0.4)
    base = (0.05, 0.0, H - 0.06)
    for k in range(7):
        yaw = k * 360 / 7 + rnd.uniform(-20, 20)
        pitch = rnd.uniform(45, 75)
        Lb = rnd.uniform(0.45, 0.75)
        limb(P, f"stem{k}", base, yaw, pitch, Lb, 0.025, 0.008, dead, segs=5, bev=0)
        cp = math.cos(math.radians(pitch))
        mid = (base[0] + cp * math.cos(math.radians(yaw)) * Lb * 0.6, base[1] + cp * math.sin(math.radians(yaw)) * Lb * 0.6,
               base[2] + math.sin(math.radians(pitch)) * Lb * 0.6)
        limb(P, f"twig{k}", mid, yaw + rnd.choice((-40, 40)), pitch - 10, 0.25, 0.012, 0.004, dead, segs=4, bev=0)
    _leaves(P, (0, 0), 0.5, 8, rnd, z=H - 0.055)
    _leaves(P, (0, -0.65), 0.8, 4, rnd)
    rust_patch(P, (-0.4, -W / 2 - 0.003, H - 0.12), (0.2, 0.004, 0.1))
    return P


def bollard_row(seed=55):
    """Four colony steel bollards in a row, 1 m tall: mustard reflector bands, domed caps, one knocked over at an angle
    with its collar cracked, one cap gone."""
    rnd = random.Random(seed)
    P = _root("bollards")
    steel = M(STEEL, metal=0.3, wear=0.3, grime=0.5, scratches=0.5)
    gra = _dark()
    for k, x in enumerate((-1.8, -0.6, 0.6, 1.8)):
        K.cyl(f"collar{k}", 0.17, 0.05, _concrete(0.5 if k == 2 else 0.25), loc=(x, 0, 0.015), segs=14, bev=0.01, parent=P)
        B = _grp(P, f"bollard{k}", (x, 0, 0.03), (0, 18, 6) if k == 2 else (0, rnd.uniform(-1.5, 1.5), 0))
        K.cyl(f"post{k}", 0.1, 0.95, steel, loc=(0, 0, 0.475), segs=14, bev=0.01, parent=B)
        K.cyl(f"base{k}", 0.104, 0.1, gra, loc=(0, 0, 0.05), segs=14, bev=0, parent=B)
        for z in (0.72, 0.83):
            K.cyl(f"band{k}", 0.103, 0.06, M(MUSTARD, wear=0.3), loc=(0, 0, z), segs=14, bev=0, parent=B)
        if k != 1:
            K.sphere(f"cap{k}", 0.1, steel, loc=(0, 0, 0.95), scale=(1, 1, 0.45), parent=B)
        else:
            K.cyl(f"open{k}", 0.085, 0.01, K.mat("hole", "#141517", rough=0.9), loc=(0, 0, 0.951), segs=14, bev=0, parent=B)
        rust_patch(B, (0, -0.101, 0.3), (0.06, 0.003, 0.08))
    for j in range(3):
        K.box("crack", (0.12, 0.012, 0.01), gra, loc=(0.6 + math.cos(j * 2.1) * 0.15, math.sin(j * 2.1) * 0.15, 0.041), rot=(0, 0, math.degrees(j * 2.1)), bev=0, parent=P)
    return P


def bike_rack(seed=56):
    """A galvanised colony bike rack of four hoops on a rail, a rusted bike frame still chained to one, its front
    wheel long gone and the rear rim buckled."""
    rnd = random.Random(seed)
    P = _root("bikerack")
    galv = M(ZINC, metal=0.5, wear=0.3, grime=0.45)
    for s in (-1, 1):
        K.box(f"rail{s}", (2.5, 0.05, 0.02), galv, loc=(0, s * 0.3, 0.01), bev=0.004, parent=P)
    for x in (-1.15, 1.15):
        for s in (-1, 1):
            nut(P, (x, s * 0.3, 0.025), r=0.015)
    hoops = (-0.9, -0.3, 0.3, 0.9)
    for k, x in enumerate(hoops):
        K.torus(f"hoop{k}", 0.3, 0.025, galv, loc=(x, 0, 0.55), rot=(90, 0, 90), arc=180, segs=20, parent=P)
        for s in (-1, 1):
            rod(P, f"leg{k}{s}", (x, s * 0.3, 0.0), (x, s * 0.3, 0.55), 0.025, galv, segs=10)
    # the bike: a diamond frame on its rear rim, the fork down on the ground, leaning on the hoop at x = 0.3
    rust = M(RUST, metal=0.4, wear=0.3, grime=0.5)
    paint = M(SEA, metal=0.3, wear=0.3, grime=0.5)
    B = _grp(P, "bike", (0.43, -0.5, 0.33), (-17, -8, 0))
    ra, bb, st, ht, hb, fa = (0, 0, 0), (0, 0.43, -0.05), (0, 0.3, 0.47), (0, 0.9, 0.45), (0, 0.93, 0.3), (0, 1.02, 0.0)
    rod(B, "seat_tube", bb, st, 0.018, paint)
    rod(B, "top_tube", st, ht, 0.018, paint)
    rod(B, "down_tube", bb, hb, 0.02, paint)
    rod(B, "head_tube", hb, ht, 0.024, paint)
    for s in (-1, 1):
        rod(B, f"chainstay{s}", bb, (s * 0.05, 0, 0), 0.011, rust)
        rod(B, f"seatstay{s}", st, (s * 0.05, 0, 0), 0.01, rust)
        rod(B, f"fork{s}", hb, (s * 0.045, fa[1], fa[2]), 0.012, rust)
    rod(B, "seat_post", st, (0, 0.27, 0.6), 0.012, rust)
    K.box("saddle", (0.13, 0.26, 0.05), K.mat("tyre", "#26282a", rough=0.9, grime=0.3), loc=(0, 0.26, 0.63), bev=0.015, parent=B)
    rod(B, "stem", ht, (0, 0.92, 0.6), 0.012, rust)
    rod(B, "bars", (-0.22, 0.95, 0.62), (0.22, 0.95, 0.62), 0.012, rust)
    K.cyl("crank", 0.08, 0.012, rust, loc=(0.03, 0.43, -0.05), rot=(0, 90, 0), segs=12, bev=0, parent=B)
    rod(B, "pedal_arm", (0.04, 0.43, -0.05), (0.04, 0.5, -0.2), 0.01, rust)
    rim = _grp(B, "rim", (0, 0, 0), (0, 0, 0))
    rim.scale = (1, 1.0, 0.9)
    K.torus("rim_ring", 0.3, 0.016, rust, rot=(0, 90, 0), segs=24, parent=rim)
    K.cyl("hub", 0.03, 0.1, rust, rot=(0, 90, 0), segs=8, bev=0, parent=rim)
    for j in range(6):
        a = j * 60 + 10
        rod(rim, f"spoke{j}", (0, 0, 0), (0, math.cos(math.radians(a)) * 0.29, math.sin(math.radians(a)) * 0.29), 0.004, rust, segs=4)
    # the chain lock round the seat tube and the hoop
    for j in range(5):
        K.torus(f"lock{j}", 0.018, 0.005, M(GRAPHITE, metal=0.6), loc=(0.33 + j * 0.015, -0.12 + j * 0.03, 0.56 - j * 0.03),
                rot=(90 if j % 2 else 0, 0, 30), segs=8, parent=P)
    _leaves(P, (0, 0), 1.0, 5, rnd)
    return P


def parking_meter(seed=57):
    """A colony parking meter, 1.5 m: a square steel post with a pill-cornered bone head, its display dark and
    cracked, a coin slot, a mustard band and a P plate on top."""
    rnd = random.Random(seed)
    P = _root("meter")
    steel = M(STEEL, metal=0.3, wear=0.3, grime=0.5)
    gra = _dark()
    K.cyl("collar", 0.13, 0.04, _concrete(0.3), loc=(0, 0, 0.01), segs=12, bev=0.008, parent=P)
    K.box("post", (0.08, 0.08, 1.12), steel, loc=(0, 0, 0.56), bev=0.01, parent=P)
    K.box("post_band", (0.085, 0.085, 0.06), M(MUSTARD, wear=0.3), loc=(0, 0, 0.9), bev=0.004, parent=P)
    K.box("neck", (0.14, 0.12, 0.06), gra, loc=(0, 0, 1.12), bev=0.01, parent=P)
    K.slab("head", 0.24, 0.17, 0.32, 0.05, M(BONE, wear=0.3, grime=0.5, scratches=0.5), loc=(0, 0, 1.31), bev=0.015, parent=P)
    K.slab("head_band", 0.245, 0.175, 0.04, 0.052, M(STEEL, wear=0.3), loc=(0, 0, 1.2), bev=0.005, parent=P)
    K.box("window_frame", (0.17, 0.012, 0.11), gra, loc=(0, -0.088, 1.37), bev=0.004, parent=P)
    K.box("display", (0.14, 0.008, 0.08), K.mat("deadscreen", "#1d2a30", rough=0.12, metal=0.2), loc=(0, -0.095, 1.37), bev=0, parent=P)
    _cracks(P, (0.03, -0.1, 1.38), rnd, n=3, L=0.08)
    K.box("coin_plate", (0.08, 0.01, 0.06), steel, loc=(0, -0.088, 1.25), bev=0.003, parent=P)
    K.box("coin_slot", (0.008, 0.01, 0.035), K.mat("hole", "#141517", rough=0.9), loc=(0, -0.094, 1.25), bev=0, parent=P)
    K.box("top_plate", (0.17, 0.12, 0.012), steel, loc=(0, 0, 1.475), bev=0.003, parent=P)
    K.box("p_plate", (0.13, 0.01, 0.13), M(STEEL, wear=0.3), loc=(0, 0, 1.55), bev=0.004, parent=P)
    stencil(P, "P", (0, -0.006, 1.55), 0.1, M(BONE, wear=0.2, scratches=0), extrude=0.003)
    rust_patch(P, (0, -0.041, 0.35), (0.05, 0.003, 0.1))
    return P


def info_board(seed=58):
    """A colony information board, 2.5 m: two steel posts carrying a framed board under a small roof, the map faded and
    peeling: the coast, green land, mustard roads, the town's blocks and a red you-are-here marker."""
    rnd = random.Random(seed)
    P = _root("infoboard")
    steel = _steel()
    gra = _dark()
    for s in (-1, 1):
        K.box(f"foot{s}", (0.3, 0.3, 0.06), _concrete(0.3), loc=(s * 0.9, 0, 0.02), bev=0.01, parent=P)
        K.box(f"post{s}", (0.1, 0.1, 2.35), steel, loc=(s * 0.9, 0, 1.175), bev=0.015, parent=P)
    zc = 1.5
    FR.framed_box(P, "board", (0, 0, zc), (1.7, 0.1, 1.15), M(BONE, wear=0.3, grime=0.5), steel, fw=0.08)
    K.slab("roof", 2.05, 0.4, 0.06, 0.06, M(BONE, wear=0.3, grime=0.5, moss=0.3), loc=(0, -0.04, 2.37), rot=(-6, 0, 0), bev=0.015, parent=P)
    K.box("light_bar", (1.5, 0.05, 0.04), gra, loc=(0, -0.18, 2.29), bev=0.006, parent=P)
    # the map: x across, z up, on the board's front face
    y = -0.06
    mx0, mz0 = -0.78, zc - 0.5
    plate_xz("map", [(0, 0), (1.25, 0), (1.25, 1.0), (0, 1.0)], 0.006, K.mat("mapbg", MAP_BG, rough=0.8, grime=0.3), loc=(mx0, y, mz0), parent=P, bev=0)
    yy = y - 0.006
    sea = M(SEA, wear=0.2, grime=0.3, scratches=0)
    plate_xz("map_sea", [(0, 0), (0.42, 0), (0.36, 0.2), (0.4, 0.42), (0.28, 0.6), (0.3, 0.82), (0.18, 1.0), (0, 1.0)], 0.004, sea, loc=(mx0, yy, mz0), parent=P, bev=0)
    land = K.mat("mapland", MAP_LAND, rough=0.8, grime=0.3)
    plate_xz("map_park", [(0, 0), (0.24, 0.02), (0.28, 0.2), (0.06, 0.24)], 0.004, land, loc=(mx0 + 0.85, yy, mz0 + 0.68), parent=P, bev=0)
    plate_xz("map_wood", [(0, 0), (0.3, 0), (0.26, 0.2), (0.04, 0.16)], 0.004, land, loc=(mx0 + 0.55, yy, mz0 + 0.08), parent=P, bev=0)
    road = M(MUSTARD, wear=0.2, scratches=0)
    for k, (a, b) in enumerate((((0.4, 0.5), (1.25, 0.55)), ((0.75, 0.0), (0.7, 1.0)), ((0.7, 0.55), (1.2, 0.95)))):
        L = math.dist(a, b)
        ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
        K.box(f"map_road{k}", (L, 0.004, 0.025), road, loc=(mx0 + (a[0] + b[0]) / 2, yy - 0.002, mz0 + (a[1] + b[1]) / 2), rot=(0, -ang, 0), bev=0, parent=P)
    blk = M(STEEL, wear=0.2, scratches=0)
    for k in range(7):
        bx, bz = rnd.uniform(0.48, 1.15), rnd.uniform(0.1, 0.9)
        if abs(bx - 0.72) < 0.06 or abs(bz - 0.52) < 0.06:
            continue
        K.box(f"map_block{k}", (rnd.uniform(0.05, 0.1), 0.004, rnd.uniform(0.04, 0.08)), blk, loc=(mx0 + bx, yy - 0.001, mz0 + bz), bev=0, parent=P)
    K.cyl("map_here", 0.03, 0.006, M(SIGNAL, wear=0.2, scratches=0), loc=(mx0 + 0.72, yy - 0.004, mz0 + 0.52), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.torus("map_here_ring", 0.05, 0.006, M(SIGNAL, wear=0.2, scratches=0), loc=(mx0 + 0.72, yy - 0.004, mz0 + 0.52), rot=(90, 0, 0), segs=16, parent=P)
    # legend: four coloured keys and their lines
    for k, m in enumerate((sea, land, road, blk)):
        z = zc + 0.35 - k * 0.2
        K.box(f"key{k}", (0.06, 0.006, 0.06), m, loc=(0.58, y - 0.004, z), bev=0, parent=P)
        K.box(f"key_line{k}", (0.14, 0.004, 0.02), gra, loc=(0.7, y - 0.004, z), bev=0, parent=P)
    # the map faded and peeling at one corner
    _plate("peel", [(0, 0), (0.22, 0), (0.0, 0.18)], 0.004, K.mat("mapbg", MAP_BG, rough=0.8, grime=0.3), P, loc=(mx0 + 1.25, y - 0.02, mz0), rot=(70, 0, 180))
    rust_patch(P, (-0.5, y - 0.012, zc - 0.42), (0.25, 0.004, 0.06))
    K.box("back", (1.6, 0.012, 1.05), M(ZINC, metal=0.5, wear=0.3, grime=0.4), loc=(0, 0.056, zc), bev=0, parent=P)
    return P


# ================================================================== civic
def colony_flag_pole(seed=59):
    """The colony flag, 8.5 m: a tapered steel pole on a framed plinth, a mustard finial and the halyard tied off at
    the cleat, the steel-blue flag with its mustard stripe torn to rags at the fly."""
    rnd = random.Random(seed)
    P = _root("flag")
    steel = _steel()
    gra = _dark()
    K.box("footing", (1.1, 1.1, 0.2), _concrete(0.3), loc=(0, 0, 0.07), bev=0.03, parent=P)
    FR.framed_box(P, "plinth", (0, 0, 0.45), (0.6, 0.6, 0.6), M(BONE, wear=0.3, grime=0.5, moss=0.2), steel, fw=0.07, screws=_zinc())
    stencil(P, "C1", (0, -0.305, 0.5), 0.12, gra, extrude=0.004)
    H = 8.0
    K.cyl("pole", 0.07, H, M(ZINC, metal=0.5, wear=0.2, grime=0.35), loc=(0, 0, 0.75 + H / 2), segs=12, r2=0.042, bev=0, parent=P)
    K.cyl("pole_collar", 0.1, 0.15, steel, loc=(0, 0, 0.82), segs=12, bev=0.01, parent=P)
    zt = 0.75 + H
    K.cyl("truck", 0.06, 0.08, gra, loc=(0, 0, zt + 0.04), segs=12, bev=0.008, parent=P)
    K.sphere("finial", 0.08, M(MUSTARD, metal=0.5, wear=0.2), loc=(0, 0, zt + 0.16), parent=P)
    K.box("cleat", (0.04, 0.03, 0.16), gra, loc=(0.075, 0, 1.35), bev=0.006, parent=P)
    rope = K.mat("rope", "#c9c1a8", rough=0.9, grime=0.4)
    rod(P, "halyard_a", (0.06, -0.01, zt), (0.08, -0.02, 1.4), 0.006, rope, segs=4)
    rod(P, "halyard_b", (0.06, 0.01, zt), (0.09, 0.02, 1.3), 0.006, rope, segs=4)
    # the flag: three bands sharing one wave, each row torn to its own length at the fly
    fly, hoist, top = 1.9, 1.2, zt - 0.15
    nx, nz = 9, 6
    lengths = [fly * rnd.uniform(0.45, 1.0) for _ in range(nz + 1)]
    lengths[0] = fly * 0.95

    def pos(i, j):
        u = i / nx
        x = 0.07 + lengths[j] * u
        z = top - hoist * j / nz - 0.18 * (lengths[j] * u / fly) ** 2
        y = 0.1 * math.sin(lengths[j] * u * 3.0 + j * 0.25) * min(1.0, lengths[j] * u * 2)
        return (x, y, z)

    for name, col, j0, j1 in (("flag_top", STEEL, 0, 2), ("flag_stripe", MUSTARD, 2, 4), ("flag_low", STEEL, 4, 6)):
        bm = bmesh.new()
        rows = [[bm.verts.new(pos(i, j)) for i in range(nx + 1)] for j in range(j0, j1 + 1)]
        for r in range(len(rows) - 1):
            for i in range(nx):
                bm.faces.new((rows[r][i], rows[r][i + 1], rows[r + 1][i + 1], rows[r + 1][i]))
        ob = K.place(K.from_bm(name, bm, M(col, wear=0.2, grime=0.45, rough=0.9, scratches=0), smooth=True), parent=P)
        ob.modifiers.new("cloth", "SOLIDIFY").thickness = 0.008
    for z in (top - 0.05, top - hoist + 0.05):
        K.torus("clip", 0.05, 0.008, gra, loc=(0, 0, z), segs=12, parent=P)
    return P


def statue_colony_founder(seed=60):
    """The colony founder in weathered metal on a concrete plinth, 4 m in all: a stylised faceted figure in a long
    work coat, one arm raised towards the town, a helmet under the other, a plaque on the plinth."""
    rnd = random.Random(seed)
    P = _root("statue")
    conc = K.mat("concrete", CONCRETE, rough=0.9, wear=0.3, grime=0.6, cracks=0.2, moss=0.4)
    K.box("step", (2.2, 2.2, 0.3), conc, loc=(0, 0, 0.15), bev=0.04, parent=P)
    tapered("plinth", (1.5, 1.5), (1.3, 1.3), 1.2, conc, loc=(0, 0, 0.3), bev=0.04, parent=P)
    K.box("cornice", (1.45, 1.45, 0.12), conc, loc=(0, 0, 1.56), bev=0.03, parent=P)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box("trim", (0.06, 0.06, 1.2), M(STEEL, metal=0.3, wear=0.3), loc=(a * 0.69, b * 0.69, 0.9), rot=(b * 4.8, -a * 4.8, 0), bev=0.01, parent=P)
    # the plaque: a steel-blue plate with raised bone lines and the colony mark
    py = -0.72
    K.box("plaque", (0.7, 0.03, 0.45), M(STEEL, metal=0.4, wear=0.3, grime=0.4), loc=(0, py, 0.95), rot=(-4.8, 0, 0), bev=0.008, parent=P)
    K.torus("plaque_mark", 0.07, 0.012, M(BONE, metal=0.3, wear=0.3), loc=(0, py - 0.03, 1.07), rot=(85, 0, 0), segs=16, parent=P)
    for j in range(3):
        K.box(f"plaque_line{j}", (0.5 - j * 0.1, 0.01, 0.025), M(BONE, metal=0.3, wear=0.3), loc=(0, py - 0.022 + j * 0.006, 0.93 - j * 0.07), rot=(-4.8, 0, 0), bev=0, parent=P)
    for a in (-1, 1):
        for b in (-1, 1):
            nut(P, (a * 0.3, py - 0.02 + b * 0.008, 0.95 + b * 0.18), r=0.015, rot=(90, 0, 0))
    moss(P, "moss0", (0.6, -0.95, 0.31), 0.18)
    moss(P, "moss1", (-0.9, 0.5, 0.31), 0.15)
    # the figure, built at human size and scaled to 2.4 m
    G = _grp(P, "figure", (0, 0.05, 1.62))
    G.scale = (1.33, 1.33, 1.33)
    bronze = M(GUNMETAL, metal=0.6, wear=0.3, grime=0.5, rough=0.45)
    K.box("base_plate", (0.7, 0.6, 0.04), bronze, loc=(0, 0, 0.02), bev=0.01, parent=G)
    for s, dy, rx in ((-1, -0.1, -7), (1, 0.08, 5)):
        x = s * 0.11
        K.box(f"boot{s}", (0.13, 0.28, 0.1), bronze, loc=(x, dy - 0.04, 0.09), bev=0.02, parent=G)
        tapered(f"leg{s}", (0.13, 0.15), (0.17, 0.19), 0.8, bronze, loc=(x, dy, 0.12), rot=(rx, 0, 0), bev=0.02, parent=G)
    tapered("coat_skirt", (0.52, 0.36), (0.42, 0.27), 0.45, bronze, loc=(0, 0, 0.55), bev=0.02, parent=G)
    tapered("torso", (0.42, 0.26), (0.5, 0.29), 0.48, bronze, loc=(0, 0, 0.97), bev=0.025, parent=G)
    K.box("collar", (0.36, 0.22, 0.08), bronze, loc=(0, 0.01, 1.48), bev=0.02, parent=G)
    K.box("belt", (0.44, 0.29, 0.05), bronze, loc=(0, 0, 0.99), bev=0.01, parent=G)
    K.box("pack", (0.32, 0.14, 0.42), bronze, loc=(0, 0.2, 1.18), bev=0.03, parent=G)
    K.cyl("neck", 0.06, 0.1, bronze, loc=(0, 0, 1.52), segs=8, bev=0, parent=G)
    H = _grp(G, "head", (0, -0.01, 1.66), (-8, 0, 0))
    K.sphere("head", 0.11, bronze, scale=(0.85, 0.95, 1.1), parent=H)
    K.box("brow", (0.15, 0.05, 0.035), bronze, loc=(0, -0.075, 0.035), bev=0.01, parent=H)
    K.box("hair", (0.17, 0.17, 0.06), bronze, loc=(0, 0.015, 0.085), rot=(-8, 0, 0), bev=0.02, parent=H)
    K.box("jaw", (0.12, 0.07, 0.05), bronze, loc=(0, -0.06, -0.075), bev=0.015, parent=H)
    # right arm raised towards the town, left arm down with the helmet held against the hip
    sh_r, sh_l = (0.27, 0.0, 1.4), (-0.27, 0.0, 1.4)

    def arm(name, sh, yaw, pitch, L, r0, r1):
        limb(G, name, sh, yaw, pitch, L, r0, r1, bronze, segs=8, bev=0.0)
        cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
        return (sh[0] + cp * math.cos(math.radians(yaw)) * L, sh[1] + cp * math.sin(math.radians(yaw)) * L, sh[2] + sp * L)

    K.sphere("shoulder_r", 0.075, bronze, loc=sh_r, parent=G)
    K.sphere("shoulder_l", 0.075, bronze, loc=sh_l, parent=G)
    el = arm("upper_r", sh_r, -78, 14, 0.33, 0.07, 0.06)
    K.sphere("elbow_r", 0.06, bronze, loc=el, parent=G)
    hd = arm("fore_r", el, -84, 22, 0.3, 0.058, 0.045)
    K.box("hand_r", (0.06, 0.13, 0.05), bronze, loc=hd, rot=(22, 0, 6), bev=0.015, parent=G)
    el = arm("upper_l", sh_l, 180, -80, 0.33, 0.07, 0.06)
    K.sphere("elbow_l", 0.06, bronze, loc=el, parent=G)
    hl = arm("fore_l", el, -120, -10, 0.25, 0.058, 0.045)
    K.sphere("helmet", 0.14, bronze, loc=(hl[0] + 0.03, hl[1] + 0.05, hl[2] - 0.06), scale=(1, 1.05, 0.9), parent=G)
    K.box("visor", (0.16, 0.04, 0.08), bronze, loc=(hl[0] + 0.03, hl[1] - 0.09, hl[2] - 0.05), bev=0.012, parent=G)
    K.box("coat_seam", (0.02, 0.02, 0.42), bronze, loc=(0.0, -0.15, 0.78), rot=(-6, 0, 0), bev=0, parent=G)
    K.box("coat_lapel", (0.02, 0.02, 0.4), bronze, loc=(0.03, -0.14, 1.2), rot=(6, 0, 0), bev=0, parent=G)
    return P


def fountain_dry(seed=61):
    """The town's fountain, 4 m across and long dry: a round concrete basin with a steel-blue coping band, a pedestal
    and bowl in the middle with its spout, stains where the water stood, dead leaves and a fallen piece of coping."""
    rnd = random.Random(seed)
    P = _root("fountain")
    conc = K.mat("concrete", CONCRETE, rough=0.9, wear=0.3, grime=0.6, cracks=0.3, moss=0.3)
    R = 2.0
    lathe("basin_wall", [(R - 0.25, 0.0), (R, 0.0), (R, 0.45), (R - 0.04, 0.5), (R - 0.21, 0.5), (R - 0.25, 0.45)], conc, segs=40, parent=P)
    lathe("coping", [(R - 0.005, 0.32), (R + 0.02, 0.32), (R + 0.02, 0.4), (R - 0.005, 0.4)], M(STEEL, metal=0.3, wear=0.3, grime=0.5), segs=40, parent=P)
    K.cyl("floor", R - 0.24, 0.12, K.mat("basin_floor", "#9a978c", rough=0.95, grime=0.7, cracks=0.4, moss=0.2), loc=(0, 0, 0.06), segs=40, bev=0, parent=P)
    K.cyl("stain", R - 0.45, 0.004, K.mat("stain", "#7f7b70", rough=0.95, grime=0.5), loc=(0.15, 0.1, 0.122), segs=24, bev=0, parent=P)
    # the pedestal, the bowl and the spout
    lathe("pedestal", [(0.001, 0.12), (0.42, 0.12), (0.42, 0.22), (0.3, 0.3), (0.24, 0.75), (0.3, 0.82), (0.001, 0.82)], conc, segs=20, parent=P)
    lathe("bowl", [(0.2, 0.8), (0.85, 0.98), (0.95, 1.08), (0.9, 1.12), (0.82, 1.04), (0.25, 0.92)], conc, segs=24, parent=P)
    K.cyl("bowl_floor", 0.84, 0.01, K.mat("stain", "#7f7b70", rough=0.95, grime=0.5), loc=(0, 0, 0.95), segs=24, r2=0.5, bev=0, parent=P)
    K.cyl("spout_base", 0.14, 0.2, conc, loc=(0, 0, 1.02), segs=12, bev=0.02, parent=P)
    K.cyl("spout", 0.04, 0.55, M(STEEL, metal=0.4, wear=0.3, grime=0.4), loc=(0, 0, 1.38), segs=10, bev=0, parent=P)
    K.cyl("spout_band", 0.05, 0.06, M(MUSTARD, metal=0.3, wear=0.3), loc=(0, 0, 1.3), segs=10, bev=0.005, parent=P)
    K.cyl("nozzle", 0.065, 0.08, M(STEEL, metal=0.4, wear=0.3, grime=0.4), loc=(0, 0, 1.69), segs=10, r2=0.04, bev=0.005, parent=P)
    rust_patch(P, (0.0, -0.042, 1.45), (0.03, 0.004, 0.18))
    # the coping chunk that fell in, cracks and leaves
    K.box("chunk", (0.35, 0.22, 0.16), conc, loc=(1.35, -0.85, 0.2), rot=(12, 8, 40), bev=0.03, parent=P)
    K.box("gap", (0.36, 0.26, 0.04), K.mat("concrete_in", "#8e8b82", rough=0.95, cracks=0.6), loc=(1.62, -1.02, 0.49), rot=(0, 0, -32), bev=0.01, parent=P)
    for k in range(5):
        a = rnd.uniform(0, 360)
        K.box("crack", (rnd.uniform(0.3, 0.7), 0.015, 0.01), _dark(), loc=(math.cos(math.radians(a)) * 1.2, math.sin(math.radians(a)) * 1.2, 0.121),
              rot=(0, 0, a + rnd.uniform(60, 120)), bev=0, parent=P)
    _leaves(P, (0, 0), 1.3, 14, rnd, z=0.125)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.4
        moss(P, f"moss{k}", (math.cos(a) * (R + 0.05), math.sin(a) * (R + 0.05), 0.02), 0.22, flat=0.3)
    return P


def planter_tree_dead(seed=62):
    """A raised colony planter, 1.8 m square, with a dead tree, 4.5 m: concrete walls under a steel-blue capping rail
    and a seat ledge, dry soil, the grey trunk forking into bare branches, a broken tree guard."""
    rnd = random.Random(seed)
    P = _root("tree_planter")
    conc = _concrete(0.3)
    steel = _steel()
    S, H, t = 1.8, 0.65, 0.14
    for s in (-1, 1):
        K.box(f"wall_x{s}", (S, t, H), conc, loc=(0, s * (S - t) / 2, H / 2), bev=0.03, parent=P)
        K.box(f"wall_y{s}", (t, S - 2 * t, H), conc, loc=(s * (S - t) / 2, 0, H / 2), bev=0.03, parent=P)
        K.box(f"cap_x{s}", (S + 0.04, t + 0.08, 0.05), steel, loc=(0, s * (S - t) / 2, H + 0.025), bev=0.01, parent=P)
        K.box(f"cap_y{s}", (t + 0.08, S - 2 * t, 0.05), steel, loc=(s * (S - t) / 2, 0, H + 0.025), bev=0.01, parent=P)
    K.box("soil", (S - 2 * t, S - 2 * t, 0.04), earth(), loc=(0, 0, H - 0.1), bev=0, parent=P)
    for k in range(4):
        rust_patch(P, (rnd.uniform(-0.6, 0.6), -S / 2 - 0.003, rnd.uniform(0.15, 0.5)), (rnd.uniform(0.1, 0.2), 0.004, rnd.uniform(0.05, 0.12)))
    dead = K.mat("deadwood", DEADWOOD, rough=0.95, grime=0.2, noise_scale=6.0, cracks=0.5)
    z0 = H - 0.1
    K.cyl("flare", 0.2, 0.2, dead, loc=(0, 0, z0 + 0.08), segs=10, r2=0.13, bev=0.01, parent=P)
    T = _grp(P, "trunk", (0, 0, z0), (3, -2, 0))
    K.cyl("trunk", 0.13, 2.2, dead, loc=(0, 0, 1.1), segs=10, r2=0.09, bev=0.01, parent=T)
    for k, (yaw, pitch, L) in enumerate(((20, 55, 1.6), (150, 60, 1.5), (260, 50, 1.4), (90, 72, 1.7))):
        b = (0, 0, 2.0 + k * 0.06)
        limb(T, f"branch{k}", b, yaw, pitch, L, 0.07, 0.025, dead, segs=7, bev=0)
        cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
        for f, dy in ((0.45, 45), (0.75, -40)):
            m = (b[0] + cp * math.cos(math.radians(yaw)) * L * f, b[1] + cp * math.sin(math.radians(yaw)) * L * f, b[2] + sp * L * f)
            limb(T, f"twig{k}{f}", m, yaw + dy, pitch + rnd.uniform(-10, 15), rnd.uniform(0.45, 0.7), 0.03, 0.008, dead, segs=5, bev=0)
    limb(T, "low_stub", (0, 0, 1.1), 300, 20, 0.35, 0.05, 0.035, dead, segs=6, bev=0)
    # the tree guard: three stakes, one snapped, and a slack strap
    for k, (a, h, tilt) in enumerate(((90, 1.4, 0), (210, 1.4, 6), (330, 0.6, 0))):
        x, y = math.cos(math.radians(a)) * 0.4, math.sin(math.radians(a)) * 0.4
        K.box(f"stake{k}", (0.05, 0.05, h), wood(k, wear=0.3), loc=(x, y, z0 + h / 2 - 0.05), rot=(tilt, 0, 0), bev=0.006, parent=P)
    K.box("strap", (0.82, 0.02, 0.04), K.mat("tyre", "#26282a", rough=0.9, grime=0.3), loc=(0.0, 0.2, z0 + 1.1), rot=(0, 8, -30), bev=0, parent=P)
    _leaves(P, (0, 0), 0.6, 10, rnd, z=H - 0.075)
    _leaves(P, (0.3, -1.4), 1.0, 6, rnd)
    return P


# ================================================================== road
def sign_gantry(seed=63):
    """An overhead road sign gantry, 8 m wide and 6.3 m tall: two braced steel legs on concrete footings, a box truss
    with a service walkway, and two steel-blue direction boards with bone arrows, the right one hanging askew."""
    rnd = random.Random(seed)
    P = _root("gantry")
    steel = M(STEEL, metal=0.3, wear=0.3, grime=0.45)
    gra = _dark()
    galv = M(ZINC, metal=0.5, wear=0.3, grime=0.45)
    X, za, zb, d = 3.85, 5.1, 5.9, 0.25
    for s in (-1, 1):
        x = s * X
        K.box(f"footing{s}", (1.0, 1.0, 0.4), _concrete(0.3), loc=(x, 0, 0.15), bev=0.03, parent=P)
        K.box(f"base{s}", (0.7, 0.8, 0.03), gra, loc=(x, 0, 0.365), bev=0.006, parent=P)
        for b in (-1, 1):
            K.box(f"leg{s}{b}", (0.16, 0.16, zb + 0.1 - 0.38), steel, loc=(x, b * d, 0.38 + (zb + 0.1 - 0.38) / 2), bev=0.015, parent=P)
            nut(P, (x + 0.25, b * d, 0.39), r=0.025)
            nut(P, (x - 0.25, b * d, 0.39), r=0.025)
        for k in range(6):
            z = 0.6 + k * 0.85
            beam(P, f"leg_brace{s}{k}", (x, -d, z), (x, d, z + 0.85), 0.05, steel, bev=0)
            K.box(f"leg_tie{s}{k}", (0.06, 2 * d, 0.06), steel, loc=(x, 0, z), bev=0, parent=P)
        K.box(f"leg_band{s}", (0.2, 2 * d + 0.2, 0.25), M(MUSTARD, wear=0.3), loc=(x, 0, 1.0), bev=0.01, parent=P)
    # the box truss: four chords, verticals and diagonals on the front and back faces
    for z in (za, zb):
        for b in (-1, 1):
            K.box(f"chord{z}{b}", (2 * X + 0.16, 0.12, 0.12), steel, loc=(0, b * d, z), bev=0.012, parent=P)
    n = 8
    for k in range(n + 1):
        x = -X + 2 * X * k / n
        for b in (-1, 1):
            K.box(f"vert{k}{b}", (0.07, 0.07, zb - za), steel, loc=(x, b * d, (za + zb) / 2), bev=0, parent=P)
        K.box(f"top_tie{k}", (0.06, 2 * d, 0.06), steel, loc=(x, 0, zb), bev=0, parent=P)
        if k < n:
            x1 = -X + 2 * X * (k + 1) / n
            for b in (-1, 1):
                a, c = ((x, b * d, za), (x1, b * d, zb)) if k % 2 == 0 else ((x, b * d, zb), (x1, b * d, za))
                beam(P, f"diag{k}{b}", a, c, 0.05, steel, bev=0)
    # service walkway in front of the boards, with a rail
    wy = -d - 0.55
    K.box("walk", (2 * X - 0.3, 0.6, 0.04), galv, loc=(0, wy, za - 0.6), bev=0.005, parent=P)
    for k in range(7):
        x = -X + 0.3 + k * (2 * X - 0.6) / 6
        beam(P, f"walk_hanger{k}", (x, -d, za), (x, wy - 0.25, za - 0.6), 0.04, galv, bev=0)
        K.box(f"rail_post{k}", (0.04, 0.04, 1.0), galv, loc=(x, wy - 0.28, za - 0.08), bev=0, parent=P)
    for z in (za - 0.1, za + 0.4):
        K.box(f"rail{z}", (2 * X - 0.3, 0.04, 0.04), galv, loc=(0, wy - 0.28, z), bev=0, parent=P)
    # ladder up the left leg
    for s in (-1, 1):
        K.box(f"ladder_side{s}", (0.04, 0.04, za - 0.9), galv, loc=(-X + 0.12 + s * 0.2, -d - 0.25, 0.9 + (za - 0.9) / 2), bev=0, parent=P)
    for k in range(int((za - 1.1) / 0.3)):
        K.box(f"rung{k}", (0.4, 0.03, 0.03), galv, loc=(-X + 0.12, -d - 0.25, 1.1 + k * 0.3), bev=0, parent=P)
    # the boards, hung on the truss front
    by = -d - 0.12
    for k, (cx, w, tilt) in enumerate(((-1.8, 3.0, 0.0), (1.75, 2.7, 4.5))):
        B = _grp(P, f"board{k}", (cx, by, (za + zb) / 2 + 0.05), (0, tilt, 0))
        K.box(f"board{k}", (w - 0.04, 0.06, 1.56), M(STEEL, wear=0.1, grime=0.4, scratches=0.1), bev=0.01, parent=B)
        for sz in (-1, 1):
            K.box(f"board_rail{k}", (w, 0.08, 0.06), gra, loc=(0, 0, sz * 0.78), bev=0.01, parent=B)
            K.box(f"board_post{k}", (0.06, 0.08, 1.62), gra, loc=(sz * w / 2, 0, 0), bev=0.01, parent=B)
        K.box(f"border{k}", (w - 0.18, 0.006, 1.42), M(BONE, wear=0.3), loc=(0, -0.042, 0), bev=0, parent=B)
        K.box(f"field{k}", (w - 0.26, 0.006, 1.34), M(STEEL, wear=0.05, grime=0.35, scratches=0.1), loc=(0, -0.046, 0), bev=0, parent=B)
        ink = M(BONE, wear=0.25, scratches=0)
        if k == 0:
            arrow = [(-0.12, -0.55), (0.12, -0.55), (0.12, 0.15), (0.32, 0.15), (0.0, 0.55), (-0.32, 0.15), (-0.12, 0.15)]
            plate_xz(f"arrow{k}", arrow, 0.006, ink, loc=(-0.7, -0.052, 0), parent=B, bev=0)
            stencil(B, "7", (0.55, -0.056, 0.05), 0.75, ink, extrude=0.004)
        else:
            arrow = [(-0.1, -0.55), (0.12, -0.55), (0.12, -0.05), (0.32, 0.2), (0.42, 0.08), (0.48, 0.55), (0.05, 0.47), (0.17, 0.37), (-0.04, 0.08)]
            plate_xz(f"arrow{k}", arrow, 0.006, ink, loc=(0.55, -0.052, 0), parent=B, bev=0)
            stencil(B, "12", (-0.5, -0.056, 0.05), 0.65, ink, extrude=0.004)
        for x in (-w / 3, w / 3):
            K.box(f"clip{k}", (0.1, 0.1, 0.2), gra, loc=(x, 0.06, 0.62), bev=0, parent=B)
        rust_patch(B, (rnd.uniform(-w / 3, w / 3), -0.05, rnd.uniform(-0.6, -0.4)), (0.3, 0.004, 0.12))
    return P


def crosswalk_sign(seed=64):
    """A colony crossing sign, 2.9 m: a steel-blue square with a bone border and triangle, a graphite walker on it,
    and a dead mustard beacon on top of the post."""
    P = _root("crossing")
    steel = M(STEEL, metal=0.4, wear=0.3, grime=0.4)
    h = 2.6
    _sign_post(P, h, steel)
    zc = h - 0.38
    sq = [(-0.31, -0.31), (0.31, -0.31), (0.31, 0.31), (-0.31, 0.31)]
    _sign_back(P, sq, zc, 0.62)
    plate_xz("border", [(x * 0.98, z * 0.98) for x, z in sq], 0.012, M(BONE, wear=0.3, grime=0.4, rough=0.5), loc=(0, -0.01, zc), parent=P, bev=0.002)
    plate_xz("face", [(x * 0.88, z * 0.88) for x, z in sq], 0.012, M(STEEL, wear=0.3, grime=0.35, rough=0.5, scratches=0.4), loc=(0, -0.016, zc), parent=P, bev=0.002)
    plate_xz("tri", _poly(3, 0.25, math.pi / 2), 0.006, M(BONE, wear=0.25, grime=0.3, scratches=0), loc=(0, -0.022, zc - 0.03), parent=P, bev=0)
    ink = M(GRAPHITE, wear=0.2, scratches=0)
    y = -0.026
    K.cyl("head", 0.024, 0.004, ink, loc=(0.01, y, zc + 0.08), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    plate_xz("body", [(-0.025, -0.05), (0.012, -0.05), (0.03, 0.045), (-0.005, 0.05)], 0.004, ink, loc=(0, y, zc), parent=P, bev=0)
    plate_xz("leg_f", [(0.0, -0.045), (0.02, -0.04), (0.07, -0.13), (0.048, -0.135)], 0.004, ink, loc=(0, y, zc), parent=P, bev=0)
    plate_xz("leg_b", [(-0.02, -0.045), (0.0, -0.04), (-0.045, -0.135), (-0.067, -0.13)], 0.004, ink, loc=(0, y, zc), parent=P, bev=0)
    plate_xz("arm_f", [(0.015, 0.04), (0.025, 0.025), (0.07, -0.02), (0.06, -0.03)], 0.004, ink, loc=(0, y, zc), parent=P, bev=0)
    plate_xz("arm_b", [(-0.01, 0.04), (0.0, 0.025), (-0.05, -0.02), (-0.06, -0.01)], 0.004, ink, loc=(0, y, zc), parent=P, bev=0)
    plate_xz("zebra", [(-0.15, -0.15), (0.15, -0.15), (0.15, -0.135), (-0.15, -0.135)], 0.004, ink, loc=(0, y, zc - 0.005), parent=P, bev=0)
    K.box("beacon_mount", (0.1, 0.1, 0.04), M(GRAPHITE, metal=0.3), loc=(0, 0.04, h + 0.02), bev=0.006, parent=P)
    K.cyl("beacon", 0.09, 0.14, M(GRAPHITE, metal=0.3), loc=(0, 0.04, h + 0.11), segs=14, bev=0.008, parent=P)
    K.sphere("beacon_lens", 0.085, K.mat(f"lens{AMBER_D}", AMBER_D, rough=0.15, metal=0.1, grime=0.3), loc=(0, 0.04, h + 0.18), scale=(1, 1, 0.7), parent=P)
    K.cyl("collar", 0.12, 0.04, _concrete(0.3), loc=(0, 0.04, 0.0), segs=12, bev=0.008, parent=P)
    return P


def bus_wreck(seed=65):
    """A colony bus, 10 m, gutted and settled on flat tyres: a bone body over a steel-blue skirt with a mustard
    beltline, a pill-edged roof with its cooling unit, most windows smashed to the frames, seats inside, the front
    doors folded open and rust coming through."""
    rnd = random.Random(seed)
    P = _root("bus")
    bone = M(BONE, wear=0.3, grime=0.6, scratches=0.6, moss=0.2)
    skirt = M(STEEL, wear=0.3, grime=0.6, scratches=0.6)
    gra = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.5)
    glass = _glass()
    seat = K.mat("seatcloth", "#3d4b55", rough=0.9, grime=0.5)
    L, W = 10.0, 2.5
    rw = 0.5
    B = _grp(P, "body", (0, 0, 0.46), (-1.6, 0.8, 0))
    # B local: z = 0 is the floor line, x forward (towards +x), the door side towards -y
    K.box("chassis", (L - 0.4, W - 0.3, 0.3), gra, loc=(0, 0, -0.05), bev=0.02, parent=B)
    K.box("floor", (L - 0.1, W - 0.1, 0.06), gra, loc=(0, 0, 0.15), bev=0.01, parent=B)
    xf, xr = L / 2 - 2.0, -L / 2 + 2.3
    arches = (xf, xr)
    xd = xf + 0.5   # the door opening starts here
    # skirt panels between and around the wheel arches, both sides
    for s in (-1, 1):
        y = s * W / 2
        segs_x = [(-L / 2, xr - 0.62), (xr + 0.62, xf - 0.62), (xf + 0.62, L / 2)]
        for k, (a, b) in enumerate(segs_x):
            if s < 0 and k == 2:   # the door opening runs down to the step
                continue
            K.box(f"skirt{s}{k}", (b - a, 0.05, 0.8), skirt, loc=((a + b) / 2, y, 0.45), bev=0.015, parent=B)
        for x in arches:
            K.box(f"over_arch{s}{x}", (1.24, 0.05, 0.22), skirt, loc=(x, y, 0.74), bev=0, parent=B)
            K.torus(f"arch{s}{x}", 0.6, 0.035, gra, loc=(x, y, 0.0), rot=(90, 0, 0), arc=180, segs=16, parent=B)
            K.box(f"well{s}{x}", (1.2, 0.04, 0.6), K.mat("hole", "#141517", rough=0.9), loc=(x, s * (W / 2 - 0.3), 0.3), bev=0, parent=B)
        K.box(f"belt{s}", (L - 0.05, 0.06, 0.1), M(MUSTARD, wear=0.3, grime=0.5), loc=(0, y, 0.9), bev=0.01, parent=B)
    # window band: pillars, sill, header; glass mostly smashed
    zs, zh = 0.95, 2.2
    pillars = [-L / 2 + 0.05 + k * 1.2 for k in range(9)]
    for s in (-1, 1):
        y = s * (W / 2 - 0.01)
        for x in pillars:
            if s < 0 and x > xd:
                continue
            K.box(f"pillar{s}{x}", (0.12, 0.06, zh - zs), bone, loc=(x, y, (zs + zh) / 2), bev=0, parent=B)
        K.box(f"upper{s}", (L - 0.05, 0.06, 0.45), bone, loc=(0, y, zh + 0.22), bev=0.015, parent=B)
        for k in range(len(pillars) - 1):
            x0, x1 = pillars[k], pillars[k + 1]
            cx = (x0 + x1) / 2
            if s < 0 and x1 > xd:
                continue
            r = rnd.random()
            if r < 0.3:
                K.box(f"pane{s}{k}", (x1 - x0 - 0.12, 0.012, zh - zs - 0.05), glass, loc=(cx, y, (zs + zh) / 2), bev=0, parent=B)
                if s < 0:
                    _cracks(B, (cx, y - 0.01, (zs + zh) / 2 + 0.1), rnd, n=4, L=0.5)
            elif r < 0.75:
                hgt = rnd.uniform(0.15, 0.45)
                _plate(f"pane_shard{s}{k}", [(-0.5, 0), (0.5, 0), (0.5, hgt), (0.15, hgt * 0.4), (-0.1, hgt * 1.2), (-0.5, hgt * 0.6)], 0.012, glass, B,
                       loc=(cx, y, zs + 0.02), rot=(90, 0, 0))
    # the front door opening on the near side, its two leaves folded back
    door_x = (xd + L / 2 - 0.1) / 2
    K.box("door_step", (1.2, 0.5, 0.25), gra, loc=(door_x, -W / 2 + 0.3, 0.2), bev=0.01, parent=B)
    for k in (-1, 1):
        K.box(f"door_leaf{k}", (0.32, 0.05, 1.95), M(STEEL, wear=0.3, grime=0.55), loc=(door_x + k * 0.5, -W / 2 + 0.12, 1.15), rot=(0, 0, k * 70), bev=0.01, parent=B)
        K.box(f"door_glass{k}", (0.22, 0.06, 1.0), glass, loc=(door_x + k * 0.5, -W / 2 + 0.12, 1.45), rot=(0, 0, k * 70), bev=0, parent=B)
    # roof: a pill-edged slab and the cooling unit
    K.slab("roof", L, W, 0.22, 0.3, M(BONE, wear=0.3, grime=0.5, moss=0.45), loc=(0, 0, zh + 0.52), bev=0.04, parent=B)
    K.box("cooler", (2.4, 1.6, 0.36), M(BONE, wear=0.3, grime=0.5, moss=0.3), loc=(-1.5, 0, zh + 0.81), bev=0.03, parent=B)
    K.box("cooler_rim", (2.46, 1.66, 0.06), skirt, loc=(-1.5, 0, zh + 0.66), bev=0.01, parent=B)
    FR.vent(B, (-1.5, -0.82, zh + 0.82), 1.6, 0.26, skirt, gra, n=3)
    K.box("hatch", (0.7, 0.7, 0.06), skirt, loc=(2.2, 0, zh + 0.65), rot=(0, -8, 0), bev=0.012, parent=B)
    # the front: windscreen, destination board, lamps, bumper
    fx = L / 2
    K.box("front_low", (0.06, W, 0.85), skirt, loc=(fx, 0, 0.5), bev=0.015, parent=B)
    K.box("front_frame", (0.08, W, 0.1), bone, loc=(fx, 0, zs - 0.03), bev=0.01, parent=B)
    K.box("windscreen", (0.02, W - 0.25, zh - zs - 0.05), glass, loc=(fx, 0, (zs + zh) / 2), rot=(0, -3, 0), bev=0, parent=B)
    _cracks(B, (fx + 0.04, -0.4, 1.6), rnd, n=5, L=0.8, face="x")
    for s in (-1, 1):
        K.box(f"front_pillar{s}", (0.08, 0.12, zh - zs), bone, loc=(fx, s * (W / 2 - 0.06), (zs + zh) / 2), bev=0.012, parent=B)
        K.box(f"headlamp{s}", (0.03, 0.3, 0.16), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(fx + 0.04, s * 0.85, 0.55), bev=0.006, parent=B)
        K.box(f"indicator{s}", (0.03, 0.14, 0.08), K.mat(f"lens{AMBER_D}", AMBER_D, rough=0.15, metal=0.1, grime=0.3), loc=(fx + 0.04, s * 1.08, 0.55), bev=0.004, parent=B)
        K.box(f"mirror_arm{s}", (0.4, 0.03, 0.03), gra, loc=(fx + 0.15, s * (W / 2 + 0.05), 2.0), rot=(0, 0, s * 20), bev=0, parent=B)
        K.box(f"mirror{s}", (0.04, 0.16, 0.3), gra, loc=(fx + 0.33, s * (W / 2 + 0.12), 1.85), bev=0.01, parent=B)
    K.box("dest_board", (0.04, W - 0.4, 0.3), K.mat("deadscreen", "#1d2a30", rough=0.12, metal=0.2), loc=(fx + 0.01, 0, zh + 0.22), bev=0.006, parent=B)
    stencil(B, "7", (fx + 0.035, -0.75, zh + 0.22), 0.22, M(MUSTARD, wear=0.6, scratches=0), rot=(90, 0, 90), extrude=0.003)
    K.box("bumper_f", (0.14, W + 0.04, 0.2), gra, loc=(fx + 0.06, 0, 0.18), bev=0.02, parent=B)
    FR.hazard(B, (fx + 0.14, 0, 0.18), W - 0.2, 0.12, M(MUSTARD, wear=0.3), gra, n=5, axis="x")
    # the back: engine grille, tail lamps, a small rear window broken out
    bx = -L / 2
    K.box("back_low", (0.06, W, 0.9), skirt, loc=(bx, 0, 0.5), bev=0.015, parent=B)
    K.box("back_up", (0.06, W, zh + 0.45 - 0.95), bone, loc=(bx, 0, (0.95 + zh + 0.45) / 2), bev=0.015, parent=B)
    K.box("rear_hole", (0.02, 1.4, 0.6), K.mat("hole", "#141517", rough=0.9), loc=(bx - 0.03, 0, 1.75), bev=0, parent=B)
    for s in (-1, 1):
        K.box(f"tail{s}", (0.03, 0.18, 0.26), K.mat(f"lens{RED_D}", RED_D, rough=0.15, metal=0.1, grime=0.3), loc=(bx - 0.03, s * 1.02, 0.6), bev=0.006, parent=B)
    K.box("bumper_r", (0.14, W + 0.04, 0.2), gra, loc=(bx - 0.06, 0, 0.18), bev=0.02, parent=B)
    # inside: rows of seats seen through the empty frames
    for k in range(7):
        x = -L / 2 + 0.9 + k * 0.95
        for s in (-1, 1):
            if s < 0 and x > xf + 0.2:
                continue
            K.box(f"seat{k}{s}", (0.45, 0.85, 0.4), seat, loc=(x, s * 0.65, 0.48), bev=0, parent=B)
            K.box(f"seat_back{k}{s}", (0.1, 0.85, 0.6), seat, loc=(x - 0.22, s * 0.65, 0.95), rot=(0, -8, 0), bev=0, parent=B)
    K.box("dash", (0.5, W - 0.4, 0.45), gra, loc=(fx - 0.35, 0, 0.9), bev=0.02, parent=B)
    K.torus("wheel_steer", 0.24, 0.025, gra, loc=(fx - 0.7, 0.6, 1.25), rot=(0, -60, 0), segs=12, parent=B)
    # rust through the paint
    for k in range(6):
        s = rnd.choice((-1, 1))
        x = rnd.uniform(-L / 2 + 0.3, L / 2 - 0.3)
        rust_patch(B, (x, s * (W / 2 + 0.03), rnd.uniform(0.25, 0.7)), (rnd.uniform(0.2, 0.5), 0.004, rnd.uniform(0.1, 0.25)))
    for k in range(3):
        rust_patch(B, (rnd.uniform(-4, 4), rnd.uniform(-0.8, 0.8), zh + 0.635), (rnd.uniform(0.4, 0.8), rnd.uniform(0.3, 0.6), 0.004), rot=(0, 0, rnd.uniform(0, 90)))
    # wheels: flat tyres, the near side settled lower
    for x in arches:
        for s in (-1, 1):
            tyre(P, f"wheel{x}{s}", (x, s * (W / 2 - 0.2), rw * (1 - 0.2)), rw, 0.32, flat=0.2 if s < 0 else 0.1, lugs=0)
    _shards(P, (0.5, -1.7), 1.6, 8, rnd)
    return P


PROPS = {
    "street_lamp": street_lamp,
    "traffic_light": traffic_light,
    "traffic_light_fallen": traffic_light_fallen,
    "road_barrier_arm": road_barrier_arm,
    "toll_booth": toll_booth,
    "bench_colony": bench_colony,
    "trash_bin": trash_bin,
    "recycling_bins": recycling_bins,
    "newspaper_kiosk": newspaper_kiosk,
    "phone_booth": phone_booth,
    "vending_machine": vending_machine,
    "fire_hydrant": fire_hydrant,
    "manhole_cover_open": manhole_cover_open,
    "street_planter": street_planter,
    "bollard_row": bollard_row,
    "bike_rack": bike_rack,
    "parking_meter": parking_meter,
    "info_board": info_board,
    "colony_flag_pole": colony_flag_pole,
    "statue_colony_founder": statue_colony_founder,
    "fountain_dry": fountain_dry,
    "planter_tree_dead": planter_tree_dead,
    "sign_gantry": sign_gantry,
    "crosswalk_sign": crosswalk_sign,
    "bus_wreck": bus_wreck,
}
