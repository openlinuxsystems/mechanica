"""Build the Fabricator, levels 1 to 5 (design spec of 2026-10-18, "Dual Bay with Ports", corrected).

One body shared by every level, exactly 10 x 8 x 9 blocks (2.50 m wide, 2.00 m deep, 2.25 m tall), front along -Y:

* a one-block plinth under the whole footprint;
* two open work bays (left and right), each with a work table, hazard-striped lip and an articulated arm that welds
  and drills the item on the table;
* a central console with the status screen, buttons and level pips, rising to 8 blocks, with the exhaust housing
  on top filling the 9th block row;
* the standard 2 x 2 IO ports (docs/io-ports.md): an input on the left side and an output on the right side, at
  floor level (centre 0.375 m up, level with the containers' ports), half a block toward the front of centre, their
  collars on the footprint boundary over the plinth;
* a pegboard with tools across the back.

The levels change finish and add trim, never the envelope or the ports (see ``LEVELS``).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/fabricator/build_fabricator.py --render OUT_DIR   # review renders and sheet into OUT_DIR
    python tools/fabricator/build_fabricator.py --game             # mods/vanilla meshes/parts/fabricator_1..5.glb,
                                                                   # their icons, and prints the spark offsets

Units are metres; in Blender Z is up. The build is deterministic.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "io_port"))
import build_io_port as io  # noqa: E402

B = 0.25                       # one block
W, D, H = 10 * B, 8 * B, 9 * B
HW, HD = W / 2, D / 2
PLINTH = B
SIDE_IN = io.PORT_OUT - io.PORT_DEPTH + 0.01   # side walls set in so the port plate stands just proud of them
BAY_X = (HW - SIDE_IN, 0.375)            # bay outer wall to console wall (|x|)
BAY_TOP = 7 * B
CONSOLE_TOP = 8 * B
BACK_Y = 0.62                            # bay back wall (the machinery behind it holds the pegboard)
REAR = HD - 0.08                         # the pegboard's face, recessed so the hung tools stay in the footprint
TABLE_Z = 0.56
PORT_Y = -0.125                          # port centre along the face: where the 3 x 3 port was centred
PORT_Z = 1.5 * B                         # port centre 0.375 m up: level with the 3-block-tall containers on the floor
SAMPLES = int(os.environ.get("FAB_SAMPLES", "48"))

LEVELS = {
    1: dict(body=(0.07, 0.075, 0.082), rough=0.55, wear=1.0, trim=(0.12, 0.125, 0.135), glow=1.5, strips=0,
            pipes=False, second_arm=False, core=False, vents=1),
    2: dict(body=(0.075, 0.08, 0.088), rough=0.42, wear=0.6, trim=(0.15, 0.155, 0.165), glow=2.5, strips=1,
            pipes=False, second_arm=False, core=False, vents=1),
    3: dict(body=(0.05, 0.055, 0.062), rough=0.36, wear=0.35, trim=(0.17, 0.175, 0.185), glow=3.5, strips=1,
            pipes=True, second_arm=False, core=False, vents=2),
    4: dict(body=(0.035, 0.037, 0.043), rough=0.3, wear=0.15, trim=(0.2, 0.2, 0.21), glow=5.0, strips=2,
            pipes=True, second_arm=True, core=False, vents=2),
    5: dict(body=(0.018, 0.019, 0.023), rough=0.16, wear=0.0, trim=(0.55, 0.56, 0.58), glow=8.0, strips=2,
            pipes=True, second_arm=True, core=True, vents=3),
}
TEAL_GLOW = (0.1, 0.95, 0.85)
GAME_GLOW = 4.0

REPO = Path(__file__).resolve().parents[2]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = 30
# the model's clips (seconds); level 5 crafts twice as fast, so its work loop runs twice as fast
CLIPS = dict(idle_loop=4.0, start=0.8, work_loop=2.0, complete=0.8)


# =====================================================================================================
# Materials
# =====================================================================================================
def emissive(name, color, strength, base=(0.02, 0.03, 0.03)):
    m, b, _ = io._principled(name)
    b.inputs["Base Color"].default_value = (*base, 1)
    b.inputs["Roughness"].default_value = 0.3
    b.inputs["Emission Color"].default_value = (*color, 1)
    # the game has bloom and no path tracing: the review's brightest glows would wash out
    b.inputs["Emission Strength"].default_value = min(strength, GAME_GLOW) if io.GAME else strength
    return m


def _pattern(name, a, b, rough, pattern):
    """A two-colour tiling texture: 'stripes' (diagonal hazard stripes, 0.2 m a tile) or 'pegboard' (one hole
    every 0.04 m). Objects using it get planar UVs in metres / tile (see ``box``), so it survives the glTF export."""
    import numpy as np  # noqa: PLC0415
    size = 64
    u, v = np.meshgrid((np.arange(size) + 0.5) / size, (np.arange(size) + 0.5) / size)
    if pattern == "stripes":
        fac = ((u + v) * 2.0) % 1.0 < 0.5
        tile = 0.2
    else:
        fac = (u - 0.5) ** 2 + (v - 0.5) ** 2 < 0.05
        tile = 0.04
    rgba = np.where(fac[..., None], np.array([*b, 1.0]), np.array([*a, 1.0])).astype(np.float32)
    img = bpy.data.images.new(name, size, size, alpha=False)
    img.colorspace_settings.name = "sRGB"
    srgb = np.where(rgba[..., :3] <= 0.0031308, rgba[..., :3] * 12.92, 1.055 * np.power(rgba[..., :3], 1 / 2.4) - 0.055)
    rgba[..., :3] = srgb
    img.pixels.foreach_set(rgba.ravel())
    img.pack()
    img.filepath_raw = f"//{name}.png"
    img.file_format = "PNG"
    m, bsdf, t = io._principled(name)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.6
    tex = t.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest" if pattern == "pegboard" else "Linear"
    t.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    m["tile"] = tile
    return m


def planar_uv(ob, tile):
    """UVs from the object's coordinates in metres / tile, projected along each face's main axis."""
    me = ob.data
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        n = poly.normal
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = [k for k in range(3) if k != axis]
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co[a] / tile, co[b] / tile)


def materials(level):
    lv = LEVELS[level]
    m = io.materials()
    m.update(
        frame=io.metal(f"fab_frame{level}", lv["body"], lv["rough"], 0.85, wear=lv["wear"]),
        panel=io.metal(f"fab_panel{level}", tuple(c * 1.6 for c in lv["body"]), lv["rough"] + 0.05, 0.8,
                       wear=lv["wear"]),
        trimm=io.metal(f"fab_trim{level}", lv["trim"], max(0.1, lv["rough"] - 0.1), 1.0, wear=lv["wear"] * 0.5),
        dark=io.metal(f"fab_dark{level}", (0.02, 0.021, 0.024), 0.7, 0.5),
        rubber=io.metal("fab_rubber", (0.015, 0.015, 0.016), 0.9, 0.0),
        stripes=_pattern(f"fab_stripes{level}", (0.02, 0.022, 0.025), io.TEAL, 0.45, "stripes"),
        pegboard=_pattern(f"fab_peg{level}", tuple(c * 2.2 for c in lv["body"]), (0.004, 0.004, 0.005), 0.5,
                          "pegboard"),
        glow=emissive(f"fab_glow{level}", TEAL_GLOW, lv["glow"]),
        screen=emissive(f"fab_screen{level}", (0.03, 0.22, 0.24), 1.2 + lv["glow"] * 0.15, base=(0.01, 0.03, 0.04)),
        screen_hi=emissive(f"fab_screenhi{level}", TEAL_GLOW, 3.0 + lv["glow"] * 0.4),
        bay_light=emissive(f"fab_baylight{level}", (0.85, 0.95, 1.0), 25.0),
        pip_off=io.metal("fab_pipoff", (0.03, 0.05, 0.05), 0.4, 0.2),
        steel=io.metal("fab_steel", (0.55, 0.56, 0.58), 0.25, 1.0),
        hot=emissive("fab_hot", (1.0, 0.75, 0.35), 40.0, base=(1, 0.8, 0.5)),
        spark=emissive("fab_spark", (1.0, 0.62, 0.22), 60.0, base=(1, 0.7, 0.4)),
        holo=emissive("fab_holo", TEAL_GLOW, 4.0),
        scan=emissive("fab_scan", TEAL_GLOW, 1.5),
        red=emissive("fab_estop", (0.9, 0.08, 0.05), 0.6, base=(0.5, 0.03, 0.02)),
    )
    return m


# =====================================================================================================
# Helpers
# =====================================================================================================
def box(name, x, y, z, mat, parent, bevel=0.004):
    """An axis-aligned box spanning x = (x0, x1), y = (y0, y1), z = (z0, z1)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x[0] if v.co.x < 0 else x[1]
        v.co.y = y[0] if v.co.y < 0 else y[1]
        v.co.z = z[0] if v.co.z < 0 else z[1]
    ob = io._object(name, bm, mat, parent)
    if mat.get("tile"):
        planar_uv(ob, mat["tile"])
    return io._bevel(ob, bevel) if bevel else ob


