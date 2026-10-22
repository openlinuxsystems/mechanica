"""Build the Soil Bag, the soil mound and the Soil Bag's icon (Soil Design Spec, 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/soil_bag.glb`` - the Soil Bag, 5 x 1 x 3 blocks (1.25 m long, 0.25 m high, 0.75 m
  deep), centred on its origin: a plump printed plastic sack of garden soil, as sold in a shop, from the invented
  brand Rootwell (the artwork is ``bag_print.py``): bright yellow with a green band at one end, "GARDEN SOIL" in big
  green letters and our crops on a bed of soil on top, so it reads as a soil bag at hotbar size. The body pinches
  in toward both ends, where crimped heat seals stick out. Two materials: the printed plastic (a baked box-chart
  atlas with a normal map) and the seals.
* ``mods/vanilla/meshes/soil.glb`` - the soil mound a bag spills into on the ground: 3 x 3 blocks (0.75 m square),
  about half a block (0.125 m) high, sitting on the bottom of its 0.25 m cell. A low rounded mound with a slightly
  raised middle, lumpy clods, soft irregular edges sloping to the ground and a few crumbs at its foot. Its texture is
  the dry look; the game darkens and glosses it for the other wetness states (a material parameter, not a new mesh).
* ``mods/vanilla/textures/items/soil_bag_icon.png`` - the Soil Bag's picture for the hotbar, the inventory and the
  Fabricator (one icon for all), 256 x 256 on a clear background, three-quarter view like the game's other pictures.
  The soil mound has no icon: it is never an item.

Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up (Blender -Y becomes glTF +Z).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/soil/build_soil.py                  # writes the .glb files and the icon
    python tools/soil/build_soil.py --render OUT_DIR # also renders a review sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The geometry and textures are
deterministic (seeded noise only). tools/characters/build_characters.py reads soil_bag.glb for the bag held in hand,
so re-run it after changing the bag.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Vector
from PIL import Image

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "rocks"))
import build_rocks as br  # noqa: E402  (the shared lattice, image, material and export helpers)
import rock_textures as rt  # noqa: E402
import soil_textures as st  # noqa: E402

REPO = HERE.parents[1]
ITEMS = REPO / "mods" / "vanilla" / "meshes" / "items"
MESHES = REPO / "mods" / "vanilla" / "meshes"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

# ---- Dimensions (metres, Blender axes: x long, y deep, z up) ----------------------------------------
BAG_HALF = (0.625, 0.375, 0.125)
BAG_BODY = (0.6, 0.34, 0.09)        # the sack before it bulges
BAG_BULGE = (0.025, 0.035, 0.016, 0.035)   # x, y, top, bottom: how far the middles of the sides bulge out
BAG_RADIUS = 0.045
BAG_PINCH = (0.62, 0.3, 0.06)       # top, bottom, depth: how much the sack narrows into its sealed ends
SEAL = (0.578, 0.624, 0.0045)       # the heat seals: from x, to x, half thickness
MOUND_HALF = 0.375
MOUND_HEIGHT = 0.105                # the rim's top; the crown and the clods add about 0.015
MOUND_RIM = 0.13                    # how far in from the edge the slope reaches the top
MOUND_BASE = -0.125                 # the bottom of the part's cell
BAG_TEX = 2048
MOUND_TEX = 1024


# =====================================================================================================
# The Soil Bag
# =====================================================================================================

def pinch_at(x):
    """0 in the middle of the bag, rising to 1 at its sealed ends."""
    return rt.smoothstep(0.34, BAG_BODY[0] + 0.01, np.abs(x))


def pinched(v):
    """The sack narrowing into its seals: the top drops most (the bag lies on its back), the bottom less."""
    p = pinch_at(v[:, 0])
    out = v.copy()
    out[:, 2] *= 1 - np.where(v[:, 2] > 0, BAG_PINCH[0], BAG_PINCH[1]) * p
    out[:, 1] *= 1 - BAG_PINCH[2] * p
    return out


def seal_z():
    """Height of the middle of the pinched ends, where the seals come out."""
    top, bottom = BAG_BODY[2] * (1 - BAG_PINCH[0]), -BAG_BODY[2] * (1 - BAG_PINCH[1])
    return (top + bottom) / 2


def seals():
    """The two crimped heat seals: thin flaps across the ends, rippled across their width (the crimp), each with
    its own strip of the seal texture (u 0..0.5 the yellow end, 0.5..1 the green end). Returns verts, faces, uvs."""
    x0, x1, t = SEAL
    hy = BAG_BODY[1] * (1 - BAG_PINCH[2]) - 0.018
    zc = seal_z()
    ys = np.linspace(-hy, hy, 97)
    verts, faces, uvs = [], [], []
    for sx in (1, -1):
        u0 = 0.5 if sx > 0 else 0.0
        base = len(verts)
        for j, y in enumerate(ys):
            ripple = 0.0016 * (1 if j % 2 else -1)
            for x in (x0, x1):
                for zs in (1, -1):
                    verts.append((sx * x, float(y), zc + zs * t + ripple))
        n = len(ys)

        def vid(j, xi, zi):
            return base + j * 4 + xi * 2 + zi

        for j in range(n - 1):
            for zi, flip in ((0, False), (1, True)):           # top and bottom sheets
                q = [vid(j, 0, zi), vid(j, 1, zi), vid(j + 1, 1, zi), vid(j + 1, 0, zi)]
                faces.append(q[::-1] if flip != (sx < 0) else q)
            q = [vid(j, 1, 0), vid(j, 1, 1), vid(j + 1, 1, 1), vid(j + 1, 1, 0)]   # the outer edge
            faces.append(q if sx > 0 else q[::-1])
        for j in (0, n - 1):                                    # the two short edges
            q = [vid(j, 0, 0), vid(j, 1, 0), vid(j, 1, 1), vid(j, 0, 1)]
            faces.append(q if (j == 0) == (sx > 0) else q[::-1])
        for f in faces[len(uvs):]:
            uvs.append([(u0 + 0.5 * (j // 4) / (n - 1) * 0.98 + 0.005, 0.5) for j in [k - base for k in f]])
    return verts, faces, uvs


def bulged(v):
    """Points on the plain rounded box moved to the bag's shape: the sides' middles bulge out (the long sides most,
    the top least), the ends pinch in toward the seals, with soft wrinkles in the plastic, never past the bag's box."""
    bx, by, bz = BAG_BODY
    v = np.asarray(v, float)
    u = np.clip(np.abs(v) / np.array(BAG_BODY), 0, 1) ** 2
    out = v.copy()
    out[:, 0] *= 1 + BAG_BULGE[0] / bx * (1 - u[:, 1]) * (1 - u[:, 2])
    out[:, 1] *= 1 + BAG_BULGE[1] / by * (1 - u[:, 0]) * (1 - u[:, 2])
    top = np.where(v[:, 2] > 0, BAG_BULGE[2], BAG_BULGE[3]) / bz
    out[:, 2] *= 1 + top * (1 - u[:, 0]) * (1 - u[:, 1])
    out = pinched(out)
    out *= (1 + 0.006 * rt.Noise3(71).fbm(v, 7.0, octaves=3))[:, None]
    return np.clip(out, -np.array(BAG_HALF), np.array(BAG_HALF))


