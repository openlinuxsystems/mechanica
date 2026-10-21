"""The Salvager Tech seats and saddles of the parts map's batch 4, at their five levels (see part_kit.py).

* ``build_seat(level, driver)`` - the Driver's Seat (4 x 4 x 4 blocks) and the Passenger Seat (4 x 4 x 3): a padded
  bucket seat on a pedestal over a framed base plinth. The driver's has a steering wheel on a column into a dash pod.
* ``build_saddle(level, driver)`` - the Driver's Saddle (3 x 4 x 4) and the Saddle (3 x 3 x 3): a saddle on a
  narrow body ridden astride, footboards either side; the driver's has handlebars, the passenger's a grab rail.

Every level keeps the seat point and the envelope and adds to the look (Chase's rule: each level looks cooler):

1. worn gunmetal and hull dark, charcoal fabric, copper screws, one copper pip;
2. teal piping on the cushions, side bolsters and a headrest (a saddle: knee pads), two pips;
3. steel frame, armrests (a saddle: copper side covers) and quilting seams, the dash screen lit cyan, three pips;
4. a roll hoop with copper collars and teal harness straps (a saddle: crash bars), a cyan running light, four pips;
5. black chrome and polished copper, black quilted fabric stitched in cyan, cyan under-glow, five lit cyan pips.

Seat points (Blender: x right, y forward, z up; the part data's ``seat`` uses game axes x, z up, -y):

* the seats: feet on the plinth (``DECK``), the cushion 0.39 m above it, chair height for the ``sit`` clip;
* the saddles: feet on the footboards (``FLOOR``), the cushion 0.528 m above them, where ``sit_saddle``'s hips
  (0.668 m, ``SADDLE_HIPS`` in tools/characters/build_characters.py) less 0.14 m land; the driver's handlebar grips
  are at ``SADDLE_GRIP`` from there.
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, dome, sweep

# ---- the bucket seats -------------------------------------------------------------------------------------------
DECK = -0.44                 # top of the base plinth: the floor the feet rest on
CUSHION_H = 0.39             # cushion above the deck (a chair: the sit clip)
WHEEL_R = 0.19
COLUMN_TILT = 40.0           # degrees from upright; the wheel is square to the column (the scrap seat's)
HUB_FROM_HIPS = (0.347, 0.319)  # the hub forward of and above the cushion point (where the sit clip's hands land)

# ---- the saddles ------------------------------------------------------------------------------------------------
SADDLE_HIPS = 0.668          # tools/characters/build_characters.py (measured), character.json clip_hips.sit_saddle
HIPS_ABOVE_CUSHION = 0.14    # character.json seat.hips_above_cushion
SADDLE_GRIP = (0.26, 0.42, 0.92)   # handlebar grips (fist centres): out, forward and up from the feet under the hips


def seat_spec(driver):
    """The seat's dimensions: half size, the cushion point (y) and the cushion top (z)."""
    half = (0.5, 0.5, 0.5) if driver else (0.5, 0.375, 0.5)
    hips_y = -0.18 if driver else -0.04
    return {"half": half, "hips_y": hips_y, "top": DECK + CUSHION_H}


def saddle_spec(driver):
    half = (0.375, 0.5, 0.5) if driver else (0.375, 0.375, 0.375)
    floor = -0.45 if driver else -0.345
    hips_y = -0.12 if driver else -0.03
    return {"half": half, "floor": floor, "hips_y": hips_y, "top": floor + SADDLE_HIPS - HIPS_ABOVE_CUSHION}


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def plinth(g, half_x, y0, y1, z0, z1, f, level):
    """A framed base plinth: a dark plate in gunmetal rails with corner blocks, bolts on the corners."""
    hx, hy = half_x, (y1 - y0) / 2
    cy, h = (y0 + y1) / 2, (z1 - z0) / 2
    cz = (z0 + z1) / 2
    box(g, (0, cy, cz - 0.004), (hx - 0.01, hy - 0.01, h - 0.004), f["dark"])
    r = 0.016
    for sx in (-1, 1):
        box(g, (sx * (hx - r), cy, cz), (r, hy - 0.03, h), f["frame"], bevel=0.005)
    for sy in (-1, 1):
        box(g, (0, cy + sy * (hy - r), cz), (hx - 0.03, r, h), f["frame"], bevel=0.005)
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = (sx * (hx - 0.026), cy + sy * (hy - 0.026), cz + 0.002)
            box(g, c, (0.026, 0.026, h + 0.002), f["frame"], bevel=0.008)
            K.screw(g, (c[0], c[1], z1 + 0.002), (0, 0, 1), 0.0075, f["bolt"])
    if level >= 3:
        # copper trim plates on the long sides
        for sx in (-1, 1):
            box(g, (sx * (hx + 0.002), cy, cz), (0.002, hy * 0.55, h * 0.55), f["trim"], bevel=0.001)


