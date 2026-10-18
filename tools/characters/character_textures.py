"""Procedural textures for the Mechanica characters and hand tools (used by build_characters.py).

Each textured mesh is unwrapped into UV atlases (one per texture group, Blender's Smart UV Project). Every atlas
texel is then rasterised back to the rest-pose surface point it covers and shaded in numpy from that point's
material name: fabric weave (canvas, denim twill, knit), leather grain, stitching and quilting, scuffs, worn
edges, dirt, metal wear and paint chips. The shading is baked into a baseColor, a tangent-space normal and a
metallic-roughness image per atlas, which the glTF export embeds in the .glb.

Because the patterns are functions of the 3D surface point, they run on across UV seams. All noise comes from
seeded tables, so the images are the same on every run.
"""

from __future__ import annotations

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


# =====================================================================================================
# Noise
# =====================================================================================================

class Noise:
    """Seeded 3D value noise in [-1, 1] (quintic interpolation on an integer lattice)."""

    def __init__(self, seed):
        rng = np.random.default_rng(seed)
        perm = rng.permutation(256).astype(np.int64)
        self.perm = np.concatenate([perm, perm])
        self.vals = rng.uniform(-1.0, 1.0, 256)

    def __call__(self, p):
        p = np.asarray(p, float)
        pi = np.floor(p)
        f = p - pi
        u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0)
        i = pi.astype(np.int64) & 255
        j = (i + 1) & 255
        perm, vals = self.perm, self.vals

        def h(x, y, z):
            return vals[perm[perm[perm[x] + y] + z]]

        x0, y0, z0 = i[:, 0], i[:, 1], i[:, 2]
        x1, y1, z1 = j[:, 0], j[:, 1], j[:, 2]
        ux, uy, uz = u[:, 0], u[:, 1], u[:, 2]
        a = h(x0, y0, z0) + (h(x1, y0, z0) - h(x0, y0, z0)) * ux
        b = h(x0, y1, z0) + (h(x1, y1, z0) - h(x0, y1, z0)) * ux
        c = h(x0, y0, z1) + (h(x1, y0, z1) - h(x0, y0, z1)) * ux
        d = h(x0, y1, z1) + (h(x1, y1, z1) - h(x0, y1, z1)) * ux
        e = a + (b - a) * uy
        g = c + (d - c) * uy
        return e + (g - e) * uz

    def fbm(self, p, octaves=4, lacunarity=2.0, gain=0.5):
        p = np.asarray(p, float)
        out = np.zeros(len(p))
        amp, tot = 1.0, 0.0
        for k in range(octaves):
            out += amp * self(p * lacunarity ** k + 17.31 * k)
            tot += amp
            amp *= gain
        return out / tot


