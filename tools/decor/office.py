"""Office and home furniture (parts map batches 13a and 13b), in the Colony Works look (bone panels in steel-blue
rails, graphite trim, mustard tags) with the soft pieces (mattress, pillow, toilet paper) in Settler Patchwork cotton
and the Calendar from the SV-04's crew room in Salvager Tech. tools/part_kit/decor_kit.py has the materials and the
runner; build_decor.py runs them.
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

import decor_kit as D
from decor_kit import Geo, Part, box, cylinder, dome, lathe, sweep

# =====================================================================================================
# Batch 13a
# =====================================================================================================


def shelf_unit(m):
    """4 x 8 x 2: a Colony rack, four perforated steel-blue posts, four bone shelves with steel-blue lips and mustard
    label cards, cross rods on the back and graphite foot plates."""
    g = Geo()
    hx, hy, hz = 0.5, 0.25, 1.0
    post = 0.022
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (hx - post - 0.01), sy * (hy - post - 0.01)
            D.perforated(g, (x, y, 0.0), (post, post, hz - 0.012), "steel", holes=16, face=(0, sy, 0))
            box(g, (x, y, -hz + 0.006), (post + 0.01, post + 0.01, 0.006), "graphite", bevel=0.003)
            box(g, (x, y, hz - 0.006), (post + 0.004, post + 0.004, 0.006), "graphite", bevel=0.003)
    for k, z in enumerate((-0.86, -0.3, 0.26, 0.82)):
        box(g, (0, 0, z), (hx - 0.004, hy - 0.006, 0.011), "bone", bevel=0.004)
        for sy in (-1, 1):
            box(g, (0, sy * (hy - 0.012), z - 0.022), (hx - 2 * post - 0.002, 0.01, 0.022), "steel", bevel=0.004)
        for sx in (-1, 1):
            box(g, (sx * (hx - 0.012), 0, z - 0.018), (0.008, hy - 2 * post - 0.002, 0.016), "steel_dark", bevel=0.003)
        box(g, (0.2 - 0.13 * (k % 2), -hy + 0.0015, z - 0.022), (0.045, 0.0015, 0.014), "card", bevel=0.001)
        box(g, (0.2 - 0.13 * (k % 2), -hy + 0.0005, z - 0.022), (0.05, 0.0015, 0.017), "mustard", bevel=0.001)
    # cross rods on the back, two bays
    for z0, z1 in ((-0.86, 0.0), (0.0, 0.82)):
        for a, b in ((-1, 1), (1, -1)):
            cylinder(g, (a * (hx - 0.05), hy - 0.012, z0 + 0.04), (b * (hx - 0.05), hy - 0.012, z1 - 0.04), 0.005, "graphite", segs=6)
        dome(g, (0, hy - 0.006, (z0 + z1) / 2), (0, 1, 0), 0.012, "bolt", segs=8)
    return g


def metal_table(m):
    """6 x 3 x 4: a Colony workbench, a bone worktop in a graphite band on a steel-blue frame, a lower shelf, a
    drawer with a mustard pull and rubber feet."""
    g = Geo()
    hx, hy, hz = 0.75, 0.5, 0.375
    top = hz - 0.02
    box(g, (0, 0, top), (hx - 0.012, hy - 0.012, 0.018), "bone_dark", bevel=0.004)
    for sy in (-1, 1):
        box(g, (0, sy * (hy - 0.01), top - 0.004), (hx, 0.01, 0.022), "graphite", bevel=0.004)
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.01), 0, top - 0.004), (0.01, hy - 0.02, 0.022), "graphite", bevel=0.004)
    leg = 0.025
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (hx - 0.07), sy * (hy - 0.07)
            box(g, (x, y, -0.01), (leg, leg, hz - 0.035), "steel", bevel=0.005)
            box(g, (x, y, -hz + 0.012), (leg + 0.006, leg + 0.006, 0.012), "rubber", bevel=0.004)
            dome(g, (x - sx * 0.0, y - sy * leg, top - 0.06), (0, -sy, 0), 0.006, "bolt")
    # apron rails under the top and round the lower shelf
    for z, h in ((top - 0.06, 0.035), (-0.24, 0.018)):
        for sy in (-1, 1):
            box(g, (0, sy * (hy - 0.07), z), (hx - 0.095, 0.012, h), "steel", bevel=0.004)
        for sx in (-1, 1):
            box(g, (sx * (hx - 0.07), 0, z), (0.012, hy - 0.095, h), "steel", bevel=0.004)
    box(g, (0, 0, -0.24 + 0.022), (hx - 0.09, hy - 0.09, 0.008), "bone", bevel=0.003)
    # a drawer under the top at the front right
    dx, dz = 0.36, top - 0.085
    box(g, (dx, -hy + 0.09, dz), (0.2, 0.06, 0.045), "steel_dark", bevel=0.004)
    box(g, (dx, -hy + 0.03, dz), (0.19, 0.008, 0.04), "bone", bevel=0.004)
    cylinder(g, (dx - 0.06, -hy + 0.018, dz), (dx + 0.06, -hy + 0.018, dz), 0.006, "mustard", segs=8)
    for sx in (-1, 1):
        cylinder(g, (dx + sx * 0.06, -hy + 0.022, dz), (dx + sx * 0.06, -hy + 0.016, dz), 0.007, "graphite", segs=8)
    box(g, (dx, -hy + 0.0215, dz + 0.024), (0.03, 0.0005, 0.009), "card")
    return g


def stall_door(m):
    """4 x 7 x 1: a washroom stall door, a steel-blue panel in a graphite edge band on chrome barrel hinges, a slide
    latch with a red/white indicator, a coat hook on the inside and the floor gap underneath."""
    g = Geo()
    hx, hy, hz = 0.5, 0.125, 0.875
    t = 0.018
    z0, z1 = -hz + 0.1, hz - 0.03
    zc, zh = (z0 + z1) / 2, (z1 - z0) / 2
    D.panel(g, (0.02, 0, zc), (hx - 0.03, t, zh), 1, 0.035, "graphite", "steel", bevel=0.005)
    # pressed panels on the face
    for zz in (zc + zh * 0.45, zc - zh * 0.45):
        for sy in (-1, 1):
            box(g, (0.02, sy * (t + 0.001), zz), (hx - 0.12, 0.002, zh * 0.4), "steel_dark", bevel=0.003)
    # hinges on the left: knuckles on a graphite post strip, with the pins out at top and bottom
    hxp = -hx + 0.018
    box(g, (hxp, 0, 0), (0.012, 0.016, hz - 0.001), "graphite", bevel=0.004)
    for z in (z1 - 0.12, (z0 + z1) / 2, z0 + 0.12):
        D.hinge(g, (hxp, -0.03, z), 0.1, 0.012, "chrome_bright")
        box(g, (hxp + 0.04, -0.022, z), (0.04, 0.003, 0.04), "chrome_bright", bevel=0.002)
    # the latch on the right: an indicator turn outside, a slide bolt inside
    lx, lz = hx - 0.09, 0.05
    cylinder(g, (lx, -t, lz), (lx, -t - 0.008, lz), 0.028, "chrome_bright", segs=20)
    D.decal(g, (lx, -t - 0.0085, lz), (1, 0, 0), (0, 0, 1), 0.02, 0.02, "occupied")
    box(g, (lx, -t - 0.012, lz - 0.05), (0.008, 0.004, 0.018), "chrome_bright", bevel=0.002)
    box(g, (lx - 0.02, t + 0.01, lz), (0.05, 0.01, 0.014), "chrome_bright", bevel=0.003)
    cylinder(g, (lx - 0.02, t + 0.02, lz), (lx - 0.02, t + 0.04, lz), 0.006, "graphite", segs=8)
    # a coat hook on the inside
    hk = Vector((0.05, t, z1 - 0.2))
    box(g, tuple(hk + Vector((0, 0.004, 0))), (0.018, 0.004, 0.03), "chrome_bright", bevel=0.002)
    sweep(g, [hk + Vector((0, 0.008, 0.01)), hk + Vector((0, 0.05, 0.0)), hk + Vector((0, 0.065, 0.03)), hk + Vector((0, 0.058, 0.05))],
          0.006, "chrome_bright", segs=8)
    # rubber stop strip on the latch edge
    box(g, (hx - 0.006, 0, zc), (0.006, 0.01, zh - 0.02), "rubber", bevel=0.002)
    return g


def occupied_image():
    im = Image.new("RGB", (128, 128), (228, 224, 214))
    d = ImageDraw.Draw(im)
    d.pieslice([0, 0, 127, 127], 180, 360, fill=(200, 66, 58))
    d.line([0, 64, 127, 64], fill=(40, 42, 46), width=4)
    return im


def calendar_image():
    """The calendar page: a print of the SV-04 Salvager over the hills, the month's grid with day 20 ringed."""
    w, h = 384, 576
    im = Image.new("RGB", (w, h), (236, 230, 214))
    d = ImageDraw.Draw(im)
    # the print: dusk sky, hills, the ship
    ph = 300
    for y in range(ph):
        t = y / ph
        d.line([16, 16 + y, w - 16, 16 + y], fill=(int(64 + 120 * t), int(110 + 80 * t), int(150 + 40 * t)))
    d.polygon([(16, 250), (110, 205), (210, 238), (300, 200), (w - 16, 230), (w - 16, 316), (16, 316)], fill=(84, 120, 78))
    d.polygon([(16, 280), (150, 250), (w - 16, 270), (w - 16, 316), (16, 316)], fill=(64, 98, 62))
    # the ship: gunmetal hull, cyan running lights, copper fins
    d.polygon([(90, 150), (250, 128), (300, 140), (250, 160), (110, 168)], fill=(90, 96, 104))
    d.polygon([(250, 128), (300, 140), (250, 160)], fill=(36, 40, 46))
    d.polygon([(130, 160), (170, 190), (190, 162)], fill=(140, 92, 62))
    d.polygon([(110, 150), (80, 128), (125, 146)], fill=(140, 92, 62))
    for x in (140, 170, 200, 230):
        d.ellipse([x - 4, 144, x + 4, 152], fill=(56, 200, 255))
    d.rectangle([16, 16, w - 16, 16 + ph], outline=(40, 42, 46), width=3)
    # the month: seven ticks, the seventh filled
    for k in range(7):
        x = w // 2 + (k - 3) * 22
        d.ellipse([x - 6, 16 + ph + 20, x + 6, 16 + ph + 32], outline=(36, 40, 46), width=2, fill=(36, 40, 46) if k == 6 else None)
    # the grid: 5 rows of 7, days 1 to 30
    gx0, gy0, cw, chh = 24, 370, (w - 48) / 7, 38
    small = D.font(18, bold=False)
    for k in range(35):
        r, c = divmod(k, 7)
        x0, y0 = gx0 + c * cw, gy0 + r * chh
        d.rectangle([x0, y0, x0 + cw, y0 + chh], outline=(160, 154, 140), width=1)
        day = k - 1
        if 1 <= day <= 30:
            d.text((x0 + 6, y0 + 4), str(day), fill=(54, 56, 60), font=small)
            if day == 20:
                d.ellipse([x0 + 2, y0 + 1, x0 + cw - 2, y0 + chh - 1], outline=(200, 66, 58), width=4)
            if day in (6, 7, 13, 14, 27, 28):
                d.rectangle([x0 + cw - 12, y0 + chh - 12, x0 + cw - 4, y0 + chh - 4], fill=(56, 200, 255))
    return im


