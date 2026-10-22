"""Build the item models and icons of consumables batch C2 (consumables/CONSUMABLES_MAP.md, items 1 to 3).

What it builds (procedurally, from scratch: no .blend inputs, no downloads), each a 1 x 1 x 1 block item standing on
z = -0.125 inside the 0.25 m cube (front -Y, Z up):

* ``meshes/items/bloom_tea.glb`` - a Colony Works glass bottle of rose-red Bloom Tea: a steel-blue screw cap, a bone
  wrap label in steel-blue bands with a five-petal bloom pictogram and BT-1, and on its back a steaming cup.
* ``meshes/items/grazer_shake.glb`` - a tall Colony Works shake bottle: a bone plastic body with a grip waist, a
  steel-blue sleeve with a big Grazer head pictogram over a milk splash and GS-1, a mustard sports cap with a flip spout.
* ``meshes/items/fertilizer.glb`` - a Rootwell stand-up pouch (the garden brand of the Soil Bag, tools/soil/bag_print.py):
  bright yellow, the green Rootwell band with the sprout badge across the top, FERTILIZER in big green letters, a
  seedling rising out of soil with up chevrons and pale granules; a crimped top seal with a tear notch, a zip ridge.
* ``meshes/items/<id>_held.glb`` for each: the same model, its base at the origin, which the game puts in the hand.
* ``textures/items/<id>_icon.png`` for each: 256 x 256.
* the ``held`` offset and rotation of each, printed (they go in mods/vanilla/editor/batches/consumables.json).

    pip install bpy==5.2.2 numpy pillow
    python tools/consumables/build_consumables.py [bloom_tea] [grazer_shake] [fertilizer]
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
GRIP = {"bloom_tea": 0.095, "grazer_shake": 0.105, "fertilizer": 0.205}
# How far the item's axis sits from the fist's centre toward the fingers (m): a bottle is fatter than a handle.
GRIP_OUT = {"bloom_tea": 0.012, "grazer_shake": 0.016, "fertilizer": 0.0}


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
