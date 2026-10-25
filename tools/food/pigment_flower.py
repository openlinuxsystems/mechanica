"""Pigment Flower: our own flower (crops map, batch K2), the cut flower (a craft input: the Fabricator makes Paint Ammo
from it) and the plant, as a game rig and as a wild plant.

Every head is a paint wheel: nine broad petals, one for each of the Paint Tool's colours (game/data/building.json
``paint.palette``, red round to magenta), each petal running through its colour's four shades, darkest at the heart and
lightest at the tip, round a domed disc speckled in the palette's greys. So the flower reads as where the paint comes from.
The plant follows the tomato's crop language: one rig with ``grow``, ``wilt`` and ``sway`` clips, the scrap-wood stake and
a crop tag. Grow: a rosette of long leaves, then stalks rising with green buds that show their colours at the tip, then
the heads open. Z up, metres.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Vector

import crop_rig
import food_lib as fl
import plant

LABEL_COLOR = "#8650D0"     # the flower's crop colour (seed label, stake tag): the palette's violet

# The Paint Tool's base colours (game/data/building.json paint.palette), each light to dark, without the greys.
HUES = [
    ("#f4a0a0", "#e03c3c", "#a82424", "#5c1414"),
    ("#f8c49a", "#ee7a26", "#b45618", "#63300c"),
    ("#faeca0", "#f2d22c", "#b89a14", "#62520a"),
    ("#d6f2a0", "#9ed23a", "#6c9a20", "#3a5410"),
    ("#a8e6b4", "#3eaa58", "#267a3c", "#13421f"),
    ("#a4e6ea", "#26b0bc", "#187c86", "#0c4248"),
    ("#a8c4f4", "#3874dc", "#2350a4", "#122a5a"),
    ("#cab0f0", "#8650d0", "#5c3296", "#301a52"),
    ("#f2b0dc", "#d84aa8", "#9c2c78", "#521640"),
]
GREYS = ("#f2f2f2", "#a8a8a8", "#5c5c5c", "#181818")

PETAL_L, PETAL_W, DISC_R = 0.036, 0.0105, 0.0125      # a head is about 9 cm across


def mats():
    return {
        "petal": fl.material("pigment_petal", "#FFFFFF", rough=0.38, coat=0.1, vertex_color=True),
        "disc": fl.material("pigment_disc", "#FFFFFF", rough=0.85, vertex_color=True),
        "bud": fl.material("pigment_bud", "#FFFFFF", rough=0.45, sss=0.08, vertex_color=True),
        "sepal": fl.material("pigment_sepal", "#4E8A34", rough=0.55, sss=0.1),
        "stem": fl.material("pigment_stem", "#5E9A3C", rough=0.55),
        "leaf": fl.material("pigment_leaf", "#3F8232", rough=0.5, sss=0.15),
        "leaf_young": fl.material("pigment_leaf_young", "#6FA645", rough=0.5, sss=0.15),
    }


def _gradient(shades, t):
    """The colour t of the way along a petal (0 heart, 1 tip): its darkest shades at the heart, the full colour over
    most of the blade and the lightest shade only at the very tip."""
    light, full, dim, dark = (fl.srgb(c) for c in shades)
    if t < 0.12:
        return fl.mix(dark, dim, fl.smoothstep(0.0, 0.12, t))
    if t < 0.35:
        return fl.mix(dim, full, fl.smoothstep(0.12, 0.35, t))
    return fl.mix(full, light, 0.7 * fl.smoothstep(0.78, 1.0, t))


def petal_bm(angle, shades, length=PETAL_L, width=PETAL_W, lift=0.25, curl=0.18, r0=DISC_R * 0.7, seed=0):
    """One petal: a spoon-shaped blade out from the disc at ``angle``, lifted a little and its tip curled down, cupped
    along its midrib, coloured through ``shades``."""
    rnd = random.Random(seed)
    d = Vector((math.cos(angle), math.sin(angle), 0))
    up = Vector((0, 0, 1))
    side = d.cross(up)
    L = length * rnd.uniform(0.94, 1.04)
    pts, ws, ns = [], [], []
    N = 14
    for i in range(N + 1):
        s = i / N
        rise = lift * s - curl * s ** 3
        p = d * (r0 + L * s) + up * (L * rise * 0.6)
        tang = (d + up * (lift - 3 * curl * s * s) * 0.6).normalized()
        n = side.cross(tang).normalized() * -1
        pts.append(p)
        # a narrow claw at the heart (so neighbours do not overlap there), broadest about two thirds out, a round tip
        w = width * (0.1 + 0.9 * math.sin(math.pi * min(1.0, 0.04 + s * 0.82)) ** 0.75)
        if s > 0.88:
            w *= math.sqrt(max(0.0, 1 - ((s - 0.88) / 0.12) ** 2)) * 0.85 + 0.15
        ws.append(w + 0.0003)
        ns.append(n)
    bm = fl.strip(pts, ws, ns, fold=-0.22)
    # colour by how far out along the petal each vertex is
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            co = lp.vert.co
            t = (Vector((co.x, co.y, 0)).length - r0) / L
            c = _gradient(shades, min(1.0, max(0.0, t)))
            # faint veins: a darker line down the middle
            k = abs((co - d * d.dot(co) - up * up.dot(co)).dot(side))
            c = fl.mix(c, fl.srgb(shades[2]), 0.18 * max(0.0, 1 - k / (width * 0.1)) * (1 - t) * fl.smoothstep(0.15, 0.3, t))
            lp[col] = (*c, 1.0)
    return bm


def disc_bm(r=DISC_R, h=0.008, seed=0):
    """The heart: a low dome of fine florets in the palette's greys, charcoal in the middle shading to mid grey at the
    rim, with a few faint pale pollen flecks."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1)
    florets = [Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.2, 1))).normalized() for _ in range(260)]
    fleck = {}
    for v in bm.verts:
        n = v.co.normalized()
        near = max(n.dot(f) for f in florets)
        bump = 1 + 0.03 * fl.smoothstep(0.99, 0.999, near) if n.z > 0 else 1.0
        fleck[v] = fl.smoothstep(0.996, 0.9995, near)
        v.co = Vector((n.x * r, n.y * r, max(-0.25, n.z) * h * bump))
    bm.normal_update()
    dark, mid, light, pale = (fl.srgb(c) for c in (GREYS[3], GREYS[2], GREYS[1], GREYS[0]))
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            co = lp.vert.co
            rr = Vector((co.x, co.y)).length / r
            c = fl.mix(dark, mid, fl.smoothstep(0.2, 0.95, rr))
            c = fl.mix(c, pale if rr > 0.6 else light, 0.12 * fleck[lp.vert])
            lp[col] = (*c, 1.0)
    return bm


