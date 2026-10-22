"""The parts map's batch 17c item transport parts (see part_kit.py, light_kit.py, working_kit.py and station_kit.py):
the Vacuum Pipe, the Vacuum Pipe Corner, the Vacuum Pump and the Resource Collector.

All four are **Salvager Tech** (``docs/art-direction.md``): hull dark and gunmetal on screwed bases, copper bands and
screws, stylised but not cartoony, with **cyan only on powered accents** (the Vacuum Pump's mouth ring and pip, lit
while its logic input is on). Never orange.

* The pipes are thick tinted glass tubes in a gunmetal cage: four stringers between two end collars and a screwed
  middle band, a few scrap bits lying in the glass so it reads as a pipe things travel through. Each end is the
  standard IO port's collar (``tools/io_port``: gunmetal ring, teal ring, gunmetal lip and dark bore, at the port's
  2/3 scale) with its face on the footprint boundary, so a pipe end meets another pipe's end or a machine port's
  collar exactly. The corner turns a quarter bend from its back to its right side.
* The Vacuum Pump is a gunmetal canister lying along its length: the standard two-way IO port on its back, a glass
  section in the middle where sucked-up items swirl, a motor with cooling fins and the logic jack on top, and a flared
  intake mouth on its front with a grille and a cyan ring (its ``on`` and ``off`` clips light the ring and the pip).
* The Resource Collector is a screwed hull bin with a cyclone intake funnel on top (vanes and a steel cone), a
  level window on its front showing what it holds, a canvas filter sock on its back (it burns) and the standard
  output port on its right side.

Axes are Blender's: x right, z up and the part's **front along -y** (glTF +z). Each part fills its size box and stands
on the bottom of it. ``build_<part>(m)`` returns a rig for ``station_kit``'s runner shape: ``root``, ``clips``,
``rest`` and ``states``. ``run`` writes ``meshes/parts/<part>.glb`` and ``textures/items/<part>_icon.png``, and with
``--render OUT_DIR`` the review views of every state and the part's sheet. Run ``tools/vacuum/build_vacuum.py``, not
this file.
"""

from __future__ import annotations

import math
import random

from mathutils import Matrix, Vector

import part_kit as K
import light_kit as L
import station_kit as S
import working_kit as W
from part_kit import Geo, box, cylinder

lt = K.lt
pt = K.pt
io = S.io

BLOCK = 0.25
PIPE_HALF = BLOCK / 2
TUBE_R = 0.066                     # the glass tube's outer radius
COLLAR_R = io.COLLAR_R * io.SCALE * 0.95   # a pipe end's collar: the IO port's collar, a little slimmer to sit in a block
TEAL_IN, TEAL_OUT = io.TEAL_RING[0] * io.SCALE, io.TEAL_RING[1] * io.SCALE
LIP_R = io.LIP_R * io.SCALE
BORE_R = io.BORE_R * io.SCALE
COLLAR_DEPTH = 0.024
SAL = W.SAL


