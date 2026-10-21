#!/usr/bin/env python3
"""Draws the block textures that have no source picture (mods/vanilla/textures/blocks/*.png), from code.

Usage: tools/draw-block-textures.py [OUT_DIR] [NAME ...]   (default OUT_DIR: mods/vanilla/textures/blocks; no
NAME draws them all). Needs Python 3 with numpy and Pillow. Deterministic: the same code gives the same pixels.

Every texture wraps at its edges. The patterned ones span several blocks (the part's visual ``texture_span``, cells
per repeat) so a wall reads as one larger pattern with no border round each block (see the block-textures notes in
docs/part-data-schema.md). Each texture is shaded from a height map lit from the top left, like scrap stone in
tools/author-block-textures.py, whose noise helpers this script shares.

Wood, Metal and Concrete Block 2 and 3 are the reinforced tiers of the 1s: the same material with steel added, more
of it at tier 3. Concrete Block 3 also gets a glow map for its teal inlays.
"""
import importlib.util
import os
import sys

import numpy as np

_spec = importlib.util.spec_from_file_location("abt", os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                                    "author-block-textures.py"))
abt = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(abt)
value_noise, stretched_noise, smoothstep, save = abt.value_noise, abt.stretched_noise, abt.smoothstep, abt.save

STEEL = np.array([168, 172, 176])
STEEL_DARK = np.array([92, 98, 106])
STEEL_BLUE = np.array([78, 98, 122])
TEAL = np.array([22, 176, 158])


def lit(height, strength=0.28, lo=0.45, hi=1.5):
    """Brightness from a height map lit from the top left (a slope facing the light is brighter). Wraps."""
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) / 2
    return np.clip(1 - strength * (gx + gy), lo, hi)


def blur_wrap(a, radius):
    """Gaussian blur that wraps at the edges."""
    from PIL import Image, ImageFilter
    lo, hi = a.min(), a.max()
    scale = 255 / max(hi - lo, 1e-6)
    size = a.shape[0]
    img = Image.fromarray(((np.tile(a, (3, 3)) - lo) * scale).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, np.float64)[size:2 * size, size:2 * size] / scale + lo


def finish(color, height, strength=0.3, seed=0):
    """Light the colour from the height map, darken crevices (ambient occlusion) and add uneven grime and wear, so
    the flat drawing reads as worn material."""
    size = height.shape[0]
    ao = np.clip(1 - 0.035 * (blur_wrap(height, size / 128) - height), 0.55, 1.0)
    grime = 0.86 + 0.18 * value_noise(size, 5, 900 + seed, 4)
    return color * (lit(height, strength) * ao * grime)[..., None]


def grid(size):
    y, x = np.mgrid[0:size, 0:size] + 0.5
    return y, x


def box_dist(x, y, x0, y0, w, h):
    """Distance inside a rectangle to its nearest edge (negative outside), for many pixels at once."""
    return np.minimum(np.minimum(x - x0, x0 + w - x), np.minimum(y - y0, y0 + h - y))


def bevel(d, width):
    """0 at the edge rising to 1 at width inside: a chamfered plate's height."""
    return np.clip(d / width, 0, 1)


def bolts(height, color, size, centers, r, metal):
    """Round bolt heads (domes) at the given centres, wrapping at the edges."""
    y, x = grid(size)
    for cx, cy in centers:
        dx = (x - cx + size / 2) % size - size / 2
        dy = (y - cy + size / 2) % size - size / 2
        d = np.sqrt(dx * dx + dy * dy)
        m = d < r
        height[m] = np.maximum(height[m], 6 + 5 * np.sqrt(np.clip(1 - (d[m] / r) ** 2, 0, 1)))
        color[m] = metal
    return height, color


