"""Cotton: the harvested bundle (a resource, not food), and the cotton plant, as a game rig and as a wild plant.

Cotton is the crop the Carpet Block is woven from (block list batch 2). The plant follows the tomato's crop language:
one rig with ``grow``, ``wilt`` and ``sway`` clips, the scrap-wood stake and a crop tag in the cotton's colour.
Grow: a woody stem branching out, three-lobed leaves, then green bolls that split their brown husks round a puff of
white fluff when ripe. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl
import plant

LABEL_COLOR = "#3E9C8F"     # the cotton's crop colour (seed label, stake tag): our teal, darkened
FLUFF = "#F4F0E6"


def mats():
    return {
        "fluff": fl.material("cotton_fluff", FLUFF, rough=0.95, sss=0.25),
        "husk": fl.material("cotton_husk", "#7A5634", rough=0.85),
        "husk_dry": fl.material("cotton_husk_dry", "#5E4128", rough=0.9),
        "boll": fl.material("cotton_boll", "#7FA046", rough=0.55, sss=0.1),
        "stem": fl.material("cotton_stem", "#7D5A3C", rough=0.8),
        "stem_young": fl.material("cotton_stem_young", "#6F9645", rough=0.6),
        "leaf": fl.material("cotton_leaf", "#4F8A36", rough=0.55, sss=0.15),
        "band": fl.material("cotton_band", LABEL_COLOR, rough=0.8),
        "band_edge": fl.material("cotton_band_edge", "#2C6F66", rough=0.8),
    }


def fluff_bm(r, seed, lobes=4):
    """A puff of cotton: a few lumpy balls run together, soft bumps all over."""
    rnd = random.Random(seed)
    out = []
    for k in range(lobes):
        a = 2 * math.pi * k / lobes + rnd.uniform(-0.3, 0.3)
        c = Vector((math.cos(a) * r * 0.45, math.sin(a) * r * 0.45, rnd.uniform(-0.1, 0.25) * r))
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=3, radius=r * rnd.uniform(0.62, 0.78))
        for v in bm.verts:
            n = v.co.normalized()
            bump = 1 + 0.12 * fl.noise3(n, 6.0, seed + k) + 0.05 * fl.noise3(n, 15.0, seed + 7 * k)
            v.co = c + v.co * bump
        out.append(bm)
    return fl.join(out)


def husk_bm(r, open_, seed):
    """The boll's husk: five pointed bracts, closed round the boll or curled open and back under the fluff."""
    rnd = random.Random(seed)
    bms = []
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        tilt = math.radians(10 + 95 * open_)                  # from hugging the boll to bent down and out
        pts, ws, ns = [], [], []
        N = 8
        for i in range(N + 1):
            s = i / N
            ang = tilt * s
            p = d * (r * 0.25 + r * 1.0 * math.sin(ang) * s) + Vector((0, 0, -r * 0.7 + r * 1.4 * s * math.cos(ang)))
            pts.append(p)
            ws.append(r * 0.42 * math.sin(math.pi * min(1, s * 0.9 + 0.1)) ** 0.8 + 0.0004)
            ns.append(d)
        bms.append(fl.strip(pts, ws, ns, fold=-0.25))
    return fl.join(bms)


def leaf_bm(base, out, length, unfurl, seed):
    """A cotton leaf: a stalk and a broad three-lobed blade (each lobe a leaflet strip), cupped a little."""
    rnd = random.Random(seed)
    out = out.normalized()
    up = Vector((0, 0, 1))
    tip = base + out * length * 0.35 + up * length * (0.12 - 0.1 * unfurl)
    bms = [fl.tube([base, (base + tip) / 2 + up * 0.004, tip], [0.0016, 0.0013, 0.001], segs=5)]
    side = out.cross(up).normalized()
    for k, spread in enumerate((-0.75, 0.0, 0.75)):
        sp = spread * (0.4 + 0.6 * unfurl)
        d = (out * math.cos(sp) + side * math.sin(sp)).normalized()
        d = (d + up * (0.25 - 0.35 * unfurl)).normalized()
        n = (up - d * d.dot(up)).normalized()
        L = length * (0.62 if k == 1 else 0.5) * rnd.uniform(0.92, 1.08)
        bms.append(plant.leaflet(tip, d, n, L, L * 0.42, curl=0.12 + 0.18 * unfurl, fold=0.2))
    return fl.join(bms)


# ---------------------------------------------------------------------------------------------------------------------
# The harvested cotton

