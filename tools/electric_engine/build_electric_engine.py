"""Build the Electric Engine at its five levels, their clips and their icons (Electric Engine Design Spec of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/electric_engine_1.glb`` to ``electric_engine_5.glb`` - the Electric Engine, 2 x 2 x 2
  blocks (0.5 m cube), front (+Y in Blender, the game's -Z) with the control panel. Every level keeps the same geometry:
  a housing with chamfered corner blocks and edge rails, inset face plates, a dark front panel (a blue lightning bolt,
  a SPEED label, a slider whose orange fill follows the speed setting, a blue light bar and an indicator dot), a lit
  strip at the front right, a grille under the panel, two copper battery terminals and finned grilles on top, a round
  bearing flange on each side in front of two copper coils with blue insulators, cooling fins above the coils and vents
  below. Each level adds to it, as the reference pictures show:

  1. dark gunmetal, copper coils, dim blue accents;
  2. the coils' insulators and the light bar glow blue;
  3. the vents, the flange rings and the terminals' bases glow too, with small arcs at high speed;
  4. polished chrome plating;
  5. chrome with glowing flange cores and terminal tips, a LVL 5 badge, arcs at high speed.

  The moving pieces are their own nodes under ``Body`` (which hums): the two flange rotors ``FlangeL`` and ``FlangeR``,
  the ``Slider`` and its ``SliderFill``, the indicator ``Lamp`` and the front strip's ``StripU`` and ``StripL``, the
  ``PanelFlash`` over the bolt and label, and the ``Glow*`` lights. glTF clips (the game plays them from the engine's
  ``clip`` readout; see docs/part-data-schema.md):

  - ``off_loop`` (4 s): at rest, the lights off; levels 3 to 5 pulse the indicator dot very slowly.
  - ``start`` (0.6 s): the panel flashes blue, the body settles 0.4 cm and back, the flanges spin up, the lights come on;
    levels 4 and 5 add a brighter flash and a flicker.
  - ``false_start`` (0.5 s): a flicker and back to rest (no battery).
  - ``run_loop`` / ``run_hard_loop`` (2 s): a fine hum (0.1 / 0.25 cm), spinning flanges, the lights on. The hard loop is
    for the upper speed settings.
  - ``gear`` (0.3 s): the panel flashes and the body twitches.
  - ``stop`` (0.4 s): the hum and flanges wind down, the lights go out.
  - ``lever`` (1 s, scrubbed): the slider and its fill from the lowest speed setting (0 s) to the highest (1 s).
  - ``heat`` (1 s, scrubbed): the blue glows from dark (0 s) to full (1 s); the engine's ``heat`` readout drives it.

* ``mods/vanilla/textures/items/electric_engine_1_icon.png`` to ``electric_engine_5_icon.png`` - the hotbar,
  inventory and Fabricator pictures, 256 x 256 on a clear background, three-quarter from the front right, switched on.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/electric_engine/build_electric_engine.py                  # writes the .glb files and the icons
    python tools/electric_engine/build_electric_engine.py --render OUT_DIR # also renders a review sheet into OUT_DIR

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
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_engine"))
import build_gas_engine as ge  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = ge.bbe, ge.bl, ge.lt, ge.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = bbe.FPS
LEVELS = (1, 2, 3, 4, 5)

Geo, box, cylinder, ring, dome, hexnut, quad = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.quad
lathe, disc = bbe.lathe, bbe.disc
glow_box, glow_disc, glow_ring = ge.glow_box, ge.glow_disc, ge.glow_ring

# ---- Engine (metres; 0.5 m cube centred on the origin, front +Y, the viewer's right is -X) ----------------------
CORE, PLATE, BEAM = ge.CORE, ge.PLATE, ge.BEAM
PANEL = (0.035, 0.0225, 0.13, 0.1425)              # front panel: centre x, centre z, half width, half height
FILL_LEFT = 0.125                                 # the slider's track starts here (the viewer's left)
SLIDER_LOW, SLIDER_HIGH = 0.11, -0.04             # knob x at the lowest and highest speed setting
SLIDER_Z = 0.0
BAR_Z, BAR_LEN = -0.07, 0.15                      # the light bar: from FILL_LEFT toward the viewer's right
DOT_AT = (-0.045, -0.07)                          # the indicator dot (x, z) at the bar's right end
STRIP_X = -0.165                                  # the lit strip at the front right
FLANGE_AT = (-0.1, 0.0)                         # (y, z) of each side's flange
FLANGE_R = 0.088
COILS = (0.125, 0.025)                           # y of the two coils on each side
COIL_R = 0.042
TERMINALS = ((0.12, 0.1), (-0.045, -0.02))        # (x, y) of the battery terminals on top
OFF = ge.OFF


def rnd(seed):
    return np.random.default_rng(seed)


# =====================================================================================================
# Materials
# =====================================================================================================

STROKES = {
    "S": [[(1, 0), (0, 0), (0, 0.5), (1, 0.5), (1, 1), (0, 1)]],
    "P": [[(0, 1), (0, 0), (1, 0), (1, 0.5), (0, 0.5)]],
    "E": [[(1, 0), (0, 0), (0, 1), (1, 1)], [(0, 0.5), (0.8, 0.5)]],
    "D": [[(0, 0), (0.7, 0), (1, 0.25), (1, 0.75), (0.7, 1), (0, 1), (0, 0)]],
    "L": [[(0, 0), (0, 1), (1, 1)]],
    "V": [[(0, 0), (0.5, 1), (1, 0)]],
    "5": [[(1, 0), (0, 0), (0, 0.45), (0.8, 0.45), (1, 0.6), (1, 0.85), (0.8, 1), (0, 1)]],
}


def lettering(d, text, x0, top, height, width, gap, ink, stroke):
    """Block letters drawn with lines (no system font, so they are the same everywhere); returns the right edge."""
    for ch in text:
        if ch == " ":
            x0 += width * 0.6
            continue
        for line in STROKES[ch]:
            d.line([(x0 + u * width, top + v * height) for u, v in line], fill=ink, width=stroke, joint="curve")
        x0 += width + gap
    return x0


def bolt(d, x, y, s, fill):
    d.polygon([(x + 0.55 * s, y), (x + 0.1 * s, y + 0.55 * s), (x + 0.42 * s, y + 0.55 * s), (x + 0.25 * s, y + s),
               (x + 0.85 * s, y + 0.38 * s), (x + 0.52 * s, y + 0.38 * s), (x + 0.75 * s, y)], fill=fill)


def speed_label():
    """The panel's top: a blue lightning bolt over a light grey SPEED, on the panel's near-black."""
    w, h = 256, 128
    im = Image.new("RGB", (w, h), (24, 25, 28))
    d = ImageDraw.Draw(im)
    bolt(d, 14, 8, 54, (60, 150, 255))
    lettering(d, "SPEED", 14, 76, 34, 26, 9, (196, 200, 206), 6)
    return np.asarray(im, float) / 255.0


def level_badge():
    """Level 5's badge: blue LVL 5 in a blue frame on dark glass."""
    w, h = 256, 96
    im = Image.new("RGB", (w, h), (8, 20, 40))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([4, 4, w - 5, h - 5], radius=12, outline=(70, 170, 255), width=6)
    lettering(d, "LVL 5", 30, 20, 56, 32, 12, (110, 200, 255), 10)
    return np.asarray(im, float) / 255.0


