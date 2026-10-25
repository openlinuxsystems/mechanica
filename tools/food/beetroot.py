"""The beetroot (crops map, batch K3; SM's Redbeet under our name): the harvested root (whole and bitten), and the
beetroot plant as a game rig.

The root is built standing (the crown's dome at the origin, the taproot down -Z), so the same mesh stands in the soil
on the plant, and laid on its side for the item (crown toward -Y, the tail toward +Y, its bitten side +X). A bite shows
the cut beet's rings, deep crimson and pale magenta. The plant follows the carrot: three beets in one mound, a rosette of
glossy leaves on red stalks each, the round shoulders pushing up out of the soil as they ripen. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import crop_rig
import food_lib as fl

R = 0.039            # the bulb's radius: a big, readable beet (7.8 cm across)
TAIL = 0.075         # the taproot below the bulb
LABEL_COLOR = "#8E2250"     # the beetroot's crop colour (seed label, stake tag): beet crimson
SKIN = ("#8A3A48", "#861E4A", "#9C2E58", "#A2645A")       # crown, body, the lower bulb, the tail
RING_DARK, RING_LIGHT, CUT_EDGE = "#8E1240", "#D2508A", "#5A0E2A"   # the cut face: crimson and pale magenta rings
STALK = "#B42A4E"


def mats():
    return {
        "skin": fl.material("beet_skin", "#FFFFFF", rough=0.62, sss=0.02, vertex_color=True),
        "flesh": fl.material("beet_flesh", "#FFFFFF", rough=0.3, sss=0.04, vertex_color=True),
        "stalk": fl.material("beet_stalk", STALK, rough=0.45, sss=0.12),
        "leaf": fl.material("beet_leaf", "#FFFFFF", rough=0.42, coat=0.05, sss=0.12, vertex_color=True),
        "hair": fl.material("beet_hair", "#9A6A58", rough=0.85),
    }


def radius(t):
    """The root's radius at depth t (m, 0 at the crown): a round bulb run smoothly into a thin, tapering taproot."""
    u = (t - R * 0.95) / (R * 1.02)
    bulb = R * math.sqrt(max(0.0, 1 - u * u)) * (1 - 0.08 * math.exp(-((t - 0.002) / 0.008) ** 2))
    s = min(1.0, max(0.0, (t - R * 1.2) / TAIL))
    tail = 0.012 * (1 - s) ** 1.4 * fl.smoothstep(R * 0.4, R * 1.3, t) + 0.0006
    p = 4.0
    return (bulb ** p + tail ** p) ** (1 / p)


def length():
    return R * 1.2 + TAIL


def root_bm(seed=4, segs=(32, 40)):
    """The root, standing: the crown at z = 0 (a low dome with a ring of leaf scars), the tail's tip at -length()."""
    rnd = random.Random(seed)
    nu, nv = segs
    Lt = length()
    bm = bmesh.new()
    rows = []
    info = {}
    rings = [(rnd.uniform(0.25, 0.9), rnd.uniform(0, 2 * math.pi), rnd.uniform(1.0, 2.4)) for _ in range(6)]
    for j in range(nv + 1):
        s = (j / nv) ** 1.25                         # more rows in the bulb than in the tail
        t = Lt * s
        r = radius(t)
        # the tail wanders and curls a little to one side
        tail = fl.smoothstep(R * 1.4, Lt, t)
        bend = Vector((0.012 * tail * tail * math.sin(1.3 + seed), 0.008 * tail * math.sin(2.0 + seed), 0))
        row = []
        for i in range(nu):
            a = 2 * math.pi * i / nu
            dent = 0.0
            for rs, ra, span in rings:
                da = math.atan2(math.sin(a - ra), math.cos(a - ra))
                dent = max(dent, math.exp(-((t / (2 * R) - rs) / 0.03) ** 2) * max(0.0, 1 - abs(da) / span))
            lump = 1 + 0.025 * math.sin(3 * a + seed) * (1 - tail) + 0.012 * math.sin(5 * a + 2 * seed)
            rr = r * lump * (1 - 0.035 * dent)
            v = bm.verts.new(bend + Vector((rr * math.cos(a), rr * math.sin(a), -t)))
            row.append(v)
            info[v] = (t, dent)
        rows.append(row)
    for a_, b_ in zip(rows, rows[1:]):
        for i in range(nu):
            bm.faces.new((a_[i], a_[(i + 1) % nu], b_[(i + 1) % nu], b_[i]))
    top = bm.verts.new((0, 0, 0.003))
    info[top] = (0.0, 0.0)
    for i in range(nu):
        bm.faces.new((top, rows[0][(i + 1) % nu], rows[0][i]))
    tip = bm.verts.new(sum((v.co for v in rows[-1]), Vector()) / nu - Vector((0, 0, 0.002)))
    info[tip] = (Lt, 0.0)
    for i in range(nu):
        bm.faces.new((rows[-1][i], rows[-1][(i + 1) % nu], tip))
    bm.normal_update()
    crown, body, low, tailc = (fl.srgb(c) for c in SKIN)
    ring = fl.srgb("#4A0E22")
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            t, dent = info[lp.vert]
            c = fl.mix(crown, body, fl.smoothstep(0.0, R * 0.45, t))
            c = fl.mix(c, low, fl.smoothstep(R * 1.0, R * 1.7, t))
            c = fl.mix(c, tailc, fl.smoothstep(R * 1.7, Lt * 0.8, t))
            c = fl.mix(c, ring, 0.6 * dent)
            k = 0.5 + 0.5 * fl.noise3(lp.vert.co, 160.0, seed)
            c = fl.mix(c, crown, 0.25 * fl.smoothstep(0.6, 0.95, k) * (1 - fl.smoothstep(R, R * 1.6, t)))
            lp[col] = (*c, 1.0)
    return bm


