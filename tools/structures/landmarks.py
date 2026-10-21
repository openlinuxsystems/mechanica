"""Landmarks: big set pieces that stand alone in open country, one or a few per region, so a player can steer by
them from the ground at 500 m and from the air (docs/art-direction.md: a landmark reads as a silhouette alone).
They hold no loot; they are things to climb, drive under, fly through and look out from.

Colony Works: a relay spire that buckled and fell with its crown still lit, a pair of cooling towers (one half
collapsed) and an unfinished aqueduct with its launching gantry still out over the gap. Old Stone: a kneeling giant
resting on a planted sword, a sky arch with steps carved up its long back, and an overgrown stepped ziggurat.
Settler Patchwork: a colony water tower the settlers crowned with a windmill. Salvager Tech: a deep-space freighter
many times the SV-04's size, broken in two, its beacon still burning cyan.

Each builder returns an empty with the landmark under it, its origin on the ground at the landmark's centre (Blender Z
up, front towards -Y). build_structures.py exports each as a structure (meshes/structures/<id>.glb) that a POI prefab
places, with a collider of its larger parts. What the design says a player climbs stays walkable: a flight of steps no
steeper than RAMP_MAX collides as a smooth ramp (steeper ones keep their 0.3 m steps, which the player steps up),
collide_only ramps smooth the other ways up, and cables, leaves, crystals and loose rubble on a ramp do not collide.
"""
import fnmatch
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import (M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC,
                      HULL, GUNMETAL, CYAN, COPPER, CONCRETE, WARM)
from props import _root, wood
from props_colony_a import beam, rod, stencil, dish_mesh, _concrete
from props_stone import moss, tapered, earth, empty, bark, WATER
from props_nature import hull, crag, boulder, leaf, ball
from props_ruins_a import lathe, crystal, glyph, cyan, profile_slab
from props_coast import mound, tube

GLASS_D = "#2c3a42"
RAMP_MAX = 40.0          # degrees: a flight of steps up to this steep collides as a ramp


