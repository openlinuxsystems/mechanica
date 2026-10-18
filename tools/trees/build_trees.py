"""Build the harvestable trees, their log segments and the Wood Rod (Tree Harvesting spec).

What it builds (procedurally, from scratch: no .blend inputs, no downloads), stylised low-poly with plain colour
materials, matching the spec's reference images. Rounded surfaces (trunks, canopies, logs) are smooth-shaded and only
hard edges (log ends, needle blades, pine plates) stay crisp (``SMOOTH_ANGLE``):

* ``mods/vanilla/meshes/world/birch_tree.glb`` - small Birch: thin white trunk with black bark marks, thin brown
  branches, drooping clusters of yellow-orange faceted blobs. About 8 m tall.
* ``mods/vanilla/meshes/world/leafy_tree.glb`` - medium Leafy: brown trunk on a root flare that forks into branches
  under a full rounded canopy of green faceted blobs. About 9 m tall.
* ``mods/vanilla/meshes/world/spruce_tree.glb`` - medium Spruce: slim brown trunk, tall cone of drooping needle blades
  in tiers. About 11 m tall.
* ``mods/vanilla/meshes/world/pine_tree.glb`` - large Pine: thick trunk on a root flare, dense stacked skirts of
  chunky green plates. About 13 m tall.
* ``meshes/world/<tree>_lod.glb`` - a far version of each (a few hundred triangles), drawn past the LOD distance.
* ``mods/vanilla/meshes/items/birch_log.glb``, ``wood_log.glb``, ``pine_log.glb`` - log segments: faceted octagonal
  logs with light radial end grain, long axis along glTF Z.
* ``mods/vanilla/meshes/items/wood_rod.glb`` - the Wood Rod: a rough hewn bar, 0.22 x 0.22 x 0.98 m, along glTF Z.

Every tree stands with its ground point at the origin; the trunk goes into the ground a little below it so trees sit
on slopes. Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/trees/build_trees.py                  # writes the .glb files
    python tools/trees/build_trees.py --render OUT_DIR # also renders a review sheet and a small forest into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded random numbers only), so running it twice gives the same files.
"""

from __future__ import annotations

import math
import struct
import json
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first)
import bmesh
import numpy as np

sys.dont_write_bytecode = True

REPO = Path(__file__).resolve().parents[2]
# Neighbouring faces meeting at less than this are shaded as one smooth surface; sharper edges stay hard.
SMOOTH_ANGLE = math.radians(62)
WORLD = REPO / "mods" / "vanilla" / "meshes" / "world"
ITEMS = REPO / "mods" / "vanilla" / "meshes" / "items"


# =====================================================================================================
# Colours and materials
# =====================================================================================================

