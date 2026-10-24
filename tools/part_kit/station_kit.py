"""The parts map's batch 7 beds and stations (see part_kit.py, light_kit.py and working_kit.py): the Fancy Bed,
Freezer, Refiner and Anvil, and the materials the Respawn Bed's sleeping bag (tools/respawn_bed) is built from. The
Fabricator keeps its own model (tools/fabricator).

They share the part models' vocabulary (``docs/art-direction.md``: stylised, not cartoony, framed construction, small
bevels, round only where the real thing is round):

* **Colony Works** for the beds and the Freezer: bone, steel blue and graphite paint over steel, mustard pinstripes and
  steel bolts. The Respawn Bed is the colony's quilted steel-blue sleeping bag on a foam mat (it burns), the Fancy
  Bed its painted steel bed (it does not), with bone cotton ticking and a steel-blue wool blanket. The Freezer is a
  bone-enamel cabinet with a steel-blue door, a frost window and the standard two-way IO port (``docs/io-ports.md``)
  on its right side;
* **Salvager Tech** for the Refiner, a craftable machine like the Fabricator: hull-dark and gunmetal on a screwed
  base, copper bands and pipes, the Fabricator's port pair (input left, output right) at floor level;
* **Settler Patchwork** for the Anvil: the camp prop ``anvil_stump`` (tools/structures/props_camp.py) as a part, a
  graphite anvil stapled onto a strapped hardwood stump;
* **Salvager Tech** for the Field Kitchen too (consumables batch C4), the colony's cooking station: a hull-dark case in a
  gunmetal frame on the screwed base, an oven door with a window (a rack of burgers, a convection fan and two heating
  rods behind it), a graphite hot plate on top with two burner rings and a skillet, a console splashback with a screen,
  two burner knobs, a timer dial and the cyan status pip, a copper flue up its back, utensils on its left side and the
  standard two-way IO port on its right side, level with the Freezer's;
* **cyan only on powered indicators** (the Freezer's, Refiner's and Field Kitchen's status pips, lit while they work); the
  frost, refining and cooking light is the working parts' pale blue-white, never orange.

Axes are Blender's: x right, z up and the part's **front along -y** (glTF +z, which faces the builder when it is
placed, as the Fabricator's): a bed's foot and a station's door face the player who puts it down. Each part stands on
the bottom of its size box. ``build_<part>(mats)`` returns a rig: ``root``, ``clips`` ({name: (seconds, loop,
pose(t))}, maybe empty), ``rest`` and ``states`` ([(label, pose)] for the review renders; the last is the icon's).
``run`` writes ``meshes/parts/<part>.glb`` with its clips and ``textures/items/<part>_icon.png``, and with ``--render
OUT_DIR`` the review views of every state and the part's sheet. Run the part scripts (``tools/<part>/build_<part>.py``),
not this file.
"""

from __future__ import annotations

import math
import sys

import bpy
from mathutils import Matrix, Vector

import part_kit as K
import light_kit as L
import working_kit as W
from part_kit import Geo, box, cylinder, ring

lt = K.lt
pt = K.pt
sys.path.insert(0, str(K.TOOLS / "gas_container"))
sys.path.insert(0, str(K.TOOLS / "io_port"))
import build_gas_container as gc  # noqa: E402  (the standard IO port, baked into one mesh as the containers ship it)
import build_io_port as io  # noqa: E402

OFF = K.OFF
FROST = "#d6ecff"                 # frost and refining light: the working parts' pale blue-white
SIDE_IN = io.PORT_OUT - io.PORT_DEPTH + 0.01    # a wall behind a port is set in so the plate stands just proud of it
FRONT_ICON = (1.0, -1.25, 0.8)    # icon view: front right, from above (the front is -y)
COL = {"frame": "steel_blue", "dark": "graphite", "trim": "mustard_paint", "bolt": "steel", "pip": "steel"}


def materials():
    m = W.materials()
    tex = lt.textured
    fabric = K.fabric_surface
    m.update({
        # Colony bedding: bone cotton ticking with a steel-blue stripe, steel-blue wool, a bone quilt stitched in steel blue
        "ticking": tex("s_ticking", K.half_res(ticking_surface(611, "#ddd5c0", "#3e6a8c")), normal_strength=1.2),
        "wool": tex("s_wool", K.half_res(fabric(613, "#3e6a8c")), normal_strength=1.6),
        "wool_dark": tex("s_wool_dark", K.half_res(fabric(615, "#2f5373")), normal_strength=1.6),
        "sheet": tex("s_sheet", K.half_res(fabric(617, "#e6dfcc")), normal_strength=0.8),
        "quilt": tex("s_quilt", K.half_res(fabric(619, "#ddd5c0", thread="#3e6a8c", quilt=0.5)), normal_strength=2.0),
        "throw": tex("s_throw", K.half_res(fabric(621, "#d9ae2e")), normal_strength=1.4),
        # the Freezer's frost window and the ice behind it
        "frost": pt.plain("s_frost", "#cfe0e8", 0.45, 0.0),
        "ice": pt.plain("s_ice", "#b9d6e2", 0.12, 0.0),
        "frost_glow": pt.plain("s_frost_glow", FROST, 0.3, 0.0, emission=FROST, strength=2.0),
        "heat_glow": pt.plain("s_heat_glow", FROST, 0.3, 0.0, emission=FROST, strength=4.0),
        "needle": pt.plain("s_needle", "#c8423a", 0.4, 0.0),
        # the Field Kitchen's food: toasted buns and dark patties behind the oven window and in the skillet
        "bun": pt.plain("s_bun", "#c4945a", 0.6, 0.0),
        "bun_top": pt.plain("s_bun_top", "#a8703c", 0.45, 0.0),
        "patty": pt.plain("s_patty", "#4a2c20", 0.7, 0.0),
        "seed": pt.plain("s_seed", "#efe2bf", 0.6, 0.0),
    })
    return m


