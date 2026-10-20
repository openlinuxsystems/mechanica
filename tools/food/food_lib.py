"""Shared helpers for the food and crop models (tools/food): materials, organic meshes and review renders.

Z up, metres. Nothing is downloaded; everything is built from scratch with bmesh.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "tools" / "bearing_engine"))
sys.path.insert(0, str(REPO / "tools" / "lift"))


# ---------------------------------------------------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------------------------------------------------

def srgb(hex_color):
    """sRGB hex to sRGB RGB, for bmesh loop colours (a byte colour layer stores what it is given as sRGB)."""
    h = hex_color.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def lin(hex_color):
    """sRGB hex to linear RGBA."""
    h = hex_color.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (*out, 1.0)


def material(name, color, rough=0.5, metal=0.0, coat=0.0, sss=0.0, vertex_color=False, emission=None, strength=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = lin(color)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    p.inputs["Coat Weight"].default_value = coat
    p.inputs["Coat Roughness"].default_value = 0.15
    if sss:
        p.inputs["Subsurface Weight"].default_value = sss
        p.inputs["Subsurface Radius"].default_value = (0.9, 0.25, 0.12)
        p.inputs["Subsurface Scale"].default_value = 0.01
    if emission:
        p.inputs["Emission Color"].default_value = lin(emission)
        p.inputs["Emission Strength"].default_value = strength
    if vertex_color:
        a = nt.nodes.new("ShaderNodeVertexColor")
        a.layer_name = "Col"
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs["A"].default_value = lin(color)
        nt.links.new(a.outputs["Color"], mix.inputs["B"])
        nt.links.new(mix.outputs["Result"], p.inputs["Base Color"])
    return m


def flesh_material(name, base=None, light=None, seed_color=None):
    """Cut fruit: its colours are in the mesh's vertex colours (a light, wet middle and a darker rim), as in the .glb."""
    return material(name, "#FFFFFF", rough=0.22, sss=0.15, vertex_color=True)


