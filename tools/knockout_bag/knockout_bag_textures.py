"""Procedural textures for the knockout bag (used by build_knockout_bag.py).

Everything is numpy on texel grids, seeded, so the build is deterministic. Lengths are metres.

* ``canvas_maps`` - the bag's mustard canvas: a plain weave of slightly uneven threads, stitched seams down the back
  and both sides, a stitched hem under the rim, darker folds where the drawstring gathers the cloth, dark holes behind
  the eyelets, a little grime low down, and the inside of the bag in shadow.
* ``band_maps`` - the burnt-orange leather base: suede-like nap, scuffed paler edges, a stitch row along its top and
  one along its foot, and a dirtier bottom.
* ``pocket_maps`` and ``flap_maps`` - the front pocket and its flap (side by side in one atlas): the same orange
  leather, stitched borders, stitched corner patches on the pocket's lower corners.
* ``strap_maps`` - the dark brown leather tabs on the sides: burnished edges and a stitched border.
* ``metal_maps`` - the dark gunmetal of the eyelets, rivets and the snap.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rocks"))
import rock_textures as rt  # noqa: E402
from rock_textures import smoothstep, srgb  # noqa: E402

# ---- Palettes ---------------------------------------------------------------------------------------
CANVAS_LIGHT, CANVAS, CANVAS_DARK = srgb("#d1a843"), srgb("#b98d2c"), srgb("#835f16")
CANVAS_THREAD = srgb("#e3cb7c")
INSIDE = srgb("#3b2c10")
HOLE = srgb("#140f07")
GRIME = srgb("#6b5226")
LEATHER_LIGHT, LEATHER, LEATHER_DARK = srgb("#d97f45"), srgb("#bf6230"), srgb("#83391a")
LEATHER_THREAD = srgb("#e9c39b")
STRAP_LIGHT, STRAP, STRAP_DARK = srgb("#8a5634"), srgb("#683b21"), srgb("#3f2112")
STRAP_THREAD = srgb("#c7a07a")
METAL_LIGHT, METAL, METAL_DARK = srgb("#8d8f90"), srgb("#66686a"), srgb("#3e3f41")
ROPE_LIGHT, ROPE_DARK = srgb("#d6c49c"), srgb("#8f7a55")


def _hash1(i, seed):
    """A repeatable pseudo-random value in [0, 1) per integer."""
    h = (np.asarray(i, np.int64) * 374761393 + seed * 668265263) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0x1000000)


def _noise2(noise, x, y, freq, octaves=3, z=0.0):
    pts = np.stack([x.ravel(), y.ravel(), np.full(x.size, z)], -1)
    return noise.fbm(pts, freq, octaves=octaves).reshape(x.shape)


def weave(x, y, seed, lam=0.003, amp=0.0003):
    """A plain weave on flat coordinates (metres): height, and a per-thread tone in [0, 1)."""
    ga, gb = x / lam, y / lam
    ja, jb = np.floor(ga).astype(np.int64), np.floor(gb).astype(np.int64)
    fa, fb = ga - ja, gb - jb
    over = ((ja + jb) % 2) == 0
    warp = np.sin(np.pi * fa) ** 0.7
    weft = np.sin(np.pi * fb) ** 0.7
    thread = np.where(over, warp, weft)
    tone = np.where(over, _hash1(ja, seed), _hash1(jb, seed + 1))
    return amp * thread, tone, thread


def stitches(s, d, width, dash=0.0045, gap=0.0025):
    """A row of stitches: s runs along the row, d across it (0 on the row). Returns coverage 0..1 and a bump."""
    period = dash + gap
    f = np.mod(s, period) / period
    along = smoothstep(0.0, 0.12, f) * (1.0 - smoothstep(dash / period - 0.12, dash / period, f))
    across = 1.0 - smoothstep(width * 0.35, width * 0.5, np.abs(d))
    m = along * across
    bump = m * np.sqrt(np.clip(1.0 - (d / (width * 0.5)) ** 2, 0, 1))
    return m, bump


def rounded_rect_sdf(x, y, a, b, r):
    """Distance inside (positive) a rounded rectangle of half sizes a, b and corner radius r."""
    qx, qy = np.abs(x) - (a - r), np.abs(y) - (b - r)
    outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
    return -outside


def rounded_rect_param(x, y, a, b, r):
    """A coordinate running along a rounded rectangle's outline (for stitch dashes): x along the top and bottom,
    y along the sides, arc length round the corners."""
    qx, qy = np.abs(x) - (a - r), np.abs(y) - (b - r)
    corner = (qx > 0) & (qy > 0)
    sides = (qx > 0) & ~corner
    phi = np.arctan2(np.maximum(qx, 0), np.maximum(qy, 0))
    s = np.where(sides, y, x)
    return np.where(corner, np.sign(x) * (a - r + phi * r), s)


def lit(albedo, height, px_per_m, shade=0.35, light=(0.35, 0.55, 0.9)):
    """Albedo brightened where the height field tilts toward the light (the painted light the rocks use).
    px_per_m is one density or (across, down) when the texels are not square in metres."""
    L = np.asarray(light, float)
    L /= np.linalg.norm(L)
    n = normal_from_height(height, px_per_m) * 2 - 1
    lt = n @ L - L[2]
    return np.clip(albedo * (1.0 + shade * lt[..., None]), 0, 1)


def normal_from_height(height, px_per_m, strength=1.0):
    """Tangent-space normal map (OpenGL: +G up the texture) of a height field in metres, texel row 0 at the top.
    px_per_m is one density or (across, down)."""
    px, py = (px_per_m, px_per_m) if np.isscalar(px_per_m) else px_per_m
    dhx = np.gradient(height, axis=1) * px
    dhy = -np.gradient(height, axis=0) * py
    nt = np.stack([-dhx * strength, -dhy * strength, np.ones_like(height)], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
    return nt * 0.5 + 0.5


# =====================================================================================================
# The canvas
# =====================================================================================================

def canvas_maps(w, h, shape, seed=11):
    """Albedo and height of the canvas lathe. Texel (column, row) is (theta = 2 pi u, profile length t from the top
    of the texture's v range down): `shape` supplies the bag's profile, folds, seams and eyelets (see
    build_knockout_bag.CanvasShape). Returns albedo, height, px_per_m."""
    jj, ii = np.mgrid[0:h, 0:w]
    u = (ii + 0.5) / w
    v = 1.0 - (jj + 0.5) / h                          # v up, as the UVs are
    theta = 2 * np.pi * u
    t = (v - 0.004) / 0.992 * shape.length          # profile length from the bottom edge (see CanvasShape.v_of)
    rho = shape.rho(t)                                # metres per radian round the bag at that height
    x = theta * shape.rho0                            # flat weave coordinates
    y = t
    noise, fine, stain = rt.Noise3(seed), rt.Noise3(seed + 1), rt.Noise3(seed + 2)

    hgt, tone, thread = weave(x, y, seed)
    fibre = _noise2(fine, x, y, 260.0, octaves=2)
    slub = smoothstep(0.55, 0.9, 0.5 + 0.5 * _noise2(fine, x, y, 35.0, octaves=2, z=3.0))
    col = CANVAS[None, None] * (0.95 + 0.07 * tone[..., None] + 0.05 * fibre[..., None])
    gap = 1.0 - smoothstep(0.05, 0.5, thread)
    col = col + (CANVAS_DARK - col) * (0.35 * gap)[..., None]
    col = col + (CANVAS_LIGHT - col) * ((0.22 * smoothstep(0.7, 1.0, thread) + 0.18 * slub))[..., None]
    hgt = hgt + 0.00015 * slub
    mott = _noise2(noise, x, y, 4.0, octaves=3)
    col = col * (1.0 + 0.07 * mott)[..., None]

    # the folds the drawstring gathers: darker in the valleys, a touch paler on the crests
    fold, amp = shape.fold(theta, t)
    col = col * (1.0 - 0.32 * amp * np.clip(-fold, 0, 1) ** 1.3 + 0.06 * amp * np.clip(fold, 0, 1))[..., None]

    # seams: down the back and both sides, from the base to the neck, each a groove with a stitch row beside it
    for th in shape.seams:
        dth = np.angle(np.exp(1j * (theta - th)))
        d = dth * rho
        on = (t > shape.t_band) & (t < shape.t_seam_top)
        groove = np.exp(-(d / 0.0009) ** 2) * on
        hgt = hgt - 0.0005 * groove
        col = col * (1.0 - 0.25 * groove)[..., None]
        m, bump = stitches(t, d - 0.0028, 0.0011)
        m, bump = m * on, bump * on
        col = col + (CANVAS_THREAD - col) * (0.85 * m)[..., None]
        hgt = hgt + 0.0004 * bump
    # the hem under the rim
    d = t - shape.t_hem
    m, bump = stitches(theta * rho, d, 0.0011)
    col = col + (CANVAS_THREAD - col) * (0.8 * m)[..., None]
    hgt = hgt + 0.0004 * bump

    # dark holes behind the eyelets
    for th in shape.eyelets:
        dth = np.angle(np.exp(1j * (theta - th)))
        r = np.hypot(dth * rho, t - shape.t_eyelet)
        col = col + (HOLE - col) * (1.0 - smoothstep(0.0048, 0.0058, r))[..., None]

    # grime low down, rubbed into the folds
    st = 0.5 + 0.5 * _noise2(stain, x, y, 7.0, octaves=3)
    low = smoothstep(shape.t_band + 0.09, shape.t_band, t) * (0.25 + 0.5 * st)
    col = col + (GRIME - col) * (0.35 * low)[..., None]

    # the inside: in shadow, darker the deeper it goes
    inside = smoothstep(shape.t_rim - 0.004, shape.t_rim + 0.012, t)
    deep = smoothstep(shape.t_rim, shape.length, t)
    shade = INSIDE[None, None] * (1.0 - 0.55 * deep)[..., None]
    col = col + (shade * (0.9 + 0.2 * tone[..., None]) - col) * (inside * 0.92)[..., None]
    return np.clip(col, 0, 1), hgt, (w / (2 * np.pi * shape.rho0), h / shape.length)


# =====================================================================================================
# The leather base
# =====================================================================================================

def leather(x, y, seed, base=LEATHER, light=LEATHER_LIGHT, dark=LEATHER_DARK):
    """Suede-like orange leather on flat coordinates: colour and height."""
    noise, fine = rt.Noise3(seed), rt.Noise3(seed + 1)
    nap = _noise2(fine, x, y, 420.0, octaves=2)
    grain = _noise2(fine, x, y, 120.0, octaves=3, z=2.0)
    mott = _noise2(noise, x, y, 9.0, octaves=3)
    col = base[None, None] * (1.0 + 0.05 * nap[..., None] + 0.07 * mott[..., None])
    col = col + (dark - col) * (0.25 * smoothstep(0.2, 0.8, -grain))[..., None]
    col = col + (light - col) * (0.18 * smoothstep(0.3, 0.9, mott))[..., None]
    height = 0.00012 * nap + 0.00025 * grain
    return col, height


def band_maps(w, h, band, seed=21):
    """Albedo and height of the base band lathe (`band` is build_knockout_bag.BandShape). Returns albedo, height,
    px_per_m."""
    jj, ii = np.mgrid[0:h, 0:w]
    u = (ii + 0.5) / w
    v = 1.0 - (jj + 0.5) / h
    theta = 2 * np.pi * u
    t = v * band.length
    x, y = theta * band.rho0, t
    col, hgt = leather(x, y, seed)
    s = theta * band.rho0
    for row in band.stitch_rows:
        m, bump = stitches(s, t - row, 0.0012, dash=0.005, gap=0.003)
        col = col + (LEATHER_THREAD - col) * (0.9 * m)[..., None]
        hgt = hgt + 0.0005 * bump
        # the thread pulls the leather in a little either side
        hgt = hgt - 0.00025 * np.exp(-((t - row) / 0.0016) ** 2)
    # scuffed, paler round the rounded foot and the top lip; dirty underneath
    scuff = 0.5 + 0.5 * _noise2(rt.Noise3(seed + 5), x, y, 30.0, octaves=3)
    edge = np.exp(-((t - band.t_foot) / 0.006) ** 2) + 0.6 * np.exp(-((t - band.t_lip) / 0.004) ** 2)
    col = col + (LEATHER_LIGHT * 1.05 - col) * np.clip(0.55 * edge * smoothstep(0.35, 0.8, scuff), 0, 0.6)[..., None]
    under = smoothstep(band.t_foot - 0.004, band.t_foot - 0.03, t)
    col = col + (GRIME * 0.6 - col) * (0.55 * under)[..., None]
    return np.clip(col, 0, 1), hgt, (w / (2 * np.pi * band.rho0), h / band.length)


# =====================================================================================================
# The pocket, its flap and the side tabs (flat patches: texel = the patch's own x, y in metres)
# =====================================================================================================

def patch_grid(w, h, a, b, margin=0.004):
    """Texel centres of a patch's planar map: x across [-a - margin, a + margin], y likewise, row 0 at the top."""
    jj, ii = np.mgrid[0:h, 0:w]
    x = -a - margin + (ii + 0.5) / w * 2 * (a + margin)
    y = b + margin - (jj + 0.5) / h * 2 * (b + margin)
    return x, y


def pocket_maps(w, h, pocket, flap_bottom, seed=31):
    """The front pocket: orange leather, a stitched border (none along the top, under the flap), stitched corner
    patches over the lower corners, paler rubbed edges. `pocket` is (a, b, r): half sizes and corner radius in
    metres; the flap's lower edge (pocket y, metres) casts a soft shadow below it. Returns albedo, height, (px across,
    px down) per metre."""
    pa, pb, pr = pocket
    x, y = patch_grid(w, h, pa, pb)
    col, hgt = leather(x, y, seed)
    d = rounded_rect_sdf(x, y, pa, pb, pr)
    s = rounded_rect_param(x, y, pa, pb, pr)
    m, bump = stitches(s, d - 0.0065, 0.0012)
    m, bump = m * (y < pb - 0.004), bump * (y < pb - 0.004)
    for sx in (-1, 1):
        cx, cy = sx * pa, -pb
        rr = np.hypot(x - cx, y - cy)
        inside = (rr < 0.042) & (d > 0)
        col = col * (1.0 - 0.08 * inside)[..., None]
        cm, cb = stitches(np.arctan2(y - cy, x - cx) * 0.042, rr - 0.042 + 0.003, 0.0011)
        cm, cb = cm * (d > 0.004), cb * (d > 0.004)
        hgt = hgt + 0.0003 * smoothstep(0.044, 0.04, rr) * (d > 0)
        m, bump = np.maximum(m, cm), np.maximum(bump, cb)
    col = col + (LEATHER_THREAD - col) * (0.9 * m)[..., None]
    hgt = hgt + 0.0005 * bump
    col = col + (LEATHER_LIGHT - col) * (0.35 * smoothstep(0.004, 0.0, d))[..., None]
    shadow = smoothstep(flap_bottom - 0.008, flap_bottom + 0.002, y)
    col = col * (1.0 - 0.22 * shadow)[..., None]
    return np.clip(col, 0, 1), hgt, patch_density(w, h, pa, pb)


def flap_maps(w, h, flap, seed=38):
    """The pocket's flap: the same leather, stitched round its sides and bottom (not along the fold at the top)."""
    fa, fb, fr = flap
    x, y = patch_grid(w, h, fa, fb)
    col, hgt = leather(x + 0.3, y, seed)
    d = rounded_rect_sdf(x, y, fa, fb, fr)
    s = rounded_rect_param(x, y, fa, fb, fr)
    m, bump = stitches(s, d - 0.0055, 0.0012)
    keep = y < fb - 0.006
    col = col + (LEATHER_THREAD - col) * (0.9 * m * keep)[..., None]
    hgt = hgt + 0.0005 * bump * keep
    col = col + (LEATHER_LIGHT - col) * (0.4 * smoothstep(0.004, 0.0, d))[..., None]
    return np.clip(col, 0, 1), hgt, patch_density(w, h, fa, fb)


def patch_density(w, h, a, b, margin=0.004):
    """Texels per metre across and down a patch's planar map (see patch_grid)."""
    return w / (2 * (a + margin)), h / (2 * (b + margin))


def strap_maps(w, h, strap, seed=41):
    """A side tab: dark brown leather, burnished edges, a stitched border. `strap` is (a, b, r)."""
    a, b, r = strap
    x, y = patch_grid(w, h, a, b)
    col, hgt = leather(x, y, seed, STRAP, STRAP_LIGHT, STRAP_DARK)
    d = rounded_rect_sdf(x, y, a, b, r)
    s = rounded_rect_param(x, y, a, b, r)
    m, bump = stitches(s, d - 0.0035, 0.0009, dash=0.0035, gap=0.002)
    col = col + (STRAP_THREAD - col) * (0.8 * m)[..., None]
    hgt = hgt + 0.0004 * bump
    col = col + (STRAP_DARK * 0.8 - col) * (0.5 * smoothstep(0.003, 0.0, d))[..., None]
    sheen = smoothstep(0.3, 0.9, 0.5 + 0.5 * _noise2(rt.Noise3(seed + 3), x, y, 25.0))
    col = col + (STRAP_LIGHT - col) * (0.25 * sheen)[..., None]
    return np.clip(col, 0, 1), hgt, patch_density(w, h, a, b)


def metal_maps(size=64, seed=51):
    """Dark gunmetal with faint mottling (the eyelets, rivets and snap are small: the light does the rest)."""
    jj, ii = np.mgrid[0:size, 0:size]
    x, y = ii / size * 0.05, jj / size * 0.05
    m = _noise2(rt.Noise3(seed), x, y, 90.0, octaves=3)
    col = METAL[None, None] * (1.0 + 0.12 * m[..., None])
    return np.clip(col, 0, 1), 0.0001 * m, size / 0.05
