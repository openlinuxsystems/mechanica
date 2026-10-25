"""The banana (crops map, batch K3): the harvested fruit (whole and bitten), and a dwarf banana plant as a game rig.

The fruit is one big banana, lying on its side (its stalk toward -X, the blossom tip toward +X, the curve bowing
toward -Y): five soft ridges, green at the neck, a few sugar spots, the tip's dark nub. Bites take the tip end off and
show the cream flesh inside a ring of pale peel. The plant is a dwarf banana that still fits a soil mound (crops map: not
a tree): a blotched green pseudostem, big paddle leaves arching out of its top and, once grown, a bunch hanging on its
stalk with the maroon bell at the end, green fingers turning yellow when ripe. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

L = 0.19             # along the curve: a big, readable banana
RAD = 0.0185         # the body's radius at its fattest
BOW = 0.17           # the radius of the curve it bows round
LABEL_COLOR = "#D9B42A"     # the banana's crop colour (seed label, stake tag): banana yellow
PEEL = ("#93A23A", "#EEC832", "#E2B42A", "#5A4426")         # neck, body, toward the tip, the tip's nub
SPOT = "#7A5426"
FLESH, CORE, PITH = "#F2E6BC", "#D8C690", "#ECE2A8"         # the cut face: flesh, the seedy core, the peel's pith


def mats():
    return {
        "peel": fl.material("banana_peel", "#FFFFFF", rough=0.42, coat=0.12, sss=0.03, vertex_color=True),
        "flesh": fl.material("banana_flesh", "#FFFFFF", rough=0.3, sss=0.08, vertex_color=True),
        "stalk": fl.material("banana_stalk", "#6E7A34", rough=0.7),
        "stalk_cut": fl.material("banana_stalk_cut", "#B9B27A", rough=0.85),
        "nub": fl.material("banana_nub", "#3A2C1C", rough=0.8),
        "flap": fl.material("banana_flap", PEEL[1], rough=0.45, coat=0.1),
        "pith": fl.material("banana_pith", PITH, rough=0.7, sss=0.05),
    }


def profile(s):
    """The fruit's radius a share ``s`` of the way from the neck (0) to the tip (1), as a share of its fattest."""
    neck = fl.smoothstep(0.0, 0.2, s)
    tip = 1 - fl.smoothstep(0.62, 1.0, s) ** 1.6
    return 0.28 + 0.72 * neck ** 0.8 * tip if s < 0.98 else 0.2


def centre(s, length=L, bow=BOW, up=False):
    """A point on the fruit's centre line and its direction: an arc of radius ``bow`` in the XY plane, bowing toward -Y
    (in the XZ plane, bowing down so the ends turn up, when ``up``)."""
    phi = length / bow
    a = (s - 0.5) * phi
    p = Vector((bow * math.sin(a), bow * (1 - math.cos(a)), 0))
    t = Vector((math.cos(a), math.sin(a), 0))
    p.y -= bow * (1 - math.cos(phi / 2))
    p.y = -p.y
    t.y = -t.y
    if up:
        p = Vector((p.x, 0, -p.y))
        t = Vector((t.x, 0, -t.y))
    return p, t