def image_material(name, path, under="#EFE6CF", rough=0.8):
    """A printed label: the picture at ``path`` (RGBA) laid over a paper colour."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = material(name, under, rough=rough)
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(path), check_existing=True)
    tex.extension = "CLIP"
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs["A"].default_value = lin(under)
    mix.inputs["Factor"].default_value = 1.0
    nt.links.new(tex.outputs["Color"], mix.inputs["B"])
    nt.links.new(mix.outputs["Result"], p.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], p.inputs["Alpha"])
    return m


# ---------------------------------------------------------------------------------------------------------------------
# Objects
# ---------------------------------------------------------------------------------------------------------------------

def link(ob, parent=None):
    bpy.context.scene.collection.objects.link(ob)
    if parent is not None:
        ob.parent = parent
    return ob


def empty(name, parent=None, location=(0, 0, 0)):
    ob = bpy.data.objects.new(name, None)
    ob.location = location
    return link(ob, parent)


def from_bm(name, bm, mats, parent=None, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    link(ob, parent)
    if smooth:
        me.shade_smooth()
    return ob


def apply_modifiers(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    for mod in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def solidify(ob, t, offset=0.0):
    mod = ob.modifiers.new("Solidify", "SOLIDIFY")
    mod.thickness = t
    mod.offset = offset
    return mod


def strip(points, widths, normals, mat=0, fold=0.0):
    """A leaf-like ribbon: one quad row either side of a centre line, the midrib ``fold`` lower than the edges."""
    bm = bmesh.new()
    rows = []
    for p, w, n in zip(points, widths, normals):
        p, n = Vector(p), Vector(n).normalized()
        rows.append((p, w, n))
    verts = []
    for i, (p, w, n) in enumerate(rows):
        t = (rows[min(i + 1, len(rows) - 1)][0] - rows[max(i - 1, 0)][0]).normalized()
        side = t.cross(n).normalized()
        verts.append((bm.verts.new(p + side * w + n * fold * w), bm.verts.new(p),
                      bm.verts.new(p - side * w + n * fold * w)))
    for a, b in zip(verts, verts[1:]):
        for j in range(2):
            f = bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
            f.material_index = mat
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    return bm


def tube(points, radii, segs=10, cap=True, mat=0, cap_mat=None):
    """A swept tube along ``points`` with a radius per point."""
    bm = bmesh.new()
    rings = []
    prev_side = None
    for i, (p, r) in enumerate(zip(points, radii)):
        p = Vector(p)
        t = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(i - 1, 0)])).normalized()
        if prev_side is None:
            up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            prev_side = t.cross(up).normalized()
        side = (prev_side - t * prev_side.dot(t)).normalized()
        prev_side = side
        other = t.cross(side)
        ring = [bm.verts.new(p + (side * math.cos(a) + other * math.sin(a)) * r)
                for a in (2 * math.pi * k / segs for k in range(segs))]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            f = bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
            f.material_index = mat
    if cap:
        f = bm.faces.new(list(reversed(rings[0])))
        f.material_index = mat
        f = bm.faces.new(rings[-1])
        f.material_index = mat if cap_mat is None else cap_mat
    return bm


def bvh_of(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(ob, dg)


def surface_z(bvh, x, y, z_from=1.0):
    hit = bvh.ray_cast(Vector((x, y, z_from)), Vector((0, 0, -1)))
    return hit[0].z if hit[0] is not None else 0.0


def bezier(p0, p1, p2, p3, n=12):
    p0, p1, p2, p3 = map(Vector, (p0, p1, p2, p3))
    out = []
    for i in range(n + 1):
        t = i / n
        out.append((1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1 + 3 * (1 - t) * t * t * p2 + t ** 3 * p3)
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Rendering
# ---------------------------------------------------------------------------------------------------------------------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _setup_render(samples, size, transparent):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.seed = 7
    sc.cycles.use_denoising = True
    sc.render.film_transparent = transparent
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    return sc


def render_view(objects, path, view, ortho=None, target=(0, 0, 0), size=900, samples=64, lens=None, distance=6.0,
                ground=True, light_scale=1.0, bg=(0.93, 0.93, 0.94), clip_start=0.01):
    """A review render: the objects on a pale studio floor, three soft lights, from ``view``."""
    sc = _setup_render(samples, size, False)
    keep = set(objects)
    hidden = [o for o in sc.objects if o not in keep and not o.hide_render and o.type in ("MESH", "CURVE")]
    for o in hidden:
        o.hide_render = True
    world = bpy.data.worlds.get("ReviewWorld") or bpy.data.worlds.new("ReviewWorld")
    sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (*bg, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55
    made = []
    target = Vector(target)
    if ground:
        me = bpy.data.meshes.new("RevGround")
        s = 20
        lowest = min((o.matrix_world @ Vector(c)).z for o in objects if o.type == "MESH" for c in o.bound_box)
        tx, ty = target.x, target.y
        me.from_pydata([(tx - s, ty - s, lowest), (tx + s, ty - s, lowest), (tx + s, ty + s, lowest), (tx - s, ty + s, lowest)],
                       [], [(0, 1, 2, 3)])
        gm = material("RevGroundMat", "#E9E9EA", rough=0.9)
        me.materials.append(gm)
        gob = link(bpy.data.objects.new("RevGround", me))
        made.append(gob)
    v = Vector(view).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    for name, offset, energy, lsize in (("Key", v * 2.5 + side * -1.6 + Vector((0, 0, 2.2)), 160, 1.6),
                                        ("Fill", v * 2.0 + side * 2.2, 45, 2.0),
                                        ("Rim", -v * 2.2 + Vector((0, 0, 1.8)), 90, 1.5)):
        light = bpy.data.lights.new("Rev" + name, "AREA")
        light.energy, light.size = energy * light_scale, lsize
        ob = link(bpy.data.objects.new("Rev" + name, light))
        ob.location = target + offset
        ob.rotation_euler = (target - ob.location).to_track_quat("-Z", "Y").to_euler()
        made.append(ob)
    cam = bpy.data.cameras.new("RevCam")
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens = lens or 50
    cam.clip_start = clip_start
    cob = link(bpy.data.objects.new("RevCam", cam))
    cob.location = target + v * distance
    cob.rotation_euler = (-v).to_track_quat("-Z", "Y").to_euler()
    made.append(cob)
    sc.camera = cob
    sc.render.filepath = str(path)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    for ob in made:
        bpy.data.objects.remove(ob)
    for o in hidden:
        o.hide_render = False
    return path


def bounds(objects):
    """Centre and largest extent of the meshes among ``objects`` (world space, modifiers applied)."""
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in objects:
        if o.type == "MESH" and not o.hide_render:
            ev = o.evaluated_get(dg)
            pts += [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return (lo + hi) / 2, max(hi - lo)


def auto_icon(objects, path, view=(1.25, -1.6, 1.5), fill=1.25):
    c, size = bounds(objects)
    return render_icon(objects, path, view, size * fill, target=tuple(c))


def render_icon(objects, path, view, ortho, target=(0, 0, 0)):
    import build_bearing_engine as bbe
    return bbe.render_icon(objects, Path(path), view=view, ortho=ortho, target=target)


def tree(root):
    return [root, *root.children_recursive]


def sheet(images, labels, path, cols, title, cell=600, pad=20):
    """A labelled contact sheet."""
    rows = math.ceil(len(images) / cols)
    head = 70
    W = cols * cell + (cols + 1) * pad
    H = head + rows * (cell + 40) + (rows + 1) * pad
    out = Image.new("RGB", (W, H), (245, 245, 246))
    d = ImageDraw.Draw(out)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 34)
        small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 24)
    except OSError:
        font = small = ImageFont.load_default()
    d.text((pad, 18), title, fill=(30, 33, 38), font=font)
    for i, (img, label) in enumerate(zip(images, labels)):
        r, c = divmod(i, cols)
        x = pad + c * (cell + pad)
        y = head + pad + r * (cell + 40 + pad)
        im = Image.open(img).convert("RGBA")
        k = min(cell / im.width, cell / im.height)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
        bgc = Image.new("RGBA", (cell, cell), (255, 255, 255, 255) if im.getpixel((0, 0))[3] < 10 else (245, 245, 246, 255))
        bgc.alpha_composite(im, ((cell - im.width) // 2, (cell - im.height) // 2))
        out.paste(bgc.convert("RGB"), (x, y))
        d.text((x, y + cell + 6), label, fill=(60, 64, 70), font=small)
    out.save(path)
    return path


def join(bms):
    """One bmesh from several (each is freed)."""
    out = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("_join")
        b.to_mesh(me)
        b.free()
        out.from_mesh(me)
        bpy.data.meshes.remove(me)
    return out


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def soil(location=(0, 0, 0), wet=True):
    """The vanilla soil mound (3 x 3 blocks), its top at z = 0 under ``location``, in a damp brown."""
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=str(REPO / "mods" / "vanilla" / "meshes" / "soil.glb"))
    obs = [o for o in bpy.context.scene.objects if o not in before]
    mat = material("soil_wet" if wet else "soil_dry", "#4A3423" if wet else "#8A6E52", rough=0.75 if wet else 0.95)
    nt = mat.node_tree
    if not any(n.type == "TEX_NOISE" for n in nt.nodes):
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 90.0
        bump = nt.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.4
        nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
        nt.links.new(bump.outputs["Normal"], nt.nodes["Principled BSDF"].inputs["Normal"])
    for o in obs:
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(mat)
        if o.parent is None:
            o.location = Vector(location) + Vector((0, 0, -0.006))
    return obs


def cut(body, cutter_bm, flesh, color_of=None):
    """Bites ``cutter_bm`` (a closed mesh, in ``body``'s own mesh coordinates) out of ``body``: the new faces take
    ``flesh`` (a vertex-coloured material) and, with ``color_of(co) -> (r, g, b)`` in linear RGB, their colours; the
    lip where skin meets flesh is kept sharp. The body needs a ``Col`` colour attribute when ``color_of`` is given."""
    bpy.context.view_layer.update()
    bmesh.ops.transform(cutter_bm, matrix=body.matrix_world, verts=cutter_bm.verts)
    cutter = from_bm(body.name + "_Cutter", cutter_bm, [flesh], None)
    cutter.hide_render = True
    mod = body.modifiers.new("Bite", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "MANIFOLD"
    mod.material_mode = "TRANSFER"
    mod.object = cutter
    apply_modifiers(body)
    bpy.data.objects.remove(cutter)
    me = body.data
    if flesh.name not in me.materials:
        return body
    idx = list(me.materials).index(flesh)
    col = me.color_attributes.get("Col")
    if col is not None and color_of is not None:
        for poly in me.polygons:
            if poly.material_index == idx:
                for li in poly.loop_indices:
                    col.data[li].color = (*color_of(me.vertices[me.loops[li].vertex_index].co), 1)
    sharp = me.attributes.get("sharp_edge") or me.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
    faces_of = {}
    for poly in me.polygons:
        for ek in poly.edge_keys:
            faces_of.setdefault(ek, set()).add(poly.material_index)
    for e in me.edges:
        if len(faces_of.get(e.key, ())) > 1:
            sharp.data[e.index].value = True
    return body


def vertex_colors(bm, color_of):
    """Paints a bmesh's ``Col`` loop colours from ``color_of(co, normal) -> (r, g, b)`` (sRGB: a byte colour layer)."""
    col = bm.loops.layers.color.get("Col") or bm.loops.layers.color.new("Col")
    for f in bm.faces:
        for lp in f.loops:
            lp[col] = (*color_of(lp.vert.co, lp.vert.normal), 1.0)
    return bm


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def noise3(p, scale=1.0, seed=0):
    """Cheap smooth value noise in [-1, 1] (sum of a few sines), for mottling skins without textures."""
    x, y, z = (c * scale for c in p)
    s = seed * 1.7
    return (math.sin(x * 1.9 + s) * math.cos(y * 2.3 - s) + math.sin(y * 1.3 + z * 2.1 + 2 * s)
            + math.cos(z * 1.7 - x * 1.1 + 3 * s)) / 3
