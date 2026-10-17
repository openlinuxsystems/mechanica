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
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "mods" / "vanilla" / "meshes" / "characters"
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


def lerp(a, b, t):
    return a + (b - a) * t


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


def rot_to(a, b):
    """Rotation matrix (3x3 numpy) turning unit vector a onto unit vector b."""
    a, b = norm(a), norm(b)
    v = np.cross(a, b)
    c = float(np.dot(a, b))
    if c < -0.999999:
        axis = norm(np.cross(a, [1, 0, 0]) if abs(a[0]) < 0.9 else np.cross(a, [0, 1, 0]))
        return np.asarray(Matrix.Rotation(math.pi, 3, Vector(axis)))
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1.0 / (1.0 + c))


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

class Part:
    """A chunk of geometry with per-face material names, a smooth flag and (later) bone weights."""

    def __init__(self, verts, faces, mat, smooth=True):
        self.v = np.asarray(verts, float).reshape(-1, 3)
        self.f = [tuple(int(i) for i in f) for f in faces]
        self.m = [mat] * len(self.f) if isinstance(mat, str) else list(mat)
        self.s = [smooth] * len(self.f) if isinstance(smooth, bool) else list(smooth)
        self.w: dict[str, np.ndarray] | None = None

    def copy(self):
        p = Part(self.v.copy(), list(self.f), list(self.m), list(self.s))
        p.w = None if self.w is None else {k: v.copy() for k, v in self.w.items()}
        return p

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
    verts, faces, mats, smooth = [], [], [], []
    weights: dict[str, list] = {}
    off = 0
    n_total = sum(len(p.v) for p in parts)
    for p in parts:
        verts.append(p.v)
        faces.extend(tuple(i + off for i in f) for f in p.f)
        mats.extend(p.m)
        smooth.extend(p.s)
        if p.w is not None:
            for k, w in p.w.items():
                arr = weights.setdefault(k, np.zeros(n_total))
                arr[off:off + len(p.v)] += w
        off += len(p.v)
    out = Part(np.vstack(verts) if verts else np.zeros((0, 3)), faces, mats, smooth)
    out.w = {k: np.asarray(v) for k, v in weights.items()} if weights else None
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
    return Part(verts, faces, mat, smooth)


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
    hx, hy, hz = half
    rl = []
    us = np.linspace(-1, 1, rings + 2)[1:-1]
    for u in us:
        k = (1 - abs(u) ** e2) ** (1.0 / e2)
        z = np.sign(u) * abs(u) ** (2.0 / e2 if e2 > 2 else 1.0) * hz if False else u * hz
        rl.append(dict(c=np.asarray(center) + ax[2] * z, a=ax[0], b=ax[1], rx=hx * k, ry=hy * k, e=e))
    p = loft(rl, segs, True, True, mat, smooth)
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
    c = np.asarray(center, float)
    vs = []
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                vs.append(c + ax[0] * sx * half[0] + ax[1] * sy * half[1] + ax[2] * sz * half[2])
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    return Part(vs, faces, mat, smooth=False)


def extrude_outline(outline2d, thickness, frame, origin, mat="default"):
    """Extrude a convex-ish 2D outline (list of (u, v)) along the frame normal. frame = (U, V, N)."""
    U, V, N = (np.asarray(x, float) for x in frame)
    o = np.asarray(origin, float)
    n = len(outline2d)
    vs = []
    for s in (-0.5, 0.5):
        for (u, v) in outline2d:
            vs.append(o + U * u + V * v + N * thickness * s)
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    return Part(vs, faces, mat, smooth=False)


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


def arm_weights(side, include_hand=True):
    bones = [f"shoulder_{side}", f"upper_arm_{side}", f"forearm_{side}"] + ([f"hand_{side}"] if include_hand else [])
    return chain_weights(bones, blends=[0.07, 0.07, 0.05][:len(bones) - 1])


def hand_weights(side):
    d, w, n = hand_frame(side)
    h = bone_head(f"hand_{side}")

    def fn(v):
        rel = v - h
        along = rel @ d
        across = rel @ w
        fw = smoothstep(0.075, 0.095, along)
        wrist = 1 - smoothstep(-0.01, 0.025, along)
        tb = smoothstep(0.012, 0.03, across) * (1 - smoothstep(0.06, 0.09, along)) * smoothstep(0.005, 0.03, along)
        hand = np.clip(1 - fw - wrist, 0, 1)
        out = {f"forearm_{side}": wrist, f"hand_{side}": hand * (1 - tb), f"fingers_{side}": fw * (1 - tb),
               f"thumb_{side}": tb * (hand + fw)}
        return out
    return fn


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
    "jacket_olive": ("#5a6238", 0.85, 0.0),
    "jacket_olive_dark": ("#454b2c", 0.9, 0.0),
    "trouser_olive": ("#4e5532", 0.9, 0.0),
    "backpack_olive": ("#3d4430", 0.85, 0.0),
    "glove_dark": ("#2e2b28", 0.7, 0.0),
    "boot_tan": ("#8a5a33", 0.6, 0.0),
    "goggle_dark": ("#2a2a2a", 0.5, 0.3),
    "lens_orange": ("#e8862a", 0.08, 0.0),
    # female outfit
    "denim": ("#4b6c9a", 0.85, 0.0),
    "denim_light": ("#6c8bb4", 0.85, 0.0),
    "denim_dark": ("#3c5880", 0.85, 0.0),
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