def materials():
    m = S.materials()
    glass = pt.plain("t_tube_glass", "#a9cdd2", 0.04, 0.0)
    b = glass.node_tree.nodes.get("Principled BSDF")
    b.inputs["Alpha"].default_value = 0.32
    b.inputs["IOR"].default_value = 1.45
    glass.surface_render_method = "BLENDED"
    m["tube_glass"] = glass
    # the bits in the glass: scrap, wood and copper chips (plain, so they read through the tint)
    m["bit_scrap"] = pt.plain("t_bit_scrap", "#6d6a63", 0.6, 0.6)
    m["bit_wood"] = pt.plain("t_bit_wood", "#8a6440", 0.8, 0.0)
    m["bit_copper"] = pt.plain("t_bit_copper", "#b8733a", 0.35, 1.0)
    m["bit_stone"] = pt.plain("t_bit_stone", "#8d8a84", 0.9, 0.0)
    m["canvas"] = lt.textured("t_canvas", K.half_res(K.fabric_surface(1721, "#7c7560")), normal_strength=1.6)
    return m


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def collar(g, face, normal):
    """A pipe end: the IO port's collar (gunmetal ring, teal ring, gunmetal lip, dark bore), its face at `face` (on the
    footprint boundary) looking along `normal`."""
    n = Vector(normal).normalized()
    origin = Vector(face) - n * COLLAR_DEPTH
    d = COLLAR_DEPTH
    L.lathe_on(g, [(TEAL_OUT - 0.002, 0.0), (COLLAR_R, 0.0), (COLLAR_R, d - 0.005), (COLLAR_R - 0.004, d - 0.001),
                   (TEAL_OUT - 0.002, d - 0.001)], "gun", origin, n, steps=40)
    L.lathe_on(g, [(TEAL_IN, d - 0.006), (TEAL_OUT, d - 0.006), (TEAL_OUT, d - 0.0015), (TEAL_IN, d - 0.0015)],
               "teal", origin, n, steps=40)
    L.lathe_on(g, [(BORE_R, 0.0), (LIP_R, 0.0), (LIP_R, d), (BORE_R, d)], "gun_dark", origin, n, steps=40)
    L.lathe_on(g, [(0.0, d - 0.012), (BORE_R, d - 0.012), (BORE_R, d - 0.010), (0.0, d - 0.010)], "hole", origin, n, steps=24)
    # four copper screws round the ring, as the port's corner bolts
    u, w = K.bl.frame_of(n)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = origin + n * (d - 0.004) + (u * math.cos(a) + w * math.sin(a)) * (COLLAR_R - 0.011)
        K.screw(g, p, n, 0.0042, "copper")


def bits(g, points, seed, size=0.011):
    """A few scrap bits (chunky little boxes) lying at the given points."""
    rnd = random.Random(seed)
    mats = ("bit_scrap", "bit_wood", "bit_copper", "bit_stone")
    for p in points:
        s = size * rnd.uniform(0.7, 1.25)
        rot = Matrix.Rotation(rnd.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rnd.uniform(-0.6, 0.6), 3, "X")
        box(g, p, (s, s * rnd.uniform(0.6, 1.0), s * rnd.uniform(0.5, 0.9)), mats[rnd.randrange(len(mats))],
            bevel=s * 0.25, rot=rot)


def straight_glass(g, y0, y1, seed):
    """The glass tube along y from y0 to y1 with its cage (four gunmetal stringers) and bits lying in it."""
    cylinder(g, (0, y0, 0), (0, y1, 0), TUBE_R, "tube_glass", segs=32)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        x, z = math.cos(a) * (TUBE_R + 0.006), math.sin(a) * (TUBE_R + 0.006)
        box(g, (x, (y0 + y1) / 2, z), (0.0055, (y1 - y0) / 2, 0.0055), "gun", bevel=0.002,
            rot=Matrix.Rotation(a, 3, "Y").transposed())
    rnd = random.Random(seed)
    floor = -TUBE_R + 0.016
    bits(g, [(rnd.uniform(-0.025, 0.025), y0 + (y1 - y0) * t, floor + rnd.uniform(0, 0.006)) for t in (0.22, 0.48, 0.7)], seed)


def band(g, center, axis, r_in, r_out, depth, mat="gun", screws=2):
    """A screwed band round a tube at `center` along `axis`."""
    n = Vector(axis).normalized()
    c = Vector(center)
    L.lathe_on(g, [(r_in, -depth / 2), (r_out, -depth / 2), (r_out, depth / 2), (r_in, depth / 2)], mat, c, n, steps=36)
    u, w = K.bl.frame_of(n)
    for k in range(screws):
        a = math.pi / 2 + k * 2 * math.pi / screws
        d = u * math.cos(a) + w * math.sin(a)
        K.screw(g, c + d * r_out, d, 0.004, "copper")


# =====================================================================================================
# Vacuum Pipe (1 x 1 x 1): a glass tube in a gunmetal cage between two port collars, along its length (y)
# =====================================================================================================

def build_vacuum_pipe(m):
    root = K.make_empty("VacuumPipe")
    g = Geo()
    h = PIPE_HALF
    collar(g, (0, -h, 0), (0, -1, 0))
    collar(g, (0, h, 0), (0, 1, 0))
    straight_glass(g, -h + COLLAR_DEPTH - 0.002, h - COLLAR_DEPTH + 0.002, 1731)
    band(g, (0, 0, 0), (0, 1, 0), TUBE_R + 0.004, TUBE_R + 0.016, 0.026)
    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}, "rest": lambda: None}
    rig["states"] = [("PIPE", rig["rest"])]
    return rig


