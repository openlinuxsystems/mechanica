"""Build the Chemical, Water and Potato Ammo Containers and their icons (siblings of the Gas and Battery Containers).

The Gas Container's design language (tools/gas_container/build_gas_container.py): a dark gunmetal box with chamfered
corner blocks and riveted edge rails, recessed riveted panels, copper fittings and bolts, a lit gauge behind glass. Each
of the three keeps that case and gets its own lid, gauge and end fittings, so the five consumable containers read as
one family and are still told apart at a glance.

What it builds (procedurally, from scratch: no .blend inputs, no downloads). Each is 4 x 3 x 3 blocks
(1.0 x 0.75 x 0.75 m centred on the origin, long along X, Z up in Blender; the -Y long side is the game's front, +Z)
and stays inside that box. The models are static (no clips).

* ``mods/vanilla/meshes/parts/chemical_container.glb`` - a sealed chemical drum in a case: a violet lid with a
  bolted manway (hex bung) and a violet-capped relief valve, violet end frames, and on each long side a violet stripe,
  a chemical hazard plate (violet diamond, white flask) and a round glass sight tube with a violet liquid column
  between copper end caps. The -X end has a round vent grille.
* ``mods/vanilla/meshes/parts/water_container.glb`` - a blue lid with a big ribbed filler cap, blue end frames, and on
  each long side a round copper porthole gauge with the blue water level behind glass next to a water-drop plate. The
  -X end has the tap: a copper gate valve with a blue hand wheel and a spout.
* ``mods/vanilla/meshes/parts/potato_ammo_container.glb`` - an ammo box: a separate hinged lid with a mustard
  (#E0B530) stencil band ("SPUD AMMO"), two copper lever latches on the front, hinges on the back, folding carry
  handles on both ends of the lid, and on each long side a small window full of potatoes.
* ``mods/vanilla/textures/items/<id>_icon.png`` - 256 x 256 on a clear background, framed as the Gas Container's icon
  (three-quarter from the -Y front, the +X end on the right).

    pip install bpy==5.2.2 numpy pillow
    python tools/containers/build_consumables.py                  # writes the .glb files and the icons
    python tools/containers/build_consumables.py --render OUT_DIR # also renders review images and sheets

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import json
import math
import struct
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
IDS = ("chemical_container", "water_container", "potato_ammo_container")

Geo, box, cylinder, ring, dome, hexnut, sweep = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep
rivets = gc.rivets

HALF_X, HALF_Y, HALF_Z = 0.5, 0.375, 0.375  # the part's half sizes: 4 x 3 x 3 blocks
FLOOR = -HALF_Z
RAIL, CORNER = 0.028, 0.046                 # as the Gas Container


# =====================================================================================================
# Decals (painted plates, drawn with PIL)
# =====================================================================================================

def _font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
              "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"):
        if Path(p).exists():
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def _arr(im):
    return np.asarray(im.convert("RGB"), float) / 255.0


def decal_hazard():
    """A chemical hazard plate: a violet diamond with a white rim and a white flask on a dark plate."""
    s = 256
    im = Image.new("RGB", (s, s), (32, 35, 38))
    d = ImageDraw.Draw(im)
    c = s / 2
    d.polygon([(c, 8), (s - 8, c), (c, s - 8), (8, c)], fill=(236, 234, 228))
    d.polygon([(c, 22), (s - 22, c), (c, s - 22), (22, c)], fill=(122, 76, 192))
    # the flask: a neck and a wide body, with bubbles
    d.rectangle([c - 13, 66, c + 13, 104], fill=(240, 238, 232))
    d.rectangle([c - 19, 60, c + 19, 70], fill=(240, 238, 232))
    d.polygon([(c - 13, 100), (c + 13, 100), (c + 50, 170), (c - 50, 170)], fill=(240, 238, 232))
    d.polygon([(c - 32, 136), (c + 32, 136), (c + 44, 160), (c - 44, 160)], fill=(70, 36, 118))
    for x, y, r in ((c - 6, 122, 5), (c + 9, 112, 4), (c + 2, 90, 3)):
        d.ellipse([x - r, y - r, x + r, y + r], fill=(122, 76, 192))
    return _arr(im)


def decal_drop():
    """A water-drop plate: a blue drop with a highlight and two wave lines on a dark plate."""
    w, h = 256, 256
    im = Image.new("RGB", (w, h), (32, 35, 38))
    d = ImageDraw.Draw(im)
    d.rectangle([6, 6, w - 7, h - 7], outline=(84, 90, 98), width=6)
    cx, cy, r = w / 2, 128, 54
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(56, 132, 220))
    d.polygon([(cx, 32), (cx + r * 0.86, cy - r * 0.5), (cx - r * 0.86, cy - r * 0.5)], fill=(56, 132, 220))
    d.ellipse([cx - 32, cy - 14, cx - 14, cy + 22], fill=(170, 214, 255))
    for k, y in enumerate((202, 224)):
        pts = [(40 + i * 8, y + 6 * math.sin(i * 0.9 + k)) for i in range(23)]
        d.line(pts, fill=(56, 132, 220), width=7)
    return _arr(im)


def decal_stencil(text="SPUD AMMO", w=1024, h=160):
    """A mustard band with dark stencilled text (with the stencil's bridges) and chevrons at the ends."""
    im = Image.new("RGB", (w, h), (224, 181, 48))
    d = ImageDraw.Draw(im)
    f = _font(int(h * 0.62))
    tw = d.textlength(text, font=f)
    x0 = (w - tw) / 2
    d.text((x0, h * 0.17), text, fill=(30, 32, 34), font=f)
    # stencil bridges: thin mustard gaps through the letters
    for k in range(len(text)):
        if text[k] == " ":
            continue
        cx = x0 + d.textlength(text[:k], font=f) + d.textlength(text[k], font=f) / 2
        d.rectangle([cx - 3, h * 0.2, cx + 3, h * 0.8], fill=(224, 181, 48))
    for sx in (1, -1):
        for k in range(2):
            x = (36 + k * 44) if sx > 0 else (w - 36 - k * 44)
            d.polygon([(x, 30), (x + sx * 26, h / 2), (x, h - 30), (x + sx * 12, h - 30), (x + sx * 38, h / 2),
                       (x + sx * 12, 30)][::sx], fill=(30, 32, 34))
    d.rectangle([0, 0, w - 1, 9], fill=(40, 42, 44))
    d.rectangle([0, h - 10, w - 1, h - 1], fill=(40, 42, 44))
    return _arr(im)


def decal_potato():
    """A label: a mustard potato stencil and five tally bars (five stacks) on a dark plate."""
    w, h = 280, 120
    im = Image.new("RGB", (w, h), (32, 35, 38))
    d = ImageDraw.Draw(im)
    d.rectangle([4, 4, w - 5, h - 5], outline=(224, 181, 48), width=5)
    d.ellipse([22, 26, 122, 94], fill=(224, 181, 48))
    for x, y in ((50, 46), (84, 62), (66, 76), (98, 44)):
        d.ellipse([x - 5, y - 4, x + 5, y + 4], fill=(32, 35, 38))
    for k in range(5):
        x = 146 + k * 24
        d.rectangle([x, 32, x + 12, 88], fill=(224, 181, 48))
    return _arr(im)


# =====================================================================================================
# Materials
# =====================================================================================================

def make_materials():
    m = gc.make_materials()
    half = gc.half_res
    m.update({
        "hull": lt.textured("cc_hull", half(lt.metal_surface(431, "#2a2e34", 0.52, 0.7, wear=0.6, edge_light=0.25)), normal_strength=0.7),
        "violet": lt.textured("cc_violet", half(lt.paint_surface(821, "#5c2fa6", "#4a4d52")), normal_strength=0.5),
        "blue": lt.textured("cc_blue", half(lt.paint_surface(823, "#1f5fae", "#4a4d52")), normal_strength=0.5),
        "mustard": lt.textured("cc_mustard", half(lt.paint_surface(825, "#e0b530", "#4a4d52")), normal_strength=0.5),
        "chem": pt.plain("cc_chem", "#6a2fb8", 0.15, 0.0, emission="#9a55ff", strength=1.6),
        "water": pt.plain("cc_water", "#1d6cd0", 0.1, 0.0, emission="#3a98ff", strength=1.1),
        "water_top": pt.plain("cc_water_top", "#6cb8ff", 0.1, 0.0, emission="#7cc4ff", strength=1.6),
        "potato": pt.plain("cc_potato", "#94642f", 0.85, 0.0),
        "potato_dark": pt.plain("cc_potato_dark", "#6e4a24", 0.85, 0.0),
        "dark": pt.plain("cc_dark", "#202326", 0.5, 0.4),
        "decal_hazard": lt.decal("cc_decal_hazard", decal_hazard(), rough=0.45),
        "decal_drop": lt.decal("cc_decal_drop", decal_drop(), rough=0.45),
        "decal_stencil": lt.decal("cc_decal_stencil", decal_stencil(), rough=0.55),
        "decal_potato": lt.decal("cc_decal_potato", decal_potato(), rough=0.5),
    })
    return m


# =====================================================================================================
# Shared pieces: the case, panels, plates, sockets
# =====================================================================================================

def case(g, bx, by, z0, z1):
    """The Gas Container's case: a dark core, riveted chamfered edge rails and corner blocks with bolts."""
    zc, hz = (z0 + z1) / 2, (z1 - z0) / 2
    box(g, (0, 0, zc), (bx - 0.022, by - 0.022, hz - 0.022), "gun_dark", bevel=0.004)
    ext = (bx - RAIL, by - RAIL, hz - RAIL)
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0 in (-1, 1):
            for s1 in (-1, 1):
                c = [0.0, 0.0, zc]
                c[others[0]] += s0 * ext[others[0]]
                c[others[1]] += s1 * ext[others[1]]
                half = [RAIL, RAIL, RAIL]
                half[axis] = ext[axis] - CORNER + 0.004
                box(g, c, half, "gun", bevel=0.009)
                for o, s in ((others[0], s0), (others[1], s1)):
                    n = [0.0, 0.0, 0.0]
                    n[o] = s
                    if o == 2 and s < 0:      # no rivets underneath: it sits flat on the floor
                        continue
                    a = list(c)
                    a[o] += s * RAIL
                    b = list(a)
                    a[axis] -= half[axis] - 0.03
                    b[axis] += half[axis] - 0.03
                    rivets(g, a, b, max(2, int(round(2 * half[axis] / 0.09)) + 1), n, r=0.0058)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * (bx - CORNER), sy * (by - CORNER), zc + sz * (hz - CORNER))
                box(g, c, (CORNER, CORNER, CORNER), "gun", bevel=0.016)
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz))[:3 if sz > 0 else 2]:  # none underneath: it sits flat
                    hexnut(g, Vector(c) + Vector(n) * CORNER, n, 0.0095, 0.005, "bolt")


