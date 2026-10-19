"""Build the Portable Fabricator, its clips and its icon (Portable Fabricator Design Spec of 2026-10-18, design 6 "With
Mini Manipulator").

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/portable_fabricator.glb`` - the Portable Fabricator, 4 x 3 x 3 blocks (1 m wide, 0.75 m
  deep, 0.75 m tall) centred on the origin, front (+Y in Blender, the game's -Z) with the teal FAB-1 panel. A chamfered
  gunmetal case with lighter top and bottom rims, corner bumpers, side clamps with copper bolts, a copper grille under
  the panel and a carry handle with copper elbows and a rubber grip on top. Behind the handle sit the fabrication pad
  (a framed teal hatch) and the mini manipulator: a three-joint arm on a copper turret at the back right, folded flat
  on the lid while idle.

  The moving pieces are their own nodes under ``Body`` (which settles and hums): the arm's ``ArmBase`` (turns about Z),
  ``ArmUpper``, ``ArmFore`` and ``ArmWrist`` (each pitches about its own Y), and the ``Glow*`` lights, which are shown
  by scaling their meshes up from inside their housings. glTF clips (the game plays them from the part's ``clip``
  ``station`` readout; see docs/part-data-schema.md):

  - ``idle_loop`` (3 s): the arm folded, the panel's indicator pulsing slowly, everything else dark.
  - ``start`` (0.8 s): the panel's frame flashes, the screen lights, the arm unfolds over the pad.
  - ``craft_loop`` (2 s): the arm works the pad in a small pattern with its tip sparking, a scan line sweeps the
    screen, the pad and the side strips flicker and the case hums.
  - ``complete`` (1.2 s): a bright flash on the panel and the pad, then the arm folds away and the lights go back to idle.

  The part's ``crafting.clips`` names them, so the station plays start then craft_loop when it begins work and complete
  when it stops; ``display.clips.idle`` plays idle_loop the rest of the time.

* ``mods/vanilla/textures/items/portable_fabricator_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256
  on a clear background, three-quarter from the front right, the arm raised over the pad.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/portable_fabricator/build_portable_fabricator.py                  # writes the .glb and the icon
    python tools/portable_fabricator/build_portable_fabricator.py --render OUT_DIR # also renders the review sheet

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
MESH = REPO / "mods" / "vanilla" / "meshes" / "parts" / "portable_fabricator.glb"
ICON = REPO / "mods" / "vanilla" / "textures" / "items" / "portable_fabricator_icon.png"
FPS = bbe.FPS

Geo, box, cylinder, hexnut, quad = bl.Geo, bl.box, bl.cylinder, bl.hexnut, bl.quad
lathe, disc = bbe.lathe, bbe.disc
glow_box, glow_disc = ge.glow_box, ge.glow_disc
OFF = ge.OFF

# ---- Dimensions (metres; the 1 x 0.75 x 0.75 m envelope centred on the origin, front +Y, the viewer's right -X) ----
HX, HY, HZ = 0.5, 0.375, 0.375                    # the envelope's half sizes
BOTTOM = -HZ
LID = 0.165                                       # the top of the case (the lid's face)
CASE_HX, CASE_HY = 0.43, 0.33                     # the case's rims
CORE_HX, CORE_HY = 0.415, 0.315                   # the darker middle band
MID_Z = (BOTTOM + LID) / 2
FRONT = CASE_HY                                   # the front rim's face
PANEL = (0.0, -0.085, 0.245, 0.15)                # teal panel: centre x, centre z, half width, half height
SCREEN = (0.0, -0.14, 0.058, 0.05)                # the black screen in the panel: centre x, centre z, half w, half h
LAMP_AT = (-0.188, -0.2)                          # the indicator (x, z) at the panel's lower right
HANDLE_Y = 0.12
HANDLE_X = 0.29                                   # the posts' x either side
GRIP_Z = 0.335
PAD = (0.0, -0.12, 0.11, 0.085)                  # fabrication pad on the lid: centre x, centre y, half w, half d
ARM_AT = (-0.33, -0.235)                          # the arm's turret (x, y) at the back right
TURRET_H = 0.03
SHOULDER_H = 0.044                                # the shoulder axis above the lid
UPPER, FORE, TOOL = 0.2, 0.17, 0.08               # segment lengths: shoulder-elbow, elbow-wrist, wrist-tip
FOLD_OFFSET = 0.042                               # the fore link and the tool sit beside the upper link, so it folds flat


# =====================================================================================================
# Materials
# =====================================================================================================

# block letters in a unit box (u to the right, v down), drawn as lines: no system font, so the same everywhere
STROKES = {
    "F": [[(1, 0), (0, 0), (0, 1)], [(0, 0.48), (0.75, 0.48)]],
    "A": [[(0, 1), (0, 0.3), (0.3, 0), (0.7, 0), (1, 0.3), (1, 1)], [(0, 0.55), (1, 0.55)]],
    "B": [[(0, 0), (0.72, 0), (0.95, 0.12), (0.95, 0.36), (0.72, 0.48), (0, 0.48)],
          [(0.72, 0.48), (1, 0.62), (1, 0.88), (0.76, 1), (0, 1), (0, 0)]],
    "-": [[(0.1, 0.52), (0.9, 0.52)]],
    "1": [[(0.15, 0.22), (0.55, 0), (0.55, 1)], [(0.15, 1), (0.95, 1)]],
    "P": [[(0, 1), (0, 0), (1, 0), (1, 0.5), (0, 0.5)]],
    "O": [[(0.25, 0), (0.75, 0), (1, 0.25), (1, 0.75), (0.75, 1), (0.25, 1), (0, 0.75), (0, 0.25), (0.25, 0)]],
    "R": [[(0, 1), (0, 0), (1, 0), (1, 0.5), (0, 0.5)], [(0.45, 0.5), (1, 1)]],
    "T": [[(0, 0), (1, 0)], [(0.5, 0), (0.5, 1)]],
    "L": [[(0, 0), (0, 1), (1, 1)]],
    "E": [[(1, 0), (0, 0), (0, 1), (1, 1)], [(0, 0.5), (0.8, 0.5)]],
    "I": [[(0.5, 0), (0.5, 1)], [(0.2, 0), (0.8, 0)], [(0.2, 1), (0.8, 1)]],
    "C": [[(1, 0), (0.25, 0), (0, 0.25), (0, 0.75), (0.25, 1), (1, 1)]],
}


def lettering(d, text, x0, top, height, width, gap, ink, stroke):
    for ch in text:
        if ch == " ":
            x0 += width * 0.7
            continue
        for line in STROKES[ch]:
            d.line([(x0 + u * width, top + v * height) for u, v in line], fill=ink, width=stroke, joint="curve")
        x0 += width + gap
    return x0


def text_width(text, width, gap):
    return sum(width * 0.7 if ch == " " else width + gap for ch in text) - gap


def panel_face():
    """The teal panel: FAB-1 over PORTABLE FABRICATOR, vent slits at the lower left, a gear mark at the lower right, a
    thin inset border. The screen and the indicator are their own meshes on top of it."""
    w, h = 1024, 627
    teal = (24, 118, 122)
    im = Image.new("RGB", (w, h), teal)
    d = ImageDraw.Draw(im)
    rng = np.random.default_rng(5)
    px = np.asarray(im, float)
    px *= (1 + 0.035 * rng.standard_normal((h, w, 1))).clip(0.9, 1.1)
    im = Image.fromarray(px.clip(0, 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    d.rectangle([14, 14, w - 15, h - 15], outline=(16, 86, 90), width=6)
    d.rectangle([24, 24, w - 25, h - 25], outline=(60, 160, 162), width=2)
    ink = (226, 240, 238)
    big_w, big_gap = 92, 26
    x0 = (w - text_width("FAB-1", big_w, big_gap)) / 2
    lettering(d, "FAB-1", x0, 70, 132, big_w, big_gap, ink, 22)
    small_w, small_gap = 25, 9
    x0 = (w - text_width("PORTABLE FABRICATOR", small_w, small_gap)) / 2
    lettering(d, "PORTABLE FABRICATOR", x0, 236, 34, small_w, small_gap, ink, 6)
    for k in range(5):                                                  # vent slits, lower left
        y = 380 + k * 26
        d.rounded_rectangle([110, y, 205, y + 11], radius=5, fill=(10, 52, 56))
    gx, gy, gr = 845, 430, 38                                            # gear mark, lower right
    for k in range(8):
        a = 2 * math.pi * k / 8
        d.line([(gx + gr * 0.7 * math.cos(a), gy + gr * 0.7 * math.sin(a)),
                (gx + gr * math.cos(a), gy + gr * math.sin(a))], fill=(14, 66, 70), width=14)
    d.ellipse([gx - gr * 0.75, gy - gr * 0.75, gx + gr * 0.75, gy + gr * 0.75], outline=(14, 66, 70), width=12)
    return np.asarray(im, float) / 255.0


def side_mark():
    """The triangle mark on each side plate: a pale teal triangle with a dark bar, on gunmetal."""
    s = 128
    im = Image.new("RGB", (s, s), (62, 66, 72))
    d = ImageDraw.Draw(im)
    d.polygon([(64, 14), (118, 110), (10, 110)], fill=(70, 170, 168))
    d.polygon([(64, 30), (104, 102), (24, 102)], fill=(38, 42, 47))
    d.rectangle([59, 52, 69, 82], fill=(70, 170, 168))
    d.rectangle([59, 88, 69, 96], fill=(70, 170, 168))
    return np.asarray(im, float) / 255.0


def emissive_decal(name, rgb, strength):
    mat = lt.decal(name, rgb, rough=0.35)
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    image = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    nt.links.new(image.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def make_materials():
    half = ge.half_res
    return {
        "hole": pt.plain("pf_hole", "#0b0c0e", 0.9, 0.2),
        "slot": pt.plain("pf_slot", "#141518", 0.7, 0.4),
        "screen": pt.plain("pf_screen", "#0d1416", 0.18, 0.1),
        "lamp_off": pt.plain("pf_lamp_off", "#174b4c", 0.3, 0.0),
        "teal": pt.plain("pf_teal", "#1d7f82", 0.38, 0.15),
        "glow": pt.plain("pf_glow", "#5ff6e6", 0.3, 0.0, emission="#2ef2dc", strength=4.0),
        "glow_soft": pt.plain("pf_glow_soft", "#4fe0d4", 0.3, 0.0, emission="#22d8c8", strength=2.0),
        "spark": pt.plain("pf_spark", "#ffffff", 0.2, 0.0, emission="#d8fffb", strength=12.0),
        "panel": emissive_decal("pf_panel", panel_face(), 0.12),
        "mark": lt.decal("pf_mark", side_mark(), rough=0.5),
        "gun": lt.textured("pf_gun", half(lt.metal_surface(501, "#40444a", 0.42, 0.75, wear=0.25, edge_light=0.25)),
                           normal_strength=0.25),
        "gun_dark": lt.textured("pf_gun_dark", half(lt.metal_surface(503, "#272a2e", 0.48, 0.72, wear=0.2,
                                                                     edge_light=0.2)), normal_strength=0.25),
        "steel_dark": lt.textured("pf_steel_dark", half(lt.metal_surface(505, "#5d6166", 0.36, 0.86, wear=0.3,
                                                                         edge_light=0.25)), normal_strength=0.4),
        "copper": lt.textured("pf_copper", half(lt.metal_surface(507, "#9c5c35", 0.32, 0.95, wear=0.3, edge_light=0.3)),
                              normal_strength=0.3),
        "rubber": lt.textured("pf_rubber", half(lt.rubber_surface(509)), normal_strength=1.5),
    }


# =====================================================================================================
# Case
# =====================================================================================================

def slats(g, centre, half, n, mat, axis_out=1, sign=1):
    """n horizontal slats across a framed recess on the face whose normal is axis_out (sign outward)."""
    c = list(centre)
    h = list(half)
    h[axis_out] = 0.003
    box(g, c, h, "steel_dark", bevel=0.003)
    inner = list(c)
    inner[axis_out] += sign * 0.0032
    hi = [v - 0.006 for v in half]
    hi[axis_out] = 0.0004
    box(g, inner, hi, "hole")
    for k in range(n):
        s = list(c)
        s[axis_out] += sign * 0.0045
        s[2] += (k - (n - 1) / 2) * (2 * half[2] - 0.014) / max(1, n - 1)
        sh = [v - 0.008 for v in half]
        sh[axis_out] = 0.0035
        sh[2] = 0.0028
        box(g, s, sh, mat, bevel=0.001)


def build_case():
    g = Geo()
    # the darker middle band and the lighter rims above and below it
    box(g, (0, 0, MID_Z), (CORE_HX, CORE_HY, (LID - BOTTOM) / 2 - 0.004), "gun_dark", bevel=0.03)
    box(g, (0, 0, LID - 0.05), (CASE_HX, CASE_HY, 0.05), "gun", bevel=0.032)
    box(g, (0, 0, BOTTOM + 0.055), (CASE_HX, CASE_HY, 0.055), "gun", bevel=0.032)
    for z in (LID - 0.1, BOTTOM + 0.11):                                # dark seams where the rims meet the band
        box(g, (0, 0, z), (CASE_HX - 0.012, CASE_HY - 0.012, 0.003), "hole")
    # the lid: an inset plate with a seam round it
    box(g, (0, 0, LID + 0.002), (CASE_HX - 0.045, CASE_HY - 0.045, 0.003), "gun_dark", bevel=0.003)
    # corner bumpers: chamfered posts on the four upright edges, bolted top and bottom
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = (sx * (CASE_HX - 0.022), sy * (CASE_HY - 0.022), MID_Z)
            box(g, c, (0.04, 0.04, (LID - BOTTOM) / 2 - 0.02), "gun", bevel=0.018)
            for z in (LID - 0.06, BOTTOM + 0.06):
                hexnut(g, (c[0] + sx * 0.04, c[1], z), (sx, 0, 0), 0.008, 0.004, "copper")
                hexnut(g, (c[0], c[1] + sy * 0.04, z), (0, sy, 0), 0.008, 0.004, "copper")
    # rubber feet
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * 0.34, sy * 0.24, BOTTOM + 0.006), (0.05, 0.035, 0.006), "rubber", bevel=0.003)
    return g


def build_sides():
    """Each side (+X left, -X right): a chamfered plate with the triangle mark, a clamp block with copper bolts and a
    latch at the front and the back corners, a teal accent strip low at the back."""
    g = Geo()
    for sx in (-1, 1):
        face = sx * CASE_HX
        # the big side plate (octagonal: a box with heavy bevels) and its mark
        box(g, (face + sx * 0.004, -0.02, MID_Z), (0.006, 0.17, 0.15), "gun", bevel=0.005)
        box(g, (face + sx * 0.0105, -0.02, MID_Z), (0.0012, 0.15, 0.13), "gun_dark")
        mx = face + sx * 0.0125
        ys, zs = (-0.02 - 0.04, -0.02 + 0.04), (MID_Z - 0.04, MID_Z + 0.04)
        y0, y1 = (ys[1], ys[0]) if sx > 0 else ys
        quad(g, [(mx, y0, zs[0]), (mx, y1, zs[0]), (mx, y1, zs[1]), (mx, y0, zs[1])], "mark")
        for y in (-0.17, 0.13):
            for z in (MID_Z - 0.13, MID_Z + 0.13):
                hexnut(g, (face + sx * 0.01, y, z), (sx, 0, 0), 0.006, 0.003, "steel_dark")
        # clamp blocks at the front and back of each side, each with two copper bolts and a copper latch plate
        for sy in (-1, 1):
            cy = sy * 0.245
            box(g, (sx * (CASE_HX + 0.03), cy, MID_Z + 0.02), (0.03, 0.05, 0.1), "gun", bevel=0.012)
            box(g, (sx * (CASE_HX + 0.062), cy, MID_Z + 0.02), (0.004, 0.032, 0.07), "copper", bevel=0.003)
            for z in (MID_Z + 0.075, MID_Z - 0.035):
                hexnut(g, (sx * (CASE_HX + 0.066), cy, z), (sx, 0, 0), 0.009, 0.003, "copper")
        # the teal accent strip low at the back (its glow sits on top of it)
        box(g, (face + sx * 0.003, -0.2, BOTTOM + 0.165), (0.003, 0.07, 0.009), "steel_dark")
        box(g, (face + sx * 0.0062, -0.2, BOTTOM + 0.165), (0.0004, 0.064, 0.0055), "teal")
    return g


def build_front():
    """The panel's bezel and teal face, the screen's frame, the indicator socket, the copper grille under the panel."""
    g = Geo()
    y = FRONT
    px, pz, hw, hh = PANEL
    box(g, (px, y + 0.006, pz), (hw + 0.022, 0.007, hh + 0.022), "gun", bevel=0.008)
    box(g, (px, y + 0.0125, pz), (hw + 0.004, 0.0005, hh + 0.004), "hole")
    fy = y + 0.0145                                                   # 1.5 mm proud of the hole: no depth fighting
    quad(g, [(px + hw, fy, pz - hh), (px - hw, fy, pz - hh), (px - hw, fy, pz + hh), (px + hw, fy, pz + hh)], "panel")
    for x, z in ((px + hw + 0.011, pz + hh + 0.011), (px - hw - 0.011, pz + hh + 0.011),
                 (px + hw + 0.011, pz - hh - 0.011), (px - hw - 0.011, pz - hh - 0.011)):
        hexnut(g, (x, y + 0.013, z), (0, 1, 0), 0.0055, 0.003, "steel_dark")
    # the screen: a raised black frame, the dark glass
    sx, sz, sw, sh = SCREEN
    box(g, (sx, fy + 0.006, sz), (sw + 0.014, 0.006, sh + 0.014), "slot", bevel=0.005)
    box(g, (sx, fy + 0.0122, sz), (sw, 0.0004, sh), "screen")
    # a small button under the screen's right corner, and the indicator's socket
    box(g, (sx - sw + 0.004, fy + 0.002, sz - sh - 0.03), (0.012, 0.002, 0.008), "slot", bevel=0.0015)
    lx, lz = LAMP_AT
    cylinder(g, (lx, fy, lz), (lx, fy + 0.004, lz), 0.016, "gun", segs=24)
    disc(g, (lx, fy + 0.0042, lz), (0, 1, 0), 0.011, "lamp_off", segs=24)
    # the copper grille under the panel, in its frame
    gz = BOTTOM + 0.07
    box(g, (0, y + 0.005, gz), (0.135, 0.006, 0.03), "gun", bevel=0.005)
    slats(g, (0, y + 0.011, gz), (0.12, 0.003, 0.02), 5, "copper")
    return g


def build_back():
    g = Geo()
    y = -FRONT
    for x in (-0.2, 0.2):
        box(g, (x, y - 0.004, MID_Z), (0.12, 0.006, 0.12), "gun", bevel=0.006)
        slats(g, (x, y - 0.01, MID_Z), (0.1, 0.003, 0.1), 9, "steel_dark", sign=-1)
    box(g, (0, y - 0.004, BOTTOM + 0.07), (0.06, 0.005, 0.025), "steel_dark", bevel=0.004)
    return g


def build_handle():
    """Two posts with copper elbows and a rubber-gripped bar along X."""
    g = Geo()
    for sx in (-1, 1):
        x = sx * HANDLE_X
        box(g, (x, HANDLE_Y, LID + 0.012), (0.04, 0.032, 0.012), "gun", bevel=0.006)          # foot plate
        for dy in (-0.02, 0.02):
            hexnut(g, (x + sx * 0.024, HANDLE_Y + dy, LID + 0.024), (0, 0, 1), 0.005, 0.003, "steel_dark")
        box(g, (x, HANDLE_Y, LID + 0.075), (0.017, 0.017, 0.06), "gun_dark", bevel=0.006)    # post
        box(g, (x, HANDLE_Y, GRIP_Z - 0.004), (0.026, 0.026, 0.03), "copper", bevel=0.01)    # copper elbow
        box(g, (x - sx * 0.032, HANDLE_Y, GRIP_Z), (0.012, 0.02, 0.02), "copper", bevel=0.006)
    cylinder(g, (HANDLE_X - 0.04, HANDLE_Y, GRIP_Z), (-HANDLE_X + 0.04, HANDLE_Y, GRIP_Z), 0.019, "gun_dark", segs=20)
    cylinder(g, (HANDLE_X - 0.07, HANDLE_Y, GRIP_Z), (-HANDLE_X + 0.07, HANDLE_Y, GRIP_Z), 0.023, "rubber", segs=20)
    for k in range(14):                                                 # grip ridges
        x = HANDLE_X - 0.08 - k * (2 * HANDLE_X - 0.16) / 13
        cylinder(g, (x - 0.003, HANDLE_Y, GRIP_Z), (x + 0.003, HANDLE_Y, GRIP_Z), 0.0245, "rubber", segs=20)
    return g


def build_pad():
    """The fabrication pad on the lid: a gunmetal frame round a dark grid; its teal glow sits on top."""
    g = Geo()
    cx, cy, hw, hd = PAD
    box(g, (cx, cy, LID + 0.008), (hw + 0.016, hd + 0.016, 0.008), "gun", bevel=0.005)
    box(g, (cx, cy, LID + 0.0162), (hw, hd, 0.0004), "teal")
    for k in range(7):
        x = cx - hw + (k + 1) * 2 * hw / 8
        box(g, (x, cy, LID + 0.017), (0.0018, hd, 0.0006), "hole")
    for k in range(5):
        y = cy - hd + (k + 1) * 2 * hd / 6
        box(g, (cx, y, LID + 0.017), (hw, 0.0018, 0.0006), "hole")
    for sx in (-1, 1):
        for sy in (-1, 1):
            hexnut(g, (cx + sx * (hw + 0.008), cy + sy * (hd + 0.008), LID + 0.016), (0, 0, 1), 0.005, 0.003, "copper")
    return g


def build_turret():
    """The arm's fixed base on the lid: a copper ring on a bolted gunmetal plate."""
    g = Geo()
    x, y = ARM_AT
    box(g, (x, y, LID + 0.006), (0.055, 0.055, 0.006), "gun", bevel=0.005)
    for dx in (-1, 1):
        for dy in (-1, 1):
            hexnut(g, (x + dx * 0.042, y + dy * 0.042, LID + 0.012), (0, 0, 1), 0.005, 0.003, "steel_dark")
    lathe(g, [(0.0, LID + 0.012), (0.042, LID + 0.012), (0.042, LID + TURRET_H), (0.0, LID + TURRET_H)], "copper",
          steps=32, center=(x, y))
    return g


