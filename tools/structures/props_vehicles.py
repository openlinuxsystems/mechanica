"""Vehicle props for the scenes between places (docs/world-generation.md, Scenes): the old colony's vehicles, wrecked
and rusting where they stopped when the colony fell, in the Colony Works family of docs/art-direction.md.

Road: a van, a pickup, a low two-seater, a box truck, a tank trailer without its cab, an empty flatbed trailer, a
motorbike, a bicycle, a golf cart and a quad bike. Rail: a freight wagon off its rails, a length of track, a signal mast,
a buffer stop and a mine cart. Field and survey: a boat on its trailer, a combine harvester, a six-wheeled survey rover
and a hovercraft. Air: a plane's tail standing out of the ground, a helicopter on its side and a light plane with a
broken wing. Loose parts: a giant tyre, an engine block on a pallet and a car seat.

Like the colony's other vehicles (car_wreck, truck_wreck, tractor_wreck in props_colony_b.py) the road and rail
vehicles run along X with their front towards +X; the rest face -Y. Each builder returns an empty with the prop under
it, its origin on the ground at the prop's centre (Blender Z up). They are world objects, so their models carry no
collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, shell_panel, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, RUST, ZINC, CONCRETE, GUNMETAL, STONE_D
from props import plank, _root
from props_colony_a import dish_mesh
from props_colony_b import label, _grp, beam, rod, plate_xz, lathe, tyre, rust_patch, mound, TYRE, GLASS
from props_settler_a import lump

LENS, CRACK, FOAM = "#cfd6d8", "#9fb3bd", "#d8c89a"
BALLAST = "#8b877c"
LAMP_RED, LAMP_GREEN = "#5a2a28", "#2a4a3a"   # dead signal lenses
GRASS, GRASS_D = "#5f9a36", "#4b7f2a"


# ---------------------------------------------------------------- materials
def _paint(col, wear=0.85, grime=0.65, moss=0.15):
    """Colony paint gone bad: worn, grimy, scratched, a little moss on top."""
    return M(col, wear=wear, grime=grime, scratches=0.7, moss=moss)


def _thin(col, metal=0.3):
    """Paint for thin and round parts (tubes, rails, rims), kept low on wear so it does not go pale."""
    return M(col, metal=metal, wear=0.25, grime=0.5)


def _dark():
    return M(GRAPHITE, metal=0.4, wear=0.25, grime=0.5)


def _rust():
    return M(RUST, metal=0.4, wear=0.2, grime=0.5, rough=0.85)


def _glass():
    return K.mat("darkglass", GLASS, rough=0.15, metal=0.2)


def _lens():
    return K.mat("deadlens", LENS, rough=0.2)


def _cloth():
    return K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4)


def _rubber():
    return K.mat("tyre", TYRE, rough=0.9, grime=0.4)


def _concrete():
    return K.mat("concrete", CONCRETE, rough=0.9, wear=0.5, grime=0.6, cracks=0.2, moss=0.2)


# ---------------------------------------------------------------- helpers
def profile(P, name, pts, width, mat, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.04):
    """A body drawn as a side outline (x, z) and pushed out to its width across Y: vans, cabs, hulls of things."""
    return plate_xz(name, pts, width, mat, loc=loc, rot=rot, parent=P, bev=bev)


def along(P, name, pts, y, w, t, mat, out=0.004):
    """Thin strips laid along an (x, z) outline at a given y: stripes, seams and trim over a profile body."""
    for k in range(len(pts) - 1):
        (x0, z0), (x1, z1) = pts[k], pts[k + 1]
        dx, dz = x1 - x0, z1 - z0
        L = math.hypot(dx, dz)
        nx, nz = dz / L, -dx / L                     # outward normal of a clockwise-from-the-front outline
        K.box(f"{name}{k}", (L + w * 0.3, w, t), mat, loc=((x0 + x1) / 2 + nx * out, y, (z0 + z1) / 2 + nz * out),
              rot=(0, -math.degrees(math.atan2(dz, dx)), 0), bev=0, parent=P)


def pane(P, name, a, b, w, h, mat, out=0.012, t=0.02):
    """A pane (windscreen, rear glass) on the slope from a to b of an (x, z) outline, w wide across Y."""
    (x0, z0), (x1, z1) = a, b
    dx, dz = x1 - x0, z1 - z0
    L = math.hypot(dx, dz)
    nx, nz = dz / L, -dx / L
    if nx < 0:
        nx, nz = -nx, -nz
    if nz < -0.2:
        nx, nz = -nx, -nz
    return K.box(name, (t, w, h or L * 0.86), mat, loc=((x0 + x1) / 2 + nx * out, 0, (z0 + z1) / 2 + nz * out),
                 rot=(0, math.degrees(math.atan2(dx, dz)), 0), bev=0.005, parent=P)


def window(P, name, pts, y, mat, t=0.02):
    """A side window: an (x, z) outline standing just proud of the side of a body at y."""
    return plate_xz(name, pts, t, mat, loc=(0, y, 0), parent=P, bev=0)


def cracks(P, loc, rot, n=3, size=0.4):
    """A star of cracks in a pane (rot is the pane's rotation)."""
    for k in range(n):
        K.box("crack", (0.006, size, 0.008), K.mat("crack", CRACK, rough=0.3), loc=loc, rot=(rot[0] + k * 60 + 15, rot[1], rot[2]), bev=0, parent=P)


def arch(P, name, x, y, z, r, mat):
    """The trim round a wheel arch on a body side."""
    K.torus(name, r, 0.03, mat, loc=(x, y, z), rot=(90, 0, 0), arc=180, segs=14, parent=P)


def lying_wheel(P, name, loc, r, w, tilt=(0, 0), rim=None, lugs=0):
    """A wheel off its vehicle, lying in the grass on its side (loc on the ground under its hub)."""
    x, y, z = loc
    G = _grp(P, name, (x, y, z + w / 2), (90 + tilt[0], tilt[1], 0))
    tyre(G, name, (0, 0, 0), r, w, rim=rim, lugs=lugs)
    return G


def rail(P, name, x0, x1, y, z0, s=1.0, rust=None, shine=None):
    """A flat-bottom rail from x0 to x1 standing on z0 (s scales the section for narrow-gauge track)."""
    L, xc = x1 - x0, (x0 + x1) / 2
    rust = rust or _rust()
    K.box(f"{name}_foot", (L, 0.14 * s, 0.022 * s), rust, loc=(xc, y, z0 + 0.011 * s), bev=0, parent=P)
    K.box(f"{name}_web", (L, 0.022 * s, 0.1 * s), rust, loc=(xc, y, z0 + 0.072 * s), bev=0, parent=P)
    K.box(f"{name}_head", (L, 0.07 * s, 0.04 * s), rust, loc=(xc, y, z0 + 0.142 * s), bev=0.006, parent=P)
    K.box(f"{name}_shine", (L, 0.05 * s, 0.006), shine or M(ZINC, metal=0.7, wear=0.1, grime=0.2, rough=0.35), loc=(xc, y, z0 + 0.163 * s), bev=0, parent=P)
    return z0 + 0.165 * s


def track(P, name, x0, x1, rnd, gauge=1.435, pitch=0.65, wood_sleepers=False, ballast=True, skip=(), s=1.0, clips=True):
    """Track on sleepers (and a ballast bed): returns the height of the rail tops."""
    L = x1 - x0
    zb = 0.0
    if ballast:
        bw = gauge + 1.9 * s
        prof = [(-bw / 2 - 0.35, 0.0), (bw / 2 + 0.35, 0.0), (bw / 2, 0.22), (-bw / 2, 0.22)]
        profile(P, f"{name}_ballast", prof, L, K.mat("ballast", BALLAST, rough=1.0, grime=0.3, cracks=0.3, moss=0.3, noise_scale=14.0),
                loc=((x0 + x1) / 2, 0, 0), rot=(0, 0, 90), bev=0.03)
        zb = 0.14
    sl = (0.26 * s, gauge + 1.1 * s, 0.16 * s)
    n = int(L / pitch)
    conc = _concrete()
    zs = zb + sl[2] / 2
    for k in range(n):
        if k in skip:
            continue
        x = x0 + pitch * (k + 0.5) + rnd.uniform(-0.03, 0.03)
        m = K.mat(f"sleeper{k % 3}", ["#6e5a44", "#5f4c39", "#7a634a"][k % 3], rough=0.9, wear=0.3, grime=0.5, cracks=0.4, moss=0.3,
                  noise_scale=3.0) if wood_sleepers else conc
        K.box(f"{name}_sleeper{k}", sl, m, loc=(x, rnd.uniform(-0.03, 0.03), zs), rot=(0, 0, rnd.uniform(-2.5, 2.5)), bev=0.02 * s, parent=P)
        for sd in (-1, 1) if clips else ():
            for e in (-1, 1):
                K.box(f"{name}_clip", (0.07 * s, 0.05 * s, 0.035 * s), _dark(), loc=(x, sd * gauge / 2 + e * 0.09 * s, zs + sl[2] / 2 + 0.017 * s),
                      bev=0, parent=P)
    zr = zs + sl[2] / 2
    for sd in (-1, 1):
        top = rail(P, f"{name}_rail{sd}", x0 + 0.05, x1 - 0.05, sd * gauge / 2, zr, s=s)
    return top


def bogie(P, name, loc, rot=(0, 0, 0), gauge=1.435, r=0.42, base=1.8):
    """A freight bogie: two wheelsets in cast side frames with spring nests and a bolster (loc on the rail tops)."""
    G = _grp(P, name, loc, rot)
    dark, steel = _dark(), M(GUNMETAL, metal=0.5, wear=0.25, grime=0.6)
    rust = _rust()
    for x in (-base / 2, base / 2):
        K.cyl("axle", 0.07, gauge + 0.3, dark, loc=(x, 0, r), rot=(90, 0, 0), segs=10, bev=0, parent=G)
        for s in (-1, 1):
            K.cyl("wheel", r, 0.13, rust, loc=(x, s * (gauge / 2 + 0.03), r), rot=(90, 0, 0), segs=14, bev=0, parent=G)
            K.cyl("flange", r + 0.035, 0.03, rust, loc=(x, s * (gauge / 2 - 0.05), r), rot=(90, 0, 0), segs=14, bev=0, parent=G)
            K.box("axlebox", (0.32, 0.2, 0.3), steel, loc=(x, s * (gauge / 2 + 0.2), r + 0.02), bev=0, parent=G)
    for s in (-1, 1):
        y = s * (gauge / 2 + 0.22)
        K.box("sideframe", (base + 0.3, 0.14, 0.16), steel, loc=(0, y, r + 0.2), bev=0, parent=G)
        beam(G, "sideframe_a", (-base / 2 + 0.15, y, r + 0.12), (-0.35, y, r - 0.12), 0.14, steel, bev=0)
        beam(G, "sideframe_b", (base / 2 - 0.15, y, r + 0.12), (0.35, y, r - 0.12), 0.14, steel, bev=0)
        K.box("sideframe_lo", (0.8, 0.14, 0.12), steel, loc=(0, y, r - 0.12), bev=0, parent=G)
        for k in range(2):
            K.cyl("spring", 0.07, 0.24, M(SIGNAL, metal=0.3, wear=0.2, grime=0.5), loc=(-0.09 + k * 0.18, y, r + 0.02), segs=6, bev=0, parent=G)
    K.box("bolster", (0.36, gauge + 0.55, 0.22), steel, loc=(0, 0, r + 0.2), bev=0, parent=G)
    return G


def hull_mesh(name, L, B, D, mat, n=12, t=0.04, sheer=0.16):
    """An open boat hull, transom at -X, bow at +X: an outer skin, an inner skin t inside it, joined at the gunwale."""
    bm = bmesh.new()
    rings_o, rings_i = [], []
    for i in range(n + 1):
        u = i / n
        x = -L / 2 + L * u
        f = max(0.0, (u - 0.5) / 0.5)
        b = max(B / 2 * (1 - f ** 1.7), 0.03)
        d = D * (1 - 0.45 * f ** 1.4)
        top = sheer * f ** 2
        prof = [(-b, top), (-b * 0.97, -d * 0.45), (-b * 0.7, -d * 0.88), (0.0, -d), (b * 0.7, -d * 0.88), (b * 0.97, -d * 0.45), (b, top)]
        k = max(0.0, (b - t) / b)
        xi = x + (t if i == 0 else 0.0) - (min(t, 0.02) if i == n else 0.0)
        rings_o.append([bm.verts.new((x, y, z)) for y, z in prof])
        rings_i.append([bm.verts.new((xi, y * k, z + (0 if j in (0, len(prof) - 1) else t))) for j, (y, z) in enumerate(prof)])
    m = len(rings_o[0])
    for i in range(n):
        o0, o1, i0, i1 = rings_o[i], rings_o[i + 1], rings_i[i], rings_i[i + 1]
        for j in range(m - 1):
            bm.faces.new((o0[j], o1[j], o1[j + 1], o0[j + 1]))
            bm.faces.new((i0[j], i0[j + 1], i1[j + 1], i1[j]))
        bm.faces.new((o0[0], i0[0], i1[0], o1[0]))
        bm.faces.new((o0[-1], o1[-1], i1[-1], i0[-1]))
    bm.faces.new(list(reversed(rings_o[0])))
    bm.faces.new(rings_i[0])
    bm.faces.new((rings_o[0][0], rings_o[0][-1], rings_i[0][-1], rings_i[0][0]))
    for j in range(m - 1):
        bm.faces.new((rings_o[-1][j], rings_o[-1][j + 1], rings_i[-1][j + 1], rings_i[-1][j]))
    bm.faces.new((rings_o[-1][-1], rings_o[-1][0], rings_i[-1][0], rings_i[-1][-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.from_bm(name, bm, mat, smooth=True)


def grass_tuft(P, name, loc, rnd, n=7, h=0.3):
    """A few blades of long grass round something lying in the field."""
    x, y = loc
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        K.cyl(f"{name}{k}", 0.014, h * rnd.uniform(0.7, 1.2), M(rnd.choice((GRASS, GRASS_D)), wear=0, grime=0, scratches=0),
              loc=(x + math.cos(a) * 0.05, y + math.sin(a) * 0.05, h * 0.45), rot=(math.cos(a) * 20, math.sin(a) * 20 + rnd.uniform(-10, 10), 0),
              segs=4, bev=0, parent=P, r2=0.004)


def rusty(P, rnd, n, xr, zr, y, thin=0.004):
    """n rust patches on a flat body side at y (x and z ranges)."""
    for k in range(n):
        rust_patch(P, (rnd.uniform(*xr), y + math.copysign(thin / 2, y), rnd.uniform(*zr)), (rnd.uniform(0.14, 0.4), thin, rnd.uniform(0.08, 0.22)))


# ================================================================== road
def van_wreck(seed=41):
    """A colony panel van on two flat tyres, its sliding door run back open on the dark load bay, rust through the bone
    paint, the roof rack and the ladder still on it."""
    rnd = random.Random(seed)
    P = _root("van")
    paint = _paint(BONE)
    steel, dark, glass, lens = _thin(STEEL), _dark(), _glass(), _lens()
    rw, W = 0.36, 1.9
    rim = M(ZINC, metal=0.5, wear=0.25, grime=0.6)
    for x, s, f in ((1.6, -1, 0.06), (1.6, 1, 0.05), (-1.5, -1, 0.3), (-1.5, 1, 0.08)):
        tyre(P, f"wheel{x}{s}", (x, s * 0.84, rw * (1 - f)), rw, 0.22, flat=f, rim=rim)
    B = _grp(P, "body", (0, 0, 0.33), (2.2, -1.4, 0))
    body = [(-2.42, 1.62), (-2.36, 1.7), (1.0, 1.7), (1.22, 1.62), (1.85, 0.92), (2.3, 0.8), (2.44, 0.64), (2.44, 0.2), (2.3, -0.04), (-2.38, -0.04),
            (-2.44, 0.05)]
    profile(B, "shell", body, W, paint, bev=0.05)
    K.box("floor", (4.6, W - 0.2, 0.1), dark, loc=(0, 0, -0.06), bev=0.01, parent=B)
    pane(B, "windscreen", (1.85, 0.92), (1.22, 1.62), W - 0.22, 0, glass)
    cracks(B, (1.56, 0.3, 1.3), (0, math.degrees(math.atan2(-0.63, 0.7)), 0))
    for s in (-1, 1):
        y = s * (W / 2 + 0.006)
        window(B, f"door_glass{s}", [(0.6, 1.0), (1.66, 1.0), (1.2, 1.53), (0.6, 1.53)], y, glass)
        K.box(f"door_seam{s}", (0.02, 0.012, 1.45), dark, loc=(0.48, y, 0.78), bev=0, parent=B)
        K.box(f"handle{s}", (0.16, 0.03, 0.04), dark, loc=(0.62, s * (W / 2 + 0.015), 0.88), bev=0.005, parent=B)
        K.box(f"mirror_arm{s}", (0.05, 0.22, 0.04), dark, loc=(1.62, s * (W / 2 + 0.1), 1.06), bev=0.005, parent=B)
        K.box(f"mirror{s}", (0.06, 0.12, 0.24), dark, loc=(1.6, s * (W / 2 + 0.22), 1.12), bev=0.012, parent=B)
        K.box(f"headlight{s}", (0.02, 0.32, 0.16), lens, loc=(2.445, s * 0.6, 0.5), bev=0.005, parent=B)
        K.box(f"taillight{s}", (0.02, 0.14, 0.3), M(SIGNAL, wear=0.2), loc=(-2.445, s * 0.82, 0.55), bev=0.005, parent=B)
        for x in (1.6, -1.5):
            arch(B, f"arch{x}{s}", x, y, 0.0, 0.42, dark)
        K.box(f"rack_rail{s}", (3.2, 0.05, 0.05), steel, loc=(-0.65, s * 0.78, 1.82), bev=0.008, parent=B)
        for x in (-2.1, 0.75):
            K.box(f"rack_foot{x}{s}", (0.06, 0.06, 0.12), steel, loc=(x, s * 0.78, 1.76), bev=0, parent=B)
    for k in range(5):
        K.box(f"rack_bar{k}", (0.05, 1.6, 0.04), steel, loc=(-2.1 + k * 0.7, 0, 1.86), bev=0.006, parent=B)
    # the steel stripe, broken where the sliding door has run back over it
    for s, segs in ((1, ((-2.42, 2.42),)), (-1, ((-2.42, -1.68), (0.48, 2.42)))):
        for a, b in segs:
            K.box("stripe", (b - a, 0.01, 0.12), M(STEEL, wear=0.6), loc=((a + b) / 2, s * (W / 2 + 0.006), 0.5), bev=0, parent=B)
    # the open load bay and the door slid back over the rear quarter
    K.box("bay", (1.05, 0.012, 1.42), K.mat("shadow", "#15171a", rough=0.9), loc=(-0.1, -W / 2 - 0.004, 0.76), bev=0, parent=B)
    K.box("bay_floor", (1.05, 0.25, 0.04), steel, loc=(-0.1, -W / 2 + 0.12, 0.06), bev=0, parent=B)
    K.box("crate", (0.5, 0.012, 0.42), M(MUSTARD, wear=0.4), loc=(-0.3, -W / 2 - 0.008, 0.29), bev=0, parent=B)
    D = _grp(B, "door", (-1.12, -W / 2 - 0.05, 0.78), (0, -1.5, 1.5))
    K.box("door_panel", (1.1, 0.05, 1.46), paint, bev=0.015, parent=D)
    K.box("door_stripe", (1.1, 0.01, 0.12), M(STEEL, wear=0.6), loc=(0, -0.03, -0.28), bev=0, parent=D)
    K.box("door_handle", (0.04, 0.03, 0.16), dark, loc=(0.48, -0.035, 0.1), bev=0.005, parent=D)
    K.box("door_track", (2.6, 0.04, 0.04), dark, loc=(-0.8, -W / 2 - 0.02, 1.52), bev=0, parent=B)
    label(B, "CW-27", (1.05, -W / 2 - 0.012, 0.75), 0.15, M(STEEL, wear=0.3))
    # front and back
    K.box("grille", (0.03, 1.0, 0.22), dark, loc=(2.445, 0, 0.42), bev=0.005, parent=B)
    for k in range(3):
        K.box(f"grille_bar{k}", (0.03, 0.96, 0.025), steel, loc=(2.455, 0, 0.35 + k * 0.07), bev=0, parent=B)
    K.box("bumper_f", (0.14, 1.94, 0.16), dark, loc=(2.5, 0.08, 0.08), rot=(5, 0, -4), bev=0.02, parent=B)
    K.box("bumper_r", (0.14, 1.94, 0.16), dark, loc=(-2.47, 0, 0.08), bev=0.02, parent=B)
    K.box("rear_seam", (0.012, 0.02, 1.5), dark, loc=(-2.444, 0, 0.82), bev=0, parent=B)
    for k in range(6):
        K.box(f"ladder_rung{k}", (0.03, 0.04, 0.42), steel, loc=(-2.48, 0.62, 0.25 + k * 0.27), rot=(90, 0, 0), bev=0, parent=B)
    for y in (0.42, 0.82):
        K.box("ladder_rail", (0.04, 0.04, 1.6), steel, loc=(-2.48, y, 1.02), bev=0.005, parent=B)
    # rust through the paint
    rusty(B, rnd, 5, (-2.2, 2.2), (0.1, 0.4), W / 2)
    rusty(B, rnd, 4, (0.6, 2.2), (0.1, 0.4), -W / 2)
    rusty(B, rnd, 2, (-2.3, -1.75), (0.1, 1.2), -W / 2)
    rust_patch(B, (-0.6, 0.2, 1.705), (0.9, 0.6, 0.006), rot=(0, 0, 15))
    rust_patch(B, (2.38, 0.6, 0.7), (0.3, 0.4, 0.006), rot=(0, -60, 0))
    return P


def pickup_wreck(seed=42):
    """A colony pickup in steel blue, its driver's door hanging open, the tailgate down on one hinge and a spare tyre
    and some planks still in the bed."""
    rnd = random.Random(seed)
    P = _root("pickup")
    paint = _paint(STEEL, wear=0.9, grime=0.7)
    steel, dark, glass, lens = _thin(STEEL), _dark(), _glass(), _lens()
    bone = M(BONE, wear=0.5)
    rw, W = 0.36, 1.84
    rim = M(BONE, metal=0.3, wear=0.25, grime=0.6)
    for x, s, f in ((1.6, -1, 0.04), (1.6, 1, 0.22), (-1.55, -1, 0.06), (-1.55, 1, 0.08)):
        tyre(P, f"wheel{x}{s}", (x, s * 0.82, rw * (1 - f)), rw, 0.24, flat=f, rim=rim)
    B = _grp(P, "body", (0, 0, 0.33), (-1.8, 0.6, 0))
    cab = [(-0.4, -0.04), (-0.4, 1.36), (-0.3, 1.43), (0.45, 1.43), (1.0, 0.86), (2.3, 0.76), (2.45, 0.6), (2.45, 0.2), (2.3, -0.04)]
    profile(B, "cab", cab, W, paint, bev=0.05)
    pane(B, "windscreen", (1.0, 0.86), (0.45, 1.43), W - 0.22, 0, glass)
    cracks(B, (0.73, -0.35, 1.16), (0, math.degrees(math.atan2(-0.55, 0.57)), 0), size=0.5)
    K.box("rear_glass", (0.02, 1.2, 0.36), glass, loc=(-0.41, 0, 1.12), bev=0.004, parent=B)
    K.box("hood_seam", (0.012, 1.5, 0.012), dark, loc=(1.05, 0, 0.87), bev=0, parent=B)
    for s in (-1, 1):
        y = s * (W / 2 + 0.006)
        if s > 0:
            window(B, "door_glass", [(-0.25, 0.9), (0.92, 0.9), (0.49, 1.35), (-0.25, 1.35)], y, glass)
            K.box("handle", (0.16, 0.03, 0.04), dark, loc=(-0.1, s * (W / 2 + 0.015), 0.78), bev=0.005, parent=B)
        K.box(f"headlight{s}", (0.02, 0.3, 0.14), lens, loc=(2.455, s * 0.6, 0.45), bev=0.005, parent=B)
        K.box(f"stripe{s}", (2.8, 0.01, 0.08), bone, loc=(1.05, y, 0.5), bev=0, parent=B)
        K.box(f"stripe_bed{s}", (2.05, 0.01, 0.08), bone, loc=(-1.45, s * (W / 2 + 0.006), 0.5), bev=0, parent=B)
        arch(B, f"arch_f{s}", 1.6, y, 0.0, 0.42, dark)
        arch(B, f"arch_r{s}", -1.55, y, 0.0, 0.42, dark)
        K.box(f"mirror{s}", (0.06, 0.12, 0.2), dark, loc=(0.95, s * (W / 2 + 0.16), 1.0), bev=0.01, parent=B)
        # the bed's sides
        profile(B, f"bed_side{s}", [(-2.45, -0.04), (-0.45, -0.04), (-0.45, 0.82), (-2.45, 0.82)], 0.06, paint, loc=(0, s * (W / 2 - 0.03), 0), bev=0.015)
        K.box(f"bed_cap{s}", (2.0, 0.1, 0.05), dark, loc=(-1.45, s * (W / 2 - 0.03), 0.84), bev=0.01, parent=B)
        K.box(f"taillight{s}", (0.03, 0.12, 0.26), M(SIGNAL, wear=0.2), loc=(-2.465, s * (W / 2 - 0.06), 0.62), bev=0.005, parent=B)
    # driver's door open on its front hinge, the cab dark behind it
    K.box("door_hole", (1.18, 0.012, 1.3), K.mat("shadow", "#15171a", rough=0.9), loc=(0.35, -W / 2 - 0.004, 0.66), bev=0, parent=B)
    K.box("seat_edge", (0.5, 0.02, 0.14), _cloth(), loc=(0.15, -W / 2 - 0.01, 0.42), bev=0.01, parent=B)
    K.box("seat_back_edge", (0.12, 0.02, 0.6), _cloth(), loc=(-0.15, -W / 2 - 0.01, 0.76), rot=(0, -12, 0), bev=0.01, parent=B)
    Dg = _grp(B, "door", (0.95, -W / 2, 0.0), (0, 0, 52))
    profile(Dg, "door_panel", [(-1.2, 0.02), (0.0, 0.02), (0.0, 0.86), (-0.48, 1.35), (-1.2, 1.35)], 0.06, paint, loc=(0, -0.04, 0), bev=0.012)
    window(Dg, "door_glass", [(-1.12, 0.9), (-0.06, 0.9), (-0.47, 1.3), (-1.12, 1.3)], -0.075, glass)
    K.box("door_stripe", (1.18, 0.01, 0.08), bone, loc=(-0.6, -0.076, 0.5), bev=0, parent=Dg)
    K.box("door_handle", (0.16, 0.03, 0.04), dark, loc=(-1.05, -0.085, 0.78), bev=0.005, parent=Dg)
    # bed floor, the tailgate hanging and the load
    K.box("bed_floor", (2.0, W - 0.12, 0.05), dark, loc=(-1.45, 0, 0.36), bev=0.01, parent=B)
    K.box("bed_front", (0.06, W - 0.12, 0.5), paint, loc=(-0.45, 0, 0.6), bev=0.01, parent=B)
    K.box("bed_under", (2.0, W - 0.12, 0.38), dark, loc=(-1.45, 0, 0.16), bev=0.01, parent=B)
    T = _grp(B, "tailgate", (-2.47, W / 2 - 0.08, 0.36), (-9, 0, 0))
    K.box("gate", (0.48, W - 0.1, 0.06), paint, loc=(-0.24, -W / 2 + 0.05, 0), bev=0.012, parent=T)
    K.cyl("spare", 0.36, 0.24, _rubber(), loc=(-1.75, 0.35, 0.51), rot=(0, -12, 0), segs=18, bev=0.05, parent=B)
    K.cyl("spare_rim", 0.2, 0.26, rim, loc=(-1.75, 0.35, 0.51), rot=(0, -12, 0), segs=12, bev=0.01, parent=B)
    for k in range(3):
        plank(B, f"plank{k}", (2.3, 0.18, 0.04), (-1.25, -0.5 + k * 0.2, 0.42 + k * 0.04 + (0.08 if k == 2 else 0)), rot=(0, -3, rnd.uniform(-4, 4)), shade=k)
    K.box("bumper_f", (0.14, 1.9, 0.16), steel, loc=(2.5, 0, 0.1), bev=0.02, parent=B)
    K.box("grille", (0.02, 1.0, 0.26), dark, loc=(2.455, 0, 0.45), bev=0.005, parent=B)
    K.box("bumper_r", (0.12, 1.9, 0.14), dark, loc=(-2.52, 0, 0.0), bev=0.02, parent=B)
    rusty(B, rnd, 5, (-2.3, 2.2), (0.05, 0.4), W / 2)
    rusty(B, rnd, 3, (1.0, 2.3), (0.05, 0.35), -W / 2)
    rust_patch(B, (1.7, -0.3, 0.775), (0.6, 0.5, 0.006), rot=(0, -4, 25))
    rust_patch(B, (0.0, 0.2, 1.435), (0.4, 0.5, 0.006), rot=(0, 0, -20))
    return P


def sports_car_wreck(seed=43):
    """A low colony two-seater in mustard with graphite stripes, nose down on a bare brake disc where its front wheel came
    off, the wheel lying by it in the grass."""
    rnd = random.Random(seed)
    P = _root("sportscar")
    paint = _paint(MUSTARD, wear=0.8, grime=0.6)
    dark, glass, lens = _dark(), _glass(), _lens()
    rw, W = 0.31, 1.82
    rim = M(GRAPHITE, metal=0.5, wear=0.25, grime=0.5)
    for x, s, f in ((1.35, 1, 0.12), (-1.3, -1, 0.04), (-1.3, 1, 0.0)):
        tyre(P, f"wheel{x}{s}", (x, s * 0.8, rw * (1 - f)), rw, 0.26, flat=f, rim=rim)
    B = _grp(P, "body", (0, 0, 0.27), (3.0, 2.2, 0))
    K.cyl("disc", 0.17, 0.08, _rust(), loc=(1.35, -0.72, 0.0), rot=(90, 0, 0), segs=14, bev=0.01, parent=B)
    K.cyl("hub", 0.08, 0.18, _rust(), loc=(1.35, -0.72, 0.0), rot=(90, 0, 0), segs=8, bev=0, parent=B)
    body = [(-2.08, 0.42), (-1.92, 0.58), (-0.85, 0.86), (-0.3, 0.86), (0.42, 0.52), (1.2, 0.38), (2.12, 0.2), (2.16, 0.02), (2.0, -0.12), (-2.0, -0.12),
            (-2.1, 0.0)]
    profile(B, "shell", body, W, paint, bev=0.05)
    top = [(2.12, 0.2), (1.2, 0.38), (0.42, 0.52)]
    for y in (-0.2, 0.2):
        along(B, f"stripe{y}", top, y, 0.16, 0.01, _dark())
        along(B, f"stripe_r{y}", [(-0.85, 0.86), (-1.92, 0.58), (-2.08, 0.42)], y, 0.16, 0.01, _dark())
    pane(B, "windscreen", (0.42, 0.52), (-0.3, 0.86), W - 0.3, 0, glass)
    cracks(B, (0.06, 0.25, 0.7), (0, math.degrees(math.atan2(-0.72, 0.34)), 0), n=4, size=0.55)
    pane(B, "rear_glass", (-0.85, 0.86), (-1.6, 0.66), W - 0.5, 0, glass)
    K.box("spoiler", (0.28, 1.7, 0.04), dark, loc=(-1.98, 0, 0.74), rot=(0, 6, 0), bev=0.01, parent=B)
    for s in (-1, 1):
        y = s * (W / 2 + 0.006)
        window(B, f"side_glass{s}", [(-1.4, 0.6), (0.3, 0.55), (-0.3, 0.8), (-0.85, 0.8)], y, glass)
        K.box(f"door_seam{s}", (0.012, 0.012, 0.62), dark, loc=(0.32, y, 0.22), bev=0, parent=B)
        K.box(f"intake{s}", (0.4, 0.02, 0.16), dark, loc=(-0.85, y, 0.3), bev=0.005, parent=B)
        K.box(f"strut{s}", (0.05, 0.05, 0.16), dark, loc=(-1.95, s * 0.55, 0.66), bev=0, parent=B)
        K.box(f"headlight{s}", (0.36, 0.3, 0.02), lens, loc=(1.86, s * 0.58, 0.262), rot=(0, -11, 0), bev=0.005, parent=B)
        K.box(f"taillight{s}", (0.02, 0.4, 0.1), M(SIGNAL, wear=0.2), loc=(-2.09, s * 0.55, 0.3), bev=0.005, parent=B)
        arch(B, f"arch_f{s}", 1.35, y, 0.0, 0.37, dark)
        arch(B, f"arch_r{s}", -1.3, y, 0.0, 0.37, dark)
        K.box(f"mirror{s}", (0.12, 0.1, 0.08), paint, loc=(0.2, s * (W / 2 + 0.06), 0.6), bev=0.015, parent=B)
    K.box("lip", (0.12, 1.86, 0.06), dark, loc=(2.08, 0, -0.1), rot=(0, 10, 0), bev=0.01, parent=B)
    K.box("vent", (0.5, 0.6, 0.02), dark, loc=(0.9, 0, 0.45), rot=(0, -10, 0), bev=0.005, parent=B)
    rusty(B, rnd, 4, (-1.9, 1.9), (-0.05, 0.25), W / 2)
    rusty(B, rnd, 4, (-1.9, 1.9), (-0.05, 0.25), -W / 2)
    rust_patch(B, (-0.55, 0.15, 0.865), (0.4, 0.5, 0.006), rot=(0, 0, 30))
    lying_wheel(P, "wheel_off", (2.75, -1.25, 0), rw, 0.26, tilt=(6, 0), rim=rim)
    return P


def delivery_truck_wreck(seed=44):
    """A colony box truck: steel-blue cab with a short nose, a framed bone box with its roll-up door stuck half open and
    the last of the load spilled on the ground behind it."""
    rnd = random.Random(seed)
    P = _root("boxtruck")
    cabp = _paint(STEEL, wear=0.85, grime=0.65)
    bone = _paint(BONE, wear=0.7, grime=0.6)
    steel, dark, glass, lens = _thin(STEEL), _dark(), _glass(), _lens()
    rw = 0.48
    rim = M(BONE, metal=0.3, wear=0.25, grime=0.6)
    for x, tw, flats in ((2.6, 0.3, (0.25, 0.05)), (-1.7, 0.48, (0.06, 0.06))):
        for s, f in zip((-1, 1), flats):
            tyre(P, f"wheel{x}{s}", (x, s * (1.0 if x > 0 else 0.92), rw * (1 - f)), rw, tw, flat=f, rim=rim)
        K.cyl(f"axle{x}", 0.06, 1.8, dark, loc=(x, 0, 0.45), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    B = _grp(P, "body", (0, 0, 0), (2.0, 0.8, 0))
    for s in (-1, 1):
        K.box(f"chassis{s}", (6.8, 0.14, 0.24), dark, loc=(-0.1, s * 0.5, 0.82), bev=0.02, parent=B)
    # the cab
    C = _grp(B, "cab", (2.6, 0, 0.0), (0, 1.2, 0))
    cab = [(-0.95, 0.62), (-0.95, 2.6), (-0.85, 2.7), (0.42, 2.7), (0.62, 1.62), (1.15, 1.5), (1.28, 1.36), (1.28, 0.62)]
    profile(C, "shell", cab, 2.2, cabp, bev=0.05)
    pane(C, "windscreen", (0.62, 1.66), (0.42, 2.66), 1.96, 0, glass)
    cracks(C, (0.54, 0.4, 2.2), (0, math.degrees(math.atan2(-0.2, 1.04)), 0), n=3, size=0.5)
    for s in (-1, 1):
        y = s * 1.106
        window(C, f"door_glass{s}", [(-0.6, 1.72), (0.5, 1.72), (0.36, 2.52), (-0.6, 2.52)], y, glass)
        K.box(f"door_seam{s}", (0.015, 0.012, 1.85), dark, loc=(-0.72, y, 1.6), bev=0, parent=C)
        K.box(f"step{s}", (0.5, 0.25, 0.05), dark, loc=(-0.1, s * 1.15, 0.55), bev=0.01, parent=C)
        K.box(f"headlight{s}", (0.02, 0.32, 0.18), lens, loc=(1.285, s * 0.72, 1.1), bev=0.005, parent=C)
        arch(C, f"arch{s}", 0.0, y, 0.45, 0.56, dark)
        K.box(f"mirror{s}", (0.06, 0.16, 0.36), dark, loc=(0.6, s * 1.3, 2.05), bev=0.012, parent=C)
        K.box(f"mirror_arm{s}", (0.04, 0.2, 0.04), dark, loc=(0.6, s * 1.18, 2.2), bev=0, parent=C)
    K.box("grille", (0.03, 1.2, 0.5), dark, loc=(1.29, 0, 1.0), bev=0.008, parent=C)
    for k in range(4):
        K.box(f"grille_bar{k}", (0.03, 1.14, 0.03), steel, loc=(1.3, 0, 0.84 + k * 0.11), bev=0, parent=C)
    K.box("bumper", (0.16, 2.3, 0.22), dark, loc=(1.36, 0, 0.62), rot=(0, 0, 4), bev=0.02, parent=C)
    label(C, "CW-31", (-0.2, -1.115, 1.25), 0.17, M(BONE, wear=0.3))
    rusty(C, rnd, 3, (-0.8, 1.1), (0.7, 1.3), -1.1)
    rust_patch(C, (-0.2, 0.3, 2.705), (0.6, 0.8, 0.006), rot=(0, 0, 20))
    # the box: framed bone panels with posts down the sides, a dent in the roof
    bx, bl, bw, bh, bz = -1.0, 4.8, 2.36, 2.3, 1.05
    FR.framed_box(B, "box", (bx, 0, bz + bh / 2), (bl, bw, bh), bone, steel, fw=0.12)
    for s in (-1, 1):
        for k in range(1, 4):
            K.box(f"post{s}{k}", (0.1, 0.06, bh - 0.12), steel, loc=(bx - bl / 2 + k * bl / 4, s * (bw / 2 - 0.01), bz + bh / 2), bev=0.01, parent=B)
        K.box(f"band{s}", (bl - 0.1, 0.012, 0.18), M(MUSTARD, wear=0.5), loc=(bx, s * (bw / 2 - 0.035), bz + 0.24), bev=0, parent=B)
        rusty(B, rnd, 3, (bx - 2.2, bx + 2.2), (bz + 0.4, bz + 1.6), s * (bw / 2 - 0.035))
    label(B, "CW-31", (bx - 0.4, -bw / 2 + 0.03, bz + 1.5), 0.3, M(STEEL, wear=0.3))
    K.box("dent", (1.4, 1.6, 0.05), M(RUST, metal=0.3, wear=0.2, grime=0.5), loc=(bx - 0.5, 0.1, bz + bh - 0.01), rot=(0, 3, 0), bev=0.01, parent=B)
    # the roll-up door stuck a third of the way up, the dark load space under it
    xr = bx - bl / 2 - 0.006
    K.box("load_dark", (0.012, bw - 0.3, 0.8), K.mat("shadow", "#15171a", rough=0.9), loc=(xr, 0, bz + 0.47), bev=0, parent=B)
    for k in range(9):
        K.box(f"slat{k}", (0.03, bw - 0.3, 0.135), M(ZINC, metal=0.5, wear=0.25, grime=0.6), loc=(xr - 0.01, 0, bz + 0.94 + k * 0.14), bev=0, parent=B)
    K.box("door_handle", (0.04, 0.3, 0.05), dark, loc=(xr - 0.035, 0, bz + 0.95), bev=0.005, parent=B)
    FR.hazard(B, (xr - 0.01, 0, bz - 0.06), 2.0, 0.14, M(MUSTARD), dark, n=6, axis="x")
    for s in (-1, 1):
        K.box(f"taillight{s}", (0.03, 0.2, 0.12), M(SIGNAL, wear=0.2), loc=(xr - 0.01, s * 0.95, bz - 0.12), bev=0.005, parent=B)
    # the load spilled behind
    card = K.mat("cardboard", "#b08d5a", rough=0.95, grime=0.4, noise_scale=4.0)
    for k, (x, y, z, sz, rot) in enumerate(((-3.7, 0.3, 0.2, 0.4, (0, 0, 20)), (-4.1, -0.4, 0.17, 0.34, (0, 0, -35)), (-3.6, 0.15, 0.6, 0.38, (8, -12, 40)),
                                            (-4.5, 0.6, 0.11, 0.22, (0, 70, 10)))):
        K.box(f"box{k}", (sz * 1.2, sz, sz), card, loc=(x, y, z), rot=rot, bev=0.012, parent=P)
    return P


def tanker_trailer_wreck(seed=45):
    """A colony tank trailer left without its cab: a bone tank on steel saddles, a catwalk and a ladder, its landing legs
    buckled at one side so it leans, rust running down from every band."""
    rnd = random.Random(seed)
    P = _root("tanker")
    bone = _paint(BONE, wear=0.7, grime=0.8, moss=0.3)
    steel, dark = _thin(STEEL), _dark()
    rw = 0.5
    rim = M(STEEL, metal=0.4, wear=0.25, grime=0.6)
    for x in (-2.4, -3.5):
        for s, f in ((-1, 0.2 if x < -3 else 0.06), (1, 0.05)):
            tyre(P, f"wheel{x}{s}", (x, s * 0.92, rw * (1 - f)), rw, 0.5, flat=f, rim=rim)
        K.cyl(f"axle{x}", 0.07, 1.9, dark, loc=(x, 0, 0.47), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    T = _grp(P, "trailer", (0, 0, 0), (1.6, 1.4, 0))
    for s in (-1, 1):
        K.box(f"rail{s}", (8.0, 0.14, 0.26), dark, loc=(0, s * 0.5, 1.08), bev=0.02, parent=T)
        shell_panel(f"fender{s}", 0.62, math.radians(12), math.radians(168), s * 0.66, s * 1.2, M(GRAPHITE, metal=0.3, wear=0.25), t=0.04, parent=T,
                    loc=(-2.95, 0, 0.5))
        shell_panel(f"fender2{s}", 0.62, math.radians(12), math.radians(168), s * 0.66, s * 1.2, M(GRAPHITE, metal=0.3, wear=0.25), t=0.04, parent=T,
                    loc=(-2.4, 0, 0.5))
    for x in (-3.2, -1.6, 0.0, 1.6, 3.2):
        K.box(f"saddle{x}", (0.3, 1.5, 0.5), steel, loc=(x, 0, 1.3), bev=0.02, parent=T)
    zt, R, L = 2.3, 1.0, 7.2
    K.cyl("tank", R, L, bone, loc=(0, 0, zt), rot=(0, 90, 0), segs=28, bev=0.02, parent=T)
    for s in (-1, 1):
        K.sphere(f"tank_end{s}", R, bone, loc=(s * L / 2, 0, zt), scale=(0.3, 1, 1), parent=T)
    G = _grp(T, "wrap", (0, 0, zt), (0, 0, 90))   # shell_panel bends round local y: turned so it wraps the tank along x
    for k, x in enumerate((-3.0, -1.2, 0.6, 2.4)):
        K.cyl(f"band{k}", R + 0.02, 0.08, steel, loc=(x, 0, zt), rot=(0, 90, 0), segs=24, bev=0, parent=T)
        for j in range(2):
            # rust run down from the band on the near side, and a bloom on the far side
            a = math.radians(rnd.uniform(185, 235) if j else rnd.uniform(-60, -10))
            shell_panel(f"streak{k}{j}", R + 0.008, a - 0.35, a + 0.12, -x - 0.6, -x - 0.05, _rust(), t=0.006, parent=G)
    shell_panel("stripe", R + 0.004, math.radians(166), math.radians(178), -3.3, 3.3, M(MUSTARD, wear=0.4, grime=0.5), t=0.006, parent=G)
    shell_panel("dent", R + 0.006, math.radians(60), math.radians(110), -2.2, -1.3, M(RUST, metal=0.3, wear=0.2, grime=0.6), t=0.006, parent=G)
    label(T, "CW-T7", (0.3, -R - 0.012, zt + 0.0), 0.3, M(STEEL, wear=0.3))
    # top: two manholes and a catwalk with a rail
    for x in (-1.8, 1.8):
        K.cyl(f"manhole{x}", 0.3, 0.16, steel, loc=(x, 0, zt + R + 0.04), segs=16, bev=0.02, parent=T)
        K.cyl(f"lid{x}", 0.26, 0.05, dark, loc=(x, 0, zt + R + 0.13), segs=16, bev=0.01, parent=T)
    K.box("catwalk", (5.4, 0.42, 0.04), M(ZINC, metal=0.5, wear=0.25, grime=0.5), loc=(0, 0.32, zt + R + 0.02), bev=0.005, parent=T)
    for k in range(7):
        rod(T, f"stanchion{k}", (-2.7 + k * 0.9, 0.52, zt + R - 0.02), (-2.7 + k * 0.9, 0.52, zt + R + 0.9), 0.02, steel)
    rod(T, "handrail", (-2.7, 0.52, zt + R + 0.9), (2.7, 0.52, zt + R + 0.9), 0.022, steel)
    rod(T, "handrail_lo", (-2.7, 0.52, zt + R + 0.45), (2.7, 0.52, zt + R + 0.45), 0.018, steel)
    # ladder up the back
    for s in (-1, 1):
        rod(T, f"ladder{s}", (-L / 2 - 0.35, s * 0.22, 1.0), (-L / 2 - 0.05, s * 0.22, zt + R + 0.2), 0.022, steel)
    for k in range(8):
        t = (k + 0.5) / 8
        rod(T, f"rung{k}", (-L / 2 - 0.35 + 0.3 * t, -0.22, 1.0 + t * (zt + R - 0.8)), (-L / 2 - 0.35 + 0.3 * t, 0.22, 1.0 + t * (zt + R - 0.8)), 0.016, steel)
    # outlet pipe and valve under the middle
    rod(T, "outlet", (0.4, 0, zt - R), (0.4, -1.05, 1.05), 0.07, dark)
    K.cyl("valve", 0.12, 0.12, M(SIGNAL, wear=0.3), loc=(0.4, -1.05, 1.05), rot=(90, 0, 0), segs=12, bev=0.01, parent=T)
    # front: kingpin plate and the landing legs, the near one buckled
    K.box("kingpin_plate", (1.2, 1.3, 0.06), dark, loc=(3.4, 0, 1.0), bev=0.01, parent=T)
    K.cyl("kingpin", 0.05, 0.12, dark, loc=(3.6, 0, 0.92), segs=8, bev=0, parent=T)
    for s in (-1, 1):
        x, y = 2.6, s * 0.75
        K.box(f"leg_outer{s}", (0.16, 0.16, 0.6), steel, loc=(x, y, 0.76), bev=0.015, parent=T)
        if s > 0:
            K.box(f"leg_inner{s}", (0.12, 0.12, 0.5), dark, loc=(x, y, 0.27), bev=0.01, parent=T)
            K.box(f"shoe{s}", (0.3, 0.3, 0.05), dark, loc=(x, y, 0.035), bev=0.01, parent=T)
        else:
            K.box("leg_bent", (0.12, 0.12, 0.45), dark, loc=(x + 0.12, y - 0.1, 0.3), rot=(25, 35, 0), bev=0.01, parent=T)
            K.box("shoe_off", (0.3, 0.3, 0.05), dark, loc=(x + 0.35, y - 0.45, 0.03), rot=(0, 0, 25), bev=0.01, parent=P)
    K.box("leg_brace", (0.08, 1.5, 0.08), dark, loc=(2.6, 0, 0.75), bev=0.01, parent=T)
    K.cyl("crank", 0.03, 0.4, dark, loc=(2.6, 0.95, 0.85), rot=(90, 0, 0), segs=6, bev=0, parent=T)
    # rear bumper and lights
    K.box("bumper", (0.12, 2.3, 0.14), dark, loc=(-4.0, 0, 0.6), bev=0.015, parent=T)
    FR.hazard(T, (-4.07, 0, 0.6), 2.2, 0.12, M(MUSTARD), dark, n=6, axis="x")
    for s in (-1, 1):
        K.box(f"tail{s}", (0.04, 0.2, 0.1), M(SIGNAL, wear=0.2), loc=(-4.0, s * 0.95, 0.8), bev=0.005, parent=T)
    return P


def flatbed_trailer(seed=46):
    """An empty colony flatbed trailer standing on its landing legs: a plank deck in a steel-blue frame with stake
    pockets, tandem axles at the back, one plank gone and another sprung."""
    rnd = random.Random(seed)
    P = _root("flatbed")
    steel = M(STEEL, metal=0.4, wear=0.3, grime=0.6)
    dark = _dark()
    rw, L, W, zd = 0.5, 8.0, 2.45, 1.3
    rim = M(STEEL, metal=0.4, wear=0.25, grime=0.6)
    for x in (-2.4, -3.5):
        for s in (-1, 1):
            tyre(P, f"wheel{x}{s}", (x, s * 0.92, rw * (1 - 0.06)), rw, 0.5, flat=0.06, rim=rim)
        K.cyl(f"axle{x}", 0.07, 1.9, dark, loc=(x, 0, 0.47), rot=(90, 0, 0), segs=10, bev=0, parent=P)
        K.box(f"hanger{x}", (0.3, 1.4, 0.4), dark, loc=(x, 0, 0.75), bev=0.02, parent=P)
    for s in (-1, 1):
        K.box(f"beam{s}", (L, 0.12, 0.36), dark, loc=(0, s * 0.5, zd - 0.32), bev=0.015, parent=P)
        K.box(f"side_rail{s}", (L, 0.12, 0.2), steel, loc=(0, s * (W / 2 - 0.06), zd - 0.06), bev=0.015, parent=P)
        for k in range(8):
            K.box(f"pocket{s}{k}", (0.1, 0.06, 0.16), dark, loc=(-3.5 + k * 1.0, s * (W / 2 + 0.02), zd - 0.08), bev=0, parent=P)
        rusty(P, rnd, 4, (-3.6, 3.6), (zd - 0.08, zd - 0.04), s * (W / 2 + 0.002), thin=0.004)
        shell_panel(f"fender{s}", 0.62, math.radians(12), math.radians(168), s * 0.66, s * 1.2, M(GRAPHITE, metal=0.3, wear=0.25), t=0.04, parent=P,
                    loc=(-2.95, 0, 0.5))
        K.box(f"mudflap{s}", (0.02, 0.5, 0.55), _rubber(), loc=(-4.05, s * 0.92, 0.5), bev=0.005, parent=P)
    for k in range(11):
        K.box(f"cross{k}", (0.1, W - 0.2, 0.14), dark, loc=(-3.8 + k * 0.76, 0, zd - 0.2), bev=0, parent=P)
    n = 8
    pw = (W - 0.24) / n
    for k in range(n):
        y = -W / 2 + 0.12 + pw * (k + 0.5)
        if k == 2:
            # a plank gone over the back third and the rest of it sprung up off the crossmembers
            plank(P, "plank_sprung", (4.6, pw - 0.02, 0.05), (1.6, y, zd + 0.11), rot=(0, -3.5, 0.6), shade=k)
            continue
        if k == 5:
            plank(P, "plank_short", (L - 2.8, pw - 0.02, 0.05), (1.4, y, zd - 0.005), shade=k)
            continue
        plank(P, f"plank{k}", (L - 0.1, pw - 0.02, 0.05), (0, y, zd - 0.005), shade=k)
    K.box("headboard", (0.1, W, 0.22), steel, loc=(L / 2 - 0.05, 0, zd + 0.08), bev=0.015, parent=P)
    K.box("tail", (0.1, W, 0.2), steel, loc=(-L / 2 + 0.05, 0, zd - 0.06), bev=0.015, parent=P)
    K.box("bumper", (0.12, 2.2, 0.14), dark, loc=(-L / 2 + 0.05, 0, 0.62), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box(f"bumper_post{s}", (0.08, 0.08, 0.6), dark, loc=(-L / 2 + 0.05, s * 0.8, 0.92), bev=0, parent=P)
        K.box(f"tail_lamp{s}", (0.04, 0.2, 0.1), M(SIGNAL, wear=0.2), loc=(-L / 2, s * 1.0, zd - 0.08), bev=0.005, parent=P)
    FR.hazard(P, (-L / 2 - 0.02, 0, 0.62), 2.1, 0.12, M(MUSTARD), dark, n=8, axis="x")
    # front: kingpin plate and the landing legs on their sand shoes
    K.box("kingpin_plate", (1.2, 1.3, 0.06), dark, loc=(3.4, 0, zd - 0.53), bev=0.01, parent=P)
    for s in (-1, 1):
        K.box(f"leg_outer{s}", (0.16, 0.16, 0.5), steel, loc=(2.4, s * 0.75, zd - 0.6), bev=0.015, parent=P)
        K.box(f"leg_inner{s}", (0.12, 0.12, 0.5), dark, loc=(2.4, s * 0.75, 0.31), bev=0.01, parent=P)
        K.box(f"shoe{s}", (0.3, 0.3, 0.05), dark, loc=(2.4, s * 0.75, 0.04), bev=0.01, parent=P)
        K.box(f"leg_strut{s}", (0.06, 0.06, 0.7), dark, loc=(2.75, s * 0.75, zd - 0.6), rot=(0, -35, 0), bev=0, parent=P)
    K.box("leg_brace", (0.08, 1.5, 0.08), dark, loc=(2.4, 0, 0.55), bev=0.01, parent=P)
    K.cyl("crank", 0.03, 0.42, dark, loc=(2.4, -0.98, zd - 0.6), rot=(90, 0, 0), segs=6, bev=0, parent=P)
    K.box("crank_handle", (0.03, 0.03, 0.24), dark, loc=(2.4, -1.2, zd - 0.7), bev=0, parent=P)
    label(P, "CW-F2", (2.6, -W / 2 - 0.004, zd - 0.06), 0.13, M(BONE, wear=0.3))
    return P


def tub(name, top, bot, h, t, mat, parent=None, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.01):
    """An open-topped tapered box with walls t thick: a hopper, a bin, a grain tank. top and bot are (x, y) sizes."""
    bm = bmesh.new()

    def ring(w, d, z):
        return [bm.verts.new((sx * w / 2, sy * d / 2, z)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    ob_, ot = ring(*bot, 0.0), ring(*top, h)
    ib, it = ring(bot[0] - 2 * t, bot[1] - 2 * t, t), ring(top[0] - 2 * t, top[1] - 2 * t, h)
    bm.faces.new(list(reversed(ob_)))
    bm.faces.new(ib)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((ob_[i], ob_[j], ot[j], ot[i]))
        bm.faces.new((ot[i], ot[j], it[j], it[i]))
        bm.faces.new((it[i], it[j], ib[j], ib[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev, segs=2)
    return K.place(ob, loc, rot, parent)


def motorbike_wreck(seed=47):
    """A colony trail motorbike down on its side in the grass: mustard tank and side panels, handlebar twisted round, the
    front mudguard torn off and lying by it."""
    rnd = random.Random(seed)
    P = _root("motorbike")
    paint = _paint(MUSTARD, wear=0.5, grime=0.6)
    dark, steel = _dark(), _thin(STEEL)
    chrome = M(ZINC, metal=0.7, wear=0.2, grime=0.5, rough=0.35)
    rim = M(BONE, metal=0.3, wear=0.2, grime=0.5)
    Y = _grp(P, "yaw", (0.05, 0.15, 0), (0, 0, -18))
    G = _grp(Y, "bike", (0, 0, 0), (69, 0, 0))   # rolled onto its left side about the line where the tyres touch
    r = 0.31
    tyre(G, "front", (0.72, 0, r), r, 0.11, rim=rim)
    tyre(G, "rear", (-0.68, 0, r), r, 0.13, rim=rim)
    for s in (-1, 1):
        rod(G, f"fork{s}", (0.46, s * 0.09, 0.98), (0.72, s * 0.09, r), 0.025, chrome)
        rod(G, f"swingarm{s}", (-0.25, s * 0.1, 0.36), (-0.68, s * 0.1, r), 0.025, dark)
        rod(G, f"shock{s}", (-0.58, s * 0.12, 0.36), (-0.36, s * 0.12, 0.74), 0.03, M(SIGNAL, metal=0.3, wear=0.2))
    rod(G, "backbone", (0.45, 0, 0.92), (-0.25, 0, 0.78), 0.035, dark)
    rod(G, "downtube", (0.45, 0, 0.9), (0.12, 0, 0.3), 0.032, dark)
    rod(G, "cradle", (0.12, 0, 0.3), (-0.28, 0, 0.32), 0.03, dark)
    rod(G, "seatpost", (-0.28, 0, 0.32), (-0.25, 0, 0.8), 0.03, dark)
    rod(G, "subframe", (-0.25, 0, 0.8), (-0.75, 0, 0.82), 0.025, dark)
    K.cyl("headstock", 0.05, 0.2, dark, loc=(0.46, 0, 0.95), rot=(0, -25, 0), segs=10, bev=0, parent=G)
    K.box("tank", (0.5, 0.3, 0.2), paint, loc=(0.16, 0, 0.94), rot=(0, -8, 0), bev=0.06, parent=G)
    K.box("tank_stripe", (0.3, 0.31, 0.04), dark, loc=(0.16, 0, 0.95), rot=(0, -8, 0), bev=0.01, parent=G)
    K.cyl("tank_cap", 0.04, 0.03, chrome, loc=(0.25, 0, 1.05), segs=10, bev=0, parent=G)
    K.box("seat", (0.62, 0.25, 0.09), _cloth(), loc=(-0.38, 0, 0.88), rot=(0, 3, 0), bev=0.035, parent=G)
    K.box("side_panel", (0.3, 0.27, 0.22), paint, loc=(-0.3, 0, 0.66), bev=0.03, parent=G)
    K.box("tail", (0.25, 0.14, 0.08), paint, loc=(-0.78, 0, 0.84), rot=(0, 8, 0), bev=0.02, parent=G)
    K.box("tail_lamp", (0.04, 0.1, 0.05), M(SIGNAL, wear=0.2), loc=(-0.91, 0, 0.84), bev=0.008, parent=G)
    shell_panel("rear_guard", r + 0.06, math.radians(50), math.radians(165), -0.06, 0.06, paint, t=0.012, parent=G, loc=(-0.68, 0, r))
    # engine: crankcase, finned barrel and head, the exhaust round the right side to the silencer
    K.box("crankcase", (0.36, 0.26, 0.24), M(GUNMETAL, metal=0.5, wear=0.25, grime=0.6), loc=(-0.05, 0, 0.42), bev=0.04, parent=G)
    for k in range(4):
        K.box(f"fin{k}", (0.2, 0.22, 0.02), M(GUNMETAL, metal=0.5, wear=0.25), loc=(0.07, 0, 0.56 + k * 0.045), rot=(0, -15, 0), bev=0, parent=G)
    K.box("head", (0.18, 0.18, 0.08), dark, loc=(0.11, 0, 0.75), rot=(0, -15, 0), bev=0.015, parent=G)
    rod(G, "exhaust0", (0.17, 0.08, 0.66), (0.22, 0.14, 0.42), 0.025, _rust())
    rod(G, "exhaust1", (0.22, 0.14, 0.42), (-0.25, 0.17, 0.3), 0.025, _rust())
    rod(G, "exhaust2", (-0.25, 0.17, 0.3), (-0.55, 0.18, 0.52), 0.025, _rust())
    K.cyl("silencer", 0.055, 0.45, chrome, loc=(-0.7, 0.18, 0.58), rot=(0, 72, 0), segs=12, bev=0.01, parent=G)
    for s in (-1, 1):
        K.box(f"peg{s}", (0.05, 0.12, 0.03), dark, loc=(-0.12, s * 0.17, 0.33), bev=0, parent=G)
    # the bars twisted round on the head stock, the headlamp hanging off
    H = _grp(G, "bars", (0.44, 0, 1.0), (0, 0, 24))
    rod(H, "handlebar", (0.0, -0.36, 0.02), (0.0, 0.36, 0.02), 0.014, chrome)
    for s in (-1, 1):
        K.cyl(f"grip{s}", 0.022, 0.12, _rubber(), loc=(0.0, s * 0.31, 0.02), rot=(90, 0, 0), segs=8, bev=0, parent=H)
        K.box(f"lever{s}", (0.14, 0.015, 0.012), dark, loc=(0.07, s * 0.24, 0.0), rot=(0, 0, s * 20), bev=0, parent=H)
    K.box("clamp", (0.08, 0.2, 0.05), dark, loc=(0.0, 0, -0.02), bev=0.01, parent=H)
    K.cyl("headlamp", 0.08, 0.1, dark, loc=(0.62, -0.05, 0.84), rot=(0, 70, 20), segs=12, bev=0.01, parent=G)
    K.cyl("headlamp_lens", 0.065, 0.02, _lens(), loc=(0.665, -0.035, 0.825), rot=(0, 70, 20), segs=12, bev=0, parent=G)
    # the front mudguard off in the grass
    F = _grp(P, "guard_off", (1.15, -0.85, 0.0), (0, 0, 60))
    shell_panel("front_guard", r + 0.05, math.radians(10), math.radians(120), -0.06, 0.06, paint, t=0.012, parent=F, loc=(0, 0, -0.06))
    rust_patch(G, (0.16, -0.152, 0.92), (0.16, 0.004, 0.08))
    for k in range(4):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-0.8, 0.9), rnd.uniform(-0.6, 0.4)), rnd, h=0.25)
    return P


def bicycle_rusty(seed=48):
    """An old colony bicycle lying in the grass, rusted to the colour of the soil: thin steel frame, spoked wheels, a rack
    over the back wheel and a split saddle."""
    rnd = random.Random(seed)
    P = _root("bicycle")
    rust = M(RUST, metal=0.4, wear=0.15, grime=0.4, rough=0.85)
    paint = M(STEEL, metal=0.3, wear=0.15, grime=0.7)
    rubber, dark = _rubber(), _dark()
    Y = _grp(P, "yaw", (-0.08, 0.1, 0), (0, 0, 14))
    G = _grp(Y, "bike", (0, 0, 0), (75, 0, 0))
    R = 0.34
    for nm, x in (("front", 0.62), ("rear", -0.42)):
        K.torus(f"{nm}_tyre", R - 0.018, 0.018, rubber, loc=(x, 0, R), rot=(90, 0, 0), segs=28, parent=G)
        K.torus(f"{nm}_rim", R - 0.04, 0.01, rust, loc=(x, 0, R), rot=(90, 0, 0), segs=24, parent=G)
        K.cyl(f"{nm}_hub", 0.025, 0.1, rust, loc=(x, 0, R), rot=(90, 0, 0), segs=8, bev=0, parent=G)
        for k in range(12):
            a = 2 * math.pi * k / 12
            s = 1 if k % 2 else -1
            rod(G, f"{nm}_spoke{k}", (x, s * 0.035, R), (x + math.cos(a) * (R - 0.05), 0, R + math.sin(a) * (R - 0.05)), 0.003, M(ZINC, metal=0.5, wear=0.1), segs=3)
    bb, seat_top, head_top, head_bot = (0.0, 0, 0.3), (-0.15, 0, 0.84), (0.52, 0, 0.88), (0.55, 0, 0.74)
    rod(G, "seat_tube", bb, seat_top, 0.016, rust)
    rod(G, "top_tube", (-0.14, 0, 0.8), (0.52, 0, 0.84), 0.016, paint)
    rod(G, "down_tube", bb, head_bot, 0.019, paint)
    rod(G, "head_tube", head_bot, head_top, 0.02, rust)
    for s in (-1, 1):
        rod(G, f"chainstay{s}", bb, (-0.42, s * 0.05, R), 0.011, rust)
        rod(G, f"seatstay{s}", (-0.15, 0, 0.8), (-0.42, s * 0.05, R), 0.01, rust)
        rod(G, f"fork{s}", (0.55, s * 0.03, 0.74), (0.62, s * 0.045, R), 0.012, rust)
        rod(G, f"rack_strut{s}", (-0.42, s * 0.06, R), (-0.5, s * 0.1, 0.72), 0.008, rust)
        rod(G, f"rack_rail{s}", (-0.72, s * 0.1, 0.72), (-0.2, s * 0.1, 0.72), 0.008, rust)
    for k in range(3):
        rod(G, f"rack_bar{k}", (-0.68 + k * 0.2, -0.1, 0.72), (-0.68 + k * 0.2, 0.1, 0.72), 0.007, rust)
    rod(G, "seatpost", seat_top, (-0.17, 0, 0.95), 0.012, rust)
    K.box("saddle", (0.27, 0.14, 0.06), K.mat("leather", "#3a2c24", rough=0.85, grime=0.4), loc=(-0.15, 0, 0.98), bev=0.02, parent=G)
    K.box("saddle_split", (0.12, 0.02, 0.015), K.mat("foam", FOAM, rough=0.95), loc=(-0.13, 0.02, 1.012), bev=0, parent=G)
    rod(G, "stem", head_top, (0.5, 0, 0.99), 0.012, rust)
    rod(G, "bars", (0.5, -0.27, 1.0), (0.5, 0.27, 1.0), 0.011, rust)
    for s in (-1, 1):
        K.cyl(f"grip{s}", 0.017, 0.1, rubber, loc=(0.5, s * 0.23, 1.0), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    # drive: a chainring on the right, cranks set upright so the left pedal clears the grass
    K.cyl("chainring", 0.09, 0.008, rust, loc=(0, 0.06, 0.3), rot=(90, 0, 0), segs=16, bev=0, parent=G)
    K.cyl("cog", 0.04, 0.008, rust, loc=(-0.42, 0.06, R), rot=(90, 0, 0), segs=10, bev=0, parent=G)
    for s, dz in ((1, -0.16), (-1, 0.16)):
        rod(G, f"crank{s}", (0, s * 0.07, 0.3), (0, s * 0.08, 0.3 + dz), 0.01, rust)
        K.box(f"pedal{s}", (0.07, 0.09, 0.025), dark, loc=(0, s * 0.12, 0.3 + dz), bev=0.005, parent=G)
    for z0 in (0.3 + 0.09, 0.3 - 0.09):
        rod(G, f"chain{z0}", (0, 0.06, z0), (-0.42, 0.06, R + (0.04 if z0 > 0.3 else -0.04)), 0.005, dark, segs=4)
    for k in range(5):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-0.6, 0.7), rnd.uniform(-0.4, 0.3)), rnd, h=0.22)
    return P


def train_wagon_wreck(seed=49):
    """A colony box wagon, ten metres of it, run off the end of a broken length of track: the front still on its bogie
    in the grass, the back end down where its bogie was torn out, the sliding door stuck half open."""
    rnd = random.Random(seed)
    P = _root("wagon")
    panel = _paint(STEEL, wear=0.7, grime=0.7, moss=0.25)
    rails = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.6)
    dark = _dark()
    door = _paint(MUSTARD, wear=0.6, grime=0.6)
    # the track it came off, the far rail kinked where the wagon left it
    Tg = _grp(P, "track", (2.6, -2.0, 0), (0, 0, -6))
    track(Tg, "track", -2.6, 2.6, rnd, ballast=False, skip=(5,), clips=False)
    # the wagon
    W = _grp(P, "wagon", (-0.2, 0.4, 0.44), (4.0, -4.6, 7))
    L, B, H = 10.0, 2.8, 2.6
    for s in (-1, 1):
        K.box(f"solebar{s}", (L, 0.16, 0.3), dark, loc=(0, s * 1.25, 0.15), bev=0.015, parent=W)
    for s in (-1, 1):
        K.box(f"headstock{s}", (0.2, B, 0.36), M(SIGNAL, wear=0.3, grime=0.6), loc=(s * (L / 2 - 0.1), 0, 0.18), bev=0.015, parent=W)
        for y in (-0.88, 0.88):
            K.cyl(f"buffer{s}{y}", 0.09, 0.4, dark, loc=(s * (L / 2 + 0.15), y, 0.2), rot=(0, 90, 0), segs=10, bev=0, parent=W)
            K.cyl(f"buffer_head{s}{y}", 0.2, 0.05, M(ZINC, metal=0.6, wear=0.2, grime=0.5), loc=(s * (L / 2 + 0.36), y, 0.2), rot=(0, 90, 0), segs=10, bev=0,
                  parent=W)
        K.box(f"coupler{s}", (0.4, 0.12, 0.12), dark, loc=(s * (L / 2 + 0.2), 0, 0.2), bev=0.01, parent=W)
    FR.framed_box(W, "body", (0, 0, 0.3 + H / 2), (L, B, H), panel, rails, fw=0.16)
    for s in (-1, 1):
        for k in range(1, 8):
            x = -L / 2 + k * L / 8
            if s < 0 and abs(x) < 1.3:
                continue
            K.box(f"rib{s}{k}", (0.1, 0.07, H - 0.2), rails, loc=(x, s * (B / 2 - 0.005), 0.3 + H / 2), bev=0, parent=W)
        rusty(W, rnd, 3, (-4.6, 4.6), (0.6, 2.4), s * (B / 2 - 0.035))
    # the sliding door run half back, the dark load space showing
    K.box("door_dark", (1.2, 0.012, H - 0.35), K.mat("shadow", "#15171a", rough=0.9), loc=(-0.6, -B / 2 + 0.03, 0.3 + H / 2 - 0.05), bev=0, parent=W)
    K.box("door", (2.4, 0.08, H - 0.2), door, loc=(0.6, -B / 2 - 0.05, 0.3 + H / 2), bev=0.02, parent=W)
    for k in range(3):
        K.box(f"door_rib{k}", (0.08, 0.04, H - 0.3), M(GRAPHITE, metal=0.4, wear=0.25), loc=(-0.3 + k * 0.9, -B / 2 - 0.1, 0.3 + H / 2), bev=0.005, parent=W)
    K.box("door_rail", (5.0, 0.06, 0.08), dark, loc=(0.0, -B / 2 - 0.06, 0.3 + H + 0.02), bev=0, parent=W)
    label(W, "CW-F12", (-3.0, -B / 2 - 0.004, 2.25), 0.34, M(BONE, wear=0.3))
    # arched roof and a hole torn in it
    R = 4.0
    half = math.asin((B / 2 + 0.05) / R)
    Rg = _grp(W, "roof", (0, 0, 0.3 + H + 0.02 - R * math.cos(half)), (0, 0, 90))
    shell_panel("roof", R, math.pi / 2 - half, math.pi / 2 + half, -L / 2 - 0.05, L / 2 + 0.05, M(STEEL, wear=0.25, grime=0.6, moss=0.4), t=0.05, parent=Rg)
    shell_panel("roof_rust", R + 0.004, math.pi / 2 - 0.18, math.pi / 2 + 0.25, -2.6, -1.0, _rust(), t=0.006, parent=Rg)
    shell_panel("roof_hole", R + 0.008, math.pi / 2 - 0.08, math.pi / 2 + 0.14, -2.2, -1.6, K.mat("shadow", "#15171a", rough=0.9), t=0.006, parent=Rg)
    # brake wheel and ladder at the front end
    K.torus("brake_wheel", 0.22, 0.02, dark, loc=(L / 2 + 0.06, 0.6, 1.9), rot=(0, 90, 0), segs=14, parent=W)
    for y in (-1.1, -0.75):
        K.box(f"ladder{y}", (0.04, 0.04, H), M(GRAPHITE, metal=0.4, wear=0.25), loc=(L / 2 + 0.06, y, 0.3 + H / 2), bev=0, parent=W)
    for k in range(7):
        K.box(f"ladder_rung{k}", (0.03, 0.35, 0.03), M(GRAPHITE, metal=0.4, wear=0.25), loc=(L / 2 + 0.06, -0.925, 0.55 + k * 0.36), bev=0, parent=W)
    # the front bogie still under it, the rear one torn out and lying off to the side
    bogie(P, "bogie_f", (3.2, 0.15, 0.0), (0, 0, 5), r=0.42)
    bogie(P, "bogie_r", (-5.6, 2.3, 0.0), (0, 0, 38), r=0.42)
    mound(P, "dug", (-4.9, 0.0), 1.2, 0.35, seed=seed, n=4, sx=0.6)
    return P


def rail_track_section(seed=50):
    """Eight metres of colony track: rails on concrete sleepers in a ballast bed, a sleeper gone, grass coming up
    between the rest."""
    rnd = random.Random(seed)
    P = _root("track")
    track(P, "track", -4.0, 4.0, rnd, skip=(8,))
    K.box("sleeper_broken", (0.26, 1.1, 0.16), _concrete(), loc=(1.55, 1.75, 0.08), rot=(4, 0, 72), bev=0.02, parent=P)
    for k in range(9):
        x = -3.8 + k * 0.92 + rnd.uniform(-0.1, 0.1)
        grass_tuft(P, f"grass{k}", (x, rnd.choice((-1.15, -0.2, 0.3, 1.1)) + rnd.uniform(-0.1, 0.1)), rnd, n=4, h=0.4)
    return P


def rail_signal(seed=51):
    """A colony rail signal: a graphite lattice-free mast on a concrete footing, a two-lamp head on a bone target board
    with sun hoods, dead lenses, a maintenance ladder and a relay cabinet at its foot."""
    rnd = random.Random(seed)
    P = _root("signal")
    dark = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.5)
    steel = _thin(STEEL)
    K.box("footing", (0.9, 0.9, 0.35), _concrete(), loc=(0, 0, 0.12), bev=0.03, parent=P)
    K.box("base_plate", (0.5, 0.5, 0.04), dark, loc=(0, 0, 0.31), bev=0.005, parent=P)
    for x in (-0.18, 0.18):
        for y in (-0.18, 0.18):
            K.cyl("anchor", 0.025, 0.06, M(ZINC, metal=0.6, wear=0.3), loc=(x, y, 0.34), segs=6, bev=0, parent=P)
    Hm = 5.0
    K.box("mast", (0.18, 0.18, Hm), dark, loc=(0, 0, 0.33 + Hm / 2), bev=0.02, parent=P)
    FR.hazard(P, (0, -0.1, 1.0), 0.6, 0.16, M(MUSTARD, wear=0.3), dark, n=4, axis="y")
    # head: a bone target board with a graphite case, two lenses under sun hoods
    zh = 0.33 + Hm - 0.25
    K.slab("target", 0.95, 1.5, 0.06, 0.42, M(BONE, wear=0.4, grime=0.5), loc=(0, -0.14, zh), rot=(90, 0, 0), bev=0.015, parent=P)
    K.box("case", (0.44, 0.3, 1.0), dark, loc=(0, -0.0, zh), bev=0.03, parent=P)
    for k, (z, col) in enumerate(((zh + 0.25, LAMP_RED), (zh - 0.25, LAMP_GREEN))):
        K.cyl(f"lens{k}", 0.13, 0.03, K.mat(f"lens{col}", col, rough=0.15), loc=(0, -0.18, z), rot=(90, 0, 0), segs=16, bev=0.005, parent=P)
        K.torus(f"bezel{k}", 0.14, 0.02, dark, loc=(0, -0.18, z), rot=(90, 0, 0), segs=16, parent=P)
        shell_panel(f"hood{k}", 0.17, math.radians(-10), math.radians(190), -0.42, -0.17, dark, t=0.015, parent=P, loc=(0, 0, z))
    label(P, "S14", (0, -0.176, zh - 0.62), 0.12, M(GRAPHITE, wear=0.2))
    # a platform and ladder for the lamp man
    K.box("platform", (0.9, 0.6, 0.04), M(ZINC, metal=0.5, wear=0.25, grime=0.5), loc=(0, 0.35, zh - 0.85), bev=0.005, parent=P)
    for x in (-0.42, 0.42):
        rod(P, f"guard{x}", (x, 0.62, zh - 0.85), (x, 0.62, zh - 0.05), 0.015, steel)
        beam(P, f"bracket{x}", (x, 0.08, zh - 1.2), (x, 0.6, zh - 0.87), 0.04, dark)
    rod(P, "guard_rail", (-0.42, 0.62, zh - 0.05), (0.42, 0.62, zh - 0.05), 0.015, steel)
    for y in (0.15, 0.45):
        K.box(f"ladder{y}", (0.04, 0.04, zh - 1.2), steel, loc=(0.12, y, 0.33 + (zh - 1.2) / 2), bev=0, parent=P)
    for k in range(int((zh - 1.5) / 0.3)):
        K.box(f"rung{k}", (0.03, 0.3, 0.03), steel, loc=(0.12, 0.3, 0.7 + k * 0.3), bev=0, parent=P)
    K.box("ladder_stay", (0.16, 0.04, 0.04), steel, loc=(0.06, 0.15, 2.4), bev=0, parent=P)
    # relay cabinet and the cable duct running from it up the mast
    FR.framed_box(P, "cabinet", (0.75, 0.15, 0.55), (0.5, 0.42, 1.1), M(BONE, wear=0.6, grime=0.6), steel, fw=0.06)
    K.box("cab_roof", (0.58, 0.5, 0.05), dark, loc=(0.75, 0.15, 1.12), rot=(0, -4, 0), bev=0.01, parent=P)
    K.box("duct", (0.08, 0.08, 1.6), dark, loc=(0.1, 0.12, 1.0), bev=0, parent=P)
    rod(P, "cable", (0.5, 0.2, 0.25), (0.13, 0.12, 0.25), 0.03, dark)
    rust_patch(P, (0.0, -0.092, 2.6), (0.08, 0.004, 0.5))
    for k in range(4):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, -0.4)), rnd, n=4, h=0.35)
    return P


def rail_buffer(seed=52):
    """The end of a line: a steel buffer stop bolted to the last rails, its head beam in signal red and bone stripes, the
    two sprung buffers facing up the track, a concrete block behind and a dead lamp on top."""
    rnd = random.Random(seed)
    P = _root("buffer")
    dark = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.5)
    x0 = -1.6
    zr = track(P, "track", x0 - 0.6, 2.6, rnd, skip=())
    for s in (-1, 1):
        y = s * 0.7175
        K.box(f"post{s}", (0.2, 0.16, 1.05 - zr + 0.1), dark, loc=(x0, y, zr + (1.05 - zr) / 2 - 0.05), bev=0.015, parent=P)
        beam(P, f"strut{s}", (x0 - 0.05, y, 1.0), (x0 - 1.1, y, zr), 0.14, dark)
        K.box(f"strut_foot{s}", (0.3, 0.22, 0.05), dark, loc=(x0 - 1.1, y, zr - 0.02), bev=0.005, parent=P)
        for k in range(2):
            K.box(f"clamp{s}{k}", (0.12, 0.24, 0.08), M(ZINC, metal=0.6, wear=0.3), loc=(x0 - 0.3 - k * 0.5, y, zr - 0.06), bev=0.005, parent=P)
    K.box("head", (0.3, 2.5, 0.36), M(SIGNAL, wear=0.4, grime=0.6), loc=(x0 + 0.08, 0, 1.06), bev=0.03, parent=P)
    for k in range(5):
        K.box(f"stripe{k}", (0.012, 0.22, 0.4), M(BONE, wear=0.4), loc=(x0 + 0.235, -1.0 + k * 0.5, 1.06), rot=(35, 0, 0), bev=0, parent=P)
    for y in (-0.88, 0.88):
        K.cyl(f"buffer{y}", 0.1, 0.36, dark, loc=(x0 + 0.42, y, 1.06), rot=(0, 90, 0), segs=12, bev=0, parent=P)
        K.cyl(f"buffer_sleeve{y}", 0.13, 0.18, dark, loc=(x0 + 0.32, y, 1.06), rot=(0, 90, 0), segs=12, bev=0.01, parent=P)
        K.cyl(f"buffer_head{y}", 0.22, 0.05, M(ZINC, metal=0.6, wear=0.2, grime=0.5), loc=(x0 + 0.62, y, 1.06), rot=(0, 90, 0), segs=16, bev=0.01, parent=P)
    K.box("block", (1.0, 2.8, 0.6), _concrete(), loc=(x0 - 1.45, 0, 0.3), bev=0.04, parent=P)
    K.cyl("lamp_post", 0.03, 0.4, dark, loc=(x0, 0, 1.44), segs=8, bev=0, parent=P)
    K.box("lamp", (0.18, 0.18, 0.22), dark, loc=(x0, 0, 1.72), bev=0.02, parent=P)
    K.cyl("lamp_lens", 0.06, 0.02, K.mat(f"lens{LAMP_RED}", LAMP_RED, rough=0.15), loc=(x0 + 0.1, 0, 1.72), rot=(0, 90, 0), segs=12, bev=0, parent=P)
    rust_patch(P, (x0 + 0.232, 0.5, 0.97), (0.004, 0.4, 0.12))
    for k in range(5):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-3.0, 2.4), rnd.uniform(-1.4, 1.4)), rnd, n=4, h=0.4)
    return P