# =====================================================================================================
# Sand
# =====================================================================================================
def sand(size=1024):
    """Orange-gold sand over 4 x 4 blocks (texture_span 4): wind ripples bent by noise, soft hollows and a fine grain."""
    y, x = grid(size)
    warp = 60 * (value_noise(size, 4, 101, 3) - 0.5)
    ripples = np.sin((y + warp + 0.25 * x) * 2 * np.pi / (size / 11))
    height = 2.2 * ripples * (0.5 + value_noise(size, 6, 102, 2))
    height += 14 * value_noise(size, 5, 103, 4)
    grain = np.random.default_rng(104).random((size, size))
    sand_c = np.array([214, 156, 80]) * (0.9 + 0.16 * value_noise(size, 10, 105, 4))[..., None]
    rgb = sand_c * lit(height, 0.22)[..., None] * (0.94 + 0.1 * grain)[..., None]
    # a few darker grains and pebbles
    pebble = smoothstep(value_noise(size, 192, 106, 1), 0.9, 0.93)
    return rgb * (1 - 0.15 * pebble)[..., None]


# =====================================================================================================
# Glass Tile
# =====================================================================================================
def glass_tile(size=512):
    """Glass bricks, 2 x 2 to a block face (texture_span 1, the grid is even so no block border shows): smoky
    blue-grey panes with rounded bevels in thin dark grout. Alpha: grout solid, panes half see-through."""
    y, x = grid(size)
    p = size / 2
    u, v = x % p, y % p
    d = np.minimum(np.minimum(u, p - u), np.minimum(v, p - v))
    grout = d < 7
    rim = bevel(d - 7, 22)
    height = 10 * np.sqrt(rim) - 6 * grout
    frost = value_noise(size, 24, 111, 3)
    pane = np.array([62, 78, 98]) * (0.92 + 0.12 * frost)[..., None]
    rgb = np.where(grout[..., None], STEEL_DARK * 0.45, pane) * lit(height, 0.4)[..., None]
    # a highlight band along each pane's top-left bevel
    shine = smoothstep(rim, 0.2, 0.5) * (1 - smoothstep(rim, 0.6, 1.0)) * ((u + v) < p)
    rgb = rgb + 36 * shine[..., None]
    alpha = np.where(grout, 1.0, 0.62 + 0.25 * (1 - rim) + 0.08 * frost)
    return rgb, alpha


# =====================================================================================================
# Plastics
# =====================================================================================================
def plastic(size=512):
    """Moulded blue plastic: plain, faintly mottled, with soft sink marks. One block (no pattern to repeat)."""
    height = 3 * value_noise(size, 6, 121, 4)
    base = np.array([44, 146, 210]) * (0.95 + 0.08 * value_noise(size, 12, 122, 3))[..., None]
    return finish(base, height, 0.3)


def bubble_plastic(size=512):
    """Bubble plastic: rows of round air cushions, staggered, 4 to a block, in navy film (texture_span 1)."""
    y, x = grid(size)
    n = 4
    p = size / n
    row = np.floor(y / p)
    xs = x + (row % 2) * p / 2
    u, v = xs % p - p / 2, y % p - p / 2
    d = np.sqrt(u * u + v * v)
    r = p * 0.4
    dome = np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))
    height = 16 * dome + 1.5 * value_noise(size, 16, 131, 2)
    film = np.array([56, 74, 112]) * (0.96 + 0.06 * value_noise(size, 8, 132, 3))[..., None]
    rgb = film * lit(height, 0.12)[..., None]
    # a bright glint high on each bubble
    glint = smoothstep(1 - np.hypot(u + r * 0.35, v + r * 0.35) / (r * 0.3), 0, 0.6) * (d < r)
    return rgb + 70 * glint[..., None]


