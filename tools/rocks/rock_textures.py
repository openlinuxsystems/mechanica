"""Procedural textures for the rock outcrops, chunks and rods (used by build_rocks.py).

Everything is made in numpy from seeded noise, so the output is the same on every run and every machine:

* 3D value noise (``Noise3``) and surface Voronoi (``voronoi_edge``) give *solid* textures: a texel's colour is a
  function of the 3D point of the surface it lands on, so cracks and moss run on across the cube's edges.
* Each mesh gets one box chart per side of its bounding box (``box_charts``), packed into one atlas; a face takes
  the chart of its dominant normal and its UVs are its vertices projected onto that chart. ``bake`` evaluates a
  surface function on every chart's texels and writes the albedo (with a painted light baked in from the height
  field), a tangent-space normal map from the same height and, for the metal-rich variants, a metallic-roughness map.
* ``scrap_stone_tile`` is the 2D, seamlessly tiling Scrap Stone Block texture (periodic Voronoi and FFT noise).

Colours are sRGB in [0, 1]; heights are in metres.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np


def srgb(hexstr):
    h = hexstr.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)



# ---- Palettes ---------------------------------------------------------------------------------------
STONE_LIGHT, STONE_MID, STONE_DARK = srgb("#b9b6ae"), srgb("#8f8a82"), srgb("#64625e")
BEIGE = srgb("#cbbf9f")
CRACK = srgb("#36332f")
MOSS_DARK, MOSS_LIGHT = srgb("#6f8a3a"), srgb("#8a9a45")
ORE_LIGHT, ORE_MID, ORE_DARK = srgb("#80878f"), srgb("#5e646c"), srgb("#41464d")
ORE_CRACK = srgb("#22252a")
STEEL, STEEL_DARK = srgb("#b2b7bc"), srgb("#7b8188")
BRIGHT_STEEL = srgb("#c3c7cb")
RUST, RUST_DARK = srgb("#8f5631"), srgb("#5f3820")
BRUSHED = srgb("#b4b9be")


# =====================================================================================================
# Noise
# =====================================================================================================

class Noise3:
    """Seeded 3D value noise in about [-1, 1], with fBm whose octaves are rotated (no axis-aligned artefacts)."""

    def __init__(self, seed):
        rng = np.random.default_rng(seed)
        p = rng.permutation(256)
        self.perm = np.concatenate([p, p]).astype(np.int64)
        self.vals = rng.uniform(-1.0, 1.0, 256)
        q, _ = np.linalg.qr(rng.standard_normal((3, 3)))
        self.rot = q

    def _h(self, ix, iy, iz):
        p = self.perm
        return self.vals[p[p[p[ix & 255] + (iy & 255)] + (iz & 255)]]

    def __call__(self, pts):
        pts = np.asarray(pts, float)
        f = np.floor(pts)
        i = f.astype(np.int64)
        t = pts - f
        t = t * t * (3.0 - 2.0 * t)
        x, y, z = i[:, 0], i[:, 1], i[:, 2]
        tx, ty, tz = t[:, 0], t[:, 1], t[:, 2]
        c = {}
        for dx in (0, 1):
            for dy in (0, 1):
                for dz in (0, 1):
                    c[dx, dy, dz] = self._h(x + dx, y + dy, z + dz)
        x00 = c[0, 0, 0] + (c[1, 0, 0] - c[0, 0, 0]) * tx
        x10 = c[0, 1, 0] + (c[1, 1, 0] - c[0, 1, 0]) * tx
        x01 = c[0, 0, 1] + (c[1, 0, 1] - c[0, 0, 1]) * tx
        x11 = c[0, 1, 1] + (c[1, 1, 1] - c[0, 1, 1]) * tx
        y0 = x00 + (x10 - x00) * ty
        y1 = x01 + (x11 - x01) * ty
        return y0 + (y1 - y0) * tz

    def fbm(self, pts, freq, octaves=4, gain=0.5, scale=(1.0, 1.0, 1.0)):
        q = np.asarray(pts, float) * np.asarray(scale) * freq
        total, amp, norm = 0.0, 1.0, 0.0
        for k in range(octaves):
            total = total + amp * self(q + 17.31 * k)
            norm += amp
            amp *= gain
            q = (q @ self.rot) * 2.03
        return total / norm


def surface_seeds(half, per_m, seed, jitter=0.38):
    """Jittered-grid points on the surface of a box (half extents), about per_m points per metre each way."""
    rng = np.random.default_rng(seed)
    half = np.asarray(half, float)
    pts = []
    for k in range(3):
        a, b = (k + 1) % 3, (k + 2) % 3
        na = max(1, int(round(2 * half[a] * per_m)))
        nb = max(1, int(round(2 * half[b] * per_m)))
        for sign in (-1, 1):
            for i in range(na):
                for j in range(nb):
                    p = np.zeros(3)
                    p[k] = sign * half[k]
                    p[a] = -half[a] + (i + 0.5 + rng.uniform(-jitter, jitter)) * 2 * half[a] / na
                    p[b] = -half[b] + (j + 0.5 + rng.uniform(-jitter, jitter)) * 2 * half[b] / nb
                    pts.append(p)
    return np.array(pts)


def voronoi_edge(pts, seeds, scale=(1.0, 1.0, 1.0)):
    """Nearest seed index and exact distance to the nearest Voronoi cell boundary (in the scaled metric)."""
    sc = np.asarray(scale, float)
    q_all = np.asarray(pts, float) * sc
    t = seeds * sc
    dist = np.linalg.norm(t[:, None] - t[None], axis=-1)
    np.fill_diagonal(dist, np.inf)
    n = len(q_all)
    idx = np.empty(n, np.int64)
    edge = np.empty(n)
    step = 40000
    for s in range(0, n, step):
        q = q_all[s:s + step]
        d2 = ((q[:, None, :] - t[None]) ** 2).sum(-1)
        i1 = d2.argmin(1)
        r = np.arange(len(q))
        d1 = d2[r, i1]
        e = (d2 - d1[:, None]) / (2.0 * dist[i1])
        e[r, i1] = np.inf
        idx[s:s + step] = i1
        edge[s:s + step] = e.min(1)
    return idx, edge


CHIP = Noise3(9001)


def chip_field(pts):
    """Where the edges are chipped, 0..1. Shared by the geometry (dents) and the texture (fresh pale stone)."""
    return smoothstep(0.12, 0.55, CHIP.fbm(pts, 5.5, octaves=2))


def edge_factor(edge_d, width=0.035):
    return 1.0 - smoothstep(0.0, width, edge_d)


# =====================================================================================================
# Charts and atlas
# =====================================================================================================

@dataclass
class Chart:
    axis: int
    sign: int
    n: np.ndarray
    u: np.ndarray
    v: np.ndarray
    hu: float          # half extent along u (m)
    hv: float
    hn: float          # half extent along the normal
    x0: int = 0        # rect top-left in the atlas (padding included)
    y0: int = 0
    w: int = 0
    h: int = 0


def box_charts(half, width, max_height, pad=6):
    """Six charts (one per box side, u along the longer side, u x v = outward normal) packed on shelves into an
    atlas `width` wide; texel density (px/m) is the largest that fits. Returns charts, (W, H), px_per_m."""
    half = np.asarray(half, float)
    e = np.eye(3)
    charts = []
    for k in range(3):
        for sign in (1, -1):
            n = e[k] * sign
            a, b = [i for i in range(3) if i != k]
            ua = a if half[a] >= half[b] - 1e-9 else b
            u = e[ua]
            v = np.cross(n, u)
            va = int(np.argmax(np.abs(v)))
            charts.append(Chart(k, sign, n, u, v, half[ua], half[va], half[k]))

    order = sorted(range(6), key=lambda i: (-charts[i].hv, -charts[i].hu, i))

    def place(s):
        x = y = row = 0
        pos = {}
        for i in order:
            c = charts[i]
            w = int(math.ceil(2 * c.hu * s)) + 2 * pad
            h = int(math.ceil(2 * c.hv * s)) + 2 * pad
            if w > width:
                return None
            if x + w > width:
                x, y, row = 0, y + row, 0
            pos[i] = (x, y, w, h)
            x += w
            row = max(row, h)
        if y + row > max_height:
            return None
        return pos, y + row

    lo, hi = 1.0, 8192.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if place(mid):
            lo = mid
        else:
            hi = mid
    s = math.floor(lo)
    pos, used = place(s)
    for i, (x, y, w, h) in pos.items():
        charts[i].x0, charts[i].y0, charts[i].w, charts[i].h = x, y, w, h
    height = min(max_height, int(math.ceil(used / 4.0) * 4))
    return charts, (width, height), float(s), pad


def chart_of_normal(charts, nrm):
    k = int(np.argmax(np.abs(nrm)))
    sign = 1 if nrm[k] >= 0 else -1
    for c in charts:
        if c.axis == k and c.sign == sign:
            return c
    raise ValueError(nrm)


def chart_uv(c, p, size, s, pad):
    """UV of point p (metres) on chart c; V up (Blender convention)."""
    W, H = size
    a, b = float(np.dot(p, c.u)), float(np.dot(p, c.v))
    xp = c.x0 + pad + (a + c.hu) * s
    yp = c.y0 + pad + (c.hv - b) * s
    return xp / W, 1.0 - yp / H


def bake(charts, size, s, pad, surface, normal_strength=1.0, light=(0.35, -0.55, 0.9), shade=0.55, rim=None):
    """Evaluates surface(P, n, edge_d) -> dict(albedo, height[, rough, metal]) on every chart's texels.

    The painted light: albedo is brightened where the height field tilts toward `light` and darkened where it tilts
    away (relative to the flat face, so the face itself is left for the game's light), with the bright side
    tinted toward `rim`. Returns albedo, normal (tangent space, OpenGL / Blender: +G = +V) and an optional
    metallic-roughness image (R unused, G roughness, B metallic, as glTF wants)."""
    W, H = size
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    albedo = np.zeros((H, W, 3)) + 0.5
    normal = np.zeros((H, W, 3)) + np.array([0.5, 0.5, 1.0])
    mr = None
    for c in charts:
        jj, ii = np.mgrid[0:c.h, 0:c.w]
        a = (ii + 0.5 - pad) / s - c.hu
        b = c.hv - (jj + 0.5 - pad) / s
        P = (c.n * c.hn)[None, :] + a.reshape(-1, 1) * c.u[None, :] + b.reshape(-1, 1) * c.v[None, :]
        edge_d = np.minimum(c.hu - np.abs(a), c.hv - np.abs(b)).reshape(-1)
        out = surface(P, c.n, edge_d)
        h = out["height"].reshape(c.h, c.w)
        dha = np.gradient(h, axis=1) * s
        dhb = -np.gradient(h, axis=0) * s
        nt = np.stack([-dha * normal_strength, -dhb * normal_strength, np.ones_like(h)], -1)
        nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
        nw = nt[..., :1] * c.u + nt[..., 1:2] * c.v + nt[..., 2:] * c.n
        lit = (nw @ L) - float(c.n @ L)
        col = out["albedo"].reshape(c.h, c.w, 3)
        col = col * (1.0 + shade * lit[..., None])
        if rim is not None:
            col = col + (np.asarray(rim) - col) * np.clip(lit * 2.2, 0, 0.6)[..., None]
        sl = (slice(c.y0, c.y0 + c.h), slice(c.x0, c.x0 + c.w))
        albedo[sl] = col
        normal[sl] = nt * 0.5 + 0.5
        if "rough" in out:
            if mr is None:
                mr = np.zeros((H, W, 3))
                mr[..., 1] = 0.9
            mr[sl + (1,)] = out["rough"].reshape(c.h, c.w)
            mr[sl + (2,)] = out["metal"].reshape(c.h, c.w)
    return np.clip(albedo, 0, 1), normal, mr


# =====================================================================================================
# Surfaces (all take P (N, 3), the face normal n and the distance to the chart edge in metres)
# =====================================================================================================

def _tone(rng_seed, count):
    return np.random.default_rng(rng_seed).uniform(0.0, 1.0, count)


class OutcropStone:
    """The rock cube: big flagstone-like plates parted by dark seams, painted shading, pale chipped edges, moss.

    metal=True gives the metal-rich variant: bluish-grey, steel-silver ore veins and flecks, rust streaks, less moss.
    """

    def __init__(self, half, seed, metal=False):
        self.metal = metal
        self.half = np.asarray(half, float)
        self.seeds = surface_seeds(half, 1.8, seed, jitter=0.42)
        self.fine_seeds = surface_seeds(half, 4.5, seed + 1, jitter=0.45)
        self.tone = _tone(seed + 2, len(self.seeds))
        self.n_warp = [Noise3(seed + 10 + k) for k in range(3)]
        self.n_mottle = Noise3(seed + 20)
        self.n_grain = Noise3(seed + 21)
        self.n_fine = Noise3(seed + 22)
        self.n_moss = Noise3(seed + 23)
        self.n_drip = Noise3(seed + 24)
        self.n_vein = Noise3(seed + 25)
        self.n_fleck = Noise3(seed + 26)
        self.n_rust = Noise3(seed + 27)

    def __call__(self, P, n, edge_d):
        N = len(P)
        warp = np.stack([w.fbm(P, 2.4, octaves=3) for w in self.n_warp], -1) * 0.06
        Pw = P + warp
        cell, e = voronoi_edge(Pw, self.seeds, scale=(1.0, 1.0, 1.5))
        _, ef = voronoi_edge(Pw + warp * 0.6, self.fine_seeds)
        fine_on = smoothstep(0.2, 0.45, self.n_fine.fbm(P, 2.2, octaves=2))
        seam = 1.0 - smoothstep(0.005, 0.016, e)
        groove = 1.0 - smoothstep(0.0, 0.035, e)
        pillow = smoothstep(0.0, 0.09, e)
        fine = (1.0 - smoothstep(0.0, 0.0024, ef)) * fine_on * (1 - seam)
        mottle = self.n_mottle.fbm(P, 2.5, octaves=3)
        grain = self.n_grain.fbm(P, 22.0, octaves=3)
        tone = self.tone[cell]
        z = P[:, 2]
        top = float(n[2] > 0.5)
        bottom = float(n[2] < -0.5)

        if not self.metal:
            lo, hi, dk, crack = STONE_MID, STONE_LIGHT, STONE_DARK, CRACK
        else:
            lo, hi, dk, crack = ORE_MID, ORE_LIGHT, ORE_DARK, ORE_CRACK
        t = np.clip(0.12 + 0.6 * tone + 0.25 * mottle, 0, 1)
        # a few posterised steps of the plate tone: reads as painted rather than photographic
        t = np.round(t * 5) / 5 * 0.6 + t * 0.4
        col = lo[None] + (hi - lo)[None] * t[:, None]
        if not self.metal:
            warm = smoothstep(0.1, 0.6, self.n_mottle.fbm(P + 5.0, 1.6, octaves=2))
            col = col + (BEIGE - col) * (0.22 * warm)[:, None]
        col = col * (1.0 + 0.06 * grain)[:, None]
        # painted ambient occlusion: darker toward each plate's seams and toward the bottom of the cube
        col = col + (dk - col) * (0.55 * (1 - pillow) + 0.12 * smoothstep(0.1, -0.5, z))[:, None]
        if bottom:
            col = col * 0.82
        # pale chipped stone on the cube's edges (matches the dents in the geometry)
        chip = chip_field(P) * edge_factor(edge_d, 0.03)
        edge_wear = edge_factor(edge_d, 0.012) * 0.35
        col = col + ((BEIGE if not self.metal else ORE_LIGHT * 1.15) - col) * np.clip(chip * 0.75 + edge_wear, 0, 0.85)[:, None]
        col = col + (dk * 0.85 - col) * (fine * 0.7)[:, None]
        col = col + (crack - col) * seam[:, None]

        height = 0.004 * pillow - 0.007 * seam - 0.0025 * fine + 0.0006 * grain - 0.003 * chip

        rough = metal = None
        if self.metal:
            vein = 1.0 - np.abs(self.n_vein.fbm(P, 3.2, octaves=3))
            broken = smoothstep(-0.1, 0.3, self.n_fleck.fbm(P + 7.0, 9.0, octaves=2))
            vein = smoothstep(0.965, 0.99, vein) * broken * (1 - seam)
            fleck = smoothstep(0.5, 0.62, self.n_fleck.fbm(P, 45.0, octaves=2)) * (1 - seam)
            ore = np.clip(vein + fleck, 0, 1)
            glint = smoothstep(0.0, 0.6, self.n_grain.fbm(P + 3.0, 60.0, octaves=1))
            orecol = STEEL_DARK[None] + (STEEL - STEEL_DARK)[None] * glint[:, None]
            col = col + (orecol - col) * ore[:, None]
            # rust: bleeds downward from the veins on the sides, sits in patches on top
            drip = self.n_rust.fbm(P, 1.0, octaves=3, scale=(9.0, 9.0, 1.2))
            rust = smoothstep(0.15, 0.55, drip) * (1 - top) * smoothstep(0.55, 0.0, ore)
            rust = np.maximum(rust, vein * smoothstep(0.1, 0.5, self.n_rust.fbm(P, 4.0, octaves=2)))
            rust = rust * 0.75
            rustcol = RUST_DARK[None] + (RUST - RUST_DARK)[None] * smoothstep(-0.3, 0.6, grain)[:, None]
            col = col + (rustcol - col) * rust[:, None]
            height = height - 0.0004 * ore
            rough = 0.88 - 0.42 * ore + 0.04 * rust
            metal = 0.85 * ore

        # moss: patches on the top face, spilling into the seams and a little over the top edges
        moss_amt = 1.0 if not self.metal else 0.45
        mn = self.n_moss.fbm(P, 3.2, octaves=4)
        if top:
            m = smoothstep(-0.05 + 0.3 * (1 - moss_amt), 0.2 + 0.3 * (1 - moss_amt), mn + 0.35 * groove + 0.15 * (1 - pillow))
        else:
            drip_len = 0.05 + 0.12 * smoothstep(-0.3, 0.6, self.n_drip.fbm(P, 1.0, octaves=2, scale=(7.0, 7.0, 0.5)))
            below = self.half[2] - z
            spill = smoothstep(drip_len, drip_len * 0.3, below) * smoothstep(-0.1, 0.25, mn)
            crev = groove * smoothstep(0.05, 0.3, z) * smoothstep(0.0, 0.3, mn + 0.2)
            m = np.clip(spill + crev * 0.9, 0, 1) * (1 - bottom)
            m = m * smoothstep(0.0, 0.35, mn + 0.25 * moss_amt)
        m = m * moss_amt
        m = smoothstep(0.35, 0.65, m)
        mg = self.n_grain.fbm(P + 9.0, 14.0, octaves=3)
        mosscol = MOSS_DARK[None] + (MOSS_LIGHT - MOSS_DARK)[None] * smoothstep(-0.4, 0.5, mg + 0.3 * mn)[:, None]
        col = col + (mosscol - col) * m[:, None]
        height = height + m * (0.0025 + 0.0015 * mg)
        out = {"albedo": col, "height": height}
        if rough is not None:
            out["rough"] = rough * (1 - m) + 0.95 * m
            out["metal"] = metal * (1 - m)
        return out


class ChunkStone:
    """A freshly broken chunk: plain stone grain, a few hairline cracks, no moss; metal=True adds the ore."""

    def __init__(self, half, seed, metal=False):
        self.metal = metal
        self.seeds = surface_seeds(half, 9.0, seed, jitter=0.45)
        self.n_crack = Noise3(seed + 30)
        self.tone = _tone(seed + 2, len(self.seeds))
        self.n_mottle, self.n_grain, self.n_vein, self.n_fleck, self.n_rust = (Noise3(seed + k) for k in range(20, 25))

    def __call__(self, P, n, edge_d):
        cell, e = voronoi_edge(P + 0.01 * np.stack([self.n_grain.fbm(P + k, 6.0, 2) for k in range(3)], -1), self.seeds)
        seam = (1.0 - smoothstep(0.0006, 0.002, e)) * smoothstep(0.15, 0.4, self.n_crack.fbm(P, 4.0, 2))
        pillow = smoothstep(0.0, 0.015, e) * 0.2 + 0.8
        mottle = self.n_mottle.fbm(P, 5.0, octaves=3)
        grain = self.n_grain.fbm(P, 40.0, octaves=3)
        if not self.metal:
            lo, hi, dk, crack = STONE_MID, STONE_LIGHT, STONE_DARK, CRACK
        else:
            lo, hi, dk, crack = ORE_MID, ORE_LIGHT, ORE_DARK, ORE_CRACK
        t = np.clip((0.3 if not self.metal else 0.1) + 0.15 * self.tone[cell] + 0.45 * mottle, 0, 1)
        col = lo[None] + (hi - lo)[None] * t[:, None]
        if not self.metal:
            col = col + (BEIGE - col) * (0.3 * smoothstep(0.0, 0.6, mottle))[:, None]
        col = col * (1.0 + 0.07 * grain)[:, None]
        col = col + (dk - col) * (0.35 * (1 - pillow))[:, None]
        col = col + (crack - col) * (seam * 0.85)[:, None]
        height = 0.0015 * pillow - 0.003 * seam + 0.0006 * grain
        out = {"albedo": col, "height": height}
        if self.metal:
            broken = smoothstep(-0.1, 0.3, self.n_fleck.fbm(P + 7.0, 16.0, octaves=2))
            vein = smoothstep(0.97, 0.99, 1.0 - np.abs(self.n_vein.fbm(P, 6.0, octaves=3))) * broken
            fleck = smoothstep(0.5, 0.62, self.n_fleck.fbm(P, 70.0, octaves=2))
            ore = np.clip(vein + fleck, 0, 1) * (1 - seam)
            glint = smoothstep(0.0, 0.6, self.n_grain.fbm(P + 3.0, 90.0, octaves=1))
            col = col + (STEEL_DARK[None] + (STEEL - STEEL_DARK)[None] * glint[:, None] - col) * ore[:, None]
            rust = smoothstep(0.2, 0.6, self.n_rust.fbm(P, 3.0, octaves=3, scale=(4.0, 4.0, 1.0))) * (1 - ore) * 0.7
            col = col + (RUST_DARK + (RUST - RUST_DARK) * 0.5 - col) * rust[:, None]
            out.update(albedo=col, height=height - 0.0003 * ore, rough=0.88 - 0.45 * ore, metal=0.85 * ore)
        return out


class RodStone:
    """The Stone Rod: rough chiselled bar. Elongated chisel facets, lengthwise tool marks, pale chipped arrises."""

    def __init__(self, half, seed):
        self.half = np.asarray(half, float)
        self.seeds = surface_seeds(half, 14.0, seed, jitter=0.45)
        self.tone = _tone(seed + 2, len(self.seeds))
        self.n_mottle, self.n_grain, self.n_tool, self.n_crack = (Noise3(seed + k) for k in range(20, 24))
        self.crack_seeds = surface_seeds(half, 5.0, seed + 5)

    def __call__(self, P, n, edge_d):
        long_axis = int(np.argmax(self.half))
        sc = np.ones(3)
        sc[long_axis] = 0.45
        cell, e = voronoi_edge(P, self.seeds, scale=sc)
        facet = smoothstep(0.0, 0.03, e)
        _, ec = voronoi_edge(P + 0.01 * np.stack([self.n_grain.fbm(P + k, 8.0, 2) for k in range(3)], -1), self.crack_seeds)
        crack = (1.0 - smoothstep(0.0008, 0.003, ec)) * smoothstep(0.0, 0.3, self.n_crack.fbm(P, 3.0, 2))
        tsc = np.full(3, 70.0)
        tsc[long_axis] = 2.5
        tool = self.n_tool.fbm(P, 1.0, octaves=3, scale=tsc)
        mottle = self.n_mottle.fbm(P, 4.0, octaves=3)
        grain = self.n_grain.fbm(P, 45.0, octaves=3)
        t = np.clip(0.2 + 0.3 * self.tone[cell] + 0.25 * mottle + 0.1 * tool, 0, 1)
        col = STONE_MID[None] + (STONE_LIGHT - STONE_MID)[None] * t[:, None]
        col = col + (BEIGE - col) * (0.12 * smoothstep(0.0, 0.6, mottle))[:, None]
        col = col * (1.0 + 0.06 * grain)[:, None]
        col = col + (STONE_DARK - col) * (0.3 * (1 - facet))[:, None]
        chip = chip_field(P * 2.0) * edge_factor(edge_d, 0.014)
        col = col + (BEIGE - col) * np.clip(chip * 0.7 + edge_factor(edge_d, 0.006) * 0.3, 0, 0.8)[:, None]
        col = col + (CRACK - col) * (crack * 0.9)[:, None]
        height = 0.0025 * facet + 0.0008 * tool + 0.0004 * grain - 0.002 * crack - 0.0015 * chip
        return {"albedo": col, "height": height}


class RodMetal:
    """The Metal Rod: brushed steel, lengthwise brushing, slightly brighter worn arrises, one thin seam line."""

    def __init__(self, half, seed, seam_offset=0.035):
        self.half = np.asarray(half, float)
        self.n_brush, self.n_smudge, self.n_fine = Noise3(seed), Noise3(seed + 1), Noise3(seed + 2)
        self.seam_offset = seam_offset

    def __call__(self, P, n, edge_d):
        long_axis = int(np.argmax(self.half))
        sc = np.full(3, 260.0)
        sc[long_axis] = 1.2
        brush = self.n_brush.fbm(P, 1.0, octaves=3, scale=sc)
        fine = self.n_fine.fbm(P, 1.0, octaves=2, scale=sc * np.where(np.arange(3) == long_axis, 4.0, 2.5))
        smudge = smoothstep(0.1, 0.7, self.n_smudge.fbm(P, 3.0, octaves=3))
        end_face = abs(n[long_axis]) > 0.5
        if end_face:  # saw-cut ends: brushing across the face
            esc = np.full(3, 1.5)
            esc[(long_axis + 1) % 3] = 220.0
            brush = self.n_brush.fbm(P, 1.0, octaves=3, scale=esc)
        col = BRUSHED[None] * (1.0 + 0.07 * brush + 0.03 * fine)[:, None]
        col = col * (1.0 - 0.10 * smudge)[:, None]
        col = col + (BRIGHT_STEEL - col) * (edge_factor(edge_d, 0.008) * 0.5)[:, None]
        height = 0.00012 * brush + 0.00005 * fine
        seam = np.zeros(len(P))
        if n[2] > 0.5 and not end_face:  # the seam runs the length of the top face
            across = [i for i in range(3) if i not in (2, long_axis)][0]
            d = np.abs(P[:, across] - self.seam_offset)
            seam = 1.0 - smoothstep(0.0006, 0.0016, d)
            lip = smoothstep(0.0016, 0.0024, d) * (1.0 - smoothstep(0.0024, 0.004, d))
            col = col + (STEEL_DARK * 0.55 - col) * seam[:, None]
            col = col + (BRIGHT_STEEL - col) * (lip * 0.5)[:, None]
            height = height - 0.0006 * seam + 0.0002 * lip
        rough = 0.35 + 0.05 * brush + 0.08 * smudge + 0.2 * seam
        return {"albedo": col, "height": height, "rough": rough, "metal": np.full(len(P), 0.9) - 0.2 * seam}


# =====================================================================================================
# The Scrap Stone Block texture (2D, tiles seamlessly)
# =====================================================================================================

def fnoise2(seed, cutoff, size, aniso=(1.0, 1.0)):
    """Tileable 2D noise in about [-1, 1] (white noise low-passed in the frequency domain, cutoff in cycles/tile)."""
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None] * size / aniso[1]
    fx = np.fft.fftfreq(size)[None, :] * size / aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * np.exp(-(f / cutoff) ** 2)))
    return out / (np.abs(out).max() + 1e-9)


def gauss2(img, sigma):
    """Tileable Gaussian blur (in the frequency domain)."""
    n = img.shape[0]
    f = np.fft.fftfreq(n)
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(img) * g))


def fbm2(seed, base, size, octaves=4):
    total, amp, norm = 0.0, 1.0, 0.0
    for k in range(octaves):
        total = total + amp * fnoise2(seed + k, base * 2 ** k, size)
        norm += amp
        amp *= 0.5
    return total / norm


def scrap_stone_tile(size=512, seed=4242):
    """Rough rubble: irregular grey-brown stones packed in dark gritty mortar, lit from the top left.

    Periodic everywhere: the stone centres are a jittered grid wrapped on a torus (each cell sees the 3 x 3
    neighbouring copies) and every noise is FFT-filtered, so the left/right and top/bottom edges meet exactly."""
    rng = np.random.default_rng(seed)
    g = 6
    pts = []
    for i in range(g):
        for j in range(g):
            pts.append(((i + 0.5 + rng.uniform(-0.4, 0.4)) / g, (j + 0.5 + rng.uniform(-0.4, 0.4)) / g))
    pts = np.array(pts)
    weight = rng.uniform(0.0, 1.0, len(pts)) ** 2 * 0.006     # power diagram: some stones big, some small
    tone = rng.uniform(0, 1, len(pts))
    warm = rng.uniform(0, 1, len(pts))
    tiles = np.concatenate([pts + np.array([dx, dy]) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    ids = np.tile(np.arange(len(pts)), 9)
    wt = weight[ids]
    yy, xx = (np.mgrid[0:size, 0:size] + 0.5) / size
    wx = fnoise2(seed + 1, 6.0, size) * 0.022 + fnoise2(seed + 2, 24.0, size) * 0.006
    wy = fnoise2(seed + 3, 6.0, size) * 0.022 + fnoise2(seed + 4, 24.0, size) * 0.006
    q = np.stack([(xx + wx).ravel(), (yy + wy).ravel()], -1)
    d2 = ((q[:, None, :] - tiles[None]) ** 2).sum(-1) - wt[None]
    i1 = d2.argmin(1)
    r = np.arange(len(q))
    dist = np.linalg.norm(tiles[:, None] - tiles[None], axis=-1)
    np.fill_diagonal(dist, np.inf)
    e = (d2 - d2[r, i1][:, None]) / (2 * dist[i1])
    e[r, i1] = np.inf
    edge = e.min(1).reshape(size, size)
    cell = ids[i1].reshape(size, size)

    gap = 0.011
    stone = smoothstep(gap * 0.6, gap * 1.4, edge)
    dome = smoothstep(gap, gap + 0.045, edge) * (0.85 + 0.15 * smoothstep(gap, gap + 0.12, edge))
    bumps = fbm2(seed + 14, 10.0, size, 3)
    grain = fbm2(seed + 10, 40.0, size, 3)
    mottle = fbm2(seed + 11, 8.0, size, 3)
    pits = smoothstep(0.62, 0.8, fnoise2(seed + 12, 120.0, size))
    grit = fnoise2(seed + 13, 160.0, size)

    t = np.clip(0.25 + 0.5 * tone[cell] + 0.25 * mottle, 0, 1)
    lo, hi = srgb("#6e6a63"), srgb("#aca699")
    col = lo + (hi - lo) * t[..., None]
    col = col + (srgb("#9a8c72") - col) * (0.35 * warm[cell] * smoothstep(-0.2, 0.5, mottle))[..., None]
    col = col * (1 + 0.07 * grain)[..., None] * (1 - 0.25 * pits)[..., None]
    mortar = srgb("#47433d") * (1 + 0.25 * grit)[..., None]
    height = dome * 0.75 + 0.12 * bumps * dome + 0.04 * grain - 0.1 * pits
    height = height * stone + (0.08 * grit) * (1 - stone)
    height = gauss2(height, 2.5)                    # softens the creases the distance field leaves at corners

    # light from the top left, from the height field (np.roll keeps the tiling)
    k = 6.0
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * k
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * k
    nrm = np.stack([-dx, dy, np.ones_like(height)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    L = np.array([-0.45, 0.55, 0.7])
    L /= np.linalg.norm(L)
    lit = nrm @ L - L[2]
    col = col * (1 + 0.7 * lit)[..., None]
    col = col + (srgb("#c4baa2") - col) * np.clip(lit * 1.6, 0, 0.45)[..., None]
    col = col * (0.72 + 0.28 * np.clip(dome * 1.6, 0, 1))[..., None]      # occlusion toward the gaps
    col = col * stone[..., None] + mortar * (1 - stone[..., None])
    return np.clip(col, 0, 1)
