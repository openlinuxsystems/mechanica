"""Build the SV-04's Master Battery: the cell (an item), where it lies after the crash (a world object) and its bay in
the ship's engineering room (a POI model), with the bay's install clips.

The Getting Started quest (mods/vanilla/quests/getting_started.json): the crash threw the cell out of the ship; the
player finds it lying in the grass near the wreck, carries it back and slides it into the bay, which clamps it, shuts
its hatch and powers up.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/master_battery.glb`` - the cell, a 2 x 2 x 3 block item (0.5 x 0.5 x 0.75 m centred on
  the origin, Z up, its connector end -Y): a framed Salvager Tech case (gunmetal rails, dark panels, finned sides), a
  lit cyan charge window and a folding carry handle on top, a mustard pull handle and hazard band on the front, and a
  copper two-socket connector on the back.
* ``mods/vanilla/textures/items/master_battery_icon.png`` - its hotbar, inventory and quest picture, 256 x 256.
* ``mods/vanilla/meshes/world/master_battery_fallen.glb`` - the cell where it came down outside the ship (the
  ``master_battery_fallen`` world object): tipped into the furrow it ploughed, earth heaped in front, its torn
  connector cable sparking. Clip ``idle`` (2 s, looped): the spark flickers and the charge window pulses.
* ``mods/vanilla/meshes/structures/sv04_battery_bay.glb`` - the Power Core in engineering, with the slot the cell goes
  in, and a ``collider`` box the game collides with. It shares the ship's origin like ``sv04_fore.glb`` (the crash site places both at the same point) and follows the
  fore section's tilt. Its moving pieces are their own nodes: ``Cell``, ``ClampL`` and ``ClampR``, ``Hatch``,
  ``Contacts``, ``Warn``, ``Bar0``..``Bar4``, ``CoilGlow0`` and ``CoilGlow1`` and ``Cable``. glTF clips (see
  docs/world-generation.md, POI ``models[].story``):

  - ``empty`` (2 s, looped): no cell, the impact-sprung hatch hanging open, the clamps swung wide, the torn feed cable
    swaying and a red NO CELL bar blinking on the screen.
  - ``install`` (3.6 s): the cell, pushed in at the mouth, slides home onto the contacts, the clamps swing shut on it,
    the hatch swings up and latches, the contacts light, then the screen boots bar by bar and the coils light.
  - ``powered`` (2.4 s, looped): the cell home and the hatch shut, the screen bars and coils breathing.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/master_battery/build_master_battery.py                  # writes the three .glb files and the icon
    python tools/master_battery/build_master_battery.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Euler, Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = bbe.FPS
OFF = 0.001                                         # the scale a hidden piece shrinks to

Geo, box, cylinder, dome, hexnut, sweep, bezier = bl.Geo, bl.box, bl.cylinder, bl.dome, bl.hexnut, bl.sweep, bl.bezier

# ---- the cell (metres, its centre at the origin, Z up, connector end -Y) ------------------------------------------
CX, CY = 0.235, 0.34          # case half width and half length
Z0, Z1 = -0.24, 0.17          # case bottom and top
RAIL = 0.035                  # frame rail size

# ---- the bay (metres, in the fore section's frame: x across, +y towards the bow, z up from the deck) -------------
BAY_AT = (0.0, -2.5, 0.3)     # the Power Core's foot on the engineering deck (ship.py: where the old box stood)
HX, HY, HZ = 0.9, 0.7, 2.4    # housing half width, half depth, height (front face +Y)
SLOT_X, SLOT_Z0, SLOT_Z1 = 0.31, 0.38, 0.9   # slot half width, floor and roof
BACK = -0.2                   # the slot's back wall (the contacts)
CELL_Z = SLOT_Z0 + 0.02 - Z0  # cell origin height when it sits on the rails
HOME = BACK + CY + 0.01       # cell origin y when home on the contacts
MOUTH = HOME + 0.55           # cell origin y when the install begins (pushed in at the mouth)
HINGE_X, HINGE_Y, HINGE_Z = 0.36, HY + 0.03, 0.64  # clamp hinges (mirrored for the left one)
HATCH_Y = HY + 0.08                                # the hatch's hinge line, in front of the clamps
CLAMP_OPEN = math.radians(105)
HATCH_OPEN = math.radians(-122)                    # sprung open, hanging out and down
COILS = ((-0.5, -1.05), (0.5, -1.05))              # the coils behind the housing (ship.py), in bay coordinates
SCREEN_Z = 1.9

EMPTY, INSTALL, POWERED = 2.0, 3.6, 2.4


# =====================================================================================================
# Materials
# =====================================================================================================

def make_materials():
    m = gc.make_materials()
    stripe = lt.paint_surface(833, "#d9a92a", "#4a4d52")
    m.update({
        "mustard": lt.textured("mb_mustard", gc.half_res(stripe), normal_strength=0.4),
        "panel": lt.textured("mb_panel", gc.half_res(lt.metal_surface(835, "#2b2f35", 0.55, 0.45, wear=0.4, edge_light=0.2)), normal_strength=0.5),
        "dark": pt.plain("mb_dark", "#1b1d20", 0.6, 0.3),
        "black": pt.plain("mb_black", "#121315", 0.6, 0.1),
        "rubber": pt.plain("mb_rubber", "#191a1b", 0.85, 0.0),
        "screen": pt.plain("mb_screen", "#0b1215", 0.15, 0.2),
        "cyan": pt.plain("mb_cyan", "#38c8ff", 0.3, 0.0, emission="#38c8ff", strength=6.0),
        "cyan_soft": pt.plain("mb_cyan_soft", "#5fd6ff", 0.3, 0.0, emission="#38c8ff", strength=2.5),
        "cyan_dim": pt.plain("mb_cyan_dim", "#164c5e", 0.3, 0.0, emission="#1a8fb8", strength=0.8),
        "red": pt.plain("mb_red", "#c8423a", 0.3, 0.0, emission="#ff3a2e", strength=5.0),
        "spark": pt.plain("mb_spark", "#e9f8ff", 0.3, 0.0, emission="#bfefff", strength=12.0),
        "earth": pt.plain("mb_earth", "#4a3a2a", 0.95, 0.0),
        "earth_dark": pt.plain("mb_earth_dark", "#2f261d", 1.0, 0.0),
        "grass": pt.plain("mb_grass", "#5c8f34", 0.9, 0.0),
        "scorch": pt.plain("mb_scorch", "#1f1c19", 1.0, 0.0),
    })
    return m


def rails(g, c, half, mat="gun", r=RAIL):
    """The framed construction: bevelled square rails along a box's twelve edges and corner blocks."""
    cx, cy, cz = c
    hx, hy, hz = (h - r / 2 for h in half)
    for a in (-1, 1):
        for b in (-1, 1):
            box(g, (cx, cy + a * hy, cz + b * hz), (hx, r / 2, r / 2), mat, bevel=r * 0.18)
            box(g, (cx + a * hx, cy, cz + b * hz), (r / 2, hy, r / 2), mat, bevel=r * 0.18)
            box(g, (cx + a * hx, cy + b * hy, cz), (r / 2, r / 2, hz), mat, bevel=r * 0.18)
    for a in (-1, 1):
        for b in (-1, 1):
            for d in (-1, 1):
                box(g, (cx + a * hx, cy + b * hy, cz + d * hz), (r * 0.62,) * 3, mat, bevel=r * 0.22)


