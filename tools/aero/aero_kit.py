"""The flight parts (parts audit batch D1): Wing, Control Surface, Rotor and Hover Jet, in the part models' look.

They are **Salvager Tech** (``docs/art-direction.md``) like every craftable machine part, built from the batch 4 to 6 kit
(``tools/part_kit``): gunmetal and hull-dark cases, copper hubs and fittings, steel grilles, and cyan only on what is
powered (the Hover Jet's glow ring while it runs). The lifting surfaces are the light **aluminium** the recipes take
("light metal for aircraft"), framed like the rest: visible ribs, rivet lines, a steel leading-edge cuff, a gunmetal
root rib with copper fittings and a mustard tip stripe.

* **Wing** (4 x 1 x 4 blocks, 1 m by 1 m): a cambered aerofoil panel, its leading edge at the part's front.
* **Control Surface** (4 x 1 x 2): a 0.5 m flap behind a gunmetal hinge bar on the part's front edge (the hinge
  sockets), a copper control horn and the logic jack on the bar. The game turns the whole model about the hinge
  (``animation.pivot``) as the flap deflects.
* **Rotor** (3 x 1 x 3): three aluminium blades with hazard tips on a copper hub over a squat finned motor. The game spins
  the whole model (``part.animate``), so everything below the blades is round. Its ``cw`` and ``ccw`` clips set the
  blades' pitch for its Spin setting (the ``blades`` readout).
* **Hover Jet** (2 x 1 x 2): a ducted fan on a base plate with four corner mounting blocks: a round duct with louvred
  intakes and a copper band, a steel exhaust grille on top (the jet's +Y face), a seven-blade fan inside and a cyan
  glow ring in the lip. Its ``run_loop`` clip spins the fan and lights the ring, ``off`` spins it down and darkens it
  (the ``pose`` readout).

Axes are Blender's: x right, y the part's front (glTF -z), z up (the part's +Y). Run ``build_aero.py``, not this file.
"""

from __future__ import annotations

import math
import os
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

import part_kit as K
import light_kit as L
import working_kit as W
from part_kit import Geo, box, cylinder, ring

lt = K.lt
pt = K.pt
SAL = L.SAL
OFF = K.OFF


def materials():
    m = W.materials()
    tex, metal = lt.textured, lt.metal_surface
    m.update({
        # aluminium skin: light grey, a little brighter where it wears
        "alu": tex("a_alu", K.half_res(metal(611, "#9ba2aa", 0.55, 0.65, wear=0.5, edge_light=0.2)), normal_strength=0.5),
        "alu_dark": tex("a_alu_dark", K.half_res(metal(613, "#7f868e", 0.5, 0.7, wear=0.5, edge_light=0.2)), normal_strength=0.5),
        "cuff": tex("a_cuff", K.half_res(metal(617, "#6f757c", 0.4, 0.85, wear=0.5, edge_light=0.3)), normal_strength=0.4),
        "rivet": pt.plain("a_rivet", "#9aa0a6", 0.35, 0.9),
        "grille": tex("a_grille", K.half_res(metal(615, "#7d8288", 0.38, 0.9, wear=0.5, edge_light=0.3)), normal_strength=1.0),
        "lens_dark": pt.plain("a_lens_dark", "#10161a", 0.2, 0.0),
        # gunmetal for big turned shells (duct, motor can): the same paint with a gentle normal map, so a curved shell
        # does not read as crumpled foil
        "shell": tex("a_shell", K.half_res(metal(421, "#5a6068", 0.42, 0.8, wear=0.45, edge_light=0.3)), normal_strength=0.25),
    })
    return m


# =====================================================================================================
# Shared shapes
# =====================================================================================================

