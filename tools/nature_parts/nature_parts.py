"""The Creative nature props (parts audit D6): Boulder, Large Rock, Small Rock, Bush, Grass Tuft, Flower Patch, Fallen
Log and Tree Trunk, built from the world's own meshes so a placed prop looks like what grows and lies about in the
world. build_nature_parts.py runs them through tools/part_kit/decor_kit.py's runner.

* Rocks: the chiselled Square Rocks deposit blocks (``meshes/world/rock_{small,medium,large}_*.glb``,
  tools/rocks/build_rocks.py), Old Stone with moss on the top edges, scaled to fit each grid box.
* Bush, Grass Tuft, Flower Patch: the built-in flora models (Mechanica.Core ``FloraModels``: ``bush_berry``, which the
  wild Bush object draws; ``grass_tall`` and ``grass_short``; ``flowers_*`` with ``clover``), read through
  tools/nature_parts/flora_dump, with the flora shader's tint, shade and yellow tips baked into vertex colours (the
  shader's own formula, FloraMaterials.cs) since a part has no flora shader.
* Fallen Log, Tree Trunk: the Leafy tree's trunk (``meshes/world/leafy_tree.glb``, tools/trees/build_trees.py), the
  bark cut out between two heights with the Wood Log's end grain (``meshes/items/wood_log.glb``) on the cuts: the
  trunk stands on its root flare, the log is a section of the trunk lying down.

Axes as decor_kit's: Blender Z up, the part's front towards -Y, centred on its grid box; every model rests on the
bottom of its box.
"""

from __future__ import annotations

import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

import decor_kit as D
from decor_kit import Part

VANILLA = D.REPO / "mods" / "vanilla" / "meshes"
WORLD = VANILLA / "world"
# The flora models each prop uses (FloraModels ids); build_nature_parts.py dumps these.
FLORA_MODELS = ("bush_berry", "grass_tall", "grass_short", "flowers_yellow", "flowers_white", "flowers_purple", "clover")
# Tints (sRGB), as the world draws them: the wild Bush object's (TerrainChunk.FloraTint), the meadow's tall grass and
# its ground cover (worldgen.json, meadow flora). Accent and brightness are the flora shader's.
BUSH_TINT = (0.36, 0.68, 0.24)
GRASS_TINT = "#5a9a3a"
COVER_TINT = "#78c23e"
ACCENT = (0.98, 0.86, 0.2)
BRIGHTNESS = 1.1
EPS = 0.002                       # kept between a model and its grid box

FLORA: dict = {}                  # id -> {"positions", "colors"} (glTF axes), filled by build_nature_parts.py


def lin(c):
    """sRGB (0..1 tuple or #rrggbb) to linear."""
    if isinstance(c, str):
        c = tuple(int(c[i:i + 2], 16) / 255 for i in (1, 3, 5))
    return np.array([x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c])


# =====================================================================================================
# Helpers
# =====================================================================================================


def fit(obj, half, scale=None, most=None):
    """Scales `obj` uniformly (by `scale`, or as large as fits the box of half extents `half`, at most `most`), centres
    it across and sets it on the bottom of the box. Bakes into the mesh; returns the scale used."""
    me = obj.data
    pts = np.array([v.co[:] for v in me.vertices])
    lo, hi = pts.min(0), pts.max(0)
    size = hi - lo
    room = 2 * np.array(half) - 2 * EPS
    s = scale if scale is not None else float(min(room / size))
    if most is not None:
        s = min(s, most)
    mid = (lo + hi) / 2
    shift = Vector((-mid[0] * s, -mid[1] * s, -half[2] + EPS - lo[2] * s))
    me.transform(Matrix.Translation(shift) @ Matrix.Scale(s, 4))
    me.update()
    return s


