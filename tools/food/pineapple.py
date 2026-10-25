"""The pineapple (crops map, batch K4): the harvested fruit (whole and bitten), and the pineapple plant as a game rig.

The fruit is a golden pineapple: an egg-shaped body covered in hexagonal eyes, each with a dark point and a green-brown
edge, under a crown of stiff blue-green leaves; it lies on its side (the crown toward -X, the base toward +X). Bites
take its base end off and show the juicy yellow flesh round a paler core, inside a thin rind. The plant is a knee-high
rosette (crops map: rosettes are knee high) of long, stiff, trough-shaped leaves, the fruit rising out of its middle on
a short stalk with its own small crown, green, golden once ripe. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

BODY_R = 0.046       # the body's radius at its fattest
BODY_H = 0.13        # its height, base to the crown
LABEL_COLOR = "#9A8A24"     # the pineapple's crop colour (seed label, stake tag): olive gold, apart from the banana's yellow
RIND, RIND_DARK, EYE, EYE_EDGE = "#D69A2A", "#9A6420", "#4A3018", "#8A8A3A"
FLESH, CORE, RIND_CUT = "#E2B640", "#EED892", "#B88428"
ROWS, PER_ROW = 9, 13        # the eyes: rows up the body, eyes round each row (staggered)


def mats():
    return {
        "rind": fl.material("pineapple_rind", "#FFFFFF", rough=0.6, coat=0.1, sss=0.03, vertex_color=True),
        "flesh": fl.material("pineapple_flesh", "#FFFFFF", rough=0.45, sss=0.0, vertex_color=True),
        "crown": fl.material("pineapple_crown", "#FFFFFF", rough=0.5, coat=0.08, sss=0.08, vertex_color=True),
    }


def body_radius(t):
    """The body's radius a share ``t`` of the way from its base (0) to its top (1): an egg, fattest a little below the middle."""
    return BODY_R * (math.sin(math.pi * min(1.0, 0.06 + t * 0.9)) ** 0.55) * (1 - 0.12 * t) + 0.002


def _eye(t, a):
    """Where (t, a) sits in its eye: (distance from the eye's centre in eye widths, the eye's index)."""
    row = t * ROWS
    j = math.floor(row)
    best = (9.0, 0)
    for jj in (j - 1, j, j + 1):
        off = 0.5 * (jj % 2)
        col = a / (2 * math.pi) * PER_ROW - off
        i = round(col)
        dr = row - (jj + 0.5)
        dc = col - i
        d = math.hypot(dr * 1.05, dc)
        if d < best[0]:
            best = (d, jj * PER_ROW + i)
    return best


def body_bm(rad=BODY_R, height=BODY_H, seed=3, segs=(52, 44), ripe=True):
    """The body, its base at z = 0 and its top at ``height``: each eye a low bump with a dark point and a darker rim,
    the rind golden (green when not ripe)."""
    nu, nv = segs
    k = rad / BODY_R
    bm = bmesh.new()
    rows, info = [], {}
    for j in range(nv + 1):
        t = j / nv
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            d, _ = _eye(t, a)
            bump = max(0.0, 1 - d / 0.62) ** 0.7
            r = body_radius(t) * k * (0.95 + 0.06 * bump)
            v = bm.verts.new((r * math.cos(a), r * math.sin(a), height * t))
            row.append(v)
            info[v] = (t, d)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    for row, z, sign in ((rows[0], 0.0, -1), (rows[-1], height, 1)):
        c = bm.verts.new((0, 0, z + sign * 0.002 * k))
        info[c] = (0.0 if sign < 0 else 1.0, 0.0)
        for i in range(nu):
            bm.faces.new((row[i], row[(i + 1) % nu], c) if sign > 0 else (c, row[(i + 1) % nu], row[i]))
    bm.normal_update()
    if ripe:
        rind, dark, eye, edge = (fl.srgb(c) for c in (RIND, RIND_DARK, EYE, EYE_EDGE))
    else:
        rind, dark, eye, edge = (fl.srgb(c) for c in ("#5E8A44", "#3E6234", "#5A3A2A", "#8A3A3A"))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            t, d = info[lp.vert]
            c = fl.mix(rind, dark, fl.smoothstep(0.4, 0.62, d))
            c = fl.mix(c, edge, 0.5 * fl.smoothstep(0.52, 0.66, d))
            c = fl.mix(c, eye, 0.9 * (1 - fl.smoothstep(0.06, 0.16, d)))
            if ripe:     # greener toward the top, as a pineapple ripens from its base up
                c = fl.mix(c, fl.srgb("#8A8A3A"), 0.35 * fl.smoothstep(0.6, 1.0, t))
            lp[col] = (*c, 1.0)
    return bm


