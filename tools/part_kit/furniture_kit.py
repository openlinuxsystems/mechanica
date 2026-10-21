"""The parts map's batch 4 furniture seats: the Toilet and the Bathtub. The Toilet is a clean porcelain fixture, a
classic US two-piece toilet in glossy white with chrome fittings; the Bathtub is in the Settler Patchwork look
(docs/art-direction.md): bone enamel, scrap planks, zinc and rust, a graphite clawfoot. Their logic outputs (10 each) go
into a link box (the Toilet: the batch's, on the floor behind it) or a small zinc junction box with brass terminals
(the Bathtub). One level each, no clips (their data has no display readout).

Seat points (Blender: x right, y forward, z up; the part data's ``seat`` uses game axes x, z up, -y):

* the Toilet (3 x 4 x 4 blocks): the feet on the part's bottom (-0.5), the closed lid's top at -0.11 over y -0.05,
  chair height for the ``sit`` clip; the tank behind is the back rest;
* the Bathtub (3 x 3 x 7 blocks, long along y): the feet on the tub's inner floor (-0.235), a plank seat across the
  tub at y -0.43 with its top at -0.065 (a low seat, ``sit_low``, the legs out along the tub); the taps at the far end.
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, dome, sweep


def basin(g, half_w, half_l, profile, power=3.0, steps=48, center=(0, 0), taper=0.0):
    """A vessel turned round a rounded-rectangle plan (half sizes `half_w` x `half_l`, squircle `power`; `taper`
    narrows its front half, +y, toward the front for an egg shape): `profile` is an open run of (k, z, mat) points, k
    the fraction of the plan (0 the middle, 1 the outside); each segment takes the material of its first point."""
    e = 2.0 / power
    ring = []
    for s in range(steps):
        a = 2 * math.pi * s / steps
        c, sn = math.cos(a), math.sin(a)
        y = half_l * math.copysign(abs(sn) ** e, sn)
        ring.append((half_w * math.copysign(abs(c) ** e, c) * (1 - taper * max(y / half_l, 0.0) ** 2), y))
    n = len(profile)
    verts = []
    for (px, py) in ring:
        for (k, z, _) in profile:
            verts.append((center[0] + px * k, center[1] + py * k, z))
    base = len(g.v)
    g.add(verts, [], profile[0][2])
    for i in range(n - 1):
        for s in range(steps):
            s1 = (s + 1) % steps
            a, b = s * n + i, s1 * n + i
            g.f.append((base + a, base + b, base + b + 1, base + a + 1))
            g.m.append(profile[i][2])
            g.uv.append(None)
    return g


def junction_box(g, at, facing, terminals=10):
    """The settlers' logic junction box: a zinc box with a rust-streaked lid, a row of brass terminals on its face."""
    at, f = Vector(at), Vector(facing).normalized()
    side = Vector((0, 0, 1)).cross(f).normalized()
    m = Matrix((side, f, (0, 0, 1))).transposed()
    box(g, tuple(at + Vector((0, 0, 0.045))), (0.075, 0.03, 0.045), "zinc", bevel=0.006, rot=m)
    box(g, tuple(at + Vector((0, 0, 0.092))), (0.08, 0.034, 0.004), "rust", bevel=0.002, rot=m)
    for k in range(terminals):
        row, col = divmod(k, 5)
        p = at + f * 0.031 + side * (-0.048 + col * 0.024) + Vector((0, 0, 0.06 - row * 0.03))
        cylinder(g, p - f * 0.002, p + f * 0.008, 0.0065, "brass", segs=10)
        cylinder(g, p + f * 0.008, p + f * 0.011, 0.0035, "hole", segs=8)
    for sx in (-1, 1):
        K.screw(g, at + f * 0.031 + side * sx * 0.064 + Vector((0, 0, 0.012)), f, 0.004, "rust")


def plank(g, c, half, mat, rot=None):
    box(g, c, half, mat, bevel=0.004, rot=rot)


# =====================================================================================================
# The Toilet
# =====================================================================================================

TOILET_FLOOR = -0.5
TOILET_LID = -0.11           # the closed lid's top: the seat point, 0.39 m above the floor (the sit clip)
BOWL_Y, BOWL_W, BOWL_L, BOWL_TAPER = -0.02, 0.18, 0.25, 0.2