# ---- the arm: each piece about its own pivot, the arm pointing along +X ----------------------------------------

def joint(g, at, r, width, mat="copper"):
    """A joint drum on the Y axis through `at`, with steel caps."""
    x, y, z = at
    cylinder(g, (x, y - width, z), (x, y + width, z), r, mat, segs=24)
    for s in (-1, 1):
        cylinder(g, (x, y + s * width, z), (x, y + s * (width + 0.003), z), r * 0.6, "steel_dark", segs=16)


def build_arm_base():
    """Turns about Z on the turret: a swivel drum with two cheeks holding the shoulder."""
    g = Geo()
    lathe(g, [(0.0, 0.0), (0.036, 0.0), (0.036, 0.018), (0.03, 0.024), (0.0, 0.024)], "gun_dark", steps=28)
    for sy in (-1, 1):
        box(g, (0.0, sy * 0.03, SHOULDER_H - 0.016), (0.028, 0.007, 0.034), "gun", bevel=0.005)
    return g


def link(g, length, half_w, half_h, mat="gun"):
    """An arm segment from its pivot along +X: a chamfered beam with a dark slot down its side."""
    box(g, (length / 2, 0, 0), (length / 2 - 0.006, half_w, half_h), mat, bevel=0.006)
    for sy in (-1, 1):
        box(g, (length / 2, sy * (half_w + 0.0004), 0), (length / 2 - 0.03, 0.0006, half_h * 0.35), "hole")