def sstep(e0, e1, x):
    t = np.clip((np.asarray(x, float) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def mix(a, b, t):
    t = np.asarray(t, float)
    if t.ndim == 1 and np.ndim(a) == 2:
        t = t[:, None]
    return a + (b - a) * t


def luma(c):
    return c @ np.array([0.299, 0.587, 0.114])


def triplanar_coords(P, N):
    """The three projection planes' 2D coordinates and blend weights for triplanar patterns."""
    w = np.abs(N) ** 4
    w /= w.sum(axis=1, keepdims=True) + 1e-12
    planes = [(P[:, 1], P[:, 2]), (P[:, 0], P[:, 2]), (P[:, 0], P[:, 1])]
    return planes, w


# =====================================================================================================
# Material kinds and looks
# =====================================================================================================
# name: (kind, stitch thread colour or None). Colours and base roughness/metal come from PALETTE.

KINDS = {
    "skin_m": "skin", "skin_f": "skin", "lips_m": "lips", "lips_f": "lips",
    "hair_m": "hair", "hair_f": "hair", "brow": "hair",
    "eye_white": "eye", "iris_m": "iris", "iris_f": "iris", "pupil": "eye",
    "orange": "webbing", "orange_dark": "webbing", "patch": "canvas",
    "metal": "metal", "metal_dark": "metal", "brass": "brass",
    "leather_dark": "leather", "leather_mid": "leather", "boot_tan": "leather", "boot_brown": "leather",
    "glove_dark": "leather", "glove_brown": "leather",
    "sole": "rubber", "rubber": "rubber", "lace": "cord", "goggle_dark": "rubber",
    "lens_orange": "lens", "lens_blue": "lens", "screen": "lens", "water": "water",
    "jacket_olive": "canvas", "jacket_olive_dark": "canvas", "trouser_olive": "canvas",
    "backpack_olive": "canvas", "canvas": "canvas", "canvas_dark": "canvas", "lining": "canvas",
    "void": "void",
    "shirt": "knit",
    "denim": "denim", "denim_light": "denim", "denim_dark": "denim",
    "paint_olive": "painted", "paint_grey": "painted", "tool_orange": "plastic", "tool_blue": "plastic",
    "blue": "painted", "handle_wood": "wood",
    "gunmetal": "metal", "steel_shaft": "metal", "bolt_steel": "metal", "paint_yellow": "painted",
    "grip_rubber": "knurl", "potato": "plain", "default": "plain",
    "bucket_teal": "plastic", "bucket_inner": "plastic", "bucket_band": "plastic", "bucket_orange": "plastic",
    "bucket_wire": "metal", "bucket_pin": "metal", "bucket_water": "water",
    "pipe_steel": "metal", "plate_steel": "metal", "tape_brown": "tape", "hazard_band": "hazard", "rust_sheet": "rust",
}

THREAD = {
    "jacket_olive": "#80865a", "jacket_olive_dark": "#7a8056", "trouser_olive": "#7a8054",
    "backpack_olive": "#747b58", "denim": "#c8873e", "denim_light": "#c8873e", "denim_dark": "#c07e38",
    "canvas": "#b98552", "canvas_dark": "#ad7d50", "patch": "#c4935e", "leather_dark": "#a9845c",
    "leather_mid": "#c9a171", "boot_tan": "#d9b07a", "boot_brown": "#c8a06e", "glove_dark": "#6e6458",
    "glove_brown": "#a07a52", "orange": "#f0b070", "orange_dark": "#e09a58", "lining": "#3c4030",
}

FABRIC = ("canvas", "denim", "knit", "webbing")
SOFT = ("canvas", "denim", "knit", "webbing", "leather")

DUST = hexrgb("#7d6a52")
MUD = hexrgb("#4a3b2b")
BARE_METAL = hexrgb("#a7a6a0")


# Flat sheets cut to an outline in the x-y plane of their part (the Refine Tool's blade), by material name: lists of
# (x, y) loops. A sheet has no vertices inside its faces, so its worn edges come from the distance to these outlines.
SHEET_OUTLINES: dict[str, list] = {}


def outline_distance(xy, loops, chunk=32768):
    """Distance in the plane from each point of xy (n, 2) to the nearest edge of the loops."""
    a = np.array([p for loop in loops for p in loop], float)
    b = np.array([p for loop in loops for p in loop[1:] + loop[:1]], float)
    ab = b - a
    ll = np.maximum(np.sum(ab * ab, axis=1), 1e-12)
    out = np.empty(len(xy))
    for i in range(0, len(xy), chunk):
        p = xy[i:i + chunk, None, :]
        t = np.clip(np.sum((p - a) * ab, axis=2) / ll, 0.0, 1.0)
        out[i:i + chunk] = np.sqrt(np.min(np.sum((p - a - t[..., None] * ab) ** 2, axis=2), axis=1))
    return out


class Ctx:
    """Everything known about a batch of texels."""

    def __init__(self, **kw):
        self.__dict__.update(kw)


# =====================================================================================================
# Surface patterns
# =====================================================================================================

def weave_plain(u, v, period):
    """Plain weave height in 0..1 (threads over/under on a checker of thread crossings)."""
    fu, fv = u / period, v / period
    par = (np.floor(fu * 2) + np.floor(fv * 2)) % 2
    warp = np.sin(math.pi * fu * 2) ** 2
    weft = np.sin(math.pi * fv * 2) ** 2
    return np.where(par > 0, 0.35 + 0.65 * warp * (0.6 + 0.4 * weft), 0.35 + 0.65 * weft * (0.6 + 0.4 * warp))


def tri_pattern(c, fn):
    planes, w = triplanar_coords(c.P, c.N)
    out = np.zeros(len(c.P))
    for k, (u, v) in enumerate(planes):
        out += w[:, k] * fn(u, v)
    return out


def pattern(c, kind, nz):
    """(albedo multiplier (n,), height in m (n,), roughness offset (n,), colour shift (n,3) or None)."""
    n = len(c.P)
    P = c.P
    one, zero = np.ones(n), np.zeros(n)
    if kind == "canvas":
        h = tri_pattern(c, lambda u, v: weave_plain(u, v, max(0.0034, 4.5 * TEXEL[0])))
        fib = nz[0].fbm(P * 900.0, 2)
        slub = nz[1](P * np.array([60.0, 60.0, 600.0]))
        return 0.95 + 0.07 * h + 0.035 * fib + 0.025 * slub, 0.00011 * h, 0.04 * (1 - h), None
    if kind == "webbing":
        ribs = tri_pattern(c, lambda u, v: np.sin(math.pi * (u + v * 0.15) / max(0.0016, 2.4 * TEXEL[0])) ** 2)
        if c.loft is not None:
            ok = ~np.isnan(c.loft[:, 3])
            across = np.where(ok, c.loft[:, 3], 0.0)
            r2 = np.sin(math.pi * across * 9.0) ** 2
            ribs = np.where(ok, 0.5 * ribs + 0.5 * r2, ribs)
        fib = nz[0].fbm(P * 800.0, 2)
        return 0.94 + 0.08 * ribs + 0.03 * fib, 0.0001 * ribs, zero, None
    if kind == "denim":
        tw = tri_pattern(c, lambda u, v: np.sin(math.pi * (u + v) / max(0.0026, 3.2 * TEXEL[0])) ** 2)
        slub = 0.5 + 0.5 * nz[1](P * np.array([90.0, 90.0, 900.0]))
        heather = nz[0].fbm(P * 700.0, 2)
        mult = 0.84 + 0.2 * tw * (0.55 + 0.45 * slub) + 0.05 * heather
        shift = np.outer(0.05 * tw * slub, np.array([0.7, 0.85, 1.0]))
        return mult, 0.00012 * tw, 0.03 * tw, shift
    if kind == "knit":
        r = tri_pattern(c, lambda u, v: np.sin(math.pi * u / max(0.0018, 2.6 * TEXEL[0])) ** 2)
        return 0.95 + 0.06 * r + 0.03 * nz[0].fbm(P * 700.0, 2), 0.00008 * r, zero, None
    if kind == "leather":
        grain = nz[0].fbm(P * 650.0, 3)
        peb = np.abs(nz[1](P * 1500.0))
        crease = sstep(0.05, 0.0, np.abs(nz[2](P * np.array([45.0, 45.0, 260.0]))))
        mult = 0.93 + 0.07 * grain - 0.05 * peb - 0.12 * crease
        return mult, -0.00006 * (1 - peb) - 0.0002 * crease, 0.06 * peb, None
    if kind in ("skin", "lips"):
        mot = nz[0].fbm(P * 55.0, 3)
        fine = nz[1].fbm(P * 420.0, 2)
        return 1.0 + 0.035 * mot + 0.018 * fine, 0.00003 * fine, 0.04 * fine, None
    if kind == "hair":
        s = hair_streaks(c, nz)
        return 0.7 + 0.5 * s, 0.00035 * s, -0.12 * s, None
    if kind == "iris":
        mult = one.copy()
        if c.box is not None:
            ok = ~np.isnan(c.box[:, 0])
            lx, ly = np.nan_to_num(c.box[:, 0]), np.nan_to_num(c.box[:, 1])
            r = np.hypot(lx, ly)
            ang = np.arctan2(ly, lx)
            fib = nz[0](np.stack([np.cos(ang) * 9, np.sin(ang) * 9, r * 3], axis=1))
            ring = sstep(0.72, 0.95, r)
            coll = np.exp(-((r - 0.5) / 0.12) ** 2)
            mult = np.where(ok, (0.85 + 0.25 * fib) * (1 - 0.5 * ring) * (1 + 0.35 * coll), 1.0)
        return mult, zero, zero, None
    if kind in ("metal", "brass"):
        brushed = tri_pattern(c, lambda u, v: nz[0](np.stack([u * 40.0, v * 2500.0, u * 0 + 3.3], axis=1)))
        mot = nz[1].fbm(P * 40.0, 3)
        return 0.93 + 0.05 * brushed + 0.06 * mot, 0.00002 * brushed, 0.08 * mot, None
    if kind in ("rubber",):
        g = nz[0].fbm(P * 500.0, 2)
        h = 0.00005 * g
        # tread: lugs on the underside of soles
        down = c.N[:, 2] < -0.55
        if down.any():
            lug = (np.sin(math.pi * P[:, 1] / 0.011) ** 2 > 0.35) & (np.sin(math.pi * P[:, 0] / 0.017 + P[:, 1] * 40) ** 2 > 0.15)
            h = np.where(down, h - 0.0012 * (~lug), h)
            g = np.where(down, g - 0.6 * (~lug), g)
        return 0.95 + 0.06 * g, h, 0.03 * g, None
    if kind == "knurl":
        # diamond knurling on a round grip: lofts know their angle and the distance along them
        g = nz[0].fbm(P * 500.0, 2)
        if c.loft is None:
            return 0.95 + 0.06 * g, 0.00005 * g, 0.03 * g, None
        ok = ~np.isnan(c.loft[:, 0])
        along = np.nan_to_num(c.loft[:, 0])
        arc = np.arctan2(np.nan_to_num(c.loft[:, 2]), np.nan_to_num(c.loft[:, 1])) * 0.0218
        period = max(0.0032, 3.0 * TEXEL[0])
        a = np.abs(np.sin(math.pi * (arc + along) / period))
        b = np.abs(np.sin(math.pi * (arc - along) / period))
        ridge = np.where(ok, sstep(0.25, 0.7, np.minimum(a, b)), 0.5)
        return 0.82 + 0.22 * ridge + 0.05 * g, 0.00028 * ridge, 0.06 * (1 - ridge), None
    if kind in ("tape", "hazard"):
        # wound round a loft: tape in overlapping turns, the hazard band in diagonal yellow and black stripes
        fib = nz[0].fbm(P * 900.0, 2)
        if c.loft is None:
            return 0.95 + 0.05 * fib, zero, zero, None
        ok = ~np.isnan(c.loft[:, 0])
        along = np.nan_to_num(c.loft[:, 0])
        turn = np.arctan2(np.nan_to_num(c.loft[:, 2]), np.nan_to_num(c.loft[:, 1])) / (2 * math.pi)
        if kind == "hazard":
            s = (along / 0.0095 + turn * 6.0) % 1.0
            black = np.where(ok, sstep(0.46, 0.5, s) * sstep(1.0, 0.96, s), 0.0)
            return (1.0 - 0.91 * black) * (0.97 + 0.04 * fib), 0.00002 * fib, 0.12 * black, None
        s = (along / 0.0068 + turn) % 1.0
        lap = np.where(ok, sstep(0.14, 0.0, s), 0.0)
        ridge = np.where(ok, sstep(0.0, 0.2, s) * (0.7 + 0.3 * np.sin(math.pi * s)), 0.5)
        fray = sstep(0.3, 0.7, nz[1](P * np.array([300.0, 2500.0, 300.0])))
        return 0.88 + 0.14 * ridge - 0.3 * lap + 0.06 * fib - 0.05 * fray, 0.00025 * ridge, 0.05 * lap, None
    if kind == "rust":
        # rusted sheet: blotchy orange and brown, pitted, flaking
        blot = nz[0].fbm(P * 45.0, 4)
        pits = sstep(0.5, 0.85, 0.5 + 0.5 * nz[1].fbm(P * 260.0, 2))
        flake = nz[2].fbm(P * 140.0, 3)
        mult = 0.88 + 0.24 * blot - 0.22 * pits + 0.06 * flake
        shift = np.outer(0.07 * blot, np.array([1.0, 0.45, 0.05])) - np.outer(0.05 * sstep(0.0, -0.5, blot), np.ones(3))
        return mult, -0.00014 * pits + 0.00006 * flake, 0.1 * pits, shift
    if kind == "cord":
        tw = tri_pattern(c, lambda u, v: np.sin(math.pi * (u + v) / max(0.0022, 3.0 * TEXEL[0])) ** 2)
        return 0.9 + 0.15 * tw, 0.00015 * tw, zero, None
    if kind in ("lens", "water"):
        z = c.N[:, 2]
        return 0.85 + 0.3 * np.clip(z, -1, 1) + 0.04 * nz[0](P * 300.0), zero, 0.03 * np.abs(nz[1](P * 200.0)), None
    if kind in ("painted", "plastic"):
        g = nz[0].fbm(P * 300.0, 2)
        return 0.97 + 0.04 * g, 0.00002 * g, 0.04 * g, None
    if kind == "wood":
        ax = c.loft[:, 0] if c.loft is not None else P[:, 1]
        ax = np.nan_to_num(ax)
        warp = nz[0].fbm(P * 30.0, 3)
        grain = np.sin((P[:, 0] * 37 + P[:, 2] * 41 + warp * 2.2) * 9.0) ** 2
        fine = nz[1](np.stack([ax * 30, P[:, 0] * 900, P[:, 2] * 900], axis=1))
        return 0.88 + 0.12 * grain + 0.05 * fine, 0.00006 * grain, 0.05 * grain, None
    return 1.0 + 0.02 * nz[0].fbm(P * 100.0, 2), zero, zero, None


def hair_streaks(c, nz):
    """Strand streaks in 0..1 along the hair's flow: lock lofts run along their length, braid lobes along
    their long axis, the scalp cap away from the crown."""
    P = c.P
    n = len(P)
    crown = np.array([0.0, 0.05, 1.79])
    d = P - crown
    ang = np.arctan2(d[:, 0], -d[:, 1])
    rad = np.linalg.norm(d, axis=1)
    q = np.stack([np.cos(ang) * 30.0, np.sin(ang) * 30.0, rad * 9.0], axis=1)
    s = 0.5 + 0.5 * (0.7 * nz[3](q) + 0.3 * nz[4](q * np.array([2.3, 2.3, 1.7])))
    if c.loft is not None:
        ok = ~np.isnan(c.loft[:, 0])
        L = np.nan_to_num(c.loft)
        th = np.arctan2(L[:, 2], L[:, 1])
        q2 = np.stack([np.cos(th) * 2.2 + c.pid * 3.17, np.sin(th) * 2.2, L[:, 0] * 14.0], axis=1)
        s2 = 0.5 + 0.5 * (0.65 * nz[3](q2 * np.array([1, 1, 1])) + 0.35 * nz[4](q2 * np.array([2.7, 2.7, 1.3])))
        s = np.where(ok, s2, s)
    if c.box is not None:
        ok = ~np.isnan(c.box[:, 0])
        B = np.nan_to_num(c.box)
        q3 = np.stack([B[:, 0] * 7.0 + c.pid * 1.31, B[:, 1] * 7.0, B[:, 2] * 1.2], axis=1)
        s3 = 0.5 + 0.5 * (0.65 * nz[3](q3) + 0.35 * nz[4](q3 * np.array([2.5, 2.5, 1.5])))
        # the lobes of a braid read as twisted strands: darker toward the lobe's ends
        s3 = s3 * (1.0 - 0.45 * sstep(0.55, 1.0, np.abs(B[:, 2])))
        s = np.where(ok, s3, s)
    return np.clip(s, 0, 1) * np.ones(n)


# =====================================================================================================
# Stitches, seams and quilting
# =====================================================================================================

TEXEL = [0.001]     # 3D size of a texel of the atlas being baked (m); fine detail never gets smaller


def stitch_line(dist, along, width=None, dash=None, duty=0.62):
    """Mask of a dashed stitch line: dist = distance from the line (m), along = coordinate along it (m)."""
    width = width or max(0.00055, 0.5 * TEXEL[0])
    dash = dash or max(0.0042, 3.4 * TEXEL[0])
    line = sstep(width * 1.6, width * 0.6, np.abs(dist))
    fr = (along / dash) % 1.0
    dashm = sstep(0.0, 0.08, fr) * sstep(duty, duty - 0.08, fr)
    return line * dashm


def box_stitches(c, ok_kind):
    """Stitching inset from the rounded border of the broad faces of soft boxes (pockets, patches, flaps,
    pads, bag panels) and flat boxes (webbing, patches). Returns (mask, edge distance m or inf)."""
    n = len(c.P)
    mask = np.zeros(n)
    edge = np.full(n, np.inf)
    if c.box is None:
        return mask, edge
    B = c.box
    ok = ~np.isnan(B[:, 0]) & ok_kind
    if not ok.any():
        return mask, edge
    idx = np.nonzero(ok)[0]
    L = B[idx, :3]
    H = B[idx, 3:6]
    e = B[idx, 6]
    k = np.argmin(H, axis=1)
    rows = np.arange(len(idx))
    ij = np.array([[1, 2], [0, 2], [0, 1]])[k]
    li, lj = np.abs(L[rows, ij[:, 0]]), np.abs(L[rows, ij[:, 1]])
    hi, hj = H[rows, ij[:, 0]], H[rows, ij[:, 1]]
    lk = np.abs(L[rows, k])
    ee = np.clip(e, 2.0, 12.0)
    pi_, pj_ = li ** ee, lj ** ee
    r = (pi_ + pj_) ** (1.0 / ee)
    wsum = pi_ + pj_ + 1e-9
    d = (1.0 - np.minimum(r, 1.0)) * (hi * pi_ + hj * pj_) / wsum
    flat = e > 15
    d = np.where(flat, np.minimum((1 - li) * hi, (1 - lj) * hj), d)
    along = np.where(pi_ > pj_, L[rows, ij[:, 1]] * hj, L[rows, ij[:, 0]] * hi)
    small = np.minimum(hi, hj)
    inset = np.where(small > 0.02, 0.0042, 0.0026)
    face = sstep(0.55, 0.8, lk) if True else 1.0
    m = stitch_line(d - inset, along) * face * (small > 0.008)
    mask[idx] = m
    edge[idx] = np.where(lk > 0.5, d, np.inf)
    return mask, edge


def loft_stitches(c, ok_kind):
    """Two stitch rows along the long edges of straps and bands (flat lofts)."""
    n = len(c.P)
    mask = np.zeros(n)
    if c.loft is None:
        return mask
    Lf = c.loft
    ok = ~np.isnan(Lf[:, 3]) & ok_kind
    if not ok.any():
        return mask
    idx = np.nonzero(ok)[0]
    across = np.abs(Lf[idx, 3])
    broad = sstep(0.35, 0.6, np.abs(Lf[idx, 1]))
    mask[idx] = stitch_line((across - 0.74) * 0.012, Lf[idx, 0]) * broad
    return mask


def chain_param(P, pts):
    """Arc length along a polyline and the radial vector from it, for points P."""
    pts = [np.asarray(p, float) for p in pts]
    best = np.full(len(P), np.inf)
    s_out = np.zeros(len(P))
    rad = np.zeros_like(P)
    cum = 0.0
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        ab = b - a
        ln = np.linalg.norm(ab)
        t = np.clip(((P - a) @ ab) / (ln * ln), -0.3 if i == 0 else 0.0, 1.3 if i == len(pts) - 2 else 1.0)
        q = a + t[:, None] * ab
        r = P - q
        dd = np.linalg.norm(r, axis=1)
        m = dd < best
        best = np.where(m, dd, best)
        s_out = np.where(m, cum + t * ln, s_out)
        rad = np.where(m[:, None], r, rad)
        cum += ln
    return s_out, rad


def seams(c, kind_of, names):
    """Position-based seams per character: (stitch mask, thread colour key per texel or '', quilt height)."""
    n = len(c.P)
    P = c.P
    mask = np.zeros(n)
    quilt = np.zeros(n)
    tag = c.tag
    if c.style not in ("male", "female"):
        return mask, quilt
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    mat = c.matname
    sx = np.where(x >= 0, 1.0, -1.0)
    B = c.bones
    # limbs: arc length and radial direction around the arm / leg chain
    torso = tag == "torso"
    sleeve = tag == "sleeve"
    leg = tag == "leg"
    pelvis = tag == "pelvis"
    if sleeve.any():
        i = np.nonzero(sleeve)[0]
        s, r = np.zeros(len(i)), np.zeros((len(i), 3))
        for side, sg in (("l", 1), ("r", -1)):
            m = sx[i] == sg
            if m.any():
                pts = [B[f"shoulder_{side}"], B[f"upper_arm_{side}"], B[f"forearm_{side}"], B[f"hand_{side}"]]
                s[m], r[m] = chain_param(P[i[m]], pts)
        rn = r / (np.linalg.norm(r, axis=1, keepdims=True) + 1e-9)
        # seam along the underside/back of the arm
        under = rn @ np.array([0.0, 0.45, -0.89])
        rr = np.linalg.norm(r, axis=1)
        dseam = np.arccos(np.clip(under, -1, 1)) * rr
        mask[i] = np.maximum(mask[i], stitch_line(dseam - 0.002, s) + stitch_line(dseam + 0.002, s) * 0.0)
        if c.style == "male":
            # padded sleeves: rings of quilting down the arm
            q = ((s - 0.12) / 0.075) % 1.0
            on = (s > 0.16) & (s < 0.58)
            quilt[i] = np.where(on, np.sqrt(np.clip(np.sin(math.pi * q), 0, 1)), 1.0)
            dq = (np.minimum(q, 1 - q)) * 0.075
            mask[i] = np.maximum(mask[i], np.where(on, stitch_line(dq, np.arctan2(rn[:, 1], rn[:, 2]) * rr), 0))
        else:
            # denim: double rows of contrast stitching along the seam
            mask[i] = np.maximum(mask[i], stitch_line(dseam - 0.0055, s))
    if torso.any():
        i = np.nonzero(torso)[0]
        xi, yi, zi = x[i], y[i], z[i]
        mi = mat[i]
        side = (np.abs(xi) > 0.12)
        dside = np.abs(yi - 0.004)
        m = stitch_line(dside - 0.0018, zi) * side
        if c.style == "male":
            q = ((zi - 1.035) / 0.068) % 1.0
            on = (zi > 1.04) & (zi < 1.43) & (np.abs(xi) > 0.02)
            quilt[i] = np.where(on, np.sqrt(np.clip(np.sin(math.pi * q), 0, 1)), 1.0)
            dq = np.minimum(q, 1 - q) * 0.068
            ang = np.arctan2(xi, -yi) * 0.17
            m = np.maximum(m, np.where(on, stitch_line(dq, ang), 0.0))
            # hem band
            m = np.maximum(m, stitch_line(zi - 1.026, ang))
        else:
            # yokes: across the back and the shoulders of the denim jacket, double rows
            yoke_b = 1.37
            back = (yi > 0.02)
            ang = np.arctan2(xi, -yi) * 0.16
            m = np.maximum(m, stitch_line(zi - yoke_b, ang) * back + stitch_line(zi - yoke_b - 0.006, ang) * back)
            front = (yi < -0.02) & (mi == "denim")
            m = np.maximum(m, (stitch_line(zi - 1.395, ang) + stitch_line(zi - 1.389, ang)) * front)
            # placket rows along the open front
            from_edge = np.abs(np.abs(xi) - (0.052 + 0.018 * sstep(1.45, 1.05, zi))) - 0.012
            m = np.maximum(m, stitch_line(from_edge, zi) * front)
            # hem band and the bib's top edge
            m = np.maximum(m, stitch_line(zi - 1.03, ang) * (mi == "denim"))
            bib = (mi == "canvas") & (yi < 0)
            m = np.maximum(m, (stitch_line(zi - 1.316, xi) + stitch_line(np.abs(xi) - 0.04, zi) * (zi > 1.07)) * bib)
        mask[i] = np.maximum(mask[i], m)
    if leg.any():
        i = np.nonzero(leg)[0]
        s, r = np.zeros(len(i)), np.zeros((len(i), 3))
        for side, sg in (("l", 1), ("r", -1)):
            m = sx[i] == sg
            if m.any():
                pts = [B[f"thigh_{side}"], B[f"shin_{side}"], B[f"foot_{side}"]]
                s[m], r[m] = chain_param(P[i[m]], pts)
        rr = np.linalg.norm(r, axis=1)
        rn = r / (rr[:, None] + 1e-9)
        outer = np.arccos(np.clip(rn[:, 0] * sx[i], -1, 1)) * rr
        inner = np.arccos(np.clip(-rn[:, 0] * sx[i], -1, 1)) * rr
        m = stitch_line(outer - 0.002, s) + stitch_line(outer + 0.002, s)
        m = np.maximum(m, stitch_line(inner, s))
        # hem: a double row near the bottom of the leg
        m = np.maximum(m, stitch_line(z[i] - 0.255, np.arctan2(rn[:, 1], rn[:, 0]) * rr) * (rr > 0.04))
        mask[i] = np.maximum(mask[i], np.clip(m, 0, 1))
    if pelvis.any():
        i = np.nonzero(pelvis)[0]
        xi, yi, zi = x[i], y[i], z[i]
        front = yi < -0.05
        fly = stitch_line(np.where(zi > 0.9, np.abs(xi - 0.028), np.hypot(xi - 0.0, zi - 0.9) - 0.028), zi + xi)
        back = yi > 0.05
        yoke = stitch_line(zi - (0.985 - 0.04 * (1 - np.abs(xi) / 0.17).clip(0, 1)), xi)
        mask[i] = np.maximum(mask[i], fly * front * (zi > 0.875) + yoke * back)
    return mask, quilt


def boot_seams(c):
    """Toe cap and heel counter outlines and the welt stitch above the sole."""
    P = c.P
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    n = len(P)
    m = np.zeros(n)
    sc = 0.94 if c.style == "female" else 1.0
    boot = c.is_boot
    if not boot.any():
        return m
    toe_line = y - (-0.148 * sc)
    m += stitch_line(toe_line - 0.006, x * 0.7 + z) * (z < 0.13)
    m += stitch_line(toe_line - 0.010, x * 0.7 + z) * (z < 0.13)
    heel = (y > 0.04)
    m += stitch_line(z - 0.11, y + x) * heel * 0.0 + stitch_line(z - 0.116, x + y) * heel
    m += stitch_line(z - 0.042, x * 0.6 + y) * (z < 0.06)
    return np.clip(m, 0, 1) * boot


# =====================================================================================================
# Shading
# =====================================================================================================

def shade(c, palette):
    """Albedo (sRGB 0..1), roughness, metallic and height (m) for a batch of texels."""
    n = len(c.P)
    P, N = c.P, c.N
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    names = c.names
    matname = np.array(names, dtype=object)[c.mat]
    c.matname = matname
    kind = np.array([KINDS.get(nm, "plain") for nm in names], dtype=object)[c.mat]
    base = np.array([hexrgb(palette[nm][0]) for nm in names])[c.mat]
    rough = np.array([palette[nm][1] for nm in names])[c.mat].astype(float)
    metal = np.array([palette[nm][2] for nm in names])[c.mat].astype(float)
    # orange on boots is leather (toe caps, heels), the rest of the orange on a character is webbing/cloth
    if c.style in ("male", "female"):
        kind = np.where((kind == "webbing") & (z < 0.27), "leather", kind)
    nz = [Noise(1000 + c.seed * 17 + k) for k in range(6)]
    alb = base.copy()
    height = np.zeros(n)
    for kd in sorted(set(kind)):
        m = kind == kd
        sub = sub_ctx(c, m)
        mult, h, dr, shift = pattern(sub, kd, nz)
        alb[m] = alb[m] * np.asarray(mult)[:, None]
        if shift is not None:
            alb[m] = alb[m] + shift
        height[m] = h
        rough[m] = rough[m] + dr

    # large-scale colour variation: per part and across the surface
    var = Noise(77 + c.seed).fbm(P * 7.0, 3)
    pid_j = (np.sin(c.pid * 12.9898 + c.seed) * 43758.5453) % 1.0 - 0.5
    soft = np.isin(kind, SOFT)
    alb *= (1.0 + (0.07 * var + 0.05 * pid_j) * soft)[:, None]
    hue = Noise(91 + c.seed).fbm(P * 4.0, 2)
    alb[soft] = alb[soft] * (1 + np.outer(0.05 * hue[soft], np.array([1.0, 0.2, -0.8])))

    # stitches and quilting
    ok_kind = np.isin(kind, ("canvas", "denim", "webbing", "leather"))
    bs, box_edge = box_stitches(c, ok_kind)
    ls = loft_stitches(c, ok_kind)
    ss, quilt = seams(c, kind, names)
    c.is_boot = np.isin(matname, ("boot_tan", "boot_brown", "leather_mid", "orange")) & (z < 0.27) & (kind == "leather")
    st = np.clip(np.maximum.reduce([bs, ls, ss * ok_kind, boot_seams(c)]), 0, 1)
    thread = np.array([hexrgb(THREAD.get(nm, "#8a7a62")) for nm in names])[c.mat]
    lum_t = 0.9 + 0.2 * Noise(55 + c.seed)(P * 900.0)
    alb = mix(alb * (1 - 0.18 * sstep(0.0, 0.3, st))[:, None], thread * lum_t[:, None], st * 0.85)
    height += 0.00018 * st
    rough -= 0.05 * st
    # quilting: puffy channels between stitched lines
    qd = (1 - quilt) * (kind == "canvas")
    height += -0.0012 * qd
    alb *= (1 - 0.1 * qd)[:, None]

    # ambient occlusion (per vertex, interpolated)
    ao = np.clip(c.ao, 0, 1)
    alb *= (0.5 + 0.5 * ao ** 0.9)[:, None]
    cav = sstep(0.75, 0.35, ao)

    # worn edges
    cvx = np.nan_to_num(c.cvx)
    edge_d = np.minimum(np.nan_to_num(c.border, nan=np.inf, posinf=np.inf), box_edge)
    wear = np.clip(sstep(28.0, 110.0, cvx) + sstep(0.0035, 0.0, edge_d), 0, 1)
    wn = 0.5 + 0.5 * Noise(31 + c.seed).fbm(P * 140.0, 3)
    wear = np.clip(wear * (0.35 + 1.1 * wn) - 0.2, 0, 1) * (kind != "rust")
    fab = np.isin(kind, FABRIC)
    lth = kind == "leather"
    met = np.isin(kind, ("metal", "brass"))
    paint = np.isin(kind, ("painted", "hazard"))
    rusty = kind == "rust"
    # fabric frays lighter and dustier at edges
    worn_fab = alb * 1.18 + 0.05
    worn_fab = mix(worn_fab, np.repeat(luma(worn_fab)[:, None], 3, 1), 0.3)
    alb = np.where(fab[:, None], mix(alb, worn_fab, wear * 0.8), alb)
    # leather: scuffed lighter, smoother at edges
    worn_l = alb * 1.35 + np.array([0.06, 0.04, 0.02])
    alb = np.where(lth[:, None], mix(alb, worn_l, wear * 0.75), alb)
    rough = np.where(lth, rough - 0.15 * wear, rough)
    # metal: bright, polished edges and grime in the cavities
    alb = np.where(met[:, None], mix(alb, alb * 1.35 + 0.08, wear), alb)
    rough = np.where(met, rough - 0.18 * wear + 0.25 * cav, rough)
    # rusted sheet is ground back to bright steel along its cut edges and teeth
    if rusty.any():
        edge = np.full(n, np.inf)
        for nm, loops in SHEET_OUTLINES.items():
            m = rusty & (matname == nm)
            if m.any():
                edge[m] = outline_distance(P[m][:, :2], loops)
        ground = np.clip(sstep(0.0028, 0.0, edge) * (0.55 + 0.9 * wn) - 0.1, 0, 1) * rusty
        alb = mix(alb, BARE_METAL * (0.75 + 0.2 * wn)[:, None], ground * 0.85)
        metal = metal + (1 - metal) * ground
        rough = rough - 0.35 * ground
    # paint chips to bare metal at the edges
    chip = np.clip((wear - 0.25) * 3.0, 0, 1) * paint
    alb = mix(alb, BARE_METAL * (0.85 + 0.15 * wn)[:, None], chip)
    metal = np.where(paint, metal + (1 - metal) * chip, metal)
    rough = np.where(paint, rough * (1 - chip) + 0.35 * chip, rough)
    # rust and tarnish
    rust_n = Noise(41 + c.seed).fbm(P * 90.0, 3)
    rust = sstep(0.25, 0.55, rust_n) * (0.4 + 0.6 * cav) * np.isin(matname, ("metal_dark", "pipe_steel", "plate_steel")) * met
    alb = mix(alb, hexrgb("#6b3d22"), rust * 0.75)
    metal = np.where(met, metal * (1 - rust), metal)
    rough = rough + 0.4 * rust
    tarn = sstep(0.0, 0.5, rust_n + cav * 0.6) * (kind == "brass")
    alb = mix(alb, hexrgb("#4f4a2c"), tarn * 0.55)
    rough = rough + 0.25 * tarn

    # scratches and scuffs on leather, boots, pads and metal
    scr = sstep(0.025, 0.0, np.abs(Noise(61 + c.seed)(P * np.array([260.0, 30.0, 260.0]) + 3.1)))
    scr = scr * sstep(0.1, 0.5, Noise(62 + c.seed)(P * 25.0))
    scuffable = lth | met | paint | (matname == "orange")
    alb = np.where(scuffable[:, None], mix(alb, alb * 1.4 + 0.05, scr * 0.6), alb)
    height -= 0.00008 * scr * scuffable

    # dirt
    dirt = np.zeros(n)
    dn = 0.5 + 0.5 * Noise(71 + c.seed).fbm(P * 22.0, 4)
    splat = sstep(0.2, 0.55, Noise(72 + c.seed).fbm(P * 60.0, 3))
    if c.style in ("male", "female"):
        foot = (z < 0.3) & ~np.isin(kind, ("skin",))
        dirt += foot * (sstep(0.17, 0.0, z) * (0.45 + 0.55 * dn) + 0.25 * splat * sstep(0.28, 0.1, z))
        dirt += foot * 0.35 * sstep(-0.09, -0.2, y) * sstep(0.12, 0.03, z)
        legs = (c.tag == "leg")
        dirt += legs * sstep(0.45, 0.13, z) * (0.25 + 0.5 * dn) * 0.8
        dirt += legs * 0.35 * splat * sstep(0.5, 0.25, z)
        for sg in (1, -1):
            knee = np.array([sg * 0.095, -0.075, 0.5])
            dk = np.linalg.norm(P - knee, axis=1)
            dirt += sstep(0.1, 0.03, dk) * (0.3 + 0.5 * dn) * (y < 0)
        gl = np.isin(matname, ("glove_dark", "glove_brown"))
        dirt += gl * (0.2 + 0.35 * dn)
        gear = c.group_name == "gear"
        if gear:
            zb = np.min(z) if len(z) else 0
            dirt += np.isin(kind, FABRIC + ("leather",)) * (0.12 + 0.5 * sstep(1.12, 1.03, z) * dn)
        dirt += cav * 0.25 * soft
        dirt += 0.12 * dn * fab
    else:
        dirt += cav * 0.35 + 0.1 * dn
        dirt *= ~np.isin(kind, ("lens", "water"))
    dirt = np.clip(dirt, 0, 1) * ~np.isin(kind, ("eye", "iris", "lens", "hair", "lips"))
    dirt_col = mix(np.tile(DUST, (n, 1)), np.tile(MUD, (n, 1)), sstep(0.12, 0.0, z) if c.style != "props" else 0.0)
    a_d = np.clip(dirt * 0.75, 0, 0.85)
    alb = mix(alb, dirt_col * (0.85 + 0.3 * dn)[:, None], a_d)
    rough = rough + (0.95 - rough) * a_d
    metal = metal * (1 - a_d)

    # skin warmth on the face: cheeks, nose and ears
    if c.style in ("male", "female"):
        sk = kind == "skin"
        if sk.any():
            hc = np.array([0.0, 0.004, 1.645])
            warm = np.zeros(n)
            for sxx in (1, -1):
                warm += np.exp(-np.sum(((P - (hc + np.array([sxx * 0.042, -0.06, -0.035]))) / 0.025) ** 2, axis=1))
                warm += 0.6 * np.exp(-np.sum(((P - (hc + np.array([sxx * 0.07, 0.015, -0.02]))) / 0.02) ** 2, axis=1))
            warm += 0.8 * np.exp(-np.sum(((P - (hc + np.array([0.0, -0.085, -0.045]))) / 0.015) ** 2, axis=1))
            alb = np.where(sk[:, None], mix(alb, alb * np.array([1.08, 0.9, 0.86]), np.clip(warm, 0, 1) * 0.6), alb)
            if c.style == "male":
                # stubble shadow around the beard
                beard = np.exp(-((z - 1.575) / 0.03) ** 2) * (y < -0.02) * sstep(0.03, 0.06, np.abs(x) + 0.03)
                alb = np.where(sk[:, None], mix(alb, alb * 0.82, np.clip(beard, 0, 1) * 0.35), alb)

    # void (the inside of a cut sleeve): flat black that reflects nothing, not even its own dirt or stitches
    vd = kind == "void"
    alb[vd], rough[vd], metal[vd], height[vd] = base[vd], 1.0, 1.0, 0.0
    rough = np.clip(rough, 0.04, 1.0)
    metal = np.clip(metal, 0.0, 1.0)
    alb = np.clip(alb, 0.0, 1.0)
    return alb, rough, metal, height


def sub_ctx(c, m):
    d = {}
    for k, v in c.__dict__.items():
        if isinstance(v, np.ndarray) and v.ndim >= 1 and len(v) == len(m) and k not in ("names",):
            d[k] = v[m]
        else:
            d[k] = v
    return Ctx(**d)


# =====================================================================================================
# Per-vertex helpers: ambient occlusion and convexity
# =====================================================================================================

def _hemisphere_dirs(k):
    """k fixed cosine-weighted directions around +Z (Fibonacci spiral)."""
    out = []
    ga = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(k):
        r = math.sqrt((i + 0.5) / k)
        phi = i * ga
        out.append((r * math.cos(phi), r * math.sin(phi), math.sqrt(max(0.0, 1.0 - r * r))))
    return np.array(out)


def vertex_ao(verts, normals, tris, max_dist=0.1, rays=28):
    bvh = BVHTree.FromPolygons([Vector(v) for v in verts], [tuple(int(i) for i in t) for t in tris])
    dirs = _hemisphere_dirs(rays)
    ao = np.ones(len(verts))
    for vi in range(len(verts)):
        nrm = normals[vi]
        a = np.array([1.0, 0, 0]) if abs(nrm[0]) < 0.9 else np.array([0, 1.0, 0])
        t = np.cross(nrm, a)
        t /= np.linalg.norm(t) + 1e-12
        b = np.cross(nrm, t)
        # rotate the spiral per vertex (deterministically) so neighbouring vertices sample differently
        rot = (vi * 0.61803398875) % 1.0 * 2 * math.pi
        cr, sr = math.cos(rot), math.sin(rot)
        o = Vector(verts[vi] + nrm * 0.0015)
        occ = 0.0
        for dx, dy, dz in dirs:
            ux, uy = dx * cr - dy * sr, dx * sr + dy * cr
            d = t * ux + b * uy + nrm * dz
            hit = bvh.ray_cast(o, Vector(d), max_dist)
            if hit[0] is not None:
                occ += 1.0 - hit[3] / max_dist
        ao[vi] = 1.0 - occ / rays
    return ao


def vertex_convexity(verts, normals, faces):
    """Mean-curvature-like convexity per vertex (1/m, > 0 on convex edges)."""
    n = len(verts)
    edges = set()
    for f in faces:
        for k in range(len(f)):
            a, b = f[k], f[(k + 1) % len(f)]
            edges.add((min(a, b), max(a, b)))
    e = np.array(sorted(edges), dtype=np.int64)
    acc = np.zeros((n, 3))
    cnt = np.zeros(n)
    l2 = np.zeros(n)
    d = verts[e[:, 1]] - verts[e[:, 0]]
    ln2 = np.sum(d * d, axis=1)
    np.add.at(acc, e[:, 0], verts[e[:, 1]])
    np.add.at(acc, e[:, 1], verts[e[:, 0]])
    np.add.at(cnt, e[:, 0], 1)
    np.add.at(cnt, e[:, 1], 1)
    np.add.at(l2, e[:, 0], ln2)
    np.add.at(l2, e[:, 1], ln2)
    cnt = np.maximum(cnt, 1)
    lap = acc / cnt[:, None] - verts
    return -2.0 * np.sum(lap * normals, axis=1) / np.maximum(l2 / cnt, 1e-10)


# =====================================================================================================
# UV atlases and rasterisation
# =====================================================================================================

def smart_uv(objs, face_sel, margin):
    """Smart UV Project the selected faces (face_sel[obj] bool arrays) of objs into one 0..1 layout."""
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="DESELECT")
    for o in objs:
        bm = bmesh.from_edit_mesh(o.data)
        bm.faces.ensure_lookup_table()
        sel = face_sel[o.name]
        for f in bm.faces:
            if sel[f.index]:
                f.select_set(True)
        bm.select_flush_mode()
        bmesh.update_edit_mesh(o.data)
    bpy.ops.uv.smart_project(angle_limit=math.radians(62), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def rasterize(tri_uv, size):
    """tri_uv (T, 3, 2) in pixels -> tri id per texel (-1 empty) and barycentrics."""
    tri = np.full((size, size), -1, np.int32)
    bary = np.zeros((size, size, 3))
    for t in range(len(tri_uv)):
        a, b, c = tri_uv[t]
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        x0 = max(int(math.floor(min(a[0], b[0], c[0]) - 0.5)), 0)
        x1 = min(int(math.ceil(max(a[0], b[0], c[0]) + 0.5)), size - 1)
        y0 = max(int(math.floor(min(a[1], b[1], c[1]) - 0.5)), 0)
        y1 = min(int(math.ceil(max(a[1], b[1], c[1]) + 0.5)), size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1) + 0.5
        X, Y = np.meshgrid(xs, ys)
        w0 = ((b[1] - c[1]) * (X - c[0]) + (c[0] - b[0]) * (Y - c[1])) / den
        w1 = ((c[1] - a[1]) * (X - c[0]) + (a[0] - c[0]) * (Y - c[1])) / den
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        yy, xx = np.nonzero(inside)
        tri[yy + y0, xx + x0] = t
        bary[yy + y0, xx + x0] = np.stack([w0[inside], w1[inside], w2[inside]], axis=1)
    return tri, bary


def grow(tri, bary, tri_uv, rings):
    """Extend triangles a few texels past island borders (extrapolated barycentrics), so bilinear
    filtering and mipmaps at the borders still sample the right surface."""
    size = tri.shape[0]
    for _ in range(rings):
        empty = tri < 0
        src = np.full_like(tri, -1)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            sh = np.roll(np.roll(tri, dy, axis=0), dx, axis=1)
            take = empty & (src < 0) & (sh >= 0)
            src[take] = sh[take]
        yy, xx = np.nonzero(src >= 0)
        if len(yy) == 0:
            break
        t = src[yy, xx]
        a, b, c = tri_uv[t, 0], tri_uv[t, 1], tri_uv[t, 2]
        X, Y = xx + 0.5, yy + 0.5
        den = (b[:, 1] - c[:, 1]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 1] - c[:, 1])
        den = np.where(np.abs(den) < 1e-12, 1e-12, den)
        w0 = ((b[:, 1] - c[:, 1]) * (X - c[:, 0]) + (c[:, 0] - b[:, 0]) * (Y - c[:, 1])) / den
        w1 = ((c[:, 1] - a[:, 1]) * (X - c[:, 0]) + (a[:, 0] - c[:, 0]) * (Y - c[:, 1])) / den
        w = np.stack([w0, w1, 1 - w0 - w1], axis=1)
        w = np.clip(w, -0.6, 1.6)
        w /= w.sum(axis=1, keepdims=True)
        tri[yy, xx] = t
        bary[yy, xx] = w
    return tri, bary


def fill_empty(img, covered, iters=12):
    """Push colours outward into empty texels (for mip levels), then fill the rest with the mean."""
    img = img.copy()
    cov = covered.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(cov.shape)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh = np.roll(np.roll(img, dy, axis=0), dx, axis=1)
            sc = np.roll(np.roll(cov, dy, axis=0), dx, axis=1)
            acc += sh * sc[..., None]
            cnt += sc
        new = (~cov) & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]
        cov = cov | new
    if (~cov).any():
        img[~cov] = img[cov].mean(axis=0) if cov.any() else 0.5
    return img


