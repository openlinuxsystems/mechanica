"""Build the Bearing and the Scrap Gas Engine, their clips and their icons (design specs of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/bearing.glb`` - the Bearing, one block (0.25 m) across on the face it is placed on. The
  bearing itself is the spec's 25 cm x 8 cm disc lying on that face: a segmented gunmetal casing in two halves with
  bolt holes round the side, a matte black band between them with a thin teal line (the turning interface), and on
  its face a black ring with a teal accent round a machined silver hub (four bolt holes, a raised boss with the axle
  hole) with a teal notch so turning shows. Nothing else: no axle. Whatever goes on the rotor sits flush on the disc's
  face (the rotor socket's ``inset`` of 0.17 m in the part data), as in Scrap Mechanic. Two nodes: ``Stator`` (stays
  with the block it is placed on) and ``rotor`` (the turning half and hub: named after the part's rotational socket,
  so the game turns it with what is attached there).
* ``mods/vanilla/meshes/parts/scrap_gas_engine.glb`` - the Scrap Gas Engine, 2 x 2 x 2 blocks (0.5 m cube), front (+Y in
  Blender, the game's -Z) with the control panel. Mismatched rusty plates with bolts, duct tape, a fuel cap on top,
  round bearing flanges on both sides, crude copper and black pipes, loose wires, two exhaust stubs at the back,
  oil streaks. Every moving piece is its own node under ``Body`` (which shakes): ``FlangeL``/``FlangeR``,
  ``PipeSide``, ``PipeFront``, ``TapeFlap``, ``LeverRattle`` > ``Lever``, ``Needle``, ``GlowGreen``, ``GlowRed``.
  glTF clips (the game plays them from the engine's ``clip`` readout; see docs/part-data-schema.md):

  - ``off_loop`` (4 s): at rest, the needle twitching now and then.
  - ``start`` (1.0 s): lever jerk, red lamp sputtering, uneven thumps (up to 1.5 cm), needle up, flanges spin up,
    green lamp on.
  - ``false_start`` (0.7 s): a sputter and a thump, then it dies back to rest.
  - ``run_loop`` / ``run_hard_loop`` (2 s): irregular rattle (0.35 / 0.7 cm), pipes and the tape flap shaking on
    their own, wobbly flanges, jittering needle, flickering green lamp. The hard loop is for the high gears.
  - ``gear`` (0.45 s): the lever rattles, the red lamp flashes, a harder shake.
  - ``stop`` (0.8 s): uneven wind-down, a last shake, the green lamp flickers out, the needle drops.
  - ``lever`` (1 s, scrubbed): the power lever from the lowest gear (0 s) to the highest (1 s). It moves ``Lever``
    only, so the game plays it on its own player alongside the others.

* ``mods/vanilla/textures/items/bearing_icon.png`` and ``scrap_gas_engine_icon.png`` - the hotbar, inventory and
  Fabricator pictures, 256 x 256 on a clear background.

Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up (Blender -Y becomes glTF +Z).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/bearing_engine/build_bearing_engine.py                  # writes the .glb files and the icons
    python tools/bearing_engine/build_bearing_engine.py --render OUT_DIR # also renders review sheets into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
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
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "lift"))
import build_lift as bl  # noqa: E402  (Geo, primitives, object and NLA helpers)
import lift_textures as lt  # noqa: E402
import part_textures as pt  # noqa: E402

REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = 40

Geo, box, cylinder, ring, dome, hexnut, sweep, bezier, quad = (
    bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep, bl.bezier, bl.quad)

# ---- Bearing (metres; Z is the turning axis, the block it sits on is below Z = -0.125) ------------------
B_R = 0.125                  # outer radius: 25 cm across, one block face
B_BOTTOM = -0.125            # the face it is placed on
B_THICK = 0.08               # 8 cm disc
B_TOP = B_BOTTOM + B_THICK   # -0.045: the disc's face
B_SPLIT0, B_SPLIT1 = B_BOTTOM + 0.027, B_TOP - 0.024   # the black band between the casing halves
SEGMENTS = 16                # casing segments round the edge

# ---- Engine (metres; 0.5 m cube centred on the origin, front +Y) ------------------------------------
E_HALF = 0.25
E_CORE = 0.214               # the inner box the plates cover
E_PLATE = 0.019              # plate thickness (plates reach about 0.234)
E_FRONT = E_CORE + E_PLATE   # the plates' outer face
LEVER_AT = (0.085, E_FRONT + 0.009, -0.105)    # the lever's pivot (viewer-left of the panel: +X)
LEVER_LEN = 0.13
LEVER_SWING = math.radians(34)                 # each side of upright: lowest gear leans left, highest right
GAUGE_AT = (-0.065, E_FRONT + 0.012, 0.035)
GAUGE_R = 0.042
NEEDLE_REST = math.radians(120)                # needle angle about +Y at rest (the dial's zero, lower left)
NEEDLE_RUN = math.radians(-8)
NEEDLE_HARD = math.radians(-58)
LAMP_GREEN = (-0.035, E_FRONT + 0.012, -0.075)
LAMP_RED = (-0.098, E_FRONT + 0.012, -0.075)
FLANGE_X = E_CORE + 0.004                      # the flange plate's inner face, each side
EXHAUSTS = [(-0.15, -0.165), (-0.075, -0.165)]  # (x, y) of the two exhaust stubs, outlets at the top face
OFF = 0.012                                    # scale of a lamp glow that is off (hidden in its lens)


def rnd(seed):
    return np.random.default_rng(seed)


# =====================================================================================================
# Shapes
# =====================================================================================================

def lathe(g, profile, mat, a0=0.0, a1=2 * math.pi, steps=48, center=(0, 0)):
    """Sweeps a closed (r, z) profile about Z from a0 to a1. A partial sweep is capped at both ends."""
    full = abs((a1 - a0) - 2 * math.pi) < 1e-6
    n = len(profile)
    count = steps if full else steps + 1
    verts, faces = [], []
    for k in range(count):
        a = a0 + (a1 - a0) * k / steps
        c, s = math.cos(a), math.sin(a)
        for r, z in profile:
            verts.append((center[0] + r * c, center[1] + r * s, z))
    for k in range(steps):
        k1 = (k + 1) % count
        for i in range(n):
            i1 = (i + 1) % n
            faces.append((k * n + i, k1 * n + i, k1 * n + i1, k * n + i1))
    if not full:
        faces.append(tuple(range(n)))
        faces.append(tuple(steps * n + i for i in reversed(range(n))))
    return g.add(verts, faces, mat)


def disc(g, p, n, r, mat, segs=20, depth=0.0):
    """A flat round patch at p facing n (a hole when dark, a lens), optionally a short plug of `depth` behind it."""
    p, n = Vector(p), Vector(n).normalized()
    if depth > 0:
        return cylinder(g, p - n * depth, p, r, mat, segs=segs, cap0=False)
    u, w = bl.frame_of(n)
    verts = [tuple(p + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r) for k in range(segs)]
    return g.add(verts, [tuple(range(segs))], mat)


def bolt_hole(g, p, n, r, rim_mat):
    """A recessed bolt hole: a dark socket with a thin rim."""
    p, n = Vector(p), Vector(n).normalized()
    disc(g, p + n * 0.0004, n, r, "hole", segs=14)
    u, w = bl.frame_of(n)
    verts, faces = [], []
    for k in range(14):
        a = 2 * math.pi * k / 14
        d = u * math.cos(a) + w * math.sin(a)
        verts += [tuple(p + n * 0.0006 + d * r), tuple(p + n * 0.0006 + d * r * 1.35)]
    for k in range(14):
        a, b = 2 * k, 2 * ((k + 1) % 14)
        faces.append((a, b, b + 1, a + 1))
    return g.add(verts, faces, rim_mat)


def plate(g, center, half, normal_axis, sign, mat, tilt, seed):
    """A bevelled scrap plate on one face: `half` in the face's two in-plane axes, slightly skewed by `tilt` (rad)."""
    r = rnd(seed)
    t = [0, 0, 0]
    t[normal_axis] = E_PLATE / 2
    size = list(half)
    size.insert(normal_axis, E_PLATE / 2)
    rot = Euler([tilt * r.uniform(-1, 1) * (0 if i == normal_axis else 0.35) for i in range(3)]).to_matrix()
    spin = [0, 0, 0]
    spin[normal_axis] = tilt * r.uniform(-1, 1)
    rot = Euler(spin).to_matrix() @ rot
    return box(g, center, size, mat, bevel=0.0025, rot=rot)


