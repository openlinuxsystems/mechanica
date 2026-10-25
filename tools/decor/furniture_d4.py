"""Furniture, frames and stairs (parts audit batch D4): the chunk-23 Chair, Stool, Wooden Table, Pallet, Door Frame,
Window Frame and Stairs Block, which were Part Editor boxes until now.

The Chair, Stool, Wooden Table and Pallet are Settler Patchwork (mismatched scrap planks, zinc brackets and nails, a
faded sea-teal plank, rope); the Door Frame and Window Frame are Colony Works joinery (steel-blue rails round a bone
lintel or sill, graphite corner blocks and hardware), in painted timber because they are made of Scrap Wood: where the
paint has chipped the wood shows. The Stairs Block is the Colony staircase kit's (tools/parts_building/stairs_windows.py:
steel-blue stringers, mustard nosings, graphite bolts) with checker-plate treads, built like the Ramp Block.

tools/part_kit/decor_kit.py has the shared materials and the runner; build_decor.py runs this batch as ``d4``.
Axes are the decor kit's: Blender Z up, the part's front towards -Y (glTF +Z, toward the builder).
"""

from __future__ import annotations

import math
import random

import numpy as np
from mathutils import Matrix, Vector

import building_kit as B
import decor_kit as D
import farm
from decor_kit import Geo, Part, box, cylinder, sweep

lt, gc = D.lt, D.gc
plank, nails, WOODS = farm.plank, farm.nails, farm.WOODS


# =====================================================================================================
# Materials
# =====================================================================================================

def painted_wood_surface(seed, color, wood=D.WOOD, chip=0.6):
    """Painted timber: the containers' chipped paint, but scrap wood (not steel) shows where it has worn through."""
    wa, wh, wr, _ = D.fm.wood_surface(seed + 100, wood)
    chips = lt.fbm(seed, 6.0) + 0.35 * lt.fnoise(seed + 1, 60.0)
    chip_mask = np.clip((chips - chip) * 14, 0, 1)
    sc = lt.scratches(seed + 2, 120, bias=0.0)
    dirt = np.clip(lt.fbm(seed + 3, 2.5) * 1.5 - 0.25, 0, 1)
    paint = lt.srgb(color)[None, None, :] * (1 + 0.05 * lt.fbm(seed + 4, 8.0)[..., None]) * (1 - 0.18 * dirt[..., None])
    paint = paint + 0.18 * sc[..., None] * (0.85 - paint)
    m = np.maximum(chip_mask, sc * 0.5)[..., None]
    albedo = paint * (1 - m) + wa * m
    height = 0.6 * (1 - m[..., 0]) + 0.2 * wh
    rough = (0.55 + 0.1 * dirt) * (1 - m[..., 0]) + wr * m[..., 0]
    return albedo, height, rough, np.zeros((lt.SIZE, lt.SIZE))


def checker_surface(seed, color="#6a7078"):
    """Checker (tread) plate: worn steel with raised lugs, alternate ones turned a quarter, polished on their tops."""
    albedo, height, rough, metal = lt.metal_surface(seed, color, 0.46, 0.82, wear=0.9, edge_light=0.3)
    size = lt.SIZE
    period = size // 16                       # about 3 cm a lug at the kit's 0.5 m texture tile
    yy, xx = np.mgrid[0:size, 0:size]
    i, j = xx // period, yy // period
    u = (xx % period) / period - 0.5
    v = (yy % period) / period - 0.5
    a, b = (u + v) / math.sqrt(2), (u - v) / math.sqrt(2)
    turn = ((i + j) % 2).astype(bool)
    a, b = np.where(turn, b, a), np.where(turn, a, b)
    lug = np.clip(1 - ((a / 0.36) ** 2 + (b / 0.09) ** 2), 0, 1)
    top = (lug > 0.25).astype(float)
    albedo = albedo * (1 + 0.28 * top[..., None])
    height = height + 2.2 * np.sqrt(lug)
    rough = rough - 0.16 * top
    return albedo, height, rough, metal


