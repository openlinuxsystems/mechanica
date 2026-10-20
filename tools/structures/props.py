"""Props for the scenes between places (docs/world-generation.md, Scenes): small things a settler or the old colony left
lying about, in the families of docs/art-direction.md.

Settler Patchwork: rail fences, a broken handcart, hay bales, a scarecrow, a water trough, a rusty barrel, a log pile,
a tarp lean-to and a signpost. Colony Works: large pipe runs, a valve end, a wrecked harvester, a concrete barrier and a
cable spool. The burned forest: a scorched trailer. Nature: a tree stump.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy

import kit as K
import frame as FR
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, WOOD, ZINC, CONCRETE

STRAW, STRAW_D = "#d8b45a", "#b8913f"
CHAR, CHAR_D = "#2a2622", "#1a1715"
BARK, HEART = "#6e4a2e", "#c9a06a"


def _root(name):
    return K.link(bpy.data.objects.new(name, None))


def wood(shade=0, wear=0.5):
    return K.mat(f"pwood{shade}", ["#9c7650", "#8a6644", "#a9845a", "#7d5c3e"][shade % 4], rough=0.85, wear=wear, grime=0.35,
                 noise_scale=2.0, scratches=0.3)


def plank(P, name, size, loc, rot=(0, 0, 0), shade=0):
    return K.box(name, size, wood(shade), loc=loc, rot=rot, bev=0.012, parent=P)


def post(P, name, loc, h, r=0.07, tilt=(0, 0), shade=3):
    x, y = loc
    tx, ty = tilt
    return K.box(name, (r * 2, r * 2, h), wood(shade), loc=(x + math.sin(math.radians(ty)) * h / 2, y - math.sin(math.radians(tx)) * h / 2, h / 2 - 0.1),
                 rot=(tx, ty, 0), bev=0.015, parent=P)


def bolt(P, loc, rot=(90, 0, 0)):
    FR.screw(P, loc, rot, r=0.025, mat=M(ZINC, metal=0.6, wear=0.6))


# ================================================================== Settler Patchwork
def fence_rail(broken=False, seed=1):
    """A 2.4 m section of rail fence: two posts, two rails, one patched with a salvaged board."""
    rnd = random.Random(seed)
    P = _root("fence")
    post(P, "post_l", (-1.15, 0), 1.25)
    if broken:
        post(P, "post_r", (1.2, 0.05), 1.0, tilt=(0, 28))
        # the top rail hangs from the left post, the lower one lies in the grass
        plank(P, "rail_top", (2.3, 0.05, 0.12), (0.0, -0.08, 0.72), rot=(0, 16, 0), shade=1)
        plank(P, "rail_down", (2.2, 0.12, 0.05), (0.1, -0.45, 0.03), rot=(0, 0, rnd.uniform(-12, -4)), shade=2)
    else:
        post(P, "post_r", (1.15, 0), 1.25)
        plank(P, "rail_top", (2.5, 0.05, 0.12), (0, -0.09, 0.95), shade=1)
        plank(P, "rail_low", (2.5, 0.05, 0.12), (0, -0.09, 0.48), shade=2)
        # a patch: a shorter board of another colour nailed over a split in the lower rail
        K.box("patch", (0.6, 0.03, 0.16), M(SEA, wear=0.7, grime=0.4), loc=(0.35, -0.13, 0.48), rot=(0, rnd.uniform(-4, 4), 0), bev=0.008, parent=P)
        for x in (0.12, 0.58):
            bolt(P, (x, -0.15, 0.48))
    for x in (-1.15, 1.15):
        bolt(P, (x, -0.12, 0.95))
    return P


def cart_broken(seed=4):
    """A settler handcart tipped on its broken axle: plank bed, metal-sheet sides, one wheel off in the grass."""
    rnd = random.Random(seed)
    P = _root("cart")
    # built at hand-cart size, shown as a full farm wagon so it reads at a distance next to the player
    G = K.link(bpy.data.objects.new("cart_scale", None))
    G.parent = P
    G.scale = (1.5, 1.5, 1.5)
    tip = K.link(bpy.data.objects.new("bed", None))
    tip.parent = G
    tip.location = (0, 0, 0.38)
    tip.rotation_euler = (math.radians(-11), math.radians(5), 0)
    for k in range(5):
        plank(tip, f"bed{k}", (1.25, 0.4, 0.06), (0, -0.85 + k * 0.42, 0), shade=k)
    for s in (-1, 1):
        K.box(f"side{s}", (0.05, 2.1, 0.45), M(rnd.choice([ZINC, BRICK, SEA]), metal=0.4, wear=0.7, grime=0.5), loc=(s * 0.64, 0, 0.24), bev=0.01, parent=tip)
        K.box(f"rib{s}", (0.08, 0.08, 0.5), wood(3), loc=(s * 0.68, -0.6, 0.22), bev=0.01, parent=tip)
        K.box(f"rib2{s}", (0.08, 0.08, 0.5), wood(3), loc=(s * 0.68, 0.6, 0.22), bev=0.01, parent=tip)
        # shafts to pull it by, resting on the ground in front
        K.box(f"shaft{s}", (0.07, 1.6, 0.07), wood(2), loc=(s * 0.42, -1.75, -0.2), rot=(12, 0, 0), bev=0.01, parent=tip)
    K.box("tail", (1.25, 0.05, 0.4), wood(1), loc=(0, 1.05, 0.22), bev=0.01, parent=tip)
    K.cyl("axle", 0.045, 1.45, M(GRAPHITE, metal=0.6, wear=0.5), loc=(0, 0.15, -0.12), rot=(0, 90, 0), segs=10, parent=tip)

    def wheel(name, loc, rot):
        w = K.link(bpy.data.objects.new(name, None))
        w.parent = G
        w.location = loc
        w.rotation_euler = [math.radians(a) for a in rot]
        K.torus(f"{name}_rim", 0.42, 0.045, M(RUST, metal=0.5, wear=0.6), rot=(90, 0, 0), segs=24, parent=w)
        K.cyl(f"{name}_hub", 0.08, 0.14, M(GRAPHITE, metal=0.5), rot=(90, 0, 0), segs=12, parent=w)
        for k in range(6):
            a = k * 60
            K.box(f"{name}_spoke", (0.04, 0.03, 0.4), wood(3), loc=(math.sin(math.radians(a)) * 0.2, 0, math.cos(math.radians(a)) * 0.2), rot=(0, a, 0), bev=0, parent=w)
        return w

    wheel("wheel_on", (0.72, 0.15, 0.42), (0, 0, 90))
    wheel("wheel_off", (-1.35, 0.9, 0.05), (90, 0, 20))
    # a sack still in the bed
    K.sphere("sack", 0.3, M("#c2a878", rough=0.95, grime=0.4), loc=(0.15, 0.35, 0.62), scale=(1.2, 0.9, 0.6), parent=G)
    return P


def hay_bale(seed=2):
    """A round bale lying on its side, tied with two straps."""
    P = _root("hay")
    straw = K.mat("straw", STRAW, rough=0.95, grime=0.25, noise_scale=12.0)
    K.cyl("bale", 0.72, 1.2, straw, loc=(0, 0, 0.7), rot=(0, 90, 0), segs=20, bev=0.12, parent=P)
    K.cyl("face_l", 0.62, 0.02, K.mat("strawd", STRAW_D, rough=0.95, noise_scale=20.0), loc=(-0.61, 0, 0.7), rot=(0, 90, 0), segs=20, bev=0, parent=P)
    K.cyl("face_r", 0.62, 0.02, K.mat("strawd", STRAW_D, rough=0.95, noise_scale=20.0), loc=(0.61, 0, 0.7), rot=(0, 90, 0), segs=20, bev=0, parent=P)
    for x in (-0.3, 0.3):
        K.torus(f"strap{x}", 0.735, 0.015, M(SIGNAL, rough=0.6), loc=(x, 0, 0.7), rot=(0, 90, 0), segs=24, parent=P)
    return P


def hay_bale_stack(seed=3):
    """Three square bales, two below and one on top, slightly askew."""
    rnd = random.Random(seed)
    P = _root("haystack")
    straw = K.mat("straw", STRAW, rough=0.95, grime=0.25, noise_scale=12.0)
    for k, (x, z, yaw) in enumerate(((-0.5, 0.25, rnd.uniform(-5, 5)), (0.5, 0.25, rnd.uniform(-5, 5)), (0.05, 0.75, rnd.uniform(-14, 14)))):
        K.box(f"bale{k}", (0.95, 0.5, 0.5), straw, loc=(x, 0, z), rot=(0, 0, yaw), bev=0.06, parent=P)
        for t in (-0.25, 0.25):
            K.box(f"twine{k}", (0.02, 0.52, 0.52), M(SIGNAL, rough=0.7), loc=(x + t * math.cos(math.radians(yaw)), t * math.sin(math.radians(yaw)), z),
                  rot=(0, 0, yaw), bev=0, parent=P)
    return P


def scarecrow(seed=5):
    """A scarecrow of salvage: a post and crossbar, a patched coat, a bucket for a head, a dead lamp for an eye."""
    P = _root("scarecrow")
    post(P, "pole", (0, 0), 2.4, r=0.05)
    K.box("arms", (1.6, 0.08, 0.08), wood(3), loc=(0, 0, 1.75), bev=0.012, parent=P)
    coat = M(SEA, wear=0.6, grime=0.5, rough=0.9)
    K.box("coat", (0.62, 0.3, 0.75), coat, loc=(0, 0, 1.45), bev=0.08, parent=P)
    K.box("patch", (0.2, 0.02, 0.2), M(MUSTARD, wear=0.5, rough=0.9), loc=(0.12, -0.16, 1.4), rot=(0, 8, 0), bev=0.01, parent=P)
    for s in (-1, 1):
        K.box(f"sleeve{s}", (0.55, 0.22, 0.22), coat, loc=(s * 0.55, 0, 1.72), rot=(0, s * -6, 0), bev=0.05, parent=P)
        K.cyl(f"straw{s}", 0.08, 0.18, K.mat("straw", STRAW, rough=0.95, noise_scale=12.0), loc=(s * 0.88, 0, 1.68), rot=(0, 90, 0), segs=8, r2=0.12, parent=P)
    K.cyl("head", 0.17, 0.32, M(ZINC, metal=0.5, wear=0.7, grime=0.4), loc=(0, 0, 2.08), segs=16, r2=0.13, parent=P)
    K.torus("handle", 0.15, 0.012, M(GRAPHITE, metal=0.6), loc=(0, 0, 2.25), rot=(0, 90, 0), arc=180, segs=12, parent=P)
    K.cyl("eye", 0.045, 0.04, K.mat("deadlamp", "#7d8f96", rough=0.2, metal=0.3), loc=(-0.06, -0.15, 2.1), rot=(90, 0, 0), segs=10, parent=P)
    K.box("mouth", (0.14, 0.02, 0.025), M(GRAPHITE), loc=(0.02, -0.15, 2.0), bev=0, parent=P)
    return P


def water_trough(seed=6):
    """A long metal trough on wooden trestles, rust along its seams, a little rainwater inside."""
    P = _root("trough")
    tin = M(ZINC, metal=0.55, wear=0.6, grime=0.5)
    L, W, H = 2.0, 0.7, 0.45
    K.box("bottom", (L, W, 0.05), tin, loc=(0, 0, 0.42), bev=0.01, parent=P)
    for s in (-1, 1):
        K.box(f"wall{s}", (L, 0.04, H), tin, loc=(0, s * W / 2, 0.42 + H / 2), rot=(s * 8, 0, 0), bev=0.01, parent=P)
        K.box(f"end{s}", (0.04, W, H), tin, loc=(s * L / 2, 0, 0.42 + H / 2), bev=0.01, parent=P)
        K.box(f"seam{s}", (0.06, W + 0.06, 0.06), M(RUST, metal=0.4, wear=0.5), loc=(s * L / 2, 0, 0.88), bev=0.01, parent=P)
        for t in (-1, 1):
            K.box(f"leg{s}{t}", (0.08, 0.08, 0.5), wood(3), loc=(s * 0.7, t * 0.28, 0.22), rot=(t * 10, 0, 0), bev=0.01, parent=P)
        K.box(f"cross{s}", (0.08, 0.75, 0.08), wood(2), loc=(s * 0.7, 0, 0.38), bev=0.01, parent=P)
    K.box("water", (L - 0.1, W - 0.08, 0.02), K.mat("trough_water", "#3d6f78", rough=0.05, metal=0.0), loc=(0, 0, 0.7), bev=0, parent=P)
    return P


def barrel_rusty(seed=7):
    """An oil drum gone to rust, its old paint flaking, a dent in its side."""
    P = _root("barrel")
    rnd = random.Random(seed)
    paint = M(rnd.choice([STEEL, BRICK, SEA]), metal=0.4, wear=0.85, grime=0.6, scratches=0.6)
    K.cyl("drum", 0.3, 0.88, paint, loc=(0, 0, 0.44), segs=20, bev=0.02, parent=P)
    for z in (0.05, 0.3, 0.6, 0.85):
        K.torus(f"hoop{z}", 0.305, 0.015, M(RUST, metal=0.5, wear=0.6), loc=(0, 0, z), segs=24, parent=P)
    K.cyl("lid", 0.27, 0.02, M(RUST, metal=0.5), loc=(0, 0, 0.885), segs=20, bev=0, parent=P)
    K.cyl("bung", 0.04, 0.03, M(GRAPHITE, metal=0.6), loc=(0.15, 0.05, 0.9), segs=10, bev=0, parent=P)
    return P


def log_pile(seed=8):
    """Split logs stacked between stakes, the ends pale where they were cut."""
    rnd = random.Random(seed)
    P = _root("logs")
    bark = K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    cut = K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)
    rows = [(5, 0.0), (4, 0.26), (3, 0.52), (2, 0.78)]
    for n, z in rows:
        for k in range(n):
            x = (k - (n - 1) / 2) * 0.3 + (0.15 if n % 2 == 0 else 0) * 0
            r = rnd.uniform(0.12, 0.15)
            L = rnd.uniform(1.4, 1.6)
            y = rnd.uniform(-0.08, 0.08)
            K.cyl(f"log{n}{k}", r, L, bark, loc=(x, y, z + r), rot=(90, 0, 0), segs=10, bev=0.01, parent=P)
            for s in (-1, 1):
                K.cyl(f"end{n}{k}{s}", r * 0.85, 0.02, cut, loc=(x, y + s * L / 2, z + r), rot=(90, 0, 0), segs=10, bev=0, parent=P)
    for s in (-1, 1):
        post(P, f"stake{s}", (s * 0.85, 0), 1.2, r=0.05, tilt=(0, s * -6))
    return P


def tarp_shelter(seed=9):
    """A lean-to of poles with a patched tarp, a bedroll and a cold fire ring in front."""
    rnd = random.Random(seed)
    P = _root("shelter")
    for s in (-1, 1):
        post(P, f"front{s}", (s * 1.3, -0.6), 1.9, r=0.05)
        post(P, f"back{s}", (s * 1.3, 0.9), 0.6, r=0.05)
        K.box(f"brace{s}", (0.06, 1.65, 0.06), wood(3), loc=(s * 1.3, 0.15, 1.15), rot=(-42, 0, 0), bev=0.01, parent=P)
    K.box("ridge", (2.8, 0.07, 0.07), wood(2), loc=(0, -0.6, 1.78), bev=0.01, parent=P)
    cols = [SEA, MUSTARD, BRICK, ZINC]
    for k in range(4):
        x = -1.05 + k * 0.7
        K.box(f"tarp{k}", (0.74, 1.9, 0.02), M(cols[(k + seed) % 4], wear=0.6, rough=0.9, grime=0.4), loc=(x, 0.15, 1.18 + rnd.uniform(-0.01, 0.01)),
              rot=(-41, rnd.uniform(-1, 1), 0), bev=0, parent=P)
    K.box("bedroll", (0.7, 1.8, 0.14), M("#6f7a5a", rough=0.95, grime=0.4), loc=(0.4, 0.2, 0.07), bev=0.05, parent=P)
    for k in range(7):
        a = 2 * math.pi * k / 7
        K.sphere(f"ring{k}", 0.12, M("#80827a", rough=0.9), loc=(-0.6 + math.cos(a) * 0.38, -1.4 + math.sin(a) * 0.38, 0.05), scale=(1, 1, 0.7), parent=P)
    K.disc("ash", 0.3, K.mat("ash", "#3a3530", rough=1.0), (-0.6, -1.4), z=0.02, segs=12).parent = P
    return P


def signpost(seed=10):
    """A post with three hand-painted arrow boards pointing different ways."""
    rnd = random.Random(seed)
    P = _root("sign")
    post(P, "pole", (0, 0), 2.5, r=0.06)
    for k, (z, yaw, col) in enumerate(((2.1, 20, BONE), (1.75, 145, MUSTARD), (1.42, -70, SEA))):
        arm = K.link(bpy.data.objects.new(f"arm{k}", None))
        arm.parent = P
        arm.location = (0, 0, z)
        arm.rotation_euler = (0, 0, math.radians(yaw))
        bm = bmesh.new()
        pts = [(0.05, -0.1), (0.75, -0.1), (0.9, 0.0), (0.75, 0.1), (0.05, 0.1)]
        bot = [bm.verts.new((x, -0.015, y)) for x, y in pts]
        top = [bm.verts.new((x, 0.015, y)) for x, y in pts]
        bm.faces.new(bot)
        bm.faces.new(list(reversed(top)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new((bot[i], bot[j], top[j], top[i]))
        K.place(K.from_bm(f"board{k}", bm, M(col, wear=0.6, rough=0.85, grime=0.3)), parent=arm)
        K.box(f"stripe{k}", (0.45, 0.034, 0.03), M(GRAPHITE, rough=0.8), loc=(0.42, 0, rnd.uniform(-0.03, 0.03)), bev=0, parent=arm)
        bolt(P, (0, -0.07, z), rot=(90, 0, 0))
    return P


def stump(seed=11):
    """A tree stump cut at an angle, its roots gripping the ground."""
    rnd = random.Random(seed)
    P = _root("stump")
    bark = K.mat("bark", BARK, rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    cut = K.mat("heart", HEART, rough=0.9, noise_scale=10.0, cracks=0.3)
    K.cyl("trunk", 0.38, 0.6, bark, loc=(0, 0, 0.28), segs=14, r2=0.33, bev=0.03, parent=P)
    K.cyl("top", 0.31, 0.03, cut, loc=(0, 0, 0.58), rot=(6, 0, 0), segs=14, bev=0, parent=P)
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        K.cyl(f"root{k}", 0.12, 0.7, bark, loc=(math.cos(a) * 0.45, math.sin(a) * 0.45, 0.07),
              rot=(0, 75, math.degrees(a)), segs=8, r2=0.04, bev=0.01, parent=P)
    return P


# ================================================================== Colony Works
def colony_pipe(length=4.0, seed=12):
    """A run of the old colony's big bone-white pipe, half sunk in the ground, steel flanges and a mustard band."""
    P = _root("pipe")
    shell = M(BONE, wear=0.55, grime=0.6, moss=0.15)
    steel = M(STEEL, metal=0.4, wear=0.5)
    r = 0.6
    K.cyl("pipe", r, length, shell, loc=(0, 0, r * 0.55), rot=(0, 90, 0), segs=24, bev=0.02, parent=P)
    for s in (-1, 1):
        K.cyl(f"flange{s}", r + 0.08, 0.16, steel, loc=(s * (length / 2 - 0.08), 0, r * 0.55), rot=(0, 90, 0), segs=24, bev=0.02, parent=P)
        for k in range(8):
            a = 2 * math.pi * k / 8
            FR.screw(P, (s * (length / 2), math.cos(a) * (r + 0.04), r * 0.55 + math.sin(a) * (r + 0.04)), (0, 90, 0), r=0.035,
                     mat=M(ZINC, metal=0.6))
    K.cyl("band", r + 0.012, 0.3, M(MUSTARD, wear=0.5), loc=(0.6, 0, r * 0.55), rot=(0, 90, 0), segs=24, bev=0, parent=P)
    return P


