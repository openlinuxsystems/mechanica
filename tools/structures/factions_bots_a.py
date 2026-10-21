"""Faction marks, set A: what the bot factions build and leave in the world, so a player can
tell whose ground they are on from a distance. Two design languages beside the four of docs/art-direction.md, under
its rules (silhouette first, framed construction, small bevels, round only where real, show the hardware, real scale).

Scuttlers (the crab-chassis swarm bots): nests and burrows of scavenged grey and rust metal, built from their own
body parts: hexagonal shells, black-tipped crab legs on steel ball joints, serrated claw jaws and stalked eyes with a
green lens. The variant colour is the only paint. A painted hexagon slashed by three claw marks is their sign. The
nest (the signature lair), a charging cradle, a head totem, a blue drill bore, a yellow stomp ring, a red blast pit
and a seed cache.

Rakehands (the pitchfork-handed farmhand bots): a twisted farm of bales, field posts, burlap rags and steel tines,
painted in the three types' colours (field green and mustard, baler red and cream, scarecrow purple and gold). Tines
always point out or up. A three-tine fork painted in a trim colour is their sign. The harvest effigy (the signature
lair), a pitchfork palisade, a bale fort, a scarecrow perch, a rake ring, a tool hoard and a trampled marker.

Each builder returns an empty with the piece under it, its origin on the ground at the piece's centre (Blender Z up,
front towards -Y). They are world objects (docs/world-generation.md, Faction sites), built by
build_structures.py props. Most carry no collider: the object's data gives one. The pieces you walk into or onto (the
nest's burrow, the cradle's pad, the shrine's plinth, the bale fort) carry kit.collider_box shapes, exported as their
``collider`` mesh.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, GRAPHITE, RUST, ZINC, GUNMETAL, COPPER, STONE, STONE_D, glow
from props import _root, wood, plank, CHAR
from props_settler_a import _orient, rod, beam, prism, lump, SOIL
from props_farm import _grp, _straw, _straw_bed, turned, long_tool, DARK

# ---------------------------------------------------------------- Scuttler palette (from tools/scuttler)
SC_GREEN, SC_BLUE, SC_YELLOW, SC_RED, SC_SEED = "#5fae3e", "#3e7fd0", "#e6b31e", "#d0402e", "#2f5a2c"
SC_GUN, SC_STEEL, SC_TIP = "#2e3136", "#8d9096", "#191a1c"
SC_SEED_LID = "#4c7d42"
SCRAP = "#4a4e54"
EYE = "#5cff6e"
BURLAP, SEEDS = "#76562f", "#5a3d1f"
WATER = "#2c4a5a"

# ---------------------------------------------------------------- Rakehand palette (from tools/rakehand)
RK_GREEN, RK_MUSTARD = "#3f7a3a", "#e2b23a"
RK_RED, RK_CREAM = "#a33122", "#e8e2d2"
RK_PURPLE, RK_GOLD = "#5b3a5c", "#c9a02a"
RK_TINE = "#8d9096"
RK_BURLAP, RK_RAG, RK_ROPE = "#8a6a3e", "#5f4e36", "#6b5434"
CROP = "#c9b25e"


# ================================================================== shared helpers
def taper(P, name, a, b, r1, r2, mat, segs=10, bev=0.0):
    """A round bar from a (radius r1) to b (radius r2): leg sleeves, spikes, tines."""
    return _orient(K.cyl(name, r1, math.dist(a, b), mat, segs=segs, bev=bev, r2=r2, parent=P), a, b)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ball(P, name, r, mat, loc, u=8, v=5, scale=(1, 1, 1)):
    """A low-poly ball (ball joints, lenses): fewer faces than K.sphere, smooth shaded."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.scale = scale
    return K.place(ob, loc, (0, 0, 0), P)


