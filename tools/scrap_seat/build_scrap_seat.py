"""Build the Scrap Driver's Seat's model, its seat clips and its icon (Scrap Driver's Seat Design Spec, 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/scrap_drivers_seat.glb`` - the seat, 4 x 4 x 6 blocks (1.0 m wide, 1.0 m high, 1.5 m deep),
  centred on its origin, facing glTF -Z (the game's forward). A low wooden pallet with light metal corner caps and
  strips; an open frame of bent, lightly rusted gray-brown pipe round the seat and up the back; a puffy olive-tan
  canvas seat pan and backrest with piped seams and a few clean, stitched patches; and in front a crude steering
  wheel of light pipe on a leaning pipe column with a collar, bolted to the pallet on a plate. A small junction box
  with two brass terminals on the pallet is where links plug in. The frame leans a little and the wheel sits a little
  crooked, as built from scrap.
* Four glTF clips, played by the game from the seat script's "clip" display (part data ``display.clips``; names
  ending in ``_loop`` repeat, the others hold their last frame):

  - ``enter`` (0.8 s, once): the cushions sink 1.4 cm (pan) and 1 cm (back) under the driver, the frame rattles and
    dies away, and the pallet creaks down half a centimetre and settles back.
  - ``seated_loop`` (2 s): the cushions stay sunk and breathe a little with the driver.
  - ``seated_rattle_loop`` (2 s, while an engine runs): the same, plus a low rattle of the frame (1 to 3 mm) and a
    fast shiver in the canvas.
  - ``exit`` (0.7 s, once): the cushions spring back, a last rattle, and the pallet creaks.

  The nodes the clips move: ``Base`` (the pallet), ``Frame`` (pipe and column, a child of Base), ``SeatPan``
  and ``Backrest`` (children of Frame). Their rest transforms are the empty seat.
* A ``steer`` clip (1 s) the game holds at the seat's ``wheel`` readout instead of playing (part data
  ``display.clips.scrub``): ``Steering`` (the wheel, a child of Frame pivoted at its hub) turns about the column from
  90 degrees left at 0 s to 90 degrees right at 1 s; the driver's hands ride round with it.
* ``mods/vanilla/textures/items/scrap_drivers_seat_icon.png`` - the seat's picture for the hotbar, the inventory,
  the creation hotbar and the Fabricator, 256 x 256 on a clear background, seen from the right and above so the wheel face shows.

Units are metres; in Blender Z is up and the front (the wheel) faces +Y, which the exporter turns into glTF -Z.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/scrap_seat/build_scrap_seat.py                  # writes the .glb and the icon
    python tools/scrap_seat/build_scrap_seat.py --render OUT_DIR # also renders the review sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only), so running it twice gives the same file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Euler, Matrix, Vector
from PIL import Image

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
for sub in ("", "../lift", "../rocks", "../soil"):
    sys.path.insert(0, str((HERE / sub).resolve()))
import build_lift as bl  # noqa: E402  (Geo and the box, cylinder, sweep and bolt helpers)
import build_rocks as br  # noqa: E402  (rounded boxes, images, materials, glb_info)
import lift_textures as lt  # noqa: E402
import rock_textures as rt  # noqa: E402
import seat_textures as st  # noqa: E402
from build_lift import Geo, box, cylinder, dome, hexnut, sweep  # noqa: E402

REPO = HERE.parents[1]
OUT = REPO / "mods" / "vanilla" / "meshes" / "parts" / "scrap_drivers_seat.glb"
ICON = REPO / "mods" / "vanilla" / "textures" / "items" / "scrap_drivers_seat_icon.png"
FPS = 60

# ---- Dimensions (metres, Blender axes: x right, y forward, z up; the part's box is x +-0.5, y +-0.75, z +-0.5) --
HALF = (0.5, 0.75, 0.5)
DECK = -0.355                       # top of the pallet's deck boards
R_PIPE = 0.021                      # frame pipe radius
Z_LOOP = DECK + 0.024               # centreline of the pipe loop lying on the deck
Z_PAN = DECK + 0.26                 # centreline of the pipe loop under the seat pan
LOOP_X, LOOP_Y = 0.30, (-0.68, 0.30)
PAN_Y = (-0.60, -0.04)
BACK_TOP = 0.452                    # centreline of the backrest frame's top bar
BACK_Y = (-0.60, -0.69)             # the backrest uprights' y at the loop and at the top (they lean back)
PAN_HALF = (0.27, 0.235, 0.055)     # seat pan cushion
PAN_AT = (0.0, -0.295, Z_PAN + R_PIPE + 0.015)    # its bottom centre (on a board over the pan frame)
BACK_HALF = (0.26, 0.05, 0.19)      # backrest cushion
BACK_LEAN = math.degrees(math.atan2(BACK_Y[0] - BACK_Y[1], BACK_TOP - Z_LOOP))
BACK_AT_Z = 0.27                    # height of the backrest cushion's middle
HIP = (0.0, -0.40, DECK)            # under the seated driver's hips, at the deck: the part data's seat offset
WHEEL_R = 0.19
COLUMN_TILT = 40.0                  # degrees from upright; the wheel is square to the column
HUB = Vector((0.0, -0.053, 0.354))  # the driver's hands (sit clip) land on the rim at ten and two
PAN_SINK, BACK_SINK = 0.014, 0.010  # how far the cushions sink under the driver


def column_dir():
    """From the hub down the column to the pallet (forward and down), leaning a little to the right."""
    t = math.radians(COLUMN_TILT)
    d = Vector((0.0, math.sin(t), -math.cos(t)))
    return (Matrix.Rotation(math.radians(-1.3), 3, "Y") @ d).normalized()


# =====================================================================================================
# Geometry helpers
# =====================================================================================================

def fillet_path(points, radius, closed=False, steps=7):
    """A polyline through `points` with every corner rounded (a quadratic curve `radius` from the corner)."""
    pts = [Vector(p) for p in points]
    n = len(pts)
    out = []
    corners = range(n) if closed else range(1, n - 1)
    if not closed:
        out.append(pts[0])
    for i in corners:
        a, p, b = pts[i - 1], pts[i], pts[(i + 1) % n]
        r = min(radius, (a - p).length * 0.45, (b - p).length * 0.45)
        s, e = p + (a - p).normalized() * r, p + (b - p).normalized() * r
        for k in range(steps + 1):
            t = k / steps
            out.append(s * (1 - t) ** 2 + p * 2 * t * (1 - t) + e * t * t)
    if closed:
        out.append(out[0].copy())
    else:
        out.append(pts[-1])
    return out


def resample(path, step=0.03):
    """Extra points along long straight runs, so a swept pipe bends smoothly and shades evenly."""
    path = [Vector(p) for p in path]
    out = [path[0]]
    for a, b in zip(path, path[1:]):
        k = max(1, int((b - a).length / step))
        out += [a.lerp(b, (i + 1) / k) for i in range(k)]
    return out


def pipe(g, points, r=R_PIPE, mat="pipe", segs=14):
    sweep(g, resample(points), r, mat, segs=segs)


def coupler(g, p, axis, r=R_PIPE + 0.006, length=0.05, mat="fitting"):
    """A pipe fitting (collar, tee or coupler) round the pipe at p along axis, with a bead at each end."""
    p, axis = Vector(p), Vector(axis).normalized()
    cylinder(g, p - axis * length / 2, p + axis * length / 2, r, mat, segs=16)
    for s in (-1, 1):
        cylinder(g, p + axis * (s * length / 2 - 0.004), p + axis * (s * length / 2), r + 0.0025, mat, segs=16)


def bolt(g, p, n=(0, 0, 1), r=0.0075):
    hexnut(g, p, n, r, 0.005, "bolt")
    dome(g, Vector(p) + Vector(n).normalized() * 0.005, n, r * 0.55, "bolt", segs=6)


def wood_box(g, c, half, long_axis, seed, mat="wood", turn=0.0):
    """A plank or block of pallet wood: a lightly bevelled box whose UVs run the grain along `long_axis`."""
    tmp = Geo()
    rot = Matrix.Rotation(math.radians(turn), 3, "Z") if turn else None
    box(tmp, (0, 0, 0), half, mat, bevel=0.002)
    rng = np.random.default_rng(seed)
    off = rng.uniform(0, 1, 2)
    uvs = []
    co = [Vector(v) for v in tmp.v]
    for f in tmp.f:
        pts = [co[i] for i in f]
        nrm = Vector((0, 0, 0))
        for i in range(len(pts)):
            nrm += pts[i].cross(pts[(i + 1) % len(pts)])
        k = int(np.argmax(np.abs(nrm)))
        others = [i for i in range(3) if i != k]
        if k == long_axis:          # end grain: the two cross axes, tight
            ua, va = others
            uvs.append([(p[ua] / 0.08 + off[0], p[va] / 0.08 + off[1]) for p in pts])
        else:
            cross = [i for i in others if i != long_axis][0]
            uvs.append([(p[long_axis] / st.WOOD_TILE[0] + off[0], p[cross] / st.WOOD_TILE[1] + off[1]) for p in pts])
    m = rot if rot is not None else Matrix.Identity(3)
    verts = [tuple(m @ v + Vector(c)) for v in co]
    g.add(verts, tmp.f, mat, uvs)


# =====================================================================================================
# The pallet (Base)
# =====================================================================================================

def build_base():
    g = Geo()
    rng = np.random.default_rng(7)
    hx, hy = HALF[0] - 0.012, HALF[1] - 0.012
    # bottom boards along y, blocks, stringer boards along x, deck boards along y
    for i, x in enumerate((-0.425, 0.0, 0.425)):
        wood_box(g, (x, 0, -0.49), (0.07, hy, 0.01), 1, 100 + i)
    for i, x in enumerate((-0.43, 0.0, 0.43)):
        for j, y in enumerate((-0.665, 0.0, 0.665)):
            wood_box(g, (x, y, -0.44), (0.065, 0.075, 0.04), 1, 110 + 3 * i + j)
    for j, y in enumerate((-0.665, 0.0, 0.665)):
        wood_box(g, (0, y, -0.39), (hx, 0.075, 0.01), 0, 130 + j)
    gap = 0.012
    w = (2 * hx - 5 * gap) / 6
    for i in range(6):
        x = -hx + w / 2 + i * (w + gap)
        dz = float(rng.uniform(-0.0015, 0.0012))
        dy = float(rng.uniform(-0.004, 0.0))
        wood_box(g, (x, dy, DECK - 0.0125 + dz), (w / 2, hy + dy * 0.5, 0.0125), 1, 140 + i, turn=float(rng.uniform(-0.3, 0.3)))
        for y in (-0.665, 0.0, 0.665):                  # nail heads over the stringers
            for s in (-1, 1):
                dome(g, (x + s * w * 0.28, y + float(rng.uniform(-0.02, 0.02)), DECK + dz), (0, 0, 1), 0.0042, "bolt", segs=6)

    # light metal: a cap over each corner's top and an L down its two outer sides, bolted; a strip at each end
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (hx - 0.06), sy * (hy - 0.065)
            box(g, (cx, cy, DECK + 0.002), (0.06, 0.065, 0.002), "plate", bevel=0.001)
            for bx, by in ((0.03, 0.03), (-0.03, -0.035)):
                bolt(g, (cx + sx * bx, cy + sy * by, DECK + 0.004))
            box(g, (sx * (hx + 0.002), cy, -0.42), (0.002, 0.065, 0.064), "plate", bevel=0.001)
            box(g, (cx, sy * (hy + 0.002), -0.42), (0.06, 0.002, 0.064), "plate", bevel=0.001)
            for z in (-0.37, -0.45):
                bolt(g, (sx * (hx + 0.004), cy, z), (sx, 0, 0), r=0.0065)
                bolt(g, (cx, sy * (hy + 0.004), z), (0, sy, 0), r=0.0065)
    for y in (-0.705, 0.705):
        box(g, (0, y, DECK + 0.0015), (0.375, 0.022, 0.0015), "plate", bevel=0.0008)
        for i in range(6):
            x = -hx + w / 2 + i * (w + gap)
            if abs(x) < 0.37:
                bolt(g, (x, y, DECK + 0.003), r=0.0055)
    return g


# =====================================================================================================
# The pipe frame, the steering column and the wheel (Frame)
# =====================================================================================================

def upright_y(z):
    t = (z - Z_LOOP) / (BACK_TOP - Z_LOOP)
    return BACK_Y[0] + (BACK_Y[1] - BACK_Y[0]) * t


def build_frame():
    g = Geo()
    x, (y0, y1) = LOOP_X, LOOP_Y
    # the loop on the deck, round the seat
    pipe(g, fillet_path([(-x, y0, Z_LOOP), (x, y0, Z_LOOP), (x, y1, Z_LOOP), (-x, y1, Z_LOOP)], 0.07, closed=True))
    # clamps holding it down: a band over the pipe and a bolted tab each side
    for cx, cy, along in ((x, -0.30, "y"), (-x, -0.30, "y"), (x, 0.14, "y"), (-x, 0.14, "y"), (0.15, y1, "x"),
                          (-0.15, y1, "x"), (0.0, y0, "x")):
        a = Vector((0, 1, 0)) if along == "y" else Vector((1, 0, 0))
        side = Vector((1, 0, 0)) if along == "y" else Vector((0, 1, 0))
        coupler(g, (cx, cy, Z_LOOP), a, r=R_PIPE + 0.004, length=0.024, mat="plate")
        for s in (-1, 1):
            p = Vector((cx, cy, DECK + 0.002)) + side * s * 0.042
            box(g, tuple(p), (0.012 if along == "x" else 0.016, 0.016 if along == "x" else 0.012, 0.002), "plate")
            bolt(g, tuple(p + Vector((0, 0, 0.002))), r=0.006)

    # the seat pan frame and its four legs, with tees where they meet the loops
    pan = fillet_path([(-x, PAN_Y[0], Z_PAN), (x, PAN_Y[0], Z_PAN), (x, PAN_Y[1], Z_PAN), (-x, PAN_Y[1], Z_PAN)], 0.05, closed=True)
    pipe(g, pan)
    for lx in (-x, x):
        for ly in (-0.54, -0.10):
            pipe(g, [(lx, ly, Z_LOOP), (lx, ly, Z_PAN)])
            coupler(g, (lx, ly, Z_LOOP), (0, 1, 0), length=0.058)
            coupler(g, (lx, ly, Z_PAN), (0, 1, 0), length=0.058)
    # the board the pan cushion sits on, and a cross pipe under it
    wood_box(g, (0, -0.32, Z_PAN + R_PIPE + 0.0075), (0.29, 0.285, 0.0075), 0, 201)
    pipe(g, [(-x, -0.32, Z_PAN), (x, -0.32, Z_PAN)], r=0.016)

    # the backrest frame: two uprights leaning back, a top bar with round corners (built a little off true), two rails
    lean = 0.008                                         # the top sits 8 mm to the right of the bottom
    top = [(-x, BACK_Y[0], Z_LOOP), (-x + lean, BACK_Y[1], BACK_TOP), (x + lean, BACK_Y[1] - 0.004, BACK_TOP),
           (x, BACK_Y[0], Z_LOOP)]
    pipe(g, fillet_path(top, 0.075))
    for lx in (-x, x):
        coupler(g, (lx, BACK_Y[0], Z_LOOP), (0, 1, 0), length=0.058)
    for z in (0.10, 0.33):
        y = upright_y(z)
        t = (z - Z_LOOP) / (BACK_TOP - Z_LOOP)
        pipe(g, [(-x + lean * t, y, z), (x + lean * t, y, z)], r=0.016)
        for lx in (-x, x):
            coupler(g, (lx + lean * t, y, z), (0, 0, 1), r=R_PIPE + 0.005, length=0.045)
    # a bent support from the backrest down to the pan frame at each side
    for lx in (-x, x):
        pipe(g, fillet_path([(lx, upright_y(0.05), 0.05), (lx, -0.62, Z_PAN + 0.04), (lx, -0.56, Z_PAN)], 0.05), r=0.014)

    # the steering column: a mount plate bolted to the deck, a socket, the leaning pipe with two collars, a strut
    d = column_dir()
    length = (HUB.z - (DECK + 0.05)) / -d.z
    base = HUB + d * length
    plate_at = Vector((base.x, base.y, DECK + 0.004))
    box(g, tuple(plate_at), (0.085, 0.085, 0.004), "plate", bevel=0.0015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bolt(g, tuple(plate_at + Vector((sx * 0.062, sy * 0.062, 0.004))))
    cylinder(g, plate_at + Vector((0, 0, 0.004)), plate_at + Vector((0, 0, 0.016)), 0.046, "fitting", segs=20)
    cylinder(g, base + d * 0.03, base - d * 0.11, 0.032, "fitting", segs=18)
    cylinder(g, base - d * 0.11, base - d * 0.125, 0.036, "fitting", segs=18)
    cylinder(g, base + d * 0.03, HUB + d * 0.03, 0.024, "pipe", segs=18)
    coupler(g, HUB + d * 0.17, d, r=0.031, length=0.04)
    coupler(g, HUB + d * (length - 0.32), d, r=0.03, length=0.05)
    strut0 = plate_at + Vector((0.05, -0.07, 0.01))
    pipe(g, [strut0, base - d * 0.28 + Vector((0.02, 0, 0))], r=0.011, mat="fitting")

    # the wheel: a light pipe rim, three bent spokes, a hub with a cap nut; square to the column, a little turned. It
    # turns with the steering, so it is a node of its own (Steering, build_scene)
    frame_g, g = g, Geo()
    w = -d                                              # toward the driver and up
    up = Vector((0, 1, 0)) - w * w.y
    up = (up - w * up.dot(w)).normalized()              # in the wheel's plane, away from the driver (twelve o'clock)
    side = up.cross(w).normalized()                     # to the driver's right... (x)
    if side.x < 0:
        side = -side
    turn = math.radians(6.0)
    rim = []
    for k in range(65):
        a = 2 * math.pi * k / 64
        rim.append(HUB + (up * math.cos(a) + side * math.sin(a)) * WHEEL_R)
    sweep(g, rim, 0.0145, "pipe", segs=12)
    hub = HUB + w * 0.012
    for phi in (180.0, 95.0, -95.0):
        a = math.radians(phi) + turn
        on_rim = HUB + (up * math.cos(a) + side * math.sin(a)) * (WHEEL_R - 0.006)
        mid = hub + (on_rim - hub) * 0.55 - w * 0.012
        sweep(g, [hub, mid, on_rim], 0.0085, "pipe", segs=10)
    cylinder(g, HUB - w * 0.03, HUB + w * 0.018, 0.036, "fitting", segs=20)
    cylinder(g, HUB + w * 0.018, HUB + w * 0.026, 0.026, "fitting", segs=20)
    hexnut(g, HUB + w * 0.026, w, 0.013, 0.012, "bolt")
    dome(g, HUB + w * 0.038, w, 0.008, "bolt", segs=8)
    wheel_g, g = g, frame_g

    # the junction box the links plug into: a bolted box with two brass terminals and a lead to the seat frame
    jb = Vector((0.38, 0.40, DECK))
    box(g, tuple(jb + Vector((0, 0, 0.032))), (0.045, 0.034, 0.03), "plate", bevel=0.004)
    box(g, tuple(jb + Vector((0, 0, 0.002))), (0.06, 0.04, 0.002), "plate", bevel=0.001)
    for sx in (-1, 1):
        bolt(g, tuple(jb + Vector((sx * 0.052, 0, 0.004))), r=0.005)
    for sy in (-1, 1):
        p = jb + Vector((0, sy * 0.015, 0.062))
        cylinder(g, p, p + Vector((0, 0, 0.014)), 0.0065, "brass", segs=10)
        hexnut(g, p + Vector((0, 0, 0.005)), (0, 0, 1), 0.009, 0.004, "brass")
    lead = [jb + Vector((-0.045, 0.0, 0.03)), jb + Vector((-0.07, -0.02, 0.012)), Vector((x + 0.03, 0.2, DECK + 0.01)),
            Vector((x + 0.026, -0.05, DECK + 0.012)), Vector((x, -0.10, Z_LOOP + 0.06))]
    sweep(g, [Vector(p) for p in bl.bezier(lead[0], lead[1], lead[2], lead[3], 12)] + [lead[4]], 0.0045, "bolt", segs=8)
    return g, wheel_g


# =====================================================================================================
# The cushions
# =====================================================================================================

def cushion_deform(v, body, bulge, sag, top_axis):
    """Points on a plain rounded box moved to a puffy cushion: the middles of the sides bulge out, the face the driver
    sits on (+top_axis) sags in its middle, with a soft unevenness in the stuffing."""
    v = np.asarray(v, float)
    u = np.clip(np.abs(v) / np.array(body), 0, 1) ** 2
    out = v.copy()
    for k in range(3):
        o = [i for i in range(3) if i != k]
        out[:, k] *= 1 + bulge[k] / body[k] * (1 - u[:, o[0]]) * (1 - u[:, o[1]])
    o = [i for i in range(3) if i != top_axis]
    dip = sag * (1 - u[:, o[0]]) * (1 - u[:, o[1]]) * (v[:, top_axis] > 0)
    out[:, top_axis] -= dip
    out *= (1 + 0.004 * rt.Noise3(511).fbm(v, 9.0, octaves=3))[:, None]
    return out


def piping_loops(body, radius, axis):
    """Points of the piped seams round the two faces normal to `axis`, along the middle of their rounded edges."""
    loops = []
    a, b = [i for i in range(3) if i != axis]
    ia, ib = body[a] - radius, body[b] - radius
    for s in (1, -1):
        pts = []
        corners = [(ia, -ib, -90.0), (ia, ib, 0.0), (-ia, ib, 90.0), (-ia, -ib, 180.0)]
        for k, (ca, cb, start) in enumerate(corners):
            for ang in np.linspace(start, start + 90.0, 9)[:-1]:
                pts.append((ca, cb, math.radians(ang)))
            na, nb, _ = corners[(k + 1) % 4]
            h = math.radians(start + 90)
            for t in np.linspace(0, 1, 18)[:-1]:
                pts.append((ca + (na - ca) * t, cb + (nb - cb) * t, h))
        loop = []
        for ca, cb, h in pts:
            p = np.zeros(3)
            p[a] = ca + math.cos(h) * radius * math.sqrt(0.5) * 0.96
            p[b] = cb + math.sin(h) * radius * math.sqrt(0.5) * 0.96
            p[axis] = s * (body[axis] - radius + radius * math.sqrt(0.5) * 0.96)
            loop.append(p)
        loop.append(loop[0])
        loops.append(np.array(loop))
    return loops


def mesh_from(name, verts, faces, face_uvs, material, smooth_angle=70):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate(clean_customdata=False)
    uv = me.uv_layers.new(name="UVMap")
    out = np.zeros((len(me.loops), 2))
    for poly, corners in zip(me.polygons, face_uvs):
        for li, c in zip(poly.loop_indices, corners):
            out[li] = c
    uv.data.foreach_set("uv", out.ravel())
    me.materials.append(material)
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_cushion(name, half, top_axis, bulge, sag, patches, seed, offset):
    """One cushion object (canvas body plus piping), its geometry offset by `offset` from its origin."""
    radius = 0.03
    body = tuple(h - b for h, b in zip(half, bulge))
    rows = (0.004, 0.012, 0.022)

    def coords(h, n):
        return br.axis_coords(h, rows, list(np.linspace(-h + 0.04, h - 0.04, n)))
    cs = [coords(body[0], 14), coords(body[1], max(3, int(body[1] / 0.02))), coords(body[2], max(3, int(body[2] / 0.02)))]
    verts, faces = br.rounded_box(body, cs, radius)
    verts = cushion_deform(verts, body, bulge, sag, top_axis)
    chart_half = tuple(h + 0.004 for h in half)
    charts, size, s, pad = rt.box_charts(chart_half, 1024, 1024)
    albedo, normal, _ = rt.bake(charts, size, s, pad, st.CanvasSurface(chart_half, seed, patches), normal_strength=1.0, shade=0.25)
    face_uvs = []
    for f in faces:
        pts = verts[f]
        nrm = np.zeros(3)
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            nrm += np.array([(a[1] - b[1]) * (a[2] + b[2]), (a[2] - b[2]) * (a[0] + b[0]), (a[0] - b[0]) * (a[1] + b[1])])
        c = rt.chart_of_normal(charts, nrm)
        face_uvs.append([rt.chart_uv(c, p, size, s, pad) for p in pts])
    canvas = br.make_material(name.lower() + "_canvas", albedo, normal, rough=0.9)
    off = np.asarray(offset, float)
    ob = mesh_from(name, [tuple(float(c) for c in p + off) for p in verts], faces, face_uvs, canvas)

    rgb, height = st.piping_texture(seed=seed + 40)
    import soil_textures as soil  # noqa: PLC0415
    import build_soil as bs  # noqa: PLC0415
    piping = br.make_material(name.lower() + "_piping", rgb, soil.normal_from_height(height * 0.0008, 4600), rough=0.9)
    pv, pf, puv = [], [], []
    for loop in piping_loops(body, radius, top_axis):
        v, f, uv = bs.tube(cushion_deform(loop, body, bulge, sag * 0.6, top_axis), 0.0055, ring=8, closed=True)
        base = len(pv)
        pv += v
        pf += [[i + base for i in q] for q in f]
        puv += uv
    cord = mesh_from(name + "Piping", [tuple(float(c) for c in np.asarray(p) + off) for p in pv], pf, puv, piping, smooth_angle=80)
    for o in bpy.context.scene.objects:
        o.select_set(o in (ob, cord))
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    return ob


PAN_PATCHES = {
    (2, 1): [{"c": (0.10, -0.07), "half": (0.055, 0.045), "turn": 4.0, "tone": 0},
             {"c": (-0.13, 0.12), "half": (0.04, 0.034), "turn": -6.0, "tone": 1}],
    (1, 1): [{"c": (-0.14, -0.002), "half": (0.05, 0.026), "turn": 0.0, "tone": 2}],
}
BACK_PATCHES = {
    (1, 1): [{"c": (0.10, 0.06), "half": (0.06, 0.05), "turn": -3.0, "tone": 0},
             {"c": (-0.12, -0.09), "half": (0.04, 0.034), "turn": 5.0, "tone": 2}],
    (1, -1): [{"c": (-0.05, 0.02), "half": (0.07, 0.055), "turn": 2.0, "tone": 1}],
}


# =====================================================================================================
# Scene, objects and clips
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = 2 * FPS


def make_object(name, g: Geo, materials, parent=None, location=(0, 0, 0)):
    """One mesh object from a Geo: per-face materials, the wood's own UVs, box-projected UVs for the rest."""
    faces, mats, uvs = [], [], []
    for face, mat, uv in zip(g.f, g.m, g.uv):
        if len(face) <= 4:
            faces.append(face), mats.append(mat), uvs.append(uv)
            continue
        for k in range(1, len(face) - 1):
            faces.append((face[0], face[k], face[k + 1])), mats.append(mat)
            uvs.append(None if uv is None else (uv[0], uv[k], uv[k + 1]))
    loc = Vector(location)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(Vector(v) - loc) for v in g.v], [], faces)
    me.validate(clean_customdata=False)
    names = list(dict.fromkeys(mats))
    for n in names:
        me.materials.append(materials[n])
    me.polygons.foreach_set("material_index", [names.index(n) for n in mats])
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(38))
    uv = me.uv_layers.new(name="UVMap")
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3) + np.asarray(loc)
    out = np.zeros((len(me.loops), 2))
    for poly in me.polygons:
        fixed = uvs[poly.index]
        if fixed is not None:
            for k, li in enumerate(poly.loop_indices):
                out[li] = fixed[k]
            continue
        axis = int(np.argmax(np.abs(np.asarray(poly.normal))))
        for li in poly.loop_indices:
            p = co[me.loops[li].vertex_index]
            q = [p[i] for i in range(3) if i != axis]
            out[li] = (q[0] / st.TILE, q[1] / st.TILE)
    uv.data.foreach_set("uv", out.ravel())
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location = loc
    return ob