def hairs(seed=4):
    """A few fine root hairs off the taproot."""
    rnd = random.Random(seed)
    bms = []
    for _ in range(6):
        t = rnd.uniform(R * 1.5, length() * 0.85)
        a = rnd.uniform(0, 2 * math.pi)
        r = radius(t)
        p = Vector((0.7 * r * math.cos(a), 0.7 * r * math.sin(a), -t))
        out = Vector((math.cos(a), math.sin(a), -0.5)).normalized()
        q = p + out * rnd.uniform(0.01, 0.018)
        bms.append(fl.tube([p - out * 0.001, (p + q) / 2 + Vector((0, 0, -0.002)), q], [0.0006, 0.0005, 0.0003], segs=5))
    return fl.join(bms)


def leaf_bm(length_, seed, width=None, arch=1.0, blade_share=0.62):
    """A beet leaf from its base at the origin along +X, rising: a red stalk running on up the blade as its midrib, and an
    oval, slightly crinkled glossy blade, darker at its edges. Returns (stalk, blade)."""
    rnd = random.Random(seed)
    width = width or length_ * 0.22
    up = Vector((0, 0, 1))
    out = Vector((1, 0, 0))
    p0 = Vector((0, 0, 0))
    pts = fl.bezier(p0, p0 + up * length_ * 0.42, p0 + up * length_ * 0.68 + out * length_ * 0.32 * arch,
                    p0 + up * length_ * (0.62 - 0.22 * arch) + out * length_ * 0.78 * arch, n=16)
    k0 = int(16 * (1 - blade_share))
    # the stalk runs on up the blade as its crimson midrib, tapering to the tip
    radii = [(0.0032 * (1 - 0.35 * i / k0) if i <= k0 else 0.0021 * (1 - (i - k0) / (16 - k0)) ** 0.8) + 0.0006 for i in range(17)]
    lift = [Vector((0, 0, 0))] * (k0 + 1) + [Vector((0, 0, 0.0009))] * (16 - k0)
    stalk = fl.tube([p + d for p, d in zip(pts, lift)], radii, segs=7)
    bpts, ws, ns = [], [], []
    for i in range(k0, 17):
        s = (i - k0) / (16 - k0)
        p = pts[i]
        tan = (pts[min(i + 1, 16)] - pts[max(i - 1, 0)]).normalized()
        side = tan.cross(up).normalized()
        n = side.cross(tan).normalized()
        bpts.append(p + n * 0.0015 * math.sin(s * 9 + seed))
        w = width * math.sin(math.pi * min(1.0, 0.05 + s * 0.95)) ** 0.7 * (1 + 0.06 * math.sin(s * 23 + rnd.uniform(0, 3)))
        ws.append(w + 0.0004)
        ns.append(n)
    blade = fl.strip(bpts, ws, ns, fold=0.2)
    # crinkle the blade: the edges ripple between the veins
    for v in blade.verts:
        d = min((v.co - q).length for q in bpts)
        v.co.z += 0.0009 * math.sin(v.co.x * 260 + v.co.y * 180 + seed) * min(1.0, d / max(1e-6, width * 0.6))
    blade.normal_update()
    green, dark = fl.srgb("#3F8E36"), fl.srgb("#2A6230")   # bright enough to read as green under the game's light
    col = blade.loops.layers.color.new("Col")
    for f in blade.faces:
        for lp in f.loops:
            co = lp.vert.co
            near = min(range(len(bpts)), key=lambda q: (co - bpts[q]).length)
            d = (co - bpts[near]).length / max(1e-6, ws[near])
            lp[col] = (*fl.mix(green, dark, 0.45 * fl.smoothstep(0.3, 1.0, d)), 1.0)
    return stalk, blade


