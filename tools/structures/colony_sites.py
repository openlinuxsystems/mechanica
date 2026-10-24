"""The last Colony Works sites (docs/art-direction.md), assembled from colony_kit.py and the colony and industry props:
the two packing stations, the silo cluster, the silo district, the growlab, the chemical lake facility and the
roadside kiosk.

Each keeps its placeholder prefab's footprint and every marker of it (tools/author-poi-placeholders.py): where the
markers stand 0.3 m up (the placeholder's pad) the model has a concrete yard pad that high, its edges ramped to the
ground (the kiosk's is 0.2 m), and the markers keep clear ground round them. The chemical lake facility has no markers
and stands on a low apron that stops short of the lake's bank (its basin and pool: nothing stands in the liquid).
Blender frame: origin on the ground at the POI's centre, front towards -Y (the POI's +Z), so a marker at the POI's
(x, y, z) is at Blender (x, -z, y). WALK lists, per site, the spots walk_settlers.py checks a player can reach.

* ``packing_station_vegetable`` and ``packing_station_fruit`` - one design: a 36 x 24 m packing hall on a dock-height
  podium, a covered loading dock with steps and a ramp, belt lines, packing tables and a cold room inside, a capsule
  coolant tank on the roof and a produce board (a green cabbage or a red apple; the doors and the canopy's edge in the
  same colour).
* ``silo_cluster`` - four 16 m silos round an enclosed gallery from a stair tower you climb to the silo roofs, a dump
  shed and a truck pit.
* ``silo_district`` - eight 24 m silos in two rows, a 50 m gallery over them from a 24 m stair tower, an inclined belt
  from the truck hopper to the tower, the grain store and the machine shed, a weighbridge and a machine pen.
* ``growlab`` - a barrel-vault glasshouse (the signature volume) of grow beds under cyan grow lights, joined to a
  porthole lab box, with water butts and solar rows outside.
* ``chemical_lake_facility`` - a process hall, the big capsule chemical tank, a pump house on the shore with its
  intake pipes to headwalls on the bank, an outfall, a pipe rack and drums.
* ``roadside_kiosk`` - a framed box kiosk with its counter under a canopy towards the road, a roll-up shutter, a
  porthole, a capsule water tank on the roof and a square lamppost.
"""
import math
import random

import bpy
from mathutils import Vector

import kit as K
import colony_kit as C
import settler_kit as S
from colony_kit import group, quiet, solid
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, CYAN, WARM

import props_colony_a as CA
import props_colony_b as CB
import props_industry as PI
import props_utility as PU
import props_street as PS
import props_farm as PF
import props_fields as PFI

PAD = 0.3
LEAF = "#5f8a4a"


def _root(name):
    return K.link(bpy.data.objects.new(name, None))


def _finish(root):
    if K.EXPORT:
        C.lighten(root)
    return root


def concrete_pad(P, w, d, h=PAD, edge=1.0, loc=(0, 0, 0)):
    return S.pad(P, w, d, h, edge=edge, mat=C.concrete(), loc=loc)


def decal(P, name, size, loc, col, rot=0.0, z=0.012):
    """A flat patch lying on a surface (oil, a grate, paint): drawn only."""
    return quiet(K.box(name, (size[0], size[1], 0.01), M(col, wear=0, grime=0.2, scratches=0, rough=0.9), loc=(loc[0], loc[1], loc[2] + z), rot=(0, 0, rot), bev=0, parent=P))


def steps(P, name, a, b, width, rise=0.25, mat=None):
    """Concrete steps from a (the foot) to b (the top), risers of rise, walked as a ramp."""
    a, b = Vector(a), Vector(b)
    n = max(1, round((b.z - a.z) / rise))
    run = Vector((b.x - a.x, b.y - a.y, 0))
    L = run.length
    d = run.normalized()
    yaw = math.degrees(math.atan2(d.y, d.x))
    for k in range(n):
        x0 = L * k / n
        ln = L - x0
        c = a + d * (x0 + ln / 2)
        quiet(K.box(f"{name}_step", (ln, width, (b.z - a.z) / n), mat or C.concrete(), loc=(c.x, c.y, a.z + (b.z - a.z) * (k + 0.5) / n), rot=(0, 0, yaw), bev=0.02, parent=P))
        quiet(K.box(f"{name}_nose", (0.05, width, 0.02), C.mustard(0.5), loc=tuple(a + d * x0 + Vector((0.025 * d.x, 0.025 * d.y, (b.z - a.z) * (k + 1) / n + 0.005))), rot=(0, 0, yaw), bev=0, parent=P))
    C.ramp(P, name, a + Vector((0, 0, (b.z - a.z) / n / 2)), b, width, base=b.z - a.z)