def build_scene():
    reset_scene()
    mats = st.make_materials()
    root = bpy.data.objects.new("ScrapDriversSeat", None)
    bpy.context.scene.collection.objects.link(root)
    rig = {"root": root}
    base = rig["base"] = make_object("Base", build_base(), mats, root)
    frame_g, wheel_g = build_frame()
    frame = rig["frame"] = make_object("Frame", frame_g, mats, base, (0, 0, DECK))
    # the wheel, pivoted at its hub on the Frame, turns about the column (the steer clip)
    steering = rig["steering"] = make_object("Steering", wheel_g, mats, frame, HUB)
    steering.location = HUB - Vector((0, 0, DECK))
    pan = build_cushion("SeatPan", PAN_HALF, 2, (0.006, 0.007, 0.008), 0.009, PAN_PATCHES, 611, (0, 0, PAN_HALF[2]))
    pan.parent = frame
    pan.location = Vector(PAN_AT) - Vector((0, 0, DECK))
    rig["pan"] = pan
    back = build_cushion("Backrest", BACK_HALF, 1, (0.006, 0.006, 0.006), 0.006, BACK_PATCHES, 631, (0, BACK_HALF[1], 0))
    back.parent = frame
    back_y = upright_y(BACK_AT_Z) + R_PIPE + 0.008
    back.location = Vector((0.004, back_y, BACK_AT_Z - DECK))
    back.rotation_euler = Euler((math.radians(BACK_LEAN), 0, 0))
    rig["back"] = back
    return rig


def smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def sink(t, rise=0.16):
    """0 to 1 as the driver's weight lands: a quick sink, a small bounce, settled by ~0.5 s."""
    if t <= 0:
        return 0.0
    return 1.0 - math.exp(-t / (rise * 0.4)) * math.cos(2 * math.pi * t / (rise * 2.6))


def shake(t, amp, seed, freqs=(11.0, 13.0, 9.0)):
    """Three sines (rattle) at `freqs` Hz, phases from `seed`."""
    rng = np.random.default_rng(seed)
    ph = rng.uniform(0, 2 * math.pi, 3)
    return [amp * math.sin(2 * math.pi * f * t + p) for f, p in zip(freqs, ph)]


def pose(rig, clip, t):
    """Sets every animated node for `clip` at time t (seconds)."""
    base, frame, pan, back = rig["base"], rig["frame"], rig["pan"], rig["back"]
    pan_scale, back_scale = 1.0, 1.0
    fr_loc, fr_rot, base_z, base_x = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0], 0.0, 0.0
    breath = math.sin(2 * math.pi * t / 2.0)
    if clip == "enter":
        pan_scale = 1 - PAN_SINK / (2 * PAN_HALF[2]) * sink(t)
        back_scale = 1 - BACK_SINK / (2 * BACK_HALF[1]) * sink(t - 0.08)
        decay = math.exp(-max(t - 0.05, 0) / 0.16) * smooth(t / 0.05) * (1 - smooth((t - 0.62) / 0.18))
        rx, ry, rz = shake(t, math.radians(0.7) * decay, 1)
        fr_rot = [rx, ry, rz * 0.5]
        fr_loc = [v * 0.0025 * decay for v in shake(t, 1.0, 2, (14.0, 10.0, 12.0))]
        bump = math.sin(math.pi * min(max(t - 0.03, 0) / 0.42, 1)) * math.exp(-max(t - 0.03, 0) / 0.3)
        base_z = -0.005 * bump
        base_x = 0.002 * bump
    elif clip in ("seated_loop", "seated_rattle_loop"):
        pan_scale = 1 - PAN_SINK / (2 * PAN_HALF[2]) * (1 + 0.12 * breath)
        back_scale = 1 - BACK_SINK / (2 * BACK_HALF[1]) * (1 + 0.15 * math.sin(2 * math.pi * t / 2.0 + 0.4))
        if clip == "seated_rattle_loop":
            # 9, 13 and 7.5 Hz: whole numbers of cycles in the 2 s loop, so it loops seamlessly
            rx, ry, rz = shake(t, math.radians(0.22), 3, (9.0, 13.0, 7.5))
            fr_rot = [rx, ry, rz * 0.6]
            fr_loc = [v * 0.0015 for v in shake(t, 1.0, 4, (11.0, 7.0, 13.0))]
            fr_loc[2] *= 1.6                              # the up-and-down shake reaches 2.4 mm
            pan_scale += 0.0012 / (2 * PAN_HALF[2]) * math.sin(2 * math.pi * 6.5 * t)
            back_scale += 0.001 / (2 * BACK_HALF[1]) * math.sin(2 * math.pi * 8.0 * t + 1.0)
    elif clip == "exit":
        up = sink(t, 0.12)
        pan_scale = 1 - PAN_SINK / (2 * PAN_HALF[2]) * (1 - up)
        back_scale = 1 - BACK_SINK / (2 * BACK_HALF[1]) * (1 - sink(t - 0.04, 0.12))
        decay = math.exp(-t / 0.14) * (1 - smooth((t - 0.5) / 0.2))
        rx, ry, rz = shake(t, math.radians(0.55) * decay, 5)
        fr_rot = [rx, ry, rz * 0.5]
        fr_loc = [v * 0.002 * decay for v in shake(t, 1.0, 6, (12.0, 9.0, 14.0))]
        bump = math.sin(math.pi * min(t / 0.4, 1)) * math.exp(-t / 0.25)
        base_z = 0.003 * bump
    frame.location = Vector((0, 0, DECK)) + Vector(fr_loc)
    frame.rotation_euler = Euler(fr_rot)
    base.location = Vector((base_x, 0, base_z))
    pan.scale = Vector((1, 1, pan_scale))
    back.scale = Vector((1, back_scale, 1))