def colony_pipe_valve(seed=13):
    """The end of a pipe run: an elbow up into a valve housing with a wheel and a warning plate."""
    P = _root("valve")
    shell = M(BONE, wear=0.55, grime=0.6, moss=0.1)
    steel = M(STEEL, metal=0.4, wear=0.5)
    r = 0.6
    K.cyl("stub", r, 1.4, shell, loc=(0.7, 0, r * 0.55), rot=(0, 90, 0), segs=24, bev=0.02, parent=P)
    K.cyl("flange", r + 0.08, 0.16, steel, loc=(1.4, 0, r * 0.55), rot=(0, 90, 0), segs=24, bev=0.02, parent=P)
    FR.framed_box(P, "housing", (-0.35, 0, 0.8), (0.9, 1.3, 1.6), M(BONE, wear=0.5, grime=0.5), steel, fw=0.12)
    K.cyl("spindle", 0.05, 0.4, M(GRAPHITE, metal=0.6), loc=(-0.35, 0, 1.8), segs=10, parent=P)
    K.torus("wheel", 0.32, 0.035, M(SIGNAL, metal=0.3, wear=0.5), loc=(-0.35, 0, 2.0), segs=24, parent=P)
    for k in range(3):
        K.box(f"spoke{k}", (0.6, 0.04, 0.03), M(SIGNAL, metal=0.3), loc=(-0.35, 0, 2.0), rot=(0, 0, k * 60), bev=0, parent=P)
    FR.hazard(P, (-0.35, -0.66, 1.2), 0.6, 0.18, M(MUSTARD), M(GRAPHITE), n=5)
    return P


