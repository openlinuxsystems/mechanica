#!/usr/bin/env python3
"""Draws the vanilla ground textures (mods/vanilla/textures/terrain/*.png) in a painted, stylised look.

Each texture is 1024 px square and tiles. Its alpha channel is the surface's height (white is high): the ground shader
blends grounds by it, so grass fills the gaps between stones and dirt shows through thin grass, and lights the
ground with bumps derived from it. Colours sit a little below the flora's, olive rather than neon.

Usage: tools/author-ground-textures.py [OUT_DIR]   (default: mods/vanilla/textures/terrain)
Needs Python 3 with numpy and Pillow. Deterministic: the same script draws the same pixels.
"""
import os
import sys

import numpy as np
from PIL import Image

S = 1024
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla", "textures", "terrain")
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S


def hexc(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


def pnoise(freq, rng, octaves=1, gain=0.5, wx=None, wy=None):
    """Tileable value noise in 0..1, optionally sampled at warped coordinates."""
    out = np.zeros((S, S), np.float32)
    amp = 1
    tot = 0
    X = xx if wx is None else wx
    Y = yy if wy is None else wy
    for o in range(octaves):
        f = freq * 2 ** o
        g = rng.random((f, f)).astype(np.float32)
        x = (X % 1) * f
        y = (Y % 1) * f
        x0 = np.floor(x).astype(int)
        y0 = np.floor(y).astype(int)
        tx = x - x0
        ty = y - y0
        tx = tx * tx * (3 - 2 * tx)
        ty = ty * ty * (3 - 2 * ty)
        x1 = (x0 + 1) % f
        y1 = (y0 + 1) % f
        x0 %= f
        y0 %= f
        a = g[y0, x0] * (1 - tx) + g[y0, x1] * tx
        b = g[y1, x0] * (1 - tx) + g[y1, x1] * tx
        out += amp * (a * (1 - ty) + b * ty)
        tot += amp
        amp *= gain
    return out / tot


def stretch(n):
    lo, hi = np.percentile(n, 2), np.percentile(n, 98)
    return np.clip((n - lo) / (hi - lo), 0, 0.999)


def blur(a, r=2):
    out = a.copy()
    for ax in (0, 1):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=ax)
        out = acc / (2 * r + 1)
    return out


def bands(n, colors, soft=2):
    """Painterly tone bands: noise stepped into the given colours, edges softened."""
    cols = np.array([hexc(c) for c in colors])
    idx = np.clip((n * len(cols)).astype(int), 0, len(cols) - 1)
    img = cols[idx]
    return np.stack([blur(img[..., i], soft) for i in range(3)], -1)


def voronoi(n, rng):
    """Tileable Voronoi: cell id, distance to nearest point, distance to the nearest edge (F2 - F1)."""
    pts = rng.random((n, 2)).astype(np.float32)
    best = np.full((S, S), 9.0, np.float32)
    second = np.full((S, S), 9.0, np.float32)
    ids = np.zeros((S, S), int)
    for i, (px, py) in enumerate(pts):
        for ox in (-1, 0, 1):
            for oy in (-1, 0, 1):
                dx = xx - (px + ox)
                dy = yy - (py + oy)
                d = np.sqrt(dx * dx + dy * dy)
                closer = d < best
                second = np.where(closer, best, np.minimum(second, d))
                ids = np.where(closer, i, ids)
                best = np.minimum(best, d)
    return ids, best, second - best


