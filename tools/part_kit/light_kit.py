"""The parts map's batch 5 lights (see part_kit.py): one family of lamps, each with ``on`` and ``off`` clips.

Every powered lamp shares one vocabulary, so the set reads as one family whatever its art family:

* a **lens** of frosted glass (pale and dull while dark) in a **bezel**, with a warm-white **glow** shell over it that
  the clips grow out of the lamp's middle (``on``) or shrink back into it (``off``), as the Beacon and the Sensor do;
* a **logic jack** where its links plug in (``part_kit.link_box``, the seats' and logic parts' socket, scaled down);
* a small **cyan pip**, lit while the lamp has power (``docs/art-direction.md``: cyan only on powered indicators);
* copper or steel **screws** at believable sizes, and the family's palette on the case.

The families (``docs/art-direction.md``): **Salvager Tech** for the craftable vehicle lamps (Headlight, Spotlight,
Warehouse Spotlight, Interior Light, Warning Light); **Colony Works** for the warehouse fittings (Frame Beam Light,
Fluorescent Light, Packing Lamp, Protector Sign, Construction Light: bone, steel blue, graphite, mustard, signal red);
**Settler Patchwork** for the shack lamps (Shack Light, Neon Sign: zinc, rust, scrap wood, enamel). Lamp light is
warm white (``#FFF1CC`` to ``#FFF4DD``), never orange; the Warning Light is signal red and the Neon Sign mint.

Axes are Blender's: x right, y the part's front (glTF -z), z up. Each ``build_<part>(mats)`` returns a rig:
``root``, ``glows`` (the nodes the clips grow and shrink) and ``lamps`` (where the light shines from, for the review
renders), plus ``hang`` for parts that hang from a ceiling (rendered from below).

``run_light`` writes ``meshes/parts/<part>.glb`` and ``textures/items/<part>_icon.png`` (the icon lit) and, with
``--render OUT_DIR``, the review views off and on and the part's sheet. Run the part scripts
(``tools/<part>/build_<part>.py``), not this file.
"""

from __future__ import annotations

import math

import bpy
from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, quad

lt = K.lt
pt = K.pt

WARM = "#fff1cc"          # Settler warm yellow, the shack lamps
WHITE = "#fff4dd"         # warm white, every other lamp
SAL = {"frame": "gun", "dark": "hull", "trim": "copper", "bolt": "copper", "pip": "copper"}
COL = {"frame": "steel_blue", "dark": "graphite", "trim": "mustard_paint", "bolt": "steel", "pip": "steel"}
SET = {"frame": "zinc", "dark": "rust", "trim": "brass", "bolt": "zinc", "pip": "brass"}


# =====================================================================================================
# Materials
# =====================================================================================================

def materials():
    m = K.materials()
    paint = lt.paint_surface
    tex = lt.textured
    metal = lt.metal_surface
    m.update({
        # the Salvager metals with a gentler normal map: a lamp is small, and the full-strength wear reads as crumpled foil
        "hull": tex("l_hull", K.half_res(metal(401, "#24282e", 0.5, 0.65, wear=0.7, edge_light=0.25)), normal_strength=1.3),
        "gun": tex("l_gun", K.half_res(metal(403, "#5a6068", 0.44, 0.78, wear=0.8, edge_light=0.3)), normal_strength=1.3),
        "gun_dark": tex("l_gun_dark", K.half_res(metal(405, "#3f444a", 0.5, 0.72, wear=0.7, edge_light=0.25)), normal_strength=1.3),
        "graphite": tex("l_graphite", K.half_res(metal(451, "#2b2f35", 0.5, 0.6, wear=0.6, edge_light=0.3)), normal_strength=1.3),
        "zinc": tex("l_zinc", K.half_res(metal(449, "#9aa3ab", 0.42, 0.8, wear=0.9, edge_light=0.3)), normal_strength=1.3),
        # Colony Works paints over graphite steel
        "bone": tex("l_bone", K.half_res(paint(501, "#d9d1bc", "#4a4e54")), normal_strength=1.3),
        "steel_blue": tex("l_steel_blue", K.half_res(paint(503, "#3e6a8c", "#4a4e54")), normal_strength=1.3),
        "mustard_paint": tex("l_mustard", K.half_res(paint(505, "#e0b530", "#4a4e54")), normal_strength=1.3),
        "signal_red": tex("l_signal_red", K.half_res(paint(507, "#c8423a", "#4a4e54")), normal_strength=1.3),
        # lenses and glass, dark
        "frost": pt.plain("l_frost", "#d8d6cc", 0.35, 0.0),
        "reflector": pt.plain("l_reflector", "#c9ccd0", 0.12, 1.0),
        "bulb": pt.plain("l_bulb", "#c8ccc8", 0.06, 0.0),
        "filament": pt.plain("l_filament", "#3a3430", 0.5, 0.6),
        "red_lens": pt.plain("l_red_lens", "#6a1712", 0.12, 0.0),
        "neon_off": pt.plain("l_neon_off", "#9fb2aa", 0.12, 0.0),
        "green_stick": pt.plain("l_green_stick", "#3f9a4a", 0.2, 0.0, emission="#40ff60", strength=1.5),
        "cable": pt.plain("l_cable", "#16181a", 0.7, 0.0),
        # lit (the clips hide them inside the lamp while it is dark)
        "warm_glow": pt.plain("l_warm_glow", WHITE, 0.3, 0.0, emission=WHITE, strength=4.0),
        "shack_glow": pt.plain("l_shack_glow", WARM, 0.3, 0.0, emission=WARM, strength=4.0),
        "red_lamp_glow": pt.plain("l_red_glow", "#ff5a48", 0.3, 0.0, emission="#ff3d2e", strength=5.0),
        "neon_glow": pt.plain("l_neon_glow", "#9dffd2", 0.3, 0.0, emission="#7dffc4", strength=5.0),
        "sign_glow": pt.plain("l_sign_glow", "#fff6e4", 0.3, 0.0, emission="#fff4dd", strength=2.5),
    })
    return m


# =====================================================================================================
# Shapes
# =====================================================================================================

def axis_matrix(origin, axis):
    """A 4x4 matrix taking local z to `axis` with its origin at `origin`."""
    a = Vector(axis).normalized()
    u, w = K.bl.frame_of(a)
    mat = Matrix((u, w, a)).transposed().to_4x4()
    mat.translation = Vector(origin)
    return mat


def lathe_on(g, profile, mat, origin, axis, steps=40):
    """A closed (r, h) profile turned about `axis` through `origin` (h along the axis)."""
    t = Geo()
    K.lathe(t, profile, mat, steps=steps)
    g.extend(t, axis_matrix(origin, axis))


def lens(g, origin, axis, r, bulge, mat="frost", steps=40):
    """A domed lens of radius `r` sitting on `origin`, bulging `bulge` along `axis`."""
    prof = [(0.0, -0.002), (r, -0.002)]
    for k in range(0, 7):
        a = math.radians(90 * k / 6)
        prof.append((r * math.cos(a) * (1 - 0.05 * k / 6), bulge * math.sin(a)))
    prof.append((0.0, bulge))
    lathe_on(g, prof, mat, origin, axis, steps)


