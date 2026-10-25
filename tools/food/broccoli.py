"""The broccoli (crops map, batch K4): the harvested head (whole and bitten), and the broccoli plant as a game rig.

The head is a dense dome of knobbly dark green florets on a short, pale, branching stalk cut square at its foot, lying on
its side (the stalk toward -X, the dome toward +X). Bites take florets off the dome's end and show the pale green
inside branching into them. The plant is a knee-high rosette (crops map: rosettes are knee high): broad blue-green leaves
with pale ribs on long stalks round a thick stem, the head swelling in their middle. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

HEAD_R = 0.05        # the dome's radius (10 cm across): a big, readable head
STALK_L = 0.06       # the stalk below the dome
LABEL_COLOR = "#3E8A3A"     # the broccoli's crop colour (seed label, stake tag): broccoli green
FLORET, FLORET_DARK, FLORET_LIGHT = "#3E7A2E", "#24521E", "#6A9A3E"
STALK, STALK_CUT, INSIDE = "#9DBA6A", "#D4E0A8", "#C6D88E"


def mats():
    return {
        "head": fl.material("broccoli_head", "#FFFFFF", rough=0.72, sss=0.06, vertex_color=True),
        "stalk": fl.material("broccoli_stalk", STALK, rough=0.5, sss=0.1),
        "cut": fl.material("broccoli_cut", STALK_CUT, rough=0.7, sss=0.05),
        "flesh": fl.material("broccoli_flesh", "#FFFFFF", rough=0.55, sss=0.08, vertex_color=True),
    }


def florets(seed, n=34):
    """The florets' directions and sizes over the dome: (unit direction from the dome's centre, bump size)."""
    rnd = random.Random(seed)
    out = []
    for k in range(n):
        z = 1 - 1.15 * (k + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = k * 2.39996 + rnd.uniform(-0.2, 0.2)
        out.append((Vector((r * math.cos(a), r * math.sin(a), z)).normalized(), rnd.uniform(0.22, 0.32)))
    return out


def head_bm(rad=HEAD_R, seed=4, segs=(56, 36), young=0.0):
    """The dome of florets, its centre at the origin and its top up +Z: a sphere flattened underneath, each floret a
    rounded knob and each knob grainy with buds; vertex colours dark in the crevices, lighter on the knobs. The closed
    underside is where the stalk joins."""
    nu, nv = segs
    fls = florets(seed)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=nu, v_segments=nv, radius=1)
    info = {}
    for v in bm.verts:
        n = v.co.normalized()
        bump, crease = 0.0, 1.0
        for d, size in fls:
            ang = math.acos(max(-1.0, min(1.0, n.dot(d))))
            t = ang / size
            if t < 1.0:
                bump = max(bump, math.sqrt(max(0.0, 1 - t * t)))
            crease = min(crease, t)
        grain = 0.5 + 0.5 * fl.noise3(n, 30.0, seed)
        r = rad * (0.82 + 0.18 * bump + 0.02 * grain * (1 - young))
        z = n.z * r
        if n.z < 0:
            z *= 0.55                                   # the underside: flatter, where the florets' stems meet
        v.co = Vector((n.x * r, n.y * r, z))
        info[v] = (bump, grain, n.z)
    bm.normal_update()
    base, dark, light = (fl.srgb(c) for c in (FLORET, FLORET_DARK, FLORET_LIGHT))
    under = fl.srgb(STALK)
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            bump, grain, nz = info[lp.vert]
            c = fl.mix(dark, base, fl.smoothstep(0.1, 0.7, bump))
            c = fl.mix(c, light, 0.35 * grain ** 3 * bump)
            c = fl.mix(c, light, 0.25 * young)
            c = fl.mix(c, under, fl.smoothstep(-0.05, -0.45, nz))
            lp[col] = (*c, 1.0)
    return bm


def stalk_bm(length=STALK_L, rad=0.017):
    """The stalk: thick, a little flared where it meets the dome, cut flat at its foot (-Z)."""
    pts = [(0, 0, -length), (0, 0, -length * 0.6), (0, 0, -length * 0.25), (0, 0, -0.004)]
    return fl.tube(pts, [rad * 0.85, rad * 0.9, rad * 1.1, rad * 1.6], segs=14, cap_mat=1)


def build(name="Broccoli", bites=0, parent=None, location=(0, 0, 0), m=None):
    """A harvested head of broccoli: Body (the dome) and Stalk under an empty named ``name``, lying on its side (stalk
    -X, dome +X) on z = 0; ``bites`` (0..2) taken off the dome's end."""
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    tilt = fl.empty(name + "_Lie", root)
    body = fl.from_bm(name + "_Body", head_bm(), [m["head"]], tilt)
    stalk = fl.from_bm(name + "_Stalk", stalk_bm(), [m["stalk"], m["cut"]], tilt)
    # side shoots: two little florets on short stems off the stalk
    for k, (a, z) in enumerate(((0.8, -0.032), (3.6, -0.045))):
        d = Vector((math.cos(a), math.sin(a), 0.5)).normalized()
        p = Vector((0, 0, z)) + d * 0.012
        fl.from_bm(f"{name}_Shoot{k}", fl.tube([p, p + d * 0.016], [0.004, 0.0035], segs=8), [m["stalk"]], stalk)
        bud = fl.from_bm(f"{name}_Bud{k}", head_bm(0.011, seed=9 + k, segs=(18, 12)), [m["head"]], stalk)
        bud.location = p + d * 0.021
    for k in range(bites):
        bite(body, k, m)
    tilt.rotation_euler = (0, math.pi / 2, 0)          # the dome's top toward +X
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in fl.tree(tilt) if o.type == "MESH" for v in o.data.vertices]
    tilt.location = (0, 0, -min(p.z for p in pts))
    return root