def mine_cart(seed=53):
    """A colony ore tub on a short length of narrow-gauge track, rusted through its steel-blue paint, still half full of
    dark ore."""
    rnd = random.Random(seed)
    P = _root("minecart")
    g = 0.6
    zr = track(P, "track", -1.4, 1.4, rnd, gauge=g, pitch=0.55, wood_sleepers=True, ballast=False, s=0.6)
    paint = _paint(STEEL, wear=0.7, grime=0.7, moss=0.1)
    dark = _dark()
    r = 0.17
    for x in (-0.33, 0.33):
        K.cyl(f"axle{x}", 0.03, g + 0.2, dark, loc=(x, 0, zr + r), rot=(90, 0, 0), segs=8, bev=0, parent=P)
        for s in (-1, 1):
            K.cyl(f"wheel{x}{s}", r, 0.07, _rust(), loc=(x, s * (g / 2 + 0.02), zr + r), rot=(90, 0, 0), segs=14, bev=0.008, parent=P)
            K.cyl(f"flange{x}{s}", r + 0.025, 0.02, _rust(), loc=(x, s * (g / 2 - 0.035), zr + r), rot=(90, 0, 0), segs=14, bev=0, parent=P)
            K.box(f"axlebox{x}{s}", (0.14, 0.08, 0.14), dark, loc=(x, s * (g / 2 + 0.1), zr + r + 0.02), bev=0.01, parent=P)
    zf = zr + r + 0.1
    for s in (-1, 1):
        K.box(f"frame{s}", (1.2, 0.08, 0.12), dark, loc=(0, s * (g / 2 + 0.1), zf), bev=0.01, parent=P)
        K.box(f"end{s}", (0.08, g + 0.28, 0.12), dark, loc=(s * 0.6, 0, zf), bev=0.01, parent=P)
        K.box(f"coupling{s}", (0.2, 0.06, 0.05), dark, loc=(s * 0.7, 0, zf), bev=0.005, parent=P)
        K.torus(f"link{s}", 0.06, 0.012, _rust(), loc=(s * 0.83, 0, zf), rot=(0, 0, 0), segs=10, parent=P)
    tub("tub", (1.3, 0.95), (0.95, 0.65), 0.62, 0.03, paint, parent=P, loc=(0, 0, zf + 0.06))
    for s in (-1, 1):
        K.box(f"rim_x{s}", (1.34, 0.05, 0.05), dark, loc=(0, s * 0.485, zf + 0.68), bev=0.008, parent=P)
        K.box(f"rim_y{s}", (0.05, 0.99, 0.05), dark, loc=(s * 0.66, 0, zf + 0.68), bev=0.008, parent=P)
        K.box(f"strap{s}", (0.05, 0.02, 0.62), dark, loc=(s * 0.25, -0.405, zf + 0.37), rot=(-15, 0, 0), bev=0, parent=P)
    rust_patch(P, (0.05, -0.415, zf + 0.4), (0.4, 0.004, 0.18), rot=(-15, 0, 0))
    ore = K.mat("ore", STONE_D, rough=0.9, grime=0.3, cracks=0.4, noise_scale=4.0)
    ore2 = M(GRAPHITE, rough=0.8, wear=0.1)
    for k in range(9):
        lump(P, f"ore{k}", rnd.uniform(0.12, 0.18), (rnd.uniform(-0.42, 0.42), rnd.uniform(-0.25, 0.25), zf + 0.52 + rnd.uniform(0, 0.08)),
             (1.0, rnd.uniform(0.8, 1.1), 0.75), ore if k % 3 else ore2, rnd, u=7, v=5)
    lump(P, "ore_spilt", 0.1, (0.95, -0.55, 0.06), (1.2, 1.0, 0.7), ore, rnd, u=7, v=5)
    return P


