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
    "denim": ("#41608a", 0.85, 0.0),
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
                t = thick * (0.7 + 0.5 * top * sstep(0, 2.5, abs(ps)))
            else:
                t = thick * (0.55 + 0.45 * top)
            off = 0.0006 + t * sstep(0.0, 0.35, u) ** 0.7
            grid[i, j] = hit + norm(hit - o) * off
    # rows run upward and the azimuth runs counter-clockwise seen from above: quads face outward
    return grid_part(grid, False, True, mat, bulge1=0.004)


def hair_cap_and_tufts(grid, bvh_head, female, hair_mat, seed):
    """Short messy hair (male) or a smooth pulled-back cap (female) over the head grid."""
    psi, zr = azimuth(grid)
    ap = np.abs(psi)
    hl = hairline(ap, female)
    mask = smoothstep(hl - 0.006, hl + 0.022, zr)
    thick = 0.016 if not female else 0.009
    cap = hair_cap_mesh(bvh_head, female, thick, hair_mat)
    parts = [cap]
    capbvh = make_bvh(cap)
    if not female:
        rng = random.Random(seed)
        n = 0
        tries = 0
        while n < 38 and tries < 4000:
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


def trouser_leg(keys, mat, segs=16):
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
    return project(bvh, (x, -0.6, z), (0, 1, 0), -off)


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
    parts += goggles(gbvh, 0.072, 0.03, "leather_dark", "goggle_dark", "lens_orange", "metal_dark")
    parts.append(neck_part(0.056, 0.058, "skin_m"))

    # --- jacket torso -----------------------------------------------------------------------------
    torso, tgrid = grid_loft(jacket_torso_keys(), 22, 36, mat="jacket_olive", ef=2.3, eb=2.5)
    torso.skin(torso_weights)
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


def braid(start, ctrl, mat, tie_mat, n_lobes=11):
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
        taper = 1.0 - 0.45 * (k / n_lobes)
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
    tip = [c, c + t * 0.025, c + t * 0.05]
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
    bun = HEAD_C + np.array([0.0, 0.098, -0.045])
    parts.append(ellipsoid(bun, (0.03, 0.02, 0.028), segs=12, rings=8, mat="hair_f").skin("head"))
    br = braid(bun + np.array([0, 0.006, -0.01]),
               [bun + np.array([-0.004, 0.024, -0.06]), np.array([-0.008, 0.15, 1.47]),
                np.array([-0.012, 0.158, 1.42]), np.array([-0.014, 0.16, 1.385])], "hair_f", "leather_dark",
               n_lobes=9)
    for p in br:
        p.skin(lambda v: {"head": smoothstep(1.5, 1.57, v[:, 2]),
                          "neck": smoothstep(1.43, 1.5, v[:, 2]) * (1 - smoothstep(1.5, 1.57, v[:, 2])),
                          "chest": 1 - smoothstep(1.43, 1.5, v[:, 2])})
    parts += br
    parts += goggles(capbvh, 0.078, 0.035, "leather_mid", "brass", "lens_blue", "brass", tilt=1.1, cup_r=0.02)
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
    parts.append(torso.skin(torso_weights))
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
                        (0.85, 0.048, 0.046), (1.0, 0.047, 0.046), (1.3, 0.047, 0.045), (1.45, 0.046, 0.044)],
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
    parts.append(pel.skin(pelvis_weights))
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
    bp += screwdriver((-0.04, 0.2, 1.3), (-0.08, 0.02, 1.0), "orange")
    bp += screwdriver((-0.075, 0.215, 1.3), (-0.2, 0.05, 1.0), "blue")
    bp += screwdriver((-0.01, 0.185, 1.3), (0.04, -0.05, 1.0), "blue")
    bp += screwdriver((0.12, 0.23, 1.3), (0.2, 0.1, 1.0), "orange")
    bp.append(cylinder((-0.11, 0.21, 1.3), (-0.12, 0.215, 1.47), 0.009, 10, "metal_dark", bevel=0.002))
    for p in bp:
        p.v += np.array([0.0, -0.016, 0.0])   # snug against the jacket
        p.skin("chest")
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


def clip_walk(t):
    return locomotion(t, 1.0, 1.5, 0.56, hips_base=-0.03, bob=0.015, lift=0.1, strike_pitch=10, off_pitch=62,
                      lean=3, arm_swing=18, elbow=16, elbow_extra=12, fingers=20, hips_yaw=6, chest_yaw=8,
                      center_y=0.08, heel_frac=0.12, toe_frac=0.55)


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


def clip_hammer_swing(t):
    h = hold_tool_static()
    up = with_right_arm(h, arm_r=tool_arm(np.array([-0.19, -0.2, 1.42]), d=(0.0, -0.4, 1.0), w=(0.0, 0.85, 0.5)),
                        chest=(-4.0, 0, -6.0), spine=(-2.0, 0, -3.0))
    hit = with_right_arm(h, arm_r=tool_arm(np.array([-0.15, -0.46, 1.12]), d=(0.0, -0.6, -0.8), w=(0.0, -0.85, 0.3)),
                         chest=(8.0, 0, 4.0), spine=(4.0, 0, 2.0))
    return oneshot([(0.0, h), (0.13, up), (0.21, hit), (0.35, h)], t)


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