def panel(g, y, sy, x0, x1, z0, z1, field="plate", rows=True):
    """A raised border plate and the recessed field inside it, with a rivet border (on a long side at y, facing sy)."""
    n = (0, sy, 0)
    cx, cz, hx, hz = (x0 + x1) / 2, (z0 + z1) / 2, (x1 - x0) / 2, (z1 - z0) / 2
    box(g, (cx, y + sy * 0.006, cz), (hx, 0.006, hz), "gun", bevel=0.005)
    box(g, (cx, y + sy * 0.008, cz), (hx - 0.024, 0.006, hz - 0.024), field, bevel=0.004)
    yy = y + sy * 0.012
    k = max(3, int(round((x1 - x0) / 0.05)))
    rivets(g, (x0 + 0.012, yy, z0 + 0.012), (x1 - 0.012, yy, z0 + 0.012), k, n, r=0.0052)
    rivets(g, (x0 + 0.012, yy, z1 - 0.012), (x1 - 0.012, yy, z1 - 0.012), k, n, r=0.0052)
    if rows:
        kz = max(2, int(round((z1 - z0 - 0.12) / 0.06)))
        rivets(g, (x0 + 0.012, yy, z0 + 0.06), (x0 + 0.012, yy, z1 - 0.06), kz, n, r=0.0052)
        rivets(g, (x1 - 0.012, yy, z0 + 0.06), (x1 - 0.012, yy, z1 - 0.06), kz, n, r=0.0052)


def decal_quad(g, y, sy, x0, x1, z0, z1, mat):
    """An upright decal on a plane at y facing sy (read the right way round from either side)."""
    if sy < 0:
        corners = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
    else:
        corners = [(x1, y, z0), (x0, y, z0), (x0, y, z1), (x1, y, z1)]
    bl.quad(g, corners, mat)


def decal_quad_x(g, x, sx, y0, y1, z0, z1, mat):
    """An upright decal on a plane at x facing sx."""
    if sx > 0:
        corners = [(x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1)]
    else:
        corners = [(x, y1, z0), (x, y0, z0), (x, y0, z1), (x, y1, z1)]
    bl.quad(g, corners, mat)