def build(name="Cotton", parent=None, location=(0, 0, 0), m=None):
    """A bundle of cotton: three puffs pressed together, tied round the middle with a teal cloth band and a knot.
    Lying on the ground, origin at the middle of its base."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    R = 0.05
    for k, (x, y, z, s) in enumerate(((-0.035, 0.0, R * 0.95, 1.0), (0.035, 0.008, R * 0.9, 0.95),
                                      (0.0, -0.01, R * 1.55, 0.85))):
        puff = fl.from_bm(f"{name}_Puff{k}", fluff_bm(R * s, seed=31 + k, lobes=5), [m["fluff"]], root)
        puff.location = (x, y, z)
    # the band: a flat loop round the bundle's waist (along Y), slightly pinched in
    pts, ws, ns = [], [], []
    N = 32
    for i in range(N + 1):
        a = 2 * math.pi * i / N
        rx, rz = 0.052, 0.058
        p = Vector((0.0, math.cos(a) * rx * 1.15, R * 1.15 + math.sin(a) * rz))
        pts.append(p)
        ws.append(0.011)
        ns.append(Vector((0, math.cos(a), math.sin(a))))
    band = fl.from_bm(name + "_Band", fl.strip(pts, ws, ns), [m["band"]], root)
    fl.solidify(band, 0.003)
    # the knot and its two short tails on top
    knot = bmesh.new()
    bmesh.ops.create_icosphere(knot, subdivisions=2, radius=0.011)
    for v in knot.verts:
        v.co = Vector((v.co.x * 1.3, v.co.y, v.co.z * 0.8)) + Vector((0, -0.006, R * 1.15 + 0.06))
    fl.from_bm(name + "_Knot", knot, [m["band_edge"]], root)
    for s in (1, -1):
        tail = fl.strip([Vector((0, -0.006, R * 1.15 + 0.062)), Vector((s * 0.02, -0.03, R * 1.15 + 0.05)),
                         Vector((s * 0.03, -0.05, R * 1.15 + 0.028))], [0.007, 0.0075, 0.006],
                        [Vector((0, -0.3, 1))] * 3)
        t = fl.from_bm(f"{name}_Tail{'L' if s > 0 else 'R'}", tail, [m["band"]], root)
        fl.solidify(t, 0.0025)
    return root


# ---------------------------------------------------------------------------------------------------------------------
# The plant

STEM_H = 0.36                # the main stem when ripe (the rig is scaled by plant.SCALE, about knee high)
BRANCHES = [(0.09, 0.3, 0.75), (0.12, 2.6, 0.85), (0.15, 4.6, 1.0), (0.18, 1.5, 1.1), (0.21, 3.6, 1.2),
            (0.24, 5.7, 1.35), (0.28, 0.9, 1.5), (0.32, 3.0, 1.7)]
LEAVES = 9


class CottonRig(crop_rig.CropRig):
    """A cotton bush. Grow: a sprout, a woody stem rising, side branches with broad leaves, green bolls at the branch
    tips, which split open on a ripe plant into puffs of cotton. Wilt: the leaves droop."""

    def __init__(self, m, label=None, stake=True):
        super().__init__("CottonPlant")
        self.m = m
        rnd = random.Random(9)
        self.stem = fl.from_bm("Stem", fl.tube([(0, 0, 0), (0.004, 0.002, STEM_H * 0.5), (0.0, 0.004, STEM_H)],
                                                [0.009, 0.0065, 0.0035], segs=8), [m["stem"]], self.sway)
        self.grow_obs.append(self.stem)
        self.branches = []
        for i, (z, yaw, born) in enumerate(BRANCHES):
            node = fl.empty(f"Branch{i}", self.sway, (0, 0, z))
            node.rotation_euler = (0, 0, yaw)
            L = 0.14 - 0.009 * i
            br = fl.from_bm(f"Branch{i}_Stick", fl.tube([(0, 0, 0), (L * 0.5, 0, L * 0.25), (L, 0, L * 0.6)],
                                                          [0.0045, 0.0034, 0.0024], segs=6), [m["stem"]], node)
            tip = fl.empty(f"Branch{i}_Tip", node, (L, 0, L * 0.6))
            boll = fl.empty(f"Boll{i}", tip, (0, 0, 0.016))
            r = 0.024
            green = bmesh.new()
            bmesh.ops.create_uvsphere(green, u_segments=14, v_segments=10, radius=r)
            for v in green.verts:
                v.co.z *= 1.25
                v.co.z += 0.25 * r * max(0.0, v.co.z / r) ** 2      # a pointed top
            closed = fl.from_bm(f"Boll{i}_Green", fl.join([green, husk_bm(r, 0.0, 40 + i)]), [m["boll"]], boll)
            opened = fl.from_bm(f"Boll{i}_Open", husk_bm(r, 1.0, 40 + i), [m["husk"]], boll)
            fluff = fl.from_bm(f"Boll{i}_Fluff", fluff_bm(r * 1.5, 50 + i, lobes=4), [m["fluff"]], boll)
            fluff.location = (0, 0, r * 0.9)
            leaves = []
            for k in range(2):
                w = self.wilt_node(f"Wilt{i}_{k}", node, (L * (0.35 + 0.3 * k), 0, L * (0.15 + 0.22 * k)),
                                   (1 if k else -1) * 1.2 + rnd.uniform(-0.3, 0.3), droop=1.0 + 0.25 * k)
                leaf = fl.from_bm(f"Leaf{i}_{k}", leaf_bm(Vector((0, 0, 0)), Vector((1, 0, 0.15)), 0.11, 1.0, 60 + 2 * i + k),
                                  [m["stem_young"], m["leaf"]], w)
                self._leaf_mats(leaf)
                fl.solidify(leaf, 0.0007)
                leaves.append((leaf, w))
            self.branches.append((node, br, closed, opened, fluff, leaves, born, z))
            self.grow_obs += [node, br, closed, fluff] + [lf for lf, _ in leaves]
            self.looks += [opened]
        # leaves on the main stem
        self.stem_leaves = []
        for k in range(4):
            z = 0.04 + 0.04 * k
            w = self.wilt_node(f"WiltStem{k}", self.sway, (0, 0, z), 2 * math.pi * k / 4 + 0.4, droop=0.8)
            leaf = fl.from_bm(f"StemLeaf{k}", leaf_bm(Vector((0, 0, 0)), Vector((1, 0, 0.2)), 0.1, 1.0, 80 + k),
                              [m["stem_young"], m["leaf"]], w)
            self._leaf_mats(leaf)
            fl.solidify(leaf, 0.0007)
            self.stem_leaves.append((leaf, w, 0.15 + 0.3 * k))
            self.grow_obs.append(leaf)
        if stake:
            self.stake(m, label)

    @staticmethod
    def _leaf_mats(leaf):
        # the stalk tube is the first piece joined: its 12 faces keep material 0; the blades take the leaf green
        for i, poly in enumerate(leaf.data.polygons):
            poly.material_index = 0 if i < 12 else 1

    def grow(self, g):
        height = fl.smoothstep(0.0, 3.4, g)
        self.stem.scale = (max(1e-4, 0.15 + 0.85 * height),) * 3
        for node, br, closed, opened, fluff, leaves, born, z in self.branches:
            age = (g - born) / 1.2
            shown = fl.smoothstep(0.0, 0.15, age)
            s = min(1.0, max(0.0, age))
            node.location = (0, 0, z * (0.15 + 0.85 * height))
            node.scale = (max(1e-4, shown * (0.3 + 0.7 * s)),) * 3
            br.scale = (1, 1, 1)
            # bolls: appear green at 2.2, swell, and split at 3.5 (the plant is ripe at 4)
            boll = fl.smoothstep(2.0, 3.0, g)
            ripe = g >= 3.5
            closed.scale = (max(1e-4, boll * (0 if ripe else 1)),) * 3
            opened.scale = (1.0 if ripe else 1e-4,) * 3
            fluff.scale = (max(1e-4, fl.smoothstep(3.45, 3.95, g)),) * 3
            for leaf, w in leaves:
                leaf.scale = (max(1e-4, fl.smoothstep(0.1, 0.8, age)),) * 3
                w.location = w.location
        for leaf, w, born in self.stem_leaves:
            w.location = (0, 0, (0.04 + 0.04 * self.stem_leaves.index((leaf, w, born))) * (0.15 + 0.85 * height))
            leaf.scale = (max(1e-4, fl.smoothstep(born, born + 0.8, g)),) * 3


def build_rig(label):
    return CottonRig(dict(mats(), **_stake_mats()), label)


def build_wild(name="WildCotton"):
    """The cotton bush growing wild in the Autumn Forest: the ripe plant without its stake, as a still model,
    a few stems together so it reads as a clump."""
    root = fl.empty(name)
    for k, (x, y, yaw, s) in enumerate(((0, 0, 0, 1.0), (0.13, 0.09, 1.9, 0.85), (-0.11, 0.1, 3.7, 0.9),
                                             (0.05, -0.13, 5.1, 0.8), (-0.14, -0.08, 2.6, 0.75))):
        rig = CottonRig(mats(), stake=False)
        crop_rig.pose(rig, 4.0)
        rig.root.parent = root
        rig.root.location = (x * plant.SCALE, y * plant.SCALE, 0)
        rig.root.rotation_euler = (0, 0, yaw)
        rig.root.scale = (plant.SCALE * s,) * 3
    return root


def _stake_mats():
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