# =====================================================================================================
# The Bearing
# =====================================================================================================

def casing_half(g, z0, z1, rim_mat="gunmetal"):
    """One gunmetal half of the casing: a solid core and SEGMENTS chunky edge blocks with small gaps between."""
    lathe(g, [(0.0, z0), (0.112, z0), (0.112, z1), (0.0, z1)], rim_mat, steps=48)
    seg = 2 * math.pi / SEGMENTS
    gap = 0.012
    ch = 0.006
    for k in range(SEGMENTS):
        a0 = k * seg + gap / 2
        lathe(g, [(0.108, z0), (B_R - ch, z0), (B_R, z0 + ch), (B_R, z1 - ch), (B_R - ch, z1), (0.108, z1)],
              rim_mat, a0=a0, a1=a0 + seg - gap, steps=4)
    # bolt holes round the side, every other segment
    zc = (z0 + z1) / 2
    for k in range(0, SEGMENTS, 2):
        a = (k + 0.5) * seg
        n = Vector((math.cos(a), math.sin(a), 0))
        bolt_hole(g, n * B_R + Vector((0, 0, zc)), n, 0.0042, rim_mat)
    return zc


def build_bearing_stator():
    g = Geo()
    casing_half(g, B_BOTTOM, B_SPLIT0)
    # the black band between the halves, a little inset, and its teal line
    lathe(g, [(0.0, B_SPLIT0), (0.119, B_SPLIT0), (0.119, B_SPLIT1), (0.0, B_SPLIT1)], "band", steps=64)
    zm = (B_SPLIT0 + B_SPLIT1) / 2
    lathe(g, [(0.1186, zm - 0.0011), (0.1198, zm - 0.0011), (0.1198, zm + 0.0011), (0.1186, zm + 0.0011)], "teal", steps=64)
    return g


def build_bearing_rotor():
    g = Geo()
    z0, z1 = B_SPLIT1, B_TOP
    casing_half(g, z0, z1)
    # the face: segmented outer ring (the casing blocks' tops), a recessed black ring with a teal accent, the hub
    lathe(g, [(0.072, z1 - 0.004), (0.091, z1 - 0.004), (0.091, z1 + 0.0005), (0.072, z1 + 0.0005)], "band", steps=64)
    lathe(g, [(0.0815, z1 + 0.0005), (0.0845, z1 + 0.0005), (0.0845, z1 + 0.0012), (0.0815, z1 + 0.0012)], "teal", steps=64)
    for k in range(8):
        a = (k + 0.5) * 2 * math.pi / 8
        bolt_hole(g, (0.106 * math.cos(a), 0.106 * math.sin(a), z1), (0, 0, 1), 0.0048, "gunmetal")
    hub_top = z1 + 0.002
    lathe(g, [(0.0, z1 - 0.004), (0.0715, z1 - 0.004), (0.0715, hub_top - 0.002), (0.0695, hub_top), (0.0, hub_top)], "silver", steps=48)
    # four shallow panel lines across the hub (the reference's pie segments), between the bolts
    for k in range(4):
        a = k * math.pi / 2
        c, s = math.cos(a), math.sin(a)
        mid = Vector((c, s, 0)) * 0.05
        box(g, (mid.x, mid.y, hub_top + 0.0002), (0.019 if abs(c) > 0.5 else 0.0012, 0.019 if abs(s) > 0.5 else 0.0012, 0.0004), "band")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = (0.044 * math.cos(a), 0.044 * math.sin(a), hub_top)
        bolt_hole(g, p, (0, 0, 1), 0.0062, "silver")
        hexnut(g, (p[0], p[1], hub_top - 0.002), (0, 0, 1), 0.0042, 0.0024, "silver")
    # the raised boss round the axle hole
    boss_top = hub_top + 0.007
    lathe(g, [(0.016, hub_top - 0.001), (0.03, hub_top - 0.001), (0.03, boss_top - 0.002), (0.028, boss_top), (0.016, boss_top)], "silver", steps=40)
    disc(g, (0, 0, boss_top - 0.004), (0, 0, 1), 0.0165, "hole", segs=24)
    # the teal notch: a mark across the hub's rim, so the turning (and its direction) shows
    box(g, (0.0575, 0, hub_top + 0.0003), (0.011, 0.0035, 0.0004), "teal")
    return g


