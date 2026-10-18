"""Build the Gas Engine at its five levels, their clips and their icons (Engine Design Spec of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/gas_engine_1.glb`` to ``gas_engine_5.glb`` - the Gas Engine, 2 x 2 x 2 blocks (0.5 m
  cube), front (+Y in Blender, the game's -Z) with the control panel. Every level keeps the same geometry: a gunmetal
  housing with chamfered corner blocks and edge rails, inset face plates, a front control panel (an orange POWER label,
  a slider in a slot, vents and an indicator lamp) between two slatted vents, a fuel cap on top, exhaust ports at the
  back of the top, two round bearing flanges on each side, a copper tank at the back of the right side, copper pipes
  over the edges and a mounting lug at the bottom front. Each level adds to it, as the reference pictures show:

  1. clean gunmetal, copper pipes, a plain control panel;
  2. brighter steel with steel pipes, vent grilles in the flanges, a second exhaust and more piping;
  3. gunmetal with a dense copper pipe network round the flanges, a side vent and a faint orange glow;
  4. weathered dark plating with reinforcing strips, glowing cores behind the vents, turbo fans in the flanges,
     braided hoses and a third exhaust;
  5. chrome and polished copper, glowing flange rings, fuel cap and label, a fourth exhaust.

  The moving pieces are their own nodes under ``Body`` (which shakes): ``Pipes`` (breathes at levels 3 to 5), the four
  flange rotors ``FlangeLU``, ``FlangeLL``, ``FlangeRU``, ``FlangeRL``, the ``Slider``, the indicator ``Lamp``, the
  ``LabelFlash`` over the POWER label, ``CoreFlash*`` (levels 4 and 5) and the ``Heat*`` glows. glTF clips (the game
  plays them from the engine's ``clip`` readout; see docs/part-data-schema.md):

  - ``off_loop`` (4 s): at rest, the lamp off; levels 3 to 5 breathe (pipes 1.00 to 1.01).
  - ``start`` (0.8 s): the label and lamp flash orange, a thump settles the body 0.75 cm and back, the flanges spin
    up; levels 4 and 5 pulse their cores.
  - ``false_start`` (0.6 s): a thump and a flicker, then back to rest (out of fuel).
  - ``run_loop`` / ``run_hard_loop`` (2 s): fine vibration (0.2 / 0.4 cm), spinning flanges, the lamp lit. The hard
    loop is for the upper power settings; level 5 flickers its label there.
  - ``gear`` (0.4 s): the label flashes and the body jolts.
  - ``stop`` (0.6 s): the vibration and flanges wind down, a last thump, the lamp goes out.
  - ``lever`` (1 s, scrubbed): the slider from the lowest power setting (0 s) to the highest (1 s).
  - ``heat`` (1 s, scrubbed): the orange glows from cold (0 s) to full (1 s); the engine's ``heat`` readout drives it.

* ``mods/vanilla/textures/items/gas_engine_1_icon.png`` to ``gas_engine_5_icon.png`` - the hotbar, inventory and
  Fabricator pictures, 256 x 256 on a clear background, three-quarter from the front right (levels 3 to 5 warm).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/gas_engine/build_gas_engine.py                  # writes the .glb files and the icons
    python tools/gas_engine/build_gas_engine.py --render OUT_DIR # also renders a review sheet into OUT_DIR

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
sys.path.insert(0, str(HERE.parent / "bearing_engine"))
sys.path.insert(0, str(HERE.parent / "lift"))
import build_bearing_engine as bbe  # noqa: E402  (shapes, export, icon render)
import build_lift as bl  # noqa: E402
import lift_textures as lt  # noqa: E402
import part_textures as pt  # noqa: E402

REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = bbe.FPS
LEVELS = (1, 2, 3, 4, 5)

Geo, box, cylinder, ring, dome, hexnut, sweep, bezier, quad = (
    bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep, bl.bezier, bl.quad)
lathe, disc = bbe.lathe, bbe.disc

# ---- Engine (metres; 0.5 m cube centred on the origin, front +Y, the viewer's right is -X) ----------------------
HALF = 0.25
CORE = 0.205                 # the inner box
PLATE = 0.216                # the face plates' outer face
BEAM = 0.218                 # edge rails' centre line (half 0.027: they reach 0.245)
FLANGES = [(0.02, 0.078), (0.02, -0.088)]        # (y, z) of the upper and lower flange on each side
FLANGE_R = 0.068
SLIDER_LOW, SLIDER_HIGH = -0.034, 0.034           # slider z at the lowest and highest power setting
SLIDER_Y = 0.229
LAMP_AT = (0.0, 0.2285, -0.137)
LABEL_AT = (0.0, 0.2295, 0.088)
VENT_X = 0.13                                     # front vents either side of the panel
TANK_AT = (-0.224, -0.14)                         # copper tank on the right side, toward the back
EXHAUSTS = {1: [0.0], 2: [-0.11, 0.11], 3: [-0.11, 0.11], 4: [-0.13, 0.0, 0.13], 5: [-0.15, -0.05, 0.05, 0.15]}
EXHAUST_Y = -0.165
OFF = 0.01                                        # scale of a glow that is off (hidden inside its housing)


def rnd(seed):
    return np.random.default_rng(seed)


# =====================================================================================================
# Materials (per level: which material plays each role)
# =====================================================================================================

def half_res(surface):
    """A 256-pixel copy of a 512-pixel tileable surface (every other pixel keeps it tiling), to keep the files small."""
    return tuple(a[::2, ::2] for a in surface)


def power_label():
    """The orange POWER label: dark block letters drawn with lines (no system font, so it is the same everywhere)."""
    w, h = 256, 96
    im = Image.new("RGB", (w, h), (214, 112, 38))
    d = ImageDraw.Draw(im)
    d.rectangle([3, 3, w - 4, h - 4], outline=(70, 36, 14), width=5)
    ink, stroke = (36, 22, 14), 9
    x0, top, bottom, gap, lw = 26, 22, 74, 12, 30

    def line(*pts):
        d.line(pts, fill=ink, width=stroke, joint="curve")

    for ch in "POWER":
        x1 = x0 + lw
        if ch == "P":
            line((x0, bottom), (x0, top), (x1 - 6, top), (x1, top + 6), (x1, 44), (x1 - 6, 50), (x0, 50))
        elif ch == "O":
            d.rounded_rectangle([x0, top, x1, bottom], radius=10, outline=ink, width=stroke)
        elif ch == "W":
            line((x0, top), (x0 + 6, bottom), ((x0 + x1) / 2, 46), (x1 - 6, bottom), (x1, top))
        elif ch == "E":
            line((x1, top), (x0, top), (x0, bottom), (x1, bottom))
            line((x0, (top + bottom) / 2), (x1 - 6, (top + bottom) / 2))
        elif ch == "R":
            line((x0, bottom), (x0, top), (x1 - 6, top), (x1, top + 6), (x1, 44), (x1 - 6, 50), (x0, 50), (x1, bottom))
        x0 = x1 + gap
    return np.asarray(im, float) / 255.0


def make_materials():
    m = {
        "hole": pt.plain("ge_hole", "#0d0d0e", 0.9, 0.2),
        "slot": pt.plain("ge_slot", "#141516", 0.7, 0.4),
        "knob": pt.plain("ge_knob", "#d2691e", 0.4, 0.15),
        "lamp_off": pt.plain("ge_lamp_off", "#5a2a10", 0.3, 0.0),
        "rubber": lt.textured("ge_rubber", half_res(lt.rubber_surface(67)), normal_strength=2.0),
        "glow": pt.plain("ge_glow", "#ff8a1e", 0.3, 0.0, emission="#ff7a12", strength=7.0),
        "glow_soft": pt.plain("ge_glow_soft", "#ff9a3a", 0.3, 0.0, emission="#ff8a2a", strength=3.5),
        "label": lt.decal("ge_label", power_label(), rough=0.45),
        "gun": lt.textured("ge_gun", half_res(lt.metal_surface(301, "#55585d", 0.46, 0.75, wear=0.8, edge_light=0.3))),
        "gun_dark": lt.textured("ge_gun_dark", half_res(lt.metal_surface(303, "#46494d", 0.5, 0.72, wear=0.7, edge_light=0.25))),
        "copper": lt.textured("ge_copper", half_res(lt.metal_surface(305, "#b5673a", 0.34, 0.95, wear=0.6, edge_light=0.35))),
        "steel": lt.textured("ge_steel", half_res(lt.metal_surface(307, "#8d9196", 0.3, 0.9, wear=0.6, edge_light=0.3))),
        "steel_dark": lt.textured("ge_steel_dark", half_res(lt.metal_surface(309, "#6f7378", 0.34, 0.88, wear=0.6, edge_light=0.3))),
        "worn": lt.textured("ge_worn", half_res(lt.metal_surface(311, "#3e4043", 0.55, 0.7, wear=1.7, edge_light=0.4))),
        "worn_dark": lt.textured("ge_worn_dark", half_res(lt.metal_surface(313, "#333538", 0.58, 0.68, wear=1.5, edge_light=0.35))),
        "chrome": lt.textured("ge_chrome", half_res(lt.chrome_surface(315)), normal_strength=1.0),
        "copper_bright": lt.textured("ge_copper_bright", half_res(lt.metal_surface(317, "#c8784a", 0.22, 1.0, wear=0.3, edge_light=0.2))),
    }
    return m


# role -> material name, per level
ROLES = {
    1: {"frame": "gun", "plate": "gun_dark", "panel": "gun", "pipe": "copper", "accent": "copper", "collar": "gun_dark", "slat": "copper"},
    2: {"frame": "steel", "plate": "steel_dark", "panel": "steel", "pipe": "steel", "accent": "steel", "collar": "steel_dark", "slat": "steel"},
    3: {"frame": "gun", "plate": "gun_dark", "panel": "gun", "pipe": "copper", "accent": "copper", "collar": "gun_dark", "slat": "copper"},
    4: {"frame": "worn", "plate": "worn_dark", "panel": "worn", "pipe": "copper", "accent": "copper", "collar": "worn_dark", "slat": "worn"},
    5: {"frame": "chrome", "plate": "chrome", "panel": "copper_bright", "pipe": "copper_bright", "accent": "copper_bright", "collar": "chrome", "slat": "chrome"},
}


# =====================================================================================================
# Housing
# =====================================================================================================

def build_housing(level):
    r = ROLES[level]
    g = Geo()
    box(g, (0, 0, 0), (CORE, CORE, CORE), r["plate"], bevel=0.004)
    # inset face plates on all six faces
    for axis in range(3):
        for sign in (-1, 1):
            c = [0.0, 0.0, 0.0]
            c[axis] = sign * (CORE + 0.0055)
            half = [0.168, 0.168, 0.168]
            half[axis] = 0.0055
            box(g, c, half, r["plate"], bevel=0.003)
    # edge rails: chamfered square bars along the twelve edges
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0 in (-1, 1):
            for s1 in (-1, 1):
                c = [0.0, 0.0, 0.0]
                c[others[0]], c[others[1]] = s0 * BEAM, s1 * BEAM
                half = [0.027, 0.027, 0.027]
                half[axis] = 0.18
                box(g, c, half, r["frame"], bevel=0.008)
    # corner blocks: big chamfers, a bolt on each side face
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * 0.21, sy * 0.21, sz * 0.21)
                box(g, c, (0.034, 0.034, 0.034), r["frame"], bevel=0.013)
                for n in ((sx, 0, 0), (0, sy, 0)):
                    p = Vector(c) + Vector(n) * 0.034 + Vector((0, 0, -sz * 0.006))
                    hexnut(g, p, n, 0.0085, 0.004, "steel_dark" if level == 2 else ("chrome" if level == 5 else "gun_dark"))
    # rail bolts along the vertical front rails
    for sx in (-1, 1):
        for z in (-0.1, 0.1):
            hexnut(g, (sx * BEAM, BEAM + 0.027, z), (0, 1, 0), 0.007, 0.003, r["collar"])
    # mounting lug at the bottom front right
    box(g, (-0.165, 0.222, -0.214), (0.03, 0.02, 0.028), r["frame"], bevel=0.005)
    disc(g, (-0.195 - 0.0005, 0.222, -0.214), (-1, 0, 0), 0.011, "hole", segs=16)
    disc(g, (-0.165, 0.2425, -0.214), (0, 1, 0), 0.009, "hole", segs=16)
    if level == 4:
        # reinforcing strips with rivets across the top and the back
        for v in (-0.1, 0.1):
            box(g, (0.0, v, PLATE + 0.003), (0.165, 0.012, 0.003), "worn", bevel=0.002)
            box(g, (0.0, -PLATE - 0.003, v * 1.2), (0.165, 0.003, 0.012), "worn", bevel=0.002)
            for u in np.linspace(-0.15, 0.15, 7):
                dome(g, (u, v, PLATE + 0.006), (0, 0, 1), 0.004, "steel_dark")
                dome(g, (u, -PLATE - 0.006, v * 1.2), (0, -1, 0), 0.004, "steel_dark")
    return g


def build_panel(level):
    """The front control panel: plate, POWER label, the slider's slot, vents and the lamp's housing, plus the side vents."""
    r = ROLES[level]
    g = Geo()
    y = PLATE
    box(g, (0, y + 0.005, -0.005), (0.078, 0.006, 0.142), r["panel"], bevel=0.004)
    box(g, (0, y + 0.009, -0.005), (0.07, 0.003, 0.134), r["frame"], bevel=0.002)
    for x, z in ((-0.062, -0.13), (0.062, -0.13), (-0.062, 0.12), (0.062, 0.12)):
        hexnut(g, (x, y + 0.012, z), (0, 1, 0), 0.0055, 0.003, r["collar"])
    # POWER label: an orange plate with the lettering decal (u runs to the viewer's right, -X)
    lx, ly, lz = LABEL_AT
    box(g, (lx, ly - 0.002, lz), (0.044, 0.0025, 0.017), "knob", bevel=0.002)
    hw, hh = 0.04, 0.015
    quad(g, [(lx + hw, ly + 0.0007, lz - hh), (lx - hw, ly + 0.0007, lz - hh), (lx - hw, ly + 0.0007, lz + hh), (lx + hw, ly + 0.0007, lz + hh)], "label")
    # the slider's slot: a frame round a dark recess
    box(g, (0, y + 0.0125, 0.0), (0.026, 0.0015, 0.058), r["frame"], bevel=0.0012)
    box(g, (0, y + 0.0142, 0.0), (0.014, 0.0006, 0.05), "slot")
    # vent slits under the slot and the lamp housing
    for k in range(4):
        box(g, (0, y + 0.0126, -0.076 - 0.012 * k), (0.034, 0.0006, 0.0036), "hole")
    lxx, lyy, lzz = LAMP_AT
    box(g, (lxx, lyy - 0.0005, lzz), (0.0105, 0.0022, 0.0065), r["frame"], bevel=0.001)
    box(g, (lxx, lyy + 0.0012, lzz), (0.0085, 0.0008, 0.0045), "lamp_off")
    # side vents on the front, either side of the panel: chamfered frame, dark recess, slats
    for sx in (-1, 1):
        x = sx * VENT_X
        box(g, (x, y + 0.003, 0.0), (0.04, 0.004, 0.108), r["frame"], bevel=0.009)
        box(g, (x, y + 0.0072, 0.0), (0.029, 0.0006, 0.092), "hole")
        if level == 4:
            continue                      # level 4 shows its glowing cores through open cutouts
        for k in range(8):
            z = -0.08 + k * 0.0229
            box(g, (x, y + 0.0102, z), (0.027, 0.0022, 0.0042), r["slat"], bevel=0.0012)
    return g


def build_slider(level):
    """The power slider's knob about its origin (it slides along Z in its slot)."""
    g = Geo()
    box(g, (0, -0.004, 0), (0.006, 0.006, 0.01), "slot")
    box(g, (0, 0.006, 0), (0.019, 0.007, 0.012), "knob", bevel=0.003)
    box(g, (0, 0.0135, 0.004), (0.017, 0.0012, 0.004), "knob", bevel=0.001)
    return g