def sheet(P, name, pts, mat, thick=0.02):
    """A thin flat polygon (a rag, a torn plate) from 3D points, given thickness."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    bm.faces.new(vs)
    ob = K.from_bm(name, bm, mat)
    sol = ob.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = thick
    return K.place(ob, parent=P)


# ---------------------------------------------------------------- Scuttler kit
def sc_paint(col, **kw):
    kw.setdefault("wear", 0.25)
    kw.setdefault("grime", 0.45)
    kw.setdefault("scratches", 0.5)
    kw.setdefault("metal", 0.3)
    return M(col, **kw)


def sc_gun():
    return M(SC_GUN, metal=0.55, wear=0.2, grime=0.4)


def sc_steel():
    return M(SC_STEEL, metal=0.75, wear=0.1, grime=0.3, rough=0.4)


def sc_tip():
    return M(SC_TIP, metal=0.4, wear=0.15, grime=0.2)


def sc_scrap(shade=0):
    return M([SCRAP, GUNMETAL, ZINC, RUST][shade % 4], metal=0.5, wear=0.2, grime=0.55, scratches=0.4, rough=0.7)


def leg(P, name, hip, knee, tip, r, paint, spike=0.35):
    """A Scuttler crab leg at any scale: painted sleeves with gunmetal collars, steel ball joints, a black spike tip."""
    ball(P, f"{name}_hip", r * 1.35, sc_steel(), hip)
    ball(P, f"{name}_knee", r * 1.3, sc_steel(), knee)
    taper(P, f"{name}_thigh", hip, knee, r, r * 0.9, paint, segs=10)
    mid = lerp(knee, tip, 1 - spike)
    taper(P, f"{name}_shin", knee, mid, r * 0.95, r * 0.8, paint, segs=10)
    taper(P, f"{name}_spike", mid, tip, r * 0.8, r * 0.08, sc_tip(), segs=8)
    for t in (0.82,):
        taper(P, f"{name}_collar", lerp(hip, knee, t - 0.03), lerp(hip, knee, t + 0.03), r * 1.08, r * 1.08, sc_gun(), segs=10)
    taper(P, f"{name}_collar2", lerp(knee, mid, 0.12), lerp(knee, mid, 0.18), r * 1.05, r * 1.05, sc_gun(), segs=10)


def eye_head(P, name, loc, s, lit=True, yaw=0.0, paint=None):
    """A Scuttler eye head: a boxy housing with a brass porthole ring round the green lens (dead lenses are dark)."""
    G = _grp(P, name, loc, (0, 0, yaw))
    K.box(f"{name}_case", (0.34 * s, 0.3 * s, 0.28 * s), paint or sc_paint(SC_GREEN), loc=(0, 0, 0), bev=0.03 * s, parent=G)
    K.box(f"{name}_brow", (0.36 * s, 0.14 * s, 0.05 * s), sc_gun(), loc=(0, -0.1 * s, 0.155 * s), rot=(-12, 0, 0), bev=0.01 * s, parent=G)
    K.torus(f"{name}_ring", 0.1 * s, 0.022 * s, M(COPPER, metal=0.8, wear=0.1, rough=0.35), loc=(0, -0.16 * s, 0), rot=(90, 0, 0), segs=16, parent=G)
    lens = glow(EYE, 5) if lit else K.mat("deadlens", "#1f2a24", rough=0.15, metal=0.2)
    ball(G, f"{name}_lens", 0.085 * s, lens, (0, -0.15 * s, 0), scale=(1, 0.45, 1))
    return G


def claw(P, name, loc, s, rot=(0, 0, 0), paint=None, gape=28):
    """A serrated claw jaw pair (the Scuttlers' claw arms), pointing along local -Y, opened gape degrees."""
    G = _grp(P, name, loc, rot)
    paint = paint or sc_paint(SC_GREEN)
    ball(G, f"{name}_wrist", 0.09 * s, sc_steel(), (0, 0.02 * s, 0), u=6, v=4)
    for k, (a, z) in enumerate(((gape / 2, 0.035), (-gape / 2, -0.035))):
        J = _grp(G, f"{name}_jaw{k}", (0, 0, z * s), (a, 0, 0))
        K.box(f"{name}_jawbox{k}", (0.14 * s, 0.42 * s, 0.07 * s), paint, loc=(0, -0.21 * s, 0), bev=0.012 * s, parent=J)
        sgn = -1 if k == 0 else 1
        for t in range(5):
            K.cyl(f"{name}_tooth{k}", 0.022 * s, 0.07 * s, sc_steel(), loc=(0, -0.06 * s - t * 0.075 * s, sgn * 0.065 * s), rot=(0 if sgn > 0 else 180, 0, 0),
                  segs=4, r2=0.0, bev=0, parent=J)
    return G


def hex_shell(P, name, r, h, paint, loc, rot=(0, 0, 0), belly=True):
    """A Scuttler body shell: a low hexagonal top over a gunmetal belly pan."""
    G = _grp(P, name, loc, rot)
    K.cyl(f"{name}_top", r, h * 0.55, paint, loc=(0, 0, h * 0.72), segs=6, r2=r * 0.82, bev=0.03 * r, parent=G)
    if belly:
        K.cyl(f"{name}_pan", r * 0.92, h * 0.45, sc_gun(), loc=(0, 0, h * 0.225), segs=6, r2=r, bev=0.02 * r, parent=G)
    return G


def scuttler_sign(P, name, loc, size, col, rot=(90, 0, 0)):
    """Their sign: a painted hexagon ring with three claw slashes across it, flush on a surface (local -Y faces out)."""
    G = _grp(P, name, loc, rot)
    paint = K.mat(f"sign{col}", col, rough=0.8, wear=0.15, grime=0.3)
    for k in range(6):
        a = math.radians(30 + k * 60)
        L = size * 0.5
        K.box(f"{name}_side", (L, 0.012, size * 0.09), paint, loc=(math.cos(a) * size * 0.43, 0, math.sin(a) * size * 0.43),
              rot=(0, -math.degrees(a) - 90, 0), bev=0, parent=G)
    for k in (-1, 0, 1):
        K.box(f"{name}_slash", (size * 0.07, 0.014, size * 0.85 - abs(k) * size * 0.2), paint, loc=(k * size * 0.17, -0.002, 0), rot=(0, 28, 0), bev=0, parent=G)
    return G


def cut(ob, cutter):
    """Cuts cutter's volume out of ob (a boolean difference); the cutter itself is never drawn or exported."""
    cutter.hide_render = True
    cutter.hide_viewport = True
    mod = ob.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    mod.solver = "EXACT"
    return ob


def scrap_plate(P, name, size, loc, rot, shade, rnd):
    return K.box(name, size, sc_scrap(shade), loc=loc, rot=rot, bev=0.02, parent=P)


# ================================================================== Scuttlers
def scuttler_nest(seed=101):
    """The signature lair: a hexagonal terraced mound of welded scrap with a burrow mouth framed by a toothed hex hood,
    six giant crab legs rising from it and closing like a claw cage over a mast that carries a great green eye. 17 m
    across, 14.5 m tall."""
    rnd = random.Random(seed)
    P = _root("scuttler_nest")
    K.disc("spoil", 9.5, K.mat("spoil", "#4d4436", rough=1.0, noise_scale=1.2, cracks=0.2), (0, 0), z=0.01, segs=24, wobble=0.12, seed=seed).parent = P
    core = M(SCRAP, metal=0.4, wear=0.1, grime=0.6, scratches=0.3, rough=0.8, moss=0.08)
    tiers = [(7.4, 6.2, 0.0, 2.6, 30), (5.9, 4.6, 2.6, 1.9, 38), (4.4, 3.2, 4.5, 1.3, 45)]
    fy = -7.4 * math.cos(math.radians(30)) + 0.6
    # the burrow: a Scuttler-wide tunnel 4 m into the lowest terrace, under the hood; you can walk in to its dark end
    tunnel = K.box("tunnel_cut", (2.3, 4.4, 2.3), None, loc=(0, fy + 1.3, 1.1), bev=0, parent=P)
    for i, (r0, r1, z0, h, yaw) in enumerate(tiers):
        tier = K.cyl(f"tier{i}", r0, h, core, loc=(0, 0, z0 + h / 2), rot=(0, 0, yaw), segs=6, r2=r1, bev=0.08, parent=P)
        # the collider: the terraces as plain hex frusta, the tunnel cut through the lowest
        col = K.collider_only(K.cyl(f"col_tier{i}", r0, h, None, loc=(0, 0, z0 + h / 2), rot=(0, 0, yaw), segs=6, r2=r1, bev=0, parent=P))
        if i == 0:
            cut(tier, tunnel)
            cut(col, tunnel)
        # a framed ledge round each terrace top: angle-iron rails at the hex edges
        if i < 2:
            nr = tiers[i + 1][0]
            for k in range(6):
                a = math.radians(60 * k + yaw)
                ap = (r1 * 0.5 + nr * 0.5) * math.cos(math.radians(30))
                L = 2 * ap * math.tan(math.radians(30))
                K.box(f"ledge{i}{k}", (L, 0.22, 0.18), sc_gun(), loc=(math.cos(a) * r1 * 0.86, math.sin(a) * r1 * 0.86, z0 + h + 0.06),
                      rot=(0, 0, math.degrees(a) + 90), bev=0, parent=P)
        # scavenged plates welded over the faces, sloped like the terrace
        slope = math.degrees(math.atan2((r0 - r1) * math.cos(math.radians(30)), h))
        for k in range(6):
            fa = 60 * k + yaw
            if i == 0 and k == 4:
                continue  # the burrow face
            for j in range(3 if i == 0 else 2):
                t = (j + 0.5) / (3 if i == 0 else 2) - 0.5
                zz = z0 + h * rnd.uniform(0.35, 0.6)
                rr = (r0 + (r1 - r0) * (zz - z0) / h) * math.cos(math.radians(30)) + 0.03
                side = 2 * rr * math.tan(math.radians(30))
                a = math.radians(fa)
                px = math.cos(a) * rr - math.sin(a) * t * side * 0.9
                py = math.sin(a) * rr + math.cos(a) * t * side * 0.9
                sh = rnd.choice([0, 1, 2, 3, 3, 1])
                mat = sc_paint(SC_GREEN, wear=0.3) if rnd.random() < 0.18 else sc_scrap(sh)
                K.box(f"plate{i}{k}{j}", (side * rnd.uniform(0.3, 0.42), 0.07, h * rnd.uniform(0.55, 0.85)), mat, loc=(px, py, zz),
                      rot=(-slope + rnd.uniform(-4, 4), rnd.uniform(-9, 9), fa + 90), bev=0, parent=P)
    # ---- the burrow mouth on the front face: a hex hood of plate with steel teeth along its lintel
    R = 1.55
    hz = R * math.sin(math.radians(60))
    hood = _grp(P, "hood", (0, fy - 0.35, hz), (0, 0, 0))
    for k in range(6):
        a0, a1 = math.radians(k * 60), math.radians((k + 1) * 60)
        p0 = (R * math.cos(a0), 0, R * math.sin(a0))
        p1 = (R * math.cos(a1), 0, R * math.sin(a1))
        if k == 4:
            continue  # no sill: the floor runs straight in
        beam(hood, f"hood{k}", p0, p1, 0.42, 0.9, sc_gun(), bev=0.03)
    for k in range(6):
        a = math.radians(k * 60)
        K.box("hood_corner", (0.5, 1.0, 0.5), sc_steel(), loc=(R * math.cos(a), 0, R * math.sin(a)), rot=(0, -k * 60, 0), bev=0.04, parent=hood)
    for k in range(7):
        x = -0.72 + k * 0.24
        K.cyl("hood_tooth", 0.07, 0.32, sc_steel(), loc=(x, -0.3, hz - 0.33), rot=(180, 0, 0), segs=4, r2=0.0, bev=0, parent=hood)
    # the tunnel's dark end, its scrap-lined walls and a floor of trodden spoil running in from the mouth
    K.box("burrow_dark", (2.3, 0.1, 2.3), K.mat("burrow", DARK, rough=1.0), loc=(0, fy + 3.45, 1.15), bev=0, parent=P)
    for s in (-1, 1):
        K.box(f"burrow_wall{s}", (0.2, 4.0, 2.2), core, loc=(s * 1.25, fy + 1.4, 1.1), bev=0.02, parent=P)
        K.collider_box(P, f"mouth{s}", (0.5, 1.0, 2.6), (s * 1.45, fy - 0.35, 1.3))
    K.box("burrow_floor", (2.4, 5.6, 0.08), K.mat("spoil", "#4d4436", rough=1.0, noise_scale=1.2, cracks=0.2), loc=(0, fy + 0.8, 0.03), bev=0, parent=P)
    scuttler_sign(P, "sign_mouth", (0, fy - 0.82, hz * 2 + 0.55), 1.1, SC_GREEN, rot=(90 - 25, 0, 0))
    # ---- six giant legs from the second terrace, knees out, spikes closing over the eye
    for k in range(6):
        a = math.radians(30 + 60 * k + 38)
        hip = (math.cos(a) * 4.6, math.sin(a) * 4.6, 4.7)
        knee = (math.cos(a) * 8.2, math.sin(a) * 8.2, 10.2)
        tip = (math.cos(a + 0.18) * 2.2, math.sin(a + 0.18) * 2.2, 14.4)
        leg(P, f"leg{k}", hip, knee, tip, 0.5, sc_paint(SC_GREEN, wear=0.35, moss=0.05), spike=0.38)
        K.box(f"hipmount{k}", (1.2, 1.2, 0.6), sc_gun(), loc=(hip[0], hip[1], 4.45), rot=(0, 0, math.degrees(a)), bev=0.05, parent=P)
    # ---- the mast and its eye
    K.cyl("mast_base", 1.3, 0.8, sc_gun(), loc=(0, 0, 6.2), segs=6, r2=1.0, bev=0.06, parent=P)
    K.collider_box(P, "mast", (0.7, 0.7, 5.0), (0, 0, 8.5))
    rod(P, "mast", (0, 0, 6.5), (0, 0, 10.6), 0.28, sc_steel(), segs=10)
    for z in (7.3, 7.9, 8.5, 9.1, 9.7):
        K.torus("coil", 0.36, 0.06, sc_gun(), loc=(0, 0, z), segs=8, parent=P)
    eye_head(P, "great_eye", (0, 0, 11.6), 7.5, lit=True, paint=sc_paint(SC_GREEN))
    K.cyl("beacon", 0.18, 0.5, sc_gun(), loc=(0, 0.1, 12.9), segs=8, bev=0.02, parent=P)
    K.sphere("beacon_lamp", 0.16, glow(EYE, 4), loc=(0, 0.1, 13.25), parent=P)
    # ---- what lies about: shells, claws and spikes
    hex_shell(P, "spare_shell", 0.6, 0.5, sc_paint(SC_GREEN), (4.6, -6.6, 0.0), rot=(0, 9, 30))
    claw(P, "dropped_claw", (-4.2, -7.0, 0.12), 1.4, rot=(0, 90, 30), gape=40)
    for k in range(5):
        x, y = rnd.uniform(-8, 8), rnd.uniform(-8.5, -6.5)
        if abs(x) < 2.2:
            x += 3.2 * (1 if x >= 0 else -1)
        scrap_plate(P, f"loose{k}", (rnd.uniform(0.6, 1.3), rnd.uniform(0.4, 0.9), 0.06), (x, y, 0.08), (rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180)), k, rnd)
    return P


