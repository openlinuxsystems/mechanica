"""Build the item models and icons of consumables batches C2 and C3 (consumables/CONSUMABLES_MAP.md, items 1 to 3, 7, 8).

What it builds (procedurally, from scratch: no .blend inputs, no downloads), each a 1 x 1 x 1 block item standing on
z = -0.125 inside the 0.25 m cube (front -Y, Z up):

* ``meshes/items/bloom_tea.glb`` - a Colony Works glass bottle of rose-red Bloom Tea: a steel-blue screw cap, a bone
  wrap label in steel-blue bands with a five-petal bloom pictogram and BT-1, and on its back a steaming cup.
* ``meshes/items/grazer_shake.glb`` - a tall Colony Works shake bottle: a bone plastic body with a grip waist, a
  steel-blue sleeve with a big Grazer head pictogram over a milk splash and GS-1, a mustard sports cap with a flip spout.
* ``meshes/items/fertilizer.glb`` - a Rootwell stand-up pouch (the garden brand of the Soil Bag, tools/soil/bag_print.py):
  bright yellow, the green Rootwell band with the sprout badge across the top, FERTILIZER in big green letters, a
  seedling rising out of soil with up chevrons and pale granules; a crimped top seal with a tear notch, a zip ridge.
* ``meshes/items/grazer_milk.glb`` (batch C3, item 8) - an old-fashioned Colony Works glass milk bottle full of milk:
  a crimped steel-blue foil cap with a mustard pull tab, a bone label in steel-blue bands with the Grazer in a roundel
  between two milk drops and GM-1, and on its back the heal mark and a cup.
* ``meshes/items/grazer_steak.glb`` (batch C3, item 7) - a Settler Patchwork butcher's parcel: a raw marbled steak with
  a fat cap on kraft butcher paper in a pressed zinc tray, the paper's far end folded over it and tied with brick-red
  twine, and a kraft tag hand-stamped with the Grazer.
* ``meshes/items/<id>_held.glb`` for each: the same model, its base at the origin, which the game puts in the hand.
* ``textures/items/<id>_icon.png`` for each: 256 x 256.
* the ``held`` offset and rotation of each, printed (they go in mods/vanilla/editor/batches/consumables.json).

    pip install bpy==5.2.2 numpy pillow
    python tools/consumables/build_consumables.py [bloom_tea] [grazer_shake] [fertilizer] [grazer_milk] [grazer_steak]
    python tools/consumables/render_consumables.py OUT_DIR    # review sheets (model, icon, held in first person)

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "tools" / "materials"))
sys.path.insert(0, str(REPO / "tools" / "soil"))
sys.path.insert(0, str(REPO / "tools" / "rocks"))
sys.path.insert(0, str(REPO / "tools" / "food"))
sys.path.insert(0, str(REPO / "tools" / "characters"))

import build_materials as bm  # noqa: E402,I001  (bpy first, through it)
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

import bag_print as bp  # noqa: E402

bbe, bl, lt, pt, gc = bm.bbe, bm.bl, bm.lt, bm.pt, bm.gc
Geo, box, cylinder, dome, lathe, wrap = bl.Geo, bl.box, bl.cylinder, bl.dome, bm.lathe, bm.wrap
MESHES, ICONS = bm.MESHES, bm.ICONS
FLOOR = bm.FLOOR

# Palettes (docs/art-direction.md: Colony Works products, and the Rootwell garden brand of the Soil Bag)
HULL, BONE, STEEL_BLUE, MUSTARD, GRAPHITE = bm.HULL, bm.BONE, bm.STEEL_BLUE, bm.MUSTARD, bm.GRAPHITE
TEA = "#A8324A"                # Bloom Tea: a rose-red flower tea
PETAL, PETAL_DARK = (214, 74, 120), (150, 40, 82)
LEAF = (78, 150, 70)
SHAKE = "#EDE3C8"              # the shake's bone plastic, a little creamier than the labels
GRAZER_SLATE, GRAZER_CREAM, GRAZER_PINK = (92, 104, 118), (236, 226, 200), (226, 150, 160)

# Where the fist holds each item in hand, from its base up (m): drinks round the body, the pouch by its top seal.
GRIP = {"bloom_tea": 0.095, "grazer_shake": 0.105, "fertilizer": 0.205, "grazer_milk": 0.09, "grazer_steak": 0.008}
# How far the item's axis sits from the fist's centre toward the fingers (m): a bottle is fatter than a handle.
GRIP_OUT = {"bloom_tea": 0.012, "grazer_shake": 0.016, "fertilizer": 0.0, "grazer_milk": 0.016, "grazer_steak": 0.0}


def rgb(hex_color):
    return bm.rgb255(hex_color)


def cw_mark(d, cx, cy, r, ink, paper):
    """The Colony Works maker's mark: a hexagon with a cog notch and a C in it (a pictogram, no words)."""
    pts = [(cx + r * math.cos(math.pi / 6 + k * math.pi / 3), cy + r * math.sin(math.pi / 6 + k * math.pi / 3)) for k in range(6)]
    d.polygon(pts, fill=ink)
    d.ellipse([cx - r * 0.55, cy - r * 0.55, cx + r * 0.55, cy + r * 0.55], fill=paper)
    d.ellipse([cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3], fill=ink)
    d.rectangle([cx + r * 0.05, cy - r * 0.16, cx + r * 0.62, cy + r * 0.16], fill=ink)


def heart_plus(d, cx, cy, s, fill, mark):
    """A heart with a plus: heals."""
    d.ellipse([cx - s, cy - s * 0.7, cx, cy + s * 0.3], fill=fill)
    d.ellipse([cx, cy - s * 0.7, cx + s, cy + s * 0.3], fill=fill)
    d.polygon([(cx - s * 0.97, cy - s * 0.05), (cx + s * 0.97, cy - s * 0.05), (cx, cy + s * 1.05)], fill=fill)
    w = s * 0.14
    d.rectangle([cx - w, cy - s * 0.45, cx + w, cy + s * 0.35], fill=mark)
    d.rectangle([cx - s * 0.4, cy - w - 0.05 * s, cx + s * 0.4, cy + w - 0.05 * s], fill=mark)


# =====================================================================================================================
# Bloom Tea
# =====================================================================================================================

