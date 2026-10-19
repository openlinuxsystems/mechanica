#!/usr/bin/env python3
"""Build the harvestable trees, their log segments and the Wood Rod (Tree Harvesting spec; 2026-10-19 redesign).

Stylised to sit with the player characters and the textured blocks and rocks: smooth, gently curving trunks with
painted bark, and soft canopies made of painted leaf-cluster cards round a solid core, shaded as one rounded mass.
Everything is built procedurally from seeded random numbers (no .blend inputs, no downloads) and written straight
to glTF with numpy and Pillow, so the build needs no Blender:

* ``mods/vanilla/meshes/world/birch_tree.glb`` - Birch: a slim, slightly bent white trunk with black dashes, thin
  upswept limbs, an airy upright crown of golden leaf clumps. About 8 m tall.
* ``leafy_tree.glb`` - Leafy: a stout trunk on a root flare that forks into four limbs under a broad, rounded crown of
  green leaf clumps. About 9 m tall.
* ``autumn_leafy_tree.glb`` - the Leafy tree in orange and red (Autumn Forest).
* ``burnt_tree.glb`` - a charred snag: the Leafy trunk broken off, cracked black bark, bare crooked limbs (Burned
  Forest).
* ``spruce_tree.glb`` - Spruce: a slim straight trunk in a narrow cone of drooping, dark blue-green needle boughs that
  reach almost to the ground. About 11 m tall.
* ``pine_tree.glb`` - Pine: a big red-brown plated trunk, bare to about 4.5 m, then whorls of branches ending in
  rounded, upturned needle clumps, broad at the bottom and narrowing to a crown. About 13 m tall.
* ``meshes/world/<tree>_lod.glb`` - a far version of each (fewer cards, simpler trunk, smaller textures), drawn past
  the LOD distance.
* ``mods/vanilla/meshes/items/birch_log.glb``, ``wood_log.glb``, ``pine_log.glb`` - log segments in their tree's
  bark with ringed end grain, long axis along glTF Z (sizes unchanged: they match the log parts' colliders).
* ``mods/vanilla/meshes/items/wood_rod.glb`` - the Wood Rod: a rough hewn bar, 0.22 x 0.22 x 0.98 m, along glTF Z.

Every tree stands with its ground point at the origin; the trunk goes into the ground a little below it so trees sit
on slopes. Sizes keep to the colliders and fall heights in ``mods/vanilla/world/worldgen.json``. Materials: bark is a
base colour plus normal map; foliage is an alpha-masked leaf atlas (four tiles from sunlit to shaded); each canopy
core is a plain colour. Textures are painted by tree_textures.py and embedded in each file.

How to run:  python3 tools/trees/build_trees.py    (needs numpy and pillow)

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic,
so running it twice gives the same files. ``tools/capture-trees.sh`` shows the result in game.
"""

from __future__ import annotations

import io
import json
import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import tree_textures as tex  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
WORLD = REPO / "mods" / "vanilla" / "meshes" / "world"
ITEMS = REPO / "mods" / "vanilla" / "meshes" / "items"
UP = np.array([0.0, 0.0, 1.0])