def emissive_decal(name, rgb, strength):
    mat = lt.decal(name, rgb, rough=0.3)
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    image = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    nt.links.new(image.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def make_materials():
    half = ge.half_res
    return {
        "hole": pt.plain("ee_hole", "#0c0d0f", 0.9, 0.2),
        "slot": pt.plain("ee_slot", "#121316", 0.7, 0.4),
        "panel": pt.plain("ee_panel", "#18191c", 0.55, 0.3),
        "knob": pt.plain("ee_knob", "#8e939a", 0.3, 0.9),
        "fill": pt.plain("ee_fill", "#c8692e", 0.45, 0.2),
        "insulator": pt.plain("ee_insulator", "#1d5fd6", 0.35, 0.0),
        "lamp_off": pt.plain("ee_lamp_off", "#16314f", 0.3, 0.0),
        "glow": pt.plain("ee_glow", "#1d5cff", 0.3, 0.0, emission="#0b47ff", strength=3.0),
        "glow_soft": pt.plain("ee_glow_soft", "#1d5cff", 0.3, 0.0, emission="#0b47ff", strength=1.8),
        "label": lt.decal("ee_label", speed_label(), rough=0.5),
        "badge": emissive_decal("ee_badge", level_badge(), 2.5),
        "gun": lt.textured("ee_gun", half(lt.metal_surface(401, "#45484d", 0.5, 0.72, wear=0.7, edge_light=0.28)), normal_strength=0.8),
        "gun_dark": lt.textured("ee_gun_dark", half(lt.metal_surface(403, "#36383c", 0.55, 0.7, wear=0.6, edge_light=0.22)), normal_strength=0.8),
        "steel": lt.textured("ee_steel", half(lt.metal_surface(405, "#8d9196", 0.3, 0.9, wear=0.6, edge_light=0.3)), normal_strength=1.4),
        "steel_dark": lt.textured("ee_steel_dark", half(lt.metal_surface(407, "#5d6166", 0.38, 0.86, wear=0.6, edge_light=0.3)), normal_strength=1.4),
        "chrome": lt.textured("ee_chrome", half(lt.chrome_surface(409)), normal_strength=1.0),
        "copper": lt.textured("ee_copper", half(lt.metal_surface(411, "#b5673a", 0.32, 0.95, wear=0.5, edge_light=0.35)), normal_strength=0.6),
    }


# role -> material name, per level
ROLES = {
    1: {"frame": "gun", "plate": "gun_dark", "collar": "steel_dark", "flange": "gun", "fins": "gun_dark"},
    2: {"frame": "gun", "plate": "gun_dark", "collar": "steel_dark", "flange": "gun", "fins": "gun_dark"},
    3: {"frame": "gun", "plate": "gun_dark", "collar": "steel_dark", "flange": "gun", "fins": "gun_dark"},
    4: {"frame": "chrome", "plate": "chrome", "collar": "steel", "flange": "chrome", "fins": "steel"},
    5: {"frame": "chrome", "plate": "chrome", "collar": "chrome", "flange": "chrome", "fins": "steel"},
}


# =====================================================================================================
# Housing
# =====================================================================================================

def slats(g, centre, axis_long, axis_out, n, length, pitch, mat, depth=0.004, thick=0.0035):
    """n slats across a grille: each long along axis_long, standing out along axis_out, stepped along the third axis."""
    step_axis = 3 - axis_long - axis_out
    for k in range(n):
        c = list(centre)
        c[step_axis] += (k - (n - 1) / 2) * pitch
        half = [0.0, 0.0, 0.0]
        half[axis_long], half[axis_out], half[step_axis] = length, depth, thick / 2
        box(g, c, half, mat, bevel=0.001)


def grille(g, centre, half, axis_out, sign, n, frame, mat, slat_along):
    """A framed recess on a face (axis_out, outward sign) with n slats long along slat_along."""
    c = list(centre)
    h = list(half)
    h[axis_out] = 0.003
    box(g, c, h, frame, bevel=0.003)
    inner = list(c)
    inner[axis_out] += sign * 0.0032
    hi = [v - 0.006 for v in half]
    hi[axis_out] = 0.0004
    box(g, inner, hi, "hole")
    s = list(c)
    s[axis_out] += sign * 0.004
    step_axis = 3 - slat_along - axis_out
    span = half[step_axis] * 2 - 0.016
    slats(g, s, slat_along, axis_out, n, half[slat_along] - 0.008, span / max(1, n - 1) if n > 1 else 0, mat)


def build_housing(level):
    r = ROLES[level]
    g = Geo()
    box(g, (0, 0, 0), (CORE, CORE, CORE), r["plate"], bevel=0.004)
    for axis in range(3):
        for sign in (-1, 1):
            c = [0.0, 0.0, 0.0]
            c[axis] = sign * (CORE + 0.0055)
            half = [0.168, 0.168, 0.168]
            half[axis] = 0.0055
            box(g, c, half, r["plate"], bevel=0.003)
    # panel seams on the top: four plates
    box(g, (0.0, 0.0, PLATE + 0.0005), (0.168, 0.0015, 0.0008), "hole")
    box(g, (0.0, 0.0, PLATE + 0.0005), (0.0015, 0.168, 0.0008), "hole")
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0 in (-1, 1):
            for s1 in (-1, 1):
                c = [0.0, 0.0, 0.0]
                c[others[0]], c[others[1]] = s0 * BEAM, s1 * BEAM
                half = [0.027, 0.027, 0.027]
                half[axis] = 0.18
                box(g, c, half, r["frame"], bevel=0.008)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * 0.21, sy * 0.21, sz * 0.21)
                box(g, c, (0.034, 0.034, 0.034), r["frame"], bevel=0.013)
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                    p = Vector(c) + Vector(n) * 0.034
                    hexnut(g, p, n, 0.0075, 0.003, r["collar"])
    for sx in (-1, 1):
        for z in (-0.1, 0.1):
            hexnut(g, (sx * BEAM, BEAM + 0.027, z), (0, 1, 0), 0.006, 0.003, r["collar"])
    # the back: two big grilles
    for x in (-0.09, 0.09):
        grille(g, (x, -PLATE - 0.003, 0.0), (0.06, 0.003, 0.12), 1, -1, 9, r["frame"], r["fins"], 0)
    return g


