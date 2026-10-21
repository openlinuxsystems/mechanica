"""Shared materials, shape helpers and the glTF export for structures (bpy 5.2; see build_structures.py).

Materials have two forms. For review renders (EXPORT False) they are procedural Cycles paint: mottling, cracks,
edge chips, grime rising from the ground, moss on top, scratches and dirt in corners. For the game (EXPORT True)
they are a flat colour times a shared tileable detail image of their finish (textures.py), and export() bakes the
grime and moss into vertex colours, so the look survives glTF.
"""
import math
import random
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix

REPO = Path(__file__).resolve().parents[2]
MESH = REPO / "mods/vanilla/meshes"

EXPORT = False      # build game materials instead of review ones
GAME_GLOW = 4.0     # the game has bloom: brighter glows wash out


def lin(hexs):
    h = hexs.lstrip("#")
    return tuple((int(h[i:i + 2], 16) / 255) ** 2.2 for i in (0, 2, 4)) + (1,)


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras, bpy.data.curves):
        for b in list(coll):
            coll.remove(b)


_mats = {}


def mat(name, color, rough=0.7, metal=0.0, wear=0.0, grime=0.0, emit=None, emit_strength=0.0, moss=0.0,
        noise_scale=6.0, cracks=0.0, scratches=0.0, ao=0.55):
    """Stylised paint: base colour, lighter chipped edges (pointiness), grime rising from the ground, moss on top."""
    key = (name, color, rough, metal, wear, grime, emit, emit_strength, moss, cracks, scratches, ao)
    if key in _mats:
        return _mats[key]
    if EXPORT:
        _mats[key] = m = _game_mat(name, color, rough, metal, grime, emit, emit_strength, moss, noise_scale, cracks)
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    n = nt.nodes
    L = nt.links
    b = n["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    base = n.new("ShaderNodeRGB")
    base.outputs[0].default_value = lin(color)
    col = base.outputs[0]
    # Painted variation: low-frequency mottling so flat colours do not look plastic.
    tex = n.new("ShaderNodeTexNoise")
    tex.inputs["Scale"].default_value = noise_scale
    tex.inputs["Detail"].default_value = 3.0
    mot = n.new("ShaderNodeMix")
    mot.data_type = "RGBA"
    mot.blend_type = "MULTIPLY"
    mot.inputs["Factor"].default_value = 0.22
    L.new(col, mot.inputs[6])
    L.new(tex.outputs["Color"], mot.inputs[7])
    col = mot.outputs[2]
    if cracks > 0:
        vor = n.new("ShaderNodeTexVoronoi")
        vor.feature = "DISTANCE_TO_EDGE"
        vor.inputs["Scale"].default_value = 3.0
        ramp = n.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = (0.12, 0.12, 0.12, 1)
        ramp.color_ramp.elements[1].position = 0.035
        ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
        L.new(vor.outputs["Distance"], ramp.inputs[0])
        cm = n.new("ShaderNodeMix")
        cm.data_type = "RGBA"
        cm.blend_type = "MULTIPLY"
        cm.inputs["Factor"].default_value = cracks
        L.new(col, cm.inputs[6])
        L.new(ramp.outputs[0], cm.inputs[7])
        col = cm.outputs[2]
    if wear > 0:
        geo = n.new("ShaderNodeNewGeometry")
        ramp = n.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.56
        ramp.color_ramp.elements[1].position = 0.62
        L.new(geo.outputs["Pointiness"], ramp.inputs[0])
        nz = n.new("ShaderNodeTexNoise")
        nz.inputs["Scale"].default_value = 18
        mul = n.new("ShaderNodeMath")
        mul.operation = "MULTIPLY"
        L.new(ramp.outputs[0], mul.inputs[0])
        L.new(nz.outputs["Fac"], mul.inputs[1])
        mul2 = n.new("ShaderNodeMath")
        mul2.operation = "MULTIPLY"
        mul2.inputs[1].default_value = wear * 2.2
        L.new(mul.outputs[0], mul2.inputs[0])
        chip = n.new("ShaderNodeMix")
        chip.data_type = "RGBA"
        chip.clamp_factor = True
        L.new(mul2.outputs[0], chip.inputs["Factor"])
        L.new(col, chip.inputs[6])
        chip.inputs[7].default_value = lin("#b9bdc1")
        col = chip.outputs[2]
    if grime > 0:
        geo = n.new("ShaderNodeNewGeometry")
        sep = n.new("ShaderNodeSeparateXYZ")
        L.new(geo.outputs["Position"], sep.inputs[0])
        mr = n.new("ShaderNodeMapRange")
        mr.inputs["From Min"].default_value = 0.0
        mr.inputs["From Max"].default_value = 1.4
        mr.inputs["To Min"].default_value = grime
        mr.inputs["To Max"].default_value = 0.0
        L.new(sep.outputs["Z"], mr.inputs["Value"])
        gm = n.new("ShaderNodeMix")
        gm.data_type = "RGBA"
        gm.clamp_factor = True
        L.new(mr.outputs[0], gm.inputs["Factor"])
        L.new(col, gm.inputs[6])
        gm.inputs[7].default_value = lin("#4a4232")
        col = gm.outputs[2]
    if moss > 0:
        geo = n.new("ShaderNodeNewGeometry")
        sep = n.new("ShaderNodeSeparateXYZ")
        L.new(geo.outputs["Normal"], sep.inputs[0])
        nz = n.new("ShaderNodeTexNoise")
        nz.inputs["Scale"].default_value = 4
        add = n.new("ShaderNodeMath")
        add.operation = "ADD"
        L.new(sep.outputs["Z"], add.inputs[0])
        sub = n.new("ShaderNodeMath")
        sub.operation = "MULTIPLY"
        sub.inputs[1].default_value = 0.5
        L.new(nz.outputs["Fac"], sub.inputs[0])
        L.new(sub.outputs[0], add.inputs[1])
        ramp = n.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.95
        ramp.color_ramp.elements[1].position = 1.02
        L.new(add.outputs[0], ramp.inputs[0])
        mm = n.new("ShaderNodeMix")
        mm.data_type = "RGBA"
        mm.clamp_factor = True
        fac = n.new("ShaderNodeMath")
        fac.operation = "MULTIPLY"
        fac.inputs[1].default_value = moss
        L.new(ramp.outputs[0], fac.inputs[0])
        L.new(fac.outputs[0], mm.inputs["Factor"])
        L.new(col, mm.inputs[6])
        mm.inputs[7].default_value = lin("#6f9a3a")
        col = mm.outputs[2]
    if scratches > 0:
        # fine directional scratches, like the worn metal on the parts
        tcd = n.new("ShaderNodeTexCoord")
        mp = n.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (6, 6, 90)
        L.new(tcd.outputs["Object"], mp.inputs[0])
        sn = n.new("ShaderNodeTexNoise")
        sn.inputs["Scale"].default_value = 6
        sn.inputs["Detail"].default_value = 8
        L.new(mp.outputs[0], sn.inputs["Vector"])
        sr = n.new("ShaderNodeValToRGB")
        sr.color_ramp.elements[0].position = 0.62
        sr.color_ramp.elements[1].position = 0.70
        L.new(sn.outputs["Fac"], sr.inputs[0])
        sm = n.new("ShaderNodeMix")
        sm.data_type = "RGBA"
        sm.blend_type = "SCREEN"
        fac = n.new("ShaderNodeMath")
        fac.operation = "MULTIPLY"
        fac.inputs[1].default_value = scratches
        L.new(sr.outputs[0], fac.inputs[0])
        L.new(fac.outputs[0], sm.inputs["Factor"])
        L.new(col, sm.inputs[6])
        sm.inputs[7].default_value = (0.45, 0.45, 0.46, 1)
        col = sm.outputs[2]
    if ao > 0:
        # dirt collects in corners and seams
        aon = n.new("ShaderNodeAmbientOcclusion")
        aon.inputs["Distance"].default_value = 0.6
        ar = n.new("ShaderNodeMapRange")
        ar.inputs["From Min"].default_value = 0.0
        ar.inputs["From Max"].default_value = 1.0
        ar.inputs["To Min"].default_value = ao
        ar.inputs["To Max"].default_value = 0.0
        L.new(aon.outputs["AO"], ar.inputs["Value"])
        am = n.new("ShaderNodeMix")
        am.data_type = "RGBA"
        am.clamp_factor = True
        L.new(ar.outputs[0], am.inputs["Factor"])
        L.new(col, am.inputs[6])
        am.inputs[7].default_value = lin("#2a261f")
        col = am.outputs[2]
    rn = n.new("ShaderNodeTexNoise")
    rn.inputs["Scale"].default_value = 9
    rr = n.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = max(rough - 0.15, 0.05)
    rr.inputs["To Max"].default_value = min(rough + 0.15, 1.0)
    L.new(rn.outputs["Fac"], rr.inputs["Value"])
    L.new(rr.outputs[0], b.inputs["Roughness"])
    L.new(col, b.inputs["Base Color"])
    if emit:
        b.inputs["Emission Color"].default_value = lin(emit)
        b.inputs["Emission Strength"].default_value = emit_strength
    _mats[key] = m
    return m


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def from_bm(name, bm, material, smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    return link(bpy.data.objects.new(name, me))


def bevel(ob, width, segs=3, angle=40):
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = 1 if EXPORT else segs   # a chamfer reads the same at game distance
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angle)
    mod.harden_normals = False
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.set_sharp_from_angle(angle=math.radians(35))
    return ob


def place(ob, loc=(0, 0, 0), rot=(0, 0, 0), parent=None):
    ob.location = loc
    ob.rotation_euler = [math.radians(a) for a in rot]
    if parent is not None:
        ob.parent = parent
    return ob


def box(name, size, material, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.04, parent=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
    ob = from_bm(name, bm, material)
    if bev:
        bevel(ob, bev)
    return place(ob, loc, rot, parent)


def cyl(name, r, h, material, loc=(0, 0, 0), rot=(0, 0, 0), segs=24, bev=0.03, parent=None, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h)
    ob = from_bm(name, bm, material)
    if bev:
        bevel(ob, bev, angle=50)
    return place(ob, loc, rot, parent)


def sphere(name, r, material, loc=(0, 0, 0), scale=(1, 1, 1), parent=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=10 if EXPORT else 24, v_segments=6 if EXPORT else 14, radius=r)
    ob = from_bm(name, bm, material, smooth=True)
    ob.scale = scale
    return place(ob, loc, (0, 0, 0), parent)


def torus(name, R, r, material, loc=(0, 0, 0), rot=(0, 0, 0), arc=360, segs=32, parent=None):
    bm = bmesh.new()
    rings = []
    n = max(4, int((segs * 0.6 if EXPORT else segs) * arc / 360))
    sides = 8 if EXPORT else 12
    for i in range(n + 1):
        a = math.radians(arc) * i / n
        c = Vector((R * math.cos(a), R * math.sin(a), 0))
        t = Vector((-math.sin(a), math.cos(a), 0))
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            d = Vector((math.cos(a), math.sin(a), 0)) * math.cos(b) + Vector((0, 0, 1)) * math.sin(b)
            ring.append(bm.verts.new(c + d * r))
        rings.append(ring)
    for i in range(n):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j]))
    ob = from_bm(name, bm, material, smooth=True)
    return place(ob, loc, rot, parent)