def build_toilet(mats, _level=None):
    """A classic US two-piece toilet in glossy white porcelain: an elongated bowl on a tapered foot with bolt caps, a
    closed seat and lid, the tank on the bowl's back with its lid and a chrome flush handle on its left front (the
    left as you face it), and the shutoff valve and braided supply line at the wall side under the tank."""
    root = K.make_empty("Toilet")
    g = Geo()
    floor, lid_top = TOILET_FLOOR, TOILET_LID
    seat_top = lid_top - 0.026
    rim = seat_top - 0.03
    plan = dict(center=(0, BOWL_Y), taper=BOWL_TAPER)
    # the bowl: the foot on the floor narrowing to a waist, flaring out to the rolled rim, the bowl's inside below it
    prof = [(0.64, floor, "porcelain"), (0.69, floor + 0.01, "porcelain"), (0.66, floor + 0.03, "porcelain"),
            (0.6, floor + 0.1, "porcelain"), (0.61, floor + 0.17, "porcelain"), (0.7, floor + 0.245, "porcelain"),
            (0.85, floor + 0.29, "porcelain"), (0.96, rim - 0.035, "porcelain"), (1.0, rim - 0.01, "porcelain"),
            (0.985, rim, "porcelain"), (0.82, rim, "porcelain"), (0.76, rim - 0.03, "porcelain"),
            (0.52, rim - 0.12, "porcelain"), (0.0, rim - 0.13, "porcelain")]
    basin(g, BOWL_W, BOWL_L, prof, power=2.3, steps=48, **plan)
    # the bowl's neck behind the seat, up under the tank
    neck = [(-0.375, 0, 0.115, floor + 0.2, rim + 0.012), (-0.33, 0, 0.13, floor + 0.13, rim + 0.012),
            (-0.26, 0, 0.13, floor + 0.04, rim + 0.006), (-0.19, 0, 0.12, floor + 0.0, rim - 0.02)]
    K.squircle_loft(g, K.smooth_sections(neck, 3), "porcelain", ring_pts=28, power=3.5)
    # bolt caps either side of the foot
    for sx in (-1, 1):
        p = Vector((sx * 0.13, BOWL_Y - 0.05, floor))
        dome(g, p, (0, 0, 1), 0.019, "porcelain", segs=14, height=0.03)
    # the seat ring and the closed lid over it, hinged at the back
    ring_prof = [(0.66, rim + 0.002, "seat_white"), (1.02, rim + 0.002, "seat_white"), (1.035, seat_top - 0.01, "seat_white"),
                 (1.01, seat_top, "seat_white"), (0.7, seat_top, "seat_white"), (0.64, seat_top - 0.012, "seat_white"),
                 (0.66, rim + 0.002, "seat_white")]
    basin(g, BOWL_W, BOWL_L, ring_prof, power=2.3, steps=48, **plan)
    lid_prof = [(0.0, seat_top + 0.001, "seat_white"), (0.99, seat_top + 0.001, "seat_white"), (1.03, seat_top + 0.009, "seat_white"),
                (1.02, lid_top - 0.008, "seat_white"), (0.95, lid_top, "seat_white"), (0.0, lid_top, "seat_white")]
    basin(g, BOWL_W, BOWL_L, lid_prof, power=2.3, steps=48, **plan)
    hinge_y = BOWL_Y - BOWL_L - 0.012
    for sx in (-1, 1):
        box(g, (sx * 0.075, hinge_y, rim + 0.016), (0.022, 0.018, 0.016), "seat_white", bevel=0.006)
        cylinder(g, (sx * 0.075 - 0.024, hinge_y + 0.004, seat_top - 0.004), (sx * 0.075 + 0.024, hinge_y + 0.004, seat_top - 0.004),
                 0.011, "seat_white", segs=12)
    # the tank on the neck: rounded, a little wider at the top, its lid overhanging
    t_back, t_front, t_bot, t_top = -0.475, -0.29, rim + 0.012, 0.222
    tank = [(t_back, 0, 0.23, t_bot + 0.012, t_top - 0.01), (t_back + 0.008, 0, 0.243, t_bot, t_top),
            (t_front - 0.008, 0, 0.243, t_bot, t_top), (t_front, 0, 0.23, t_bot + 0.012, t_top - 0.01)]
    K.squircle_loft(g, tank, "porcelain", ring_pts=36, power=7.0)
    box(g, (0, (t_back + t_front) / 2, t_top + 0.012), (0.255, (t_front - t_back) / 2 + 0.009, 0.012), "porcelain", bevel=0.009)
    # the flush handle: a chrome escutcheon on the tank's front near its left end, the lever out toward that side
    esc = Vector((0.17, t_front, t_top - 0.065))
    cylinder(g, esc - Vector((0, 0.003, 0)), esc + Vector((0, 0.008, 0)), 0.021, "chrome", segs=20)
    cylinder(g, esc + Vector((0, 0.008, 0)), esc + Vector((0, 0.016, 0)), 0.01, "chrome", segs=14)
    cylinder(g, esc + Vector((0, 0.016, 0)), esc + Vector((0.075, 0.02, -0.006)), 0.0075, "chrome", segs=12, r1=0.0105)
    dome(g, esc + Vector((0.075, 0.02, -0.006)), (1, 0.05, -0.08), 0.0105, "chrome", segs=12, height=0.008)
    # the shutoff valve at the wall side and the braided supply line up to the fill valve under the tank's left
    sx = 0.17
    shank = Vector((sx, -0.41, t_bot))
    cylinder(g, shank - Vector((0, 0, 0.022)), shank + Vector((0, 0, 0.002)), 0.019, "seat_white", segs=8)
    valve = Vector((sx, -0.455, floor + 0.11))
    cylinder(g, (sx, valve.y, floor), (sx, valve.y, floor + 0.006), 0.032, "chrome", segs=20)
    cylinder(g, (sx, valve.y, floor + 0.006), valve - Vector((0, 0, 0.02)), 0.011, "chrome", segs=14)
    cylinder(g, valve - Vector((0, 0, 0.022)), valve + Vector((0, 0, 0.02)), 0.015, "chrome", segs=16)
    cylinder(g, valve, valve + Vector((0, 0.035, 0)), 0.006, "chrome", segs=10)
    box(g, tuple(valve + Vector((0, 0.04, 0))), (0.022, 0.006, 0.012), "chrome", bevel=0.005)
    line = [valve + Vector((0, 0, 0.02)), valve + Vector((0, 0.004, 0.08)), valve + Vector((0, 0.02, 0.16)),
            shank + Vector((0, -0.015, -0.11)), shank + Vector((0, -0.002, -0.05)), shank - Vector((0, 0, 0.022))]
    sweep(g, bend(line, 0.05), 0.0065, "steel", segs=10)
    for p in (line[0], line[-1]):
        cylinder(g, p - Vector((0, 0, 0.012)), p + Vector((0, 0, 0.012)), 0.0105, "chrome", segs=6)
    # where the links plug in: the batch's link box on the floor at the back right
    K.link_box(g, (-0.26, -0.43, floor), (0, 1, 0), K.FINISH[1], scale=0.9)
    K.make_object("Body", g, mats, root)
    return {"root": root}


