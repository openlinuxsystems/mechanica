"""The tomato plant, grown continuously: ``build(g)`` for any growth g from 0 (just planted) to 4 (ripe).

The game's stages 0..4 (SoilState.Stages) are g = 0, 1, 2, 3, 4; between them the plant grows smoothly, so the same
model plays as a scrubbed "grow" clip and every stage change animates instead of popping. Z up, metres, origin on the
soil surface at the plant's base.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

import bmesh
from mathutils import Matrix, Quaternion, Vector

import food_lib as fl
import tomato

GOLD = 2.39996                                    # leaves spiral round the stem by the golden angle
NODE_Z = [0.04 + 0.034 * i for i in range(14)]    # leaf nodes up the stem
TRUSSES = {4: 3, 7: 3, 10: 2}                     # node -> fruits in the cluster hanging there
STAKE_X = 0.045
TIES = [0.14, 0.29, 0.42]
SCALE = 1.6                                       # the plant fills its 0.75 m soil mound: knee high when ripe


def mats():
    m = tomato.mats("ripe")
    m.update({
        "leaf": fl.material("tomato_leaf", "#4A8A30", rough=0.55, sss=0.15),
        "leaf_old": fl.material("tomato_leaf_old", "#9DA845", rough=0.6, sss=0.1),
        "leaf_dry": fl.material("tomato_leaf_dry", "#7E8C3E", rough=0.7, sss=0.05),
        "plant_stem": fl.material("tomato_plant_stem", "#5B8F37", rough=0.6),
        "flower": fl.material("tomato_flower", "#F2C531", rough=0.5, sss=0.2),
        "stake": fl.material("stake_wood", "#9C7650", rough=0.8),
        "stake_end": fl.material("stake_end", "#C9A97C", rough=0.85),
        "twine": fl.material("twine", "#CDB98C", rough=0.9),
        "tag": fl.material("stake_tag", "#C8423A", rough=0.6),
        "tag_card": fl.material("stake_tag_card", "#ECE4CD", rough=0.75),
    })
    for r in ("green", "turning"):
        m["skin_" + r] = tomato.mats(r)["skin"]
    return m


def stem_length(g):
    return 0.03 + 0.12 * g if g < 3.5 else 0.03 + 0.12 * 3.5 + 0.03 * (g - 3.5)


def stem_point(z, g):
    """The stem's centre line at height z: a gentle lean toward the stake once it is tied."""
    tie = fl.smoothstep(1.2, 2.2, g)
    return Vector((0.012 * math.sin(z * 9) * (1 - tie) + tie * 0.022 * min(1, z / 0.25), 0.006 * math.sin(z * 13 + 1), z))


def leaflet(base, d, n, length, width, curl, fold=0.35):
    """One leaflet: an ovate blade along d, drooping with curl, a little serrated."""
    pts, ws, ns = [], [], []
    side = d.cross(n).normalized()
    N = 10
    for i in range(N + 1):
        s = i / N
        p = base + d * (length * s) - n * (curl * length * s * s)
        pts.append(p)
        w = width * math.sin(math.pi * min(1, s * 0.92 + 0.08)) ** 0.75 * (1 - 0.12 * (i % 2)) + 0.0004
        ws.append(w)
        ns.append(n)
    return fl.strip(pts, ws, ns, fold=fold)


def compound_leaf(base, out, length, unfurl, seed, dry=0.0):
    """A tomato leaf: a rachis with 2-3 pairs of leaflets and a terminal one; young leaves are small and folded up."""
    rnd = random.Random(seed)
    out = out.normalized()
    up = Vector((0, 0, 1))
    side = out.cross(up).normalized()
    nrm = side.cross(out).normalized()
    droop = 0.35 + 0.35 * unfurl + 0.55 * dry
    pts = fl.bezier(base, base + out * length * 0.35 + up * length * 0.25,
                    base + out * length * 0.75 + up * length * (0.2 - 0.25 * droop),
                    base + out * length + up * length * (0.05 - 0.45 * droop), n=10)
    radii = [0.0022 * (1 - 0.6 * i / 10) + 0.0006 for i in range(11)]
    bms = [fl.tube(pts, radii, segs=6)]
    pairs = 3 if length > 0.08 else 2
    for k in range(pairs):
        t = 0.3 + 0.55 * k / max(1, pairs - 1) * 0.85
        i = min(9, int(t * 10))
        p = pts[i]
        tan = (pts[i + 1] - pts[i]).normalized()
        sd = tan.cross(up).normalized()
        for sgn in (1, -1):
            spread = 0.35 + 0.9 * unfurl                           # leaflets open out as the leaf unfurls
            d = (tan * math.cos(spread) + sd * sgn * math.sin(spread)).normalized()
            n = (up - d * d.dot(up)).normalized()
            L = length * (0.3 + 0.08 * k / pairs) * rnd.uniform(0.9, 1.1)
            bms.append(leaflet(p, d, n, L, L * 0.38, curl=0.25 * unfurl + 0.35 * dry))
    tip = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    bms.append(leaflet(tip, d, (up - d * d.dot(up)).normalized(), length * 0.42, length * 0.15, curl=0.2 * unfurl))
    return bms


