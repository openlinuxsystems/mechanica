"""Build the Scrap Wheel, the Wheel and the Off Road Wheel, their icons and the Scrap Wheel's lumpy collision hull
(Wheel Design Spec, 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/scrap_wheel.glb`` - the Scrap Wheel, 3 x 1 x 3 blocks (0.75 m across, 0.25 m wide): a ring of
  chunky, uneven black rubber blocks with gaps and a flattened spot, weathered wood planks nailed round both faces at
  odd angles and across the tread, rusty plates and straps bolted on at random, and a rust-orange octagonal hub plate
  with mismatched hex bolts round a dark octagonal axle collar.
* ``mods/vanilla/meshes/scrap_wheel_collision.glb`` - its collision: a 13-sided prism whose corners sit at slightly
  different radii (0.35 to 0.372 m), so the wheel thumps as it rolls. The part wraps it in a convex hull.
* ``mods/vanilla/meshes/wheel.glb`` - the Wheel, 3 x 1 x 3 blocks: a round black tyre with two staggered rows of
  rectangular tread blocks and a clean groove round the middle, a thick bright yellow rim with five heavy spokes and
  a silver-grey hub with a bolt circle round the axle hole.
* ``mods/vanilla/meshes/big_wheel.glb`` - the Off Road Wheel, 5 x 2 x 5 blocks (1.25 m across, 0.5 m wide): a tyre
  with deep, staggered hexagonal lugs that reach over the shoulders, a thicker yellow rim with six spokes and a bolted
  lip, and a larger grey hub with a bolt ring and a big octagonal centre nut.
* ``mods/vanilla/textures/items/{scrap_wheel,wheel,big_wheel}_icon.png`` - the pictures for the hotbar, the inventory
  and the Fabricator, 256 x 256 on a clear background: the wheel standing up, three-quarter view.

Each wheel lies with its axle along glTF +Y (Blender +Z), centred on its origin: the hub's back (-Y) is the side that
goes on a bearing's rotor and the spokes face +Y. Units are metres. Materials are flat colours (rubber, yellow paint,
steel) plus small baked textures for the Scrap Wheel's wood and rust; every model keeps strictly inside its part box.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/wheels/build_wheels.py                  # writes the .glb files and the icons
    python tools/wheels/build_wheels.py --render OUT_DIR # also renders a four-view review sheet per wheel into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The shapes and textures are
seeded, so a re-run gives the same wheels (the exported normals and the rendered icons can differ in their last bits).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "rocks"))
import build_rocks as br  # noqa: E402  (the shared image, export and glb-reading helpers)
import rock_textures as rt  # noqa: E402

REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
TAU = 2 * math.pi

# The part boxes (half sizes): radius and half width. Nothing may poke out of them.
SMALL = (0.375, 0.125)
LARGE = (0.625, 0.25)


# =====================================================================================================
# Textures (tileable, 0..1 RGB)
# =====================================================================================================

def _mix(a, b, t):
    return np.asarray(a)[None, None, :] * (1 - t[..., None]) + np.asarray(b)[None, None, :] * t[..., None]


def rubber_tex(size=128, seed=11):
    n = rt.fbm2(seed, 6, size, 3)
    return _mix((0.055, 0.057, 0.062), (0.095, 0.097, 0.104), np.clip(0.5 + 0.6 * n, 0, 1))


def wood_tex(size=256, seed=21):
    """Weathered planks: grain running along u, greyed in patches, a few dark knots and cracks."""
    v = np.linspace(0, 1, size, endpoint=False)[:, None] * np.ones((1, size))
    warp = rt.fnoise2(seed, 3, size)
    grain = 0.5 + 0.5 * np.sin(TAU * (14 * v + 1.2 * warp))
    fine = rt.fnoise2(seed + 1, 48, size, (0.08, 1.0))
    t = np.clip(0.6 * grain + 0.3 * fine + 0.2, 0, 1)
    col = _mix((0.22, 0.13, 0.07), (0.50, 0.33, 0.18), t)
    grey = np.clip(0.5 + rt.fbm2(seed + 2, 4, size, 3), 0, 1) * 0.3
    col = col * (1 - grey[..., None]) + np.array((0.42, 0.39, 0.35)) * grey[..., None]
    cracks = np.abs(rt.fnoise2(seed + 3, 24, size, (0.1, 1.0))) < 0.025
    col[cracks] *= 0.45
    return col


def rust_tex(size=256, seed=31, base=((0.36, 0.17, 0.07), (0.62, 0.31, 0.12)), steel=0.0):
    """Mottled rust; with steel > 0 that much of the surface is bare grey steel showing through."""
    n = rt.fbm2(seed, 5, size, 4)
    col = _mix(base[0], base[1], np.clip(0.5 + 0.8 * n, 0, 1))
    pits = rt.fbm2(seed + 1, 30, size, 2) > 0.35
    col[pits] *= 0.55
    if steel > 0:
        bare = rt.fbm2(seed + 2, 4, size, 3)
        m = np.clip((bare - (0.5 - steel)) * 6, 0, 1)
        grey = _mix((0.30, 0.30, 0.31), (0.47, 0.47, 0.48), np.clip(0.5 + rt.fbm2(seed + 3, 20, size, 2), 0, 1))
        col = col * (1 - m[..., None]) + grey * m[..., None]
    return col


# =====================================================================================================
# Materials
# =====================================================================================================

_MATS: dict[str, bpy.types.Material] = {}


def material(name):
    """The shared materials, made on first use."""
    if name in _MATS:
        return _MATS[name]
    spec = {
        "rubber": dict(tex=lambda: rubber_tex(), rough=0.88),
        "paint": dict(color=(1.0, 0.76, 0.06), rough=0.45),
        "steel": dict(color=(0.55, 0.56, 0.58), rough=0.38, metal=0.85),
        "dark_steel": dict(color=(0.16, 0.165, 0.17), rough=0.55, metal=0.75),
        "bore": dict(color=(0.04, 0.04, 0.045), rough=0.7),
        "wood": dict(tex=lambda: wood_tex(), rough=0.85),
        "rust": dict(tex=lambda: rust_tex(seed=31, steel=0.25), rough=0.7, metal=0.3),
        "rust_orange": dict(tex=lambda: rust_tex(seed=41, base=((0.48, 0.20, 0.06), (0.74, 0.36, 0.12))), rough=0.65,
                            metal=0.25),
        "bolt": dict(tex=lambda: rust_tex(seed=51, base=((0.20, 0.19, 0.18), (0.36, 0.34, 0.32)), steel=0.4),
                     rough=0.5, metal=0.7),
    }[name]
    mat = bpy.data.materials.new("wheel_" + name)
    mat.use_backface_culling = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if "tex" in spec:
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = br.make_image("wheel_" + name + "_color", spec["tex"](), "sRGB")
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*spec["color"], 1)
    bsdf.inputs["Roughness"].default_value = spec["rough"]
    bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0)
    _MATS[name] = mat
    return mat


# =====================================================================================================
# Geometry: pieces built in their own local frame, then mapped into place
# =====================================================================================================

class Wheel:
    """Collects the pieces of one wheel; each piece is bevelled on its own, then all are joined into one object."""

    def __init__(self, name, radius, half_width):
        self.name, self.radius, self.half = name, radius, half_width
        self.objects = []

    def piece(self, mat, verts, faces, place=None, bevel=0.0, uv_scale=0.25, segments=2):
        """Adds a closed mesh. verts are local; place maps an (N, 3) array of them into the wheel (default: as is).
        Box-projected UVs come from the local coordinates (so a plank's grain follows its length)."""
        local = np.asarray(verts, float)
        world = place(local) if place else local
        me = bpy.data.meshes.new(f"{self.name}_{len(self.objects)}")
        me.from_pydata([tuple(map(float, p)) for p in world], [], [list(f) for f in faces])
        me.validate(clean_customdata=False)
        uv = me.uv_layers.new(name="UVMap")
        out = np.zeros((len(me.loops), 2))
        for poly in me.polygons:
            pts = local[list(poly.vertices)]
            nrm = np.zeros(3)
            for i in range(len(pts)):
                a, b = pts[i], pts[(i + 1) % len(pts)]
                nrm += np.cross(a, b)
            k = int(np.argmax(np.abs(nrm)))
            ax = [(1, 2), (0, 2), (0, 1)][k]
            for li in poly.loop_indices:
                p = local[me.loops[li].vertex_index]
                out[li] = (p[ax[0]] / uv_scale, p[ax[1]] / uv_scale)
        uv.data.foreach_set("uv", out.ravel())
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if bevel > 0:
            edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(30)]
            bmesh.ops.bevel(bm, geom=edges, offset=bevel, offset_type="OFFSET", segments=segments, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(material(mat))
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(ob)
        self.objects.append(ob)
        return ob

    def finish(self):
        """Joins the pieces, smooths with sharp edges by angle, and checks the bounds."""
        root = self.objects[0]
        with bpy.context.temp_override(active_object=root, selected_editable_objects=self.objects,
                                       selected_objects=self.objects):
            bpy.ops.object.join()
        root.name = root.data.name = self.name
        bm = bmesh.new()
        bm.from_mesh(root.data)
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.to_mesh(root.data)
        bm.free()
        root.data.shade_smooth()
        root.data.set_sharp_from_angle(angle=math.radians(42))
        co = np.array([v.co[:] for v in root.data.vertices])
        r = np.hypot(co[:, 0], co[:, 1]).max()
        z = np.abs(co[:, 2]).max()
        assert r <= self.radius + 1e-6 and z <= self.half + 1e-6, f"{self.name} leaves its box: r {r:.4f}, z {z:.4f}"
        return root


def revolve(profile, n, radius_of=None):
    """A closed solid of revolution about Z: profile is a closed loop of (r, z); radius_of(angle) scales r per angle
    (for lumpy rings). n segments; a small n gives a faceted ring (8 makes an octagon)."""
    m = len(profile)
    verts, faces = [], []
    for i in range(n):
        a = TAU * i / n
        s = radius_of(a) if radius_of else 1.0
        for r, z in profile:
            verts.append((r * s * math.cos(a), r * s * math.sin(a), z))
    for i in range(n):
        for j in range(m):
            i2, j2 = (i + 1) % n, (j + 1) % m
            faces.append([i * m + j, i2 * m + j, i2 * m + j2, i * m + j2])
    return verts, faces


def prism(poly, h0, h1, taper=1.0):
    """A polygon in local (x, y) extruded along local z from h0 to h1; the top is scaled by taper about the centroid."""
    poly = np.asarray(poly, float)
    c = poly.mean(axis=0)
    top = c + (poly - c) * taper
    n = len(poly)
    verts = [(x, y, h0) for x, y in poly] + [(x, y, h1) for x, y in top]
    faces = [list(range(n))[::-1], list(range(n, 2 * n))]
    faces += [[i, (i + 1) % n, n + (i + 1) % n, n + i] for i in range(n)]
    return verts, faces


def box(hx, hy, hz):
    return prism([(-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)], -hz, hz)


def ngon(r, n, phase=0.0, jitter=None):
    return [((r * (jitter[i] if jitter is not None else 1)) * math.cos(phase + TAU * i / n),
             (r * (jitter[i] if jitter is not None else 1)) * math.sin(phase + TAU * i / n)) for i in range(n)]


def on_tread(angle, radius_scale=1.0):
    """Maps local (s, z, h) - arc length along the tread, across the width, height off the axle - round the wheel,
    centred at `angle`. h is the radius."""
    def place(p):
        a = angle + p[:, 0] / radius_scale
        return np.stack([p[:, 2] * np.cos(a), p[:, 2] * np.sin(a), p[:, 1]], axis=1)
    return place


def on_face(x, y, z, spin=0.0, flip=False):
    """Maps a local piece onto a wheel face: rotated by spin about Z, moved to (x, y, z), mirrored through the
    XY plane for the back face."""
    c, s = math.cos(spin), math.sin(spin)

    def place(p):
        q = np.stack([p[:, 0] * c - p[:, 1] * s + x, p[:, 0] * s + p[:, 1] * c + y, p[:, 2]], axis=1)
        if flip:
            q[:, 2] = -q[:, 2]
        q[:, 2] += z
        return q
    return place


def flip_faces(faces):
    return [f[::-1] for f in faces]


def bolt(w, x, y, z, r, h, spin=0.0, back=False, mat="dark_steel"):
    """A hex bolt head standing on a face at height z, pointing out (+Z, or -Z on the back)."""
    v, f = prism(ngon(r, 6, spin), 0, h, 0.86)
    w.piece(mat, v, f, on_face(x, y, z, flip=back), bevel=r * 0.18, segments=1, uv_scale=0.05)


# =====================================================================================================
# The Wheel
# =====================================================================================================

def build_wheel():
    R, H = SMALL
    w = Wheel("SM_Wheel_Standard", R, H)
    # The tyre: a round carcass with rounded shoulders, its bead tucked over the rim.
    tyre = [(0.245, -0.104), (0.285, -0.116), (0.322, -0.118), (0.341, -0.11), (0.349, -0.094), (0.349, 0.094),
            (0.341, 0.11), (0.322, 0.118), (0.285, 0.116), (0.245, 0.104)]
    w.piece("rubber", *revolve(tyre, 72), bevel=0.004)
    # Tread: two staggered rows of rectangular blocks with a clean groove between, every other block reaching over the
    # shoulder so the silhouette is notched.
    n = 24
    pitch = TAU * 0.35 / n
    for row in (1, -1):
        for i in range(n):
            a = TAU * (i + (0.5 if row < 0 else 0)) / n
            long = i % 2 == 0
            z0, z1 = 0.012, (0.121 if long else 0.106)
            length = pitch * 0.74
            poly = [(-length / 2, z0), (length / 2, z0), (length / 2, z1), (-length / 2, z1)]
            v, f = prism(poly, 0.335, R, 0.9)
            # and a knob down the sidewall under it, longer under the long blocks
            sv, sf = prism([(-length * 0.45, 0.296 if long else 0.318), (length * 0.45, 0.296 if long else 0.318),
                            (length / 2, 0.36), (-length / 2, 0.36)], 0.09, z1, 0.9)
            sv = [(x, h, y) for x, y, h in sv]
            for vv, ff in ((v, f), (sv, flip_faces(sf))):
                if row < 0:
                    vv = [(x, -y, h) for x, y, h in vv]
                    ff = flip_faces(ff)
                w.piece("rubber", vv, ff, on_tread(a, 0.35), bevel=0.005)
    # The rim: a thick yellow ring with a lip over the tyre's bead on the outer face.
    rim = [(0.205, -0.088), (0.25, -0.088), (0.25, 0.09), (0.268, 0.09), (0.268, 0.108), (0.205, 0.108)]
    w.piece("paint", *revolve(rim, 72), bevel=0.004)
    # Five heavy spokes, wider at the rim.
    for k in range(5):
        a = TAU * k / 5 + math.pi / 2
        poly = [(0.06, -0.026), (0.212, -0.04), (0.212, 0.04), (0.06, 0.026)]
        v, f = prism(poly, 0.03, 0.088, 0.92)
        w.piece("paint", v, f, on_face(0, 0, 0, spin=a), bevel=0.008)
    # The hub: a grey barrel from the bearing side, a flange with a bolt circle, and the axle hole through it.
    hub = [(0.024, -H), (0.068, -H), (0.068, 0.05), (0.09, 0.05), (0.09, 0.1), (0.04, 0.1), (0.04, 0.107),
           (0.024, 0.107)]
    w.piece("steel", *revolve(hub, 48), bevel=0.003)
    w.piece("bore", *revolve([(0.0, -0.03), (0.0242, -0.03), (0.0242, -0.025), (0.0, -0.025)], 24))
    for k in range(6):
        a = TAU * k / 6 + 0.2
        bolt(w, 0.064 * math.cos(a), 0.064 * math.sin(a), 0.1, 0.0105, 0.01, spin=a)
    return w.finish()


# =====================================================================================================
# The Off Road Wheel
# =====================================================================================================

def build_off_road_wheel():
    R, H = LARGE
    w = Wheel("SM_Wheel_OffRoad", R, H)
    tyre = [(0.4, -0.21), (0.46, -0.232), (0.515, -0.238), (0.548, -0.225), (0.562, -0.195), (0.562, 0.195),
            (0.548, 0.225), (0.515, 0.238), (0.46, 0.232), (0.4, 0.21)]
    w.piece("rubber", *revolve(tyre, 96), bevel=0.006)
    # Lugs: two staggered rows of big hexagonal knobs that run from past the middle out over the shoulder; the inner
    # points of one row sit in the gaps of the other.
    n = 17
    rr = 0.56
    pitch = TAU * rr / n
    rng = np.random.default_rng(7)
    for row in (1, -1):
        for i in range(n):
            a = TAU * (i + (0.5 if row < 0 else 0)) / n
            hw = pitch * 0.4
            poly = [(-hw * 0.55, -0.02), (hw * 0.55, -0.02), (hw, 0.07), (hw * 1.02, 0.2), (hw * 0.7, 0.246),
                    (-hw * 0.7, 0.246), (-hw * 1.02, 0.2), (-hw, 0.07)]
            poly = [(x + rng.uniform(-0.006, 0.006), y) for x, y in poly]
            v, f = prism(poly, 0.535, R, 0.86)
            if row < 0:
                v = [(x, -y, h) for x, y, h in v]
                f = flip_faces(f)
            w.piece("rubber", v, f, on_tread(a, rr), bevel=0.009)
            # A shoulder knob down the sidewall under each lug, so the side silhouette is toothed.
            sv, sf = prism([(-hw * 0.6, 0.47), (hw * 0.6, 0.47), (hw * 0.75, 0.555), (-hw * 0.75, 0.555)], 0.2, 0.247,
                           0.85)
            sv = [(x, h, y) for x, y, h in sv]      # local (s, z, r): the knob's face is on the sidewall
            sf = flip_faces(sf)
            if row < 0:
                sv = [(x, -y, h) for x, y, h in sv]
                sf = flip_faces(sf)
            w.piece("rubber", sv, sf, on_tread(a, rr), bevel=0.007)
    # Rim: a thicker yellow ring with a lip over the bead, bolted round the lip.
    rim = [(0.335, -0.19), (0.405, -0.19), (0.405, 0.18), (0.428, 0.18), (0.428, 0.212), (0.335, 0.212)]
    w.piece("paint", *revolve(rim, 96), bevel=0.006)
    for k in range(12):
        a = TAU * (k + 0.5) / 12
        bolt(w, 0.385 * math.cos(a), 0.385 * math.sin(a), 0.212, 0.012, 0.011, spin=a, mat="steel")
    # Six heavy spokes.
    for k in range(6):
        a = TAU * k / 6 + math.pi / 2
        poly = [(0.11, -0.04), (0.345, -0.05), (0.345, 0.05), (0.11, 0.04)]
        v, f = prism(poly, 0.07, 0.17, 0.9)
        w.piece("paint", v, f, on_face(0, 0, 0, spin=a), bevel=0.012)
    # Hub: a big grey barrel and flange, a bolt ring and an octagonal centre nut round the axle hole.
    hub = [(0.035, -H), (0.105, -H), (0.105, 0.12), (0.15, 0.12), (0.15, 0.2), (0.035, 0.2)]
    w.piece("steel", *revolve(hub, 64), bevel=0.005)
    nut = [(0.035, 0.19), (0.068, 0.19), (0.068, 0.236), (0.035, 0.236)]
    w.piece("dark_steel", *revolve(nut, 8), bevel=0.005)
    w.piece("bore", *revolve([(0.0, 0.0), (0.0352, 0.0), (0.0352, 0.008), (0.0, 0.008)], 24))
    for k in range(6):
        a = TAU * k / 6 + TAU / 12
        bolt(w, 0.112 * math.cos(a), 0.112 * math.sin(a), 0.2, 0.014, 0.014, spin=a)
    return w.finish()


# =====================================================================================================
# The Scrap Wheel
# =====================================================================================================

def plank(w, rng, length, width, x, y, z, spin, back=False, thick=0.018):
    """A weathered board on a face (grain along its length), with a nail head near each end."""
    hx, hy = length / 2, width / 2
    poly = [(-hx + rng.uniform(0, 0.01), -hy), (hx - rng.uniform(0, 0.012), -hy), (hx, hy), (-hx, hy)]
    v, f = prism(poly, 0, thick + rng.uniform(-0.003, 0.003), 1.0)
    w.piece("wood", v, f, on_face(x, y, z, spin=spin, flip=back), bevel=0.003, segments=1, uv_scale=0.3)
    c, s = math.cos(spin), math.sin(spin)
    top = z + (-1 if back else 1) * thick
    for d in (-hx + 0.018, hx - 0.018):
        bolt(w, x + c * d, y + s * d, top, 0.0075, 0.005, spin=rng.uniform(0, 1), back=back, mat="bolt")


def plate(w, rng, length, width, x, y, z, spin, back=False, bolts=2):
    """A rusty scrap plate bolted down."""
    hx, hy = length / 2, width / 2
    j = rng.uniform(-0.006, 0.006, 4)
    poly = [(-hx + j[0], -hy), (hx, -hy + j[1]), (hx + j[2], hy), (-hx, hy + j[3])]
    v, f = prism(poly, 0, 0.007, 1.0)
    w.piece("rust", v, f, on_face(x, y, z, spin=spin, flip=back), bevel=0.0015, segments=1, uv_scale=0.15)
    c, s = math.cos(spin), math.sin(spin)
    top = z + (-1 if back else 1) * 0.007
    for k in range(bolts):
        d = (k - (bolts - 1) / 2) * (length - 0.03) / max(bolts - 1, 1)
        bolt(w, x + c * d, y + s * d, top, 0.008, 0.006, spin=rng.uniform(0, 1), back=back, mat="bolt")


def build_scrap_wheel():
    R, H = SMALL
    w = Wheel("SM_Wheel_Scrap", R, H)
    rng = np.random.default_rng(1234)
    # The body: a lumpy, faceted rubber disc with stepped rings on both faces and an axle hole.
    lumps = rng.uniform(0.95, 1.02, 13)

    def lumpy(a):
        k = a / TAU * 13
        i = int(k) % 13
        t = k - int(k)
        return lumps[i] * (1 - t) + lumps[(i + 1) % 13] * t
    body = [(0.06, -0.06), (0.12, -0.06), (0.12, -0.08), (0.16, -0.08), (0.16, -0.095), (0.312, -0.095),
            (0.326, -0.075), (0.326, 0.075), (0.312, 0.095), (0.16, 0.095), (0.16, 0.08), (0.12, 0.08),
            (0.12, 0.06), (0.06, 0.06)]
    w.piece("rubber", *revolve(body, 26, lumpy), bevel=0.005)
    # Tread: chunky blocks in two columns, uneven heights, with gaps and a flattened run.
    n = 17
    skip = {3, 11}                                   # gaps (boards go across the tread there)
    flat = {7, 8}                                    # a worn, flattened spot
    boards = []
    for i in range(n):
        a0 = TAU * i / n
        if i in skip:
            boards.append(a0)
            continue
        for row in (1, -1):
            a = a0 + rng.uniform(-0.04, 0.04) + (0.5 * TAU / n * 0.25 if row < 0 else 0)
            length = TAU * 0.33 / n * rng.uniform(0.74, 0.9)
            top = 0.335 if i in flat else rng.uniform(0.352, 0.372)
            z0, z1 = rng.uniform(0.004, 0.012), rng.uniform(0.104, 0.117)
            poly = [(-length / 2, z0), (length / 2, z0 + rng.uniform(-0.004, 0.004)), (length / 2, z1),
                    (-length / 2 + rng.uniform(0, 0.01), z1)]
            v, f = prism(poly, 0.3, top, rng.uniform(0.82, 0.9))
            if row < 0:
                v = [(x, -y, h) for x, y, h in v]
                f = flip_faces(f)
            w.piece("rubber", v, f, on_tread(a, 0.33), bevel=0.008, segments=1)
    # Boards nailed across the tread in the gaps, with a strap over one of them.
    for k, a in enumerate(boards):
        v, f = prism([(-0.035, -0.115), (0.035, -0.115), (0.035, 0.115), (-0.035, 0.115)], 0.3, 0.348, 1.0)
        w.piece("wood", v, f, on_tread(a, 0.33), bevel=0.003, segments=1, uv_scale=0.3)
        if k == 0:
            sv, sf = prism([(-0.014, -0.118), (0.014, -0.118), (0.014, 0.118), (-0.014, 0.118)], 0.347, 0.354, 1.0)
            w.piece("rust", sv, sf, on_tread(a + 0.02, 0.33), bevel=0.0015, segments=1, uv_scale=0.15)
    # Boards round both faces at odd angles, then rusty plates on top of some.
    for back, count, seed_off in ((False, 13, 0.0), (True, 10, 0.3)):
        for k in range(count):
            a = TAU * (k + seed_off + rng.uniform(-0.15, 0.15)) / count
            r = rng.uniform(0.215, 0.255)
            length = rng.uniform(0.12, 0.18)
            spin = a + math.pi / 2 + rng.uniform(-0.45, 0.45)
            z = 0.094
            plank(w, rng, length, rng.uniform(0.05, 0.068), r * math.cos(a), r * math.sin(a), -z if back else z,
                  spin, back=back)
        for k in range(4 if not back else 3):
            a = TAU * (k + 0.37 + rng.uniform(-0.1, 0.1)) / (4 if not back else 3)
            r = rng.uniform(0.2, 0.26)
            plate(w, rng, rng.uniform(0.06, 0.09), rng.uniform(0.04, 0.055), r * math.cos(a), r * math.sin(a),
                  (-1 if back else 1) * 0.0965, a + rng.uniform(-0.6, 0.6), back=back, bolts=int(rng.integers(1, 3)))
    # The hub: a rust-orange octagonal plate with mismatched hex bolts, round a dark octagonal collar with the hole.
    hub = ngon(0.13, 8, math.pi / 8, rng.uniform(0.93, 1.04, 8))
    v, f = prism(hub, 0.0, 0.022, 0.97)
    w.piece("rust_orange", v, f, on_face(0, 0, 0.06), bevel=0.004, uv_scale=0.2)
    v, f = prism(ngon(0.1, 8, 0.2), 0.0, 0.016, 0.97)
    w.piece("rust_orange", v, f, on_face(0, 0, 0.06, flip=True), bevel=0.004, uv_scale=0.2)
    for k in range(6):
        a = TAU * k / 6 + rng.uniform(-0.12, 0.12)
        r = rng.uniform(0.085, 0.098)
        size = rng.uniform(0.0095, 0.014)
        bolt(w, r * math.cos(a), r * math.sin(a), 0.082, size, rng.uniform(0.008, 0.014), spin=rng.uniform(0, 1),
             mat="bolt")
    collar = [(0.032, -H), (0.058, -H), (0.058, H - 0.004), (0.032, H - 0.004)]
    w.piece("dark_steel", *revolve(collar, 8), bevel=0.004)
    return w.finish()


def build_scrap_collision():
    """A 13-sided prism, corners at slightly different radii: convex, but not round."""
    R, H = SMALL
    rng = np.random.default_rng(99)
    radii = rng.uniform(0.35, 0.372, 13)
    poly = [(radii[i] * math.cos(TAU * i / 13), radii[i] * math.sin(TAU * i / 13)) for i in range(13)]
    v, f = prism(poly, -H, H)
    me = bpy.data.meshes.new("SM_Wheel_Scrap_Collision")
    me.from_pydata(v, [], f)
    me.validate()
    ob = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# =====================================================================================================
# Pictures
# =====================================================================================================

def studio(film_transparent, backdrop=(0.5, 0.5, 0.52)):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 96
    sc.cycles.seed = 7
    sc.cycles.use_denoising = True
    sc.render.film_transparent = film_transparent
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    world = bpy.data.worlds.get("WheelWorld") or bpy.data.worlds.new("WheelWorld")
    sc.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (*backdrop, 1)
    bg.inputs["Strength"].default_value = 0.6
    made = []
    for name, loc, energy, size in (("Key", (-1.2, -2.2, 2.6), 160, 1.6), ("Fill", (2.4, -0.8, 1.2), 55, 2.0),
                                    ("Rim", (0.6, 2.4, 1.8), 90, 1.5)):
        light = bpy.data.lights.new("Wheel" + name, "AREA")
        light.energy, light.size = energy, size
        ob = bpy.data.objects.new("Wheel" + name, light)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(ob)
        made.append(ob)
    return made


def shoot(ob, view, ortho, path, px):
    """One orthographic picture of ob from direction view (Blender axes), framed by ortho metres."""
    sc = bpy.context.scene
    cam = bpy.data.cameras.new("WheelCam")
    cam.type = "ORTHO"
    cam.ortho_scale = ortho
    cob = bpy.data.objects.new("WheelCam", cam)
    view = Vector(view).normalized()
    cob.location = view * 6
    cob.rotation_euler = (-view).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cob)
    sc.camera = cob
    sc.render.resolution_x = sc.render.resolution_y = px
    sc.render.resolution_percentage = 100
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cob)
    return path


