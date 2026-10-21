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


# =====================================================================================================
# Batch 2: sheet metal, nets and meshes, worn surfaces, ice
# =====================================================================================================
BEIGE = np.array([196, 186, 160])
ORANGE_STEEL = np.array([226, 132, 40])


def extruded_metal(size=1024):
    """Extruded Metal over 2 x 2 blocks (texture_span 2): horizontal extruded profiles, ridges of different widths
    in light and dark steel with a polished strip, running unbroken along a wall. Profile seams sit off the block
    edges."""
    y, x = grid(size)
    B = size / 2
    # one profile per block height, offset by a third of a block, each with a ridge, a groove and a bright strip
    v = (y + B / 3) % B / B
    height = np.select([v < 0.08, v < 0.38, v < 0.46, v < 0.58, v < 0.9], [2, 10 + 3 * np.sin(v * 30), 4, 14, 9], 2)
    height = blur_wrap(height.astype(np.float64), 2.5)
    tone = np.select([v < 0.08, v < 0.38, v < 0.46, v < 0.58, v < 0.9], [0.55, 0.95, 0.7, 1.22, 0.86], 0.55)
    color = STEEL * (tone * (0.95 + 0.08 * value_noise(size, 12, 201, 3)))[..., None]
    color = np.where(((v > 0.58) & (v < 0.9))[..., None], STEEL_BLUE * 1.25 * (0.95 + 0.08 * value_noise(size, 12, 202, 3))[..., None], color)
    height = height + 0.5 * stretched_noise(size, 256, 4, 203, 2)
    return finish(np.ascontiguousarray(color), height, 0.32)


def punched_steel(size=512):
    """Punched Steel, one block (texture_span 1): a pale steel sheet punched with a staggered grid of round holes,
    four rows to a block, the holes see-through. Returns (colour, alpha)."""
    y, x = grid(size)
    n = 4
    p = size / n
    row = np.floor(y / p)
    xs = x + (row % 2) * p / 2
    u, v = xs % p - p / 2, y % p - p / 2
    d = np.hypot(u, v)
    r = p * 0.3
    hole = d < r
    height = 8 - 6 * np.clip(1 - (d - r) / 8, 0, 1)            # pressed lip round each hole
    height += 0.6 * value_noise(size, 16, 211, 2)
    color = BEIGE * (0.94 + 0.1 * value_noise(size, 10, 212, 3))[..., None]
    rgb = finish(np.ascontiguousarray(color), height, 0.35)
    return rgb, np.where(hole, 0.0, 1.0)


def strips(u, period, half):
    """Distance to the nearest of a set of parallel strips, and their coverage."""
    d = np.abs(((u + period / 2) % period) - period / 2)
    return d, np.clip(half + 0.5 - d, 0, 1)


def solid_net(size=512):
    """Solid Net, one block (texture_span 1, the lattice meets itself at the edges): flat pale-steel bands woven over
    and under in two diagonals, steel-blue shadows where they cross, open gaps between. Returns (colour, alpha)."""
    y, x = grid(size)
    period = size / 3
    half = period * 0.16
    d1, a1 = strips(x + y, period * np.sqrt(2), half * np.sqrt(2))
    d2, a2 = strips(x - y, period * np.sqrt(2), half * np.sqrt(2))
    first_on_top = (np.floor((x + y) / (period * np.sqrt(2))) + np.floor((x - y) / (period * np.sqrt(2)))) % 2 == 0
    h1 = 8 + 3 * np.sqrt(np.clip(1 - (d1 / (half * np.sqrt(2))) ** 2, 0, 1))
    h2 = 8 + 3 * np.sqrt(np.clip(1 - (d2 / (half * np.sqrt(2))) ** 2, 0, 1))
    both = (a1 > 0) & (a2 > 0)
    height = np.where(both, np.where(first_on_top, h1 + 3, h2 + 3), np.maximum(h1 * a1, h2 * a2))
    under = both & np.where(first_on_top, d2 < d1 + 1e9, False)
    color = BEIGE * (0.94 + 0.1 * value_noise(size, 12, 221, 3))[..., None]
    # the band underneath darkens to steel blue near a crossing
    shade = both & ~np.where(first_on_top, a1 >= a2, a2 > a1)
    color = np.where(shade[..., None], STEEL_BLUE * 1.2, color)
    rgb = finish(np.ascontiguousarray(color), height, 0.3)
    return rgb, np.maximum(a1, a2)


