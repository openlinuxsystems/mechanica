"""Build the five Scuttlers (crab chassis concept approved by Chase on 2026-10-19).

The Scuttler family shares one body: a low hexagonal shell on six crab legs, two raised serrated claw arms, and one
eye on a stalk. Each variant changes its paint and adds one signature piece that telegraphs its attack:

* Green: a scorpion tail of cable segments ending in a spiked weight (whip);
* Blue: a drill ram under the eye and a water kit (webbed feet, twin ducted props, pontoons, snorkel, porthole);
* Yellow (1.25x, thick legs): a piston hammer under the belly and hydraulic rams on the shell (stomp);
* Red: a pressure tank with a gauge and a warning lamp (it detonates);
* Seed (0.85x, dark green): a leaky burlap seed sack on its back.

The paint is worn: chipped through to rust and bare steel, scratched and grimy.

What it writes: ``mods/vanilla/meshes/bots/scuttler_<variant>.glb``. Feet are at the origin and it faces -Z, as bots
do. The nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the shell), ``Stalk`` and ``EyeHead`` (the eye), ``LampGlow`` and ``EyeLens`` (the glowing eye and front
  lamps, which the game colours green while calm and red while engaged);
* ``Claw<L|R>_Shoulder`` / ``_Elbow`` / ``_Wrist`` / ``_Jaw`` (the upper jaw opens);
* ``Leg<L|R><1-3>_Hip`` / ``_Knee`` / ``_Foot`` (1 is the front pair; ``_Foot`` is an empty at the tip);
* Green ``Tail0`` … ``Tail13`` (base to weight); Blue ``Drill``, ``PropL``, ``PropR``; Yellow ``Hammer``;
  Red ``Tank``, ``GaugeNeedle``; Seed ``Sack``.

Clips (seconds; the loops loop): ``idle`` 2.4, ``patrol`` 0.5 and ``chase`` 0.24 (gaits; their ground speeds at
speed scale 1 are printed and go in the bot's ``model.clip_speeds``), ``alert`` 0.8, ``lose_interest`` 0.7,
``hit`` 0.35, ``death`` 1.6 (Red: it bursts), ``jump`` 0.35, ``fall`` 0.6, ``land`` 0.35, and each variant's attacks:
Green ``tail_slam``, ``tail_sweep`` and ``claw_combo`` (1.0 each), Blue ``drill_ram`` 0.9, Yellow ``stomp`` 1.2,
Red ``detonate`` 0.4, Seed ``claw_snap`` 0.5. Strikes land about 0.2 s in: the host deals the damage as an attack
starts.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/scuttler/build_scuttler.py [--variant blue ...]                # writes the .glb files
    python tools/scuttler/build_scuttler.py --render OUT_DIR [--variant ...]    # review renders and sheets

The output is committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up and the model is built facing -Y, then turned to face -Z in glTF. The build is
deterministic.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "lift"))
import lift_textures as lt  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "bots"
FPS = 30
SAMPLES = int(os.environ.get("SCUTTLER_SAMPLES", "48"))

# Paint, size, leg thickness and the attack clips each attack plays (the bot's model.attacks).
VARIANTS = {
    "green": dict(name="Green Scuttler", paint="#5fae3e", scale=1.0, leg=1.0, attacks={"whip": ["tail_slam", "tail_sweep", "claw_combo"]}),
    "blue": dict(name="Blue Scuttler", paint="#3e7fd0", scale=1.0, leg=1.0, attacks={"drill": ["drill_ram"]},
                 extra={"swim": 1.2, "swim_idle": 2.4}),
    "yellow": dict(name="Yellow Scuttler", paint="#e6b31e", scale=1.25, leg=1.45, attacks={"stomp": ["stomp"]}),
    "red": dict(name="Red Scuttler", paint="#d0402e", scale=1.0, leg=1.0, attacks={"explode": ["detonate"]}),
    "seed": dict(name="Seed Scuttler", paint="#2f5a2c", scale=0.85, leg=1.0, attacks={"nip": ["claw_snap"]}),
}
ATTACK_CLIPS = {"tail_slam": 1.0, "tail_sweep": 1.0, "claw_combo": 1.0, "drill_ram": 0.9, "stomp": 1.2, "detonate": 0.4, "claw_snap": 0.5}
VARIANT = ["green"]

Z0 = 0.30                   # bottom of the belly pan
EYE_Z = Z0 + 0.48
UP = Vector((0, 0, 1))

# Leg rest geometry: hip on the pan side, knee out and up, foot tip on the ground. 1 is the front pair.
LEG_Y = {1: -0.16, 2: 0.0, 3: 0.16}
LEGS = [f"{s}{k}" for s in "LR" for k in (1, 2, 3)]
GROUP_A = {"L1", "R2", "L3"}            # tripod gait: A and B alternate


def leg_points(name):
    sx = -1 if name[0] == "L" else 1
    y = LEG_Y[int(name[1])]
    spread = y * 1.3
    hip = Vector((sx * 0.32, y, Z0 + 0.07))
    knee = Vector((sx * 0.52, y + spread * 0.45, Z0 + 0.2))
    foot = Vector((sx * 0.66, y + spread, 0.0))
    return hip, knee, foot


def claw_points(sx):
    sh = Vector((sx * 0.22, -0.22, Z0 + 0.06))
    el = Vector((sx * 0.34, -0.36, Z0 + 0.24))
    wr = Vector((sx * 0.22, -0.52, Z0 + 0.2))
    fwd = Vector((-sx * 0.25, -1, 0)).normalized()
    palm_end = wr + fwd * 0.1
    return sh, el, wr, fwd, palm_end


def tail_points():
    pts = []
    for i in range(14):
        t = i / 13
        a = math.radians(-20 + 230 * t)
        r = 0.32 * (1 - 0.35 * t)
        pts.append(Vector((0, 0.2 + r * math.sin(a) * 0.9, Z0 + 0.25 + r * (1 - math.cos(a)))))
    return pts


# =====================================================================================================
# Geometry, gathered per rig node
# =====================================================================================================
class Geo:
    """Vertices, faces and per-face material names, in model space."""

    def __init__(self):
        self.v, self.f, self.m = [], [], []

    def add_bm(self, bm, mat, matrix=None):
        bm.verts.ensure_lookup_table()
        base = len(self.v)
        for v in bm.verts:
            self.v.append(tuple(matrix @ v.co) if matrix is not None else tuple(v.co))
        for f in bm.faces:
            self.f.append(tuple(base + v.index for v in f.verts))
            self.m.append(mat)
        bm.free()


GEO: dict[str, Geo] = {}
CUR = ["Body"]


def g():
    return GEO.setdefault(CUR[0], Geo())


def at(node):
    """Puts the following pieces on rig node `node`."""
    CUR[0] = node


def sphere(c, r, mat, scale=(1, 1, 1), segs=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(6, segs // 2), radius=r)
    bmesh.ops.scale(bm, verts=bm.verts, vec=scale)
    bmesh.ops.translate(bm, verts=bm.verts, vec=c)
    g().add_bm(bm, mat)


def rod(a, b, r, mat, r2=None, segs=14):
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=d.length)
    bmesh.ops.transform(bm, matrix=d.to_track_quat("Z", "Y").to_matrix().to_4x4(), verts=bm.verts)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(a + b) / 2)
    g().add_bm(bm, mat)


def disc(c, r, depth, axis, mat, segs=24):
    axis = Vector(axis)
    rod(Vector(c) - axis * depth / 2, Vector(c) + axis * depth / 2, r, mat, segs=segs)


def box(size, c, mat, bevel=0.006, rot=Matrix.Identity(4)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    bmesh.ops.scale(bm, verts=bm.verts, vec=size)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
    bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, verts=bm.verts, vec=c)
    g().add_bm(bm, mat)


def prism_z(outline, z0, z1, mat, bevel=0.012, top_scale=1.0):
    bm = bmesh.new()
    vs = [bm.verts.new((x, y, z0)) for x, y in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.z = z1
            v.co.x *= top_scale
            v.co.y *= top_scale
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
    g().add_bm(bm, mat)


def hexagon(hx, hy, cut):
    return [(-hx, 0), (-hx + cut, -hy), (hx - cut, -hy), (hx, 0), (hx - cut, hy), (-hx + cut, hy)]


def build_geometry():
    GEO.clear()
    # ---- Body: belly pan, painted shell, spine plate, rivets, hazard chevrons, lamp bezels, tail mount ----
    at("Body")
    prism_z(hexagon(0.34, 0.25, 0.11), Z0, Z0 + 0.12, "gun", bevel=0.015)
    prism_z(hexagon(0.36, 0.265, 0.12), Z0 + 0.12, Z0 + 0.22, "paint", bevel=0.02, top_scale=0.86)
    prism_z(hexagon(0.18, 0.13, 0.05), Z0 + 0.22, Z0 + 0.25, "paint", bevel=0.012, top_scale=0.9)
    for i in range(14):
        a = 2 * math.pi * i / 14
        sphere((0.32 * math.cos(a), 0.232 * math.sin(a), Z0 + 0.125), 0.011, "steel", segs=8)
    for i in range(5):
        box((0.03, 0.01, 0.05), (-0.1 + i * 0.05, -0.266, Z0 + 0.06), "hazard", bevel=0.002)
    for sx in (-1, 1):
        disc((sx * 0.17, -0.235, Z0 + 0.075), 0.032, 0.02, (0, 1, 0), "bezel")
    disc((0, 0.2, Z0 + 0.26), 0.07, 0.04, (0, 0, 1), "gun")
    disc((0, -0.08, Z0 + 0.23), 0.05, 0.03, (0, 0, 1), "gun")
    at("LampGlow")
    for sx in (-1, 1):
        disc((sx * 0.17, -0.244, Z0 + 0.075), 0.022, 0.02, (0, 1, 0), "glow")

    # ---- Eye on its stalk ----
    at("Stalk")
    rod((0, -0.08, Z0 + 0.22), (0, -0.12, EYE_Z - 0.05), 0.028, "steel")
    for k in range(3):
        rod((0, -0.088 - k * 0.011, Z0 + 0.27 + k * 0.045), (0, -0.09 - k * 0.011, Z0 + 0.285 + k * 0.045), 0.04, "gun")
    at("EyeHead")
    disc((0, -0.13, EYE_Z), 0.085, 0.11, (0, 1, 0), "paint")
    disc((0, -0.19, EYE_Z), 0.07, 0.02, (0, 1, 0), "bezel")
    box((0.2, 0.08, 0.025), (0, -0.18, EYE_Z + 0.085), "paint", bevel=0.008, rot=Matrix.Rotation(math.radians(-12), 4, "X"))
    sphere((0, -0.075, EYE_Z), 0.06, "gun", scale=(1, 0.6, 1))
    at("EyeLens")
    sphere((0, -0.196, EYE_Z), 0.05, "glow", scale=(1, 0.45, 1))

    # ---- Claws: raised two-segment arms with serrated pincers ----
    for sx, side in ((-1, "L"), (1, "R")):
        sh, el, wr, fwd, palm_end = claw_points(sx)
        at(f"Claw{side}_Shoulder")
        sphere(sh, 0.06, "gun")
        rod(sh, el, 0.038, "gun")
        rod(sh.lerp(el, 0.2), sh.lerp(el, 0.85), 0.055, "paint", segs=16)
        rod(sh.lerp(el, 0.5) + Vector((0, 0, -0.04)), sh.lerp(el, 0.75) + Vector((0, 0, -0.04)), 0.024, "gun")
        at(f"Claw{side}_Elbow")
        sphere(el, 0.055, "steel")
        rod(el, el + Vector((sx * 0.06, 0.05, 0.08)), 0.03, "steel", r2=0.002, segs=10)
        rod(el, wr, 0.04, "gun")
        rod(el.lerp(wr, 0.1) + Vector((0, 0, -0.04)), el.lerp(wr, 0.7) + Vector((0, 0, -0.04)), 0.014, "steel")
        at(f"Claw{side}_Wrist")
        sphere(wr, 0.05, "gun")
        rod(wr, palm_end, 0.075, "paint", r2=0.065, segs=16)
        sphere(palm_end, 0.045, "steel")
        for j, lift in enumerate((0.55, -0.25)):
            at(f"Claw{side}_Jaw" if j == 0 else f"Claw{side}_Wrist")
            d = (fwd + Vector((0, 0, lift))).normalized()
            base = palm_end + Vector((0, 0, 0.03 if lift > 0 else -0.03))
            tip = base + d * 0.22
            inward = Vector((0, 0, -1 if lift > 0 else 1))
            rod(base, tip, 0.045, "paint", r2=0.025, segs=14)
            rod(tip, tip + inward * 0.07 + fwd * 0.02, 0.025, "steel", r2=0.002, segs=10)
            for t in range(5):
                q = base.lerp(tip, 0.15 + 0.17 * t) + inward * 0.03
                rod(q, q + inward * 0.05 + fwd * 0.012, 0.014, "steel", r2=0.001, segs=8)

    # ---- Legs: hip ball, gunmetal femur in painted armour, steel knee, tapered tibia, rubber tip ----
    for name in LEGS:
        hip, knee, foot = leg_points(name)
        r = 0.036 * VARIANTS[VARIANT[0]]["leg"]
        at(f"Leg{name}_Hip")
        sphere(hip, 0.055, "gun")
        rod(hip, knee, r, "gun")
        rod(hip.lerp(knee, 0.2), hip.lerp(knee, 0.9), r * 1.7, "paint", segs=16)
        rod(hip.lerp(knee, 0.55) + Vector((0, 0, 0.05)), knee + Vector((0, 0, 0.02)), r * 0.3, "steel")
        at(f"Leg{name}_Knee")
        sphere(knee, 0.05, "steel")
        tip_top = foot + Vector((-0.01 if foot.x > 0 else 0.01, 0, 0.09))
        rod(knee, tip_top, r * 1.05, "gun", r2=r * 0.75, segs=8)
        rod(knee.lerp(foot, 0.15), knee.lerp(foot, 0.6), r * 1.35, "paint", r2=r, segs=16)
        rod(tip_top, foot, r * 0.75, "rubber", r2=r * 0.15, segs=8)

    SIGNATURES[VARIANT[0]]()


def green_tail():
    """Cable segments curled over the back like a scorpion's, a spiked weight at the end."""
    pts = tail_points()
    for i in range(len(pts) - 1):
        at(f"Tail{i}")
        sphere(pts[i], 0.038 - 0.0015 * i, "paint" if i % 2 else "gun", segs=12)
        rod(pts[i], pts[i + 1], 0.03 - 0.0012 * i, "steel", segs=10)
    at(f"Tail{len(pts) - 1}")
    tip = pts[-1]
    sphere(tip, 0.06, "gun")
    for k in range(6):
        a = 2 * math.pi * k / 6
        d = Vector((math.cos(a), 0.4 * math.sin(a), math.sin(a))).normalized()
        rod(tip, tip + d * 0.1, 0.018, "steel", r2=0.002, segs=8)


