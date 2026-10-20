"""The tomato plant as a game model: one rig whose clips the game scrubs.

* ``grow`` (4 s): clip time = growth, 0 just planted to 4 ripe (the soil's stages 0..4 are seconds 0..4). Stem
  segments, leaves, side shoots, trusses, flowers, ties and fruit scale in, leaves unfurl, fruit swells and ripens.
* ``wilt`` (1 s): 0 healthy to 1 wilted (dry soil): every leaf droops.
* ``sway`` (4 s loop): a breeze rocking the plant.

The three clips move disjoint nodes, so each runs on its own player at once. Z up here, Y up in the .glb.
"""

from __future__ import annotations

import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import food_lib as fl
import plant
import tomato

FPS = 24
GROW_SECONDS = 4.0
WILT_SECONDS = 1.0
SWAY_SECONDS = 4.0
SEG0 = plant.NODE_Z[0]
SEG = plant.NODE_Z[1] - plant.NODE_Z[0]
LEAF_TILT = math.radians(55)        # a young leaf points up this much before it unfurls
WILT_DROOP = math.radians(32)


def seg_len(i):
    return SEG0 if i == 0 else SEG


def seg_start(i):
    return 0.0 if i == 0 else plant.NODE_Z[i - 1]


class Rig:
    def __init__(self, m, label):
        self.m = m
        self.root = fl.empty("TomatoPlant")
        self.root.scale = (plant.SCALE,) * 3
        self.sway = fl.empty("Sway", self.root)
        self.nodes, self.segs, self.wilts, self.leaves, self.shoots = [], [], [], [], []
        self.trusses, self.flowers, self.fruits, self.ties = {}, {}, {}, []
        rnd = random.Random(11)
        n = len(plant.NODE_Z)
        for i in range(n):
            node = fl.empty(f"Node{i}", self.sway, (0, 0, seg_start(i)))
            r0 = 0.0085 * (1 - 0.55 * seg_start(i) / plant.NODE_Z[-1]) + 0.001
            r1 = 0.0085 * (1 - 0.55 * plant.NODE_Z[i] / plant.NODE_Z[-1]) + 0.001
            L = seg_len(i)
            pts = [(0.004 * math.sin(i * 1.7 + 6 * t * L), 0.003 * math.cos(i * 2.3 + 5 * t * L), t * L) for t in (0, 0.5, 1)]
            pts = [(0, 0, 0)] + pts[1:]
            seg = fl.from_bm(f"Seg{i}", fl.tube(pts, [r0, (r0 + r1) / 2, r1], segs=8), [m["plant_stem"]], node)
            self.nodes.append(node)
            self.segs.append(seg)
            # the leaf at the top of this segment
            a = i * plant.GOLD + 0.4
            top = (0, 0, L)
            wilt = fl.empty(f"Wilt{i}", node, top)
            wilt.rotation_euler = (0, 0, a)
            full = 0.2 - 0.008 * i
            leaf = fl.from_bm(f"Leaf{i}", fl.join(plant.compound_leaf(Vector((0, 0, 0)), Vector((1, 0, 0.3)), full, 1.0,
                                                                      seed=11 + i)), [m["leaf"]], wilt)
            self.wilts.append(wilt)
            self.leaves.append(leaf)
            if 2 <= i <= 10:
                b = a + 2.1
                ws = fl.empty(f"WiltShoot{i}", node, (0, 0, L + 0.01))
                ws.rotation_euler = (0, 0, b)
                sh = fl.from_bm(f"Shoot{i}", fl.join(plant.compound_leaf(Vector((0, 0, 0)), Vector((1, 0, 0.6)), full * 0.7,
                                                                          1.0, seed=61 + i)), [m["leaf"]], ws)
                self.wilts.append(ws)
                self.shoots.append((i, sh))
            if i in plant.TRUSSES:
                self._truss(i, a, node, top, rnd)
        # seed leaves
        cot = [plant.leaflet(Vector((0, 0, 0.028)), Vector((0.3 * s, s, 0.25)).normalized(), Vector((0, 0, 1)), 0.034, 0.008,
                             curl=0.1) for s in (1, -1)]
        self.cotyledons = fl.from_bm("Cotyledons", fl.join(cot), [m["leaf"]], self.sway)
        # twine ties round stem and stake
        for k, z in enumerate(plant.TIES):
            c = Vector((plant.STAKE_X / 2 + 0.003, 0, z))
            ring = [c + Vector((math.cos(2 * math.pi * j / 16) * (plant.STAKE_X / 2 + 0.012),
                                math.sin(2 * math.pi * j / 16) * 0.013, 0.002 * math.sin(4 * math.pi * j / 16)))
                    for j in range(17)]
            tie = fl.from_bm(f"Tie{k}", fl.tube(ring, [0.0017] * 17, segs=6, cap=False), [m["twine"]], self.root)
            tie.location = c
            for v in tie.data.vertices:
                v.co -= c
            self.ties.append((z, tie))
        plant.stake(self.root, "Plant", 1.0, m, label)
        for ob in [o for o in self.root.children if o.name.startswith("Plant_Twine")]:
            bpy.data.objects.remove(ob)

    def _truss(self, i, a, node, top, rnd):
        m = self.m
        ta = math.pi + (i - 7) * 0.5 + 0.3
        tdir = Vector((math.cos(ta), math.sin(ta), 0))
        p = Vector(top)
        p1 = p + tdir * 0.05 + Vector((0, 0, 0.015))
        p2 = p1 + tdir * 0.04 - Vector((0, 0, 0.035))
        bms = [fl.tube(fl.bezier(p, p + tdir * 0.02 + Vector((0, 0, 0.012)), p1, p2, n=8), [0.0018] * 9, segs=6)]
        hangs = []
        for k in range(plant.TRUSSES[i]):
            fa = ta + (k - 1) * 1.1
            fd = Vector((math.cos(fa), math.sin(fa), 0))
            hang = p2 + fd * 0.03 - Vector((0, 0, 0.012))
            bms.append(fl.tube([p2, (p2 + hang) / 2 + Vector((0, 0, 0.004)), hang], [0.0012] * 3, segs=6))
            hangs.append(hang)
        truss = fl.from_bm(f"Truss{i}", fl.join(bms), [m["plant_stem"]], node)
        self.trusses[i] = truss
        for k, hang in enumerate(hangs):
            pb, cone = plant.flower(Vector((0, 0, 0)), 1.0, rnd.uniform(0, 6))
            fl_ob = fl.from_bm(f"Flower{i}_{k}", fl.join(pb + [cone]), [m["flower"]], node, smooth=False)
            fl_ob.location = hang
            self.flowers[(i, k)] = fl_ob
            fruit = fl.empty(f"Fruit{i}_{k}", node, hang + Vector((0, 0, 0.003)))
            fruit.rotation_euler = (rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(0, 6))
            looks = {}
            for ripeness in ("green", "turning", "ripe"):
                ob = bpy.data.objects.new(f"Fruit{i}_{k}_{ripeness}", FRUIT_MESHES[ripeness])
                fl.link(ob, fruit)
                looks[ripeness] = ob
            self.fruits[(i, k)] = (fruit, looks)

    # -----------------------------------------------------------------------------------------------------------------

    def grow(self, g):
        L = plant.stem_length(g)
        z = 0.0
        for i, (node, seg) in enumerate(zip(self.nodes, self.segs)):
            s = min(1.0, max(0.0, (L - seg_start(i)) / seg_len(i)))
            node.location = (0, 0, z)
            seg.scale = (max(s, 1e-4),) * 3
            # the leaf at its top rides on the scaled segment's top: place the wilt node there
            self.wilts_of(i)[0].location = (0, 0, seg_len(i) * s)
            z += seg_len(i) * s
            age = (L - plant.NODE_Z[i]) / 0.11
            sz = min(1.0, max(0.0, age))
            shown = fl.smoothstep(0.0, 0.12, age)
            self.leaves[i].scale = (max(1e-4, shown * (0.2 + 0.8 * sz)),) * 3
            self.leaves[i].rotation_euler = (0, -LEAF_TILT * (1 - fl.smoothstep(0.2, 1.0, sz)), 0)
        for i, sh in self.shoots:
            age = (L - plant.NODE_Z[i]) / 0.11
            sz = min(1.0, max(0.0, age))
            self.wilts_of(i)[1].location = (0, 0, self.wilts_of(i)[0].location.z + 0.01 * (sz > 0))
            sh.scale = (max(1e-4, fl.smoothstep(0.4, 1.0, sz)),) * 3
            sh.rotation_euler = (0, -LEAF_TILT * (1 - fl.smoothstep(0.5, 1.0, sz)), 0)
        cot = 1 - fl.smoothstep(2.0, 3.0, g)
        self.cotyledons.scale = (max(1e-4, (0.6 + 0.4 * cot) * fl.smoothstep(-0.2, 0.15, g) * (cot > 0.02)),) * 3
        grow_t = fl.smoothstep(2.3, 2.8, g)
        for i, tr in self.trusses.items():
            tr.scale = (max(1e-4, grow_t),) * 3
        bloom = fl.smoothstep(2.4, 2.9, g) * (1 - fl.smoothstep(3.1, 3.4, g))
        for (i, k), f in self.flowers.items():
            f.scale = (max(1e-4, bloom),) * 3
            f.location = Vector(f.location) * 0 + self._hang_of(i, k) * grow_t
        set_ = fl.smoothstep(3.0, 3.8, g)
        for (i, k), (fruit, looks) in self.fruits.items():
            fruit.location = self._hang_of(i, k) * grow_t + Vector((0, 0, 0.003))
            fruit.scale = (max(1e-4, (0.2 + 0.5 * set_) * (set_ > 0.02)),) * 3
            want = "green" if g < 3.55 else ("turning" if g < 3.85 else "ripe")
            if g >= 3.85 and i == 10 and k == 1:
                want = "turning"
            for name, ob in looks.items():
                ob.scale = (1.0 if name == want else 1e-4,) * 3
        for zt, tie in self.ties:
            tie.scale = (max(1e-4, fl.smoothstep(zt + 0.03, zt + 0.06, L)),) * 3

    def _hang_of(self, i, k):
        return self._hangs[(i, k)]

    def wilts_of(self, i):
        return self._wilt_index[i]

    def index(self):
        self._wilt_index = {}
        for w in self.wilts:
            i = int(w.name.replace("WiltShoot", "").replace("Wilt", ""))
            self._wilt_index.setdefault(i, []).append(w)
        for i in self._wilt_index:
            self._wilt_index[i].sort(key=lambda o: o.name.startswith("WiltShoot"))
        self._hangs = {key: Vector(f.location) for key, f in self.flowers.items()}

    def wilt(self, w):
        for ws in self.wilts:
            ws.rotation_euler = (0, WILT_DROOP * w * (1.15 if ws.name.startswith("WiltShoot") else 1.0), ws.rotation_euler.z)

    def sway_pose(self, t):
        ph = 2 * math.pi * t / SWAY_SECONDS
        self.sway.rotation_euler = (0.03 * math.sin(ph), 0.022 * math.sin(2 * ph + 1.0), 0)