def upright(surface):
    """A surface turned a quarter, so wood grain runs up the box-projected faces of a leg or a post."""
    return tuple(np.ascontiguousarray(np.swapaxes(a, 0, 1)) for a in surface)


UPRIGHT = {"wood": (971, D.WOOD), "wood_b": (973, "#8a6644"), "wood_c": (975, "#a9845a")}   # the containers' scrap woods


def materials_d4(m):
    for name, (seed, color) in UPRIGHT.items():
        m[f"{name}_up"] = lt.textured(f"d4_{name}_up", gc.half_res(upright(D.fm.wood_surface(seed, color))), normal_strength=1.2)
    m["steel_wood"] = lt.textured("d4_steel_wood", gc.half_res(painted_wood_surface(1401, D.STEEL)), normal_strength=0.8)
    m["bone_wood"] = lt.textured("d4_bone_wood", gc.half_res(painted_wood_surface(1403, D.BONE, chip=0.64)), normal_strength=0.8)
    m["sea_wood"] = lt.textured("d4_sea_wood", gc.half_res(painted_wood_surface(1405, "#3f8079", chip=0.52)), normal_strength=0.8)
    m["checker"] = lt.textured("d4_checker", gc.half_res(checker_surface(1407)), normal_strength=2.2)


# =====================================================================================================
# Settler Patchwork furniture
# =====================================================================================================

def zinc_corner(g, p, sx, sy, h=0.04, w=0.035):
    """A zinc angle bracket bent round a leg's outer corner at p (the corner's x and y), nailed on both faces."""
    x, y, z = p
    box(g, (x + sx * 0.0012, y - sy * w / 2, z), (0.0012, w / 2, h / 2), "zinc", bevel=0.0008)
    box(g, (x - sx * w / 2, y + sy * 0.0012, z), (w / 2, 0.0012, h / 2), "zinc", bevel=0.0008)
    nails(g, [(x + sx * 0.0025, y - sy * w * 0.6, z + dz) for dz in (-h * 0.25, h * 0.25)], (sx, 0, 0))
    nails(g, [(x - sx * w * 0.6, y + sy * 0.0025, z + dz) for dz in (-h * 0.25, h * 0.25)], (0, sy, 0))