def bag_body():
    bx, by, bz = BAG_BODY
    rows = (0.005, 0.015, 0.028)
    cx = br.axis_coords(bx, rows, list(np.linspace(-0.52, 0.52, 13)))
    cy = br.axis_coords(by, rows, list(np.linspace(-0.26, 0.26, 8)))
    cz = br.axis_coords(bz, rows, [-0.02, 0.0, 0.02])
    verts, faces = br.rounded_box(BAG_BODY, [cx, cy, cz], BAG_RADIUS)
    return [tuple(float(c) for c in p) for p in bulged(verts)], faces


def tube(path, radius, ring=10, taper=None, closed=False):
    """A tube along `path` (N x 3), `ring` sides; taper(t) scales the radius along it (t 0..1). Returns verts, faces,
    uvs per face corner (u round, v along, one repeat per 0.05 m)."""
    path = np.asarray(path, float)
    n = len(path)
    tang = np.gradient(path, axis=0)
    if closed:
        tang = np.roll(path, -1, 0) - np.roll(path, 1, 0)
    tang /= np.linalg.norm(tang, axis=1, keepdims=True)
    ref = np.array([0.0, 0.0, 1.0]) if abs(tang[0][2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    normal = np.cross(tang[0], ref)
    normal /= np.linalg.norm(normal)
    frames = []
    for i in range(n):                                   # parallel transport
        if i:
            normal = normal - tang[i] * np.dot(normal, tang[i])
            normal /= np.linalg.norm(normal)
        frames.append((normal.copy(), np.cross(tang[i], normal)))
    seg = np.linalg.norm(np.diff(path, axis=0), axis=1)
    along = np.concatenate([[0], np.cumsum(seg)])
    verts, uvs = [], []
    for i in range(n):
        t = i / (n - 1)
        r = radius * (taper(t) if taper else 1.0)
        nn, bb = frames[i]
        for j in range(ring + 1):
            a = 2 * math.pi * j / ring
            verts.append(tuple(path[i] + r * (math.cos(a) * nn + math.sin(a) * bb)))
            uvs.append((j / ring, along[i] / 0.05))
    faces, fuv = [], []
    for i in range(n - 1):
        for j in range(ring):
            a, b = i * (ring + 1) + j, i * (ring + 1) + j + 1
            c, d = b + ring + 1, a + ring + 1
            faces.append([a, b, c, d])
            fuv.append([uvs[a], uvs[b], uvs[c], uvs[d]])
    return verts, faces, fuv


def mesh_object(name, verts, faces, face_uvs, material, smooth_angle=60):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate(clean_customdata=False)
    uv = me.uv_layers.new(name="UVMap")
    out = np.zeros((len(me.loops), 2))
    for poly, corners in zip(me.polygons, face_uvs):
        for li, c in zip(poly.loop_indices, corners):
            out[li] = c
    uv.data.foreach_set("uv", out.ravel())
    me.materials.append(material)
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_bag():
    verts, faces = bag_body()
    charts, size, s, pad = rt.box_charts(BAG_HALF, BAG_TEX, BAG_TEX)
    albedo, normal, _ = rt.bake(charts, size, s, pad, st.PrintedBag(BAG_HALF, 404), normal_strength=1.0, shade=0.3)
    co = np.array(verts)
    face_uvs = []
    for f in faces:
        pts = co[f]
        nrm = np.zeros(3)
        for i in range(len(pts)):                       # Newell normal
            a, b = pts[i], pts[(i + 1) % len(pts)]
            nrm += np.array([(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])])
        c = rt.chart_of_normal(charts, nrm)
        face_uvs.append([rt.chart_uv(c, p, size, s, pad) for p in pts])
    plastic = br.make_material("soil_bag", albedo, normal, rough=0.55)
    body = mesh_object("SoilBag", verts, faces, face_uvs, plastic, smooth_angle=70)

    sv, sf, suv = seals()
    seal_rgb, seal_h = st.seal_texture()
    seal_mat = br.make_material("soil_bag_seal", seal_rgb, st.normal_from_height(seal_h * 0.0006, 2048), rough=0.5)
    seal_mat.use_backface_culling = False            # a thin flap: seen from both sides
    seal = mesh_object("SoilBagSeals", sv, sf, suv, seal_mat, smooth_angle=30)
    # one object, two materials
    for o in bpy.context.scene.objects:
        o.select_set(o in (body, seal))
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    return body


# =====================================================================================================
# The soil mound
# =====================================================================================================

def footprint_inside(x, y):
    """How far (metres) a point is inside the mound's irregular rounded-square edge (negative outside)."""
    corner = 0.1
    qx, qy = np.abs(x) - (MOUND_HALF - corner), np.abs(y) - (MOUND_HALF - corner)
    outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - corner
    wobble = rt.Noise3(907).fbm(np.stack([x.ravel(), y.ravel(), np.zeros(x.size)], -1), 9.0, octaves=3).reshape(x.shape)
    return -outside - 0.012 * (0.5 + 0.5 * wobble) - 0.002


def mound_height(x, y):
    """The mound's height above its base: the rounded slope to the rim, the raised middle, coarse clods."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    d = footprint_inside(x, y)
    t = np.clip(d / MOUND_RIM, 0, 1)
    h = MOUND_HEIGHT * (1 - (1 - t) ** 2.4) ** 0.8
    crown = 0.012 * np.clip(1 - (x * x + y * y) / 0.34 ** 2, 0, 1) * t
    f1, _ = st.worley2(x, y, 0.045, 913)
    g1, _ = st.worley2(x + 0.2, y, 0.024, 917)
    clods = (0.010 * np.clip(1 - (f1 / 0.03) ** 2, 0, 1) + 0.005 * np.clip(1 - (g1 / 0.016) ** 2, 0, 1)) * rt.smoothstep(-0.01, 0.04, d)
    return np.where(d > 0, h + crown + clods, -0.004)


def build_mound():
    n = 76
    g = np.linspace(-MOUND_HALF, MOUND_HALF, n)
    X, Y = np.meshgrid(g, g)
    H = mound_height(X, Y)
    D = footprint_inside(X, Y)
    verts = [(float(X[j, i]), float(Y[j, i]), float(MOUND_BASE + H[j, i])) for j in range(n) for i in range(n)]
    faces = []
    for j in range(n - 1):
        for i in range(n - 1):
            q = [j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i]
            if max(D.flat[k] for k in q) > -0.006:
                faces.append(q)

    def uv_of(p):
        return ((p[0] + MOUND_HALF) / (2 * MOUND_HALF), 1.0 - (p[1] + MOUND_HALF) / (2 * MOUND_HALF))

    # crumbs at the foot of the mound
    rng = np.random.default_rng(929)
    bm = bmesh.new()
    crumbs = 0
    while crumbs < 26:
        p = rng.uniform(-MOUND_HALF, MOUND_HALF, 2)
        r = rng.uniform(0.006, 0.013)
        d = float(footprint_inside(np.array([p[0]]), np.array([p[1]]))[0])
        if not (-0.012 < d < 0.012) or np.max(np.abs(p)) + r > MOUND_HALF - 0.002:
            continue
        crumbs += 1
        made = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
        squash = rng.uniform(0.55, 0.8)
        for v in made["verts"]:
            j = 1 + 0.25 * rng.uniform(-1, 1)
            v.co = Vector((p[0] + v.co.x * j, p[1] + v.co.y * j, MOUND_BASE + max(-0.003, r * 0.35 + v.co.z * squash * j)))
    bm.verts.index_update()
    base = len(verts)
    verts += [tuple(v.co) for v in bm.verts]
    faces += [[base + v.index for v in f.verts] for f in bm.faces]
    bm.free()
    face_uvs = [[uv_of(verts[k]) for k in f] for f in faces]

    albedo, height = st.mound_maps(MOUND_TEX, MOUND_HALF, 941, mound_height)
    px = MOUND_TEX / (2 * MOUND_HALF)
    albedo = st.lit(albedo, height, px, shade=0.45)
    normal = st.normal_from_height(height, px, strength=1.2)
    # row 0 of the maps is y = -half, which is v = 1: the top row of the image, as make_image stores it
    material = br.make_material("soil_mound", albedo, normal, rough=0.95)
    return mesh_object("SoilMound", verts, faces, face_uvs, material, smooth_angle=75)


# =====================================================================================================
# The icon
# =====================================================================================================

def render_icon(bag, path):
    """The Soil Bag alone, three-quarter view, on a clear background (the hotbar and Fabricator picture)."""
    sc = bpy.context.scene
    for o in sc.objects:
        o.hide_render = o is not bag
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 96
    sc.cycles.seed = 7
    sc.cycles.use_denoising = True
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    world = bpy.data.worlds.new("IconWorld")
    sc.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.75, 0.76, 0.8, 1)
    bg.inputs["Strength"].default_value = 0.55
    made = []
    for name, loc, energy, size in (("Key", (-1.2, -2.2, 2.6), 120, 1.6), ("Fill", (2.4, -0.8, 1.2), 40, 2.0),
                                    ("Rim", (0.6, 2.4, 1.8), 70, 1.5)):
        light = bpy.data.lights.new("Icon" + name, "AREA")
        light.energy, light.size = energy, size
        ob = bpy.data.objects.new("Icon" + name, light)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(ob)
        made.append(ob)
    cam = bpy.data.cameras.new("IconCam")
    cam.type = "ORTHO"
    cam.ortho_scale = 1.42
    cob = bpy.data.objects.new("IconCam", cam)
    view = Vector((2.0, -2.0, 1.6)).normalized()      # the game's ItemIcons.ViewFrom (2, 1.6, 2), in Blender axes
    cob.location = view * 6
    cob.rotation_euler = (-view).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cob)
    made.append(cob)
    sc.camera = cob
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.resolution_percentage = 100
    tmp = path.with_suffix(".render.png")
    sc.render.filepath = str(tmp)
    bpy.ops.render.render(write_still=True)
    img = Image.open(tmp).convert("RGBA").resize((256, 256), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    tmp.unlink()
    for ob in made:
        bpy.data.objects.remove(ob)
    for o in sc.objects:
        o.hide_render = False
    return path


# =====================================================================================================
# Main
# =====================================================================================================

def main():
    br.reset_scene()
    bag = build_bag()
    mound = build_mound()
    outputs = {"soil_bag": (bag, ITEMS / "soil_bag.glb"), "soil": (mound, MESHES / "soil.glb")}
    for name, (ob, path) in outputs.items():
        br.export(ob, path)
        i = br.glb_info(path)
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
              f"{i['materials']} materials) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()}")
    icon = render_icon(bag, ICONS / "soil_bag_icon.png")
    print(f"wrote {icon.relative_to(REPO)}")
    if "--render" in sys.argv:
        import render_soil
        render_soil.render_all(outputs, Path(sys.argv[sys.argv.index("--render") + 1]), br)


if __name__ == "__main__":
    main()