def golf_cart_wreck(seed=54):
    """A colony site runabout (a two-seat golf cart): bone cowl, a steel canopy on four posts, one rear post snapped so
    the roof has come down onto the seat back, a crate still strapped on the rack behind."""
    rnd = random.Random(seed)
    P = _root("golfcart")
    bone = _paint(BONE, wear=0.75, grime=0.65)
    steel = _thin(STEEL)
    dark, glass = _dark(), _glass()
    rw = 0.22
    rim = M(ZINC, metal=0.5, wear=0.2, grime=0.6)
    for x, s, f in ((0.85, -1, 0.05), (0.85, 1, 0.05), (-0.8, -1, 0.3), (-0.8, 1, 0.05)):
        tyre(P, f"wheel{x}{s}", (x, s * 0.55, rw * (1 - f)), rw, 0.18, flat=f, rim=rim)
    B = _grp(P, "body", (0, 0, 0), (2.0, -1.5, 0))
    K.box("floor", (2.2, 1.1, 0.08), dark, loc=(0, 0, 0.3), bev=0.01, parent=B)
    profile(B, "cowl", [(0.5, 0.2), (1.15, 0.2), (1.22, 0.32), (1.2, 0.58), (0.95, 0.72), (0.62, 0.95), (0.5, 0.95)], 1.16, bone, bev=0.04)
    K.box("rear_body", (0.85, 1.16, 0.38), bone, loc=(-0.72, 0, 0.47), bev=0.04, parent=B)
    for s in (-1, 1):
        arch(B, f"arch_f{s}", 0.85, s * 0.585, 0.22, 0.29, dark)
        arch(B, f"arch_r{s}", -0.8, s * 0.585, 0.22, 0.29, dark)
        K.box(f"headlight{s}", (0.02, 0.16, 0.08), _lens(), loc=(1.215, s * 0.36, 0.48), bev=0.004, parent=B)
        K.box(f"taillight{s}", (0.02, 0.1, 0.08), M(SIGNAL, wear=0.2), loc=(-1.15, s * 0.45, 0.55), bev=0.004, parent=B)
    K.box("bumper", (0.1, 1.2, 0.1), dark, loc=(1.24, 0, 0.24), bev=0.015, parent=B)
    K.box("seat", (0.5, 1.0, 0.13), _cloth(), loc=(-0.52, 0, 0.72), bev=0.04, parent=B)
    K.box("seat_back", (0.1, 1.0, 0.45), _cloth(), loc=(-0.8, 0, 1.0), rot=(0, -10, 0), bev=0.04, parent=B)
    K.box("seat_split", (0.2, 0.18, 0.012), K.mat("foam", FOAM, rough=0.95), loc=(-0.5, 0.2, 0.79), rot=(0, 0, 12), bev=0, parent=B)
    rod(B, "column", (0.62, 0.28, 0.75), (0.35, 0.28, 1.05), 0.025, dark)
    K.torus("steer", 0.17, 0.016, dark, loc=(0.33, 0.28, 1.07), rot=(0, -60, 0), segs=16, parent=B)
    K.box("pedal", (0.12, 0.1, 0.03), dark, loc=(0.4, 0.25, 0.4), rot=(0, -30, 0), bev=0, parent=B)
    # canopy: front posts with the windscreen, rear posts, the rear-left one snapped
    for s in (-1, 1):
        rod(B, f"post_f{s}", (0.58, s * 0.52, 0.9), (0.42, s * 0.54, 1.98), 0.025, steel)
    rod(B, "post_r", (-0.95, 0.52, 0.62), (-0.95, 0.52, 1.95), 0.025, steel)
    rod(B, "post_r_stub", (-0.95, -0.52, 0.62), (-0.95, -0.52, 1.18), 0.025, steel)
    K.box("windscreen", (0.02, 1.0, 0.7), glass, loc=(0.52, 0, 1.5), rot=(0, -8, 0), bev=0.004, parent=B)
    cracks(B, (0.535, -0.2, 1.55), (0, -8, 0), n=3, size=0.4)
    Rf = _grp(B, "roof", (0.45, 0, 2.0), (-7, 9, 0))
    K.slab("roof", 1.75, 1.3, 0.06, 0.12, M(STEEL, wear=0.25, grime=0.55, moss=0.4), loc=(-0.72, 0, 0.0), bev=0.015, parent=Rf)
    for s in (-1, 1):
        K.box(f"roof_edge{s}", (1.75, 0.04, 0.08), dark, loc=(-0.72, s * 0.65, -0.02), bev=0.005, parent=Rf)
    rod(Rf, "post_r_hang", (-1.4, -0.52, -0.02), (-1.36, -0.66, -0.55), 0.025, steel)
    rust_patch(Rf, (-0.5, 0.2, 0.032), (0.4, 0.5, 0.004))
    # the rack behind and a crate strapped to it
    for s in (-1, 1):
        rod(B, f"rack_arm{s}", (-1.12, s * 0.42, 0.62), (-1.45, s * 0.42, 0.62), 0.02, steel)
    K.box("rack", (0.4, 0.95, 0.04), dark, loc=(-1.32, 0, 0.64), bev=0.005, parent=B)
    FR.framed_box(B, "crate", (-1.3, 0.05, 0.88), (0.42, 0.6, 0.44), M(BONE, wear=0.5, grime=0.5), steel, fw=0.04)
    K.box("strap", (0.43, 0.04, 0.46), M(MUSTARD, wear=0.25), loc=(-1.3, 0.1, 0.88), bev=0, parent=B)
    rusty(B, rnd, 3, (-1.0, 1.0), (0.3, 0.55), -0.58)
    rust_patch(B, (0.85, 0.2, 0.705), (0.3, 0.4, 0.004), rot=(0, -24, 0))
    for k in range(4):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-1.2, 1.2), rnd.choice((-0.7, 0.7)) + rnd.uniform(-0.1, 0.1)), rnd, n=4, h=0.3)
    return P