def bloom(d, cx, cy, r):
    """The bloom pictogram: five round rose petals about a mustard heart, two leaves under it."""
    green = LEAF
    for side in (-1, 1):
        d.polygon([(cx, cy + r * 0.9), (cx + side * r * 1.25, cy + r * 1.55), (cx + side * r * 0.35, cy + r * 1.7)], fill=green)
    d.rectangle([cx - r * 0.06, cy + r * 0.6, cx + r * 0.06, cy + r * 1.9], fill=green)
    for k in range(5):
        a = -math.pi / 2 + k * 2 * math.pi / 5
        px, py = cx + math.cos(a) * r * 0.58, cy + math.sin(a) * r * 0.58
        pr = r * 0.5
        d.ellipse([px - pr, py - pr, px + pr, py + pr], fill=PETAL, outline=PETAL_DARK, width=max(2, int(r * 0.05)))
    d.ellipse([cx - r * 0.34, cy - r * 0.34, cx + r * 0.34, cy + r * 0.34], fill=rgb(MUSTARD))
    d.ellipse([cx - r * 0.16, cy - r * 0.22, cx + r * 0.02, cy - r * 0.04], fill=(246, 222, 130))


def cup(d, cx, cy, s, ink):
    """A cup with steam: something to drink."""
    d.rounded_rectangle([cx - s, cy - s * 0.5, cx + s * 0.75, cy + s * 0.75], radius=int(s * 0.25), fill=ink)
    d.ellipse([cx + s * 0.45, cy - s * 0.3, cx + s * 1.15, cy + s * 0.4], outline=ink, width=int(s * 0.16))
    d.rectangle([cx - s * 1.2, cy + s * 0.82, cx + s * 1.0, cy + s * 0.98], fill=ink)
    for k in (-1, 0, 1):
        x = cx - s * 0.12 + k * s * 0.45
        pts = [(x + s * 0.12 * math.sin(t * 2.2), cy - s * 0.65 - t * s * 0.32) for t in np.linspace(0, 2.8, 14)]
        d.line(pts, fill=ink, width=int(s * 0.12))


def tea_label():
    w, h = 1024, 420                                  # 17 x 7 cm round the bottle
    im = Image.new("RGB", (w, h), rgb(BONE))
    d = ImageDraw.Draw(im)
    blue, bone, ink = rgb(STEEL_BLUE), rgb(BONE), rgb(HULL)
    d.rectangle([0, 0, w - 1, 54], fill=blue)
    d.rectangle([0, h - 55, w - 1, h - 1], fill=blue)
    d.rectangle([0, 60, w - 1, 67], fill=rgb(MUSTARD))
    d.rectangle([0, h - 68, w - 1, h - 61], fill=rgb(MUSTARD))
    # the front (u = 0.5): the bloom over the code, the maker's mark in the top band
    bloom(d, 512, 160, 70)
    d.text((512, 318), "BT-1", fill=blue, font=bm.font(50), anchor="mm")
    cw_mark(d, 512, 27, 18, bone, blue)
    # the back (u = 0 and 1): a steaming cup and the heal mark
    for x0 in (-w // 2, w // 2):
        cup(d, x0 + 440, 230, 50, ink)
        heart_plus(d, x0 + 600, 215, 44, (200, 66, 70), bone)
    return bm.as_rgb(im)


def build_bloom_tea():
    mats = {
        "glass": glass("tea_glass", "#9fb8ae"),
        "tea": pt.plain("tea_liquid", TEA, 0.12, 0.0),
        "label": lt.decal("tea_label", tea_label(), rough=0.5),
        "cap": bm.metal("tea_cap", 861, STEEL_BLUE, rough=0.38, metal_=0.7, wear=0.4),
        "seal": pt.plain("tea_seal", GRAPHITE, 0.5, 0.2),
    }
    g = Geo()
    R, z0 = 0.027, FLOOR
    body = [(0.0, z0), (R - 0.004, z0), (R, z0 + 0.004), (R, z0 + 0.118), (R - 0.004, z0 + 0.136),
            (0.017, z0 + 0.156), (0.0125, z0 + 0.168), (0.0118, z0 + 0.186), (0.0, z0 + 0.186)]
    lathe(g, body, "glass", steps=48)
    tea = [(0.0, z0 + 0.004), (R - 0.0025, z0 + 0.004), (R - 0.0025, z0 + 0.13), (0.0, z0 + 0.13)]
    lathe(g, tea, "tea", steps=40)
    bl.ring(g, z0 + 0.168, z0 + 0.172, 0.0118, 0.0136, "glass", segs=32)            # the finish's bead
    # the screw cap: a ridged steel-blue cap with a dark liner showing at its skirt
    cylinder(g, (0, 0, z0 + 0.172), (0, 0, z0 + 0.198), 0.0142, "cap", segs=40)
    for k in range(20):
        a = 2 * math.pi * k / 20
        box(g, (0.0146 * math.cos(a), 0.0146 * math.sin(a), z0 + 0.185), (0.0009, 0.0012, 0.0115), "cap",
            rot=Matrix.Rotation(a, 3, "Z"))
    bl.ring(g, z0 + 0.1705, z0 + 0.1725, 0.0118, 0.0142, "seal", segs=32)
    wrap(g, (0, 0, 0), (0, 0, 1), R + 0.0007, z0 + 0.03, z0 + 0.1, -math.pi / 2 - math.pi, -math.pi / 2 + math.pi,
         "label", segs=48, ref=(1, 0, 0))
    root = bl.make_empty("BloomTea")
    bl.make_object("Bottle", g, mats, root)
    return root


def glass(name, tint):
    mat = gc.glass_material()
    mat.name = name
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*(lt.srgb(tint) ** 2.2), 1.0)
    bsdf.inputs["Alpha"].default_value = 0.5
    return mat


# =====================================================================================================================
# Grazer Shake
# =====================================================================================================================