def striped_net(size=512):
    """Striped Net, one block (texture_span 1): upright slats, pale steel and steel blue in turn, with see-through
    slots between, held by a pale rail across the middle of the block. Returns (colour, alpha)."""
    y, x = grid(size)
    n = 4
    p = size / n
    u = x % p
    slat = (u > p * 0.12) & (u < p * 0.62)
    k = np.floor(x / p)
    rail = np.abs(y - size / 2) < p * 0.18
    solid = slat | rail
    du = np.minimum(u - p * 0.12, p * 0.62 - u)
    height = np.where(rail, 12 + 3 * bevel(p * 0.18 - np.abs(y - size / 2), 5), 8 + 3 * bevel(du, 5))
    noise = (0.94 + 0.1 * value_noise(size, 12, 231, 3))[..., None]
    color = np.where(((k % 2 == 1) & ~rail)[..., None], STEEL_BLUE * 1.15, BEIGE) * noise
    rgb = finish(np.ascontiguousarray(color), height, 0.3)
    return rgb, np.where(solid, 1.0, 0.0)


def square_mesh(size=512):
    """Square Mesh, one block (texture_span 1): a cast frame in orange-painted steel, two square openings a side
    with rounded corners, chipped to bare steel on the edges. Returns (colour, alpha)."""
    y, x = grid(size)
    p = size / 2
    u, v = x % p, y % p
    w = p * 0.13
    c = p / 2
    # the opening: a rounded square; distance from its outline
    q = np.maximum(np.abs(u - c), np.abs(v - c))
    rr = p * 0.12
    ox, oy = np.maximum(np.abs(u - c) - (c - w - rr), 0), np.maximum(np.abs(v - c) - (c - w - rr), 0)
    corner = np.hypot(ox, oy)
    open_ = corner < rr
    edge = rr - corner
    height = 12 - 6 * np.clip(1 - (-edge) / 10, 0, 1)
    color = ORANGE_STEEL * (0.92 + 0.12 * value_noise(size, 10, 241, 3))[..., None]
    chips = (smoothstep(value_noise(size, 48, 242, 2), 0.72, 0.76) > 0.5) & (-edge < 8)
    color = np.where(chips[..., None], STEEL * 0.9, color)
    rgb = finish(np.ascontiguousarray(color), height, 0.32)
    return rgb, np.where(open_, 0.0, 1.0)


def worn_metal(size=1024):
    """Worn Metal over 4 x 4 blocks (texture_span 4): sage-green painted steel plates, two blocks square and offset
    half a block, joined by rusty brown seams with bolts at the corners; the paint is scuffed and chipped."""
    y, x = grid(size)
    B = size / 4
    P = 2 * B
    u, v = (x + B / 2) % P, (y + B / 2) % P
    du, dv = np.minimum(u, P - u), np.minimum(v, P - v)
    seam = np.minimum(du, dv) < B * 0.06
    height = 10 * bevel(np.minimum(du, dv) - B * 0.06, 10) + 2
    paint = np.array([116, 146, 122]) * (0.9 + 0.16 * value_noise(size, 6, 251, 4))[..., None]
    rust = np.array([122, 84, 52]) * (0.85 + 0.25 * value_noise(size, 24, 252, 3))[..., None]
    wear = smoothstep(value_noise(size, 20, 253, 4) + 0.35 * (1 - bevel(np.minimum(du, dv), B * 0.25)), 0.72, 0.8)
    color = np.where(seam[..., None], rust, paint * (1 - wear[..., None]) + STEEL_DARK * 1.2 * wear[..., None])
    height -= 1.5 * wear
    # scratches: thin bright lines along the plates
    scratch = smoothstep(stretched_noise(size, 96, 3, 254, 1), 0.86, 0.88) * (1 - seam)
    color = color + 30 * scratch[..., None]
    color = np.ascontiguousarray(color)
    centers = [((i * P - B / 2 + s * B * 0.16) % size, (j * P - B / 2 + t * B * 0.16) % size)
               for i in range(2) for j in range(2) for s in (-1, 1) for t in (-1, 1)]
    height, color = bolts(height, color, size, centers, 7, STEEL * 0.95)
    return finish(color, height, 0.3)