def build_upper():
    g = Geo()
    joint(g, (0, 0, 0), 0.03, 0.022)
    link(g, UPPER, 0.017, 0.019)
    joint(g, (UPPER, FOLD_OFFSET / 2, 0), 0.026, 0.017 + FOLD_OFFSET / 2)
    return g


def build_fore():
    g = Geo()
    link(g, FORE, 0.014, 0.016, "gun_dark")
    cylinder(g, (0.035, 0.016, 0.014), (FORE - 0.035, 0.016, 0.014), 0.005, "copper", segs=10)   # a copper line
    joint(g, (FORE, 0, 0), 0.021, 0.017)
    return g


def build_wrist():
    """The tool head: a small housing and a needle nozzle along +X."""
    g = Geo()
    box(g, (0.027, 0, 0), (0.025, 0.017, 0.016), "gun", bevel=0.005)
    cylinder(g, (0.052, 0, 0), (0.064, 0, 0), 0.011, "steel_dark", segs=16)
    cylinder(g, (0.064, 0, 0), (TOOL - 0.004, 0, 0), 0.007, "copper", segs=16, r1=0.0025)
    cylinder(g, (TOOL - 0.004, 0, 0), (TOOL, 0, 0), 0.0025, "steel_dark", segs=10, r1=0.001)
    return g


