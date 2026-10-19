"""Build the Drill (design spec of 2026-10-18 and Chase's reference sheet).

A 3x3x3-block part (0.75 m each way) with its axis on Blender +Z (glTF +Y, the part's up), the tip pointing up and
the mounting face down: a chamfered gunmetal housing of bolted corner pillars and riveted side plates (a teal inset
on two sides), a round collar with a teal bearing ring and a dark bore on the mounting face, and in front a teal ring
round the base of the bit. The bit is a tapering dark-steel core wound with three turns of a copper helical flute
that ends in a sharp steel point at the top of the envelope.

What it writes: ``mods/vanilla/meshes/parts/drill.glb`` and ``mods/vanilla/textures/items/drill_icon.png`` (the item
icon the hotbar, inventory and Fabricator show). The model has two nodes: ``Drill`` (everything rigid) and
``BitGlow`` (an emissive teal ring in the bit's base ring). Its one clip, ``glow`` (1 s, scrubbed), grows the glow
from nothing at 0 s to full at 1 s; the drill's ``spin`` readout (how close it is to drilling speed) drives it.

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
Z_BACK = -HALF          # the mounting face
Z_HOUSING_BACK = -0.325
Z_HOUSING_FRONT = -0.06
Z_TIP = HALF
Z_BIT = -0.03           # where the bit leaves the base ring
R_BIT_BASE = 0.2        # the core's radius at its base
R_BIT_NECK = 0.075      # the core's radius where the point starts
Z_NECK = 0.27
FLUTE_TURNS = 2.75
FLUTE_OUT = 0.032       # how far the flute stands off the core
FLUTE_H = 0.058         # the flute's height along the axis
CORNER = 0.19           # the corner pillars' width
R_COLLAR, R_BORE = 0.2, 0.1
R_TEAL_OUT, R_TEAL_IN = 0.295, 0.245   # the ring round the bit's base
OFF = 1e-4              # a scale that hides a glow

GUNMETAL = (0.085, 0.09, 0.095)
GUNMETAL_DARK = (0.045, 0.047, 0.05)
STEEL = (0.16, 0.165, 0.17)
COPPER = (0.50, 0.21, 0.11)
TEAL = (0.015, 0.20, 0.22)
GLOW = (0.1, 0.95, 1.0)


def core_radius(z):
    """The bit core's radius at height z, tapering from its base to the neck of the point."""
    t = min(1.0, max(0.0, (z - Z_BIT) / (Z_NECK - Z_BIT)))
    # a short straight shank, then a steady taper
    t = max(0.0, (t - 0.18) / 0.82)
    return R_BIT_BASE + (R_BIT_NECK - R_BIT_BASE) * t


