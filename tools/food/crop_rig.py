"""What every crop plant model shares: one rig whose three clips the game plays (docs/part-data-schema.md, Crop plant models).

* ``grow`` (4 s): clip time = growth, 0 just planted to 4 ripe (the soil's stages 0..4 are seconds 0..4).
* ``wilt`` (1 s): 0 healthy to 1 wilted (dry soil): every wilt node droops.
* ``sway`` (4 s loop): a breeze rocking the plant.

A rig subclass builds its nodes and poses them in ``grow(g)``; it lists what moves:

* ``grow_obs`` - keyed on location, rotation and scale in ``grow``;
* ``wilt_nodes`` - (node, droop factor): keyed on location in ``grow`` (so they ride up with the plant) and on rotation
  in ``wilt``; their rotation is (0, droop, yaw) and ``grow`` must never set it;
* ``looks`` - switched on and off by scale with constant keys in ``grow`` (fruit ripening, leaves yellowing);
* ``sway`` - the node the breeze rocks.

The three clips touch different properties, so the game runs each on its own player at once. Z up here, Y up in the .glb.
"""

from __future__ import annotations

import math

import bpy

import food_lib as fl
import plant

FPS = 24
GROW_SECONDS = 4.0
WILT_SECONDS = 1.0
SWAY_SECONDS = 4.0
WILT_DROOP = math.radians(32)


class CropRig:
    """A crop plant: ``root`` (scaled like the tomato, so every crop fills its soil mound alike) and ``sway`` under it."""

    def __init__(self, name, scale=plant.SCALE):
        self.root = fl.empty(name)
        self.root.scale = (scale,) * 3
        self.sway = fl.empty("Sway", self.root)
        self.grow_obs = []
        self.wilt_nodes = []
        self.looks = []

    def wilt_node(self, name, parent, location, yaw, droop=1.0):
        node = fl.empty(name, parent, location)
        node.rotation_euler = (0, 0, yaw)
        self.wilt_nodes.append((node, droop))
        return node

    def grow(self, g):
        raise NotImplementedError

    def wilt(self, w):
        for node, droop in self.wilt_nodes:
            node.rotation_euler = (0, WILT_DROOP * w * droop, node.rotation_euler.z)

    def sway_pose(self, t):
        ph = 2 * math.pi * t / SWAY_SECONDS
        self.sway.rotation_euler = (0.03 * math.sin(ph), 0.022 * math.sin(2 * ph + 1.0), 0)

    def stake(self, m, label):
        """The scrap-wood stake and its crop tag, as on the tomato (no twine: only a vine is tied)."""
        plant.stake(self.root, "Plant", 0.0, m, label)
        for ob in [o for o in self.root.children if o.name.startswith("Plant_Twine")]:
            bpy.data.objects.remove(ob)


def _key(obs, frame, paths=("location", "rotation_euler", "scale")):
    for ob in obs:
        for p in paths:
            ob.keyframe_insert(p, frame=frame)


def _constant(action):
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for kp in fc.keyframe_points:
                        kp.interpolation = "CONSTANT"


def bake(rig):
    """Keys ``grow``, ``wilt`` and ``sway`` into NLA tracks of those names (one glTF animation each) and leaves the rig
    ripe, fresh and still."""
    import build_lift as bl
    import lift_textures as lt

    wilts = [n for n, _ in rig.wilt_nodes]
    tracks = {}

    def start(obs, name):
        for ob in obs:
            ob.animation_data_create()
            act = bpy.data.actions.new(f"{name}_{ob.name}")
            ob.animation_data.action = act
            tracks.setdefault(name, []).append((ob, act))

    start(rig.grow_obs + wilts + rig.looks, "grow")
    frames = int(GROW_SECONDS * FPS)
    for f in range(frames + 1):
        rig.grow(GROW_SECONDS * f / frames)
        _key(rig.grow_obs, f)
        _key(wilts, f, ("location",))
        _key(rig.looks, f, ("scale",))
    for ob, act in tracks["grow"]:
        lt.linear_keys(act)
        if ob in rig.looks:
            _constant(act)
        bl.push_track(ob, act, "grow")
    rig.grow(GROW_SECONDS)
    start(wilts, "wilt")
    for f in range(int(WILT_SECONDS * FPS) + 1):
        rig.wilt(f / (WILT_SECONDS * FPS))
        _key(wilts, f, ("rotation_euler",))
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


def pose(rig, g, w=0.0):
    """Poses the rig for a still (design renders) without keys."""
    rig.grow(g)
    rig.wilt(w)
    bpy.context.view_layer.update()
