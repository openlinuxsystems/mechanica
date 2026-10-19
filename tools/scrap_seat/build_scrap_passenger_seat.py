"""Build the Scrap Seat's model, its seat clips and its icon (Scrap Seat Design Spec, 2026-10-18).

The Scrap Seat is the passenger seat beside the Scrap Driver's Seat (``build_scrap_seat.py``, whose pipe, pallet,
cushion and clip helpers this reuses): lower, simpler and with no steering.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/scrap_seat.glb`` - the seat, 4 x 4 x 4 blocks (1.0 m wide, deep and high), centred on its
  origin, facing glTF -Z (the game's forward). A low wooden pallet with light metal corner caps and strips; an open
  frame of bent, lightly rusted gray-brown pipe on the deck round the seat, up the back and forward as an armrest each
  side, braced behind with rails, two struts and a V; a low, puffy olive-tan canvas seat pan and a reclined backrest
  with piped seams and a few clean, stitched patches. A small junction box with two brass terminals on the front right
  of the pallet is where its two links plug in. The backrest leans a little to one side, as built from scrap.
* The same four glTF clips as the driver's seat, played from the seat script's "clip" display (part data
  ``display.clips``): ``enter`` (0.8 s), ``seated_loop`` (2 s), ``seated_rattle_loop`` (2 s, while an engine runs) and
  ``exit`` (0.7 s). The cushions sink 1.6 cm (pan) and 1 cm (back) under the rider, the frame rattles, the pallet creaks.
  The nodes they move: ``Base`` (the pallet), ``Frame`` (a child of Base), ``SeatPan`` and ``Backrest`` (children of
  Frame). Their rest transforms are the empty seat.
* ``mods/vanilla/textures/items/scrap_seat_icon.png`` - the seat's picture for the hotbar, the inventory and the
  Fabricator, 256 x 256 on a clear background, seen from the front left and above.

Units are metres; in Blender Z is up and the front faces +Y, which the exporter turns into glTF -Z.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/scrap_seat/build_scrap_passenger_seat.py                  # writes the .glb and the icon
    python tools/scrap_seat/build_scrap_passenger_seat.py --render OUT_DIR # also renders the review sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Euler, Vector
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_scrap_seat as ds  # noqa: E402  (the driver's seat: pipe, pallet, cushion, clip and export helpers)
from build_scrap_seat import Geo, bl, bolt, box, br, coupler, cylinder, fillet_path, hexnut, lt, pipe, sweep, wood_box  # noqa: E402

REPO = HERE.parents[1]
OUT = REPO / "mods" / "vanilla" / "meshes" / "parts" / "scrap_seat.glb"
ICON = REPO / "mods" / "vanilla" / "textures" / "items" / "scrap_seat_icon.png"
FPS = ds.FPS

# ---- Dimensions (metres, Blender axes: x right, y forward, z up; the part's box is x, y and z +-0.5) --------------
HALF = (0.5, 0.5, 0.5)
DECK = -0.355                       # top of the pallet's deck boards (as the driver's seat)
R_PIPE = ds.R_PIPE
Z_LOOP = DECK + 0.024               # centreline of the pipe loop lying on the deck
Z_PAN = DECK + 0.075                # centreline of the low pipe loop under the seat pan
LOOP_X, LOOP_Y = 0.40, (-0.42, 0.40)
PAN_X, PAN_Y = 0.36, (-0.38, 0.29)
BACK_TOP = 0.462                    # centreline of the backrest frame's top bar
BACK_Y = (-0.42, -0.462)            # the backrest uprights' y at the loop and at the top (they lean back)
PAN_HALF = (0.33, 0.30, 0.075)      # seat pan cushion
PAN_AT = (0.0, -0.055, Z_PAN + R_PIPE + 0.015)    # its bottom centre (on a board over the pan frame)
PAN_TOP = PAN_AT[2] + 2 * PAN_HALF[2]
BACK_HALF = (0.31, 0.06, 0.215)     # backrest cushion
BACK_LEAN = math.degrees(math.atan2(BACK_Y[0] - BACK_Y[1], BACK_TOP - Z_LOOP)) + 7.0   # the cushion reclines more
BACK_AT = (0.006, -0.405, PAN_TOP + 0.2)         # the backrest cushion's back middle
ARM_Z = PAN_TOP + 0.13              # armrest pipe height
HIP = (0.0, -0.17, DECK)            # under the seated rider's hips, at the deck: the part data's seat offset
PAN_SINK, BACK_SINK = 0.016, 0.010  # how far the cushions sink under the rider
JB = Vector((0.40, 0.452, DECK))    # the junction box's foot


def upright_y(z):
    t = (z - Z_LOOP) / (BACK_TOP - Z_LOOP)
    return BACK_Y[0] + (BACK_Y[1] - BACK_Y[0]) * t


# =====================================================================================================
# The pallet (Base)
# =====================================================================================================

def build_base():
    g = Geo()
    rng = np.random.default_rng(17)
    hx, hy = HALF[0] - 0.012, HALF[1] - 0.012
    rows = (-0.415, 0.0, 0.415)
    # bottom boards along y, blocks, stringer boards along x, deck boards along y
    for i, x in enumerate((-0.425, 0.0, 0.425)):
        wood_box(g, (x, 0, -0.49), (0.07, hy, 0.01), 1, 300 + i)
    for i, x in enumerate((-0.43, 0.0, 0.43)):
        for j, y in enumerate(rows):
            wood_box(g, (x, y, -0.44), (0.065, 0.073, 0.04), 1, 310 + 3 * i + j)
    for j, y in enumerate(rows):
        wood_box(g, (0, y, -0.39), (hx, 0.073, 0.01), 0, 330 + j)
    gap = 0.012
    w = (2 * hx - 5 * gap) / 6
    for i in range(6):
        x = -hx + w / 2 + i * (w + gap)
        dz = float(rng.uniform(-0.0015, 0.0012))
        dy = float(rng.uniform(-0.004, 0.0))
        wood_box(g, (x, dy, DECK - 0.0125 + dz), (w / 2, hy + dy * 0.5, 0.0125), 1, 340 + i, turn=float(rng.uniform(-0.3, 0.3)))
        for y in rows:                                  # nail heads over the stringers
            for s in (-1, 1):
                ds.dome(g, (x + s * w * 0.28, y + float(rng.uniform(-0.02, 0.02)), DECK + dz), (0, 0, 1), 0.0042, "bolt", segs=6)

    # light metal: a cap over each corner's top and an L down its two outer sides, bolted; a strip at front and back
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (hx - 0.06), sy * (hy - 0.065)
            box(g, (cx, cy, DECK + 0.002), (0.06, 0.065, 0.002), "plate", bevel=0.001)
            for bx, by in ((0.03, 0.03), (-0.03, -0.035)):
                bolt(g, (cx + sx * bx, cy + sy * by, DECK + 0.004))
            box(g, (sx * (hx + 0.002), cy, -0.42), (0.002, 0.065, 0.064), "plate", bevel=0.001)
            box(g, (cx, sy * (hy + 0.002), -0.42), (0.06, 0.002, 0.064), "plate", bevel=0.001)
            for z in (-0.37, -0.45):
                bolt(g, (sx * (hx + 0.004), cy, z), (sx, 0, 0), r=0.0065)
                bolt(g, (cx, sy * (hy + 0.004), z), (0, sy, 0), r=0.0065)
    for y in (-0.455, 0.455):
        box(g, (0, y, DECK + 0.0015), (0.32, 0.02, 0.0015), "plate", bevel=0.0008)
        for i in range(6):
            x = -hx + w / 2 + i * (w + gap)
            if abs(x) < 0.31:
                bolt(g, (x, y, DECK + 0.003), r=0.0055)
    return g


# =====================================================================================================
# The pipe frame (Frame)
# =====================================================================================================

def build_frame():
    g = Geo()
    x, (y0, y1) = LOOP_X, LOOP_Y
    # the loop on the deck, round the seat
    pipe(g, fillet_path([(-x, y0, Z_LOOP), (x, y0, Z_LOOP), (x, y1, Z_LOOP), (-x, y1, Z_LOOP)], 0.08, closed=True))
    # clamps holding it down: a band over the pipe and a bolted tab each side
    for cx, cy, along in ((x, -0.15, "y"), (-x, -0.15, "y"), (x, 0.2, "y"), (-x, 0.2, "y"), (0.18, y1, "x"),
                          (-0.18, y1, "x"), (0.0, y0, "x")):
        a = Vector((0, 1, 0)) if along == "y" else Vector((1, 0, 0))
        side = Vector((1, 0, 0)) if along == "y" else Vector((0, 1, 0))
        coupler(g, (cx, cy, Z_LOOP), a, r=R_PIPE + 0.004, length=0.024, mat="plate")
        for s in (-1, 1):
            p = Vector((cx, cy, DECK + 0.002)) + side * s * 0.042
            if abs(p.x) > 0.48 or abs(p.y) > 0.48:
                continue
            box(g, tuple(p), (0.012 if along == "x" else 0.016, 0.016 if along == "x" else 0.012, 0.002), "plate")
            bolt(g, tuple(p + Vector((0, 0, 0.002))), r=0.006)

    # the low seat pan frame on four short legs, with tees where they meet the loops, and a board for the cushion
    px = PAN_X
    pipe(g, fillet_path([(-px, PAN_Y[0], Z_PAN), (px, PAN_Y[0], Z_PAN), (px, PAN_Y[1], Z_PAN), (-px, PAN_Y[1], Z_PAN)], 0.05, closed=True), r=0.018)
    for lx in (-px, px):
        for ly in (-0.30, 0.20):
            pipe(g, [(lx, ly, DECK + 0.004), (lx, ly, Z_PAN)], r=0.016)
            coupler(g, (lx, ly, Z_PAN), (0, 1, 0), r=0.024, length=0.05)
            box(g, (lx, ly, DECK + 0.003), (0.03, 0.03, 0.003), "plate", bevel=0.001)
            bolt(g, (lx + 0.02, ly + 0.02, DECK + 0.006), r=0.005)
            bolt(g, (lx - 0.02, ly - 0.02, DECK + 0.006), r=0.005)
    wood_box(g, (0, PAN_AT[1], Z_PAN + 0.018 + 0.0075), (0.345, 0.32, 0.0075), 0, 401)
    for ly in (-0.20, 0.06):
        pipe(g, [(-px, ly, Z_PAN), (px, ly, Z_PAN)], r=0.013)

    # the backrest frame: two uprights leaning back, a top bar with round corners (built a little off true), two rails,
    # two inner struts and a V brace low down
    lean = 0.009                                         # the top sits 9 mm to the right of the bottom
    top = [(-x, BACK_Y[0], Z_LOOP), (-x + lean, BACK_Y[1], BACK_TOP), (x + lean, BACK_Y[1] - 0.003, BACK_TOP),
           (x, BACK_Y[0], Z_LOOP)]
    pipe(g, fillet_path(top, 0.085))
    for lx in (-x, x):
        coupler(g, (lx, BACK_Y[0], Z_LOOP), (0, 1, 0), length=0.058)
    rails = (-0.13, 0.20)
    for z in rails:
        y = upright_y(z)
        t = (z - Z_LOOP) / (BACK_TOP - Z_LOOP)
        pipe(g, [(-x + lean * t, y, z), (x + lean * t, y, z)], r=0.016)
        for lx in (-x, x):
            coupler(g, (lx + lean * t, y, z), (0, 0, 1), r=R_PIPE + 0.005, length=0.045)
    for sx in (-0.17, 0.17):                             # inner struts from the low rail to the top bar
        z0, z1 = rails[0], BACK_TOP - 0.004
        pipe(g, [(sx + lean * 0.3, upright_y(z0) - 0.002, z0), (sx + lean, upright_y(z1), z1)], r=0.014)
        coupler(g, (sx + lean * 0.3, upright_y(z0), z0), (1, 0, 0), r=0.019, length=0.035)
        coupler(g, (sx + lean * 0.65, upright_y(rails[1]), rails[1]), (1, 0, 0), r=0.019, length=0.035)
    apex = (0.0, upright_y(rails[0]) - 0.004, rails[0])
    for lx in (-x, x):                                   # the V: from the bottom of each upright up to the low rail
        foot = (lx * 0.97, upright_y(Z_LOOP + 0.05) - 0.004, Z_LOOP + 0.05)
        pipe(g, [foot, apex], r=0.013)
    coupler(g, apex, (1, 0, 0), r=0.019, length=0.05)

    # the armrests: out of each upright, forward at elbow height and bending down to the front of the deck loop
    for lx in (-x, x):
        start = (lx, upright_y(ARM_Z), ARM_Z)
        path = fillet_path([start, (lx, 0.24, ARM_Z), (lx, 0.33, Z_LOOP + 0.03), (lx, 0.34, Z_LOOP)], 0.09)
        pipe(g, path, r=0.019)
        coupler(g, start, (0, 0, 1), r=R_PIPE + 0.005, length=0.045)
        coupler(g, (lx, 0.34, Z_LOOP), (0, 1, 0), length=0.05)
        # a short stay from the armrest down to the pan frame
        pipe(g, [(lx, -0.02, ARM_Z), (lx * PAN_X / x + (0.008 if lx > 0 else -0.008), -0.02, Z_PAN)], r=0.012)

    # the junction box the links plug into: a bolted box with two brass terminals and a lead to the pan frame
    jb = JB
    box(g, tuple(jb + Vector((0, 0, 0.032))), (0.04, 0.03, 0.03), "plate", bevel=0.004)
    box(g, tuple(jb + Vector((0, 0, 0.002))), (0.055, 0.035, 0.002), "plate", bevel=0.001)
    for sx in (-1, 1):
        bolt(g, tuple(jb + Vector((sx * 0.047, 0, 0.004))), r=0.005)
    for sx in (-1, 1):
        p = jb + Vector((sx * 0.015, 0, 0.062))
        cylinder(g, p, p + Vector((0, 0, 0.014)), 0.0065, "brass", segs=10)
        hexnut(g, p + Vector((0, 0, 0.005)), (0, 0, 1), 0.009, 0.004, "brass")
    lead = [jb + Vector((0.0, -0.03, 0.03)), jb + Vector((0.0, -0.06, 0.012)), Vector((PAN_X + 0.01, 0.30, DECK + 0.01)),
            Vector((PAN_X, 0.22, DECK + 0.03)), Vector((PAN_X, 0.18, Z_PAN - 0.02))]
    sweep(g, [Vector(p) for p in bl.bezier(lead[0], lead[1], lead[2], lead[3], 12)] + [lead[4]], 0.0045, "bolt", segs=8)
    return g


PAN_PATCHES = {
    (2, 1): [{"c": (0.12, 0.08), "half": (0.06, 0.05), "turn": -5.0, "tone": 0},
             {"c": (-0.15, -0.12), "half": (0.045, 0.035), "turn": 7.0, "tone": 1}],
    (1, 1): [{"c": (0.16, 0.0), "half": (0.05, 0.028), "turn": 0.0, "tone": 2}],
}
BACK_PATCHES = {
    (1, 1): [{"c": (-0.11, 0.07), "half": (0.06, 0.05), "turn": 4.0, "tone": 1},
             {"c": (0.13, -0.10), "half": (0.045, 0.036), "turn": -6.0, "tone": 0}],
    (1, -1): [{"c": (0.06, 0.03), "half": (0.07, 0.055), "turn": -2.0, "tone": 2}],
}


# =====================================================================================================
# Scene and clips
# =====================================================================================================

def build_scene():
    ds.reset_scene()
    mats = ds.st.make_materials()
    root = bpy.data.objects.new("ScrapSeat", None)
    bpy.context.scene.collection.objects.link(root)
    rig = {"root": root}
    base = rig["base"] = ds.make_object("Base", build_base(), mats, root)
    frame = rig["frame"] = ds.make_object("Frame", build_frame(), mats, base, (0, 0, DECK))
    pan = ds.build_cushion("SeatPan", PAN_HALF, 2, (0.007, 0.007, 0.009), 0.011, PAN_PATCHES, 711, (0, 0, PAN_HALF[2]))
    pan.parent = frame
    pan.location = Vector(PAN_AT) - Vector((0, 0, DECK))
    rig["pan"] = pan
    back = ds.build_cushion("Backrest", BACK_HALF, 1, (0.006, 0.006, 0.006), 0.007, BACK_PATCHES, 731, (0, BACK_HALF[1], 0))
    back.parent = frame
    back.location = Vector(BACK_AT) - Vector((0, 0, DECK))
    back.rotation_euler = Euler((math.radians(BACK_LEAN), 0, math.radians(0.8)))
    rig["back"] = back
    return rig


def pose(rig, clip, t):
    """Sets every animated node for `clip` at time t (seconds): the driver's seat's clips, scaled to this seat."""
    base, frame, pan, back = rig["base"], rig["frame"], rig["pan"], rig["back"]
    pan_scale, back_scale = 1.0, 1.0
    fr_loc, fr_rot, base_z, base_x = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0], 0.0, 0.0
    breath = math.sin(2 * math.pi * t / 2.0)
    pan_k, back_k = PAN_SINK / (2 * PAN_HALF[2]), BACK_SINK / (2 * BACK_HALF[1])
    if clip == "enter":
        pan_scale = 1 - pan_k * ds.sink(t)
        back_scale = 1 - back_k * ds.sink(t - 0.08)
        decay = math.exp(-max(t - 0.05, 0) / 0.16) * ds.smooth(t / 0.05) * (1 - ds.smooth((t - 0.62) / 0.18))
        rx, ry, rz = ds.shake(t, math.radians(0.8) * decay, 11)
        fr_rot = [rx, ry, rz * 0.5]
        fr_loc = [v * 0.0025 * decay for v in ds.shake(t, 1.0, 12, (14.0, 10.0, 12.0))]
        bump = math.sin(math.pi * min(max(t - 0.03, 0) / 0.42, 1)) * math.exp(-max(t - 0.03, 0) / 0.3)
        base_z = -0.005 * bump
        base_x = -0.002 * bump
    elif clip in ("seated_loop", "seated_rattle_loop"):
        pan_scale = 1 - pan_k * (1 + 0.12 * breath)
        back_scale = 1 - back_k * (1 + 0.15 * math.sin(2 * math.pi * t / 2.0 + 0.4))
        if clip == "seated_rattle_loop":
            # whole numbers of cycles in the 2 s loop, so it loops seamlessly
            rx, ry, rz = ds.shake(t, math.radians(0.25), 13, (9.0, 13.0, 7.5))
            fr_rot = [rx, ry, rz * 0.6]
            fr_loc = [v * 0.0015 for v in ds.shake(t, 1.0, 14, (11.0, 7.0, 13.0))]
            fr_loc[2] *= 1.6                              # the up-and-down shake reaches 2.4 mm
            pan_scale += 0.0012 / (2 * PAN_HALF[2]) * math.sin(2 * math.pi * 6.5 * t)
            back_scale += 0.001 / (2 * BACK_HALF[1]) * math.sin(2 * math.pi * 8.0 * t + 1.0)
    elif clip == "exit":
        pan_scale = 1 - pan_k * (1 - ds.sink(t, 0.12))
        back_scale = 1 - back_k * (1 - ds.sink(t - 0.04, 0.12))
        decay = math.exp(-t / 0.14) * (1 - ds.smooth((t - 0.5) / 0.2))
        rx, ry, rz = ds.shake(t, math.radians(0.6) * decay, 15)
        fr_rot = [rx, ry, rz * 0.5]
        fr_loc = [v * 0.002 * decay for v in ds.shake(t, 1.0, 16, (12.0, 9.0, 14.0))]
        bump = math.sin(math.pi * min(t / 0.4, 1)) * math.exp(-t / 0.25)
        base_z = 0.003 * bump
    frame.location = Vector((0, 0, DECK)) + Vector(fr_loc)
    frame.rotation_euler = Euler(fr_rot)
    base.location = Vector((base_x, 0, base_z))
    pan.scale = Vector((1, 1, pan_scale))
    back.scale = Vector((1, back_scale, 1))


def bake(rig):
    obs = [rig["base"], rig["frame"], rig["pan"], rig["back"]]
    for ob in obs:
        ob.animation_data_create()
    for clip, seconds in ds.CLIPS.items():
        actions = {}
        for ob in obs:
            act = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = act
            actions[ob.name] = act
        frames = int(round(seconds * FPS))
        for f in range(frames + 1):
            # a loop's last key equals its first, so it wraps without a jump
            pose(rig, clip, (f % frames if clip.startswith("seated") else f) / FPS)
            for ob in obs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in obs:
            lt.linear_keys(actions[ob.name])
            bl.push_track(ob, actions[ob.name], clip)
    rest(rig)


def rest(rig):
    rig["base"].location = (0, 0, 0)
    rig["frame"].location = (0, 0, DECK)
    rig["frame"].rotation_euler = (0, 0, 0)
    for ob in (rig["base"], rig["frame"], rig["pan"], rig["back"]):
        ob.scale = (1, 1, 1)
    rig["base"].rotation_euler = (0, 0, 0)
    rig["pan"].rotation_euler = (0, 0, 0)
    rig["back"].rotation_euler = Euler((math.radians(BACK_LEAN), 0, math.radians(0.8)))


# =====================================================================================================
# Review sheet
# =====================================================================================================

def render_review(rig, out: Path):
    """The reference sheet's views, a scale view in a 4 x 4 x 4 block outline and the clips, on one labelled sheet."""
    import render_lift as rl  # noqa: PLC0415
    import render_scrap_seat as rs  # noqa: PLC0415
    out.mkdir(parents=True, exist_ok=True)
    for ob in bpy.context.scene.objects:      # the baked clips would override the poses set below
        ob.animation_data_clear()
    ground = rl.setup_world()
    ground.location.z = -0.5
    sc = bpy.context.scene
    sc.cycles.samples = 64
    sc.render.film_transparent = False
    rest(rig)
    bpy.context.view_layer.update()
    W, H = 900, 640
    shots, dims = {}, {}
    f_dim = rl.font(20)
    cam = rl.camera("ThreeQuarter", (-2.3, 2.6, 1.5), (0, 0.0, -0.1), lens=50)
    shots["three_quarter"], dims["three_quarter"] = rl.render(cam, out / "scrap_seat_three_quarter.png", W, H), []
    cam = rl.camera("Side", (10, 0, 0.0), (0, 0, 0.0), ortho=1.6)
    shots["side"] = rl.render(cam, out / "scrap_seat_side.png", W, H)
    dims["side"] = [(rl.project(cam, (0.5, -0.5, -0.56), W, H), rl.project(cam, (0.5, 0.5, -0.56), W, H), "4 BLOCKS (1.00 m)", (0, 10), False),
                    (rl.project(cam, (0.5, 0.56, -0.5), W, H), rl.project(cam, (0.5, 0.56, 0.5), W, H), "4 BLOCKS\n(1.00 m)", (0, 0), True)]
    cam = rl.camera("Rear", (0, -10, 0.0), (0, 0, 0.0), ortho=1.6)
    shots["rear"] = rl.render(cam, out / "scrap_seat_rear.png", W, H)
    dims["rear"] = [(rl.project(cam, (-0.5, -0.56, -0.56), W, H), rl.project(cam, (0.5, -0.56, -0.56), W, H), "4 BLOCKS (1.00 m)", (0, 10), False)]
    cam = rl.camera("Top", (0, 0.0001, 10), (0, 0, 0), ortho=1.5)
    cam.rotation_euler.z = math.radians(180)
    shots["top"], dims["top"] = rl.render(cam, out / "scrap_seat_top.png", W, H), []
    cam = rl.camera("Fabric", (-0.3, 0.45, 0.25), (0.02, -0.15, -0.12), lens=60)
    shots["fabric"], dims["fabric"] = rl.render(cam, out / "scrap_seat_fabric.png", W, H), []
    box_ = rs.outline_box("Blocks", HALF, (0, 0, 0))
    sil = rl.silhouette(-0.95, 0.2)
    cam = rl.camera("Scale", (-3.0, 3.2, 1.5), (0.3, 0, 0.1), lens=40)
    shots["scale"], dims["scale"] = rl.render(cam, out / "scrap_seat_scale.png", W, H), []
    box_.hide_render = True
    sil.hide_render = True

    strips = []
    cam = rl.camera("Clip", (-1.9, 1.5, 0.8), (0, -0.1, -0.15), lens=50)
    for clip, times in (("enter", (0.0, 0.05, 0.1, 0.2, 0.4, 0.8)), ("exit", (0.0, 0.06, 0.12, 0.25, 0.45, 0.7)),
                        ("seated_rattle_loop", (0.0, 0.03, 0.07, 0.1, 0.13, 0.17))):
        frames = []
        for t in times:
            pose(rig, clip, t)
            bpy.context.view_layer.update()
            frames.append((t, rl.render(cam, out / f"scrap_seat_{clip}_{int(t * 100):03d}.png", 480, 360)))
        strips.append((clip, frames))
    rest(rig)

    ink = rl.INK
    title_f, head_f = rl.font(44, True), rl.font(24, True)
    sheet = Image.new("RGB", (2860, 2520), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((50, 36), "MECHANICA SCRAP SEAT (PASSENGER SEAT)", fill=ink, font=title_f)
    info = br.glb_info(OUT)
    d.text((50, 96), f"4 x 4 x 4 blocks (1.00 m wide, deep and high). Clean scrapyard: light-rust pipe, raw pallet wood, "
           f"olive-tan canvas with simple patches, light metal plates. {info['tris']} triangles, {info['kib']:.0f} KiB.",
           fill=ink, font=rl.font(24))

    def place(key, title, x, y):
        im = Image.open(shots[key]).convert("RGB")
        sheet.paste(im, (x, y))
        dd = ImageDraw.Draw(sheet)
        dd.rectangle([x, y, x + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        dd.text((x, y - 34), title, fill=ink, font=head_f)
        for a, b, text, off, vert in dims[key]:
            rl.dim_line(dd, (x + a[0], y + a[1]), (x + b[0], y + b[1]), text, f_dim, off, vert)

    place("three_quarter", "FRONT THREE-QUARTER VIEW", 50, 190)
    place("side", "SIDE VIEW", 980, 190)
    place("rear", "REAR VIEW", 1910, 190)
    place("top", "TOP VIEW", 50, 900)
    place("fabric", "FABRIC DETAIL (puffy stitched canvas, clean patches)", 980, 900)
    place("scale", "SCALE (4 x 4 x 4 block box, 1.5 m figure)", 1910, 900)
    y = 1610
    for clip, frames in strips:
        d.text((50, y - 34), {"enter": "ENTER (0.8 s): cushions sink, frame rattles, pallet creaks",
                              "exit": "EXIT (0.7 s): cushions spring back, last rattle",
                              "seated_rattle_loop": "SEATED WITH AN ENGINE RUNNING (2 s loop): low rattle"}[clip], fill=ink, font=head_f)
        for i, (t, p) in enumerate(frames):
            im = Image.open(p).convert("RGB").resize((300, 225), Image.LANCZOS)
            sheet.paste(im, (50 + i * 305, y))
            d.text((56 + i * 305, y + 6), f"{t:.2f} s", fill=ink, font=rl.font(20, True))
        y += 290
    sheet.save(out / "scrap_seat_review_sheet.png")
    print(f"rendered the review sheet into {out}")


# =====================================================================================================
# Main
# =====================================================================================================

def main():
    rig = build_scene()
    bake(rig)
    ds.export(OUT)
    i = br.glb_info(OUT)
    print(f"wrote {OUT.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
          f"{i['materials']} materials, {i['extras']}) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()}")
    icon = ds.render_icon(ICON, view=(-2.0, 2.2, 1.7), ortho_scale=1.45)
    print(f"wrote {icon.relative_to(REPO)}")
    print(f"seat offset (part frame, glTF axes): [{HIP[0]:.3f}, {HIP[2]:.3f}, {-HIP[1]:.3f}]; "
          f"cushion top {PAN_TOP:.3f} ({PAN_TOP - PAN_SINK:.3f} sunk); backrest lean {BACK_LEAN:.1f} deg")
    if "--render" in sys.argv:
        render_review(rig, Path(sys.argv[sys.argv.index("--render") + 1]))


if __name__ == "__main__":
    main()