def solo(ob):
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            o.hide_render = o is not ob


def stand_up(ob, turn):
    """Stands the wheel on its tread (axle horizontal), spokes towards -Y turned by `turn` radians."""
    ob.rotation_euler = (math.pi / 2, 0, turn)


def render_icon(ob, path):
    """The wheel standing up, three-quarter view, on a clear background (the hotbar and Fabricator picture)."""
    solo(ob)
    made = studio(True)
    stand_up(ob, math.radians(28))
    tmp = path.with_suffix(".render.png")
    radius = max(ob.dimensions) / 2
    shoot(ob, (2.0, -2.0, 1.6), radius * 2.6, tmp, 512)   # the game's ItemIcons.ViewFrom (2, 1.6, 2), in Blender axes
    img = Image.open(tmp).convert("RGBA").resize((256, 256), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    tmp.unlink()
    for o in made:
        bpy.data.objects.remove(o)
    ob.rotation_euler = (0, 0, 0)
    return path


def render_sheet(ob, label, out):
    """Front, side, three-quarter and back views on a grey backdrop, like the reference sheets."""
    solo(ob)
    made = studio(False)
    radius = max(ob.dimensions) / 2
    views = [("front", (0, 0), (0, -1, 0)), ("side", (math.pi / 2, 0), (0, -1, 0)),
             ("three_quarter", (math.radians(30), 0), (0.25, -1, 0.3)), ("back", (math.pi, 0), (0, -1, 0))]
    tiles = []
    for name, (turn, _), view in views:
        stand_up(ob, turn)
        tiles.append(Image.open(shoot(ob, view, radius * 2.5, out / f"{label}_{name}.png", 600)).convert("RGB"))
    ob.rotation_euler = (0, 0, 0)
    sheet = Image.new("RGB", (1200, 1240), (120, 120, 124))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 2) * 600, 40 + (i // 2) * 600))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("DejaVuSans.ttf", 22)
    except OSError:
        font = ImageFont.load_default()
    d.text((14, 8), f"{label}: {2 * radius:.2f} m across", fill=(240, 240, 240), font=font)
    sheet.save(out / f"{label}_sheet.png", optimize=True)
    for o in made:
        bpy.data.objects.remove(o)


# =====================================================================================================
# Main
# =====================================================================================================

def main():
    br.reset_scene()
    wheels = {"scrap_wheel": build_scrap_wheel(), "wheel": build_wheel(), "big_wheel": build_off_road_wheel()}
    collision = build_scrap_collision()
    for name, ob in [*wheels.items(), ("scrap_wheel_collision", collision)]:
        path = MESHES / f"{name}.glb"
        br.export(ob, path)
        i = br.glb_info(path)
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
              f"{i['materials']} materials) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()}")
    collision.hide_render = True
    for name, ob in wheels.items():
        icon = render_icon(ob, ICONS / f"{name}_icon.png")
        print(f"wrote {icon.relative_to(REPO)}")
    if "--render" in sys.argv:
        out = Path(sys.argv[sys.argv.index("--render") + 1])
        out.mkdir(parents=True, exist_ok=True)
        for name, ob in wheels.items():
            render_sheet(ob, name, out)
            print(f"rendered {name} into {out}")


if __name__ == "__main__":
    main()