def tube(pts, r, mat, segs=10):
    """A closed tube through `pts` (a ring)."""
    bm = bmesh.new()
    n = len(pts)
    rings = []
    for i, q in enumerate(pts):
        t = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        ref = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 0, 1))
        u = t.cross(ref).normalized()
        w = t.cross(u).normalized()
        rings.append([bm.verts.new(q + r * (math.cos(2 * math.pi * k / segs) * u + math.sin(2 * math.pi * k / segs) * w)) for k in range(segs)])
    for i in range(n):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(segs):
            bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    g().add_bm(bm, mat)


def ring(c, r, axis_y=True, n=40):
    c = Vector(c)
    return [c + Vector((r * math.cos(2 * math.pi * k / n), 0, r * math.sin(2 * math.pi * k / n))) for k in range(n)]


DRILL = Vector((0, -0.26, Z0 + 0.11))
PROPS = {"L": Vector((-0.15, 0.33, Z0 + 0.12)), "R": Vector((0.15, 0.33, Z0 + 0.12))}


def blue_kit():
    """A drill ram under the eye, plus a water kit: webbed paddle feet, twin ducted props, pontoons, snorkel, porthole."""
    at("Body")
    disc(DRILL, 0.12, 0.06, (0, 1, 0), "paint")
    disc(DRILL + Vector((0, -0.04, 0)), 0.105, 0.03, (0, 1, 0), "haz_y")
    at("Drill")
    base = DRILL + Vector((0, -0.055, 0))
    rod(base, base + Vector((0, -0.3, 0)), 0.1, "steel", r2=0.004, segs=24)
    for i in range(40):                     # flute: a helix of ridges round the cone
        t = i / 40
        a = 2 * math.pi * 3 * t
        rr = 0.1 * (1 - t) + 0.004
        sphere(base + Vector((rr * math.cos(a), -0.3 * t, rr * math.sin(a))), 0.016 * (1 - t) + 0.004, "steel", segs=10)
    for name in LEGS:                       # webbed paddle feet: a fan blade facing fore and aft
        _, knee, foot = leg_points(name)
        at(f"Leg{name}_Knee")
        lo, hi = knee.lerp(foot, 0.4), knee.lerp(foot, 0.92)
        for off in (-0.12, -0.04, 0.04, 0.12):
            rod(lo, hi + Vector((0, off, 0)), 0.008, "gun")
        bm = bmesh.new()
        bm.faces.new([bm.verts.new(v) for v in (lo, hi + Vector((0, -0.125, 0)), hi + Vector((0, 0.125, 0)))])
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.008)
        g().add_bm(bm, "web")
    for side, c in PROPS.items():           # twin ducted propellers at the back
        sx = -1 if side == "L" else 1
        at("Body")
        tube(ring(c, 0.095), 0.022, "paint")
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            rod(c + Vector((0, -0.02, 0)), c + Vector((0.09 * math.cos(a), -0.02, 0.09 * math.sin(a))), 0.006, "gun")
        rod(c + Vector((-sx * 0.05, -0.04, 0.06)), Vector((sx * 0.1, 0.2, Z0 + 0.16)), 0.018, "gun")
        at(f"Prop{side}")
        rod(c + Vector((0, -0.09, 0)), c + Vector((0, 0.04, 0)), 0.025, "steel", r2=0.012)
        for k in range(3):
            a = 2 * math.pi * k / 3 + 0.3
            box((0.075, 0.008, 0.035), c + Vector((0, 0.01, 0)), "copper", bevel=0,
                rot=Matrix.Rotation(-a, 4, "Y") @ Matrix.Rotation(0.5, 4, "X") @ Matrix.Translation((0.045, 0, 0)))
    at("Body")
    for sx in (-1, 1):                      # side pontoons: white floats with blue bands
        a_, b_ = Vector((sx * 0.3, -0.17, Z0 + 0.25)), Vector((sx * 0.3, 0.17, Z0 + 0.25))
        rod(a_, b_, 0.055, "white", segs=24)
        sphere(a_, 0.055, "white", scale=(1, 1.6, 1))
        sphere(b_, 0.055, "white", scale=(1, 1.2, 1))
        for y in (-0.08, 0.08):
            rod(a_ + Vector((0, y + 0.17 - 0.012, 0)), a_ + Vector((0, y + 0.17 + 0.012, 0)), 0.058, "paint", segs=24)
    sn = [Vector((0.09, 0.0, Z0 + 0.22)), Vector((0.09, 0.0, Z0 + 0.62)), Vector((0.09, -0.04, Z0 + 0.7)), Vector((0.09, -0.1, Z0 + 0.72))]
    for i in range(3):                      # snorkel behind the eye
        rod(sn[i], sn[i + 1], 0.022, "gun" if i else "paint")
    sphere(sn[1], 0.022, "gun")
    rod(sn[3], sn[3] + Vector((0, -0.05, 0)), 0.03, "copper", r2=0.04)
    at("EyeHead")                           # a brass porthole ring round the lens, like a diving helmet
    tube(ring((0, -0.198, EYE_Z), 0.075), 0.012, "copper")
    for k in range(8):
        a = 2 * math.pi * k / 8
        sphere((0.075 * math.cos(a), -0.21, EYE_Z + 0.075 * math.sin(a)), 0.01, "steel", segs=8)