HEAVY_R = np.array([-0.1, -0.2, 1.3])         # right fist on the sledge handle (rest coords)
HEAVY_AXIS = norm(np.array([-0.2, 0.55, 0.6]))  # handle direction toward the head (over the right shoulder)


def heavy_arms(grip_r, axis, d_hint=(0.0, -1.0, 0.0), spacing=0.24):
    axis = norm(axis)
    d = norm(np.asarray(d_hint) - np.dot(d_hint, axis) * axis)
    grip_l = np.asarray(grip_r) - axis * spacing
    return {"arm_r": arm_ik_from_grip("r", grip_r, d, axis, pole=(-0.7, 0.4, -1.0)),
            "arm_l": arm_ik_from_grip("l", grip_l, d, axis, pole=(0.6, 0.3, -1.0))}


def clip_hold_heavy(t, T=2.0):
    ph = 2 * math.pi * t / T
    s = upper_base(t, T)
    s.update(heavy_arms(HEAVY_R + np.array([0, 0, 0.004 * math.sin(ph)]), HEAVY_AXIS))
    for side in ("l", "r"):
        s[f"fingers_{side}"] = 95.0
        s[f"thumb_{side}"] = 45.0
    s["chest"] = (s["chest"][0], 0, -5.0)
    return s


def clip_sledge_swing(t):
    h = clip_hold_heavy(0.0)
    up = with_right_arm(h, **heavy_arms(np.array([-0.06, 0.0, 1.78]), (0.0, 0.75, 0.2), d_hint=(0, 0, 1)),
                        spine=(-6.0, 0, -4.0), chest=(-8.0, 0, -6.0), head=(-4.0, 0, 0))
    fwd = with_right_arm(h, **heavy_arms(np.array([-0.05, -0.42, 1.55]), (0.0, -0.6, 0.8), d_hint=(0, -0.3, -1)),
                         spine=(6.0, 0, 0.0), chest=(6.0, 0, 0.0))
    hit = with_right_arm(h, **heavy_arms(np.array([-0.04, -0.5, 1.02]), (0.0, -0.95, -0.25), d_hint=(0, 0, -1)),
                         spine=(18.0, 0, 2.0), chest=(12.0, 0, 2.0), neck=(-12.0, 0, 0), head=(-6.0, 0, 0))
    return oneshot([(0.0, h), (0.15, up), (0.23, fwd), (0.29, hit), (0.36, hit), (0.45, h)], t)


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


# name: (function, duration seconds, loops, upper_body_only)
CLIPS = {
    "idle": (clip_idle, 3.0, True, False),
    "walk": (clip_walk, 1.0, True, False),
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
    "hold_gun": (clip_hold_gun, 2.0, True, True),
    "hold_item": (clip_hold_item, 2.0, True, True),
    "hammer_swing": (clip_hammer_swing, 0.35, False, True),
    "sledge_swing": (clip_sledge_swing, 0.45, False, True),
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
}

# the prop each clip shows in the right hand (verification renders and the game's defaults)
CLIP_PROP = {"hold_tool": "hammer", "hammer_swing": "hammer", "tool_use": "lift_remote", "weld": "weld_tool",
             "paint_spray": "paint_tool", "hold_heavy": "sledgehammer", "sledge_swing": "sledgehammer",
             "hold_gun": "potato_blaster", "fire_gun": "potato_blaster", "pour": "water_bucket"}


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


def prop_hammer():
    p = [cylinder((0, -0.095, 0), (0, 0.2, 0), 0.014, 12, "handle_wood", ref=X, bevel=0.003),
         cylinder((0, -0.1, 0), (0, 0.045, 0), 0.017, 12, "rubber", ref=X, bevel=0.004),
         soft_box((0.012, 0.215, 0), (0.06, 0.022, 0.02), axes=(X, Y, Z), e=8, e2=8, segs=12, rings=6,
                  mat="metal_dark"),
         cylinder((0.07, 0.215, 0), (0.085, 0.215, 0), 0.02, 12, "metal", ref=Y, bevel=0.003)]
    claw = [np.array([-0.045, 0.215, 0]), np.array([-0.07, 0.205, 0]), np.array([-0.09, 0.185, 0]),
            np.array([-0.098, 0.165, 0])]
    for dz in (-0.008, 0.008):
        p.append(tube([c + np.array([0, 0, dz]) for c in claw], [(0.011, 0.006), (0.009, 0.005), (0.007, 0.004),
                                                                  (0.003, 0.002)], 8, Z, "metal_dark"))
    p.append(box((0, 0.11, 0.0), (0.016, 0.006, 0.0145), mat="orange"))
    return p


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
    p = [cylinder((0, -0.38, 0), (0, 0.43, 0), 0.016, 12, "handle_wood", ref=X, bevel=0.004),
         cylinder((0, -0.39, 0), (0, -0.25, 0), 0.019, 12, "rubber", ref=X, bevel=0.005),
         soft_box((0, 0.46, 0), (0.1, 0.042, 0.042), axes=(X, Y, Z), e=7, e2=7, segs=14, rings=8, mat="metal_dark"),
         cylinder((0.1, 0.46, 0), (0.115, 0.46, 0), 0.04, 14, "metal", ref=Y, bevel=0.004),
         cylinder((-0.1, 0.46, 0), (-0.115, 0.46, 0), 0.04, 14, "metal", ref=Y, bevel=0.004),
         box((0, 0.36, 0.0), (0.02, 0.012, 0.02), mat="orange")]
    return p