def cyl_axis(name, r, a, b, mat, parent, segs=32, bevel=0.0):
    """A cylinder of radius r from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    ob = io.cylinder(name, r, 0.0, d.length, mat, parent, segs=segs, bevel=bevel)
    ob.location = a
    ob.rotation_euler = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_euler()
    return ob


def empty(name, parent, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    e.location = loc
    return e


def louvres(name, x, y, z, mat, parent, n, face):
    """A vent panel: a dark recess with n slats across it. face is the direction it looks: "+x", "-x", "-y", "+y"
    (slats stacked up the panel, standing proud of it) or "+z" (slats stacked front to back on top)."""
    box(f"{name}_back", x, y, z, mat["dark"], parent, bevel=0.002)
    for i in range(n):
        if face == "+z":
            h = (y[1] - y[0]) / n
            box(f"{name}_slat{i}", x, (y[0] + i * h + h * 0.2, y[0] + i * h + h * 0.75), (z[1], z[1] + 0.02),
                mat["panel"], parent, bevel=0.002)
            continue
        h = (z[1] - z[0]) / n
        zz = (z[0] + i * h + h * 0.18, z[0] + i * h + h * 0.73)
        axis, sgn = face[1], (1 if face[0] == "+" else -1)
        if axis == "x":
            xo = x[1] if sgn > 0 else x[0]
            box(f"{name}_slat{i}", (min(xo, xo + sgn * 0.018), max(xo, xo + sgn * 0.018)), y, zz, mat["panel"], parent,
                bevel=0.002)
        else:
            yo = y[1] if sgn > 0 else y[0]
            box(f"{name}_slat{i}", x, (min(yo, yo + sgn * 0.018), max(yo, yo + sgn * 0.018)), zz, mat["panel"], parent,
                bevel=0.002)


# =====================================================================================================
# The arm
# =====================================================================================================
def arm(name, parent, base, m, tool="welder", scale=1.0):
    """An articulated arm standing on the bay table. Returns its joints (empties) for posing:
    yaw (about Z), shoulder, elbow, wrist (about X; positive tips toward the front)."""
    root = empty(f"{name}", parent, base)
    io.cylinder(f"{name}_foot", 0.085 * scale, 0.0, 0.03, m["trimm"], root, bevel=0.004)
    yaw = empty(f"{name}_yaw", root, (0, 0, 0.03))
    io.cylinder(f"{name}_turret", 0.065 * scale, 0.0, 0.09 * scale, m["frame"], yaw, bevel=0.006)
    io.annulus(f"{name}_ring", 0.066 * scale, 0.072 * scale, 0.03, 0.045, m["glow"], yaw)
    sh = empty(f"{name}_shoulder", yaw, (0, 0, 0.12 * scale))
    cyl_axis(f"{name}_shaxle", 0.045 * scale, (-0.06 * scale, 0, 0), (0.06 * scale, 0, 0), m["trimm"], sh)
    up = 0.42 * scale
    box(f"{name}_upper", (-0.035 * scale, 0.035 * scale), (-0.03 * scale, 0.03 * scale), (0.0, up), m["panel"], sh,
        bevel=0.01)
    box(f"{name}_upperStripe", (-0.036 * scale, 0.036 * scale), (-0.031 * scale, -0.02 * scale), (0.08, up - 0.08),
        m["teal"], sh, bevel=0.0)
    cyl_axis(f"{name}_piston", 0.012 * scale, (0.0, 0.045 * scale, 0.05), (0.0, 0.045 * scale, up - 0.06), m["steel"],
             sh, segs=16)
    el = empty(f"{name}_elbow", sh, (0, 0, up))
    cyl_axis(f"{name}_elaxle", 0.04 * scale, (-0.05 * scale, 0, 0), (0.05 * scale, 0, 0), m["trimm"], el)
    fo = 0.38 * scale
    box(f"{name}_fore", (-0.028 * scale, 0.028 * scale), (-0.025 * scale, 0.025 * scale), (0.0, fo), m["panel"], el,
        bevel=0.008)
    cyl_axis(f"{name}_cable", 0.008 * scale, (0.034 * scale, 0.0, 0.02), (0.034 * scale, 0.0, fo - 0.02),
             m["copper"], el, segs=12)
    wr = empty(f"{name}_wrist", el, (0, 0, fo))
    cyl_axis(f"{name}_wraxle", 0.032 * scale, (-0.04 * scale, 0, 0), (0.04 * scale, 0, 0), m["trimm"], wr)
    io.cylinder(f"{name}_toolBody", 0.03 * scale, 0.0, 0.08 * scale, m["frame"], wr, bevel=0.004)
    io.annulus(f"{name}_toolRing", 0.03 * scale, 0.034 * scale, 0.05 * scale, 0.06 * scale, m["glow"], wr)
    tip = empty(f"{name}_tip", wr, (0, 0, 0.16 * scale))
    if tool == "welder":
        io.cylinder(f"{name}_nozzle", 0.016 * scale, 0.08 * scale, 0.14 * scale, m["copper"], wr, bevel=0.002)
        io.cylinder(f"{name}_electrode", 0.004, 0.14 * scale, 0.16 * scale, m["steel"], wr, segs=8)
    else:                                     # drill
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.018 * scale, radius2=0.002, depth=0.08 * scale)
        bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 0.12 * scale))
        io._object(f"{name}_bit", bm, m["steel"], wr, smooth=True)
    return dict(yaw=yaw, shoulder=sh, elbow=el, wrist=wr, tip=tip, tool=tool)


def pose(j, yaw=0.0, shoulder=0.0, elbow=0.0, wrist=0.0):
    j["yaw"].rotation_euler.z = math.radians(yaw)
    j["shoulder"].rotation_euler.x = math.radians(shoulder)
    j["elbow"].rotation_euler.x = math.radians(elbow)
    j["wrist"].rotation_euler.x = math.radians(wrist)


ARM = 1.35
REST = dict(yaw=0, shoulder=-8, elbow=118, wrist=55)
WORK = dict(yaw=0, shoulder=24, elbow=106, wrist=45)    # the tool tip just over the item on the table


# =====================================================================================================
# The Fabricator
# =====================================================================================================
def build(level=1, name=None):
    lv = LEVELS[level]
    m = materials(level)
    root = empty(name or f"Fabricator{level}", None)

    # plinth (block row 0): dark toe, gunmetal body, teal band
    box("plinthToe", (-HW + 0.04, HW - 0.04), (-HD + 0.04, HD - 0.04), (0.0, 0.05), m["dark"], root)
    # the sides are set in like the walls above, so the floor-level ports sit over the plinth
    box("plinth", (-HW + SIDE_IN, HW - SIDE_IN), (-HD + 0.004, HD - 0.004), (0.05, PLINTH), m["frame"], root, bevel=0.012)
    box("plinthBand", (-HW + SIDE_IN - 0.004, HW - SIDE_IN + 0.004), (-HD, HD), (0.135, 0.15), m["teal"], root, bevel=0)

    # console (centre, 3 blocks wide) up to 8 blocks; its front is set back FY so the controls stay in the footprint
    cx = BAY_X[1]
    fy = -HD + 0.035
    box("console", (-cx, cx), (fy, REAR), (PLINTH, CONSOLE_TOP - 0.03), m["frame"], root, bevel=0.02)
    box("consoleCap", (-cx - 0.01, cx + 0.01), (fy - 0.01, REAR + 0.01), (CONSOLE_TOP - 0.06, CONSOLE_TOP), m["trimm"],
        root, bevel=0.012)
    # screen
    box("screenBezel", (-0.27, 0.27), (fy - 0.02, fy + 0.02), (1.22, 1.66), m["trimm"], root, bevel=0.01)
    sy = fy - 0.024
    box("screen", (-0.24, 0.24), (sy, fy), (1.25, 1.63), m["screen"], root, bevel=0.0)
    # screen content: a gear icon, a progress bar and two lines of status
    gear = io.annulus("screenGear", 0.035, 0.06, -0.0005, 0.0005, m["screen_hi"], root, segs=48)
    gear.location = (-0.15, sy - 0.001, 1.5)
    gear.rotation_euler = (math.radians(90), 0, 0)
    for k in range(8):
        t = box(f"screenTooth{k}", (-0.012, 0.012), (-0.0005, 0.0005), (0.055, 0.075), m["screen_hi"], root, bevel=0)
        t.location = (-0.15, sy - 0.001, 1.5)
        t.rotation_euler = (0, k * math.pi / 4, 0)
    box("screenBarBack", (-0.2, 0.2), (sy - 0.0015, sy - 0.0005), (1.3, 1.34), m["dark"], root, bevel=0)
    box("screenBar", (-0.195, 0.07), (sy - 0.0025, sy - 0.0015), (1.305, 1.335), m["screen_hi"], root, bevel=0)
    for k, (w, z) in enumerate(((0.12, 1.53), (0.08, 1.47))):
        box(f"screenText{k}", (-0.06, -0.06 + w), (sy - 0.0015, sy - 0.0005), (z, z + 0.018), m["screen_hi"], root,
            bevel=0)
    # level pips above the screen
    for k in range(5):
        pip = io.cylinder(f"pip{k}", 0.012, 0.0, 0.012, m["glow"] if k < level else m["pip_off"], root, segs=24)
        pip.location = (-0.1 + k * 0.05, fy, 1.72)
        pip.rotation_euler = (math.radians(90), 0, 0)
    # buttons: start (teal), stop (red), copper dial
    for nme, x, mat in (("btnStart", -0.15, m["glow"]), ("btnStop", -0.05, m["red"])):
        for ob in (io.cylinder(nme, 0.028, 0.0, 0.02, mat, root, bevel=0.003),
                   io.annulus(nme + "Ring", 0.028, 0.04, 0.0, 0.012, m["trimm"], root, bevel=0.002)):
            ob.location = (x, fy, 1.1)
            ob.rotation_euler = (math.radians(90), 0, 0)
    dial = io.cylinder("dial", 0.038, 0.0, 0.025, m["copper"], root, bevel=0.004)
    dial.location = (0.1, fy, 1.1)
    dial.rotation_euler = (math.radians(90), 0, 0)
    box("dialMark", (0.097, 0.103), (fy - 0.026, fy - 0.02), (1.105, 1.135), m["dark"], root, bevel=0)
    # lower door with handle and vent
    box("door", (-0.3, 0.3), (fy - 0.01, fy + 0.01), (0.32, 0.98), m["panel"], root, bevel=0.008)
    box("doorHandle", (0.2, 0.23), (fy - 0.032, fy - 0.01), (0.55, 0.75), m["trimm"], root, bevel=0.006)
    louvres("doorVent", (-0.22, 0.12), (fy - 0.014, fy - 0.008), (0.4, 0.58), m, root, 4, "-y")
    # exhaust housing on top (block row 8)
    box("exhaust", (-0.3, 0.3), (-0.45, 0.55), (CONSOLE_TOP, H - 0.02), m["panel"], root, bevel=0.02)
    louvres("exhaustGrille", (-0.25, 0.25), (-0.38, 0.48), (H - 0.045, H - 0.022), m, root, 5 + lv["vents"], "+z")
    for k in range(lv["vents"]):
        box(f"exhaustFin{k}", (-0.3 - 0.015, 0.3 + 0.015), (-0.4 + k * 0.08, -0.37 + k * 0.08),
            (CONSOLE_TOP + 0.04, H - 0.06), m["trimm"], root, bevel=0.004)
    if lv["core"]:
        box("exhaustGlow", (-0.24, 0.24), (-0.37, 0.47), (H - 0.03, H - 0.028), m["glow"], root, bevel=0)

    # the two bays
    joints = {}
    for side, sgn in (("L", -1), ("R", 1)):
        xo, xi = BAY_X[0] * sgn, BAY_X[1] * sgn
        X = (min(xo, xi), max(xo, xi))
        wi = xo - sgn * 0.07                                  # inner face of the outer wall
        ix0, ix1 = min(wi, xi), max(wi, xi)                   # the bay's inside, wall to console
        # outer side wall, roof, back block (behind the pegboard recess)
        box(f"{side}_wall", (min(xo, wi), max(xo, wi)), (-HD + 0.01, HD - 0.01), (PLINTH, BAY_TOP - 0.15), m["frame"], root,
            bevel=0.015)
        box(f"{side}_roof", X, (-HD + 0.006, HD - 0.006), (BAY_TOP - 0.15, BAY_TOP), m["frame"], root, bevel=0.03)
        box(f"{side}_back", (ix0, ix1), (BACK_Y, REAR), (PLINTH, BAY_TOP - 0.15), m["frame"], root, bevel=0.01)
        box(f"{side}_backLining", (ix0, ix1), (BACK_Y - 0.01, BACK_Y), (TABLE_Z, BAY_TOP - 0.15), m["panel"], root,
            bevel=0)
        # front frame round the bay opening
        po = (min(xo + sgn * 0.01, xo - sgn * 0.11), max(xo + sgn * 0.01, xo - sgn * 0.11))
        box(f"{side}_postOut", po, (-HD, -HD + 0.08), (PLINTH, BAY_TOP - 0.02), m["trimm"], root, bevel=0.01)
        box(f"{side}_postIn", (min(xi, xi + sgn * 0.05), max(xi, xi + sgn * 0.05)), (-HD, -HD + 0.08),
            (PLINTH, BAY_TOP - 0.15), m["trimm"], root, bevel=0.01)
        box(f"{side}_lintel", (ix0, ix1), (-HD + 0.006, -HD + 0.1), (BAY_TOP - 0.3, BAY_TOP - 0.12), m["trimm"], root,
            bevel=0.012)
        box(f"{side}_lintelStripe", (ix0 + 0.06, ix1 - 0.07), (-HD + 0.002, -HD + 0.012), (BAY_TOP - 0.26, BAY_TOP - 0.2),
            m["stripes"], root, bevel=0)
        box(f"{side}_lintelInset", (ix0 + 0.055, ix1 - 0.065), (-HD + 0.004, -HD + 0.02), (BAY_TOP - 0.265,
            BAY_TOP - 0.195), m["dark"], root, bevel=0)
        if not io.GAME:                                   # the review renders light the bay; the game has no lights
            lamp = bpy.data.lights.new(f"{side}_bayLamp", "AREA")
            lamp.energy = 35
            lamp.size = 0.5
            lamp.color = (0.85, 0.95, 1.0)
            lob = bpy.data.objects.new(f"{side}_bayLamp", lamp)
            bpy.context.scene.collection.objects.link(lob)
            lob.parent = root
            lob.location = ((ix0 + ix1) / 2, -0.2, BAY_TOP - 0.2)
        box(f"{side}_ceilLight", (ix0 + 0.1, ix1 - 0.1), (-0.6, 0.3), (BAY_TOP - 0.16, BAY_TOP - 0.15),
            m["bay_light"], root, bevel=0)
        # work table: body, hazard-striped lip, grated top
        box(f"{side}_table", (ix0, ix1), (-HD + 0.02, BACK_Y), (PLINTH, TABLE_Z), m["panel"], root, bevel=0.01)
        box(f"{side}_tableLip", (ix0 + 0.05, ix1 - 0.05), (-HD + 0.004, -HD + 0.03), (PLINTH + 0.08, TABLE_Z - 0.06),
            m["stripes"], root, bevel=0.0)
        box(f"{side}_tableTop", (ix0 + 0.02, ix1 - 0.02), (-HD + 0.04, BACK_Y - 0.02), (TABLE_Z, TABLE_Z + 0.015),
            m["trimm"], root, bevel=0.004)
        for k in range(7):
            y0 = -HD + 0.09 + k * 0.2
            box(f"{side}_grate{k}", (ix0 + 0.06, ix1 - 0.06), (y0, y0 + 0.012), (TABLE_Z + 0.015, TABLE_Z + 0.018),
                m["dark"], root, bevel=0)
        # teal glow strips up the outer post (level 2+)
        for k in range(lv["strips"]):
            xs = xo - sgn * (0.035 + k * 0.035)
            box(f"{side}_strip{k}", (xs - 0.006, xs + 0.006), (-HD - 0.0, -HD + 0.004), (PLINTH + 0.1, BAY_TOP - 0.35),
                m["glow"], root, bevel=0)
        # side louvres behind the port, a panel seam
        louvres(f"{side}_vent", (min(xo, xo + sgn * 0.004), max(xo, xo + sgn * 0.004)), (0.42, 0.85),
                (0.95, 1.36), m, root, 5 + lv["vents"], "+x" if sgn > 0 else "-x")
        box(f"{side}_seam", (min(xo, xo + sgn * 0.003), max(xo, xo + sgn * 0.003)), (0.33, 0.34),
            (PLINTH, BAY_TOP - 0.15), m["dark"], root, bevel=0)
        # copper pipes along the side (level 3+)
        if lv["pipes"]:
            for k, pz in enumerate((1.46, 1.52)):
                cyl_axis(f"{side}_pipe{k}", 0.016, (xo, -HD + 0.12, pz), (xo, HD - 0.12, pz), m["copper"], root)
                for y in (-HD + 0.18, 0.0, HD - 0.18):
                    cyl_axis(f"{side}_clamp{k}_{y:.2f}", 0.021, (xo, y - 0.012, pz), (xo, y + 0.012, pz),
                             m["trimm"], root)
        # the port: left input, right output
        io.mount(root, "in" if sgn < 0 else "out", (sgn * HW, PORT_Y, PORT_Z), (sgn, 0, 0), m, f"{side}_port")
        # the arm on the table, plus a second smaller arm from level 4
        bx = (ix0 + ix1) / 2
        joints[side] = arm(f"{side}_arm", root, (bx, 0.42, TABLE_Z + 0.015), m,
                           tool="welder" if sgn < 0 else "drill", scale=ARM)
        pose(joints[side], **REST)
        if lv["second_arm"]:
            j2 = arm(f"{side}_arm2", root, (bx - sgn * 0.2, 0.2, TABLE_Z + 0.015), m,
                     tool="drill" if sgn < 0 else "welder", scale=0.72 * ARM)
            pose(j2, yaw=sgn * 35, shoulder=-10, elbow=115, wrist=45)
            joints[side + "2"] = j2
        # level 5 core: a glowing teal ring on the back wall of the bay
        if lv["core"]:
            for ob in (io.annulus(f"{side}_core", 0.12, 0.15, 0.0, 0.02, m["glow"], root, segs=64),
                       io.cylinder(f"{side}_coreDisc", 0.12, 0.0, 0.005, m["dark"], root)):
                ob.location = (bx, BACK_Y - 0.01, 1.2)
                ob.rotation_euler = (math.radians(90), 0, 0)

    # rear: pegboard in a frame (the back is recessed so the tools stay in the footprint), lower access panels
    box("rearLower", (-HW + 0.03, HW - 0.03), (REAR, HD - 0.012), (PLINTH, 0.74), m["frame"], root, bevel=0.01)
    for k, (x0, x1) in enumerate(((-1.1, -0.42), (-0.32, 0.32), (0.42, 1.1))):
        box(f"rearPanel{k}", (x0, x1), (HD - 0.014, HD - 0.002), (0.32, 0.68), m["panel"], root, bevel=0.006)
    louvres("rearVent", (0.62, 0.92), (HD - 0.002, HD - 0.0), (0.4, 0.6), m, root, 4, "+y")
    for nme, x, z in (("pegFrameB", (-HW + 0.03, HW - 0.03), (0.74, 0.79)), ("pegFrameT", (-HW + 0.03, HW - 0.03),
                      (1.55, BAY_TOP - 0.15)), ("pegFrameL", (-HW + 0.03, -HW + 0.1), (0.74, BAY_TOP - 0.15)),
                      ("pegFrameR", (HW - 0.1, HW - 0.03), (0.74, BAY_TOP - 0.15))):
        box(nme, x, (REAR, HD), z, m["trimm"], root, bevel=0.008)
    box("pegboard", (-HW + 0.1, HW - 0.1), (REAR, REAR + 0.02), (0.79, 1.55), m["pegboard"], root, bevel=0.0)
    tools_on_board(m, root, REAR + 0.02)
    return root, joints, m


def tools_on_board(m, root, y):
    """Copper and steel tools hung on the pegboard: wrench, screwdriver, saw, level, hammer."""
    out = y + 0.004
    # wrench
    box("wrenchShaft", (-0.92, -0.88), (out, out + 0.012), (0.95, 1.4), m["steel"], root, bevel=0.003)
    w = io.annulus("wrenchHead", 0.025, 0.05, 0.0, 0.012, m["steel"], root, segs=6)
    w.location = (-0.9, out, 1.43)
    w.rotation_euler = (math.radians(-90), 0, 0)
    # screwdriver
    box("sdHandle", (-0.64, -0.6), (out, out + 0.03), (1.3, 1.45), m["copper"], root, bevel=0.01)
    box("sdShaft", (-0.625, -0.615), (out, out + 0.01), (1.05, 1.3), m["steel"], root, bevel=0.0)
    # saw
    box("sawHandle", (-0.32, -0.2), (out, out + 0.025), (1.32, 1.46), m["copper"], root, bevel=0.012)
    box("sawBlade", (-0.3, -0.25), (out, out + 0.004), (0.92, 1.33), m["steel"], root, bevel=0.0)
    # level (spirit level) with a teal bubble
    box("level", (0.15, 0.6), (out, out + 0.03), (1.38, 1.44), m["copper"], root, bevel=0.006)
    box("levelBubble", (0.35, 0.4), (out + 0.03, out + 0.032), (1.395, 1.425), m["glow"], root, bevel=0)
    # hammer
    box("hammerHandle", (0.86, 0.89), (out, out + 0.02), (0.95, 1.35), m["copper"], root, bevel=0.006)
    box("hammerHead", (0.8, 0.96), (out, out + 0.04), (1.35, 1.42), m["steel"], root, bevel=0.008)
    # hooks
    for x in (-0.9, -0.62, -0.26, 0.2, 0.55, 0.875):
        cyl_axis(f"hook{x:.2f}", 0.006, (x, y, 1.47), (x, y + 0.04, 1.49), m["steel"], root, segs=8)


# =====================================================================================================
# Working state (for the review stills): arms at the item, sparks, the item being made
# =====================================================================================================
def working(root, joints, m, t=0.0):
    """Pose the arms at phase t (0..1 of the work loop) and add the item, sparks and glow for a still."""
    for side, sgn in (("L", -1), ("R", 1)):
        j = joints[side]
        bob = math.sin(2 * math.pi * t)
        pose(j, yaw=sgn * 6 * bob, shoulder=WORK["shoulder"] + 3 * bob, elbow=WORK["elbow"] - 4 * bob,
             wrist=WORK["wrist"] + 6 * bob)
        if side + "2" in joints:
            pose(joints[side + "2"], **arm2_work(sgn))
        bx = joints[side]["yaw"].parent.location.x
        # the item on the table: a half-built gearbox under a teal scan line
        ix, iy, iz = bx, -0.22, TABLE_Z + 0.015
        box(f"{side}_item", (ix - 0.12, ix + 0.12), (iy - 0.1, iy + 0.1), (iz, iz + 0.11), m["panel"], root,
            bevel=0.01)
        g = io.annulus(f"{side}_itemGear", 0.03, 0.075, 0.0, 0.03, m["copper"], root, segs=12, bevel=0.003)
        g.location = (ix, iy, iz + 0.11)
        box(f"{side}_scan", (ix - 0.14, ix + 0.14), (iy - 0.12, iy + 0.12), (iz + 0.075, iz + 0.078), m["holo"], root,
            bevel=0)
    bpy.context.view_layer.update()
    # sparks at the welder tip, chips at the drill
    for side in ("L", "R"):
        tip = joints[side]["tip"].matrix_world.translation.copy()
        hot = io.cylinder(f"{side}_hotspot", 0.02, -0.008, 0.008, m["hot"], root, segs=16)
        hot.location = tip
        light = bpy.data.lights.new(f"{side}_sparkLight", "POINT")
        light.energy = 25 if side == "L" else 8
        light.color = (1.0, 0.7, 0.4)
        light.shadow_soft_size = 0.02
        lob = bpy.data.objects.new(f"{side}_sparkLight", light)
        bpy.context.scene.collection.objects.link(lob)
        lob.location = tip + Vector((0, -0.03, 0.02))
        lob.parent = None
        n = 60 if side == "L" else 24
        for k in range(n):
            # deterministic spray: a cone thrown up and toward the front
            a = 2 * math.pi * ((k * 0.61803) % 1.0)
            e = math.radians(15 + 60 * ((k * 0.38197) % 1.0))
            length = 0.05 + 0.1 * ((k * 0.7548) % 1.0)
            dvec = Vector((math.cos(a) * math.cos(e), -abs(math.sin(a)) * math.cos(e) - 0.2, math.sin(e)))
            start = tip + dvec.normalized() * (0.02 + 0.08 * ((k * 0.5698) % 1.0))
            cyl_axis(f"{side}_spark{k}", 0.0028, start, start + dvec.normalized() * length * 0.6, m["spark"], root,
                     segs=6)


# =====================================================================================================
# The game model: the item, scan line and hot spots as nodes, the clips, merged meshes, the export
# =====================================================================================================
def work_nodes(root, joints, m):
    """The animated extras: the item on each table (a blank that grows into a half-built gearbox), the scan line
    over it and a hot spot on each welder's tip. They start hidden (scale HIDDEN); the clips show them."""
    nodes = {}
    for side in ("L", "R"):
        bx = joints[side]["yaw"].parent.location.x
        item = empty(f"{side}_item", root, (bx, -0.22, TABLE_Z + 0.015))
        box(f"{side}_itemBody", (-0.12, 0.12), (-0.1, 0.1), (0.0, 0.11), m["panel"], item, bevel=0.01)
        g = io.annulus(f"{side}_itemGear", 0.03, 0.075, 0.0, 0.03, m["copper"], item, segs=12, bevel=0.003)
        g.location = (0, 0, 0.11)
        scan = empty(f"{side}_scan", root, (bx, -0.22, TABLE_Z + 0.09))
        for k, (x, y) in enumerate((((-0.14, 0.14), (-0.125, -0.115)), ((-0.14, 0.14), (0.115, 0.125)),
                                    ((-0.145, -0.135), (-0.12, 0.12)), ((0.135, 0.145), (-0.12, 0.12)))):
            box(f"{side}_scanLine{k}", x, y, (0.0, 0.004), m["scan"], scan, bevel=0)
        nodes[side] = dict(item=item, scan=scan, hot=[])
    for key, j in joints.items():
        if j.get("tool") == "welder":
            hot = empty(f"{key}_hot", j["tip"])
            io.cylinder(f"{key}_hotSpot", 0.02, -0.008, 0.008, m["hot"], hot, segs=16)
            nodes[key[0]]["hot"].append(hot)
    for n in nodes.values():
        for ob in (n["item"], n["scan"], *n["hot"]):
            ob.scale = (HIDDEN,) * 3
    return nodes


