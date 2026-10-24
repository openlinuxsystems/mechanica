"""Review renders of the Old Stone kit and the ruins: python3 render_ruins.py OUT [views...]

Views: ``kit`` (every piece of stone_kit.py on a sheet, labelled), and for each ruin ``<name>_34`` (three-quarter view
from the front right) and ``<name>_eye`` (eye level from the front), each with a 1.8 m player for scale; for Ruin City
also ``ruin_city_above`` (from high over the gate), ``ruin_city_street`` (down the avenue from a street corner),
``ruin_city_plaza`` (across the plaza to the temple) and ``ruin_city_keep`` (from the top of the keep).
``<name>_top`` looks across a ruin's highest walkable floor (the lookout's crate). SAMPLES (default 32) and SCALE
(resolution percent, default 100) control quality; renders are 1600 px wide JPEGs.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
import bpy  # noqa: E402

import kit as K  # noqa: E402
import frame as FR  # noqa: E402
import stone_kit as S  # noqa: E402
import ruins as R  # noqa: E402
from families import M, GRAPHITE  # noqa: E402

out = sys.argv[1]
ALL = ["kit"] + [f"{n}_{v}" for n in ("small_ruin", "ruin", "forest_ruin", "mountain_lookout") for v in ("34", "eye")] + \
    ["mountain_lookout_top", "ruin_city_34", "ruin_city_above", "ruin_city_eye", "ruin_city_street", "ruin_city_plaza", "ruin_city_keep"]
views = sys.argv[2:] or ALL
os.makedirs(out, exist_ok=True)
MAN = K.MESH / "characters/scrapper_male.glb"


def scene(ground=True, color="#7f9a5c"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K._protos.clear()
    K.setup_render(int(os.environ.get("SAMPLES", "32")))
    sc = bpy.context.scene
    sc.render.resolution_percentage = int(os.environ.get("SCALE", "100"))
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    K.sky(1.0, top="#86b4d8", horizon="#dce8ee")
    K.sun((52, 0, -38), 4.2)
    if ground:
        K.ground(2000, color=color)


def shoot(name, loc, target, lens, w=1600, h=900):
    cam = K.camera(name, loc, target, lens=lens)
    K.render(cam, f"{out}/{name}.jpg", w, h)


def man(loc, yaw=0.0):
    return K.import_glb(MAN, loc, yaw=yaw)


def tag(P, text, loc, size=0.5):
    FR.label(P, text, loc, size, M(GRAPHITE, wear=0, grime=0, scratches=0), rot=(0, 0, 0), extrude=0.005)


def kit_sheet():
    """Every piece in rows on a pale pad, each named on the ground in front of it."""
    scene(color="#7f8b6c")
    site = S.Site("sheet", 5)
    P = site.P
    K.box("sheet_pad", (92.0, 62.0, 0.06), M("#b9b6ac", wear=0, grime=0, scratches=0), loc=(4.0, 10.0, 0.03), bev=0, parent=P)
    z = 0.06
    rnd = site.rnd
    # row 1: walls
    y = -10.0
    S.wall(site, (-38.0, y), (-30.0, y), 4.25, t=0.7, z0=z, holes=[S.door(2.0, z + 0.3), S.window(5.5, z + 0.3)])
    tag(P, "WALL WHOLE, DOOR, WINDOW", (-34.0, y - 2.2, z + 0.01))
    S.wall(site, (-27.0, y), (-19.0, y), S.broken(8.0, 4.25, 1.5, rnd, points=5), t=0.7, z0=z, holes=[S.door(5.5, z + 0.3, arch=True)])
    tag(P, "WALL BROKEN, ARCHED DOOR", (-23.0, y - 2.2, z + 0.01))
    S.wall(site, (-16.0, y), (-8.0, y), S.collapsed(8.0, rnd), t=0.7, z0=z)
    S.rubble(site, (-12.0, y - 1.8, z), 1.8, 1.0, 0.6)
    tag(P, "WALL COLLAPSED, RUBBLE", (-12.0, y - 3.4, z + 0.01))
    S.wall(site, (-5.0, y), (3.0, y), S.broken(8.0, 4.0, 2.5, rnd, points=4), t=0.7, z0=z,
           holes=[S.window(2.0, z + 0.3, arch=True), S.window(6.0, z + 0.3, w=1.25, h=1.5)])
    tag(P, "ARCHED AND SQUARE WINDOWS", (-1.0, y - 2.2, z + 0.01))
    for k, (x, kw) in enumerate(((7.0, {}), (10.0, {"broken_at": 1.6}))):
        S.pillar(site, (x, y, z), 4.0, w=0.9, **kw)
    tag(P, "PILLARS", (8.5, y - 2.2, z + 0.01))
    S.column(site, (15.0, y, z), 5.0, r=0.45)
    S.column(site, (18.5, y, z), 5.0, r=0.45, broken_at=1.8)
    tag(P, "COLUMNS", (16.8, y - 2.2, z + 0.01))
    S.arch(site, (27.0, y, z), span=3.0, spring=3.0, depth=1.0)
    S.arch(site, (36.0, y, z), span=3.0, spring=2.5, depth=1.0, broken=True)
    tag(P, "ARCH WHOLE, BROKEN", (31.5, y - 2.6, z + 0.01))
    # row 2: floors, stairs, rubble, bits
    y = 6.0
    S.floor(site, -40.0, -32.0, y - 3.0, y + 3.0, z + 0.3, gone=0.1, lifted=0.08)
    tag(P, "FLAGSTONE FLOOR", (-36.0, y - 4.6, z + 0.01))
    S.stair(site, (-27.0, y - 2.5, z), (-27.0, y + 1.5, z + 3.0), 2.0)
    S.slab(site, -29.0, -24.0, y + 1.5, y + 4.0, z + 3.0, ragged=("x1", 0.6), below=z)
    for x in (-28.5, -24.5):
        S.pillar(site, (x, y + 3.6, z), 2.6, w=0.6, cap=False)
    tag(P, "STAIR, UPPER FLOOR SLAB", (-26.5, y - 4.6, z + 0.01))
    S.rubble(site, (-17.0, y, z), 2.6, 2.0, 1.2)
    tag(P, "RUBBLE HEAP", (-17.0, y - 4.6, z + 0.01))
    for k in range(4):
        S.fallen(site, (-9.0 + k * 1.6, y + rnd.uniform(-1, 1), z), (1.0, 0.7, 0.5), lean=rnd.choice((5, 30, 70, 85)), yaw=rnd.uniform(0, 90))
    tag(P, "FALLEN BLOCKS", (-6.6, y - 4.6, z + 0.01))
    for k in range(6):
        S.grass(site, (1.0 + k * 0.6, y + rnd.uniform(-1, 1), z))
        S.moss(site, (1.0 + k * 0.6, y + 1.8, z + 0.02), 0.3)
    S.crystals(site, (6.0, y, z), n=6, h=1.6)
    tag(P, "GRASS, MOSS, CRYSTALS", (3.5, y - 4.6, z + 0.01))
    # row 3: a house of each kind, the tower
    y = 24.0
    S.house(site, -40.0, y - 4.0, -30.0, y + 4.0, z0=z + 0.3, storeys=1, ruin="whole", seed=31)
    tag(P, "HOUSE WHOLE", (-35.0, y - 6.0, z + 0.01))
    S.house(site, -27.0, y - 5.0, -15.0, y + 5.0, z0=z + 0.3, storeys=2, ruin="half", upper=("y", y + 0.5), seed=32, arch_doors=True)
    tag(P, "HOUSE TWO STOREYS, UPPER FLOOR", (-21.0, y - 7.0, z + 0.01))
    S.house(site, -12.0, y - 4.0, -2.0, y + 4.0, z0=z + 0.3, storeys=1, ruin="shell", seed=33)
    tag(P, "HOUSE SHELL", (-7.0, y - 6.0, z + 0.01))
    S.tower(site, 12.0, y + 1.0, 10.0, z + 0.3, 14.75, t=0.9, broken_sides=("n",), spike=("e", 18.5, 0.75))
    tag(P, "TOWER, STAIR INSIDE", (12.0, y - 6.0, z + 0.01))
    site.done()
    man((2.0, -12.5, z), 200)
    shoot("kit_sheet", (0, -88.0, 62.0), (2.0, 10.0, 2.0), 30, 1600, 1100)
    shoot("kit_1_walls", (0.0, -34.0, 9.0), (0.0, -10.0, 2.0), 30, 1600, 700)
    shoot("kit_2_floors_bits", (-16.0, -16.0, 10.0), (-16.0, 6.0, 0.5), 30, 1600, 700)
    shoot("kit_3_houses_tower", (-12.0, -10.0, 22.0), (-8.0, 24.0, 4.0), 30, 1600, 800)


def ruin_scene(name):
    scene()
    root = R.RUINS[name]()
    return root


def mark(loc):
    """A marker's crate stand-in: a 0.75 m wooden box where the POI puts its crate."""
    K.box("crate", (0.75, 0.75, 0.75), K.mat("pwood0", "#9c7650", rough=0.85), loc=(loc[0], loc[1], loc[2] + 0.375), bev=0.03)