def grazer_head(im, cx, cy, s):
    """The Grazer pictogram, face on: a big round slate head, wide droopy ears, pink curled horns, a cream muzzle with a
    pink nose, friendly eyes."""
    d = ImageDraw.Draw(im)
    slate, cream, pink = GRAZER_SLATE, GRAZER_CREAM, GRAZER_PINK
    dark = (40, 44, 50)
    for side in (-1, 1):                                                 # horns: a curl on top of each side
        hx = cx + side * s * 0.55
        d.ellipse([hx - s * 0.3, cy - s * 1.02, hx + s * 0.3, cy - s * 0.48], fill=pink)
        d.ellipse([hx - s * 0.14 + side * s * 0.05, cy - s * 0.88, hx + s * 0.14 + side * s * 0.05, cy - s * 0.62],
                  fill=rgb(STEEL_BLUE))
        # ears: long drooping ovals out to the side
        e = Image.new("RGBA", (int(s * 1.2), int(s * 0.5)), (0, 0, 0, 0))
        ImageDraw.Draw(e).ellipse([0, 0, int(s * 1.2) - 1, int(s * 0.5) - 1], fill=slate)
        e = e.rotate(-side * 22, expand=True, resample=Image.BICUBIC)
        im.paste(e, (int(hx + side * s * 0.25 - e.width / 2), int(cy - s * 0.45)), e)
    d.ellipse([cx - s * 0.78, cy - s * 0.75, cx + s * 0.78, cy + s * 0.62], fill=slate)       # head
    d.ellipse([cx - s * 0.62, cy + s * 0.05, cx + s * 0.62, cy + s * 0.95], fill=cream)       # muzzle
    d.ellipse([cx - s * 0.4, cy + s * 0.35, cx + s * 0.4, cy + s * 0.78], fill=pink)          # nose
    for side in (-1, 1):
        d.ellipse([cx + side * s * 0.2 - s * 0.07, cy + s * 0.48, cx + side * s * 0.2 + s * 0.07, cy + s * 0.64], fill=(160, 80, 92))
        ex = cx + side * s * 0.38
        d.ellipse([ex - s * 0.16, cy - s * 0.38, ex + s * 0.16, cy - s * 0.04], fill=(250, 248, 240))
        d.ellipse([ex - s * 0.09, cy - s * 0.3, ex + s * 0.09, cy - s * 0.08], fill=dark)
        d.ellipse([ex - s * 0.04, cy - s * 0.27, ex + s * 0.01, cy - s * 0.2], fill=(250, 248, 240))


def splash(d, cx, cy, s, fill):
    """A milk splash: a wide puddle with drops thrown up."""
    d.ellipse([cx - s * 1.4, cy - s * 0.25, cx + s * 1.4, cy + s * 0.35], fill=fill)
    for dx, h, r in ((-1.1, 0.5, 0.16), (-0.6, 0.85, 0.2), (0.0, 1.05, 0.22), (0.6, 0.8, 0.2), (1.1, 0.45, 0.15)):
        x = cx + dx * s
        d.polygon([(x - r * s, cy), (x + r * s, cy), (x, cy - h * s)], fill=fill)
        d.ellipse([x - r * s * 0.8, cy - h * s - r * s * 1.4, x + r * s * 0.8, cy - h * s + r * s * 0.2], fill=fill)