# =====================================================================================================
# Scene
# =====================================================================================================

def build_fabricator(mats):
    rig = {}
    root = rig["root"] = bl.make_empty("PortableFabricator")
    body = rig["body"] = bl.make_empty("Body", root)
    for name, geo in (("Case", build_case()), ("Sides", build_sides()), ("Front", build_front()), ("Back", build_back()),
                      ("Handle", build_handle()), ("Pad", build_pad()), ("Turret", build_turret())):
        bl.make_object(name, geo, mats, body)
    x, y = ARM_AT
    base = rig["base"] = bl.make_object("ArmBase", build_arm_base(), mats, body, (x, y, LID + TURRET_H))
    upper = rig["upper"] = bl.make_object("ArmUpper", build_upper(), mats, base, (0, 0, SHOULDER_H - TURRET_H + 0.006))
    fore = rig["fore"] = bl.make_object("ArmFore", build_fore(), mats, upper, (UPPER, FOLD_OFFSET, 0))
    wrist = rig["wrist"] = bl.make_object("ArmWrist", build_wrist(), mats, fore, (FORE, 0, 0))
    fy = FRONT + 0.0145
    sx, sz, sw, sh = SCREEN
    px, pz, hw, hh = PANEL
    cx, cy, pw, pd = PAD
    rig["lamp"] = bl.make_object("GlowLamp", glow_disc(0.0098, (0, 1, 0), "glow", depth=0.001), mats, body,
                                 (LAMP_AT[0], fy + 0.0048, LAMP_AT[1]))
    rig["screen"] = bl.make_object("GlowScreen", glow_box((sw - 0.002, 0.0008, sh - 0.002), "glow_soft"), mats, body,
                                   (sx, fy + 0.0128, sz))
    rig["scan"] = bl.make_object("GlowScan", glow_box((sw - 0.004, 0.0006, 0.0025), "glow"), mats, body,
                                 (sx, fy + 0.0142, sz))
    frame = Geo()
    e = 0.009
    for c, half in (((0, 0, hh + e), (hw + e, 0.0011, 0.0026)), ((0, 0, -hh - e), (hw + e, 0.0011, 0.0026)),
                    ((hw + e, 0, 0), (0.0026, 0.0011, hh + e)), ((-hw - e, 0, 0), (0.0026, 0.0011, hh + e))):
        box(frame, c, half, "glow")
    rig["frame"] = bl.make_object("GlowFrame", frame, mats, body, (px, FRONT + 0.0126, pz))
    rig["flash"] = bl.make_object("GlowFlash", glow_box((sw - 0.002, 0.0008, sh - 0.002), "spark"), mats, body,
                                  (sx, fy + 0.0118, sz))
    rig["pad"] = bl.make_object("GlowPad", glow_box((pw - 0.004, pd - 0.004, 0.0004), "glow_soft"), mats, body,
                                (cx, cy, LID + 0.0168))
    strips = Geo()
    for s in (-1, 1):
        box(strips, (s * (CASE_HX + 0.0068), 0, 0), (0.0005, 0.062, 0.005), "glow")
    rig["strips"] = bl.make_object("GlowStrips", strips, mats, body, (0, -0.2, BOTTOM + 0.165))
    rig["spark"] = bl.make_object("GlowSpark", glow_disc(0.009, (1, 0, 0), "spark", depth=0.006), mats, wrist,
                                  (TOOL + 0.002, 0, 0))
    for ob in (body, base, upper, fore, wrist, rig["lamp"], rig["screen"], rig["scan"], rig["frame"], rig["flash"], rig["pad"],
               rig["strips"], rig["spark"]):
        ob.rotation_mode = "XYZ"
    apply_pose(rig, rest_pose())
    return rig