def build_front(level):
    """The control panel (dark face, bolt and SPEED label, slider track, light bar housing), the lit strip's housing at
    the front right, a small grille above it, and the grille under the panel."""
    r = ROLES[level]
    g = Geo()
    y = PLATE
    px, pz, hw, hh = PANEL
    box(g, (px, y + 0.004, pz), (hw + 0.012, 0.005, hh + 0.012), r["frame"], bevel=0.005)
    box(g, (px, y + 0.0095, pz), (hw, 0.001, hh), "panel")
    for x, z in ((px + hw - 0.008, pz + hh - 0.008), (px - hw + 0.008, pz + hh - 0.008),
                 (px + hw - 0.008, pz - hh + 0.008), (px - hw + 0.008, pz - hh + 0.008)):
        hexnut(g, (x, y + 0.0105, z), (0, 1, 0), 0.0045, 0.0025, r["collar"])
    # the bolt and SPEED decal (u runs to the viewer's right, -X)
    lx, lz, lw, lh = px + 0.005, 0.105, 0.1, 0.05
    quad(g, [(lx + lw, y + 0.0108, lz - lh), (lx - lw, y + 0.0108, lz - lh), (lx - lw, y + 0.0108, lz + lh), (lx + lw, y + 0.0108, lz + lh)], "label")
    # the slider's track: a frame, a dark slot and tick marks above it
    tc = (FILL_LEFT + SLIDER_HIGH) / 2 - 0.006
    box(g, (tc, y + 0.011, SLIDER_Z), (0.098, 0.0015, 0.02), r["frame"], bevel=0.0012)
    box(g, (tc, y + 0.0128, SLIDER_Z), (0.092, 0.0005, 0.012), "slot")
    for k in range(9):
        x = FILL_LEFT - 0.004 - k * 0.019
        box(g, (x, y + 0.0128, SLIDER_Z + 0.017), (0.0012, 0.0004, 0.004), "knob")
    # the light bar's housing and the dot's socket
    box(g, (FILL_LEFT - BAR_LEN / 2, y + 0.0108, BAR_Z), (BAR_LEN / 2 + 0.006, 0.0008, 0.0085), "slot")
    cylinder(g, (DOT_AT[0], y + 0.0102, DOT_AT[1]), (DOT_AT[0], y + 0.0118, DOT_AT[1]), 0.0145, r["frame"], segs=24)
    disc(g, (DOT_AT[0], y + 0.0119, DOT_AT[1]), (0, 1, 0), 0.0105, "lamp_off", segs=24)
    if level == 5:
        quad(g, [(px + 0.075, y + 0.0112, -0.125), (px - 0.075, y + 0.0112, -0.125), (px - 0.075, y + 0.0112, -0.092),
                 (px + 0.075, y + 0.0112, -0.092)], "badge")
    else:
        for k in range(4):
            box(g, (px, y + 0.0108, -0.1 - 0.0085 * k), (0.09, 0.0005, 0.0022), "hole")
    # the lit strip's housing at the front right, and a small grille above it
    box(g, (STRIP_X, y + 0.004, -0.02), (0.012, 0.006, 0.09), r["frame"], bevel=0.003)
    box(g, (STRIP_X, y + 0.0102, -0.02), (0.007, 0.0004, 0.082), "slot")
    grille(g, (STRIP_X, y + 0.003, 0.135), (0.022, 0.003, 0.03), 1, 1, 4, r["frame"], r["fins"], 0)
    # the grille under the panel: upright slats
    grille(g, (px, y + 0.003, -0.157), (0.1, 0.003, 0.024), 1, 1, 13, r["frame"], r["fins"], 2)
    return g


