"""The chili (crops map, batch K3): the harvested pepper (whole and bitten), and the chili bush as a game rig.

The pepper is a long, glossy red chili with a hooked tip, a green cap and a curled stalk, lying on its side (cap toward
-X, tip toward +X). Bites take the tip off and show the thin red wall round the pale pith and its seeds. It is a joke
food: eating one hurts (food.lua, a negative health). The plant is a knee-high bush in the tomato's language: a branching
stem, small glossy leaves, white star flowers, then chilies hanging under the leaves, green, turning red when ripe.
Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

L = 0.135            # the pod's length: a big, readable chili
RAD = 0.0135         # its radius at the shoulder
LABEL_COLOR = "#B8261E"     # the chili's crop colour (seed label, stake tag): chili red
SKIN = ("#A8141A", "#D42A1E", "#B01A16")     # shoulder, body, tip
WALL, PITH, SEED = "#C8261C", "#F0D8A0", "#F4E6B0"


def mats():
    return {
        "skin": fl.material("chili_skin", "#FFFFFF", rough=0.22, coat=0.45, sss=0.05, vertex_color=True),
        "flesh": fl.material("chili_flesh", "#FFFFFF", rough=0.35, sss=0.06, vertex_color=True),
        "cap": fl.material("chili_cap", "#3E7A2A", rough=0.5, sss=0.08),
        "stalk": fl.material("chili_stalk", "#4E8A30", rough=0.55),
        "stalk_cut": fl.material("chili_stalk_cut", "#B9C88A", rough=0.8),
    }


def profile(s):
    """The pod's radius a share ``s`` of the way from the shoulder (0) to the tip (1), as a share of the shoulder's."""
    shoulder = fl.smoothstep(0.0, 0.1, s) ** 0.5
    return (0.6 + 0.4 * shoulder) * (1 - 0.25 * s) * (1 - s ** 1.8) ** 0.75 + 0.03


def centre(s, length=L, hook=1.0):
    """The pod's centre line: straight from the cap along +X, then curving down (-Z) and hooking at the tip."""
    x = length * s
    z = -length * 0.22 * hook * s ** 2.6
    y = length * 0.05 * hook * math.sin(s * math.pi)
    p = Vector((x, y, z))
    ds = 1e-3
    q = Vector((length * (s + ds), length * 0.05 * hook * math.sin((s + ds) * math.pi), -length * 0.22 * hook * (s + ds) ** 2.6))
    return p, (q - p).normalized()


def pod_bm(length=L, rad=RAD, hook=1.0, seed=2, segs=(26, 34), ripe=True):
    """The pod: a three-lobed tube along centre(), closed at the shoulder and the tip."""
    nu, nv = segs
    bm = bmesh.new()
    rows, info = [], {}
    for j in range(nv + 1):
        s = j / nv
        p, t = centre(s, length, hook)
        n1 = Vector((0, 0, 1)) - t * t.z
        n1.normalize()
        n2 = t.cross(n1)
        r = rad * profile(s)
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            lobe = math.cos(3 * a + seed)
            rr = r * (1 + 0.06 * lobe * (1 - s) + 0.02 * math.sin(7 * a + 11 * s + seed))
            v = bm.verts.new(p + (n1 * math.cos(a) + n2 * math.sin(a)) * rr)
            row.append(v)
            info[v] = (s, lobe)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    for row, s, sign in ((rows[0], 0.0, -1), (rows[-1], 1.0, 1)):
        p, t = centre(s, length, hook)
        c = bm.verts.new(p + t * sign * rad * (0.25 if sign < 0 else 0.02))
        info[c] = (s, 0.0)
        for i in range(nu):
            bm.faces.new((row[i], row[(i + 1) % nu], c) if sign > 0 else (c, row[(i + 1) % nu], row[i]))
    bm.normal_update()
    if ripe:
        sh, body, tip = (fl.srgb(c) for c in SKIN)
    else:
        sh, body, tip = (fl.srgb(c) for c in ("#3E7A28", "#4E9030", "#3C7426"))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            s, lobe = info[lp.vert]
            c = fl.mix(sh, body, fl.smoothstep(0.0, 0.2, s))
            c = fl.mix(c, tip, fl.smoothstep(0.75, 1.0, s))
            c = fl.mix(c, sh, 0.35 * max(0.0, -lobe) ** 4)          # darker in the grooves between the lobes
            lp[col] = (*c, 1.0)
    return bm