def scuttler_cradle(seed=102):
    """A charging cradle: a hex pad with three unfinished shells (one per paint) sitting in claw-prong sockets round a
    charger column of stacked salvaged battery packs, which reaches a crab leg down onto each shell with its charging
    lead. 4.6 m across, 2.9 m tall."""
    rnd = random.Random(seed)
    P = _root("scuttler_cradle")
    K.cyl("pad", 2.4, 0.25, sc_gun(), loc=(0, 0, 0.125), rot=(0, 0, 30), segs=6, r2=2.3, bev=0.03, parent=P)
    K.cyl("pad_top", 2.15, 0.05, sc_scrap(1), loc=(0, 0, 0.27), rot=(0, 0, 30), segs=6, bev=0.01, parent=P)
    # collision: the pad to step up on, the charger column and the three shells
    K.collider_only(K.cyl("col_pad", 2.4, 0.3, None, loc=(0, 0, 0.15), rot=(0, 0, 30), segs=6, bev=0, parent=P))
    K.collider_box(P, "column", (0.95, 0.8, 1.9), (0, 0.1, 1.25))
    for k in range(6):
        a = math.radians(60 * k + 30)
        K.box(f"pad_rail{k}", (2.3, 0.14, 0.1), sc_steel(), loc=(math.cos(a) * 2.08, math.sin(a) * 2.08, 0.31), rot=(0, 0, math.degrees(a) + 90), bev=0.015, parent=P)
    # the charger column
    for j, (w, h) in enumerate(((0.95, 0.45), (0.85, 0.42), (0.75, 0.4))):
        z = 0.3 + (0.0, 0.45, 0.87)[j]
        K.box(f"batt{j}", (w, w * 0.8, h), sc_scrap(j + 1), loc=(0, 0.1, z + h / 2), rot=(0, 0, rnd.uniform(-8, 8)), bev=0.025, parent=P)
        for s in (-1, 1):
            K.cyl(f"term{j}{s}", 0.045, 0.06, M(COPPER, metal=0.8, wear=0.1), loc=(s * w * 0.3, 0.1 - w * 0.25, z + h + 0.03), segs=8, bev=0, parent=P)
    for s in (-1, 1):
        K.box(f"strap{s}", (0.06, 0.85, 1.3), M("#3b3428", rough=0.9), loc=(s * 0.22, 0.1, 0.95), bev=0, parent=P)
    K.cyl("hub", 0.42, 0.35, sc_gun(), loc=(0, 0.1, 1.75), segs=6, bev=0.03, parent=P)
    rod(P, "stalk", (0, 0.1, 1.9), (0, 0.1, 2.35), 0.04, sc_steel(), segs=8)
    for j in range(3):
        K.torus(f"coil{j}", 0.07, 0.016, sc_gun(), loc=(0, 0.1, 2.0 + j * 0.1), segs=8, parent=P)
    eye_head(P, "lamp_head", (0, 0.1, 2.5), 1.3, lit=True, paint=sc_paint(SC_GREEN))
    scuttler_sign(P, "batt_sign", (0, 0.1 - 0.95 * 0.4 - 0.006, 0.52), 0.34, SC_GREEN)
    # three cradles round it, a leg reaching down to each
    for k, (ang, col) in enumerate(((-90, SC_GREEN), (30, SC_BLUE), (150, SC_YELLOW))):
        a = math.radians(ang)
        x, y = math.cos(a) * 1.4, math.sin(a) * 1.4 + 0.1
        for b in (0, 120, 240):
            rb = math.radians(b + ang)
            p0 = (x + math.cos(rb) * 0.18, y + math.sin(rb) * 0.18, 0.3)
            p1 = (x + math.cos(rb) * 0.52, y + math.sin(rb) * 0.52, 0.55)
            p2 = (x + math.cos(rb) * 0.42, y + math.sin(rb) * 0.42, 0.88)
            taper(P, f"prong{k}{b}", p0, p1, 0.045, 0.04, sc_gun(), segs=6)
            ball(P, f"pj{k}{b}", 0.055, sc_steel(), p1, u=6, v=4)
            taper(P, f"prong2{k}{b}", p1, p2, 0.04, 0.008, sc_tip(), segs=6)
        hex_shell(P, f"pod{k}", 0.44, 0.44, sc_paint(col, wear=0.2), (x, y, 0.42), rot=(rnd.uniform(-5, 5), rnd.uniform(-5, 5), rnd.uniform(0, 60)))
        K.collider_only(K.cyl(f"col_pod{k}", 0.55, 0.7, None, loc=(x, y, 0.65), segs=6, bev=0, parent=P))
        rod(P, f"stub{k}", (x, y, 0.85), (x, y, 1.0), 0.035, sc_steel(), segs=6)
        hip = (math.cos(a) * 0.38, math.sin(a) * 0.38 + 0.1, 1.75)
        knee = (math.cos(a) * 1.15, math.sin(a) * 1.15 + 0.1, 2.55)
        tip = (x, y, 1.02)
        leg(P, f"arm{k}", hip, knee, tip, 0.07, sc_paint(SC_GREEN, wear=0.15), spike=0.25)
        K.cyl(f"contact{k}", 0.07, 0.06, M(COPPER, metal=0.8, wear=0.1), loc=(x, y, 0.88), segs=8, bev=0, parent=P)
        cab = K.mat("cable", "#2b2d30", rough=0.6)
        pts = [lerp(hip, knee, 0.5), lerp(knee, tip, 0.15), lerp(knee, tip, 0.6), (x + 0.05, y, 0.95)]
        for j in range(3):
            rod(P, f"lead{k}{j}", (pts[j][0], pts[j][1], pts[j][2] - 0.1), (pts[j + 1][0], pts[j + 1][1], pts[j + 1][2] - 0.1), 0.02, cab, segs=5)
    return P


def scuttler_totem(seed=103):
    """An antenna totem: a pipe mast planted in a cairn of shells and rock, stacked with Scuttler eye heads on coil
    stalks (only the top one still lit), crowned with two open claws and a whip aerial, guyed to black leg spikes.
    7 m tall."""
    rnd = random.Random(seed)
    P = _root("scuttler_totem")
    rock = K.mat("rock", STONE, rough=0.9, grime=0.3, cracks=0.3, moss=0.15)
    for k in range(7):
        a = k * 360 / 7 + rnd.uniform(-15, 15)
        r = rnd.uniform(0.75, 0.95)
        lump(P, f"cairn{k}", rnd.uniform(0.4, 0.55), (math.cos(math.radians(a)) * r, math.sin(math.radians(a)) * r, 0.1), (1.0, 0.85, 0.75), rock, rnd,
             jitter=0.18, flat=0.3, u=6, v=4, rot=(0, 0, a))
    for k, col in enumerate((SC_GREEN, SCRAP, SC_BLUE)):
        a = math.radians(40 + k * 120)
        hex_shell(P, f"base_shell{k}", 0.48, 0.45, sc_paint(col) if col != SCRAP else sc_scrap(0), (math.cos(a) * 0.5, math.sin(a) * 0.5, 0.45),
                  rot=(rnd.uniform(-20, 20), rnd.uniform(20, 35), math.degrees(a)))
    K.cyl("socket", 0.36, 0.9, sc_gun(), loc=(0, 0, 0.5), segs=6, r2=0.28, bev=0.03, parent=P)
    rod(P, "mast", (0, 0, 0.8), (0, 0, 6.0), 0.12, sc_steel(), segs=10)
    heads = [(1.75, 0, False, SC_SEED, 2.6), (2.9, 60, False, SC_YELLOW, 2.5), (4.0, -35, False, SC_RED, 2.4), (5.1, 0, True, SC_GREEN, 2.4)]
    for k, (z, yaw, lit, col, s) in enumerate(heads):
        H = eye_head(P, f"head{k}", (0, 0, z), s, lit=lit, yaw=yaw, paint=sc_paint(col, wear=0.45, grime=0.6))
        H.rotation_euler.x = math.radians(rnd.uniform(-7, 7))
        H.rotation_euler.y = math.radians(rnd.uniform(-6, 6))
        K.box(f"collar{k}", (0.3, 0.3, 0.12), sc_gun(), loc=(0, 0, z - 0.14 * s - 0.06), rot=(0, 0, yaw), bev=0.015, parent=P)
        for j in range(2):
            K.torus(f"coil{k}{j}", 0.17, 0.03, sc_gun(), loc=(0, 0, z + 0.22 * s + j * 0.14), segs=8, parent=P)
    # the crown: two open claws like antlers
    for s in (-1, 1):
        claw(P, f"crown{s}", (s * 0.2, 0, 5.75), 2.8, rot=(-90 + 10, s * -32, 0), gape=50, paint=sc_scrap(1))
    claw(P, "hung_claw", (0.42, -0.05, 3.3), 1.6, rot=(180, 0, 90), gape=20, paint=sc_paint(SC_GREEN, wear=0.4))
    rod(P, "hung_wire", (0.2, 0, 3.55), (0.42, -0.05, 3.42), 0.01, K.mat("cable", "#2b2d30", rough=0.6), segs=4)
    leg(P, "lashed_leg", (-0.2, 0.05, 1.1), (-0.24, 0.05, 1.9), (-0.2, 0.05, 2.55), 0.05, sc_paint(SC_BLUE, wear=0.4), spike=0.35)
    rod(P, "whip", (0, 0, 6.0), (0.12, 0.05, 7.2), 0.02, sc_steel(), segs=5)
    K.sphere("whip_tip", 0.05, glow(EYE, 4), loc=(0.12, 0.05, 7.2), parent=P)
    # guys to three planted leg spikes
    for k in range(3):
        a = math.radians(-90 + 30 + k * 120)
        g = (math.cos(a) * 2.6, math.sin(a) * 2.6)
        top = (g[0] * 1.06, g[1] * 1.06, 0.75)
        taper(P, f"stake_sleeve{k}", top, lerp(top, (g[0], g[1], -0.1), 0.35), 0.075, 0.065, sc_paint(SC_GREEN), segs=8)
        taper(P, f"stake{k}", lerp(top, (g[0], g[1], -0.1), 0.35), (g[0], g[1], -0.1), 0.065, 0.01, sc_tip(), segs=6)
        ball(P, f"stake_ball{k}", 0.1, sc_steel(), top, u=6, v=4)
        rod(P, f"guy{k}", top, (0, 0, 4.6), 0.014, K.mat("cable", "#2b2d30", rough=0.6), segs=4)
    scuttler_sign(P, "sign", (0, -0.335, 0.55), 0.42, SC_GREEN, rot=(90 + 5, 0, 0))
    return P