def build_slider():
    """The speed slider's knob about its origin (it slides along X in its track)."""
    g = Geo()
    box(g, (0, -0.003, 0), (0.005, 0.005, 0.008), "slot")
    box(g, (0, 0.006, 0), (0.011, 0.007, 0.019), "knob", bevel=0.0025)
    box(g, (0.0, 0.0135, 0), (0.0035, 0.0012, 0.016), "steel_dark", bevel=0.0008)
    return g


def build_fill():
    """The slider's orange fill: from its origin (the track's left end) a unit length toward the viewer's right."""
    g = Geo()
    box(g, (-0.5, 0, 0), (0.5, 0.0006, 0.0075), "fill")
    return g


def coil(level, x_face, sx):
    """One coil stack on side sx: copper windings round a core, two blue insulator bands on its outer face."""
    g = Geo()
    prof = [(0.0, -0.118)]
    turns = 15
    for k in range(turns * 2 + 1):
        z = -0.11 + 0.22 * k / (turns * 2)
        prof.append((COIL_R if k % 2 == 0 else COIL_R - 0.003, z))
    prof.append((0.0, 0.112))
    local = Geo()
    lathe(local, prof, "copper", steps=28)
    for z in (-0.122, 0.118):
        lathe(local, [(0.0, z), (COIL_R + 0.005, z), (COIL_R + 0.005, z + 0.008), (0.0, z + 0.008)], "steel_dark", steps=28)
    for a in (-0.38, 0.38):
        v = Vector((math.cos(a), math.sin(a), 0)) * (COIL_R + 0.002)
        box(local, (v.x, v.y, -0.003), (0.0035, 0.006, 0.112), "insulator", rot=Matrix.Rotation(a, 3, "Z"))
    g.extend(local, Matrix.Translation((x_face, 0, 0)) @ Matrix.Rotation(0 if sx > 0 else math.pi, 4, "Z"))
    return g