def calendar(m):
    """2 x 3 x 1: the crew room calendar from the SV-04, a hull-dark backing board on a nail, a gunmetal header with
    copper binding rings and a printed page (the ship over the hills, the month's grid with the 20th ringed)."""
    g = Geo()
    hx, hy, hz = 0.25, 0.125, 0.375
    back = hy - 0.004
    box(g, (0, back, -0.02), (0.205, 0.004, 0.33), "hull", bevel=0.003)
    # the stack of pages under the top one, edges showing
    for k in range(4):
        box(g, (0, back - 0.006 - 0.0015 * k, -0.04 - 0.002 * k), (0.19 - 0.002 * k, 0.0008, 0.295 - 0.002 * k), "paper")
    page_y = back - 0.0135
    D.decal(g, (0, page_y, -0.035), (1, 0, 0), (0, 0, 1), 0.19, 0.285, "calendar")
    # header bar and binding
    box(g, (0, back - 0.012, 0.285), (0.205, 0.012, 0.03), "gun", bevel=0.005)
    for k in range(-3, 4):
        x = k * 0.05
        p = Vector((x, back - 0.022, 0.255))
        pts = [p + Vector((0, 0.012 * math.cos(a), 0.012 * math.sin(a))) for a in [i * math.pi / 8 for i in range(-4, 13)]]
        sweep(g, pts, 0.0025, "copper", segs=6)
    for sx in (-1, 1):
        D.bolts(g, [(sx * 0.18, back - 0.0245, 0.285)], (0, -1, 0), r=0.006, mat="copper")
    D.pixel_text(g, "SV-04", (0.0, back - 0.0245, 0.29), (1, 0, 0), (0, 0, 1), 0.006, "bone")
    # the nail it hangs on, and its cord
    cylinder(g, (0, hy, hz - 0.015), (0, back - 0.02, hz - 0.015), 0.004, "zinc", segs=8)
    for sx in (-1, 1):
        cylinder(g, (sx * 0.12, back - 0.004, 0.31), (0, back - 0.004, hz - 0.017), 0.0018, "graphite", segs=5)
    return g


