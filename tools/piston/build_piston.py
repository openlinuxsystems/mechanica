"""Build the Piston, levels 1 to 5 (Chase's Piston design spec and reference sheet of 2026-10-19).

One 1 x 1 x 1 block body (0.25 m) shared by every level, standing along Blender +Z (the part's +Y, where its head is):

* a gunmetal base plate with teal corner brackets, teal side panels and copper rivets, a stepped plinth and a
  copper-sleeved neck the rod comes out of;
* the rod: square gunmetal segments 0.125 m long with seams between them (and teal bands from level 2);
* the head: a copper collar under a square gunmetal head plate with copper corner caps and the teal four-way cross.

The levels change finish and add trim only, never the geometry's envelope or its mounting (see ``LEVELS``): level 1 is
worn gunmetal with matte teal and copper; each level is glossier, adds teal banding on the rod, rivets and plates, and
level 5 is black chrome with a glowing cross and corner lights. Pips on the base's front show the level.

What it writes: ``mods/vanilla/meshes/parts/piston_<level>.glb`` and ``mods/vanilla/textures/items/piston_<level>_icon.png``
(256 x 256, clear background) for levels 1 to 5. Each model has three nodes, retracted: ``Base`` (stays on the block the
piston is placed on), ``head`` (named after the part's linear socket, so the game moves it with what is on the head) and
``headRod`` (one rod segment, modelled inside the neck with its top under the head; the game repeats it along the
stroke so the rod comes out of the neck segment by segment as the head moves out).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/piston/build_piston.py                    # writes the .glb files and the icons
    python tools/piston/build_piston.py --render OUT_DIR   # review renders and sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up. The build is deterministic.
"""

from __future__ import annotations

import math
import os
import shutil
import sys
import tempfile
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "sport_suspension"))
import build_sport_suspension as sp  # noqa: E402  (shared materials and mesh helpers)

BLOCK = 0.25
HALF = BLOCK / 2
SAMPLES = int(os.environ.get("PISTON_SAMPLES", "64"))

# heights (Blender Z), the part's centre at 0
Z_BOTTOM = -HALF
Z_PLATE = -0.06        # top of the base plate
Z_PLINTH = -0.04       # top of the stepped plinth
Z_NECK = 0.02          # top of the neck: where the rod comes out and the head's collar sits when retracted
Z_HEAD = 0.072         # underside of the head plate
SEGMENT = 0.125        # one rod segment (two per block of stroke)
ROD = 0.088            # rod width
NECK = 0.122           # neck width
COLLAR = 0.112         # head collar width
TEAL = (0.03, 0.33, 0.33)

# Each level: body and panel finish, how worn, trim metal, rod banding and what lights up.
LEVELS = {
    1: dict(body=(0.16, 0.165, 0.175), rough=0.55, wear=1.0, coat=0.0, teal=TEAL, teal_rough=0.6, flake=0.0,
            trim="copper", rod_bands=0, rivets=False, plates=False, lit=0.0, rod=(0.2, 0.205, 0.215)),
    2: dict(body=(0.17, 0.175, 0.185), rough=0.34, wear=0.4, coat=0.0, teal=(0.02, 0.38, 0.38), teal_rough=0.4,
            flake=0.6, trim="copper", rod_bands=1, rivets=False, plates=False, lit=0.0, rod=(0.22, 0.225, 0.235)),
    3: dict(body=(0.1, 0.104, 0.112), rough=0.28, wear=0.15, coat=0.8, teal=(0.0, 0.42, 0.44), teal_rough=0.25,
            flake=0.8, trim="copper_bright", rod_bands=1, rivets=True, plates=False, lit=0.0, rod=(0.2, 0.205, 0.215)),
    4: dict(body=(0.06, 0.062, 0.07), rough=0.24, wear=0.0, coat=1.0, teal=(0.0, 0.46, 0.5), teal_rough=0.2,
            flake=0.8, trim="copper_bright", rod_bands=2, rivets=True, plates=True, lit=0.6, rod=(0.16, 0.165, 0.175)),
    5: dict(body=(0.02, 0.021, 0.025), rough=0.12, wear=0.0, coat=1.0, teal=(0.0, 0.5, 0.56), teal_rough=0.15,
            flake=0.5, trim="copper_bright", rod_bands=2, rivets=True, plates=True, lit=4.0, rod=(0.1, 0.103, 0.11)),
}
GLOW = (0.1, 0.95, 0.9)