def scuttler_bore(seed=104):
    """Blue Scuttlers' drill work: a rock outcrop with a sheer cut face bored by a slanting row of drill holes and one
    tunnel a Scuttler fits in, tailings heaped under them, a snapped drill bit still in a hole, a cracked blue shell
    plate and a wet patch. 5 m long, 2.6 m tall."""
    rnd = random.Random(seed)
    P = _root("scuttler_bore")
    rock = K.mat("rockb", STONE, rough=0.9, grime=0.35, cracks=0.45, moss=0.25)
    rock_d = K.mat("rockbd", STONE_D, rough=0.9, grime=0.35, cracks=0.45, moss=0.2)
    # the outcrop: angular blocks behind and beside a flat face
    lump(P, "back", 1.0, (0.1, 0.9, 0.0), (2.4, 1.2, 2.5), rock_d, rnd, jitter=0.16, flat=0.05, u=7, v=5)
    lump(P, "side", 0.9, (2.0, 0.6, 0.0), (1.1, 1.1, 1.4), rock, rnd, jitter=0.18, flat=0.05, u=6, v=4)
    lump(P, "side2", 0.7, (-2.1, 0.5, 0.0), (1.0, 1.0, 1.1), rock_d, rnd, jitter=0.18, flat=0.05, u=6, v=4)
    face = _grp(P, "face", (0, 0.25, 1.15), (4, 0, -3))
    outline = [(-1.95, -1.17), (1.9, -1.17), (2.05, -0.3), (1.7, 0.75), (1.0, 1.25), (0.1, 1.45), (-0.9, 1.3), (-1.7, 0.9), (-2.1, 0.0)]
    prism(face, "face_slab", outline, 1.0, rock, plane="xz", bev=0.08)
    K.box("face_ledge", (1.6, 0.7, 0.45), rock_d, loc=(-0.5, 0.25, 1.35), rot=(8, 12, 6), bev=0.08, parent=face)
    fresh = K.mat("rock_cut", "#a7a99f", rough=0.85, cracks=0.1)
    bore = K.mat("bore", DARK, rough=1.0)
    holes = [(-1.35, 1.95, 0.16), (-0.85, 1.75, 0.16), (-0.35, 1.55, 0.16), (0.15, 1.35, 0.16), (0.65, 1.15, 0.16), (1.2, 2.0, 0.16)]
    for k, (x, z, r) in enumerate(holes):
        G = _grp(face, f"hole{k}", (x, -0.5, z - 1.15))
        K.cyl(f"rim{k}", r * 1.2, 0.04, fresh, loc=(0, 0.0, 0), rot=(90, 0, 0), segs=12, bev=0.005, parent=G)
        K.cyl(f"hole{k}", r, 0.1, bore, loc=(0, -0.012, 0), rot=(90, 0, 0), segs=12, bev=0, parent=G)
    # the tunnel at the foot of the face: a Scuttler-sized bore with scored rings round its mouth
    T = _grp(face, "tunnel", (-0.6, -0.5, 0.55 - 1.15))
    K.cyl("tunnel_rim", 0.58, 0.06, fresh, rot=(90, 0, 0), segs=16, bev=0.02, parent=T)
    K.cyl("tunnel", 0.5, 0.12, bore, loc=(0, -0.02, 0), rot=(90, 0, 0), segs=16, bev=0, parent=T)
    for j in range(7):
        lump(P, f"tail{j}", rnd.uniform(0.12, 0.2), (-0.6 + rnd.uniform(-0.8, 0.9), -0.65 - rnd.uniform(0, 0.6), 0.03), (1.4, 1.1, 0.5), fresh, rnd, flat=0.3, u=6, v=4)
    # the snapped bit in hole 3: a fluted steel cone behind a hex collar
    x, z, r = holes[3]
    B = _grp(face, "bit", (x, -0.5, z - 1.15))
    K.cyl("bit_collar", 0.15, 0.14, sc_gun(), loc=(0, -0.2, 0), rot=(90, 0, 0), segs=6, bev=0.015, parent=B)
    K.cyl("bit", 0.12, 0.3, sc_steel(), loc=(0, -0.08, 0), rot=(-90, 0, 0), segs=8, r2=0.05, bev=0, parent=B)
    rod(B, "bit_shank", (0, -0.27, 0), (0.05, -0.55, -0.05), 0.04, sc_steel(), segs=8)
    # a cracked blue shell plate, a pontoon float and a wet patch
    K.box("blue_plate", (0.9, 0.6, 0.05), sc_paint(SC_BLUE), loc=(1.3, -1.25, 0.1), rot=(6, -9, 25), bev=0.02, parent=P)
    scuttler_sign(P, "sign_plate", (1.3, -1.25, 0.13), 0.42, "#d8d6cc", rot=(6, -9, 25))
    K.cyl("pontoon", 0.16, 0.9, sc_paint(SC_BLUE), loc=(-2.0, -1.0, 0.16), rot=(90, 0, 70), segs=10, bev=0.06, parent=P)
    K.disc("wet", 1.0, K.mat("wet", WATER, rough=0.08, metal=0.1), (0.5, -1.0), z=0.012, segs=20, wobble=0.25, seed=seed).parent = P
    scuttler_sign(face, "sign_rock", (1.25, -0.512, 0.7 - 1.15), 0.6, SC_BLUE, rot=(90, 0, 0))
    return P


def scuttler_stomp_ring(seed=105):
    """A Yellow Scuttler's stomp: a 3 m crater, the turf heaved up round it in broken clods, cracks running out into
    the grass, scrap crushed flat in the middle and a hammer face lying where it sheared off."""
    rnd = random.Random(seed)
    P = _root("scuttler_stomp_ring")
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0, cracks=0.4)
    soil_d = K.mat("soil_d", "#46311f", rough=1.0, noise_scale=3.0, cracks=0.5)
    turf = K.mat("turf", "#4f7a34", rough=1.0, noise_scale=4.0)
    K.disc("crater", 1.25, soil_d, (0, 0), z=0.012, segs=20, wobble=0.1, seed=seed).parent = P
    turned(P, "lip", [(1.0, 0.0), (1.2, 0.1), (1.42, 0.16), (1.62, 0.08), (1.85, 0.0)], soil, segs=20)
    for k in range(14):
        a = math.radians(k * 360 / 14 + rnd.uniform(-10, 10))
        r = rnd.uniform(1.25, 1.6)
        sz = rnd.uniform(0.18, 0.32)
        tilt = rnd.uniform(20, 55)
        G = _grp(P, f"clod{k}", (math.cos(a) * r, math.sin(a) * r, 0.1), (0, -tilt, math.degrees(a)))
        lump(G, f"clod{k}", sz, (0, 0, 0), (1.0, rnd.uniform(1.0, 1.8), 0.45), soil, rnd, jitter=0.2, u=6, v=4)
        if k % 2 == 0:
            K.box(f"sod{k}", (sz * 1.6, sz * 2.0, 0.04), turf, loc=(0, 0, sz * 0.42), rot=(0, 0, rnd.uniform(-20, 20)), bev=0.01, parent=G)
    crack = K.mat("crack", DARK, rough=1.0)
    for k in range(5):
        a = math.radians(k * 72 + 30)
        K.box(f"icrack{k}", (0.7, 0.03, 0.02), crack, loc=(math.cos(a) * 0.55, math.sin(a) * 0.55, 0.025), rot=(0, 0, math.degrees(a) + rnd.uniform(-10, 10)), bev=0, parent=P)
    # crushed scrap: a flattened drum and a plate
    K.cyl("drum_flat", 0.34, 0.1, sc_scrap(3), loc=(-0.35, 0.2, 0.06), rot=(3, -2, 0), segs=12, bev=0.03, parent=P)
    K.box("plate_flat", (0.7, 0.45, 0.03), sc_scrap(2), loc=(0.25, -0.35, 0.035), rot=(2, 1, 35), bev=0.01, parent=P)
    # the hammer: a yellow hex head on its side, piston rod sheared
    G = _grp(P, "hammer", (0.3, 0.4, 0.3), (0, 80, 20))
    K.cyl("ham_face", 0.34, 0.16, sc_steel(), loc=(0, 0, -0.2), segs=6, bev=0.02, parent=G)
    K.cyl("ham_body", 0.3, 0.3, sc_paint(SC_YELLOW, wear=0.35), loc=(0, 0, 0.03), segs=6, r2=0.24, bev=0.02, parent=G)
    rod(G, "ham_rod", (0, 0, 0.15), (0.0, 0.0, 0.55), 0.07, sc_steel(), segs=8)
    for k in range(6):
        a = math.radians(k * 60 + 30)
        K.cyl("ham_bolt", 0.025, 0.03, M(ZINC, metal=0.6), loc=(math.cos(a) * 0.2, math.sin(a) * 0.2, 0.19), segs=6, bev=0, parent=G)
    scuttler_sign(P, "sign", (0.55, -0.35, 0.055), 0.36, SC_YELLOW, rot=(0, 0, 35))
    return P


def scuttler_blast_pit(seed=106):
    """Where a Red Scuttler went off: a scorched pit inside a lip of thrown earth, its burst pressure tank, torn red
    shell plates, a severed leg and its gauge. 3.4 m across."""
    rnd = random.Random(seed)
    P = _root("scuttler_blast_pit")
    char = K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    ash = K.mat("ash", "#57524a", rough=1.0, noise_scale=2.0)
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0, cracks=0.3)
    K.disc("scorch", 1.85, ash, (0, 0), z=0.01, segs=20, wobble=0.22, seed=seed).parent = P
    K.disc("pit", 1.0, char, (0, 0), z=0.018, segs=18, wobble=0.1, seed=seed + 1).parent = P
    turned(P, "lip", [(0.95, 0.0), (1.1, 0.14), (1.3, 0.2), (1.5, 0.1), (1.7, 0.0)], soil, segs=20)
    for k in range(10):
        a = math.radians(rnd.uniform(0, 360))
        r = rnd.uniform(1.4, 2.1)
        sz = rnd.uniform(0.07, 0.16)
        lump(P, f"clod{k}", sz, (math.cos(a) * r, math.sin(a) * r, 0.02), (1.4, 1.0, 0.6), soil, rnd, jitter=0.25, flat=0.3, u=5, v=4, rot=(0, 0, math.degrees(a)))
    # the burst tank: a short cylinder lying on its side, its end blown open into petals
    G = _grp(P, "tank", (0.15, 0.1, 0.25), (0, 82, 25))
    red = sc_paint(SC_RED, wear=0.4, grime=0.65)
    K.cyl("tank_body", 0.24, 0.6, red, loc=(0, 0, 0), segs=12, bev=0.02, parent=G)
    K.cyl("tank_cap", 0.245, 0.08, sc_gun(), loc=(0, 0, -0.32), segs=12, bev=0.015, parent=G)
    K.cyl("tank_dark", 0.2, 0.02, K.mat("pit", DARK, rough=1.0), loc=(0, 0, 0.29), segs=12, bev=0, parent=G)
    for k in range(6):
        a = k * 60
        P2 = _grp(G, f"petal{k}", (math.cos(math.radians(a)) * 0.22, math.sin(math.radians(a)) * 0.22, 0.3), (0, 0, a))
        K.box(f"petal{k}", (0.025, 0.16, 0.2), red, loc=(0.07, 0, 0.06), rot=(0, 55 + rnd.uniform(-10, 10), 0), bev=0.005, parent=P2)
    K.cyl("gauge", 0.08, 0.04, sc_gun(), loc=(-0.6, -0.55, 0.04), segs=12, bev=0.01, parent=P)
    K.cyl("gauge_face", 0.065, 0.045, M("#d8d6cc", rough=0.5), loc=(-0.6, -0.55, 0.045), segs=12, bev=0, parent=P)
    K.box("needle", (0.06, 0.008, 0.01), M("#c0201a"), loc=(-0.6, -0.55, 0.07), rot=(0, 0, 70), bev=0, parent=P)
    for k in range(6):
        a = math.radians(rnd.uniform(0, 360))
        r = rnd.uniform(0.5, 1.7)
        K.box(f"shard{k}", (rnd.uniform(0.25, 0.45), rnd.uniform(0.18, 0.3), 0.025), red if k % 2 == 0 else sc_gun(), loc=(math.cos(a) * r, math.sin(a) * r, 0.08),
              rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), rnd.uniform(0, 180)), bev=0.006, parent=P)
    leg(P, "leg", (0.85, -0.95, 0.07), (1.2, -0.6, 0.2), (1.65, -0.2, 0.03), 0.055, red, spike=0.4)
    for k in range(2):
        a = math.radians(140 + k * 60)
        taper(P, f"spike{k}", (math.cos(a) * 1.0, math.sin(a) * 1.0, 0.05), (math.cos(a) * 1.3, math.sin(a) * 1.3, 0.12), 0.045, 0.005, sc_tip(), segs=6)
    scuttler_sign(P, "sign", (-0.45, 0.45, 0.03), 0.6, SC_RED, rot=(0, 0, 15))
    return P