def shake_sleeve():
    w, h = 1024, 650                                  # 20.7 x 13.2 cm round the bottle
    blue, bone = rgb(STEEL_BLUE), rgb(BONE)
    im = Image.new("RGB", (w, h), blue)
    d = ImageDraw.Draw(im)
    d.rectangle([0, h - 80, w - 1, h - 1], fill=rgb(MUSTARD))
    d.rectangle([0, h - 92, w - 1, h - 81], fill=bone)
    # the front (u = 0.5): the Grazer over a milk splash, the code under it
    splash(d, 512, 450, 120, bone)
    grazer_head(im, 512, 280, 125)
    d = ImageDraw.Draw(im)
    d.text((512, h - 40), "GS-1", fill=rgb(HULL), font=bm.font(54), anchor="mm")
    # the back: the heal mark, the maker's mark
    for x0 in (-w // 2, w // 2):
        heart_plus(d, x0 + 512, 270, 78, (200, 66, 70), bone)
        cw_mark(d, x0 + 512, 430, 34, bone, blue)
    return bm.as_rgb(im)


def build_grazer_shake():
    mats = {
        "body": pt.plain("shake_body", SHAKE, 0.42, 0.0),
        "sleeve": lt.decal("shake_sleeve", shake_sleeve(), rough=0.35),
        "cap": pt.plain("shake_cap", MUSTARD, 0.4, 0.0),
        "spout": pt.plain("shake_spout", BONE, 0.45, 0.0),
        "dark": pt.plain("shake_dark", GRAPHITE, 0.6, 0.05),
    }
    g = Geo()
    z0 = FLOOR
    prof = [(0.0, z0), (0.029, z0), (0.033, z0 + 0.005), (0.033, z0 + 0.06), (0.0295, z0 + 0.085), (0.0295, z0 + 0.11),
            (0.033, z0 + 0.13), (0.033, z0 + 0.158), (0.028, z0 + 0.172), (0.021, z0 + 0.18), (0.0195, z0 + 0.186),
            (0.0, z0 + 0.186)]
    lathe(g, prof, "body", steps=48)
    for z in (z0 + 0.012, z0 + 0.155):                                   # moulded beads
        bl.ring(g, z, z + 0.003, 0.03, 0.0338, "body", segs=48)
    # the sleeve over the lower body (the waist shows the sleeve's shrink), front at u = 0.5
    sleeve = []
    for k in range(13):
        z = z0 + 0.018 + (0.15 - 0.018) * k / 12
        r = np.interp(z - z0, [p[1] - z0 for p in prof[2:9]], [p[0] for p in prof[2:9]]) + 0.0006
        sleeve.append((z, r))
    n = 48
    verts, faces, uvs = [], [], []
    for k in range(n + 1):
        a = -math.pi / 2 - math.pi + 2 * math.pi * k / n
        for z, r in sleeve:
            verts.append((r * math.cos(a), r * math.sin(a), z))
    rows = len(sleeve)
    for k in range(n):
        for i in range(rows - 1):
            a, b = k * rows + i, (k + 1) * rows + i
            faces.append((a, b, b + 1, a + 1))
            uvs.append(((k / n, i / (rows - 1)), ((k + 1) / n, i / (rows - 1)), ((k + 1) / n, (i + 1) / (rows - 1)),
                        (k / n, (i + 1) / (rows - 1))))
    g.add(verts, faces, "sleeve", uvs)
    # the sports cap: a ribbed mustard cap, a flip lid hinged at the back over a bone spout
    zc = z0 + 0.184
    cylinder(g, (0, 0, zc), (0, 0, zc + 0.018), 0.0225, "cap", segs=40)
    for k in range(24):
        a = 2 * math.pi * k / 24
        box(g, (0.023 * math.cos(a), 0.023 * math.sin(a), zc + 0.009), (0.0008, 0.0011, 0.008), "cap",
            rot=Matrix.Rotation(a, 3, "Z"))
    bl.ring(g, zc - 0.001, zc + 0.002, 0.0195, 0.0232, "dark", segs=40)
    cylinder(g, (0, -0.004, zc + 0.018), (0, -0.004, zc + 0.0235), 0.0075, "spout", segs=20, r1=0.0058)
    lid = Matrix.Translation((0, 0.017, zc + 0.0245)) @ Matrix.Rotation(math.radians(-4), 4, "X")
    sub = Geo()
    box(sub, (0, -0.017, 0.0025), (0.0115, 0.0135, 0.0025), "cap", bevel=0.002)
    box(sub, (0, -0.031, 0.0015), (0.005, 0.0025, 0.0035), "cap", bevel=0.001)
    g.extend(sub, lid)
    box(g, (0, 0.019, zc + 0.019), (0.006, 0.002, 0.002), "dark", bevel=0.0008)       # the hinge
    root = bl.make_empty("GrazerShake")
    bl.make_object("Bottle", g, mats, root)
    return root


# =====================================================================================================================
# Fertilizer (Rootwell)
# =====================================================================================================================

PW, PH, PD = 0.15, 0.19, 0.068          # the pouch: width, body height (to the seal), depth at its base
SEAL = 0.022                            # the crimped top seal's height
PX = 4                                  # print pixels per millimetre


def pouch_print(side):
    """One face of the pouch (side -1 front, +1 back), 150 x 212 mm, at PX px per mm, as RGB floats."""
    old = bp.SS
    bp.SS = PX
    try:
        W, H = 150, int((PH + SEAL) * 1000)
        cv = bp.Canvas(W, H, bp.YELLOW)
        X, Y = cv.grid()
        glow = np.clip(1 - np.hypot(X - 75, Y - 150) / 120, 0, 1) ** 1.5
        cv.paste_mask(glow * 0.5, (248, 222, 96))
        # the green Rootwell band across the top (the seal and 40 mm under it), a white keyline on its wavy edge
        edge = SEAL * 1000 + 40 + 4 * np.sin(X / 150 * 2 * np.pi * 0.9 + 0.6)
        cv.paste_mask(np.clip((edge + 2.2 - Y) * PX * 0.7, 0, 1), bp.WHITE)
        cv.paste_mask(np.clip((edge - Y) * PX * 0.7, 0, 1), bp.GREEN)
        cv.paste_mask(np.clip((SEAL * 1000 - Y) * PX * 0.7, 0, 1) * 0.35, bp.GREEN_DARK)
        if side < 0:
            bp.sprout_badge(cv, 28, 42, 17)
            cv.text((50, 44), "ROOTWELL", bp.font(20, "Bold", True), bp.WHITE, anchor="lm")
            cv.text((75, 92), "FERTILIZER", bp.font(30, "Bold", True), bp.INK, anchor="mm", stroke=1.2, stroke_fill=bp.WHITE)
            # the pictogram: a seedling rising from a bed of soil, chevrons up beside it, granules falling on it
            soil_top = 182 - 5 * np.sin(X / 18)
            inside = np.clip((Y - soil_top) * PX * 0.7, 0, 1) * np.clip((198 - Y) * PX * 0.7, 0, 1)
            inside *= np.clip((X - 18) * PX * 0.7, 0, 1) * np.clip((132 - X) * PX * 0.7, 0, 1)
            cv.paste_mask(inside, bp.SOIL)
            cv.paste_mask(np.clip((2.4 - np.abs(X - 66)) * PX * 0.7, 0, 1) * (Y > 140) * (Y < 184), bp.LEAF_DARK)
            bp.leaf(cv, 66, 160, 40, 136, 14)
            bp.leaf(cv, 66, 150, 94, 124, 16)
            bp.leaf(cv, 66, 142, 70, 116, 11)
            rng = np.random.default_rng(7)
            for _ in range(26):                                   # the granules
                gx, gy, gr = rng.uniform(24, 108), rng.uniform(112, 180), rng.uniform(1.1, 1.9)
                if abs(gx - 66) < 6 and gy > 140:
                    continue
                bp.shaded_blob(cv, gx, gy, gr, gr, (206, 222, 170), (120, 140, 96), (248, 252, 230))
            # the red tag: the growth chevrons, fast
            tag = Image.new("RGBA", (int(34 * PX), int(28 * PX)), (0, 0, 0, 0))
            td = ImageDraw.Draw(tag)
            td.rounded_rectangle([0, 0, tag.width - 1, tag.height - 1], radius=int(6 * PX), fill=bp.WHITE)
            td.rounded_rectangle([int(1.6 * PX), int(1.6 * PX), tag.width - 1 - int(1.6 * PX), tag.height - 1 - int(1.6 * PX)],
                                 radius=int(5 * PX), fill=bp.RED)
            for k in range(2):
                y = (19 - k * 8) * PX
                td.line([(9 * PX, y), (17 * PX, y - 7 * PX), (25 * PX, y)], fill=bp.WHITE, width=int(3 * PX), joint="curve")
            tag = tag.rotate(-8, resample=Image.BICUBIC, expand=True)
            cv.img.paste(tag, (int(102 * PX), int(140 * PX)), tag)
            cv.d = ImageDraw.Draw(cv.img)
        else:
            bp.sprout_badge(cv, 75, 40, 15)
            cv.d.rounded_rectangle(cv.s(18, 78, 132, 176), radius=5 * PX, fill=bp.WHITE)
            # a watering can and a drop: it still needs water
            ink = bp.GREEN_DARK
            cv.d.rounded_rectangle(cv.s(34, 98, 66, 124), radius=3 * PX, fill=ink)
            cv.d.polygon(cv.s(66, 104, 86, 92, 89, 96, 68, 114), fill=ink)
            cv.d.arc(cv.s(36, 88, 62, 112), 180, 360, fill=ink, width=int(3 * PX))
            cv.d.ellipse(cv.s(94, 104, 104, 116), fill=(70, 140, 200))
            cv.d.polygon(cv.s(94, 109, 104, 109, 99, 96), fill=(70, 140, 200))
            for k in range(4):                                    # ruled lines for small print
                cv.d.rectangle(cv.s(28, 136 + k * 7, 28 + (90 if k % 2 else 64), 138.5 + k * 7), fill=(160, 160, 150))
            rng = np.random.default_rng(13)
            x = 70.0
            while x < 122:                                        # the barcode
                bw = float(rng.uniform(0.6, 1.8))
                cv.d.rectangle(cv.s(x, 150, x + bw, 168), fill=(30, 30, 30))
                x += bw + float(rng.uniform(0.6, 1.5))
        # the crimp: fine vertical ribs across the seal, and the tear notch's mark at the right
        ribs = (np.sin(X * 2 * np.pi / 1.6) > 0.3) * (Y < SEAL * 1000 - 3) * (Y > 3)
        cv.paste_mask(ribs * 0.25, bp.GREEN_DARK)
        return np.asarray(cv.img.convert("RGB"), float) / 255.0
    finally:
        bp.SS = old


def pouch_atlas():
    """Front print on the left half, back print on the right."""
    front, back = pouch_print(-1), pouch_print(1)
    return np.concatenate([front, back], axis=1)


def build_fertilizer():
    mats = {
        "print": lt.decal("fert_print", pouch_atlas(), rough=0.42),
        "base": pt.plain("fert_base", "#D6A41E", 0.5, 0.0),
        "zip": pt.plain("fert_zip", "#c9a020", 0.45, 0.0),
    }
    g = Geo()
    z0 = FLOOR
    n_around, n_up = 24, 22
    total = PH + SEAL

    def section(t):
        """Half width and half depth of the pouch at height fraction t (0 base, 1 the seal's start)."""
        a = PW / 2 * (0.94 + 0.06 * math.sin(math.pi * min(1.0, t * 1.3)))
        b = PD / 2 * (1 - t ** 1.7) * (0.92 + 0.08 * math.sin(math.pi * min(1.0, t * 1.6))) + 0.0012
        return a, b

    for side in (-1, 1):                                          # front (-Y) and back (+Y), each its own UV half
        verts, faces, uvs = [], [], []
        for j in range(n_up + 1):
            t = j / n_up
            a, b = section(t)
            z = z0 + 0.003 + (PH - 0.003) * t
            for i in range(n_around + 1):
                s = -1 + 2 * i / n_around                         # -1 .. 1 across the face
                th = math.acos(max(-1.0, min(1.0, s)))
                x = a * math.copysign(abs(math.cos(th)) ** 0.6, math.cos(th))
                y = side * b * math.sin(th) ** 0.4
                verts.append((x if side < 0 else -x, y, z))
        for j in range(n_up):
            for i in range(n_around):
                p = j * (n_around + 1) + i
                q = p + n_around + 1
                faces.append((p, p + 1, q + 1, q) if side < 0 else (p, q, q + 1, p + 1))

                def uv(ii, jj):
                    # projected straight onto the face, so the print keeps its proportions round the bulge
                    x = verts[jj * (n_around + 1) + ii][0] * (1 if side < 0 else -1)
                    u = (x + section(jj / n_up)[0]) / (2 * section(jj / n_up)[0])
                    v = (PH * jj / n_up) / total
                    return ((u * 0.5) if side < 0 else (0.5 + u * 0.5), v)

                uvs.append((uv(i, j), uv(i + 1, j), uv(i + 1, j + 1), uv(i, j + 1)) if side < 0 else
                           (uv(i, j), uv(i, j + 1), uv(i + 1, j + 1), uv(i + 1, j)))
        g.add(verts, faces, "print", uvs)
    # the base gusset: a flat oval under the pouch
    a, b = section(0.0)
    ring = []
    for k in range(32):
        th = 2 * math.pi * k / 32
        c, s = math.cos(th), math.sin(th)
        ring.append((a * math.copysign(abs(c) ** 0.6, c), b * math.copysign(abs(s) ** 0.4, s), z0 + 0.003))
    g.add(ring + [(0, 0, z0)], [(k, 32, (k + 1) % 32) for k in range(32)], "base")
    # the top seal: a thin printed strip (two faces), a tear notch cut at its right end
    ztop = z0 + PH
    a, _ = section(1.0)
    for side in (-1, 1):
        y = side * 0.0011
        x0, x1 = -a, a
        v0, v1 = PH / total, 1.0

        def u_of(x):
            u = (x + a) / (2 * a)
            return u * 0.5 if side < 0 else 0.5 + (1 - u) * 0.5
        notch = [(x0, ztop), (x1, ztop), (x1, ztop + SEAL * 0.55), (x1 - 0.004, ztop + SEAL * 0.62), (x1, ztop + SEAL * 0.69),
                 (x1, ztop + SEAL), (x0, ztop + SEAL)]
        pts = [(x, y, z) for x, z in notch]
        uv = [(u_of(x), v0 + (z - ztop) / SEAL * (v1 - v0)) for x, z in notch]
        order = list(range(len(pts))) if side > 0 else list(reversed(range(len(pts))))
        base = len(g.v)
        g.v.extend(pts)
        for k in range(1, len(order) - 1):
            g.f.append((base + order[0], base + order[k], base + order[k + 1]))
            g.m.append("print")
            g.uv.append((uv[order[0]], uv[order[k]], uv[order[k + 1]]))
    box(g, (0, 0, ztop + SEAL / 2), (a, 0.0011, SEAL / 2), "zip")                        # the seal's edge
    # the zip ridge just under the seal, on both faces
    for side in (-1, 1):
        _, b = section((PH - 0.012) / PH)
        box(g, (0, side * (b + 0.0004), ztop - 0.012), (a * 0.93, 0.0012, 0.0016), "zip", bevel=0.0006)
    root = bl.make_empty("Fertilizer")
    bl.make_object("Pouch", g, mats, root)
    return root


# =====================================================================================================================
# Grazer Milk (batch C3)
# =====================================================================================================================

MILK = "#F3EFE2"


def milk_drop(d, cx, cy, s, fill):
    """A drop of milk: something to drink that comes from a Grazer."""
    d.ellipse([cx - s, cy - s * 0.2, cx + s, cy + s * 1.6], fill=fill)
    d.polygon([(cx - s * 0.93, cy + s * 0.45), (cx + s * 0.93, cy + s * 0.45), (cx, cy - s * 1.4)], fill=fill)


def milk_label():
    w, h = 1024, 400                                  # 19 x 7.4 cm round the bottle
    blue, bone, ink = rgb(STEEL_BLUE), rgb(BONE), rgb(HULL)
    im = Image.new("RGB", (w, h), bone)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w - 1, 48], fill=blue)
    d.rectangle([0, h - 49, w - 1, h - 1], fill=blue)
    d.rectangle([0, 54, w - 1, 61], fill=rgb(MUSTARD))
    d.rectangle([0, h - 62, w - 1, h - 55], fill=rgb(MUSTARD))
    # the front (u = 0.5): the Grazer in a steel-blue roundel, a milk drop each side, the code under it
    d.ellipse([512 - 118, 210 - 118, 512 + 118, 210 + 118], fill=blue)
    d.ellipse([512 - 104, 210 - 104, 512 + 104, 210 + 104], fill=bone)
    grazer_head(im, 512, 222, 82)
    d = ImageDraw.Draw(im)
    for x in (330, 694):
        milk_drop(d, x, 180, 30, blue)
    d.text((512, h - 25), "GM-1", fill=bone, font=bm.font(34), anchor="mm")
    cw_mark(d, 512, 24, 17, bone, blue)
    # the back (u = 0 and 1): the heal mark and the drink mark
    for x0 in (-w // 2, w // 2):
        heart_plus(d, x0 + 440, 200, 52, (200, 66, 70), bone)
        cup(d, x0 + 610, 215, 46, ink)
    return bm.as_rgb(im)


def build_grazer_milk():
    mats = {
        "glass": glass("milk_glass", "#d8e2de"),
        "milk": pt.plain("milk_liquid", MILK, 0.28, 0.0),
        "label": lt.decal("milk_label", milk_label(), rough=0.55),
        "cap": pt.plain("milk_cap", STEEL_BLUE, 0.32, 0.6),
        "tab": pt.plain("milk_tab", MUSTARD, 0.45, 0.0),
    }
    g = Geo()
    R, z0 = 0.031, FLOOR
    # an old-fashioned milk bottle: a straight body, a long round shoulder, a short neck and a thick lip
    body = [(0.0, z0), (R - 0.005, z0), (R, z0 + 0.005), (R, z0 + 0.098), (R - 0.002, z0 + 0.112), (0.026, z0 + 0.13),
            (0.0195, z0 + 0.148), (0.0168, z0 + 0.162), (0.0168, z0 + 0.172), (0.0186, z0 + 0.174), (0.0186, z0 + 0.181),
            (0.0, z0 + 0.181)]
    lathe(g, body, "glass", steps=48)
    milk = [(0.0, z0 + 0.004), (R - 0.0025, z0 + 0.004), (R - 0.0025, z0 + 0.098), (R - 0.0045, z0 + 0.112),
            (0.0235, z0 + 0.13), (0.0172, z0 + 0.148), (0.0, z0 + 0.148)]
    lathe(g, milk, "milk", steps=40)
    # the crimped foil cap over the lip, a mustard pull tab
    cylinder(g, (0, 0, z0 + 0.170), (0, 0, z0 + 0.1855), 0.0196, "cap", segs=40)
    for k in range(28):
        a = 2 * math.pi * k / 28
        box(g, (0.0199 * math.cos(a), 0.0199 * math.sin(a), z0 + 0.1745), (0.0007, 0.0013, 0.0045), "cap",
            rot=Matrix.Rotation(a, 3, "Z"))
    box(g, (0, -0.0225, z0 + 0.172), (0.0045, 0.0028, 0.0055), "tab", bevel=0.0012)
    wrap(g, (0, 0, 0), (0, 0, 1), R + 0.0007, z0 + 0.024, z0 + 0.092, -math.pi / 2 - math.pi, -math.pi / 2 + math.pi,
         "label", segs=48, ref=(1, 0, 0))
    root = bl.make_empty("GrazerMilk")
    bl.make_object("Bottle", g, mats, root)
    return root


# =====================================================================================================================
# Grazer Steak (batch C3): a Settler Patchwork butcher's parcel
# =====================================================================================================================

ZINC = "#9AA3AB"
KRAFT = "#D3BF98"                # butcher paper, faded bone-kraft
MEAT, MEAT_DARK, FAT = (124, 20, 28), (80, 12, 20), (236, 222, 198)
TWINE = "#A8473B"                # brick-red twine
TRAY = (0.105, 0.07)             # the tray's half size
STEAK_C, STEAK_R, STEAK_H = (-0.008, 0.0), (0.072, 0.047), 0.024


def steak_radius(th):
    """The steak's outline: a fat oval with a kidney notch on one long side and a lumpier far end."""
    rx, ry = STEAK_R
    r = 1.0 + 0.05 * math.cos(3 * th + 0.4) + 0.03 * math.sin(5 * th) - 0.11 * math.exp(-((th + math.pi / 2 - 0.25) / 0.45) ** 2)
    return rx * r, ry * r


def steak_s(x, y):
    """How far (x, y) is out from the steak's middle, as a share of its outline (1 on the edge)."""
    dx, dy = x - STEAK_C[0], y - STEAK_C[1]
    th = math.atan2(dy / STEAK_R[1], dx / STEAK_R[0])
    ax, ay = steak_radius(th)
    return math.hypot(dx / ax, dy / ay)


def steak_height(s):
    """Height of the steak's top above its base at outline share s: flat, then a rounded edge."""
    if s <= 0.86:
        return STEAK_H
    if s >= 1.0:
        return 0.0
    return STEAK_H * math.cos(math.asin(min(1.0, (s - 0.86) / 0.14)))


def steak_print():
    """The steak seen from above, over the tray's footprint: red meat with fat marbling, a fat cap along its +Y side."""
    W, H = 640, 640
    (x0, x1), (y0, y1) = (STEAK_C[0] - 0.08, STEAK_C[0] + 0.08), (STEAK_C[1] - 0.08, STEAK_C[1] + 0.08)
    xs = np.linspace(x0, x1, W)
    ys = np.linspace(y1, y0, H)                      # image rows go down; +Y is the top of the image
    X, Y = np.meshgrid(xs, ys)
    dx, dy = (X - STEAK_C[0]) / STEAK_R[0], (Y - STEAK_C[1]) / STEAK_R[1]
    th = np.arctan2(dy, dx)
    r = 1.0 + 0.05 * np.cos(3 * th + 0.4) + 0.03 * np.sin(5 * th) - 0.11 * np.exp(-((th + np.pi / 2 - 0.25) / 0.45) ** 2)
    s = np.hypot(dx, dy) / r
    rng = np.random.default_rng(29)
    n = lt.fbm(31, base=6.0)
    n = np.asarray(Image.fromarray((np.clip(n, 0, 1) * 255).astype(np.uint8)).resize((W, H)), float) / 255.0
    meat = np.array(MEAT, float)[None, None] * (0.86 + 0.24 * n[..., None])
    # the grain: dark streaks across, and white marbling wandering through
    grain = 0.5 + 0.5 * np.sin((X * 0.8 + Y * 1.4) * 900 + 6 * n)
    meat = meat * (1 - 0.12 * grain[..., None]) + np.array(MEAT_DARK, float) * 0.12 * grain[..., None]
    marble = np.zeros((H, W))
    for _ in range(9):
        px, py = rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6)
        ang, freq, amp = rng.uniform(0, np.pi), rng.uniform(9, 20), rng.uniform(0.03, 0.08)
        u = (dx - px) * np.cos(ang) + (dy - py) * np.sin(ang)
        v = -(dx - px) * np.sin(ang) + (dy - py) * np.cos(ang)
        line = np.abs(v - amp * np.sin(u * freq + px * 5)) < rng.uniform(0.006, 0.016)
        marble = np.maximum(marble, line * (np.abs(u) < rng.uniform(0.2, 0.5)))
    marble = np.clip(lt.blur(marble, 2), 0, 1) * 0.7
    out = meat * (1 - 0.8 * marble[..., None]) + np.array(FAT, float) * 0.8 * marble[..., None]
    # the fat cap: a creamy band along part of the front (-Y) side, and the cut edge darker
    cap = np.clip((s - 0.8) / 0.05, 0, 1) * np.clip((-0.25 - dy) / 0.3, 0, 1) * np.clip((0.3 - np.abs(dx + 0.25)) / 0.15, 0, 1)
    out = out * (1 - cap[..., None]) + np.array(FAT, float) * (0.9 + 0.1 * n[..., None]) * cap[..., None]
    edge = np.clip((s - 0.9) / 0.1, 0, 1) * (1 - cap)
    out = out * (1 - 0.35 * edge[..., None])
    return np.clip(out / 255.0, 0, 1), (x0, x1, y0, y1)