def cubicle_wall(m):
    """8 x 8 x 1: an office partition, a bone fabric panel in steel-blue rails with a graphite cap rail and a glass
    strip along the top, on two graphite stabiliser feet, a pinned note on its face."""
    g = Geo()
    hx, hy, hz = 1.0, 0.125, 1.0
    t = 0.03
    rail = 0.045
    floor = -hz
    z_glass = hz - 0.26
    # the fabric panel and its frame, standing on a kick rail
    D.panel(g, (0, 0, (floor + 0.07 + z_glass) / 2), (hx - 0.002, t, (z_glass - floor - 0.07) / 2), 1, rail, "steel", "fabric_bone",
            bevel=0.006, inset=0.006, corner_mat="graphite")
    box(g, (0, 0, floor + 0.035), (hx - 0.03, t - 0.004, 0.035), "graphite", bevel=0.005)
    # the glass strip and its frame, the cap rail
    box(g, (0, 0, (z_glass + hz - 0.03) / 2), (hx - rail, 0.005, (hz - 0.03 - z_glass) / 2), "glass")
    for sx in (-1, 1):
        box(g, (sx * (hx - rail / 2), 0, (z_glass + hz) / 2), (rail / 2, t, (hz - z_glass) / 2), "steel", bevel=0.006)
    box(g, (0, 0, hz - 0.02), (hx, t + 0.006, 0.02), "graphite", bevel=0.008)
    # glazing beads round the glass
    for z in (z_glass + 0.006, hz - 0.046):
        for sy in (-1, 1):
            box(g, (0, sy * 0.009, z), (hx - rail, 0.004, 0.006), "steel_dark", bevel=0.002)
    # stabiliser feet
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.18), 0, floor + 0.012), (0.03, hy - 0.004, 0.012), "graphite", bevel=0.006)
        box(g, (sx * (hx - 0.18), 0, floor + 0.04), (0.02, t + 0.02, 0.02), "graphite", bevel=0.005)
    # a pinned note and a mustard tab on the face
    box(g, (-0.35, -t - 0.001, 0.25), (0.07, 0.0008, 0.09), "paper")
    for x in (-0.31, -0.39):
        box(g, (x, -t - 0.0018, 0.25 + 0.04 - (0.025 if x < -0.35 else 0.06)), (0.03, 0.0005, 0.003), "faded_dark")
    dome(g, (-0.35, -t - 0.002, 0.325), (0, -1, 0), 0.008, "signal", segs=10)
    box(g, (0.55, -t - 0.0012, 0.38), (0.09, 0.001, 0.035), "mustard", bevel=0.001)
    return g