def machine_wreck(seed=14):
    """A Colony harvester torn in half and left on its side: pill-cornered body, a broken arm, one big wheel."""
    rnd = random.Random(seed)
    P = _root("wreck")
    # built at 1:1.6 and scaled up to a harvester the player can climb on
    G = K.link(bpy.data.objects.new("wreck_scale", None))
    G.parent = P
    G.scale = (1.6, 1.6, 1.6)
    paint = M(MUSTARD, wear=0.85, grime=0.7, scratches=0.7, moss=0.2)
    steel = M(STEEL, metal=0.4, wear=0.6, grime=0.5)
    dark = M(GRAPHITE, metal=0.4, wear=0.4)
    body = K.link(bpy.data.objects.new("body", None))
    body.parent = G
    body.location = (0, 0, 0.9)
    body.rotation_euler = (math.radians(4), math.radians(-14), math.radians(6))
    K.slab("hull", 2.8, 1.8, 1.5, 0.16, paint, bev=0.03, parent=body)
    K.slab("cab", 1.2, 1.5, 0.9, 0.12, paint, loc=(-0.6, 0, 1.1), bev=0.03, parent=body)
    # chassis rails under the hull and a steel band round its waist: framed construction, not a toy shell
    for s in (-1, 1):
        K.box(f"rail{s}", (3.0, 0.2, 0.22), dark, loc=(-0.05, s * 0.75, -0.82), bev=0.02, parent=body)
        K.box(f"band{s}", (2.84, 0.06, 0.14), steel, loc=(0, s * 0.92, 0.3), bev=0.01, parent=body)
        for x in (-0.95, 0.15):
            K.box(f"seam{s}{x}", (0.03, 0.03, 1.3), dark, loc=(x, s * 0.91, -0.05), bev=0, parent=body)
            for z in (-0.5, 0.45):
                FR.screw(body, (x + 0.08, s * 0.915, z), (90, 0, 0), r=0.025, mat=M(ZINC, metal=0.6))
    K.box("cab_frame", (1.26, 1.56, 0.1), steel, loc=(-0.6, 0, 1.58), bev=0.015, parent=body)
    K.box("glass", (0.04, 1.2, 0.5), K.mat("darkglass", "#22303a", rough=0.15, metal=0.2), loc=(-1.21, 0, 1.15), bev=0.01, parent=body)
    K.box("torn", (0.1, 1.7, 1.4), dark, loc=(1.42, 0, 0), bev=0.02, parent=body)
    for k in range(5):
        K.box(f"rib{k}", (0.5, 0.05, rnd.uniform(0.6, 1.2)), M(RUST, metal=0.4, wear=0.6), loc=(1.6, -0.7 + k * 0.35, rnd.uniform(-0.2, 0.2)),
              rot=(rnd.uniform(-20, 20), rnd.uniform(-30, 30), 0), bev=0.01, parent=body)
    FR.hazard(body, (-1.41, -0.91, -0.45), 1.2, 0.2, M(MUSTARD), dark, n=6)
    FR.vent(body, (0.4, -0.91, 0.2), 0.8, 0.5, steel, dark)
    # the arm: two boxed beams and a toothed head lying in the grass
    K.box("arm1", (0.25, 0.25, 2.0), steel, loc=(-1.5, -1.2, 0.4), rot=(0, 70, 25), bev=0.03, parent=G)
    K.box("arm2", (0.22, 0.22, 1.6), steel, loc=(-2.7, -1.6, 0.15), rot=(0, 88, 40), bev=0.03, parent=G)
    head = K.box("head", (0.9, 0.5, 0.35), dark, loc=(-3.4, -2.2, 0.18), rot=(0, 0, 40), bev=0.04, parent=G)
    for k in range(5):
        K.box(f"tooth{k}", (0.08, 0.2, 0.08), steel, loc=(-3.75 + k * 0.17, -2.5 + k * 0.14, 0.08), rot=(0, 0, 40), bev=0.01, parent=G)
    w = K.link(bpy.data.objects.new("wheel", None))
    w.parent = G
    w.location = (0.9, 1.4, 0.75)
    w.rotation_euler = (math.radians(90), 0, math.radians(10))
    K.cyl("tyre", 0.75, 0.5, K.mat("tyre", "#26282a", rough=0.9, grime=0.3), segs=20, bev=0.08, parent=w)
    K.cyl("rim", 0.45, 0.52, M(BONE, metal=0.3, wear=0.5), segs=16, bev=0.02, parent=w)
    return P


