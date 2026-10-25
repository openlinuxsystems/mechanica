"""Builds the Horn (audit batch D2; tools/logic/logic_parts.py has the shared kit).

    python3 tools/logic/build_horn.py [--render OUT_DIR]

One block (0.25 m) on the Switch and Button's gunmetal base plate: a gunmetal compression driver on a pedestal at the
back, a copper bell horn flaring forward (the part's front, the game's +Z) with a rolled lip, held by a saddle post, and a
small cyan "powered" pip on top of the driver that lights while it honks. Writes
``mods/vanilla/meshes/parts/horn.glb`` with its ``on`` and ``off`` clips (the ``pose`` readout scripts/horn.lua
sets) and ``mods/vanilla/textures/items/horn_icon.png``; with ``--render`` the review sheet.

Blender's Python module (bpy) runs it; nothing is downloaded.
"""

import sys
from pathlib import Path

sys.dont_write_bytecode = True                      # no __pycache__ next to the scripts
sys.path.insert(0, str(Path(__file__).resolve().parent))
import logic_parts as P  # noqa: E402  (first: it imports bpy)

K, Geo, box, cylinder = P.K, P.Geo, P.box, P.cylinder
AX = -0.03                    # the horn's axis height
DRIVER_BACK = -0.112          # the driver's back face (y)
DRIVER_LEN = 0.07
BELL_AT = DRIVER_BACK + DRIVER_LEN       # the bell's throat (y)
BELL_LEN = 0.158
THROAT, MOUTH = 0.017, 0.064
WALL = 0.0025
PIP = (0.0, -0.08, AX + 0.0525)


def bell_r(t):
    return THROAT * (MOUTH / THROAT) ** (t ** 1.6)


def build(mats):
    f = K.FINISH[1]
    root = K.make_empty("Horn")
    g = Geo()
    top = K.logic_plate(g, -0.125, (0.112, 0.112), f)
    axis = (0, 1, 0)
    # the compression driver: a rounded drum, a teal band, two dark ribs and a strap
    P.lathe_along(g, [(0, 0), (0.036, 0), (0.042, 0.005), (0.042, DRIVER_LEN - 0.012), (0.034, DRIVER_LEN - 0.004),
                      (0.02, DRIVER_LEN), (0, DRIVER_LEN)], "gun", (0, DRIVER_BACK, AX), axis, steps=40)
    for z0, z1, r1, mat in ((0.012, 0.016, 0.0434, "teal"), (0.024, 0.027, 0.0438, "gun_dark"), (0.031, 0.034, 0.0438, "gun_dark"),
                            (0.042, 0.052, 0.0458, "gun_dark")):
        P.lathe_along(g, [(0.040, z0), (r1, z0), (r1, z1), (0.040, z1)], mat, (0, DRIVER_BACK, AX), axis, steps=40)
    # the pedestal under it, screwed to the plate, with the model code on its side
    ped_top = AX - 0.036
    box(g, (0, -0.072, (top + ped_top) / 2), (0.03, 0.026, (ped_top - top) / 2 + 0.002), "gun", bevel=0.004)
    box(g, (0, -0.072, top + 0.004), (0.036, 0.032, 0.004), "gun_dark", bevel=0.002)
    for sx in (-1, 1):
        K.screw(g, (sx * 0.0305, -0.0525, (top + ped_top) / 2), (sx, 0, 0), 0.004, f["bolt"])
    P.decal_quad(g, (0.0306, -0.079, (top + ped_top) / 2), (0, 1, 0), (0, 0, 1), 0.034, 0.0136, "code_horn")
    # the terminal block on its back: two copper terminals the links plug into
    box(g, (0, DRIVER_BACK - 0.003, AX - 0.004), (0.02, 0.004, 0.012), "hull", bevel=0.002)
    for sx in (-1, 1):
        cylinder(g, (sx * 0.009, DRIVER_BACK - 0.006, AX - 0.004), (sx * 0.009, DRIVER_BACK - 0.0095, AX - 0.004), 0.0042,
                 f["trim"], segs=12)
    # the bell: a thin copper wall flaring forward, a rolled lip, a dark throat
    n = 16
    outer = [(bell_r(k / n), BELL_LEN * k / n) for k in range(n + 1)]
    lip = [(MOUTH + 0.004, BELL_LEN - 0.001), (MOUTH + 0.0045, BELL_LEN + 0.002), (MOUTH + 0.001, BELL_LEN + 0.0035),
           (MOUTH - WALL, BELL_LEN + 0.001)]
    inner = [(bell_r(k / n) - WALL, BELL_LEN * k / n) for k in range(n - 1, -1, -1)]
    P.lathe_along(g, outer + lip + inner, "bell_copper", (0, BELL_AT, AX), axis, steps=48)
    K.disc(g, (0, BELL_AT + 0.004, AX), (0, 1, 0), THROAT - WALL + 0.0005, "hole", segs=20)
    # the saddle post holding the bell
    t = 0.56
    y_post, r_post = BELL_AT + BELL_LEN * t, bell_r(t)
    box(g, (0, y_post, (top + AX - r_post) / 2), (0.011, 0.007, (AX - r_post - top) / 2 + 0.002), "gun", bevel=0.003)
    P.lathe_along(g, [(r_post - 0.001, -0.005), (r_post + 0.004, -0.005), (r_post + 0.004, 0.005), (r_post - 0.001, 0.005)],
                  "gun_dark", (0, y_post, AX), axis, steps=36)
    box(g, (0, y_post, top + 0.003), (0.018, 0.012, 0.003), "gun_dark", bevel=0.0015)
    # the pip: a small dark lamp housing on the driver, a copper bezel, a lens
    box(g, (PIP[0], PIP[1], AX + 0.044), (0.018, 0.019, 0.0072), "hull", bevel=0.0025)
    cylinder(g, (PIP[0], PIP[1], PIP[2] - 0.003), (PIP[0], PIP[1], PIP[2] + 0.0018), 0.0125, f["trim"], segs=18)
    cylinder(g, (PIP[0], PIP[1], PIP[2] - 0.002), (PIP[0], PIP[1], PIP[2] + 0.0026), 0.0088, "lens", segs=16)
    K.make_object("Body", g, mats, root)
    glow = K.make_object("Pip", K.glow_disc(0.0082, (0, 0, 1), "cyan_glow", depth=0.0012, segs=16), mats, root,
                         (PIP[0], PIP[1], PIP[2] + 0.0028))
    rig = {"root": root, "glows": [glow]}
    K.bake_pose_clips([glow], lambda u: P.lit([glow], u))
    P.end_clips([glow])
    P.lit([glow], 0)
    rig["states"] = {"silent": lambda: P.lit([glow], 0), "honking (input on)": lambda: P.lit([glow], 1),
                     "icon": lambda: P.lit([glow], 1)}
    return rig


if __name__ == "__main__":
    P.run("horn", build, ortho=0.34, icon_target=(0, 0, -0.04), icon_state="icon", title="Horn (1 block)",
          lines=["On the Switch's base plate: a gunmetal driver on a pedestal, a copper bell horn with a rolled lip facing the",
                 "part's front, and a cyan pip on the driver that lights while it honks (pose on and off)."])