def build_top(level):
    """Fuel cap on its bolted plate, and the exhaust ports at the back of the top."""
    r = ROLES[level]
    g = Geo()
    top = PLATE
    box(g, (0, 0, top + 0.004), (0.062, 0.062, 0.004), r["frame"], bevel=0.003)
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        hexnut(g, (dx * 0.05, dy * 0.05, top + 0.008), (0, 0, 1), 0.0055, 0.003, r["collar"])
    lathe(g, [(0.0, top + 0.008), (0.046, top + 0.008), (0.048, top + 0.011), (0.046, top + 0.022), (0.042, top + 0.025), (0.0, top + 0.025)],
          r["accent"], steps=32)
    box(g, (0, 0, top + 0.03), (0.032, 0.008, 0.0065), r["accent"], bevel=0.003)
    for x in EXHAUSTS[level]:
        cylinder(g, (x, EXHAUST_Y, top), (x, EXHAUST_Y, top + 0.02), 0.024, r["frame"], segs=20, cap1=False)
        ring(g, top + 0.016, top + 0.026, 0.017, 0.026, r["accent"] if level != 2 else "steel", segs=20, center=(x, EXHAUST_Y))
        disc(g, (x, EXHAUST_Y, top + 0.018), (0, 0, 1), 0.017, "hole", segs=20)
        if level >= 4:
            for k in range(3):        # heat shield fins
                box(g, (x, EXHAUST_Y + 0.031, top + 0.006 + 0.007 * k), (0.02, 0.003, 0.0022), r["frame"])
    if level >= 3:
        # an intake box with louvres at the top back of the right side
        box(g, (-0.13, -0.03, top + 0.009), (0.045, 0.035, 0.009), r["frame"], bevel=0.003)
        for k in range(4):
            box(g, (-0.13, -0.055 + k * 0.016, top + 0.0185), (0.036, 0.0035, 0.0012), "hole")
    return g