def scuttler_seed_cache(seed=107):
    """A Seed Scuttler's hoard: a dark green shell turned over as a lid on a scratched-out hole, propped up on a leg,
    the stolen seed sacks split in the dirt beside it and a shell plate staked up with their sign. 2.4 m across."""
    rnd = random.Random(seed)
    P = _root("scuttler_seed_cache")
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    K.disc("dug", 1.2, soil, (0, 0), z=0.01, segs=18, wobble=0.18, seed=seed).parent = P
    lump(P, "spoil", 0.5, (-0.45, 0.75, 0.0), (1.4, 0.9, 0.55), soil, rnd, flat=0.05, u=7, v=5)
    K.cyl("hole", 0.42, 0.04, K.mat("pit", DARK, rough=1.0), loc=(-0.15, 0.05, 0.02), segs=6, bev=0, parent=P)
    # the shell lid, upside down, tipped up at the front and propped
    # the seed paint, a shade lighter than the bot's so the lid still reads as green in shade
    green = sc_paint(SC_SEED_LID, wear=0.6, scratches=0.7, grime=0.12, rough=0.45)
    L = _grp(P, "lid", (-0.15, 0.25, 0.42), (180 - 30, 0, 10))
    hex_shell(L, "lid_shell", 0.6, 0.45, green, (0, 0, -0.22))
    leg(P, "prop", (-0.2, -0.3, 0.02), (-0.12, -0.38, 0.28), (-0.15, -0.2, 0.62), 0.035, green, spike=0.2)
    # claw scratches in the dirt
    for k in range(3):
        K.box(f"scratch{k}", (0.6, 0.035, 0.02), K.mat("crack", DARK, rough=1.0), loc=(0.15 + k * 0.1, -0.75 - k * 0.04, 0.02), rot=(0, 0, 25), bev=0, parent=P)
    # split sacks and spilled seed
    sack = K.mat("burlap", BURLAP, rough=0.95, grime=0.4, noise_scale=14.0)
    seedm = K.mat("seeds", SEEDS, rough=0.9, noise_scale=30.0)
    for k, (x, y, yaw) in enumerate(((0.6, -0.1, 30), (0.75, 0.35, -50))):
        lump(P, f"sack{k}", 0.27, (x, y, 0.0), (1.3, 0.9, 0.8), sack, rnd, flat=0.35, u=8, v=6, rot=(0, 0, yaw))
        K.torus(f"tie{k}", 0.06, 0.015, M("#6b5434", rough=0.9), loc=(x - 0.3 * math.cos(math.radians(yaw)), y - 0.3 * math.sin(math.radians(yaw)), 0.12),
                rot=(0, 90, yaw), segs=8, parent=P)
    for k in range(4):
        lump(P, f"spill{k}", 0.12, (0.35 + rnd.uniform(-0.25, 0.2), -0.45 + rnd.uniform(-0.15, 0.15), 0.0), (1.6, 1.2, 0.35), seedm, rnd, flat=0.2, u=6, v=4)
    claw(P, "claw", (-0.9, -0.55, 0.08), 0.8, rot=(0, 90, -40), paint=green, gape=35)
    # a shell plate staked in the spoil, their sign on it
    K.box("stake_plate", (0.42, 0.04, 0.38), green, loc=(0.55, 0.85, 0.42), rot=(8, 0, -15), bev=0.01, parent=P)
    taper(P, "stake", (0.55, 0.88, 0.5), (0.55, 0.92, -0.1), 0.03, 0.006, sc_tip(), segs=6)
    scuttler_sign(P, "sign", (0.545, 0.828, 0.42), 0.3, "#c9b25e", rot=(90 + 8, 0, -15))
    return P


# ---------------------------------------------------------------- Rakehand kit
def rk_paint(col, **kw):
    kw.setdefault("wear", 0.3)
    kw.setdefault("grime", 0.45)
    kw.setdefault("scratches", 0.4)
    kw.setdefault("metal", 0.25)
    return M(col, **kw)


def rk_steel():
    return M(RK_TINE, metal=0.45, wear=0.1, grime=0.25, rough=0.4)


def rk_iron():
    return M(GRAPHITE, metal=0.55, wear=0.2, grime=0.45)


def rk_rope():
    return K.mat("rk_rope", RK_ROPE, rough=0.95, noise_scale=20.0)


def rk_cloth(col):
    return K.mat(f"rag{col}", col, rough=0.95, grime=0.45, noise_scale=10.0)


def fork_head(P, name, loc, n, width, length, s=1.0, rot=(0, 0, 0)):
    """A Rakehand's fork: a steel bridge with n tapered tines along local +Z (their hands, at any scale)."""
    G = _grp(P, name, loc, rot)
    K.box(f"{name}_bridge", (width, 0.05 * s, 0.07 * s), rk_iron(), loc=(0, 0, 0), bev=0.01 * s if s > 3 else 0, parent=G)
    K.cyl(f"{name}_socket", 0.05 * s, 0.16 * s, rk_iron(), loc=(0, 0, -0.1 * s), segs=8, r2=0.04 * s, bev=0, parent=G)
    for k in range(n):
        x = -width / 2 + width * (k + 0.5) / n
        K.cyl(f"{name}_tine{k}", 0.016 * s, length, rk_steel(), loc=(x, 0, length / 2 + 0.03 * s), segs=6, r2=0.003 * s, bev=0, parent=G)
    return G


def bale(P, name, size, loc, rot=(0, 0, 0), twine=None):
    """A square bale with two twine bands round it."""
    sx, sy, sz = size
    G = _grp(P, name, loc, rot)
    K.box(f"{name}_hay", size, _straw(), loc=(0, 0, 0), bev=min(size) * 0.1, parent=G)
    for t in (-0.27, 0.27):
        K.box(f"{name}_twine", (0.025, sy * 1.01, sz * 1.01), twine or rk_rope(), loc=(t * sx, 0, 0), bev=0, parent=G)
    return G


def rakehand_sign(P, name, loc, size, col, rot=(90, 0, 0)):
    """Their sign: a three-tine fork painted on a surface (local -Y faces out), tines up."""
    G = _grp(P, name, loc, rot)
    paint = K.mat(f"rsign{col}", col, rough=0.8, wear=0.15, grime=0.3)
    w = size * 0.1
    K.box(f"{name}_shaft", (w, 0.012, size * 0.5), paint, loc=(0, 0, -size * 0.25), bev=0, parent=G)
    K.box(f"{name}_bar", (size * 0.62, 0.012, w), paint, loc=(0, 0, 0), bev=0, parent=G)
    for x in (-0.31, 0.0, 0.31):
        K.box(f"{name}_tine", (w * 0.8, 0.012, size * 0.42), paint, loc=(x * size, 0, size * 0.21), bev=0, parent=G)
        K.cyl(f"{name}_pt", w * 0.4, size * 0.1, paint, loc=(x * size, 0, size * 0.47), segs=4, r2=0.0, bev=0, parent=G)
    return G


def hay_tufts(P, name, loc, n, rnd, r=0.4, h=0.25):
    """Loose wisps of hay stuck in a seam or lying in the grass."""
    for k in range(n):
        x = loc[0] + rnd.uniform(-r, r)
        y = loc[1] + rnd.uniform(-r, r)
        K.cyl(f"{name}{k}", 0.05, h * rnd.uniform(0.6, 1.2), _straw(k % 2 == 1), loc=(x, y, loc[2] + h * 0.3),
              rot=(rnd.uniform(-60, 60), rnd.uniform(-60, 60), 0), segs=4, r2=0.005, bev=0, parent=P)


def rag(P, name, top_a, top_b, drop, col, rnd, thick=0.015):
    """A ragged strip of burlap hanging from a bar between top_a and top_b, its lower edge cut."""
    a, b = Vector(top_a), Vector(top_b)
    n = 4
    pts = [tuple(a), tuple(b)]
    for k in range(n + 1):
        t = 1 - k / n
        p = a.lerp(b, t)
        pts.append((p.x + rnd.uniform(-0.03, 0.03), p.y + rnd.uniform(-0.05, 0.05), p.z - drop * rnd.uniform(0.7, 1.15)))
    return sheet(P, name, pts, rk_cloth(col), thick)


def lantern_cage(P, name, loc, s, lit=True, frame=None):
    """A Scarecrow Rakehand's caged lantern head: gold-trimmed cage, roof cone, green lamp inside."""
    G = _grp(P, name, loc)
    frame = frame or rk_paint(RK_GOLD, metal=0.5)
    K.cyl(f"{name}_base", 0.16 * s, 0.06 * s, rk_iron(), loc=(0, 0, 0.03 * s), segs=8, bev=0.01 * s, parent=G)
    for k in range(4):
        a = math.radians(45 + k * 90)
        K.box(f"{name}_bar{k}", (0.025 * s, 0.025 * s, 0.32 * s), frame, loc=(math.cos(a) * 0.13 * s, math.sin(a) * 0.13 * s, 0.22 * s), bev=0, parent=G)
    K.cyl(f"{name}_roof", 0.2 * s, 0.14 * s, frame, loc=(0, 0, 0.45 * s), segs=8, r2=0.04 * s, bev=0.01 * s, parent=G)
    K.torus(f"{name}_ring", 0.05 * s, 0.012 * s, rk_iron(), loc=(0, 0, 0.56 * s), rot=(90, 0, 0), segs=10, parent=G)
    K.cyl(f"{name}_glass", 0.1 * s, 0.26 * s, glow(EYE, 3) if lit else K.mat("rk_deadglass", "#3a4a3c", rough=0.15), loc=(0, 0, 0.21 * s), segs=10, bev=0.01 * s, parent=G)
    return G