def unit(v):
    v = np.asarray(v, float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


# =====================================================================================================
# Geometry: primitives keyed by material
# =====================================================================================================

class Model:
    """Triangles grouped by material. Built Z up (``zup``) and written Y up, or built straight in glTF axes."""

    def __init__(self, zup=True):
        self.zup = zup
        self.prims = {}

    def prim(self, mat):
        if mat not in self.prims:
            self.prims[mat] = {"p": [], "n": [], "uv": [], "t": [], "i": []}
        return self.prims[mat]

    def add(self, mat, pos, nrm, uv, tris, tan=None):
        p = self.prim(mat)
        base = len(p["p"])
        p["p"].extend(np.asarray(pos, float))
        p["n"].extend(unit(n) for n in nrm)
        p["uv"].extend(np.asarray(uv, float))
        if tan is not None:
            p["t"].extend(np.asarray(tan, float))
        p["i"].extend((base + a, base + b, base + c) for a, b, c in tris)

    def triangles(self):
        return sum(len(p["i"]) for p in self.prims.values())


def frames(path):
    """Parallel-transported (tangent, u, v) frames along a polyline."""
    path = np.asarray(path, float)
    out = []
    t = unit(path[1] - path[0])
    helper = np.array([1.0, 0.0, 0.0]) if abs(t[0]) < 0.9 else np.array([0.0, 1.0, 0.0])
    u = unit(np.cross(t, helper))
    for i in range(len(path)):
        if i == 0:
            t_new = unit(path[1] - path[0])
        elif i == len(path) - 1:
            t_new = unit(path[-1] - path[-2])
        else:
            t_new = unit(path[i + 1] - path[i - 1])
        u = unit(u - t_new * np.dot(u, t_new))
        out.append((t_new, u, np.cross(t_new, u)))
    return out


def tube(m, mat, path, radii, sides, *, tile=1.2, around=None, flare=None, tip=True, wobble=None):
    """A smooth tube along `path` (one radius per point), with bark UVs: U wraps `around` times (default from the
    girth, so texels stay square), V runs up `tile` metres a repeat. `flare(angle, i)` scales a ring point (roots);
    `wobble(angle, i)` too (knots). A closing point caps the top when `tip`."""
    path = np.asarray(path, float)
    fr = frames(path)
    girth = 2 * math.pi * max(radii[0], 0.02)
    around = around or max(1, round(girth / tile))
    lengths = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))])
    pos, nrm, uv, tan, tris = [], [], [], [], []
    for i, (c, r, (t, u, v)) in enumerate(zip(path, radii, fr)):
        # Taper tilts the surface normal toward the narrowing end.
        j0, j1 = max(0, i - 1), min(len(path) - 1, i + 1)
        slope = (radii[j1] - radii[j0]) / max(lengths[j1] - lengths[j0], 1e-6)
        for k in range(sides + 1):
            a = 2 * math.pi * k / sides
            d = u * math.cos(a) + v * math.sin(a)
            rr = r
            if flare is not None:
                rr *= flare(a, i)
            if wobble is not None:
                rr *= wobble(a, i)
            pos.append(c + d * rr)
            nrm.append(unit(d - t * slope))
            uv.append((around * k / sides, -lengths[i] / tile))
            tan.append((*(-u * math.sin(a) + v * math.cos(a)), 1.0))
    ring = sides + 1
    for i in range(len(path) - 1):
        for k in range(sides):
            a, b = i * ring + k, i * ring + k + 1
            tris += [(a, b, b + ring), (a, b + ring, a + ring)]
    if tip:
        t = fr[-1][0]
        top = len(pos)
        pos.append(path[-1] + t * radii[-1] * 0.6)
        nrm.append(t)
        uv.append((around * 0.5, -(lengths[-1] + radii[-1]) / tile))
        tan.append((*fr[-1][1], 1.0))
        last = (len(path) - 1) * ring
        tris += [(last + k, last + k + 1, top) for k in range(sides)]
    m.add(mat, pos, nrm, uv, tris, tan)


def curve(start, direction, length, bend, segments, rng, sway=0.0):
    """Points along a gently bending limb: `bend` pulls it toward world up (or down when negative) as it goes."""
    p = np.asarray(start, float)
    d = unit(direction)
    pts = [p.copy()]
    step = length / segments
    side = unit(np.cross(d, UP)) if abs(d[2]) < 0.99 else np.array([1.0, 0, 0])
    for s in range(segments):
        d = unit(d + UP * bend / segments + side * rng.uniform(-sway, sway))
        p = p + d * step
        pts.append(p.copy())
    return pts


ICO = {}


def icosphere(subdiv):
    if subdiv in ICO:
        return ICO[subdiv]
    t = (1 + 5 ** 0.5) / 2
    verts = [unit(v) for v in [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t),
                               (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]]
    faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6),
             (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10),
             (8, 6, 7), (9, 8, 1)]
    for _ in range(subdiv):
        cache, new = {}, []

        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                verts.append(unit(verts[a] + verts[b]))
                cache[key] = len(verts) - 1
            return cache[key]

        for a, b, c in faces:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            new += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        faces = new
    ICO[subdiv] = (np.array(verts), faces)
    return ICO[subdiv]


class Canopy:
    """A crown's overall shape, for shading: a point's normal leans toward the outward direction from its middle, so
    every clump and card shades as part of one rounded mass (the stylised soft look)."""

    def __init__(self, centre, radii):
        self.centre = np.asarray(centre, float)
        self.radii = np.asarray(radii, float)

    def normal(self, p, local, weight=0.65):
        g = unit((np.asarray(p) - self.centre) / self.radii ** 2)
        return unit(g * weight + unit(local) * (1 - weight))


def core(m, mat, canopy, centre, radii, rng, subdiv=1, jitter=0.08):
    """A solid clump inside the cards so the crown reads full and never see-through."""
    verts, faces = icosphere(subdiv)
    centre = np.asarray(centre, float)
    pos, nrm = [], []
    for v in verts:
        p = centre + v * radii * (1 + rng.uniform(-jitter, jitter))
        pos.append(p)
        nrm.append(canopy.normal(p, v))
    m.add(mat, pos, nrm, [(0.5, 0.5)] * len(pos), [(a, b, c) for a, b, c in faces])