def flange_stator(level, sx):
    """A flange's fixed rings on side sx, centred on its (y, z) spots."""
    r = ROLES[level]
    g = Geo()
    for y, z in FLANGES:
        local = Geo()
        lathe(local, [(0.05, 0.0), (FLANGE_R, 0.0), (FLANGE_R, 0.016), (FLANGE_R - 0.004, 0.02), (0.05, 0.02)], r["frame"], steps=40)
        lathe(local, [(0.041, 0.0), (0.05, 0.0), (0.05, 0.023), (0.041, 0.023)], r["accent"], steps=40)
        disc(local, (0, 0, 0.003), (0, 0, 1), 0.041, "hole", segs=32)
        for k in range(6):
            a = 2 * math.pi * k / 6 + math.pi / 6
            hexnut(local, (0.059 * math.cos(a), 0.059 * math.sin(a), 0.02), (0, 0, 1), 0.0045, 0.0025, r["collar"])
        m = Matrix.Translation((sx * PLATE, y, z)) @ Matrix.Rotation(sx * math.pi / 2, 4, "Y")
        g.extend(local, m)
    return g


def flange_rotor(level):
    """A flange's turning insert about its axis (Z here; turned onto X when placed)."""
    r = ROLES[level]
    g = Geo()
    if level == 1:
        lathe(g, [(0.0, 0.004), (0.036, 0.004), (0.036, 0.012), (0.0, 0.012)], r["frame"], steps=28)
        for k in range(3):
            a = 2 * math.pi * k / 3
            hexnut(g, (0.024 * math.cos(a), 0.024 * math.sin(a), 0.012), (0, 0, 1), 0.006, 0.004, "copper")
        disc(g, (0, 0, 0.0125), (0, 0, 1), 0.01, "hole", segs=16)
    elif level == 2:
        lathe(g, [(0.031, 0.004), (0.038, 0.004), (0.038, 0.016), (0.031, 0.016)], "steel", steps=28)
        for k in range(5):
            v = -0.024 + k * 0.012
            w = math.sqrt(max(0.0, 0.031 ** 2 - v ** 2))
            box(g, (0, v, 0.012), (w, 0.0028, 0.0028), "steel_dark", bevel=0.0008)
    elif level == 3:
        lathe(g, [(0.026, 0.004), (0.037, 0.004), (0.037, 0.014), (0.026, 0.014)], "copper", steps=28)
        lathe(g, [(0.0, 0.006), (0.011, 0.006), (0.011, 0.018), (0.0, 0.018)], "gun", steps=20)
        for k in range(4):
            a = 2 * math.pi * k / 4
            box(g, (0.019 * math.cos(a), 0.019 * math.sin(a), 0.011), (0.008, 0.0035, 0.003), "copper",
                rot=Matrix.Rotation(a, 3, "Z"))
    elif level == 4:
        # turbo fan: hub and twelve twisted blades
        lathe(g, [(0.0, 0.004), (0.012, 0.004), (0.014, 0.012), (0.009, 0.02), (0.0, 0.021)], "steel", steps=24)
        for k in range(12):
            a = 2 * math.pi * k / 12
            rot = Matrix.Rotation(a, 3, "Z") @ Matrix.Rotation(math.radians(28), 3, "X")
            box(g, Matrix.Rotation(a, 3, "Z") @ Vector((0.025, 0, 0.012)), (0.013, 0.0016, 0.0062), "worn", rot=rot)
        lathe(g, [(0.037, 0.004), (0.04, 0.004), (0.04, 0.018), (0.037, 0.018)], "worn_dark", steps=32)
    else:
        lathe(g, [(0.03, 0.004), (0.038, 0.004), (0.038, 0.016), (0.03, 0.016)], "copper_bright", steps=32)
        lathe(g, [(0.0, 0.006), (0.012, 0.006), (0.012, 0.02), (0.0, 0.02)], "chrome", steps=20)
        for k in range(6):
            a = 2 * math.pi * k / 6
            box(g, (0.021 * math.cos(a), 0.021 * math.sin(a), 0.012), (0.009, 0.0025, 0.0025), "chrome",
                rot=Matrix.Rotation(a, 3, "Z"))
    return g