HIDDEN = 0.001     # the scale of a hidden node: Godot's importer can't take a zero scale


def smooth(u):
    return u * u * (3 - 2 * u)


def lerp_pose(a, b, u):
    return {k: a[k] + (b[k] - a[k]) * u for k in a}


def arm2_rest(sgn):
    return dict(yaw=sgn * 35, shoulder=-10, elbow=115, wrist=45)


def arm2_work(sgn):
    return dict(yaw=sgn * 26, shoulder=21, elbow=103, wrist=51)


def animate(joints, nodes, clip, t, length):
    """Poses everything for clip at t seconds (of length)."""
    u = t / length
    for key, j in joints.items():
        sgn = -1 if key[0] == "L" else 1
        second = len(key) > 1
        rest, work = (arm2_rest(sgn), arm2_work(sgn)) if second else (REST, WORK)
        if clip == "idle_loop":
            breathe = math.sin(2 * math.pi * u + (1.3 if second else 0) + (0.7 if sgn > 0 else 0))
            p = dict(rest, shoulder=rest["shoulder"] + 1.5 * breathe, wrist=rest["wrist"] + 3 * breathe)
        elif clip in ("start", "complete"):
            k = smooth(u if clip == "start" else 1 - u)
            p = lerp_pose(rest, work, k)
        else:
            bob = math.sin(2 * math.pi * u + (math.pi / 2 if second else 0))
            if j.get("tool") == "welder":       # the welder sweeps along the seam
                p = dict(work, yaw=work["yaw"] + sgn * (6 if not second else 3) * bob, shoulder=work["shoulder"] + 3 * bob,
                         elbow=work["elbow"] - 4 * bob, wrist=work["wrist"] + 6 * bob)
            else:                               # the drill bores: twice down and up a loop
                dip = math.sin(4 * math.pi * u)
                p = dict(work, shoulder=work["shoulder"] + 2 * dip, elbow=work["elbow"] - 1.5 * dip,
                         wrist=work["wrist"] + dip, yaw=work["yaw"] + (2 * bob if second else 0))
        pose(j, **p)
    for side, n in nodes.items():
        if clip == "idle_loop":
            item = scan = hot = 0.0
        elif clip == "start":
            item, scan, hot = smooth(u), smooth(u), 0.0
        elif clip == "complete":
            item, scan, hot = smooth(1 - u), smooth(1 - u), 0.0
        else:                                   # the item and scan line throb a little as they are worked
            item = 1.0 + 0.015 * math.sin(2 * math.pi * 4 * u)
            scan = 1.0 + 0.02 * math.sin(2 * math.pi * 2 * u)
            hot = 0.8 + 0.35 * math.sin(2 * math.pi * 7 * u) * math.sin(2 * math.pi * 3 * u)
        item, scan, hot = max(item, HIDDEN), max(scan, HIDDEN), max(hot, HIDDEN)
        n["item"].scale = (item,) * 3
        n["scan"].scale = (scan, scan, 1.0)
        z = TABLE_Z + 0.03 + (0.1 * (0.5 - 0.5 * math.cos(2 * math.pi * u)) if clip == "work_loop" else 0.06)
        n["scan"].location.z = z
        for h in n["hot"]:
            h.scale = (hot,) * 3