HAMMER = Vector((0, 0, Z0))


def yellow_kit():
    """A heavy piston hammer under the belly and armoured hydraulic rams on the shell."""
    at("Body")
    rod((0, 0, Z0 + 0.01), (0, 0, Z0 - 0.08), 0.075, "gun")
    at("Hammer")
    rod((0, 0, Z0), (0, 0, 0.12), 0.05, "steel")
    disc((0, 0, 0.08), 0.17, 0.08, (0, 0, 1), "gun", segs=6)
    for i in range(12):
        a = 2 * math.pi * i / 12
        box((0.05, 0.02, 0.06), (0.165 * math.cos(a), 0.165 * math.sin(a), 0.08), "haz_y" if i % 2 else "haz_k",
            bevel=0.002, rot=Matrix.Rotation(a + math.pi / 2, 4, "Z"))
    at("Body")
    for sx in (-1, 1):
        a = Vector((sx * 0.14, 0.08, Z0 + 0.24))
        b = Vector((sx * 0.14, -0.1, Z0 + 0.3))
        rod(a, a.lerp(b, 0.6), 0.035, "gun")
        rod(a.lerp(b, 0.5), b, 0.018, "steel")
        sphere(b, 0.03, "paint")
    box((0.36, 0.22, 0.035), (0, 0.03, Z0 + 0.27), "paint", bevel=0.01)
    for i in range(4):
        box((0.05, 0.012, 0.03), (-0.12 + i * 0.08, -0.08, Z0 + 0.275), "haz_k", bevel=0.002, rot=Matrix.Rotation(0.5, 4, "Z"))


TANK = Vector((0, 0.06, Z0 + 0.38))
NEEDLE = Vector((-0.266, 0.06, Z0 + 0.4))


def red_kit():
    """A pressurised tank on the back, a pressure gauge and a warning lamp that lights up with the eye."""
    at("Body")
    for sx in (-1, 1):
        box((0.05, 0.2, 0.12), (sx * 0.14, 0.06, Z0 + 0.28), "gun")
    pts = [Vector((0.12, -0.05, Z0 + 0.42)), Vector((0.2, -0.12, Z0 + 0.3)), Vector((0.16, -0.2, Z0 + 0.23))]
    for i in range(2):                      # fuse line from the tank to the front
        rod(pts[i], pts[i + 1], 0.012, "copper")
    at("Tank")
    a, b = TANK + Vector((-0.18, 0, 0)), TANK + Vector((0.18, 0, 0))
    rod(a, b, 0.13, "paint", segs=32)
    sphere(a, 0.13, "paint", scale=(0.45, 1, 1), segs=24)
    sphere(b, 0.13, "paint", scale=(0.45, 1, 1), segs=24)
    for i in range(10):
        x = -0.17 + i * 0.038
        rod(TANK + Vector((x, 0, 0)), TANK + Vector((x + 0.036, 0, 0)), 0.134, "haz_y" if i % 2 == 0 else "haz_k", segs=32)
    for sx in (-1, 1):
        rod(TANK + Vector((sx * 0.22, 0, 0)), TANK + Vector((sx * 0.24, 0, 0)), 0.137, "gun", segs=32)
    disc((-0.245, 0.06, Z0 + 0.4), 0.06, 0.03, (1, 0, 0), "steel")
    disc((-0.262, 0.06, Z0 + 0.4), 0.048, 0.006, (1, 0, 0), "white")
    disc((0, 0.06, Z0 + 0.52), 0.05, 0.03, (0, 0, 1), "gun")
    sphere((0, 0.06, Z0 + 0.56), 0.042, "glow")
    for k in range(4):
        a_ = math.pi / 4 + k * math.pi / 2
        q = Vector((0.05 * math.cos(a_), 0.06 + 0.05 * math.sin(a_), Z0 + 0.535))
        rod(q, Vector((0, 0.06, Z0 + 0.615)) + (q - Vector((0, 0.06, Z0 + 0.535))) * 0.2, 0.006, "steel")
    at("GaugeNeedle")
    box((0.004, 0.006, 0.04), NEEDLE + Vector((0, 0.0, 0.016)), "needle", bevel=0)


SACK = Vector((0, 0.08, Z0 + 0.24))


