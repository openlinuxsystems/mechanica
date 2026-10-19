"""Painted textures for the trees and logs (used by build_trees.py; numpy and Pillow only, deterministic).

* Bark (``bark(kind)``): a tileable albedo and tangent-space normal map, one tile per kind. U wraps round the trunk,
  V runs up it. Kinds: ``oak`` (Leafy and autumn trees: deep vertical furrows), ``birch`` (white with black dashes),
  ``spruce`` (grey-brown fine scales), ``pine`` (red-brown plates with dark gaps), ``charred`` (burnt, cracked black).
* Foliage (``foliage(kind)``): a 2 x 2 atlas of leaf-cluster cards with alpha, painted as stylised leaves round a twig;
  the four tiles run from light (sunlit, outer cards) to dark (shaded, inner cards). Kinds: ``leafy``, ``autumn``,
  ``birch``, ``spruce`` (drooping needle sprays) and ``pine`` (upturned needle tufts).
* End grain (``end_grain(kind)``): the cut face of a log: rings round a pale heart, a bark rim, a few cracks.

Colours are sRGB. Everything is drawn from seeded random numbers, so the same call gives the same pixels.
"""

from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def hexrgb(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])


# =====================================================================================================
# Tileable noise
# =====================================================================================================

def noise(size, rng, scale_u, scale_v):
    """Tileable smooth noise in [-1, 1]: white noise low-passed in frequency space. `scale_u`, `scale_v` are feature
    sizes in pixels along each axis (large `scale_v` with small `scale_u` gives vertical streaks)."""
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    filt = np.exp(-((fx * scale_u) ** 2 + (fy * scale_v) ** 2) * 2.0)
    n = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
    n -= n.mean()
    return n / (np.abs(n).max() + 1e-9)


def voronoi(size, rng, count, stretch_v=1.0):
    """Tileable Voronoi: (distance to the nearest point, distance to the second nearest minus the nearest), in pixels.
    `stretch_v` > 1 makes cells taller than wide."""
    pts = rng.uniform(0, size, (count, 2))
    ys, xs = np.mgrid[0:size, 0:size].astype(float)
    d1 = np.full((size, size), 1e9)
    d2 = np.full((size, size), 1e9)
    for px, py in pts:
        dx = np.abs(xs - px)
        dx = np.minimum(dx, size - dx)
        dy = np.abs(ys - py)
        dy = np.minimum(dy, size - dy) / stretch_v
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        d1 = np.where(closer, d, d1)
    return d1, d2 - d1