CLIPS = {"enter": 0.8, "seated_loop": 2.0, "seated_rattle_loop": 2.0, "exit": 0.7}
# The steer clip (part data display.clips.scrub, held at the seat's "wheel" readout): the wheel from WHEEL_TURN degrees
# left at 0 s to as far right at STEER_SECONDS. tools/characters/build_characters.py WHEEL_TURN must match, so the
# driver's hands ride round with it.
WHEEL_TURN = 90.0
STEER_SECONDS = 1.0


def steer(rig, u):
    """The wheel turned for the steer clip's point u (0 full left, 1 full right): clockwise as the driver sees it is a
    negative turn about the column toward them."""
    ob = rig["steering"]
    ob.rotation_mode = "XYZ"
    ob.rotation_euler = Matrix.Rotation(math.radians((1.0 - 2.0 * u) * WHEEL_TURN), 3, -column_dir()).to_euler("XYZ", ob.rotation_euler)


def bake(rig):
    obs = [rig["base"], rig["frame"], rig["pan"], rig["back"]]
    for ob in obs:
        ob.animation_data_create()
    for clip, seconds in CLIPS.items():
        actions = {}
        for ob in obs:
            act = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = act
            actions[ob.name] = act
        frames = int(round(seconds * FPS))
        for f in range(frames + 1):
            # a loop's last key equals its first, so it wraps without a jump
            pose(rig, clip, (f % frames if clip.startswith("seated") else f) / FPS)
            for ob in obs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in obs:
            lt.linear_keys(actions[ob.name])
            bl.push_track(ob, actions[ob.name], clip)
    ob = rig["steering"]
    ob.animation_data_create()
    act = ob.animation_data.action = bpy.data.actions.new("steer_Steering")
    frames = int(round(STEER_SECONDS * FPS))
    for f in range(frames + 1):
        steer(rig, f / frames)
        ob.keyframe_insert("rotation_euler", frame=f)
    lt.linear_keys(act)
    bl.push_track(ob, act, "steer")
    rest(rig)