# =====================================================================================================
# Arm poses
# =====================================================================================================

FOLDED = {"yaw": math.radians(4), "upper": math.radians(-4), "fore": math.radians(-172), "wrist": math.radians(-8)}


def reach(tip):
    """Joint angles that put the tool's tip at `tip` (world, Body at rest) with the tool pointing straight down."""
    x, y = ARM_AT
    shoulder = Vector((x, y, LID + SHOULDER_H + 0.006))
    d = Vector(tip) - shoulder
    flat = math.hypot(d.x, d.y)
    r = math.sqrt(max(1e-6, flat ** 2 - FOLD_OFFSET ** 2))            # the arm's plane sits FOLD_OFFSET to its left
    yaw = math.atan2(d.y, d.x) - math.atan2(FOLD_OFFSET, r)
    h = d.z + TOOL                                                     # the wrist sits TOOL above the tip
    dist = math.hypot(r, h)
    dist = min(dist, UPPER + FORE - 1e-4)
    cos_e = (UPPER ** 2 + FORE ** 2 - dist ** 2) / (2 * UPPER * FORE)
    elbow = math.pi - math.acos(max(-1.0, min(1.0, cos_e)))            # the bend between the two links
    a = math.atan2(h, r) + math.atan2(FORE * math.sin(elbow), UPPER + FORE * math.cos(elbow))
    upper = a                                                          # elevation of the upper link
    fore = -elbow                                                      # relative pitch of the fore link (bends down)
    wrist = -math.pi / 2 - (upper + fore)                              # the tool points down
    return {"yaw": yaw, "upper": upper, "fore": fore, "wrist": wrist}