# =====================================================================================================
# The Scrap Gas Engine
# =====================================================================================================

RUSTS = ("rust_a", "rust_b", "rust_c")


def face_plates(g, axis, sign, seed, skip=()):
    """Two by two mismatched plates on one face of the core, a little skewed, with bolts at their corners."""
    r = rnd(seed)
    other = [i for i in range(3) if i != axis]
    q = E_CORE / 2
    for i, (ua, ub) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        if i in skip:
            continue
        c = [0.0, 0.0, 0.0]
        c[axis] = sign * (E_CORE + E_PLATE / 2 - 0.001 * r.uniform(0, 1))
        jitter = r.uniform(-0.004, 0.004, 2)
        c[other[0]] = ua * q + jitter[0]
        c[other[1]] = ub * q + jitter[1]
        half = (q - 0.004 + r.uniform(-0.003, 0.002), q - 0.004 + r.uniform(-0.003, 0.002))
        plate(g, c, half, axis, sign, RUSTS[int(r.integers(0, 3))], math.radians(1.4), seed * 7 + i)
        n = [0, 0, 0]
        n[axis] = sign
        for cu, cv in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            if r.uniform() < 0.25:
                continue
            p = list(c)
            p[axis] = sign * (E_CORE + E_PLATE)
            p[other[0]] += cu * (half[0] - 0.017)
            p[other[1]] += cv * (half[1] - 0.017)
            hexnut(g, p, n, 0.0075, 0.006, "steel" if r.uniform() < 0.6 else "dark")


def tape_strip(g, points, width, seed):
    """Duct tape laid along a path of surface points (each with its outward normal), slightly lifted."""
    pts = [(Vector(p), Vector(n).normalized()) for p, n in points]
    verts, faces = [], []
    for i, (p, n) in enumerate(pts):
        a = pts[max(i - 1, 0)][0]
        b = pts[min(i + 1, len(pts) - 1)][0]
        t = (b - a).normalized()
        side = n.cross(t).normalized()
        lift = 0.0016 + 0.0006 * math.sin(i * 1.7 + seed)
        verts += [tuple(p + n * lift - side * width / 2), tuple(p + n * lift + side * width / 2)]
    for i in range(len(pts) - 1):
        faces.append((2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2))
    return g.add(verts, faces, "tape")


def build_housing():
    g = Geo()
    box(g, (0, 0, 0), (E_CORE, E_CORE, E_CORE), "dark", bevel=0.004)
    face_plates(g, 2, 1, 11)                       # top
    face_plates(g, 2, -1, 13)                      # bottom
    face_plates(g, 1, -1, 17)                      # back
    face_plates(g, 0, 1, 19)                       # left side (viewer-left of the front)
    face_plates(g, 0, -1, 23)                      # right side
    face_plates(g, 1, 1, 29, skip=(0, 1, 2, 3))    # the front is the panel's (below)
    # front: a lower skirt plate, a top plate, and a dark gap on the right where the wires come out
    box(g, (0.0, E_CORE + E_PLATE / 2, -0.185), (0.205, E_PLATE / 2, 0.026), "rust_c", bevel=0.004, rot=Euler((0, math.radians(1.2), 0)).to_matrix())
    box(g, (0.02, E_CORE + E_PLATE / 2, 0.172), (0.19, E_PLATE / 2, 0.038), "rust_a", bevel=0.004, rot=Euler((0, math.radians(-0.9), 0)).to_matrix())
    box(g, (0.185, E_CORE + E_PLATE / 2, 0.02), (0.024, E_PLATE / 2, 0.13), "rust_b", bevel=0.004)
    box(g, (-0.185, E_CORE + 0.004, 0.02), (0.026, 0.004, 0.13), "hole")                     # the wire gap
    box(g, (-0.207, E_CORE + E_PLATE / 2, 0.02), (0.007, E_PLATE / 2, 0.13), "rust_c", bevel=0.002)
    # the fuel cap on top, toward the back right
    cx, cy, top = -0.11, -0.06, E_CORE + E_PLATE
    box(g, (cx, cy, top + 0.004), (0.05, 0.05, 0.004), "steel", bevel=0.003)
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        hexnut(g, (cx + dx * 0.038, cy + dy * 0.038, top + 0.008), (0, 0, 1), 0.006, 0.004, "dark")
    lathe(g, [(0.0, top + 0.008), (0.04, top + 0.008), (0.042, top + 0.011), (0.04, E_HALF - 0.004), (0.034, E_HALF), (0.0, E_HALF)],
          "dark", steps=24, center=(cx, cy))
    for k in range(12):
        a = 2 * math.pi * k / 12
        box(g, (cx + 0.041 * math.cos(a), cy + 0.041 * math.sin(a), top + 0.014), (0.0025, 0.0025, 0.004), "steel")
    # exhaust stubs: out of the back plate, up over the top edge (smoke leaves at their outlets)
    for x, y in EXHAUSTS:
        top_z = E_HALF - 0.002
        pts = bezier((x, -E_CORE - 0.002, 0.10), (x, -E_HALF + 0.004, 0.10), (x, y - 0.012, 0.15), (x, y, 0.2), n=10)
        pts.append(np.array((x, y, top_z)))
        sweep(g, pts, 0.016, "black_pipe", segs=12)
        ring(g, top_z - 0.012, top_z, 0.013, 0.019, "dark", segs=16, center=(x, y))
        disc(g, (x, y, top_z - 0.006), (0, 0, 1), 0.013, "hole", segs=16)
    # tape: over the top-front edge, across the left side's top edge, over a seam on the back
    tape_strip(g, [((0.06, 0.12, E_CORE + E_PLATE), (0, 0, 1)), ((0.062, 0.2, E_CORE + E_PLATE), (0, 0, 1)),
                   ((0.063, E_CORE + E_PLATE, E_CORE + E_PLATE), (0, 0.7, 0.7)), ((0.063, E_CORE + E_PLATE, 0.18), (0, 1, 0)),
                   ((0.064, E_CORE + E_PLATE, 0.135), (0, 1, 0))], 0.045, 1)
    tape_strip(g, [((-0.16, 0.2, E_CORE + E_PLATE), (0, 0, 1)), ((-0.1, 0.2, E_CORE + E_PLATE), (0, 0, 1))], 0.04, 2)
    tape_strip(g, [((E_CORE + E_PLATE, 0.03, 0.2), (1, 0, 0)), ((E_CORE + E_PLATE, 0.08, E_CORE + E_PLATE), (0.7, 0, 0.7)),
                   ((0.18, 0.1, E_CORE + E_PLATE), (0, 0, 1)), ((0.13, 0.11, E_CORE + E_PLATE), (0, 0, 1))], 0.04, 3)
    tape_strip(g, [((0.02, -E_CORE - E_PLATE, 0.16), (0, -1, 0)), ((-0.04, -E_CORE - E_PLATE, 0.05), (0, -1, 0))], 0.042, 4)
    tape_strip(g, [((-E_CORE - E_PLATE, 0.13, -0.02), (-1, 0, 0)), ((-E_CORE - E_PLATE, 0.19, -0.12), (-1, 0, 0))], 0.04, 5)
    return g


