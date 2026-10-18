"""Build the rock outcrop cubes, the stone and metal chunks, the stone and metal rods, and the Scrap Stone texture.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/world/rock_cube.glb`` - one mineable rock-outcrop cube, 1 m on a side and centred on its
  origin: grey-brown stone with bevelled, gently chipped edges, its faces cracked into irregular flagstone-like
  plates, light green moss on the top and in the seams near the top edges. Several stacked make an outcrop.
* ``mods/vanilla/meshes/world/rock_cube_metal.glb`` - the rarer metal-rich cube: the same shape, darker bluish-grey
  stone with steel-silver ore veins and flecks (metallic in the metallic-roughness map), rust streaks, less moss.
* ``mods/vanilla/meshes/items/stone_chunk.glb`` and ``metal_chunk.glb`` - broken angular lumps (0.45 x 0.40 x 0.45 m,
  inside a 0.5 m box centred on the origin), fresh stone without moss.
* ``mods/vanilla/meshes/items/stone_rod.glb`` - the Stone Rod: a rough chiselled square bar, 0.22 x 0.22 x 0.98 m,
  its long axis along glTF Z.
* ``mods/vanilla/meshes/items/metal_rod.glb`` - the Metal Rod: the same bar in brushed steel (metallic 0.9,
  roughness about 0.35) with slightly bevelled edges and one thin seam line along the top.
* ``mods/vanilla/textures/blocks/scrap_stone.png`` - the Scrap Stone Block texture: 512 x 512, one 0.25 m block
  face, tiling seamlessly (rubble stones in dark mortar).

Each model is one mesh with one material: a baked albedo atlas (one box chart per side, see rock_textures.py) and
a tangent-space normal map, plus a metallic-roughness map on the metal-rich ones; the images are embedded in the
.glb. Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up (Blender -Y becomes glTF +Z,
so the rods are built along Blender Y).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/rocks/build_rocks.py                  # writes the .glb files and the .png
    python tools/rocks/build_rocks.py --render OUT_DIR # also renders the review sheet and an outcrop into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is
deterministic (seeded noise only), so running it twice gives the same files.
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from PIL import Image

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
sys.path.insert(0, str(Path(__file__).resolve().parent))
import rock_textures as rt  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
WORLD = REPO / "mods" / "vanilla" / "meshes" / "world"
ITEMS = REPO / "mods" / "vanilla" / "meshes" / "items"
BLOCKS = REPO / "mods" / "vanilla" / "textures" / "blocks"

# ---- Dimensions (metres) ----------------------------------------------------------------------------
CUBE_HALF = (0.5, 0.5, 0.5)
CUBE_RADIUS = 0.035         # edge rounding
CUBE_CHIP = 0.026           # deepest chip dent (inward only: nothing passes 0.5)
CHUNK_SIZE = (0.45, 0.45, 0.40)    # Blender x, y, z (up): glTF 0.45 x 0.40 (up) x 0.45
ROD_HALF = (0.11, 0.49, 0.11)      # long axis Blender Y = glTF Z
TEX_MAX = 1024


# =====================================================================================================
# Geometry
# =====================================================================================================

def axis_coords(h, rows, interior):
    """Lattice coordinates along one axis: the edge rows (distances in from the face) plus interior points."""
    inner = [-h + r for r in rows] + list(interior) + [h - r for r in reversed(rows)]
    return np.array([-h] + inner + [h])


def rounded_box(half, coords, radius, dent=None):
    """A watertight box lattice (quads, outward winding) whose edges are rounded with `radius`, each vertex pushed
    inward along its rounded normal by dent(p, edge_distance, corner_distance) metres. Returns verts, faces."""
    half = np.asarray(half, float)
    keys, lattice, faces = {}, [], []
    for k in range(3):
        a, b = (k + 1) % 3, (k + 2) % 3
        for sign in (1, -1):
            A, B = (a, b) if sign > 0 else (b, a)        # e_A x e_B = outward normal
            fixed = len(coords[k]) - 1 if sign > 0 else 0
            for i in range(len(coords[A]) - 1):
                for j in range(len(coords[B]) - 1):
                    quad = []
                    for di, dj in ((0, 0), (1, 0), (1, 1), (0, 1)):
                        idx = [0, 0, 0]
                        idx[k], idx[A], idx[B] = fixed, i + di, j + dj
                        t = tuple(idx)
                        if t not in keys:
                            keys[t] = len(lattice)
                            lattice.append(t)
                        quad.append(keys[t])
                    faces.append(quad)
    verts = []
    inner = half - radius
    for t in lattice:
        p = np.array([coords[0][t[0]], coords[1][t[1]], coords[2][t[2]]])
        c = np.clip(p, -inner, inner)
        d = p - c
        nout = d / np.linalg.norm(d)
        q = c + nout * radius
        dist = np.sort(half - np.abs(p))
        if dent is not None:
            q = q - nout * dent(p, dist[1], dist[2])
        verts.append(tuple(float(x) for x in q))
    return verts, faces


def cube_geometry():
    h = CUBE_HALF[0]
    interior = [-0.33, -0.21, -0.1, 0.0, 0.11, 0.22, 0.34]
    c = axis_coords(h, (0.025, 0.06), interior)
    face_noise = rt.Noise3(77)

    def dent(p, edge_d, corner_d):
        e = rt.edge_factor(edge_d, 0.035)
        corner = rt.edge_factor(corner_d, 0.07)
        chip = float(rt.chip_field(p[None])[0])
        wobble = 0.5 + 0.5 * float(face_noise.fbm(p[None], 2.0, octaves=2)[0])
        return 0.0015 + 0.004 * wobble + CUBE_CHIP * chip * e + 0.012 * corner * e
    return rounded_box(CUBE_HALF, [c, c, c], CUBE_RADIUS, dent)


def stone_rod_geometry():
    hx, hy, hz = ROD_HALF
    cx = axis_coords(hx, (0.008, 0.02), [-0.05, 0.0, 0.05])
    cy = axis_coords(hy, (0.008, 0.02), list(np.linspace(-0.4, 0.4, 9)))
    noise = rt.Noise3(311)

    def dent(p, edge_d, corner_d):
        e = rt.edge_factor(edge_d, 0.02)
        chip = float(rt.chip_field(p[None] * 2.0)[0])
        wobble = 0.5 + 0.5 * float(noise.fbm(p[None], 3.0, octaves=2)[0])
        return 0.001 + 0.003 * wobble + 0.012 * chip * e + 0.006 * rt.edge_factor(corner_d, 0.03) * e
    return rounded_box(ROD_HALF, [cx, cy, cx], 0.012, dent)


def metal_rod_geometry():
    hx, hy, hz = ROD_HALF
    cx = axis_coords(hx, (0.004,), [])
    cy = axis_coords(hy, (0.004,), [])
    return rounded_box(ROD_HALF, [cx, cy, cx], 0.007)


def chunk_geometry(seed):
    """A broken angular lump: a box knocked about by a dozen random flat fracture planes (each cut takes a slice
    off, mostly at the corners and edges), scaled to CHUNK_SIZE and centred."""
    rng = np.random.default_rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for k in range(13):
        n = rng.normal(0.0, 1.0, 3)
        if k < 8:                                       # the first cuts aim at the corners
            n = np.array([(1 if k & 1 else -1), (1 if k & 2 else -1), (1 if k & 4 else -1)], float) + rng.normal(0, 0.45, 3)
        n /= np.linalg.norm(n)
        reach = 0.5 * np.abs(n).sum()                   # how far the box extends along n
        depth = reach * rng.uniform(0.52, 0.8) if k < 8 else 0.5 * rng.uniform(0.82, 0.95)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=n * depth, plane_no=n, clear_outer=True)
        cut = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
        if cut:
            bmesh.ops.holes_fill(bm, edges=cut, sides=0)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(2), verts=bm.verts[:], edges=bm.edges[:])
    co = np.array([v.co[:] for v in bm.verts])
    lo, hi = co.min(0), co.max(0)
    size = np.asarray(CHUNK_SIZE)
    for v in bm.verts:
        v.co = (np.array(v.co[:]) - (lo + hi) / 2) / (hi - lo) * size
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.verts.index_update()
    verts = [tuple(v.co) for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


# =====================================================================================================
# Scene, materials, objects
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_image(name, rgb, colorspace):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), np.float32)
    # Blender images are bottom-up; quantise so the stored bytes do not depend on the encoder's rounding
    px[..., :3] = np.round(np.clip(rgb[::-1], 0, 1) * 255.0) / 255.0
    img.pixels.foreach_set(px.ravel())
    img.pack()
    return img


def make_material(name, albedo, normal, mr=None, rough=0.9, metal=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_backface_culling = True                  # exported as single-sided
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    ia = nt.nodes.new("ShaderNodeTexImage")
    ia.image = make_image(name + "_color", albedo, "sRGB")
    nt.links.new(ia.outputs["Color"], bsdf.inputs["Base Color"])
    inn = nt.nodes.new("ShaderNodeTexImage")
    inn.image = make_image(name + "_normal", normal, "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(inn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if mr is not None:
        ir = nt.nodes.new("ShaderNodeTexImage")
        ir.image = make_image(name + "_mr", mr, "Non-Color")
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(ir.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
        nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    else:
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
    return mat


def make_object(name, verts, faces, half, surface, tex_width, *, smooth=True, normal_strength=1.0, shade=0.55,
                rim=None, rough=0.9, metal=0.0):
    """One mesh object with box-chart UVs and its baked material."""
    charts, size, s, pad = rt.box_charts(half, tex_width, TEX_MAX)
    albedo, normal, mr = rt.bake(charts, size, s, pad, surface, normal_strength=normal_strength, shade=shade, rim=rim)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate(clean_customdata=False)
    co = np.array(verts)
    uv = me.uv_layers.new(name="UVMap")
    out = np.zeros((len(me.loops), 2))
    for poly in me.polygons:
        pts = co[list(poly.vertices)]
        nrm = np.zeros(3)
        for i in range(len(pts)):                       # Newell normal (robust for slightly bent quads)
            a, b = pts[i], pts[(i + 1) % len(pts)]
            nrm += np.array([(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])])
        c = rt.chart_of_normal(charts, nrm)
        for li in poly.loop_indices:
            out[li] = rt.chart_uv(c, co[me.loops[li].vertex_index], size, s, pad)
    uv.data.foreach_set("uv", out.ravel())
    me.materials.append(make_material(name.lower(), albedo, normal, mr, rough, metal))
    if smooth:
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(50))
    else:
        me.shade_flat()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_models():
    """name -> (object, output path)."""
    reset_scene()
    models = {}
    v, f = cube_geometry()
    models["rock_cube"] = (make_object("RockCube", v, f, CUBE_HALF, rt.OutcropStone(CUBE_HALF, 101), TEX_MAX,
                                       normal_strength=1.6, shade=0.6, rim=rt.BEIGE), WORLD / "rock_cube.glb")
    models["rock_cube_metal"] = (make_object("RockCubeMetal", v, f, CUBE_HALF, rt.OutcropStone(CUBE_HALF, 202, metal=True),
                                             TEX_MAX, normal_strength=1.6, shade=0.6, rim=rt.ORE_LIGHT),
                                 WORLD / "rock_cube_metal.glb")
    chalf = tuple(x / 2 for x in CHUNK_SIZE)
    v, f = chunk_geometry(303)
    models["stone_chunk"] = (make_object("StoneChunk", v, f, chalf, rt.ChunkStone(chalf, 303), 512, smooth=False,
                                         normal_strength=1.4, rim=rt.BEIGE), ITEMS / "stone_chunk.glb")
    v, f = chunk_geometry(404)
    models["metal_chunk"] = (make_object("MetalChunk", v, f, chalf, rt.ChunkStone(chalf, 404, metal=True), 512,
                                         smooth=False, normal_strength=1.4), ITEMS / "metal_chunk.glb")
    v, f = stone_rod_geometry()
    models["stone_rod"] = (make_object("StoneRod", v, f, ROD_HALF, rt.RodStone(ROD_HALF, 505), 512,
                                       normal_strength=1.5, rim=rt.BEIGE), ITEMS / "stone_rod.glb")
    v, f = metal_rod_geometry()
    models["metal_rod"] = (make_object("MetalRod", v, f, ROD_HALF, rt.RodMetal(ROD_HALF, 606), 512,
                                       normal_strength=1.0, shade=0.3), ITEMS / "metal_rod.glb")
    return models


def export(ob, path):
    for o in bpy.context.scene.objects:
        o.select_set(o is ob)
    bpy.context.view_layer.objects.active = ob
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=False, export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False,
        export_tangents=True, export_image_format="JPEG", export_jpeg_quality=92, export_image_quality=92,
        export_extras=False, export_cameras=False, export_lights=False)


def write_scrap_stone():
    rgb = rt.scrap_stone_tile()
    path = BLOCKS / "scrap_stone.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.round(rgb * 255).astype(np.uint8), "RGB").save(path, optimize=True)
    return path


# =====================================================================================================
# Verification
# =====================================================================================================

def glb_info(path):
    """Bounds (glTF axes), triangle count and embedded image count, read back from the .glb itself."""
    data = path.read_bytes()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glb"
    ln, kind = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            lo, hi = np.minimum(lo, acc["min"]), np.maximum(hi, acc["max"])
            tris += doc["accessors"][prim["indices"]]["count"] // 3
    images = [im for im in doc.get("images", []) if "bufferView" in im]
    return {"min": lo, "max": hi, "size": hi - lo, "tris": tris, "images": len(images),
            "materials": len(doc.get("materials", [])), "kib": len(data) / 1024,
            "extras": [k for k in ("cameras", "animations", "lights", "skins") if doc.get(k)]}


def main():
    models = build_models()
    for name, (ob, path) in models.items():
        export(ob, path)
        i = glb_info(path)
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
              f"{i['materials']} material) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()} "
              f"size {np.round(i['size'], 4).tolist()}{' EXTRA ' + str(i['extras']) if i['extras'] else ''}")
    p = write_scrap_stone()
    print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_rocks
        render_rocks.render_all(models, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