FRUIT_MESHES = {}


def fruit_meshes():
    """One shared mesh per ripeness, hanging from its stem point (origin), body below."""
    for ripeness in ("green", "turning", "ripe"):
        t = tomato.build(f"_fruit_{ripeness}", ripeness=ripeness, stem=False, m=dict(tomato.mats(ripeness)), segs=(22, 14))
        parts = [o for o in t.children if o.type == "MESH"]
        for o in parts:
            fl.apply_modifiers(o)
        bpy.ops.object.select_all(action="DESELECT")
        for o in parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        bpy.ops.object.join()
        ob = bpy.context.view_layer.objects.active
        h = ob.dimensions.z
        top = max(v.co.z for v in ob.data.vertices)
        for v in ob.data.vertices:
            v.co = Matrix.Rotation(math.pi, 3, "X") @ (v.co - Vector((0, 0, top)))
        ob.data.name = f"fruit_{ripeness}"
        FRUIT_MESHES[ripeness] = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.objects.remove(t)


def build(label):
    m = plant.mats()
    fruit_meshes()
    rig = Rig(m, label)
    rig.index()
    return rig


def _key(obs, frame, paths=("location", "rotation_euler", "scale")):
    for ob in obs:
        for p in paths:
            ob.keyframe_insert(p, frame=frame)