def tag_print():
    """The butcher's kraft tag: a brick-red stamped Grazer and three tally strokes (settler hand-stamp, no words)."""
    w, h = 300, 200
    im = Image.new("RGB", (w, h), (214, 192, 150))
    d = ImageDraw.Draw(im)
    d.rectangle([4, 4, w - 5, h - 5], outline=(150, 64, 52), width=5)
    stamp = Image.new("RGB", (w, h), (214, 192, 150))
    grazer_head(stamp, 105, 104, 62)
    # recolour the stamp in one brick-red ink, as a rubber stamp would print it
    a = np.asarray(stamp, float)
    paper = np.array((214, 192, 150), float)
    ink = np.clip(np.abs(a - paper).sum(-1) / 120.0, 0, 1)[..., None]
    base = np.asarray(im, float)
    base = base * (1 - ink * 0.9) + np.array((158, 60, 48), float) * ink * 0.9
    im = Image.fromarray(base.astype(np.uint8))
    d = ImageDraw.Draw(im)
    for k in range(3):
        d.line([(200 + k * 22, 70), (196 + k * 22, 134)], fill=(150, 56, 46), width=7)
    d.ellipse([w - 44, h // 2 - 12, w - 20, h // 2 + 12], fill=(120, 96, 70))       # the string hole
    return bm.as_rgb(im)


def build_grazer_steak():
    meat_rgb, (x0, x1, y0, y1) = steak_print()
    mats = {
        "tray": bm.metal("steak_tray", 871, ZINC, rough=0.5, metal_=0.8, wear=0.7),
        "paper": lt.textured("steak_paper", gc.half_res(lt.paint_surface(873, KRAFT, "#B89C70")), normal_strength=0.6),
        "meat": lt.decal("steak_meat", meat_rgb, rough=0.55),
        "twine": pt.plain("steak_twine", TWINE, 0.85, 0.0),
        "tag": lt.decal("steak_tag", tag_print(), rough=0.8),
    }
    g = Geo()
    z0 = FLOOR
    tx, ty = TRAY
    # the pressed-tin tray: a floor, four flared walls and a rolled rim round the top
    rim_h, flare = 0.016, 0.008
    box(g, (0, 0, z0 + 0.0008), (tx - flare, ty - flare, 0.0008), "tray")

    def rect(hx, hy, z, rad, n=6):
        pts = []
        for cx, cy, a0 in ((hx - rad, hy - rad, 0), (-hx + rad, hy - rad, 90), (-hx + rad, -hy + rad, 180), (hx - rad, -hy + rad, 270)):
            for k in range(n + 1):
                a = math.radians(a0 + 90 * k / n)
                pts.append((cx + rad * math.cos(a), cy + rad * math.sin(a), z))
        return pts
    lo = rect(tx - flare, ty - flare, z0 + 0.0016, 0.012)
    hi = rect(tx, ty, z0 + rim_h, 0.016)
    m = len(lo)
    verts = lo + hi
    faces = [(k, (k + 1) % m, m + (k + 1) % m, m + k) for k in range(m)]
    g.add(verts, faces, "tray")
    g.add([(x, y, z - 0.0004) for x, y, z in lo] + [(x * 0.995, y * 0.995, z - 0.0006) for x, y, z in hi],
          [(m + k, m + (k + 1) % m, (k + 1) % m, k) for k in range(m)], "tray")          # the outside of the walls
    bl.sweep(g, hi + [hi[0], hi[1]], 0.0017, "tray", segs=8)                              # the rolled rim
    # the butcher paper the steak lies on: a sheet a little short of the tray, its corners curling up
    zp = z0 + 0.0022
    px, py, n = 0.088, 0.058, 18

    def under(x, y):
        curl = max(0.0, (abs(x) / px) ** 6 + (abs(y) / py) ** 6 - 0.6) * 0.006
        return zp + curl
    verts, faces = [], []
    for j in range(n + 1):
        for i in range(n + 1):
            x, y = -px + 2 * px * i / n, -py + 2 * py * j / n
            verts.append((x, y, under(x, y)))
    for j in range(n):
        for i in range(n):
            p = j * (n + 1) + i
            faces.append((p, p + 1, p + n + 2, p + n + 1))
    g.add(verts, faces, "paper")
    # the steak: rings from its middle out to the outline and round its edge, the meat print projected from above
    zs = zp + 0.0008
    steak_verts, rings, segs = [], 14, 64
    for i in range(rings + 1):
        s = 0.86 * i / 10 if i <= 10 else 0.86 + 0.14 * math.sin(math.pi / 2 * (i - 10) / 4)
        h = steak_height(min(s, 0.9999)) if i < rings else 0.0
        for k in range(segs):
            th = 2 * math.pi * k / segs
            ax, ay = steak_radius(th)
            wob = 0.0012 * math.sin(7 * th) * (i / rings)                                  # a little uneven, as cut meat is
            steak_verts.append((STEAK_C[0] + (s * ax + wob) * math.cos(th), STEAK_C[1] + (s * ay + wob) * math.sin(th),
                                zs + h + 0.0015 * math.sin(2.3 * th) * (s < 0.86)))
    steak_faces = []
    for i in range(rings):
        for k in range(segs):
            a, b = i * segs + k, i * segs + (k + 1) % segs
            steak_faces.append((a, b, b + segs, a + segs))
    steak_faces.append(tuple(rings * segs + k for k in reversed(range(segs))))          # its underside
    uv_of = [((x - x0) / (x1 - x0), (y - y0) / (y1 - y0)) for x, y, _ in steak_verts]
    g.add(steak_verts, steak_faces, "meat", [tuple(uv_of[q] for q in f) for f in steak_faces])
    # the flap of paper folded back over the steak's far end, draped over it
    fx0, fx1, fy = 0.018, 0.09, 0.064
    nx, ny = 16, 22

    def drape(x, y):
        s = steak_s(x, y)
        top = zs + steak_height(min(s, 0.9999)) + 0.0028 if s < 1 else zs
        fall = zs + (STEAK_H + 0.0028) * math.exp(-(max(0.0, s - 1.0) / 0.12) ** 2)
        return max(top, fall, under(x, y) + 0.001)
    verts, faces = [], []
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = fx0 + (fx1 - fx0) * i / nx + (0.003 * math.sin(j * 1.7) if i == 0 else 0.0)            # the torn fold
            y = -fy + 2 * fy * j / ny
            verts.append((x, y, drape(x, y)))
    for j in range(ny):
        for i in range(nx):
            p = j * (nx + 1) + i
            faces.append((p, p + 1, p + nx + 2, p + nx + 1))
    g.add(verts, faces, "paper")
    # the fold's turned-back lip along the flap's edge
    lip = [(fx0 - 0.002, -fy + 2 * fy * j / ny, drape(fx0, -fy + 2 * fy * j / ny) + 0.0012) for j in range(ny + 1)]
    bl.sweep(g, lip, 0.0016, "paper", segs=6)
    # twine round the flap and a knot on top
    tx_ = 0.054
    path = [(tx_, y, drape(tx_, y) + 0.0012) for y in np.linspace(-fy, fy, 28)]
    bl.sweep(g, path, 0.0011, "twine", segs=6)
    kz = drape(tx_, 0.0) + 0.0016
    for side in (-1, 1):
        loop = [(tx_ + side * 0.012 * math.sin(t) * 0.9, 0.006 * math.sin(2 * t) * side, kz + 0.0018 * math.sin(t))
                for t in np.linspace(0, math.pi, 12)]
        bl.sweep(g, loop, 0.0010, "twine", segs=6)
        end = [(tx_, 0, kz), (tx_ + side * 0.006, -0.012, kz - 0.001), (tx_ + side * 0.01, -0.022, drape(tx_ + side * 0.01, -0.022) + 0.001)]
        bl.sweep(g, end, 0.0009, "twine", segs=6)
    dome(g, (tx_, 0, kz), (0, 0, 1), 0.0026, "twine", segs=8)
    # the kraft tag tied to the twine, lying on the flap's front corner
    tag = Geo()
    tw, th_ = 0.017, 0.0115
    tag.add([(-tw, -th_, 0), (tw, -th_, 0), (tw, th_, 0), (-tw, th_, 0)], [(0, 1, 2, 3)], "tag",
            [((0, 0), (1, 0), (1, 1), (0, 1))])
    box(tag, (0, 0, -0.0004), (tw, th_, 0.0004), "paper")
    at = (0.07, -0.042)
    place = Matrix.Translation((at[0], at[1], drape(*at) + 0.0026)) @ Matrix.Rotation(math.radians(12), 4, "Z") \
        @ Matrix.Rotation(math.radians(-6), 4, "X")
    g.extend(tag, place)
    knot = Vector((tx_, -0.03, drape(tx_, -0.03) + 0.0014))
    string = [tuple(place @ Vector((-tw + 0.004, 0, 0.0008))), tuple(knot.lerp(place @ Vector((-tw, 0, 0.002)), 0.5) + Vector((0, 0, 0.002))),
              tuple(knot)]
    bl.sweep(g, string, 0.0007, "twine", segs=5)
    root = bl.make_empty("GrazerSteak")
    bl.make_object("Parcel", g, mats, root)
    return root


# =====================================================================================================================
# Held placement
# =====================================================================================================================

def placement(item):
    """Rest-pose (Blender world) placement of the item's base in the right fist: its axis up the thumb (as the hand
    holds a handle), the fist round it GRIP[item] up from the base, its front away from the palm."""
    import build_characters as bc
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    z = w.normalized()
    y = (n - z * n.dot(z)).normalized()
    x = y.cross(z)
    basis = Matrix((x, y, z)).transposed().to_4x4()
    grip = Vector(bc.grip_point("r")) + y * GRIP_OUT[item]
    return Matrix.Translation(grip - z * GRIP[item]) @ basis


def held_entry(item):
    import build_tomato as bt
    return bt.held_entry(placement(item))


# =====================================================================================================================
# Main
# =====================================================================================================================

ITEMS = {
    "bloom_tea": ("Bloom Tea", build_bloom_tea),
    "grazer_shake": ("Grazer Shake", build_grazer_shake),
    "fertilizer": ("Fertilizer", build_fertilizer),
    "grazer_milk": ("Grazer Milk", build_grazer_milk),
    "grazer_steak": ("Grazer Steak", build_grazer_steak),
}


def build(item):
    bbe.reset_scene()
    root = ITEMS[item][1]()
    objs = bbe.mesh_tree(root)
    lo, hi = bm.bounds(objs)
    assert all(abs(c) <= bm.HALF + 1e-4 for c in (*lo, *hi)), f"{item} leaves its 0.25 m cube: {lo} {hi}"
    return root, objs, (lo, hi, bm.triangles(root))


def main(argv):
    only = [a for a in argv if a in ITEMS] or list(ITEMS)
    held = {}
    for item in only:
        root, objs, (lo, hi, tris) = build(item)
        bbe.export(root, MESHES / f"{item}.glb", animations=False)
        bm.icon(objs, ICONS / f"{item}_icon.png", fill=1.16)
        root.location.z = -FLOOR                                   # in the hand: the base at the origin
        bpy.context.view_layer.update()
        bbe.export(root, MESHES / f"{item}_held.glb", animations=False)
        for p in (MESHES / f"{item}.glb", MESHES / f"{item}_held.glb", ICONS / f"{item}_icon.png"):
            print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
        print(f"{item}: {(hi - lo).x * 100:.1f} x {(hi - lo).y * 100:.1f} x {(hi - lo).z * 100:.1f} cm, {tris} triangles")
        held[item] = held_entry(item)
    for item, entry in held.items():
        print(f"held {item}:", json.dumps(entry))


if __name__ == "__main__":
    main(sys.argv[1:])
