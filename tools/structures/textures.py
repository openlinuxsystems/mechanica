"""Tileable detail textures for structures (used by kit.py when exporting).

A structure's materials are a flat colour times one of a few shared grey detail images (a "finish"): worn paint,
worn metal, cracked stone, plank wood and concrete. glTF multiplies the colour (baseColorFactor) by the image, so one
image serves every colour of that finish and a model stays small. Each finish also has a tangent-space normal map.
Meshes get box-projected UVs in metres (TILE[finish] metres per image), so detail keeps one scale everywhere.

Made in numpy from seeded noise with the Lift's helpers (tools/lift/lift_textures.py), so the output is the same on
every machine.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lift"))
import lift_textures as lt  # noqa: E402

SIZE = lt.SIZE
FINISHES = ("paint", "metal", "stone", "wood", "concrete", "worn_concrete")
# metres of surface one image covers
TILE = {"paint": 1.5, "metal": 1.5, "stone": 2.5, "wood": 1.2, "concrete": 3.0, "worn_concrete": 4.0}


def voronoi_edges(seed, cells):
    """Tileable distance to the nearest cell border, in pixels: cells x cells jittered points, wrapped."""
    rng = np.random.default_rng(seed)
    step = SIZE / cells
    pts = np.array([((i + rng.uniform(0.15, 0.85)) * step, (j + rng.uniform(0.15, 0.85)) * step)
                    for i in range(cells) for j in range(cells)])
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    d1 = np.full((SIZE, SIZE), 1e9, np.float32)
    d2 = np.full((SIZE, SIZE), 1e9, np.float32)
    for px, py in pts:
        dx = np.abs(xx - px)
        dy = np.abs(yy - py)
        dx = np.minimum(dx, SIZE - dx)
        dy = np.minimum(dy, SIZE - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        d1 = np.where(closer, d, d1)
    return (d2 - d1) * 0.5


def paint():
    mottle = lt.fbm(101, 3.0)
    dirt = np.clip(lt.fbm(102, 2.0) * 1.6 - 0.15, 0, 1)
    sc = lt.scratches(103, 220, bias=0.2)
    chips = np.clip((lt.fbm(104, 7.0) + 0.3 * lt.fnoise(105, 70.0) - 0.5) * 10, 0, 1)
    v = 0.86 + 0.05 * mottle - 0.16 * dirt + 0.12 * sc + 0.1 * chips
    height = 0.5 * (1 - chips) - 0.6 * sc + 0.1 * mottle
    return v, height


def metal():
    mottle = lt.fbm(201, 3.0)
    grain = lt.fnoise(202, 140.0, aniso=(1.0, 0.12))
    grime = np.clip(lt.fbm(203, 2.0) * 1.4 - 0.2, 0, 1)
    sc = lt.scratches(204, 320, bias=0.0)
    v = 0.84 + 0.05 * mottle + 0.04 * grain - 0.14 * grime + 0.14 * sc
    height = 0.3 * grain - 0.7 * sc + 0.1 * mottle
    return v, height


def stone():
    mottle = lt.fbm(301, 3.0)
    edges = voronoi_edges(302, 5)
    crack = np.clip(1 - edges / 3.5, 0, 1)
    fine = np.clip(1 - voronoi_edges(303, 11) / 1.6, 0, 1) * 0.5
    pits = (lt.fnoise(304, 160.0) > 0.6).astype(float)
    v = 0.9 + 0.07 * mottle - 0.55 * crack - 0.2 * fine - 0.12 * pits
    height = -crack - 0.4 * fine + 0.2 * mottle - 0.2 * pits
    return v, height


def wood():
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    grain = lt.fnoise(401, 90.0, aniso=(0.06, 1.0))
    knots = np.clip(lt.fnoise(402, 12.0) * 2 - 1.2, 0, 1)
    planks = SIZE // 4
    seam = ((yy % planks) < 3).astype(float)
    shade = np.repeat(np.random.default_rng(403).uniform(-0.06, 0.06, 4), planks)[:, None]
    v = 0.85 + 0.09 * grain - 0.25 * knots - 0.45 * seam + shade
    height = 0.4 * grain - seam - 0.3 * knots
    return v, height


def concrete():
    mottle = lt.fbm(501, 2.5)
    speck = lt.fnoise(502, 200.0)
    stain = np.clip(lt.fbm(503, 1.5) * 1.5 - 0.3, 0, 1)
    v = 0.88 + 0.06 * mottle + 0.04 * speck - 0.14 * stain
    height = 0.3 * speck + 0.2 * mottle
    return v, height


def worn_concrete():
    """Concrete that has seen use: stains, scuffs, pitting and fine hairline cracks (workshop floors)."""
    mottle = lt.fbm(511, 2.5)
    speck = lt.fnoise(512, 220.0)
    stain = np.clip(lt.fbm(513, 1.2) * 1.8 - 0.25, 0, 1)
    scuff = np.clip(lt.fnoise(514, 18.0, aniso=(1.0, 0.25)) * 1.6 - 0.5, 0, 1)
    crack = np.clip(1 - voronoi_edges(515, 4) / 1.2, 0, 1) * (lt.fbm(516, 3.0) > 0.05)
    pits = (lt.fnoise(517, 180.0) > 0.7).astype(float)
    v = 0.9 + 0.07 * mottle + 0.05 * speck - 0.22 * stain - 0.1 * scuff - 0.4 * crack - 0.1 * pits
    height = 0.3 * speck + 0.2 * mottle - 0.8 * crack - 0.25 * pits
    return v, height


def images(finish):
    """(detail sRGB HxWx3, normal HxWx3) of a finish."""
    v, height = {"paint": paint, "metal": metal, "stone": stone, "wood": wood, "concrete": concrete, "worn_concrete": worn_concrete}[finish]()
    # v is the linear factor the colour is multiplied by; the image is stored as sRGB, at half the noise's size
    v = half(np.clip(v, 0.05, 1.0)) ** (1 / 2.2)
    normal = lt.normal_from_height(height, 2.5)
    normal = np.stack([half(normal[..., k]) for k in range(3)], -1)
    return np.repeat(v[..., None], 3, -1), normal


def half(x):
    """Halves a tileable image by averaging 2 x 2 pixels."""
    return (x[0::2, 0::2] + x[1::2, 0::2] + x[0::2, 1::2] + x[1::2, 1::2]) / 4