def flower(p, s, rot):
    """A small yellow tomato flower hanging down: five petals swept back."""
    bms = []
    down = Vector((0, 0, -1))
    for k in range(5):
        a = rot + 2 * math.pi * k / 5
        d = Vector((math.cos(a), math.sin(a), 0))
        tip = p + (d * 0.012 + Vector((0, 0, 0.004))) * s
        pts = fl.bezier(p, p + d * 0.004 * s - Vector((0, 0, 0.002)) * s, tip - Vector((0, 0, 0.002)) * s, tip, n=5)
        bms.append(fl.strip(pts, [0.002 * s * math.sin(math.pi * min(1, i / 5 * 0.9 + 0.1)) + 0.0003 for i in range(6)],
                            [(0, 0, 1)] * 6))
    cone = fl.tube([p, p + down * 0.007 * s], [0.0022 * s, 0.0008 * s], segs=8)
    return bms, cone


def build(g, name="TomatoPlant", parent=None, location=(0, 0, 0), m=None, seed=11, with_stake=True, label=None, dry=0.0):
    """The plant at growth ``g``; ``dry`` (0..1) wilts it, which is how it shows its soil has run out of water."""
    import bpy
    m = m or mats()
    rnd = random.Random(seed)
    root = fl.empty(name, parent, location)
    root.scale = (SCALE,) * 3
    L = stem_length(g)
    # main stem
    n = max(4, int(L / 0.012))
    pts = [stem_point(L * i / n, g) for i in range(n + 1)]
    radii = [(0.0025 + 0.006 * fl.smoothstep(0, 3, g)) * (1 - 0.55 * i / n) + 0.001 for i in range(n + 1)]
    stem_bms = [fl.tube(pts, radii, segs=10)]
    leaf_bms, old_bms, flower_bms = [], [], []
    # seed leaves: two narrow cotyledons that yellow and wither as the plant grows
    cot = 1 - fl.smoothstep(2.0, 3.0, g)
    if cot > 0.05:
        cz = min(L, 0.03)
        for sgn in (1, -1):
            d = Vector((0.3 * sgn, sgn, 0.25)).normalized()
            b = leaflet(stem_point(cz, g), d, Vector((0, 0, 1)), 0.034 * (0.6 + 0.4 * cot), 0.008, curl=0.3 * (1 - cot) + 0.1)
            (leaf_bms if g < 1.5 else old_bms).append(b)
    # true leaves
    for i, z in enumerate(NODE_Z):
        age = (L - z) / 0.11
        if age <= 0:
            continue
        s = min(1.0, age)
        p = stem_point(z, g)
        a = i * GOLD + 0.4
        out = Vector((math.cos(a), math.sin(a), 0.45 - 0.15 * s))
        full = 0.2 - 0.008 * i
        length = full * (0.2 + 0.8 * s)
        leaf_bms += compound_leaf(p, out, length, unfurl=fl.smoothstep(0.2, 1.0, s), seed=seed + i, dry=dry)
        if 2 <= i <= 10 and s > 0.4:                       # side shoots fill the bush out
            b = a + 2.1
            leaf_bms += compound_leaf(p + Vector((0, 0, 0.01)), Vector((math.cos(b), math.sin(b), 0.6)),
                                      length * 0.7 * fl.smoothstep(0.4, 1.0, s), unfurl=fl.smoothstep(0.5, 1.0, s),
                                      seed=seed + 50 + i, dry=dry)
        # fruit trusses, opposite the leaf
        if i in TRUSSES and g > 2.3:
            ta = math.pi + (i - 7) * 0.5 + 0.3          # away from the stake, so the fruit shows
            tdir = Vector((math.cos(ta), math.sin(ta), 0))
            grow = fl.smoothstep(2.3, 2.8, g)
            p1 = p + tdir * 0.05 * grow + Vector((0, 0, 0.015))
            p2 = p1 + tdir * 0.04 * grow - Vector((0, 0, 0.035 * grow))
            stem_bms.append(fl.tube(fl.bezier(p, p + tdir * 0.02 + Vector((0, 0, 0.012)), p1, p2, n=8),
                                    [0.0018] * 9, segs=6))
            for k in range(TRUSSES[i]):
                fa = ta + (k - 1) * 1.1
                fd = Vector((math.cos(fa), math.sin(fa), 0))
                hang = p2 + fd * 0.03 * grow - Vector((0, 0, 0.012)) * grow
                stem_bms.append(fl.tube([p2, (p2 + hang) / 2 + Vector((0, 0, 0.004)), hang], [0.0012] * 3, segs=6))
                bloom = fl.smoothstep(2.4, 2.9, g) * (1 - fl.smoothstep(3.1, 3.4, g))
                set_ = fl.smoothstep(3.0, 3.8, g)
                if bloom > 0.02:
                    pb, cone = flower(hang, 0.4 + 0.6 * bloom, rnd.uniform(0, 6))
                    flower_bms += pb
                    stem_bms.append(cone)
                if set_ > 0.02:
                    sc = 0.2 + 0.5 * set_
                    ripe = "green" if g < 3.55 else ("turning" if g < 3.85 else "ripe")
                    if g >= 3.85 and i == 10 and k == 1:       # one late one still turning: the plant looks alive
                        ripe = "turning"
                    body_h = 0.07 * sc
                    fruit = tomato.build(f"{name}_Fruit{i}{k}", ripeness=ripe, scale=sc, seed=seed + 10 * i + k,
                                         stem=False, parent=root, location=hang - Vector((0, 0, body_h + 0.004)),
                                         m=dict(tomato.mats(ripe)))
                    fruit.rotation_euler = (math.pi + rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, 6))
                    fruit.location = hang + Vector((0, 0, 0.003))
    fl.from_bm(name + "_Stem", fl.join(stem_bms), [m["plant_stem"]], root)
    if leaf_bms:
        o = fl.from_bm(name + "_Leaves", fl.join(leaf_bms), [m["leaf_dry" if dry > 0.5 else "leaf"]], root)
        fl.solidify(o, 0.0009, offset=0)
    if old_bms:
        o = fl.from_bm(name + "_OldLeaves", fl.join(old_bms), [m["leaf_old"]], root)
        fl.solidify(o, 0.0009, offset=0)
    if flower_bms:
        o = fl.from_bm(name + "_Flowers", fl.join(flower_bms), [m["flower"]], root)
        fl.solidify(o, 0.0008, offset=0)
    if with_stake:
        stake(root, name, g, m, label)
    return root