CAMS = {
    # name: (34 cam, 34 target, lens), (eye cam, eye target, lens), man (loc, yaw), crates
    "small_ruin": (((20.0, -24.0, 13.0), (0.0, 1.0, 1.5), 30), ((4.0, -19.0, 1.7), (-1.0, 2.0, 2.2), 30), ((1.2, -5.0, 0.3), 200),
                   [(-9.0, -2.0, 0.3)]),
    "ruin": (((34.0, -40.0, 22.0), (0.0, 1.0, 2.5), 30), ((6.0, -30.0, 1.7), (0.0, 4.0, 4.0), 28), ((8.0, -13.8, 0.3), 200),
             [(-10.0, -4.0, 0.3)]),
    "forest_ruin": (((18.0, -20.0, 11.0), (0.0, 1.0, 1.5), 30), ((3.0, -15.0, 1.7), (-1.0, 2.0, 2.0), 30), ((3.5, -6.5, 0.0), 200),
                    [(-5.0, 5.0, 0.0), (4.0, -5.0, 0.0), (-6.4, 0.0, 0.0)]),
    "mountain_lookout": (((22.0, -28.0, 16.0), (0.0, 0.0, 8.0), 30), ((5.0, -20.0, 1.7), (0.0, 0.0, 8.5), 26), ((-1.0, -6.6, 0.3), 200),
                         [(2.5, -2.5, 14.75)]),
}