def concrete_barrier(seed=15):
    """A colony road barrier: a concrete jersey block with hazard stripes, chipped at one end."""
    P = _root("barrier")
    bm = bmesh.new()
    prof = [(-0.3, 0), (0.3, 0), (0.3, 0.08), (0.16, 0.3), (0.12, 0.8), (-0.12, 0.8), (-0.16, 0.3), (-0.3, 0.08)]
    L = 2.0
    a = [bm.verts.new((-L / 2, y, z)) for y, z in prof]
    b = [bm.verts.new((L / 2, y, z)) for y, z in prof]
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)
    for i in range(len(prof)):
        j = (i + 1) % len(prof)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    ob = K.from_bm("block", bm, K.mat("concrete", CONCRETE, rough=0.9, wear=0.5, grime=0.6, cracks=0.15, moss=0.15))
    K.bevel(ob, 0.03)
    K.place(ob, parent=P)
    for s in (-1, 1):
        FR.hazard(P, (0, s * 0.235, 0.55), 1.6, 0.16, M(MUSTARD, wear=0.6), M(GRAPHITE), n=8, axis="y")
    K.box("chip", (0.2, 0.3, 0.3), K.mat("concrete_in", "#8e8b82", rough=0.95, cracks=0.6), loc=(0.98, 0.0, 0.7), rot=(20, 30, 10), bev=0.04, parent=P)
    return P