def crown_bm(scale=1.0, seed=5, n=22, ripe=True):
    """The crown: stiff, pointed, trough-shaped leaves standing up and fanning out from the top, the outer ones longer
    and more spread."""
    rnd = random.Random(seed)
    bms = []
    for k in range(n):
        q = k / (n - 1)                                   # 0 the innermost (upright, short), 1 the outermost
        a = k * 2.39996 + rnd.uniform(-0.2, 0.2)
        spread = math.radians(8 + 52 * q)
        length = scale * (0.05 + 0.045 * math.sin(math.pi * min(1.0, 0.25 + q * 0.75))) * rnd.uniform(0.9, 1.08)
        d = Vector((math.cos(a) * math.sin(spread), math.sin(a) * math.sin(spread), math.cos(spread)))
        side = Vector((-math.sin(a), math.cos(a), 0))
        out = Vector((math.cos(a), math.sin(a), 0))
        pts, ws, ns = [], [], []
        for i in range(8):
            s = i / 7
            p = d * length * s + out * length * 0.12 * s * s
            pts.append(p)
            ws.append(scale * 0.011 * (1 - s) ** 0.8 + 0.0005)
            ns.append(side.cross(d).normalized())
        bms.append(fl.strip(pts, ws, ns, fold=-0.35))
    bm = fl.join(bms)
    base, tip = (fl.srgb(c) for c in (("#4E7E5A", "#7AA06A") if ripe else ("#4E7E5A", "#6A9060")))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            h = lp.vert.co.length / (scale * 0.1)
            lp[col] = (*fl.mix(base, tip, fl.smoothstep(0.2, 1.0, h)), 1.0)
    return bm