# =====================================================================================================
# Vacuum Pipe Corner (1 x 1 x 1): a quarter bend from its back (+y in Blender) to its right side (+x)
# =====================================================================================================

def build_vacuum_pipe_corner(m):
    root = K.make_empty("VacuumPipeCorner")
    g = Geo()
    h = PIPE_HALF
    collar(g, (0, h, 0), (0, 1, 0))
    collar(g, (h, 0, 0), (1, 0, 0))
    # the bend: a torus quarter about the block's back right edge (h, h), from the back face's centre to the side's
    c = (h, h)
    radius = h
    tube = [(radius + TUBE_R * math.cos(2 * math.pi * k / 24), TUBE_R * math.sin(2 * math.pi * k / 24)) for k in range(24)]
    K.lathe(g, tube, "tube_glass", a0=math.pi, a1=1.5 * math.pi, steps=20, center=c)
    # the cage: top and bottom stringers following the bend, and a band at its middle
    for z in (TUBE_R + 0.006, -TUBE_R - 0.006):
        K.lathe(g, [(radius - 0.0055, z - 0.0055), (radius + 0.0055, z - 0.0055), (radius + 0.0055, z + 0.0055),
                    (radius - 0.0055, z + 0.0055)], "gun", a0=math.pi, a1=1.5 * math.pi, steps=20, center=c)
    for side in (-1, 1):
        a = math.pi + math.pi / 4
        p = Vector((c[0] + radius * math.cos(a), c[1] + radius * math.sin(a), 0))
        off = Vector((math.cos(a), math.sin(a), 0)) * side * (TUBE_R + 0.006)
        box(g, p + off, (0.0055, 0.0055, TUBE_R * 0.75), "gun", bevel=0.002, rot=Matrix.Rotation(a, 3, "Z"))
    mid = Vector((c[0] + radius * math.cos(1.25 * math.pi), c[1] + radius * math.sin(1.25 * math.pi), 0))
    band(g, mid, (math.sin(1.25 * math.pi), -math.cos(1.25 * math.pi), 0), TUBE_R + 0.004, TUBE_R + 0.016, 0.026)
    # scrap bits lying in the glass
    floor = -TUBE_R + 0.016
    pts = []
    for t in (0.25, 0.55, 0.8):
        a = math.pi + t * math.pi / 2
        pts.append((c[0] + radius * math.cos(a), c[1] + radius * math.sin(a), floor))
    bits(g, pts, 1733)
    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}, "rest": lambda: None}
    rig["states"] = [("CORNER", rig["rest"])]
    return rig


# =====================================================================================================
# Vacuum Pump (3 x 3 x 4): canister along y, back port, glass chamber, motor on top, flared mouth in front
# =====================================================================================================