def build_panel():
    """The control panel on the front: a bolted plate, the lever's slotted bracket, the gauge and two lamps."""
    g = Geo()
    y = E_CORE + E_PLATE
    box(g, (0.01, y - 0.004, -0.02), (0.165, 0.008, 0.15), "panel", bevel=0.004, rot=Euler((0, math.radians(-0.8), 0)).to_matrix())
    for x, z in ((-0.14, -0.155), (0.155, -0.155), (-0.14, 0.115), (0.155, 0.115), (0.01, 0.12), (0.01, -0.16)):
        hexnut(g, (x, y + 0.004, z), (0, 1, 0), 0.0075, 0.006, "steel")
    # lever bracket: a slotted block the lever pivots in
    lx, ly, lz = LEVER_AT
    box(g, (lx, y + 0.009, lz), (0.03, 0.009, 0.024), "steel", bevel=0.003)
    box(g, (lx, y + 0.0185, lz + 0.004), (0.022, 0.0012, 0.006), "hole")
    box(g, (lx, y + 0.0035, lz - 0.038), (0.034, 0.006, 0.016), "dark", bevel=0.002)
    for dx in (-0.024, 0.024):
        hexnut(g, (lx + dx, y + 0.009, lz - 0.038), (0, 1, 0), 0.005, 0.005, "steel")
    # gauge: bezel, dial (decal, the viewer's right is -X) and a dark glass rim
    gx, gy, gz = GAUGE_AT
    ring_y0, ring_y1 = y, gy + 0.004
    cylinder(g, (gx, ring_y0, gz), (gx, ring_y1, gz), GAUGE_R + 0.008, "steel", segs=32, cap0=False, cap1=False)
    verts, faces = [], []
    for k in range(32):
        a = 2 * math.pi * k / 32
        d = Vector((-math.cos(a), 0, math.sin(a)))
        verts += [tuple(Vector((gx, ring_y1, gz)) + d * (GAUGE_R + 0.008)), tuple(Vector((gx, ring_y1, gz)) + d * GAUGE_R)]
    for k in range(32):
        a, b = 2 * k, 2 * ((k + 1) % 32)
        faces.append((a, b, b + 1, a + 1))
    g.add(verts, faces, "steel")
    dial_y = gy - 0.003
    r = GAUGE_R
    quad(g, [(gx + r, dial_y, gz - r), (gx - r, dial_y, gz - r), (gx - r, dial_y, gz + r), (gx + r, dial_y, gz + r)], "decal_gauge")
    # lamps: bezels and dark lenses (the glows are separate nodes)
    for (x, ly2, z), lens in ((LAMP_GREEN, "lamp_green"), (LAMP_RED, "lamp_red")):
        cylinder(g, (x, y, z), (x, ly2, z), 0.017, "steel", segs=20, cap0=False)
        dome(g, (x, ly2, z), (0, 1, 0), 0.0125, lens, segs=14, height=0.007)
    # a little handwritten-looking tick of tape over the gauge's corner
    tape_strip(g, [((gx - 0.07, y + 0.0, gz + 0.06), (0, 1, 0)), ((gx - 0.02, y + 0.0, gz + 0.09), (0, 1, 0))], 0.026, 6)
    return g


def build_lever():
    """The power lever about its pivot (origin): a flat bar up to a red ball knob."""
    g = Geo()
    box(g, (0, 0, 0), (0.008, 0.012, 0.012), "steel", bevel=0.002)
    box(g, (0, 0.002, LEVER_LEN / 2), (0.0075, 0.005, LEVER_LEN / 2), "steel", bevel=0.002)
    cylinder(g, (-0.012, 0, 0), (0.012, 0, 0), 0.006, "dark", segs=10)
    bl.dome(g, (0, 0.0, LEVER_LEN + 0.004), (0, 0, 1), 0.0155, "knob", segs=16, height=0.0155)
    bl.dome(g, (0, 0.0, LEVER_LEN + 0.004), (0, 0, -1), 0.0155, "knob", segs=16, height=0.012)
    return g


def build_needle():
    g = Geo()
    verts = [(-0.0028, 0, -0.008), (0.0028, 0, -0.008), (0.0009, 0, 0.034), (-0.0009, 0, 0.034)]
    g.add(verts, [(0, 1, 2, 3)], "needle")
    g.add([(v[0], -0.0004, v[2]) for v in verts], [(3, 2, 1, 0)], "needle")
    cylinder(g, (0, -0.002, 0), (0, 0.003, 0), 0.0045, "dark", segs=10)
    return g