def rest(rig):
    rig["base"].location = (0, 0, 0)
    rig["frame"].location = (0, 0, DECK)
    rig["frame"].rotation_euler = (0, 0, 0)
    for ob in (rig["base"], rig["frame"], rig["pan"], rig["back"]):
        ob.scale = (1, 1, 1)
    rig["base"].rotation_euler = (0, 0, 0)
    rig["pan"].rotation_euler = (0, 0, 0)
    rig["back"].rotation_euler = Euler((math.radians(BACK_LEAN), 0, 0))
    steer(rig, 0.5)


def export(path):
    for ob in bpy.context.scene.objects:
        ob.select_set(not ob.name.startswith("RenderOnly") and not ob.name.startswith("Icon"))
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=True,
        export_image_format="JPEG", export_jpeg_quality=90, export_image_quality=90, export_extras=False,
        export_cameras=False, export_lights=False)


# =====================================================================================================
# The icon
# =====================================================================================================

def render_icon(path, view=(2.2, -0.4, 1.7), ortho_scale=2.05):
    """The seat alone, from `view` (by default the right and above, with the wheel face showing), on a clear background."""
    sc = bpy.context.scene
    hidden = [o for o in sc.objects if o.name.startswith("RenderOnly")]
    for o in hidden:
        o.hide_render = True
    # the baked clips' NLA tracks would pose the seat; the icon shows it at rest
    muted = [t for o in sc.objects if o.animation_data for t in o.animation_data.nla_tracks if not t.mute]
    for t in muted:
        t.mute = True
    for o in sc.objects:
        if o.animation_data:
            o.animation_data.action = None
    sc.frame_set(0)
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 96
    sc.cycles.seed = 7
    sc.cycles.use_denoising = True
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    world = bpy.data.worlds.new("IconWorld")
    sc.world = world
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.75, 0.76, 0.8, 1)
    bg.inputs["Strength"].default_value = 0.6
    made = []
    for name, loc, energy, size in (("Key", (2.6, 2.2, 3.0), 260, 2.0), ("Fill", (-2.8, 1.0, 1.4), 90, 2.4),
                                    ("Rim", (-0.8, -3.0, 2.2), 140, 2.0)):
        light = bpy.data.lights.new("Icon" + name, "AREA")
        light.energy, light.size = energy, size
        ob = bpy.data.objects.new("Icon" + name, light)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(ob)
        made.append(ob)
    cam = bpy.data.cameras.new("IconCam")
    cam.type = "ORTHO"
    cam.ortho_scale = ortho_scale
    cob = bpy.data.objects.new("IconCam", cam)
    # from the right, a little behind and above: the wheel faces the driver, so from the front it is seen edge-on
    view = Vector(view).normalized()
    cob.location = view * 8 + Vector((0, 0, -0.02))
    cob.rotation_euler = (-view).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cob)
    made.append(cob)
    sc.camera = cob
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.resolution_percentage = 100
    tmp = path.with_suffix(".render.png")
    sc.render.filepath = str(tmp)
    bpy.ops.render.render(write_still=True)
    img = Image.open(tmp).convert("RGBA")
    box_ = img.getbbox()
    if box_:                                             # fill the picture: crop to the seat, square, small margin
        l, t, r, b = box_
        side = int(max(r - l, b - t) * 1.04)
        cx, cy = (l + r) // 2, (t + b) // 2
        sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        sq.paste(img.crop((cx - side // 2, cy - side // 2, cx - side // 2 + side, cy - side // 2 + side)), (0, 0))
        img = sq
    img = img.resize((256, 256), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    tmp.unlink()
    for ob in made:
        bpy.data.objects.remove(ob)
    for o in hidden:
        o.hide_render = False
    for t in muted:
        t.mute = False
    return path


# =====================================================================================================
# Main
# =====================================================================================================

def main():
    rig = build_scene()
    bake(rig)
    export(OUT)
    i = br.glb_info(OUT)
    print(f"wrote {OUT.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
          f"{i['materials']} materials, {i['extras']}) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()}")
    icon = render_icon(ICON)
    print(f"wrote {icon.relative_to(REPO)}")
    print(f"seat offset (part frame, glTF axes): [{HIP[0]:.3f}, {HIP[2]:.3f}, {-HIP[1]:.3f}]; backrest lean {BACK_LEAN:.1f} deg")
    if "--render" in sys.argv:
        import render_scrap_seat
        render_scrap_seat.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
