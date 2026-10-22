"""Procedural textures for the Soil Bag and the soil mound (used by build_soil.py).

Everything is numpy on texel grids, seeded, so the build is deterministic. Lengths are metres.

* ``PrintedBag`` - the Soil Bag's printed plastic: the Rootwell artwork from ``bag_print.py`` with creases out of the
  sealed ends, a faint crinkle, scuffed edges and a little soil dust.
* ``rope`` - a twisted cord (three strands), used by other props' builds.
* ``mound`` - the soil mound in its dry state: pale tan crumbly clods with dark crevices between them, faint dry
  cracks and grit. The game darkens and glosses it for water (the wetness states are a material parameter).
"""

from __future__ import annotations

import numpy as np

import rock_textures as rt
from rock_textures import smoothstep, srgb

# ---- Palettes ---------------------------------------------------------------------------------------
TWINE_LIGHT, TWINE_DARK = srgb("#b59a6c"), srgb("#7a6243")
GRIME = srgb("#4b3826")

SOIL_LIGHT, SOIL, SOIL_DARK = srgb("#b38f62"), srgb("#8f6c46"), srgb("#4f3824")
SOIL_GRIT = srgb("#d6bf98")


def _hash2(ix, iy, seed):
    """A repeatable pseudo-random value in [0, 1) per integer lattice point."""
    h = (ix.astype(np.int64) * 374761393 + iy.astype(np.int64) * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0x1000000)


