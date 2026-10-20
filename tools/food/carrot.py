"""The carrot: the harvested root (whole and bitten from the tip), and the carrot plant as a game rig.

The harvested carrot is built standing (crown at the origin, tip down -Z) and laid on its side by its root empty, so
the same root mesh stands in the soil on the plant. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

L = 0.17             # root length: a big, readable carrot
R0 = 0.021           # radius at the shoulder
LABEL_COLOR = "#E0B530"     # the carrot's crop colour (seed label, stake tag): mustard, never orange
SKIN = ("#9E7230", "#E0761A", "#E98A2A", "#B85E16")       # shoulder, body, tip, growth-ring dents
CORE, CORTEX = "#F8C46E", "#EA8430"                         # the cut face: a light core inside a deep cortex


def mats():
    return {
        "skin": fl.material("carrot_skin", "#FFFFFF", rough=0.5, sss=0.02, vertex_color=True),
        "flesh": fl.material("carrot_flesh", "#FFFFFF", rough=0.42, sss=0.03, vertex_color=True),
        "stalk": fl.material("carrot_stalk", "#6F9C3D", rough=0.6, sss=0.1),
        "frond": fl.material("carrot_frond", "#3E7E2C", rough=0.55, sss=0.15),
        "frond_dry": fl.material("carrot_frond_dry", "#86903C", rough=0.7, sss=0.05),
        "hair": fl.material("carrot_hair", "#D8A060", rough=0.8),
    }


def radius(s, r0=R0):
    """The root's radius a share ``s`` of the way from the crown (0) to the tip (1)."""
    shoulder = fl.smoothstep(0.0, 0.07, s) ** 0.35
    return r0 * (0.3 + 0.7 * shoulder) * (1 - s) ** 0.78 + 0.0008


RINGS = [0.11, 0.19, 0.26, 0.34, 0.41, 0.5, 0.58, 0.66, 0.73, 0.81, 0.88]


def root_bm(length=L, r0=R0, seed=4, segs=(28, 48)):
    """The root, standing: the crown's dome at z = 0, the tip at z = -length; growth-ring dents and a gentle bend."""
    rnd = random.Random(seed)
    nu, nv = segs
    bm = bmesh.new()
    rings_at = [(s + rnd.uniform(-0.015, 0.015), rnd.uniform(0, 2 * math.pi), rnd.uniform(1.2, 2.6)) for s in RINGS]
    dents = {}
    rows = []
    for j in range(nv + 1):
        s = j / nv
        z = -length * s
        bend = Vector((0.006 * math.sin(s * 2.6 + seed), 0.003 * math.sin(s * 3.3 + 2 * seed), 0))
        r = radius(s, r0)
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            dent = 0.0
            for rs, ra, span in rings_at:
                # each ring is a short groove part of the way round
                da = math.atan2(math.sin(a - ra), math.cos(a - ra))
                dent = max(dent, math.exp(-((s - rs) / 0.008) ** 2) * max(0.0, 1 - abs(da) / span))
            rr = r * (1 - 0.07 * dent) * (1 + 0.015 * math.sin(3 * a + seed))
            row.append(bm.verts.new(bend + Vector((rr * math.cos(a), rr * math.sin(a), z))))
            dents[row[-1]] = (s, dent)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    # the crown: a low dome over the top ring
    top = bm.verts.new((0, 0, 0.0035))
    dents[top] = (0.0, 0.0)
    for i in range(nu):
        bm.faces.new((top, rows[0][(i + 1) % nu], rows[0][i]))
    tip = bm.verts.new(rows[-1][0].co * 0 + Vector((0.006 * math.sin(2.6 + seed), 0.003 * math.sin(3.3 + 2 * seed), -length - 0.002)))
    dents[tip] = (1.0, 0.0)
    for i in range(nu):
        bm.faces.new((rows[-1][i], rows[-1][(i + 1) % nu], tip))
    bm.normal_update()
    shoulder, body, tipc, ring = (fl.srgb(c) for c in SKIN)
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            s, dent = dents[lp.vert]
            c = fl.mix(shoulder, body, fl.smoothstep(0.0, 0.1, s))
            c = fl.mix(c, tipc, fl.smoothstep(0.6, 1.0, s))
            c = fl.mix(c, ring, 0.75 * dent)
            lp[col] = (*c, 1.0)
    return bm


def lacy(base, d, n, length, width, seed, lobes=4):
    """One carrot pinna: a narrow blade cut deep into lobes, so the frond reads as feathery."""
    rnd = random.Random(seed)
    N = 2 * lobes + 1
    pts, ws, ns = [], [], []
    for i in range(N + 1):
        s = i / N
        pts.append(base + d * (length * s) - n * (0.15 * length * s * s))
        lobe = 1.0 if i % 2 else 0.22
        ws.append(width * lobe * (1 - 0.7 * s) * rnd.uniform(0.85, 1.1) + 0.0003)
        ns.append(n)
    return fl.strip(pts, ws, ns, fold=0.2)