class Layer:
    """An albedo and a height map painted together."""

    def __init__(self, albedo, height):
        self.c = albedo.astype(np.float32)
        self.h = height.astype(np.float32)

    def disc(self, x0, y0, r, color, height, alpha=1.0, shade=0.0, dome=0.0):
        R = int(np.ceil(r))
        ys, xs = np.mgrid[-R:R + 1, -R:R + 1]
        inside = xs * xs + ys * ys <= r * r
        X = (int(x0) + xs[inside]) % S
        Y = (int(y0) + ys[inside]) % S
        lit = 1 + shade * (-(xs[inside] + ys[inside]) / (2 * r))
        self.c[Y, X] = self.c[Y, X] * (1 - alpha) + color * lit[:, None] * alpha
        bump = 1 - (xs[inside] ** 2 + ys[inside] ** 2) / (r * r)
        self.h[Y, X] = np.maximum(self.h[Y, X], height + dome * bump)

    def stroke(self, x0, y0, length, angle, width, color, height, alpha=0.9):
        steps = int(length * 1.2) + 2
        for k in range(steps):
            t = k / steps
            w = width * (1 - t) + 0.5
            px = x0 + np.cos(angle) * length * t
            py = y0 + np.sin(angle) * length * t
            self.disc(px, py, w, color * (0.82 + 0.3 * t), height + 0.25 * t, alpha)

    def strokes(self, rng, count, colors, length=(10, 24), width=1.6, lean=0.35, height=0.6, alpha=0.9):
        cols = [hexc(c) for c in colors]
        for _ in range(count):
            self.stroke(rng.random() * S, rng.random() * S, rng.uniform(*length), -np.pi / 2 + rng.normal(0, lean), width,
                        cols[rng.integers(len(cols))] * rng.uniform(0.9, 1.1), height + rng.uniform(-0.1, 0.1), alpha)

    def pebbles(self, rng, count, colors, radius=(1.5, 4), shade=0.5, height=0.55, dome=0.35, mask=None):
        cols = [hexc(c) for c in colors]
        for _ in range(count):
            x, y = rng.random() * S, rng.random() * S
            if mask is not None and not mask[int(y) % S, int(x) % S]:
                continue
            r = rng.uniform(*radius)
            self.disc(x, y, r, cols[rng.integers(len(cols))] * rng.uniform(0.9, 1.08), height, shade=shade, dome=dome)

    def leaves(self, rng, count, colors, size=(5, 11), height=0.6):
        cols = [hexc(c) for c in colors]
        for _ in range(count):
            x0, y0 = rng.random() * S, rng.random() * S
            a = rng.uniform(0, np.pi)
            L = rng.uniform(*size)
            W = L * 0.45
            c = cols[rng.integers(len(cols))] * rng.uniform(0.85, 1.1)
            R = int(L) + 1
            ys, xs = np.mgrid[-R:R + 1, -R:R + 1]
            p = xs * np.cos(a) + ys * np.sin(a)
            q = -xs * np.sin(a) + ys * np.cos(a)
            inside = (p / L) ** 2 + (q / W) ** 2 <= 1
            X = (int(x0) + xs[inside]) % S
            Y = (int(y0) + ys[inside]) % S
            self.c[Y, X] = c * np.where(q[inside] < 0, 1.1, 0.85)[:, None]
            self.h[Y, X] = np.maximum(self.h[Y, X], height + rng.uniform(0, 0.15))


def save(name, layer, dim):
    rgb = np.clip(layer.c * dim, 0, 1)
    h = np.clip(layer.h, 0, 1)
    rgba = np.concatenate([rgb, h[..., None]], -1)
    Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, f"{name}.png"))


out = {}

# Grass: olive and sap greens in soft patches, dense blades on top, a few dry stems; low soil between clumps.
r = np.random.default_rng(1)
n = stretch(pnoise(3, r, 4, 0.55))
clump = stretch(pnoise(9, r, 2))
g = Layer(bands(n, ['#3f6a24', '#4a7728', '#55822d', '#5f8b31']), 0.25 + 0.25 * clump)
g.c *= (0.9 + 0.12 * clump)[..., None]
g.strokes(r, 9000, ['#5a8a30', '#6a9636', '#46752a', '#76a03c'], width=2.2, length=(14, 30), height=0.55)
g.strokes(r, 900, ['#a5a848', '#b9b252', '#8f9640'], width=1.6, length=(10, 20), height=0.6)
out['grass'] = (g, 0.85)

# Forest floor: dark loam under fallen leaves, moss patches, twigs.
r = np.random.default_rng(2)
n = stretch(pnoise(3, r, 4, 0.55))
moss = stretch(pnoise(4, r, 3))
f = Layer(bands(n, ['#3d3022', '#47382a', '#4e3e2c', '#3f4826']), 0.2 + 0.1 * n)
mc = bands(stretch(pnoise(8, r, 2)), ['#3e5a24', '#4a6a2a', '#557430'])
f.c = np.where((moss > 0.62)[..., None], mc, f.c)
f.h = np.where(moss > 0.62, 0.45 + 0.1 * moss, f.h)
f.leaves(r, 2400, ['#8a6a32', '#9c7430', '#6e7a30', '#7a4f26', '#5c6a2a', '#a8822e'])
f.strokes(r, 260, ['#5a4128', '#6a4c2e', '#4e3a26'], width=1.4, length=(30, 80), lean=1.5, height=0.7)
out['forest_floor'] = (f, 0.92)

# Dry grass: straw and khaki tufts over pale earth with gravel showing through.
r = np.random.default_rng(3)
n = stretch(pnoise(3, r, 4, 0.55))
d = Layer(bands(n, ['#7a6f44', '#857a4a', '#908550', '#9a8f58']), 0.2 + 0.15 * n)
bare = stretch(pnoise(5, r, 2)) > 0.6
d.pebbles(r, 7000, ['#9a968c', '#85827a', '#b0aca0', '#6f6c66'], radius=(2, 5), mask=bare)
d.strokes(r, 6000, ['#a8a04e', '#b9a858', '#8a8a40', '#c2b066'], width=1.8, length=(12, 26), height=0.6)
out['dry_grass'] = (d, 0.78)

