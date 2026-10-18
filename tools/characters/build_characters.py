"""Build the Mechanica player characters, their shared animations and the hand tool props.

What it builds (everything procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/characters/scrapper_male.glb``   - the male Scrapper: armature + skinned mesh, rest pose.
* ``mods/vanilla/meshes/characters/scrapper_female.glb`` - the female Scrapper on the same skeleton.
* ``mods/vanilla/meshes/characters/humanoid_animations.glb`` - the armature only, with every clip as its own
  glTF animation (30 fps, in place, no root motion).
* ``mods/vanilla/meshes/characters/hand_tools.glb`` - the hand-held tool props, each a node placed in the right
  hand of the rest-pose character (object origin = grip point).

Both characters share ONE skeleton (identical bone names and rest positions), so one animation set drives both.
Units are metres; in Blender Z is up, the character faces -Y, feet on Z=0. The rest pose is an A-pose.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy
    python tools/characters/build_characters.py

The outputs are committed; re-run this script after changing it and commit the regenerated .glb files.
The build is deterministic (seeded randomness only), so running it twice gives the same result.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
sys.path.insert(0, str(Path(__file__).resolve().parent))
import character_textures as ct  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "mods" / "vanilla" / "meshes" / "characters"
ITEM_DIR = REPO / "mods" / "vanilla" / "meshes" / "items"
FPS = 30


# =====================================================================================================
# Small math helpers
# =====================================================================================================

def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sstep(e0, e1, x):
    """Scalar smoothstep."""
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def norm(v):
    v = np.asarray(v, float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def catmull(points, t):
    """Catmull-Rom through a list of (x, value) keys, evaluated at x=t (values may be numpy arrays)."""
    xs = [p[0] for p in points]
    ys = [np.asarray(p[1], float) for p in points]
    if t <= xs[0]:
        return ys[0]
    if t >= xs[-1]:
        return ys[-1]
    i = max(k for k in range(len(xs) - 1) if xs[k] <= t)
    u = (t - xs[i]) / (xs[i + 1] - xs[i])
    p0 = ys[i - 1] if i > 0 else 2 * ys[i] - ys[i + 1]
    p1, p2 = ys[i], ys[i + 1]
    p3 = ys[i + 2] if i + 2 < len(ys) else 2 * ys[i + 1] - ys[i]
    u2, u3 = u * u, u * u * u
    return 0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3)


def srgb(hexstr):
    """'#rrggbb' -> linear RGBA tuple for Blender colour inputs."""
    h = hexstr.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], 1.0)


def axis_rot(axis, deg):
    return np.asarray(Matrix.Rotation(math.radians(deg), 3, Vector(norm(axis))))


# =====================================================================================================
# Skeleton (shared by both characters and the animation file)
# =====================================================================================================

# name, parent, head, tail. Left-side bones are listed once and mirrored to _r (x -> -x).
_CENTER_BONES = [
    ("root", None, (0.0, 0.0, 0.0), (0.0, 0.0, 0.25)),
    ("hips", "root", (0.0, 0.0, 0.98), (0.0, 0.0, 1.08)),
    ("spine", "hips", (0.0, 0.0, 1.08), (0.0, 0.0, 1.25)),
    ("chest", "spine", (0.0, 0.0, 1.25), (0.0, 0.0, 1.46)),
    ("neck", "chest", (0.0, 0.008, 1.46), (0.0, 0.004, 1.565)),
    ("head", "neck", (0.0, 0.004, 1.565), (0.0, 0.004, 1.76)),
]
_LEFT_BONES = [
    ("shoulder", "chest", (0.03, 0.0, 1.43), (0.175, 0.012, 1.45)),
    ("upper_arm", "shoulder", (0.175, 0.012, 1.45), (0.385, 0.032, 1.24)),
    ("forearm", "upper_arm", (0.385, 0.032, 1.24), (0.57, 0.007, 1.055)),
    ("hand", "forearm", (0.57, 0.007, 1.055), (0.63, 0.002, 0.995)),
    ("fingers", "hand", (0.63, 0.002, 0.995), (0.685, 0.0, 0.94)),
    ("thumb", "hand", (0.588, -0.028, 1.03), (0.612, -0.058, 1.003)),
    ("thigh", "hips", (0.092, 0.0, 0.92), (0.095, -0.018, 0.5)),
    ("shin", "thigh", (0.095, -0.018, 0.5), (0.095, 0.018, 0.085)),
    ("foot", "shin", (0.095, 0.018, 0.085), (0.095, -0.1, 0.025)),
    ("toe", "foot", (0.095, -0.1, 0.025), (0.095, -0.165, 0.02)),
]


def _skeleton():
    bones = list(_CENTER_BONES)
    for side, sx in (("l", 1.0), ("r", -1.0)):
        for name, parent, h, t in _LEFT_BONES:
            par = parent if parent in ("chest", "hips") else f"{parent}_{side}"
            bones.append((f"{name}_{side}", par, (h[0] * sx, h[1], h[2]), (t[0] * sx, t[1], t[2])))
    return bones


SKELETON = _skeleton()
BONE = {b[0]: {"parent": b[1], "head": np.array(b[2]), "tail": np.array(b[3])} for b in SKELETON}
BONE_NAMES = [b[0] for b in SKELETON]
UPPER_BODY_BONES = ["spine", "chest", "neck", "head"] + [
    f"{n}_{s}" for s in ("l", "r") for n in ("shoulder", "upper_arm", "forearm", "hand", "fingers", "thumb")]


def bone_head(name):
    return BONE[name]["head"]


def bone_tail(name):
    return BONE[name]["tail"]


def hand_frame(side):
    """Rest frame of a hand: d = toward the fingers, w = toward the thumb side, n = palm normal."""
    d = norm(bone_head(f"fingers_{side}") - bone_head(f"hand_{side}"))
    w = np.array([0.0, -1.0, 0.0])
    w = norm(w - np.dot(w, d) * d)
    sgn = 1.0 if side == "l" else -1.0
    n = norm(np.cross(d, w)) * sgn
    return d, w, n


def grip_point(side):
    """Centre of the closed fist (where a handle passes through) in rest pose."""
    d, w, n = hand_frame(side)
    return bone_head(f"hand_{side}") + d * 0.062 + n * 0.028


# =====================================================================================================
# Mesh parts
# =====================================================================================================

# Per-vertex surface attributes the texture baker reads (see character_textures.py). Each is (n, width);
# parts that do not set one get NaN there.
#   box:  a soft box / ellipsoid in its own frame: (x/hx, y/hy, z/hz, hx, hy, hz, e)
#   loft: a loft or strap: (distance along it in m, cos theta, sin theta, across in -1..1)
ATTR_WIDTH = {"box": 7, "loft": 4}


class Part:
    """A chunk of geometry with per-face material names, a smooth flag and (later) bone weights.

    a holds optional per-vertex surface attributes (ATTR_WIDTH), g an optional per-face texture group."""

    def __init__(self, verts, faces, mat, smooth=True):
        self.v = np.asarray(verts, float).reshape(-1, 3)
        self.f = [tuple(int(i) for i in f) for f in faces]
        self.m = [mat] * len(self.f) if isinstance(mat, str) else list(mat)
        self.s = [smooth] * len(self.f) if isinstance(smooth, bool) else list(smooth)
        self.w: dict[str, np.ndarray] | None = None
        self.a: dict[str, np.ndarray] = {}
        self.g: list | None = None
        self.t: str | None = None

    def copy(self):
        p = Part(self.v.copy(), list(self.f), list(self.m), list(self.s))
        p.w = None if self.w is None else {k: v.copy() for k, v in self.w.items()}
        p.a = {k: v.copy() for k, v in self.a.items()}
        p.g = None if self.g is None else list(self.g)
        p.t = self.t
        return p

    def tagged(self, name):
        """Name the kind of part for the texture baker's seams (torso, sleeve, leg, pelvis)."""
        self.t = name
        return self

    def group(self, name):
        """Put every face of this part in texture group `name` (else the group follows the bones)."""
        self.g = [name] * len(self.f)
        return self

    def mirrored(self):
        """Mirror across the YZ plane (left <-> right), fixing winding and swapping _l/_r weights."""
        p = self.copy()
        p.v[:, 0] *= -1.0
        p.f = [tuple(reversed(f)) for f in p.f]
        if p.w is not None:
            p.w = {swap_side(k): v for k, v in p.w.items()}
        return p

    def transformed(self, mat3=None, offset=(0, 0, 0)):
        p = self.copy()
        if mat3 is not None:
            p.v = p.v @ np.asarray(mat3).T
        p.v = p.v + np.asarray(offset)
        return p

    def subdivided(self, levels=1):
        if levels <= 0:
            return self.copy()
        return _subdivide(self, levels)

    def skin(self, weights):
        """weights: a bone name (rigid) or a callable(verts) -> {bone: array}."""
        if isinstance(weights, str):
            self.w = {weights: np.ones(len(self.v))}
        else:
            self.w = weights(self.v)
        return self

    def mat(self, name):
        self.m = [name] * len(self.f)
        return self

    def flat(self):
        self.s = [False] * len(self.f)
        return self


def swap_side(name):
    if name.endswith("_l"):
        return name[:-2] + "_r"
    if name.endswith("_r"):
        return name[:-2] + "_l"
    return name


def merge(parts):
    """One part from many. Faces remember which input part they came from (out.pid)."""
    verts, faces, mats, smooth, groups, pid, tags = [], [], [], [], [], [], []
    weights: dict[str, list] = {}
    off = 0
    n_total = sum(len(p.v) for p in parts)
    attrs = {k: np.full((n_total, w), np.nan) for k, w in ATTR_WIDTH.items()
             if any(k in p.a for p in parts)}
    for k_part, p in enumerate(parts):
        verts.append(p.v)
        faces.extend(tuple(i + off for i in f) for f in p.f)
        mats.extend(p.m)
        smooth.extend(p.s)
        groups.extend(p.g if p.g is not None else [None] * len(p.f))
        pid.extend([k_part] * len(p.f))
        tags.extend(getattr(p, "tag", None) or [p.t] * len(p.f))
        if p.w is not None:
            for k, w in p.w.items():
                arr = weights.setdefault(k, np.zeros(n_total))
                arr[off:off + len(p.v)] += w
        for k, a in p.a.items():
            attrs[k][off:off + len(p.v)] = a
        off += len(p.v)
    out = Part(np.vstack(verts) if verts else np.zeros((0, 3)), faces, mats, smooth)
    out.w = {k: np.asarray(v) for k, v in weights.items()} if weights else None
    out.a = attrs
    out.g = groups
    out.pid = pid
    out.tag = tags
    return out


def _subdivide(part, levels):
    names = sorted(set(part.m))
    me = bpy.data.meshes.new("_tmp_subd")
    me.from_pydata(part.v.tolist(), [], part.f)
    me.polygons.foreach_set("material_index", [names.index(m) for m in part.m])
    me.polygons.foreach_set("use_smooth", [bool(s) for s in part.s])
    me.update()
    ob = bpy.data.objects.new("_tmp_subd", me)
    bpy.context.scene.collection.objects.link(ob)
    mod = ob.modifiers.new("subd", "SUBSURF")
    mod.levels = levels
    mod.render_levels = levels
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    m2 = ev.to_mesh()
    verts = np.zeros(len(m2.vertices) * 3)
    m2.vertices.foreach_get("co", verts)
    faces = [tuple(p.vertices) for p in m2.polygons]
    mats = [names[p.material_index] for p in m2.polygons]
    smooth = [p.use_smooth for p in m2.polygons]
    ev.to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.meshes.remove(me)
    return Part(verts.reshape(-1, 3), faces, mats, smooth)


# ------------------------------------------------------------------------------------------------
# Primitive generators
# ------------------------------------------------------------------------------------------------

def _se(u, e):
    """Superellipse shaping of a cosine/sine value (e=2 ellipse, larger e squarer)."""
    return np.sign(u) * np.abs(u) ** (2.0 / e)


def loft(rings, segs=16, cap0=True, cap1=True, mat="default", smooth=True, bulge0=0.0, bulge1=0.0,
         theta0=0.0, closed=True):
    """Loft closed cross-sections.

    rings: list of dicts with c (centre), a (cross-section x axis), b (y axis), rx, ry,
    optional e (superellipse exponent) and optional fn(theta)->radius multiplier.
    The ring tangent is a x b; faces point outward.
    """
    verts = []
    n = len(rings)
    thetas = theta0 + np.linspace(0, 2 * math.pi, segs, endpoint=False) if closed else \
        theta0 + np.linspace(0, 2 * math.pi, segs)
    cs = [np.asarray(r["c"], float) for r in rings]
    along = np.concatenate([[0.0], np.cumsum([np.linalg.norm(cs[i + 1] - cs[i]) for i in range(n - 1)])])
    lat = []
    for i, r in enumerate(rings):
        for th in thetas:
            lat.append((along[i], math.cos(th), math.sin(th), _se(math.sin(th), r.get("e", 2.0))))
    for r in rings:
        c, a, b = (np.asarray(r[k], float) for k in ("c", "a", "b"))
        e = r.get("e", 2.0)
        mul = r.get("fn")
        for th in thetas:
            k = mul(th) if mul else 1.0
            ka = r.get("fa", None)
            rx = r["rx"] * k
            ry = r["ry"] * k
            pt = c + a * rx * _se(math.cos(th), e) + b * ry * _se(math.sin(th), e)
            if ka:
                pt = pt + ka(th)
            verts.append(pt)
    faces = []
    m = segs if closed else segs - 1
    for i in range(n - 1):
        for j in range(m):
            j1 = (j + 1) % segs
            faces.append((i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j))
    if closed and cap0:
        c0 = np.mean(verts[0:segs], axis=0)
        t0 = norm(np.cross(rings[0]["a"], rings[0]["b"]))
        verts.append(c0 - t0 * bulge0)
        ci = len(verts) - 1
        for j in range(segs):
            faces.append((ci, (j + 1) % segs, j))
    if closed and cap1:
        base = (n - 1) * segs
        c1 = np.mean(verts[base:base + segs], axis=0)
        t1 = norm(np.cross(rings[-1]["a"], rings[-1]["b"]))
        verts.append(c1 + t1 * bulge1)
        ci = len(verts) - 1
        for j in range(segs):
            faces.append((ci, base + j, base + (j + 1) % segs))
    p = Part(verts, faces, mat, smooth)
    lat = np.asarray(lat, float)
    caps = len(verts) - len(lat)
    if caps:
        ends = [np.array([along[0], 1.0, 0.0, 0.0])] * (1 if (closed and cap0) else 0) + \
            [np.array([along[-1], 1.0, 0.0, 0.0])] * (1 if (closed and cap1) else 0)
        lat = np.vstack([lat] + [e[None] for e in ends])
    p.a["loft"] = lat
    return p


def frames_along(points, ref):
    """Tangents and parallel-transported cross-section axes along a polyline."""
    pts = [np.asarray(p, float) for p in points]
    tans = []
    for i in range(len(pts)):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == len(pts) - 1:
            t = pts[-1] - pts[-2]
        else:
            t = norm(pts[i + 1] - pts[i]) + norm(pts[i] - pts[i - 1])
        tans.append(norm(t))
    a = np.asarray(ref, float)
    out = []
    for t in tans:
        a = norm(a - np.dot(a, t) * t)
        b = np.cross(t, a)
        out.append((t, a, b))
    return out


def tube(points, radii, segs=10, ref=(0, -1, 0), mat="default", e=2.0, ry_scale=1.0, cap=True,
         smooth=True, bulge=0.0):
    """A tube along a polyline. radii: scalar, list of scalars or list of (rx, ry)."""
    fr = frames_along(points, ref)
    rings = []
    for i, (p, (t, a, b)) in enumerate(zip(points, fr)):
        r = radii[i] if isinstance(radii, (list, tuple, np.ndarray)) else radii
        rx, ry = (r, r * ry_scale) if np.isscalar(r) else r
        rings.append(dict(c=p, a=a, b=b, rx=rx, ry=ry, e=e))
    return loft(rings, segs, cap, cap, mat, smooth, bulge, bulge)


def soft_box(center, half, axes=None, e=4.0, e2=4.0, segs=16, rings=10, mat="default", smooth=True):
    """Rounded box / superellipsoid. half = (hx, hy, hz); axes = 3x3 rows (x, y, z directions)."""
    ax = np.eye(3) if axes is None else np.asarray(axes, float)
    ax = np.array([norm(ax[0]), norm(ax[1]), norm(ax[2])])
    if np.linalg.det(ax) < 0:      # keep faces pointing outward with a left-handed axis set
        ax[2] = -ax[2]
    hx, hy, hz = half
    rl = []
    us = np.linspace(-1, 1, rings + 2)[1:-1]
    for u in us:
        k = (1 - abs(u) ** e2) ** (1.0 / e2)
        z = u * hz
        rl.append(dict(c=np.asarray(center) + ax[2] * z, a=ax[0], b=ax[1], rx=hx * k, ry=hy * k, e=e))
    p = loft(rl, segs, True, True, mat, smooth)
    del p.a["loft"]
    loc = (p.v - np.asarray(center, float)) @ ax.T
    h = np.array([hx, hy, hz], float)
    p.a["box"] = np.hstack([loc / h, np.tile(np.append(h, e), (len(p.v), 1))])
    return p


def ellipsoid(center, radii, axes=None, segs=16, rings=10, mat="default", smooth=True):
    return soft_box(center, radii, axes, 2.0, 2.0, segs, rings, mat, smooth)


def cylinder(p0, p1, r, segs=12, mat="default", smooth=True, e=2.0, ref=(0, -1, 0), bevel=0.0):
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    if bevel > 0:
        d = norm(p1 - p0)
        pts = [p0, p0 + d * bevel, p1 - d * bevel, p1]
        rad = [r - bevel, r, r, r - bevel]
        return tube(pts, rad, segs, ref, mat, e, cap=True, smooth=smooth)
    return tube([p0, p1], r, segs, ref, mat, e, cap=True, smooth=smooth)


def box(center, half, axes=None, mat="default"):
    """A plain hard box (flat shaded)."""
    ax = np.eye(3) if axes is None else np.array([norm(a) for a in axes])
    if np.linalg.det(ax) < 0:      # keep faces pointing outward with a left-handed axis set
        ax[2] = -ax[2]
    c = np.asarray(center, float)
    vs = []
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                vs.append(c + ax[0] * sx * half[0] + ax[1] * sy * half[1] + ax[2] * sz * half[2])
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    p = Part(vs, faces, mat, smooth=False)
    loc = (p.v - c) @ ax.T
    h = np.asarray(half, float)
    p.a["box"] = np.hstack([loc / h, np.tile(np.append(h, 20.0), (len(p.v), 1))])
    return p


def c_ring(center, r_out, r_in, thickness, a0, a1, frame, segs=14, mat="metal"):
    """An annulus sector (open-end wrench head) in the frame's UV plane."""
    U, V, N = (np.asarray(x, float) for x in frame)
    c = np.asarray(center, float)
    angs = np.linspace(a0, a1, segs)
    vs = []
    for s in (-0.5, 0.5):
        for a in angs:
            for r in (r_out, r_in):
                vs.append(c + (U * math.cos(a) + V * math.sin(a)) * r + N * thickness * s)
    idx = lambda s, i, k: s * segs * 2 + i * 2 + k
    faces = []
    for i in range(segs - 1):
        faces.append((idx(1, i, 0), idx(1, i + 1, 0), idx(1, i + 1, 1), idx(1, i, 1)))
        faces.append((idx(0, i, 1), idx(0, i + 1, 1), idx(0, i + 1, 0), idx(0, i, 0)))
        faces.append((idx(0, i, 0), idx(0, i + 1, 0), idx(1, i + 1, 0), idx(1, i, 0)))
        faces.append((idx(1, i, 1), idx(1, i + 1, 1), idx(0, i + 1, 1), idx(0, i, 1)))
    for i in (0, segs - 1):
        f = (idx(0, i, 0), idx(1, i, 0), idx(1, i, 1), idx(0, i, 1))
        faces.append(f if i == 0 else tuple(reversed(f)))
    return Part(vs, faces, mat, smooth=False)


# =====================================================================================================
# Skin weights
# =====================================================================================================

def _seg_dist(v, a, b):
    ab = b - a
    t = np.clip(((v - a) @ ab) / max(np.dot(ab, ab), 1e-12), 0.0, 1.0)
    p = a + t[:, None] * ab
    return np.linalg.norm(v - p, axis=1), t


def chain_weights(bones, blend=0.06, start=None, blends=None):
    """Weights along a bone chain by projection onto the chain polyline, blending across joints."""
    def fn(v):
        pts = [np.asarray(start, float) if start is not None else bone_head(bones[0])]
        pts += [bone_head(b) for b in bones[1:]] + [bone_tail(bones[-1])]
        lens = [np.linalg.norm(pts[i + 1] - pts[i]) for i in range(len(pts) - 1)]
        cum = np.concatenate([[0.0], np.cumsum(lens)])
        best = np.full(len(v), np.inf)
        s = np.zeros(len(v))
        for i in range(len(pts) - 1):
            d, t = _seg_dist(v, pts[i], pts[i + 1])
            # extend first/last segments beyond their ends
            if i == 0:
                ab = pts[1] - pts[0]
                tt = ((v - pts[0]) @ ab) / np.dot(ab, ab)
                t = np.where(tt < 0, tt, t)
            if i == len(pts) - 2:
                ab = pts[i + 1] - pts[i]
                tt = ((v - pts[i]) @ ab) / np.dot(ab, ab)
                t = np.where(tt > 1, tt, t)
            m = d < best - 1e-9
            best = np.where(m, d, best)
            s = np.where(m, cum[i] + t * lens[i], s)
        out = {}
        for i, b in enumerate(bones):
            lo = -np.inf if i == 0 else cum[i]
            hi = np.inf if i == len(bones) - 1 else cum[i + 1]
            bw0 = (blends[i - 1] if blends else blend) if i > 0 else 1.0
            bw1 = (blends[i] if blends else blend) if i < len(bones) - 1 else 1.0
            w0 = 1.0 if lo == -np.inf else smoothstep(lo - bw0 / 2, lo + bw0 / 2, s)
            w1 = 0.0 if hi == np.inf else smoothstep(hi - bw1 / 2, hi + bw1 / 2, s)
            out[b] = np.clip(w0 - w1, 0, 1) * np.ones(len(v))
        return out
    return fn


def torso_weights(v):
    """Hips / spine / chest by height, plus shoulder and upper-arm pull near the shoulders."""
    z = v[:, 2]
    ax = np.abs(v[:, 0])
    w_sp = smoothstep(1.02, 1.14, z)
    w_ch = smoothstep(1.2, 1.32, z)
    hips = 1 - w_sp
    spine = w_sp - w_ch
    chest = w_ch
    neck = smoothstep(1.47, 1.56, z) * (1 - smoothstep(0.06, 0.1, ax))
    sh = smoothstep(0.07, 0.17, ax) * smoothstep(1.34, 1.46, z) * 0.85
    ua = smoothstep(0.155, 0.215, ax) * smoothstep(1.3, 1.42, z) * 0.6
    chest = chest * (1 - sh) * (1 - neck)
    out = {"hips": hips, "spine": spine, "chest": chest, "neck": chest * 0 + neck * w_ch}
    left = v[:, 0] > 0
    for side, msk in (("l", left), ("r", ~left)):
        out[f"shoulder_{side}"] = np.where(msk, sh * (1 - ua) * w_ch, 0.0)
        out[f"upper_arm_{side}"] = np.where(msk, ua * w_ch, 0.0)
    return out


def leg_weights(side):
    chain = chain_weights([f"thigh_{side}", f"shin_{side}", f"foot_{side}", f"toe_{side}"],
                          blends=[0.09, 0.06, 0.03])

    def fn(v):
        out = chain(v)
        # pelvis region: blend into the hips bone above/around the hip joint
        top = smoothstep(0.84, 0.97, v[:, 2])
        inner = 1 - smoothstep(0.0, 0.07, np.abs(v[:, 0]))
        hw = np.clip(top * 0.75 + inner * top * 0.25, 0, 1)
        for k in out:
            out[k] = out[k] * (1 - hw)
        out["hips"] = hw
        return out
    return fn


def pelvis_weights(v):
    """Trouser seat: hips in the middle, thighs on each side toward the leg holes."""
    z = v[:, 2]
    ax = np.abs(v[:, 0])
    low = 1 - smoothstep(0.86, 0.99, z)
    side = smoothstep(0.02, 0.1, ax)
    tw = np.clip(low * side * 0.7, 0, 1)
    out = {"hips": 1 - tw}
    out["spine"] = smoothstep(1.03, 1.12, z) * 0.0
    left = v[:, 0] > 0
    out["thigh_l"] = np.where(left, tw, 0.0)
    out["thigh_r"] = np.where(~left, tw, 0.0)
    return out


def neck_weights(v):
    z = v[:, 2]
    hw = smoothstep(1.53, 1.6, z)
    cw = 1 - smoothstep(1.43, 1.5, z)
    return {"chest": cw, "neck": np.clip(1 - hw - cw, 0, 1), "head": hw}


def finalize_weights(w, n, max_inf=4):
    """Normalise, keep the strongest influences per vertex."""
    names = list(w.keys())
    W = np.stack([np.asarray(w[k], float) * np.ones(n) for k in names], axis=1)
    W = np.clip(W, 0, None)
    if W.shape[1] > max_inf:
        idx = np.argsort(-W, axis=1)[:, max_inf:]
        np.put_along_axis(W, idx, 0.0, axis=1)
    s = W.sum(axis=1)
    s[s < 1e-9] = 1.0
    W = W / s[:, None]
    return names, W


# =====================================================================================================
# Materials
# =====================================================================================================

PALETTE = {
    # skin, hair, face
    "skin_m": ("#c08560", 0.55, 0.0),
    "skin_f": ("#d29a74", 0.5, 0.0),
    "lips_m": ("#9a5a48", 0.45, 0.0),
    "lips_f": ("#b4625a", 0.4, 0.0),
    "hair_m": ("#3a2416", 0.7, 0.0),
    "hair_f": ("#3b2316", 0.6, 0.0),
    "eye_white": ("#ece6de", 0.25, 0.0),
    "iris_m": ("#4a2e1a", 0.2, 0.0),
    "iris_f": ("#5a3a1c", 0.2, 0.0),
    "pupil": ("#0e0b0a", 0.2, 0.0),
    "brow": ("#2e1c12", 0.8, 0.0),
    # shared accents
    "orange": ("#d8762a", 0.55, 0.0),
    "orange_dark": ("#b85f22", 0.6, 0.0),
    "metal": ("#9a9a94", 0.38, 1.0),
    "metal_dark": ("#55555a", 0.45, 1.0),
    "leather_dark": ("#3b2a1d", 0.6, 0.0),
    "leather_mid": ("#5c3d26", 0.55, 0.0),
    "sole": ("#2b2724", 0.85, 0.0),
    "lace": ("#2a2420", 0.8, 0.0),
    # male outfit
    "jacket_olive": ("#585d3a", 0.85, 0.0),
    "jacket_olive_dark": ("#45482e", 0.9, 0.0),
    "trouser_olive": ("#4d5033", 0.9, 0.0),
    "backpack_olive": ("#40432f", 0.85, 0.0),
    "glove_dark": ("#2e2b28", 0.7, 0.0),
    "boot_tan": ("#8a5a33", 0.6, 0.0),
    "goggle_dark": ("#2a2a2a", 0.5, 0.3),
    "lens_orange": ("#e8862a", 0.08, 0.0),
    # female outfit
    "denim": ("#44628a", 0.85, 0.0),
    "denim_light": ("#5d7ca6", 0.85, 0.0),
    "denim_dark": ("#34507a", 0.85, 0.0),
    "shirt": ("#7a8470", 0.9, 0.0),
    "canvas": ("#7a5234", 0.9, 0.0),
    "canvas_dark": ("#5a4636", 0.9, 0.0),
    "glove_brown": ("#6a4429", 0.6, 0.0),
    "boot_brown": ("#6b4529", 0.6, 0.0),
    "brass": ("#b8903e", 0.35, 1.0),
    "lens_blue": ("#4a8cbc", 0.08, 0.0),
    "patch": ("#7a5a40", 0.85, 0.0),
    # props
    "paint_olive": ("#5d6440", 0.6, 0.15),
    "paint_grey": ("#6e7270", 0.55, 0.2),
    "handle_wood": ("#7a5534", 0.7, 0.0),
    "rubber": ("#232323", 0.8, 0.0),
    "blue": ("#3a78b8", 0.5, 0.0),
    "screen": ("#1e3a4a", 0.2, 0.0),
    "water": ("#4a90b8", 0.05, 0.0),
    "potato": ("#a8834e", 0.8, 0.0),
    "tool_orange": ("#d8762a", 0.45, 0.0),
    "tool_blue": ("#3a78b8", 0.45, 0.0),
    "lining": ("#3a3f2c", 0.95, 0.0),
    # sledgehammer
    "gunmetal": ("#36383c", 0.52, 1.0),
    "steel_shaft": ("#4a4c50", 0.42, 1.0),
    "bolt_steel": ("#5c5e62", 0.38, 1.0),
    "paint_yellow": ("#e6b52a", 0.5, 0.05),
    "grip_rubber": ("#1d1d1e", 0.82, 0.0),
    # water bucket
    "bucket_teal": ("#4f8985", 0.7, 0.0),
    "bucket_inner": ("#3e6f6c", 0.74, 0.0),
    "bucket_band": ("#4e5357", 0.66, 0.0),
    "bucket_orange": ("#e2662c", 0.55, 0.0),
    "bucket_wire": ("#a3a6a8", 0.32, 1.0),
    "bucket_pin": ("#5a5d61", 0.4, 1.0),
    "bucket_water": ("#6aa6b8", 0.04, 0.0),
    # refine tool
    "pipe_steel": ("#525457", 0.5, 1.0),
    "plate_steel": ("#4a4c50", 0.5, 1.0),
    "tape_brown": ("#5a3e27", 0.86, 0.0),
    "hazard_band": ("#e2b322", 0.5, 0.05),
    "rust_sheet": ("#83401a", 0.8, 0.25),
    # connect tool
    "tool_gunmetal": ("#33363a", 0.46, 0.75),
    "tool_bezel": ("#222326", 0.52, 0.6),
    "tool_silver": ("#8c9096", 0.32, 1.0),
    "panel_line": ("#141517", 0.7, 0.6),
    "grip_chevron": ("#1d1e20", 0.8, 0.05),
    "accent_orange": ("#e8670f", 0.42, 0.0),
    "void": ("#060505", 1.0, 1.0),          # the inside of a cut sleeve: no light comes back from it
    "default": ("#808080", 0.5, 0.0),
}