def quad_bike_wreck(seed=55):
    """A colony quad bike with mustard plastics, nose down on its front-left hub where the wheel came off (it lies in
    the grass in front), racks bent, seat split."""
    rnd = random.Random(seed)
    P = _root("quad")
    plastic = _paint(MUSTARD, wear=0.55, grime=0.6)
    dark, steel = _dark(), _thin(STEEL)
    r, w = 0.3, 0.24
    rim = M(GRAPHITE, metal=0.4, wear=0.2, grime=0.5)
    for x, s, f in ((0.62, 1, 0.08), (-0.6, -1, 0.1), (-0.6, 1, 0.06)):
        tyre(P, f"wheel{x}{s}", (x, s * 0.5, r * (1 - f)), r, w, flat=f, rim=rim)
    B = _grp(P, "body", (0, 0, 0.28), (6.5, 3.5, 0))
    K.cyl("hub", 0.09, 0.14, _rust(), loc=(0.62, -0.42, 0.0), rot=(90, 0, 0), segs=8, bev=0, parent=B)
    for s in (-1, 1):
        rod(B, f"frame_lo{s}", (-0.75, s * 0.2, 0.1), (0.75, s * 0.2, 0.1), 0.025, dark)
        rod(B, f"frame_hi{s}", (-0.6, s * 0.18, 0.45), (0.55, s * 0.18, 0.5), 0.025, dark)
        rod(B, f"a_arm{s}", (0.5, s * 0.15, 0.08), (0.62, s * 0.38, 0.0), 0.02, dark)
        rod(B, f"shock{s}", (0.55, s * 0.2, 0.45), (0.62, s * 0.36, 0.05), 0.03, M(SIGNAL, metal=0.3, wear=0.2))
        K.box(f"footboard{s}", (0.45, 0.22, 0.03), dark, loc=(0.0, s * 0.38, 0.12), bev=0.005, parent=B)
    K.box("engine", (0.5, 0.36, 0.34), M(GUNMETAL, metal=0.5, wear=0.25, grime=0.6), loc=(0.0, 0, 0.3), bev=0.03, parent=B)
    profile(B, "front_body", [(0.22, 0.42), (0.95, 0.4), (1.02, 0.5), (0.88, 0.62), (0.6, 0.66), (0.3, 0.72), (0.18, 0.62)], 1.08, plastic, bev=0.03)
    profile(B, "rear_body", [(-0.95, 0.42), (-0.25, 0.42), (-0.25, 0.62), (-0.55, 0.66), (-0.92, 0.6)], 1.08, plastic, bev=0.03)
    K.box("tank", (0.42, 0.34, 0.2), plastic, loc=(0.05, 0, 0.66), bev=0.05, parent=B)
    K.box("seat", (0.62, 0.32, 0.1), _cloth(), loc=(-0.38, 0, 0.73), rot=(0, 2, 0), bev=0.035, parent=B)
    K.box("seat_split", (0.25, 0.04, 0.012), K.mat("foam", FOAM, rough=0.95), loc=(-0.42, 0.05, 0.783), rot=(0, 0, 15), bev=0, parent=B)
    for s in (-1, 1):
        K.box(f"headlight{s}", (0.02, 0.14, 0.08), _lens(), loc=(1.0, s * 0.3, 0.52), rot=(0, -15, 0), bev=0.004, parent=B)
    rod(B, "column", (0.3, 0, 0.6), (0.4, 0, 0.88), 0.025, dark)
    H = _grp(B, "bars", (0.4, 0, 0.9), (0, 0, -14))
    rod(H, "handlebar", (0, -0.4, 0.02), (0, 0.4, 0.02), 0.014, M(ZINC, metal=0.6, wear=0.2))
    for s in (-1, 1):
        K.cyl(f"grip{s}", 0.022, 0.12, _rubber(), loc=(0, s * 0.35, 0.02), rot=(90, 0, 0), segs=8, bev=0, parent=H)
    # racks, the front one bent down where the nose hit
    for x0, x1, z, bend in ((0.45, 0.98, 0.72, -12), (-0.95, -0.35, 0.72, 0)):
        Rk = _grp(B, f"rack{x0}", (x1 if bend else x0, 0, z), (0, -bend, 0))
        L = x1 - x0
        sx = -1 if bend else 1
        for s in (-1, 1):
            rod(Rk, f"rack_side{s}", (0, s * 0.42, 0), (sx * L, s * 0.42, 0), 0.014, steel)
        for k in range(4):
            xx = sx * L * k / 3
            rod(Rk, f"rack_bar{k}", (xx, -0.42, 0), (xx, 0.42, 0), 0.012, steel)
    rusty(B, rnd, 2, (-0.8, 0.8), (0.48, 0.58), -0.546)
    lying_wheel(P, "wheel_off", (1.55, -0.85, 0), r, w, tilt=(-8, 0), rim=rim)
    for k in range(4):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-0.9, 1.1), rnd.uniform(-0.7, 0.7)), rnd, n=4, h=0.3)
    return P


