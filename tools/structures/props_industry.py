"""Colony Works industry props for the scenes between places (docs/world-generation.md, Scenes): the old colony's plant,
yards and heavy machines, abandoned where they stood when the colony fell (docs/art-direction.md).

Process plant: a tall storage tank with its caged ladder, a steel silo that reads from the air, a small cooling tower,
a pipe rack, a pipe bridge on two towers, a valve manifold, a pump house, a skid compressor, a guyed steel chimney and
a brick smokestack. Material handling: a conveyor up to a hopper tower, a raised hopper and a jaw crusher. Heavy
plant, wrecked: a mixer drum off its truck, a mobile crane with its boom down, an excavator, a bulldozer and a haul
truck with its bed up. Yard stock: beams, rebar, a scaffold tower, cable drums, a drum pallet, spilled chemical drums
and a sealed waste bin.

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
from families import M, shell_panel, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, BRICK, RUST, ZINC, CONCRETE, STONE, STONE_D
from props import _root, wood, plank, bolt
from props_colony_a import stencil, nut, _empty
from props_colony_b import beam, rod, cable, tyre, rust_patch, lathe, plate_xz, _between

TYRE, GLASS, CABLE = "#26282a", "#22303a", "#2b2d30"
SPILL = "#1f3a26"       # the dark green chemical that leaked from the drums
SOOT = "#221f1c"        # soot round a flue's mouth
GRAVEL = "#8a8579"      # crushed stone
SLURRY = "#9b978c"      # set concrete spilled from the mixer


# ---------------------------------------------------------------- materials
def _bone(**kw):
    kw.setdefault("wear", 0.25)
    kw.setdefault("grime", 0.5)
    return M(BONE, **kw)


def _steel(wear=0.3):
    return M(STEEL, metal=0.4, wear=wear, grime=0.45)


def _dark():
    return M(GRAPHITE, metal=0.4, wear=0.3, grime=0.4)


def _zinc():
    return M(ZINC, metal=0.6, wear=0.3, grime=0.4)


def _rust():
    return M(RUST, metal=0.4, wear=0.3, grime=0.5, rough=0.85)


def _concrete(cracks=0.2):
    return K.mat("concrete", CONCRETE, rough=0.9, wear=0.5, grime=0.6, cracks=cracks, moss=0.15)


def _glass():
    return K.mat("darkglass", GLASS, rough=0.15, metal=0.2)


def _tyre():
    return K.mat("tyre", TYRE, rough=0.9, grime=0.4)


# ---------------------------------------------------------------- helpers
def pipe(P, name, a, b, r, mat, segs=16):
    """A smooth-shaded pipe from a to b."""
    L = (Vector(b) - Vector(a)).length
    ob = K.cyl(name, r, L, mat, segs=segs, bev=0.01, parent=P)
    return _between(ob, a, b)


def flange(P, loc, r, rot, mat=None, w=0.07):
    """A pipe flange (rot (0, 90, 0) for a pipe along x, (90, 0, 0) along y, (0, 0, 0) vertical)."""
    return K.cyl("flange", r + 0.06, w, mat or _steel(), loc=loc, rot=rot, segs=14, bev=0, parent=P)


def hazard(P, loc, w, h, a_mat, b_mat, n=6, axis="y"):
    """FR.hazard's stripes without bevels: they are paint, and a bevel on each costs more than the stripe."""
    x, y, z = loc
    step = w / n
    for k in range(n):
        m = a_mat if k % 2 == 0 else b_mat
        if axis == "y":
            K.box("haz", (step, 0.03, h), m, loc=(x - w / 2 + step * (k + 0.5), y, z), bev=0, parent=P)
        else:
            K.box("haz", (0.03, step, h), m, loc=(x, y - w / 2 + step * (k + 0.5), z), bev=0, parent=P)


def cased_box(P, name, center, size, panel, frame, fw=0.08):
    """A cheaper framed case for vehicle cabs and cabinets: a bevelled panel box, corner posts and top and bottom rims."""
    cx, cy, cz = center
    sx, sy, sz = size
    K.box(f"{name}_core", (sx - 0.02, sy - 0.02, sz), panel, loc=center, bev=0.02, parent=P)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box(f"{name}_post", (fw, fw, sz), frame, loc=(cx + a * (sx / 2 - fw / 2 + 0.01), cy + b * (sy / 2 - fw / 2 + 0.01), cz), bev=0.01, parent=P)
    for z in (cz - sz / 2 + fw / 2, cz + sz / 2 - fw / 2):
        K.box(f"{name}_rim", (sx + 0.02, sy + 0.02, fw), frame, loc=(cx, cy, z), bev=0.01, parent=P)


def lump(P, name, s, mat, loc, rnd, flat=0.6):
    """A rough stone or clod: a jittered box, its corners knocked off."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= s * 2 * rnd.uniform(0.75, 1.25)
        v.co.y *= s * 2 * rnd.uniform(0.75, 1.25)
        v.co.z *= s * 2 * flat * rnd.uniform(0.8, 1.2)
    ob = K.from_bm(name, bm, mat)
    K.bevel(ob, s * 0.35, segs=2, angle=30)
    return K.place(ob, loc, (rnd.uniform(-10, 10), rnd.uniform(-10, 10), rnd.uniform(0, 180)), P)


def elbow(P, name, centre, R, r, mat, rot, arc=90):
    """A pipe bend: a torus arc of bend radius R about centre (the arc starts on local +x and turns towards local +y)."""
    return K.torus(name, R, r, mat, loc=centre, rot=rot, arc=arc, segs=24, parent=P)


def ring_text(P, text, r, z, a0, size, mat, spacing=0.62, extrude=0.008):
    """Stencil text painted round a cylinder of radius r, one letter at a time so none stands off the curve; a0 is the
    angle (radians) of the middle letter, -pi/2 being the front."""
    step = size * spacing / r
    n = len(text)
    for i, ch in enumerate(text):
        if ch == " ":
            continue
        a = a0 + (i - (n - 1) / 2) * step
        stencil(P, ch, (r * math.cos(a), r * math.sin(a), z), size, mat, rot=(90, 0, math.degrees(a) + 90), extrude=extrude)


def on_ring(r, a, z):
    return (r * math.cos(a), r * math.sin(a), z)


def yaw_out(a):
    """The yaw that turns a part's -Y face outwards at angle a round a cylinder."""
    return math.degrees(a) + 90


def ladder(P, loc, yaw, z0, z1, cage_from=2.4, standoff=0.2, mat=None, top_ext=1.0):
    """A fixed steel ladder on brackets: its back towards local +Y (the wall), a safety cage from cage_from up."""
    G = _empty(P, "ladder", (loc[0], loc[1], 0), (0, 0, yaw))
    m = mat or _zinc()
    h = z1 - z0 + top_ext
    for s in (-1, 1):
        K.box("lad_rail", (0.06, 0.05, h), m, loc=(s * 0.22, 0, z0 + h / 2), bev=0, parent=G)
    for k in range(1, int((z1 - z0) / 0.3) + 1):
        K.box("rung", (0.44, 0.03, 0.03), m, loc=(0, 0, z0 + k * 0.3), bev=0, parent=G)
    z = z0 + 0.6
    while z < z1 + top_ext:
        for s in (-1, 1):
            K.box("lad_bracket", (0.05, standoff, 0.05), m, loc=(s * 0.22, standoff / 2, z), bev=0, parent=G)
        z += 2.0
    if cage_from is not None and z1 - z0 > cage_from + 0.5:
        zc = z0 + cage_from
        hoops = []
        z = zc
        while z <= z1 + top_ext - 0.1:
            hoops.append(z)
            z += 1.1
        for z in hoops:
            K.torus("hoop", 0.36, 0.014, m, loc=(0, -0.3, z), rot=(0, 0, 180), arc=180, segs=16, parent=G)
            for s in (-1, 1):
                K.box("hoop_tie", (0.15, 0.3, 0.03), m, loc=(s * 0.29, -0.15, z), rot=(0, 0, s * -25), bev=0, parent=G)
        for a in (215, 270, 325):
            x, y = 0.36 * math.cos(math.radians(a)), -0.3 + 0.36 * math.sin(math.radians(a))
            K.box("cage_strap", (0.04, 0.01, hoops[-1] - hoops[0]), m, loc=(x, y, (hoops[0] + hoops[-1]) / 2), rot=(0, 0, a - 270), bev=0, parent=G)
    return G


def frustum(name, small, big, h, mat, loc=(0, 0, 0), rot=(0, 0, 0), parent=None, open_top=True, t=0.05):
    """A hopper: a square (or oblong) funnel from small (w, d) at its foot to big (w, d) at its mouth h higher,
    with walls t thick, open at the top."""
    bm = bmesh.new()
    q = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    (w0, d0), (w1, d1) = small, big
    ob_ = [bm.verts.new((x * w0 / 2, y * d0 / 2, 0)) for x, y in q]
    ot = [bm.verts.new((x * w1 / 2, y * d1 / 2, h)) for x, y in q]
    ib = [bm.verts.new((x * (w0 / 2 - t), y * (d0 / 2 - t), t)) for x, y in q]
    it = [bm.verts.new((x * (w1 / 2 - t), y * (d1 / 2 - t), h)) for x, y in q]
    bm.faces.new(list(reversed(ob_)))
    bm.faces.new(ib)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((ob_[i], ob_[j], ot[j], ot[i]))
        bm.faces.new((ib[j], ib[i], it[i], it[j]))
        if open_top:
            bm.faces.new((ot[i], ot[j], it[j], it[i]))
    if not open_top:
        bm.faces.new(ot)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    K.bevel(ob, 0.01, segs=2)
    return K.place(ob, loc, rot, parent)


def hcol(P, name, x, y, z0, h, mat, H=0.24, F=0.22, rot=0):
    """A wide-flange steel column (flanges facing x when rot is 0)."""
    G = _empty(P, name, (x, y, z0), (0, 0, rot))
    K.box(f"{name}_web", (H - 0.04, 0.02, h), mat, loc=(0, 0, h / 2), bev=0, parent=G)
    for s in (-1, 1):
        K.box(f"{name}_fl", (0.025, F, h), mat, loc=(s * (H / 2 - 0.0125), 0, h / 2), bev=0, parent=G)
    return G


def ibeam(P, name, L, loc, rot=(0, 0, 0), H=0.3, F=0.15, mat=None):
    """An I-beam along local x, its bottom flange on local z 0."""
    G = _empty(P, name, loc, rot)
    m = mat or _steel()
    tf, tw = 0.02, 0.014
    K.box(f"{name}_web", (L, tw, H - 2 * tf), m, loc=(0, 0, H / 2), bev=0.003, parent=G)
    for z in (tf / 2, H - tf / 2):
        K.box(f"{name}_fl", (L, F, tf), m, loc=(0, 0, z), bev=0.005, parent=G)
    return G


def gate_valve(P, loc, axis="y", r=0.1, wheel=None, h=0.45):
    """A gate valve on a pipe of radius r along x or y: flanged body, bonnet and yoke up, a handwheel on top."""
    x, y, z = loc
    rot = (90, 0, 0) if axis == "y" else (0, 90, 0)
    body = _steel(0.25)
    K.cyl("vbody", r * 1.5, r * 3.0, body, loc=loc, rot=rot, segs=12, bev=0.01, parent=P)
    for s in (-1, 1):
        off = (0, s * r * 1.6, 0) if axis == "y" else (s * r * 1.6, 0, 0)
        K.cyl("vflange", r * 1.8, 0.05, body, loc=(x + off[0], y + off[1], z), rot=rot, segs=12, bev=0, parent=P)
    K.box("bonnet", (r * 1.6, r * 1.6, h * 0.5), body, loc=(x, y, z + h * 0.3), bev=0.012, parent=P)
    K.box("yoke", (r * 1.8, 0.04, h * 0.45), _dark(), loc=(x, y, z + h * 0.75), bev=0, parent=P)
    K.cyl("stem", 0.015, h * 0.6, _zinc(), loc=(x, y, z + h * 0.9), segs=6, bev=0, parent=P)
    wm = wheel or M(SIGNAL, metal=0.3, wear=0.25, grime=0.3)
    wr = max(0.14, r * 1.9)
    K.torus("handwheel", wr, 0.02, wm, loc=(x, y, z + h + 0.05), segs=12, parent=P)
    for k in range(3):
        K.box("wspoke", (wr * 2, 0.025, 0.02), wm, loc=(x, y, z + h + 0.05), rot=(0, 0, k * 60), bev=0, parent=P)
    K.cyl("whub", 0.035, 0.05, wm, loc=(x, y, z + h + 0.05), segs=8, bev=0, parent=P)


def gauge(P, loc, rot=(90, 0, 0), r=0.07):
    """A round pressure gauge: steel case, bone face, graphite needle."""
    G = _empty(P, "gauge", loc, rot)
    K.cyl("gauge_case", r, 0.05, _steel(0.2), segs=12, bev=0.008, parent=G)
    K.cyl("gauge_face", r * 0.82, 0.01, M(BONE, wear=0.1, grime=0.2), loc=(0, 0, 0.026), segs=12, bev=0, parent=G)
    K.box("gauge_needle", (r * 0.7, 0.01, 0.006), _dark(), loc=(r * 0.2, r * 0.15, 0.032), rot=(0, 0, 35), bev=0, parent=G)
    return G


def drum(P, name, loc, rot=(0, 0, 0), col=STEEL, lid=None, wear=0.25):
    """A 200 litre steel drum, its origin at its centre (stood up, loc z is 0.44; lying, 0.31)."""
    G = _empty(P, name, loc, rot)
    paint = M(col, metal=0.35, wear=wear, grime=0.45, scratches=0.4)
    K.cyl(f"{name}_body", 0.29, 0.88, paint, segs=18, bev=0.015, parent=G)
    for z in (-0.15, 0.15):
        K.cyl(f"{name}_hoop", 0.297, 0.025, paint, loc=(0, 0, z), segs=18, bev=0, parent=G)
    K.cyl(f"{name}_chime", 0.294, 0.03, paint, loc=(0, 0, 0.43), segs=18, bev=0, parent=G)
    K.cyl(f"{name}_lid", 0.27, 0.012, lid or paint, loc=(0, 0, 0.446), segs=18, bev=0, parent=G)
    K.cyl(f"{name}_bung", 0.035, 0.03, _dark(), loc=(0.16, 0.05, 0.455), segs=8, bev=0, parent=G)
    K.cyl(f"{name}_vent", 0.022, 0.025, _dark(), loc=(-0.17, -0.03, 0.453), segs=8, bev=0, parent=G)
    return G


def hazard_diamond(P, loc, rot, s=0.2, mark="!"):
    """A hazard label: a mustard diamond with a graphite border and an exclamation mark (or a drop), facing local -Y."""
    G = _empty(P, "diamond", loc, rot)
    h = s / 2
    plate_xz("diamond_edge", [(0, -h), (h, 0), (0, h), (-h, 0)], 0.004, _dark(), parent=G, bev=0)
    h2 = h * 0.82
    plate_xz("diamond", [(0, -h2), (h2, 0), (0, h2), (-h2, 0)], 0.004, M(MUSTARD, wear=0.1, grime=0.3), loc=(0, -0.003, 0), parent=G, bev=0)
    if mark == "!":
        K.box("mark", (s * 0.08, 0.004, s * 0.3), _dark(), loc=(0, -0.006, s * 0.06), bev=0, parent=G)
        K.box("mark_dot", (s * 0.08, 0.004, s * 0.08), _dark(), loc=(0, -0.006, -s * 0.19), bev=0, parent=G)
    else:
        plate_xz("mark", [(0, s * 0.25), (s * 0.12, 0), (s * 0.08, -s * 0.12), (-s * 0.08, -s * 0.12), (-s * 0.12, 0)], 0.004, _dark(),
                 loc=(0, -0.006, 0), parent=G, bev=0)
    return G


def track(P, name, xc, y, L, H, w, frame_mat, seed=0, broken=False):
    """A crawler track along x on the ground: the steel-shoed belt round idler and sprocket, rollers under the frame."""
    rnd = random.Random(seed)
    G = _empty(P, name, (xc, y, 0))
    belt = M(GRAPHITE, metal=0.4, wear=0.3, grime=0.6, rough=0.8)
    pts = K.rounded_rect(L, H - 0.04, (H - 0.04) / 2 - 0.01, segs=6)
    plate_xz(f"{name}_belt", pts, w, belt, loc=(0, 0, H / 2 + 0.02), parent=G, bev=0.02)
    n = int((L - H) / 0.2)
    for k in range(n + 1):
        x = -(L - H) / 2 + k * (L - H) / n
        K.box(f"{name}_shoe", (0.08, w + 0.04, 0.05), belt, loc=(x, 0, 0.025), bev=0, parent=G)
        if not (broken and k > n - 3):
            K.box(f"{name}_shoe_t", (0.08, w + 0.04, 0.05), belt, loc=(x + 0.1, 0, H - 0.005), bev=0, parent=G)
    for s in (-1, 1):
        K.cyl(f"{name}_idler", H * 0.36, w + 0.06, frame_mat, loc=(s * (L - H) / 2, 0, H / 2 + 0.02), rot=(90, 0, 0), segs=14, bev=0.01, parent=G)
        K.cyl(f"{name}_hub", H * 0.12, w + 0.1, _dark(), loc=(s * (L - H) / 2, 0, H / 2 + 0.02), rot=(90, 0, 0), segs=8, bev=0, parent=G)
    K.box(f"{name}_frame", (L - H * 0.9, w + 0.08, H * 0.38), frame_mat, loc=(0, 0, H / 2 + 0.02), bev=0.02, parent=G)
    for k in range(4):
        x = -(L - H) / 2 + (k + 0.5) * (L - H) / 4
        K.cyl(f"{name}_roller", 0.11, w + 0.1, _dark(), loc=(x, 0, 0.17), rot=(90, 0, 0), segs=10, bev=0, parent=G)
    return G