def bend(points, radius, steps=5):
    """A polyline with rounded corners (quadratic fillets), for the supply line."""
    pts = [Vector(p) for p in points]
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, p, b = pts[i - 1], pts[i], pts[i + 1]
        r = min(radius, (a - p).length * 0.45, (b - p).length * 0.45)
        s, e = p + (a - p).normalized() * r, p + (b - p).normalized() * r
        for k in range(steps + 1):
            t = k / steps
            out.append(s * (1 - t) ** 2 + p * 2 * t * (1 - t) + e * t * t)
    out.append(pts[-1])
    return out


# =====================================================================================================
# The Bathtub
# =====================================================================================================

def build_bathtub(mats, _level=None):
    root = K.make_empty("Bathtub")
    g = Geo()
    hw, hl = 0.355, 0.85
    rim, out_floor, in_floor = 0.15, -0.265, -0.235
    prof = [(0.0, out_floor, "enamel"), (0.86, out_floor, "enamel"), (0.95, out_floor + 0.06, "enamel"), (0.99, 0.0, "enamel"),
            (1.0, rim - 0.02, "enamel"), (1.0, rim, "enamel"), (0.965, rim, "enamel_in"), (0.95, rim - 0.03, "enamel_in"),
            (0.92, in_floor + 0.08, "enamel_in"), (0.84, in_floor, "enamel_in"), (0.0, in_floor, "enamel_in")]
    basin(g, hw, hl, prof, power=3.2, steps=56)
    # a rolled rim
    rim_pts = []
    e = 2.0 / 3.2
    for s in range(57):
        a = 2 * math.pi * s / 56
        c, sn = math.cos(a), math.sin(a)
        rim_pts.append(Vector((0.985 * hw * math.copysign(abs(c) ** e, c), 0.985 * hl * math.copysign(abs(sn) ** e, sn), rim)))
    sweep(g, rim_pts, 0.011, "enamel", segs=8)
    # claw feet
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = Vector((sx * 0.23, sy * 0.6, out_floor + 0.01))
            cylinder(g, p, p + Vector((sx * 0.03, sy * 0.02, -0.08)), 0.032, "graphite", segs=10, r1=0.022)
            foot = p + Vector((sx * 0.035, sy * 0.025, -0.095))
            dome(g, foot + Vector((0, 0, -0.012)), (0, 0, 1), 0.036, "graphite", segs=10, height=0.03)
            for k in (-1, 0, 1):
                d = Vector((sx * math.cos(k * 0.6), sy * math.sin(k * 0.6) * 0.8 + sy * 0.2, 0)).normalized()
                cylinder(g, foot + Vector((0, 0, -0.008)), foot + d * 0.045 + Vector((0, 0, -0.012)), 0.009, "graphite", segs=6)
    # the plank seat across the tub (hips over it, the back to the -y end)
    seat_y, seat_top = -0.43, -0.065
    for k, dy in enumerate((-0.07, 0.0, 0.07)):
        plank(g, (0, seat_y + dy, seat_top - 0.014), (0.31 - 0.03 * (k != 1), 0.033, 0.014), "plank" if k != 1 else "plank_dark")
    for sx in (-1, 1):
        # cleats under the seat resting on the tub's sides
        plank(g, (sx * 0.27, seat_y, seat_top - 0.05), (0.02, 0.1, 0.022), "plank_dark")
        plank(g, (sx * 0.29, seat_y, (seat_top - 0.07 + in_floor) / 2), (0.016, 0.03, (seat_top - 0.07 - in_floor) / 2), "plank_dark")
    # the water and the taps at the far end
    K.squircle_loft(g, [(y, 0, hw * 0.86 * w, -0.15, -0.14) for (y, w) in ((-hl * 0.78, 0.5), (-hl * 0.6, 0.98), (hl * 0.6, 0.98), (hl * 0.78, 0.5))],
                    "water", ring_pts=16, power=2.0, caps=True)
    tap_base = Vector((0, hl - 0.03, rim + 0.01))
    cylinder(g, tap_base + Vector((0, 0, -0.02)), tap_base + Vector((0, 0, 0.08)), 0.018, "zinc", segs=12)
    cylinder(g, tap_base + Vector((0, 0, 0.07)), tap_base + Vector((0, -0.1, 0.07)), 0.013, "zinc", segs=10)
    cylinder(g, tap_base + Vector((0, -0.1, 0.07)), tap_base + Vector((0, -0.11, 0.045)), 0.014, "zinc", segs=10)
    for sx in (-1, 1):
        h = tap_base + Vector((sx * 0.07, 0.0, 0.0))
        cylinder(g, h + Vector((0, 0, -0.02)), h + Vector((0, 0, 0.04)), 0.012, "zinc", segs=10)
        for k in range(3):
            a = k * math.pi / 1.5
            cylinder(g, h + Vector((0, 0, 0.04)), h + Vector((math.cos(a) * 0.03, math.sin(a) * 0.03, 0.045)), 0.005, "brass", segs=6)
    # a mustard towel folded over the side near the seat
    box(g, (hw - 0.03, -0.15, rim + 0.013), (0.05, 0.1, 0.004), "mustard", bevel=0.002)
    box(g, (hw + 0.015, -0.15, rim - 0.09), (0.004, 0.1, 0.105), "mustard", bevel=0.002)
    # the junction box hung on the seat end, outside the tub
    junction_box(g, (0, -hl + 0.02, -0.2), (0, -1, 0))
    K.make_object("Body", g, mats, root)
    return {"root": root}