def ramp_slab(P, name, a, b, width, t=0.35):
    """A drawn concrete ramp whose top runs from a to b, walked on (its collider the same slope)."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    d = run.normalized()
    L = (b - a).length
    pitch = math.atan2(b.z - a.z, run.length)
    yaw = math.atan2(d.y, d.x)
    n = Vector((-math.sin(pitch) * d.x, -math.sin(pitch) * d.y, math.cos(pitch)))
    c = (a + b) / 2 - n * (t / 2)
    quiet(K.box(name, (L, width, t), C.concrete(), loc=tuple(c), rot=(0, -math.degrees(pitch), math.degrees(yaw)), bev=0.02, parent=P))
    for s in (-1, 1):
        side = Vector((-d.y, d.x, 0)) * s * (width / 2 - 0.08)
        quiet(C.beam(P, f"{name}_kerb", a + side + Vector((0, 0, 0.04)), b + side + Vector((0, 0, 0.04)), 0.16, C.mustard(0.5), d=0.1))
    C.ramp(P, name, a, b, width, base=max(a.z, b.z))


def recolour(G, prefix, mat):
    """Paints every mesh under G whose name starts with prefix in mat (a door curtain, a canopy edge)."""
    for ob in G.children_recursive:
        if ob.type == "MESH" and ob.name.startswith(prefix):
            ob.data.materials.clear()
            ob.data.materials.append(mat)


def crate(P, loc, size=(1.0, 0.8, 0.7), yaw=0.0, col=BONE, collide=True):
    """A framed colony produce crate (slatted sides in a steel frame)."""
    G = group(P, "crate", loc, yaw)
    sx, sy, sz = size
    C.framed(G, "crate_box", (0, 0, sz / 2), size, M(col, wear=0.4, grime=0.4), C.steel(), fw=0.06)
    for k in range(3):
        quiet(K.box("crate_slat", (sx + 0.01, sy + 0.01, 0.03), C.graphite(), loc=(0, 0, sz * (k + 1) / 4), bev=0, parent=G))
    if collide:
        solid(G, "crate", size, (0, 0, sz / 2))
    return G


def produce(G, loc, kind, rnd, n=8, spread=(0.8, 0.6)):
    """A few heads of produce heaped on a crate or a bed: cabbages and roots, or apples and pears (drawn only)."""
    cols = ("#6f9a3a", "#86ad4c", "#b98a3c", "#7a4a6a") if kind == "vegetable" else (SIGNAL, "#d6b13c", "#9c3a32", "#a8b84a")
    for k in range(n):
        r = rnd.uniform(0.09, 0.16) if kind == "vegetable" else rnd.uniform(0.06, 0.09)
        quiet(K.sphere("produce", r, M(rnd.choice(cols), wear=0, grime=0.1, scratches=0, rough=0.6),
                       loc=(loc[0] + rnd.uniform(-spread[0], spread[0]) / 2, loc[1] + rnd.uniform(-spread[1], spread[1]) / 2, loc[2] + r * 0.8), parent=G))


# ================================================================== the packing stations
def packing_station(kind):
    """The packing station (36 x 24 m, one design for both kinds): the hall on a concrete podium at dock height (1.3
    m), its two roller doors open onto a 20 m loading dock under a canopy, steps up at the dock's left end and a ramp
    at its right, a walk-in door at the back with steel steps down. Inside: two belt lines with packing tables, the
    produce in crates on pallets, a cold room in the back corner. On the roof a capsule coolant tank (the signature
    volume), air handlers and the produce board facing the road. kind 'vegetable' paints the doors and the canopy's
    edge leaf green and draws a cabbage; 'fruit' paints them signal red and draws an apple."""
    veg = kind == "vegetable"
    rnd = random.Random(5 if veg else 18)
    accent = M(LEAF if veg else SIGNAL, metal=0.3, wear=0.35, grime=0.4)
    P = _root(f"packing_station_{kind}")
    concrete_pad(P, 44.0, 32.0)
    FLOOR = 1.3
    K.box("podium", (36.0, 24.0, FLOOR - PAD), C.concrete(), loc=(0, 0, (PAD + FLOOR) / 2), bev=0.04, parent=P)
    K.box("dock", (20.0, 3.0, FLOOR - PAD), C.concrete(), loc=(0, -13.5, (PAD + FLOOR) / 2), bev=0.04, parent=P)
    quiet(K.box("dock_edge", (20.0, 0.12, 0.12), C.graphite(), loc=(0, -14.97, FLOOR - 0.06), bev=0.01, parent=P))
    C.hazard_band(P, (0, -15.02, FLOOR - 0.4), 20.0, 0.25, name="dock_haz")
    for x in (-8.0, -2.0, 2.0, 8.0):
        quiet(K.box("dock_bumper", (0.35, 0.18, 0.5), M("#26282a", rough=0.9), loc=(x, -15.08, FLOOR - 0.35), bev=0.03, parent=P))
    steps(P, "dock_steps", (-12.4, -13.5, PAD), (-10.0, -13.5, FLOOR), 3.0)
    C.railing(P, (-12.4, -14.9, PAD), (-10.0, -14.9, FLOOR), h=1.0, posts=1.2, kick=False)
    ramp_slab(P, "dock_ramp", (13.75, -13.5, PAD), (10.0, -13.5, FLOOR), 3.0)
    # the hall
    plans = {
        "front": lambda b, f: "window" if b in (0, 1, 6, 7) else "plain",
        "right": lambda b, f: ("fan", "window", "fan", "plain", "fan")[b % 5],
        "back": lambda b, f: ("louvre", "plain", "plain", "plain", "plain", "plain", "louvre", "window")[b % 8],
        "left": lambda b, f: ("window", "broken", "louvre", "window", "plain")[b % 5],
    }
    H0 = FLOOR - C.SLAB
    hall, walls, H = C.shed(P, "hall", 36.0, 24.0, floors=1, loc=(0, 0, H0), plans=plans,
                            rollers=(("front", -5.0, 4.0, 3.25, 2.75), ("front", 5.0, 4.0, 3.25, 2.75), ("back", -11.0, 4.0, 3.25, 0.0)),
                            doorways=(("back", 4.0),), seed=7 if veg else 9, gear=False)
    H += H0
    recolour(walls["front"], "door_curtain", accent)
    recolour(walls["back"], "door_curtain", accent)
    front = walls["front"]
    C.canopy(front, (0, C.CREST - 0.02, C.SLAB + 3.25 + 0.75), 20.0, depth=3.0, ties=False)    # under the parapet
    recolour(front, "canopy_edge", accent)
    C.logo_panel(front, (0, C.CREST - 0.02, C.SLAB + 1.0), w=3.4, h=1.4, code="PACK V-02" if veg else "PACK F-01")
    # the back door's landing and steps down to the yard
    C.landing(P, -5.0, -3.0, 12.0, 13.5, FLOOR)
    C.railing(P, (-5.0, 13.44, FLOOR), (-3.0, 13.44, FLOOR))
    C.railing(P, (-4.94, 12.1, FLOOR), (-4.94, 13.44, FLOOR))
    C.steel_flight(P, (-0.6, 12.75, PAD), (-3.0, 12.75, FLOOR), 1.4, name="back_steps", rails=(True, True))
    for x in (-4.9, -3.1):
        quiet(K.box("landing_post", (0.14, 0.14, FLOOR - PAD), C.steel(), loc=(x, 13.4, (PAD + FLOOR) / 2), bev=0.01, parent=P))
    # inside: belt lines from the doors' sides to the back, tables along them, crates, the cold room
    In = group(P, "inside", (0, 0, FLOOR))
    for sx in (-1, 1):
        x = sx * 10.5
        C.belt_conveyor(In, (x, -9.5, 0.9), (x, 5.5, 0.9), w=0.8, name="pack_line")
        for k, y in enumerate((-7.0, -3.5, 0.0, 3.5)):
            C.framed(In, "pack_table", (x + sx * 1.3, y, 0.45), (1.2, 2.4, 0.9), M(STEEL, metal=0.3, wear=0.4), C.graphite(), fw=0.06)
            solid(In, "pack_table", (1.2, 2.4, 0.9), (x + sx * 1.3, y, 0.45))
            crate(In, (x + sx * 1.3, y - 0.5, 0.9), size=(0.9, 0.7, 0.45), col=BONE, collide=False)
            produce(In, (x + sx * 1.3, y - 0.5, 1.33), kind, rnd, n=6)
        for y in (-8.0, -5.0, -2.0, 1.0, 4.0):
            crate(In, (x, y, 0.9), size=(0.6, 0.5, 0.35), col=LEAF if veg else BONE, collide=False)
            produce(In, (x, y, 1.25), kind, rnd, n=4, spread=(0.4, 0.3))
        for k, y in enumerate((-1.0, 2.5, 6.0)):
            C.prop(In, lambda: CB.pallet(21 + k), (sx * 15.5, y, 0), yaw=90 + rnd.uniform(-5, 5))
    for k, (x, y) in enumerate(((-6.5, 8.0), (-4.0, 9.8), (2.0, 9.6), (-1.0, 8.2))):
        if veg:
            C.prop(In, lambda k=k: PF.vegetable_crates(53 + k), (x, y, 0), yaw=rnd.uniform(0, 360))
        else:
            C.prop(In, lambda k=k: PFI.fruit_crate_stack(70 + k), (x, y, 0), yaw=rnd.uniform(0, 360))
    C.prop(In, lambda: CB.forklift_wreck(22), (-13.0, 9.0, 0), yaw=150)
    # the cold room: a framed insulated box with its door shut and a fan on its front
    C.framed(In, "cold_room", (13.0, 8.5, 1.6), (8.0, 6.0, 3.2), C.bone(1), C.steel(), fw=0.16, screws=C.zinc())
    solid(In, "cold_room", (8.0, 6.0, 3.2), (13.0, 8.5, 1.6))
    CR = group(In, "cold_room_door", (9.0, 8.5, 0), -90)
    C.roller_door(CR, (0, -0.02, 0), 2.5, 2.4, 0.0, lamp=False, name="cold_door")
    C.wall_fan(group(In, "cold_room_fan", (14.5, 5.5, 0)), 0.0, 2.2, 0.5, y=-0.01)
    C.hazard_band(group(In, "cold_room_haz", (13.0, 5.49, 0)), (0, 0, 0.05), 7.6, 0.3)
    C.prop(In, lambda: PU.switch_cabinet(), (16.8, 3.0, 0), yaw=-90)
    # the roof: the coolant tank, air handlers, vents, skylights and the produce board
    C.roof_tank(P, (11.0, 5.0, H), r=2.0, h=3.5, code="CL-1")
    C.pipe_run(P, [(8.6, 5.0, H + 1.0), (8.6, 5.0, H + 0.4), (8.6, 9.5, H + 0.4), (17.4, 9.5, H + 0.4), (17.4, 9.5, H - 1.5)], r=0.14)
    for x, y, yaw in ((-10.0, 6.0, 0), (-4.0, 6.0, 0), (3.0, 7.0, 90)):
        C.ac_unit(P, (x, y, H), yaw=yaw)
    for x, y in ((-14, -8), (-7, -9), (6, -8), (15, -3), (-15, 3), (0, 2)):
        C.mushroom_vent(P, (x, y, H), r=rnd.choice((0.3, 0.35, 0.45)))
    for x in (-8.0, 4.0):
        C.skylight(P, (x, -2.0, H), l=6.0, w=2.0, broken={(rnd.randrange(6), 1)})
    C.pictogram_board(P, (0.0, -10.5, H), kind, size=3.5, legs=1.25)
    C.roof_hatch(P, (15.0, 9.0, H))
    # the yard: lampposts at the front corners, drums, sacks, a water tank by the side
    for x in (-21.0, 21.0):
        C.lamppost(P, (x, -15.5, PAD), h=5.0, yaw=0, lit=False)
    C.prop(P, lambda: PI.barrel_pallet(63), (19.5, 4.0, PAD), yaw=10)
    C.prop(P, lambda: CA.fuel_tank(28), (20.0, -6.0, PAD), yaw=90)
    C.prop(P, lambda: CB.pallet(24), (-20.5, -6.0, PAD), yaw=80)
    C.prop(P, lambda: PF.milk_churns(46) if veg else PFI.fruit_crate_stack(77), (-20.5, 2.0, PAD), yaw=90)
    weather(P, 18.0, 12.0, rnd)
    return _finish(P)


def weather(P, hw, hd, rnd, z=PAD, at=(0.0, 0.0)):
    """Dirt drifted against a building's plinth (hw x hd round at) and weeds at its foot (drawn only), as
    warehouses.weather."""
    dirt = K.mat("drift_dirt", "#8c7f66", rough=1.0, ao=0)
    weed = K.mat("weed", "#6d8f3c", rough=0.8, ao=0)
    for k in range(14):
        side = rnd.randrange(4)
        if side < 2:
            x, y = rnd.uniform(-hw + 2, hw - 2), (-hd - 0.5 if side == 0 else hd + 0.5)
            sx, sy = rnd.uniform(1.0, 2.2), rnd.uniform(0.3, 0.5)
        else:
            x, y = (-hw - 0.5 if side == 2 else hw + 0.5), rnd.uniform(-hd + 2, hd - 2)
            sx, sy = rnd.uniform(0.3, 0.5), rnd.uniform(1.0, 2.2)
        x, y = x + at[0], y + at[1]
        d = K.disc("drift", 1.0, dirt, (x, y), z=z + 0.012 + 0.0004 * k, segs=16, wobble=0.25, seed=k + 3)
        d.scale = (sx, sy, 1)
        d.parent = P
        quiet(d)
        for _ in range(rnd.randint(2, 5)):
            h = rnd.uniform(0.15, 0.4)
            blade = C.quad(P, "weed", [(-0.025, 0.0), (0.025, 0.0), (0.0, h)], weed)
            blade.location = (x + rnd.uniform(-sx, sx) * 0.7, y + rnd.uniform(-sy, sy) * 0.7, z)
            blade.rotation_euler = (math.radians(rnd.uniform(-20, 20)), 0, math.radians(rnd.uniform(0, 360)))


def packing_station_vegetable():
    return packing_station("vegetable")


def packing_station_fruit():
    return packing_station("fruit")


# ================================================================== the silos
def silo_block(P, floors, tower, xs, ys, r, yc, x_end, trestles, codes, seed, tower_yaw=0.0):
    """Silos at xs x ys (two rows either side of the gallery at y = yc), their tops 0.1 m under the stair tower's deck,
    the gallery from the tower over their roofs to x_end, with a door onto each silo's roof, and the spouts down from
    the gallery's belt into each silo. tower: the tower's loc (its corner; see stair_tower). Returns the deck height."""
    _, _, H = C.stair_tower(P, tower, floors, S=6.0, yaw=tower_yaw, exits=(("right", 0.0),), code=codes[0], seed=seed)
    h = H - 0.1 - PAD
    k = 0
    for y in ys:
        side = 1 if y > yc else -1
        for x in xs:
            C.silo(P, (x, y, PAD), r=r, h=h, code=codes[1] + str(k + 1), seed=seed + k, gap=(-90 if side > 0 else 90, 30, abs(y - yc) - 1.25),
                   ladder=(90 if side > 0 else -90) if k % 2 == 0 else None)
            k += 1
            quiet(C.rod(P, "spout", (x, yc + side * 0.4, H + 2.2), (x, y - side * 0.2, PAD + h + 1.05), 0.16, C.zinc(), segs=10))
            quiet(K.box("spout_head", (0.5, 0.5, 0.4), C.graphite(), loc=(x, yc + side * 0.4, H + 2.15), bev=0.02, parent=P))
    tx = tower[0]
    x0, x1 = (min(tx, x_end), max(tx, x_end))
    if tower_yaw:
        x1 = tx - 6.0 + 0.2
        x0 = x_end
        ends = (True, False)
    else:
        x0 = tx + 6.0 - 0.2
        x1 = x_end
        ends = (False, True)
    C.gallery(P, x0, x1, yc, H, doors=[(x, s) for x in xs for s in (-1, 1)], ends=ends, trestles=trestles, seed=seed)
    return H


def silo_cluster():
    """The silo cluster (30 x 30 m): four 16 m silos in two rows, their roofs railed decks you reach from the
    enclosed gallery over them, the gallery reached from the top of the stair tower on the left (the elevator leg's
    housing; its door in its left wall), a truck pit at the tower's foot, a dump shed with its roller door open at the
    front left, and sacks, pallets and lampposts round the yard."""
    rnd = random.Random(6)
    P = _root("silo_cluster")
    concrete_pad(P, 38.0, 38.0)
    H = silo_block(P, 4, (-12.0, -3.0, 0.0), (-1.5, 7.5), (-5.0, 5.0), 4.0, 0.0, 12.0, (3.0, 11.0), ("E-6", "AG-"), 6)
    # the truck pit in front of the tower and the screw up into the leg's boot
    decal(P, "pit_grate", (4.0, 2.4), (-9.0, -5.0, PAD), "#3a3d40")
    for k in range(9):
        decal(P, "pit_bar", (0.06, 2.3), (-10.8 + k * 0.45, -5.0, PAD), "#5a6068", z=0.02)
    C.hazard_band(group(P, "pit_haz", (-9.0, -6.3, PAD + 0.0), 0), (0, 0, 0), 4.0, 0.1)
    quiet(C.rod(P, "pit_screw", (-9.0, -4.6, PAD + 0.2), (-9.0, -3.25, PAD + 2.6), 0.22, C.steel(), segs=12))
    # the dump shed at the front left, its door open towards the road
    C.shed(P, "dump_shed", 10.0, 7.0, floors=1, loc=(-10.0, -12.5, 0.0), rollers=(("front", 0.0, 4.0, 3.25, 2.75),),
           plans={"right": lambda b, f: "window", "left": lambda b, f: "louvre", "back": lambda b, f: "plain"}, seed=61)
    C.prop(P, lambda: PF.seed_sacks_cart(48), (-13.0, -12.0, PAD), yaw=30)
    C.prop(P, lambda: CB.pallet(25), (-7.5, -11.0, PAD), yaw=5)
    # the yard
    C.prop(P, lambda: PI.barrel_pallet(66), (14.5, -10.0, PAD), yaw=-20)
    C.prop(P, lambda: CB.pallet(26), (16.0, 12.0, PAD), yaw=10)
    C.prop(P, lambda: CA.transformer_box(31), (-16.5, 5.0, PAD), yaw=90)
    for x, y in ((-17.5, -17.5), (17.5, 15.0), (6.5, -17.5)):
        C.lamppost(P, (x, y, PAD), h=5.0, yaw=0 if y < 0 else 180, lit=False)
    weather(P, 5.0, 3.5, rnd, at=(-10.0, -12.5))
    return _finish(P)


def silo_district():
    """The silo district (90 x 70 m): eight 24 m silos in two rows at the back, the 50 m gallery over their roofs from
    the 24 m stair tower at the east end (its door facing the grain store), an inclined belt from the truck hopper up
    to the tower, the grain store (22 x 20 m, two storeys high) at the back right, the machine shed (24 x 16 m) at the
    front left, a weighbridge with its booth on the way in, and a fenced machine pen at the front right."""
    rnd = random.Random(26)
    P = _root("silo_district")
    concrete_pad(P, 98.0, 78.0, edge=1.2)
    H = silo_block(P, 6, (19.0, 19.0, 0.0), (-30.0, -18.0, -6.0, 6.0), (10.0, 22.0), 5.0, 16.0, -37.0, (-36.0, -24.0, -12.0, 0.0, 10.0),
                   ("E-26", "SD-"), 26, tower_yaw=180.0)
    # the truck hopper and the belt up into the tower's south face
    C.prop(P, lambda: PI.hopper(52), (8.5, -2.5, PAD), yaw=90)
    C.belt_conveyor(P, (10.0, 0.5, 2.6), (14.5, 12.6, 7.5), w=0.9, name="feed_belt", collide="legs")
    quiet(K.box("feed_hood", (1.4, 1.2, 1.3), C.graphite(), loc=(14.5, 12.5, 7.6), bev=0.03, parent=P))
    # the grain store
    C.shed(P, "grain_store", 22.0, 20.0, floors=2, loc=(33.0, 14.0, 0.0),
           rollers=(("front", -4.0, 6.0, 3.75, 3.0), ("left", 4.0, 6.0, 3.75, 0.0)), doorways=(("front", 6.0),),
           plans={"front": lambda b, f: "window" if (f and b in (0, 4)) else "plain", "right": lambda b, f: ("window", "louvre", "broken", "window")[b % 4] if f else "fan" if b % 2 else "plain",
                  "back": lambda b, f: "louvre" if f else "plain", "left": lambda b, f: "window" if f else "plain"}, seed=27)
    C.logo_panel(group(P, "store_logo", (33.0, 4.0, 0)), (0, C.CREST - 0.02, 5.6), w=8.0, h=2.25, code="SILO DISTRICT")
    C.roof_tank(P, (38.0, 19.0, 8.3), r=2.0, h=3.5, code="T-26")
    S_in = group(P, "store_in", (33.0, 14.0, PAD))
    C.prop(S_in, lambda: PI.compressor_unit(48), (6.0, 6.0, 0), yaw=0)
    for k, (x, y) in enumerate(((-7.0, 6.0), (-4.0, 7.0), (0.0, 6.5))):
        C.prop(S_in, lambda k=k: CB.pallet(30 + k), (x, y, 0), yaw=rnd.uniform(0, 30))
    C.belt_conveyor(S_in, (-8.0, -3.0, 0.9), (4.0, -3.0, 0.9), name="store_line")
    # the machine shed
    C.shed(P, "machine_shed", 24.0, 16.0, floors=2, loc=(-20.0, -20.0, 0.0),
           rollers=(("front", -6.0, 6.0, 3.75, 3.4), ("front", 6.0, 6.0, 3.75, 2.0)), doorways=(("right", -4.0),),
           plans={"front": lambda b, f: "window" if f else "plain", "left": lambda b, f: "louvre" if f else "window",
                  "back": lambda b, f: "window" if f else "fan" if b == 2 else "plain", "right": lambda b, f: "window"}, seed=28)
    M_in = group(P, "shed_in", (-20.0, -20.0, PAD))
    C.prop(M_in, lambda: CB.tractor_wreck(25), (5.0, 2.0, 0), yaw=100)
    C.prop(M_in, lambda: PF.harrow_rusty(50), (-6.0, 3.0, 0), yaw=0)
    C.prop(M_in, lambda: PI.steel_beam_stack(59), (-8.0, -3.5, 0), yaw=90)
    # the weighbridge and its booth on the way in
    decal(P, "weighbridge", (3.6, 16.0), (-2.0, -22.0, PAD), "#4a4f55")
    for y in (-30.0, -14.0):
        C.hazard_band(group(P, "wb_haz", (-2.0, y, PAD)), (0, 0, 0), 3.6, 0.05)
    for k in range(7):
        decal(P, "wb_plate", (3.5, 0.04), (-2.0, -29.0 + k * 2.3, PAD), "#2b2f35", z=0.02)
    C.shed(P, "weigh_booth", 4.0, 3.5, floors=1, loc=(2.5, -22.0, 0.0), doorways=(("right", 0.0),),
           plans={"left": lambda b, f: "window", "front": lambda b, f: "porthole", "back": lambda b, f: "porthole"}, seed=29, gear=False)
    # the machine pen at the front right and the yard
    C.fence_ring(P, [(22.0, -36.0), (46.0, -36.0), (46.0, -4.0), (22.0, -4.0)], gates={0: (0.3, 0.6)}, name="pen")
    for x, y in ((-46.0, -36.0), (-2.0, -37.0), (20.0, -37.0), (46.0, 30.0), (-46.0, 30.0), (12.0, 2.0)):
        C.lamppost(P, (x, y, PAD), h=6.0, yaw=0 if y < 0 else 180, lit=False)
    C.prop(P, lambda: CA.fuel_tank(29), (-38.0, -8.0, PAD), yaw=0)
    C.prop(P, lambda: CA.gas_cylinder_rack(29), (-32.0, -8.5, PAD), yaw=180)
    C.prop(P, lambda: PI.barrel_pallet(64), (-4.0, -8.0, PAD), yaw=15)
    C.prop(P, lambda: CA.transformer_box(32), (42.0, 30.0, PAD), yaw=180)
    C.prop(P, lambda: CA.power_pole(23), (46.5, 34.0, PAD), yaw=90, collide=((0.5, 0.5, 9.0), (0, 0, 4.5)))
    C.prop(P, lambda: PI.cable_drum_stack(62), (-42.0, 2.0, PAD), yaw=70)
    weather(P, 11.0, 10.0, rnd, at=(33.0, 14.0))
    weather(P, 12.0, 8.0, rnd, at=(-20.0, -20.0))
    return _finish(P)


# ================================================================== the growlab
def growlab():
    """The growlab (30 x 20 m): a 20 m barrel-vault glasshouse of raised grow beds under cyan grow lights (a few still
    powered), irrigation along the beds and benches against the knee walls, its door at the east end; joined at its
    west end by a doorway to the lab, a porthole box with a roller door to the yard. Water butts, a compost bay and
    solar rows outside."""
    rnd = random.Random(14)
    P = _root("growlab")
    concrete_pad(P, 38.0, 28.0)
    V, (zk, rise, R, zc) = C.glass_vault(P, "vault", 20.0, 14.0, 4.0, knee=1.25, loc=(4.0, 0.0, 0.0), doors=(1,), gables=(1,), seed=14)
    lab, walls, H = C.shed(P, "lab", 9.0, 14.0, floors=1, loc=(-10.5, 0.0, 0.0), doorways=(("right", 0.0), ("front", 2.25)),
                           rollers=(("left", -2.0, 3.0, 3.0, 2.6),),
                           plans={"front": lambda b, f: "porthole" if b == 0 else "plain", "back": lambda b, f: ("porthole", "fan")[b % 2],
                                  "left": lambda b, f: "porthole" if b == 2 else "plain", "right": lambda b, f: "plain"}, seed=15)
    C.logo_panel(walls["front"], (-2.25, C.CREST - 0.02, 3.25), w=4.0, h=1.3, code="GROWLAB")
    C.prop(P, lambda: PU.antenna_array(), (-12.5, 3.5, H), yaw=0, collide=False)
    # inside the vault: two long raised beds, benches along the knee walls, grow lights hung from the ribs
    In = group(P, "beds", (4.0, 0.0, C.SLAB))
    soil = K.mat("bed_soil", "#5b4636", rough=1.0, ao=0.3)
    leafs = [M(c, wear=0, grime=0.1, scratches=0, rough=0.7) for c in ("#5f8a4a", "#6f9a3a", "#7fa85a", "#8a9a48", "#9a8a40")]
    for y in (-2.65, 2.65):
        C.framed(In, "bed", (1.5, y, 0.35), (16.0, 1.6, 0.7), C.concrete(), C.steel(), fw=0.08)
        solid(In, "bed", (16.0, 1.6, 0.7), (1.5, y, 0.35))
        quiet(K.box("bed_soil", (15.8, 1.45, 0.04), soil, loc=(1.5, y, 0.71), bev=0, parent=In))
        for k in range(48):
            x = -6.3 + k * 0.33
            if rnd.random() < 0.15:
                continue
            for dy in (-0.4, 0.4):
                hgt = rnd.uniform(0.15, 0.6)
                quiet(K.cyl("plant", rnd.uniform(0.1, 0.2), hgt, rnd.choice(leafs), loc=(x, y + dy, 0.73 + hgt / 2), r2=0.02, segs=6, bev=0, parent=In))
        C.pipe_run(In, [(-6.6, y, 0.85), (9.6, y, 0.85)], r=0.04, mat=M(SEA, metal=0.2, wear=0.3), brackets=4.0)
    for y in (-6.0, 6.0):
        for x in (-4.0, 0.5, 5.0):
            C.prop(In, lambda: PF.potting_bench(), (x, y, 0), yaw=0 if y > 0 else 180, pad=0.05)
    for k, x in enumerate((-5.0, -1.0, 3.0, 7.0)):
        for y in (-2.65, 2.65):
            on = (k + (y > 0)) % 3 == 0
            quiet(K.box("grow_light", (3.0, 0.3, 0.08), C.graphite(), loc=(x, y, 3.2), bev=0.01, parent=In))
            quiet(K.box("grow_lens", (2.9, 0.22, 0.02), glow(CYAN, 5) if on else C.glass(), loc=(x, y, 3.155), bev=0, parent=In))
            for s in (-1, 1):
                quiet(K.box("grow_hanger", (0.03, 0.03, 0.9), C.graphite(), loc=(x + s * 1.3, y, 3.65), bev=0, parent=In))
            if on:
                K.point("grow", (x, y, 2.9), CYAN, 60, 0.3).parent = In
    # the lab: a bench, shelves, a cabinet
    L_in = group(P, "lab_in", (-10.5, 0.0, C.SLAB))
    C.prop(L_in, lambda: PU.data_cabinet(), (-3.6, 4.5, 0), yaw=90)
    C.prop(L_in, lambda: PU.server_rack_wreck(), (-3.6, 1.5, 0), yaw=90)
    C.framed(L_in, "lab_bench", (2.5, 4.8, 0.45), (3.0, 1.0, 0.9), M(BONE, wear=0.3), C.steel(), fw=0.06)
    solid(L_in, "lab_bench", (3.0, 1.0, 0.9), (2.5, 4.8, 0.45))
    # the yard: water butts, compost, solar rows behind, a lamppost at the door
    C.prop(P, lambda: PF.water_butt_stand(), (15.8, 4.5, PAD), yaw=-90)
    C.prop(P, lambda: PF.cold_frame(), (-2.0, -9.5, PAD), yaw=0)
    C.prop(P, lambda: PU.solar_farm_row(), (2.0, 10.5, PAD), yaw=0)
    C.lamppost(P, (16.5, -3.5, PAD), h=4.5, yaw=-90, lit=True)
    C.prop(P, lambda: CB.pallet(27), (-17.0, -4.0, PAD), yaw=85)
    weather(P, 10.0, 7.0, rnd, at=(4.0, 0.0))
    return _finish(P)


# ================================================================== the chemical lake facility
LAKE = (0.0, -36.0)       # the pool's centre (pois: basin and pool at the POI's (0, 36))
SHORE = 20.0              # nothing nearer the pool's centre than this (the basin's widest bank, 16 m * 1.15 + 1.4)


def chemical_lake_facility():
    """The chemical lake facility (40 x 30 m) on a low concrete apron that stops 23 m from the lake's centre: the
    process hall at the back left (two storeys high), the big capsule chemical tank at the back right (the signature
    volume) with its valve manifold, the pump house in front with two intake pipes running out to headwalls on the
    bank, an outfall pipe from the tank to the bank on the right, a pipe rack between the hall and the pump house,
    chemical drums and waste bins, and caution signs on the bank. The pipes end at the bank: the pool is left clear."""
    rnd = random.Random(23)
    P = _root("chemical_lake_facility")
    concrete_pad(P, 42.0, 25.0, h=0.15, edge=0.6, loc=(0, 1.0, 0))
    A = 0.15
    hall, walls, H = C.shed(P, "process_hall", 22.0, 12.0, floors=2, loc=(-8.0, 7.5, A - 0.15),
                            rollers=(("front", -5.0, 5.0, 3.75, 3.0),), doorways=(("front", 5.0), ("right", 3.0)),
                            plans={"front": lambda b, f: ("window" if b in (0, 4) else "plain") if f else ("plain" if b in (0, 1, 3) else "louvre"), "left": lambda b, f: "louvre" if f else "fan",
                                   "back": lambda b, f: "window" if f else "plain", "right": lambda b, f: "broken" if f else "plain"}, seed=23)
    C.logo_panel(walls["front"], (0.0, C.CREST - 0.02, C.STOREY + C.SLAB + 1.0), w=8.0, h=2.25, code="CHEM WORKS 2")
    C.prop(P, lambda: PI.chimney_steel(49), (-21.5, 10.0, A), yaw=0)
    tank = C.roof_tank(P, (12.0, 7.0, A), r=3.25, h=6.0, code="CHEM-1")
    for a0 in (-0.5, 0.5):
        PI.hazard_diamond(tank, (3.27 * math.sin(a0), -3.27 * math.cos(a0), 0.8 + 2.4), (0, 0, math.degrees(a0)), s=0.45)
    C.prop(P, lambda: PI.valve_manifold(46), (12.0, 0.5, A), yaw=0)
    C.pipe_run(P, [(12.0, 3.6, A + 1.2), (12.0, 1.8, A + 1.2)], r=0.2, mat=M(MUSTARD, metal=0.2, wear=0.4))
    # the pump house on the shore and its intakes to the bank
    C.shed(P, "pump_house", 8.0, 5.0, floors=1, loc=(-6.0, -8.5, A - 0.15), doorways=(("right", 0.0),),
           plans={"front": lambda b, f: "louvre", "left": lambda b, f: "fan", "back": lambda b, f: "window"}, seed=24, gear=False)
    yb = LAKE[1] + SHORE + 0.5
    for x in (-8.0, -4.0):
        # over the headwall and down its face, the dip pipe stopping short of the lake
        C.pipe_run(P, [(x, -11.0, 0.95), (x, yb - 0.25, 0.95), (x, yb - 0.25, -0.5)], r=0.25, mat=M(MUSTARD, metal=0.2, wear=0.45, grime=0.5), brackets=1.5)
        quiet(K.box("headwall", (1.6, 1.2, 1.9), C.concrete(), loc=(x, yb + 0.6, 0.05), bev=0.04, parent=P))
        solid(P, "headwall", (1.6, 1.2, 1.9), (x, yb + 0.6, 0.05))
        for y in (-12.5, -14.0):
            quiet(K.box("sleeper", (0.9, 0.3, 1.0), C.concrete(), loc=(x, y, 0.2), bev=0.02, parent=P))
            solid(P, "sleeper", (0.9, 0.3, 0.7), (x, y, 0.35))
    for x in (-8.0, -4.0):
        PI.gate_valve(P, (x, -11.8, 0.95), axis="y", r=0.25)
    C.prop(P, lambda: CB.caution_sign(30), (-11.0, yb + 1.5, A), yaw=180)
    C.prop(P, lambda: CB.caution_sign(30), (4.0, yb + 1.5, A), yaw=180)
    # the outfall from the tank to the bank
    C.pipe_run(P, [(14.0, 3.6, A + 0.6), (14.0, yb + 0.6, A + 0.6)], r=0.2, mat=M(STEEL, metal=0.3, wear=0.5, grime=0.6), brackets=2.0)
    quiet(K.box("outfall_wall", (1.4, 1.0, 1.5), C.concrete(), loc=(14.0, yb + 0.5, -0.1), bev=0.04, parent=P))
    solid(P, "outfall", (1.4, 1.0, 1.5), (14.0, yb + 0.5, -0.1))
    quiet(K.box("outfall_stain", (0.5, 0.02, 0.9), M("#6f8a3a", wear=0, grime=0.3, scratches=0), loc=(14.0, yb - 0.01, 0.0), bev=0, parent=P))
    # a pipe rack from the hall over to the pump house
    for y in (-1.5, -4.5):
        for x in (-7.5, -4.5):
            quiet(K.box("rack_post", (0.25, 0.25, 4.0), C.steel(), loc=(x, y, A + 2.0), bev=0.02, parent=P))
            solid(P, "rack_post", (0.25, 0.25, 4.0), (x, y, A + 2.0))
        quiet(K.box("rack_beam", (3.6, 0.25, 0.3), C.graphite(), loc=(-6.0, y, A + 4.0), bev=0.02, parent=P))
    for k, col in enumerate((SEA, MUSTARD, STEEL)):
        x = -7.0 + k * 0.5
        C.pipe_run(P, [(x, 1.4, A + 4.6 - 0.3 * k), (x, 1.0, A + 4.6 - 0.3 * k), (x, -5.9, A + 4.6 - 0.3 * k), (x, -5.9, A + 2.6)], r=0.12, mat=M(col, metal=0.2, wear=0.4))
    # drums, bins and gas by the hall, a fence round the tank's yard, lampposts
    C.prop(P, lambda: PI.chemical_drums(64), (4.5, -3.0, A), yaw=-20)
    C.prop(P, lambda: PI.hazard_waste_bin(65), (-15.0, -3.0, A), yaw=90)
    C.prop(P, lambda: PI.barrel_pallet(67), (-17.0, -6.5, A), yaw=10)
    C.prop(P, lambda: CA.gas_cylinder_rack(30), (5.5, 12.0, A), yaw=-90)
    C.prop(P, lambda: PI.storage_tank_tall(41), (18.0, -4.0, A), yaw=0)
    for x, y in ((-19.0, -10.0), (8.0, -10.0), (19.0, 12.5)):
        C.lamppost(P, (x, y, A), h=5.0, yaw=0 if y < 0 else 180, lit=False)
    weather(P, 11.0, 6.0, rnd, z=A, at=(-8.0, 7.5))
    return _finish(P)


# ================================================================== the roadside kiosk
def kiosk_wall(P, name, L, loc, yaw, cuts=(), top=2.8, z0=0.2):
    """A kiosk wall from -L/2 to L/2 (wall frame, outside -y): a concrete kick strip, ribbed bone cladding round the
    cuts (x0, x1, z0, z1), steel-blue posts at the ends and the middle, a graphite coping; collides round the cuts."""
    G = group(P, name, loc, yaw)
    quiet(K.box("kick", (L, 0.3, 0.25), C.concrete(), loc=(0, 0.05, z0 + 0.125), bev=0.02, parent=G))
    for r in C.rect_minus(-L / 2, L / 2, z0 + 0.25, top, list(cuts)):
        C.corrugated(G, "kiosk_clad", r[0], r[1], r[2], r[3], C.bone(int(L)), pitch=0.5, depth=0.05)
    for x in (-L / 2 + 0.1, 0.0, L / 2 - 0.1):
        if any(c[0] - 0.2 < x < c[1] + 0.2 for c in cuts):
            continue
        quiet(K.box("kiosk_post", (0.2, 0.22, top - z0 + 0.1), C.steel(), loc=(x, -0.08, (z0 + top) / 2), bev=0.02, parent=G))
    if not cuts:
        quiet(K.box("kiosk_belt", (L, 0.2, 0.2), C.steel(), loc=(0, -0.1, z0 + 1.0), bev=0.02, parent=G))
    for x0, x1, a, b in C.holes_cut(-L / 2, L / 2, 0.0, top, [(c[0], c[1], c[2], c[3]) for c in cuts]):
        solid(G, "kiosk_wall", (x1 - x0, 0.25, b - a), ((x0 + x1) / 2, 0.05, (a + b) / 2))
    return G


def roadside_kiosk():
    """The roadside kiosk (6 x 5 m footprint; its front, -z in the POI, faces the road): a 5 x 3.5 m framed box on a
    concrete pad, its counter towards the road (Blender +Y) under a steel canopy with a light bar, the roll-up shutter
    half down over it, a walk-in doorway in the right side, a porthole in the left, shelves and a stool inside, an air
    handler and a capsule water tank (its rounded volume) on the roof, a square lamppost with its code by the road."""
    rnd = random.Random(40)
    P = _root("roadside_kiosk")
    Z = 0.2
    concrete_pad(P, 10.0, 8.0, h=Z, edge=0.6)
    x0, x1, y0, y1 = -2.5, 2.5, -2.25, 1.25
    top = Z + 2.6
    K.box("kiosk_floor", (5.0, 3.5, 0.1), C.concrete(), loc=(0, -0.5, Z - 0.03), bev=0.01, parent=P)
    # walls: the road side (+y) with the counter opening, the back, the right with the door, the left with a porthole
    counter = (-1.8, 1.8, Z + 1.05, Z + 2.25)
    Fw = kiosk_wall(P, "kiosk_front", 5.0, (0, y1, 0), 180, cuts=[counter], top=top, z0=Z)
    kiosk_wall(P, "kiosk_back", 5.0, (0, y0, 0), 0, top=top, z0=Z)
    Rw = kiosk_wall(P, "kiosk_right", 3.5, (x1, -0.5, 0), 90, cuts=[(-0.78, 0.78, 0.0, Z + 2.25)], top=top, z0=Z)
    Lw = kiosk_wall(P, "kiosk_left", 3.5, (x0, -0.5, 0), -90, cuts=[(-0.6, 0.6, Z + 1.0, Z + 2.2)], top=top, z0=Z)
    C.porthole(Lw, 0.0, Z + 1.6, r=0.4, plate=1.2)
    C.doorway(Rw, 0.0, Z, h=2.05, w=1.25, swing=-1)
    for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
        quiet(K.box("kiosk_corner", (0.3, 0.3, top - Z + 0.25), C.steel(), loc=(x, y, (Z + top + 0.25) / 2), bev=0.03, parent=P))
        quiet(K.box("kiosk_corner_block", (0.36, 0.36, 0.25), C.graphite(), loc=(x, y, top + 0.05), bev=0.02, parent=P))
        if y == y1:
            C.hazard_band(group(P, "kiosk_corner_haz", (x, y + 0.16, 0), 180), (0, 0, Z + 0.1), 0.3, 0.5)
    # the roof, its coping, the tank and the air handler
    quiet(K.box("kiosk_roof", (5.4, 3.9, 0.2), C.graphite(), loc=(0, -0.5, top + 0.1), bev=0.03, parent=P))
    solid(P, "kiosk_roof", (5.4, 3.9, 0.2), (0, -0.5, top + 0.1))
    quiet(K.box("kiosk_coping", (5.5, 4.0, 0.08), C.steel(), loc=(0, -0.5, top + 0.24), bev=0.01, parent=P))
    # the rounded volume: a small capsule water tank lying on two saddles
    T = group(P, "water_capsule", (-1.3, -1.4, top + 0.28))
    body = M(BONE, wear=0.25, grime=0.45, scratches=0.2, moss=0.1)
    quiet(K.cyl("cap_body", 0.42, 1.3, body, loc=(0, 0, 0.62), rot=(0, 90, 0), segs=20, bev=0, parent=T))
    for s in (-1, 1):
        quiet(K.sphere("cap_end", 0.42, body, loc=(s * 0.65, 0, 0.62), scale=(0.45, 1, 1), parent=T))
        quiet(K.box("cap_saddle", (0.16, 0.7, 0.3), C.graphite(), loc=(s * 0.4, 0, 0.15), bev=0.01, parent=T))
        quiet(K.cyl("cap_band", 0.44, 0.08, C.steel(), loc=(s * 0.4, 0, 0.62), rot=(0, 90, 0), segs=20, bev=0, parent=T))
    quiet(K.cyl("cap_band_m", 0.435, 0.3, C.mustard(0.3), loc=(0, 0, 0.62), rot=(0, 90, 0), segs=20, bev=0, parent=T))
    solid(T, "capsule", (2.1, 0.9, 1.05), (0, 0, 0.52))
    C.ac_unit(P, (1.0, -1.0, top + 0.28), w=1.5, d=1.0, h=0.7)
    C.pipe_run(P, [(-1.0, -1.3, top + 0.8), (-0.6, -1.3, top + 0.8), (-0.6, -2.45, top + 0.8), (-0.6, -2.45, Z + 0.4)], r=0.06)
    # the counter, the shutter half down, the canopy over it
    quiet(K.box("counter_top", (3.8, 0.75, 0.08), C.steel(), loc=(0, y1 + 0.12, Z + 1.05), bev=0.02, parent=P))
    quiet(K.box("counter_lip", (3.8, 0.06, 0.12), C.mustard(0.4), loc=(0, y1 + 0.48, Z + 1.03), bev=0, parent=P))
    solid(P, "counter", (3.8, 0.75, 0.12), (0, y1 + 0.12, Z + 1.03))
    S_ = group(P, "shutter", (0, y1, 0), 180)
    C.corrugated(S_, "door_curtain", -1.8, 1.8, Z + 1.75, Z + 2.25, M(STEEL, metal=0.35, wear=0.3, grime=0.45), y=0.05, pitch=0.25, depth=0.04, horizontal=True)
    quiet(K.box("shutter_bar", (3.6, 0.1, 0.1), C.mustard(0.5), loc=(0, 0.05, Z + 1.75), bev=0.01, parent=S_))
    quiet(K.box("shutter_box", (4.0, 0.35, 0.35), C.steel(), loc=(0, 0.3, Z + 2.4), bev=0.03, parent=S_))
    C.canopy(Fw, (0, C.CREST - 0.1, Z + 2.4), 4.6, depth=1.6, ties=False)    # the roof is right over it
    # inside: shelves along the back, a stool, a till
    In = group(P, "kiosk_in", (0, -0.5, Z))
    for k, x in enumerate((-1.4, 0.0, 1.4)):
        C.framed(In, "shelf", (x, -1.45, 0.9), (1.2, 0.4, 1.8), M(BONE, wear=0.4), C.steel(), fw=0.05)
        for z in (0.5, 1.0, 1.5):
            quiet(K.box("shelf_board", (1.15, 0.38, 0.03), C.graphite(), loc=(x, -1.45, z), bev=0, parent=In))
            for j in range(rnd.randint(1, 3)):
                quiet(K.box("shelf_tin", (0.12, 0.12, 0.18), M(rnd.choice((SEA, MUSTARD, BONE, SIGNAL)), wear=0.4), loc=(x + rnd.uniform(-0.45, 0.45), -1.45, z + 0.11), bev=0, parent=In))
    solid(In, "shelves", (4.2, 0.4, 1.8), (0, -1.45, 0.9))
    quiet(K.cyl("stool", 0.2, 0.7, C.graphite(), loc=(-1.2, 0.6, 0.35), segs=10, bev=0.01, parent=In))
    C.framed(In, "till", (0.9, 1.45, 1.1), (0.45, 0.35, 0.25), M(BONE, wear=0.4), C.graphite(), fw=0.03)
    # the lamppost by the road with the kiosk's code, a bin and a bench
    C.lamppost(P, (3.8, 3.2, Z), h=4.5, yaw=180, lit=True, board="K-4")
    C.prop(P, lambda: PS.trash_bin(), (3.6, -2.6, Z), yaw=0)
    weather(P, 2.5, 1.75, rnd, z=Z, at=(0.0, -0.5))
    return _finish(P)


SITES = {
    "packing_station_vegetable": packing_station_vegetable,
    "packing_station_fruit": packing_station_fruit,
    "silo_cluster": silo_cluster,
    "silo_district": silo_district,
    "growlab": growlab,
    "chemical_lake_facility": chemical_lake_facility,
    "roadside_kiosk": roadside_kiosk,
}

# Spots a player must reach (Blender frame), per site: checked by walk_settlers.py on the exported collider.
WALK = {
    "packing_station_vegetable": {"targets": {"on the dock": (0.0, -13.5, 1.3), "in the hall": (0.0, 0.0, 1.3), "by the cold room": (8.0, 8.0, 1.3),
                                              "at the back door": (-4.0, 12.7, 1.3), "behind the hall": (8.0, 14.5, 0.3)}},
    "packing_station_fruit": {"targets": {"on the dock": (0.0, -13.5, 1.3), "in the hall": (0.0, 0.0, 1.3), "by the cold room": (8.0, 8.0, 1.3),
                                          "at the back door": (-4.0, 12.7, 1.3), "behind the hall": (8.0, 14.5, 0.3)}},
    "silo_cluster": {"targets": {"in the tower": (-11.25, -2.25, 0.3), "on the tower's deck": (-8.0, -2.4, 16.3), "in the gallery": (3.0, 0.0, 16.3),
                                 "on a front silo's roof": (-1.5, -3.0, 16.56), "on a back silo's roof": (7.5, 3.0, 16.56), "in the dump shed": (-10.0, -12.0, 0.3)}},
    "silo_district": {"targets": {"in the tower": (18.25, 18.25, 0.3), "on the tower's deck": (16.0, 18.4, 24.3), "in the gallery": (-24.0, 16.0, 24.3),
                                  "on the far silo's roof": (-30.0, 12.0, 24.61), "on a back silo's roof": (6.0, 20.0, 24.61),
                                  "in the grain store": (33.0, 10.0, 0.3), "in the machine shed": (-20.0, -24.0, 0.3), "in the pen": (34.0, -20.0, 0.3)}},
    "growlab": {"targets": {"in the vault": (4.0, 0.0, 0.3), "between the beds": (-2.0, 0.0, 0.3), "in the lab": (-10.5, 0.0, 0.3)}},
    "chemical_lake_facility": {"targets": {"in the hall": (-8.0, 7.0, 0.3), "in the pump house": (-6.0, -8.5, 0.3), "by the intake": (-6.0, -15.0, 0.15),
                                           "by the tank": (8.0, 2.5, 0.15)}},
    "roadside_kiosk": {"targets": {"in the kiosk": (0.0, -0.6, 0.2), "at the counter": (0.0, 2.0, 0.2)}},
}