def lens_glow(r, bulge, axis, mat="warm_glow", steps=40):
    """A lit shell just over a `lens` of the same size, about its own origin (the lens centre)."""
    g = Geo()
    lens(g, (0, 0, 0), axis, r + 0.0012, bulge + 0.0012, mat, steps)
    return g


def bezel(g, origin, axis, r_in, r_out, depth, mat, lip=0.004, steps=40):
    """A ring round a lens: from `origin` back along -axis by `depth`, with a rounded lip at the front."""
    prof = [(r_in, -depth), (r_out, -depth), (r_out, -lip), (r_out - lip, 0.0), (r_in + lip, 0.0), (r_in, -lip)]
    lathe_on(g, prof, mat, origin, axis, steps)


def jack(g, at, facing, finish, scale=0.6):
    """The logic jack (``part_kit.link_box``, scaled): where the lamp's links plug in."""
    f = {"dark": finish["dark"], "bolt": finish["bolt"]}
    K.link_box(g, at, facing, f, scale)


def pip_socket(g, at, n, r=0.006):
    """The dark socket of the cyan power pip (the lit pip is a glow node)."""
    at, n = Vector(at), Vector(n).normalized()
    cylinder(g, at - n * 0.002, at + n * 0.0015, r + 0.0025, "hole", segs=14)


def pip_glow(m, root, name, at, n, r=0.006):
    n = Vector(n).normalized()
    return K.make_object(name, K.glow_disc(r, n, "cyan_glow", depth=0.003, segs=14), m, root, tuple(Vector(at) + n * 0.0012))


def screws(g, points, n, r=0.0065, mat="copper"):
    for p in points:
        K.screw(g, p, n, r, mat)


def plate(g, center, half, mat, screw_mat, n=(0, 0, 1), inset=0.016, r=0.0065, bevel=0.005):
    """A mounting plate (box) with a screw in each corner of the face looking along the plate's thin axis `n`."""
    box(g, center, half, mat, bevel=bevel)
    n = Vector(n)
    c = Vector(center)
    axes = [i for i in range(3) if abs(n[i]) < 0.5]
    face = c + Vector([n[i] * half[i] for i in range(3)])
    pts = []
    for s0 in (-1, 1):
        for s1 in (-1, 1):
            p = face.copy()
            p[axes[0]] += s0 * (half[axes[0]] - inset)
            p[axes[1]] += s1 * (half[axes[1]] - inset)
            pts.append(tuple(p))
    screws(g, pts, n, r, screw_mat)


def polygon(g, pts2d, y, mat, depth=0.0):
    """A flat polygon in the x-z plane at `y`, facing +y (fan from its first point); with `depth` an extruded slab."""
    front = [(x, y, z) for x, z in pts2d]
    if depth <= 0:
        g.add(front, [tuple(range(len(front)))], mat)
        return
    back = [(x, y - depth, z) for x, z in pts2d]
    n = len(pts2d)
    faces = [tuple(range(n)), tuple(n + k for k in reversed(range(n)))]
    for k in range(n):
        k1 = (k + 1) % n
        faces.append((k, n + k, n + k1, k1))
    g.add(front + back, faces, mat)


def fan_poly(g, pts2d, y, mat, center):
    """A flat polygon facing +y drawn as a fan round `center` (for concave outlines that are star-shaped)."""
    cx, cz = center
    verts = [(cx, y, cz)] + [(x, y, z) for x, z in pts2d]
    n = len(pts2d)
    g.add(verts, [(0, 1 + k, 1 + (k + 1) % n) for k in range(n)], mat)


# =====================================================================================================
# Salvager Tech lamps
# =====================================================================================================

def build_headlight(m):
    """1 x 1 x 1. Its base is its back (Blender -y); it shines out of its front."""
    root = K.make_empty("Headlight")
    g = Geo()
    f = SAL
    plate(g, (0, -0.113, 0), (0.112, 0.012, 0.112), "gun_dark", "copper", n=(0, 1, 0), inset=0.017, r=0.007)
    secs = [(-0.1015, 0, 0.08, -0.08, 0.078), (-0.06, 0, 0.088, -0.088, 0.086), (0.02, 0, 0.092, -0.09, 0.09),
            (0.068, 0, 0.09, -0.088, 0.088)]
    K.squircle_loft(g, K.smooth_sections(secs, 3), "hull", ring_pts=28, power=4.5)
    # a copper band round the can, vent slots on its flanks, a hood over the lens
    K.squircle_loft(g, [(-0.072, 0, 0.0895, -0.0885, 0.0875), (-0.062, 0, 0.0895, -0.0885, 0.0875)], "copper", ring_pts=28, power=4.5)
    for sx in (-1, 1):
        for k in range(3):
            box(g, (sx * 0.0915, -0.03 + k * 0.022, -0.035), (0.0015, 0.006, 0.03), "hole")
    box(g, (0, 0.074, 0.096), (0.093, 0.04, 0.006), "gun", bevel=0.003, rot=Matrix.Rotation(math.radians(-6), 3, "X"))
    for sx in (-1, 1):
        box(g, (sx * 0.088, 0.078, 0.06), (0.005, 0.035, 0.036), "gun", bevel=0.002)
    # bezel and lens
    c = Vector((0, 0.1, 0))
    bezel(g, c, (0, 1, 0), 0.068, 0.084, 0.034, "gun")
    lens(g, c + Vector((0, -0.004, 0)), (0, 1, 0), 0.069, 0.012)
    bezel(g, c + Vector((0, -0.003, 0)), (0, 1, 0), 0.03, 0.033, 0.004, "reflector", lip=0.001, steps=28)
    screws(g, [(0.076 * math.cos(a), 0.1, 0.076 * math.sin(a)) for a in (math.radians(45 + 90 * k) for k in range(4))], (0, 1, 0), 0.0045, "copper")
    jack(g, (-0.0925 - 0.012, -0.04, -0.045), (-1, 0, 0), f, 0.55)
    pip_socket(g, (0.05, -0.05, 0.0905), (0, 0, 1))
    K.make_object("Body", g, m, root)
    glows = [K.make_object("Glow", lens_glow(0.069, 0.012, (0, 1, 0)), m, root, tuple(c + Vector((0, -0.004, 0)))),
             pip_glow(m, root, "Pip", (0.05, -0.05, 0.0905), (0, 0, 1))]
    return {"root": root, "glows": glows, "lamps": [spot((0, 0.12, 0), (0, 1, 0), 25, 40)]}