def cable_spool(seed=16):
    """A big cable drum on its side with a few turns of cable left, a tail of it trailing off."""
    P = _root("spool")
    wood_ = wood(1, wear=0.6)
    K.cyl("core", 0.4, 0.9, K.mat("cable", "#2b2d30", rough=0.6), loc=(0, 0, 0.75), rot=(90, 0, 0), segs=20, bev=0.02, parent=P)
    for s in (-1, 1):
        K.cyl(f"flange{s}", 0.75, 0.08, wood_, loc=(0, s * 0.49, 0.75), rot=(90, 0, 0), segs=20, bev=0.02, parent=P)
        for k in range(4):
            a = k * 90
            K.box(f"bolt{s}{k}", (0.05, 0.1, 0.05), M(ZINC, metal=0.6), loc=(math.cos(math.radians(a)) * 0.55, s * 0.55, 0.75 + math.sin(math.radians(a)) * 0.55),
                  bev=0, parent=P)
    FR.label(P, "COLONY", (0, -0.54, 0.85), 0.14, M(STEEL), rot=(90, 0, 0))
    pts = [(0.4, -0.2, 0.4), (0.9, -0.5, 0.05), (1.6, -0.3, 0.04), (2.1, -0.8, 0.04)]
    for k in range(len(pts) - 1):
        (x0, y0, z0), (x1, y1, z1) = pts[k], pts[k + 1]
        L = math.dist(pts[k], pts[k + 1])
        yaw = math.degrees(math.atan2(y1 - y0, x1 - x0))
        pitch = math.degrees(math.atan2(z1 - z0, math.hypot(x1 - x0, y1 - y0)))
        K.cyl(f"tail{k}", 0.04, L, K.mat("cable", "#2b2d30", rough=0.6), loc=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
              rot=(0, 90 - pitch, yaw), segs=8, bev=0, parent=P)
    return P


