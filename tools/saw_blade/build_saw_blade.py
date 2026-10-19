"""Build the Saw Blade (design spec of 2026-10-18 and Chase's reference sheet).

A toothed disc 5 blocks across (1.25 m to the tooth tips) and 1 block (0.25 m) thick at the hub, lying flat with
its axis on Blender +Z (glTF +Y, the part's up): a ring of 28 hooked copper teeth riveted to a gunmetal disc, a
reinforced outer ring with a row of rivets, two rings of plates split by panel lines, and a hub with a teal bearing
ring round a dark bore. The front (+Z, the side away from the bearing) has a bolted octagonal boss; the back (-Z, the
mounting face the bearing turns) has a bolted cross brace. Both faces carry the hub's teal ring at +-0.125 m, so the
part's 1-block envelope is the hub.

What it writes: ``mods/vanilla/meshes/parts/saw_blade.glb`` and ``mods/vanilla/textures/items/saw_blade_icon.png``
(the item icon the hotbar, inventory and Fabricator show). The model has two nodes: ``Blade`` (everything rigid) and
``HubGlow`` (an emissive teal ring on each face). Its one clip, ``glow`` (1 s, scrubbed), grows the glow from nothing
at 0 s to full at 1 s; the saw's ``spin`` readout (how close it is to cutting speed) drives it.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/saw_blade/build_saw_blade.py                    # writes the .glb and the icon
    python tools/saw_blade/build_saw_blade.py --render OUT_DIR   # review renders and sheet into OUT_DIR

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
from mathutils import Matrix

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "sport_suspension"))
import build_sport_suspension as sp  # noqa: E402  (shared materials)

SAMPLES = int(os.environ.get("SAW_SAMPLES", "64"))
FPS = 24

R_TIP = 0.625          # tooth tips: 5 blocks across
R_GULLET = 0.552       # the bottom of the gaps between teeth
R_TEETH_IN = 0.50      # the copper ring's inner edge, under the outer ring
R_RIM_OUT = 0.515      # the reinforced outer ring
R_RIM_IN = 0.455
R_DISC = 0.535         # the disc itself, under the plates
HALF = 0.125           # half the thickness at the hub (1 block)
TEETH = 28
T_TEETH = 0.026        # half thickness of the copper teeth
T_DISC = 0.03          # half thickness of the disc
T_RIM = 0.042          # half thickness of the outer ring
T_PLATE = 0.04         # half thickness at the plates' top faces
R_HUB = 0.205          # the octagonal boss and the cross brace's reach
R_TEAL_OUT, R_TEAL_IN = 0.118, 0.078
R_BORE = 0.06
OFF = 1e-4             # a scale that hides a glow

GUNMETAL = (0.085, 0.09, 0.095)
GUNMETAL_DARK = (0.045, 0.047, 0.05)
COPPER = (0.50, 0.21, 0.11)
TEAL = (0.015, 0.20, 0.22)
GLOW = (0.1, 0.95, 1.0)


# =====================================================================================================
# Mesh helpers (all about the Z axis)
# =====================================================================================================
def _object(name, bm, mat, parent, smooth=False):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    return ob


def _bevel(ob, width, segments=2):
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = 1 if sp.GAME else segments
    mod.limit_method = "ANGLE"
    return ob


def band(name, outer, inner, z0, z1, mat, parent, closed=True, bevel=0.0):
    """A solid between two matching point lists in the XY plane (outer and inner outlines), from z0 to z1.

    closed: the outlines go all the way round (a ring); otherwise the ends are walled (a sector)."""
    bm = bmesh.new()
    n = len(outer)
    lo = [[bm.verts.new((x, y, z0)) for x, y in pts] for pts in (outer, inner)]
    hi = [[bm.verts.new((x, y, z1)) for x, y in pts] for pts in (outer, inner)]
    last = n if closed else n - 1
    for i in range(last):
        j = (i + 1) % n
        bm.faces.new((hi[0][i], hi[0][j], hi[1][j], hi[1][i]))   # top
        bm.faces.new((lo[0][j], lo[0][i], lo[1][i], lo[1][j]))   # bottom
        bm.faces.new((lo[0][i], lo[0][j], hi[0][j], hi[0][i]))   # outer wall
        bm.faces.new((lo[1][j], lo[1][i], hi[1][i], hi[1][j]))   # inner wall
    if not closed:
        bm.faces.new((lo[0][0], hi[0][0], hi[1][0], lo[1][0]))
        bm.faces.new((lo[0][-1], lo[1][-1], hi[1][-1], hi[0][-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _object(name, bm, mat, parent)
    return _bevel(ob, bevel) if bevel else ob


def arc(r, a0, a1, segs):
    return [(r * math.cos(a0 + (a1 - a0) * i / segs), r * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs + 1)]


def ring(name, r_out, r_in, z0, z1, mat, parent, segs=96, bevel=0.0):
    a = [2 * math.pi * i / segs for i in range(segs)]
    return band(name, [(r_out * math.cos(t), r_out * math.sin(t)) for t in a],
                [(r_in * math.cos(t), r_in * math.sin(t)) for t in a], z0, z1, mat, parent, bevel=bevel)


def sector(name, r_out, r_in, a0, a1, z0, z1, mat, parent, bevel=0.003):
    segs = max(2, int(abs(a1 - a0) / math.radians(4)))
    return band(name, arc(r_out, a0, a1, segs), arc(r_in, a0, a1, segs), z0, z1, mat, parent, closed=False, bevel=bevel)


def disc(name, r, z0, z1, mat, parent, segs=64, x=0.0, y=0.0, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=abs(z1 - z0))
    bmesh.ops.translate(bm, verts=bm.verts, vec=(x, y, (z0 + z1) / 2))
    return _object(name, bm, mat, parent, smooth=segs <= 16)


def box(name, size, centre, angle, mat, parent, bevel=0.004):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=bm.verts, vec=size)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(angle, 3, "Z"))
    bmesh.ops.translate(bm, verts=bm.verts, vec=centre)
    return _bevel(_object(name, bm, mat, parent), bevel)


def rivet(name, x, y, z, up, mat, parent, r=0.011):
    """A domed rivet head on a face at height z, facing +Z (up=1) or -Z (up=-1)."""
    h = 0.007
    return disc(name, r, z, z + up * h, mat, parent, segs=12, x=x, y=y, r2=r * 0.55) if up > 0 else \
        disc(name, r * 0.55, z - h, z, mat, parent, segs=12, x=x, y=y, r2=r)


def bolt(name, x, y, z, up, mat, parent, r=0.014):
    """A hex bolt head on a face at height z."""
    return disc(name, r, z, z + up * 0.01, mat, parent, segs=6, x=x, y=y)


# =====================================================================================================
# The blade
# =====================================================================================================
def tooth_outline():
    """The toothed edge: per tooth, a long back rising to a hooked tip, then a steep face down into a round gullet.

    Teeth point the way the blade turns (counter-clockwise seen from +Z)."""
    pts = []
    pitch = 2 * math.pi / TEETH
    # (fraction of the pitch, radius): gullet, back, tip, hooked face, gullet bottom
    profile = [(0.00, R_GULLET + 0.006), (0.12, R_GULLET + 0.012), (0.32, R_GULLET + 0.024), (0.50, R_GULLET + 0.042),
               (0.62, R_GULLET + 0.060), (0.70, R_TIP), (0.72, R_TIP - 0.004), (0.70, R_GULLET + 0.040),
               (0.72, R_GULLET + 0.016), (0.79, R_GULLET + 0.002), (0.88, R_GULLET + 0.001)]
    for k in range(TEETH):
        for f, r in profile:
            a = (k + f) * pitch
            pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


def build(root_name="SawBlade"):
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    body = sp.metal("gunmetal", GUNMETAL, 0.42, wear=1.0)
    dark = sp.metal("gunmetal_dark", GUNMETAL_DARK, 0.5, wear=0.6)
    copper = sp.metal("copper", COPPER, 0.45, wear=0.8)
    steel = sp.metal("bolt", (0.32, 0.33, 0.35), 0.3)
    teal = sp.metal("teal", TEAL, 0.32, metallic=0.6, coat=0.4)
    bore = sp.plain("bore", (0.01, 0.01, 0.012), 0.6)
    glow = sp.plain("glow", GLOW, 0.3, emission=GLOW, strength=6.0)

    # copper teeth, riveted under the outer ring
    outer = tooth_outline()
    inner = [(R_TEETH_IN * x / math.hypot(x, y), R_TEETH_IN * y / math.hypot(x, y)) for x, y in outer]
    _bevel(band("Teeth", outer, inner, -T_TEETH, T_TEETH, copper, root), 0.004)
    disc("Disc", R_DISC, -T_DISC, T_DISC, dark, root, segs=128)
    ring("OuterRing", R_RIM_OUT, R_RIM_IN, -T_RIM, T_RIM, body, root, segs=128, bevel=0.005)
    for up in (1, -1):
        side = "F" if up > 0 else "B"
        for k in range(TEETH):
            a = 2 * math.pi * (k + 0.35) / TEETH
            rivet(f"{side}RimRivet", 0.487 * math.cos(a), 0.487 * math.sin(a), up * T_RIM, up, steel, root, r=0.009)
            a = 2 * math.pi * (k + 0.2) / TEETH
            rivet(f"{side}ToothRivet", 0.532 * math.cos(a), 0.532 * math.sin(a), up * T_TEETH, up, dark, root, r=0.008)
        # plates: an inner ring of 8 and an outer ring of 6, split by panel lines, the rings turned against each other
        gap = math.radians(1.6)
        for k in range(8):
            a0 = 2 * math.pi * k / 8 + (0.12 if up < 0 else 0.0)
            sector(f"{side}PlateIn", 0.338, R_HUB + 0.02, a0 + gap / 2, a0 + 2 * math.pi / 8 - gap / 2,
                   up * T_DISC * 0.5, up * T_PLATE, body, root)
        for k in range(6):
            a0 = 2 * math.pi * k / 6 + math.radians(17) + (0.3 if up < 0 else 0.0)
            sector(f"{side}PlateOut", R_RIM_IN - 0.012, 0.35, a0 + gap / 2, a0 + 2 * math.pi / 6 - gap / 2,
                   up * T_DISC * 0.5, up * T_PLATE, body, root)
            # a raised strap across every other outer plate, and a rivet at each of its ends
            if k % 2 == 0:
                am = a0 + math.pi / 6
                sector(f"{side}Strap", 0.452, 0.362, am - 0.07, am + 0.07, up * T_PLATE * 0.9, up * (T_PLATE + 0.008),
                       dark, root, bevel=0.002)
                for rr in (0.372, 0.442):
                    rivet(f"{side}StrapRivet", rr * math.cos(am), rr * math.sin(am), up * (T_PLATE + 0.008), up, steel, root,
                          r=0.008)
        # a step ring between the plate rings, and rivets along it
        ring(f"{side}MidRing", 0.352, 0.336, up * T_DISC * 0.5, up * (T_PLATE + 0.004), dark, root, segs=96)
        for k in range(16):
            a = 2 * math.pi * (k + 0.5) / 16
            rivet(f"{side}MidRivet", 0.344 * math.cos(a), 0.344 * math.sin(a), up * (T_PLATE + 0.004), up, steel, root,
                  r=0.007)
        # hub: a raised collar round the bore, the teal bearing ring flush with the 1-block envelope, and the bore
        ring(f"{side}Collar", 0.14, R_BORE, 0, up * 0.1, body, root, segs=64, bevel=0.004)
        ring(f"{side}Teal", R_TEAL_OUT, R_TEAL_IN, up * 0.098, up * HALF, teal, root, segs=64, bevel=0.003)
        ring(f"{side}TealSeat", R_TEAL_OUT + 0.012, R_TEAL_OUT, up * 0.09, up * 0.106, dark, root, segs=64)
        disc(f"{side}Bore", R_BORE, up * 0.11, up * 0.112, bore, root, segs=40)
    # front: an octagonal boss with 8 bolts
    octagon = [(R_HUB * math.cos(a), R_HUB * math.sin(a)) for a in (2 * math.pi * (k + 0.5) / 8 for k in range(8))]
    hole = [(0.13 * x / R_HUB, 0.13 * y / R_HUB) for x, y in octagon]
    _bevel(band("FBoss", octagon, hole, T_PLATE * 0.5, 0.075, body, root), 0.006)
    _bevel(band("FBossLip", [(x * 1.06, y * 1.06) for x, y in octagon], octagon, T_PLATE * 0.5, T_PLATE + 0.01, dark, root),
           0.003)
    for k in range(8):
        a = 2 * math.pi * k / 8
        bolt("FBossBolt", 0.168 * math.cos(a), 0.168 * math.sin(a), 0.075, 1, steel, root)
    # back (the mounting face): a cross brace of four arms with a bolted pad at each end
    _bevel(band("BHubPlate", arc(0.15, 0, 2 * math.pi, 64)[:-1], arc(0.13, 0, 2 * math.pi, 64)[:-1], -0.075,
                -T_PLATE * 0.5, dark, root), 0.004)
    for k in range(4):
        a = 2 * math.pi * k / 4
        c, s = math.cos(a), math.sin(a)
        box("BBraceArm", (0.2, 0.085, 0.034), (0.21 * c, 0.21 * s, -(T_PLATE + 0.017)), a, body, root)
        box("BBracePad", (0.07, 0.13, 0.04), (0.315 * c, 0.315 * s, -(T_PLATE + 0.02)), a, dark, root)
        for o in (-0.04, 0.04):
            bolt("BBraceBolt", 0.315 * c - o * s, 0.315 * s + o * c, -(T_PLATE + 0.04), -1, steel, root, r=0.011)
        bolt("BBraceBoltIn", 0.17 * c, 0.17 * s, -(T_PLATE + 0.034), -1, steel, root, r=0.012)
    # the glow: a teal ring on each face, just proud of the bearing ring
    glows = [ring(f"HubGlow{side}", R_TEAL_OUT - 0.008, R_TEAL_IN + 0.008, up * HALF, up * (HALF + 0.0015), glow, root,
                  segs=64) for up, side in ((1, "F"), (-1, "B"))]
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

    def pose(standing, glow):
        """standing: the disc upright (axis along Blender Y), as on a bearing on a vehicle's side."""
        root.rotation_euler = (math.radians(90), 0, 0) if standing else (0, 0, 0)
        root.location = (0, 0, R_TIP + 0.002) if standing else (0, 0, HALF + 0.005)
        for g in glows:
            g.scale = (glow, glow, 1) if glow > OFF else (OFF, OFF, OFF)
        bpy.context.view_layer.update()

    shots = {}
    if not icons_only:
        pose(True, OFF)
        shots["front"] = (rl.render(rl.camera("front", (0, -4, R_TIP), (0, 0, R_TIP), ortho=1.6), out / "saw_blade_front.png", 900, 900),)
        pose(True, OFF)
        shots["back"] = (rl.render(rl.camera("back", (0, 4, R_TIP), (0, 0, R_TIP), ortho=1.6), out / "saw_blade_back.png", 900, 900),)
        cam = rl.camera("edge", (4, 0, R_TIP), (0, 0, R_TIP), ortho=1.6)
        shots["edge"] = (rl.render(cam, out / "saw_blade_edge.png", 500, 900), cam)
        pose(False, OFF)
        shots["tq"] = (rl.render(rl.camera("tq", (1.7, -2.2, 1.5), (0, 0, 0.05), ortho=1.75), out / "saw_blade_three_quarter.png", 900, 700),)
        pose(True, 1.0)
        shots["glow"] = (rl.render(rl.camera("glow", (1.3, -2.6, 1.0), (0, 0, R_TIP), ortho=1.7), out / "saw_blade_cutting_speed.png", 900, 900),)
    ground.hide_render = True
    sc.render.film_transparent = True
    pose(False, OFF)
    root.rotation_euler = (math.radians(62), 0, math.radians(-28))
    root.location = (0, 0, 0)
    p = rl.render(rl.camera("icon", (0, -4, 0), (0, 0, 0), ortho=1.45), out / "saw_blade_icon_raw.png", 512, 512)
    im = Image.open(p).convert("RGBA")
    im = im.crop(im.getbbox())
    side = max(im.size)
    pad = int(side * 0.05)
    sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
    sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2))
    icon = sq.resize((256, 256), Image.LANCZOS)
    icon.save(out / "saw_blade_icon.png")
    os.remove(p)
    if icons_only:
        return

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2560, 2160), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "SAW BLADE (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "5 blocks across (1.25 m to the tooth tips), 1 block (0.25 m) thick at the hub. Gunmetal disc, "
           "28 copper teeth, teal bearing ring; the back is the mounting face the bearing turns.", fill=INK, font=F(24))
    tiles = [("FRONT", shots["front"][0], (60, 170), 900), ("EDGE", shots["edge"][0], (990, 170), None),
             ("BACK (MOUNTING FACE)", shots["back"][0], (1520, 170), 900)]
    for label, path, (x, y), size in tiles:
        im = Image.open(path).convert("RGB")
        if size:
            im = im.resize((size, size), Image.LANCZOS)
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(26, True))
    # dimensions: across the front, and the edge's thickness
    px = 900 / 1.6
    rl.dim_line(d, (60 + 450 - R_TIP * px, 170 + 450 + R_TIP * px + 18), (60 + 450 + R_TIP * px, 170 + 450 + R_TIP * px + 18),
                "125 cm (5 blocks)", F(22))
    cam = shots["edge"][1]
    a = rl.project(cam, (0, -HALF, 2 * R_TIP + 0.03), 500, 900)
    b = rl.project(cam, (0, HALF, 2 * R_TIP + 0.03), 500, 900)
    rl.dim_line(d, (990 + a[0], 170 + a[1] - 30), (990 + b[0], 170 + b[1] - 30), "25 cm (1 block)", F(22))
    y = 1110
    for label, path, x, w in (("THREE-QUARTER", shots["tq"][0], 60, 900), ("AT CUTTING SPEED (hub glow)", shots["glow"][0], 990, 700)):
        im = Image.open(path).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(26, True))
    tile = Image.new("RGB", (276, 276), (40, 42, 46))
    tile.paste(icon, (10, 10), icon)
    sheet.paste(tile, (1760, 1180))
    d.text((1730, 1470), "hotbar / inventory / Fabricator icon (256 px)", fill=INK, font=F(20))
    sheet.save(out / "review_sheet.png")
    print(f"rendered the review sheet into {out}")