def seed_kit():
    """A burlap seed sack on its back, cinched with rope, leaking seeds out of a hole."""
    at("Body")
    for sx in (-1, 1):
        rod((sx * 0.17, 0.0, Z0 + 0.24), (sx * 0.12, 0.12, Z0 + 0.52), 0.012, "rope")
    at("Sack")
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=16, radius=0.17)
    for v in bm.verts:
        v.co.z *= 1.15
        v.co.x *= 1.05
        v.co += v.co.normalized() * 0.012 * math.sin(v.co.x * 41) * math.cos(v.co.y * 37 + v.co.z * 23)
        if v.co.z < -0.05:                  # it sags at the bottom
            v.co.x *= 1.1
            v.co.y *= 1.1
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0.08, Z0 + 0.39))
    g().add_bm(bm, "burlap")
    rod((0, 0.08, Z0 + 0.56), (0, 0.1, Z0 + 0.64), 0.045, "burlap", r2=0.07)
    rod((0, 0.085, Z0 + 0.585), (0, 0.088, Z0 + 0.605), 0.052, "rope")
    sphere((0, 0.24, Z0 + 0.33), 0.03, "seed", scale=(1, 0.4, 1))
    for x, y, z in [(0.01, 0.27, Z0 + 0.27), (-0.02, 0.29, Z0 + 0.18)]:
        sphere((x, y, z), 0.016, "seed", scale=(1, 0.6, 0.7), segs=12)
    for sx in (-1, 1):
        box((0.012, 0.08, 0.07), (sx * 0.175, 0.08, Z0 + 0.4), "rope", rot=Matrix.Rotation(sx * 0.2, 4, "Z"))


SIGNATURES = {"green": green_tail, "blue": blue_kit, "yellow": yellow_kit, "red": red_kit, "seed": seed_kit}


# =====================================================================================================
# Rig: one empty per node at its pivot; meshes under them
# =====================================================================================================
def pivots():
    """Each rig node's parent and pivot (model space, before the turn to -Z)."""
    p = {"Body": (None, Vector((0, 0, Z0 + 0.1)))}
    p["LampGlow"] = ("Body", Vector((0, -0.244, Z0 + 0.075)))
    p["Stalk"] = ("Body", Vector((0, -0.08, Z0 + 0.22)))
    p["EyeHead"] = ("Stalk", Vector((0, -0.13, EYE_Z)))
    p["EyeLens"] = ("EyeHead", Vector((0, -0.196, EYE_Z)))
    for sx, side in ((-1, "L"), (1, "R")):
        sh, el, wr, fwd, palm_end = claw_points(sx)
        p[f"Claw{side}_Shoulder"] = ("Body", sh)
        p[f"Claw{side}_Elbow"] = (f"Claw{side}_Shoulder", el)
        p[f"Claw{side}_Wrist"] = (f"Claw{side}_Elbow", wr)
        p[f"Claw{side}_Jaw"] = (f"Claw{side}_Wrist", palm_end + Vector((0, 0, 0.03)))
    for name in LEGS:
        hip, knee, foot = leg_points(name)
        p[f"Leg{name}_Hip"] = ("Body", hip)
        p[f"Leg{name}_Knee"] = (f"Leg{name}_Hip", knee)
        p[f"Leg{name}_Foot"] = (f"Leg{name}_Knee", foot)
    v = VARIANT[0]
    if v == "green":
        for i, q in enumerate(tail_points()):
            p[f"Tail{i}"] = ("Body" if i == 0 else f"Tail{i - 1}", q)
    elif v == "blue":
        p["Drill"] = ("Body", DRILL)
        for side, c in PROPS.items():
            p[f"Prop{side}"] = ("Body", c)
    elif v == "yellow":
        p["Hammer"] = ("Body", HAMMER)
    elif v == "red":
        p["Tank"] = ("Body", TANK)
        p["GaugeNeedle"] = ("Tank", NEEDLE)
    elif v == "seed":
        p["Sack"] = ("Body", SACK)
    return p


def materials():
    """Makers of every material, by name; only the ones a variant's pieces use are made."""
    paint = VARIANTS[VARIANT[0]]["paint"]
    return {
        "paint": lambda: lt.textured("scuttler_paint", battle_paint(71, paint)),
        "gun": lambda: lt.textured("scuttler_gunmetal", lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)),
        "steel": lambda: lt.textured("scuttler_steel", lt.metal_surface(97, "#8d9096", 0.32, 0.9, wear=0.7, edge_light=0.2)),
        "rubber": lambda: lt.textured("scuttler_rubber", lt.rubber_surface(101), normal_strength=2.0),
        "hazard": lambda: lt.textured("scuttler_hazard", hazard_surface(113)),
        "bezel": lambda: lt.textured("scuttler_bezel", lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)),
        "glow": glow_material,
        "haz_y": lambda: lt.textured("scuttler_hazard_yellow", battle_paint(131, "#e8b820")),
        "haz_k": lambda: lt.textured("scuttler_hazard_black", battle_paint(137, "#191a1c")),
        "white": lambda: lt.textured("scuttler_white", battle_paint(139, "#d8d6cc")),
        "copper": lambda: lt.textured("scuttler_copper", lt.metal_surface(149, "#b06a3c", 0.3, 0.95, wear=0.5, edge_light=0.25)),
        "web": lambda: lt.textured("scuttler_web", lt.rubber_surface(151), normal_strength=1.5),
        "needle": lambda: lt.textured("scuttler_needle", battle_paint(157, "#c0201a")),
        "burlap": lambda: lt.textured("scuttler_burlap", cloth_surface(163, "#76562f")),
        "rope": lambda: lt.textured("scuttler_rope", cloth_surface(167, "#6b5434")),
        "seed": lambda: lt.textured("scuttler_seed", cloth_surface(173, "#5a3d1f")),
    }


def cloth_surface(seed, color):
    """Woven, dusty and stained: the sack, its rope and the seeds."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    weave = 0.5 + 0.25 * np.sin(xx * 2 * np.pi / 8) * np.sign(np.sin(yy * 2 * np.pi / 16)) + 0.25 * np.sin(yy * 2 * np.pi / 8)
    stain = np.clip(lt.fbm(seed, 3.0) * 1.5, 0, 1)
    albedo = lt.srgb(color)[None, None, :] * (0.8 + 0.3 * weave[..., None]) * (1 - 0.3 * stain[..., None])
    return np.clip(albedo, 0, 1), 0.6 * weave + 0.1 * lt.fnoise(seed + 1, 60.0), 0.9 - 0.05 * weave, np.zeros((size, size))


def battle_paint(seed, color):
    """The approved concept's battle wear: paint chipped through to rust blooms and dark steel, bright scratches,
    grime. Coarser and heavier than the lift's paint, so it reads at bot size."""
    size = lt.SIZE
    steel, h_s, r_s, m_s = lt.metal_surface(seed + 100, "#3a3d42", 0.45, 0.8, wear=0.6)
    rust_tone = np.clip(lt.fbm(seed + 5, 10.0) * 0.5 + 0.5, 0, 1)[..., None]
    rust = lt.srgb("#7a3a18")[None, None, :] * (1 - rust_tone) + lt.srgb("#b8602a")[None, None, :] * rust_tone
    chips = lt.fbm(seed, 4.0) + 0.45 * lt.fnoise(seed + 1, 40.0)
    chip = np.clip((chips - 0.2) * 9, 0, 1)                       # paint gone
    rusty = np.clip((chips - 0.32) * 7, 0, 1)                      # the middle of a chip has rusted
    sc = np.clip(lt.blur(lt.scratches(seed + 2, 420, length=(10, 90), bias=0.2, width=2), 0) * 1.4, 0, 1)
    dirt = np.clip(lt.fbm(seed + 3, 2.5) * 1.6, 0, 1)
    paint = lt.srgb(color)[None, None, :] * (1 + 0.08 * lt.fbm(seed + 4, 8.0)[..., None]) * (1 - 0.35 * dirt[..., None])
    under = steel * (1 - rusty[..., None]) + rust * rusty[..., None]
    albedo = paint * (1 - chip[..., None]) + under * chip[..., None]
    albedo = albedo + 0.55 * sc[..., None] * (lt.srgb("#b9bcc0")[None, None, :] - albedo)
    height = 0.7 * (1 - chip) - 0.5 * sc + 0.2 * rusty * lt.fnoise(seed + 6, 90.0) + 0.1 * h_s
    rough = (0.5 + 0.15 * dirt) * (1 - chip) + (r_s * (1 - rusty) + 0.85 * rusty) * chip
    rough = rough * (1 - sc) + 0.3 * sc
    metal = (m_s * (1 - rusty)) * chip + 0.9 * sc * (1 - chip)
    return albedo, height, rough, np.clip(metal, 0, 1) + np.zeros((size, size))


