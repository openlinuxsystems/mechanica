"""Build the Beacon's model, its off, on and flash clips and its icon (Parts batch 2, Salvager Tech family).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/beacon.glb`` - the Beacon, 1 x 2 x 1 blocks (0.25 x 0.5 x 0.25 m), centred on its origin,
  standing up from the base socket on the face it is placed on (glTF -Y). As a Salvager Tech part: a gunmetal base
  plate with four copper screws; on it a framed hull-dark case (corner posts, inset vented panels, a top cap) with a
  small screen on its front showing a waypoint chevron; on the case a copper collar, the smoked-glass lamp in a
  gunmetal cage, and a copper whip antenna with a ball tip. Nodes: ``Housing`` (fixed), ``Band`` (a cyan ring round the
  lamp, lit while the beacon is on), ``Flash`` (a cyan shell over the whole lamp, lit while it flashes) and ``Pip``
  (the screen's chevron, lit while it is on). While dark, the lamp's lit nodes shrink into the middle of the lens and
  the chevron sits behind the screen.
* Three glTF clips, played by the game from the beacon script's ``pose`` readout (part data ``display.clips``), each
  holding its last frame:

  - ``off``: everything dark.
  - ``on``: the band and the screen's chevron lit (between flashes).
  - ``flash``: the whole lamp lit (each ping).

  The rest pose in the file (and before any clip has played) is dark.
* ``mods/vanilla/textures/items/beacon_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a clear
  background, three-quarter from the front right, lit.

Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/beacon/build_beacon.py                  # writes the .glb and the icon
    python tools/beacon/build_beacon.py --render OUT_DIR # also renders the review sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic,
so running it twice gives the same file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
for sub in ("", "../lift", "../bearing_engine", "../button"):
    sys.path.insert(0, str((HERE / sub).resolve()))
import build_bearing_engine as be  # noqa: E402  (lathe, the icon renderer, the exporter)
import build_button as bb  # noqa: E402  (rounded squares, lofts)
import build_lift as bl  # noqa: E402  (Geo, primitives, objects, NLA tracks)
import lift_textures as lt  # noqa: E402

REPO = HERE.parents[1]
MESH = REPO / "mods" / "vanilla" / "meshes" / "parts" / "beacon.glb"
ICON = REPO / "mods" / "vanilla" / "textures" / "items" / "beacon_icon.png"
FPS = 100
SNAP = 0.04                   # every clip is a 4-frame snap

Geo, cylinder, quad = bl.Geo, bl.cylinder, bl.quad
lathe = be.lathe

# ---- Dimensions (metres; the part is -0.125 to 0.125 across and -0.25 to 0.25 up; it stands on Z = -0.25) --------
BOTTOM = -0.25
PLATE_HALF = 0.112            # 22.4 cm square base plate
PLATE_T = 0.022
PLATE_R = 0.022
PLATE_TOP = BOTTOM + PLATE_T
HOLES = 0.087                 # corner screws at (+-HOLES, +-HOLES)
CASE_HALF = 0.074             # the framed case: 14.8 cm square ...
CASE_TOP = -0.03              # ... up to here
POST = 0.013                  # corner post half width
CAP_HALF, CAP_T = 0.084, 0.014
CAP_TOP = CASE_TOP + CAP_T
LENS_R = 0.041                # the smoked-glass lamp
LENS_BOTTOM = CAP_TOP + 0.016
LENS_TOP = LENS_BOTTOM + 0.1
LENS_MID = (LENS_BOTTOM + LENS_TOP) / 2
CAGE_R = 0.053                # cage bars on this radius
CAGE_TOP = LENS_TOP + 0.016
ANTENNA_TOP = 0.236
SCREEN_Z = -0.105             # the front screen's centre height
SCREEN_HALF = (0.036, 0.024)


# =====================================================================================================
# Shapes
# =====================================================================================================

def screw(g, x, y, z):
    """A copper pan-head screw in a dark countersink, its slot turned a little off square."""
    lathe(g, [(0.0, z - 0.004), (0.0095, z - 0.004), (0.0125, z + 0.0002), (0.0, z + 0.0002)], "hole", steps=24, center=(x, y))
    lathe(g, [(0.0, z - 0.004), (0.008, z - 0.004), (0.008, z - 0.0012), (0.0068, z + 0.0012), (0.0036, z + 0.0022),
              (0.0, z + 0.0024)], "copper", steps=24, center=(x, y))
    a = math.atan2(y, x) + math.radians(20)
    c, s = math.cos(a), math.sin(a)
    d, w = (c * 0.0062, s * 0.0062), (-s * 0.0009, c * 0.0009)
    zz = z + 0.0025
    quad(g, [(x - d[0] - w[0], y - d[1] - w[1], zz), (x + d[0] - w[0], y + d[1] - w[1], zz),
             (x + d[0] + w[0], y + d[1] + w[1], zz), (x - d[0] + w[0], y - d[1] + w[1], zz)], "hole")


def base_plate(g):
    rings = [(BOTTOM, bb.rounded_rect(PLATE_HALF, PLATE_R)), (PLATE_TOP - 0.004, bb.rounded_rect(PLATE_HALF, PLATE_R))]
    for k in range(1, 4):
        a = math.radians(90 * k / 3)
        rings.append((PLATE_TOP - 0.004 + 0.004 * math.sin(a), bb.rounded_rect(PLATE_HALF, PLATE_R, 0.004 * (1 - math.cos(a)))))
    bb.loft(g, rings, "gunmetal")
    # a cyan running line round the plate's sides
    z = BOTTOM + PLATE_T * 0.42
    bb.loft(g, [(z - 0.0014, bb.rounded_rect(PLATE_HALF + 0.0004, PLATE_R)), (z + 0.0014, bb.rounded_rect(PLATE_HALF + 0.0004, PLATE_R))],
            "cyan_line", cap_top=False, cap_bottom=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            screw(g, sx * HOLES, sy * HOLES, PLATE_TOP)


def side_panel(g, side):
    """One inset panel between the corner posts, with three vent slots (the front gets the screen instead)."""
    # side: 0 = -Y (front), 1 = +X, 2 = +Y, 3 = -X; local frame u (across, left to right seen from outside), n (out)
    n = [(0, -1), (1, 0), (0, 1), (-1, 0)][side]
    u = (-n[1], n[0]) if side in (0, 2) else (-n[1], n[0])
    inset = CASE_HALF - 0.004
    z0, z1 = PLATE_TOP + 0.012, CASE_TOP - 0.012
    half_u = CASE_HALF - 2 * POST + 0.002

    def p(a, z, out=0.0):
        return (n[0] * (inset + out) + u[0] * a, n[1] * (inset + out) + u[1] * a, z)

    # the panel: a thin plate a little proud of the inset face, its edges bevelled by a step
    quad(g, [p(-half_u, z0, 0.0015), p(half_u, z0, 0.0015), p(half_u, z1, 0.0015), p(-half_u, z1, 0.0015)], "panel")
    for a0, a1, za, zb in ((-half_u, half_u, z0, z0), (half_u, half_u, z0, z1), (half_u, -half_u, z1, z1), (-half_u, -half_u, z1, z0)):
        quad(g, [p(a0, za, 0.0), p(a1, zb, 0.0), p(a1, zb, 0.0015), p(a0, za, 0.0015)], "panel")
    if side == 0:
        # the screen: a dark glass window in a copper-screwed bezel
        sx, sz = SCREEN_HALF
        quad(g, [p(-sx - 0.006, SCREEN_Z - sz - 0.006, 0.002), p(sx + 0.006, SCREEN_Z - sz - 0.006, 0.002),
                 p(sx + 0.006, SCREEN_Z + sz + 0.006, 0.002), p(-sx - 0.006, SCREEN_Z + sz + 0.006, 0.002)], "hull")
        quad(g, [p(-sx, SCREEN_Z - sz, 0.0026), p(sx, SCREEN_Z - sz, 0.0026), p(sx, SCREEN_Z + sz, 0.0026),
                 p(-sx, SCREEN_Z + sz, 0.0026)], "screen")
        # a label strip under it
        quad(g, [p(-0.03, SCREEN_Z - sz - 0.022, 0.0022), p(0.03, SCREEN_Z - sz - 0.022, 0.0022),
                 p(0.03, SCREEN_Z - sz - 0.013, 0.0022), p(-0.03, SCREEN_Z - sz - 0.013, 0.0022)], "label")
        # vents above the screen
        slots = [(SCREEN_Z + sz + 0.02 + k * 0.011) for k in range(4)]
    else:
        slots = [(-0.17 + k * 0.016) for k in range(6)]
    for zc in slots:
        if zc + 0.003 > z1 - 0.006:
            continue
        quad(g, [p(-half_u + 0.012, zc - 0.0028, 0.0017), p(half_u - 0.012, zc - 0.0028, 0.0017),
                 p(half_u - 0.012, zc + 0.0028, 0.0017), p(-half_u + 0.012, zc + 0.0028, 0.0017)], "hole")


def case(g):
    # the core box, inset, in hull dark
    inner = CASE_HALF - 0.004
    bl.box(g, (0, 0, (PLATE_TOP + CASE_TOP) / 2), (inner, inner, (CASE_TOP - PLATE_TOP) / 2), "hull")
    # corner posts with small bevels
    for sx in (-1, 1):
        for sy in (-1, 1):
            bl.box(g, (sx * (CASE_HALF - POST), sy * (CASE_HALF - POST), (PLATE_TOP + CASE_TOP) / 2),
                   (POST, POST, (CASE_TOP - PLATE_TOP) / 2), "gunmetal", bevel=0.003)
    # a foot rail and a head rail round the case
    for zc in (PLATE_TOP + 0.006, CASE_TOP - 0.006):
        for axis in (0, 1):
            for s in (-1, 1):
                c = (s * (CASE_HALF - 0.004), 0, zc) if axis == 0 else (0, s * (CASE_HALF - 0.004), zc)
                half = (0.004, CASE_HALF - 2 * POST, 0.006) if axis == 0 else (CASE_HALF - 2 * POST, 0.004, 0.006)
                bl.box(g, c, half, "gunmetal", bevel=0.0015)
    for side in range(4):
        side_panel(g, side)
    # the top cap
    bl.box(g, (0, 0, CASE_TOP + CAP_T / 2), (CAP_HALF, CAP_HALF, CAP_T / 2), "gunmetal", bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            screw(g, sx * (CAP_HALF - 0.012), sy * (CAP_HALF - 0.012), CAP_TOP)


def lamp(g):
    # the copper collar the lamp sits in
    lathe(g, [(0.0, CAP_TOP - 0.001), (0.056, CAP_TOP - 0.001), (0.056, CAP_TOP + 0.006), (0.052, LENS_BOTTOM + 0.002),
              (0.0, LENS_BOTTOM + 0.002)], "copper", steps=64)
    # the smoked-glass lens, a rounded top
    profile = [(0.0, LENS_BOTTOM), (LENS_R, LENS_BOTTOM), (LENS_R, LENS_TOP - 0.01)]
    for k in range(1, 7):
        a = math.radians(90 * k / 6)
        profile.append((LENS_R - 0.01 * (1 - math.cos(a)), LENS_TOP - 0.01 + 0.01 * math.sin(a)))
    profile.append((0.0, LENS_TOP))
    lathe(g, profile, "lens", steps=64)
    # the cage: four bars, a middle hoop and a top cap
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, y = CAGE_R * math.cos(a), CAGE_R * math.sin(a)
        cylinder(g, (x, y, LENS_BOTTOM - 0.002), (x, y, CAGE_TOP), 0.0055, "gunmetal", segs=12)
    bl.ring(g, LENS_MID - 0.004, LENS_MID + 0.004, CAGE_R - 0.005, CAGE_R + 0.005, "gunmetal", segs=48)
    lathe(g, [(0.0, CAGE_TOP - 0.004), (CAGE_R + 0.008, CAGE_TOP - 0.004), (CAGE_R + 0.008, CAGE_TOP + 0.004),
              (CAGE_R + 0.003, CAGE_TOP + 0.009), (0.0, CAGE_TOP + 0.009)], "gunmetal", steps=48)
    # the whip antenna: a copper base nut, the rod and a ball tip
    top = CAGE_TOP + 0.009
    cylinder(g, (0, 0, top), (0, 0, top + 0.01), 0.011, "copper", segs=6, phase=math.pi / 6)
    cylinder(g, (0, 0, top + 0.01), (0, 0, ANTENNA_TOP - 0.008), 0.0035, "copper", segs=12)
    lathe(g, [(0.0, ANTENNA_TOP - 0.016)] + [(0.008 * math.sin(math.radians(a)), ANTENNA_TOP - 0.008 - 0.008 * math.cos(math.radians(a)))
                                          for a in range(30, 181, 30)], "copper", steps=20)


def build_housing():
    g = Geo()
    base_plate(g)
    case(g)
    lamp(g)
    return g


def build_band():
    """The lit band round the lamp's middle, at the Band node's origin (the lens's centre)."""
    g = Geo()
    lathe(g, [(LENS_R + 0.0004, -0.012), (LENS_R + 0.0014, -0.012), (LENS_R + 0.0014, 0.012), (LENS_R + 0.0004, 0.012)],
          "glow", steps=64)
    return g


def build_flash():
    """The lit shell over the whole lens (and the band), at the Flash node's origin (the lens's centre)."""
    g = Geo()
    r = LENS_R + 0.0024
    b, t = LENS_BOTTOM - LENS_MID + 0.001, LENS_TOP - LENS_MID + 0.0016
    profile = [(r, b), (r, t - 0.011)]
    for k in range(1, 7):
        a = math.radians(90 * k / 6)
        profile.append((r - 0.011 * (1 - math.cos(a)), t - 0.011 + 0.011 * math.sin(a)))
    profile.append((0.0, t))
    lathe(g, profile, "flash", steps=64)
    return g


def build_pip():
    """The screen's waypoint chevron (a downward arrow over a dot), at the Pip node's origin (the screen's centre)."""
    g = Geo()
    y = -0.0008
    pts = [(-0.016, 0.012), (-0.008, 0.012), (0.0, 0.002), (0.008, 0.012), (0.016, 0.012), (0.0, -0.008)]
    quad(g, [(pts[0][0], y, pts[0][1]), (pts[5][0], y, pts[5][1]), (pts[2][0], y, pts[2][1]), (pts[1][0], y, pts[1][1])], "glow")
    quad(g, [(pts[2][0], y, pts[2][1]), (pts[5][0], y, pts[5][1]), (pts[4][0], y, pts[4][1]), (pts[3][0], y, pts[3][1])], "glow")
    quad(g, [(-0.004, y, -0.017), (0.004, y, -0.017), (0.004, y, -0.012), (-0.004, y, -0.012)], "glow")
    return g


def make_materials():
    plain = be.pt.plain
    return {
        # Salvager Tech palette (docs/art-direction.md): hull, gunmetal, copper, cyan only where powered
        "hull": plain("beacon_hull", "#24282e", 0.55, 0.6),
        "gunmetal": plain("beacon_gunmetal", "#5a6068", 0.42, 0.85),
        "panel": plain("beacon_panel", "#4a5058", 0.5, 0.75),
        "copper": plain("beacon_copper", "#b8733a", 0.3, 1.0),
        "hole": plain("beacon_hole", "#0b0c0d", 0.9, 0.2),
        "label": plain("beacon_label", "#c9c4b4", 0.7, 0.0),
        "screen": plain("beacon_screen", "#0d1b24", 0.18, 0.1),
        "lens": plain("beacon_lens", "#1f3440", 0.16, 0.15),
        "cyan_line": plain("beacon_cyan_line", "#38c8ff", 0.35, 0.2),
        # The lit parts are always lit in the file; the clips hide them by scaling them into the lamp and screen.
        "glow": plain("beacon_glow", "#38c8ff", 0.3, 0.0, emission="#38c8ff", strength=3.0),
        "flash": plain("beacon_flash", "#bfeeff", 0.25, 0.0, emission="#5fd8ff", strength=8.0),
    }


# =====================================================================================================
# Scene and clips
# =====================================================================================================

HIDDEN = 0.01                 # a dark lamp node's scale: shrunk into the middle of the lens
PIP_IN = 0.012                # a dark chevron sits this far behind the screen

# What each clip shows: node -> lit.
POSES = {
    "off": {"band": False, "flash": False, "pip": False},
    "on": {"band": True, "flash": False, "pip": True},
    "flash": {"band": True, "flash": True, "pip": True},
}
LIT_NODES = ("band", "flash", "pip")
PIP_AT = (0, -(CASE_HALF - 0.004 + 0.0026), SCREEN_Z)


def build_scene(mats):
    rig = {}
    root = rig["root"] = bl.make_empty("Beacon")
    rig["housing"] = bl.make_object("Housing", build_housing(), mats, root)
    rig["band"] = bl.make_object("Band", build_band(), mats, root, (0, 0, LENS_MID))
    rig["flash"] = bl.make_object("Flash", build_flash(), mats, root, (0, 0, LENS_MID))
    rig["pip"] = bl.make_object("Pip", build_pip(), mats, root, PIP_AT)
    pose(rig, "off")
    return rig


def place(rig, key, lit):
    """`lit` 0 is dark, 1 lit: the lamp's nodes grow out of the lens's middle, the chevron comes up to the screen."""
    if key == "pip":
        rig["pip"].location = (PIP_AT[0], PIP_AT[1] + PIP_IN * (1 - lit), PIP_AT[2])
    else:
        s = HIDDEN + (1 - HIDDEN) * lit
        rig[key].scale = (s, s, s)


def pose(rig, name):
    for key in LIT_NODES:
        place(rig, key, 1.0 if POSES[name][key] else 0.0)


def bake(rig):
    for key in LIT_NODES:
        rig[key].animation_data_create()
    frames = int(round(SNAP * FPS))
    for name, shown in POSES.items():
        actions = {}
        for key in LIT_NODES:
            actions[key] = bpy.data.actions.new(f"{name}_{rig[key].name}")
            rig[key].animation_data.action = actions[key]
        for f in range(frames + 1):
            t = f / frames
            for key in LIT_NODES:
                # Every track must change, or the exporter drops it and the clip would leave that node as the last clip
                # left it: a lit node comes the last part of the way out (the flash swells over the lens), a dark one
                # goes the last part of the way in, from where it is already hidden inside the lens or the case.
                if shown[key]:
                    lit = 0.6 + 0.4 * t * t * (3 - 2 * t) if key == "flash" else 0.9 + 0.1 * t
                else:
                    lit = 0.25 * (1 - t)
                place(rig, key, lit)
                rig[key].keyframe_insert("location" if key == "pip" else "scale", frame=f)
        for key in LIT_NODES:
            lt.linear_keys(actions[key])
            bl.push_track(rig[key], actions[key], name)
    for key in LIT_NODES:
        rig[key].animation_data.use_nla = False
    pose(rig, "off")


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = 10


def main():
    reset_scene()
    mats = make_materials()
    rig = build_scene(mats)
    bake(rig)
    be.export(rig["root"], MESH, animations=True)
    # the icon: three-quarter from the front right, lit between flashes
    for ob in be.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data.use_nla = False
    pose(rig, "on")
    be.render_icon(be.mesh_tree(rig["root"]), ICON, view=(0.75, -1.25, 0.7), ortho=0.55, target=(0, 0, -0.008))
    pose(rig, "off")
    for p in (MESH, ICON):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_beacon
        render_beacon.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