def build_vacuum_pump(m):
    root = K.make_empty("VacuumPump")
    f = SAL
    g = Geo()
    floor, hx, hy = -0.375, 0.375, 0.5
    r = 0.24
    # screwed base under the canister and two saddles holding it
    top = W.base_plate(g, floor, hx - 0.02, 0.3, f, t=0.012, inset=0.026)
    for y in (-0.09, 0.22):
        box(g, (0, y, (top + (-r * 0.55)) / 2), (r * 0.82, 0.03, ((-r * 0.55) - top) / 2), "gun", bevel=0.006)
        for sx in (-1, 1):
            K.screw(g, (sx * r * 0.62, y - 0.031, top + 0.03), (0, -1, 0), 0.006, "copper")
    # the back: a hull port housing with a gunmetal flange, set in so the port's plate stands just proud of it
    back = hy - S.SIDE_IN
    box(g, (0, back - 0.1, 0), (0.29, 0.09, 0.29), "hull", bevel=0.025)
    box(g, (0, back - 0.014, 0), (0.33, 0.014, 0.33), "gun", bevel=0.01)
    for sx in (-1, 1):
        for sz in (-1, 1):
            K.screw(g, (sx * 0.305, back + 0.0005, sz * 0.305), (0, 1, 0), 0.0075, "copper")
            box(g, (sx * 0.27, back - 0.11, sz * 0.27), (0.03, 0.08, 0.03), "gun", bevel=0.008)
    S.port(root, (0, hy, 0), (0, 1, 0), "both")
    # the canister: hull rear drum, copper bands, the glass chamber, the front drum
    y_back, y_glass0, y_glass1, y_front = back - 0.19, 0.16, -0.04, -0.13
    W.y_cyl(g, y_glass0, y_back, r, "hull", segs=40)
    W.y_cyl(g, y_front, y_glass1, r, "hull", segs=40)
    for y in (y_back - 0.03, y_glass0 + 0.02, y_glass1 - 0.02, y_front + 0.02):
        W.y_ring(g, y, 0.022, r - 0.004, r + 0.012, "copper", segs=40)
    W.y_cyl(g, y_glass1, y_glass0, r - 0.012, "tube_glass", segs=40)
    W.y_cyl(g, y_glass1, y_glass0, 0.05, "gun_dark", segs=16)
    # the cage over the glass, and items swirling in it
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        box(g, (math.cos(a) * (r - 0.006), (y_glass0 + y_glass1) / 2, math.sin(a) * (r - 0.006)),
            (0.007, (y_glass0 - y_glass1) / 2, 0.007), "gun", bevel=0.002, rot=Matrix.Rotation(-a, 3, "Y"))
    rnd = random.Random(1741)
    swirl = []
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.2, 0.2)
        rr = rnd.uniform(0.09, r - 0.05)
        swirl.append((math.cos(a) * rr, rnd.uniform(y_glass1 + 0.03, y_glass0 - 0.03), math.sin(a) * rr))
    bits(g, swirl, 1743, size=0.016)
    # the flared mouth: a gunmetal cone, a copper lip, a steel grille over a dark throat
    y_mouth = -hy + 0.004
    L.lathe_on(g, [(r - 0.01, 0.0), (r + 0.012, 0.0), (0.345, -y_mouth + y_front - 0.03), (0.36, -y_mouth + y_front - 0.006),
                   (0.33, -y_mouth + y_front - 0.006), (r - 0.03, 0.02)], "gun", (0, y_front, 0), (0, -1, 0), steps=48)
    L.lathe_on(g, [(0.33, 0.0), (0.366, 0.0), (0.366, 0.018), (0.33, 0.018)], "copper", (0, y_mouth, 0), (0, 1, 0), steps=48)
    L.lathe_on(g, [(0.0, 0.0), (r - 0.02, 0.0), (r - 0.02, 0.004), (0.0, 0.004)], "hole", (0, y_front - 0.02, 0), (0, -1, 0), steps=32)
    # a spoked grille over the throat: a hub, six spokes and a ring
    gy = y_mouth + 0.05
    W.y_cyl(g, gy - 0.012, gy + 0.012, 0.045, "steel", segs=24)
    W.y_cyl(g, gy - 0.014, gy + 0.014, 0.022, "copper", segs=16)
    for k in range(6):
        a = 2 * math.pi * k / 6
        box(g, (math.cos(a) * 0.17, gy, math.sin(a) * 0.17), (0.13, 0.007, 0.009), "steel", bevel=0.003,
            rot=Matrix.Rotation(-a, 3, "Y"))
    W.y_ring(g, gy, 0.016, 0.285, 0.305, "steel", segs=48)
    # the motor on the port housing: a finned hull case on a gunmetal plinth with the status pip; the logic jack on the
    # canister's back drum
    my, mz = back - 0.1, 0.29
    box(g, (0, my, mz + 0.006), (0.15, 0.085, 0.006), "gun", bevel=0.003)
    box(g, (0, my, mz + 0.04), (0.12, 0.07, 0.03), "hull", bevel=0.01)
    for k in range(7):
        box(g, (-0.099 + k * 0.033, my, mz + 0.072), (0.006, 0.065, 0.009), "gun", bevel=0.002)
    pip = S.status_pip(m, g, root, "Pip", (0.075, my - 0.071, mz + 0.04), (0, -1, 0), r=0.008)
    K.link_box(g, (0.0, 0.215, r - 0.012), (0, -1, 0), {"dark": "gun_dark", "bolt": "copper"}, 1.0)
    K.make_object("Body", g, m, root)
    # the cyan ring inside the mouth, lit while the pump runs
    ring = K.make_object("Ring", K.glow_ring(0.255, 0.275, "cyan_glow", h=0.006), m, root, (0, y_front - 0.04, 0))
    ring.rotation_euler = (math.pi / 2, 0, 0)
    rig = {"root": root}
    W.keep(rig, pip)
    rest_ring = ring.scale.copy()

    def lit(u):
        W.lit(pip, u)
        s = K.OFF + (1 - K.OFF) * u
        ring.scale = (rest_ring.x * s, rest_ring.y * s, rest_ring.z * s)
    W.keep(rig, ring)
    rig["rest"] = lambda: lit(0)
    rig["clips"] = {}
    rig["pose"] = lit
    rig["states"] = [("OFF", lambda: lit(0)), ("ON", lambda: lit(1))]
    return rig