def bake(rig):
    import lift_textures as lt
    import build_lift as bl

    grow_obs = rig.nodes + rig.segs + rig.leaves + [sh for _, sh in rig.shoots] + [rig.cotyledons] \
        + list(rig.trusses.values()) + list(rig.flowers.values()) + [f for f, _ in rig.fruits.values()] \
        + [t for _, t in rig.ties] + [w for w in rig.wilts]
    looks = [ob for _, ls in rig.fruits.values() for ob in ls.values()]
    tracks = {}

    def start(obs, name):
        for ob in obs:
            ob.animation_data_create()
            act = bpy.data.actions.new(f"{name}_{ob.name}")
            ob.animation_data.action = act
            tracks.setdefault(name, []).append((ob, act))

    # grow: wilt nodes only move up with their segment here (location); wilt rotates them (rotation) in its own clip
    start(grow_obs + looks, "grow")
    frames = int(GROW_SECONDS * FPS)
    for f in range(frames + 1):
        rig.grow(GROW_SECONDS * f / frames)
        _key([o for o in grow_obs if not o.name.startswith("Wilt")], f)
        _key([o for o in grow_obs if o.name.startswith("Wilt")], f, ("location",))
        _key(looks, f, ("scale",))
    for ob, act in tracks["grow"]:
        lt.linear_keys(act)
        if ob in looks:
            _constant(act)
        bl.push_track(ob, act, "grow")
    rig.grow(GROW_SECONDS)
    start(rig.wilts, "wilt")
    for f in range(int(WILT_SECONDS * FPS) + 1):
        rig.wilt(f / (WILT_SECONDS * FPS))
        _key(rig.wilts, f, ("rotation_euler",))
    for ob, act in tracks["wilt"]:
        lt.linear_keys(act)
        bl.push_track(ob, act, "wilt")
    rig.wilt(0)
    start([rig.sway], "sway")
    for f in range(int(SWAY_SECONDS * FPS) + 1):
        rig.sway_pose(f / FPS)
        _key([rig.sway], f, ("rotation_euler",))
    for ob, act in tracks["sway"]:
        lt.linear_keys(act)
        bl.push_track(ob, act, "sway")
    rig.sway_pose(0)


def _constant(action):
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                curves.extend(bag.fcurves)
    for fc in curves:
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"