def get_material(name):
    m = bpy.data.materials.get("M_" + name)
    if m:
        return m
    col, rough, metal = PALETTE[name]
    m = bpy.data.materials.new("M_" + name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = srgb(col)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    m.diffuse_color = srgb(col)
    m.use_backface_culling = True   # exported as single-sided (all shells face outward)
    return m


# =====================================================================================================
# Building Blender objects
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.unit_settings.system = "METRIC"
    return sc


def build_armature(name="Armature"):
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for bname, parent, h, t in SKELETON:
        b = arm.edit_bones.new(bname)
        b.head = h
        b.tail = t
        b.use_deform = True
        if parent:
            b.parent = eb[parent]
            b.use_connect = np.allclose(BONE[parent]["tail"], h)
        if bname.startswith(("foot", "toe")):
            b.align_roll(Vector((0, 0, 1)))
        else:
            b.align_roll(Vector((0, -1, 0)))
        eb[bname] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in ob.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return ob


def bevel_box(center, half, bevel, axes=None, mat="default"):
    """A hard box with chamfered edges and corners (flat shaded): 6 faces, 12 edge strips, 8 corner triangles."""
    ax = np.eye(3) if axes is None else np.array([norm(a) for a in axes])
    c = np.asarray(center, float)
    h = np.asarray(half, float)
    inner = h - bevel
    idx = {}
    vs = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                s = np.array([sx, sy, sz], float)
                for k in range(3):
                    loc = s * inner
                    loc[k] = s[k] * h[k]
                    idx[(sx, sy, sz, k)] = len(vs)
                    vs.append(c + ax.T @ loc)
    vs = np.array(vs)
    faces = []
    signs = (-1, 1)
    for k in range(3):                       # main faces
        for s in signs:
            faces.append([idx[key] for key in idx if key[3] == k and key[k] == s])
    for a in range(3):                       # edge strips between the faces of axes a and b
        for b in range(a + 1, 3):
            for sa in signs:
                for sb in signs:
                    faces.append([idx[key] for key in idx if key[3] in (a, b) and key[a] == sa and key[b] == sb])
    for sx in signs:                         # corners
        for sy in signs:
            for sz in signs:
                faces.append([idx[(sx, sy, sz, k)] for k in range(3)])
    out = []
    for f in faces:
        pts = vs[f]
        fc = pts.mean(axis=0)
        nrm = fc - c
        nrm = norm(nrm)
        u = norm(pts[0] - fc)
        v = np.cross(nrm, u)
        ang = [math.atan2(np.dot(p - fc, v), np.dot(p - fc, u)) for p in pts]
        out.append(tuple(np.array(f)[np.argsort(ang)]))
    p = Part(vs, out, mat, smooth=False)
    loc = (p.v - c) @ ax.T
    p.a["box"] = np.hstack([loc / h, np.tile(np.append(h, 20.0), (len(p.v), 1))])
    return p


def hex_bolt(base, normal, ref, r=0.0105, height=0.0065, washer=True, mat="bolt_steel"):
    """A hex bolt head standing on a surface: base point, outward normal, ref fixes the hex rotation."""
    base, normal = np.asarray(base, float), norm(np.asarray(normal, float))
    ref = norm(np.asarray(ref, float) - np.dot(ref, normal) * normal)
    parts = []
    if washer:
        parts.append(cylinder(base - normal * 0.001, base + normal * 0.0022, r * 1.3, 16, mat, ref=ref, smooth=True))
        base = base + normal * 0.0022
    parts.append(cylinder(base, base + normal * height, r, 6, mat, smooth=False, ref=ref, bevel=0.0015))
    return parts


def rivet(base, normal, r=0.0065, mat="bolt_steel"):
    """A domed rivet head on a surface."""
    base, normal = np.asarray(base, float), norm(np.asarray(normal, float))
    a = norm(np.cross(normal, (1.0, 0.3, 0.2)))
    b = np.cross(normal, a)
    rings = [dict(c=base - normal * 0.001, a=a, b=b, rx=r, ry=r)]
    for k in (0.35, 0.7, 0.92):
        rr = r * math.cos(k * math.pi / 2)
        rings.append(dict(c=base + normal * r * 0.55 * math.sin(k * math.pi / 2), a=a, b=b, rx=rr, ry=rr))
    return loft(rings, 12, True, True, mat, True)


def build_mesh_object(name, part, armature=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(part.v.tolist(), [], part.f)
    names = []
    for m in part.m:
        if m not in names:
            names.append(m)
    for n in names:
        me.materials.append(get_material(n))
    me.polygons.foreach_set("material_index", [names.index(m) for m in part.m])
    me.polygons.foreach_set("use_smooth", [bool(s) for s in part.s])
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if armature is not None and part.w is not None:
        bnames, W = finalize_weights(part.w, len(part.v))
        for j, bn in enumerate(bnames):
            col = W[:, j]
            idx = np.nonzero(col > 1e-4)[0]
            if len(idx) == 0:
                continue
            vg = ob.vertex_groups.new(name=bn)
            for i in idx:
                vg.add([int(i)], float(col[i]), "REPLACE")
        ob.parent = armature
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = armature
    return ob


def tri_count(part):
    return sum(len(f) - 2 for f in part.f)


# =====================================================================================================
# Shared body-part builders
# =====================================================================================================

def ring_points(w, yf, yb, yc, ef, eb, thetas):
    """Cross-section with a front half (y < yc) and a back half of different depth and roundness."""
    pts = []
    for th in thetas:
        c, s = math.cos(th), math.sin(th)
        x = w * _se(c, ef if s < 0 else eb)
        if s < 0:
            y = yc + (yc - yf) * -abs(_se(s, ef))
        else:
            y = yc + (yb - yc) * _se(s, eb)
        pts.append((x, y))
    return pts


def grid_loft(keys, n_rings, segs, x0=0.0, cap0=True, cap1=True, mat="default", ef=2.4, eb=2.4,
              zs=None, theta_fn=None):
    """Vertical loft through key cross-sections keys = [(z, w, yf, yb, yc), ...] (Catmull-Rom).

    Returns (part, grid) where grid has shape (n_rings, segs, 3) for later masking and projections.
    The seam (theta=0) sits on +X, front is theta=-90deg.
    """
    kz = [k[0] for k in keys]
    zs = np.linspace(kz[0], kz[-1], n_rings) if zs is None else np.asarray(zs)
    thetas = np.linspace(0, 2 * math.pi, segs, endpoint=False)
    grid = np.zeros((len(zs), segs, 3))
    for i, z in enumerate(zs):
        w, yf, yb, yc = catmull([(k[0], k[1:5]) for k in keys], z)
        pts = ring_points(w, yf, yb, yc, ef, eb, theta_fn(z, w) if theta_fn else thetas)
        for j, (x, y) in enumerate(pts):
            grid[i, j] = (x + x0, y, z)
    part = grid_part(grid, cap0, cap1, mat)
    return part, grid


def grid_part(grid, cap0=True, cap1=True, mat="default", mask=None, bulge0=0.0, bulge1=0.0):
    """Turn an (n, segs, 3) grid of closed rings into quads (+ fan caps). mask (n, segs) keeps quads
    whose four corners are all True."""
    n, segs, _ = grid.shape
    verts = grid.reshape(-1, 3).tolist()
    faces = []
    for i in range(n - 1):
        for j in range(segs):
            j1 = (j + 1) % segs
            q = (i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j)
            if mask is not None and not (mask[i, j] and mask[i, j1] and mask[i + 1, j1] and mask[i + 1, j]):
                continue
            faces.append(q)
    if cap0 and mask is None:
        c = grid[0].mean(axis=0) - np.array([0, 0, bulge0])
        verts.append(c.tolist())
        ci = len(verts) - 1
        for j in range(segs):
            faces.append((ci, (j + 1) % segs, j))
    if cap1 and mask is None:
        c = grid[-1].mean(axis=0) + np.array([0, 0, bulge1])
        verts.append(c.tolist())
        ci = len(verts) - 1
        base = (n - 1) * segs
        for j in range(segs):
            faces.append((ci, base + j, base + (j + 1) % segs))
    p = Part(verts, faces, mat)
    if mask is not None:
        p = compact(p)
    return p


def compact(part):
    used = sorted({i for f in part.f for i in f})
    remap = {o: n for n, o in enumerate(used)}
    p = Part(part.v[used], [tuple(remap[i] for i in f) for f in part.f], part.m, part.s)
    if part.w is not None:
        p.w = {k: v[used] for k, v in part.w.items()}
    p.a = {k: v[used] for k, v in part.a.items()}
    p.g = part.g
    return p


def assign_mats(part, fn):
    """Per-face material by a function of the face centre (x, y, z) -> name or None (keep)."""
    out = []
    for f, m in zip(part.f, part.m):
        c = part.v[list(f)].mean(axis=0)
        r = fn(c)
        out.append(r if r else m)
    part.m = out
    return part


def make_bvh(part):
    tris = []
    for f in part.f:
        for k in range(1, len(f) - 1):
            tris.append((f[0], f[k], f[k + 1]))
    return BVHTree.FromPolygons([Vector(v) for v in part.v], tris)


def project(bvh, origin, direction, offset=0.0):
    """Ray-cast onto a surface; returns the hit point pushed out by offset along the ray's reverse."""
    o, d = Vector(origin), Vector(direction).normalized()
    hit, nrm, _, _ = bvh.ray_cast(o, d, 10.0)
    if hit is None:
        return np.asarray(origin, float)
    return np.asarray(hit - d * offset)


def strap(points, normals, width, thickness, mat, segs=6, e=6.0):
    """A flat strap along points; normals give the thickness direction (surface normal)."""
    fr = frames_along(points, normals[0])
    rings = []
    for p, (t, _, _), nv in zip(points, fr, normals):
        a = norm(np.asarray(nv) - np.dot(nv, t) * t)
        b = np.cross(t, a)
        rings.append(dict(c=p, a=a, b=b, rx=thickness, ry=width, e=e))
    return loft(rings, segs, True, True, mat, True)


def surface_strap(bvh, points, width, thickness, mat, out=0.004, samples=2):
    """Strap whose control points are projected onto a body (bvh) from outside along their normals.

    points: list of (point_near_surface, inward_direction); each is cast from 0.4 m outside."""
    pts, nrms = [], []
    for (p, d) in points:
        hit = project(bvh, np.asarray(p, float) - norm(d) * 0.4, d, 0.0)
        nrm = -norm(d)
        pts.append(hit + nrm * (out + thickness))
        nrms.append(nrm)
    # resample smoothly
    fine, fn = [], []
    for i in range(len(pts) - 1):
        for k in range(samples):
            u = k / samples
            fine.append(catmull([(j, pts[j]) for j in range(len(pts))], i + u))
            fn.append(norm(catmull([(j, nrms[j]) for j in range(len(nrms))], i + u)))
    fine.append(pts[-1])
    fn.append(nrms[-1])
    return strap(fine, fn, width, thickness, mat)


def ring_band(center, rx, ry, height, thickness, mat, segs=20, axes=None, e=4.0):
    """A band (short thick ring) around an axis: e.g. a strap around a leg or an arm band."""
    ax = np.eye(3) if axes is None else np.array([norm(a) for a in axes])
    c = np.asarray(center, float)
    pts = []
    for k in range(segs + 1):
        th = 2 * math.pi * k / segs
        pts.append(c + ax[0] * rx * math.cos(th) + ax[1] * ry * math.sin(th))
    # closed tube: duplicate ends overlap; normals point outward from the axis
    nrms = [norm(p - c) for p in pts]
    fr = frames_along(pts, nrms[0])
    rings = []
    for p, (t, _, _), nv in zip(pts, fr, nrms):
        a = norm(nv - np.dot(nv, t) * t)
        b = np.cross(t, a)
        rings.append(dict(c=p, a=a, b=b, rx=thickness, ry=height, e=e))
    return loft(rings[:-1] + [rings[0]], 6, False, False, mat, True)


def limb_rings(chain_pts, keys, ref, e=2.0):
    """Rings along a polyline (chain_pts) at arc-length fractions: keys = [(s, rx, ry), ...]
    where s is in chain-segment units (0..1 first segment, 1..2 second ...)."""
    pts = [np.asarray(p, float) for p in chain_pts]

    def at(s):
        i = min(int(math.floor(s)), len(pts) - 2) if s >= 0 else 0
        u = s - i
        return pts[i] + (pts[i + 1] - pts[i]) * u

    centers = [at(k[0]) for k in keys]
    fr = frames_along(centers, ref)
    rings = []
    for k, c, (t, a, b) in zip(keys, centers, fr):
        rings.append(dict(c=c, a=a, b=b, rx=k[1], ry=k[2], e=k[3] if len(k) > 3 else e))
    return rings


# ------------------------------------------------------------------------------------------------
# Head, face and hair
# ------------------------------------------------------------------------------------------------

HEAD_C = np.array([0.0, 0.004, 1.645])


def head_keys(female):
    # z_rel: half width, front y, back y, y of widest point
    if female:
        return [(-0.106, 0.010, -0.066, -0.050, -0.058), (-0.100, 0.022, -0.076, -0.030, -0.052),
                (-0.090, 0.030, -0.083, 0.004, -0.040), (-0.076, 0.039, -0.088, 0.028, -0.028),
                (-0.058, 0.048, -0.091, 0.050, -0.016), (-0.040, 0.057, -0.094, 0.070, -0.008),
                (-0.018, 0.065, -0.095, 0.088, 0.0), (0.006, 0.069, -0.094, 0.097, 0.005),
                (0.030, 0.071, -0.092, 0.101, 0.008), (0.055, 0.070, -0.087, 0.101, 0.01),
                (0.080, 0.065, -0.075, 0.093, 0.012), (0.098, 0.052, -0.056, 0.076, 0.012),
                (0.110, 0.034, -0.035, 0.050, 0.012), (0.116, 0.012, -0.012, 0.020, 0.01)]
    return [(-0.118, 0.012, -0.072, -0.050, -0.062), (-0.112, 0.030, -0.083, -0.025, -0.055),
            (-0.100, 0.043, -0.089, 0.010, -0.040), (-0.084, 0.054, -0.092, 0.030, -0.030),
            (-0.064, 0.062, -0.095, 0.050, -0.020), (-0.044, 0.068, -0.097, 0.070, -0.010),
            (-0.020, 0.072, -0.098, 0.088, 0.0), (0.005, 0.074, -0.097, 0.097, 0.005),
            (0.030, 0.075, -0.095, 0.102, 0.008), (0.055, 0.074, -0.089, 0.102, 0.01),
            (0.080, 0.068, -0.077, 0.094, 0.012), (0.098, 0.055, -0.058, 0.077, 0.012),
            (0.110, 0.035, -0.036, 0.050, 0.012), (0.116, 0.012, -0.012, 0.020, 0.01)]


EYE_Z = -0.008       # relative to HEAD_C (1.637 m)
EYE_X = 0.032


def _bump(x, z, cx, cz, sx, sz):
    return np.exp(-(((x - cx) / sx) ** 2 + ((z - cz) / sz) ** 2))


def build_head_grid(female):
    keys = head_keys(female)
    n_r, segs = 38, 50
    part, grid = grid_loft(keys, n_r, segs, ef=2.5, eb=2.15)
    g = grid.copy()
    x, y, z = g[..., 0], g[..., 1], g[..., 2]
    front = np.clip(-y / 0.09, 0, 1) ** 1.5
    dy = np.zeros_like(y)
    dx = np.zeros_like(x)
    for sx in (1, -1):
        dy += 0.0055 * _bump(x, z, sx * EYE_X, EYE_Z + 0.001, 0.017, 0.012) * front          # eye socket
        dy -= 0.0045 * _bump(x, z, sx * 0.03, 0.017, 0.022, 0.008) * front                 # brow ridge
        dy -= (0.004 if not female else 0.006) * _bump(x, z, sx * 0.045, -0.035, 0.018, 0.02) * front  # cheek
        dx += sx * 0.003 * _bump(x, z, sx * 0.062, -0.03, 0.012, 0.02)
    dy -= 0.004 * _bump(x, z, 0, -0.076 if not female else -0.07, 0.024, 0.016) * front  # muzzle
    dy -= (0.0045 if not female else 0.003) * _bump(x, z, 0, -0.106 if not female else -0.097, 0.014, 0.01) * front
    if not female:
        dx += np.sign(x) * 0.004 * _bump(np.abs(x), z, 0.055, -0.085, 0.02, 0.02)           # jaw
    g[..., 0] += dx
    g[..., 1] += dy
    g[..., 2] = z
    g += HEAD_C
    return g


def head_parts(female, skin, lips_mat, iris_mat):
    g = build_head_grid(female)
    head = grid_part(g, True, True, skin, bulge0=0.0, bulge1=0.004)
    bvh = make_bvh(head)
    parts = [head]

    def face_y(x, zr):
        hit = project(bvh, (x, -0.5, HEAD_C[2] + zr), (0, 1, 0))
        return hit[1]

    # nose
    nose_keys = [(0.010, 0.005, 0.001), (-0.006, 0.0065, 0.005), (-0.022, 0.0075, 0.010),
                 (-0.036, 0.0095, 0.016), (-0.044, 0.0105, 0.0175), (-0.050, 0.0085, 0.013),
                 (-0.055, 0.005, 0.005)]
    nose_keys = [(z, w * (0.7 if female else 0.92), p * (0.45 if female else 0.88)) for (z, w, p) in nose_keys
                 if not (female and z > -0.012)]
    rings = []
    for zr, w, prot in nose_keys:
        yf = face_y(0.0, zr)
        rings.append(dict(c=(0, yf + 0.006, HEAD_C[2] + zr), a=(1, 0, 0), b=(0, -1, 0), rx=w, ry=prot + 0.006,
                          e=2.2))
    # loft rings stacked downward: tangent must be a x b = (1,0,0)x(0,-1,0) = (0,0,-1): downward. good
    parts.append(loft(rings, 14, True, True, skin, True, 0.002, 0.002))
    nw = 0.0115 if not female else 0.0088
    for sx in (1, -1):
        yf = face_y(sx * nw, -0.047)
        k = 0.7 if female else 1.0
        parts.append(ellipsoid((sx * nw, yf - 0.003 * k, HEAD_C[2] - 0.047), (0.0065 * k, 0.008 * k, 0.0055 * k), mat=skin,
                               segs=10, rings=6))
    # eyes
    for sx in (1, -1):
        ex = sx * EYE_X
        yf = face_y(ex, EYE_Z)
        r = 0.0118 if not female else 0.0132
        c = np.array([ex, yf + r - 0.0058, HEAD_C[2] + EYE_Z])
        parts.append(ellipsoid(c, (r, r, r), segs=14, rings=8, mat="eye_white"))
        look = norm(np.array([-sx * 0.02, -1.0, 0.0]))
        iris_c = c + look * (r - 0.0006)
        ax = np.array([norm(np.cross(look, (0, 0, 1))), np.array([0, 0, 1.0]), look])
        ir = 0.0062 if not female else 0.0078
        parts.append(ellipsoid(iris_c, (ir, ir, 0.0012), axes=ax, segs=12, rings=5, mat=iris_mat))
        parts.append(ellipsoid(iris_c + look * 0.0009, (ir * 0.45, ir * 0.45, 0.0008), axes=ax, segs=10,
                               rings=4, mat="pupil"))
        # upper eyelid (skin) and lash line hugging the top of the eyeball
        lid2 = [c + norm(np.array([sx * math.sin(a), -math.cos(a), 1.15 if female else 0.85])) * (r + 0.0005)
                for a in np.linspace(-1.2, 1.2, 9)]
        parts.append(tube(lid2, [0.0026] * len(lid2), 6, (0, 0, 1), skin))
        lash = [c + norm(np.array([sx * math.sin(a), -math.cos(a), 0.8 if female else 0.58])) * (r + 0.0012)
                + np.array([sx * 0.0012 * max(0.0, a * sx), 0, 0.0006 * max(0.0, a * sx)]) * (2.5 if female else 1.0)
                for a in np.linspace(-1.1, 1.25, 9)]
        parts.append(tube(lash, [0.0011] * len(lash) if not female else list(np.linspace(0.0014, 0.0024, len(lash))), 6, (0, 0, 1),
                          "brow" if female else "hair_m"))
    # brows
    for sx in (1, -1):
        pts = []
        for u in np.linspace(0, 1, 6):
            x = sx * (0.012 + 0.04 * u)
            zr = 0.013 + (0.006 if not female else 0.01) * math.sin(math.pi * (u * 0.8 + 0.1)) - (0.006 * u if female else 0.0)
            yf = face_y(x, zr)
            pts.append(np.array([x, yf - 0.0015, HEAD_C[2] + zr]))
        rad = [(0.0022, 0.0045), (0.0028, 0.0055), (0.003, 0.0055), (0.0028, 0.005), (0.0024, 0.0042),
               (0.0018, 0.003)]
        if female:
            rad = [(a * 0.5, b * 0.42) for a, b in rad]
        parts.append(tube(pts, rad, 8, (0, -1, 0), "brow", e=2.0))
    # lips
    mz = -0.066 if female else -0.071
    yl = face_y(0, mz)
    ul = (0.017, 0.0055, 0.0042) if not female else (0.0145, 0.006, 0.0045)
    ll = (0.015, 0.006, 0.0048) if not female else (0.0135, 0.0066, 0.0055)
    parts.append(ellipsoid((0, yl + 0.0025, HEAD_C[2] + mz + 0.0005), ul, segs=14, rings=6, mat=lips_mat))
    yl2 = face_y(0, mz - 0.008)
    parts.append(ellipsoid((0, yl2 + 0.0028, HEAD_C[2] + mz - 0.008), ll, segs=14, rings=6, mat=lips_mat))
    # ears
    for sx in (1, -1):
        x = sx * (0.071 if not female else 0.066)
        ax = np.array([(sx, 0, 0), (0, math.cos(0.25), -math.sin(0.25)), (0, math.sin(0.25), math.cos(0.25))])
        es = 0.85 if female else 1.0
        parts.append(ellipsoid((x + sx * 0.004, 0.018, HEAD_C[2] - 0.022), (0.008 * es, 0.019 * es, 0.03 * es), axes=ax,
                               segs=12, rings=8, mat=skin))
    for p in parts:
        p.skin("head")
    return parts, g, bvh


def head_shell(grid, mask, thick, mat, base_offset=0.0012):
    """Shell over the head grid (hair cap, beard): verts pushed out along their radial normal.

    Where the mask fades out the shell sinks just below the skin, so its edge follows the smooth mask
    instead of the grid's staircase."""
    n = grid - (HEAD_C + np.array([0, 0.0, -0.01]))
    n = n / np.linalg.norm(n, axis=-1, keepdims=True)
    m = np.clip(mask, 0, 1)
    off = (-0.0025 + (base_offset + 0.0025) * smoothstep(0.0, 0.25, m) + thick * m)[..., None]
    g2 = grid + n * off
    keep = np.zeros_like(m, dtype=bool)
    near = m > 0.01
    # grow the kept region by one vertex so the sunken edge is included
    keep |= near
    keep[1:] |= near[:-1]
    keep[:-1] |= near[1:]
    keep |= np.roll(near, 1, axis=1) | np.roll(near, -1, axis=1)
    return grid_part(g2, False, False, mat, mask=keep)


def azimuth(grid):
    rel = grid - HEAD_C
    return np.arctan2(rel[..., 0], -rel[..., 1]), rel[..., 2]


def interp_curve(pts, x):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return np.interp(x, xs, ys)


def hairline(ap, female):
    """Height of the hairline (relative to HEAD_C) as a function of |azimuth| (0 = front)."""
    if female:
        pts = [(0, 0.078), (0.45, 0.074), (0.85, 0.062), (1.15, 0.044), (1.4, 0.02), (1.6, 0.02), (1.85, 0.0),
               (2.3, -0.05), (math.pi, -0.085)]
    else:
        pts = [(0, 0.074), (0.5, 0.07), (0.85, 0.058), (1.15, 0.036), (1.4, 0.008), (1.6, 0.018), (1.85, 0.0),
               (2.3, -0.05), (math.pi, -0.08)]
    return interp_curve(pts, ap)


def hair_cap_mesh(bvh_head, female, thick, mat, n_az=48, n_rows=12):
    """Hair cap whose bottom ring follows the hairline exactly (clean edge), ray-cast onto the skull."""
    o = HEAD_C + np.array([0.0, 0.006, -0.005])
    grid = np.zeros((n_rows, n_az, 3))
    for j in range(n_az):
        ps = -math.pi + 2 * math.pi * j / n_az        # seam at the back
        h = float(hairline(abs(ps), female))
        horiz = np.array([math.sin(ps), -math.cos(ps), 0.0])
        # elevation where the skull reaches the hairline height (iterate on the ray hit)
        phi = math.asin(max(-0.95, min(0.95, (h + 0.005) / 0.11)))
        for _ in range(4):
            d = horiz * math.cos(phi) + np.array([0, 0, math.sin(phi)])
            hit = project(bvh_head, o + d * 0.3, -d, 0.0)
            phi += (h - (hit[2] - HEAD_C[2])) / 0.1
        for i in range(n_rows):
            u = i / (n_rows - 1)
            el = phi + (math.pi / 2 - 0.08 - phi) * u
            d = horiz * math.cos(el) + np.array([0, 0, math.sin(el)])
            hit = project(bvh_head, o + d * 0.3, -d, 0.0)
            top = smoothstep(0.03, 0.11, hit[2] - HEAD_C[2])
            if female:
                t = thick * (0.7 + 0.6 * top)
            else:
                t = thick * (0.55 + 0.45 * top)
            off = 0.0006 + t * sstep(0.0, 0.35, u) ** 0.7
            grid[i, j] = hit + norm(hit - o) * off
    # rows run upward and the azimuth runs counter-clockwise seen from above: quads face outward
    return grid_part(grid, False, True, mat, bulge1=0.004)


def hair_lock(bvh, root, direction, length, width, thick, lift, mat, steps=7, sink=0.003, bend=None,
              taper=0.62, skin_bvh=None, arch=0.0, segs=8, root_w=1.0):
    """A soft lock of hair: a flat, rounded ribbon that follows the surface under it (bvh) from root along
    `direction`, lifting off it toward the tip (lift) and, with arch, standing off it along the middle the way
    a lock of full hair does. bend(u) -> extra direction added along the way. root_w < 1 narrows the lock where it
    leaves the scalp, so it grows out of the hair under it instead of starting blunt."""
    o = HEAD_C + np.array([0.0, 0.0, -0.01])
    d = norm(direction)
    p = np.asarray(root, float)
    pts, nrms = [], []
    for k in range(steps + 1):
        u = k / steps
        radial = norm(p - o)
        hit = project(bvh, o + radial * 0.3, -radial, 0.0)
        nrm = norm(hit - o)
        off = -sink + (sink + thick * 0.6 + 0.0008) * sstep(0.0, 0.25, u) + lift * u * u + arch * math.sin(math.pi * u)
        q = hit + nrm * off
        if skin_bvh is not None:
            # keep clear of the face: never closer than a few mm to the skin under it
            sk = project(skin_bvh, o + norm(q - o) * 0.3, -norm(q - o), 0.0)
            gap = np.dot(q - sk, norm(q - o))
            if gap < thick * 0.6 + 0.0025:
                q = q + norm(q - o) * (thick * 0.6 + 0.0025 - gap)
        pts.append(q)
        nrms.append(nrm)
        dd = d + (bend(u) if bend else 0.0)
        dt = norm(dd - np.dot(dd, nrm) * nrm)
        p = hit + dt * (length / steps)
        d = norm(dd)
    fr = frames_along(pts, nrms[0])
    rings = []
    for i, (q, (t, _, _), nv) in enumerate(zip(pts, fr, nrms)):
        u = i / steps
        a = norm(nv - np.dot(nv, t) * t)
        b = np.cross(t, a)
        k = (1.0 - taper * u ** 1.6) * (root_w + (1.0 - root_w) * sstep(0.0, 0.3, u))
        rings.append(dict(c=q, a=a, b=b, rx=thick * (1.0 - 0.45 * u) * (root_w + (1.0 - root_w) * sstep(0.0, 0.3, u)),
                          ry=width * 0.5 * k, e=2.3))
    return loft(rings, segs, True, True, mat, True, 0.002, 0.004)


# Height of the goggle strap above HEAD_C at the front and at the back (it runs between them around the head).
GOGGLE_Z = {True: (0.078, 0.035), False: (0.072, 0.03)}
STRAP_HALF = 0.011            # half the strap's height (goggles: strap width)
STRAP_INNER = 0.0012          # how far the inside of the strap stands off the hair cap


def tuck_under_strap(part, capbvh, female, half=STRAP_HALF + 0.003, fade=0.01):
    """Press the hair where the goggle strap crosses it down onto the cap, under the strap, fading back to its
    own shape above and below, so locks pass under the strap instead of through it."""
    front_z, back_z = GOGGLE_Z[female]
    rel = part.v - HEAD_C
    ps = np.arctan2(rel[:, 0], -rel[:, 1])
    zs = back_z + (front_z - back_z) * (0.5 + 0.5 * np.cos(ps))
    w = 1.0 - smoothstep(half, half + fade, np.abs(rel[:, 2] - zs))
    for i in np.nonzero(w > 0)[0]:
        horiz = np.array([rel[i, 0], rel[i, 1], 0.0])
        r = np.linalg.norm(horiz)
        if r < 1e-6:
            continue
        dirv = horiz / r
        o = np.array([HEAD_C[0], HEAD_C[1], part.v[i, 2]])
        hit = project(capbvh, o + dirv * 0.3, -dirv, 0.0)
        limit = np.linalg.norm((hit - o)[:2]) + STRAP_INNER
        if r > limit:
            part.v[i, :2] = HEAD_C[:2] + dirv[:2] * (r - w[i] * (r - limit))
    return part


def hair_cap_and_tufts(grid, bvh_head, female, hair_mat, seed):
    """Soft, messy swept-up hair of overlapping locks (male) or a smooth pulled-back cap with a fringe and
    face-framing strands (female) over the head grid."""
    psi, zr = azimuth(grid)
    ap = np.abs(psi)
    hl = hairline(ap, female)
    mask = smoothstep(hl - 0.006, hl + 0.022, zr)
    thick = 0.02 if not female else 0.012
    cap = hair_cap_mesh(bvh_head, female, thick, hair_mat)
    parts = [cap]
    # the outer surface: hair cap over the skull (a ray below the cap's edge must land on the skin, not
    # on the inside of the cap's far side)
    capbvh = make_bvh(merge([cap, grid_part(grid, True, True, hair_mat)]))
    rng = random.Random(seed)
    o = HEAD_C + np.array([0.0, 0.0, -0.01])

    def root_at(ps, zrel):
        dirv = np.array([math.sin(ps), -math.cos(ps), 0.0])
        el = math.asin(max(-0.9, min(0.97, zrel / 0.115)))
        d = dirv * math.cos(el) + np.array([0.0, 0.0, math.sin(el)])
        return project(capbvh, o + d * 0.3, -d, 0.0)

    if not female:
        # rows of locks from the crown outward; front locks sweep up and back with a lean to one side,
        # the top flows back, the sides and the back lie down toward the neck
        locks = []
        for zrel, n_az, ps_max in ((0.114, 4, 1.0), (0.104, 8, 2.2), (0.092, 12, 2.7), (0.078, 14, 3.0),
                                   (0.062, 15, 3.0), (0.045, 13, 3.0), (0.028, 12, 3.0), (0.012, 8, 3.0)):
            for k in range(n_az):
                ps = -ps_max + 2 * ps_max * (k + 0.5) / n_az + rng.uniform(-0.12, 0.12)
                locks.append((ps, zrel + rng.uniform(-0.006, 0.006)))
        for ps, zrel in locks:
            ap_ = abs(ps)
            if zrel < float(hairline(ap_, False)) + 0.012:
                continue
            if ap_ > 2.0 and zrel < 0.07:
                continue                               # the nape stays short: the cap's own strands show
            root = root_at(ps, zrel)
            sx = math.copysign(1.0, ps) if ap_ > 0.05 else 1.0
            side = np.array([sx, 0.0, 0.0])
            jit = np.array([rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)]) * 0.22
            if ap_ < 0.95 and zrel > 0.06:            # front: swept up and back, leaning to the left
                d = np.array([0.35, 0.55, 0.8]) + side * 0.15 + jit
                length, lift, width = rng.uniform(0.05, 0.066), rng.uniform(0.006, 0.012), rng.uniform(0.034, 0.042)

                def bend(u, s=sx):
                    return np.array([0.1 * s, 0.25, -0.25]) * u
            elif zrel > 0.09:                          # crown: back and out
                d = np.array([0.25 * sx, 1.0, 0.0]) + jit
                length, lift, width = rng.uniform(0.045, 0.06), rng.uniform(0.004, 0.009), rng.uniform(0.034, 0.042)
                bend = None
            elif ap_ < 2.1:                            # sides: back and down past the ears
                d = np.array([0.1 * sx, 0.75, -0.65]) + jit
                length, lift, width = rng.uniform(0.032, 0.045), rng.uniform(0.002, 0.005), rng.uniform(0.03, 0.038)
                bend = None
            else:                                      # back: down toward the neck
                d = np.array([0.15 * sx, 0.15, -1.0]) + jit
                length, lift, width = rng.uniform(0.03, 0.04), rng.uniform(0.0, 0.002), rng.uniform(0.03, 0.038)
                bend = None
            back = ap_ > 1.4 and zrel < 0.09
            parts.append(hair_lock(capbvh, root, d, length, width, 0.0065 if not back else 0.005, lift, hair_mat,
                                   bend=bend, taper=0.55 if not back else 0.8))
        # the back below the strap: short locks in staggered rows, each overlapping the one under it, so the
        # back reads as layered hair down to the nape
        for zrel, n, k0 in ((0.088, 8, 0.0), (0.062, 9, 0.5), (0.014, 10, 0.0), (-0.016, 10, 0.5),
                            (-0.044, 9, 0.0)):
            for k in range(n):
                ps = math.pi + (k + k0 - (n - 1) / 2) * 0.25 + rng.uniform(-0.1, 0.1)
                zr_ = zrel + rng.uniform(-0.007, 0.007)
                if zr_ < float(hairline(abs(ps), False)) + 0.014:
                    continue
                root = root_at(ps, zr_)
                d = np.array([0.2 * math.sin(ps), 0.2, -1.0]) + np.array([rng.uniform(-0.3, 0.3), 0.0, 0.0])
                parts.append(hair_lock(capbvh, root, d, rng.uniform(0.04, 0.056), rng.uniform(0.016, 0.026),
                                       0.0045, rng.uniform(0.0, 0.002), hair_mat, steps=7, sink=0.002,
                                       taper=rng.uniform(0.45, 0.65), root_w=0.5))
        for p in parts[1:]:
            tuck_under_strap(p, capbvh, False)
    else:
        skin_bvh = bvh_head
        # fringe: strands from under the goggle strap, swept down and to the sides across the forehead
        for ps, length, width, sweep in ((-0.5, 0.05, 0.022, -0.8), (-0.3, 0.056, 0.024, -0.6),
                                         (-0.12, 0.05, 0.022, -0.35), (0.06, 0.054, 0.024, 0.5),
                                         (0.24, 0.058, 0.024, 0.7), (0.44, 0.05, 0.022, 0.9)):
            ps += rng.uniform(-0.04, 0.04)
            root = root_at(ps, 0.074)
            d = np.array([sweep * 0.55, 0.0, -1.0])

            def bend(u, s=sweep):
                return np.array([s * 0.5, 0.0, 0.25]) * u
            parts.append(hair_lock(bvh_head, root, d, length, width, 0.004, 0.003, hair_mat, steps=7, sink=-0.002,
                                   bend=bend, taper=0.75, skin_bvh=skin_bvh))
        # loose strands framing the face, in front of the ears down to the jaw
        for sx in (1.0, -1.0):
            for ps, zrel, length, width in ((1.0, 0.045, 0.1, 0.014), (1.16, 0.04, 0.085, 0.012)):
                root = root_at(sx * ps, zrel)
                d = np.array([0.08 * sx, -0.25, -1.0])

                def bend(u, s=sx):
                    return np.array([0.05 * s, 0.45, 0.15]) * u
                parts.append(hair_lock(bvh_head, root, d, length, width, 0.0035, 0.006, hair_mat, steps=8,
                                       sink=-0.003, bend=bend, taper=0.8, skin_bvh=skin_bvh))
        # the rest is pulled back to the bun at the nape: full locks of varied length and width that leave the
        # hairline and the side parting, stand off the cap along their middle (volume) and leave gaps between them
        # where the darker cap shows as depth, so it reads as hair, not a helmet
        bun = HEAD_C + np.array([0.0, 0.09, -0.062])
        part_ps = 0.3                                  # the parting, on her left
        rows = (  # zrel, locks, half span (rad), length range, width range, thickness, arch
            (0.093, 10, 1.3, (0.15, 0.19), (0.018, 0.03), 0.0075, 0.009),
            (0.108, 6, 0.75, (0.13, 0.17), (0.018, 0.028), 0.007, 0.008),
            (0.072, 12, 2.25, (0.085, 0.115), (0.018, 0.028), 0.0065, 0.006),
            (0.045, 14, 2.9, (0.07, 0.095), (0.016, 0.026), 0.006, 0.0045),
            (0.015, 13, 3.05, (0.055, 0.075), (0.016, 0.024), 0.0055, 0.003),
        )
        for zrel, n_az, ps_max, (l0, l1), (w0, w1), th, arch in rows:
            for k in range(n_az):
                ps = -ps_max + 2 * ps_max * (k + 0.5) / n_az + rng.uniform(-0.1, 0.1)
                zr_ = zrel + rng.uniform(-0.006, 0.006)
                if zr_ < float(hairline(abs(ps), True)) + 0.012:
                    continue
                root = root_at(ps, zr_)
                # away from the parting near it, then round toward the bun
                away = math.copysign(1.0, ps - part_ps) * math.exp(-((ps - part_ps) / 0.55) ** 2)
                d = norm(bun - root) + np.array([0.55 * away, 0.0, 0.25 * away * away]) + \
                    np.array([rng.uniform(-0.12, 0.12), 0.0, rng.uniform(-0.05, 0.1)])

                def bend(u, r=root):
                    return norm(bun - r) * 0.5 * u
                length = rng.uniform(l0, l1)
                parts.append(hair_lock(capbvh, root, d, length, rng.uniform(w0, w1),
                                       th * rng.uniform(0.85, 1.15), rng.uniform(0.0, 0.004), hair_mat,
                                       steps=max(10, int(length / 0.011)), sink=0.002, bend=bend,
                                       taper=rng.uniform(0.65, 0.85), arch=arch * rng.uniform(0.4, 1.0), root_w=0.55))
        # the crown and the back of the head: locks from the crown down over the back to the bun
        for zrel, n, spread, (l0, l1), k0 in ((0.104, 11, 0.27, (0.08, 0.105), 0.0),
                                               (0.08, 10, 0.27, (0.06, 0.08), 0.5)):
            for k in range(n):
                ps = math.pi + (k + k0 - (n - 1) / 2) * spread + rng.uniform(-0.07, 0.07)
                root = root_at(ps, zrel + rng.uniform(-0.005, 0.005))
                d = norm(bun - root) + np.array([rng.uniform(-0.2, 0.2), 0.0, 0.0])

                def bend(u, r=root):
                    return norm(bun - r) * 0.5 * u
                parts.append(hair_lock(capbvh, root, d, rng.uniform(l0, l1), rng.uniform(0.013, 0.022),
                                       0.0065 * rng.uniform(0.85, 1.15), rng.uniform(0.0, 0.003), hair_mat, steps=10,
                                       sink=0.003, bend=bend, taper=rng.uniform(0.65, 0.85),
                                       arch=0.008 * rng.uniform(0.4, 1.0), root_w=0.4))
        # the nape: short locks swept up into the bun
        for k in range(9):
            ps = math.pi + (k - 4) * 0.28 + rng.uniform(-0.08, 0.08)
            zr_ = -0.055 + rng.uniform(-0.01, 0.01) + 0.02 * abs(k - 4) / 4
            root = root_at(ps, zr_)
            d = norm(bun - root) + np.array([rng.uniform(-0.15, 0.15), 0.0, 0.0])
            parts.append(hair_lock(capbvh, root, d, rng.uniform(0.035, 0.05), rng.uniform(0.016, 0.024), 0.005,
                                   0.0015, hair_mat, steps=7, sink=0.002, taper=0.8, arch=0.0015, root_w=0.5))
        # flyaway strands: thin wisps off the crown and the parting that lift clear of the rest
        for k in range(8):
            ps = rng.uniform(-1.6, 1.8)
            zr_ = rng.uniform(0.085, 0.105)
            root = root_at(ps, zr_)
            sx = rng.choice((-1.0, 1.0))
            d = norm(bun - root) + np.array([sx * rng.uniform(0.3, 0.7), 0.0, rng.uniform(0.2, 0.5)])

            def bend(u, s=sx):
                return np.array([0.5 * s, 0.3, -0.8]) * u
            parts.append(hair_lock(capbvh, root, d, rng.uniform(0.045, 0.07), rng.uniform(0.004, 0.006), 0.0018,
                                   rng.uniform(0.004, 0.009), hair_mat, steps=8, sink=0.001, bend=bend, taper=0.85,
                                   arch=rng.uniform(0.003, 0.006), segs=6))
        for p in parts[1:]:
            tuck_under_strap(p, capbvh, True)
    for p in parts:
        p.skin("head")
    return parts, mask


def beard_parts(grid, mat):
    psi, zr = azimuth(grid)
    ap = np.abs(psi)
    x = grid[..., 0]
    bt = interp_curve([(0, -0.057), (0.22, -0.055), (0.42, -0.047), (0.7, -0.032), (1.0, -0.016),
                       (1.25, 0.002), (1.42, 0.018), (1.55, 0.024)], ap)
    m = 1 - smoothstep(bt - 0.007, bt + 0.009, zr)
    m *= 1 - smoothstep(1.42, 1.58, ap)
    ell = (x / 0.0205) ** 2 + ((zr + 0.0748) / 0.0098) ** 2
    m *= smoothstep(1.0, 1.7, ell)
    under = smoothstep(-0.07, -0.11, zr)  # underside of the jaw
    m *= 1 - under * smoothstep(0.9, 1.3, ap)
    th = 0.005 + 0.004 * smoothstep(-0.07, -0.11, zr) * (1 - smoothstep(0.3, 0.9, ap))
    th += 0.002 * _bump(x, zr, 0, -0.062, 0.02, 0.006)
    part = head_shell(grid, m, th, mat, base_offset=0.0008)
    part.skin("head")
    return [part]


def goggles(head_bvh, front_z, back_z, strap_mat, frame_mat, lens_mat, rim_mat, tilt=0.85, cup_r=0.021):
    """Goggles pushed up on the forehead: a strap around the head and two lens cups at the front."""
    pts, nrms = [], []
    n = 36
    for k in range(n + 1):
        ps = 2 * math.pi * k / n
        dirv = np.array([math.sin(ps), -math.cos(ps), 0.0])
        zr = back_z + (front_z - back_z) * (0.5 + 0.5 * math.cos(ps))
        o = HEAD_C + np.array([0, 0, zr]) + dirv * 0.3
        hit = project(head_bvh, o, -dirv, 0.0)
        pts.append(hit + dirv * 0.0045)
        nrms.append(dirv)
    parts = [strap(pts, nrms, 0.011, 0.0028, strap_mat, segs=6)]
    for sx in (1, -1):
        ps = sx * 0.36
        dirv = np.array([math.sin(ps), -math.cos(ps), 0.0])
        zr = back_z + (front_z - back_z) * (0.5 + 0.5 * math.cos(ps))
        o = HEAD_C + np.array([0, 0, zr]) + dirv * 0.3
        base = project(head_bvh, o, -dirv, 0.0) + dirv * 0.004
        axis = norm(dirv + np.array([0, 0, tilt]))
        top = base + axis * 0.018
        parts.append(cylinder(base, top, cup_r, 16, frame_mat, ref=(0, 0, 1)))
        parts.append(ring_band(top - axis * 0.002, cup_r + 0.0015, cup_r + 0.0015, 0.0035, 0.0028, rim_mat, 18,
                               axes=(norm(np.cross(axis, (0, 0, 1))), norm(np.cross(axis, np.cross(axis, (0, 0, 1)))),
                                     axis)))
        ax = np.array([norm(np.cross(axis, (0, 0, 1))), norm(np.cross(np.cross(axis, (0, 0, 1)), axis)), axis])
        parts.append(ellipsoid(top + axis * 0.0005, (cup_r * 0.86, cup_r * 0.86, 0.004), axes=ax, segs=16, rings=5,
                               mat=lens_mat))
    # bridge between the cups
    c = HEAD_C + np.array([0, 0, front_z])
    hit = project(head_bvh, c + np.array([0, -0.3, 0]), (0, 1, 0), 0.0)
    parts.append(tube([hit + np.array([-0.018, -0.006, 0.002]), hit + np.array([0, -0.009, 0.004]),
                       hit + np.array([0.018, -0.006, 0.002])], 0.004, 8, (0, 0, 1), frame_mat))
    for p in parts:
        p.skin("head")
    return parts


def neck_part(rx, ry, mat):
    keys = [(1.40, rx * 1.12, -ry * 0.95, ry * 1.05, 0.018), (1.47, rx * 1.02, -ry * 0.92, ry * 1.0, 0.016),
            (1.53, rx, -ry * 0.9, ry, 0.018), (1.6, rx * 0.98, -ry * 0.95, ry * 1.02, 0.022)]
    p, _ = grid_loft(keys, 8, 20, cap0=False, cap1=False, mat=mat)
    return p.skin(neck_weights)


# ------------------------------------------------------------------------------------------------
# Arms and hands
# ------------------------------------------------------------------------------------------------

def arm_chain_points(side="l"):
    return [bone_head(f"upper_arm_{side}"), bone_head(f"forearm_{side}"), bone_head(f"hand_{side}")]


def sleeve(keys, mat, segs=18):
    rings = limb_rings(arm_chain_points("l"), keys, (0, -1, 0))
    p = loft(rings, segs, True, True, mat, True, 0.01, 0.0).tagged("sleeve")
    return p.skin(chain_weights(["shoulder_l", "upper_arm_l", "forearm_l", "hand_l"], start=bone_head("shoulder_l"),
                                blends=[0.08, 0.08, 0.05]))


def arm_point(s, outward=0.0, forward=0.0):
    """Point on the left arm axis at segment parameter s, offset outward (away from body) / forward."""
    pts = arm_chain_points("l")
    i = min(int(s), 1)
    u = s - i
    c = pts[i] + (pts[i + 1] - pts[i]) * u
    t = norm(pts[i + 1] - pts[i])
    out = norm(np.cross((0, -1, 0), t))
    fwd = norm(np.cross(t, out))
    return c + out * outward + fwd * forward, t, out, fwd


# The bones first-person arms are cut from (CharacterRig.ArmBones in src/Mechanica.Core/Characters).
FIRST_PERSON_ARM_BONES = {f"{b}_{s}" for b in ("forearm", "hand", "fingers", "thumb") for s in ("l", "r")}


def _export_triangles(part):
    """The triangles of `part` as the glTF export writes them (Blender's own split of each quad)."""
    me = bpy.data.meshes.new("_tmp_tris")
    me.from_pydata(part.v.tolist(), [], part.f)
    me.validate(clean_customdata=False)
    me.update()
    me.calc_loop_triangles()
    tris = np.zeros(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tris)
    polys = np.zeros(len(me.loop_triangles), dtype=np.int64)
    me.loop_triangles.foreach_get("polygon_index", polys)
    bpy.data.meshes.remove(me)
    return tris.reshape(-1, 3), polys


def _settle_arm_ties(parts, margin=0.025):
    """Tip vertices whose strongest arm and body bones weigh the same (a ring right on the elbow) to the body, so
    the cut cannot fall either way after the export rounds the weights."""
    for p in parts:
        if p.w is None:
            continue
        n = len(p.v)
        body = [b for b in p.w if b not in FIRST_PERSON_ARM_BONES]
        limb = [b for b in p.w if b in FIRST_PERSON_ARM_BONES]
        if not body or not limb:
            continue
        wb = np.stack([np.asarray(p.w[b], float) * np.ones(n) for b in body], axis=1)
        wa = np.stack([np.asarray(p.w[b], float) * np.ones(n) for b in limb], axis=1)
        tie = np.abs(wb.max(axis=1) - wa.max(axis=1)) < margin
        if tie.any():
            best = wb.argmax(axis=1)
            for j, b in enumerate(body):
                sel = tie & (best == j)
                if sel.any():
                    p.w[b] = np.asarray(p.w[b], float) * np.ones(n)
                    p.w[b][sel] += margin


def arm_cut_caps(parts, depth=0.014, hem_width=0.0035):
    """Dark caps closing the sleeves where first-person arms are cut from the body.

    The game keeps the triangles whose three corners follow an arm bone most (CharacterRig.ArmTriangles), so a
    sleeve crossing the elbow is cut there into an open tube. This finds the edge of that cut exactly (on the
    triangles the export writes) and closes each opening with a narrow hem of the sleeve's own cloth turned in
    (`hem_width`), so the edge reads as fabric with a thickness, and inside it a shallow funnel of `void` (a black,
    unlit-looking material) sunk `depth` toward the hand. Its rim reuses the cut edge's own positions and weights,
    so it moves with the sleeve and leaves no gap; on the full body it sits inside the sleeve and is never seen."""
    _settle_arm_ties(parts)
    part = merge(parts)
    names, W = finalize_weights(part.w, len(part.v))
    arm = np.isin(np.array(names)[W.argmax(axis=1)], sorted(FIRST_PERSON_ARM_BONES))
    tris, polys = _export_triangles(part)
    kept = arm[tris].all(axis=1)
    # directed edges of kept triangles that border a dropped triangle of the same input part
    owner = {}
    for k, (t, poly) in enumerate(zip(tris, polys)):
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            owner.setdefault((min(a, b), max(a, b)), []).append((k, a, b, part.pid[poly]))
    nxt = {}
    owner_face = {}
    for users in owner.values():
        if len(users) != 2 or users[0][3] != users[1][3] or kept[users[0][0]] == kept[users[1][0]]:
            continue
        k, a, b, _ = users[0] if kept[users[0][0]] else users[1]
        nxt[b] = a                                       # the cap runs the shared edge the other way round
        owner_face[b] = k
    caps = []
    done = set()
    for start in sorted(nxt):
        if start in done:
            continue
        loop = [start]
        while nxt.get(loop[-1]) is not None and nxt[loop[-1]] != start and len(loop) <= len(nxt):
            loop.append(nxt[loop[-1]])
        done.update(loop)
        if len(loop) < 3 or nxt.get(loop[-1]) != start:
            continue
        idx = np.array(loop)
        rim = part.v[idx]
        side = "l" if rim[:, 0].mean() > 0 else "r"
        toward_hand = norm(bone_tail(f"forearm_{side}") - bone_head(f"forearm_{side}"))
        mid = rim.mean(axis=0)
        # a hem: the cloth's own thickness turned in at the edge, then the black inside
        hem = rim + (mid - rim) * (hem_width / np.linalg.norm(mid - rim, axis=1))[:, None] + toward_hand * 0.002
        centre = mid + toward_hand * depth
        n = len(idx)
        verts = np.vstack([rim, hem, hem, centre])
        faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        faces += [(2 * n + i, 2 * n + (i + 1) % n, 3 * n) for i in range(n)]
        cloth = part.m[polys[owner_face[loop[0]]]]
        cap = Part(verts, faces, [cloth] * n + ["void"] * n, smooth=False)
        w = np.vstack([W[idx], W[idx], W[idx], W[idx].mean(axis=0)])
        cap.w = {b: w[:, j] for j, b in enumerate(names) if w[:, j].any()}
        caps.append(cap)
    return caps


def hand_local(along, across, palm):
    d, w, n = hand_frame("l")
    return bone_head("hand_l") + d * along + w * across + n * palm


def glove(palm_mat, finger_mat, tip_mat, cuff_mat, cuff_keys, accent_mat=None, fingerless=True,
          female=False):
    """Left hand (glove, fingers, thumb, cuff). Mirror for the right hand."""
    d, w, n = hand_frame("l")
    sc = 0.93 if female else 1.0
    parts = []
    rings = []
    for along, hw, ht, off in [(-0.01, 0.027, 0.019, 0.0), (0.02, 0.035, 0.018, 0.001), (0.05, 0.042, 0.0165, 0.0015),
                               (0.08, 0.044, 0.0145, 0.001), (0.094, 0.040, 0.012, 0.0)]:
        rings.append(dict(c=hand_local(along * sc, 0.0, off), a=w, b=n, rx=hw * sc, ry=ht * sc, e=2.6))
    # tangent = w x n = d (left hand): rings progress along d
    palm = loft(rings, 16, True, True, palm_mat, True, 0.0, 0.004)
    parts.append(palm.skin(chain_weights(["forearm_l", "hand_l", "fingers_l"], blends=[0.05, 0.03])))
    # fingers
    for across, length, r in [(0.029, 0.078, 0.0092), (0.0095, 0.086, 0.0096), (-0.0095, 0.081, 0.0092),
                              (-0.028, 0.066, 0.0082)]:
        across *= sc
        length *= sc
        r *= sc
        pts = [hand_local(0.074 * sc + length * u, across, 0.001 - 0.002 * u) for u in np.linspace(0, 1, 6)]
        rad = [r * k for k in (1.02, 1.0, 0.97, 0.94, 0.9, 0.82)]
        f = tube(pts, rad, 8, n, finger_mat, cap=True, bulge=0.004)
        if fingerless:
            cut = 0.074 * sc + length * 0.42
            assign_mats(f, lambda c, cut=cut: tip_mat if (c - bone_head("hand_l")) @ d > cut else None)
        parts.append(f.skin(chain_weights(["hand_l", "fingers_l"], blend=0.025)))
    # thumb
    pts = [hand_local(0.012 * sc, 0.022 * sc, 0.006), hand_local(0.038 * sc, 0.042 * sc, 0.008),
           hand_local(0.06 * sc, 0.056 * sc, 0.006), hand_local(0.08 * sc, 0.066 * sc, 0.003)]
    th = tube(pts, [0.015 * sc, 0.0118 * sc, 0.0105 * sc, 0.0092 * sc], 8, n, finger_mat, cap=True, bulge=0.004)
    if fingerless:
        assign_mats(th, lambda c: tip_mat if (c - bone_head("hand_l")) @ d > 0.068 * sc else None)
    parts.append(th.skin(chain_weights(["hand_l", "thumb_l"], blend=0.03)))
    # cuff
    rings = []
    for along, hw, ht in cuff_keys:
        rings.append(dict(c=hand_local(along, 0.0, 0.0), a=w, b=n, rx=hw, ry=ht, e=2.4))
    cuff = loft(rings, 16, True, True, cuff_mat, True)
    parts.append(cuff.skin(chain_weights(["forearm_l", "hand_l"], blend=0.04)))
    if accent_mat:
        # orange strip across the back of the hand + knuckle bar
        k1 = loft([dict(c=hand_local(along, 0.0, -0.0145 * sc), a=-n, b=w, rx=0.0035, ry=0.032 * sc, e=4)
                   for along in (0.046 * sc, 0.06 * sc)], 10, True, True, accent_mat, True)
        parts.append(k1.skin("hand_l"))
    return parts


# ------------------------------------------------------------------------------------------------
# Legs and boots
# ------------------------------------------------------------------------------------------------

def leg_chain_points(side="l"):
    return [bone_head(f"thigh_{side}"), bone_head(f"shin_{side}"), bone_head(f"foot_{side}")]


def leg_point(s, outward=0.0, forward=0.0):
    pts = leg_chain_points("l")
    i = min(int(s), 1)
    u = s - i
    c = pts[i] + (pts[i + 1] - pts[i]) * u
    t = norm(pts[i + 1] - pts[i])
    fwd = norm(np.array([0, -1.0, 0]) - np.dot((0, -1, 0), t) * t)
    out = norm(np.cross(fwd, t))  # +X for the left leg
    if out[0] < 0:
        out = -out
    return c + out * outward + fwd * forward, t, out, fwd


def trouser_leg(keys, mat, segs=16):
    rings = limb_rings(leg_chain_points("l"), keys, (0, -1, 0))
    p = loft(rings, segs, True, True, mat, True).tagged("leg")
    return p.skin(leg_weights("l"))


def boot(main, toe_mat, heel_mat, sole_mat, collar_mat, lace_mat, female=False):
    x0 = bone_head("foot_l")[0]
    sc = 0.94 if female else 1.0
    keys = [(0.088, 0.036, 0.03, 0.085), (0.07, 0.046, 0.026, 0.125), (0.03, 0.05, 0.026, 0.152),
            (-0.02, 0.053, 0.026, 0.132), (-0.07, 0.056, 0.026, 0.102), (-0.12, 0.056, 0.026, 0.084),
            (-0.16, 0.051, 0.026, 0.074), (-0.185, 0.041, 0.029, 0.064), (-0.198, 0.024, 0.034, 0.054)]
    rings = []
    for y, wv, zb, zt in keys:
        rings.append(dict(c=(x0, y * sc + 0.003, (zb + zt) / 2), a=(1, 0, 0), b=(0, 0, 1), rx=wv * sc,
                          ry=(zt - zb) / 2, e=2.7))
    foot = loft(rings, 16, True, True, main, True, 0.006, 0.008)

    def zone(c):
        y, z = c[1], c[2]
        if y < -0.148 * sc:
            return toe_mat
        if y > 0.058 and z < 0.11:
            return heel_mat
        return None
    assign_mats(foot, zone)
    parts = [foot]
    shaft = loft([dict(c=(x0, 0.024, z), a=(1, 0, 0), b=(0, 1, 0), rx=r * sc, ry=r * 1.07 * sc, e=2.3)
                  for z, r in ((0.09, 0.05), (0.15, 0.052), (0.2, 0.053), (0.245, 0.055))], 20, True, True, main)
    assign_mats(shaft, lambda c: heel_mat if (c[1] > 0.06 and c[2] < 0.11) else None)
    parts.append(shaft)
    parts.append(ring_band((x0, 0.024, 0.245), 0.058 * sc, 0.062 * sc, 0.011, 0.006, collar_mat, 20))
    # sole
    srings = []
    for y, wv, zb, zt in [(0.096, 0.036, 0, 0.03), (0.085, 0.048, 0, 0.034), (0.03, 0.054, 0, 0.034),
                          (-0.02, 0.054, 0, 0.026), (-0.07, 0.061, 0, 0.026), (-0.12, 0.062, 0, 0.028),
                          (-0.165, 0.057, 0, 0.03), (-0.192, 0.045, 0.002, 0.034), (-0.206, 0.026, 0.006, 0.036)]:
        srings.append(dict(c=(x0, y * sc + 0.003, (zb + zt) / 2), a=(1, 0, 0), b=(0, 0, 1), rx=wv * sc,
                           ry=(zt - zb) / 2, e=6.0))
    parts.append(loft(srings, 16, True, True, sole_mat, True, 0.004, 0.006))
    # laces: little cross bars down the front of the shaft and instep
    for k, z in enumerate(np.linspace(0.105, 0.235, 6)):
        y = 0.024 - 0.058 * 1.07 * sc - 0.002
        parts.append(box((x0, y, z), (0.022 * sc, 0.003, 0.003), mat=lace_mat))
    for k, (y, z) in enumerate([(-0.055, 0.112), (-0.03, 0.124)]):
        parts.append(box((x0, y * sc, z + 0.005), (0.02 * sc, 0.0035, 0.003),
                         axes=((1, 0, 0), (0, 1, -0.6), (0, 0.6, 1)), mat=lace_mat))
    out = []
    for p in parts:
        out.append(p.skin(chain_weights(["shin_l", "foot_l", "toe_l"], blends=[0.07, 0.035])))
    return out


# =====================================================================================================
# Characters
# =====================================================================================================

def both_sides(parts):
    """Parts built for the left side + their mirrored right-side copies."""
    out = []
    for p in parts:
        out.append(p)
        out.append(p.mirrored())
    return out


def front_point(bvh, x, z, off=0.0):
    """The front of a surface at (x, z), pushed off it toward the viewer by off."""
    return project(bvh, (x, -0.6, z), (0, 1, 0), off)


def patch(bvh, p, d, half, mat, up=(0, 0, 1), e=5.0, lift=0.0):
    """A flat soft-box patch lying on a surface hit by a ray from p along d."""
    d = norm(d)
    hit = project(bvh, np.asarray(p) - d * 0.4, d, 0.0)
    nrm = -d
    upv = norm(np.asarray(up) - np.dot(up, nrm) * nrm)
    side = np.cross(upv, nrm)
    return soft_box(hit + nrm * (half[1] * 0.6 + lift), half, axes=(side, nrm, upv), e=e, e2=e, segs=16, rings=6,
                    mat=mat)


def jacket_torso_keys():
    return [(z, w * 1.05, yf * 1.03, yb, yc) for z, w, yf, yb, yc in _JACKET_KEYS]


_JACKET_KEYS = [(0.99, 0.166, -0.118, 0.118, 0.0), (1.04, 0.168, -0.12, 0.12, 0.0), (1.12, 0.172, -0.124, 0.12, 0.0),
            (1.22, 0.179, -0.132, 0.123, -0.002), (1.30, 0.186, -0.136, 0.125, -0.003),
            (1.37, 0.19, -0.13, 0.122, 0.0), (1.42, 0.184, -0.113, 0.115, 0.004),
            (1.455, 0.158, -0.095, 0.104, 0.008), (1.48, 0.118, -0.08, 0.09, 0.01), (1.50, 0.07, -0.066, 0.076, 0.01)]


def collar(center_y, z0, height, rx, ry, gap, mat, piping_mat=None, flare=0.25, thickness=0.006):
    """Stand-up collar: an arc band around the neck, open by `gap` radians at the front."""
    pts, nrms = [], []
    for ps in np.linspace(gap, 2 * math.pi - gap, 22):
        dirv = np.array([math.sin(ps), -math.cos(ps), 0.0])
        pts.append(np.array([0, center_y, z0 + height / 2]) + np.array([dirv[0] * rx, dirv[1] * ry, 0]))
        nrms.append(norm(dirv - np.array([0, 0, flare])))
    band = strap(pts, nrms, height / 2, thickness, mat, segs=8)
    parts = [band]
    if piping_mat:
        top = []
        for p, nv in zip(pts, nrms):
            up = norm(np.array([0, 0, 1.0]) + np.asarray(nv) * flare)
            top.append(p + up * (height / 2 - 0.001) + np.asarray(nv) * 0.002)
        # down the two front edges
        e0 = [pts[0] + norm(np.array([0, 0, 1.0]) + nrms[0] * flare) * h + nrms[0] * 0.002
              for h in np.linspace(-height / 2, height / 2, 4)]
        e1 = [pts[-1] + norm(np.array([0, 0, 1.0]) + nrms[-1] * flare) * h + nrms[-1] * 0.002
              for h in np.linspace(height / 2, -height / 2, 4)]
        parts.append(tube(e0[:-1] + top + e1[1:], 0.0036, 6, (0, 0, 1), piping_mat))
    for p in parts:
        p.skin(lambda v: {"chest": 1 - 0.45 * smoothstep(1.47, 1.56, v[:, 2]),
                          "neck": 0.45 * smoothstep(1.47, 1.56, v[:, 2])})
    return parts


def build_male():
    parts = []
    # --- head, face, hair, beard, goggles -------------------------------------------------------
    hp, hgrid, hbvh = head_parts(False, "skin_m", "lips_m", "iris_m")
    parts += hp
    hair, _ = hair_cap_and_tufts(hgrid, hbvh, False, "hair_m", seed=7)
    parts += hair
    parts += beard_parts(hgrid, "hair_m")
    gbvh = make_bvh(merge([hp[0]] + hair[:1]))
    parts += goggles(gbvh, *GOGGLE_Z[False], "leather_dark", "goggle_dark", "lens_orange", "metal_dark")
    parts.append(neck_part(0.056, 0.058, "skin_m"))

    # --- jacket torso -----------------------------------------------------------------------------
    torso, tgrid = grid_loft(jacket_torso_keys(), 22, 36, mat="jacket_olive", ef=2.3, eb=2.5)
    torso.skin(torso_weights).tagged("torso")
    parts.append(torso)
    tbvh = make_bvh(torso)
    parts += collar(0.012, 1.455, 0.088, 0.074, 0.072, 0.32, "jacket_olive", "orange")
    # zip + piping down the front
    zpts = [front_point(tbvh, 0.0, z, 0.0035) for z in np.linspace(0.99, 1.47, 17)]
    zip_strip = strap(zpts, [np.array([0, -1.0, 0])] * len(zpts), 0.005, 0.0018, "metal_dark")
    parts.append(zip_strip.skin(torso_weights))
    for sx in (1, -1):
        pp = [front_point(tbvh, sx * 0.012, z, 0.0045) for z in np.linspace(0.99, 1.47, 17)]
        parts.append(tube(pp, 0.0034, 6, (1, 0, 0), "orange").skin(torso_weights))
        # slanted hand-warmer pocket edges
        pk = [front_point(tbvh, sx * (0.07 + 0.065 * u), 1.02 + 0.13 * u, 0.0045) for u in np.linspace(0, 1, 7)]
        parts.append(tube(pk, 0.0038, 6, (0, -1, 0), "orange").skin(torso_weights))
    # chest patch (left chest)
    parts.append(patch(tbvh, (0.092, 0, 1.335), (0, 1, 0), (0.027, 0.004, 0.017), "orange").skin(torso_weights))

    # --- sleeves, arm bands, shoulder patch, gloves ----------------------------------------------
    left = []
    left.append(sleeve([(-0.18, 0.04, 0.04), (-0.03, 0.052, 0.05), (0.25, 0.06, 0.058), (0.55, 0.06, 0.058),
                        (0.85, 0.056, 0.054), (1.0, 0.054, 0.053), (1.13, 0.0535, 0.0525), (1.35, 0.053, 0.051), (1.7, 0.049, 0.048),
                        (1.86, 0.046, 0.045)], "jacket_olive"))
    left.append(sleeve([(1.80, 0.0495, 0.0485), (1.88, 0.0505, 0.0495), (1.97, 0.047, 0.046)], "jacket_olive_dark"))
    c, t, out, fwd = arm_point(0.42)
    band = ring_band(c, 0.0625, 0.0605, 0.017, 0.004, "orange", 22,
                     axes=(fwd, norm(np.cross(t, fwd)), t))
    left.append(band.skin(chain_weights(["upper_arm_l", "forearm_l"])))
    left += glove("glove_dark", "glove_dark", "skin_m", "leather_mid",
                  [(-0.035, 0.041, 0.031), (-0.01, 0.042, 0.03), (0.022, 0.039, 0.025), (0.03, 0.035, 0.022)],
                  fingerless=True)
    parts += both_sides(left)
    c, t, out, fwd = arm_point(0.2)
    sp = soft_box(c + out * 0.058, (0.022, 0.004, 0.024), axes=(fwd, out, -t), e=5, e2=5, segs=14, rings=5,
                  mat="orange")
    parts.append(sp.skin(chain_weights(["shoulder_l", "upper_arm_l", "forearm_l"], start=bone_head("shoulder_l"))))

    # --- harness straps ---------------------------------------------------------------------------
    for sx in (1, -1):
        ctrl = [(np.array([sx * 0.105, -0.1, 1.13]), (0, 1, 0)), (np.array([sx * 0.11, -0.1, 1.25]), (0, 1, 0)),
                (np.array([sx * 0.118, -0.1, 1.37]), (0, 1, 0)),
                (np.array([sx * 0.125, -0.07, 1.45]), (0, 1, -0.6)),
                (np.array([sx * 0.128, -0.03, 1.48]), (0, 0, -1)), (np.array([sx * 0.128, 0.03, 1.48]), (0, 0, -1)),
                (np.array([sx * 0.125, 0.07, 1.45]), (0, -1, -0.6)), (np.array([sx * 0.12, 0.1, 1.38]), (0, -1, 0))]
        st = surface_strap(tbvh, ctrl, 0.021, 0.0035, "leather_dark", out=0.002)
        parts.append(st.skin(torso_weights))
        # orange slider + metal buckle on the strap
        parts.append(patch(tbvh, (sx * 0.111, 0, 1.29), (0, 1, 0), (0.025, 0.006, 0.014), "orange",
                           lift=0.005).skin(torso_weights))
        parts.append(patch(tbvh, (sx * 0.106, 0, 1.17), (0, 1, 0), (0.019, 0.006, 0.012), "metal",
                           lift=0.007).skin(torso_weights))

    # --- trousers, belt and pouches ---------------------------------------------------------------
    pel, _ = grid_loft([(0.84, 0.15, -0.1, 0.11, 0.005), (0.9, 0.168, -0.112, 0.12, 0.0),
                        (0.96, 0.172, -0.116, 0.121, 0.0), (1.03, 0.168, -0.118, 0.118, 0.0)], 8, 36,
                       mat="trouser_olive", ef=2.3, eb=2.3)
    parts.append(pel.skin(pelvis_weights).tagged("pelvis"))
    belt, _ = grid_loft([(0.982, 0.18, -0.131, 0.13, 0.0), (1.036, 0.18, -0.131, 0.13, 0.0)], 2, 40,
                        mat="leather_dark", ef=3, eb=3)
    parts.append(belt.skin("hips"))
    bb = make_bvh(belt)
    parts.append(patch(bb, (0, 0, 1.009), (0, 1, 0), (0.03, 0.005, 0.024), "metal").skin("hips"))
    parts.append(patch(bb, (0, 0, 1.009), (0, 1, 0), (0.017, 0.004, 0.012), "leather_dark", lift=0.005).skin("hips"))
    for (x, y, z, h, m, d) in [(-0.135, -0.09, 0.995, (0.034, 0.024, 0.045), "backpack_olive", (0.4, 1, 0)),
                               (0.165, -0.03, 0.995, (0.03, 0.026, 0.05), "leather_mid", (-1, 0.3, 0)),
                               (0.12, 0.1, 0.995, (0.028, 0.02, 0.04), "backpack_olive", (-0.6, -1, 0))]:
        hit = project(bb, np.array([x, y, z]) - norm(d) * 0.4, d, 0.0)
        nrm = -norm(np.asarray(d, float))
        side = norm(np.cross((0, 0, 1), nrm))
        c0 = hit + nrm * (h[1] + 0.002)
        parts.append(soft_box(c0, h, axes=(side, nrm, (0, 0, 1)), e=4, e2=4, segs=16, rings=7, mat=m).skin("hips"))
        parts.append(soft_box(c0 + nrm * 0.002 + np.array([0, 0, h[2] * 0.75]), (h[0] * 1.05, h[1] * 1.05, h[2] * 0.3),
                              axes=(side, nrm, (0, 0, 1)), e=4, e2=4, segs=16, rings=5,
                              mat="leather_dark" if m != "leather_dark" else "leather_mid").skin("hips"))

    legs = []
    legs.append(trouser_leg([(-0.12, 0.088, 0.09), (0.1, 0.087, 0.089), (0.35, 0.081, 0.083), (0.65, 0.071, 0.072),
                             (0.92, 0.065, 0.065), (1.05, 0.063, 0.063), (1.3, 0.064, 0.062), (1.55, 0.062, 0.061),
                             (1.68, 0.065, 0.064), (1.76, 0.066, 0.065)], "trouser_olive"))
    # cargo pocket on the outer thigh
    c, t, out, fwd = leg_point(0.48)
    side_dir = norm(out * 0.85 + fwd * 0.5)
    pc = c + side_dir * 0.083
    up = -t
    tang = norm(np.cross(up, side_dir))
    legs.append(soft_box(pc, (0.045, 0.016, 0.06), axes=(tang, side_dir, up), e=5, e2=5, segs=16, rings=7,
                         mat="trouser_olive").skin(leg_weights("l")))
    legs.append(soft_box(pc + up * 0.048 + side_dir * 0.004, (0.048, 0.018, 0.016), axes=(tang, side_dir, up), e=5,
                         e2=5, segs=16, rings=5, mat="jacket_olive_dark").skin(leg_weights("l")))
    # knee pad + straps
    kc, kt, kout, kfwd = leg_point(1.0)
    legs.append(soft_box(kc + kfwd * 0.066 + np.array([0, 0, -0.005]), (0.052, 0.024, 0.066),
                         axes=(kout, kfwd, -kt), e=2.6, e2=2.6, segs=18, rings=9, mat="orange").skin("shin_l"))
    for zoff, bone in ((0.075, "thigh_l"), (-0.08, "shin_l")):
        cc, ct, co, cf = leg_point(1.0 - zoff / 0.42)
        legs.append(ring_band(cc + cf * 0.004, 0.068, 0.068, 0.012, 0.004, "leather_dark", 20,
                              axes=(co, cf, -ct)).skin(bone))
    legs += boot("boot_tan", "leather_mid", "orange", "sole", "leather_dark", "lace")
    parts += both_sides(legs)

    # --- backpack with tools ----------------------------------------------------------------------
    parts += male_backpack()
    return parts


def male_backpack():
    bp = []
    body_c = np.array([0.0, 0.232, 1.255])
    bp.append(soft_box(body_c, (0.165, 0.085, 0.2), e=5, e2=4, segs=20, rings=10, mat="backpack_olive"))
    bp.append(soft_box(body_c + (0, 0.004, 0.19), (0.168, 0.089, 0.035), e=5, e2=4, segs=24, rings=6,
                       mat="backpack_olive"))
    bp.append(soft_box((0, 0.322, 1.2), (0.125, 0.024, 0.11), e=5, e2=4, segs=20, rings=8, mat="backpack_olive"))
    bp.append(soft_box((0, 0.326, 1.29), (0.128, 0.026, 0.026), e=5, e2=4, segs=20, rings=5,
                       mat="jacket_olive_dark"))
    for sx in (1, -1):
        bp.append(soft_box((sx * 0.185, 0.235, 1.17), (0.028, 0.058, 0.085), e=5, e2=4, segs=16, rings=8,
                           mat="backpack_olive"))
        bp.append(soft_box((sx * 0.188, 0.235, 1.24), (0.03, 0.06, 0.016), e=5, e2=4, segs=16, rings=5,
                           mat="jacket_olive_dark"))
        # vertical orange straps with buckles on the back face
        bp.append(box((sx * 0.07, 0.349, 1.33), (0.014, 0.003, 0.11), mat="orange"))
        bp.append(box((sx * 0.07, 0.353, 1.255), (0.017, 0.004, 0.012), mat="metal"))
        # side compression strap
        bp.append(box((sx * 0.215, 0.235, 1.17), (0.003, 0.05, 0.012), mat="orange"))
    bp.append(box((0, 0.35, 1.115), (0.15, 0.003, 0.012), mat="orange"))
    for sx in (1, -1):
        # the lid straps continue down the front pocket to a second buckle
        bp.append(box((sx * 0.07, 0.349, 1.17), (0.012, 0.003, 0.045), mat="orange"))
        bp.append(box((sx * 0.07, 0.353, 1.145), (0.016, 0.004, 0.011), mat="metal"))
        # D-rings at the top corners of the side pockets
        bp.append(ring_band((sx * 0.214, 0.235, 1.262), 0.012, 0.009, 0.002, 0.0022, "metal_dark", 12,
                            axes=((0, 1, 0), (0, 0, 1), (1, 0, 0))))
    # webbing rows across the lower back panel and a hanging loop under the pack
    for zz in (1.08, 1.1):
        bp.append(box((0, 0.319, zz), (0.14, 0.002, 0.004), mat="jacket_olive_dark"))
    loop = [np.array([-0.06, 0.262, 1.066]), np.array([-0.04, 0.272, 1.02]), np.array([0.0, 0.276, 1.005]),
            np.array([0.04, 0.272, 1.02]), np.array([0.06, 0.262, 1.066])]
    bp.append(tube(loop, 0.0055, 8, (0, 1, 0), "leather_dark"))
    # top handle
    hp = [np.array([-0.05, 0.236, 1.48]), np.array([-0.03, 0.236, 1.505]), np.array([0.03, 0.236, 1.505]),
          np.array([0.05, 0.236, 1.48])]
    bp.append(tube(hp, 0.007, 8, (0, 1, 0), "leather_dark"))
    # tools sticking out of the top (character's left side)
    bp.append(cylinder((0.095, 0.205, 1.38), (0.095, 0.205, 1.62), 0.017, 14, "metal_dark", bevel=0.003))
    bp.append(cylinder((0.095, 0.205, 1.62), (0.095, 0.205, 1.645), 0.021, 14, "orange", bevel=0.004))
    bp.append(cylinder((0.135, 0.26, 1.38), (0.135, 0.26, 1.585), 0.013, 12, "metal", bevel=0.003))
    bp.append(cylinder((0.135, 0.26, 1.585), (0.135, 0.26, 1.6), 0.016, 12, "metal_dark", bevel=0.003))
    bp.append(cylinder((0.06, 0.26, 1.4), (0.06, 0.26, 1.565), 0.011, 12, "orange_dark", bevel=0.003))
    bp += wrench((0.03, 0.29, 1.47), (0.0, 0.0, 1.0), (1, 0, 0), 0.24, 0.022, "metal")
    bp += wrench((-0.09, 0.27, 1.45), (0.25, 0.0, 1.0), (1, 0, -0.25), 0.18, 0.018, "metal")
    # wrenches strapped to the side panels
    bp += wrench((0.218, 0.24, 1.26), (0, 0.15, 1.0), (0, 1, 0), 0.3, 0.024, "metal")
    bp += wrench((-0.218, 0.25, 1.25), (0, -0.1, 1.0), (0, 1, 0), 0.26, 0.022, "metal")
    for sx in (1, -1):
        bp.append(box((sx * 0.222, 0.24, 1.3), (0.004, 0.03, 0.01), mat="orange"))
    for p in bp:
        p.v += np.array([0.0, -0.038, 0.0])   # snug against the jacket
        p.skin("chest").group("gear")
    return bp


def wrench(center, up, face_normal_hint, length, jaw, mat):
    """Flat open-end wrench centred on `center`, handle along `up`, flat faces facing the hint normal."""
    up = norm(up)
    nrm = norm(np.asarray(face_normal_hint, float) - np.dot(face_normal_hint, up) * up)
    side = np.cross(up, nrm)
    c = np.asarray(center, float)
    parts = []
    h = length / 2 - jaw
    parts.append(box(c, (jaw * 0.3, 0.0035, h), axes=(side, nrm, up), mat=mat))
    for sgn, open_dir in ((1, 1), (-1, -1)):
        hc = c + up * sgn * (h + jaw * 0.6)
        a_mid = math.pi / 2 if sgn > 0 else -math.pi / 2
        parts.append(c_ring(hc, jaw, jaw * 0.5, 0.008 if sgn > 0 else 0.007, a_mid + 0.8 - math.pi * 2,
                            a_mid - 0.8, (side, up, nrm), segs=12, mat=mat))
    return parts


# =====================================================================================================
# Assembly and export
# =====================================================================================================

CHARACTERS = {"scrapper_male": lambda: build_male(), "scrapper_female": lambda: build_female()}


# texture style and noise seed per character; atlas size per texture group (see character_textures.py)
CHARACTER_STYLE = {"scrapper_male": ("male", 7), "scrapper_female": ("female", 11)}
ATLASES = {"head": 1024, "torso": 1024, "arms": 1024, "legs": 1024, "feet": 512, "gear": 1024}
PROP_ATLAS = 1024
BUCKET_ATLAS = 1024


def assemble_character(name, textured=True):
    """Build armature + one skinned, textured mesh for a character in the current scene."""
    arm = build_armature("Armature")
    parts = CHARACTERS[name]()
    part = merge(parts + arm_cut_caps(parts))
    ob = build_mesh_object(name, part, arm)
    if textured:
        stats = texture_mesh(name, ob, part)
        for g, (size, texel_mm, cov) in stats.items():
            print(f"  {name} atlas {g}: {size}px, {texel_mm:.2f} mm/texel, {cov * 100:.0f}% covered")
    return arm, ob, part


def mesh_material_names(part):
    names = []
    for m in part.m:
        if m not in names:
            names.append(m)
    return names


def texture_mesh(name, ob, part):
    bnames, W = finalize_weights(part.w, len(part.v))
    dominant = [bnames[i] for i in np.argmax(W, axis=1)]
    style, seed = CHARACTER_STYLE[name]
    bones = {b: bone_head(b) for b in BONE_NAMES}
    return ct.texture_character(ob, part, mesh_material_names(part), PALETTE, style, seed, ATLASES, bones, dominant)


def select_only(objs):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def export_glb(path, objs, animations=False):
    select_only(objs)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_skins=True, export_def_bones=False, export_animations=animations,
        export_animation_mode="ACTIONS", export_force_sampling=False, export_frame_step=1,
        export_optimize_animation_size=False, export_optimize_animation_keep_anim_armature=False,
        export_anim_slide_to_zero=False, export_morph=False, export_draco_mesh_compression_enable=False,
        export_tangents=True, export_image_format="JPEG", export_jpeg_quality=88, export_image_quality=88,
        export_extras=False, export_cameras=False, export_lights=False)