def build_tank(level):
    """The copper tank on the right side, toward the back, with its collars and straps."""
    r = ROLES[level]
    g = Geo()
    x, y = TANK_AT
    cylinder(g, (x, y, -0.07), (x, y, 0.105), 0.025, r["pipe"], segs=24)
    for z in (-0.07, 0.1):
        cylinder(g, (x, y, z - 0.008), (x, y, z + 0.008), 0.0275, r["collar"], segs=24)
    dome(g, (x, y, 0.108), (0, 0, 1), 0.016, r["collar"], segs=16, height=0.008)
    for z in (-0.02, 0.05):
        cylinder(g, (x, y, z - 0.004), (x, y, z + 0.004), 0.0268, r["frame"], segs=24)
        box(g, (x + 0.012, y, z), (0.01, 0.004, 0.004), r["frame"])
    return g


def elbow_path(points, radius=0.03, n=6):
    """A polyline with its corners rounded (quadratic arcs of about `radius`), as a pipe bends."""
    pts = [np.asarray(p, float) for p in points]
    out = [pts[0]]
    for a, b, c in zip(pts, pts[1:], pts[2:]):
        ab, bc = b - a, c - b
        ra = min(radius, np.linalg.norm(ab) / 2)
        rc = min(radius, np.linalg.norm(bc) / 2)
        p0 = b - ab / np.linalg.norm(ab) * ra
        p2 = b + bc / np.linalg.norm(bc) * rc
        for k in range(n + 1):
            t = k / n
            out.append((1 - t) ** 2 * p0 + 2 * (1 - t) * t * b + t ** 2 * p2)
    out.append(pts[-1])
    return out


