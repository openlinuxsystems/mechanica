"""The Salvager Tech seats and saddles of the parts map's batch 4, at their five levels (see part_kit.py).

* ``build_seat(level, driver)`` - the Driver's Seat (4 x 4 x 4 blocks) and the Passenger Seat (4 x 4 x 3): a vehicle
  seat, one moulded bucket shell (side bolsters on the pan and the back, the back reclined 15 degrees, the headrest part
  of the shell) mounted low on a pair of seat rails over a framed base plinth. The driver's has a firewall panel at
  the plinth's front, the steering column out of it at a vehicle's angle, a gauge pod on the column and pedals on the
  floor.
* ``build_saddle(level, driver)`` - the Driver's Saddle (3 x 4 x 4) and the Saddle (3 x 3 x 3): a saddle on a
  narrow body ridden astride, footboards either side; the driver's has handlebars, the passenger's a grab rail.

Every level keeps the seat point and the envelope and adds to the look (Chase's rule: each level looks cooler):

1. worn gunmetal and hull dark, charcoal fabric, copper screws, one copper pip; the seats: a lap belt, the rails on
   two stamped risers;
2. teal piping on the cushions and taller bolsters (a saddle: knee pads), a copper buckle and slider bar, rubber
   pedal pads and a heel plate, two pips;
3. steel frame, quilting seams (a saddle: copper side covers); the seats: a welded box subframe, a four-point harness
   through slots in the shell, an outboard grab handle, head wings, the dash screen lit cyan, three pips;
4. a roll cage with copper collars, its hoop round the seat back and side bars into the subframe, a five-point
   harness (a saddle: crash bars), a cyan running light, four pips;
5. black chrome and polished copper, black quilted fabric stitched in cyan, a harness bar and lamp on the cage, cyan
   under-glow, five lit cyan pips.

Seat points (Blender: x right, y forward, z up; the part data's ``seat`` uses game axes x, z up, -y):

* the seats: feet on the plinth (``DECK``), the cushion 0.32 m above it, a vehicle seat's height: lower than the
  ``sit`` clip's chair, so the game eases it a third of the way to ``sit_low`` (legs out toward the pedals);
* the saddles: feet on the footboards (``FLOOR``), the cushion 0.528 m above them, where ``sit_saddle``'s hips
  (0.668 m, ``SADDLE_HIPS`` in tools/characters/build_characters.py) less 0.14 m land; the driver's handlebar grips
  are at ``SADDLE_GRIP`` from there.
"""

from __future__ import annotations

import math

import bmesh
from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, dome, sweep

# ---- the bucket seats -------------------------------------------------------------------------------------------
DECK = -0.44                 # top of the base plinth: the floor the feet rest on
CUSHION_H = 0.32             # cushion above the deck: a vehicle seat, lower than a chair (the game blends sit and sit_low)
RECLINE = 15.0               # the shell's back, degrees back from upright
SHELL_TOP = 0.41             # the top of the shell's headrest
RAIL_X = 0.17                # the seat rails either side of the middle
HOOP_X, HOOP_BACK = 0.31, 0.29   # the roll cage's hoop: half its width, and how far behind the hips it stands
WHEEL_R = 0.19
COLUMN_TILT = 40.0           # degrees from upright; the wheel is square to the column (the scrap seat's)
# The hub forward of and above the cushion point, where the hands land: the sit clip's (0.347, 0.319) eased toward
# sit_low's for this cushion (both keep the grip in the chest's space; sit_low's hands are 0.028 m back, 0.016 m up).
HUB_FROM_HIPS = (0.338, 0.324)

# ---- the saddles ------------------------------------------------------------------------------------------------
SADDLE_HIPS = 0.668          # tools/characters/build_characters.py (measured), character.json clip_hips.sit_saddle
HIPS_ABOVE_CUSHION = 0.14    # character.json seat.hips_above_cushion
SADDLE_GRIP = (0.26, 0.42, 0.92)   # handlebar grips (fist centres): out, forward and up from the feet under the hips


def seat_spec(driver):
    """The seat's dimensions: half size, the cushion point (y) and the cushion top (z)."""
    half = (0.5, 0.5, 0.5) if driver else (0.5, 0.375, 0.5)
    hips_y = -0.14 if driver else 0.0
    return {"half": half, "hips_y": hips_y, "top": DECK + CUSHION_H}


def saddle_spec(driver):
    half = (0.375, 0.5, 0.5) if driver else (0.375, 0.375, 0.375)
    floor = -0.45 if driver else -0.345
    hips_y = -0.12 if driver else -0.03
    return {"half": half, "floor": floor, "hips_y": hips_y, "top": floor + SADDLE_HIPS - HIPS_ABOVE_CUSHION}


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def plinth(g, half_x, y0, y1, z0, z1, f, level, light=False):
    """A framed base plinth: a dark plate in gunmetal rails with corner blocks, bolts on the corners (`light`: chamfered
    in one segment, lighter bolts, for the seats' triangle budget)."""
    box, screw = (cbox, bolt) if light else (globals()["box"], K.screw)
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
            screw(g, (c[0], c[1], z1 + 0.002), (0, 0, 1), 0.0075, f["bolt"])
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

