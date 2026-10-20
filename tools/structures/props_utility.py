"""Colony Works utility props for the scenes between places (docs/world-generation.md, Scenes): the old colony's power,
data and site infrastructure, standing dead or broken where it was when the colony fell (docs/art-direction.md).

Power: a fenced substation section, a switch cabinet, a pole-mounted transformer, a 30 m wind turbine that reads from
the air, a row of ground-mounted solar panels and a containerised battery bank. Data and sensing: a data cabinet with a
dim cyan light (something inside still has power), toppled server racks, an antenna cluster, a weather station, a
camera mast, a crashed survey drone and a fallen robot arm. Site: a vehicle charging post, a maintenance hatch, a
ventilation shaft, a 12 m water tower, an oil pump jack, a small derrick, a gas meter station, a half-buried culvert
pipe, a drain grate, a retaining wall, stacked road barriers and a sandbag wall.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Euler, Vector

import kit as K
import frame as FR
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, RUST, ZINC, CYAN, CONCRETE
from props import _root, wood, bolt, concrete_barrier
from props_colony_a import (GLAZE, GLASS_D, CABLE, _concrete, _steel, _dark, _zinc, stencil, nut, beam, rod, _empty,
                            warning_plate, pin_insulator, insulator_string, dish_mesh)
from props_colony_b import plate_xz, lathe, mound, rust_patch

PV, PV_LINE = "#22344a", "#8fa3b0"      # solar cells and their bus lines
PCB, CHIP = "#2f5d4a", "#1d2024"        # circuit boards and the chips on them
VOID = "#121416"                        # the dark down a shaft, a drain or behind louvres
POLE = "#5e4a36"                        # creosoted pole timber
SACK, SACK_D = "#b5a27a", "#9c8a62"     # sandbags, two batches
SOIL = "#5a3f2a"
STAIN = "#55574a"                       # water streaks down concrete
DEADLAMP = "#5a3a3a"                    # an unlit red aviation lamp
DEADLED = "#2a3a40"                     # an unlit status lamp


def _glaze():
    return K.mat("glaze", GLAZE, rough=0.2, metal=0.0, wear=0.2, grime=0.3)


def _cable():
    return K.mat("cable", CABLE, rough=0.6)


def _void():
    return K.mat("void", VOID, rough=0.95)


def _soil(moss=0.0):
    return K.mat(f"soil{moss}", SOIL, rough=1.0, noise_scale=3.0, moss=moss)


def _bone(wear=0.3, grime=0.5, moss=0.05):
    return M(BONE, wear=wear, grime=grime, moss=moss)


def _rot(v, rot):
    """Point v turned by the Euler angles rot (degrees): for fixing parts to a tilted group from outside it."""
    e = Euler([math.radians(a) for a in rot])
    return Vector(v) @ e.to_matrix().transposed()


def louvre(P, loc, w, h, frame, slat, yaw=0, n=None):
    """A louvred panel facing -y (turned by yaw about Z): a frame and plain raked slats, cheaper than FR.vent for big runs."""
    G = _empty(P, "louvre", loc, (0, 0, yaw))
    n = n or max(3, int(h / 0.11))
    for s in (-1, 1):
        K.box("louvre_side", (0.05, 0.06, h), frame, loc=(s * (w / 2 - 0.025), 0, 0), bev=0.008, parent=G)
        K.box("louvre_end", (w, 0.06, 0.05), frame, loc=(0, 0, s * (h / 2 - 0.025)), bev=0.008, parent=G)
    pitch = (h - 0.1) / n
    for k in range(n):
        K.box("slat", (w - 0.1, 0.012, pitch * 1.25), slat, loc=(0, -0.01, -h / 2 + 0.05 + pitch * (k + 0.5)), rot=(-40, 0, 0), bev=0, parent=G)
    return G


def hazard_flat(P, loc, w, d, a, b, n=6):
    """Hazard stripes painted on a top face: alternating blocks along x, thin in z."""
    x, y, z = loc
    step = w / n
    for k in range(n):
        K.box("haz", (step * 1.02, d, 0.012), a if k % 2 == 0 else b, loc=(x - w / 2 + step * (k + 0.5), y, z), bev=0, parent=P)


def mesh_panel(P, a, b, z0, h, frame, wire, pitch=0.3, rows=5):
    """A welded-mesh fence panel from a to b (x, y) standing on z0: top and bottom rails and a wire grid."""
    (ax, ay), (bx, by) = a, b
    L = math.hypot(bx - ax, by - ay)
    for z in (z0 + 0.08, z0 + h - 0.04):
        rod(P, "rail", (ax, ay, z), (bx, by, z), 0.022, frame, segs=6)
    n = max(2, int(L / pitch))
    for k in range(1, n):
        t = k / n
        x, y = ax + (bx - ax) * t, ay + (by - ay) * t
        K.box("wire_v", (0.012, 0.012, h - 0.12), wire, loc=(x, y, z0 + h / 2 + 0.02), bev=0, parent=P)
    for k in range(1, rows):
        z = z0 + 0.08 + (h - 0.12) * k / rows
        beam(P, "wire_h", (ax, ay, z), (bx, by, z), 0.012, wire)


def _loft(name, rings, mat, parent=None, sharp=50):
    """A closed solid through rings of points (each ring the same length), capped at both ends."""
    bm = bmesh.new()
    vs = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    bm.faces.new(list(reversed(vs[0])))
    bm.faces.new(vs[-1])
    for i in range(len(vs) - 1):
        for j in range(n):
            k = (j + 1) % n
            bm.faces.new((vs[i][j], vs[i][k], vs[i + 1][k], vs[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(sharp))
    return K.place(ob, parent=parent)


def _sharp(ob, angle=40):
    ob.data.set_sharp_from_angle(angle=math.radians(angle))
    return ob


# ================================================================== power
def substation_fence(seed=41):
    """A section of a fenced substation compound, 6 x 4 m on a concrete pad: welded-mesh fence on three sides with barbed
    wire on raked arms, the gate stood open, a transformer with its radiators and bushings, a line gantry and a kiosk."""
    P = _root("substation")
    gra, zinc, steel = _dark(), _zinc(), _steel()
    post_m = M(ZINC, metal=0.55, wear=0.2, grime=0.4)
    wire = M(ZINC, metal=0.6, wear=0.1, grime=0.3, scratches=0.0)
    z0, H = 0.15, 2.2
    K.box("pad", (6.4, 4.4, 0.25), _concrete(0.25), loc=(0, 0, 0.025), bev=0.03, parent=P)
    posts = [(-3, -2), (-1, -2), (1, -2), (3, -2), (-3, 0), (3, 0), (-3, 2), (3, 2)]
    for (x, y) in posts:
        K.cyl("post", 0.045, H + 0.1, post_m, loc=(x, y, z0 + (H + 0.1) / 2), segs=8, bev=0, parent=P)
        ox, oy = (0, -1) if y == -2 else ((-1 if x < 0 else 1), 0)
        beam(P, "barb_arm", (x, y, z0 + H), (x + ox * 0.3, y + oy * 0.3, z0 + H + 0.35), 0.035, gra)
    for t in (0.35, 0.68, 1.0):
        dz = z0 + H + 0.35 * t
        for (xa, xb) in ((-3, -1), (1, 3)):
            rod(P, "barb", (xa, -2 - 0.3 * t, dz), (xb, -2 - 0.3 * t, dz), 0.007, wire, segs=4)
        for s in (-1, 1):
            rod(P, "barb", (s * (3 + 0.3 * t), 0, dz), (s * (3 + 0.3 * t), 2, dz), 0.007, wire, segs=4)
    for a, b in (((-3, -2), (-1, -2)), ((1, -2), (3, -2)), ((-3, -2), (-3, 0)), ((-3, 0), (-3, 2)), ((3, -2), (3, 0)), ((3, 0), (3, 2))):
        mesh_panel(P, a, b, z0, H, post_m, wire)
    # the gate, swung open into the compound
    G = _empty(P, "gate", (0.95, -2.0, 0), (0, 0, -68))
    gx = -1.85
    for x in (-0.05, gx):
        K.cyl("gate_stile", 0.03, H - 0.15, post_m, loc=(x, 0, z0 + 0.05 + (H - 0.15) / 2), segs=8, bev=0, parent=G)
    mesh_panel(G, (-0.05, 0), (gx, 0), z0 + 0.05, H - 0.15, post_m, wire)
    beam(G, "gate_brace", (-0.05, 0, z0 + 0.25), (gx, 0, z0 + H - 0.25), 0.03, post_m)
    for z in (z0 + 0.4, z0 + H - 0.4):
        K.box("gate_hinge", (0.1, 0.08, 0.06), gra, loc=(0, 0, z), bev=0, parent=G)
    K.box("gate_latch", (0.14, 0.05, 0.08), zinc, loc=(gx - 0.04, 0, z0 + 1.1), bev=0, parent=G)
    warning_plate(P, (-2.0, -2.03, z0 + 1.45), 0.4)
    # the transformer on its plinth: framed tank, radiators at both ends, three bushings
    tx, ty = 0.4, 0.55
    K.box("tx_plinth", (2.1, 1.3, 0.25), _concrete(0.2), loc=(tx, ty, z0 + 0.125), bev=0.02, parent=P)
    zb = z0 + 0.25
    tank = M(STEEL, metal=0.3, wear=0.25, grime=0.5)
    K.box("tx_tank", (1.4, 0.9, 1.4), tank, loc=(tx, ty, zb + 0.7), bev=0.03, parent=P)
    for z in (zb + 0.08, zb + 1.32):
        K.box("tx_rail", (1.44, 0.94, 0.08), gra, loc=(tx, ty, z), bev=0.01, parent=P)
    K.box("tx_lid", (1.5, 1.0, 0.08), tank, loc=(tx, ty, zb + 1.44), bev=0.015, parent=P)
    for s in (-1, 1):
        for k in range(7):
            K.box("tx_fin", (0.32, 0.03, 1.05), M(STEEL, metal=0.4, wear=0.2, grime=0.5), loc=(tx + s * 0.87, ty - 0.33 + k * 0.11, zb + 0.66), bev=0, parent=P)
        for z in (zb + 0.18, zb + 1.16):
            K.cyl("tx_header", 0.035, 0.8, gra, loc=(tx + s * 0.87, ty, z), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    warning_plate(P, (tx, ty - 0.465, zb + 0.85), 0.3)
    K.box("tx_rating", (0.26, 0.01, 0.14), M(ZINC, metal=0.6, wear=0.2), loc=(tx + 0.42, ty - 0.455, zb + 1.1), bev=0, parent=P)
    glaze = _glaze()
    line = M(ZINC, metal=0.6, wear=0.1, grime=0.2, scratches=0.0)
    for k in range(3):
        bx, by = tx - 0.45 + k * 0.45, ty + 0.15
        for j in range(3):
            K.cyl("bush_skirt", 0.09, 0.05, glaze, loc=(bx, by, zb + 1.56 + j * 0.13), segs=8, bev=0, r2=0.05, parent=P)
        K.cyl("bush_core", 0.045, 0.5, glaze, loc=(bx, by, zb + 1.72), segs=10, bev=0, parent=P)
        K.cyl("bush_term", 0.03, 0.12, zinc, loc=(bx, by, zb + 2.02), segs=8, bev=0, parent=P)
        rod(P, "drop", (bx, by, zb + 2.07), (-1.2 + k * 1.2, 1.75, 2.95), 0.014, line, segs=6)
    # line gantry along the open back, insulator strings where the incoming lines were cut off
    for s in (-1, 1):
        K.box("gantry_leg", (0.16, 0.16, 3.8), steel, loc=(s * 1.9, 1.75, z0 + 1.9), bev=0.015, parent=P)
        K.box("gantry_foot", (0.34, 0.34, 0.05), gra, loc=(s * 1.9, 1.75, z0 + 0.025), bev=0.005, parent=P)
        beam(P, "gantry_brace", (s * 1.9, 1.75, 3.1), (s * 1.3, 1.75, 3.78), 0.07, zinc)
    K.box("gantry_beam", (4.1, 0.14, 0.22), steel, loc=(0, 1.75, 3.86), bev=0.015, parent=P)
    for k in range(3):
        insulator_string(P, (-1.2 + k * 1.2, 1.75, 3.75), n=3)
    # control kiosk in the back corner
    FR.framed_box(P, "kiosk", (-2.15, 0.95, z0 + 0.8), (1.0, 0.7, 1.6), _bone(), steel, fw=0.06)
    K.box("kiosk_roof", (1.12, 0.82, 0.06), steel, loc=(-2.15, 0.95, z0 + 1.63), bev=0.012, parent=P)
    FR.vent(P, (-2.15, 0.62, z0 + 0.55), 0.6, 0.36, steel, gra)
    stencil(P, "SS-4", (-2.15, 0.633, z0 + 1.25), 0.16, gra, extrude=0.004)
    rod(P, "kiosk_cable", (-1.65, 0.85, z0 + 0.12), (tx - 0.7, ty + 0.1, z0 + 0.12), 0.03, _cable(), segs=6)
    return P


def switch_cabinet(seed=42):
    """A tall outdoor switch cabinet, 1.1 x 0.55 x 1.9 m on a concrete plinth: bone panels in steel rails, double doors
    with a lever handle and hasp, louvres down one side, a rain hood, conduits into the plinth at the back."""
    P = _root("switch_cabinet")
    gra, zinc, steel = _dark(), _zinc(), _steel()
    bone = _bone(wear=0.3, grime=0.55)
    K.box("plinth", (1.35, 0.8, 0.35), _concrete(0.25), loc=(0, 0, 0.125), bev=0.025, parent=P)
    FR.hazard(P, (0, -0.41, 0.17), 1.2, 0.12, M(MUSTARD, wear=0.3), gra, n=8)
    z0, W, D, H = 0.3, 1.1, 0.55, 1.9
    FR.framed_box(P, "cab", (0, 0, z0 + H / 2), (W, D, H), bone, steel, fw=0.06)
    K.box("hood", (W + 0.16, D + 0.18, 0.06), steel, loc=(0, -0.02, z0 + H + 0.03), rot=(-3, 0, 0), bev=0.015, parent=P)
    fy = -D / 2 - 0.01
    for s in (-1, 1):
        K.box("door", (W / 2 - 0.08, 0.025, H - 0.16), bone, loc=(s * (W / 4 - 0.005), fy, z0 + H / 2), bev=0.006, parent=P)
        for z in (z0 + 0.3, z0 + H - 0.3):
            K.box("hinge", (0.03, 0.04, 0.1), gra, loc=(s * (W / 2 - 0.07), fy - 0.01, z), bev=0, parent=P)
    K.box("handle_base", (0.05, 0.03, 0.18), gra, loc=(0.06, fy - 0.025, z0 + 1.0), bev=0.005, parent=P)
    K.box("handle", (0.035, 0.05, 0.14), zinc, loc=(0.06, fy - 0.05, z0 + 0.96), bev=0.005, parent=P)
    K.box("hasp", (0.1, 0.03, 0.04), zinc, loc=(-0.05, fy - 0.025, z0 + 1.0), bev=0, parent=P)
    warning_plate(P, (-W / 4, fy - 0.02, z0 + 1.45), 0.26)
    stencil(P, "SW-12", (W / 4, fy - 0.016, z0 + 1.5), 0.11, gra, extrude=0.004)
    rust_patch(P, (0.32, fy - 0.014, z0 + 0.3), (0.22, 0.004, 0.12))
    for z in (z0 + 0.5, z0 + 1.35):
        FR.vent(P, (-W / 2 - 0.005, 0, z), 0.36, 0.5, steel, gra, axis="x")
    K.box("rating", (0.01, 0.2, 0.12), M(ZINC, metal=0.6, wear=0.2), loc=(W / 2 + 0.005, 0, z0 + 1.5), bev=0, parent=P)
    for k in range(3):
        x = -0.3 + k * 0.3
        K.cyl("conduit", 0.04, 0.55, zinc, loc=(x, D / 2 + 0.06, z0 + 0.275), segs=10, bev=0, parent=P)
        K.cyl("gland", 0.06, 0.06, gra, loc=(x, D / 2 + 0.06, z0 + 0.58), segs=10, bev=0.01, parent=P)
    return P


def transformer_pole(seed=43):
    """A 9 m creosoted timber pole with a transformer drum on its hanger brackets: a timber cross arm with three pin
    insulators, fuse cutouts, drop leads to the drum's bushings, an earth wire under a guard and step bolts."""
    rnd = random.Random(seed)
    P = _root("transformer_pole")
    timber = K.mat("pole_timber", POLE, rough=0.9, wear=0.0, grime=0.35, noise_scale=2.0, cracks=0.3, scratches=0.2)
    gra, zinc, glaze = _dark(), _zinc(), _glaze()
    line = M(ZINC, metal=0.6, wear=0.1, grime=0.2, scratches=0.0)
    K.cyl("soil", 0.45, 0.1, _soil(0.6), loc=(0, 0, 0.0), segs=12, r2=0.22, bev=0, parent=P)
    L = _empty(P, "lean", (0, 0, 0), (-2.0, 1.5, 0))
    H = 9.0

    def rad(z):
        return 0.16 - 0.04 * (z + 0.5) / (H + 0.5)

    K.cyl("pole", 0.16, H + 0.5, timber, loc=(0, 0, (H + 0.5) / 2 - 0.5), segs=12, r2=0.12, bev=0.01, parent=L)
    K.cyl("pole_cap", 0.13, 0.05, gra, loc=(0, 0, H + 0.02), segs=12, bev=0.01, parent=L)
    # cross arm, braces and insulators
    za = 8.5
    ya = -rad(za) - 0.06
    K.box("arm", (2.4, 0.11, 0.12), wood(3, wear=0.2), loc=(0, ya, za), bev=0.015, parent=L)
    K.box("arm_bolt_plate", (0.16, 0.04, 0.2), gra, loc=(0, ya - 0.07, za), bev=0.005, parent=L)
    for s in (-1, 1):
        beam(L, "arm_brace", (s * 0.75, ya, za - 0.06), (s * 0.05, ya + 0.02, za - 0.7), 0.05, zinc, d=0.025)
    ins = [(-1.05, ya, za + 0.06), (0.35, ya, za + 0.06), (1.05, ya, za + 0.06)]
    for p in ins:
        pin_insulator(L, p, glaze)
    # the drum on two hangers, its lid, fins and bushings
    zd, rd = 6.2, 0.34
    yd = -(rad(zd) + 0.1 + rd)
    can = M(STEEL, metal=0.35, wear=0.2, grime=0.45)
    K.cyl("drum", rd, 1.05, can, loc=(0, yd, zd), segs=20, bev=0.02, parent=L)
    K.cyl("drum_lid", rd + 0.03, 0.07, can, loc=(0, yd, zd + 0.56), segs=20, bev=0.015, parent=L)
    K.cyl("drum_dome", rd, 0.08, can, loc=(0, yd, zd + 0.63), segs=20, bev=0.02, r2=rd * 0.7, parent=L)
    K.cyl("drum_base", rd + 0.01, 0.05, gra, loc=(0, yd, zd - 0.53), segs=20, bev=0, parent=L)
    for z in (zd - 0.3, zd + 0.3):
        K.box("hanger", (0.12, 0.3, 0.1), zinc, loc=(0, -rad(z) - 0.14, z), bev=0.01, parent=L)
        K.cyl("strap", rad(z) + 0.012, 0.05, zinc, loc=(0, 0, z), segs=12, bev=0, parent=L)
    for s in (-1, 1):
        for k in (-1, 0, 1):
            a = math.radians(90 - s * 90 + k * 22)
            K.box("drum_fin", (0.1, 0.02, 0.7), can, loc=(math.cos(a) * (rd + 0.04), yd + math.sin(a) * (rd + 0.04), zd - 0.05),
                  rot=(0, 0, math.degrees(a)), bev=0, parent=L)
    K.box("drum_plate", (0.16, 0.01, 0.1), M(ZINC, metal=0.6, wear=0.2), loc=(0, yd - rd - 0.004, zd + 0.15), bev=0, parent=L)
    stencil(L, "T-31", (0, yd - rd - 0.02, zd - 0.2), 0.1, M(BONE, wear=0.2), extrude=0.004)
    bush = []
    for x in (-0.15, 0.15):
        for j in range(3):
            K.cyl("bush", 0.05, 0.04, glaze, loc=(x, yd, zd + 0.7 + j * 0.06), segs=8, bev=0, r2=0.03, parent=L)
        bush.append((x, yd, zd + 0.86))
    # fuse cutouts hanging off the arm, leads from insulators through the cutouts to the bushings
    for k, s in enumerate((-1, 1)):
        cx = s * 0.6
        top = (cx, ya - 0.12, za - 0.25)
        bot = (cx + s * 0.05, ya - 0.16, za - 0.65)
        rod(L, "cutout", top, bot, 0.035, glaze, segs=8)
        K.box("cutout_bracket", (0.06, 0.14, 0.04), gra, loc=(cx, ya - 0.06, za - 0.1), bev=0, parent=L)
        p = ins[0] if s < 0 else ins[2]
        rod(L, "lead_in", (p[0], p[1], p[2] + 0.3), top, 0.01, line, segs=5)
        rod(L, "lead_out", bot, bush[k], 0.01, line, segs=5)
    # a cut line end hanging off the middle insulator
    rod(L, "line_end", (0.35, ya, za + 0.36), (0.55, ya - 0.2, za - 0.9 - rnd.uniform(0, 0.2)), 0.012, line, segs=5)
    # earth wire under a guard, step bolts, number plate
    rod(L, "earth", (rad(4) + 0.012, 0, zd - 0.5), (rad(0) + 0.012, 0, -0.1), 0.008, M(RUST, metal=0.5, wear=0.2), segs=5)
    K.box("earth_guard", (0.05, 0.06, 2.4), zinc, loc=(rad(1.2) + 0.02, 0, 1.2), bev=0.005, parent=L)
    for k in range(10):
        z = 2.7 + k * 0.4
        s = 1 if k % 2 else -1
        a = math.radians(90 + s * 70)
        r = rad(z)
        K.cyl("step", 0.018, 0.22, zinc, loc=(math.cos(a) * (r + 0.09), math.sin(a) * (r + 0.09), z), rot=(90, 0, math.degrees(a) + 90), segs=6, bev=0, parent=L)
    K.box("num_plate", (0.26, 0.02, 0.14), M(BONE, wear=0.5), loc=(0, -rad(2.0) - 0.01, 2.0), bev=0.004, parent=L)
    stencil(L, "TP-31", (0, -rad(2.0) - 0.025, 2.0), 0.07, _dark(), extrude=0.004)
    for k in range(4):
        z = 1.2 + k * 0.14
        K.cyl("band", rad(z) + 0.006, 0.14, M(MUSTARD if k % 2 == 0 else GRAPHITE, wear=0.3), loc=(0, 0, z), segs=12, bev=0, parent=L)
    return P