BITES = ((0.0, 0.45), (0.0, 0.05))      # each bite's turn about the axis and how far from the top it is centred


def bite(body, k, m):
    """Florets bitten off the dome's top: pale green flesh, the florets' stems branching into the dark buds."""
    turn, drop = BITES[k]
    out = Vector((math.cos(turn) * 0.35, math.sin(turn) * 0.35, 1)).normalized()
    c0 = out * HEAD_R * (1.05 if k == 0 else 0.78) - Vector((0, 0, drop * 0.01))
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=20, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(c0) @ rot @ Matrix.Diagonal((0.042, 0.036, 0.03, 1)))
    inside, dark, base = (fl.lin(c)[:3] for c in (INSIDE, FLORET_DARK, FLORET))
    fls = florets(4)

    def color_of(co):
        rr = co.length / HEAD_R
        n = co.normalized()
        # pale stems running out to each floret, dark buds at the surface
        near = max(n.dot(d) for d, _ in fls)
        c = fl.mix(inside, fl.mix(inside, base, 0.4), fl.smoothstep(0.985, 0.94, near) * fl.smoothstep(0.4, 0.75, rr))
        return fl.mix(c, dark, fl.smoothstep(0.72, 0.86, rr))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

STEM_H = 0.14
LEAVES = [(0.3, 0.25, 0.26, 0.55), (2.4, 0.5, 0.27, 0.6), (4.5, 0.8, 0.28, 0.65), (1.35, 1.1, 0.28, 0.7),
          (3.45, 1.4, 0.27, 0.75), (5.55, 1.7, 0.26, 0.8), (0.85, 2.0, 0.24, 0.9), (2.95, 2.3, 0.22, 1.0),
          (5.0, 2.55, 0.2, 1.1)]           # (yaw, born, length, lean: share of the way from upright to arching out)
HEAD_BORN = 1.9
PLANT_HEAD_R = 0.066