def worley2(x, y, cell, seed):
    """F1 and F2 distances (metres) to jittered points of a grid of `cell` metres, on 2D coordinates."""
    gx, gy = x / cell, y / cell
    ix, iy = np.floor(gx).astype(np.int64), np.floor(gy).astype(np.int64)
    f1 = np.full(x.shape, 9.0)
    f2 = np.full(x.shape, 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            cx, cy = ix + dx, iy + dy
            px = cx + 0.1 + 0.8 * _hash2(cx, cy, seed)
            py = cy + 0.1 + 0.8 * _hash2(cx, cy, seed + 1)
            d = np.hypot(gx - px, gy - py)
            f2 = np.where(d < f1, f1, np.minimum(f2, d))
            f1 = np.minimum(f1, d)
    return f1 * cell, f2 * cell


# =====================================================================================================
# The Soil Bag
# =====================================================================================================

def sprout_sdf_up(a, b):
    """The sprout standing up: stem from b = -0.052 to 0, leaves rising left and right from the stem top."""
    stem = np.maximum(np.abs(a) - 0.0072, np.maximum(b - 0.004, -0.052 - b))
    out = stem
    for side in (-1, 1):
        ang = np.radians(36)                                # leaf axis 36 degrees up from horizontal
        dx, dy = side * np.cos(ang), np.sin(ang)
        u = a * dx + b * dy                                 # along the leaf, from the stem top
        v = -a * dy + b * dx                                # across it
        L, W = 0.07, 0.042
        r = (L * L / 4 + W * W / 4) / W
        c_off = r - W / 2
        d1 = np.hypot(u - L / 2, v - c_off) - r
        d2 = np.hypot(u - L / 2, v + c_off) - r
        out = np.minimum(out, np.maximum(np.maximum(d1, d2), -u))
    return out


class PrintedBag:
    """Surface callback for rock_textures.bake: the Soil Bag's printed plastic (the artwork from bag_print), with soft
    creases running out of the sealed ends, a faint crinkle all over, scuffs on the edges and a little soil dust
    on the bottom and round the seals."""

    def __init__(self, half, seed):
        self.half = np.asarray(half, float)
        self.noise = rt.Noise3(seed)
        self.fine = rt.Noise3(seed + 1)
        self.stain = rt.Noise3(seed + 2)

    def __call__(self, P, n, edge_d):
        import bag_print
        x, y = P[:, 0], P[:, 1]
        col = bag_print.print_colour(P, n)

        # creases fanning out of each sealed end, fading toward the middle of the bag
        ends = smoothstep(0.36, 0.6, np.abs(x))
        wob = self.noise.fbm(P, 6.0, octaves=2)
        crease = np.sin(y * 70.0 + wob * 2.5 + np.sign(x) * 1.3) * ends
        crinkle = self.fine.fbm(P, 28.0, octaves=3)
        height = 0.0016 * crease + 0.00035 * crinkle
        col = col * (1.0 + 0.05 * crease * ends)[:, None]

        # the print scuffs pale on the edges, and soil dust gathers underneath and at the seals
        st = 0.5 + 0.5 * self.stain.fbm(P, 9.0, octaves=3)
        scuff = smoothstep(0.03, 0.0, edge_d) * smoothstep(0.55, 0.85, st) * 0.35
        col = col + (np.array([0.93, 0.9, 0.82]) - col) * scuff[:, None]
        under = (n[2] < -0.5) * (0.15 + 0.3 * st)
        seal = smoothstep(0.52, 0.62, np.abs(x)) * smoothstep(0.45, 0.8, st) * 0.3
        dust = np.clip(np.maximum(under, seal), 0, 0.5)
        col = col + (GRIME - col) * dust[:, None]
        return {"albedo": np.clip(col, 0, 1), "height": height}


def seal_texture(w=512, h=16):
    """Albedo and height of the heat seals: u 0..0.5 is the yellow end's seal, 0.5..1 the green end's, each with fine
    crimp ridges across it (the ripple in the geometry is the coarse crimp)."""
    import bag_print
    u = (np.arange(w) + 0.5) / w
    col = np.where((u < 0.5)[:, None], np.asarray(bag_print.YELLOW_DEEP) / 255.0, np.asarray(bag_print.GREEN) / 255.0)
    ridge = 0.5 + 0.5 * np.sin(u * 2 * np.pi * 96)
    col = col * (0.9 + 0.1 * ridge)[:, None]
    return np.repeat(np.clip(col, 0, 1)[None], h, 0), np.repeat(ridge[None], h, 0)


def rope_texture(w=256, h=256, seed=31, light=TWINE_LIGHT, dark=TWINE_DARK, strands=3):
    """Albedo and height of a cord, tiling: u round the cord (0..1), v along it (one repeat per 0.05 m)."""
    jj, ii = np.mgrid[0:h, 0:w]
    u, v = (ii + 0.5) / w, (jj + 0.5) / h
    phase = np.mod((u + v * 1.0) * strands, 1.0)          # three strands spiralling along the rope
    ridge = np.sin(np.pi * phase)
    fib = np.sin(2 * np.pi * (u * 24 + v * 30)) * 0.5 + 0.5
    noise = rt.Noise3(seed).fbm(np.stack([u.ravel() * 4, v.ravel() * 4, np.zeros(u.size)], -1), 3.0, octaves=3).reshape(h, w)
    col = dark[None, None] + (light - dark)[None, None] * (ridge ** 0.6 * (0.85 + 0.15 * fib))[..., None]
    col = col * (1.0 + 0.08 * noise)[..., None]
    height = ridge ** 0.6 + 0.15 * fib
    return np.clip(col, 0, 1), height


# =====================================================================================================
# The soil mound
# =====================================================================================================

def mound_maps(size, half, seed, crown_height):
    """Albedo, height (metres) and the coarse clod field of the dry mound on a size x size texel grid covering
    [-half, half] in x and z (texel row 0 at z = -half). `crown_height(x, z)` is the mound's own height, used to keep
    cracks on the top and grit near the base."""
    jj, ii = np.mgrid[0:size, 0:size]
    x = -half + (ii + 0.5) / size * 2 * half
    z = -half + (jj + 0.5) / size * 2 * half
    pts = np.stack([x.ravel(), z.ravel(), np.zeros(x.size)], -1)
    n1 = rt.Noise3(seed).fbm(pts, 8.0, octaves=4).reshape(size, size)
    n2 = rt.Noise3(seed + 1).fbm(pts, 60.0, octaves=3).reshape(size, size)

    # clods: rounded lumps of a few sizes heaped together, crumbly rather than tiled
    lumps = np.zeros_like(x)
    shade = np.zeros_like(x)
    for i, (cell, amp) in enumerate(((0.03, 0.009), (0.016, 0.0055), (0.0075, 0.0025))):
        f1, f2 = worley2(x + 0.13 * i, z - 0.07 * i, cell, seed + 2 + i)
        r = f1 / (cell * (0.55 + 0.25 * _hash2(np.floor(x / cell).astype(np.int64), np.floor(z / cell).astype(np.int64), seed + 9 + i)))
        blob = np.clip(1.0 - r * r, 0, 1) ** 0.7
        lumps = np.maximum(lumps, blob * amp * (0.8 + 0.4 * n2))
        shade = np.maximum(shade, blob * [1.0, 0.8, 0.6][i])
    height = lumps + 0.0015 * n2 + 0.0008 * n1
    big = smoothstep(0.1, 0.6, shade)
    dome = smoothstep(0.4, 1.0, shade)
    small = big

    # faint dry cracks on the top
    c1, c2 = worley2(x, z, 0.11, seed + 7)
    top = smoothstep(0.03, 0.09, crown_height(x, z))
    crack = (1.0 - smoothstep(0.0004, 0.0014, c2 - c1)) * top * smoothstep(-0.2, 0.3, n1)
    height = height - 0.0025 * crack

    # colour: lighter on the clods' tops, darker in the hollows between them, grit specks
    tone = 0.5 + 0.5 * n1
    col = SOIL[None, None] * (0.92 + 0.14 * tone + 0.08 * n2)[..., None]
    col = col + (SOIL_LIGHT - col) * (0.5 * dome)[..., None]
    crevice = 1.0 - big
    col = col + (SOIL_DARK - col) * (0.75 * crevice)[..., None]
    col = col + (SOIL_DARK - col) * (0.45 * crack)[..., None]
    rng = np.random.default_rng(seed + 5)
    grit = (rng.random((size, size)) > 0.985) * (0.5 + 0.5 * rng.random((size, size)))
    grit = np.maximum(grit, np.roll(grit, 1, 0) * 0.6)
    col = col + (SOIL_GRIT - col) * (0.6 * grit * (0.4 + 0.6 * small))[..., None]
    height = height + 0.0008 * grit
    return np.clip(col, 0, 1), height


def normal_from_height(height, px_per_m, strength=1.0):
    """Tangent-space normal map (OpenGL: +G up the texture) of a height field in metres, texel row 0 at the top."""
    dhx = np.gradient(height, axis=1) * px_per_m
    dhy = -np.gradient(height, axis=0) * px_per_m
    nt = np.stack([-dhx * strength, -dhy * strength, np.ones_like(height)], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
    return nt * 0.5 + 0.5


def lit(albedo, height, px_per_m, shade=0.35, light=(0.35, 0.55, 0.9)):
    """The painted light rock_textures.bake uses: albedo brightened where the height tilts toward the light."""
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    n = normal_from_height(height, px_per_m) * 2 - 1
    lt = n @ L - L[2]
    return np.clip(albedo * (1.0 + shade * lt[..., None]), 0, 1)