def stripes(g, c, half, axis, n, a="mustard", b="black"):
    """A hazard band of n alternating blocks along `axis` (0 x, 2 z) on a face."""
    lo = c[axis] - half[axis]
    step = 2 * half[axis] / n
    for k in range(n):
        cc = list(c)
        cc[axis] = lo + step * (k + 0.5)
        hh = list(half)
        hh[axis] = step / 2
        box(g, cc, hh, a if k % 2 == 0 else b)


# =====================================================================================================
# The cell
# =====================================================================================================

def build_cell():
    g = Geo()
    zc, hz = (Z0 + Z1) / 2, (Z1 - Z0) / 2
    box(g, (0, 0, zc), (CX - 0.012, CY - 0.012, hz - 0.012), "panel", bevel=0.006)
    rails(g, (0, 0, zc), (CX, CY, hz))
    # finned sides: heat fins between the rails
    for sx in (-1, 1):
        for k in range(7):
            y = -CY + 0.09 + k * (2 * CY - 0.18) / 6
            box(g, (sx * (CX + 0.004), y, zc), (0.012, 0.009, hz - 0.06), "gun_dark", bevel=0.003)
        # side grips the hands take it by
        box(g, (sx * (CX + 0.02), 0.12, zc + 0.03), (0.012, 0.07, 0.028), "rubber", bevel=0.008)
    # top: a charge window of five segments under glass, and a folding carry handle
    box(g, (0, -0.02, Z1 + 0.004), (0.13, 0.2, 0.008), "gun_dark", bevel=0.004)
    for k in range(5):
        box(g, (0, -0.17 + k * 0.075, Z1 + 0.011), (0.1, 0.03, 0.004), "cyan_soft")
    for sy in (-1, 1):
        box(g, (0, sy * 0.25, Z1 + 0.012), (0.17, 0.018, 0.012), "gun", bevel=0.004)
    for sx in (-1, 1):
        box(g, (sx * 0.19, 0.0, Z1 + 0.018), (0.016, 0.03, 0.02), "gun", bevel=0.005)
    pts = [Vector((-0.19, 0, Z1 + 0.02)), Vector((-0.19, 0, Z1 + 0.06)), Vector((-0.15, 0, Z1 + 0.07)),
           Vector((0.15, 0, Z1 + 0.07)), Vector((0.19, 0, Z1 + 0.06)), Vector((0.19, 0, Z1 + 0.02))]
    sweep(g, pts, 0.011, "steel", segs=10)
    cylinder(g, (-0.12, 0, Z1 + 0.07), (0.12, 0, Z1 + 0.07), 0.019, "mustard", segs=14)
    # front (+Y): hazard band, a recessed pull handle, a stencilled plate
    y = CY + 0.002
    stripes(g, (0, y, Z1 - 0.045), (CX - 0.03, 0.004, 0.022), 0, 8)
    for sx in (-1, 1):
        box(g, (sx * 0.12, y + 0.02, zc - 0.03), (0.018, 0.022, 0.018), "gun", bevel=0.005)
    sweep(g, [Vector((-0.12, y + 0.03, zc - 0.03)), Vector((-0.1, y + 0.06, zc - 0.03)), Vector((0.1, y + 0.06, zc - 0.03)),
              Vector((0.12, y + 0.03, zc - 0.03))], 0.012, "mustard", segs=10)
    box(g, (0, y + 0.001, zc - 0.13), (0.11, 0.003, 0.035), "gun", bevel=0.003)
    box(g, (0, y + 0.004, zc - 0.13), (0.095, 0.0012, 0.024), "dark")
    for k in range(4):
        box(g, (-0.06 + k * 0.04, y + 0.006, zc - 0.13), (0.012, 0.001, 0.012), "cyan_dim")
    # back (-Y): the copper connector block with two sockets
    y = -CY - 0.002
    box(g, (0, y - 0.02, zc), (0.15, 0.02, 0.09), "gun_dark", bevel=0.006)
    for sx in (-1, 1):
        cylinder(g, (sx * 0.08, y - 0.04, zc), (sx * 0.08, y - 0.046, zc), 0.045, "copper", segs=20)
        cylinder(g, (sx * 0.08, y - 0.046, zc), (sx * 0.08, y - 0.047, zc), 0.022, "hole", segs=16)
    # feet
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (CX - 0.05), sy * (CY - 0.06), Z0 - 0.006), (0.03, 0.04, 0.008), "rubber", bevel=0.004)
    return g


