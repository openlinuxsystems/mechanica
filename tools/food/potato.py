"""The potato: the harvested tuber (whole and bitten), and the potato plant as a game rig.

Z up, metres, the tuber's origin at the middle of its base.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

SIZE = (0.105, 0.072, 0.062)        # length, width, height: a big baking potato
LABEL_COLOR = "#7A4A30"             # the potato's crop colour (seed label, stake tag): rust
SKIN, MOTTLE, EYE = "#A97F4E", "#7E5C38", "#553D24"
FLESH_LIGHT, FLESH_RIM, FLESH_EDGE = "#D8C898", "#C4AD78", "#A0794A"   # middle, outer flesh, the thin skin edge


def mats():
    return {
        "skin": fl.material("potato_skin", "#FFFFFF", rough=0.85, vertex_color=True),
        "flesh": fl.material("potato_flesh", "#FFFFFF", rough=0.55, sss=0.02, vertex_color=True),
        "stem": fl.material("potato_stem", "#5D8A36", rough=0.6),
        "leaf": fl.material("potato_leaf", "#3D7A2F", rough=0.55, sss=0.12),
        "leaf_yellow": fl.material("potato_leaf_yellow", "#A9A23E", rough=0.65, sss=0.08),
        "petal": fl.material("potato_petal", "#EDE6F4", rough=0.5, sss=0.25),
        "petal_eye": fl.material("potato_flower_eye", "#F0C431", rough=0.5),
    }


def body_bm(scale=1.0, seed=2, segs=(40, 26)):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1)
    bumps = [(Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized(), rnd.uniform(-0.09, 0.12))
             for _ in range(14)]
    eyes = [Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.5, 1))).normalized() for _ in range(7)]
    eye_of = {}
    lx, ly, lz = (v * scale / 2 for v in SIZE)
    for v in bm.verts:
        n = v.co.normalized()
        r = 1.0
        for d, a in bumps:
            r += a * max(0, n.dot(d)) ** 3
        e = max((max(0.0, n.dot(q) - 0.975) / 0.025 for q in eyes), default=0.0)
        r -= 0.07 * e
        bend = 0.08 * n.x * n.x                     # a slight kidney bend
        v.co = Vector((n.x * lx, n.y * ly + bend * ly, n.z * lz)) * r
        eye_of[v] = e
    lo = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= lo
    bm.normal_update()
    skin, mottle, eye = (fl.srgb(c) for c in (SKIN, MOTTLE, EYE))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            p = lp.vert.co
            k = 0.5 + 0.5 * fl.noise3(p, 90.0, seed)
            c = fl.mix(skin, mottle, 0.55 * fl.smoothstep(0.45, 0.9, k))
            speck = fl.noise3(p, 420.0, seed + 3)
            c = fl.mix(c, mottle, 0.6 * fl.smoothstep(0.75, 0.95, speck))
            c = fl.mix(c, eye, min(1.0, 1.6 * eye_of[lp.vert]))
            lp[col] = (*c, 1.0)
    return bm


def build(name="Potato", bites=0, parent=None, location=(0, 0, 0), m=None, scale=1.0, seed=2, segs=(40, 26)):
    """A potato: its Body under an empty named ``name``; ``bites`` (0..2) taken out of its +X end."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", body_bm(scale, seed, segs), [m["skin"]], root)
    for k in range(bites):
        bite(body, k, m)
    return root


BITES = ((0.05, -0.012, 0.042, 0.0), (0.042, 0.012, 0.044, 0.3))   # where each bite goes (x, y, z) and how it turns


def _bite_axis(k):
    """Bite ``k``'s centre and the direction it goes in."""
    x, y, z, _ = BITES[k]
    where = Vector((x, y, z))
    out = (where - Vector((0, 0, SIZE[2] / 2))).normalized()
    return where + out * 0.012, out


def bite(body, k, m):
    """A mouth-sized hollow into the end of the potato, showing pale, wet flesh."""
    turn = BITES[k][3]
    c0, out = _bite_axis(k)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4() @ Matrix.Rotation(turn, 4, "Z")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(c0) @ rot @ Matrix.Diagonal((0.025, 0.018, 0.03, 1)))
    light, rim, edge = (fl.lin(c)[:3] for c in (FLESH_LIGHT, FLESH_RIM, FLESH_EDGE))

    axes = [_bite_axis(j) for j in range(k + 1)]     # every bite so far: the cut recolours all the flesh

    def color_of(co):
        r = min(((co - c) - o * (co - c).dot(o)).length for c, o in axes)
        return fl.mix(fl.mix(light, rim, fl.smoothstep(0.006, 0.018, r)), edge, fl.smoothstep(0.021, 0.026, r))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

