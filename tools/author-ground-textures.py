#!/usr/bin/env python3
"""Redraws the vanilla ground textures (mods/vanilla/textures/terrain/*.png) in the flora's stylised look.

Usage: tools/author-ground-textures.py [OUT_DIR]   (default: mods/vanilla/textures/terrain)
Needs Python 3 with numpy and Pillow. Deterministic: the same script draws the same pixels.
"""
import os
import sys

import numpy as np
from PIL import Image

S = 512
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla", "textures", "terrain")
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S

def hexc(h):
    h = h.lstrip('#'); return np.array([int(h[i:i+2], 16) / 255 for i in (0, 2, 4)], np.float32)

def pnoise(freq, rng, octaves=1, gain=0.5):
    """Tileable value noise in 0..1."""
    out = np.zeros((S, S), np.float32); amp = 1; tot = 0
    for o in range(octaves):
        f = freq * 2 ** o
        g = rng.random((f, f)).astype(np.float32)
        x = xx * f; y = yy * f
        x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
        tx = x - x0; ty = y - y0
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty)
        x1 = (x0 + 1) % f; y1 = (y0 + 1) % f; x0 %= f; y0 %= f
        a = g[y0, x0] * (1 - tx) + g[y0, x1] * tx
        b = g[y1, x0] * (1 - tx) + g[y1, x1] * tx
        out += amp * (a * (1 - ty) + b * ty); tot += amp; amp *= gain
    return out / tot

def voronoi(n, rng, jitter=1.0):
    """Tileable Voronoi: cell id, distance to nearest, distance to edge (F2-F1), facet gradient."""
    pts = rng.random((n, 2)).astype(np.float32)
    best = np.full((S, S), 9.0, np.float32); second = np.full((S, S), 9.0, np.float32)
    ids = np.zeros((S, S), int); dxs = np.zeros((S, S), np.float32); dys = np.zeros((S, S), np.float32)
    for i, (px, py) in enumerate(pts):
        for ox in (-1, 0, 1):
            for oy in (-1, 0, 1):
                dx = xx - (px + ox); dy = yy - (py + oy)
                d = np.sqrt(dx * dx + dy * dy)
                closer = d < best
                second = np.where(closer, best, np.minimum(second, d))
                ids = np.where(closer, i, ids); dxs = np.where(closer, dx, dxs); dys = np.where(closer, dy, dys)
                best = np.minimum(best, d)
    return ids, best, second - best, dxs, dys

def facets(n, rng, colors, light=(0.6, -0.8), facet=0.35, edge=0.10, edge_w=0.008, vary=0.08):
    """Flat-shaded cells like the flora's facets: a colour per cell, a tilt per cell lit from one side."""
    ids, d1, edgeD, dx, dy = voronoi(n, rng)
    cols = np.array([hexc(c) for c in colors])
    pick = rng.integers(0, len(cols), n)
    base = cols[pick] * (1 + rng.uniform(-vary, vary, (n, 1)))
    tilt = rng.normal(0, 1, (n, 2)).astype(np.float32)
    shade = 1 + facet * (tilt[ids, 0] * light[0] + tilt[ids, 1] * light[1]) * 0.35
    img = base[ids] * shade[..., None]
    e = np.clip(edgeD / edge_w, 0, 1)
    img *= (1 - edge * (1 - e))[..., None]
    return img, ids, edgeD

def strokes(img, rng, count, colors, length=(0.012, 0.03), width=1.6, lean=0.35, alpha=0.9):
    """Short tapered blade strokes, drawn tileably."""
    cols = [hexc(c) for c in colors]
    out = img.copy()
    for _ in range(count):
        x0, y0 = rng.random() * S, rng.random() * S
        L = rng.uniform(*length) * S
        ang = -np.pi / 2 + rng.normal(0, lean)
        c = cols[rng.integers(len(cols))] * rng.uniform(0.9, 1.1)
        steps = int(L * 1.5) + 2
        for k in range(steps):
            t = k / steps
            w = width * (1 - t) + 0.4
            px = x0 + np.cos(ang) * L * t; py = y0 + np.sin(ang) * L * t
            r = int(np.ceil(w))
            for u in range(-r, r + 1):
                for v in range(-r, r + 1):
                    if u * u + v * v <= w * w:
                        X = int(px + u) % S; Y = int(py + v) % S
                        out[Y, X] = out[Y, X] * (1 - alpha) + c * (0.8 + 0.3 * t) * alpha
    return out