def boat_trailer(seed=56):
    """A small colony work boat on its trailer, both left at the field edge: a steel-blue hull with a bone gunwale on
    wooden bunks, the outboard tilted up, the winch strap still on the bow eye, one trailer tyre gone flat."""
    rnd = random.Random(seed)
    P = _root("boattrailer")
    dark, steel = _dark(), _thin(STEEL)
    hullp = _paint(STEEL, wear=0.6, grime=0.65, moss=0.25)
    rim = M(ZINC, metal=0.5, wear=0.2, grime=0.6)
    rw = 0.3
    tyre(P, "wheel_l", (-0.3, -0.85, rw * 0.75), rw, 0.18, flat=0.25, rim=rim)
    tyre(P, "wheel_r", (-0.3, 0.85, rw * 0.96), rw, 0.18, flat=0.04, rim=rim)
    T = _grp(P, "trailer", (0, 0, 0), (2.5, 0, 0))
    K.cyl("axle", 0.04, 1.6, dark, loc=(-0.3, 0, 0.26), rot=(90, 0, 0), segs=8, bev=0, parent=T)
    for s in (-1, 1):
        K.box(f"rail{s}", (3.8, 0.08, 0.1), steel, loc=(-0.25, s * 0.45, 0.42), bev=0.01, parent=T)
        K.box(f"spring{s}", (0.7, 0.06, 0.06), dark, loc=(-0.3, s * 0.45, 0.33), bev=0, parent=T)
        beam(T, f"tongue{s}", (1.55, s * 0.45, 0.42), (2.9, 0, 0.42), 0.08, steel)
        shell_panel(f"fender{s}", rw + 0.08, math.radians(20), math.radians(160), s * 0.73, s * 0.98, M(GRAPHITE, metal=0.3, wear=0.2), t=0.02, parent=T,
                    loc=(-0.3, 0, rw))
        beam(T, f"bunk_post{s}", (-1.4, s * 0.38, 0.47), (-1.4, s * 0.36, 0.6), 0.06, dark)
        beam(T, f"bunk_post2{s}", (0.9, s * 0.38, 0.47), (0.9, s * 0.36, 0.6), 0.06, dark)
        plank(T, f"bunk{s}", (3.0, 0.12, 0.07), (-0.25, s * 0.35, 0.64), rot=(-s * 20, 0, 0), shade=s + 1)
        K.box(f"light{s}", (0.06, 0.16, 0.1), M(SIGNAL, wear=0.2), loc=(-2.15, s * 0.5, 0.48), bev=0.008, parent=T)
    K.box("lightbar", (0.05, 1.1, 0.05), dark, loc=(-2.15, 0, 0.45), bev=0, parent=T)
    for k in range(4):
        K.box(f"cross{k}", (0.08, 0.98, 0.08), steel, loc=(-2.0 + k * 1.1, 0, 0.42), bev=0.008, parent=T)
    K.cyl("keel_roller", 0.07, 0.22, _rubber(), loc=(1.4, 0, 0.55), rot=(90, 0, 0), segs=10, bev=0, parent=T)
    K.box("coupler", (0.3, 0.1, 0.1), dark, loc=(3.0, 0, 0.42), bev=0.01, parent=T)
    K.sphere("coupler_ball", 0.06, dark, loc=(3.15, 0, 0.42), parent=T)
    # jack wheel down on the grass, winch post with its drum and strap
    rod(T, "jack", (2.4, -0.12, 0.6), (2.4, -0.12, 0.12), 0.035, dark)
    K.cyl("jack_wheel", 0.09, 0.06, _rubber(), loc=(2.4, -0.12, 0.09), rot=(90, 0, 0), segs=10, bev=0.01, parent=T)
    beam(T, "winch_post", (2.15, 0, 0.42), (2.05, 0, 1.12), 0.08, steel)
    K.cyl("winch", 0.08, 0.22, dark, loc=(2.08, 0, 1.0), rot=(90, 0, 0), segs=10, bev=0.01, parent=T)
    K.box("bow_stop", (0.1, 0.18, 0.14), _rubber(), loc=(1.98, 0, 1.12), bev=0.02, parent=T)
    # the hull, slid a little to one side on the bunks
    L, Bm, D = 4.2, 1.6, 0.62
    H = _grp(T, "hull", (-0.15, 0.06, 0.66 + 0.9 * D), (1.5, 0, 0))
    hull = hull_mesh("hull", L, Bm, D, hullp)
    hull.parent = H
    K.bevel(hull, 0.01, segs=1)
    # bone gunwale capping and rub strake, laid along the sheer
    gun = M(BONE, wear=0.25, grime=0.5)
    n = 12
    pts = []
    for i in range(n + 1):
        u = i / n
        f = max(0.0, (u - 0.5) / 0.5)
        pts.append((-L / 2 + L * u, max(Bm / 2 * (1 - f ** 1.7), 0.03), 0.16 * f ** 2))
    for s in (-1, 1):
        for i in range(n):
            (x0, b0, z0), (x1, b1, z1) = pts[i], pts[i + 1]
            rod(H, f"gunwale{s}{i}", (x0, s * (b0 + 0.01), z0 + 0.015), (x1, s * (b1 + 0.01), z1 + 0.015), 0.03, gun, segs=6)
            if i < n - 2:
                rod(H, f"strake{s}{i}", (x0, s * (b0 * 0.985 + 0.012), z0 - 0.2), (x1, s * (b1 * 0.985 + 0.012), z1 - 0.2), 0.02, gun, segs=6)
    for k, x in enumerate((-0.9, 0.5)):
        b = Bm / 2 - 0.08
        plank(H, f"thwart{k}", (0.26, 2 * b - 0.04, 0.04), (x, 0, -0.18), shade=k)
    K.box("transom_pad", (0.06, 0.4, 0.3), _dark(), loc=(-L / 2 - 0.02, 0, -0.12), bev=0.01, parent=H)
    O = _grp(H, "outboard", (-L / 2 - 0.1, 0, 0.05), (0, -55, 0))
    K.box("cowl", (0.38, 0.3, 0.36), M(GRAPHITE, metal=0.3, wear=0.25, grime=0.5), loc=(-0.12, 0, 0.12), bev=0.06, parent=O)
    K.box("cowl_band", (0.39, 0.31, 0.05), M(MUSTARD, wear=0.3), loc=(-0.12, 0, 0.05), bev=0.01, parent=O)
    K.box("shaft", (0.1, 0.08, 0.6), dark, loc=(-0.12, 0, -0.32), bev=0.01, parent=O)
    K.box("skeg", (0.16, 0.03, 0.12), dark, loc=(-0.16, 0, -0.64), bev=0.005, parent=O)
    K.cyl("prop", 0.1, 0.03, dark, loc=(-0.06, 0, -0.58), rot=(0, 90, 0), segs=3, bev=0, parent=O)
    K.cyl("bow_eye", 0.04, 0.03, M(ZINC, metal=0.6), loc=(L / 2 - 0.05, 0, -0.18), rot=(0, 90, 0), segs=8, bev=0, parent=H)
    rod(P, "strap", (2.08 * math.cos(math.radians(2.5)), 0, 1.06), (L / 2 - 0.2, 0.05, 0.66 + 0.9 * D - 0.12), 0.012, M(MUSTARD, wear=0.25, grime=0.5), segs=4)
    for k in range(5):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-2.2, 2.6), rnd.uniform(-1.1, 1.1)), rnd, n=4, h=0.35)
    return P