def flora_material(name, double_sided=True):
    """White base times the baked vertex colours (exported as COLOR_0; ModGltf turns them on in game)."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Specular IOR Level"].default_value = 0.2
    mat.use_backface_culling = not double_sided
    return mat


def flora_object(name, model, tint, mat, at=(0.0, 0.0), scale=1.0, yaw=0.0):
    """A FloraModels mesh as a Blender object (flat-shaded triangles), the flora shader's colours baked in: leaves
    (alpha 1) take `tint` by their shade with the accent on the tips, flowers and bolts keep their own colour."""
    data = FLORA[model]
    p = np.array(data["positions"], dtype=float).reshape(-1, 3)
    c = np.array(data["colors"], dtype=float).reshape(-1, 4)
    t = lin(tint)
    accent = lin(ACCENT)
    shade, tips = c[:, :1], c[:, 1:2]
    leaf = t * shade * BRIGHTNESS
    tip = (t * 0.4 + accent * 0.6) * np.maximum(shade, 0.85) * BRIGHTNESS
    leafy = leaf * (1 - tips) + tip * tips
    own = np.array([lin(row) for row in c[:, :3]])
    rgb = np.clip(np.where(c[:, 3:] > 0.5, leafy, own), 0, 1)
    # glTF (x, y up, z) -> Blender (x, -z, y up), then the placement
    co, sn = math.cos(yaw), math.sin(yaw)
    bx, by, bz = p[:, 0], -p[:, 2], p[:, 1]
    verts = np.stack([(bx * co - by * sn) * scale + at[0], (bx * sn + by * co) * scale + at[1], bz * scale], 1)
    n = len(verts) // 3
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(n)])
    me.validate()
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    loops = np.array([li for poly in me.polygons for li in poly.loop_indices])
    verts_of = np.array([me.loops[li].vertex_index for li in loops])
    rgba = np.ones((len(me.loops), 4))
    rgba[loops, :3] = rgb[verts_of]
    col.data.foreach_set("color", rgba.ravel())
    me.materials.append(mat)
    me.shade_flat()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    objects[0].name = name
    objects[0].data.name = name
    return objects[0]


def material_named(prefix):
    return next(m for m in bpy.data.materials if m.name == prefix or m.name.startswith(prefix + "."))


# =====================================================================================================
# Rocks
# =====================================================================================================


def rock(file, name):
    def build(half):
        ob = D.place(D.asset(WORLD / file), Matrix.Identity(4), name)
        s = fit(ob, half)
        print(f"  {name}: {file} at {s:.3f}")
        return [ob]
    return build


# =====================================================================================================
# Trunk sections
# =====================================================================================================


def trunk_section(name, z0, z1):
    """The Leafy tree's bark between heights z0 and z1 (metres above its ground point), the cuts capped with the Wood
    Log's end grain, as a new object (Blender axes, still upright)."""
    tree = D.asset(WORLD / "leafy_tree.glb")
    D.asset(VANILLA / "items" / "wood_log.glb")          # brings its "end" material in
    me = tree.data.copy()
    me.name = name
    bark = next(i for i, m in enumerate(me.materials) if m.name.startswith("bark"))
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != bark], context="FACES")
    for z, keep_above in ((z0, True), (z1, False)):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(0, 0, z), plane_no=(0, 0, 1),
                               clear_inner=keep_above, clear_outer=not keep_above)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    me.materials.append(material_named("end"))
    end = len(me.materials) - 1
    uv = bm.loops.layers.uv.active
    for z, up in ((z0, False), (z1, True)):
        ring = [v for v in bm.verts if abs(v.co.z - z) < 1e-4 and v.is_boundary]
        centre = sum((v.co for v in ring), Vector()) / len(ring)
        ring.sort(key=lambda v: math.atan2(v.co.y - centre.y, v.co.x - centre.x))
        reach = max((v.co - centre).length for v in ring)
        mid = bm.verts.new(centre)
        for a, b in zip(ring, ring[1:] + ring[:1]):
            f = bm.faces.new((mid, a, b) if up else (mid, b, a))
            f.material_index = end
            f.smooth = False
            for loop in f.loops:
                d = (loop.vert.co - centre) / reach
                loop[uv].uv = (0.5 + 0.5 * d.x, 0.5 + 0.5 * d.y)
    bm.to_mesh(me)
    bm.free()
    unused = [i for i in range(len(me.materials)) if i not in {bark, end}]
    for i in sorted(unused, reverse=True):
        me.materials.pop(index=i)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def tree_trunk(half):
    """The Leafy trunk sawn off 2 m up, standing on its root flare (the part of the trunk above the ground)."""
    ob = trunk_section("TreeTrunkBark", 0.0, 2.0)
    s = fit(ob, half, most=1.0)
    print(f"  tree trunk at {s:.3f}")
    return [ob]