_FEMALE_TORSO_KEYS = [(0.995, 0.158, -0.112, 0.112, 0.0), (1.06, 0.152, -0.112, 0.11, 0.0),
                      (1.14, 0.153, -0.116, 0.111, 0.0), (1.24, 0.163, -0.132, 0.115, -0.004),
                      (1.30, 0.168, -0.136, 0.118, -0.004), (1.36, 0.172, -0.123, 0.118, 0.0),
                      (1.42, 0.17, -0.108, 0.112, 0.004), (1.455, 0.148, -0.092, 0.1, 0.008),
                      (1.48, 0.11, -0.077, 0.088, 0.01), (1.50, 0.066, -0.062, 0.072, 0.01)]


def jacket_opening(z):
    """Half width of the open front of the female's denim jacket at height z."""
    return 0.052 + (0.07 - 0.052) * sstep(1.45, 1.05, z)


def opening_thetas(n_front, n_rest, ef):
    """Ring angles with fixed columns on the edges of the jacket opening, so the shirt/bib boundary is a
    clean line instead of a staircase. Front is theta = 3pi/2."""
    def fn(z, w):
        u = min(0.95, jacket_opening(z) / w) ** (ef / 2.0)
        a = math.acos(u)                   # angle from +X where x = opening
        t0, t1 = math.pi + a, 2 * math.pi - a
        front = np.linspace(t0, t1, n_front + 1)
        rest = np.linspace(t1, t0 + 2 * math.pi, n_rest + 1)[1:-1]
        th = np.concatenate([front, rest]) % (2 * math.pi)
        return np.roll(th, -int(np.argmin(th)))
    return fn