def stake(root, name, g, m, label=None):
    """The scrap-wood stake every planted crop gets, with a tag in the crop's colour and twine ties as it grows."""
    h = 0.5
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        x, y, z = v.co
        taper = 1.0 if z > -0.45 else 0.5
        v.co = Vector((STAKE_X + x * 0.022 * taper + 0.012 * (z + 0.5), y * 0.022 * taper, (z + 0.5) * (h + 0.05) - 0.05))
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.0025, segments=2)
    fl.from_bm(name + "_Stake", bm, [m["stake"]], root, smooth=False)
    # the crop tag nailed near the top: a cream card over a band in the crop's colour (the seed packet's label)
    tag = bmesh.new()
    bmesh.ops.create_cube(tag, size=1.0)
    for v in tag.verts:
        x, y, z = v.co
        v.co = Vector((STAKE_X + 0.006 + x * 0.056, -0.014 + y * 0.005, h - 0.06 + z * 0.064))
    bmesh.ops.bevel(tag, geom=tag.edges[:], offset=0.002, segments=1)
    fl.from_bm(name + "_TagCard", tag, [m["tag_card"]], root, smooth=False)
    if label is not None:
        # the seed packet's label, small, on the front of the card
        face = bmesh.new()
        uv = face.loops.layers.uv.new("UVMap")
        cx, cz, hw = STAKE_X + 0.006, h - 0.06, 0.0235
        hh = hw * 1.18
        vs = [face.verts.new((cx + x * hw, -0.0168, cz + z * hh)) for x, z in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        f = face.faces.new(vs)
        for lp, (u, v) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            lp[uv].uv = (u, v)
        fl.from_bm(name + "_TagLabel", face, [fl.image_material("tag_label_" + Path(label).stem, label)], root,
                   smooth=False)
    ties = []
    for z in TIES:
        if stem_length(g) > z + 0.03:
            c = (stem_point(z, g) + Vector((STAKE_X + 0.012 * (z / h + 0.5), 0, z))) / 2
            ring = []
            for k in range(17):
                a = 2 * math.pi * k / 16
                ring.append(c + Vector((math.cos(a) * 0.026, math.sin(a) * 0.013, 0.002 * math.sin(2 * a))))
            ties.append(fl.tube(ring, [0.0017] * 17, segs=6, cap=False))
    if ties:
        fl.from_bm(name + "_Twine", fl.join(ties), [m["twine"]], root)