def can_lamp(g, c, r, length, f, fins=4, grille=2, steps=40):
    """A round lamp can along +y ending in its bezel and lens at `c` (the lens centre): hull can, copper fins at its
    back, a gunmetal bezel with screws and a grille across the lens."""
    c = Vector(c)
    back = c - Vector((0, length, 0))
    lathe_on(g, [(0.0, 0.0), (r * 0.92, 0.0), (r, r * 0.08), (r, length - 0.01), (0.0, length - 0.01)], "hull", back, (0, 1, 0), steps)
    for k in range(fins):
        y = 0.012 + k * r * 0.12
        lathe_on(g, [(r - 0.002, y), (r + r * 0.09, y), (r + r * 0.09, y + r * 0.04), (r - 0.002, y + r * 0.04)], f["trim"], back, (0, 1, 0), steps)
    bezel(g, c, (0, 1, 0), r * 0.9, r * 1.1, r * 0.22, "gun", steps=steps)
    lens(g, c + Vector((0, -0.006, 0)), (0, 1, 0), r * 0.9, r * 0.1, steps=steps)
    bezel(g, c + Vector((0, -0.005, 0)), (0, 1, 0), r * 0.3, r * 0.34, 0.004, "reflector", lip=0.001, steps=28)
    for k in range(grille):
        z = (k - (grille - 1) / 2) * r * 0.6
        hw = math.sqrt(max(r * r * 0.81 - z * z, 0)) + 0.004
        box(g, (c.x, c.y + r * 0.12, c.z + z), (hw, 0.004, 0.0045), "gun_dark", bevel=0.0015)
    box(g, (c.x, c.y + r * 0.12, c.z), (0.0045, 0.004, r * 0.92), "gun_dark", bevel=0.0015)
    screws(g, [(c.x + r * 1.0 * math.cos(a), c.y + 0.0005, c.z + r * 1.0 * math.sin(a))
               for a in (math.radians(45 + 90 * k) for k in range(4))], (0, 1, 0), r * 0.045, f["bolt"])


def yoke(g, base_z, pivot_z, half_gap, arm_t, depth, f):
    """A U yoke on a turntable: arms at x = +-half_gap from the turntable up to the pivot, copper pivot knobs."""
    lathe_on(g, [(0.0, 0.0), (half_gap * 0.62, 0.0), (half_gap * 0.62, 0.02), (half_gap * 0.5, 0.032), (0.0, 0.032)], "gun", (0, 0, base_z), (0, 0, 1), 36)
    bar_z = base_z + 0.032 + arm_t
    box(g, (0, 0, bar_z), (half_gap + arm_t, depth, arm_t), "gun", bevel=arm_t * 0.4)
    for sx in (-1, 1):
        h = (pivot_z - bar_z) / 2
        box(g, (sx * half_gap, 0, bar_z + h), (arm_t, depth, h + depth * 0.6), "gun", bevel=arm_t * 0.4)
        p = Vector((sx * (half_gap + arm_t), 0, pivot_z))
        cylinder(g, p, p + Vector((sx * arm_t * 1.4, 0, 0)), depth * 0.85, f["trim"], segs=20)
        K.hexnut(g, p + Vector((sx * arm_t * 1.4, 0, 0)), (sx, 0, 0), depth * 0.38, arm_t * 0.5, "steel")


def build_spotlight(m):
    """2 x 2 x 2. Stands on its base (Blender -z); a swivel can shines out of its front."""
    root = K.make_empty("Spotlight")
    g = Geo()
    f = SAL
    plate(g, (0, 0, -0.238), (0.2, 0.2, 0.012), "hull", "copper", inset=0.022, r=0.008)
    yoke(g, -0.226, 0.07, 0.15, 0.012, 0.04, f)
    c = (0, 0.2, 0.07)
    can_lamp(g, c, 0.125, 0.31, f, fins=4, grille=2)
    jack(g, (-0.12, 0.14, -0.226), (0, 1, 0), f, 0.6)
    pip_socket(g, (0.0, -0.03, 0.07 + 0.125 + 0.001), (0, 0, 1))
    K.make_object("Body", g, m, root)
    lc = Vector(c) + Vector((0, -0.006, 0))
    glows = [K.make_object("Glow", lens_glow(0.125 * 0.9, 0.0125, (0, 1, 0)), m, root, tuple(lc)),
             pip_glow(m, root, "Pip", (0.0, -0.03, 0.07 + 0.125 + 0.001), (0, 0, 1))]
    return {"root": root, "glows": glows, "lamps": [spot((0, 0.24, 0.07), (0, 1, 0), 15, 60)]}


def build_warehouse_spotlight(m):
    """3 x 3 x 3. The Spotlight's big brother: a floodlight drum on a heavy yoke."""
    root = K.make_empty("WarehouseSpotlight")
    g = Geo()
    f = SAL
    plate(g, (0, 0, -0.358), (0.3, 0.3, 0.017), "hull", "copper", inset=0.03, r=0.011)
    # gussets from the plate to the turntable
    for a in range(4):
        rot = Matrix.Rotation(math.radians(45 + 90 * a), 3, "Z")
        box(g, tuple(rot @ Vector((0.16, 0, -0.33))), (0.06, 0.008, 0.012), "gun_dark", bevel=0.004, rot=rot)
    yoke(g, -0.341, 0.1, 0.275, 0.016, 0.06, f)
    c = (0, 0.33, 0.1)
    can_lamp(g, c, 0.24, 0.46, f, fins=5, grille=3, steps=48)
    # a carry handle across the drum's top
    top = 0.1 + 0.24
    for sx in (-1, 1):
        box(g, (sx * 0.1, 0.02, top + 0.01), (0.012, 0.03, 0.014), "gun_dark", bevel=0.004)
    cylinder(g, (-0.1, 0.02, top + 0.024), (0.1, 0.02, top + 0.024), 0.008, "rubber", segs=12)
    jack(g, (-0.2, 0.22, -0.341), (0, 1, 0), f, 0.75)
    pip_socket(g, (0.12, -0.05, top - 0.002), (0, 0, 1), r=0.007)
    K.make_object("Body", g, m, root)
    lc = Vector(c) + Vector((0, -0.006, 0))
    glows = [K.make_object("Glow", lens_glow(0.24 * 0.9, 0.024, (0, 1, 0), steps=48), m, root, tuple(lc)),
             pip_glow(m, root, "Pip", (0.12, -0.05, top - 0.002), (0, 0, 1), r=0.007)]
    return {"root": root, "glows": glows, "lamps": [spot((0, 0.38, 0.1), (0, 1, 0), 20, 120)]}


def build_interior_light(m):
    """2 x 1 x 2. Hangs from a ceiling (its base is its top); a square panel of four frosted cells."""
    root = K.make_empty("InteriorLight")
    g = Geo()
    f = SAL
    box(g, (0, 0, 0.113), (0.232, 0.232, 0.012), "gun_dark", bevel=0.006)
    box(g, (0, 0, 0.072), (0.214, 0.214, 0.03), "hull", bevel=0.012)
    box(g, (0, 0, 0.098), (0.218, 0.218, 0.0035), "copper", bevel=0.002)
    # the frame round the cells and the cross between them
    box(g, (0, 0, 0.036), (0.21, 0.21, 0.008), "gun", bevel=0.004)
    for half in ((0.21, 0.009, 0.012), (0.009, 0.21, 0.012)):
        box(g, (0, 0, 0.03), half, "gun", bevel=0.003)
    cells = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = (sx * 0.1, sy * 0.1, 0.03)
            box(g, p, (0.087, 0.087, 0.006), "frost", bevel=0.003)
            cells.append(p)
    screws(g, [(sx * 0.192, sy * 0.192, 0.0277) for sx in (-1, 1) for sy in (-1, 1)], (0, 0, -1), 0.007, "copper")
    jack(g, (0.13, 0.214 + 0.014, 0.046), (0, 1, 0), f, 0.5)
    pip_socket(g, (-0.13, 0.2145, 0.072), (0, 1, 0))
    K.make_object("Body", g, m, root)
    gl = Geo()
    for (x, y, _) in cells:
        box(gl, (x, y, -0.0072), (0.0885, 0.0885, 0.0014), "warm_glow", bevel=0.001)
    glows = [K.make_object("Glow", gl, m, root, (0, 0, 0.03)),
             pip_glow(m, root, "Pip", (-0.13, 0.2145, 0.072), (0, 1, 0))]
    return {"root": root, "glows": glows, "lamps": [point((0, 0, -0.04), 30)], "hang": True}