def braid(start, ctrl, mat, tie_mat, n_lobes=11, size=1.0, tuft=0.05):
    """A thick three-strand braid: alternating tilted lobes along a curve."""
    pts = [np.asarray(start, float)] + [np.asarray(c, float) for c in ctrl]
    fine = [catmull([(i, p) for i, p in enumerate(pts)], u) for u in np.linspace(0, len(pts) - 1, 60)]
    seg = np.cumsum([0] + [np.linalg.norm(fine[i + 1] - fine[i]) for i in range(len(fine) - 1)])
    total = seg[-1]
    parts = []
    for k in range(n_lobes):
        s = (k + 0.5) / n_lobes * total * 0.92
        i = min(np.searchsorted(seg, s), len(fine) - 1)
        c = fine[i]
        t = norm(fine[min(i + 1, len(fine) - 1)] - fine[max(i - 1, 0)])
        side = norm(np.cross(t, (0, 1, 0)))
        if np.linalg.norm(side) < 0.5:
            side = np.array([1.0, 0, 0])
        out = np.cross(side, t)
        taper = size * (1.0 - 0.4 * (k / n_lobes))
        ang = 0.55 if k % 2 == 0 else -0.55
        ax_t = t * math.cos(ang) + side * math.sin(ang)
        ax_s = np.cross(out, ax_t)
        parts.append(ellipsoid(c + side * (0.006 if k % 2 == 0 else -0.006) * taper, (0.017 * taper, 0.013 * taper,
                               0.026 * taper), axes=(ax_s, out, ax_t), segs=10, rings=6, mat=mat))
    # tie and tuft at the end
    s = total * 0.95
    i = min(np.searchsorted(seg, s), len(fine) - 1)
    c, t = fine[i], norm(fine[-1] - fine[-5])
    a = norm(np.cross(t, (1, 0, 0)))
    parts.append(ring_band(c, 0.0105, 0.0105, 0.006, 0.003, tie_mat, 12, axes=(a, np.cross(t, a), t)))
    tip = [c, c + t * tuft * 0.5, c + t * tuft]
    parts.append(tube(tip, [(0.011, 0.009), (0.012, 0.01), (0.002, 0.002)], 8, (0, 1, 0), mat))
    return parts


def build_female():
    parts = []
    # --- head, hair with braid, goggles -------------------------------------------------------------
    hp, hgrid, hbvh = head_parts(True, "skin_f", "lips_f", "iris_f")
    parts += hp
    hair, _ = hair_cap_and_tufts(hgrid, hbvh, True, "hair_f", seed=11)
    parts += hair
    capbvh = make_bvh(merge([hp[0]] + hair[:1]))
    # hair gathered at the nape into one long braid hanging down the back, past the shoulder blades, to just
    # above the open top of the backpack. Below the neck it rides the chest bone like the backpack does,
    # so the two never meet in any clip.
    bun = HEAD_C + np.array([0.0, 0.088, -0.06])
    parts.append(ellipsoid(bun, (0.028, 0.022, 0.03), segs=12, rings=8, mat="hair_f").skin("head"))
    br = braid(bun + np.array([0, 0.008, -0.012]),
               [np.array([0.004, 0.122, 1.52]), np.array([0.01, 0.138, 1.46]), np.array([0.014, 0.146, 1.40]),
                np.array([0.016, 0.148, 1.36])], "hair_f", "leather_dark", n_lobes=12, size=1.15, tuft=0.035)
    for p in br:
        p.skin(lambda v: {"head": smoothstep(1.53, 1.58, v[:, 2]),
                          "neck": smoothstep(1.46, 1.52, v[:, 2]) * (1 - smoothstep(1.53, 1.58, v[:, 2])),
                          "chest": 1 - smoothstep(1.46, 1.52, v[:, 2])})
    parts += br
    parts += goggles(capbvh, *GOGGLE_Z[True], "leather_mid", "brass", "lens_blue", "brass", tilt=1.1, cup_r=0.02)
    parts.append(neck_part(0.047, 0.05, "skin_f"))

    # --- torso: open denim jacket over shirt and overall bib ----------------------------------------
    zs = sorted(set(np.round(np.linspace(0.995, 1.5, 26), 4)) | {1.325})
    torso, _ = grid_loft(_FEMALE_TORSO_KEYS, 0, 44, mat="denim", ef=2.3, eb=2.5, zs=zs,
                         theta_fn=opening_thetas(8, 36, 2.3))

    def zone(c):
        x, y, z = c
        if y < 0 and abs(x) < jacket_opening(z) - 0.002:
            return "shirt" if z > 1.325 else "canvas"
        return None
    assign_mats(torso, zone)
    parts.append(torso.skin(torso_weights).tagged("torso"))
    tbvh = make_bvh(torso)
    parts += collar(0.012, 1.45, 0.06, 0.07, 0.068, 0.55, "denim", None, flare=0.7, thickness=0.005)
    parts += collar(0.014, 1.47, 0.055, 0.056, 0.058, 0.32, "shirt", None, flare=0.2, thickness=0.004)
    # bib: buckles, strap stubs, chest pocket
    for sx in (1, -1):
        parts.append(patch(tbvh, (sx * 0.042, 0, 1.318), (0, 1, 0), (0.011, 0.004, 0.011), "brass",
                           lift=0.002).skin(torso_weights))
        pts = [front_point(tbvh, sx * (0.042 + 0.004 * u), 1.325 + 0.06 * u, 0.003) for u in np.linspace(0, 1, 4)]
        parts.append(strap(pts, [np.array([0, -1.0, 0])] * len(pts), 0.009, 0.002, "canvas").skin(torso_weights))
    parts.append(patch(tbvh, (0, 0, 1.24), (0, 1, 0), (0.04, 0.004, 0.04), "canvas", lift=0.001).skin(torso_weights))
    parts.append(patch(tbvh, (0, 0, 1.278), (0, 1, 0), (0.042, 0.005, 0.009), "canvas_dark",
                       lift=0.003).skin(torso_weights))
    # harness straps of the backpack, over the jacket
    for sx in (1, -1):
        ctrl = [(np.array([sx * 0.108, -0.1, 1.16]), (0, 1, 0)), (np.array([sx * 0.112, -0.1, 1.27]), (0, 1, 0)),
                (np.array([sx * 0.116, -0.1, 1.37]), (0, 1, 0)),
                (np.array([sx * 0.12, -0.07, 1.45]), (0, 1, -0.6)),
                (np.array([sx * 0.122, -0.03, 1.48]), (0, 0, -1)), (np.array([sx * 0.122, 0.03, 1.48]), (0, 0, -1)),
                (np.array([sx * 0.12, 0.07, 1.45]), (0, -1, -0.6)), (np.array([sx * 0.115, 0.1, 1.36]), (0, -1, 0))]
        parts.append(surface_strap(tbvh, ctrl, 0.017, 0.0035, "leather_mid", out=0.002).skin(torso_weights))
        parts.append(patch(tbvh, (sx * 0.112, 0, 1.3), (0, 1, 0), (0.02, 0.006, 0.012), "orange",
                           lift=0.005).skin(torso_weights))

    # --- arms: denim sleeves rolled to the forearm, gloves ------------------------------------------
    left = []
    left.append(sleeve([(-0.18, 0.038, 0.038), (-0.03, 0.047, 0.045), (0.25, 0.051, 0.049), (0.55, 0.05, 0.048),
                        (0.85, 0.048, 0.046), (1.0, 0.047, 0.046), (1.13, 0.047, 0.0455), (1.3, 0.047, 0.045), (1.45, 0.046, 0.044)],
                       "denim"))
    left.append(sleeve([(1.36, 0.05, 0.048), (1.42, 0.054, 0.052), (1.5, 0.054, 0.052), (1.56, 0.049, 0.047)],
                       "denim_light"))
    left.append(sleeve([(1.4, 0.04, 0.038), (1.6, 0.039, 0.036), (1.8, 0.033, 0.03), (1.95, 0.03, 0.027)],
                       "skin_f"))
    left += glove("glove_brown", "glove_brown", "glove_brown", "glove_brown",
                  [(-0.05, 0.039, 0.03), (-0.02, 0.04, 0.029), (0.02, 0.037, 0.024), (0.028, 0.034, 0.021)],
                  accent_mat="orange", fingerless=False, female=True)
    d, w, n = hand_frame("l")
    left.append(loft([dict(c=hand_local(a, 0.0, 0.0), a=w, b=n, rx=0.0405, ry=0.0305, e=2.4) for a in (-0.04, -0.03)],
                     16, True, True, "orange").skin(chain_weights(["forearm_l", "hand_l"], blend=0.04)))
    parts += both_sides(left)
    c, t, out, fwd = arm_point(0.3)
    sp = soft_box(c + out * 0.049, (0.02, 0.004, 0.022), axes=(fwd, out, -t), e=5, e2=5, segs=14, rings=5,
                  mat="patch")
    parts.append(sp.skin(chain_weights(["shoulder_l", "upper_arm_l", "forearm_l"], start=bone_head("shoulder_l"))))

    # --- overalls ------------------------------------------------------------------------------------
    pel, _ = grid_loft([(0.84, 0.165, -0.105, 0.12, 0.005), (0.9, 0.183, -0.112, 0.129, 0.0),
                        (0.96, 0.185, -0.114, 0.127, 0.0), (1.02, 0.166, -0.112, 0.116, 0.0),
                        (1.06, 0.152, -0.11, 0.11, 0.0)], 9, 36, mat="canvas", ef=2.3, eb=2.3)
    parts.append(pel.skin(pelvis_weights).tagged("pelvis"))
    pb = make_bvh(pel)
    for sx in (1, -1):
        parts.append(patch(pb, (sx * 0.155, 0, 0.985), (-sx, 0.15, 0), (0.008, 0.003, 0.008), "brass").skin("hips"))
    legs = []
    legs.append(trouser_leg([(-0.12, 0.092, 0.095), (0.1, 0.089, 0.091), (0.35, 0.08, 0.082), (0.65, 0.068, 0.069),
                             (0.92, 0.062, 0.062), (1.05, 0.06, 0.06), (1.3, 0.061, 0.059), (1.55, 0.059, 0.058),
                             (1.68, 0.062, 0.061), (1.76, 0.063, 0.062)], "canvas"))
    c, t, out, fwd = leg_point(0.5)
    side_dir = norm(out * 0.9 + fwd * 0.35)
    pc = c + side_dir * 0.078
    up = -t
    tang = norm(np.cross(up, side_dir))
    legs.append(soft_box(pc, (0.042, 0.015, 0.058), axes=(tang, side_dir, up), e=5, e2=5, segs=16, rings=7,
                         mat="canvas").skin(leg_weights("l")))
    legs.append(soft_box(pc + up * 0.046 + side_dir * 0.004, (0.045, 0.017, 0.015), axes=(tang, side_dir, up), e=5,
                         e2=5, segs=16, rings=5, mat="canvas_dark").skin(leg_weights("l")))
    kc, kt, kout, kfwd = leg_point(1.0)
    legs.append(soft_box(kc + kfwd * 0.056 + np.array([0, 0, 0.0]), (0.05, 0.012, 0.062),
                         axes=(kout, kfwd, -kt), e=4, e2=4, segs=18, rings=8, mat="canvas_dark").skin(leg_weights("l")))
    legs += boot("boot_brown", "orange", "orange", "sole", "leather_dark", "lace", female=True)
    legs.append(ring_band((bone_head("foot_l")[0], 0.024, 0.13), 0.056, 0.06, 0.008, 0.004, "leather_dark", 18)
                .skin(chain_weights(["shin_l", "foot_l"], blend=0.07)))
    legs.append(box((bone_head("foot_l")[0] + 0.058, 0.02, 0.13), (0.002, 0.009, 0.009), mat="brass")
                .skin(chain_weights(["shin_l", "foot_l"], blend=0.07)))
    parts += both_sides(legs)
    # thigh strap with buckle (left thigh only)
    c, t, out, fwd = leg_point(0.62)
    parts.append(ring_band(c, 0.074, 0.075, 0.011, 0.004, "leather_mid", 20, axes=(fwd, out, -t)).skin(leg_weights("l")))
    parts.append(box(c + fwd * 0.078, (0.014, 0.003, 0.012), axes=(out, fwd, -t), mat="brass").skin(leg_weights("l")))

    parts += female_backpack()
    return parts


def screwdriver(base, direction, handle_mat, length=0.17):
    d = norm(direction)
    b = np.asarray(base, float)
    return [cylinder(b, b + d * 0.08, 0.0035, 8, "metal"),
            cylinder(b + d * 0.075, b + d * (0.075 + 0.075), 0.011, 10, handle_mat, bevel=0.003)]


def female_backpack():
    bp = []
    c = np.array([0.0, 0.2, 1.215])
    bp.append(soft_box(c, (0.14, 0.072, 0.145), e=5, e2=5, segs=24, rings=10, mat="denim"))
    bp.append(ring_band(c + (0, 0, 0.14), 0.142, 0.074, 0.012, 0.006, "denim_dark", 24, e=6))
    bp.append(soft_box((0, 0.275, 1.18), (0.1, 0.018, 0.075), e=5, e2=5, segs=20, rings=8, mat="denim"))
    bp.append(soft_box((0, 0.279, 1.245), (0.104, 0.02, 0.022), e=5, e2=5, segs=20, rings=5, mat="denim_dark"))
    for sx in (1, -1):
        bp.append(box((sx * 0.06, 0.296, 1.22), (0.012, 0.003, 0.07), mat="leather_mid"))
        bp.append(box((sx * 0.06, 0.3, 1.2), (0.014, 0.004, 0.011), mat="brass"))
        bp.append(box((sx * 0.11, 0.273, 1.3), (0.012, 0.003, 0.05), mat="orange"))
        bp.append(soft_box((sx * 0.148, 0.2, 1.17), (0.016, 0.05, 0.065), e=5, e2=5, segs=14, rings=6, mat="denim"))
        bp.append(box((sx * 0.166, 0.2, 1.2), (0.003, 0.04, 0.01), mat="leather_mid"))
        # leather corner patches
        bp.append(box((sx * 0.12, 0.272, 1.09), (0.025, 0.003, 0.025), mat="leather_mid"))
    bp.append(box((0, 0.274, 1.12), (0.12, 0.003, 0.009), mat="orange"))
    # tools in the open top
    bp += wrench((0.085, 0.2, 1.39), (0.25, 0.0, 1.0), (0, 1, 0), 0.2, 0.02, "metal")
    bp += wrench((0.03, 0.22, 1.4), (0.08, 0.05, 1.0), (0, 1, 0), 0.24, 0.022, "metal")
    bp += screwdriver((-0.045, 0.2, 1.3), (-0.08, 0.02, 1.0), "tool_orange")
    bp += screwdriver((-0.08, 0.215, 1.3), (-0.2, 0.05, 1.0), "tool_blue")
    bp += screwdriver((0.0, 0.24, 1.3), (0.03, 0.04, 1.0), "tool_blue")
    bp += screwdriver((0.12, 0.23, 1.3), (0.2, 0.1, 1.0), "tool_orange")
    # the open top: dark lining inside the rim, with the tools standing in it
    bp.append(soft_box(c + (0, 0.004, 0.141), (0.126, 0.058, 0.004), e=5, e2=5, segs=24, rings=4, mat="lining"))
    bp.append(cylinder((-0.11, 0.21, 1.3), (-0.12, 0.215, 1.47), 0.009, 10, "metal_dark", bevel=0.002))
    for p in bp:
        p.v += np.array([0.0, -0.016, -0.065])   # snug against the jacket, low on the back as on the sheet
        p.skin("chest").group("gear")
    return bp


# =====================================================================================================
# Pose solver: forward kinematics, two-bone IK and pose specs
# =====================================================================================================
#
# A pose is a rotation Q[bone] per bone, expressed in ARMATURE axes and applied relative to the parent's
# posed frame (so Q = identity everywhere is the rest pose), plus a vertical hips offset. The world
# orientation of a bone is W[bone] = W[parent] @ Q[bone]. Converting to Blender's pose-bone basis:
# basis = R_rest^T @ Q @ R_rest. Hips only ever move vertically (no root motion, no horizontal travel).

I3 = np.eye(3)


def E(x=0.0, y=0.0, z=0.0):
    """Rotation from degrees about armature X, then Y, then Z."""
    rx, ry, rz = (math.radians(a) for a in (x, y, z))
    cx, sx, cy, sy, cz, sz = math.cos(rx), math.sin(rx), math.cos(ry), math.sin(ry), math.cos(rz), math.sin(rz)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Rz @ Ry @ Rx


def mirror_euler(e, side):
    """Euler for the given side from a left-side Euler (mirror across the YZ plane)."""
    x, y, z = e
    return (x, y, z) if side == "l" else (x, -y, -z)


def frame_map(a0, n0, a1, n1):
    """Rotation taking the orthonormal pair (a0, n0) to (a1, n1)."""
    def basis(a, n):
        a = norm(a)
        n = norm(np.asarray(n) - np.dot(n, a) * a)
        return np.stack([a, n, np.cross(a, n)], axis=1)
    return basis(a1, n1) @ basis(a0, n0).T


def side_sign(side):
    return 1.0 if side == "l" else -1.0


def elbow_axis(side):
    u0 = bone_head(f"forearm_{side}") - bone_head(f"upper_arm_{side}")
    f0 = bone_head(f"hand_{side}") - bone_head(f"forearm_{side}")
    return norm(np.cross(u0, f0))


def knee_axis(side):
    u0 = bone_head(f"shin_{side}") - bone_head(f"thigh_{side}")
    f0 = bone_head(f"foot_{side}") - bone_head(f"shin_{side}")
    return norm(np.cross(u0, f0))


def curl_axis(side):
    d, w, n = hand_frame(side)
    return norm(np.cross(d, n))


def thumb_axis(side):
    d, w, n = hand_frame(side)
    t = norm(bone_tail(f"thumb_{side}") - bone_head(f"thumb_{side}"))
    return norm(np.cross(t, norm(n - 0.6 * w)))


class Pose:
    def __init__(self):
        self.Q = {b: I3.copy() for b in BONE_NAMES}
        self.hips_z = 0.0
        self.W = {}
        self.P = {}

    def fk(self):
        for name, parent, h, t in SKELETON:
            if parent is None:
                self.W[name] = self.Q[name]
                self.P[name] = bone_head(name).copy()
                continue
            self.W[name] = self.W[parent] @ self.Q[name]
            self.P[name] = self.P[parent] + self.W[parent] @ (bone_head(name) - bone_head(parent))
            if name == "hips":
                self.P[name] = self.P[name] + np.array([0.0, 0.0, self.hips_z])
        return self

    def point(self, bone, rest_point):
        """World position of a rest-space point rigidly attached to `bone`."""
        return self.P[bone] + self.W[bone] @ (np.asarray(rest_point, float) - bone_head(bone))

    def to_space(self, space, p=None, v=None):
        if space == "world":
            return p if p is not None else v
        if p is not None:
            return self.point(space, p)
        return self.W[space] @ np.asarray(v, float)


def two_bone_ik(pose, b1, b2, end, target, pole):
    """Solve bones b1, b2 so the head of `end` reaches target, bending toward pole. Returns W[b2]."""
    pose.fk()
    parent = BONE[b1]["parent"]
    root = pose.P[b1]
    la = np.linalg.norm(bone_head(b2) - bone_head(b1))
    lb = np.linalg.norm(bone_head(end) - bone_head(b2))
    u0 = norm(bone_head(b2) - bone_head(b1))
    f0 = norm(bone_head(end) - bone_head(b2))
    n0 = norm(np.cross(u0, f0))
    tv = np.asarray(target, float) - root
    dist = float(np.clip(np.linalg.norm(tv), abs(la - lb) + 1e-4, (la + lb) * 0.9995))
    t_hat = norm(tv)
    p = np.asarray(pole, float)
    p_hat = norm(p - np.dot(p, t_hat) * t_hat)
    cos_a = (la * la + dist * dist - lb * lb) / (2 * la * dist)
    sin_a = math.sqrt(max(0.0, 1 - cos_a * cos_a))
    knee = root + la * (cos_a * t_hat + sin_a * p_hat)
    u = norm(knee - root)
    f = norm(root + t_hat * dist - knee)
    n = norm(np.cross(p_hat, t_hat))
    W1 = frame_map(u0, n0, u, n)
    W2 = frame_map(f0, n0, f, n)
    pose.Q[b1] = pose.W[parent].T @ W1
    pose.Q[b2] = W1.T @ W2
    return W2


# ------------------------------------------------------------------------------------------------
# Pose specs
# ------------------------------------------------------------------------------------------------
# A spec is a dict of numbers / tuples / nested dicts, so key poses can be interpolated:
#   hips_z, hips (euler), spine, chest, neck, head, shoulder_l/r (euler, degrees)
#   arm_l/r: {"down", "swing", "yaw", "elbow", "wrist", "twist"}  (FK, degrees)
#         or {"pos", "d", "w", "pole", "space"}                    (IK: wrist position, finger dir, thumb dir)
#   fingers_l/r, thumb_l/r: curl in degrees
#   leg_l/r: {"thigh": euler, "knee", "foot"}  (FK)  or  {"ankle": pos, "pitch", "yaw", "pole", "toe"} (IK)

def flatten(spec, prefix=""):
    out = []
    for k in sorted(spec):
        v = spec[k]
        if isinstance(v, dict):
            out += flatten(v, prefix + k + ".")
        elif isinstance(v, str):
            continue
        else:
            out.append((prefix + k, np.atleast_1d(np.asarray(v, float))))
    return out


def unflatten(template, values):
    """Rebuild a spec shaped like template from a flat vector."""
    out = {}
    i = 0
    for k in sorted(template):
        v = template[k]
        if isinstance(v, dict):
            sub, used = _unflatten_count(v, values[i:])
            out[k] = sub
            i += used
        elif isinstance(v, str):
            out[k] = v
        else:
            n = np.atleast_1d(np.asarray(v, float)).size
            seg = values[i:i + n]
            out[k] = float(seg[0]) if np.isscalar(v) or np.asarray(v).ndim == 0 else tuple(seg)
            i += n
    return out


def _unflatten_count(template, values):
    n = sum(a.size for _, a in flatten(template))
    return unflatten(template, values[:n]), n


def spec_vector(spec):
    return np.concatenate([a for _, a in flatten(spec)]) if flatten(spec) else np.zeros(0)


def interp_specs(keys, t, loop_period=None):
    """Catmull-Rom through key specs [(time, spec), ...] (all specs share the same structure)."""
    template = keys[0][1]
    pts = [(kt, spec_vector(s)) for kt, s in keys]
    if loop_period:
        pts = [(pts[-2][0] - loop_period, pts[-2][1])] + pts + [(pts[1][0] + loop_period, pts[1][1])]
    return unflatten(template, catmull(pts, t))


def build_pose(spec):
    pose = Pose()
    pose.hips_z = spec.get("hips_z", 0.0)
    for b in ("hips", "spine", "chest", "neck", "head"):
        if b in spec:
            pose.Q[b] = E(*spec[b])
    for side in ("l", "r"):
        if f"shoulder_{side}" in spec:
            pose.Q[f"shoulder_{side}"] = E(*mirror_euler(spec[f"shoulder_{side}"], side))
    pose.fk()
    for side in ("l", "r"):
        leg = spec.get(f"leg_{side}")
        if leg:
            _pose_leg(pose, side, leg)
        arm = spec.get(f"arm_{side}")
        if arm:
            _pose_arm(pose, side, arm)
        if f"fingers_{side}" in spec:
            pose.Q[f"fingers_{side}"] = axis_rot(curl_axis(side), spec[f"fingers_{side}"])
        if f"thumb_{side}" in spec:
            pose.Q[f"thumb_{side}"] = axis_rot(thumb_axis(side), spec[f"thumb_{side}"])
    return pose.fk()


def _pose_leg(pose, side, leg):
    s = side_sign(side)
    if "ankle" in leg:
        W_shin = two_bone_ik(pose, f"thigh_{side}", f"shin_{side}", f"foot_{side}", leg["ankle"],
                             leg.get("pole", (0, -1, 0)))
        W_foot = E(0, 0, s * leg.get("yaw", 0.0)) @ E(leg.get("pitch", 0.0))
        pose.Q[f"foot_{side}"] = W_shin.T @ W_foot
        toe = leg.get("toe", 0.0)
        # toe: >=0 keeps the toes flat on the ground (counter the foot pitch); extra value bends further
        pose.Q[f"toe_{side}"] = E(-leg.get("pitch", 0.0) * toe)
    else:
        pose.Q[f"thigh_{side}"] = E(*mirror_euler(leg.get("thigh", (0, 0, 0)), side))
        pose.Q[f"shin_{side}"] = axis_rot(knee_axis(side), leg.get("knee", 0.0))
        pose.Q[f"foot_{side}"] = E(leg.get("foot", 0.0))
        pose.Q[f"toe_{side}"] = E(leg.get("toe_bend", 0.0))
    pose.fk()


def _pose_arm(pose, side, arm):
    s = side_sign(side)
    if "pos" in arm:
        space = arm.get("space", "chest")
        target = pose.to_space(space, p=arm["pos"])
        pole = pose.to_space(space, v=arm.get("pole", (s * 0.5, 0.6, -1.0)))
        W_fore = two_bone_ik(pose, f"upper_arm_{side}", f"forearm_{side}", f"hand_{side}", target, pole)
        d = pose.to_space(space, v=arm["d"])
        w = pose.to_space(space, v=arm["w"])
        d0, w0, n0 = hand_frame(side)
        W_hand = frame_map(d0, w0, d, w)
        pose.Q[f"hand_{side}"] = W_fore.T @ W_hand
    else:
        down, swing, yaw = arm.get("down", 0.0), arm.get("swing", 0.0), arm.get("yaw", 0.0)
        # down: bring the A-pose arm toward the body; swing: forward; yaw: about the vertical
        pose.Q[f"upper_arm_{side}"] = E(0, 0, s * yaw) @ E(-swing) @ E(0, s * down, 0) @ \
            axis_rot(norm(bone_head(f"forearm_{side}") - bone_head(f"upper_arm_{side}")), s * arm.get("twist", 0.0))
        pose.Q[f"forearm_{side}"] = axis_rot(elbow_axis(side), arm.get("elbow", 0.0))
        pose.Q[f"hand_{side}"] = axis_rot(curl_axis(side), arm.get("wrist", 0.0))
    pose.fk()


def hand_targets_from_grip(side, grip, d, w):
    """Wrist position for a hand whose fist centre is at `grip` with finger dir d and thumb dir w."""
    d = norm(d)
    w = norm(np.asarray(w) - np.dot(w, d) * d)
    n = norm(np.cross(d, w)) * side_sign(side)
    return np.asarray(grip) - d * 0.062 - n * 0.028, d, w


def arm_ik_from_grip(side, grip, d, w, pole=None, space="chest"):
    pos, d, w = hand_targets_from_grip(side, grip, d, w)
    s = side_sign(side)
    return {"pos": tuple(pos), "d": tuple(d), "w": tuple(w),
            "pole": tuple(pole if pole is not None else (s * 0.6, 0.5, -1.0)), "space": space}


# =====================================================================================================
# Animation clips
# =====================================================================================================

RELAXED_ARM = {"down": 36.0, "swing": 4.0, "yaw": 0.0, "elbow": 14.0, "wrist": 6.0, "twist": 0.0}


def relaxed_upper(fingers=18.0):
    s = {"spine": (0, 0, 0), "chest": (0, 0, 0), "neck": (0, 0, 0), "head": (0, 0, 0),
         "shoulder_l": (0, 0, 0), "shoulder_r": (0, 0, 0)}
    for side in ("l", "r"):
        s[f"arm_{side}"] = dict(RELAXED_ARM)
        s[f"fingers_{side}"] = fingers
        s[f"thumb_{side}"] = 10.0
    return s


def relaxed_arm_ik(side):
    """The relaxed FK arm expressed as an IK target (chest space), for blending into IK key poses."""
    spec = relaxed_upper()
    pose = build_pose({k: v for k, v in spec.items() if k in ("arm_l", "arm_r")})
    p = pose.P[f"hand_{side}"]
    W = pose.W[f"hand_{side}"]
    d0, w0, n0 = hand_frame(side)
    elbow = pose.P[f"forearm_{side}"]
    mid = (pose.P[f"upper_arm_{side}"] + p) / 2
    return {"pos": tuple(p), "d": tuple(W @ d0), "w": tuple(W @ w0), "pole": tuple(norm(elbow - mid)),
            "space": "chest"}


REST_ANKLE = {s: bone_head(f"foot_{s}").copy() for s in ("l", "r")}
REST_HIP_JOINT = {s: bone_head(f"thigh_{s}").copy() for s in ("l", "r")}
LEG_LEN = float(np.linalg.norm(bone_head("shin_l") - bone_head("thigh_l")) +
                np.linalg.norm(bone_head("foot_l") - bone_head("shin_l")))
HEEL_OFF = np.array([0.0, 0.067, -0.085])     # heel contact relative to the ankle (rest)
BALL_OFF = np.array([0.0, -0.118, -0.085])    # ball-of-foot contact relative to the ankle (rest)


def _rx(deg):
    return E(deg)


def foot_cycle(phase, T, v, duty, lift, strike_pitch, off_pitch, center_y, side, swing_shape=0.75,
               heel_frac=0.15, toe_frac=0.4):
    """Ankle target and foot pitch for one leg. phase in [0,1): 0 = heel strike.

    During stance the contact point (heel, then the whole foot, then the ball) moves backward (+Y) at
    exactly the ground speed v, so feet don't skate when the body moves at v."""
    D = v * T * duty
    x = bone_head(f"foot_{side}")[0]
    y_h0 = center_y - D / 2 + HEEL_OFF[1]

    def stance(s):
        heel_y = y_h0 + D * s
        s1, s2 = heel_frac, 1.0 - toe_frac
        if s < s1:
            th = strike_pitch * (1 - sstep(0, s1, s))
            heel = np.array([x, heel_y, 0.0])
            return heel - _rx(th) @ HEEL_OFF, th
        if s < s2:
            return np.array([x, heel_y, 0.0]) - HEEL_OFF, 0.0
        u = (s - s2) / (1 - s2)
        th = off_pitch * u ** 1.4
        ball = np.array([x, heel_y + (BALL_OFF[1] - HEEL_OFF[1]), 0.0])
        return ball - _rx(th) @ BALL_OFF, th

    if phase < duty:
        a, th = stance(phase / duty)
        return a, th, True
    u = (phase - duty) / (1 - duty)
    a_end, th_end = stance(1.0)
    a_start, th_start = stance(0.0)
    e = 0.5 - 0.5 * math.cos(math.pi * u)
    y = a_end[1] + (a_start[1] - a_end[1]) * e
    z = a_end[2] + (a_start[2] - a_end[2]) * e + lift * math.sin(math.pi * u ** swing_shape)
    th = th_end + (th_start - th_end) * sstep(0.1, 0.95, u) + 12.0 * math.sin(math.pi * u)
    return np.array([x, y, z]), th, False


def locomotion(t, T, v, duty, *, hips_base, bob, lift, strike_pitch=15.0, off_pitch=40.0, lean=4.0,
               arm_swing=20.0, elbow=18.0, elbow_extra=10.0, fingers=20.0, hips_yaw=5.0, chest_yaw=7.0,
               center_y=0.02, running=False, arm_down=None, head_comp=0.8, swing_shape=0.75,
               heel_frac=0.15, toe_frac=0.4, knee_pole=(0, -1, 0)):
    """Full-body locomotion spec at time t (seconds) for a cycle of period T at speed v."""
    ph = (t / T) % 1.0
    c = math.cos(2 * math.pi * ph)
    spec = {}
    feet = {}
    for side, off in (("l", 0.0), ("r", 0.5)):
        a, th, st = foot_cycle((ph - off) % 1.0, T, v, duty, lift, strike_pitch, off_pitch, center_y, side,
                               swing_shape, heel_frac, toe_frac)
        feet[side] = (a, th, st)
    hz = _hips_curve(T, v, duty, hips_base, bob, running, strike_pitch, off_pitch, center_y, lift, swing_shape,
                     heel_frac, toe_frac)(ph)
    spec["hips_z"] = hz
    spec["hips"] = (lean * 0.35, 0.0, -hips_yaw * c)
    spec["spine"] = (lean * 0.35, 0.0, (hips_yaw + chest_yaw) * c * 0.5)
    spec["chest"] = (lean * 0.3, 0.0, (hips_yaw + chest_yaw) * c * 0.5)
    spec["neck"] = (-lean * head_comp * 0.5, 0.0, -chest_yaw * c * 0.5)
    spec["head"] = (-lean * head_comp * 0.5, 0.0, -chest_yaw * c * 0.5)
    for side in ("l", "r"):
        a, th, st = feet[side]
        spec[f"leg_{side}"] = {"ankle": tuple(a), "pitch": th, "pole": knee_pole, "toe": 1.0 if th > 0 else 0.0}
        sgn = -1.0 if side == "l" else 1.0
        sw = sgn * c        # +1 = arm forward
        down = RELAXED_ARM["down"] if arm_down is None else arm_down
        spec[f"arm_{side}"] = {"down": down, "swing": arm_swing * sw + (6.0 if running else 2.0), "yaw": 0.0,
                               "elbow": elbow + elbow_extra * max(0.0, sw), "wrist": 8.0, "twist": 0.0}
        spec[f"fingers_{side}"] = fingers
        spec[f"thumb_{side}"] = fingers * 0.6
    return spec