def build_charge_glow():
    """The lit charge window, its own piece so a clip can breathe it."""
    g = Geo()
    for k in range(5):
        box(g, (0, -0.17 + k * 0.075, Z1 + 0.0135), (0.098, 0.028, 0.0015), "cyan")
    return g


# =====================================================================================================
# The fallen cell (world object)
# =====================================================================================================

FALL_TILT = Euler((math.radians(-12), math.radians(24), math.radians(28)))
FALL_AT = Vector((0, 0, -Z0 - 0.07))          # sunk 7 cm into the furrow


def build_fallen_ground():
    g = Geo()
    rng = random.Random(41)
    # the furrow it ploughed: a dark trench running back from it, earth heaped round its front and sides
    yaw = Matrix.Rotation(FALL_TILT.z, 3, "Z")
    for k in range(22):     # overlapping, turned slabs of turned-over earth, narrowing as the gouge runs out
        t = k / 21
        turn = yaw @ Matrix.Rotation(rng.uniform(-0.5, 0.5), 3, "Z")
        c = yaw @ Vector((rng.uniform(-0.08, 0.08) * (1 - t), -0.45 - t * 2.4, 0.004 + 0.002 * (k % 3)))
        w = (0.3 - 0.14 * t) * rng.uniform(0.7, 1.0)
        box(g, c, (w, rng.uniform(0.07, 0.14), 0.01), "earth_dark" if k % 3 else "earth", bevel=0.008, rot=turn)
    for k in range(16):
        a = rng.uniform(-1.9, 1.9)
        r = rng.uniform(0.42, 0.62)
        c = yaw @ Vector((math.sin(a) * r, math.cos(a) * r * 0.9 + 0.05, 0.0))
        s = rng.uniform(0.06, 0.13)
        box(g, (c.x, c.y, s * 0.4), (s, s * rng.uniform(0.8, 1.3), s * 0.7), "earth" if k % 3 else "earth_dark",
            bevel=s * 0.4, rot=Matrix.Rotation(rng.uniform(0, 3), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 3, "X"))
    for k in range(10):    # torn turf flaps along the furrow's lips
        side = 1 if k % 2 else -1
        c = yaw @ Vector((side * rng.uniform(0.32, 0.42), -0.5 - k * 0.2, 0.03))
        box(g, c, (0.06, 0.09, 0.015), "grass", rot=yaw @ Matrix.Rotation(side * 0.6, 3, "Y"))
    # a scorched hull shard it took with it
    box(g, yaw @ Vector((0.55, -1.4, 0.03)), (0.18, 0.12, 0.012), "gun_dark", bevel=0.01,
        rot=yaw @ Matrix.Rotation(0.25, 3, "X") @ Matrix.Rotation(0.4, 3, "Z"))
    # the torn feed cable trailing from its connector, frayed copper at the end
    back = FALL_AT + FALL_TILT.to_matrix() @ Vector((0.08, -CY - 0.05, (Z0 + Z1) / 2))
    end = yaw @ Vector((0.25, -1.15, 0.04))
    sweep(g, bezier(back, back + Vector((0, 0, -0.12)), end + yaw @ Vector((0, 0.35, 0.05)), end, n=12), 0.022, "rubber", segs=10)
    for k in range(5):
        d = yaw @ Vector((rng.uniform(-0.04, 0.04), -rng.uniform(0.04, 0.09), rng.uniform(-0.01, 0.03)))
        cylinder(g, end, end + d, 0.004, "copper", segs=5)
    return g, end