def blend(a, b, t):
    return {k: a[k] + (b[k] - a[k]) * t for k in a}


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def work_tip(t, period=2.0):
    """The tool tip's path over the pad during the craft loop: a slow figure eight with short dabs down to the grid."""
    cx, cy, hw, hd = PAD
    u = 2 * math.pi * t / period
    x = cx + 0.025 + 0.5 * hw * math.sin(u)
    y = cy + 0.5 * hd * math.sin(2 * u)
    dab = max(0.0, math.sin(4 * u)) ** 3
    return (x, y, LID + 0.019 + 0.016 - 0.014 * dab), dab


def rest_pose():
    return {"body": ((0, 0, 0), (0, 0, 0)), "arm": dict(FOLDED), "lamp": 0.45, "screen": OFF, "scan": None,
            "frame": OFF, "flash": OFF, "pad": OFF, "strips": 0.25, "spark": OFF}


def apply_pose(rig, p):
    loc, rot = p["body"]
    rig["body"].location = loc
    rig["body"].rotation_euler = Euler(rot)
    arm = p["arm"]
    rig["base"].rotation_euler = Euler((0, 0, arm["yaw"]))
    rig["upper"].rotation_euler = Euler((0, -arm["upper"], 0))
    rig["fore"].rotation_euler = Euler((0, -arm["fore"], 0))
    rig["wrist"].rotation_euler = Euler((0, -arm["wrist"], 0))
    rig["lamp"].scale = (max(OFF, p["lamp"]),) * 3
    rig["screen"].scale = (1.0, 1.0, max(OFF, p["screen"]))
    sx, sz, sw, sh = SCREEN
    scan = p["scan"]
    rig["scan"].scale = (OFF,) * 3 if scan is None else (1.0, 1.0, 1.0)
    rig["scan"].location.z = sz + (0.0 if scan is None else (sh - 0.006) * (1 - 2 * scan))
    rig["frame"].scale = (1.0, max(OFF, p["frame"]), 1.0)              # pushed out of the bezel when lit
    rig["flash"].scale = (1.0, max(OFF, 3.0 * p["flash"]), 1.0)      # out in front of the lit screen above 0.6
    rig["pad"].scale = (max(OFF, p["pad"]),) * 3
    rig["strips"].scale = (1.0, max(OFF, p["strips"]), max(OFF, p["strips"]))
    rig["spark"].scale = (max(OFF, p["spark"]),) * 3