def running_light(root, mats, name, at, half, mat="cyan_glow"):
    """A thin cyan strip (powered: levels 4 and 5)."""
    g = Geo()
    box(g, (0, 0, 0), half, mat)
    return K.make_object(name, g, mats, root, at)


# =====================================================================================================
# The bucket seats
# =====================================================================================================

def pan_sections(hips_y, top, bolster):
    """The seat pan cushion, from its back to its rounded front edge; wider at the back, a little dished."""
    back, front = hips_y - 0.2, hips_y + 0.27
    keys = [
        (back, 0.0, 0.215 + bolster, top - 0.075, top - 0.005),
        (hips_y - 0.08, 0.0, 0.225 + bolster, top - 0.075, top + 0.004),
        (hips_y + 0.12, 0.0, 0.23 + bolster, top - 0.075, top + 0.01),
        (front - 0.03, 0.0, 0.225 + bolster, top - 0.07, top + 0.004),
        (front, 0.0, 0.21 + bolster, top - 0.055, top - 0.02),
    ]
    return K.smooth_sections(keys, 2)


def back_sections(height, bolster):
    """The backrest, lofted up its local y (local z is its thickness toward the rider, from 0 at its back face)."""
    keys = [
        (0.0, 0.0, 0.215 + bolster, 0.0, 0.07),
        (height * 0.35, 0.0, 0.225 + bolster, 0.0, 0.085),
        (height * 0.8, 0.0, 0.215 + bolster, 0.0, 0.075),
        (height, 0.0, 0.19 + bolster * 0.6, 0.0, 0.06),
    ]
    return K.smooth_sections(keys, 2)


def back_frame(hips_y, top):
    """The backrest's frame: its bottom-back point and the matrix taking the loft's local axes (x, y up the back, z
    toward the rider) into the seat, leaning back."""
    lean = math.radians(8)
    origin = Vector((0, hips_y - 0.23, top - 0.06))
    up = Vector((0, -math.sin(lean), math.cos(lean)))
    fwd = Vector((0, math.cos(lean), math.sin(lean)))
    m = Matrix((Vector((-1, 0, 0)), up, fwd)).transposed().to_4x4()
    return origin, m, up, fwd