def rounded_rect(w, d, r, segs=8):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for i in range(segs + 1):
            a = math.radians(a0 + 90 * i / segs)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def slab(name, w, d, h, r, material, loc=(0, 0, 0), rot=(0, 0, 0), bev=0.12, parent=None):
    """A pill-cornered slab: Colony Works' signature volume."""
    bm = bmesh.new()
    pts = rounded_rect(w, d, r)
    bot = [bm.verts.new((x, y, -h / 2)) for x, y in pts]
    top = [bm.verts.new((x, y, h / 2)) for x, y in pts]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    ob = from_bm(name, bm, material)
    if bev:
        bevel(ob, bev, segs=4, angle=60)
    return place(ob, loc, rot, parent)


def import_glb(path, loc=(0, 0, 0), yaw=0, scale=1.0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    root = bpy.data.objects.new("glb_" + Path(path).stem, None)
    link(root)
    for o in new:
        if o.parent is None:
            o.parent = root
        if o.animation_data:
            o.animation_data.action = None
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    root.scale = (scale, scale, scale)
    # The game lights foliage brighter than Cycles does; lift textured leaves so they read like the in-game trees.
    for o in new:
        for slot in getattr(o, "material_slots", []):
            m = slot.material
            if not m or not m.use_nodes or m.get("lifted") or not any(k in m.name.lower() for k in ("lea", "need", "canopy", "foli")):
                continue
            b = m.node_tree.nodes.get("Principled BSDF")
            if not b or not b.inputs["Base Color"].is_linked:
                continue
            src = b.inputs["Base Color"].links[0].from_socket
            mix = m.node_tree.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.clamp_result = False
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs[7].default_value = (2.2, 2.2, 2.0, 1)
            m.node_tree.links.new(src, mix.inputs[6])
            m.node_tree.links.new(mix.outputs[2], b.inputs["Base Color"])
            m["lifted"] = True
    return root


def ground(size=200, color="#5aa83a", noise=True):
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size / 2)
    return from_bm("Ground", bm, mat("ground", color, rough=0.95, noise_scale=0.6))


