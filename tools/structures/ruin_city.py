"""Ruin City (docs/art-direction.md, Old Stone): a stone town of the people before the colony, 160 x 140 m, built from
stone_kit.py and split into seven models that share one origin (the POI's centre), so its prefab places all seven at
[0, 0, 0]:

* ``ruin_city_centre`` - the paved ground of the whole town on its low platform, the high street (the avenue, raised
  a step, with colonnades), the plaza with its broken fountain, statue and obelisk, the temple on its podium at the
  plaza's head (columns, a roofless cella, probe crystals growing from the altar) and the houses beside it.
* ``ruin_city_keep`` - the north-west quarter: the keep, a square tower 14 m across whose stair winds up inside to a
  top floor at 24.3 m (the town's lookout, one corner standing to 33 m), and the houses round it.
* ``ruin_city_west``, ``ruin_city_east``, ``ruin_city_northeast`` - the quarters either side of the plaza: streets of
  stone houses, some still two storeys with their upper floor and its stair, some broken to the sills, some only stubs
  and rubble.
* ``ruin_city_southwest``, ``ruin_city_southeast`` - the south quarters between the high street and the town wall, and
  (in the south-east model) the gatehouse: a gate block 5 m thick over an arched passage, its top a platform reached by
  a stair from inside the walls.

The town wall runs round the platform's edge, broken down to stubs in long stretches. Streets are flagstones at the
platform's top (0.3 m) and the high street 0.6 m; every marker of the placeholder prefab stands where it stood. Frame:
Blender Z up, front (the gate, the POI's +Z) towards -Y; a marker at POI (x, y, z) is at (x, -z, y) here.
"""
import math
import random

import bpy

import kit as K
import stone_kit as S
from landmarks import tree, bush

Z0 = 0.3                     # the platform's top: streets, plaza and house floors
ZA = 0.6                     # the high street's top
PAD = (-84.0, 84.0, -74.0, 74.0)
# The placeholder's markers in this frame: (x, y, z) of the crates' and the chest's feet.
CRATES = [(0.0, 25.0, 0.3), (-40.0, 15.0, 0.3), (40.0, 20.0, 0.3), (0.0, -40.0, 0.3)]
KEEP_CLEAR = [(0.0, 25.0, 0.8), (-40.0, 15.0, 0.6), (40.0, 20.0, 0.6), (0.0, -40.0, 0.6), (-12.0, -24.0, 0.5), (12.0, -28.0, 0.5),
              (0.0, -70.0, 0.6), (7.0, -70.0, 0.6)]

PIECES = ("centre", "keep", "west", "east", "northeast", "southwest", "southeast")


def region(x, y):
    """Which model a point of the town belongs to."""
    if y <= -34.0:
        return "southwest" if x < 0 else "southeast"
    if abs(x) <= 30.0:
        return "centre"
    if x < 0:
        return "keep" if y > 10.0 else "west"
    return "northeast" if y > 10.0 else "east"


# ---------------------------------------------------------------- the plan
# Streets (flagstones at Z0): x0, x1, y0, y1
STREETS = [
    (-4.0, 4.0, -66.25, -31.0),        # the gate street, north from the gate to the high street
    (-75.5, 75.5, -51.0, -46.0),       # the south cross street
    (-42.0, -37.0, -66.25, -31.0), (37.0, 42.0, -66.25, -31.0),   # the south lanes
    (-5.0, 5.0, -21.0, -14.0),         # the plaza's way from the high street
    (-30.0, -24.0, -21.0, 66.25), (24.0, 30.0, -21.0, 66.25),     # the streets either side of the plaza and temple
    (-75.5, -30.0, 2.0, 8.0), (30.0, 75.5, 2.0, 8.0),             # the cross streets west and east
    (-75.5, 75.5, 56.0, 62.0),         # the north street
]
PLAZA = (-24.0, 24.0, -14.0, 26.0)
AVENUE = (-75.5, 75.5, -31.0, -21.0)
TEMPLE = (-13.0, 13.0, 28.1, 50.0)
KEEP = (-48.0, 40.0, 14.0)            # centre and width

