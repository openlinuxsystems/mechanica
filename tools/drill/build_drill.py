"""Build the Drill (Drill spec of 2026-10-18, after the Scrap Mechanic drill Chase pointed to).

A 3x3x3-block part (0.75 m each way) with its axis on Blender +Z (glTF +Y, the part's up), the tip pointing up and
the mounting face down: a squat, polished steel bit (a broad shouldered dome, a stepped cone and a faceted point) on a
round base with an amber band, held by four steel claw brackets, one on each side, each with an amber-rimmed loop at
its foot and amber edging along its top. The base's underside is the mounting face the bearing turns.

What it writes: ``mods/vanilla/meshes/parts/drill.glb`` and ``mods/vanilla/textures/items/drill_icon.png`` (the item
icon the hotbar, inventory and Fabricator show). The model has two nodes: ``Drill`` (everything rigid) and
``BitGlow`` (an emissive amber ring in the base band). Its one clip, ``glow`` (1 s, scrubbed), grows the glow from
nothing at 0 s to full at 1 s; the drill's ``spin`` readout (how close it is to drilling speed) drives it.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/drill/build_drill.py                    # writes the .glb and the icon
    python tools/drill/build_drill.py --render OUT_DIR   # review renders and sheet into OUT_DIR

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
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "sport_suspension"))
sys.path.insert(0, str(HERE.parent / "saw_blade"))
import build_sport_suspension as sp  # noqa: E402  (shared materials)
import build_saw_blade as sb  # noqa: E402  (mesh helpers about the Z axis, the glow clip)

SAMPLES = int(os.environ.get("DRILL_SAMPLES", "64"))
FPS = 24

HALF = 0.375            # half the envelope: 3 blocks each way
Z_BASE = -HALF          # the mounting face
Z_BAND = -0.29          # the top of the base, where the amber band sits
Z_BIT = -0.25           # where the bit's dome starts
Z_TIP = HALF
R_BASE = 0.31
R_BAND = 0.3
BRACKET_R = 0.37        # the brackets' outer faces
BRACKET_W = 0.2         # their width round the bit
BRACKET_T = 0.075       # their thickness
BRACKET_TOP = 0.02      # the top of their inner edge
OFF = 1e-4              # a scale that hides a glow

STEEL = (0.56, 0.58, 0.62)
STEEL_DARK = (0.16, 0.17, 0.19)
AMBER = (0.62, 0.13, 0.0)
HOLE = (0.03, 0.03, 0.035)
GLOW = (1.0, 0.62, 0.12)

# The bit's outline as (radius, z): a broad shoulder curving in to a step, a cone to a second step, then the point.
BIT = ([(0.27, Z_BIT), (0.283, Z_BIT + 0.04), (0.285, Z_BIT + 0.09), (0.276, Z_BIT + 0.14), (0.258, Z_BIT + 0.19),
        (0.232, Z_BIT + 0.235), (0.205, Z_BIT + 0.27)]
       + [(0.186, Z_BIT + 0.27), (0.186, Z_BIT + 0.29)]
       + [(0.178, Z_BIT + 0.31), (0.16, Z_BIT + 0.35), (0.142, Z_BIT + 0.39)]
       + [(0.128, Z_BIT + 0.39), (0.128, Z_BIT + 0.405)])
POINT_BASE = Z_BIT + 0.405


def lathe(name, profile, mat, parent, segs=64, smooth=True, close_top=True):
    """A solid of revolution about Z from (radius, z) points, closed at radius 0 ends (and capped at the top)."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z))
                          for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[k], b[0], a[j]))
            else:
                bm.faces.new((a[k], b[k], b[j], a[j]))
    for end in (rings[0], rings[-1]) if close_top else (rings[0],):
        if len(end) > 1:
            bm.faces.new(end)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sb._object(name, bm, mat, parent, smooth=smooth)


def prism(name, outline, depth, mat, parent, frame, bevel=0.004):
    """An outline in a plane (u, v points) pushed `depth` m along that plane's normal; frame = (origin, u, v, n)."""
    o, u, v, n = (Vector(x) for x in frame)
    bm = bmesh.new()
    lo = [bm.verts.new(o + u * a + v * b) for a, b in outline]
    hi = [bm.verts.new(o + u * a + v * b + n * depth) for a, b in outline]
    bm.faces.new(lo[::-1])
    bm.faces.new(hi)
    for i in range(len(outline)):
        j = (i + 1) % len(outline)
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sb._bevel(sb._object(name, bm, mat, parent), bevel)