def hum(t, period, amp, seed):
    w = bbe.wobble
    return ((amp * w(seed + 1, t, period, (41, 59, 73)), amp * w(seed + 2, t, period, (43, 61, 79)),
             amp * 0.6 * (1 + w(seed + 3, t, period, (47, 67, 83))) / 2),
            (0.0, 0.0, math.radians(0.05) * w(seed + 4, t, period, (37, 53))))


def clip_idle():
    def pose(t):
        p = rest_pose()
        p["lamp"] = 0.4 + 0.45 * (0.5 - 0.5 * math.cos(2 * math.pi * t / 3.0))
        p["strips"] = 0.2 + 0.1 * (0.5 - 0.5 * math.cos(2 * math.pi * t / 3.0))
        return p
    return 3.0, pose


def crafting(t, period=2.0, scale=1.0):
    """The working state at time t of the craft loop."""
    tip, dab = work_tip(t, period)
    p = rest_pose()
    p["body"] = hum(t, period, 0.0007 * scale, 10)
    p["arm"] = reach(tip)
    p["lamp"] = 1.0
    p["screen"] = 1.0
    p["scan"] = (t % (period / 2)) / (period / 2)
    p["frame"] = 1.0
    p["pad"] = 1.0
    p["strips"] = 0.7 + 0.3 * abs(bbe.wobble(20, t, period, (13, 29)))
    flick = 0.5 + 0.5 * math.sin(t * 97.0) * math.sin(t * 61.0)
    p["spark"] = dab * (0.6 + 0.6 * flick) if dab > 0.15 else OFF
    return p


def clip_craft():
    return 2.0, lambda t: crafting(t)


def clip_start(length=0.8):
    work0 = crafting(0.0)

    def pose(t):
        p = rest_pose()
        k = smooth((t - 0.1) / 0.6)
        lift = math.sin(math.pi * k) * 0.15                             # the arm lifts as it swings round
        arm = blend(FOLDED, work0["arm"], k)
        arm["upper"] += lift
        arm["fore"] -= lift * 0.4
        p["arm"] = arm
        p["frame"] = 1.0 if t > 0.05 else OFF
        p["flash"] = max(OFF, math.exp(-max(0.0, t - 0.05) / 0.1)) if t > 0.05 else OFF
        p["screen"] = max(OFF, smooth((t - 0.05) / 0.3))
        p["lamp"] = 1.0 if t > 0.03 else 0.45
        p["pad"] = max(OFF, k * work0["pad"])
        p["strips"] = 0.25 + (work0["strips"] - 0.25) * k
        p["body"] = hum(t, 2.0, 0.0007 * k, 10)
        if t > 0.7:
            p["scan"] = 0.0
        return p
    return length, pose