def sepals_bm(r=DISC_R, seed=0):
    """The green cup under the head: nine small pointed sepals."""
    rnd = random.Random(seed)
    bms = []
    for k in range(9):
        a = 2 * math.pi * (k + 0.5) / 9 + rnd.uniform(-0.1, 0.1)
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [d * r * 0.3 + Vector((0, 0, -0.006)), d * r * 0.85 + Vector((0, 0, -0.004)), d * r * 1.35 + Vector((0, 0, -0.0005))]
        bms.append(fl.strip(pts, [0.003, 0.0034, 0.0006], [d.cross(Vector((0, 0, 1))).cross(d) * -1] * 3))
    cup = bmesh.new()
    bmesh.ops.create_uvsphere(cup, u_segments=14, v_segments=8, radius=1)
    for v in cup.verts:
        v.co = Vector((v.co.x * r * 0.62, v.co.y * r * 0.62, min(0.0, v.co.z) * 0.009 - 0.0015))
    bms.append(cup)
    return fl.join(bms)


def head(name, parent, m, turn=0.0, seed=0, location=(0, 0, 0)):
    """A head facing +Z, its disc's base at the origin: petals, disc and the sepal cup, each its own mesh."""
    node = fl.empty(name, parent, location)
    bms = [petal_bm(turn + 2 * math.pi * k / 9, HUES[k], seed=seed * 31 + k) for k in range(9)]
    petals = fl.from_bm(name + "_Petals", fl.join(bms), [m["petal"]], node)
    fl.solidify(petals, 0.0012, offset=0)
    disc = fl.from_bm(name + "_Disc", disc_bm(seed=seed), [m["disc"]], node)
    disc.location.z = 0.0012
    sep = fl.from_bm(name + "_Sepals", sepals_bm(seed=seed), [m["sepal"]], node)
    fl.solidify(sep, 0.0008, offset=0)
    return node