def pipe_run(g, corners, rad, mat, collar, clamps=(), ends=True):
    """A pipe along a cornered path (each end goes into the housing through a collar), clamped at `clamps` (corner indices)."""
    sweep(g, elbow_path(corners), rad, mat, segs=12)
    pts = [Vector(p) for p in corners]
    for i in clamps:
        a, b = pts[i], pts[i + 1]
        t = (b - a).normalized()
        p = (a + b) / 2
        cylinder(g, p - t * 0.009, p + t * 0.009, rad * 1.4, collar, segs=8, phase=math.pi / 8)
    if ends:
        for a, b in ((pts[0], pts[1]), (pts[-1], pts[-2])):
            t = (b - a).normalized()
            cylinder(g, a, a + t * 0.012, rad * 1.45, collar, segs=8, phase=math.pi / 8)


def build_pipes(level):
    """The copper (steel at level 2) pipes on the faces between the rails; more of them each level."""
    r = ROLES[level]
    g = Geo()
    m, col = r["pipe"], r["collar"]
    sx_out = 0.232                                   # side-face pipes' centre line
    face = PLATE
    # left side (+X): a loop round both flanges, out of the face at the front top and back in at the back top
    pipe_run(g, [(face, 0.165, 0.17), (sx_out, 0.165, 0.17), (sx_out, 0.165, -0.168), (sx_out, -0.165, -0.168),
                 (sx_out, -0.165, 0.17), (face, -0.165, 0.17)], 0.0105, m, col, clamps=(1, 2, 3))
    # right side (-X): the front riser, and the tank's feed and return
    pipe_run(g, [(-face, 0.125, -0.15), (-sx_out, 0.125, -0.15), (-sx_out, 0.125, 0.15), (-face, 0.125, 0.15)], 0.0095, m, col, clamps=(1,))
    tx, ty = TANK_AT
    pipe_run(g, [(tx, ty, 0.112), (tx, ty, 0.17), (-sx_out, 0.0, 0.17), (-face, 0.0, 0.17)], 0.009, m, col, clamps=(1,))
    pipe_run(g, [(tx, ty, -0.075), (tx, ty, -0.168), (-sx_out, 0.09, -0.168), (-face, 0.09, -0.168)], 0.009, m, col, clamps=(1,))
    # top: one run beside the cap
    top = face + 0.0165
    pipe_run(g, [(0.15, -0.13, face), (0.15, -0.13, top), (0.15, 0.15, top), (0.15, 0.15, face)], 0.0095, m, col, clamps=(1,))
    if level >= 2:
        # a run down the back, and a valve on the left loop
        back = -face - 0.0165
        pipe_run(g, [(-0.15, -face, 0.17), (-0.15, back, 0.17), (-0.15, back, -0.17), (-0.15, -face, -0.17)], 0.0095, m, col, clamps=(1,))
        pipe_run(g, [(0.15, -face, 0.17), (0.15, back, 0.17), (0.0, back, 0.17), (0.0, -face, 0.17)], 0.0085, m, col)
        cylinder(g, (sx_out, 0.165, 0.0), (sx_out + 0.012, 0.165, 0.0), 0.005, col, segs=8)
        cylinder(g, (sx_out + 0.012, 0.165 - 0.012, 0.0), (sx_out + 0.012, 0.165 + 0.012, 0.0), 0.004, "rubber", segs=8)
    if level >= 3:
        # the dense network round the right flanges: thin pipes in arcs, with junctions on the riser
        thin, xo = 0.006, -0.237
        for (y, z), up in zip(FLANGES, (1, -1)):
            arc = [(xo, y + 0.083 * math.cos(a), z + up * 0.075 * math.sin(a)) for a in np.linspace(0.15, math.pi - 0.15, 9)]
            pipe_run(g, [(-face, arc[0][1], arc[0][2]), *arc, (-face, arc[-1][1], arc[-1][2])], thin, m, col, clamps=(1, 8))
        pipe_run(g, [(-face, 0.1, 0.0), (xo, 0.1, 0.0), (xo, -0.085, 0.0), (-face, -0.085, 0.0)], thin, m, col)
        # and on the left, a thin bypass across the loop
        pipe_run(g, [(face, 0.13, -0.005), (0.237, 0.13, -0.005), (0.237, -0.13, -0.005), (face, -0.13, -0.005)], thin, m, col, clamps=(1,))
    if level >= 4:
        # braided hoses: dark rubber, wavy
        for x, y in ((0.2395, -0.12), (-0.2395, -0.075)):
            pts = [(x, y + 0.006 * math.sin(k * 1.3), 0.15 - 0.3 * k / 18) for k in range(19)]
            sweep(g, pts, 0.0055, "rubber", segs=8)
            for p in (pts[0], pts[-1]):
                cylinder(g, (math.copysign(face, x), p[1], p[2]), p, 0.0075, col, segs=8)
    return g