def _blade(name, length, chord, mat, tip_mat, parent):
    """A turbine blade along +Z from its root: airfoil sections with the chord along X, twisting towards the tip; the
    last tenth (the red tip) in tip_mat."""
    secs = [(0.0, 0.42, 0.42, 0), (0.07, 0.5, 0.4, 2), (0.2, chord, 0.3, 7), (0.45, chord * 0.75, 0.2, 11), (0.7, chord * 0.5, 0.13, 14),
            (0.9, chord * 0.33, 0.08, 15), (1.0, chord * 0.22, 0.05, 16)]

    def ring(t, c, th, tw):
        pts = [(-0.3 * c, 0), (-0.18 * c, th / 2), (0.2 * c, th * 0.4), (0.7 * c, th * 0.06), (0.2 * c, -th * 0.3), (-0.18 * c, -th / 2)]
        a = math.radians(tw)
        return [(x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a), t * length) for x, y in pts]

    rings = [ring(*s) for s in secs]
    _loft(name, rings[:6], mat, parent)
    _loft(name + "_tip", rings[5:], tip_mat, parent)


def wind_turbine_colony(seed=44):
    """A 30 m colony wind turbine, stopped: a tapering bone tower in three sections on an octagonal footing, a door up a
    steel stair, a pill-cornered nacelle banded in steel blue, a spinner and three bone blades with signal-red tips, and
    its transformer kiosk at the foot. A landmark from the air."""
    P = _root("wind_turbine")
    bone = M(BONE, wear=0.2, grime=0.45, scratches=0.2, moss=0.05)
    steel, gra, zinc = M(STEEL, metal=0.3, wear=0.2, grime=0.4), _dark(), _zinc()
    K.cyl("found", 3.2, 0.5, _concrete(0.2), loc=(0, 0, 0.1), segs=8, bev=0.04, parent=P)
    z0, Ht, r0, r1 = 0.35, 21.0, 1.25, 0.8

    def tr(z):
        return r0 + (r1 - r0) * (z - z0) / Ht

    K.cyl("flange", 1.42, 0.1, gra, loc=(0, 0, z0 + 0.05), segs=24, bev=0.01, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        nut(P, (math.cos(a) * 1.35, math.sin(a) * 1.35, z0 + 0.12), r=0.035)
    K.cyl("tower", r0, Ht, bone, loc=(0, 0, z0 + Ht / 2), segs=24, r2=r1, bev=0, parent=P)
    for z in (z0 + 7.0, z0 + 14.0):
        K.cyl("joint", tr(z) + 0.03, 0.16, steel, loc=(0, 0, z), segs=24, bev=0, parent=P)
    K.cyl("foot_band", tr(z0 + 0.5) + 0.012, 0.5, M(MUSTARD, wear=0.25, grime=0.5), loc=(0, 0, z0 + 0.35), segs=24, bev=0, parent=P)
    # door and stair
    zd = 1.35
    yf = -tr(zd + 1.0)
    K.box("door_frame", (1.1, 0.26, 2.35), steel, loc=(0, yf + 0.03, zd + 1.12), bev=0.02, parent=P)
    K.box("door", (0.85, 0.05, 2.0), M(STEEL, metal=0.3, wear=0.3, grime=0.4), loc=(0, yf - 0.11, zd + 1.05), bev=0.008, parent=P)
    K.box("door_handle", (0.05, 0.05, 0.18), zinc, loc=(0.3, yf - 0.15, zd + 1.0), bev=0.005, parent=P)
    stencil(P, "WT-3", (0, yf - 0.14, zd + 1.75), 0.2, M(BONE, wear=0.2), extrude=0.004)
    yl = yf - 0.55
    K.box("landing", (1.4, 0.8, 0.06), zinc, loc=(0, yl, zd - 0.05), bev=0.005, parent=P)
    for sx in (-0.65, 0.65):
        K.box("landing_leg", (0.06, 0.06, zd - z0), gra, loc=(sx, yl - 0.35, (zd + z0) / 2), bev=0, parent=P)
        beam(P, "stringer", (sx, yl - 0.4, zd - 0.06), (sx, yl - 1.25, z0), 0.04, gra, d=0.16)
        rod(P, "handrail", (sx, yl + 0.3, zd + 0.95), (sx, yl - 0.4, zd + 0.95), 0.025, zinc)
        rod(P, "handrail2", (sx, yl - 0.4, zd + 0.95), (sx, yl - 1.25, z0 + 0.95), 0.025, zinc)
        for y, z in ((yl + 0.3, zd), (yl - 0.4, zd), (yl - 1.25, z0)):
            rod(P, "rail_post", (sx, y, z), (sx, y, z + 0.95), 0.022, zinc, segs=6)
    for k in range(3):
        t = (k + 1) / 4
        K.box("tread", (1.25, 0.24, 0.04), zinc, loc=(0, yl - 0.4 - 0.85 * t, zd - 0.06 - (zd - 0.06 - z0) * t), bev=0, parent=P)
    # nacelle
    zt = z0 + Ht
    K.cyl("yaw_ring", 0.9, 0.3, gra, loc=(0, 0, zt + 0.15), segs=24, bev=0.02, parent=P)
    N = _empty(P, "nacelle", (0, 0, zt + 0.3), (0, 0, 0))
    K.slab("nacelle", 2.1, 4.6, 2.0, 0.45, bone, loc=(0, 0.6, 1.0), bev=0.04, parent=N)
    for z in (0.22,):
        K.slab("nac_band", 2.16, 4.66, 0.14, 0.48, steel, loc=(0, 0.6, z), bev=0.01, parent=N)
    for s in (-1, 1):
        louvre(N, (s * 1.07, 1.9, 0.9), 1.0, 0.6, steel, M(STEEL, metal=0.3, wear=0.15), yaw=90 * s, n=5)
    stencil(N, "WT-3", (1.065, 0.2, 0.9), 0.55, gra, rot=(90, 0, 90), extrude=0.008)
    K.box("hatch", (0.8, 1.0, 0.06), steel, loc=(0, 1.8, 2.02), bev=0.01, parent=N)
    K.cyl("met_post", 0.03, 0.9, gra, loc=(0, 2.6, 2.45), segs=8, bev=0, parent=N)
    K.box("met_arm", (0.7, 0.04, 0.04), gra, loc=(0, 2.6, 2.9), bev=0, parent=N)
    K.box("vane_tail", (0.02, 0.35, 0.2), gra, loc=(0.32, 2.75, 3.0), bev=0, parent=N)
    for k in range(3):
        a = 2 * math.pi * k / 3
        K.cyl("cup", 0.05, 0.06, gra, loc=(-0.32 + math.cos(a) * 0.15, 2.6 + math.sin(a) * 0.15, 3.0), rot=(90, 0, math.degrees(a)), segs=8, bev=0, r2=0.02, parent=N)
    K.cyl("cup_hub", 0.03, 0.12, gra, loc=(-0.32, 2.6, 2.96), segs=8, bev=0, parent=N)
    K.cyl("lamp_base", 0.08, 0.06, gra, loc=(0, 0.2, 2.03), segs=10, bev=0.005, parent=N)
    K.sphere("lamp", 0.1, K.mat("deadlamp_red", DEADLAMP, rough=0.3), loc=(0, 0.2, 2.13), scale=(1, 1, 1.2), parent=N)
    # spinner and blades
    hub_y = -1.7
    lathe("spinner", [(0.02, -0.05), (1.0, -0.05), (1.0, 0.45), (0.88, 1.0), (0.6, 1.5), (0.22, 1.85), (0.02, 1.9)], bone, segs=18,
          loc=(0, hub_y + 0.05, 1.0), rot=(90, 0, 0), parent=N)
    K.cyl("spinner_ring", 1.03, 0.12, steel, loc=(0, hub_y - 0.1, 1.0), rot=(90, 0, 0), segs=18, bev=0, parent=N)
    blade_mat = M(BONE, wear=0.15, grime=0.3, scratches=0.2)
    tip = M(SIGNAL, wear=0.15, grime=0.2)
    for k in range(3):
        B = _empty(N, f"blade{k}", (0, hub_y - 0.55, 1.0), (0, 18 + 120 * k, 0))
        K.cyl("pitch_ring", 0.46, 0.22, gra, loc=(0, 0, 0.78), segs=12, bev=0, parent=B)
        bl = _empty(B, "blade", (0, 0, 0.7), (0, 0, 0))
        _blade("blade", 8.6, 1.35, blade_mat, tip, bl)
    # transformer kiosk at the foot, its cable into the footing
    kx, ky = 4.4, 1.0
    K.box("kiosk_pad", (1.8, 1.4, 0.2), _concrete(0.3), loc=(kx, ky, 0.05), bev=0.02, parent=P)
    FR.framed_box(P, "kiosk", (kx, ky, 0.15 + 0.75), (1.4, 1.0, 1.5), _bone(), steel, fw=0.07)
    K.box("kiosk_roof", (1.55, 1.15, 0.07), steel, loc=(kx, ky, 1.68), bev=0.015, parent=P)
    warning_plate(P, (kx, ky - 0.47, 1.1), 0.3)
    FR.vent(P, (kx - 0.67, ky, 0.75), 0.5, 0.4, steel, gra, axis="x")
    rod(P, "trench", (kx - 0.7, ky - 0.1, 0.03), (2.9, 0.6, 0.03), 0.06, _concrete(0.1), segs=6)
    return P


def solar_farm_row(seed=45):
    """A row of six ground-mounted colony solar panels, 6.4 m long, tilted south on a steel-blue rack with concrete
    footings: one panel gone (only its clamps left), one cracked, a combiner box and its conduit at the end."""
    rnd = random.Random(seed)
    P = _root("solar_row")
    steel, gra, zinc = M(STEEL, metal=0.35, wear=0.2, grime=0.4), _dark(), _zinc()
    cell = K.mat("pv", PV, rough=0.15, metal=0.4, grime=0.25)
    line = K.mat("pv_line", PV_LINE, rough=0.4, metal=0.5)
    pw, ph, tilt, n, gap = 1.0, 1.7, 25, 6, 0.06
    W = n * (pw + gap)
    z0 = 1.05
    rot = (tilt, 0, 0)
    T = _empty(P, "rack", (0, 0, z0), rot)
    for s in (-1, 1):
        K.box("rail", (W + 0.15, 0.07, 0.1), steel, loc=(0, s * 0.5, -0.075), bev=0.01, parent=T)
    posts_x = (-W / 2 + 0.3, -W / 6, W / 6, W / 2 - 0.3)
    for x in posts_x:
        K.box("rafter", (0.08, ph + 0.1, 0.1), steel, loc=(x, 0, -0.175), bev=0.01, parent=T)
        for ly in (-0.7, 0.7):
            p = _rot((0, ly, -0.225), rot)
            K.box("post", (0.09, 0.09, z0 + p.z), steel, loc=(x, p.y, (z0 + p.z) / 2), bev=0.01, parent=P)
            K.cyl("footing", 0.15, 0.35, _concrete(0.2), loc=(x, p.y, 0.075), segs=10, bev=0.02, parent=P)
        a, b = _rot((0, -0.7, -0.225), rot), _rot((0, 0.7, -0.225), rot)
        beam(P, "brace", (x, a.y, 0.3), (x, b.y, z0 + b.z - 0.1), 0.045, zinc)
    for k in range(n):
        x = -W / 2 + (pw + gap) * (k + 0.5)
        for s in (-1, 1):
            for e in (-1, 1):
                K.box("clamp", (0.05, 0.05, 0.035), zinc, loc=(x + e * (pw / 2 + gap / 2), s * 0.5, 0.0), bev=0, parent=T)
        if k == 4:
            continue  # salvaged: only the clamps are left
        Q = _empty(T, f"panel{k}", (x, 0, 0.0), (0, 0, 0))
        K.box("frame", (pw, ph, 0.045), M(ZINC, metal=0.6, wear=0.2), loc=(0, 0, 0), bev=0.008, parent=Q)
        K.box("cells", (pw - 0.05, ph - 0.05, 0.012), cell, loc=(0, 0, 0.02), bev=0, parent=Q)
        for i in range(1, 4):
            K.box("grid_v", (0.008, ph - 0.06, 0.004), line, loc=(-pw / 2 + 0.025 + i * (pw - 0.05) / 4, 0, 0.027), bev=0, parent=Q)
        for i in range(1, 6):
            K.box("grid_h", (pw - 0.06, 0.008, 0.004), line, loc=(0, -ph / 2 + 0.025 + i * (ph - 0.05) / 6, 0.027), bev=0, parent=Q)
        if k == 1:
            for j in range(6):
                a = rnd.uniform(0, 180) + j * 30
                Lc = rnd.uniform(0.3, 0.6)
                K.box(f"crack{j}", (Lc, 0.01, 0.004), K.mat("crack", "#c9d6dc", rough=0.3),
                      loc=(0.1 + math.cos(math.radians(a)) * Lc / 2, 0.2 + math.sin(math.radians(a)) * Lc / 2, 0.03), rot=(0, 0, a), bev=0, parent=Q)
    # combiner box on the end post, conduit into the ground
    ex = W / 2 + 0.1
    p = _rot((0, 0.7, -0.225), rot)
    FR.framed_box(P, "combiner", (ex + 0.12, p.y, 0.75), (0.16, 0.4, 0.5), _bone(), steel, fw=0.035)
    K.box("combiner_label", (0.005, 0.2, 0.06), M(MUSTARD, wear=0.2), loc=(ex + 0.205, p.y, 0.9), bev=0, parent=P)
    rod(P, "conduit", (ex + 0.12, p.y, 0.5), (ex + 0.12, p.y, -0.05), 0.03, zinc, segs=8)
    return P


def battery_bank(seed=46):
    """A containerised battery bank, 4.2 x 2.2 x 2.4 m on concrete sleepers: bone panels in steel-blue rails, container
    corner castings, louvred vents along the front, a service door with a dead charge gauge, a roof cooler and cables."""
    P = _root("battery_bank")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    bone = _bone(wear=0.3, grime=0.5)
    L, Wd, H = 4.2, 2.2, 2.4
    for x in (-1.5, 1.5):
        K.box("sleeper", (0.35, Wd + 0.3, 0.25), _concrete(0.3), loc=(x, 0, 0.08), bev=0.02, parent=P)
    z0 = 0.2
    FR.framed_box(P, "body", (0, 0, z0 + H / 2), (L, Wd, H), bone, steel, fw=0.14)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (z0 + 0.08, z0 + H - 0.08):
                K.box("casting", (0.2, 0.2, 0.18), gra, loc=(sx * (L / 2 - 0.07), sy * (Wd / 2 - 0.07), z), bev=0.01, parent=P)
    fy = -Wd / 2 - 0.005
    for x in (-1.45, -0.55):
        louvre(P, (x, fy + 0.015, z0 + 1.45), 0.7, 0.9, steel, M(STEEL, metal=0.3, wear=0.15))
        K.box("louvre_back", (0.6, 0.01, 0.8), _void(), loc=(x, fy + 0.04, z0 + 1.45), bev=0, parent=P)
    K.box("door", (0.95, 0.04, 2.0), M(STEEL, metal=0.3, wear=0.3, grime=0.45), loc=(1.25, fy + 0.025, z0 + 1.08), bev=0.008, parent=P)
    for z in (z0 + 0.4, z0 + 1.7):
        K.box("hinge", (0.04, 0.05, 0.12), gra, loc=(1.74, fy + 0.005, z), bev=0, parent=P)
    K.box("lever", (0.05, 0.05, 0.3), zinc, loc=(0.88, fy - 0.015, z0 + 1.0), bev=0.006, parent=P)
    FR.screen(P, (1.25, fy + 0.0, z0 + 1.55), 0.3, 0.16, gra, K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2))
    warning_plate(P, (1.25, fy - 0.002, z0 + 1.2), 0.26)
    stencil(P, "BB-2", (-1.0, fy + 0.042, z0 + 0.65), 0.32, _dark(), extrude=0.006)
    FR.hazard(P, (-0.6, fy + 0.03, z0 + 0.42), 2.4, 0.12, M(MUSTARD, wear=0.3), gra, n=8)
    rust_patch(P, (-1.75, fy + 0.042, z0 + 0.85), (0.3, 0.004, 0.2))
    # end vent and roof cooler
    louvre(P, (L / 2 - 0.01, 0, z0 + 1.4), 1.0, 1.0, steel, M(STEEL, metal=0.3, wear=0.15), yaw=90)
    K.box("end_back", (0.01, 0.9, 0.9), _void(), loc=(L / 2 - 0.035, 0, z0 + 1.4), bev=0, parent=P)
    cz = z0 + H + 0.25
    K.box("cooler", (1.3, 1.0, 0.5), _bone(wear=0.25), loc=(-0.8, 0.2, cz), bev=0.03, parent=P)
    K.torus("fan_ring", 0.34, 0.03, gra, loc=(-0.8, 0.2, cz + 0.26), segs=20, parent=P)
    for k in range(4):
        K.box("fan_blade", (0.55, 0.1, 0.015), gra, loc=(-0.8, 0.2, cz + 0.23), rot=(12, 0, 45 * k + 10), bev=0, parent=P)
    for k in range(5):
        K.box("fan_guard", (0.015, 0.72, 0.015), zinc, loc=(-0.8 - 0.28 + k * 0.14, 0.2, cz + 0.28), bev=0, parent=P)
    for k in range(3):
        y = -0.3 + k * 0.3
        rod(P, "cable", (L / 2 - 0.2, y + 0.3, z0 + 0.1), (L / 2 + 0.25, y + 0.5, 0.02), 0.04, _cable(), segs=6)
    return P