# Rock: weathered stone in warped strata with cracks, lichen and a grey-beige range (the rocks' material).
r = np.random.default_rng(4)
wy = yy + 0.08 * (pnoise(2, r, 3) - 0.5) + 0.03 * (pnoise(6, r, 2) - 0.5)
strata = (wy * 7) % 1
layer_id = np.floor(wy * 7).astype(int) % 7
tones = np.array([hexc(c) for c in ['#7d7a72', '#87837a', '#736f68', '#8c877c', '#7a766d', '#827e74', '#6f6c66']])
base = tones[layer_id]
grain = stretch(pnoise(16, r, 3))
k = Layer(base * (0.86 + 0.22 * grain)[..., None], 0.55 + 0.25 * grain)
# Each band's top edge stands proud and catches the light; its foot sits in shadow.
k.h -= 0.35 * strata
k.c *= (1.08 - 0.22 * strata)[..., None]
ids, _, edge = voronoi(40, r)
crack = np.clip(1 - edge / 0.003, 0, 1) * (stretch(pnoise(5, r)) > 0.35)
k.c *= (1 - 0.55 * crack)[..., None]
k.h -= 0.4 * crack
lichen = stretch(pnoise(10, r, 2)) > 0.86
k.c = np.where(lichen[..., None], k.c * 0.5 + hexc('#8a9a50') * 0.5, k.c)
k.h = np.clip(k.h, 0, 1)
out['rock'] = (k, 0.88)

# Sand: wind ripples and a few shells and pebbles.
r = np.random.default_rng(5)
n = stretch(pnoise(3, r, 3, 0.5))
rip = np.sin((yy + 0.05 * pnoise(3, r) + 0.02 * pnoise(9, r)) * np.pi * 2 * 22)
s2 = Layer(bands(n, ['#cdb984', '#d6c38e', '#ddca98']), 0.45 + 0.18 * rip)
s2.c *= (0.95 + 0.06 * rip)[..., None]
s2.pebbles(r, 700, ['#b9a874', '#f0e4b8', '#9c8c66'], radius=(1.2, 3), height=0.7, dome=0.2)
out['sand'] = (s2, 0.78)

# Lot: packed grey-brown ground with broken concrete slabs, gravel and weeds in the cracks.
r = np.random.default_rng(6)
n = stretch(pnoise(3, r, 4, 0.55))
c = Layer(bands(n, ['#4f4a42', '#575147', '#5f584c']), 0.3 + 0.1 * n)
ids, _, ed = voronoi(16, r)
keep = r.random(16) < 0.5
slab = keep[ids] & (ed > 0.012 + 0.012 * pnoise(10, r))
slabc = bands(stretch(pnoise(6, r, 2)), ['#646668', '#6c6e70'], soft=0) * (0.92 + 0.12 * stretch(pnoise(20, r, 2)))[..., None]
c.c = np.where(slab[..., None], slabc, c.c)
c.h = np.where(slab, 0.75, c.h)
edge = slab & ~(np.roll(slab, 3, 0) & np.roll(slab, -3, 0) & np.roll(slab, 3, 1) & np.roll(slab, -3, 1))
c.c[edge] *= 0.6
c.pebbles(r, 4400, ['#8a857c', '#6f6a62', '#9b968c'], radius=(2, 5), mask=~slab)
c.strokes(r, 1600, ['#5f7a30', '#6f8a36', '#86983c'], width=1.6, length=(10, 22), height=0.5)
out['lot'] = (c, 1.0)

# Burnt ground: charcoal earth, ash drifts, embers in cracks, charred twigs.
r = np.random.default_rng(8)
n = stretch(pnoise(4, r, 4, 0.55))
b = Layer(bands(n, ['#221f1c', '#2b2723', '#332e29', '#3c3630']), 0.3 + 0.2 * n)
ash = stretch(pnoise(12, r, 2))
b.c = np.where((ash > 0.84)[..., None], b.c * 0.55 + hexc('#5f5a54') * 0.45, b.c)
ids, _, ed = voronoi(24, r)
crack = np.clip(1 - ed / 0.0016, 0, 1)
glow = np.clip(1 - ed / 0.0045, 0, 1) * (stretch(pnoise(5, r)) > 0.7)
b.c = b.c * (1 - 0.8 * crack[..., None]) + hexc('#c4501e')[None, None] * (glow * 0.55)[..., None]
b.h -= 0.3 * crack
b.pebbles(r, 1400, ['#141210', '#5c534a', '#7d756c'], radius=(2, 5))
b.strokes(r, 1600, ['#1a1714', '#33281d', '#4a3a26'], width=1.6, length=(12, 36), lean=1.4, height=0.6)
b.h = np.clip(b.h, 0, 1)
out['burnt'] = (b, 1.0)

# Dirt: packed brown earth with ruts, gravel and the odd weed, for tracks, shoulders and yards.
r = np.random.default_rng(9)
n = stretch(pnoise(4, r, 4, 0.55))
rut = stretch(pnoise(2, r, 2, wx=xx * 0.5))
t = Layer(bands(n, ['#5f4630', '#6a5036', '#755a3c', '#7f6343']), 0.3 + 0.2 * rut)
t.c *= (0.92 + 0.12 * rut)[..., None]
t.pebbles(r, 9000, ['#9c958a', '#7d776e', '#b2aca0', '#5e5850'], radius=(1.6, 4.4), shade=0.6, height=0.6)
t.strokes(r, 700, ['#6a8a3a', '#7a983e'], width=1.4, length=(8, 18), height=0.6)
out['dirt'] = (t, 0.88)

for name, (layer, dim) in out.items():
    save(name, layer, dim)
    print(name)