# =====================================================================================================
# The game model
# =====================================================================================================
def linear_keys(action):
    """Linear interpolation on every key of the action (Blender 5 keeps fcurves in layered channelbags)."""
    curves = []
    if hasattr(action, "layers") and action.layers:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    curves.extend(bag.fcurves)
    else:
        curves = list(action.fcurves)
    for fc in curves:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"


def export_model():
    sp.GAME = True
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, glows = build()
    rigid = [o for o in root.children if o not in glows]
    sp.join(rigid, "Blade").parent = root
    glow = sp.join(glows, "HubGlow")
    glow.parent = root
    # the glow clip: nothing at 0 s, full at 1 s
    glow.animation_data_create()
    act = bpy.data.actions.new("glow_HubGlow")
    glow.animation_data.action = act
    for f in range(0, FPS + 1, 4):
        v = max(OFF, f / FPS)
        glow.scale = (v, v, v)
        glow.keyframe_insert("scale", frame=f)
    linear_keys(act)
    track = glow.animation_data.nla_tracks.new()
    track.name = "glow"
    strip = track.strips.new("glow", 0, act)
    strip.action_frame_start, strip.action_frame_end = act.frame_range
    glow.animation_data.action = None
    glow.animation_data.use_nla = False
    glow.scale = (OFF, OFF, OFF)
    path = sp.VANILLA / "meshes" / "parts" / "saw_blade.glb"
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
        dest = sp.VANILLA / "textures" / "items" / "saw_blade_icon.png"
        shutil.copyfile(Path(tmp) / "saw_blade_icon.png", dest)
        print(f"wrote {dest}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--render" in args:
        render_review(Path(args[args.index("--render") + 1]))
    else:
        export_model()
        write_icon()