def build_warning_light(m):
    """1 x 2 x 1. Stands on its base: a red dome in a cage on a round drum; it blinks."""
    root = K.make_empty("WarningLight")
    g = Geo()
    f = SAL
    plate(g, (0, 0, -0.242), (0.11, 0.11, 0.008), "gun_dark", "copper", inset=0.016, r=0.0065)
    lathe_on(g, [(0.0, 0.0), (0.085, 0.0), (0.085, 0.012), (0.07, 0.02), (0.0, 0.02)], "hull", (0, 0, -0.234), (0, 0, 1), 40)
    lathe_on(g, [(0.0, 0.0), (0.066, 0.0), (0.066, 0.13), (0.07, 0.136), (0.07, 0.15), (0.0, 0.15)], "hull", (0, 0, -0.214), (0, 0, 1), 40)
    for z in (-0.19, -0.11):
        lathe_on(g, [(0.065, z), (0.069, z), (0.069, z + 0.014), (0.065, z + 0.014)], "copper", (0, 0, 0), (0, 0, 1), 40)
    # dome lens on a collar, a cage of four bars with a top cap
    base = -0.064
    lathe_on(g, [(0.0, 0.0), (0.072, 0.0), (0.072, 0.014), (0.0, 0.014)], "gun", (0, 0, base), (0, 0, 1), 40)
    dome_c = Vector((0, 0, base + 0.014))
    prof = [(0.0, -0.001), (0.06, -0.001), (0.06, 0.085)]
    for k in range(1, 7):
        a = math.radians(90 * k / 6)
        prof.append((0.06 * math.cos(a), 0.085 + 0.06 * math.sin(a)))
    lathe_on(g, prof, "red_lens", dome_c, (0, 0, 1), 40)
    lathe_on(g, [(0.0, 0.03), (0.02, 0.03), (0.026, 0.07), (0.0, 0.07)], "reflector", dome_c, (0, 0, 1), 20)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, y = 0.071 * math.cos(a), 0.071 * math.sin(a)
        K.sweep(g, [(x, y, base + 0.012), (x, y, base + 0.1), (x * 0.75, y * 0.75, base + 0.15), (0, 0, base + 0.168)], 0.0042, "gun", segs=8)
    lathe_on(g, [(0.0, 0.0), (0.016, 0.0), (0.016, 0.01), (0.0, 0.01)], "gun", (0, 0, base + 0.163), (0, 0, 1), 16)
    K.bl.ring(g, base + 0.07, base + 0.077, 0.066, 0.075, "gun", segs=36)
    jack(g, (0.0, -0.066 - 0.012, -0.2), (0, -1, 0), f, 0.5)
    pip_socket(g, (0.0, 0.066, -0.17), (0, 1, 0), r=0.005)
    K.make_object("Body", g, m, root)
    gl = Geo()
    prof = [(0.0, 0.0), (0.0615, 0.0), (0.0615, 0.085)]
    for k in range(1, 7):
        a = math.radians(90 * k / 6)
        prof.append((0.0615 * math.cos(a), 0.085 + 0.0615 * math.sin(a)))
    lathe_on(gl, [(r, h - 0.05) for r, h in prof], "red_lamp_glow", (0, 0, 0), (0, 0, 1), 40)
    glows = [K.make_object("Glow", gl, m, root, tuple(dome_c + Vector((0, 0, 0.05)))),
             pip_glow(m, root, "Pip", (0.0, 0.066, -0.17), (0, 1, 0), r=0.005)]
    return {"root": root, "glows": glows, "lamps": [point((0, 0, 0.08), 25, "#ff3d2e")]}


# =====================================================================================================
# Colony Works fittings
# =====================================================================================================

def build_construction_light(m):
    """2 x 4 x 2. A mustard work lamp on a tripod mast, tilted down to light the ground in front of it."""
    root = K.make_empty("ConstructionLight")
    g = Geo()
    f = COL
    hub = -0.2
    for k in range(3):
        a = math.radians(90 + 120 * k)
        foot = Vector((0.215 * math.cos(a), 0.215 * math.sin(a), -0.49))
        K.sweep(g, [(0.03 * math.cos(a), 0.03 * math.sin(a), hub), tuple(foot + Vector((0, 0, 0.012)))], 0.011, "steel_blue", segs=10)
        cylinder(g, foot + Vector((0, 0, -0.01)), foot + Vector((0, 0, 0.018)), 0.02, "rubber", segs=14)
    lathe_on(g, [(0.0, -0.02), (0.034, -0.02), (0.034, 0.03), (0.0, 0.03)], "graphite", (0, 0, hub), (0, 0, 1), 24)
    cylinder(g, (0, 0, hub - 0.06), (0, 0, 0.27), 0.018, "steel", segs=16)
    cylinder(g, (0, 0, 0.0), (0, 0, 0.27), 0.0215, "steel_blue", segs=16)
    lathe_on(g, [(0.0, 0.0), (0.03, 0.0), (0.03, 0.03), (0.0, 0.03)], "graphite", (0, 0, -0.02), (0, 0, 1), 20)
    cylinder(g, (0.028, 0, -0.005), (0.06, 0, -0.005), 0.007, "graphite", segs=10)
    cylinder(g, (0.06, 0, -0.005), (0.07, 0, -0.005), 0.016, "mustard_paint", segs=12)
    # the head: yoke on the mast, mustard housing pitched down 21.8 degrees
    pivot = Vector((0, 0.0, 0.32))
    box(g, (0, 0, 0.285), (0.16, 0.02, 0.012), "graphite", bevel=0.004)
    for sx in (-1, 1):
        box(g, (sx * 0.152, 0, 0.31), (0.008, 0.02, 0.035), "graphite", bevel=0.003)
        cylinder(g, pivot + Vector((sx * 0.16, 0, 0)), pivot + Vector((sx * 0.178, 0, 0)), 0.018, "mustard_paint", segs=14)
    tilt = Matrix.Rotation(math.radians(-21.8), 4, "X")
    head = Matrix.Translation(pivot) @ tilt
    h = Geo()
    box(h, (0, 0.0, 0.0), (0.142, 0.055, 0.095), "mustard_paint", bevel=0.012)
    box(h, (0, -0.06, 0.0), (0.12, 0.012, 0.075), "graphite", bevel=0.006)
    for k in range(5):
        box(h, (-0.08 + k * 0.04, -0.075, 0), (0.006, 0.01, 0.065), "graphite", bevel=0.002)
    box(h, (0, 0.056, 0), (0.132, 0.006, 0.085), "graphite", bevel=0.003)
    box(h, (0, 0.06, 0), (0.118, 0.004, 0.072), "frost", bevel=0.002)
    for k in range(4):
        z = -0.054 + k * 0.036
        box(h, (0, 0.067, z), (0.124, 0.003, 0.003), "steel", bevel=0.001)
    for k in range(3):
        x = -0.06 + k * 0.06
        box(h, (x, 0.069, 0), (0.003, 0.003, 0.08), "steel", bevel=0.001)
    screws(h, [(sx * 0.12, 0.062, sz * 0.077) for sx in (-1, 1) for sz in (-1, 1)], (0, 1, 0), 0.006, "steel")
    K.sweep(h, [(-0.07, -0.02, 0.095), (-0.06, -0.02, 0.13), (0.06, -0.02, 0.13), (0.07, -0.02, 0.095)], 0.008, "rubber", segs=10)
    pip_at = Vector((0.1, -0.055, 0.06))
    pip_socket(h, pip_at, (0, -1, 0), r=0.005)
    g.extend(h, head)
    # the cable down the mast to the jack on the hub
    K.sweep(g, [tuple(head @ Vector((0.05, -0.07, -0.06))), (0.04, -0.03, 0.2), (0.03, -0.028, 0.0), (0.04, -0.03, hub + 0.03)], 0.006, "cable", segs=8)
    jack(g, (0.0, -0.034 - 0.012, hub - 0.03), (0, -1, 0), f, 0.5)
    K.make_object("Body", g, m, root)
    gl = Geo()
    box(gl, (0, 0.0, 0), (0.119, 0.0012, 0.073), "warm_glow", bevel=0.001)
    lc = head @ Vector((0, 0.0615, 0))
    glow = K.make_object("Glow", gl, m, root, tuple(lc))
    glow.rotation_euler = (math.radians(-21.8), 0, 0)
    pip = pip_glow(m, root, "Pip", (0, 0, 0), (0, -1, 0), r=0.005)
    pip.location = head @ (pip_at + Vector((0, -0.0012, 0)))
    pip.rotation_euler = (math.radians(-21.8), 0, 0)
    d = (tilt.to_3x3() @ Vector((0, 1, 0))).normalized()
    return {"root": root, "glows": [glow, pip], "lamps": [spot(tuple(lc + d * 0.03), tuple(d), 45, 60)],
            "light_at": lc, "light_dir": d}


