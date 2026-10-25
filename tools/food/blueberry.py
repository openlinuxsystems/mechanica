"""The blueberry (crops map, batch K4): the harvested berry (whole and bitten), and the blueberry bush as a game rig.

The berry is one big, readable blueberry: a round, slightly flattened berry in deep blue under a dusty pale bloom, a
five-pointed crown sunk into its top. Bites take its +X side off and show the pale green flesh going violet toward the
skin, with a few tiny seeds. The plant is a knee-high bush (crops map: bushes are knee high): woody canes from the soil,
small oval leaves, clusters of white bell flowers, then bunches of berries at the cane tips, pale green, blue once ripe.
Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

R = 0.033            # the berry's radius: a big, readable blueberry (6.6 cm across)
SQUASH = 0.86        # its height over its width
LABEL_COLOR = "#3A4E9A"     # the blueberry's crop colour (seed label, stake tag): berry blue
SKIN, BLOOM, CROWN = "#16205A", "#56669E", "#20142A"
FLESH, FLESH_RIM, SEED = "#BCCB9A", "#5E2E78", "#7E6A42"
CROWN_DEPTH = 0.22   # how deep the crown sinks, as a share of the radius


def mats():
    return {
        "skin": fl.material("blueberry_skin", "#FFFFFF", rough=0.58, coat=0.08, sss=0.04, vertex_color=True),
        "flesh": fl.material("blueberry_flesh", "#FFFFFF", rough=0.28, sss=0.1, vertex_color=True),
    }


def _shape(n, rad, seed):
    """The berry's radius along the unit direction ``n``: round, flattened, a crown cupped into its top with five
    sepal points standing round the rim, and a faint dimple underneath."""
    theta = math.acos(max(-1.0, min(1.0, n.z)))           # 0 at the top
    phi = math.atan2(n.y, n.x)
    r = rad * (1 + 0.025 * math.sin(3 * phi + seed) * math.sin(theta) + 0.012 * math.sin(5 * phi + 2 * theta))
    cup = 0.34
    if theta < cup:
        r *= 1 - CROWN_DEPTH * (1 - theta / cup) ** 1.6
    # the sepals: five points standing up round the cup's rim
    rim = math.exp(-((theta - cup) / 0.07) ** 2)
    points = max(0.0, math.cos(5 * phi / 2 + seed) ** 2) ** 4
    r *= 1 + 0.07 * rim * points - 0.02 * rim
    if theta > math.pi - 0.25:                           # the dimple where it hung on its stalk
        r *= 1 - 0.04 * (1 - (math.pi - theta) / 0.25)
    return r, theta, phi, rim * points


def berry_bm(rad=R, seed=2, segs=(40, 30), ripe=True):
    """The berry, its base at z = 0 (crown up); vertex colours: blue under a mottled bloom, the crown dark."""
    nu, nv = segs
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=nu, v_segments=nv, radius=1)
    info = {}
    for v in bm.verts:
        n = v.co.normalized()
        r, theta, phi, sepal = _shape(n, rad, seed)
        v.co = Vector((n.x * r, n.y * r, n.z * r * SQUASH))
        info[v] = (theta, sepal)
    lo = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= lo
    bm.normal_update()
    if ripe:
        skin, bloom, crown = (fl.srgb(c) for c in (SKIN, BLOOM, CROWN))
    else:
        skin, bloom, crown = (fl.srgb(c) for c in ("#4E8030", "#7C9E52", "#4A5A28"))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            theta, sepal = info[lp.vert]
            k = 0.5 + 0.5 * fl.noise3(lp.vert.co, 1.0 / rad * 3.2, seed)
            c = fl.mix(skin, bloom, 0.1 + 0.4 * fl.smoothstep(0.3, 0.9, k))
            c = fl.mix(c, crown, 0.8 * (1 - fl.smoothstep(0.18, 0.36, theta)))
            c = fl.mix(c, fl.mix(skin, crown, 0.4), 0.5 * sepal)
            lp[col] = (*c, 1.0)
    return bm


def build(name="Blueberry", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested blueberry: Body under an empty named ``name``, sitting crown up on z = 0; ``bites`` (0..2) bitten out
    of its +X side."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", berry_bm(), [m["skin"]], root)
    for k in range(bites):
        bite(body, k, m)
    return root


BITES = ((0.0, 0.25), (-0.75, -0.05))     # each bite's turn about the berry's axis from +X (rad) and its rise


def bite(body, k, m):
    """A mouth-sized scoop out of the berry's side: pale green flesh, violet toward the skin, a few tiny seeds."""
    turn, rise = BITES[k]
    mid = Vector((0, 0, R * SQUASH))
    out = Vector((math.cos(turn), math.sin(turn), rise)).normalized()
    c0 = mid + out * R * (1.04 if k == 0 else 0.86)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=20, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(c0) @ rot @ Matrix.Diagonal((0.026, 0.022, 0.024, 1)))
    flesh, rim, seed = (fl.lin(c)[:3] for c in (FLESH, FLESH_RIM, SEED))
    rnd = random.Random(5 + k)
    seeds = [mid + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 0.6))).normalized() * R * rnd.uniform(0.25, 0.55)
             for _ in range(14)]

    def color_of(co):
        d = co - mid
        rr = math.sqrt(d.x * d.x + d.y * d.y + (d.z / SQUASH) ** 2) / R
        c = fl.mix(flesh, rim, fl.smoothstep(0.55, 0.92, rr) ** 1.3)
        for s in seeds:
            if (co - s).length < 0.0022:
                c = fl.mix(c, seed, 0.8)
        return c

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