def harvester_combine_wreck(seed=57):
    """A colony combine harvester gone to rust in its last field: mustard body on big lugged drive wheels, a glazed cab,
    the grain tank on top, the cutting table dropped off its feeder into the grass with its reel bent, a side panel off."""
    rnd = random.Random(seed)
    P = _root("combine")
    paint = _paint(MUSTARD, wear=0.9, grime=0.75, moss=0.25)
    dark, steel, glass = _dark(), _thin(STEEL), _glass()
    rim = M(BONE, metal=0.3, wear=0.25, grime=0.6)
    for s in (-1, 1):
        tyre(P, f"drive{s}", (1.2, s * 1.48, 0.85 * 0.97), 0.85, 0.6, flat=0.03, rim=rim, lugs=10)
    tyre(P, "steer_l", (-2.8, -1.15, 0.5 * 0.78), 0.5, 0.32, flat=0.22, rim=rim)
    tyre(P, "steer_r", (-2.8, 1.15, 0.5 * 0.96), 0.5, 0.32, flat=0.04, rim=rim)
    K.box("rear_axle", (0.2, 2.1, 0.2), dark, loc=(-2.8, 0, 0.45), bev=0.02, parent=P)
    B = _grp(P, "body", (0, 0, 0), (1.2, -1.2, 0))
    K.box("frame", (5.4, 2.1, 0.3), dark, loc=(-0.65, 0, 1.0), bev=0.02, parent=B)
    K.box("hull", (5.3, 2.2, 2.0), paint, loc=(-0.65, 0, 2.1), bev=0.06, parent=B)
    for s in (-1, 1):
        for k, x in enumerate((-2.6, -1.3, 0.0, 1.3)):
            K.box(f"seam{s}{k}", (0.06, 0.03, 1.9), steel, loc=(x, s * 1.105, 2.1), bev=0, parent=B)
        K.box(f"band{s}", (5.3, 0.03, 0.12), steel, loc=(-0.65, s * 1.105, 3.0), bev=0, parent=B)
        rusty(B, rnd, 2, (-3.0, 1.6), (1.4, 2.8), s * 1.1)
    # a side panel off and lying in the grass, the ribs and dark works behind it
    K.box("bay", (1.15, 0.012, 1.0), K.mat("shadow", "#15171a", rough=0.9), loc=(-1.95, -1.104, 2.0), bev=0, parent=B)
    for k in range(4):
        K.box(f"rib{k}", (0.05, 0.05, 1.0), _rust(), loc=(-2.4 + k * 0.3, -1.1, 2.0), bev=0, parent=B)
    K.box("panel_off", (1.15, 1.0, 0.04), paint, loc=(-1.7, -2.45, 0.05), rot=(4, -3, 18), bev=0.01, parent=P)
    label(B, "CW-H2", (0.65, -1.106, 2.45), 0.32, M(GRAPHITE, wear=0.3))
    FR.vent(B, (-2.75, -1.12, 2.55), 0.8, 0.45, steel, dark)
    rod(B, "stack", (-2.5, -0.6, 3.1), (-2.55, -0.6, 3.95), 0.08, _rust(), segs=10)
    K.box("spreader", (0.5, 1.7, 0.55), dark, loc=(-3.45, 0, 1.45), bev=0.03, parent=B)
    K.box("spreader_mouth", (0.02, 1.4, 0.3), K.mat("shadow", "#15171a", rough=0.9), loc=(-3.71, 0, 1.4), bev=0, parent=B)
    FR.hazard(B, (-3.33, 0, 1.0), 2.0, 0.14, M(MUSTARD), dark, n=4, axis="x")
    # grain tank: an open bin on top, rotten grain still in it
    tub("grain_tank", (2.8, 2.4), (2.2, 2.0), 0.85, 0.04, paint, parent=B, loc=(-0.6, 0, 3.1))
    lump(B, "grain", 1.0, (-0.6, 0, 3.6), (1.25, 1.05, 0.25), K.mat("grain", "#8f7f52", rough=1.0, grime=0.4, moss=0.5, noise_scale=8.0), rnd, u=10, v=6)
    # unloading auger folded along the right side
    rod(B, "unload", (-1.9, 1.25, 3.55), (1.3, 1.5, 3.3), 0.17, paint, segs=12)
    rod(B, "unload_spout", (1.3, 1.5, 3.3), (1.45, 1.5, 2.95), 0.13, dark, segs=10)
    K.box("unload_rest", (0.12, 0.4, 0.5), dark, loc=(0.9, 1.3, 3.0), bev=0.01, parent=B)
    # cab on the front of the body
    K.box("cab_floor", (1.5, 2.0, 0.14), dark, loc=(1.65, 0, 3.12), bev=0.02, parent=B)
    K.box("cab_glass", (1.3, 1.75, 1.3), glass, loc=(1.68, 0, 3.86), bev=0.03, parent=B)
    for x in (1.02, 2.34):
        for s in (-1, 1):
            K.box(f"cab_post{x}{s}", (0.08, 0.08, 1.36), steel, loc=(x, s * 0.88, 3.86), bev=0, parent=B)
    K.box("cab_rail", (1.36, 0.08, 0.08), steel, loc=(1.68, -0.88, 3.25), bev=0.01, parent=B)
    K.box("cab_roof", (1.65, 2.0, 0.14), M(BONE, wear=0.5, grime=0.6, moss=0.4), loc=(1.7, 0, 4.58), bev=0.04, parent=B)
    for s in (-1, 1):
        K.box(f"worklamp{s}", (0.1, 0.16, 0.1), dark, loc=(2.48, s * 0.7, 4.55), bev=0.01, parent=B)
    cracks(B, (2.335, -0.2, 3.9), (0, 0, 0), n=4, size=0.6)
    K.box("seat", (0.45, 0.5, 0.5), _cloth(), loc=(1.45, 0.1, 3.45), bev=0.05, parent=B)
    # ladder up the left side to the cab
    for x in (0.25, 0.65):
        K.box(f"ladder{x}", (0.05, 0.05, 2.5), steel, loc=(x, -1.2, 1.85), rot=(0, -6, 0), bev=0, parent=B)
    for k in range(7):
        K.box(f"rung{k}", (0.45, 0.04, 0.04), steel, loc=(0.45 - (k - 3) * 0.04, -1.2, 0.85 + k * 0.33), bev=0, parent=B)
    # feeder house and the cutting table dropped off it
    beam(B, "feeder", (2.0, 0, 1.8), (2.95, 0, 1.0), 0.75, paint, d=1.15, bev=0.03)
    Hd = _grp(P, "header", (3.75, -0.1, 0.0), (-2.5, 0, 7))
    W = 5.8
    K.box("table_floor", (1.4, W, 0.06), dark, loc=(0, 0, 0.32), bev=0.01, parent=Hd)
    K.box("table_back", (0.08, W, 0.95), paint, loc=(-0.66, 0, 0.78), bev=0.015, parent=Hd)
    K.box("table_lip", (0.4, W, 0.06), paint, loc=(-0.48, 0, 1.24), rot=(0, 20, 0), bev=0.01, parent=Hd)
    for s in (-1, 1):
        profile(Hd, f"table_end{s}", [(-0.7, 0.29), (0.72, 0.29), (0.76, 0.38), (-0.1, 1.28), (-0.7, 1.28)], 0.06, paint, loc=(0, s * W / 2, 0), bev=0.01)
        K.box(f"skid{s}", (1.3, 0.12, 0.06), dark, loc=(0.0, s * (W / 2 - 0.2), 0.27), bev=0.01, parent=Hd)
    K.cyl("auger", 0.22, W - 0.2, steel, loc=(-0.3, 0, 0.62), rot=(90, 0, 0), segs=12, bev=0, parent=Hd)
    for k in range(9):
        K.cyl(f"flight{k}", 0.3, 0.02, _rust(), loc=(-0.3, -2.6 + k * 0.65, 0.62), rot=(90, 18, 0), segs=8, bev=0, parent=Hd)
    for k in range(12):
        K.box(f"guard{k}", (0.16, 0.05, 0.04), steel, loc=(0.78, -W / 2 + 0.2 + k * (W - 0.4) / 11, 0.31), bev=0, parent=Hd)
    for s in (-1, 1):
        beam(Hd, f"reel_arm{s}", (-0.62, s * (W / 2 - 0.1), 1.25), (0.32, s * (W / 2 - 0.2), 1.55), 0.08, dark)
    Rl = _grp(Hd, "reel", (0.32, 0, 1.55), (0, 0, 0))
    K.cyl("reel_tube", 0.07, W - 0.3, dark, rot=(90, 0, 0), segs=8, bev=0, parent=Rl)
    for y in (-2.6, 0.0, 2.6):
        for k in range(5):
            a = 2 * math.pi * k / 5 + 0.3
            beam(Rl, f"spider{y}{k}", (0, y, 0), (math.cos(a) * 0.6, y, math.sin(a) * 0.6), 0.04, dark, bev=0)
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        p = (math.cos(a) * 0.6, math.sin(a) * 0.6)
        if k == 1:   # one bat torn loose at one end and hanging
            rod(Rl, f"bat{k}", (p[0], -2.7, p[1]), (p[0] + 0.5, 0.4, p[1] - 0.95), 0.03, M(MUSTARD, metal=0.2, wear=0.2, grime=0.5), segs=6)
            continue
        rod(Rl, f"bat{k}", (p[0], -2.7, p[1]), (p[0], 2.7, p[1]), 0.03, M(MUSTARD, metal=0.2, wear=0.2, grime=0.5), segs=6)
    rust_patch(Hd, (-0.62, -1.2, 0.8), (0.004, 0.9, 0.4))
    rust_patch(Hd, (-0.62, 1.6, 0.9), (0.004, 0.7, 0.3))
    for k in range(5):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-3.4, 4.4), rnd.uniform(-2.8, 2.8)), rnd, n=4, h=0.5)
    return P


def colony_rover_wreck(seed=58):
    """A six-wheeled colony survey rover broken down out in the open: a rounded bone cab with a wrap-round window band,
    a framed lab module behind, rocker-bogie legs (the front-left one snapped down onto its hub, its wheel lying in
    front), the survey dish knocked askew on its mast."""
    rnd = random.Random(seed)
    P = _root("rover")
    bone = _paint(BONE, wear=0.7, grime=0.65, moss=0.2)
    steel = M(STEEL, metal=0.3, wear=0.3, grime=0.6)
    dark, glass = _dark(), _glass()
    rim = M(STEEL, metal=0.4, wear=0.25, grime=0.6)
    r, w, ys = 0.5, 0.38, 1.42
    for s in (-1, 1):
        for x, f in ((1.9, 0.04), (0.0, 0.08 if s < 0 else 0.03), (-1.9, 0.05)):
            if s < 0 and x > 1:
                continue
            tyre(P, f"wheel{x}{s}", (x, s * ys, r * (1 - f)), r, w, flat=f, rim=rim)
            K.cyl(f"motor{x}{s}", 0.17, 0.2, steel, loc=(x, s * 1.13, r * (1 - f)), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    for s in (-1, 1):
        y = s * 1.13
        front = (1.9, y, r * 0.96) if s > 0 else (1.75, y, 0.18)
        beam(P, f"rocker_f{s}", (0.45, y, 1.18), front, 0.14, dark, bev=0)
        beam(P, f"rocker_r{s}", (0.45, y, 1.18), (-0.95, y, 0.95), 0.14, dark, bev=0)
        beam(P, f"bogie_m{s}", (-0.95, y, 0.95), (0.0, y, r * 0.95), 0.12, dark, bev=0)
        beam(P, f"bogie_r{s}", (-0.95, y, 0.95), (-1.9, y, r * 0.95), 0.12, dark, bev=0)
        for p in ((0.45, y, 1.18), (-0.95, y, 0.95)):
            K.cyl(f"pivot{p[0]}{s}", 0.1, 0.22, steel, loc=p, rot=(90, 0, 0), segs=8, bev=0, parent=P)
    K.cyl("stub", 0.17, 0.22, _rust(), loc=(1.75, -1.18, 0.18), rot=(90, 0, 0), segs=10, bev=0.01, parent=P)
    lying_wheel(P, "wheel_off", (3.1, -2.1, 0), r, w, tilt=(-6, 0), rim=rim)
    # body
    K.box("chassis", (4.4, 1.9, 0.45), steel, loc=(0, 0, 1.35), bev=0.04, parent=P)
    K.box("bumper", (0.18, 2.0, 0.24), dark, loc=(2.25, 0, 1.3), bev=0.02, parent=P)
    FR.hazard(P, (2.35, 0, 1.3), 1.8, 0.14, M(MUSTARD), dark, n=4, axis="x")
    K.slab("cab", 2.0, 2.1, 1.45, 0.55, bone, loc=(1.15, 0, 2.3), bev=0.1, parent=P)
    K.slab("cab_band", 2.04, 2.14, 0.48, 0.57, glass, loc=(1.15, 0, 2.45), bev=0.03, parent=P)
    K.box("cab_roof", (1.5, 1.7, 0.1), steel, loc=(1.15, 0, 3.05), bev=0.03, parent=P)
    cracks(P, (2.17, -0.3, 2.45), (0, 0, 0), n=3, size=0.4)
    for y in (-0.5, 0.5):
        K.box(f"mullion{y}", (0.05, 0.06, 0.5), steel, loc=(2.17, y, 2.45), bev=0, parent=P)
    K.box("lightbar", (0.2, 1.2, 0.1), dark, loc=(1.7, 0, 3.15), bev=0.02, parent=P)
    for k in range(4):
        K.box(f"lamp{k}", (0.02, 0.18, 0.06), _lens(), loc=(1.81, -0.45 + k * 0.3, 3.15), bev=0, parent=P)
    label(P, "CW-R6", (1.05, -1.051, 1.92), 0.17, M(STEEL, wear=0.3))
    # lab module behind, a porthole each side, a hatch at the back
    FR.framed_box(P, "lab", (-1.0, 0, 2.22), (2.3, 2.0, 1.3), bone, steel, fw=0.1)
    for s in (-1, 1):
        K.cyl(f"port_ring{s}", 0.25, 0.03, steel, loc=(-0.7, s * 0.975, 2.3), rot=(90, 0, 0), segs=12, bev=0, parent=P)
        K.cyl(f"port{s}", 0.2, 0.03, glass, loc=(-0.7, s * 0.99, 2.3), rot=(90, 0, 0), segs=12, bev=0, parent=P)
        rusty(P, rnd, 1, (-1.9, -1.4), (1.8, 2.7), s * 0.965)
    K.box("hatch", (0.03, 0.9, 1.0), M(STEEL, wear=0.3, grime=0.6), loc=(-2.165, 0, 2.15), bev=0.01, parent=P)
    K.box("hatch_handle", (0.04, 0.05, 0.25), dark, loc=(-2.19, 0.3, 2.15), bev=0, parent=P)
    for k in range(2):
        K.box(f"step{k}", (0.12, 0.5, 0.03), dark, loc=(-2.25, -0.65, 1.05 + k * 0.3), bev=0, parent=P)
    # mast with the dish knocked over and a bent whip antenna
    rod(P, "mast", (-1.6, 0.55, 2.87), (-1.6, 0.55, 4.1), 0.06, dark, segs=8)
    D = _grp(P, "dish_mount", (-1.6, 0.55, 4.1), (70, 0, 35))
    dish = dish_mesh("dish", 0.6, 0.16, 0.025, M(BONE, wear=0.25, grime=0.5), rings=4, segs=16)
    dish.parent = D
    K.cyl("feed", 0.03, 0.5, dark, loc=(0, 0, 0.25), segs=6, bev=0, parent=D)
    rod(P, "whip0", (-0.4, -0.7, 2.87), (-0.4, -0.7, 3.8), 0.012, dark, segs=4)
    rod(P, "whip1", (-0.4, -0.7, 3.8), (-0.1, -0.85, 4.25), 0.01, dark, segs=4)
    rust_patch(P, (1.3, 0.3, 3.105), (0.5, 0.6, 0.004))
    for k in range(3):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-2.4, 2.6), rnd.uniform(-1.9, 1.9)), rnd, n=4, h=0.4)
    return P


