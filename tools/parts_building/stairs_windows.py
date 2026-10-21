"""Models of the parts map's batch 11: stairs, railings, the Net Fence, windows, windshields and the Concave Wedge.

Colony Works staircase kit (steel-blue frames, mustard handrails and nosings, graphite bolts and grating), Colony house
windows (steel-blue frames with corner blocks round clear glass), Salvager Tech vehicle glass (gunmetal frames,
rubber seals, tinted glass). Each builder takes the materials and returns the part's root (tools/part_kit/building_kit.py).
"""

from __future__ import annotations

import math

from mathutils import Vector

import building_kit as B
import part_kit as K
from part_kit import Geo, box, cylinder

# =====================================================================================================
# Stairs and railings (Colony Works)
# =====================================================================================================


def ramp_block(mats):
    """Ramp Block, 4 x 4 x 4: a 45 degree steel ramp, low at the front. Steel-blue side stringers, a grated deck with
    graphite cleats, a mustard nosing on the low edge, a bone back panel."""
    g = Geo()
    h = 0.5
    for s in (-1, 1):
        # the stringer: a steel-blue plate with a rail round a bone panel on its outer face, bolted along the slope
        x0, x1 = sorted((s * (h - 0.012), s * (h - 0.04)))
        B.prism_x(g, [(h, -h), (-h, -h), (-h, h)], x0, x1, "steel_blue")
        xa, xb = sorted((s * (h - 0.012), s * (h - 0.004)))
        B.prism_x(g, [(h - 0.16, -h + 0.05), (-h + 0.05, -h + 0.05), (-h + 0.05, h - 0.16)], xa, xb, "bone")
        for k in range(1, 6):
            t = k / 6
            B.bolt(g, (s * (h - 0.012), h - t - 0.03, -h + t - 0.03), (s, 0, 0), 0.007)
    # the deck plate a little under the slope, then cleats across it
    n = Vector((0, 1, 1)).normalized()
    along = Vector((0, -1, 1)).normalized()
    foot = Vector((0, h, -h))
    deck_len = math.sqrt(2) * 2 * h
    B.bar(g, foot - n * 0.009 + along * 0.03, foot - n * 0.009 + along * (deck_len - 0.03), h - 0.04, 0.007, "grate", 0.002)
    for k in range(1, 13):
        p = foot + along * (deck_len * k / 13) - n * 0.0005
        B.bar(g, p + Vector((-(h - 0.045), 0, 0)), p + Vector((h - 0.045, 0, 0)), 0.008, 0.006, "graphite", 0.002,
              up=tuple(n))
    nose = foot + along * 0.035 - n * 0.002
    B.bar(g, nose + Vector((-(h - 0.03), 0, 0)), nose + Vector((h - 0.03, 0, 0)), 0.025, 0.006, "mustard", 0.002,
          up=tuple(n))
    # the back: a bone panel in a steel-blue rail, and base rails
    box(g, (0, -h + 0.012, 0.0), (h - 0.03, 0.01, h - 0.04), "bone", bevel=0.004)
    box(g, (0, -h + 0.015, h - 0.025), (h - 0.03, 0.015, 0.025), "steel_blue", bevel=0.004)
    for y in (-h + 0.03, h - 0.06):
        box(g, (0, y, -h + 0.02), (h - 0.03, 0.02, 0.02), "graphite", bevel=0.004)
    return B.one("RampBlock", g, mats)


def railing(g, length):
    """A Colony handrail section `length` long (x) and 1 m tall: posts every metre or less, a mustard handrail, a
    steel-blue mid rail and toe board."""
    h = 0.5
    ends = length / 2 - 0.05
    n_posts = max(2, int(math.ceil(length / 1.0)) + 1)
    for k in range(n_posts):
        x = -ends + 2 * ends * k / (n_posts - 1)
        B.post(g, x, 0, -h, h - 0.04, half=0.022, foot_half=0.045)
        box(g, (x, 0, h - 0.04), (0.018, 0.026, 0.012), "graphite", bevel=0.003)       # the rail saddle
    rail_z = h - 0.024
    cylinder(g, (-length / 2 + 0.004, 0, rail_z), (length / 2 - 0.004, 0, rail_z), 0.022, "mustard", segs=16)
    for s in (-1, 1):
        cylinder(g, (s * (length / 2 - 0.004), 0, rail_z), (s * (length / 2), 0, rail_z), 0.019, "mustard", segs=16)
    box(g, (0, 0, 0.02), (ends, 0.012, 0.016), "steel_blue", bevel=0.004)
    box(g, (0, 0, -h + 0.07), (ends, 0.005, 0.045), "steel_blue", bevel=0.002)