def mattress(m):
    """4 x 1 x 8: a settler's mattress in faded sea-teal ticking, buttoned, with corded piping, carry loops and a
    mustard patch sewn over a tear."""
    g = Geo()
    hx, hy, hz = 0.5, 1.0, 0.125
    t = hz - 0.008
    box(g, (0, 0, -0.004), (hx - 0.012, hy - 0.012, t), "ticking", bevel=0.045)
    # piping round the top and bottom edges
    for z in (t - 0.02, -t + 0.012):
        pts = []
        for k in range(73):
            a = 2 * math.pi * k / 72
            c, s = math.cos(a), math.sin(a)
            e = 2 / 6.0
            pts.append(Vector(((hx - 0.016) * math.copysign(abs(c) ** e, c), (hy - 0.016) * math.copysign(abs(s) ** e, s), z)))
        sweep(g, pts, 0.007, "cotton_sea", segs=6)
    # buttons in a grid on top
    for i in range(3):
        for j in range(7):
            x, y = (i - 1) * 0.3, (j - 3) * 0.26
            dome(g, (x, y, t - 0.017), (0, 0, 1), 0.012, "cotton_sea", segs=8)
    # a patch and its stitches
    box(g, (0.18, 0.42, t - 0.016), (0.09, 0.07, 0.003), "cotton_mustard", bevel=0.002)
    for k in range(9):
        x = 0.1 + k * 0.02
        box(g, (x, 0.42 - 0.072, t - 0.012), (0.004, 0.006, 0.0015), "faded_dark")
        box(g, (x, 0.42 + 0.072, t - 0.012), (0.004, 0.006, 0.0015), "faded_dark")
    # carry loops on the long sides
    for sx in (-1, 1):
        for y in (-0.35, 0.35):
            box(g, (sx * (hx - 0.006), y, 0.0), (0.006, 0.05, 0.014), "cotton_sea", bevel=0.004)
    return g


