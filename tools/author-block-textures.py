#!/usr/bin/env python3
"""Cuts the vanilla block textures (mods/vanilla/textures/blocks/*.png) from the artist's source pictures.

Usage: tools/author-block-textures.py SOURCE_DIR [OUT_DIR]   (default OUT_DIR: mods/vanilla/textures/blocks)
SOURCE_DIR holds one <material>.jpg per block material (aluminum.jpg, wood.jpg, ...). Needs Python 3 with numpy
and Pillow. Deterministic: the same sources give the same pixels.

One texture covers one 0.25 m block face and repeats on the next block, so each picture is cut to a whole number
of its pattern repeats (measured by autocorrelation), resized to 512 px and blended across its edges so it tiles.
See-through materials get alpha (glass, armored glass); the net is drawn as a cut-out lattice because its source
is not regular enough to tile; the path light gets a glow map of its lit strips.
"""
import os
import sys

import numpy as np
from PIL import Image

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
    "scrap_metal": {"px": 292, "py": 259, "nx": 2, "ny": 2},
    "scrap_wood": {"px": 293, "py": 336, "nx": 1, "ny": 1},
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
    save(os.path.join(out, "net.png"), *net())
    print("net")


if __name__ == "__main__":
    main()
