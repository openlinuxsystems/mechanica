"""Corn (crops map, batch K5): the harvested ear (whole and bitten) and the wild corn plant, a clump of tall stalks.

The ear is a fat cob of golden kernels in straight rows, plump and rounded, with its husk pulled back from the base into
a bundle of pale green leaves that reads as a handle, and the short stalk it was snapped from. It lies on its side (the
husk toward -X, the tip toward +X). Bites take the tip end off and show the kernels' yellow round the cob's pale pith.
Corn is not farmed (no seed): it grows wild in fields and meadows as clumps of tall stalks with long arching leaves,
pale tassels on top and ears in their husks and silks on the stalks, one merged mesh for the world. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import food_lib as fl

H = 0.165            # the cob, base to tip
R = 0.0245           # its radius at its fattest
ROWS, ALONG = 14, 24         # kernels: rows round the cob, kernels along each row
LABEL_COLOR = "#E8B82A"      # the corn's colour (no seed packet: kept for the review sheets)
KERNEL, KERNEL_EDGE, SEAM, KERNEL_PALE = "#F8CE38", "#EEB42A", "#B47C18", "#F2DC86"
PITH, CHAFF, FLESH = "#E2D6AE", "#C9AE72", "#EEC84A"
HUSK_IN, HUSK_OUT, HUSK_DRY = "#CBD394", "#7E9E44", "#BFB272"


def mats():
    return {
        "kernels": fl.material("corn_kernels", "#FFFFFF", rough=0.32, coat=0.25, sss=0.05, vertex_color=True),
        "flesh": fl.material("corn_flesh", "#FFFFFF", rough=0.5, sss=0.05, vertex_color=True),
        "husk": fl.material("corn_husk", "#FFFFFF", rough=0.6, sss=0.12, vertex_color=True),
        "stalk": fl.material("corn_stalk", "#8EA452", rough=0.65),
        "stalk_cut": fl.material("corn_stalk_cut", "#D8D2A0", rough=0.85),
    }


def radius(t):
    """The cob's radius a share ``t`` of the way from its base (0) to its tip (1): full most of the way, tapering into a
    rounded tip."""
    base = 0.86 + 0.14 * fl.smoothstep(0.0, 0.18, t)
    tip = 1 - 0.55 * fl.smoothstep(0.5, 1.0, t) ** 1.8
    cap = math.sqrt(max(0.0, 1 - fl.smoothstep(0.93, 1.0, t) ** 2)) if t > 0.93 else 1.0
    return R * base * tip * cap


def _kernel(t, a):
    """Where (t, a) sits in its kernel: (how far from the kernel's middle, 0 to about 1.4; the kernel's index)."""
    u = a / (2 * math.pi) * ROWS
    v = t * ALONG
    i, j = math.floor(u), math.floor(v)
    du, dv = (u - i - 0.5) * 2, (v - j - 0.5) * 2
    d = (abs(du) ** 3 + abs(dv * 1.08) ** 3) ** (1 / 3)
    return d, (i % ROWS) * ALONG + j


def cob_bm(seed=7, segs=(ROWS * 3, ALONG * 3)):
    """The cob upright, its base at z = 0 and its tip at H: each kernel a plump rounded square, golden, darker at its
    edges, the seams between them deep; a few pale ones toward the tip. The mesh's rings and columns fall on the seams
    (three to a kernel each way), so the kernels stay crisp with few faces."""
    rnd = random.Random(seed)
    nu, nv = segs
    bm = bmesh.new()
    rows, info = [], {}
    for j in range(nv + 1):
        t = j / nv
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            d, k = _kernel(t, a)
            bump = max(0.0, 1 - d ** 4) ** 0.45
            r = radius(t) * (0.9 + 0.1 * bump * (1 - 0.6 * fl.smoothstep(0.9, 1.0, t)))
            v = bm.verts.new((r * math.cos(a), r * math.sin(a), H * t))
            row.append(v)
            info[v] = (t, d, k)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    for row, z, sign in ((rows[0], 0.0, -1), (rows[-1], H, 1)):
        c = bm.verts.new((0, 0, z + sign * 0.0015))
        info[c] = (0.0 if sign < 0 else 1.0, 1.0, -1)
        for i in range(nu):
            bm.faces.new((row[i], row[(i + 1) % nu], c) if sign > 0 else (c, row[(i + 1) % nu], row[i]))
    bm.normal_update()
    kernel, edge, seam, pale = (fl.srgb(c) for c in (KERNEL, KERNEL_EDGE, SEAM, KERNEL_PALE))
    shade = {}
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            t, d, k = info[lp.vert]
            if k not in shade:
                shade[k] = (rnd.uniform(-0.06, 0.06), rnd.random() < 0.5 * fl.smoothstep(0.8, 1.0, t))
            jit, is_pale = shade[k]
            c = fl.mix(pale if is_pale else kernel, (1.0, 0.95, 0.75), 0.25 * max(0.0, 1 - d * 1.6))    # the plump middle catches light
            c = tuple(min(1.0, max(0.0, x + jit * w)) for x, w in zip(c, (1.0, 0.8, 0.2)))
            c = fl.mix(c, edge, fl.smoothstep(0.55, 0.85, d))
            c = fl.mix(c, seam, 0.7 * fl.smoothstep(0.88, 1.02, d))
            lp[col] = (*c, 1.0)
    return bm


def husk_bm(seed=11, n=7):
    """The husk pulled back from the base: ``n`` long leaves fixed round the cob's foot, folded back past the stalk into
    a loose bundle, each cupped, pale inside at the fold and green to a dry tan at its tip."""
    rnd = random.Random(seed)
    r0 = radius(0.0) * 0.92
    bms = []
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.25, 0.25)
        out = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-math.sin(a), math.cos(a), 0))
        length = rnd.uniform(0.07, 0.095)
        flare = rnd.uniform(0.004, 0.009)
        pts = []
        N = 14
        for i in range(N + 1):
            s = i / N
            # up off the foot a little, round the fold, then back along the stalk, flaring out and drooping a touch
            fold = math.sin(min(1.0, s * 3.2) * math.pi / 2)
            rad = r0 + 0.0035 * fold + flare * fl.smoothstep(0.25, 1.0, s) + 0.004 * math.sin(s * 9 + k)
            z = 0.012 * (1 - fl.smoothstep(0.0, 0.3, s)) - length * fl.smoothstep(0.12, 1.0, s) ** 1.1
            pts.append(out * rad + Vector((0, 0, z)) + side * 0.004 * math.sin(s * 5 + k))
        ws = [0.0115 * math.sin(math.pi * min(1.0, 0.18 + 0.82 * i / N)) ** 0.7 + 0.001 for i in range(N + 1)]
        ns = [out for _ in pts]
        bms.append(fl.strip(pts, ws, ns, fold=0.3))
    bm = fl.join(bms)
    inner, outer, dry = (fl.srgb(c) for c in (HUSK_IN, HUSK_OUT, HUSK_DRY))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            co = lp.vert.co
            s = min(1.0, max(0.0, (0.012 - co.z) / 0.1))
            c = fl.mix(inner, outer, fl.smoothstep(0.05, 0.4, s))
            c = fl.mix(c, dry, 0.8 * fl.smoothstep(0.65, 1.0, s))
            # faint veins along the leaf
            c = fl.mix(c, inner, 0.15 * (0.5 + 0.5 * math.cos(math.atan2(co.y, co.x) * 40)) ** 6)
            lp[col] = (*c, 1.0)
    return bm


def build(name="Corn", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested ear: Cob, Husk and Stalk under an empty named ``name``, lying on its side (husk -X, tip +X) on z = 0;
    ``bites`` (0..2) taken off its tip end."""
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    lie = fl.empty(name + "_Lie", root)
    cob = fl.from_bm(name + "_Cob", cob_bm(), [m["kernels"]], lie)
    husk = fl.from_bm(name + "_Husk", husk_bm(), [m["husk"]], lie)
    fl.solidify(husk, 0.0011)
    stalk = fl.tube([(0, 0, 0.004), (0, 0, -0.012), (0.0015, 0, -0.03)], [0.0095, 0.0085, 0.0088], segs=9, cap_mat=1)
    fl.from_bm(name + "_Stalk", stalk, [m["stalk"], m["stalk_cut"]], lie, smooth=False)
    for k in range(bites):
        bite(cob, k, m)
    # tip +X, the husk -X; turned a little about its axis so the husk's leaves do not lie symmetrically, then tipped so the
    # ear rests on both its cob and its husk bundle (worked out on the whole ear, kept for the bitten ones)
    lie.rotation_euler = (0.35, math.pi / 2, 0)
    bpy.context.view_layer.update()

    def lowest(objs):
        return min((o.matrix_world @ v.co).z for o in objs for v in o.data.vertices)

    whole = [cob]
    for k in range(20):
        lie.rotation_euler = (0.35, math.pi / 2 + 0.01 * k, 0)
        bpy.context.view_layer.update()
        if lowest(whole) <= lowest([husk]) + 0.001:
            break
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in fl.tree(lie) if o.type == "MESH" for v in o.data.vertices]
    lie.location = (0, 0, -min(p.z for p in pts))
    return root