def plate(g, y, sy, cx, cz, hx, hz, decal=None, field="gun_dark"):
    """A bolted gunmetal plate (a label or emblem) on a long side, with a decal or a dark field."""
    n = (0, sy, 0)
    box(g, (cx, y + sy * 0.017, cz), (hx, 0.004, hz), "gun", bevel=0.003)
    if decal:
        decal_quad(g, y + sy * 0.0212, sy, cx - hx + 0.01, cx + hx - 0.01, cz - hz + 0.01, cz + hz - 0.01, decal)
    else:
        box(g, (cx, y + sy * 0.0205, cz), (hx - 0.01, 0.0008, hz - 0.009), field)
    for dx in (-hx + 0.006, hx - 0.006):
        for dz in (-hz + 0.006, hz - 0.006):
            dome(g, (cx + dx, y + sy * 0.021, cz + dz), n, 0.0042, "bolt")


def end_frame(g, sx, bx, by, zc, hz, mat):
    """A painted frame round the end face over a gunmetal inset panel (the Gas Container's ends, recoloured)."""
    x = sx * (bx - 0.004)
    fy, fz, t = by - 0.05, hz - 0.05, 0.022
    for s in (-1, 1):
        box(g, (x + sx * 0.008, s * (fy - t), zc), (0.008, t, fz), mat, bevel=0.005)
        box(g, (x + sx * 0.008, 0, zc + s * (fz - t)), (0.008, fy, t), mat, bevel=0.005)
    box(g, (x + sx * 0.003, 0, zc), (0.004, fy - 2 * t, fz - 2 * t), "plate", bevel=0.003)
    for s in (-1, 1):
        for zz in np.linspace(zc - fz + 0.04, zc + fz - 0.04, 5):
            dome(g, (x + sx * 0.016, s * (fy - t), zz), (sx, 0, 0), 0.0055, "bolt")


def turned(axis_to, at):
    """A matrix: something built round +Z turned so +Z points along axis_to, moved to at."""
    q = Vector((0, 0, 1)).rotation_difference(Vector(axis_to))
    return Matrix.Translation(at) @ q.to_matrix().to_4x4()


def oriented(g, mat=None):
    """Flip faces whose normal points toward the centroid of the whole piece (for convex pieces built loosely)."""
    c = Vector(np.mean(np.asarray(g.v), axis=0))
    out = []
    for f in g.f:
        p = [Vector(g.v[i]) for i in f]
        nrm = (p[1] - p[0]).cross(p[2] - p[0])
        fc = sum(p, Vector()) / len(p)
        out.append(f if nrm.dot(fc - c) >= 0 else tuple(reversed(f)))
    g.f = out
    return g


def prism(pts, y0, y1, mat):
    """A convex prism: the outline pts (x, z) between y0 and y1."""
    g = Geo()
    m = len(pts)
    verts = [(x, y0, z) for x, z in pts] + [(x, y1, z) for x, z in pts]
    faces = [tuple(range(m)), tuple(range(m, 2 * m))] + [(i, (i + 1) % m, m + (i + 1) % m, m + i) for i in range(m)]
    g.add(verts, faces, mat)
    return oriented(g)


def blob(center, radii, seed, mat, rot=None, rings=7, segs=12):
    """A lumpy ellipsoid (a potato)."""
    rng = np.random.default_rng(seed)
    ph = rng.uniform(0, 2 * math.pi, 4)
    amp = rng.uniform(0.04, 0.1, 4)
    m = Matrix(rot) if rot is not None else Matrix.Identity(3)
    verts = []
    for i in range(1, rings):
        th = math.pi * i / rings
        for k in range(segs):
            a = 2 * math.pi * k / segs
            bump = 1 + amp[0] * math.sin(2 * a + ph[0]) * math.sin(th) + amp[1] * math.cos(3 * th + ph[1]) \
                + amp[2] * math.sin(a + 2 * th + ph[2])
            p = Vector((radii[0] * math.sin(th) * math.cos(a), radii[1] * math.sin(th) * math.sin(a), radii[2] * math.cos(th))) * bump
            verts.append(tuple(m @ p + Vector(center)))
    top = tuple(m @ Vector((0, 0, radii[2] * (1 + amp[3]))) + Vector(center))
    bot = tuple(m @ Vector((0, 0, -radii[2] * (1 - amp[3]))) + Vector(center))
    verts += [top, bot]
    it, ib = len(verts) - 2, len(verts) - 1
    faces = []
    for i in range(rings - 2):
        for k in range(segs):
            k1 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k1, (i + 1) * segs + k1, (i + 1) * segs + k))
    for k in range(segs):
        k1 = (k + 1) % segs
        faces.append((it, k1, k))
        faces.append((ib, (rings - 2) * segs + k, (rings - 2) * segs + k1))
    g = Geo()
    g.add(verts, faces, mat)
    return oriented(g)


def glass_pane(g, y, cx, cz, hx, hz):
    box(g, (cx, y, cz), (hx, 0.0012, hz), "glass")


# =====================================================================================================
# Chemical Container: violet lid and end frames, hazard plate, sight tube
# =====================================================================================================

class Chem:
    BX, BY = 0.455, 0.345
    Z_BOT, Z_TOP = -0.375, 0.30
    ZC, HZ = (Z_BOT + Z_TOP) / 2, (Z_TOP - Z_BOT) / 2
    SIDE = BY - 0.022
    TUBE_X, TUBE_R = 0.265, 0.027
    TUBE_Z0, TUBE_Z1 = -0.245, 0.17
    FILL = 0.6