def chair(m):
    """Chair, 2 x 4 x 2: a scrap-wood chair. Odd planks on the seat (one in faded sea teal), back legs run up into the
    back posts, two back slats and a top rail, aprons and low stretchers, zinc angle brackets on the front corners."""
    rng = random.Random(2020)
    g = Geo()
    hx, hy, hz = 0.25, 0.25, 0.5
    z0, seat = -hz + 0.004, -hz + 0.45          # the legs stop a hair above the floor: their scrap planks lean
    leg, lx = 0.021, hx - 0.03
    # legs: the back ones run on up as the back posts
    for sx in (-1, 1):
        plank(g, (sx * lx, -lx, (z0 + seat - 0.026) / 2), (leg, leg, (seat - 0.026 - z0) / 2), "wood_b_up", rng)
        plank(g, (sx * lx, lx, (z0 + hz - 0.008) / 2), (leg, leg + 0.002, (hz - 0.008 - z0) / 2), "wood_c_up", rng)
        box(g, (sx * lx, lx, hz - 0.006), (leg + 0.001, leg + 0.003, 0.004), "wood_b", bevel=0.002)
    # the seat: three odd planks with gaps, nailed into the aprons
    seat_mats = ("wood", "sea_wood", "wood_b")
    y0, y1 = -hy + 0.004, lx - leg - 0.002
    w = (y1 - y0 - 0.012) / 3
    for k in range(3):
        yc = y0 + w / 2 + k * (w + 0.006)
        long = hx - 0.012 - 0.008 * (k == 1)
        plank(g, (0.004 * (k - 1), yc, seat - 0.013), (long, w / 2, 0.012 + 0.002 * (k == 2)), seat_mats[k], rng)
        nails(g, [(sx * (lx - 0.002), yc + dy, seat + 0.0005 + 0.004 * (k == 2)) for sx in (-1, 1) for dy in (-w * 0.25, w * 0.25)],
              (0, 0, 1))
    # aprons under the seat and stretchers low down
    za = seat - 0.026 - 0.03
    box(g, (0, -lx, za), (lx - leg, 0.01, 0.03), "wood_c", bevel=0.003)
    box(g, (0, lx, za), (lx - leg, 0.01, 0.03), "wood", bevel=0.003)
    for sx in (-1, 1):
        box(g, (sx * lx, 0, za), (0.01, lx - leg, 0.03), "wood_b", bevel=0.003)
        plank(g, (sx * lx, 0, z0 + 0.12), (0.011, lx - leg, 0.014), "wood", rng)
    plank(g, (0, -lx, z0 + 0.17), (lx - leg, 0.011, 0.014), "wood_c", rng)
    # the back: two slats and a top rail between the posts, nailed through the posts
    for z, half_h, mat in ((seat + 0.13, 0.035, "wood"), (seat + 0.28, 0.04, "wood_b"), (hz - 0.06, 0.045, "wood_c")):
        plank(g, (0, lx + 0.004, z), (lx + leg - 0.002, 0.011, half_h), mat, rng)
        nails(g, [(sx * lx, lx - 0.008, z) for sx in (-1, 1)], (0, -1, 0))
    # the patched top rail: a short sea-teal batten over a split, two nails
    box(g, (0.07, lx - 0.0085, hz - 0.06), (0.06, 0.0025, 0.022), "sea_wood", bevel=0.001)
    nails(g, [(0.07 + dx, lx - 0.012, hz - 0.06) for dx in (-0.045, 0.045)], (0, -1, 0))
    # zinc angle brackets where the front legs meet the apron, and on the back posts at the seat
    for sx in (-1, 1):
        zinc_corner(g, (sx * (lx + leg), -lx - leg, za), sx, -1)
        box(g, (sx * (lx + leg + 0.0012), lx, seat - 0.01), (0.0012, 0.016, 0.03), "zinc", bevel=0.0008)
        nails(g, [(sx * (lx + leg + 0.0026), lx, seat - 0.01 + dz) for dz in (-0.015, 0.015)], (sx, 0, 0))
    return g


def stool(m):
    """Stool, 2 x 2 x 2: a three-legged stool, a round seat of three planks, splayed legs with rungs, one leg split and
    bound with rope."""
    g = Geo()
    hz = 0.25
    top = hz - 0.004
    D.lathe(g, [(0.0, top - 0.032), (0.192, top - 0.032), (0.2, top - 0.026), (0.2, top - 0.006), (0.193, top), (0.0, top)],
            "wood", steps=36)
    # the seams between its three planks, and the battens under it
    for y in (-0.064, 0.064):
        half = math.sqrt(0.2 ** 2 - y ** 2) - 0.004
        box(g, (0, y, top + 0.0002), (half, 0.0016, 0.0004), "hole")
        nails(g, [(sx * 0.11, y + s * 0.03, top) for sx in (-1, 1) for s in (-1, 1)], (0, 0, 1))
    for sx in (-1, 1):
        box(g, (sx * 0.1, 0, top - 0.044), (0.016, 0.15, 0.012), "wood_b", bevel=0.003)
    legs = []
    for k in range(3):
        a = math.radians(90 + 120 * k)
        c, s = math.cos(a), math.sin(a)
        p0 = Vector((0.11 * c, 0.11 * s, top - 0.03))
        p1 = Vector((0.205 * c, 0.205 * s, -hz + 0.012))
        legs.append((p0, p1))
        cylinder(g, p0, p1, 0.02, WOODS[k] + "_up", segs=10)
    # rungs between the legs
    for k in range(3):
        (a0, a1), (b0, b1) = legs[k], legs[(k + 1) % 3]
        t = 0.62
        cylinder(g, a0.lerp(a1, t), b0.lerp(b1, t), 0.011, "wood_b", segs=8)
    # leg 0 split at mid height and bound with rope: a few turns round it, the loose end hanging
    p0, p1 = legs[0]
    axis = (p1 - p0).normalized()
    u = axis.cross(Vector((0, 0, 1))).normalized()
    w = axis.cross(u)
    c0 = p0.lerp(p1, 0.34)
    box(g, tuple(c0 + w * 0.0195), (0.0008, 0.0008, 0.035), "hole", rot=Matrix((u, w, axis)).transposed())
    pts = []
    for n in range(73):
        t = n / 72
        ang = t * 2 * math.pi * 5
        pts.append(c0 + axis * (t - 0.5) * 0.06 + (u * math.cos(ang) + w * math.sin(ang)) * 0.0235)
    sweep(g, pts, 0.0035, "twine", segs=6)
    end = pts[-1]
    sweep(g, [end, end + Vector((0, 0, -0.012)) + u * 0.006, end + Vector((0, 0, -0.03)) + u * 0.01], 0.0032, "twine", segs=6)
    return g