def _pallet(P, loc, rnd):
    """A plain 1.2 x 1.0 m pallet, its deck top 0.144 m up."""
    G = _empty(P, "pallet", loc)
    W = 1.0
    for k, x in enumerate((-0.53, 0.0, 0.53)):
        plank(G, f"bottom{k}", (0.14, W, 0.022), (x, 0, 0.011), shade=k)
        for j, y in enumerate((-0.43, 0.0, 0.43)):
            plank(G, f"block{k}{j}", (0.14, 0.12, 0.078), (x, y, 0.061), shade=3)
        plank(G, f"stringer{k}", (0.14, W, 0.022), (x, 0, 0.111), shade=1)
    for k in range(5):
        plank(G, f"deck{k}", (1.2, 0.14, 0.022), (0, -0.43 + k * 0.215, 0.133), rot=(0, 0, rnd.uniform(-0.8, 0.8)), shade=k + 1)
    return G


def rubble(P, name, loc, r, n, rnd, mat, flat=0.55, z=0.0):
    """A heap of loose stones or gravel lumps."""
    x0, y0 = loc
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        d = r * math.sqrt(rnd.random())
        s = rnd.uniform(0.06, 0.14) * (1.3 - d / r * 0.5)
        lump(P, f"{name}{k}", s, mat, (x0 + math.cos(a) * d, y0 + math.sin(a) * d, z + s * flat * 0.6 + (r - d) * 0.25), rnd, flat=flat)