def cbox(g, c, half, mat, bevel=0.0, rot=None):
    """A box (turned by the 3x3 `rot`) with its edges chamfered by `bevel` in one segment: the seats' lighter box."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 2 * half[0], v.co.y * 2 * half[1], v.co.z * 2 * half[2]))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, 0.45 * min(half) * 2), segments=1,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.verts.ensure_lookup_table()
    verts = [tuple(v.co) for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    m = Matrix(rot) if rot is not None else Matrix.Identity(3)
    return g.add([tuple(m @ Vector(p) + Vector(c)) for p in verts], faces, mat)


def bolt(g, p, n=(0, 0, 1), r=0.0075, mat="copper"):
    """A light button-head bolt (six sides, no socket) for the seats."""
    p, n = Vector(p), Vector(n).normalized()
    cylinder(g, p - n * 0.001, p + n * r * 0.35, r, mat, segs=6, r1=r * 0.75)


def smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def seat_path(hips_y, top, step=0.035):
    """The bucket's centre line on its inner (rider's) face, from the pan's front lip back round the corner and up the
    reclined back to the headrest's top, evenly spaced: [{"p": point, "n": normal toward the rider, "h": height above
    the cushion point}]."""
    H, C = hips_y, top
    r = math.radians(RECLINE)
    base = Vector((0, H - 0.16, C - 0.075))
    up = Vector((0, -math.sin(r), math.cos(r)))
    tip = base + up * ((SHELL_TOP - 0.012 - base.z) / math.cos(r))
    keys = [Vector((0, H + 0.30, C - 0.03)), Vector((0, H + 0.15, C - 0.06)), Vector((0, H - 0.02, C - 0.075)), base, tip]
    curve = bend(keys, 0.12, steps=10)
    lengths = [0.0]
    for a, b in zip(curve, curve[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    n = max(2, round(lengths[-1] / step))
    pts, j = [], 0
    for k in range(n + 1):
        s = lengths[-1] * k / n
        while j < len(curve) - 2 and lengths[j + 1] < s:
            j += 1
        t = (s - lengths[j]) / max(lengths[j + 1] - lengths[j], 1e-9)
        pts.append(curve[j].lerp(curve[j + 1], min(max(t, 0.0), 1.0)))
    x = Vector((1, 0, 0))
    out = []
    for i, p in enumerate(pts):
        tan = (pts[min(i + 1, n)] - pts[max(i - 1, 0)]).normalized()
        out.append({"p": p, "n": tan.cross(x).normalized(), "h": p.z - C, "y": p.y})
    return out


def _wing(u, hw):
    return smoothstep((abs(u) / hw - 0.45) / 0.55)


def section_n(u, v, sec):
    """The offset toward the rider of a section's point (u across, v up its squircle): the side wings rise toward the
    rider, the inner face by `wing_in`, the outer by `wing_out` (so the wall keeps its thickness)."""
    hw, n0, n1, win, wout = sec
    hh = (n1 - n0) / 2
    k = (v + hh) / (2 * hh)
    return (n0 + n1) / 2 + v + _wing(u, hw) * (wout + (win - wout) * k)


def bucket_loft(g, path, secs, mat, ring=32, power=4.0, matrix=None):
    """A moulded solid along `path` (seat_path points): each section a squircle `hw` wide across x and from `n0` to
    `n1` along the point's normal, its sides raised toward the rider (section_n). Capped at both ends."""
    x = Vector((1, 0, 0))
    verts, faces = [], []
    for pt, sec in zip(path, secs):
        hw, n0, n1 = sec[:3]
        for u, v in K.squircle(ring, hw, (n1 - n0) / 2, power):
            verts.append(pt["p"] + x * u + pt["n"] * section_n(u, v, sec))
    m = len(path)
    for i in range(m - 1):
        for k in range(ring):
            k1 = (k + 1) % ring
            a, b = i * ring + k, i * ring + k1
            faces.append((b, b + ring, a + ring, a))     # wound outward (the path's frame is left-handed)
    faces.append(tuple(reversed(range(ring))))
    faces.append(tuple((m - 1) * ring + k for k in range(ring)))
    if matrix is not None:
        verts = [matrix @ v for v in verts]
    g.add(verts, faces, mat)