# ================================================================== burned forest
def trailer_scorched(seed=17):
    """A box trailer the fire went through: charred panels, warped roof, one wheel left, doors hanging open."""
    rnd = random.Random(seed)
    P = _root("trailer")
    char = K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)
    rust = M(RUST, metal=0.4, wear=0.8, grime=0.6)
    body = K.link(bpy.data.objects.new("body", None))
    body.parent = P
    body.location = (0, 0, 0.55)
    body.rotation_euler = (math.radians(-5), math.radians(3), 0)
    L, W, H = 3.6, 1.9, 1.8
    K.box("floor", (W, L, 0.12), rust, loc=(0, 0, 0), bev=0.02, parent=body)
    for s in (-1, 1):
        for k in range(4):
            col = char if rnd.random() < 0.65 else rust
            K.box(f"panel{s}{k}", (0.05, L / 4 - 0.04, H - rnd.uniform(0, 0.3)), col, loc=(s * W / 2, -L / 2 + L / 8 + k * L / 4, H / 2 - 0.05),
                  rot=(0, s * rnd.uniform(-4, 4), 0), bev=0.01, parent=body)
    K.box("front", (W, 0.05, H), char, loc=(0, -L / 2, H / 2), bev=0.01, parent=body)
    K.box("roof", (W + 0.1, L * 0.7, 0.05), rust, loc=(0, -L * 0.15, H + 0.05), rot=(6, -4, 0), bev=0.01, parent=body)
    K.box("door_l", (W / 2, 0.05, H - 0.1), rust, loc=(-W / 2 - 0.45, L / 2 + 0.3, H / 2), rot=(0, 0, -70), bev=0.01, parent=body)
    K.box("hitch", (0.1, 1.2, 0.1), M(GRAPHITE, metal=0.5, wear=0.6), loc=(0, -L / 2 - 0.6, -0.2), rot=(-12, 0, 0), bev=0.01, parent=body)
    K.cyl("wheel", 0.38, 0.25, K.mat("tyre_burnt", CHAR_D, rough=1.0), loc=(W / 2 + 0.05, 0.4, 0.38), rot=(0, 90, 0), segs=16, bev=0.04, parent=P)
    K.cyl("hub", 0.2, 0.27, rust, loc=(W / 2 + 0.05, 0.4, 0.38), rot=(0, 90, 0), segs=12, bev=0.01, parent=P)
    K.box("block", (0.4, 0.4, 0.3), K.mat("concrete", CONCRETE, rough=0.9, grime=0.6), loc=(-W / 2 + 0.2, 0.4, 0.15), bev=0.03, parent=P)
    return P


PROPS = {
    "fence_rail": lambda: fence_rail(),
    "fence_rail_broken": lambda: fence_rail(broken=True, seed=2),
    "cart_broken": cart_broken,
    "hay_bale": hay_bale,
    "hay_bale_stack": hay_bale_stack,
    "scarecrow": scarecrow,
    "water_trough": water_trough,
    "barrel_rusty": barrel_rusty,
    "log_pile": log_pile,
    "tarp_shelter": tarp_shelter,
    "signpost": signpost,
    "tree_trunk": stump,
    "large_pipe_long": lambda: colony_pipe(4.0),
    "large_pipe_short": lambda: colony_pipe(2.0, seed=18),
    "pipe_valve": colony_pipe_valve,
    "machine_wreck": machine_wreck,
    "concrete_barrier": concrete_barrier,
    "cable_spool": cable_spool,
    "trailer_scorched": trailer_scorched,
}