# =====================================================================================================
# Mesh helpers (standing along Z)
# =====================================================================================================
def chamfered(half_x, half_y, c):
    return [(-half_x + c, -half_y), (half_x - c, -half_y), (half_x, -half_y + c), (half_x, half_y - c),
            (half_x - c, half_y), (-half_x + c, half_y), (-half_x, half_y - c), (-half_x, -half_y + c)]


def zprism(name, outline, z0, z1, mat, parent, bevel=0.003, at=(0.0, 0.0)):
    """Extrude an XY outline from z0 to z1, bevelled."""
    bm = bmesh.new()
    vs = [bm.verts.new((x + at[0], y + at[1], z0)) for x, y in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.z = z1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = sp._object(name, bm, mat, parent, smooth=False)
    if bevel > 0:
        mod = ob.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
    return ob


def cube(name, size, center, mat, parent, bevel=0.002):
    sx, sy, sz = size
    return zprism(name, chamfered(sx / 2, sy / 2, 0.0001), center[2] - sz / 2, center[2] + sz / 2, mat, parent, bevel,
                  (center[0], center[1]))


def stud(name, r, h, at, normal, mat, parent):
    """A short round rivet head of radius r and height h at point at, sticking out along an axis-aligned normal."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=r, radius2=r * 0.7, depth=h)
    ob = sp._object(name, bm, mat, parent)
    nx, ny, nz = normal
    ob.rotation_euler = (math.radians(-90) * ny, math.radians(90) * nx, 0) if nz == 0 else ((0, 0, 0) if nz > 0 else (math.pi, 0, 0))
    ob.location = (at[0] + nx * h / 2, at[1] + ny * h / 2, at[2] + nz * h / 2)
    return ob


# =====================================================================================================
# Build one level
# =====================================================================================================
def build(level: int, root_name=None):
    """The retracted piston: three child groups named Base*, head* and Rod*."""
    L = LEVELS[level]
    root = bpy.data.objects.new(root_name or f"Piston{level}", None)
    bpy.context.scene.collection.objects.link(root)
    tag = f"P{level}"
    body = sp.metal(f"body{tag}", L["body"], L["rough"], wear=L["wear"], coat=L["coat"])
    rod = sp.metal(f"rod{tag}", L["rod"], max(0.1, L["rough"] - 0.1), wear=0.5 * L["wear"], coat=L["coat"])
    teal = sp.metal(f"teal{tag}", L["teal"], L["teal_rough"], metallic=0.55, coat=0.5 * L["coat"], flake=L["flake"])
    trim = sp.metal(f"trim{tag}", *sp.TRIM[L["trim"]])
    dark = sp.plain(f"seam{tag}", (0.012, 0.012, 0.014), 0.7)
    rubber = sp.plain(f"rubber{tag}", (0.02, 0.02, 0.022), 0.85)
    cross = (sp.plain(f"cross{tag}", L["teal"], 0.3, emission=GLOW, strength=L["lit"]) if L["lit"] else teal)
    lit = sp.plain(f"lit{tag}", GLOW, 0.3, emission=GLOW, strength=max(1.0, L["lit"]))

    # ---- base: plate, teal corner brackets and side panels, plinth, neck
    zprism("BasePlate", chamfered(0.124, 0.124, 0.012), Z_BOTTOM, Z_PLATE, body, root, 0.004)
    zprism("BaseFoot", chamfered(0.118, 0.118, 0.01), Z_BOTTOM - 0.0005, Z_BOTTOM + 0.006, rubber, root, 0.002)
    h = (Z_PLATE - Z_BOTTOM)
    zc = (Z_PLATE + Z_BOTTOM) / 2
    for sx in (-1, 1):
        for sy in (-1, 1):
            # an L-shaped teal bracket hugging each corner
            k = 0.042
            outline = [(0, 0), (k, 0), (k, 0.012), (0.012, 0.012), (0.012, k), (0, k)]
            outline = [(sx * (0.1255 - x), sy * (0.1255 - y)) for x, y in outline]
            if sx * sy < 0:
                outline.reverse()
            zprism("BaseBracket", outline, Z_BOTTOM + 0.003, Z_PLATE + 0.004, teal, root, 0.0015)
            for nx, ny in ((sx, 0), (0, sy)):
                p = (sx * 0.125 - (0 if nx else sx * 0.026), sy * 0.125 - (0 if ny else sy * 0.026), zc + 0.002)
                stud("BaseRivet", 0.0055, 0.004, (p[0] + nx * 0.0005, p[1] + ny * 0.0005, p[2]), (nx, ny, 0), trim, root)
            stud("BaseRivet", 0.0055, 0.004, (sx * 0.104, sy * 0.104, Z_PLATE + 0.004), (0, 0, 1), trim, root)
    for side in range(4):
        # a recessed teal panel on each face, with a seam line above it
        a = side * math.pi / 2
        ux, uy = round(math.cos(a)), round(math.sin(a))
        size = (0.003, 0.13, h * 0.42) if ux else (0.13, 0.003, h * 0.42)
        cube("BasePanel", size, (ux * 0.1245, uy * 0.1245, zc - 0.003), teal, root, 0.0008)
        if L["plates"]:
            psize = (0.002, 0.15, 0.007) if ux else (0.15, 0.002, 0.007)
            cube("BaseTrimBar", psize, (ux * 0.1262, uy * 0.1262, Z_PLATE - 0.006), trim, root, 0.0006)
        if L["rivets"]:
            for t in (-0.05, 0.05):
                p = (ux * 0.1265 + (0 if ux else t), uy * 0.1265 + (0 if uy else t), zc - 0.003)
                stud("BaseRivet", 0.0035, 0.003, p, (ux, uy, 0), trim, root)
    # level pips on the front edge of the plate's top (Blender -Y is the part's front)
    for i in range(level):
        x = (i - (level - 1) / 2) * 0.014
        stud("BasePip", 0.0042, 0.003, (x, -0.112, Z_PLATE), (0, 0, 1), lit if L["lit"] else trim, root)
    zprism("BasePlinth", chamfered(0.086, 0.086, 0.014), Z_PLATE - 0.001, Z_PLINTH, body, root, 0.003)
    zprism("BasePlinthLip", chamfered(0.09, 0.09, 0.016), Z_PLATE - 0.001, Z_PLATE + 0.004, rubber, root, 0.0015)
    zprism("BaseNeck", chamfered(NECK / 2, NECK / 2, 0.012), Z_PLINTH - 0.001, Z_NECK - 0.006, trim, root, 0.0025)
    zprism("BaseNeckSeal", chamfered(NECK / 2 - 0.004, NECK / 2 - 0.004, 0.011), Z_NECK - 0.0065, Z_NECK, rubber, root, 0.0015)
    for side in range(4):
        a = side * math.pi / 2
        ux, uy = round(math.cos(a)), round(math.sin(a))
        stud("BaseNeckRivet", 0.0045, 0.003, (ux * NECK / 2, uy * NECK / 2, (Z_PLINTH + Z_NECK) / 2 - 0.003), (ux, uy, 0),
             body, root)
    # the bore the rod slides through, dark, in the neck's top seal
    zprism("BaseBore", chamfered(ROD / 2 + 0.003, ROD / 2 + 0.003, 0.006), Z_NECK - 0.0005, Z_NECK + 0.0004, dark, root, 0)

    # ---- one rod segment, retracted: top at Z_NECK, inside the neck and plate
    z0, z1 = Z_NECK - SEGMENT, Z_NECK
    zprism("RodBody", chamfered(ROD / 2, ROD / 2, 0.006), z0 + 0.003, z1, rod, root, 0.002)
    zprism("RodSeam", chamfered(ROD / 2 - 0.002, ROD / 2 - 0.002, 0.006), z0, z0 + 0.003, dark, root, 0)
    # a seam half way, so a block of stroke shows four rod blocks as on the reference
    zprism("RodSeamMid", chamfered(ROD / 2 + 0.0004, ROD / 2 + 0.0004, 0.006), z0 + SEGMENT / 2 - 0.001,
           z0 + SEGMENT / 2 + 0.001, dark, root, 0)
    if L["rod_bands"]:
        zprism("RodBand", chamfered(ROD / 2 + 0.0015, ROD / 2 + 0.0015, 0.0065), z0 + 0.03, z0 + 0.03 + 0.018, teal, root,
               0.001)
    if L["rod_bands"] >= 2:
        zprism("RodRing", chamfered(ROD / 2 + 0.0012, ROD / 2 + 0.0012, 0.0065), z0 + 0.09, z0 + 0.095, trim, root, 0.0006)

    # ---- head: copper collar, flange, head plate with corner caps and the teal cross
    zprism("headCollar", chamfered(COLLAR / 2, COLLAR / 2, 0.012), Z_NECK, Z_HEAD - 0.012, trim, root, 0.003)
    zprism("headFlange", chamfered(0.066, 0.066, 0.014), Z_HEAD - 0.013, Z_HEAD + 0.001, body, root, 0.003)
    for side in range(4):
        a = side * math.pi / 2
        ux, uy = round(math.cos(a)), round(math.sin(a))
        stud("headCollarRivet", 0.0045, 0.003, (ux * COLLAR / 2, uy * COLLAR / 2, (Z_NECK + Z_HEAD) / 2 - 0.006), (ux, uy, 0),
             body, root)
    zprism("headPlate", chamfered(0.124, 0.124, 0.01), Z_HEAD, HALF - 0.003, body, root, 0.004)
    zprism("headPlateRim", chamfered(0.12, 0.12, 0.009), Z_HEAD - 0.004, Z_HEAD + 0.001, rubber, root, 0.0015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            k = 0.036
            outline = [(0, 0), (k, 0), (k, 0.011), (0.011, 0.011), (0.011, k), (0, k)]
            outline = [(sx * (0.1255 - x), sy * (0.1255 - y)) for x, y in outline]
            if sx * sy < 0:
                outline.reverse()
            zprism("headCap", outline, Z_HEAD - 0.002, HALF - 0.0015, trim, root, 0.0015)
            stud("headRivet", 0.0048, 0.003, (sx * 0.104, sy * 0.104, HALF - 0.003), (0, 0, 1), body, root)
    # the four-way cross, a raised teal inlay on top
    arm, w = 0.064, 0.022
    zprism("headCross", [(-w / 2, -arm), (w / 2, -arm), (w / 2, -w / 2), (arm, -w / 2), (arm, w / 2), (w / 2, w / 2),
                         (w / 2, arm), (-w / 2, arm), (-w / 2, w / 2), (-arm, w / 2), (-arm, -w / 2), (-w / 2, -w / 2)],
           HALF - 0.0035, HALF, cross, root, 0.001)
    for ux, uy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        # arrow heads at the cross's ends
        tip = arm + 0.012
        tri = [(ux * tip, uy * tip), (ux * arm - uy * 0.019, uy * arm + ux * 0.019), (ux * arm + uy * 0.019, uy * arm - ux * 0.019)]
        if (ux, uy) in ((-1, 0), (0, -1)):
            tri.reverse()
        zprism("headCrossTip", tri, HALF - 0.0035, HALF, cross, root, 0.0008)
    if L["lit"]:
        for sx in (-1, 1):
            for sy in (-1, 1):
                stud("BaseLight", 0.004, 0.002, (sx * 0.118, sy * 0.118, Z_PLATE + 0.004), (0, 0, 1), lit, root)
    return root


def pose(root, stroke):
    """Moves a built level's head out by stroke m and fills the gap with rod segments (review renders and icons)."""
    for ob in list(root.children):
        if ob.name.startswith("head"):
            ob.location.z += stroke
    rods = [ob for ob in root.children if ob.name.startswith("Rod")]
    n = math.ceil(stroke / SEGMENT - 1e-3)
    for i in range(n):
        for ob in rods:
            copy = ob.copy()
            bpy.context.scene.collection.objects.link(copy)
            copy.parent = root
            copy.location.z += stroke - i * SEGMENT


# =====================================================================================================
# Review renders, icons
# =====================================================================================================
def render_review(out: Path, icons_only=False):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ground = rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    shots, views, icons = {}, {}, {}

    def stage(level, stroke):
        for ob in list(bpy.data.objects):
            if not ob.name.startswith("RenderOnly"):
                bpy.data.objects.remove(ob)
        root = build(level)
        pose(root, stroke)
        root.location.z = HALF
        return root

    if not icons_only:
        for lv in LEVELS:
            stage(lv, 0.0)
            cam = rl.camera(f"tq{lv}", (0.75, -0.95, 0.72), (0, 0, HALF + 0.02), ortho=0.42)
            shots[(lv, "retracted")] = rl.render(cam, out / f"piston_{lv}_retracted.png", 700, 700)
            stage(lv, 4 * BLOCK)
            cam = rl.camera(f"ex{lv}", (1.1, -1.4, 1.1), (0, 0, 0.62), ortho=1.45)
            shots[(lv, "extended")] = rl.render(cam, out / f"piston_{lv}_extended.png", 700, 1000)
        for lv in (1, 5):
            stage(lv, 0.0)
            for name, loc in (("front", (0, -2, HALF)), ("side", (2, 0, HALF)), ("top", (0, -0.0001, 2))):
                cam = rl.camera(f"{name}{lv}", loc, (0, 0, HALF if name != "top" else 0), ortho=0.36)
                views[(lv, name)] = (rl.render(cam, out / f"piston_{lv}_{name}.png", 500, 500), cam)
        # the extend animation at level 1, five steps of the stroke
        for k, stroke in enumerate((0.0, 0.25, 0.75, 1.25, 1.75)):
            stage(1, stroke)
            cam = rl.camera(f"anim{k}", (1.25, -1.6, 1.2), (0, 0, 0.85), ortho=2.15)
            shots[("anim", k)] = rl.render(cam, out / f"piston_extend_{k}.png", 420, 800)
    ground.hide_render = True
    sc.render.film_transparent = True
    for lv in LEVELS:
        stage(lv, 0.75 * BLOCK)
        cam = rl.camera(f"icon{lv}", (0.75, -0.95, 0.85), (0, 0, HALF + 0.09), ortho=0.6)
        p = rl.render(cam, out / f"piston_{lv}_icon_raw.png", 512, 512)
        im = Image.open(p).convert("RGBA")
        im = im.crop(im.getbbox())
        side = max(im.size)
        pad = int(side * 0.06)
        sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
        sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2))
        icons[lv] = sq.resize((256, 256), Image.LANCZOS)
        icons[lv].save(out / f"piston_{lv}_icon.png")
        os.remove(p)
    if icons_only:
        return

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (5 * 560 + 120, 3000), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "PISTON - LEVELS 1 TO 5 (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "Same body at every level: 1 x 1 x 1 block (25 cm). Gunmetal base and head, teal brackets, panels and "
           "cross, copper fittings. The rod comes out of the neck segment by segment; the pips show the level.",
           fill=INK, font=F(24))
    notes = {
        1: ["Worn gunmetal, matte teal", "Plain rod, 1 pip", "Range 7 blocks, speed 2", "Force 5000 N (craftable)"],
        2: ["Polished metal, flaked teal", "Teal band on every rod segment", "Range 9, speed 4", "Force 10000 N"],
        3: ["Clear-coated dark gunmetal", "Bright copper, rivets on panels", "Range 11, speed 6", "Force 15000 N"],
        4: ["Copper trim bars, copper rod rings", "Softly lit cross and pips", "Range 13, speed 8", "Force 20000 N"],
        5: ["Black chrome, glowing cross", "Corner lights, lit pips", "Range 15, speed 10", "Force 25000 N"],
    }
    for i, lv in enumerate(LEVELS):
        x = 60 + i * 560
        sheet.paste(Image.open(shots[(lv, "retracted")]).convert("RGB").resize((540, 540), Image.LANCZOS), (x, 190))
        d.rectangle([x, 190, x + 539, 729], outline=(190, 190, 190), width=2)
        d.text((x + 16, 202), f"LEVEL {lv}", fill=INK, font=F(34, True))
        ex = Image.open(shots[(lv, "extended")]).convert("RGB").resize((378, 540), Image.LANCZOS)
        sheet.paste(ex, (x + 81, 745))
        d.rectangle([x + 81, 745, x + 458, 1284], outline=(190, 190, 190), width=2)
        d.text((x + 92, 755), "4 blocks out", fill=INK, font=F(20))
        for k, line in enumerate(notes[lv]):
            d.text((x + 10, 1300 + k * 32), line, fill=INK, font=F(24))
        ic = icons[lv].resize((170, 170), Image.LANCZOS)
        tile = Image.new("RGB", (180, 180), (40, 42, 46))
        tile.paste(ic, (5, 5), ic)
        sheet.paste(tile, (x + 180, 1440))
        d.text((x + 136, 1626), "hotbar icon (256 px)", fill=INK, font=F(20))
    d.text((60, 1690), "LEVEL 1 AND LEVEL 5 - FRONT, SIDE AND TOP; EXTEND ANIMATION (LEVEL 1, 0 TO 7 BLOCKS)", fill=INK,
           font=F(30, True))
    y = 1740
    for lv in (1, 5):
        xx = 60
        for name in ("front", "side", "top"):
            p, cam = views[(lv, name)]
            im = Image.open(p).convert("RGB").resize((400, 400), Image.LANCZOS)
            sheet.paste(im, (xx, y))
            dd = ImageDraw.Draw(sheet)
            dd.rectangle([xx, y, xx + 399, y + 399], outline=(190, 190, 190), width=2)
            dd.text((xx + 12, y + 10), f"L{lv} {name.upper()}", fill=INK, font=F(24, True))
            if name == "front":
                a = rl.project(cam, (-HALF, -0.2, 0.0), 500, 500)
                b = rl.project(cam, (HALF, -0.2, 0.0), 500, 500)
                rl.dim_line(dd, (xx + a[0] * 0.8, y + a[1] * 0.8 - 30), (xx + b[0] * 0.8, y + b[1] * 0.8 - 30),
                            "25 cm (1 block)", F(20))
            xx += 420
        y += 420
    xx = 60 + 3 * 420 + 20
    for k in range(5):
        im = Image.open(shots[("anim", k)]).convert("RGB").resize((252, 480), Image.LANCZOS)
        sheet.paste(im, (xx + k * 262, 1740))
        d.rectangle([xx + k * 262, 1740, xx + k * 262 + 251, 2219], outline=(190, 190, 190), width=2)
        d.text((xx + k * 262 + 10, 2226), ["0", "1 block", "3 blocks", "5 blocks", "7 blocks"][k], fill=INK, font=F(22))
    sheet = sheet.crop((0, 0, sheet.width, 2600))
    sheet.save(out / "review_sheet.png")
    strip = Image.new("RGBA", (5 * 256 + 6 * 16, 288), (40, 42, 46, 255))
    for i, lv in enumerate(LEVELS):
        strip.paste(icons[lv], (16 + i * 272, 16), icons[lv])
    strip.save(out / "hotbar_icons.png")
    print(f"rendered the review sheet into {out}")