def surface(pt, sec, u, lift=0.0, power=4.0, top=True):
    """The point on a bucket_loft section's inner (or outer) face at `u` across, `lift` off it."""
    hw, n0, n1 = sec[:3]
    hh = (n1 - n0) / 2
    a = min(abs(u) / hw, 0.999)
    v = hh * (1 - a ** power) ** (1 / power) * (1 if top else -1)
    n = section_n(u, v, sec) + (lift if top else -lift)
    return pt["p"] + Vector((u, 0, 0)) + pt["n"] * n


def nearest(path, idx, y=None, h=None):
    """The index in `idx` whose point is nearest the given y (along the pan) or height (up the back)."""
    if y is not None:
        return min(idx, key=lambda i: abs(path[i]["y"] - y))
    return min(idx, key=lambda i: abs(path[i]["h"] - h))


def shell_sec(pt, hips_y, level):
    """The moulded shell: (half width, n0, n1, wing in, wing out) at a path point."""
    h, y = pt["h"], pt["y"]
    b = smoothstep((h + 0.06) / 0.08)                 # 0 on the pan, 1 up the back
    pan_hw = 0.215 + 0.025 * smoothstep((hips_y + 0.30 - y) / 0.2)
    back_hw = 0.245 - 0.09 * smoothstep((h - 0.27) / 0.08)
    hw = pan_hw + (back_hw - pan_hw) * b
    top_h = SHELL_TOP - 0.012 - (pt["p"].z - h)       # the shell top's height above the cushion point
    k = smoothstep((h - (top_h - 0.05)) / 0.05)
    hw *= 1 - 0.3 * k * k
    pan_w = 0.05 + 0.025 * smoothstep((hips_y + 0.2 - y) / 0.3)
    head_w = 0.045 if level >= 3 else 0.025
    back_w = 0.095 + (0.025 - 0.095) * smoothstep((h - 0.22) / 0.11)
    back_w += (head_w - 0.025) * smoothstep((h - 0.34) / 0.04)
    w = (pan_w + (back_w - pan_w) * b) * (0.7 if level == 1 else 1.0)
    return (hw, -0.024, 0.0, w, max(w - 0.014, 0.0))


def strap(g, pts, normal, mat="webbing", half_w=0.02, t=0.0026):
    """Flat webbing through `pts`, lying on a surface facing `normal`."""
    for a, b in zip(pts, pts[1:]):
        d = b - a
        if d.length < 1e-4:
            continue
        rot = along(d, Vector(normal).cross(d))
        cbox(g, tuple((a + b) / 2), (half_w, d.length / 2 + 0.002, t), mat, rot=rot)


def seat_mount(g, hips_y, level, f):
    """What the shell sits on: a pair of seat rails on two risers (levels 1 and 2), or (3 and up) on a welded box
    subframe with outriggers for the roll cage. Returns the rails' top height."""
    H = hips_y
    rz = DECK + 0.15
    y0, y1 = H - 0.17, H + 0.27
    slider = "chrome" if level >= 5 else "steel"
    for sx in (-1, 1):
        x = sx * RAIL_X
        cbox(g, (x, (y0 + y1) / 2, rz + 0.011), (0.022, (y1 - y0) / 2, 0.011), f["frame"], bevel=0.003)
        cbox(g, (x, (y0 + y1) / 2 + 0.015, rz + 0.031), (0.014, (y1 - y0) / 2 - 0.035, 0.009), slider, bevel=0.002)
        for yy in (y0 + 0.004, y1 - 0.004):
            cbox(g, (x, yy, rz + 0.012), (0.025, 0.005, 0.014), f["dark"], bevel=0.002)
    for yr in (H + 0.20, H - 0.10):
        # a riser under the rails, flanged and bolted down
        if level >= 3:
            for sx in (-1, 1):
                cbox(g, (sx * 0.19, yr, (DECK + 0.04 + rz) / 2), (0.016, 0.02, (rz - DECK - 0.04) / 2), f["frame"], bevel=0.004)
            cbox(g, (0, yr, rz - 0.012), (0.21, 0.022, 0.012), f["frame"], bevel=0.004)
            cbox(g, (0, yr, (DECK + 0.04 + rz) / 2 - 0.01), (0.17, 0.004, (rz - DECK - 0.06) / 2 - 0.01), f["dark"], bevel=0.002)
        else:
            cbox(g, (0, yr, DECK + 0.075), (0.205, 0.02, 0.075), f["dark"], bevel=0.006)
            cbox(g, (0, yr, DECK + 0.004), (0.215, 0.04, 0.004), f["frame"], bevel=0.002)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    bolt(g, (sx * 0.185, yr + sy * 0.03, DECK + 0.008), (0, 0, 1), 0.0065, f["bolt"])
            if level >= 2:
                for x in (-0.11, 0.0, 0.11):
                    cylinder(g, (x, yr + 0.019, DECK + 0.08), (x, yr + 0.0215, DECK + 0.08), 0.03, "hole", segs=12)
    if level == 2:
        # the slider bar under the pan's front
        bar = [Vector((-RAIL_X, y1 - 0.02, rz + 0.03)), Vector((-RAIL_X + 0.02, y1 + 0.05, rz + 0.03)),
               Vector((RAIL_X - 0.02, y1 + 0.05, rz + 0.03)), Vector((RAIL_X, y1 - 0.02, rz + 0.03))]
        sweep(g, bend(bar, 0.03), 0.007, f["trim"], segs=8)
    if level >= 3:
        # the welded box subframe on the plinth: side beams, cross tubes (the rear one out to the cage's feet), braces
        yb, yf = H - HOOP_BACK, H + 0.30
        tube = 0.019
        for sx in (-1, 1):
            cbox(g, (sx * 0.27, (yb + yf) / 2, DECK + tube), (tube, (yf - yb) / 2, tube), f["frame"], bevel=0.005)
            cylinder(g, (sx * 0.27, yf - 0.02, DECK + 0.035), (sx * RAIL_X, y1 - 0.03, rz), 0.01, f["frame"], segs=10)
            cylinder(g, (sx * 0.27, yb + 0.03, DECK + 0.035), (sx * RAIL_X, y0 + 0.03, rz), 0.01, f["frame"], segs=10)
        for yy, wx in ((yf, 0.27), (H + 0.20, 0.27), (H - 0.10, 0.27), (yb, 0.33)):
            cbox(g, (0, yy, DECK + tube), (wx + tube, tube * 0.9, tube * 0.9), f["frame"], bevel=0.005)
        for sx in (-1, 1):
            for yy in (yb, yf):
                cbox(g, (sx * 0.27, yy, DECK + 0.003), (0.035, 0.035, 0.003), f["dark"], bevel=0.002)
                bolt(g, (sx * 0.27 + sx * 0.022, yy + 0.022, DECK + 0.006), (0, 0, 1), 0.006, f["bolt"])
    return rz + 0.04