# Houses: x0, y0, x1, y1, storeys, doors, ruin, upper
HOUSES = [
    # west, along the high street and the cross street
    (-74.0, -19.0, -62.0, -9.0, 2, (("s", 0.5),), "half", ("y", -13.0)),
    (-59.0, -19.0, -47.0, -10.0, 1, (("s", 0.35),), "whole", None),
    (-44.0, -19.0, -32.0, -8.0, 2, (("s", 0.6), ("e", 0.5)), "half", ("x", -37.5)),
    (-74.0, -6.0, -62.0, 0.0, 1, (("n", 0.5),), "shell", None),
    (-58.0, -6.0, -46.0, 0.0, 1, (("n", 0.4),), "half", None),
    (-44.0, -6.0, -33.0, 0.0, 1, (("n", 0.5), ("e", 0.5)), "whole", None),
    # north-west, round the keep
    (-46.0, 10.0, -34.0, 21.0, 2, (("s", 0.5),), "half", ("y", 16.75)),     # the crate's house (crate at -40, 15)
    (-74.0, 10.0, -60.0, 22.0, 2, (("s", 0.5),), "half", ("y", 16.5)),
    (-74.0, 26.0, -60.0, 36.0, 1, (("e", 0.5),), "shell", None),
    (-74.0, 40.0, -60.0, 52.0, 1, (("e", 0.4),), "half", None),
    (-56.0, 50.0, -40.0, 54.5, 1, (("n", 0.5),), "shell", None),
    # east
    (32.0, -19.0, 44.0, -9.0, 1, (("s", 0.5),), "whole", None),
    (47.0, -19.0, 61.0, -8.0, 2, (("s", 0.3),), "half", ("x", 54.0)),
    (64.0, -19.0, 75.0, -9.0, 1, (("s", 0.5),), "shell", None),
    (33.0, -6.0, 45.0, 0.0, 1, (("n", 0.5),), "half", None),
    (48.0, -6.0, 60.0, 0.0, 1, (("n", 0.5), ("w", 0.5)), "whole", None),
    (34.0, 13.0, 46.0, 25.0, 1, (("s", 0.5), ("w", 0.6)), "half", None),    # the metal crate's house (crate at 40, 20)
    (50.0, 12.0, 62.0, 24.0, 2, (("w", 0.25),), "half", ("y", 18.5)),
    (65.0, 12.0, 75.0, 22.0, 1, (("w", 0.5),), "shell", None),
    (34.0, 30.0, 46.0, 42.0, 2, (("w", 0.5),), "whole", ("x", 41.0)),
    (50.0, 30.0, 62.0, 40.0, 1, (("s", 0.5),), "half", None),
    (34.0, 45.0, 48.0, 53.0, 1, (("n", 0.5),), "half", None),
    (52.0, 44.0, 66.0, 53.0, 2, (("n", 0.85),), "half", ("x", 59.0)),
    (68.0, 30.0, 75.0, 48.0, 1, (("w", 0.5),), "shell", None),
    # beside the temple
    (-24.0, 30.0, -15.0, 42.0, 1, (("e", 0.5),), "half", None),
    (15.0, 30.0, 24.0, 42.0, 2, (("w", 0.5),), "half", ("y", 36.5)),
    # south of the high street
    (-34.0, -45.0, -22.0, -34.0, 2, (("n", 0.5),), "half", ("y", -39.0)),
    (-19.0, -45.0, -7.0, -35.0, 1, (("e", 0.5),), "half", None),
    (7.0, -45.0, 19.0, -35.0, 1, (("w", 0.5),), "whole", None),
    (22.0, -45.0, 34.0, -34.0, 2, (("n", 0.75),), "half", ("x", 29.0)),
    (45.0, -45.0, 57.0, -34.0, 1, (("n", 0.5),), "half", None),
    (60.0, -45.0, 74.0, -34.0, 1, (("n", 0.5),), "shell", None),
    (-74.0, -45.0, -60.0, -34.0, 1, (("n", 0.5),), "half", None),
    (-57.0, -45.0, -45.0, -35.0, 2, (("n", 0.5),), "whole", ("y", -39.0)),
    # south of the cross street
    (-34.0, -64.0, -20.0, -54.0, 1, (("n", 0.5),), "shell", None),
    (-17.0, -63.0, -7.0, -54.0, 1, (("n", 0.5),), "half", None),
    (10.0, -63.0, 22.0, -54.0, 2, (("w", 0.5),), "half", ("x", 16.5)),
    (25.0, -64.0, 37.0, -54.0, 1, (("n", 0.5),), "half", None),
    (45.0, -64.0, 58.0, -55.0, 1, (("n", 0.5),), "whole", None),
    (-58.0, -64.0, -46.0, -55.0, 1, (("n", 0.5),), "half", None),
    (-74.0, -64.0, -62.0, -54.0, 1, (("n", 0.5),), "shell", None),
    (62.0, -64.0, 74.0, -54.0, 1, (("n", 0.5),), "half", None),
]