def build(name="Beetroot", bites=0, parent=None, location=(0, 0, 0), m=None, standing=False):
    """A harvested beetroot: Body, Hairs, Stalks and Leaves under an empty named ``name``, lying on its side (crown toward
    -Y, the tail toward +Y, resting on z = 0) unless ``standing``; ``bites`` (0..2) bitten out of its +X side."""
    import bpy
    m = m or mats()
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", root_bm(), [m["skin"]], root)
    fl.from_bm(name + "_Hairs", hairs(), [m["hair"]], root)
    stalks, blades = [], []
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.5
        st, bl = leaf_bm(0.095 * (0.85 + 0.12 * (k % 3)), seed=20 + k, arch=0.9, blade_share=0.5)
        turn = Matrix.Translation((0.004 * math.cos(a), 0.004 * math.sin(a), 0.002)) @ Matrix.Rotation(a, 4, "Z") \
            @ Matrix.Rotation(math.radians(-8), 4, "Y")
        for b in (st, bl):
            bmesh.ops.transform(b, matrix=turn, verts=b.verts)
        stalks.append(st)
        blades.append(bl)
    fl.from_bm(name + "_Stalks", fl.join(stalks), [m["stalk"]], root)
    lv = fl.from_bm(name + "_Leaves", fl.join(blades), [m["leaf"]], root)
    fl.solidify(lv, 0.0007)
    for k in range(bites):
        bite(body, k, m)
    if not standing:
        root.rotation_euler = (math.pi / 2, 0, 0)    # the crown toward -Y, the tail toward +Y
        bpy.context.view_layer.update()
        lo = min((o.matrix_world @ Vector(c)).z for o in fl.tree(root) if o.type == "MESH" and o.name.endswith("_Body")
                 for c in o.bound_box)
        root.location = Vector(location) + Vector((0, 0, -lo))
    return root


BITES = ((0.0, 0.0), (0.3, -0.45))      # each bite's turn about the axis (rad) and tilt, into the bulb's +X side


def _bite_axis(k):
    turn, tilt = BITES[k]
    out = Vector((math.cos(turn), math.sin(turn), math.sin(tilt) * 0.6)).normalized()
    centre = Vector((0, 0, -R * 0.95))
    return centre + out * (R * (1.02 if k == 0 else 0.92)), out


def bite(body, k, m):
    """A mouth-sized hollow into the bulb's side: its cut face shows the beet's rings, crimson and pale magenta."""
    c0, out = _bite_axis(k)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, verts=bm.verts, matrix=Matrix.Translation(c0) @ rot @ Matrix.Diagonal((0.028, 0.021, 0.026, 1)))
    dark, light, edge = (fl.lin(c)[:3] for c in (RING_DARK, RING_LIGHT, CUT_EDGE))

    def color_of(co):
        # rings round the beet's axis, as in a sliced beet; the skin a thin dark line at the rim
        rr = math.hypot(co.x, co.y)
        dz = co.z + R * 0.95
        rho = math.sqrt(rr * rr + 0.25 * dz * dz) / R
        ring = 0.5 + 0.5 * math.cos(rho * 2 * math.pi * 3.4)
        c = fl.mix(dark, light, 0.75 * ring ** 1.5)
        return fl.mix(c, edge, fl.smoothstep(0.9, 1.0, rho))

    fl.cut(body, bm, m["flesh"], color_of)