# ================================================================== process plant
def storage_tank_tall(seed=41):
    """An 8 m vertical storage tank: bone shell in welded courses on a concrete ring, a steel cone roof with a vent and a
    rail, a caged ladder, a manway, a level gauge and a valved outlet into the ground; rust runs from the seams."""
    rnd = random.Random(seed)
    P = _root("tank_tall")
    R, H, z0 = 1.5, 7.2, 0.25
    K.cyl("pad", R + 0.45, 0.35, _concrete(0.25), loc=(0, 0, 0.075), segs=24, bev=0.03, parent=P)
    shell = _bone(wear=0.2, grime=0.55, moss=0.08)
    K.cyl("shell", R, H, shell, loc=(0, 0, z0 + H / 2), segs=32, bev=0.03, parent=P)
    K.cyl("chime", R + 0.04, 0.2, _steel(0.2), loc=(0, 0, z0 + 0.1), segs=32, bev=0.01, parent=P)
    for k in range(1, 4):
        K.cyl("course", R + 0.008, 0.04, _steel(0.15), loc=(0, 0, z0 + k * H / 4), segs=24, bev=0, parent=P)
    zt = z0 + H
    K.cyl("roof", R + 0.06, 0.6, _steel(0.2), loc=(0, 0, zt + 0.3), segs=32, r2=0.3, bev=0.02, parent=P)
    K.cyl("roof_cap", 0.32, 0.12, _steel(0.2), loc=(0, 0, zt + 0.64), segs=16, bev=0.01, parent=P)
    K.cyl("vent", 0.1, 0.4, _dark(), loc=(0.1, 0, zt + 0.85), segs=10, bev=0, parent=P)
    K.cyl("vent_hat", 0.2, 0.12, _dark(), loc=(0.1, 0, zt + 1.08), segs=10, r2=0.04, bev=0.01, parent=P)
    # roof rail round the ladder head
    a_l = -math.pi / 2 - 0.55
    rail = M(MUSTARD, metal=0.3, wear=0.2, grime=0.3)
    for k in range(5):
        a = a_l - 0.6 + k * 0.3
        K.box("rail_post", (0.05, 0.05, 1.0), rail, loc=on_ring(R - 0.08, a, zt + 0.55), bev=0, parent=P)
    for z in (zt + 0.6, zt + 1.05):
        K.torus("rail", R - 0.08, 0.025, rail, loc=(0, 0, z), rot=(0, 0, math.degrees(a_l - 0.6)), arc=math.degrees(1.2), segs=40, parent=P)
    ladder(P, on_ring(R + 0.2, a_l, 0), yaw_out(a_l), 0.3, zt)
    # the tank's code painted on a mustard band
    a_t = -math.pi / 2 + 0.45
    K.cyl("band", R + 0.006, 0.7, M(MUSTARD, wear=0.15, grime=0.3), loc=(0, 0, z0 + H * 0.72), segs=32, bev=0, parent=P)
    ring_text(P, "T-07", R + 0.012, z0 + H * 0.72, a_t, 0.5, M(GRAPHITE, wear=0.2))
    # manway on the front right, bolted round
    a_m = -math.pi / 2 + 0.9
    mw = _empty(P, "manway", on_ring(R + 0.05, a_m, 1.0), (90, 0, yaw_out(a_m) + 180))
    K.cyl("mw_neck", 0.38, 0.2, _steel(0.2), loc=(0, 0, 0), segs=20, bev=0.01, parent=mw)
    K.cyl("mw_cover", 0.44, 0.06, _steel(0.25), loc=(0, 0, 0.12), segs=20, bev=0.01, parent=mw)
    for k in range(6):
        a = 2 * math.pi * k / 6
        nut(mw, (0.39 * math.cos(a), 0.39 * math.sin(a), 0.16), r=0.025)
    K.box("mw_handle", (0.3, 0.04, 0.04), _dark(), loc=(0, 0, 0.18), bev=0, parent=mw)
    # level gauge: a tube down the side with a float marker
    a_g = -math.pi / 2 + 0.05
    for z in (0.9, zt - 0.3):
        K.box("lg_bracket", (0.05, 0.16, 0.05), _zinc(), loc=on_ring(R + 0.08, a_g, z), rot=(0, 0, yaw_out(a_g)), bev=0, parent=P)
    K.cyl("lg_tube", 0.025, zt - 1.2, _zinc(), loc=on_ring(R + 0.14, a_g, (zt + 0.6) / 2), segs=8, bev=0, parent=P)
    K.box("lg_float", (0.09, 0.09, 0.14), M(SIGNAL, wear=0.2), loc=on_ring(R + 0.14, a_g, 2.9), rot=(0, 0, yaw_out(a_g)), bev=0.01, parent=P)
    # outlet: out of the bottom course, through a valve and down into the ground
    a_o = -math.pi / 2 - 1.3
    d = Vector((math.cos(a_o), math.sin(a_o), 0))
    zo = 0.65
    p0 = d * (R - 0.05) + Vector((0, 0, zo))
    p1 = d * (R + 1.0) + Vector((0, 0, zo))
    pipe(P, "outlet", p0, p1, 0.12, _steel(0.2))
    flange(P, d * (R + 0.02) + Vector((0, 0, zo)), 0.12, (90, 0, math.degrees(a_o) + 90))
    vx = d * (R + 0.55)
    gate_valve(P, (vx.x, vx.y, zo), axis="y", r=0.12)
    pc = d * (R + 1.0 + 0.3)
    elbow(P, "outlet_bend", (pc.x, pc.y, zo - 0.3), 0.3, 0.12, _steel(0.2), rot=(90, 0, math.degrees(a_o) + 180))
    pipe(P, "outlet_down", pc + Vector((0, 0, zo - 0.3)), pc + Vector((0, 0, -0.2)), 0.12, _steel(0.2))
    # rust running down from the seams
    for k in range(9):
        a = rnd.uniform(-math.pi, math.pi)
        if abs(a - a_l) < 0.3:
            continue
        zk = z0 + rnd.choice((1, 2, 3)) * H / 4
        L = rnd.uniform(0.4, 1.1)
        K.box("streak", (rnd.uniform(0.04, 0.1), 0.006, L), _rust(), loc=on_ring(R + 0.002, a, zk - L / 2), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
    return P


def silo_steel(seed=42):
    """A 12 m steel grain silo: galvanised rings and stiffeners on a concrete ring, a steel-blue cone roof with bone ribs
    that reads from the air, a caged ladder to the eave and a roof ladder to the peak hatch, an aeration fan and a
    discharge gate at the foot."""
    rnd = random.Random(seed)
    P = _root("silo")
    R, H, z0 = 2.3, 8.6, 0.3
    K.cyl("footing", R + 0.35, 0.45, _concrete(0.25), loc=(0, 0, 0.075), segs=28, bev=0.03, parent=P)
    body = M(ZINC, metal=0.5, wear=0.15, grime=0.5, scratches=0.4)
    K.cyl("body", R, H, body, loc=(0, 0, z0 + H / 2), segs=32, bev=0.03, parent=P)
    for k in range(1, 7):
        K.cyl("ring", R + 0.012, 0.05, M(ZINC, metal=0.5, wear=0.1, grime=0.3, rough=0.4), loc=(0, 0, z0 + k * H / 7), segs=24, bev=0, parent=P)
    for k in range(10):
        a = 2 * math.pi * k / 10 + 0.15
        K.box("stiffener", (0.1, 0.05, H), _steel(0.15), loc=on_ring(R + 0.02, a, z0 + H / 2), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
        K.box("anchor", (0.22, 0.16, 0.12), _dark(), loc=on_ring(R + 0.06, a, z0 + 0.06), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
    K.cyl("band", R + 0.02, 0.9, M(MUSTARD, wear=0.15, grime=0.3), loc=(0, 0, z0 + H - 0.9), segs=32, bev=0, parent=P)
    ring_text(P, "SILO 2", R + 0.026, z0 + H - 0.9, -math.pi / 2 + 0.35, 0.6, M(GRAPHITE, wear=0.2))
    zt = z0 + H
    roof_h = 2.3
    K.cyl("eave", R + 0.15, 0.12, _steel(0.2), loc=(0, 0, zt + 0.02), segs=32, bev=0.01, parent=P)
    K.cyl("roof", R + 0.15, roof_h, M(STEEL, metal=0.35, wear=0.15, grime=0.3, moss=0.1), loc=(0, 0, zt + roof_h / 2 + 0.06), segs=32, r2=0.45,
          bev=0.02, parent=P)
    slope = math.degrees(math.atan2(roof_h, R - 0.3))
    for k in range(12):
        a = 2 * math.pi * k / 12
        rr = (R + 0.15 + 0.45) / 2
        K.box("roof_rib", (0.1, (R - 0.3) / math.cos(math.radians(slope)), 0.05), _bone(wear=0.15, grime=0.3),
              loc=on_ring(rr, a, zt + roof_h / 2 + 0.11), rot=(-slope, 0, math.degrees(a) - 90), bev=0, parent=P)
    K.cyl("peak", 0.5, 0.3, _bone(wear=0.15), loc=(0, 0, zt + roof_h + 0.2), segs=16, bev=0.02, parent=P)
    K.cyl("peak_vent", 0.3, 0.4, _dark(), loc=(0, 0, zt + roof_h + 0.55), segs=12, bev=0.01, parent=P)
    K.cyl("peak_hat", 0.55, 0.25, _steel(0.2), loc=(0, 0, zt + roof_h + 0.85), segs=16, r2=0.1, bev=0.01, parent=P)
    # ladders: caged up the side, then up the roof slope to the peak
    a_l = -math.pi / 2 - 0.6
    ladder(P, on_ring(R + 0.22, a_l, 0), yaw_out(a_l), 0.4, zt, top_ext=0.6)
    rl = _empty(P, "roof_ladder", on_ring(R - 0.1, a_l, zt + 0.25), (0, 0, yaw_out(a_l)))
    rl2 = _empty(rl, "roof_ladder_tilt", (0, 0, 0), (slope, 0, 0))
    Lr = (R - 0.5) / math.cos(math.radians(slope))
    for s in (-1, 1):
        K.box("rl_rail", (0.05, Lr, 0.05), _zinc(), loc=(s * 0.22, Lr / 2, 0.06), bev=0, parent=rl2)
    for k in range(int(Lr / 0.35)):
        K.box("rl_rung", (0.44, 0.04, 0.03), _zinc(), loc=(0, 0.2 + k * 0.35, 0.1), bev=0, parent=rl2)
    # aeration fan: a round housing on a transition piece
    a_f = -math.pi / 2 + 0.7
    fan = _empty(P, "fan", on_ring(R, a_f, 0.9), (0, 0, yaw_out(a_f)))
    K.box("fan_transition", (0.6, 0.5, 0.5), _steel(0.2), loc=(0, -0.2, 0), bev=0.02, parent=fan)
    K.cyl("fan_housing", 0.42, 0.5, _steel(0.2), loc=(0, -0.65, 0), rot=(90, 0, 0), segs=20, bev=0.02, parent=fan)
    K.cyl("fan_motor", 0.2, 0.36, _dark(), loc=(0, -1.05, 0), rot=(90, 0, 0), segs=12, bev=0.02, parent=fan)
    for k in range(4):
        K.box("fan_guard", (0.8, 0.02, 0.02), _zinc(), loc=(0, -0.91, -0.3 + k * 0.2), bev=0, parent=fan)
    K.box("fan_stand", (0.5, 0.4, 0.5), _dark(), loc=(0, -0.65, -0.55), bev=0.02, parent=fan)
    # discharge gate at the foot with a short auger stub
    a_d = -math.pi / 2 - 0.05
    dg = _empty(P, "gate", on_ring(R, a_d, 0.55), (0, 0, yaw_out(a_d)))
    K.box("gate_box", (0.6, 0.4, 0.45), _steel(0.25), loc=(0, -0.15, 0), bev=0.02, parent=dg)
    K.box("gate_slide", (0.7, 0.05, 0.06), M(SIGNAL, wear=0.2), loc=(0.1, -0.37, 0.12), bev=0, parent=dg)
    K.box("gate_lever", (0.04, 0.04, 0.6), _dark(), loc=(0.42, -0.4, 0.2), rot=(0, 25, 0), bev=0, parent=dg)
    pipe(dg, "auger", (0, -0.35, -0.1), (0.2, -2.0, -0.35), 0.13, M(ZINC, metal=0.5, wear=0.2, grime=0.5))
    for k in range(6):
        a = rnd.uniform(-math.pi, math.pi)
        if abs(a - a_l) < 0.3:
            continue
        zk = z0 + rnd.randint(2, 6) * H / 7
        L = rnd.uniform(0.5, 1.4)
        K.box("streak", (rnd.uniform(0.05, 0.12), 0.006, L), _rust(), loc=on_ring(R + 0.004, a, zk - L / 2), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
    return P


def cooling_tower_small(seed=43):
    """A small induced-draught cooling tower, 6 m: a concrete hyperboloid shell on raked legs over its basin, a steel
    fan deck across the top with the fan cowl, blades and motor, and the warm-water pipes coming in at the foot."""
    P = _root("cooling")
    conc = _concrete(0.3)
    lathe("basin", [(3.4, 0.0), (3.65, 0.0), (3.65, 0.55), (3.4, 0.55)], conc, segs=32, parent=P)
    K.cyl("water", 3.42, 0.04, K.mat("basin_water", "#2f4a4c", rough=0.08), loc=(0, 0, 0.36), segs=32, bev=0, parent=P)
    K.cyl("basin_floor", 3.45, 0.3, conc, loc=(0, 0, 0.15), segs=32, bev=0, parent=P)
    prof = [(2.95, 1.1), (2.7, 2.1), (2.42, 3.2), (2.28, 4.1), (2.3, 4.7), (2.38, 5.15),
            (2.24, 5.15), (2.16, 4.7), (2.14, 4.1), (2.27, 3.2), (2.55, 2.1), (2.8, 1.1)]
    lathe("shell", prof, K.mat("concrete_tower", CONCRETE, rough=0.9, wear=0.3, grime=0.5, moss=0.2, noise_scale=3.0), segs=32, parent=P)
    K.cyl("shell_lip", 2.96, 0.14, conc, loc=(0, 0, 1.13), segs=32, bev=0.02, parent=P)
    # raked legs in V pairs from the basin floor to the ring beam
    for k in range(12):
        a = 2 * math.pi * k / 12
        for s in (-1, 1):
            b = a + s * 0.13
            beam(P, "leg", on_ring(3.1, a, 0.3), on_ring(2.88, b, 1.08), 0.16, conc, bev=0)
    # louvres glimpsed between the legs: dark slats inside the shell foot
    K.cyl("fill", 2.7, 0.6, K.mat("fill", "#33383a", rough=0.8, grime=0.3), loc=(0, 0, 0.75), segs=24, bev=0, parent=P)
    # fan deck and cowl
    zt = 5.15
    for s in (-1, 1):
        K.box(f"deck_beam{s}", (4.9, 0.22, 0.3), _steel(0.25), loc=(0, s * 0.9, zt + 0.12), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box(f"deck_cross{s}", (0.22, 2.0, 0.22), _steel(0.25), loc=(s * 1.75, 0, zt + 0.38), bev=0.015, parent=P)
    cowl = _bone(wear=0.15, grime=0.4, moss=0.1)
    lathe("cowl", [(1.45, zt + 0.3), (1.52, zt + 0.3), (1.55, zt + 1.05), (1.75, zt + 1.3), (1.67, zt + 1.3), (1.47, zt + 1.05)], cowl, segs=32, parent=P)
    K.cyl("cowl_band", 1.535, 0.25, M(MUSTARD, wear=0.15, grime=0.3), loc=(0, 0, zt + 0.55), segs=32, bev=0, parent=P)
    K.cyl("fan_hub", 0.28, 0.25, _dark(), loc=(0, 0, zt + 0.6), segs=12, bev=0.02, parent=P)
    for k in range(6):
        a = k * 60
        K.box("blade", (1.2, 0.3, 0.03), _zinc(), loc=(math.cos(math.radians(a)) * 0.82, math.sin(math.radians(a)) * 0.82, zt + 0.62),
              rot=(14, 0, a), bev=0.008, parent=P)
    for k in range(5):
        K.box("guard", (0.03, 3.1, 0.03), _zinc(), loc=(-1.2 + k * 0.6, 0, zt + 1.25), bev=0, parent=P)
    K.box("gearbox", (0.4, 0.35, 0.3), _dark(), loc=(0, 0, zt + 0.35), bev=0.02, parent=P)
    K.box("motor", (0.55, 0.4, 0.4), _steel(0.2), loc=(2.1, 0.0, zt + 0.65), bev=0.03, parent=P)
    K.cyl("shaft", 0.04, 1.6, _zinc(), loc=(1.25, 0, zt + 0.55), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    ring_text(P, "CT-2", 2.3, 4.45, -math.pi / 2 + 0.3, 0.55, M(GRAPHITE, wear=0.2))
    # two warm-water pipes rising out of the ground into the basin side
    bone = _bone(wear=0.2, moss=0.1)
    for k, x in enumerate((-1.0, 1.0)):
        pipe(P, f"in{k}", (x, 4.9, 0.35), (x, 3.3, 0.35), 0.22, bone)
        flange(P, (x, 3.75, 0.35), 0.22, (90, 0, 0))
        K.box(f"saddle{k}", (0.5, 0.25, 0.2), _concrete(), loc=(x, 4.4, 0.08), bev=0.02, parent=P)
        K.box(f"sump{k}", (0.8, 0.5, 0.5), _concrete(), loc=(x, 4.95, 0.2), bev=0.03, parent=P)
    return P


def pipe_rack(seed=44):
    """A 6 m steel pipe rack: three bents of wide-flange columns on footings, knee-braced beams carrying four pipes on
    shoes and U-bolts, and a cable tray on the lower tier."""
    P = _root("piperack")
    steel = _steel(0.25)
    xs = (-2.6, 0.0, 2.6)
    zb = 3.6
    for x in xs:
        for s in (-1, 1):
            K.box("footing", (0.6, 0.6, 0.35), _concrete(0.25), loc=(x, s * 1.0, 0.08), bev=0.03, parent=P)
            K.box("base_plate", (0.4, 0.4, 0.03), _dark(), loc=(x, s * 1.0, 0.27), bev=0, parent=P)
            for bx in (-0.14, 0.14):
                nut(P, (x + bx, s * 1.0 - 0.14, 0.3), r=0.022)
            hcol(P, "col", x, s * 1.0, 0.28, zb - 0.28 + 0.3, steel)
            beam(P, "knee", (x, s * 0.88, zb - 0.7), (x, s * 0.45, zb - 0.05), 0.07, steel)
        ibeam(P, "cross", 2.3, (x, 0, zb), (0, 0, 90), H=0.3, F=0.18, mat=steel)
        ibeam(P, "tier", 2.0, (x, 0, 2.3), (0, 0, 90), H=0.2, F=0.14, mat=steel)
    for s in (-1, 1):
        ibeam(P, "tie", 5.6, (0, s * 1.0, zb + 0.05), H=0.2, F=0.12, mat=steel)
    beam(P, "brace", (-2.6, -1.0, 0.6), (0.0, -1.0, zb - 0.1), 0.08, _zinc())
    beam(P, "brace", (0.0, -1.0, 0.6), (-2.6, -1.0, zb - 0.1), 0.08, _zinc())
    pipes = ((-0.68, 0.3, _bone(wear=0.2, moss=0.05)), (-0.08, 0.2, _steel(0.2)), (0.4, 0.2, M(MUSTARD, metal=0.3, wear=0.15, grime=0.4)),
             (0.8, 0.14, _zinc()))
    L = 6.6
    for k, (y, r, m) in enumerate(pipes):
        z = zb + 0.3 + 0.06 + r
        pipe(P, f"pipe{k}", (-L / 2, y, z), (L / 2, y, z), r, m)
        for s in (-1, 1):
            flange(P, (s * L / 2 - s * 0.04, y, z), r, (0, 90, 0))
        if k == 0:
            flange(P, (1.3, y, z), r, (0, 90, 0))
        for x in xs:
            K.box("shoe", (0.25, r * 1.2, 0.06), _dark(), loc=(x, y, zb + 0.33), bev=0, parent=P)
        for x in xs[1:2]:
            K.torus("ubolt", r + 0.015, 0.012, _zinc(), loc=(x, y, z), rot=(90, 0, 90), arc=180, segs=12, parent=P)
    # cable tray on the lower tier
    K.box("tray", (L - 0.6, 0.6, 0.03), _zinc(), loc=(0, 0.2, 2.53), bev=0, parent=P)
    for s in (-1, 1):
        K.box("tray_side", (L - 0.6, 0.02, 0.12), _zinc(), loc=(0, 0.2 + s * 0.3, 2.58), bev=0, parent=P)
    for k, (y, col) in enumerate(((0.0, CABLE), (0.12, CABLE), (0.26, SIGNAL), (0.4, CABLE))):
        K.cyl(f"cable{k}", 0.04, L - 0.4, K.mat(f"cable{col}", col, rough=0.6), loc=(0, y, 2.585), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    stencil(P, "PR-4", (0, -1.1, 1.6), 0.22, M(BONE, wear=0.2), rot=(90, 0, 0))
    K.box("tag_plate", (0.6, 0.02, 0.3), _dark(), loc=(0, -1.09, 1.6), bev=0.005, parent=P)
    return P


def pipe_bridge(seed=45):
    """Two pipes crossing a road overhead: lattice towers 8 m apart, a truss between them with hazard stripes on its
    soffit, the pipes bending down each tower into the ground."""
    P = _root("pipebridge")
    steel = _steel(0.25)
    brace = _zinc()
    S, hw, Ht = 4.0, 0.6, 5.4
    lv = (0.25, 1.95, 3.7, Ht)
    for tx in (-S, S):
        for sx in (-1, 1):
            for sy in (-1, 1):
                K.box("footing", (0.5, 0.5, 0.35), _concrete(0.25), loc=(tx + sx * hw, sy * hw, 0.08), bev=0.03, parent=P)
                beam(P, "leg", (tx + sx * hw, sy * hw, 0.25), (tx + sx * hw, sy * hw, Ht + 0.1), 0.16, steel)
        for i in range(len(lv)):
            z = lv[i]
            for s in (-1, 1):
                beam(P, "hx", (tx - hw, s * hw, z), (tx + hw, s * hw, z), 0.09, brace, bev=0)
                beam(P, "hy", (tx + s * hw, -hw, z), (tx + s * hw, hw, z), 0.09, brace, bev=0)
            if i == 0:
                continue
            z0 = lv[i - 1]
            for s in (-1, 1):
                d = 1 if i % 2 else -1
                beam(P, "dx", (tx - d * hw, s * hw, z0), (tx + d * hw, s * hw, z), 0.07, brace, bev=0)
                beam(P, "dy", (tx + s * hw, -d * hw, z0), (tx + s * hw, d * hw, z), 0.07, brace, bev=0)
    # the truss: chords both sides, verticals and diagonals, cross members carrying the pipes
    zb, zt = Ht, Ht + 1.0
    for s in (-1, 1):
        beam(P, "chord_b", (-S - hw, s * hw, zb), (S + hw, s * hw, zb), 0.14, steel)
        beam(P, "chord_t", (-S - hw, s * hw, zt), (S + hw, s * hw, zt), 0.12, steel)
        n = 8
        for k in range(n + 1):
            x = -S + k * 2 * S / n
            beam(P, "vert", (x, s * hw, zb), (x, s * hw, zt), 0.07, brace, bev=0)
            if k < n:
                x2 = x + 2 * S / n
                a, b = ((x, zb), (x2, zt)) if k < n / 2 else ((x, zt), (x2, zb))
                beam(P, "diag", (a[0], s * hw, a[1]), (b[0], s * hw, b[1]), 0.06, brace, bev=0)
    for k in range(9):
        x = -S + k * S / 4
        beam(P, "cross_b", (x, -hw, zb), (x, hw, zb), 0.1, steel, bev=0)
        beam(P, "cross_t", (x, -hw, zt), (x, hw, zt), 0.07, brace, bev=0)
    hazard(P, (0, -hw - 0.08, zb), 3.6, 0.13, M(MUSTARD, wear=0.2), _dark(), n=12)
    # pipes: along the truss, bending down outside each tower to the ground
    for k, (y, r, m) in enumerate(((-0.28, 0.2, _bone(wear=0.2, moss=0.05)), (0.25, 0.14, _steel(0.2)))):
        z = zb + 0.05 + r
        Rb = 0.45
        xe = S + hw + 0.35
        pipe(P, f"run{k}", (-xe + Rb, y, z), (xe - Rb, y, z), r, m)
        elbow(P, f"bend_r{k}", (xe - Rb, y, z - Rb), Rb, r, m, rot=(90, 0, 0))
        elbow(P, f"bend_l{k}", (-xe + Rb, y, z - Rb), Rb, r, m, rot=(90, 0, 180))
        for sx in (-1, 1):
            pipe(P, f"drop{k}{sx}", (sx * xe, y, z - Rb), (sx * xe, y, 0.0), r, m)
            flange(P, (sx * xe, y, 2.0 + k * 0.4), r, (0, 0, 0))
            K.box(f"thrust{k}{sx}", (0.6, 0.55, 0.3), _concrete(0.3), loc=(sx * xe, y, 0.05), bev=0.03, parent=P)
            for zc in (1.2, 3.0, 4.5):
                beam(P, "clip", (sx * (S + hw), y, zc), (sx * (xe - r), y, zc), 0.06, _dark(), bev=0)
        for x in (-2.0, 2.0):
            flange(P, (x + k * 0.3, y, z), r, (0, 90, 0))
    stencil(P, "5.0m", (0, -hw - 0.11, zb), 0.13, _dark(), rot=(90, 0, 0))
    ladder(P, (S, -hw - 0.2, 0), 0, 0.25, Ht, standoff=0.12, top_ext=1.0)
    return P


def valve_manifold(seed=46):
    """A valve manifold on a concrete pad: a bone header on saddles fed from the ground through a big gate valve,
    three branches each with its own valve and red handwheel, tags on chains and a pressure gauge."""
    P = _root("manifold")
    K.box("pad", (4.4, 2.6, 0.18), _concrete(0.3), loc=(0, 0.2, 0.06), bev=0.03, parent=P)
    hdr = _bone(wear=0.2, moss=0.05)
    br = _steel(0.2)
    zh, rh = 0.75, 0.2
    pipe(P, "header", (-1.6, 0, zh), (1.75, 0, zh), rh, hdr)
    K.cyl("header_cap", rh + 0.06, 0.08, br, loc=(1.79, 0, zh), rot=(0, 90, 0), segs=16, bev=0.01, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        nut(P, (1.84, math.cos(a) * (rh + 0.03), zh + math.sin(a) * (rh + 0.03)), r=0.02, rot=(0, 90, 0))
    for x in (-0.9, 1.2):
        K.box("saddle", (0.3, 0.5, zh - 0.1), _concrete(), loc=(x, 0, (zh - 0.1) / 2 + 0.1), bev=0.02, parent=P)
        K.torus("strap", rh + 0.015, 0.014, _zinc(), loc=(x, 0, zh), rot=(90, 0, 90), arc=180, segs=12, parent=P)
    # inlet: up out of the ground, an elbow and the main valve with its wheel facing the front
    Rb = 0.35
    pipe(P, "riser", (-2.0, 0, 0.1), (-2.0, 0, zh - Rb), rh, hdr)
    elbow(P, "inlet_bend", (-2.0 + Rb, 0, zh - Rb), Rb, rh, hdr, rot=(90, 0, 180))
    gate_valve(P, (-1.25, 0, zh), axis="x", r=rh, h=0.7, wheel=M(MUSTARD, metal=0.3, wear=0.2, grime=0.3))
    # three branches off to the back, each through a valve and down into the ground
    rb = 0.1
    for k, x in enumerate((-0.35, 0.45, 1.25)):
        pipe(P, f"branch{k}", (x, 0, zh), (x, 1.3, zh), rb, br, segs=10)
        K.cyl("tee", rb + 0.03, 0.2, br, loc=(x, rh, zh), rot=(90, 0, 0), segs=10, bev=0, parent=P)
        gate_valve(P, (x, 0.65, zh), axis="y", r=rb, h=0.5)
        elbow(P, f"bend{k}", (x, 1.3, zh - 0.25), 0.25, rb, br, rot=(90, 0, 90))
        pipe(P, f"down{k}", (x, 1.55, zh - 0.25), (x, 1.55, 0.05), rb, br, segs=10)
        # a tag hanging from the handwheel
        G = _empty(P, "tag", (x + 0.2, 0.65, zh + 0.38))
        K.box("tag_chain", (0.008, 0.008, 0.14), _zinc(), loc=(0, 0, 0.07), bev=0, parent=G)
        K.box("tag", (0.12, 0.006, 0.1), M(MUSTARD, wear=0.2), loc=(0, 0, -0.05), bev=0, parent=G)
        stencil(G, "ABC"[k], (0, -0.005, -0.05), 0.07, _dark(), extrude=0.003)
    K.cyl("gauge_stub", 0.025, 0.25, _zinc(), loc=(0.85, 0, zh + rh + 0.12), segs=8, bev=0, parent=P)
    gauge(P, (0.85, -0.02, zh + rh + 0.3), rot=(90, 0, 0), r=0.09)
    hazard(P, (0.0, -1.1, 0.16), 3.8, 0.06, M(MUSTARD, wear=0.3), _dark(), n=14)
    return P


def pump_station(seed=47):
    """A boxy pump house: framed bone walls on a pad, a steel door with a hazard kick plate, a louvred vent, a dead
    lamp and its code, the suction pipe in one side and the delivery pipe out the other, both into the ground."""
    P = _root("pumphouse")
    K.box("pad", (5.4, 4.2, 0.2), _concrete(0.3), loc=(0, 0.1, 0.07), bev=0.03, parent=P)
    W, D, H = 4.0, 3.0, 2.9
    steel = _steel(0.3)
    FR.framed_box(P, "house", (0, 0.2, 0.17 + H / 2), (W, D, H), _bone(wear=0.3, grime=0.55, moss=0.05), steel, fw=0.16, screws=_zinc())
    yf = 0.2 - D / 2
    K.box("roof", (W + 0.4, D + 0.4, 0.14), M(STEEL, metal=0.35, wear=0.25, grime=0.4, moss=0.15), loc=(0, 0.2, 0.17 + H + 0.1), rot=(2, 0, 0),
          bev=0.02, parent=P)
    K.box("door_frame", (1.15, 0.06, 2.2), _dark(), loc=(-0.9, yf - 0.02, 0.17 + 1.1), bev=0.01, parent=P)
    K.box("door", (1.0, 0.06, 2.05), M(STEEL, metal=0.35, wear=0.35, grime=0.5), loc=(-0.9, yf - 0.05, 0.17 + 1.04), bev=0.01, parent=P)
    hazard(P, (-0.9, yf - 0.085, 0.17 + 0.15), 0.96, 0.2, M(MUSTARD, wear=0.3), _dark(), n=6)
    K.box("handle", (0.04, 0.05, 0.22), _zinc(), loc=(-0.5, yf - 0.1, 0.17 + 1.05), bev=0, parent=P)
    for z in (0.5, 1.7):
        K.box("hinge", (0.06, 0.06, 0.14), _dark(), loc=(-1.38, yf - 0.08, 0.17 + z), bev=0, parent=P)
    stencil(P, "PS-3", (-0.9, yf + 0.037, 0.17 + 2.5), 0.24, _dark())
    FR.vent(P, (0.9, yf - 0.04, 0.17 + 1.7), 1.0, 0.6, steel, _dark())
    K.box("lamp_arm", (0.08, 0.3, 0.08), _dark(), loc=(-0.9, yf - 0.17, 0.17 + 2.75), bev=0, parent=P)
    K.box("lamp", (0.34, 0.2, 0.12), _dark(), loc=(-0.9, yf - 0.3, 0.17 + 2.72), bev=0.015, parent=P)
    K.box("lamp_lens", (0.28, 0.14, 0.02), K.mat("deadlens", "#cfd6d8", rough=0.2), loc=(-0.9, yf - 0.3, 0.17 + 2.65), bev=0, parent=P)
    # meter cabinet and conduit up the right side
    cased_box(P, "cabinet", (W / 2 + 0.17, 0.0, 1.2), (0.3, 0.6, 0.8), M(GRAPHITE, wear=0.2, grime=0.4), steel, fw=0.04)
    K.cyl("conduit", 0.03, 1.6, _zinc(), loc=(W / 2 + 0.1, 0.4, 2.2), segs=8, bev=0, parent=P)
    # suction pipe in the left wall, delivery pipe out the right
    bone = _bone(wear=0.2, moss=0.08)
    zi, ri = 0.8, 0.28
    Rb = 0.45
    pipe(P, "suction", (-W / 2 - 1.2 + Rb, 0.6, zi), (-W / 2 + 0.05, 0.6, zi), ri, bone)
    elbow(P, "suction_bend", (-W / 2 - 1.2 + Rb, 0.6, zi - Rb), Rb, ri, bone, rot=(90, 0, 180))
    pipe(P, "suction_down", (-W / 2 - 1.2, 0.6, zi - Rb), (-W / 2 - 1.2, 0.6, -0.1), ri, bone)
    flange(P, (-W / 2 - 0.08, 0.6, zi), ri, (0, 90, 0))
    flange(P, (-W / 2 - 0.65, 0.6, zi), ri, (0, 90, 0))
    zo, ro = 1.7, 0.18
    dl = _steel(0.2)
    pipe(P, "delivery", (W / 2 - 0.05, 0.9, zo), (W / 2 + 1.2 - Rb, 0.9, zo), ro, dl)
    elbow(P, "delivery_bend", (W / 2 + 1.2 - Rb, 0.9, zo - Rb), Rb, ro, dl, rot=(90, 0, 0))
    pipe(P, "delivery_down", (W / 2 + 1.2, 0.9, zo - Rb), (W / 2 + 1.2, 0.9, -0.1), ro, dl)
    flange(P, (W / 2 + 0.08, 0.9, zo), ro, (0, 90, 0))
    gate_valve(P, (W / 2 + 0.4, 0.9, zo), axis="x", r=ro, h=0.45)
    for x in (-W / 2 - 1.2, W / 2 + 1.2):
        K.box("thrust", (0.7, 0.7, 0.25), _concrete(0.3), loc=(x, 0.75, 0.05), bev=0.03, parent=P)
    # roof: a turbine vent and a lifting eye
    K.cyl("roof_vent", 0.22, 0.3, _zinc(), loc=(1.0, 0.6, 3.35), segs=12, bev=0.01, parent=P)
    K.sphere("roof_vent_head", 0.28, _zinc(), loc=(1.0, 0.6, 3.55), scale=(1, 1, 0.7), parent=P)
    rust_patch(P, (1.5, yf - 0.03, 0.6), (0.4, 0.004, 0.3))
    return P


def compressor_unit(seed=48):
    """A skid-mounted air compressor: I-beam skid with checker plate, motor, guarded coupling, a framed compressor block
    with finned heads, and the receiver tank on saddles with its gauge, safety valve and drain."""
    P = _root("compressor")
    L = 3.8
    for s in (-1, 1):
        ibeam(P, f"runner{s}", L, (0, s * 0.62, 0), H=0.22, F=0.12, mat=_dark())
    K.box("deck", (L - 0.1, 1.38, 0.025), M(ZINC, metal=0.55, wear=0.25, grime=0.55), loc=(0, 0, 0.233), bev=0.004, parent=P)
    zd = 0.245
    # motor
    K.cyl("motor", 0.3, 0.75, M(STEEL, metal=0.35, wear=0.2, grime=0.4), loc=(-1.35, 0, zd + 0.38), rot=(0, 90, 0), segs=18, bev=0.02, parent=P)
    for k in range(4):
        K.cyl("fin", 0.315, 0.03, _dark(), loc=(-1.55 + k * 0.13, 0, zd + 0.38), rot=(0, 90, 0), segs=14, bev=0, parent=P)
    K.box("motor_feet", (0.6, 0.5, 0.1), _dark(), loc=(-1.35, 0, zd + 0.05), bev=0.01, parent=P)
    K.box("terminal_box", (0.2, 0.2, 0.14), _dark(), loc=(-1.3, 0, zd + 0.73), bev=0.01, parent=P)
    K.cyl("motor_cowl", 0.27, 0.12, _dark(), loc=(-1.78, 0, zd + 0.38), rot=(0, 90, 0), segs=18, bev=0.01, parent=P)
    # coupling guard
    K.box("guard", (0.35, 0.5, 0.5), M(MUSTARD, wear=0.25, grime=0.4), loc=(-0.8, 0, zd + 0.38), bev=0.02, parent=P)
    hazard(P, (-0.8, -0.27, zd + 0.38), 0.33, 0.12, M(MUSTARD, wear=0.2), _dark(), n=4)
    # compressor block and two finned heads in a V
    FR.framed_box(P, "block", (-0.2, 0, zd + 0.42), (0.8, 0.9, 0.84), _bone(wear=0.25, grime=0.5), _steel(0.25), fw=0.06)
    for s in (-1, 1):
        G = _empty(P, "head", (-0.2, s * 0.2, zd + 0.88), (s * -30, 0, 0))
        K.cyl("cyl", 0.17, 0.35, _dark(), loc=(0, 0, 0.17), segs=14, bev=0.01, parent=G)
        for k in range(4):
            K.box("hfin", (0.42, 0.42, 0.025), _dark(), loc=(0, 0, 0.05 + k * 0.08), bev=0, parent=G)
        K.box("head_cap", (0.38, 0.38, 0.08), _steel(0.2), loc=(0, 0, 0.38), bev=0.015, parent=G)
    stencil(P, "AC-6", (-0.2, -0.413, zd + 0.42), 0.15, _dark())
    # receiver tank
    xt0, xt1, rt = 0.35, 1.75, 0.42
    zt = zd + 0.24 + rt
    tank = _bone(wear=0.2, grime=0.45)
    K.cyl("receiver", rt, xt1 - xt0, tank, loc=((xt0 + xt1) / 2, 0, zt), rot=(0, 90, 0), segs=20, bev=0.01, parent=P)
    for x, s in ((xt0, -1), (xt1, 1)):
        K.sphere("dome", rt, tank, loc=(x, 0, zt), scale=(0.4, 1, 1), parent=P)
    for x in (0.7, 1.4):
        K.box("tank_saddle", (0.14, 0.7, 0.3), _dark(), loc=(x, 0, zd + 0.13), bev=0.01, parent=P)
    K.cyl("tank_band", rt + 0.006, 0.18, M(MUSTARD, wear=0.15, grime=0.3), loc=(1.05, 0, zt), rot=(0, 90, 0), segs=20, bev=0, parent=P)
    pipe(P, "discharge", (-0.2, 0.2, zd + 1.2), (0.6, 0.2, zd + 1.2), 0.04, _zinc(), segs=8)
    pipe(P, "discharge_dn", (0.6, 0.2, zd + 1.2), (0.6, 0.2, zt + rt - 0.05), 0.04, _zinc(), segs=8)
    K.cyl("safety_valve", 0.04, 0.2, M(SIGNAL, wear=0.2), loc=(1.3, 0, zt + rt + 0.08), segs=8, bev=0, parent=P)
    K.cyl("gauge_stub", 0.02, 0.15, _zinc(), loc=(1.0, -0.1, zt + rt + 0.04), segs=6, bev=0, parent=P)
    gauge(P, (1.0, -0.12, zt + rt + 0.17), rot=(90, 0, 0), r=0.07)
    K.cyl("drain", 0.03, 0.15, _zinc(), loc=(1.6, 0, zd + 0.17), segs=6, bev=0, parent=P)
    # control box on a post at the front corner
    K.box("ctrl_post", (0.08, 0.08, 1.1), _dark(), loc=(-1.6, -0.55, zd + 0.55), bev=0, parent=P)
    cased_box(P, "ctrl", (-1.6, -0.6, zd + 1.1), (0.45, 0.2, 0.5), M(GRAPHITE, wear=0.2, grime=0.3), _steel(0.2), fw=0.035)
    K.cyl("estop", 0.04, 0.04, M(SIGNAL, wear=0.1), loc=(-1.6, -0.715, zd + 1.2), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    K.box("ctrl_label", (0.25, 0.01, 0.08), M(BONE, wear=0.2), loc=(-1.6, -0.705, zd + 1.02), bev=0, parent=P)
    return P


def chimney_steel(seed=49):
    """A 10 m steel flue on a framed base: flanged sections, a soot-black mouth under a rain cap, red and bone bands near
    the top, a guy ring with three rods to anchor blocks, and the torn breeching duct where the boiler stood."""
    P = _root("chimney")
    K.box("plinth", (1.8, 1.8, 0.4), _concrete(0.3), loc=(0, 0, 0.1), bev=0.03, parent=P)
    FR.framed_box(P, "base", (0, 0, 0.3 + 0.7), (1.2, 1.2, 1.4), M(STEEL, metal=0.35, wear=0.3, grime=0.5), _dark(), fw=0.09, screws=_zinc())
    K.box("cleanout", (0.5, 0.04, 0.5), _dark(), loc=(0, -0.62, 0.75), bev=0.01, parent=P)
    for x in (-0.18, 0.18):
        K.box("dog", (0.06, 0.05, 0.08), _zinc(), loc=(x, -0.65, 0.75), bev=0, parent=P)
    K.box("base_cap", (1.3, 1.3, 0.12), _dark(), loc=(0, 0, 1.76), bev=0.015, parent=P)
    z0, z1, r = 1.82, 9.7, 0.38
    flue = M(ZINC, metal=0.5, wear=0.15, grime=0.4, scratches=0.4)
    K.cyl("flue", r, z1 - z0, flue, loc=(0, 0, (z0 + z1) / 2), segs=20, r2=r - 0.06, bev=0.01, parent=P)
    for k, z in enumerate((2.0, 4.0, 6.0, 8.0)):
        rr = r - 0.06 * (z - z0) / (z1 - z0)
        K.cyl("joint", rr + 0.05, 0.07, _dark(), loc=(0, 0, z), segs=20, bev=0.01, parent=P)
    for k, (z, col) in enumerate(((8.4, SIGNAL), (8.75, BONE), (9.1, SIGNAL))):
        rr = r - 0.06 * (z - z0) / (z1 - z0)
        K.cyl("band", rr + 0.006, 0.35, M(col, wear=0.15, grime=0.3), loc=(0, 0, z), segs=20, r2=rr + 0.004, bev=0, parent=P)
    K.cyl("soot", r - 0.055, 0.3, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, z1 - 0.14), segs=20, bev=0, parent=P)
    K.cyl("mouth", r - 0.1, 0.02, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, z1 + 0.005), segs=16, bev=0, parent=P)
    for k in range(3):
        a = 2 * math.pi * k / 3
        beam(P, "cap_strut", on_ring(r - 0.1, a, z1 - 0.1), on_ring(r - 0.05, a, z1 + 0.35), 0.03, _dark(), bev=0)
    K.cyl("rain_cap", 0.62, 0.22, _steel(0.2), loc=(0, 0, z1 + 0.42), segs=20, r2=0.06, bev=0.01, parent=P)
    # guy ring and rods
    zg = 7.0
    rg = r - 0.06 * (zg - z0) / (z1 - z0)
    K.cyl("guy_ring", rg + 0.06, 0.14, _dark(), loc=(0, 0, zg), segs=20, bev=0.01, parent=P)
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2 + math.pi / 3
        top = on_ring(rg + 0.1, a, zg)
        foot = on_ring(4.4, a, 0.35)
        K.box("lug", (0.08, 0.16, 0.12), _dark(), loc=on_ring(rg + 0.1, a, zg), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
        rod(P, "guy", top, foot, 0.016, _zinc(), segs=6)
        tb = Vector(foot).lerp(Vector(top), 0.12)
        rod(P, "turnbuckle", tb - (Vector(top) - Vector(foot)).normalized() * 0.15, tb + (Vector(top) - Vector(foot)).normalized() * 0.15, 0.03, _dark(), segs=8)
        K.box("anchor", (0.6, 0.6, 0.4), _concrete(0.3), loc=on_ring(4.5, a, 0.1), rot=(0, 0, math.degrees(a)), bev=0.03, parent=P)
    # breeching duct stub from the left, its end torn
    K.box("duct", (1.5, 0.6, 0.6), M(STEEL, metal=0.35, wear=0.3, grime=0.5), loc=(-1.35, 0, 1.0), bev=0.02, parent=P)
    K.box("duct_flange", (0.08, 0.7, 0.7), _dark(), loc=(-0.64, 0, 1.0), bev=0.01, parent=P)
    K.box("duct_torn", (0.04, 0.5, 0.5), K.mat("soot", SOOT, rough=0.95), loc=(-2.11, 0, 1.0), bev=0, parent=P)
    K.box("duct_lip", (0.3, 0.6, 0.03), _rust(), loc=(-2.12, 0, 1.31), rot=(0, 25, 0), bev=0, parent=P)
    K.box("duct_leg", (0.1, 0.1, 0.7), _dark(), loc=(-1.7, 0, 0.35), bev=0, parent=P)
    stencil(P, "F-1", (0, -0.563, 1.35), 0.18, M(BONE, wear=0.2))
    return P


def smokestack_brick(seed=50):
    """A 12 m brick stack on a square plinth with a stone coping and a barred flue arch, iron hoops up its taper, the
    colony's bone and mustard bands near the top, a corbelled cap, iron rungs and a lightning rod."""
    P = _root("smokestack")
    brick = K.mat("brick", BRICK, rough=0.9, wear=0.3, grime=0.55, cracks=0.35, noise_scale=4.0, moss=0.1)
    stone = K.mat("stone", STONE, rough=0.9, wear=0.3, grime=0.5, cracks=0.2, moss=0.2)
    K.box("footing", (3.0, 3.0, 0.3), stone, loc=(0, 0, 0.1), bev=0.04, parent=P)
    K.box("plinth", (2.6, 2.6, 2.0), brick, loc=(0, 0, 1.25), bev=0.03, parent=P)
    K.box("coping", (2.85, 2.85, 0.2), stone, loc=(0, 0, 2.35), bev=0.03, parent=P)
    # flue arch on the front: a dark opening behind iron bars
    K.box("arch_opening", (0.8, 0.04, 1.0), K.mat("soot", SOOT, rough=0.95), loc=(0, -1.3, 0.85), bev=0, parent=P)
    K.cyl("arch_top", 0.4, 0.04, K.mat("soot", SOOT, rough=0.95), loc=(0, -1.3, 1.35), rot=(90, 0, 0), segs=16, bev=0, parent=P)
    lathe("arch_ring", [(0.4, -0.02), (0.55, -0.02), (0.55, 0.03), (0.4, 0.03)], stone, segs=16, loc=(0, -1.31, 1.35), rot=(90, 0, 0), parent=P)
    for k in range(4):
        K.cyl("bar", 0.018, 1.35, _dark(), loc=(-0.3 + k * 0.2, -1.33, 1.0), segs=6, bev=0, parent=P)
    K.box("sill", (1.1, 0.2, 0.1), stone, loc=(0, -1.33, 0.32), bev=0.02, parent=P)
    z0, z1, r0, r1 = 2.45, 11.4, 1.05, 0.72

    def rad(z):
        return r0 + (r1 - r0) * (z - z0) / (z1 - z0)

    K.cyl("stack", r0, z1 - z0, brick, loc=(0, 0, (z0 + z1) / 2), segs=24, r2=r1, bev=0.02, parent=P)
    for z in (3.6, 5.1, 6.6, 8.1, 9.3):
        K.cyl("hoop", rad(z) + 0.02, 0.06, _dark(), loc=(0, 0, z), segs=24, r2=rad(z + 0.06) + 0.02, bev=0, parent=P)
    for z, h, col in ((9.85, 0.42, BONE), (10.3, 0.42, MUSTARD)):
        K.cyl("band", rad(z - h / 2) + 0.008, h, M(col, wear=0.2, grime=0.3), loc=(0, 0, z), segs=24, r2=rad(z + h / 2) + 0.008, bev=0, parent=P)
    K.cyl("corbel", r1 + 0.1, 0.18, brick, loc=(0, 0, z1 + 0.04), segs=24, bev=0.02, parent=P)
    K.cyl("cap", r1 + 0.16, 0.22, stone, loc=(0, 0, z1 + 0.24), segs=24, bev=0.02, parent=P)
    K.cyl("mouth", r1 - 0.12, 0.02, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, z1 + 0.355), segs=16, bev=0, parent=P)
    K.cyl("soot_ring", r1 + 0.165, 0.08, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, z1 + 0.32), segs=24, bev=0, parent=P)
    # iron rungs up the back left, the lightning conductor beside them
    a = math.pi / 2 + 0.5
    for k in range(21):
        z = z0 + 0.4 + k * 0.42
        K.box("rung", (0.34, 0.035, 0.035), _dark(), loc=on_ring(rad(z) + 0.1, a, z), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
        for s in (-1, 1):
            K.box("rung_leg", (0.03, 0.12, 0.03), _dark(), loc=on_ring(rad(z) + 0.05, a + s * 0.15 / rad(z), z), rot=(0, 0, yaw_out(a)), bev=0, parent=P)
    ac = a + 0.5
    beam(P, "conductor", on_ring(r0 + 0.02, ac, z0), on_ring(r1 + 0.02, ac, z1 - 0.1), 0.03, _dark(), d=0.012, bev=0)
    rod(P, "rod", on_ring(r1 + 0.1, ac, z1 - 0.1), on_ring(r1 + 0.1, ac, z1 + 1.15), 0.016, _dark(), segs=6)
    K.box("conductor_low", (0.03, 0.012, 2.3), _dark(), loc=(-1.31, 0.8, 1.25), bev=0, parent=P)
    return P


# ================================================================== material handling
def conveyor_tower(seed=51):
    """An inclined belt conveyor in a lattice gallery, 12 m up to a hopper tower: the tower's legs braced in X, a bin
    and funnel with a discharge gate over the truck bay, a railed platform and the head drive under a hood."""
    rnd = random.Random(seed)
    P = _root("conveyortower")
    G = _empty(P, "offset", (3.3, 0, 0))
    steel = _steel(0.25)
    brace = _zinc()
    tx, hw, Ht = 2.4, 1.25, 7.2
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("footing", (0.6, 0.6, 0.35), _concrete(0.25), loc=(tx + sx * hw, sy * hw, 0.08), bev=0.03, parent=G)
            hcol(G, "leg", tx + sx * hw, sy * hw, 0.25, Ht - 0.25, steel)
    lv = (2.4, 4.8, Ht)
    for i, z in enumerate(lv):
        for s in (-1, 1):
            beam(G, "hx", (tx - hw, s * hw, z), (tx + hw, s * hw, z), 0.14, steel, bev=0)
            beam(G, "hy", (tx + s * hw, -hw, z), (tx + s * hw, hw, z), 0.14, steel, bev=0)
        z0 = lv[i - 1] if i else 0.3
        if i == 0:
            continue
        for s in (-1, 1):
            beam(G, "dx", (tx - hw, s * hw, z0), (tx + hw, s * hw, z), 0.07, brace, bev=0)
            beam(G, "dx", (tx + hw, s * hw, z0), (tx - hw, s * hw, z), 0.07, brace, bev=0)
            beam(G, "dy", (tx + s * hw, -hw, z0), (tx + s * hw, hw, z), 0.07, brace, bev=0)
    # bin at the top of the tower and the funnel below it, a gate and a short spout over the truck bay
    zb = 5.5
    bin_ = _bone(wear=0.25, grime=0.5, moss=0.05)
    FR.framed_box(G, "bin", (tx, 0, zb + 0.85), (2.5, 2.5, 1.7), bin_, steel, fw=0.12)
    frustum("funnel", (0.6, 0.6), (2.4, 2.4), 1.8, bin_, loc=(tx, 0, zb - 1.8), parent=G, open_top=False)
    K.box("gate", (0.8, 0.8, 0.2), _dark(), loc=(tx, 0, zb - 1.9), bev=0.02, parent=G)
    K.box("gate_handle", (0.06, 0.6, 0.06), M(SIGNAL, wear=0.2), loc=(tx + 0.45, -0.3, zb - 1.9), bev=0, parent=G)
    frustum("spout", (0.45, 0.45), (0.6, 0.6), 0.5, _dark(), loc=(tx, 0, zb - 2.5), rot=(180, 0, 0), parent=G)
    hazard(G, (tx, -1.27, zb + 0.2), 2.2, 0.22, M(MUSTARD, wear=0.25), _dark(), n=10)
    stencil(G, "HT-1", (tx, -1.213, zb + 1.05), 0.45, _dark())
    # platform on the bin with a rail
    zp = zb + 1.75
    K.box("platform", (3.0, 3.0, 0.06), M(ZINC, metal=0.55, wear=0.25, grime=0.5), loc=(tx, 0, zp), bev=0.005, parent=G)
    rail = M(MUSTARD, metal=0.3, wear=0.2, grime=0.3)
    for (ax, ay), (bx, by) in (((-1.5, -1.5), (1.5, -1.5)), ((1.5, -1.5), (1.5, 1.5)), ((1.5, 1.5), (-1.5, 1.5))):
        for z in (0.55, 1.05):
            beam(G, "rail", (tx + ax, ay, zp + z), (tx + bx, by, zp + z), 0.04, rail, bev=0)
        for t in (0.0, 0.5, 1.0):
            x, y = ax + (bx - ax) * t, ay + (by - ay) * t
            beam(G, "rail_post", (tx + x, y, zp), (tx + x, y, zp + 1.05), 0.045, rail, bev=0)
    ladder(G, (tx + hw + 0.2, 0.6, 0), -90, 0.25, zp, standoff=0.12, cage_from=4.5)
    # the conveyor gallery: tail on the ground at the left, head over the bin
    tail = Vector((-9.2, 0, 0.75))
    head = Vector((tx - 0.3, 0, zp + 0.55))
    v = head - tail
    Lc = v.length
    pitch = math.degrees(math.atan2(v.z, v.x))
    C = _empty(G, "gallery", tail, (0, -pitch, 0))
    belt = K.mat("belt", "#232527", rough=0.85, grime=0.3)
    wd = 0.55
    for s in (-1, 1):
        K.box("stringer", (Lc, 0.06, 0.22), steel, loc=(Lc / 2, s * wd, 0), bev=0.008, parent=C)
        K.box("chord", (Lc - 0.8, 0.07, 0.07), steel, loc=(Lc / 2 + 0.2, s * wd, -0.75), bev=0, parent=C)
        n = int((Lc - 0.8) / 1.4)
        for k in range(n + 1):
            x = 0.6 + k * (Lc - 0.8) / n
            beam(C, "vert", (x, s * wd, -0.75), (x, s * wd, -0.1), 0.05, brace, bev=0)
            if k < n:
                beam(C, "diag", (x, s * wd, -0.75), (x + (Lc - 0.8) / n, s * wd, -0.1), 0.045, brace, bev=0)
        for k in range(int(Lc / 2.0) + 1):
            beam(C, "walk_post", (0.5 + k * 2.0, s * (wd + 0.05), 0.1), (0.5 + k * 2.0, s * (wd + 0.05), 0.95), 0.04, rail, bev=0)
        K.box("handrail", (Lc - 0.6, 0.04, 0.04), rail, loc=(Lc / 2 + 0.2, s * (wd + 0.05), 0.95), bev=0, parent=C)
    K.box("belt_top", (Lc - 0.3, wd * 2 - 0.2, 0.015), belt, loc=(Lc / 2, 0, 0.12), bev=0, parent=C)
    K.box("belt_ret", (Lc - 0.4, wd * 2 - 0.2, 0.015), belt, loc=(Lc / 2, 0, -0.06), bev=0, parent=C)
    for k in range(int(Lc / 1.2)):
        K.cyl("idler", 0.05, wd * 2 - 0.1, _zinc(), loc=(0.5 + k * 1.2, 0, 0.07), rot=(90, 0, 0), segs=6, bev=0, parent=C)
    for k in range(int(Lc / 1.4)):
        K.box("cross", (0.05, wd * 2, 0.05), steel, loc=(0.6 + k * 1.4, 0, -0.75), bev=0, parent=C)
    K.cyl("tail_drum", 0.18, wd * 2 - 0.05, _dark(), loc=(0.1, 0, 0.04), rot=(90, 0, 0), segs=14, bev=0.01, parent=C)
    K.box("tail_frame", (0.6, wd * 2 + 0.2, 0.15), steel, loc=(0.15, 0, -0.18), bev=0.01, parent=C)
    frustum("feed", (0.7, 0.9), (1.4, 1.6), 0.7, _bone(wear=0.3), loc=(0.7, 0, 0.18), rot=(0, pitch, 0), parent=C)
    # head: a hooded drive end over the bin
    K.box("hood", (1.4, wd * 2 + 0.25, 0.7), _bone(wear=0.25), loc=(Lc - 0.3, 0, 0.3), rot=(0, pitch, 0), bev=0.02, parent=C)
    K.cyl("head_drum", 0.2, wd * 2, _dark(), loc=(Lc - 0.15, 0, 0.04), rot=(90, 0, 0), segs=14, bev=0.01, parent=C)
    K.box("drive", (0.5, 0.4, 0.4), _steel(0.2), loc=(Lc - 0.5, -wd - 0.35, 0.15), rot=(0, pitch, 0), bev=0.02, parent=C)
    K.cyl("drive_motor", 0.16, 0.45, _dark(), loc=(Lc - 1.0, -wd - 0.35, 0.15), rot=(0, 90 + pitch, 0), segs=12, bev=0.01, parent=C)

    def world(s, zl):
        p = math.radians(pitch)
        return Vector((tail.x + s * math.cos(p) - zl * math.sin(p), 0, tail.z + s * math.sin(p) + zl * math.cos(p)))

    # two bents under the gallery
    for s_ in (Lc * 0.32, Lc * 0.65):
        w = world(s_, -0.75)
        for sy in (-1, 1):
            K.box("bent_foot", (0.45, 0.45, 0.3), _concrete(0.25), loc=(w.x, sy * wd, 0.05), bev=0.02, parent=G)
            beam(G, "bent_leg", (w.x, sy * wd, 0.2), (w.x, sy * wd, w.z), 0.14, steel)
        beam(G, "bent_tie", (w.x, -wd, w.z * 0.5), (w.x, wd, w.z * 0.5), 0.08, brace, bev=0)
        beam(G, "bent_x", (w.x, -wd, 0.3), (w.x, wd, w.z * 0.5), 0.06, brace, bev=0)
    rubble(G, "spill", (tail.x + 0.6, -0.9), 0.6, 4, rnd, K.mat("gravel", GRAVEL, rough=0.95, grime=0.2))
    rubble(G, "spill2", (tx + 0.3, 0.4), 0.9, 5, rnd, K.mat("gravel", GRAVEL, rough=0.95, grime=0.2))
    return P


def hopper(seed=52):
    """A raised hopper on four legs: an open steel bin half full of gravel over a funnel, a slide gate and an inclined
    chute down to a spill heap, cross-braced legs and a caged ladder to the rim."""
    rnd = random.Random(seed)
    P = _root("hopper")
    steel = _steel(0.25)
    hw, zr, zf = 1.15, 5.0, 3.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("footing", (0.55, 0.55, 0.3), _concrete(0.25), loc=(sx * hw, sy * hw, 0.07), bev=0.03, parent=P)
            hcol(P, "leg", sx * hw, sy * hw, 0.22, zr - 0.22, steel)
    for s in (-1, 1):
        for a, b in (((-hw, s * hw, 0.5), (hw, s * hw, 2.7)), ((hw, s * hw, 0.5), (-hw, s * hw, 2.7))):
            beam(P, "xbrace", a, b, 0.07, _zinc(), bev=0)
        beam(P, "ybrace", (s * hw, -hw, 0.5), (s * hw, hw, 2.7), 0.07, _zinc(), bev=0)
        beam(P, "girt", (-hw, s * hw, 2.75), (hw, s * hw, 2.75), 0.12, steel)
        beam(P, "girt", (s * hw, -hw, 2.75), (s * hw, hw, 2.75), 0.12, steel)
    # bin walls with stiffeners, an angle rim, gravel inside
    wall = _bone(wear=0.25, grime=0.5, moss=0.08)
    hb = zr - zf
    for s in (-1, 1):
        K.box("wall_x", (2.36, 0.05, hb), wall, loc=(0, s * (hw - 0.02), zf + hb / 2), bev=0.01, parent=P)
        K.box("wall_y", (0.05, 2.36, hb), wall, loc=(s * (hw - 0.02), 0, zf + hb / 2), bev=0.01, parent=P)
        for t in (-0.4, 0.4):
            K.box("stiff", (0.1, 0.06, hb), steel, loc=(t, s * (hw + 0.03), zf + hb / 2), bev=0.005, parent=P)
            K.box("stiff", (0.06, 0.1, hb), steel, loc=(s * (hw + 0.03), t, zf + hb / 2), bev=0.005, parent=P)
        K.box("rim_x", (2.5, 0.12, 0.1), steel, loc=(0, s * hw, zr), bev=0.01, parent=P)
        K.box("rim_y", (0.12, 2.5, 0.1), steel, loc=(s * hw, 0, zr), bev=0.01, parent=P)
        K.box("ring_x", (2.5, 0.12, 0.12), steel, loc=(0, s * hw, zf), bev=0.01, parent=P)
        K.box("ring_y", (0.12, 2.5, 0.12), steel, loc=(s * hw, 0, zf), bev=0.01, parent=P)
    frustum("funnel", (0.5, 0.5), (2.32, 2.32), zf - 2.3, wall, loc=(0, 0, 2.3), parent=P)
    gravel = K.mat("gravel", GRAVEL, rough=0.95, grime=0.2)
    K.sphere("fill", 1.05, gravel, loc=(0, 0, zr - 0.55), scale=(1.05, 1.05, 0.4), parent=P)
    rubble(P, "fill_top", (0.0, 0.0), 0.8, 6, rnd, gravel, z=zr - 0.3)
    K.box("gate", (0.7, 0.7, 0.16), _dark(), loc=(0, 0, 2.22), bev=0.015, parent=P)
    K.box("gate_slide", (0.9, 0.05, 0.08), M(SIGNAL, wear=0.2), loc=(0.2, -0.38, 2.24), bev=0, parent=P)
    K.box("gate_lever", (0.05, 0.05, 0.8), _dark(), loc=(0.62, -0.42, 2.05), rot=(0, 35, 0), bev=0, parent=P)
    hazard(P, (0, -0.36, 2.2), 0.6, 0.1, M(MUSTARD, wear=0.25), _dark(), n=5)
    # the chute: a trough from under the gate down to the +x side
    a, b = Vector((0.0, 0, 2.05)), Vector((3.0, 0, 1.0))
    pitch = math.degrees(math.atan2(b.z - a.z, b.x - a.x))
    Lc = (b - a).length
    C = _empty(P, "chute", a, (0, -pitch, 0))
    K.box("chute_floor", (Lc, 0.6, 0.04), _steel(0.3), loc=(Lc / 2, 0, 0), bev=0.008, parent=C)
    for s in (-1, 1):
        K.box("chute_side", (Lc, 0.04, 0.3), _steel(0.3), loc=(Lc / 2, s * 0.3, 0.13), bev=0.008, parent=C)
    rust_patch(C, (Lc * 0.6, 0, 0.022), (0.6, 0.4, 0.004))
    beam(P, "chute_hanger", (hw, 0, 2.7), (2.2, 0, 1.4), 0.05, _zinc(), bev=0)
    rubble(P, "heap", (3.4, 0.05), 0.75, 10, rnd, gravel)
    ladder(P, (-hw - 0.2, -0.5, 0), 90, 0.22, zr, standoff=0.12)
    stencil(P, "H-2", (-0.78, -hw - 0.008, zf + 0.75), 0.28, _dark())
    return P


def crusher_machine(seed=53):
    """A jaw crusher on its concrete plinth: framed steel body, two spoked flywheels on the eccentric shaft, an open
    feed box with rock in it, the motor and a belt guard, and a heap of crushed stone under the discharge."""
    rnd = random.Random(seed)
    P = _root("crusher")
    K.box("plinth", (3.6, 2.6, 0.45), _concrete(0.3), loc=(0, 0.1, 0.12), bev=0.03, parent=P)
    z0 = 0.35
    steel = _steel(0.3)
    FR.framed_box(P, "body", (0, 0.1, z0 + 0.85), (1.6, 1.8, 1.7), M(STEEL, metal=0.35, wear=0.3, grime=0.5), _dark(), fw=0.14, screws=_zinc())
    for s in (-1, 1):
        K.box("cheek", (0.1, 1.9, 1.3), _dark(), loc=(s * 0.85, 0.1, z0 + 0.8), bev=0.02, parent=P)
    zs, ys = z0 + 1.45, 0.45
    K.cyl("shaft", 0.09, 2.6, _zinc(), loc=(0, ys, zs), rot=(0, 90, 0), segs=12, bev=0, parent=P)
    for s in (-1, 1):
        K.box("bearing", (0.3, 0.35, 0.3), _dark(), loc=(s * 0.95, ys, zs), bev=0.02, parent=P)
        F = _empty(P, "flywheel", (s * 1.18, ys, zs), (0, 90, 0))
        fw = M(SIGNAL, metal=0.3, wear=0.2, grime=0.4)
        lathe("rim", [(0.72, -0.1), (0.88, -0.1), (0.88, 0.1), (0.72, 0.1)], fw, segs=28, parent=F)
        K.cyl("hub", 0.18, 0.26, fw, segs=12, bev=0.02, parent=F)
        for k in range(5):
            a = k * 72 + 15
            K.box("spoke", (0.62, 0.1, 0.08), fw, loc=(math.cos(math.radians(a)) * 0.46, math.sin(math.radians(a)) * 0.46, 0), rot=(0, 0, a), bev=0, parent=F)
        K.box("key", (0.06, 0.06, 0.3), _dark(), loc=(0, 0.17, 0), bev=0, parent=F)
    # feed box on top, open, with rock in it
    feed = _bone(wear=0.3, grime=0.55)
    frustum("feedbox", (1.3, 1.0), (2.2, 1.9), 0.9, feed, loc=(0, -0.1, z0 + 1.7), parent=P)
    K.box("feed_rim", (2.3, 0.1, 0.1), steel, loc=(0, -1.05, z0 + 2.6), bev=0.01, parent=P)
    stone = K.mat("stone", STONE, rough=0.9, wear=0.3, grime=0.5, cracks=0.2, moss=0.2)
    stone_d = K.mat("stone_d", STONE_D, rough=0.9, grime=0.4, cracks=0.3)
    for k in range(7):
        x, y = rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.4)
        s = rnd.uniform(0.18, 0.3)
        lump(P, "rock", s, rnd.choice((stone, stone_d)), (x, y - 0.1, z0 + 2.2 + s * 0.4), rnd, flat=0.75)
    # motor behind and the belt guard down the +x side
    K.box("motor_base", (0.9, 0.7, 0.3), _dark(), loc=(1.0, 1.55, 0.5), bev=0.02, parent=P)
    K.cyl("motor", 0.32, 0.8, _steel(0.2), loc=(1.0, 1.55, 1.0), rot=(0, 90, 0), segs=16, bev=0.02, parent=P)
    G = _empty(P, "guard", (1.5, (ys + 1.55) / 2, (zs + 1.0) / 2), (math.degrees(math.atan2(zs - 1.0, ys - 1.55)) + 180, 0, 0))
    K.slab("guard_box", 0.18, 1.6, 0.7, 0.2, M(MUSTARD, wear=0.25, grime=0.45), rot=(0, 90, 0), bev=0.02, parent=G)
    hazard(P, (1.6, 1.0, z0 + 0.25), 1.4, 0.16, M(MUSTARD, wear=0.25), _dark(), n=7, axis="x")
    # discharge chute and heap at the front
    K.box("discharge", (1.0, 0.8, 0.06), _steel(0.35), loc=(0, -1.05, z0 + 0.3), rot=(-25, 0, 0), bev=0.01, parent=P)
    gravel = K.mat("gravel", GRAVEL, rough=0.95, grime=0.2)
    K.sphere("heap_core", 0.8, gravel, loc=(0, -1.6, -0.08), scale=(1.1, 0.8, 0.32), parent=P)
    rubble(P, "heap", (0, -1.65), 0.85, 10, rnd, gravel, z=0.05)
    stencil(P, "JC-40", (0.0, -0.763, z0 + 1.15), 0.18, M(BONE, wear=0.2))
    return P


# ================================================================== heavy plant wrecks
def cement_mixer_wreck(seed=54):
    """A mixer drum unit fallen off its truck: the bone drum with mustard bands lying on its subframe, its roller ring
    and pedestal, the charge hopper bent, the chute in the grass and a slab of set concrete by the mouth."""
    rnd = random.Random(seed)
    P = _root("mixer")
    U = _empty(P, "unit", (-0.1, 0, 0.0), (8, 0, 4))
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.6)
    steel = M(STEEL, metal=0.4, wear=0.6, grime=0.6)
    for s in (-1, 1):
        ibeam(U, f"rail{s}", 4.8, (0, s * 0.5, 0.0), H=0.26, F=0.14, mat=dark)
    for x in (-1.8, 0.2, 1.9):
        K.box("cross", (0.12, 1.1, 0.2), dark, loc=(x, 0, 0.13), bev=0.01, parent=U)
    # pedestal at the front (-x) and roller stand at the back
    K.box("pedestal", (0.5, 0.9, 1.25), steel, loc=(-2.05, 0, 0.85), bev=0.03, parent=U)
    K.box("gearbox", (0.45, 0.6, 0.6), dark, loc=(-1.8, 0, 1.5), bev=0.03, parent=U)
    K.box("water_tank", (0.8, 0.9, 0.6), _bone(wear=0.5, grime=0.6), loc=(-2.0, 0, 0.55), rot=(0, 0, 0), bev=0.04, parent=U)
    for s in (-1, 1):
        K.box("roller_stand", (0.25, 0.2, 0.75), steel, loc=(1.15, s * 0.45, 0.6), bev=0.02, parent=U)
        K.cyl("roller", 0.13, 0.15, dark, loc=(1.15, s * 0.42, 1.0), rot=(0, 90, 0), segs=12, bev=0.01, parent=U)
    # the drum: lathe along local z, laid along +x and rising to the mouth
    D = _empty(U, "drum", (-1.75, 0, 1.5), (0, 76, 0))
    prof = [(0.25, 0.0), (0.75, 0.12), (1.05, 0.55), (1.12, 1.5), (1.02, 2.4), (0.75, 3.25), (0.5, 3.85),
            (0.43, 3.85), (0.68, 3.25), (0.95, 2.4), (1.05, 1.5), (0.98, 0.55), (0.68, 0.18), (0.2, 0.06)]
    paint = M(BONE, wear=0.6, grime=0.65, scratches=0.6, moss=0.15)
    lathe("drum_shell", prof, paint, segs=24, parent=D)
    K.cyl("drum_inside", 0.6, 0.02, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, 3.4), segs=16, bev=0, parent=D)
    for z, r0, r1 in ((0.9, 1.085, 1.115), (2.0, 1.075, 1.03)):
        K.cyl("stripe", r0 + 0.01, 0.35, M(MUSTARD, wear=0.25, grime=0.5), loc=(0, 0, z), segs=24, r2=r1 + 0.01, bev=0, parent=D)
    K.torus("roller_ring", 0.97, 0.06, dark, loc=(0, 0, 2.75), segs=28, parent=D)
    K.cyl("drive_flange", 0.35, 0.12, dark, loc=(0, 0, -0.04), segs=14, bev=0.01, parent=D)
    # the charge hopper bent over the mouth, the chute lying off to the side
    frustum("charge_hopper", (0.35, 0.4), (0.9, 0.9), 0.6, steel, loc=(2.05, 0.15, 2.2), rot=(15, -25, 0), parent=U)
    K.box("hopper_strut", (0.06, 0.06, 1.1), dark, loc=(2.1, -0.25, 1.6), rot=(0, 15, 0), bev=0, parent=U)
    C = _empty(P, "chute", (3.0, -1.35, 0.12), (0, 4, 60))
    shell_panel("chute", 0.25, math.radians(180), math.radians(360), -0.8, 0.8, steel, t=0.03, parent=C, loc=(0, 0, 0.25))
    for s in (-1, 1):
        K.box("chute_end", (0.05, 0.04, 0.12), dark, loc=(s * 0.25, s * 0.8, 0.2), bev=0, parent=C)
    slurry = K.mat("slurry", SLURRY, rough=0.95, cracks=0.4, grime=0.3)
    K.disc("set_concrete", 0.9, slurry, (3.0, 0.5), z=0.03, segs=20, wobble=0.25, seed=seed).parent = P
    K.sphere("lump", 0.45, slurry, loc=(2.75, 0.35, 0.05), scale=(1.4, 1.1, 0.35), parent=P)
    # the ladder that went up to the hopper, bent and lying
    L = _empty(P, "ladder", (-0.4, 1.35, 0.04), (90, 0, -8))
    for s in (-1, 1):
        K.box("lrail", (2.2, 0.05, 0.04), _zinc(), loc=(0, s * 0.2, 0), bev=0, parent=L)
    for k in range(7):
        K.box("lrung", (0.03, 0.4, 0.03), _zinc(), loc=(-0.95 + k * 0.32, 0, 0), bev=0, parent=L)
    return P


def crane_mobile_wreck(seed=55):
    """A small mobile crane left where it failed: the steel-blue carrier on its tyres, an outrigger down, the mustard
    upper slewed aside with its cab and counterweight, the telescopic boom down on the ground and the hook block
    lying by its head."""
    rnd = random.Random(seed)
    P = _root("crane")
    G = _empty(P, "offset", (-1.4, -1.6, 0))
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.6)
    carrier = M(STEEL, metal=0.35, wear=0.7, grime=0.6, scratches=0.6, moss=0.1)
    paint = M(MUSTARD, wear=0.7, grime=0.6, scratches=0.7, moss=0.1)
    rim = M(BONE, metal=0.3, wear=0.7, grime=0.6)
    rw = 0.55
    for x, flats in ((2.2, (0.04, 0.2)), (-1.7, (0.05, 0.05))):
        for s, f in zip((-1, 1), flats):
            tyre(G, f"wheel{x}{s}", (x, s * 1.05, rw * (1 - f)), rw, 0.45, flat=f, rim=rim)
    for s in (-1, 1):
        K.box(f"rail{s}", (6.6, 0.18, 0.3), dark, loc=(0.0, s * 0.42, 0.75), bev=0.02, parent=G)
    K.box("deck", (6.4, 2.3, 0.35), carrier, loc=(0, 0, 1.05), bev=0.03, parent=G)
    for x in (2.2, -1.7):
        for s in (-1, 1):
            shell_panel(f"fender{x}{s}", 0.68, math.radians(10), math.radians(170), s * 0.83, s * 1.3, carrier, t=0.04, parent=G, loc=(x, 0, 0.55))
    hazard(G, (-3.21, 0, 1.05), 2.0, 0.2, M(MUSTARD, wear=0.3), dark, n=8, axis="x")
    # cab: half-width at the front left
    cased_box(G, "cab", (2.65, -0.55, 1.95), (1.3, 1.1, 1.4), _bone(wear=0.6, grime=0.6), carrier, fw=0.09)
    glass = _glass()
    K.box("windscreen", (0.03, 0.9, 0.6), glass, loc=(3.31, -0.55, 2.2), bev=0.005, parent=G)
    K.box("side_glass", (0.8, 0.03, 0.5), glass, loc=(2.55, -1.11, 2.25), bev=0.005, parent=G)
    for k in range(3):
        K.box("crack", (0.01, 0.35, 0.008), K.mat("crack", "#9fb3bd", rough=0.3), loc=(3.33, -0.4, 2.25), rot=(k * 60 + 15, 0, 0), bev=0, parent=G)
    K.box("bumper", (0.15, 2.3, 0.25), dark, loc=(3.25, 0, 0.8), bev=0.02, parent=G)
    # outriggers: boxes out of the deck, the front left down on its pad, the others half out
    for x, s, out, down in ((2.9, -1, 1.1, True), (2.9, 1, 0.4, False), (-2.9, -1, 0.5, False), (-2.9, 1, 0.3, False)):
        K.box("outrigger", (0.3, 1.0 + out, 0.25), carrier, loc=(x - 0.6 * (x < 0) + 0.3 * (x < 0), s * (0.6 + out / 2), 0.95), bev=0.015, parent=G)
        if down:
            yy = s * (1.0 + out)
            K.box("jack", (0.2, 0.2, 0.85), dark, loc=(x, yy, 0.5), bev=0.01, parent=G)
            K.box("pad", (0.6, 0.6, 0.08), dark, loc=(x, yy, 0.04), bev=0.01, parent=G)
    # upper: turntable, house, cab, counterweight, slewed aside
    yaw = 32
    K.cyl("turntable", 0.95, 0.25, dark, loc=(-0.9, 0, 1.35), segs=20, bev=0.02, parent=G)
    U = _empty(G, "upper", (-0.9, 0, 1.47), (0, 1.5, yaw))
    K.box("house", (2.6, 1.9, 0.9), paint, loc=(-0.4, 0.15, 0.6), bev=0.03, parent=U)
    FR.vent(U, (-0.7, -0.82, 0.65), 0.9, 0.5, carrier, dark)
    K.box("counterweight", (0.6, 2.0, 0.9), dark, loc=(-1.95, 0.15, 0.55), bev=0.05, parent=U)
    hazard(U, (-2.26, 0.15, 0.55), 1.8, 0.25, M(MUSTARD, wear=0.3), dark, n=8, axis="x")
    cased_box(U, "ucab", (0.5, -1.15, 0.85), (1.3, 0.8, 1.4), _bone(wear=0.6, grime=0.6), paint, fw=0.08)
    K.box("ucab_glass", (0.03, 0.62, 0.7), glass, loc=(1.16, -1.15, 1.05), bev=0.005, parent=U)
    K.box("ucab_side", (0.9, 0.03, 0.6), glass, loc=(0.5, -1.56, 1.05), bev=0.005, parent=U)
    for s in (-1, 1):
        K.box("boom_foot", (0.5, 0.12, 0.8), paint, loc=(0.75, 0.35 + s * 0.38, 1.1), bev=0.02, parent=U)
    # boom: three telescopic sections down to the ground, heel up on the house
    heel = Vector((0.8, 0.35, 1.35))
    tip = Vector((8.2, 0.35, -1.47 + 0.45))
    v = tip - heel
    pitch = math.degrees(math.atan2(v.z, v.x))
    B = _empty(U, "boom", heel, (0, -pitch, 0))
    Lb = v.length
    secs = ((0.0, 4.4, 0.72, 0.6), (4.1, 2.6, 0.58, 0.48), (6.4, 1.6, 0.46, 0.38))
    for k, (x0, L, h, w) in enumerate(secs):
        K.box(f"boom{k}", (L, w, h), paint, loc=(x0 + L / 2, 0, 0), bev=0.02, parent=B)
        K.box(f"boom_band{k}", (0.12, w + 0.02, h + 0.02), dark, loc=(x0 + L - 0.06, 0, 0), bev=0.01, parent=B)
    K.box("lift_cyl", (2.4, 0.22, 0.22), dark, loc=(1.3, 0, -0.55), rot=(0, -18, 0), bev=0.02, parent=B)
    K.cyl("lift_rod", 0.07, 1.0, _zinc(), loc=(2.6, 0, -0.2), rot=(0, 90 - 18, 0), segs=8, bev=0, parent=B)
    hazard(B, (Lb - 0.35, -0.2, 0), 0.6, 0.3, M(MUSTARD, wear=0.25), dark, n=4)
    for s in (-1, 1):
        K.box("head_plate", (0.6, 0.04, 0.6), dark, loc=(Lb + 0.2, s * 0.2, 0.05), bev=0.01, parent=B)
    K.cyl("sheave", 0.25, 0.28, dark, loc=(Lb + 0.3, 0, 0.05), rot=(90, 0, 0), segs=14, bev=0.01, parent=B)
    stencil(B, "MC-25", (2.2, -0.305, 0.05), 0.32, _dark())
    # the hook block on the ground, its rope back up to the head
    hx, hy = -1.4 + 0.0, -1.6
    wtip = Vector((-0.9, 0, 1.47)) + Vector((math.cos(math.radians(yaw)) * (heel.x + Lb * math.cos(math.radians(pitch)) + 0.3),
                                              math.sin(math.radians(yaw)) * (heel.x + Lb * math.cos(math.radians(pitch)) + 0.3), 0))
    hb = Vector((wtip.x - 0.9, wtip.y + 0.9, 0.25))
    H = _empty(G, "hookblock", hb, (90, 0, yaw + 40))
    K.box("block", (0.45, 0.6, 0.3), paint, loc=(0, 0.0, 0), bev=0.03, parent=H)
    K.cyl("block_sheave", 0.18, 0.32, dark, loc=(0, 0.1, 0), segs=12, bev=0.01, parent=H)
    K.torus("hook", 0.15, 0.04, dark, loc=(0, -0.45, 0), rot=(0, 0, 0), arc=270, segs=16, parent=H)
    K.box("hook_shank", (0.06, 0.2, 0.06), dark, loc=(0.0, -0.3, 0), bev=0, parent=H)
    rope = K.mat("rope_steel", "#4a4e52", rough=0.5, metal=0.6)
    cable(G, "rope", [(wtip.x - 0.1, wtip.y, 0.55), (wtip.x - 0.5, wtip.y + 0.5, 0.06), (hb.x, hb.y, 0.3)], 0.018, rope)
    for k in range(4):
        rust_patch(G, (rnd.uniform(-2.8, 1.8), -1.155, rnd.uniform(0.95, 1.15)), (rnd.uniform(0.2, 0.5), 0.004, 0.12))
    return P


def excavator_wreck(seed=56):
    """A tracked excavator wreck: the mustard house slewed a little on its tracks, the cab's glass broken, the boom
    still up but the stick down and the bucket resting on the ground, the hydraulic rams rusted."""
    rnd = random.Random(seed)
    P = _root("excavator")
    G = _empty(P, "offset", (-1.2, 0, 0))
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.6)
    paint = M(MUSTARD, wear=0.8, grime=0.7, scratches=0.7, moss=0.15)
    for s in (-1, 1):
        track(G, f"track{s}", 0, s * 1.05, 3.8, 0.8, 0.6, dark, seed=seed + s)
    K.box("carbody", (2.0, 1.6, 0.5), dark, loc=(0, 0, 0.6), bev=0.03, parent=G)
    K.cyl("slew_ring", 0.85, 0.22, dark, loc=(0, 0, 0.95), segs=20, bev=0.02, parent=G)
    U = _empty(G, "upper", (0, 0, 1.06), (0, 0, -14))
    K.box("deck", (3.0, 2.4, 0.2), dark, loc=(-0.3, 0, 0.1), bev=0.02, parent=U)
    K.box("engine_hood", (1.4, 2.3, 0.9), paint, loc=(-1.0, 0.0, 0.65), bev=0.04, parent=U)
    K.slab("counterweight", 0.6, 2.4, 1.0, 0.25, dark, loc=(-1.95, 0, 0.6), bev=0.03, parent=U)
    K.box("side_box", (1.2, 0.9, 0.85), paint, loc=(0.6, 0.72, 0.6), bev=0.03, parent=U)
    FR.vent(U, (-1.0, -1.17, 0.7), 1.0, 0.55, M(STEEL, metal=0.4, wear=0.4), dark)
    rod(U, "exhaust", (-0.7, 0.6, 1.0), (-0.65, 0.62, 1.55), 0.05, _rust(), segs=8)
    # cab front left
    cab = _bone(wear=0.6, grime=0.6)
    K.box("cab_floor", (1.1, 0.95, 0.12), dark, loc=(0.55, -0.68, 0.26), bev=0.01, parent=U)
    for x in (0.02, 1.08):
        for y in (-1.13, -0.23):
            K.box("cab_post", (0.07, 0.07, 1.45), cab, loc=(x, y, 0.98), bev=0.01, parent=U)
    K.box("cab_roof", (1.16, 1.0, 0.08), paint, loc=(0.55, -0.68, 1.72), bev=0.02, parent=U)
    K.box("cab_back", (0.04, 0.9, 1.4), cab, loc=(0.02, -0.68, 0.98), bev=0.01, parent=U)
    K.box("cab_door", (1.0, 0.04, 0.75), cab, loc=(0.55, -1.13, 0.68), bev=0.01, parent=U)
    glass = _glass()
    K.box("cab_glass_side", (0.9, 0.02, 0.6), glass, loc=(0.55, -1.13, 1.35), bev=0.004, parent=U)
    K.box("cab_glass_front", (0.02, 0.5, 0.5), glass, loc=(1.08, -0.85, 1.35), bev=0.004, parent=U)
    K.box("seat", (0.45, 0.45, 0.5), K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4), loc=(0.35, -0.68, 0.55), bev=0.04, parent=U)
    stencil(U, "EX-9", (-1.0, -1.155, 0.35), 0.18, _dark())
    # boom (two pieces), stick down to the bucket on the ground
    root = Vector((1.0, 0.25, 0.75))
    knee = Vector((2.5, 0.25, 2.55))
    btip = Vector((3.95, 0.25, 2.05))
    bucket = Vector((4.75, 0.25, -0.62))
    paint_b = M(MUSTARD, wear=0.7, grime=0.6, scratches=0.6)
    beam(U, "boom1", root, knee, 0.5, paint_b, d=0.6, bev=0.03)
    beam(U, "boom2", knee, btip, 0.45, paint_b, d=0.55, bev=0.03)
    K.box("boom_knee", (0.55, 0.5, 0.6), paint_b, loc=knee, bev=0.03, parent=U)
    beam(U, "stick", btip, bucket + Vector((0, 0, 0.35)), 0.38, paint_b, d=0.42, bev=0.03)

    def ram(a, b, f=0.55):
        a, b = Vector(a), Vector(b)
        m = a.lerp(b, f)
        rod(U, "ram_barrel", a, m, 0.1, dark, segs=10)
        rod(U, "ram_rod", m, b, 0.05, _rust(), segs=8)

    ram((0.95, -0.05, 0.4), (1.9, -0.05, 1.9))
    ram((0.95, 0.55, 0.4), (1.9, 0.55, 1.9))
    ram((2.3, 0.25, 2.95), (3.9, 0.25, 2.6), 0.6)
    ram((4.1, 0.25, 1.6), (4.65, 0.25, 0.25))
    B = _empty(U, "bucket", bucket, (0, 0, 0))
    K.box("bucket_back", (0.12, 0.95, 0.75), paint_b, loc=(-0.15, 0, 0.42), rot=(0, -15, 0), bev=0.02, parent=B)
    K.box("bucket_floor", (0.8, 0.95, 0.08), dark, loc=(0.25, 0, 0.06), bev=0.015, parent=B)
    for s in (-1, 1):
        plate_xz(f"bucket_side{s}", [(-0.25, 0.02), (0.65, 0.02), (0.15, 0.6), (-0.1, 0.8)], 0.05, paint_b, loc=(0, s * 0.47, 0), parent=B)
    for k in range(5):
        K.box("tooth", (0.18, 0.08, 0.06), dark, loc=(0.72, -0.36 + k * 0.18, 0.05), bev=0.01, parent=B)
    for k in range(5):
        rust_patch(U, (rnd.uniform(-1.6, -0.4), -1.155, rnd.uniform(0.4, 1.0)), (rnd.uniform(0.15, 0.4), 0.004, rnd.uniform(0.08, 0.2)))
    return P


def bulldozer_wreck(seed=57):
    """A crawler dozer wreck: mustard hood and an open roll-over frame, the curved blade down on the ground on its push
    arms, a ripper at the back, its left track thrown and lying out behind it."""
    rnd = random.Random(seed)
    P = _root("dozer")
    G = _empty(P, "offset", (-0.4, 0, 0))
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.6)
    paint = M(MUSTARD, wear=0.8, grime=0.7, scratches=0.7, moss=0.15)
    track(G, "track_r", 0, 1.0, 3.2, 0.85, 0.5, dark, seed=seed)
    track(G, "track_l", 0, -1.0, 3.2, 0.85, 0.5, dark, seed=seed + 1, broken=True)
    K.box("frame", (3.4, 1.5, 0.5), dark, loc=(0.1, 0, 0.75), bev=0.03, parent=G)
    K.box("hood", (1.8, 1.25, 0.85), paint, loc=(0.85, 0, 1.43), bev=0.04, parent=G)
    K.box("hood_top", (1.7, 1.1, 0.05), paint, loc=(0.85, 0, 1.88), bev=0.01, parent=G)
    K.box("grille", (0.08, 1.05, 0.7), dark, loc=(1.77, 0, 1.38), bev=0.01, parent=G)
    for k in range(6):
        K.box("grille_bar", (0.03, 0.98, 0.03), paint, loc=(1.82, 0, 1.12 + k * 0.1), bev=0, parent=G)
    FR.vent(G, (0.85, -0.64, 1.45), 1.1, 0.45, M(STEEL, metal=0.4, wear=0.4), dark)
    beam(G, "stack", (1.2, 0.35, 1.88), (1.05, 0.4, 2.55), 0.06, _rust(), bev=0)
    K.box("seat_box", (1.4, 1.5, 0.6), paint, loc=(-0.85, 0, 1.3), bev=0.03, parent=G)
    K.box("fuel_tank", (0.55, 1.5, 0.7), paint, loc=(-1.65, 0, 1.35), bev=0.03, parent=G)
    K.box("seat", (0.5, 0.5, 0.45), K.mat("seatcloth", GRAPHITE, rough=0.9, grime=0.4), loc=(-0.8, 0, 1.8), bev=0.04, parent=G)
    K.box("levers", (0.1, 0.5, 0.35), dark, loc=(-0.4, 0, 1.75), bev=0, parent=G)
    # roll-over frame: four posts and a roof, one rear post bent
    for x in (-0.25, -1.45):
        for s in (-1, 1):
            if x < -1 and s > 0:
                beam(G, "rops_bent", (x, s * 0.7, 1.6), (x + 0.2, s * 0.62, 2.4), 0.1, paint, bev=0.01)
                beam(G, "rops_bent2", (x + 0.2, s * 0.62, 2.4), (x, s * 0.7, 2.9), 0.1, paint, bev=0.01)
            else:
                beam(G, "rops", (x, s * 0.7, 1.6), (x, s * 0.7, 2.9), 0.1, paint, bev=0.01)
    K.box("roof", (1.5, 1.6, 0.08), paint, loc=(-0.85, 0, 2.94), rot=(3, -2, 0), bev=0.02, parent=G)
    # blade on the ground, push arms back to the track frames, lift rams to the hood
    bx = 2.55
    shell_panel("blade", 0.78, math.radians(118), math.radians(242), -1.55, 1.55, paint, t=0.06, parent=G, loc=(bx + 0.38, 0, 0.68))
    K.box("cutting_edge", (0.12, 3.1, 0.12), dark, loc=(bx, 0, 0.06), bev=0.01, parent=G)
    K.box("blade_top", (0.25, 3.1, 0.1), dark, loc=(bx - 0.0, 0, 1.36), bev=0.01, parent=G)
    for s in (-1, 1):
        K.box("end_bit", (0.3, 0.06, 1.25), dark, loc=(bx + 0.12, s * 1.56, 0.66), bev=0.01, parent=G)
        beam(G, "push_arm", (bx - 0.15, s * 1.0, 0.4), (-0.3, s * 1.0, 0.5), 0.18, dark, bev=0.015)
        rod(G, "lift_barrel", (1.6, s * 0.75, 1.75), (2.2, s * 0.85, 1.3), 0.08, dark, segs=10)
        rod(G, "lift_rod", (2.2, s * 0.85, 1.3), (bx - 0.1, s * 0.9, 1.15), 0.04, _rust(), segs=8)
    for k in range(3):
        K.box("blade_rib", (0.08, 0.06, 1.1), dark, loc=(bx - 0.12, -1.0 + k * 1.0, 0.7), rot=(0, 18, 0), bev=0, parent=G)
    # ripper at the back
    K.box("ripper_beam", (0.25, 1.2, 0.25), dark, loc=(-2.3, 0, 0.75), bev=0.02, parent=G)
    for s in (-1, 1):
        beam(G, "ripper_arm", (-1.6, s * 0.5, 0.7), (-2.3, s * 0.5, 0.75), 0.15, dark)
    K.box("shank", (0.12, 0.12, 0.9), dark, loc=(-2.42, 0, 0.35), rot=(0, -15, 0), bev=0.01, parent=G)
    # the thrown track lying out behind on the left
    for k in range(16):
        x = -2.0 - k * 0.2
        K.box("loose_shoe", (0.08, 0.54, 0.05), dark, loc=(x, -1.15 - k * 0.04, 0.025), rot=(0, 0, rnd.uniform(-8, 8) - 10), bev=0, parent=G)
    K.box("loose_link", (3.2, 0.1, 0.04), dark, loc=(-3.5, -1.45, 0.05), rot=(0, 0, -11), bev=0, parent=G)
    stencil(G, "D-6", (0.85, -0.63, 1.82), 0.16, _dark())
    for k in range(5):
        s = rnd.choice((-1, 1))
        rust_patch(G, (rnd.uniform(0.1, 1.6), s * 0.628, rnd.uniform(1.1, 1.75)), (rnd.uniform(0.15, 0.35), 0.004, rnd.uniform(0.08, 0.2)))
    return P