def metal_railing(mats):
    g = Geo()
    railing(g, 1.0)
    return B.one("MetalRailing", g, mats)


def metal_railing_long(mats):
    g = Geo()
    railing(g, 2.0)
    return B.one("MetalRailingLong", g, mats)


def railing_join(mats):
    """Railing Join, 1 x 1 x 1: a steel-blue knuckle block with mustard collar sockets on its four sides, bolted."""
    g = Geo()
    box(g, (0, 0, 0), (0.09, 0.09, 0.09), "steel_blue", bevel=0.012)
    for n in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)):
        n = Vector(n)
        cylinder(g, n * 0.088, n * 0.125, 0.03, "mustard", segs=16)
        cylinder(g, n * 0.12, n * 0.1251, 0.018, "hole", segs=12)
    for sx in (-1, 1):
        for sy in (-1, 1):
            B.bolt(g, (sx * 0.055, sy * 0.055, 0.09), r=0.008)
    box(g, (0, 0, -0.105), (0.1, 0.1, 0.02), "graphite", bevel=0.005)
    return B.one("RailingJoin", g, mats)


def stair_baluster(mats):
    """Stair Baluster, 1 x 4 x 1: the post under a horizontal railing, a mustard saddle on top for the rail."""
    g = Geo()
    B.post(g, 0, 0, -0.5, 0.45, half=0.026, foot_half=0.06)
    box(g, (0, 0, 0.46), (0.05, 0.034, 0.012), "graphite", bevel=0.003)
    for s in (-1, 1):
        box(g, (s * 0.04, 0, 0.485), (0.008, 0.034, 0.016), "mustard", bevel=0.003)
    for z in (-0.2, 0.15):
        box(g, (0, 0, z), (0.03, 0.03, 0.01), "graphite", bevel=0.003)
    return B.one("StairBaluster", g, mats)


def stair_banister(mats):
    """Stair Banister, 1 x 4 x 1: the post under a stair's sloping handrail: a short post, an angled graphite bracket
    and a 45 degree mustard rail stub along the stair (y)."""
    g = Geo()
    B.post(g, 0, 0, -0.5, 0.26, half=0.026, foot_half=0.06)
    a, b = Vector((0, 0.095, 0.25)), Vector((0, -0.095, 0.44))
    mid = (a + b) / 2
    B.bar(g, (0, 0, 0.25), mid - Vector((0, 0, 0.02)), 0.014, 0.014, "graphite", 0.003, up=(0, 1, 0))
    cylinder(g, a, b, 0.022, "mustard", segs=16)
    box(g, tuple(mid - Vector((0, 0, 0.02))), (0.02, 0.03, 0.012), "graphite", bevel=0.003)
    return B.one("StairBanister", g, mats)


def stair_step(mats):
    """Stair Step, 6 x 1 x 1: a grated tread in a steel-blue channel frame, a mustard nosing on its front edge and
    end brackets that bolt to a stringer."""
    g = Geo()
    hx, hy, hz = 0.75, 0.125, 0.125
    for s in (-1, 1):
        box(g, (s * (hx - 0.008), 0, 0), (0.008, hy - 0.002, hz - 0.002), "steel_blue", bevel=0.003)
        for y in (-0.06, 0.06):
            B.bolt(g, (s * (hx - 0.009), y, -0.03), (s, 0, 0), 0.007)
    for y in (-hy + 0.012, hy - 0.012):
        box(g, (0, y, hz - 0.03), (hx - 0.016, 0.012, 0.03), "steel_blue", bevel=0.003)
    B.grating(g, -hx + 0.02, hx - 0.02, -hy + 0.024, hy - 0.024, hz - 0.004, pitch=0.04, cross=0.06)
    box(g, (0, hy - 0.01, hz - 0.004), (hx - 0.016, 0.012, 0.005), "mustard", bevel=0.002)
    box(g, (0, -0.02, -hz + 0.02), (hx - 0.016, 0.01, 0.015), "graphite", bevel=0.003)
    return B.one("StairStep", g, mats)