def build_spark():
    g = Geo()
    for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1), (0.7, 0.7, 0)):
        a = Vector(axis).normalized() * 0.05
        cylinder(g, -a, a, 0.006, "spark", segs=5, r1=0.002)
    return g


def build_fallen(mats):
    rig = {"root": bl.make_empty("MasterBatteryFallen")}
    ground, end = build_fallen_ground()
    bl.make_object("Ground", ground, mats, rig["root"])
    cell = bl.make_object("Cell", build_cell(), mats, rig["root"], FALL_AT)
    cell.rotation_mode = "XYZ"
    cell.rotation_euler = FALL_TILT
    rig["charge"] = bl.make_object("Charge", build_charge_glow(), mats, cell)
    rig["spark"] = bl.make_object("Spark", build_spark(), mats, rig["root"], end + Vector((0, 0, 0.02)))
    return rig


def pose_fallen_idle(rig, t):
    """The window pulses slowly (still holds charge) and the frayed cable spits a spark now and then."""
    w = 2 * math.pi * t / EMPTY
    s = 0.75 + 0.25 * math.sin(w)
    rig["charge"].scale = (1, 1, s)
    flick = [0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0]
    on = flick[int(t / EMPTY * len(flick)) % len(flick)]
    spin = (t * 37.0) % (2 * math.pi)
    rig["spark"].scale = (1.0 if on else OFF,) * 3
    rig["spark"].rotation_euler = Euler((spin, spin * 0.7, spin * 1.3))


# =====================================================================================================
# The bay (Power Core)
# =====================================================================================================