# =====================================================================================================
# Game models and icons
# =====================================================================================================
def export_level(level: int):
    sp.GAME = True
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = build(level, "Piston")
    parts = list(root.children)
    groups = {
        "Base": [o for o in parts if o.name.startswith("Base")],
        "head": [o for o in parts if o.name.startswith("head")],
        "headRod": [o for o in parts if o.name.startswith("Rod")],
    }
    for name, objects in groups.items():
        sp.join(objects, name).parent = root
    path = sp.VANILLA / "meshes" / "parts" / f"piston_{level}.glb"
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root, *root.children_recursive]:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=False, export_skins=False, export_morph=False, export_draco_mesh_compression_enable=False,
        export_tangents=False, export_extras=False, export_cameras=False, export_lights=False)
    sp.GAME = False
    print(f"wrote {path}")


def write_icons():
    with tempfile.TemporaryDirectory() as tmp:
        render_review(Path(tmp), icons_only=True)
        for level in LEVELS:
            dest = sp.VANILLA / "textures" / "items" / f"piston_{level}_icon.png"
            shutil.copyfile(Path(tmp) / f"piston_{level}_icon.png", dest)
            print(f"wrote {dest}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--render" in args:
        render_review(Path(args[args.index("--render") + 1]))
    else:
        for lv in LEVELS:
            export_level(lv)
        write_icons()