STEMS = 3
NODES = 6
SEG = 0.042
TUBERS = [(0.075, 0.2, 0.85, 7), (0.07, 2.3, 0.75, 8), (0.08, 4.1, 0.9, 9), (0.06, 5.4, 0.65, 10)]


def leaflet(base, d, n, length, width, curl):
    pts, ws, ns = [], [], []
    for i in range(11):
        s = i / 10
        pts.append(base + d * (length * s) - n * (curl * length * s * s))
        ws.append(width * math.sin(math.pi * min(1, s * 0.9 + 0.1)) ** 0.7 + 0.0004)
        ns.append(n)
    return fl.strip(pts, ws, ns, fold=0.3)


def leaf(length, seed):
    """A potato leaf along +X, rising a little: a rachis with three pairs of broad leaflets and a terminal one."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    pts = fl.bezier((0, 0, 0), (length * 0.3, 0, length * 0.25), (length * 0.7, 0, length * 0.25), (length, 0, length * 0.02), n=10)
    bms = [fl.tube(pts, [0.0022 * (1 - 0.6 * i / 10) + 0.0006 for i in range(11)], segs=6)]
    for k in range(3):
        i = 3 + 2 * k
        p = pts[i]
        tan = (pts[i + 1] - pts[i]).normalized()
        sd = tan.cross(up).normalized()
        for sgn in (1, -1):
            d = (tan * 0.45 + sd * sgn * 0.9).normalized()
            n = (up - d * d.dot(up)).normalized()
            ln = length * (0.36 - 0.03 * k) * rnd.uniform(0.9, 1.1)
            bms.append(leaflet(p, d, n, ln, ln * 0.27, curl=0.22))
    d = (pts[-1] - pts[-2]).normalized()
    bms.append(leaflet(pts[-1], d, (up - d * d.dot(up)).normalized(), length * 0.4, length * 0.13, curl=0.2))
    return fl.join(bms)


def flower(p, rot, m):
    bms, eyes = [], []
    for k in range(5):
        a = rot + 2 * math.pi * k / 5
        d = Vector((math.cos(a), math.sin(a), 0.25)).normalized()
        tip = p + d * 0.013
        pts = fl.bezier(p, p + d * 0.004, tip - Vector((0, 0, 0.001)), tip, n=5)
        bms.append(fl.strip(pts, [0.0045 * math.sin(math.pi * min(1, i / 5 * 0.85 + 0.15)) + 0.0004 for i in range(6)],
                            [(0, 0, 1)] * 6))
    eyes.append(fl.tube([p, p + Vector((0, 0, 0.006))], [0.0022, 0.0008], segs=8))
    return bms, eyes


class PotatoRig(crop_rig.CropRig):
    """A potato bush in one mound: three leafy stems, white flowers, then the tops yellow from the bottom and the
    tubers push up out of the soil round its base. Wilt: every leaf droops."""

    def __init__(self, m, label):
        super().__init__("PotatoPlant")
        self.m = m
        self.stems = []
        rnd = random.Random(9)
        for s in range(STEMS):
            yaw = 0.5 + 2 * math.pi * s / STEMS
            base = fl.empty(f"Stem{s}", self.sway, (0.012 * math.cos(yaw) - 0.02, 0.012 * math.sin(yaw), 0))
            base.rotation_euler = (0, math.radians(14), yaw)
            nodes = []
            for i in range(NODES):
                node = fl.empty(f"Node{s}_{i}", base, (0, 0, SEG * i))
                r0 = 0.0062 * (1 - 0.5 * i / NODES) + 0.001
                r1 = 0.0062 * (1 - 0.5 * (i + 1) / NODES) + 0.001
                seg = fl.from_bm(f"Seg{s}_{i}", fl.tube([(0, 0, 0), (0.002 * math.sin(i + s), 0, SEG / 2), (0, 0, SEG)],
                                                         [r0, (r0 + r1) / 2, r1], segs=8), [m["stem"]], node)
                a = i * 2.39996 + s
                w = self.wilt_node(f"Wilt{s}_{i}", node, (0, 0, SEG), a, droop=1.0)
                ln = 0.15 - 0.008 * i
                if i <= 1:
                    # the lowest leaves yellow as the crop ripens: a green and a yellow look
                    lf = fl.empty(f"Leaf{s}_{i}", w)
                    lb = leaf(ln, 30 * s + i)
                    green = fl.from_bm(f"Leaf{s}_{i}_green", lb, [m["leaf"]], lf)
                    yellow = fl.from_bm(f"Leaf{s}_{i}_yellow", leaf(ln, 30 * s + i), [m["leaf_yellow"]], lf)
                    for o in (green, yellow):
                        fl.solidify(o, 0.0008)
                    self.looks += [green, yellow]
                    looks = (green, yellow)
                else:
                    lf = fl.from_bm(f"Leaf{s}_{i}", leaf(ln, 30 * s + i), [m["leaf"]], w)
                    fl.solidify(lf, 0.0008)
                    looks = None
                nodes.append((node, seg, w, lf, looks))
                self.grow_obs += [node, seg, lf]
            # the flower cluster at the top
            top = fl.empty(f"FlowerTop{s}", base, (0, 0, SEG * NODES))
            pb, eyes = [], []
            for k in range(4):
                fa = rnd.uniform(0, 6.3)
                p = Vector((0.018 * math.cos(fa), 0.018 * math.sin(fa), 0.012 + 0.006 * k))
                pb.append(fl.tube([(0, 0, 0), p * 0.5 + Vector((0, 0, 0.006)), p], [0.0011] * 3, segs=5))
                petals, eye = flower(p, rnd.uniform(0, 6.3), m)
                pb += []
                eyes += eye
                pf = fl.from_bm(f"Petals{s}_{k}", fl.join(petals), [m["petal"]], top, smooth=False)
                fl.solidify(pf, 0.0006)
            fl.from_bm(f"Pedicels{s}", fl.join(pb), [m["stem"]], top)
            fl.from_bm(f"Eyes{s}", fl.join(eyes), [m["petal_eye"]], top)
            self.stems.append((base, nodes, top))
            self.grow_obs += [base, top]
        self.tubers = []
        for k, (r, a, size, seed) in enumerate(TUBERS):
            t = build(f"Tuber{k}", parent=self.sway, location=(r * math.cos(a) - 0.02, r * math.sin(a), 0), m=m,
                      scale=size, seed=seed, segs=(24, 16))
            self.tubers.append((t, a, size))
            self.grow_obs.append(t)
        self.stake(m, label)

    def grow(self, g):
        L = 0.04 + (NODES * SEG) * fl.smoothstep(-0.4, 3.0, g)       # the stem's length so far
        yellow = g >= 3.7
        for s, (base, nodes, top) in enumerate(self.stems):
            gs = max(0.0, g - 0.15 * s)
            Ls = 0.04 + (NODES * SEG) * fl.smoothstep(-0.4, 3.0, gs)
            base.scale = (1, 1, 1)
            z = 0.0
            for i, (node, seg, w, lf, looks) in enumerate(nodes):
                f = min(1.0, max(0.0, (Ls - SEG * i) / SEG))
                node.location = (0, 0, z)
                seg.scale = (max(f, 1e-4),) * 3
                w.location = (0, 0, SEG * f)
                z += SEG * f
                age = (Ls - SEG * (i + 0.6)) / 0.1
                shown = fl.smoothstep(0.0, 0.15, age)
                sz = min(1.0, max(0.0, age))
                lf.scale = (max(1e-4, shown * (0.25 + 0.75 * sz)),) * 3
                lf.rotation_euler = (0, -math.radians(55) * (1 - fl.smoothstep(0.2, 1.0, sz)), 0)
                if looks:
                    looks[0].scale = (1e-4 if yellow else 1.0,) * 3
                    looks[1].scale = (1.0 if yellow else 1e-4,) * 3
            top.location = (0, 0, z)
            bloom = fl.smoothstep(2.4, 2.9, gs) * (1 - fl.smoothstep(3.3, 3.7, gs))
            top.scale = (max(1e-4, bloom),) * 3
        for t, a, size in self.tubers:
            show = fl.smoothstep(2.8, 3.9, g)
            t.location = (t.location.x, t.location.y, -0.05 * size + 0.032 * size * show)
            t.scale = (max(1e-4, 0.3 + 0.7 * show),) * 3
            t.rotation_euler = (0.25, -0.2, a)


def build_rig(label):
    import plant
    pm = plant.mats()
    m = dict(mats(), **{k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")})
    return PotatoRig(m, label)