# ---------------------------------------------------------------------------------------------------------------------
# The plant

CROWNS = [((-0.025, 0.035), 1.0, 0.0), ((-0.065, -0.02), 0.9, 0.12), ((0.005, -0.045), 0.95, 0.06)]
LEAVES = 6
RISE = (-0.012, 0.028)     # the bulb's crown under the soil when young; ripe, its round shoulder stands out of it


class BeetrootRig(crop_rig.CropRig):
    """Three beets in one mound. Grow: two seed leaves, then a rosette of glossy leaves on red stalks one after another, the
    roots swelling and their crimson shoulders rising out of the soil once ripe. Wilt: the leaves flop over."""

    def __init__(self, m, label):
        super().__init__("BeetrootPlant")
        self.m = m
        self.crowns = []
        rnd = random.Random(7)
        for c, ((x, y), size, delay) in enumerate(CROWNS):
            crown = fl.empty(f"Crown{c}", self.sway, (x, y, 0))
            root = fl.from_bm(f"Root{c}", root_bm(seed=4 + c, segs=(22, 28)), [m["skin"]], crown)
            leaves = []
            for k in range(LEAVES):
                yaw = c * 1.1 + 2 * math.pi * k / LEAVES + rnd.uniform(-0.3, 0.3)
                w = self.wilt_node(f"Wilt{c}_{k}", crown, (0, 0, 0), yaw, droop=1.1 + 0.25 * (k % 2))
                ln = 0.25 * size * rnd.uniform(0.85, 1.05) * (0.8 + 0.2 * (k / LEAVES))
                st, bl = leaf_bm(ln, seed=100 * c + k, arch=rnd.uniform(0.6, 0.95))
                stalk = fl.from_bm(f"Leaf{c}_{k}", st, [m["stalk"]], w)
                blade = fl.from_bm(f"Leaf{c}_{k}_Blade", bl, [m["leaf"]], stalk)
                fl.solidify(blade, 0.0008)
                leaves.append((stalk, 0.3 + 0.42 * k + delay, w))
            cot = [fl.strip([Vector((0, 0, 0)) + Vector((0.45 * sg, sg * 0.1, 1)).normalized() * (0.045 * t / 6) for t in range(7)],
                            [0.0042 * math.sin(math.pi * min(1, t / 6 * 0.9 + 0.1)) + 0.0004 for t in range(7)],
                            [(sg * 0.1, -0.45 * sg, 0)] * 7) for sg in (1, -1)]
            cots = fl.from_bm(f"Cot{c}", fl.join(cot), [m["stalk"]], crown)
            for poly in cots.data.polygons:
                poly.material_index = 0
            cots.data.materials[0] = fl.material("beet_cot", "#4A8A34", rough=0.5, sss=0.12)
            fl.solidify(cots, 0.0007)
            self.crowns.append((crown, root, leaves, cots, size, delay))
            self.grow_obs += [root, cots] + [f for f, _, _ in leaves]
        self.stake(m, label)

    def grow(self, g):
        for crown, root, leaves, cots, size, delay in self.crowns:
            gc = max(0.0, g - delay)
            swell = fl.smoothstep(0.3, 3.9, gc)
            rise = fl.mix((RISE[0],), (RISE[1],), fl.smoothstep(2.2, 3.9, gc))[0]
            root.location = (0, 0, rise * size)
            root.scale = (max(1e-4, (0.12 + 0.88 * swell) * size),) * 3
            for stalk, born, w in leaves:
                age = (gc - born) / 1.3
                shown = fl.smoothstep(0.0, 0.12, age)
                s = min(1.0, max(0.0, age))
                stalk.scale = (max(1e-4, shown * (0.25 + 0.75 * s)),) * 3
                stalk.rotation_euler = (0, -math.radians(38) * (1 - fl.smoothstep(0.1, 1.0, s)), 0)
                w.location = (0, 0, max(0.002, rise * size + 0.002))
            cots.scale = (max(1e-4, fl.smoothstep(-0.3, 0.15, gc) * (1 - fl.smoothstep(1.6, 2.4, gc))),) * 3


def build_rig(label):
    return BeetrootRig(dict(mats(), **_stake_mats()), label)


def _stake_mats():
    import plant
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}