def skirt_mesh(name, w, d, rr, mat, rad=0.9, gap=None, sides=8, squash=0.6, step=0.5):
    """A hovercraft's rubber skirt: a fat tube swept round a rounded rectangle. gap (x0, x1) on the -y side leaves it
    torn open there."""
    loop = K.rounded_rect(w, d, rad, segs=4)
    pts = []
    for i in range(len(loop)):
        a, b = Vector(loop[i]), Vector(loop[(i + 1) % len(loop)])
        n = max(1, int((b - a).length / step))
        pts += [a.lerp(b, k / n) for k in range(n)]
    keep = [not (gap and p.y < -d / 2 + 0.01 and gap[0] < p.x < gap[1]) for p in pts]
    N = len(pts)
    start = next((i for i in range(N) if keep[i] and not keep[i - 1]), 0)
    order = [(start + k) % N for k in range(N) if keep[(start + k) % N]] if gap else list(range(N))
    bm = bmesh.new()
    rings = []
    for i in order:
        t = (pts[(i + 1) % N] - pts[i - 1]).normalized()
        out = Vector((t.y, -t.x))
        p = pts[i]
        rings.append([bm.verts.new((p.x + out.x * rr * math.cos(2 * math.pi * j / sides), p.y + out.y * rr * math.cos(2 * math.pi * j / sides),
                                    rr * squash * math.sin(2 * math.pi * j / sides))) for j in range(sides)])
    M_ = len(rings)
    for k in range(M_ if not gap else M_ - 1):
        r0, r1 = rings[k], rings[(k + 1) % M_]
        for j in range(sides):
            bm.faces.new((r0[j], r0[(j + 1) % sides], r1[(j + 1) % sides], r1[j]))
    if gap:
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.from_bm(name, bm, mat, smooth=True)


def hovercraft_wreck(seed=59):
    """A colony hovercraft sat down for good on its deflated skirt, the skirt torn open along one side, a bone cabin with
    a window band, two ducted fans at the back (one duct split and a blade lying on the deck)."""
    rnd = random.Random(seed)
    P = _root("hovercraft")
    hullp = _paint(STEEL, wear=0.75, grime=0.7, moss=0.25)
    bone = _paint(BONE, wear=0.7, grime=0.65)
    dark, glass = _dark(), _glass()
    rubber = K.mat("skirt", "#2b2d30", rough=0.85, grime=0.5, moss=0.2)
    B = _grp(P, "craft", (0, 0, 0), (2.0, -1.2, 0))
    sk = skirt_mesh("skirt", 6.1, 3.1, 0.34, rubber, gap=(-1.7, -0.1))
    sk.parent = B
    sk.location = (0, 0, 0.26)
    for k, (x, rx) in enumerate(((-1.5, 70), (-0.95, 80), (-0.4, 65))):
        K.box(f"flap{k}", (0.5, 0.5, 0.02), rubber, loc=(x, -1.75, 0.2), rot=(rx, rnd.uniform(-10, 10), rnd.uniform(-8, 8)), bev=0, parent=B)
    K.slab("hull", 6.0, 3.0, 0.4, 0.9, hullp, loc=(0, 0, 0.64), bev=0.06, parent=B)
    K.slab("deck_edge", 6.1, 3.1, 0.06, 0.92, dark, loc=(0, 0, 0.86), bev=0.02, parent=B)
    label(B, "CW-V5", (0.9, -1.502, 0.62), 0.2, M(BONE, wear=0.3))
    rusty(B, rnd, 3, (-2.0, 2.0), (0.52, 0.72), -1.5)
    # cabin
    K.slab("cabin", 2.6, 2.3, 1.05, 0.5, bone, loc=(0.9, 0, 1.4), bev=0.08, parent=B)
    K.slab("cabin_band", 2.64, 2.34, 0.42, 0.52, glass, loc=(0.9, 0, 1.55), bev=0.03, parent=B)
    K.slab("cabin_roof", 2.7, 2.4, 0.08, 0.55, M(STEEL, wear=0.25, grime=0.6, moss=0.4), loc=(0.9, 0, 1.96), bev=0.02, parent=B)
    cracks(B, (2.22, 0.3, 1.55), (0, 0, 0), n=3, size=0.4)
    K.box("door_seam", (0.6, 0.014, 0.9), K.mat("shadow", "#15171a", rough=0.9), loc=(0.2, -1.152, 1.32), bev=0, parent=B)
    K.box("door_handle", (0.12, 0.03, 0.03), dark, loc=(0.42, -1.17, 1.25), bev=0, parent=B)
    K.box("intake", (0.8, 1.2, 0.06), dark, loc=(2.55, 0, 0.87), bev=0.01, parent=B)
    for k in range(5):
        K.box(f"intake_bar{k}", (0.04, 1.2, 0.04), M(STEEL, metal=0.3, wear=0.2), loc=(2.23 + k * 0.16, 0, 0.91), bev=0, parent=B)
    # ducted fans
    zc, xd = 1.75, -2.2
    for s in (-1, 1):
        y = s * 0.82
        F = _grp(B, f"fan{s}", (xd, y, zc), (0, 90, 0))
        if s > 0:
            lathe("duct", [(0.74, -0.25), (0.86, -0.25), (0.88, 0.25), (0.74, 0.25)], dark, segs=20, parent=F)
        else:
            Fs = _grp(F, "duct_split", (0, 0, 0), (90, 0, 0))
            for k in range(3):
                shell_panel(f"duct_part{k}", 0.86, math.radians(-60 + k * 90), math.radians(30 + k * 90), -0.25, 0.25, dark, t=0.12, parent=Fs)
        K.cyl("hub", 0.14, 0.3, M(GUNMETAL, metal=0.5, wear=0.2), segs=10, bev=0.01, parent=F)
        for k in range(4 if s > 0 else 3):
            K.box(f"blade{k}", (0.7, 0.16, 0.03), M(ZINC, metal=0.5, wear=0.2, grime=0.5), loc=(0, 0, 0.0), rot=(0, 0, 45 * (2 * k + 1)), bev=0.005, parent=F)
        for k in range(2):
            K.box(f"stator{k}", (1.5, 0.04, 0.04), dark, loc=(0, 0, 0.18), rot=(0, 0, 45 + 90 * k), bev=0, parent=F)
        K.box(f"pylon{s}", (0.3, 0.2, 0.2), dark, loc=(xd, y, 0.95), bev=0.02, parent=B)
        K.box(f"rudder{s}", (0.45, 0.04, 1.3), M(STEEL, wear=0.3, grime=0.6), loc=(xd - 0.6, y, zc), rot=(0, 0, 12), bev=0.01, parent=B)
    K.box("rudder_bar", (0.06, 1.8, 0.06), dark, loc=(xd - 0.75, 0, zc + 0.5), bev=0, parent=B)
    K.box("blade_loose", (0.7, 0.16, 0.03), M(ZINC, metal=0.5, wear=0.2, grime=0.5), loc=(-0.9, 0.9, 0.9), rot=(0, 3, 30), bev=0.005, parent=B)
    for k in range(5):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-3.2, 3.2), rnd.choice((-1.85, 1.85)) + rnd.uniform(-0.15, 0.15)), rnd, n=5, h=0.4)
    return P


# ================================================================== air
def airplane_tail(seed=60):
    """The tail of a colony transport standing out of the ground where it came down nose first: a bone tail cone with a
    steel band, the fin and rudder in steel blue with a mustard tip, one tailplane torn short, skin torn round the
    ground."""
    rnd = random.Random(seed)
    P = _root("planetail")
    bone = _paint(BONE, wear=0.75, grime=0.65, moss=0.3)
    steel = _paint(STEEL, wear=0.7, grime=0.6, moss=0.2)
    dark = _dark()
    mound(P, "crater", (0.0, 0.0), 1.25, 0.45, seed=seed, n=7)
    T = _grp(P, "tail", (0, 0, 0), (0, -50, 15))
    # along local +x from the buried break to the tail cone
    K.cyl("fuselage", 1.0, 2.8, bone, loc=(0.4, 0, 0), rot=(0, 90, 0), segs=20, bev=0, r2=0.8, parent=T)
    K.cyl("band", 0.93, 0.35, steel, loc=(0.9, 0, 0), rot=(0, 90, 0), segs=20, bev=0, r2=0.915, parent=T)
    K.cyl("cone", 0.8, 2.6, bone, loc=(3.08, 0, 0.12), rot=(0, 85, 0), segs=20, bev=0, r2=0.24, parent=T)
    K.sphere("cone_tip", 0.25, dark, loc=(4.38, 0, 0.23), scale=(1.3, 1, 1), parent=T)
    for k in range(3):
        K.cyl(f"window{k}", 0.12, 0.02, K.mat("darkglass", GLASS, rough=0.15, metal=0.2), loc=(-0.15 + k * 0.55, -0.955 + k * 0.03, 0.25), rot=(90, 0, 0), segs=12,
              bev=0, parent=T)
    fin = [(2.2, 0.55), (3.9, 0.45), (4.55, 2.4), (3.95, 2.45)]
    profile(T, "fin", fin, 0.14, steel, bev=0.02)
    profile(T, "fin_tip", [(3.88, 2.25), (4.5, 2.2), (4.56, 2.45), (3.95, 2.48)], 0.15, M(MUSTARD, wear=0.4, grime=0.5), bev=0.01)
    Rd = _grp(T, "rudder", (3.92, 0, 0.45), (0, -18, 12))
    profile(Rd, "rudder", [(0.0, 0.0), (0.42, 0.0), (0.98, 1.8), (0.62, 1.9)], 0.1, steel, rot=(0, 18, 0), bev=0.015)
    K.cyl("beacon", 0.07, 0.1, K.mat(f"lens{LAMP_RED}", LAMP_RED, rough=0.15), loc=(4.25, 0, 2.5), segs=10, bev=0, parent=T)
    label(T, "CW-A1", (3.4, -0.072, 1.45), 0.26, M(BONE, wear=0.3), rot=(90, 0, 0))
    # tailplanes: the left one whole, the right one torn off short and bent down
    profile(T, "tailplane_l", [(3.2, 0.25), (4.15, 0.25), (4.45, 2.1), (3.95, 2.1)], 0.1, steel, loc=(0, 0, 0.1), rot=(90, 0, 0), bev=0.015)
    Tr = _grp(T, "tailplane_r", (3.4, 0.6, 0.12), (-14, 0, 0))
    profile(Tr, "tailplane_r", [(-0.2, 0.0), (0.75, 0.0), (0.85, 0.7), (0.4, 0.95), (0.1, 0.65)], 0.1, steel, rot=(-90, 0, 0), bev=0.015)
    for k in range(3):
        K.box(f"spar{k}", (0.05, 0.4, 0.05), _rust(), loc=(3.55 + k * 0.25, 1.55 - k * 0.1, 0.12), rot=(-14, 0, rnd.uniform(-10, 10)), bev=0, parent=T)
    # torn skin at the ground and ribs showing
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.2, 0.2)
        K.box(f"shard{k}", (0.6, 0.04, rnd.uniform(0.35, 0.7)), bone if k % 3 else steel,
              loc=(math.cos(a) * 1.15, math.sin(a) * 1.15, 0.15), rot=(rnd.uniform(-35, -10), 0, math.degrees(a) + 90), bev=0.01, parent=P)
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.5
        K.box(f"rib{k}", (0.06, 0.06, 0.8), _rust(), loc=(math.cos(a) * 0.95, math.sin(a) * 0.95, 0.3), rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), 0),
              bev=0, parent=P)
    for k in range(6):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-2.4, 2.4), rnd.uniform(-2.4, 2.4)), rnd, n=5, h=0.4)
    return P


def helicopter_wreck(seed=61):
    """A colony helicopter down on its left side: a bone cabin with a glass nose, steel-blue engine fairing, a tail boom
    with its little rotor, and the main rotor's blades bent and broken against the ground."""
    rnd = random.Random(seed)
    P = _root("helicopter")
    bone = _paint(BONE, wear=0.75, grime=0.65, moss=0.25)
    steel = _paint(STEEL, wear=0.7, grime=0.6)
    dark, glass = _dark(), _glass()
    blade = M(GRAPHITE, metal=0.3, wear=0.25, grime=0.5)
    tip = M(MUSTARD, wear=0.25, grime=0.5)
    Y = _grp(P, "yaw", (0.4, 0.9, 0), (0, 0, 8))
    H = _grp(Y, "heli", (0, 0, 0.95), (90, 0, 0))   # local: x forward, z up from the skid bottoms; its left side is down
    for s in (-1, 1):
        rod(H, f"skid{s}", (-1.0, s * 0.9, 0.05), (1.55, s * 0.9, 0.05), 0.05, dark, segs=8)
        rod(H, f"skid_toe{s}", (1.55, s * 0.9, 0.05), (1.85, s * 0.9, 0.3), 0.05, dark, segs=8)
        for x in (0.9, -0.5):
            rod(H, f"cross{x}{s}", (x, s * 0.9, 0.05), (x, s * 0.5, 0.62), 0.045, dark, segs=8)
    K.slab("cabin", 2.6, 1.6, 1.5, 0.6, bone, loc=(0.2, 0, 1.35), bev=0.08, parent=H)
    K.sphere("nose", 0.82, glass, loc=(1.15, 0, 1.3), scale=(1.05, 0.95, 0.88), parent=H)
    cracks(H, (1.9, -0.25, 1.4), (0, 90, 0), n=4, size=0.5)
    K.box("chin", (0.6, 1.2, 0.2), bone, loc=(1.35, 0, 0.62), bev=0.06, parent=H)
    for s in (-1, 1):
        K.box(f"door_glass{s}", (0.8, 0.02, 0.6), glass, loc=(0.25, s * 0.805, 1.62), bev=0.005, parent=H)
        K.box(f"door_seam{s}", (0.02, 0.015, 1.2), dark, loc=(-0.25, s * 0.805, 1.35), bev=0, parent=H)
        K.box(f"stripe{s}", (2.0, 0.012, 0.12), M(STEEL, wear=0.3), loc=(0.0, s * 0.806, 0.95), bev=0, parent=H)
    label(H, "CW-C3", (-0.55, 0.81, 1.62), 0.2, M(STEEL, wear=0.3), rot=(90, 0, 180))
    K.slab("fairing", 1.7, 1.1, 0.6, 0.3, steel, loc=(-0.35, 0, 2.35), bev=0.06, parent=H)
    FR.vent(H, (-0.6, -0.56, 2.35), 0.6, 0.35, M(GRAPHITE, wear=0.2), dark)
    for s in (-1, 1):
        rod(H, f"exhaust{s}", (-1.05, s * 0.35, 2.4), (-1.35, s * 0.42, 2.45), 0.08, _rust(), segs=8)
    K.cyl("mast", 0.08, 0.4, dark, loc=(0.0, 0, 2.8), segs=8, bev=0, parent=H)
    K.cyl("hub", 0.22, 0.16, M(GUNMETAL, metal=0.5, wear=0.25), loc=(0.0, 0, 3.02), segs=10, bev=0.02, parent=H)
    # tail boom, fins and the tail rotor
    K.cyl("boom", 0.34, 4.4, bone, loc=(-3.05, 0, 1.75), rot=(0, -90, 0), segs=14, bev=0, r2=0.16, parent=H)
    for k in range(3):
        K.cyl(f"boom_band{k}", 0.25 - k * 0.04, 0.2, tip if k % 2 == 0 else dark, loc=(-3.8 - k * 0.45, 0, 1.75), rot=(0, 90, 0), segs=14, bev=0,
              r2=0.24 - k * 0.04, parent=H)
    profile(H, "fin", [(-5.45, 1.65), (-4.9, 1.65), (-5.25, 2.85), (-5.6, 2.85)], 0.07, steel, bev=0.01)
    profile(H, "fin_lo", [(-5.35, 1.6), (-4.95, 1.6), (-5.2, 1.15), (-5.4, 1.15)], 0.07, steel, bev=0.01)
    K.box("stabiliser", (0.42, 1.3, 0.05), steel, loc=(-4.4, 0, 1.78), bev=0.01, parent=H)
    K.cyl("tr_hub", 0.07, 0.12, dark, loc=(-5.3, 0.1, 2.45), rot=(90, 0, 0), segs=8, bev=0, parent=H)
    for k in range(2):
        K.box(f"tr_blade{k}", (0.1, 0.03, 1.15), blade, loc=(-5.3, 0.14, 2.45), rot=(0, 30 + 90 * k, 0), bev=0.005, parent=H)
    # main rotor, in the yaw group's frame: the hub sits at (0, -3.02, 0.95), the blades bent against the ground
    hub = Vector((0.0, -3.02, 0.95))

    def bent(name, pts, short=False):
        for k in range(len(pts) - 1):
            a, b = hub + Vector(pts[k]), hub + Vector(pts[k + 1])
            beam(Y, f"{name}{k}", a, b, 0.05, blade, d=0.3, bev=0.005)
        if not short:
            b = hub + Vector(pts[-1])
            a = hub + Vector(pts[-2])
            tipv = a + (b - a) * 0.88
            beam(Y, f"{name}_tip", tipv, b, 0.06, tip, d=0.31, bev=0.005)
    bent("blade_a", [(0, 0, 0), (1.1, 0, 0.5), (2.5, -0.9, 0.8), (3.4, -1.6, -0.5)])
    bent("blade_b", [(0, 0, 0), (-0.9, 0, 0.5), (-2.4, -0.6, 0.3), (-3.6, -1.0, -0.85)])
    bent("blade_c", [(0, 0, 0), (-1.0, 0, -0.45), (-1.8, -0.2, -0.85), (-3.6, -1.3, -0.88)])
    bent("blade_d", [(0, 0, 0), (0.55, 0, -0.35)], short=True)
    beam(Y, "blade_d_piece", (2.6, -4.6, 0.05), (4.6, -5.2, 0.06), 0.05, blade, d=0.3, bev=0.005)
    beam(Y, "blade_d_tip", (4.4, -5.14, 0.06), (4.6, -5.2, 0.06), 0.06, tip, d=0.31, bev=0.005)
    mound(P, "gouge", (0.6, -1.6), 0.9, 0.25, seed=seed, n=5, sx=1.8)
    for k in range(6):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-4.0, 4.0), rnd.uniform(-2.0, 2.0)), rnd, n=5, h=0.4)
    return P