# ---------------------------------------------------------------- helpers
def block(P, name, a, b, s0, s1, mat, bev=0.12, up=(0, 0, 1)):
    """A four-sided block from point a to point b, s0 = (w, d) across at a and s1 at b: w lies across the block level
    with the ground (along X when the block stands upright), d across it the other way."""
    a, b = Vector(a), Vector(b)
    c = (a + b) / 2
    t = (b - a).normalized()
    u = Vector(up).cross(t)
    if u.length < 1e-3:
        u = Vector((1, 0, 0))
    u.normalize()
    v = t.cross(u)
    bm = bmesh.new()
    vs = []
    for end, (w, d) in ((a, s0), (b, s1)):
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            vs.append(bm.verts.new(end - c + u * sx * w / 2 + v * sy * d / 2))
    bm.faces.new(vs[0:4])
    bm.faces.new(vs[4:8])
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((vs[i], vs[j], vs[4 + j], vs[4 + i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    if bev:
        K.bevel(ob, bev)
    return K.place(ob, tuple(c), parent=P)


def orient(ob, d):
    """Turns ob so its local Z runs along d."""
    ob.rotation_euler = Vector(d).to_track_quat("Z", "Y").to_euler()
    return ob


def lattice(P, name, a, b, w0, w1, panels, legs, brace, leg=0.7, br=0.3, band=2, up=(0, 0, 1)):
    """A square lattice mast from a to b, w0 wide at a and w1 at b: legs per panel (their paint changes every band
    panels, the colours of legs in turn), a ring of horizontals and one diagonal per face, alternating."""
    a, b = Vector(a), Vector(b)
    t = (b - a).normalized()
    u = Vector(up).cross(t)
    if u.length < 1e-3:
        u = Vector((1, 0, 0))
    u.normalize()
    v = t.cross(u)
    sq = ((-1, -1), (1, -1), (1, 1), (-1, 1))

    def corners(f):
        c = a + (b - a) * f
        h = (w0 + (w1 - w0) * f) / 2
        return [c + u * sx * h + v * sy * h for sx, sy in sq]

    for i in range(panels):
        c0, c1 = corners(i / panels), corners((i + 1) / panels)
        lm = legs[(i // band) % len(legs)]
        for k in range(4):
            beam(P, f"{name}_leg", c0[k], c1[k], leg, lm)
            beam(P, f"{name}_h", c0[k], c0[(k + 1) % 4], br, brace)
            if i % 2:
                beam(P, f"{name}_d", c0[k], c1[(k + 1) % 4], br * 0.8, brace)
            else:
                beam(P, f"{name}_d", c0[(k + 1) % 4], c1[k], br * 0.8, brace)
    c1 = corners(1.0)
    for k in range(4):
        beam(P, f"{name}_h", c1[k], c1[(k + 1) % 4], br, brace)
    return corners


def stair(P, name, a, b, width, mat, rise=0.3, base=1.5):
    """A flight of steps cut as one block from the foot a to the top b (its run is level, its rise rise per step),
    base thick under the nosings so it beds into what it climbs."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    L, H = run.length, b.z - a.z
    n = max(1, round(H / rise))
    r, h = L / n, H / n
    prof = [(0.0, 0.0)]
    for i in range(n):
        prof += [(i * r, (i + 1) * h), ((i + 1) * r, (i + 1) * h)]
    prof += [(L, H - base), (0.0, -base)]
    bm = bmesh.new()
    f = [bm.verts.new((x, -width / 2, z)) for x, z in prof]
    k = [bm.verts.new((x, width / 2, z)) for x, z in prof]
    bm.faces.new(f)
    bm.faces.new(list(reversed(k)))
    for i in range(len(prof)):
        j = (i + 1) % len(prof)
        bm.faces.new((f[j], f[i], k[i], k[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    yaw = (0, 0, math.degrees(math.atan2(run.y, run.x)))
    if math.degrees(math.atan2(H, L)) <= RAMP_MAX:
        # walked as a ramp from half a step up at the foot to the top, so wheels and feet roll up it smoothly
        K.no_collide(ob)
        K.place(K.collide_only(_prism(f"{name}_ramp", (0, 0, h / 2), (L, 0, H), width, base)), tuple(a), yaw, P)
        # and a lip past the top, over the edge of whatever it climbs onto
        K.place(K.collide_only(_prism(f"{name}_lip", (L, 0, H), (L + 0.6, 0, H), width, base)), tuple(a), yaw, P)
    return K.place(ob, tuple(a), yaw, P)


def _prism(name, a, b, width, base):
    """A slab whose top runs straight from a to b, width across (level), its sides dropping base metres straight down."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    u = Vector((-run.y, run.x, 0)).normalized() * width / 2
    bm = bmesh.new()
    top = [bm.verts.new(p) for p in (a - u, b - u, b + u, a + u)]
    bot = [bm.verts.new(v.co - Vector((0, 0, base))) for v in top]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((top[j], top[i], bot[i], bot[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.from_bm(name, bm, None)


def ramp(P, name, a, b, width, base=1.0):
    """A collision-only ramp (not drawn) whose top runs from a to b: a way up that the drawn model shows as rubble,
    earth or a step."""
    return K.place(K.collide_only(_prism(name, a, b, width, base)), parent=P)


def quiet(P, *stems):
    """Takes the parts named stem (before any .001 suffix; a stem may hold * wildcards) out of the collider: cables,
    leaves, crystals, glows."""
    for ob in P.children_recursive:
        if ob.type == "MESH" and any(fnmatch.fnmatchcase(ob.name.split(".")[0], t) for t in stems):
            K.no_collide(ob)
    return P


def spline(pts, sub):
    """Catmull-Rom through pts, sub points per span."""
    pts = [Vector(p) for p in pts]
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2 = pts[max(i - 1, 0)], pts[i], pts[i + 1]
        p3 = pts[min(i + 2, len(pts) - 1)]
        for s in range(sub):
            t = s / sub
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    return out


def rock_mat():
    """The Old Stone rock material for faces tens of metres across: the family's stone with its crack lines toned
    down, since they scale with the block."""
    return K.mat("lm_stone", "#8f9289", rough=0.9, wear=0.0, grime=0.35, moss=1.0, cracks=0.3, noise_scale=3.0)


def rock_dark():
    return K.mat("lm_stone_d", "#5f645c", rough=0.9, grime=0.3, moss=0.6, cracks=0.35, noise_scale=3.0)


def interp(vals, f):
    """The value at f (0..1) of vals spread evenly over 0..1."""
    x = f * (len(vals) - 1)
    i = min(int(x), len(vals) - 2)
    return vals[i] + (vals[i + 1] - vals[i]) * (x - i)


def tree(P, name, loc, h, rnd, crown=None):
    """A broadleaf tree that took root on old stone: a leaning trunk and two or three crowns."""
    x, y, z = loc
    lean = (rnd.uniform(-8, 8), rnd.uniform(-8, 8))
    tr = K.cyl(f"{name}_trunk", h * 0.045, h * 0.7, bark(0.3), loc=(x, y, z + h * 0.33), rot=(lean[0], lean[1], 0), segs=7, r2=h * 0.025, bev=0, parent=P)
    top = Vector((x, y, z)) + tr.rotation_euler.to_matrix() @ Vector((0, 0, h * 0.7))
    cr = crown or h * 0.3
    for k in range(2):
        ball(P, f"{name}_crown{k}", cr * rnd.uniform(0.7, 1.0), top + Vector((rnd.uniform(-cr, cr) * 0.6, rnd.uniform(-cr, cr) * 0.6, rnd.uniform(-0.2, 0.4) * cr)),
             leaf(k), scale=(1.0, 1.0, 0.8))


def bush(P, name, loc, r, rnd, shade=1):
    ball(P, name, r, loc, leaf(shade), scale=(rnd.uniform(1.0, 1.5), rnd.uniform(0.9, 1.3), 0.6))


# ================================================================== Colony Works
def relay_spire(seed=101):
    """Relay spire RS-7: a 110 m colony relay mast, red and bone, that buckled at 40 m. Its upper 65 m leans down onto
    the ground with the crown drum crushed at the far end, its beacon still blinking cyan. Drive or fly under the
    span; walk up the fallen section's catwalk from the crown to the break, 40 m up."""
    rnd = random.Random(seed)
    P = _root("relay_spire")
    red, bone = M(SIGNAL, metal=0.3, wear=0.15), M(BONE, metal=0.2, wear=0.15)
    steel, gra, mus = M(STEEL, metal=0.4, wear=0.2), M(GRAPHITE, metal=0.4, wear=0.2), M(MUSTARD, wear=0.2)
    conc = _concrete()
    # footing: a stepped concrete pad and four leg piers
    K.box("footing", (15, 15, 1.0), conc, loc=(0, 0, 0.3), bev=0.1, parent=P)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("pier", (2.4, 2.4, 1.6), conc, loc=(sx * 3.5, sy * 3.5, 1.0), bev=0.1, parent=P)
    w = lambda s: 7.0 - 3.6 * s / 110            # the mast's width at s metres along it
    # the standing stump, 1.8 m to 40 m
    lattice(P, "stump", (0, 0, 1.8), (0, 0, 40), w(0), w(38), 10, (red, bone), steel, leg=0.75)
    for z in (14.0, 27.5):     # service platforms
        ww = w(z) + 2.6
        K.box("deck", (ww, ww, 0.35), steel, loc=(0, 0, z), bev=0.05, parent=P)
        for k in range(4):
            g = K.box("kerb", (ww, 0.12, 1.0), mus, loc=(0, -ww / 2, z + 0.6), bev=0, parent=P)
            g.rotation_euler.z = math.radians(90 * k)
            g.location = (math.sin(math.radians(90 * k)) * ww / 2, -math.cos(math.radians(90 * k)) * ww / 2, z + 0.6)
    # the fallen upper section: from the break down to the crown on the ground
    A = Vector((4.0, 0.6, 40.5))
    B = Vector((58.0, -5.0, 7.0))
    d = (B - A).normalized()
    corn = lattice(P, "fallen", A, B, w(42), w(105), 16, (red, bone), steel, leg=0.75)
    # buckled legs: the stump's top corners bent over into the fallen section's foot
    top = [Vector((sx * w(38) / 2, sy * w(38) / 2, 40)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    foot = corn(0.0)
    for k in (1, 2):
        kink = top[k].lerp(foot[k], 0.5) + Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 1.6))
        beam(P, "bent", top[k], kink, 0.75, red)
        beam(P, "bent", kink, foot[k], 0.75, red)
    for k in (0, 3):           # the far legs tore: stubs sticking up and torn ends
        tip = top[k] + Vector((rnd.uniform(-1.5, 0.5), rnd.uniform(-1, 1), rnd.uniform(2.0, 3.5)))
        beam(P, "torn", top[k], tip, 0.7, bone)
        tip2 = foot[k] - d * rnd.uniform(1.5, 2.5) + Vector((0, 0, rnd.uniform(-1.5, -0.5)))
        beam(P, "torn", foot[k], tip2, 0.7, red)
    # a catwalk along the fallen section's upper face, a way up from the crown to the break: its last length climbs
    # off the mast onto the crown drum's top, so a player walks up the bank, along the drum and up the catwalk
    u = Vector((0, 0, 1)).cross(d).normalized()
    v = d.cross(u)
    cr = B + d * 2.5
    for i in range(4):
        f0, f1 = 0.06 + i * 0.235, 0.06 + (i + 1) * 0.235
        p0 = A + (B - A) * f0 + v * (w(42 + 63 * f0) / 2 + 0.45)
        p1 = A + (B - A) * f1 + v * (w(42 + 63 * f1) / 2 + 0.45) if i < 3 else cr - d * 3.4 + v * 4.75
        block(P, "catwalk", p0, p1, (1.4, 0.18), (1.4, 0.18), steel, bev=0)
        beam(P, "handrail", p0 + u * 0.7 + v * 1.0, p1 + u * 0.7 + v * 1.0, 0.1, mus)
    ramp(P, "catwalk_ramp", cr - d * 1.8 + v * 4.62, p1 + v * 0.09, 1.6, base=0.6)    # over the drum's rib onto it
    # the crown: the colony's rounded drum, crushed into the ground, a dish torn half off, the beacon still lit
    orient(K.cyl("crown", 4.6, 7.5, bone, loc=tuple(cr), segs=20, bev=0.12, parent=P), d)
    for s in (-3.2, 3.2):
        orient(K.cyl("crown_rib", 4.8, 0.5, steel, loc=tuple(cr + d * s), segs=20, bev=0, parent=P), d)
    band = orient(K.cyl("crown_band", 4.75, 1.0, red, loc=tuple(cr), segs=20, bev=0, parent=P), d)
    dish = dish_mesh("dish", 3.6, 1.2, 0.12, bone, rings=4, segs=16)
    K.place(dish, tuple(cr + u * 6.2 + v * 1.5), (60, 10, 30), P)
    rod(P, "dish_arm", cr + u * 4.3, cr + u * 5.9 + v * 1.2, 0.25, gra)
    dish2 = dish_mesh("dish2", 3.0, 1.0, 0.12, bone, rings=4, segs=16)
    K.place(dish2, (B.x - 9, B.y - 8, 1.1), (-160, 18, 40), P)
    lamp = cr + v * 4.9 + d * 1.0
    K.cyl("beacon_cage", 0.9, 1.6, gra, loc=tuple(lamp), segs=8, bev=0.03, parent=P).rotation_euler = band.rotation_euler
    bl = K.cyl("beacon", 0.7, 1.4, glow(CYAN, 10), loc=tuple(lamp + v * 0.6), segs=10, bev=0.05, parent=P)
    orient(bl, v)
    K.point("beacon_light", tuple(lamp + v * 2.0), CYAN, 3000, 0.4).parent = P
    rod(P, "whip", cr + d * 3.6, cr + d * 3.6 + Vector((-3, 2, 11)), 0.18, gra)
    # the ground torn up where the crown came down
    mound(P, "crater", 9.0, 7.0, 2.4, earth(), rnd, loc=(B.x + 2.5, B.y - 0.5), segs=14, rings=4)
    # guy anchors: two cables still taut, one snapped and lying slack in the grass
    for k, (ax, ay) in enumerate(((-20, -16), (-18, 20), (24, 26))):
        K.box("anchor", (2.4, 2.4, 1.0), conc, loc=(ax, ay, 0.3), rot=(0, 0, rnd.uniform(0, 90)), bev=0.08, parent=P)
        hi = Vector((math.copysign(w(30) / 2, ax), math.copysign(w(30) / 2, ay), 30))
        if k < 2:
            rod(P, "guy", (ax, ay, 0.9), hi, 0.07, gra, segs=6)
        else:
            tube(P, "guy_slack", [(ax, ay, 0.9), (ax - 6, ay - 8, 0.15), (ax - 14, ay - 16, 0.15), (ax - 22, ay - 20, 0.2)], 0.07, gra, sides=5, sub=3)
    # equipment cabin and cable tray
    FR.framed_box(P, "cabin", (-12, -7, 2.0), (8.0, 5.0, 3.5), bone, steel, fw=0.3, screws=None)
    K.box("cabin_door", (1.25, 0.1, 2.4), gra, loc=(-10.0, -9.53, 1.5), bev=0.02, parent=P)
    FR.vent(P, (-13.5, -9.56, 2.2), 2.0, 1.2, gra, M(GUNMETAL, metal=0.4))
    stencil(P, "RS-7", (-13.0, -9.57, 3.2), 0.7, gra)
    K.box("cabin_pad", (9.5, 6.5, 0.4), conc, loc=(-12, -7, 0.1), bev=0.05, parent=P)
    block(P, "tray", (-8, -5.5, 0.9), (-3.5, -3.5, 2.0), (0.8, 0.3), (0.8, 0.3), gra, bev=0)
    # bits of the crown and lattice that came off in the fall
    for k in range(5):
        p = A.lerp(B, rnd.uniform(0.35, 0.9)) + Vector((rnd.uniform(-6, 6), rnd.uniform(-10, -4), 0))
        p.z = 0.3
        beam(P, "debris", p, p + Vector((rnd.uniform(-4, 4), rnd.uniform(-4, 4), rnd.uniform(0.2, 1.2))), 0.6, red if k % 2 else bone)
    # the bank of earth the crown ploughed up over its far end: the way up onto the drum and the catwalk
    heap(P, "bank", 14.0, 24.0, 9.0, earth(), rnd, loc=(B.x + 10.0, B.y - 0.6))
    return quiet(P, "guy", "guy_slack", "whip", "dish_arm", "beacon")


def _shell(P, name, mat, inner, R0, Rw, zw, z0, rim, segs=32, rings=8, t=0.7):
    """A hyperboloid cooling-tower shell from z0 up to rim(j) on column j (a jagged rim makes a broken tower)."""
    c = (zw - z0) / math.sqrt((R0 / Rw) ** 2 - 1)
    r = lambda z: Rw * math.sqrt(1 + ((z - zw) / c) ** 2)
    out = bmesh.new()
    O, I = [], []
    for j in range(segs):
        a = 2 * math.pi * j / segs
        top = rim(j)
        co, ci = [], []
        for i in range(rings + 1):
            z = z0 + (top - z0) * i / rings
            rr = r(z)
            co.append(out.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
            ci.append(out.verts.new(((rr - t) * math.cos(a), (rr - t) * math.sin(a), z)))
        O.append(co)
        I.append(ci)
    for j in range(segs):
        k = (j + 1) % segs
        for i in range(rings):
            out.faces.new((O[j][i], O[k][i], O[k][i + 1], O[j][i + 1]))
            out.faces.new((I[j][i], I[j][i + 1], I[k][i + 1], I[k][i]))
        out.faces.new((O[j][-1], O[k][-1], I[k][-1], I[j][-1]))
        out.faces.new((O[j][0], I[j][0], I[k][0], O[k][0]))
    bmesh.ops.recalc_face_normals(out, faces=out.faces)
    ob = K.from_bm(name, out, mat, smooth=True)
    ob.data.set_sharp_from_angle(angle=math.radians(40))
    # paint the inside darker: a second material on the inner faces
    ob.data.materials.append(inner)
    for p in ob.data.polygons:
        x, y, _ = p.center
        rr = math.hypot(x, y)
        n = p.normal
        if n.x * x + n.y * y < -0.2 * rr:
            p.material_index = 1
    K.place(ob, parent=P)
    return r


def _tower(P, name, loc, R0, Rw, zw, H, rim, rnd, conc, inner, steel, mus, red, broken=False):
    G = empty(P, name, loc)
    z0 = 8.0
    r = _shell(G, f"{name}_shell", conc, inner, R0, Rw, zw, z0, rim)
    n = 24
    for k in range(n):        # the raking columns that carry the shell, in a diamond lattice
        a = 2 * math.pi * k / n
        da = math.pi / n
        g = Vector((R0 * math.cos(a), R0 * math.sin(a), 0.5))
        for s in (-1, 1):
            beam(G, "col", g, Vector(((R0 - 0.3) * math.cos(a + s * da), (R0 - 0.3) * math.sin(a + s * da), z0 + 0.4)), 0.9, conc)
    # the ring footing and, in a whole tower, the pond inside it
    for q in range(16):
        a0 = 360 * q / 16
        a = math.radians(a0 + 11.25)
        K.box("ring", (2 * math.pi * R0 / 16 + 0.3, 2.2, 1.4), conc, loc=(R0 * math.cos(a), R0 * math.sin(a), 0.3), rot=(0, 0, a0 + 11.25 + 90), bev=0, parent=G)
    if not broken:
        pond = K.disc(f"{name}_pond", R0 - 1.0, K.mat("pond", WATER, rough=0.15, metal=0.1), (0, 0), z=0.45, segs=24)
        pond.parent = G
    # a steel band and a mustard stripe below the rim, and the dead warning lamps
    for (z1, z2, m) in ((H - 4.2, H - 2.2, steel), (H - 1.6, H - 1.0, mus)):
        bm = bmesh.new()
        segs = 32
        ring = []
        for j in range(segs):
            a = 2 * math.pi * j / segs
            if rim(j) < z2 + 0.5 or rim((j + 1) % segs) < z2 + 0.5:
                ring.append(None)
                continue
            ring.append(j)
        for j in range(segs):
            k = (j + 1) % segs
            if ring[j] is None or ring[k] is None:
                continue
            a, b = 2 * math.pi * j / segs, 2 * math.pi * k / segs
            vs = [bm.verts.new(((r(z) + 0.12) * math.cos(aa), (r(z) + 0.12) * math.sin(aa), z)) for aa, z in ((a, z1), (b, z1), (b, z2), (a, z2))]
            bm.faces.new(vs)
        if bm.faces:
            K.place(K.from_bm(f"{name}_band", bm, m), parent=G)
        else:
            bm.free()
    for j in range(0, 32, 8):
        if rim(j) > H - 0.5:
            a = 2 * math.pi * j / 32
            K.box("warn_lamp", (0.6, 0.6, 0.7), red, loc=((r(H) - 0.3) * math.cos(a), (r(H) - 0.3) * math.sin(a), H + 0.3), bev=0.05, parent=G)
    return G, r


def cooling_towers(seed=102):
    """Twin colony cooling towers, 60 m and 56 m: bone concrete hyperboloids on raking columns, a steel band and a
    mustard stripe at the rim. The far one cracked open in the fall and half of it lies in a heap. Drive in between
    the columns, fly in at the top or through the broken side; a pump house and pipe bridge sit between them."""
    rnd = random.Random(seed)
    P = _root("cooling_towers")
    conc = K.mat("ct_conc", BONE, rough=0.9, wear=0.1, grime=0.5, cracks=0.08, moss=0.15, noise_scale=1.5)
    inner = K.mat("ct_inner", CONCRETE, rough=0.95, grime=0.9, cracks=0.1, noise_scale=1.5)
    steel, mus, red, gra = M(STEEL, metal=0.3, wear=0.15), M(MUSTARD, wear=0.2), M(SIGNAL, wear=0.2), M(GRAPHITE, metal=0.4, wear=0.2)
    HA, HB = 60.0, 56.0
    _tower(P, "tower_a", (-28, 6, 0), 23.0, 14.5, 44, HA, lambda j: HA, rnd, conc, inner, steel, mus, red)
    jag = [rnd.uniform(-2.5, 2.5) for _ in range(32)]

    def rim_b(j):
        a = 2 * math.pi * j / 32
        drop = max(0.0, math.cos(a - math.radians(-20))) ** 1.5 * 34
        return HB - drop + (jag[j] if drop > 1 else 0)
    _tower(P, "tower_b", (30, -2, 0), 21.5, 13.5, 41, HB, rim_b, rnd, conc, inner, steel, mus, red, broken=True)
    # the collapsed half: shell slabs and rubble heaped against and inside the open side
    for k in range(11):
        a = math.radians(-20 + rnd.uniform(-60, 60))
        rr = rnd.uniform(8, 32)
        p = (30 + rr * math.cos(a), -2 + rr * math.sin(a), 0)
        sz = rnd.uniform(4, 9)
        crag(P, "shell_slab", (sz, sz * rnd.uniform(0.3, 0.6), rnd.uniform(1.2, 3.5)), p, rnd, mat=conc,
             rot=(rnd.uniform(-25, 25), rnd.uniform(-35, 35), rnd.uniform(0, 360)), bev=0)
    mound(P, "rubble_heap", 16, 12, 7, inner, rnd, loc=(30 + 14 * math.cos(math.radians(-20)), -2 + 14 * math.sin(math.radians(-20))), segs=14, rings=4)
    # pump house between the towers, with the pipe bridge to each
    FR.framed_box(P, "pump", (0, -16, 4.0), (14, 8, 8), K.mat("pump_bone", BONE, rough=0.6, wear=0.2, grime=0.4), steel, fw=0.35)
    K.box("pump_roof", (14.6, 8.6, 0.4), gra, loc=(0, -16, 8.2), bev=0.05, parent=P)
    K.box("pump_door", (6.0, 0.12, 5.0), M(STEEL, metal=0.3, wear=0.25), loc=(-2.5, -20.05, 2.5), bev=0.03, parent=P)
    for k in range(5):
        K.box("door_rib", (6.0, 0.18, 0.12), gra, loc=(-2.5, -20.12, 0.8 + k * 1.0), bev=0, parent=P)
    stencil(P, "CT-2", (3.5, -20.1, 5.6), 1.4, gra)
    FR.vent(P, (3.5, -20.08, 2.8), 2.6, 2.0, gra, M(GUNMETAL, metal=0.4))
    for sx, tx, ty in ((-1, -28, 6), (1, 30, -2)):
        x0 = sx * 7.4
        x1 = tx - sx * 19
        rod(P, "pipe", (x0, -15, 5.0), (x1, ty - 3 * sx, 5.0), 1.1, steel, segs=14)
        for f in (0.35, 0.7):
            px = x0 + (x1 - x0) * f
            py = -15 + (ty - 3 * sx + 15) * f
            K.box("pipe_stand", (0.8, 2.8, 4.0), conc, loc=(px, py, 2.0), rot=(0, 0, math.degrees(math.atan2(ty - 3 * sx + 15, x1 - x0))), bev=0.05, parent=P)
    # the service stair up tower A's front, from the ground to the rim: switchback flights in two lanes with landings
    # across both, bracketed to the shell, each pair of flights set out from the shell as it narrows and flares
    _service_stair(P, (-28.0, 6.0), math.radians(-70), HA, lambda z: 24.2 if z < 8.5 else r_tower_a(z), steel, gra, mus)
    K.box("ground_pad", (20, 14, 0.3), _concrete(), loc=(0, -17, 0.0), bev=0.05, parent=P)
    return P


def r_tower_a(z):
    """Tower A's shell radius at height z (above its columns)."""
    return 14.5 * math.sqrt(1 + ((z - 44) / ((44 - 8) / math.sqrt((23 / 14.5) ** 2 - 1))) ** 2)


def _service_stair(P, c, ang, top, r_of, steel, gra, mus, rise=3.0, run=4.4, width=1.3):
    """Switchback steel flights up the outside of a round shell centred at c, on the side facing ang, to height top:
    r_of(z) is how far out the shell is at z. Each flight rises rise metres over run (34 degrees, walked as a ramp)."""
    cx, cy = c
    rad = Vector((math.cos(ang), math.sin(ang), 0))
    tng = Vector((-math.sin(ang), math.cos(ang), 0))
    n = round(top / rise)
    pairs = (n + 1) // 2
    # a pair is never set further out than the one below it (a flight would run into the one under it), so where the
    # shell flares the stair stands off it and the brackets reach
    zs_ = [0.5 * k for k in range(int(top * 2) + 1)]
    R = [max(r_of(z) for z in zs_ if z >= 2 * rise * p - 1) + 0.6 + width / 2 for p in range(pairs)]
    at = lambda r, t, z: Vector((cx, cy, 0)) + rad * r + tng * t + Vector((0, 0, z))
    half = run / 2
    for i in range(n):
        p, outer = i // 2, i % 2
        r = R[p] + outer * (width + 0.1)
        t0, t1 = (-half, half) if not outer else (half, -half)
        z0, z1 = i * rise, (i + 1) * rise
        stair(P, "service_stair", at(r, t0, z0), at(r, t1, z1), width, steel, rise=0.3, base=0.3)
        side = width / 2 - 0.05 if outer else -(width / 2 - 0.05)
        rod(P, "stair_rail", at(r + side, t0, z0 + 1.0), at(r + side, t1, z1 + 1.0), 0.05, mus, segs=6)
        # the landing at the top of the flight, across both lanes (and the next pair's, set further in or out)
        rn = R[min(p + outer, pairs - 1)]
        r_in = min(R[p], rn) - width / 2 - (1.2 if i == n - 1 else 0.0)
        r_out = max(R[p], rn) + width * 1.5 + 0.1
        tl = t1 + (0.7 if t1 > 0 else -0.7)
        K.box("stair_landing", (r_out - r_in, 1.4, 0.3), steel, loc=tuple(at((r_in + r_out) / 2, tl, z1 - 0.15)),
              rot=(0, 0, math.degrees(ang)), bev=0, parent=P)
        if z1 < 9:
            beam(P, "stair_post", at(r_out - 0.15, tl, 0), at(r_out - 0.15, tl, z1 - 0.3), 0.25, gra)
        else:
            beam(P, "stair_bracket", at(r_in + 0.1, tl, z1 - 0.35), at(r_of(z1) - 0.2, tl, z1 - 0.35), 0.25, gra)


def aqueduct(seed=103):
    """Aqueduct AQ-1, unfinished: a colony water channel on 26 m piers striding off from its intake tower, stopping
    in mid-air with the launching gantry still out over the gap and the last piers only rebar. Climb the intake
    tower's stair to walk or drive the deck, and jump off the end."""
    rnd = random.Random(seed)
    P = _root("aqueduct")
    conc = K.mat("aq_conc", BONE, rough=0.9, wear=0.1, grime=0.5, cracks=0.2, moss=0.15, noise_scale=2.0)
    raw = K.mat("aq_raw", CONCRETE, rough=0.95, grime=0.5, cracks=0.15, noise_scale=2.0)
    steel, mus, gra, red = M(STEEL, metal=0.4, wear=0.2), M(MUSTARD, wear=0.2), M(GRAPHITE, metal=0.4, wear=0.2), M(SIGNAL, wear=0.2)
    rebar = K.mat("rebar", RUST, rough=0.8, metal=0.5)
    DZ = 26.0                 # deck underside
    xs = [-42 + 14 * k for k in range(8)]
    for k, x in enumerate(xs):
        K.box("footing", (8, 10, 1.4), raw, loc=(x, 0, 0.45), bev=0.08, parent=P)
        if k <= 5:
            tapered("pier", (5.4, 7.4), (3.6, 5.4), DZ - 2.5 - 1.15, conc, loc=(x, 0, 1.15), bev=0.08, parent=P)
            K.box("cap", (4.8, 8.6, 2.5), conc, loc=(x, 0, DZ - 1.25), bev=0.08, parent=P)
            K.box("cap_band", (5.1, 8.9, 0.6), steel, loc=(x, 0, DZ - 2.0), bev=0.03, parent=P)
            K.box("mid_band", (4.6, 6.5, 0.5), steel, loc=(x, 0, 12.0), bev=0.03, parent=P)
        elif k == 6:          # half poured, rebar standing out of it, a scaffold round it
            tapered("pier", (5.4, 7.4), (4.6, 6.6), 12.0, conc, loc=(x, 0, 1.15), bev=0.08, parent=P)
            for i in range(10):
                px = x + rnd.choice((-1, 1)) * rnd.uniform(0.8, 2.0)
                py = rnd.uniform(-2.8, 2.8)
                rod(P, "rebar", (px, py, 12.5), (px + rnd.uniform(-0.3, 0.3), py + rnd.uniform(-0.3, 0.3), 12.5 + rnd.uniform(3, 6)), 0.08, rebar, segs=4)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    rod(P, "scaff", (x + sx * 4, sy * 5, 0.5), (x + sx * 4, sy * 5, 17), 0.12, mus, segs=5)
                for z in (6.0, 11.5, 17.0):
                    rod(P, "scaff_h", (x + sx * 4, -5, z), (x + sx * 4, 5, z), 0.1, mus, segs=5)
                    rod(P, "scaff_h", (x - 4, sx * 5, z), (x + 4, sx * 5, z), 0.1, mus, segs=5)
            K.box("scaff_deck", (8.2, 10.2, 0.2), wood(1), loc=(x, 0, 11.6), bev=0.02, parent=P)
        else:                 # footing and a rebar cage only
            for i in range(14):
                a = 2 * math.pi * i / 14
                px, py = x + 2.0 * math.cos(a), 2.9 * math.sin(a)
                rod(P, "rebar", (px, py, 1.1), (px, py, rnd.uniform(5, 8)), 0.08, rebar, segs=4)
    # the deck: a precast trough channel, complete out to the gap, its last segment cantilevered
    x0, x1 = -53.0, 1.0
    GAP = -50.8
    n = 8
    seg = (x1 - x0) / n
    for i in range(n):
        a, b = x0 + i * seg + 0.06, x0 + (i + 1) * seg - 0.06
        K.box("deck", (b - a, 7.2, 1.2), conc, loc=((a + b) / 2, 0, DZ + 0.6), bev=0.06, parent=P)
        for s in (-1, 1):
            a_ = GAP if i == 0 and s < 0 else a       # the gap in the south wall where the gangway from the stair comes in
            K.box("wall", (b - a_, 0.6, 1.8), conc, loc=((a_ + b) / 2, s * 3.3, DZ + 2.1), bev=0.06, parent=P)
    for k in range(4):
        cx = (xs[k] + xs[k + 1]) / 2
        arch = [(-7.0, 0.0), (-7.0, -9.0)]
        arch += [(5.0 * math.cos(math.pi * (1 - i / 10)), -9.0 + 6.5 * math.sin(math.pi * i / 10)) for i in range(11)]
        arch += [(7.0, -9.0), (7.0, 0.0)]
        profile_slab("spandrel", arch, 5.4, conc, loc=(cx, 0, DZ), parent=P, bev=0)
    K.box("deck_lip", (x1 - GAP, 0.3, 0.35), steel, loc=((GAP + x1) / 2, -3.66, DZ + 2.85), bev=0, parent=P)
    # hazard barrier where the deck ends
    for k in range(5):
        K.box("barrier", (0.6, 1.2, 1.1), red if k % 2 else M(BONE, wear=0.3), loc=(x1 - 0.5, -2.4 + k * 1.2, DZ + 1.75), bev=0.04, parent=P)
    # the launching gantry: twin box girders on legs, out over the gap to the next pier, a segment on its hoist
    gx0, gx1, gz = -14.0, 30.0, DZ + 9.5
    for s in (-1, 1):
        K.box("girder", (gx1 - gx0, 1.4, 2.4), steel, loc=((gx0 + gx1) / 2, s * 3.0, gz), bev=0.05, parent=P)
        for x in (gx0 + 1.5, -4.5):
            K.box("gleg", (1.2, 1.0, gz - DZ - 2.6), mus, loc=(x, s * 3.0, (gz + DZ + 1.2) / 2 - 0.4), bev=0.04, parent=P)
        K.box("gleg_far", (1.2, 1.0, gz - DZ - 1.2), mus, loc=(gx1 - 2.0, s * 3.0, (gz + DZ) / 2), bev=0.04, parent=P)
    for x in (gx0 + 1.5, -4.5, gx1 - 2.0):
        K.box("gcross", (1.0, 7.2, 1.0), mus, loc=(x, 0, gz - 1.8), bev=0.03, parent=P)
    K.box("trolley", (3.6, 7.6, 2.0), mus, loc=(10.0, 0, gz + 1.9), bev=0.05, parent=P)
    K.box("winch", (2.0, 2.4, 1.6), gra, loc=(10.0, 0, gz + 3.6), bev=0.04, parent=P)
    for s in (-1, 1):
        rod(P, "hoist", (10 + s * 1.2, -1.5, gz - 1.0), (10 + s * 1.2, -1.5, DZ + 3.2), 0.06, gra, segs=4)
        rod(P, "hoist", (10 + s * 1.2, 1.5, gz - 1.0), (10 + s * 1.2, 1.5, DZ + 3.2), 0.06, gra, segs=4)
    K.box("hung_seg", (6.8, 7.2, 1.2), conc, loc=(10.0, 0, DZ + 0.2), rot=(0, 2.0, 0), bev=0.06, parent=P)
    for s in (-1, 1):
        K.box("hung_wall", (6.8, 0.6, 1.8), conc, loc=(10.0, s * 3.3, DZ + 1.7), rot=(0, 2.0, 0), bev=0.06, parent=P)
    K.box("spreader", (6.0, 3.6, 0.5), gra, loc=(10.0, -1.5 + 1.5, DZ + 3.3), bev=0.02, parent=P)
    stencil(P, "AQ-1", (8.0, -3.75, gz), 1.3, gra)
    # the intake tower at the west end, its capsule header tank and a stair up the outside
    tx = -60.0
    FR.framed_box(P, "intake", (tx, 0, 15.0), (12.0, 12.0, 30.0), K.mat("aq_panel", BONE, rough=0.6, wear=0.2, grime=0.4), steel, fw=0.6)
    for z in (10.0, 20.0):
        K.box("floor_rail", (12.4, 12.4, 0.5), steel, loc=(tx, 0, z), bev=0.03, parent=P)
    K.slab("tank", 10.0, 8.0, 7.0, 3.9, conc, loc=(tx, 0, 33.5), bev=0.15, parent=P)
    for z in (31.5, 35.5):
        K.slab("tank_rib", 10.3, 8.3, 0.5, 4.0, steel, loc=(tx, 0, z), bev=0.04, parent=P)
    K.box("roll_door", (6.0, 0.15, 5.8), M(STEEL, metal=0.3, wear=0.25), loc=(tx, -6.05, 2.9), bev=0.03, parent=P)
    for k in range(6):
        K.box("door_rib", (6.0, 0.2, 0.12), gra, loc=(tx, -6.12, 0.6 + k * 1.0), bev=0, parent=P)
    for z in range(7, 30, 6):
        K.box("window", (5.0, 0.12, 1.2), K.mat("aq_glass", GLASS_D, rough=0.2, metal=0.3), loc=(tx, -6.04, z + 0.5), bev=0.02, parent=P)
    stencil(P, "AQ-1", (tx, -6.1, 27.5), 2.0, gra)
    K.box("intake_pad", (16.0, 16.0, 0.4), raw, loc=(tx, 0, 0.1), bev=0.05, parent=P)
    for i in range(4):                 # the stair: four flights up the tower's south face to the deck, in two lanes
        z0, z1 = i * 6.75, (i + 1) * 6.75
        sx = -1 if i % 2 == 0 else 1
        y = -7.3 if i % 2 == 0 else -9.0
        a = Vector((tx - sx * 5.2, y, z0))
        b = Vector((tx + sx * 5.2, y, z1))
        stair(P, "stair", a, b, 1.6, steel, rise=0.3, base=0.35)
        if i < 3:
            K.box("landing", (2.0, 3.3, 0.35), steel, loc=(tx + sx * 6.2, -8.15, z1 - 0.18), bev=0, parent=P)
    # the top landing and the gangway across to the deck, through the gap in its wall
    K.box("landing", (3.4, 3.3, 0.35), steel, loc=(-53.1, -8.15, DZ + 1.0 - 0.18), bev=0, parent=P)
    K.box("gangway", (2.0, 3.9, 0.3), steel, loc=(-51.9, -4.9, DZ + 1.05), bev=0, parent=P)
    for s in (-1, 1):
        beam(P, "stair_post", (tx + s * 7.3, -10.0, 0), (tx + s * 7.3, -10.0, 27.5), 0.3, gra)
    # spare segments stacked in the yard past the end, and a spoil heap
    for k in range(3):
        K.box("spare", (6.8, 7.2, 1.2), raw, loc=(44.0 + rnd.uniform(-0.3, 0.3), -14.0, 0.6 + k * 1.25), rot=(0, 0, rnd.uniform(-3, 3)), bev=0.05, parent=P)
    mound(P, "spoil", 9, 6, 3.5, earth(), rnd, loc=(52, 12), segs=12, rings=4)
    return quiet(P, "rebar", "scaff", "scaff_h", "hoist")


# ================================================================== Old Stone
def kneeling_giant(seed=104):
    """The Kneeling Giant: a hooded figure of dressed stone, 49 m to the hood, down on one knee on its stepped plinth,
    cloak pooled behind it, head bowed over a great sword planted point-down in the stone; a probe crystal grows from the pommel between its hands. Climb the
    plinth steps to the sword; land on the shoulders or the bowed hood."""
    rnd = random.Random(seed)
    P = _root("kneeling_giant")
    G = empty(P, "giant_scale")
    G.scale = (1.15, 1.15, 1.15)
    s, ds = rock_mat(), rock_dark()
    z0 = 2.7
    # plinth: two courses of great slabs and a flight of steps
    for k, x in enumerate((-11.5, 0.0, 11.5)):
        K.box("plinth1", (11.3, 36, 1.5), s, loc=(x, 0, 0.75), rot=(0, 0, rnd.uniform(-0.6, 0.6)), bev=0.15, parent=G)
    for k, x in enumerate((-7.0, 7.0)):
        K.box("plinth2", (13.8, 30, 1.2), s, loc=(x, 1.0, 2.1), rot=(0, 0, rnd.uniform(-0.5, 0.5)), bev=0.12, parent=G)
    stair(G, "steps", (0, -22.5, 0), (0, -14.0, z0), 12, s, rise=0.3, base=1.0)
    # legs: the right knee down, its shin lying back on the plinth; the left knee up, its foot planted forward
    block(G, "thigh_r", (5.5, 3, z0 + 13), (5.0, -1, z0 + 2.4), (6.6, 7.0), (5.2, 5.2), s, bev=0.25)
    block(G, "shin_r", (5.0, -1.5, z0 + 2.0), (5.0, 11.5, z0 + 1.7), (4.8, 4.2), (3.5, 3.2), s, bev=0.25)
    block(G, "foot_r", (5.0, 11.0, z0 + 1.4), (5.0, 15.5, z0 + 0.9), (3.6, 3.0), (3.8, 1.8), s, bev=0.2)
    block(G, "knee_r", (5.0, -1.2, z0 + 0.0), (5.0, -1.4, z0 + 4.2), (5.4, 5.0), (5.0, 4.6), ds, bev=0.25)
    block(G, "thigh_l", (-5.5, 3, z0 + 13), (-6.0, -9.0, z0 + 14.2), (6.6, 7.0), (5.4, 5.4), s, bev=0.25)
    block(G, "knee_l", (-6.0, -8.6, z0 + 12.0), (-6.0, -9.6, z0 + 16.4), (5.6, 5.0), (5.0, 4.4), ds, bev=0.25)
    block(G, "shin_l", (-6.0, -9.6, z0 + 13.5), (-6.0, -10.6, z0 + 1.5), (5.0, 4.8), (3.8, 3.6), s, bev=0.25)
    block(G, "foot_l", (-6.0, -9.5, z0 + 1.1), (-6.0, -16.5, z0 + 0.8), (4.2, 2.6), (4.6, 1.6), s, bev=0.2)
    # body: hips, belt, waist, chest, pauldrons
    block(G, "hips", (0, 3.0, z0 + 10.0), (0, 2.6, z0 + 16.0), (14.0, 8.4), (13.0, 7.8), s, bev=0.25)
    block(G, "belt", (0, 2.6, z0 + 15.4), (0, 2.5, z0 + 17.2), (14.2, 8.6), (13.8, 8.4), ds, bev=0.15)
    block(G, "waist", (0, 2.5, z0 + 17.0), (0, 1.6, z0 + 22.0), (11.6, 7.2), (13.2, 7.8), s, bev=0.25)
    block(G, "chest", (0, 1.6, z0 + 21.6), (0, 0.6, z0 + 29.2), (14.4, 8.6), (18.4, 8.2), s, bev=0.3)
    block(G, "breast", (0, -2.2, z0 + 22.5), (0, -2.6, z0 + 28.0), (9.0, 1.4), (11.0, 1.2), ds, bev=0.15)
    for sx in (-1, 1):
        block(G, f"pauldron{sx}", (sx * 9.2, 0.8, z0 + 25.0), (sx * 9.6, 0.6, z0 + 30.6), (6.6, 7.6), (5.8, 6.6), ds, bev=0.25)
    # head bowed over the sword: neck, a helm with a crest, a brow over a blank face
    block(G, "neck", (0, 0.5, z0 + 28.8), (0, -1.0, z0 + 31.4), (5.2, 5.0), (4.8, 4.8), s, bev=0.2)
    H = empty(G, "head_bow", (0, -0.6, z0 + 30.4), (30, 0, 0))
    pts = []
    for i in range(22):
        zz = 1 - 2 * (i + 0.5) / 22
        ang = i * 2.39996
        rr = math.sqrt(1 - zz * zz)
        p = Vector((rr * math.cos(ang) * 3.0, rr * math.sin(ang) * 3.3, 3.6 + zz * 3.9)) * rnd.uniform(0.95, 1.04)
        p.y = max(p.y, -2.7)          # the face: a flat mask
        pts.append(tuple(p))
    hull(H, "head", pts, s, bev=0.2)
    block(H, "brow", (-2.2, -2.75, 4.6), (2.2, -2.75, 4.6), (0.9, 0.8), (0.9, 0.8), ds, bev=0.1)
    block(H, "nose", (0, -2.9, 4.4), (0, -3.1, 2.6), (0.8, 0.7), (1.0, 0.9), s, bev=0.1)
    hood = [(sx * 4.1, -1.9, 3.4) for sx in (-1, 1)] + [(sx * 3.3, -1.9, 6.9) for sx in (-1, 1)] + [(0, -2.3, 8.6)]
    hood += [(sx * 3.8, -1.7, 0.6) for sx in (-1, 1)] + [(sx * 5.4, 2.4, -1.6) for sx in (-1, 1)] + [(0, 4.6, -1.8)]
    for i in range(14):
        ang = 2 * math.pi * i / 14
        for el in (0.3, 0.9):
            hood.append((math.cos(ang) * 4.4 * math.cos(el), max(-0.6, math.sin(ang)) * 4.6 * math.cos(el) + 0.4, 4.0 + math.sin(el) * 4.6))
    hull(H, "hood", hood, ds, bev=0.2)
    # the cloak, from the shoulders down the back and pooled over the kneeling leg
    cl = [(sx * 9.8, 1.4, z0 + 30.2) for sx in (-1, 1)] + [(sx * 8.4, 4.8, z0 + 29.8) for sx in (-1, 1)] + [(0, 5.4, z0 + 30.4)]
    cl += [(sx * 12.4, 8.6, z0 + 13.0) for sx in (-1, 1)] + [(0, 11.4, z0 + 13.0)]
    cl += [(sx * 13.4, 2.6, z0 + 0.2) for sx in (-1, 1)] + [(sx * 13.8, 12.5, z0 + 0.2) for sx in (-1, 1)]
    cl += [(sx * 9.6, 19.5, z0 + 0.2) for sx in (-1, 1)] + [(0, 21.0, z0 + 0.2)]
    hull(G, "cloak", cl, ds, bev=0.25)
    for x in (-6.0, 0.0, 6.0):
        block(G, "fold", (x, 5.4, z0 + 28.6), (x * 1.35, 12.2 if x else 11.8, z0 + 13.0), (1.8, 1.4), (2.2, 1.4), ds, bev=0.12)
        block(G, "fold", (x * 1.35, 12.0 if x else 11.6, z0 + 13.2), (x * 1.5, 20.4 - abs(x) * 0.25, z0 + 0.4), (2.2, 1.4), (2.6, 1.4), ds, bev=0.12)
    # arms down to the hands folded on the pommel
    for sx in (-1, 1):
        block(G, f"upper{sx}", (sx * 9.4, 0.4, z0 + 27.4), (sx * 9.0, -5.6, z0 + 21.4), (4.4, 4.4), (3.8, 3.8), s, bev=0.25)
        block(G, f"elbow{sx}", (sx * 9.0, -5.0, z0 + 21.8), (sx * 8.9, -6.2, z0 + 21.0), (4.2, 4.2), (4.0, 4.0), ds, bev=0.2)
        block(G, f"fore{sx}", (sx * 8.9, -5.9, z0 + 21.2), (sx * 2.6, -11.6, z0 + 20.2), (3.8, 3.8), (3.2, 3.2), s, bev=0.25)
        block(G, f"hand{sx}", (sx * 2.9, -11.8, z0 + 20.0), (sx * 0.9, -12.4, z0 + 20.6), (3.6, 4.0), (3.4, 3.8), s, bev=0.2)
    # the sword: pommel crystal, grip, guard and a blade driven into the plinth through cracked flags
    Y = -12.4
    block(G, "grip", (0, Y, z0 + 22.4), (0, Y, z0 + 17.4), (1.6, 1.6), (1.6, 1.6), ds, bev=0.1)
    block(G, "guard", (-5.4, Y, z0 + 17.0), (5.4, Y, z0 + 17.0), (1.8, 1.4), (1.8, 1.4), ds, bev=0.15)
    for sx in (-1, 1):
        block(G, f"quillon{sx}", (sx * 5.2, Y, z0 + 16.6), (sx * 6.2, Y, z0 + 18.2), (1.5, 1.3), (1.0, 0.9), ds, bev=0.12)
    block(G, "blade", (0, Y, z0 + 16.5), (0, Y, z0 - 1.5), (3.4, 1.0), (2.4, 0.8), s, bev=0.12)
    block(G, "fuller", (0, Y - 0.45, z0 + 15.5), (0, Y - 0.45, z0 + 1.0), (0.6, 0.2), (0.5, 0.2), ds, bev=0.03)
    block(G, "pommel", (0, Y, z0 + 22.2), (0, Y, z0 + 23.6), (2.4, 2.4), (1.6, 1.6), ds, bev=0.12)
    for k in range(5):
        crystal(G, f"pommel_crystal{k}", (rnd.uniform(-0.4, 0.4), Y + rnd.uniform(-0.4, 0.4), z0 + 23.2), rnd.uniform(0, 360), rnd.uniform(55, 85),
                rnd.uniform(1.6, 3.2), rnd.uniform(0.3, 0.5), cyan(3))
    K.point("crystal_light", (0, Y - 1.5, z0 + 24.5), CYAN, 2500, 0.8).parent = G
    for k in range(6):
        a = rnd.uniform(0, 6.28)
        K.box("cracked_flag", (rnd.uniform(1.5, 2.6), rnd.uniform(1.5, 2.6), 0.5), ds, loc=(math.cos(a) * 2.6, Y + math.sin(a) * 2.2, z0 + 0.05),
              rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 90)), bev=0.06, parent=G)
    # time: moss on the shoulders, head and thigh, a pauldron chunk fallen to the plinth, crystals in the cracks
    for loc, r in (((8.5, 0.6, z0 + 30.6), 2.4), ((-8.8, 0.6, z0 + 30.6), 2.0), ((0, -4.4, z0 + 36.4), 1.6),
                   ((-6.0, -4.0, z0 + 17.4), 2.2)):
        moss(G, "moss", loc, r, flat=0.2)
    crag(G, "fallen_chunk", (4.0, 3.2, 2.6), (13.5, -9.0, z0), rnd, mat=ds, rot=(14, -10, 30), bev=0.15)
    crag(G, "fallen_chunk2", (2.6, 2.2, 1.6), (15.5, -5.0, 0), rnd, mat=s, rot=(8, 20, 70), bev=0.12)
    for (cx, cy, cz) in ((-14.0, 10.0, z0), (16.5, 12.0, 0.0)):
        for k in range(4):
            crystal(G, "crystal", (cx + rnd.uniform(-0.8, 0.8), cy + rnd.uniform(-0.8, 0.8), cz), rnd.uniform(0, 360), rnd.uniform(50, 80),
                    rnd.uniform(1.2, 2.6), rnd.uniform(0.25, 0.4), cyan(1.5))
    for k in range(10):
        a = rnd.uniform(0, 6.28)
        boulder(G, "rubble", (rnd.uniform(1.0, 2.6), rnd.uniform(1.0, 2.2), rnd.uniform(0.6, 1.4)), (math.cos(a) * rnd.uniform(19, 23), math.sin(a) * rnd.uniform(19, 23), 0), rnd)
    for ob in G.children:      # nothing in the way of the steps
        if ob.name.startswith("rubble") and abs(ob.location.x) < 9 and ob.location.y < -12:
            K.no_collide(ob)
    return quiet(P, "pommel_crystal*", "crystal", "moss", "cracked_flag")


def sky_arch(seed=105):
    """The Sky Arch: a natural rock arch 115 m long, its crest 49 m up, a long back rising from the west to a steep
    east leg. The old people cut a stair up its back to a gateway at the crest, where probe crystals glow. Fly through
    the opening (about 45 m wide and 35 m high), climb the stair, look out from the gate."""
    rnd = random.Random(seed)
    P = _root("sky_arch")
    s, ds = rock_mat(), rock_dark()
    ctrl = [(-60, 0, -8), (-47, 0, 5), (-32, 0, 18), (-17, 0, 30), (-3, 0, 40), (10, 0, 44), (21, 0, 39), (29, 0, 28),
            (33, 0, 14), (35, 0, 2), (36, 0, -6)]
    th = [16, 18, 16, 13, 11, 10, 10, 12, 15, 19, 22]      # thickness across the arch
    dp = [20, 19, 17, 15, 13, 12, 12, 13, 16, 19, 22]      # depth through it (Y)
    pts = spline(ctrl, 4)
    N = len(pts) - 1
    nseg = 18
    for q in range(nseg):
        i0 = round(q * N / nseg)
        i1 = round((q + 1) * N / nseg)
        cloud = []
        for i, ext in ((i0, -1), (i1, 1)):
            c = pts[i]
            tng = (pts[min(i + 1, N)] - pts[max(i - 1, 0)]).normalized()
            c = c + tng * ext * 1.2
            nrm = Vector((-tng.z, 0, tng.x))
            f = i / N
            t, d = interp(th, f) / 2, interp(dp, f) / 2
            for a, b in ((1, 0.55), (0.55, 1), (-0.55, 1), (-1, 0.55), (-1, -0.55), (-0.55, -1), (0.55, -1), (1, -0.55)):
                j = rnd.uniform(0.86, 1.08)
                cloud.append(tuple(c + nrm * a * t * j + Vector((0, b * d * j, 0)) + tng * rnd.uniform(-0.8, 0.8)))
        hull(P, f"arch{q}", cloud, ds if q in (5, 12) else s, bev=0.25)
    # the stair up the back: flights of cut steps bedded in the rock from the foot of the talus to the gate, landings
    # between. Each flight is set just clear of the rock under it (found by casting down onto the arch), so the steps
    # are never buried and the way up is never blocked
    fl = [0.06, 0.13, 0.20, 0.27, 0.34, 0.41, 0.47]
    top = lambda f: pts[round(f * N)] + Vector((-(pts[min(round(f * N) + 1, N)] - pts[max(round(f * N) - 1, 0)]).normalized().z, 0,
                                                (pts[min(round(f * N) + 1, N)] - pts[max(round(f * N) - 1, 0)]).normalized().x)) * interp(th, f) / 2
    xs = [top(f).x for f in fl]
    zs = [top(f).z + 0.3 for f in fl]
    rock = _rock_under(P, (-4.0, 0.0))
    clear = lambda x0, z0, x1, z1: max(rock(x0 + (x1 - x0) * q / 12) - (z0 + (z1 - z0) * q / 12) for q in range(13)) + 0.15
    for _ in range(30):
        for k in range(len(fl)):
            e = clear(xs[k] - 2.1, zs[k], xs[k] + 1.3, zs[k])          # the landing (or the first flight's foot)
            if k + 1 < len(fl):
                e = max(e, clear(xs[k], zs[k], xs[k + 1] - 2.0, zs[k + 1]))
            if e > 0:
                zs[k] += e
                if k + 1 < len(fl):
                    zs[k + 1] += e
    # the first flight climbs from the ground at the foot of the talus to the first landing
    xf = xs[0] - 2.0 - zs[0] / math.tan(math.radians(36))
    while clear(xf, 0.0, xs[0] - 2.0, zs[0]) > 0.0 and xf > -90:
        xf -= 0.5
    stairs = [((xf, 0.0), (xs[0] - 2.0, zs[0]))] + [((xs[k], zs[k]), (xs[k + 1] - 2.0, zs[k + 1])) for k in range(len(fl) - 1)]
    for k, ((ax, az), (bx, bz)) in enumerate(stairs):
        stair(P, f"flight{k}", (ax, -2.0, az), (bx, -2.0, bz), 4.0, s, rise=0.3, base=3.5)
        K.box("landing", (3.4, 4.4, 1.6), s, loc=(bx + 1.6, -2.0, bz - 0.8), bev=0.1, parent=P)
        # the low wall that lines the stair
        block(P, "stair_wall", (ax - 0.6, -4.6, az - 0.1), (bx + 2.6, -4.6, bz - 0.1), (1.0, 1.0), (1.0, 1.0), ds, bev=0.08)
    # the gate at the crest: two pillars and a lintel framing the view, crystals at its foot
    g = pts[round(0.52 * N)] + Vector((0, 0, interp(th, 0.52) / 2))
    g.z = max(g.z, zs[-1] - 0.6)
    gx0 = xs[-1] + 1.2
    K.box("gate_step", (g.x - 4.5 - gx0 + 0.4, 4.4, 3.0), s, loc=((gx0 + g.x - 4.5) / 2, -2.0, zs[-1] - 1.5), bev=0.1, parent=P)
    K.box("gate_floor", (9.0, 10.0, 1.6), s, loc=(g.x, -1.0, g.z - 0.2), bev=0.12, parent=P)
    for sy in (-1, 1):
        block(P, "gate_pillar", (g.x, sy * 3.6 - 1.0, g.z), (g.x, sy * 3.6 - 1.0, g.z + 7.0), (1.8, 1.8), (1.5, 1.5), s, bev=0.12)
    K.box("gate_lintel", (2.2, 10.0, 1.4), ds, loc=(g.x, -1.0, g.z + 7.6), bev=0.12, parent=P)
    glyph(P, "gate_glyph", "eye", (g.x - 1.12, -1.0, g.z + 7.6), 1.1, s, rot=(0, 0, -90))
    for k in range(6):
        crystal(P, "crystal", (g.x + rnd.uniform(-2, 2), -1.0 + rnd.uniform(-1.6, 1.6), g.z + 0.5), rnd.uniform(0, 360), rnd.uniform(50, 85),
                rnd.uniform(1.4, 3.0), rnd.uniform(0.25, 0.45), cyan(3))
    K.point("gate_light", (g.x, -1.0, g.z + 3.0), CYAN, 2500, 0.6).parent = P
    # a carved ring on the east leg's face, crystals in a crack at its foot
    glyph(P, "leg_ring", "ring", (31.0, -10.6, 12.0), 6.0, ds)
    for k in range(5):
        crystal(P, "crystal", (28.0 + rnd.uniform(-1.5, 1.5), -11.5 + rnd.uniform(-1, 1), 0), rnd.uniform(0, 360), rnd.uniform(50, 80),
                rnd.uniform(1.4, 2.8), rnd.uniform(0.25, 0.45), cyan(1.5))
    # scree and boulders where both ends meet the ground
    for k in range(14):
        x = rnd.choice((rnd.uniform(-66, -44), rnd.uniform(24, 46)))
        y = rnd.uniform(-16, 16)
        sc = boulder(P, "scree", (rnd.uniform(2, 6), rnd.uniform(2, 5), rnd.uniform(1.2, 3.5)), (x, y, 0), rnd)
        if x < 0 and -8.0 < y < 4.0:       # the stair's foot runs through here
            K.no_collide(sc)
    for k, (x, y) in enumerate(((-63, -9), (-62, 7), (-57, -11))):   # the west end beds into a slope of big fallen rock
        crag(P, "talus", (rnd.uniform(8, 11), rnd.uniform(7, 10), rnd.uniform(4, 7)), (x, y, 0), rnd, mat=s, bev=0.15)
    return P


def heap(P, name, rx, ry, h, mat, rnd, loc, flat=0.2, segs=20, rings=8):
    """A smooth heap of earth h metres high, rx by ry across, level on top out to flat of its radius and easing down
    to the ground (steepest 1.5 h / ((1 - flat) r)): a bank a player walks up, unlike a lumpy mound."""
    bm = bmesh.new()
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    top = bm.verts.new((0, 0, h))
    prev = None
    for k in range(1, rings + 1):
        f = k / rings
        q = min(1.0, max(0.0, (f - flat) / (1 - flat)))
        z = h * (1 - q * q * (3 - 2 * q)) - (0.05 if k == rings else 0)
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            wob = 1 + 0.05 * (math.sin(2 * a + ph[0]) + 0.6 * math.sin(3 * a + ph[1]) + 0.4 * math.sin(5 * a + ph[2]))
            ring.append(bm.verts.new((math.cos(a) * rx * f * wob, math.sin(a) * ry * f * wob, z)))
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((top, ring[i], ring[j]) if prev is None else (prev[i], ring[i], ring[j], prev[j]))
        prev = ring
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm(name, bm, mat, smooth=True), (loc[0], loc[1], 0), parent=P)


def _rock_under(P, ys):
    """height(x): the highest point of what is built under P so far, cast straight down at x across ys (y0, y1)."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    mine = set(P.children_recursive)

    def height(x):
        best = -50.0
        for k in range(5):
            y = ys[0] + (ys[1] - ys[0]) * k / 4
            o = Vector((x, y, 200.0))
            for _ in range(8):           # step past anything that is not ours (a render's ground plane)
                hit, loc, _n, _i, ob, _m = bpy.context.scene.ray_cast(dg, o, Vector((0, 0, -1)))
                if not hit:
                    break
                if ob in mine or ob.original in mine:
                    best = max(best, loc.z)
                    break
                o = loc - Vector((0, 0, 0.01))
        return best
    return height


def ziggurat(seed=106):
    """The Green Ziggurat: five stepped tiers of Old Stone, 60 m square and 28 m to the top terrace, a grand stair up
    the front to a sanctuary whose roof fell in, probe crystals growing out through the gap. Trees grow on the
    terraces, vines hang down the walls and one corner has slumped into a ramp of rubble. Climb the stair or the rubble, land on the terraces."""
    rnd = random.Random(seed)
    P = _root("ziggurat")
    s, ds = rock_mat(), rock_dark()
    TH = 5.5
    for k in range(5):
        W = 60 - 10 * k
        z = TH * k
        if k in (1, 2):       # the slumped corner (east, front) is missing from these tiers
            g = 14 if k == 1 else 12
            a = (W - g)
            tapered(f"tier{k}a", (a, W), (a - 0.6, W - 1.2), TH, s, loc=(-g / 2, 0, z), bev=0.15, parent=P)
            tapered(f"tier{k}b", (g, W - g), (g - 0.6, W - g - 0.6), TH, s, loc=(W / 2 - g / 2, g / 2, z), bev=0.15, parent=P)
        else:
            tapered(f"tier{k}", (W, W), (W - 1.2, W - 1.2), TH, s, loc=(0, 0, z), bev=0.15, parent=P)
        # a carved frieze under each terrace edge
        for side in range(4):
            if k in (1, 2) and side in (0, 1):
                continue
            o = K.box("frieze", (W - 1.4, 0.5, 0.9), ds, loc=(0, -(W / 2 - 0.55), z + TH - 1.0), bev=0.06, parent=P)
            ang = math.radians(90 * side)
            o.rotation_euler.z = ang
            o.location = (math.sin(ang) * (W / 2 - 0.55), -math.cos(ang) * (W / 2 - 0.55), z + TH - 1.0)
    for k in (1, 2):          # partial friezes along the broken tiers' long sides
        W = 60 - 10 * k
        g = 14 if k == 1 else 12
        z = TH * k
        K.box("frieze", (W - g - 1.4, 0.5, 0.9), ds, loc=(-g / 2, -(W / 2 - 0.55), z + TH - 1.0), bev=0.06, parent=P)
        K.box("frieze", (0.5, W - g - 1.4, 0.9), ds, loc=(W / 2 - 0.55, g / 2, z + TH - 1.0), bev=0.06, parent=P)
    # the slumped corner: a ramp of fallen blocks from the ground east of the corner up onto the first terrace (11 m),
    # 25 degrees, wide enough to drive. The ramp's bed collides; the blocks heaped on it only show
    rx0, rx1, rx2, ry, rw = 42.0, 18.6, 10.4, -19.0, 11.0
    rz = lambda x: TH * 2 * min(1.0, max(0.0, (rx0 - x) / (rx0 - rx1)))
    o = _prism("slump_bed", (rx0, ry, 0.0), (rx1, ry, TH * 2), rw, TH * 2 + 1)
    o.data.materials[0] = ds
    K.place(o, parent=P)
    o = _prism("slump_top", (rx1, ry, TH * 2), (rx2, ry, TH * 2), rw, TH * 2 + 1)
    o.data.materials[0] = ds
    K.place(o, parent=P)
    for k in range(12):
        x = rnd.uniform(14, 40)
        y = rnd.uniform(ry - rw / 2, ry + rw / 2)
        h = rnd.uniform(1.4, 3.0)
        o = crag(P, "slump", (rnd.uniform(3, 6), rnd.uniform(3, 6), h), (x, y, rz(x) - h * 0.65), rnd, mat=s if k % 3 else ds, bev=0.15)
        K.no_collide(o)
    for k, (x, y) in enumerate(((34.0, -26.5), (26.0, -27.0), (38.0, -12.0), (31.0, -11.0))):   # big blocks along its sides
        crag(P, "slump", (rnd.uniform(4, 6), rnd.uniform(3, 4), rz(x) + rnd.uniform(1.0, 2.5)), (x, y, 0), rnd, mat=ds if k % 2 else s, bev=0.15)
    # the grand stair on the front, flanked by sloping walls
    top_z = TH * 5
    ys, ye = -42.8, -9.6      # no steeper than RAMP_MAX: walked and driven as a ramp
    stair(P, "grand_stair", (0, ys, 0), (0, ye, top_z), 9.0, s, rise=0.3, base=7.0)
    for sx in (-1, 1):
        block(P, "stair_wall", (sx * 5.0, ys - 0.5, 0.2), (sx * 5.0, ye, top_z + 0.2), (1.2, 1.1), (1.2, 1.1), ds, bev=0.1)
        K.box("stair_post", (2.2, 2.2, 2.6), ds, loc=(sx * 5.2, ys - 0.6, 1.0), bev=0.1, parent=P)
    # the sanctuary on top: thick walls round a doorway, half its roof fallen in, crystals growing out of the gap
    zt = top_z
    K.box("sanct_floor", (16.0, 16.0, 0.6), ds, loc=(0, 2.0, zt + 0.3), bev=0.1, parent=P)
    K.box("sanct_step", (6.0, 3.3, 0.3), ds, loc=(0, -7.65, zt + 0.15), bev=0.05, parent=P)
    for sx in (-1, 1):
        block(P, "sanct_side", (sx * 5.6, 2.0, zt + 0.6), (sx * 5.6, 2.0, zt + 8.6), (2.2, 12.8), (1.8, 12.2), s, bev=0.15)
        block(P, "sanct_front", (sx * 3.4, -3.6, zt + 0.6), (sx * 3.4, -3.6, zt + 8.6), (2.6, 2.0), (2.4, 1.8), s, bev=0.15)
    block(P, "sanct_back", (0, 7.6, zt + 0.6), (0, 7.6, zt + 8.6), (9.4, 2.0), (9.0, 1.8), s, bev=0.15)
    K.box("sanct_lintel", (6.0, 2.4, 2.2), ds, loc=(0, -3.6, zt + 7.6), bev=0.12, parent=P)
    glyph(P, "sanct_glyph", "eye", (0, -4.85, zt + 7.6), 1.6, s)
    K.box("sanct_roof", (13.6, 6.6, 1.2), ds, loc=(0, 5.0, zt + 9.2), bev=0.12, parent=P)
    K.box("sanct_cornice", (13.6, 1.4, 1.0), ds, loc=(0, -3.8, zt + 9.1), bev=0.1, parent=P)
    crag(P, "roof_fallen", (6.0, 4.0, 1.4), (6.5, -9.0, zt), rnd, mat=ds, rot=(6, 14, 20), bev=0.12)
    K.box("altar", (3.6, 3.6, 1.4), ds, loc=(0, 1.0, zt + 1.3), bev=0.1, parent=P)
    for k in range(7):
        crystal(P, "crystal", (rnd.uniform(-0.8, 0.8), 1.0 + rnd.uniform(-0.8, 0.8), zt + 1.9), rnd.uniform(0, 360), rnd.uniform(60, 88),
                rnd.uniform(3.0, 7.5), rnd.uniform(0.35, 0.6), cyan(3))
    K.point("altar_light", (0, -1.5, zt + 4.0), CYAN, 2500, 0.6).parent = P
    # overgrowth: trees and bushes on the terraces, vines down the walls
    for k in range(8):
        tier = rnd.choice((0, 1, 2, 3))
        W = 60 - 10 * tier
        side = rnd.choice((1, 2, 3))
        u = rnd.uniform(-W / 2 + 6, W / 2 - 6)
        r_ = W / 2 - 2.8
        ang = math.radians(90 * side)
        x, y = math.sin(ang) * r_ + math.cos(ang) * u, -math.cos(ang) * r_ + math.sin(ang) * u
        tree(P, f"tree{k}", (x, y, TH * (tier + 1)), rnd.uniform(7, 11), rnd)
    for k in range(10):
        tier = rnd.choice((0, 1, 2, 3, 4))
        W = 60 - 10 * tier
        x = rnd.uniform(-W / 2 + 4, W / 2 - 4)
        if abs(x) < 7:
            x += 9
        bush(P, "bush", (x, -(W / 2 - 3.5), TH * (tier + 1) + 0.3), rnd.uniform(1.2, 2.4), rnd, shade=k)
    vine = leaf(1)
    for k in range(16):
        tier = rnd.choice((0, 1, 2, 3, 4))
        W = 60 - 10 * tier
        side = rnd.choice((0, 2, 3))
        u = rnd.uniform(-W / 2 + 3, W / 2 - 3)
        if side == 0 and abs(u) < 8:
            continue
        ang = math.radians(90 * side)
        h = rnd.uniform(2.5, TH - 0.5)
        r_ = W / 2 + 0.05
        o = K.box("vine", (rnd.uniform(1.0, 2.6), 0.3, h), vine, loc=(math.sin(ang) * r_ + math.cos(ang) * u, -math.cos(ang) * r_ + math.sin(ang) * u, TH * (tier + 1) - h / 2),
                  bev=0, parent=P)
        o.rotation_euler.z = ang
    for k in range(12):
        a = rnd.uniform(0, 6.28)
        boulder(P, "rubble", (rnd.uniform(1.2, 3.0), rnd.uniform(1.2, 2.6), rnd.uniform(0.6, 1.6)), (math.cos(a) * 33, math.sin(a) * 33, 0), rnd)
    for ob in P.children_recursive:    # nothing in the way of the stair or the ramp
        n = ob.name.split(".")[0]
        if n.startswith(("tree", "rubble")) and ob.type == "MESH":
            x, y, _ = ob.matrix_world.translation
            if (x > 8 and y < -9) or (abs(x) < 7 and y < -28):
                K.no_collide(ob)
    return quiet(P, "tree*_crown*", "bush", "vine", "crystal")


# ================================================================== Settler Patchwork
def windmill_tower(seed=107):
    """Windmill Tower: a colony water tower the settlers took over, its capsule tank patched with rust, zinc and teal
    plate, a plank gallery and shacks round its waist, a ring of shacks between its legs and a four-sailed windmill
    on top pumping water, 56 m to the sail tips. Climb the switchback stair to the gallery and the mill mast to
    the cap."""
    rnd = random.Random(seed)
    P = _root("windmill_tower")
    steel = M(STEEL, metal=0.4, wear=0.3, grime=0.4)
    gra = M(GRAPHITE, metal=0.4, wear=0.3)
    rust = M(RUST, metal=0.4, wear=0.2, grime=0.5)
    zinc = M(ZINC, metal=0.5, wear=0.2, grime=0.4)
    bone = M(BONE, wear=0.3, grime=0.5)
    patches = [M(RUST, metal=0.3, wear=0.2, grime=0.5), M(ZINC, metal=0.5, wear=0.2), M(SEA, wear=0.25, grime=0.4),
               M(BRICK, wear=0.25, grime=0.4), M(MUSTARD, wear=0.25, grime=0.5)]
    # legs: four raking lattice legs on concrete feet, braced in X
    TZ = 22.0
    base, topw = 15.0, 9.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("foot", (2.4, 2.4, 1.2), _concrete(), loc=(sx * base / 2, sy * base / 2, 0.4), bev=0.08, parent=P)
            beam(P, "leg", (sx * base / 2, sy * base / 2, 0.8), (sx * topw / 2, sy * topw / 2, TZ), 0.8, steel)
    for k in range(4):
        z0, z1 = 0.8 + k * (TZ - 0.8) / 4, 0.8 + (k + 1) * (TZ - 0.8) / 4
        h0 = base / 2 + (topw / 2 - base / 2) * k / 4
        h1 = base / 2 + (topw / 2 - base / 2) * (k + 1) / 4
        for side in range(4):
            ang = math.radians(90 * side)
            c, sn = math.cos(ang), math.sin(ang)
            p = lambda h, u, z: Vector((sn * h + c * u, -c * h + sn * u, z))
            beam(P, "brace", p(h0, -h0, z0), p(h1, h1, z1), 0.25, gra)
            if side % 2 == 0:
                beam(P, "brace", p(h0, h0, z0), p(h1, -h1, z1), 0.25, gra)
            beam(P, "ring", p(h1, -h1, z1), p(h1, h1, z1), 0.3, steel)
    # the capsule tank, patched
    K.cyl("tank", 6.5, 7.0, bone, loc=(0, 0, TZ + 5.5), segs=24, bev=0.1, parent=P)
    K.sphere("tank_top", 6.5, bone, loc=(0, 0, TZ + 9.0), scale=(1, 1, 0.42), parent=P)
    K.sphere("tank_bot", 6.5, bone, loc=(0, 0, TZ + 2.0), scale=(1, 1, 0.3), parent=P)
    for z in (TZ + 3.0, TZ + 8.0):
        K.cyl("tank_rib", 6.62, 0.5, steel, loc=(0, 0, z), segs=24, bev=0, parent=P)
    for k in range(14):
        a = rnd.uniform(0, 360)
        z = rnd.uniform(TZ + 3.6, TZ + 7.6)
        w, h = rnd.uniform(1.6, 3.2), rnd.uniform(1.2, 2.4)
        K.box("patch", (w, 0.1, h), rnd.choice(patches), loc=(6.56 * math.cos(math.radians(a)), 6.56 * math.sin(math.radians(a)), z),
              rot=(0, rnd.uniform(-6, 6), a + 90), bev=0.02, parent=P)
    stencil(P, "W", (0, -6.75, TZ + 5.6), 3.0, M(SEA, wear=0.3))
    # the plank gallery round the tank's waist, its rail, and shacks bolted to it
    GZ = TZ + 0.2
    for k in range(8):
        ang = math.radians(45 * k)
        o = K.box("gallery", (6.4, 4.2, 0.3), wood(k), loc=(math.cos(ang) * 7.6, math.sin(ang) * 7.6, GZ), rot=(0, 0, math.degrees(ang) + 90), bev=0.03, parent=P)
        # the rail is open where the stair comes up, at the south-east corner
        t0, t1 = (-0.6, 3.2) if k == 7 else (-3.2, 3.2)
        tc = Vector((-math.sin(ang), math.cos(ang), 0)) * (t0 + t1) / 2
        K.box("rail", (t1 - t0, 0.12, 0.12), wood(3), loc=(math.cos(ang) * 9.6 + tc.x, math.sin(ang) * 9.6 + tc.y, GZ + 1.1), rot=(0, 0, math.degrees(ang) + 90), bev=0, parent=P)
        if k != 6:
            K.box("post", (0.16, 0.16, 1.2), wood(3), loc=(math.cos(ang + 0.39) * 9.9, math.sin(ang + 0.39) * 9.9, GZ + 0.6), bev=0, parent=P)
    for k, ang in enumerate((200, 330)):
        a = math.radians(ang)
        cx, cy = math.cos(a) * 8.2, math.sin(a) * 8.2
        o = empty(P, f"gshack{k}", (cx, cy, GZ + 0.15), (0, 0, ang + 90))
        K.box("gs_wall", (4.4, 2.8, 2.8), wood(k + 1), loc=(0, 0, 1.4), bev=0.03, parent=o)
        K.box("gs_roof", (5.0, 3.6, 0.15), rnd.choice(patches[:2]), loc=(0, 0.2, 3.0), rot=(-8, 0, 0), bev=0.02, parent=o)
        K.box("gs_door", (1.0, 0.1, 2.0), rnd.choice(patches[2:]), loc=(-0.8, -1.42, 1.0), bev=0.02, parent=o)
        K.box("gs_window", (1.0, 0.1, 0.7), glow(WARM, 2), loc=(1.1, -1.42, 1.7), bev=0.01, parent=o)
    # string of bulbs round the gallery
    pts = [(math.cos(math.radians(a)) * 9.7, math.sin(math.radians(a)) * 9.7, GZ + 2.6 + 0.4 * math.cos(math.radians(a * 4))) for a in range(0, 361, 30)]
    tube(P, "bulb_wire", pts, 0.03, gra, sides=4, sub=2)
    for p in pts[:-1]:
        ball(P, "bulb", 0.2, (p[0], p[1], p[2] - 0.25), glow(WARM, 8))
    # the windmill: a lattice mast on the tank's crown, the cap, four sails and the tail vane
    MZ = TZ + 11.6
    HZ = MZ + 10.5
    lattice(P, "mill_mast", (0, 0, TZ + 10.5), (0, 0, HZ - 1.5), 3.2, 1.8, 4, (rust, steel), gra, leg=0.35, br=0.15, band=1)
    K.box("mill_platform", (4.4, 4.4, 0.25), wood(2), loc=(0, 0, HZ - 1.4), bev=0.02, parent=P)
    K.box("cap", (2.6, 4.6, 2.4), M(BRICK, wear=0.3, grime=0.4), loc=(0, 0.4, HZ), bev=0.06, parent=P)
    K.box("cap_roof", (3.0, 5.0, 0.3), zinc, loc=(0, 0.4, HZ + 1.35), rot=(0, 6, 0), bev=0.02, parent=P)
    rod(P, "shaft", (0, -1.8, HZ), (0, -3.2, HZ), 0.35, gra, segs=10)
    K.cyl("hub", 0.8, 1.2, rust, loc=(0, -3.4, HZ), rot=(90, 0, 0), segs=12, bev=0.05, parent=P)
    cloth = [M(BONE, wear=0.2, grime=0.3, rough=0.9), M(SEA, wear=0.2, rough=0.9), M(BONE, wear=0.2, grime=0.3, rough=0.9), M(BRICK, wear=0.2, rough=0.9)]
    for k in range(4):
        arm = empty(P, f"sail{k}", (0, -3.6, HZ), (0, 20 + 90 * k, 0))
        K.box("spar", (0.45, 0.4, 13.0), wood(3), loc=(0, 0, 6.6), bev=0.03, parent=arm)
        for r_ in (3.4, 5.8, 8.2, 10.6, 12.8):
            K.box("rung", (2.9, 0.16, 0.16), wood(1), loc=(1.3, 0, r_), bev=0, parent=arm)
        K.box("stile", (0.16, 0.16, 9.8), wood(1), loc=(2.7, 0, 8.0), bev=0, parent=arm)
        K.box("cloth", (2.5, 0.06, 9.0), cloth[k], loc=(1.4, -0.15, 8.1), bev=0, parent=arm)
    rod(P, "tail_boom", (0, 2.6, HZ), (0, 9.5, HZ + 0.8), 0.18, gra, segs=6)
    K.box("tail_vane", (0.12, 4.2, 3.0), patches[4], loc=(0, 10.0, HZ + 1.0), bev=0.02, parent=P)
    rod(P, "flagpole", (0, 0.4, HZ + 1.5), (0, 0.4, HZ + 6.0), 0.07, gra, segs=6)
    K.box("pennant", (2.4, 0.04, 1.0), M(SEA, wear=0.2, rough=0.9), loc=(1.25, 0.4, HZ + 5.4), rot=(0, 8, 0), bev=0, parent=P)
    rod(P, "pump_rod", (0.3, 0, HZ - 1.4), (0.3, 0, TZ + 10.0), 0.08, gra, segs=6)
    # the switchback stair from the ground to the gallery, up the front, in two lanes with landings across both
    for i in range(4):
        z0, z1 = i * (GZ / 4), (i + 1) * (GZ / 4)
        sx = -1 if i % 2 == 0 else 1
        y = -9.6 if i % 2 == 0 else -11.1
        a = Vector((-sx * 4.2, y, z0))
        b = Vector((sx * 4.2, y, z1))
        stair(P, "stair", a, b, 1.4, wood(2), rise=0.3, base=0.3)
        if i < 3:
            K.box("landing", (2.2, 2.9, 0.3), wood(1), loc=(sx * 5.3, -10.35, z1 - 0.15), bev=0.02, parent=P)
        beam(P, "stair_post", (sx * 6.3, -11.7, 0), (sx * 6.3, -11.7, z1), 0.25, wood(3))
    K.box("landing", (2.4, 3.8, 0.3), wood(1), loc=(5.4, -9.9, GZ), bev=0.02, parent=P)      # the top, onto the gallery
    # shacks between the legs, a cistern under the tank and its pipe
    for k, (x, y, yaw, w_, d_) in enumerate(((-11.5, 4.0, 90, 6.0, 4.0), (11.0, 3.0, -90, 5.0, 4.0), (4.0, 12.0, 180, 6.5, 4.0))):
        o = empty(P, f"shack{k}", (x, y, 0), (0, 0, yaw))
        K.box("sh_wall", (w_, d_, 3.0), wood(k), loc=(0, 0, 1.5), bev=0.03, parent=o)
        K.box("sh_roof", (w_ + 0.8, d_ + 1.2, 0.15), rnd.choice(patches), loc=(0, -0.2, 3.3), rot=(10, 0, 0), bev=0.02, parent=o)
        K.box("sh_patch", (1.6, 0.1, 1.4), rnd.choice(patches), loc=(w_ * 0.25, -d_ / 2 - 0.05, 1.6), bev=0.02, parent=o)
        K.box("sh_door", (1.1, 0.1, 2.1), rnd.choice(patches[2:]), loc=(-w_ * 0.2, -d_ / 2 - 0.05, 1.05), bev=0.02, parent=o)
    K.cyl("cistern", 3.0, 2.6, rust, loc=(0, 3.0, 1.3), segs=16, bev=0.05, parent=P)
    rod(P, "downpipe", (1.5, 1.5, TZ + 1.0), (1.5, 2.5, 2.6), 0.25, zinc, segs=8)
    K.disc("yard", 16.0, earth(), (0, 0), z=0.02, segs=24, wobble=0.12, seed=seed).parent = P
    return quiet(P, "bulb", "bulb_wire", "pump_rod", "flagpole", "pennant", "tail_boom", "downpipe")


# ================================================================== Salvager Tech
def _oct(w, h, c):
    """The freighter's chamfered section, (y, z) counter-clockwise from the bottom."""
    return [(-w + c, 0), (w - c, 0), (w, c), (w, h - c), (w - c, h), (-w + c, h), (-w, h - c), (-w, c)]


def _hull(G, x0, x1, sec, hm, gm, rnd, seg=8.0, ragged=None, ring_every=2, nose=False):
    """Plates round the section from x0 to x1 in seg-long bays; ragged (+1 or -1) tears the bay at that end. The rings
    standing proud of the roof get a low ramp each side in the collider, so a player walks along the roof over them
    (with nose, the ring at x1 only on its -x side: the nose meets it there)."""
    n = max(1, round((x1 - x0) / seg))
    L = (x1 - x0) / n
    sides = list(zip(sec, sec[1:] + sec[:1]))
    T = 0.4
    for i in range(n + 1):
        xr = x0 + i * L
        if ragged == -1 and i == 0 or ragged == 1 and i == n:
            continue
        if i % ring_every:
            continue
        for (ya, za), (yb, zb) in sides:
            d = Vector((yb - ya, zb - za))
            mid = Vector(((ya + yb) / 2, (za + zb) / 2))
            out = Vector((d.y, -d.x)).normalized()
            ang = math.degrees(math.atan2(d.y, d.x))
            K.box("ring", (0.9, d.length + 0.8, 0.9), gm, loc=(xr, mid.x + out.x * 0.55, mid.y + out.y * 0.55), rot=(ang, 0, 0), bev=0, parent=G)
            if za == zb and za > 0:          # the roof
                for e in (-1, 1):
                    if not (nose and i == n and e > 0):
                        ramp(G, "ring_ramp", (xr + e * 2.25, 0, za + 0.4), (xr + e * 0.45, 0, za + 1.0), abs(yb - ya), base=0.8)
    for i in range(n):
        xa, xb = x0 + i * L, x0 + (i + 1) * L
        for k, ((ya, za), (yb, zb)) in enumerate(sides):
            a, b = xa, xb
            if ragged == -1 and i == 0:
                a = xa + rnd.uniform(0.5, L * 0.8) * (k > 0)      # the floor plate runs out to the torn end, to walk in on
            if ragged == 1 and i == n - 1:
                b = xb - rnd.uniform(0.5, L * 0.8) * (k > 0)
            d = Vector((yb - ya, zb - za))
            mid = Vector(((ya + yb) / 2, (za + zb) / 2))
            out = Vector((d.y, -d.x)).normalized()
            ang = math.degrees(math.atan2(d.y, d.x))
            pm = gm if rnd.random() < 0.18 else hm
            K.box("plate", (b - a - 0.05, d.length + 0.05, T), pm, loc=((a + b) / 2, mid.x + out.x * T / 2, mid.y + out.y * T / 2), rot=(ang, 0, 0), bev=0, parent=G)
    # torn plate ends bent out at a ragged end
    if ragged:
        xe = x0 if ragged == -1 else x1
        for k in range(14):
            f = k / 14
            idx = int(f * 8)
            (ya, za), (yb, zb) = sides[idx]
            t = f * 8 - idx
            y, z = ya + (yb - ya) * t, za + (zb - za) * t
            K.box("torn", (rnd.uniform(1.5, 3.5), rnd.uniform(1.5, 3.0), 0.3), hm if k % 3 else gm,
                  loc=(xe + ragged * rnd.uniform(-1.0, 0.6), y * 1.04, z if z > 0.5 else 0.6), rot=(rnd.uniform(-40, 40), rnd.uniform(-50, 50), rnd.uniform(-30, 30)), bev=0, parent=G)


def freighter_wreck(seed=108):
    """The Long Hauler: a deep-space freighter, CF-9, nearly three times the SV-04's length and twice
    its beam, broken in two over a ridge. The fore section
    lies nose-down in a ploughed furrow with its bridge and beacon mast up, the beacon still burning cyan; the aft
    section rolled over on its side behind it with three dead drive bells. Walk into either torn end, climb to the
    bridge, salvage the spilled cargo pods."""
    rnd = random.Random(seed)
    P = _root("freighter_wreck")
    hm = M(HULL, metal=0.4, wear=0.25, grime=0.4)
    gm = M(GUNMETAL, metal=0.5, wear=0.25, grime=0.4)
    dark = K.mat("hull_in", "#15181c", rough=0.9)
    cop = M(COPPER, metal=0.7, wear=0.2)
    mus = M(MUSTARD, wear=0.25)
    cy = glow(CYAN, 8)
    sec = _oct(11.0, 17.0, 4.0)
    # the ridge the ship broke its back on: both torn ends rest on it, the nose and the drives on the ground
    heap(P, "ridge", 24.0, 26.0, 7.0, earth(), rnd, loc=(-8.0, 3.0), flat=0.25)
    for k in range(5):
        crag(P, "ridge_rock", (rnd.uniform(3, 6), rnd.uniform(3, 5), rnd.uniform(3, 6)), (-8 + rnd.uniform(-9, 9), 3 + rnd.uniform(-14, 14), 0), rnd, mat=rock_mat(), bev=0)
    # fore section: 44 m of hull and a 14 m nose, bridge on top; built from its torn end at x = 0 towards +x
    F = empty(P, "fore", (-2.0, 1.0, 7.6), (4.0, 11.0, 6.0))
    _hull(F, 0.0, 44.0, sec, hm, gm, rnd, ragged=-1, nose=True)
    K.box("fore_deck", (44.0, 20.0, 0.4), dark, loc=(22.0, 0, 5.5), bev=0, parent=F)
    o = _prism("bent_plate", (-3.2, -4.0, -1.25), (0.4, -4.0, 0.0), 7.5, 0.35)   # a floor plate bent down onto the ridge: the way in
    o.data.materials[0] = hm
    K.place(o, parent=F)
    K.box("fore_inner", (43.0, 0.3, 16.0), dark, loc=(22.5, 0, 8.5), bev=0, parent=F)
    # the nose: a tapering octagon prism with a dark glazed band
    bm = bmesh.new()
    s2 = _oct(6.5, 10.0, 2.6)
    ra = [bm.verts.new((44.0, y, z)) for y, z in sec]
    rb = [bm.verts.new((58.0, y, z + 2.0)) for y, z in s2]
    bm.faces.new(list(reversed(ra)))
    bm.faces.new(rb)
    for i in range(8):
        j = (i + 1) % 8
        bm.faces.new((ra[i], ra[j], rb[j], rb[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    nose = K.from_bm("nose", bm, hm)
    K.bevel(nose, 0.1, segs=2)
    nose.parent = F
    K.box("nose_glass", (8.0, 13.0, 1.6), K.mat("glass_d", GLASS_D, rough=0.15, metal=0.3), loc=(51.0, 0, 13.6), rot=(0, 24, 0), bev=0.05, parent=F)
    # the way up: from the bank of earth the nose ploughed up, over the nose and its ring onto the roof
    ramp(F, "nose_ring_ramp", (52.45, 0, 14.15), (44.45, 0, 18.0), 9.0, base=1.0)
    # bridge tower, beacon mast and the beacon, still lit
    FR.framed_box(F, "bridge", (30.0, 0, 20.0), (12.0, 10.0, 6.0), hm, gm, fw=0.5)
    K.box("bridge_glass", (0.2, 8.4, 1.6), K.mat("glass_d", GLASS_D, rough=0.15, metal=0.3), loc=(36.05, 0, 21.0), bev=0.02, parent=F)
    K.box("bridge_screen", (0.2, 3.0, 0.5), cy, loc=(36.1, 0, 19.4), bev=0.01, parent=F)
    stair(F, "bridge_stair", (16.5, -3.5, 17.4), (23.9, -3.5, 23.3), 1.6, gm, rise=0.3, base=0.4)
    rod(F, "bridge_stair_rail", (16.5, -4.25, 18.4), (23.9, -4.25, 24.3), 0.05, mus, segs=6)
    lattice(F, "beacon_mast", (27.0, 0, 23.0), (27.0, 0, 41.0), 2.6, 1.0, 6, (gm, hm), gm, leg=0.35, br=0.14, band=1)
    K.cyl("beacon_cage", 1.1, 1.8, gm, loc=(27.0, 0, 41.9), segs=10, bev=0.04, parent=F)
    K.sphere("beacon", 1.0, glow(CYAN, 14), loc=(27.0, 0, 42.0), parent=F)
    K.point("beacon_light", (27.0, 0, 43.5), CYAN, 6000, 0.6).parent = F
    # running lights: cyan strips along the flanks, some dead
    for side in (-1, 1):
        for k in range(10):
            on = rnd.random() > 0.3
            K.box("runlight", (2.6, 0.3, 0.3), cy if on else gm, loc=(3.0 + k * 4.2, side * 11.45, 11.5), bev=0, parent=F)
    stencil(F, "CF-9", (24.0, -11.47, 7.0), 4.0, mus)
    for k in range(4):        # hazard bands on the hull by the break
        K.box("haz", (2.0, 0.2, 3.0), mus if k % 2 == 0 else gm, loc=(4.0 + k * 2.0, -11.45, 3.0), bev=0, parent=F)
    # aft section: x from -32 (drives) to 0 (torn end), its torn end up on the ridge facing the fore section's
    A = empty(P, "aft", (-14.0, 4.0, 7.4), (-12.0, -12.0, 10.0))
    _hull(A, -32.0, 0.0, sec, hm, gm, rnd, ragged=1)
    K.box("aft_deck", (32.0, 20.0, 0.4), dark, loc=(-16.0, 0, 5.5), bev=0, parent=A)
    K.box("aft_bulk", (0.4, 20.0, 16.0), dark, loc=(-31.5, 0, 8.5), bev=0, parent=A)
    for k, (y, z) in enumerate(((-5.5, 5.0), (5.5, 5.0), (0.0, 12.0))):
        bell = lathe("bell", [(0, 0), (2.4, 0), (2.6, 1.0), (3.6, 4.0), (4.6, 7.0), (4.2, 7.0), (3.2, 4.0), (2.0, 1.0), (0, 0.8)], gm, segs=16)
        K.place(bell, (-32.0, y, z), (0, -90, 0), A)
        K.cyl("throat", 2.7, 0.6, cop, loc=(-32.6, y, z), rot=(0, 90, 0), segs=16, bev=0, parent=A)
    K.box("engine_block", (10.0, 12.0, 3.0), gm, loc=(-24.0, 0, 18.4), bev=0.1, parent=A)
    for x in (-28.8, -19.2):
        K.box("engine_rail", (0.6, 12.6, 3.4), hm, loc=(x, 0, 18.4), bev=0, parent=A)
    for k in range(5):
        rod(A, "pipe", (-30.0, -6 + k * 3, 17.5), (-18.0, -6 + k * 3, 17.5), 0.35, cop, segs=8)
    # radiator wings on the aft section's back: one still up, one snapped over
    for k, (side, lean) in enumerate(((-1, 22), (1, 64))):
        W = empty(A, f"wing{k}", (-12.0, side * 5.0, 17.0), (side * -lean, 0, 0))
        K.box("wing_panel", (14.0, 0.4, 18.0), gm, loc=(0, 0, 9.4), bev=0, parent=W)
        for x in (-7.0, 7.0):
            K.box("wing_rail", (0.8, 0.8, 19.0), hm, loc=(x, 0, 9.5), bev=0, parent=W)
        for z in (0.4, 9.4, 18.6):
            K.box("wing_rail", (14.8, 0.8, 0.8), hm, loc=(0, 0, z), bev=0, parent=W)
        for x in (-4.0, 0.0, 4.0):
            K.box("wing_pipe", (0.5, 0.7, 17.0), cop, loc=(x, 0, 9.4), bev=0, parent=W)
        K.box("wing_hinge", (6.0, 2.0, 1.6), hm, loc=(0, 0, -0.3), bev=0, parent=W)
    # the furrow the fore section ploughed, the mounds it pushed up, and the cargo it spilled
    K.strip("furrow", [(14, -2), (34, 0), (56, 2)], 20.0, earth()).parent = P
    for (x, y, rx, ry, h) in ((44, -12, 9, 6, 2.5), (-42, -12, 10, 6, 2.5), (-36, 20, 12, 7, 3.0)):
        mound(P, "mound", rx, ry, h, earth(), rnd, loc=(x, y), segs=14, rings=4)
    heap(P, "bank", 22.0, 15.0, 9.0, earth(), rnd, loc=(60.0, 7.0))     # ploughed up by the nose: the way onto it
    pods = [M(GUNMETAL, metal=0.4, wear=0.25), M(HULL, metal=0.4, wear=0.25)]
    for k, (x, y, yaw, roll) in enumerate(((-4.0, -18.0, 20, 0), (6.0, -22.0, 70, 12), (-16.0, -20.0, -10, 90), (12.0, -14.0, 40, 0))):
        o = empty(P, f"pod{k}", (x, y, 1.5), (roll, 0, yaw))
        K.box("pod", (6.0, 3.0, 3.0), pods[k % 2], bev=0.1, parent=o)
        for s_ in (-1, 1):
            K.box("pod_band", (0.6, 3.1, 3.1), mus, loc=(s_ * 2.4, 0, 0), bev=0.02, parent=o)
        K.box("pod_light", (1.0, 0.1, 0.3), cy if k % 2 == 0 else gm, loc=(0, -1.55, 0.9), bev=0, parent=o)
    return quiet(P, "torn", "nose_glass", "runlight", "haz", "beacon", "pipe")


PROPS = {
    "landmark_relay_spire": relay_spire,
    "landmark_cooling_towers": cooling_towers,
    "landmark_aqueduct": aqueduct,
    "landmark_kneeling_giant": kneeling_giant,
    "landmark_sky_arch": sky_arch,
    "landmark_ziggurat": ziggurat,
    "landmark_windmill_tower": windmill_tower,
    "landmark_freighter_wreck": freighter_wreck,
}
