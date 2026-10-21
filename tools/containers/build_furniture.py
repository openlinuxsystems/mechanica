"""Build the nine furniture-style storage containers and their icons (box placeholders until 2026-10-21).

The Gas Container's approach (tools/gas_container/build_gas_container.py): procedural meshes from bevelled boxes,
cylinders, lathed profiles, rivets and bolts, worn tileable textures from the Lift's texture kit, Blender as a Python
module. The families and palettes are docs/art-direction.md's (and tools/structures/families.py's):

* Colony Works furniture (bone ``#D9D1BC``, steel blue ``#3E6A8C``, graphite trim ``#2B2F35``, mustard ``#E0B530``),
  framed like the colony's buildings: steel-blue rails round bone panels, graphite corner caps and plinths, pressed
  louvres, cyan only on what still has power:

  - ``fridge`` 4 x 7 x 3 blocks: a tall two-door fridge, bar handles, header vent grille, a cyan status light, the
    condenser coil on the back.
  - ``locker`` 2 x 7 x 2: a steel-blue locker, pressed vent slots, a name card, a latch with a padlock hasp.
  - ``file_cabinet`` 2 x 5 x 3: four bone drawers with pulls and label cards in a steel-blue case.
  - ``barrel`` 2 x 4 x 2: a steel drum with rolled hoops and chimes, two bungs and a faded stencil.
  - ``metal_crate`` 3 x 3 x 3: a ribbed steel shipping crate, graphite corner castings, a mustard band, latches.
  - ``dispenser`` 3 x 6 x 2: a supply locker: a glass-fronted, lit upper bay with stock on its shelves, a control
    strip with a cyan screen, and a dispensing hatch.

* Salvager Tech (hull ``#24282E``, gunmetal ``#5A6068``, copper ``#B8733A``), the part models' framed cases:

  - ``tool_cabinet`` 3 x 4 x 2: a signal-red (``#C8423A``) rolling tool chest: gunmetal rails and corner blocks,
    five drawers with bar pulls, a lidded top tray, a side push bar, four casters.
  - ``broken_microwave`` 2 x 2 x 2: the SV-04's galley microwave, dead: a bone casing, its door hanging open off a
    broken top hinge with a cracked window, a broken turntable, a dead screen, soot round the cavity.

* Settler Patchwork: ``wooden_crate`` 3 x 3 x 3, a slatted crate of mismatched scrap wood (``#9C7650``), one patched
  slat in faded sea teal, a diagonal brace, zinc corner brackets, nails and a stencilled outpost code.

Every container is centred on the origin and stays inside its box (sizes in 0.25 m blocks, X wide x Y tall x Z
deep). In Blender Z is up and the front faces -Y; the exporter turns that into glTF's +Y up with the front towards +Z.
Each model is one mesh, ``Body``, under a root empty, with no animation.

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png`` (256 x 256 on a
clear background, three-quarter from the front right, framed as the Gas Container's icon).

    pip install bpy==5.2.2 numpy pillow
    python tools/containers/build_furniture.py                   # writes the .glb files and the icons
    python tools/containers/build_furniture.py --render OUT_DIR  # also renders review sheets and a lineup
    python tools/containers/build_furniture.py --only fridge,locker   # just these (for iterating)

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import itertools
import json
import math
import random
import struct
import sys
from pathlib import Path

import bpy  # noqa: F401  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # its imports put the bearing_engine and lift helpers on the path

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, dome, hexnut, sweep = bl.Geo, bl.box, bl.cylinder, bl.dome, bl.hexnut, bl.sweep
lathe = bbe.lathe
BLOCK = 0.25

# palette (docs/art-direction.md)
BONE, STEEL, GRAPHITE, MUSTARD, SIGNAL = "#d9d1bc", "#3e6a8c", "#2b2f35", "#e0b530", "#c8423a"
HULL, GUNMETAL, COPPER, CYAN = "#24282e", "#5a6068", "#b8733a", "#38c8ff"
WOOD, SEA, ZINC, WARM = "#9c7650", "#4f9a93", "#9aa3ab", "#fff1cc"
UNDER = "#4a4e54"           # bare metal under chipped paint


# =====================================================================================================
# Materials
# =====================================================================================================

def wood_surface(seed, color):
    """Scrap wood: grain stretched along u, darker growth lines, knots, grime and scratches."""
    grain = lt.fnoise(seed, 90.0, aniso=(0.05, 1.0))
    broad = lt.fnoise(seed + 1, 18.0, aniso=(0.12, 1.0))
    yy = np.mgrid[0:lt.SIZE, 0:lt.SIZE][0]
    lines = np.sin((yy / lt.SIZE * 2 * math.pi * 22) + 4.0 * lt.fbm(seed + 2, 3.0)) ** 8
    knots = np.clip((lt.fnoise(seed + 3, 10.0) - 0.62) * 6, 0, 1)
    grime = np.clip(lt.fbm(seed + 4, 2.5) * 1.4 - 0.1, 0, 1)
    sc = lt.scratches(seed + 5, 90, bias=0.0)
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.16 * grain[..., None] + 0.12 * broad[..., None])
    albedo = albedo * (1 - 0.32 * lines[..., None]) * (1 - 0.5 * knots[..., None]) * (1 - 0.3 * grime[..., None])
    albedo = albedo + 0.18 * sc[..., None] * (0.75 - albedo)
    height = 0.6 * grain - 0.5 * lines - 0.4 * knots - 0.4 * sc
    rough = 0.78 + 0.1 * grime - 0.05 * grain
    return albedo, height, rough, np.zeros((lt.SIZE, lt.SIZE))


def paint_surface(seed, color, under, chip=0.62):
    """The Lift's chipped paint (lift_textures.paint_surface) with fewer, smaller chips: furniture paint is worn at
    the edges and scratched, not flaked off in big patches (`chip` is the noise level where the paint gives way)."""
    a_m, h_m, r_m, m_m = lt.metal_surface(seed + 100, under, 0.45, 0.75, wear=0.6)
    chips = lt.fbm(seed, 6.0) + 0.35 * lt.fnoise(seed + 1, 60.0)
    chip_mask = np.clip((chips - chip) * 14, 0, 1)
    sc = lt.scratches(seed + 2, 140, bias=0.0)
    dirt = np.clip(lt.fbm(seed + 3, 2.5) * 1.5 - 0.25, 0, 1)
    paint = lt.srgb(color)[None, None, :] * (1 + 0.05 * lt.fbm(seed + 4, 8.0)[..., None]) * (1 - 0.18 * dirt[..., None])
    paint = paint + 0.22 * sc[..., None] * (0.85 - paint)
    m = np.maximum(chip_mask, sc * 0.5)[..., None]
    albedo = paint * (1 - m) + a_m * m
    height = 0.6 * (1 - m[..., 0]) + 0.1 * h_m
    roughness = (0.5 + 0.12 * dirt) * (1 - m[..., 0]) + r_m * m[..., 0]
    metallic = m_m * m[..., 0]
    return albedo, height, roughness, metallic


def paint(name, seed, color, under=UNDER, normal=0.5, chip=0.62):
    return lt.textured(name, gc.half_res(paint_surface(seed, color, under, chip)), normal_strength=normal)


def metal(name, seed, color, rough, met, wear=0.6, edge=0.3):
    return lt.textured(name, gc.half_res(lt.metal_surface(seed, color, rough, met, wear=wear, edge_light=edge)), normal_strength=0.7)


def make_materials():
    plain = pt.plain
    return {
        # Colony Works
        "bone": paint("cf_bone", 901, BONE),
        "bone_b": paint("cf_bone_b", 907, "#d3cab3"),
        "steel": paint("cf_steel", 911, STEEL),
        "steel_b": paint("cf_steel_b", 917, "#3a6585"),
        "mustard": paint("cf_mustard", 921, MUSTARD),
        "graphite": metal("cf_graphite", 931, GRAPHITE, 0.55, 0.45, wear=0.25, edge=0.15),
        "signal": paint("cf_signal", 941, SIGNAL),
        "card": plain("cf_card", "#e6dfca", 0.85, 0.0),
        "ink": plain("cf_ink", "#33363b", 0.8, 0.0),
        "faded": plain("cf_faded", "#8499a6", 0.85, 0.0),           # bone stencil paint mostly worn off steel blue
        "faded_dark": plain("cf_faded_dark", "#3c3732", 0.9, 0.0),
        # Salvager Tech
        "gun": metal("cf_gun", 951, GUNMETAL, 0.46, 0.75, wear=0.4),
        "hull": metal("cf_hull", 953, HULL, 0.5, 0.65, wear=0.5, edge=0.25),
        "copper": metal("cf_copper", 955, COPPER, 0.34, 0.95, wear=0.6, edge=0.35),
        "chrome": metal("cf_chrome", 957, "#a9adb2", 0.28, 0.95, wear=0.5),
        "bolt": metal("cf_bolt", 959, "#3c3f43", 0.45, 0.8, wear=0.5),
        "rubber": lt.textured("cf_rubber", gc.half_res(lt.rubber_surface(961)), normal_strength=1.5),
        "bone_tech": paint("cf_bone_tech", 963, BONE, under=HULL),
        # Settler Patchwork
        "wood": lt.textured("cf_wood", gc.half_res(wood_surface(971, WOOD)), normal_strength=1.2),
        "wood_b": lt.textured("cf_wood_b", gc.half_res(wood_surface(973, "#8a6644")), normal_strength=1.2),
        "wood_c": lt.textured("cf_wood_c", gc.half_res(wood_surface(975, "#a9845a")), normal_strength=1.2),
        "sea": paint("cf_sea", 977, "#6f9e98", under="#8a6644"),
        "zinc": metal("cf_zinc", 979, ZINC, 0.45, 0.8, wear=1.0),
        "rust": lt.textured("cf_rust", gc.half_res(pt.rust_surface(981, "#7a4a30", "#4a2a18", "#3d3a36")), normal_strength=0.8),
        # shared
        "hole": plain("cf_hole", "#0d0e0f", 0.9, 0.2),
        "dark": plain("cf_dark", "#1b1d20", 0.6, 0.3),
        "glass": gc.glass_material(),
        "screen": plain("cf_screen", "#0b0f11", 0.12, 0.0),
        "crack": plain("cf_crack", "#c8d2d4", 0.35, 0.0),
        # scorch marks in four rings, from smoke-stained bone at the rim to soot in the middle
        "soot_0": plain("cf_soot_0", "#b3a68e", 0.85, 0.0),
        "soot_1": plain("cf_soot_1", "#7d6e5c", 0.88, 0.0),
        "soot_2": plain("cf_soot_2", "#463b31", 0.92, 0.0),
        "soot": plain("cf_soot", "#1d1a17", 0.95, 0.0),
        "cyan": plain("cf_cyan", "#7fe0ff", 0.3, 0.0, emission=CYAN, strength=6.0),
        "light": plain("cf_light", "#fff6dd", 0.3, 0.0, emission=WARM, strength=5.0),
        "lamp_dead": plain("cf_lamp_dead", "#3a1d1a", 0.3, 0.0),
    }


# =====================================================================================================
# Shapes
# =====================================================================================================

class Face:
    """A frame on one face: (a, b) across it (u right, v up as seen from outside) and `out` along its normal."""

    def __init__(self, origin, u, v):
        self.o, self.u, self.v = Vector(origin), Vector(u).normalized(), Vector(v).normalized()
        self.n = self.u.cross(self.v)
        self.rot = Matrix((self.u, self.v, self.n)).transposed()

    def p(self, a, b, out=0.0):
        return self.o + self.u * a + self.v * b + self.n * out

    def box(self, g, a, b, ha, hb, t, mat, bevel=0.0, out=0.0, spin=0.0, tilt=0.0):
        """A box ha x hb across, t thick from `out` outward; spin turns it in the face, tilt about its u axis."""
        rot = self.rot @ Matrix.Rotation(spin, 3, "Z") @ Matrix.Rotation(tilt, 3, "X")
        box(g, tuple(self.p(a, b, out + t / 2)), (ha, hb, t / 2), mat, bevel=bevel, rot=rot)

    def dome(self, g, a, b, r, mat, out=0.0, segs=8):
        dome(g, self.p(a, b, out), self.n, r, mat, segs=segs)

    def nut(self, g, a, b, r, h, mat, out=0.0):
        hexnut(g, self.p(a, b, out), self.n, r, h, mat)

    def cyl(self, g, a, b, r, t, mat, out=0.0, segs=24, r1=None):
        cylinder(g, self.p(a, b, out), self.p(a, b, out + t), r, mat, segs=segs, r1=r1)

    def stencil(self, g, text, a, b, px, mat, out=0.0, t=0.0008, gap=1):
        for (x0, x1, row), w, h in glyph_runs(text, gap):
            ca = a + ((x0 + x1) / 2 - w / 2 + 0.5) * px
            cb = b + (h / 2 - row - 0.5) * px
            self.box(g, ca, cb, (x1 - x0 + 1) * px / 2, px / 2, t, mat, out=out)

    def soot(self, g, a, b, r, seed, out=0.0, squash=1.0):
        """A soft scorch mark: a brown rim round a black core, lying on the face."""
        rng = np.random.default_rng(seed)
        n = 28
        for k, (mat, scale) in enumerate((("soot_0", 1.0), ("soot_1", 0.78), ("soot_2", 0.56), ("soot", 0.34))):
            wob = rng.uniform(0.6, 1.35, n)
            wob = (wob + np.roll(wob, 1) + np.roll(wob, -1)) / 3
            ca, cb = a + rng.uniform(-0.1, 0.1) * r * k * 0.3, b + rng.uniform(-0.1, 0.1) * r * k * 0.3
            lift = 0.0003 * (k + 1)
            ring_pts = [self.p(ca + r * scale * wob[i] * math.cos(2 * math.pi * i / n),
                               cb + r * scale * squash * wob[i] * math.sin(2 * math.pi * i / n), out + lift) for i in range(n)]
            verts = [self.p(ca, cb, out + lift), *ring_pts]
            g.add(verts, [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)], mat)

    def groove(self, g, a, b, ha, hb, out=0.0, w=0.0016):
        """A dark pressed seam round a rectangle (a raised panel's outline)."""
        for s in (-1, 1):
            self.box(g, a, b + s * hb, ha + w, w, 0.0006, "hole", out=out)
            self.box(g, a + s * ha, b, w, hb + w, 0.0006, "hole", out=out)

    def louvres(self, g, a, b, ha, count, pitch, mat_lip, out=0.0, hb=0.0065):
        """Pressed vent slots: a dark slot under a lip bent out of the sheet, `count` rows downward from b."""
        for k in range(count):
            bb = b - k * pitch
            self.box(g, a, bb, ha, hb, 0.0012, "hole", out=out)
            self.box(g, a, bb + hb * 0.4, ha + 0.004, hb * 0.9, 0.003, mat_lip, bevel=0.0012, out=out + 0.0008,
                     tilt=math.radians(-28))


def edge_frame(g, c, half, t, mat, bevel, corner=0.0, cmat=None, cbevel=0.01, bolts=None, skip=()):
    """Bars along the twelve edges of the box (c, half), `t` half thick, and corner blocks `corner` half size."""
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0, s1 in itertools.product((-1, 1), repeat=2):
            if (axis, s0, s1) in skip:
                continue
            p = list(c)
            p[others[0]] += s0 * (half[others[0]] - t)
            p[others[1]] += s1 * (half[others[1]] - t)
            h = [t, t, t]
            h[axis] = half[axis] - (corner * 0.9 if corner else 0.0)
            box(g, p, h, mat, bevel=bevel)
    if corner:
        for sx, sy, sz in itertools.product((-1, 1), repeat=3):
            p = Vector((c[0] + sx * (half[0] - corner), c[1] + sy * (half[1] - corner), c[2] + sz * (half[2] - corner)))
            box(g, p, (corner, corner, corner), cmat, bevel=cbevel)
            if bolts:
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                    dome(g, p + Vector(n) * corner, n, corner * 0.28, bolts)


def hinge(g, p, length, r, mat, leaf=None):
    """A vertical barrel hinge at p: two knuckles and a pin cap."""
    p = Vector(p)
    cylinder(g, p - Vector((0, 0, length / 2)), p - Vector((0, 0, 0.002)), r, mat, segs=12)
    cylinder(g, p + Vector((0, 0, 0.002)), p + Vector((0, 0, length / 2)), r, mat, segs=12)
    cylinder(g, p + Vector((0, 0, length / 2)), p + Vector((0, 0, length / 2 + 0.004)), r * 0.7, mat, segs=12)


GLYPHS = {
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "F": ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "W": ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    "-": ["000", "000", "000", "111", "000", "000", "000"],
    " ": ["00", "00", "00", "00", "00", "00", "00"],
    "^": ["00100", "01110", "10101", "00100", "00100", "00100", "00100"],
    "*": ["0001000", "1001001", "0101010", "0011100", "0101010", "1001001", "0001000"],   # snowflake
    "#": ["1111111", "1100011", "1010101", "1001001", "1010101", "1100011", "1111111"],   # crate
}


def glyph_runs(text, gap=1):
    """Horizontal pixel runs (x0, x1, row) of `text` in the pixel font, with the text's width and height in pixels."""
    runs, x = [], 0
    for ch in text:
        rows = GLYPHS[ch]
        for r, line in enumerate(rows):
            c = 0
            while c < len(line):
                if line[c] == "1":
                    c1 = c
                    while c1 + 1 < len(line) and line[c1 + 1] == "1":
                        c1 += 1
                    runs.append((x + c, x + c1, r))
                    c = c1 + 1
                else:
                    c += 1
        x += len(rows[0]) + gap
    w = x - gap
    return [(run, w, 7) for run in runs]


def stencil_round(g, text, angle, z, radius, px, mat, t=0.0008, gap=1, keep=1.0, seed=0):
    """The pixel font round a vertical drum (centred at `angle`, reading left to right from outside); `keep` is the
    share of pixels whose paint is left (the rest has worn off)."""
    rng = random.Random(seed)
    for (x0, x1, row), w, h in glyph_runs(text, gap):
        for x in range(x0, x1 + 1):
            if rng.random() > keep:
                continue
            a = angle + (x - w / 2 + 0.5) * px / radius
            n = Vector((math.cos(a), math.sin(a), 0))
            u = Vector((-math.sin(a), math.cos(a), 0))
            rot = Matrix((u, Vector((0, 0, 1)), n)).transposed()
            c = n * (radius + t / 2) + Vector((0, 0, z + (h / 2 - row - 0.5) * px))
            box(g, tuple(c), (px / 2 + 0.0002, px / 2, t / 2), mat, rot=rot)


def cracks(face, g, a, b, rng, count, length, out, width=0.0007):
    """Cracks radiating from (a, b) on a face: thin jagged strips."""
    for k in range(count):
        ang = 2 * math.pi * k / count + rng.uniform(-0.3, 0.3)
        pa, pb = a, b
        segs = int(rng.integers(3, 6))
        for s in range(segs):
            step = length * rng.uniform(0.6, 1.2) / segs
            ang += rng.uniform(-0.45, 0.45)
            qa, qb = pa + step * math.cos(ang), pb + step * math.sin(ang)
            face.box(g, (pa + qa) / 2, (pb + qb) / 2, step / 2 + width, width, 0.0006, "crack", out=out,
                     spin=math.atan2(qb - pb, qa - pa))
            pa, pb = qa, qb
    face.cyl(g, a, b, 0.006, 0.0007, "crack", out=out, segs=10)


# =====================================================================================================
# Fridge (Colony Works), 4 x 7 x 3
# =====================================================================================================

def build_fridge():
    g = Geo()
    front, back, z0, z1 = -0.335, 0.365, -0.795, 0.86
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (0.485, (back - front) / 2, (z1 - z0) / 2)
    # plinth with a kick grille
    box(g, (0, 0.025, -0.835), (0.465, 0.335, 0.04), "graphite", bevel=0.006)
    kick = Face((0, -0.31, -0.835), (1, 0, 0), (0, 0, 1))
    kick.box(g, 0, 0, 0.3, 0.026, 0.002, "hole")
    for k in range(4):
        kick.box(g, 0, 0.018 - k * 0.012, 0.3, 0.0035, 0.006, "graphite", bevel=0.0015, tilt=math.radians(-30))
    # cabinet: bone panels in steel-blue rails, graphite corner caps
    box(g, c, (h[0] - 0.01, h[1] - 0.01, h[2] - 0.01), "bone", bevel=0.006)
    edge_frame(g, c, h, 0.021, "steel", 0.006, corner=0.03, cmat="graphite", cbevel=0.01, bolts="bolt")
    for sx in (-1, 1):
        side = Face((sx * (h[0] - 0.01), c[1], c[2]), (0, sx, 0), (0, 0, 1))
        # (b is from the cabinet's centre, c[2] above the origin): a panel over the band, one under it
        side.box(g, 0, 0.335 - c[2], h[1] - 0.065, 0.465, 0.004, "bone_b", bevel=0.003)
        side.box(g, 0, -0.53 - c[2], h[1] - 0.065, 0.23, 0.004, "bone_b", bevel=0.003)
        side.box(g, 0, -0.24 - c[2], h[1] - 0.03, 0.045, 0.006, "steel", bevel=0.003)
        for a in (-h[1] + 0.06, h[1] - 0.06):
            side.dome(g, a, -0.24 - c[2], 0.006, "bolt", out=0.006)
    # back: condenser coil on a dark grille
    rear = Face((0, back - 0.01, c[2]), (-1, 0, 0), (0, 0, 1))
    rear.box(g, 0, 0.05, 0.38, 0.62, 0.003, "dark")
    for k in range(18):
        b = -0.53 + k * 1.16 / 17
        rear.box(g, 0, b, 0.34, 0.0055, 0.011, "graphite", bevel=0.004, out=0.003)
    for a in (-0.36, -0.12, 0.12, 0.36):
        rear.box(g, a, 0.05, 0.005, 0.6, 0.004, "bolt", out=0.014)
    rear.box(g, 0, -0.7, 0.2, 0.06, 0.015, "graphite", bevel=0.005)
    # header: a graphite strip with the vent grille, the snowflake and the cyan status light
    f = Face((0, front + 0.01, 0), (1, 0, 0), (0, 0, 1))
    f.box(g, 0, 0.775, 0.465, 0.068, 0.012, "graphite", bevel=0.005)
    f.louvres(g, -0.1, 0.81, 0.28, 6, 0.015, "graphite", out=0.012, hb=0.0045)
    f.box(g, 0.33, 0.775, 0.085, 0.045, 0.004, "dark", bevel=0.002, out=0.012)
    f.stencil(g, "*", 0.29, 0.775, 0.0075, "bone", out=0.016)
    f.cyl(g, 0.375, 0.775, 0.017, 0.005, "bolt", out=0.016, segs=20)
    f.dome(g, 0.375, 0.775, 0.012, "cyan", out=0.021, segs=16)
    for a in (-0.445, 0.445):
        f.dome(g, a, 0.775, 0.006, "bolt", out=0.012)
    # doors
    dz, dh = (0.69 - 0.775) / 2, (0.69 + 0.775) / 2
    f.box(g, 0, dz, 0.47, dh + 0.005, 0.002, "dark")                        # gasket line between the doors
    for s in (-1, 1):
        a = s * 0.2345
        f.box(g, a, dz, 0.2305, dh, 0.02, "bone", bevel=0.008, out=0.002)
        f.box(g, a, 0.335, 0.19, 0.3, 0.003, "bone_b", bevel=0.003, out=0.022)
        f.box(g, a, -0.52, 0.19, 0.2, 0.003, "bone_b", bevel=0.003, out=0.022)
        f.groove(g, a, 0.335, 0.196, 0.306, out=0.022)
        f.groove(g, a, -0.52, 0.196, 0.206, out=0.022)
        f.box(g, a, -0.24, 0.2305 - 0.002, 0.045, 0.003, "steel", bevel=0.002, out=0.022)
        for da in (-0.2, 0.2):
            f.dome(g, a + da, -0.24, 0.005, "bolt", out=0.025)
        # bar handle on the inner edge
        ah = s * 0.036
        for b in (0.03, 0.4):
            f.box(g, ah, b, 0.008, 0.012, 0.01, "graphite", bevel=0.003, out=0.022)
        f.box(g, ah, 0.215, 0.0095, 0.215, 0.009, "graphite", bevel=0.004, out=0.031)
        # hinges on the outer edge
        for b in (0.6, -0.69):
            f.box(g, s * 0.44, b, 0.022, 0.03, 0.002, "graphite", out=0.022)
            hinge(g, f.p(s * 0.468, b, 0.013), 0.064, 0.0075, "graphite")
    # a mustard model plate on the right door, the left door's inventory card
    f.box(g, 0.31, 0.56, 0.06, 0.022, 0.003, "mustard", bevel=0.0015, out=0.025)
    f.stencil(g, "CW-04", 0.31, 0.56, 0.0042, "ink", out=0.028)
    f.box(g, -0.31, 0.53, 0.07, 0.05, 0.002, "graphite", bevel=0.001, out=0.025)
    f.box(g, -0.31, 0.53, 0.062, 0.042, 0.0008, "card", out=0.027)
    for k in range(4):
        f.box(g, -0.32 + 0.006 * (k % 2), 0.555 - k * 0.017, 0.042 - 0.008 * (k % 3), 0.0022, 0.0004, "ink", out=0.0278)
    # top: a small vent and a bolted service plate
    top = Face((0, c[1], z1 - 0.01), (1, 0, 0), (0, 1, 0))
    top.box(g, 0, 0.12, 0.3, 0.1, 0.004, "bone_b", bevel=0.003)
    for a, b in itertools.product((-0.28, 0.28), (0.04, 0.2)):
        top.dome(g, a, b, 0.006, "bolt", out=0.004)
    return g


# =====================================================================================================
# Locker (Colony Works), 2 x 7 x 2
# =====================================================================================================

def build_locker():
    g = Geo()
    front, back, z0, z1 = -0.215, 0.245, -0.815, 0.835
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (0.245, (back - front) / 2, (z1 - z0) / 2)
    box(g, (0, 0.015, -0.845), (0.235, 0.225, 0.03), "graphite", bevel=0.005)         # plinth
    box(g, c, h, "steel", bevel=0.006)
    box(g, (0, c[1] - 0.003, 0.855), (0.25, h[1] + 0.008, 0.02), "graphite", bevel=0.006)   # top cap
    for sx in (-1, 1):                                                                # pressed side ribs
        side = Face((sx * h[0], c[1], c[2]), (0, sx, 0), (0, 0, 1))
        for b in (0.45, -0.1, -0.55):
            side.box(g, 0, b, h[1] - 0.05, 0.012, 0.002, "steel_b", bevel=0.0015)
    for sx in (-1, 1):                                                               # steel corner posts at the front
        box(g, (sx * 0.233, front + 0.006, c[2]), (0.012, 0.009, h[2] - 0.002), "steel_b", bevel=0.004)
    f = Face((0, front, 0), (1, 0, 0), (0, 0, 1))
    # door frame lip and the door
    f.box(g, 0, 0.01, 0.235, 0.815, 0.002, "dark")
    dz, dh = 0.01, 0.8
    f.box(g, 0, dz, 0.225, dh, 0.012, "steel_b", bevel=0.005, out=0.002)
    f.box(g, 0, 0.12, 0.185, 0.52, 0.002, "steel", bevel=0.002, out=0.014)            # pressed panel
    f.box(g, 0, -0.62, 0.185, 0.12, 0.002, "steel", bevel=0.002, out=0.014)
    f.louvres(g, 0, 0.76, 0.14, 6, 0.024, "steel_b", out=0.014)
    f.louvres(g, 0, -0.56, 0.14, 5, 0.024, "steel_b", out=0.014)
    # name card in a graphite holder
    f.box(g, 0, 0.47, 0.075, 0.03, 0.003, "graphite", bevel=0.0015, out=0.016)
    f.box(g, 0, 0.47, 0.066, 0.022, 0.001, "card", out=0.019)
    f.box(g, -0.012, 0.476, 0.045, 0.0028, 0.0004, "ink", out=0.020)
    f.box(g, -0.022, 0.465, 0.035, 0.0022, 0.0004, "ink", out=0.020)
    for a in (-0.071, 0.071):
        f.dome(g, a, 0.47, 0.0035, "bolt", out=0.019)
    # mustard number tag under the card
    f.box(g, 0, 0.405, 0.032, 0.018, 0.002, "mustard", bevel=0.001, out=0.016)
    f.stencil(g, "14", 0, 0.405, 0.0035, "ink", out=0.018)
    # latch: a plate, a lift lever, a hasp with a padlock
    la, lb = 0.165, 0.02
    f.box(g, la, lb, 0.03, 0.1, 0.003, "graphite", bevel=0.0015, out=0.014)
    f.box(g, la, lb + 0.02, 0.011, 0.055, 0.007, "chrome", bevel=0.003, out=0.017)
    f.cyl(g, la, lb + 0.065, 0.011, 0.006, "chrome", out=0.017, segs=16)
    f.box(g, la, lb - 0.055, 0.016, 0.008, 0.004, "graphite", bevel=0.0015, out=0.017)
    f.box(g, la, lb - 0.08, 0.013, 0.013, 0.007, "mustard", bevel=0.004, out=0.016)    # padlock body
    f.cyl(g, la, lb - 0.083, 0.0035, 0.0015, "hole", out=0.023, segs=10)
    sweep(g, [f.p(la - 0.008, lb - 0.068, 0.0195), f.p(la - 0.008, lb - 0.058, 0.0195), f.p(la - 0.004, lb - 0.052, 0.0195),
              f.p(la + 0.004, lb - 0.052, 0.0195), f.p(la + 0.008, lb - 0.058, 0.0195), f.p(la + 0.008, lb - 0.068, 0.0195)],
          0.0022, "chrome", segs=8)
    for b in (0.65, 0.0, -0.65):
        hinge(g, f.p(-0.232, b, 0.009), 0.07, 0.0065, "graphite")
    for a, b in itertools.product((-0.205, 0.205), (0.79, -0.77)):
        f.dome(g, a, b, 0.004, "bolt", out=0.014)
    return g


# =====================================================================================================
# File cabinet (Colony Works), 2 x 5 x 3
# =====================================================================================================

def build_file_cabinet():
    g = Geo()
    front, back, z0, z1 = -0.345, 0.37, -0.59, 0.6
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (0.245, (back - front) / 2, (z1 - z0) / 2)
    box(g, (0, 0.015, -0.607), (0.23, 0.35, 0.018), "graphite", bevel=0.004)
    box(g, c, h, "steel", bevel=0.006)
    box(g, (0, c[1] - 0.003, 0.6125), (0.25, h[1] + 0.006, 0.0125), "graphite", bevel=0.005)
    for sx in (-1, 1):
        side = Face((sx * h[0], c[1], c[2]), (0, sx, 0), (0, 0, 1))
        side.box(g, 0, 0, h[1] - 0.04, h[2] - 0.04, 0.002, "steel_b", bevel=0.002)
        for b in (0.3, -0.3):
            side.box(g, 0, b, h[1] - 0.06, 0.01, 0.002, "steel", bevel=0.001, out=0.002)
    f = Face((0, front, 0), (1, 0, 0), (0, 0, 1))
    f.box(g, 0, 0.005, 0.232, 0.588, 0.002, "dark")
    top, bottom, gap = 0.585, -0.575, 0.008
    dh = (top - bottom - 3 * gap) / 4
    for k in range(4):
        hi = top - k * (dh + gap)
        b = hi - dh / 2
        f.box(g, 0, b, 0.228, dh / 2, 0.014, "bone" if k % 2 == 0 else "bone_b", bevel=0.006, out=0.002)
        f.box(g, 0, b, 0.2, dh / 2 - 0.028, 0.002, "bone_b" if k % 2 == 0 else "bone", bevel=0.002, out=0.016)
        # label card holder
        lb = hi - 0.055
        f.box(g, 0, lb, 0.055, 0.022, 0.003, "graphite", bevel=0.0015, out=0.018)
        f.box(g, 0, lb - 0.001, 0.047, 0.015, 0.001, "card", out=0.021)
        f.box(g, -0.008, lb + 0.003, 0.03, 0.0025, 0.0004, "ink", out=0.022)
        f.box(g, -0.016 + 0.006 * k, lb - 0.006, 0.022, 0.002, 0.0004, "ink", out=0.022)
        # pull: a graphite bar on two posts
        pb = lb - 0.06
        for a in (-0.06, 0.06):
            f.box(g, a, pb, 0.008, 0.009, 0.008, "graphite", bevel=0.003, out=0.016)
        f.box(g, 0, pb - 0.002, 0.075, 0.009, 0.007, "graphite", bevel=0.003, out=0.022)
    # lock cylinder in the top trim's front
    f.cyl(g, 0.17, 0.555, 0.011, 0.004, "chrome", out=0.016, segs=18)
    f.box(g, 0.17, 0.555, 0.0015, 0.006, 0.001, "hole", out=0.0195)
    return g


# =====================================================================================================
# Tool cabinet (Salvager Tech), 3 x 4 x 2
# =====================================================================================================

def caster(g, x, y, spin):
    """A swivel caster under the cabinet: a plate, a fork and a rubber wheel (bottom at z -0.5)."""
    zt = -0.405
    box(g, (x, y, zt - 0.004), (0.032, 0.032, 0.004), "gun", bevel=0.002)
    cylinder(g, (x, y, zt - 0.008), (x, y, zt - 0.018), 0.016, "bolt", segs=16)
    rot = Matrix.Rotation(spin, 3, "Z")
    for s in (-1, 1):
        off = rot @ Vector((s * 0.019, 0.008, 0))
        box(g, (x + off.x, y + off.y, -0.44), (0.003, 0.02, 0.025), "gun", bevel=0.002, rot=rot)
    axle = rot @ Vector((1, 0, 0))
    centre = Vector((x, y, -0.46)) + rot @ Vector((0, 0.01, 0))
    cylinder(g, centre - axle * 0.014, centre + axle * 0.014, 0.0395, "rubber", segs=24)
    cylinder(g, centre - axle * 0.0155, centre + axle * 0.0155, 0.018, "gun", segs=16)
    cylinder(g, centre - axle * 0.023, centre + axle * 0.023, 0.006, "bolt", segs=8)


def build_tool_cabinet():
    g = Geo()
    front, back, z0, z1 = -0.205, 0.235, -0.405, 0.44
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (0.33, (back - front) / 2, (z1 - z0) / 2)
    box(g, c, (h[0] - 0.008, h[1] - 0.008, h[2] - 0.008), "signal", bevel=0.005)
    edge_frame(g, (c[0], c[1] - 0.006, c[2]), (h[0], h[1] + 0.006, h[2]), 0.016, "gun", 0.006, corner=0.025,
               cmat="hull", cbevel=0.009, bolts="bolt")
    # lid: a gunmetal frame over a red panel with a rubber mat, copper piano hinge at the back
    box(g, (0, c[1] - 0.004, 0.467), (0.335, h[1] + 0.008, 0.027), "gun", bevel=0.008)
    box(g, (0, c[1] - 0.004, 0.4965), (0.305, h[1] - 0.022, 0.0025), "rubber", bevel=0.001)
    cylinder(g, (-0.3, 0.238, 0.44), (0.3, 0.238, 0.44), 0.009, "copper", segs=12)
    for sx in (-1, 1):
        for sy in (-1, 1):
            dome(g, (sx * 0.318, c[1] + sy * (h[1] - 0.006), 0.494), (0, 0, 1), 0.005, "bolt")
    f = Face((0, front, 0), (1, 0, 0), (0, 0, 1))
    # top tray front: a flap with the SV-04 plate and a copper hasp
    f.box(g, 0, 0.36, 0.3, 0.06, 0.012, "signal", bevel=0.005)
    f.box(g, -0.17, 0.36, 0.075, 0.03, 0.003, "gun", bevel=0.0015, out=0.012)
    f.stencil(g, "SV-04", -0.17, 0.36, 0.0052, "bone_tech", out=0.015)
    f.box(g, 0, 0.43, 0.022, 0.014, 0.008, "copper", bevel=0.003, out=0.006)
    f.box(g, 0, 0.405, 0.013, 0.02, 0.006, "copper", bevel=0.002, out=0.012)
    f.box(g, 0.17, 0.36, 0.08, 0.011, 0.003, "hull", bevel=0.001, out=0.012)
    for a in (0.11, 0.17, 0.23):
        f.box(g, a, 0.36, 0.006, 0.004, 0.002, "hole", out=0.015)
    f.box(g, 0, 0.283, 0.314, 0.009, 0.006, "gun", bevel=0.003)
    # drawers: three shallow, a medium and a deep one, each with a full-width bar pull
    heights = (0.075, 0.075, 0.095, 0.13, 0.205)
    top, gap = 0.268, 0.006
    for k, dh in enumerate(heights):
        b = top - dh / 2
        f.box(g, 0, b, 0.304, dh / 2, 0.012, "signal", bevel=0.004)
        f.box(g, 0, b + dh / 2 - 0.016, 0.24, 0.0065, 0.007, "gun", bevel=0.003, out=0.012)
        f.box(g, 0, b + dh / 2 - 0.0225, 0.24, 0.001, 0.0005, "hole", out=0.012)
        for a in (-0.243, 0.243):
            f.box(g, a, b + dh / 2 - 0.016, 0.006, 0.008, 0.009, "copper", bevel=0.003, out=0.011)
        f.box(g, 0.26, b - 0.004, 0.022, 0.009, 0.001, "hull", out=0.012)              # little label slot
        top -= dh + gap
    f.box(g, 0, top + gap / 2 + 0.0, 0.31, 0.012, 0.008, "gun", bevel=0.003)
    # the -X side: an inset panel with vents; the +X side: the push bar
    left = Face((-h[0] + 0.008, c[1], c[2]), (0, -1, 0), (0, 0, 1))
    left.box(g, 0, 0.0, h[1] - 0.045, h[2] - 0.06, 0.004, "signal", bevel=0.003)
    for k in range(7):
        left.box(g, 0, 0.18 - k * 0.025, 0.12, 0.0055, 0.0014, "hole", out=0.004)
    for a, b in itertools.product((-0.15, 0.15), (0.32, -0.32)):
        left.dome(g, a, b, 0.005, "bolt", out=0.004)
    right = Face((h[0] - 0.008, c[1], c[2]), (0, 1, 0), (0, 0, 1))
    right.box(g, 0, -0.1, h[1] - 0.045, 0.22, 0.004, "signal", bevel=0.003)
    zb = 0.29
    for y in (-0.13, 0.16):
        box(g, (0.338, y, zb), (0.012, 0.016, 0.022), "copper", bevel=0.004)
        for s in (-1, 1):
            hexnut(g, (0.338, y + s * 0.016, zb), (0, s, 0), 0.005, 0.003, "bolt")
    pts = [Vector((0.347, -0.13, zb)), Vector((0.357, -0.126, zb)), Vector((0.36, -0.115, zb)),
           Vector((0.36, 0.145, zb)), Vector((0.357, 0.156, zb)), Vector((0.347, 0.16, zb))]
    sweep(g, pts, 0.011, "gun", segs=14)
    cylinder(g, (0.36, -0.07, zb), (0.36, 0.1, zb), 0.0135, "rubber", segs=14)
    # the casters
    for (x, y), spin in zip(itertools.product((-0.27, 0.27), (-0.13, 0.16)), (0.0, 0.25, -0.2, 0.0)):
        caster(g, x, y, spin)
    return g


# =====================================================================================================
# Barrel (Colony Works), 2 x 4 x 2
# =====================================================================================================

def build_barrel():
    g = Geo()
    r = 0.238
    prof = [(0.0, -0.486), (0.226, -0.486), (0.229, -0.497), (0.236, -0.5), (0.247, -0.497), (0.25, -0.489),
            (0.247, -0.48), (0.24, -0.474), (r, -0.468)]
    for zc in (-0.17, 0.17):                                     # rolling hoops
        prof += [(r, zc - 0.022), (0.243, zc - 0.014), (0.248, zc - 0.006), (0.249, zc), (0.248, zc + 0.006),
                 (0.243, zc + 0.014), (r, zc + 0.022)]
    for zc in (-0.4, 0.4):                                       # small beads near the chimes
        prof += [(r, zc - 0.01), (0.2415, zc - 0.004), (0.2415, zc + 0.004), (r, zc + 0.01)]
    prof += [(r, 0.468), (0.24, 0.474), (0.247, 0.48), (0.25, 0.489), (0.247, 0.497), (0.236, 0.5), (0.229, 0.497),
             (0.226, 0.486), (0.0, 0.486)]
    lathe(g, prof, "steel", steps=56)
    # bungs on the lid: a big one and a small vent one, each a flange and a ribbed cap
    for (x, y), rad in (((0.135, 0.05), 0.03), ((-0.14, 0.06), 0.019)):
        cylinder(g, (x, y, 0.486), (x, y, 0.49), rad + 0.012, "steel_b", segs=24)
        cylinder(g, (x, y, 0.49), (x, y, 0.496), rad, "zinc", segs=8, phase=math.pi / 8)
        box(g, (x, y, 0.4965), (rad * 0.75, 0.004, 0.0012), "hole")
    # lid seam and a dent of grime
    lathe(g, [(0.0, 0.4862), (0.2, 0.4862), (0.2, 0.4866), (0.0, 0.4866)], "steel_b", steps=40)
    # faded stencil: colony mark and code on the front (-Y), an up arrow beside it
    stencil_round(g, "CW-07", -math.pi / 2, 0.035, r, 0.0105, "faded", keep=0.92, seed=1)
    stencil_round(g, "^", -math.pi / 2 - 0.85, 0.035, r, 0.0105, "faded", keep=0.75, seed=2)
    stencil_round(g, "^", -math.pi / 2 + 0.85, 0.035, r, 0.0105, "faded", keep=0.75, seed=3)
    # a faded fill-line band between the lower hoop and bead, worn through in places
    lathe(g, [(r, -0.3), (r + 0.0008, -0.3), (r + 0.0008, -0.27), (r, -0.27)], "faded", a0=0.2, a1=2.3, steps=30)
    lathe(g, [(r, -0.3), (r + 0.0008, -0.3), (r + 0.0008, -0.27), (r, -0.27)], "faded", a0=2.6, a1=5.9, steps=40)
    # rust streaks run down from the top chime
    rnd = random.Random(8)
    for k in range(7):
        a = rnd.uniform(0, 2 * math.pi)
        n, u = Vector((math.cos(a), math.sin(a), 0)), Vector((-math.sin(a), math.cos(a), 0))
        ln = rnd.uniform(0.025, 0.055)                       # down to the bead under the chime
        rot = Matrix((u, Vector((0, 0, 1)), n)).transposed()
        box(g, tuple(n * (r + 0.0004) + Vector((0, 0, 0.465 - ln / 2))), (rnd.uniform(0.002, 0.005), ln / 2, 0.0004), "rust", rot=rot)
    return g


# =====================================================================================================
# Broken microwave (Salvager Tech, from the SV-04's galley), 2 x 2 x 2
# =====================================================================================================

def build_broken_microwave():
    g = Geo()
    rng = np.random.default_rng(404)
    front, back, z0, z1 = -0.13, 0.24, -0.235, 0.175
    x0, x1 = -0.238, 0.238
    cav_x0, cav_x1, cav_z0, cav_z1 = -0.205, 0.075, -0.2, 0.14
    for sx, sy in itertools.product((-1, 1), (-0.09, 0.19)):                       # feet
        cylinder(g, (sx * 0.2, sy, -0.25), (sx * 0.2, sy, z0), 0.016, "rubber", segs=12)
    # casing round the cavity: top, bottom, left wall, the control block on the right, the back
    box(g, (0, (front + back) / 2, (cav_z1 + z1) / 2), (x1, (back - front) / 2, (z1 - cav_z1) / 2), "bone_tech", bevel=0.006)
    box(g, (0, (front + back) / 2, (z0 + cav_z0) / 2), (x1, (back - front) / 2, (cav_z0 - z0) / 2), "bone_tech", bevel=0.006)
    box(g, ((x0 + cav_x0) / 2, (front + back) / 2, (cav_z0 + cav_z1) / 2), ((cav_x0 - x0) / 2, (back - front) / 2, (cav_z1 - cav_z0) / 2 + 0.002), "bone_tech")
    box(g, ((cav_x1 + x1) / 2, (front + back) / 2, (cav_z0 + cav_z1) / 2), ((x1 - cav_x1) / 2, (back - front) / 2, (cav_z1 - cav_z0) / 2 + 0.002), "bone_tech")
    box(g, ((cav_x0 + cav_x1) / 2, back - 0.02, (cav_z0 + cav_z1) / 2), ((cav_x1 - cav_x0) / 2, 0.02, (cav_z1 - cav_z0) / 2), "bone_tech")
    # cavity liner: dark gunmetal walls, soot, a mica cover and the broken turntable
    cd = back - 0.04 - front
    cy = front + 0.006 + cd / 2
    box(g, ((cav_x0 + cav_x1) / 2, back - 0.0415, (cav_z0 + cav_z1) / 2), ((cav_x1 - cav_x0) / 2, 0.0015, (cav_z1 - cav_z0) / 2), "hull")
    box(g, ((cav_x0 + cav_x1) / 2, cy, cav_z0 + 0.0015), ((cav_x1 - cav_x0) / 2, cd / 2, 0.0015), "hull")
    box(g, ((cav_x0 + cav_x1) / 2, cy, cav_z1 - 0.0015), ((cav_x1 - cav_x0) / 2, cd / 2, 0.0015), "hull")
    box(g, (cav_x0 + 0.0015, cy, (cav_z0 + cav_z1) / 2), (0.0015, cd / 2, (cav_z1 - cav_z0) / 2), "hull")
    box(g, (cav_x1 - 0.0015, cy, (cav_z0 + cav_z1) / 2), (0.0015, cd / 2, (cav_z1 - cav_z0) / 2), "hull")
    inner_r = Face((cav_x1 - 0.003, cy, 0), (0, -1, 0), (0, 0, 1))
    inner_r.box(g, 0.04, 0.07, 0.035, 0.03, 0.001, "gun")                                # waveguide cover
    inner_r.soot(g, 0.04, 0.06, 0.06, 11, out=0.0012)
    Face((0, back - 0.043, 0), (1, 0, 0), (0, 0, 1)).soot(g, -0.06, 0.0, 0.09, 12, out=0.0)
    Face((0, cy, cav_z1 - 0.003), (1, 0, 0), (0, -1, 0)).soot(g, -0.06, 0.0, 0.08, 13, out=0.0)
    tc = Vector(((cav_x0 + cav_x1) / 2, cy + 0.01, cav_z0 + 0.003))
    cylinder(g, tc, tc + Vector((0, 0, 0.012)), 0.012, "gun", segs=12)
    plate = Geo()
    lathe(plate, [(0.0, 0.0), (0.105, 0.0), (0.11, 0.008), (0.104, 0.008), (0.1, 0.004), (0.0, 0.004)], "glass",
          a0=0.3, a1=math.pi + 0.1, steps=20)
    g.extend(plate, Matrix.Translation(tc + Vector((0, 0, 0.012))) @ Matrix.Rotation(math.radians(9), 4, "X"))
    shard = Geo()
    lathe(shard, [(0.03, 0.0), (0.105, 0.0), (0.11, 0.008), (0.104, 0.008), (0.1, 0.004), (0.03, 0.004)], "glass",
          a0=math.pi + 0.5, a1=math.pi + 1.6, steps=8)
    g.extend(shard, Matrix.Translation(tc + Vector((0.025, -0.02, -0.009))) @ Matrix.Rotation(math.radians(-4), 4, "Y"))
    # Salvager framing: gunmetal rails on the front and back rims, hull corner blocks
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (x1, (back - front) / 2, (z1 - z0) / 2)
    edge_frame(g, c, (h[0] + 0.004, h[1] + 0.004, h[2] + 0.004), 0.011, "gun", 0.004, corner=0.018, cmat="hull", cbevel=0.006, bolts="bolt")
    f = Face((0, front, 0), (1, 0, 0), (0, 0, 1))
    # the control panel: a hull inset with a dead cracked screen, a keypad, a dial and a dead lamp
    pa = (cav_x1 + x1) / 2 + 0.004
    f.box(g, pa, -0.03, 0.068, 0.18, 0.004, "hull", bevel=0.003)
    f.box(g, pa, 0.105, 0.052, 0.03, 0.002, "screen", bevel=0.001, out=0.004)
    cracks(f, g, pa + 0.018, 0.112, rng, 5, 0.035, out=0.0062, width=0.0005)
    for i, j in itertools.product(range(3), range(4)):
        missing = (i, j) in ((2, 1), (0, 3))
        f.box(g, pa - 0.03 + i * 0.03, 0.045 - j * 0.026, 0.011, 0.008, 0.002 if missing else 0.005,
              "hole" if missing else "gun", bevel=0.0 if missing else 0.002, out=0.004)
    f.cyl(g, pa, -0.12, 0.024, 0.006, "gun", out=0.004, segs=24)
    f.cyl(g, pa, -0.12, 0.018, 0.012, "hull", out=0.01, segs=18)
    f.box(g, pa, -0.108, 0.002, 0.008, 0.002, "bone_tech", out=0.022, spin=0.6)
    f.dome(g, pa + 0.045, -0.17, 0.006, "lamp_dead", out=0.004, segs=10)
    f.stencil(g, "SV-04", pa - 0.012, -0.17, 0.0035, "bone_tech", out=0.004)
    # soot blown out of the cavity: on the casing above the opening and up the front of the top
    f.soot(g, -0.07, 0.158, 0.07, 21, out=0.0, squash=0.16)
    top = Face((0, (front + back) / 2, z1), (1, 0, 0), (0, 1, 0))
    top.soot(g, -0.07, -0.125, 0.11, 22, out=0.0, squash=0.32)
    # vents on the right side and the back, a cut power cord at the back
    right = Face((x1, c[1], c[2]), (0, 1, 0), (0, 0, 1))
    for k in range(8):
        right.box(g, 0.03 + k * 0.022, 0.08, 0.006, 0.05, 0.0012, "hole")
    rear = Face((0, back, c[2]), (-1, 0, 0), (0, 0, 1))
    for k in range(6):
        rear.box(g, -0.1 + k * 0.022, 0.07, 0.006, 0.04, 0.0012, "hole")
    rear.box(g, -0.15, -0.12, 0.02, 0.015, 0.008, "hull", bevel=0.003)
    cord = [rear.p(-0.15, -0.135, 0.004), rear.p(-0.15, -0.15, 0.004), rear.p(-0.146, -0.175, 0.0042),
            rear.p(-0.135, -0.195, 0.0042), rear.p(-0.115, -0.2, 0.004)]
    sweep(g, cord, 0.0048, "rubber", segs=8)
    for k in range(3):
        sweep(g, [cord[-1], cord[-1] + Vector((0.009 + 0.002 * k, -0.002, -0.004 + 0.004 * k))], 0.0016,
              ("copper", "copper", "bolt")[k], segs=5)
    # the door, hanging open off its bottom hinge: frame, cracked window, handle
    door = Geo()
    dw, dh, dt = 0.29, 0.335, 0.022
    df = Face((0, 0, 0), (1, 0, 0), (0, 0, 1))
    for (a, b, ha, hb) in ((dw / 2, 0.02, dw / 2, 0.02), (dw / 2, dh - 0.02, dw / 2, 0.02),
                           (0.022, dh / 2, 0.022, dh / 2), (dw - 0.03, dh / 2, 0.03, dh / 2)):
        df.box(door, a, b, ha, hb, dt, "bone_tech", bevel=0.004)
    wa, wb, wha, whb = (0.044 + dw - 0.06) / 2, dh / 2, (dw - 0.06 - 0.044) / 2, dh / 2 - 0.04
    df.box(door, wa, wb, wha, whb, 0.003, "screen", out=0.008)
    for k in range(9):                                                                              # mesh lines
        df.box(door, wa, wb - whb + 0.012 + k * (2 * whb - 0.024) / 8, wha - 0.01, 0.0007, 0.0004, "gun", out=0.011)
    df.box(door, wa, wb, wha + 0.002, whb + 0.002, 0.0015, "glass", out=0.016)
    cracks(df, door, wa + 0.03, wb + 0.04, rng, 7, 0.12, out=0.0175, width=0.0008)
    df.box(door, wa - 0.03, wb - 0.07, 0.025, 0.018, 0.0008, "hole", out=0.0176, spin=0.4)       # a missing shard
    for b in (0.06, dh - 0.06):
        df.box(door, dw - 0.03, b, 0.008, 0.01, 0.01, "gun", bevel=0.003, out=dt)
    df.box(door, dw - 0.03, dh / 2, 0.009, dh / 2 - 0.05, 0.009, "gun", bevel=0.004, out=dt + 0.01)
    df.soot(door, 0.05, dh - 0.02, 0.035, 25, out=dt, squash=0.45)
    cylinder(door, (0.0, -0.006, 0.0), (0.0, -0.006, 0.04), 0.005, "gun", segs=10)                # bottom hinge
    open_a, sag = math.radians(11), math.radians(5)
    hinge_p = Vector((x0 + 0.004, front - 0.003, cav_z0 - 0.005))
    g.extend(door, Matrix.Translation(hinge_p) @ Matrix.Rotation(-open_a, 4, "Z") @ Matrix.Rotation(sag, 4, "Y"))
    # the broken top hinge: a bent strap left on the casing
    box(g, tuple(hinge_p + Vector((0.0, -0.004, 0.33))), (0.006, 0.004, 0.018), "gun", bevel=0.002,
        rot=Matrix.Rotation(math.radians(25), 3, "Y"))
    return g


# =====================================================================================================
# Wooden crate (Settler Patchwork), 3 x 3 x 3
# =====================================================================================================

def build_wooden_crate():
    g = Geo()
    rnd = random.Random(77)
    H = 0.375
    woods = ("wood", "wood_b", "wood_c")
    box(g, (0, 0, 0), (H - 0.03, H - 0.03, H - 0.03), "hole")                       # the dark inside, seen through gaps
    # frame boards along the twelve edges
    t = 0.028
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0, s1 in itertools.product((-1, 1), repeat=2):
            p = [0.0, 0.0, 0.0]
            p[others[0]] = s0 * (H - t - 0.005)
            p[others[1]] = s1 * (H - t - 0.005)
            hh = [t, t, t]
            hh[axis] = H - 0.004 - (0.0 if axis == 2 else t)
            box(g, p, hh, rnd.choice(woods), bevel=0.004)
    # slats on the four sides and the lid: four boards each with gaps, one patched in faded teal
    faces = [Face((0, -(H - 0.012), 0), (1, 0, 0), (0, 0, 1)), Face((H - 0.012, 0, 0), (0, 1, 0), (0, 0, 1)),
             Face((0, H - 0.012, 0), (-1, 0, 0), (0, 0, 1)), Face((-(H - 0.012), 0, 0), (0, -1, 0), (0, 0, 1)),
             Face((0, 0, H - 0.012), (1, 0, 0), (0, 1, 0))]
    n, span = 4, 2 * (H - 2 * t)
    sh = (span - (n - 1) * 0.022) / n / 2
    for fi, f in enumerate(faces):
        for k in range(n):
            b = -span / 2 + sh + k * (2 * sh + 0.022)
            mat = "sea" if (fi, k) == (1, 2) else rnd.choice(woods)
            f.box(g, rnd.uniform(-0.004, 0.004), b, H - 2 * t + 0.004, sh * rnd.uniform(0.93, 1.0), 0.016, mat,
                  bevel=0.003, out=-0.008, spin=math.radians(rnd.uniform(-0.9, 0.9)))
            for a in (-(H - 2 * t - 0.015), H - 2 * t - 0.015):                      # nails at the slat ends
                for db in (-sh * 0.45, sh * 0.45):
                    f.dome(g, a + rnd.uniform(-0.006, 0.006), b + db, 0.0042, "bolt", out=0.008)
        if fi in (1, 3):                                                             # diagonal brace on the sides
            L = math.hypot(span, span) - 0.05
            f.box(g, 0, 0, L / 2, 0.03, 0.009, rnd.choice(woods), bevel=0.003, out=0.0, spin=math.pi / 4 * (1 if fi == 1 else -1))
            for s in (-1, 0, 1):
                d = s * 0.19
                f.dome(g, d, d * (1 if fi == 1 else -1), 0.0038, "bolt", out=0.009)
    # zinc corner brackets: three plates folded over each corner, nailed
    for sx, sy, sz in itertools.product((-1, 1), repeat=3):
        corner = Vector((sx * H, sy * H, sz * H))
        mat = "rust" if (sx, sy, sz) in ((1, -1, -1), (-1, 1, 1)) else "zinc"
        for axis, sgn in ((0, sx), (1, sy), (2, sz)):
            others = [i for i in range(3) if i != axis]
            c = list(corner)
            c[axis] = sgn * (H - 0.0035)
            for o in others:
                c[o] = corner[o] - [sx, sy, sz][o] * 0.04
            hh = [0.04, 0.04, 0.04]
            hh[axis] = 0.0015
            box(g, c, hh, mat, bevel=0.001)
            nrm = [0, 0, 0]
            nrm[axis] = sgn
            p = Vector(c)
            for o in others:
                p[o] = corner[o] - [sx, sy, sz][o] * 0.022
            dome(g, p - Vector(nrm) * 0.002, nrm, 0.003, "bolt")
    # stencil: outpost code on the front, up arrows on the sides, faded dark paint
    faces[0].stencil(g, "OP-7", 0, 0.06, 0.0125, "faded_dark", out=0.0085)
    faces[0].stencil(g, "^", -0.2, -0.17, 0.011, "faded_dark", out=0.0085)
    faces[1].stencil(g, "^", -0.2, 0.2, 0.011, "faded_dark", out=0.0085)
    return g


# =====================================================================================================
# Metal crate (Colony Works), 3 x 3 x 3
# =====================================================================================================

def build_metal_crate():
    g = Geo()
    H = 0.375
    core = H - 0.02
    box(g, (0, 0, 0), (core, core, core), "steel", bevel=0.006)
    edge_frame(g, (0, 0, 0), (H - 0.004, H - 0.004, H - 0.004), 0.02, "graphite", 0.006, corner=0.045,
               cmat="graphite", cbevel=0.012)
    # corner castings: an oval slot on each face
    for sx, sy, sz in itertools.product((-1, 1), repeat=3):
        cc = Vector((sx * (H - 0.049), sy * (H - 0.049), sz * (H - 0.049)))
        for axis, sgn in ((0, sx), (1, sy), (2, sz)):
            u = Vector((0, 0, 1)) if axis != 2 else Vector((1, 0, 0))
            nrm = Vector([sgn if i == axis else 0 for i in range(3)])
            fc = Face(cc + nrm * 0.045, u.cross(nrm), u)
            fc.box(g, 0, 0, 0.012, 0.019, 0.001, "hole", bevel=0.0)
    sides = [Face((0, -core, 0), (1, 0, 0), (0, 0, 1)), Face((core, 0, 0), (0, 1, 0), (0, 0, 1)),
             Face((0, core, 0), (-1, 0, 0), (0, 0, 1)), Face((-core, 0, 0), (0, -1, 0), (0, 0, 1))]
    span = core - 0.03
    for fi, f in enumerate(sides):
        for k in range(9):                                              # vertical ribs, broken by the band
            a = -span + 0.03 + k * (2 * span - 0.06) / 8
            for b, hb in ((0.2, 0.13), (-0.2, 0.13)):
                f.box(g, a, b, 0.013, hb, 0.009, "steel_b", bevel=0.004)
        f.box(g, 0, 0, span + 0.022, 0.055, 0.013, "mustard", bevel=0.004)  # the band
        for a in (-span + 0.01, span - 0.01):
            f.dome(g, a, 0.03, 0.005, "bolt", out=0.013)
            f.dome(g, a, -0.03, 0.005, "bolt", out=0.013)
        if fi == 0:
            f.stencil(g, "MC-3", 0, 0.0, 0.0095, "ink", out=0.013)
            for a in (-0.2, 0.2):                                       # latches over the band
                f.box(g, a, 0.075, 0.022, 0.016, 0.008, "graphite", bevel=0.003, out=0.009)
                f.box(g, a, 0.02, 0.012, 0.045, 0.004, "graphite", bevel=0.0015, out=0.013)
                f.box(g, a, -0.035, 0.02, 0.012, 0.004, "graphite", bevel=0.0015, out=0.013)
                f.cyl(g, a, 0.075, 0.004, 0.002, "bolt", out=0.017, segs=8)
            f.box(g, 0, 0.255, 0.075, 0.022, 0.0008, "faded", out=0.009)
            f.stencil(g, "CW", 0, 0.255, 0.006, "steel_b", out=0.0098)
        if fi == 2:
            for a in (-0.15, 0.15):                                     # lid hinges at the back
                f.box(g, a, core - 0.075, 0.035, 0.014, 0.008, "graphite", bevel=0.003, out=0.009)
        f.box(g, 0, core - 0.055, span + 0.015, 0.0015, 0.0012, "hole", out=0.0)     # the lid seam
    top = Face((0, 0, core), (1, 0, 0), (0, 1, 0))
    top.box(g, 0, 0, span - 0.02, span - 0.02, 0.004, "steel_b", bevel=0.003)
    for a in (-0.11, 0.11):
        top.box(g, a, 0, 0.018, span - 0.04, 0.008, "steel", bevel=0.004, out=0.004)
        top.box(g, 0, a, span - 0.04, 0.018, 0.007, "steel", bevel=0.004, out=0.004)
    return g


# =====================================================================================================
# Dispenser (Colony Works supply locker), 3 x 6 x 2
# =====================================================================================================

def build_dispenser():
    g = Geo()
    front, back, z0, z1 = -0.215, 0.245, -0.705, 0.73
    c, h = (0, (front + back) / 2, (z0 + z1) / 2), (0.36, (back - front) / 2, (z1 - z0) / 2)
    box(g, (0, 0.015, -0.7275), (0.345, 0.225, 0.0225), "graphite", bevel=0.005)
    bz0, bz1, ba, bdepth = 0.03, 0.57, 0.3, 0.2
    X, yb, zt, zb = h[0] - 0.008, front + bdepth, z1 - 0.008, z0 + 0.008
    for (xa, xb, ya, yc, za, zc) in ((-X, X, yb - 0.01, back - 0.008, zb, zt), (-X, X, front, yb, zb, bz0),
                                     (-X, X, front, yb, bz1, zt), (-X, -ba, front, yb, bz0, bz1), (ba, X, front, yb, bz0, bz1)):
        box(g, ((xa + xb) / 2, (ya + yc) / 2, (za + zc) / 2), ((xb - xa) / 2, (yc - ya) / 2, (zc - za) / 2), "steel")
    edge_frame(g, (c[0], c[1] - 0.006, c[2]), (h[0], h[1] + 0.006, h[2]), 0.017, "steel_b", 0.005, corner=0.026,
               cmat="graphite", cbevel=0.009, bolts="bolt")
    for sx in (-1, 1):
        side = Face((sx * (h[0] - 0.008), c[1], c[2]), (0, sx, 0), (0, 0, 1))
        side.box(g, 0, 0.3, h[1] - 0.05, 0.34, 0.004, "bone", bevel=0.003)
        side.box(g, 0, -0.4, h[1] - 0.05, 0.24, 0.004, "bone", bevel=0.003)
        side.louvres(g, 0, -0.25, 0.1, 6, 0.024, "bone_b", out=0.004)
    f = Face((0, front, 0), (1, 0, 0), (0, 0, 1))
    # header sign: bone plate with the crate pictogram, a cyan light bar
    f.box(g, 0, 0.64, 0.32, 0.05, 0.008, "bone", bevel=0.004)
    f.stencil(g, "#", -0.22, 0.64, 0.0085, "steel_b", out=0.008)
    f.stencil(g, "S-3", -0.09, 0.64, 0.0085, "ink", out=0.008)
    f.box(g, 0.19, 0.64, 0.09, 0.009, 0.003, "graphite", bevel=0.001, out=0.008)
    f.box(g, 0.19, 0.64, 0.082, 0.0045, 0.002, "cyan", out=0.011)
    # the glass-fronted bay: graphite frame, lit interior, two shelves of stock
    bf = f
    inside = Face((0, front + bdepth, 0), (1, 0, 0), (0, 0, 1))
    inside.box(g, 0, (bz0 + bz1) / 2, ba, (bz1 - bz0) / 2, 0.002, "bone_b")                    # back wall
    for s in (-1, 1):
        box(g, (s * (ba + 0.001), front + bdepth / 2, (bz0 + bz1) / 2), (0.002, bdepth / 2, (bz1 - bz0) / 2), "bone_b")
    box(g, (0, front + bdepth / 2, bz0 + 0.002), (ba, bdepth / 2, 0.002), "graphite")
    box(g, (0, front + bdepth / 2, bz1 - 0.002), (ba, bdepth / 2, 0.002), "graphite")
    box(g, (0, front + 0.04, bz1 - 0.012), (ba - 0.03, 0.01, 0.006), "light")                   # light bar
    shelf_z = (bz0 + 0.004, 0.3)
    box(g, (0, front + bdepth / 2, shelf_z[1]), (ba - 0.002, bdepth / 2 - 0.002, 0.006), "graphite", bevel=0.002)
    rnd = random.Random(31)
    # stock: ration boxes, cans, a medkit
    stock = [("box", -0.22, 0, "mustard", (0.05, 0.06, 0.07)), ("box", -0.13, 0, "mustard", (0.035, 0.06, 0.055)),
             ("can", -0.03, 0, "signal", (0.032, 0.0, 0.1)), ("can", 0.045, 0, "signal", (0.032, 0.0, 0.1)),
             ("box", 0.17, 0, "bone", (0.08, 0.06, 0.05)), ("box", 0.17, 0, "signal", (0.022, 0.0, 0.0)),
             ("can", -0.21, 1, "steel_b", (0.04, 0.0, 0.12)), ("can", -0.12, 1, "steel_b", (0.04, 0.0, 0.12)),
             ("box", 0.0, 1, "card", (0.06, 0.05, 0.035)), ("box", 0.0, 1, "card", (0.055, 0.045, 0.03)),
             ("box", 0.16, 1, "mustard", (0.07, 0.06, 0.09))]
    stack = {}
    for kind, x, shelf, mat, (a, b, hh) in stock:
        zb = shelf_z[shelf] + (0.002 if shelf == 0 else 0.006)
        y = front + bdepth / 2 + 0.01
        if kind == "can":
            cylinder(g, (x, y, zb), (x, y, zb + hh), a, mat, segs=20)
            cylinder(g, (x, y, zb + hh), (x, y, zb + hh + 0.004), a * 0.92, "chrome", segs=20)
            lathe_band = Geo()
            cylinder(lathe_band, (x, y, zb + hh * 0.35), (x, y, zb + hh * 0.7), a + 0.0008, "card", segs=20, cap0=False, cap1=False)
            g.extend(lathe_band)
        elif mat == "signal" and b == 0.0:                                                 # the medkit's cross
            top = stack[(x, shelf)]
            box(g, (x, y - 0.0605, top - 0.05), (a, 0.0008, 0.007), "signal")
            box(g, (x, y - 0.0605, top - 0.05), (0.007, 0.0008, a), "signal")
        else:
            zb = stack.get((x, shelf), zb)
            box(g, (x + rnd.uniform(-0.005, 0.005), y, zb + hh), (a, b, hh), mat, bevel=0.004,
                rot=Matrix.Rotation(math.radians(rnd.uniform(-6, 6)), 3, "Z"))
            stack[(x, shelf)] = zb + 2 * hh
    # the frame and the glass door
    fw = 0.03
    for (a, b, ha, hb) in ((0, bz1 + fw / 2, ba + fw, fw / 2), (0, bz0 - fw / 2, ba + fw, fw / 2),
                           (-ba - fw / 2, (bz0 + bz1) / 2, fw / 2, (bz1 - bz0) / 2), (ba + fw / 2, (bz0 + bz1) / 2, fw / 2, (bz1 - bz0) / 2)):
        bf.box(g, a, b, ha, hb, 0.014, "graphite", bevel=0.005)
    bf.box(g, 0, (bz0 + bz1) / 2, ba + 0.004, (bz1 - bz0) / 2 + 0.004, 0.0015, "glass", out=0.007)
    bf.box(g, 0.25, (bz0 + bz1) / 2 + 0.24, 0.035, 0.0012, 0.0004, "crack", out=0.0086, spin=0.7)   # a glint scratch
    bf.box(g, ba + fw / 2, 0.3, 0.007, 0.05, 0.008, "chrome", bevel=0.003, out=0.014)               # door pull
    for b in (bz0 + 0.06, bz1 - 0.06):
        hinge(g, bf.p(-ba - fw - 0.004, b, 0.01), 0.06, 0.006, "graphite")
    # control strip: a cyan screen, three buttons, a card slot
    f.box(g, 0, -0.06, 0.32, 0.05, 0.006, "graphite", bevel=0.003)
    f.box(g, -0.19, -0.06, 0.09, 0.032, 0.002, "screen", bevel=0.001, out=0.006)
    f.box(g, -0.19, -0.06, 0.082, 0.024, 0.0005, "cyan", out=0.008)
    for k, mat in enumerate(("mustard", "bone", "bone")):
        f.cyl(g, -0.04 + k * 0.045, -0.06, 0.014, 0.006, mat, out=0.006, segs=16)
    f.box(g, 0.2, -0.06, 0.06, 0.012, 0.004, "dark", bevel=0.0015, out=0.006)
    f.box(g, 0.2, -0.06, 0.045, 0.0018, 0.001, "hole", out=0.01)
    # the dispensing hatch: a recessed tray behind a top-hinged flap with a mustard pull
    hz = -0.34
    f.box(g, 0, hz, 0.24, 0.16, 0.01, "graphite", bevel=0.004)
    f.box(g, 0, hz, 0.21, 0.13, 0.002, "hole", out=0.01)
    f.box(g, 0, hz + 0.005, 0.205, 0.12, 0.008, "steel_b", bevel=0.004, out=0.01, tilt=math.radians(-6))
    f.box(g, 0, hz - 0.09, 0.08, 0.012, 0.009, "mustard", bevel=0.004, out=0.02)
    cylinder(g, f.p(-0.2, hz + 0.128, 0.016), f.p(0.2, hz + 0.128, 0.016), 0.006, "graphite", segs=10)
    f.stencil(g, "^", 0, hz + 0.04, 0.007, "bone", out=0.0185, gap=1)
    # kick plate with vents
    f.box(g, 0, -0.62, 0.32, 0.055, 0.004, "graphite", bevel=0.002)
    f.louvres(g, 0, -0.59, 0.25, 3, 0.022, "graphite", out=0.004)
    for a, b in itertools.product((-0.3, 0.3), (-0.585, -0.655)):
        f.dome(g, a, b, 0.0045, "bolt", out=0.004)
    return g


# =====================================================================================================
# Registry, export, checks
# =====================================================================================================

# id: (blocks X x Y x Z as the game sees them, builder, root name)
CONTAINERS = {
    "fridge": ((4, 7, 3), build_fridge, "Fridge"),
    "locker": ((2, 7, 2), build_locker, "Locker"),
    "file_cabinet": ((2, 5, 3), build_file_cabinet, "FileCabinet"),
    "tool_cabinet": ((3, 4, 2), build_tool_cabinet, "ToolCabinet"),
    "barrel": ((2, 4, 2), build_barrel, "Barrel"),
    "broken_microwave": ((2, 2, 2), build_broken_microwave, "BrokenMicrowave"),
    "wooden_crate": ((3, 3, 3), build_wooden_crate, "WoodenCrate"),
    "metal_crate": ((3, 3, 3), build_metal_crate, "MetalCrate"),
    "dispenser": ((3, 6, 2), build_dispenser, "Dispenser"),
}


def half_size(cid):
    """The part's half sizes in Blender axes (X wide, Y deep, Z tall), metres."""
    (x, y, z), _, _ = CONTAINERS[cid]
    return (x * BLOCK / 2, z * BLOCK / 2, y * BLOCK / 2)


def build(cid, mats):
    _, fn, name = CONTAINERS[cid]
    root = bl.make_empty(name)
    bl.make_object(f"{name}Body", fn(), mats, root)
    return root


def glb_info(path):
    """Bounds (glTF axes), triangle count and embedded image count, read back from the .glb (as build_rocks.py)."""
    data = path.read_bytes()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glb"
    ln, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    assert all(not n.get("translation") and not n.get("rotation") and not n.get("scale") for n in doc["nodes"]), \
        "nodes must sit at the origin, so the accessor bounds are the model's"
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            lo, hi = np.minimum(lo, acc["min"]), np.maximum(hi, acc["max"])
            tris += doc["accessors"][prim["indices"]]["count"] // 3
    images = [im for im in doc.get("images", []) if "bufferView" in im]
    return {"min": lo, "max": hi, "tris": tris, "images": len(images), "materials": len(doc.get("materials", [])),
            "kib": len(data) / 1024}


def icon_framing(cid):
    """The Gas Container icon's view, with the ortho scale that frames this part's box as that icon frames its own."""
    hx, hy, hz = half_size(cid)
    v = Vector(gc.ICON_VIEW).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(v)
    pts = [Vector((sx * hx, sy * hy, sz * hz)) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    w = max(p.dot(side) for p in pts) - min(p.dot(side) for p in pts)
    hh = max(p.dot(up) for p in pts) - min(p.dot(up) for p in pts)
    return gc.ICON_VIEW, 1.14 * max(w, hh)       # 1.14: the Gas Container icon's ortho over its box's extent


def main():
    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1].split(",")
    ids = [c for c in CONTAINERS if only is None or c in only]
    bbe.reset_scene()
    mats = make_materials()
    roots = {cid: build(cid, mats) for cid in ids}
    for cid in ids:
        path = MESHES / f"{cid}.glb"
        bbe.export(roots[cid], path, animations=False)
        i = glb_info(path)
        lim = np.array(half_size(cid))[[0, 2, 1]]             # glTF axes: X, Y up, Z front
        ok = bool(np.all(i["min"] >= -lim - 1e-4) and np.all(i["max"] <= lim + 1e-4))
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['materials']} materials, "
              f"{i['images']} images) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()} "
              f"{'inside' if ok else 'OUTSIDE'} +-{np.round(lim, 4).tolist()}")
    for cid in ids:
        view, ortho = icon_framing(cid)
        icon = ICONS / f"{cid}_icon.png"
        bbe.render_icon(bbe.mesh_tree(roots[cid]), icon, view=view, ortho=ortho, target=(0, 0, 0))
        print(f"wrote {icon.relative_to(REPO)}")
    if "--render" in sys.argv:
        import render_furniture
        render_furniture.render_all(roots, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