BITE_AT = (0.74, 0.5)        # bites take the tip end off from these shares of the length


def bite(cob, k, m):
    """Bite ``k`` (0 or 1) off the tip end: a rounded, slanted scoop showing the kernels' yellow round the pale pith."""
    s = BITE_AT[k] * H
    big = 0.05
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=1)
    tilt = Matrix.Rotation(math.radians(18 if k == 0 else -14), 4, "X")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation((0.004, 0.0, s + big - 0.006)) @ tilt
                        @ Matrix.Diagonal((0.055, 0.055, big, 1)))
    pith, chaff, flesh, kernel = (fl.lin(c)[:3] for c in (PITH, CHAFF, FLESH, KERNEL_EDGE))

    def color_of(co):
        t = min(1.0, max(0.0, co.z / H))
        rr = math.hypot(co.x, co.y) / max(1e-4, radius(t))
        a = math.atan2(co.y, co.x)
        c = fl.mix(pith, chaff, fl.smoothstep(0.32, 0.42, rr))
        c = fl.mix(c, flesh, fl.smoothstep(0.5, 0.6, rr))
        # the kernels' walls: faint radial lines through the yellow, one per row
        c = fl.mix(c, kernel, 0.4 * (0.5 + 0.5 * math.cos(a * ROWS)) ** 8 * fl.smoothstep(0.55, 0.7, rr))
        return fl.mix(c, kernel, fl.smoothstep(0.88, 0.96, rr))

    fl.cut(cob, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The wild plant

def wild_mats():
    return {
        "stalk": fl.material("wild_corn_stalk", "#FFFFFF", rough=0.6, vertex_color=True),
        "leaf": fl.material("wild_corn_leaf", "#FFFFFF", rough=0.55, sss=0.1, vertex_color=True),
        "tassel": fl.material("wild_corn_tassel", "#C2AC6A", rough=0.75),
        "ear": fl.material("wild_corn_ear", "#FFFFFF", rough=0.6, sss=0.08, vertex_color=True),
        "silk": fl.material("wild_corn_silk", "#7A4E2A", rough=0.8),
    }


def _colored(bm, color_of):
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            lp[col] = (*color_of(lp.vert.co), 1.0)
    return bm


def _leaf_bm(base, yaw, length, width, rise, droop, rnd, z0):
    """A corn leaf from the stalk at ``base``: a long, wavy-edged blade out along ``yaw``, rising then arching over and
    drooping toward its tip, its midrib pale."""
    d = Vector((math.cos(yaw), math.sin(yaw), 0))
    side = Vector((-math.sin(yaw), math.cos(yaw), 0))
    pts, ws, ns = [], [], []
    N = 12
    twist = rnd.uniform(-0.4, 0.4)
    for i in range(N + 1):
        s = i / N
        p = base + d * length * s + Vector((0, 0, length * (rise * math.sin(s * math.pi * 0.75) - droop * s ** 2.2)))
        p += side * length * 0.06 * math.sin(s * 3 + twist) * s
        pts.append(p)
        ws.append(width * (math.sin(math.pi * min(1.0, 0.08 + s * 0.92)) ** 0.5) * (1 - 0.75 * s) + 0.002)
        ns.append(Vector((0, 0, 1)) + side * twist * s)
    bm = fl.strip(pts, ws, ns, fold=-0.25)
    dark, light, dry = (fl.srgb(c) for c in ("#466C2A", "#6F9440", "#B0A860"))

    def color_of(co):
        s = min(1.0, (Vector((co.x, co.y, 0)) - Vector((base.x, base.y, 0))).length / max(1e-3, length))
        c = fl.mix(dark, light, fl.smoothstep(0.1, 0.6, s))
        c = fl.mix(c, dry, 0.75 * fl.smoothstep(0.78 + 0.1 * rnd.random(), 1.0, s))
        # lighter toward the top of the plant, where the sun is
        return fl.mix(c, light, 0.25 * fl.smoothstep(0.6, 2.0, co.z - z0))

    return _colored(bm, color_of)


def _ear_bm(at, d, size):
    """An ear on the stalk in its husk: a green spindle leaning out along ``d``, pale at its pointed top."""
    pts = [at + d * size * s for s in (0.0, 0.08, 0.3, 0.6, 0.85, 1.0)]
    radii = [size * r for r in (0.06, 0.13, 0.17, 0.15, 0.09, 0.02)]
    bm = fl.tube(pts, radii, segs=9)
    green, husk, kernel = fl.srgb("#6F9440"), fl.srgb("#A9B170"), fl.srgb("#E8BE3A")

    def color_of(co):
        s = (co - at).dot(d) / size
        c = fl.mix(green, husk, fl.smoothstep(0.2, 0.7, s))
        # the husk opening at the top shows the ripe kernels: ready to pick
        return fl.mix(c, kernel, fl.smoothstep(0.7, 0.8, s) * (1 - fl.smoothstep(0.97, 1.0, s)))

    return _colored(bm, color_of)


def build_wild(name="WildCorn", seed=23):
    """Wild corn: a clump of four tall stalks (1.7 to 2.2 m) leaning a little apart from one patch, each with brace roots
    at its foot, long arching leaves up its length, a pale branching tassel on top and one or two husked ears with their
    silks. Its own Z up root, the clump's foot at the origin."""
    rnd = random.Random(seed)
    m = wild_mats()
    root = fl.empty(name)
    stalk_bms, leaf_bms, tassel_bms, ear_bms, silk_bms = [], [], [], [], []
    stalk_lo, stalk_hi = fl.srgb("#6E7E34"), fl.srgb("#8C8F4A")
    spots = ((0.0, 0.0, 2.15), (0.16, 0.09, 1.9), (-0.12, 0.13, 1.75), (0.04, -0.17, 2.0))
    for k, (x, y, h) in enumerate(spots):
        lean = Vector((x * 0.25 + rnd.uniform(-0.04, 0.04), y * 0.25 + rnd.uniform(-0.04, 0.04), 1)).normalized()
        foot = Vector((x, y, -0.03))
        top = foot + lean * h
        n = 9
        pts = [foot + lean * h * (i / n) + Vector((0.015 * math.sin(i * 1.7 + k), 0.015 * math.cos(i * 1.3 + k), 0)) * (i / n)
               for i in range(n + 1)]
        radii = [0.016 * (1 - 0.55 * i / n) + 0.004 for i in range(n + 1)]
        stalk_bms.append(_colored(fl.tube(pts, radii, segs=7), lambda co, f=foot, hh=h: fl.mix(stalk_lo, stalk_hi, min(1.0, (co.z - f.z) / hh))))
        # brace roots: three short props splaying out from the first node
        for j in range(3):
            a = rnd.uniform(0, 2 * math.pi)
            out = Vector((math.cos(a), math.sin(a), 0))
            rp = [foot + Vector((0, 0, 0.12)), foot + Vector((0, 0, 0.06)) + out * 0.04, foot + out * 0.07 + Vector((0, 0, -0.01))]
            stalk_bms.append(_colored(fl.tube(rp, [0.006, 0.005, 0.004], segs=5), lambda co: stalk_lo))
        # leaves, alternating sides up the stalk, the lower ones longest
        yaw0 = rnd.uniform(0, 2 * math.pi)
        for j in range(8):
            f = 0.14 + 0.095 * j
            base = foot + lean * h * f
            yaw = yaw0 + math.pi * (j % 2) + rnd.uniform(-0.5, 0.5)
            length = h * rnd.uniform(0.36, 0.44) * (1 - 0.35 * fl.smoothstep(0.4, 0.9, f))
            leaf_bms.append(_leaf_bm(base, yaw, length, 0.06 * (1 - 0.25 * f), rnd.uniform(0.25, 0.4), rnd.uniform(0.55, 0.85), rnd, foot.z))
        # the tassel: a spike and a few arching branches
        tassel_bms.append(fl.tube([top - lean * 0.02, top + lean * 0.22], [0.005, 0.0015], segs=5))
        for j in range(6):
            a = rnd.uniform(0, 2 * math.pi)
            out = Vector((math.cos(a), math.sin(a), 0))
            b = top + lean * rnd.uniform(0.0, 0.08)
            br = [b, b + out * 0.07 + lean * 0.05, b + out * 0.14 + lean * 0.01, b + out * 0.18 - lean * 0.06]
            tassel_bms.append(fl.tube(br, [0.003, 0.0025, 0.002, 0.001], segs=4, cap=False))
        # ears: one, two on the taller stalks, leaning out from the stalk with a tuft of silk at the top
        for j in range(2 if h > 1.95 else 1):
            f = 0.46 + 0.1 * j
            at = foot + lean * h * f
            a = yaw0 + math.pi / 2 + math.pi * j + rnd.uniform(-0.4, 0.4)
            d = (lean * 0.82 + Vector((math.cos(a), math.sin(a), 0)) * 0.45).normalized()
            size = rnd.uniform(0.27, 0.31)
            ear_bms.append(_ear_bm(at, d, size))
            tip = at + d * size
            for q in range(7):
                a2 = rnd.uniform(0, 2 * math.pi)
                sd = Vector((math.cos(a2), math.sin(a2), 0))
                silk_bms.append(fl.tube([tip - d * 0.01, tip + d * 0.03 + sd * 0.015, tip + d * 0.04 + sd * 0.04 - Vector((0, 0, 0.03))],
                                        [0.0016, 0.0012, 0.0006], segs=3, cap=False))
    fl.from_bm(name + "_Stalks", fl.join(stalk_bms), [m["stalk"]], root)
    leaves = fl.from_bm(name + "_Leaves", fl.join(leaf_bms), [m["leaf"]], root)
    fl.solidify(leaves, 0.0015, offset=0)
    fl.from_bm(name + "_Tassels", fl.join(tassel_bms), [m["tassel"]], root)
    fl.from_bm(name + "_Ears", fl.join(ear_bms), [m["ear"]], root)
    fl.from_bm(name + "_Silk", fl.join(silk_bms), [m["silk"]], root)
    return root