# =====================================================================================================
# Glows (each its own node so it scales about its own centre)
# =====================================================================================================

def glow_box(half, mat="glow"):
    g = Geo()
    box(g, (0, 0, 0), half, mat)
    return g


def glow_disc(r, n, mat="glow", depth=0.002):
    g = Geo()
    cylinder(g, Vector(n) * -depth / 2, Vector(n) * depth / 2, r, mat, segs=24)
    return g


def glow_ring(r_in, r_out, axis_matrix, mat="glow"):
    g = Geo()
    local = Geo()
    ring(local, -0.001, 0.001, r_in, r_out, mat, segs=36)
    g.extend(local, axis_matrix)
    return g


def heat_glows(level):
    """(name, geometry, centre) of the glows the heat clip scales from cold to full."""
    out = []
    if level >= 2:
        for sx in (-1, 1):
            # behind the front vents' slats (level 4: big cores in the open cutouts)
            half = (0.028, 0.0004, 0.09)
            out.append((f"HeatVent{'L' if sx > 0 else 'R'}", glow_box(half, "glow_soft" if level < 4 else "glow"), (sx * VENT_X, PLATE + 0.0083, 0.0)))
    if level >= 3:
        for x in EXHAUSTS[level]:
            out.append((f"HeatExhaust{len(out)}", glow_disc(0.016, (0, 0, 1), "glow_soft", depth=0.001), (x, EXHAUST_Y, PLATE + 0.0188)))
    if level >= 4:
        # cores glowing through the flange holes
        for sx, side in ((1, "L"), (-1, "R")):
            for (y, z), tier in zip(FLANGES, "UL"):
                out.append((f"HeatCore{side}{tier}", glow_disc(0.039, (1, 0, 0), "glow", depth=0.001), (sx * (PLATE + 0.0037), y, z)))
    if level == 5:
        for sx, side in ((1, "L"), (-1, "R")):
            for (y, z), tier in zip(FLANGES, "UL"):
                out.append((f"HeatRing{side}{tier}", glow_ring(0.05, 0.054, Matrix.Rotation(math.pi / 2, 4, "Y"), "glow"), (sx * (PLATE + 0.0215), y, z)))
        out.append(("HeatCap", glow_ring(0.047, 0.0505, Matrix.Identity(4), "glow"), (0.0, 0.0, PLATE + 0.0135)))
        out.append(("HeatLabel", glow_box((0.0445, 0.0006, 0.0175), "glow_soft"), (LABEL_AT[0], LABEL_AT[1] - 0.0048, LABEL_AT[2])))
    return out


# =====================================================================================================
# Scene
# =====================================================================================================