# =====================================================================================================
# Metal Block 2 and 3
# =====================================================================================================
def metal_2(size=1024):
    """Metal Block 2 over 4 x 4 blocks (texture_span 4): bevelled steel plates two blocks square, rows staggered by a
    block, with a recessed steel-blue channel along each plate's middle and paired bolts by the channel ends."""
    y, x = grid(size)
    B = size / 4                       # one block
    P = 2 * B                          # one plate
    row = np.floor(y / P)
    xs = (x + (row % 2) * B) % size
    u, v = xs % P, y % P
    d = np.minimum(np.minimum(u, P - u), np.minimum(v, P - v))
    height = 12 * bevel(d - 3, 14)
    # the channel: a recessed strip along the plate's middle, horizontal on even rows, vertical on odd ones
    across = np.where(row % 2 == 0, np.abs(v - P / 2), np.abs(u - P / 2))
    along = np.where(row % 2 == 0, u, v)
    channel = (across < B * 0.16) & (along > B * 0.3) & (along < P - B * 0.3)
    height = np.where(channel, 4 - 3 * bevel(B * 0.16 - across, 6), height)
    color = np.broadcast_to(STEEL, (size, size, 3)) * (0.94 + 0.1 * value_noise(size, 16, 141, 4))[..., None]
    color = np.where(channel[..., None], STEEL_BLUE, color)
    color = np.ascontiguousarray(color)
    # brushed grain
    height += 0.6 * stretched_noise(size, 4, 256, 142, 2)
    centers = []
    for ry in range(2):
        for cx in range(2):
            ox = cx * P - (ry % 2) * B
            oy = ry * P
            for a in (0.18, 0.82):
                for b in (-0.3, 0.3):
                    if ry % 2 == 0:
                        centers.append(((ox + a * P) % size, oy + P / 2 + b * B))
                    else:
                        centers.append(((ox + P / 2 + b * B) % size, oy + a * P))
    height, color = bolts(height, color, size, centers, 9, STEEL * 1.05)
    return finish(color, height, 0.3)


def metal_3(size=1024):
    """Metal Block 3 over 4 x 4 blocks (texture_span 4): heavy armour. Steel-blue ribs on a two-block grid with a
    raised pyramid stud where they cross, and thick bevelled light-steel plates between them, each with a shallow
    pressed diamond."""
    y, x = grid(size)
    B = size / 4
    P = 2 * B
    u, v = x % P, y % P
    du = np.minimum(u, P - u)
    dv = np.minimum(v, P - v)
    rib_w = B * 0.2
    rib = (du < rib_w) | (dv < rib_w)
    inner = np.minimum(du, dv) - rib_w
    height = 14 + 10 * bevel(inner, 16)
    # pressed diamond in each plate
    cu, cv = np.abs(u - P / 2), np.abs(v - P / 2)
    diamond = cu + cv
    height += 5 * bevel(B * 0.5 - diamond, 12)
    # ribs: rounded tops
    rd = np.where(du < dv, du, dv)
    height = np.where(rib, 18 + 6 * np.sqrt(np.clip(1 - (rd / rib_w) ** 2, 0, 1)), height)
    # pyramid studs at the crossings
    su, sv = np.minimum(u, P - u), np.minimum(v, P - v)
    stud = np.maximum(su, sv)
    sr = B * 0.36
    height = np.where(stud < sr, np.maximum(height, 30 - 22 * stud / sr), height)
    noise = (0.94 + 0.1 * value_noise(size, 16, 151, 4))[..., None]
    color = np.where(rib[..., None], STEEL_BLUE * 1.1, STEEL * 0.92) * noise
    color = np.where((stud < sr)[..., None], STEEL * 1.08, color)
    color = np.ascontiguousarray(color)
    centers = [(P * i + P / 2 + s * B * 0.62, P * j + P / 2 + t * B * 0.62)
               for i in range(2) for j in range(2) for s in (-1, 1) for t in (-1, 1)]
    height, color = bolts(height, color, size, centers, 8, STEEL_DARK * 1.3)
    return finish(color, height, 0.32)