def normal_map(height, strength):
    """Tangent-space normal map (RGB in [0, 1]) from a tileable height field; +V is up the image."""
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    gy = (np.roll(height, 1, axis=0) - np.roll(height, -1, axis=0)) * 0.5
    n = np.stack([-gx * strength, -gy * strength, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def mix(a, b, t):
    t = np.clip(t, 0, 1)[..., None]
    return a * (1 - t) + b * t


# =====================================================================================================
# Bark
# =====================================================================================================

BARK_SIZE = 512


def bark(kind):
    """(albedo RGB, normal RGB) float arrays, BARK_SIZE square, tileable."""
    s = BARK_SIZE
    rng = np.random.default_rng({"oak": 1, "birch": 2, "spruce": 3, "pine": 4, "charred": 5}[kind])
    if kind == "oak":
        streak = noise(s, rng, 9, 70)
        fine = noise(s, rng, 3, 12)
        ridge = np.abs(streak + 0.25 * fine)
        height = 1 - np.clip(ridge * 1.6, 0, 1) ** 0.6           # furrows are where the streak field crosses zero
        height += 0.15 * noise(s, rng, 20, 20)
        base = mix(hexrgb("#3d2615")[None, None], hexrgb("#8a5d36")[None, None], height)
        base = mix(base, hexrgb("#a37349")[None, None], np.clip(fine, 0, 1) * height * 0.5)
        return base, normal_map(height * 10, 1.6)
    if kind == "birch":
        streak = noise(s, rng, 60, 8)
        height = 0.6 + 0.2 * streak + 0.1 * noise(s, rng, 4, 4)
        base = mix(hexrgb("#d9d4c8")[None, None], hexrgb("#f4f1ea")[None, None], 0.5 + 0.5 * streak)
        # Black lenticel dashes and a few torn patches, wide round the trunk and thin up it.
        img = Image.new("L", (s * 3, s * 3), 0)
        d = ImageDraw.Draw(img)
        for _ in range(95):
            x, y = rng.uniform(0, s), rng.uniform(0, s)
            w, h = rng.uniform(14, 52), rng.uniform(2.5, 6.5)
            for ox in (-s, 0, s):
                for oy in (-s, 0, s):
                    cx, cy = (x + ox + s) * 1.0, (y + oy + s) * 1.0
                    d.ellipse([cx - w, cy - h, cx + w, cy + h], fill=255)
                    if rng.uniform() < 0.25:
                        d.polygon([(cx - w * 0.6, cy), (cx, cy - h * 2.6), (cx + w * 0.5, cy)], fill=255)
        dash = np.asarray(img.filter(ImageFilter.GaussianBlur(1.2)), float)[s:2 * s, s:2 * s] / 255.0
        base = mix(base, hexrgb("#262422")[None, None], dash)
        height -= dash * 0.5
        return base, normal_map(height * 8, 1.2)
    if kind == "spruce":
        d1, edge = voronoi(s, rng, 260, stretch_v=1.6)
        scale = np.clip(edge / 6.0, 0, 1)
        height = scale * (1 - d1 / 40.0).clip(0, 1) * 0.6 + scale * 0.4
        tone = noise(s, rng, 30, 30)
        base = mix(hexrgb("#3a2a1f")[None, None], hexrgb("#7a5a44")[None, None], height)
        base = mix(base, hexrgb("#8d8579")[None, None], np.clip(tone, 0, 1) * 0.45)
        return base, normal_map(height * 8, 1.4)
    if kind == "pine":
        d1, edge = voronoi(s, rng, 70, stretch_v=2.4)
        plate = np.clip(edge / 9.0, 0, 1)
        warp = noise(s, rng, 25, 25)
        height = plate * (0.75 + 0.25 * warp)
        base = mix(hexrgb("#2b1a12")[None, None], hexrgb("#9a5634")[None, None], plate)
        base = mix(base, hexrgb("#b9784b")[None, None], np.clip(warp, 0, 1) * plate * 0.6)
        return base, normal_map(height * 12, 1.4)
    if kind == "charred":
        d1, edge = voronoi(s, rng, 70, stretch_v=2.2)
        crack = 1 - np.clip(edge / 6.0, 0, 1)
        tone = noise(s, rng, 20, 50)
        height = 1 - crack * 0.8 + 0.1 * tone
        base = mix(hexrgb("#2c2826")[None, None], hexrgb("#161312")[None, None], crack)
        base = mix(base, hexrgb("#45403b")[None, None], np.clip(tone, 0, 1) * 0.6 * (1 - crack))
        return base, normal_map(height * 10, 1.5)
    raise ValueError(kind)


# =====================================================================================================
# Foliage cards
# =====================================================================================================

TILE = 512
SS = 2   # drawn at twice the size, then filtered down

FOLIAGE = {
    # leaf colours (light to dark), twig colour, leaf shape
    "leafy": (["#98cc44", "#7cb735", "#63a02b", "#4c8924", "#3a721c"], "#5b3b22", "oval"),
    "autumn": (["#f7b534", "#f08d22", "#e2661c", "#cc4a1a", "#a8341a"], "#5b3b22", "oval"),
    "birch": (["#fbd84a", "#f4c41c", "#eba413", "#e38912", "#c86e10"], "#4a3324", "round"),
    "spruce": (["#5f9a4c", "#4d873f", "#3d7436", "#30602e", "#244d26"], "#4b3323", "needles"),
    "pine": (["#729c4c", "#5c8842", "#4a7438", "#3a602f", "#2d4e26"], "#5e3a24", "tuft"),
}


def _leaf(draw, at, angle, length, width, colour, shape, rng):
    """One stylised leaf: a pointed oval (or a rounder birch leaf) with a lighter tip and a midrib."""
    ca, sa = math.cos(angle), math.sin(angle)

    def p(u, v):  # u along the leaf, v across
        return (at[0] + ca * u - sa * v, at[1] + sa * u + ca * v)

    n = 14
    pts = []
    for i in range(n + 1):
        t = i / n
        if shape == "round":
            w = width * math.sin(math.pi * t) ** 0.75 * (1.0 - 0.25 * t)
        else:
            w = width * math.sin(math.pi * t ** 0.85) * (1.0 - 0.15 * t)
        pts.append(p(length * t, w))
    for i in range(n, -1, -1):
        t = i / n
        if shape == "round":
            w = width * math.sin(math.pi * t) ** 0.75 * (1.0 - 0.25 * t)
        else:
            w = width * math.sin(math.pi * t ** 0.85) * (1.0 - 0.15 * t)
        pts.append(p(length * t, -w))
    dark = tuple(int(c * 0.72) for c in colour)
    draw.polygon(pts, fill=dark)
    # Inner face, a little smaller, so a darker rim outlines each leaf; a lighter half where the light falls.
    inner = [(at[0] + (x - at[0]) * 0.9 + ca * length * 0.05, at[1] + (y - at[1]) * 0.9 + sa * length * 0.05) for x, y in pts]
    draw.polygon(inner, fill=colour)
    light = tuple(min(255, int(c * 1.12 + 10)) for c in colour)
    half = [q for q, i in zip(inner, range(len(inner))) if i <= n] + [p(length * 0.92, 0), p(length * 0.05, 0)]
    draw.polygon(half, fill=light)
    draw.line([p(length * 0.02, 0), p(length * 0.85, 0)], fill=dark, width=max(1, int(width * 0.12)))


def _twig(draw, a, b, width, colour):
    draw.line([a, b], fill=colour, width=int(width))


def _cluster(kind, tone, rng):
    """One card: RGBA uint8, TILE square, leaves on a twig from the bottom middle. `tone` 0 (light) .. 3 (dark)."""
    colours, twig_hex, shape = FOLIAGE[kind]
    s = TILE * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    twig = tuple(int(c * 255) for c in hexrgb(twig_hex)) + (255,)
    pal = [tuple(int(c * 255) for c in hexrgb(h)) for h in colours]
    # Each tone leans on its own end of the palette.
    weights = np.array([[5, 4, 2, 1, 0.3], [2, 4, 4, 2, 0.5], [0.5, 2, 4, 4, 2], [0.2, 1, 2, 4, 5]][tone], float)
    weights /= weights.sum()

    def colour():
        c = np.array(pal[int(rng.choice(5, p=weights))], float) * rng.uniform(0.94, 1.06)
        return tuple(int(x) for x in np.clip(c, 0, 255)) + (255,)

    margin = s * 0.06
    if shape in ("oval", "round"):
        base = (s / 2, s - margin)
        # A forked twig, then leaves filling a round crown round it, back to front so the front ones overlap.
        tips = []
        for k in range(5):
            ang = -math.pi / 2 + (k - 2) * 0.42 + rng.uniform(-0.1, 0.1)
            ln = s * rng.uniform(0.34, 0.46)
            mid = (base[0] + math.cos(ang) * ln * 0.45, base[1] + math.sin(ang) * ln * 0.45 - s * 0.08)
            tip = (base[0] + math.cos(ang) * ln, base[1] + math.sin(ang) * ln - s * 0.12)
            _twig(d, base, mid, s * 0.016, twig)
            _twig(d, mid, tip, s * 0.009, twig)
            tips.append(tip)
        count = 78 if shape == "oval" else 120
        size = (0.16, 0.058) if shape == "oval" else (0.1, 0.052)
        leaves = []
        for _ in range(count):
            r = math.sqrt(rng.uniform(0, 1)) * s * 0.37
            th = rng.uniform(0, 2 * math.pi)
            c = (s / 2 + r * math.cos(th), s * 0.45 + r * math.sin(th) * 0.95)
            # Leaves point away from the middle and a little down, as if hanging off the twigs.
            ang = math.atan2(c[1] - s * 0.5, c[0] - s / 2) + rng.uniform(-0.6, 0.6)
            leaves.append((c[1], c, ang))
        for _, c, ang in sorted(leaves, key=lambda t: t[0]):
            ln = s * size[0] * rng.uniform(0.8, 1.15)
            _leaf(d, c, ang, ln, ln * size[1] / size[0], colour()[:3], shape, rng)
    elif shape == "needles":
        # A drooping spruce spray: a main twig with side twigs, every twig thick with short needles.
        base = (s * 0.5, margin)
        main_end = (s * 0.5, s - margin * 1.5)
        twigs = [(base, main_end, s * 0.012)]
        for k in range(9):
            t = 0.1 + 0.095 * k
            at = (base[0] + (main_end[0] - base[0]) * t, base[1] + (main_end[1] - base[1]) * t)
            for side in (-1, 1):
                ln = s * (0.4 - 0.032 * k) * rng.uniform(0.85, 1.05)
                end = (at[0] + side * ln * 0.9, at[1] + ln * 0.45)
                twigs.append((at, end, s * 0.007))
        for a, b, w in twigs:
            _twig(d, a, b, w, twig)
        for a, b, w in twigs:
            length = math.dist(a, b)
            ang = math.atan2(b[1] - a[1], b[0] - a[0])
            for i in range(int(length / (s * 0.008))):
                t = i / max(1, length / (s * 0.008))
                p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
                nl = s * 0.075 * (1 - 0.4 * t) * rng.uniform(0.8, 1.1)
                for side in (-1, 1):
                    na = ang + side * rng.uniform(0.75, 1.05) + 0.25
                    q = (p[0] + math.cos(na) * nl, p[1] + math.sin(na) * nl)
                    d.line([p, q], fill=colour(), width=int(s * 0.011))
    else:  # tuft
        # An upturned pine tuft: long needles fanning up from a short twig end.
        base = (s * 0.5, s - margin)
        top = (s * 0.5, s * 0.62)
        _twig(d, base, top, s * 0.022, twig)
        for _ in range(460):
            t = rng.uniform(0, 1)
            p = (base[0] + (top[0] - base[0]) * t, base[1] + (top[1] - base[1]) * t)
            ang = -math.pi / 2 + rng.normal(0, 0.72)
            nl = s * rng.uniform(0.26, 0.46) * (1 - 0.25 * t)
            q = (p[0] + math.cos(ang) * nl, p[1] + math.sin(ang) * nl)
            q = (min(max(q[0], margin), s - margin), max(q[1], margin))
            d.line([p, q], fill=colour(), width=int(s * 0.011))
    img = img.resize((TILE, TILE), Image.LANCZOS)
    return np.asarray(img).copy()


def _bleed(rgba):
    """Spreads leaf colour into the see-through pixels, so filtered edges do not pick up a dark fringe."""
    rgb = rgba[..., :3].astype(float)
    a = (rgba[..., 3] > 8).astype(float)
    acc, wsum = rgb * a[..., None], a.copy()
    out = rgb.copy()
    filled = a > 0
    for _ in range(24):
        acc_n = sum(np.roll(np.roll(acc, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        w_n = sum(np.roll(np.roll(wsum, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        new = (~filled) & (w_n > 0)
        out[new] = acc_n[new] / w_n[new, None]
        acc = np.where(new[..., None], out, acc * 1.0)
        wsum = np.where(new, 1.0, wsum)
        acc = out * wsum[..., None]
        filled = filled | new
    mean = rgb[a > 0].mean(axis=0) if a.any() else np.zeros(3)
    out[~filled] = mean
    res = rgba.copy()
    res[..., :3] = np.clip(out, 0, 255).astype(np.uint8)
    return res


def foliage(kind):
    """RGBA uint8 atlas, 2 TILEs square: tiles light (top left), mid-light (top right), mid-dark, dark (bottom right)."""
    rng = np.random.default_rng({"leafy": 11, "autumn": 11, "birch": 12, "spruce": 13, "pine": 14}[kind])
    atlas = np.zeros((TILE * 2, TILE * 2, 4), np.uint8)
    for tone in range(4):
        tile = _cluster(kind, tone, rng)
        r, c = divmod(tone, 2)
        atlas[r * TILE:(r + 1) * TILE, c * TILE:(c + 1) * TILE] = tile
    return _bleed(atlas)


def tile_uv(tone):
    """UV rectangle (u0, v0, u1, v1) of a foliage tile, in glTF UV space (V down from the top of the image)."""
    r, c = divmod(tone, 2)
    return c * 0.5, r * 0.5, c * 0.5 + 0.5, r * 0.5 + 0.5


# =====================================================================================================
# Log end grain
# =====================================================================================================

def end_grain(kind):
    """RGB float array, 256 square: rings round the middle, a bark rim at the edge, a couple of radial cracks."""
    s = 256
    rng = np.random.default_rng({"oak": 21, "birch": 22, "pine": 23, "spruce": 24, "charred": 25}[kind])
    ys, xs = (np.mgrid[0:s, 0:s] - (s - 1) / 2) / (s / 2)
    r = np.sqrt(xs * xs + ys * ys)
    th = np.arctan2(ys, xs)
    wobble = 0.02 * np.sin(th * 5 + 1.3) + 0.015 * np.sin(th * 9 + 0.4)
    rings = 0.5 + 0.5 * np.cos((r + wobble) * 2 * math.pi * 7)
    heart, sap = {"oak": ("#c98e55", "#e6bd85"), "birch": ("#e2c9a0", "#f0dfbf"), "pine": ("#d79a5a", "#efc68d"),
                  "spruce": ("#d9b07a", "#ecd2a3"), "charred": ("#5a4636", "#7a6450")}[kind]
    rim = {"oak": "#5b3a22", "birch": "#e9e5dc", "pine": "#7b4127", "spruce": "#5e4434", "charred": "#1c1816"}[kind]
    col = mix(hexrgb(heart)[None, None], hexrgb(sap)[None, None], np.clip(r * 1.2, 0, 1))
    col = mix(col, col * 0.82, rings * 0.6)
    col = mix(col, hexrgb(heart)[None, None] * 0.7, np.clip(0.12 - r, 0, 1) * 8)
    for _ in range(2):
        a = rng.uniform(-math.pi, math.pi)
        da = np.abs((th - a + math.pi) % (2 * math.pi) - math.pi)
        crack = (da < 0.03 * (1 - r)) & (r > 0.15) & (r < 0.85)
        col[crack] *= 0.55
    col = mix(col, hexrgb(rim)[None, None], np.clip((r - 0.9) * 20, 0, 1))
    return col


def save_rgb(path, rgb):
    Image.fromarray(np.round(np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(path)