def hazard_surface(seed):
    """Diagonal yellow and black stripes, worn (the lift's hazard is orange)."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    stripe = (((xx + yy) // (size // 8)) % 2).astype(float)
    a_y, h_y, r_y, m_y = lt.paint_surface(seed, "#e8b820", "#4a4e54")
    a_b, h_b, r_b, m_b = lt.paint_surface(seed + 7, "#191a1c", "#4a4e54")
    s = stripe[..., None]
    return a_y * s + a_b * (1 - s), h_y * stripe + h_b * (1 - stripe), r_y * stripe + r_b * (1 - stripe), m_y * stripe + m_b * (1 - stripe)


def glow_material():
    """The eye and lamps: the game sets their colour (green calm, red engaged); the file holds them green."""
    mat = bpy.data.materials.new("scuttler_glow")
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.05, 0.6, 0.08, 1)
    b.inputs["Roughness"].default_value = 0.15
    b.inputs["Emission Color"].default_value = (0.1, 1.0, 0.15, 1)
    b.inputs["Emission Strength"].default_value = 3.0
    return mat


def build():
    """Builds the rig; returns (root, nodes by name)."""
    build_geometry()
    makers = materials()
    order = list(makers)
    mats = {}
    root = bpy.data.objects.new("Scuttler", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler = (0, 0, math.pi)          # built facing -Y; bots face -Z in glTF
    root.scale = (VARIANTS[VARIANT[0]]["scale"],) * 3
    nodes = {}
    for name, (parent, pivot) in pivots().items():
        ob = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(ob)
        ob.rotation_mode = "QUATERNION"
        ob.parent = nodes[parent] if parent else root
        ob.location = pivot - (pivots()[parent][1] if parent else Vector())
        ob["pivot"] = tuple(pivot)
        nodes[name] = ob
        geo = GEO.get(name)
        if geo is None:
            continue
        me = bpy.data.meshes.new(f"{name}_mesh")
        used = sorted(set(geo.m), key=order.index)
        me.from_pydata([tuple(Vector(v) - pivot) for v in geo.v], [], geo.f)
        for m in used:
            if m not in mats:
                mats[m] = makers[m]()
            me.materials.append(mats[m])
        me.polygons.foreach_set("material_index", [used.index(m) for m in geo.m])
        me.validate()
        for poly in me.polygons:
            poly.use_smooth = True
        lt.box_uvs(me, [None] * len(me.polygons))
        mesh_ob = bpy.data.objects.new(f"{name}_mesh" if name not in ("EyeLens", "LampGlow") else f"{name}Mesh", me)
        bpy.context.scene.collection.objects.link(mesh_ob)
        mesh_ob.parent = ob
        # flat-shade the hard edges, smooth the round ones
        mod = mesh_ob.modifiers.new("smooth", "SMOOTH_BY_ANGLE") if hasattr(bpy.types, "SmoothByAngleModifier") else None
        if mod is None:
            me.set_sharp_from_angle(angle=math.radians(40)) if hasattr(me, "set_sharp_from_angle") else None
    bpy.context.view_layer.update()
    return root, nodes


# =====================================================================================================
# Poses
# =====================================================================================================
def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


def leg_axes(name):
    hip, knee, foot = leg_points(name)
    d = Vector((foot.x - hip.x, foot.y - hip.y, 0)).normalized()
    return d, d.cross(UP).normalized()       # out, and the axis a positive turn about lifts the leg


def fwd_sign(name):
    return 1 if name[0] == "L" else -1        # +Z swing moves a left foot forward (-Y)


class Pose:
    """Rig node rotations (quaternions) and offsets, built up per frame."""

    def __init__(self, nodes):
        self.nodes = nodes
        self.rot = {n: Quaternion() for n in nodes}
        self.off = {n: Vector() for n in nodes}

    def turn(self, name, axis, deg):
        self.rot[name] = q_axis(axis, deg) @ self.rot[name]

    def move(self, name, v):
        self.off[name] += Vector(v)

    def leg(self, name, swing=0.0, lift=0.0, bend=0.0):
        _, a = leg_axes(name)
        self.rot[f"Leg{name}_Hip"] = q_axis(UP, swing * fwd_sign(name)) @ q_axis(a, lift) @ self.rot[f"Leg{name}_Hip"]
        self.rot[f"Leg{name}_Knee"] = q_axis(a, bend) @ self.rot[f"Leg{name}_Knee"]

    def claw(self, side, raise_=0.0, inward=0.0, elbow=0.0, jaw=0.0):
        sx = -1 if side == "L" else 1
        sh, el, wr, fwd, _ = claw_points(sx)
        self.turn(f"Claw{side}_Shoulder", (1, 0, 0), -raise_)
        self.turn(f"Claw{side}_Shoulder", UP, -sx * inward)
        arm = (wr - el).normalized()
        self.turn(f"Claw{side}_Elbow", arm.cross(UP), elbow)
        self.turn(f"Claw{side}_Jaw", fwd.cross(UP), jaw)

    def apply(self):
        piv = pivots()
        for n, ob in self.nodes.items():
            parent, pivot = piv[n]
            ob.rotation_quaternion = self.rot[n]
            ob.location = pivot - (piv[parent][1] if parent else Vector()) + self.off[n]


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def env(t, a, b):
    """0 before a, rising to 1 at b (smoothstep)."""
    return smooth((t - a) / (b - a)) if b > a else float(t >= a)


def pulse(t, a, peak, b):
    """0 → 1 at peak → 0 at b."""
    return env(t, a, peak) if t < peak else 1 - env(t, peak, b)


def gait(p, u, swing, lift, bend, bob):
    """Tripod gait at phase u (0..1): group A swings while B stands, then the other way."""
    for name in LEGS:
        ph = (u + (0.0 if name in GROUP_A else 0.5)) % 1.0
        if ph < 0.5:                      # stance: sweep back along the ground
            s = swing * (1 - 4 * ph)
            p.leg(name, s, 0, 0)
        else:                             # swing: lift and bring forward
            k = (ph - 0.5) * 2
            s = -swing + 2 * swing * smooth(k)
            p.leg(name, s, lift * math.sin(math.pi * k), -bend * math.sin(math.pi * k))
    p.move("Body", (0, 0, -bob * math.cos(4 * math.pi * u)))
    p.turn("Body", (0, 1, 0), 2.0 * math.sin(2 * math.pi * u))


def tail_curl(p, base_pitch=0.0, curl=0.0, yaw=0.0):
    if "Tail0" not in p.rot:
        return
    p.turn("Tail0", (0, 0, 1), yaw)
    p.turn("Tail0", (1, 0, 0), base_pitch)
    for i in range(1, 14):
        p.turn(f"Tail{i}", (1, 0, 0), curl)


def gait_speed(swing, length):
    """Ground speed of the gait at speed scale 1: each stance sweeps a foot through 2 * swing about the hip."""
    hip, _, foot = leg_points("L2")
    reach = (foot - hip).xy.length
    return 2 * reach * math.sin(math.radians(swing)) / (length / 2)


GAITS = {"patrol": (0.5, 20.0), "chase": (0.24, 32.0)}

SHARED_CLIPS = {
    "idle": 2.4, "patrol": GAITS["patrol"][0], "chase": GAITS["chase"][0], "alert": 0.8, "lose_interest": 0.7,
    "hit": 0.35, "death": 1.6, "jump": 0.35, "fall": 0.6, "land": 0.35,
}
CLIPS = dict(SHARED_CLIPS)


def use(variant):
    """Builds `variant` from now on: its pieces, rig nodes and clips."""
    VARIANT[0] = variant
    CLIPS.clear()
    CLIPS.update(SHARED_CLIPS)
    CLIPS.update(VARIANTS[variant].get("extra", {}))
    for clips in VARIANTS[variant]["attacks"].values():
        for clip in clips:
            CLIPS[clip] = ATTACK_CLIPS[clip]


def strike_early(t, strike=0.45, at=0.18):
    """Re-times a 1 s attack so its strike (at `strike` s as written) lands at `at` s: the host deals the damage when the
    attack starts, so the wind-up has to be short and the follow-through long."""
    return t * strike / at if t < at else strike + (t - at) * (1 - strike) / (1 - at)


def pose(p, clip, t, length):
    if clip in ("tail_slam", "tail_sweep"):
        t = strike_early(t)
    u = t / length
    w = 2 * math.pi * u
    if clip == "idle":
        p.move("Body", (0, 0, 0.008 * math.sin(w)))
        p.turn("Stalk", UP, 28 * math.sin(w))
        p.turn("EyeHead", (1, 0, 0), 6 * math.sin(2 * w))
        for side in "LR":
            p.claw(side, raise_=6 + 3 * math.sin(w + (0 if side == "L" else 1)), jaw=6 + 6 * max(0, math.sin(3 * w)))
        tail_curl(p, 3 * math.sin(w), 1.0 * math.sin(w + 1), 6 * math.sin(w * 0.5 * 2))
        k = pulse(u, 0.45, 0.55, 0.65)
        p.leg("R1", 6 * k, 18 * k, -10 * k)
    elif clip in GAITS:
        swing = GAITS[clip][1]
        chase = clip == "chase"
        gait(p, u, swing, 30 if chase else 24, 18, 0.014 if chase else 0.009)
        p.move("Body", (0, 0, -0.05 if chase else 0))
        p.turn("Body", (1, 0, 0), -4 if chase else 0)
        for side in "LR":
            sgn = 1 if side == "L" else -1
            if chase:
                p.claw(side, raise_=24 + 4 * math.sin(2 * w), inward=-6, elbow=10, jaw=12 + 14 * max(0, math.sin(2 * w + sgn)))
            else:
                p.claw(side, raise_=4 + 2 * math.sin(w), jaw=4 + 2 * math.sin(w))
        p.turn("Stalk", UP, 0 if chase else 12 * math.sin(w))
        tail_curl(p, -6 if chase else 2 * math.sin(w), 2 if chase else 0.5 * math.sin(w), 4 * math.sin(w))
    elif clip == "alert":
        k = env(t, 0.0, 0.25) - 0.4 * env(t, 0.45, 0.8)
        p.turn("Body", (1, 0, 0), -16 * k)
        p.move("Body", (0, 0.03 * k, 0.06 * k))
        for name in LEGS:
            p.leg(name, 0, (-8 if name[1] == "1" else 6) * k, 6 * k)
        for side in "LR":
            p.claw(side, raise_=38 * k, inward=-14 * k, elbow=18 * k, jaw=34 * k)
        p.turn("EyeHead", (1, 0, 0), 10 * pulse(t, 0.0, 0.12, 0.3))
        p.move("Stalk", (0, 0, 0.03 * k))
        tail_curl(p, -14 * k, 3 * k)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 0.7)
        p.turn("Body", (1, 0, 0), -6 * k)
        for side in "LR":
            p.claw(side, raise_=20 * k, jaw=10 * k)
        p.turn("Stalk", UP, 25 * math.sin(math.pi * u))
        tail_curl(p, -6 * k, 1 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.06, 0.35)
        p.move("Body", (0, 0.05 * k, 0.02 * k))
        p.turn("Body", (1, 0, 0), -9 * k)
        p.turn("Body", (0, 1, 0), 5 * k)
        p.turn("Stalk", (1, 0, 0), 14 * k)
        for side in "LR":
            p.claw(side, raise_=-6 * k, jaw=20 * k)
        tail_curl(p, 10 * k, -2 * k)
    elif clip == "death" and VARIANT[0] == "red":
        burst(p, t)
    elif clip == "death":
        drop = env(t, 0.1, 0.5)
        flip = env(t, 0.55, 1.1)
        curl = env(t, 0.9, 1.5)
        p.move("Body", (0, 0, -0.26 * drop + 0.16 * flip))
        p.turn("Body", (0, 1, 0), 180 * flip)
        p.turn("Body", (1, 0, 0), 8 * pulse(t, 0.0, 0.1, 0.3))
        for name in LEGS:
            p.leg(name, 0, 34 * drop + 10 * curl, -40 * curl - 10 * drop)
        p.turn("Stalk", (1, 0, 0), 40 * drop)
        for side in "LR":
            p.claw(side, raise_=-20 * drop + 30 * curl, inward=20 * curl, elbow=-30 * curl, jaw=26 * pulse(t, 0, 0.2, 0.6))
        tail_curl(p, 30 * drop, -6 * drop)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.15, 0.3)
        push = env(t, 0.15, 0.35)
        p.move("Body", (0, 0, -0.08 * crouch + 0.04 * push))
        for name in LEGS:
            p.leg(name, 0, 10 * crouch - 18 * push, -8 * crouch + 10 * push)
        for side in "LR":
            p.claw(side, raise_=10 + 20 * push, jaw=10 * push)
        tail_curl(p, -10 * push)
    elif clip == "fall":
        for i, name in enumerate(LEGS):
            p.leg(name, 8 * math.sin(w * 2 + i), -18 + 6 * math.sin(w * 2 + i * 1.7), 10)
        for side in "LR":
            p.claw(side, raise_=30 + 6 * math.sin(w * 2), jaw=20 + 10 * math.sin(w * 3))
        tail_curl(p, -12 + 4 * math.sin(w), 2)
    elif clip == "land":
        k = pulse(t, 0.0, 0.08, 0.35)
        p.move("Body", (0, 0, -0.09 * k))
        for name in LEGS:
            p.leg(name, 0, 14 * k, -10 * k)
        for side in "LR":
            p.claw(side, raise_=-8 * k, jaw=8 * k)
        tail_curl(p, 12 * k)
    elif clip == "tail_slam":
        # rears and coils, then whips over the top and smashes the weight down in front
        wind = env(t, 0.0, 0.38) * (1 - env(t, 0.38, 0.5))
        strike = env(t, 0.38, 0.52) * (1 - env(t, 0.65, 1.0))
        p.turn("Body", (1, 0, 0), -6 * wind + 10 * strike)
        p.move("Body", (0, 0.02 * wind, -0.03 * strike))
        tail_curl(p, -30 * wind + 165 * strike, 6 * wind - 9 * strike)
        p.turn("Stalk", (0, 1, 0), 30 * strike)       # the eye ducks aside as the tail goes over
        for side in "LR":
            p.claw(side, raise_=18 * wind - 10 * strike, inward=-10 * wind - 12 * strike, jaw=24 * wind)
        for name in LEGS:
            p.leg(name, 0, (-6 if name[1] == "3" else 0) * wind + 6 * strike, 0)
    elif clip == "tail_sweep":
        # straightens the tail out behind, winds it to the right, then swings it round the left side and back
        out = env(t, 0.0, 0.25) * (1 - env(t, 0.7, 1.0))
        yaw = (-60 * env(t, 0.0, 0.3) + 160 * env(t, 0.3, 0.6)) * (1 - env(t, 0.7, 1.0))
        tail_curl(p, 6 * out * math.sin(math.pi * env(t, 0.3, 0.6)), -16 * out, yaw)
        p.turn("Body", (0, 0, 1), 0.3 * yaw)
        p.turn("Body", (0, 1, 0), 0.05 * yaw * out)
        p.move("Body", (0, 0, -0.03 * out))
        for side in "LR":
            p.claw(side, raise_=10 * out, inward=-10 * out, jaw=16 * out)
    elif clip == "claw_combo":
        jab_l = pulse(t, 0.0, 0.18, 0.4)
        jab_r = pulse(t, 0.3, 0.48, 0.7)
        crunch = pulse(t, 0.62, 0.78, 1.0)
        for side, jab in (("L", jab_l), ("R", jab_r)):
            p.claw(side, raise_=18 - 12 * jab + 10 * crunch, inward=18 * jab + 22 * crunch, elbow=-28 * jab - 20 * crunch,
                   jaw=34 * (pulse(t, 0.0, 0.1, 0.2) if side == "L" else pulse(t, 0.3, 0.4, 0.5)) + 30 * pulse(t, 0.6, 0.68, 0.8))
        p.turn("Body", (1, 0, 0), 5 * (jab_l + jab_r) + 8 * crunch)
        p.move("Body", (0, -0.03 * (jab_l + jab_r + crunch), -0.02 * crunch))
        p.turn("Body", (0, 0, 1), 6 * jab_l - 6 * jab_r)
        tail_curl(p, -8 * crunch)
    elif clip == "drill_ram":
        # digs in and lunges the spinning drill forward, then backs off
        lunge = env(t, 0.0, 0.15) * (1 - env(t, 0.45, 0.9))
        p.move("Body", (0, -0.14 * lunge, -0.04 * lunge))
        p.turn("Body", (1, 0, 0), 8 * lunge)
        for name in LEGS:
            p.leg(name, (-12 if name[1] == "3" else 6) * lunge, -6 * lunge, 4 * lunge)
        for side in "LR":
            p.claw(side, raise_=30 * lunge, inward=-20 * lunge, jaw=20 * lunge)
    elif clip == "stomp":
        # rears up tall with its claws high, then crashes down: legs splay, the hammer fires into the ground, the whole
        # body bounces and shudders with the shock and the claws and eye whip about
        rise = env(t, 0.0, 0.12) * (1 - env(t, 0.12, 0.19))
        slam = env(t, 0.12, 0.19) * (1 - env(t, 0.45, 1.15))
        bounce = pulse(t, 0.19, 0.28, 0.42)
        shudder = math.sin((t - 0.19) * 75) * max(0.0, 1 - (t - 0.19) / 0.6) * float(t > 0.19)
        p.move("Body", (0, 0, 0.16 * rise - 0.07 * slam + 0.05 * bounce + 0.012 * shudder))
        p.turn("Body", (1, 0, 0), -12 * rise + 5 * slam + 2.5 * shudder)
        p.turn("Body", (0, 1, 0), 2 * shudder)
        for name in LEGS:
            p.leg(name, 0, -22 * rise + 16 * slam, 14 * rise - 14 * slam)
        p.move("Hammer", (0, 0, 0.12 * rise + 0.055 * slam - 0.045 * bounce))
        p.turn("Stalk", (1, 0, 0), -14 * rise + 26 * slam + 8 * shudder)
        p.turn("EyeHead", (1, 0, 0), 10 * shudder)
        for side in "LR":
            p.claw(side, raise_=55 * rise - 18 * slam + 6 * shudder, inward=-26 * rise + 10 * slam, elbow=-20 * rise,
                   jaw=34 * rise + 20 * bounce)
    elif clip == "detonate":
        # the needle pins, it rears and shakes as the tank lets go
        k = env(t, 0.0, 0.3)
        p.turn("Body", (1, 0, 0), -10 * k + 4 * math.sin(t * 90) * k)
        p.turn("Body", (0, 1, 0), 5 * math.sin(t * 70) * k)
        p.move("Body", (0, 0, 0.04 * k))
        for side in "LR":
            p.claw(side, raise_=40 * k, inward=-20 * k, jaw=40 * k)
    elif clip in ("swim", "swim_idle"):
        # breaststroke: every leg reaches forward folded in, opens wide, sweeps back hard together and trails behind
        # through the glide; the body surges forward and rises on each stroke (swim_idle is a slow, small version)
        k = 1.0 if clip == "swim" else 0.45
        for name in LEGS:
            lag = {"1": -0.04, "2": 0.0, "3": 0.04}[name[1]]
            sw, lf, bd = stroke((u - lag) % 1.0)
            p.leg(name, k * sw, 40 + k * (lf - 40), k * bd)
        power = pulse(u, 0.45, 0.6, 0.95)
        reach = pulse(u, 0.05, 0.25, 0.45)
        p.move("Body", (0, -0.05 * k * power, 0.03 * k * power - 0.015 * k * reach))
        p.turn("Body", (1, 0, 0), k * (-5 * power + 4 * reach))
        for side in "LR":
            p.claw(side, raise_=8 + 10 * k * reach - 6 * k * power, inward=22 + 8 * k * power, elbow=-20 - 10 * k * power,
                   jaw=6 + 18 * k * reach)
        p.turn("Stalk", (1, 0, 0), -14 * k - 10 * k * power)
        p.turn("EyeHead", (1, 0, 0), 14 * k)
        if clip == "swim_idle":
            p.turn("Stalk", UP, 25 * math.sin(w))
    elif clip == "claw_snap":
        # one quick lunge and snap of both claws
        snap = pulse(t, 0.0, 0.12, 0.4)
        p.move("Body", (0, -0.04 * snap, -0.01 * snap))
        p.turn("Body", (1, 0, 0), 6 * snap)
        for side in "LR":
            p.claw(side, raise_=16 - 10 * snap, inward=20 * snap, elbow=-24 * snap, jaw=36 * pulse(t, 0.0, 0.06, 0.16))
    else:
        raise ValueError(clip)
    gear(p, clip, t, length)


STROKE = [  # breaststroke keys: phase, then a leg's swing, lift and knee bend (degrees); legs stay out flat like oars
    (0.00, -40, 48, -2),     # trailing back through the glide
    (0.25, 34, 50, -24),     # reach: gathered forward, knees bent
    (0.45, 44, 66, -6),      # catch: opened out wide
    (0.68, -46, 54, -2),     # power: swept back hard
    (1.00, -40, 48, -2),
]


def stroke(u):
    for (u0, *a), (u1, *b) in zip(STROKE, STROKE[1:]):
        if u0 <= u <= u1:
            t = smooth((u - u0) / (u1 - u0))
            return tuple(x + (y - x) * t for x, y in zip(a, b))
    return tuple(STROKE[0][1:])


def spin(p, node, axis, turns, u):
    """`turns` whole turns over the clip (so loops still loop)."""
    p.turn(node, axis, 360 * turns * u)


def gear(p, clip, t, length):
    """Each variant's moving signature pieces, on top of the clip."""
    u = t / length
    w = 2 * math.pi * u
    v = VARIANT[0]
    if v == "blue":
        # the drill turns while it walks and screams round in the ram; the props always turn
        drill = {"patrol": 1, "chase": 2, "drill_ram": 8, "idle": 1, "alert": 1, "swim": 2, "swim_idle": 1}.get(clip, 0)
        spin(p, "Drill", (0, 1, 0), drill, u)
        props = {"idle": 2, "patrol": 1, "chase": 1, "drill_ram": 4, "death": 1, "swim": 8, "swim_idle": 6}.get(clip, 1)
        spin(p, "PropL", (0, 1, 0), props, u)
        spin(p, "PropR", (0, 1, 0), -props, u)
    elif v == "red":
        # the gauge needle shivers while calm, swings while chasing and pins when it arms
        swing = {"chase": 40 * math.sin(2 * w), "alert": 60 * env(t, 0.0, 0.3), "detonate": 100 * env(t, 0.0, 0.2),
                 "idle": 6 * math.sin(3 * w), "patrol": 10 * math.sin(w), "hit": 30 * pulse(t, 0, 0.05, 0.35)}.get(clip, 0)
        p.turn("GaugeNeedle", (1, 0, 0), -swing)
    elif v == "seed":
        # the sack lags behind the body: it sways with each step and flops when hit or killed
        sway = {"patrol": (5 * math.sin(2 * w - 0.8), 3 * math.sin(w)), "chase": (8 * math.sin(2 * w - 0.8), 5 * math.sin(w)),
                "idle": (2 * math.sin(w), 0), "hit": (14 * pulse(t, 0.0, 0.08, 0.35), 0),
                "claw_snap": (-10 * pulse(t, 0.0, 0.12, 0.4), 0), "death": (50 * env(t, 0.2, 0.8), 15 * env(t, 0.2, 0.8)),
                "alert": (8 * pulse(t, 0.0, 0.2, 0.6), 0), "land": (12 * pulse(t, 0.0, 0.1, 0.35), 0)}.get(clip, (0, 0))
        p.turn("Sack", (1, 0, 0), sway[0])
        p.turn("Sack", (0, 1, 0), sway[1])
    elif v == "yellow" and clip in ("patrol", "chase"):
        p.move("Hammer", (0, 0, 0.006 * math.sin(2 * w)))      # the hammer rattles in its sleeve