_HIPS_CACHE = {}


def _hips_curve(T, v, duty, base, bob, running, sp, op, cy, lift, shape, hf, tf):
    """Pelvis height over the cycle: a bob (walk: peak at mid-stance, run: dip at mid-stance), limited so
    every planted leg can reach its foot, then smoothed (periodic) so there are no kinks."""
    key = (T, v, duty, base, bob, running, sp, op, cy, lift, shape, hf, tf)
    if key in _HIPS_CACHE:
        return _HIPS_CACHE[key]
    n = 240
    phs = np.arange(n) / n
    zmax = np.full(n, 1.0)
    for i, ph in enumerate(phs):
        for side, off in (("l", 0.0), ("r", 0.5)):
            a, th, st = foot_cycle((ph - off) % 1.0, T, v, duty, lift, sp, op, cy, side, shape, hf, tf)
            hj = REST_HIP_JOINT[side]
            dx, dy = a[0] - hj[0], a[1] - hj[1]
            reach = (LEG_LEN * (0.994 if st else 0.998)) ** 2 - dx * dx - dy * dy
            zmax[i] = min(zmax[i], a[2] + math.sqrt(max(reach, 0.0)) - hj[2])
    mid = duty / 2
    wave = np.cos(4 * math.pi * (phs - mid))
    nominal = base + (-bob * wave if running else bob * wave)
    raw = np.minimum(nominal, zmax - 0.004)
    k = np.exp(-0.5 * (np.minimum(np.arange(n), n - np.arange(n)) / (0.035 * n)) ** 2)
    k /= k.sum()
    smooth = np.real(np.fft.ifft(np.fft.fft(raw) * np.fft.fft(k)))
    final = np.minimum(smooth, zmax)

    def fn(ph):
        x = (ph % 1.0) * n
        i = int(x) % n
        u = x - int(x)
        return float(final[i] * (1 - u) + final[(i + 1) % n] * u)
    _HIPS_CACHE[key] = fn
    return fn


WALK_T = 0.9       # walk cycle (s): 1.35 m per cycle at 1.5 m/s; steps short enough that the hips dip < 4 cm


def clip_walk(t):
    # strike_pitch < 0: toes up at heel strike, which keeps the front leg's reach (and the hips) high
    return locomotion(t, WALK_T, 1.5, 0.52, hips_base=-0.025, bob=0.006, lift=0.09, strike_pitch=-12, off_pitch=62,
                      lean=3, arm_swing=16, elbow=16, elbow_extra=10, fingers=20, hips_yaw=5, chest_yaw=7,
                      center_y=0.09, heel_frac=0.12, toe_frac=0.6)


def clip_jog(t):
    return locomotion(t, 0.72, 4.0, 0.31, hips_base=-0.05, bob=0.022, lift=0.26, strike_pitch=6, off_pitch=62,
                      lean=9, arm_swing=32, elbow=70, elbow_extra=15, fingers=55, hips_yaw=8, chest_yaw=10,
                      center_y=0.09, running=True, arm_down=30, swing_shape=0.6, heel_frac=0.1, toe_frac=0.6)


def clip_run(t):
    return locomotion(t, 0.6, 7.0, 0.23, hips_base=-0.06, bob=0.03, lift=0.45, strike_pitch=0, off_pitch=65,
                      lean=16, arm_swing=48, elbow=85, elbow_extra=10, fingers=75, hips_yaw=10, chest_yaw=12,
                      center_y=0.1, running=True, arm_down=28, swing_shape=0.5, heel_frac=0.05, toe_frac=0.7)


def clip_crouch_walk(t):
    s = locomotion(t, 1.2, 1.5, 0.62, hips_base=-0.35, bob=0.012, lift=0.09, strike_pitch=8, off_pitch=35,
                   lean=24, arm_swing=12, elbow=35, elbow_extra=10, fingers=35, hips_yaw=5, chest_yaw=6,
                   center_y=0.02, head_comp=0.9, knee_pole=(0, -1, -0.2))
    return s


def standing_legs(spec, hips_z=0.0, pitch=0.0, spread=0.0, front=0.0):
    for side in ("l", "r"):
        a = REST_ANKLE[side].copy()
        a[0] += side_sign(side) * spread
        a[1] += front * (1 if side == "l" else -1)
        spec[f"leg_{side}"] = {"ankle": tuple(a), "pitch": pitch, "pole": (side_sign(side) * 0.15, -1, 0), "toe": 0.0}
    spec["hips_z"] = hips_z
    return spec


def clip_idle(t):
    T = 3.0
    ph = 2 * math.pi * t / T
    br = math.sin(ph)               # one breath per loop
    sh = math.sin(ph)               # weight shift
    s = relaxed_upper(16)
    s["hips"] = (1.0, 2.0 * sh, 1.5 * math.sin(ph + 0.6))
    s["spine"] = (-0.5 + 0.8 * br, -1.2 * sh, -0.8 * math.sin(ph + 0.6))
    s["chest"] = (-1.0 * br, -0.6 * sh, -0.5 * math.sin(ph + 0.6))
    s["neck"] = (0.5 * br, 0.4 * sh, 1.5 * math.sin(ph * 2 + 1.0) * 0.4)
    s["head"] = (0.5 * br, 0.4 * sh, 2.0 * math.sin(ph + 1.3))
    for side, sg in (("l", 1), ("r", -1)):
        s[f"shoulder_{side}"] = (0.0, 0.0, 0.6 * br)
        s[f"arm_{side}"]["swing"] = RELAXED_ARM["swing"] + 1.2 * math.sin(ph + sg * 0.5)
        s[f"arm_{side}"]["elbow"] = RELAXED_ARM["elbow"] + 2.0 * math.sin(ph + 0.8)
    standing_legs(s, -0.008 - 0.004 * (1 - math.cos(ph)) * 0.5, spread=0.012, front=0.01)
    return s


def clip_crouch_idle(t):
    T = 2.0
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    s = relaxed_upper(30)
    s["hips"] = (16.0 + 1.0 * br, 0.0, 0.0)
    s["spine"] = (9.0 + 1.0 * br, 0.0, 0.0)
    s["chest"] = (5.0 - 1.5 * br, 0.0, 0.0)
    s["neck"] = (-12.0, 0.0, 0.0)
    s["head"] = (-12.0 + br, 0.0, 1.5 * math.sin(ph))
    for side in ("l", "r"):
        s[f"arm_{side}"] = {"down": 30.0, "swing": 28.0 + br, "yaw": 0.0, "elbow": 40.0, "wrist": 10.0, "twist": 0.0}
    standing_legs(s, -0.35 - 0.006 * br, spread=0.04, front=0.03)
    for side in ("l", "r"):
        s[f"leg_{side}"]["pole"] = (side_sign(side) * 0.35, -1, 0)
    return s


def airborne_legs(s, k=0.0):
    """Loosely bent legs in the air; k in [-1, 1] alternates them slightly."""
    s["leg_l"] = {"thigh": (-28 - 8 * k, 0, 4), "knee": 45 + 10 * k, "foot": 18, "toe_bend": 0}
    s["leg_r"] = {"thigh": (-18 + 8 * k, 0, 4), "knee": 38 - 10 * k, "foot": 15, "toe_bend": 0}
    return s


def jump_pose_specs():
    """Key poses for jump (takeoff) - also used to make fall/land continuous."""
    stand = relaxed_upper(18)
    standing_legs(stand, 0.0)
    stand["hips"] = (0, 0, 0)
    crouch = relaxed_upper(30)
    standing_legs(crouch, -0.2, pitch=0.0)
    crouch["hips"] = (22.0, 0, 0)
    crouch["spine"] = (10.0, 0, 0)
    crouch["chest"] = (6.0, 0, 0)
    crouch["neck"] = (-14.0, 0, 0)
    crouch["head"] = (-10.0, 0, 0)
    for side in ("l", "r"):
        crouch[f"arm_{side}"] = {"down": 30.0, "swing": -45.0, "yaw": 0.0, "elbow": 25.0, "wrist": 0.0, "twist": 0.0}
    push = relaxed_upper(30)
    standing_legs(push, 0.02, pitch=35.0)
    for side in ("l", "r"):
        push[f"leg_{side}"]["ankle"] = tuple(np.array(REST_ANKLE[side]) + np.array([0, 0.02, 0.07]))
        push[f"leg_{side}"]["toe"] = 1.0
    push["hips"] = (4.0, 0, 0)
    push["spine"] = (-2.0, 0, 0)
    push["chest"] = (-4.0, 0, 0)
    push["neck"] = (-2.0, 0, 0)
    push["head"] = (0.0, 0, 0)
    for side in ("l", "r"):
        push[f"arm_{side}"] = {"down": 18.0, "swing": 120.0, "yaw": 0.0, "elbow": 30.0, "wrist": 0.0, "twist": 0.0}
    return stand, crouch, push


def to_fk_legs(spec):
    """Replace IK legs by the equivalent FK description (so key poses with FK legs can blend)."""
    pose = build_pose(spec)
    out = dict(spec)
    for side in ("l", "r"):
        s = side_sign(side)
        Wt = pose.Q[f"thigh_{side}"]
        # decompose thigh rotation to XYZ euler (left-side convention)
        m = Matrix([list(r) for r in Wt]).to_euler("XYZ")
        e = (math.degrees(m.x), s * math.degrees(m.y), s * math.degrees(m.z))
        # knee angle about the knee axis
        Qs = pose.Q[f"shin_{side}"]
        ang = math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(Qs) - 1) / 2))))
        Qf = pose.Q[f"foot_{side}"]
        fe = Matrix([list(r) for r in Qf]).to_euler("XYZ")
        out[f"leg_{side}"] = {"thigh": e, "knee": ang, "foot": math.degrees(fe.x), "toe_bend": 0.0}
    return out


def clip_jump(t):
    stand, crouch, push = jump_pose_specs()
    air = airborne_legs(dict(push), 0.0)
    keys = [(0.0, to_fk_legs(stand)), (0.12, to_fk_legs(crouch)), (0.22, to_fk_legs(push)), (0.3, air)]
    for k in keys:
        k[1]["hips_z"] = k[1].get("hips_z", 0.0)
    keys[-1][1]["hips_z"] = 0.0
    keys[-1][1]["hips"] = (2.0, 0, 0)
    for side in ("l", "r"):
        keys[-1][1][f"arm_{side}"] = {"down": 15.0, "swing": 95.0, "yaw": 0.0, "elbow": 35.0, "wrist": 0.0,
                                      "twist": 0.0}
    return interp_specs(keys, t)


def fall_spec(t):
    ph = 2 * math.pi * t / 1.0
    k = math.sin(ph)
    s = relaxed_upper(25)
    s["hips"] = (6.0 + 2 * k, 0, 0)
    s["spine"] = (-2.0, 0, 1.5 * k)
    s["chest"] = (-3.0, 0, 1.5 * k)
    s["neck"] = (-3.0, 0, 0)
    s["head"] = (-2.0, 0, 0)
    for side, sg in (("l", 1), ("r", -1)):
        s[f"arm_{side}"] = {"down": -8.0 + 6 * math.sin(ph + sg), "swing": 22.0 + 10 * math.sin(ph + sg * 1.2),
                            "yaw": 0.0, "elbow": 35.0 + 8 * math.sin(ph * 2 + sg), "wrist": 0.0, "twist": 0.0}
    airborne_legs(s, k)
    s["hips_z"] = 0.0
    return s


def clip_fall(t):
    return fall_spec(t)


def clip_land(t):
    first = fall_spec(0.0)
    first["leg_l"] = {"thigh": (-12, 0, 2), "knee": 14, "foot": 12, "toe_bend": 0}
    first["leg_r"] = {"thigh": (-8, 0, 2), "knee": 10, "foot": 10, "toe_bend": 0}
    first["hips_z"] = 0.0
    impact = relaxed_upper(30)
    standing_legs(impact, -0.28)
    impact["hips"] = (24.0, 0, 0)
    impact["spine"] = (12.0, 0, 0)
    impact["chest"] = (6.0, 0, 0)
    impact["neck"] = (-16.0, 0, 0)
    impact["head"] = (-10.0, 0, 0)
    for side in ("l", "r"):
        impact[f"arm_{side}"] = {"down": 10.0, "swing": 30.0, "yaw": 0.0, "elbow": 40.0, "wrist": 0.0, "twist": 0.0}
    mid = relaxed_upper(22)
    standing_legs(mid, -0.08)
    mid["hips"] = (8.0, 0, 0)
    mid["spine"] = (4.0, 0, 0)
    mid["neck"] = (-5.0, 0, 0)
    end = clip_idle(0.0)
    keys = [(0.0, first), (0.08, to_fk_legs(impact)), (0.22, to_fk_legs(mid)), (0.35, to_fk_legs(end))]
    keys = [(kt, _complete(s, keys[-1][1])) for kt, s in keys]
    return interp_specs(keys, t)


def _complete(spec, template):
    """Give spec every key of template (missing ones copied from template) so key poses can blend."""
    out = {}
    for k, v in template.items():
        if k not in spec:
            out[k] = v
        elif isinstance(v, dict):
            out[k] = {kk: spec[k].get(kk, vv) for kk, vv in v.items()}
        else:
            out[k] = spec[k]
    return out


def clip_swim_idle(t):
    T = 2.0
    ph = 2 * math.pi * t / T
    s = relaxed_upper(10)
    s["hips"] = (4.0, 0, 0)
    s["spine"] = (-2.0 + 1.5 * math.sin(ph * 2), 0, 0)
    s["chest"] = (-2.0, 0, 0)
    s["neck"] = (-4.0, 0, 0)
    s["head"] = (-4.0 + 2 * math.sin(ph * 2), 0, 0)
    for side, sg in (("l", 1), ("r", -1)):
        sc = math.sin(ph * 2 + (0 if side == "l" else 0.4))
        s[f"arm_{side}"] = {"down": -6.0, "swing": 30.0, "yaw": 22.0 * sc, "elbow": 55.0 + 10 * sc, "wrist": 0.0,
                            "twist": -40.0 + 20 * sc}
        s[f"fingers_{side}"] = 8.0
    for side, off in (("l", 0.0), ("r", math.pi)):
        k = math.sin(ph + off)
        kk = math.cos(ph + off)
        s[f"leg_{side}"] = {"thigh": (-38 - 14 * k, 0, 10 + 6 * kk), "knee": 70 + 25 * kk, "foot": 25 + 10 * k,
                            "toe_bend": 0}
    s["hips_z"] = 0.0
    return s


def clip_swim(t):
    T = 1.2
    ph = t / T
    s = relaxed_upper(5)
    roll = 28.0 * math.sin(2 * math.pi * (ph - 0.0))
    s["hips"] = (70.0, 0, 0)
    s["spine"] = (0.0, 0, -roll * 0.6)
    s["chest"] = (-2.0, 0, -roll * 0.4)
    s["neck"] = (-30.0, 0, roll * 0.5)
    s["head"] = (-25.0, 0, roll * 0.4)
    for side, off in (("r", 0.25), ("l", 0.75)):
        # arm angle around the shoulder: 90 = pointing down in the water (mid-pull at t = off)
        phi = 90.0 - 360.0 * (ph - off)
        u = ((ph - off + 0.5) % 1.0)           # 0.5 = mid pull
        pull = math.exp(-((u - 0.5) / 0.16) ** 2)
        rec = math.exp(-((u - 0.0) / 0.14) ** 2) + math.exp(-((u - 1.0) / 0.14) ** 2)
        s[f"arm_{side}"] = {"down": 30.0 - 10 * rec, "swing": phi, "yaw": 0.0, "elbow": 15 + 55 * pull + 70 * rec,
                            "wrist": 10.0 * pull, "twist": 0.0}
        s[f"fingers_{side}"] = 6.0
    for side, off in (("l", 0.0), ("r", 0.5)):
        k = math.sin(2 * math.pi * (3 * ph + off))
        s[f"leg_{side}"] = {"thigh": (-6 + 14 * k, 0, 2), "knee": 18 + 14 * max(0.0, -k), "foot": 45, "toe_bend": 10}
    s["hips_z"] = 0.0
    return s


def clip_sit(t):
    T = 2.0
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    s = relaxed_upper(60)
    s["hips_z"] = -0.45
    s["hips"] = (-6.0, 0, 0)
    s["spine"] = (4.0 + 0.6 * br, 0, 0)
    s["chest"] = (2.0 - 0.8 * br, 0, 0)
    s["neck"] = (0.0, 0, 0)
    s["head"] = (0.4 * br, 0, 1.5 * math.sin(ph * 0.5 * 2))
    for side in ("l", "r"):
        s[f"leg_{side}"] = {"thigh": (-84, 0, 6), "knee": 80, "foot": -6, "toe_bend": 0}
        sg = side_sign(side)
        grip = np.array([sg * 0.17, -0.42, 1.22 + 0.002 * br])
        s[f"arm_{side}"] = arm_ik_from_grip(side, grip, (sg * -0.2, -0.95, -0.2), (sg * -0.2, 0.0, 1.0),
                                            pole=(sg * 0.7, 0.4, -1.0))
        s[f"thumb_{side}"] = 35.0
    return s


# ------------------------------------------------------------------------------------------------
# Upper-body clips (only spine/chest/neck/head/shoulders/arms/hands/fingers/thumbs are keyed)
# ------------------------------------------------------------------------------------------------

def upper_base(t, T=2.0, amp=1.0):
    """Subtle breathing torso shared by hold loops."""
    ph = 2 * math.pi * t / T
    br = math.sin(ph) * amp
    return {"spine": (0.6 * br, 0, 0), "chest": (-0.8 * br, 0, 0), "neck": (0.3 * br, 0, 0), "head": (0.2 * br, 0, 0),
            "shoulder_l": (0, 0, 0.5 * br), "shoulder_r": (0, 0, 0.5 * br)}


def relaxed_left(s, ph=0.0):
    s["arm_l"] = dict(RELAXED_ARM)
    s["arm_l"]["swing"] += 1.0 * math.sin(ph)
    s["fingers_l"] = 18.0
    s["thumb_l"] = 10.0
    return s


TOOL_GRIP = np.array([-0.17, -0.27, 1.13])        # right fist centre when holding a one-handed tool (rest coords)


def tool_arm(grip=TOOL_GRIP, d=(0.05, -0.95, 0.3), w=(0.1, -0.25, 1.0)):
    return arm_ik_from_grip("r", grip, d, w, pole=(-0.6, 0.5, -1.0))


