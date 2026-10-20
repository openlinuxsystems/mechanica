#!/usr/bin/env python3
"""Cuts the vanilla block textures (mods/vanilla/textures/blocks/*.png) from the artist's source pictures.

Usage: tools/author-block-textures.py SOURCE_DIR [OUT_DIR]   (default OUT_DIR: mods/vanilla/textures/blocks)
SOURCE_DIR holds one <material>.jpg per block material (aluminum.jpg, wood.jpg, ...). Needs Python 3 with numpy
and Pillow. Deterministic: the same sources give the same pixels.

Most textures cover one 0.25 m block face and repeat on the next block, so each picture is cut to a whole number
of its pattern repeats (measured by autocorrelation), resized to 512 px and blended across its edges so it tiles.
See-through materials get alpha (glass, armored glass); the net is drawn as a cut-out lattice because its source
is not regular enough to tile; scrap metal is drawn as a
2048 px patchwork of plates that spans 8 x 8 blocks (its part's texture_span), and scrap wood (staggered planks) and
scrap stone (dressed slabs) the same way; the path light gets a glow map of its lit strips.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 512

# How each source is cut. px/py: approximate pattern repeat in source pixels, nx/ny: repeats per block face.
# size/w/h: a plain square (or w x h) crop for patternless pictures. feather (or fx/fy): fraction of the edge
# blended so the cut tiles; 0 leaves that axis as it is. at: crop corner.
CUTS = {
    "aluminum": {"px": 194.5, "py": 195, "nx": 2, "ny": 2},
    "armored_glass": {"w": 1168, "h": 784, "fx": 0, "fy": 0},  # the whole picture already tiles
    "barrier": {"px": 173, "py": 159, "nx": 2, "ny": 2},  # top rows: the bottom of the source is smeared
    "brick": {"px": 194.5, "py": 130, "nx": 2, "ny": 3},
    "cardboard": {"px": 390, "py": 390, "nx": 1, "ny": 1},
    "carpet": {"size": 400, "feather": 0.3},
    "concrete": {"size": 512, "feather": 0.3},
    "diamond_plate": {"px": 131, "py": 124, "nx": 3, "ny": 3},
    "glass": {"px": 196, "py": 196, "nx": 2, "ny": 2},
    "hull_plate": {"w": 579, "h": 579, "fx": 0.08, "fy": 0.15},
    "insulation": {"size": 392, "feather": 0.3},
    "metal": {"px": 434, "py": 390, "nx": 1, "ny": 1},
    "path_light": {"px": 195, "py": 196, "nx": 2, "ny": 2},
    "plaster": {"size": 512, "feather": 0.3},
    "rusted_metal": {"size": 512, "feather": 0.3},
    "tile": {"px": 83.4, "py": 78.5, "nx": 4, "ny": 4},
    "wood": {"px": 581, "py": 336, "nx": 1, "ny": 1},
}


def autocorrelation(gray):
    a = gray - gray.mean()
    f = np.fft.fft2(a)
    ac = np.real(np.fft.ifft2(f * np.conj(f)))
    return ac / ac[0, 0]


def refine(profile, period, limit):
    """The repeat measured precisely: the autocorrelation peak near the largest multiple that fits, divided back."""
    n = max(1, int(limit // period))
    centre = int(round(n * period))
    width = max(3, n * 2)
    i = int(np.argmax(profile[centre - width:centre + width + 1])) + centre - width
    y0, y1, y2 = profile[i - 1], profile[i], profile[i + 1]
    curve = y0 - 2 * y1 + y2
    return (i + (0.5 * (y0 - y2) / curve if curve else 0)) / n


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def seamless(a, fx, fy):
    """Blends the picture with itself shifted by half, weighted towards the shifted copy at the edges, so it wraps."""
    s = a.shape[0]
    def ramp(f):
        if f == 0:
            return np.ones(s)
        return smoothstep(np.minimum(np.arange(s), s - 1 - np.arange(s)) / (s * f), 0, 1)
    shifted = np.roll(a, (s // 2 if fy else 0, s // 2 if fx else 0), axis=(0, 1))
    w = np.minimum(ramp(fy)[:, None], ramp(fx)[None, :])[..., None]
    return a * w + shifted * (1 - w)


def cut(source, c):
    im = Image.open(source).convert("RGB")
    if "px" in c:
        ac = autocorrelation(np.asarray(im.convert("L"), np.float64))
        w = refine(ac[0, :], c["px"], im.width / 2) * c["nx"]
        h = refine(ac[:, 0], c["py"], im.height / 2) * c["ny"]
    else:
        w, h = c.get("w", c.get("size")), c.get("h", c.get("size"))
    x, y = c.get("at", (0, 0))
    a = np.asarray(im.resize((N, N), Image.LANCZOS, box=(x, y, x + w, y + h)), np.float64)
    feather = c.get("feather", 0.15)
    return seamless(a, c.get("fx", feather), c.get("fy", feather))


def save(path, rgb, alpha=None):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    if alpha is None:
        Image.fromarray(rgb, "RGB").save(path, optimize=True)
    else:
        a = np.clip(alpha * 255, 0, 255).astype(np.uint8)
        Image.fromarray(np.dstack([rgb, a]), "RGBA").save(path, optimize=True)


def value_noise(size, cells, seed, octaves=4):
    """Smooth noise in 0..1 that wraps at the edges: tiled random grids upscaled bicubically, the middle tile kept;
    each octave twice as fine and half as strong."""
    r = np.random.default_rng(seed)
    out, amp, total = np.zeros((size, size)), 1.0, 0.0
    for _ in range(octaves):
        grid = np.tile(r.random((cells, cells)), (3, 3))
        g = Image.fromarray((grid * 255).astype(np.uint8)).resize((3 * size, 3 * size), Image.BICUBIC)
        out += amp * np.asarray(g, np.float64)[size:2 * size, size:2 * size] / 255
        total += amp
        amp *= 0.5
        cells *= 2
    return out / total

def pack(units, seed):
    """Rectangles (x, y, w, h) in units that cover a units x units torus with no gaps."""
    r = np.random.default_rng(seed)
    taken = np.full((units, units), -1)
    rects = []
    order = [(x, y) for y in range(units) for x in range(units)]
    for x, y in order:
        if taken[y, x] >= 0:
            continue
        shapes = [(int(r.integers(2, 7)), int(r.integers(1, 4))) for _ in range(12)]
        shapes = [s if r.random() < 0.6 else s[::-1] for s in shapes]
        shapes += [(2, 1), (1, 2), (1, 1)]
        for w, h in shapes:
            ys, xs = (np.arange(y, y + h) % units), (np.arange(x, x + w) % units)
            if (taken[np.ix_(ys, xs)] < 0).all():
                taken[np.ix_(ys, xs)] = len(rects)
                rects.append((x, y, w, h))
                break
    return rects, taken

def scrap_metal(size=2048, units=32):
    """Scrap plates of many sizes bolted together over one 2 m repeat (texture_span 8): mostly orange paint, with grey
    grates, yellow crate panels, hazard stripes and bare grey plates mixed in, worn to steel at the edges, with rust,
    scratches and dents. Drawn rather than cut, and every pattern (plates, noise, scratches) wraps at the edges."""
    ss = smoothstep
    U = size // units
    rects, taken = pack(units, 5)
    n = len(rects)
    r = np.random.default_rng(6)
    # kind per piece: 0 orange plate, 1 grey grate, 2 yellow crate, 3 hazard stripes, 4 grey plate
    kinds = np.zeros(n, int)
    for i, (x, y, w, h) in enumerate(rects):
        roll = r.random()
        long = max(w, h) >= 4 and min(w, h) == 1
        if long and roll < 0.45: kinds[i] = 3
        elif roll < 0.10: kinds[i] = 1
        elif roll < 0.17 and min(w, h) >= 1: kinds[i] = 2
        elif roll < 0.25: kinds[i] = 4
    y, x = np.mgrid[0:size, 0:size] + 0.5
    lab = taken[(y // U).astype(int) % units, (x // U).astype(int) % units]
    R = np.array(rects, float) * U
    X0, Y0, W, H = R[lab, 0], R[lab, 1], R[lab, 2], R[lab, 3]
    kind = kinds[lab]
    u, v = (x - X0) % size, (y - Y0) % size
    gap = 3
    d = np.minimum(np.minimum(u - gap, W - gap - u), np.minimum(v - gap, H - gap - v))
    horiz = W >= H
    chip, chip_fine, dirt, rust_n = (value_noise(size, 8, 1, 5), value_noise(size, 32, 2, 3),
                                     value_noise(size, 4, 3, 4), value_noise(size, 16, 4, 5))
    tone = r.uniform(0.88, 1.08, n)[lab]
    # brushed streaks along each piece's long side
    line = np.asarray(Image.fromarray((r.random(size) * 255).astype(np.uint8)[None, :].repeat(size, 0)).filter(ImageFilter.GaussianBlur(0.8)), np.float64) / 255
    streak = np.where(horiz, line.T, line)
    orange = np.array([156, 86, 54])
    base = np.zeros((size, size, 3)) + orange
    grey = np.array([92, 88, 86])
    yellow = np.array([184, 150, 62])
    k = kind[..., None]
    # grate: dark slots across the piece's short side, ribs between them
    t = np.where(horiz, u, v)
    slot = ss(np.abs(((t / 14) % 1) - 0.5), 0.18, 0.26)
    inner = ss(d, 10, 12)
    grate = grey * (0.35 + 0.75 * np.maximum(slot, 1 - inner)[..., None])
    # crate: yellow cells with an X in each
    cs = np.minimum(W, H)
    cu, cv = (u % cs) / cs - 0.5, (v % cs) / cs - 0.5
    cross = np.minimum(np.abs(cu - cv), np.abs(cu + cv))
    border = np.maximum(np.abs(cu), np.abs(cv))
    crate_dark = np.maximum(1 - ss(cross, 0.05, 0.08), ss(border, 0.40, 0.43)) * ss(border, 0, 0.46)
    crate = yellow * (1 - 0.75 * crate_dark[..., None])
    # hazard: diagonal orange and dark stripes
    stripe = ss(np.abs(((u + v) / 34 % 1) - 0.5), 0.23, 0.27)
    hazard = np.array([140, 78, 48]) * (1 - stripe[..., None]) + np.array([60, 50, 46]) * stripe[..., None]
    base = np.where(k == 1, grate, base)
    base = np.where(k == 2, crate, base)
    base = np.where(k == 3, hazard, base)
    base = np.where(k == 4, grey * 1.15, base)
    p = base * tone[..., None] * (0.95 + 0.07 * streak[..., None]) * (0.82 + 0.32 * dirt[..., None])
    bevel = ss(d, 0, 6)
    top_left = np.minimum(u - gap, v - gap) < np.minimum(W - gap - u, H - gap - v)
    shade = bevel + (1 - bevel) * np.where(top_left, 1.3, 0.65)
    wear = ss(chip * 0.6 + chip_fine * 0.4 + 0.3 * (1 - ss(d, 0, 10)), 0.77, 0.80)
    steel = np.array([116, 112, 108]) * (0.8 + 0.4 * streak[..., None]) * (0.85 + 0.3 * chip_fine[..., None])
    rust = ss(rust_n + 0.16 * (1 - ss(d, 0, 22)), 0.71, 0.90) * np.where(kind == 1, 0.5, 1)
    rust_colour = np.array([92, 44, 24]) * (0.8 + 0.4 * chip_fine[..., None])
    q = p * (1 - wear[..., None]) + steel * wear[..., None]
    q = q * (1 - 0.8 * rust[..., None]) + rust_colour * 0.8 * rust[..., None]
    grime = 1 - 0.35 * (1 - ss(d, 0, 16)) * (0.6 + 0.4 * chip_fine)
    out = q * (shade * grime)[..., None]
    out = np.where((d > 0)[..., None], out, np.array([30, 27, 26]))
    plate = (d > 0).astype(float)
    # bolts in the corners of the bigger pieces
    for i, (bx, by, w, h) in enumerate(rects):
        if w * h < 3 or kinds[i] == 1:
            continue
        x0, y0, w, h = bx * U, by * U, w * U, h * U
        for cx, cy in [(x0 + 11, y0 + 11), (x0 + w - 11, y0 + 11), (x0 + 11, y0 + h - 11), (x0 + w - 11, y0 + h - 11)]:
            if r.random() < 0.2:
                continue  # a bolt lost
            # a small window round the bolt, wrapping at the edges
            rows, cols = np.arange(cy - 12, cy + 12) % size, np.arange(cx - 12, cx + 12) % size
            dy, dx = np.meshgrid(np.arange(-12, 12) + 0.5, np.arange(-12, 12) + 0.5, indexing="ij")
            rr = np.hypot(dx, dy)
            bolt = ss(4.5 - rr, 0, 1.0)[..., None]
            lit = np.clip(1.05 - (dx + dy) / 8, 0.5, 1.5)[..., None]
            patch = out[np.ix_(rows, cols)] * (1 - 0.45 * ss(6 - rr, 0, 1)[..., None] * (1 - bolt))
            out[np.ix_(rows, cols)] = patch * (1 - bolt) + np.array([60, 56, 54]) * lit * bolt
    # scratches, wrapping
    canvas = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(canvas)
    for _ in range(70):
        x0, y0 = r.uniform(0, size, 2)
        angle, length = r.uniform(0, np.pi), r.uniform(15, 90)
        pts = [(x0 + t * length * np.cos(angle) + r.normal(0, 0.6), y0 + t * length * np.sin(angle) + r.normal(0, 0.6)) for t in np.linspace(0, 1, 8)]
        shade_ = int(r.uniform(50, 140))
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                draw.line([(a + ox, b + oy) for a, b in pts], fill=shade_, width=1)
    sc = np.asarray(canvas, np.float64) / 255
    sc = np.clip((sc + np.roll(sc, 1, 0) + np.roll(sc, -1, 0) + np.roll(sc, 1, 1) + np.roll(sc, -1, 1)) / 5 * 1.6, 0, 1) * plate
    out = out * (1 - sc[..., None]) + np.array([132, 126, 120]) * sc[..., None]
    for _ in range(12):
        cx, cy = r.uniform(0, size, 2)
        radius = r.uniform(12, 28)
        dx, dy = (x - cx + size / 2) % size - size / 2, (y - cy + size / 2) % size - size / 2
        rr = np.hypot(dx, dy) / radius
        dent = np.exp(-rr ** 2 * 2) * plate
        rim = np.exp(-((rr - 0.9) ** 2) * 18) * plate * np.clip(-(dx + dy) / radius, 0, 1)
        out = out * (1 - 0.22 * dent[..., None]) * (1 + 0.18 * rim[..., None])
    out = out * (0.92 + 0.08 * value_noise(size, 8, 9, 3)[..., None])
    # muted a little towards grey, like weathered paint
    out = out * 0.88 + out.mean(axis=2, keepdims=True) * 0.12
    # rolled so a lone block (an icon, the build ghost: the cell at the grid origin) shows plates and a hazard strip
    return np.roll(out, (-1100, -1380), (0, 1))


def stretched_noise(size, cells_x, cells_y, seed, octaves=4):
    """value_noise with cells_x by cells_y cells: long features along x, like wood grain. Wraps at the edges."""
    r = np.random.default_rng(seed)
    out, amp, total = np.zeros((size, size)), 1.0, 0.0
    for _ in range(octaves):
        grid = np.tile(r.random((cells_y, cells_x)), (3, 3))
        g = Image.fromarray((grid * 255).astype(np.uint8)).resize((3 * size, 3 * size), Image.BICUBIC)
        out += amp * np.asarray(g, np.float64)[size:2 * size, size:2 * size] / 255
        total += amp
        amp *= 0.5
        cells_x, cells_y = cells_x * 2, cells_y * 2
    return out / total


def scrap_wood(size=2048, rows=16):
    """Weathered planks in staggered courses over one 2 m repeat (texture_span 8): each plank its own shade, with
    grain, knots, split ends and nails where planks butt together. Every pattern wraps at the edges."""
    ss = smoothstep
    r = np.random.default_rng(41)
    y, x = np.mgrid[0:size, 0:size] + 0.5
    ph = size // rows
    row = (y // ph).astype(int)
    v = y - row * ph
    # planks per course: random lengths that add up to the width, the course shifted at random
    starts = np.zeros((rows, 12)) + np.inf
    tones = np.zeros((rows, 12))
    for i in range(rows):
        edges, at = [], 0.0
        while at < size - 160:
            edges.append(at)
            at += r.uniform(190, 620)
        shift = r.uniform(0, size)
        starts[i, :len(edges)] = (np.array(edges) + shift) % size
        tones[i, :len(edges)] = r.uniform(0.82, 1.12, len(edges))
    # which plank each pixel is in: the last start at or before x, wrapping
    s = starts[row]  # (size, size, 12)
    rel = np.where(np.isfinite(s), (x[..., None] - np.nan_to_num(s, posinf=0)) % size, np.inf)
    k = np.argmin(rel, axis=-1)
    u = np.take_along_axis(rel, k[..., None], -1)[..., 0]
    count = np.isfinite(starts).sum(axis=1)
    # plank length: the gap to the next start in its course
    length = np.full(u.shape, float(size))
    for i in range(rows):
        st = np.sort(starts[i, :count[i]])
        lens = np.diff(np.append(st, st[0] + size))
        idx = np.searchsorted(st, starts[i, :count[i]])
        per = np.zeros(12)
        per[:count[i]] = lens[idx]
        mask = row == i
        length[mask] = per[k[mask]]
    tone = tones[row, k]
    gap = 3
    d = np.minimum(np.minimum(u - gap, length - gap - u), np.minimum(v - gap, ph - gap - v))
    # grain: stretched noise, offset per plank so neighbours differ
    grain = stretched_noise(size, 6, 96, 42, 4)
    fine = stretched_noise(size, 24, 384, 43, 2)
    offset = (k * 997 + row * 131) % size
    gx = ((x + offset).astype(int)) % size
    g = grain[y.astype(int), gx]
    f = fine[y.astype(int), gx]
    lines = ss(np.abs(((g * 9) % 1) - 0.5), 0.36, 0.5)  # the dark rings of the grain
    wood = np.array([100, 64, 38]) * tone[..., None]
    wood = wood * (0.86 + 0.18 * f[..., None]) * (1 - 0.22 * lines[..., None])
    blotch = value_noise(size, 8, 44, 4)
    wood = wood * (0.85 + 0.25 * blotch[..., None])
    # knots: a few dark rings per repeat
    for _ in range(14):
        cx, cy = r.uniform(0, size, 2)
        rad = r.uniform(7, 14)
        dx, dy = (x - cx + size / 2) % size - size / 2, (y - cy + size / 2) % size - size / 2
        rr = np.hypot(dx / 1.8, dy) / rad
        knot = np.exp(-rr ** 2 * 1.5) + 0.4 * ss(np.abs(((rr * 3) % 1) - 0.5), 0.3, 0.5) * np.exp(-rr ** 2 * 0.4)
        wood = wood * (1 - 0.45 * np.clip(knot, 0, 1)[..., None])
    # rounded plank edges lit from above, darker at the ends
    bevel = ss(d, 0, 7)
    end = np.minimum(u - gap, length - gap - u) < np.minimum(v - gap, ph - gap - v)
    top = (v - gap) < (ph - gap - v)
    shade = bevel + (1 - bevel) * np.where(end, np.where(u < length / 2, 1.08, 0.8), np.where(top, 1.18, 0.62))
    grime = 1 - 0.3 * (1 - ss(d, 0, 18))
    out = wood * (shade * grime)[..., None]
    out = np.where((d > 0)[..., None], out, np.array([44, 30, 22]))
    # split ends and nails where planks butt together
    canvas = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(canvas)
    for i in range(rows):
        for st in starts[i, :count[i]]:
            for end, sign in ((st, 1), (st, -1)):
                if r.random() < 0.55:
                    y0 = i * ph + r.uniform(0.3, 0.7) * ph
                    reach = r.uniform(10, 26)
                    pts = [(end + sign * t * reach, y0 + r.normal(0, 2.5) * t) for t in np.linspace(0, 1, 5)]
                    for ox in (-size, 0, size):
                        draw.line([(a + ox, b) for a, b in pts], fill=255, width=2)
                        a, b = pts[2]
                        draw.line([(a + ox, b), (a + ox + sign * reach * 0.4, b + r.choice([-1, 1]) * reach * 0.35)], fill=200, width=1)
                for ny in (0.28, 0.72):
                    cx, cy = (end + sign * 12) % size, i * ph + ny * ph
                    rows_, cols_ = np.arange(int(cy) - 6, int(cy) + 6) % size, np.arange(int(cx) - 6, int(cx) + 6) % size
                    dy, dx = np.meshgrid(np.arange(-6, 6) + 0.5, np.arange(-6, 6) + 0.5, indexing="ij")
                    nail = ss(3.2 - np.hypot(dx, dy), 0, 1)[..., None]
                    out[np.ix_(rows_, cols_)] = out[np.ix_(rows_, cols_)] * (1 - nail) + np.array([58, 52, 50]) * nail
    crack = np.asarray(canvas.filter(ImageFilter.GaussianBlur(0.6)), np.float64) / 255
    out = out * (1 - 0.7 * crack[..., None])
    return out * (0.94 + 0.06 * value_noise(size, 8, 45, 3)[..., None])


def scrap_stone(size=2048, units=32):
    """Dressed grey stone over one 2 m repeat (texture_span 8): slabs of many sizes cut at slightly different depths,
    so only some joints show, with wobbly worn edges, chisel strokes and chipped corners. Shaded from a height map lit
    from the top left. Every pattern wraps at the edges."""
    ss = smoothstep
    U = size // units
    rects, taken = pack(units, 51)
    r = np.random.default_rng(52)
    # about half the slabs sit proud or sunk; the rest are flush, so their joints barely show
    depth = np.where(r.random(len(rects)) < 0.55, r.uniform(-1, 1, len(rects)) * 6, 0.0)
    tone = r.uniform(0.95, 1.05, len(rects))
    y, x = np.mgrid[0:size, 0:size] + 0.5
    # wobble the joints: look the slab up at a position pushed about by noise
    wx = x + 10 * (value_noise(size, 16, 56, 3) - 0.5)
    wy = y + 10 * (value_noise(size, 16, 57, 3) - 0.5)
    lab = taken[(wy // U).astype(int) % units, (wx // U).astype(int) % units]
    height = depth[lab]
    tiled = Image.fromarray(np.tile((height + 10) * 10, (3, 3)).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2))
    height = np.asarray(tiled, np.float64)[size:2 * size, size:2 * size] / 10 - 10
    # chisel strokes running down the stone, and broad unevenness
    height += 1.2 * stretched_noise(size, 256, 12, 53, 3)
    height += 5 * value_noise(size, 6, 54, 4)
    # chips: ragged bites out of a slab edge, deepest at the edge
    ragged = value_noise(size, 128, 58, 2)
    for bx, by, w, h in rects:
        for _ in range(int(r.integers(0, 3))):
            side = int(r.integers(0, 4))
            length, reach = r.uniform(24, 80), r.uniform(10, 24)
            along = r.uniform(0, max(1.0, (w if side < 2 else h) * U - length))
            if side < 2:  # top or bottom edge
                x0, y0, cw, ch = bx * U + along, by * U + (0 if side == 0 else h * U - reach), length, reach
            else:  # left or right edge
                x0, y0, cw, ch = bx * U + (0 if side == 2 else w * U - reach), by * U + along, reach, length
            rows, cols = np.arange(int(y0), int(y0 + ch)) % size, np.arange(int(x0), int(x0 + cw)) % size
            dy, dx = np.meshgrid(np.arange(len(rows)) + 0.5, np.arange(len(cols)) + 0.5, indexing="ij")
            inward = [dy, ch - dy, dx, cw - dx][side] / reach  # 0 at the edge, 1 at the far side of the bite
            ends = np.minimum(dx, cw - dx) / cw if side < 2 else np.minimum(dy, ch - dy) / ch
            bite = ss(ragged[np.ix_(rows, cols)] + 0.55 - inward - 0.6 * (1 - ss(ends, 0, 0.35)), 0.3, 0.45)
            height[np.ix_(rows, cols)] -= bite * 5
    # light from the top left: a slope facing it is brighter
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) / 2
    light = np.clip(1 - 0.28 * (gx + gy), 0.45, 1.5)
    stone = np.array([72, 71, 69]) * tone[lab][..., None] * (0.92 + 0.12 * value_noise(size, 12, 55, 4)[..., None])
    return stone * light[..., None]


def net():
    """Rope netting: two sets of diagonal strands, woven over and under, with clear gaps (alpha 0)."""
    period = N / 6
    half = 7.5
    y, x = np.mgrid[0:N, 0:N] + 0.5
    def distance(u):
        return np.abs(((u + period / 2) % period) - period / 2) / np.sqrt(2)
    def strand(d):
        return np.clip(half + 0.5 - d, 0, 1), np.sqrt(np.clip(1 - (d / half) ** 2, 0, 1))
    a1, h1 = strand(distance(x + y))
    a2, h2 = strand(distance(x - y))
    first_on_top = (np.floor((x + y) / period) + np.floor((x - y) / period)) % 2 == 0
    shade = np.where(a1 * a2 > 0, np.where(first_on_top, h1, h2), np.maximum(h1 * a1, h2 * a2))
    rng = np.random.default_rng(7)
    grain = np.asarray(Image.fromarray((rng.random((N // 4, N // 4)) * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC), np.float64) / 255
    twist = 0.5 + 0.5 * np.sin(np.where(a1 >= a2, x - y, x + y) * 0.55)
    base = 55 + 60 * shade + 18 * twist * shade + 20 * (grain - 0.5)
    return np.dstack([base * 0.98, base * 0.97, base * 0.92]), np.maximum(a1, a2)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla", "textures", "blocks")
    os.makedirs(out, exist_ok=True)
    for name, c in CUTS.items():
        a = cut(os.path.join(src, f"{name}.jpg"), c)
        lum = a.mean(axis=2)
        if name == "armored_glass":
            # Frame solid, panes see-through (multiplied by the part's opacity).
            save(os.path.join(out, f"{name}.png"), a, 0.45 + 0.55 * (1 - smoothstep(lum, 160, 190)))
        elif name == "glass":
            save(os.path.join(out, f"{name}.png"), a, 0.75 + 0.25 * smoothstep(lum, 170, 235))
        else:
            save(os.path.join(out, f"{name}.png"), a)
        if name == "path_light":
            # Glow map: the yellow strips (red and green well above blue).
            strips = smoothstep((a[..., 0] + a[..., 1]) / 2 - a[..., 2], 60, 200) * 255
            save(os.path.join(out, "path_light_glow.png"), np.dstack([strips] * 3))
        print(name)
    save(os.path.join(out, "scrap_metal.png"), scrap_metal())
    print("scrap_metal")
    save(os.path.join(out, "scrap_wood.png"), scrap_wood())
    print("scrap_wood")
    save(os.path.join(out, "scrap_stone.png"), scrap_stone())
    print("scrap_stone")
    save(os.path.join(out, "net.png"), *net())
    print("net")


if __name__ == "__main__":
    main()