def dots(img, rng, count, colors, radius=(1.5, 4), alpha=1.0, shade=0.25):
    cols = [hexc(c) for c in colors]
    out = img.copy()
    for _ in range(count):
        x0, y0 = rng.random() * S, rng.random() * S
        r = rng.uniform(*radius); c = cols[rng.integers(len(cols))]
        R = int(np.ceil(r))
        for u in range(-R, R + 1):
            for v in range(-R, R + 1):
                if u * u + v * v <= r * r:
                    X = int(x0 + u) % S; Y = int(y0 + v) % S
                    lit = 1 + shade * (-(u + v) / (2 * r))
                    out[Y, X] = out[Y, X] * (1 - alpha) + c * lit * alpha
    return out

def save(name, img):
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, f"{name}.png"))


def blur(a, r=2):
    out = a.copy()
    for ax in (0, 1):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=ax)
        out = acc / (2 * r + 1)
    return out

def bands(n, colors, soft=1):
    """Painterly tone bands: noise stepped into the given colours, edges softened a touch."""
    cols = np.array([hexc(c) for c in colors])
    idx = np.clip((n * len(cols)).astype(int), 0, len(cols) - 1)
    img = cols[idx]
    return np.stack([blur(img[..., i], soft) for i in range(3)], -1)

def stretch(n):
    lo, hi = np.percentile(n, 2), np.percentile(n, 98)
    return np.clip((n - lo) / (hi - lo), 0, 0.999)

def leaves(img, rng, count, colors, size=(3, 6)):
    cols = [hexc(c) for c in colors]; out = img.copy()
    for _ in range(count):
        x0, y0 = rng.random() * S, rng.random() * S
        a = rng.uniform(0, np.pi); L = rng.uniform(*size); W = L * 0.45
        c = cols[rng.integers(len(cols))] * rng.uniform(0.85, 1.1)
        R = int(L) + 1
        for u in range(-R, R + 1):
            for v in range(-R, R + 1):
                p = u * np.cos(a) + v * np.sin(a); q = -u * np.sin(a) + v * np.cos(a)
                if (p / L) ** 2 + (q / W) ** 2 <= 1:
                    X = int(x0 + u) % S; Y = int(y0 + v) % S
                    out[Y, X] = c * (1.1 if q < 0 else 0.85)
    return out

out = {}
r = np.random.default_rng(1)
n = stretch(pnoise(3, r, 4, 0.55))
g = bands(n, ['#3a8a1f', '#45992a', '#50a62e', '#5cb033'])
g = strokes(g, r, 3400, ['#5aa832', '#6cba3a', '#3c8424', '#78c440'], width=1.3, length=(0.01, 0.024))
g = strokes(g, r, 260, ['#b8d244', '#cdd84a'], width=1.0, length=(0.008, 0.016))
out['grass'] = g

r = np.random.default_rng(2)
n = stretch(pnoise(3, r, 4, 0.55))
f = bands(n, ['#4a3a28', '#56432c', '#4f5a2a', '#3f6e2a'])
f = leaves(f, r, 520, ['#8a6a32', '#a07a30', '#6e8030', '#7a4f26', '#557a2c'])
f = strokes(f, r, 120, ['#5a4128', '#6a4c2e'], width=1.0, length=(0.02, 0.05), lean=1.5)
f = strokes(f, r, 900, ['#3f7f2a', '#4d8f30'], width=1.1)
out['forest_floor'] = f