def body_bm(length=L, rad=RAD, bow=BOW, up=False, seed=3, segs=(30, 44), spots=True):
    """The peel: a five-ridged tube along centre(), its neck at s = 0 and its tip at s = 1, closed at both ends."""
    rnd = random.Random(seed)
    nu, nv = segs
    bm = bmesh.new()
    rows, info = [], {}
    side_ref = Vector((0, 0, 1)) if not up else Vector((0, 1, 0))
    for j in range(nv + 1):
        s = j / nv
        p, t = centre(s, length, bow, up)
        n1 = side_ref - t * side_ref.dot(t)
        n1.normalize()
        n2 = t.cross(n1)
        r = rad * profile(s)
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            ridge = math.cos(5 * a)
            rr = r * (1 + 0.07 * ridge * fl.smoothstep(0.05, 0.25, s) * (1 - fl.smoothstep(0.85, 1.0, s)))
            v = bm.verts.new(p + (n1 * math.cos(a) + n2 * math.sin(a)) * rr)
            row.append(v)
            info[v] = (s, ridge)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    for row, s, sign in ((rows[0], 0.0, -1), (rows[-1], 1.0, 1)):
        p, t = centre(s, length, bow, up)
        c = bm.verts.new(p + t * sign * rad * 0.12)
        info[c] = (s, 0.0)
        for i in range(nu):
            if sign > 0:
                bm.faces.new((row[i], row[(i + 1) % nu], c))
            else:
                bm.faces.new((c, row[(i + 1) % nu], row[i]))
    bm.normal_update()
    neck, body, tipc, nub = (fl.srgb(c) for c in PEEL)
    spot = fl.srgb(SPOT)
    marks = [(rnd.uniform(0.2, 0.85), rnd.uniform(0, 2 * math.pi), rnd.uniform(0.6, 1.4)) for _ in range(16)] if spots else []
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            s, ridge = info[lp.vert]
            c = fl.mix(neck, body, fl.smoothstep(0.04, 0.24, s))
            c = fl.mix(c, tipc, fl.smoothstep(0.7, 0.92, s))
            c = fl.mix(c, nub, fl.smoothstep(0.955, 0.99, s))
            c = fl.mix(c, fl.mix(c, nub, 0.5), 0.35 * max(0.0, ridge) ** 12 * fl.smoothstep(0.1, 0.3, s))
            for ms, ma, size in marks:
                co = lp.vert.co
                p, t = centre(ms, length, bow, up)
                d = (co - p).length / (rad * 0.11 * size)
                if d < 1.6:
                    c = fl.mix(c, spot, 0.8 * max(0.0, 1 - d * d / 2.5))
            lp[col] = (*c, 1.0)
    return bm


def build(name="Banana", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested banana: Peel, Stalk and Nub under an empty named ``name``, lying on z = 0 (stalk -X, tip +X);
    ``bites`` (0..2) taken off its tip end."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", body_bm(), [m["peel"]], root)
    # the stalk: a short, squarish stub out of the neck, its cut end pale
    p0, t0 = centre(0.0)
    pts = [p0 + t0 * 0.004, p0 - t0 * 0.012 + Vector((0, 0, 0.001)), p0 - t0 * 0.026 + Vector((0, 0, 0.003))]
    fl.from_bm(name + "_Stalk", fl.tube(pts, [0.0048, 0.0042, 0.0046], segs=5, cap_mat=1), [m["stalk"], m["stalk_cut"]], root,
               smooth=False)
    p1, t1 = centre(1.0)
    nub = fl.tube([p1 - t1 * 0.002, p1 + t1 * 0.004], [0.0026, 0.002], segs=8)
    fl.from_bm(name + "_Nub", nub, [m["nub"]], root)
    for k in range(bites):
        bite(body, k, m)
    if bites:
        flaps(name, root, BITE_AT[bites - 1], 0.055 + 0.025 * (bites - 1), m)
        # the tip's nub went with the first bite
        import bpy
        bpy.data.objects.remove(next(o for o in root.children if o.name.endswith("_Nub")))
    root.location = Vector(location) + Vector((0, 0, RAD * 1.02))
    return root


BITE_AT = (0.74, 0.5)        # bites take the tip end off at these shares of the length


def flaps(name, root, s, length, m):
    """Three strips of peel, pulled back from the bitten end and hanging down along the fruit, pale pith inside."""
    def frame(q):
        p, t = centre(q)
        n1 = Vector((0, 0, 1)) - t * t.z
        n1.normalize()
        return p, t, n1, t.cross(n1)

    s0 = s - 0.012
    bms = []
    for k, a in enumerate((0.15, 1.75, -1.6)):
        span = length * (1.0 - 0.12 * k) / L
        pts, ns = [], []
        for i in range(13):
            u = i / 12
            p, t, n1, n2 = frame(s0 - span * u)
            d = n1 * math.cos(a) + n2 * math.sin(a)
            # up off the rim, out a little, then lying back against the fruit
            off = 0.0012 + 0.0055 * math.sin(math.pi * min(1.0, u * 1.6)) ** 0.7 * (1 - 0.4 * u) + 0.002 * u
            pts.append(p + d * (RAD * profile(s0 - span * u) + off) + t * 0.004 * (1 - u) ** 3)
            ns.append(d)
        ws = [0.0082 * (1 - 0.6 * (i / 12) ** 1.5) + 0.0005 for i in range(13)]
        bms.append(fl.strip(pts, ws, ns, fold=-0.45))
    ob = fl.from_bm(name + "_Flaps", fl.join(bms), [m["flap"], m["pith"]], root)
    mod = fl.solidify(ob, 0.0016, offset=-1)
    mod.material_offset = 1
    return ob