def card(m, mat, canopy, centre, normal, up, w, h, tone, *, flip_v=False, lean=0.0):
    """A leaf-cluster card: a quad showing one atlas tile, written front and back (each one-sided) so it shades the
    same from both sides. Its top edge points along `up`. `lean` bends the card's lower half back (curved cards)."""
    n = unit(normal)
    up = unit(np.asarray(up, float) - n * np.dot(up, n))
    right = np.cross(up, n)
    c = np.asarray(centre, float)
    u0, v0, u1, v1 = tex.tile_uv(tone)
    if flip_v:
        v0, v1 = v1, v0
    # Top and bottom rows; `lean` sets the bottom edge back a little so the card curves away from the eye.
    rows = [(0.5, 0.0), (-0.5, lean)]
    pos, uv = [], []
    for y, back in rows:
        for x in (-0.5, 0.5):
            pos.append(c + right * x * w + up * y * h - n * back * h)
            uv.append((u0 + (x + 0.5) * (u1 - u0), v0 + (0.5 - y) * (v1 - v0)))
    nrm = [canopy.normal(p, n * 0.6 + unit(p - c) * 0.4 if np.linalg.norm(p - c) > 0 else n) for p in pos]
    front = [(0, 2, 3), (0, 3, 1)]
    back = [(a, c_, b) for a, b, c_ in front]
    m.add(mat, pos, nrm, uv, front)
    m.add(mat, pos, nrm, uv, back)


def tone_for(height_frac, outward_z, rng):
    """Atlas tile from how sunlit a card is: high and upward-facing cards light, low and downward ones dark."""
    light = 0.5 * height_frac + 0.45 * (outward_z * 0.5 + 0.5) - 0.08 + rng.uniform(-0.15, 0.15)
    return int(np.clip(3 - light * 4, 0, 3))


def fibonacci(count, rng):
    off = rng.uniform(0, 1)
    pts = []
    for i in range(count):
        z = 1 - 2 * (i + 0.5) / count
        r = math.sqrt(max(0, 1 - z * z))
        a = math.pi * (3 - math.sqrt(5)) * i + off * 2 * math.pi
        pts.append(np.array([r * math.cos(a), r * math.sin(a), z]))
    return pts


def leaf_clump(m, canopy, centre, radius, rng, cards, *, squash=0.85, size=1.05, lo=0.0, hi=1.0, droop=0.0,
               core_mat="core", core_scale=0.72, core_subdiv=1, skip_below=-0.75):
    """A broadleaf clump: a solid core and `cards` leaf cards round it, facing outward, their twigs toward the middle.
    `lo`..`hi` is the crown's height range (for light and dark tiles)."""
    centre = np.asarray(centre, float)
    core(m, core_mat, canopy, centre, np.array([radius, radius, radius * squash]) * core_scale, rng, subdiv=core_subdiv)
    for d in fibonacci(cards, rng):
        if d[2] < skip_below:
            continue
        d = unit(d + rng.uniform(-0.2, 0.2, 3))
        p = centre + d * radius * np.array([1, 1, squash]) * rng.uniform(0.66, 0.9)
        n = unit(d + rng.uniform(-0.35, 0.35, 3))
        up = unit(UP * (1 - droop) + d * 0.35 + rng.uniform(-0.3, 0.3, 3))
        s = radius * size * rng.uniform(0.85, 1.15)
        frac = (p[2] - lo) / max(hi - lo, 1e-6)
        m_tone = tone_for(frac, d[2], rng)
        card(m, "leaves", canopy, p, n, up, s, s, m_tone, lean=0.12)


# =====================================================================================================
# Trees (ground point at the origin, Z up)
# =====================================================================================================

def trunk_path(height, bottom, segments, rng, sway, lean=(0.0, 0.0)):
    pts = []
    phase = rng.uniform(0, 2 * math.pi)
    for i in range(segments + 1):
        t = i / segments
        z = bottom + (height - bottom) * t
        bend = sway * math.sin(t * math.pi * 1.4 + phase) * t
        pts.append(np.array([lean[0] * t * t + bend, lean[1] * t * t + bend * 0.5, z]))
    return pts


def lerp_radii(profile, n):
    xs = np.linspace(0, 1, len(profile))
    return list(np.interp(np.linspace(0, 1, n), xs, profile))


def roots_flare(count, amount, rings, rng):
    phase = rng.uniform(0, 2 * math.pi)
    def f(a, i):
        if i >= rings:
            return 1.0
        k = (1 - i / rings) ** 2
        return 1 + amount * k * max(0.0, math.cos(count * (a + phase))) ** 3 + 0.25 * amount * k
    return f