class City:
    def __init__(self, seed):
        self.sites = {p: S.Site(f"ruin_city_{p}", seed + k * 101) for k, p in enumerate(PIECES)}
        for site in self.sites.values():
            # a town of hundreds of walls: bigger blocks in 0.75 m courses, and its top blocks drawn plain
            site.loose = site.blocks
            site.course, site.block = 0.75, (1.1, 1.9)
            site.scatter = 0.6
        self.rnd = random.Random(seed)

    def at(self, x, y):
        return self.sites[region(x, y)]


def _clear(x, y, r=1.0):
    return all(math.hypot(x - a, y - b) > r + c for a, b, c in KEEP_CLEAR)


def _free(x, y, pad=0.0):
    """Not on a street, the plaza, the temple, a house or the keep."""
    for x0, x1, y0, y1 in STREETS + [PLAZA, AVENUE, TEMPLE, (-26.0, 26.0, 24.0, 52.0)]:
        if x0 - pad <= x <= x1 + pad and y0 - pad <= y <= y1 + pad:
            return False
    for h in HOUSES:
        if h[0] - pad <= x <= h[2] + pad and h[1] - pad <= y <= h[3] + pad:
            return False
    kx, ky, kw = KEEP
    if abs(x - kx) <= kw / 2 + pad and abs(y - ky) <= kw / 2 + pad:
        return False
    if abs(x) < 14 and y < -60:            # the gatehouse
        return False
    return True


# ---------------------------------------------------------------- ground
def ground(city):
    c = city.sites["centre"]
    x0, x1, y0, y1 = PAD
    # the platform: trodden earth over a stone footing, its top at Z0 (drawn 2 cm under the flags)
    c.blocks.box((x1 - x0, y1 - y0, Z0 - 0.02), ((x0 + x1) / 2, (y0 + y1) / 2, (Z0 - 0.02) / 2), (0, 0, 0), S.ground())
    c.col.box((x1 - x0, y1 - y0, Z0), ((x0 + x1) / 2, (y0 + y1) / 2, Z0 / 2))
    rnd = city.rnd
    # its kerb of big footing stones round the edge
    for (a, b) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        ln = S.Line(a, b)
        u = 0.0
        while u < ln.L - 0.1:
            L = min(rnd.uniform(1.4, 2.2), ln.L - u)
            x, y, _ = ln.at(u + L / 2, 0.3)
            site = city.at(x * 0.97, y * 0.97)
            if rnd.random() > 0.06:
                site.blocks.box((L - 0.04, 0.6, Z0 + 0.04), (x, y, (Z0 + 0.04) / 2 - 0.02), (0, 0, ln.yaw + rnd.uniform(-1, 1)), site.stone_or_dark(0.15))
            if rnd.random() < 0.5:
                S.grass(site, ln.at(u + rnd.uniform(0, L), -0.3, 0.0))
            u += L
    # streets and the plaza
    for x0_, x1_, y0_, y1_ in STREETS + [PLAZA]:
        _pave(city, x0_, x1_, y0_, y1_, Z0)
    # the high street: a step up, a kerb either side
    x0_, x1_, y0_, y1_ = AVENUE
    _pave(city, x0_, x1_, y0_, y1_, ZA, bed=0.3)
    for y in (y0_, y1_):
        u = x0_
        while u < x1_ - 0.1:
            L = min(rnd.uniform(1.2, 1.8), x1_ - u)
            site = city.at(u + L / 2, (y0_ + y1_) / 2)
            site.blocks.box((L - 0.03, 0.4, ZA - Z0 + 0.05), (u + L / 2, y + (0.2 if y == y0_ else -0.2), Z0 + (ZA - Z0 + 0.05) / 2 - 0.02), (0, 0, 0), site.stone_or_dark(0.1))
            u += L