def build_housing():
    g = Geo()
    # plinth under the slot, pillars either side, the upper cabinet above
    pz = SLOT_Z0 / 2
    box(g, (0, 0, pz), (HX - 0.03, HY - 0.03, pz - 0.01), "panel", bevel=0.01)
    rails(g, (0, 0, pz), (HX, HY, pz), r=0.1)
    for sx in (-1, 1):
        cx = sx * (SLOT_X + (HX - SLOT_X) / 2)
        hz = (SLOT_Z1 - SLOT_Z0) / 2
        box(g, (cx, 0, SLOT_Z0 + hz), ((HX - SLOT_X) / 2 - 0.02, HY - 0.03, hz), "panel", bevel=0.01)
        rails(g, (cx, 0, SLOT_Z0 + hz), ((HX - SLOT_X) / 2, HY, hz), r=0.08)
    uz = (SLOT_Z1 + HZ) / 2
    box(g, (0, 0, uz), (HX - 0.03, HY - 0.03, (HZ - SLOT_Z1) / 2 - 0.01), "panel", bevel=0.01)
    rails(g, (0, 0, uz), (HX, HY, (HZ - SLOT_Z1) / 2), r=0.12)
    # the slot: dark liner, two steel rails, the back wall with its contact pins
    box(g, (0, (BACK + HY) / 2, SLOT_Z0 + 0.005), (SLOT_X, (HY - BACK) / 2, 0.006), "dark")
    for sx in (-1, 1):
        box(g, (sx * SLOT_X, (BACK + HY) / 2, (SLOT_Z0 + SLOT_Z1) / 2), (0.006, (HY - BACK) / 2, (SLOT_Z1 - SLOT_Z0) / 2), "dark")
        box(g, (sx * 0.17, (BACK + HY) / 2, SLOT_Z0 + 0.012), (0.018, (HY - BACK) / 2, 0.01), "steel", bevel=0.004)
        box(g, (sx * (SLOT_X - 0.02), (BACK + HY) / 2, (SLOT_Z0 + SLOT_Z1) / 2), (0.012, (HY - BACK) / 2 - 0.02, 0.012), "steel", bevel=0.004)
    box(g, (0, (BACK + HY) / 2, SLOT_Z1 - 0.005), (SLOT_X, (HY - BACK) / 2, 0.006), "dark")
    box(g, (0, BACK - 0.005, (SLOT_Z0 + SLOT_Z1) / 2), (SLOT_X, 0.01, (SLOT_Z1 - SLOT_Z0) / 2), "gun_dark")
    zc = CELL_Z + (Z0 + Z1) / 2
    box(g, (0, BACK + 0.012, zc), (0.17, 0.012, 0.1), "gun", bevel=0.005)
    for sx in (-1, 1):
        cylinder(g, (sx * 0.08, BACK + 0.024, zc), (sx * 0.08, BACK + 0.034, zc), 0.05, "gun_dark", segs=20)
        cylinder(g, (sx * 0.08, BACK + 0.034, zc), (sx * 0.08, BACK + 0.07, zc), 0.02, "copper", segs=14, r1=0.017)
    # mouth trim: a mustard-and-black band round the slot
    y = HY + 0.012
    stripes(g, (0, y, SLOT_Z1 + 0.035), (SLOT_X + 0.06, 0.012, 0.03), 0, 10)
    stripes(g, (0, y, SLOT_Z0 - 0.035), (SLOT_X + 0.06, 0.012, 0.03), 0, 10)
    for sx in (-1, 1):
        stripes(g, (sx * (SLOT_X + 0.035), y, (SLOT_Z0 + SLOT_Z1) / 2), (0.03, 0.012, (SLOT_Z1 - SLOT_Z0) / 2), 2, 6)
        # clamp hinge brackets
        box(g, (sx * HINGE_X, HY + 0.02, HINGE_Z), (0.03, 0.03, 0.06), "gun", bevel=0.008)
        # hatch hinge knuckles
        cylinder(g, (sx * 0.29, HATCH_Y, SLOT_Z0 - 0.07), (sx * 0.22, HATCH_Y, SLOT_Z0 - 0.07), 0.022, "gun", segs=14)
        box(g, (sx * 0.255, (HY + HATCH_Y) / 2, SLOT_Z0 - 0.07), (0.035, (HATCH_Y - HY) / 2 + 0.01, 0.02), "gun", bevel=0.005)
    # the screen above the slot: frame, dark glass; the MASTER CELL plate under it
    box(g, (0, HY + 0.02, SCREEN_Z), (0.56, 0.03, 0.26), "gun", bevel=0.012)
    box(g, (0, HY + 0.045, SCREEN_Z), (0.5, 0.004, 0.2), "screen")
    box(g, (0, HY + 0.02, SCREEN_Z - 0.42), (0.36, 0.02, 0.07), "mustard", bevel=0.008)
    box(g, (0, HY + 0.035, SCREEN_Z - 0.42), (0.3, 0.004, 0.035), "black")
    for k in range(11):    # a stencil of blocks reading as a label at distance
        if k != 6:
            box(g, (-0.24 + k * 0.045, HY + 0.039, SCREEN_Z - 0.42), (0.016, 0.002, 0.022), "mustard")
    # vents up the cabinet front, screws in the rails
    for sx in (-1, 1):
        for k in range(5):
            box(g, (sx * 0.72, HY + 0.01, 1.2 + k * 0.1), (0.1, 0.012, 0.018), "gun_dark", bevel=0.004)
    # the feed trunk to the coils behind, over the top
    for sx in (-1, 1):
        cylinder(g, (sx * 0.5, -HY + 0.1, HZ - 0.02), (sx * 0.5, -HY + 0.1, HZ + 0.15), 0.07, "gun_dark", segs=16)
        sweep(g, bezier(Vector((sx * 0.5, -HY + 0.1, HZ + 0.15)), Vector((sx * 0.5, -HY + 0.1, HZ + 0.4)),
                        Vector((sx * 0.5, COILS[0][1] - 0.05, HZ + 0.4)), Vector((sx * 0.5, COILS[0][1], HZ - 0.4)), n=10),
              0.045, "rubber", segs=10)
    return g