CANES = [(0.3, 0.30, 0.42, 0.2), (1.2, 0.26, 0.6, 0.4), (2.1, 0.31, 0.48, 0.6), (3.0, 0.25, 0.66, 0.8),
         (3.9, 0.29, 0.5, 1.0), (4.8, 0.27, 0.62, 1.2), (5.6, 0.32, 0.4, 1.4)]     # (yaw, height, lean, born)
LEAVES_PER_CANE = 9
FRUIT_BORN = 2.7
RIPE_AT = 3.6


def cane_points(h, lean, n=10):
    """A cane from the soil: up and out along +X, a little arched."""
    return fl.bezier((0, 0, 0), (h * lean * 0.1, 0, h * 0.4), (h * lean * 0.7, 0, h * 0.8), (h * lean, 0, h), n=n)


def leaf_bm(length, seed):
    """A blueberry leaf along +X from its base: a short stalk and a small, pointed oval blade, slightly cupped."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    pts = fl.bezier((0, 0, 0), (length * 0.3, 0, length * 0.12), (length * 0.7, 0, length * 0.1), (length, 0, -length * 0.04), n=9)
    stalk = fl.tube(pts[:3], [0.0011, 0.001, 0.0009], segs=5)
    ws, ns = [], []
    for i in range(2, 10):
        s = (i - 2) / 7
        ws.append(length * 0.27 * math.sin(math.pi * min(1.0, 0.08 + s * 0.92)) ** 0.8 * rnd.uniform(0.92, 1.06) + 0.0004)
        tan = (pts[min(i + 1, 9)] - pts[i - 1]).normalized()
        ns.append(tan.cross(up).cross(tan).normalized() * -1)
    return fl.join([stalk, fl.strip(pts[2:], ws, ns, fold=0.28)])


def cluster_bm(n, seed, ripe):
    """A bunch of ``n`` small berries round the end of a short stalk, hanging below it."""
    rnd = random.Random(seed)
    bms = []
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.3, 0.3)
        out = 0.006 + 0.006 * (k % 2)
        at = Vector((math.cos(a) * out, math.sin(a) * out, -0.004 - 0.008 * (k % 3) / 2))
        size = 0.0072 * rnd.uniform(0.85, 1.12)
        b = berry_bm(size, seed=seed + k, segs=(12, 9), ripe=ripe if k else ripe)
        if not ripe and k % 3 == 1:
            # a few of the unripe ones blush pink before they go blue
            col = b.loops.layers.color["Col"]
            for f in b.faces:
                for lp in f.loops:
                    c = lp[col]
                    lp[col] = (min(1.0, c[0] * 1.5), c[1] * 0.75, c[2] * 0.95, 1.0)
        tilt = Matrix.Rotation(rnd.uniform(2.4, 3.0), 4, "X" if k % 2 else "Y")       # the crowns face down and out
        bmesh.ops.transform(b, matrix=Matrix.Translation(at) @ tilt @ Matrix.Translation((0, 0, -size * SQUASH)), verts=b.verts)
        bms.append(b)
        stalk = fl.tube([Vector((0, 0, 0)), at * 0.6 + Vector((0, 0, 0.002)), at], [0.0007, 0.0006, 0.0005], segs=4)
        if ripe:
            col = stalk.loops.layers.color.new("Col")
            for f in stalk.faces:
                for lp in f.loops:
                    lp[col] = (*fl.srgb("#7A4A50"), 1.0)
        else:
            col = stalk.loops.layers.color.new("Col")
            for f in stalk.faces:
                for lp in f.loops:
                    lp[col] = (*fl.srgb("#7A9A4A"), 1.0)
        bms.append(stalk)
    return fl.join(bms)


def lathe(profile, segs):
    """A surface of revolution round Z through the (radius, z) points of ``profile``, closed at its first point."""
    bm = bmesh.new()
    rings = [[bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)]
             for r, z in profile]
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], b[k], b[(k + 1) % segs], a[(k + 1) % segs]))
    bm.faces.new(list(reversed(rings[0])))
    bm.normal_update()
    return bm


def bells_bm(n, seed):
    """A cluster of small white bell flowers hanging from the end of a cane."""
    rnd = random.Random(seed)
    bms = []
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.3, 0.3)
        at = Vector((math.cos(a) * 0.007, math.sin(a) * 0.007, -0.006 - 0.004 * (k % 2)))
        b = lathe([(0.0012, 0.0), (0.0032, -0.0015), (0.0042, -0.0045), (0.0038, -0.0072), (0.0046, -0.0082)], 10)
        tilt = Matrix.Rotation(rnd.uniform(-0.4, 0.4), 4, "X") @ Matrix.Rotation(rnd.uniform(-0.4, 0.4), 4, "Y")
        bmesh.ops.transform(b, matrix=Matrix.Translation(at) @ tilt, verts=b.verts)
        bms.append(b)
    return fl.join(bms)


class BlueberryRig(crop_rig.CropRig):
    """A blueberry bush in one mound. Grow: a few shoots, then woody canes rising and arching out with small oval leaves,
    white bell flowers at their tips, then bunches of berries, pale green, swelling and blue once ripe. Wilt: the leaves
    droop along the canes."""

    def __init__(self, m, label=None, stake=True, seed=17):
        super().__init__("BlueberryPlant")
        self.m = m
        rnd = random.Random(seed)
        self.canes = []
        for i, (yaw, h, lean, born) in enumerate(CANES):
            node = fl.empty(f"Cane{i}", self.sway, (0.018 * math.cos(yaw), 0.018 * math.sin(yaw), 0))
            node.rotation_euler = (0, 0, yaw)
            pts = cane_points(h, lean)
            stick = fl.from_bm(f"Cane{i}_Stick", fl.tube(pts, [0.0048 - 0.003 * k / 10 for k in range(11)], segs=7), [m["cane"]], node)
            leaves = []
            for k in range(LEAVES_PER_CANE):
                q = 0.3 + 0.62 * k / (LEAVES_PER_CANE - 1)
                at = pts[int(q * 10)]
                side = (1 if k % 2 else -1) * (1.1 + rnd.uniform(-0.3, 0.3))
                w = self.wilt_node(f"Wilt{i}_{k}", node, tuple(at), side, droop=1.0 + 0.3 * (k % 3))
                leaf = fl.from_bm(f"Leaf{i}_{k}", leaf_bm(0.068 + 0.012 * rnd.uniform(-1, 1), 40 + 7 * i + k), [m["leaf"]], w)
                fl.solidify(leaf, 0.0006)
                leaves.append((leaf, q))
            # a side twig half way up, and the bunches at the twig's and the cane's ends
            twig_node = fl.empty(f"Twig{i}", node, tuple(pts[6]))
            twig_node.rotation_euler = (0, 0, (0.9 if i % 2 else -0.9))
            tpts = fl.bezier((0, 0, 0), (0.02, 0, 0.02), (0.045, 0, 0.035), (0.06, 0, 0.03), n=6)
            twig = fl.from_bm(f"Twig{i}_Stick", fl.tube(tpts, [0.0024, 0.0021, 0.0018, 0.0016, 0.0014, 0.0013, 0.0012], segs=6),
                              [m["cane"]], twig_node)
            bunches = []
            for b, (holder, tip) in enumerate(((node, pts[-1]), (twig_node, tpts[-1]))):
                hang = fl.empty(f"Bunch{i}_{b}", holder, tuple(tip))
                bells = fl.from_bm(f"Bells{i}_{b}", bells_bm(5, 3 * i + b), [m["flower"]], hang)
                fl.solidify(bells, 0.0004)
                looks = []
                for ripe in (False, True):
                    ob = fl.from_bm(f"Bunch{i}_{b}_{'ripe' if ripe else 'green'}", cluster_bm(6 - b, 11 * i + b, ripe), [m["skin"]], hang)
                    looks.append(ob)
                    self.looks.append(ob)
                bunches.append((hang, bells))
                self.grow_obs += [hang, bells]
            self.canes.append((node, stick, leaves, twig_node, bunches, born, h))
            self.grow_obs += [node, twig_node] + [lf for lf, _ in leaves]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        height = fl.smoothstep(-0.2, 3.2, g)
        for node, stick, leaves, twig_node, bunches, born, h in self.canes:
            age = (g - born) / 1.4
            shown = fl.smoothstep(0.0, 0.12, age)
            s = min(1.0, max(0.0, age))
            node.scale = (max(1e-4, shown * (0.25 + 0.75 * s) * (0.5 + 0.5 * height)),) * 3
            for leaf, q in leaves:
                leaf.scale = (max(1e-4, fl.smoothstep(q * 0.6, q * 0.6 + 0.5, age)),) * 3
            twig_node.scale = (max(1e-4, fl.smoothstep(0.45, 0.9, age)),) * 3
            for hang, bells in bunches:
                bells.scale = (max(1e-4, fl.smoothstep(2.0, 2.3, g) * (1 - fl.smoothstep(2.7, 3.0, g))),) * 3
                fruit = fl.smoothstep(FRUIT_BORN, RIPE_AT, g)
                hang.scale = (max(1e-4, 1.0 if g < FRUIT_BORN else 0.4 + 0.6 * fruit),) * 3
        ripe = g >= RIPE_AT
        for ob in self.looks:
            if ob.name.endswith("_ripe"):
                ob.scale = (1.0 if ripe else 1e-4,) * 3
            else:
                # the green bunch only shows once the flowers have set
                ob.scale = (1e-4 if ripe or g < FRUIT_BORN else 1.0,) * 3


def plant_mats():
    m = mats()
    m.update({
        "cane": fl.material("blueberry_cane", "#7A5A44", rough=0.75),
        "leaf": fl.material("blueberry_leaf", "#3E7E36", rough=0.4, coat=0.08, sss=0.12),
        "flower": fl.material("blueberry_flower", "#F2EEE4", rough=0.5, sss=0.25),
    })
    return m


def build_rig(label):
    return BlueberryRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