def stair_landing(mats):
    """Stair Landing, 8 x 1 x 8: a 2 x 2 m grated platform in a steel-blue frame, graphite joists under it, corner
    blocks and a mustard edge on the front."""
    g = Geo()
    h, hz = 1.0, 0.125
    for s in (-1, 1):
        box(g, (0, s * (h - 0.02), 0), (h, 0.02, hz - 0.002), "steel_blue", bevel=0.004)
        box(g, (s * (h - 0.02), 0, 0), (0.02, h - 0.04, hz - 0.002), "steel_blue", bevel=0.004)
    for k in range(1, 4):
        y = -h + 2 * h * k / 4
        box(g, (0, y, -0.02), (h - 0.04, 0.012, hz - 0.03), "graphite", bevel=0.003)
    B.grating(g, -h + 0.04, h - 0.04, -h + 0.04, h - 0.04, hz - 0.004, pitch=0.05, cross=0.25)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (h - 0.03), sy * (h - 0.03), 0), (0.032, 0.032, hz), "graphite", bevel=0.005)
            B.bolt(g, (sx * (h - 0.03), sy * (h - 0.03), hz - 0.009), r=0.01)
    box(g, (0, h - 0.02, hz - 0.004), (h - 0.035, 0.022, 0.0045), "mustard", bevel=0.002)
    return B.one("StairLanding", g, mats)


def net_fence(mats):
    """Net Fence, 7 x 4 x 1: galvanised chain-link between two steel-blue posts, top and bottom rails, mustard bands."""
    g = Geo()
    hx, hz = 0.875, 0.5
    for s in (-1, 1):
        B.post(g, s * (hx - 0.045), 0, -hz, hz, half=0.024, foot_half=0.045)
        box(g, (s * (hx - 0.045), 0, 0.3), (0.027, 0.027, 0.03), "mustard", bevel=0.003)
    x0, x1, z0, z1 = -hx + 0.06, hx - 0.06, -hz + 0.08, hz - 0.07
    for z in (z0, z1):
        cylinder(g, (x0 - 0.002, 0, z), (x1 + 0.002, 0, z), 0.012, "galv", segs=10)
    # the diamond mesh: wires on x - z = c and x + z = c, clipped to the panel
    pitch = 0.075
    for sign in (1, -1):
        corners = [x - sign * z for x in (x0, x1) for z in (z0, z1)]
        c, c_end = min(corners) + pitch / 2, max(corners)
        while c < c_end:
            pts = [(c + sign * z, z) for z in (z0, z1) if x0 <= c + sign * z <= x1]
            pts += [(x, (x - c) * sign) for x in (x0, x1) if z0 <= (x - c) * sign <= z1]
            pts = sorted(set((round(px, 6), round(pz, 6)) for px, pz in pts))
            if len(pts) >= 2 and math.dist(pts[0], pts[-1]) > 0.01:
                (ax, az), (bx, bz) = pts[0], pts[-1]
                B.bar(g, (ax, 0, az), (bx, 0, bz), 0.0016, 0.0016, "galv", 0, up=(0, 1, 0))
            c += pitch
    return B.one("NetFence", g, mats)


# =====================================================================================================
# Windows (Colony Works) and vehicle glass (Salvager Tech)
# =====================================================================================================


def colony_window(g, hw, hh, mullions=0, rail=0.06):
    """A Colony window: a steel-blue frame with graphite corner blocks round a clear pane, a bone sill under it."""
    B.frame(g, hw, hh, -0.055, 0.055, rail, "steel_blue")
    B.gasket(g, hw, hh, 0.0, 0.006, "rubber")
    B.pane(g, hw, hh, 0.0)
    for k in range(1, mullions + 1):
        x = -hw + 2 * hw * k / (mullions + 1)
        box(g, (x, 0, 0), (0.018, 0.035, hh), "steel_blue", bevel=0.003)
    box(g, (0, 0.085, -hh - rail * 0.6), (hw + rail * 0.6, 0.032, 0.012), "bone", bevel=0.003)


def window_square(mats):
    g = Geo()
    colony_window(g, 0.44, 0.44)
    return B.one("WindowSquare", g, mats)


def window_large(mats):
    g = Geo()
    colony_window(g, 0.94, 0.44, mullions=1)
    return B.one("WindowLarge", g, mats)


def window_metal(mats):
    """Metal Window, 4 x 4 x 1: a Colony frame with fixed graphite louvres over dark glass (a storm shutter)."""
    g = Geo()
    hw = hh = 0.44
    B.frame(g, hw, hh, -0.07, 0.07, 0.06, "steel_blue")
    box(g, (0, -0.045, 0), (hw, 0.004, hh), "glass_dark")
    n = 9
    for k in range(n):
        z = -hh + (2 * hh) * (k + 0.5) / n
        B.bar(g, (-hw, 0.02, z), (hw, 0.02, z), 0.034, 0.004, "graphite", 0.002, up=(0, -0.55, 0.83))
    for x in (-hw / 3, hw / 3):
        box(g, (x, 0.05, 0), (0.012, 0.01, hh), "steel_blue", bevel=0.003)
    return B.one("WindowMetal", g, mats)