def dump_truck_wreck(seed=58):
    """A big rigid haul truck wreck: steel-blue body raised on its hoist rams, bone cab and upper deck over the engine,
    giant tyres on mustard rims, one gone flat, rails and stairs up the front."""
    rnd = random.Random(seed)
    P = _root("dumptruck")
    G = _empty(P, "offset", (0.3, 0, 0))
    dark = M(GRAPHITE, metal=0.4, wear=0.5, grime=0.6)
    bone = _bone(wear=0.7, grime=0.65, scratches=0.6, moss=0.1)
    bed = M(STEEL, metal=0.35, wear=0.75, grime=0.6, scratches=0.7, moss=0.1)
    rim = M(MUSTARD, metal=0.3, wear=0.6, grime=0.6)
    rw = 1.0
    xf, xr = 2.4, -1.9
    tyre(G, "front_l", (xf, -1.35, rw * 0.97), rw, 0.7, flat=0.03, rim=rim, lugs=6)
    tyre(G, "front_r", (xf, 1.35, rw * 0.97), rw, 0.7, flat=0.03, rim=rim, lugs=6)
    for s, f in ((-1, 0.22), (1, 0.03)):
        for k in (0, 1):
            tyre(G, f"rear{s}{k}", (xr, s * (0.95 + k * 0.74), rw * (1 - f)), rw, 0.7, flat=f, rim=rim, lugs=0)
    for s in (-1, 1):
        K.box(f"rail{s}", (6.8, 0.25, 0.5), dark, loc=(0, s * 0.5, 1.25), bev=0.02, parent=G)
    K.box("axle_r", (0.5, 2.6, 0.5), dark, loc=(xr, 0, 0.9), bev=0.03, parent=G)
    K.box("axle_f", (0.4, 2.2, 0.35), dark, loc=(xf, 0, 0.95), bev=0.02, parent=G)
    # front: engine box, radiator, upper deck, cab on the left, stairs and rails
    K.box("engine", (1.8, 1.6, 1.4), bone, loc=(2.7, 0, 1.9), bev=0.04, parent=G)
    K.box("radiator", (0.15, 2.0, 1.5), dark, loc=(3.65, 0, 1.95), bev=0.02, parent=G)
    for k in range(7):
        K.box("rad_bar", (0.04, 1.85, 0.05), bone, loc=(3.74, 0, 1.35 + k * 0.2), bev=0, parent=G)
    K.box("bumper", (0.4, 3.6, 0.45), dark, loc=(3.75, 0, 0.95), bev=0.03, parent=G)
    zd = 2.65
    K.box("upper_deck", (1.6, 4.0, 0.08), M(ZINC, metal=0.55, wear=0.3, grime=0.6), loc=(3.05, 0, zd), bev=0.01, parent=G)
    rail = M(MUSTARD, metal=0.3, wear=0.3, grime=0.4)
    for y in (-1.95, -0.65, 0.65, 1.95):
        beam(G, "rail_post", (3.82, y, zd), (3.82, y, zd + 1.0), 0.05, rail, bev=0)
    for z in (0.5, 1.0):
        beam(G, "rail", (3.82, -1.95, zd + z), (3.82, 1.95, zd + z), 0.045, rail, bev=0)
    cased_box(G, "cab", (2.7, -1.15, zd + 0.85), (1.5, 1.4, 1.7), bone, M(STEEL, metal=0.35, wear=0.6), fw=0.1)
    glass = _glass()
    K.box("cab_front", (0.03, 1.2, 0.8), glass, loc=(3.46, -1.15, zd + 1.15), bev=0.005, parent=G)
    K.box("cab_side", (1.1, 0.03, 0.7), glass, loc=(2.7, -1.86, zd + 1.15), bev=0.005, parent=G)
    for k in range(3):
        K.box("crack", (0.01, 0.45, 0.008), K.mat("crack", "#9fb3bd", rough=0.3), loc=(3.48, -1.0, zd + 1.2), rot=(k * 60 + 10, 0, 0), bev=0, parent=G)
    ladder(G, (4.17, 1.55), 90, 1.18, zd, cage_from=None, standoff=0.22, top_ext=1.0)
    stencil(G, "HT-90", (2.7, -1.843, zd + 0.3), 0.22, _dark())
    # body: raised about its rear hinge
    B = _empty(G, "body", (-3.35, 0, 1.9), (0, -38, 0))
    W, Lb, Hs = 3.6, 6.3, 1.5
    K.box("floor", (Lb, W, 0.12), bed, loc=(Lb / 2, 0, 0.06), bev=0.02, parent=B)
    for s in (-1, 1):
        K.box(f"side{s}", (Lb - 0.2, 0.1, Hs), bed, loc=(Lb / 2 + 0.1, s * W / 2, Hs / 2), bev=0.02, parent=B)
        K.box(f"side_rail{s}", (Lb - 0.2, 0.2, 0.15), dark, loc=(Lb / 2 + 0.1, s * (W / 2 + 0.05), Hs), bev=0.015, parent=B)
        for k in range(4):
            K.box(f"rib{s}{k}", (0.12, 0.12, Hs - 0.1), dark, loc=(0.9 + k * 1.4, s * (W / 2 + 0.06), Hs / 2), bev=0.01, parent=B)
        K.box(f"hinge{s}", (0.35, 0.3, 0.35), dark, loc=(0.0, s * 0.5, -0.1), bev=0.02, parent=B)
    K.box("tail_slope", (0.9, W, 0.1), bed, loc=(0.35, 0, 0.25), rot=(0, 30, 0), bev=0.02, parent=B)
    K.box("front_wall", (0.12, W, Hs + 0.5), bed, loc=(Lb - 0.05, 0, (Hs + 0.5) / 2), bev=0.02, parent=B)
    K.box("canopy", (1.5, W + 0.1, 0.1), bed, loc=(Lb + 0.7, 0, Hs + 0.5), bev=0.02, parent=B)
    hazard(B, (Lb + 1.46, 0, Hs + 0.5), 3.4, 0.12, M(MUSTARD, wear=0.3), dark, n=10, axis="x")
    for k in range(3):
        K.box("under_rib", (0.15, W - 0.4, 0.2), dark, loc=(1.5 + k * 1.6, 0, -0.08), bev=0.01, parent=B)
    for k in range(3):
        rust_patch(B, (rnd.uniform(0.6, Lb - 0.6), -W / 2 - 0.052, rnd.uniform(0.2, 0.5)), (rnd.uniform(0.4, 0.8), 0.004, rnd.uniform(0.1, 0.2)))
    rust_patch(B, (3.0, 0.3, 0.125), (1.2, 1.5, 0.004))
    # hoist rams from the frame to the body underside
    for s in (-1, 1):
        a = Vector((0.6, s * 0.5, 1.4))
        p = math.radians(38)
        lb = 3.0
        b = Vector((-3.35 + lb * math.cos(p) + 0.2 * math.sin(p), s * 0.5, 1.9 + lb * math.sin(p) - 0.2 * math.cos(p)))
        m = a.lerp(b, 0.45)
        rod(G, "ram_barrel", a, m, 0.14, dark, segs=12)
        rod(G, "ram_stage", m, a.lerp(b, 0.75), 0.1, _zinc(), segs=10)
        rod(G, "ram_rod", a.lerp(b, 0.75), b, 0.07, _rust(), segs=8)
    return P


