"""The orange (crops map, batch K4): the harvested fruit (whole and bitten), and a dwarf orange tree as a game rig.

The fruit is a round, slightly squat orange: a pitted peel, a little paler where the sun did not reach, a green stalk
button with one glossy leaf on top and a small navel underneath. Bites take its +X side off and show the segments
under a thin white pith. The plant is a dwarf orange tree that still fits a soil mound (crops map: not a tree, the same
height as the other crops): a short grey-brown trunk, a round crown of glossy dark leaves, white star flowers, then
oranges hanging in the crown, green, orange once ripe. An orange fruit is content, not interface (crops map, style).
Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

R = 0.04             # the fruit's radius: 8 cm across
SQUASH = 0.94
LABEL_COLOR = "#E8821E"     # the orange's crop colour (seed label, stake tag): orange
PEEL, PEEL_LIGHT, PEEL_DARK = "#EE8418", "#F6A63A", "#D46A12"
FLESH, MEMBRANE, PITH, CORE = "#F08214", "#F6C47E", "#F6EEDC", "#F6DEB0"
SEGMENTS = 10


def mats():
    return {
        "peel": fl.material("orange_peel", "#FFFFFF", rough=0.38, coat=0.25, sss=0.04, vertex_color=True),
        "flesh": fl.material("orange_flesh", "#FFFFFF", rough=0.22, sss=0.15, vertex_color=True),
        "stalk": fl.material("orange_stalk", "#5E7A34", rough=0.55),
        "leaf": fl.material("orange_leaf", "#2E6A2C", rough=0.3, coat=0.25, sss=0.1),
    }


def fruit_bm(rad=R, seed=3, segs=(44, 32), ripe=True, pores=True):
    """The fruit, its base at z = 0: a pitted, slightly lumpy squat sphere, the stalk's dimple on top and the navel
    below; vertex colours: orange, paler on one side, darker round the stalk."""
    nu, nv = segs
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=nu, v_segments=nv, radius=1)
    info = {}
    for v in bm.verts:
        n = v.co.normalized()
        theta = math.acos(max(-1.0, min(1.0, n.z)))
        r = rad * (1 + 0.018 * fl.noise3(n, 2.2, seed))
        if pores:
            r *= 1 - 0.006 * (0.5 + 0.5 * fl.noise3(n, 38.0, seed + 1))
        r *= 1 - 0.07 * math.exp(-(theta / 0.22) ** 2)                 # the stalk's dimple
        r *= 1 - 0.05 * math.exp(-((math.pi - theta) / 0.18) ** 2)     # the navel
        v.co = Vector((n.x * r, n.y * r, n.z * r * SQUASH))
        info[v] = (theta, n)
    lo = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= lo
    bm.normal_update()
    if ripe:
        base, light, dark = (fl.srgb(c) for c in (PEEL, PEEL_LIGHT, PEEL_DARK))
    else:
        base, light, dark = (fl.srgb(c) for c in ("#4E8A2E", "#78A23E", "#3E7428"))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            theta, n = info[lp.vert]
            c = fl.mix(base, light, 0.5 * fl.smoothstep(-0.2, 0.9, n.x * 0.6 - n.y * 0.5 + n.z * 0.3))
            c = fl.mix(c, dark, 0.5 * math.exp(-(theta / 0.3) ** 2) + 0.2 * (0.5 + 0.5 * fl.noise3(n, 6.0, seed)) ** 3)
            lp[col] = (*c, 1.0)
    return bm


def leaf_bm(length, seed, width=0.42):
    """A glossy citrus leaf along +X from its base: a short winged stalk and a pointed oval blade folded on its midrib."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    pts = fl.bezier((0, 0, 0), (length * 0.3, 0, length * 0.1), (length * 0.7, 0, length * 0.08), (length, 0, -length * 0.06), n=10)
    stalk = fl.tube(pts[:3], [0.0012 * length / 0.05, 0.0011 * length / 0.05, 0.001 * length / 0.05], segs=5)
    ws, ns = [], []
    for i in range(2, 11):
        s = (i - 2) / 8
        ws.append(length * width * 0.5 * math.sin(math.pi * min(1.0, 0.06 + s * 0.94)) ** 0.85 * rnd.uniform(0.94, 1.05) + 0.0004)
        tan = (pts[min(i + 1, 10)] - pts[i - 1]).normalized()
        ns.append(tan.cross(up).cross(tan).normalized() * -1)
    return stalk, fl.strip(pts[2:], ws, ns, fold=0.32)