def small_plane_wreck(seed=62):
    """A colony light plane nosed over in a field: bone fuselage and steel-blue cowl, the left wing broken off at the
    root and lying beside it, the nose gear folded, the propeller bent back."""
    rnd = random.Random(seed)
    P = _root("lightplane")
    bone = _paint(BONE, wear=0.75, grime=0.65, moss=0.25)
    steel = _paint(STEEL, wear=0.7, grime=0.6)
    dark, glass = _dark(), _glass()
    tipm = M(MUSTARD, wear=0.3, grime=0.5)
    Pv = _grp(P, "pivot", (0.3, 0.6, 0.0), (3.0, 11.0, -8))
    G = _grp(Pv, "plane", (-1.2, 0, 0))
    zc = 1.0
    cab = K.cyl("cabin", 0.62, 2.2, bone, loc=(1.3, 0, zc), rot=(0, 90, 0), segs=16, bev=0, r2=0.6, parent=G)
    cab.scale = (1.15, 0.85, 1)
    tail = K.cyl("tailcone", 0.6, 4.3, bone, loc=(-1.95, 0, zc + 0.12), rot=(0, -87, 0), segs=16, bev=0, r2=0.14, parent=G)
    tail.scale = (1.15, 0.85, 1)
    cowl = K.cyl("cowl", 0.62, 0.85, steel, loc=(2.82, 0, zc - 0.02), rot=(0, 90, 0), segs=16, bev=0.02, r2=0.42, parent=G)
    cowl.scale = (1.1, 0.85, 1)
    K.cyl("spinner", 0.17, 0.3, dark, loc=(3.38, 0, zc - 0.02), rot=(0, 90, 0), segs=12, bev=0, r2=0.03, parent=G)
    K.sphere("canopy", 0.7, glass, loc=(1.35, 0, zc + 0.42), scale=(1.25, 0.72, 0.6), parent=G)
    cracks(G, (1.9, -0.25, zc + 0.6), (0, 60, 0), n=3, size=0.4)
    for s in (-1, 1):
        K.box(f"cheatline{s}", (3.0, 0.012, 0.1), M(STEEL, wear=0.3), loc=(-0.4, s * 0.462, zc + 0.05), rot=(0, -2, 0), bev=0, parent=G)
    # right wing whole, left wing broken off at the root
    K.box("wing_r", (1.45, 4.6, 0.14), bone, loc=(1.25, 2.55, zc - 0.42), rot=(-4, 0, 0), bev=0.04, parent=G)
    K.box("wing_r_tip", (1.3, 0.15, 0.15), tipm, loc=(1.25, 4.88, zc - 0.26), rot=(-4, 0, 0), bev=0.03, parent=G)
    K.box("aileron_r", (0.35, 1.6, 0.06), steel, loc=(0.4, 3.6, zc - 0.36), rot=(-4, 8, 0), bev=0.01, parent=G)
    K.cyl("nav_r", 0.05, 0.06, K.mat(f"lens{LAMP_GREEN}", LAMP_GREEN, rough=0.15), loc=(1.9, 4.9, zc - 0.25), rot=(0, 90, 0), segs=8, bev=0, parent=G)
    K.box("wing_l_root", (1.45, 0.8, 0.14), bone, loc=(1.25, -0.65, zc - 0.42), bev=0.03, parent=G)
    for k in range(4):
        K.box(f"spar_l{k}", (0.06, 0.35, 0.06), _rust(), loc=(0.8 + k * 0.3, -1.15, zc - 0.42), rot=(rnd.uniform(-20, 20), 0, rnd.uniform(-15, 15)), bev=0,
              parent=G)
    Wl = _grp(P, "wing_l", (0.6, -2.7, 0.0), (0, 0, 28))
    Wf = _grp(Wl, "wing_l_tilt", (0, 0, 0), (-12, 6, 0))
    K.box("wing_l", (1.45, 3.8, 0.14), bone, loc=(0, -0.2, 0.42), bev=0.04, parent=Wf)
    K.box("wing_l_tip", (1.3, 0.15, 0.15), tipm, loc=(0, -2.15, 0.42), bev=0.03, parent=Wf)
    K.box("aileron_l", (0.35, 1.6, 0.06), steel, loc=(-0.85, -1.2, 0.44), rot=(0, -20, 0), bev=0.01, parent=Wf)
    for k in range(3):
        K.box(f"spar_end{k}", (0.06, 0.3, 0.06), _rust(), loc=(-0.3 + k * 0.35, 1.75, 0.42), rot=(0, 0, rnd.uniform(-20, 20)), bev=0, parent=Wf)
    # tail feathers
    profile(G, "fin", [(-4.25, zc + 0.25), (-3.25, zc + 0.3), (-3.75, zc + 1.4), (-4.4, zc + 1.4)], 0.08, steel, bev=0.012)
    profile(G, "fin_tip", [(-3.78, zc + 1.3), (-4.38, zc + 1.3), (-4.4, zc + 1.42), (-3.75, zc + 1.42)], 0.09, tipm, bev=0.005)
    label(G, "CW-P4", (-3.9, -0.045, zc + 0.75), 0.18, M(BONE, wear=0.3), rot=(90, 0, 0))
    K.box("tailplane", (0.65, 2.6, 0.06), steel, loc=(-3.95, 0, zc + 0.22), bev=0.01, parent=G)
    # gear: mains on, nose leg folded under
    for s in (-1, 1):
        rod(G, f"main_leg{s}", (1.0, s * 0.4, zc - 0.48), (1.0, s * 1.05, 0.26), 0.04, dark, segs=8)
        tyre(G, f"main{s}", (1.0, s * 1.12, 0.26), 0.26, 0.14, rim=M(BONE, metal=0.3, wear=0.2, grime=0.5))
    rod(G, "nose_leg", (2.75, 0, zc - 0.5), (2.3, 0, zc - 0.62), 0.04, dark, segs=8)
    lying_wheel(P, "nose_wheel", (3.9, -1.0, 0), 0.2, 0.1, tilt=(4, 0))
    # propeller: one blade bent back where it hit, the other straight up
    Pr = _grp(G, "prop", (3.5, 0, zc - 0.02), (0, 0, 0))
    beam(Pr, "prop_up", (0, 0, 0), (-0.05, 0.05, 0.9), 0.04, dark, d=0.14)
    beam(Pr, "prop_dn", (0, 0, 0), (0.0, 0, -0.45), 0.04, dark, d=0.14)
    beam(Pr, "prop_dn2", (0.0, 0, -0.45), (-0.45, -0.1, -0.7), 0.04, dark, d=0.14)
    rusty(G, rnd, 2, (-1.5, 0.3), (zc - 0.1, zc + 0.2), -0.47)
    mound(P, "furrow", (2.6, 0.8), 0.8, 0.25, seed=seed, n=4, sx=1.4)
    for k in range(6):
        grass_tuft(P, f"grass{k}", (rnd.uniform(-4.0, 4.0), rnd.uniform(-3.0, 3.0)), rnd, n=5, h=0.4)
    return P


# ================================================================== loose parts
def wheel_loose_big(seed=63):
    """A giant tyre off a colony mining hauler, lying on its side in the grass on its mustard rim, one edge propped on a
    stone, grass grown up round it."""
    rnd = random.Random(seed)
    P = _root("bigwheel")
    rim = M(MUSTARD, metal=0.3, wear=0.5, grime=0.6)
    R, Wd = 1.3, 0.75
    lump(P, "stone", 0.32, (0.95, 0.3, 0.08), (1.3, 1.0, 0.6), K.mat("stone", "#8f9289", rough=0.9, grime=0.35, moss=1.0, cracks=0.5, noise_scale=3.0), rnd)
    G = lying_wheel(P, "tyre", (0, 0, 0.12), R, Wd, tilt=(0, -7), rim=rim, lugs=18)
    for k in range(10):
        a = 2 * math.pi * k / 10
        K.cyl(f"nut{k}", 0.04, Wd + 0.05, M(ZINC, metal=0.6, wear=0.3), loc=(math.cos(a) * R * 0.35, 0, math.sin(a) * R * 0.35), rot=(90, 0, 0), segs=6,
              bev=0, parent=G)
    K.cyl("valve", 0.03, 0.12, _dark(), loc=(R * 0.5, Wd / 2 + 0.05, 0.2), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    rust_patch(G, (0.2, Wd / 2 + 0.012, -0.4), (0.3, 0.004, 0.15))
    for k in range(8):
        a = 2 * math.pi * k / 8 + rnd.uniform(-0.2, 0.2)
        grass_tuft(P, f"grass{k}", (math.cos(a) * (R + 0.08), math.sin(a) * (R + 0.08)), rnd, n=5, h=0.45)
    return P


def engine_block(seed=64):
    """A colony car engine pulled and left on a pallet: cast block and head, a steel-blue rocker cover, manifolds, the
    pulleys on the front and the bell housing at the back, an oil stain on the boards under it."""
    rnd = random.Random(seed)
    P = _root("engineblock")
    for k, x in enumerate((-0.53, 0.0, 0.53)):
        plank(P, f"bottom{k}", (0.14, 1.0, 0.022), (x, 0, 0.011), shade=k)
        for j, y in enumerate((-0.43, 0.0, 0.43)):
            plank(P, f"block{k}{j}", (0.14, 0.12, 0.078), (x, y, 0.061), shade=3)
        plank(P, f"stringer{k}", (0.14, 1.0, 0.022), (x, 0, 0.111), shade=1)
    for k in range(5):
        plank(P, f"deck{k}", (1.2, 0.14, 0.022), (0, -0.43 + k * 0.215, 0.133), rot=(0, 0, rnd.uniform(-0.8, 0.8)), shade=k + 1)
    K.box("oil_stain", (0.6, 0.5, 0.004), K.mat("oil", "#1d1a17", rough=0.3), loc=(0.1, 0.05, 0.145), rot=(0, 0, 20), bev=0, parent=P)
    cast = M(GUNMETAL, metal=0.5, wear=0.3, grime=0.7, rough=0.6)
    dark, rust = _dark(), _rust()
    E = _grp(P, "engine", (0.0, 0.0, 0.144), (0, 0, 10))
    K.box("sump", (0.6, 0.32, 0.12), dark, loc=(0, 0, 0.07), bev=0.02, parent=E)
    K.box("block", (0.72, 0.42, 0.42), cast, loc=(0, 0, 0.34), bev=0.025, parent=E)
    for s in (-1, 1):
        for k in range(3):
            K.box(f"web{s}{k}", (0.04, 0.03, 0.36), cast, loc=(-0.24 + k * 0.24, s * 0.215, 0.32), bev=0, parent=E)
        rust_patch(E, (rnd.uniform(-0.2, 0.2), s * 0.212, 0.28), (0.14, 0.004, 0.1))
    K.box("head", (0.7, 0.38, 0.14), cast, loc=(0, 0, 0.62), bev=0.02, parent=E)
    K.slab("rocker_cover", 0.66, 0.28, 0.1, 0.06, M(STEEL, wear=0.4, grime=0.6), loc=(0, 0, 0.74), bev=0.02, parent=E)
    for x in (-0.25, -0.08, 0.08, 0.25):
        K.cyl(f"cover_bolt{x}", 0.015, 0.02, M(ZINC, metal=0.6), loc=(x, 0.1, 0.79), segs=6, bev=0, parent=E)
        rod(E, f"exhaust_runner{x}", (x, -0.19, 0.6), (x * 0.6, -0.3, 0.46), 0.03, rust, segs=8)
        rod(E, f"intake_runner{x}", (x, 0.19, 0.62), (x, 0.3, 0.75), 0.028, M(ZINC, metal=0.5, wear=0.2, grime=0.6), segs=8)
        K.cyl(f"plug{x}", 0.012, 0.06, _rubber(), loc=(x, -0.12, 0.8), segs=6, bev=0, parent=E)
    K.cyl("exhaust_log", 0.05, 0.45, rust, loc=(0, -0.3, 0.45), rot=(0, 90, 0), segs=10, bev=0, parent=E)
    rod(E, "downpipe", (-0.22, -0.3, 0.45), (-0.3, -0.3, 0.22), 0.045, rust)
    K.box("plenum", (0.66, 0.12, 0.12), M(ZINC, metal=0.5, wear=0.2, grime=0.6), loc=(0, 0.32, 0.8), bev=0.03, parent=E)
    K.cyl("oil_cap", 0.03, 0.03, M(MUSTARD, wear=0.2), loc=(-0.2, -0.05, 0.805), segs=10, bev=0, parent=E)
    # front: timing cover, pulleys and a belt; back: the bell housing
    K.box("timing", (0.06, 0.34, 0.38), cast, loc=(0.385, 0, 0.42), bev=0.015, parent=E)
    for nm, y, z, r in (("crank_pulley", 0.0, 0.2, 0.1), ("pump_pulley", 0.0, 0.5, 0.07), ("alt_pulley", 0.2, 0.52, 0.05)):
        K.cyl(nm, r, 0.04, dark, loc=(0.44, y, z), rot=(0, 90, 0), segs=14, bev=0, parent=E)
    K.cyl("alternator", 0.08, 0.14, M(ZINC, metal=0.5, wear=0.2, grime=0.6), loc=(0.37, 0.2, 0.52), rot=(0, 90, 0), segs=12, bev=0.01, parent=E)
    for a, b in (((0.445, -0.1, 0.2), (0.445, -0.07, 0.5)), ((0.445, 0.1, 0.2), (0.445, 0.25, 0.52))):
        rod(E, "belt", a, b, 0.012, _rubber(), segs=4)
    K.cyl("bell", 0.3, 0.16, cast, loc=(-0.44, 0, 0.36), rot=(0, 90, 0), segs=16, bev=0.01, r2=0.24, parent=E)
    K.cyl("dipstick", 0.006, 0.4, M(ZINC, metal=0.6), loc=(0.2, 0.24, 0.6), rot=(0, 8, 0), segs=4, bev=0, parent=E)
    K.torus("dipstick_ring", 0.03, 0.007, M(MUSTARD, wear=0.2), loc=(0.23, 0.24, 0.8), rot=(90, 0, 0), segs=10, parent=E)
    label(E, "CW-4", (0, -0.141, 0.74), 0.06, M(BONE, wear=0.2), rot=(90, 0, 0))
    return P


def car_seat(seed=65):
    """A car seat out of some colony runabout, left standing on its rails in the grass: graphite cloth with steel-blue
    inserts, a split in the cushion with the foam showing, the back tipped over a little."""
    rnd = random.Random(seed)
    P = _root("carseat")
    cloth = _cloth()
    insert = K.mat("seatcloth_b", STEEL, rough=0.9, grime=0.45)
    foam = K.mat("foam", FOAM, rough=0.95, grime=0.3)
    dark = _dark()
    S = _grp(P, "seat", (0, 0, 0), (3, -4, 20))
    for x in (-0.18, 0.18):
        K.box(f"rail{x}", (0.04, 0.52, 0.03), dark, loc=(x, 0, 0.015), bev=0, parent=S)
    K.box("frame", (0.42, 0.42, 0.08), dark, loc=(0, 0, 0.08), bev=0.01, parent=S)
    K.box("cushion", (0.52, 0.52, 0.14), cloth, loc=(0, -0.03, 0.19), bev=0.04, parent=S)
    K.box("cushion_insert", (0.3, 0.42, 0.01), insert, loc=(0, -0.05, 0.262), bev=0, parent=S)
    for s in (-1, 1):
        K.box(f"bolster{s}", (0.1, 0.48, 0.12), cloth, loc=(s * 0.24, -0.03, 0.29), bev=0.04, parent=S)
    K.box("split", (0.16, 0.12, 0.012), foam, loc=(0.06, -0.12, 0.264), rot=(0, 0, 25), bev=0, parent=S)
    K.box("lever", (0.03, 0.18, 0.03), dark, loc=(0.29, 0.1, 0.14), bev=0, parent=S)
    rod(S, "buckle_stalk", (-0.29, 0.2, 0.14), (-0.31, 0.15, 0.36), 0.015, dark, segs=6)
    K.box("buckle", (0.05, 0.03, 0.08), M(ZINC, metal=0.6, wear=0.2), loc=(-0.31, 0.15, 0.39), bev=0.008, parent=S)
    Bk = _grp(S, "back", (0, 0.24, 0.22), (-24, 0, 0))
    K.box("back", (0.52, 0.14, 0.68), cloth, loc=(0, 0, 0.38), bev=0.045, parent=Bk)
    K.box("back_insert", (0.3, 0.01, 0.5), insert, loc=(0, -0.071, 0.38), bev=0, parent=Bk)
    for s in (-1, 1):
        K.box(f"back_bolster{s}", (0.1, 0.18, 0.6), cloth, loc=(s * 0.24, -0.03, 0.36), bev=0.04, parent=Bk)
        rod(Bk, f"head_post{s}", (s * 0.08, 0, 0.7), (s * 0.08, 0, 0.8), 0.008, M(ZINC, metal=0.6), segs=6)
    K.box("headrest", (0.28, 0.12, 0.2), cloth, loc=(0, 0, 0.88), bev=0.04, parent=Bk)
    K.box("back_split", (0.1, 0.012, 0.18), foam, loc=(-0.08, -0.072, 0.5), rot=(0, 15, 0), bev=0, parent=Bk)
    for k in range(7):
        a = 2 * math.pi * k / 7 + rnd.uniform(-0.3, 0.3)
        grass_tuft(P, f"grass{k}", (math.cos(a) * 0.42, math.sin(a) * 0.42), rnd, n=6, h=0.35)
    return P


PROPS = {
    "van_wreck": van_wreck,
    "pickup_wreck": pickup_wreck,
    "sports_car_wreck": sports_car_wreck,
    "delivery_truck_wreck": delivery_truck_wreck,
    "tanker_trailer_wreck": tanker_trailer_wreck,
    "flatbed_trailer": flatbed_trailer,
    "motorbike_wreck": motorbike_wreck,
    "bicycle_rusty": bicycle_rusty,
    "train_wagon_wreck": train_wagon_wreck,
    "rail_track_section": rail_track_section,
    "rail_signal": rail_signal,
    "rail_buffer": rail_buffer,
    "mine_cart": mine_cart,
    "golf_cart_wreck": golf_cart_wreck,
    "quad_bike_wreck": quad_bike_wreck,
    "boat_trailer": boat_trailer,
    "harvester_combine_wreck": harvester_combine_wreck,
    "colony_rover_wreck": colony_rover_wreck,
    "hovercraft_wreck": hovercraft_wreck,
    "airplane_tail": airplane_tail,
    "helicopter_wreck": helicopter_wreck,
    "small_plane_wreck": small_plane_wreck,
    "wheel_loose_big": wheel_loose_big,
    "engine_block": engine_block,
    "car_seat": car_seat,
}