def build_frame_beam_light(m):
    """4 x 1 x 1. A steel-blue channel beam with a light strip in its underside; hangs from a ceiling or beam."""
    root = K.make_empty("FrameBeamLight")
    g = Geo()
    f = COL
    L = 0.47
    box(g, (0, 0, 0.113), (L, 0.112, 0.012), "steel_blue", bevel=0.004)
    for sy in (-1, 1):
        box(g, (0, sy * 0.104, 0.05), (L, 0.008, 0.068), "steel_blue", bevel=0.003)
        box(g, (0, sy * 0.09, -0.012), (L, 0.022, 0.007), "steel_blue", bevel=0.003)
        # bolts down the webs and a mustard tag on the front one
        for k in range(5):
            x = -0.4 + k * 0.2
            K.screw(g, (x, sy * 0.112, 0.085), (0, sy, 0), 0.0065, "steel")
    box(g, (0.28, 0.1125, 0.03), (0.06, 0.0015, 0.022), "mustard_paint", bevel=0.001)
    for k in range(3):
        box(g, (0.25 + k * 0.025, 0.1142, 0.03), (0.006, 0.0008, 0.012), "graphite")
    for sx in (-1, 1):
        box(g, (sx * 0.483, 0, 0.05), (0.014, 0.118, 0.07), "graphite", bevel=0.005)
        K.screw(g, (sx * 0.497, 0.06, 0.08), (sx, 0, 0), 0.0065, "steel")
        K.screw(g, (sx * 0.497, -0.06, 0.08), (sx, 0, 0), 0.0065, "steel")
    # the strip: a bone housing inside the channel, four frosted lenses between graphite ribs
    box(g, (0, 0, 0.06), (L - 0.01, 0.084, 0.04), "bone", bevel=0.006)
    cells = []
    for k in range(4):
        x = -0.3525 + k * 0.235
        box(g, (x, 0, 0.012), (0.105, 0.06, 0.008), "frost", bevel=0.003)
        cells.append(x)
    for k in range(5):
        x = -0.47 + k * 0.235
        box(g, (x, 0, 0.012), (0.012, 0.074, 0.012), "graphite", bevel=0.003)
    jack(g, (-0.395, 0.1145, 0.0), (0, 1, 0), f, 0.4)
    pip_socket(g, (-0.33, 0.1125, 0.03), (0, 1, 0), r=0.005)
    K.make_object("Body", g, m, root)
    gl = Geo()
    for x in cells:
        box(gl, (x, 0, -0.0402), (0.106, 0.061, 0.0012), "warm_glow", bevel=0.0008)
    glows = [K.make_object("Glow", gl, m, root, (0, 0, 0.044)),
             pip_glow(m, root, "Pip", (-0.33, 0.1125, 0.03), (0, 1, 0), r=0.005)]
    return {"root": root, "glows": glows, "lamps": [spot((0, 0, -0.02), (0, 0, -1), 60, 50)], "hang": True}


def build_fluorescent_light(m):
    """4 x 1 x 2. A bone batten with two tubes under a reflector; hangs from a ceiling."""
    root = K.make_empty("FluorescentLight")
    g = Geo()
    f = COL
    box(g, (0, 0, 0.104), (0.47, 0.215, 0.018), "bone", bevel=0.008)
    box(g, (0, 0, 0.068), (0.45, 0.17, 0.022), "bone", bevel=0.008)
    for sy in (-1, 1):
        box(g, (0, sy * 0.205, 0.08), (0.46, 0.012, 0.03), "steel_blue", bevel=0.004)
        for k in range(4):
            K.screw(g, (-0.36 + k * 0.24, sy * 0.217, 0.08), (0, sy, 0), 0.0055, "steel")
    box(g, (0, 0, 0.044), (0.44, 0.15, 0.003), "reflector", bevel=0.001)
    # end blocks holding the tubes
    for sx in (-1, 1):
        box(g, (sx * 0.452, 0, 0.022), (0.02, 0.15, 0.03), "graphite", bevel=0.006)
        for sy in (-1, 1):
            cylinder(g, (sx * 0.432, sy * 0.075, 0.0), (sx * 0.418, sy * 0.075, 0.0), 0.03, "graphite", segs=16)
            for dz in (-0.008, 0.008):
                cylinder(g, (sx * 0.418, sy * 0.075, dz), (sx * 0.41, sy * 0.075, dz), 0.0025, "steel", segs=6)
    tubes = []
    for sy in (-1, 1):
        cylinder(g, (-0.418, sy * 0.075, 0.0), (0.418, sy * 0.075, 0.0), 0.024, "frost", segs=20)
        for sx in (-1, 1):
            cylinder(g, (sx * 0.418, sy * 0.075, 0.0), (sx * 0.392, sy * 0.075, 0.0), 0.0245, "steel", segs=20)
        tubes.append(sy * 0.075)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * 0.3, sy * 0.13, 0.124), (0.03, 0.02, 0.003), "graphite", bevel=0.001)
    jack(g, (-0.3, 0.217 + 0.009, 0.042), (0, 1, 0), f, 0.45)
    pip_socket(g, (-0.22, 0.2175, 0.08), (0, 1, 0), r=0.005)
    K.make_object("Body", g, m, root)
    gl = Geo()
    for y in tubes:
        cylinder(gl, (-0.391, y, 0.0), (0.391, y, 0.0), 0.0253, "warm_glow", segs=20)
    glows = [K.make_object("Glow", gl, m, root, (0, 0, 0)),
             pip_glow(m, root, "Pip", (-0.22, 0.2175, 0.08), (0, 1, 0), r=0.005)]
    return {"root": root, "glows": glows, "lamps": [point((0, 0, -0.06), 45)], "hang": True}