# =====================================================================================================
# Wood Block 2 and 3
# =====================================================================================================
def planks(size, rows, seed, base):
    """Staggered planks: (colour, height). Each plank its own shade, grain along it, dark joints."""
    r = np.random.default_rng(seed)
    y, x = grid(size)
    ph = size / rows
    row = np.floor(y / ph).astype(int)
    v = y - row * ph
    length = size / 2
    offs = r.uniform(0, size, rows)
    xs = (x + offs[row]) % size
    k = np.floor(xs / length).astype(int)
    u = xs - k * length
    tone = r.uniform(0.86, 1.1, (rows, 2))[row, k]
    grain = stretched_noise(size, 6, 96, seed + 1, 4)
    color = base * (tone * (0.86 + 0.24 * grain))[..., None]
    d = np.minimum(np.minimum(v, ph - v), np.minimum(u, length - u))
    height = 6 * bevel(d, 5) + 1.5 * grain
    return color, height


def strap(color, height, y0, h, size, metal, bolt_every, bolt_r=8, holes=False):
    """A steel strap across the whole width from y0 to y0 + h, with bolts (or punched holes) along it."""
    y, x = grid(size)
    m = (y >= y0) & (y < y0 + h)
    d = np.minimum(y - y0, y0 + h - y)
    height[m] = 10 + 4 * bevel(d[m], 4)
    color[m] = metal * (0.95 + 0.08 * value_noise(size, 32, int(y0) + 7, 2))[m][..., None]
    for cx in np.arange(bolt_every / 2, size, bolt_every):
        dx = (x - cx + size / 2) % size - size / 2
        dd = np.hypot(dx, y - (y0 + h / 2))
        b = dd < bolt_r
        if holes:
            height[b] = 3
            color[b] = np.array([18, 20, 24])
        else:
            height[b] = 14 + 4 * np.sqrt(np.clip(1 - (dd[b] / bolt_r) ** 2, 0, 1))
            color[b] = STEEL * 1.05
    return color, height


def wood_2(size=1024):
    """Wood Block 2 over 4 x 4 blocks (texture_span 4): honey planks, four to a block, bound every two blocks by a
    bolted light-steel strap with a steel-blue band under it."""
    color, height = planks(size, 16, 161, np.array([176, 118, 62]))
    B = size / 4
    for k in range(2):
        y0 = k * 2 * B + B * 0.5
        color, height = strap(color, height, y0, B * 0.22, size, STEEL, B / 2)
        color, height = strap(color, height, y0 + B * 0.22, B * 0.12, size, STEEL_BLUE, 1e9)
    return finish(color, height, 0.3)


def wood_3(size=1024):
    """Wood Block 3 over 4 x 4 blocks (texture_span 4): darker planks, a wide steel-blue band with punched holes every
    two blocks, and a bolted light-steel strap above each band."""
    color, height = planks(size, 16, 171, np.array([150, 92, 48]))
    B = size / 4
    for k in range(2):
        y0 = k * 2 * B + B * 1.25
        color, height = strap(color, height, y0, B * 0.16, size, STEEL, B / 2)
        color, height = strap(color, height, y0 + B * 0.16, B * 0.4, size, STEEL_BLUE, B / 3, bolt_r=10, holes=True)
    return finish(color, height, 0.3)


# =====================================================================================================
# Concrete Block 2 and 3
# =====================================================================================================
def concrete_base(size, seed):
    height = 4 * value_noise(size, 8, seed, 4)
    pits = smoothstep(value_noise(size, 256, seed + 1, 1), 0.88, 0.92)
    height -= 1.2 * pits
    color = np.array([170, 168, 160]) * (0.9 + 0.14 * value_noise(size, 14, seed + 2, 4))[..., None]
    return np.ascontiguousarray(color), height