def ticking_surface(seed, color, stripe):
    """Mattress ticking: the seat fabric's weave with a narrow stripe every eighth of the tile."""
    import numpy as np
    albedo, height, rough, metal = K.fabric_surface(seed, color)
    size = albedo.shape[0]
    xx = np.mgrid[0:size, 0:size][1]
    band = ((xx % (size // 8)) < size // 64).astype(float)
    s = lt.srgb(stripe)[None, None, :]
    albedo = albedo * (1 - 0.8 * band[..., None]) + s * 0.8 * band[..., None]
    return albedo, height, rough, metal


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def bolts(g, points, n, mat="steel", r=0.0065):
    for p in points:
        K.screw(g, p, n, r, mat)


def pillow(g, center, length, depth, height, mat, turn=0.0):
    """A plump pillow lying along x: a squircle loft whose ends pinch to seams."""
    sections = K.smooth_sections([
        (-length / 2, 0, depth * 0.30, -height * 0.18, height * 0.18),
        (-length * 0.40, 0, depth * 0.47, -height * 0.47, height * 0.47),
        (0, 0, depth * 0.5, -height * 0.5, height * 0.5),
        (length * 0.40, 0, depth * 0.47, -height * 0.47, height * 0.47),
        (length / 2, 0, depth * 0.30, -height * 0.18, height * 0.18),
    ], steps=3)
    mtx = Matrix.Translation(center) @ Matrix.Rotation(math.radians(turn), 4, "X") @ Matrix.Rotation(-math.pi / 2, 4, "Z")
    K.squircle_loft(g, sections, mat, ring_pts=28, power=3.2, matrix=mtx)


def status_pip(m, g, root, name, at, n, r=0.0075):
    """A cyan status pip: its dark socket in `g` and the glow node (hidden while off)."""
    L.pip_socket(g, at, n, r)
    return L.pip_glow(m, root, name, at, n, r)


def port(root, center, normal, mode):
    """The standard IO port (docs/io-ports.md), one mesh node, its collar's face on the footprint boundary."""
    ob = gc.mount_io_port(root, center, normal, mode)
    ob.name = ob.data.name = f"IOPort_{mode}"
    return ob


# =====================================================================================================
# Fancy Bed (5 x 3 x 8): the colony's painted steel bed
# =====================================================================================================

def build_fancy_bed(m):
    root = K.make_empty("FancyBed")
    g = Geo()
    floor, hx, hy = -0.375, 0.625, 1.0
    post = 0.03
    px, py = hx - post - 0.008, hy - post - 0.008
    # square-tube posts: the head pair to the top, the foot pair to the footboard; chrome caps, rubber feet
    for sx in (-1, 1):
        for sy in (-1, 1):
            top = 0.345 if sy > 0 else 0.06
            box(g, (sx * px, sy * py, (floor + 0.02 + top) / 2), (post, post, (top - floor - 0.02) / 2), "graphite", bevel=0.006)
            box(g, (sx * px, sy * py, top + 0.009), (post + 0.004, post + 0.004, 0.009), "chrome", bevel=0.004)
            cylinder(g, (sx * px, sy * py, floor), (sx * px, sy * py, floor + 0.022), post * 0.9, "rubber", segs=16)
    # steel-blue box rails with a mustard pinstripe, bolted to the posts
    rz = -0.2
    for sx in (-1, 1):
        box(g, (sx * (px + 0.004), 0, rz), (0.022, py - post, 0.055), "steel_blue", bevel=0.006)
        box(g, (sx * (px + 0.0265), 0, rz + 0.032), (0.0008, py - post - 0.02, 0.003), "mustard_paint")
        bolts(g, [(sx * (px + 0.027), sy * (py - post - 0.03), rz + dz) for sy in (-1, 1) for dz in (-0.025, 0.02)], (sx, 0, 0))
    for sy in (-1, 1):
        box(g, (0, sy * (py + 0.004), rz), (px - post, 0.022, 0.055), "steel_blue", bevel=0.006)
    box(g, (0, 0, rz + 0.06), (px - post, py - post, 0.008), "graphite", bevel=0.003)
    # headboard: a graphite frame round two bone panels, a steel-blue top rail, mustard pinstripe; a lower footboard
    hb = py + 0.002

    def board(y, z0, z1, n, out):
        box(g, (0, y, z1 - 0.02), (px - post, 0.022, 0.02), "steel_blue", bevel=0.006)
        box(g, (0, y, z0 + 0.015), (px - post, 0.018, 0.015), "graphite", bevel=0.004)
        w = (2 * (px - post) - (n + 1) * 0.03) / n
        for k in range(n):
            x = -(px - post) + 0.03 + w / 2 + k * (w + 0.03)
            box(g, (x, y, (z0 + z1) / 2 - 0.005), (w / 2, 0.012, (z1 - z0) / 2 - 0.04), "bone", bevel=0.006)
            box(g, (x, y + out * 0.0125, (z0 + z1) / 2 - 0.005), (w / 2 - 0.025, 0.0008, (z1 - z0) / 2 - 0.065), "mustard_paint")
            box(g, (x, y + out * 0.0132, (z0 + z1) / 2 - 0.005), (w / 2 - 0.028, 0.0008, (z1 - z0) / 2 - 0.068), "bone")
        for k in range(n + 1):
            x = -(px - post) + 0.015 + k * (w + 0.03)
            box(g, (x, y, (z0 + z1) / 2), (0.015, 0.016, (z1 - z0) / 2 - 0.02), "graphite", bevel=0.004)
        bolts(g, [(sx * (px - post - 0.04), y + out * 0.023, z1 - 0.02) for sx in (-1, 1)], (0, out, 0))
    board(hb, -0.14, 0.33, 2, -1)
    board(-hb, -0.14, 0.05, 2, -1)
    # thick mattress with steel-blue piping, a quilted bone duvet turned down at the head, two pillows, a mustard throw
    mz0, mz1 = rz + 0.068, 0.04
    mx, my = px - post - 0.01, py - post - 0.012
    box(g, (0, 0, (mz0 + mz1) / 2), (mx, my, (mz1 - mz0) / 2), "ticking", bevel=0.035)
    for z in (mz0 + 0.012, mz1 - 0.012):
        for sx in (-1, 1):
            cylinder(g, (sx * (mx - 0.004), -my + 0.03, z), (sx * (mx - 0.004), my - 0.03, z), 0.006, "wool_dark", segs=8)
    dy0, dy1 = -py + post - 0.004, 0.5
    box(g, (0, (dy0 + dy1) / 2, mz1 - 0.06), (mx + 0.008, (dy1 - dy0) / 2, 0.075), "quilt", bevel=0.03)
    box(g, (0, dy1 - 0.08, mz1 + 0.02), (mx + 0.009, 0.085, 0.009), "wool", bevel=0.007)
    cylinder(g, (-(mx + 0.009), dy1 + 0.004, mz1 + 0.016), (mx + 0.009, dy1 + 0.004, mz1 + 0.016), 0.02, "wool", segs=16)
    for sx in (-1, 1):
        pillow(g, (sx * 0.26, my - 0.2, mz1 + 0.05), 0.46, 0.3, 0.11, "sheet", turn=-14)
    box(g, (0, -py + 0.3, mz1 + 0.024), (mx + 0.012, 0.16, 0.012), "throw", bevel=0.009)
    for sx in (-1, 1):
        box(g, (sx * (mx + 0.016), -py + 0.3, mz1 - 0.03), (0.006, 0.16, 0.055), "throw", bevel=0.004)
    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}}
    rig["rest"] = lambda: None
    rig["states"] = [("MADE", lambda: None)]
    return rig


# =====================================================================================================
# Freezer (3 x 4 x 3): a bone-enamel cabinet with a steel-blue door, a frost window and a two-way port on its right
# =====================================================================================================

def build_freezer(m):
    root = K.make_empty("Freezer")
    g = Geo()
    floor, h = -0.5, 0.375
    xr = h - SIDE_IN                # right wall set in behind the port
    xl = -h + 0.012
    y0, y1 = -h + 0.095, h - 0.012  # the cabinet; the door, its window and handle stand out in front of y0
    z0, z1 = floor + 0.055, 0.455
    xc = (xl + xr) / 2
    # rubber feet, a graphite kick plinth, the bone cabinet, a graphite top cap
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder(g, (sx * 0.29, sy * 0.27, floor), (sx * 0.29, sy * 0.27, floor + 0.01), 0.022, "rubber", segs=14)
    box(g, (xc, (y0 + y1) / 2, floor + 0.035), ((xr - xl) / 2 - 0.01, (y1 - y0) / 2 - 0.01, 0.025), "graphite", bevel=0.006)
    box(g, (xc, (y0 + y1) / 2, (z0 + z1) / 2), ((xr - xl) / 2, (y1 - y0) / 2, (z1 - z0) / 2), "bone", bevel=0.012)
    box(g, (xc, (y0 + y1) / 2 + 0.004, z1 + 0.014), ((xr - xl) / 2 + 0.004, (y1 - y0) / 2 + 0.004, 0.016), "graphite", bevel=0.006)
    # mustard pinstripe round the cabinet under the cap
    box(g, (xc, (y0 + y1) / 2, z1 - 0.035), ((xr - xl) / 2 + 0.0008, (y1 - y0) / 2 + 0.0008, 0.005), "mustard_paint")
    # the door: steel blue on a rubber gasket, two graphite hinges on the left, a chrome lever on the right
    dz0, dz1 = z0 + 0.03, z1 - 0.07
    dx = (xr - xl) / 2 - 0.022
    df = y0 - 0.028                 # the door's face
    box(g, (xc, y0 - 0.003, (dz0 + dz1) / 2), (dx + 0.006, 0.004, (dz1 - dz0) / 2 + 0.006), "rubber", bevel=0.002)
    box(g, (xc, y0 - 0.016, (dz0 + dz1) / 2), (dx, 0.012, (dz1 - dz0) / 2), "steel_blue", bevel=0.008)
    for z in (dz0 + 0.1, dz1 - 0.1):
        box(g, (xl + 0.012, y0 - 0.014, z), (0.012, 0.016, 0.04), "graphite", bevel=0.004)
        cylinder(g, (xl + 0.004, y0 - 0.024, z - 0.045), (xl + 0.004, y0 - 0.024, z + 0.045), 0.009, "steel", segs=10)
    hx_ = xr - 0.07
    zc = (dz0 + dz1) / 2
    box(g, (hx_, df - 0.005, zc + 0.08), (0.016, 0.006, 0.03), "graphite", bevel=0.003)
    box(g, (hx_, df - 0.005, zc - 0.14), (0.016, 0.006, 0.03), "graphite", bevel=0.003)
    K.sweep(g, [Vector((hx_, df - 0.008, zc + 0.09)), Vector((hx_, df - 0.04, zc + 0.07)),
                Vector((hx_, df - 0.04, zc - 0.13)), Vector((hx_, df - 0.008, zc - 0.15))], 0.011, "chrome", segs=10)
    # frost window in the door's upper half: a graphite bezel round a frosted pane, frosted ice blocks stacked in it
    wz0, wz1 = dz1 - 0.27, dz1 - 0.06
    wx = dx - 0.07
    box(g, (xc, df - 0.002, (wz0 + wz1) / 2), (wx + 0.016, 0.004, (wz1 - wz0) / 2 + 0.016), "graphite", bevel=0.003)
    box(g, (xc, df - 0.003, (wz0 + wz1) / 2), (wx, 0.0035, (wz1 - wz0) / 2), "frost", bevel=0.001)
    for x, z in [(-0.1, 0.0), (0.0, 0.0), (0.1, 0.0), (-0.05, 0.088), (0.05, 0.088)]:
        box(g, (xc + x, df - 0.0075, wz0 + 0.05 + z), (0.042, 0.0012, 0.04), "ice", bevel=0.001)
    bolts(g, [(xc + sx * (wx + 0.008), df - 0.006, z) for sx in (-1, 1) for z in (wz0 - 0.008, wz1 + 0.008)], (0, -1, 0), "steel", 0.004)
    # the colony's freezer mark on the door's lower half: three bone squares (ice blocks) on a mustard bar
    box(g, (xc, df - 0.0008, dz0 + 0.075), (0.11, 0.0008, 0.006), "mustard_paint")
    for k in range(3):
        box(g, (xc - 0.06 + k * 0.06, df - 0.0008, dz0 + 0.115), (0.022, 0.0008, 0.022), "bone")
    # control strip on the cap's front: a temperature dial with a red needle and the cyan status pip
    cz = z1 + 0.014
    K.disc(g, (xc - 0.08, y0 - 0.0015, cz), (0, -1, 0), 0.0105, "dial", segs=20)
    rg = Geo()
    ring(rg, -0.002, 0.002, 0.0105, 0.0135, "steel", segs=20)
    g.extend(rg, L.axis_matrix((xc - 0.08, y0 - 0.001, cz), (0, -1, 0)))
    box(g, (xc - 0.08 + 0.003, y0 - 0.0025, cz + 0.003), (0.0012, 0.0006, 0.0065), "needle", rot=Matrix.Rotation(math.radians(-40), 3, "Y"))
    pip = status_pip(m, g, root, "Pip", (xc + 0.08, y0 - 0.0005, cz), (0, -1, 0), r=0.007)
    # back: the compressor fan behind a round graphite guard
    fz = floor + 0.21
    gy = y1
    rg = Geo()
    ring(rg, -0.004, 0.004, 0.088, 0.1, "graphite", segs=32)
    g.extend(rg, L.axis_matrix((xc, gy + 0.006, fz), (0, 1, 0)))
    for k in range(4):
        box(g, (xc, gy + 0.009, fz), (0.092, 0.0025, 0.004), "graphite", rot=Matrix.Rotation(math.pi * k / 4, 3, "Y"))
    cylinder(g, (xc, gy + 0.008, fz), (xc, gy + 0.0115, fz), 0.018, "graphite", segs=16)
    K.disc(g, (xc, gy + 0.0005, fz), (0, 1, 0), 0.088, "hole", segs=32)
    bolts(g, [(xc + 0.094 * math.cos(a), gy + 0.01, fz + 0.094 * math.sin(a)) for a in (0.4, 2.0, 3.6, 5.2)], (0, 1, 0), "steel", 0.004)
    # right side: the two-way port (water in, ice out), its plate just proud of the set-in wall
    port(root, (h, 0.0, floor + 0.375), (1, 0, 0), "both")
    K.make_object("Body", g, m, root)
    fan_g = Geo()
    cylinder(fan_g, (0, -0.001, 0), (0, 0.003, 0), 0.02, "gun_dark", segs=16)
    for k in range(5):
        a = 2 * math.pi * k / 5
        box(fan_g, (math.cos(a) * 0.05, 0, math.sin(a) * 0.05), (0.034, 0.0012, 0.014), "gun", bevel=0.0008,
            rot=Matrix.Rotation(-a, 3, "Y") @ Matrix.Rotation(math.radians(20), 3, "X"))
    fan = K.make_object("Fan", fan_g, m, root, (xc, gy + 0.0035, fz))
    glow = K.make_object("Frost", K.glow_box((wx - 0.004, 0.001, (wz1 - wz0) / 2 - 0.004), "frost_glow"), m, root,
                         (xc, df - 0.0063, (wz0 + wz1) / 2))
    rig = {"root": root}
    W.keep(rig, fan, glow, pip)

    def at(spin, cold, power):
        fan.rotation_euler.y = spin
        W.lit(glow, cold)
        W.lit(pip, power)
    rig["rest"] = lambda: at(0, 0, 0)
    turn = 2 * math.pi
    rig["clips"] = {
        "idle_loop": (4.0, True, lambda t: at(turn * t / 4.0, 0, 0)),
        "start": (0.6, False, lambda t: at(turn * 1.5 * (t / 0.6) ** 2, K.smooth(t / 0.6), K.smooth(t / 0.15))),
        "work_loop": (2.0, True, lambda t: at(turn * 4 * t / 2.0, 0.85 + 0.15 * math.cos(2 * math.pi * t / 2.0), 1)),
        "complete": (0.8, False, lambda t: at(turn * (2 * t - t * t / 0.8), 1 - K.smooth(t / 0.5), 1 - K.smooth(t / 0.2))),
    }
    rig["states"] = [("IDLE", lambda: at(0, 0, 0)), ("FREEZING", lambda: at(0.3, 1, 1))]
    return rig


# =====================================================================================================
# Refiner (4 x 6 x 4): a Salvager refining drum on a framed case, a hopper on top, the Fabricator's port pair
# =====================================================================================================

def build_refiner(m):
    root = K.make_empty("Refiner")
    f = W.SAL
    g = Geo()
    floor, h = -0.75, 0.5
    port_y, port_z = -0.125, floor + 0.375
    xs = h - SIDE_IN
    # screwed base plate under the whole footprint (set in at the port sides)
    top = W.base_plate(g, floor, xs, h - 0.012, f, t=0.014, inset=0.03)
    # the lower case: hull core, gunmetal frame rails and corner posts, inset panels
    c0, c1 = top, floor + 0.7
    cy0, cy1 = -h + 0.1, h - 0.04
    box(g, (0, (cy0 + cy1) / 2, (c0 + c1) / 2), (xs - 0.004, (cy1 - cy0) / 2, (c1 - c0) / 2), "hull", bevel=0.01)
    for sx in (-1, 1):
        for sy in (cy0, cy1):
            box(g, (sx * (xs - 0.025), sy, (c0 + c1) / 2), (0.025, 0.025, (c1 - c0) / 2), "gun", bevel=0.006)
        for z in (c0 + 0.022, c1 - 0.022):
            box(g, (sx * (xs - 0.025), (cy0 + cy1) / 2, z), (0.025, (cy1 - cy0) / 2, 0.022), "gun", bevel=0.006)
    for sy in (cy0, cy1):
        for z in (c0 + 0.022, c1 - 0.022):
            box(g, (0, sy, z), (xs - 0.05, 0.025, 0.022), "gun", bevel=0.006)
    # front: an output hatch with a copper lip and a tray, a console with a screen, a pressure gauge and the pip
    fy = cy0 - 0.025
    box(g, (0, fy - 0.002, c0 + 0.17), (0.2, 0.012, 0.09), "gun_dark", bevel=0.006)
    box(g, (0, fy - 0.012, c0 + 0.17), (0.16, 0.004, 0.06), "hole")
    box(g, (0, fy - 0.014, c0 + 0.232), (0.17, 0.006, 0.006), "copper", bevel=0.002)
    box(g, (0, fy - 0.035, c0 + 0.085), (0.16, 0.035, 0.008), "steel", bevel=0.003)
    for sx in (-1, 1):
        box(g, (sx * 0.162, fy - 0.035, c0 + 0.105), (0.004, 0.035, 0.025), "steel", bevel=0.002)
    L.plate(g, (0, fy - 0.004, c1 - 0.17), (0.22, 0.008, 0.11), "gun_dark", f["bolt"], n=(0, -1, 0), inset=0.016, r=0.006)
    box(g, (-0.06, fy - 0.013, c1 - 0.165), (0.1, 0.002, 0.06), "screen", bevel=0.002)
    for k in range(4):
        box(g, (-0.13 + k * 0.045, fy - 0.0145, c1 - 0.2 + (k % 2) * 0.02), (0.016, 0.0008, 0.004 + 0.01 * (k % 3)), "teal_glow")
    gx, gz = 0.14, c1 - 0.165
    cylinder(g, (gx, fy - 0.012, gz), (gx, fy - 0.03, gz), 0.045, "copper", segs=24)
    K.disc(g, (gx, fy - 0.0305, gz), (0, -1, 0), 0.038, "dial", segs=24)
    pip = status_pip(m, g, root, "Pip", (gx, fy - 0.014, gz - 0.075), (0, -1, 0), r=0.008)
    # the refining drum: a steel-banded gunmetal cylinder with an inspection window on the front
    d0, d1, dr = c1 + 0.005, c1 + 0.465, 0.32
    cylinder(g, (0, 0.02, d0), (0, 0.02, d0 + 0.03), dr + 0.03, "gun_dark", segs=40)
    K.lathe(g, [(0.0, d0 + 0.03), (dr, d0 + 0.03), (dr, d1 - 0.05), (dr - 0.06, d1), (0.0, d1)], "gun", steps=40, center=(0, 0.02))
    for z in (d0 + 0.09, d1 - 0.11):
        ring(g, z, z + 0.035, dr - 0.002, dr + 0.012, "copper", segs=40, center=(0, 0.02))
    wz = (d0 + d1) / 2 - 0.02
    win = Geo()
    cylinder(win, (0, 0, -0.03), (0, 0, 0.012), 0.11, "copper", segs=32)
    cylinder(win, (0, 0, 0.0), (0, 0, 0.0135), 0.085, "hole", segs=32)
    g.extend(win, L.axis_matrix((0, 0.02 - dr + 0.002, wz), (0, -1, 0)))
    for k in range(8):
        a = 2 * math.pi * k / 8
        K.screw(g, (math.cos(a) * 0.098, 0.02 - dr - 0.0095, wz + math.sin(a) * 0.098), (0, -1, 0), 0.006, "copper")
    # hopper on top: a steel funnel on a hull collar, the stirrer motor on a bridge over it
    h0 = d1 - 0.005
    K.lathe(g, [(0.12, h0), (0.15, h0), (0.15, h0 + 0.03), (0.24, h0 + 0.17), (0.235, h0 + 0.18), (0.14, h0 + 0.035), (0.11, h0 + 0.035)],
            "steel", steps=40, center=(0, 0.02))
    ring(g, h0 + 0.165, h0 + 0.185, 0.232, 0.246, "hull", segs=40, center=(0, 0.02))
    box(g, (0, 0.02, h0 + 0.19), (0.25, 0.022, 0.012), "gun", bevel=0.004)
    box(g, (0, 0.02, h0 + 0.215), (0.045, 0.045, 0.03), "hull", bevel=0.006)
    # back: exhaust stack and copper feed pipes from the drum down into the case
    K.sweep(g, K.bezier((0.22, 0.25, c1 - 0.05), (0.32, 0.36, c1 + 0.1), (0.34, 0.36, d1), (0.34, 0.36, h0 + 0.24)), 0.035, "steel", segs=16)
    cylinder(g, (0.34, 0.36, h0 + 0.24), (0.34, 0.36, h0 + 0.26), 0.05, "hull", segs=20)
    for sx in (-1, 1):
        K.sweep(g, K.bezier((sx * 0.3, 0.12, d0 + 0.25), (sx * 0.4, 0.12, d0 + 0.2), (sx * 0.4, 0.16, c1 - 0.05), (sx * 0.36, 0.16, c1 - 0.2)),
                0.014, "copper", segs=10)
    # the ports: input on the left, output on the right, half a block toward the front (as the Fabricator's)
    port(root, (-h, port_y, port_z), (-1, 0, 0), "in")
    port(root, (h, port_y, port_z), (1, 0, 0), "out")
    K.make_object("Body", g, m, root)
    # moving pieces: the stirrer turning in the hopper, the gauge needle, the window glow
    st = Geo()
    cylinder(st, (0, 0, -0.12), (0, 0, 0.0), 0.012, "steel", segs=10)
    for k in range(3):
        a = 2 * math.pi * k / 3
        box(st, (math.cos(a) * 0.05, math.sin(a) * 0.05, -0.1), (0.05, 0.006, 0.018), "copper", bevel=0.002, rot=Matrix.Rotation(a, 3, "Z"))
    stirrer = K.make_object("Stirrer", st, m, root, (0, 0.02, h0 + 0.185))
    nd = Geo()
    box(nd, (0, 0, 0.016), (0.0025, 0.0012, 0.02), "needle")
    K.disc(nd, (0, -0.0015, 0), (0, -1, 0), 0.005, "hull", segs=12)
    needle = K.make_object("Needle", nd, m, root, (gx, fy - 0.0325, gz))
    glow = K.make_object("Melt", K.glow_disc(0.084, (0, -1, 0), "heat_glow", depth=0.004, segs=32), m, root,
                         (0, 0.02 - dr - 0.012, wz))
    rig = {"root": root}
    W.keep(rig, stirrer, needle, glow, pip)

    def at(spin, heat, power, needle_deg):
        stirrer.rotation_euler.z = spin
        W.lit(glow, heat)
        W.lit(pip, power)
        needle.rotation_euler.y = math.radians(needle_deg)
    rig["rest"] = lambda: at(0, 0, 0, -60)
    turn = 2 * math.pi
    rig["clips"] = {
        "idle_loop": (4.0, True, lambda t: at(turn * t / 4.0, 0, 0, -60 + 2 * math.sin(2 * math.pi * t / 4.0))),
        "start": (0.8, False, lambda t: at(turn * (t / 0.8) ** 2, K.smooth(t / 0.8), K.smooth(t / 0.2), -60 + 95 * K.sink(t, 0.3))),
        "work_loop": (2.0, True, lambda t: at(turn * 2 * t / 2.0, 0.8 + 0.2 * math.cos(2 * math.pi * t), 1,
                                             35 + 4 * math.sin(2 * math.pi * 3 * t / 2.0) + 2 * math.sin(2 * math.pi * 5 * t / 2.0))),
        "complete": (0.8, False, lambda t: at(turn * (t - t * t / 1.6), 1 - K.smooth(t / 0.6), 1 - K.smooth(t / 0.3), 35 - 95 * K.smooth(t / 0.6))),
    }
    rig["states"] = [("IDLE", rig["rest"]), ("REFINING", lambda: at(0.4, 1, 1, 35))]
    return rig


# =====================================================================================================
# Field Kitchen (3 x 4 x 3): a Salvager cooking station: oven with a window, hot plate, console, a two-way port
# =====================================================================================================

def burger(g, at, r, squash=1.0, seeds=True):
    """A little burger (bun, patty, domed top bun) standing at `at`, `r` across its patty, its depth (y) squashed."""
    k = r / 0.045
    s = Geo()
    K.lathe(s, [(0.0, 0.0), (r * 0.9, 0.0), (r * 0.97, 0.005 * k), (r * 0.97, 0.012 * k), (0.0, 0.012 * k)], "bun", steps=28)
    K.lathe(s, [(0.0, 0.012 * k), (r * 1.03, 0.012 * k), (r * 1.06, 0.019 * k), (r * 1.03, 0.027 * k), (0.0, 0.027 * k)],
          "patty", steps=28)
    K.lathe(s, [(0.0, 0.027 * k), (r * 0.98, 0.027 * k), (r, 0.034 * k), (r * 0.9, 0.047 * k), (r * 0.6, 0.058 * k),
              (r * 0.25, 0.062 * k), (0.0, 0.063 * k)], "bun_top", steps=28)
    if seeds:
        for a, rr in ((0.3, 0.45), (1.4, 0.55), (2.5, 0.35), (3.3, 0.6), (4.4, 0.4), (5.4, 0.55), (0.9, 0.15)):
            x, y = math.cos(a) * r * rr, math.sin(a) * r * rr
            zz = 0.063 * k - 0.016 * k * rr * rr
            box(s, (x, y, zz), (0.0035 * k, 0.002 * k, 0.0012 * k), "seed", rot=Matrix.Rotation(a, 3, "Z"))
    g.extend(s, Matrix.Translation(at) @ Matrix.Diagonal((1.0, squash, 1.0, 1.0)))


def build_field_kitchen(m):
    root = K.make_empty("FieldKitchen")
    f = W.SAL
    g = Geo()
    floor, h = -0.5, 0.375
    xs = h - SIDE_IN                       # both side walls set in: the port on the right, the utensils on the left
    top = W.base_plate(g, floor, xs, h - 0.012, f, t=0.014, inset=0.03)
    c0, c1 = top, 0.2                      # the case; the hot plate sits on it
    cy0, cy1 = -h + 0.11, h - 0.04         # the case's front (the door stands out of it) and back (the flue behind it)
    # the case: a hull core in a gunmetal frame of corner posts and rails
    box(g, (0, (cy0 + cy1) / 2, (c0 + c1) / 2), (xs - 0.004, (cy1 - cy0) / 2, (c1 - c0) / 2), "hull", bevel=0.01)
    for sx in (-1, 1):
        for sy in (cy0, cy1):
            box(g, (sx * (xs - 0.022), sy, (c0 + c1) / 2), (0.022, 0.022, (c1 - c0) / 2), "gun", bevel=0.006)
        for z in (c0 + 0.02, c1 - 0.02):
            box(g, (sx * (xs - 0.022), (cy0 + cy1) / 2, z), (0.022, (cy1 - cy0) / 2, 0.02), "gun", bevel=0.006)
    for sy in (cy0, cy1):
        for z in (c0 + 0.02, c1 - 0.02):
            box(g, (0, sy, z), (xs - 0.044, 0.022, 0.02), "gun", bevel=0.006)
    # the oven door: a gunmetal frame round a window, a copper band round the window, screws, a chrome bar handle
    dz0, dz1, dx = -0.215, 0.135, 0.27
    wz0, wz1, wx = -0.17, 0.09, 0.19
    yb, yf = cy0 - 0.022, cy0 - 0.067      # the door's back (proud of the front rails) and face
    yc, hy = (yb + yf) / 2, (yb - yf) / 2
    box(g, (0, (cy0 + yb) / 2, (dz0 + dz1) / 2), (dx - 0.01, (cy0 - yb) / 2, (dz1 - dz0) / 2 - 0.01), "hull")
    for x0, x1, z0, z1 in ((-dx, dx, wz1, dz1), (-dx, dx, dz0, wz0), (-dx, -wx, wz0, wz1), (wx, dx, wz0, wz1)):
        box(g, ((x0 + x1) / 2, yc, (z0 + z1) / 2), ((x1 - x0) / 2, hy, (z1 - z0) / 2), "gun_dark", bevel=0.002)
    t = 0.007
    for cx, cz, bx, bz in ((0, wz1 + t, wx + 2 * t, t), (0, wz0 - t, wx + 2 * t, t),
                           (-wx - t, (wz0 + wz1) / 2, t, (wz1 - wz0) / 2), (wx + t, (wz0 + wz1) / 2, t, (wz1 - wz0) / 2)):
        box(g, (cx, yf - 0.0025, cz), (bx, 0.003, bz), "copper", bevel=0.0015)
    bolts(g, [(sx * (dx - 0.024), yf - 0.0005, z) for sx in (-1, 1) for z in (dz0 + 0.022, dz1 - 0.022)], (0, -1, 0), "copper", 0.0065)
    hz = dz1 - 0.024
    for sx in (-1, 1):
        box(g, (sx * 0.17, yf - 0.006, hz), (0.012, 0.006, 0.014), "gun", bevel=0.003)
    K.sweep(g, [Vector((-0.17, yf - 0.008, hz)), Vector((-0.17, yf - 0.034, hz)), Vector((0.17, yf - 0.034, hz)),
                Vector((0.17, yf - 0.008, hz))], 0.0085, "chrome", segs=10)
    # inside the window: the dark oven back, a wire rack with two burgers on it, the heating rods top and bottom
    box(g, (0, yb - 0.0015, (wz0 + wz1) / 2), (wx, 0.0015, (wz1 - wz0) / 2), "hole")
    zr = -0.112
    for y in (yb - 0.01, yb - 0.023, yb - 0.036):
        cylinder(g, (-wx, y, zr), (wx, y, zr), 0.0022, "steel", segs=8)
    for x in (-0.085, 0.085):
        burger(g, (x, yc + 0.003, zr + 0.002), 0.05, squash=0.34)
    rods = (wz1 - 0.016, wz0 + 0.012)
    for z in rods:
        cylinder(g, (-wx, yb - 0.012, z), (wx, yb - 0.012, z), 0.0042, "gun_dark", segs=10)
    # the convection fan's guard on the oven back
    fz = 0.022
    gr = Geo()
    ring(gr, -0.002, 0.002, 0.04, 0.045, "graphite", segs=32)
    g.extend(gr, L.axis_matrix((0, yb - 0.006, fz), (0, -1, 0)))
    # under the door: a pantry drawer with louvres and a copper pull
    rz0, rz1 = c0 + 0.045, dz0 - 0.02
    rzc = (rz0 + rz1) / 2
    L.plate(g, (0, cy0 - 0.012, rzc), (dx, 0.012, (rz1 - rz0) / 2), "gun_dark", "copper", n=(0, -1, 0), inset=0.018, r=0.006)
    box(g, (0, cy0 - 0.03, rz1 - 0.04), (0.09, 0.007, 0.009), "copper", bevel=0.003)
    for sx in (-1, 1):
        box(g, (sx * 0.085, cy0 - 0.028, rz1 - 0.04), (0.007, 0.005, 0.011), "gun", bevel=0.002)
    for k in range(5):
        box(g, (0, cy0 - 0.0245, rz0 + 0.035 + k * 0.022), (0.2, 0.001, 0.0045), "hole")
    # the hot plate: a graphite slab with a steel front lip, two burner rings, a skillet with a patty on the right one
    ct = c1 + 0.022
    box(g, (0, (cy0 - 0.004 + 0.2) / 2, c1 + 0.011), (xs - 0.004, (0.2 - cy0 + 0.004) / 2, 0.011), "graphite", bevel=0.004)
    box(g, (0, cy0 - 0.006, c1 + 0.011), (xs - 0.002, 0.004, 0.013), "steel", bevel=0.002)
    burners = ((-0.15, -0.07), (0.135, -0.06))
    for bx, by in burners:
        ring(g, ct - 0.001, ct + 0.003, 0.052, 0.062, "gun_dark", segs=40, center=(bx, by))
        ring(g, ct - 0.001, ct + 0.0025, 0.026, 0.034, "steel", segs=32, center=(bx, by))
        cylinder(g, (bx, by, ct - 0.001), (bx, by, ct + 0.002), 0.012, "hole", segs=16)
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            box(g, (bx + math.cos(a) * 0.044, by + math.sin(a) * 0.044, ct + 0.004), (0.012, 0.003, 0.002), "gun",
                rot=Matrix.Rotation(a, 3, "Z"))
    sx_, sy_ = burners[1]
    K.lathe(g, [(0.0, ct + 0.006), (0.06, ct + 0.006), (0.068, ct + 0.012), (0.071, ct + 0.034), (0.066, ct + 0.034),
              (0.061, ct + 0.014), (0.056, ct + 0.011), (0.0, ct + 0.011)], "gun_dark", steps=40, center=(sx_, sy_))
    hd = Vector((0.55, -0.83, 0.0)).normalized()
    p0 = Vector((sx_, sy_, ct + 0.028)) + hd * 0.066
    K.sweep(g, [p0, p0 + hd * 0.05 + Vector((0, 0, 0.006)), p0 + hd * 0.11 + Vector((0, 0, 0.01))], 0.0065, "steel", segs=10)
    cylinder(g, p0 + hd * 0.06 + Vector((0, 0, 0.007)), p0 + hd * 0.115 + Vector((0, 0, 0.01)), 0.0085, "rubber", segs=12)
    burger_patty = Geo()
    K.lathe(burger_patty, [(0.0, 0.0), (0.036, 0.0), (0.039, 0.006), (0.036, 0.012), (0.0, 0.012)], "patty", steps=28)
    g.extend(burger_patty, Matrix.Translation((sx_, sy_, ct + 0.011)))
    # the console splashback: a gunmetal block, a hull face plate with a screen, two burner knobs, the timer dial, the pip
    yk = 0.2
    zt = 0.49
    zc = (ct + zt) / 2
    box(g, (0, (yk + cy1) / 2, (ct + zt) / 2), (xs - 0.004, (cy1 - yk) / 2, (zt - ct) / 2), "gun", bevel=0.008)
    L.plate(g, (0, yk - 0.004, zc), (0.29, 0.006, 0.105), "hull", "copper", n=(0, -1, 0), inset=0.016, r=0.006)
    box(g, (-0.1, yk - 0.0105, zc + 0.022), (0.125, 0.0012, 0.058), "screen", bevel=0.002)
    for k in range(3):                       # the screen's readout: a progress bar and two lines, in the console's teal
        box(g, (-0.1 - 0.035 + k * 0.035 * (k < 2), yk - 0.0118, zc + 0.05 - k * 0.028),
            (0.075 - 0.02 * k, 0.0006, 0.006), "teal_glow")
    pip = status_pip(m, g, root, "Pip", (-0.19, yk - 0.01, zc - 0.07), (0, -1, 0), r=0.008)
    for kx in (0.1, 0.19):
        K.disc(g, (kx, yk - 0.0105, zc + 0.045), (0, -1, 0), 0.024, "dial", segs=24)
        cylinder(g, (kx, yk - 0.01, zc + 0.045), (kx, yk - 0.03, zc + 0.045), 0.013, "gun_dark", segs=16)
        box(g, (kx, yk - 0.031, zc + 0.052), (0.0018, 0.001, 0.006), "copper")
    tx, tz = 0.145, zc - 0.055
    cylinder(g, (tx, yk - 0.008, tz), (tx, yk - 0.02, tz), 0.034, "copper", segs=28)
    K.disc(g, (tx, yk - 0.0205, tz), (0, -1, 0), 0.028, "dial", segs=28)
    for k in range(12):
        a = 2 * math.pi * k / 12
        box(g, (tx + math.sin(a) * 0.023, yk - 0.021, tz + math.cos(a) * 0.023), (0.0012, 0.0006, 0.0035), "hull",
            rot=Matrix.Rotation(-a, 3, "Y"))
    # on top of the console: copper trim and vent slots
    box(g, (0, (yk + cy1) / 2, zt - 0.012), (xs - 0.002, (cy1 - yk) / 2 + 0.002, 0.004), "copper", bevel=0.0015)
    for k in range(6):
        box(g, (-0.2 + k * 0.08, (yk + cy1) / 2, zt + 0.0005), (0.028, 0.045, 0.001), "hole")
    # the back: a louvred vent panel and a copper flue from the case up into the console
    L.plate(g, (0.1, cy1 + 0.006, -0.18), (0.16, 0.006, 0.15), "gun_dark", "copper", n=(0, 1, 0), inset=0.016, r=0.006)
    for k in range(7):
        box(g, (0.1, cy1 + 0.0125, -0.29 + k * 0.035), (0.13, 0.0015, 0.006), "hole")
    fy = h - 0.022
    K.sweep(g, K.bezier((-0.2, cy1, -0.32), (-0.2, fy, -0.32), (-0.2, fy, -0.2), (-0.2, fy, 0.0)), 0.019, "copper", segs=16)
    K.sweep(g, [Vector((-0.2, fy, 0.0)), Vector((-0.2, fy, 0.38)), Vector((-0.2, fy - 0.0001, 0.4))], 0.019, "copper", segs=16)
    K.sweep(g, K.bezier((-0.2, fy, 0.4), (-0.2, fy, 0.44), (-0.2, cy1 + 0.005, 0.44), (-0.2, cy1 - 0.01, 0.44)), 0.019, "copper", segs=16)
    for z in (-0.32, -0.05, 0.25):
        box(g, (-0.2, (cy1 + fy) / 2, z if z > -0.3 else -0.25), (0.026, (fy - cy1) / 2 + 0.004, 0.008), "gun", bevel=0.002)
    # the left side: a steel rail with a spatula and a ladle hanging from it
    xr_ = -xs - 0.014
    for y in (-0.16, 0.16):
        box(g, ((-xs + xr_) / 2, y, 0.08), (0.009, 0.008, 0.008), "gun", bevel=0.002)
    cylinder(g, (xr_, -0.19, 0.08), (xr_, 0.19, 0.08), 0.0045, "steel", segs=10)
    cylinder(g, (xr_ + 0.002, -0.06, 0.075), (xr_ + 0.002, -0.06, -0.08), 0.005, "gun_dark", segs=10)
    box(g, (xr_ + 0.002, -0.06, -0.12), (0.0015, 0.038, 0.045), "steel", bevel=0.002)
    for k in range(3):
        box(g, (xr_ + 0.001, -0.075 + k * 0.015, -0.12), (0.0018, 0.003, 0.03), "hole")
    cylinder(g, (xr_ + 0.002, 0.07, 0.075), (xr_ + 0.002, 0.07, -0.1), 0.004, "steel", segs=10)
    ladle = Geo()
    K.lathe(ladle, [(0.0, -0.012), (0.012, -0.011), (0.02, -0.005), (0.021, 0.0), (0.018, 0.0), (0.0, -0.009)], "steel", steps=28)
    g.extend(ladle, Matrix.Translation((xr_ - 0.002, 0.07, -0.115)) @ Matrix.Rotation(-math.pi / 2, 4, "Y"))
    # the right side: the standard two-way port (ingredients in, food out), level with the Freezer's
    port(root, (h, 0.0, floor + 0.375), (1, 0, 0), "both")
    K.make_object("Body", g, m, root)
    # moving pieces: the oven fan, the timer needle, the oven's heating rods and the burner rings glowing, the pip
    fan_g = Geo()
    cylinder(fan_g, (0, -0.001, 0), (0, 0.004, 0), 0.012, "copper", segs=16)
    for k in range(6):
        a = 2 * math.pi * k / 6
        box(fan_g, (math.cos(a) * 0.022, 0, math.sin(a) * 0.022), (0.016, 0.001, 0.007), "gun", bevel=0.0006,
            rot=Matrix.Rotation(-a, 3, "Y") @ Matrix.Rotation(math.radians(25), 3, "X"))
    fan = K.make_object("Fan", fan_g, m, root, (0, yb - 0.004, fz))
    nd = Geo()
    box(nd, (0, 0, 0.011), (0.0022, 0.001, 0.014), "needle")
    K.disc(nd, (0, -0.0015, 0), (0, -1, 0), 0.0045, "hull", segs=12)
    needle = K.make_object("Needle", nd, m, root, (tx, yk - 0.0225, tz))
    og = Geo()
    zo = (wz0 + wz1) / 2
    for z in rods:
        cylinder(og, (-wx + 0.004, 0, z - zo), (wx - 0.004, 0, z - zo), 0.0062, "heat_glow", segs=10)
    oven = K.make_object("OvenGlow", og, m, root, (0, yb - 0.012, zo))
    glows = []
    for name, (bx, by) in zip(("BurnerL", "BurnerR"), burners):
        bg = Geo()
        ring(bg, 0.0, 0.0035, 0.035, 0.051, "heat_glow", segs=40)
        glows.append(K.make_object(name, bg, m, root, (bx, by, ct - 0.0005)))
    rig = {"root": root}
    W.keep(rig, fan, needle, oven, pip, *glows)

    def at(spin, heat, power, needle_deg):
        fan.rotation_euler.y = spin
        for ob in (oven, *glows):
            W.lit(ob, heat)
        W.lit(pip, power)
        needle.rotation_euler.y = math.radians(needle_deg)
    rig["rest"] = lambda: at(0, 0, 0, -150)
    turn = 2 * math.pi
    rig["clips"] = {
        "idle_loop": (4.0, True, lambda t: at(0, 0, 0, -150 + 1.5 * math.sin(2 * math.pi * t / 4.0))),
        "start": (0.8, False, lambda t: at(turn * (t / 0.8) ** 2, K.smooth(t / 0.8), K.smooth(t / 0.2), -150 + 240 * K.sink(t, 0.3))),
        "work_loop": (2.0, True, lambda t: at(turn * 3 * t / 2.0, 0.85 + 0.15 * math.cos(2 * math.pi * t / 2.0), 1,
                                             90 + 4 * math.sin(2 * math.pi * t / 2.0))),
        "complete": (0.8, False, lambda t: at(turn * 1.5 * (t - t * t / 1.6), 1 - K.smooth(t / 0.6), 1 - K.smooth(t / 0.3),
                                              90 - 240 * K.smooth(t / 0.6))),
    }
    rig["states"] = [("IDLE", rig["rest"]), ("COOKING", lambda: at(0.4, 1, 1, 90))]
    return rig


# =====================================================================================================
# Anvil (3 x 3 x 3): the camp's anvil_stump as a part (graphite anvil stapled onto a strapped hardwood stump)
# =====================================================================================================

def build_anvil(m):
    root = K.make_empty("Anvil")
    g = Geo()
    floor = -0.375
    sh = floor + 0.43                      # the stump's top
    # the stump: a turned log flaring at its foot, heartwood top, two rusty iron straps, four short root spurs
    K.lathe(g, [(0.0, floor), (0.33, floor), (0.335, floor + 0.03), (0.31, floor + 0.09), (0.3, sh - 0.02), (0.295, sh), (0.0, sh)],
            "plank_dark", steps=28)
    cylinder(g, (0, 0, sh - 0.001), (0, 0, sh + 0.004), 0.287, "plank", segs=28)
    for z in (floor + 0.12, sh - 0.07):
        ring(g, z, z + 0.035, 0.299, 0.309, "rust", segs=28)
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            K.screw(g, (math.cos(a) * 0.31, math.sin(a) * 0.31, z + 0.0175), (math.cos(a), math.sin(a), 0), 0.006, "zinc")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2 + 0.1
        d = Vector((math.cos(a), math.sin(a), 0))
        K.sweep(g, [Vector((0, 0, floor + 0.13)) + d * 0.27, Vector((0, 0, floor + 0.05)) + d * 0.34, Vector((0, 0, floor + 0.036)) + d * 0.4],
                0.035, "plank_dark", segs=10)
    # the anvil (the prop's, scaled to sit inside 0.75 m): foot, waist, body, steel face, heel, horn and hardy hole
    s = 0.95
    z = sh + 0.004
    box(g, (0.02, 0, z + 0.026 * s), (0.2 * s, 0.11 * s, 0.026 * s), "graphite", bevel=0.008)
    K.lathe(g, [(0.0, z + 0.05 * s), (0.12 * s, z + 0.05 * s), (0.085 * s, z + 0.16 * s), (0.0, z + 0.16 * s)], "graphite",
            a0=math.pi / 4, a1=math.pi / 4 + 2 * math.pi, steps=4)
    body_z = z + 0.21 * s
    box(g, (0.03, 0, body_z), (0.22 * s, 0.065 * s, 0.05 * s), "graphite", bevel=0.01)
    box(g, (0.03, 0, body_z + 0.052 * s), (0.215 * s, 0.058 * s, 0.004), "steel", bevel=0.002)
    box(g, (0.03 + 0.25 * s, 0, body_z - 0.005), (0.04 * s, 0.05 * s, 0.032 * s), "graphite", bevel=0.008)
    horn = Geo()
    cylinder(horn, (0, 0, 0), (-0.2 * s, 0, 0.01), 0.055 * s, "graphite", segs=20, r1=0.008)
    hm = Matrix.Translation((0.03 - 0.22 * s, 0, body_z + 0.012)) @ Matrix.Diagonal((1.0, 0.75, 1.0, 1.0))
    g.extend(horn, hm)
    box(g, (0.03 + 0.17 * s, 0, body_z + 0.053 * s + 0.001), (0.012, 0.012, 0.003), "hole")
    cylinder(g, (0.03 + 0.1 * s, 0, body_z + 0.053 * s), (0.03 + 0.1 * s, 0, body_z + 0.053 * s + 0.004), 0.007, "hole", segs=10)
    # zinc staples over the foot into the stump
    for x in (-0.12, 0.16):
        for sy in (-1, 1):
            box(g, (x, sy * 0.104 * s, z + 0.026 * s), (0.006, 0.004, 0.026 * s + 0.006), "zinc", bevel=0.002)
        box(g, (x, 0, z + 0.052 * s + 0.004), (0.006, 0.104 * s + 0.004, 0.004), "zinc", bevel=0.002)
    # a hammer resting on the stump in front of the anvil
    hz = sh + 0.02
    cylinder(g, (-0.2, -0.17, hz), (0.06, -0.24, hz), 0.012, "plank", segs=8)
    box(g, (0.08, -0.245, hz + 0.004), (0.018, 0.045, 0.02), "graphite", bevel=0.005, rot=Matrix.Rotation(math.radians(-15), 3, "Z"))
    K.make_object("Body", g, m, root)
    rig = {"root": root, "clips": {}}
    rig["rest"] = lambda: None
    rig["states"] = [("ANVIL", lambda: None)]
    return rig


# =====================================================================================================
# Bake, export, icon, review
# =====================================================================================================

def bake(rig):
    """As working_kit.bake, except a loop samples its pose at its last frame too (its poses are periodic), so a turning
    part ends a whole number of turns on (the same pose) and never spins back over its last frame."""
    obs = [v[0] for v in rig.get("_rest", {}).values()]
    for ob in obs:
        ob.animation_data_create()
        ob.rotation_mode = "XYZ"
    for name, (seconds, _loop, pose) in rig["clips"].items():
        acts = {}
        for ob in obs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        frames = max(2, int(round(seconds * K.FPS)))
        for fr in range(frames + 1):
            W.reset(rig)
            pose(fr / K.FPS)
            for ob in obs:
                ob.keyframe_insert("location", frame=fr)
                ob.keyframe_insert("rotation_euler", frame=fr)
                ob.keyframe_insert("scale", frame=fr)
        for ob in obs:
            lt.linear_keys(acts[ob.name])
            K.push_track(ob, acts[ob.name], name)
    for ob in obs:
        ob.animation_data.use_nla = False
    W.reset(rig)
    rig["rest"]()


def run(part, build, *, half, ortho, icon_target=(0, 0, 0), title="", lines=()):
    """Builds `part`, bakes its clips, writes ``meshes/parts/<part>.glb`` and its icon (in its last state), and with
    ``--render OUT_DIR`` the review views of every state and the sheet."""
    K.reset_scene()
    m = materials()
    rig = build(m)
    root = rig["root"]
    rig["rest"]()
    animated = bool(rig["clips"])
    if animated:
        bake(rig)
    info = K.export(root, K.MESHES / f"{part}.glb", animations=animated)
    K.report(K.MESHES / f"{part}.glb", info)
    K.strip_animation(root)
    W.reset(rig)
    rig["states"][-1][1]()
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", FRONT_ICON, ortho, icon_target)
    out = K.args_out()
    if out is not None:
        review(part, title or part, list(lines), rig, icon, info, out, half)
    return info


def review(name, title, lines, rig, icon, info, out, half):
    """working_kit.review's views, seen from the part's front (-y): three-quarter front right, front, right side."""
    import render_kit as rk
    rl = rk.rl
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    W.reset(rig)
    rig["rest"]()
    lo, _hi = rk._bounds(root)
    size = max(half) * 2
    ground.location.z = lo.z - 0.0005
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    cams = {
        "three_quarter": rl.camera("Q", target + Vector((1.0, -1.25, 0.75)).normalized() * size * 2.6, target, lens=50),
        "front": rl.camera("F", target + Vector((0, -20, 0)), target, ortho=size * 1.32),
        "side": rl.camera("S", target + Vector((20, 0, 0)), target, ortho=size * 1.32),
    }
    paths = {v: [] for v in rk.VIEWS}
    for label, pose in rig["states"]:
        W.reset(rig)
        pose()
        for view in rk.VIEWS:
            p = out / "frames" / f"{name}_{label.lower().replace(' ', '_')}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, rk.W, rk.H)
            paths[view].append((label, p))
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    rk.sheet(name, title, lines, paths, [(rig["states"][-1][0], icon)], [rk.fmt_stats(None, info)], out)