for v in views:
    if v == "kit":
        kit_sheet()
        continue
    name, kind = v.rsplit("_", 1)
    if name.startswith("ruin_city"):
        import ruin_city as RC  # noqa: PLC0415
        scene()
        RC.ruin_city()
        man_at = {"34": ((4.0, -72.0, 0.3), 160), "above": ((2.0, -66.0, 0.3), 180), "eye": ((2.5, -73.0, 0.3), 160),
                  "street": ((-30.0, -24.0, 0.6), 100), "plaza": ((5.0, 6.0, 0.3), 30), "keep": ((-46.0, 38.5, 24.3), 40)}[kind]
        man(*man_at)
        for c in RC.CRATES:
            mark(c)
        if kind == "34":
            shoot(v, (125.0, -150.0, 80.0), (0.0, 0.0, 0.0), 30)
        elif kind == "above":
            shoot(v, (0.0, -125.0, 135.0), (0.0, 0.0, 0.0), 34)
        elif kind == "eye":
            shoot(v, (8.0, -88.0, 1.7), (0.0, -50.0, 6.0), 30)
        elif kind == "street":
            shoot(v, (-38.0, -27.5, 1.7), (10.0, -24.0, 2.5), 28)
        elif kind == "plaza":
            shoot(v, (12.0, -10.0, 1.7), (0.0, 30.0, 4.0), 26)
        elif kind == "keep":
            shoot(v, (-47.5, 36.5, 26.0), (20.0, -10.0, 0.0), 24)
        continue
    scene()
    root = R.RUINS[name]()
    c34, ceye, (mloc, myaw), crates = CAMS[name]
    for c in crates:
        mark(c)
    if kind == "top":
        man((1.0, -0.5, 14.75), 220)
        shoot(v, (-3.0, -3.3, 16.6), (3.0, 0.5, 14.6), 22)
        continue
    man(mloc, myaw)
    loc, tgt, lens = c34 if kind == "34" else ceye
    shoot(v, loc, tgt, lens)
