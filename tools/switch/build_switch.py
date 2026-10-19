"""Build the Switch: a slim surface-mounted rocker toggle with a status light (Switch Design Spec 1.1 of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/switch.glb`` - the Switch in its 1 x 1 x 1 cell (0.25 m cube centred on the origin),
  mounted on the cell's bottom face (Blender -Z, the game's -Y, where its base socket is). A thin gunmetal base plate
  with copper screws in its corners carries a raised housing; a teal ring frames the rocker's recess, two copper
  brackets hold the rocker's pivot pin, and a copper bezel to the right of the rocker holds the status light.
  The moving pieces are their own nodes: the ``Rocker`` (its +X end, the viewer's right, down when off and up when
  on), the ``LampRed`` and ``LampTeal`` lenses in the bezel and the ``RecessGlow`` under the rocker's raised end.
  glTF clips (the game plays them from the switch's ``pose`` readout; see docs/part-data-schema.md):

  - ``off`` (0.12 s): the rocker snaps to its off tilt (the viewer's right end down), the light turns red.
  - ``on`` (0.12 s): the rocker snaps to its on tilt (the right end up), the light turns teal and the recess glows.

  Both clips end on their pose and hold it, so a switch loaded on shows on.

* ``mods/vanilla/textures/items/switch_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the front right, switched on.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/switch/build_switch.py                  # writes the .glb and the icon
    python tools/switch/build_switch.py --render OUT_DIR # also renders review shots into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Euler

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_engine"))
import build_gas_engine as ge  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = ge.bbe, ge.bl, ge.lt, ge.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = bbe.FPS

Geo, box, cylinder, ring, hexnut = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.hexnut
glow_box, glow_disc = ge.glow_box, ge.glow_disc
OFF = ge.OFF

# ---- Switch (metres; 0.25 m cell centred on the origin, mounted on z = -0.125, front -Y, the viewer's right +X) ----
FLOOR = -0.125
PLATE_HALF = (0.104, 0.078, 0.0095)              # the base plate
PLATE_TOP = FLOOR + 2 * PLATE_HALF[2]
HOUSING_HALF = (0.066, 0.05, 0.007)              # the raised housing the rocker sits in (centred left of the light)
HOUSING_X = -0.012
HOUSING_TOP = PLATE_TOP + 2 * HOUSING_HALF[2]
RECESS_HALF = (0.047, 0.035)                     # the rocker's opening, framed by the teal ring
PIVOT = (HOUSING_X, 0.0, HOUSING_TOP + 0.004)    # the rocker turns about Y through here
ROCKER_HALF = (0.044, 0.031, 0.0125)             # the rocker's gunmetal cap
SKIRT_HALF = (0.041, 0.029, 0.006)               # its teal skirt, mostly inside the recess
TILT = math.radians(14.0)                        # off: rotation +TILT about Y (right end down); on: -TILT
LAMP_AT = (0.07, 0.0)                            # the status light, right of the housing
LAMP_R = 0.0085
SCREWS = [(sx * 0.084, sy * 0.058) for sx in (-1, 1) for sy in (-1, 1)]
SNAP = 0.12                                      # seconds the rocker takes to snap over


# =====================================================================================================
# Materials
# =====================================================================================================

def make_materials():
    return {
        "hole": pt.plain("sw_hole", "#0c0d0f", 0.9, 0.2),
        "lens": pt.plain("sw_lens", "#1a1c1f", 0.15, 0.0),
        "teal": pt.plain("sw_teal", "#24807e", 0.38, 0.5),
        "teal_glow": pt.plain("sw_teal_glow", "#14b8ac", 0.3, 0.0, emission="#10c8b8", strength=1.2),
        "red_glow": pt.plain("sw_red_glow", "#d0241c", 0.3, 0.0, emission="#ff1a10", strength=1.2),
        "gun": pt.plain("sw_gun", "#4b4e53", 0.42, 0.65),
        "gun_dark": pt.plain("sw_gun_dark", "#36383c", 0.5, 0.6),
        "copper": pt.plain("sw_copper", "#c47645", 0.3, 0.95),
    }


# =====================================================================================================
# Geometry
# =====================================================================================================

def screw(g, x, y, z):
    """A copper button-head screw with a dark hex socket."""
    cylinder(g, (x, y, z - 0.001), (x, y, z + 0.0022), 0.0082, "copper", segs=20, r1=0.0068)
    hexnut(g, (x, y, z + 0.0016), (0, 0, 1), 0.0034, 0.0008, "hole")


def build_body():
    g = Geo()
    box(g, (0, 0, FLOOR + PLATE_HALF[2]), PLATE_HALF, "gun_dark", bevel=0.007)
    for x, y in SCREWS:
        screw(g, x, y, PLATE_TOP)
    hx, hy, hz = HOUSING_HALF
    box(g, (HOUSING_X, 0, PLATE_TOP + hz), (hx, hy, hz), "gun", bevel=0.004)
    # the recess: a dark well, framed by the teal ring standing proud of the housing
    rx, ry = RECESS_HALF
    box(g, (HOUSING_X, 0, HOUSING_TOP - 0.004), (rx, ry, 0.0045), "hole")
    t = 0.0042
    for sy in (-1, 1):
        box(g, (HOUSING_X, sy * (ry + t), HOUSING_TOP + 0.0012), (rx + 2 * t, t, 0.0016), "teal", bevel=0.001)
    for sx in (-1, 1):
        box(g, (HOUSING_X + sx * (rx + t), 0, HOUSING_TOP + 0.0012), (t, ry, 0.0016), "teal", bevel=0.001)
    # copper brackets front and back carrying the pivot pin
    px, _, pz = PIVOT
    for sy in (-1, 1):
        y = sy * (ry + 2 * t + 0.0055)
        box(g, (px, y, HOUSING_TOP + 0.0075), (0.0085, 0.0045, 0.0085), "copper", bevel=0.002)
        cylinder(g, (px, y + sy * 0.0045, pz), (px, y + sy * 0.0062, pz), 0.0042, "gun_dark", segs=14)
    # the status light's bezel: a copper ring around a dark lens, on a low boss
    lx, ly = LAMP_AT
    cylinder(g, (lx, ly, PLATE_TOP - 0.001), (lx, ly, PLATE_TOP + 0.0035), 0.0165, "gun", segs=28)
    ring(g, PLATE_TOP + 0.0035, PLATE_TOP + 0.0068, LAMP_R + 0.0005, 0.0138, "copper", segs=28, center=LAMP_AT)
    cylinder(g, (lx, ly, PLATE_TOP + 0.003), (lx, ly, PLATE_TOP + 0.0045), LAMP_R + 0.0006, "lens", segs=24)
    return g


def build_rocker():
    """The rocker about its pivot: a teal skirt and a gunmetal cap, its on (+X) half topped with a darker pad."""
    g = Geo()
    _, _, pz = PIVOT
    base = HOUSING_TOP - 0.006 - pz                # skirt bottom, relative to the pivot
    sx, sy, sz = SKIRT_HALF
    box(g, (0, 0, base + sz), SKIRT_HALF, "teal", bevel=0.002)
    cx, cy, cz = ROCKER_HALF
    cap = base + 2 * sz + cz - 0.002
    box(g, (0, 0, cap), (cx, cy, cz), "gun", bevel=0.006)
    top = cap + cz
    box(g, (cx * 0.47, 0, top + 0.0004), (cx * 0.43, cy * 0.84, 0.0012), "gun_dark", bevel=0.001)
    # the pivot pin through the skirt
    cylinder(g, (0, -sy - 0.006, 0), (0, sy + 0.006, 0), 0.0028, "copper", segs=12)
    return g


# =====================================================================================================
# Scene and clips
# =====================================================================================================

def build_switch(mats, offset=(0, 0, 0)):
    rig = {}
    root = rig["root"] = bl.make_empty("Switch")
    root.location = offset
    bl.make_object("Body", build_body(), mats, root)
    rig["rocker"] = bl.make_object("Rocker", build_rocker(), mats, root, PIVOT)
    lx, ly = LAMP_AT
    z = PLATE_TOP + 0.0047
    rig["red"] = bl.make_object("LampRed", glow_disc(LAMP_R, (0, 0, 1), "red_glow", depth=0.0012), mats, root, (lx, ly, z))
    rig["teal"] = bl.make_object("LampTeal", glow_disc(LAMP_R, (0, 0, 1), "teal_glow", depth=0.0012), mats, root, (lx, ly, z + 0.0002))
    # under the rocker's raised (+X) end when on: the recess lights up teal
    rx, ry = RECESS_HALF
    rig["glow"] = bl.make_object("RecessGlow", glow_box((0.012, ry - 0.002, 0.0005), "teal_glow"), mats, root,
                                 (HOUSING_X + rx - 0.013, 0, HOUSING_TOP - 0.0075))
    for ob in (rig["rocker"], rig["red"], rig["teal"], rig["glow"]):
        ob.rotation_mode = "XYZ"
    apply_pose(rig, 0.0)
    return rig


def apply_pose(rig, on):
    """on: 0 (off) to 1 (on). The rocker turns through it; the light swaps at the middle of the snap."""
    rig["rocker"].rotation_euler = Euler((0.0, TILT * (1 - 2 * on), 0.0))
    lit = on >= 0.5
    rig["red"].scale = (OFF,) * 3 if lit else (1.0,) * 3
    rig["teal"].scale = (1.0,) * 3 if lit else (OFF,) * 3
    rig["glow"].scale = (1.0, 1.0, 1.0) if lit else (OFF, OFF, OFF)


def clip_objects(rig):
    return [rig["rocker"], rig["red"], rig["teal"], rig["glow"]]


def snap(t):
    """The rocker's travel over the snap: a quick ease-in that lands hard."""
    u = min(1.0, max(0.0, t / SNAP))
    return u * u * (3 - 2 * u) if u < 1 else 1.0


def bake(rig):
    objs = clip_objects(rig)
    for ob in objs:
        ob.animation_data_create()
    frames = max(2, int(round(SNAP * FPS)))
    for name, start, end in (("off", 1.0, 0.0), ("on", 0.0, 1.0)):
        acts = {}
        for ob in objs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        for f in range(frames + 1):
            apply_pose(rig, start + (end - start) * snap(f / FPS))
            for ob in objs:
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(acts[ob.name])
            bl.push_track(ob, acts[ob.name], name)
    for ob in objs:
        ob.animation_data.use_nla = False
    apply_pose(rig, 0.0)


def strip_animation(rig):
    for ob in bbe.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None


ICON_VIEW = (1.3, -1.7, 1.55)


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_switch(mats)
    bake(rig)
    bbe.export(rig["root"], MESHES / "switch.glb", animations=True)
    bpy.context.scene.frame_set(0)
    strip_animation(rig)
    apply_pose(rig, 1.0)
    bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / "switch_icon.png", view=ICON_VIEW, ortho=0.225,
                    target=(0.0, 0.0, FLOOR + 0.02))
    for p in (MESHES / "switch.glb", ICONS / "switch_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_switch
        render_switch.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