def build_sides(level):
    """Each side (+X left, -X right): a recess with two coils, cooling fins above them, vents below, the flange's stator."""
    r = ROLES[level]
    g = Geo()
    for sx in (-1, 1):
        face = sx * PLATE
        box(g, (sx * (PLATE + 0.0005), 0.06, 0.0), (0.0006, 0.12, 0.13), "hole")
        for y in COILS:
            c = coil(level, 0.0, sx)
            g.extend(c, Matrix.Translation((sx * (PLATE - 0.008), y, 0.0)))
        # cooling fins: thin plates standing out above the coils
        for k in range(11):
            y = 0.165 - k * 0.0215
            box(g, (sx * (PLATE + 0.01), y, 0.158), (0.012, 0.0022, 0.022), r["fins"], bevel=0.0008)
        box(g, (sx * (PLATE + 0.001), 0.06, 0.158), (0.002, 0.118, 0.026), r["frame"])
        grille(g, (sx * (PLATE + 0.003), 0.06, -0.158), (0.003, 0.11, 0.022), 0, sx, 6, r["frame"], r["fins"], 2)
        # the flange's stator: a raised collar with bolts, a ring and the bore
        y, z = FLANGE_AT
        local = Geo()
        lathe(local, [(0.06, 0.0), (FLANGE_R, 0.0), (FLANGE_R, 0.044), (FLANGE_R - 0.006, 0.05), (0.06, 0.05)], r["flange"], steps=44)
        lathe(local, [(0.05, 0.0), (0.06, 0.0), (0.06, 0.054), (0.05, 0.054)], "copper" if level < 4 else "steel", steps=40)
        lathe(local, [(0.03, 0.0), (0.05, 0.0), (0.05, 0.004), (0.03, 0.004)], r["frame"], steps=40)
        for k in range(8):
            a = 2 * math.pi * k / 8 + math.pi / 8
            hexnut(local, (0.073 * math.cos(a), 0.073 * math.sin(a), 0.05), (0, 0, 1), 0.0045, 0.0025, r["collar"])
        g.extend(local, Matrix.Translation((face, y, z)) @ Matrix.Rotation(sx * math.pi / 2, 4, "Y"))
    return g


def flange_rotor(level):
    """A flange's turning hub about its axis (Z here; turned onto X when placed): a ringed disc round the bore."""
    r = ROLES[level]
    g = Geo()
    lathe(g, [(0.018, 0.004), (0.048, 0.004), (0.048, 0.022), (0.044, 0.026), (0.022, 0.026), (0.018, 0.03)], r["flange"], steps=40)
    lathe(g, [(0.012, 0.0), (0.018, 0.0), (0.018, 0.032), (0.012, 0.032)], "steel", steps=24)
    disc(g, (0, 0, 0.012), (0, 0, 1), 0.012, "hole", segs=20)
    for k in range(4):
        a = 2 * math.pi * k / 4
        box(g, (0.033 * math.cos(a), 0.033 * math.sin(a), 0.027), (0.006, 0.0025, 0.0015), "steel_dark",
            rot=Matrix.Rotation(a, 3, "Z"))
    return g


def build_top(level):
    """The two battery terminals on their bolted plates, and two finned grilles."""
    r = ROLES[level]
    g = Geo()
    top = PLATE
    for x, y in TERMINALS:
        box(g, (x, y, top + 0.004), (0.033, 0.033, 0.004), r["frame"], bevel=0.003)
        for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            hexnut(g, (x + dx * 0.025, y + dy * 0.025, top + 0.008), (0, 0, 1), 0.004, 0.0025, r["collar"])
        g.extend(_lathe([(0.0, top + 0.008), (0.017, top + 0.008), (0.017, top + 0.012), (0.0, top + 0.012)], "steel_dark"),
                 Matrix.Translation((x, y, 0)))
        prof = [(0.0, top + 0.012)]
        for k in range(5):
            z = top + 0.012 + k * 0.009
            prof += [(0.0145, z), (0.0145, z + 0.006), (0.0125, z + 0.007), (0.0125, z + 0.009)]
        prof.append((0.0, top + 0.057))
        g.extend(_lathe(prof, "copper"), Matrix.Translation((x, y, 0)))
        g.extend(_lathe([(0.0, top + 0.057), (0.009, top + 0.057), (0.009, top + 0.068), (0.007, top + 0.071), (0.0, top + 0.071)],
                        "steel"), Matrix.Translation((x, y, 0)))
    for x, y in ((-0.11, 0.12), (0.1, -0.12), (-0.11, -0.12)):
        grille(g, (x, y, top + 0.003), (0.065, 0.045, 0.003), 2, 1, 7, r["frame"], r["fins"], 0)
    return g


def _lathe(profile, mat):
    g = Geo()
    lathe(g, profile, mat, steps=24)
    return g