def fallen_log(half):
    """A 2.1 m section of the Leafy trunk above the flare, lying along the part's depth, scaled to fit its box."""
    ob = trunk_section("FallenLogBark", 0.7, 2.8)
    ob.data.transform(Matrix.Rotation(math.radians(90), 4, "X"))
    s = fit(ob, half)
    print(f"  fallen log at {s:.3f}")
    return [ob]


# =====================================================================================================
# Flora
# =====================================================================================================


def bush(half):
    ob = flora_object("BushLeaves", "bush_berry", BUSH_TINT, flora_material("bush", double_sided=False))
    s = fit(ob, half)
    print(f"  bush at {s:.3f}")
    return [ob]


def grass_tuft(half):
    mat = flora_material("grass")
    tall = flora_object("GrassTall", "grass_tall", GRASS_TINT, mat)
    short = flora_object("GrassShort", "grass_short", GRASS_TINT, mat, yaw=1.1)
    ob = join([tall, short], "GrassTuftBlades")
    s = fit(ob, half)
    print(f"  grass at {s:.3f}")
    return [ob]


def flower_patch(half):
    mat = flora_material("flowers")
    s = 0.74
    pieces = [
        flora_object("Clover", "clover", COVER_TINT, mat, at=(0.0, 0.0), scale=1.2, yaw=0.4),
        flora_object("FlowersPurple", "flowers_purple", COVER_TINT, mat, at=(-0.11, 0.08), scale=s, yaw=0.3),
        flora_object("FlowersYellow", "flowers_yellow", COVER_TINT, mat, at=(0.11, 0.1), scale=s, yaw=2.0),
        flora_object("FlowersWhite", "flowers_white", COVER_TINT, mat, at=(0.02, -0.12), scale=s, yaw=4.1),
    ]
    ob = join(pieces, "FlowerPatchPlants")
    fit(ob, half, scale=1.0)
    lo = np.array([v.co[:] for v in ob.data.vertices]).min(0)
    hi = np.array([v.co[:] for v in ob.data.vertices]).max(0)
    print(f"  flower patch {np.round(lo, 3)} .. {np.round(hi, 3)}")
    return [ob]


def part(size, build, note):
    p = Part(size, None, note)
    p.build = lambda mats, p=p: build(p.half)
    return p


PARTS = {
    "boulder": part((6, 5, 6), rock("rock_large_a.glb", "BoulderStone"),
                    "The large Square Rocks deposit block (rock_large_a), scaled to fit: Old Stone, moss on the top edges."),
    "large_rock": part((3, 3, 3), rock("rock_medium_b.glb", "LargeRockStone"),
                       "The medium Square Rocks deposit block (rock_medium_b), scaled to a 75 cm block."),
    "small_rock": part((1, 1, 1), rock("rock_small_a.glb", "SmallRockStone"),
                       "The small Square Rocks deposit block (rock_small_a), scaled to fist size."),
    "bush": part((5, 4, 5), bush,
                 "The wild Bush's own flora model (bush_berry: leaf lumps, red berries, a bolt or two) in the world's bush tint."),
    "grass_tuft": part((2, 3, 2), grass_tuft,
                       "The meadow's tall grass tuft (grass_tall) over a short one (grass_short), meadow tint, yellow tips."),
    "flower_patch": part((2, 1, 2), flower_patch,
                         "The meadow's purple, yellow and white flowers on a clover mat (flora models flowers_*, clover)."),
    "fallen_log": part((3, 3, 8), fallen_log,
                       "A section of the Leafy tree's trunk lying down, its bark with Wood Log end grain on both cuts."),
    "tree_trunk": part((5, 8, 5), tree_trunk,
                       "The Leafy tree's trunk sawn off 2 m up, standing on its root flare, end grain on top."),
}