def aerofoil(n, chord, thick, camber=0.0, peak=0.4, closed_te=0.0015):
    """A NACA four-digit style section as (s, z) points round its outline, s from 0 (leading edge) to 1 times `chord`
    (trailing edge): the upper surface from the trailing edge forward, then the lower surface back. `thick` and `camber`
    are fractions of the chord; the trailing edge keeps `closed_te` m of thickness."""
    def yt(x):
        return 5 * thick * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x ** 2 + 0.2843 * x ** 3 - 0.1036 * x ** 4)

    def yc(x):
        if camber <= 0:
            return 0.0
        return camber / peak ** 2 * (2 * peak * x - x * x) if x < peak else camber / (1 - peak) ** 2 * ((1 - 2 * peak) + 2 * peak * x - x * x)
    xs = [0.5 * (1 - math.cos(math.pi * k / n)) for k in range(n + 1)]       # cosine spacing: fine at both edges
    upper = [(x * chord, (yc(x) + max(yt(x), closed_te / chord / 2)) * chord) for x in xs]
    lower = [(x * chord, (yc(x) - max(yt(x), closed_te / chord / 2)) * chord) for x in xs]
    return list(reversed(upper)) + lower[1:-1]


def section_at(outline, y_le, z0, direction=-1):
    """Turns (s, z) outline points into (y, z): the leading edge at `y_le`, the chord running along `direction` in y."""
    return [(y_le + direction * s, z0 + z) for s, z in outline]