# =====================================================================================================
# Resource Collector (4 x 4 x 4): a hull bin with a cyclone intake on top and an output port on its right
# =====================================================================================================

def build_resource_collector(m):
    root = K.make_empty("ResourceCollector")
    f = SAL
    g = Geo()
    floor, h = -0.5, 0.5
    xs = h - S.SIDE_IN
    top = W.base_plate(g, floor, xs, h - 0.02, f, t=0.014, inset=0.03)
    # the bin: hull core, gunmetal corner posts and rails, inset panels; it stops short of the back, where the filter
    # sock hangs inside the footprint
    b0, b1 = top, 0.12
    cy, hd = -0.065, 0.355
    box(g, (0, cy, (b0 + b1) / 2), (xs - 0.006, hd, (b1 - b0) / 2), "hull", bevel=0.012)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (xs - 0.028), cy + sy * hd, (b0 + b1) / 2), (0.028, 0.028, (b1 - b0) / 2), "gun", bevel=0.007)
    for z in (b0 + 0.024, b1 - 0.024):
        for sy in (-1, 1):
            box(g, (0, cy + sy * hd, z), (xs - 0.05, 0.026, 0.024), "gun", bevel=0.006)
        for sx in (-1, 1):
            box(g, (sx * (xs - 0.028), cy, z), (0.026, hd - 0.03, 0.024), "gun", bevel=0.006)
    # front: a level window showing what it holds, and a hatch with a copper handle
    fy = cy - hd - 0.026
    L.plate(g, (0, fy - 0.004, -0.06), (0.3, 0.008, 0.21), "gun_dark", "copper", n=(0, -1, 0), inset=0.018, r=0.007)
    wy = fy - 0.013
    box(g, (-0.12, wy, -0.06), (0.07, 0.004, 0.16), "gun", bevel=0.003)
    box(g, (-0.12, wy - 0.003, -0.06), (0.052, 0.003, 0.142), "tube_glass")
    box(g, (-0.12, wy + 0.012, -0.12), (0.052, 0.01, 0.082), "hull")
    bits(g, [(-0.12 + dx, wy - 0.006, -0.19 + dz) for dx, dz in ((-0.03, 0.0), (0.01, 0.012), (0.035, 0.0), (-0.01, 0.035),
                                                                 (0.025, 0.04), (-0.035, 0.05))], 1751, size=0.012)
    for k in range(5):
        box(g, (-0.05, wy - 0.004, -0.2 + k * 0.07), (0.012, 0.002, 0.002), "copper")
    box(g, (0.11, wy, -0.06), (0.1, 0.006, 0.15), "gun", bevel=0.005)
    W.y_cyl(g, wy - 0.006, wy - 0.022, 0.009, "copper", x=0.11 + 0.05, z=-0.01, segs=12)
    W.y_cyl(g, wy - 0.006, wy - 0.022, 0.009, "copper", x=0.11 + 0.05, z=-0.11, segs=12)
    box(g, (0.16, wy - 0.026, -0.06), (0.01, 0.008, 0.06), "copper", bevel=0.004)
    # the right side's output port, at floor level as the stations'
    S.port(root, (h, -0.125, -0.125), (1, 0, 0), "out")
    # back: a canvas filter sock on a copper ring, strapped to the bin
    sy = cy + hd + 0.026
    W.y_ring(g, sy + 0.012, 0.024, 0.07, 0.095, "copper", x=0.0, z=-0.02, segs=28)
    sock = [Vector((0, sy + 0.02, -0.02)), Vector((0, sy + 0.07, -0.06)), Vector((0, sy + 0.09, -0.2)), Vector((0, sy + 0.07, -0.33))]
    pts = K.bezier(*sock, n=10)
    for i, p in enumerate(pts):
        rr = 0.085 - 0.02 * (i / (len(pts) - 1)) ** 2
        if i < len(pts) - 1:
            cylinder(g, tuple(p), tuple(pts[i + 1]), rr, "canvas", segs=20, r1=0.085 - 0.02 * ((i + 1) / (len(pts) - 1)) ** 2)
    box(g, (0, sy + 0.035, -0.22), (0.09, 0.006, 0.012), "gun_dark", bevel=0.003)
    # the cyclone intake on top: a gunmetal funnel opening upward, a copper lip, vanes and a steel cone in its throat
    z0 = b1
    fc = (0, cy + 0.02)
    box(g, (0, cy, z0 + 0.012), (xs - 0.03, hd - 0.02, 0.012), "gun", bevel=0.006)
    L.lathe_on(g, [(0.17, 0.0), (0.19, 0.0), (0.37, h - z0 - 0.042), (0.39, h - z0 - 0.02), (0.365, h - z0 - 0.02),
                   (0.155, 0.035)], "gun", (*fc, z0), (0, 0, 1), steps=56)
    L.lathe_on(g, [(0.36, 0.0), (0.398, 0.0), (0.398, 0.017), (0.36, 0.017)], "copper", (*fc, h - 0.02), (0, 0, 1), steps=56)
    L.lathe_on(g, [(0.0, 0.0), (0.18, 0.0), (0.18, 0.004), (0.0, 0.004)], "hole", (*fc, z0 + 0.03), (0, 0, 1), steps=32)
    K.lathe(g, [(0.0, z0 + 0.03), (0.075, z0 + 0.03), (0.012, z0 + 0.2), (0.0, z0 + 0.2)], "steel", steps=32, center=fc)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rr = 0.17
        box(g, (fc[0] + math.cos(a) * rr, fc[1] + math.sin(a) * rr, z0 + 0.2), (0.065, 0.004, 0.04), "gun_dark", bevel=0.002,
            rot=Matrix.Rotation(a + 0.5, 3, "Z") @ Matrix.Rotation(0.5, 3, "X"))
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        K.screw(g, (fc[0] + math.cos(a) * 0.38, fc[1] + math.sin(a) * 0.38, h - 0.003), (0, 0, 1), 0.006, "copper")
    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}, "rest": lambda: None}
    rig["states"] = [("COLLECTOR", rig["rest"])]
    return rig


