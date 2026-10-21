"""The parts map's batch 4 furniture seats in the Settler Patchwork look (docs/art-direction.md): the Toilet and the
Bathtub. Bone enamel, scrap planks, zinc and rust, a graphite clawfoot; the logic outputs (10 each) go into a small
zinc junction box with brass terminals. One level each, no clips (their data has no display readout).

Seat points (Blender: x right, y forward, z up; the part data's ``seat`` uses game axes x, z up, -y):

* the Toilet (3 x 4 x 4 blocks): the feet on the part's bottom (-0.5), the seat ring's top at -0.11 over y -0.05,
  chair height for the ``sit`` clip; the cistern behind is the back rest;
* the Bathtub (3 x 3 x 7 blocks, long along y): the feet on the tub's inner floor (-0.235), a plank seat across the
  tub at y -0.43 with its top at -0.065 (a low seat, ``sit_low``, the legs out along the tub); the taps at the far end.
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, dome, sweep


def basin(g, half_w, half_l, profile, power=3.0, steps=48, center=(0, 0)):
    """A vessel turned round a rounded-rectangle plan (half sizes `half_w` x `half_l`, squircle `power`): `profile`
    is an open run of (k, z, mat) points, k the fraction of the plan (0 the middle, 1 the outside); each segment takes
    the material of its first point."""
    e = 2.0 / power
    ring = []
    for s in range(steps):
        a = 2 * math.pi * s / steps
        c, sn = math.cos(a), math.sin(a)
        ring.append((half_w * math.copysign(abs(c) ** e, c), half_l * math.copysign(abs(sn) ** e, sn)))
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

def build_toilet(mats, _level=None):
    root = K.make_empty("Toilet")
    g = Geo()
    bottom = -0.5
    # the plank stand under the bowl and cistern (the feet stand on the ground in front of it)
    for k, x in enumerate((-0.2, -0.067, 0.067, 0.2)):
        plank(g, (x, -0.16, bottom + 0.018), (0.064, 0.3, 0.018), "plank" if k % 2 else "plank_dark")
    for y in (-0.4, 0.08):
        plank(g, (0, y, bottom + 0.042), (0.29, 0.04, 0.006), "plank_dark")
        for x in (-0.24, 0.0, 0.24):
            K.screw(g, (x, y, bottom + 0.048), (0, 0, 1), 0.006, "rust")
    floor = bottom + 0.048
    # the bowl: enamel, turned round an oval plan, a foot widening onto the stand
    cy = 0.0
    prof = [(0.62, floor, "enamel"), (0.7, floor + 0.02, "enamel"), (0.62, floor + 0.12, "enamel"), (0.72, floor + 0.26, "enamel"),
            (0.92, floor + 0.33, "enamel"), (1.0, -0.15, "enamel"), (1.0, -0.135, "enamel"), (0.8, -0.135, "enamel_in"),
            (0.7, -0.2, "enamel_in"), (0.42, -0.3, "water"), (0.0, -0.3, "water")]
    basin(g, 0.19, 0.24, prof, power=2.4, steps=40, center=(0, cy))
    # the plank seat ring (a hole cut as a ring of planks) and the lid up against the cistern
    seat_top = -0.11
    ring_prof = [(0.62, -0.135, "plank"), (1.04, -0.135, "plank"), (1.06, seat_top - 0.006, "plank"), (1.0, seat_top, "plank"),
                 (0.66, seat_top, "plank"), (0.6, seat_top - 0.01, "plank"), (0.62, -0.135, "plank")]
    basin(g, 0.19, 0.24, ring_prof, power=2.4, steps=40, center=(0, cy))
    lean = math.radians(8)
    lid_c = Vector((0, -0.25 - 0.012, seat_top + 0.19))
    rot = Matrix.Rotation(-lean, 3, "X")
    for k, x in enumerate((-0.12, 0.0, 0.12)):
        plank(g, tuple(lid_c + rot @ Vector((x, 0, 0))), (0.059, 0.011, 0.19), "plank_dark" if k == 1 else "plank", rot=rot)
    for z in (-0.12, 0.12):
        plank(g, tuple(lid_c + rot @ Vector((0, -0.013, z))), (0.17, 0.004, 0.025), "rust", rot=rot)
    # hinges
    for sx in (-1, 1):
        cylinder(g, (sx * 0.1 - 0.03, -0.235, seat_top - 0.005), (sx * 0.1 + 0.03, -0.235, seat_top - 0.005), 0.009, "zinc", segs=10)
    # the cistern: a zinc tank on a plank back board, a pipe down into the bowl, a pull lever with a brass handle
    plank(g, (0, -0.44, -0.05), (0.2, 0.012, 0.4), "plank_dark")
    for z in (-0.38, 0.27):
        plank(g, (0, -0.425, z), (0.22, 0.006, 0.025), "plank")
    tank_c = Vector((0, -0.36, 0.2))
    box(g, tuple(tank_c), (0.2, 0.068, 0.11), "zinc", bevel=0.012)
    box(g, tuple(tank_c + Vector((0, 0, 0.115))), (0.21, 0.075, 0.01), "rust", bevel=0.004)
    for x in (-0.13, 0.0, 0.13):
        box(g, tuple(tank_c + Vector((x, 0.069, 0))), (0.006, 0.0015, 0.1), "rust")
    cylinder(g, tank_c + Vector((0.0, 0.03, -0.11)), (0, -0.25, -0.16), 0.022, "zinc", segs=12)
    cylinder(g, (0, -0.27, -0.155), (0, -0.21, -0.165), 0.03, "rust", segs=12)
    lever = tank_c + Vector((0.2, 0.03, 0.06))
    cylinder(g, lever, lever + Vector((0.035, 0, 0)), 0.008, "zinc", segs=8)
    chain_top = lever + Vector((0.035, 0, 0))
    chain_end = chain_top + Vector((0, 0.02, -0.22))
    sweep(g, [chain_top, chain_top.lerp(chain_end, 0.5) + Vector((0, 0.008, 0)), chain_end], 0.003, "zinc", segs=5)
    cylinder(g, chain_end + Vector((0, 0, -0.045)), chain_end, 0.012, "brass", segs=10)
    # the junction box on the stand's front right corner (its links)
    junction_box(g, (0.25, 0.18, bottom + 0.036), (0, 1, 0))
    K.make_object("Body", g, mats, root)
    return {"root": root}


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