def build_glow(mat):
    g = Geo()
    dome(g, (0, 0, 0), (0, 1, 0), 0.0122, mat, segs=14, height=0.0072)
    return g


def build_flange():
    """A rough round bearing flange on the +X side (mirrored for -X), about its axis through the origin."""
    g = Geo()
    t0 = 0.0
    t1 = 0.019
    profile = [(0.0, t0), (0.088, t0), (0.092, t0 + 0.004), (0.092, t1 - 0.004), (0.087, t1), (0.044, t1), (0.042, t1 + 0.012), (0.0, t1 + 0.012)]
    hub = Geo()
    lathe(hub, profile, "steel", steps=36)
    lathe(hub, [(0.019, t1 + 0.012), (0.03, t1 + 0.012), (0.03, t1 + 0.0125), (0.019, t1 + 0.0125)], "dark", steps=24)
    disc(hub, (0, 0, t1 + 0.006), (0, 0, 1), 0.019, "hole", segs=20)
    r = rnd(5)
    for k in range(8):
        a = 2 * math.pi * k / 8 + r.uniform(-0.06, 0.06)
        hexnut(hub, (0.068 * math.cos(a), 0.068 * math.sin(a), t1), (0, 0, 1), 0.0078, 0.0065, "dark" if k % 3 else "steel")
    # a dented, rust-streaked ring plate behind it
    lathe(hub, [(0.07, -0.004), (0.104, -0.004), (0.104, 0.0), (0.07, 0.0)], "rust_c", steps=36)
    # turn so its axis (Z) points along +X
    g.extend(hub, Matrix.Rotation(math.pi / 2, 4, "Y"))
    return g


def build_side_pipe():
    """Copper pipe down the front-right vertical edge (viewer-right is -X) with couplings and a clamp."""
    g = Geo()
    x, y = -E_CORE - 0.012, E_CORE + 0.006
    pts = bezier((x + 0.02, y - 0.05, E_CORE + E_PLATE - 0.002), (x - 0.005, y - 0.05, 0.215), (x - 0.006, y, 0.19), (x - 0.006, y, 0.12), n=10)
    pts += [np.array((x - 0.006, y, z)) for z in (0.05, -0.05, -0.13)]
    pts += bezier((x - 0.006, y, -0.15), (x - 0.006, y, -0.2), (x - 0.006, y + 0.012, -0.212), (x + 0.04, y + 0.016, -0.214), n=8)[1:]
    sweep(g, pts, 0.0115, "copper", segs=12)
    for z in (0.08, -0.07):
        cylinder(g, (x - 0.006, y, z - 0.012), (x - 0.006, y, z + 0.012), 0.015, "steel", segs=6, phase=math.pi / 6)
    box(g, (x + 0.004, y - 0.004, 0.0), (0.008, 0.016, 0.006), "dark", bevel=0.002)
    return g


def build_front_pipe():
    """Black pipe along the bottom front with a joint and a valve, into the housing at the left."""
    g = Geo()
    y, z = E_CORE + E_PLATE, -0.212
    pts = [np.array((-0.17, y + 0.004, z))]
    pts += [np.array((x, y + 0.004, z)) for x in (-0.06, 0.06)]
    pts += bezier((0.12, y + 0.004, z), (0.18, y + 0.004, z), (0.2, y - 0.01, z), (0.205, y - 0.03, z + 0.03), n=8)
    sweep(g, pts, 0.0105, "black_pipe", segs=12)
    cylinder(g, (-0.03, y + 0.004, z), (0.0, y + 0.004, z), 0.0145, "copper", segs=6, phase=math.pi / 6)
    cylinder(g, (0.09, y + 0.004, z), (0.09, y + 0.004, z + 0.03), 0.006, "steel", segs=8)
    cylinder(g, (0.09, y + 0.004, z + 0.03), (0.09, y + 0.004, z + 0.034), 0.016, "dark", segs=10)
    return g


def build_wires():
    """Loose coloured wires out of the gap at the front right, down into the panel."""
    g = Geo()
    x0, y0 = -0.19, E_CORE + 0.003
    for i, (mat, dz) in enumerate((("wire_red", 0.1), ("wire_green", 0.08), ("wire_blue", 0.06), ("wire_yellow", 0.035))):
        r = rnd(40 + i)
        start = (x0 + 0.004 * i, y0, dz + 0.02)
        mid1 = (x0 - 0.012 + r.uniform(-0.006, 0.006), y0 + 0.032 + r.uniform(0, 0.012), dz - 0.01)
        mid2 = (x0 + 0.01, y0 + 0.03 + r.uniform(0, 0.01), dz - 0.07)
        end = (-0.15, E_CORE + E_PLATE + 0.002, -0.06 + 0.012 * i)
        sweep(g, bezier(start, mid1, mid2, end, n=12), 0.0032, mat, segs=6)
    return g


def build_tape_flap():
    """A loose end of tape on the right side, hinged at its top edge (origin)."""
    g = Geo()
    verts = [(-0.0015, -0.02, 0.0), (-0.0015, 0.02, 0.0), (-0.006, 0.022, -0.05), (-0.009, -0.018, -0.052)]
    g.add(verts, [(0, 1, 2, 3)], "tape")
    g.add([(v[0] + 0.0006, v[1], v[2]) for v in verts], [(3, 2, 1, 0)], "tape")
    return g


# =====================================================================================================
# Scene
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = 200


def make_object(name, g, mats, parent=None, location=(0, 0, 0)):
    return bl.make_object(name, g, mats, parent, location)


def build_bearing_scene(mats):
    root = bl.make_empty("Bearing")
    make_object("Stator", build_bearing_stator(), mats, root)
    make_object("rotor", build_bearing_rotor(), mats, root)
    return root