def birch(lod=False):
    rng = np.random.default_rng(11)
    m = Model()
    height = 7.8
    path = trunk_path(height, -0.4, 6 if lod else 14, rng, 0.18, lean=(0.25, 0.1))
    tube(m, "bark", path, lerp_radii([0.24, 0.19, 0.16, 0.14, 0.12, 0.1, 0.07, 0.04], len(path)), 6 if lod else 12,
         tile=1.0, flare=None if lod else roots_flare(4, 0.35, 3, rng))
    canopy = Canopy((0.2, 0.05, 5.9), (1.9, 1.9, 2.4))
    clumps = [(np.array([0.3, 0.1, 7.7]), 0.95)]
    for k in range(8):
        a = 2 * math.pi * k * 0.381966 + rng.uniform(-0.2, 0.2)
        z0 = 3.4 + 0.42 * k + rng.uniform(-0.15, 0.15)
        reach = 1.55 - 0.07 * k + rng.uniform(-0.15, 0.15)
        d = np.array([math.cos(a), math.sin(a), 1.3 + 0.08 * k])
        start = np.interp(z0, [p[2] for p in path], np.arange(len(path)))
        base = path[int(start)] * (1 - start % 1) + path[min(int(start) + 1, len(path) - 1)] * (start % 1)
        limb = curve(base, d, reach * 1.35, 0.25, 2 if lod else 4, rng, sway=0.15)
        if not lod:
            tube(m, "bark", limb, lerp_radii([0.06, 0.035, 0.015], len(limb)), 6, tile=0.8, around=1)
        clumps.append((limb[-1] + np.array([0, 0, 0.15]), 0.82 - 0.02 * k + rng.uniform(-0.08, 0.08)))
    for c, r in clumps:
        leaf_clump(m, canopy, c, r, rng, 10 if lod else 32, size=1.1, lo=3.6, hi=8.6, droop=0.25, core_scale=0.55,
                   core_subdiv=1)
    return m


def leafy(lod=False, seed=23):
    rng = np.random.default_rng(seed)
    m = Model()
    path = trunk_path(3.6, -0.4, 5 if lod else 10, rng, 0.08)
    tube(m, "bark", path, lerp_radii([0.5, 0.42, 0.38, 0.36, 0.34], len(path)), 7 if lod else 14, tile=1.3,
         flare=roots_flare(5, 0.9, 4 if not lod else 2, rng), tip=False)
    canopy = Canopy((0.0, 0.0, 6.3), (3.0, 3.0, 2.3))
    clumps = []
    top = path[-1]
    # Four limbs fork from the top of the trunk, each into a pair of clumps; a leader carries the crown's top.
    for k in range(4):
        a = 2 * math.pi * k / 4 + rng.uniform(-0.25, 0.25) + 0.4
        d = np.array([math.cos(a) * 0.9, math.sin(a) * 0.9, 1.0])
        limb = curve(top - np.array([0, 0, 0.3]), d, 2.5 + rng.uniform(-0.2, 0.2), 0.35, 2 if lod else 5, rng, sway=0.1)
        tube(m, "bark", limb, lerp_radii([0.24, 0.17, 0.11, 0.06], len(limb)), 5 if lod else 9, tile=1.0)
        mid = limb[len(limb) // 2]
        side = unit(np.cross(d, UP))
        twig = curve(mid, d * 0.6 + side * 0.8 + UP * 0.4, 1.3, 0.3, 2, rng)
        if not lod:
            tube(m, "bark", twig, lerp_radii([0.08, 0.04, 0.02], len(twig)), 6, tile=0.8, around=1)
        clumps.append((limb[-1] + np.array([0, 0, 0.3]), 1.45 + rng.uniform(-0.1, 0.1)))
        clumps.append((twig[-1] + np.array([0, 0, 0.1]), 1.15 + rng.uniform(-0.1, 0.1)))
    leader = curve(top - np.array([0, 0, 0.2]), (0.05, -0.04, 1), 3.4, 0.0, 2 if lod else 4, rng, sway=0.05)
    tube(m, "bark", leader, lerp_radii([0.22, 0.14, 0.06], len(leader)), 5 if lod else 9, tile=1.0)
    clumps.append((leader[-1] + np.array([0, 0, 0.2]), 1.6))
    for k in range(5):
        a = 2 * math.pi * (k + 0.5) / 5 + rng.uniform(-0.2, 0.2)
        clumps.append((np.array([1.5 * math.cos(a), 1.5 * math.sin(a), 6.9 + rng.uniform(-0.2, 0.3)]), 1.25))
    for c, r in clumps:
        leaf_clump(m, canopy, c, r, rng, 12 if lod else 40, size=0.95, lo=4.2, hi=8.9, core_scale=0.62)
    return m


def burnt(lod=False):
    """A charred snag: the Leafy trunk snapped off at the top, with bare, crooked limbs."""
    rng = np.random.default_rng(29)
    m = Model()
    path = trunk_path(5.1, -0.4, 5 if lod else 11, rng, 0.1)
    radii = lerp_radii([0.48, 0.4, 0.36, 0.33, 0.29, 0.25], len(path))
    # The snapped top: a jagged rim instead of a closed cap.
    tube(m, "bark", path, radii, 7 if lod else 13, tile=1.2, flare=roots_flare(5, 0.85, 4 if not lod else 2, rng),
         tip=False, wobble=None if lod else (lambda a, i: 1 + 0.05 * math.sin(a * 3 + i)))
    rim_c = path[-1]
    pos, nrm, uv, tris = [], [], [], []
    for k in range(13):
        a = 2 * math.pi * k / 13
        d = np.array([math.cos(a), math.sin(a), 0])
        pos += [rim_c + d * radii[-1], rim_c + d * radii[-1] * 0.4 + np.array([0, 0, rng.uniform(0.1, 0.7)])]
        nrm += [d + UP * 0.3, d + UP]
        uv += [(k / 13, 0), (k / 13, -0.3)]
    for k in range(13):
        a, b = 2 * k, 2 * ((k + 1) % 13)
        tris += [(a, b, b + 1), (a, b + 1, a + 1)]
    centre = len(pos)
    pos.append(rim_c + np.array([0, 0, 0.35]))
    nrm.append(UP)
    uv.append((0.5, -0.2))
    tris += [(2 * k + 1, 2 * ((k + 1) % 13) + 1, centre) for k in range(13)]
    m.add("bark", pos, nrm, uv, tris, [(1, 0, 0, 1)] * len(pos))

    def limb(start, d, length, radius, depth):
        pts = curve(start, d, length, rng.uniform(-0.1, 0.4), 2 if lod else 4, rng, sway=0.35)
        tube(m, "bark", pts, lerp_radii([radius, radius * 0.6, radius * 0.25], len(pts)), 4 if lod else 7, tile=0.9,
             around=1)
        if depth > 0 and not lod:
            for _ in range(2):
                j = int(rng.integers(1, len(pts) - 1))
                nd = unit(unit(pts[j + 1] - pts[j]) + rng.uniform(-0.9, 0.9, 3) + UP * 0.3)
                limb(pts[j], nd, length * 0.5, radius * 0.5, depth - 1)

    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3)
        z = 2.8 + 0.45 * k
        i = int(np.interp(z, [p[2] for p in path], np.arange(len(path))))
        d = np.array([math.cos(a), math.sin(a), 0.6 + rng.uniform(0, 0.5)])
        limb(path[i], d, 2.0 - 0.15 * k, 0.15, 2)
    return m