def bite(body, k, m):
    """Bite ``k`` (0 or 1) off the tip end: a rounded, slanted scoop showing cream flesh in a ring of pale peel."""
    s = BITE_AT[k]
    p, t = centre(s)
    big = 0.032
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=1)
    rot = t.to_track_quat("Z", "X").to_matrix().to_4x4()
    tilt = Matrix.Rotation(math.radians(16 if k == 0 else -12), 4, "Y")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(p) @ rot @ tilt @ Matrix.Translation((0, 0, big - 0.004))
                        @ Matrix.Diagonal((0.042, 0.042, big, 1)))
    flesh, core, pith, peel = (fl.lin(c)[:3] for c in (FLESH, CORE, PITH, PEEL[1]))

    def color_of(co):
        # how far out from the centre line, as a share of the radius there
        best = min((centre(q / 40.0) for q in range(41)), key=lambda pt: (co - pt[0]).length)
        q = min(range(41), key=lambda i: (co - centre(i / 40.0)[0]).length) / 40.0
        rr = (co - best[0]).length / (RAD * profile(q))
        c = fl.mix(core, flesh, fl.smoothstep(0.12, 0.3, rr))
        c = fl.mix(c, pith, fl.smoothstep(0.72, 0.8, rr))
        return fl.mix(c, peel, fl.smoothstep(0.9, 0.98, rr))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

STEM_H = 0.25                # the pseudostem when grown (local; the rig is scaled by plant.SCALE)
LEAVES = [(0.4, 0.0, 0.22, 0.95), (2.6, 0.35, 0.24, 0.9), (4.95, 0.75, 0.25, 0.8), (1.3, 1.2, 0.26, 0.7),
          (3.2, 1.65, 0.25, 0.55), (5.75, 2.1, 0.23, 0.45), (2.0, 2.55, 0.2, 0.3)]   # (yaw, born, length, droop)
BUNCH_YAW = 4.05             # the bunch hangs out to the front left, away from the stake
FRUIT_BORN = 2.7


def leaf_bm(length, width, seed):
    """A banana leaf along +X from its base: a stout petiole rising, then a long oblong paddle on a pale midrib arching
    out and down, its edges torn here and there. Returns (petiole and midrib, blade)."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    N = 18
    pts = fl.bezier((0, 0, 0), (length * 0.08, 0, length * 0.45), (length * 0.55, 0, length * 0.55), (length, 0, length * 0.12), n=N)
    k0 = 4
    radii = [(0.0055 * (1 - 0.4 * i / k0) if i <= k0 else 0.0033 * (1 - (i - k0) / (N - k0)) ** 0.7) + 0.0006 for i in range(N + 1)]
    rib = fl.tube(pts, radii, segs=7)
    tears = sorted(rnd.uniform(0.25, 0.9) for _ in range(3))
    bpts, ws, ns = [], [], []
    for i in range(k0, N + 1):
        s = (i - k0) / (N - k0)
        p = pts[i]
        tan = (pts[min(i + 1, N)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(up).normalized()
        n = side.cross(tan).normalized()
        bpts.append(p - n * 0.001)
        w = width * math.sin(math.pi * min(1.0, 0.04 + s * 0.96)) ** 0.35
        ws.append(w + 0.0005)
        ns.append(n)
    blade = fl.strip(bpts, ws, ns, fold=0.18)
    # ragged edges: notch the margin where it has torn
    for v in blade.verts:
        near = min(range(len(bpts)), key=lambda q: (v.co - bpts[q]).length)
        s = near / (len(bpts) - 1)
        d = (v.co - bpts[near]).length
        if d > 0.5 * ws[near]:
            for tr in tears:
                if abs(s - tr) < 0.025:
                    v.co = bpts[near] + (v.co - bpts[near]) * 0.72
    blade.normal_update()
    green, dark, dry = fl.srgb("#4E9A38"), fl.srgb("#357A2C"), fl.srgb("#A39A4E")
    col = blade.loops.layers.color.new("Col")
    for f in blade.faces:
        for lp in f.loops:
            near = min(range(len(bpts)), key=lambda q: (lp.vert.co - bpts[q]).length)
            d = (lp.vert.co - bpts[near]).length / max(1e-6, ws[near])
            c = fl.mix(green, dark, 0.4 * fl.smoothstep(0.2, 1.0, d))
            c = fl.mix(c, dry, 0.5 * fl.smoothstep(0.85, 1.0, d) * fl.smoothstep(0.5, 1.0, near / len(bpts)))
            lp[col] = (*c, 1.0)
    return rib, blade


def stem_bm(seed=5):
    """The pseudostem: leaf sheaths wrapped into a trunk, flared at its foot, green blotched with brown and maroon."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    nu, nv = 18, 14
    rows, zs = [], {}
    for j in range(nv + 1):
        t = j / nv
        z = STEM_H * t - 0.01
        r = 0.016 + 0.006 * (1 - t) + 0.012 * math.exp(-(t / 0.08) ** 2)
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            rr = r * (1 + 0.04 * math.sin(3 * a + t * 4 + seed))
            v = bm.verts.new((rr * math.cos(a), rr * math.sin(a), z))
            row.append(v)
            zs[v] = t
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    top = bm.verts.new((0, 0, STEM_H - 0.006))
    zs[top] = 1.0
    for i in range(nu):
        bm.faces.new((top, rows[-1][i], rows[-1][(i + 1) % nu]))
    bm.normal_update()
    green, pale, brown, maroon = (fl.srgb(c) for c in ("#6E9A40", "#9DB65A", "#6A4A2A", "#5E2A34"))
    blots = [(rnd.uniform(0, 1), rnd.uniform(0, 2 * math.pi), rnd.uniform(0.6, 1.2)) for _ in range(22)]
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            co = lp.vert.co
            t = zs[lp.vert]
            a = math.atan2(co.y, co.x)
            c = fl.mix(green, pale, 0.4 * (0.5 + 0.5 * math.sin(7 * a + 3 * t)))
            for bt, ba, size in blots:
                da = math.atan2(math.sin(a - ba), math.cos(a - ba))
                d = math.hypot(da / 0.5, (t - bt) / 0.08) / size
                if d < 1:
                    c = fl.mix(c, maroon if size > 0.95 else brown, 0.7 * (1 - d * d))
            c = fl.mix(c, brown, 0.6 * (1 - fl.smoothstep(0.0, 0.1, t)))
            lp[col] = (*c, 1.0)
    return bm