def rounded_rect(w, h, r, segs=4):
    """A rounded rectangle w x h about the origin, corners of radius r."""
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(segs + 1):
            a = math.radians(a0 + 90 * k / segs)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def loop(name, w, h, r, band, depth, frame, mat, parent):
    """A flat rounded-rectangle ring `band` m wide, pushed `depth` m along the frame's normal."""
    o, u, v, n = (Vector(x) for x in frame)
    outer, inner = rounded_rect(w, h, r), rounded_rect(w - 2 * band, h - 2 * band, max(0.004, r - band))
    bm = bmesh.new()
    rows = [[bm.verts.new(o + u * a + v * b + n * d) for a, b in pts] for pts in (outer, inner) for d in (0, depth)]
    (ol, oh), (il, ih) = (rows[0], rows[1]), (rows[2], rows[3])
    m = len(outer)
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((oh[i], oh[j], ih[j], ih[i]))
        bm.faces.new((ol[j], ol[i], il[i], il[j]))
        bm.faces.new((ol[i], ol[j], oh[j], oh[i]))
        bm.faces.new((il[j], il[i], ih[i], ih[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sb._bevel(sb._object(name, bm, mat, parent), 0.003)


def build(root_name="Drill"):
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    steel = sp.metal("steel", STEEL, 0.22, wear=0.5)
    point = sp.metal("point", (0.66, 0.68, 0.72), 0.16, wear=0.3)
    dark = sp.metal("steel_dark", STEEL_DARK, 0.35, wear=0.8)
    amber = sp.metal("amber", AMBER, 0.45, metallic=0.0, coat=0.15)
    claw = sp.metal("claw", (0.27, 0.29, 0.33), 0.32, wear=0.8)
    hole = sp.plain("hole", HOLE, 0.7)
    glow = sp.plain("glow", GLOW, 0.3, emission=GLOW, strength=5.0)

    # the base: a round plate with the mounting face underneath, a raised rim and the amber band above it
    lathe("Base", [(0.0, Z_BASE + 0.012), (R_BASE - 0.01, Z_BASE + 0.012), (R_BASE, Z_BASE + 0.022), (R_BASE, Z_BAND - 0.035),
                   (R_BASE - 0.012, Z_BAND - 0.03), (0.0, Z_BAND - 0.03)], dark, root, segs=96)
    sb.ring("Band", R_BAND, R_BAND - 0.05, Z_BAND - 0.031, Z_BAND, amber, root, segs=96, bevel=0.003)
    sb.ring("BandSeat", R_BAND - 0.04, 0.2, Z_BAND - 0.031, Z_BIT + 0.004, steel, root, segs=96, bevel=0.003)
    # the mounting face: a collar round a dark bore, and a ring of bolts, for the bearing
    sb.ring("Hub", 0.17, 0.075, Z_BASE, Z_BASE + 0.016, steel, root, segs=64, bevel=0.003)
    sb.disc("HubBore", 0.075, Z_BASE + 0.006, Z_BASE + 0.012, hole, root, segs=40)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        sb.bolt("HubBolt", 0.23 * math.cos(a), 0.23 * math.sin(a), Z_BASE + 0.012, -1, steel, root, r=0.014)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        sb.bolt("BaseBolt", 0.27 * math.cos(a), 0.27 * math.sin(a), Z_BAND, 1, dark, root, r=0.013)
    # the bit: a smooth shouldered dome and cone with two steps, and a faceted point
    lathe("Bit", BIT, steel, root, segs=96)
    lathe("Point", [(0.128, POINT_BASE - 0.002), (0.0, Z_TIP)], point, root, segs=10, smooth=False)
    # shallow grooves round the shoulder, as on a turned part
    for z, r in ((Z_BIT + 0.11, 0.284), (Z_BIT + 0.35, 0.16)):
        sb.ring("Groove", r + 0.002, r - 0.01, z - 0.004, z + 0.004, dark, root, segs=96)
    # four claw brackets, one on each side
    for k in range(4):
        a = k * math.pi / 2
        c, s = math.cos(a), math.sin(a)
        n, t, up = Vector((c, s, 0)), Vector((-s, c, 0)), Vector((0, 0, 1))
        # the claw's side outline (radial distance in from the outer face, z), leaning in towards the bit at the top
        side = [(0.0, Z_BASE + 0.005), (BRACKET_T + 0.03, Z_BASE + 0.005), (BRACKET_T + 0.03, Z_BAND - 0.02),
                (BRACKET_T, Z_BAND), (BRACKET_T + 0.035, BRACKET_TOP - 0.03), (BRACKET_T + 0.035, BRACKET_TOP),
                (0.05, BRACKET_TOP + 0.04), (0.012, BRACKET_TOP + 0.02), (0.0, -0.05)]
        origin = n * BRACKET_R + t * (-BRACKET_W / 2)
        prism("Bracket", [(-d, z) for d, z in side], BRACKET_W, claw, root, (origin, n, up, t), bevel=0.008)
        # a darker plate down the middle of its outer face, and amber edging along its top
        plate = [(-0.06, Z_BASE + 0.03), (0.06, Z_BASE + 0.03), (0.06, -0.08), (0.0, -0.045), (-0.06, -0.08)]
        prism("BracketPlate", plate, 0.006, dark, root, (n * (BRACKET_R - 0.004), t, up, n), bevel=0.003)
        for side_sign in (-1, 1):
            # amber strips down each side of the claw's upper part
            strip = [(-0.008, -0.2), (0.008, -0.2), (0.008, BRACKET_TOP - 0.01), (-0.008, BRACKET_TOP - 0.01)]
            centre = n * (BRACKET_R - 0.05) + t * side_sign * (BRACKET_W / 2 + 0.0)
            prism("BracketEdge", strip, 0.006, amber, root, (centre - t * side_sign * 0.003, n, up, t * side_sign), bevel=0.002)
        # the amber-rimmed loop at its foot, a dark hole inside
        foot = n * (BRACKET_R - 0.004) + up * (Z_BASE + 0.1)
        loop("Loop", 0.16, 0.135, 0.035, 0.036, 0.018, (foot, t, up, n), amber, root)
        prism("LoopHole", rounded_rect(0.09, 0.064, 0.01), 0.004, hole, root, (foot - n * 0.002, t, up, n), bevel=0.0)
    # the glow: a ring just proud of the amber band
    glows = [sb.ring("BitGlow", R_BAND - 0.006, R_BAND - 0.044, Z_BAND, Z_BAND + 0.002, glow, root, segs=96)]
    return root, glows


# =====================================================================================================
# Review renders and the icon
# =====================================================================================================
def render_review(out: Path, icons_only=False):
    sys.path.insert(0, str(HERE.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ground = rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    root, glows = build()

    def pose(lying, glow, turn=0.0):
        """lying: the axis level (along Blender -X, the tip to the left), as on a bearing on a vehicle's front."""
        root.rotation_euler = (0, math.radians(-90), turn) if lying else (0, 0, turn)
        root.location = (0, 0, HALF + 0.002)
        for g in glows:
            g.scale = (glow, glow, 1) if glow > OFF else (OFF, OFF, OFF)
        bpy.context.view_layer.update()

    shots = {}
    if not icons_only:
        pose(False, OFF)
        shots["front"] = (rl.render(rl.camera("front", (0, 0, 4), (0, 0, HALF), ortho=1.05), out / "drill_tip_on.png", 900, 900),)
        cam = rl.camera("side", (0, -4, HALF), (0, 0, HALF), ortho=1.05)
        shots["side"] = (rl.render(cam, out / "drill_side.png", 900, 900), cam)
        shots["top"] = (rl.render(rl.camera("diag", (-2.83, -2.83, HALF), (0, 0, HALF), ortho=1.05), out / "drill_side_45.png", 900, 900),)
        shots["tq"] = (rl.render(rl.camera("tq", (-1.6, -2.7, 2.0), (0, 0, HALF * 0.9), ortho=1.3), out / "drill_three_quarter.png", 900, 800),)
        pose(True, OFF)
        shots["back"] = (rl.render(rl.camera("back", (4, 0, HALF), (0, 0, HALF), ortho=1.05), out / "drill_back.png", 900, 900),)
        shots["rear_tq"] = (rl.render(rl.camera("rtq", (2.4, -2.4, 1.7), (0, 0, HALF), ortho=1.35), out / "drill_rear_three_quarter.png", 900, 800),)
        pose(False, 1.0)
        shots["glow"] = (rl.render(rl.camera("glow", (-2.2, -2.2, 1.7), (0, 0, HALF * 0.9), ortho=1.3), out / "drill_at_speed.png", 900, 800),)
    ground.hide_render = True
    sc.render.film_transparent = True
    pose(False, OFF)
    # tip up, seen from above between two brackets, as the game's other part icons are
    root.rotation_euler = (0, 0, math.radians(45))
    root.location = (0, 0, 0)
    p = rl.render(rl.camera("icon", (0, -3.4, 2.0), (0, 0, -0.02), ortho=1.2), out / "drill_icon_raw.png", 512, 512)
    im = Image.open(p).convert("RGBA")
    im = im.crop(im.getbbox())
    side = max(im.size)
    pad = int(side * 0.05)
    sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
    sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2))
    icon = sq.resize((256, 256), Image.LANCZOS)
    icon.save(out / "drill_icon.png")
    os.remove(p)
    if icons_only:
        return

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2860, 2120), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "DRILL (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "3 x 3 x 3 blocks (75 cm each way). A squat steel bit held by four claw brackets with amber trim; "
           "the round base's underside is the mounting face the bearing turns.", fill=INK, font=F(24))
    tiles = [("TIP-ON", "front", (60, 170)), ("SIDE", "side", (720, 170)), ("SIDE, 45 DEGREES", "top", (1380, 170)),
             ("MOUNTING FACE", "back", (2040, 170))]
    w = 620
    for label, key, (x, y) in tiles:
        im = Image.open(shots[key][0]).convert("RGB").resize((w, w), Image.LANCZOS)
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + w - 1, y + w - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(26, True))
    cam = shots["side"][1]
    a = rl.project(cam, (-HALF, 0, -0.03), w, w)
    b = rl.project(cam, (HALF, 0, -0.03), w, w)
    rl.dim_line(d, (720 + a[0], 170 + a[1] - 22), (720 + b[0], 170 + b[1] - 22), "75 cm (3 blocks)", F(22))
    a = rl.project(cam, (HALF + 0.03, 0, 0), w, w)
    b = rl.project(cam, (HALF + 0.03, 0, 2 * HALF), w, w)
    rl.dim_line(d, (720 + a[0] + 8, 170 + a[1]), (720 + b[0] + 8, 170 + b[1]), "75 cm", F(22), vertical=True)
    y = 850
    for label, key, x in (("THREE-QUARTER", "tq", 60), ("ON ITS SIDE, FROM BEHIND", "rear_tq", 1000),
                          ("AT DRILLING SPEED (base band glow)", "glow", 1940)):
        im = Image.open(shots[key][0]).convert("RGB")
        im = im.resize((880, int(im.height * 880 / im.width)), Image.LANCZOS)
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(26, True))
    tile = Image.new("RGB", (276, 276), (40, 42, 46))
    tile.paste(icon, (10, 10), icon)
    sheet.paste(tile, (60, 1700))
    d.text((360, 1820), "hotbar / inventory / Fabricator icon (256 px)", fill=INK, font=F(22))
    sheet.save(out / "review_sheet.png")
    print(f"rendered the review sheet into {out}")