def mug(m):
    """1 x 1 x 1: a colony mess-hall mug in steel-blue enamel with a bone rim and handle, a faded CW stencil, coffee."""
    g = Geo()
    hz = 0.125
    z0, z1, r = -hz, hz - 0.035, 0.072
    prof = [(0.0, z0), (r - 0.012, z0), (r - 0.002, z0 + 0.006), (r, z0 + 0.02), (r + 0.002, z1 - 0.012), (r, z1 - 0.006),
            (r - 0.008, z1 - 0.006), (r - 0.009, z0 + 0.02), (0.0, z0 + 0.02)]
    lathe(g, prof, "enamel_steel", steps=40)
    lathe(g, [(r - 0.009, z1 - 0.012), (r + 0.0005, z1 - 0.0105), (r + 0.0015, z1 - 0.003), (r - 0.002, z1), (r - 0.008, z1 - 0.002),
              (r - 0.01, z1 - 0.008)], "bone", steps=40)
    cylinder(g, (0, 0, z1 - 0.03), (0, 0, z1 - 0.026), r - 0.009, "coffee", segs=36)
    cylinder(g, (0, 0, z0 + 0.0002), (0, 0, z0 + 0.003), r - 0.01, "bone", segs=36)
    # the handle on the right
    pts = [Vector((r - 0.004 + 0.042 * math.sin(a), 0, (z0 + z1) / 2 + 0.05 * math.cos(a))) for a in [k * math.pi / 14 for k in range(15)]]
    sweep(g, pts, 0.01, "bone", segs=10)
    fm_stencil(g, "CW", -math.pi / 2, (z0 + z1) / 2 + 0.01, r + 0.0015, 0.0075, "bone", seed=3)
    return g


def fm_stencil(g, text, angle, z, radius, px, mat, seed=0):
    D.fm.stencil_round(g, text, angle, z, radius, px, mat, keep=0.82, seed=seed)