def normal_from_height(H, covered, texel_m, strength=1.0):
    Hf = fill_empty(H[..., None], covered, 4)[..., 0]
    gx = (np.roll(Hf, -1, axis=1) - np.roll(Hf, 1, axis=1)) / (2 * texel_m)
    gy = (np.roll(Hf, -1, axis=0) - np.roll(Hf, 1, axis=0)) / (2 * texel_m)
    nrm = np.stack([-gx * strength, -gy * strength, np.ones_like(Hf)], axis=-1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    return nrm * 0.5 + 0.5


# =====================================================================================================
# Baking a group
# =====================================================================================================

class MeshData:
    """Flat arrays for one Blender object plus its source part's per-vertex/per-face data."""

    def __init__(self, ob, verts, attrs, pid, tags, ao, cvx):
        me = ob.data
        self.ob = ob
        nl = len(me.loops)
        self.loop_v = np.zeros(nl, np.int64)
        me.loops.foreach_get("vertex_index", self.loop_v)
        uv = np.zeros(nl * 2)
        me.uv_layers[0].data.foreach_get("uv", uv)
        self.uv = uv.reshape(-1, 2)
        cn = np.zeros(nl * 3)
        me.corner_normals.foreach_get("vector", cn)
        self.cn = cn.reshape(-1, 3)
        npoly = len(me.polygons)
        self.ls = np.zeros(npoly, np.int64)
        self.lt = np.zeros(npoly, np.int64)
        me.polygons.foreach_get("loop_start", self.ls)
        me.polygons.foreach_get("loop_total", self.lt)
        self.pmat = np.zeros(npoly, np.int64)
        me.polygons.foreach_get("material_index", self.pmat)
        sm = np.zeros(npoly, bool)
        me.polygons.foreach_get("use_smooth", sm)
        self.smooth = sm
        self.verts = verts
        self.attrs = attrs
        self.pid = np.asarray(pid)
        self.tags = np.asarray(tags, dtype=object)
        self.ao = ao
        self.cvx = cvx

    def triangles(self, polys):
        """Fan-triangulate the given polygons: (loop triples (T,3), polygon of each)."""
        L, owner = [], []
        for p in polys:
            s, t = self.ls[p], self.lt[p]
            for k in range(1, t - 1):
                L.append((s, s + k, s + k + 1))
                owner.append(p)
        return np.array(L, np.int64).reshape(-1, 3), np.array(owner, np.int64)


def bake_group(md_list, polys_list, size, names_list, palette, style, seed, group_name, bones):
    """Rasterise and shade one atlas from (MeshData, polygons) pairs. Returns RGBA float images."""
    tri_uv, tri_loops, tri_poly, tri_obj = [], [], [], []
    for oi, (md, polys) in enumerate(zip(md_list, polys_list)):
        L, owner = md.triangles(polys)
        if len(L) == 0:
            continue
        tri_uv.append(md.uv[L] * size)
        tri_loops.append(L)
        tri_poly.append(owner)
        tri_obj.append(np.full(len(L), oi))
    tri_uv = np.concatenate(tri_uv)
    tri_loops = np.concatenate(tri_loops)
    tri_poly = np.concatenate(tri_poly)
    tri_obj = np.concatenate(tri_obj)
    tri, bary = rasterize(tri_uv, size)
    covered = tri >= 0
    tri, bary = grow(tri, bary, tri_uv, 3)
    shaded = tri >= 0
    yy, xx = np.nonzero(shaded)
    t = tri[yy, xx]
    w = bary[yy, xx]
    # gather per texel
    n = len(t)
    P = np.zeros((n, 3))
    Nn = np.zeros((n, 3))
    ao = np.zeros(n)
    cvx = np.zeros(n)
    attrs = {"box": np.full((n, 7), np.nan), "loft": np.full((n, 4), np.nan)}
    mat = np.zeros(n, np.int64)
    pid = np.zeros(n)
    tag = np.empty(n, dtype=object)
    smooth = np.ones(n, bool)
    border = np.full(n, np.inf)
    all_names = []
    name_index = {}
    for oi, md in enumerate(md_list):
        m = tri_obj[t] == oi
        if not m.any():
            continue
        tl = tri_loops[t[m]]
        vv = md.loop_v[tl]
        wm = w[m]
        P[m] = np.einsum("nk,nkd->nd", wm, md.verts[vv])
        Nn[m] = np.einsum("nk,nkd->nd", wm, md.cn[tl])
        ao[m] = np.einsum("nk,nk->n", wm, md.ao[vv])
        cvx[m] = np.einsum("nk,nk->n", wm, md.cvx[vv])
        for k in attrs:
            if k in md.attrs:
                attrs[k][m] = np.einsum("nk,nkd->nd", wm, md.attrs[k][vv])
        poly = tri_poly[t[m]]
        remap = []
        for nm in names_list[oi]:
            if nm not in name_index:
                name_index[nm] = len(all_names)
                all_names.append(nm)
            remap.append(name_index[nm])
        mat[m] = np.array(remap)[md.pmat[poly]]
        pid[m] = md.pid[poly] + oi * 1000
        tag[m] = md.tags[poly]
        smooth[m] = md.smooth[poly]
        # distance to the border of flat polygons (boxes): edge wear and stitching on hard boxes
        fm = np.nonzero(m & ~smooth)[0] if (~md.smooth[poly]).any() else []
        if len(fm):
            fpoly = tri_poly[t[fm]]
            for p in np.unique(fpoly):
                sel = fm[fpoly == p]
                s0, tt = md.ls[p], md.lt[p]
                pv = md.verts[md.loop_v[s0:s0 + tt]]
                dmin = np.full(len(sel), np.inf)
                for k in range(tt):
                    a, b = pv[k], pv[(k + 1) % tt]
                    ab = b - a
                    q = np.clip(((P[sel] - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
                    dmin = np.minimum(dmin, np.linalg.norm(P[sel] - (a + q[:, None] * ab), axis=1))
                border[sel] = dmin
    Nn /= np.linalg.norm(Nn, axis=1, keepdims=True) + 1e-12
    pal = dict(palette)
    ctx = Ctx(P=P, N=Nn, ao=ao, cvx=cvx, box=attrs["box"], loft=attrs["loft"], mat=mat, names=all_names,
              pid=pid, tag=tag, border=border, style=style, seed=seed, group_name=group_name, bones=bones)
    # 3D size of a texel: area of the surface over its UV area
    area3 = 0.0
    areauv = 0.0
    for oi, (md, polys) in enumerate(zip(md_list, polys_list)):
        L, _ = md.triangles(polys)
        if len(L) == 0:
            continue
        v = md.verts[md.loop_v[L]]
        area3 += 0.5 * np.linalg.norm(np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0]), axis=1).sum()
        u = md.uv[L] * size
        areauv += 0.5 * np.abs((u[:, 1, 0] - u[:, 0, 0]) * (u[:, 2, 1] - u[:, 0, 1]) -
                               (u[:, 2, 0] - u[:, 0, 0]) * (u[:, 1, 1] - u[:, 0, 1])).sum()
    texel_m = math.sqrt(area3 / max(areauv, 1e-9))
    TEXEL[0] = texel_m
    alb, rough, metal, height = shade(ctx, pal)
    A = np.zeros((size, size, 3))
    R = np.zeros((size, size, 3))
    Hm = np.zeros((size, size))
    A[yy, xx] = alb
    R[yy, xx] = np.stack([np.ones(n), rough, metal], axis=1)
    Hm[yy, xx] = height
    A = fill_empty(A, shaded)
    R = fill_empty(R, shaded)
    Nm = normal_from_height(Hm, shaded, texel_m)
    cov = covered.mean()
    return A, Nm, R, texel_m, cov


# =====================================================================================================
# Blender images and materials
# =====================================================================================================

def make_image(name, rgb, colorspace):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), np.float32)
    # quantise here so the stored bytes do not depend on the float -> byte rounding of the encoder
    px[..., :3] = np.round(np.clip(rgb, 0, 1) * 255.0) / 255.0
    img.pixels.foreach_set(px.ravel())
    img.pack()
    return img