def frond(length, seed, arch=1.0):
    """A carrot leaf, from its base at the origin along +X rising steeply: a pale stalk, then pairs of lacy pinnae."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    out = Vector((1, 0, 0))
    p0 = Vector((0, 0, 0))
    pts = fl.bezier(p0, p0 + up * length * 0.45, p0 + up * length * 0.75 + out * length * 0.35 * arch,
                    p0 + up * length * (0.78 - 0.18 * arch) + out * length * 0.7 * arch, n=12)
    stalk = fl.tube(pts, [0.0024 * (1 - 0.6 * i / 12) + 0.0006 for i in range(13)], segs=6)
    leaves = []
    pairs = 6
    for k in range(pairs):
        t = 0.38 + 0.6 * k / pairs
        i = min(11, int(t * 12))
        p = pts[i]
        tan = (pts[i + 1] - pts[i]).normalized()
        side = tan.cross(up).normalized() if abs(tan.dot(up)) < 0.98 else Vector((0, 1, 0))
        for sgn in (1, -1):
            d = (tan * 0.55 + side * sgn * 0.85).normalized()
            n = (d.cross(tan.cross(d)) if False else (up - d * d.dot(up))).normalized()
            ln = length * 0.2 * (1 - 0.55 * k / pairs) * rnd.uniform(0.9, 1.1)
            leaves.append(lacy(p, d, n, ln, ln * 0.2, seed + 3 * k + (sgn > 0)))
    tip = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    leaves.append(lacy(tip, d, (up - d * d.dot(up)).normalized(), length * 0.12, length * 0.025, seed + 99, lobes=3))
    return stalk, leaves


def hairs(seed=4, length=L, r0=R0):
    rnd = random.Random(seed)
    bms = []
    for _ in range(7):
        s = rnd.uniform(0.15, 0.4)
        a = rnd.uniform(0, 2 * math.pi)
        r = radius(s, r0)
        p = Vector((0.82 * r * math.cos(a), 0.82 * r * math.sin(a), -length * s))   # rooted under the skin
        out = Vector((math.cos(a), math.sin(a), -0.6)).normalized()
        q = p + out * rnd.uniform(0.008, 0.016)
        bms.append(fl.tube([p - out * 0.001, (p + q) / 2 + Vector((0, 0, -0.002)), q], [0.0006, 0.0005, 0.0003], segs=5))
    return fl.join(bms)


def build(name="Carrot", bites=0, parent=None, location=(0, 0, 0), m=None, standing=False):
    """A harvested carrot: Body, Hairs, Stalks and Fronds under an empty named ``name``, lying on its side (crown toward
    -X, tip toward +X, resting on z = 0) unless ``standing``; ``bites`` (0..2) bitten off the tip."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    if not standing:
        root.rotation_euler = (0, -math.pi / 2, 0)     # crown -X, tip +X
        root.location = Vector(location) + Vector((-L / 2, 0, R0 * 0.93))
    body = fl.from_bm(name + "_Body", root_bm(), [m["skin"]], root)
    fl.from_bm(name + "_Hairs", hairs(), [m["hair"]], root)
    stalks, leaves = [], []
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        st, lv = frond(0.1 * (0.85 + 0.1 * (k % 3)), seed=20 + k, arch=0.7)
        turn = Matrix.Translation((0.0025 * math.cos(a), 0.0025 * math.sin(a), 0.002)) @ Matrix.Rotation(a, 4, "Z") \
            @ Matrix.Rotation(math.radians(-14), 4, "Y")
        for b in [st] + lv:
            bmesh.ops.transform(b, matrix=turn, verts=b.verts)
        stalks.append(st)
        leaves += lv
    fl.from_bm(name + "_Stalks", fl.join(stalks), [m["stalk"]], root)
    fr = fl.from_bm(name + "_Fronds", fl.join(leaves), [m["frond"]], root)
    fl.solidify(fr, 0.0007)
    for k in range(bites):
        bite(body, k, m)
    return root


BITE_AT = (0.64, 0.42)       # bites take the tip off at these shares of the length