def build_packing_lamp(m):
    """4 x 1 x 1. A caged tube lamp on two clip feet, rubber bumpers at its ends; it shines in its paint colour."""
    root = K.make_empty("PackingLamp")
    g = Geo()
    f = COL
    z = 0.0
    cylinder(g, (-0.4, 0, z), (0.4, 0, z), 0.04, "frost", segs=24)
    for sx in (-1, 1):
        lathe_on(g, [(0.0, 0.0), (0.052, 0.0), (0.058, 0.01), (0.058, 0.052), (0.05, 0.062), (0.0, 0.062)], "graphite",
                 (sx * 0.4, 0, z), (sx, 0, 0), 28)
        lathe_on(g, [(0.0, 0.062), (0.05, 0.062), (0.056, 0.07), (0.054, 0.082), (0.0, 0.086)], "rubber", (sx * 0.4, 0, z), (sx, 0, 0), 28)
    # the cage: four rods and three hoops
    for k in range(4):
        a = math.radians(45 + 90 * k)
        y, zz = 0.05 * math.cos(a), z + 0.05 * math.sin(a)
        cylinder(g, (-0.4, y, zz), (0.4, y, zz), 0.0045, "steel_blue", segs=8)
    for x in (-0.2, 0.0, 0.2):
        lathe_on(g, [(0.047, -0.005), (0.055, -0.005), (0.055, 0.005), (0.047, 0.005)], "steel_blue", (x, 0, z), (1, 0, 0), 28)
    # clip feet onto the base with a collar round the tube, a hanging ring at one end
    collar = Geo()
    K.bl.ring(collar, -0.012, 0.012, 0.0405, 0.05, "graphite", segs=24)
    for sx in (-1, 1):
        x = sx * 0.28
        box(g, (x, 0, -0.112), (0.04, 0.05, 0.013), "graphite", bevel=0.005)
        box(g, (x, 0, -0.072), (0.012, 0.03, 0.03), "graphite", bevel=0.004)
        g.extend(collar, axis_matrix((x, 0, z), (1, 0, 0)))
    loop = [(-0.43 + 0.018 * math.sin(math.radians(t)), 0.0, 0.074 + 0.018 * (1 - math.cos(math.radians(t)))) for t in range(0, 361, 30)]
    K.sweep(g, loop, 0.0035, "steel", segs=6)
    jack(g, (-0.28, 0.062, -0.125), (0, 1, 0), f, 0.45)
    pip_socket(g, (-0.43, -0.059, 0.0), (0, -1, 0), r=0.005)
    K.make_object("Body", g, m, root)
    gl = Geo()
    cylinder(gl, (-0.399, 0, 0), (0.399, 0, 0), 0.0412, "warm_glow", segs=24)
    glows = [K.make_object("Glow", gl, m, root, (0, 0, z)),
             pip_glow(m, root, "Pip", (-0.43, -0.059, 0.0), (0, -1, 0), r=0.005)]
    return {"root": root, "glows": glows, "lamps": [point((0, 0, 0.0), 30)]}


def shield_outline(w, h, n=8):
    """A heater shield's outline in the x-z plane, `w` wide and `h` tall, centred, counter-clockwise from top right."""
    top = h * 0.5
    # the left flank curves in to the point, the right flank back up
    pts = [(w / 2, top), (-w / 2, top)]
    for k in range(1, n + 1):
        t = k / n
        pts.append((-w / 2 * math.cos(math.radians(90 * t)) ** 0.8, top - h * 0.42 - h * 0.58 * math.sin(math.radians(90 * t))))
    for k in range(n - 1, 0, -1):
        t = k / n
        pts.append((w / 2 * math.cos(math.radians(90 * t)) ** 0.8, top - h * 0.42 - h * 0.58 * math.sin(math.radians(90 * t))))
    return pts


def build_protector_sign(m):
    """3 x 2 x 1. A Colony lightbox for a wall (its base is its back): a steel-blue shield on a lit bone face, a signal
    red band along its foot, graphite frame and wall brackets."""
    root = K.make_empty("ProtectorSign")
    g = Geo()
    f = COL
    hw, hh = 0.36, 0.236
    box(g, (0, -0.07, 0), (hw, 0.052, hh), "graphite", bevel=0.01)
    for sx in (-1, 1):
        box(g, (sx * 0.24, -0.118, 0), (0.03, 0.007, 0.2), "steel_blue", bevel=0.003)
        for sz in (-1, 1):
            K.screw(g, (sx * 0.24, -0.111, sz * 0.17), (0, 1, 0), 0.0055, "steel")
    # the front frame and the face
    t = 0.02
    for (cx, cz, hx, hz) in ((0, hh - t / 2, hw, t / 2), (0, -hh + t / 2, hw, t / 2), (hw - t / 2, 0, t / 2, hh), (-hw + t / 2, 0, t / 2, hh)):
        box(g, (cx, -0.008, cz), (hx, 0.016, hz), "steel_blue", bevel=0.004)
    screws(g, [(sx * (hw - 0.01), 0.0082, sz * (hh - 0.01)) for sx in (-1, 1) for sz in (-1, 1)], (0, 1, 0), 0.0055, "steel")
    fw, fh = hw - t, hh - t
    quad(g, [(-fw, -0.002, -fh), (fw, -0.002, -fh), (fw, -0.002, fh), (-fw, -0.002, fh)], "frost")
    # the band and the shield sit on the face
    band = 0.05
    box(g, (0, 0.0, -fh + band / 2), (fw, 0.0015, band / 2), "signal_red")
    for k in range(9):
        x = -fw + 0.04 + k * 0.08
        polygon(g, [(x - 0.012, -fh + 0.004), (x + 0.012, -fh + 0.004), (x + 0.03, -fh + band - 0.004), (x + 0.006, -fh + band - 0.004)],
                0.0017, "bone")
    sh = [(x, z + 0.02) for x, z in shield_outline(0.2, 0.27)]
    polygon(g, sh, 0.0024, "steel_blue", depth=0.0035)
    inner = [(x * 0.78, z * 0.78 + 0.02 * 0.22 + 0.006) for x, z in shield_outline(0.2, 0.27)]
    polygon(g, [(x, z) for x, z in inner], 0.0028, "graphite", depth=0.0)
    jack(g, (hw + 0.0045, -0.07, -0.12), (1, 0, 0), f, 0.4)
    pip_socket(g, (-(hw - 0.03), 0.0082, -(hh - 0.01)), (0, 1, 0), r=0.004)
    K.make_object("Body", g, m, root)
    gl = Geo()
    quad(gl, [(-fw, 0.0, -fh + band - 0.02), (fw, 0.0, -fh + band - 0.02), (fw, 0.0, fh - 0.02), (-fw, 0.0, fh - 0.02)], "sign_glow")
    # the chevron in the shield lights too
    chev = [(0.0, 0.07), (0.055, 0.015), (0.035, -0.005), (0.0, 0.03), (-0.035, -0.005), (-0.055, 0.015)]
    fan_poly(gl, chev, 0.0052, "sign_glow", (0.0, 0.0))
    glows = [K.make_object("Glow", gl, m, root, (0, -0.001, 0.02)),
             pip_glow(m, root, "Pip", (-(hw - 0.03), 0.0082, -(hh - 0.01)), (0, 1, 0), r=0.004)]
    return {"root": root, "glows": glows, "lamps": [point((0, 0.12, 0.0), 18)]}