def build_engine_scene(mats):
    rig = {}
    root = rig["root"] = bl.make_empty("ScrapGasEngine")
    body = rig["body"] = bl.make_empty("Body", root)
    make_object("Housing", build_housing(), mats, body)
    make_object("Panel", build_panel(), mats, body)
    make_object("Wires", build_wires(), mats, body)
    flange = build_flange()
    rig["flange_l"] = make_object("FlangeL", flange, mats, body, (FLANGE_X, 0.0, -0.01))
    mirrored = Geo()
    mirrored.extend(flange, Matrix.Rotation(math.pi, 4, "Z"))
    rig["flange_r"] = make_object("FlangeR", mirrored, mats, body, (-FLANGE_X, 0.0, -0.01))
    for ob in (rig["flange_l"], rig["flange_r"]):
        ob.rotation_mode = "XYZ"
    rig["pipe_side"] = make_object("PipeSide", build_side_pipe(), mats, body)
    rig["pipe_front"] = make_object("PipeFront", build_front_pipe(), mats, body)
    rig["flap"] = make_object("TapeFlap", build_tape_flap(), mats, body, (-E_CORE - E_PLATE - 0.001, -0.12, 0.05))
    rig["rattle"] = bl.make_empty("LeverRattle", body, LEVER_AT)
    rig["lever"] = make_object("Lever", build_lever(), mats, rig["rattle"])
    rig["needle"] = make_object("Needle", build_needle(), mats, body, GAUGE_AT)
    rig["glow_green"] = make_object("GlowGreen", build_glow("glow_green"), mats, body, LAMP_GREEN)
    rig["glow_red"] = make_object("GlowRed", build_glow("glow_red"), mats, body, LAMP_RED)
    for key in ("body", "pipe_side", "pipe_front", "flap", "rattle", "lever", "needle", "glow_green", "glow_red"):
        rig[key].rotation_mode = "XYZ"
    apply_pose(rig, rest_pose())
    return rig


# =====================================================================================================
# Clips
# =====================================================================================================

def wobble(seed, t, period, cycles):
    """Smooth irregular noise in about [-1, 1] that repeats every `period` s: sines of whole cycles per period."""
    r = rnd(seed)
    total, norm = 0.0, 0.0
    for k in cycles:
        amp = 1.0 / math.sqrt(k)
        total += amp * math.sin(2 * math.pi * k * t / period + r.uniform(0, 2 * math.pi))
        norm += amp
    return total / norm * 1.6


def thumps(t, times, width=0.05, period=None):
    """A sum of sharp decaying knocks at `times` (each (time, strength)), optionally repeating every `period`."""
    v = 0.0
    for at, s in times:
        for shift in ((0.0, -period, period) if period else (0.0,)):
            d = t - at - shift
            if 0 <= d < width * 6:
                v += s * math.exp(-d / width) * math.cos(d * 2 * math.pi / (width * 3.2))
    return v


def window(t, a, b):
    return 1.0 if a <= t < b else 0.0


def rest_pose():
    return {
        "body": ((0, 0, 0), (0, 0, 0)), "pipe_side": (0, 0, 0), "pipe_front": (0, 0, 0), "flap": 0.0,
        "flange": 0.0, "flange_tilt": 0.0, "rattle": ((0, 0, 0), 0.0), "needle": NEEDLE_REST,
        "green": OFF, "red": OFF,
    }


def running(t, hard, period=2.0, scale=1.0, seed=0):
    """The running pose at time t of a loop of `period` s (scale fades the shaking in and out)."""
    amp = (0.007 if hard else 0.0035) * scale
    knocks = [(0.13, 1.0), (0.52, 0.7), (0.71, 1.25), (1.18, 0.6), (1.46, 1.1), (1.83, 0.8)]
    if hard:
        knocks += [(0.33, 0.9), (0.95, 1.2), (1.62, 0.9)]
    sp = 2.0 if hard else 1.0
    k = thumps(t, knocks, width=0.035, period=period)
    body = ((amp * wobble(seed + 1, t, period, (13, 19, 29, 37)), amp * wobble(seed + 2, t, period, (11, 23, 31)),
             amp * (0.6 * wobble(seed + 3, t, period, (17, 27, 41)) + 0.8 * k)),
            (math.radians(0.55) * scale * sp * wobble(seed + 4, t, period, (7, 15, 22)),
             math.radians(0.4) * scale * sp * wobble(seed + 5, t, period, (9, 18, 25)), 0.0))
    pipe = lambda s: tuple(amp * 0.9 * wobble(seed + s + i, t, period, (31, 43, 53)) for i in range(3))  # noqa: E731
    return {
        "body": body,
        "pipe_side": pipe(10), "pipe_front": pipe(20),
        "flap": math.radians(9 if hard else 6) * scale * wobble(seed + 30, t, period, (21, 34, 47)),
        "flange_tilt": math.radians(2.2 if hard else 1.4) * scale * wobble(seed + 31, t, period, (6, 10)),
        "flange_wobble": math.radians(14) * scale * wobble(seed + 32, t, period, (3, 5, 8)),
        "rattle": ((0, 0, 0), math.radians(2.4 if hard else 1.3) * scale * wobble(seed + 33, t, period, (37, 49, 61))),
        "needle": (NEEDLE_HARD if hard else NEEDLE_RUN) + math.radians(9 if hard else 6) * wobble(seed + 34, t, period, (23, 39, 57)),
        "green": 1.0 - 0.9 * min(1.0, sum(window(t, a, a + d) for a, d in ((0.41, 0.05), (1.07, 0.03), (1.13, 0.04), (1.71, 0.06)))),
        "red": OFF,
    }


def loop_turns(hard):
    return 4 if hard else 2


def clip_run(hard):
    period = 2.0
    turns = loop_turns(hard)

    def pose(t):
        p = running(t, hard, period)
        p["flange"] = 2 * math.pi * turns * t / period + p.pop("flange_wobble")
        return p
    return period, pose


def sputter(t, a, b, rate=31.0):
    """1 while t is in [a, b) and an uneven flicker says so, else OFF."""
    if not (a <= t < b):
        return OFF
    phase = (t - a) * rate
    return 1.0 if (math.sin(phase * 2.1) + math.sin(phase * 3.3 + 1.0)) > 0.1 else OFF


def jerk(t, at, size, width=0.06):
    """A decaying rattle of `size` rad starting at `at`."""
    d = t - at
    return 0.0 if d < 0 else size * math.exp(-d / width) * math.sin(d * 2 * math.pi / 0.045)