def disc(name, r, material, loc, z=0.01, segs=48, wobble=0.0, seed=1):
    rnd = random.Random(seed)
    bm = bmesh.new()
    c = bm.verts.new((0, 0, 0))
    ring = []
    ph = [rnd.uniform(0, 6.28) for _ in range(3)]
    for i in range(segs):
        a = 2 * math.pi * i / segs
        rr = r * (1 + wobble * (0.5 * math.sin(3 * a + ph[0]) + 0.3 * math.sin(5 * a + ph[1]) + 0.2 * math.sin(7 * a + ph[2])))
        ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), 0)))
    for i in range(segs):
        bm.faces.new((c, ring[i], ring[(i + 1) % segs]))
    ob = from_bm(name, bm, material)
    return place(ob, (loc[0], loc[1], z))


def strip(name, pts, width, material, z=0.02):
    """A flat path along points (roads, tracks)."""
    bm = bmesh.new()
    left, right = [], []
    for i, p in enumerate(pts):
        p = Vector((p[0], p[1], 0))
        a = Vector((pts[max(i - 1, 0)][0], pts[max(i - 1, 0)][1], 0))
        b = Vector((pts[min(i + 1, len(pts) - 1)][0], pts[min(i + 1, len(pts) - 1)][1], 0))
        t = (b - a).normalized()
        nrm = Vector((-t.y, t.x, 0))
        left.append(bm.verts.new(p + nrm * width / 2 + Vector((0, 0, z))))
        right.append(bm.verts.new(p - nrm * width / 2 + Vector((0, 0, z))))
    for i in range(len(pts) - 1):
        bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
    return from_bm(name, bm, material)


