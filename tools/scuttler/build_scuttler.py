"""Build the Green Scuttler (crab chassis concept approved by Chase on 2026-10-19).

The Scuttler family shares one body: a low hexagonal shell on six crab legs, two raised serrated claw arms, and one
eye on a stalk. Each variant changes its paint and adds one signature piece; the Green Scuttler's is a scorpion tail
of cable segments ending in a spiked weight. The paint is worn: chipped to bare steel, scratched and grimy.

What it writes: ``mods/vanilla/meshes/bots/scuttler_green.glb``. Its feet are at the origin and it faces -Z, as bots
do. The nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the shell), ``Stalk`` and ``EyeHead`` (the eye), ``LampGlow`` and ``EyeLens`` (the glowing eye and front
  lamps, which the game colours green while calm and red while engaged);
* ``Claw<L|R>_Shoulder`` / ``_Elbow`` / ``_Wrist`` / ``_Jaw`` (the upper jaw opens);
* ``Leg<L|R><1-3>_Hip`` / ``_Knee`` / ``_Foot`` (1 is the front pair; ``_Foot`` is an empty at the tip);
* ``Tail0`` … ``Tail13`` (the tail, base to weight).

Clips (seconds; the loops loop): ``idle`` 2.4, ``patrol`` 0.5 and ``chase`` 0.24 (gaits; their ground speeds at
speed scale 1 are printed and go in the bot's ``model.clip_speeds``), ``alert`` 0.8, ``lose_interest`` 0.7,
``hit`` 0.35, ``death`` 1.6, ``jump`` 0.35, ``fall`` 0.6, ``land`` 0.35, ``tail_slam`` 1.0, ``tail_sweep`` 1.0 and
``claw_combo`` 1.0 (the whip attack's three moves).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/scuttler/build_scuttler.py                     # writes the .glb
    python tools/scuttler/build_scuttler.py --render OUT_DIR    # review renders, poses and sheet into OUT_DIR

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

OUT = REPO / "mods" / "vanilla" / "meshes" / "bots" / "scuttler_green.glb"
FPS = 30
SAMPLES = int(os.environ.get("SCUTTLER_SAMPLES", "48"))

PAINT = "#5fae3e"           # the bot's colour (scuttler_green.json)
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
        r = 0.036
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

    # ---- Tail: cable segments curled over the back, a spiked weight at the end ----
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
    pts = tail_points()
    for i, q in enumerate(pts):
        p[f"Tail{i}"] = ("Body" if i == 0 else f"Tail{i - 1}", q)
    return p


def materials():
    return {
        "paint": lt.textured("scuttler_paint", battle_paint(71, PAINT)),
        "gun": lt.textured("scuttler_gunmetal", lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)),
        "steel": lt.textured("scuttler_steel", lt.metal_surface(97, "#8d9096", 0.32, 0.9, wear=0.7, edge_light=0.2)),
        "rubber": lt.textured("scuttler_rubber", lt.rubber_surface(101), normal_strength=2.0),
        "hazard": lt.textured("scuttler_hazard", hazard_surface(113)),
        "bezel": lt.textured("scuttler_bezel", lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)),
        "glow": glow_material(),
    }


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
    mats = materials()
    order = list(mats)
    root = bpy.data.objects.new("Scuttler", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler = (0, 0, math.pi)          # built facing -Y; bots face -Z in glTF
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

CLIPS = {
    "idle": 2.4, "patrol": GAITS["patrol"][0], "chase": GAITS["chase"][0], "alert": 0.8, "lose_interest": 0.7,
    "hit": 0.35, "death": 1.6, "jump": 0.35, "fall": 0.6, "land": 0.35,
    "tail_slam": 1.0, "tail_sweep": 1.0, "claw_combo": 1.0,
}


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
    else:
        raise ValueError(clip)


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
        export_image_format="AUTO", export_extras=False, export_cameras=False, export_lights=False)


def build_game():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, nodes = build()
    bake_clips(nodes)
    export(root, OUT)
    for clip, (length, swing) in GAITS.items():
        print(f"{clip}: {gait_speed(swing, length):.2f} m/s at speed scale 1")
    print(f"wrote {OUT.relative_to(REPO)}")


# =====================================================================================================
# Review renders
# =====================================================================================================
def render_review(out: Path):
    sys.path.insert(0, str(REPO / "tools" / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    bpy.context.scene.cycles.samples = SAMPLES
    root, nodes = build()
    root.rotation_euler = (0, 0, 0)          # renders keep the build's -Y front
    c = (0, 0, 0.42)
    shots = []

    def shot(name, clip, t, cam):
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t, CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cam, out / f"{name}.png", 900, 800)
        shots.append((name, path))

    tq = rl.camera("tq", (-2.2, -3.0, 1.9), c, ortho=1.9)
    side = rl.camera("side", (4, 0, 0.45), c, ortho=1.9)
    shot("rest", None, 0, tq)
    shot("rest_side", None, 0, side)
    shot("rear", None, 0, rl.camera("rear", (2.4, 2.6, 1.8), c, ortho=1.9))
    shot("patrol", "patrol", 0.12, tq)
    shot("chase", "chase", 0.08, tq)
    shot("alert", "alert", 0.3, tq)
    shot("claw_combo", "claw_combo", 0.78, tq)
    shot("tail_slam_wind", "tail_slam", 0.15, side)
    shot("tail_slam", "tail_slam", 0.25, side)
    shot("tail_sweep", "tail_sweep", 0.22, rl.camera("top", (0.01, 0, 5), c, ortho=2.4))
    shot("hit", "hit", 0.06, tq)
    shot("death_drop", "death", 0.6, tq)
    shot("death", "death", 1.6, tq)
    shot("fall", "fall", 0.2, side)

    from render_lift import INK, font as F  # noqa: PLC0415
    w, h = 560, int(560 * 800 / 900)
    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (60 + cols * (w + 20) + 40, 150 + rows * (h + 20) + 20), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), "GREEN SCUTTLER (game model and clips)", fill=INK, font=F(44, True))
    d.text((60, 90), "Final game model with worn textures, shown in its rest pose and at key frames of its clips. "
           "The eye shows green; the game turns it red while engaged.", fill=INK, font=F(22))
    for i, (name, path) in enumerate(shots):
        x, y = 60 + (i % cols) * (w + 20), 140 + (i // cols) * (h + 20)
        sheet.paste(Image.open(path).convert("RGB").resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(190, 190, 190), width=2)
        d.text((x + 12, y + 8), name.replace("_", " ").upper(), fill=INK, font=F(20, True))
    sheet.save(out / "review_sheet.png")
    print(f"rendered the review sheet into {out}")


if __name__ == "__main__":
    if "--render" in sys.argv:
        render_review(Path(sys.argv[sys.argv.index("--render") + 1]))
    else:
        build_game()