def bake(joints, nodes, level):
    """Every clip keys every animated node, so a clip never inherits another's pose. Godot's glTF import drops a
    channel that holds one value through a clip and resets it to the node's own transform (the idle pose), so a
    channel a clip holds away from idle must move a little; the bake checks that."""
    from build_lift import push_track  # noqa: PLC0415
    from lift_textures import linear_keys  # noqa: PLC0415

    rot = [j[k] for j in joints.values() for k in ("yaw", "shoulder", "elbow", "wrist")]
    extra = [ob for n in nodes.values() for ob in (n["item"], n["scan"], *n["hot"])]
    for ob in rot + extra:
        ob.animation_data_create()
    held = {}
    for clip, length in CLIPS.items():
        if clip == "work_loop" and level == 5:
            length /= 2
        acts = {}
        for ob in rot + extra:
            acts[ob] = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = acts[ob]
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            animate(joints, nodes, clip, f / FPS, length)
            for ob in rot:
                ob.keyframe_insert("rotation_euler", frame=f)
                held.setdefault((clip, ob.name, "rot"), []).append(tuple(ob.rotation_euler))
            for ob in extra:
                ob.keyframe_insert("scale", frame=f)
                ob.keyframe_insert("location", frame=f)
                held.setdefault((clip, ob.name, "scale"), []).append(tuple(ob.scale))
                held.setdefault((clip, ob.name, "loc"), []).append(tuple(ob.location))
        for ob, act in acts.items():
            linear_keys(act)
            push_track(ob, act, clip)
    animate(joints, nodes, "idle_loop", 0.0, CLIPS["idle_loop"])
    idle = {ob.name: dict(rot=tuple(ob.rotation_euler), scale=tuple(ob.scale), loc=tuple(ob.location))
            for ob in rot + extra}
    for (clip, name, prop), values in held.items():
        if all(v == values[0] for v in values) and any(abs(a - b) > 1e-4 for a, b in zip(values[0], idle[name][prop])):
            raise SystemExit(f"{clip}: {name} holds its {prop} at {values[0]}, away from idle {idle[name][prop]}")


