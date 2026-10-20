"""Settler Patchwork props, camp set (docs/world-generation.md, Scenes): what survivors live among in a camp or a
trading post, in the Settler Patchwork family of docs/art-direction.md (scavenged wood, canvas, corrugated sheet
patches, rope and the faded colony colours).

Sleeping: a ridge tent, a patched dome tent, a hammock between posts and bedrolls on a tarp. Cooking and washing: a
camp kitchen counter, a barrel smoker, a fish drying rack, a washtub on a stand and an outdoor tub over a fire pit.
Living: a clothes chest, a rocking chair, a plank bench, a pot-belly stove under the sky and a chimney stack whose
house is gone. Working: a hoop rack of firewood, a chopping block, a pair of sawhorses, a free-standing tool wall, an
anvil on a stump, a pedal grindstone, a heavy workbench and a barrel water filter. Trading: a two-wheel trader's
cart, a market stall and a bare awning frame with a torn tarp.

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
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, ZINC, GUNMETAL, STONE, CONCRETE
from props import _root, wood, plank, post, bolt, BARK, HEART, CHAR
from props_settler_a import rod, beam, prism, lump, corrugated, tin, rope_mat, sack, SACK, SACK_D, SACK_L, ASH, ROCK
from props_settler_b import _split_log
from props_colony_b import lathe, plate_xz
from props_stone import rock, limb, tapered

CANVAS, CANVAS_D = "#bcae8a", "#9d9070"
OLIVE = "#6f7a5a"
DARK = "#2e2b26"        # the shadowed inside of a tent or a stove
WATER = "#2f4f55"
MORTAR = "#b3a996"
BRICK_D, SOOT = "#8a3a31", "#4a3530"
FISH, FISH_D = "#8e948c", "#9c8457"
LEATHER = "#6b4f38"
LEAF, APPLE = "#6fbf3a", "#c8423a"


# ---------------------------------------------------------------- helpers
def _grp(P, name, loc=(0, 0, 0), rot=(0, 0, 0), scale=None):
    g = K.link(bpy.data.objects.new(name, None))
    g.parent = P
    g.location = loc
    g.rotation_euler = [math.radians(a) for a in rot]
    if scale:
        g.scale = scale
    return g


def canvas(col=CANVAS, **kw):
    """Canvas, tarp and blanket cloth: matte, a little grimy, never chipped like paint."""
    kw.setdefault("rough", 0.95)
    kw.setdefault("wear", 0.12)
    kw.setdefault("grime", 0.35)
    kw.setdefault("scratches", 0.0)
    return M(col, **kw)


def iron(**kw):
    kw.setdefault("metal", 0.55)
    kw.setdefault("wear", 0.25)
    kw.setdefault("grime", 0.4)
    return M(GRAPHITE, **kw)


def bark():
    return K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)


def heart():
    return K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)


def sheet(P, name, f, nu, nv, mat, t=0.008, keep=None):
    """A cloth surface: f(u, v) gives each grid point; keep(u, v) drops the cells it rejects (tears)."""
    bm = bmesh.new()
    g = [[bm.verts.new(f(i / nu, j / nv)) for j in range(nv + 1)] for i in range(nu + 1)]
    for i in range(nu):
        for j in range(nv):
            if keep is None or keep((i + 0.5) / nu, (j + 0.5) / nv):
                bm.faces.new((g[i][j], g[i + 1][j], g[i + 1][j + 1], g[i][j + 1]))
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    ob = K.from_bm(name, bm, mat, smooth=True)
    if t:
        mod = ob.modifiers.new("thick", "SOLIDIFY")
        mod.thickness = t
        mod.offset = 0.0   # centred, so overlays (patches, doors) sit clear whichever way the faces point
    return K.place(ob, parent=P)


def peg(P, loc, yaw=0.0, lean=15):
    """A tent peg driven in at a lean, away from the line pulling on it."""
    x, y = loc
    return K.box("peg", (0.025, 0.025, 0.2), wood(3, wear=0.1), loc=(x, y, 0.05), rot=(0, lean, yaw), bev=0.004, parent=P)


def guy(P, a, b, r=0.005):
    return rod(P, "guy", a, b, r, rope_mat(), segs=4)


def nail(P, loc, rot=(90, 0, 0)):
    return K.cyl("nail", 0.009, 0.01, iron(), loc=loc, rot=rot, segs=6, bev=0, parent=P)


def _se(a, b, n, e):
    """n points round a superellipse of half sizes a, b (e = 2 an ellipse, higher a rounded box)."""
    pts = []
    for i in range(n):
        th = 2 * math.pi * i / n
        c, s = math.cos(th), math.sin(th)
        pts.append((a * math.copysign(abs(c) ** (2 / e), c), b * math.copysign(abs(s) ** (2 / e), s)))
    return pts


def basin(P, name, lo, hi, h, t, mat, loc=(0, 0, 0), rot=(0, 0, 0), e=2.0, n=24):
    """A hollow vessel (tub, bucket, pot): its floor's half sizes lo, its rim's hi, walls t thick, origin at its foot."""
    bm = bmesh.new()
    (a0, b0), (a1, b1) = lo, hi
    rings = [(_se(a0, b0, n, e), 0), (_se(a1, b1, n, e), h), (_se(a1 - t, b1 - t, n, e), h), (_se(a0 - t, b0 - t, n, e), t)]
    vs = [[bm.verts.new((x, y, z)) for x, y in pts] for pts, z in rings]
    for k in range(3):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((vs[k][i], vs[k][j], vs[k + 1][j], vs[k + 1][i]))
    bm.faces.new(list(reversed(vs[0])))
    bm.faces.new(vs[3])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat)
    K.bevel(ob, min(0.006, t * 0.4), segs=2, angle=50)
    return K.place(ob, loc, rot, P)


def arc_shell(P, name, R, a0, a1, y0, y1, mat, t=0.01, n=12, loc=(0, 0, 0)):
    """A curved sheet round the y axis from angle a0 to a1 (radians, 0 = +x, pi/2 = +z): drum halves, chutes."""
    bm = bmesh.new()
    O0, O1, I0, I1 = [], [], [], []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        for lst, rr, yy in ((O0, R, y0), (O1, R, y1), (I0, R - t, y0), (I1, R - t, y1)):
            lst.append(bm.verts.new((rr * math.cos(a), yy, rr * math.sin(a))))
    for i in range(n):
        bm.faces.new((O0[i], O0[i + 1], O1[i + 1], O1[i]))
        bm.faces.new((I0[i], I1[i], I1[i + 1], I0[i + 1]))
        bm.faces.new((O0[i], I0[i], I0[i + 1], O0[i + 1]))
        bm.faces.new((O1[i], O1[i + 1], I1[i + 1], I1[i]))
    bm.faces.new((O0[0], O1[0], I1[0], I0[0]))
    bm.faces.new((O0[-1], I0[-1], I1[-1], O1[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    return K.place(ob, loc, (0, 0, 0), P)


def bucket(P, name, loc, r=0.14, h=0.27, col=ZINC, water=True, yaw=0):
    """A tin pail with a bail lying back on its rim."""
    x, y, z = loc
    m = tin(col, wear=0.25)
    basin(P, name, (r * 0.8, r * 0.8), (r, r), h, 0.008, m, loc, n=14)
    K.torus(name + "_rim", r + 0.003, 0.006, m, loc=(x, y, z + h), segs=14, parent=P)
    if water:
        K.cyl(name + "_water", r * 0.94 - 0.008, 0.004, K.mat("water", WATER, rough=0.05), loc=(x, y, z + h * 0.72), segs=16, bev=0, parent=P)
    K.torus(name + "_bail", r + 0.004, 0.005, iron(), loc=(x, y, z + h - 0.02), rot=(25, 0, yaw), arc=180, segs=14, parent=P)


def crate(P, name, size, loc, yaw=0.0, shade=0):
    """A nailed plank crate: a box with corner battens and two rim bands."""
    sx, sy, sz = size
    g = _grp(P, name, loc, (0, 0, yaw))
    K.box(name + "_core", (sx - 0.02, sy - 0.02, sz - 0.01), wood(shade), loc=(0, 0, sz / 2), bev=0.006, parent=g)
    for a in (-1, 1):
        for b in (-1, 1):
            K.box(name + "_corner", (0.045, 0.045, sz), wood(3, wear=0.1), loc=(a * (sx / 2 - 0.02), b * (sy / 2 - 0.02), sz / 2), bev=0, parent=g)
    for z in (0.035, sz - 0.035):
        K.box(name + "_band", (sx + 0.004, sy + 0.004, 0.06), wood(shade + 1, wear=0.1), loc=(0, 0, z), bev=0, parent=g)
    return g


def spoked_wheel(P, name, loc, r, spokes=10, iron_tyre=True):
    """A wooden cart wheel on an axle along x: felloe, iron tyre, hub and spokes."""
    w = _grp(P, name, loc, (0, 0, 90))
    if iron_tyre:
        K.torus(f"{name}_tyre", r - 0.02, 0.024, iron(wear=0.2), rot=(90, 0, 0), segs=26, parent=w)
    K.torus(f"{name}_felloe", r - 0.06, 0.035, wood(2, wear=0.1), rot=(90, 0, 0), segs=18, parent=w)
    K.cyl(f"{name}_hub", 0.08, 0.2, wood(3, wear=0.3), rot=(90, 0, 0), segs=12, bev=0.01, parent=w)
    K.cyl(f"{name}_cap", 0.05, 0.24, iron(), rot=(90, 0, 0), segs=10, bev=0.005, parent=w)
    L = r - 0.13
    for k in range(spokes):
        a = 360 * k / spokes
        K.box(f"{name}_spoke", (0.035, 0.03, L), wood(1, wear=0.2), loc=(math.sin(math.radians(a)) * (0.07 + L / 2), 0, math.cos(math.radians(a)) * (0.07 + L / 2)),
              rot=(0, a, 0), bev=0, parent=w)
    return w


def patch(P, name, size, loc, rot, col, nails=True):
    """A salvaged board or sheet nailed over a split or a hole."""
    ob = K.box(name, size, M(col, wear=0.3, grime=0.45), loc=loc, rot=rot, bev=0.004, parent=P)
    if nails:
        w = max(size[0], size[2]) / 2 - 0.03
        for s in (-1, 1):
            nail(ob, (s * w if size[0] >= size[2] else 0, -size[1] / 2 - 0.003, 0 if size[0] >= size[2] else s * w))
    return ob


def bricks(P, name, x0, x1, y0, y1, z0, z1, rnd, mats, accents=0.5, faces=(-2, -1, 1, 2)):
    """Laid brick: rows of 65 mm courses round a mortar core, a few bricks picked out in another shade so the bond
    reads. faces: which faces get picked bricks (-2 = front, 2 = back, -1 = left, 1 = right)."""
    mortar = K.mat("mortar", MORTAR, rough=0.95, grime=0.4, moss=0.3, noise_scale=6.0)
    K.box(f"{name}_core", (x1 - x0 - 0.02, y1 - y0 - 0.02, z1 - z0), mortar, loc=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), bev=0, parent=P)
    rows = max(1, int(round((z1 - z0) / 0.075)))
    hz = (z1 - z0) / rows
    for r in range(rows):
        z = z0 + hz * (r + 0.5)
        K.box(f"{name}_row{r}", (x1 - x0 + rnd.uniform(-0.006, 0.006), y1 - y0 + rnd.uniform(-0.006, 0.006), hz - 0.011), mats[r % 2],
              loc=((x0 + x1) / 2 + rnd.uniform(-0.004, 0.004), (y0 + y1) / 2 + rnd.uniform(-0.004, 0.004), z), bev=0, parent=P)
        for f in faces:
            if rnd.random() > accents:
                continue
            m = rnd.choice(mats[2:]) if len(mats) > 2 else mats[(r + 1) % 2]
            if abs(f) == 2:
                span = x1 - x0
                if span < 0.3:
                    continue
                bx = x0 + 0.11 + (rnd.randrange(int((span - 0.22) / 0.11) + 1)) * 0.11 + (0.055 if r % 2 else 0)
                bx = min(bx, x1 - 0.11)
                y = y0 - 0.003 if f < 0 else y1 + 0.003
                K.box(f"{name}_b", (0.2, 0.02, hz - 0.013), m, loc=(bx, y + (0.006 if f < 0 else -0.006), z), bev=0, parent=P)
            else:
                span = y1 - y0
                if span < 0.3:
                    continue
                by = y0 + 0.11 + (rnd.randrange(int((span - 0.22) / 0.11) + 1)) * 0.11 + (0.055 if r % 2 else 0)
                by = min(by, y1 - 0.11)
                x = x0 - 0.003 if f < 0 else x1 + 0.003
                K.box(f"{name}_b", (0.02, 0.2, hz - 0.013), m, loc=(x + (0.006 if f < 0 else -0.006), by, z), bev=0, parent=P)


# ================================================================== sleeping
def tent_ridge(seed=41):
    """A canvas ridge tent on two poles: one door flap laced shut, the other rolled back and tied, guy ropes out to
    pegs, a patch sewn on its weather side and a bedroll inside."""
    rnd = random.Random(seed)
    P = _root("tent_ridge")
    L, W, H = 2.3, 1.9, 1.35
    cv = canvas()
    pole = wood(3, wear=0.1)
    for s in (-1, 1):
        K.cyl(f"pole{s}", 0.022, H + 0.14, pole, loc=(0, s * L / 2, (H + 0.14) / 2), segs=8, bev=0, parent=P)
    rod(P, "ridge", (0, -L / 2 - 0.05, H + 0.005), (0, L / 2 + 0.05, H + 0.005), 0.018, pole)

    def slope(s, dr=0.0):
        n = Vector((s * H, 0, W / 2)).normalized()

        def f(u, v):
            y = -L / 2 - 0.02 + (L + 0.04) * u
            sag = math.sin(math.pi * u) * math.sin(math.pi * v)
            p = Vector((s * (W / 2 * v + 0.015), y, H + 0.03 - (H + 0.0) * v))
            p -= n * 0.035 * sag
            if v > 0.9:  # the sod cloth turns out along the ground
                p.x += s * 0.25 * (v - 0.9)
            return tuple(p + n * dr)
        return f

    for s in (-1, 1):
        sheet(P, f"fly{s}", slope(s), 10, 6, cv, t=0.008)
    prism(P, "back", [(-W / 2 + 0.02, 0.01), (W / 2 - 0.02, 0.01), (0, H + 0.01)], 0.008, cv, loc=(0, L / 2 - 0.005, 0), bev=0)
    # door: the left flap laced shut, the right one rolled back to the eave and tied
    prism(P, "flap_l", [(-W / 2 + 0.03, 0.01), (-0.01, 0.01), (-0.01, H - 0.02)], 0.008, canvas(CANVAS_D), loc=(0, -L / 2 - 0.004, 0), bev=0)
    for k in range(5):
        z = 0.15 + k * 0.24
        K.box("lace", (0.05, 0.006, 0.008), rope_mat(), loc=(-0.025, -L / 2 - 0.012, z), rot=(0, 25 if k % 2 else -25, 0), bev=0, parent=P)
    a, b = Vector((0.03, -L / 2 - 0.035, H - 0.08)), Vector((W / 2 - 0.05, -L / 2 - 0.035, 0.12))
    rod(P, "flap_roll", tuple(a), tuple(b), 0.05, canvas(CANVAS_D), segs=10)
    d = (b - a).normalized()
    for f in (0.3, 0.7):
        p = a.lerp(b, f)
        rod(P, "tie", tuple(p - d * 0.012), tuple(p + d * 0.012), 0.054, rope_mat(), segs=10)
    # inside: a groundsheet and a bedroll
    K.box("groundsheet", (W - 0.25, L - 0.1, 0.01), canvas(OLIVE), loc=(0, 0, 0.005), bev=0, parent=P)
    K.box("bedroll", (0.62, 1.8, 0.08), canvas(SEA, grime=0.3), loc=(0.3, 0.1, 0.05), rot=(0, 0, 2), bev=0.035, parent=P)
    # guy ropes out to pegs, loops from the sod cloth to side pegs
    for s in (-1, 1):
        guy(P, (0, s * (L / 2 + 0.03), H + 0.1), (0, s * (L / 2 + 1.05), 0.12))
        peg(P, (0, s * (L / 2 + 1.08)), yaw=90 * s, lean=-18)
        for u in (0.02, 0.5, 0.98):
            y = -L / 2 + L * u
            guy(P, (s * (W / 2 + 0.04), y, 0.05), (s * (W / 2 + 0.33), y, 0.12), r=0.004)
            peg(P, (s * (W / 2 + 0.36), y), yaw=0 if s > 0 else 180, lean=-15)
    # patches on the weather side, sewn on in odd cloth
    sheet(P, "patch_a", lambda u, v: slope(1, 0.011)(0.18 + 0.12 * u, 0.3 + 0.22 * v), 2, 2, canvas(MUSTARD, wear=0.1), t=0.003)
    sheet(P, "patch_b", lambda u, v: slope(1, 0.011)(0.62 + 0.08 * u, 0.55 + 0.18 * v), 2, 2, canvas(SEA, wear=0.1), t=0.003)
    sheet(P, "patch_c", lambda u, v: slope(-1, 0.011)(0.4 + 0.1 * u, 0.2 + 0.15 * v), 2, 2, canvas(BRICK, wear=0.1), t=0.003)
    return P


def tent_dome_patched(seed=42):
    """A dome tent on two crossed poles, patched in four odd cloths, its door unzipped with the flap rolled up, a pair
    of boots left outside."""
    rnd = random.Random(seed)
    P = _root("tent_dome")
    R, H = 1.25, 1.15
    t0 = 0.12

    def dome(dr=0.0):
        def f(u, v):
            p = 2 * math.pi * u
            t = t0 + (math.pi / 2 - t0) * v
            sag = 1 - 0.06 * math.sin(2 * (p - math.pi / 4)) ** 2 * math.sin(2 * t) ** 1.5
            return ((R + dr) * sag * math.sin(t) * math.cos(p), (R + dr) * sag * math.sin(t) * math.sin(p), (H + dr) * sag * math.cos(t) + 0.01)
        return f

    sheet(P, "dome", dome(), 28, 7, canvas(CANVAS), t=0.008)
    K.cyl("crown", R * math.sin(t0) + 0.02, 0.03, canvas(CANVAS_D), loc=(0, 0, H * math.cos(t0) + 0.01), segs=12, bev=0.008, parent=P)
    pm = M(GUNMETAL, metal=0.5, wear=0.2)
    for k, a in enumerate((45, 135)):
        pl = K.torus(f"pole{k}", R + 0.012, 0.011, pm, loc=(0, 0, 0.01), rot=(90, 0, a), arc=180, segs=36, parent=P)
        pl.scale = (1, (H + 0.012) / (R + 0.012), 1)
        for s in (-1, 1):
            ar = math.radians(a)
            x, y = s * math.cos(ar) * (R + 0.02), s * math.sin(ar) * (R + 0.02)
            K.cyl("pin", 0.015, 0.06, pm, loc=(x, y, 0.02), segs=6, bev=0, parent=P)
            guy(P, dome(0.02)(((a + (180 if s < 0 else 0)) % 360) / 360, 0.35), (x * 1.75, y * 1.75, 0.1))
            peg(P, (x * 1.78, y * 1.78), yaw=a + (180 if s < 0 else 0), lean=-18)
    # patches in mismatched cloth, away from the door
    for k, (u0, v0, du, dv, col) in enumerate(((0.05, 0.25, 0.06, 0.25, SEA), (0.33, 0.5, 0.05, 0.3, MUSTARD), (0.45, 0.1, 0.08, 0.2, BRICK),
                                               (0.9, 0.55, 0.05, 0.25, BONE), (0.18, 0.62, 0.04, 0.2, STEEL))):
        sheet(P, f"patch{k}", lambda u, v, u0=u0, v0=v0, du=du, dv=dv: dome(0.012)(u0 + du * u, v0 + dv * v), 3, 2, canvas(col, wear=0.1), t=0.003)
    # the door: a dark arched opening on the front with its flap rolled up over it
    uc, hw = 0.75, 0.06

    def door(u, v):
        uu = uc + (u - 0.5) * 2 * hw
        top = 0.4 + 0.3 * ((uu - uc) / hw) ** 2
        return dome(0.014)(uu, top + (1 - top) * v)

    sheet(P, "door", door, 6, 4, K.mat("tent_dark", DARK, rough=1.0), t=0.003)
    a, b = Vector(dome(0.04)(uc - hw * 1.1, 0.36)), Vector(dome(0.04)(uc + hw * 1.1, 0.36))
    rod(P, "flap_roll", tuple(a), tuple(b), 0.04, canvas(CANVAS_D), segs=10)
    for f in (0.25, 0.75):
        p = a.lerp(b, f)
        rod(P, "tie", tuple(p), tuple(p + Vector((0, -0.02, -0.22))), 0.005, rope_mat(), segs=4)
    for k in range(10):
        a = 2 * math.pi * (k + 0.5) / 10
        if abs(a - 1.5 * math.pi) < 0.4:
            continue
        x, y = math.cos(a) * (R + 0.08), math.sin(a) * (R + 0.08)
        peg(P, (x, y), yaw=math.degrees(a), lean=-12)
    # boots by the door
    lea = K.mat("leather", LEATHER, rough=0.7, grime=0.4, noise_scale=8.0)
    for k, (x, yaw, tip) in enumerate(((-0.12, 8, 0), (0.12, -14, 70))):
        g = _grp(P, f"boot{k}", (x, -R - 0.3, 0), (0, tip, yaw))
        if tip:
            g.location.z = 0.05
        K.box("foot", (0.1, 0.27, 0.07), lea, loc=(0, -0.04, 0.035), bev=0.025, parent=g)
        K.box("leg", (0.1, 0.12, 0.2), lea, loc=(0, 0.04, 0.16), bev=0.03, parent=g)
        K.box("sole", (0.105, 0.28, 0.02), M(GRAPHITE, rough=0.9, wear=0.1), loc=(0, -0.04, 0.008), bev=0.006, parent=g)
    return P


def hammock_posts(seed=43):
    """A canvas hammock slung between two braced posts on gathered strings, a blanket over its side and a pillow."""
    rnd = random.Random(seed)
    P = _root("hammock")
    X = 1.85
    for s in (-1, 1):
        post(P, f"post{s}", (s * X, 0), 2.15, r=0.07, tilt=(0, s * 4))
        beam(P, f"brace{s}", (s * (X + 0.08), 0, 1.25), (s * (X + 0.75), 0, -0.05), 0.07, 0.06, wood(2))
        K.box(f"brace_foot{s}", (0.3, 0.2, 0.06), wood(1), loc=(s * (X + 0.72), 0, 0.02), bev=0.01, parent=P)
        bolt(P, (s * (X + 0.085), -0.075, 1.25))
        for k in range(3):
            K.torus("lash", 0.085, 0.011, rope_mat(), loc=(s * (X + 0.11), 0, 1.62 + k * 0.025), rot=(0, s * 4, 0), segs=12, parent=P)
        K.box(f"cap{s}", (0.17, 0.17, 0.04), wood(1), loc=(s * (X + 0.14), 0, 2.07), rot=(0, s * 4, 0), bev=0.01, parent=P)
    E = 0.98

    def bed(u, v, dz=0.0):
        x = -E + 2 * E * u
        sp = math.sin(math.pi * u)
        w = 0.1 + 0.42 * sp ** 0.5
        q = 2 * v - 1
        return (x, q * w, 1.12 - 0.42 * sp ** 0.9 + 0.2 * q * q * (w / 0.46) + dz)

    sheet(P, "bed", bed, 14, 6, canvas(SEA, grime=0.3), t=0.01)
    for s in (-1, 1):
        ring = (s * 1.3, 0, 1.22)
        K.torus(f"ring{s}", 0.04, 0.008, iron(), loc=ring, rot=(90, 0, 0), segs=12, parent=P)
        u = 0 if s < 0 else 1
        for k in range(6):
            p = bed(u, k / 5)
            rod(P, "string", (ring[0] - s * 0.03, 0, ring[2] - 0.02), p, 0.004, rope_mat(), segs=4)
        for dy in (-0.012, 0.012):
            rod(P, "rope", (ring[0] + s * 0.03, dy, ring[2] + 0.01), (s * (X + 0.02), dy, 1.635), 0.009, rope_mat(), segs=6)
    # a blanket over one side, hanging down past the edge
    def blanket(u, v):
        uu = 0.38 + 0.36 * u
        if v < 0.65:
            return bed(uu, 0.3 + 0.7 * v / 0.65, 0.012)
        x, y, z = bed(uu, 1.0, 0.012)
        k = (v - 0.65) / 0.35
        return (x + 0.02 * math.sin(7 * u), y + 0.03 + 0.03 * k, z - 0.38 * k)

    sheet(P, "blanket", blanket, 8, 7, canvas(MUSTARD, grime=0.25), t=0.006)
    lump(P, "pillow", 0.16, (-0.62, 0.0, bed(0.18, 0.5)[2] + 0.005), (1.0, 1.3, 0.45), canvas(BONE, grime=0.3), rnd, jitter=0.06, flat=0.2)
    # a tin mug on the ground under it
    K.cyl("mug", 0.04, 0.09, M(SEA, metal=0.3, wear=0.25), loc=(0.35, -0.55, 0.045), segs=12, bev=0.004, parent=P)
    return P


def sleeping_mats(seed=44):
    """A tarp pegged out on the ground with a sleeping bag, a foam mat under a rumpled blanket, a rolled bedroll and a
    small lantern."""
    rnd = random.Random(seed)
    P = _root("mats")
    TW, TD = 2.6, 2.2
    sheet(P, "tarp", lambda u, v: (-TW / 2 + TW * u, -TD / 2 + TD * v, 0.008 + 0.006 * math.sin(9 * u + 2) * math.sin(7 * v)), 10, 8,
          canvas(STEEL, grime=0.4), t=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.torus("grommet", 0.018, 0.005, tin(ZINC, wear=0.2), loc=(sx * (TW / 2 - 0.04), sy * (TD / 2 - 0.04), 0.014), segs=10, parent=P)
            peg(P, (sx * (TW / 2 - 0.04), sy * (TD / 2 - 0.04)), yaw=math.degrees(math.atan2(sy, sx)), lean=-20)
    # the sleeping bag, its top folded back to show the lining
    bag = canvas(OLIVE, grime=0.3)
    K.box("bag", (0.76, 1.95, 0.09), bag, loc=(-0.6, 0.0, 0.055), rot=(0, 0, -3), bev=0.04, parent=P)
    for k in range(5):
        K.box("quilt", (0.72, 0.012, 0.093), canvas("#5f6a4c"), loc=(-0.6 - 0.02 * (k - 2) * 0.05, -0.75 + k * 0.33, 0.056), rot=(0, 0, -3), bev=0, parent=P)
    prism(P, "lining", [(-0.36, 0), (0.36, 0), (0.36, 0.42)], 0.02, canvas(MUSTARD), loc=(-0.6, 0.55, 0.11), rot=(-90, 0, -3), plane="xz", bev=0.005)
    lump(P, "pillow", 0.17, (-0.6, 0.86, 0.1), (1.3, 0.8, 0.4), canvas(SACK_L, grime=0.3), rnd, jitter=0.06, flat=0.3)
    # a foam mat with a blanket rumpled over its lower two thirds
    K.box("mat", (0.58, 1.85, 0.025), canvas(SEA, grime=0.3), loc=(0.5, 0.05, 0.022), rot=(0, 0, 4), bev=0.008, parent=P)
    sheet(P, "blanket", lambda u, v: (0.5 - 0.36 + 0.72 * u - 0.07 * (v - 0.5), -0.85 + 1.3 * v,
                                      0.04 + 0.05 * math.sin(math.pi * u) ** 0.5 + 0.025 * math.sin(11 * v + 3 * u) * math.sin(math.pi * u)),
          8, 8, canvas(BRICK, grime=0.25), t=0.008)
    # a bedroll rolled and strapped at the foot of the tarp
    K.cyl("roll", 0.12, 0.7, canvas(CANVAS_D), loc=(0.55, -0.92, 0.13), rot=(0, 90, 2), segs=14, bev=0.02, parent=P)
    for k in range(2):
        K.cyl("roll_end", 0.1, 0.004, canvas(OLIVE), loc=(0.55 + (k * 2 - 1) * 0.351, -0.92, 0.13), rot=(0, 90, 2), segs=14, bev=0, parent=P)
        K.torus("strap", 0.123, 0.01, K.mat("leather", LEATHER, rough=0.7, grime=0.4), loc=(0.55 + (k * 2 - 1) * 0.2, -0.92, 0.13), rot=(0, 90, 0), segs=16, parent=P)
    # a small hurricane lantern (cold) and a tin mug
    lx, ly = -1.05, -0.9
    K.cyl("lamp_base", 0.07, 0.06, M(SEA, metal=0.4, wear=0.25), loc=(lx, ly, 0.04), segs=12, bev=0.006, parent=P)
    K.cyl("lamp_glass", 0.055, 0.13, K.mat("lampglass", "#c9c4ae", rough=0.15), loc=(lx, ly, 0.135), segs=12, r2=0.045, bev=0, parent=P)
    K.cyl("lamp_cap", 0.06, 0.05, M(SEA, metal=0.4, wear=0.25), loc=(lx, ly, 0.225), segs=12, r2=0.03, bev=0.004, parent=P)
    K.torus("lamp_bail", 0.065, 0.004, iron(), loc=(lx, ly, 0.23), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    for k in range(3):
        a = math.radians(120 * k)
        rod(P, "lamp_guard", (lx + math.cos(a) * 0.065, ly + math.sin(a) * 0.065, 0.07), (lx + math.cos(a) * 0.055, ly + math.sin(a) * 0.055, 0.2), 0.004, iron())
    K.cyl("mug", 0.04, 0.09, tin(ZINC, wear=0.2), loc=(-0.95, -0.7, 0.053), segs=12, bev=0.004, parent=P)
    return P


# ================================================================== cooking and washing
def camp_kitchen(seed=45):
    """A plank counter on posts with a corrugated splashback: a gas ring with a pot on it fed from a bottle on the
    shelf below, a water jug with a tap over a tin basin, a board with a loaf, pans hanging under a shelf of jars."""
    rnd = random.Random(seed)
    P = _root("kitchen")
    W, D, H = 1.8, 0.6, 0.9
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = H - 0.04 if sy < 0 else 1.78
            K.box("leg", (0.08, 0.08, h), wood(3, wear=0.1), loc=(sx * (W / 2 - 0.06), sy * (D / 2 - 0.05), h / 2), rot=(0, 0, rnd.uniform(-2, 2)), bev=0, parent=P)
    for k in range(4):
        plank(P, f"top{k}", (W + 0.06 + rnd.uniform(-0.03, 0.03), 0.148, 0.04), (rnd.uniform(-0.015, 0.015), -D / 2 + 0.075 + k * 0.15, H - 0.02), shade=k)
    for sy in (-1, 1):
        plank(P, f"apron{sy}", (W - 0.04, 0.03, 0.1), (0, sy * (D / 2 - 0.02), H - 0.1), shade=3)
        plank(P, f"rail{sy}", (W - 0.1, 0.05, 0.05), (0, sy * (D / 2 - 0.05), 0.2), shade=2)
    for k in range(3):
        plank(P, f"shelf{k}", (W - 0.1, 0.16, 0.025), (0, -0.18 + k * 0.18, 0.235), shade=k + 1)
    # splashback: a zinc sheet nailed across the back posts
    corrugated(P, "splash", W - 0.06, 0.6, tin(ZINC, wear=0.2, grime=0.5), loc=(0, D / 2 - 0.1, H + 0.3), rot=(90, 0, 0), pitch=0.15, amp=0.016)
    for x in (-0.84, 0.84):
        for z in (H + 0.08, H + 0.52):
            nail(P, (x, D / 2 - 0.12, z))
    plank(P, "upper", (W + 0.04, 0.06, 0.09), (0, D / 2 - 0.05, 1.72), shade=2)
    plank(P, "shelf_top", (W - 0.02, 0.2, 0.03), (0, D / 2 - 0.16, 1.5), shade=1)
    for x in (-0.6, 0.6):
        beam(P, f"bracket{x}", (x, D / 2 - 0.08, 1.33), (x, D / 2 - 0.22, 1.48), 0.04, 0.04, wood(3))
    for k, (x, col) in enumerate(((-0.75, LEAF), (-0.6, MUSTARD), (-0.44, APPLE), (0.2, BONE), (0.36, BONE))):
        K.cyl(f"jar{k}", 0.05, 0.15, M(col, wear=0.05, scratches=0, rough=0.4), loc=(x, D / 2 - 0.16, 1.59), segs=10, bev=0, parent=P)
        K.cyl(f"jar_lid{k}", 0.045, 0.02, tin(ZINC, wear=0.2), loc=(x, D / 2 - 0.16, 1.675), segs=10, bev=0, parent=P)
    K.cyl("tin_can", 0.06, 0.16, M(SEA, metal=0.4, wear=0.25), loc=(0.62, D / 2 - 0.16, 1.595), segs=12, bev=0.005, parent=P)
    # utensils on a rail under the shelf
    rod(P, "hook_rail", (-0.8, D / 2 - 0.24, 1.43), (0.8, D / 2 - 0.24, 1.43), 0.008, iron())
    gm = M(GUNMETAL, metal=0.6, wear=0.25, grime=0.4)
    py = D / 2 - 0.24
    K.cyl("pan", 0.13, 0.025, gm, loc=(-0.25, py - 0.01, 1.12), rot=(90, 0, 0), segs=16, bev=0.006, parent=P)
    rod(P, "pan_handle", (-0.25, py - 0.01, 1.25), (-0.25, py, 1.42), 0.012, iron())
    rod(P, "ladle", (0.05, py, 1.42), (0.05, py - 0.01, 1.16), 0.006, tin(ZINC, wear=0.2))
    K.sphere("ladle_cup", 0.045, tin(ZINC, wear=0.2), loc=(0.05, py - 0.02, 1.14), scale=(1, 1, 0.6), parent=P)
    rod(P, "spatula", (-0.02, py, 1.42), (-0.02, py, 1.2), 0.005, wood(2, wear=0.1))
    K.box("spatula_blade", (0.06, 0.004, 0.08), tin(ZINC, wear=0.2), loc=(-0.02, py, 1.17), bev=0, parent=P)
    # gas ring with a pot, its hose down to a gas bottle on the shelf
    gx = 0.45
    K.cyl("ring_base", 0.14, 0.05, iron(), loc=(gx, -0.05, H + 0.025), segs=16, bev=0.006, parent=P)
    K.cyl("burner", 0.07, 0.02, iron(wear=0.1), loc=(gx, -0.05, H + 0.06), segs=10, bev=0, parent=P)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        K.box("support", (0.1, 0.015, 0.03), iron(), loc=(gx + math.cos(a) * 0.1, -0.05 + math.sin(a) * 0.1, H + 0.065), rot=(0, 0, 90 + 120 * k), bev=0, parent=P)
    K.cyl("knob", 0.018, 0.03, M(GRAPHITE, wear=0.1), loc=(gx, -0.2, H + 0.025), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    K.cyl("pot", 0.13, 0.17, gm, loc=(gx, -0.05, H + 0.165), segs=14, r2=0.135, bev=0, parent=P)
    K.cyl("pot_lid", 0.135, 0.012, gm, loc=(gx + 0.02, -0.05, H + 0.258), rot=(0, 4, 0), segs=18, bev=0.003, parent=P)
    K.sphere("pot_knob", 0.02, M(GRAPHITE), loc=(gx + 0.02, -0.05, H + 0.275), parent=P)
    for s in (-1, 1):
        K.torus("pot_handle", 0.03, 0.006, gm, loc=(gx + s * 0.135, -0.05, H + 0.22), rot=(0, 90, 0), arc=180, segs=10, parent=P)
    hose = M(GRAPHITE, rough=0.6, wear=0.05)
    pts = [(gx + 0.1, 0.02, H + 0.03), (gx + 0.2, 0.15, H + 0.02), (gx + 0.32, 0.25, H - 0.08), (gx + 0.3, 0.2, 0.62), (gx + 0.1, 0.05, 0.58)]
    for k in range(len(pts) - 1):
        rod(P, "hose", pts[k], pts[k + 1], 0.01, hose, segs=6)
    lathe("bottle", [(0.001, 0.25), (0.13, 0.25), (0.13, 0.52), (0.1, 0.58), (0.03, 0.6), (0.001, 0.6)], M(SIGNAL, metal=0.3, wear=0.25, grime=0.4),
          segs=16, loc=(gx + 0.1, 0.0, 0), parent=P)
    K.cyl("valve", 0.025, 0.05, tin(ZINC, wear=0.2), loc=(gx + 0.1, 0.0, 0.62), segs=8, bev=0, parent=P)
    K.torus("collar", 0.07, 0.01, M(SIGNAL, metal=0.3, wear=0.2), loc=(gx + 0.1, 0.0, 0.66), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    # water jug with a tap, a basin under it, a board with a loaf and a knife
    jx = -0.68
    K.box("jug", (0.2, 0.32, 0.38), M(SEA, rough=0.5, wear=0.15, grime=0.3, scratches=0.2), loc=(jx, 0.0, H + 0.19), bev=0.03, parent=P)
    K.box("jug_handle", (0.04, 0.2, 0.04), M(SEA, rough=0.5, wear=0.1), loc=(jx, 0.02, H + 0.41), bev=0.012, parent=P)
    K.cyl("jug_cap", 0.03, 0.04, M(BONE, wear=0.1), loc=(jx, -0.11, H + 0.39), segs=10, bev=0.005, parent=P)
    K.cyl("tap", 0.012, 0.07, tin(ZINC, wear=0.2), loc=(jx, -0.19, H + 0.06), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    K.box("tap_lever", (0.012, 0.012, 0.04), M(BRICK, wear=0.1), loc=(jx, -0.21, H + 0.08), bev=0, parent=P)
    basin(P, "basin", (0.13, 0.13), (0.18, 0.18), 0.09, 0.006, tin(ZINC, wear=0.2), loc=(-0.32, -0.08, H), n=14)
    K.cyl("basin_water", 0.155, 0.004, K.mat("water", WATER, rough=0.05), loc=(-0.32, -0.08, H + 0.055), segs=16, bev=0, parent=P)
    K.box("board", (0.36, 0.24, 0.025), wood(2, wear=0.1), loc=(0.02, -0.08, H + 0.012), rot=(0, 0, 6), bev=0.006, parent=P)
    lump(P, "loaf", 0.1, (0.0, -0.06, H + 0.025), (1.4, 0.85, 0.6), K.mat("crust", "#a8834f", rough=0.9, noise_scale=10.0), rnd, jitter=0.05, flat=0.3)
    K.box("knife", (0.16, 0.025, 0.004), tin(ZINC, wear=0.1), loc=(0.12, -0.15, H + 0.027), rot=(0, 0, 20), bev=0, parent=P)
    K.box("knife_h", (0.09, 0.022, 0.016), wood(3), loc=(0.23, -0.11, H + 0.033), rot=(0, 0, 20), bev=0.004, parent=P)
    K.cyl("plates", 0.11, 0.04, tin(ZINC, wear=0.2), loc=(0.78, -0.05, H + 0.02), segs=14, bev=0.003, parent=P)
    # a crate of potatoes on the shelf, a bucket by the leg
    crate(P, "crate", (0.45, 0.35, 0.25), (-0.45, -0.03, 0.25), yaw=3, shade=2)
    for k in range(4):
        lump(P, "potato", 0.05, (-0.58 + (k % 3) * 0.13 + rnd.uniform(-0.02, 0.02), -0.09 + (k // 3) * 0.11, 0.47), (1.3, 1, 0.8),
             K.mat("potato", "#b89a68", rough=0.9, noise_scale=12.0), rnd, jitter=0.15, u=5, v=4)
    return P


def smoker_barrel(seed=46):
    """An oil drum cut lengthwise into a smoker: the lid hinged at the back and propped open on a grate of fish, a
    firebox door at one end and a chimney at the other, on a stand of pipe legs."""
    rnd = random.Random(seed)
    P = _root("smoker")
    G = _grp(P, "drum", (0, 0, 0.78), (0, 0, 90))   # drum axis along G's y, which is world -x; G's +x is the back
    r, hl = 0.29, 0.43
    paint = M(STEEL, metal=0.4, wear=0.25, grime=0.65, scratches=0.4)
    soot = K.mat("soot", SOOT, rough=0.9, grime=0.3, noise_scale=5.0)
    arc_shell(G, "body", r, math.pi, 2 * math.pi, -hl, hl, paint, t=0.01, n=14)
    for s in (-1, 1):
        plate_xz("end", [(r * math.cos(a), r * math.sin(a)) for a in [math.pi + math.pi * i / 10 for i in range(11)]], 0.01, paint,
                 loc=(0, s * hl, 0), parent=G, bev=0)
        K.torus("hoop", r + 0.006, 0.012, M(RUST, metal=0.4, wear=0.2), loc=(0, s * (hl - 0.05), 0), rot=(-90, 0, 0), arc=180, segs=20, parent=G)
    lid = _grp(G, "lid", (r, 0, 0), (0, 32, 0))
    arc_shell(lid, "lid", r + 0.004, 0, math.pi, -hl, hl, paint, t=0.01, n=14, loc=(-r, 0, 0))
    for s in (-1, 1):
        plate_xz("lid_end", [(r * math.cos(a), r * math.sin(a)) for a in [math.pi * i / 10 for i in range(11)]], 0.01, paint,
                 loc=(-r, s * hl, 0), parent=lid, bev=0)
        K.box("hinge", (0.04, 0.08, 0.02), iron(), loc=(0.0, s * 0.25, 0.01), bev=0.003, parent=lid)
    hx, hz = -r + (r + 0.05) * math.cos(math.radians(140)), (r + 0.05) * math.sin(math.radians(140))
    rod(lid, "handle", (hx, -0.14, hz), (hx, 0.14, hz), 0.012, wood(3, wear=0.1), segs=8)
    for s in (-1, 1):
        rod(lid, "standoff", (hx + 0.03, s * 0.12, hz - 0.03), (hx, s * 0.12, hz), 0.006, iron())
    K.cyl("gauge", 0.035, 0.02, tin(ZINC, wear=0.2), loc=(-r - 0.02, 0, r * 0.98 + 0.012), segs=12, bev=0.003, parent=lid)
    K.cyl("gauge_face", 0.028, 0.004, M(BONE, wear=0.05), loc=(-r - 0.02, 0, r * 0.98 + 0.024), segs=12, bev=0, parent=lid)
    # grate with fish split and laid on it
    for k in range(7):
        x = -0.2 + k * 0.066
        rod(G, "grate", (x, -hl + 0.04, 0.04), (x, hl - 0.04, 0.04), 0.005, iron())
    fm = M(FISH_D, rough=0.6, wear=0.05, scratches=0, grime=0.3)
    for k in range(4):
        lump(G, f"fillet{k}", 0.09, (-0.06 + (k % 2) * 0.12, -0.27 + k * 0.18, 0.05), (0.7, 2.0, 0.22), fm, rnd, jitter=0.05, flat=0.2, u=8, v=5)
    K.box("soot_floor", (0.4, 0.8, 0.01), soot, loc=(0, 0, -r + 0.03), bev=0, parent=G)
    # firebox door at the near end, chimney at the far end
    K.box("door", (0.18, 0.012, 0.13), iron(), loc=(0, -hl - 0.008, -0.13), bev=0.004, parent=G)
    K.box("door_latch", (0.03, 0.02, 0.06), iron(), loc=(-0.07, -hl - 0.02, -0.13), bev=0.003, parent=G)
    for k in range(3):
        K.box("vent", (0.02, 0.006, 0.05), soot, loc=(-0.03 + k * 0.035, -hl - 0.015, -0.13), bev=0, parent=G)
    cz = 0.08
    pipe = M(GRAPHITE, metal=0.5, wear=0.2, grime=0.5)
    rod(G, "flue_stub", (0.1, hl - 0.02, cz), (0.1, hl + 0.12, cz), 0.055, pipe, segs=12)
    K.sphere("flue_elbow", 0.056, pipe, loc=(0.1, hl + 0.12, cz), parent=G)
    rod(G, "flue", (0.1, hl + 0.12, cz), (0.1, hl + 0.12, 1.15), 0.055, pipe, segs=12)
    K.cyl("flue_cap", 0.1, 0.07, pipe, loc=(0.1, hl + 0.12, 1.24), segs=12, r2=0.015, bev=0, parent=G)
    for k in range(3):
        a = math.radians(120 * k)
        rod(G, "cap_leg", (0.1 + math.cos(a) * 0.05, hl + 0.12 + math.sin(a) * 0.05, 1.15), (0.1 + math.cos(a) * 0.06, hl + 0.12 + math.sin(a) * 0.06, 1.21), 0.004, pipe)
    rod(G, "damper", (0.04, hl + 0.12, 0.8), (0.16, hl + 0.12, 0.8), 0.005, iron())
    K.box("damper_key", (0.015, 0.05, 0.03), iron(), loc=(0.17, hl + 0.12, 0.8), bev=0, parent=G)
    # stand: four pipe legs and a shelf
    for sx in (-1, 1):
        for sy in (-1, 1):
            rod(G, "leg", (sx * 0.17, sy * 0.3, -0.2), (sx * 0.3, sy * 0.4, -0.78), 0.018, tin(GRAPHITE, wear=0.15), segs=8)
            K.box("foot", (0.07, 0.07, 0.01), iron(), loc=(sx * 0.3, sy * 0.4, -0.775), bev=0, parent=G)
        rod(G, "shelf_rail", (sx * 0.255, -0.37, -0.5), (sx * 0.255, 0.37, -0.5), 0.012, iron())
        rod(G, "saddle", (sx * 0.17, -0.3, -0.2), (sx * 0.17, 0.3, -0.2), 0.012, iron())
    for k in range(4):
        plank(G, f"shelf{k}", (0.11, 0.78, 0.02), (-0.18 + k * 0.12, 0, -0.48), shade=k)
    K.box("chips_bag", (0.2, 0.2, 0.08), canvas(SACK), loc=(0.0, -0.15, -0.43), rot=(0, 0, 12), bev=0.035, parent=G)
    # a few split logs on the ground by the firebox
    for k in range(3):
        g = _split_log(P, f"log{k}", (0.75 + k * 0.03, -0.25 + k * 0.17, 0.06 + (0.1 if k == 2 else 0)), 0.07, 0.42, rnd.uniform(0, 360), bark(), heart(), 4)
        g.rotation_euler[2] = math.radians(rnd.uniform(70, 110))
    return P


def fish_drying_rack(seed=47):
    """Two lashed A-frames carrying a ridge pole and two rails, split fish hung by their tails along them, a basket
    of more below."""
    rnd = random.Random(seed)
    P = _root("fish_rack")
    pole = wood(3, wear=0.05)
    zx, X = 1.75, 1.15
    for x in (-X, X):
        for s in (-1, 1):
            rod(P, f"leg{x}{s}", (x + rnd.uniform(-0.03, 0.03), s * 0.62, -0.03), (x, -s * 0.08, zx + 0.2), 0.035, pole, segs=7)
        for k in range(3):
            K.torus("lash", 0.05, 0.009, rope_mat(), loc=(x, 0, zx - 0.02 + k * 0.025), rot=(90, 0, 0), segs=12, parent=P)
    rod(P, "ridge", (-X - 0.2, 0, zx + 0.07), (X + 0.2, 0, zx + 0.06), 0.035, pole, segs=7)
    zr = 1.12
    yr = 0.62 * (1 - (zr + 0.03) / (zx + 0.03)) + 0.06
    for s in (-1, 1):
        rod(P, f"rail{s}", (-X - 0.15, s * yr, zr), (X + 0.15, s * yr, zr + rnd.uniform(-0.02, 0.02)), 0.028, pole, segs=7)
    mats = [M(FISH, rough=0.45, wear=0.05, scratches=0, grime=0.2), M(FISH_D, rough=0.6, wear=0.05, scratches=0, grime=0.25)]

    def fish(x, y, z, m):
        drop = rnd.uniform(0.05, 0.1)
        rod(P, "string", (x, y, z), (x, y, z - drop), 0.003, rope_mat(), segs=4)
        g = _grp(P, "fish", (x, y, z - drop), (rnd.uniform(-4, 4), rnd.uniform(-4, 4), rnd.uniform(-25, 25)))
        L = rnd.uniform(0.3, 0.4)
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=5, radius=1.0)
        for v in bm.verts:
            c = v.co
            taper = 0.55 + 0.45 * (1 - (c.z + 1) / 2) ** 0.6   # narrower towards the tail at the top
            v.co = Vector((c.x * 0.065 * taper, c.y * 0.018, c.z * L / 2))
        body = K.place(K.from_bm("body", bm, m, smooth=True), (0, 0, -0.07 - L / 2), (0, 0, 0), g)
        prism(g, "tail", [(0, -0.07), (-0.06, 0.0), (0.06, 0.0)], 0.006, m, loc=(0, 0, -0.005), bev=0)
        return body

    for k in range(6):
        fish(-0.95 + k * 0.38 + rnd.uniform(-0.04, 0.04), 0, zx + 0.03, mats[k % 2])
    for s in (-1, 1):
        for k in range(5):
            fish(-0.85 + k * 0.42 + rnd.uniform(-0.05, 0.05), s * yr, zr - 0.028, mats[(k + (s > 0)) % 2])
    # a basket of fresh fish and a gutting board on a stump
    basin(P, "basket", (0.2, 0.15), (0.26, 0.2), 0.2, 0.015, K.mat("wicker", "#b8955c", rough=0.95, grime=0.3, noise_scale=20.0), loc=(0.6, -0.9, 0), e=3)
    for k in range(4):
        lump(P, "fresh", 0.1, (0.52 + (k % 2) * 0.14, -0.94 + (k // 2) * 0.08, 0.18), (1.6, 0.35, 0.3), mats[0], rnd, jitter=0.05,
             rot=(0, 0, rnd.uniform(-20, 20)), u=8, v=5)
    K.cyl("stump", 0.22, 0.45, bark(), loc=(-0.65, -0.95, 0.225), segs=10, bev=0.01, parent=P)
    K.cyl("stump_top", 0.2, 0.01, heart(), loc=(-0.65, -0.95, 0.45), segs=10, bev=0, parent=P)
    K.box("knife", (0.18, 0.03, 0.005), tin(ZINC, wear=0.1), loc=(-0.62, -0.93, 0.458), rot=(0, 0, 30), bev=0, parent=P)
    K.box("knife_h", (0.1, 0.025, 0.018), wood(3), loc=(-0.74, -0.99, 0.462), rot=(0, 0, 30), bev=0.004, parent=P)
    return P


def laundry_tub(seed=48):
    """A zinc washtub on a plank stand, a washboard propped in it, soap on the rail, a wrung shirt over the side and a
    pail under the stand."""
    rnd = random.Random(seed)
    P = _root("laundry")
    H = 0.55
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(P, "leg", (sx * 0.3, sy * 0.25, H), (sx * 0.36, sy * 0.3, 0), 0.06, 0.06, wood(3))
        plank(P, f"rail{sx}", (0.06, 0.62, 0.08), (sx * 0.3, 0, H - 0.04), shade=2)
        plank(P, f"low{sx}", (0.05, 0.6, 0.05), (sx * 0.34, 0, 0.18), shade=1)
    for k in range(4):
        plank(P, f"top{k}", (0.74, 0.14, 0.03), (0, -0.23 + k * 0.155, H + 0.015), shade=k)
    zinc = tin(ZINC, wear=0.25, grime=0.4)
    basin(P, "tub", (0.25, 0.25), (0.33, 0.33), 0.3, 0.008, zinc, loc=(0, 0, H + 0.03), n=28)
    K.torus("tub_rim", 0.333, 0.009, zinc, loc=(0, 0, H + 0.33), segs=28, parent=P)
    for z in (H + 0.12, H + 0.24):
        K.torus("tub_rib", 0.25 + 0.08 * (z - H - 0.03) / 0.3 + 0.004, 0.006, zinc, loc=(0, 0, z), segs=28, parent=P)
    for s in (-1, 1):
        K.torus("tub_handle", 0.045, 0.008, zinc, loc=(s * 0.34, 0, H + 0.28), rot=(90, 0, 90), arc=180, segs=10, parent=P).rotation_euler = (math.radians(-90), 0, math.radians(90 * s))
    K.cyl("water", 0.3, 0.004, K.mat("suds_water", "#6f8a86", rough=0.1), loc=(0, 0, H + 0.24), segs=24, bev=0, parent=P)
    suds = M(BONE, wear=0, grime=0.05, scratches=0, rough=0.6)
    for k in range(6):
        a = rnd.uniform(0, 6.28)
        lump(P, "suds", 0.05, (math.cos(a) * 0.17, math.sin(a) * 0.17, H + 0.24), (1.4, 1.1, 0.4), suds, rnd, jitter=0.2, u=6, v=4)
    # the washboard: a wooden frame round a ribbed zinc panel, propped in the tub
    wb = _grp(P, "washboard", (0.0, 0.12, H + 0.06), (-22, 0, 4))
    for s in (-1, 1):
        K.box("stile", (0.04, 0.025, 0.62), wood(2, wear=0.2), loc=(s * 0.15, 0, 0.31), bev=0.006, parent=wb)
    K.box("head", (0.34, 0.03, 0.08), wood(2, wear=0.2), loc=(0, 0, 0.58), bev=0.008, parent=wb)
    K.box("foot", (0.34, 0.025, 0.04), wood(2, wear=0.2), loc=(0, 0, 0.1), bev=0.006, parent=wb)
    corrugated(wb, "ribs", 0.26, 0.4, tin(ZINC, wear=0.15), loc=(0, 0, 0.33), rot=(90, 90, 0), pitch=0.03, amp=0.004, t=0.003)
    K.box("soap", (0.09, 0.05, 0.035), M(BONE, wear=0.05, scratches=0, rough=0.5), loc=(0.29, -0.31, H + 0.048), rot=(0, 0, 15), bev=0.01, parent=P)
    # a wrung shirt hanging over the far side of the stand
    sheet(P, "shirt", lambda u, v: (-0.36 - 0.02 * math.sin(6 * u), -0.15 + 0.3 * u, H + 0.03 - 0.4 * v + 0.03 * math.sin(math.pi * v)),
          4, 4, canvas(SEA, grime=0.2), t=0.006)
    sheet(P, "shirt_top", lambda u, v: (-0.36 + 0.06 * v, -0.15 + 0.3 * u, H + 0.032), 4, 1, canvas(SEA, grime=0.2), t=0.006)
    bucket(P, "pail", (0.05, 0.02, 0), r=0.13, h=0.24, water=False)
    return P


def bathtub_outdoor(seed=49):
    """A clawfoot tub raised on four brick piers over a fire pit, its enamel chipped and its belly sooted, a crate
    step, a towel over the rim and a pail beside it."""
    rnd = random.Random(seed)
    P = _root("bathtub")
    bm_ = [M(BRICK, rough=0.85, wear=0.15, grime=0.45, cracks=0.3, noise_scale=4.0), M(BRICK_D, rough=0.85, wear=0.15, grime=0.45, cracks=0.3, noise_scale=4.0),
           K.mat("soot_brick", SOOT, rough=0.9, grime=0.3, noise_scale=4.0)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.55, sy * 0.2
            bricks(P, f"pier{sx}{sy}", x - 0.16, x + 0.16, y - 0.16, y + 0.16, 0, 0.3, rnd, bm_, accents=0.8)
    # the fire pit between the walls
    K.disc("ash", 0.35, K.mat("ash", ASH, rough=1.0, noise_scale=6.0), (0, 0), z=0.01, segs=16, wobble=0.15, seed=seed).parent = P
    char = K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    for k, (yaw, L) in enumerate(((10, 0.6), (-25, 0.5), (60, 0.45))):
        g = _grp(P, f"brand{k}", (rnd.uniform(-0.1, 0.1), rnd.uniform(-0.05, 0.05), 0.05 + 0.04 * k), (0, rnd.uniform(-8, 8), yaw))
        K.cyl("b", 0.045, L, char, rot=(0, 90, 0), segs=7, bev=0.008, parent=g)
    lump(P, "embers", 0.14, (0, 0, 0.02), (1.4, 1.0, 0.25), char, rnd, jitter=0.2, flat=0.0)
    # the tub on its feet
    T = _grp(P, "tub", (0, 0, 0.33))
    enamel = M(BONE, rough=0.35, wear=0.3, grime=0.35, scratches=0.4)
    basin(T, "tub", (0.68, 0.27), (0.82, 0.37), 0.56, 0.03, enamel, loc=(0, 0, 0.1), e=2.6, n=36)
    K.box("soot_belly", (1.2, 0.4, 0.01), K.mat("soot", SOOT, rough=0.9, grime=0.3, noise_scale=5.0), loc=(0, 0, 0.098), bev=0, parent=T)
    K.cyl("water", 0.3, 0.004, K.mat("water", WATER, rough=0.05), loc=(0, 0, 0.5), segs=24, bev=0, parent=T).scale = (2.55, 1.1, 1)
    foot = M(GRAPHITE, metal=0.5, wear=0.25)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.55, sy * 0.2
            K.cyl("leg", 0.035, 0.1, foot, loc=(x, y, 0.06), segs=8, r2=0.05, bev=0.005, parent=T)
            K.sphere("claw", 0.045, foot, loc=(x + sx * 0.02, y, 0.012), scale=(1.3, 1.0, 0.5), parent=T)
    K.cyl("tap", 0.02, 0.12, tin(ZINC, wear=0.2), loc=(0.76, 0, 0.66), rot=(0, 90, 0), segs=8, bev=0, parent=T)
    K.cyl("plug_chain", 0.004, 0.25, iron(), loc=(0.7, 0.0, 0.53), rot=(0, 30, 0), segs=4, bev=0, parent=T)
    sheet(T, "towel", lambda u, v: (-0.2 + 0.4 * u, -0.37 - 0.02 - 0.03 * min(v * 3, 1) + 0.06 * max(0, 0.3 - v), 0.67 - 0.42 * max(v - 0.15, 0) / 0.85 + 0.02 * (v < 0.15)),
          4, 5, canvas(MUSTARD, grime=0.2), t=0.006)
    sheet(T, "towel_top", lambda u, v: (-0.2 + 0.4 * u, -0.37 + 0.12 * v, 0.68), 4, 1, canvas(MUSTARD, grime=0.2), t=0.006)
    crate(P, "step", (0.5, 0.35, 0.3), (0.2, 0.68, 0), yaw=-4, shade=1)
    bucket(P, "pail", (-1.05, -0.2, 0), r=0.14, h=0.27)
    for k in range(4):
        b = K.box("loose_brick", (0.215, 0.1, 0.065), bm_[k % 2], loc=(1.05 + rnd.uniform(-0.1, 0.1), -0.3 + k * 0.12, 0.033 + (0.065 if k == 3 else 0)),
                  rot=(0, 0, rnd.uniform(-30, 30)), bev=0.006, parent=P)
    return P


# ================================================================== living
def clothes_chest(seed=50):
    """A battered plank chest on runners: iron straps and corner caps, rope handles, a zinc patch over a split and a
    sleeve caught under its lid."""
    rnd = random.Random(seed)
    P = _root("chest")
    W, D, H = 0.95, 0.52, 0.42
    for y in (-0.18, 0.18):
        plank(P, f"runner{y}", (W - 0.06, 0.06, 0.04), (0, y, 0.02), shade=3)
    for k in range(3):
        z = 0.04 + 0.065 + k * 0.13
        for s in (-1, 1):
            plank(P, f"side{k}{s}", (W, 0.025, 0.125), (rnd.uniform(-0.005, 0.005), s * (D / 2 - 0.012), z), shade=k + (s > 0))
            plank(P, f"end{k}{s}", (0.025, D - 0.05, 0.125), (s * (W / 2 - 0.012), 0, z), shade=k + 2)
    K.box("floor", (W - 0.04, D - 0.04, 0.02), wood(1, wear=0.1), loc=(0, 0, 0.05), bev=0, parent=P)
    K.box("inner", (W - 0.05, D - 0.05, H - 0.06), K.mat("tent_dark", DARK, rough=1.0), loc=(0, 0, 0.04 + (H - 0.06) / 2), bev=0, parent=P)
    lid = _grp(P, "lid", (0, D / 2, 0.04 + H - 0.02), (-4, 0, 0))
    for k in range(4):
        plank(lid, f"lid{k}", (W + 0.02, 0.135, 0.03), (0, -D - 0.01 + 0.0675 + k * 0.135, 0.015 + 0.004 * (k % 2)), shade=k)
    strap = M(GRAPHITE, metal=0.5, wear=0.25, grime=0.5)
    for x in (-0.3, 0.3):
        K.box("lid_strap", (0.05, D + 0.04, 0.006), strap, loc=(x, -D / 2, 0.035), bev=0, parent=lid)
        K.box("lid_strap_f", (0.05, 0.006, 0.05), strap, loc=(x, -D - 0.02, 0.01), bev=0, parent=lid)
        K.box("strap_f", (0.05, 0.006, H - 0.02), strap, loc=(x, -D / 2 - 0.004, 0.04 + (H - 0.02) / 2), bev=0, parent=P)
        K.box("strap_b", (0.05, 0.006, H - 0.02), strap, loc=(x, D / 2 + 0.004, 0.04 + (H - 0.02) / 2), bev=0, parent=P)
        for z in (0.12, 0.36):
            nail(P, (x, -D / 2 - 0.009, z))
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("cap", (0.06, 0.06, 0.06), strap, loc=(sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), 0.07), bev=0.008, parent=P)
        K.torus("handle", 0.07, 0.012, rope_mat(), loc=(sx * (W / 2 + 0.02), 0, 0.3), rot=(0, 90 * sx, 0), arc=180, segs=12, parent=P).rotation_euler = (math.radians(-110), 0, math.radians(90 * sx))
        for y in (-0.07, 0.07):
            K.cyl("handle_block", 0.02, 0.03, wood(3), loc=(sx * (W / 2 + 0.012), y, 0.3), rot=(0, 90, 0), segs=8, bev=0.004, parent=P)
    # hasp and padlock (hanging open)
    K.box("hasp", (0.05, 0.008, 0.1), strap, loc=(0, -D - 0.022, -0.02), bev=0.002, parent=lid)
    K.box("staple", (0.03, 0.02, 0.03), strap, loc=(0, -D / 2 - 0.01, H - 0.05), bev=0.002, parent=P)
    K.box("lock", (0.06, 0.025, 0.07), tin(MUSTARD, wear=0.25), loc=(0.0, -D / 2 - 0.03, H - 0.12), rot=(0, 10, 0), bev=0.008, parent=P)
    K.torus("shackle", 0.02, 0.005, tin(ZINC, wear=0.2), loc=(0.0, -D / 2 - 0.03, H - 0.08), rot=(90, 0, 10), arc=180, segs=10, parent=P)
    patch(P, "patch", (0.22, 0.006, 0.12), (-0.18, -D / 2 - 0.006, 0.15), (0, 6, 0), ZINC)
    # a sleeve caught under the lid
    sheet(P, "sleeve", lambda u, v: (0.18 + 0.12 * u, -D / 2 - 0.01 - 0.03 * math.sin(math.pi * v), H + 0.01 - 0.25 * v + 0.01 * math.sin(math.pi * u)),
          3, 4, canvas(BRICK, grime=0.3), t=0.008)
    return P


def rocking_chair(seed=51):
    """A homemade rocking chair: curved rockers, a slatted back with one replaced slat in salvaged paint, a cushion and
    a blanket over its top rail."""
    rnd = random.Random(seed)
    P = _root("rocker")
    R = 1.1
    for s in (-1, 1):
        ys = [-0.42 + 0.92 * i / 10 for i in range(11)]
        bot = [(y, R - math.sqrt(R * R - y * y)) for y in ys]
        top = [(y, z + 0.06) for y, z in reversed(bot)]
        prism(P, f"rocker{s}", bot + top, 0.035, wood(2), loc=(s * 0.25, 0, 0), plane="yz", bev=0.006)
    fz = 0.06 + R - math.sqrt(R * R - 0.04)
    for s in (-1, 1):
        x = s * 0.25
        K.box(f"front_leg{s}", (0.045, 0.045, 0.64 - fz), wood(3), loc=(x, -0.2, fz + (0.64 - fz) / 2), bev=0.006, parent=P)
        beam(P, f"back_leg{s}", (x, 0.2, fz - 0.01), (x, 0.22, 0.46), 0.045, 0.045, wood(3), bev=0.006)
        beam(P, f"upright{s}", (x, 0.21, 0.42), (x, 0.4, 1.08), 0.045, 0.04, wood(3), bev=0.006)
        plank(P, f"seat_rail{s}", (0.04, 0.48, 0.07), (x, 0.0, 0.42), shade=1)
        K.box(f"arm{s}", (0.07, 0.6, 0.025), wood(1), loc=(x + s * 0.01, 0.0, 0.655), rot=(-2, 0, 0), bev=0.006, parent=P)
    for k in range(5):
        plank(P, f"seat{k}", (0.54, 0.09, 0.025), (0, -0.2 + k * 0.1, 0.465), shade=k)
    rod(P, "stretcher", (-0.25, 0.0, 0.14), (0.25, 0.0, 0.14), 0.016, wood(3, wear=0.1))
    rod(P, "stretcher_f", (-0.25, -0.2, 0.3), (0.25, -0.2, 0.3), 0.014, wood(3, wear=0.1))
    plank(P, "back_low", (0.5, 0.03, 0.06), (0, 0.25, 0.56), rot=(-16, 0, 0), shade=2)
    plank(P, "back_top", (0.58, 0.035, 0.1), (0, 0.395, 1.03), rot=(-16, 0, 0), shade=2)
    for k in range(5):
        x = -0.16 + k * 0.08
        mat = M(SEA, wear=0.3, grime=0.35) if k == 3 else wood(k)
        beam(P, f"slat{k}", (x, 0.26, 0.58), (x, 0.385, 0.99), 0.05, 0.015, mat, bev=0.004)
    lump(P, "cushion", 0.24, (0, -0.02, 0.48), (1.0, 0.95, 0.25), canvas(BRICK, grime=0.3), rnd, jitter=0.04, flat=0.2)
    def blanket(u, v):
        x = -0.22 + 0.44 * u + 0.03 * v
        if v < 0.5:  # down the back, behind the slats
            k = v / 0.5
            return (x, 0.43 + 0.05 * k, 1.07 - 0.3 * k + 0.015 * math.sin(8 * u))
        k = (v - 0.5) / 0.5
        return (x, 0.36 - 0.04 * k, 1.07 - 0.16 * k + 0.012 * math.sin(7 * u + 1))
    sheet(P, "blanket", blanket, 8, 6, canvas(SEA, grime=0.25), t=0.006)
    return P


def bench_plank(seed=52):
    """A crude bench: one thick plank on two splayed board legs, a stretcher, a nailed-on brace and a short board
    patched over a split."""
    rnd = random.Random(seed)
    P = _root("bench")
    L, H = 1.8, 0.45
    plank(P, "top", (L, 0.3, 0.06), (0, 0, H - 0.03), rot=(0, 0, 0.6), shade=2)
    for s in (-1, 1):
        x = s * 0.62
        prism(P, f"leg{s}", [(-0.15, 0), (-0.06, 0), (0, 0.05), (0.06, 0), (0.15, 0), (0.12, H - 0.06), (-0.12, H - 0.06)], 0.05, wood(3),
              loc=(x, 0, 0), plane="yz", bev=0.006)
        for y in (-0.08, 0.08):
            nail(P, (x + 0.02, y, H + 0.001), rot=(0, 0, 0))
    plank(P, "stretcher", (1.3, 0.04, 0.1), (0, 0.0, 0.18), shade=1)
    beam(P, "brace", (-0.6, -0.04, 0.12), (-0.2, -0.04, H - 0.07), 0.05, 0.03, wood(0))
    patch(P, "patch", (0.4, 0.012, 0.05), (0.25, -0.155, H - 0.03), (0, 0, 0.6), BRICK)
    K.box("split", (0.5, 0.006, 0.012), K.mat("tent_dark", DARK, rough=1.0), loc=(0.3, 0.06, H + 0.0005), rot=(0, 0, 3), bev=0, parent=P)
    K.cyl("mug", 0.04, 0.09, tin(ZINC, wear=0.2), loc=(-0.7, 0.05, H + 0.045), segs=12, bev=0.004, parent=P)
    return P


def stove_pot_belly(seed=53):
    """A cast iron pot-belly stove stood out in the open on flagstones, its flue up through a rain cap, guyed to
    three stakes, a kettle on its top and a scuttle of coal beside it."""
    rnd = random.Random(seed)
    P = _root("stove")
    for k, (x, y) in enumerate(((-0.3, -0.25), (0.28, -0.3), (-0.28, 0.28), (0.3, 0.26))):
        rock(P, f"flag{k}", 0.36, 0.06, (x, y, 0.03), rnd, mat=K.mat("flag", STONE, rough=0.9, grime=0.35, moss=0.15, cracks=0.5, noise_scale=3.0))
    ci = M(GRAPHITE, metal=0.5, wear=0.25, grime=0.5, scratches=0.4)
    z0 = 0.06
    for k in range(4):
        a = math.radians(45 + 90 * k)
        beam(P, "leg", (math.cos(a) * 0.17, math.sin(a) * 0.17, z0 + 0.15), (math.cos(a) * 0.25, math.sin(a) * 0.25, z0), 0.05, 0.04, ci)
    K.cyl("base", 0.27, 0.05, ci, loc=(0, 0, z0 + 0.17), segs=20, bev=0.01, parent=P)
    lathe("belly", [(0.001, z0 + 0.19), (0.22, z0 + 0.19), (0.27, z0 + 0.3), (0.29, z0 + 0.45), (0.26, z0 + 0.6), (0.2, z0 + 0.72), (0.001, z0 + 0.72)], ci,
          segs=24, parent=P)
    K.cyl("collar", 0.23, 0.08, ci, loc=(0, 0, z0 + 0.76), segs=20, bev=0.01, parent=P)
    K.cyl("top", 0.27, 0.04, ci, loc=(0, 0, z0 + 0.82), segs=20, bev=0.01, parent=P)
    K.torus("rail", 0.3, 0.012, tin(ZINC, wear=0.2), loc=(0, 0, z0 + 0.86), segs=24, parent=P)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        rod(P, "rail_post", (math.cos(a) * 0.26, math.sin(a) * 0.26, z0 + 0.83), (math.cos(a) * 0.3, math.sin(a) * 0.3, z0 + 0.86), 0.008, iron())
    for z in (z0 + 0.3, z0 + 0.6):
        K.torus("band", 0.29 if z < z0 + 0.5 else 0.27, 0.01, ci, loc=(0, 0, z), segs=24, parent=P)
    # the fire door with its handle and a draught spinner below
    K.box("door", (0.2, 0.06, 0.18), ci, loc=(0, -0.275, z0 + 0.45), bev=0.01, parent=P)
    K.box("door_glow", (0.12, 0.01, 0.04), K.mat("tent_dark", DARK, rough=1.0), loc=(0, -0.306, z0 + 0.47), bev=0, parent=P)
    rod(P, "door_handle", (0.08, -0.31, z0 + 0.45), (0.08, -0.36, z0 + 0.45), 0.008, tin(ZINC, wear=0.2))
    K.cyl("spinner", 0.06, 0.015, ci, loc=(0, -0.24, z0 + 0.27), rot=(80, 0, 0), segs=12, bev=0.003, parent=P)
    # the flue in three lengths with collars, a damper, a rain cap
    pipe = M(GUNMETAL, metal=0.5, wear=0.2, grime=0.55)
    top = 2.55
    K.cyl("flue", 0.075, top - (z0 + 0.84), pipe, loc=(0, 0, (top + z0 + 0.84) / 2), segs=14, bev=0, parent=P)
    for z in (z0 + 0.88, 1.5, 2.1):
        K.cyl("flue_collar", 0.085, 0.05, pipe, loc=(0, 0, z), segs=14, bev=0.005, parent=P)
    rod(P, "damper", (-0.12, 0, 1.25), (0.12, 0, 1.25), 0.005, iron())
    K.box("damper_key", (0.02, 0.06, 0.03), iron(), loc=(0.13, 0, 1.25), bev=0, parent=P)
    K.cyl("cap", 0.17, 0.1, pipe, loc=(0, 0, top + 0.13), segs=14, r2=0.02, bev=0, parent=P)
    for k in range(3):
        a = math.radians(120 * k + 30)
        rod(P, "cap_leg", (math.cos(a) * 0.07, math.sin(a) * 0.07, top - 0.02), (math.cos(a) * 0.1, math.sin(a) * 0.1, top + 0.09), 0.005, pipe)
    K.torus("guy_band", 0.08, 0.008, iron(), loc=(0, 0, 2.2), segs=14, parent=P)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        st = (math.cos(a) * 1.0, math.sin(a) * 1.0)
        guy(P, (math.cos(a) * 0.085, math.sin(a) * 0.085, 2.2), (st[0], st[1], 0.12), r=0.003)
        peg(P, st, yaw=math.degrees(a), lean=-15)
    # kettle on the top, scuttle of coal and a poker
    kz = z0 + 0.84
    lathe("kettle", [(0.001, kz), (0.1, kz), (0.12, kz + 0.06), (0.1, kz + 0.14), (0.04, kz + 0.17), (0.001, kz + 0.17)], M(SEA, metal=0.4, wear=0.25),
          segs=16, loc=(0.06, 0.04, 0), parent=P)
    K.cyl("spout", 0.018, 0.14, M(SEA, metal=0.4, wear=0.25), loc=(0.2, 0.04, kz + 0.1), rot=(0, 55, 0), segs=8, r2=0.012, bev=0, parent=P)
    K.torus("kettle_handle", 0.07, 0.008, iron(), loc=(0.06, 0.04, kz + 0.17), rot=(90, 0, 0), arc=180, segs=12, parent=P)
    basin(P, "scuttle", (0.13, 0.11), (0.16, 0.14), 0.24, 0.006, M(GUNMETAL, metal=0.5, wear=0.25, grime=0.5), loc=(0.55, -0.15, 0), rot=(0, 0, 20), e=3, n=20)
    lump(P, "coal", 0.13, (0.55, -0.15, 0.22), (1.1, 0.9, 0.4), K.mat("coal", "#22201e", rough=0.6, noise_scale=20.0), rnd, jitter=0.25, flat=0.0)
    rod(P, "poker", (0.4, 0.25, 0.0), (0.28, 0.2, 0.62), 0.008, iron())
    return P


def chimney_stack_free(seed=54):
    """A brick chimney still standing where its house burned away: the hearth and its sooted fireplace, the breast,
    the stack with a corbelled top and a pot, stubs of the house wall stepping down either side and bricks in the
    grass."""
    rnd = random.Random(seed)
    P = _root("chimney")
    mats = [M(BRICK, rough=0.85, wear=0.15, grime=0.45, moss=0.35, cracks=0.3, noise_scale=4.0),
            M(BRICK_D, rough=0.85, wear=0.15, grime=0.45, moss=0.35, cracks=0.3, noise_scale=4.0),
            M("#b45a48", rough=0.85, wear=0.15, grime=0.45, moss=0.35, cracks=0.3, noise_scale=4.0)]
    sootm = [K.mat("soot_brick", SOOT, rough=0.9, grime=0.3, noise_scale=4.0), K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)]
    W, D = 1.5, 0.9
    y0, y1 = -0.45, 0.45
    # the firebox: two jambs and a sooted back, an iron lintel, then the breast
    bricks(P, "jamb_l", -W / 2, -0.38, y0, y1, 0.0, 0.825, rnd, mats, faces=(-2, -1))
    bricks(P, "jamb_r", 0.38, W / 2, y0, y1, 0.0, 0.825, rnd, mats, faces=(-2, 1))
    bricks(P, "fireback", -0.38, 0.38, 0.15, y1, 0.0, 0.825, rnd, sootm, accents=0.0)
    K.box("lintel", (0.95, 0.08, 0.08), iron(wear=0.15), loc=(0, y0 + 0.04, 0.865), bev=0.006, parent=P)
    bricks(P, "breast", -W / 2, W / 2, y0, y1, 0.825, 2.1, rnd, mats, accents=0.6)
    # the shoulders step in, a few courses at a time, to the stack
    for k in range(4):
        f = (k + 1) / 5
        hw, hd = W / 2 + (0.38 - W / 2) * f, D / 2 + (0.3 - D / 2) * f
        bricks(P, f"shoulder{k}", -hw, hw, -hd, hd, 2.1 + k * 0.125, 2.1 + (k + 1) * 0.125, rnd, mats, accents=0.0)
    bricks(P, "stack", -0.38, 0.38, -0.3, 0.3, 2.6, 4.6, rnd, mats, accents=0.55)
    bricks(P, "corbel", -0.43, 0.43, -0.35, 0.35, 4.6, 4.75, rnd, mats, accents=0.0)
    bricks(P, "crown", -0.38, 0.38, -0.3, 0.3, 4.75, 4.9, rnd, mats, accents=0.3)
    K.box("cap", (0.8, 0.64, 0.05), K.mat("concrete", CONCRETE, rough=0.9, grime=0.5, moss=0.6, cracks=0.4), loc=(0, 0, 4.925), bev=0.01, parent=P)
    lathe("pot", [(0.11, 4.95), (0.13, 4.97), (0.11, 5.2), (0.13, 5.24), (0.13, 5.28), (0.09, 5.28), (0.08, 4.97)], mats[2], segs=14, parent=P)
    # inside the hearth: ash, a fallen log, the crane arm with its hook
    K.box("hearth_floor", (0.76, 0.6, 0.02), K.mat("ash", ASH, rough=1.0, noise_scale=6.0), loc=(0, -0.15, 0.01), bev=0, parent=P)
    K.cyl("hearth_log", 0.06, 0.5, K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0), loc=(0.05, -0.05, 0.08), rot=(0, 90, 12), segs=8, bev=0.01, parent=P)
    rod(P, "crane_post", (-0.33, 0.08, 0.15), (-0.33, 0.08, 0.75), 0.012, iron())
    rod(P, "crane_arm", (-0.33, 0.08, 0.7), (0.05, -0.18, 0.7), 0.01, iron())
    rod(P, "crane_hook", (0.05, -0.18, 0.7), (0.05, -0.18, 0.5), 0.006, iron())
    K.torus("hook", 0.025, 0.006, iron(), loc=(0.05, -0.18, 0.48), rot=(90, 0, 0), arc=270, segs=10, parent=P)
    # the hearthstone, cracked in two
    st = K.mat("stone", STONE, rough=0.9, grime=0.35, moss=0.6, cracks=0.7, noise_scale=3.0)
    K.box("hearthstone_a", (0.95, 0.5, 0.08), st, loc=(-0.48, -0.72, 0.035), rot=(0, 0, 1), bev=0.02, parent=P)
    K.box("hearthstone_b", (0.9, 0.5, 0.08), st, loc=(0.47, -0.73, 0.025), rot=(1.5, 0, -2), bev=0.02, parent=P)
    # stubs of the house wall, broken in steps
    for s, steps in ((-1, (0.5, 0.3)), (1, (1.35, 0.85, 0.4))):
        x = s * W / 2
        for k, h in enumerate(steps):
            xa, xb = sorted((x + s * 0.32 * k, x + s * 0.32 * (k + 1)))
            bricks(P, f"wall{s}{k}", xa, xb, 0.05, 0.3, 0.0, h, rnd, mats, accents=0.6, faces=(-2, 2))
    # bricks in the grass and a charred joist
    for k in range(9):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(1.1, 1.9)
        x, y = math.cos(a) * d, math.sin(a) * d * 0.8 - 0.3
        K.box("rubble", (0.215, 0.1, 0.065), mats[k % 3], loc=(x, y, 0.03), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180)), bev=0, parent=P)
    beam(P, "joist", (1.0, -0.9, 0.0), (0.76, -0.2, 0.95), 0.1, 0.14, K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0), bev=0.02)
    return P


# ================================================================== working
def firewood_rack(seed=55):
    """A steel hoop rack on two feet, packed two thirds full of split logs, a few more on the ground."""
    rnd = random.Random(seed)
    P = _root("hoop_rack")
    R, zc, dep = 0.66, 0.72, 0.5
    steel = M(GRAPHITE, metal=0.55, wear=0.2, grime=0.4)
    for s in (-1, 1):
        K.torus(f"hoop{s}", R, 0.018, steel, loc=(0, s * dep / 2, zc), rot=(90, 0, 0), segs=40, parent=P)
    for k in range(8):
        a = math.radians(-90 + 45 * k)
        rod(P, "cross", (math.cos(a) * R, -dep / 2, zc + math.sin(a) * R), (math.cos(a) * R, dep / 2, zc + math.sin(a) * R), 0.014, steel, segs=6)
    for s in (-1, 1):
        K.box(f"foot{s}", (0.05, 0.7, 0.03), steel, loc=(s * 0.32, 0, 0.015), bev=0.004, parent=P)
        rod(P, "foot_post", (s * 0.32, 0, 0.03), (s * 0.3, 0, zc - math.sqrt(R * R - 0.09)), 0.014, steel, segs=6)
    b, c = bark(), heart()
    rr, pitch = 0.075, 0.15
    rows = 0
    z = zc - R + rr + 0.02
    while z < zc + 0.28:
        half = math.sqrt(max(R * R - (z - zc) ** 2, 0)) - rr - 0.01
        n = int(2 * half / pitch) + 1
        off = (rows % 2) * pitch / 2
        for i in range(n):
            x = -half + i * pitch + off * (1 if n * pitch < 2 * half else 0)
            if abs(x) > half + 0.001:
                continue
            _split_log(P, f"log{rows}{i}", (x + rnd.uniform(-0.01, 0.01), rnd.uniform(-0.03, 0.03), z), rr * rnd.uniform(0.9, 1.1), dep + 0.04,
                       rnd.uniform(0, 360), b, c, rnd.choice((4, 5)))
        z += pitch * 0.86
        rows += 1
    for k in range(3):
        g = _split_log(P, f"loose{k}", (0.8 + k * 0.05, -0.35 + k * 0.2, 0.07), 0.075, 0.5, rnd.uniform(0, 360), b, c, 4)
        g.rotation_euler[2] = math.radians(rnd.uniform(-30, 30))
    return P


def chopping_block(seed=56):
    """A chopping stump with an axe left in it, chips and splits scattered round it and a half-split round waiting."""
    rnd = random.Random(seed)
    P = _root("chopping")
    b, c = bark(), heart()
    K.cyl("stump", 0.32, 0.5, b, loc=(0, 0, 0.25), segs=14, r2=0.29, bev=0.02, parent=P)
    K.cyl("top", 0.28, 0.012, c, loc=(0, 0, 0.502), segs=14, bev=0, parent=P)
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        limb(P, f"root{k}", (math.cos(a) * 0.27, math.sin(a) * 0.27, 0.08), math.degrees(a), -22, 0.28, 0.07, 0.02, b)
    axe = _grp(P, "axe", (0.04, -0.02, 0.5), (0, -30, 25))
    K.box("axe_head", (0.06, 0.03, 0.17), M(GRAPHITE, metal=0.6, wear=0.25), loc=(0, 0, 0.04), bev=0.006, parent=axe)
    K.box("axe_edge", (0.062, 0.012, 0.02), tin(ZINC, wear=0.1), loc=(0, 0, -0.02), bev=0, parent=axe)
    K.cyl("axe_handle", 0.018, 0.72, wood(2, wear=0.05), loc=(-0.38, 0, 0.1), rot=(0, 90, 0), segs=8, bev=0, parent=axe)
    # a round half split, lying by the block
    K.cyl("round", 0.2, 0.36, b, loc=(-0.6, 0.25, 0.2), segs=12, bev=0.01, parent=P)
    K.cyl("round_top", 0.18, 0.01, c, loc=(-0.6, 0.25, 0.381), segs=12, bev=0, parent=P)
    K.box("crack", (0.38, 0.02, 0.01), K.mat("tent_dark", DARK, rough=1.0), loc=(-0.6, 0.25, 0.382), rot=(0, 0, 30), bev=0, parent=P)
    for k in range(5):
        g = _split_log(P, f"split{k}", (rnd.uniform(-0.2, 0.75), rnd.uniform(-0.75, -0.35), 0.06), 0.07, 0.36, rnd.uniform(0, 360), b, c, 4)
        g.rotation_euler[2] = math.radians(rnd.uniform(0, 180))
    for k in range(22):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(0.38, 0.9)
        K.box("chip", (rnd.uniform(0.04, 0.09), rnd.uniform(0.025, 0.05), 0.012), c, loc=(math.cos(a) * d, math.sin(a) * d, 0.006),
              rot=(rnd.uniform(-10, 10), rnd.uniform(-10, 10), rnd.uniform(0, 180)), bev=0, parent=P)
    return P


def sawhorse_pair(seed=57):
    """Two sawhorses with a plank across them, a saw cut half through its end, the offcut and sawdust below."""
    rnd = random.Random(seed)
    P = _root("sawhorses")
    H = 0.72
    for k, x in enumerate((-0.7, 0.75)):
        yaw = rnd.uniform(-5, 5)
        g = _grp(P, f"horse{k}", (x, 0, 0), (0, 0, yaw))
        K.box("beam", (0.1, 0.95, 0.09), wood(1), loc=(0, 0, H - 0.045), bev=0.01, parent=g)
        for sy in (-1, 1):
            for sx in (-1, 1):
                beam(g, "leg", (sx * 0.04, sy * 0.36, H - 0.06), (sx * 0.24, sy * 0.44, 0), 0.06, 0.04, wood(3))
            beam(g, "spreader", (-0.18, sy * 0.42, 0.18), (0.18, sy * 0.42, 0.18), 0.08, 0.02, wood(2))
        K.box("gusset", (0.3, 0.02, 0.14), wood(0), loc=(0, -0.38, H - 0.17), bev=0.004, parent=g)
    pl = plank(P, "board", (2.4, 0.25, 0.045), (0.05, 0.02, H + 0.023), rot=(0, 0, -2), shade=0)
    K.box("kerf", (0.006, 0.12, 0.046), K.mat("tent_dark", DARK, rough=1.0), loc=(0.9, -0.05, 0), bev=0, parent=pl)
    # the saw stood in its cut across the board, toe down
    saw = _grp(P, "saw", (0.9, -0.08, H + 0.1), (0, 0, 88))
    tilt = _grp(saw, "saw_tilt", rot=(0, -22, 0))
    prism(tilt, "blade", [(-0.32, -0.03), (0.25, -0.06), (0.25, 0.06), (-0.32, 0.02)], 0.003, tin(ZINC, wear=0.1), plane="xz", bev=0)
    prism(tilt, "handle", [(0.24, -0.06), (0.38, -0.07), (0.42, 0.09), (0.24, 0.07)], 0.025, wood(1, wear=0.1), plane="xz", bev=0.004)
    plank(P, "offcut", (0.5, 0.25, 0.045), (1.45, -0.5, 0.023), rot=(0, 0, 35), shade=0)
    lump(P, "sawdust", 0.25, (0.9, -0.05, 0.0), (1.0, 0.8, 0.08), K.mat("sawdust", "#d2b07a", rough=1.0, noise_scale=25.0), rnd, jitter=0.15, flat=0.0)
    return P


def tool_wall(seed=58):
    """A free-standing plank wall on braced skids, hung with a settler's tools on nails: shovel, rake, saws, hammer,
    wrench and rope, with a shelf of tins along its top."""
    rnd = random.Random(seed)
    P = _root("tool_wall")
    W, H = 2.2, 2.0
    for s in (-1, 1):
        x = s * (W / 2 - 0.05)
        K.box(f"post{s}", (0.09, 0.09, H + 0.05), wood(3), loc=(x, 0.06, (H + 0.05) / 2), bev=0.01, parent=P)
        plank(P, f"skid{s}", (0.1, 1.1, 0.08), (x, 0.2, 0.04), shade=3)
        beam(P, f"brace{s}", (x, 0.1, 1.2), (x, 0.72, 0.06), 0.07, 0.05, wood(2))
    plank(P, "rail_top", (W, 0.05, 0.1), (0, 0.06, H - 0.1), shade=2)
    plank(P, "rail_low", (W, 0.05, 0.1), (0, 0.06, 0.35), shade=2)
    x = -W / 2 + 0.1
    k = 0
    while x < W / 2 - 0.05:
        w = rnd.uniform(0.17, 0.24)
        w = min(w, W / 2 - x)
        plank(P, f"board{k}", (w - 0.008, 0.022, H - 0.15), (x + w / 2, 0.025, 0.15 + (H - 0.15) / 2 + rnd.uniform(-0.02, 0.02)), shade=k)
        x += w
        k += 1
    corrugated(P, "patch", 0.5, 0.6, tin(RUST, wear=0.15), loc=(0.62, 0.005, 0.55), rot=(90, 0, 0), pitch=0.08, amp=0.01)
    K.box("drip", (W + 0.2, 0.25, 0.03), wood(1), loc=(0, -0.03, H + 0.06), rot=(-8, 0, 0), bev=0.006, parent=P)
    zinc = tin(ZINC, wear=0.2)
    ir = iron()
    handle = wood(2, wear=0.05)
    fy = -0.02
    # shovel
    rod(P, "shovel_shaft", (-0.85, fy, 0.55), (-0.85, fy, 1.55), 0.017, handle)
    K.torus("shovel_grip", 0.06, 0.012, handle, loc=(-0.85, fy, 1.6), rot=(90, 0, 0), arc=360, segs=12, parent=P)
    prism(P, "shovel_blade", [(-0.12, 0.3), (0.12, 0.3), (0.12, 0.08), (0, -0.02), (-0.12, 0.08)], 0.006, zinc, loc=(-0.85, fy - 0.01, 0.25), bev=0.002)
    nail(P, (-0.85, fy - 0.02, 1.52))
    # rake hung head up
    rod(P, "rake_shaft", (-0.5, fy, 0.3), (-0.5, fy, 1.7), 0.015, handle)
    K.box("rake_head", (0.4, 0.03, 0.03), ir, loc=(-0.5, fy, 1.72), bev=0.004, parent=P)
    for t in range(9):
        K.box("tine", (0.008, 0.008, 0.07), ir, loc=(-0.68 + t * 0.045, fy - 0.02, 1.76), rot=(-30, 0, 0), bev=0, parent=P)
    # bow saw, hand saw, hammer, wrench, a coil of rope
    K.torus("bowsaw", 0.3, 0.012, ir, loc=(-0.05, fy - 0.01, 1.45), rot=(90, 0, 0), arc=180, segs=16, parent=P).scale = (1, 0.5, 1)
    rod(P, "bowsaw_blade", (-0.35, fy - 0.01, 1.45), (0.25, fy - 0.01, 1.45), 0.005, zinc)
    nail(P, (-0.05, fy - 0.02, 1.61))
    prism(P, "saw_blade", [(-0.07, 0.0), (0.07, 0.0), (0.05, -0.5), (-0.02, -0.5)], 0.003, zinc, loc=(0.0, fy - 0.01, 1.2), bev=0)
    prism(P, "saw_handle", [(-0.08, 0.0), (0.08, 0.0), (0.07, 0.15), (-0.07, 0.15)], 0.025, wood(1, wear=0.1), loc=(0.0, fy - 0.015, 1.18), bev=0.004)
    rod(P, "hammer_handle", (0.3, fy - 0.02, 0.85), (0.3, fy - 0.02, 1.2), 0.014, handle)
    K.box("hammer_head", (0.14, 0.035, 0.035), ir, loc=(0.3, fy - 0.02, 1.22), bev=0.006, parent=P)
    K.box("wrench", (0.03, 0.012, 0.3), zinc, loc=(0.48, fy - 0.01, 1.05), bev=0.004, parent=P)
    K.torus("wrench_ring", 0.03, 0.01, zinc, loc=(0.48, fy - 0.01, 1.22), rot=(90, 0, 0), segs=10, parent=P)
    for k in range(3):
        K.torus("rope_coil", 0.16 - k * 0.01, 0.016, rope_mat(), loc=(0.8, fy - 0.04 - k * 0.01, 1.3 - k * 0.015), rot=(90, 0, 0), segs=20, parent=P)
    nail(P, (0.8, fy - 0.02, 1.45))
    # shelf of tins along the top
    plank(P, "shelf", (1.0, 0.18, 0.025), (0.5, -0.07, 1.72), shade=1)
    for x in (0.15, 0.85):
        beam(P, f"shelf_bracket{x}", (x, 0.0, 1.58), (x, -0.13, 1.7), 0.03, 0.03, wood(3))
    for k, (x, col) in enumerate(((0.12, SEA), (0.27, BRICK), (0.45, MUSTARD), (0.62, ZINC), (0.8, SEA))):
        h = rnd.uniform(0.1, 0.18)
        K.cyl("tin", rnd.uniform(0.04, 0.06), h, tin(col, wear=0.25), loc=(x, -0.07, 1.7325 + h / 2), segs=12, bev=0.004, parent=P)
    return P


def anvil_stump(seed=59):
    """An anvil stapled down onto a hardwood stump, tongs leaning on it, a hammer on the block and a quench bucket."""
    rnd = random.Random(seed)
    P = _root("anvil")
    b, c = bark(), heart()
    sh = 0.55
    K.cyl("stump", 0.32, sh, b, loc=(0, 0, sh / 2), segs=14, r2=0.3, bev=0.02, parent=P)
    K.cyl("top", 0.29, 0.01, c, loc=(0, 0, sh + 0.001), segs=14, bev=0, parent=P)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2 + rnd.uniform(-0.3, 0.3)
        limb(P, f"root{k}", (math.cos(a) * 0.28, math.sin(a) * 0.28, 0.08), math.degrees(a), -22, 0.28, 0.07, 0.02, b)
    ai = M(GRAPHITE, metal=0.6, wear=0.25, grime=0.4, scratches=0.5)
    z = sh
    K.box("foot", (0.4, 0.22, 0.06), ai, loc=(0.02, 0, z + 0.03), bev=0.01, parent=P)
    tapered("waist", (0.28, 0.16), (0.2, 0.11), 0.13, ai, loc=(0.02, 0, z + 0.06), bev=0.01, parent=P)
    K.box("body", (0.44, 0.13, 0.1), ai, loc=(0.03, 0, z + 0.24), bev=0.012, parent=P)
    K.box("face", (0.42, 0.11, 0.008), M(ZINC, metal=0.8, rough=0.3, wear=0.05), loc=(0.03, 0, z + 0.292), bev=0.002, parent=P)
    K.box("heel", (0.08, 0.1, 0.05), ai, loc=(0.27, 0, z + 0.235), bev=0.01, parent=P)
    horn = K.cyl("horn", 0.06, 0.22, ai, loc=(-0.29, 0, z + 0.25), rot=(0, -90, 0), segs=12, r2=0.008, bev=0, parent=P)
    horn.scale = (0.75, 1.0, 1.0)
    K.box("hardy", (0.02, 0.02, 0.01), K.mat("tent_dark", DARK, rough=1.0), loc=(0.2, 0, z + 0.293), bev=0, parent=P)
    for x in (-0.12, 0.16):
        K.torus("staple", 0.04, 0.008, ai, loc=(x, 0, z + 0.01), rot=(90, 0, 90), arc=180, segs=10, parent=P)
    # hammer on the block, tongs leaning on it
    K.cyl("hammer_handle", 0.014, 0.32, wood(2, wear=0.05), loc=(-0.08, -0.2, z + 0.02), rot=(0, 90, -20), segs=8, bev=0, parent=P)
    K.box("hammer_head", (0.04, 0.04, 0.12), ai, loc=(0.08, -0.26, z + 0.025), rot=(90, 0, -20), bev=0.006, parent=P)
    pv = (0.37, -0.17, 0.42)   # tongs leaning on the stump, handles splayed on the ground
    rod(P, "tongs_a", (0.5, -0.36, 0.0), pv, 0.009, ai, segs=6)
    rod(P, "tongs_b", (0.6, -0.25, 0.0), pv, 0.009, ai, segs=6)
    K.cyl("tongs_rivet", 0.018, 0.03, ai, loc=pv, rot=(90, 0, 30), segs=8, bev=0, parent=P)
    rod(P, "tongs_jaw_a", pv, (0.31, -0.14, 0.54), 0.011, ai, segs=6)
    rod(P, "tongs_jaw_b", pv, (0.34, -0.09, 0.53), 0.011, ai, segs=6)
    bucket(P, "quench", (-0.55, -0.3, 0), r=0.16, h=0.3, col=GUNMETAL)
    for k in range(5):
        K.box("scrap", (rnd.uniform(0.05, 0.12), 0.02, 0.012), ai, loc=(rnd.uniform(-0.6, 0.6), rnd.uniform(-0.7, -0.4), 0.006), rot=(0, 0, rnd.uniform(0, 180)), bev=0, parent=P)
    return P


def grindstone(seed=60):
    """A treadle grindstone: a sandstone wheel on an iron axle in a trestle frame, a crank and rod down to the pedal,
    a trough under the wheel and a seat at the other end."""
    rnd = random.Random(seed)
    P = _root("grindstone")
    fr = wood(3)
    zr = 0.45
    for s in (-1, 1):
        plank(P, f"rail{s}", (1.4, 0.06, 0.09), (0, s * 0.17, zr), shade=2)
        for e in (-1, 1):
            beam(P, "leg", (e * 0.58, s * 0.17, zr), (e * 0.66, s * 0.27, 0), 0.06, 0.05, fr)
        beam(P, f"upright{s}", (-0.35, s * 0.17, zr + 0.04), (-0.35, s * 0.17, 0.78), 0.07, 0.06, fr)
        K.box(f"bearing{s}", (0.1, 0.08, 0.06), iron(), loc=(-0.35, s * 0.17, 0.79), bev=0.008, parent=P)
    for e in (-1, 1):
        beam(P, "stretch", (e * 0.62, -0.22, 0.15), (e * 0.62, 0.22, 0.15), 0.05, 0.04, wood(1))
    sw = K.mat("grit", STONE, rough=0.95, grime=0.3, cracks=0.2, noise_scale=10.0)
    K.cyl("wheel", 0.33, 0.1, sw, loc=(-0.35, 0, 0.79), rot=(90, 0, 0), segs=28, bev=0.012, parent=P)
    rod(P, "axle", (-0.35, -0.3, 0.79), (-0.35, 0.24, 0.79), 0.016, iron())
    K.cyl("washer", 0.06, 0.02, iron(), loc=(-0.35, -0.06, 0.79), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    # crank and rod down to the treadle
    cx, cz = -0.35 + 0.09, 0.79 - 0.03
    K.box("crank", (0.11, 0.02, 0.03), iron(), loc=(-0.3, -0.3, 0.775), rot=(0, 18, 0), bev=0.004, parent=P)
    rod(P, "conrod", (cx, -0.31, cz), (-0.15, -0.31, 0.14), 0.01, iron())
    beam(P, "treadle", (-0.2, -0.31, 0.14), (0.35, -0.12, 0.03), 0.12, 0.025, wood(1))
    K.cyl("treadle_pivot", 0.02, 0.12, iron(), loc=(0.36, -0.12, 0.04), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    # trough under the wheel
    basin(P, "trough", (0.2, 0.06), (0.24, 0.1), 0.13, 0.006, tin(ZINC, wear=0.2), loc=(-0.35, 0, zr + 0.04), e=4, n=20)
    K.box("trough_water", (0.42, 0.13, 0.004), K.mat("grey_water", "#5f6e6c", rough=0.1), loc=(-0.35, 0, zr + 0.14), bev=0, parent=P)
    # seat at the far end
    K.box("seat", (0.38, 0.42, 0.05), wood(0), loc=(0.48, 0, zr + 0.07), bev=0.01, parent=P)
    lump(P, "seat_sack", 0.18, (0.48, 0, zr + 0.1), (1.0, 1.1, 0.25), canvas(SACK), rnd, jitter=0.05, flat=0.2)
    K.box("sickle_h", (0.12, 0.025, 0.025), wood(2), loc=(0.1, 0.08, zr + 0.06), rot=(0, 0, 10), bev=0.005, parent=P)
    K.torus("sickle", 0.12, 0.008, tin(ZINC, wear=0.1), loc=(0.0, 0.14, zr + 0.06), rot=(0, 0, 160), arc=200, segs=14, parent=P).scale = (1, 1, 0.3)
    bucket(P, "can", (0.45, -0.45, 0), r=0.1, h=0.22, water=False)
    return P


def workbench_settler(seed=61):
    """A heavy bench of thick planks: a cast vice on its front corner, a drawer, a lower shelf of offcuts, a back rail
    with a shelf of tins, and clutter on its top (a can of nails, a hammer, files, a gear and a lamp)."""
    rnd = random.Random(seed)
    P = _root("workbench")
    W, D, H = 2.0, 0.75, 0.9
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = H - 0.08 if sy < 0 else 1.7
            K.box("leg", (0.11, 0.11, h), wood(3), loc=(sx * (W / 2 - 0.1), sy * (D / 2 - 0.08), h / 2), bev=0.012, parent=P)
        plank(P, f"side_rail{sx}", (0.06, D - 0.1, 0.1), (sx * (W / 2 - 0.1), 0, 0.2), shade=2)
    for sy in (-1, 1):
        plank(P, f"long_rail{sy}", (W - 0.2, 0.06, 0.1), (0, sy * (D / 2 - 0.08), 0.2), shade=2)
        plank(P, f"apron{sy}", (W - 0.1, 0.04, 0.12), (0, sy * (D / 2 - 0.04), H - 0.14), shade=3)
    for k in range(4):
        plank(P, f"top{k}", (W + 0.08, D / 4 - 0.004, 0.08), (rnd.uniform(-0.01, 0.01), -D / 2 + D / 8 + k * D / 4, H - 0.04), shade=k)
    patch(P, "burn_plate", (0.5, 0.35, 0.006), (0.62, 0.12, H + 0.003), (0, 0, 3), ZINC, nails=False)
    for k in range(4):
        plank(P, f"shelf{k}", (W - 0.25, D / 4 - 0.02, 0.025), (0, -D / 2 + D / 8 + k * D / 4, 0.265), shade=k + 1)
    # drawer under the top
    K.box("drawer", (0.5, 0.03, 0.1), wood(0), loc=(0.2, -D / 2 - 0.01, H - 0.14), bev=0.006, parent=P)
    K.cyl("drawer_knob", 0.02, 0.03, iron(), loc=(0.2, -D / 2 - 0.035, H - 0.14), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    # back rail and shelf
    plank(P, "back_rail", (W, 0.05, 0.12), (0, D / 2 - 0.08, 1.64), shade=2)
    plank(P, "back_board", (W - 0.2, 0.025, 0.5), (0, D / 2 - 0.05, H + 0.27), shade=1)
    plank(P, "back_shelf", (W - 0.2, 0.18, 0.03), (0, D / 2 - 0.15, 1.4), shade=0)
    for x in (-0.6, 0.6):
        beam(P, f"bracket{x}", (x, D / 2 - 0.07, 1.25), (x, D / 2 - 0.22, 1.38), 0.04, 0.04, wood(3))
    for k, (x, col) in enumerate(((-0.8, SEA), (-0.62, BRICK), (-0.3, MUSTARD), (0.1, ZINC), (0.3, SEA), (0.72, BRICK))):
        h = rnd.uniform(0.1, 0.2)
        K.cyl("tin", rnd.uniform(0.04, 0.065), h, tin(col, wear=0.25), loc=(x, D / 2 - 0.15, 1.415 + h / 2), segs=12, bev=0.004, parent=P)
    # the vice on the front left corner
    vx, vy = -0.7, -D / 2 - 0.05
    vm = M(STEEL, metal=0.5, wear=0.25, grime=0.5)
    K.box("vice_body", (0.16, 0.22, 0.1), vm, loc=(vx, vy + 0.08, H + 0.05), bev=0.012, parent=P)
    K.box("vice_jaw_fixed", (0.18, 0.04, 0.09), vm, loc=(vx, vy + 0.02, H + 0.13), bev=0.01, parent=P)
    K.box("vice_jaw_move", (0.18, 0.05, 0.09), vm, loc=(vx, vy - 0.06, H + 0.13), bev=0.01, parent=P)
    for jy in (vy - 0.005, vy - 0.035):
        K.box("vice_face", (0.17, 0.008, 0.03), iron(), loc=(vx, jy, H + 0.165), bev=0, parent=P)
    K.box("vice_slide", (0.06, 0.18, 0.05), vm, loc=(vx, vy, H + 0.05), bev=0.006, parent=P)
    rod(P, "vice_screw", (vx, vy - 0.06, H + 0.1), (vx, vy - 0.18, H + 0.1), 0.012, iron())
    rod(P, "vice_bar", (vx - 0.12, vy - 0.17, H + 0.1), (vx + 0.12, vy - 0.17, H + 0.06), 0.008, iron())
    K.box("vice_board", (0.05, 0.35, 0.02), wood(1), loc=(vx, vy - 0.05, H + 0.19), rot=(0, 0, 90), bev=0.004, parent=P)
    # clutter: a can of nails, a hammer, files, a gear, a clamp lamp, a plane
    K.cyl("nail_can", 0.06, 0.13, tin(BRICK, wear=0.25), loc=(-0.25, 0.15, H + 0.065), segs=12, bev=0.004, parent=P)
    lump(P, "nails", 0.055, (-0.25, 0.15, H + 0.125), (1, 1, 0.35), iron(), rnd, jitter=0.2, u=6, v=4)
    K.cyl("hammer_handle", 0.014, 0.32, wood(2, wear=0.05), loc=(-0.05, -0.15, H + 0.016), rot=(0, 90, 12), segs=8, bev=0, parent=P)
    K.box("hammer_head", (0.035, 0.035, 0.13), iron(), loc=(0.11, -0.12, H + 0.02), rot=(90, 0, 12), bev=0.005, parent=P)
    for k in range(2):
        K.box("file", (0.25, 0.022, 0.006), tin(ZINC, wear=0.1), loc=(0.15 + k * 0.03, -0.25 + k * 0.05, H + 0.004 + k * 0.006), rot=(0, 0, -20 + k * 14), bev=0, parent=P)
    gx, gy = 0.55, -0.15
    K.cyl("gear", 0.09, 0.03, M(GUNMETAL, metal=0.6, wear=0.2), loc=(gx, gy, H + 0.021), segs=16, bev=0.004, parent=P)
    for k in range(12):
        a = math.radians(30 * k)
        K.box("tooth", (0.025, 0.022, 0.03), M(GUNMETAL, metal=0.6, wear=0.2), loc=(gx + math.cos(a) * 0.1, gy + math.sin(a) * 0.1, H + 0.021), rot=(0, 0, 30 * k), bev=0, parent=P)
    K.cyl("gear_hole", 0.025, 0.032, K.mat("tent_dark", DARK, rough=1.0), loc=(gx, gy, H + 0.021), segs=8, bev=0, parent=P)
    K.box("plane", (0.24, 0.06, 0.06), wood(1, wear=0.1), loc=(0.85, -0.2, H + 0.03), rot=(0, 0, 8), bev=0.012, parent=P)
    K.box("plane_iron", (0.03, 0.05, 0.05), iron(), loc=(0.85, -0.2, H + 0.07), rot=(0, 30, 8), bev=0.004, parent=P)
    lx, ly = 0.95, D / 2 - 0.12
    K.cyl("lamp_clamp", 0.03, 0.06, iron(), loc=(lx, ly, H + 0.03), segs=8, bev=0.004, parent=P)
    rod(P, "lamp_arm", (lx, ly, H + 0.06), (lx - 0.1, ly - 0.1, H + 0.45), 0.008, iron())
    K.cyl("lamp_shade", 0.08, 0.1, M(MUSTARD, metal=0.3, wear=0.25), loc=(lx - 0.13, ly - 0.15, H + 0.43), rot=(-40, -15, 0), segs=14, r2=0.03, bev=0.004, parent=P)
    for k in range(3):
        plank(P, f"offcut{k}", (rnd.uniform(0.3, 0.6), 0.12, 0.03), (rnd.uniform(-0.5, 0.5), rnd.uniform(-0.15, 0.15), 0.295 + k * 0.03), rot=(0, 0, rnd.uniform(-15, 15)), shade=k)
    return P


def water_filter_barrel(seed=62):
    """A timber tower of drums filtering rainwater: a funnel of sheet into the top drum, hose down into a sand and
    charcoal drum below it, and a tap from that over a bucket and a jerry can."""
    rnd = random.Random(seed)
    P = _root("water_filter")
    S = 0.45
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("post", (0.09, 0.09, 2.15), wood(3), loc=(sx * S, sy * S, 1.075), bev=0.01, parent=P)
    for z, lvl in ((0.55, "low"), (1.45, "high")):
        for sy in (-1, 1):
            plank(P, f"{lvl}_bearer{sy}", (2 * S + 0.12, 0.06, 0.1), (0, sy * S, z - 0.05), shade=2)
        for k in range(5):
            plank(P, f"{lvl}_deck{k}", (0.15, 2 * S, 0.03), (-0.36 + k * 0.18, 0, z + 0.015), shade=k)
    for sy in (-1, 1):
        beam(P, f"xbrace{sy}", (-S, sy * (S + 0.05), 0.1), (S, sy * (S + 0.05), 1.35), 0.07, 0.03, wood(1))
    for sx in (-1, 1):
        beam(P, f"ybrace{sx}", (sx * (S + 0.05), -S, 1.35), (sx * (S + 0.05), S, 0.65), 0.07, 0.03, wood(1))

    def drum(name, z, col, h=0.88):
        paint = M(col, metal=0.4, wear=0.25, grime=0.5, scratches=0.4)
        K.cyl(name, 0.29, h, paint, loc=(0, 0, z + h / 2), segs=20, bev=0.015, parent=P)
        for f in (0.03, 0.35, 0.65, 0.97):
            K.cyl(name + "_hoop", 0.302, 0.025, M(col, metal=0.4, wear=0.2, grime=0.5), loc=(0, 0, z + h * f), segs=20, bev=0, parent=P)

    drum("drum_top", 1.48, SEA)
    drum("drum_low", 0.58, STEEL)
    # a funnel of zinc sheet into the top drum
    lathe("funnel", [(0.06, 2.3), (0.45, 2.55), (0.43, 2.56), (0.05, 2.32)], tin(ZINC, wear=0.2), segs=20, parent=P)
    K.cyl("funnel_neck", 0.06, 0.08, tin(ZINC, wear=0.2), loc=(0, 0, 2.3), segs=10, bev=0, parent=P)
    K.disc("rain", 0.38, K.mat("water", WATER, rough=0.05), (0, 0), z=2.48, segs=20).parent = P
    # hose from the top drum's bottom tap, down outside the deck into the low drum's lid
    hose = M(GRAPHITE, rough=0.6, wear=0.05)
    K.cyl("tap_top", 0.02, 0.1, tin(ZINC, wear=0.2), loc=(0.2, -0.27, 1.56), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    pts = [(0.2, -0.32, 1.56), (0.33, -0.53, 1.42), (0.35, -0.55, 1.1), (0.25, -0.3, 1.0), (0.15, -0.15, 1.47)]
    for k in range(len(pts) - 1):
        rod(P, "hose", pts[k], pts[k + 1], 0.016, hose, segs=8)
        if k:
            K.cyl("hose_bend", 0.016, 0.03, hose, loc=pts[k], segs=8, bev=0, parent=P)
    K.cyl("bung", 0.04, 0.03, tin(ZINC, wear=0.2), loc=(0.15, -0.15, 1.47), segs=10, bev=0, parent=P)
    # a strip of cloth tied round the low drum, chalk marks
    K.cyl("cloth_band", 0.296, 0.1, canvas(BONE, grime=0.4), loc=(0, 0, 1.2), segs=20, bev=0, parent=P)
    # tap from the low drum over a bucket, a jerry can beside it
    K.cyl("tap_low", 0.02, 0.18, tin(ZINC, wear=0.2), loc=(0, -0.36, 0.66), rot=(90, 0, 0), segs=8, bev=0, parent=P)
    rod(P, "tap_spout", (0, -0.45, 0.66), (0, -0.45, 0.6), 0.016, tin(ZINC, wear=0.2))
    K.box("tap_handle", (0.06, 0.012, 0.012), M(BRICK, wear=0.1), loc=(0, -0.42, 0.7), bev=0, parent=P)
    bucket(P, "bucket", (0, -0.62, 0), r=0.15, h=0.3)
    K.box("jerry", (0.17, 0.35, 0.45), tin(STEEL, wear=0.25), loc=(0.62, -0.7, 0.225), rot=(0, 0, 10), bev=0.025, parent=P)
    K.box("jerry_handle", (0.05, 0.18, 0.04), tin(STEEL, wear=0.2), loc=(0.62, -0.7, 0.47), rot=(0, 0, 10), bev=0.01, parent=P)
    K.cyl("jerry_spout", 0.025, 0.05, tin(STEEL, wear=0.2), loc=(0.6, -0.83, 0.47), rot=(0, 0, 10), segs=8, bev=0, parent=P)
    return P


# ================================================================== trading
def trade_cart(seed=63):
    """A trader's two-wheel cart parked on its prop: plank bed with low sides, a striped awning on four poles, crates,
    sacks, a barrel and hung pots for goods, and a sign board with a painted gear."""
    rnd = random.Random(seed)
    P = _root("trade_cart")
    BW, BL, BZ = 1.3, 1.9, 0.7
    wr = 0.62
    for s in (-1, 1):
        spoked_wheel(P, f"wheel{s}", (s * (BW / 2 + 0.12), 0.15, wr), wr, spokes=8)
    rod(P, "axle", (-(BW / 2 + 0.25), 0.15, wr), ((BW / 2 + 0.25), 0.15, wr), 0.03, iron())
    for s in (-1, 1):
        plank(P, f"sill{s}", (0.1, BL + 0.2, 0.12), (s * (BW / 2 - 0.06), 0.05, BZ - 0.06), shade=3)
        beam(P, f"shaft{s}", (s * (BW / 2 - 0.15), -BL / 2 + 0.1, BZ - 0.08), (s * 0.35, -BL / 2 - 1.5, 0.32), 0.07, 0.07, wood(3))
    K.box("prop_leg", (0.08, 0.08, 0.32), wood(2), loc=(0, -BL / 2 - 1.25, 0.16), bev=0.01, parent=P)
    rod(P, "swingle", (-0.4, -BL / 2 - 1.25, 0.34), (0.4, -BL / 2 - 1.25, 0.34), 0.03, wood(1, wear=0.1))
    for k in range(7):
        plank(P, f"bed{k}", (BW, BL / 7 - 0.006, 0.035), (0, -BL / 2 + BL / 14 + k * BL / 7, BZ + 0.0175), shade=k)
    for s in (-1, 1):
        plank(P, f"side{s}", (0.03, BL, 0.25), (s * (BW / 2 + 0.015), 0, BZ + 0.16), shade=1 + (s > 0))
    plank(P, "tail", (BW, 0.03, 0.25), (0, BL / 2 + 0.015, BZ + 0.16), shade=2)
    patch(P, "side_patch", (0.034, 0.5, 0.18), (BW / 2 + 0.03, 0.4, BZ + 0.15), (0, 0, 0), SEA, nails=False)
    # awning poles and a striped canvas roof
    AZ0, AZ1 = 2.15, 2.35
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("pole", (0.06, 0.06, 2.4 - BZ), wood(3, wear=0.1), loc=(sx * (BW / 2 + 0.05), sy * (BL / 2 - 0.05), BZ + (2.4 - BZ) / 2 - 0.12), bev=0, parent=P)
    cols = [CANVAS, SEA, CANVAS, SEA, CANVAS, SEA]
    n = len(cols)
    x0, x1 = -BW / 2 - 0.2, BW / 2 + 0.2
    y0, y1 = -BL / 2 - 0.35, BL / 2 + 0.15

    def roof(u, v, dz=0.0):
        x = x0 + (x1 - x0) * u
        y = y0 + (y1 - y0) * v
        z = AZ0 + (AZ1 - AZ0) * v + 0.06 * math.sin(math.pi * u) - 0.03 * math.sin(math.pi * v)
        return (x, y, z + dz)

    for k in range(n):
        sheet(P, f"stripe{k}", lambda u, v, k=k: roof((k + u) / n, v, 0.001 * (k % 2)), 2, 6, canvas(cols[k], grime=0.3), t=0.006)
        sheet(P, f"valance{k}", lambda u, v, k=k: (roof((k + u) / n, 0)[0], y0 - 0.005, roof((k + u) / n, 0)[2] - v * (0.13 + 0.025 * math.sin(math.pi * u))),
              3, 1, canvas(cols[k], grime=0.3), t=0.004)
    sheet(P, "awning_patch", lambda u, v: roof(0.52 + 0.12 * u, 0.45 + 0.15 * v, 0.008), 2, 2, canvas(MUSTARD), t=0.003)
    rod(P, "ridge", (0, y0 + 0.1, roof(0.5, 0.05)[2] - 0.03), (0, y1 - 0.05, roof(0.5, 0.95)[2] - 0.03), 0.02, wood(3, wear=0.1))
    for sy in (-1, 1):
        rod(P, "bow", (-(BW / 2 + 0.05), sy * (BL / 2 - 0.05), 2.26 + sy * 0.08), ((BW / 2 + 0.05), sy * (BL / 2 - 0.05), 2.26 + sy * 0.08), 0.02, wood(3, wear=0.1))
    # goods
    crate(P, "crate_a", (0.5, 0.4, 0.35), (-0.3, 0.55, BZ + 0.035), yaw=4, shade=0)
    crate(P, "crate_b", (0.45, 0.38, 0.3), (-0.32, 0.55, BZ + 0.385), yaw=-8, shade=2)
    for k in range(4):
        lump(P, "apple", 0.045, (-0.42 + (k % 2) * 0.2, 0.5 + (k // 2) * 0.11, BZ + 0.69), (1, 1, 0.9),
             M(APPLE if k % 2 else LEAF, wear=0.0, scratches=0, rough=0.5), rnd, jitter=0.08, u=5, v=4)
    smat = [K.mat(f"sack{k}", c, rough=0.95, grime=0.4, noise_scale=9.0) for k, c in enumerate((SACK, SACK_D, SACK_L))]
    for k in range(2):
        sack(P, f"sack{k}", (0.6, 0.38, 0.22), (0.3, -0.45 + k * 0.4, BZ + 0.035), (0, 0, 90 + rnd.uniform(-10, 10)), smat[k], rnd)
    staves_r = 0.2
    K.cyl("keg", staves_r, 0.45, wood(1), loc=(0.35, 0.6, BZ + 0.26), segs=14, bev=0.02, parent=P)
    for z in (0.08, 0.37):
        K.cyl("keg_hoop", staves_r + 0.006, 0.03, tin(RUST, wear=0.2), loc=(0.35, 0.6, BZ + 0.035 + z), segs=14, bev=0, parent=P)
    K.cyl("rug", 0.11, 1.0, canvas(BRICK), loc=(-0.25, -0.35, BZ + 0.15), rot=(90, 0, 3), segs=12, bev=0.02, parent=P)
    for k in range(2):
        K.cyl("rug_band", 0.115, 0.03, canvas(MUSTARD), loc=(-0.25, -0.62 + k * 0.54, BZ + 0.15), rot=(90, 0, 3), segs=12, bev=0, parent=P)
    # pots and a lantern hung from the front bow
    hz = 2.18
    gm = M(GUNMETAL, metal=0.6, wear=0.25)
    for k, x in enumerate((-0.35, 0.0, 0.32)):
        rod(P, "hang", (x, -BL / 2 - 0.05, hz), (x, -BL / 2 - 0.05, hz - 0.18), 0.004, rope_mat(), segs=4)
        if k == 1:
            K.cyl("lantern", 0.06, 0.16, K.mat("lampglass", "#c9c4ae", rough=0.15), loc=(x, -BL / 2 - 0.05, hz - 0.27), segs=10, bev=0, parent=P)
            K.cyl("lantern_cap", 0.07, 0.04, M(SEA, metal=0.4, wear=0.25), loc=(x, -BL / 2 - 0.05, hz - 0.17), segs=10, r2=0.03, bev=0, parent=P)
            K.cyl("lantern_base", 0.07, 0.03, M(SEA, metal=0.4, wear=0.25), loc=(x, -BL / 2 - 0.05, hz - 0.36), segs=10, bev=0, parent=P)
        else:
            K.cyl("pot", 0.11, 0.14, gm, loc=(x, -BL / 2 - 0.05, hz - 0.3), segs=12, r2=0.12, bev=0, parent=P)
            K.torus("pot_bail", 0.12, 0.004, iron(), loc=(x, -BL / 2 - 0.05, hz - 0.23), rot=(90, 0, 0), arc=180, segs=8, parent=P)
    # a sign board on the side with a painted gear pictogram
    sb = _grp(P, "sign", (BW / 2 + 0.06, -0.35, BZ + 0.45), (0, 0, 90))
    K.box("sign_board", (0.7, 0.03, 0.38), M(MUSTARD, wear=0.3, grime=0.35), bev=0.008, parent=sb)
    for s in (-1, 1):
        rod(sb, "sign_post", (s * 0.28, 0.0, -0.19), (s * 0.28, 0.0, -0.31), 0.012, wood(3, wear=0.1))
    pg = M(GRAPHITE, wear=0.2, scratches=0)
    K.cyl("gear_paint", 0.1, 0.006, pg, loc=(0, -0.017, 0), rot=(90, 0, 0), segs=16, bev=0, parent=sb)
    for k in range(8):
        a = math.radians(45 * k)
        K.box("gear_tooth", (0.04, 0.006, 0.04), pg, loc=(math.cos(a) * 0.11, -0.017, math.sin(a) * 0.11), rot=(0, -45 * k, 0), bev=0, parent=sb)
    K.cyl("gear_eye", 0.035, 0.008, M(MUSTARD, wear=0.3), loc=(0, -0.019, 0), rot=(90, 0, 0), segs=10, bev=0, parent=sb)
    return P


def market_stall(seed=64):
    """A plank market stall: a counter front of odd boards, a patched canvas awning sloping out over it, a back shelf
    of jars, produce crates on the counter and in front, and a corrugated sheet down one side."""
    rnd = random.Random(seed)
    P = _root("market_stall")
    W, D = 2.6, 1.3
    yf, yb = -D / 2, D / 2
    for sx in (-1, 1):
        K.box("post_f", (0.09, 0.09, 2.13), wood(3), loc=(sx * (W / 2 - 0.05), yf + 0.05, 1.065), bev=0.01, parent=P)
        K.box("post_b", (0.09, 0.09, 2.47), wood(3), loc=(sx * (W / 2 - 0.05), yb - 0.05, 1.235), bev=0.01, parent=P)
        beam(P, f"side_plate{sx}", (sx * (W / 2 - 0.05), yf - 0.02, 2.06), (sx * (W / 2 - 0.05), yb + 0.02, 2.41), 0.06, 0.1, wood(2))
    plank(P, "plate_f", (W, 0.06, 0.1), (0, yf + 0.05, 2.07), shade=2)
    plank(P, "plate_b", (W, 0.06, 0.1), (0, yb - 0.05, 2.41), shade=2)
    # counter: odd boards on the front, a thick top
    x = -W / 2 + 0.1
    k = 0
    while x < W / 2 - 0.1:
        w = min(rnd.uniform(0.18, 0.26), W / 2 - 0.1 - x)
        col = SEA if k == 4 else None
        if col:
            K.box("board", (w - 0.008, 0.025, 0.95), M(col, wear=0.3, grime=0.4), loc=(x + w / 2, yf - 0.0, 0.5), bev=0.006, parent=P)
        else:
            plank(P, f"front{k}", (w - 0.008, 0.025, 0.95 + rnd.uniform(-0.02, 0.02)), (x + w / 2, yf, 0.5), shade=k)
        x += w
        k += 1
    plank(P, "counter", (W + 0.1, 0.5, 0.05), (0, yf + 0.18, 1.0), shade=1)
    for sx in (-1, 0, 1):
        beam(P, f"knee{sx}", (sx * 1.0, yf + 0.03, 0.75), (sx * 1.0, yf + 0.35, 0.97), 0.05, 0.05, wood(3))
    # back shelf of jars and a hanging scale
    plank(P, "shelf", (W - 0.2, 0.25, 0.03), (0, yb - 0.17, 1.3), shade=0)
    plank(P, "shelf2", (W - 0.2, 0.25, 0.03), (0, yb - 0.17, 0.7), shade=2)
    for k in range(5):
        col = [LEAF, MUSTARD, APPLE, BONE][k % 4]
        x = -0.9 + k * 0.42
        K.cyl("jar", 0.055, 0.17, M(col, wear=0.05, scratches=0, rough=0.4), loc=(x, yb - 0.17, 1.4), segs=10, bev=0, parent=P)
        K.cyl("jar_lid", 0.05, 0.02, tin(ZINC, wear=0.2), loc=(x, yb - 0.17, 1.495), segs=10, bev=0, parent=P)
    for k in range(2):
        sack(P, f"shelf_sack{k}", (0.45, 0.25, 0.2), (-0.7 + k * 0.9, yb - 0.17, 0.715), (0, 0, rnd.uniform(-10, 10)), K.mat(f"sack{k}", [SACK, SACK_D, SACK_L][k], rough=0.95, grime=0.4, noise_scale=9.0), rnd)
    # produce on the counter and in crates out front
    produce = [M(c, wear=0.0, scratches=0, rough=0.5) for c in (APPLE, LEAF, MUSTARD)]
    for k, cx in enumerate((-0.8, 0.0, 0.75)):
        crate(P, f"tray{k}", (0.5, 0.36, 0.12), (cx, yf + 0.2, 1.025), yaw=rnd.uniform(-5, 5), shade=k)
        for j in range(4):
            lump(P, "fruit", 0.05, (cx - 0.12 + (j % 2) * 0.24 + (j // 2) * 0.05, yf + 0.14 + (j // 2) * 0.12, 1.14), (1, 1, 0.9), produce[k], rnd, jitter=0.08, u=5, v=4)
    for k, (cx, cy, z, veg) in enumerate(((-0.75, yf - 0.45, 0, False), (-0.73, yf - 0.45, 0.35, True), (0.15, yf - 0.4, 0, True))):
        crate(P, f"crate{k}", (0.5, 0.38, 0.35 if k != 1 else 0.3), (cx, cy, z), yaw=rnd.uniform(-5, 5), shade=k)
        if veg:
            for j in range(4):
                lump(P, "veg", 0.07, (cx - 0.1 + (j % 2) * 0.2, cy - 0.08 + (j // 2) * 0.15, z + (0.36 if k != 1 else 0.31)), (1.2, 1, 0.8), produce[(k + 1) % 3], rnd, jitter=0.1, u=5, v=4)
    # awning: canvas from the back plate out past the front, patched
    def awn(u, v, dz=0.0):
        x = -W / 2 - 0.1 + (W + 0.2) * u
        y = yb + 0.05 - (D + 0.75) * v
        z = 2.52 - 0.6 * v - 0.05 * math.sin(math.pi * u) * math.sin(math.pi * min(v * 1.5, 1))
        return (x, y, z + dz)
    sheet(P, "awning", awn, 10, 6, canvas(CANVAS, grime=0.35), t=0.008)
    for k, (u0, v0, du, dv, col) in enumerate(((0.12, 0.3, 0.12, 0.2, SEA), (0.55, 0.55, 0.1, 0.25, BRICK), (0.75, 0.15, 0.08, 0.2, MUSTARD))):
        sheet(P, f"patch{k}", lambda u, v, u0=u0, v0=v0, du=du, dv=dv: awn(u0 + du * u, v0 + dv * v, 0.008), 2, 2, canvas(col), t=0.003)
    for sx in (-1, 1):
        rod(P, "awning_stay", (sx * (W / 2 - 0.05), yf + 0.05, 2.15), (sx * (W / 2 + 0.05), yf - 0.7, 1.9), 0.018, wood(3, wear=0.1))
    for k in range(6):
        u = k / 5
        p = awn(u, 1.0)
        rod(P, "awning_tie", p, (p[0], p[1], p[2] - 0.12), 0.006, rope_mat(), segs=4)
    # one side closed with a corrugated sheet
    corrugated(P, "side_sheet", D - 0.1, 1.8, tin(ZINC, wear=0.2, grime=0.5), loc=(W / 2 + 0.01, 0, 1.15), basis=((0, 1, 0), (0, 0, 1), (1, 0, 0)), pitch=0.15, amp=0.016)
    return P


def awning_frame(seed=65):
    """A bare frame of four lashed poles with a tarp stretched over it, torn through and one corner come loose and
    hanging, guy ropes out to pegs."""
    rnd = random.Random(seed)
    P = _root("awning")
    X, Y = 1.5, 1.2
    hf, hb = 2.3, 2.1
    pole = wood(3, wear=0.1)
    corners = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            h = hf if sy < 0 else hb
            lean = sx * 1.5
            post(P, f"pole{sx}{sy}", (sx * X, sy * Y), h + 0.1, r=0.045, tilt=(sy * 1, lean))
            top = (sx * X + math.sin(math.radians(lean)) * (h + 0.1) * 0.95, sy * Y - math.sin(math.radians(sy * 1)) * (h + 0.1) * 0.95, h - 0.05)
            corners[(sx, sy)] = top
            K.torus("lash", 0.055, 0.009, rope_mat(), loc=(top[0], top[1], top[2] - 0.1), segs=12, parent=P)
            out = (sx * (X + 0.9), sy * (Y + 0.8))
            guy(P, top, (out[0], out[1], 0.1))
            peg(P, out, yaw=math.degrees(math.atan2(sy, sx)), lean=-18)
    for sy in (-1, 1):
        rod(P, f"rail_x{sy}", corners[(-1, sy)], corners[(1, sy)], 0.035, pole, segs=7)
    for sx in (-1, 1):
        rod(P, f"rail_y{sx}", corners[(sx, -1)], corners[(sx, 1)], 0.035, pole, segs=7)
    # the tarp: sagging, a tear through its middle, the front right corner loose and hanging
    def tarp(u, v):
        a = Vector(corners[(-1, -1)]).lerp(Vector(corners[(1, -1)]), u)
        b = Vector(corners[(-1, 1)]).lerp(Vector(corners[(1, 1)]), u)
        p = a.lerp(b, v) + Vector((0, 0, 0.06))
        p.z -= 0.18 * math.sin(math.pi * u) * math.sin(math.pi * v)
        d = max(0.0, (u - 0.62) / 0.38) * max(0.0, (0.45 - v) / 0.45)
        if d > 0:
            p.z -= 1.2 * d ** 1.2
            p.y -= 0.25 * d
            p.x += 0.1 * d
        return tuple(p)

    def keep(u, v):
        if (u - 0.38) ** 2 / 0.012 + (v - 0.55) ** 2 / 0.05 < 1 + 0.6 * math.sin(40 * u + 13 * v):
            return False
        if u > 0.9 and v < 0.12:   # the torn-off corner
            return False
        return True

    sheet(P, "tarp", tarp, 18, 14, canvas(STEEL, grime=0.4), t=0.006, keep=keep)
    for k, (u, v) in enumerate(((0.0, 0.0), (0.0, 1.0), (1.0, 1.0))):
        K.torus("grommet", 0.02, 0.006, tin(ZINC, wear=0.2), loc=tarp(u * 0.98 + 0.01, v * 0.98 + 0.01), segs=10, parent=P)
    sheet(P, "patch", lambda u, v: tuple(Vector(tarp(0.15 + 0.12 * u, 0.15 + 0.14 * v)) + Vector((0, 0, 0.007))), 2, 2, canvas(MUSTARD), t=0.003)
    # the rope that held the loose corner, trailing from its pole
    c = corners[(1, -1)]
    rod(P, "loose_rope", (c[0], c[1], c[2] - 0.1), (c[0] - 0.05, c[1] - 0.1, 0.9), 0.006, rope_mat(), segs=4)
    rod(P, "loose_rope2", (c[0] - 0.05, c[1] - 0.1, 0.9), (c[0] + 0.2, c[1] - 0.35, 0.02), 0.006, rope_mat(), segs=4)
    return P


PROPS = {
    "tent_ridge": tent_ridge,
    "tent_dome_patched": tent_dome_patched,
    "hammock_posts": hammock_posts,
    "sleeping_mats": sleeping_mats,
    "camp_kitchen": camp_kitchen,
    "smoker_barrel": smoker_barrel,
    "fish_drying_rack": fish_drying_rack,
    "laundry_tub": laundry_tub,
    "bathtub_outdoor": bathtub_outdoor,
    "clothes_chest": clothes_chest,
    "rocking_chair": rocking_chair,
    "bench_plank": bench_plank,
    "stove_pot_belly": stove_pot_belly,
    "chimney_stack_free": chimney_stack_free,
    "firewood_rack": firewood_rack,
    "chopping_block": chopping_block,
    "sawhorse_pair": sawhorse_pair,
    "tool_wall": tool_wall,
    "anvil_stump": anvil_stump,
    "grindstone": grindstone,
    "workbench_settler": workbench_settler,
    "water_filter_barrel": water_filter_barrel,
    "trade_cart": trade_cart,
    "market_stall": market_stall,
    "awning_frame": awning_frame,
}