def cap_bm(rad=RAD, seed=3):
    """The green cap over the shoulder: a cup with five short pointed lobes folded back onto the pod."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    nu, nv = 30, 6
    rows = []
    for j in range(nv + 1):
        t = j / nv
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            lobe = max(0.0, math.cos(5 * a / 2 + seed) ** 2) ** 3       # five points (a half-angle cosine squared)
            reach = rad * (0.45 + 0.7 * lobe * rnd.uniform(0.9, 1.0)) * t
            r = rad * (0.62 + 0.42 * math.sin(math.pi * min(1.0, t * 0.9)) ** 0.6) + 0.0006
            row.append(bm.verts.new((-rad * 0.32 + reach, r * math.cos(a), r * math.sin(a))))
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    c = bm.verts.new((-rad * 0.45, 0, 0))
    for i in range(nu):
        bm.faces.new((c, rows[0][(i + 1) % nu], rows[0][i]))
    bm.normal_update()
    return bm


def stalk_bm(rad=RAD, length=0.04, curl=1.0):
    """The stalk out of the cap, back along -X and curling up."""
    p0 = Vector((-rad * 0.4, 0, 0))
    pts = fl.bezier(p0, p0 + Vector((-length * 0.45, 0, 0)), p0 + Vector((-length * 0.8, 0, length * 0.25 * curl)),
                    p0 + Vector((-length * 0.95, 0, length * 0.6 * curl)), n=10)
    return fl.tube(pts, [0.0042 * (1 - 0.35 * i / 10) for i in range(11)], segs=8, cap_mat=1)


def build(name="Chili", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested chili: Body, Cap and Stalk under an empty named ``name``, lying on z = 0 (cap -X, tip +X); ``bites``
    (0..2) bitten off the tip."""
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    tilt = fl.empty(name + "_Lie", root)
    body = fl.from_bm(name + "_Body", pod_bm(), [m["skin"]], tilt)
    fl.from_bm(name + "_Cap", cap_bm(), [m["cap"]], tilt)
    fl.from_bm(name + "_Stalk", stalk_bm(length=0.028, curl=0.55), [m["stalk"], m["stalk_cut"]], tilt)
    for k in range(bites):
        bite(body, k, m)
    # lie it on its side: the hook flat on the ground, then sit it on z = 0
    tilt.rotation_euler = (math.radians(-90), 0, 0)
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in fl.tree(tilt) if o.type == "MESH" for v in o.data.vertices]
    tilt.location = (-(min(p.x for p in pts) + max(p.x for p in pts)) / 2, -(min(p.y for p in pts) + max(p.y for p in pts)) / 2,
                     -min(p.z for p in pts))
    return root


BITE_AT = (0.66, 0.4)


def bite(body, k, m):
    """Bite ``k`` (0 or 1) off the tip: a slanted scoop showing the red wall, the pale pith and its seeds."""
    s = BITE_AT[k]
    p, t = centre(s)
    big = 0.028
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=1)
    rot = t.to_track_quat("Z", "Y").to_matrix().to_4x4()
    tilt = Matrix.Rotation(math.radians(20 if k == 0 else -16), 4, "X")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(p) @ rot @ tilt @ Matrix.Translation((0, 0, big - 0.003))
                        @ Matrix.Diagonal((0.032, 0.032, big, 1)))
    wall, pith, seed = (fl.lin(c)[:3] for c in (WALL, PITH, SEED))
    rnd = random.Random(k)
    seeds = [Vector((rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4))) for _ in range(9)]

    def color_of(co):
        q = min(range(41), key=lambda i: (co - centre(i / 40.0)[0]).length) / 40.0
        pc, tc = centre(q)
        d = co - pc
        r = RAD * profile(q)
        rr = d.length / r
        c = fl.mix(pith, wall, fl.smoothstep(0.68, 0.78, rr))
        # the seeds: pale flat discs scattered through the pith
        n1 = Vector((0, 0, 1)) - tc * tc.z
        n1.normalize()
        n2 = tc.cross(n1)
        u = Vector((d.dot(n1), d.dot(n2))) / r
        for sd in seeds:
            if (u - sd).length < 0.11 and rr < 0.62:
                c = fl.mix(c, seed, 0.85)
        return c

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