def wooden_table(m):
    """Wooden Table, 6 x 3 x 4: a top of four odd planks on cleats, two trestles (head beam, splayed legs, foot beam)
    held apart by a stretcher wedged through them, and a zinc patch nailed over a broken front corner."""
    rng = random.Random(2030)
    g = Geo()
    hx, hy, hz = 0.75, 0.5, 0.375
    top = hz - 0.005
    t = 0.017
    w = (2 * hy - 0.012 - 3 * 0.006) / 4
    for k in range(4):
        y = -hy + 0.006 + w / 2 + k * (w + 0.006)
        plank(g, (0.003 * (k % 2), y, top - t - 0.0015 * (k == 2)), (hx - 0.004 - 0.006 * (k % 2), w / 2, t - 0.0015 * (k == 2)), WOODS[(k + 1) % 3])
        nails(g, [(sx * 0.62, y + dy, top + 0.0002) for sx in (-1, 1) for dy in (-w * 0.28, w * 0.28)], (0, 0, 1))
    under = top - 2 * t
    for sx in (-1, 1):
        box(g, (sx * 0.62, 0, under - 0.012), (0.03, hy - 0.06, 0.012), "wood_b", bevel=0.003)
    # the trestles
    tx = 0.5
    head = under - 0.024 - 0.028
    foot = -hz + 0.024
    for sx in (-1, 1):
        x = sx * tx
        plank(g, (x, 0, head), (0.03, hy - 0.08, 0.028), "wood_c", rng)
        plank(g, (x, 0, foot), (0.03, hy - 0.03, 0.024), "wood_b")
        for sy in (-1, 1):
            B.bar(g, (x, sy * 0.22, head - 0.02), (x, sy * (hy - 0.09), foot + 0.02), 0.026, 0.03, WOODS[(sx + sy + 2) % 3] + "_up", 0.004,
                  up=(1, 0, 0))
            nails(g, [(x - sx * 0.0312, sy * 0.25, head), (x - sx * 0.0312, sy * (hy - 0.11), foot + 0.004)], (-sx, 0, 0))
        # a diagonal brace, nailed at both ends
        B.bar(g, (x - sx * 0.035, -0.22, head - 0.03), (x - sx * 0.035, hy - 0.16, foot + 0.035), 0.022, 0.006, "wood", 0.002,
              up=(1, 0, 0))
    # the stretcher through both trestles, wedged outside them
    zs = -0.06
    plank(g, (0, 0, zs), (tx + 0.07, 0.022, 0.032), "wood", rng)
    for sx in (-1, 1):
        box(g, (sx * (tx + 0.052), 0, zs), (0.008, 0.03, 0.045), "wood_b", bevel=0.002,
            rot=Matrix.Rotation(sx * 0.12, 3, "Y"))
    # the zinc patch over the front right corner, folded down over the edge
    px, py = hx - 0.095, -hy + 0.075
    box(g, (px, py, top + 0.0006), (0.085, 0.07, 0.0006), "zinc", bevel=0.0004)
    box(g, (px, -hy + 0.0045, top - t), (0.085, 0.0007, t * 0.9), "zinc", bevel=0.0004)
    nails(g, [(px + dx, py + dy, top + 0.0012) for dx in (-0.065, 0.065) for dy in (-0.05, 0.05)], (0, 0, 1))
    nails(g, [(px + dx, -hy + 0.0038, top - t) for dx in (-0.06, 0.06)], (0, -1, 0))
    return g