# ================================================================== yard stock
def steel_beam_stack(seed=59):
    """A stack of 5 m I-beams on timber bearers: four, then three, then two (one tipped on its side), spacers between
    the layers, mustard paint marks on the ends and rust along the flanges."""
    rnd = random.Random(seed)
    P = _root("beamstack")
    L, H, F = 5.0, 0.3, 0.16
    xs = (-1.8, 0.0, 1.8)
    for k, x in enumerate(xs):
        K.box(f"bearer{k}", (0.2, 1.7, 0.18), wood(k, wear=0.5), loc=(x, 0, 0.09), rot=(0, 0, rnd.uniform(-3, 3)), bev=0.015, parent=P)
    steel = M(STEEL, metal=0.45, wear=0.3, grime=0.55, scratches=0.6)
    z = 0.18
    for layer, n in enumerate((4, 3, 2)):
        if layer:
            for k, x in enumerate(xs):
                K.box("spacer", (0.1, 0.25 * n + 0.3, 0.08), wood(k + layer, wear=0.5), loc=(x + 0.15, 0, z + 0.04), bev=0.01, parent=P)
            z += 0.08
        for k in range(n):
            y = (k - (n - 1) / 2) * 0.32
            dx = rnd.uniform(-0.15, 0.15)
            yaw = rnd.uniform(-1.5, 1.5)
            if layer == 2 and k == 1:
                ib = ibeam(P, f"beam{layer}{k}", L, (dx, y + 0.1, z + F / 2), (90, 0, yaw), H=H, F=F, mat=steel)
                ib.location.y += H / 2
            else:
                ib = ibeam(P, f"beam{layer}{k}", L, (dx, y, z), (0, 0, yaw), H=H, F=F, mat=steel)
            for s in (-1, 1):
                K.box("end_mark", (0.02, F + 0.004, H * 0.6), M(MUSTARD, wear=0.2), loc=(s * (L / 2 + 0.003), 0, H / 2), bev=0, parent=ib)
            rust_patch(ib, (rnd.uniform(-1.5, 1.5), 0, H + 0.002), (rnd.uniform(0.3, 0.7), F * 0.6, 0.004))
        z += H
    stencil(P, "W300", (-0.8, -0.48 - 0.008, 0.18 + H / 2), 0.08, M(BONE, wear=0.2), extrude=0.004)
    return P