def chem_side(g, sy):
    c = Chem
    y = sy * c.SIDE
    n = (0, sy, 0)
    px0, px1 = -c.BX + 0.07, 0.15
    pz0, pz1 = c.Z_BOT + 0.065, c.Z_TOP - 0.065
    panel(g, y, sy, px0, px1, pz0, pz1)
    # a violet stripe along the panel's top, a hazard plate and a label plate
    box(g, ((px0 + px1) / 2, y + sy * 0.0145, pz1 - 0.05), ((px1 - px0) / 2 - 0.03, 0.0008, 0.016), "violet")
    plate(g, y, sy, -0.175, -0.03, 0.105, 0.105, decal="decal_hazard")
    plate(g, y, sy, -0.175, -0.215, 0.075, 0.03)
    box(g, (-0.175, y + sy * 0.0215, -0.205), (0.05, 0.0006, 0.004), "tick")
    box(g, (-0.19, y + sy * 0.0215, -0.225), (0.035, 0.0006, 0.004), "tick")
    # a small round inspection hatch beside the plates
    hx, hz = 0.07, -0.215
    cylinder(g, (hx, y + sy * 0.014, hz), (hx, y + sy * 0.021, hz), 0.045, "gun", segs=36)
    cylinder(g, (hx, y + sy * 0.021, hz), (hx, y + sy * 0.0235, hz), 0.039, "plate", segs=36)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        dome(g, (hx + 0.03 * math.cos(a), y + sy * 0.0235, hz + 0.03 * math.sin(a)), n, 0.0036, "bolt")
    # the sight tube's mount: a riveted plate, a dark slot behind the tube, copper caps and clamps, ticks
    tx = c.TUBE_X
    gz0, gz1 = c.Z_BOT + 0.065, c.Z_TOP - 0.065
    box(g, (tx, y + sy * 0.006, (gz0 + gz1) / 2), (0.075, 0.006, (gz1 - gz0) / 2), "gun", bevel=0.005)
    box(g, (tx, y + sy * 0.0115, (c.TUBE_Z0 + c.TUBE_Z1) / 2), (c.TUBE_R + 0.008, 0.0008, (c.TUBE_Z1 - c.TUBE_Z0) / 2), "backing")
    for dz in (gz0 + 0.016, gz1 - 0.016):
        for dx in (-0.06, 0.06):
            dome(g, (tx + dx, y + sy * 0.012, dz), n, 0.0052, "bolt")
    ty = y + sy * 0.013
    for z0, z1 in ((c.TUBE_Z0 - 0.026, c.TUBE_Z0), (c.TUBE_Z1, c.TUBE_Z1 + 0.026)):
        cylinder(g, (tx, ty, z0), (tx, ty, z1), c.TUBE_R + 0.009, "copper", segs=28)
        zz = (z0 + z1) / 2
        box(g, (tx, y + sy * 0.017, zz), (c.TUBE_R + 0.022, 0.005, 0.009), "copper", bevel=0.003)
        for dx in (-1, 1):
            hexnut(g, (tx + dx * (c.TUBE_R + 0.015), y + sy * 0.022, zz), n, 0.0055, 0.003, "bolt")
    for k in range(6):
        z = c.TUBE_Z0 + (c.TUBE_Z1 - c.TUBE_Z0) * k / 5
        for s in (-1, 1):
            w = 0.011 if k in (0, 5) else 0.009
            box(g, (tx + s * (c.TUBE_R + 0.016 + w), y + sy * 0.0125, z), (w, 0.0008, 0.0022), "tick")