def clip_hold_tool(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = upper_base(t, T)
    relaxed_left(s, ph)
    s["arm_r"] = tool_arm(TOOL_GRIP + np.array([0, 0, 0.004 * math.sin(ph)]))
    s["fingers_r"] = 95.0
    s["thumb_r"] = 45.0
    return s


def hold_tool_static():
    return clip_hold_tool(0.0)


def oneshot(keys, t, template=None):
    tmpl = template or keys[0][1]
    keys = [(kt, _complete(s, tmpl)) for kt, s in keys]
    return interp_specs(keys, t)


def with_right_arm(base, **kw):
    s = {k: (dict(v) if isinstance(v, dict) else v) for k, v in base.items()}
    for k, v in kw.items():
        s[k] = v
    return s


def clip_tool_use(t):
    h = hold_tool_static()
    push = with_right_arm(h, arm_r=tool_arm(TOOL_GRIP + np.array([0.01, -0.13, 0.03])), chest=(3.0, 0, 2.0),
                          spine=(2.0, 0, 1.0))
    return oneshot([(0.0, h), (0.12, push), (0.18, push), (0.3, h)], t)


def clip_weld(t):
    h = hold_tool_static()
    aim = with_right_arm(h, arm_r=tool_arm(TOOL_GRIP + np.array([0.0, -0.1, 0.04]), d=(0.05, -0.97, 0.05),
                                           w=(0.1, 0.0, 1.0)), chest=(4.0, 0, 2.0))
    keys = [(0.0, h), (0.1, aim)]
    for i, tt in enumerate((0.16, 0.22, 0.28, 0.34)):
        j = np.array([0.004 * (-1) ** i, 0.0, 0.004 * (-1) ** (i // 2)])
        keys.append((tt, with_right_arm(aim, arm_r=tool_arm(TOOL_GRIP + np.array([0.0, -0.1, 0.04]) + j,
                                                             d=(0.05, -0.97, 0.05), w=(0.1, 0.0, 1.0)))))
    keys += [(0.4, aim), (0.5, h)]
    return oneshot(keys, t)


def clip_paint_spray(t):
    h = hold_tool_static()
    def aim(x):
        return with_right_arm(h, arm_r=tool_arm(TOOL_GRIP + np.array([x, -0.08, 0.06]), d=(x * 1.5, -0.97, 0.05),
                                                w=(0.1, 0.0, 1.0)), chest=(3.0, 0, -x * 40))
    return oneshot([(0.0, h), (0.08, aim(-0.08)), (0.3, aim(0.1)), (0.4, h)], t)


# The Sledgehammer, built to its reference sheet: 1.3 m long, a 38 mm steel shaft with a 280 mm knurled rubber grip
# and one yellow safety band, and a 280 x 140 x 110 mm head of stacked, bolted steel plates. The right fist sits on
# the bare shaft just above the band (the origin) and the left fist on the grip SLEDGE_SPACING below it.
SLEDGE_BUTT = -0.38            # bottom of the end cap (grip frame y)
SLEDGE_HEAD_Y = 0.85           # centre of the head
SLEDGE_HEAD = (0.14, 0.07, 0.055)   # head half sizes: strike axis (x), along the handle (y), across (z)
SLEDGE_SPACING = 0.26          # left fist below the right one, along the handle
SHAFT_R = 0.019


# Sledgehammer: the right fist sits on the shaft (the prop's origin), the left one lower on the grip, `spacing`
# below it along the handle (it slides between 0.1 and 0.33 m, all on the rubber). The head's strike faces point
# along the fingers (d), so d is kept on the tangent of the swing arc in the body's side plane: the face leads.

def heavy_arms(grip_r, axis, d_hint=None, spacing=SLEDGE_SPACING, space="chest", poles=((-0.7, 0.4, -1.0), (0.6, 0.3, -1.0))):
    axis = norm(np.asarray(axis, float))
    if d_hint is None:
        d_hint = (0.0, -axis[2], axis[1])      # face forward along an overhead arc
    d_hint = np.asarray(d_hint, float)
    d = norm(d_hint - np.dot(d_hint, axis) * axis)
    grip_l = np.asarray(grip_r, float) - axis * spacing
    return {"arm_r": arm_ik_from_grip("r", grip_r, d, axis, pole=poles[0], space=space),
            "arm_l": arm_ik_from_grip("l", grip_l, d, axis, pole=poles[1], space=space)}


def heavy_key(grip_r, axis, spacing=SLEDGE_SPACING, d_hint=None, space="chest", **body):
    """One key of a two-handed Sledgehammer clip: both arms on the handle plus the torso angles given."""
    s = upper_base(0.0)
    poles = FP_POLES if space == "world" else ((-0.7, 0.4, -1.0), (0.6, 0.3, -1.0))
    s.update(heavy_arms(np.asarray(grip_r, float), axis, d_hint, spacing, space, poles))
    for side in ("l", "r"):
        s[f"fingers_{side}"] = 95.0
        s[f"thumb_{side}"] = 45.0
    s.update(body)
    return s


# Ready pose: the handle rests on the right shoulder, the head hanging behind it, both fists in front of the chest.
HEAVY_R = np.array([-0.12, -0.27, 1.29])
HEAVY_AXIS = norm(np.array([-0.17, 0.8, 0.58]))
HEAVY_BODY = dict(spine=(0.0, 0, -3.0), chest=(-1.0, 0, -7.0), neck=(0.0, 0, 4.0), head=(0.0, 0, 3.0))


def clip_hold_heavy(t, T=2.0):
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    s = heavy_key(HEAVY_R + np.array([0.0, 0.0, 0.005 * br]), HEAVY_AXIS + np.array([0.0, 0.0, 0.006 * br]),
                  spacing=0.22, **HEAVY_BODY)
    base = upper_base(t, T)
    for b in ("spine", "chest", "neck", "head"):
        s[b] = tuple(x + y for x, y in zip(HEAVY_BODY[b], base[b]))
    s["shoulder_l"], s["shoulder_r"] = base["shoulder_l"], base["shoulder_r"]
    return s


def heavy_ready():
    return clip_hold_heavy(0.0)


# Three swings, picked at random for each click (CharacterRig.GestureClip): overhead, a forehand sweep from the right
# and a backhand from the left. Each lasts SWING_SECONDS (sledge.swing_seconds): a quick wind-up, the hit at
# STRIKE_SECONDS (sledge.strike_seconds), then the recovery back to the ready pose. The overhead keys below were
# first laid out over 1 s and are squeezed onto that timing by swing_time.
SWING_SECONDS = 0.6
STRIKE_SECONDS = 0.25


def swing_time(t):
    """Maps a key time on the 1 s layout (wind-up 0.35, hit 0.6) onto the swing's own timing."""
    pts = [(0.0, 0.0), (0.35, 0.14), (0.6, STRIKE_SECONDS), (1.0, SWING_SECONDS)]
    for (a, x), (b, y) in zip(pts, pts[1:]):
        if t <= b + 1e-9:
            return x + (y - x) * (t - a) / (b - a)
    return SWING_SECONDS


# Overhead: the right fist slides down to the left one as the head comes over.
SLEDGE_SWING_KEYS = [
    (0.0, None),
    (0.17, ((-0.11, -0.14, 1.6), (-0.12, 0.5, 0.86), 0.22,
            dict(spine=(-3.0, 0, -5.0), chest=(-5.0, 0, -9.0), neck=(0.0, 0, 5.0), head=(-3.0, 0, 3.0)))),
    (0.35, ((-0.08, 0.0, 1.86), (-0.04, 0.9, -0.43), 0.2,
            dict(spine=(-7.0, 2.0, -6.0), chest=(-10.0, 1.0, -10.0), neck=(-2.0, 0, 6.0), head=(-6.0, 0, 4.0)))),
    (0.46, ((-0.07, -0.2, 1.86), (-0.02, 0.05, 1.0), 0.18,
            dict(spine=(-2.0, 1.0, -2.0), chest=(-4.0, 0, -3.0), neck=(-2.0, 0, 2.0), head=(-4.0, 0, 1.0)))),
    (0.53, ((-0.05, -0.44, 1.56), (0.0, -0.74, 0.67), 0.15,
            dict(spine=(8.0, 0, 2.0), chest=(6.0, 0, 3.0), neck=(-6.0, 0, -1.0), head=(-3.0, 0, 0)))),
    (0.6, ((-0.04, -0.52, 1.2), (0.0, -0.94, -0.34), 0.12,
           dict(spine=(13.0, 0, 3.0), chest=(9.0, 0, 4.0), neck=(-10.0, 0, -2.0), head=(-6.0, 0, 0)))),
    (0.67, ((-0.04, -0.51, 1.24), (0.0, -0.95, -0.27), 0.12,
            dict(spine=(12.0, 0, 3.0), chest=(8.0, 0, 3.0), neck=(-9.0, 0, -2.0), head=(-5.0, 0, 0)))),
    (0.82, ((-0.1, -0.38, 1.3), (-0.1, -0.15, 0.98), 0.18,
            dict(spine=(6.0, 0, -1.0), chest=(3.0, 0, -4.0), neck=(-3.0, 0, 2.0), head=(-2.0, 0, 1.0)))),
    (1.0, None),
]


def heavy_clip(keys, ready, t, timing=swing_time):
    return oneshot([(timing(kt), ready if k is None else heavy_key(k[0], k[1], k[2], d_hint=k[4] if len(k) > 4 else None, **k[3]))
                    for kt, k in keys], t, template=ready)


def same_time(t):
    return t


# Forehand: the hammer drawn back past the right hip with the body turned right, swept flat across the front from
# right to left (the face leading, +x), the hit straight ahead at chest height, then round to the left.
SWEEP_KEYS = [
    (0.0, None),
    (0.07, ((-0.2, -0.12, 1.36), (-0.55, 0.45, 0.7), 0.2, None, None)),
    (0.14, ((-0.26, -0.12, 1.2), (-0.9, 0.15, 0.4), 0.2,
            dict(spine=(-2.0, -3.0, -16.0), chest=(-3.0, -2.0, -18.0), neck=(0.0, 0, 15.0), head=(0.0, 0, 12.0)), (0.2, -0.8, 0.0))),
    (0.2, ((-0.16, -0.32, 1.12), (-0.62, -0.62, 0.3), 0.17,
           dict(spine=(4.0, -1.0, -6.0), chest=(3.0, 0, -6.0), neck=(-2.0, 0, 6.0), head=(-2.0, 0, 4.0)), (0.7, -0.6, 0.0))),
    (STRIKE_SECONDS, ((-0.02, -0.46, 1.1), (0.08, -0.98, 0.12), 0.15,
                      dict(spine=(8.0, 1.0, 4.0), chest=(6.0, 1.0, 6.0), neck=(-5.0, 0, -5.0), head=(-4.0, 0, -4.0)), (1.0, 0.0, 0.0))),
    (0.32, ((0.14, -0.36, 1.1), (0.75, -0.6, 0.2), 0.15,
            dict(spine=(6.0, 2.0, 14.0), chest=(4.0, 2.0, 16.0), neck=(-4.0, 0, -12.0), head=(-3.0, 0, -10.0)), (0.6, 0.8, 0.0))),
    (0.42, ((0.08, -0.3, 1.2), (0.6, -0.15, 0.78), 0.18,
            dict(spine=(3.0, 1.0, 8.0), chest=(2.0, 1.0, 8.0), neck=(-2.0, 0, -6.0), head=(-2.0, 0, -5.0)), None)),
    (SWING_SECONDS, None),
]

# Backhand: the mirror image, drawn back past the left hip and swept from left to right (the face leading, -x).
BACKHAND_KEYS = [
    (0.0, None),
    (0.07, ((0.02, -0.26, 1.36), (0.45, 0.35, 0.82), 0.2, None, None)),
    (0.14, ((0.14, -0.16, 1.2), (0.85, 0.25, 0.45), 0.2,
            dict(spine=(-2.0, 3.0, 16.0), chest=(-3.0, 2.0, 18.0), neck=(0.0, 0, -15.0), head=(0.0, 0, -12.0)), (-0.2, -0.8, 0.0))),
    (0.2, ((0.06, -0.36, 1.12), (0.62, -0.62, 0.3), 0.17,
           dict(spine=(4.0, 1.0, 6.0), chest=(3.0, 0, 6.0), neck=(-2.0, 0, -6.0), head=(-2.0, 0, -4.0)), (-0.7, -0.6, 0.0))),
    (STRIKE_SECONDS, ((-0.06, -0.46, 1.1), (-0.08, -0.98, 0.12), 0.15,
                      dict(spine=(8.0, -1.0, -4.0), chest=(6.0, -1.0, -6.0), neck=(-5.0, 0, 5.0), head=(-4.0, 0, 4.0)), (-1.0, 0.0, 0.0))),
    (0.32, ((-0.22, -0.34, 1.1), (-0.75, -0.6, 0.2), 0.15,
            dict(spine=(6.0, -2.0, -14.0), chest=(4.0, -2.0, -16.0), neck=(-4.0, 0, 12.0), head=(-3.0, 0, 10.0)), (-0.6, 0.8, 0.0))),
    (0.42, ((-0.18, -0.3, 1.24), (-0.45, 0.2, 0.87), 0.2,
            dict(spine=(2.0, -1.0, -6.0), chest=(1.0, -1.0, -8.0), neck=(-1.0, 0, 6.0), head=(-1.0, 0, 5.0)), None)),
    (SWING_SECONDS, None),
]


def side_keys(keys, ready_body):
    """Fills a side swing's missing torso angles with the ready pose's."""
    return [(kt, k if k is None else (k[0], k[1], k[2], k[3] if k[3] is not None else ready_body, k[4])) for kt, k in keys]


def clip_sledge_swing_side(t):
    return heavy_clip(side_keys(SWEEP_KEYS, HEAVY_BODY), heavy_ready(), t, timing=same_time)


def clip_sledge_swing_back(t):
    return heavy_clip(side_keys(BACKHAND_KEYS, HEAVY_BODY), heavy_ready(), t, timing=same_time)


def clip_sledge_swing(t):
    return heavy_clip(SLEDGE_SWING_KEYS, heavy_ready(), t)


# Block (hold right click): the hammer raised diagonally across the chest, head up and to the right.
BLOCK_R = np.array([0.06, -0.4, 1.3])
BLOCK_AXIS = norm(np.array([-0.8, -0.12, 0.6]))
BLOCK_BODY = dict(spine=(6.0, 0, 2.0), chest=(4.0, 0, 6.0), neck=(6.0, 0, -4.0), head=(4.0, 0, -2.0))


def clip_hold_heavy_block(t, T=2.0):
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    s = heavy_key(BLOCK_R + np.array([0.0, 0.0, 0.004 * br]), BLOCK_AXIS, 0.26, d_hint=(0.0, -1.0, 0.0), **BLOCK_BODY)
    base = upper_base(t, T, 0.7)
    for b in ("spine", "chest", "neck", "head"):
        s[b] = tuple(x + y for x, y in zip(BLOCK_BODY[b], base[b]))
    return s


# First person: the same three, posed for the camera of the arms-only model (game/data/character.json:
# first_person_offset and first_person_pitch_degrees put the eye at FP_EYE looking along FP_LOOK in rest coords).
# The ready pose stands the hammer up at the lower right of the view; the swing throws it back over the right
# shoulder and down through the middle of the view, closer and wider than the body's. Only the forearms are drawn
# in first person, so the fists stay low in the view and the elbows (the open sleeve ends) stay out of it.
FP_EYE = np.array([0.0, 0.164, 1.237])
FP_LOOK = norm(np.array([0.0, -0.97, 0.242]))
# The body is not drawn in first person, so the torso stoops to bring the shoulders forward and below the eye:
# the elbows then hang under the bottom of the view, whatever the fists do. Fists are placed in model space.
FP_BODY = dict(spine=(55.0, 0, 0.0), chest=(35.0, 0, 0.0), neck=(-45.0, 0, 0.0), head=(-45.0, 0, 0.0), space="world")
FP_POLES = ((-0.4, 0.3, -1.0), (0.4, 0.3, -1.0))
FP_READY = ((-0.17, -0.4, 1.03), (-0.25, -0.35, 0.9), 0.16)


def clip_hold_heavy_fp(t, T=2.0):
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    grip, axis, spacing = FP_READY
    s = heavy_key(np.array(grip) + np.array([0.0, 0.0, 0.006 * br]), axis, spacing, **FP_BODY)
    base = upper_base(t, T, 0.5)
    s["shoulder_l"], s["shoulder_r"] = base["shoulder_l"], base["shoulder_r"]
    return s


SLEDGE_SWING_FP_KEYS = [
    (0.0, None),
    (0.17, ((-0.2, -0.36, 1.12), (-0.2, 0.0, 1.0), 0.16, FP_BODY)),
    (0.35, ((-0.22, -0.3, 1.2), (-0.12, 0.75, 0.65), 0.16, FP_BODY)),
    (0.46, ((-0.12, -0.42, 1.15), (-0.03, -0.1, 1.0), 0.15, FP_BODY)),
    (0.53, ((-0.18, -0.38, 1.12), (0.2, -0.25, 0.95), 0.13, FP_BODY)),
    (0.6, ((-0.2, -0.4, 0.98), (0.37, -0.8, 0.48), 0.12, FP_BODY)),
    (0.67, ((-0.2, -0.41, 0.95), (0.37, -0.83, 0.42), 0.12, FP_BODY)),
    (0.82, ((-0.14, -0.45, 1.05), (-0.15, -0.55, 0.82), 0.15, FP_BODY)),
    (1.0, None),
]


def clip_sledge_swing_fp(t):
    return heavy_clip(SLEDGE_SWING_FP_KEYS, clip_hold_heavy_fp(0.0), t)


# First-person side swings: the head comes in from one edge of the view and crosses the middle at the hit.
SWEEP_FP_KEYS = [
    (0.0, None),
    (0.07, ((-0.24, -0.33, 1.1), (-0.55, 0.2, 0.8), 0.16, FP_BODY, None)),
    (0.14, ((-0.28, -0.26, 1.06), (-0.9, 0.05, 0.44), 0.16, FP_BODY, (0.2, -0.9, 0.0))),
    (0.2, ((-0.2, -0.38, 1.02), (-0.6, -0.62, 0.5), 0.14, FP_BODY, (0.8, -0.5, 0.0))),
    (STRIKE_SECONDS, ((-0.08, -0.42, 1.0), (0.12, -0.82, 0.56), 0.13, FP_BODY, (1.0, 0.0, 0.0))),
    (0.32, ((0.06, -0.4, 0.98), (0.78, -0.48, 0.4), 0.13, FP_BODY, (0.5, 0.8, 0.0))),
    (0.42, ((-0.06, -0.42, 1.0), (0.3, -0.4, 0.86), 0.15, FP_BODY, None)),
    (SWING_SECONDS, None),
]

BACKHAND_FP_KEYS = [
    (0.0, None),
    (0.07, ((-0.06, -0.36, 1.06), (0.3, -0.1, 0.95), 0.16, FP_BODY, None)),
    (0.14, ((0.08, -0.32, 1.02), (0.88, 0.0, 0.48), 0.16, FP_BODY, (-0.2, -0.9, 0.0))),
    (0.2, ((0.02, -0.4, 1.0), (0.6, -0.62, 0.5), 0.14, FP_BODY, (-0.8, -0.5, 0.0))),
    (STRIKE_SECONDS, ((-0.1, -0.42, 1.0), (-0.12, -0.82, 0.56), 0.13, FP_BODY, (-1.0, 0.0, 0.0))),
    (0.32, ((-0.24, -0.38, 0.98), (-0.78, -0.48, 0.4), 0.13, FP_BODY, (-0.5, 0.8, 0.0))),
    (0.42, ((-0.22, -0.4, 1.02), (-0.4, -0.3, 0.86), 0.15, FP_BODY, None)),
    (SWING_SECONDS, None),
]


def clip_sledge_swing_side_fp(t):
    return heavy_clip(SWEEP_FP_KEYS, clip_hold_heavy_fp(0.0), t, timing=same_time)


def clip_sledge_swing_back_fp(t):
    return heavy_clip(BACKHAND_FP_KEYS, clip_hold_heavy_fp(0.0), t, timing=same_time)


def clip_hold_heavy_block_fp(t, T=2.0):
    ph = 2 * math.pi * t / T
    br = math.sin(ph)
    s = heavy_key(np.array([0.0, -0.45, 1.08 + 0.004 * br]), norm(np.array([-0.75, -0.35, 0.56])), 0.22,
                  d_hint=(0.0, -1.0, 0.0), **FP_BODY)
    return s


GUN_R = np.array([-0.12, -0.31, 1.3])


def gun_arms(grip_r=GUN_R, pitch=0.0):
    d = norm(np.array([0.06, -1.0, math.tan(math.radians(pitch))]))
    w = norm(np.array([0.0, math.sin(math.radians(pitch)) * 1.0, 1.0]))
    w = norm(w - np.dot(w, d) * d)
    # support hand under the barrel, palm up
    barrel = np.asarray(grip_r) + w * 0.075 + d * 0.15
    dl = norm(np.array([-0.35, -0.95, 0.0]))
    nl = np.array([0.0, 0.0, 1.0])
    wl = np.cross(nl, dl)
    palm = barrel - w * 0.035
    wrist_l = palm - dl * 0.045 - nl * 0.016
    return {"arm_r": arm_ik_from_grip("r", grip_r, d, w, pole=(-0.6, 0.3, -1.0)),
            "arm_l": {"pos": tuple(wrist_l), "d": tuple(dl), "w": tuple(wl), "pole": (0.6, 0.2, -1.0),
                      "space": "chest"}}


def clip_hold_gun(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = upper_base(t, T, 0.6)
    s.update(gun_arms(GUN_R + np.array([0, 0, 0.003 * math.sin(ph)])))
    s["fingers_r"] = 92.0
    s["thumb_r"] = 40.0
    s["fingers_l"] = 40.0
    s["thumb_l"] = 15.0
    s["neck"] = (s["neck"][0], 0, 4.0)
    s["head"] = (s["head"][0] + 3.0, -4.0, 4.0)
    return s


def clip_fire_gun(t):
    h = clip_hold_gun(0.0)
    kick = with_right_arm(h, **gun_arms(GUN_R + np.array([0.0, 0.045, 0.02]), pitch=8.0), chest=(-4.0, 0, 0),
                          spine=(-2.0, 0, 0))
    return oneshot([(0.0, h), (0.04, kick), (0.12, with_right_arm(h, **gun_arms(GUN_R + np.array([0, 0.01, 0.005]),
                                                                                   pitch=3.0))), (0.25, h)], t)


ITEM_R = np.array([-0.08, -0.3, 1.24])


def item_arm(grip=ITEM_R, d=(0.35, -0.75, 0.25), w=(0.2, -0.3, 1.0)):
    return arm_ik_from_grip("r", grip, d, w, pole=(-0.7, 0.4, -1.0))


def clip_hold_item(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = upper_base(t, T)
    relaxed_left(s, ph)
    s["arm_r"] = item_arm(ITEM_R + np.array([0, 0, 0.004 * math.sin(ph)]))
    s["fingers_r"] = 70.0
    s["thumb_r"] = 35.0
    return s


def clip_eat(t):
    h = clip_hold_item(0.0)
    mouth = np.array([-0.01, -0.13, 1.56])
    at = with_right_arm(h, arm_r=item_arm(mouth, d=(0.6, -0.2, 0.6), w=(0.3, -0.5, 0.8)), neck=(6.0, 0, 0),
                        head=(4.0, 0, 0))
    down = with_right_arm(h, arm_r=item_arm(np.array([-0.07, -0.24, 1.36])))
    return oneshot([(0.0, h), (0.22, at), (0.36, at), (0.5, down), (0.68, at), (0.8, at), (1.0, h)], t)


def clip_throw(t):
    h = clip_hold_item(0.0)
    back = with_right_arm(h, arm_r=item_arm(np.array([-0.27, 0.12, 1.62]), d=(-0.3, 0.6, 0.6), w=(0.0, -0.7, 0.6)),
                          spine=(-4.0, 0, -12.0), chest=(-6.0, 0, -14.0), head=(0, 0, 12.0),
                          arm_l={"down": 10.0, "swing": 60.0, "yaw": 0.0, "elbow": 30.0, "wrist": 0.0, "twist": 0.0})
    release = with_right_arm(h, arm_r=item_arm(np.array([-0.12, -0.5, 1.6]), d=(0.0, -0.8, 0.6),
                                               w=(0.3, -0.4, 0.8)),
                             spine=(6.0, 0, 6.0), chest=(6.0, 0, 10.0), head=(0, 0, -10.0),
                             arm_l={"down": 30.0, "swing": -10.0, "yaw": 0.0, "elbow": 30.0, "wrist": 0.0,
                                    "twist": 0.0}, fingers_r=10.0)
    follow = with_right_arm(release, arm_r=item_arm(np.array([0.02, -0.38, 1.12]), d=(0.4, -0.6, -0.7),
                                                    w=(0.6, -0.5, 0.3)), spine=(10.0, 0, 8.0), chest=(8.0, 0, 10.0),
                            fingers_r=20.0)
    return oneshot([(0.0, h), (0.2, back), (0.29, release), (0.38, follow), (0.5, h)], t, template=h)


def relaxed_ik_upper():
    s = upper_base(0.0)
    s.update(relaxed_upper(18))
    s["arm_r"] = relaxed_arm_ik("r")
    return s


def clip_plant(t):
    r = relaxed_ik_upper()
    reach = with_right_arm(r, spine=(28.0, 0, -4.0), chest=(24.0, 0, -6.0), neck=(-14.0, 0, 0), head=(-8.0, 0, 0),
                           arm_r={"pos": (-0.2, -0.5, 0.74), "d": (0.0, -0.3, -1.0), "w": (0.2, -1.0, 0.0),
                                  "pole": (-0.8, 0.3, -0.3), "space": "world"},
                           arm_l={"down": 26.0, "swing": 25.0, "yaw": 0.0, "elbow": 35.0, "wrist": 0.0, "twist": 0.0},
                           fingers_r=40.0)
    pat = with_right_arm(reach, arm_r=dict(reach["arm_r"], pos=(-0.2, -0.48, 0.70)), fingers_r=10.0)
    r_w = dict(r)
    # express the relaxed right arm in world space for the blend
    pose = build_pose(r)
    W = pose.W["hand_r"]
    d0, w0, n0 = hand_frame("r")
    r_w["arm_r"] = {"pos": tuple(pose.P["hand_r"]), "d": tuple(W @ d0), "w": tuple(W @ w0),
                    "pole": tuple(r["arm_r"]["pole"]), "space": "world"}
    return oneshot([(0.0, r_w), (0.32, reach), (0.45, pat), (0.56, reach), (0.8, r_w)], t, template=r_w)


def clip_pour(t):
    carry = upper_base(0.0)
    relaxed_left(carry)
    carry["arm_r"] = arm_ik_from_grip("r", np.array([-0.24, -0.1, 0.92]), (-0.1, -0.2, -1.0), (0.0, -1.0, 0.2),
                                      pole=(-0.4, 0.9, -0.3))
    carry["fingers_r"] = 95.0
    carry["thumb_r"] = 45.0
    lift = with_right_arm(carry, arm_r=arm_ik_from_grip("r", np.array([-0.2, -0.42, 1.2]), (0.0, -0.15, -1.0),
                                                        (0.0, -1.0, 0.15), pole=(-0.7, 0.4, -1.0)))
    tip = with_right_arm(carry, arm_r=arm_ik_from_grip("r", np.array([-0.19, -0.45, 1.24]), (0.0, -1.0, -0.35),
                                                       (0.0, 0.3, 1.0), pole=(-0.7, 0.4, -1.0)),
                         spine=(4.0, 0, 0), chest=(4.0, 0, 0))
    return oneshot([(0.0, carry), (0.25, lift), (0.42, tip), (0.6, tip), (0.8, carry)], t)


def clip_interact(t):
    r = relaxed_ik_upper()
    press = with_right_arm(r, arm_r={"pos": (-0.12, -0.5, 1.28), "d": (0.05, -1.0, 0.0), "w": (-0.1, 0.0, 1.0),
                                     "pole": (-0.7, 0.3, -1.0), "space": "chest"}, fingers_r=8.0,
                           chest=(3.0, 0, 3.0))
    push = with_right_arm(press, arm_r=dict(press["arm_r"], pos=(-0.12, -0.53, 1.28)))
    return oneshot([(0.0, r), (0.16, press), (0.22, push), (0.27, press), (0.4, r)], t)


def clip_hurt(t):
    r = relaxed_upper(18)
    r.update(upper_base(0.0))
    flinch = {k: (dict(v) if isinstance(v, dict) else v) for k, v in r.items()}
    flinch.update({"spine": (-8.0, 4.0, 6.0), "chest": (-6.0, 3.0, 5.0), "neck": (8.0, 0, -6.0), "head": (10.0, 6.0, -8.0),
                   "shoulder_l": (0, 0, 10.0), "shoulder_r": (0, 0, 10.0)})
    for side in ("l", "r"):
        flinch[f"arm_{side}"] = {"down": 28.0, "swing": 20.0, "yaw": 0.0, "elbow": 55.0, "wrist": 10.0, "twist": 0.0}
        flinch[f"fingers_{side}"] = 45.0
    return oneshot([(0.0, r), (0.07, flinch), (0.14, flinch), (0.3, r)], t)


# ------------------------------------------------------------------------------------------------
# Water bucket clips
# ------------------------------------------------------------------------------------------------
# The bucket hangs from the right fist: by the back of its rim while it is empty (light, the handle hanging down in
# front) and by its handle once it is full, the left hand steadying it. The right hand is keyed; the bucket follows it
# through bucket_in_grip with the grip, tilt and water of the clip's curve in game/data/character.json (the game moves
# the prop the same way, game/scripts/Player/BucketRig.cs), and the left hand is put on the bucket where it is in each
# frame, so it never slips off. Keys are (time, right fist, fingers, thumb, torso); fingers and thumb are the right
# hand's directions: fingers point down the bucket's axis, the thumb along its hinges (to the bucket's -X).

def bucket_motion(clip, t):
    """(grip, tilt degrees, water) of a bucket clip at time t: eased between its keys."""
    keys = BUCKET["motion"].get(clip.removesuffix("_fp"))
    if keys is None:
        return (1.0, 0.0, 1.0) if clip.startswith("hold_bucket_full") else (0.0, 0.0, 0.0)
    if t <= keys[0][0]:
        return tuple(keys[0][1:])
    for a, b in zip(keys, keys[1:]):
        if t <= b[0]:
            u = sstep(0.0, 1.0, (t - a[0]) / max(b[0] - a[0], 1e-9))
            return tuple(x + (y - x) * u for x, y in zip(a[1:], b[1:]))
    return tuple(keys[-1][1:])


def grip_world(pose, side="r"):
    """A solved pose's fist grip frame in model space (4x4: grip -> model)."""
    d0, w0, _ = hand_frame(side)
    W = pose.W[f"hand_{side}"]
    d, w = W @ d0, W @ w0
    M = np.eye(4)
    M[:3, :3] = np.stack([d, w, np.cross(d, w)], axis=1)
    M[:3, 3] = pose.point(f"hand_{side}", grip_point(side))
    return M


def bucket_world(pose, clip, t):
    g, tilt, _ = bucket_motion(clip, t)
    return grip_world(pose) @ bucket_in_grip(g, tilt)


BUCKET_R_POLE = (-0.6, 0.5, -1.0)


def bucket_key(fist, fingers, thumb, space="chest", pole=BUCKET_R_POLE, curl=95.0, **body):
    s = upper_base(0.0)
    relaxed_left(s)
    s["arm_r"] = arm_ik_from_grip("r", np.asarray(fist, float), fingers, thumb, pole=pole, space=space)
    s["fingers_r"] = curl
    s["thumb_r"] = 45.0
    s.update(body)
    return s


# The Refine Tool, which only shows while Use is held on a resource node: it is drawn from the right hip into a ready
# grip (refine_draw), worked against the node in a loop (hold_refine), then put back at the hip (refine_stow). The
# fist holds it like a knife, fingers down, so the teeth face the node and the blade points at it; each stroke shoves
# it forward and down along the blade with the body behind it (the teeth biting), then draws it back lifted clear.
REFINE_LOOP_SECONDS = 0.75
REFINE_DRAW_SECONDS = 0.4
REFINE_STOW_SECONDS = 0.35
REFINE_R = np.array([-0.15, -0.47, 1.1])            # right fist in the ready grip, reaching out in front
REFINE_AXIS = norm(np.array([0.12, -0.9, -0.42]))   # the blade: forward, a little in, down toward the node
REFINE_HIP = np.array([-0.24, -0.06, 0.93])         # right fist at the hip, the blade hanging down and back
REFINE_HIP_AXIS = norm(np.array([-0.05, 0.25, -1.0]))


def refine_arm(grip, axis, side="r", space="world", pole=(-0.6, 0.5, -1.0)):
    """A fist on the Refine Tool's handle at grip, the blade along axis, the fingers (and teeth) turned down."""
    axis = norm(np.asarray(axis, float))
    d = np.array([0.0, -0.25, -1.0]) if abs(axis[2]) < 0.9 else np.array([0.0, -1.0, 0.0])
    d = norm(d - np.dot(d, axis) * axis)
    return arm_ik_from_grip(side, np.asarray(grip, float), d, axis, pole=pole, space=space)


# The fist is placed in model space, so the blade keeps its aim while the body leans into each stroke. The free hand
# braces forward, as if steadying the work (third person).
REFINE_BRACE = {"down": 40.0, "swing": 38.0, "yaw": -14.0, "elbow": 52.0, "wrist": -10.0, "twist": 20.0}


def refine_key(grip, axis, brace=0.0, **body):
    s = upper_base(0.0)
    s.update(relaxed_upper(18))
    s["arm_r"] = refine_arm(grip, axis)
    s["arm_l"] = {k: v + (brace * (6.0 if k in ("swing", "elbow") else 0.0)) for k, v in REFINE_BRACE.items()}
    s["fingers_r"], s["thumb_r"] = 95.0, 45.0
    s["fingers_l"], s["thumb_l"] = 28.0, 18.0
    s.update(body)
    return s


def left_on_bucket(B, point, palm, fingers):
    """Left arm IK (model space) with the palm on the bucket at `point`, facing `palm`, fingers along `fingers`
    (all in the bucket's frame)."""
    R = B[:3, :3]
    p = B[:3, :3] @ np.asarray(point, float) + B[:3, 3]
    n = norm(R @ np.asarray(palm, float))
    d = norm(R @ np.asarray(fingers, float))
    d = norm(d - np.dot(d, n) * n)
    w = np.cross(n, d)
    wrist = p - d * 0.055 - n * 0.022
    return {"pos": tuple(wrist), "d": tuple(d), "w": tuple(w), "pole": (0.7, 0.4, -0.9), "space": "world"}


def blend_arm(a, b, u):
    """Blend two model-space IK arm targets."""
    pa, pb = np.asarray(a["pos"]), np.asarray(b["pos"])
    d = norm(np.asarray(a["d"]) * (1 - u) + np.asarray(b["d"]) * u)
    w = np.asarray(a["w"]) * (1 - u) + np.asarray(b["w"]) * u
    w = norm(w - np.dot(w, d) * d)
    pole = norm(np.asarray(a["pole"]) * (1 - u) + np.asarray(b["pole"]) * u)
    return {"pos": tuple(pa + (pb - pa) * u), "d": tuple(d), "w": tuple(w), "pole": tuple(pole), "space": "world"}


def eased(keys, t):
    """A value eased between (time, value) keys; values are numbers or vectors."""
    if t <= keys[0][0]:
        return np.asarray(keys[0][1], float)
    for (ta, a), (tb, b) in zip(keys, keys[1:]):
        if t <= tb:
            u = sstep(0.0, 1.0, (t - ta) / max(tb - ta, 1e-9))
            return np.asarray(a, float) + (np.asarray(b, float) - np.asarray(a, float)) * u
    return np.asarray(keys[-1][1], float)


# The left hand on the full bucket: flat on its left side under the top band, fingers down.
LEFT_RIM = ((-0.148, -0.045, 0.2), (1.0, 0.25, 0.0), (0.0, 0.25, -1.0))
# ... and low on the side under the front, cupping the bottom edge to heave it.
LEFT_BASE = ((-0.115, -0.04, 0.012), (0.55, 0.15, 0.82), (0.25, 0.95, 0.0))


def bucket_clip(clip, keys, t, left=None, template=None):
    """A bucket clip at time t: the right arm and torso between `keys`, the left hand on the bucket by `left`, a list
    of (time, weight, contact) where contact is (point, palm, fingers) in the bucket's frame (weight 0: relaxed)."""
    s = oneshot(keys, t, template) if len(keys) > 1 else keys[0][1]
    s = {k: (dict(v) if isinstance(v, dict) else v) for k, v in s.items()}
    if not left:
        return s
    weight = float(eased([(k[0], k[1]) for k in left], t))
    if weight <= 1e-4:
        return s
    pose = build_pose(s)
    B = bucket_world(pose, clip, t)
    point = eased([(k[0], k[2][0]) for k in left], t)
    palm = eased([(k[0], k[2][1]) for k in left], t)
    fingers = eased([(k[0], k[2][2]) for k in left], t)
    target = left_on_bucket(B, point, palm, fingers)
    d0, w0, _ = hand_frame("l")
    W = pose.W["hand_l"]
    relaxed = {"pos": tuple(pose.P["hand_l"]), "d": tuple(W @ d0), "w": tuple(W @ w0), "pole": (0.5, 0.6, -1.0),
               "space": "world"}
    s["arm_l"] = blend_arm(relaxed, target, weight)
    s["fingers_l"] = 18.0 + 52.0 * weight
    s["thumb_l"] = 10.0 + 30.0 * weight
    return s


# Third person. Empty: held in front by the back of the rim, elbow bent, the bucket in front of the right hip.
B_EMPTY_FIST = np.array([-0.14, -0.48, 0.98])
B_EMPTY = ((0.0, 0.1, -1.0), (1.0, 0.0, 0.0))
# Full: carried by the handle in front of the belly, palm towards the body, the left hand on the rim.
B_FULL_FIST = np.array([-0.02, -0.36, 1.1])
B_FULL = ((0.0, 0.0, -1.0), (1.0, 0.0, 0.0))
B_FULL_POLE = (-0.7, 0.3, -1.0)


def clip_hold_bucket(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = bucket_key(B_EMPTY_FIST + np.array([0.0, 0.0, 0.004 * math.sin(ph)]), *B_EMPTY)
    base = upper_base(t, T)
    for b in ("spine", "chest", "neck", "head", "shoulder_l", "shoulder_r"):
        s[b] = base[b]
    relaxed_left(s, ph)
    return s


def full_hold_key(t=0.0, T=2.0, fist=B_FULL_FIST, space="chest", **body):
    ph = 2 * math.pi * t / T
    s = bucket_key(fist + np.array([0.0, 0.0, 0.004 * math.sin(ph)]), *B_FULL, space=space, pole=B_FULL_POLE, **body)
    if "spine" not in body:
        base = upper_base(t, T, 0.8)
        for b in ("spine", "chest", "neck", "head", "shoulder_l", "shoulder_r"):
            s[b] = base[b]
    return s


def clip_hold_bucket_full(t, T=2.0):
    return bucket_clip("hold_bucket_full", [(0.0, full_hold_key(t, T))], t, left=[(0.0, 1.0, LEFT_RIM)])


def as_world(spec):
    """A key with its right arm turned into a model-space target where that arm actually is: for starting and
    ending a model-space clip on a chest-space hold pose."""
    pose = build_pose(spec)
    W = pose.W["hand_r"]
    d0, w0, _ = hand_frame("r")
    out = {k: (dict(v) if isinstance(v, dict) else v) for k, v in spec.items()}
    out["arm_r"] = {"pos": tuple(pose.P["hand_r"]), "d": tuple(W @ d0), "w": tuple(W @ w0),
                    "pole": tuple(spec["arm_r"]["pole"]) if "pole" in spec["arm_r"] else BUCKET_R_POLE, "space": "world"}
    return out


def lean(sp, ch=None, nk=None, hd=None, twist=0.0):
    ch = sp * 0.6 if ch is None else ch
    nk = -sp * 0.55 if nk is None else nk
    hd = -sp * 0.4 if hd is None else hd
    return dict(spine=(sp, 0, twist * 0.5), chest=(ch, 0, twist * 0.5), neck=(nk, 0, -twist * 0.3), head=(hd, 0, -twist * 0.2))


def wkey(fist, fingers, thumb, pole=BUCKET_R_POLE, curl=95.0, **body):
    return bucket_key(fist, fingers, thumb, space="world", pole=pole, curl=curl, **body)


# Fill: a little swing back, then bow and swing the bucket forward and down mouth first, scoop it up as it fills,
# lift it, and take it by the handle as it comes up (the handle swings up into the hand); the left hand steadies it.
def bucket_fill_keys():
    return [
        (0.0, as_world(clip_hold_bucket(0.0))),
        (0.16, wkey((-0.15, -0.42, 1.02), (0.0, -0.2, -1.0), (1.0, 0.0, 0.0), **lean(4))),
        (0.34, wkey((-0.14, -0.6, 0.86), (0.0, -0.6, -0.8), (1.0, 0.0, 0.0), **lean(34))),
        (0.46, wkey((-0.13, -0.66, 0.84), (0.0, -0.97, -0.25), (1.0, 0.0, 0.0), **lean(42))),
        (0.6, wkey((-0.14, -0.52, 0.9), (0.0, -0.1, -1.0), (1.0, 0.0, 0.0), **lean(28))),
        (0.8, wkey((-0.08, -0.42, 1.08), (0.0, 0.0, -1.0), (1.0, 0.0, 0.0), pole=B_FULL_POLE, **lean(8))),
        (1.1, as_world(full_hold_key())),
    ]


BUCKET_FILL_LEFT = [(0.0, 0.0, None), (0.74, 0.0, None), (0.95, 1.0, None), (1.1, 1.0, None)]


def clip_bucket_fill(t):
    keys = bucket_fill_keys()
    return bucket_clip("bucket_fill", keys, t, left=with_contact(BUCKET_FILL_LEFT, LEFT_RIM), template=keys[0][1])


def with_contact(keys, contact):
    return [(k[0], k[1], k[2] if k[2] is not None else contact) for k in keys]


# Throw: drop the bucket back between the knees, heave it forward and up as the left hand lifts its bottom, so it
# swings over on its hinges and the water flies out ahead, then let it fall back and take it by the rim again.
def bucket_throw_keys():
    return [
        (0.0, as_world(full_hold_key())),
        (0.2, wkey((-0.02, -0.28, 0.92), (0.0, 0.3, -1.0), (1.0, 0.0, 0.0), pole=B_FULL_POLE, **lean(18))),
        (0.32, wkey((0.0, -0.44, 1.12), (0.0, -0.5, -0.85), (1.0, 0.0, 0.0), pole=B_FULL_POLE, **lean(2))),
        (0.42, wkey((0.0, -0.48, 1.28), (0.0, -0.8, -0.6), (1.0, 0.0, 0.0), pole=B_FULL_POLE, **lean(-6))),
        (0.54, wkey((-0.04, -0.48, 1.22), (0.0, -0.55, -0.8), (1.0, 0.0, 0.0), pole=B_FULL_POLE, **lean(-2))),
        (0.7, wkey((-0.12, -0.46, 1.06), (0.0, -0.1, -1.0), (1.0, 0.0, 0.0), pole=BUCKET_R_POLE, **lean(2))),
        (0.9, as_world(clip_hold_bucket(0.0))),
    ]


BUCKET_THROW_LEFT = [(0.0, 1.0, LEFT_RIM), (0.16, 1.0, LEFT_BASE), (0.44, 1.0, LEFT_BASE), (0.56, 0.0, LEFT_BASE),
                     (0.9, 0.0, LEFT_BASE)]


def clip_bucket_throw(t):
    keys = bucket_throw_keys()
    return bucket_clip("bucket_throw", keys, t, left=BUCKET_THROW_LEFT, template=keys[0][1])


# Place: bow and set the bucket down in front, let go, and straighten up with the hand empty (the bucket stays there).
def bucket_place_keys(full):
    hold = as_world(full_hold_key() if full else clip_hold_bucket(0.0))
    grip = B_FULL if full else B_EMPTY
    pole = B_FULL_POLE if full else BUCKET_R_POLE
    low = np.array([-0.06, -0.6, 0.98]) if full else np.array([-0.14, -0.6, 0.8])
    down = wkey(low, *grip, pole=pole, **lean(44))
    let_go = wkey(low + np.array([0.0, 0.05, 0.07]), *grip, pole=pole, curl=20.0, **lean(40))
    relaxed = as_world(relaxed_ik_upper())
    relaxed["arm_l"] = hold["arm_l"]
    return [(0.0, hold), (0.42, down), (0.52, let_go), (0.8, relaxed)]


def clip_bucket_place(t):
    keys = bucket_place_keys(False)
    return bucket_clip("bucket_place", keys, t, template=keys[0][1])


PLACE_FULL_LEFT = [(0.0, 1.0, LEFT_RIM), (0.42, 1.0, LEFT_RIM), (0.56, 0.0, LEFT_RIM), (0.8, 0.0, LEFT_RIM)]


def clip_bucket_place_full(t):
    keys = bucket_place_keys(True)
    return bucket_clip("bucket_place_full", keys, t, left=PLACE_FULL_LEFT, template=keys[0][1])


# First person: the same clips posed for the camera (FP_EYE, FP_LOOK), the torso stooped as for the Sledgehammer so
# the elbows stay out of the view. The empty bucket rides at the lower right with its rim and inside in view; the full
# one is held up in front of the right half of the view so the water shows.
FPB_EMPTY_FIST = np.array([-0.28, -0.46, 1.1])
FPB_EMPTY = ((0.0, -0.35, -1.0), (1.0, 0.0, 0.0))
FPB_FULL_FIST = np.array([-0.24, -0.48, 1.16])
FPB_FULL = ((0.0, 0.2, -1.0), (1.0, 0.0, 0.0))
FPB_POLE = (-0.5, 0.5, -1.0)


def fp_key(fist, fingers, thumb, pole=FPB_POLE, curl=95.0):
    s = bucket_key(fist, fingers, thumb, space="world", pole=pole, curl=curl, **{k: v for k, v in FP_BODY.items() if k != "space"})
    return s


# Where the free left fist hangs in first person: by the hip, below the view (the bent first-person spine would swing
# a relaxed arm up in front of the camera).
FPB_LEFT_FIST = (0.24, -0.1, 0.86)


def clip_hold_bucket_fp(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = fp_key(FPB_EMPTY_FIST + np.array([0.0, 0.0, 0.005 * math.sin(ph)]), *FPB_EMPTY)
    s["arm_l"] = arm_ik_from_grip("l", np.array(FPB_LEFT_FIST), (0.0, 0.0, -1.0), (0.0, -1.0, 0.0), space="world")
    base = upper_base(t, T, 0.5)
    s["shoulder_l"], s["shoulder_r"] = base["shoulder_l"], base["shoulder_r"]
    return s


def clip_hold_bucket_full_fp(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = fp_key(FPB_FULL_FIST + np.array([0.0, 0.0, 0.005 * math.sin(ph)]), *FPB_FULL)
    s["arm_l"] = clip_hold_bucket_fp(0.0)["arm_l"]
    return s


def clip_bucket_fill_fp(t):
    keys = [
        (0.0, clip_hold_bucket_fp(0.0)),
        (0.16, fp_key(np.array([-0.26, -0.41, 1.17]), (0.0, -0.2, -1.0), (1.0, 0.0, 0.0))),
        (0.34, fp_key(np.array([-0.22, -0.57, 0.90]), (0.0, -0.6, -0.8), (1.0, 0.0, 0.0))),
        (0.46, fp_key(np.array([-0.21, -0.61, 0.84]), (0.0, -0.97, -0.25), (1.0, 0.0, 0.0))),
        (0.6, fp_key(np.array([-0.22, -0.51, 1.00]), (0.0, -0.1, -1.0), (1.0, 0.0, 0.0))),
        (0.8, fp_key(np.array([-0.19, -0.47, 1.16]), (0.0, 0.1, -1.0), (1.0, 0.0, 0.0))),
        (1.1, clip_hold_bucket_full_fp(0.0)),
    ]
    keys[-1] = (1.1, {k: v for k, v in keys[-1][1].items()})
    keys[-1][1]["arm_l"] = keys[0][1]["arm_l"]
    return bucket_clip("bucket_fill", keys, t, left=with_contact(BUCKET_FILL_LEFT, LEFT_RIM), template=keys[0][1])


def clip_bucket_throw_fp(t):
    hold = clip_hold_bucket_full_fp(0.0)
    hold["arm_l"] = clip_hold_bucket_fp(0.0)["arm_l"]
    keys = [
        (0.0, hold),
        (0.2, fp_key(np.array([-0.14, -0.41, 1.00]), (0.0, 0.35, -1.0), (1.0, 0.0, 0.0))),
        (0.32, fp_key(np.array([-0.12, -0.51, 1.20]), (0.0, -0.45, -0.9), (1.0, 0.0, 0.0))),
        (0.42, fp_key(np.array([-0.12, -0.55, 1.34]), (0.0, -0.8, -0.6), (1.0, 0.0, 0.0))),
        (0.54, fp_key(np.array([-0.14, -0.53, 1.28]), (0.0, -0.6, -0.8), (1.0, 0.0, 0.0))),
        (0.7, fp_key(np.array([-0.23, -0.47, 1.16]), (0.0, 0.1, -1.0), (1.0, 0.0, 0.0))),
        (0.9, clip_hold_bucket_fp(0.0)),
    ]
    return bucket_clip("bucket_throw", keys, t, left=BUCKET_THROW_LEFT, template=keys[0][1])


def fp_place_keys(full):
    hold = clip_hold_bucket_full_fp(0.0) if full else clip_hold_bucket_fp(0.0)
    hold = dict(hold)
    hold["arm_l"] = clip_hold_bucket_fp(0.0)["arm_l"]
    grip = FPB_FULL if full else FPB_EMPTY
    low = np.array([-0.18, -0.56, 0.9])
    down = fp_key(low, *grip)
    let_go = fp_key(low + np.array([0.0, 0.04, 0.05]), *grip, curl=20.0)
    away = fp_key(np.array([-0.32, -0.15, 0.76]), (0.0, -0.3, -1.0), (1.0, 0.0, 0.0), curl=30.0)
    return [(0.0, hold), (0.42, down), (0.52, let_go), (0.8, away)]


def clip_bucket_place_fp(t):
    return bucket_clip("bucket_place", fp_place_keys(False), t)


def clip_bucket_place_full_fp(t):
    return bucket_clip("bucket_place_full", fp_place_keys(True), t, left=PLACE_FULL_LEFT)


BUCKET_CLIPS = {
    "hold_bucket": (clip_hold_bucket, 2.0, True, True),
    "hold_bucket_full": (clip_hold_bucket_full, 2.0, True, True),
    "bucket_fill": (clip_bucket_fill, 1.1, False, True),
    "bucket_throw": (clip_bucket_throw, 0.9, False, True),
    "bucket_place": (clip_bucket_place, 0.8, False, True),
    "bucket_place_full": (clip_bucket_place_full, 0.8, False, True),
    "hold_bucket_fp": (clip_hold_bucket_fp, 2.0, True, True),
    "hold_bucket_full_fp": (clip_hold_bucket_full_fp, 2.0, True, True),
    "bucket_fill_fp": (clip_bucket_fill_fp, 1.1, False, True),
    "bucket_throw_fp": (clip_bucket_throw_fp, 0.9, False, True),
    "bucket_place_fp": (clip_bucket_place_fp, 0.8, False, True),
    "bucket_place_full_fp": (clip_bucket_place_full_fp, 0.8, False, True),
}


def refine_stroke(t, T, ready, axis, key, reach=0.065):
    """One stroke a loop: a shove forward and down along the blade (the first 45%) and a lifted draw back."""
    ph = (t / T) % 1.0
    down = norm(np.array([0.0, 0.0, -1.0]) - np.dot((0.0, 0.0, -1.0), axis) * axis)
    if ph < 0.45:
        s = sstep(0.0, 0.45, ph)
        along, lift = -reach + 2 * reach * s, -0.012 * math.sin(math.pi * s)
    else:
        s = sstep(0.45, 1.0, ph)
        along, lift = reach - 2 * reach * s, 0.022 * math.sin(math.pi * s)
    grip = np.asarray(ready) + axis * along - down * lift
    # the blade dips as it bites and rises on the way back
    tilt = norm(axis + down * (0.12 * along / reach))
    push = along / reach
    return key(grip, tilt, push, chest=(12.0 + 4.0 * push, 0.0, 5.0 * push), spine=(7.0 + 3.0 * push, 0.0, 2.5 * push),
               neck=(-6.0, 0.0, -2.0), head=(-5.0, 0.0, -2.0), shoulder_r=(0.0, 0.0, 3.0 * push))


def clip_hold_refine(t):
    return refine_stroke(t, REFINE_LOOP_SECONDS, REFINE_R, REFINE_AXIS, refine_key)


def refine_hip_key():
    return refine_key(REFINE_HIP, REFINE_HIP_AXIS, chest=(0.0, 0.0, 0.0), spine=(0.0, 0.0, 0.0), neck=(0.0, 0.0, 0.0),
                      head=(0.0, 0.0, 0.0), shoulder_r=(0.0, 0.0, 0.0),
                      arm_l=dict(RELAXED_ARM), fingers_l=18.0, thumb_l=10.0)


def clip_refine_draw(t):
    ready = clip_hold_refine(0.0)
    hip = refine_hip_key()
    mid = refine_key((REFINE_HIP + REFINE_R) / 2 + np.array([-0.04, 0.0, 0.06]),
                     norm(REFINE_HIP_AXIS + REFINE_AXIS * 1.6 + np.array([-0.3, 0.0, 0.0])),
                     chest=(4.0, 0.0, -3.0), spine=(2.0, 0.0, -2.0), neck=(-3.0, 0.0, 0.0), head=(-3.0, 0.0, 0.0),
                     shoulder_r=(0.0, 0.0, 0.0))
    return oneshot([(0.0, hip), (0.16, mid), (REFINE_DRAW_SECONDS, ready)], t, template=ready)


def clip_refine_stow(t):
    ready = clip_hold_refine(0.0)
    hip = refine_hip_key()
    lift = refine_key(REFINE_R + np.array([-0.05, 0.06, 0.05]), norm(REFINE_AXIS + np.array([-0.3, 0.6, 0.6])),
                      chest=(4.0, 0.0, -2.0), spine=(2.0, 0.0, -1.0), neck=(-3.0, 0.0, 0.0), head=(-3.0, 0.0, 0.0),
                      shoulder_r=(0.0, 0.0, 0.0))
    return oneshot([(0.0, ready), (0.14, lift), (REFINE_STOW_SECONDS, hip)], t, template=ready)


# First person: the stooped body and folded arm of the Sledgehammer's first-person clips, so the elbow stays out to the
# lower right of the view and only the fist and the tool come into it. The blade reaches in toward the middle of the
# view, its teeth on whatever the crosshair is on. Fists are placed in model space (see first_person_offset).
REFINE_R_FP = np.array([-0.17, -0.389, 1.086])
REFINE_AXIS_FP = norm(np.array([0.458, -0.761, 0.459]))
REFINE_HIP_FP = np.array([-0.3, -0.26, 0.776])       # below the bottom right of the view
REFINE_HIP_AXIS_FP = norm(np.array([-0.111, -0.581, -0.806]))


def refine_key_fp(grip, axis, push=0.0, **body):
    s = upper_base(0.0, amp=0.5)
    s.update(relaxed_upper(18))
    s.update({k: v for k, v in FP_BODY.items() if k != "space"})
    s["arm_r"] = refine_arm(grip, axis, pole=FP_POLES[0])
    # the free hand hangs down to the left, out of the view
    s["arm_l"] = dict(RELAXED_ARM, swing=15.0, down=45.0, elbow=40.0)
    s["fingers_r"], s["thumb_r"] = 95.0, 45.0
    for k in ("chest", "spine", "neck", "head", "shoulder_r"):
        if k in body:
            base = s.get(k, (0.0, 0.0, 0.0))
            s[k] = tuple(a + b for a, b in zip(base, body[k]))
    return s


def clip_hold_refine_fp(t):
    def key(grip, axis, push, chest, spine, neck, head, shoulder_r):
        return refine_key_fp(grip, axis, push, chest=(2.0 * push, 0.0, 2.0 * push), shoulder_r=shoulder_r)
    return refine_stroke(t, REFINE_LOOP_SECONDS, REFINE_R_FP, REFINE_AXIS_FP, key, reach=0.045)


def clip_refine_draw_fp(t):
    ready = clip_hold_refine_fp(0.0)
    hip = refine_key_fp(REFINE_HIP_FP, REFINE_HIP_AXIS_FP)
    mid = refine_key_fp(np.array([-0.24, -0.374, 0.938]), norm(np.array([0.286, -0.948, 0.138])))
    return oneshot([(0.0, hip), (0.18, mid), (REFINE_DRAW_SECONDS, ready)], t, template=ready)


def clip_refine_stow_fp(t):
    ready = clip_hold_refine_fp(0.0)
    hip = refine_key_fp(REFINE_HIP_FP, REFINE_HIP_AXIS_FP)
    lift = refine_key_fp(np.array([-0.19, -0.362, 1.111]), norm(np.array([0.32, -0.63, 0.707])))
    return oneshot([(0.0, ready), (0.12, lift), (REFINE_STOW_SECONDS, hip)], t, template=ready)


# The Connect Tool, held like a pistol: drawn from a holster on the right hip into a forward point at chest height
# (connect_draw, hold_connect), raised and aimed more deliberately while it is over a node or dragging a link
# (hold_connect_aim, the guard flag), a flick of the wrist as a link is made or taken away (connect_click) and a roll
# of it as a bearing is reversed (connect_reverse), then put back on the hip (connect_stow). Both loops sway and
# breathe; the aim sways less. The fist is placed in model space with the probe along a direction and the top of the
# housing toward an up hint.
CONNECT_DRAW_SECONDS = 0.5
CONNECT_STOW_SECONDS = 0.45
CONNECT_CLICK_SECONDS = 0.25
CONNECT_REVERSE_SECONDS = 0.3
CONNECT_R = np.array([-0.16, -0.31, 1.08])           # fist in the hold: chest height, the probe forward and a little down
CONNECT_PROBE = norm(np.array([0.08, -1.0, -0.22]))
CONNECT_AIM_R = np.array([-0.11, -0.49, 1.32])        # raised: the arm out, the probe level at what the player looks at
CONNECT_AIM_PROBE = norm(np.array([0.1, -1.0, 0.0]))
CONNECT_HIP = np.array([-0.235, -0.04, 0.95])        # holstered on the right hip, the probe down, the grip back
CONNECT_HIP_PROBE = norm(np.array([-0.06, 0.12, -1.0]))
CONNECT_HIP_UP = np.array([0.15, 1.0, 0.0])


def connect_arm(grip, probe, up=(0.0, 0.0, 1.0), roll=0.0, space="world", pole=(-0.6, 0.5, -1.0)):
    """A fist on the Connect Tool's grip at grip, its probe along probe and the top of its housing toward up (rolled
    by roll degrees about the probe)."""
    probe = norm(np.asarray(probe, float))
    top = np.asarray(up, float)
    top = norm(top - np.dot(top, probe) * probe)
    if roll:
        r = math.radians(roll)
        top = top * math.cos(r) + np.cross(probe, top) * math.sin(r)
    rk = math.radians(CONNECT_RAKE)
    d = probe * math.cos(rk) - top * math.sin(rk)     # the fingers (grip frame x)
    w = probe * math.sin(rk) + top * math.cos(rk)     # the handle (grip frame y)
    return arm_ik_from_grip("r", np.asarray(grip, float), d, w, pole=pole, space=space)


def connect_key(grip, probe, up=(0.0, 0.0, 1.0), roll=0.0, t=0.0, amp=1.0, **body):
    s = upper_base(t, 2.0, amp)
    relaxed_left(s, 2 * math.pi * t / 2.0)
    s["arm_r"] = connect_arm(grip, probe, up, roll)
    s["fingers_r"], s["thumb_r"] = 92.0, 40.0
    s.update(body)
    return s


def connect_sway(t, T, amp):
    """A slow figure of eight of the fist and the probe, so the held tool is never quite still."""
    ph = 2 * math.pi * t / T
    return np.array([0.004 * math.sin(ph), 0.002 * math.sin(2 * ph), 0.005 * math.sin(ph + 0.6)]) * amp, \
        np.array([0.015 * math.sin(ph + 0.4), 0.0, 0.02 * math.sin(2 * ph + 1.0)]) * amp


def clip_hold_connect(t, T=2.0):
    dp, da = connect_sway(t, T, 1.0)
    return connect_key(CONNECT_R + dp, CONNECT_PROBE + da, t=t, chest=(2.0, 0.0, -2.0))


AIM_BODY = dict(chest=(-2.0, 0.0, -6.0), spine=(-1.0, 0.0, -3.0), shoulder_r=(0.0, 0.0, 4.0))


def clip_hold_connect_aim(t, T=2.0):
    dp, da = connect_sway(t, T, 0.4)
    return connect_key(CONNECT_AIM_R + dp, CONNECT_AIM_PROBE + da, t=t, amp=0.5, **AIM_BODY)


def connect_hip_key(body=None):
    return connect_key(CONNECT_HIP, CONNECT_HIP_PROBE, CONNECT_HIP_UP, **(body or {}))


def clip_connect_draw(t):
    ready = clip_hold_connect(0.0)
    hip = connect_hip_key()
    # out of the holster: the fist lifts and swings the probe up and round to the front
    lift = connect_key(np.array([-0.24, -0.1, 1.02]), norm(np.array([-0.1, -0.5, -0.86])), (0.1, 0.9, 0.3),
                       chest=(1.0, 0.0, -1.0))
    swing = connect_key(np.array([-0.2, -0.24, 1.08]), norm(np.array([0.0, -0.95, -0.32])), chest=(2.0, 0.0, -2.0))
    return oneshot([(0.0, hip), (0.16, lift), (0.32, swing), (CONNECT_DRAW_SECONDS, ready)], t, template=ready)


def clip_connect_stow(t):
    ready = clip_hold_connect(0.0)
    hip = connect_hip_key()
    lift = connect_key(np.array([-0.23, -0.12, 1.04]), norm(np.array([-0.1, -0.5, -0.86])), (0.1, 0.9, 0.3),
                       chest=(1.0, 0.0, -1.0))
    return oneshot([(0.0, ready), (0.15, lift), (0.32, hip), (CONNECT_STOW_SECONDS, hip)], t, template=ready)


def connect_click_clip(t, aim_key, grip, probe):
    """A flick: the probe jabs forward and dips as the trigger clicks, then settles back."""
    ready = aim_key(grip, probe)
    jab = aim_key(grip + probe * 0.018, norm(probe + np.array([0.0, 0.0, -0.1])))
    return oneshot([(0.0, ready), (0.06, jab), (0.12, jab), (CONNECT_CLICK_SECONDS, ready)], t, template=ready)


def connect_reverse_clip(t, aim_key, grip, probe):
    """A roll of the wrist one way and back, as if turning the bearing round."""
    ready = aim_key(grip, probe)
    twist = aim_key(grip, probe, roll=-28.0)
    back = aim_key(grip, probe, roll=8.0)
    return oneshot([(0.0, ready), (0.09, twist), (0.2, back), (CONNECT_REVERSE_SECONDS, ready)], t, template=ready)


def clip_connect_click(t):
    return connect_click_clip(t, lambda g, p, roll=0.0: connect_key(g, p, roll=roll, amp=0.5, **AIM_BODY), CONNECT_AIM_R,
                              CONNECT_AIM_PROBE)


def clip_connect_reverse(t):
    return connect_reverse_clip(t, lambda g, p, roll=0.0: connect_key(g, p, roll=roll, amp=0.5, **AIM_BODY),
                                CONNECT_AIM_R, CONNECT_AIM_PROBE)


# First person: the stooped body of the other first-person clips, the elbow out of the bottom right of the view, the
# tool at the lower right with its probe toward the middle of the view (raised: further in and level with the
# crosshair), and the holster below the bottom right.
CONNECT_R_FP = np.array([-0.135, -0.4, 1.13])
CONNECT_PROBE_FP = norm(np.array([0.17, -0.98, 0.1]))
CONNECT_AIM_R_FP = np.array([-0.11, -0.44, 1.155])
CONNECT_AIM_PROBE_FP = norm(np.array([0.1, -0.98, 0.16]))
CONNECT_HIP_FP = np.array([-0.3, -0.26, 0.78])
CONNECT_HIP_PROBE_FP = norm(np.array([-0.1, -0.3, -0.95]))


def connect_key_fp(grip, probe, up=(0.0, 0.0, 1.0), roll=0.0, t=0.0):
    s = upper_base(t, 2.0, amp=0.5)
    s.update(relaxed_upper(18))
    s.update({k: v for k, v in FP_BODY.items() if k != "space"})
    s["arm_r"] = connect_arm(grip, probe, up, roll, pole=FP_POLES[0])
    s["arm_l"] = dict(RELAXED_ARM, swing=15.0, down=45.0, elbow=40.0)
    s["fingers_r"], s["thumb_r"] = 92.0, 40.0
    return s


def clip_hold_connect_fp(t, T=2.0):
    dp, da = connect_sway(t, T, 0.6)
    return connect_key_fp(CONNECT_R_FP + dp, CONNECT_PROBE_FP + da, t=t)


def clip_hold_connect_aim_fp(t, T=2.0):
    dp, da = connect_sway(t, T, 0.25)
    return connect_key_fp(CONNECT_AIM_R_FP + dp, CONNECT_AIM_PROBE_FP + da, t=t)


def clip_connect_draw_fp(t):
    ready = clip_hold_connect_fp(0.0)
    hip = connect_key_fp(CONNECT_HIP_FP, CONNECT_HIP_PROBE_FP, (0.1, 1.0, 0.0))
    mid = connect_key_fp(np.array([-0.24, -0.33, 0.95]), norm(np.array([0.05, -0.7, -0.7])))
    return oneshot([(0.0, hip), (0.22, mid), (CONNECT_DRAW_SECONDS, ready)], t, template=ready)


def clip_connect_stow_fp(t):
    ready = clip_hold_connect_fp(0.0)
    hip = connect_key_fp(CONNECT_HIP_FP, CONNECT_HIP_PROBE_FP, (0.1, 1.0, 0.0))
    mid = connect_key_fp(np.array([-0.24, -0.33, 0.95]), norm(np.array([0.05, -0.7, -0.7])))
    return oneshot([(0.0, ready), (0.18, mid), (0.36, hip), (CONNECT_STOW_SECONDS, hip)], t, template=ready)


def clip_connect_click_fp(t):
    return connect_click_clip(t, lambda g, p, roll=0.0: connect_key_fp(g, p, roll=roll), CONNECT_AIM_R_FP,
                              CONNECT_AIM_PROBE_FP)


def clip_connect_reverse_fp(t):
    return connect_reverse_clip(t, lambda g, p, roll=0.0: connect_key_fp(g, p, roll=roll), CONNECT_AIM_R_FP,
                                CONNECT_AIM_PROBE_FP)


# name: (function, duration seconds, loops, upper_body_only)
CLIPS = {
    "idle": (clip_idle, 3.0, True, False),
    "walk": (clip_walk, WALK_T, True, False),
    "jog": (clip_jog, 0.72, True, False),
    "run": (clip_run, 0.6, True, False),
    "crouch_idle": (clip_crouch_idle, 2.0, True, False),
    "crouch_walk": (clip_crouch_walk, 1.2, True, False),
    "jump": (clip_jump, 0.3, False, False),
    "fall": (clip_fall, 1.0, True, False),
    "land": (clip_land, 0.35, False, False),
    "swim_idle": (clip_swim_idle, 2.0, True, False),
    "swim": (clip_swim, 1.2, True, False),
    "sit": (clip_sit, 2.0, True, False),
    "hold_tool": (clip_hold_tool, 2.0, True, True),
    "hold_heavy": (clip_hold_heavy, 2.0, True, True),
    "hold_heavy_block": (clip_hold_heavy_block, 2.0, True, True),
    "hold_heavy_fp": (clip_hold_heavy_fp, 2.0, True, True),
    "hold_heavy_block_fp": (clip_hold_heavy_block_fp, 2.0, True, True),
    "hold_gun": (clip_hold_gun, 2.0, True, True),
    "hold_item": (clip_hold_item, 2.0, True, True),
    "sledge_swing": (clip_sledge_swing, SWING_SECONDS, False, True),
    "sledge_swing_side": (clip_sledge_swing_side, SWING_SECONDS, False, True),
    "sledge_swing_back": (clip_sledge_swing_back, SWING_SECONDS, False, True),
    "sledge_swing_fp": (clip_sledge_swing_fp, SWING_SECONDS, False, True),
    "sledge_swing_side_fp": (clip_sledge_swing_side_fp, SWING_SECONDS, False, True),
    "sledge_swing_back_fp": (clip_sledge_swing_back_fp, SWING_SECONDS, False, True),
    "tool_use": (clip_tool_use, 0.3, False, True),
    "weld": (clip_weld, 0.5, False, True),
    "paint_spray": (clip_paint_spray, 0.4, False, True),
    "fire_gun": (clip_fire_gun, 0.25, False, True),
    "eat": (clip_eat, 1.0, False, True),
    "throw": (clip_throw, 0.5, False, True),
    "plant": (clip_plant, 0.8, False, True),
    "pour": (clip_pour, 0.8, False, True),
    "interact": (clip_interact, 0.4, False, True),
    "hurt": (clip_hurt, 0.3, False, True),
    **BUCKET_CLIPS,
    "hold_refine": (clip_hold_refine, REFINE_LOOP_SECONDS, True, True),
    "refine_draw": (clip_refine_draw, REFINE_DRAW_SECONDS, False, True),
    "refine_stow": (clip_refine_stow, REFINE_STOW_SECONDS, False, True),
    "hold_refine_fp": (clip_hold_refine_fp, REFINE_LOOP_SECONDS, True, True),
    "refine_draw_fp": (clip_refine_draw_fp, REFINE_DRAW_SECONDS, False, True),
    "refine_stow_fp": (clip_refine_stow_fp, REFINE_STOW_SECONDS, False, True),
    "hold_connect": (clip_hold_connect, 2.0, True, True),
    "hold_connect_aim": (clip_hold_connect_aim, 2.0, True, True),
    "connect_draw": (clip_connect_draw, CONNECT_DRAW_SECONDS, False, True),
    "connect_stow": (clip_connect_stow, CONNECT_STOW_SECONDS, False, True),
    "connect_click": (clip_connect_click, CONNECT_CLICK_SECONDS, False, True),
    "connect_reverse": (clip_connect_reverse, CONNECT_REVERSE_SECONDS, False, True),
    "hold_connect_fp": (clip_hold_connect_fp, 2.0, True, True),
    "hold_connect_aim_fp": (clip_hold_connect_aim_fp, 2.0, True, True),
    "connect_draw_fp": (clip_connect_draw_fp, CONNECT_DRAW_SECONDS, False, True),
    "connect_stow_fp": (clip_connect_stow_fp, CONNECT_STOW_SECONDS, False, True),
    "connect_click_fp": (clip_connect_click_fp, CONNECT_CLICK_SECONDS, False, True),
    "connect_reverse_fp": (clip_connect_reverse_fp, CONNECT_REVERSE_SECONDS, False, True),
}

# the prop each clip shows in the right hand (verification renders and the game's defaults)
CLIP_PROP = {"hold_tool": "lift_remote", "tool_use": "lift_remote", "weld": "weld_tool",
             "paint_spray": "paint_tool", "hold_heavy": "sledgehammer", "sledge_swing": "sledgehammer",
             "hold_heavy_block": "sledgehammer", "hold_heavy_fp": "sledgehammer", "hold_heavy_block_fp": "sledgehammer",
             "sledge_swing_fp": "sledgehammer", "sledge_swing_side": "sledgehammer", "sledge_swing_back": "sledgehammer",
             "sledge_swing_side_fp": "sledgehammer", "sledge_swing_back_fp": "sledgehammer",
             "hold_gun": "potato_blaster", "fire_gun": "potato_blaster", "pour": "water_bucket",
             "hold_refine": "refine_tool", "refine_draw": "refine_tool", "refine_stow": "refine_tool",
             "hold_refine_fp": "refine_tool", "refine_draw_fp": "refine_tool", "refine_stow_fp": "refine_tool",
             **{name: "water_bucket" for name in BUCKET_CLIPS},
             **{c: "connect_tool" for c in ("hold_connect", "hold_connect_aim", "connect_draw", "connect_stow", "connect_click",
                                            "connect_reverse", "hold_connect_fp", "hold_connect_aim_fp", "connect_draw_fp",
                                            "connect_stow_fp", "connect_click_fp", "connect_reverse_fp")}}


def clip_times(duration):
    n = int(math.floor(duration * FPS + 1e-6))
    times = [k / FPS for k in range(n + 1)]
    if duration - times[-1] > 1e-6:
        times.append(duration)
    return times


def keyed_bones(name):
    return UPPER_BODY_BONES if CLIPS[name][3] else BONE_NAMES


def apply_pose(arm_obj, pose, bones=None):
    """Write a solved pose into the armature's pose bones (rest-relative basis)."""
    for b in bones or BONE_NAMES:
        pb = arm_obj.pose.bones[b]
        R = np.asarray(arm_obj.data.bones[b].matrix_local.to_3x3())
        basis = R.T @ pose.Q[b] @ R
        pb.rotation_quaternion = Matrix([list(r) for r in basis]).to_quaternion()
        if b == "hips":
            pb.location = Vector(R.T @ np.array([0.0, 0.0, pose.hips_z]))
        else:
            pb.location = (0, 0, 0)


def bake_actions(arm_obj):
    """One action per clip, keyed every 1/30 s (plus the exact end time)."""
    arm_obj.animation_data_create()
    for name, (fn, dur, loop, upper) in CLIPS.items():
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        arm_obj.animation_data.action = act
        bones = keyed_bones(name)
        prev = {}
        for t in clip_times(dur):
            pose = build_pose(fn(t))
            frame = t * FPS
            for b in bones:
                pb = arm_obj.pose.bones[b]
                R = np.asarray(arm_obj.data.bones[b].matrix_local.to_3x3())
                q = Matrix([list(r) for r in (R.T @ pose.Q[b] @ R)]).to_quaternion()
                if b in prev and prev[b].dot(q) < 0:
                    q.negate()
                prev[b] = q.copy()
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=b)
                if b == "hips":
                    pb.location = Vector(R.T @ np.array([0.0, 0.0, pose.hips_z]))
                    pb.keyframe_insert("location", frame=frame, group=b)
        act.use_frame_range = True
        act.frame_start = 0.0
        act.frame_end = dur * FPS
        act.use_cyclic = loop
    arm_obj.animation_data.action = None
    for pb in arm_obj.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)


# =====================================================================================================
# Hand tool props
# =====================================================================================================
# Each prop is modelled in a grip frame: origin = centre of the right fist, x = finger direction
# (forward when holding), y = thumb direction (the handle axis, "up" when holding), z = x cross y.
# The object is then placed with the rest-pose transform of that frame, so the game can attach it
# to hand_r with the offset (hand_r rest)^-1 * (prop transform).

def grip_frame_matrix(side="r"):
    d, w, n = hand_frame(side)
    z = np.cross(d, w)
    M = Matrix.Identity(4)
    for r in range(3):
        M[r][0], M[r][1], M[r][2], M[r][3] = d[r], w[r], z[r], grip_point(side)[r]
    return M


X, Y, Z = np.eye(3)


def prop_lift_remote():
    p = [soft_box((0.0, 0.03, 0.0), (0.024, 0.075, 0.016), axes=(X, Y, Z), e=6, e2=6, segs=14, rings=8,
                  mat="paint_olive"),
         soft_box((0.0, 0.095, 0.0), (0.026, 0.018, 0.018), axes=(X, Y, Z), e=6, e2=6, segs=14, rings=5,
                  mat="metal_dark"),
         soft_box((0.022, 0.055, 0.0), (0.004, 0.02, 0.011), axes=(X, Y, Z), e=6, e2=6, segs=10, rings=5,
                  mat="screen")]
    for i, (yy, m) in enumerate(((0.025, "orange"), (0.005, "blue"))):
        p.append(cylinder((0.014, yy, 0.0), (0.026, yy, 0.0), 0.006, 10, m, ref=Y))
    p.append(cylinder((-0.01, 0.11, -0.006), (-0.035, 0.24, -0.01), 0.0025, 8, "metal"))
    p.append(ellipsoid((-0.035, 0.243, -0.01), (0.006, 0.006, 0.006), segs=10, rings=6, mat="orange"))
    return p


def prop_weld_tool():
    p = [cylinder((-0.004, -0.06, 0), (0.0, 0.05, 0), 0.017, 12, "rubber", ref=X, bevel=0.004),
         soft_box((0.03, 0.078, 0), (0.07, 0.024, 0.022), axes=(X, Y, Z), e=5, e2=5, segs=14, rings=7,
                  mat="paint_grey"),
         soft_box((0.0, 0.081, 0.0), (0.03, 0.027, 0.025), axes=(X, Y, Z), e=5, e2=5, segs=12, rings=5,
                  mat="blue"),
         cylinder((0.1, 0.078, 0), (0.17, 0.07, 0), 0.012, 12, "metal", ref=Y),
         cylinder((0.17, 0.07, 0), (0.235, 0.05, 0), 0.0085, 10, "orange_dark", ref=Y),
         cylinder((0.235, 0.05, 0), (0.25, 0.045, 0), 0.006, 8, "metal_dark", ref=Y),
         box((0.03, 0.035, 0.0), (0.005, 0.012, 0.007), mat="orange")]
    hose = [np.array([-0.03, 0.06, 0]), np.array([-0.07, 0.03, 0]), np.array([-0.08, -0.05, 0.01]),
            np.array([-0.07, -0.16, 0.02])]
    p.append(tube(hose, 0.008, 8, Z, "rubber"))
    return p


def prop_paint_tool():
    p = [cylinder((-0.004, -0.06, 0), (0.0, 0.05, 0), 0.016, 12, "paint_olive", ref=X, bevel=0.004),
         soft_box((0.03, 0.074, 0), (0.06, 0.02, 0.02), axes=(X, Y, Z), e=5, e2=5, segs=14, rings=7, mat="metal_dark"),
         cylinder((0.09, 0.074, 0), (0.14, 0.074, 0), 0.009, 10, "metal", ref=Y),
         cylinder((0.14, 0.074, 0), (0.155, 0.074, 0), 0.006, 10, "orange", ref=Y),
         cylinder((0.035, 0.094, 0), (0.035, 0.17, 0), 0.032, 16, "blue", ref=X, bevel=0.006),
         cylinder((0.035, 0.17, 0), (0.035, 0.18, 0), 0.02, 12, "metal_dark", ref=X),
         box((0.022, 0.03, 0.0), (0.006, 0.014, 0.007), mat="orange")]
    return p


def prop_sledgehammer():
    hx, hy, hz = SLEDGE_HEAD
    yc = SLEDGE_HEAD_Y
    p = [
        # shaft, end cap with its cross pin, knurled rubber grip and the yellow band above it
        cylinder((0, SLEDGE_BUTT + 0.01, 0), (0, yc - hy + 0.004, 0), SHAFT_R, 16, "steel_shaft", ref=X),
        cylinder((0, SLEDGE_BUTT, 0), (0, SLEDGE_BUTT + 0.032, 0), 0.0245, 18, "gunmetal", ref=X, bevel=0.004),
        cylinder((-0.0255, SLEDGE_BUTT + 0.016, 0), (0.0255, SLEDGE_BUTT + 0.016, 0), 0.0055, 10, "bolt_steel",
                 ref=Z, bevel=0.0012),
        cylinder((0, SLEDGE_BUTT + 0.03, 0), (0, -0.078, 0), 0.0218, 18, "grip_rubber", ref=X, bevel=0.003),
        cylinder((0, SLEDGE_BUTT + 0.03, 0), (0, SLEDGE_BUTT + 0.042, 0), 0.0232, 18, "grip_rubber", ref=X,
                 bevel=0.003),
        cylinder((0, -0.09, 0), (0, -0.078, 0), 0.0228, 18, "grip_rubber", ref=X, bevel=0.003),
        cylinder((0, -0.076, 0), (0, -0.044, 0), 0.0196, 18, "paint_yellow", ref=X, bevel=0.0008),
        # stepped collar where the shaft meets the head
        cylinder((0, yc - hy - 0.075, 0), (0, yc - hy - 0.062, 0), 0.0222, 18, "gunmetal", ref=X, bevel=0.002),
        cylinder((0, yc - hy - 0.045, 0), (0, yc - hy + 0.002, 0), 0.0265, 18, "gunmetal", ref=X, bevel=0.004),
        bevel_box((0, yc - hy - 0.004, 0), (0.042, 0.007, 0.036), 0.003, mat="gunmetal"),
    ]
    # head core: a yellow-painted block, seen between the straps and around the cheek plates
    p.append(bevel_box((0, yc, 0), (0.075, hy - 0.006, hz - 0.007), 0.004, mat="paint_yellow"))
    # striking ends: three stacked plates each, then a face plate
    for sx in (-1, 1):
        x0, x1 = 0.068, hx - 0.011
        xm = sx * (x0 + x1) / 2
        for k, (ya, yb) in enumerate(((-hy, -0.0235), (-0.0215, 0.0215), (0.0235, hy))):
            p.append(bevel_box((xm, yc + (ya + yb) / 2, 0), ((x1 - x0) / 2, (yb - ya) / 2, hz), 0.006, mat="gunmetal"))
        p.append(bevel_box((sx * (hx - 0.0055), yc, 0), (0.0055, hy - 0.009, hz - 0.009), 0.0035, mat="gunmetal"))
        # rivets on the cheeks of the end plates
        for sz in (-1, 1):
            for yy in (-0.046, 0.046):
                p.append(rivet((sx * 0.112, yc + yy, sz * hz), (0, 0, sz)))
    # cheek plates over the core, front and back: two layers with a groove between, yellow showing round them
    for sz in (-1, 1):
        for ya, yb in ((-hy + 0.014, -0.002), (0.002, hy - 0.014)):
            p.append(bevel_box((0, yc + (ya + yb) / 2, sz * (hz - 0.004)), (0.04, (yb - ya) / 2, 0.006), 0.0025,
                               mat="gunmetal"))
    # a top plate leaves a strip of yellow showing on each side
    p.append(bevel_box((0, yc + hy - 0.002, 0), (0.04, 0.005, hz - 0.034), 0.0025, mat="gunmetal"))
    # straps wrapping the head where the ends meet the core, each with four hex bolts
    t = 0.0075
    for sx in (-1, 1):
        xs = sx * 0.068
        w = 0.0125
        for sy in (-1, 1):
            p.append(bevel_box((xs, yc + sy * (hy + t / 2), 0), (w, t / 2, hz + t), 0.0025, mat="gunmetal"))
        for sz in (-1, 1):
            p.append(bevel_box((xs, yc, sz * (hz + t / 2)), (w, hy, t / 2), 0.0025, mat="gunmetal"))
            for yy in (-0.042, 0.042):
                p.extend(hex_bolt((xs, yc + yy, sz * (hz + t)), (0, 0, sz), (0, 1, 0)))
        for sy in (-1, 1):
            p.extend(hex_bolt((xs, yc + sy * (hy + t), 0), (0, sy, 0), (1, 0, 0), r=0.009))
    return p


# The Connect Tool, built to its reference sheet: a pistol-grip probe about 18 cm long and 12 cm tall. A dark brushed
# gunmetal housing 4 cm across with panel lines, a lens in its rear face, an orange ring at the back and a cyan and an
# orange ring at the nose, a chamfered side display on the left (the side a right-handed holder sees), a recessed top
# panel and small scanner fins at the back; a stepped nose cone, a slender silver probe and a hex sensor at its tip;
# under it a raked grip of dark rubber with chevron ridges, finger bumps down the front and an orange hex on the butt.
# It is modelled in a gun frame (u along the probe, v up from the barrel to the housing, w to the holder's right) with
# the fist at the origin and the grip CONNECT_RAKE degrees back from upright, then turned into the grip frame (the
# handle along y). Everything cyan lights up: it is a second node, connect_tool_glow, with an emissive material of its
# own, so the game can pulse it (a drag, a click, a reversed bearing) without touching the atlas.
CONNECT_RAKE = 14.0            # degrees the grip leans back from square to the probe
CONNECT_BODY_V = 0.058         # the housing's axis above the fist (gun frame v)
CONNECT_BODY_R = 0.02          # the housing's radius
CONNECT_TIP_U = 0.161          # the front of the sensor (gun frame u)
CONNECT_GLOW = "#2fd8ff"


def _connect_rot():
    r = math.radians(CONNECT_RAKE)
    return np.array([[math.cos(r), -math.sin(r), 0.0], [math.sin(r), math.cos(r), 0.0], [0.0, 0.0, 1.0]])


def connect_to_grip(p):
    """A gun-frame point (or direction) in the grip frame."""
    return _connect_rot() @ np.asarray(p, float)


def connect_tip():
    """The sensor's front in the grip frame (where a drag's glow and the click pulse come from)."""
    return connect_to_grip((CONNECT_TIP_U, CONNECT_BODY_V, 0.0))


def connect_probe_axis():
    """Which way the probe points, in the grip frame."""
    return connect_to_grip((1.0, 0.0, 0.0))


def prism(outline, w0, w1, origin, ax_p, ax_q, ax_w, mat, smooth=False):
    """A flat-sided plate: the 2D outline (counter-clockwise in p, q) pushed from w0 to w1 along ax_w."""
    o, P, Q, W = (np.asarray(v, float) for v in (origin, ax_p, ax_q, ax_w))
    w0, w1 = min(w0, w1), max(w0, w1)
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(outline, outline[1:] + outline[:1]))
    if (area > 0) != (np.dot(np.cross(P, Q), W) > 0):
        outline = outline[::-1]
    n = len(outline)
    verts = [o + P * x + Q * y + W * w for w in (w0, w1) for x, y in outline]
    # convex outlines: caps as fans (glTF tangents need triangles and quads)
    faces = [(0, k + 1, k) for k in range(1, n - 1)] + [(n, n + k, n + k + 1) for k in range(1, n - 1)]
    faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return Part(verts, faces, mat, smooth)


def chamfer_rect(hx, hy, c):
    return [(hx - c, -hy), (hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c),
            (-hx + c, -hy)]


def hex_outline(r, rot=0.0):
    return [(r * math.cos(rot + k * math.pi / 3), r * math.sin(rot + k * math.pi / 3)) for k in range(6)]


def annulus(center, axis, r0, r1, mat, segs=28, ref=(0, 1, 0), smooth=True):
    """A flat ring facing along axis."""
    t = norm(axis)
    a = norm(np.asarray(ref, float) - np.dot(ref, t) * t)
    b = np.cross(t, a)
    c = np.asarray(center, float)
    th = np.linspace(0, 2 * math.pi, segs, endpoint=False)
    verts = [c + (a * math.cos(x) + b * math.sin(x)) * r for r in (r0, r1) for x in th]
    faces = [(i, (i + 1) % segs, segs + (i + 1) % segs, segs + i) for i in range(segs)]
    return Part(verts, faces, mat, smooth)


def _connect_parts():
    U, V, W = X, Y, Z
    R, H = CONNECT_BODY_R, CONNECT_BODY_V
    on = lambda u, dv=0.0, w=0.0: np.array([u, H + dv, w])
    p = []
    # housing: a rounded rear cap, the main tube and a shoulder down into the nose
    prof = [(-0.026, 0.0138), (-0.0256, 0.0172), (-0.0245, 0.019), (-0.0225, 0.0198), (-0.019, R), (0.071, R),
            (0.0745, 0.0196), (0.0758, 0.0186)]
    p.append(tube([on(u) for u, _ in prof], [r for _, r in prof], 40, V, "tool_gunmetal"))
    # rear face: a bezel round a lens of dark glass
    p.append(annulus(on(-0.0262), -U, 0.0109, 0.0136, "tool_bezel"))
    p.append(ring_band(on(-0.0266), 0.0123, 0.0123, 0.0009, 0.0011, "tool_bezel", 32, axes=(V, W, U), e=2.0))
    p.append(cylinder(on(-0.0255), on(-0.0262), 0.0089, 28, "screen", ref=V))
    # rings and panel lines round the housing
    p.append(cylinder(on(-0.0192), on(-0.0138), R + 0.0011, 40, "accent_orange", ref=V, bevel=0.0005))
    for u in (-0.0115, 0.0105, 0.064):
        p.append(cylinder(on(u - 0.00035), on(u + 0.00035), R + 0.00025, 40, "panel_line", ref=V))
    # nose: a collar behind the cyan ring, the orange ring and a stepped cone down to the probe
    p.append(cylinder(on(0.0752), on(0.0862), 0.017, 32, "tool_gunmetal", ref=V, bevel=0.0008))
    p.append(cylinder(on(0.0797), on(0.0858), 0.0181, 32, "accent_orange", ref=V, bevel=0.0007))
    cone = [(0.0855, 0.0158), (0.0885, 0.0152), (0.089, 0.0128), (0.0925, 0.0118), (0.093, 0.0094), (0.0965, 0.0086),
            (0.097, 0.0062), (0.1005, 0.0056)]
    p.append(tube([on(u) for u, _ in cone], [r for _, r in cone], 32, V, "tool_gunmetal"))
    # the probe, a collar near its end and the hex sensor
    p.append(cylinder(on(0.1), on(0.105), 0.0042, 18, "tool_silver", ref=V, bevel=0.0006))
    p.append(cylinder(on(0.104), on(0.1505), 0.0025, 14, "tool_silver", ref=V))
    p.append(cylinder(on(0.1405), on(0.1445), 0.0034, 14, "tool_bezel", ref=V, bevel=0.0005))
    hexc = on(0.1545)
    p.append(prism(hex_outline(0.0068), -0.0036, 0.0036, hexc, U, V, W, "tool_gunmetal"))
    for sw in (-1, 1):
        p.append(prism(hex_outline(0.0044), 0.0035 * sw, 0.0041 * sw, hexc, U, V, W, "accent_orange"))
    # side display (left, -w): a chamfered bezel and its dark screen
    disp = on(0.024)
    p.append(prism(chamfer_rect(0.0252, 0.0126, 0.0045), -(R - 0.004), -(R + 0.0016), disp, U, V, W, "tool_bezel"))
    p.append(prism(chamfer_rect(0.0198, 0.0091, 0.0028), -(R + 0.0015), -(R + 0.0019), disp, U, V, W, "screen"))
    # the other side: a raised plate with two panel screws
    p.append(prism(chamfer_rect(0.022, 0.0095, 0.003), R - 0.004, R + 0.0012, on(0.026), U, V, W, "tool_gunmetal"))
    for du in (-0.017, 0.017):
        p.append(cylinder(on(0.026 + du, 0.0, R + 0.0011), on(0.026 + du, 0.0, R + 0.0019), 0.0017, 10, "tool_silver",
                          ref=U))
    # top: a recessed panel, a smaller one at the back, and the frame of the status slot
    p.append(prism(chamfer_rect(0.026, 0.0082, 0.002), R - 0.003, R + 0.0009, on(0.03), U, W, V, "tool_bezel"))
    p.append(prism(chamfer_rect(0.0225, 0.0062, 0.0015), R + 0.0008, R + 0.0014, on(0.03), U, W, V, "tool_gunmetal"))
    p.append(prism(chamfer_rect(0.0055, 0.0045, 0.001), R - 0.003, R + 0.0009, on(-0.009, 0.0, -0.0065), U, W, V,
                   "tool_gunmetal"))
    p.append(prism(chamfer_rect(0.0042, 0.0022, 0.0008), R - 0.003, R + 0.0011, on(0.0655), U, W, V, "tool_bezel"))
    # scanner fins: three short blades on each side of the back
    for sw in (-1, 1):
        for k in range(3):
            uu = -0.0075 + 0.0032 * k
            p.append(prism(chamfer_rect(0.0009, 0.0062, 0.0006), 0.0, sw * 0.0032,
                           on(uu, 0.0, 0.0) + W * sw * (R - 0.0012), U, V, W, "tool_bezel"))
    # grip: raked back, wider at the top and the butt, finger bumps down the front (theta 0 is the front)
    rk = math.radians(CONNECT_RAKE)
    g = np.array([math.sin(rk), math.cos(rk), 0.0])
    n = np.array([math.cos(rk), -math.sin(rk), 0.0])

    def bumps(s):
        def fn(th):
            front = max(0.0, math.cos(th)) ** 3
            ridge = 0.5 + 0.5 * math.cos(2 * math.pi * (s + 0.004) / 0.021)
            return 1.0 + front * 0.075 * ridge * sstep(-0.045, -0.035, s) * sstep(0.03, 0.02, s)
        return fn

    gs = [(-0.053, 0.0170, 0.0124), (-0.0515, 0.0203, 0.0152), (-0.048, 0.0211, 0.0158), (-0.043, 0.0201, 0.0150),
          (-0.036, 0.0187, 0.0141), (-0.02, 0.0181, 0.0138), (0.0, 0.0183, 0.0139), (0.016, 0.0189, 0.0142),
          (0.03, 0.0199, 0.0147), (0.04, 0.0211, 0.0152), (0.052, 0.0215, 0.0154)]
    fine = [s / 1000.0 for s in range(-53, 53, 3)]
    rings = []
    for s in fine:
        k = np.searchsorted([x[0] for x in gs], s)
        k = min(max(k, 1), len(gs) - 1)
        (s0, a0, b0), (s1, a1, b1) = gs[k - 1], gs[k]
        f = (s - s0) / (s1 - s0)
        rings.append(dict(c=g * s, a=n, b=-W, rx=a0 + (a1 - a0) * f, ry=b0 + (b1 - b0) * f, e=2.7, fn=bumps(s)))
    for r in rings:
        r["a"], r["b"] = r["a"], -r["b"] if np.dot(np.cross(r["a"], r["b"]), g) < 0 else r["b"]
    p.append(loft(rings, 36, True, True, "grip_chevron", True))
    # the butt: a hard band round the bottom and an orange hex on its back
    p.append(loft([dict(c=g * s, a=n, b=-W, rx=rx, ry=ry, e=2.7) for s, rx, ry in
                   ((-0.0532, 0.0204, 0.0158), (-0.0526, 0.0214, 0.0166), (-0.0478, 0.0216, 0.0168), (-0.0472, 0.0206, 0.016))],
                  36, True, True, "tool_bezel", True))
    back = g * -0.044 - n * 0.0192
    p.append(prism(hex_outline(0.0052, math.pi / 6), 0.0, -0.0014, back, W, g, n, "accent_orange"))
    p.append(prism(hex_outline(0.0034, math.pi / 6), -0.0012, -0.0016, back, W, g, n, "grip_chevron"))
    # trigger, just under the housing in front of the grip
    trig = [np.array([0.0315, 0.0395, 0]), np.array([0.0328, 0.033, 0]), np.array([0.0312, 0.0265, 0]),
            np.array([0.0275, 0.0222, 0])]
    p.append(tube(trig, [(0.0036, 0.0021), (0.0034, 0.0021), (0.003, 0.002), (0.0024, 0.0018)], 10, W, "tool_silver"))
    return p


def _connect_glow_parts():
    """Everything on the tool that lights up cyan, in the gun frame."""
    R, H = CONNECT_BODY_R, CONNECT_BODY_V
    U, V, W = X, Y, Z
    on = lambda u, dv=0.0, w=0.0: np.array([u, H + dv, w])
    p = [annulus(on(-0.0264), -U, 0.0089, 0.0111, "glow"),
         cylinder(on(0.0757), on(0.0797), 0.0191, 36, "glow", ref=V, bevel=0.0005),
         prism(chamfer_rect(0.0034, 0.0014, 0.0006), R + 0.0005, R + 0.0013, on(0.0655), U, W, V, "glow"),
         cylinder(on(CONNECT_TIP_U - 0.0024), on(CONNECT_TIP_U), 0.0019, 10, "glow", ref=V)]
    # the display: two hex shields (a tick in the first) over a row of bars and a dashed line, on the left side
    d = on(0.024)
    w0, w1 = -(R + 0.00195), -(R + 0.0021)
    for cx, cy, r in ((-0.0105, 0.0025, 0.0043), (0.0105, 0.0025, 0.0036)):
        outer, inner = hex_outline(r, math.pi / 2), hex_outline(r - 0.0007, math.pi / 2)
        for k in range(6):
            a0, a1, b0, b1 = outer[k], outer[(k + 1) % 6], inner[k], inner[(k + 1) % 6]
            quad = [(a0[0] + cx, a0[1] + cy), (a1[0] + cx, a1[1] + cy), (b1[0] + cx, b1[1] + cy), (b0[0] + cx, b0[1] + cy)]
            p.append(prism(quad, w0, w1, d, U, V, W, "glow"))
    tick = [(-0.0123, 0.0026), (-0.0108, 0.0012), (-0.0084, 0.0041)]
    for a, b in zip(tick, tick[1:]):
        mid, half = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2), math.hypot(b[0] - a[0], b[1] - a[1]) / 2
        ang = math.atan2(b[1] - a[1], b[0] - a[0])
        ca, sa = math.cos(ang), math.sin(ang)
        rect = [(x * ca - y * sa + mid[0], x * sa + y * ca + mid[1]) for x, y in
                ((-half, -0.00035), (half, -0.00035), (half, 0.00035), (-half, 0.00035))]
        p.append(prism(rect, w0, w1, d, U, V, W, "glow"))
    for k in range(6):
        x = -0.0165 + 0.0029 * k
        p.append(prism([(x, -0.0068), (x + 0.0019, -0.0068), (x + 0.0019, -0.0052), (x, -0.0052)], w0, w1, d, U, V, W,
                       "glow"))
    for k in range(3):
        x = 0.003 + 0.0047 * k
        p.append(prism([(x, -0.0063), (x + 0.0032, -0.0063), (x + 0.0032, -0.0057), (x, -0.0057)], w0, w1, d, U, V, W,
                       "glow"))
    return p


def _connect_turn(parts):
    rot = _connect_rot()
    for part in parts:
        part.v = part.v @ rot.T
    return parts


def prop_connect_tool():
    return _connect_turn(_connect_parts())


def glow_material():
    m = bpy.data.materials.get("M_connect_glow")
    if m:
        return m
    m = bpy.data.materials.new("M_connect_glow")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = srgb(CONNECT_GLOW)
    bsdf.inputs["Roughness"].default_value = 0.3
    bsdf.inputs["Emission Color"].default_value = srgb(CONNECT_GLOW)
    bsdf.inputs["Emission Strength"].default_value = 1.0
    return m


def build_connect_glow(parent):
    part = merge(_connect_turn(_connect_glow_parts()))
    me = bpy.data.meshes.new("connect_tool_glow")
    me.from_pydata(part.v.tolist(), [], part.f)
    me.materials.append(glow_material())
    me.polygons.foreach_set("use_smooth", [False] * len(part.f))
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new("connect_tool_glow", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = parent.matrix_world.copy()
    ob.parent = parent
    ob.matrix_parent_inverse = parent.matrix_world.inverted()
    return ob


def prop_potato_blaster():
    p = [cylinder((-0.006, -0.065, 0), (0.0, 0.045, 0), 0.019, 12, "rubber", ref=X, bevel=0.004),
         cylinder((-0.17, 0.08, 0), (0.06, 0.08, 0), 0.047, 18, "paint_olive", ref=Y, bevel=0.008),
         cylinder((0.06, 0.08, 0), (0.08, 0.08, 0), 0.04, 16, "metal_dark", ref=Y, bevel=0.004),
         cylinder((0.08, 0.08, 0), (0.46, 0.08, 0), 0.026, 16, "metal", ref=Y),
         cylinder((0.44, 0.08, 0), (0.47, 0.08, 0), 0.032, 16, "orange", ref=Y, bevel=0.004),
         cylinder((-0.19, 0.08, 0), (-0.17, 0.08, 0), 0.05, 18, "metal_dark", ref=Y, bevel=0.004),
         cylinder((0.022, 0.0, 0.0), (0.03, 0.035, 0.0), 0.007, 10, "orange"),
         box((-0.03, 0.13, 0.0), (0.03, 0.006, 0.008), mat="metal_dark"),
         box((0.3, 0.112, 0.0), (0.004, 0.008, 0.003), mat="orange")]
    for xx in (-0.12, -0.02):
        p.append(ring_band((xx, 0.08, 0), 0.05, 0.05, 0.006, 0.003, "metal_dark", 18, axes=(Y, Z, X)))
    return p


# The water bucket, built to its reference sheets: a 30 cm tall, 30 cm wide matte teal plastic bucket tapering to the
# base, an orange foot and rolled rim, dark grey bands round the base and under the rim, an embossed droplet with three
# lines under it on the front, ridged grip panels on both sides, and a steel wire handle with a ribbed orange grip,
# hinged on bosses in the top band. It is modelled in its own frame (Blender axes): origin in the middle of the base,
# +Z up the axis, the front (the droplet) towards -Y and the handle's hinges on the X axis. The handle and the water
# are objects of their own, so the game can swing the handle and fill the bucket (game/scripts/Player/BucketRig.cs);
# the numbers both sides need are in game/data/character.json under "bucket".

def read_json_with_comments(path):
    import json
    import re
    text = re.sub(r"^\s*//.*$", "", Path(path).read_text(), flags=re.M)
    text = re.sub(r",\s*([}\]])", r"\1", text)
    return json.loads(text)


BUCKET = read_json_with_comments(REPO / "game" / "data" / "character.json")["bucket"]
BUCKET_HINGE = BUCKET["hinge_height"]
BUCKET_HANDLE = BUCKET["handle_length"]
BUCKET_LOWERED = BUCKET["handle_lowered_degrees"]
BUCKET_SEGS = 48


def bucket_wall_radius(z):
    """Outer radius of the teal wall at height z: it tapers from the top band down to the base."""
    return 0.1255 + (0.1405 - 0.1255) * (z - 0.07) / (0.235 - 0.07)


def lathe(profile, mat, segs=BUCKET_SEGS, cap0=False, cap1=False, smooth=True):
    """Turns a (z, r) profile about the Z axis. Faces point to the left of the profile's direction (outward while it
    climbs, inward when it comes back down the inside)."""
    rings = [dict(c=(0.0, 0.0, z), a=X, b=Y, rx=r, ry=r) for z, r in profile]
    return loft(rings, segs, cap0, cap1, mat, smooth)


def on_wall(phi, z, out=0.0):
    """A point on the outside of the teal wall at angle phi (0 = +X, -pi/2 = the front) and height z, out metres proud."""
    r = bucket_wall_radius(z) + out
    return np.array([r * math.cos(phi), r * math.sin(phi), z])


def wall_axes(phi):
    """(along the wall round the bucket, up the wall, out of the wall) at angle phi, following the taper."""
    slope = (0.1405 - 0.1255) / (0.235 - 0.07)
    out = norm(np.array([math.cos(phi), math.sin(phi), -slope]))
    tangent = np.array([-math.sin(phi), math.cos(phi), 0.0])
    up = np.cross(out, tangent)
    return tangent, up, out


def wall_bar(phi, z, half_w, half_h, proud, mat, rounding=6.0):
    """A rounded bar lying on the wall (a grip ridge or an embossed line), its middle at (phi, z)."""
    t, u, o = wall_axes(phi)
    c = on_wall(phi, z, proud * 0.5 - 0.001)
    return soft_box(c, (half_w, half_h, proud * 0.5 + 0.001), axes=(t, u, o), e=rounding, e2=rounding, segs=14, rings=6,
                    mat=mat)


def droplet_outline(phi, zc, size):
    """An embossed droplet: a raised round cord tracing a teardrop, its round end at height zc."""
    r = bucket_wall_radius(zc)
    pts = []
    n = 40
    for k in range(n + 1):
        a = 2 * math.pi * k / n
        # teardrop: x = sin(a) * sin(a/2)^m, y = -cos(a), the point up
        u = size * 0.62 * math.sin(a) * math.sin(a / 2) ** 1.2
        v = size * (0.5 - 0.5 * math.cos(a)) * 1.0
        pts.append(on_wall(phi + u / r, zc - size * 0.42 + v * 1.25, 0.0012))
    loop = pts[:-1] + [pts[0]]
    return tube(loop, [(0.0022, 0.0016)] * len(loop), 8, norm(np.array([math.cos(phi), math.sin(phi), 0.0])),
                "bucket_teal", cap=False)


def bucket_body():
    p = []
    # orange foot
    p.append(lathe([(0.0, 0.0), (0.0, 0.112), (0.002, 0.121), (0.008, 0.126), (0.03, 0.128), (0.036, 0.1245)],
                   "bucket_orange"))
    # lower band
    p.append(lathe([(0.033, 0.124), (0.037, 0.1315), (0.042, 0.1335), (0.072, 0.1345), (0.077, 0.1325), (0.08, 0.125)],
                   "bucket_band"))
    # teal wall, tapering
    zs = np.linspace(0.074, 0.232, 9)
    p.append(lathe([(z, bucket_wall_radius(z)) for z in zs], "bucket_teal"))
    # top band, with a boss for each hinge
    p.append(lathe([(0.226, 0.1395), (0.230, 0.1465), (0.235, 0.149), (0.257, 0.1505), (0.262, 0.1485),
                    (0.264, 0.144)], "bucket_band"))
    # rolled orange rim, over the lip and down the inside
    p.append(lathe([(0.259, 0.1455), (0.263, 0.1525), (0.268, 0.1555), (0.288, 0.1565), (0.294, 0.1545),
                    (0.2985, 0.1495), (0.2995, 0.1445), (0.297, 0.1405), (0.29, 0.1385), (0.282, 0.1378)],
                   "bucket_orange"))
    # the inside: wall and floor
    inner = BUCKET["inner_radius_rim"]
    floor = BUCKET["inner_radius_floor"]
    fz = BUCKET["floor_height"]
    prof = [(0.284, inner + 0.0004)]
    for z in np.linspace(0.27, fz + 0.012, 8):
        prof.append((z, floor + (inner - floor) * (z - fz) / (BUCKET["rim_height"] - fz)))
    prof += [(fz + 0.004, floor - 0.003), (fz, floor - 0.012), (fz, 0.06), (fz, 0.0)]
    p.append(lathe(prof, "bucket_inner"))
    for sx in (-1, 1):
        # hinge boss on the top band and the pin the handle turns on
        p.append(bevel_box((sx * 0.1505, 0.0, 0.2445), (0.0075, 0.021, 0.0185), 0.0035, mat="bucket_band"))
        p.append(cylinder((sx * 0.155, 0, BUCKET_HINGE), (sx * 0.1665, 0, BUCKET_HINGE), 0.0085, 16, "bucket_pin",
                          ref=Y, bevel=0.0018))
        # a tab on the rim above each hinge
        p.append(bevel_box((sx * 0.1535, 0.0, 0.2785), (0.0055, 0.03, 0.0165), 0.004, mat="bucket_orange"))
        # side grip panel with four ridges
        phi = 0.0 if sx > 0 else math.pi
        p.append(wall_bar(phi, 0.152, 0.03, 0.058, 0.004, "bucket_teal", rounding=8.0))
        for z in (0.118, 0.141, 0.164, 0.187):
            p.append(wall_bar(phi, z, 0.022, 0.0055, 0.0085, "bucket_teal"))
    # front: an embossed droplet with three lines under it
    front = -math.pi / 2
    p.append(droplet_outline(front, 0.168, 0.05))
    for z, w in ((0.112, 0.03), (0.099, 0.024), (0.086, 0.018)):
        p.append(wall_bar(front, z, w, 0.0024, 0.0036, "bucket_teal", rounding=4.0))
    return p


def bucket_handle():
    """The wire handle in its own frame: origin on the hinge axis, X along it, raised along +Z."""
    L = BUCKET_HANDLE
    half = []
    for x, z in ((0.1675, 0.0), (0.1675, 0.012), (0.165, 0.03), (0.158, 0.075), (0.142, 0.13), (0.118, 0.175),
                 (0.095, 0.2), (0.075, L - 0.002), (0.058, L)):
        half.append(np.array([x, 0.0, z]))
    wire = [np.array([-p[0], 0, p[2]]) for p in half] + [np.array([p[0], 0, p[2]]) for p in reversed(half)]
    # the wire's ends hook through the pins: start along the axis inside them
    wire = [np.array([-0.158, 0.0, 0.0])] + wire + [np.array([0.158, 0.0, 0.0])]
    p = [tube(wire, 0.0031, 10, Y, "bucket_wire")]
    # ribbed orange grip sleeve on the top bar
    p.append(cylinder((-0.056, 0, L), (0.056, 0, L), 0.0125, 18, "bucket_orange", ref=Y, bevel=0.004))
    for x in np.linspace(-0.04, 0.04, 6):
        p.append(ring_band((x, 0, L), 0.0128, 0.0128, 0.0028, 0.0016, "bucket_orange", 18, axes=(Y, Z, X)))
    return p


def bucket_water_disc():
    """A unit disc for the water's surface (the game sizes it to the inside at the water's height)."""
    segs = BUCKET_SEGS
    v = [np.array([0.0, 0.0, 0.0])]
    for k in range(segs):
        a = 2 * math.pi * k / segs
        v.append(np.array([math.cos(a), math.sin(a), 0.0]))
    f = [(0, 1 + k, 1 + (k + 1) % segs) for k in range(segs)]
    return Part(v, f, "bucket_water", True)


def inner_radius_at(z):
    zf, zr = BUCKET["floor_height"], BUCKET["rim_height"]
    return BUCKET["inner_radius_floor"] + (BUCKET["inner_radius_rim"] - BUCKET["inner_radius_floor"]) * (z - zf) / (zr - zf)


def rot_x(deg):
    return axis_rot(np.array([1.0, 0.0, 0.0]), deg)


def bucket_in_grip(grip, tilt):
    """The bucket's frame in the grip frame of the hand holding it (4x4, bucket -> grip): grip 0 holds the back of the
    rim, 1 the handle; tilt (degrees) swings the mouth towards the bucket's back (+Y) about its hinges, the handle
    staying in the hand. Matches BucketRig.cs."""
    R0 = np.array([[0.0, 0.0, -1.0], [-1.0, 0.0, 0.0], [0.0, 1.0, 0.0]])    # columns: bucket X, Y, Z in grip axes
    o_rim = -R0 @ np.asarray(BUCKET["rim_grip"], float)
    o_handle = -R0 @ np.array([0.0, 0.0, BUCKET_HINGE + BUCKET_HANDLE])
    o = o_rim + (o_handle - o_rim) * grip
    pivot = o + R0 @ np.array([0.0, 0.0, BUCKET_HINGE])
    R = axis_rot(R0 @ np.array([1.0, 0.0, 0.0]), -tilt)
    M = np.eye(4)
    M[:3, :3] = R @ R0
    M[:3, 3] = pivot + R @ (o - pivot)
    return M


def handle_angle(grip, tilt):
    """The handle's angle from upright in the bucket's frame (degrees about +X: positive falls to the front, -Y)."""
    return BUCKET_LOWERED * (1.0 - sstep(0.0, 1.0, grip)) + tilt


def to_matrix(M):
    return Matrix([list(r) for r in M])


def build_bucket_objects(name, handle_deg, water, parent_matrix=None):
    """The bucket as three objects: `name` (the body), `name`_handle and `name`_water (both children of the body).
    water: 0 (none) to 1 (full). Returns ([objects], [parts], body)."""
    body_part = merge(bucket_body())
    body = build_mesh_object(name, body_part)
    if parent_matrix is not None:
        body.matrix_world = parent_matrix
    handle_part = merge(bucket_handle())
    handle = build_mesh_object(name + "_handle", handle_part)
    handle.parent = body
    handle.matrix_parent_inverse = Matrix.Identity(4)
    Mh = np.eye(4)
    Mh[:3, :3] = rot_x(handle_deg)
    Mh[:3, 3] = (0.0, 0.0, BUCKET_HINGE)
    handle.matrix_basis = to_matrix(Mh)
    objs, parts = [body, handle], [body_part, handle_part]
    water_part = bucket_water_disc()
    wat = build_mesh_object(name + "_water", water_part)
    wat.parent = body
    wat.matrix_parent_inverse = Matrix.Identity(4)
    z = BUCKET["floor_height"] + (BUCKET["water_height"] - BUCKET["floor_height"]) * max(water, 1e-3)
    r = inner_radius_at(z) + 0.001
    wat.matrix_basis = Matrix.LocRotScale(Vector((0, 0, z)), None, Vector((r, r, 1.0)))
    wat.hide_render = water <= 0.0
    return objs, parts, body, wat


def water_material():
    m = bpy.data.materials.new("M_bucket_water")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col, rough, _ = PALETTE["bucket_water"]
    bsdf.inputs["Base Color"].default_value = srgb(col)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Alpha"].default_value = 0.78
    m.blend_method = "BLEND" if hasattr(m, "blend_method") else m.blend_method
    if hasattr(m, "surface_render_method"):
        m.surface_render_method = "BLENDED"
    return m


def texture_bucket(objs, parts, wat, group):
    stats = ct.texture_props(objs, parts, [mesh_material_names(p) for p in parts], PALETTE, BUCKET_ATLAS, seed=9,
                             group=group, material="M_water_bucket")
    wat.data.materials.clear()
    wat.data.materials.append(water_material())
    return stats


# The Refine Tool, built to its reference sheet: a scrap scraper about 35 cm long that is never in the inventory and
# only shows in hand while refining. A 13 cm handle of welded grey pipe and riveted plates, its grip wrapped in brown
# tape with a yellow-and-black hazard band at the butt, and a 19.5 x 7.5 cm blade cut from rusty sheet: a jagged spine,
# a serrated scraping edge, a pointed tip and lightening holes. The fist sits on the tape (the origin); the blade runs
# up the handle axis (y) with its teeth on the finger side (x), so a fist with the fingers down holds it edge down.
REFINE_BLADE_Y = 0.098                 # where the blade leaves the collar (grip frame y)
REFINE_TIP_Y = 0.293                   # its point
REFINE_PIPE_R = 0.0225                 # the steel pipe of the handle
REFINE_HALF_T = 0.00175                # half the sheet's thickness


def _refine_spine(u, length):
    """x of the blade's spine (the back, -x) at u along the blade: straight, then curving into the point."""
    s = max(0.0, (u - 0.085) / (length - 0.085))
    return -0.03 + 0.032 * s ** 1.7


def _refine_edge(u, length):
    """x of the serrated edge's tooth roots at u along the blade, from the barbed shoulder to the point."""
    s = (u - 0.03) / (length - 0.03)
    return 0.002 + 0.041 * (1.0 - s) ** 0.85


def refine_blade_loops(seed=23):
    """The blade's outline (counter-clockwise seen from +z) and its holes (clockwise), as (x, y) points."""
    rnd = random.Random(seed)
    length = REFINE_TIP_Y - REFINE_BLADE_Y
    y0 = REFINE_BLADE_Y - 0.022        # the tang, hidden in the collar
    out = [(0.018, y0), (0.022, REFINE_BLADE_Y - 0.004), (0.025, REFINE_BLADE_Y + 0.008),
           (0.03, REFINE_BLADE_Y + 0.012), (0.047, REFINE_BLADE_Y + 0.026)]
    # serrated edge: saw teeth leaning toward the handle, deepest near the shoulder, worn smaller toward the point
    teeth = 12
    u0, u1 = 0.034, length - 0.018
    for k in range(teeth):
        ua = u0 + (u1 - u0) * k / teeth
        ub = u0 + (u1 - u0) * (k + 1) / teeth
        depth = (0.0075 - 0.003 * k / teeth) * (0.85 + 0.3 * rnd.random())
        out.append((_refine_edge(ua, length), REFINE_BLADE_Y + ua))
        up = ua + (ub - ua) * 0.32
        out.append((_refine_edge(up, length) + depth, REFINE_BLADE_Y + up + 0.0008))
    out.append((_refine_edge(u1, length), REFINE_BLADE_Y + u1))
    out.append((0.002, REFINE_TIP_Y))
    # the spine back to the collar, chipped and uneven
    n = 26
    for k in range(1, n + 1):
        u = length * (1.0 - k / n)
        x = _refine_spine(u, length)
        if 0 < k < n:
            x += (rnd.random() - 0.5) * 0.0011 - (0.0016 if rnd.random() < 0.1 else 0.0)
        out.append((x, REFINE_BLADE_Y + u))
    out += [(-0.03, REFINE_BLADE_Y - 0.008), (-0.022, y0)]
    # lightening holes: round, oval and one slot, drilled in a loose cluster
    holes = []
    for u, v, rx, ry, e in ((0.032, -0.012, 0.0052, 0.0052, 2.0), (0.05, 0.008, 0.0062, 0.0055, 2.0),
                            (0.058, -0.016, 0.0045, 0.0045, 2.0), (0.075, -0.003, 0.014, 0.0042, 3.5),
                            (0.093, 0.014, 0.005, 0.0045, 2.0), (0.098, -0.016, 0.0042, 0.0042, 2.0),
                            (0.118, 0.0, 0.0055, 0.0048, 2.0), (0.138, -0.013, 0.0038, 0.0038, 2.0),
                            (0.143, 0.009, 0.0036, 0.0034, 2.0), (0.162, -0.003, 0.003, 0.003, 2.0)):
        segs = 18 if e > 2.0 else 12
        holes.append([(v + _se(math.cos(th), e) * ry, REFINE_BLADE_Y + u + _se(math.sin(th), e) * rx)
                      for th in np.linspace(0, 2 * math.pi, segs, endpoint=False)])
    return _wound(out, 1), [_wound(h, -1) for h in holes]


def _wound(loop, sign):
    """loop turned counter-clockwise (sign 1) or clockwise (sign -1), seen from +z."""
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(loop, loop[1:] + loop[:1]))
    return loop if area * sign > 0 else loop[::-1]


def refine_blade():
    """The blade: the outline and its holes filled front and back, with side walls round every loop."""
    from mathutils.geometry import tessellate_polygon
    outer, holes = refine_blade_loops()
    loops = [outer] + holes
    ct.SHEET_OUTLINES["rust_sheet"] = loops
    t = REFINE_HALF_T

    def warp(x, y):
        # a flattened sheet is never quite flat: a slight twist toward the point and a dent or two
        s = (y - REFINE_BLADE_Y) / (REFINE_TIP_Y - REFINE_BLADE_Y)
        return 0.0035 * max(0.0, s) ** 2 * (x / 0.04) + 0.0008 * math.sin(x * 160.0 + y * 90.0) * max(0.0, s)

    flat = [p for loop in loops for p in loop]
    tris = tessellate_polygon([[Vector((x, y, 0.0)) for x, y in loop] for loop in loops])
    verts = [(x, y, warp(x, y) + t) for x, y in flat] + [(x, y, warp(x, y) - t) for x, y in flat]
    nf = len(flat)
    faces = []
    for a, b, c in tris:
        (ax, ay), (bx, by), (cx, cy) = flat[a], flat[b], flat[c]
        if (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) < 0:     # keep the front face pointing +z
            b, c = c, b
        faces.append((a, b, c))
        faces.append((c + nf, b + nf, a + nf))
    off = 0
    for loop in loops:
        m = len(loop)
        for i in range(m):
            j = (i + 1) % m
            faces.append((off + i + nf, off + j + nf, off + j, off + i))
        off += m
    return Part(verts, faces, "rust_sheet", smooth=False)


def prop_refine_tool():
    r = REFINE_PIPE_R
    p = [
        # butt: a capped end, the hazard band, a thin ring, then the tape-wrapped grip
        cylinder((0, -0.07, 0), (0, -0.056, 0), 0.0215, 24, "pipe_steel", ref=X, bevel=0.0035),
        cylinder((0, -0.0575, 0), (0, -0.0385, 0), 0.0207, 28, "hazard_band", ref=X, bevel=0.0008),
        cylinder((0, -0.04, 0), (0, -0.034, 0), 0.0214, 24, "pipe_steel", ref=X, bevel=0.0012),
        tube([(0, -0.0355, 0), (0, -0.034, 0), (0, -0.018, 0), (0, 0.004, 0), (0, 0.022, 0), (0, 0.034, 0),
              (0, 0.0355, 0)], [0.0186, 0.0204, 0.0207, 0.0203, 0.0208, 0.0204, 0.0186], 28, X, "tape_brown"),
        # the welded pipe body with its clamp ring by the grip
        cylinder((0, 0.03, 0), (0, 0.104, 0), r, 28, "pipe_steel", ref=X, bevel=0.0025),
        cylinder((0, 0.033, 0), (0, 0.042, 0), r + 0.0016, 28, "pipe_steel", ref=X, bevel=0.0012),
        # strap round the front of the pipe with a bolted lug on the spine side
        cylinder((0, 0.083, 0), (0, 0.091, 0), r + 0.0018, 28, "plate_steel", ref=X, bevel=0.001),
        bevel_box((-r - 0.0035, 0.087, 0), (0.0045, 0.0045, 0.009), 0.0015, mat="plate_steel"),
        # the collar that grips the blade's tang, and the cheek plates riveted through it
        bevel_box((0.003, 0.1015, 0), (0.0255, 0.008, 0.0095), 0.0022, mat="plate_steel"),
    ]
    p.extend(hex_bolt((-r - 0.008, 0.087, 0), (-1, 0, 0), (0, 0, 1), r=0.0042, height=0.003))
    for sz in (-1, 1):
        # a bent plate on each face of the pipe, a rivet in each corner
        p.append(bevel_box((0.0, 0.064, sz * (r + 0.0012)), (0.0145, 0.016, 0.0024), 0.0012, mat="plate_steel"))
        for xx in (-0.009, 0.009):
            for yy in (0.053, 0.075):
                p.append(rivet((xx, yy, sz * (r + 0.0036)), (0, 0, sz), r=0.0024))
        p.append(bevel_box((0.006, 0.113, sz * (REFINE_HALF_T + 0.0019)), (0.019, 0.0105, 0.0019), 0.0009,
                           mat="plate_steel"))
        for xx in (-0.006, 0.017):
            p.append(rivet((xx, 0.114, sz * (REFINE_HALF_T + 0.0038)), (0, 0, sz), r=0.0028))
    # a plate along the spine side of the pipe, held by two rivets
    p.append(bevel_box((-r - 0.0012, 0.06, 0), (0.0024, 0.017, 0.0105), 0.0011, mat="plate_steel"))
    for yy in (0.049, 0.071):
        p.append(rivet((-r - 0.0036, yy, 0), (-1, 0, 0), r=0.0024))
    p.append(refine_blade())
    return p


PROPS = {"lift_remote": prop_lift_remote, "weld_tool": prop_weld_tool,
         "paint_tool": prop_paint_tool, "sledgehammer": prop_sledgehammer, "connect_tool": prop_connect_tool,
         "potato_blaster": prop_potato_blaster, "refine_tool": prop_refine_tool}


def build_prop(name):
    part = merge(PROPS[name]())
    ob = build_mesh_object(name, part)
    ob.matrix_world = grip_frame_matrix("r")
    return ob, part


# =====================================================================================================
# Main
# =====================================================================================================

def export_character(name):
    reset_scene()
    arm, ob, part = assemble_character(name)
    path = OUT_DIR / f"{name}.glb"
    export_glb(path, [arm, ob], animations=False)
    print(f"{path.name}: {tri_count(part)} triangles")


def export_animations():
    reset_scene()
    arm = build_armature("Armature")
    bake_actions(arm)
    path = OUT_DIR / "humanoid_animations.glb"
    export_glb(path, [arm], animations=True)
    print(f"{path.name}: {len(CLIPS)} clips")


def export_props():
    reset_scene()
    objs, parts = [], []
    for name in PROPS:
        ob, part = build_prop(name)
        objs.append(ob)
        parts.append(part)
    # The Sledgehammer is 1.3 m long, so it has an atlas of its own rather than starving the small tools of texels; the
    # Refine Tool and the Connect Tool have one each too, since first person holds them close to the eye.
    own = {name: [i for i, n in enumerate(PROPS) if n == name] for name in ("sledgehammer", "refine_tool", "connect_tool")}
    shared = [i for i in range(len(objs)) if not any(i in idx for idx in own.values())]
    stats = {}
    for idx, group, material in ((shared, "props", "M_hand_tools"), (own["sledgehammer"], "sledgehammer", "M_sledgehammer"),
                                 (own["refine_tool"], "refine_tool", "M_refine_tool"),
                                 (own["connect_tool"], "connect_tool", "M_connect_tool")):
        stats.update(ct.texture_props([objs[i] for i in idx], [parts[i] for i in idx],
                                      [mesh_material_names(parts[i]) for i in idx], PALETTE, PROP_ATLAS, seed=5,
                                      group=group, material=material))
    for g, (size, texel_mm, cov) in stats.items():
        print(f"  {g} atlas: {size}px, {texel_mm:.2f} mm/texel, {cov * 100:.0f}% covered")
    # The water bucket: an empty at the grip with the body under it (as the full bucket hangs from the hand), and the
    # handle and the water under the body, all moved by the game. It has an atlas of its own too.
    root = bpy.data.objects.new("water_bucket", None)
    bpy.context.scene.collection.objects.link(root)
    root.matrix_world = grip_frame_matrix("r")
    bobjs, bparts, body, wat = build_bucket_objects("water_bucket_body", handle_angle(1.0, 0.0), 1.0)
    body.parent = root
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = to_matrix(bucket_in_grip(1.0, 0.0))
    wat.hide_render = False
    stats.update(texture_bucket(bobjs, bparts, wat, "water_bucket"))
    for g, (size, texel_mm, cov) in stats.items():
        if g == "water_bucket":
            print(f"  {g} atlas: {size}px, {texel_mm:.2f} mm/texel, {cov * 100:.0f}% covered")
    # the Connect Tool's lights are a child node with their own emissive material, outside every atlas
    glow = build_connect_glow(objs[list(PROPS).index("connect_tool")])
    path = OUT_DIR / "hand_tools.glb"
    export_glb(path, objs + [glow] + [root] + bobjs + [wat], animations=False)
    print(f"{path.name}: {len(objs) + 1} props")


def export_bucket_items():
    """The empty and full water buckets as items (their pictures, and the bucket put down): handle down and dry, or
    handle up with the water in."""
    for name, full in (("water_bucket", False), ("water_bucket_full", True)):
        reset_scene()
        objs, parts, body, wat = build_bucket_objects(name, handle_angle(1.0 if full else 0.0, 0.0), 1.0 if full else 0.0)
        texture_bucket(objs, parts, wat, name)
        path = ITEM_DIR / f"{name}.glb"
        export_glb(path, objs + ([wat] if full else []), animations=False)
        print(f"{path.name}: {sum(tri_count(p) for p in parts)} triangles")


def main(argv):
    what = set(argv) or {"characters", "animations", "props", "items"}
    if "characters" in what:
        for name in CHARACTERS:
            export_character(name)
    if "animations" in what:
        export_animations()
    if "props" in what:
        export_props()
    if "items" in what:
        export_bucket_items()


if __name__ == "__main__":
    main([a for a in sys.argv[1:] if not a.startswith("-")])