def merge(root, anchors):
    """Bakes the modifiers and joins the meshes under each anchor (the root, the arms' joints, the animated
    nodes) into one mesh, so the model is a few dozen nodes instead of hundreds."""
    dg = bpy.context.evaluated_depsgraph_get()
    groups = {}
    for ob in [root, *root.children_recursive]:
        if ob.type != "MESH":
            continue
        a = ob.parent
        while a not in anchors:
            a = a.parent
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        me.transform(a.matrix_world.inverted() @ ob.matrix_world)
        part = bpy.data.objects.new(f"{a.name}_mesh", me)
        bpy.context.scene.collection.objects.link(part)
        groups.setdefault(a, []).append(part)
    for ob in [o for o in root.children_recursive if o not in anchors]:
        bpy.data.objects.remove(ob)
    for a, parts in groups.items():
        for p in parts:
            p.parent = a
        with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts,
                                       selected_objects=parts):
            bpy.ops.object.join()
        parts[0].name = f"{a.name}_mesh"


def export(top, path):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [top, *top.children_recursive]:
        ob.select_set(True)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_optimize_animation_keep_anim_object=True,
        export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=False,
        export_image_format="AUTO", export_extras=False, export_cameras=False, export_lights=False)


def spark_offsets(level):
    """Where the welder and drill tips are in the work pose, in the part's frame (metres, +Y up, front -Z, centred
    on the footprint), for display.effects."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, joints, m = build(level)
    animate(joints, {}, "work_loop", 0.0, 1.0)
    bpy.context.view_layer.update()
    out = {}
    for key, j in joints.items():
        x, y, z = j["tip"].matrix_world.translation
        out[key] = (round(-x, 3), round(z - H / 2, 3), round(y, 3), j["tool"])
    return out


def build_game(level):
    """Writes meshes/parts/fabricator_<level>.glb and its icon."""
    sys.path.insert(0, str(REPO / "tools" / "bearing_engine"))
    sys.path.insert(0, str(REPO / "tools" / "lift"))
    import build_bearing_engine as bbe  # noqa: PLC0415

    bpy.ops.wm.read_factory_settings(use_empty=True)
    root, joints, m = build(level)
    nodes = work_nodes(root, joints, m)
    bpy.context.view_layer.update()
    shown = [root, *root.children_recursive]
    bbe.render_icon(shown, ICONS / f"fabricator_{level}_icon.png", (1.0, -1.25, 0.85), 3.45, (0, 0, H / 2 - 0.08))
    anchors = {root}
    for j in joints.values():
        anchors.update((j["yaw"].parent, j["yaw"], j["shoulder"], j["elbow"], j["wrist"], j["tip"]))
    for n in nodes.values():
        for ob in (n["item"], n["scan"], *n["hot"]):
            ob.scale = (1, 1, 1)
            anchors.add(ob)
    bpy.context.view_layer.update()
    merge(root, anchors)
    bake(joints, nodes, level)
    top = empty(f"Fabricator{level}Model", None)
    top.rotation_euler.z = math.pi            # the model's front is -Y; the game's is -Z (glTF +Z after the y-up swap)
    root.parent = top
    root.location.z = -H / 2                  # centred on the part's cells
    export(top, MESHES / f"fabricator_{level}.glb")
    print(f"wrote fabricator_{level}.glb")


# =====================================================================================================
# Review renders
# =====================================================================================================
def render_review(out: Path):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    rl.SAMPLES = SAMPLES
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    sc.cycles.max_bounces = 6

    builds = {}
    for lv in LEVELS:
        builds[lv] = build(lv)

    def show(lv, extra=()):
        for k, (r, _, _) in builds.items():
            for ob in [r, *r.children_recursive]:
                ob.hide_render = k != lv
        for ob in extra:
            ob.hide_render = False

    cz = H / 2
    shots = {}
    iso = (5.2, -6.2, 4.6)
    for lv in LEVELS:
        show(lv)
        shots[("iso", lv)] = rl.render(rl.camera(f"iso{lv}", iso, (0, 0, cz - 0.15), ortho=4.2),
                                       out / f"fabricator_{lv}.png", 1100, 1000)
    show(1)
    for nme, loc, w, h, ortho in (("front", (0, -8, cz), 1200, 1080, 3.4),
                                  ("left", (-8, 0, cz), 1100, 1080, 2.75),
                                  ("right", (8, 0, cz), 1100, 1080, 2.75),
                                  ("back", (0, 8, cz), 1200, 1080, 3.4),
                                  ("top", (0, -0.0001, 9), 1200, 1000, 3.0)):
        cam = rl.camera(f"{nme}1", loc, (0, 0, cz) if nme != "top" else (0, 0, 0), ortho=ortho)
        if nme == "top":
            cam.rotation_euler = (0, 0, 0)
        shots[(nme, 1)] = (rl.render(cam, out / f"fabricator_1_{nme}.png", w, h), cam, w, h)
    show(5)
    shots[("back", 5)] = rl.render(rl.camera("back5", (5.2, 6.2, 4.6), (0, 0, cz - 0.15), ortho=4.2),
                                   out / "fabricator_5_back.png", 1100, 1000)

    # working stills at levels 1 and 5
    for lv in (1, 5):
        r, joints, m = builds[lv]
        before = set(bpy.data.objects)
        working(r, joints, m, 0.25)
        show(lv)
        for ob in set(bpy.data.objects) - before:
            ob.hide_render = False
        shots[("work", lv)] = rl.render(rl.camera(f"work{lv}", (0.25, -4.3, 1.75), (0, -0.4, 0.95), lens=40),
                                        out / f"fabricator_{lv}_working.png", 1400, 900)
        shots[("weld", lv)] = rl.render(rl.camera(f"weld{lv}", (-0.35, -2.2, 1.35), (-0.8, -0.1, 0.8), lens=45),
                                        out / f"fabricator_{lv}_weld.png", 1000, 800)
        for ob in set(bpy.data.objects) - before:
            ob.hide_render = True
        for side in ("L", "R"):
            pose(joints[side], **REST)

    # ---------------- sheet 1: level 1 multi-view ----------------
    F = rl.font
    INK = rl.INK
    sheet = Image.new("RGB", (3600, 2700), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), "FABRICATOR - DUAL BAY WITH PORTS, CORRECTED (design review, level 1)", fill=INK, font=F(50, True))
    d.text((60, 100), "Exactly 10 x 8 x 9 blocks (2.50 x 2.00 x 2.25 m). Input port on the left, output port on the "
           "right: standard IO ports, one block above the base, collars flush with the footprint.", fill=INK, font=F(26))

    def panel(img, x, y, w, label):
        im = Image.open(img).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + w - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(28, True))
        return im.height

    def dims(key, x, y, w, a3, b3, label, vertical=False, off=(0, 0)):
        _, cam, cw, ch = shots[key]
        s = w / cw
        a = rl.project(cam, a3, cw, ch)
        b = rl.project(cam, b3, cw, ch)
        rl.dim_line(d, (x + a[0] * s + off[0], y + a[1] * s + off[1]), (x + b[0] * s + off[0], y + b[1] * s + off[1]),
                    label, F(24), vertical=vertical)

    panel(shots[("iso", 1)], 60, 160, 1180, "ISOMETRIC")
    panel(shots[("front", 1)][0], 1270, 160, 1130, "FRONT")
    dims(("front", 1), 1270, 160, 1130, (-HW, -HD, 0), (HW, -HD, 0), "10 blocks (2.50 m)", off=(0, 30))
    dims(("front", 1), 1270, 160, 1130, (HW, -HD, 0), (HW, -HD, H), "9 blocks\n(2.25 m)", vertical=True, off=(30, 0))
    panel(shots[("top", 1)][0], 2430, 160, 1110, "TOP")
    dims(("top", 1), 2430, 160, 1110, (HW, -HD, H), (HW, HD, H), "8 blocks\n(2.00 m)", vertical=True, off=(14, 0))
    y2 = 1290
    panel(shots[("left", 1)][0], 60, y2, 1000, "LEFT - INPUT SIDE")
    dims(("left", 1), 60, y2, 1000, (0, HD, 0), (0, -HD, 0), "8 blocks (2.00 m)", off=(0, 24))
    panel(shots[("back", 1)][0], 1090, y2, 1180, "REAR - PEGBOARD")
    panel(shots[("right", 1)][0], 2300, y2, 1000, "RIGHT - OUTPUT SIDE")
    dims(("right", 1), 2300, y2, 1000, (0, -HD, 0), (0, HD, 0), "8 blocks (2.00 m)", off=(0, 24))
    notes = [
        "Corrections to the reference sheet:",
        "- True 10 x 8 x 9 envelope (the reference top view was 8 wide); nothing sticks outside it.",
        "- Standard IO ports (teal out / copper in), not the yellow and blue rings; same height and position on both sides.",
        "- Hazard stripes are teal on black, not safety yellow; the pegboard is gunmetal, not steel blue.",
        "- One status screen on the console, matching the crafting menu it opens.",
        "Port position (both sides, the same height on every machine): 2 x 2, centre 0.375 m up; "
        "half a block toward the front of centre (centre 0.875 m from the front edge).",
    ]
    for k, line in enumerate(notes):
        d.text((60, 2390 + k * 36), line, fill=INK, font=F(26, k == 0))
    sheet.save(out / "review_sheet.png")

    # ---------------- sheet 2: levels and working stills ----------------
    s2 = Image.new("RGB", (3600, 2700), (246, 246, 244))
    d = ImageDraw.Draw(s2)
    d.text((60, 30), "FABRICATOR - LEVELS 1 TO 5 AND THE WORK ANIMATION (design review)", fill=INK, font=F(50, True))
    d.text((60, 100), "Same body and ports at every level; level pips over the screen. Only level 1 is crafted; "
           "levels 2 to 5 are upgrades in place.", fill=INK, font=F(26))
    lvnotes = {
        1: ["Worn gunmetal, one arm per bay", "2 slots, 1x speed"],
        2: ["Polished, teal strip on each bay", "4 slots, 1x speed"],
        3: ["Darker finish, copper pipes, bigger vents", "6 slots, 1x speed"],
        4: ["Second small arm per bay, two strips", "8 slots, 1x speed"],
        5: ["Black chrome, glowing cores and exhaust", "8 slots, 2x speed"],
    }
    for i, lv in enumerate(LEVELS):
        x = 60 + i * 700
        im = Image.open(shots[("iso", lv)]).convert("RGB").resize((680, 618), Image.LANCZOS)
        s2.paste(im, (x, 170))
        d.rectangle([x, 170, x + 679, 787], outline=(190, 190, 190), width=2)
        d.text((x + 14, 180), f"LEVEL {lv}", fill=INK, font=F(32, True))
        for k, line in enumerate(lvnotes[lv]):
            d.text((x + 6, 800 + k * 34), line, fill=INK, font=F(24))
    y = 900
    for k, (key, label, w) in enumerate(((("work", 1), "LEVEL 1 WORKING: welder (left) and drill (right)", 1700),
                                         (("work", 5), "LEVEL 5 WORKING: second arms join in", 1700))):
        x = 60 + k * 1740
        im = Image.open(shots[key]).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
        s2.paste(im, (x, y + 40))
        d.text((x, y), label, fill=INK, font=F(28, True))
    y = 2130
    im = Image.open(shots[("weld", 1)]).convert("RGB").resize((560, 448), Image.LANCZOS)
    s2.paste(im, (60, y))
    d.text((60, y - 40), "WELD CLOSE-UP", fill=INK, font=F(28, True))
    im = Image.open(shots[("back", 5)]).convert("RGB").resize((492, 448), Image.LANCZOS)
    s2.paste(im, (660, y))
    d.text((660, y - 40), "LEVEL 5 REAR", fill=INK, font=F(28, True))
    anim = [
        "Animation and sound plan (all driven by the crafting state):",
        "- Idle: arms parked, teal lights breathe, screen shows the queue.  Sound: low hum.",
        "- Start: arms unfold to the table, ceiling lights come up.  Sound: fabricator_start (servo whirr).",
        "- Working: welder arm sweeps with sparks and a hot spot; drill arm bores with chips; the item",
        "  grows under a teal scan line; screen progress bar fills.  Sound: fabricator_work loop (arc crackle, drill).",
        "- Complete: arms fold back, teal flash on the screen and the output port collar.  Sound: fabricator_complete.",
        "- Level 5 runs the loop at double speed; the second arms join from level 4.",
        "- Menu: opens the existing Fabricator crafting menu (the one the Mini Fabricator uses); slots follow the level.",
    ]
    for k, line in enumerate(anim):
        d.text((1220, y - 40 + k * 40), line, fill=INK, font=F(26, k == 0))
    s2.save(out / "review_levels.png")
    print(f"rendered the review sheets into {out}")


def main(argv):
    if "--game" in argv:
        io.GAME = True
        for lv in LEVELS:
            build_game(lv)
        for lv in LEVELS:
            print(lv, spark_offsets(lv))
    elif "--render" in argv:
        render_review(Path(argv[argv.index("--render") + 1]))
    else:
        print(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