# =====================================================================================================
# Settler Patchwork lamps
# =====================================================================================================

def build_shack_light(m):
    """2 x 2 x 2. Hangs from a ceiling: a zinc shade, enamel inside, over a bare bulb, on a scrap-wood block."""
    root = K.make_empty("ShackLight")
    g = Geo()
    f = SET
    box(g, (0, 0, 0.222), (0.13, 0.08, 0.026), "plank", bevel=0.004)
    box(g, (0.0, 0.0, 0.222), (0.085, 0.13, 0.024), "plank_dark", bevel=0.004)
    for p in ((-0.1, -0.05), (0.1, 0.05), (-0.06, 0.1), (0.06, -0.1)):
        K.screw(g, (p[0], p[1], 0.1975 if abs(p[1]) < 0.09 else 0.1975), (0, 0, -1), 0.005, "zinc")
    lathe_on(g, [(0.0, 0.0), (0.045, 0.0), (0.045, 0.012), (0.03, 0.022), (0.0, 0.022)], "zinc", (0, 0, 0.174), (0, 0, 1), 28)
    K.sweep(g, [(0, 0, 0.176), (0.004, 0.002, 0.12), (-0.003, -0.002, 0.07), (0, 0, 0.04)], 0.0055, "cable", segs=8)
    # the shade: zinc outside, enamel inside, a rolled rim; a riveted rust patch
    lathe_on(g, [(0.03, 0.06), (0.036, 0.06), (0.06, 0.045), (0.19, -0.075), (0.2, -0.082), (0.2, -0.088), (0.19, -0.085),
                 (0.055, 0.035), (0.03, 0.045)], "zinc", (0, 0, 0), (0, 0, 1), 40)
    lathe_on(g, [(0.031, 0.044), (0.055, 0.0345), (0.189, -0.0845), (0.186, -0.0835), (0.054, 0.032), (0.031, 0.04)], "enamel_in", (0, 0, -0.0012), (0, 0, 1), 40)
    patch = Matrix.Rotation(math.radians(30), 4, "Z") @ Matrix.Translation((0.12, 0, -0.008)) @ Matrix.Rotation(math.radians(41), 4, "Y")
    pg = Geo()
    box(pg, (0, 0, 0), (0.045, 0.04, 0.0025), "rust", bevel=0.001)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.bl.dome(pg, (sx * 0.035, sy * 0.03, 0.0025), (0, 0, 1), 0.004, "zinc", segs=6)
    g.extend(pg, patch)
    lathe_on(g, [(0.0, 0.0), (0.033, 0.0), (0.033, 0.06), (0.0, 0.06)], "zinc", (0, 0, 0.0), (0, 0, 1), 20)
    # the socket and bulb
    lathe_on(g, [(0.0, 0.0), (0.02, 0.0), (0.02, 0.035), (0.017, 0.04), (0.0, 0.04)], "brass", (0, 0, -0.04), (0, 0, 1), 20)
    bulb_c = Vector((0, 0, -0.085))
    prof = [(0.0, 0.045), (0.014, 0.045), (0.016, 0.03)]
    for k in range(0, 9):
        a = math.radians(90 - 180 * k / 8)
        prof.append((0.032 * math.cos(a), 0.032 * math.sin(a) - 0.002))
    prof.append((0.0, -0.034))
    lathe_on(g, prof, "bulb", bulb_c, (0, 0, 1), 24)
    K.sweep(g, [(-0.008, 0, -0.06), (-0.008, 0, -0.085), (0.008, 0, -0.085), (0.008, 0, -0.06)], 0.0012, "filament", segs=5)
    jack(g, (0.13 + 0.008, 0.0, 0.198), (1, 0, 0), {"dark": "plank_dark", "bolt": "zinc"}, 0.45)
    K.make_object("Body", g, m, root)
    gl = Geo()
    lathe_on(gl, [(r * 1.04, h * 1.04) for r, h in prof], "shack_glow", (0, 0, 0), (0, 0, 1), 24)
    glows = [K.make_object("Glow", gl, m, root, tuple(bulb_c))]
    return {"root": root, "glows": glows, "lamps": [point((0, 0, -0.1), 35, WARM)], "hang": True}


def sprout_paths():
    """The Neon Sign's pictogram in the x-z plane: a ring, a stem and two leaves (lists of points)."""
    ring = [(0.25 * math.cos(math.radians(a)), 0.25 * math.sin(math.radians(a))) for a in range(-60, 241, 15)]
    stem = [(0.0, -0.19), (0.0, -0.05), (0.004, 0.06), (0.0, 0.14)]
    left = [(0.0, -0.02), (-0.05, 0.02), (-0.12, 0.03), (-0.15, 0.0), (-0.12, -0.04), (-0.05, -0.04), (0.0, -0.02)]
    right = [(0.0, 0.07), (0.05, 0.12), (0.12, 0.14), (0.15, 0.11), (0.12, 0.07), (0.05, 0.06), (0.0, 0.07)]
    soil = [(-0.14, -0.19), (0.14, -0.19)]
    return [ring, stem, left, right, soil]