def burst(p, t):
    """The Red Scuttler's death: it swells and shakes, then the tank blows and its legs, claws and eye fly off."""
    shake = env(t, 0.0, 0.2) * (1 - env(t, 0.25, 0.3))
    blow = env(t, 0.25, 0.6)
    fall = env(t, 0.45, 1.2)
    p.turn("Body", (0, 1, 0), 6 * math.sin(t * 80) * shake + 25 * blow)
    p.move("Body", (0, 0, 0.05 * shake + 0.12 * blow - 0.3 * fall))
    p.move("Tank", (0, 0.25 * blow, 0.6 * blow - 0.75 * fall))
    p.turn("Tank", (0, 1, 0), 160 * blow)
    for name in LEGS:
        hip, _, foot = leg_points(name)
        out = (foot - hip).normalized()
        p.move(f"Leg{name}_Hip", out * 0.35 * blow + Vector((0, 0, 0.25 * blow - 0.4 * fall)))
        p.leg(name, 0, 40 * blow, -50 * blow)
    for side in "LR":
        sx = -1 if side == "L" else 1
        p.move(f"Claw{side}_Shoulder", (sx * 0.3 * blow, -0.3 * blow, 0.3 * blow - 0.45 * fall))
        p.claw(side, raise_=60 * blow, inward=-40 * blow, elbow=-50 * blow, jaw=40 * blow)
    p.move("Stalk", (0.1 * blow, -0.1 * blow, 0.5 * blow - 0.6 * fall))
    p.turn("Stalk", (1, 0, 0), 120 * blow)