def build_clamp(side):
    """A swing clamp hinged at the slot's mouth: a knuckle, an arm reaching back along the slot wall and a hook with a
    rubber pad that holds the cell's front corner (shut; in its own frame, the hinge at the origin)."""
    g = Geo()
    s = side
    cylinder(g, (0, 0, -0.07), (0, 0, 0.07), 0.026, "gun", segs=14)
    box(g, (-s * 0.04, 0, 0), (0.045, 0.018, 0.035), "mustard", bevel=0.008)
    box(g, (-s * 0.08, -0.11, 0), (0.018, 0.11, 0.035), "mustard", bevel=0.008)
    box(g, (-s * 0.12, -0.215, 0), (0.05, 0.015, 0.045), "gun", bevel=0.006)
    box(g, (-s * 0.13, -0.234, 0), (0.04, 0.006, 0.04), "rubber", bevel=0.003)
    return g


def build_hatch():
    """The slot's hatch, hinged along its bottom edge: a framed plate with a small window."""
    g = Geo()
    w, h = SLOT_X + 0.03, (SLOT_Z1 - SLOT_Z0) / 2 + 0.05
    box(g, (0, 0, h), (w - 0.02, 0.012, h - 0.02), "panel", bevel=0.006)
    rails(g, (0, 0, h), (w, 0.022, h), r=0.04)
    box(g, (0, 0.016, h + 0.06), (0.15, 0.006, 0.06), "screen")
    box(g, (0, 0.018, h + 0.06), (0.17, 0.008, 0.075), "gun_dark", bevel=0.004)
    box(g, (0, 0.022, h + 0.06), (0.14, 0.002, 0.052), "screen")
    stripes(g, (0, 0.026, 0.05), (w - 0.04, 0.004, 0.022), 0, 8)
    box(g, (0.0, 0.03, 2 * h - 0.06), (0.06, 0.02, 0.015), "mustard", bevel=0.006)  # the latch tab
    cylinder(g, (-w + 0.06, 0, 0), (w - 0.06, 0, 0), 0.016, "gun", segs=12)
    return g


def build_contacts():
    g = Geo()
    zc = CELL_Z + (Z0 + Z1) / 2
    for sx in (-1, 1):
        cylinder(g, (sx * 0.08, BACK + 0.035, zc), (sx * 0.08, BACK + 0.04, zc), 0.046, "cyan", segs=20)
    return g


def build_warn():
    g = Geo()
    box(g, (0, HY + 0.05, SCREEN_Z), (0.42, 0.002, 0.05), "red")
    return g


def build_bar(k):
    g = Geo()
    box(g, (-0.36 + k * 0.18, HY + 0.05, SCREEN_Z), (0.075, 0.002, 0.15), "cyan")
    return g


def build_coil_glow():
    """Glowing bands between a coil's copper turns (ship.py: eight turns 0.18 m apart from 0.85 m on a 0.22 m core)."""
    g = Geo()
    for k in range(7):
        z = 0.85 + 0.09 + k * 0.18 - BAY_AT[2]
        cylinder(g, (0, 0, z - 0.03), (0, 0, z + 0.03), 0.235, "cyan_soft", segs=20)
    return g


def build_cable():
    """The torn feed cable hanging from the cabinet beside the slot, sparking end down (in its own frame: hung from the
    origin)."""
    g = Geo()
    pts = bezier(Vector((0, 0, 0)), Vector((0.03, 0.12, -0.15)), Vector((0.0, 0.16, -0.4)), Vector((-0.02, 0.1, -0.62)), n=12)
    sweep(g, pts, 0.022, "rubber", segs=10)
    end = Vector(pts[-1])
    rng = random.Random(3)
    for k in range(5):
        d = Vector((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), -rng.uniform(0.03, 0.07)))
        cylinder(g, end, end + d, 0.004, "copper", segs=5)
    return g


CABLE_AT = (HX - 0.12, HY + 0.02, SLOT_Z1 + 0.32)