def build_seat(mats, level, driver):
    f = K.FINISH[level]
    sp = seat_spec(driver)
    hx, hy, hz = sp["half"]
    hips_y, top = sp["hips_y"], sp["top"]
    name = "DriversSeat" if driver else "PassengerSeat"
    root = K.make_empty(name)
    rig = {"root": root}

    # ---- Base: plinth, pedestal rails, link box, pips, and (driver) the dash pod, column and wheel ----
    g = Geo()
    plinth(g, hx - 0.03, -hy + 0.03, hy - 0.03, -hz, DECK, f, level)
    # the slider rails the pedestal sits on
    for sx in (-1, 1):
        box(g, (sx * 0.12, hips_y - 0.02, DECK + 0.008), (0.014, 0.2, 0.008), f["trim"], bevel=0.003)
    # pips on the plinth's front rail, the link box on its front right corner
    K.pips(g, level, (-0.04 * (level - 1) / 2 - 0.0, hy - 0.03 - 0.0005, (DECK - hz) / 2 - 0.0), (0.04, 0, 0), (0, 1, 0), f)
    K.link_box(g, (-(hx - 0.13), hy - 0.12, DECK), (0, 1, 0), f)
    if driver:
        hub = Vector((0, hips_y + HUB_FROM_HIPS[0], top + HUB_FROM_HIPS[1]))
        t = math.radians(COLUMN_TILT)
        down = Vector((0, math.sin(t), -math.cos(t)))
        pod_top = top + 0.16
        reach = (hub.z - pod_top) / math.cos(t)
        land = hub + down * reach
        pod_c = Vector((0, land.y + 0.065, pod_top - 0.08))
        box(g, tuple(pod_c), (0.085, 0.075, 0.08), f["frame"], bevel=0.012)
        box(g, tuple(pod_c + Vector((0, 0.02, -0.25))), (0.055, 0.04, 0.2), f["dark"], bevel=0.008)
        box(g, (0, pod_c.y + 0.02, DECK + 0.012), (0.09, 0.07, 0.012), f["frame"], bevel=0.004)
        for sx in (-1, 1):
            K.screw(g, (sx * 0.07, pod_c.y + 0.02, DECK + 0.024), (0, 0, 1), 0.006, f["bolt"])
        # the dash face: a screen toward the driver, slanting back, with a copper bezel
        face = pod_c + Vector((0, -0.076, 0.035))
        box(g, tuple(face), (0.06, 0.004, 0.028), f["trim"], bevel=0.002)
        box(g, tuple(face + Vector((0, -0.003, 0))), (0.052, 0.002, 0.022), "screen")
        # the column with collars, then the wheel square to it
        cylinder(g, land + down * 0.02, hub - down * 0.0, 0.022, f["dark"], segs=16)
        cylinder(g, land - down * 0.04, land + down * 0.0, 0.032, f["trim"], segs=18)
        cylinder(g, hub + down * 0.03, hub + down * 0.075, 0.03, f["frame"], segs=18)
        wheel(g, hub, -down, level, f)
    K.make_object("Base", g, mats, root)
    if driver and level >= 3:
        screen = Geo()
        box(screen, (0, 0, 0), (0.046, 0.001, 0.018), "cyan_glow" if level >= 3 else "teal")
        K.make_object("DashScreen", screen, mats, root, tuple(pod_c + Vector((0, -0.0835, 0.035))))
    if level >= 4:
        running_light(root, mats, "RunningLight", (0, hy - 0.03 + 0.003, DECK - 0.012), (hx - 0.12, 0.002, 0.004))
    if level >= 5:
        for sx in (-1, 1):
            running_light(root, mats, f"UnderGlow{'R' if sx > 0 else 'L'}", (sx * (hx - 0.03 + 0.003), 0, DECK - 0.02),
                          (0.002, hy - 0.1, 0.004))

    # ---- Shell (moves with the clips): pedestal, pan tray, back shell, armrests, headrest posts ----
    shell = rig["shell"] = K.make_empty("Shell", root, (0, 0, DECK))
    rig["shell_at"] = (0, 0, DECK)
    s = Geo()
    z0 = -DECK                                   # shell-local z of the world origin
    tray_z = top - 0.085 + z0
    cylinder(s, (0, hips_y, 0.015), (0, hips_y, tray_z - 0.03), 0.05, f["dark"], segs=14)
    cylinder(s, (0, hips_y, tray_z - 0.13), (0, hips_y, tray_z - 0.02), 0.034, "steel" if level < 5 else "chrome", segs=18)
    box(s, (0, hips_y, 0.012), (0.15, 0.16, 0.012), f["frame"], bevel=0.004)
    box(s, (0, hips_y + 0.03, tray_z - 0.01), (0.25, 0.25, 0.018), f["frame"], bevel=0.008)
    for sx in (-1, 1):
        box(s, (sx * 0.252, hips_y + 0.03, tray_z + 0.02), (0.008, 0.24, 0.03), f["dark"], bevel=0.004)
    origin, m, up, fwd = back_frame(hips_y, top)
    back_h = 0.42 if level == 1 else 0.4
    # the back shell: a dark panel behind the backrest, framed
    shell_g = Geo()
    K.squircle_loft(shell_g, [(y, 0.0, hw + 0.02, -0.03, 0.005) for (y, _, hw, _, _) in back_sections(back_h, 0.03 if level >= 2 else 0.0)],
                    f["dark"], ring_pts=16, power=6.0)
    mm = Matrix.Translation(origin + Vector((0, 0, z0))) @ m
    s.extend(shell_g, mm)
    if level >= 3:
        # armrests: a bar each side on a stay from the tray, copper end caps
        for sx in (-1, 1):
            x = sx * 0.265
            a = Vector((x, hips_y - 0.17, top + 0.12 + z0))
            b = Vector((x, hips_y + 0.17, top + 0.12 + z0))
            box(s, tuple((a + b) / 2), (0.028, 0.17, 0.02), f["frame"], bevel=0.008)
            box(s, tuple((a + b) / 2 + Vector((0, 0, 0.022))), (0.024, 0.15, 0.006), f["fabric"], bevel=0.004)
            cylinder(s, b + Vector((0, 0.0, 0)), b + Vector((0, 0.012, 0)), 0.02, f["trim"], segs=16)
            box(s, (x, hips_y + 0.07, (tray_z + top + 0.12 + z0) / 2), (0.012, 0.016, (top + 0.12 + z0 - tray_z) / 2), f["dark"], bevel=0.004)
    if level >= 2:
        # headrest posts
        head_at = origin + up * (back_h + 0.06) + fwd * 0.04 + Vector((0, 0, z0))
        for sx in (-1, 1):
            cylinder(s, origin + up * (back_h - 0.05) + fwd * 0.03 + Vector((sx * 0.07, 0, z0)),
                     head_at + Vector((sx * 0.07, 0, 0)), 0.008, "steel" if level < 5 else "chrome", segs=10)
    K.make_object("ShellFrame", s, mats, shell)

    # ---- Cushion (sinks): the pan, bolsters and piping ----
    bolster = 0.02 if level >= 2 else 0.0
    secs = pan_sections(hips_y, top, 0.0)
    pan_bottom = top - 0.075
    c = Geo()
    local = [(y, xc, hw, zb - pan_bottom, zt - pan_bottom) for (y, xc, hw, zb, zt) in secs]
    K.squircle_loft(c, local, f["fabric"], ring_pts=20, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            bol = [(y, sx * (hw - 0.02), 0.035, zb + 0.01, zt + 0.03) for (y, _, hw, zb, zt) in local[:-2]]
            K.squircle_loft(c, bol, f["fabric"], ring_pts=12, power=3.0)
            sweep(c, K.loft_edge(bol, 70, sx, 3.0), 0.0045, "piping" if level < 5 else "cyan_glow", segs=6)
    if level >= 3:
        for k in range(3):
            y = hips_y - 0.1 + k * 0.12
            box(c, (0, y, local[0][4] + 0.012), (0.17, 0.003, 0.003), "hole")
    rig["cushion"] = K.make_object("Cushion", c, mats, shell, (0, 0, pan_bottom - DECK))

    # ---- Backrest (sinks toward its back face) ----
    bk = Geo()
    bsecs = back_sections(back_h, bolster)
    K.squircle_loft(bk, bsecs, f["fabric"], ring_pts=20, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            sweep(bk, K.loft_edge(bsecs, 55, sx, 4.0), 0.0045, "piping" if level < 5 else "cyan_glow", segs=6)
            bol = [(y, sx * (hw - 0.015), 0.035, 0.0, zt + 0.035) for (y, _, hw, _, zt) in bsecs[1:-1]]
            K.squircle_loft(bk, bol, f["fabric"], ring_pts=12, power=3.0)
        # headrest
        K.squircle_loft(bk, K.smooth_sections([(back_h + 0.03, 0, 0.1, 0.01, 0.07), (back_h + 0.08, 0, 0.11, 0.0, 0.08),
                                               (back_h + 0.13, 0, 0.09, 0.01, 0.065)], 3), f["fabric"], ring_pts=20)
    if level >= 3:
        for k in range(4):
            yy = 0.08 + k * 0.09
            box(bk, (0, yy, 0.086), (0.16, 0.003, 0.004), "hole")
    if level >= 4:
        # harness straps over the shoulders down to the pan, and a lap belt
        for sx in (-1, 1):
            pts = [Vector((sx * 0.09, back_h + 0.0, 0.07)), Vector((sx * 0.1, back_h * 0.6, 0.1)),
                   Vector((sx * 0.11, back_h * 0.25, 0.12)), Vector((sx * 0.13, 0.02, 0.14))]
            for a, b in zip(pts, pts[1:]):
                mid = (a + b) / 2
                d = (b - a)
                rot = along(d, (1, 0, 0))
                box(bk, tuple(mid), (0.022, d.length / 2 + 0.004, 0.003), "webbing", rot=rot)
            box(bk, (sx * 0.13, 0.04, 0.142), (0.018, 0.022, 0.006), f["trim"], bevel=0.002)
    ob = K.make_object("Backrest", bk, mats, shell, tuple(origin - Vector((0, 0, DECK))))
    # local x mirrored so the frame is right-handed (the loft is symmetric): y up the back, z toward the rider
    ob.rotation_mode = "XYZ"
    ob.rotation_euler = Matrix((Vector((-1, 0, 0)), up, fwd)).transposed().to_euler()
    rig["back"] = ob
    rig["back_axis"] = 2
    rig["pan_sink"], rig["back_sink"] = 0.16, 0.1

    # ---- the roll hoop (level 4 and up), on the base ----
    if level >= 4:
        hg = Geo()
        yb = hips_y - 0.27
        pts = [Vector((-0.3, yb, DECK)), Vector((-0.3, yb, 0.36)), Vector((-0.22, yb, 0.465)),
               Vector((0.22, yb, 0.465)), Vector((0.3, yb, 0.36)), Vector((0.3, yb, DECK))]
        sweep(hg, bend(pts, 0.08), 0.019, f["frame"], segs=10)
        for sx in (-1, 1):
            for z in (DECK + 0.04, top + 0.2):
                cylinder(hg, (sx * 0.3, yb, z - 0.018), (sx * 0.3, yb, z + 0.018), 0.026, f["trim"], segs=16)
            box(hg, (sx * 0.3, yb, DECK + 0.006), (0.04, 0.04, 0.006), f["frame"], bevel=0.003)
        if level >= 5:
            box(hg, (0, yb - 0.026, 0.465), (0.06, 0.008, 0.012), f["frame"], bevel=0.004)
        K.make_object("Hoop", hg, mats, root)
        if level >= 5:
            running_light(root, mats, "HoopLamp", (0, hips_y - 0.27 - 0.0355, 0.465), (0.05, 0.0015, 0.007))
    return rig


def wheel(g, hub, normal, level, f):
    """The steering wheel at `hub`, facing `normal` (toward the driver): a rubber rim on three spokes, a hub cap."""
    n = Vector(normal).normalized()
    u = Vector((1, 0, 0))
    w = n.cross(u).normalized()           # up the wheel's face
    rim = []
    for k in range(33):
        a = 2 * math.pi * k / 32
        rim.append(hub + (u * math.cos(a) + w * math.sin(a)) * WHEEL_R)
    sweep(g, rim, 0.017, "rubber", segs=10)
    if level >= 2:
        # grip wraps at ten and two
        for a0 in (math.radians(140), math.radians(40)):
            pts = [hub + (u * math.cos(a0 + d) + w * math.sin(a0 + d)) * WHEEL_R for d in (-0.3, -0.1, 0.1, 0.3)]
            sweep(g, pts, 0.0195, "webbing" if level < 5 else "fabric_quilt", segs=12)
    spoke = "chrome" if level >= 5 else ("copper" if level >= 3 else f["frame"])
    for a in (math.radians(-90), math.radians(20), math.radians(160)):
        d = u * math.cos(a) + w * math.sin(a)
        a_, b_ = hub + d * 0.035, hub + d * (WHEEL_R - 0.012)
        mid, dd = (a_ + b_) / 2, (b_ - a_)
        rot = along(dd, dd.normalized().cross(n))
        box(g, tuple(mid - n * 0.004), (0.016, dd.length / 2, 0.006), spoke, bevel=0.003, rot=rot)
    cylinder(g, hub - n * 0.02, hub + n * 0.012, 0.045, f["frame"], segs=16)
    cylinder(g, hub + n * 0.012, hub + n * 0.018, 0.033, f["trim"], segs=16)
    cylinder(g, hub + n * 0.018, hub + n * 0.021, 0.02, "teal" if level < 5 else "cyan_glow", segs=14)


def along(d, x_hint):
    """A right-handed 3x3 frame with its y along `d` and its x as near `x_hint` as can be."""
    y = Vector(d).normalized()
    x = Vector(x_hint)
    x = (x - y * x.dot(y)).normalized()
    return Matrix((x, y, x.cross(y))).transposed()


def bend(points, radius, steps=4):
    """A polyline with rounded corners (quadratic fillets), for tubes."""
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
# The saddles
# =====================================================================================================

def saddle_sections(hips_y, top, driver):
    """The saddle: a wide seat at the back with a raised lip, narrowing to a nose in front."""
    back = hips_y - 0.2
    nose = hips_y + (0.24 if driver else 0.2)
    keys = [
        (back, 0.0, 0.135, top - 0.06, top + 0.035),
        (back + 0.05, 0.0, 0.16, top - 0.065, top + 0.02),
        (hips_y, 0.0, 0.165, top - 0.07, top),
        (hips_y + 0.1, 0.0, 0.12, top - 0.07, top - 0.006),
        (nose - 0.04, 0.0, 0.075, top - 0.07, top - 0.012),
        (nose, 0.0, 0.05, top - 0.065, top - 0.022),
    ]
    return K.smooth_sections(keys, 3)


def build_saddle(mats, level, driver):
    f = K.FINISH[level]
    sp = saddle_spec(driver)
    hx, hy, hz = sp["half"]
    floor, hips_y, top = sp["floor"], sp["hips_y"], sp["top"]
    name = "DriversSaddle" if driver else "Saddle"
    root = K.make_empty(name)
    rig = {"root": root}
    plate_top = -hz + 0.025

    g = Geo()
    plinth(g, hx - 0.02, -hy + 0.02, hy - 0.02, -hz, plate_top, f, level)
    # footboards on brackets either side, their tops the floor the feet rest on
    fb_y = (hips_y - 0.1, hips_y + 0.25) if driver else (hips_y - 0.09, hips_y + 0.24)
    for sx in (-1, 1):
        cx = sx * 0.235
        box(g, (cx, sum(fb_y) / 2, floor - 0.008), (0.085, (fb_y[1] - fb_y[0]) / 2, 0.008), f["frame"], bevel=0.004)
        for k in range(4):
            y = fb_y[0] + 0.04 + k * (fb_y[1] - fb_y[0] - 0.08) / 3
            box(g, (cx, y, floor + 0.001), (0.07, 0.006, 0.002), "rubber")
        if floor - 0.016 > plate_top:
            for y in fb_y:
                box(g, (cx, y + (0.03 if y == fb_y[0] else -0.03), (plate_top + floor - 0.016) / 2), (0.02, 0.015, (floor - 0.016 - plate_top) / 2),
                    f["dark"], bevel=0.004)
    # the body between the legs, from the plate up under the saddle
    body_top = top - 0.06
    body_y = (hips_y - 0.16, hips_y + (0.44 if driver else 0.2))
    keys = [(body_y[0], 0, 0.08, plate_top, body_top - 0.01), (hips_y, 0, 0.1, plate_top, body_top),
            (body_y[1] - 0.06, 0, 0.095, plate_top, body_top - (0.12 if driver else 0.02)), (body_y[1], 0, 0.07, plate_top, body_top - (0.17 if driver else 0.05))]
    K.squircle_loft(g, K.smooth_sections(keys, 3), f["frame"], ring_pts=20, power=6.0)
    if driver:
        # the tank ahead of the saddle's nose, the riser rising out of it: a dark hump with a filler cap
        tank = [(hips_y + 0.2, 0, 0.09, body_top - 0.08, body_top + 0.0), (hips_y + 0.27, 0, 0.115, body_top - 0.1, body_top + 0.05),
                (hips_y + 0.37, 0, 0.11, body_top - 0.12, body_top + 0.055), (hips_y + 0.45, 0, 0.08, body_top - 0.15, body_top + 0.02)]
        K.squircle_loft(g, K.smooth_sections(tank, 3), f["dark"], ring_pts=20, power=3.5)
        cap = Vector((0, hips_y + 0.29, body_top + 0.05))
        cylinder(g, cap - Vector((0, 0, 0.01)), cap + Vector((0, 0, 0.012)), 0.026, f["trim"], segs=16)
        if level >= 2:
            for sx in (-1, 1):
                sweep(g, K.loft_edge(K.smooth_sections(tank, 3), 25, sx, 3.5), 0.004, "teal" if level < 5 else "cyan_glow", segs=6)
    # inset dark side panels and screws
    for sx in (-1, 1):
        box(g, (sx * 0.098, hips_y + 0.04, (plate_top + body_top) / 2), (0.004, 0.1, (body_top - plate_top) / 2 - 0.05), f["dark"], bevel=0.003)
        for y in (hips_y - 0.04, hips_y + 0.12):
            for z in (plate_top + 0.05, body_top - 0.05):
                K.screw(g, (sx * 0.1, y, z), (sx, 0, 0), 0.006, f["bolt"])
    # pips on the body's top ahead of the saddle (the driver's) or behind it (the passenger's)
    if driver:
        K.pips(g, level, (0, hips_y + 0.205, body_top + 0.001 - 0.05 * 0.0), (0, 0.022, 0), (0, 0, 1), f, r=0.0055)
    else:
        K.pips(g, level, (-0.022 * (level - 1) / 2, body_y[0] - 0.0, (plate_top + body_top) / 2), (0.022, 0, 0), (0, -1, 0), f, r=0.0055)
    K.link_box(g, (0, -hy + 0.075, plate_top), (0, -1, 0), f, scale=0.9)
    if level >= 3:
        # copper side covers over the body
        for sx in (-1, 1):
            K.squircle_loft(g, [(y, sx * 0.104, 0.006, z0, z1) for (y, z0, z1) in
                                ((hips_y - 0.06, body_top - 0.2, body_top - 0.05), (hips_y + 0.06, body_top - 0.22, body_top - 0.04),
                                 (hips_y + 0.14, body_top - 0.2, body_top - 0.06))], f["trim"], ring_pts=12, power=4.0)
    if level >= 2:
        # knee pads on the body's sides under the saddle's nose
        for sx in (-1, 1):
            K.squircle_loft(g, [(y, sx * 0.105, 0.012, z0, z1) for (y, z0, z1) in
                                ((hips_y + 0.12, body_top - 0.12, body_top - 0.02), (hips_y + 0.2, body_top - 0.13, body_top - 0.01),
                                 (hips_y + 0.27 if driver else hips_y + 0.18, body_top - 0.12, body_top - 0.03))], f["fabric"], ring_pts=14, power=3.0)
    if driver:
        handlebars(g, hips_y, floor, level, f, mats)
    else:
        grab_rail(g, hips_y, top, level, f)
    if level >= 4:
        # crash bars round the body's front
        for sx in (-1, 1):
            pts = [Vector((sx * 0.07, hips_y + 0.06, plate_top)), Vector((sx * 0.2, hips_y + 0.12, plate_top + 0.08)),
                   Vector((sx * 0.21, hips_y + 0.2, body_top - 0.12)), Vector((sx * 0.09, hips_y + 0.24, body_top - 0.04))]
            sweep(g, bend(pts, 0.05), 0.012, f["frame"], segs=12)
            cylinder(g, pts[1] + Vector((0, -0.012, 0)), pts[1] + Vector((0, 0.012, 0)), 0.018, f["trim"], segs=14)
    K.make_object("Base", g, mats, root)
    if level >= 4:
        running_light(root, mats, "RunningLight", (0, -hy + 0.02 - 0.003, plate_top - 0.01), (hx - 0.1, 0.002, 0.004))
    if level >= 5:
        for sx in (-1, 1):
            running_light(root, mats, f"UnderGlow{'R' if sx > 0 else 'L'}", (sx * 0.104, hips_y + 0.04, plate_top + 0.012), (0.002, 0.16, 0.003))
    if driver and level >= 3:
        sg = Geo()
        box(sg, (0, 0, 0), (0.036, 0.001, 0.016), "cyan_glow")
        K.make_object("DashScreen", sg, mats, root, g.dash_at)

    # ---- Shell and Cushion: the saddle (no back) ----
    shell = rig["shell"] = K.make_empty("Shell", root, (0, 0, body_top))
    rig["shell_at"] = (0, 0, body_top)
    sg = Geo()
    secs = saddle_sections(hips_y, top, driver)
    pan_bottom = top - 0.07
    K.squircle_loft(sg, [(y, xc, hw + 0.006, zb - body_top - 0.012, zb - body_top + 0.012) for (y, xc, hw, zb, zt) in secs],
                    f["dark"], ring_pts=20, power=5.0)
    K.make_object("SaddlePan", sg, mats, shell)
    c = Geo()
    local = [(y, xc, hw, zb - pan_bottom, zt - pan_bottom) for (y, xc, hw, zb, zt) in secs]
    K.squircle_loft(c, local, f["fabric"], ring_pts=24, power=3.2)
    if level >= 2:
        for sx in (-1, 1):
            sweep(c, K.loft_edge(local, 40, sx, 3.2), 0.0042, "piping" if level < 5 else "cyan_glow", segs=6)
    if level >= 3:
        for k in range(3):
            box(c, (0, hips_y - 0.12 + k * 0.09, 0.0705 + 0.002 * (k == 0)), (0.11 - 0.015 * k, 0.0025, 0.003), "hole")
    rig["cushion"] = K.make_object("Cushion", c, mats, shell, (0, 0, pan_bottom - body_top))
    rig["pan_sink"] = 0.14
    return rig


def handlebars(g, hips_y, floor, level, f, mats):
    """The riser from the body's front up to a bar with rubber grips where the sit_saddle clip puts the hands, and a
    small dash pod under the bar's middle."""
    gx, gy, gz = SADDLE_GRIP
    grip_y, grip_z = hips_y + gy, floor + gz
    clamp = Vector((0, grip_y - 0.035, grip_z - 0.03))
    base = Vector((0, hips_y + 0.37, floor + 0.42))
    cylinder(g, base, clamp, 0.026, f["frame"], segs=18)
    for sx in (-1, 1):
        cylinder(g, base + Vector((sx * 0.035, -0.02, -0.1)), clamp + Vector((sx * 0.03, -0.01, -0.02)), 0.012, f["dark"], segs=12)
    box(g, tuple(clamp), (0.04, 0.025, 0.022), f["trim"], bevel=0.006)
    bar = [Vector((-gx - 0.06, grip_y, grip_z)), Vector((-gx + 0.04, grip_y, grip_z)), Vector((-0.1, grip_y - 0.03, grip_z - 0.025)),
           Vector((0, grip_y - 0.035, grip_z - 0.03)), Vector((0.1, grip_y - 0.03, grip_z - 0.025)),
           Vector((gx - 0.04, grip_y, grip_z)), Vector((gx + 0.06, grip_y, grip_z))]
    sweep(g, bend(bar, 0.05), 0.011, "steel" if level < 5 else "chrome", segs=12)
    for sx in (-1, 1):
        a = Vector((sx * (gx - 0.055), grip_y, grip_z))
        b = Vector((sx * (gx + 0.065), grip_y, grip_z))
        cylinder(g, a, b, 0.017, "rubber", segs=14)
        cylinder(g, b, b + Vector((sx * 0.01, 0, 0)), 0.02, f["trim"], segs=14)
        if level >= 3:
            # hand guards
            pts = [a + Vector((sx * -0.01, 0.02, 0)), a + Vector((sx * 0.03, 0.07, 0.01)), b + Vector((0, 0.07, 0.01)), b + Vector((sx * 0.012, 0.01, 0))]
            sweep(g, bend(pts, 0.03), 0.008, f["frame"], segs=10)
    # the dash pod with its screen toward the rider
    pod = clamp + Vector((0, -0.045, -0.075))
    box(g, tuple(pod), (0.055, 0.035, 0.032), f["frame"], bevel=0.01)
    box(g, tuple(pod + Vector((0, -0.033, 0.008))), (0.042, 0.003, 0.02), f["trim"], bevel=0.0015)
    box(g, tuple(pod + Vector((0, -0.036, 0.008))), (0.036, 0.0015, 0.016), "screen")
    g.dash_at = tuple(pod + Vector((0, -0.0385, 0.008)))


def grab_rail(g, hips_y, top, level, f):
    """A rail looping behind the passenger's saddle to hold on to."""
    yb = hips_y - 0.26
    z = top - 0.02
    pts = [Vector((-0.11, hips_y - 0.02, top - 0.075)), Vector((-0.15, hips_y - 0.12, z)), Vector((-0.12, yb, z + 0.03)),
           Vector((0.12, yb, z + 0.03)), Vector((0.15, hips_y - 0.12, z)), Vector((0.11, hips_y - 0.02, top - 0.075))]
    sweep(g, bend(pts, 0.06), 0.013, "steel" if level < 5 else "chrome", segs=12)
    for sx in (-1, 1):
        p = pts[0] if sx < 0 else pts[-1]
        cylinder(g, p + Vector((0, 0, -0.012)), p + Vector((0, 0, 0.012)), 0.02, f["trim"], segs=14)