def bell_bm():
    """The banana heart at the end of the bunch's stalk: a maroon teardrop of overlapping bracts, pointing down."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=12, radius=1)
    for v in bm.verts:
        n = v.co.normalized()
        t = (1 - n.z) / 2                                   # 0 top .. 1 bottom tip
        r = 0.017 * math.sin(math.pi * min(1.0, 0.15 + t * 0.85)) ** 0.9
        a = math.atan2(n.y, n.x)
        r *= 1 + 0.06 * math.cos(4 * a + 6 * t)              # the bracts' edges
        v.co = Vector((math.cos(a) * r, math.sin(a) * r, -t * 0.054))
    bm.normal_update()
    return bm


def finger_bm(seed):
    """One banana of the bunch, from its base along +X, curving up: a small body_bm."""
    return body_bm(length=0.078, rad=0.0088, bow=0.085, up=True, seed=seed, segs=(12, 14), spots=False)


class BananaRig(crop_rig.CropRig):
    """A dwarf banana in one mound. Grow: a shoot, then the pseudostem rising with paddle leaves unrolling out of its top
    one after another; then the bunch's stalk arches out and down with the maroon bell, and two hands of green bananas
    swell on it, turning yellow when ripe. Wilt: the leaves hang down along the stem."""

    def __init__(self, m, label=None, stake=True, seed=11):
        super().__init__("BananaPlant")
        self.m = m
        rnd = random.Random(seed)
        self.stem = fl.from_bm("Stem", stem_bm(seed), [m["trunk"]], self.sway)
        self.grow_obs.append(self.stem)
        self.top = fl.empty("Top", self.sway, (0, 0, STEM_H))
        self.grow_obs.append(self.top)
        self.leaves = []
        for i, (yaw, born, length, droop) in enumerate(LEAVES):
            w = self.wilt_node(f"Wilt{i}", self.top, (0, 0, -0.01 - 0.004 * i), yaw, droop=1.6 + 0.3 * (i % 2))
            tilt = fl.empty(f"Tilt{i}", w)
            tilt.rotation_euler = (0, math.radians(32) * droop - math.radians(14), 0)
            rib, blade = leaf_bm(length, 0.05 + 0.005 * (i % 3), seed + i)
            leaf = fl.from_bm(f"Leaf{i}", rib, [m["rib"]], tilt)
            bl = fl.from_bm(f"Leaf{i}_Blade", blade, [m["leaf"]], leaf)
            fl.solidify(bl, 0.0009)
            self.leaves.append((leaf, tilt, born, droop))
            self.grow_obs += [leaf]
        # the bunch: its stalk out of the top, arching over and hanging, the hands on it and the bell at its end
        self.bunch = fl.empty("Bunch", self.top, (0, 0, -0.004))
        self.bunch.rotation_euler = (0, 0, BUNCH_YAW)
        wb = self.wilt_node("WiltBunch", self.bunch, (0, 0, 0), 0.0, droop=0.35)
        pts = fl.bezier((0, 0, 0), (0.05, 0, 0.05), (0.12, 0, 0.01), (0.14, 0, -0.19), n=16)
        self.stalk = fl.from_bm("BunchStalk", fl.tube(pts, [0.0062 - 0.002 * i / 16 for i in range(17)], segs=8),
                                [m["rib"]], wb)
        self.bell = fl.from_bm("Bell", bell_bm(), [m["bell"]], fl.empty("BellAt", wb, pts[-1]))
        self.hands = []
        for h, at in enumerate((9, 11, 13)):
            node = fl.empty(f"Hand{h}", wb, pts[at])
            looks = []
            for ripe in (False, True):
                bms = []
                n = 8 - h
                for f in range(n):
                    a = 2 * math.pi * (f / n) * 0.8 - math.pi * 0.4 + rnd.uniform(-0.1, 0.1) + h * 0.5
                    fb = finger_bm(seed + 10 * h + f)
                    # out from the stalk and curving up toward the sky, as bananas grow
                    turn = Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(math.radians(-58), 4, "Y") \
                        @ Matrix.Translation((0.006, 0, 0))
                    bmesh.ops.transform(fb, matrix=turn, verts=fb.verts)
                    if not ripe:
                        tint = fb.loops.layers.color["Col"]
                        for fc in fb.faces:
                            for lp in fc.loops:
                                c = lp[tint]
                                lp[tint] = (c[0] * 0.55, c[1] * 0.85, c[2] * 0.5, 1.0)
                    bms.append(fb)
                ob = fl.from_bm(f"Hand{h}_{'ripe' if ripe else 'green'}", fl.join(bms), [m["peel"]], node)
                looks.append(ob)
                self.looks.append(ob)
            self.hands.append((node, looks))
            self.grow_obs.append(node)
        self.grow_obs += [self.bunch, self.stalk, self.bell]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        h = fl.smoothstep(-0.2, 3.2, g)
        self.stem.scale = (max(1e-4, 0.3 + 0.7 * h), max(1e-4, 0.3 + 0.7 * h), max(1e-4, 0.06 + 0.94 * h))
        self.top.location = (0, 0, STEM_H * (0.06 + 0.94 * h))
        for leaf, tilt, born, droop in self.leaves:
            age = (g - born) / 1.1
            shown = fl.smoothstep(0.0, 0.1, age)
            s = min(1.0, max(0.0, age))
            # a new leaf comes up rolled and upright, then opens out and leans over
            leaf.scale = (max(1e-4, shown * (0.25 + 0.75 * s)), max(1e-4, shown * (0.15 + 0.85 * s ** 1.5)), max(1e-4, shown * (0.25 + 0.75 * s)))
            leaf.rotation_euler = (0, -math.radians(55) * (1 - fl.smoothstep(0.0, 1.0, s)), 0)
        b = fl.smoothstep(FRUIT_BORN - 0.3, FRUIT_BORN + 0.4, g)
        self.bunch.scale = (max(1e-4, b),) * 3
        self.stalk.scale = (1, 1, 1)
        self.bell.scale = (max(1e-4, 0.6 + 0.4 * fl.smoothstep(FRUIT_BORN, 3.6, g)),) * 3
        ripe = g >= 3.7
        for node, (green, yellow) in self.hands:
            node.scale = (max(1e-4, 0.5 + 0.5 * fl.smoothstep(FRUIT_BORN, 3.7, g)),) * 3
            green.scale = (1e-4 if ripe else 1.0,) * 3
            yellow.scale = (1.0 if ripe else 1e-4,) * 3


def plant_mats():
    m = mats()
    m.update({
        "trunk": fl.material("banana_trunk", "#FFFFFF", rough=0.6, sss=0.05, vertex_color=True),
        "rib": fl.material("banana_rib", "#8DAE4C", rough=0.55, sss=0.1),
        "leaf": fl.material("banana_leaf", "#FFFFFF", rough=0.45, sss=0.15, vertex_color=True),
        "bell": fl.material("banana_bell", "#6A1E38", rough=0.45, coat=0.15),
    })
    return m


def build_rig(label):
    return BananaRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
