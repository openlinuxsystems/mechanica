"""The parts map's batch 4 logic parts at their five levels: the Controller and the Sensor (see part_kit.py).

Both are one block (0.25 m) on the Switch and Button's base plate (``logic_plate``) on the block's bottom face, in
the Salvager Tech finish of their level, with one pip per level (lit cyan at level 5). Their light is the logic teal
and is the ``pose`` readout's ``on`` and ``off`` clips (the scripts show ``on`` while the Controller runs and while
the Sensor sees something).

* ``build_controller(level)``: a housing with a row of ten step cells on its top, one fitted per output the level
  drives (1, 2, 4, 8, 10), lit while it runs, and a status lamp. Level 2 adds copper corner guards, 3 steel cooling
  fins, 4 a copper coil and a cyan running light, 5 black chrome.
* ``build_sensor(level)``: a round housing with the lens on top (the part's +y face, the way it looks), a teal ring
  round the lens that lights while it sees something. The shroud grows with the range: level 2 a copper shroud, 3
  steel cooling rings, 4 a second, wider lens ring and a cyan running light, 5 black chrome with a chrome shroud and
  an antenna.
"""

from __future__ import annotations

import math

from mathutils import Vector

import part_kit as K
from part_kit import Geo, box, cylinder, ring

HALF = 0.125
FLOOR = -HALF
OUTPUTS = {1: 1, 2: 2, 3: 4, 4: 8, 5: 10}     # the Controller's outputs per level (logic_power.json)


def glow_pose(objects):
    def apply(u):
        s = K.OFF + (1 - K.OFF) * u
        for ob in objects:
            ob.scale = (s, s, s)
    return apply


def build_controller(mats, level):
    f = K.FINISH[level]
    root = K.make_empty("Controller")
    g = Geo()
    top = K.logic_plate(g, FLOOR, (0.112, 0.112), f)
    hx, hy, h_top = 0.092, 0.088, top + 0.105
    box(g, (0, 0, (top + h_top) / 2), (hx, hy, (h_top - top) / 2), f["frame"], bevel=0.008)
    # a dark top deck with the step cells: ten windows in a row, fitted ones glass, the rest blanked
    box(g, (0, -0.012, h_top + 0.003), (hx - 0.012, 0.05, 0.004), f["dark"], bevel=0.002)
    n = OUTPUTS[level]
    cells = []
    for k in range(10):
        x = -0.0675 + k * 0.015
        if k < n:
            box(g, (x, -0.012, h_top + 0.0072), (0.0055, 0.032, 0.0012), "glass")
            cells.append((x, -0.012, h_top + 0.0086))
        else:
            box(g, (x, -0.012, h_top + 0.0072), (0.0055, 0.032, 0.0012), "hole")
    # status lamp in a copper bezel behind the cells, a teal band round the housing
    lamp = Vector((0.06, 0.06, h_top + 0.006))
    cylinder(g, lamp - Vector((0, 0, 0.006)), lamp + Vector((0, 0, 0.004)), 0.014, f["trim"], segs=16)
    cylinder(g, lamp, lamp + Vector((0, 0, 0.005)), 0.009, "lens", segs=14)
    box(g, (0, 0, top + 0.022), (hx + 0.0012, hy + 0.0012, 0.003), "teal", bevel=0.001)
    for sx in (-1, 1):
        K.screw(g, (sx * 0.072, 0.064, h_top), (0, 0, 1), 0.0055, f["bolt"])
    # pips on the front face, a vent slot above them
    K.pips(g, level, (-0.012 * (level - 1) / 2, hy + 0.0005, top + 0.04), (0.012, 0, 0), (0, 1, 0), f, r=0.0042)
    for k in range(3):
        box(g, (0, hy + 0.0005, top + 0.065 + k * 0.01), (0.05, 0.001, 0.0018), "hole")
    if level >= 2:
        for sx in (-1, 1):
            for sy in (-1, 1):
                box(g, (sx * (hx - 0.004), sy * (hy - 0.004), (top + h_top) / 2 + 0.01), (0.008, 0.008, (h_top - top) / 2 - 0.004),
                    f["trim"], bevel=0.003)
    if level >= 3:
        for sx in (-1, 1):
            for k in range(5):
                box(g, (sx * (hx + 0.005), -0.05 + k * 0.025, (top + h_top) / 2 + 0.004), (0.005, 0.0025, 0.035), "steel" if level < 5 else "chrome",
                    bevel=0.001)
    if level >= 4:
        # a copper coil on its back edge
        c0 = Vector((-0.05, -0.072, h_top + 0.012))
        cylinder(g, c0, c0 + Vector((0.1, 0, 0)), 0.006, f["dark"], segs=10)
        for k in range(9):
            x = -0.045 + k * 0.0112
            cylinder(g, Vector((x - 0.0035, -0.072, h_top + 0.012)), Vector((x + 0.0035, -0.072, h_top + 0.012)), 0.0105, f["trim"], segs=12)
        for sx in (-1, 1):
            box(g, (sx * 0.056, -0.072, h_top + 0.006), (0.004, 0.008, 0.008), f["frame"], bevel=0.002)
    K.make_object("Body", g, mats, root)
    glows = []
    for i, c in enumerate(cells):
        glows.append(K.make_object(f"Cell{i}", K.glow_box((0.005, 0.031, 0.0006), "teal_glow"), mats, root, c))
    glows.append(K.make_object("Lamp", K.glow_disc(0.0085, (0, 0, 1), "teal_glow", depth=0.0012, segs=14), mats, root,
                               tuple(lamp + Vector((0, 0, 0.0056)))))
    if level >= 4:
        K.make_object("RunningLight", K.glow_box((hx - 0.01, 0.0008, 0.0016), "cyan_glow"), mats, root,
                      (0, hy + 0.0016, top + 0.022))
    if level >= 5:
        for sx in (-1, 1):
            K.make_object(f"SideGlow{'R' if sx > 0 else 'L'}", K.glow_box((0.0008, hy - 0.01, 0.0016), "cyan_glow"), mats, root,
                          (sx * (hx + 0.0016), 0, top + 0.022))
    rig = {"root": root, "glows": glows}
    return rig