def chem_top(g):
    c = Chem
    z = c.Z_TOP - 0.022
    box(g, (0, 0, z + 0.007), (c.BX - 0.07, c.BY - 0.07, 0.007), "violet", bevel=0.006)
    rivets(g, (-c.BX + 0.085, -c.BY + 0.085, z + 0.014), (c.BX - 0.085, -c.BY + 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    rivets(g, (-c.BX + 0.085, c.BY - 0.085, z + 0.014), (c.BX - 0.085, c.BY - 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    zt = z + 0.014
    # two raised ribs across the lid, like a drum's rolling hoops
    for x in (-0.33, 0.33):
        box(g, (x, 0, zt + 0.006), (0.014, c.BY - 0.08, 0.006), "violet", bevel=0.004)
    # the manway: a copper flange, eight nuts, a gunmetal cover and the hex bung
    mx = -0.12
    cylinder(g, (mx, 0, zt), (mx, 0, zt + 0.012), 0.11, "copper", segs=48)
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 8
        hexnut(g, (mx + 0.094 * math.cos(a), 0.094 * math.sin(a), zt + 0.012), (0, 0, 1), 0.0085, 0.006, "bolt")
    cylinder(g, (mx, 0, zt + 0.012), (mx, 0, zt + 0.026), 0.08, "gun", segs=48)
    cylinder(g, (mx, 0, zt + 0.026), (mx, 0, zt + 0.03), 0.07, "gun_dark", segs=48)
    hexnut(g, (mx, 0, zt + 0.03), (0, 0, 1), 0.034, 0.018, "copper")
    box(g, (mx, 0, zt + 0.0485), (0.022, 0.004, 0.0008), "hole")
    # the relief valve: a copper base, a stem, a violet cap
    vx, vy = 0.24, -0.1
    cylinder(g, (vx, vy, zt), (vx, vy, zt + 0.014), 0.042, "copper", segs=32)
    hexnut(g, (vx, vy, zt + 0.014), (0, 0, 1), 0.024, 0.014, "copper")
    cylinder(g, (vx, vy, zt + 0.028), (vx, vy, zt + 0.052), 0.012, "steel", segs=16)
    cylinder(g, (vx, vy, zt + 0.05), (vx, vy, zt + 0.064), 0.03, "violet", segs=28)
    dome(g, (vx, vy, zt + 0.064), (0, 0, 1), 0.03, "violet", segs=20, height=0.008)
    # a small plate with a slot (the sampling port)
    box(g, (0.24, 0.12, zt + 0.005), (0.06, 0.05, 0.005), "gun", bevel=0.004)
    box(g, (0.24, 0.12, zt + 0.0105), (0.03, 0.006, 0.0008), "hole")


def chem_ends(g):
    c = Chem
    for sx in (-1, 1):
        end_frame(g, sx, c.BX, c.BY, c.ZC, c.HZ, "violet")
    # the round vent grille on -X: a copper ring, a dark field, slats
    x = -c.BX - 0.004
    z = c.ZC
    cylinder(g, (x, 0, z), (x - 0.014, 0, z), 0.105, "copper", segs=48)
    cylinder(g, (x - 0.014, 0, z), (x - 0.016, 0, z), 0.088, "hole", segs=48)
    for k in range(7):
        zz = z - 0.066 + k * 0.022
        w = math.sqrt(max(0.0, 0.088 ** 2 - (zz - z) ** 2)) - 0.004
        box(g, (x - 0.017, 0, zz), (0.004, w, 0.0055), "gun", bevel=0.002)
    for k in range(6):
        a = 2 * math.pi * k / 6
        hexnut(g, (x - 0.014, 0.096 * math.cos(a), z + 0.096 * math.sin(a)), (-1, 0, 0), 0.006, 0.004, "bolt")


def build_chemical(mats):
    c = Chem
    root = bl.make_empty("ChemicalContainer")
    g = Geo()
    case(g, c.BX, c.BY, c.Z_BOT, c.Z_TOP)
    for sy in (-1, 1):
        chem_side(g, sy)
    chem_top(g)
    chem_ends(g)
    bl.make_object("Housing", g, mats, root)
    gc.mount_io_port(root)
    glass, liquid = Geo(), Geo()
    h = c.TUBE_Z1 - c.TUBE_Z0
    for sy in (-1, 1):
        ty = sy * (c.SIDE + 0.013)
        cylinder(glass, (c.TUBE_X, ty, c.TUBE_Z0), (c.TUBE_X, ty, c.TUBE_Z1), c.TUBE_R, "glass", segs=28)
        top = c.TUBE_Z0 + h * c.FILL
        cylinder(liquid, (c.TUBE_X, ty, c.TUBE_Z0), (c.TUBE_X, ty, top), c.TUBE_R - 0.005, "chem", segs=24)
        cylinder(liquid, (c.TUBE_X, ty, top), (c.TUBE_X, ty, top + 0.004), c.TUBE_R - 0.007, "glow_soft", segs=24)
    bl.make_object("Liquid", liquid, mats, root)
    bl.make_object("Glass", glass, mats, root)
    return root


# =====================================================================================================
# Water Container: blue lid and end frames, porthole gauge, tap
# =====================================================================================================

class Water:
    BX, BY = 0.42, 0.345
    Z_BOT, Z_TOP = -0.375, 0.29
    ZC, HZ = (Z_BOT + Z_TOP) / 2, (Z_TOP - Z_BOT) / 2
    SIDE = BY - 0.022
    PORT_X, PORT_Z, PORT_R = 0.19, -0.045, 0.112
    FILL = 0.6
    TAP_Z = -0.06


def y_ring(g, y, sy, cx, cz, r_in, r_out, depth, mat, segs=48):
    """A ring on a long side at y, standing `depth` out toward sy."""
    tmp = Geo()
    ring(tmp, 0.0, depth, r_in, r_out, mat, segs=segs)
    g.extend(tmp, turned((0, sy, 0), Vector((cx, y, cz))))


def water_side(g, sy):
    w = Water
    y = sy * w.SIDE
    n = (0, sy, 0)
    px0, px1 = -w.BX + 0.07, -0.03
    pz0, pz1 = w.Z_BOT + 0.065, w.Z_TOP - 0.065
    panel(g, y, sy, px0, px1, pz0, pz1)
    plate(g, y, sy, -0.205, 0.035, 0.095, 0.095, decal="decal_drop")
    plate(g, y, sy, -0.205, -0.215, 0.075, 0.03)
    box(g, (-0.205, y + sy * 0.0215, -0.205), (0.05, 0.0006, 0.004), "tick")
    box(g, (-0.22, y + sy * 0.0215, -0.225), (0.035, 0.0006, 0.004), "tick")
    # the porthole: a riveted square mount, a copper bezel with eight nuts, a dark backing, ticks on the glass
    cx, cz, r = w.PORT_X, w.PORT_Z, w.PORT_R
    box(g, (cx, y + sy * 0.006, cz), (0.168, 0.006, 0.168), "gun", bevel=0.006)
    box(g, (cx, y + sy * 0.008, cz), (0.148, 0.006, 0.148), "plate", bevel=0.004)
    for dx in (-0.156, 0.156):
        for dz in (-0.156, 0.156):
            dome(g, (cx + dx, y + sy * 0.012, cz + dz), n, 0.0055, "bolt")
    cylinder(g, (cx, y + sy * 0.0135, cz), (cx, y + sy * 0.0145, cz), r + 0.004, "backing", segs=48)
    y_ring(g, y + sy * 0.012, sy, cx, cz, r, r + 0.036, 0.016, "copper")
    y_ring(g, y + sy * 0.028, sy, cx, cz, r + 0.004, r + 0.026, 0.004, "copper")
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 8
        hexnut(g, (cx + (r + 0.018) * math.cos(a), y + sy * 0.032, cz + (r + 0.018) * math.sin(a)), n, 0.0065, 0.004, "bolt")
    for k in range(1, 5):
        z = -r + 2 * r * k / 5
        x_edge = math.sqrt(r * r - z * z)
        box(g, (cx - x_edge + 0.014, y + sy * 0.0262, cz + z), (0.014, 0.0006, 0.0022), "tick")


def water_level(sy, y, cx, cz, r, fill, thick):
    """The water inside the porthole: the circle below the level as a thin disc segment, its surface line lit."""
    zl = -r + 2 * r * fill
    a0 = math.asin(max(-1.0, min(1.0, zl / r)))
    pts = []
    steps = 40
    for k in range(steps + 1):
        a = (math.pi - a0) + (math.pi + 2 * a0) * k / steps     # from the level's left end down round to its right end
        pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    seg = prism(pts, y, y + sy * thick, "water")
    hw = math.sqrt(r * r - zl * zl)
    line = Geo()
    box(line, (cx, y + sy * thick / 2, cz + zl - 0.003), (hw - 0.004, thick / 2 + 0.0004, 0.003), "water_top")
    seg.extend(line)
    return seg


def water_top(g):
    w = Water
    z = w.Z_TOP - 0.022
    box(g, (0, 0, z + 0.007), (w.BX - 0.07, w.BY - 0.07, 0.007), "blue", bevel=0.006)
    rivets(g, (-w.BX + 0.085, -w.BY + 0.085, z + 0.014), (w.BX - 0.085, -w.BY + 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    rivets(g, (-w.BX + 0.085, w.BY - 0.085, z + 0.014), (w.BX - 0.085, w.BY - 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    zt = z + 0.014
    # the filler: a copper boss and a big ribbed gunmetal cap with a copper top and a T grip
    fx, fy = -0.17, 0.0
    cylinder(g, (fx, fy, zt), (fx, fy, zt + 0.014), 0.092, "copper", segs=48)
    for k in range(6):
        a = 2 * math.pi * k / 6
        hexnut(g, (fx + 0.08 * math.cos(a), fy + 0.08 * math.sin(a), zt + 0.014), (0, 0, 1), 0.0075, 0.005, "bolt")
    cylinder(g, (fx, fy, zt + 0.014), (fx, fy, zt + 0.05), 0.064, "gun", segs=48)
    for k in range(16):
        a = 2 * math.pi * k / 16
        box(g, (fx + 0.064 * math.cos(a), fy + 0.064 * math.sin(a), zt + 0.032), (0.004, 0.004, 0.017), "gun",
            rot=Matrix.Rotation(a, 3, "Z"))
    cylinder(g, (fx, fy, zt + 0.05), (fx, fy, zt + 0.056), 0.05, "copper", segs=40)
    box(g, (fx, fy, zt + 0.067), (0.04, 0.009, 0.011), "blue", bevel=0.004)
    # a vent mushroom and a small hatch
    vx, vy = 0.24, 0.13
    cylinder(g, (vx, vy, zt), (vx, vy, zt + 0.03), 0.014, "steel", segs=16)
    cylinder(g, (vx, vy, zt + 0.03), (vx, vy, zt + 0.042), 0.034, "gun", segs=28)
    dome(g, (vx, vy, zt + 0.042), (0, 0, 1), 0.034, "gun", segs=20, height=0.01)
    box(g, (0.18, -0.1, zt + 0.006), (0.1, 0.08, 0.006), "gun", bevel=0.005)
    box(g, (0.18, -0.1, zt + 0.011), (0.082, 0.062, 0.005), "gun_dark", bevel=0.003)
    box(g, (0.18, -0.14, zt + 0.0162), (0.035, 0.007, 0.0006), "hole")


def water_ends(g):
    w = Water
    for sx in (-1, 1):
        end_frame(g, sx, w.BX, w.BY, w.ZC, w.HZ, "blue")
    gc.port_block(g, w.BX)
    # the tap on -X: a pipe out of a flange, a gate valve body, a stem and a blue hand wheel facing -X, a spout down
    s = -1
    z = w.TAP_Z
    x0 = s * (w.BX + 0.004)
    cylinder(g, (x0, 0, z), (x0 + s * 0.012, 0, z), 0.058, "copper", segs=36)
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        hexnut(g, (x0 + s * 0.012, 0.047 * math.cos(a), z + 0.047 * math.sin(a)), (s, 0, 0), 0.006, 0.004, "bolt")
    cylinder(g, (x0 + s * 0.012, 0, z), (s * 0.44, 0, z), 0.026, "copper", segs=24)
    box(g, (s * 0.458, 0, z), (0.02, 0.036, 0.036), "copper", bevel=0.008)
    cylinder(g, (s * 0.47, 0, z), (s * 0.484, 0, z), 0.012, "steel", segs=16)
    wheel = Geo()
    ring(wheel, -0.006, 0.006, 0.05, 0.062, "blue", segs=40)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        box(wheel, (0.028 * math.cos(a), 0.028 * math.sin(a), 0), (0.024, 0.006, 0.004), "blue",
            rot=Matrix.Rotation(a, 3, "Z"))
    cylinder(wheel, (0, 0, -0.008), (0, 0, 0.008), 0.016, "copper", segs=20)
    for k in range(10):
        a = 2 * math.pi * k / 10
        dome(wheel, (0.056 * math.cos(a), 0.056 * math.sin(a), 0.006), (0, 0, 1), 0.006, "blue", segs=8, height=0.003)
    g.extend(wheel, turned((s, 0, 0), Vector((s * 0.488, 0, z))))
    # the spout: down from the valve body, a collar, a flared mouth with a dark bore
    xs = s * 0.458
    cylinder(g, (xs, 0, z - 0.036), (xs, 0, z - 0.13), 0.017, "copper", segs=20)
    cylinder(g, (xs, 0, z - 0.1), (xs, 0, z - 0.11), 0.022, "copper", segs=20)
    cylinder(g, (xs, 0, z - 0.13), (xs, 0, z - 0.15), 0.017, "copper", segs=20, r1=0.024)
    cylinder(g, (xs, 0, z - 0.1505), (xs, 0, z - 0.135), 0.017, "hole", segs=20)


def build_water(mats):
    w = Water
    root = bl.make_empty("WaterContainer")
    g = Geo()
    case(g, w.BX, w.BY, w.Z_BOT, w.Z_TOP)
    for sy in (-1, 1):
        water_side(g, sy)
    water_top(g)
    water_ends(g)
    bl.make_object("Housing", g, mats, root)
    gc.mount_io_port(root)
    liquid, glass = Geo(), Geo()
    for sy in (-1, 1):
        liquid.extend(water_level(sy, sy * (w.SIDE + 0.0145), w.PORT_X, w.PORT_Z, w.PORT_R, w.FILL, 0.004))
        cylinder(glass, (w.PORT_X, sy * (w.SIDE + 0.0235), w.PORT_Z), (w.PORT_X, sy * (w.SIDE + 0.0255), w.PORT_Z),
                 w.PORT_R + 0.002, "glass", segs=48)
    bl.make_object("Liquid", liquid, mats, root)
    bl.make_object("Glass", glass, mats, root)
    return root


# =====================================================================================================
# Potato Ammo Container: an ammo box with a hinged lid, latches, end handles, a potato window
# =====================================================================================================

class Potato:
    BX, BY = 0.43, 0.335
    Z_BOT, Z_LID = -0.375, 0.255     # the body's bottom and top (the lid sits on it, above the IO port)
    LX, LY, LZ1 = 0.445, 0.352, 0.352  # the lid's half length, half depth and top
    SIDE = BY - 0.022
    WIN_X, WIN_Z, WIN_HX, WIN_HZ = 0.215, -0.105, 0.088, 0.07
    LATCH_X = (-0.3, 0.3)


def potato_side(g, sy):
    p = Potato
    y = sy * p.SIDE
    n = (0, sy, 0)
    px0, px1 = -p.BX + 0.07, 0.09
    pz0, pz1 = p.Z_BOT + 0.065, p.Z_LID - 0.065
    panel(g, y, sy, px0, px1, pz0, pz1)
    plate(g, y, sy, -0.13, -0.085, 0.115, 0.05, decal="decal_potato")
    for x in (-0.13,):
        box(g, (x, y + sy * 0.0145, -0.225), (0.15, 0.0008, 0.0025), "hole")
        box(g, (x, y + sy * 0.0145, -0.245), (0.15, 0.0008, 0.0025), "hole")
    # the window: a deep frame (the potatoes sit inside it), a bezel, rivets, ticks for the five stacks
    cx, cz, hx, hz = p.WIN_X, p.WIN_Z, p.WIN_HX, p.WIN_HZ
    f, depth = 0.026, 0.017
    fy = y + sy * depth
    for s in (-1, 1):
        box(g, (cx, fy, cz + s * (hz + f / 2)), (hx + f, depth, f / 2), "gun", bevel=0.006)
        box(g, (cx + s * (hx + f / 2), fy, cz), (f / 2, depth, hz), "gun", bevel=0.006)
        box(g, (cx + s * (hx + 0.004), y + sy * 2 * depth, cz), (0.004, 0.003, hz + 0.004), "gun_dark", bevel=0.0012)
        box(g, (cx, y + sy * 2 * depth, cz + s * (hz + 0.004)), (hx + 0.008, 0.003, 0.004), "gun_dark", bevel=0.0012)
    box(g, (cx, y + sy * 0.0015, cz), (hx, 0.0015, hz), "backing")
    for dx in (-hx - 0.013, hx + 0.013):
        for dz in (-hz - 0.013, hz + 0.013):
            dome(g, (cx + dx, y + sy * 2 * depth, cz + dz), n, 0.005, "bolt")
    for k in range(1, 5):
        x = cx - hx + 2 * hx * k / 5
        box(g, (x, y + sy * (2 * depth + 0.0008), cz + hz - 0.007), (0.0018, 0.0006, 0.007), "tick")
    # a mustard tab on the window's top frame
    box(g, (cx, y + sy * (2 * depth + 0.0005), cz + hz + f / 2 + 0.002), (0.032, 0.0012, 0.0065), "mustard")


def potatoes(sy, y):
    p = Potato
    g = Geo()
    rng = np.random.default_rng(77 if sy < 0 else 78)
    spots = [(-0.055, -0.035), (0.0, -0.038), (0.058, -0.033), (-0.03, 0.012), (0.03, 0.015), (-0.066, 0.03), (0.07, 0.03),
             (0.0, 0.05)]
    for i, (dx, dz) in enumerate(spots):
        a = rng.uniform(-0.6, 0.6)
        r = (0.03 * rng.uniform(0.9, 1.1), 0.016, 0.026 * rng.uniform(0.9, 1.1))
        rot = Matrix.Rotation(a, 3, "Y")
        g.extend(blob((p.WIN_X + dx, y + sy * 0.008, p.WIN_Z + dz), r, 300 + i + (0 if sy < 0 else 50),
                      "potato" if i % 3 else "potato_dark", rot=rot))
    return g


def potato_lid(g):
    p = Potato
    z0, z1 = p.Z_LID, p.LZ1
    zc, hz = (z0 + z1) / 2, (z1 - z0) / 2
    box(g, (0, 0, zc), (p.LX - 0.012, p.LY - 0.012, hz - 0.006), "gun_dark", bevel=0.006)
    # rails round the lid's top edges and corner caps
    for sx in (-1, 1):
        box(g, (sx * (p.LX - 0.022), 0, z1 - 0.022), (0.022, p.LY - 0.06, 0.022), "gun", bevel=0.008)
    for sy in (-1, 1):      # the long sides' top edges: a low lip, so the stencil band shows full height below it
        box(g, (0, sy * (p.LY - 0.03), z1 - 0.008), (p.LX - 0.06, 0.03, 0.008), "gun", bevel=0.005)
        rivets(g, (-p.LX + 0.08, sy * (p.LY - 0.03), z1), (p.LX - 0.08, sy * (p.LY - 0.03), z1), 10, (0, 0, 1), r=0.005)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (p.LX - 0.04), sy * (p.LY - 0.04), zc), (0.04, 0.04, hz), "gun", bevel=0.014)
            hexnut(g, (sx * (p.LX - 0.04), sy * (p.LY - 0.04), z1), (0, 0, 1), 0.009, 0.005, "bolt")
            for n in ((sx, 0, 0), (0, sy, 0)):
                hexnut(g, Vector((sx * (p.LX - 0.04), sy * (p.LY - 0.04), zc)) + Vector(n) * 0.04, n, 0.008, 0.004, "bolt")
    # the raised top panel with a seam and rivets
    box(g, (0, 0, z1 - 0.004), (p.LX - 0.085, p.LY - 0.085, 0.008), "gun", bevel=0.005)
    box(g, (0, 0, z1 + 0.002), (p.LX - 0.11, p.LY - 0.11, 0.004), "plate", bevel=0.003)
    for x in (-0.16, 0.16):
        box(g, (x, 0, z1 + 0.0062), (0.002, p.LY - 0.13, 0.0008), "hole")
    # the mustard stencil band round the lid: text on the long sides, plain on the ends
    bz, bhz = zc - 0.006, 0.036
    for sy in (-1, 1):
        y = sy * (p.LY - 0.012)
        box(g, (0, y + sy * 0.004, bz), (p.LX - 0.08, 0.004, bhz), "mustard", bevel=0.002)
        decal_quad(g, y + sy * 0.0083, sy, -0.19, 0.19, bz - 0.03, bz + 0.03, "decal_stencil")
    for sx in (-1, 1):
        box(g, (sx * (p.LX - 0.008), 0, bz), (0.004, p.LY - 0.08, bhz), "mustard", bevel=0.002)
    # the seam between lid and body, and a rubber seal line
    box(g, (0, 0, z0 + 0.003), (p.BX - 0.01, p.BY - 0.004, 0.004), "hole")


def latch(g, x):
    """A copper lever latch on the front: a hinge bracket on the body, a lever over the band, a catch on the lid."""
    p = Potato
    yb = -p.SIDE                       # body side face
    # bracket on the body
    box(g, (x, yb - 0.023, p.Z_LID - 0.115), (0.034, 0.023, 0.02), "copper", bevel=0.005)
    cylinder(g, (x - 0.036, -(p.LY + 0.006), p.Z_LID - 0.112), (x + 0.036, -(p.LY + 0.006), p.Z_LID - 0.112), 0.008, "steel", segs=14)
    # the lever, flat against the lid band, and its grip
    ly = -(p.LY + 0.012)
    box(g, (x, ly, p.Z_LID - 0.03), (0.024, 0.006, 0.075), "copper", bevel=0.004)
    box(g, (x, ly - 0.002, p.Z_LID + 0.02), (0.028, 0.006, 0.016), "copper", bevel=0.005)
    box(g, (x, ly - 0.0065, p.Z_LID - 0.05), (0.012, 0.0008, 0.03), "hole")
    # the catch on the lid
    box(g, (x, -(p.LY + 0.004), p.Z_LID + 0.055), (0.03, 0.007, 0.018), "copper", bevel=0.004)
    for dx in (-0.02, 0.02):
        hexnut(g, (x + dx, -(p.LY + 0.011), p.Z_LID + 0.055), (0, -1, 0), 0.0055, 0.003, "bolt")


def hinges(g):
    p = Potato
    for x in (-0.25, 0.25):
        y = p.LY + 0.004
        box(g, (x, p.SIDE + 0.012, p.Z_LID - 0.03), (0.055, 0.008, 0.03), "gun", bevel=0.004)
        box(g, (x, p.LY - 0.002, p.Z_LID + 0.035), (0.055, 0.008, 0.03), "gun", bevel=0.004)
        cylinder(g, (x - 0.06, y, p.Z_LID), (x + 0.06, y, p.Z_LID), 0.014, "steel", segs=18)
        for dx in (-0.035, 0.035):
            dome(g, (x + dx, p.SIDE + 0.02, p.Z_LID - 0.035), (0, 1, 0), 0.005, "bolt")
            dome(g, (x + dx, p.LY + 0.006, p.Z_LID + 0.045), (0, 1, 0), 0.005, "bolt")


def end_handle(g, sx):
    """A carry handle on an end of the lid (above the IO port): copper brackets, a steel U bar held out flat, a copper
    grip; and the body's inset end panel."""
    p = Potato
    x0 = sx * (p.LX - 0.002)
    z = (p.Z_LID + p.LZ1) / 2 - 0.004
    # an inset end panel first
    box(g, (sx * (p.BX - 0.002), 0, (p.Z_BOT + p.Z_LID) / 2), (0.004, p.BY - 0.07, (p.Z_LID - p.Z_BOT) / 2 - 0.07), "plate", bevel=0.003)
    for y in (-0.13, 0.13):
        box(g, (x0 + sx * 0.012, y, z), (0.012, 0.022, 0.026), "copper", bevel=0.005)
        hexnut(g, (x0 + sx * 0.012, y + (0.022 if y > 0 else -0.022), z), (0, 1 if y > 0 else -1, 0), 0.007, 0.004, "bolt")
    r = 0.012
    xo = sx * 0.479
    pts = [Vector((x0 + sx * 0.012, -0.13, z))]
    bend = 0.03
    for k in range(0, 7):
        a = math.pi / 2 * k / 6
        pts.append(Vector((xo - sx * bend * (1 - math.sin(a)), -0.13 + bend * (1 - math.cos(a)), z)))
    for k in range(0, 7):
        a = math.pi / 2 * k / 6
        pts.append(Vector((xo - sx * bend * (1 - math.cos(a)), 0.13 - bend * (1 - math.sin(a)), z)))
    pts.append(Vector((x0 + sx * 0.012, 0.13, z)))
    # drop duplicates at the joins
    clean = [pts[0]]
    for q in pts[1:]:
        if (q - clean[-1]).length > 1e-5:
            clean.append(q)
    sweep(g, clean, r, "steel", segs=14)
    cylinder(g, (xo, -0.075, z), (xo, 0.075, z), r + 0.005, "copper", segs=18)
    for yy in (-0.075, 0.075):
        cylinder(g, (xo, yy - 0.005, z), (xo, yy + 0.005, z), r + 0.006, "copper", segs=18)


def build_potato(mats):
    p = Potato
    root = bl.make_empty("PotatoAmmoContainer")
    g = Geo()
    case(g, p.BX, p.BY, p.Z_BOT, p.Z_LID)
    for sy in (-1, 1):
        potato_side(g, sy)
    potato_lid(g)
    for x in p.LATCH_X:
        latch(g, x)
    hinges(g)
    for sx in (-1, 1):
        end_handle(g, sx)
    gc.port_block(g, p.BX)
    bl.make_object("Housing", g, mats, root)
    gc.mount_io_port(root)
    spuds, glass = Geo(), Geo()
    for sy in (-1, 1):
        spuds.extend(potatoes(sy, sy * p.SIDE))
        glass_pane(glass, sy * (p.SIDE + 0.031), p.WIN_X, p.WIN_Z, p.WIN_HX + 0.004, p.WIN_HZ + 0.004)
    bl.make_object("Potatoes", spuds, mats, root)
    bl.make_object("Glass", glass, mats, root)
    return root


BUILDERS = {"chemical_container": build_chemical, "water_container": build_water, "potato_ammo_container": build_potato}


# =====================================================================================================
# Checks
# =====================================================================================================

def glb_info(path):
    """World bounds (glTF axes, node transforms applied) and triangle count, read back from the .glb's JSON."""
    data = path.read_bytes()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glb"
    ln, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0

    def walk(i, parent):
        nonlocal lo, hi, tris
        node = doc["nodes"][i]
        t = Matrix.Translation(node.get("translation", (0, 0, 0)))
        q = node.get("rotation", (0, 0, 0, 1))
        r = Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4()
        s = Matrix.Diagonal((*node.get("scale", (1, 1, 1)), 1))
        m = parent @ t @ r @ s
        if "mesh" in node:
            for prim in doc["meshes"][node["mesh"]]["primitives"]:
                acc = doc["accessors"][prim["attributes"]["POSITION"]]
                a, b = acc["min"], acc["max"]
                for cx in (a[0], b[0]):
                    for cy in (a[1], b[1]):
                        for cz in (a[2], b[2]):
                            p = np.asarray(m @ Vector((cx, cy, cz)))
                            lo, hi = np.minimum(lo, p), np.maximum(hi, p)
                tris += doc["accessors"][prim["indices"]]["count"] // 3
        for c in node.get("children", []):
            walk(c, m)

    for i in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        walk(i, Matrix.Identity(4))
    return {"min": lo, "max": hi, "size": hi - lo, "tris": tris, "kib": len(data) / 1024,
            "images": len([im for im in doc.get("images", []) if "bufferView" in im]), "materials": len(doc.get("materials", []))}


def check_inside(info, name):
    limit = np.array([HALF_X, HALF_Z, HALF_Y])   # glTF: X, Y up (Blender Z), Z (Blender -Y)
    bad = (np.abs(info["min"]) > limit + 1e-4).any() or (np.abs(info["max"]) > limit + 1e-4).any()
    if bad:
        print(f"WARNING: {name} pokes outside the 1.0 x 0.75 x 0.75 m box")
    return not bad


# =====================================================================================================
# Main
# =====================================================================================================

ICON_VIEW = gc.ICON_VIEW         # as the Gas Container's icon: from the -Y front, the +X end on the right
ICON_ORTHO = gc.ICON_ORTHO


def tree(root):
    return bbe.mesh_tree(root)


def main():
    bbe.reset_scene()
    mats = make_materials()
    roots = {cid: BUILDERS[cid](mats) for cid in IDS}
    for cid, root in roots.items():
        path = MESHES / f"{cid}.glb"
        bbe.export(root, path, animations=False)
        i = glb_info(path)
        ok = check_inside(i, cid)
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['materials']} materials, "
              f"{i['images']} images) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()} "
              f"size {np.round(i['size'], 4).tolist()} {'inside the box' if ok else 'OUTSIDE THE BOX'}")
    for cid, root in roots.items():
        icon = ICONS / f"{cid}_icon.png"
        bbe.render_icon(tree(root), icon, view=ICON_VIEW, ortho=ICON_ORTHO, target=(0, 0, -0.01))
        print(f"wrote {icon.relative_to(REPO)} ({icon.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_consumables
        render_consumables.render_all(roots, mats, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