def dash(g, hub, level, f, hy):
    """The driver's station at the plinth's front: a firewall panel on two legs with a cowl lip, the column out of it
    at the vehicle angle to the wheel, a gauge pod on the column, pedals and a heel plate on the floor. Returns the
    gauge screen's centre and normal."""
    t = math.radians(COLUMN_TILT)
    down = Vector((0, math.sin(t), -math.cos(t)))
    fy = hy - 0.045                                   # the firewall's back face
    land = hub + down * ((fy - hub.y) / down.y)
    pz0, pz1 = -0.30, -0.085                          # the panel's bottom and top: the knees clear its top
    cbox(g, (0, fy + 0.0125, (pz0 + pz1) / 2), (0.30, 0.0125, (pz1 - pz0) / 2), f["dark"], bevel=0.006)
    for sx in (-1, 1):
        cbox(g, (sx * 0.285, fy + 0.012, (DECK + pz0) / 2 + 0.01), (0.018, 0.016, (pz0 - DECK) / 2 + 0.01), f["frame"], bevel=0.004)
        cbox(g, (sx * 0.285, fy + 0.012, DECK + 0.004), (0.035, 0.03, 0.004), f["frame"], bevel=0.002)
        bolt(g, (sx * 0.285, fy - 0.01, DECK + 0.008), (0, 0, 1), 0.006, f["bolt"])
        # the panel's frame edges
        cbox(g, (sx * 0.3, fy + 0.012, (pz0 + pz1) / 2), (0.012, 0.016, (pz1 - pz0) / 2 + 0.004), f["frame"], bevel=0.004)
    cbox(g, (0, fy + 0.012, pz0), (0.31, 0.016, 0.01), f["frame"], bevel=0.004)
    # the cowl lip over the panel, sloping down toward the driver
    tilt = Matrix.Rotation(math.radians(-14), 3, "X")
    cbox(g, (0, fy - 0.012, pz1 + 0.01), (0.315, 0.042, 0.01), f["frame"], bevel=0.005, rot=tilt)
    if level >= 3:
        for sx in (-1, 1):
            cbox(g, (sx * 0.215, fy - 0.0005, -0.2), (0.05, 0.002, 0.05), f["trim"], bevel=0.0015)
    # the column: a boot in the panel, the tube, a collar under the wheel
    cylinder(g, land - down * 0.045, land - down * 0.008, 0.04, f["trim"], segs=14)
    cylinder(g, land, hub, 0.022, f["dark"], segs=12)
    cylinder(g, hub + down * 0.03, hub + down * 0.085, 0.03, f["frame"], segs=14)
    if level >= 2:
        cylinder(g, land - down * 0.075, land - down * 0.045, 0.03, "rubber", segs=12)
    wheel(g, hub, -down, level, f)
    # the gauge pod on top of the column under the wheel (between the knees), its face toward the driver
    face_n = -down
    w_up = face_n.cross(Vector((1, 0, 0))).normalized()      # up the wheel's face
    pod = hub + down * 0.16 + w_up * 0.032
    rot = along(w_up, (1, 0, 0))
    cbox(g, tuple(pod), (0.055, 0.03, 0.024), f["frame"], bevel=0.008, rot=rot)
    cbox(g, tuple(pod + face_n * 0.025), (0.049, 0.026, 0.0015), f["trim"], bevel=0.001, rot=rot)
    cbox(g, tuple(pod + face_n * 0.0275), (0.042, 0.021, 0.0015), "screen", rot=rot)
    if level < 3:
        cylinder(g, pod + face_n * 0.028, pod + face_n * 0.031, 0.014, f["trim"], segs=12)
    # pedals: throttle under the right foot, brake under the left; a heel plate from level 2
    for x, w in ((0.12, 0.03), (-0.1, 0.042)):
        piv = Vector((x, fy - 0.02, DECK + 0.012))
        cylinder(g, piv - Vector((0.03, 0, 0)), piv + Vector((0.03, 0, 0)), 0.01, f["frame"], segs=10)
        cbox(g, (x, fy - 0.02, DECK + 0.004), (0.036, 0.018, 0.004), f["dark"], bevel=0.002)
        a = math.radians(24)
        d = Vector((0, -math.cos(a), math.sin(a)))
        nrm = Vector((0, math.sin(a), math.cos(a)))
        L = 0.11
        rot = along(d, (1, 0, 0))
        cbox(g, tuple(piv + d * (L / 2)), (w, L / 2, 0.006), f["frame"], bevel=0.003, rot=rot)
        if level >= 2:
            cbox(g, tuple(piv + d * (L / 2 + 0.01) + nrm * 0.0065), (w - 0.005, L / 2 - 0.015, 0.002), "rubber", rot=rot)
    if level >= 2:
        cbox(g, (0, fy - 0.2, DECK + 0.003), (0.2, 0.06, 0.003), f["dark"], bevel=0.002)
        for k in range(5):
            cbox(g, (0, fy - 0.245 + k * 0.022, DECK + 0.0065), (0.18, 0.005, 0.0015), "rubber")
    return pod + face_n * 0.0295, face_n, rot