# ================================================================== data and sensing
def data_cabinet(seed=47):
    """An outdoor data cabinet, 0.8 x 0.5 x 1.4 m on a plinth: bone case in steel rails, a rain hood, a door with a lock,
    louvres low on the front, a fibre marker post, and one dim cyan status light: something inside still has power."""
    P = _root("data_cabinet")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    bone = _bone(wear=0.3, grime=0.5)
    K.box("plinth", (0.95, 0.65, 0.25), _concrete(0.25), loc=(0, 0, 0.075), bev=0.02, parent=P)
    z0, W, D, H = 0.2, 0.8, 0.5, 1.4
    FR.framed_box(P, "cab", (0, 0, z0 + H / 2), (W, D, H), bone, steel, fw=0.05)
    K.box("hood", (W + 0.12, D + 0.14, 0.05), steel, loc=(0, -0.02, z0 + H + 0.025), rot=(-3, 0, 0), bev=0.012, parent=P)
    fy = -D / 2 - 0.008
    K.box("door", (W - 0.1, 0.02, H - 0.12), bone, loc=(0, fy, z0 + H / 2), bev=0.005, parent=P)
    louvre(P, (0, fy - 0.025, z0 + 0.32), 0.5, 0.3, steel, gra, n=4)
    K.cyl("lock", 0.025, 0.03, zinc, loc=(0.3, fy - 0.015, z0 + 0.8), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    K.box("handle", (0.03, 0.04, 0.12), gra, loc=(0.3, fy - 0.025, z0 + 0.68), bev=0.004, parent=P)
    K.cyl("led_bezel", 0.032, 0.02, gra, loc=(-0.25, fy - 0.01, z0 + 1.2), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.cyl("led", 0.024, 0.02, glow(CYAN, 2.0), loc=(-0.25, fy - 0.018, z0 + 1.2), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.cyl("led2_bezel", 0.032, 0.02, gra, loc=(-0.16, fy - 0.01, z0 + 1.2), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    K.cyl("led2", 0.022, 0.02, K.mat("deadled", DEADLED, rough=0.2), loc=(-0.16, fy - 0.018, z0 + 1.2), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    stencil(P, "DC-07", (0.12, fy - 0.012, z0 + 1.2), 0.08, gra, extrude=0.004)
    K.box("id_plate", (0.22, 0.005, 0.08), M(ZINC, metal=0.6, wear=0.2), loc=(-0.2, fy - 0.012, z0 + 1.05), bev=0, parent=P)
    for k in range(2):
        rod(P, "duct", (-0.15 + k * 0.3, D / 2 - 0.02, z0 + 0.1), (-0.15 + k * 0.3, D / 2 + 0.35, -0.05), 0.04, gra, segs=8)
    # fibre route marker post beside it
    K.box("marker", (0.08, 0.08, 0.9), M(MUSTARD, wear=0.3), loc=(0.75, -0.15, 0.45), bev=0.01, parent=P)
    K.box("marker_cap", (0.1, 0.1, 0.06), gra, loc=(0.75, -0.15, 0.92), bev=0.01, parent=P)
    for k in range(2):
        K.box("marker_band", (0.085, 0.085, 0.05), gra, loc=(0.75, -0.15, 0.62 + k * 0.1), bev=0, parent=P)
    return P


def _rack(P, name, loc, rot, seed, slots=8, door="shut"):
    """A 2 m server rack built standing on its base (front -y) and turned by rot: graphite frame and side panels,
    servers in the bays with zinc fronts and a few slots empty or pulled out; its steel-blue door shut, open or gone."""
    rnd = random.Random(seed)
    R = _empty(P, name, loc, rot)
    frame = M(GRAPHITE, metal=0.4, wear=0.25, grime=0.4)
    side = M(GRAPHITE, metal=0.3, wear=0.2, grime=0.45, scratches=0.4)
    srv = M(GRAPHITE, metal=0.5, wear=0.1, grime=0.3)
    bez = M(ZINC, metal=0.5, wear=0.1, grime=0.3)
    dark = _void()
    w, d, h = 0.6, 1.0, 2.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("upright", (0.05, 0.05, h), frame, loc=(sx * (w / 2 - 0.025), sy * (d / 2 - 0.025), h / 2), bev=0.008, parent=R)
        K.box("side", (0.02, d - 0.06, h - 0.1), side, loc=(sx * (w / 2 - 0.01), 0, h / 2), bev=0.004, parent=R)
    K.box("top", (w, d, 0.05), frame, loc=(0, 0, h - 0.025), bev=0.008, parent=R)
    K.box("plinth", (w - 0.04, d - 0.04, 0.1), frame, loc=(0, 0, 0.05), bev=0.008, parent=R)
    K.box("back", (w - 0.04, 0.02, h - 0.15), side, loc=(0, d / 2 - 0.02, h / 2), bev=0, parent=R)
    K.box("inside", (w - 0.06, 0.02, h - 0.2), dark, loc=(0, d / 2 - 0.04, h / 2), bev=0, parent=R)
    for k in range(slots):
        if rnd.random() < 0.3:
            continue
        z = 0.22 + k * 0.215
        out = rnd.uniform(0.05, 0.3) if rnd.random() < 0.3 else 0.0
        fy = -(d / 2 - 0.05) - out
        K.box("server", (w - 0.08, d - 0.12, 0.18), srv, loc=(0, -out, z), bev=0, parent=R)
        K.box("bezel", (w - 0.06, 0.02, 0.17), bez, loc=(0, fy, z), bev=0, parent=R)
        K.box("grille", (0.3, 0.005, 0.1), dark, loc=(0.08, fy - 0.011, z), bev=0, parent=R)
        K.box("tag", (0.08, 0.005, 0.03), M(BONE, wear=0.1), loc=(-0.19, fy - 0.011, z + 0.04), bev=0, parent=R)
    door_m = M(STEEL, metal=0.3, wear=0.25, grime=0.45)
    if door == "shut":
        K.box("door", (w - 0.02, 0.025, h - 0.1), door_m, loc=(0.0, -d / 2, h / 2), bev=0.004, parent=R)
    elif door == "open":
        Dd = _empty(R, "door_hinge", (w / 2 - 0.01, -d / 2 - 0.01, 0), (0, 0, 115))
        K.box("door", (w - 0.02, 0.025, h - 0.1), door_m, loc=(-(w - 0.02) / 2, 0, h / 2), bev=0.004, parent=Dd)
    return R


def server_rack_wreck(seed=48):
    """Three server racks from a colony data room dumped outside: one on its back with its servers up and sliding out,
    one toppled on its side, one still standing askew with its door hanging open, circuit boards and loose servers
    spilled round them and a cable tangle."""
    rnd = random.Random(seed)
    P = _root("server_wreck")
    _rack(P, "rack_a", (0.0, -1.35, 0.5), (-90, 0, 78), seed, door="gone")
    _rack(P, "rack_b", (0.75, -0.2, 0.3), (0, 90, -40), seed + 1)
    _rack(P, "rack_c", (-0.8, 1.0, 0.0), (5, -3, 12), seed + 2, door="open")
    # the torn-off door of rack A and loose servers
    K.box("door_loose", (0.58, 1.9, 0.025), M(STEEL, metal=0.3, wear=0.25, grime=0.5), loc=(-1.7, 0.1, 0.02), rot=(2, -1, 70), bev=0.004, parent=P)
    srv = M(GRAPHITE, metal=0.5, wear=0.1, grime=0.4)
    bez = M(ZINC, metal=0.5, wear=0.1, grime=0.3)
    for k, (x, y, yaw) in enumerate(((0.3, -2.2, 40), (1.2, -1.9, -15), (0.6, -2.5, 70))):
        G = _empty(P, "loose", (x, y, 0.09 + (0.18 if k == 2 else 0)), (0, 0, yaw))
        K.box("loose_server", (0.52, 0.86, 0.18), srv, bev=0, parent=G)
        K.box("loose_bezel", (0.54, 0.02, 0.17), bez, loc=(0, -0.44, 0), bev=0, parent=G)
    pcb = K.mat("pcb", PCB, rough=0.5, metal=0.1, grime=0.3)
    chip = K.mat("chip", CHIP, rough=0.4)
    gold = M(ZINC, metal=0.7, wear=0.0, grime=0.1, scratches=0.0)
    for k in range(10):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(1.3, 2.3)
        x, y = 0.0 + math.cos(a) * r * 0.9, -0.8 + math.sin(a) * r * 0.6
        B = _empty(P, f"board{k}", (x, y, 0.025), (rnd.uniform(-5, 5), rnd.uniform(-5, 5), rnd.uniform(0, 180)))
        K.box("pcb", (0.34, 0.22, 0.012), pcb, bev=0, parent=B)
        K.box("fingers", (0.3, 0.02, 0.013), gold, loc=(0, -0.1, 0.001), bev=0, parent=B)
        for j in range(3):
            K.box("chip", (0.05 + 0.02 * (j % 2), 0.05, 0.012), chip, loc=(-0.1 + j * 0.1, rnd.uniform(-0.03, 0.06), 0.012), bev=0, parent=B)
    for k in range(2):
        K.box("lean_pcb", (0.34, 0.012, 0.22), pcb, loc=(-0.5 + k * 0.25, 0.42, 0.12), rot=(-25, 0, 12 + k * 10), bev=0, parent=P)
    pts = [(-0.6, 0.6, 0.6), (-0.4, 0.2, 0.15), (-0.1, -0.3, 0.03), (0.5, -0.6, 0.03), (1.0, -1.2, 0.03)]
    for j, dx in enumerate((0, 0.05, -0.04)):
        for k in range(len(pts) - 1):
            a, b = pts[k], pts[k + 1]
            rod(P, "cable", (a[0] + dx, a[1] + dx, a[2]), (b[0] + dx, b[1] - dx, b[2]), 0.012,
                K.mat(f"patch{j}", [CABLE, STEEL, BONE][j], rough=0.6), segs=5)
    return P


def antenna_array(seed=49):
    """Four antennas on one concrete base: a whip mast with ground radials, a Yagi on a pole, a three-sector panel
    head and a small dish, with an equipment box in the middle and cables out to each."""
    P = _root("antenna_array")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    galv = M(ZINC, metal=0.6, wear=0.1, grime=0.3)
    bone = _bone(wear=0.25)
    K.box("base", (3.4, 3.4, 0.35), _concrete(0.25), loc=(0, 0, 0.075), bev=0.03, parent=P)
    z0 = 0.25

    def pole(x, y, h, r=0.06):
        K.box("plate", (0.4, 0.4, 0.03), gra, loc=(x, y, z0 + 0.015), bev=0.005, parent=P)
        for sx in (-1, 1):
            for sy in (-1, 1):
                nut(P, (x + sx * 0.15, y + sy * 0.15, z0 + 0.04), r=0.022)
        K.cyl("pole", r, h, galv, loc=(x, y, z0 + h / 2), segs=12, bev=0, parent=P)
        K.cyl("pole_cap", r + 0.01, 0.04, gra, loc=(x, y, z0 + h + 0.02), segs=12, bev=0, parent=P)
        return z0 + h

    # a: whip with four drooping radials
    ax, ay = -1.2, 1.2
    top = pole(ax, ay, 5.4)
    rod(P, "whip", (ax, ay, top), (ax, ay, top + 1.6), 0.012, galv, segs=6)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        rod(P, "radial", (ax, ay, top - 0.1), (ax + math.cos(a) * 0.6, ay + math.sin(a) * 0.6, top - 0.6), 0.008, galv, segs=4)
    # b: Yagi pointing out to the front-left
    bx, by = 1.2, 1.2
    top = pole(bx, by, 3.9, r=0.05)
    d = Vector((-0.45, -1, 0)).normalized()
    c = Vector((bx, by, top - 0.15)) + Vector((d.y, -d.x, 0)) * 0.08
    K.box("yagi_clamp", (0.12, 0.12, 0.12), gra, loc=c - Vector((d.y, -d.x, 0)) * 0.04, bev=0.01, parent=P)
    rod(P, "yagi_boom", c - d * 0.8, c + d * 1.0, 0.022, galv, segs=6)
    perp = Vector((d.y, -d.x, 0))
    for k in range(8):
        p = c - d * 0.75 + d * (k * 0.25)
        half = 0.48 - 0.03 * k if k else 0.55
        rod(P, "yagi_el", p - perp * half, p + perp * half, 0.008, galv, segs=4)
    K.box("yagi_feed", (0.1, 0.06, 0.08), gra, loc=c - d * 0.5, bev=0, parent=P)
    # c: three sector panels round a pole head
    cx, cy = 1.2, -1.15
    top = pole(cx, cy, 4.2)
    for k in range(3):
        a = math.radians(-90 + 120 * k)
        px, py = cx + math.cos(a) * 0.32, cy + math.sin(a) * 0.32
        beam(P, "head_arm", (cx, cy, top - 0.45), (px, py, top - 0.45), 0.05, gra)
        beam(P, "head_arm", (cx, cy, top - 1.15), (px, py, top - 1.15), 0.05, gra)
        K.box("sector", (0.3, 0.11, 1.3), bone, loc=(px + math.cos(a) * 0.06, py + math.sin(a) * 0.06, top - 0.8), rot=(0, 0, math.degrees(a) + 90), bev=0.02, parent=P)
        K.box("sector_cap", (0.32, 0.13, 0.06), gra, loc=(px + math.cos(a) * 0.06, py + math.sin(a) * 0.06, top - 0.12), rot=(0, 0, math.degrees(a) + 90), bev=0.005, parent=P)
    # d: small dish on a short pedestal, looking up at the sky towards the front
    dx_, dy_ = -1.2, -1.15
    top = pole(dx_, dy_, 1.5, r=0.09)
    K.box("dish_yoke", (0.24, 0.16, 0.2), gra, loc=(dx_, dy_, top + 0.1), bev=0.01, parent=P)
    D = _empty(P, "dish", (dx_, dy_, top + 0.2), (60, 0, 15))
    K.place(dish_mesh("reflector", 0.6, 0.16, 0.025, M(BONE, metal=0.2, wear=0.2, grime=0.4, moss=0.1), rings=4, segs=20), (0, 0, 0.08), (0, 0, 0), D)
    K.cyl("dish_hub", 0.12, 0.12, gra, loc=(0, 0, 0.03), segs=12, bev=0.01, parent=D)
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        rod(D, "feed_strut", (math.cos(a) * 0.55, math.sin(a) * 0.55, 0.08 + 0.15), (0, 0, 0.7), 0.012, galv, segs=5)
    K.cyl("lnb", 0.05, 0.16, gra, loc=(0, 0, 0.74), segs=10, bev=0.01, parent=D)
    # equipment box, cables to each pole along the slab
    FR.framed_box(P, "equip", (0.0, 0.1, z0 + 0.6), (0.9, 0.6, 1.2), bone, steel, fw=0.06, screws=zinc)
    K.box("equip_roof", (1.0, 0.72, 0.05), steel, loc=(0.0, 0.1, z0 + 1.23), bev=0.01, parent=P)
    FR.vent(P, (-0.2, -0.17, z0 + 0.45), 0.4, 0.3, steel, gra)
    warning_plate(P, (0.25, -0.165, z0 + 0.85), 0.2)
    stencil(P, "AX-2", (-0.2, -0.163, z0 + 0.9), 0.12, gra, extrude=0.004)
    for (x, y) in ((ax, ay), (bx, by), (cx, cy), (dx_, dy_)):
        sx = 0.45 if x > 0 else -0.45
        rod(P, "feeder", (sx, 0.1 + (0.2 if y > 0 else -0.2), z0 + 0.2), (x - (0.12 if x > 0 else -0.12), y, z0 + 0.03), 0.02, _cable(), segs=5)
    return P


def weather_station(seed=50):
    """A colony weather station: a 6 m galvanised mast on a pad, guyed three ways, a cross arm with a cup anemometer and
    a wind vane, a radiation shield, a little solar panel and a logger box, a louvred instrument screen on its own stand
    and a rain gauge."""
    P = _root("weather")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    galv = M(ZINC, metal=0.6, wear=0.1, grime=0.3)
    bone = _bone(wear=0.2, grime=0.4)
    mx, my = 0.4, 0.4
    K.box("pad", (0.7, 0.7, 0.25), _concrete(0.25), loc=(mx, my, 0.025), bev=0.02, parent=P)
    K.box("base_plate", (0.3, 0.3, 0.03), gra, loc=(mx, my, 0.165), bev=0.005, parent=P)
    Hm = 6.0
    K.cyl("mast", 0.045, Hm, galv, loc=(mx, my, 0.15 + Hm / 2), segs=10, bev=0, parent=P)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        g = (mx + math.cos(a) * 2.4, my + math.sin(a) * 2.4)
        K.box("peg", (0.12, 0.12, 0.1), gra, loc=(g[0], g[1], 0.03), bev=0.01, parent=P)
        rod(P, "guy", (g[0], g[1], 0.08), (mx + math.cos(a) * 0.05, my + math.sin(a) * 0.05, 4.3), 0.006, galv, segs=4)
    za = 0.15 + Hm
    K.box("cross_arm", (1.4, 0.05, 0.05), gra, loc=(mx, my, za), bev=0, parent=P)
    K.box("arm_clamp", (0.12, 0.12, 0.12), gra, loc=(mx, my, za), bev=0.008, parent=P)
    rod(P, "lightning_rod", (mx, my, za), (mx, my, za + 0.7), 0.01, M(RUST, metal=0.5, wear=0.2), segs=5)
    # anemometer on the left end
    ex = mx - 0.65
    K.cyl("anem_post", 0.018, 0.3, gra, loc=(ex, my, za + 0.15), segs=8, bev=0, parent=P)
    K.cyl("anem_hub", 0.04, 0.06, bone, loc=(ex, my, za + 0.32), segs=10, bev=0, parent=P)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.4
        rod(P, "anem_arm", (ex, my, za + 0.32), (ex + math.cos(a) * 0.22, my + math.sin(a) * 0.22, za + 0.32), 0.008, gra, segs=4)
        K.cyl("cup", 0.055, 0.07, bone, loc=(ex + math.cos(a) * 0.24, my + math.sin(a) * 0.24, za + 0.32), rot=(90, 0, math.degrees(a)),
              segs=10, bev=0, r2=0.015, parent=P)
    # wind vane on the right end
    vx = mx + 0.65
    K.cyl("vane_post", 0.018, 0.25, gra, loc=(vx, my, za + 0.12), segs=8, bev=0, parent=P)
    rod(P, "vane_arm", (vx, my - 0.3, za + 0.27), (vx, my + 0.35, za + 0.27), 0.01, gra, segs=5)
    K.box("vane_tail", (0.015, 0.22, 0.16), bone, loc=(vx, my + 0.3, za + 0.29), bev=0, parent=P)
    K.cyl("vane_nose", 0.03, 0.1, gra, loc=(vx, my - 0.33, za + 0.27), rot=(90, 0, 0), segs=8, bev=0, r2=0.005, parent=P)
    # radiation shield, solar panel and logger on the mast
    for k in range(6):
        K.cyl("shield", 0.11 - (0.02 if k in (0, 5) else 0), 0.014, bone, loc=(mx + 0.25, my, 2.3 + k * 0.045), segs=14, bev=0, parent=P)
    K.box("shield_arm", (0.22, 0.03, 0.03), gra, loc=(mx + 0.13, my, 2.4), bev=0, parent=P)
    S = _empty(P, "panel", (mx, my - 0.2, 3.3), (50, 0, 0))
    K.box("pv_frame", (0.5, 0.38, 0.03), galv, bev=0.005, parent=S)
    K.box("pv_cells", (0.46, 0.34, 0.01), K.mat("pv", PV, rough=0.15, metal=0.4, grime=0.25), loc=(0, 0, 0.016), bev=0, parent=S)
    K.box("pv_arm", (0.04, 0.2, 0.04), gra, loc=(mx, my - 0.1, 3.25), bev=0, parent=P)
    FR.framed_box(P, "logger", (mx, my - 0.17, 1.35), (0.4, 0.2, 0.5), bone, steel, fw=0.035)
    stencil(P, "WX-1", (mx, my - 0.233, 1.45), 0.08, gra, extrude=0.004)
    for z in (1.2, 1.5):
        K.box("logger_strap", (0.1, 0.12, 0.03), zinc, loc=(mx, my - 0.06, z), bev=0, parent=P)
    rod(P, "logger_cable", (mx + 0.1, my - 0.1, 1.1), (mx + 0.04, my - 0.04, 0.25), 0.012, _cable(), segs=5)
    # louvred instrument screen on its stand
    sx, sy, sz = -1.2, -0.5, 1.45
    for lx in (-0.2, 0.2):
        for ly in (-0.15, 0.15):
            K.box("screen_leg", (0.04, 0.04, sz - 0.25), galv, loc=(sx + lx, sy + ly, (sz - 0.25) / 2), bev=0, parent=P)
    K.box("screen_floor", (0.56, 0.46, 0.03), bone, loc=(sx, sy, sz - 0.24), bev=0.005, parent=P)
    K.box("screen_roof", (0.66, 0.56, 0.04), bone, loc=(sx, sy, sz + 0.27), rot=(0, 4, 0), bev=0.008, parent=P)
    K.box("screen_back", (0.52, 0.02, 0.5), bone, loc=(sx, sy + 0.21, sz), bev=0, parent=P)
    K.box("screen_inner", (0.48, 0.38, 0.46), _void(), loc=(sx, sy, sz), bev=0, parent=P)
    for k in range(6):
        z = sz - 0.2 + k * 0.08
        K.box("slat_f", (0.52, 0.012, 0.075), bone, loc=(sx, sy - 0.22, z), rot=(-35, 0, 0), bev=0, parent=P)
        for s in (-1, 1):
            K.box("slat_s", (0.012, 0.42, 0.075), bone, loc=(sx + s * 0.27, sy, z), rot=(0, s * 35, 0), bev=0, parent=P)
    # rain gauge
    gx, gy = 1.4, -0.9
    K.box("gauge_post", (0.07, 0.07, 0.6), galv, loc=(gx, gy, 0.3), bev=0, parent=P)
    K.cyl("gauge", 0.1, 0.4, galv, loc=(gx, gy, 0.8), segs=14, bev=0.01, parent=P)
    K.torus("gauge_rim", 0.1, 0.012, gra, loc=(gx, gy, 1.0), segs=16, parent=P)
    K.cyl("gauge_funnel", 0.09, 0.02, _void(), loc=(gx, gy, 0.995), segs=14, bev=0, parent=P)
    return P


def sensor_mast(seed=51):
    """A thin 5.5 m steel mast on a bolted base plate: a pan-tilt camera on an arm at the top with a sun hood, a motion
    sensor under it, a whip, a junction box at chest height and a hazard sleeve at the foot."""
    P = _root("sensor_mast")
    steel, gra, zinc = M(STEEL, metal=0.35, wear=0.2, grime=0.4), _dark(), _zinc()
    bone = _bone(wear=0.2, grime=0.4)
    K.cyl("footing", 0.35, 0.3, _concrete(0.25), loc=(0, 0, 0.0), segs=12, bev=0.02, parent=P)
    K.box("base_plate", (0.34, 0.34, 0.03), gra, loc=(0, 0, 0.165), bev=0.005, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            nut(P, (sx * 0.13, sy * 0.13, 0.19), r=0.022)
    for k in range(4):
        a = k * 90
        K.box("gusset", (0.16, 0.015, 0.16), gra, loc=(math.cos(math.radians(a)) * 0.1, math.sin(math.radians(a)) * 0.1, 0.24), rot=(0, 0, a), bev=0, parent=P)
    H = 5.4
    K.box("mast", (0.1, 0.1, H), steel, loc=(0, 0, 0.18 + H / 2), bev=0.012, parent=P)
    K.box("mast_cap", (0.13, 0.13, 0.04), gra, loc=(0, 0, 0.18 + H + 0.02), bev=0.005, parent=P)
    for k in range(4):
        K.box("sleeve", (0.115, 0.115, 0.15), M(MUSTARD if k % 2 == 0 else GRAPHITE, wear=0.3), loc=(0, 0, 0.5 + k * 0.15), bev=0.004, parent=P)
    zt = 0.18 + H - 0.15
    K.box("arm", (0.07, 0.6, 0.07), gra, loc=(0, -0.33, zt), bev=0.008, parent=P)
    K.cyl("pan", 0.07, 0.12, gra, loc=(0, -0.6, zt - 0.1), segs=12, bev=0.01, parent=P)
    C = _empty(P, "camera", (0, -0.6, zt - 0.25), (16, 0, -12))
    for s in (-1, 1):
        K.box("tilt_yoke", (0.02, 0.1, 0.18), gra, loc=(s * 0.1, 0, 0.08), bev=0, parent=C)
    K.slab("housing", 0.17, 0.44, 0.17, 0.04, bone, loc=(0, -0.05, 0), bev=0.01, parent=C)
    K.box("sun_hood", (0.22, 0.52, 0.012), bone, loc=(0, -0.1, 0.105), bev=0.004, parent=C)
    K.cyl("lens_ring", 0.06, 0.02, gra, loc=(0, -0.275, 0), rot=(90, 0, 0), segs=14, bev=0, parent=C)
    K.cyl("lens", 0.045, 0.02, K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(0, -0.28, 0), rot=(90, 0, 0), segs=14, bev=0, parent=C)
    for k in range(6):
        a = 2 * math.pi * k / 6
        K.cyl("ir", 0.008, 0.01, K.mat("deadled", DEADLED, rough=0.2), loc=(math.cos(a) * 0.072, -0.276, math.sin(a) * 0.072), rot=(90, 0, 0), segs=6, bev=0, parent=C)
    K.box("wiper", (0.1, 0.01, 0.01), gra, loc=(0.02, -0.29, 0.03), rot=(0, 30, 0), bev=0, parent=C)
    rod(P, "cam_cable", (0.03, -0.55, zt - 0.05), (0.06, -0.04, zt - 0.4), 0.01, _cable(), segs=5)
    # motion sensor and whip
    K.box("pir_mount", (0.06, 0.12, 0.06), gra, loc=(0, -0.1, 4.6), bev=0, parent=P)
    K.slab("pir", 0.12, 0.08, 0.14, 0.03, bone, loc=(0, -0.18, 4.6), bev=0.01, parent=P)
    K.sphere("pir_dome", 0.04, K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(0, -0.22, 4.58), parent=P)
    rod(P, "whip", (0.04, 0.04, 0.18 + H), (0.06, 0.06, 0.18 + H + 1.0), 0.008, zinc, segs=5)
    # junction box and conduit
    FR.framed_box(P, "jbox", (0, -0.15, 1.45), (0.32, 0.18, 0.42), bone, steel, fw=0.03)
    stencil(P, "SM-8", (0, -0.203, 1.55), 0.06, gra, extrude=0.003)
    for z in (1.32, 1.58):
        K.box("jstrap", (0.14, 0.14, 0.03), zinc, loc=(0, -0.03, z), bev=0, parent=P)
    rod(P, "conduit", (0.1, -0.12, 1.24), (0.1, -0.09, 0.0), 0.02, zinc, segs=8)
    rod(P, "riser", (-0.07, -0.07, 1.66), (-0.07, -0.07, zt - 0.4), 0.012, _cable(), segs=5)
    return P


def survey_drone_crashed(seed=52):
    """A colony quad survey drone, 1.9 m across the rotors, come down nose first: pill-cornered bone body with a mustard
    top, four arms with motors and rotor guards (one arm snapped and its rotor thrown clear), bent skids, a dead camera
    ball, and a ring of turned-up soil where it hit."""
    rnd = random.Random(seed)
    P = _root("drone")
    gra, zinc, steel = _dark(), _zinc(), M(STEEL, metal=0.35, wear=0.2, grime=0.4)
    bone = M(BONE, wear=0.3, grime=0.55, scratches=0.4)
    carbon = M(GRAPHITE, metal=0.2, wear=0.15, grime=0.3)
    prop = K.mat("prop", "#1e2125", rough=0.5)
    mound(P, "dirt", (0.05, -0.55), 0.42, 0.18, seed=seed, n=6, sx=1.3)
    D = _empty(P, "body", (0, 0, 0.38), (24, -10, 25))

    def rotor(G, loc, broken_blade=False):
        x, y, z = loc
        K.cyl("motor", 0.07, 0.11, gra, loc=(x, y, z + 0.02), segs=12, bev=0.01, parent=G)
        K.cyl("motor_cap", 0.05, 0.03, zinc, loc=(x, y, z + 0.09), segs=12, bev=0, parent=G)
        K.torus("guard", 0.31, 0.016, steel, loc=(x, y, z + 0.06), segs=22, parent=G)
        for k in range(2):
            a = math.radians(k * 180 + 40)
            rod(G, "guard_strut", (x + math.cos(a) * 0.06, y + math.sin(a) * 0.06, z), (x + math.cos(a) * 0.31, y + math.sin(a) * 0.31, z + 0.06), 0.008, steel, segs=4)
        yaw = rnd.uniform(0, 180)
        K.box("blade", (0.55 if not broken_blade else 0.36, 0.05, 0.01), prop, loc=(x, y, z + 0.11), rot=(0, 0, yaw), bev=0, parent=G)

    K.slab("hull", 0.62, 0.86, 0.22, 0.12, bone, bev=0.025, parent=D)
    K.slab("top", 0.46, 0.62, 0.06, 0.1, M(MUSTARD, wear=0.3, grime=0.4), loc=(0, 0.02, 0.13), bev=0.015, parent=D)
    K.box("seam", (0.64, 0.02, 0.02), gra, loc=(0, 0.15, 0.0), bev=0, parent=D)
    stencil(D, "SD-4", (0, 0.08, 0.162), 0.11, gra, rot=(0, 0, 0), extrude=0.004)
    K.box("gps", (0.12, 0.12, 0.04), gra, loc=(0, 0.3, 0.17), bev=0.008, parent=D)
    rod(D, "antenna", (-0.18, 0.32, 0.12), (-0.2, 0.36, 0.42), 0.006, zinc, segs=4)
    K.cyl("gimbal", 0.05, 0.06, gra, loc=(0, -0.3, -0.13), segs=10, bev=0, parent=D)
    K.sphere("cam_ball", 0.075, K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2), loc=(0, -0.3, -0.2), parent=D)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        tip = (math.cos(a) * 0.68, math.sin(a) * 0.68, 0.02)
        root = (math.cos(a) * 0.25, math.sin(a) * 0.25, 0.02)
        if k == 3:  # front right: snapped off near the body
            stub = (math.cos(a) * 0.4, math.sin(a) * 0.4, 0.0)
            beam(D, "arm_stub", root, stub, 0.06, carbon)
            K.box("splinter", (0.05, 0.02, 0.08), carbon, loc=stub, rot=(30, 20, 0), bev=0, parent=D)
            continue
        beam(D, "arm", root, tip, 0.06, carbon)
        rotor(D, tip, broken_blade=(k == 0))
    # skids, the front one folded under
    for s in (-1, 1):
        rod(D, "skid", (s * 0.22, -0.35, -0.3), (s * 0.22, 0.35, -0.3 + (0.12 if s > 0 else 0)), 0.014, gra, segs=6)
        for y in (-0.2, 0.2):
            rod(D, "skid_leg", (s * 0.18, y, -0.1), (s * 0.22, y + (0.08 if s > 0 else 0), -0.3 + (0.08 if s > 0 and y > 0 else 0)), 0.012, gra, segs=6)
    # the thrown rotor with its piece of arm
    T = _empty(P, "thrown", (1.25, 0.75, 0.04), (6, -4, 40))
    beam(T, "arm_piece", (-0.3, 0, 0.0), (0.0, 0, 0.0), 0.06, carbon)
    rotor(T, (0.0, 0, 0.0), broken_blade=True)
    for k in range(4):
        _sh = K.box("shard", (rnd.uniform(0.06, 0.12), rnd.uniform(0.04, 0.08), 0.01), bone, loc=(rnd.uniform(-0.6, 1.0), rnd.uniform(-1.0, -0.4), 0.01),
                    rot=(0, 0, rnd.uniform(0, 180)), bev=0, parent=P)
    return P


def robot_arm_fallen(seed=53):
    """A big six-axis colony industrial robot torn off its plinth and lying on its side: graphite base, mustard
    turret and arms, steel joint housings and motors, a two-finger gripper, hoses along the arms; the plinth still
    standing with its sheared bolts."""
    rnd = random.Random(seed)
    P = _root("robot_arm")
    paint = M(MUSTARD, wear=0.35, grime=0.5, scratches=0.4)
    gra, zinc = _dark(), _zinc()
    joint = M(STEEL, metal=0.4, wear=0.25, grime=0.4)
    hose = _cable()
    # the plinth it stood on
    px = -2.35
    K.box("plinth", (1.5, 1.5, 0.5), _concrete(0.3), loc=(px, 0.1, 0.15), bev=0.03, parent=P)
    K.cyl("mount", 0.62, 0.05, gra, loc=(px, 0.1, 0.42), segs=20, bev=0.005, parent=P)
    for k in range(8):
        a = 2 * math.pi * k / 8
        K.cyl("sheared", 0.025, 0.06 + 0.05 * (k % 3), zinc, loc=(px + math.cos(a) * 0.52, 0.1 + math.sin(a) * 0.52, 0.47), rot=(rnd.uniform(-15, 15), rnd.uniform(-15, 15), 0), segs=6, bev=0, parent=P)
    # the robot, built standing (base at the origin, reaching towards -y) and laid on its side
    R = _empty(P, "robot", (-1.55, 0.25, 0.72), (0, 90, 0))
    Q = _empty(R, "turn", (0, 0, 0), (0, 0, 17))
    K.cyl("base", 0.6, 0.4, gra, loc=(0, 0, 0.2), segs=20, bev=0.02, parent=Q)
    K.cyl("base_flange", 0.7, 0.07, gra, loc=(0, 0, 0.035), segs=20, bev=0.01, parent=Q)
    for k in range(8):
        a = 2 * math.pi * k / 8
        K.cyl("anchor", 0.022, 0.25, zinc, loc=(math.cos(a) * 0.55, math.sin(a) * 0.55, -0.1), segs=6, bev=0, parent=Q)
    K.slab("turret", 1.0, 0.9, 0.7, 0.18, paint, loc=(0, 0.05, 0.75), bev=0.03, parent=Q)
    K.cyl("shoulder", 0.4, 0.86, joint, loc=(0, 0, 1.2), rot=(0, 90, 0), segs=20, bev=0.02, parent=Q)
    K.cyl("shoulder_motor", 0.2, 0.36, gra, loc=(0.6, 0, 1.2), rot=(0, 90, 0), segs=14, bev=0.015, parent=Q)
    elbow = Vector((0, 0.35, 3.4))
    wrist = Vector((0, -1.75, 3.65))
    beam(Q, "lower_arm", (0, 0, 1.2), elbow, 0.42, paint, d=0.36, bev=0.03)
    K.cyl("elbow", 0.3, 0.62, joint, loc=elbow, rot=(0, 90, 0), segs=18, bev=0.02, parent=Q)
    K.cyl("elbow_motor", 0.16, 0.3, gra, loc=elbow + Vector((-0.42, 0, 0)), rot=(0, 90, 0), segs=12, bev=0.01, parent=Q)
    K.box("counter", (0.44, 0.55, 0.42), paint, loc=elbow + Vector((0, 0.45, 0.05)), rot=(-7, 0, 0), bev=0.03, parent=Q)
    beam(Q, "upper_arm", elbow, wrist, 0.32, paint, d=0.32, bev=0.03)
    K.cyl("wrist_motor", 0.13, 0.36, gra, loc=elbow + Vector((0, -0.6, 0.28)), rot=(90, 0, 0), segs=12, bev=0.01, parent=Q)
    K.cyl("wrist", 0.17, 0.32, joint, loc=wrist + Vector((0, -0.12, 0)), rot=(0, 90, 0), segs=14, bev=0.015, parent=Q)
    K.cyl("flange", 0.13, 0.08, gra, loc=wrist + Vector((0, -0.12, -0.22)), segs=14, bev=0.01, parent=Q)
    K.box("palm", (0.3, 0.16, 0.1), gra, loc=wrist + Vector((0, -0.12, -0.31)), bev=0.01, parent=Q)
    for s in (-1, 1):
        K.box("finger", (0.06, 0.12, 0.3), joint, loc=wrist + Vector((s * 0.1, -0.12, -0.5)), bev=0.01, parent=Q)
    stencil(Q, "RA-6", (-0.216, 0.17, 2.3), 0.22, gra, rot=(90, 0, -90), extrude=0.006)
    K.box("warn_band", (0.012, 0.3, 0.12), M(SIGNAL, wear=0.3), loc=(-0.215, 0.17, 1.75), rot=(-6, 0, 0), bev=0, parent=Q)
    # hoses in loops along the arms
    pts = [(0.25, 0.3, 0.9), (0.28, 0.4, 1.6), (0.27, 0.45, 2.6), (0.22, 0.55, 3.3), (0.22, 0.2, 3.75), (0.2, -0.9, 3.9), (0.18, -1.6, 3.85)]
    for k in range(len(pts) - 1):
        rod(Q, "hose", pts[k], pts[k + 1], 0.035, hose, segs=6)
        if k:
            K.sphere("hose_joint", 0.035, hose, loc=pts[k], parent=Q)
    for k in range(3):
        K.box("hose_clip", (0.08, 0.08, 0.04), gra, loc=pts[k + 1], bev=0, parent=Q)
    # the torn power cable from the plinth to the base
    rod(P, "torn_cable", (px + 0.5, -0.25, 0.42), (-1.65, -0.35, 0.05), 0.04, hose, segs=6)
    return P


# ================================================================== site
def charging_post(seed=54):
    """A colony vehicle charging post on a small plinth: framed bone case with a steel cap, a dead screen, the connector
    in its holster, the cable coiled on a hook, a mustard band and two guard bollards."""
    P = _root("charging_post")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    bone = _bone(wear=0.3, grime=0.5)
    K.box("plinth", (0.8, 0.6, 0.2), _concrete(0.25), loc=(0, 0, 0.05), bev=0.02, parent=P)
    z0 = 0.15
    FR.framed_box(P, "post", (0, 0, z0 + 0.8), (0.46, 0.3, 1.6), bone, steel, fw=0.045)
    K.slab("cap", 0.56, 0.4, 0.07, 0.06, steel, loc=(0, -0.02, z0 + 1.635), bev=0.015, parent=P)
    K.box("band", (0.465, 0.305, 0.1), M(MUSTARD, wear=0.3), loc=(0, 0, z0 + 0.3), bev=0.004, parent=P)
    fy = -0.155
    FR.screen(P, (0, fy - 0.02, z0 + 1.3), 0.26, 0.16, gra, K.mat("darkglass", GLASS_D, rough=0.15, metal=0.2))
    K.box("bolt_plate", (0.14, 0.01, 0.14), M(MUSTARD, wear=0.2), loc=(0, -0.113, z0 + 1.0), bev=0, parent=P)
    _bolt = [(0.03, 0.06), (-0.03, 0.0), (0.01, 0.0), (-0.03, -0.06), (0.03, 0.01), (-0.01, 0.01)]
    plate_xz("bolt_icon", [(x, z) for x, z in _bolt], 0.006, gra, loc=(0, -0.12, z0 + 1.0), bev=0, parent=P)
    stencil(P, "CP-3", (0, -0.113, z0 + 0.78), 0.09, gra, extrude=0.003)
    # holster and connector on the right
    K.box("holster", (0.08, 0.14, 0.16), gra, loc=(0.27, 0, z0 + 1.0), bev=0.01, parent=P)
    K.box("plug", (0.07, 0.1, 0.22), gra, loc=(0.29, -0.03, z0 + 1.1), rot=(0, -15, 0), bev=0.015, parent=P)
    # cable out of the base on the left, up to a hook and coiled
    K.box("hook", (0.12, 0.04, 0.04), zinc, loc=(-0.29, 0, z0 + 1.15), bev=0, parent=P)
    for k in range(3):
        K.torus("coil", 0.2 + 0.01 * k, 0.022, _cable(), loc=(-0.37 - 0.01 * k, 0, z0 + 0.95), rot=(90 - 6 + 6 * k, 0, 90), segs=20, parent=P)
    rod(P, "lead", (-0.2, -0.05, z0 + 0.25), (-0.37, 0, z0 + 0.76), 0.022, _cable(), segs=6)
    for x in (-0.55, 0.55):
        K.cyl("bollard", 0.07, 0.9, M(MUSTARD, wear=0.3, grime=0.5), loc=(x, -0.55, 0.45), segs=12, bev=0.01, parent=P)
        K.cyl("bollard_band", 0.072, 0.1, gra, loc=(x, -0.55, 0.7), segs=12, bev=0, parent=P)
        K.cyl("bollard_cap", 0.075, 0.04, gra, loc=(x, -0.55, 0.9), segs=12, bev=0.01, parent=P)
    return P


def maintenance_hatch(seed=55):
    """A maintenance access hatch: a concrete collar out of the ground round a dark shaft, a steel rim, grab rails off
    the ladder, the steel-blue lid propped open on its stay with hazard paint on its underside."""
    P = _root("hatch")
    conc = _concrete(0.3)
    gra, zinc = _dark(), _zinc()
    lid_m = M(STEEL, metal=0.35, wear=0.25, grime=0.45)
    S, O, top = 1.5, 0.9, 0.35
    wall = (S - O) / 2
    for s in (-1, 1):
        K.box("collar_x", (wall, S, top + 0.1), conc, loc=(s * (O / 2 + wall / 2), 0, (top - 0.1) / 2), bev=0.03, parent=P)
        K.box("collar_y", (O, wall, top + 0.1), conc, loc=(0, s * (O / 2 + wall / 2), (top - 0.1) / 2), bev=0.01, parent=P)
        K.box("liner_x", (0.02, O, top - 0.05), _void(), loc=(s * (O / 2 - 0.01), 0, (top - 0.05) / 2), bev=0, parent=P)
        K.box("liner_y", (O, 0.02, top - 0.05), _void(), loc=(0, s * (O / 2 - 0.01), (top - 0.05) / 2), bev=0, parent=P)
        K.box("rim_x", (0.06, O + 0.12, 0.04), gra, loc=(s * (O / 2 + 0.03), 0, top + 0.02), bev=0.004, parent=P)
        K.box("rim_y", (O + 0.12, 0.06, 0.04), gra, loc=(0, s * (O / 2 + 0.03), top + 0.02), bev=0.004, parent=P)
    K.box("shaft", (O, O, 0.02), _void(), loc=(0, 0, 0.01), bev=0, parent=P)
    hazard_flat(P, (0, -(O / 2 + wall / 2), top + 0.006), S - 0.1, wall - 0.12, M(MUSTARD, wear=0.3), gra, n=7)
    stencil(P, "MH-2", (0.35, -S / 2 - 0.005, 0.17), 0.12, gra, extrude=0.004)
    # ladder: rails up out of the shaft as grab handles, two rungs showing
    for s in (-1, 1):
        rod(P, "ladder_rail", (s * 0.2, O / 2 - 0.1, 0.0), (s * 0.2, O / 2 - 0.1, top + 0.75), 0.022, zinc, segs=8)
        K.torus("grab_bend", 0.08, 0.022, zinc, loc=(s * 0.2, O / 2 - 0.02, top + 0.75), rot=(90, 0, 90), arc=180, segs=10, parent=P)
        rod(P, "grab_back", (s * 0.2, O / 2 + 0.06, top + 0.75), (s * 0.2, O / 2 + 0.06, top + 0.5), 0.022, zinc, segs=8)
    for z in (0.08, 0.3):
        rod(P, "rung", (-0.2, O / 2 - 0.1, z), (0.2, O / 2 - 0.1, z), 0.016, zinc, segs=6)
    # the lid, hinged at the back, propped at 78 degrees
    hinge = Vector((0, O / 2 + 0.06, top + 0.06))
    lr = (-78, 0, 0)
    Ld = _empty(P, "lid", hinge, lr)
    ls = O + 0.12
    K.box("lid", (ls, ls, 0.05), lid_m, loc=(0, -ls / 2, 0), bev=0.01, parent=Ld)
    for x in (-0.3, 0.3):
        K.box("lid_rib", (0.05, ls - 0.1, 0.05), lid_m, loc=(x, -ls / 2, -0.05), bev=0.005, parent=Ld)
    K.box("lid_rib_x", (ls - 0.1, 0.05, 0.05), lid_m, loc=(0, -ls / 2, -0.05), bev=0.005, parent=Ld)
    for k in range(7):
        K.box("lid_haz", (ls / 7 * 1.02, 0.16, 0.01), M(MUSTARD, wear=0.3) if k % 2 == 0 else gra, loc=(-ls / 2 + ls / 7 * (k + 0.5), -ls + 0.1, -0.03), bev=0, parent=Ld)
    for x in (-0.3, 0.3):
        K.cyl("hinge", 0.035, 0.14, gra, loc=(x, 0, 0), rot=(0, 90, 0), segs=10, bev=0, parent=Ld)
    K.box("lid_handle", (0.2, 0.03, 0.03), zinc, loc=(0, -ls + 0.06, 0.04), bev=0, parent=Ld)
    # the stay from the collar to the lid's underside
    a = Vector((0.48, 0.05, top + 0.04))
    b = hinge + _rot((0.42, -0.5, -0.06), lr)
    rod(P, "stay", a, b, 0.018, zinc, segs=6)
    K.cyl("stay_body", 0.03, 0.25, gra, loc=a.lerp(b, 0.25), segs=8, bev=0, parent=P).rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    K.box("vent_pipe", (0.1, 0.1, 0.6), gra, loc=(0.9, 0.55, 0.3), bev=0.01, parent=P)
    K.box("vent_cap", (0.16, 0.16, 0.05), gra, loc=(0.9, 0.55, 0.62), bev=0.01, parent=P)
    return P


def ventilation_shaft(seed=56):
    """A ventilation tower out of the ground, 2 x 2 x 2.4 m on a sunk concrete base: steel-blue rails round louvres on
    all four faces, a rain cap on spacers over a bird mesh, a hazard band and a stencil on the base."""
    P = _root("vent_shaft")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    slat = M(STEEL, metal=0.3, wear=0.15, grime=0.45)
    K.box("base", (2.6, 2.6, 0.8), _concrete(0.3), loc=(0, 0, 0.2), bev=0.04, parent=P)
    z0, W, H = 0.6, 2.0, 2.4
    FR.framed_box(P, "tower", (0, 0, z0 + H / 2), (W, W, H), _void(), steel, fw=0.12)
    for yaw in (0, 90, 180, 270):
        t = math.radians(yaw)
        louvre(P, (W / 2 * math.sin(t), -W / 2 * math.cos(t), z0 + H / 2), W - 0.24, H - 0.24, steel, slat, yaw=yaw, n=14)
    zc = z0 + H
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("spacer", (0.1, 0.1, 0.36), gra, loc=(sx * (W / 2 - 0.06), sy * (W / 2 - 0.06), zc + 0.18), bev=0.008, parent=P)
    for k in range(7):
        t = -W / 2 + 0.15 + k * (W - 0.3) / 6
        K.box("mesh_x", (W - 0.2, 0.015, 0.015), zinc, loc=(0, t, zc + 0.05), bev=0, parent=P)
        K.box("mesh_y", (0.015, W - 0.2, 0.015), zinc, loc=(t, 0, zc + 0.05), bev=0, parent=P)
    K.box("cap", (W + 0.4, W + 0.4, 0.1), M(STEEL, metal=0.3, wear=0.25, grime=0.4, moss=0.2), loc=(0, 0, zc + 0.41), bev=0.02, parent=P)
    K.box("cap_ridge", (W - 0.2, W - 0.2, 0.12), M(STEEL, metal=0.3, wear=0.25, grime=0.4, moss=0.2), loc=(0, 0, zc + 0.51), bev=0.03, parent=P)
    FR.hazard(P, (-0.5, -1.31, 0.35), 1.2, 0.16, M(MUSTARD, wear=0.3), gra, n=8)
    stencil(P, "VS-5", (0.75, -1.305, 0.35), 0.2, gra, extrude=0.005)
    return P


def water_tower_colony(seed=57):
    """A 12 m colony water tower: four splayed steel legs on footings, braced in three tiers, a railed platform, and the
    bone capsule tank banded in steel blue with a mustard band and W-2 round it; a caged ladder, the riser pipe and a
    dead red lamp on top. Reads from the air."""
    P = _root("water_tower")
    steel, gra, zinc = M(STEEL, metal=0.35, wear=0.2, grime=0.45), _dark(), _zinc()
    galv = M(ZINC, metal=0.6, wear=0.1, grime=0.3)
    tank_m = M(BONE, wear=0.2, grime=0.4, scratches=0.2, moss=0.1)
    zp = 6.6

    def hw(z):
        return 2.0 - 0.6 * z / zp

    cs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in cs:
        K.box("footing", (0.8, 0.8, 0.5), _concrete(0.3), loc=(sx * 2.0, sy * 2.0, 0.1), bev=0.03, parent=P)
        beam(P, "leg", (sx * 2.0, sy * 2.0, 0.3), (sx * hw(zp), sy * hw(zp), zp), 0.22, steel, bev=0.015)
        K.box("leg_shoe", (0.36, 0.36, 0.06), gra, loc=(sx * 2.0, sy * 2.0, 0.38), bev=0.008, parent=P)
    levels = [0.4, 2.4, 4.5, zp - 0.05]
    for i in range(len(levels) - 1):
        za, zb = levels[i], levels[i + 1]
        for f in range(4):
            (ax, ay), (bx, by) = cs[f], cs[(f + 1) % 4]
            beam(P, "girt", (ax * hw(zb), ay * hw(zb), zb), (bx * hw(zb), by * hw(zb), zb), 0.1, steel)
            rod(P, "xbrace", (ax * hw(za), ay * hw(za), za), (bx * hw(zb), by * hw(zb), zb), 0.022, galv, segs=5)
            rod(P, "xbrace", (bx * hw(za), by * hw(za), za), (ax * hw(zb), ay * hw(zb), zb), 0.022, galv, segs=5)
    # platform and railing
    pw = 2 * hw(zp) + 0.9
    K.box("deck", (pw, pw, 0.08), M(GRAPHITE, metal=0.4, wear=0.2), loc=(0, 0, zp + 0.04), bev=0.01, parent=P)
    for f in range(4):
        (ax, ay), (bx, by) = cs[f], cs[(f + 1) % 4]
        h = pw / 2 - 0.04
        for z in (zp + 0.55, zp + 1.05):
            if f == 0 and z > 0:
                rod(P, "rail", (ax * h, ay * h, z), (0.35, ay * h, z), 0.02, zinc, segs=6)
                rod(P, "rail", (1.0, ay * h, z), (bx * h, by * h, z), 0.02, zinc, segs=6)
            else:
                rod(P, "rail", (ax * h, ay * h, z), (bx * h, by * h, z), 0.02, zinc, segs=6)
        rod(P, "rail_post", (ax * h, ay * h, zp + 0.08), (ax * h, ay * h, zp + 1.05), 0.022, zinc, segs=6)
        mx, my = (ax + bx) / 2 * h, (ay + by) / 2 * h
        if f != 0:
            rod(P, "rail_post", (mx, my, zp + 0.08), (mx, my, zp + 1.05), 0.022, zinc, segs=6)
    for x in (0.35, 1.0):
        rod(P, "rail_post", (x, -pw / 2 + 0.04, zp + 0.08), (x, -pw / 2 + 0.04, zp + 1.05), 0.022, zinc, segs=6)
    # the capsule tank on its skirt
    R, hc, hcyl = 2.0, 1.0, 2.6
    zb = zp + 0.35
    K.cyl("skirt", 1.2, 0.5, steel, loc=(0, 0, zp + 0.33), segs=20, bev=0.01, parent=P)
    prof = []
    for i in range(7):
        ph = math.radians(-90 + 15 * i)
        prof.append((max(0.01, R * math.cos(ph)), hc * (1 + math.sin(ph))))
    for i in range(7):
        ph = math.radians(15 * i)
        prof.append((max(0.01, R * math.cos(ph)), hc + hcyl + hc * math.sin(ph)))
    lathe("tank", prof, tank_m, segs=28, loc=(0, 0, zb), parent=P)
    for z in (zb + hc, zb + hc + hcyl):
        K.torus("seam_band", R + 0.01, 0.04, steel, loc=(0, 0, z), segs=36, parent=P)
    K.cyl("mustard_band", R + 0.012, 0.45, M(MUSTARD, wear=0.2, grime=0.3), loc=(0, 0, zb + hc + hcyl - 0.45), segs=36, bev=0, parent=P)
    text, cw = "W-2", 0.5
    for i, ch in enumerate(text):
        th = -math.pi / 2 + (i - (len(text) - 1) / 2) * cw / R
        stencil(P, ch, ((R + 0.008) * math.cos(th), (R + 0.008) * math.sin(th), zb + hc + 1.15), 0.95, gra,
                rot=(90, 0, math.degrees(th) + 90), extrude=0.01)
    zt = zb + 2 * hc + hcyl
    K.cyl("roof_hatch", 0.35, 0.1, steel, loc=(0, 0.6, zt - 0.08), segs=16, bev=0.01, parent=P)
    K.cyl("vent", 0.12, 0.4, steel, loc=(-0.5, -0.3, zt + 0.1), segs=12, bev=0.01, parent=P)
    K.cyl("vent_cap", 0.2, 0.12, steel, loc=(-0.5, -0.3, zt + 0.34), segs=12, bev=0.01, r2=0.05, parent=P)
    K.cyl("lamp_post", 0.03, 0.4, gra, loc=(0, 0, zt + 0.15), segs=8, bev=0, parent=P)
    K.sphere("lamp", 0.1, K.mat("deadlamp_red", DEADLAMP, rough=0.3), loc=(0, 0, zt + 0.4), scale=(1, 1, 1.2), parent=P)
    # riser pipe up the middle and its valve
    K.cyl("riser", 0.22, zp + 0.1, M(STEEL, metal=0.35, wear=0.2, grime=0.5), loc=(0, 0, (zp + 0.1) / 2), segs=16, bev=0, parent=P)
    for z in (0.15, 2.4, 4.5):
        K.cyl("riser_flange", 0.3, 0.08, gra, loc=(0, 0, z), segs=16, bev=0.01, parent=P)
    K.torus("valve_wheel", 0.22, 0.025, M(SIGNAL, wear=0.3), loc=(0, -0.45, 1.0), rot=(90, 0, 0), segs=18, parent=P)
    K.box("valve_stem", (0.06, 0.25, 0.06), gra, loc=(0, -0.3, 1.0), bev=0, parent=P)
    # caged ladder up the front to the platform gap
    ly = -2.2
    for x in (0.45, 0.9):
        rod(P, "ladder_rail", (x, ly, 0.0), (x, ly, zp + 1.1), 0.025, galv, segs=6)
    nr = int(zp / 0.3)
    for k in range(nr):
        K.box("rung", (0.45, 0.025, 0.025), galv, loc=(0.675, ly, 0.3 + k * 0.3), bev=0, parent=P)
    for z in (2.6, 3.6, 4.6, 5.6, zp + 0.9):
        K.torus("cage_hoop", 0.38, 0.016, galv, loc=(0.675, ly, z), rot=(0, 0, 180), arc=180, segs=14, parent=P)
    for a in (200, 270, 340):
        x, y = 0.675 + math.cos(math.radians(a)) * 0.38, ly + math.sin(math.radians(a)) * 0.38
        rod(P, "cage_bar", (x, y, 2.6), (x, y, zp + 0.9), 0.012, galv, segs=4)
    for z in (1.5, 4.0):
        rod(P, "standoff", (0.675, ly, z), (0.675, -hw(z), z), 0.02, gra, segs=4)
    return P


def pump_jack(seed=58):
    """An oil pump jack on a skid and pad: a Samson post A-frame, a mustard walking beam with its horsehead over the
    wellhead, bridle and polished rod, pitman arms down to cranks with graphite counterweights, a gearbox and motor."""
    P = _root("pump_jack")
    steel, gra, zinc = M(STEEL, metal=0.35, wear=0.25, grime=0.5), _dark(), _zinc()
    paint = M(MUSTARD, wear=0.35, grime=0.5, scratches=0.4)
    wire = M(ZINC, metal=0.6, wear=0.1, grime=0.3)
    K.box("pad", (7.2, 2.0, 0.3), _concrete(0.25), loc=(0.1, 0, 0.0), bev=0.03, parent=P)
    zs = 0.15
    for s in (-1, 1):
        K.box("skid", (5.8, 0.16, 0.26), steel, loc=(0.5, s * 0.45, zs + 0.13), bev=0.01, parent=P)
    for x in (-2.1, -0.5, 1.0, 2.6):
        K.box("skid_cross", (0.14, 0.95, 0.2), steel, loc=(x, 0, zs + 0.12), bev=0.01, parent=P)
    zk = zs + 0.26
    pv = Vector((0.2, 0, 3.4))
    for s in (-1, 1):
        beam(P, "samson_f", (-0.55, s * 0.45, zk), (0.2, s * 0.14, 3.18), 0.14, steel, bev=0.01)
        beam(P, "samson_b", (1.0, s * 0.45, zk), (0.2, s * 0.14, 3.18), 0.14, steel, bev=0.01)
        beam(P, "samson_tie", (-0.2, s * 0.32, 1.7), (0.6, s * 0.32, 1.7), 0.08, steel)
    K.box("saddle", (0.36, 0.42, 0.22), gra, loc=(0.2, 0, 3.22), bev=0.02, parent=P)
    K.cyl("pivot_pin", 0.06, 0.55, zinc, loc=(0.2, 0, 3.3), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    # walking beam and horsehead, tilted with the head up
    tilt = 6
    B = _empty(P, "beam", pv, (0, tilt, 0))
    K.box("web", (5.3, 0.12, 0.42), paint, loc=(-0.15, 0, 0), bev=0.01, parent=B)
    for z in (-0.23, 0.23):
        K.box("flange", (5.3, 0.3, 0.05), paint, loc=(-0.15, 0, z), bev=0.008, parent=B)
    stencil(B, "PJ-5", (0.9, -0.065, 0.0), 0.2, gra, extrude=0.004)
    Rh = 2.95
    arc = [(-Rh * math.cos(math.radians(a)), Rh * math.sin(math.radians(a))) for a in range(-34, 22, 7)]
    outline = arc + [(-2.55, 0.55), (-2.55, -1.2)]
    plate_xz("horsehead", outline, 0.34, paint, parent=B, bev=0.02)
    face = [(-(Rh + 0.03) * math.cos(math.radians(a)), (Rh + 0.03) * math.sin(math.radians(a))) for a in range(-34, 22, 7)]
    for k in range(len(face) - 1):
        beam(B, "head_face", (face[k][0], 0, face[k][1]), (face[k + 1][0], 0, face[k + 1][1]), 0.36, gra, d=0.04)
    K.box("equalizer", (0.25, 0.9, 0.2), gra, loc=(2.45, 0, -0.32), bev=0.015, parent=B)
    # bridle hanging straight down from the face to the carrier bar, polished rod into the wellhead
    fp = pv + _rot((-(Rh + 0.05) * math.cos(math.radians(-15)), 0, (Rh + 0.05) * math.sin(math.radians(-15))), (0, tilt, 0))
    xw = fp.x
    for s in (-1, 1):
        rod(P, "bridle", (xw, s * 0.1, fp.z), (xw, s * 0.1, 1.55), 0.014, wire, segs=5)
    K.box("carrier", (0.16, 0.36, 0.1), gra, loc=(xw, 0, 1.5), bev=0.01, parent=P)
    rod(P, "polished_rod", (xw, 0, 1.75), (xw, 0, 0.95), 0.03, M(ZINC, metal=0.8, wear=0.0, scratches=0.0), segs=8)
    K.cyl("stuffing_box", 0.09, 0.25, gra, loc=(xw, 0, 0.85), segs=12, bev=0.01, parent=P)
    K.cyl("wellhead", 0.12, 0.6, steel, loc=(xw, 0, 0.45), segs=12, bev=0.01, parent=P)
    for z in (0.3, 0.72):
        K.cyl("wh_flange", 0.18, 0.06, gra, loc=(xw, 0, z), segs=12, bev=0.01, parent=P)
    rod(P, "flowline", (xw, 0, 0.55), (xw, -0.8, 0.55), 0.06, steel, segs=10)
    K.torus("wh_valve", 0.12, 0.018, M(SIGNAL, wear=0.3), loc=(xw, -0.45, 0.72), segs=16, parent=P)
    K.box("wh_valve_body", (0.14, 0.14, 0.16), gra, loc=(xw, -0.45, 0.58), bev=0.01, parent=P)
    rod(P, "flowline_down", (xw, -0.8, 0.55), (xw, -0.8, 0.0), 0.06, steel, segs=10)
    # gearbox, cranks and counterweights, pitman arms
    gc = Vector((2.75, 0, 1.25))
    K.box("gear_stand", (1.0, 0.8, 0.5), steel, loc=(gc.x, 0, zk + 0.25), bev=0.015, parent=P)
    K.box("gearbox", (0.95, 0.62, 0.8), gra, loc=(gc.x, 0, gc.z - 0.1), bev=0.04, parent=P)
    K.cyl("crankshaft", 0.08, 1.1, zinc, loc=gc, rot=(90, 0, 0), segs=10, bev=0, parent=P)
    eq = pv + _rot((2.45, 0, -0.32), (0, tilt, 0))
    al = math.radians(250)
    for s in (-1, 1):
        y = s * 0.45
        pin = Vector((gc.x + math.cos(al) * 0.7, y, gc.z + math.sin(al) * 0.7))
        cw = Vector((gc.x - math.cos(al) * 0.55, y, gc.z - math.sin(al) * 0.55))
        beam(P, "crank", (pin.x, y, pin.z), (cw.x, y, cw.z), 0.22, gra, d=0.1, bev=0.01)
        W = _empty(P, "counterweight", (cw.x, y + s * 0.03, cw.z), (0, -math.degrees(al) + 90, 0))
        plate_xz("cw", [(-0.5, 0.0), (0.5, 0.0), (0.38, 0.45), (-0.38, 0.45)], 0.14, gra, loc=(0, 0, -0.05), parent=W, bev=0.015)
        for k in range(4):
            K.box("cw_haz", (0.16, 0.015, 0.12), M(MUSTARD, wear=0.3), loc=(-0.3 + k * 0.2, s * 0.075, 0.2), rot=(0, 35, 0), bev=0, parent=W)
        beam(P, "pitman", (eq.x, y, eq.z), (pin.x, y, pin.z), 0.12, steel, bev=0.01)
        K.cyl("crank_pin", 0.06, 0.18, zinc, loc=pin, rot=(90, 0, 0), segs=8, bev=0, parent=P)
    K.box("motor", (0.5, 0.45, 0.45), gra, loc=(3.75, -0.1, zk + 0.35), bev=0.03, parent=P)
    K.cyl("motor_body", 0.2, 0.5, M(STEEL, metal=0.3, wear=0.2), loc=(3.75, -0.1, zk + 0.6), rot=(90, 0, 0), segs=14, bev=0.01, parent=P)
    K.box("belt_guard", (1.15, 0.12, 0.55), paint, loc=(3.25, -0.4, zk + 0.75), rot=(0, -12, 0), bev=0.03, parent=P)
    return P


def oil_derrick_small(seed=59):
    """A small 10 m steel lattice derrick over a raised drill floor: tapering braced legs on footings, the V-door open on
    the front, a stair, a monkey board and ladder, the crown block on top and the travelling block hanging on its lines
    over the rotary table."""
    P = _root("derrick")
    steel, gra, zinc = M(STEEL, metal=0.35, wear=0.25, grime=0.45), _dark(), _zinc()
    brace = M(ZINC, metal=0.55, wear=0.15, grime=0.35)
    Ht = 10.0

    def hw(z):
        return 1.5 - 1.05 * z / Ht

    cs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in cs:
        K.box("footing", (0.6, 0.6, 0.4), _concrete(0.3), loc=(sx * 1.5, sy * 1.5, 0.05), bev=0.03, parent=P)
        beam(P, "leg", (sx * 1.5, sy * 1.5, 0.25), (sx * hw(Ht), sy * hw(Ht), Ht), 0.14, steel, bev=0.01)
    levels = [0.25, 1.3, 3.4, 5.6, 7.8, Ht]
    for i in range(len(levels) - 1):
        za, zb = levels[i], levels[i + 1]
        for f in range(4):
            (ax, ay), (bx, by) = cs[f], cs[(f + 1) % 4]
            beam(P, "girt", (ax * hw(zb), ay * hw(zb), zb), (bx * hw(zb), by * hw(zb), zb), 0.08, steel)
            if f == 0 and i < 2:
                continue  # the V-door
            if i == 0:
                continue
            beam(P, "brace", (ax * hw(za), ay * hw(za), za), (bx * hw(zb), by * hw(zb), zb), 0.05, brace)
            beam(P, "brace", (bx * hw(za), by * hw(za), za), (ax * hw(zb), ay * hw(zb), zb), 0.05, brace)
    # drill floor on its substructure
    zf = 1.3
    K.box("floor", (3.4, 3.4, 0.1), M(GRAPHITE, metal=0.4, wear=0.25, grime=0.4), loc=(0, 0, zf + 0.05), bev=0.01, parent=P)
    for x in (-1.0, 0.0, 1.0):
        K.box("sub", (0.16, 3.3, 0.2), steel, loc=(x, 0, zf - 0.1), bev=0.01, parent=P)
    for (sx, sy) in cs:
        K.box("sub_post", (0.14, 0.14, zf - 0.2), steel, loc=(sx * 1.6, sy * 1.6, (zf - 0.2) / 2), bev=0.01, parent=P)
    K.cyl("rotary", 0.4, 0.22, gra, loc=(0, 0, zf + 0.21), segs=16, bev=0.02, parent=P)
    K.box("kelly_bush", (0.2, 0.2, 0.15), zinc, loc=(0, 0, zf + 0.38), bev=0.01, parent=P)
    # drawworks at the back
    K.box("draw_frame", (1.4, 0.7, 0.6), M(MUSTARD, wear=0.35, grime=0.5), loc=(0, 1.15, zf + 0.4), bev=0.03, parent=P)
    K.cyl("drum", 0.25, 1.0, gra, loc=(0, 1.1, zf + 0.85), rot=(0, 90, 0), segs=14, bev=0.01, parent=P)
    # railing on three sides of the floor
    for (a, b) in (((-1.65, 1.65), (1.65, 1.65)), ((-1.65, -1.65), (-1.65, 1.65)), ((1.65, -1.65), (1.65, 0.6))):
        for z in (zf + 0.55, zf + 1.05):
            rod(P, "rail", (a[0], a[1], z), (b[0], b[1], z), 0.02, zinc, segs=6)
    for (x, y) in ((-1.65, 0.0), (-1.65, 1.65), (1.65, 1.65), (0.0, 1.65), (1.65, 0.6), (-1.65, -1.65), (1.65, -1.65)):
        rod(P, "rail_post", (x, y, zf + 0.1), (x, y, zf + 1.05), 0.022, zinc, segs=6)
    # stair up the right side
    for y in (0.75, 1.55):
        beam(P, "stringer", (1.7, y, zf + 0.05), (3.1, y, 0.0), 0.05, gra, d=0.16)
    for k in range(4):
        t = (k + 1) / 5
        K.box("tread", (0.24, 0.75, 0.04), zinc, loc=(1.7 + 1.4 * t, 1.15, zf - zf * t), bev=0, parent=P)
    rod(P, "stair_rail", (1.7, 1.6, zf + 1.0), (3.1, 1.6, 0.95), 0.02, zinc, segs=6)
    rod(P, "stair_post", (3.1, 1.6, 0.0), (3.1, 1.6, 0.95), 0.022, zinc, segs=6)
    # monkey board and ladder on the back face
    zm = 7.0
    K.box("monkey", (1.1, 0.7, 0.06), M(GRAPHITE, metal=0.4, wear=0.25), loc=(0, hw(zm) - 0.3, zm), bev=0.005, parent=P)
    rod(P, "monkey_rail", (-0.55, hw(zm) - 0.65, zm + 0.9), (0.55, hw(zm) - 0.65, zm + 0.9), 0.02, zinc, segs=6)
    for x in (-0.2, 0.2):
        rod(P, "ladder", (x, hw(zf) + 0.05, zf + 0.1), (x, hw(zm) + 0.05, zm), 0.02, zinc, segs=6)
    for k in range(int((zm - zf) / 0.35)):
        z = zf + 0.35 + k * 0.35
        K.box("rung", (0.4, 0.02, 0.02), zinc, loc=(0, hw(z) + 0.05, z), bev=0, parent=P)
    # crown block, lines and travelling block
    K.box("crown_frame", (1.0, 1.0, 0.25), steel, loc=(0, 0, Ht + 0.12), bev=0.02, parent=P)
    for x in (-0.25, 0.0, 0.25):
        K.cyl("sheave", 0.22, 0.08, gra, loc=(x, 0, Ht + 0.38), rot=(0, 90, 0), segs=14, bev=0.01, parent=P)
    K.box("gin_pole", (0.08, 0.08, 1.1), steel, loc=(0, 0.45, Ht + 0.55), bev=0.005, parent=P)
    zb = 5.6
    wire = M(ZINC, metal=0.6, wear=0.1, grime=0.3)
    for x in (-0.25, -0.08, 0.08, 0.25):
        rod(P, "line", (x, 0, Ht + 0.2), (x * 0.5, 0, zb + 0.3), 0.01, wire, segs=4)
    K.box("tblock", (0.4, 0.3, 0.6), M(MUSTARD, wear=0.35, grime=0.5), loc=(0, 0, zb), bev=0.04, parent=P)
    K.torus("hook", 0.12, 0.03, gra, loc=(0, 0, zb - 0.45), rot=(90, 0, 0), arc=270, segs=14, parent=P)
    rod(P, "hook_shank", (0, 0, zb - 0.3), (0, 0, zb - 0.33), 0.04, gra, segs=8)
    K.box("hook_link", (0.06, 0.06, 0.25), gra, loc=(0, 0, zb - 0.3), bev=0, parent=P)
    stencil(P, "DR-1", (0, -hw(3.0) - 0.08, 3.0), 0.3, M(BONE, wear=0.2), extrude=0.006)
    K.box("plate", (0.85, 0.03, 0.4), gra, loc=(0, -hw(3.0) - 0.05, 3.0), bev=0.005, parent=P)
    beam(P, "plate_bar", (-hw(3.0), -hw(3.0) - 0.02, 3.0), (hw(3.0), -hw(3.0) - 0.02, 3.0), 0.05, brace)
    return P


def _pipe(P, pts, r, mat, joints=True):
    """A pipe run through pts with a fitting at each bend."""
    for k in range(len(pts) - 1):
        rod(P, "pipe", pts[k], pts[k + 1], r, mat, segs=10)
        if joints and 0 < k:
            K.sphere("elbow", r * 1.25, mat, loc=pts[k], parent=P)


def gas_meter_station(seed=60):
    """A colony gas meter station on a pad under a little steel roof: mustard gas pipes up out of the ground through
    isolation valves, a filter, a regulator and a meter with its dial, a bypass line, and a cabinet with the warning
    plate."""
    P = _root("gas_station")
    steel, gra, zinc = _steel(), _dark(), _zinc()
    gas = M(MUSTARD, wear=0.25, grime=0.5, scratches=0.3)
    red = M(SIGNAL, wear=0.3, grime=0.3)
    bone = _bone(wear=0.25)
    K.box("pad", (3.4, 2.2, 0.25), _concrete(0.25), loc=(0, 0, 0.0), bev=0.03, parent=P)
    zp = 0.125
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("roof_post", (0.08, 0.08, 2.2), gra, loc=(sx * 1.55, sy * 0.95, zp + 1.1), bev=0.008, parent=P)
    K.box("roof", (3.5, 2.3, 0.08), M(STEEL, metal=0.3, wear=0.25, grime=0.4, moss=0.2), loc=(0, 0, zp + 2.25), rot=(4, 0, 0), bev=0.015, parent=P)
    for sy in (-1, 1):
        K.box("roof_beam", (3.3, 0.08, 0.12), gra, loc=(0, sy * 0.95, zp + 2.16 + sy * 0.065), bev=0.008, parent=P)
    r = 0.07
    yM, yB, zM, zB = 0.35, -0.3, 1.0, 0.55
    _pipe(P, [(-1.3, yM, -0.05), (-1.3, yM, zM), (1.3, yM, zM), (1.3, yM, -0.05)], r, gas)
    _pipe(P, [(-1.3, yM, zB), (-1.3, yB, zB), (1.3, yB, zB), (1.3, yM, zB)], 0.05, gas)
    for x in (-1.3, 1.3):
        K.sphere("tee", r * 1.25, gas, loc=(x, yM, zB), parent=P)
    for x, z in ((-1.3, 0.3), (1.3, 0.3), (-0.95, zM), (0.95, zM), (-0.2, zM), (0.35, zM)):
        if z == zM:
            K.cyl("flange", r + 0.05, 0.05, gra, loc=(x, yM, z), rot=(0, 90, 0), segs=10, bev=0, parent=P)
        else:
            K.cyl("flange", r + 0.05, 0.05, gra, loc=(x, yM, z), segs=10, bev=0, parent=P)

    def valve(x, y, z, rr=0.15):
        K.box("valve_body", (0.2, 0.18, 0.18), gas, loc=(x, y, z), bev=0.02, parent=P)
        K.cyl("valve_stem", 0.02, 0.25, gra, loc=(x, y, z + 0.18), segs=6, bev=0, parent=P)
        K.torus("handwheel", rr, 0.018, red, loc=(x, y, z + 0.3), segs=12, parent=P)
        for k in range(2):
            K.box("hw_spoke", (rr * 2, 0.02, 0.015), red, loc=(x, y, z + 0.3), rot=(0, 0, 45 + 90 * k), bev=0, parent=P)

    valve(-1.05, yM, zM)
    valve(1.05, yM, zM)
    valve(0.0, yB, zB, rr=0.12)
    # filter, regulator, meter
    K.cyl("filter", 0.15, 0.62, gas, loc=(-0.6, yM, zM + 0.05), segs=16, bev=0.03, parent=P)
    K.cyl("filter_cap", 0.17, 0.06, gra, loc=(-0.6, yM, zM + 0.38), segs=16, bev=0.01, parent=P)
    K.cyl("reg_body", 0.1, 0.18, gra, loc=(0.6, yM, zM), rot=(0, 90, 0), segs=12, bev=0.01, parent=P)
    K.cyl("reg_dome", 0.2, 0.12, gas, loc=(0.6, yM, zM + 0.2), segs=18, bev=0.02, r2=0.15, parent=P)
    K.cyl("reg_spring", 0.06, 0.22, gas, loc=(0.6, yM, zM + 0.37), segs=10, bev=0.01, parent=P)
    K.box("meter", (0.42, 0.38, 0.46), M(ZINC, metal=0.5, wear=0.2, grime=0.4), loc=(0.1, yM, zM - 0.05), bev=0.03, parent=P)
    K.cyl("dial", 0.11, 0.03, bone, loc=(0.1, yM - 0.2, zM + 0.03), rot=(90, 0, 0), segs=16, bev=0.005, parent=P)
    K.box("needle", (0.012, 0.005, 0.08), gra, loc=(0.12, yM - 0.218, zM + 0.05), rot=(0, -30, 0), bev=0, parent=P)
    K.box("counter", (0.2, 0.01, 0.05), gra, loc=(0.1, yM - 0.195, zM - 0.17), bev=0, parent=P)
    for x in (-1.3, 1.3):
        rod(P, "gauge_stub", (x, yM, zM - 0.25), (x, yM - 0.15, zM - 0.25), 0.015, gra, segs=6)
        K.cyl("gauge", 0.06, 0.04, bone, loc=(x, yM - 0.17, zM - 0.25), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    for x in (-0.3, 0.75):
        K.box("support", (0.07, 0.07, zM - zp - r), gra, loc=(x, yM, zp + (zM - zp - r) / 2), bev=0, parent=P)
        K.box("saddle", (0.12, 0.2, 0.04), gra, loc=(x, yM, zM - r - 0.02), bev=0, parent=P)
    # cabinet at the back with its plate and code
    FR.framed_box(P, "cabinet", (-0.9, 0.8, zp + 0.55), (0.7, 0.35, 1.1), bone, steel, fw=0.045)
    warning_plate(P, (-0.9, 0.66, zp + 0.8), 0.24)
    stencil(P, "GM-4", (-0.9, 0.662, zp + 0.4), 0.11, gra, extrude=0.004)
    return P


def culvert_pipe(seed=61):
    """A 2.1 m concrete culvert pipe section half buried along a ditch: a bell on its back end, a silted floor with a
    puddle, mossy earth heaped along its flanks and rusty rebar out of a broken front rim."""
    rnd = random.Random(seed)
    P = _root("culvert")
    conc = K.mat("concrete_culvert", CONCRETE, rough=0.9, wear=0.3, grime=0.5, cracks=0.2, moss=0.2)
    Ro, Ri, L, zc = 1.05, 0.88, 3.0, 0.5
    _sharp(lathe("pipe", [(Ri, -L / 2), (Ro, -L / 2), (Ro, L / 2), (Ri, L / 2)], conc, segs=28, loc=(0, 0, zc), rot=(90, 0, 0), parent=P))
    _sharp(lathe("bell", [(Ro - 0.02, -L / 2), (Ro + 0.12, -L / 2), (Ro + 0.12, -L / 2 + 0.45), (Ro - 0.02, -L / 2 + 0.5)], conc, segs=28,
                 loc=(0, 0, zc), rot=(90, 0, 0), parent=P))
    K.box("silt", (1.5, L - 0.04, 0.1), _soil(0.0), loc=(0, 0, 0.03), bev=0.02, parent=P)
    K.box("puddle", (0.7, 1.4, 0.01), K.mat("puddle", "#3d5a5c", rough=0.05), loc=(0.1, -0.3, 0.085), bev=0, parent=P)
    berm = _soil(0.9)
    for s in (-1, 1):
        for k in range(4):
            y = -1.2 + k * 0.8 + rnd.uniform(-0.1, 0.1)
            K.sphere("berm", 0.7, berm, loc=(s * (Ro + 0.2), y, -0.05), scale=(0.85, 1.25, 0.45 + rnd.uniform(0, 0.1)), parent=P)
    rust = M(RUST, metal=0.4, wear=0.2, grime=0.4)
    for k in range(5):
        a = math.radians(40 + k * 14)
        rr = (Ri + Ro) / 2
        rod(P, "rebar", (math.cos(a) * rr, -L / 2 + 0.05, zc + math.sin(a) * rr),
            (math.cos(a) * (rr + rnd.uniform(-0.05, 0.05)), -L / 2 - 0.15 - rnd.uniform(0, 0.15), zc + math.sin(a) * rr + rnd.uniform(-0.1, 0.05)), 0.012, rust, segs=5)
    K.box("rim_chunk", (0.35, 0.25, 0.14), conc, loc=(1.25, -1.9, 0.07), rot=(10, 4, 35), bev=0.03, parent=P)
    return P


def drainage_grate(seed=62):
    """A concrete drain block set flush in the ground: a steel grate of cross bars in an angle frame over a dark pit,
    moss in the joints and a few dead leaves on the bars. Walked over, not round."""
    rnd = random.Random(seed)
    P = _root("drain")
    conc = K.mat("concrete_drain", CONCRETE, rough=0.9, wear=0.3, grime=0.5, cracks=0.3, moss=0.6)
    gra = M(GRAPHITE, metal=0.4, wear=0.25, grime=0.5)
    L, Wd, oL, oW, top = 1.2, 0.8, 0.8, 0.42, 0.08
    sw, ew = (Wd - oW) / 2, (L - oL) / 2
    for s in (-1, 1):
        K.box("side", (L, sw, 0.3), conc, loc=(0, s * (oW / 2 + sw / 2), top - 0.15), bev=0.02, parent=P)
        K.box("end", (ew, oW, 0.3), conc, loc=(s * (oL / 2 + ew / 2), 0, top - 0.15), bev=0.01, parent=P)
        K.box("angle_x", (oL + 0.04, 0.03, 0.03), gra, loc=(0, s * (oW / 2 + 0.005), top - 0.01), bev=0, parent=P)
        K.box("angle_y", (0.03, oW + 0.04, 0.03), gra, loc=(s * (oL / 2 + 0.005), 0, top - 0.01), bev=0, parent=P)
    K.box("pit", (oL, oW, 0.02), _void(), loc=(0, 0, 0.012), bev=0, parent=P)
    for k in range(11):
        x = -oL / 2 + 0.04 + k * (oL - 0.08) / 10
        K.box("bar", (0.022, oW - 0.02, 0.04), gra, loc=(x, 0, top - 0.03), bev=0, parent=P)
    for y in (-0.1, 0.1):
        K.box("tie", (oL - 0.02, 0.015, 0.02), gra, loc=(0, y, top - 0.04), bev=0, parent=P)
    leaf = K.mat("leaf", "#7a6a3a", rough=0.9)
    for k in range(5):
        K.box("leaf", (0.07, 0.04, 0.004), leaf, loc=(rnd.uniform(-0.35, 0.35), rnd.uniform(-0.18, 0.18), top - 0.008), rot=(0, 0, rnd.uniform(0, 180)), bev=0, parent=P)
    return P


def retaining_wall_concrete(seed=63):
    """A 6 m cast concrete retaining wall section, 2.6 m high, holding back a bank of earth: a toe footing, a coping,
    formwork joints and tie holes, an expansion joint, weep pipes with water stains under them and a crack or two."""
    rnd = random.Random(seed)
    P = _root("retaining_wall")
    conc = K.mat("concrete_wall", CONCRETE, rough=0.9, wear=0.3, grime=0.6, cracks=0.25, moss=0.4, noise_scale=4.0)
    seam = K.mat("concrete_seam", "#7e7c74", rough=0.95)
    stain = K.mat("stain", STAIN, rough=0.9)
    L, T, H = 6.0, 0.4, 2.6
    K.box("wall", (L, T, H), conc, loc=(0, 0, H / 2 - 0.1), bev=0.02, parent=P)
    K.box("toe", (L, 0.75, 0.25), conc, loc=(0, -0.12, 0.02), bev=0.02, parent=P)
    K.box("coping", (L + 0.06, T + 0.12, 0.12), conc, loc=(0, -0.02, H - 0.04), bev=0.02, parent=P)
    fy = -T / 2
    for x in (-1.5, 1.5):
        K.box("form_joint", (0.015, 0.01, H - 0.4), seam, loc=(x, fy - 0.002, H / 2 - 0.05), bev=0, parent=P)
    K.box("lift_joint", (L - 0.02, 0.01, 0.015), seam, loc=(0, fy - 0.002, 1.3), bev=0, parent=P)
    K.box("expansion", (0.035, 0.012, H - 0.15), K.mat("sealant", "#2b2f35", rough=0.7), loc=(0.0, fy - 0.003, H / 2 - 0.05), bev=0, parent=P)
    for x in (-2.25, -0.75, 0.75, 2.25):
        for z in (0.75, 1.85):
            K.cyl("tie_hole", 0.022, 0.012, seam, loc=(x + 0.3, fy - 0.003, z), rot=(90, 0, 0), segs=6, bev=0, parent=P)
        K.cyl("weep", 0.065, 0.24, M(GRAPHITE, metal=0.2, wear=0.2), loc=(x, fy - 0.06, 0.45), rot=(90, 0, 0), segs=10, bev=0, parent=P)
        K.cyl("weep_hole", 0.045, 0.01, _void(), loc=(x, fy - 0.181, 0.45), rot=(90, 0, 0), segs=10, bev=0, parent=P)
        K.box("streak", (0.1, 0.006, 0.28), stain, loc=(x, fy - 0.003, 0.25), bev=0, parent=P)
        K.box("streak_hi", (0.05, 0.006, 0.6), stain, loc=(x + 0.05, fy - 0.003, H - 0.45), bev=0, parent=P)
    for k in range(3):
        K.box("crack", (0.006, 0.006, rnd.uniform(0.35, 0.6)), seam, loc=(-2.0 + k * 1.6 + rnd.uniform(-0.3, 0.3), fy - 0.004, 0.9 + rnd.uniform(0, 0.8)),
              rot=(0, rnd.uniform(-35, 35), 0), bev=0, parent=P)
    # the bank it holds back: an earth wedge from the coping down to the ground behind
    bm = bmesh.new()
    prof = [(T / 2, -0.05), (T / 2, H - 0.12), (0.9, H - 0.12), (3.2, -0.05)]
    a = [bm.verts.new((-L / 2, y, z)) for y, z in prof]
    b = [bm.verts.new((L / 2, y, z)) for y, z in prof]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((a[j], a[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    K.place(K.from_bm("bank", bm, _soil(1.0)), parent=P)
    return P


def jersey_barrier_stack(seed=64):
    """Concrete jersey barriers stacked for placing: two side by side on the ground, one laid across them on timber
    bearers, a fourth stood off to the side, lifting eyes cast in their tops."""
    P = _root("jersey_stack")
    timber = wood(2, wear=0.3)
    zinc = M(RUST, metal=0.5, wear=0.2, grime=0.4)
    spots = ((0.0, -0.33, 0.0, 0), (0.06, 0.33, 0.0, 1.5), (0.03, 0.0, 0.9, -2.5), (2.35, 0.1, 0.0, 78))
    for k, (x, y, z, yaw) in enumerate(spots):
        b = concrete_barrier(seed=seed + k)
        b.parent = P
        b.location = (x, y, z)
        b.rotation_euler = (0, 0, math.radians(yaw))
        for e in (-0.6, 0.6):
            K.torus("eye", 0.06, 0.014, zinc, loc=(e, 0, 0.8), rot=(90, 0, 0), arc=180, segs=10, parent=b)
    for e in (-0.6, 0.6):
        K.box("bearer", (0.14, 1.05, 0.1), timber, loc=(e, 0.0, 0.85), bev=0.012, parent=P)
    return P


def sandbag_wall(seed=65):
    """A curved sandbag wall about a metre high: staggered courses of khaki bags, two bags thick at the bottom and one
    above, the ends stepped down, the bags slumped and creased."""
    rnd = random.Random(seed)
    P = _root("sandbags")
    mats = [K.mat("sack", SACK, rough=0.95, grime=0.35, noise_scale=10.0), K.mat("sack_d", SACK_D, rough=0.95, grime=0.4, noise_scale=10.0)]
    R, cy = 3.0, 2.55
    a0, a1 = math.radians(234), math.radians(306)
    bl, bw, bh = 0.6, 0.34, 0.17
    for c in range(6):
        z = bh / 2 + c * (bh - 0.015)
        radii = (R - 0.18, R + 0.18) if c < 3 else (R,)
        shrink = max(0, c - 2) * 0.03
        for ri, r in enumerate(radii):
            span = (a1 - a0) - 2 * shrink
            n = max(2, int(r * span / (bl * 0.98)))
            step = span / n
            off = 0.5 * step if (c + ri) % 2 else 0.0
            for k in range(n + (1 if off == 0 else 0)):
                a = a0 + shrink + off + k * step
                if a > a1 - shrink + 1e-6:
                    continue
                x, y = r * math.cos(a), cy + r * math.sin(a)
                K.box("bag", (bl * rnd.uniform(0.95, 1.03), bw * rnd.uniform(0.95, 1.05), bh), mats[rnd.randint(0, 1)],
                      loc=(x, y, z + rnd.uniform(-0.01, 0.01)), rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), math.degrees(a) + 90 + rnd.uniform(-4, 4)),
                      bev=0.07, parent=P)
    return P


PROPS = {
    "substation_fence": substation_fence,
    "switch_cabinet": switch_cabinet,
    "transformer_pole": transformer_pole,
    "wind_turbine_colony": wind_turbine_colony,
    "solar_farm_row": solar_farm_row,
    "battery_bank": battery_bank,
    "data_cabinet": data_cabinet,
    "server_rack_wreck": server_rack_wreck,
    "antenna_array": antenna_array,
    "weather_station": weather_station,
    "sensor_mast": sensor_mast,
    "survey_drone_crashed": survey_drone_crashed,
    "robot_arm_fallen": robot_arm_fallen,
    "charging_post": charging_post,
    "maintenance_hatch": maintenance_hatch,
    "ventilation_shaft": ventilation_shaft,
    "water_tower_colony": water_tower_colony,
    "pump_jack": pump_jack,
    "oil_derrick_small": oil_derrick_small,
    "gas_meter_station": gas_meter_station,
    "culvert_pipe": culvert_pipe,
    "drainage_grate": drainage_grate,
    "retaining_wall_concrete": retaining_wall_concrete,
    "jersey_barrier_stack": jersey_barrier_stack,
    "sandbag_wall": sandbag_wall,
}