def build(name="Orange", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested orange: Body, Stalk and Leaf under an empty named ``name``, sitting on z = 0; ``bites`` (0..2) bitten
    out of its +X side."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", fruit_bm(), [m["peel"]], root)
    top = Vector((0, 0, 2 * R * SQUASH * 0.965))
    fl.from_bm(name + "_Stalk", fl.tube([top - Vector((0, 0, 0.003)), top + Vector((0.001, 0, 0.004)), top + Vector((0.003, 0, 0.009))],
                                        [0.0032, 0.0022, 0.0018], segs=8), [m["stalk"]], root)
    st, bl = leaf_bm(0.065, 4)
    turn = Matrix.Translation(top + Vector((0.003, 0, 0.008))) @ Matrix.Rotation(2.4, 4, "Z") @ Matrix.Rotation(math.radians(8), 4, "Y")
    for b in (st, bl):
        bmesh.ops.transform(b, matrix=turn, verts=b.verts)
    leaf = fl.from_bm(name + "_Leaf", fl.join([st, bl]), [m["leaf"]], root)
    fl.solidify(leaf, 0.0008)
    for k in range(bites):
        bite(body, k, m)
    return root


BITES = ((0.0, 0.2), (-0.8, 0.0))     # each bite's turn about the axis from +X (rad) and its rise


def bite(body, k, m):
    """A mouth-sized scoop out of the fruit's side: orange peel, a thin white pith, then the segments and their pale
    membranes running in to the white core."""
    turn, rise = BITES[k]
    mid = Vector((0, 0, R * SQUASH))
    out = Vector((math.cos(turn), math.sin(turn), rise)).normalized()
    c0 = mid + out * R * (1.02 if k == 0 else 0.84)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=20, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(c0) @ rot @ Matrix.Diagonal((0.031, 0.027, 0.028, 1)))
    flesh, membrane, pith, core, peel = (fl.lin(c)[:3] for c in (FLESH, MEMBRANE, PITH, CORE, PEEL))

    def color_of(co):
        d = co - mid
        rr = math.sqrt(d.x * d.x + d.y * d.y + (d.z / SQUASH) ** 2) / R
        axial = math.hypot(d.x, d.y) / R
        a = math.atan2(d.y, d.x) * SEGMENTS / (2 * math.pi)
        edge = abs(a - round(a))                       # 0 on a membrane between two segments
        c = fl.mix(membrane, flesh, fl.smoothstep(0.02, 0.07, edge * max(0.25, axial)))
        # the juice sacs: a faint grain along each segment
        c = fl.mix(c, fl.mix(flesh, membrane, 0.35), 0.4 * (0.5 + 0.5 * math.sin(rr * 90 + a * 7)) ** 6)
        c = fl.mix(core, c, fl.smoothstep(0.08, 0.16, axial))
        c = fl.mix(c, pith, fl.smoothstep(0.8, 0.86, rr))
        return fl.mix(c, peel, fl.smoothstep(0.93, 0.98, rr))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

TRUNK_H = 0.15               # the trunk to where the crown starts (local; the rig is scaled by plant.SCALE)
CROWN_C = 0.26               # the crown's centre height
CROWN_R = 0.12               # its radius
SPRAYS = 46                  # leaf sprays round the crown
FRUIT = 7
FRUIT_R = 0.024
FRUIT_BORN = 2.7
RIPE_AT = 3.6