def set_pose(nodes, clip, t, length):
    p = Pose(nodes)
    pose(p, clip, t, length)
    # A hair of motion (a quarter degree) on every node, so no channel holds still through a clip (see bake_clips).
    # It is zero at both ends of a clip, so loops still loop and one-shots end on their authored pose.
    for i, n in enumerate(nodes):
        p.turn(n, (1, 0, 0), 0.25 * math.sin(2 * math.pi * t / length) * (1 if i % 2 else -1))
    p.apply()


# =====================================================================================================
# Clips
# =====================================================================================================
def push_track(ob, action, name):
    track = ob.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 0, action)
    strip.action_frame_start, strip.action_frame_end = action.frame_range
    ob.animation_data.action = None


def bake_clips(nodes):
    """Keys every rig node every frame of every clip, so a clip never inherits another's pose. Godot's glTF import
    drops a channel that holds one value through a clip and resets it to the node's rest, so a channel a clip holds
    away from rest gets a hair of motion (the check below says if one is missed)."""
    anim = [ob for n, ob in nodes.items() if not n.endswith("_Foot") and n not in ("EyeLens", "LampGlow")]
    for ob in anim:
        ob.animation_data_create()
    rest = Pose(nodes)
    rest.apply()
    held = []
    rest_tf = {ob.name: (tuple(ob.location), tuple(ob.rotation_quaternion)) for ob in anim}
    for clip, length in CLIPS.items():
        acts = {}
        for ob in anim:
            acts[ob] = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = acts[ob]
        frames = int(round(length * FPS))
        seen = {ob.name: [] for ob in anim}
        for f in range(frames + 1):
            set_pose(nodes, clip, f / FPS, length)
            for ob in anim:
                ob.keyframe_insert("rotation_quaternion", frame=f)
                ob.keyframe_insert("location", frame=f)
                seen[ob.name].append((tuple(ob.location), tuple(ob.rotation_quaternion)))
        for ob, act in acts.items():
            lt.linear_keys(act)
            push_track(ob, act, clip)
        for name, values in seen.items():
            for k in (0, 1):
                vals = [v[k] for v in values]
                if all(np.allclose(v, vals[0], atol=1e-6) for v in vals) and not np.allclose(vals[0], rest_tf[name][k], atol=1e-4):
                    held.append(f"{clip}: {name} holds {'location' if k == 0 else 'rotation'} away from rest")
    if held:
        raise SystemExit("\n".join(held))
    rest.apply()