def build_engine(level, mats, offset=(0, 0, 0)):
    rig = {"level": level}
    root = rig["root"] = bl.make_empty(f"GasEngine{level}")
    root.location = offset
    body = rig["body"] = bl.make_empty("Body", root)
    bl.make_object("Housing", build_housing(level), mats, body)
    bl.make_object("Panel", build_panel(level), mats, body)
    bl.make_object("Top", build_top(level), mats, body)
    stator = flange_stator(level, 1)
    stator.extend(flange_stator(level, -1))
    bl.make_object("Flanges", stator, mats, body)
    bl.make_object("Tank", build_tank(level), mats, body)
    rig["pipes"] = bl.make_object("Pipes", build_pipes(level), mats, body)
    rotor = flange_rotor(level)
    rig["rotors"] = []
    for sx, side in ((1, "L"), (-1, "R")):
        for (y, z), tier in zip(FLANGES, "UL"):
            placed = Geo()
            placed.extend(rotor, Matrix.Rotation(sx * math.pi / 2, 4, "Y"))
            ob = bl.make_object(f"Flange{side}{tier}", placed, mats, body, (sx * PLATE, y, z))
            rig["rotors"].append((ob, sx))
    rig["slider"] = bl.make_object("Slider", build_slider(level), mats, body, (0.0, SLIDER_Y, SLIDER_LOW))
    rig["lamp"] = bl.make_object("Lamp", glow_box((0.0088, 0.0012, 0.0048)), mats, body, (LAMP_AT[0], LAMP_AT[1] + 0.0016, LAMP_AT[2]))
    rig["flash"] = bl.make_object("LabelFlash", glow_box((0.0442, 0.001, 0.0172), "glow_soft"), mats, body,
                                  (LABEL_AT[0], LABEL_AT[1] + 0.0002, LABEL_AT[2]))
    rig["cores"] = []
    if level >= 4:
        for sx in (-1, 1):
            rig["cores"].append(bl.make_object(f"CoreFlash{'L' if sx > 0 else 'R'}", glow_box((0.0275, 0.0005, 0.089)), mats, body,
                                               (sx * VENT_X, PLATE + 0.0089, 0.0)))
    rig["heat"] = [bl.make_object(name, geo, mats, body, centre) for name, geo, centre in heat_glows(level)]
    for ob in [body, rig["pipes"], rig["slider"], rig["lamp"], rig["flash"], *rig["cores"], *rig["heat"], *(o for o, _ in rig["rotors"])]:
        ob.rotation_mode = "XYZ"
    apply_pose(rig, rest_pose())
    set_heat(rig, 0.0)
    return rig


# =====================================================================================================
# Clips
# =====================================================================================================

wobble, thumps, window = bbe.wobble, bbe.thumps, bbe.window


def rest_pose():
    return {"body": ((0, 0, 0), (0, 0, 0)), "pipes": 1.0, "spin": 0.0, "lamp": OFF, "flash": OFF, "core": OFF}


def running(t, hard, level, period=2.0, scale=1.0, seed=0):
    """Fine, fast vibration: 0.2 cm (0.4 cm in the hard loop), a touch more at the higher levels."""
    amp = (0.004 if hard else 0.002) * (1.0 + 0.06 * (level - 1)) * scale
    body = ((amp * wobble(seed + 1, t, period, (41, 53, 67)), amp * wobble(seed + 2, t, period, (37, 59, 71)),
             amp * wobble(seed + 3, t, period, (43, 61, 79))),
            (math.radians(0.18) * scale * wobble(seed + 4, t, period, (29, 47)), math.radians(0.14) * scale * wobble(seed + 5, t, period, (31, 49)), 0.0))
    return {"body": body, "lamp": 1.0, "pipes": 1.0}


def spin_turns(hard):
    return 5 if hard else 3


def clip_off(level):
    def pose(t):
        p = rest_pose()
        if level >= 3:
            p["pipes"] = 1.005 + 0.005 * math.sin(2 * math.pi * t / 4.0 - math.pi / 2)
        return p
    return 4.0, pose


def clip_run(level, hard):
    period = 2.0

    def pose(t):
        p = running(t, hard, level, period)
        p["spin"] = 2 * math.pi * spin_turns(hard) * t / period
        if level == 5 and hard:
            p["flash"] = 1.0 if (math.sin(t * 37) + math.sin(t * 23 + 1)) > -0.6 else 0.6
        return p
    return period, pose


def clip_start(level):
    length = 0.8

    def pose(t):
        ease = min(1.0, max(0.0, (t - 0.2) / 0.6))
        p = running(t, False, level, 2.0, scale=ease, seed=50)
        settle = 0.0075 * math.sin(math.pi * min(1.0, t / 0.35)) if t < 0.35 else 0.0
        knock = thumps(t, [(0.12, 1.0)], width=0.05)
        (dx, dy, dz), rot = p["body"]
        p["body"] = ((dx, dy, dz - settle + 0.002 * knock), (rot[0] + math.radians(0.5) * knock, rot[1], 0.0))
        omega = 2 * math.pi * spin_turns(False) / 2.0
        p["spin"] = omega * (t - 0.2) ** 2 / (2 * 0.6) if t > 0.2 else 0.0
        flash = math.exp(-max(0.0, t - 0.05) / 0.12) if t >= 0.05 else 0.0
        p["flash"] = max(OFF, flash)
        p["lamp"] = 1.0 if t > 0.05 and (t > 0.3 or math.sin(t * 60) > 0) else OFF
        if level >= 4:
            p["core"] = max(OFF, math.sin(math.pi * min(1.0, max(0.0, (t - 0.1) / 0.5))) ** 2)
        return p
    return length, pose