def rebar_bundle(seed=60):
    """Three bundles of rusty 4 m rebar tied with wire on timber blocks, two below and one on top, tag plates on the
    ties and a few bent bars pulled out on the ground."""
    rnd = random.Random(seed)
    P = _root("rebar")
    bar = M(RUST, metal=0.45, wear=0.2, grime=0.5, rough=0.8)
    tie = M(GRAPHITE, metal=0.5, wear=0.1)
    for k, x in enumerate((-1.4, 0.0, 1.4)):
        K.box(f"block{k}", (0.22, 0.9, 0.16), wood(k, wear=0.5), loc=(x, 0, 0.08), rot=(0, 0, rnd.uniform(-4, 4)), bev=0.015, parent=P)
    rb, pitch = 0.016, 0.036
    hexes = [(0, 0)] + [(math.cos(a) * pitch, math.sin(a) * pitch) for a in (k * math.pi / 3 for k in range(6))]
    hexes += [(math.cos(a) * pitch * 2, math.sin(a) * pitch * 2) for a in (k * math.pi / 6 for k in range(12))]
    R = pitch * 2 + rb
    for b, (by, bz) in enumerate(((-0.17, 0.16 + R), (0.17, 0.16 + R), (0.0, 0.16 + R + R * 1.72))):
        dx = rnd.uniform(-0.15, 0.15)
        for k, (u, v) in enumerate(hexes):
            L = 4.0 + rnd.uniform(-0.12, 0.12)
            K.cyl("bar", rb, L, bar, loc=(dx + rnd.uniform(-0.05, 0.05), by + u, bz + v), rot=(0, 90, 0), segs=6, bev=0, parent=P)
        for t in (-1.2, 0.0, 1.2):
            K.cyl("tie", R + 0.006, 0.025, tie, loc=(dx + t, by, bz), rot=(0, 90, 0), segs=10, bev=0, parent=P)
        K.box("tag", (0.005, 0.08, 0.06), M(MUSTARD, wear=0.2), loc=(dx + 1.2, by - 0.04, bz - R - 0.04), bev=0, parent=P)
    for k in range(3):
        y = -0.8 - k * 0.12
        a = Vector((rnd.uniform(-1.8, -0.5), y, 0.02))
        b = a + Vector((rnd.uniform(1.4, 2.0), rnd.uniform(-0.15, 0.15), 0))
        c = b + Vector((rnd.uniform(0.3, 0.6), rnd.uniform(-0.6, -0.3), rnd.uniform(0.0, 0.2)))
        rod(P, "loose", a, b, rb, bar, segs=6)
        rod(P, "loose_bend", b, c, rb, bar, segs=6)
    return P