def _sphere_points(n, seed):
    """``n`` roughly even directions over the upper four fifths of a sphere (a golden spiral)."""
    rnd = random.Random(seed)
    out = []
    for k in range(n):
        z = 0.95 - 1.6 * (k + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = k * 2.39996 + rnd.uniform(-0.15, 0.15)
        out.append(Vector((r * math.cos(a), r * math.sin(a), z)))
    return out


def spray_bm(seed):
    """A spray of glossy leaves along +X from its base: a twig with five leaves fanned round its end."""
    rnd = random.Random(seed)
    bms = [fl.tube([(0, 0, 0), (0.02, 0, 0.003), (0.04, 0, 0.002)], [0.0018, 0.0014, 0.001], segs=5)]
    blades = []
    for k in range(5):
        st, bl = leaf_bm(0.06 + 0.008 * rnd.uniform(-1, 1), seed * 7 + k)
        turn = Matrix.Translation((0.012 + 0.007 * k, 0, 0.002)) @ Matrix.Rotation((k - 2) * 0.55 + rnd.uniform(-0.2, 0.2), 4, "Z") \
            @ Matrix.Rotation(rnd.uniform(-0.35, 0.1), 4, "Y") @ Matrix.Rotation(rnd.uniform(-0.4, 0.4), 4, "X")
        for b in (st, bl):
            bmesh.ops.transform(b, matrix=turn, verts=b.verts)
        bms.append(st)
        blades.append(bl)
    return fl.join(bms), fl.join(blades)


def flower_bm(seed):
    """A small white orange blossom: five thick petals round yellow stamens, facing out."""
    bms = []
    for k in range(5):
        a = 2 * math.pi * k / 5 + seed
        d = Vector((0.25, math.cos(a), math.sin(a))).normalized()
        pts = [Vector((0, 0, 0)), d * 0.006, d * 0.011]
        bms.append(fl.strip(pts, [0.0008, 0.0032, 0.0012], [Vector((1, 0, 0))] * 3))
    return fl.join(bms)


class OrangeRig(crop_rig.CropRig):
    """A dwarf orange tree in one mound. Grow: a sprout, then a woody stem thickening into a short trunk under a round
    crown of glossy leaves filling out spray by spray; white blossoms, then oranges set in the crown, green and swelling,
    orange once ripe. Wilt: the sprays droop."""

    def __init__(self, m, label=None, stake=True, seed=19):
        super().__init__("OrangePlant")
        self.m = m
        rnd = random.Random(seed)
        trunk_pts = [(0, 0, 0), (0.004, 0.002, TRUNK_H * 0.5), (-0.002, 0.003, TRUNK_H), (0.0, 0.0, CROWN_C)]
        self.trunk = fl.from_bm("Trunk", fl.tube(trunk_pts, [0.011, 0.0085, 0.007, 0.004], segs=10), [m["bark"]], self.sway)
        self.crown = fl.empty("Crown", self.sway, (0, 0, CROWN_C))
        self.grow_obs += [self.trunk, self.crown]
        # a few limbs from the trunk's top out into the crown, under the leaves
        self.limbs = []
        for k in range(4):
            a = 2 * math.pi * k / 4 + 0.4
            d = Vector((math.cos(a), math.sin(a), 0.5)).normalized() * CROWN_R * 0.8
            limb = fl.from_bm(f"Limb{k}", fl.tube([(0, 0, TRUNK_H - CROWN_C), d * 0.4 + Vector((0, 0, (TRUNK_H - CROWN_C) * 0.4)), d],
                                                  [0.005, 0.0035, 0.002], segs=6), [m["bark"]], self.crown)
            self.limbs.append(limb)
        self.sprays = []
        for k, n in enumerate(_sphere_points(SPRAYS, seed)):
            at = n * CROWN_R * rnd.uniform(0.78, 0.92)
            yaw = math.atan2(n.y, n.x)
            w = self.wilt_node(f"Wilt{k}", self.crown, tuple(at), yaw, droop=0.8 + 0.4 * (k % 3) / 2)
            tilt = fl.empty(f"Tilt{k}", w)
            tilt.rotation_euler = (rnd.uniform(-0.5, 0.5), -math.asin(max(-0.9, min(0.9, n.z))) * 0.8, 0)
            twig, blades = spray_bm(seed + k)
            spray = fl.from_bm(f"Spray{k}", twig, [m["twig"]], tilt)
            bl = fl.from_bm(f"Spray{k}_Leaves", blades, [m["leaf"]], spray)
            fl.solidify(bl, 0.0008)
            born = 0.5 + 2.4 * (1 - (n.z + 0.65) / 1.6) * 0.5 + 1.2 * (k / SPRAYS)
            self.sprays.append((spray, born))
            self.grow_obs.append(spray)
        # the fruit: hanging on short stalks in the outer crown, round its lower half where they are easy to see
        self.fruit = []
        for k in range(FRUIT):
            a = 2 * math.pi * k / FRUIT + 0.3 + rnd.uniform(-0.2, 0.2)
            z = rnd.uniform(-0.45, 0.25)
            n = Vector((math.cos(a) * math.sqrt(1 - z * z), math.sin(a) * math.sqrt(1 - z * z), z))
            hang = fl.empty(f"Fruit{k}", self.crown, tuple(n * (CROWN_R + 0.006)))
            stalk = fl.from_bm(f"Fruit{k}_Stalk", fl.tube([(0, 0, 0.012), (0, 0, 0.0)], [0.0012, 0.0014], segs=5), [m["twig"]], hang)
            flower = fl.from_bm(f"Blossom{k}", flower_bm(k), [m["flower"]], hang)
            flower.rotation_euler = (0, 0, a)
            fl.solidify(flower, 0.0005)
            looks = []
            for ripe in (False, True):
                b = fruit_bm(FRUIT_R, seed=k, segs=(20, 14), ripe=ripe, pores=False)
                bmesh.ops.transform(b, matrix=Matrix.Translation((0, 0, -2 * FRUIT_R * SQUASH)), verts=b.verts)
                ob = fl.from_bm(f"Fruit{k}_{'ripe' if ripe else 'green'}", b, [m["peel"]], hang)
                looks.append(ob)
                self.looks.append(ob)
            self.fruit.append((hang, flower, stalk))
            self.grow_obs += [hang, flower]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        h = fl.smoothstep(-0.2, 3.2, g)
        self.trunk.scale = (max(1e-4, 0.35 + 0.65 * h), max(1e-4, 0.35 + 0.65 * h), max(1e-4, 0.12 + 0.88 * h))
        self.crown.location = (0, 0, CROWN_C * (0.12 + 0.88 * h))
        self.crown.scale = (max(1e-4, 0.2 + 0.8 * h),) * 3
        for spray, born in self.sprays:
            age = (g - born) / 1.0
            spray.scale = (max(1e-4, fl.smoothstep(0.0, 0.15, age) * (0.35 + 0.65 * min(1.0, max(0.0, age)))),) * 3
        fruit = fl.smoothstep(FRUIT_BORN, RIPE_AT, g)
        for hang, flower, stalk in self.fruit:
            flower.scale = (max(1e-4, fl.smoothstep(2.0, 2.3, g) * (1 - fl.smoothstep(2.7, 3.0, g))),) * 3
            hang.scale = (max(1e-4, 1.0 if g < FRUIT_BORN else 0.35 + 0.65 * fruit),) * 3
        ripe = g >= RIPE_AT
        for ob in self.looks:
            if ob.name.endswith("_ripe"):
                ob.scale = (1.0 if ripe else 1e-4,) * 3
            else:
                ob.scale = (1e-4 if ripe or g < FRUIT_BORN else 1.0,) * 3


def plant_mats():
    m = mats()
    m.update({
        "bark": fl.material("orange_bark", "#7A6A58", rough=0.8),
        "twig": fl.material("orange_twig", "#5E7A34", rough=0.6),
        "flower": fl.material("orange_flower", "#F6F2E6", rough=0.45, sss=0.25),
    })
    return m


def build_rig(label):
    return OrangeRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