def build_neon_sign(m):
    """3 x 3 x 1. A Settler shop sign for a wall (its base is its back): neon tubes bent into a sprout in a ring
    (fresh produce, a pictogram) on a patched plank board, with its rusty transformer."""
    root = K.make_empty("NeonSign")
    g = Geo()
    # the board: three planks on two rusty rails, zinc corner plates
    for sx in (-1, 1):
        box(g, (sx * 0.22, -0.112, 0), (0.025, 0.012, 0.36), "rust", bevel=0.004)
    for k, mat in enumerate(("plank", "plank_dark", "plank")):
        z = -0.24 + k * 0.24
        box(g, (0, -0.088, z), (0.355, 0.012, 0.117), mat, bevel=0.005)
        for sx in (-1, 1):
            K.bl.dome(g, (sx * 0.22, -0.076, z + 0.06), (0, 1, 0), 0.006, "zinc", segs=6)
            K.bl.dome(g, (sx * 0.22, -0.076, z - 0.06), (0, 1, 0), 0.006, "zinc", segs=6)
    for sx in (-1, 1):
        for sz in (-1, 1):
            polygon(g, [(sx * 0.355, sz * 0.355), (sx * 0.355, sz * 0.27), (sx * 0.27, sz * 0.355)][:: sx * sz], -0.0755, "zinc", depth=0.002)
    # the tubes on standoff clips, a cable down to the transformer at the foot
    y = -0.05
    paths = sprout_paths()
    for path in paths:
        pts = [(x, y, z + 0.03) for x, z in path]
        K.sweep(g, pts, 0.0085, "neon_off", segs=8)
        for p in pts[:: max(len(pts) // 3, 2)]:
            cylinder(g, (p[0], -0.077, p[2]), (p[0], y, p[2]), 0.004, "zinc", segs=6)
    lathe_on(g, [(0.0, 0.0), (0.01, 0.0), (0.01, 0.012), (0.0, 0.012)], "rubber", (-0.14, y, -0.16), (0, -1, 0), 10)
    box(g, (0.21, -0.045, -0.29), (0.07, 0.03, 0.045), "rust", bevel=0.006)
    for k in range(4):
        box(g, (0.165 + k * 0.03, -0.0145, -0.29), (0.008, 0.002, 0.03), "hole")
    K.sweep(g, [(0.15, -0.04, -0.27), (0.08, -0.06, -0.24), (0.0, -0.06, -0.19)], 0.0045, "cable", segs=6)
    pip_socket(g, (0.255, -0.015, -0.265), (0, 1, 0), r=0.005)
    jack(g, (0.355 + 0.0, -0.06, -0.2), (1, 0, 0), {"dark": "graphite", "bolt": "zinc"}, 0.45)
    K.make_object("Body", g, m, root)
    gl = Geo()
    for path in paths:
        K.sweep(gl, [(x, y + 0.08, z + 0.03) for x, z in path], 0.0098, "neon_glow", segs=8)
    glows = [K.make_object("Glow", gl, m, root, (0, -0.08, 0)),
             pip_glow(m, root, "Pip", (0.255, -0.015, -0.265), (0, 1, 0), r=0.005)]
    return {"root": root, "glows": glows, "lamps": [point((0, 0.06, 0.03), 20, "#7dffc4")]}


def build_glowstick(m):
    """1 x 1 x 1 (consumable). A green snap-light tube with grey caps and a lanyard loop; always lit, no clips."""
    root = K.make_empty("Glowstick")
    g = Geo()
    cylinder(g, (0, -0.085, 0), (0, 0.085, 0), 0.02, "green_stick", segs=20)
    for sy in (-1, 1):
        lathe_on(g, [(0.0, 0.0), (0.022, 0.0), (0.022, 0.012), (0.018, 0.018), (0.0, 0.018)], "graphite", (0, sy * 0.083, 0), (0, sy, 0), 20)
    loop = [(0.0, -0.096 - 0.0115 * (1 - math.cos(math.radians(a))), 0.0115 * math.sin(math.radians(a))) for a in range(0, 361, 30)]
    K.sweep(g, loop, 0.0025, "cable", segs=6)
    K.make_object("Body", g, m, root)
    return {"root": root, "glows": [], "lamps": [point((0, 0, 0), 6, "#60ff70")]}


# =====================================================================================================
# Render lamps (review renders only, never exported)
# =====================================================================================================

def spot(at, d, angle, watts, color=WHITE):
    return {"kind": "SPOT", "at": at, "dir": d, "angle": angle, "watts": watts, "color": color}


def point(at, watts, color=WHITE):
    return {"kind": "POINT", "at": at, "watts": watts, "color": color}


def add_lamps(rig, lamps, scale=1.0):
    made = []
    for i, l in enumerate(lamps):
        data = bpy.data.lights.new(f"Lamp{i}", l["kind"])
        data.energy = l["watts"] * scale
        data.color = tuple(c ** 2.2 for c in lt.srgb(l["color"]))
        data.shadow_soft_size = 0.02
        if l["kind"] == "SPOT":
            data.spot_size = math.radians(min(l["angle"] * 2.2, 170))
            data.spot_blend = 0.5
        ob = bpy.data.objects.new(f"RenderOnlyLamp{i}", data)
        bpy.context.scene.collection.objects.link(ob)
        ob.parent = rig["root"]
        ob.location = l["at"]
        if l["kind"] == "SPOT":
            ob.rotation_euler = Vector(l["dir"]).to_track_quat("-Z", "Y").to_euler()
        made.append(ob)
    return made


# =====================================================================================================
# Runner and review renders
# =====================================================================================================

ICON_VIEW = K.ICON_VIEW
ICON_UNDER = (-1.0, 1.25, -0.55)


def glow_pose(rig):
    def apply(u):
        s = K.OFF + (1 - K.OFF) * u
        for ob in rig["glows"]:
            ob.scale = (s, s, s)
    return apply


def run_light(part, build, *, half, ortho, icon_target=(0, 0, 0), title="", lines=(), clips=True):
    """Builds `part`, bakes its ``on``/``off`` clips, writes its .glb and lit icon, and with ``--render OUT_DIR`` the
    review views off and on and its sheet."""
    K.reset_scene()
    mats = materials()
    rig = build(mats)
    root = rig["root"]
    pose = glow_pose(rig)
    if clips and rig["glows"]:
        K.bake_pose_clips(rig["glows"], pose)
    stem = part
    info = K.export(root, K.MESHES / f"{stem}.glb", animations=clips and bool(rig["glows"]))
    K.report(K.MESHES / f"{stem}.glb", info)
    K.strip_animation(root)
    for ob in [root, *root.children_recursive]:
        if ob.animation_data:
            ob.animation_data.use_nla = False
    pose(1.0)
    view = ICON_UNDER if rig.get("hang") else ICON_VIEW
    icon = K.icon(root, K.ICONS / f"{stem}_icon.png", view, ortho, icon_target)
    out = K.args_out()
    if out is not None:
        review(part, title or part, list(lines), rig, pose, icon, info, out, half)
    return info


def review(name, title, lines, rig, pose, icon, info, out, half):
    import render_kit as rk
    rl = rk.rl
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    lo, hi = rk._bounds(root)
    hang = rig.get("hang", False)
    size = max(half) * 2
    if hang:
        ground.location.z = hi.z + 0.0005
        ground.visible_shadow = False
        target = Vector((0, 0, hi.z - half[2] * 0.95))
        q = Vector((-1.0, 1.25, -0.6))
    else:
        ground.location.z = lo.z - 0.0005
        target = Vector((0, 0, lo.z + half[2] * 0.95))
        q = Vector((-1.0, 1.25, 0.75))
    cams = {
        "three_quarter": rl.camera("Q", target + q.normalized() * size * 3.4, target, lens=50),
        "front": rl.camera("F", target + Vector((0, 20, 0)), target, ortho=size * 1.32),
        "side": rl.camera("S", target + Vector((-20, 0, 0)), target, ortho=size * 1.32),
    }
    studio = [o for o in sc.objects if o.type == "LIGHT"]
    energy = {o.name: o.data.energy for o in studio}
    world_bg = sc.world.node_tree.nodes["Background"].inputs["Strength"]
    world_strength = world_bg.default_value
    paths = {v: [] for v in rk.VIEWS}
    for state in ("OFF", "ON"):
        lit = state == "ON"
        pose(1.0 if lit else 0.0)
        lamps = add_lamps(rig, rig["lamps"], scale=1.0) if lit else []
        for o in studio:
            o.data.energy = energy[o.name] * (0.35 if lit else 1.0)
        world_bg.default_value = world_strength * (0.4 if lit else 1.0)
        for view in rk.VIEWS:
            p = out / "frames" / f"{name}_{state.lower()}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, rk.W, rk.H)
            paths[view].append((state, p))
        for ob in lamps:
            bpy.data.objects.remove(ob)
    for o in studio:
        o.data.energy = energy[o.name]
    world_bg.default_value = world_strength
    pose(1.0)
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    stats = [rk.fmt_stats(None, info)]
    rk.sheet(name, title, lines, paths, [("ON", icon)], stats, out)
