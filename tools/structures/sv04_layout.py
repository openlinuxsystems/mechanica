"""Where the SV-04's wreck lies at the crash site: shared by crash_site.py (the models), build_structures.py (their
export), tools/master_battery/build_master_battery.py (the Power Core in engineering) and print_crash_layout.py (the
numbers mods/vanilla/pois/crash_site.json and game/data/opening.json take). Plain Python: no Blender needed.

Frames: the ship's own (Blender: x starboard, y towards the bow, z up; origin on the deck line at mid-ship), and the
wreck's, which is the crash site's centre on its pad turned by WRECK_YAW (Blender axes too). A POI gives places in its
own frame: x east, y up, z south; a model's yaw turns it from north (-z) towards west.
"""

import math

# the spine snaps at the middle rack post; these containers stay crushed in their racks, the rest fell in the sky
SPINE_BREAK = 1.0
KEEP = ("SV4-112", "SV4-131", "SV4-215", "SV4-309")

# Each piece turns about its pivot (its middle on the ship's deck line) and comes to rest with the pivot at the given
# place in the wreck's frame, rotated (deg, XYZ). The aft block hit first and stopped soonest; the bow slid on furthest.
# Their bellies are dug into the furrow: the aft block and the bow lie nearly level, so a player steps in at the aft
# block's starboard door (deck 0.3 m up) and the bow's airlock (0.4 m, with its steps), and no deck dips under the ground.
PIVOTS = {"aft": (0.0, -21.0, 0.0), "spine_a": (0.0, -5.5, 0.0), "spine_b": (0.0, 7.5, 0.0), "bow": (0.0, 23.0, 0.0)}
PIECES = {
    "aft": ((-7.0, -27.0, 0.55), (0.8, 3.0, 16.0)),
    "spine_a": ((-2.0, -9.5, 1.3), (-3.0, -38.0, 38.0)),
    "spine_b": ((3.5, 4.0, 1.05), (2.0, 14.0, -22.0)),
    "bow": ((6.0, 21.5, 0.55), (-1.5, 3.0, -9.0)),
}
# The port engine housing tore off in the explosion and lies upside-down off the port side of the furrow: its own
# model, centred on HOUSING_CENTRE (ship frame), lying at HOUSING_LYING (wreck frame, deg XYZ).
HOUSING_CENTRE = (-4.3, -24.1, 6.65)
HOUSING_LYING = ((-20.0, -26.0, 2.0), (170.0, 12.0, 40.0))

# Fires (key, the piece it burns on, its place in the ship's frame, size): the three at the ways in (the bow's starboard
# airlock, the aft block's starboard door and its torn front) must be put out to get in; three more burn inside.
FIRES = [
    ("airlock", "bow", (7.6, 16.6, 0.0), 1.2),
    ("mess_door", "aft", (8.4, -15.0, 0.0), 1.1),
    ("spine_end", "aft", (0.0, -11.2, 0.0), 1.3),
    ("engine_room", "aft", (2.5, -27.5, 0.0), 0.9),
    ("bridge", "bow", (3.0, 23.6, 0.0), 0.7),
    ("spine_a", "spine_a", (0.0, -6.0, 0.0), 0.9),
]

# The Power Core in engineering (its foot on the deck, front towards +y): tools/master_battery builds it.
BAY_AT = (-4.6, -22.45, 0.04)

# The wreck on the site: turned so the bow points north-west, the furrow (and the containers) trailing back south-east.
WRECK_YAW = 35.0

# The rack slots: four racks along the spine, each with a lower and upper container either side.
RACK_CODES = (112, 118, 120, 131, 207, 215, 233, 240, 301, 309, 316, 322, 405, 411, 420, 438)
RACKS = [("red", "tan", "blue", "red"), ("green", "red", "yellow", "tan"),
         ("blue", "yellow", "red", "green"), ("tan", "blue", "green", "yellow")]
S0, S1 = -12.0, 14.0


def slots():
    """Every container slot on the intact ship: code -> (colour, centre in the ship's frame, tier)."""
    posts = [S0 + 0.2 + k * 6.4 for k in range(5)]
    centres = [(posts[k] + posts[k + 1]) / 2 for k in range(4)]
    codes = iter(f"SV4-{n:03d}" for n in RACK_CODES)
    out = {}
    for k, (pl, pu, sl, su) in enumerate(RACKS):
        for side, lo, up in ((-1, pl, pu), (1, sl, su)):
            for tier, col, z in (("lower", lo, -0.3), ("upper", up, 2.35)):
                out[next(codes)] = (col, (side * 3.95, centres[k], z), tier)
    return out


# The containers lost in the sky, where each burst open (wreck frame: x, y, yaw deg), back along the flight path (-y)
# and out to the sides. The game places them as POI models with the cargo they spilled round them.
SCATTER = [
    ("SV4-118", -42.3, -29.0, 30.0), ("SV4-120", -28.8, -48.2, -50.0), ("SV4-207", 10.0, -72.9, 75.0),
    ("SV4-233", -63.9, -28.5, 10.0), ("SV4-240", -50.6, -54.9, -20.0), ("SV4-301", 28.5, -63.9, 120.0),
    ("SV4-316", 46.2, -42.1, 200.0), ("SV4-322", -30.0, -74.2, 60.0), ("SV4-405", 45.1, -19.3, -70.0),
    ("SV4-411", -63.7, -45.7, 160.0), ("SV4-420", -68.3, -3.4, 15.0), ("SV4-438", -3.6, -85.4, -110.0),
]
CONTAINER_MIDDLE = 1.05   # the burst container's middle above its model's origin


def rot_xyz(rx, ry, rz):
    """Rotation matrix (rows) for Blender's XYZ Euler order (degrees)."""
    a, b, c = (math.radians(v) for v in (rx, ry, rz))
    ca, sa, cb, sb, cc, sc = math.cos(a), math.sin(a), math.cos(b), math.sin(b), math.cos(c), math.sin(c)
    rxm = ((1, 0, 0), (0, ca, -sa), (0, sa, ca))
    rym = ((cb, 0, sb), (0, 1, 0), (-sb, 0, cb))
    rzm = ((cc, -sc, 0), (sc, cc, 0), (0, 0, 1))
    return mul(rzm, mul(rym, rxm))


def mul(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))


def apply(m, p):
    return tuple(sum(m[i][k] * p[k] for k in range(3)) for i in range(3))


def piece_point(piece, p):
    """A point given in the intact ship's frame, on piece, in the wreck's frame."""
    loc, rot = PIECES[piece]
    q = apply(rot_xyz(*rot), tuple(p[i] - PIVOTS[piece][i] for i in range(3)))
    return tuple(q[i] + loc[i] for i in range(3))


def to_poi(p):
    """A wreck-frame point (Blender axes) in the POI's frame (x east, y up, z south)."""
    x, z = p[0], -p[1]
    s, c = math.sin(math.radians(WRECK_YAW)), math.cos(math.radians(WRECK_YAW))
    return (x * c + z * s, p[2], -x * s + z * c)