def export(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root, *root.children_recursive]:
        ob.select_set(True)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_optimize_animation_keep_anim_object=True,
        export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=False,
        export_image_format="JPEG", export_image_quality=85, export_extras=False, export_cameras=False, export_lights=False)


def build_game(variants):
    for variant in variants:
        use(variant)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root, nodes = build()
        bake_clips(nodes)
        out = MESHES / f"scuttler_{variant}.glb"
        export(root, out)
        scale = VARIANTS[variant]["scale"]
        speeds = ", ".join(f"{clip} {gait_speed(swing, length) * scale:.2f}" for clip, (length, swing) in GAITS.items())
        print(f"wrote {out.relative_to(REPO)} (clip speeds, m/s: {speeds})")


# =====================================================================================================
# Review renders
# =====================================================================================================
def render_review(out: Path, variant):
    use(variant)
    sys.path.insert(0, str(REPO / "tools" / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    bpy.context.scene.cycles.samples = SAMPLES
    root, nodes = build()
    root.rotation_euler = (0, 0, 0)          # renders keep the build's -Y front
    k = VARIANTS[variant]["scale"]
    c = (0, 0, 0.42 * k)
    shots = []

    def shot(name, clip, t, cam):
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t, CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cam, out / f"{variant}_{name}.png", 900, 800)
        shots.append((name, path))

    tq = rl.camera("tq", (-2.2 * k, -3.0 * k, 1.9 * k), c, ortho=1.9 * k)
    side = rl.camera("side", (4 * k, 0, 0.45 * k), c, ortho=1.9 * k)
    shot("rest", None, 0, tq)
    shot("rest_side", None, 0, side)
    shot("rear", None, 0, rl.camera("rear", (2.4 * k, 2.6 * k, 1.8 * k), c, ortho=1.9 * k))
    shot("patrol", "patrol", 0.12, tq)
    shot("chase", "chase", 0.08, tq)
    shot("alert", "alert", 0.3, tq)
    attack_shots = {
        "green": [("claw_combo", "claw_combo", 0.78, tq), ("tail_slam_wind", "tail_slam", 0.15, side),
                  ("tail_slam", "tail_slam", 0.25, side),
                  ("tail_sweep", "tail_sweep", 0.22, rl.camera("top", (0.01, 0, 5), c, ortho=2.4))],
        "blue": [("drill_ram", "drill_ram", 0.2, tq), ("drill_ram_side", "drill_ram", 0.2, side)],
        "yellow": [("stomp_rise", "stomp", 0.13, side), ("stomp_slam", "stomp", 0.24, side), ("stomp", "stomp", 0.24, tq)],
        "red": [("detonate", "detonate", 0.3, tq), ("burst", "death", 0.45, tq), ("burst_side", "death", 0.6, side)],
        "seed": [("claw_snap", "claw_snap", 0.12, tq), ("sack_sway", "chase", 0.1, side)],
    }
    for args in attack_shots[variant]:
        shot(*args)
    shot("hit", "hit", 0.06, tq)
    if variant != "red":
        shot("death_drop", "death", 0.6, tq)
    shot("death", "death", 1.6, tq)
    shot("fall", "fall", 0.2, side)

    from render_lift import INK, font as F  # noqa: PLC0415
    w, h = 560, int(560 * 800 / 900)
    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (60 + cols * (w + 20) + 40, 150 + rows * (h + 20) + 20), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), f"{VARIANTS[variant]['name'].upper()} (game model and clips)", fill=INK, font=F(44, True))
    d.text((60, 90), "Final game model with worn textures, shown in its rest pose and at key frames of its clips. "
           "The eye shows green; the game turns it red while engaged.", fill=INK, font=F(22))
    for i, (name, path) in enumerate(shots):
        x, y = 60 + (i % cols) * (w + 20), 140 + (i // cols) * (h + 20)
        sheet.paste(Image.open(path).convert("RGB").resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(190, 190, 190), width=2)
        d.text((x + 12, y + 8), name.replace("_", " ").upper(), fill=INK, font=F(20, True))
    sheet.save(out / f"{variant}_review_sheet.png")
    print(f"rendered the review sheet into {out}")


if __name__ == "__main__":
    args = sys.argv[1:]
    chosen = [args[i + 1] for i, a in enumerate(args) if a == "--variant"] or list(VARIANTS)
    if "--render" in args:
        for key in chosen:
            render_review(Path(args[args.index("--render") + 1]), key)
    else:
        build_game(chosen)