def clip_start():
    length = 1.0

    def pose(t):
        spin_in = max(0.0, (t - 0.35) / 0.65)
        p = running(t, False, 2.0, scale=min(1.0, spin_in * 1.3), seed=50)
        k = thumps(t, [(0.15, 1.5), (0.42, 1.0), (0.66, 0.6)], width=0.05)
        (dx, dy, dz), rot = p["body"]
        p["body"] = ((dx + 0.004 * k * 0.4, dy, dz + 0.01 * k), (rot[0] + math.radians(1.2) * k, rot[1] - math.radians(0.8) * k, 0.0))
        omega = 2 * math.pi * loop_turns(False) / 2.0
        p["flange"] = omega * 0.65 * spin_in ** 2 / 2 * 1.0 + p.pop("flange_wobble") * spin_in
        p["rattle"] = ((0, 0.003 * math.exp(-t / 0.05) if t < 0.3 else 0, 0), jerk(t, 0.0, math.radians(9)))
        settle = min(1.0, max(0.0, (t - 0.15) / 0.5))
        p["needle"] = NEEDLE_REST + (p["needle"] - NEEDLE_REST) * settle + math.radians(18) * math.sin(t * 22) * (1 - settle) * (t > 0.15)
        p["red"] = sputter(t, 0.04, 0.4)
        p["green"] = OFF if t < 0.62 else (sputter(t, 0.62, 0.8, rate=40) if t < 0.8 else p["green"])
        return p
    return length, pose


def clip_false_start():
    length = 0.7

    def pose(t):
        p = rest_pose()
        k = thumps(t, [(0.12, 1.0), (0.3, 0.45)], width=0.05)
        shake = math.exp(-max(0.0, t - 0.1) / 0.15) * (t > 0.08)
        p["body"] = ((0.003 * shake * math.sin(t * 90), 0.002 * shake * math.sin(t * 71 + 1), 0.009 * k),
                     (math.radians(0.9) * k, 0.0, 0.0))
        p["pipe_side"] = (0.0, 0.002 * shake * math.sin(t * 103), 0.0)
        p["pipe_front"] = (0.002 * shake * math.sin(t * 97), 0.0, 0.0)
        p["flap"] = math.radians(8) * shake * math.sin(t * 60)
        p["flange"] = math.pi / 2 * min(1.0, t / 0.3) * (1 - 0.3 * max(0.0, t - 0.3))
        p["rattle"] = ((0, 0, 0), jerk(t, 0.0, math.radians(9)))
        up = math.sin(min(1.0, max(0.0, (t - 0.1) / 0.45)) * math.pi)
        p["needle"] = NEEDLE_REST + (math.radians(55) - NEEDLE_REST) * up * 0.6
        p["red"] = sputter(t, 0.05, 0.28)
        return p
    return length, pose


def clip_gear(hard_after):
    length = 0.45

    def pose(t):
        surge = 1.0 + 1.2 * math.exp(-t / 0.15)
        p = running(t, hard_after, 2.0, scale=surge, seed=70)
        omega = 2 * math.pi * loop_turns(hard_after) / 2.0
        p["flange"] = omega * t + p.pop("flange_wobble") * 0.5
        p["rattle"] = ((0, 0, 0), jerk(t, 0.0, math.radians(7), width=0.08))
        p["red"] = sputter(t, 0.02, 0.12, rate=45)
        return p
    return length, pose


def clip_stop():
    length = 0.8

    def pose(t):
        fade = max(0.0, 1.0 - t / 0.6)
        p = running(t, False, 2.0, scale=fade, seed=90)
        k = thumps(t, [(0.62, 0.9)], width=0.04)
        (dx, dy, dz), rot = p["body"]
        p["body"] = ((dx, dy, dz + 0.006 * k), (rot[0] + math.radians(0.7) * k, rot[1], 0.0))
        omega = 2 * math.pi * loop_turns(False) / 2.0
        travel = omega * (t - t * t / (2 * 0.6)) if t < 0.6 else omega * 0.3
        p["flange"] = travel + p.pop("flange_wobble") * fade
        drop = min(1.0, t / 0.55)
        p["needle"] = NEEDLE_RUN + (NEEDLE_REST - NEEDLE_RUN) * drop + math.radians(10) * math.sin(t * 30) * (1 - drop)
        p["green"] = 1.0 if t < 0.32 else sputter(t, 0.32, 0.58, rate=38)
        p["red"] = sputter(t, 0.5, 0.56, rate=60)
        p["rattle"] = ((0, 0, 0), jerk(t, 0.62, math.radians(4)))
        return p
    return length, pose


def clip_off():
    length = 4.0

    def pose(t):
        p = rest_pose()
        twitch = thumps(t, [(1.3, 1.0), (3.1, 0.6)], width=0.08)
        p["needle"] = NEEDLE_REST - math.radians(3) * twitch
        return p
    return length, pose


def apply_pose(rig, p):
    (loc, rot) = p["body"]
    rig["body"].location = loc
    rig["body"].rotation_euler = Euler(rot)
    rig["pipe_side"].location = p["pipe_side"]
    rig["pipe_front"].location = p["pipe_front"]
    rig["flap"].rotation_euler = Euler((p["flap"], 0.0, 0.0))
    rig["flange_l"].rotation_euler = Euler((p["flange"], 0.0, p["flange_tilt"]))
    rig["flange_r"].rotation_euler = Euler((p["flange"], 0.0, -p["flange_tilt"]))
    (rloc, rangle) = p["rattle"]
    rig["rattle"].location = Vector(LEVER_AT) + Vector(rloc)
    rig["rattle"].rotation_euler = Euler((0.0, rangle, 0.0))
    rig["needle"].rotation_euler = Euler((0.0, p["needle"], 0.0))
    for key, value in (("glow_green", p["green"]), ("glow_red", p["red"])):
        rig[key].scale = (value, value, value)


def lever_angle(fraction):
    """The lever's angle about +Y: leaning left (positive) at the lowest gear, right at the highest."""
    return LEVER_SWING * (1 - 2 * fraction)


CLIP_OBJECTS = ("body", "pipe_side", "pipe_front", "flap", "flange_l", "flange_r", "rattle", "needle", "glow_green", "glow_red")