# ================================================================== Rakehands
def rakehand_shrine(seed=201):
    """The signature lair: a harvest effigy. A 13.5 m scarecrow of field timber, bales and rags on a plinth of bales:
    splayed timber legs, a torso of strapped bales with a red baler drum for a chest, a yoke whose ends are giant
    pitchforks hung with rags in all three Rakehand colours, and a great caged lantern for a head, ringed by planted
    forks. 17 m across the trodden hay."""
    rnd = random.Random(seed)
    P = _root("rakehand_shrine")
    K.disc("trodden", 8.0, _straw_bed(), (0, 0), z=0.012, segs=24, wobble=0.1, seed=seed).parent = P
    timber = wood(3, wear=0.2)
    iron = rk_iron()
    # ---- the plinth: a ring of big bales round a packed deck, one bale set forward as a step
    B = (2.4, 1.2, 0.9)
    K.box("deck", (6.2, 6.2, 0.86), _straw(True), loc=(0, 0, 0.43), bev=0.06, parent=P)
    for side in range(4):
        yaw = side * 90
        a = math.radians(yaw)
        for j in range(3):
            t = (j - 1) * 2.45
            x = math.cos(a) * t + math.sin(a) * 3.1
            y = math.sin(a) * t - math.cos(a) * 3.1
            bale(P, f"pl{side}{j}", B, (x, y, 0.45), (0, 0, yaw + rnd.uniform(-3, 3)))
    bale(P, "step", (2.4, 1.2, 0.45), (0.2, -4.3, 0.225), (0, 0, 4))
    # collision: the plinth you climb by its step, the effigy's legs, spine, torso, drums and yoke
    K.collider_box(P, "plinth", (7.4, 7.4, 0.9), (0, 0, 0.45))
    K.collider_box(P, "step", (2.4, 1.2, 0.45), (0.2, -4.3, 0.225), (0, 0, 4))
    for s in (-1, 1):
        K.collider_only(beam(P, f"col_leg{s}", (s * 1.9, -0.1, 0.9), (s * 0.75, 0, 5.6), 0.6, 0.6, None, bev=0))
    K.collider_box(P, "spine", (0.6, 0.6, 3.0), (0, 0.15, 10.2))
    K.collider_box(P, "torso", (3.4, 3.8, 2.7), (0, 0.3, 6.9))
    K.collider_box(P, "yoke", (10.4, 0.5, 0.55), (0, 0.15, 9.0))
    # ---- legs, spine and a hay skirt
    for s in (-1, 1):
        beam(P, f"leg{s}", (s * 1.9, -0.1, 0.9), (s * 0.75, 0, 5.6), 0.55, 0.55, wood(2, wear=0.2), bev=0.03)
        K.box(f"leg_shoe{s}", (0.7, 0.7, 0.2), iron, loc=(s * 1.9, -0.1, 0.95), bev=0.02, parent=P)
        for z in (2.0, 3.6):
            t = (z - 0.9) / 4.7
            K.box(f"leg_band{s}{z}", (0.52, 0.52, 0.16), iron, loc=(s * (1.9 - 1.15 * t), -0.1 + 0.1 * t, z), rot=(0, s * 14, 0), bev=0.015, parent=P)
    K.box("spine", (0.55, 0.55, 8.8), timber, loc=(0, 0.15, 0.86 + 4.4), bev=0.04, parent=P)
    for k in range(7):
        x0 = -1.5 + k * 0.45
        rag(P, f"waist_rag{k}", (x0, -0.42, 5.55), (x0 + 0.38, -0.42, 5.55), rnd.uniform(0.9, 1.5), [RK_BURLAP, RK_RAG, RK_PURPLE][k % 3], rnd, thick=0.03)
    # ---- the torso: six strapped bales, a red baler drum for a chest
    for r in range(3):
        for s in (-1, 1):
            bale(P, f"torso{r}{s}", (1.6, 1.15, 0.85), (s * 0.81, 0.2, 5.95 + r * 0.86), (0, 0, rnd.uniform(-3, 3)))
    for z in (5.55, 8.15):
        K.box(f"strap{z}", (3.35, 1.3, 0.12), iron, loc=(0, 0.2, z), bev=0.01, parent=P)
    K.cyl("drum", 0.95, 2.0, rk_paint(RK_RED), loc=(0, -0.75, 6.9), rot=(0, 90, 0), segs=16, bev=0.05, parent=P)
    for x in (-0.7, 0.7):
        K.torus(f"drum_band{x}", 0.96, 0.05, rk_paint(RK_CREAM), loc=(x, -0.75, 6.9), rot=(0, 90, 0), segs=10, parent=P)
    rakehand_sign(P, "chest_sign", (0, -1.705, 6.9), 1.2, RK_CREAM)
    # ---- a bale drum on its back, like a Baler's
    K.cyl("back_drum", 1.05, 2.2, _straw(), loc=(0, 1.35, 7.1), rot=(0, 90, 0), segs=14, bev=0, parent=P)
    K.cyl("back_drum_face", 0.92, 2.22, _straw(True), loc=(0, 1.35, 7.1), rot=(0, 90, 0), segs=14, bev=0, parent=P)
    for x in (-0.6, 0.6):
        K.cyl(f"back_wrap{x}", 1.07, 0.08, rk_paint(RK_RED, metal=0.4), loc=(x, 1.35, 7.1), rot=(0, 90, 0), segs=14, bev=0, parent=P)
    # ---- the yoke and its fork hands
    K.box("yoke", (10.4, 0.5, 0.55), timber, loc=(0, 0.15, 9.0), bev=0.04, parent=P)
    for s in (-1, 1):
        beam(P, f"brace{s}", (s * 0.3, 0.15, 7.6), (s * 2.6, 0.15, 8.75), 0.26, 0.26, wood(1, wear=0.2), bev=0.02)
        K.box(f"yoke_end{s}", (0.7, 0.7, 0.8), rk_paint(RK_GREEN), loc=(s * 5.2, 0.15, 9.0), bev=0.04, parent=P)
        K.box(f"yoke_trim{s}", (0.72, 0.72, 0.12), rk_paint(RK_MUSTARD), loc=(s * 5.2, 0.15, 9.3), bev=0.01, parent=P)
        fork_head(P, f"hand{s}", (s * 5.45, 0.15, 8.45), 4, 2.0, 3.0, s=8.0, rot=(0, 180 - s * 22, 0))
        for j in range(4):
            x0 = s * (0.9 + j * 1.05)
            rag(P, f"rag{s}{j}", (x0, -0.12, 8.8), (x0 + s * 0.75, -0.12, 8.8), rnd.uniform(1.8, 3.0),
                [RK_PURPLE, RK_RAG, RK_GREEN, RK_RED, RK_BURLAP][(j + (s > 0) * 2) % 5], rnd, thick=0.03)
    hay_tufts(P, "tuft_yoke", (0, 0.15, 9.25), 8, rnd, r=2.5, h=0.7)
    # ---- the head: a great caged lantern on an iron neck
    K.box("neck", (0.8, 0.8, 0.5), iron, loc=(0, 0.15, 9.5), bev=0.03, parent=P)
    lantern_cage(P, "head", (0, 0.15, 9.7), 6.8, lit=True)
    # ---- the ring of planted forks, leaning out
    for k in range(8):
        a = math.radians(k * 45 + 22.5)
        r = 5.3
        x, y = math.cos(a) * r, math.sin(a) * r
        G = _grp(P, f"ring{k}", (x, y, -0.2), (0, 14, math.degrees(a)))
        K.cyl(f"ring_handle{k}", 0.05, 2.5, wood(2, wear=0.1), loc=(0, 0, 1.25), segs=6, bev=0, parent=G)
        fork_head(G, f"ring_fork{k}", (0, 0, 2.6), 3, 0.55, 0.8, s=2.4)
    return P


def rakehand_palisade(seed=202):
    """A pitchfork palisade: a 6 m line of sharpened field posts with two rails and painted boards, a course of bales
    packed behind, and a row of raked poles set through it, each with a fork head lashed on, tines out at the enemy.
    2.7 m tall."""
    rnd = random.Random(seed)
    P = _root("rakehand_palisade")
    W = 6.0
    for k in range(4):
        x = -W / 2 + k * W / 3
        h = rnd.uniform(2.4, 2.7)
        K.box(f"post{k}", (0.22, 0.22, h + 0.3), wood(3, wear=0.2), loc=(x, 0, h / 2 - 0.15), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), 0), bev=0.02, parent=P)
        K.cyl(f"point{k}", 0.15, 0.36, wood(3, wear=0.2), loc=(x, 0, h + 0.17), segs=4, r2=0.0, bev=0, rot=(0, 0, 45), parent=P)
    for z, shade in ((0.95, 1), (1.9, 2)):
        plank(P, f"rail{z}", (W + 0.4, 0.07, 0.18), (0, -0.14, z), rot=(0, rnd.uniform(-1, 1), 0), shade=shade)
    for k, (x, col) in enumerate(((-2.0, RK_GREEN), (0.0, RK_PURPLE), (2.0, RK_RED))):
        K.box(f"board{k}", (0.9, 0.04, 0.7), rk_paint(col), loc=(x, -0.2, 1.48), rot=(0, rnd.uniform(-4, 4), 0), bev=0.01, parent=P)
        rakehand_sign(P, f"sign{k}", (x, -0.225, 1.48), 0.5, {RK_GREEN: RK_MUSTARD, RK_PURPLE: RK_GOLD, RK_RED: RK_CREAM}[col])
    for k in range(5):
        bale(P, f"bale{k}", (1.15, 0.6, 0.55), (-2.4 + k * 1.2, 0.45, 0.28), (0, 0, rnd.uniform(-5, 5)))
    for k in range(3):
        bale(P, f"bale2{k}", (1.15, 0.6, 0.55), (-1.8 + k * 1.5, 0.48, 0.83), (0, 0, rnd.uniform(-8, 8)))
    # raked poles through the rails, a fork head on each, tines out
    for k in range(6):
        x = -2.5 + k * 1.0 + rnd.uniform(-0.1, 0.1)
        butt = (x, 0.9, 0.0)
        tip = (x + rnd.uniform(-0.15, 0.15), -1.0, 2.0 + rnd.uniform(-0.15, 0.15))
        rod(P, f"pole{k}", butt, tip, 0.055, wood(2, wear=0.15), segs=6)
        d = Vector(tip) - Vector(butt)
        G = _grp(P, f"polehead{k}", tip)
        G.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        fork_head(G, f"fork{k}", (0, 0, 0.08), 4 if k % 2 else 3, 0.42, 0.7, s=2.0)
        K.cyl(f"lash{k}", 0.06, 0.12, rk_rope(), loc=(0, 0, -0.1), segs=6, bev=0, parent=G)
    hay_tufts(P, "tuft", (0, 0.4, 1.0), 8, rnd, r=2.6, h=0.3)
    rag(P, "rag", (2.95, -0.14, 2.55), (3.15, -0.14, 2.55), 0.9, RK_PURPLE, rnd)
    return P