def spruce(lod=False):
    rng = np.random.default_rng(37)
    m = Model()
    height = 11.2
    path = trunk_path(height, -0.4, 6 if lod else 12, rng, 0.02)
    tube(m, "bark", path, lerp_radii([0.3, 0.24, 0.18, 0.13, 0.08, 0.04], len(path)), 6 if lod else 11, tile=0.9,
         flare=None if lod else roots_flare(5, 0.4, 2, rng))
    canopy = Canopy((0, 0, 5.6), (2.3, 2.3, 6.0))
    # A dark cone inside the boughs so the tree reads solid between them.
    cone_path = [np.array([0, 0, z]) for z in np.linspace(1.3, height + 0.2, 7 if lod else 12)]
    cone_r = [max(0.05, 0.75 * (1 - (p[2] - 1.3) / (height - 1.1)) ** 1.05) for p in cone_path]
    pos, nrm = [], []
    tube_m = Model()
    tube(tube_m, "core", cone_path, cone_r, 8 if lod else 12, tile=2.0)
    for p, n in zip(tube_m.prims["core"]["p"], tube_m.prims["core"]["n"]):
        pos.append(p)
        nrm.append(canopy.normal(p, n + UP * 0.4))
    m.add("core", pos, nrm, tube_m.prims["core"]["uv"], tube_m.prims["core"]["i"])
    whorls = 10 if lod else 22
    for w in range(whorls):
        f = w / (whorls - 1)
        z = 1.5 + f * (height - 1.9)
        length = 2.25 * (1 - f) ** 0.95 + 0.4
        count = (5 if lod else 8) if f < 0.7 else (4 if lod else 6)
        for k in range(count):
            a = 2 * math.pi * k / count + (w % 2) * math.pi / count + rng.uniform(-0.2, 0.2)
            out = np.array([math.cos(a), math.sin(a), 0.0])
            droop = 0.45 + rng.uniform(-0.08, 0.08)
            d = unit(out - UP * droop)
            mid = np.array([0, 0, z]) + out * 0.15 + d * length * 0.5
            frac = z / height
            # A spray lying along the bough (seen from above and below), and one standing across it (seen side on).
            flat_n = unit(UP + out * 0.35)
            card(m, "leaves", canopy, mid, flat_n, -d, length * 1.0, length * 1.15, tone_for(frac, 0.6, rng),
                 lean=-0.1)
            if not lod or w % 2 == 0:
                side = np.cross(UP, out)
                card(m, "leaves", canopy, mid - UP * 0.05, side, -d, length * 0.75, length * 1.1, tone_for(frac, 0.0, rng))
    # The leader: a little upright spray at the very top.
    top = np.array([0, 0, height + 0.15])
    for k in range(2):
        a = k * math.pi / 2 + 0.3
        card(m, "leaves", canopy, top, np.array([math.cos(a), math.sin(a), 0]), -UP, 0.55, 1.1, 0)
    return m