def build_seat(mats, level, driver):
    f = K.FINISH[level]
    sp = seat_spec(driver)
    hx, hy, hz = sp["half"]
    H, C = sp["hips_y"], sp["top"]
    name = "DriversSeat" if driver else "PassengerSeat"
    root = K.make_empty(name)
    rig = {"root": root}
    path = seat_path(H, C)
    idx = range(len(path))
    shell_secs = [shell_sec(p, H, level) for p in path]
    pan = [i for i in idx if path[i]["y"] > H - 0.12 and path[i]["h"] < -0.035]
    back = [i for i in idx if path[i]["y"] < H - 0.15 and -0.02 < path[i]["h"] < 0.3]
    head = [i for i in idx if path[i]["h"] > 0.355 and path[i]["p"].z < SHELL_TOP - 0.03]

    # ---- Base: plinth, rails or subframe, link box, pips, and (driver) the firewall, column, wheel and pedals ----
    g = Geo()
    plinth(g, hx - 0.03, -hy + 0.03, hy - 0.03, -hz, DECK, f, level, light=True)
    rail_top = seat_mount(g, H, level, f)
    K.pips(g, level, (-0.04 * (level - 1) / 2, hy - 0.03 - 0.0005, (DECK - hz) / 2), (0.04, 0, 0), (0, 1, 0), f)
    K.link_box(g, (-(hx - 0.13), hy - 0.12, DECK), (0, 1, 0), f)
    screen = None
    if driver:
        hub = Vector((0, H + HUB_FROM_HIPS[0], C + HUB_FROM_HIPS[1]))
        screen = dash(g, hub, level, f, hy)
    K.make_object("Base", g, mats, root)
    if screen is not None and level >= 3:
        sg = Geo()
        cbox(sg, (0, 0, 0), (0.038, 0.018, 0.001), "cyan_glow", rot=screen[2])
        K.make_object("DashScreen", sg, mats, root, tuple(screen[0]))
    if level >= 4:
        running_light(root, mats, "RunningLight", (0, hy - 0.03 + 0.003, DECK - 0.012), (hx - 0.12, 0.002, 0.004))
    if level >= 5:
        for sx in (-1, 1):
            running_light(root, mats, f"UnderGlow{'R' if sx > 0 else 'L'}", (sx * (hx - 0.03 + 0.003), 0, DECK - 0.02),
                          (0.002, hy - 0.1, 0.004))

    # ---- Shell (moves with the clips): the moulded bucket with its headrest, the brackets onto the rails, belt anchors
    shell = rig["shell"] = K.make_empty("Shell", root, (0, 0, DECK))
    rig["shell_at"] = (0, 0, DECK)
    to_shell = Matrix.Translation((0, 0, -DECK))
    s = Geo()
    bucket_loft(s, path, shell_secs, f["dark"], ring=20, power=4.0)
    x_axis = Vector((1, 0, 0))
    for yy in (H + 0.18, H - 0.07):
        i = nearest(path, pan, y=yy)
        for sx in (-1, 1):
            under = surface(path[i], shell_secs[i], sx * RAIL_X, top=False)
            zt = under.z + 0.006
            cbox(s, (sx * RAIL_X, path[i]["y"], (rail_top + zt) / 2), (0.016, 0.026, (zt - rail_top) / 2), f["frame"], bevel=0.004)
    # belt anchors on the shell's sides where the pan meets the back
    i_anchor = nearest(path, pan, y=H - 0.1)
    for sx in (-1, 1):
        hw = shell_secs[i_anchor][0]
        p = path[i_anchor]["p"] + Vector((sx * (hw - 0.012), 0, 0)) + path[i_anchor]["n"] * (shell_secs[i_anchor][3] * 0.75)
        bolt(s, p + Vector((sx * 0.004, 0, 0)), (sx, 0, 0), 0.009, f["bolt"])
    if level >= 3:
        # harness slots through the shell at the shoulders
        i_slot = nearest(path, idx, h=0.335)
        pt = path[i_slot]
        tan = (path[i_slot + 1]["p"] - path[i_slot - 1]["p"]).normalized()
        rot = Matrix((x_axis, -tan, pt["n"])).transposed()
        for sx in (-1, 1):
            cbox(s, tuple(surface(pt, shell_secs[i_slot], sx * 0.075, lift=-0.004)), (0.026, 0.007, 0.006), "hole", rot=rot)
        # an outboard grab handle on the right of the pan, copper bosses
        hw = shell_secs[nearest(path, pan, y=H + 0.04)][0]
        hpts = [Vector((hw - 0.01, H + 0.13, C - 0.035)), Vector((hw + 0.045, H + 0.12, C + 0.015)), Vector((hw + 0.058, H + 0.04, C + 0.035)),
                Vector((hw + 0.045, H - 0.04, C + 0.015)), Vector((hw - 0.01, H - 0.05, C - 0.03))]
        sweep(s, bend(hpts, 0.04, steps=3), 0.0105, "chrome" if level >= 5 else "steel", segs=6)
        cylinder(s, Vector((hw + 0.058, H + 0.075, C + 0.035)), Vector((hw + 0.058, H + 0.005, C + 0.035)), 0.016, "rubber", segs=14)
        for e in (hpts[0], hpts[-1]):
            cylinder(s, e - Vector((0.012, 0, 0)), e + Vector((0.012, 0, 0)), 0.018, f["trim"], segs=14)
        # a copper screw on each of the shell's back wings
        for h in (0.12,):
            i = nearest(path, back, h=h)
            for sx in (-1, 1):
                p = surface(path[i], shell_secs[i], sx * shell_secs[i][0] * 0.97, top=False)
                bolt(s, p + Vector((sx * 0.004, 0, 0)), (sx, 0, 0), 0.0065, f["bolt"])
    sl = Geo()
    sl.extend(s, to_shell)
    K.make_object("ShellFrame", sl, mats, shell)

    piping = "piping" if level < 5 else "cyan_glow"
    seam = "hole"

    # ---- Cushion (sinks): the pan pad with its bolsters, piping, quilting and the lap belt ----
    i_h = nearest(path, pan, y=H)
    pad_t = C - path[i_h]["p"].z
    bol = 0.032 if level >= 2 else 0.012
    pad_secs = {}
    for i in pan:
        hw = shell_secs[i][0] * 0.8
        t = pad_t - (0.014 if i == pan[0] else 0.0)
        pad_secs[i] = (hw, -0.012, t, bol, 0.0)
    c = Geo()
    bucket_loft(c, [path[i] for i in pan], [pad_secs[i] for i in pan], f["fabric"], ring=16, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            sweep(c, [surface(path[i], pad_secs[i], sx * pad_secs[i][0] * 0.62, lift=0.003) for i in pan[1:]], 0.0045, piping, segs=5)
    if level >= 3:
        for yy in (H - 0.06, H + 0.06, H + 0.17):
            i = nearest(path, pan, y=yy)
            w = pad_secs[i][0] * 0.55
            sweep(c, [surface(path[i], pad_secs[i], u, lift=0.0005) for u in (-w, -w / 2, 0, w / 2, w)], 0.0025, seam, segs=4)
    # the lap belt across the pad from the anchors to a buckle in the middle
    up = Vector((0, 0, 1))
    for sx in (-1, 1):
        pts = []
        for u in (0.2, 0.16, 0.12, 0.08, 0.045, 0.02):
            yy = H - 0.10 + (0.2 - u) * 0.55
            i = nearest(path, pan, y=yy)
            pt = dict(path[i])
            pt["p"] = Vector((0, yy, pt["p"].z))
            pts.append(surface(pt, pad_secs[i], sx * u, lift=0.004))
        strap(c, pts, up)
    buckle = surface(path[nearest(path, pan, y=H + 0.0)], pad_secs[nearest(path, pan, y=H + 0.0)], 0.0, lift=0.006)
    if level >= 3:
        # the harness straps' lower ends to the buckle, and (level 4 up) the crotch strap from the pad's front
        for sx in (-1, 1):
            pts = []
            for yy, u in ((H - 0.13, 0.085), (H - 0.08, 0.07), (H - 0.03, 0.045)):
                i = nearest(path, pan, y=yy)
                pts.append(surface(path[i], pad_secs[i], sx * u, lift=0.0045))
            strap(c, pts + [buckle], up)
        if level >= 4:
            pts = [surface(path[nearest(path, pan, y=yy)], pad_secs[nearest(path, pan, y=yy)], 0.0, lift=0.0045)
                   for yy in (H + 0.24, H + 0.17, H + 0.1, H + 0.04)]
            strap(c, pts, up)
        cylinder(c, buckle - up * 0.003, buckle + up * 0.006, 0.033, "chrome" if level >= 5 else f["trim"], segs=14)
        cylinder(c, buckle + up * 0.006, buckle + up * 0.0085, 0.019, f["dark"], segs=10)
        cbox(c, tuple(buckle + up * 0.009), (0.012, 0.003, 0.0015), f["trim"])
    else:
        cbox(c, tuple(buckle), (0.032, 0.02, 0.007), "steel" if level == 1 else f["trim"], bevel=0.003)
        cbox(c, tuple(buckle + Vector((0.0, 0.0, 0.0065))), (0.012, 0.006, 0.0015), f["dark"])
    pan_bottom = min(v[2] for v in c.v)
    to_cushion = Matrix.Translation((0, 0, -pan_bottom))
    cl = Geo()
    cl.extend(c, to_cushion)
    rig["cushion"] = K.make_object("Cushion", cl, mats, shell, (0, 0, pan_bottom - DECK))

    # ---- Backrest (sinks toward the shell): the back pad, the headrest pad, piping, quilting and harness ----
    r = math.radians(RECLINE)
    bup = Vector((0, -math.sin(r), math.cos(r)))
    fwd = Vector((0, math.cos(r), math.sin(r)))
    origin = path[back[0]]["p"].copy()
    m = Matrix((Vector((-1, 0, 0)), bup, fwd)).transposed()
    back_secs = {}
    for i in back:
        h = path[i]["h"]
        t = 0.05 + 0.028 * math.sin(math.pi * min(max(h / 0.3, 0), 1)) - (0.012 if i == back[-1] else 0.0)
        back_secs[i] = (shell_secs[i][0] * 0.8, -0.012, t, 0.035 if level >= 2 else 0.014, 0.0)
    bk = Geo()
    bucket_loft(bk, [path[i] for i in back], [back_secs[i] for i in back], f["fabric"], ring=16, power=4.0)
    head_secs = {i: (0.105, -0.01, 0.04, 0.012 if level >= 3 else 0.0, 0.0) for i in head}
    if len(head) >= 2:
        bucket_loft(bk, [path[i] for i in head], [head_secs[i] for i in head], f["fabric"], ring=16, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            sweep(bk, [surface(path[i], back_secs[i], sx * back_secs[i][0] * 0.62, lift=0.003) for i in back], 0.0045, piping, segs=5)
    if level >= 3:
        for h in (0.08, 0.15, 0.22):
            i = nearest(path, back, h=h)
            w = back_secs[i][0] * 0.55
            sweep(bk, [surface(path[i], back_secs[i], u, lift=0.0005) for u in (-w, -w / 2, 0, w / 2, w)], 0.0025, seam, segs=4)
        # shoulder straps from the slots down the pad
        for sx in (-1, 1):
            i_slot = nearest(path, idx, h=0.335)
            pts = [surface(path[i_slot], shell_secs[i_slot], sx * 0.075, lift=0.002)]
            for h, u in ((0.28, 0.08), (0.2, 0.085), (0.12, 0.09), (0.04, 0.09), (0.0, 0.09)):
                i = nearest(path, back, h=h)
                pts.append(surface(path[i], back_secs[i], sx * u, lift=0.0045))
            strap(bk, pts, fwd)
            if level >= 4:
                # an adjuster on each strap at the chest
                i = nearest(path, back, h=0.16)
                p = surface(path[i], back_secs[i], sx * 0.087, lift=0.008)
                cbox(bk, tuple(p), (0.024, 0.008, 0.004), f["trim"], bevel=0.002, rot=m)
    to_back = (Matrix.Translation(origin) @ m.to_4x4()).inverted()
    bl_geo = Geo()
    bl_geo.extend(bk, to_back)
    ob = K.make_object("Backrest", bl_geo, mats, shell, tuple(origin - Vector((0, 0, DECK))))
    ob.rotation_mode = "XYZ"
    ob.rotation_euler = m.to_euler()
    rig["back"] = ob
    rig["back_axis"] = 2
    rig["pan_sink"], rig["back_sink"] = 0.16, 0.1

    # ---- the roll cage (level 4 and up), on the subframe: a hoop round the seat back, side bars down to the front ----
    if level >= 4:
        hg = Geo()
        yb = H - HOOP_BACK
        z0 = DECK + 0.036
        top_z = hz - 0.03
        pts = [Vector((-HOOP_X, yb, z0)), Vector((-HOOP_X, yb, top_z - 0.08)), Vector((-HOOP_X + 0.08, yb, top_z)),
               Vector((HOOP_X - 0.08, yb, top_z)), Vector((HOOP_X, yb, top_z - 0.08)), Vector((HOOP_X, yb, z0))]
        sweep(hg, bend(pts, 0.08, steps=4), 0.019, f["frame"], segs=8)
        joint_z = C + 0.26
        for sx in (-1, 1):
            a = Vector((sx * HOOP_X, yb, joint_z))
            b = Vector((sx * 0.27, H + 0.29, DECK + 0.03))
            cylinder(hg, a, b, 0.014, f["frame"], segs=8)
            for z in (DECK + 0.075, joint_z):
                cylinder(hg, (sx * HOOP_X, yb, z - 0.02), (sx * HOOP_X, yb, z + 0.02), 0.026, f["trim"], segs=10)
            cbox(hg, (sx * HOOP_X, yb, DECK + 0.041), (0.035, 0.035, 0.005), f["frame"], bevel=0.003)
            for k in (-1, 1):
                bolt(hg, (sx * HOOP_X + k * 0.022, yb + 0.022, DECK + 0.046), (0, 0, 1), 0.005, f["bolt"])
        if level >= 5:
            # a harness bar across the hoop behind the shoulders, and a lamp on the top bar
            cylinder(hg, (-HOOP_X, yb, C + 0.2), (HOOP_X, yb, C + 0.2), 0.014, f["frame"], segs=8)
            for sx in (-1, 1):
                cylinder(hg, (sx * (HOOP_X - 0.03), yb, C + 0.2), (sx * (HOOP_X - 0.005), yb, C + 0.2), 0.022, f["trim"], segs=10)
            cbox(hg, (0, yb - 0.026, top_z), (0.06, 0.008, 0.012), f["frame"], bevel=0.004)
        K.make_object("Hoop", hg, mats, root)
        if level >= 5:
            running_light(root, mats, "HoopLamp", (0, yb - 0.0355, top_z), (0.05, 0.0015, 0.007))
    return rig


def wheel(g, hub, normal, level, f):
    """The steering wheel at `hub`, facing `normal` (toward the driver): a rubber rim on three spokes, a hub cap."""
    n = Vector(normal).normalized()
    u = Vector((1, 0, 0))
    w = n.cross(u).normalized()           # up the wheel's face
    rim = []
    for k in range(21):
        a = 2 * math.pi * k / 20
        rim.append(hub + (u * math.cos(a) + w * math.sin(a)) * WHEEL_R)
    sweep(g, rim, 0.017, "rubber", segs=8)
    if level >= 2:
        # grip wraps at ten and two
        for a0 in (math.radians(140), math.radians(40)):
            pts = [hub + (u * math.cos(a0 + d) + w * math.sin(a0 + d)) * WHEEL_R for d in (-0.3, -0.1, 0.1, 0.3)]
            sweep(g, pts, 0.0195, "webbing" if level < 5 else "fabric_quilt", segs=8)
    spoke = "chrome" if level >= 5 else ("copper" if level >= 3 else f["frame"])
    for a in (math.radians(-90), math.radians(20), math.radians(160)):
        d = u * math.cos(a) + w * math.sin(a)
        a_, b_ = hub + d * 0.035, hub + d * (WHEEL_R - 0.012)
        mid, dd = (a_ + b_) / 2, (b_ - a_)
        rot = along(dd, dd.normalized().cross(n))
        cbox(g, tuple(mid - n * 0.004), (0.016, dd.length / 2, 0.006), spoke, bevel=0.003, rot=rot)
    cylinder(g, hub - n * 0.02, hub + n * 0.012, 0.045, f["frame"], segs=14)
    cylinder(g, hub + n * 0.012, hub + n * 0.018, 0.033, f["trim"], segs=14)
    cylinder(g, hub + n * 0.018, hub + n * 0.021, 0.02, "teal" if level < 5 else "cyan_glow", segs=12)


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