r = np.random.default_rng(3)
n = stretch(pnoise(3, r, 4, 0.55))
d = bands(n, ['#7a7a46', '#86864c', '#939253', '#a09c5a'])
gm = stretch(pnoise(5, r, 2))
dd = dots(d, r, 1600, ['#9a968c', '#85827a', '#b0aca0', '#6f6c66'], radius=(1.5, 3.8), shade=0.6)
d = np.where((gm > 0.55)[..., None], dd, d)
d = strokes(d, r, 1400, ['#a8a84e', '#b9b058', '#7f8a3c'], width=1.2)
out['dry_grass'] = d

r = np.random.default_rng(4)
k, _, _ = facets(55, r, ['#7c7b76', '#84837c', '#77766f'], facet=0.6, edge=0.25, edge_w=0.005, vary=0.04)
out['rock'] = k

r = np.random.default_rng(5)
n = stretch(pnoise(3, r, 3, 0.5))
s2 = bands(n, ['#d2bf86', '#dac890', '#e2d29c'])
s2 *= (0.96 + 0.05 * np.sin((yy + 0.06 * pnoise(3, r)) * np.pi * 2 * 10))[..., None]
s2 = dots(s2, r, 160, ['#b9a874', '#f0e4b8'], radius=(1, 2.2))
out['sand'] = s2

r = np.random.default_rng(6)
# Warehouse lot: packed grey-brown dirt with broken concrete slabs, gravel and weeds.
n = stretch(pnoise(3, r, 4, 0.55))
c = bands(n, ['#4f4a42', '#575147', '#5f584c'])
ids, d1, ed, _, _ = voronoi(16, r)
keep = r.random(16) < 0.5
slab = keep[ids] & (ed > 0.012 + 0.012 * pnoise(10, r))
slabc = bands(stretch(pnoise(6, r, 2)), ['#646668', '#6c6e70'], soft=0)
c = np.where(slab[..., None], slabc, c)
c[slab & (ed < 0.003)] *= 0.6
edge = slab & ~(np.roll(slab, 2, 0) & np.roll(slab, -2, 0) & np.roll(slab, 2, 1) & np.roll(slab, -2, 1))
c[edge] *= 0.55
cc = dots(c, r, 1100, ['#8a857c', '#6f6a62', '#9b968c'], radius=(1.2, 3), shade=0.6)
c = np.where(slab[..., None], c, cc)
c = strokes(c, r, 700, ['#5f8a30', '#6f9a36', '#86a83c'], width=1.2, length=(0.008, 0.018))
out['lot'] = c

r = np.random.default_rng(8)
n = stretch(pnoise(4, r, 4, 0.55))
b = bands(n, ['#221f1c', '#2b2723', '#332e29', '#3c3630'])
ash = stretch(pnoise(12, r, 2))
b = np.where((ash > 0.84)[..., None], b * 0.55 + hexc('#5f5a54') * 0.45, b)
ids, _, ed, _, _ = voronoi(12, r)
crack = np.clip(1 - ed / 0.0016, 0, 1); glow = np.clip(1 - ed / 0.0045, 0, 1) * (stretch(pnoise(5, r)) > 0.7)
b = b * (1 - 0.8 * crack[..., None]) + hexc('#c4501e')[None, None] * (glow * 0.55)[..., None]
b = dots(b, r, 350, ['#141210', '#5c534a', '#7d756c'], radius=(1.2, 3), shade=0.6)
b = strokes(b, r, 900, ['#1a1714', '#33281d', '#4a3a26'], width=1.1, length=(0.008, 0.02))
out['burnt'] = b
# Albedo levels, so the ground sits under the brighter flora in the game's light.
dim = {'grass': 0.8, 'forest_floor': 0.92, 'dry_grass': 0.74, 'rock': 0.82, 'sand': 0.74, 'lot': 1.0, 'burnt': 1.0}
for nme, img in out.items():
    save(nme, img * dim[nme])