def pine(lod=False):
    rng = np.random.default_rng(53)
    m = Model()
    height = 12.6
    path = trunk_path(height, -0.5, 6 if lod else 14, rng, 0.12, lean=(0.15, -0.1))
    tube(m, "bark", path, lerp_radii([0.66, 0.56, 0.5, 0.45, 0.39, 0.32, 0.24, 0.14, 0.06], len(path)), 8 if lod else 16,
         tile=1.5, flare=roots_flare(6, 0.7, 4 if not lod else 2, rng))
    canopy = Canopy((0, 0, 8.2), (3.4, 3.4, 4.4))
    zs = [p[2] for p in path]

    def at(z):
        i = np.interp(z, zs, np.arange(len(path)))
        a, f = int(i), i % 1
        return path[a] * (1 - f) + path[min(a + 1, len(path) - 1)] * f

    def tuft_clump(c, r):
        core(m, "core", canopy, c, np.array([r, r, r * 0.42]) * 0.66, rng, subdiv=0 if lod else 1)
        n = 5 if lod else 16
        for d in fibonacci(n * 2, rng)[:n]:          # the upper half of the sphere only: tufts point up and out
            d = unit(d + rng.uniform(-0.2, 0.2, 3))
            p = c + d * np.array([r, r, r * 0.45]) * 0.72
            nrm = unit(d * np.array([1, 1, 0.5]) + rng.uniform(-0.3, 0.3, 3))
            up = unit(UP + d * 0.6)
            s = r * rng.uniform(1.0, 1.3)
            card(m, "leaves", canopy, p, nrm, up, s * 1.1, s, tone_for((p[2] - 4.5) / 8.5, d[2], rng), lean=0.1)

    whorls = 7
    for w in range(whorls):
        f = w / (whorls - 1)
        z = 4.6 + f * 6.4
        length = 2.3 * (1 - f) + 0.6
        count = (4 if lod else 6) if f < 0.5 else (3 if lod else 5)
        for k in range(count):
            a = 2 * math.pi * k / count + w * 0.9 + rng.uniform(-0.25, 0.25)
            d = np.array([math.cos(a), math.sin(a), 0.25 + 0.2 * f])
            branch = curve(at(z), d, length, 0.3, 2 if lod else 4, rng, sway=0.1)
            if not lod or f < 0.5:
                tube(m, "bark", branch, lerp_radii([0.15 * (1 - f * 0.5), 0.09, 0.04], len(branch)), 5 if lod else 7,
                     tile=1.0, around=1)
            r = 0.8 * (1 - f) + 0.55
            tuft_clump(branch[-1] + np.array([0, 0, 0.15]), r)
            if f < 0.2 and not lod:
                tuft_clump(branch[len(branch) // 2] + np.array([0, 0, 0.25]), r * 0.75)
    tuft_clump(at(height - 0.4) + np.array([0, 0, 0.3]), 1.0)
    tuft_clump(at(height - 1.2) + np.array([0.4, 0.2, 0.0]), 0.85)
    return m


# =====================================================================================================
# Logs and the rod (built straight in glTF axes: long axis along Z, centred on the origin)
# =====================================================================================================

def end_cap(m, mat, centre, axis, radius, sides, inset=0.0):
    axis = unit(axis)
    _, u, v = frames([np.zeros(3), axis])[0]
    pos, nrm, uv = [np.asarray(centre) - axis * inset], [axis], [(0.5, 0.5)]
    for k in range(sides + 1):
        a = 2 * math.pi * k / sides
        d = u * math.cos(a) + v * math.sin(a)
        pos.append(np.asarray(centre) + d * radius)
        nrm.append(axis)
        uv.append((0.5 + 0.5 * math.cos(a), 0.5 - 0.5 * math.sin(a)))
    tris = [(0, k + 1, k + 2) for k in range(sides)]
    m.add(mat, pos, nrm, uv, tris)


def log(radius, length, seed):
    rng = np.random.default_rng(seed)
    m = Model(zup=False)
    sides = 16
    path = [np.array([0, 0, z]) for z in np.linspace(-length / 2, length / 2, 5)]
    radii = [radius * (1 + rng.uniform(-0.03, 0.03)) for _ in path]
    tube(m, "bark", path, radii, sides, tile=1.0 if radius > 0.2 else 0.7, tip=False,
         wobble=lambda a, i: 1 + 0.025 * math.sin(a * 5 + i * 1.7))
    end_cap(m, "end", path[-1], (0, 0, 1), radii[-1] * 1.03, sides, inset=0.0)
    end_cap(m, "end", path[0], (0, 0, -1), radii[0] * 1.03, sides, inset=0.0)
    return m


def wood_rod():
    rng = np.random.default_rng(71)
    m = Model(zup=False)
    path = [np.array([0, 0, z]) for z in np.linspace(-0.47, 0.47, 4)]
    radii = [0.13, 0.135, 0.128, 0.132]
    tube(m, "bark", path, radii, 10, tile=0.8, around=1, tip=False, wobble=lambda a, i: 1 + rng.uniform(-0.03, 0.03))
    end_cap(m, "end", np.array([0, 0, 0.49]), (0, 0, 1), 0.12, 10)
    end_cap(m, "end", np.array([0, 0, -0.49]), (0, 0, -1), 0.12, 10)
    # Close the gap between the tube ends and the slightly set-out caps with a bevel ring.
    for sign in (1, -1):
        pos, nrm, uv, tris = [], [], [], []
        for k in range(11):
            a = 2 * math.pi * k / 10
            d = np.array([math.cos(a), math.sin(a), 0])
            pos += [d * radii[0 if sign < 0 else -1] + np.array([0, 0, 0.47 * sign]), d * 0.12 + np.array([0, 0, 0.49 * sign])]
            nrm += [d + np.array([0, 0, 0.7 * sign])] * 2
            uv += [(k / 10, 0), (k / 10, 0.03)]
        for k in range(10):
            a, b = 2 * k, 2 * k + 2
            tris += [(a, b, b + 1), (a, b + 1, a + 1)] if sign > 0 else [(a, b + 1, b), (a, a + 1, b + 1)]
        m.add("bark", pos, nrm, uv, tris, [(1, 0, 0, 1)] * len(pos))
    return m


# =====================================================================================================
# Materials and textures
# =====================================================================================================

def png(img, palette=False):
    buf = io.BytesIO()
    if palette:
        img = img.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue(), "image/png"


def jpeg(img):
    buf = io.BytesIO()
    img.convert("RGB").save(buf, format="JPEG", quality=90, optimize=True)
    return buf.getvalue(), "image/jpeg"


_cache = {}


def cached(key, make):
    if key not in _cache:
        _cache[key] = make()
    return _cache[key]


def rgb_image(arr, size=None):
    img = Image.fromarray(np.round(np.clip(arr, 0, 1) * 255).astype(np.uint8), "RGB")
    return img.resize((size, size), Image.LANCZOS) if size else img


def bark_material(kind, size):
    albedo, normal = cached(("bark", kind), lambda: tex.bark(kind))
    return {"color": jpeg(rgb_image(albedo, size)), "normal": jpeg(rgb_image(normal, size)), "rough": 0.95}


def leaf_material(kind, size):
    atlas = cached(("leaves", kind), lambda: tex.foliage(kind))
    img = Image.fromarray(atlas, "RGBA")
    if size != img.width:
        img = img.resize((size, size), Image.LANCZOS)
    return {"color": png(img, palette=True), "mask": True, "rough": 0.85}


def plain(hex_colour, rough=0.9):
    return {"factor": [*tex.hexrgb(hex_colour) ** 2.2, 1.0], "rough": rough}


def end_material(kind):
    return {"color": jpeg(rgb_image(cached(("end", kind), lambda: tex.end_grain(kind)))), "rough": 0.85}


CORES = {"leafy": "#2f5c17", "autumn": "#9a3514", "birch": "#b0700c", "spruce": "#284a25", "pine": "#2a4719"}


def tree_materials(bark, leaves, lod):
    return {
        "bark": bark_material(bark, 128 if lod else 512),
        "leaves": leaf_material(leaves, 256 if lod else 1024),
        "core": plain(CORES[leaves]),
    }


# =====================================================================================================
# glTF writer
# =====================================================================================================

def write_glb(path, name, model, materials):
    """One mesh (a primitive per material) in one node, with embedded images. Deterministic bytes."""
    blob = bytearray()
    views, accessors = [], []

    def view(data, target=None):
        while len(blob) % 4:
            blob.append(0)
        v = {"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)}
        if target:
            v["target"] = target
        blob.extend(data)
        views.append(v)
        return len(views) - 1

    def accessor(arr, kind, component=5126, target=34962, minmax=False):
        arr = np.ascontiguousarray(arr, dtype=np.float32 if component == 5126 else np.uint32)
        a = {"bufferView": view(arr.tobytes(), target), "componentType": component, "count": int(arr.shape[0]), "type": kind}
        if minmax:
            a["min"] = [float(x) for x in arr.min(axis=0)]
            a["max"] = [float(x) for x in arr.max(axis=0)]
        accessors.append(a)
        return len(accessors) - 1

    def axes(v):
        v = np.asarray(v, float)
        return np.stack([v[:, 0], v[:, 2], -v[:, 1]], axis=1) if model.zup else v

    images, textures, mats, prims = [], [], [], []

    def texture(data):
        images.append({"bufferView": view(data[0]), "mimeType": data[1]})
        textures.append({"sampler": 0, "source": len(images) - 1})
        return len(textures) - 1

    for mat_name in sorted(model.prims):
        spec = materials[mat_name]
        pbr = {"metallicFactor": 0.0, "roughnessFactor": spec["rough"]}
        mat = {"name": mat_name, "pbrMetallicRoughness": pbr}
        if "factor" in spec:
            pbr["baseColorFactor"] = [round(float(x), 5) for x in spec["factor"]]
        if "color" in spec:
            pbr["baseColorTexture"] = {"index": texture(spec["color"])}
        if "normal" in spec:
            mat["normalTexture"] = {"index": texture(spec["normal"])}
        if spec.get("mask"):
            mat["alphaMode"] = "MASK"
            mat["alphaCutoff"] = 0.5
        mats.append(mat)
        p = model.prims[mat_name]
        attrs = {
            "POSITION": accessor(np.round(axes(p["p"]), 5), "VEC3", minmax=True),
            "NORMAL": accessor(np.round(axes(p["n"]), 5), "VEC3"),
            "TEXCOORD_0": accessor(np.round(np.asarray(p["uv"]), 5), "VEC2"),
        }
        if "normal" in spec and p["t"]:
            t = np.asarray(p["t"], float)
            t3 = axes(t[:, :3])
            t3 = t3 - np.asarray(axes(p["n"])) * np.sum(t3 * axes(p["n"]), axis=1, keepdims=True)
            t3 /= np.maximum(np.linalg.norm(t3, axis=1, keepdims=True), 1e-9)
            attrs["TANGENT"] = accessor(np.round(np.concatenate([t3, t[:, 3:4]], axis=1), 5), "VEC4")
        idx = accessor(np.asarray(p["i"], np.uint32).reshape(-1), "SCALAR", component=5125, target=34963)
        prims.append({"attributes": attrs, "indices": idx, "material": len(mats) - 1})
    doc = {
        "asset": {"version": "2.0", "generator": "tools/trees/build_trees.py"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": name, "mesh": 0}],
        "meshes": [{"name": name, "primitives": prims}],
        "materials": mats,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob)}],
    }
    if images:
        doc["images"] = images
        doc["textures"] = textures
        doc["samplers"] = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]
    while len(blob) % 4:
        blob.append(0)
    doc["buffers"][0]["byteLength"] = len(blob)
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    body = struct.pack("<I", len(text)) + b"JSON" + text + struct.pack("<I", len(blob)) + b"BIN\x00" + bytes(blob)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body)