def glows(level):
    """(name, geometry, centre, axes) of the glows the heat clip scales from dark to full; axes says which scale."""
    out = []
    y = PLATE
    bar = Geo()
    box(bar, (-0.5 * BAR_LEN, 0, 0), (0.5 * BAR_LEN, 0.0007, 0.0055), "glow")
    out.append(("GlowBar", bar, (FILL_LEFT, y + 0.0118, BAR_Z), "x"))
    if level >= 2:
        for sx, side in ((1, "L"), (-1, "R")):
            for y_c, tier in zip(COILS, "FB"):
                g = Geo()
                for a in (-0.38, 0.38):
                    v = Vector((math.cos(a), math.sin(a), 0)) * (COIL_R + 0.0058)
                    box(g, (v.x, v.y, 0.0), (0.0006, 0.0035, 0.106), "glow_soft", rot=Matrix.Rotation(a, 3, "Z"))
                placed = Geo()
                placed.extend(g, Matrix.Rotation(0 if sx > 0 else math.pi, 4, "Z"))
                out.append((f"GlowCoil{side}{tier}", placed, (sx * (PLATE - 0.008), y_c, -0.003), "z"))
    if level >= 3:
        px, pz, _, _ = PANEL
        out.append(("GlowGrille", glow_box((0.092, 0.0004, 0.018), "glow_soft"), (px, y + 0.0068, -0.157), "xyz"))
        for sx, side in ((1, "L"), (-1, "R")):
            out.append((f"GlowVent{side}", glow_box((0.0004, 0.104, 0.016), "glow_soft"), (sx * (PLATE + 0.0068), 0.06, -0.158), "xyz"))
            out.append((f"GlowRing{side}", glow_ring(0.0605, 0.0645, Matrix.Rotation(math.pi / 2, 4, "Y"), "glow"),
                        (sx * (PLATE + 0.0545), *FLANGE_AT), "xyz"))
        for k, (x, yy) in enumerate(TERMINALS):
            out.append((f"GlowTerminal{k}", glow_ring(0.0172, 0.0215, Matrix.Identity(4), "glow"), (x, yy, PLATE + 0.0125), "xyz"))
    if level == 5:
        for sx, side in ((1, "L"), (-1, "R")):
            out.append((f"GlowCore{side}", glow_disc(0.0118, (1, 0, 0), "glow", depth=0.001), (sx * (PLATE + 0.0365), *FLANGE_AT), "xyz"))
        for k, (x, yy) in enumerate(TERMINALS):
            out.append((f"GlowTip{k}", glow_disc(0.0072, (0, 0, 1), "glow", depth=0.001), (x, yy, PLATE + 0.0715), "xyz"))
    return out


# =====================================================================================================
# Scene
# =====================================================================================================

def build_engine(level, mats, offset=(0, 0, 0)):
    rig = {"level": level}
    root = rig["root"] = bl.make_empty(f"ElectricEngine{level}")
    root.location = offset
    body = rig["body"] = bl.make_empty("Body", root)
    bl.make_object("Housing", build_housing(level), mats, body)
    bl.make_object("Front", build_front(level), mats, body)
    bl.make_object("Sides", build_sides(level), mats, body)
    bl.make_object("Top", build_top(level), mats, body)
    rotor = flange_rotor(level)
    rig["rotors"] = []
    for sx, side in ((1, "L"), (-1, "R")):
        placed = Geo()
        placed.extend(rotor, Matrix.Rotation(sx * math.pi / 2, 4, "Y"))
        ob = bl.make_object(f"Flange{side}", placed, mats, body, (sx * (PLATE + 0.024), *FLANGE_AT))
        rig["rotors"].append((ob, sx))
    rig["slider"] = bl.make_object("Slider", build_slider(), mats, body, (SLIDER_LOW, PLATE + 0.011, SLIDER_Z))
    rig["fill"] = bl.make_object("SliderFill", build_fill(), mats, body, (FILL_LEFT, PLATE + 0.0134, SLIDER_Z))
    rig["lamps"] = [
        bl.make_object("Lamp", glow_disc(0.0098, (0, 1, 0), "glow", depth=0.001), mats, body, (DOT_AT[0], PLATE + 0.0122, DOT_AT[1])),
        bl.make_object("StripU", glow_box((0.0062, 0.0006, 0.034)), mats, body, (STRIP_X, PLATE + 0.0108, 0.022)),
        bl.make_object("StripL", glow_box((0.0062, 0.0006, 0.034)), mats, body, (STRIP_X, PLATE + 0.0108, -0.062)),
    ]
    rig["flash"] = bl.make_object("PanelFlash", glow_box((0.098, 0.0004, 0.048), "glow_soft"), mats, body,
                                  (PANEL[0] + 0.005, PLATE + 0.0112, 0.105))
    rig["heat"] = []
    for name, geo, centre, axes in glows(level):
        ob = bl.make_object(name, geo, mats, body, centre)
        ob["axes"] = axes
        rig["heat"].append(ob)
    for ob in [body, rig["slider"], rig["fill"], rig["flash"], *rig["lamps"], *rig["heat"], *(o for o, _ in rig["rotors"])]:
        ob.rotation_mode = "XYZ"
    apply_pose(rig, rest_pose())
    set_slider(rig, 0.5)
    set_heat(rig, 0.0)
    return rig