def _pave(city, x0, x1, y0, y1, z, bed=0.0, cell=24.0):
    """Flagstones over a street, in chunks so each lands in its model."""
    nx = max(1, int(math.ceil((x1 - x0) / cell)))
    ny = max(1, int(math.ceil((y1 - y0) / cell)))
    for i in range(nx):
        for j in range(ny):
            a, b = x0 + (x1 - x0) * i / nx, x0 + (x1 - x0) * (i + 1) / nx
            c, d = y0 + (y1 - y0) * j / ny, y0 + (y1 - y0) * (j + 1) / ny
            site = city.at((a + b) / 2, (c + d) / 2)
            S.floor(site, a, b, c, d, z, size=(1.2, 1.8), gone=0.06, lifted=0.03, keep=[(k[0], k[1], k[2]) for k in KEEP_CLEAR],
                    bed=bed, collide=bed > 0, grass_in_gaps=True)


# ---------------------------------------------------------------- the town wall and the gate
def town_wall(city):
    rnd = city.rnd
    t, hw = 1.5, 77.25
    hd = 67.0
    # (a, b, hi, lo, breach) per stretch, split where the models change
    runs = [
        # south, either side of the gatehouse
        ((-hw, -hd), (-30.0, -hd), 5.5, 1.0, True), ((-30.0, -hd), (-13.0, -hd), 6.5, 3.5, False),
        ((13.0, -hd), (30.0, -hd), 6.0, 2.5, False), ((30.0, -hd), (hw, -hd), 4.5, 0.8, True),
        # west, broken at the high street (the west gate) and the cross street
        ((-hw, -hd + 0.75), (-hw, -32.0), 4.5, 1.0, False), ((-hw, -20.0), (-hw, 1.0), 3.5, 0.8, False),
        ((-hw, 9.0), (-hw, 30.0), 5.0, 1.5, True), ((-hw, 30.0), (-hw, hd - 0.75), 6.0, 2.0, False),
        # north
        ((-hw, hd), (-30.0, hd), 5.5, 1.5, True), ((-30.0, hd), (30.0, hd), 3.0, 0.6, True), ((30.0, hd), (hw, hd), 4.5, 0.8, True),
        # east
        ((hw, -hd + 0.75), (hw, -32.0), 3.0, 0.6, True), ((hw, -20.0), (hw, 1.0), 4.0, 1.0, False),
        ((hw, 9.0), (hw, 30.0), 3.5, 0.8, True), ((hw, 30.0), (hw, hd - 0.75), 5.0, 1.0, False),
    ]
    for a, b, hi, lo, breach in runs:
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        site = city.at((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        top = S.broken(L, Z0 + hi, Z0 + lo, rnd, points=max(4, int(L / 8)), breach=Z0 if breach else 0.0)
        S.wall(site, a, b, top, t=t, z0=0.0, ch=0.75, block=(1.0, 1.75), bond=rnd.randrange(2))
        # what fell off it lies along its inner side
        for k in range(int(L / 14)):
            u = rnd.uniform(2, L - 2)
            ln = S.Line(a, b)
            side = 1 if (ln.n.x * -a[0] + ln.n.y * -a[1]) > 0 else -1
            x, y, _ = ln.at(u, side * rnd.uniform(1.6, 2.6))
            if _clear(x, y) and _free(x, y, 0.5):
                S.fallen(site, (x, y, Z0), (rnd.uniform(1.0, 1.6), 0.75, 0.75), lean=rnd.choice((5, 20, 70)), yaw=rnd.uniform(0, 180))
    # corner towers, broken
    for x, y in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
        site = city.at(x * 0.9, y * 0.9)
        h = rnd.uniform(5.5, 8.0)
        S.wall(site, (x - 3.0, y), (x + 3.0, y), S.broken(6.0, Z0 + h, Z0 + h - 2.5, rnd, points=3), t=6.0, z0=0.0, ch=0.75, block=(1.0, 1.75))


def gatehouse(city):
    """The gate block over the gate street: 18 m wide and 5 m thick, an arched passage 6 m wide through it, its top a
    platform 8 m up; two towers either side; a stair up its inner face from the east side of the gate street."""
    site = city.sites["southeast"]
    rnd = site.rnd
    y = -66.5
    top = Z0 + 7.5
    S.wall(site, (-9.0, y), (9.0, y), top, t=5.0, z0=0.0, holes=[(6.0, 12.0, -0.3, Z0 + 3.5, "arch")], ch=0.75, block=(1.0, 1.75))
    for x in (-11.0, 11.0):
        h = 10.5 if x < 0 else 9.0
        S.wall(site, (x - 2.0, y), (x + 2.0, y), S.broken(4.0, Z0 + h, Z0 + h - 1.6, rnd, points=3), t=6.0, z0=0.0, ch=0.75, block=(1.0, 1.75))
    # the passage's floor, and the stair onto the gate's top (a landing at its head)
    S.floor(site, -3.0, 3.0, -69.0, -64.0, Z0, bed=0.0, collide=False, keep=[(0.0, -70.0, 0.6)])
    zt = 7.75
    S.stair(site, (6.5, -52.5, Z0), (6.5, -62.75, zt), 2.5)
    S.rail(site, (7.93, -54.5, Z0 + 1.4), (7.93, -62.75, zt), h=0.9, t=0.35)
    site.cut.box((3.5, 1.3, zt), (6.25, -63.35, zt / 2), (0, 0, 0), site.s)
    site.col.box((3.5, 1.3, zt), (6.25, -63.35, zt / 2))
    S.crystals(site, (-6.5, -66.0, zt), n=5, h=1.4, energy=900)


# ---------------------------------------------------------------- the plaza and the temple
def plaza(city):
    site = city.sites["centre"]
    rnd = site.rnd
    from props_ruins_a import stone_fountain_ruin, stone_obelisk  # noqa: PLC0415
    from props_ruins_b import broken_statue_body, stone_bench_curved  # noqa: PLC0415
    for build, loc, yaw, sc in ((stone_fountain_ruin, (0.0, 4.0, Z0), 0, 2.0), (broken_statue_body, (-13.0, 12.0, Z0), -60, 1.3),
                                (stone_obelisk, (13.0, 12.0, Z0), 10, 1.4), (stone_bench_curved, (-6.0, -6.0, Z0), 200, 1.0),
                                (stone_bench_curved, (7.0, -7.5, Z0), 150, 1.0)):
        p = build()
        p.parent = site.P
        p.location = loc
        p.rotation_euler = (0, 0, math.radians(yaw))
        p.scale = (sc, sc, sc)
    # the colonnade round the plaza's sides, mostly fallen
    for x in (-21.5, 21.5):
        for k, y in enumerate(range(-10, 22, 5)):
            roll = rnd.random()
            if roll < 0.35:
                S.column(site, (x, y, Z0), 6.0, r=0.5)
            elif roll < 0.8:
                S.column(site, (x, y, Z0), 6.0, r=0.5, broken_at=rnd.uniform(1.0, 3.0))
            else:
                site.cut.box((1.2, 1.2, 0.3), (x, y, Z0 + 0.15), (0, 0, 0), site.s)
    # the high street's colonnades, every 8 m where no street joins it
    for y in (-32.0, -20.0):
        for x in range(-70, 71, 8):
            if any(a - 1.5 < x < b + 1.5 and c - 2 < y < d + 2 for a, b, c, d in STREETS if not (a < -70 and b > 70)):
                continue
            if not _clear(x, y, 1.0):
                continue
            s_ = city.at(x, -26.0)
            roll = rnd.random()
            if roll < 0.3:
                S.column(s_, (x, y, Z0), 5.5, r=0.45)
            elif roll < 0.85:
                S.column(s_, (x, y, Z0), 5.5, r=0.45, broken_at=rnd.uniform(0.8, 2.6))
    # rubble and fallen stones about the plaza, clear of the chest
    for k in range(6):
        x, y = rnd.uniform(-18, 18), rnd.uniform(-11, 22)
        if _clear(x, y, 2.0) and math.hypot(x, y - 4.0) > 5.5:
            S.fallen(site, (x, y, Z0), (rnd.uniform(0.8, 1.4), 0.7, 0.6), lean=rnd.choice((5, 25, 70)), yaw=rnd.uniform(0, 180))


def temple(city):
    site = city.sites["centre"]
    rnd = site.rnd
    x0, x1, y0, y1 = TEMPLE
    zp = Z0 + 1.5
    # the podium: courses round a solid core, its flagged top, steps the whole width of its front
    for a, b in (((x0, y0 + 0.5), (x1, y0 + 0.5)), ((x1, y1 - 0.5), (x0, y1 - 0.5))):
        S.wall(site, a, b, zp - 0.05, t=1.0, z0=0.0)
    for a, b in (((x0 + 0.5, y1 - 1.0), (x0 + 0.5, y0 + 1.0)), ((x1 - 0.5, y0 + 1.0), (x1 - 0.5, y1 - 1.0))):
        S.wall(site, a, b, zp - 0.05, t=1.0, z0=0.0, bond=1)
    site.blocks.box((x1 - x0, y1 - y0, 0.4), ((x0 + x1) / 2, (y0 + y1) / 2, zp - 0.21), (0, 0, 0), site.mo)
    site.col.box((x1 - x0, y1 - y0, zp), ((x0 + x1) / 2, (y0 + y1) / 2, zp / 2))
    S._flag_top(site, x0, x1, y0, y1, zp, size=1.5)
    S.stair(site, (0.0, 26.0, Z0), (0.0, y0 + 0.02, zp), 16.0)
    # the columns round it: six across the front, four down each side
    cols = [(x, y0 + 1.6) for x in (-10.5, -6.3, -2.1, 2.1, 6.3, 10.5)] + [(x, y) for x in (x0 + 1.6, x1 - 1.6) for y in (33.0, 38.0, 43.0, 48.0)]
    standing = {(-6.3, y0 + 1.6), (-2.1, y0 + 1.6), (10.5, y0 + 1.6), (x0 + 1.6, 33.0), (x1 - 1.6, 43.0), (x1 - 1.6, 48.0)}
    for x, y in cols:
        if (x, y) in standing:
            S.column(site, (x, y, zp), 7.5, r=0.55)
        else:
            S.column(site, (x, y, zp), 7.5, r=0.55, broken_at=rnd.uniform(1.0, 4.0))
    # the beam still lying across the two standing front columns, another fallen in front
    site.cut.box((5.6, 1.2, 1.0), (-4.2, y0 + 1.6, zp + 7.5 + 0.5), (0, 0, 0), site.sd)
    site.col.box((5.6, 1.2, 1.0), (-4.2, y0 + 1.6, zp + 7.5 + 0.5))
    S.fallen(site, (5.0, 31.6, zp), (4.4, 1.1, 0.9), lean=4, yaw=12)
    # the cella: roofless, its front a great arched door, crystals grown out of the altar
    cx0, cx1, cy0, cy1, t = -8.0, 8.0, 33.0, 49.0, 1.0
    hi = zp + 8.5
    S.wall(site, (cx0, cy0 + t / 2), (cx1, cy0 + t / 2), S.broken(16.0, hi, zp + 5.0, rnd, points=4), t=t, z0=zp - 0.3,
           holes=[(6.25, 9.75, zp - 0.3, zp + 3.5, "arch")], plinth=False)
    S.wall(site, (cx1, cy1 - t / 2), (cx0, cy1 - t / 2), S.broken(16.0, hi, zp + 6.0, rnd, points=4), t=t, z0=zp - 0.3, plinth=False)
    S.wall(site, (cx0 + t / 2, cy1 - t), (cx0 + t / 2, cy0 + t), S.broken(14.0, hi - 1.0, zp + 2.0, rnd, points=5), t=t, z0=zp - 0.3, bond=1,
           holes=[S.window(4.0, zp, sill=2.5, arch=True), S.window(10.0, zp, sill=2.5, arch=True)], plinth=False)
    S.wall(site, (cx1 - t / 2, cy0 + t), (cx1 - t / 2, cy1 - t), S.broken(14.0, hi, zp + 3.5, rnd, points=4, breach=zp), t=t, z0=zp - 0.3, bond=1,
           plinth=False)
    site.cut.box((3.0, 1.8, 1.2), (0.0, 45.0, zp + 0.6), (0, 0, 0), site.sd)
    site.col.box((3.0, 1.8, 1.2), (0.0, 45.0, zp + 0.6))
    S.crystals(site, (0.0, 45.0, zp + 1.2), n=9, h=2.8, energy=2500)
    S.rubble(site, (-5.0, 46.0, zp), 2.4, 2.0, 1.4)
    S.rubble(site, (6.0, 36.0, zp), 1.6, 1.6, 0.8)


# ---------------------------------------------------------------- the keep
def keep(city):
    site = city.sites["keep"]
    cx, cy, w = KEEP
    S.tower(site, cx, cy, w, Z0, Z0 + 24.0, t=1.2, width=1.75, start=1, door_side="e", broken_sides=("w",),
            spike=("s", 33.0, 0.2), big=True, oculus=2.5)
    S.crystals(site, (cx + 4.6, cy + 4.6, Z0 + 24.0), n=6, h=1.8, energy=1500)
    S.rubble(site, (cx - 9.0, cy + 2.0, Z0), 2.6, 4.0, 1.5)


# ---------------------------------------------------------------- the houses and what lies between
def houses(city):
    for k, (x0, y0, x1, y1, storeys, doors, ruin, upper) in enumerate(HOUSES):
        site = city.at((x0 + x1) / 2, (y0 + y1) / 2)
        keep = [(c[0], c[1], c[2]) for c in KEEP_CLEAR if x0 <= c[0] <= x1 and y0 <= c[1] <= y1]
        S.house(site, x0, y0, x1, y1, z0=Z0, storeys=storeys, t=0.75, doors=doors, ruin=ruin, upper=upper, keep=keep, seed=500 + k,
                arch_doors=(k % 3 == 0))
        rnd = random.Random(900 + k)
        # what fell from it: a heap against one wall outside, fallen blocks
        if ruin != "whole":
            side = rnd.choice(("n", "s", "e", "w"))
            cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
            px, py = {"n": (cx + rnd.uniform(-2, 2), y1 + 1.4), "s": (cx + rnd.uniform(-2, 2), y0 - 1.4),
                      "e": (x1 + 1.4, cy + rnd.uniform(-2, 2)), "w": (x0 - 1.4, cy + rnd.uniform(-2, 2))}[side]
            if _clear(px, py, 2.5) and not any(a - 1.0 < px < b + 1.0 and c - 1.0 < py < d + 1.0 for a, b, c, d in [PLAZA, AVENUE]):
                S.rubble(site, (px, py, Z0), 2.0, 1.4, 0.7, yaw=0 if side in ("n", "s") else 90)


def lots(city):
    """The empty lots between the houses: rubble, fallen blocks, grass, bushes and a few trees grown up in the town."""
    rnd = random.Random(77)
    placed = 0
    for k in range(1400):
        x, y = rnd.uniform(-75, 75), rnd.uniform(-65, 65)
        if not _free(x, y, 0.6) or not _clear(x, y, 1.0):
            continue
        site = city.at(x, y)
        roll = rnd.random()
        if roll < 0.62:
            S.grass(site, (x, y, Z0))
        elif roll < 0.72:
            S.fallen(site, (x, y, Z0), (rnd.uniform(0.8, 1.5), 0.7, 0.6), lean=rnd.choice((5, 20, 70)), yaw=rnd.uniform(0, 180))
        elif roll < 0.76 and _free(x, y, 2.5):
            S.rubble(site, (x, y, Z0), rnd.uniform(1.4, 2.4), rnd.uniform(1.2, 2.0), rnd.uniform(0.5, 1.0))
        elif roll < 0.79 and _free(x, y, 2.0):
            bush(site.P, "bush", (x, y, Z0), rnd.uniform(0.7, 1.2), rnd, shade=k)
        elif roll < 0.8 and _free(x, y, 3.0) and placed < 12:
            tree(site.P, "tree", (x, y, Z0), rnd.uniform(7.0, 10.0), rnd, crown=2.2)
            placed += 1
    # grass along the streets' edges
    for x0, x1, y0, y1 in STREETS:
        for _ in range(int((x1 - x0 + y1 - y0) / 3)):
            x, y = (rnd.uniform(x0, x1), rnd.choice((y0, y1))) if x1 - x0 > y1 - y0 else (rnd.choice((x0, x1)), rnd.uniform(y0, y1))
            if _clear(x, y):
                S.grass(city.at(x, y), (x, y, Z0))


def ruin_city(seed=40):
    """Builds the town; returns its seven models' roots by piece name (all at the town's origin)."""
    city = City(seed)
    ground(city)
    town_wall(city)
    gatehouse(city)
    plaza(city)
    temple(city)
    keep(city)
    houses(city)
    lots(city)
    return {p: s.done() for p, s in city.sites.items()}