def hull_floor(size=1024):
    """Hull Floor over 2 x 2 blocks (texture_span 2): light steel deck plates offset half a block, each a field of
    raised anti-slip bars turned a different way per plate, and a steel-blue plate in each repeat."""
    y, x = grid(size)
    B = size / 2
    u, v = (x + B / 2) % B, (y + B / 2) % B
    qi = np.floor(((x + B / 2) % size) / B).astype(int)
    qj = np.floor(((y + B / 2) % size) / B).astype(int)
    q = qi + 2 * qj
    du, dv = np.minimum(u, B - u), np.minimum(v, B - v)
    rim = np.minimum(du, dv)
    angles = np.array([np.pi / 4, -np.pi / 4, 0.0, np.pi / 2])[q]
    t = u * np.cos(angles) + v * np.sin(angles)
    period = B / 9
    bars = np.abs((t % period) - period / 2) < period * 0.22
    inner = rim > B * 0.08
    height = 4 + 6 * bevel(rim, 8) + np.where(bars & inner, 5, 0)
    height = blur_wrap(height, 1.2)
    noise = (0.94 + 0.1 * value_noise(size, 14, 261, 3))[..., None]
    base = np.where((q == 1)[..., None], STEEL_BLUE * 1.15, STEEL * 1.02)
    color = base * noise
    color = np.where((bars & inner)[..., None], color * 0.62, color)
    return finish(np.ascontiguousarray(color), height, 0.3)


def painted_wall(size=1024):
    """Painted Wall over 4 x 4 blocks (texture_span 4): off-white paint over plaster, a soft blue lower band two
    blocks high under a thin trim line, speckled with wear and the odd chip."""
    y, x = grid(size)
    B = size / 4
    height = 3 * value_noise(size, 10, 271, 4)
    lower = y > 2 * B
    trim = np.abs(y - 2 * B) < B * 0.035
    white = np.array([232, 228, 216]) * (0.95 + 0.07 * value_noise(size, 8, 272, 3))[..., None]
    blue = np.array([150, 176, 204]) * (0.94 + 0.08 * value_noise(size, 8, 273, 3))[..., None]
    color = np.where(lower[..., None], blue, white)
    color = np.where(trim[..., None], STEEL_BLUE * 1.2, color)
    height = np.where(trim, 6, height)
    speck = smoothstep(value_noise(size, 300, 274, 1), 0.86, 0.9)
    color = color * (1 - 0.3 * speck[..., None])
    chip = smoothstep(value_noise(size, 40, 275, 2), 0.82, 0.85)
    color = color * (1 - chip[..., None]) + np.array([182, 172, 154]) * chip[..., None]
    height -= 1.5 * chip
    # the paint wraps the block in bands at 2 blocks; the top half of the next repeat is white again
    return finish(np.ascontiguousarray(color), height, 0.25)