def rakehand_bale_fort(seed=203):
    """A bale fort: a U of square bales three courses high, gun-port gaps with fork tines poking out, a round bale
    in red baler strapping set on top as a turret, and a salvaged slatted baler box as the gatepost. 5.5 m across."""
    rnd = random.Random(seed)
    P = _root("rakehand_bale_fort")
    K.disc("bed", 3.4, _straw_bed(), (0, 0.2), z=0.012, segs=20, wobble=0.12, seed=seed).parent = P
    B = (1.2, 0.6, 0.55)
    # the U: a front wall with a gate gap, two side walls
    walls = []
    for c in range(3):
        z = 0.275 + c * 0.55
        off = 0.3 if c % 2 else 0.0
        for j in range(4):
            x = -2.4 + j * 1.25 + off
            walls.append(("front", (x, -1.6, z), 0))
        for s in (-1, 1):
            for j in range(3):
                walls.append(("side", (s * 2.6, -0.9 + j * 1.25 + off, z), 90))
    for k, (kind, (x, y, z), yaw) in enumerate(walls):
        if kind == "front" and z < 1.2 and 0.0 < x < 1.6:
            continue  # the gate
        if kind == "front" and abs(x - (-1.15)) < 0.2 and 0.6 < z < 1.0:
            continue  # a gun port
        bale(P, f"b{k}", B, (x, y, z), (rnd.uniform(-2, 2), 0, yaw + rnd.uniform(-4, 4)))
    # forks through the port
    for k in range(2):
        fork_head(P, f"port_fork{k}", (-1.15 + k * 0.25, -1.8, 0.85 + k * 0.08), 4, 0.32, 0.5, s=1.2, rot=(90 + 4, 0, k * 8 - 4))
    # the turret: a round bale in red strapping on the back corner
    G = _grp(P, "turret", (-1.6, 1.4, 0.0))
    K.cyl("roundbale", 0.75, 1.2, _straw(), loc=(0, 0, 0.75), segs=18, bev=0.12, parent=G)
    K.cyl("face", 0.66, 1.22, _straw(True), loc=(0, 0, 0.75), segs=18, bev=0, parent=G)
    for z in (0.35, 0.75, 1.15):
        K.torus(f"wrap{z}", 0.76, 0.03, rk_paint(RK_RED, metal=0.4), loc=(0, 0, z), segs=20, parent=G)
    K.cyl("turret_cap", 0.5, 0.08, rk_paint(RK_CREAM), loc=(0, 0, 1.38), segs=12, bev=0.01, parent=G)
    fork_head(G, "turret_fork", (0, 0, 1.45), 5, 0.6, 0.9, s=2.0, rot=(-20, 0, 0))
    # the gatepost: a baler box from a dead Baler, slatted front, red with cream trim
    gx = 1.8
    K.box("baler_box", (0.9, 0.7, 1.4), rk_paint(RK_RED), loc=(gx, -1.6, 0.7), rot=(0, 0, 4), bev=0.03, parent=P)
    for k in range(5):
        K.box(f"slat{k}", (0.8, 0.04, 0.08), rk_paint(RK_CREAM), loc=(gx, -1.97, 0.3 + k * 0.22), rot=(0, 0, 4), bev=0.008, parent=P)
    K.cyl("baler_lamp", 0.08, 0.08, glow(EYE, 3), loc=(gx - 0.25, -1.96, 1.25), rot=(90, 0, 4), segs=10, bev=0.01, parent=P)
    K.cyl("baler_lamp_r", 0.1, 0.06, rk_iron(), loc=(gx - 0.25, -1.94, 1.25), rot=(90, 0, 4), segs=10, bev=0.01, parent=P)
    rakehand_sign(P, "sign", (gx + 0.15, -2.0, 1.25), 0.32, RK_CREAM, rot=(90, 0, 4))
    hay_tufts(P, "tuft", (0, -0.5, 0.0), 8, rnd, r=2.2, h=0.25)
    # collision: the front wall either side of its low gate, the side walls, the turret and the baler box; open at the
    # back, so you can walk in
    K.collider_box(P, "front_l", (2.75, 0.62, 1.65), (-1.62, -1.6, 0.825))
    K.collider_box(P, "front_r", (0.9, 0.62, 1.65), (2.15, -1.6, 0.825))
    K.collider_box(P, "gate_top", (1.6, 0.62, 0.55), (0.75, -1.6, 1.375))
    for s in (-1, 1):
        K.collider_box(P, f"side{s}", (0.62, 3.8, 1.65), (s * 2.6, 0.4, 0.825))
    K.collider_only(K.cyl("col_turret", 0.75, 1.5, None, loc=(-1.6, 1.4, 0.75), segs=8, bev=0, parent=P))
    K.collider_box(P, "baler", (0.9, 0.7, 1.4), (gx, -1.6, 0.7), (0, 0, 4))
    return P


def rakehand_perch(seed=204):
    """A scarecrow perch: a gallows of field timber with a ladder, a plank perch at 2.4 m where a Scarecrow
    Rakehand stands its watch, a caged lantern hung from the arm and purple rags. 5 m tall."""
    rnd = random.Random(seed)
    P = _root("rakehand_perch")
    timber = wood(3, wear=0.2)
    K.box("mast", (0.3, 0.3, 5.2), timber, loc=(0, 0.3, 2.5), bev=0.03, parent=P)
    K.box("arm", (2.6, 0.24, 0.26), timber, loc=(-1.0, 0.3, 4.75), bev=0.03, parent=P)
    beam(P, "brace", (-0.05, 0.3, 3.7), (-1.2, 0.3, 4.65), 0.16, 0.16, wood(1, wear=0.2), bev=0.015)
    for k in range(3):
        a = math.radians(90 + k * 120 + 30)
        beam(P, f"strut{k}", (math.cos(a) * 1.1, 0.3 + math.sin(a) * 1.1, 0.0), (0, 0.3, 1.6), 0.14, 0.14, wood(k % 3, wear=0.2), bev=0.015)
    # the perch: plank deck on two bearers, a fork rail on its front
    for s in (-1, 1):
        plank(P, f"bearer{s}", (0.1, 1.3, 0.16), (s * 0.4, 0.05, 2.32), shade=3)
    for k in range(5):
        plank(P, f"deck{k}", (1.1, 0.22, 0.05), (0, -0.45 + k * 0.24, 2.42), rot=(0, 0, rnd.uniform(-2, 2)), shade=k)
    K.box("perch_rail", (1.1, 0.06, 0.06), rk_paint(RK_PURPLE), loc=(0, -0.6, 2.95), bev=0.01, parent=P)
    for x in (-0.5, 0.5):
        K.box(f"rail_post{x}", (0.07, 0.07, 0.55), rk_paint(RK_PURPLE), loc=(x, -0.6, 2.7), bev=0.01, parent=P)
        fork_head(P, f"rail_fork{x}", (x, -0.6, 3.0), 3, 0.22, 0.4, s=1.0)
    # the ladder up the back of the mast
    for s in (-1, 1):
        beam(P, f"stile{s}", (s * 0.22, 0.8, 0.0), (s * 0.22, 0.5, 2.45), 0.06, 0.06, wood(2, wear=0.2), bev=0.008)
    for k in range(7):
        t = (k + 0.6) / 7.5
        K.box(f"rung{k}", (0.46, 0.04, 0.04), wood(1), loc=(0, 0.8 - 0.3 * t, 2.45 * t), bev=0, parent=P)
    # the hung lantern on a chain, and rags
    for k in range(5):
        K.torus(f"chain{k}", 0.035, 0.008, rk_iron(), loc=(-2.0, 0.3, 4.55 - k * 0.09), rot=(90 * (k % 2), 0, 90 * (k % 2)), segs=8, parent=P)
    lantern_cage(P, "lantern", (-2.0, 0.3, 3.4), 1.7, lit=True)
    rag(P, "rag0", (-1.0, 0.17, 4.65), (-0.5, 0.17, 4.65), 1.1, RK_PURPLE, rnd)
    rag(P, "rag1", (-0.2, 0.14, 3.0), (0.16, 0.14, 3.0), 0.8, RK_RAG, rnd)
    K.box("arm_cap", (0.32, 0.32, 0.12), rk_paint(RK_GOLD, metal=0.4), loc=(-2.25, 0.3, 4.9), bev=0.01, parent=P)
    rakehand_sign(P, "sign", (0, 0.145, 1.6), 0.4, RK_GOLD)
    hay_tufts(P, "tuft", (0, 0.0, 0.0), 6, rnd, r=1.0, h=0.3)
    return P