def bake_engine(rig):
    """Keys every clip on every moving node (each clip keys them all, so switching clips never leaves a node astray)."""
    clips = {
        "off_loop": clip_off(), "start": clip_start(), "false_start": clip_false_start(),
        "run_loop": clip_run(False), "run_hard_loop": clip_run(True), "gear": clip_gear(False), "stop": clip_stop(),
    }
    for key in CLIP_OBJECTS:
        rig[key].animation_data_create()
    for name, (length, pose) in clips.items():
        actions = {}
        for key in CLIP_OBJECTS:
            act = bpy.data.actions.new(f"{name}_{rig[key].name}")
            rig[key].animation_data.action = act
            actions[key] = act
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            p = rest_pose()
            p.update(pose(min(f / FPS, length - 1e-6) if name.endswith("_loop") and f == frames else f / FPS))
            if name.endswith("_loop") and f == frames:
                p = rest_pose()
                p.update(pose(0.0))
                if "flange" in p and name.startswith("run"):
                    p["flange"] += 2 * math.pi * loop_turns(name == "run_hard_loop")
            apply_pose(rig, p)
            for key in CLIP_OBJECTS:
                ob = rig[key]
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for key in CLIP_OBJECTS:
            lt.linear_keys(actions[key])
            bl.push_track(rig[key], actions[key], name)
    # the lever, scrubbed: lowest gear at 0 s, highest at 1 s
    lever = rig["lever"]
    lever.animation_data_create()
    act = bpy.data.actions.new("lever_Lever")
    lever.animation_data.action = act
    for f in range(0, FPS + 1, 4):
        lever.rotation_euler = Euler((0.0, lever_angle(f / FPS), 0.0))
        lever.keyframe_insert("rotation_euler", frame=f)
    lt.linear_keys(act)
    bl.push_track(lever, act, "lever")
    for ob in [*(rig[k] for k in CLIP_OBJECTS), lever]:
        ob.animation_data.use_nla = False
    apply_pose(rig, rest_pose())
    lever.rotation_euler = Euler((0.0, lever_angle(1 / 3), 0.0))


def export(root, path, animations):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root, *root.children_recursive]:
        ob.select_set(True)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_animations=animations, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=True,
        export_image_format="JPEG", export_jpeg_quality=90, export_image_quality=90, export_extras=False,
        export_cameras=False, export_lights=False)


# =====================================================================================================
# Icons
# =====================================================================================================

def render_icon(objects, path, view, ortho, target=(0, 0, 0)):
    """The objects alone on a clear background (the hotbar, inventory and Fabricator picture), 256 x 256."""
    sc = bpy.context.scene
    keep = set(objects)
    hidden = [o for o in sc.objects if o not in keep and not o.hide_render]
    for o in hidden:
        o.hide_render = True
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
    v = Vector(view).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    for name, offset, energy, size in (("Key", v * 2.5 + side * -1.6 + Vector((0, 0, 2.2)), 140, 1.6),
                                       ("Fill", v * 2.0 + side * 2.2, 45, 2.0), ("Rim", -v * 2.2 + Vector((0, 0, 1.8)), 80, 1.5)):
        light = bpy.data.lights.new("Icon" + name, "AREA")
        light.energy, light.size = energy, size
        ob = bpy.data.objects.new("Icon" + name, light)
        ob.location = Vector(target) + offset
        ob.rotation_euler = (Vector(target) - ob.location).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(ob)
        made.append(ob)
    cam = bpy.data.cameras.new("IconCam")
    cam.type = "ORTHO"
    cam.ortho_scale = ortho
    cob = bpy.data.objects.new("IconCam", cam)
    cob.location = Vector(target) + v * 6
    cob.rotation_euler = (-v).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(cob)
    made.append(cob)
    sc.camera = cob
    sc.render.resolution_x = sc.render.resolution_y = 512
    sc.render.resolution_percentage = 100
    tmp = path.with_suffix(".render.png")
    sc.render.filepath = str(tmp)
    bpy.ops.render.render(write_still=True)
    img = Image.open(tmp).convert("RGBA").resize((256, 256), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)
    tmp.unlink()
    for ob in made:
        bpy.data.objects.remove(ob)
    for o in hidden:
        o.hide_render = False
    return path


def mesh_tree(root):
    return [root, *root.children_recursive]


# =====================================================================================================
# Main
# =====================================================================================================

def main():
    reset_scene()
    mats = pt.make_materials()
    bearing = build_bearing_scene(mats)
    export(bearing, MESHES / "parts" / "bearing.glb", animations=False)
    rig = build_engine_scene(mats)
    bake_engine(rig)
    export(rig["root"], MESHES / "parts" / "scrap_gas_engine.glb", animations=True)

    # icons: the engine three-quarter from the front left (panel and flange, as in the reference), the bearing disc
    # tipped toward the camera (as in the reference's three-quarter view)
    bpy.context.scene.frame_set(0)
    for ob in mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None
    apply_pose(rig, rest_pose())
    rig["lever"].rotation_euler = Euler((0.0, lever_angle(1 / 3), 0.0))
    for ob in mesh_tree(bearing):
        ob.hide_render = True
    icon_bearing = build_bearing_scene(mats)
    icon_bearing.name = "IconBearing"
    icon_bearing.location = (3.0, 0, 0)
    icon_bearing.rotation_euler = Euler((math.radians(62), 0, math.radians(-38)))
    # aimed at the disc's middle, which sits below the block's centre
    disc_mid = icon_bearing.location + icon_bearing.rotation_euler.to_matrix() @ Vector((0, 0, (B_BOTTOM + B_TOP) / 2))
    render_icon(mesh_tree(icon_bearing), ICONS / "bearing_icon.png", view=(0.9, -2.0, 1.25), ortho=0.33,
                target=tuple(disc_mid))
    render_icon(mesh_tree(rig["root"]), ICONS / "scrap_gas_engine_icon.png", view=(1.55, 2.0, 1.45), ortho=0.98)
    for p in (MESHES / "parts" / "bearing.glb", MESHES / "parts" / "scrap_gas_engine.glb", ICONS / "bearing_icon.png", ICONS / "scrap_gas_engine_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_bearing_engine
        render_bearing_engine.render_all(bearing, rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