def pallet(m):
    """Pallet, 5 x 1 x 5: a 1.25 m shipping pallet. Three notched stringers, seven worn top boards (odd woods, one
    split short, one newer), three bottom boards, nail heads over the stringers and a stencilled outpost code."""
    rng = random.Random(2060)
    g = Geo()
    h, hz = 0.625, 0.125
    z_bot = -hz + 0.022
    z_top = hz - 0.03
    xs = (-h + 0.045, 0.0, h - 0.045)
    # bottom boards, across the stringers
    for y in (-h + 0.06, 0.0, h - 0.06):
        plank(g, (0, y, (-hz + z_bot) / 2), (h - 0.003, 0.055, (z_bot + hz) / 2), WOODS[int(y > 0)])
    # stringers: a rail over three feet, the gaps between the feet being the fork notches
    for k, x in enumerate(xs):
        mat = WOODS[(k + 2) % 3]
        plank(g, (x, 0, z_top - 0.04), (0.04, h - 0.004, 0.04), mat)
        for y in (-h + 0.075, 0.0, h - 0.075):
            box(g, (x, y, (z_bot + z_top - 0.08) / 2), (0.04, 0.072, (z_top - 0.08 - z_bot) / 2 + 0.001), mat, bevel=0.003)
    # top boards
    n = 7
    w = (2 * h - 0.006 - (n - 1) * 0.022) / n
    for k in range(n):
        y = -h + 0.003 + w / 2 + k * (w + 0.022)
        if k == 2:
            # split short: a board broken off past the middle stringer, its end ragged
            plank(g, (-0.21, y, z_top + 0.0115), (h - 0.21 - 0.003, w / 2, 0.0115), "wood_c")
            for j in range(3):
                box(g, (0.205 + 0.02 * j, y + (j - 1) * w * 0.3, z_top + 0.011), (0.012, w * 0.17, 0.011), "wood_c", bevel=0.002)
            nails(g, [(x, y + dy, z_top + 0.025) for x in xs[:2] for dy in (-w * 0.25, w * 0.25)], (0, 0, 1))
            continue
        mat = "wood_b" if k == 5 else WOODS[k % 3]
        plank(g, (0, y, z_top + 0.0125 - 0.002 * (k % 2)), (h - 0.003 - 0.004 * (k % 3), w / 2, 0.0125 - 0.002 * (k % 2)), mat)
        nails(g, [(x, y + dy, z_top + 0.025) for x in xs for dy in (-w * 0.25, w * 0.25)], (0, 0, 1))
    # a stencil on the end of the middle stringer
    D.pixel_text(g, "OP-7", (0.0, -h + 0.0035, (z_bot + z_top) / 2 - 0.035), (1, 0, 0), (0, 0, 1), 0.006, "faded_dark")
    return g


# =====================================================================================================
# Colony Works frames (painted timber)
# =====================================================================================================

DOOR_JAMB = 0.1         # jamb width; the opening is 1.05 m wide
DOOR_HEAD = 0.2         # head height; the opening is 2.3 m tall