def scaffold_tower(seed=61):
    """A 6 m tube-and-fitting scaffold tower: four galvanised standards on base plates and sole boards, ledgers and
    transoms at each lift, zigzag face braces, a full top deck with toe boards and rails, a middle deck missing a
    plank (it leans against the foot), and a ladder up the inside."""
    rnd = random.Random(seed)
    P = _root("scaffold")
    tube = M(ZINC, metal=0.6, wear=0.15, grime=0.45)
    r = 0.024
    hx, hy = 1.25, 0.65
    lifts = (0.2, 1.85, 3.45, 5.0)
    top = lifts[-1] + 1.0
    for sx in (-1, 1):
        K.box(f"sole{sx}", (0.25, 1.8, 0.05), wood(sx + 1, wear=0.5), loc=(sx * hx, 0, 0.025), bev=0.01, parent=P)
        for sy in (-1, 1):
            K.box("base_plate", (0.15, 0.15, 0.01), _dark(), loc=(sx * hx, sy * hy, 0.055), bev=0, parent=P)
            rod(P, "standard", (sx * hx, sy * hy, 0.06), (sx * hx, sy * hy, top), r, tube, segs=6)
    rows = list(lifts) + [lifts[-1] + 0.5, top - 0.02]
    for z in rows:
        for s in (-1, 1):
            rod(P, "ledger", (-hx - 0.1, s * hy, z), (hx + 0.1, s * hy, z), r, tube, segs=6)
            if z in lifts or z == top - 0.02:
                rod(P, "transom", (s * hx, -hy - 0.1, z + 0.05), (s * hx, hy + 0.1, z + 0.05), r, tube, segs=6)
    for i in range(len(lifts) - 1):
        z0, z1 = lifts[i], lifts[i + 1]
        d = 1 if i % 2 == 0 else -1
        for s in (-1, 1):
            rod(P, "brace", (-d * hx, s * (hy + 0.03), z0), (d * hx, s * (hy + 0.03), z1), r, tube, segs=6)
    for (x, y, z) in ((-hx, -hy, 1.85), (hx, -hy, 3.45), (-hx, hy, 5.0), (hx, hy, 0.2), (hx, -hy, 5.0)):
        K.box("coupler", (0.07, 0.07, 0.07), _dark(), loc=(x, y, z), bev=0.005, parent=P)
    # decks: the top one full with toe boards, the middle one with a plank gone
    for k in range(5):
        y = -hy + 0.13 + k * 0.26
        plank(P, f"top_plank{k}", (2.7, 0.24, 0.045), (rnd.uniform(-0.04, 0.04), y, lifts[-1] + 0.1), shade=k)
    for s in (-1, 1):
        plank(P, f"toe{s}", (2.7, 0.03, 0.15), (0, s * (hy + 0.04), lifts[-1] + 0.2), shade=2)
    for k in (0, 1, 3):
        y = -hy + 0.13 + k * 0.26
        plank(P, f"mid_plank{k}", (2.7, 0.24, 0.045), (rnd.uniform(-0.05, 0.05), y, lifts[2] + 0.1), shade=k + 1)
    plank(P, "fallen_plank", (2.7, 0.24, 0.045), (hx + 0.35, -0.2, 1.2), rot=(0, -64, 4), shade=3)
    # ladder inside, up to the middle deck, lashed to the transom
    Lg = _empty(P, "ladder", (-hx + 0.35, 0.25, 0), (0, 0, 0))
    lad = M(ZINC, metal=0.5, wear=0.2, grime=0.4)
    zt = lifts[2] + 1.0
    for s in (-1, 1):
        rod(Lg, "lrail", (0.0, s * 0.2, 0.06), (0.35, s * 0.2, zt), 0.022, lad, segs=6)
    for k in range(1, 15):
        t = k / 15
        K.cyl("lrung", 0.014, 0.4, lad, loc=(0.35 * t, 0, 0.06 + (zt - 0.06) * t), rot=(90, 0, 0), segs=6, bev=0, parent=Lg)
    K.box("tag", (0.25, 0.01, 0.18), M(SIGNAL, wear=0.2), loc=(0, -hy - 0.035, 1.3), bev=0, parent=P)
    return P