def build_bay(mats, parent=None, collider=False):
    rig = {}
    root = rig["root"] = bl.make_empty("BatteryBay", parent, BAY_AT)
    bl.make_object("Housing", build_housing(), mats, root)
    if collider:     # what the game collides with (StructureModel), never drawn: the housing as one box
        g = Geo()
        box(g, (0, 0, HZ / 2), (HX, HY, HZ / 2), "dark")
        bl.make_object("collider", g, mats, root)
    rig["cell"] = bl.make_object("Cell", build_cell(), mats, root, (0, HOME, CELL_Z))
    rig["charge"] = bl.make_object("Charge", build_charge_glow(), mats, rig["cell"])
    rig["clamps"] = [bl.make_object(f"Clamp{'L' if s < 0 else 'R'}", build_clamp(s), mats, root, (s * HINGE_X, HINGE_Y, HINGE_Z))
                     for s in (-1, 1)]
    rig["hatch"] = bl.make_object("Hatch", build_hatch(), mats, root, (0, HATCH_Y, SLOT_Z0 - 0.07))
    rig["contacts"] = bl.make_object("Contacts", build_contacts(), mats, root)
    rig["warn"] = bl.make_object("Warn", build_warn(), mats, root)
    rig["bars"] = [bl.make_object(f"Bar{k}", build_bar(k), mats, root) for k in range(5)]
    rig["coils"] = [bl.make_object(f"CoilGlow{i}", build_coil_glow(), mats, root, (x, y, 0)) for i, (x, y) in enumerate(COILS)]
    rig["cable"] = bl.make_object("Cable", build_cable(), mats, root, CABLE_AT)
    for ob in moving(rig):
        ob.rotation_mode = "XYZ"
    rest(rig)
    return rig


def moving(rig):
    return [rig["cell"], rig["charge"], *rig["clamps"], rig["hatch"], rig["contacts"], rig["warn"], *rig["bars"], *rig["coils"], rig["cable"]]


def clamp(x, lo=0.0, hi=1.0):
    return max(lo, min(hi, x))


def smooth(u):
    u = clamp(u)
    return u * u * (3 - 2 * u)


def spring(u, rate=7.0):
    u = clamp(u)
    return 1 - math.exp(-rate * u) * math.cos(rate * u) if u < 1 else 1.0


def window(t, a, b):
    return clamp((t - a) / (b - a))


def set_clamps(rig, shut):
    for s, ob in zip((-1, 1), rig["clamps"]):
        ob.rotation_euler = Euler((0, 0, s * CLAMP_OPEN * (1 - shut)))


def set_hatch(rig, shut):
    rig["hatch"].rotation_euler = Euler((HATCH_OPEN * (1 - shut), 0, 0))


def show(ob, on, along=None):
    """Shows a glow piece (scale 1) or hides it (shrunk to nothing); `along` grows it along one axis instead."""
    if along is None:
        ob.scale = (1.0 if on > 0.5 else OFF,) * 3
    else:
        s = [1.0, 1.0, 1.0]
        s[along] = max(OFF, on)
        ob.scale = tuple(s)


def rest(rig):
    """Powered and still: the cell home, clamps and hatch shut, every glow on (the review pose)."""
    rig["cell"].location = (0, HOME, CELL_Z)
    rig["cell"].scale = (1, 1, 1)
    rig["charge"].scale = (1, 1, 1)
    set_clamps(rig, 1)
    set_hatch(rig, 1)
    show(rig["contacts"], 1)
    show(rig["warn"], 0)
    for ob in rig["bars"]:
        show(ob, 1)
    for ob in rig["coils"]:
        show(ob, 1)
    rig["cable"].rotation_euler = Euler((0, 0, 0))


def pose_empty(rig, t):
    rest(rig)
    rig["cell"].scale = (OFF,) * 3
    set_clamps(rig, 0)
    set_hatch(rig, 0)
    show(rig["contacts"], 0)
    for ob in rig["bars"] + rig["coils"]:
        show(ob, 0)
    show(rig["warn"], 1 if (t % 1.0) < 0.55 else 0)
    w = 2 * math.pi * t / EMPTY
    rig["cable"].rotation_euler = Euler((0.09 * math.sin(w), 0.05 * math.sin(w + 1.1), 0))


