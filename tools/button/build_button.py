"""Build the Button's model, its press and release clips and its icon (Button Design Spec 1.0, 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/button.glb`` - the Button, one block (0.25 m) across, centred on its origin, its push face
  up (glTF +Y, away from the base socket on the face it is placed on). As the reference sheet shows it: a slim dark
  gunmetal base plate (24.4 cm square, 2.2 cm thick) with rounded corners, a thin teal accent line round its sides and
  four corner holes with copper screws; on it a low gunmetal bezel and a copper contact ring round the well the button
  sits in; and the large round teal (anodized) push face, raised 4 cm above the plate, with a fine groove round its
  top. Under the plate, two copper contact pads are where links plug in. Nodes: ``Housing`` (fixed), ``Face`` (the push
  face, the only part that moves) and its child ``Glow`` (a teal ring lit in the groove while the button is down).
  Collision stays the part's full block in both states (the part data has no collision of its own).
* Two glTF clips, played by the game from the button script's "clip" display (part data ``display.clips``), each
  holding its last frame, so they are the button's two poses:

  - ``press`` (0.08 s, eased in and out): the face goes down 2.2 cm, to 3 mm above the copper ring, and the ring in
    its groove lights.
  - ``release`` (0.08 s, eased): back up to the raised rest pose, the ring going dark.

  The rest pose in the file (and before any clip has played) is the raised one.
* ``mods/vanilla/textures/items/button_icon.png`` - the hotbar, inventory, creation hotbar and Fabricator picture,
  256 x 256 on a clear background, three-quarter from above as on the reference sheet.

Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/button/build_button.py                  # writes the .glb and the icon
    python tools/button/build_button.py --render OUT_DIR # also renders the review sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only), so running it twice gives the same file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Euler, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
for sub in ("", "../lift", "../bearing_engine"):
    sys.path.insert(0, str((HERE / sub).resolve()))
import build_bearing_engine as be  # noqa: E402  (lathe, disc, bolt holes, the icon renderer)
import build_lift as bl  # noqa: E402  (Geo, primitives, objects, NLA tracks)
import lift_textures as lt  # noqa: E402

REPO = HERE.parents[1]
MESH = REPO / "mods" / "vanilla" / "meshes" / "parts" / "button.glb"
ICON = REPO / "mods" / "vanilla" / "textures" / "items" / "button_icon.png"
FPS = 100                     # 0.08 s clips get 8 frames

Geo, cylinder, dome, quad = bl.Geo, bl.cylinder, bl.dome, bl.quad
lathe, disc = be.lathe, be.disc

# ---- Dimensions (metres; the block is -0.125 to 0.125, the face it is placed on is Z = -0.125) ------------------
BOTTOM = -0.125
PLATE_HALF = 0.122            # 24.4 cm square: a hair inside the block, so neighbours' edges show a seam
PLATE_T = 0.022               # base plate thickness
PLATE_R = 0.024               # corner radius
PLATE_BEVEL = 0.004           # rounded top edge
TOP = BOTTOM + PLATE_T        # -0.103: the plate's face
HOLES = 0.092                 # corner holes at (+-HOLES, +-HOLES)
BEZEL_IN, BEZEL_OUT, BEZEL_H = 0.079, 0.093, 0.006    # the low gunmetal bezel on the plate
RING_IN, RING_OUT, RING_H = 0.0705, 0.0790, 0.012     # the copper contact ring
WELL_R = RING_IN              # the dark well the face goes down into
WELL_FLOOR = BOTTOM + 0.0015
FACE_R = 0.064                # the push face
FACE_BOTTOM = TOP + 0.003     # its skirt's lower edge, raised
FACE_TOP = TOP + 0.040        # its top, raised (4 cm proud of the plate)
FACE_EDGE = 0.007             # rounded top edge
TRAVEL = 0.022                # pressed: 3 mm above the copper ring, the skirt still inside the plate
GROOVE_IN, GROOVE_OUT, GROOVE_D = 0.039, 0.045, 0.0016
GLOW_DOWN = 0.0007            # the lit ring sits this far under the face's top when pressed ...
GLOW_HIDDEN = 0.0028          # ... and this far (under the groove's floor) when not
PRESS_SECONDS = 0.08


# =====================================================================================================
# Shapes
# =====================================================================================================

def rounded_rect(half, radius, inset=0.0, per_corner=6):
    """The outline of a rounded square, counter-clockwise from +X, `inset` in from its edge."""
    h, r = half - inset, max(radius - inset, 0.001)
    points = []
    for cx, cy, a0 in ((h - r, h - r, 0.0), (-h + r, h - r, 90.0), (-h + r, -h + r, 180.0), (h - r, -h + r, 270.0)):
        for k in range(per_corner + 1):
            a = math.radians(a0 + 90.0 * k / per_corner)
            points.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return points


def loft(g, rings, mat, cap_top=True, cap_bottom=True):
    """Joins outlines (lists of (x, y) at a z, all with the same point count) bottom to top, capping the ends."""
    n = len(rings[0][1])
    verts, faces = [], []
    for z, outline in rings:
        verts += [(x, y, z) for x, y in outline]
    for i in range(len(rings) - 1):
        for k in range(n):
            k1 = (k + 1) % n
            faces.append((i * n + k, i * n + k1, (i + 1) * n + k1, (i + 1) * n + k))
    if cap_bottom:
        faces.append(tuple(reversed(range(n))))
    if cap_top:
        faces.append(tuple((len(rings) - 1) * n + k for k in range(n)))
    return g.add(verts, faces, mat)


def screw(g, x, y):
    """A copper pan-head screw sunk in a countersunk corner hole, its slot turned a little off square."""
    lathe(g, [(0.0, TOP - 0.004), (0.0105, TOP - 0.004), (0.0135, TOP + 0.0002), (0.0, TOP + 0.0002)], "hole", steps=24,
          center=(x, y))
    lathe(g, [(0.0, TOP - 0.004), (0.0088, TOP - 0.004), (0.0088, TOP - 0.0012), (0.0074, TOP + 0.0012),
              (0.004, TOP + 0.0022), (0.0, TOP + 0.0024)], "copper", steps=24, center=(x, y))
    a = math.atan2(y, x) + math.radians(20)
    c, s = math.cos(a), math.sin(a)
    d = (c * 0.0068, s * 0.0068)
    w = (-s * 0.0009, c * 0.0009)
    z = TOP + 0.0025
    quad(g, [(x - d[0] - w[0], y - d[1] - w[1], z), (x + d[0] - w[0], y + d[1] - w[1], z),
             (x + d[0] + w[0], y + d[1] + w[1], z), (x - d[0] + w[0], y - d[1] + w[1], z)], "hole")


def build_housing():
    g = Geo()
    # the base plate: straight sides, a rounded top edge, a flat back
    rings = [(BOTTOM, rounded_rect(PLATE_HALF, PLATE_R)), (TOP - PLATE_BEVEL, rounded_rect(PLATE_HALF, PLATE_R))]
    for k in range(1, 4):
        a = math.radians(90 * k / 3)
        rings.append((TOP - PLATE_BEVEL + PLATE_BEVEL * math.sin(a), rounded_rect(PLATE_HALF, PLATE_R, PLATE_BEVEL * (1 - math.cos(a)))))
    loft(g, rings, "gunmetal")
    # the teal accent line round the sides, a hair proud of them
    z = BOTTOM + PLATE_T * 0.42
    loft(g, [(z - 0.0014, rounded_rect(PLATE_HALF + 0.0004, PLATE_R)), (z + 0.0014, rounded_rect(PLATE_HALF + 0.0004, PLATE_R))],
         "teal", cap_top=False, cap_bottom=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            screw(g, sx * HOLES, sy * HOLES)
    # the bezel and the copper contact ring round the well, rounded on top
    lathe(g, [(BEZEL_IN, TOP - 0.001), (BEZEL_OUT + 0.003, TOP - 0.001), (BEZEL_OUT, TOP + BEZEL_H * 0.6),
              (BEZEL_OUT - 0.002, TOP + BEZEL_H), (BEZEL_IN, TOP + BEZEL_H)], "gunmetal", steps=64)
    top = TOP + RING_H
    lathe(g, [(RING_IN, TOP - 0.004), (RING_OUT, TOP - 0.004), (RING_OUT, top - 0.0025), (RING_OUT - 0.001, top - 0.0008),
              (RING_OUT - 0.0028, top), (RING_IN + 0.0028, top), (RING_IN + 0.001, top - 0.0008), (RING_IN, top - 0.0025)],
          "copper", steps=64)
    # the well: a dark tube down to a floor just inside the back face
    cylinder(g, (0, 0, WELL_FLOOR), (0, 0, TOP - 0.004), WELL_R, "hole", segs=64, cap1=False)
    # the back: two copper contact pads either side of a small dark terminal block
    for sx in (-1, 1):
        lathe(g, [(0.0, BOTTOM - 0.0012), (0.011, BOTTOM - 0.0012), (0.012, BOTTOM), (0.0, BOTTOM)], "copper", steps=20,
              center=(sx * 0.03, 0.0))
    bl.box(g, (0, 0, BOTTOM - 0.0008), (0.012, 0.006, 0.0008), "hole")
    return g


def build_face():
    """The push face, raised. Its skirt reaches down into the well, so pressing it never opens a gap."""
    g = Geo()
    profile = [(0.0, FACE_BOTTOM), (FACE_R, FACE_BOTTOM)]
    for k in range(0, 7):
        a = math.radians(90 * k / 6)
        profile.append((FACE_R - FACE_EDGE * (1 - math.cos(a)), FACE_TOP - FACE_EDGE * (1 - math.sin(a))))
    # a very slight crown, and the groove round the top
    top = FACE_TOP
    profile += [(GROOVE_OUT, top), (GROOVE_OUT, top - GROOVE_D), (GROOVE_IN, top - GROOVE_D), (GROOVE_IN, top),
                (0.0, top + 0.0012)]
    lathe(g, profile, "teal", steps=72)
    # the groove's floor is a darker teal, so the lit ring reads against it
    lathe(g, [(GROOVE_IN + 0.0002, top - GROOVE_D + 0.0002), (GROOVE_OUT - 0.0002, top - GROOVE_D + 0.0002),
              (GROOVE_OUT - 0.0002, top - GROOVE_D + 0.0004), (GROOVE_IN + 0.0002, top - GROOVE_D + 0.0004)], "teal_dark", steps=72)
    return g


def build_glow():
    """The lit ring, at the Glow node's origin (the face's top); the clips raise it into the groove."""
    g = Geo()
    lathe(g, [(GROOVE_IN + 0.0004, -0.0003), (GROOVE_OUT - 0.0004, -0.0003), (GROOVE_OUT - 0.0004, 0.0003),
              (GROOVE_IN + 0.0004, 0.0003)], "glow", steps=72)
    return g


def make_materials():
    plain = be.pt.plain
    return {
        # Button Design Spec: satin gunmetal body, anodized teal face, polished copper accents
        "gunmetal": plain("button_gunmetal", "#4e5256", 0.46, 0.8),
        "teal": plain("button_teal", "#2b8a92", 0.32, 0.35),
        "teal_dark": plain("button_teal_dark", "#174a50", 0.5, 0.3),
        "copper": plain("button_copper", "#c27a4a", 0.28, 1.0),
        "hole": plain("button_hole", "#0b0c0d", 0.9, 0.2),
        # The lit ring is always lit in the file; the clips hide it under the groove's floor when the button is up.
        "glow": plain("button_glow", "#3fdcea", 0.3, 0.0, emission="#3fdcea", strength=2.0),
    }


# =====================================================================================================
# Scene and clips
# =====================================================================================================

def build_scene(mats):
    rig = {}
    root = rig["root"] = bl.make_empty("Button")
    bl.make_object("Housing", build_housing(), mats, root)
    rig["face"] = bl.make_object("Face", build_face(), mats, root)
    rig["glow"] = bl.make_object("Glow", build_glow(), mats, rig["face"], (0, 0, FACE_TOP - GLOW_HIDDEN))
    pose(rig, 0.0)
    return rig


def pose(rig, down):
    """`down` 0 is raised (released), 1 pressed. The face goes down; the ring rises into the groove in the last half."""
    rig["face"].location = (0, 0, -TRAVEL * down)
    lit = min(max(down * 2 - 1, 0.0), 1.0)
    rig["glow"].location = (0, 0, FACE_TOP - GLOW_HIDDEN + (GLOW_HIDDEN - GLOW_DOWN) * lit)


def ease(t):
    return t * t * (3 - 2 * t)


def bake(rig):
    for key in ("face", "glow"):
        rig[key].animation_data_create()
    frames = int(round(PRESS_SECONDS * FPS))
    for name, start, end in (("press", 0.0, 1.0), ("release", 1.0, 0.0)):
        actions = {}
        for key in ("face", "glow"):
            actions[key] = bpy.data.actions.new(f"{name}_{rig[key].name}")
            rig[key].animation_data.action = actions[key]
        for f in range(frames + 1):
            pose(rig, start + (end - start) * ease(f / frames))
            for key in ("face", "glow"):
                rig[key].keyframe_insert("location", frame=f)
        for key in ("face", "glow"):
            lt.linear_keys(actions[key])
            bl.push_track(rig[key], actions[key], name)
    for key in ("face", "glow"):
        rig[key].animation_data.use_nla = False
    pose(rig, 0.0)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = 20


def main():
    reset_scene()
    mats = make_materials()
    rig = build_scene(mats)
    bake(rig)
    be.export(rig["root"], MESH, animations=True)
    # the icon: three-quarter from above, raised, as on the reference sheet's isometric view
    for ob in be.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None
    pose(rig, 0.0)
    be.render_icon(be.mesh_tree(rig["root"]), ICON, view=(1.0, -1.25, 1.15), ortho=0.36, target=(0, 0, -0.095))
    for p in (MESH, ICON):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_button
        render_button.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