def clip_false_start(level):
    length = 0.6

    def pose(t):
        p = rest_pose()
        k = thumps(t, [(0.1, 1.0)], width=0.05)
        p["body"] = ((0.0, 0.0, -0.004 * k), (math.radians(0.4) * k, 0.0, 0.0))
        p["spin"] = math.radians(40) * math.sin(math.pi * min(1.0, t / 0.5))
        p["lamp"] = 1.0 if 0.05 < t < 0.3 and math.sin(t * 70) > 0 else OFF
        p["flash"] = max(OFF, 0.6 * math.exp(-max(0.0, t - 0.05) / 0.1)) if t >= 0.05 else OFF
        return p
    return length, pose


def clip_gear(level, hard_after):
    length = 0.4

    def pose(t):
        surge = 1.0 + 1.5 * math.exp(-t / 0.12)
        p = running(t, hard_after, level, 2.0, scale=surge, seed=70)
        p["spin"] = 2 * math.pi * spin_turns(hard_after) / 2.0 * t
        p["flash"] = max(OFF, math.exp(-t / 0.1))
        return p
    return length, pose


def clip_stop(level):
    length = 0.6

    def pose(t):
        fade = max(0.0, 1.0 - t / 0.45)
        p = running(t, False, level, 2.0, scale=fade, seed=90)
        k = thumps(t, [(0.47, 0.8)], width=0.04)
        (dx, dy, dz), rot = p["body"]
        p["body"] = ((dx, dy, dz - 0.003 * k), rot)
        omega = 2 * math.pi * spin_turns(False) / 2.0
        p["spin"] = omega * (t - t * t / (2 * 0.45)) if t < 0.45 else omega * 0.225
        p["lamp"] = 1.0 if t < 0.3 else (OFF if t > 0.45 or math.sin(t * 80) < 0 else 1.0)
        return p
    return length, pose


def apply_pose(rig, p):
    (loc, rot) = p["body"]
    rig["body"].location = loc
    rig["body"].rotation_euler = Euler(rot)
    s = p.get("pipes", 1.0)
    rig["pipes"].scale = (s, s, s)
    for ob, sx in rig["rotors"]:
        ob.rotation_euler = Euler((sx * p["spin"], 0.0, 0.0))
    for key in ("lamp", "flash"):
        v = p[key]
        rig[key].scale = (v, v, v)
    for ob in rig["cores"]:
        v = p.get("core", OFF)
        ob.scale = (v, v, v)


def set_slider(rig, fraction):
    rig["slider"].location = (0.0, SLIDER_Y, SLIDER_LOW + (SLIDER_HIGH - SLIDER_LOW) * fraction)


def set_heat(rig, h):
    v = max(OFF, h)
    for ob in rig["heat"]:
        ob.scale = (v, v, v)


def clip_objects(rig):
    return [rig["body"], rig["pipes"], *(o for o, _ in rig["rotors"]), rig["lamp"], rig["flash"], *rig["cores"]]


def bake(rig):
    """Keys every clip on every moving node (each clip keys them all), then the two scrubbed clips on their own nodes."""
    level = rig["level"]
    clips = {
        "off_loop": clip_off(level), "start": clip_start(level), "false_start": clip_false_start(level),
        "run_loop": clip_run(level, False), "run_hard_loop": clip_run(level, True), "gear": clip_gear(level, False),
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
    # scrubbed: the slider (lowest power at 0 s, highest at 1 s) and the heat glows (cold at 0 s, full at 1 s)
    slider = rig["slider"]
    slider.animation_data_create()
    act = bpy.data.actions.new(f"lever_Slider_{level}")
    slider.animation_data.action = act
    for f in (0, FPS):
        set_slider(rig, f / FPS)
        slider.keyframe_insert("location", frame=f)
    lt.linear_keys(act)
    bl.push_track(slider, act, "lever")
    for ob in rig["heat"]:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"heat_{ob.name}_{level}")
        ob.animation_data.action = act
        for f in range(0, FPS + 1, 4):
            h = f / FPS
            v = max(OFF, h ** 0.8)
            ob.scale = (v, v, v)
            ob.keyframe_insert("scale", frame=f)
        lt.linear_keys(act)
        bl.push_track(ob, act, "heat")
    for ob in [*objs, slider, *rig["heat"]]:
        ob.animation_data.use_nla = False
    apply_pose(rig, rest_pose())
    set_slider(rig, 0.5)
    set_heat(rig, 0.0)


def pose_for_icon(rig):
    """The still the icon shows: switched on, the slider in the middle, levels 3 to 5 warm (as the references)."""
    level = rig["level"]
    p = rest_pose()
    p["lamp"] = 1.0 if level >= 2 else OFF
    p["flash"] = 1.0 if level == 5 else OFF
    p["core"] = OFF
    apply_pose(rig, p)
    set_slider(rig, 0.5)
    set_heat(rig, {1: 0.0, 2: 0.45, 3: 0.55, 4: 1.0, 5: 1.0}[level])


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
        bbe.export(rig["root"], MESHES / f"gas_engine_{level}.glb", animations=True)
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
        bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / f"gas_engine_{rig['level']}_icon.png",
                        view=(-1.5, 2.0, 1.45), ortho=0.98, target=tuple(rig["root"].location))
        for o in others:
            for ob in bbe.mesh_tree(o["root"]):
                ob.hide_render = False
    for level in LEVELS:
        for p in (MESHES / f"gas_engine_{level}.glb", ICONS / f"gas_engine_{level}_icon.png"):
            print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_gas_engine
        render_gas_engine.render_all(rigs, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