def side_window(mats):
    """Side Window, 4 x 3 x 1: Salvager vehicle glass, a gunmetal frame with hull corner blocks and copper screws,
    a rubber seal and tinted glass."""
    g = Geo()
    hw, hh = 0.455, 0.33
    B.frame(g, hw, hh, -0.04, 0.04, 0.045, "gun", corner="hull", screws=False)
    for sx in (-1, 1):
        for sz in (-1, 1):
            K.screw(g, (sx * (hw + 0.0225), 0.044, sz * (hh + 0.0225)), (0, 1, 0), 0.0055, "copper")
    B.gasket(g, hw, hh, 0.0, 0.007, "rubber")
    B.pane(g, hw, hh, 0.0, "glass_tint")
    return B.one("SideWindow", g, mats)


def windshield(g, hx, hy, hz, wrap, divider=False):
    """A Salvager windshield in a hx x hy x hz box: a wrapped, tinted pane leaning back from a gunmetal cowl to a
    header bar, with A-pillars, a rubber seal and copper screws along the cowl."""
    y_bot, z_bot = hy - 0.07, -hz + 0.1
    y_top, z_top = -hy + 0.045 + wrap, hz - 0.04
    xs = [-(hx - 0.05) + 2 * (hx - 0.05) * k / 16 for k in range(17)]

    def at(x, t):
        y = y_bot + (y_top - y_bot) * t - wrap * (x / hx) ** 2
        z = z_bot + (z_top - z_bot) * t
        return (x, y, z)

    verts, faces = [], []
    for x in xs:
        for t in (0.0, 0.5, 1.0):
            verts.append(at(x, t))
    for i in range(len(xs) - 1):
        for j in range(2):
            a = i * 3 + j
            faces.append((a, a + 3, a + 4, a + 1))
    g.add(verts, faces, "glass_tint")
    # seal along the bottom and top edges, pillars at the sides
    seal_b = [at(x, 0.0) for x in xs]
    seal_t = [at(x, 1.0) for x in xs]
    K.sweep(g, seal_b, 0.008, "rubber", segs=8)
    K.sweep(g, seal_t, 0.008, "rubber", segs=8)
    for s in (-1, 1):
        B.bar(g, at(s * (hx - 0.03), -0.02), at(s * (hx - 0.03), 1.02), 0.025, 0.02, "gun", 0.006, up=(0, 1, 1))
    K.sweep(g, [Vector(at(x, 1.0)) + Vector((0, -0.004, 0.012)) for x in [-hx + 0.01] + xs + [hx - 0.01]], 0.022,
            "gun", segs=10)
    # the cowl under the glass: a hull box with a gunmetal top plate and copper screws
    box(g, (0, y_bot - 0.055, -hz + 0.045), (hx, 0.1, 0.045), "hull", bevel=0.008)
    B.bar(g, (-hx + 0.01, y_bot + 0.0, z_bot - 0.01), (hx - 0.01, y_bot + 0.0, z_bot - 0.01), 0.035, 0.012, "gun",
          0.004)
    for k in range(7):
        x = -hx + 0.1 + (2 * hx - 0.2) * k / 6
        K.screw(g, (x, y_bot + 0.02, -hz + 0.09), (0, 0.3, 1), 0.006, "copper")
    if divider:
        B.bar(g, at(0, -0.02), at(0, 1.02), 0.012, 0.012, "gun", 0.003, up=(0, 1, 1))


def windshield_small(mats):
    g = Geo()
    windshield(g, 0.75, 0.25, 0.5, wrap=0.08)
    return B.one("Windshield", g, mats)


def windshield_large(mats):
    g = Geo()
    windshield(g, 1.0, 0.375, 0.625, wrap=0.12, divider=True)
    return B.one("WindshieldLarge", g, mats)