def cable_drum_stack(seed=62):
    """Three wooden cable drums: two standing on their flanges against chocks, a third in the cradle on top, each with
    a different cable, steel straps across the flanges and a tail of cable down to the grass."""
    P = _root("drums")
    R, Wd = 0.75, 0.9
    cables = (CABLE, SIGNAL, STEEL)
    centres = ((-0.8, 0, R), (0.8, 0, R), (0.0, 0, R + math.sqrt((2 * R) ** 2 - 0.8 ** 2) - 0.02))
    for k, ((x, y, z), col) in enumerate(zip(centres, cables)):
        G = _empty(P, f"drum{k}", (x, y, z), (90, 0, 0))
        fl = wood(k, wear=0.45)
        for s in (-1, 1):
            K.cyl(f"flange{k}{s}", R, 0.07, fl, loc=(0, 0, s * (Wd / 2 - 0.035)), segs=20, bev=0.015, parent=G)
            for a in (0, 90):
                K.box("strap", (2 * R - 0.08, 0.06, 0.012), _steel(0.2), loc=(0, 0, s * (Wd / 2 + 0.002)), rot=(0, 0, a + 20 * k), bev=0, parent=G)
            K.cyl("bush", 0.1, 0.02, _dark(), loc=(0, 0, s * (Wd / 2 + 0.008)), segs=12, bev=0, parent=G)
            K.cyl("hole", 0.06, 0.01, K.mat("soot", SOOT, rough=0.95), loc=(0, 0, s * (Wd / 2 + 0.019)), segs=10, bev=0, parent=G)
            for j in range(4):
                a = 2 * math.pi * j / 4 + 0.3
                nut(G, (math.cos(a) * 0.3, math.sin(a) * 0.3, s * (Wd / 2 + 0.01)), r=0.022)
        K.cyl(f"cable{k}", R - 0.12 - 0.04 * k, Wd - 0.14, K.mat(f"cable{col}", col, rough=0.55, grime=0.25), segs=20, bev=0.02, parent=G)
        for j in range(3):
            K.cyl("turn", R - 0.115 - 0.04 * k, 0.04, K.mat(f"cable{col}", col, rough=0.55, grime=0.25), loc=(0, 0, -0.25 + j * 0.25), segs=20, bev=0, parent=G)
    for s in (-1, 1):
        for y in (-0.3, 0.3):
            K.box("chock", (0.3, 0.2, 0.2), wood(3, wear=0.5), loc=(s * 1.45, y, 0.1), rot=(0, s * -20, 0), bev=0.015, parent=P)
    tail = [(0.8 + 0.3, -0.35, R + 0.55), (1.35, -0.6, 0.6), (1.55, -0.85, 0.04), (2.1, -1.2, 0.04), (2.4, -0.9, 0.04)]
    cable(P, "tail", tail, 0.025, K.mat(f"cable{SIGNAL}", SIGNAL, rough=0.55, grime=0.25))
    stencil(P, "CW", (-0.8, -Wd / 2 - 0.012, R + 0.35), 0.16, M(BONE, wear=0.2), extrude=0.004)
    return P


def barrel_pallet(seed=63):
    """Four steel-blue drums on a pallet, strapped round with two mustard bands, a stencilled number on each."""
    rnd = random.Random(seed)
    P = _root("barrelpallet")
    _pallet(P, (0, 0, 0), rnd)
    top = 0.144
    lid = M(BONE, metal=0.3, wear=0.2, grime=0.3)
    pos = ((-0.3, -0.25), (0.3, -0.25), (-0.3, 0.25), (0.3, 0.25))
    for k, (x, y) in enumerate(pos):
        drum(P, f"drum{k}", (x + rnd.uniform(-0.01, 0.01), y, top + 0.44), (0, 0, rnd.uniform(0, 360)), col=STEEL, lid=lid)
        if y < 0:
            stencil(P, f"{k + 1}", (x, y - 0.292, top + 0.6), 0.14, M(BONE, wear=0.2), extrude=0.003)
    strap = M(MUSTARD, wear=0.1, grime=0.3, rough=0.6)
    for z in (top + 0.25, top + 0.65):
        for s in (-1, 1):
            K.box("strap_x", (0.6, 0.006, 0.045), strap, loc=(0, s * 0.545, z), bev=0, parent=P)
            K.box("strap_y", (0.006, 0.5, 0.045), strap, loc=(s * 0.595, 0, z), bev=0, parent=P)
        for (x, y), a0 in (((0.3, 0.25), 0), ((-0.3, 0.25), 90), ((-0.3, -0.25), 180), ((0.3, -0.25), 270)):
            K.torus("strap_corner", 0.297, 0.006, strap, loc=(x, y, z), rot=(0, 0, a0), arc=90, segs=16, parent=P)
        K.box("buckle", (0.07, 0.014, 0.06), _zinc(), loc=(0.12, -0.552, z), bev=0, parent=P)
    return P


def chemical_drums(seed=64):
    """Five chemical drums, two standing and three toppled, one leaking into a dark green pool; mustard hazard diamonds
    on every drum and the grass around the pool dead."""
    rnd = random.Random(seed)
    P = _root("chemdrums")
    spill = K.mat("spill", SPILL, rough=0.08, metal=0.0)
    K.disc("pool", 1.25, spill, (0.35, -0.3), z=0.012, segs=28, wobble=0.3, seed=seed).parent = P
    K.disc("pool2", 0.45, spill, (1.6, -0.9), z=0.016, segs=16, wobble=0.3, seed=seed + 1).parent = P
    K.disc("dead_grass", 1.8, K.mat("deadgrass", "#8a7d4e", rough=1.0), (0.4, -0.3), z=0.006, segs=28, wobble=0.25, seed=seed + 2).parent = P
    drums = (
        ("d0", (-0.9, 0.55, 0.44), (0, 0, 20), MUSTARD),
        ("d1", (-0.3, 0.75, 0.44), (0, 0, -40), STEEL),
        ("d2", (0.0, 0.0, 0.31), (90, 0, 200), MUSTARD),     # leaking, its bung end towards the pool
        ("d3", (1.0, 0.45, 0.31), (90, 0, 115), MUSTARD),
        ("d4", (-1.25, -0.35, 0.31), (90, 0, 75), BONE),
    )
    for k, (nm, loc, rot, col) in enumerate(drums):
        G = drum(P, nm, loc, rot, col=col, wear=0.3)
        hazard_diamond(G, (0, -0.296, 0.05), (0, 0, 0), s=0.22, mark="!" if k % 2 == 0 else "drop")
        hazard_diamond(G, (0.296, 0, 0.05), (0, 0, 90), s=0.22)
        if col != BONE:
            rust_patch(G, (rnd.uniform(-0.1, 0.1), 0.293, rnd.uniform(-0.35, 0.0)), (0.18, 0.004, 0.14))
    K.disc("drip", 0.12, spill, (0.25, -0.12), z=0.31 - 0.25, segs=10).parent = P
    return P


def hazard_waste_bin(seed=65):
    """A sealed hazardous-waste container on skids: framed bone box with hazard stripes round its belly, a ridged steel
    lid clamped down with dogs and a red seal tag, corner lifting lugs and a hazard placard."""
    P = _root("wastebin")
    L, W, H = 2.6, 1.6, 1.3
    dark = _dark()
    for s in (-1, 1):
        K.box(f"skid{s}", (L + 0.1, 0.18, 0.16), dark, loc=(0, s * 0.55, 0.08), bev=0.015, parent=P)
    for x in (-0.6, 0.6):
        K.box("fork_pocket", (0.3, W, 0.16), dark, loc=(x, 0, 0.24), bev=0.01, parent=P)
    z0 = 0.32
    FR.framed_box(P, "bin", (0, 0, z0 + H / 2), (L, W, H), _bone(wear=0.3, grime=0.55), _steel(0.3), fw=0.1, screws=_zinc())
    for s in (-1, 1):
        hazard(P, (0, s * (W / 2 - 0.028), z0 + 0.45), L - 0.3, 0.22, M(MUSTARD, wear=0.25), dark, n=12)
        hazard(P, (s * (L / 2 - 0.028), 0, z0 + 0.45), W - 0.3, 0.22, M(MUSTARD, wear=0.25), dark, n=7, axis="x")
    # lid: two raked halves meeting at a ridge, dogs along the front and back
    zt = z0 + H
    for s in (-1, 1):
        K.box(f"lid{s}", (L + 0.06, W / 2 + 0.05, 0.06), _steel(0.3), loc=(0, s * W / 4, zt + 0.08), rot=(s * -7, 0, 0), bev=0.015, parent=P)
    K.box("ridge", (L + 0.08, 0.1, 0.06), dark, loc=(0, 0, zt + 0.14), bev=0.01, parent=P)
    for s in (-1, 1):
        for x in (-0.9, -0.3, 0.3, 0.9):
            K.box("dog", (0.08, 0.06, 0.2), dark, loc=(x, s * (W / 2 + 0.07), zt - 0.02), bev=0.005, parent=P)
            K.cyl("dog_nut", 0.035, 0.05, _zinc(), loc=(x, s * (W / 2 + 0.07), zt + 0.1), segs=6, bev=0, parent=P)
    K.box("seal", (0.03, 0.01, 0.07), M(SIGNAL, wear=0.1), loc=(0.3, -W / 2 - 0.105, zt - 0.08), bev=0, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.torus("lug", 0.07, 0.018, dark, loc=(sx * (L / 2 - 0.05), sy * (W / 2 - 0.05), zt + 0.08), rot=(90, 0, 0), arc=180, segs=10, parent=P)
    hazard_diamond(P, (-0.6, -W / 2 + 0.036, z0 + 0.92), (0, 0, 0), s=0.4)
    stencil(P, "HZ-4", (0.5, -W / 2 + 0.037, z0 + 0.92), 0.22, dark)
    rust_patch(P, (1.0, -W / 2 - 0.008, z0 + 0.2), (0.3, 0.004, 0.15))
    return P


PROPS = {
    "storage_tank_tall": storage_tank_tall,
    "silo_steel": silo_steel,
    "cooling_tower_small": cooling_tower_small,
    "pipe_rack": pipe_rack,
    "pipe_bridge": pipe_bridge,
    "valve_manifold": valve_manifold,
    "pump_station": pump_station,
    "compressor_unit": compressor_unit,
    "chimney_steel": chimney_steel,
    "smokestack_brick": smokestack_brick,
    "conveyor_tower": conveyor_tower,
    "hopper": hopper,
    "crusher_machine": crusher_machine,
    "cement_mixer_wreck": cement_mixer_wreck,
    "crane_mobile_wreck": crane_mobile_wreck,
    "excavator_wreck": excavator_wreck,
    "bulldozer_wreck": bulldozer_wreck,
    "dump_truck_wreck": dump_truck_wreck,
    "steel_beam_stack": steel_beam_stack,
    "rebar_bundle": rebar_bundle,
    "scaffold_tower": scaffold_tower,
    "cable_drum_stack": cable_drum_stack,
    "barrel_pallet": barrel_pallet,
    "chemical_drums": chemical_drums,
    "hazard_waste_bin": hazard_waste_bin,
}