def sky(strength=1.0, top="#6fb6e8", horizon="#cfe6f2"):
    sc = bpy.context.scene
    w = bpy.data.worlds.new("Sky")
    sc.world = w
    w.use_nodes = True
    n = w.node_tree.nodes
    L = w.node_tree.links
    bg = n["Background"]
    lp = n.new("ShaderNodeLightPath")
    mix = n.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    # Camera rays see a sky gradient by view height; everything else gets a soft even fill.
    tc = n.new("ShaderNodeTexCoord")
    sep = n.new("ShaderNodeSeparateXYZ")
    L.new(tc.outputs["Window"], sep.inputs[0])
    ramp = n.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[0].color = lin(horizon)
    ramp.color_ramp.elements[1].position = 1.0
    ramp.color_ramp.elements[1].color = lin(top)
    L.new(sep.outputs["Y"], ramp.inputs[0])
    L.new(lp.outputs["Is Camera Ray"], mix.inputs["Factor"])
    mix.inputs[6].default_value = lin("#a9cfe6")
    L.new(ramp.outputs[0], mix.inputs[7])
    L.new(mix.outputs[2], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = strength


def sun(rot=(50, 0, -35), energy=4.0):
    s = bpy.data.lights.new("Sun", "SUN")
    s.energy = energy
    s.angle = math.radians(3)
    s.color = (1.0, 0.96, 0.9)
    ob = link(bpy.data.objects.new("Sun", s))
    ob.rotation_euler = [math.radians(a) for a in rot]
    return ob


def point(name, loc, color, energy, radius=0.1):
    l = bpy.data.lights.new(name, "POINT")
    l.energy = energy
    l.color = lin(color)[:3]
    l.shadow_soft_size = radius
    ob = link(bpy.data.objects.new(name, l))
    ob.location = loc
    return ob


def setup_render(samples=48):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Medium High Contrast"


def camera(name, loc, target, lens=35, ortho=None):
    cam = bpy.data.cameras.new(name)
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens = lens
    cam.clip_end = 2000
    ob = link(bpy.data.objects.new(name, cam))
    ob.location = loc
    ob.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return ob


def render(cam, path, w=1400, h=900):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


_protos = {}


def proto(path):
    """Imports a glb once into a hidden collection for instancing."""
    path = str(path)
    if path in _protos:
        return _protos[path]
    coll = bpy.data.collections.new("proto_" + Path(path).stem)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)
        if o.animation_data:
            o.animation_data.action = None
    for o in new:
        for slot in getattr(o, "material_slots", []):
            m = slot.material
            if m and m.use_nodes and m.name.startswith("core") and not m.get("lifted"):
                b = m.node_tree.nodes.get("Principled BSDF")
                c = b.inputs["Base Color"].default_value
                b.inputs["Base Color"].default_value = (min(c[0] * 3, 1), min(c[1] * 3, 1), min(c[2] * 3, 1), 1)
                m["lifted"] = True
    for o in new:
        for slot in getattr(o, "material_slots", []):
            m = slot.material
            if not m or not m.use_nodes or m.get("lifted") or not any(k in m.name.lower() for k in ("lea", "need", "canopy", "foli")):
                continue
            b = m.node_tree.nodes.get("Principled BSDF")
            if not b or not b.inputs["Base Color"].is_linked:
                continue
            src = b.inputs["Base Color"].links[0].from_socket
            mix = m.node_tree.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.clamp_result = False
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs[7].default_value = (2.2, 2.2, 2.0, 1)
            m.node_tree.links.new(src, mix.inputs[6])
            m.node_tree.links.new(mix.outputs[2], b.inputs["Base Color"])
            m["lifted"] = True
    _protos[path] = coll
    return coll