def office_chair_base(m):
    """3 x 1 x 3: an office chair's five-star base, graphite legs on swivel casters, a chrome gas column stub and a
    steel-blue mounting plate on top for the seat."""
    g = Geo()
    hz = 0.125
    floor = -hz
    hub_z = floor + 0.07
    cylinder(g, (0, 0, hub_z - 0.03), (0, 0, hub_z + 0.025), 0.055, "plastic_dark", segs=24)
    for k in range(5):
        a = 2 * math.pi * k / 5 + math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        rot = Matrix((d, side, (0, 0, 1))).transposed()
        L = 0.3
        box(g, tuple(d * (L / 2 + 0.03) + Vector((0, 0, hub_z - 0.004))), (L / 2, 0.022, 0.018), "plastic_dark", bevel=0.008, rot=rot)
        box(g, tuple(d * (L / 2 + 0.03) + Vector((0, 0, hub_z + 0.012))), (L / 2 - 0.02, 0.008, 0.004), "graphite", bevel=0.002, rot=rot)
        tip = d * (L + 0.02)
        cylinder(g, tip + Vector((0, 0, hub_z - 0.03)), tip + Vector((0, 0, hub_z + 0.012)), 0.02, "plastic_dark", segs=12)
        # the caster: a swivel stem, a hood and a twin wheel
        c = tip + d * 0.012 + Vector((0, 0, floor + 0.026))
        cylinder(g, tip + Vector((0, 0, hub_z - 0.03)), tip + Vector((0, 0, floor + 0.05)), 0.006, "chrome", segs=8)
        box(g, tuple(c + Vector((0, 0, 0.012))), (0.02, 0.026, 0.016), "plastic_dark", bevel=0.006, rot=rot)
        for s in (-1, 1):
            cylinder(g, c + side * s * 0.008, c + side * s * 0.022, 0.026, "rubber", segs=14)
    # the gas column and the mounting plate
    cylinder(g, (0, 0, hub_z + 0.025), (0, 0, hz - 0.02), 0.026, "chrome", segs=20)
    cylinder(g, (0, 0, hub_z + 0.025), (0, 0, hub_z + 0.04), 0.04, "plastic_dark", segs=20)
    box(g, (0, 0, hz - 0.016), (0.09, 0.09, 0.01), "steel", bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            D.bolts(g, [(sx * 0.068, sy * 0.068, hz - 0.006)], (0, 0, 1), r=0.007)
    return g


def materials_13(m):
    m["occupied"] = D.image_mat("dk_occupied", occupied_image(), rough=0.4)
    m["calendar"] = D.image_mat("dk_calendar", calendar_image(), rough=0.85)


BATCH_13A = {
    "shelf_unit": Part((4, 8, 2), shelf_unit, "Colony rack: perforated steel-blue posts, four bone shelves with label cards, back cross rods."),
    "metal_table": Part((6, 3, 4), metal_table, "Colony workbench: bone top in a graphite band, steel-blue frame, lower shelf, drawer."),
    "stall_door": Part((4, 7, 1), stall_door, "Colony washroom stall door: steel-blue panel, chrome barrel hinges, latch with a red indicator, coat hook."),
    "calendar": Part((2, 3, 1), calendar, "SV-04 crew room calendar (Salvager Tech): ship print, the month with the 20th ringed, copper binding."),
    "cubicle_wall": Part((8, 8, 1), cubicle_wall, "Colony partition: bone fabric in steel-blue rails, glass strip, graphite cap and feet, a pinned note."),
    "mattress": Part((4, 1, 8), mattress, "Settler mattress: faded sea-teal ticking, buttons, corded piping, carry loops, a mustard patch."),
    "mug": Part((1, 1, 1), mug, "Colony mess mug: steel-blue enamel, bone rim and handle, worn CW stencil, coffee."),
    "office_chair_base": Part((3, 1, 3), office_chair_base, "Colony office chair base: five graphite legs on casters, chrome column, seat plate."),
}


# =====================================================================================================
# Batch 13b
# =====================================================================================================


def office_table(m):
    """8 x 1 x 4: a Colony office table top, bone laminate in a graphite edge band on a steel-blue underframe, with
    mounting plates under the corners for Table Supports (or anything else) to hold it up."""
    g = Geo()
    hx, hy, hz = 1.0, 0.5, 0.125
    top = hz - 0.025
    box(g, (0, 0, top), (hx - 0.014, hy - 0.014, 0.024), "bone", bevel=0.004)
    for sy in (-1, 1):
        box(g, (0, sy * (hy - 0.012), top - 0.002), (hx, 0.012, 0.027), "graphite", bevel=0.005)
    for sx in (-1, 1):
        box(g, (sx * (hx - 0.012), 0, top - 0.002), (0.012, hy - 0.024, 0.027), "graphite", bevel=0.005)
    rz = top - 0.06
    for sy in (-1, 1):
        box(g, (0, sy * (hy - 0.12), rz), (hx - 0.12, 0.018, 0.035), "steel", bevel=0.004)
    for x in (-hx + 0.12, 0.0, hx - 0.12):
        box(g, (x, 0, rz), (0.018, hy - 0.12, 0.035), "steel", bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = (sx * (hx - 0.12), sy * (hy - 0.12), -hz + 0.017)
            box(g, (p[0], p[1], rz - 0.02), (0.035, 0.035, 0.058), "steel_dark", bevel=0.004)
            box(g, p, (0.06, 0.06, 0.012), "graphite", bevel=0.004)
            for kx in (-1, 1):
                for ky in (-1, 1):
                    dome(g, (p[0] + kx * 0.042, p[1] + ky * 0.042, -hz + 0.005), (0, 0, -1), 0.007, "bolt")
    # a cable grommet near the back edge
    cylinder(g, (0.55, hy - 0.12, top + 0.02), (0.55, hy - 0.12, top + 0.0245), 0.03, "plastic_dark", segs=20)
    cylinder(g, (0.55, hy - 0.12, top + 0.0246), (0.55, hy - 0.12, top + 0.0248), 0.02, "hole", segs=16)
    return g


def pillow(m):
    """3 x 1 x 2: a plump settler pillow in a striped cotton case with a darned mustard patch."""
    g = Geo()
    hx, hy, hz = 0.375, 0.25, 0.125
    secs = []
    for k in range(13):
        t = -1 + 2 * k / 12
        bulge = 1 - 0.55 * t ** 4
        secs.append((t * (hy - 0.02), 0.0, (hx - 0.03) * (1 - 0.12 * t ** 6), -hz * bulge * 0.92 + 0.0, hz * bulge * 0.92))
    D.squircle_loft(g, secs, "ticking_plain", ring_pts=32, power=3.2)
    box(g, (-0.12, -0.05, hz * 0.9 - 0.004), (0.05, 0.04, 0.003), "cotton_mustard", bevel=0.002, rot=Matrix.Rotation(0.2, 3, "Z"))
    return g


def shelf_frame(g, height, hz):
    """A 1-block shelf support frame: four perforated steel-blue posts and a graphite plate top and bottom."""
    h = 0.125
    for sx in (-1, 1):
        for sy in (-1, 1):
            D.perforated(g, (sx * (h - 0.02), sy * (h - 0.02), 0.0), (0.016, 0.016, hz - 0.014), "steel",
                         holes=max(2, round(height / 0.06)), face=(sx, 0, 0), hole_half=(0.005, 0.01))
    for z in (-hz + 0.007, hz - 0.007):
        D.panel(g, (0, 0, z), (h - 0.002, h - 0.002, 0.007), 2, 0.022, "graphite", "steel_dark", bevel=0.003)


def shelf_support(m):
    """1 x 1 x 1: a cube of four perforated posts between graphite plates, the smallest shelf piece."""
    g = Geo()
    shelf_frame(g, 0.25, 0.125)
    return g


def shelf_support_tall(m):
    """1 x 4 x 1: the shelf support frame a metre tall, with a mid brace."""
    g = Geo()
    shelf_frame(g, 1.0, 0.5)
    for sx in (-1, 1):
        box(g, (sx * 0.105, 0, 0), (0.008, 0.09, 0.012), "steel_dark", bevel=0.003)
        box(g, (0, sx * 0.105, 0), (0.09, 0.008, 0.012), "steel_dark", bevel=0.003)
    return g


def shelf_pillar(m):
    """1 x 3 x 1: a single perforated steel-blue upright on graphite end plates, for tall shelving."""
    g = Geo()
    hz = 0.375
    D.perforated(g, (0, 0, 0), (0.04, 0.04, hz - 0.016), "steel", holes=10, face=(0, -1, 0))
    for sx in (-1, 1):
        D.perforated(g, (sx * 0.0405, 0, 0), (0.0005, 0.03, hz - 0.03), "steel", holes=10, face=(sx, 0, 0), hole_half=(0.004, 0.012))
    for z in (-hz + 0.013, hz - 0.013):
        box(g, (0, 0, z), (0.12, 0.12, 0.008), "graphite", bevel=0.004)
        for sx in (-1, 1):
            for sy in (-1, 1):
                dome(g, (sx * 0.09, sy * 0.09, z + math.copysign(0.008, z)), (0, 0, math.copysign(1, z)), 0.007, "bolt")
    return g


def sink(m):
    """3 x 3 x 2: a wall-hung Colony washbasin, glazed white on a steel-blue bracket, a chrome tap with two heads, a
    mustard soap bar, the trap below and a splashback plate on the wall side."""
    g = Geo()
    hx, hy, hz = 0.375, 0.25, 0.375
    back = hy
    top = 0.12
    # the splashback and wall bracket
    box(g, (0, back - 0.01, top + 0.06), (0.3, 0.01, 0.16), "steel", bevel=0.005)
    box(g, (0, back - 0.012, -0.1), (0.07, 0.012, 0.14), "steel_dark", bevel=0.005)
    # the basin: a rounded bowl, rolled rim, its inside hollow
    prof = [(0.0, top - 0.17), (0.7, top - 0.17), (0.92, top - 0.1), (0.99, top - 0.02), (1.0, top), (0.93, top),
            (0.88, top - 0.03), (0.62, top - 0.115), (0.0, top - 0.12)]
    D.basin(g, 0.3, 0.2, [(k, z, "porcelain") for (k, z) in prof], power=3.2, steps=40, center=(0, back - 0.22))
    cylinder(g, (0, back - 0.24, top - 0.1195), (0, back - 0.24, top - 0.118), 0.022, "chrome", segs=16)
    cylinder(g, (0, back - 0.24, top - 0.1178), (0, back - 0.24, top - 0.1176), 0.014, "hole", segs=12)
    # the tap on the deck at the back, two cross heads
    deck = Vector((0, back - 0.05, top))
    cylinder(g, deck, deck + Vector((0, 0, 0.09)), 0.016, "chrome", segs=14)
    cylinder(g, deck + Vector((0, 0, 0.085)), deck + Vector((0, -0.11, 0.085)), 0.011, "chrome", segs=12)
    cylinder(g, deck + Vector((0, -0.11, 0.085)), deck + Vector((0, -0.118, 0.06)), 0.012, "chrome", segs=12)
    for sx in (-1, 1):
        h = deck + Vector((sx * 0.1, 0, 0))
        cylinder(g, h, h + Vector((0, 0, 0.04)), 0.012, "chrome", segs=12)
        for k in range(2):
            a = k * math.pi / 2 + math.pi / 4
            d = Vector((math.cos(a), math.sin(a), 0)) * 0.028
            cylinder(g, h + Vector((0, 0, 0.045)) - d, h + Vector((0, 0, 0.045)) + d, 0.005, "chrome", segs=6)
        dome(g, h + Vector((0, 0, 0.045)), (0, 0, 1), 0.01, "signal" if sx < 0 else "steel", segs=8)
    # a soap bar on the rim
    box(g, (0.23, back - 0.2, top + 0.012), (0.03, 0.045, 0.012), "mustard", bevel=0.008)
    # the trap: down from the drain, a U and back to the wall
    tr = [Vector((0, back - 0.24, top - 0.17)), Vector((0, back - 0.24, -0.12)), Vector((0, back - 0.22, -0.19)),
          Vector((0, back - 0.15, -0.2)), Vector((0, back - 0.1, -0.16)), Vector((0, back - 0.06, -0.15)), Vector((0, back - 0.005, -0.15))]
    sweep(g, D.bend(tr, 0.03), 0.02, "chrome", segs=12)
    cylinder(g, (0, back - 0.004, -0.15), (0, back - 0.02, -0.15), 0.032, "chrome", segs=16)
    return g


def table_support(m):
    """1 x 3 x 1: a table pedestal, a graphite octagonal column on a flared foot with a steel-blue top plate."""
    g = Geo()
    hz = 0.375
    lathe(g, [(0.0, -hz), (0.11, -hz), (0.11, -hz + 0.02), (0.07, -hz + 0.06), (0.045, -hz + 0.09), (0.045, hz - 0.08),
              (0.07, hz - 0.04), (0.0, hz - 0.04)], "graphite", steps=8)
    for z in (-hz + 0.11, hz - 0.1):
        ring_c(g, z, 0.05, 0.012, "steel_dark")
    box(g, (0, 0, hz - 0.02), (0.11, 0.11, 0.02), "steel", bevel=0.005)
    for sx in (-1, 1):
        for sy in (-1, 1):
            dome(g, (sx * 0.08, sy * 0.08, hz - 0.006), (0, 0, 1), 0.008, "bolt")
    return g


def ring_c(g, z, r, h, mat):
    lathe(g, [(r - 0.004, z - h), (r + 0.004, z - h), (r + 0.006, z), (r + 0.004, z + h), (r - 0.004, z + h)], mat, steps=8)


def toilet_paper(m):
    """1 x 1 x 1: a roll of toilet paper on its card core, the end sheet hanging loose (two stacked make a towel roll)."""
    g = Geo()
    hz = 0.125
    r, rc = 0.105, 0.03
    lathe(g, [(rc, -hz + 0.004), (r - 0.006, -hz + 0.004), (r, -hz + 0.01), (r, hz - 0.01), (r - 0.006, hz - 0.004), (rc, hz - 0.004)],
          "cotton", steps=40)
    lathe(g, [(rc - 0.004, -hz), (rc, -hz), (rc, hz), (rc - 0.004, hz)], "card", steps=24)
    # the loose end, peeling off the front
    for i in range(10):
        a0, a1 = -math.pi / 2 - 0.6 + 0.12 * i, -math.pi / 2 - 0.6 + 0.12 * (i + 1)
        r0, r1 = r + 0.0015 + 0.0016 * i * i / 9, r + 0.0015 + 0.0016 * (i + 1) ** 2 / 9
        g.add([(r0 * math.cos(a0), r0 * math.sin(a0), -hz + 0.012), (r1 * math.cos(a1), r1 * math.sin(a1), -hz + 0.012),
               (r1 * math.cos(a1), r1 * math.sin(a1), hz - 0.012), (r0 * math.cos(a0), r0 * math.sin(a0), hz - 0.012)], [(0, 1, 2, 3)], "cotton")
    return g


BATCH_13B = {
    "office_table": Part((8, 1, 4), office_table, "Colony office table top: bone laminate in a graphite band, steel-blue underframe, corner mounting plates."),
    "pillow": Part((3, 1, 2), pillow, "Settler pillow: plump, in a striped cotton case with a darned mustard patch."),
    "shelf_pillar": Part((1, 3, 1), shelf_pillar, "Colony shelf upright: perforated steel-blue post on bolted graphite end plates."),
    "shelf_support": Part((1, 1, 1), shelf_support, "Colony shelf support: four perforated posts between graphite plates."),
    "sink": Part((3, 3, 2), sink, "Colony washbasin: white bowl on a steel-blue bracket, chrome tap, mustard soap, trap to the wall."),
    "table_support": Part((1, 3, 1), table_support, "Colony table pedestal: graphite octagonal column, flared foot, steel-blue top plate."),
    "shelf_support_tall": Part((1, 4, 1), shelf_support_tall, "Colony tall shelf support: the support frame a metre tall with a mid brace."),
    "toilet_paper": Part((1, 1, 1), toilet_paper, "Settler toilet roll on its card core, the end sheet hanging loose."),
}