def concave_wedge(mats):
    """Concave Wedge, 1 x 1 x 1: a block whose top is scooped out in a quarter circle, high at the back (as the Ramp
    Block), in Salvager gunmetal."""
    g = Geo()
    h = 0.125
    pts = [(h, -h), (-h, -h), (-h, h)]
    steps = 12
    for k in range(1, steps):
        a = math.pi + (math.pi / 2) * k / steps
        pts.append((h + 2 * h * math.cos(a), h + 2 * h * math.sin(a)))
    B.prism_x(g, pts, -h, h, "gun_plain", center=(-h * 0.85, -h * 0.85))
    return B.one("ConcaveWedge", g, mats)


# id: (builder, half size (m), icon ortho, title, notes)
PARTS = [
    ("ramp_block", ramp_block, (0.5, 0.5, 0.5), 1.8, "Ramp Block (4 x 4 x 4 blocks)",
     ["A 45 degree Colony steel ramp, low at the front: steel-blue stringers, grated deck, cleats, mustard nosing."]),
    ("metal_railing", metal_railing, (0.5, 0.125, 0.5), 1.35, "Metal Railing (4 x 4 x 1 blocks)",
     ["Colony handrail: steel-blue posts on bolted feet, mustard handrail, mid rail and toe board."]),
    ("windshield", windshield_small, (0.75, 0.25, 0.5), 1.8, "Windshield (6 x 4 x 2 blocks)",
     ["Salvager vehicle glass: a wrapped tinted pane on a gunmetal cowl, A-pillars and header, rubber seals."]),
    ("side_window", side_window, (0.5, 0.125, 0.375), 1.3, "Side Window (4 x 3 x 1 blocks)",
     ["Salvager vehicle glass: gunmetal frame, hull corner blocks, copper screws, rubber seal, tinted pane."]),
    ("concave_wedge", concave_wedge, (0.125, 0.125, 0.125), 0.42, "Concave Wedge (1 x 1 x 1 block)",
     ["A one-block wedge with a quarter-circle scoop, high at the back like the Ramp Block. Paintable gunmetal."]),
    ("window_large", window_large, (1.0, 0.125, 0.5), 2.3, "Large Window (8 x 4 x 1 blocks)",
     ["Colony window: steel-blue frame, corner blocks and a mullion round clear glass, a bone sill."]),
    ("windshield_large", windshield_large, (1.0, 0.375, 0.625), 2.4, "Large Windshield (8 x 5 x 3 blocks)",
     ["The Windshield's big brother for cabins: more wrap, a centre divider, the same cowl and pillars."]),
    ("window_metal", window_metal, (0.5, 0.125, 0.5), 1.3, "Metal Window (4 x 4 x 1 blocks)",
     ["A Colony storm window: steel-blue frame, fixed graphite louvres over dark glass."]),
    ("net_fence", net_fence, (0.875, 0.125, 0.5), 2.05, "Net Fence (7 x 4 x 1 blocks)",
     ["Galvanised chain-link between steel-blue posts, top and bottom rails, mustard bands; panels line up end to end."]),
    ("window_square", window_square, (0.5, 0.125, 0.5), 1.3, "Square Window (4 x 4 x 1 blocks)",
     ["Colony window: steel-blue frame with graphite corner blocks round clear glass, a bone sill."]),
    ("stair_baluster", stair_baluster, (0.125, 0.125, 0.5), 1.2, "Stair Baluster (1 x 4 x 1 blocks)",
     ["The post under a level railing: steel-blue post on a bolted foot, a mustard rail saddle on top."]),
    ("stair_banister", stair_banister, (0.125, 0.125, 0.5), 1.2, "Stair Banister (1 x 4 x 1 blocks)",
     ["The post under a stair's sloping rail: a short post, an angled bracket, a 45 degree mustard rail stub."]),
    ("stair_landing", stair_landing, (1.0, 1.0, 0.125), 2.6, "Stair Landing (8 x 1 x 8 blocks)",
     ["A 2 x 2 m grated platform in a steel-blue frame, joists under it, a mustard front edge."]),
    ("metal_railing_long", metal_railing_long, (1.0, 0.125, 0.5), 2.3, "Metal Railing (Long) (8 x 4 x 1 blocks)",
     ["The Metal Railing at 2 m: three posts."]),
    ("railing_join", railing_join, (0.125, 0.125, 0.125), 0.42, "Railing Join (1 x 1 x 1 block)",
     ["A steel-blue knuckle with mustard collar sockets on four sides, where railings meet at a corner."]),
    ("stair_step", stair_step, (0.75, 0.125, 0.125), 1.7, "Stair Step (6 x 1 x 1 blocks)",
     ["A grated tread in a steel-blue channel, a mustard nosing, end brackets bolted to the stringers."]),
]