def inst(path, loc, yaw=0.0, scale=1.0):
    e = bpy.data.objects.new("inst", None)
    e.instance_type = "COLLECTION"
    e.instance_collection = proto(path)
    e.location = loc
    e.rotation_euler = (0, 0, math.radians(yaw))
    e.scale = (scale, scale, scale)
    return link(e)


# =====================================================================================================
# Game export
# =====================================================================================================
_finish_images = {}


def _finish(name, metal, noise_scale, cracks, moss):
    n = name.lower()
    if cracks > 0 or moss > 0 or "stone" in n or "flag" in n:
        return "stone"
    if "wood" in n or "plank" in n:
        return "wood"
    if "worn_concrete" in n:
        return "worn_concrete"
    if "concrete" in n:
        return "concrete"
    return "metal" if metal >= 0.3 else "paint"


def _finish_nodes(finish):
    if finish not in _finish_images:
        import textures  # noqa: PLC0415  (only the export needs numpy textures)
        detail, normal = textures.images(finish)
        _finish_images[finish] = (_image(f"{finish}_detail", detail, "sRGB"), _image(f"{finish}_normal", normal, "Non-Color"))
    return _finish_images[finish]


def _image(name, rgb, colorspace):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), np.float32)
    px[..., :3] = np.round(np.clip(rgb[::-1], 0, 1) * 255.0) / 255.0
    img.pixels.foreach_set(px.ravel())
    img.pack()
    img.filepath_raw = f"//{name}.png"
    img.file_format = "PNG"
    return img


def _game_mat(name, color, rough, metal, grime, emit, emit_strength, moss, noise_scale, cracks):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m["grime"] = grime
    m["moss"] = moss
    m["base"] = color
    if emit:
        b.inputs["Base Color"].default_value = lin(color)
        b.inputs["Emission Color"].default_value = lin(emit)
        b.inputs["Emission Strength"].default_value = min(emit_strength, GAME_GLOW)
        m["finish"] = "glow"
        return m
    finish = _finish(name, metal, noise_scale, cracks, moss)
    m["finish"] = finish
    detail, normal = _finish_nodes(finish)
    td = nt.nodes.new("ShaderNodeTexImage")
    td.image = detail
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    mix.inputs[7].default_value = lin(color)
    nt.links.new(td.outputs["Color"], mix.inputs[6])
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = normal
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = 0.6
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


MOSS = "#6f9a3a"
GRIME = "#4a4232"


def _bake_vertex_colours(ob):
    """Grime rising from the ground (z 0 to 1.4 m) and moss on upward faces of mossy materials, as vertex colours the
    game multiplies the base colour by."""
    me = ob.data
    attr = me.color_attributes.new("Color", "BYTE_COLOR", "CORNER")
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    out = np.ones((len(me.loops), 4))
    mats = me.materials
    for poly in me.polygons:
        m = mats[poly.material_index] if poly.material_index < len(mats) else None
        if m is None or m.get("finish") == "glow":
            continue
        grime = float(m.get("grime", 0.0))
        moss = float(m.get("moss", 0.0))
        base = np.array(lin(m.get("base", "#808080"))[:3])
        g = np.array(lin(GRIME)[:3]) / np.maximum(base, 1e-3)
        mo = np.clip(np.array(lin(MOSS)[:3]) / np.maximum(base, 1e-3), 0, 1)
        up = poly.normal.z
        for li in poly.loop_indices:
            z = co[me.loops[li].vertex_index][2]
            c = np.ones(3)
            if grime > 0:
                f = grime * max(0.0, 1.0 - z / 1.4) * 0.6
                c = c * (1 - f) + np.clip(g, 0, 1) * f
            if moss > 0 and up > 0.6:
                f = min(1.0, moss * (up - 0.6) / 0.3) * 0.85
                c = c * (1 - f) + mo * f
            out[li, :3] = c
    attr.data.foreach_set("color", out.ravel())
    me.color_attributes.active_color = attr