def door_frame(m):
    """Door Frame, 5 x 10 x 1 (the art-direction door, 1.25 x 2.5 m): steel-blue jambs and head rails round a bone
    lintel panel with a stencilled code, graphite corner blocks and foot plates, a door stop, three hinge leaves on the
    left jamb and a strike plate on the right, a flat graphite threshold the player walks over."""
    g = Geo()
    hx, hy, hz = 0.625, 0.125, 1.25
    jx = hx - DOOR_JAMB / 2
    depth = hy - 0.03
    lintel0 = hz - DOOR_HEAD
    for sx in (-1, 1):
        x = sx * jx
        box(g, (x, 0, (-hz + 0.012 + lintel0) / 2), (DOOR_JAMB / 2 - 0.006, depth, (lintel0 + hz - 0.012) / 2), "steel_wood", bevel=0.006)
        # architrave strips front and back, a door stop on the inside face
        for sy in (-1, 1):
            box(g, (x + sx * 0.004, sy * (depth + 0.008), (-hz + 0.02 + lintel0) / 2), (DOOR_JAMB / 2 - 0.004, 0.008, (lintel0 + hz - 0.02) / 2),
                "steel_wood", bevel=0.003)
        box(g, (x - sx * (DOOR_JAMB / 2 - 0.004), 0.03, (-hz + 0.02 + lintel0) / 2), (0.012, 0.018, (lintel0 + hz - 0.02) / 2 - 0.01),
            "graphite", bevel=0.003)
        # foot plate, bolted
        box(g, (x, 0, -hz + 0.006), (DOOR_JAMB / 2, hy - 0.004, 0.006), "graphite", bevel=0.002)
        for sy in (-1, 1):
            B.bolt(g, (x, sy * (hy - 0.03), -hz + 0.012), r=0.007)
        # bolts up the face of the jamb, front and back
        for sy in (-1, 1):
            for z in (-0.85, 0.0, 0.75):
                B.bolt(g, (x + sx * 0.008, sy * (depth + 0.016), z), (0, sy, 0), r=0.0065)
    # the head: a steel-blue top rail and sill rail round a bone lintel panel
    z_top = hz - 0.03
    box(g, (0, 0, z_top), (hx - 0.004, depth + 0.012, 0.028), "steel_wood", bevel=0.006)
    box(g, (0, 0, lintel0 + 0.02), (hx - DOOR_JAMB + 0.004, depth + 0.012, 0.02), "steel_wood", bevel=0.005)
    zc = (lintel0 + 0.04 + z_top - 0.028) / 2
    zh = (z_top - 0.028 - lintel0 - 0.04) / 2
    box(g, (0, 0, zc), (hx - DOOR_JAMB + 0.002, depth, zh), "bone_wood", bevel=0.004)
    # corner blocks where the head meets the jambs, front and back
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * jx, sy * (depth + 0.006), zc), (DOOR_JAMB / 2 - 0.001, 0.012, zh + 0.018), "graphite", bevel=0.005)
            B.bolt(g, (sx * jx, sy * (depth + 0.018), zc), (0, sy, 0), r=0.009)
    # a stencilled code and a mustard tag on the lintel's front
    D.pixel_text(g, "CW-01", (-0.12, -depth - 0.0002, zc), (1, 0, 0), (0, 0, 1), 0.011, "faded_dark")
    box(g, (0.2, -depth - 0.002, zc), (0.06, 0.002, 0.022), "mustard", bevel=0.001)
    # hinge leaves on the left jamb's inside face, a strike plate on the right
    xi = -hx + DOOR_JAMB
    for z in (-0.95, 0.0, 0.85):
        box(g, (xi + 0.0015, -0.03, z), (0.0015, 0.025, 0.05), "graphite", bevel=0.001)
        cylinder(g, (xi + 0.006, -0.058, z - 0.05), (xi + 0.006, -0.058, z + 0.05), 0.007, "graphite", segs=10)
        for dz in (-0.03, 0.03):
            D.dome(g, (xi + 0.003, -0.03, z + dz), (1, 0, 0), 0.004, "bolt", segs=6)
    box(g, (hx - DOOR_JAMB - 0.0015, -0.03, -0.25), (0.0015, 0.02, 0.07), "chrome_bright", bevel=0.001)
    box(g, (hx - DOOR_JAMB - 0.0025, -0.03, -0.25), (0.0012, 0.008, 0.025), "hole")
    # the threshold: a flat graphite plate across the opening with mustard nosing strips, 1.2 cm high
    box(g, (0, 0, -hz + 0.006), (hx - DOOR_JAMB, hy - 0.004, 0.006), "graphite", bevel=0.002)
    for sy in (-1, 1):
        box(g, (0, sy * (hy - 0.016), -hz + 0.0125), (hx - DOOR_JAMB - 0.01, 0.01, 0.0007), "mustard")
    return g