def clip_complete(length=1.2):
    work0 = crafting(0.0)

    def pose(t):
        p = rest_pose()
        flash = math.exp(-max(0.0, t) / 0.12)
        k = smooth((t - 0.3) / 0.7)
        lift = math.sin(math.pi * k) * 0.15
        arm = blend(work0["arm"], FOLDED, k)
        arm["upper"] += lift
        arm["fore"] -= lift * 0.4
        p["arm"] = arm
        p["frame"] = 1.0 if t < 0.8 else OFF
        p["flash"] = max(OFF, flash)
        p["screen"] = max(OFF, 1.0 - smooth((t - 0.6) / 0.4))
        p["pad"] = max(OFF, (1.0 if t < 0.25 else max(0.0, 1.0 - (t - 0.25) / 0.5)))
        p["lamp"] = 1.0 if t < 0.9 else 0.45 + 0.55 * (1 - smooth((t - 0.9) / 0.3))
        p["strips"] = work0["strips"] + (0.25 - work0["strips"]) * k
        p["spark"] = max(OFF, 1.4 * flash) if t < 0.3 else OFF          # the finishing flash at the tip
        p["body"] = hum(t, 2.0, 0.0007 * (1 - k), 10)
        return p
    return length, pose


# =====================================================================================================
# Bake
# =====================================================================================================

def clip_objects(rig):
    return [rig["body"], rig["base"], rig["upper"], rig["fore"], rig["wrist"], rig["lamp"], rig["screen"], rig["scan"],
            rig["frame"], rig["flash"], rig["pad"], rig["strips"], rig["spark"]]


CLIPS = {"idle_loop": clip_idle, "start": clip_start, "craft_loop": clip_craft, "complete": clip_complete}


def pose_at(name, t):
    length, pose = CLIPS[name]()
    p = rest_pose()
    p.update(pose(t))
    return p


def bake(rig):
    objs = clip_objects(rig)
    for ob in objs:
        ob.animation_data_create()
    for name, make in CLIPS.items():
        length, _ = make()
        actions = {}
        for ob in objs:
            act = bpy.data.actions.new(f"{name}_{ob.name}")
            ob.animation_data.action = act
            actions[ob.name] = act
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            loop_end = name.endswith("_loop") and f == frames
            apply_pose(rig, pose_at(name, 0.0 if loop_end else f / FPS))
            for ob in objs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(actions[ob.name])
            bl.push_track(ob, actions[ob.name], name)
    for ob in objs:
        ob.animation_data.use_nla = False
    apply_pose(rig, rest_pose())


def pose_for_icon(rig):
    """The icon's still: switched on, the arm raised over the pad."""
    p = crafting(0.42)
    p["spark"] = OFF
    p["scan"] = 0.3
    apply_pose(rig, p)


def strip_animation(rig):
    for ob in bbe.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None


def envelope(rig):
    """The model's bounds over every clip frame, to check it stays inside the 4 x 3 x 3 block envelope."""
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for name, make in CLIPS.items():
        length, _ = make()
        for f in range(0, int(round(length * FPS)) + 1, 2):
            apply_pose(rig, pose_at(name, f / FPS))
            bpy.context.view_layer.update()
            for ob in bbe.mesh_tree(rig["root"]):
                if ob.type != "MESH" or ob.name.startswith("Glow"):
                    continue
                co = np.zeros(len(ob.data.vertices) * 3)
                ob.data.vertices.foreach_get("co", co)
                co = co.reshape(-1, 3) @ np.asarray(ob.matrix_world.to_3x3()).T + np.asarray(ob.matrix_world.translation)
                lo = Vector(np.minimum(lo, co.min(0)))
                hi = Vector(np.maximum(hi, co.max(0)))
    apply_pose(rig, rest_pose())
    return lo, hi


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_fabricator(mats)
    lo, hi = envelope(rig)
    print(f"bounds over every clip: x {lo.x:.3f}..{hi.x:.3f}  y {lo.y:.3f}..{hi.y:.3f}  z {lo.z:.3f}..{hi.z:.3f}")
    if lo.x < -HX - 1e-3 or hi.x > HX + 1e-3 or lo.y < -HY - 1e-3 or hi.y > HY + 1e-3 or lo.z < -HZ - 1e-3 or hi.z > HZ + 1e-3:
        raise SystemExit("the model leaves its 4 x 3 x 3 block envelope")
    bake(rig)
    bbe.export(rig["root"], MESH, animations=True)
    bpy.context.scene.frame_set(0)
    strip_animation(rig)
    pose_for_icon(rig)
    bbe.render_icon(bbe.mesh_tree(rig["root"]), ICON, view=(-1.5, 2.0, 1.25), ortho=1.42)
    apply_pose(rig, rest_pose())
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in bbe.mesh_tree(rig["root"]) if ob.type == "MESH")
    for p in (MESH, ICON):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    print(f"{tris} triangles")
    if "--render" in sys.argv:
        import render_portable_fabricator
        render_portable_fabricator.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