# =====================================================================================================
# The models
# =====================================================================================================

def tree(make, bark, leaves):
    return lambda lod: (make(lod), tree_materials(bark, leaves, lod))


TREES = {
    "birch_tree": tree(birch, "birch", "birch"),
    "leafy_tree": tree(leafy, "oak", "leafy"),
    "autumn_leafy_tree": tree(leafy, "oak", "autumn"),
    "burnt_tree": lambda lod: (burnt(lod), {"bark": bark_material("charred", 128 if lod else 512)}),
    "spruce_tree": tree(spruce, "spruce", "spruce"),
    "pine_tree": tree(pine, "pine", "pine"),
}

ITEM_MODELS = {
    "birch_log": lambda: (log(0.12, 1.45, 3), {"bark": bark_material("birch", 256), "end": end_material("birch")}),
    "wood_log": lambda: (log(0.235, 1.45, 5), {"bark": bark_material("oak", 256), "end": end_material("oak")}),
    "pine_log": lambda: (log(0.35, 1.2, 7), {"bark": bark_material("pine", 256), "end": end_material("pine")}),
    "wood_rod": lambda: (wood_rod(), {"bark": {**bark_material("oak", 128), "color": jpeg(rgb_image(
        cached(("rod",), lambda: np.clip(tex.bark("oak")[0] * 1.55 + 0.08, 0, 1)), 128))}, "end": end_material("oak")}),
}


def report(path, model):
    lo = np.min([np.min(p["p"], axis=0) for p in model.prims.values()], axis=0)
    hi = np.max([np.max(p["p"], axis=0) for p in model.prims.values()], axis=0)
    size = hi - lo
    print(f"{path.relative_to(REPO)}: {model.triangles()} triangles, {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, "
          f"{path.stat().st_size // 1024} KiB")


def main():
    only = set(sys.argv[1:])
    for name, make in TREES.items():
        for lod in (False, True):
            full = f"{name}_lod" if lod else name
            if only and name not in only and full not in only:
                continue
            model, mats = make(lod)
            path = WORLD / f"{full}.glb"
            write_glb(path, full, model, mats)
            report(path, model)
    for name, make in ITEM_MODELS.items():
        if only and name not in only:
            continue
        model, mats = make()
        path = ITEMS / f"{name}.glb"
        write_glb(path, name, model, mats)
        report(path, model)


if __name__ == "__main__":
    main()