def srgb(hex_colour):
    """#rrggbb as linear RGB (Blender colours are linear)."""
    c = [int(hex_colour[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


PALETTE = {
    "birch_bark": ("#ece9e1", 0.85),
    "birch_shade": ("#c9c4ba", 0.85),
    "birch_mark": ("#1d1c1b", 0.9),
    "twig": ("#6b4528", 0.9),
    "birch_leaf_a": ("#f4c414", 0.75),
    "birch_leaf_b": ("#eba413", 0.75),
    "birch_leaf_c": ("#f08c12", 0.75),
    "bark": ("#7b4f2c", 0.9),
    "bark_dark": ("#64401f", 0.9),
    "leafy_a": ("#8fd033", 0.75),
    "leafy_b": ("#72bd2b", 0.75),
    "leafy_c": ("#58a027", 0.8),
    "spruce_bark": ("#6d4326", 0.9),
    "spruce_a": ("#79a646", 0.8),
    "spruce_b": ("#678f3b", 0.8),
    "spruce_c": ("#577d32", 0.8),
    "pine_bark": ("#734a2a", 0.9),
    "pine_a": ("#6c9a48", 0.8),
    "pine_b": ("#5a873c", 0.8),
    "pine_c": ("#4b7433", 0.8),
    "end_grain": ("#d8a466", 0.85),
    "end_ring": ("#c08848", 0.85),
    "rod_wood": ("#c48b50", 0.85),
    "rod_dark": ("#a8723e", 0.85),
}

_materials = {}


def material(name):
    if name not in _materials:
        hex_colour, rough = PALETTE[name]
        mat = bpy.data.materials.new(name)
        mat.use_backface_culling = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*srgb(hex_colour), 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = 0.0
        _materials[name] = mat
    return _materials[name]


# =====================================================================================================
# Geometry builder
# =====================================================================================================

class Mesh:
    """Triangles and quads with a material each, joined into one object at the end."""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []

    def add(self, verts, faces, mat):
        base = len(self.verts)
        self.verts.extend(tuple(float(c) for c in v) for v in verts)
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
            self.mats.append(mat)

    def triangles(self):
        return sum(len(f) - 2 for f in self.faces)


def frame(axis):
    """Two unit vectors at right angles to `axis` (and each other)."""
    axis = np.asarray(axis, float)
    axis = axis / np.linalg.norm(axis)
    helper = np.array([0.0, 0.0, 1.0]) if abs(axis[2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(axis, helper)
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    return axis, u, v


def tube(m, mat, base, axis, length, radii, sides, rng, *, jitter=0.0, twist=0.0, cap_top=True, cap_bottom=False,
         top_mat=None, bottom_mat=None, side_mats=None, flare=None):
    """A faceted tapered tube from `base` along `axis`. `radii` lists the radius at each ring (evenly spaced along the
    length). `flare(k, ring)` may scale a ring's vertices one by one (roots). Caps are fans round a middle point."""
    axis, u, v = frame(axis)
    base = np.asarray(base, float)
    rings = len(radii)
    verts = []
    for r_i, radius in enumerate(radii):
        t = r_i / (rings - 1)
        centre = base + axis * length * t
        for k in range(sides):
            a = 2 * math.pi * k / sides + twist * r_i
            rad = radius * (1 + rng.uniform(-jitter, jitter))
            if flare is not None:
                rad *= flare(k, r_i)
            verts.append(centre + (u * math.cos(a) + v * math.sin(a)) * rad)
    faces, mats = [], []
    for r_i in range(rings - 1):
        for k in range(sides):
            a, b = r_i * sides + k, r_i * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
            mats.append(side_mats[(r_i + k) % len(side_mats)] if side_mats else mat)
    for f, fm in zip(faces, mats):
        m.add([verts[i] for i in f], [(0, 1, 2, 3)], fm)
    if cap_top:
        top = base + axis * (length + (radii[-1] * 0.15))
        ring = verts[(rings - 1) * sides:]
        for k in range(sides):
            m.add([ring[k], ring[(k + 1) % sides], top], [(0, 1, 2)], top_mat or mat)
    if cap_bottom:
        bottom = base - axis * radii[0] * 0.08
        ring = verts[:sides]
        for k in range(sides):
            m.add([ring[(k + 1) % sides], ring[k], bottom], [(0, 1, 2)], bottom_mat or mat)


ICO_CACHE = {}


def icosphere(subdiv):
    """Unit icosphere: vertices, triangles."""
    if subdiv in ICO_CACHE:
        return ICO_CACHE[subdiv]
    t = (1 + 5 ** 0.5) / 2
    verts = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
             (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    verts = [np.array(v, float) / np.linalg.norm(v) for v in verts]
    faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6),
             (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10),
             (8, 6, 7), (9, 8, 1)]
    for _ in range(subdiv):
        cache, new = {}, []

        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                p = verts[a] + verts[b]
                verts.append(p / np.linalg.norm(p))
                cache[key] = len(verts) - 1
            return cache[key]

        for a, b, c in faces:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            new += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        faces = new
    ICO_CACHE[subdiv] = (np.array(verts), faces)
    return ICO_CACHE[subdiv]


def blob(m, mats, centre, radii, rng, *, subdiv=1, jitter=0.12, droop=0.0):
    """A faceted lump: an icosphere scaled to `radii`, its vertices pushed in and out; the lower half pulled down by
    `droop` (a hanging birch cluster). It takes one colour from `mats`."""
    verts, faces = icosphere(subdiv)
    centre = np.asarray(centre, float)
    out = []
    for p in verts:
        q = p * np.asarray(radii, float) * (1 + rng.uniform(-jitter, jitter))
        if droop and p[2] < 0:
            q[2] *= 1 + droop * (-p[2])
        out.append(centre + q)
    # One colour per lump: the light shades it, so a hard colour seam across it would only look jagged.
    mat = mats[int(rng.integers(0, len(mats)))]
    for f in faces:
        m.add([out[i] for i in f], [(0, 1, 2)], mat)


def blade(m, mat, root, direction, length, width, droop, rng, ridge=0.12):
    """A spruce needle blade: a ridged, drooping leaf-shaped wedge from the trunk outwards."""
    d = np.array([direction[0], direction[1], 0.0])
    d /= np.linalg.norm(d)
    side = np.array([-d[1], d[0], 0.0])
    root = np.asarray(root, float)
    tip = root + d * length + np.array([0, 0, -droop * length])
    midp = root + d * length * 0.5 + np.array([0, 0, -droop * length * 0.42])
    w = width * length * (1 + rng.uniform(-0.1, 0.1))
    left, right = midp + side * w, midp - side * w
    top = midp + np.array([0, 0, ridge * length])
    bottom = midp - np.array([0, 0, 0.05 * length])
    v = [root, tip, left, right, top, bottom]
    m.add(v, [(0, 2, 4), (0, 4, 3), (2, 1, 4), (4, 1, 3), (0, 5, 2), (0, 3, 5), (2, 5, 1), (5, 3, 1)], mat)


def plate(m, mat, angle, half_width, inner, outer, z_inner, z_outer, thick):
    """A pine skirt plate: a thick trapezoid from radius `inner` at height `z_inner` out to `outer` at `z_outer`,
    `half_width` radians wide, with a short upturned lip, as in the reference's chunky layers."""
    def at(r, a, z):
        return np.array([r * math.cos(a), r * math.sin(a), z])
    hw_in, hw_out = half_width * 0.75, half_width
    top = [at(inner, angle - hw_in, z_inner), at(outer, angle - hw_out, z_outer), at(outer, angle + hw_out, z_outer),
           at(inner, angle + hw_in, z_inner)]
    lip = [at(outer * 1.04, angle - hw_out * 0.98, z_outer - thick), at(outer * 1.04, angle + hw_out * 0.98, z_outer - thick)]
    bottom = [p - np.array([0, 0, thick]) for p in (top[0], top[3])]
    v = top + lip + bottom
    # top, front lip, underside, two sides
    m.add(v, [(0, 1, 2, 3), (1, 4, 5, 2), (6, 7, 5, 4), (0, 6, 4, 1), (3, 2, 5, 7)], mat)


def mark(m, mat, centre_axis_z, radius, angle, width, height):
    """A black birch bark mark: a thin dash hugging the trunk."""
    r = radius * 1.03
    a0, a1 = angle - width / radius / 2, angle + width / radius / 2
    z0, z1 = centre_axis_z - height / 2, centre_axis_z + height / 2
    pts = [np.array([r * math.cos(a), r * math.sin(a), z]) for a, z in ((a0, centre_axis_z), (angle, z0), (a1, centre_axis_z), (angle, z1))]
    m.add(pts, [(0, 1, 2, 3)], mat)


# =====================================================================================================
# Trees (ground point at the origin, Z up)
# =====================================================================================================

def birch(lod=False):
    rng = np.random.default_rng(11)
    m = Mesh()
    bark, dark, twig = material("birch_bark"), material("birch_mark"), material("twig")
    leaves = [material("birch_leaf_a"), material("birch_leaf_b"), material("birch_leaf_c")]
    height = 7.2
    sides = 7 if lod else 10
    radii = [0.32, 0.2, 0.17, 0.15, 0.13, 0.11, 0.09, 0.07] if not lod else [0.3, 0.16, 0.12, 0.07]
    tube(m, bark, (0, 0, -0.4), (0, 0, 1), height + 0.4, radii, sides, rng, jitter=0.0 if lod else 0.03)
    if lod:
        for k in range(7):
            a = 2 * math.pi * k / 7
            r = 1.4 if k % 2 else 1.0
            blob(m, leaves, (r * math.cos(a), r * math.sin(a), 5.0 - 0.3 * (k % 2)), (0.95, 0.95, 1.45), rng, subdiv=1, jitter=0.05)
        blob(m, leaves, (0, 0, 6.6), (1.2, 1.2, 1.0), rng, subdiv=1, jitter=0.05)
        return m
    # Black marks up the trunk.
    def trunk_radius(z):
        f = min(max((z + 0.4) / (height + 0.4) * (len(radii) - 1), 0), len(radii) - 1.001)
        i = int(f)
        return radii[i] + (radii[i + 1] - radii[i]) * (f - i)
    for z in np.arange(0.6, 6.0, 0.32):
        for _ in range(int(rng.integers(1, 3))):
            mark(m, dark, z + rng.uniform(-0.08, 0.08), trunk_radius(z) * 1.04, rng.uniform(0, 2 * math.pi),
                 rng.uniform(0.09, 0.16), rng.uniform(0.05, 0.08))
    # Branches out to drooping clusters, all the way round.
    tips = []
    for k in range(11):
        a = 2 * math.pi * k * 0.381966 + rng.uniform(-0.2, 0.2)   # golden-angle spiral
        z0 = 3.4 + 0.25 * k + rng.uniform(-0.2, 0.2)
        reach = 1.9 - 0.09 * k + rng.uniform(-0.2, 0.2)
        d = np.array([math.cos(a), math.sin(a), 0.55 + 0.06 * k])
        length = reach * math.sqrt(1 + d[2] ** 2)
        start = np.array([0, 0, z0])
        tube(m, twig, start, d, length, [0.05, 0.02], 6, rng, cap_top=True)
        tips.append(start + d / np.linalg.norm(d) * length)
    for tip in tips:
        # A hanging chain of blobs under each branch tip, longer lower down.
        n = int(rng.integers(3, 5)) if tip[2] < 6.2 else 2
        for i in range(n):
            out = 1.0 + 0.06 * i
            c = np.array([tip[0] * out, tip[1] * out, tip[2] - 0.45 * i + 0.25])
            size = 0.5 - 0.05 * i
            blob(m, leaves, c, (size, size, size * 1.15), rng, jitter=0.07, droop=0.35)
    # Crown and fill.
    for _ in range(18):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 1.3)
        z = rng.uniform(5.4, 7.8)
        s = rng.uniform(0.42, 0.6)
        blob(m, leaves, (r * math.cos(a), r * math.sin(a), z), (s, s, s * 1.1), rng, jitter=0.07, droop=0.25)
    return m


def roots(m, mat, rng, count, radius, height, spread):
    for k in range(count):
        a = 2 * math.pi * k / count + rng.uniform(-0.2, 0.2)
        d = np.array([math.cos(a) * spread, math.sin(a) * spread, -1.0])
        start = np.array([0, 0, height])
        tube(m, mat, start, d, math.sqrt(spread ** 2 + 1) * height * 1.05, [radius, radius * 0.75, radius * 0.25], 7, rng,
             jitter=0.04, cap_top=True)


def leafy(lod=False):
    rng = np.random.default_rng(23)
    m = Mesh()
    bark = material("bark")
    greens = [material("leafy_a"), material("leafy_b"), material("leafy_c")]
    sides = 7 if lod else 11
    tube(m, bark, (0, 0, -0.4), (0, 0, 1), 4.0, [0.5, 0.4, 0.34, 0.3, 0.27] if not lod else [0.45, 0.3, 0.27], sides, rng,
         jitter=0.0 if lod else 0.03)
    if not lod:
        roots(m, bark, rng, 5, 0.32, 0.8, 1.1)
    # Forked branches up into the canopy.
    ends = []
    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.2, 0.2)
        d = np.array([math.cos(a) * 0.75, math.sin(a) * 0.75, 1.0])
        start = np.array([0, 0, 3.2 + rng.uniform(-0.25, 0.3)])
        length = 2.4 + rng.uniform(-0.3, 0.3)
        if not lod:
            tube(m, bark, start, d, length, [0.2, 0.13, 0.08], 7, rng, jitter=0.03)
        ends.append(start + d / np.linalg.norm(d) * length)
    if not lod:
        tube(m, bark, (0, 0, 3.4), (0.05, 0.0, 1), 2.8, [0.24, 0.14, 0.08], 7, rng, jitter=0.03)
    # Canopy: big faceted blobs over a dome.
    centres = [np.array([0, 0, 7.4])]
    centres += [e + np.array([0, 0, 0.6]) for e in ends]
    for k in range(7):
        a = 2 * math.pi * (k + 0.5) / 7
        centres.append(np.array([2.3 * math.cos(a), 2.3 * math.sin(a), 5.5 + 0.2 * (k % 2)]))
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        centres.append(np.array([1.3 * math.cos(a), 1.3 * math.sin(a), 7.0]))
    for c in centres:
        s = rng.uniform(1.05, 1.35)
        blob(m, greens, c, (s, s, s * 0.85), rng, subdiv=1 if lod else 2, jitter=0.05 if not lod else 0.0)
    return m


def spruce(lod=False):
    rng = np.random.default_rng(37)
    m = Mesh()
    bark = material("spruce_bark")
    greens = [material("spruce_a"), material("spruce_b"), material("spruce_c")]
    height = 11.0
    tube(m, bark, (0, 0, -0.4), (0, 0, 1), height + 0.2, [0.3, 0.22, 0.17, 0.12, 0.08, 0.05], 7 if lod else 10, rng,
         jitter=0.0 if lod else 0.03)
    if lod:
        # Stacked cones.
        for i, (z, r) in enumerate([(1.4, 2.6), (3.6, 2.1), (5.8, 1.6), (7.8, 1.1), (9.6, 0.6)]):
            tube(m, greens[i % 3], (0, 0, z), (0, 0, 1), 2.6, [r, 0.05], 9, rng, cap_bottom=True)
        return m
    tiers = 13
    for t in range(tiers):
        f = t / (tiers - 1)
        z = 1.9 + f * (height - 2.3)
        length = 2.5 * (1 - f) + 0.55
        count = 7 if f < 0.6 else (6 if f < 0.85 else 5)
        for k in range(count):
            a = 2 * math.pi * k / count + (t % 2) * math.pi / count + rng.uniform(-0.15, 0.15)
            root = np.array([0.12 * math.cos(a), 0.12 * math.sin(a), z + 0.35])
            blade(m, greens[(t + k) % 3], root, (math.cos(a), math.sin(a)), length, 0.36, 0.55 + rng.uniform(-0.08, 0.08), rng)
    # Leader on top.
    tube(m, greens[0], (0, 0, height - 0.6), (0, 0, 1), 1.3, [0.32, 0.03], 5, rng, cap_bottom=True)
    return m


def pine(lod=False):
    rng = np.random.default_rng(53)
    m = Mesh()
    bark = material("pine_bark")
    greens = [material("pine_a"), material("pine_b"), material("pine_c")]
    tube(m, bark, (0, 0, -0.5), (0, 0, 1), 12.0, [0.72, 0.55, 0.47, 0.4, 0.3, 0.2, 0.1], 8 if lod else 12, rng,
         jitter=0.0 if lod else 0.03)
    if lod:
        for i, (z, r) in enumerate([(3.6, 3.4), (5.6, 2.8), (7.6, 2.1), (9.4, 1.5), (11.0, 0.9)]):
            tube(m, greens[i % 3], (0, 0, z), (0, 0, 1), 2.4, [r, 0.08], 10, rng, cap_bottom=True)
        return m
    roots(m, bark, rng, 7, 0.38, 1.1, 1.3)
    tiers = 8
    for t in range(tiers):
        f = t / (tiers - 1)
        z = 4.6 + f * 7.0
        outer = 3.4 * (1 - f) + 0.95
        count = 9 if f < 0.5 else 7
        for k in range(count):
            a = 2 * math.pi * k / count + (t % 2) * math.pi / count
            plate(m, greens[(k + t) % 3], a, math.pi / count * 1.05, 0.2, outer, z + 0.35, z - 0.55 - 0.12 * outer, 0.24)
    tube(m, greens[0], (0, 0, 11.2), (0, 0, 1), 1.6, [0.75, 0.05], 7, rng, cap_bottom=True)
    return m


# =====================================================================================================
# Logs and the rod (long axis along Blender Y, so glTF Z; centred on the origin)
# =====================================================================================================

def log(radius, length, bark_mats, seed, marks=False):
    rng = np.random.default_rng(seed)
    m = Mesh()
    sides = 12
    rings = 4
    radii = [radius * (1 + rng.uniform(-0.03, 0.03)) for _ in range(rings)]
    end, ring = material("end_grain"), material("end_ring")
    tube(m, bark_mats[0], (0, -length / 2, 0), (0, 1, 0), length, radii, sides, rng, jitter=0.02,
         cap_top=False, cap_bottom=False)
    # End grain: an inner ring and a fan of facets, slightly hollowed, at each end.
    axis, u, v = frame((0, 1, 0))
    for sign, r_end in ((-1, radii[0]), (1, radii[-1])):
        y = sign * length / 2
        outer = [np.array([0, y, 0]) + (u * math.cos(2 * math.pi * k / sides) + v * math.sin(2 * math.pi * k / sides)) * r_end
                 for k in range(sides)]
        inner = [np.array([0, y + sign * -0.01, 0]) + (p - np.array([0, y, 0])) * 0.78 for p in outer]
        centre = np.array([0, y - sign * 0.025, 0])
        for k in range(sides):
            a, b = k, (k + 1) % sides
            quad = [outer[a], outer[b], inner[b], inner[a]] if sign > 0 else [outer[b], outer[a], inner[a], inner[b]]
            m.add(quad, [(0, 1, 2, 3)], ring)
            tri = [inner[a], inner[b], centre] if sign > 0 else [inner[b], inner[a], centre]
            m.add(tri, [(0, 1, 2)], end)
    if marks:
        dark = material("birch_mark")
        for y in np.arange(-length / 2 + 0.15, length / 2 - 0.1, 0.3):
            for _ in range(2):
                a = rng.uniform(0, 2 * math.pi)
                w = rng.uniform(0.07, 0.12)
                r = radius * 1.06
                a0, a1 = a - w / r / 2, a + w / r / 2
                pts = [np.array([r * math.cos(a0), y, r * math.sin(a0)]), np.array([r * math.cos(a), y - 0.035, r * math.sin(a)]),
                       np.array([r * math.cos(a1), y, r * math.sin(a1)]), np.array([r * math.cos(a), y + 0.035, r * math.sin(a)])]
                m.add(pts, [(3, 2, 1, 0)], dark)
    return m


def wood_rod():
    rng = np.random.default_rng(71)
    m = Mesh()
    wood, end = material("rod_wood"), material("end_grain")
    tube(m, wood, (0, -0.49, 0), (0, 1, 0), 0.98, [0.13, 0.135, 0.128, 0.132], 10, rng, jitter=0.03,
         cap_top=True, cap_bottom=True, top_mat=end, bottom_mat=end)
    return m


# =====================================================================================================
# Export
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _materials.clear()


def to_object(name, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata(m.verts, [], m.faces)
    me.validate(clean_customdata=False)
    mats = []
    for mat in m.mats:
        if mat not in mats:
            mats.append(mat)
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", [mats.index(mat) for mat in m.mats])
    # Faces are added with their own corners: weld shared corners so rounded surfaces can shade smoothly.
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=SMOOTH_ANGLE)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def export(ob, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=False, export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False,
        export_extras=False, export_cameras=False, export_lights=False)


def glb_info(path):
    data = path.read_bytes()
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    acc = doc["accessors"]
    tris = sum(acc[p["indices"]]["count"] // 3 for mesh in doc["meshes"] for p in mesh["primitives"])
    pos = [acc[p["attributes"]["POSITION"]] for mesh in doc["meshes"] for p in mesh["primitives"]]
    lo = np.min([a["min"] for a in pos], axis=0)
    hi = np.max([a["max"] for a in pos], axis=0)
    return tris, lo, hi


MODELS = {
    "birch_tree": (WORLD, lambda: birch()),
    "birch_tree_lod": (WORLD, lambda: birch(lod=True)),
    "leafy_tree": (WORLD, lambda: leafy()),
    "leafy_tree_lod": (WORLD, lambda: leafy(lod=True)),
    "spruce_tree": (WORLD, lambda: spruce()),
    "spruce_tree_lod": (WORLD, lambda: spruce(lod=True)),
    "pine_tree": (WORLD, lambda: pine()),
    "pine_tree_lod": (WORLD, lambda: pine(lod=True)),
    "birch_log": (ITEMS, lambda: log(0.12, 1.45, [material("birch_bark"), material("birch_bark"), material("birch_shade")], 3, marks=True)),
    "wood_log": (ITEMS, lambda: log(0.235, 1.45, [material("bark"), material("bark_dark")], 5)),
    "pine_log": (ITEMS, lambda: log(0.35, 1.2, [material("pine_bark"), material("bark_dark")], 7)),
    "wood_rod": (ITEMS, wood_rod),
}


def build(names=None):
    reset_scene()
    built = {}
    for name, (folder, make) in MODELS.items():
        if names and name not in names:
            continue
        built[name] = (to_object(name, make()), folder / f"{name}.glb")
    return built


def main():
    built = build()
    for name, (ob, path) in built.items():
        export(ob, path)
        tris, lo, hi = glb_info(path)
        size = hi - lo
        print(f"{path.relative_to(REPO)}: {tris} triangles, {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, {path.stat().st_size // 1024} KiB")
    if "--render" in sys.argv:
        out = Path(sys.argv[sys.argv.index("--render") + 1])
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import render_trees
        render_trees.render(out, built)


if __name__ == "__main__":
    main()
