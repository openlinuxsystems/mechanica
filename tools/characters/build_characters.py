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
        z = u * hz
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
              zs=None):
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
        pts = ring_points(w, yf, yb, yc, ef, eb, thetas)
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


def surface_normal(bvh, point):
    loc, nrm, _, _ = bvh.find_nearest(Vector(point))
    return np.asarray(nrm) if nrm is not None else np.array([0.0, 0.0, 1.0])


def strap(points, normals, width, thickness, mat, segs=6, e=6.0):
    """A flat strap along points; normals give the thickness direction (surface normal)."""
    fr = frames_along(points, normals[0])
    rings = []
    for p, (t, _, _), nv in zip(points, fr, normals):
        a = norm(np.asarray(nv) - np.dot(nv, t) * t)
        b = np.cross(t, a)
        rings.append(dict(c=p, a=a, b=b, rx=thickness, ry=width, e=e))
    return loft(rings, segs, True, True, mat, True)


def surface_strap(bvh, points, width, thickness, mat, out=0.004, samples=3):
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
        return [(-0.116, 0.010, -0.070, -0.052, -0.062), (-0.110, 0.026, -0.080, -0.030, -0.056),
                (-0.098, 0.038, -0.086, 0.004, -0.042), (-0.082, 0.049, -0.090, 0.028, -0.030),
                (-0.062, 0.058, -0.093, 0.050, -0.018), (-0.042, 0.065, -0.095, 0.070, -0.008),
                (-0.018, 0.069, -0.096, 0.088, 0.0), (0.006, 0.072, -0.095, 0.097, 0.005),
                (0.030, 0.073, -0.093, 0.101, 0.008), (0.055, 0.072, -0.087, 0.101, 0.01),
                (0.080, 0.066, -0.075, 0.093, 0.012), (0.098, 0.053, -0.056, 0.076, 0.012),
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
    n_r, segs = 42, 56
    part, grid = grid_loft(keys, n_r, segs, ef=2.5, eb=2.15)
    g = grid.copy()
    x, y, z = g[..., 0], g[..., 1], g[..., 2]
    front = np.clip(-y / 0.09, 0, 1) ** 1.5
    dy = np.zeros_like(y)
    dx = np.zeros_like(x)
    for sx in (1, -1):
        dy += 0.0055 * _bump(x, z, sx * EYE_X, EYE_Z + 0.001, 0.017, 0.012) * front          # eye socket
        dy -= 0.0045 * _bump(x, z, sx * 0.03, 0.017, 0.022, 0.008) * front                 # brow ridge
        dy -= 0.004 * _bump(x, z, sx * 0.048, -0.03, 0.016, 0.016) * front                 # cheekbone
        dx += sx * 0.003 * _bump(x, z, sx * 0.062, -0.03, 0.012, 0.02)
    dy -= 0.004 * _bump(x, z, 0, -0.076, 0.024, 0.016) * front                            # muzzle
    dy -= (0.0045 if not female else 0.003) * _bump(x, z, 0, -0.106, 0.014, 0.01) * front  # chin
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
    nose_keys = [(z, w * (0.82 if female else 0.92), p * (0.8 if female else 0.88)) for (z, w, p) in nose_keys]
    rings = []
    for zr, w, prot in nose_keys:
        yf = face_y(0.0, zr)
        rings.append(dict(c=(0, yf + 0.006, HEAD_C[2] + zr), a=(1, 0, 0), b=(0, -1, 0), rx=w, ry=prot + 0.006,
                          e=2.2))
    # loft rings stacked downward: tangent must be a x b = (1,0,0)x(0,-1,0) = (0,0,-1): downward. good
    parts.append(loft(rings, 14, True, True, skin, True, 0.002, 0.002))
    nw = 0.0115 if not female else 0.0098
    for sx in (1, -1):
        yf = face_y(sx * nw, -0.047)
        parts.append(ellipsoid((sx * nw, yf - 0.003, HEAD_C[2] - 0.047), (0.0065, 0.008, 0.0055), mat=skin,
                               segs=10, rings=6))
    # eyes
    for sx in (1, -1):
        ex = sx * EYE_X
        yf = face_y(ex, EYE_Z)
        r = 0.0118 if not female else 0.0122
        c = np.array([ex, yf + r - 0.0075, HEAD_C[2] + EYE_Z])
        parts.append(ellipsoid(c, (r, r, r), segs=14, rings=8, mat="eye_white"))
        look = norm(np.array([sx * 0.06, -1.0, 0.0]))
        iris_c = c + look * (r - 0.0006)
        ax = np.array([norm(np.cross(look, (0, 0, 1))), np.array([0, 0, 1.0]), look])
        ir = 0.0062 if not female else 0.0068
        parts.append(ellipsoid(iris_c, (ir, ir, 0.0012), axes=ax, segs=12, rings=5, mat=iris_mat))
        parts.append(ellipsoid(iris_c + look * 0.0009, (ir * 0.45, ir * 0.45, 0.0008), axes=ax, segs=10,
                               rings=4, mat="pupil"))
        # upper eyelid: a shell hugging the top of the eyeball
        lid = []
        for k, a in enumerate(np.linspace(-1.2, 1.2, 9)):
            dirv = norm(np.array([sx * math.sin(a) * 1.0, -math.cos(a), 0.0]))
            p = c + dirv * (r + 0.0012) + np.array([0, 0, r * 0.5])
            lid.append(p)
        parts.append(tube(lid, [0.0016 if not female else 0.0019] * len(lid), 6, (0, 0, 1),
                          "brow" if female else skin))
        lid2 = [c + norm(np.array([sx * math.sin(a), -math.cos(a), 0.55])) * (r + 0.0005) for a in
                np.linspace(-1.15, 1.15, 9)]
        parts.append(tube(lid2, [0.0028] * len(lid2), 6, (0, 0, 1), skin))
    # brows
    for sx in (1, -1):
        pts = []
        for u in np.linspace(0, 1, 6):
            x = sx * (0.012 + 0.04 * u)
            zr = 0.013 + 0.006 * math.sin(math.pi * (u * 0.8 + 0.1)) - (0.004 * u if female else 0.0)
            yf = face_y(x, zr)
            pts.append(np.array([x, yf - 0.0015, HEAD_C[2] + zr]))
        rad = [(0.0022, 0.0045), (0.0028, 0.0055), (0.003, 0.0055), (0.0028, 0.005), (0.0024, 0.0042),
               (0.0018, 0.003)]
        if female:
            rad = [(a * 0.7, b * 0.65) for a, b in rad]
        parts.append(tube(pts, rad, 8, (0, -1, 0), "brow", e=2.0))
    # lips
    yl = face_y(0, -0.071)
    ul = (0.017, 0.0055, 0.0042) if not female else (0.0165, 0.006, 0.0048)
    ll = (0.015, 0.006, 0.0048) if not female else (0.0155, 0.0068, 0.0058)
    parts.append(ellipsoid((0, yl + 0.0025, HEAD_C[2] - 0.0705), ul, segs=14, rings=6, mat=lips_mat))
    yl2 = face_y(0, -0.079)
    parts.append(ellipsoid((0, yl2 + 0.0028, HEAD_C[2] - 0.0790), ll, segs=14, rings=6, mat=lips_mat))
    # ears
    for sx in (1, -1):
        x = sx * 0.071
        ax = np.array([(sx, 0, 0), (0, math.cos(0.25), -math.sin(0.25)), (0, math.sin(0.25), math.cos(0.25))])
        parts.append(ellipsoid((x + sx * 0.004, 0.018, HEAD_C[2] - 0.022), (0.008, 0.019, 0.03), axes=ax,
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


def hair_cap_and_tufts(grid, bvh_head, female, hair_mat, seed):
    """Short messy hair (male) or a smooth pulled-back cap (female) over the head grid."""
    psi, zr = azimuth(grid)
    ap = np.abs(psi)
    if female:
        hl = interp_curve([(0, 0.078), (0.45, 0.074), (0.85, 0.06), (1.15, 0.04), (1.4, 0.012), (1.6, 0.018),
                           (1.85, 0.0), (2.3, -0.05), (math.pi, -0.085)], ap)
    else:
        hl = interp_curve([(0, 0.074), (0.5, 0.07), (0.85, 0.058), (1.15, 0.035), (1.4, 0.006), (1.6, 0.018),
                           (1.85, 0.0), (2.3, -0.05), (math.pi, -0.08)], ap)
    mask = smoothstep(hl - 0.002, hl + 0.014, zr)
    thick = 0.016 if not female else 0.009
    top = smoothstep(0.03, 0.11, zr)
    th = thick * (0.55 + 0.45 * top) if not female else thick * (0.7 + 0.5 * top * smoothstep(0, 2.5, ap))
    cap = head_shell(grid, mask, th, hair_mat)
    parts = [cap]
    capbvh = make_bvh(cap)
    if not female:
        rng = random.Random(seed)
        n = 0
        tries = 0
        while n < 48 and tries < 4000:
            tries += 1
            i = rng.randrange(grid.shape[0])
            j = rng.randrange(grid.shape[1])
            if mask[i, j] < 0.9 or zr[i, j] < 0.03 or abs(psi[i, j]) > 2.3:
                continue
            p0 = grid[i, j]
            radial = norm(p0 - (HEAD_C + np.array([0, 0, -0.01])))
            z = zr[i, j]
            ps = psi[i, j]
            fwd = abs(ps) < 0.75 and z > 0.045
            side_dir = np.array([math.copysign(1.0, p0[0]), 0.0, 0.0])
            if fwd:
                flow = np.array([0.0, -0.25, 1.0]) + side_dir * 0.3
            elif z > 0.075:
                flow = np.array([0.0, 0.8, 0.45]) + side_dir * 0.5
            else:
                flow = np.array([0.0, 0.5, -0.9]) + side_dir * 0.1
            jit = np.array([rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)]) * 0.3
            d = norm(radial * (0.55 if fwd else 0.3) + norm(flow) + jit)
            length = (0.03 if z > 0.07 else 0.022) * rng.uniform(0.8, 1.2)
            if fwd:
                length *= 1.2
            base = project(capbvh, p0 + radial * 0.06, -radial, 0.0) - radial * 0.008
            w0 = rng.uniform(0.018, 0.026)
            pts, rad = [], []
            for k, u in enumerate(np.linspace(0, 1, 4)):
                pts.append(base + d * length * u + radial * (0.004 * u))
                rad.append((max(0.001, w0 * 0.4 * (1 - u) ** 0.8), max(0.0015, w0 * (1 - u * 0.92) ** 0.7)))
            parts.append(tube(pts, rad, 6, radial, hair_mat, cap=True))
            n += 1
    for p in parts:
        p.skin("head")
    return parts, mask


def beard_parts(grid, mat):
    psi, zr = azimuth(grid)
    ap = np.abs(psi)
    x = grid[..., 0]
    bt = interp_curve([(0, -0.057), (0.22, -0.055), (0.42, -0.047), (0.7, -0.032), (1.0, -0.016),
                       (1.25, 0.002), (1.42, 0.018), (1.55, 0.024)], ap)
    m = 1 - smoothstep(bt - 0.004, bt + 0.006, zr)
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
    p = loft(rings, segs, True, True, mat, True, 0.01, 0.0)
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


def trouser_leg(keys, mat, segs=20):
    rings = limb_rings(leg_chain_points("l"), keys, (0, -1, 0))
    p = loft(rings, segs, True, True, mat, True)
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
    foot = loft(rings, 20, True, True, main, True, 0.006, 0.008)

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
    parts.append(loft(srings, 20, True, True, sole_mat, True, 0.004, 0.006))
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
    return project(bvh, (x, -0.6, z), (0, 1, 0), -off)


def back_point(bvh, x, z, off=0.0):
    return project(bvh, (x, 0.6, z), (0, -1, 0), -off)


def on_surface(bvh, p, d, off):
    """Project p along d onto the body, then push it outward by off."""
    d = norm(d)
    hit = project(bvh, np.asarray(p) - d * 0.4, d, 0.0)
    return hit - d * off


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
    for ps in np.linspace(gap, 2 * math.pi - gap, 30):
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
    parts += goggles(gbvh, 0.072, 0.03, "leather_dark", "goggle_dark", "lens_orange", "metal_dark")
    parts.append(neck_part(0.056, 0.058, "skin_m"))

    # --- jacket torso -----------------------------------------------------------------------------
    torso, tgrid = grid_loft(jacket_torso_keys(), 26, 40, mat="jacket_olive", ef=2.3, eb=2.5)
    torso.skin(torso_weights)
    parts.append(torso)
    tbvh = make_bvh(torso)
    parts += collar(0.012, 1.455, 0.088, 0.074, 0.072, 0.32, "jacket_olive", "orange")
    # zip + piping down the front
    zpts = [front_point(tbvh, 0.0, z, 0.002) for z in np.linspace(0.99, 1.47, 9)]
    zip_strip = strap(zpts, [np.array([0, -1.0, 0])] * len(zpts), 0.005, 0.0018, "metal_dark")
    parts.append(zip_strip.skin(torso_weights))
    for sx in (1, -1):
        pp = [front_point(tbvh, sx * 0.012, z, 0.003) for z in np.linspace(0.99, 1.47, 9)]
        parts.append(tube(pp, 0.0034, 6, (1, 0, 0), "orange").skin(torso_weights))
        # slanted hand-warmer pocket edges
        pk = [front_point(tbvh, sx * (0.07 + 0.065 * u), 1.02 + 0.13 * u, 0.003) for u in np.linspace(0, 1, 5)]
        parts.append(tube(pk, 0.0038, 6, (0, -1, 0), "orange").skin(torso_weights))
    # chest patch (left chest)
    parts.append(patch(tbvh, (0.092, 0, 1.335), (0, 1, 0), (0.027, 0.004, 0.017), "orange").skin(torso_weights))

    # --- sleeves, arm bands, shoulder patch, gloves ----------------------------------------------
    left = []
    left.append(sleeve([(-0.18, 0.04, 0.04), (-0.03, 0.052, 0.05), (0.25, 0.06, 0.058), (0.55, 0.06, 0.058),
                        (0.85, 0.056, 0.054), (1.0, 0.054, 0.053), (1.35, 0.053, 0.051), (1.7, 0.049, 0.048),
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
    parts.append(pel.skin(pelvis_weights))
    belt, _ = grid_loft([(0.982, 0.18, -0.131, 0.13, 0.0), (1.036, 0.18, -0.131, 0.13, 0.0)], 2, 40,
                        mat="leather_dark", ef=3, eb=3)
    parts.append(belt.skin("hips"))
    bb = make_bvh(belt)
    parts.append(patch(bb, (0, 0, 1.009), (0, 1, 0), (0.03, 0.005, 0.024), "metal").skin("hips"))
    parts.append(patch(bb, (0, 0, 1.009), (0, 1, 0), (0.017, 0.004, 0.012), "leather_dark", lift=0.005).skin("hips"))
    for (x, y, z, h, m, d) in [(-0.135, -0.09, 0.985, (0.034, 0.024, 0.045), "backpack_olive", (0.4, 1, 0)),
                               (0.165, -0.03, 0.98, (0.03, 0.026, 0.05), "leather_mid", (-1, 0.3, 0)),
                               (0.12, 0.1, 0.985, (0.028, 0.02, 0.04), "backpack_olive", (-0.6, -1, 0))]:
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
    bp.append(soft_box(body_c, (0.165, 0.085, 0.2), e=5, e2=4, segs=24, rings=12, mat="backpack_olive"))
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
    bp.append(box((0, 0.320, 1.068), (0.16, 0.003, 0.01), mat="orange"))
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
        p.v += np.array([0.0, -0.024, 0.0])   # snug against the jacket
        p.skin("chest")
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


def assemble_character(name):
    """Build armature + one skinned mesh for a character in the current scene."""
    arm = build_armature("Armature")
    part = merge(CHARACTERS[name]())
    ob = build_mesh_object(name, part, arm)
    return arm, ob, part


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
        export_image_format="AUTO", export_extras=False, export_cameras=False, export_lights=False)