def bite(body, k, m):
    """Bite ``k`` (0 or 1) off the tip: a scooped, slightly slanted face showing the light core."""
    s = BITE_AT[k]
    z = -L * s
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1)
    # an ellipsoid swallowing the rest of the tip, its rounded top the bite: a scoop, tilted so it is not a saw cut
    tilt = Matrix.Rotation(math.radians(18 if k == 0 else -15), 4, "X")
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation((0, 0, z + 0.007)) @ tilt
                        @ Matrix.Translation((0, 0, -L)) @ Matrix.Diagonal((0.04, 0.04, L, 1)))
    core, cortex = fl.lin(CORE)[:3], fl.lin(CORTEX)[:3]

    def color_of(co):
        sz = min(1.0, max(0.0, -co.z / L))
        rr = math.hypot(co.x - 0.006 * math.sin(sz * 2.6 + 4), co.y) / radius(sz)
        return fl.mix(core, cortex, fl.smoothstep(0.35, 0.6, rr))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

CROWNS = [((-0.02, 0.035), 1.0, 0.0), ((-0.06, -0.015), 0.92, 0.12), ((0.0, -0.04), 0.96, 0.06)]
FRONDS = 7
RISE = (-0.03, 0.013)      # the root's crown under the soil when young, its orange shoulder showing when ripe


class CarrotRig(crop_rig.CropRig):
    """Three carrots in one mound. Grow: seed leaves, then feathery fronds one after another, the roots swelling and
    their shoulders rising out of the soil once ripe. Wilt: the fronds droop."""

    def __init__(self, m, label):
        super().__init__("CarrotPlant")
        self.m = m
        self.crowns = []
        rnd = random.Random(5)
        root_mesh = None
        for c, ((x, y), size, delay) in enumerate(CROWNS):
            crown = fl.empty(f"Crown{c}", self.sway, (x, y, 0))
            rb = root_bm(segs=(20, 32), seed=4 + c)
            root = fl.from_bm(f"Root{c}", rb, [m["skin"]], crown)
            root_mesh = root_mesh or root.data
            fronds = []
            for k in range(FRONDS):
                yaw = c * 1.3 + 2 * math.pi * k / FRONDS + rnd.uniform(-0.25, 0.25)
                w = self.wilt_node(f"Wilt{c}_{k}", crown, (0, 0, 0), yaw, droop=1.0 + 0.2 * (k % 2))
                ln = 0.25 * size * rnd.uniform(0.82, 1.05) * (0.8 + 0.2 * (k / FRONDS))
                st, lv = frond(ln, seed=100 * c + k, arch=rnd.uniform(0.8, 1.15))
                stalk = fl.from_bm(f"Frond{c}_{k}", fl.join([st]), [m["stalk"]], w)
                leaves = fl.from_bm(f"Frond{c}_{k}_Leaves", fl.join(lv), [m["frond"]], stalk)
                fl.solidify(leaves, 0.0007)
                fronds.append((stalk, 0.35 + 0.38 * k + delay, w))
            cot = [fl.strip([Vector((0, 0, 0)) + Vector((0.4 * sg, sg * 0.15, 1)).normalized() * (0.05 * t / 6) for t in range(7)],
                            [0.0016 * math.sin(math.pi * min(1, t / 6 * 0.9 + 0.1)) + 0.0003 for t in range(7)],
                            [(sg * 0.15, -0.4 * sg, 0)] * 7) for sg in (1, -1)]
            cots = fl.from_bm(f"Cot{c}", fl.join(cot), [m["frond"]], crown)
            fl.solidify(cots, 0.0007)
            self.crowns.append((crown, root, fronds, cots, size, delay))
            self.grow_obs += [root, cots] + [f for f, _, _ in fronds]
        self.stake(m, label)

    def grow(self, g):
        for crown, root, fronds, cots, size, delay in self.crowns:
            gc = max(0.0, g - delay)
            swell = fl.smoothstep(0.3, 3.9, gc)
            rise = fl.mix((RISE[0],), (RISE[1],), fl.smoothstep(2.5, 3.9, gc))[0]
            root.location = (0, 0, rise)
            root.scale = (max(1e-4, (0.15 + 0.85 * swell) * size),) * 3
            for stalk, born, w in fronds:
                age = (gc - born) / 1.3
                shown = fl.smoothstep(0.0, 0.12, age)
                s = min(1.0, max(0.0, age))
                stalk.scale = (max(1e-4, shown * (0.25 + 0.75 * s)),) * 3
                stalk.rotation_euler = (0, -math.radians(40) * (1 - fl.smoothstep(0.1, 1.0, s)), 0)
                w.location = (0, 0, rise + 0.004)
            cots.scale = (max(1e-4, fl.smoothstep(-0.3, 0.15, gc) * (1 - fl.smoothstep(1.6, 2.4, gc))),) * 3
            cots.location = (0, 0, rise * 0.2)


def build_rig(label):
    rig = CarrotRig(dict(mats(), **_stake_mats()), label)
    return rig


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