def pose_install(rig, t):
    """0-1.0 s the cell slides home (the player's push), 1.0-1.5 clamps swing shut, 1.4-2.0 the hatch swings up and
    latches, 2.0 contacts light, 2.3-3.1 the screen bars come on one by one, 2.6-3.4 the coils light."""
    rest(rig)
    push = smooth(window(t, 0.0, 1.0))
    seat = 0.006 * math.sin(math.pi * window(t, 0.95, 1.15))     # a little bump as the pins seat
    rig["cell"].location = (0, MOUTH + (HOME - MOUTH) * push - seat, CELL_Z)
    set_clamps(rig, spring(window(t, 1.0, 1.55)))
    set_hatch(rig, spring(window(t, 1.4, 2.05), rate=6.0))
    show(rig["contacts"], 1 if t >= 2.0 else 0)
    show(rig["warn"], 1 if t < 2.3 and (t % 0.5) < 0.3 else 0)
    for k, ob in enumerate(rig["bars"]):
        show(ob, smooth(window(t, 2.3 + k * 0.16, 2.42 + k * 0.16)), along=2)
    for k, ob in enumerate(rig["coils"]):
        show(ob, smooth(window(t, 2.6 + k * 0.2, 3.2 + k * 0.2)), along=2)
    sway = math.exp(-2 * t) * math.sin(2 * math.pi * t / EMPTY)
    rig["cable"].rotation_euler = Euler((0.09 * sway, 0.05 * sway, 0))
    rig["charge"].scale = (1, 1, 0.6 + 0.4 * smooth(window(t, 2.0, 2.6)))


def pose_powered(rig, t):
    rest(rig)
    w = 2 * math.pi * t / POWERED
    for k, ob in enumerate(rig["bars"]):
        show(ob, 0.82 + 0.18 * math.sin(w - k * 0.6), along=2)
    for k, ob in enumerate(rig["coils"]):
        s = 0.97 + 0.03 * math.sin(w + k * math.pi)
        ob.scale = (s, s, 1.0)


CLIPS = (("empty", EMPTY, pose_empty), ("install", INSTALL, pose_install), ("powered", POWERED, pose_powered))


def bake(objs, clips):
    for ob in objs:
        ob.animation_data_create()
    for name, seconds, pose in clips:
        acts = {}
        for ob in objs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        frames = int(round(seconds * FPS))
        for f in range(frames + 1):
            pose(f / FPS)
            for ob in objs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(acts[ob.name])
            bl.push_track(ob, acts[ob.name], name)
    for ob in objs:
        ob.animation_data.use_nla = False


def unbake(objs):
    for ob in objs:
        if ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data.use_nla = False


# =====================================================================================================
# Main
# =====================================================================================================

def fore_frame():
    """An empty with the fore section's placement under the ship's origin (ship.py: tilted -1.5 deg and 2 deg)."""
    ship = bl.make_empty("SV04")
    fore = bl.make_empty("Fore", ship)
    fore.rotation_mode = "XYZ"
    fore.rotation_euler = (math.radians(-1.5), math.radians(2), 0)
    return ship, fore


ICON_VIEW = (1.3, 1.7, 1.0)   # from the front (+Y): handle, hazard band and plate


def main():
    out = Path(sys.argv[sys.argv.index("--render") + 1]) if "--render" in sys.argv else None

    bbe.reset_scene()
    mats = make_materials()
    cell = bl.make_empty("MasterBattery")
    bl.make_object("Case", build_cell(), mats, cell)
    bl.make_object("Charge", build_charge_glow(), mats, cell)
    bbe.export(cell, MESHES / "items" / "master_battery.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(cell), ICONS / "master_battery_icon.png", view=ICON_VIEW, ortho=0.95, target=(0.0, 0.0, -0.02))

    bbe.reset_scene()
    mats = make_materials()
    fallen = build_fallen(mats)
    bake([fallen["charge"], fallen["spark"]], (("idle", EMPTY, lambda t: pose_fallen_idle(fallen, t)),))
    bbe.export(fallen["root"], MESHES / "world" / "master_battery_fallen.glb", animations=True)

    bbe.reset_scene()
    mats = make_materials()
    ship, fore = fore_frame()
    bay = build_bay(mats, fore, collider=True)
    bake(moving(bay), [(n, s, lambda t, p=p: p(bay, t)) for n, s, p in CLIPS])
    bbe.export(ship, MESHES / "structures" / "sv04_battery_bay.glb", animations=True)

    for p in (MESHES / "items" / "master_battery.glb", ICONS / "master_battery_icon.png", MESHES / "world" / "master_battery_fallen.glb",
              MESHES / "structures" / "sv04_battery_bay.glb"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if out is not None:
        import render_master_battery
        render_master_battery.render_all(out, sys.modules[__name__])


if __name__ == "__main__":
    main()