def prop_connect_tool():
    p = [soft_box((0.0, 0.02, 0.0), (0.022, 0.07, 0.017), axes=(X, Y, Z), e=6, e2=6, segs=14, rings=8,
                  mat="paint_grey"),
         soft_box((0.0, 0.07, 0.0), (0.024, 0.018, 0.019), axes=(X, Y, Z), e=6, e2=6, segs=14, rings=5, mat="orange"),
         soft_box((0.019, 0.035, 0.0), (0.004, 0.022, 0.012), axes=(X, Y, Z), e=6, e2=6, segs=10, rings=5,
                  mat="screen"),
         cylinder((0.0, 0.088, 0), (0.0, 0.15, 0), 0.004, 8, "metal", ref=X),
         cylinder((0.0, 0.15, 0), (0.0, 0.17, 0), 0.0015, 6, "metal", ref=X),
         cylinder((0.0, -0.05, 0), (0.0, -0.065, 0), 0.008, 10, "rubber", ref=X)]
    cable = [np.array([0.0, -0.06, 0]), np.array([0.0, -0.1, 0.0]), np.array([0.03, -0.17, 0.02]),
             np.array([0.08, -0.2, 0.03]), np.array([0.12, -0.17, 0.03])]
    p.append(tube(cable, 0.004, 8, Z, "blue"))
    p.append(cylinder(cable[-1], cable[-1] + np.array([0.02, 0.03, 0]), 0.007, 10, "orange"))
    return p


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


def prop_water_bucket():
    p = []
    rings = []
    for xx, r in ((0.13, 0.105), (0.135, 0.108), (0.33, 0.088)):
        rings.append(dict(c=(xx, 0, 0), a=Y, b=Z, rx=r, ry=r, e=2.0))
    # tangent = Y x Z = X: rings go from the rim (near the hand) to the bottom
    p.append(loft(rings, 24, False, True, "paint_grey", True))
    inner = [dict(c=(0.335, 0, 0), a=Y, b=Z, rx=0.085, ry=0.085), dict(c=(0.14, 0, 0), a=Y, b=Z, rx=0.1, ry=0.1)]
    p.append(loft(inner, 24, False, False, "metal_dark", True))
    p.append(ring_band((0.132, 0, 0), 0.108, 0.108, 0.006, 0.004, "metal", 24, axes=(Y, Z, X)))
    p.append(ring_band((0.24, 0, 0), 0.1, 0.1, 0.008, 0.003, "orange", 24, axes=(Y, Z, X)))
    p.append(loft([dict(c=(0.16, 0, 0), a=Y, b=Z, rx=0.096, ry=0.096), dict(c=(0.162, 0, 0), a=Y, b=Z, rx=0.096,
                                                                                 ry=0.096)],
                  24, True, True, "water", True))
    bail = [np.array([0.138, -0.107, 0]), np.array([0.07, -0.08, 0]), np.array([0.01, -0.035, 0]),
            np.array([-0.004, 0.0, 0]), np.array([0.01, 0.035, 0]), np.array([0.07, 0.08, 0]),
            np.array([0.138, 0.107, 0])]
    p.append(tube(bail, 0.0028, 6, Z, "metal"))
    p.append(cylinder((-0.002, -0.035, 0), (-0.002, 0.035, 0), 0.011, 10, "rubber", ref=X))
    return p


PROPS = {"hammer": prop_hammer, "lift_remote": prop_lift_remote, "weld_tool": prop_weld_tool,
         "paint_tool": prop_paint_tool, "sledgehammer": prop_sledgehammer, "connect_tool": prop_connect_tool,
         "potato_blaster": prop_potato_blaster, "water_bucket": prop_water_bucket}


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
    objs = []
    for name in PROPS:
        ob, part = build_prop(name)
        objs.append(ob)
    path = OUT_DIR / "hand_tools.glb"
    export_glb(path, objs, animations=False)
    print(f"{path.name}: {len(objs)} props")


def main(argv):
    what = set(argv) or {"characters", "animations", "props"}
    if "characters" in what:
        for name in CHARACTERS:
            export_character(name)
    if "animations" in what:
        export_animations()
    if "props" in what:
        export_props()


if __name__ == "__main__":
    main([a for a in sys.argv[1:] if not a.startswith("-")])