STEM_H = 0.3
BRANCHES = [(0.06, 0.4, 0.5), (0.09, 2.5, 0.75), (0.12, 4.4, 1.0), (0.15, 1.4, 1.2), (0.18, 3.5, 1.45),
            (0.21, 5.5, 1.65), (0.24, 0.8, 1.85), (0.27, 2.9, 2.05)]          # (height, yaw, born)
PODS = [0, 1, 2, 3, 4, 5, 6, 7]


def leaf_bm(length, seed):
    """A chili leaf along +X from its base: a short stalk, then a glossy pointed oval blade, arching a little."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    pts = fl.bezier((0, 0, 0), (length * 0.3, 0, length * 0.18), (length * 0.7, 0, length * 0.15), (length, 0, -length * 0.05), n=12)
    stalk = fl.tube(pts[:4], [0.0014, 0.0012, 0.0011, 0.001], segs=5)
    ws, ns = [], []
    for i in range(3, 13):
        s = (i - 3) / 9
        ws.append(length * 0.3 * math.sin(math.pi * min(1.0, 0.06 + s * 0.94)) ** 0.75 * (1 - 0.35 * s) * rnd.uniform(0.95, 1.05) + 0.0004)
        tan = (pts[min(i + 1, 12)] - pts[i - 1]).normalized()
        ns.append(tan.cross(up).cross(tan).normalized() * -1)
    return stalk, fl.strip(pts[3:], ws, ns, fold=0.3)


def flower_bm(seed):
    """A small white star flower, five petals, facing down and out."""
    bms = []
    for k in range(5):
        a = 2 * math.pi * k / 5 + seed
        d = Vector((math.cos(a), math.sin(a), -0.35)).normalized()
        pts = [Vector((0, 0, 0)), d * 0.006, d * 0.011]
        bms.append(fl.strip(pts, [0.0006, 0.0028, 0.0008], [Vector((0, 0, -1))] * 3))
    return fl.join(bms)


class ChiliRig(crop_rig.CropRig):
    """A chili bush in one mound. Grow: a sprout, a stem rising with side branches and small glossy leaves, white star
    flowers, then chilies hanging under the branches, green and lengthening, red once ripe. Wilt: the leaves droop."""

    def __init__(self, m, label=None, stake=True, seed=13):
        super().__init__("ChiliPlant")
        self.m = m
        rnd = random.Random(seed)
        self.stem = fl.from_bm("Stem", fl.tube([(0, 0, 0), (0.003, -0.002, STEM_H * 0.5), (-0.002, 0.003, STEM_H)],
                                                [0.0075, 0.0055, 0.003], segs=8), [m["stem"]], self.sway)
        self.grow_obs.append(self.stem)
        self.branches = []
        for i, (z, yaw, born) in enumerate(BRANCHES):
            node = fl.empty(f"Branch{i}", self.sway, (0, 0, z))
            node.rotation_euler = (0, 0, yaw)
            Lb = 0.13 - 0.008 * i
            tipz = Lb * 0.45
            br = fl.from_bm(f"Branch{i}_Stick", fl.tube([(0, 0, 0), (Lb * 0.5, 0, Lb * 0.3), (Lb, 0, tipz)],
                                                          [0.0036, 0.0028, 0.0018], segs=6), [m["stem"]], node)
            leaves = []
            for k in range(4):
                at = (Lb * (0.25 + 0.24 * k), 0, Lb * (0.14 + 0.1 * k))
                w = self.wilt_node(f"Wilt{i}_{k}", node, at, (1 if k % 2 else -1) * 1.0 + rnd.uniform(-0.35, 0.35), droop=1.0 + 0.2 * k)
                st, bl = leaf_bm(0.09 + 0.012 * rnd.uniform(-1, 1), 70 + 4 * i + k)
                leaf = fl.from_bm(f"Leaf{i}_{k}", fl.join([st, bl]), [m["leaf"]], w)
                fl.solidify(leaf, 0.0007)
                leaves.append(leaf)
            tip = fl.empty(f"Branch{i}_Tip", node, (Lb * 0.8, 0, tipz * 0.85))
            flower = fl.from_bm(f"Flower{i}", flower_bm(i), [m["flower"]], tip)
            fl.solidify(flower, 0.0005)
            # the pod hangs from the branch on its stalk, tip down
            hang = fl.empty(f"Pod{i}", tip, (0, 0, -0.004))
            hang.rotation_euler = (0, math.radians(70 + rnd.uniform(-12, 12)), rnd.uniform(-0.4, 0.4))
            green = fl.from_bm(f"Pod{i}_green", fl.join([pod_bm(0.07, 0.0095, hook=0.6, seed=i, segs=(14, 16), ripe=False),
                                                          cap_bm(0.0095, seed=i)]), [m["pod_green"]], hang)
            red = fl.from_bm(f"Pod{i}_ripe", fl.join([pod_bm(0.075, 0.01, hook=0.6, seed=i, segs=(14, 16)),
                                                       cap_bm(0.01, seed=i)]), [m["skin"]], hang)
            self._cap_green(green, red)
            self.looks += [green, red]
            self.branches.append((node, br, leaves, flower, hang, born, z))
            self.grow_obs += [node, flower, hang] + leaves
        if stake:
            self.stake(m, label)

    def _cap_green(self, *pods):
        # the caps were joined after the pods: give their faces the cap's green
        for ob in pods:
            if "chili_cap" not in ob.data.materials:
                ob.data.materials.append(self.m["cap"])
            idx = list(ob.data.materials).index(self.m["cap"])
            n = 14 * 16 + 2 * 14
            for i, poly in enumerate(ob.data.polygons):
                if i >= n:
                    poly.material_index = idx

    def grow(self, g):
        height = fl.smoothstep(-0.2, 3.2, g)
        self.stem.scale = (max(1e-4, 0.2 + 0.8 * height),) * 3
        for node, br, leaves, flower, hang, born, z in self.branches:
            age = (g - born) / 1.2
            shown = fl.smoothstep(0.0, 0.15, age)
            s = min(1.0, max(0.0, age))
            node.location = (0, 0, z * (0.2 + 0.8 * height))
            node.scale = (max(1e-4, shown * (0.3 + 0.7 * s)),) * 3
            for leaf in leaves:
                leaf.scale = (max(1e-4, fl.smoothstep(0.05, 0.7, age)),) * 3
            # flowers at 2.1 to 2.9, then the pods set and lengthen, red at 3.6
            flower.scale = (max(1e-4, fl.smoothstep(2.0, 2.3, g) * (1 - fl.smoothstep(2.7, 3.0, g))),) * 3
            pod = fl.smoothstep(2.75, 3.6, g)
            hang.scale = (max(1e-4, pod * (0.35 + 0.65 * pod)),) * 3
        ripe = g >= 3.6
        for ob in self.looks:
            ob.scale = ((1.0 if ripe else 1e-4) if ob.name.endswith("_ripe") else (1e-4 if ripe else 1.0),) * 3


def plant_mats():
    m = mats()
    m.update({
        "stem": fl.material("chili_plant_stem", "#4F8A32", rough=0.6),
        "leaf": fl.material("chili_leaf", "#2F7428", rough=0.35, coat=0.12, sss=0.12),
        "flower": fl.material("chili_flower", "#F4F2E8", rough=0.5, sss=0.25),
        "pod_green": fl.material("chili_pod_green", "#FFFFFF", rough=0.25, coat=0.4, sss=0.05, vertex_color=True),
    })
    return m


def build_rig(label):
    return ChiliRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