# =====================================================================================================
# Clips
# =====================================================================================================

wobble = bbe.wobble


def rest_pose():
    return {"body": ((0, 0, 0), (0, 0, 0)), "spin": 0.0, "lamp": OFF, "strip": OFF, "flash": OFF}


def humming(t, hard, level, period=2.0, scale=1.0, seed=0):
    """A fine, fast hum: 0.1 cm (0.25 cm in the hard loop), a touch more at the higher levels."""
    amp = (0.0025 if hard else 0.001) * (1.0 + 0.08 * (level - 1)) * scale
    body = ((amp * wobble(seed + 1, t, period, (61, 79, 97)), amp * wobble(seed + 2, t, period, (67, 83, 101)),
             amp * wobble(seed + 3, t, period, (71, 89, 103))),
            (math.radians(0.08) * scale * wobble(seed + 4, t, period, (53, 73)), math.radians(0.06) * scale * wobble(seed + 5, t, period, (59, 77)), 0.0))
    return {"body": body, "lamp": 1.0, "strip": 1.0}


def spin_turns(hard):
    return 6 if hard else 4


def clip_off(level):
    def pose(t):
        p = rest_pose()
        if level >= 3:
            p["lamp"] = max(OFF, 0.55 + 0.25 * math.sin(2 * math.pi * t / 4.0 - math.pi / 2))
        return p
    return 4.0, pose


def clip_run(level, hard):
    period = 2.0

    def pose(t):
        p = humming(t, hard, level, period)
        p["spin"] = 2 * math.pi * spin_turns(hard) * t / period
        return p
    return period, pose


def clip_start(level, length=0.6):
    def pose(t):
        ease = min(1.0, max(0.0, (t - 0.1) / 0.5))
        p = humming(t, False, level, 2.0, scale=ease, seed=50)
        settle = 0.004 * math.sin(math.pi * min(1.0, t / 0.3)) if t < 0.3 else 0.0
        (dx, dy, dz), rot = p["body"]
        p["body"] = ((dx, dy, dz - settle), rot)
        omega = 2 * math.pi * spin_turns(False) / 2.0
        p["spin"] = omega * (t - 0.1) ** 2 / (2 * 0.5) if t > 0.1 else 0.0
        flash = math.exp(-max(0.0, t - 0.03) / (0.14 if level >= 4 else 0.1)) if t >= 0.03 else 0.0
        if level >= 4 and 0.2 < t < 0.4:
            flash = max(flash, 0.7 if math.sin(t * 90) > 0 else 0.0)
        p["flash"] = max(OFF, flash)
        p["lamp"] = 1.0 if t > 0.05 else OFF
        p["strip"] = max(OFF, min(1.0, (t - 0.1) / 0.3)) if t > 0.1 else OFF
        return p
    return length, pose


def clip_false_start(level, length=0.5):
    def pose(t):
        p = rest_pose()
        p["lamp"] = 1.0 if 0.03 < t < 0.3 and math.sin(t * 70) > 0 else OFF
        p["flash"] = max(OFF, 0.5 * math.exp(-max(0.0, t - 0.03) / 0.08)) if t >= 0.03 else OFF
        p["spin"] = math.radians(25) * math.sin(math.pi * min(1.0, t / 0.4))
        return p
    return length, pose


def clip_gear(level, length=0.3):
    def pose(t):
        surge = 1.0 + 2.0 * math.exp(-t / 0.08)
        p = humming(t, False, level, 2.0, scale=surge, seed=70)
        p["spin"] = 2 * math.pi * spin_turns(False) / 2.0 * t
        p["flash"] = max(OFF, math.exp(-t / 0.08))
        return p
    return length, pose


def clip_stop(level, length=0.4):
    def pose(t):
        fade = max(0.0, 1.0 - t / 0.35)
        p = humming(t, False, level, 2.0, scale=fade, seed=90)
        omega = 2 * math.pi * spin_turns(False) / 2.0
        p["spin"] = omega * (t - t * t / (2 * 0.35)) if t < 0.35 else omega * 0.175
        p["lamp"] = max(OFF, fade)
        p["strip"] = max(OFF, fade)
        return p
    return length, pose


def apply_pose(rig, p):
    (loc, rot) = p["body"]
    rig["body"].location = loc
    rig["body"].rotation_euler = Euler(rot)
    for ob, sx in rig["rotors"]:
        ob.rotation_euler = Euler((sx * p["spin"], 0.0, 0.0))
    lamp, strip = rig["lamps"][0], rig["lamps"][1:]
    lamp.scale = (p["lamp"],) * 3
    for ob in strip:
        ob.scale = (p["strip"],) * 3
    rig["flash"].scale = (p["flash"],) * 3