def loft_x(g, section, x0, x1, mat, caps=True):
    """A prism along x from x0 to x1 through the closed (y, z) `section` (counter-clockwise seen from +x)."""
    n = len(section)
    verts = [(x0, y, z) for y, z in section] + [(x1, y, z) for y, z in section]
    faces = [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
    if caps:
        faces.append(tuple(reversed(range(n))))
        faces.append(tuple(n + k for k in range(n)))
    g.add(verts, faces, mat)


def oriented(section):
    """The section counter-clockwise seen from +x (faces outward for loft_x)."""
    area = 0.0
    for k in range(len(section)):
        y0, z0 = section[k]
        y1, z1 = section[(k + 1) % len(section)]
        area += y0 * z1 - y1 * z0
    return section if area > 0 else list(reversed(section))


def offset_section(section, d):
    """The section pushed out by `d` m along its outward normals (a band over the skin: a rib cap or a paint stripe)."""
    sec = oriented(section)
    n = len(sec)
    out = []
    for k in range(n):
        y0, z0 = sec[(k - 1) % n]
        y1, z1 = sec[(k + 1) % n]
        ty, tz = y1 - y0, z1 - z0
        ln = math.hypot(ty, tz) or 1.0
        # counter-clockwise outline: the outward normal is the tangent turned clockwise
        ny, nz = tz / ln, -ty / ln
        y, z = sec[k]
        out.append((y + ny * d, z + nz * d))
    return out


def surface_z(section, y, top=True):
    """The z of the section's upper (or lower) surface at y (linear between its points)."""
    best = None
    pts = section
    for k in range(len(pts)):
        (ya, za), (yb, zb) = pts[k], pts[(k + 1) % len(pts)]
        if (ya - y) * (yb - y) <= 0 and ya != yb:
            z = za + (zb - za) * (y - ya) / (yb - ya)
            if best is None or (top and z > best) or (not top and z < best):
                best = z
    return best


def rivets(g, points, n=(0, 0, 1), r=0.0052, mat="rivet"):
    for p in points:
        K.dome(g, p, n, r, mat, segs=6, height=r * 0.45)


def skin_panel(g, section, x0, x1, ribs, mat="alu", rib_mat="alu_dark", rivet_pitch=0.045, cuff=0.0, spar=None):
    """An aerofoil panel along x: the skin, raised rib caps at `ribs` (x positions), rivet rows along each rib on both
    faces and along the spar line `spar` (y), and a steel leading-edge cuff over the front `cuff` m of the chord."""
    sec = oriented(section)
    loft_x(g, sec, x0, x1, mat)
    ys = [y for y, _ in sec]
    y_le, y_te = max(ys), min(ys)
    if cuff > 0:
        outer, inner = offset_section(sec, 0.0013), offset_section(sec, -0.0004)
        keep = [k for k in range(len(sec)) if sec[k][0] >= y_le - cuff]
        # the nose points are one run round the outline (it starts at the trailing edge), in order
        strip(g, [outer[k] for k in keep], [inner[k] for k in keep], x0 + 0.001, x1 - 0.001, "cuff")
    for x in ribs:
        tube(g, offset_section(sec, 0.0018), offset_section(sec, -0.0004), x - 0.007, x + 0.007, rib_mat)
        for top in (True, False):
            k = int((y_le - y_te - 0.03) / rivet_pitch)
            for i in range(k + 1):
                y = y_le - 0.02 - i * rivet_pitch
                z = surface_z(sec, y, top)
                if z is None or (cuff > 0 and y > y_le - cuff - 0.004):
                    continue
                n = Vector((0, _slope_normal(sec, y, top), 1 if top else -1)).normalized()
                rivets(g, [(x, y, z + (0.0017 if top else -0.0017))], tuple(n))
    if spar is not None:
        steps = int((x1 - x0 - 0.04) / rivet_pitch)
        for i in range(steps + 1):
            x = x0 + 0.02 + i * rivet_pitch
            if any(abs(x - r) < 0.02 for r in ribs):
                continue
            for top in (True, False):
                z = surface_z(sec, spar, top)
                n = Vector((0, _slope_normal(sec, spar, top), 1 if top else -1)).normalized()
                rivets(g, [(x, spar, z)], tuple(n), r=0.0036)
    return sec


def _slope_normal(sec, y, top):
    """The y part of the surface normal at y (so rivets sit square to a curved skin)."""
    a, b = surface_z(sec, y + 0.005, top), surface_z(sec, y - 0.005, top)
    if a is None or b is None:
        return 0.0
    slope = (a - b) / 0.01
    return -slope if top else slope


def strip(g, outer, inner, x0, x1, mat):
    """A thin solid band along x between two open (y, z) runs (outer over the skin, inner just under it)."""
    n = len(outer)
    verts = ([(x0, y, z) for y, z in outer] + [(x1, y, z) for y, z in outer]
             + [(x0, y, z) for y, z in inner] + [(x1, y, z) for y, z in inner])
    o0, o1, i0, i1 = 0, n, 2 * n, 3 * n
    faces = []
    for k in range(n - 1):
        faces.append((o0 + k, o0 + k + 1, o1 + k + 1, o1 + k))        # outer face
        faces.append((i0 + k, i1 + k, i1 + k + 1, i0 + k + 1))        # inner face
        faces.append((o0 + k, i0 + k, i0 + k + 1, o0 + k + 1))        # end at x0
        faces.append((o1 + k, o1 + k + 1, i1 + k + 1, i1 + k))        # end at x1
    for k in (0, n - 1):
        faces.append((o0 + k, o1 + k, i1 + k, i0 + k))
    g.add(verts, faces, mat)


def tube(g, outer, inner, x0, x1, mat):
    """A band all round a section (a rib cap): the outer and inner closed loops along x, with ring ends."""
    n = len(outer)
    verts = ([(x0, y, z) for y, z in outer] + [(x1, y, z) for y, z in outer]
             + [(x0, y, z) for y, z in inner] + [(x1, y, z) for y, z in inner])
    o0, o1, i0, i1 = 0, n, 2 * n, 3 * n
    faces = []
    for k in range(n):
        k1 = (k + 1) % n
        faces.append((o0 + k, o0 + k1, o1 + k1, o1 + k))
        faces.append((i0 + k, i1 + k, i1 + k1, i0 + k1))
        faces.append((o0 + k, i0 + k, i0 + k1, o0 + k1))
        faces.append((o1 + k, o1 + k1, i1 + k1, i1 + k))
    g.add(verts, faces, mat)


def x_cyl(g, x0, x1, y, z, r, mat, segs=24):
    cylinder(g, (x0, y, z), (x1, y, z), r, mat, segs=segs)


def bolt_x(g, x, y, z, r, mat, side=1):
    """A hex bolt head on a face looking along +x (side 1) or -x."""
    K.hexnut(g, (x, y, z), (side, 0, 0), r, r * 0.6, mat)


# =====================================================================================================
# Wing (4 x 1 x 4): a 1 m x 1 m cambered aerofoil panel; root at -x, tip at +x, leading edge at +y (the part's front)
# =====================================================================================================

WING_SECTION = aerofoil(30, 1.0, 0.055, camber=0.03, peak=0.38)


def build_wing(m):
    f = SAL
    root = K.make_empty("Wing")
    g = Geo()
    sec = section_at(WING_SECTION, 0.5, -0.012)
    ribs = (-0.25, 0.0, 0.25)
    sec = skin_panel(g, sec, -0.47, 0.42, ribs, cuff=0.07, spar=0.5 - 0.3)
    # the root rib: a gunmetal plate at the attach edge, its copper fittings and bolts; the root sockets are on its face
    root_sec = offset_section(sec, 0.004)
    loft_x(g, root_sec, -0.5, -0.47, f["frame"])
    for y in (0.375, 0.125, -0.125, -0.375):
        zt, zb = surface_z(sec, y, True), surface_z(sec, y, False)
        if zt is None:
            continue
        h = max((zt - zb) / 2 - 0.004, 0.004)
        box(g, (-0.493, y, (zt + zb) / 2), (0.009, 0.03, h + 0.007), f["trim"], bevel=0.003)
        bolt_x(g, -0.496, y, (zt + zb) / 2, 0.0065, "steel", side=-1)
        for top in (True, False):
            z = (zt + 0.006) if top else (zb - 0.006)
            K.screw(g, (-0.49, y + 0.018, z), (0, 0, 1 if top else -1), 0.004, "steel")
            K.screw(g, (-0.49, y - 0.018, z), (0, 0, 1 if top else -1), 0.004, "steel")
    # the tip: a mustard stripe band and a rounded gunmetal tip cap
    loft_x(g, offset_section(sec, 0.0008), 0.42, 0.47, "mustard_paint")
    loft_x(g, offset_section(sec, 0.002), 0.47, 0.5, f["frame"])
    # an inspection hatch on top by the root: a gunmetal plate square to the skin, a screw in each corner
    for (hx, hy, hw, hd) in ((-0.36, 0.06, 0.07, 0.045), (0.125, -0.2, 0.05, 0.035)):
        z = surface_z(sec, hy, True)
        tilt = math.atan2(surface_z(sec, hy + 0.01, True) - surface_z(sec, hy - 0.01, True), 0.02)
        rot = Matrix.Rotation(tilt, 3, "X")
        n = rot @ Vector((0, 0, 1))
        c = Vector((hx, hy, z)) + n * 0.0012
        box(g, tuple(c), (hw, hd, 0.0016), f["frame"], bevel=0.0012, rot=rot)
        for sx in (-1, 1):
            for sy in (-1, 1):
                K.screw(g, tuple(c + rot @ Vector((sx * (hw - 0.011), sy * (hd - 0.011), 0.0014))), tuple(n), 0.0042, f["bolt"])
    K.make_object("Wing", g, m, root)
    rig = {"root": root}
    rig["rest"] = lambda: None
    rig["clips"] = {}
    rig["states"] = [("AT REST", lambda: None)]
    return rig


# =====================================================================================================
# Control Surface (4 x 1 x 2): a 1 m x 0.5 m flap behind a hinge bar along its front edge
# =====================================================================================================

HINGE_Y = 0.22                 # the hinge axis (part data animation.pivot [0, 0, -0.22], glTF axes)
FLAP_SECTION = aerofoil(24, 0.47, 0.11, camber=0.0)


def build_control_surface(m):
    f = SAL
    root = K.make_empty("ControlSurface")
    g = Geo()
    sec = section_at(FLAP_SECTION, 0.235, 0.0)
    sec = skin_panel(g, sec, -0.47, 0.42, (-0.25, 0.0, 0.25), rivet_pitch=0.05)
    loft_x(g, offset_section(sec, 0.0008), 0.42, 0.47, "mustard_paint")
    loft_x(g, offset_section(sec, 0.002), 0.47, 0.5, f["frame"])
    loft_x(g, offset_section(sec, 0.002), -0.5, -0.47, f["frame"])
    # the hinge bar along the front edge and its knuckles (one at each hinge socket), copper bolts through them
    x_cyl(g, -0.5, 0.5, HINGE_Y, 0.0, 0.017, "steel", segs=20)
    for x in (-0.375, -0.125, 0.125, 0.375):
        x_cyl(g, x - 0.035, x + 0.035, HINGE_Y, 0.0, 0.024, f["frame"], segs=24)
        box(g, (x, HINGE_Y + 0.021, 0.0), (0.035, 0.009, 0.021), f["dark"], bevel=0.003)
        for sx in (-1, 1):
            K.screw(g, (x + sx * 0.022, HINGE_Y + 0.03, 0.009), (0, 1, 0), 0.0045, f["bolt"])
            K.screw(g, (x + sx * 0.022, HINGE_Y + 0.03, -0.009), (0, 1, 0), 0.0045, f["bolt"])
    for x in (-0.5, 0.5):
        K.hexnut(g, (x * 0.988, HINGE_Y, 0.0), (1 if x > 0 else -1, 0, 0), 0.012, 0.006, f["trim"])
    # the copper control horn on top, a steel pin through it, and the logic jack on the bar's root end
    horn = [(0.2, 0.012), (0.12, 0.012), (0.175, 0.058), (0.2, 0.058)]
    _horn(g, 0.3, horn, f["trim"])
    cylinder(g, (0.286, 0.19, 0.049), (0.314, 0.19, 0.049), 0.0045, "steel", segs=10)
    L.jack(g, (-0.4, HINGE_Y - 0.035, surface_z(sec, HINGE_Y - 0.035, True) or 0.02), (0, -1, 0), f, scale=0.42)
    K.make_object("Flap", g, m, root)
    rig = {"root": root}
    flap = root
    rest_rot = flap.rotation_euler.copy()

    def deflect(deg):
        # about the hinge (x through y = HINGE_Y): trailing edge down for a positive command, as the game turns it
        pivot = Matrix.Translation((0, HINGE_Y, 0)) @ Matrix.Rotation(math.radians(deg), 4, "X") @ Matrix.Translation((0, -HINGE_Y, 0))
        flap.matrix_world = pivot
    rig["rest"] = lambda: deflect(0)
    rig["clips"] = {}
    rig["states"] = [("LEVEL", lambda: deflect(0)), ("DEFLECTED 10°", lambda: deflect(10)), ("LEVEL ", lambda: deflect(0))]
    rig["_root_rest"] = rest_rot
    rig["state_view"] = "side"
    return rig


def _horn(g, x, pts, mat, t=0.007):
    """A flat plate of thickness 2t at x through the (y, z) outline `pts`."""
    n = len(pts)
    verts = [(x - t, y, z) for y, z in pts] + [(x + t, y, z) for y, z in pts]
    faces = [tuple(reversed(range(n))), tuple(n + k for k in range(n))]
    for k in range(n):
        k1 = (k + 1) % n
        faces.append((k, k1, n + k1, n + k))
    g.add(verts, faces, mat)


# =====================================================================================================
# Rotor (3 x 1 x 3): three blades on a copper hub over a squat finned motor; the whole model spins in the game
# =====================================================================================================

BLADE_Z = 0.07                 # the blade disc (collision cylinder: radius 0.36, 0.08 high, at y 0.07)
BLADE_R = 0.355
PITCH = 14.0                   # degrees; clockwise seen from the top needs the leading edge (-y side of a +x blade) up


def blade_geo(m_):
    """One blade along +x from its root, about its own origin (the hub centre at the blade's height)."""
    g = Geo()
    # steel shank out of the hub
    cylinder(g, (0.03, 0, 0), (0.1, 0, 0), 0.011, "steel", segs=12)
    cylinder(g, (0.085, 0, 0), (0.1, 0, 0), 0.015, "copper", segs=14)
    # the blade: a thin symmetric aerofoil, tapering from 0.075 to 0.05 chord, its tip in hazard paint
    stations = [0.095, 0.15, 0.22, 0.29, BLADE_R - 0.045, BLADE_R]
    rings = []
    for x in stations:
        t = (x - stations[0]) / (BLADE_R - stations[0])
        chord = 0.1 - 0.035 * t
        sec = aerofoil(12, chord, 0.12 if x == stations[0] else 0.1)
        # the chord centred on the span axis, leading edge at -y (a clockwise blade moving toward -y)
        rings.append([(x, -chord * 0.4 + s, z) for s, z in sec])
    n = len(rings[0])
    for i in range(len(rings) - 1):
        mat = "hazard" if stations[i] >= BLADE_R - 0.046 else "alu"
        verts = rings[i] + rings[i + 1]
        faces = [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
        g.add(verts, faces, mat)
    g.add(rings[0], [tuple(reversed(range(n)))], "alu")
    g.add(rings[-1], [tuple(range(n))], "hazard")
    return g


def build_rotor(m):
    f = SAL
    root = K.make_empty("Rotor")
    g = Geo()
    zb = -0.125
    # base flange with a ring of bolts, the finned motor can, a copper winding band and its cap
    K.lathe(g, [(0.0, zb), (0.112, zb), (0.112, zb + 0.012), (0.104, zb + 0.018), (0.0, zb + 0.018)], f["dark"], steps=48)
    for k in range(10):
        a = 2 * math.pi * k / 10
        K.hexnut(g, (math.cos(a) * 0.098, math.sin(a) * 0.098, zb + 0.016), (0, 0, 1), 0.0062, 0.004, f["bolt"])
    K.lathe(g, [(0.0, zb + 0.016), (0.078, zb + 0.016), (0.082, zb + 0.022), (0.082, 0.012), (0.074, 0.022), (0.0, 0.022)],
            "shell", steps=48)
    for k in range(18):
        a = 2 * math.pi * k / 18
        box(g, (math.cos(a) * 0.088, math.sin(a) * 0.088, -0.05), (0.009, 0.0022, 0.05), f["frame"], bevel=0.0012,
            rot=Matrix.Rotation(a, 3, "Z"))
    ring(g, -0.008, 0.004, 0.081, 0.097, f["trim"], segs=48)
    ring(g, -0.104, -0.096, 0.081, 0.097, f["trim"], segs=48)
    K.lathe(g, [(0.0, 0.02), (0.064, 0.02), (0.066, 0.032), (0.058, 0.04), (0.0, 0.04)], f["dark"], steps=48)
    # the copper hub the blades clamp into, a ring of bolts and a chrome spinner on top
    K.lathe(g, [(0.0, 0.038), (0.05, 0.038), (0.054, 0.044), (0.054, 0.096), (0.05, 0.1), (0.0, 0.1)], f["trim"], steps=48)
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        K.hexnut(g, (math.cos(a) * 0.04, math.sin(a) * 0.04, 0.1), (0, 0, 1), 0.0055, 0.004, "steel")
    K.lathe(g, [(0.0, 0.1), (0.036, 0.1)] + [(0.036 * math.cos(math.radians(a)), 0.1 + 0.024 * math.sin(math.radians(a))) for a in range(10, 91, 10)],
            "chrome", steps=32)
    K.make_object("Motor", g, m, root)
    blades = []
    for k in range(3):
        a = 2 * math.pi * k / 3
        ob = K.make_object(f"Blade{k}", blade_geo(m), m, root, (0, 0, BLADE_Z))
        ob.rotation_mode = "XYZ"
        ob.rotation_euler = (math.radians(-PITCH), 0, a)
        blades.append(ob)
    rig = {"root": root, "blades": blades}
    W.keep(rig, *blades)

    def pitch(sign):
        def apply(u=1.0):
            for k, ob in enumerate(blades):
                ob.rotation_mode = "XYZ"
                ob.rotation_euler = (math.radians(-PITCH * sign * u), 0, 2 * math.pi * k / 3)
        return apply

    def turn(sign_from, sign_to):
        def pose(t):
            u = K.smooth(t / 0.25)
            p = sign_from * (1 - u) + sign_to * u
            for k, ob in enumerate(blades):
                ob.rotation_mode = "XYZ"
                ob.rotation_euler = (math.radians(-PITCH * p), 0, 2 * math.pi * k / 3)
        return pose
    rig["rest"] = lambda: pitch(1)()
    rig["clips"] = {"cw": (0.25, False, turn(-1, 1)), "ccw": (0.25, False, turn(1, -1))}
    rig["states"] = [("CLOCKWISE", lambda: pitch(1)()), ("COUNTER-CLOCKWISE", lambda: pitch(-1)()), ("CLOCKWISE ", lambda: pitch(1)())]
    return rig


# =====================================================================================================
# Hover Jet (2 x 1 x 2): a ducted fan on a base plate; its exhaust grille is the top (+Y) face
# =====================================================================================================

DUCT_R, DUCT_IN = 0.226, 0.192
FAN_BLADES = 7
FAN_SPEED = 3.0                # revolutions a second in run_loop
LIP = 0.118


def fan_geo():
    g = Geo()
    # hub and spinner (pointing at the exhaust)
    K.lathe(g, [(0.0, -0.022), (0.05, -0.022), (0.052, 0.012), (0.044, 0.02)] +
            [(0.044 * math.cos(math.radians(a)), 0.02 + 0.02 * math.sin(math.radians(a))) for a in range(15, 91, 15)], "gun_dark", steps=32)
    ring(g, -0.006, 0.004, 0.051, 0.054, "copper", segs=32)
    for k in range(FAN_BLADES):
        a = 2 * math.pi * k / FAN_BLADES
        # a swept, pitched blade: a twisted quad strip from the hub out to just inside the duct
        verts, faces = [], []
        stations = 6
        for i in range(stations + 1):
            r = 0.05 + (DUCT_IN - 0.006 - 0.05) * i / stations
            chord = 0.06 + 0.022 * i / stations
            twist = math.radians(30 - 12 * i / stations)
            sweep = 0.25 * i / stations
            for s, th in ((-0.5, 0.0), (0.5, 0.0), (0.5, 1.0), (-0.5, 1.0)):
                c = chord * s
                dz = 0.004 * (1 if th else -1)
                lx, ly, lz = r, c * math.cos(twist) - dz * math.sin(twist), c * math.sin(twist) + dz * math.cos(twist)
                ang = a + sweep
                verts.append((lx * math.cos(ang) - ly * math.sin(ang), lx * math.sin(ang) + ly * math.cos(ang), lz))
        for i in range(stations):
            b0, b1 = 4 * i, 4 * (i + 1)
            for q in range(4):
                q1 = (q + 1) % 4
                faces.append((b0 + q, b0 + q1, b1 + q1, b1 + q))
        faces.append((0, 3, 2, 1))
        faces.append((4 * stations, 4 * stations + 1, 4 * stations + 2, 4 * stations + 3))
        g.add(verts, faces, "alu")
    ring(g, 0.01, 0.016, 0.03, 0.045, "copper", segs=32)
    return g


def build_hover_jet(m):
    f = SAL
    root = K.make_empty("HoverJet")
    g = Geo()
    zb = -0.125
    top = W.base_plate(g, zb, 0.247, 0.247, f, t=0.01, inset=0.03)
    # four corner mounting blocks with copper bolts, braced against the duct
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = Vector((sx * 0.205, sy * 0.205, top + 0.03))
            box(g, tuple(c), (0.04, 0.04, 0.03), f["frame"], bevel=0.005)
            box(g, (c.x, c.y, top + 0.062), (0.03, 0.03, 0.003), f["dark"], bevel=0.0015)
            K.screw(g, (c.x, c.y, top + 0.065), (0, 0, 1), 0.007, f["bolt"])
            for d in ((sx, 0), (0, sy)):
                K.screw(g, (c.x + d[0] * 0.04, c.y + d[1] * 0.04, c.z), (d[0], d[1], 0), 0.005, "steel")
    # the duct: a turned shell, flared at the exhaust lip, dark inside
    K.lathe(g, [(DUCT_IN, top), (DUCT_R - 0.004, top), (DUCT_R, top + 0.006), (DUCT_R, LIP - 0.022), (DUCT_R + 0.006, LIP - 0.012),
                (DUCT_R + 0.004, LIP - 0.002), (DUCT_R - 0.004, LIP + 0.002), (DUCT_IN + 0.012, LIP - 0.002), (DUCT_IN, LIP - 0.014)],
            "shell", steps=64)
    K.lathe(g, [(DUCT_IN + 0.0005, LIP - 0.016), (DUCT_IN + 0.0005, top + 0.002), (DUCT_IN - 0.0005, top + 0.002), (DUCT_IN - 0.0005, LIP - 0.016)],
            "gun_dark", steps=64)
    K.disc(g, (0, 0, top + 0.004), (0, 0, 1), DUCT_IN, "hole", segs=48)
    # copper bands round the duct, louvred intakes between the corner blocks
    ring(g, top + 0.072, top + 0.084, DUCT_R - 0.001, DUCT_R + 0.007, f["trim"], segs=64)
    ring(g, top + 0.15, top + 0.158, DUCT_R - 0.001, DUCT_R + 0.005, f["trim"], segs=64)
    for k in range(4):
        a = math.pi / 2 * k
        d = Vector((math.cos(a), math.sin(a), 0))
        sd = Vector((-d.y, d.x, 0))
        rot = Matrix.Rotation(a, 3, "Z")
        box(g, tuple(d * (DUCT_R + 0.004) + Vector((0, 0, top + 0.036))), (0.006, 0.07, 0.03), f["dark"], bevel=0.003, rot=rot)
        for i in range(4):
            z = top + 0.015 + i * 0.014
            box(g, tuple(d * (DUCT_R + 0.011) + Vector((0, 0, z))), (0.0035, 0.06, 0.0028), "steel", bevel=0.001,
                rot=rot @ Matrix.Rotation(math.radians(-30), 3, "Y"))
        for s in (-1, 1):
            K.screw(g, tuple(d * (DUCT_R + 0.01) + sd * s * 0.064 + Vector((0, 0, top + 0.036))), tuple(d), 0.0038, f["bolt"])
    # the motor on four stator struts, low in the duct
    zs = top + 0.03
    cylinder(g, (0, 0, top + 0.004), (0, 0, top + 0.13), 0.044, f["dark"], segs=32)
    ring(g, top + 0.07, top + 0.09, 0.043, 0.047, f["trim"], segs=32)
    for k in range(4):
        a = math.pi / 4 + math.pi / 2 * k
        box(g, (math.cos(a) * 0.12, math.sin(a) * 0.12, zs), (0.075, 0.006, 0.014), f["frame"], bevel=0.002, rot=Matrix.Rotation(a, 3, "Z"))
    # the exhaust grille: concentric steel rings and spokes across the lip
    zg = LIP - 0.012
    for r in (0.11, DUCT_IN - 0.004):
        ring(g, zg - 0.004, zg + 0.004, r - 0.003, r + 0.003, "grille", segs=48)
    for k in range(6):
        a = math.pi / 6 + math.pi / 3 * k
        box(g, (math.cos(a) * 0.112, math.sin(a) * 0.112, zg), (0.078, 0.0028, 0.0045), "grille", bevel=0.0012, rot=Matrix.Rotation(a, 3, "Z"))
    cylinder(g, (0, 0, zg - 0.005), (0, 0, zg + 0.006), 0.036, f["dark"], segs=32)
    K.hexnut(g, (0, 0, zg + 0.006), (0, 0, 1), 0.012, 0.005, f["trim"])
    # the unlit glow ring's dark channel in the lip, and the logic jack on the front of the plate
    ring(g, LIP - 0.009, LIP - 0.004, DUCT_IN + 0.0015, DUCT_IN + 0.009, "lens_dark", segs=64)
    L.jack(g, (0.0, 0.232, top), (0, 1, 0), f, scale=0.5)
    K.make_object("Duct", g, m, root)
    fan = K.make_object("Fan", fan_geo(), m, root, (0, 0, top + 0.15))
    glow = K.make_object("GlowRing", K.glow_ring(DUCT_IN + 0.002, DUCT_IN + 0.0085, "cyan_glow", z=0.0, h=0.004), m, root,
                         (0, 0, LIP - 0.0055))
    rig = {"root": root, "fan": fan, "glow": glow}
    W.keep(rig, fan, glow)
    step = 2 * math.pi / FAN_BLADES
    w = FAN_SPEED * 2 * math.pi
    # spinning down from full speed takes `down` seconds and stops three blades on (the rest pose)
    down = 2 * 3 * step / w

    def lit(u):
        # the ring sits in its channel at full size; unlit it shrinks to nothing behind the grille hub
        s = OFF + (1 - OFF) * u
        glow.scale = (s, s, 1.0)

    def run(t):
        fan.rotation_euler.z = -w * t
        lit(1.0)

    def off(t):
        t = min(t, down)
        fan.rotation_euler.z = -(w * t - w * t * t / (2 * down))
        lit(max(0.0, 1 - t / 0.25))

    rig["rest"] = lambda: (setattr(fan.rotation_euler, "z", 0.0), lit(0.0))
    rig["clips"] = {"run_loop": (1.0, True, run), "off": (down, False, off)}
    rig["states"] = [("OFF", lambda: lit(0.0)), ("RUNNING", lambda: (setattr(fan.rotation_euler, "z", -0.21), lit(1.0)))]
    return rig