def build(name="Pineapple", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested pineapple: Body and Crown under an empty named ``name``, lying on its side (crown -X, base +X) on
    z = 0; ``bites`` (0..2) taken off its base end."""
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    tilt = fl.empty(name + "_Lie", root)
    body = fl.from_bm(name + "_Body", body_bm(), [m["rind"]], tilt)
    crown = fl.from_bm(name + "_Crown", crown_bm(1.0), [m["crown"]], tilt)
    crown.location = (0, 0, BODY_H - 0.004)
    fl.solidify(crown, 0.0012)
    # the crown toward -X, the base toward +X: tip it up until the crown's lowest leaf no longer sinks below the body,
    # so the whole fruit rests on its body with the crown just touching (worked out on the unbitten fruit, then kept)
    def low(ob):
        return min((ob.matrix_world @ v.co).z for v in ob.data.vertices)
    for k in range(15):
        tilt.rotation_euler = (0, -math.pi / 2 + 0.25 + 0.05 * k, 0)
        bpy.context.view_layer.update()
        if low(crown) >= low(body) - 0.003:
            break
    for k in range(bites):
        bite(body, k, m)
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in fl.tree(tilt) if o.type == "MESH" for v in o.data.vertices]
    tilt.location = (0, 0, -min(p.z for p in pts))
    return root


BITES = (0.035, 0.07)      # how far up from the base each bite reaches (m)


def bite(body, k, m):
    """A big slanted bite off the base end: yellow flesh round a paler core, inside a thin golden rind."""
    reach = BITES[k]
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=1)
    tilt = Matrix.Rotation(math.radians(24 if k == 0 else -18), 4, "X")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation((0.006, 0.0, reach - 0.052)) @ tilt
                        @ Matrix.Diagonal((0.07, 0.07, 0.052, 1)))
    flesh, core, rind = (fl.lin(c)[:3] for c in (FLESH, CORE, RIND_CUT))

    def color_of(co):
        t = min(1.0, max(0.0, co.z / BODY_H))
        rr = math.hypot(co.x, co.y) / body_radius(t)
        c = fl.mix(core, flesh, fl.smoothstep(0.18, 0.3, rr))
        # fibres: faint radial streaks through the flesh
        a = math.atan2(co.y, co.x)
        c = fl.mix(c, core, 0.25 * (0.5 + 0.5 * math.cos(a * 24)) ** 4 * fl.smoothstep(0.3, 0.6, rr))
        return fl.mix(c, rind, fl.smoothstep(0.86, 0.94, rr))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

LEAVES = 34
FRUIT_BORN = 2.3
RIPE_AT = 3.6
FRUIT_K = 0.8               # the fruit on the plant, as a share of the harvested one (the rig is scaled up)
STALK_H = 0.04


def sword_bm(length, seed, ripe_tint=0.0):
    """A pineapple leaf along +X from its base: long, stiff and narrow, trough-shaped, arching a little toward its tip,
    blue-green with a bronze-red flush toward the end."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    pts = fl.bezier((0, 0, 0), (length * 0.35, 0, length * 0.05), (length * 0.7, 0, length * 0.02), (length, 0, -length * 0.12), n=14)
    ws, ns = [], []
    for i in range(15):
        s = i / 14
        ws.append(0.0155 * (1 - s ** 1.4) * (0.75 + 0.25 * math.sin(math.pi * min(1.0, s * 3))) + 0.0005)
        tan = (pts[min(i + 1, 14)] - pts[max(i - 1, 0)]).normalized()
        ns.append(tan.cross(up).cross(tan).normalized() * -1)
    bm = fl.strip(pts, ws, ns, fold=-0.55)
    green, pale, bronze = fl.srgb("#4E7E5E"), fl.srgb("#7FA27A"), fl.srgb("#8E5A44")
    col = bm.loops.layers.color.new("Col")
    flush = rnd.uniform(0.2, 0.5)
    for f in bm.faces:
        for lp in f.loops:
            s = lp.vert.co.x / length
            c = fl.mix(pale, green, fl.smoothstep(0.0, 0.25, s))
            c = fl.mix(c, bronze, flush * fl.smoothstep(0.55, 1.0, s))
            lp[col] = (*c, 1.0)
    return bm


class PineappleRig(crop_rig.CropRig):
    """A pineapple plant in one mound. Grow: a tuft of short leaves, the rosette spreading leaf by leaf into a fountain
    of long stiff swords; then a stalk rises out of its middle with the fruit and its little crown, green and swelling,
    golden once ripe. Wilt: the leaves splay down."""

    def __init__(self, m, label=None, stake=True, seed=29):
        super().__init__("PineapplePlant")
        self.m = m
        rnd = random.Random(seed)
        self.leaves = []
        for k in range(LEAVES):
            q = k / (LEAVES - 1)
            yaw = k * 2.39996
            w = self.wilt_node(f"Wilt{k}", self.sway, (0.006 * math.cos(yaw), 0.006 * math.sin(yaw), 0.004 + 0.012 * q), yaw,
                               droop=0.8 + 0.6 * (1 - q))
            tilt = fl.empty(f"Tilt{k}", w)
            # the oldest leaves (first born) lie lowest and spread widest; the young ones stand up in the middle
            tilt.rotation_euler = (0, -math.radians(28 + 42 * q + rnd.uniform(-5, 5)), 0)
            leaf = fl.from_bm(f"Leaf{k}", sword_bm(0.27 - 0.08 * q + rnd.uniform(-0.02, 0.02), seed + k), [m["sword"]], tilt)
            fl.solidify(leaf, 0.0009)
            self.leaves.append((leaf, -0.15 + 2.35 * q))   # the first leaves are already up as a sprout at planting
            self.grow_obs.append(leaf)
        self.fruit = fl.empty("Fruit", self.sway, (0, 0, 0.01))
        self.stalk = fl.from_bm("Fruit_Stalk", fl.tube([(0, 0, 0), (0.002, 0, STALK_H * 0.5), (0, 0, STALK_H)], [0.007, 0.0065, 0.0075], segs=8),
                                [m["stalk"]], self.fruit)
        top = self.top = fl.empty("Fruit_Top", self.fruit, (0, 0, STALK_H))
        for ripe in (False, True):
            ob = fl.from_bm(f"Fruit_{'ripe' if ripe else 'green'}", body_bm(BODY_R * FRUIT_K, BODY_H * FRUIT_K, seed=7, segs=(44, 36), ripe=ripe),
                            [m["rind"]], top)
            self.looks.append(ob)
        crown = fl.from_bm("Fruit_Crown", crown_bm(FRUIT_K, seed=9, n=16), [m["crown"]], top)
        crown.location = (0, 0, BODY_H * FRUIT_K - 0.003)
        fl.solidify(crown, 0.0009)
        self.grow_obs += [self.fruit, self.stalk, self.top]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        for leaf, born in self.leaves:
            age = (g - born) / 1.3
            shown = fl.smoothstep(0.0, 0.12, age)
            s = min(1.0, max(0.0, age))
            leaf.scale = (max(1e-4, shown * (0.2 + 0.8 * s)),) * 3
            leaf.rotation_euler = (0, -math.radians(30) * (1 - fl.smoothstep(0.0, 1.0, s)), 0)
        rise = fl.smoothstep(FRUIT_BORN, RIPE_AT, g)
        self.fruit.scale = (max(1e-4, fl.smoothstep(FRUIT_BORN - 0.1, FRUIT_BORN + 0.2, g) * (0.3 + 0.7 * rise)),) * 3
        self.stalk.scale = (1, 1, max(1e-4, 0.4 + 0.6 * rise))
        self.top.location = (0, 0, STALK_H * (0.4 + 0.6 * rise))
        ripe = g >= RIPE_AT
        for ob in self.looks:
            ob.scale = ((1.0 if ripe else 1e-4) if ob.name.endswith("_ripe") else (1e-4 if ripe else 1.0),) * 3


def plant_mats():
    m = mats()
    m.update({
        "sword": fl.material("pineapple_sword", "#FFFFFF", rough=0.5, coat=0.06, sss=0.08, vertex_color=True),
        "stalk": fl.material("pineapple_stalk", "#6E8E5A", rough=0.55, sss=0.08),
    })
    return m


def build_rig(label):
    return PineappleRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