def set_slider(rig, fraction):
    x = SLIDER_LOW + (SLIDER_HIGH - SLIDER_LOW) * fraction
    rig["slider"].location = (x, PLATE + 0.011, SLIDER_Z)
    rig["fill"].scale = (max(0.02, FILL_LEFT - x), 1.0, 1.0)


def heat_scale(ob, h):
    v = max(OFF, h)
    return {"x": (v, 1.0, 1.0), "z": (1.0, 1.0, v)}.get(ob["axes"], (v, v, v))


def set_heat(rig, h):
    for ob in rig["heat"]:
        ob.scale = heat_scale(ob, h)


def clip_objects(rig):
    return [rig["body"], *(o for o, _ in rig["rotors"]), *rig["lamps"], rig["flash"]]


def bake(rig):
    """Keys every clip on every moving node (each clip keys them all), then the two scrubbed clips on their own nodes."""
    level = rig["level"]
    clips = {
        "off_loop": clip_off(level), "start": clip_start(level), "false_start": clip_false_start(level),
        "run_loop": clip_run(level, False), "run_hard_loop": clip_run(level, True), "gear": clip_gear(level),
        "stop": clip_stop(level),
    }
    objs = clip_objects(rig)
    for ob in objs:
        ob.animation_data_create()
    for name, (length, pose) in clips.items():
        actions = {}
        for ob in objs:
            act = bpy.data.actions.new(f"{name}_{ob.name}_{level}")
            ob.animation_data.action = act
            actions[ob.name] = act
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            p = rest_pose()
            loop_end = name.endswith("_loop") and f == frames
            p.update(pose(0.0 if loop_end else f / FPS))
            if loop_end and name.startswith("run"):
                p["spin"] += 2 * math.pi * spin_turns(name == "run_hard_loop")
            apply_pose(rig, p)
            for ob in objs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(actions[ob.name])
            bl.push_track(ob, actions[ob.name], name)
    # scrubbed: the slider and its fill (lowest speed at 0 s, highest at 1 s), the glows (dark at 0 s, full at 1 s)
    lever = [rig["slider"], rig["fill"]]
    acts = {}
    for ob in lever:
        ob.animation_data_create()
        acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"lever_{ob.name}_{level}")
    for f in (0, FPS):
        set_slider(rig, f / FPS)
        rig["slider"].keyframe_insert("location", frame=f)
        rig["fill"].keyframe_insert("scale", frame=f)
    for ob in lever:
        lt.linear_keys(acts[ob.name])
        bl.push_track(ob, acts[ob.name], "lever")
    for ob in rig["heat"]:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"heat_{ob.name}_{level}")
        ob.animation_data.action = act
        for f in range(0, FPS + 1, 4):
            ob.scale = heat_scale(ob, (f / FPS) ** 0.8)
            ob.keyframe_insert("scale", frame=f)
        lt.linear_keys(act)
        bl.push_track(ob, act, "heat")
    for ob in [*objs, *lever, *rig["heat"]]:
        ob.animation_data.use_nla = False
    apply_pose(rig, rest_pose())
    set_slider(rig, 0.5)
    set_heat(rig, 0.0)


def pose_for_icon(rig):
    """The still the icon shows: switched on, the slider just past the middle, the glows as the references show them."""
    level = rig["level"]
    p = rest_pose()
    p["lamp"] = p["strip"] = 1.0
    apply_pose(rig, p)
    set_slider(rig, 0.6)
    set_heat(rig, {1: 0.45, 2: 0.7, 3: 0.85, 4: 0.85, 5: 1.0}[level])


# =====================================================================================================
# Main
# =====================================================================================================

def strip_animation(rig):
    for ob in bbe.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None


def main():
    bbe.reset_scene()
    mats = make_materials()
    rigs = []
    for level in LEVELS:
        rig = build_engine(level, mats, offset=(4.0 * (level - 1), 0, 0))
        bake(rig)
        rig["root"].location = (0, 0, 0)
        bbe.export(rig["root"], MESHES / f"electric_engine_{level}.glb", animations=True)
        rig["root"].location = (4.0 * (level - 1), 0, 0)
        rigs.append(rig)
    bpy.context.scene.frame_set(0)
    for rig in rigs:
        strip_animation(rig)
        pose_for_icon(rig)
    for rig in rigs:
        others = [r for r in rigs if r is not rig]
        for o in others:
            for ob in bbe.mesh_tree(o["root"]):
                ob.hide_render = True
        bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / f"electric_engine_{rig['level']}_icon.png",
                        view=(-1.5, 2.0, 1.45), ortho=0.98, target=tuple(rig["root"].location))
        for o in others:
            for ob in bbe.mesh_tree(o["root"]):
                ob.hide_render = False
    for level in LEVELS:
        for p in (MESHES / f"electric_engine_{level}.glb", ICONS / f"electric_engine_{level}_icon.png"):
            print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_electric_engine
        render_electric_engine.render_all(rigs, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