def concrete_2(size=1024):
    """Concrete Block 2 over 4 x 4 blocks (texture_span 4): cast concrete set in a light steel frame on a two-block
    grid, with bolted steel corner plates where the frame meets."""
    y, x = grid(size)
    color, height = concrete_base(size, 181)
    B = size / 4
    P = 2 * B
    u, v = x % P, y % P
    du, dv = np.minimum(u, P - u), np.minimum(v, P - v)
    w = B * 0.09
    frame = (du < w) | (dv < w)
    height = np.where(frame, 8 + 3 * bevel(w - np.minimum(du, dv), 3), height)
    color = np.where(frame[..., None], STEEL * 0.95, color)
    plate = (du < B * 0.32) & (dv < B * 0.32)
    height = np.where(plate, 11 + 2 * bevel(B * 0.32 - np.maximum(du, dv), 4), height)
    color = np.ascontiguousarray(np.where(plate[..., None], STEEL_DARK * 1.25, color))
    centers = [(i * P + s * B * 0.2, j * P + t * B * 0.2) for i in range(2) for j in range(2)
               for s in (-1, 1) for t in (-1, 1)]
    height, color = bolts(height, color, size, centers, 7, STEEL * 1.05)
    return finish(color, height, 0.3)


def concrete_3(size=1024):
    """Concrete Block 3 over 4 x 4 blocks (texture_span 4): concrete panels in a heavy steel-blue frame on a two-block
    grid, each panel crossed by a light steel brace, with teal glowing slots along the frame. Returns (colour, glow)."""
    y, x = grid(size)
    color, height = concrete_base(size, 191)
    B = size / 4
    P = 2 * B
    u, v = x % P, y % P
    du, dv = np.minimum(u, P - u), np.minimum(v, P - v)
    w = B * 0.15
    frame = (du < w) | (dv < w)
    height = np.where(frame, 10 + 4 * bevel(w - np.minimum(du, dv), 4), height)
    color = np.where(frame[..., None], STEEL_BLUE * 1.1, color)
    # a brace across each panel, top left to bottom right
    brace = (np.abs(u - v) < B * 0.07) & ~frame
    height = np.where(brace, 8, height)
    color = np.where(brace[..., None], STEEL, color)
    # glowing slots in the frame: short rounded bars centred on each frame segment
    seg_h = (dv < w * 0.35) & (np.abs(u - P / 2) < B * 0.3)
    seg_v = (du < w * 0.35) & (np.abs(v - P / 2) < B * 0.3)
    slot = seg_h | seg_v
    height = np.where(slot, 7, height)
    color = np.ascontiguousarray(np.where(slot[..., None], TEAL, color))
    centers = [(i * P + s * B * 0.42, j * P + t * B * 0.42) for i in range(2) for j in range(2)
               for s in (-1, 1) for t in (-1, 1)]
    height, color = bolts(height, color, size, centers, 7, STEEL * 1.05)
    rgb = finish(color, height, 0.3)
    glow = np.dstack([slot * 255.0] * 3)
    return rgb, glow


# name: (drawing function, what it returns)
TEXTURES = {
    "sand": sand,
    "glass_tile": glass_tile,
    "plastic": plastic,
    "bubble_plastic": bubble_plastic,
    "metal_2": metal_2,
    "metal_3": metal_3,
    "wood_2": wood_2,
    "wood_3": wood_3,
    "concrete_2": concrete_2,
    "concrete_3": concrete_3,
}


def main():
    args = sys.argv[1:]
    out = args[0] if args else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla",
                                             "textures", "blocks")
    names = args[1:] or list(TEXTURES)
    os.makedirs(out, exist_ok=True)
    for name in names:
        result = TEXTURES[name]()
        if name == "glass_tile":
            save(os.path.join(out, f"{name}.png"), *result)
        elif name == "concrete_3":
            save(os.path.join(out, f"{name}.png"), result[0])
            save(os.path.join(out, f"{name}_glow.png"), result[1])
        else:
            save(os.path.join(out, f"{name}.png"), result)
        print(name)


if __name__ == "__main__":
    main()