# =====================================================================================================
# Runner
# =====================================================================================================

def fresh():
    """An empty scene; the IO port's cached materials went with the old one."""
    K.reset_scene()
    S.gc._IO_MATS.clear()


def run(part, build, *, half, ortho, icon_target=(0, 0, 0), title="", lines=(), icon_view=S.FRONT_ICON):
    """As station_kit.run with this kit's materials; a rig with ``pose`` gets ``on`` and ``off`` clips from it."""
    fresh()
    m = materials()
    rig = build(m)
    root = rig["root"]
    rig["rest"]()
    animated = "pose" in rig
    if animated:
        K.bake_pose_clips([v[0] for v in rig["_rest"].values()], rig["pose"])
    info = K.export(root, K.MESHES / f"{part}.glb", animations=animated)
    K.report(K.MESHES / f"{part}.glb", info)
    K.strip_animation(root)
    W.reset(rig)
    rig["states"][-1][1]()
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", icon_view, ortho, icon_target)
    out = K.args_out()
    if out is not None:
        S.review(part, title or part, list(lines), rig, icon, info, out, half)
    return info


def build_into(m, build, location, rotation_z=0.0):
    """Builds a part's model at `location`, turned `rotation_z` about z (for the batch lineup); returns its root."""
    rig = build(m)
    root = rig["root"]
    rig["states"][-1][1]()
    root.location = location
    root.rotation_euler = (0, 0, rotation_z)
    return root, rig