def build_sensor(mats, level):
    f = K.FINISH[level]
    root = K.make_empty("Sensor")
    g = Geo()
    top = K.logic_plate(g, FLOOR, (0.112, 0.112), f)
    r_body = 0.07 + 0.004 * (level - 1)
    h_top = top + 0.075
    # the housing: a short drum with a chamfered top, a teal band, on a square foot
    box(g, (0, 0, top + 0.008), (0.085, 0.085, 0.008), f["frame"], bevel=0.004)
    K.lathe(g, [(0, top), (r_body, top), (r_body, h_top - 0.012), (r_body - 0.012, h_top), (0, h_top)], f["frame"], steps=32)
    ring(g, top + 0.03, top + 0.036, r_body - 0.001, r_body + 0.0015, "teal", segs=32)
    # the lens: dark glass dome in a ring
    lens_r = 0.036 + 0.003 * (level - 1)
    K.lathe(g, [(0, h_top + 0.016), (lens_r * 0.6, h_top + 0.013), (lens_r, h_top + 0.003), (lens_r, h_top - 0.002), (0, h_top - 0.002)], "lens",
            steps=32)
    ring(g, h_top - 0.002, h_top + 0.006, lens_r, lens_r + 0.008, f["dark"], segs=32)
    if level >= 2:
        # the shroud round the lens, taller with the range
        sh = 0.012 + 0.004 * (level - 2)
        ring(g, h_top - 0.001, h_top + sh, lens_r + 0.014, lens_r + 0.02, "chrome" if level >= 5 else f["trim"], segs=32)
    if level >= 3:
        for k in range(3):
            z = top + 0.045 + k * 0.009
            ring(g, z, z + 0.004, r_body - 0.002, r_body + 0.005, "steel" if level < 5 else "chrome", segs=32)
    if level >= 5:
        a = Vector((-0.06, -0.06, top + 0.012))
        cylinder(g, a, a + Vector((0, 0, 0.09)), 0.0035, "chrome", segs=10)
        cylinder(g, a, a + Vector((0, 0, 0.012)), 0.008, f["dark"], segs=12)
    # pips on the foot's front
    K.pips(g, level, (-0.011 * (level - 1) / 2 + 0.05, 0.085 + 0.0005, top + 0.008), (0.011, 0, 0), (0, 1, 0), f, r=0.0035)
    for a in range(4):
        ang = math.pi / 4 + a * math.pi / 2
        K.screw(g, (math.cos(ang) * (r_body - 0.016), math.sin(ang) * (r_body - 0.016), h_top - 0.0005), (0, 0, 1), 0.004, f["bolt"])
    K.make_object("Body", g, mats, root)
    glows = [K.make_object("LensGlow", K.glow_ring(lens_r + 0.0005, lens_r + 0.007, "teal_glow", h=0.0012), mats, root,
                           (0, 0, h_top + 0.0068))]
    if level >= 4:
        glows.append(K.make_object("LensGlow2", K.glow_ring(lens_r + 0.021, lens_r + 0.026, "teal_glow", h=0.0012), mats, root,
                                   (0, 0, h_top + 0.0006)))
        K.make_object("RunningLight", K.glow_ring(r_body + 0.0005, r_body + 0.002, "cyan_glow", h=0.002), mats, root, (0, 0, top + 0.0145))
    if level >= 5:
        K.make_object("AntennaTip", K.glow_box((0.0045, 0.0045, 0.0045), "cyan_glow"), mats, root, (-0.06, -0.06, top + 0.106))
    return {"root": root, "glows": glows}


def bake(rig):
    K.bake_pose_clips(rig["glows"], glow_pose(rig["glows"]))