def leaf_bm(length, width, seed):
    """A broccoli leaf along +X from its base: a long pale stalk rising and arching, running on as a thick pale midrib
    through a broad, wavy-edged blue-green blade, lobed near its foot. Returns (stalk and rib, blade)."""
    rnd = random.Random(seed)
    up = Vector((0, 0, 1))
    N = 18
    pts = fl.bezier((0, 0, 0), (length * 0.1, 0, length * 0.5), (length * 0.55, 0, length * 0.62), (length, 0, length * 0.3), n=N)
    k0 = 6
    rib = fl.tube(pts, [(0.0045 * (1 - 0.25 * i / k0) if i <= k0 else 0.0033 * (1 - (i - k0) / (N - k0)) ** 0.8) + 0.0006
                        for i in range(N + 1)], segs=7)
    bpts, ws, ns = [], [], []
    for i in range(k0 - 2, N + 1):
        s = (i - k0 + 2) / (N - k0 + 2)
        p = pts[i]
        tan = (pts[min(i + 1, N)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(up).normalized()
        n = side.cross(tan).normalized()
        bpts.append(p - n * 0.001)
        lobe = 0.55 + 0.45 * abs(math.sin(s * 9 + seed)) if s < 0.35 else 1.0
        w = width * math.sin(math.pi * min(1.0, 0.05 + s * 0.95)) ** 0.6 * lobe
        ws.append(w + 0.0005)
        ns.append(n)
    blade = fl.strip(bpts, ws, ns, fold=0.15)
    # a wavy edge: the margin ripples up and down
    for v in blade.verts:
        near = min(range(len(bpts)), key=lambda q: (v.co - bpts[q]).length)
        d = (v.co - bpts[near]).length / max(1e-6, ws[near])
        v.co += ns[near] * 0.006 * d * d * math.sin(near * 1.9 + rnd.uniform(0, 0.4) + seed)
    blade.normal_update()
    return rib, blade


class BroccoliRig(crop_rig.CropRig):
    """A broccoli plant in one mound. Grow: two seed leaves, then a thick stem rising with broad blue-green leaves on
    long stalks one after another; the head shows in their middle as a tight bud and swells into a full dark dome once
    ripe. Wilt: the leaves flop over."""

    def __init__(self, m, label=None, stake=True, seed=23):
        super().__init__("BroccoliPlant")
        self.m = m
        self.stem = fl.from_bm("Stem", fl.tube([(0, 0, 0), (0.002, 0.001, STEM_H * 0.5), (0, 0, STEM_H)], [0.012, 0.011, 0.012], segs=10),
                               [m["stalk"]], self.sway)
        self.top = fl.empty("Top", self.sway, (0, 0, STEM_H))
        self.grow_obs += [self.stem, self.top]
        self.leaves = []
        for i, (yaw, born, length, lean) in enumerate(LEAVES):
            z = -STEM_H * (0.85 - 0.75 * i / len(LEAVES))
            w = self.wilt_node(f"Wilt{i}", self.top, (0.008 * math.cos(yaw), 0.008 * math.sin(yaw), z), yaw, droop=1.2 + 0.3 * (i % 2))
            tilt = fl.empty(f"Tilt{i}", w)
            tilt.rotation_euler = (0, math.radians(40) * lean - math.radians(18), 0)
            rib, blade = leaf_bm(length, 0.07 + 0.006 * (i % 3), seed + i)
            leaf = fl.from_bm(f"Leaf{i}", rib, [m["rib"]], tilt)
            bl = fl.from_bm(f"Leaf{i}_Blade", blade, [m["leaf"]], leaf)
            fl.solidify(bl, 0.0009)
            self.leaves.append((leaf, born))
            self.grow_obs.append(leaf)
        cot = [fl.strip([Vector((0.5 * sg, 0, 1)).normalized() * (0.04 * t / 6) for t in range(7)],
                        [0.009 * math.sin(math.pi * min(1, t / 6 * 0.9 + 0.1)) + 0.0004 for t in range(7)],
                        [(0, 1, 0)] * 7) for sg in (1, -1)]
        self.cots = fl.from_bm("Cots", fl.join(cot), [m["cot"]], self.sway)
        fl.solidify(self.cots, 0.0007)
        self.grow_obs.append(self.cots)
        self.head = fl.empty("Head", self.top, (0, 0, 0))
        hb = head_bm(PLANT_HEAD_R, seed=6, segs=(40, 26))
        bmesh.ops.transform(hb, matrix=Matrix.Translation((0, 0, PLANT_HEAD_R * 0.5)), verts=hb.verts)
        self.dome = fl.from_bm("Head_Dome", hb, [m["head"]], self.head)
        self.grow_obs += [self.head]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        h = fl.smoothstep(-0.2, 3.0, g)
        self.stem.scale = (max(1e-4, 0.3 + 0.7 * h), max(1e-4, 0.3 + 0.7 * h), max(1e-4, 0.08 + 0.92 * h))
        self.top.location = (0, 0, STEM_H * (0.08 + 0.92 * h))
        for leaf, born in self.leaves:
            age = (g - born) / 1.2
            shown = fl.smoothstep(0.0, 0.12, age)
            s = min(1.0, max(0.0, age))
            leaf.scale = (max(1e-4, shown * (0.25 + 0.75 * s)),) * 3
            leaf.rotation_euler = (0, -math.radians(40) * (1 - fl.smoothstep(0.0, 1.0, s)), 0)
        self.cots.scale = (max(1e-4, fl.smoothstep(-0.3, 0.15, g) * (1 - fl.smoothstep(1.4, 2.2, g))),) * 3
        head = fl.smoothstep(HEAD_BORN, 3.9, g)
        self.head.scale = (max(1e-4, fl.smoothstep(HEAD_BORN - 0.1, HEAD_BORN + 0.2, g) * (0.22 + 0.78 * head)),) * 3


def plant_mats():
    m = mats()
    m.update({
        "rib": fl.material("broccoli_rib", "#8EAA72", rough=0.55, sss=0.06),
        "leaf": fl.material("broccoli_leaf", "#2E6442", rough=0.55, sss=0.05),
        "cot": fl.material("broccoli_cot", "#5A9040", rough=0.5, sss=0.12),
    })
    return m


def build_rig(label):
    return BroccoliRig(dict(plant_mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