def _box_uvs(ob):
    me = ob.data
    uv = me.uv_layers.new(name="UVMap")
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    import textures  # noqa: PLC0415
    out = np.zeros((len(me.loops), 2))
    for poly in me.polygons:
        m = me.materials[poly.material_index] if poly.material_index < len(me.materials) else None
        tile = textures.TILE.get(m.get("finish") if m else None, 1.0)
        axis = int(np.argmax(np.abs(np.asarray(poly.normal))))
        a, b = [k for k in range(3) if k != axis]
        for li in poly.loop_indices:
            p = co[me.loops[li].vertex_index]
            out[li] = (p[a] / tile, p[b] / tile)
    uv.data.foreach_set("uv", out.ravel())


def _realise(ob, root_inv, dg):
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    me.transform(root_inv @ ob.matrix_world)
    return me


def collide_only(ob):
    """Marks ob as collision only: it goes in the collider and is not drawn (a smooth ramp under a flight of steps)."""
    ob["collide"] = "only"
    return ob


def no_collide(*obs):
    """Marks parts as drawn only: the player passes through them (thin cables, leaves, rubble on a ramp)."""
    for ob in obs:
        ob["collide"] = "never"
    return obs[0] if len(obs) == 1 else obs


def export(root, path, collide_min=0.6, lights=True, frame=None):
    """Writes the structure under root to a .glb with its origin at frame's (default root's): one mesh of every
    visible part (named after the file), a 'collider' mesh of the parts at least collide_min metres across (the game
    collides with it and does not draw it), and the point lights. Detail too small to bump into (screws, labels,
    leaves) only shows. A part marked with no_collide never collides, and one marked with collide_only always does
    and is not drawn."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    root_inv = (frame or root).matrix_world.inverted()
    obs = [ob for ob in root.children_recursive if not ob.hide_render and ob.type in ("MESH", "FONT")]
    parts = [me for me in (_realise(ob, root_inv, dg) for ob in obs if ob.get("collide") != "only") if len(me.polygons) > 0]
    # the collider is the larger parts without their bevels (openings cut by booleans stay)
    big = [ob for ob in obs if ob.type == "MESH" and ob.get("collide") != "never"
           and (ob.get("collide") == "only" or max(ob.dimensions) >= collide_min)]
    for ob in big:
        for mod in ob.modifiers:
            if mod.type == "BEVEL":
                mod.show_viewport = False
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    solid = [me for me in (_realise(ob, root_inv, dg) for ob in big) if len(me.polygons) > 0]
    stem = Path(path).stem
    visual = _join(stem, parts)
    _box_uvs(visual)
    _bake_vertex_colours(visual)
    collider = _join("collider", solid)
    collider.data.materials.clear()
    lamps = []
    if lights:
        for ob in root.children_recursive:
            if ob.type == "LIGHT":
                lamp = bpy.data.objects.new(ob.name, ob.data)
                lamp.matrix_world = root_inv @ ob.matrix_world
                link(lamp)
                lamps.append(lamp)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in (visual, collider, *lamps):
        ob.select_set(True)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_animations=False, export_vertex_color="ACTIVE", export_tangents=False, export_image_format="AUTO",
        export_extras=False, export_cameras=False, export_lights=lights, export_import_convert_lighting_mode="SPEC")
    tris = sum(len(p.vertices) - 2 for p in visual.data.polygons)
    ctris = sum(len(p.vertices) - 2 for p in collider.data.polygons)
    for ob in (visual, collider, *lamps):
        bpy.data.objects.remove(ob)
    return tris, ctris


def _join(name, meshes):
    obs = []
    for me in meshes:
        ob = bpy.data.objects.new(name, me)
        link(ob)
        obs.append(ob)
    if not obs:
        ob = link(bpy.data.objects.new(name, bpy.data.meshes.new(name)))
        return ob
    with bpy.context.temp_override(active_object=obs[0], selected_editable_objects=obs, selected_objects=obs):
        bpy.ops.object.join()
    obs[0].name = name
    obs[0].data.name = name
    return obs[0]