def bud_bm(seed=0, r=0.011, h=0.03):
    """A closed bud: a green teardrop whose tip shows the nine colours in stripes, as the petals push out."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=27, v_segments=14, radius=1)
    for v in bm.verts:
        n = v.co.normalized()
        t = (n.z + 1) / 2                                   # 0 bottom .. 1 tip
        rr = r * math.sin(math.pi * min(1.0, t ** 0.75)) ** 0.8
        a = math.atan2(n.y, n.x) + 0.5 * t                  # the petals are furled with a twist
        v.co = Vector((math.cos(a) * rr, math.sin(a) * rr, t * h))
    bm.normal_update()
    green, dark = fl.srgb("#5E9A3C"), fl.srgb("#3E6E2A")
    col = bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            co = lp.vert.co
            t = co.z / h
            a = (math.atan2(co.y, co.x) - 0.5 * t) % (2 * math.pi)
            k = int(a / (2 * math.pi) * 9) % 9
            stripe = fl.srgb(HUES[k][1])
            c = fl.mix(dark, green, fl.smoothstep(0.0, 0.4, t))
            c = fl.mix(c, stripe, fl.smoothstep(0.5, 0.8, t))
            lp[col] = (*c, 1.0)
    return bm


def leaf_bm(length, width, curl, seed, young=0.0):
    """A long rosette leaf along +X: lance-shaped with a wavy edge, arching up then over."""
    rnd = random.Random(seed)
    pts, ws, ns = [], [], []
    N = 14
    arch = 0.35 + 0.25 * young
    for i in range(N + 1):
        s = i / N
        z = length * (arch * s - (curl + arch * 0.5) * s * s)
        p = Vector((length * s, 0.004 * math.sin(s * 5 + seed), z))
        pts.append(p)
        w = width * math.sin(math.pi * min(1.0, 0.06 + s * 0.94)) ** 0.8 * (1 + 0.08 * math.sin(s * 26 + rnd.uniform(0, 3)))
        ws.append(w + 0.0004)
        ns.append(Vector((-(arch - 2 * (curl + arch * 0.5) * s), 0, 1)).normalized())
    return fl.strip(pts, ws, ns, fold=0.3)


# ---------------------------------------------------------------------------------------------------------------------
# The cut flower

STEM_L = 0.28
CUT_HEAD = 1.5         # a cut flower's head is half as big again as a plant's (some 13 cm across), so it reads in hand
HEAD_TILT = math.radians(45)


def build_upright(name="PigmentFlower", parent=None, location=(0, 0, 0), m=None, seed=3, stem=STEM_L, nod=-math.pi / 2):
    """The cut flower standing up, origin at the foot of its stem: a stalk with two leaves and the head on top, nodding
    toward ``nod`` (an angle about +Z from +X; -Y unless given). A short ``stem`` (the icon's) keeps one leaf."""
    m = m or mats()
    root = fl.empty(name, parent, location)
    top = Vector((0, -0.012 * stem / STEM_L, stem))
    pts = fl.bezier((0, 0, 0), (0.004, 0.002, stem * 0.35), (0.0, -0.004, stem * 0.75), top, n=12)
    fl.from_bm(name + "_Stem", fl.tube(pts, [0.004 - 0.0012 * i / 12 for i in range(13)], segs=10), [m["stem"]], root)
    leaves = ((0.1, 0.4, 0.095), (0.14, 3.6, 0.08)) if stem >= STEM_L else ((stem * 0.25, 0.4, 0.055),)
    for k, (z, yaw, L) in enumerate(leaves):
        lf = fl.from_bm(f"{name}_Leaf{k}", leaf_bm(L, 0.012, 0.3, seed + k), [m["leaf"]], root)
        lf.location = (0.002, 0, z)
        lf.rotation_euler = (0, -0.5, yaw)
        fl.solidify(lf, 0.0008)
    h = head(name + "_Head", root, m, turn=0.15, seed=seed, location=top)
    h.rotation_euler = (HEAD_TILT, 0, nod + math.pi / 2)
    h.scale = (CUT_HEAD,) * 3
    return root


def build(name="PigmentFlower", parent=None, location=(0, 0, 0), m=None):
    """The cut flower lying on its side, as it lies on the ground or in a slot, origin at the middle of its base: the
    stalk along -X, resting on the rim of its head and the foot of its stem."""
    import bpy
    root = fl.empty(name, parent, location)
    tip = fl.empty(name + "_Lie", root)
    up = build_upright(name + "_Flower", tip, m=m)
    up.rotation_euler = (0, 0, math.pi / 2)                   # its leaves out to the sides, so they lie flat
    tip.rotation_euler = (0, -math.pi / 2, 0)                 # stem along -X, the head at x = 0 facing +X
    bpy.context.view_layer.update()
    # tilt it about Y until the foot of the stem rests on the floor too, then sit it on z = 0
    lo, hi = _box(fl.tree(tip))
    head_r = (hi.z - lo.z) / 2
    tip.rotation_euler = (0, -math.pi / 2 + math.atan2(head_r * 0.85, STEM_L), 0)
    bpy.context.view_layer.update()
    lo, hi = _box(fl.tree(tip))
    tip.location.z -= lo.z
    tip.location.x -= (lo.x + hi.x) / 2
    tip.location.y -= (lo.y + hi.y) / 2
    return root


def _box(objects):
    import bpy
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objects:
        if o.type == "MESH":
            ev = o.evaluated_get(dg)
            pts += [ev.matrix_world @ v.co for v in ev.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


# ---------------------------------------------------------------------------------------------------------------------
# The plant

# Rosette leaves: (yaw, length, born at g); stalks: (yaw, height, lean, born at g, head turn)
ROSETTE = [(0.3, 0.16, 0.0), (3.4, 0.15, 0.0), (1.9, 0.17, 0.5), (5.0, 0.17, 0.6), (1.0, 0.18, 1.0), (4.2, 0.18, 1.1),
           (2.6, 0.19, 1.4), (5.7, 0.17, 1.5), (3.0, 0.16, 1.7)]
STALKS = [(2.0, 0.36, 0.30, 1.6, 0.0), (2.9, 0.30, 0.38, 1.75, 0.7), (3.8, 0.40, 0.22, 1.9, 1.4), (4.6, 0.33, 0.35, 2.05, 2.1),
          (5.4, 0.27, 0.42, 2.2, 2.8), (1.2, 0.29, 0.40, 2.3, 0.35), (3.3, 0.22, 0.45, 2.4, 1.05)]
HEAD_SCALE = 1.25


class PigmentRig(crop_rig.CropRig):
    """A Pigment Flower plant. Grow: two seed leaves, a rosette of long leaves, then seven stalks rising with green buds that
    show their colours at the tip, which open into paint-wheel heads when ripe. Wilt: the leaves sag onto the soil, the
    stalks lean out and the heads hang."""

    def __init__(self, m, label=None, stake=True, seed=5):
        super().__init__("PigmentFlowerPlant")
        self.m = m
        rnd = random.Random(seed)
        self.leaves = []
        for i, (yaw, L, born) in enumerate(ROSETTE):
            w = self.wilt_node(f"WiltLeaf{i}", self.sway, (0, 0, 0.004), yaw, droop=0.2 + 0.1 * (i % 2))  # sagging onto the soil, not into it
            leaf = fl.from_bm(f"Leaf{i}", leaf_bm(L, 0.021, 0.25, seed + i), [m["leaf"] if born > 0.1 else m["leaf_young"]], w)
            fl.solidify(leaf, 0.0009)
            self.leaves.append((leaf, born))
            self.grow_obs.append(leaf)
        self.stalks = []
        for i, (yaw, H, lean, born, turn) in enumerate(STALKS):
            w = self.wilt_node(f"WiltStalk{i}", self.sway, (0, 0, 0.006), yaw, droop=0.55)
            top = Vector((lean * H, 0, H))
            pts = fl.bezier((0, 0, 0), (0.0, 0, H * 0.4), (lean * H * 0.6, 0, H * 0.8), top, n=12)
            stalk = fl.from_bm(f"Stalk{i}", fl.tube(pts, [0.0034 - 0.0012 * k / 12 for k in range(13)], segs=8), [m["stem"]], w)
            node = fl.empty(f"Head{i}", w, top)
            node.rotation_euler = (0, math.radians(40) + rnd.uniform(-0.1, 0.1), 0)
            nod = self.wilt_node(f"WiltHead{i}", node, (0, 0, 0), 0.0, droop=1.6)   # dry, the heads hang
            bud = fl.from_bm(f"Bud{i}", bud_bm(seed=i), [m["bud"]], nod)
            bud.location.z = -0.004
            opened = head(f"Bloom{i}", nod, m, turn=turn, seed=seed + i)
            self.stalks.append((w, stalk, node, bud, opened, born, top))
            self.grow_obs += [stalk, node, bud, opened]
        if stake:
            self.stake(m, label)

    def grow(self, g):
        for leaf, born in self.leaves:
            s = fl.smoothstep(born, born + 1.2, g)
            leaf.scale = (max(1e-4, 0.15 + 0.85 * s) if g >= born else 1e-4,) * 3
        for w, stalk, node, bud, opened, born, top in self.stalks:
            rise = fl.smoothstep(born, born + 1.0, g)
            stalk.scale = (max(1e-4, rise),) * 3
            node.location = (top.x * rise, 0, top.z * rise)
            # the bud swells as it rises, colours at its tip from 2.8; the head opens from 3.3 and is wide open at 3.9
            b = fl.smoothstep(born, born + 0.8, g) * (1 - fl.smoothstep(3.35, 3.55, g))
            bud.scale = (max(1e-4, (0.5 + 0.5 * fl.smoothstep(born + 0.5, 3.2, g)) * b),) * 3
            o = fl.smoothstep(3.3, 3.9, g)
            opened.scale = (max(1e-4, HEAD_SCALE * (0.3 + 0.7 * o) * (1 if g >= 3.3 else 0)),) * 3


def build_rig(label):
    return PigmentRig(dict(mats(), **_stake_mats()), label)


WILD_LIFT = 0.02    # the wild rosettes' leaf tips arch down to the ground: lift them clear of uneven terrain (m)


def build_wild(name="WildPigmentFlower"):
    """Pigment Flowers growing wild in meadows and forests: three ripe plants without their stakes, as one still clump."""
    root = fl.empty(name)
    for k, (x, y, yaw, s) in enumerate(((0, 0, 0, 1.0), (0.17, 0.1, 2.1, 0.85), (-0.12, 0.15, 4.0, 0.9))):
        rig = PigmentRig(mats(), stake=False, seed=5 + 7 * k)
        crop_rig.pose(rig, 4.0)
        rig.root.parent = root
        rig.root.location = (x * plant.SCALE, y * plant.SCALE, WILD_LIFT)
        rig.root.rotation_euler = (0, 0, yaw)
        rig.root.scale = (plant.SCALE * s,) * 3
    return root


def _stake_mats():
    pm = plant.mats()
    return {k: pm[k] for k in ("stake", "stake_end", "twine", "tag", "tag_card")}