def rakehand_rake_ring(seed=205):
    """A rake ring: ten forks and rakes planted handle down in a 3 m circle, tines up, roped together with hay twisted
    in the rope, round an old milk churn with the fork painted on it."""
    rnd = random.Random(seed)
    P = _root("rakehand_rake_ring")
    K.disc("bed", 1.7, _straw_bed(), (0, 0), z=0.012, segs=18, wobble=0.1, seed=seed).parent = P
    ring = []
    for k in range(10):
        a = math.radians(k * 36 + 10)
        r = 1.35
        x, y = math.cos(a) * r, math.sin(a) * r
        lean = rnd.uniform(6, 12)
        G = _grp(P, f"tool{k}", (x, y, -0.15), (0, lean, math.degrees(a) + rnd.uniform(-8, 8)))
        h = rnd.uniform(1.55, 1.8)
        K.cyl(f"handle{k}", 0.026, h, wood(2, wear=0.1), loc=(0, 0, h / 2), segs=6, bev=0, parent=G)
        if k % 3 == 1:
            K.box(f"rake{k}", (0.5, 0.05, 0.06), wood(1, wear=0.3), loc=(0, 0, h + 0.03), bev=0.006, parent=G)
            for j in range(8):
                K.cyl(f"rtooth{k}{j}", 0.008, 0.12, rk_steel(), loc=(-0.21 + j * 0.06, 0, h + 0.12), segs=4, r2=0.003, bev=0, parent=G)
        else:
            fork_head(G, f"fork{k}", (0, 0, h + 0.05), 3 + k % 2, 0.3, 0.42, s=1.5)
        ring.append(G)
    bpy.context.view_layer.update()
    tops = [tuple(G.matrix_world @ Vector((0, 0, 1.35))) for G in ring]
    for k in range(10):
        rod(P, f"rope{k}", tops[k], tops[(k + 1) % 10], 0.016, rk_rope(), segs=4)
        if k % 2 == 0:
            K.cyl(f"rope_hay{k}", 0.05, 0.25, _straw(), loc=tops[k], rot=(rnd.uniform(-30, 30), 90, math.degrees(math.radians(k * 36 + 100))), segs=5, bev=0, parent=P)
    G = _grp(P, "churn", (0, 0, 0))
    turned(G, "churn_body", [(0.0, 0.0), (0.24, 0.0), (0.26, 0.05), (0.26, 0.55), (0.14, 0.72), (0.12, 0.85), (0.0, 0.85)], rk_paint(ZINC, metal=0.5), segs=16)
    for z in (0.12, 0.45):
        K.torus(f"churn_band{z}", 0.265, 0.016, rk_paint(RK_GREEN), loc=(0, 0, z), segs=16, parent=G)
    K.cyl("churn_lid", 0.13, 0.07, rk_paint(RK_GREEN), loc=(0, 0, 0.88), segs=12, bev=0.01, parent=G)
    rakehand_sign(G, "sign", (0, -0.266, 0.3), 0.24, RK_MUSTARD)
    hay_tufts(P, "tuft", (0, 0, 0.0), 10, rnd, r=1.2, h=0.25)
    return P


def rakehand_tool_hoard(seed=206):
    """A tool hoard: a big slatted crate painted field green with the mustard fork, stuffed with hay and fork heads,
    a sickle on top, forks and a shovel leaning on it. 2.2 m across, 1.6 m tall."""
    rnd = random.Random(seed)
    P = _root("rakehand_tool_hoard")
    W, D, H = 1.3, 0.85, 0.75
    green = rk_paint(RK_GREEN)
    for s in (-1, 1):
        for k in range(3):
            K.box(f"side{s}{k}", (W, 0.04, 0.2), green, loc=(0, s * D / 2, 0.13 + k * 0.24), rot=(0, rnd.uniform(-1, 1), 0), bev=0.006, parent=P)
            K.box(f"end{s}{k}", (0.04, D, 0.2), green, loc=(s * W / 2, 0, 0.13 + k * 0.24), bev=0.006, parent=P)
        for t in (-1, 1):
            K.box(f"corner{s}{t}", (0.07, 0.07, H), wood(3), loc=(s * (W / 2 - 0.02), t * (D / 2 - 0.02), H / 2), bev=0.008, parent=P)
    K.box("floor", (W, D, 0.04), wood(2), loc=(0, 0, 0.03), bev=0, parent=P)
    rakehand_sign(P, "sign", (0, -D / 2 - 0.025, 0.38), 0.5, RK_MUSTARD)
    K.box("tag", (0.18, 0.01, 0.1), M("#d8d6cc", rough=0.8), loc=(0.48, -D / 2 - 0.03, 0.6), rot=(0, 10, 0), bev=0, parent=P)
    K.box("hay_fill", (W - 0.08, D - 0.08, 0.1), _straw(True), loc=(0, 0, 0.66), bev=0.02, parent=P)
    hay_tufts(P, "fill_tuft", (0, 0, 0.62), 14, rnd, r=0.5, h=0.35)
    for k in range(5):
        fork_head(P, f"head{k}", (-0.45 + k * 0.22, rnd.uniform(-0.2, 0.2), 0.72), 3 + k % 3, 0.28, 0.42, s=1.2,
                  rot=(rnd.uniform(-25, 25), rnd.uniform(-30, 30), rnd.uniform(-20, 20)))
    G = _grp(P, "sickle", (0.35, -0.1, 0.92), (0, 70, 30))
    K.torus("sickle_blade", 0.2, 0.014, rk_steel(), loc=(0, 0, 0.2), rot=(90, 0, 0), arc=200, segs=14, parent=G)
    K.cyl("sickle_grip", 0.022, 0.18, wood(2), loc=(0.2, 0, 0.05), segs=6, bev=0, parent=G)
    long_tool(P, "lean0", (-1.05, -0.75, 0.0), (-0.62, -0.43, 1.0), kind="fork", L=1.3)
    long_tool(P, "lean1", (0.95, -0.8, 0.0), (0.6, -0.43, 1.0), kind="shovel", L=1.15)
    long_tool(P, "lean2", (1.1, 0.25, 0.0), (0.66, 0.15, 1.0), kind="fork", L=1.3)
    hay_tufts(P, "tuft", (0, 0, 0.0), 8, rnd, r=1.0, h=0.25)
    return P


def crop_tuft(P, name, loc, h, mat, rnd, lean_out=(0.0, 0.0)):
    """A clump of standing stalks: a few thin tapered blades from one root, splaying a little, leaning away from the
    clearing (lean_out is the outward direction)."""
    x, y = loc
    for k in range(5):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(0.02, 0.14)
        bx, by = x + math.cos(a) * r, y + math.sin(a) * r
        hh = h * rnd.uniform(0.6, 1.05)
        tilt = rnd.uniform(10, 30)
        tx, ty = math.cos(a) * 0.6 + lean_out[0] * 3, math.sin(a) * 0.6 + lean_out[1] * 3
        taper(P, f"{name}{k}", (bx, by, -0.02), (bx + tx * hh * math.sin(math.radians(tilt)), by + ty * hh * math.sin(math.radians(tilt)), hh),
              rnd.uniform(0.022, 0.035), 0.004, mat, segs=3)


def rakehand_trample(seed=207):
    """A trampled marker: a 3 m clearing of flattened crop, the laid straw making their three-tine fork, a single fork
    staked at its foot with a purple rag. Clumps of crop still stand round it, thinning out into the grass."""
    rnd = random.Random(seed)
    P = _root("rakehand_trample")
    flat = _straw_bed()
    dark = K.mat("hay_lay", "#8f7a45", rough=1.0, noise_scale=16.0)
    K.disc("flat", 1.55, flat, (0, 0), z=0.015, segs=20, wobble=0.12, seed=seed).parent = P
    K.disc("stubble", 2.3, K.mat("stubble", "#a99a5a", rough=1.0, noise_scale=8.0), (0, 0), z=0.008, segs=20, wobble=0.16, seed=seed + 1).parent = P
    crop = K.mat("crop", CROP, rough=0.95, grime=0.15, noise_scale=10.0)
    crop_d = K.mat("crop_d", "#b39a4c", rough=0.95, grime=0.15, noise_scale=10.0)
    # standing crop round it in clumps, densest at the clearing's edge and thinning out, gaps where it was pushed over
    for k in range(56):
        a = 2 * math.pi * k / 56 + rnd.uniform(-0.1, 0.1)
        band = rnd.random() ** 1.4
        r = 1.55 + band * 1.0
        if rnd.random() < 0.12:
            continue
        h = 0.55 * (1 - 0.5 * band) * rnd.uniform(0.8, 1.1)
        crop_tuft(P, f"tuft{k}_", (math.cos(a) * r, math.sin(a) * r), h, crop if k % 3 else crop_d, rnd,
                  lean_out=(math.cos(a) * 0.15, math.sin(a) * 0.15))
    # crop laid flat at the clearing's edge, swirling out the way it was pushed
    for k in range(16):
        a = math.radians(k * 22.5 + rnd.uniform(-8, 8))
        rr = rnd.uniform(1.15, 1.45)
        K.box(f"laid{k}", (rnd.uniform(0.35, 0.6), 0.1, 0.02), crop_d if k % 2 else crop, loc=(math.cos(a) * rr, math.sin(a) * rr, 0.025),
              rot=(0, rnd.uniform(-3, 3), math.degrees(a) + rnd.uniform(20, 45)), bev=0, parent=P)
    # the fork, in straw laid in strips: each arm a few strands, not one crisp board
    arms = [((0, -0.75), (0.0, 1.1), 0), ((0, -0.2), (1.3, 0.0), 90)] + [((x, 0.3), (0.0, 1.0), 0) for x in (-0.6, 0.0, 0.6)]
    for n, ((cx, cy), (lx, ly), yaw) in enumerate(arms):
        L = max(lx, ly)
        for j in range(3):
            off = (j - 1) * 0.05 + rnd.uniform(-0.015, 0.015)
            ox, oy = (off, 0) if yaw == 0 else (0, off)
            K.box(f"fork{n}_{j}", (0.07, L * rnd.uniform(0.88, 1.0), 0.02), dark if j != 1 else crop_d,
                  loc=(cx + ox, cy + oy, 0.03 + j * 0.002), rot=(0, 0, yaw + rnd.uniform(-4, 4)), bev=0, parent=P)
    for x in (-0.6, 0.0, 0.6):
        K.cyl(f"tine_pt{x}", 0.09, 0.22, dark, loc=(x, 0.91, 0.03), rot=(-90, 0, 0), segs=4, r2=0.0, bev=0, parent=P)
    G = _grp(P, "stake", (0.0, -1.3, -0.25), (6, 0, 0))
    K.cyl("stake_handle", 0.026, 1.7, wood(2, wear=0.1), loc=(0, 0, 0.85), segs=6, bev=0, parent=G)
    fork_head(G, "stake_fork", (0, 0, 1.75), 3, 0.3, 0.42, s=1.5)
    rag(P, "rag", (-0.02, -1.25, 1.2), (0.14, -1.25, 1.2), 0.5, RK_PURPLE, rnd)
    return P


PROPS = {
    "scuttler_nest": scuttler_nest,
    "scuttler_cradle": scuttler_cradle,
    "scuttler_totem": scuttler_totem,
    "scuttler_bore": scuttler_bore,
    "scuttler_stomp_ring": scuttler_stomp_ring,
    "scuttler_blast_pit": scuttler_blast_pit,
    "scuttler_seed_cache": scuttler_seed_cache,
    "rakehand_shrine": rakehand_shrine,
    "rakehand_palisade": rakehand_palisade,
    "rakehand_bale_fort": rakehand_bale_fort,
    "rakehand_perch": rakehand_perch,
    "rakehand_rake_ring": rakehand_rake_ring,
    "rakehand_tool_hoard": rakehand_tool_hoard,
    "rakehand_trample": rakehand_trample,
}