# =====================================================================================================
# The game model
# =====================================================================================================
def export_model():
    sp.GAME = True
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, glows = build()
    rigid = [o for o in root.children if o not in glows]
    sp.join(rigid, "Drill").parent = root
    glow = sp.join(glows, "BitGlow")
    glow.parent = root
    # the glow clip: nothing at 0 s, full at 1 s
    glow.animation_data_create()
    act = bpy.data.actions.new("glow_BitGlow")
    glow.animation_data.action = act
    for f in range(0, FPS + 1, 4):
        v = max(OFF, f / FPS)
        glow.scale = (v, v, 1.0)
        glow.keyframe_insert("scale", frame=f)
    sb.linear_keys(act)
    track = glow.animation_data.nla_tracks.new()
    track.name = "glow"
    strip = track.strips.new("glow", 0, act)
    strip.action_frame_start, strip.action_frame_end = act.frame_range
    glow.animation_data.action = None
    glow.animation_data.use_nla = False
    glow.scale = (OFF, OFF, OFF)
    path = sp.VANILLA / "meshes" / "parts" / "drill.glb"
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root, *root.children_recursive]:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=False,
        export_extras=False, export_cameras=False, export_lights=False)
    sp.GAME = False
    print(f"wrote {path}")


def write_icon():
    with tempfile.TemporaryDirectory() as tmp:
        render_review(Path(tmp), icons_only=True)
        dest = sp.VANILLA / "textures" / "items" / "drill_icon.png"
        shutil.copyfile(Path(tmp) / "drill_icon.png", dest)
        print(f"wrote {dest}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--render" in args:
        render_review(Path(args[args.index("--render") + 1]))
    else:
        export_model()
        write_icon()