def helix(name, z0, z1, turns, mat, parent, phase=0.0, steps=360):
    """A copper flute wound round the core from z0 to z1: a strap FLUTE_H wide lying on the cone and standing FLUTE_OUT
    proud of it, narrowing towards the point."""
    bm = bmesh.new()
    rings = []
    for i in range(steps + 1):
        t = i / steps
        z = z0 + (z1 - z0) * t
        a = phase + 2 * math.pi * turns * t
        out, h = FLUTE_OUT * (1.0 - 0.3 * t), FLUTE_H * (1.0 - 0.35 * t)
        c, s = math.cos(a), math.sin(a)
        # the strap follows the cone: each edge sits at the core's radius where it is, its outer face beveled off
        lo, hi = core_radius(z - h / 2), core_radius(z + h / 2)
        rings.append([bm.verts.new((rr * c, rr * s, z + dz)) for rr, dz in
                      ((lo - 0.015, -h / 2), (lo + out * 0.6, -h / 2), (lo + out, -h / 2 + 0.012),
                       (hi + out, h / 2 - 0.012), (hi + out * 0.6, h / 2), (hi - 0.015, h / 2))])
    for i in range(steps):
        a, b = rings[i], rings[i + 1]
        for k in range(6):
            bm.faces.new((a[k], a[(k + 1) % 6], b[(k + 1) % 6], b[k]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sb._object(name, bm, mat, parent, smooth=False)


def lathe(name, profile, mat, parent, segs=64, smooth=True):
    """A solid of revolution about Z from (radius, z) points, closed at radius 0 ends."""
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
    for end in (rings[0], rings[-1]):
        if len(end) > 1:
            bm.faces.new(end)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return sb._object(name, bm, mat, parent, smooth=smooth)


def build(root_name="Drill"):
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    body = sp.metal("gunmetal", GUNMETAL, 0.42, wear=1.0)
    dark = sp.metal("gunmetal_dark", GUNMETAL_DARK, 0.5, wear=0.6)
    steel = sp.metal("steel", STEEL, 0.28, wear=0.5)
    copper = sp.metal("copper", COPPER, 0.4, wear=0.8)
    bolt = sp.metal("bolt", (0.32, 0.33, 0.35), 0.3)
    teal = sp.metal("teal", TEAL, 0.32, metallic=0.6, coat=0.4)
    bore = sp.plain("bore", (0.01, 0.01, 0.012), 0.6)
    glow = sp.plain("glow", GLOW, 0.3, emission=GLOW, strength=6.0)

    zc = (Z_HOUSING_BACK + Z_HOUSING_FRONT) / 2
    length = Z_HOUSING_FRONT - Z_HOUSING_BACK
    # the housing's core, set in from the pillars and plates
    sb.box("HousingCore", (0.66, 0.66, length - 0.01), (0, 0, zc), 0, dark, root, bevel=0.01)
    # corner pillars, split into a back and a front block by a panel line, a bolt on each end face
    edge = HALF - CORNER / 2
    for k in range(4):
        sx, sy = (1, 1, -1, -1)[k], (1, -1, 1, -1)[k]
        x, y = sx * edge, sy * edge
        split = -0.2
        sb.box("PillarBack", (CORNER, CORNER, split - Z_HOUSING_BACK - 0.004), (x, y, (Z_HOUSING_BACK + split) / 2 - 0.002),
               0, body, root, bevel=0.012)
        sb.box("PillarFront", (CORNER, CORNER, Z_HOUSING_FRONT + 0.015 - split - 0.004),
               (x, y, (split + Z_HOUSING_FRONT + 0.015) / 2 + 0.002), 0, body, root, bevel=0.012)
        sb.bolt("PillarBoltF", x + sx * 0.02, y + sy * 0.02, Z_HOUSING_FRONT + 0.015, 1, bolt, root, r=0.022)
        sb.bolt("PillarBoltB", x + sx * 0.02, y + sy * 0.02, Z_HOUSING_BACK, -1, bolt, root, r=0.022)
        # a rivet on each outer face of the front block
        sb.box("PillarRivetX", (0.012, 0.03, 0.03), (x + sx * (CORNER / 2 + 0.004), y, -0.13), 0, bolt, root, bevel=0.004)
        sb.box("PillarRivetY", (0.03, 0.012, 0.03), (x, y + sy * (CORNER / 2 + 0.004), -0.13), 0, bolt, root, bevel=0.004)
    # side plates: a back and a front plate on each side between the pillars, the front one with a raised strip
    for k in range(4):
        a = k * math.pi / 2
        c, s = math.cos(a), math.sin(a)
        span = 2 * edge - CORNER - 0.012
        for z0, z1, name in ((Z_HOUSING_BACK + 0.01, -0.2, "PlateBack"), (-0.19, Z_HOUSING_FRONT - 0.005, "PlateFront")):
            r = HALF - 0.03
            sb.box(name, (0.05, span, z1 - z0), (r * c, r * s, (z0 + z1) / 2), a, body, root, bevel=0.008)
        r = HALF - 0.004
        sb.box("PlateStrip", (0.012, span * 0.62, 0.05), (r * c, r * s, -0.125), a, dark, root, bevel=0.004)
        for o in (-1, 1):
            sb.box("PlateRivet", (0.012, 0.026, 0.026), ((r + 0.002) * c - o * span * 0.38 * s, (r + 0.002) * s + o * span * 0.38 * c,
                                                          -0.27), a, bolt, root, bevel=0.004)
        if k % 2 == 0:
            # a teal inset on the two sides: a round lens in a dark socket, on the back plate
            ex = HALF + 0.002
            ob = sb.disc("TealSocket", 0.06, 0, 0.012, dark, root, segs=32)
            ob.rotation_euler = (0, math.radians(90), a)
            ob.location = (ex * c - 0.006 * c, ex * s - 0.006 * s, -0.262)
            ob = sb.disc("TealInset", 0.042, 0, 0.012, teal, root, segs=32)
            ob.rotation_euler = (0, math.radians(90), a)
            ob.location = (ex * c, ex * s, -0.262)
    # the front face: a ring plate, the base ring with its teal band, and the bit's seat
    sb.ring("FrontPlate", 0.335, 0.27, Z_HOUSING_FRONT - 0.01, Z_HOUSING_FRONT + 0.012, body, root, segs=96, bevel=0.004)
    sb.ring("BaseRing", R_TEAL_OUT + 0.016, R_BIT_BASE - 0.02, Z_HOUSING_FRONT, Z_BIT, dark, root, segs=96, bevel=0.004)
    sb.ring("BaseTeal", R_TEAL_OUT, R_TEAL_IN, Z_BIT - 0.004, Z_BIT + 0.026, teal, root, segs=96, bevel=0.003)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        sb.bolt("BaseBolt", 0.31 * math.cos(a), 0.31 * math.sin(a), Z_HOUSING_FRONT + 0.012, 1, bolt, root, r=0.012)
    # the bit: the tapering core, the copper flute wound round it, and the steel point
    profile = [(R_BIT_BASE - 0.02, Z_BIT - 0.01), (R_BIT_BASE, Z_BIT + 0.01)]
    profile += [(core_radius(z), z) for z in (Z_BIT + (Z_NECK - Z_BIT) * i / 16 for i in range(1, 17))]
    profile += [(R_BIT_NECK + 0.012, Z_NECK + 0.004), (0.0, Z_NECK + 0.006)]
    lathe("BitCore", profile, steel, root, segs=64)
    helix("Flute", Z_BIT + 0.03, Z_NECK - 0.01, FLUTE_TURNS, copper, root)
    lathe("Point", [(R_BIT_NECK + 0.02, Z_NECK - 0.012), (R_BIT_NECK + 0.02, Z_NECK + 0.006), (0.0, Z_TIP)], steel, root,
          segs=12, smooth=False)
    # the mounting face: a back plate, the collar with its teal ring, and the dark bore
    sb.box("BackPlate", (0.6, 0.6, 0.02), (0, 0, Z_HOUSING_BACK - 0.005), 0, dark, root, bevel=0.006)
    sb.ring("Collar", R_COLLAR, R_BORE, Z_BACK + 0.004, Z_HOUSING_BACK, body, root, segs=96, bevel=0.005)
    sb.ring("CollarTeal", R_COLLAR + 0.03, R_COLLAR + 0.002, Z_BACK + 0.02, Z_HOUSING_BACK, teal, root, segs=96, bevel=0.003)
    sb.ring("BoreLip", R_BORE + 0.012, R_BORE - 0.012, Z_BACK, Z_BACK + 0.02, dark, root, segs=64)
    sb.disc("Bore", R_BORE, Z_BACK + 0.03, Z_BACK + 0.032, bore, root, segs=40)
    for k in range(6):
        a = 2 * math.pi * k / 6
        sb.bolt("CollarBolt", 0.15 * math.cos(a), 0.15 * math.sin(a), Z_BACK + 0.004, -1, bolt, root, r=0.012)
    # the glow: a ring just proud of the teal band round the bit's base
    glows = [sb.ring("BitGlow", R_TEAL_OUT - 0.008, R_TEAL_IN + 0.008, Z_BIT + 0.026, Z_BIT + 0.028, glow, root, segs=96)]
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
        pose(True, OFF)
        shots["front"] = (rl.render(rl.camera("front", (-4, 0, HALF), (0, 0, HALF), ortho=1.05), out / "drill_front.png", 900, 900),)
        shots["back"] = (rl.render(rl.camera("back", (4, 0, HALF), (0, 0, HALF), ortho=1.05), out / "drill_back.png", 900, 900),)
        cam = rl.camera("side", (0, -4, HALF), (0, 0, HALF), ortho=1.05)
        shots["side"] = (rl.render(cam, out / "drill_side.png", 900, 900), cam)
        shots["top"] = (rl.render(rl.camera("top", (0, 0, 4), (0, 0, HALF), ortho=1.05), out / "drill_top.png", 900, 900),)
        shots["tq"] = (rl.render(rl.camera("tq", (-1.5, -3.0, 1.6), (0, 0, HALF), ortho=1.35), out / "drill_three_quarter.png", 900, 800),)
        shots["rear_tq"] = (rl.render(rl.camera("rtq", (2.4, -2.4, 1.7), (0, 0, HALF), ortho=1.35), out / "drill_rear_three_quarter.png", 900, 800),)
        pose(True, 1.0)
        shots["glow"] = (rl.render(rl.camera("glow", (-2.6, -1.6, 1.2), (0, 0, HALF), ortho=1.3), out / "drill_at_speed.png", 900, 800),)
    ground.hide_render = True
    sc.render.film_transparent = True
    pose(False, OFF)
    # the tip towards the viewer's lower left, the housing behind it, as on the reference sheet
    root.rotation_mode = "QUATERNION"
    root.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(Vector((-0.88, -0.3, -0.36)).normalized())
    root.location = (0, 0, 0)
    p = rl.render(rl.camera("icon", (0, -4, 0), (0, 0, 0), ortho=1.3), out / "drill_icon_raw.png", 512, 512)
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
    d.text((60, 98), "3 x 3 x 3 blocks (75 cm each way). Gunmetal housing, copper helical flute on a steel bit, teal "
           "rings; the round collar at the back is the mounting face the bearing turns.", fill=INK, font=F(24))
    tiles = [("FRONT (TIP)", "front", (60, 170)), ("SIDE", "side", (720, 170)), ("TOP", "top", (1380, 170)),
             ("BACK (MOUNTING FACE)", "back", (2040, 170))]
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
    for label, key, x in (("THREE-QUARTER", "tq", 60), ("REAR THREE-QUARTER", "rear_tq", 1000),
                          ("AT DRILLING SPEED (base ring glow)", "glow", 1940)):
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