def window_frame(m):
    """Window Frame, 4 x 4 x 1: a 1 m steel-blue frame (no glass) round a 0.84 m opening, graphite corner blocks, a
    glazing stop round the opening and a bone sill on the front."""
    g = Geo()
    hx, hy, hz = 0.5, 0.125, 0.5
    rail = 0.08
    depth = 0.07
    inner = hx - rail
    for s in (-1, 1):
        box(g, (0, 0, s * (hz - rail / 2)), (inner + 0.002, depth, rail / 2 - 0.004), "steel_wood", bevel=0.006)
        box(g, (s * (hx - rail / 2), 0, 0), (rail / 2 - 0.004, depth, inner + 0.002), "steel_wood", bevel=0.006)
    for sx in (-1, 1):
        for sz in (-1, 1):
            box(g, (sx * (hx - rail / 2), 0, sz * (hz - rail / 2)), (rail / 2, depth + 0.008, rail / 2), "graphite", bevel=0.006)
            for sy in (-1, 1):
                B.bolt(g, (sx * (hx - rail / 2), sy * (depth + 0.008), sz * (hz - rail / 2)), (0, sy, 0), r=0.008)
    # the glazing stop: a thin graphite bead round the opening, set back from the front
    y = 0.02
    for s in (-1, 1):
        box(g, (0, y, s * (inner - 0.006)), (inner - 0.002, 0.008, 0.006), "graphite", bevel=0.002)
        box(g, (s * (inner - 0.006), y, 0), (0.006, 0.008, inner - 0.014), "graphite", bevel=0.002)
    # the sill: a bone board on the front, flush with the opening, with a drip groove and two brackets under it
    sill_z = -inner - 0.016
    box(g, (0, -0.06, sill_z), (inner + 0.06, 0.06, 0.016), "bone_wood", bevel=0.004)
    box(g, (0, -hy + 0.016, sill_z - 0.016), (inner + 0.05, 0.002, 0.0008), "hole")
    for sx in (-1, 1):
        B.bar(g, (sx * 0.3, -depth, sill_z - 0.016), (sx * 0.3, -depth, sill_z - 0.048), 0.006, 0.012, "graphite", 0.002,
              up=(0, 1, 0))
        B.bar(g, (sx * 0.3, -depth, sill_z - 0.048), (sx * 0.3, -hy + 0.02, sill_z - 0.016), 0.006, 0.006, "graphite", 0.002)
    return g


# =====================================================================================================
# Stairs Block (the Colony staircase kit, built like the Ramp Block)
# =====================================================================================================

def stair_profile(h, d, steps=4):
    """The (y, z) outline under a flight of `steps` steps in a 2h cube, low at the front (-y), inset by d."""
    s = 2 * h / steps
    pts = [(-h + d, -h + d)]
    for k in range(steps):
        z = -h + s * (k + 1) - d
        pts.append((-h + s * k + d, z))
        pts.append((-h + s * (k + 1) + d if k < steps - 1 else h - d, z))
    pts.append((h - d, -h + d))
    return pts