def textured_material(name, A, Nm, R, double_sided=True):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    ia = nt.nodes.new("ShaderNodeTexImage")
    ia.image = make_image(name + "_color", A, "sRGB")
    inn = nt.nodes.new("ShaderNodeTexImage")
    inn.image = make_image(name + "_normal", Nm, "Non-Color")
    ir = nt.nodes.new("ShaderNodeTexImage")
    ir.image = make_image(name + "_mr", R, "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(ia.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(inn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(ir.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    mat.use_backface_culling = not double_sided
    return mat


def apply_group_materials(ob, poly_group, group_mats):
    """Replace the object's materials by one material per texture group."""
    me = ob.data
    order = list(group_mats)
    me.materials.clear()
    for g in order:
        me.materials.append(group_mats[g])
    idx = np.array([order.index(g) for g in poly_group], np.int64)
    me.polygons.foreach_set("material_index", idx)
    me.update()


# =====================================================================================================
# Entry points
# =====================================================================================================

def texture_character(ob, part, names, palette, style, seed, groups, bones, dominant_bone):
    """UV-unwrap and texture a character mesh. groups: {group: atlas size}; dominant_bone(vertex) per
    vertex decides the group of faces without an explicit one."""
    me = ob.data
    verts = part.v
    npoly = len(me.polygons)
    assert npoly == len(part.f), "the Blender mesh lost faces"
    vn = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", vn)
    vn = vn.reshape(-1, 3)
    tris = [t for f in part.f for t in ((f[0], f[k], f[k + 1]) for k in range(1, len(f) - 1))]
    ao = vertex_ao(verts, vn, tris)
    cvx = vertex_convexity(verts, vn, part.f)
    poly_group = []
    for fi, f in enumerate(part.f):
        g = part.g[fi] if part.g is not None else None
        if g is None:
            bones_f = [dominant_bone[v] for v in f]
            b = max(set(bones_f), key=lambda k: (bones_f.count(k), k))
            g = bone_group(b)
        poly_group.append(g)
    poly_group = np.array(poly_group, dtype=object)
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    md = MeshData(ob, verts, part.a, part.pid, part.tag, ao, cvx)
    mats = {}
    stats = {}
    for gi, (g, size) in enumerate(groups.items()):
        sel = poly_group == g
        if not sel.any():
            continue
        smart_uv([ob], {ob.name: sel}, margin=3.0 / size)
        md.uv = _read_uv(ob)
        polys = np.nonzero(sel)[0]
        A, Nm, R, texel_m, cov = bake_group([md], [polys], size, [names], palette, style, seed + gi, g, bones)
        mats[g] = textured_material(f"M_{ob.name}_{g}", A, Nm, R)
        stats[g] = (size, texel_m * 1000.0, cov)
    apply_group_materials(ob, poly_group, mats)
    return stats


def bone_group(bone):
    if bone in ("head", "neck"):
        return "head"
    if bone.startswith(("foot", "toe")):
        return "feet"
    if bone in ("hips", "root") or bone.startswith(("thigh", "shin")):
        return "legs"
    if bone.startswith(("upper_arm", "forearm", "hand", "fingers", "thumb")):
        return "arms"
    return "torso"


def _read_uv(ob):
    uv = np.zeros(len(ob.data.loops) * 2)
    ob.data.uv_layers[0].data.foreach_get("uv", uv)
    return uv.reshape(-1, 2)


def texture_props(objs, parts, names_list, palette, size, seed, group="props", material="M_hand_tools"):
    """One shared atlas for the given hand-tool props."""
    mds = []
    for ob, part in zip(objs, parts):
        me = ob.data
        vn = np.zeros(len(me.vertices) * 3)
        me.vertices.foreach_get("normal", vn)
        vn = vn.reshape(-1, 3)
        tris = [t for f in part.f for t in ((f[0], f[k], f[k + 1]) for k in range(1, len(f) - 1))]
        ao = vertex_ao(part.v, vn, tris, max_dist=0.05)
        cvx = vertex_convexity(part.v, vn, part.f)
        mds.append((ob, part, ao, cvx))
    smart_uv(objs, {ob.name: np.ones(len(ob.data.polygons), bool) for ob in objs}, margin=3.0 / size)
    md_list, polys_list = [], []
    for ob, part, ao, cvx in mds:
        md = MeshData(ob, part.v, part.a, part.pid, part.tag, ao, cvx)
        md_list.append(md)
        polys_list.append(np.arange(len(ob.data.polygons)))
    A, Nm, R, texel_m, cov = bake_group(md_list, polys_list, size, names_list, palette, "props", seed, group, {})
    mat = textured_material(material, A, Nm, R, double_sided=False)
    for ob in objs:
        apply_group_materials(ob, [group] * len(ob.data.polygons), {group: mat})
    return {group: (size, texel_m * 1000.0, cov)}
