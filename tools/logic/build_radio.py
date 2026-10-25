"""Builds the Radio (audit batch D2; tools/logic/logic_parts.py has the shared kit).

    python3 tools/logic/build_radio.py [--render OUT_DIR]

A colony-era set, 2 x 2 x 1 blocks (0.5 x 0.5 x 0.25 m) standing on rubber feet: a framed gunmetal case, the
speaker grille (slats over dark cloth) on the left of its front, on the right a tuning dial behind glass with the six
station marks, a red needle, two copper knobs, a power lamp and the CR-6 model plate; a carry handle and a whip
antenna on top, folded down so it stays inside the footprint. Writes ``mods/vanilla/meshes/parts/radio.glb`` and
``mods/vanilla/textures/items/radio_icon.png``; with ``--render`` the review sheet. Clips (``display.clips``;
scripts/radio.lua sets the readouts):

* ``on`` and ``off`` (0.12 s; the ``pose`` readout): the dial's cyan backlight and the power lamp;
* ``tune`` (scrubbed by the ``tune`` readout, 0 at station 1 to 1 at station 6): the needle across the dial.

Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.dont_write_bytecode = True                      # no __pycache__ next to the scripts
sys.path.insert(0, str(Path(__file__).resolve().parent))
import logic_parts as P  # noqa: E402  (first: it imports bpy)

K, Geo, box, cylinder = P.K, P.Geo, P.box, P.cylinder
X, Y = 0.235, 0.1             # the case's half width and half depth
CB, CT = -0.236, 0.1          # the case's bottom and top (on 14 mm feet)
FRONT = Y - 0.006             # the inset front panel
COL = 0.112                   # the centre of the dial column (+x is the viewer's left; the dial is on the right)
DIAL_Z, DIAL_W, DIAL_H = 0.035, 0.18, 0.07
NEEDLE_Y = FRONT + 0.0032


def needle_x(u):
    """Station 1 (u = 0) to station 6 (u = 1): the dial's marks run from 10 % to 90 % of its width, from the viewer's
    left (+x) to right (-x)."""
    return -COL + DIAL_W * (0.4 - 0.8 * u)


def build(mats):
    f = K.FINISH[1]
    mats["signal_red"] = P.pt.plain("d2_signal_red", "#c8423a", 0.45, 0.1)
    root = K.make_empty("Radio")
    g = Geo()
    zc, hz = (CB + CT) / 2, (CT - CB) / 2
    # the case: inset panels in a frame of corner posts and rails, a middle rail between grille and dial
    box(g, (0, 0, zc), (X - 0.006, Y - 0.006, hz), "gun_dark", bevel=0.005)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (X - 0.012), sy * (Y - 0.012), zc), (0.012, 0.012, hz), "gun", bevel=0.004)
    for z in (CT - 0.01, CB + 0.01):
        for s in (-1, 1):
            box(g, (0, s * (Y - 0.012), z), (X - 0.02, 0.012, 0.01), "gun", bevel=0.003)
            box(g, (s * (X - 0.012), 0, z), (0.012, Y - 0.02, 0.01), "gun", bevel=0.003)
    box(g, (0, Y - 0.008, zc), (0.008, 0.008, hz - 0.012), "gun", bevel=0.003)
    box(g, (0, 0, CB + 0.03), (X - 0.0048, Y - 0.0048, 0.0025), "teal", bevel=0.0008)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.screw(g, (sx * (X - 0.012), sy * (Y - 0.012), CT), (0, 0, 1), 0.0055, f["bolt"])
            cylinder(g, (sx * 0.2, sy * 0.07, -0.25), (sx * 0.2, sy * 0.07, CB + 0.001), 0.015, "rubber", segs=16)
    # the speaker: dark cloth behind thirteen slats (viewer's left, +x)
    gx, gw, gz0, gz1 = COL, 0.2, CB + 0.024, CT - 0.024
    P.decal_quad(g, (gx, FRONT + 0.0006, (gz0 + gz1) / 2), (-1, 0, 0), (0, 0, 1), gw, gz1 - gz0, "grille")
    for k in range(13):
        z = gz0 + 0.014 + k * (gz1 - gz0 - 0.028) / 12
        box(g, (gx, FRONT + 0.004, z), (gw / 2 - 0.004, 0.0035, 0.0042), "gun", bevel=0.0015)
    for s in (-1, 1):
        box(g, (gx + s * (gw / 2 - 0.002), FRONT + 0.005, (gz0 + gz1) / 2), (0.004, 0.005, (gz1 - gz0) / 2), "gun", bevel=0.0015)
    # the dial: the scale, its lit twin (a node), the needle (a node), glass and a bezel
    P.decal_quad(g, (-COL, FRONT + 0.0008, DIAL_Z), (-1, 0, 0), (0, 0, 1), DIAL_W, DIAL_H, "radio_dial")
    box(g, (-COL, FRONT + 0.0062, DIAL_Z), (DIAL_W / 2, 0.0006, DIAL_H / 2), "clear_glass")
    for s in (-1, 1):
        box(g, (-COL, FRONT + 0.005, DIAL_Z + s * (DIAL_H / 2 + 0.005)), (DIAL_W / 2 + 0.01, 0.005, 0.005), "gun", bevel=0.002)
        box(g, (-COL + s * (DIAL_W / 2 + 0.005), FRONT + 0.005, DIAL_Z), (0.005, 0.005, DIAL_H / 2), "gun", bevel=0.002)
    # the knobs: copper, on dark skirts, each with a pointer line
    for x in (-COL + 0.047, -COL - 0.047):
        z = -0.06
        cylinder(g, (x, FRONT - 0.001, z), (x, FRONT + 0.005, z), 0.031, "gun_dark", segs=28)
        cylinder(g, (x, FRONT + 0.005, z), (x, FRONT + 0.024, z), 0.025, f["trim"], segs=28, r1=0.022)
        cylinder(g, (x, FRONT + 0.024, z), (x, FRONT + 0.0255, z), 0.019, "copper_bright", segs=28)
        box(g, (x, FRONT + 0.0257, z + 0.011), (0.0016, 0.0006, 0.008), "hole")
    # the power lamp and the model plate
    lamp = (-COL, FRONT + 0.006, -0.118)
    cylinder(g, (lamp[0], FRONT - 0.001, lamp[2]), (lamp[0], FRONT + 0.0045, lamp[2]), 0.0095, f["trim"], segs=18)
    cylinder(g, (lamp[0], FRONT, lamp[2]), (lamp[0], FRONT + 0.0055, lamp[2]), 0.0062, "lens", segs=16)
    P.decal_quad(g, (-COL, FRONT + 0.0007, -0.172), (-1, 0, 0), (0, 0, 1), 0.08, 0.0267, "code_radio")
    # the back: vent slots and screws; the sides: screws
    for k in range(6):
        box(g, (0, -FRONT - 0.0004, -0.02 + k * 0.016), (0.11, 0.001, 0.0028), "hole")
    for sx in (-1, 1):
        for sz in (-1, 1):
            K.screw(g, (sx * 0.15, -FRONT - 0.0004, zc + sz * 0.11), (0, -1, 0), 0.005, f["bolt"])
            K.screw(g, (sx * (X - 0.006 + 0.0004), 0.05 * sz, zc + 0.11), (sx, 0, 0), 0.0045, f["bolt"])
            K.screw(g, (sx * (X - 0.006 + 0.0004), 0.05 * sz, zc - 0.11), (sx, 0, 0), 0.0045, f["bolt"])
    # the handle on top
    for sx in (-1, 1):
        box(g, (sx * 0.13, 0.0, CT + 0.017), (0.009, 0.011, 0.017), "gun", bevel=0.003)
    cylinder(g, (-0.142, 0, CT + 0.03), (0.142, 0, CT + 0.03), 0.0085, "rubber", segs=16)
    # the whip antenna: a copper swivel at the back right corner, three chrome sections folded over the top, a ball tip
    base = K.Vector((-0.19, -0.05, CT))
    cylinder(g, base, base + K.Vector((0, 0, 0.012)), 0.011, f["trim"], segs=18)
    K.hexnut(g, base + K.Vector((0, 0, 0.012)), (0, 0, 1), 0.008, 0.006, "steel")
    a = base + K.Vector((0, 0, 0.016))
    tip = K.Vector((-0.02, -0.05, 0.236))
    d = tip - a
    for t0, t1, r in ((0.0, 0.42, 0.0055), (0.4, 0.74, 0.0042), (0.72, 1.0, 0.003)):
        cylinder(g, a + d * t0, a + d * t1, r, "chrome", segs=12)
    cylinder(g, tip - d.normalized() * 0.004, tip + d.normalized() * 0.004, 0.0055, "chrome", segs=12)
    K.make_object("Body", g, mats, root)

    lit_g = Geo()
    P.decal_quad(lit_g, (0, P.SINK + 0.0004, 0), (-1, 0, 0), (0, 0, 1), DIAL_W, DIAL_H, "radio_dial_lit")
    dial = K.make_object("DialLit", lit_g, mats, root, (-COL, FRONT + 0.0008 - P.SINK, DIAL_Z))
    lamp_glow = K.make_object("Lamp", K.glow_disc(0.0056, (0, 1, 0), "cyan_glow", depth=0.0012, segs=16), mats, root,
                              (lamp[0], FRONT + 0.006, lamp[2]))
    ng = Geo()
    box(ng, (0, 0, 0), (0.002, 0.0007, DIAL_H / 2 - 0.006), "signal_red")
    needle = K.make_object("Needle", ng, mats, root, (needle_x(0), NEEDLE_Y, DIAL_Z))
    glows = [dial, lamp_glow]

    def tune(u):
        needle.location = (needle_x(u), NEEDLE_Y, DIAL_Z)

    K.bake_pose_clips(glows, lambda u: P.lit(glows, u))
    P.bake_clip([needle], "tune", 1.0, tune)
    P.end_clips([*glows, needle])
    P.lit(glows, 0)
    tune(0)

    def state(on, station):
        def apply():
            P.lit(glows, 1 if on else 0)
            tune((station - 1) / 5)
        return apply

    rig = {"root": root, "glows": glows, "needle": needle,
           "states": {"off (station 1)": state(False, 1), "on, station 3": state(True, 3), "on, station 6": state(True, 6),
                      "icon": state(True, 2)}}
    return rig


if __name__ == "__main__":
    P.run("radio", build, ortho=0.62, icon_target=(0, 0, -0.03), icon_state="icon", title="Radio (2 x 2 x 1 blocks)",
          half=(0.25, 0.125, 0.25),
          lines=["A colony-era set in a framed gunmetal case: speaker grille, a tuning dial behind glass that lights cyan",
                 "while it plays (pose on and off), the needle on the station (tune), two copper knobs and a whip antenna."])