def stairs_block(m):
    """Stairs Block, 4 x 4 x 4: four 25 cm steps in a 1 m block, low at the front. Steel-blue stepped stringers with a
    bone panel on their outer faces, checker-plate treads with mustard nosings, steel risers, a bone back panel in a
    steel-blue rail and graphite base rails, as the Ramp Block."""
    g = Geo()
    h = 0.5
    s = 2 * h / 4
    for sx in (-1, 1):
        x0, x1 = sorted((sx * (h - 0.012), sx * (h - 0.044)))
        B.prism_x(g, stair_profile(h, 0.0), x0, x1, "steel", center=(h - 0.06, -h + 0.06))
        xa, xb = sorted((sx * (h - 0.012), sx * (h - 0.0095)))
        B.prism_x(g, stair_profile(h, 0.045), xa, xb, "bone", center=(h - 0.1, -h + 0.1))
        for k in range(4):
            B.bolt(g, (sx * (h - 0.0095), -h + s * k + s * 0.5, -h + s * (k + 1) - 0.03), (sx, 0, 0), 0.007)
    for k in range(4):
        y0, z1 = -h + s * k, -h + s * (k + 1)
        # tread, nosing and riser
        box(g, (0, y0 + s / 2 + 0.001, z1 - 0.009), (h - 0.044, s / 2 - 0.001, 0.007), "checker", bevel=0.002)
        box(g, (0, y0 + 0.016, z1 - 0.0085), (h - 0.048, 0.016, 0.0065), "mustard", bevel=0.002)
        r0 = -h if k == 0 else z1 - s - 0.006  # the riser stands on (into) the step below, the floor for the first
        box(g, (0, y0 + 0.009, (r0 + z1 - 0.014) / 2), (h - 0.044, 0.009, (z1 - 0.014 - r0) / 2), "steel_dark", bevel=0.003)
        for sx in (-1, 1):
            D.dome(g, (sx * (h - 0.07), y0 + s - 0.04, z1 - 0.002), (0, 0, 1), 0.006, "bolt", segs=8, height=0.0015)
    # the back: a bone panel in a steel-blue rail, and base rails front and back
    box(g, (0, h - 0.012, 0.0), (h - 0.044, 0.01, h - 0.04), "bone", bevel=0.004)
    box(g, (0, h - 0.015, h - 0.025), (h - 0.044, 0.015, 0.025), "steel", bevel=0.004)
    box(g, (0, h - 0.015, -h + 0.04), (h - 0.044, 0.015, 0.02), "steel", bevel=0.004)
    for y in (-h + 0.03, h - 0.06):
        box(g, (0, y, -h + 0.02), (h - 0.044, 0.02, 0.02), "graphite", bevel=0.004)
    return g


# id: Part(size in blocks (x across, y tall, z deep), builder, review note)
BATCH_D4 = {
    "chair": Part((2, 4, 2), chair, "Settler chair: odd scrap planks (one faded sea teal), back slats, zinc angle brackets."),
    "stool": Part((2, 2, 2), stool, "Settler three-legged stool: a round seat of three planks, rungs, a split leg bound in rope."),
    "wooden_table": Part((6, 3, 4), wooden_table, "Settler trestle table: four odd planks on two trestles, a wedged stretcher, a zinc corner patch."),
    "pallet": Part((5, 1, 5), pallet, "Settler shipping pallet: three notched stringers, worn odd boards (one split short), OP-7 stencil."),
    "door_frame": Part((5, 10, 1), door_frame, "Colony door frame at the art-direction door size (1.25 x 2.5 m; opening 1.05 x 2.3 m): "
                       "steel-blue painted timber round a bone lintel, hinges, strike plate, flat threshold."),
    "window_frame": Part((4, 4, 1), window_frame, "Colony window frame, 1 m: steel-blue painted timber, graphite corner blocks, "
                         "glazing stop, bone sill. Open (no glass)."),
    "stairs_block": Part((4, 4, 4), stairs_block, "Colony stairs in a 1 m block, low at the front: steel-blue stringers, checker-plate "
                         "treads, mustard nosings, as the Ramp Block."),
}