def worn_tile(size=1024):
    """Worn Tile over 2 x 2 blocks (texture_span 2): sage-green glazed tiles laid in an octagon and small-square
    pattern, offset half a block, crazed and chipped down to the grey body here and there."""
    y, x = grid(size)
    B = size / 2
    T = B / 2                                   # one octagon tile
    u, v = (x + T / 2) % T - T / 2, (y + T / 2) % T - T / 2
    c = T / (2 + np.sqrt(2))                    # the corner cut of a regular octagon T wide
    gap = T * 0.025
    oct_edge = np.minimum(T / 2 - np.maximum(np.abs(u), np.abs(v)), (T - c - np.abs(u) - np.abs(v)) / np.sqrt(2))
    in_oct = oct_edge > gap
    # the small squares fill the corners where four octagons meet
    cu, cv = T / 2 - np.abs(u), T / 2 - np.abs(v)
    sq_edge = (c - cu - cv) / np.sqrt(2)
    in_sq = sq_edge > gap
    tile = in_oct | in_sq
    edge = np.where(in_oct, oct_edge, sq_edge) - gap
    height = np.where(tile, 6 + 3 * bevel(edge, 6), 1.5)
    glaze = np.array([128, 158, 136]) * (0.9 + 0.16 * value_noise(size, 6, 281, 4))[..., None]
    glaze = np.where(in_sq[..., None], np.array([96, 120, 118]) * 1.0, glaze)
    grout = np.array([88, 92, 84])
    color = np.where(tile[..., None], glaze, grout)
    chip = smoothstep(value_noise(size, 18, 282, 3), 0.76, 0.8) * tile
    color = color * (1 - chip[..., None]) + np.array([150, 140, 120]) * chip[..., None]
    height -= 3 * chip
    craze = smoothstep(np.abs(value_noise(size, 40, 283, 2) - 0.5), 0.0, 0.012)
    color = color * (0.85 + 0.15 * craze)[..., None]
    return finish(np.ascontiguousarray(color), height, 0.3)


def ice(size=1024):
    """Ice over 2 x 2 blocks (texture_span 2): clear blue ice, cloudy deep down, with long pale cracks and frosted
    patches near the surface. Returns (colour, alpha): mostly solid, a little see-through where it is clear."""
    y, x = grid(size)
    deep = value_noise(size, 4, 291, 4)
    cloud = value_noise(size, 10, 292, 4)
    color = (np.array([36, 92, 150]) * (1 - deep[..., None]) + np.array([96, 162, 210]) * deep[..., None])
    color = color + 50 * smoothstep(cloud, 0.55, 0.85)[..., None]
    cracks = np.zeros((size, size))
    for k, seed in enumerate((293, 294, 295)):
        n = value_noise(size, 6 + 3 * k, seed, 3)
        cracks = np.maximum(cracks, 1 - smoothstep(np.abs(n - 0.5), 0.0, 0.006 + 0.002 * k))
    color = color + 110 * cracks[..., None]
    frost = smoothstep(value_noise(size, 16, 296, 3), 0.62, 0.8)
    color = color * (1 - 0.5 * frost[..., None]) + np.array([210, 230, 240]) * 0.5 * frost[..., None]
    height = 3 * cloud + 2 * cracks - 1.5 * frost
    rgb = finish(np.clip(color, 0, 255), height, 0.2)
    alpha = np.clip(0.82 + 0.1 * frost + 0.08 * cracks + 0.06 * cloud, 0, 1)
    return rgb, alpha


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
    "extruded_metal": extruded_metal,
    "punched_steel": punched_steel,
    "solid_net": solid_net,
    "striped_net": striped_net,
    "square_mesh": square_mesh,
    "worn_metal": worn_metal,
    "hull_floor": hull_floor,
    "painted_wall": painted_wall,
    "worn_tile": worn_tile,
    "ice": ice,
}

# The textures with a see-through alpha channel.
ALPHA = {"glass_tile", "punched_steel", "solid_net", "striped_net", "square_mesh", "ice"}


def main():
    args = sys.argv[1:]
    out = args[0] if args else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mods", "vanilla",
                                             "textures", "blocks")
    names = args[1:] or list(TEXTURES)
    os.makedirs(out, exist_ok=True)
    for name in names:
        result = TEXTURES[name]()
        if name in ALPHA:
            save(os.path.join(out, f"{name}.png"), *result)
        elif name == "concrete_3":
            save(os.path.join(out, f"{name}.png"), result[0])
            save(os.path.join(out, f"{name}_glow.png"), result[1])
        else:
            save(os.path.join(out, f"{name}.png"), result)
        print(name)


if __name__ == "__main__":
    main()
