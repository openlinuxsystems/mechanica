"""The Settler Patchwork buildings (docs/art-direction.md), assembled from settler_kit.py and the settler props: the
farmer's shack, the farmers' hideout, three camps, two scrapyards, the haymaze and the Scrap City garage.

Each keeps its placeholder prefab's footprint and every marker of it (tools/author-poi-placeholders.py): where the
markers stand 0.3 m up (the placeholder's pad), the model has a packed-earth yard pad that high, its edges ramped to
the ground; camps whose markers stand on the ground have none. Markers keep clear ground round them. Blender frame:
origin on the ground at the POI's centre, front towards -Y (the POI's +Z), so a marker at the POI's (x, y, z) is at
Blender (x, -z, y). WALK lists, per building, the spots walk_settlers.py checks a player can reach.
"""
import math
import random

import bpy

import kit as K
import colony_kit as C
import settler_kit as S
from colony_kit import group, quiet, quiet_all, solid
from families import M, glow, garden_plot, BONE, SEA, BRICK, MUSTARD, RUST, ZINC, WARM

import props_settler_a as PA
import props_settler_b as PB
import props_salvage as PS
import props_farm as PF
import props_camp as PC
import props as PR

PAD = 0.3


def _root(name):
    return K.link(bpy.data.objects.new(name, None))


def _finish(root):
    if K.EXPORT:
        C.lighten(root)
    return root


def ground_patch(P, r, loc=(0, 0), seed=1, col="#7d6b52"):
    """Bare trodden earth round a camp that has no pad (drawn only)."""
    d = K.disc("trodden", r, K.mat("trodden_earth", col, rough=1.0, grime=0.2, noise_scale=1.5, ao=0.2), loc, z=0.015, segs=24, wobble=0.25, seed=seed)
    d.parent = P
    return quiet(d)


# ================================================================== the farmer's shack
def farmer_shack():
    """The farmer's half-dome shack (12 x 10 m): the dome walked into by its hatch door, a plank lean-to store on its
    right, the trade counter on its left front under a striped awning, a water tank on a stand behind, a fenced
    garden at the back, and the yard pad the farm cart, the crate and the barrel stand on."""
    rnd = random.Random(4)
    P = _root("farmer_shack")
    S.pad(P, 20.0, 19.0, PAD, edge=1.0)
    G = group(P, "on_pad", (0, 0, PAD))
    S.half_dome(G, "shack", 3.0, 7.5, loc=(-0.5, 0.75, 0), seed=5, knee=0.5,
                front={"door": (0.9, 1.25, 2.5, "hatch"), "port": [(-1.4, 2.4)]}, back={"window": [(0.0, 1.5)]})
    # inside the dome: a bunk, a stove with its pipe out through the shell, a table, shelves
    dome = (-0.5, 0.75)
    S.bedroll(G, (dome[0] - 1.7, dome[1] + 1.9, 0.05), yaw=0)
    S.stove(G, (dome[0] + 1.6, dome[1] + 2.6, 0.05), yaw=180, pipe_h=2.6)
    S.prop(G, PB.wooden_table, (dome[0] - 1.2, dome[1] - 0.6, 0.05), yaw=90)
    S.prop(G, PB.stool, (dome[0] - 0.4, dome[1] - 0.4, 0.05))
    S.prop(G, PS.junk_shelf, (dome[0] + 2.2, dome[1] - 0.8, 0.05), yaw=-90, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.sacks(G, (dome[0] + 1.9, dome[1] + 1.0, 0.05), n=4, yaw=-90, seed=6)
    # bolted on: a bulb rack over the door, the twin fan box on top, pipes arched over the shell
    S.bulb_rack(G, (0.4, -3.05, 3.05), w=2.2, n=3)
    S.fan_box(G, (dome[0] - 0.6, dome[1] - 2.2, 3.58), yaw=0)
    for y in (dome[1] - 1.2, dome[1] + 2.4):
        pts = [(dome[0] + 3.4 * math.cos(a), y, 0.55 + 3.4 * math.sin(a)) for a in [math.pi * k / 10 for k in range(11)]]
        for a, b in zip(pts[:-1], pts[1:]):
            S.pipe(G, "arch_pipe", a, b, 0.11, S.plate("brick"), segs=8)
    S.stove_pipe(G, (dome[0] + 1.48, dome[1] + 2.5, 3.4), 1.6)
    # the store: an open plank lean-to on the dome's right
    S.lean_to(G, "store", 3.0, 5.0, 2.75, 2.25, loc=(4.25, 0.25, 0), seed=7, walls=("back", "right"), wall_kind="planks", floor=True)
    S.prop(G, PC.workbench_settler, (4.25, 2.15, 0.06), yaw=0, collide=((2.0, 0.7, 1.0), (0, 0, 0.5)))
    S.log_pile(G, (5.25, -0.9, 0.06), n=8, yaw=90, seed=8)
    S.prop(G, PA.wheelbarrow, (3.4, -3.1, 0.0), yaw=200)
    S.drum(G, (6.25, -2.6, 0.0), col="sea")
    S.drum(G, (6.3, -1.85, 0.0), col="brick")
    S.bulb(G, (4.25, -1.9, 2.6), arm=0.0, energy=30)
    # the trade counter on the left front
    S.trade_counter(G, "counter", loc=(-4.85, -4.6, 0), w=2.5, seed=9)
    S.lamp_pole(G, (-6.6, -5.4, 0), h=3.25, yaw=-30)
    # water tank behind, a washing line, the garden
    S.prop(G, PB.water_tower_small, (-5.25, 3.6, 0), yaw=10, collide=((2.0, 2.0, 5.0), (0, 0, 2.5)))
    S.prop(G, PA.washing_line, (-5.0, 7.2, 0), yaw=0)
    S.crops(G, -1.5, 4.5, 5.25, 8.75, rnd, rows=4, name="garden", gate=(3.0, 4.2))
    S.crate(G, (5.0, 4.6, 0.0), yaw=10, shade=1)
    S.crate(G, (5.1, 4.6, 0.5), size=(0.6, 0.5, 0.4), yaw=-5, shade=2)
    S.junk_pile(G, (5.8, -6.6, 0), r=1.2, h=0.8, seed=11)
    S.prop(G, PA.chicken_coop, (-7.4, -6.6, 0), yaw=30, collide=((1.6, 1.2, 1.4), (0, 0, 0.7)))
    for k, (x, y) in enumerate(((-9.2, -8.6), (9.0, -8.8), (-9.4, 8.6))):
        S.straw_patch(G, (x * 0.6, y * 0.4), 0.8, rnd, z=0.0)
    return _finish(P)


# ================================================================== the camps
def campsite():
    """A campsite in the open (20 x 20 m): a cold fire ring in the middle, two tents, a walk-in hut of plates and
    sheet facing the fire, a log bench, a woodpile, a windbreak of scrap behind the tents."""
    rnd = random.Random(10)
    P = _root("campsite")
    ground_patch(P, 6.5, seed=10)
    S.fire_ring(P, (0, 0, 0), seed=10)
    S.tripod(P, (0, 0, 0))
    S.prop(P, PC.tent_ridge, (-5.0, 4.0, 0), yaw=20, collide=((2.6, 4.4, 1.4), (0, 0, 0.7)))
    S.prop(P, PC.tent_dome_patched, (5.0, 3.0, 0), yaw=-15, collide=((3.0, 3.0, 1.1), (0, 0, 0.55)))
    hut = S.lean_to(P, "hut", 4.0, 3.5, 2.75, 2.3, loc=(0, -6.25, 0), yaw=180, seed=12, walls=("back", "left", "right"), floor=True)
    S.bedroll(hut, (0.6, 0.3, 0.06), yaw=0)
    S.bedroll(hut, (-0.5, 0.3, 0.06), yaw=0, col="#7d6b52")
    S.prop(hut, PC.clothes_chest, (-1.3, 1.2, 0.06), yaw=180)
    S.prop(hut, PS.junk_shelf, (1.45, 0.6, 0.06), yaw=90, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.bulb(hut, (0, -1.4, 2.5), arm=0.0, energy=30)
    S.stove_pipe(P, (-1.5, -7.6, 2.4), 1.2)
    S.prop(P, PC.bench_plank, (-2.3, 1.4, 0), yaw=-35)
    S.prop(P, PC.bench_plank, (-1.0, -2.3, 0), yaw=20)
    S.log_pile(P, (-6.8, 0.8, 0), n=8, yaw=90, seed=16)
    S.prop(P, PC.chopping_block, (6.5, -1.8, 0))
    S.fence(P, "windbreak", (-9.0, 6.6), (-2.0, 8.6), kind="mixed", h=1.9, seed=13)
    S.fence(P, "windbreak2", (2.0, 7.8), (8.5, 6.0), kind="sheet", h=1.9, seed=14)
    S.junk_pile(P, (7.2, -7.0, 0), r=1.1, h=0.7, seed=15)
    S.tyres(P, (8.4, -4.2, 0), n=3, seed=17)
    return _finish(P)


def camp():
    """A small forest camp (14 x 14 m): a plank lean-to with a bedroll, a patched dome tent, a fire ring with a log
    bench, a rain barrel and a scrap windbreak along the back."""
    rnd = random.Random(30)
    P = _root("camp")
    ground_patch(P, 5.0, loc=(0, 0.5), seed=30)
    S.fire_ring(P, (0, -2.2, 0), seed=30)
    lt = S.lean_to(P, "lean_to", 3.5, 2.75, 2.6, 2.1, loc=(-4.0, 3.6, 0), seed=31, walls=("back", "left"), wall_kind="planks", floor=True)
    S.bedroll(lt, (0.3, 0.1, 0.06), yaw=90)
    S.crate(lt, (-1.2, 0.7, 0.06), yaw=8)
    S.bulb(lt, (0.0, -1.1, 2.45), arm=0.0, energy=25)
    S.prop(P, PC.tent_dome_patched, (4.0, 3.0, 0), yaw=-25, collide=((3.0, 3.0, 1.1), (0, 0, 0.55)))
    S.prop(P, PC.bench_plank, (-1.8, -3.6, 0), yaw=70)
    S.drum(P, (-1.7, 4.4, 0), col="sea")
    S.tripod(P, (0, -2.2, 0))
    S.log_pile(P, (-5.9, 1.6, 0), n=6, yaw=10, seed=34)
    S.fence(P, "windbreak", (-6.5, 6.3), (6.5, 6.3), kind="mixed", h=1.9, seed=32, gate=(5.5, 8.0))
    S.junk_pile(P, (5.6, -5.4, 0), r=0.9, h=0.6, seed=33)
    return _finish(P)


def large_camp():
    """The settlers' trading camp (50 x 40 m): a long half-dome hall in the middle with a porch and counter along its
    front, two market stalls, eight tents and huts in two rows, a workshop lean-to, a fire with benches, hay bales by
    the way in, a watchtower and a water tank at the back corners, and scrap fences round the back and sides."""
    rnd = random.Random(43)
    P = _root("large_camp")
    S.pad(P, 58.0, 48.0, PAD, edge=1.0)
    G = group(P, "on_pad", (0, 0, PAD))
    # the hall, its ends at x = -6 and 6 (doors off the lights' line), the porch and counter along its front
    hall = S.half_dome(G, "hall", 4.0, 12.0, yaw=90, seed=44, knee=0.5,
                       front={"door": (1.4, 1.5, 2.5, "planks"), "port": [(-1.6, 2.7)]},
                       back={"door": (-1.4, 1.5, 2.5, "hatch"), "window": [(1.6, 1.5)]})
    S.table(hall, (0.0, 1.6, 0.05), length=3.0, yaw=90, seed=45)
    S.table(hall, (0.0, -2.6, 0.05), length=3.0, yaw=90, seed=46)
    S.stove(hall, (-1.9, 4.9, 0.05), yaw=90, pipe_h=3.3)
    for k, y in enumerate((-4.6, 4.2)):
        S.bedroll(hall, (2.6, y, 0.05), yaw=0, col=("#6f7a5a", "#7d6b52")[k])
    S.prop(hall, PS.junk_shelf, (-3.0, 0.4, 0.05), yaw=90, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.sacks(hall, (2.7, 0.8, 0.05), n=5, yaw=90, seed=47)
    S.stove_pipe(G, (-4.9, -1.9, 3.95), 1.4)
    S.fan_box(G, (-2.5, -0.4, 4.42), yaw=180)
    S.lean_to(G, "porch", 10.0, 3.2, 2.5, 2.75, loc=(0, -5.0, 0), walls=(), posts=[-4.9, -1.6, 1.6, 4.9], back_posts=False, seed=48)
    S.trade_counter(G, "porch_counter", loc=(0.0, -4.95, 0), w=3.0, seed=49, booth=False)
    # the market stalls and their lamps, bulbs strung from the hall's ends
    S.trade_counter(G, "stall_w", loc=(-14.0, -3.0, 0), w=2.5, seed=50)
    S.trade_counter(G, "stall_e", loc=(14.0, -3.0, 0), w=2.5, seed=51)
    for sx in (-1, 1):
        S.lamp_pole(G, (sx * 11.0, -6.5, 0), h=3.5, yaw=180 if sx > 0 else 0, light=False)
        S.string_lights(G, (sx * 6.3, 0.0, 3.0), (sx * 11.0, -6.5, 3.35), n=4)
        S.crate(G, (sx * 15.9, -4.6, 0), yaw=12 * sx, shade=2)
        S.drum(G, (sx * 12.1, -1.9, 0), col="sea" if sx < 0 else "mustard")
    # tents and huts, two rows facing the middle
    S.lean_to(G, "hut_fw", 4.0, 3.5, 2.75, 2.3, loc=(-18.0, -12.0, 0), yaw=180, seed=52, floor=True)
    S.bedroll(G, (-18.5, -12.4, 0.06), yaw=0)
    S.prop(G, PC.tent_ridge, (-9.0, -12.0, 0), yaw=90, collide=((4.4, 2.6, 1.4), (0, 0, 0.7)))
    S.prop(G, PC.tent_dome_patched, (9.0, -12.0, 0), yaw=10, collide=((3.0, 3.0, 1.1), (0, 0, 0.55)))
    S.dome_hut(G, "hut_fe", loc=(18.0, -12.0, 0), yaw=180, seed=53)
    S.dome_hut(G, "hut_bw", loc=(-18.0, 12.0, 0), yaw=0, seed=54, kind="hatch")
    S.prop(G, PC.tent_ridge, (-9.0, 12.0, 0), yaw=80, collide=((4.4, 2.6, 1.4), (0, 0, 0.7)))
    S.lean_to(G, "hut_be", 4.0, 3.5, 2.75, 2.3, loc=(9.0, 12.0, 0), seed=55, walls=("back", "left", "right"), wall_kind="planks", floor=True)
    S.bedroll(G, (9.5, 12.4, 0.06), yaw=0, col="#7d6b52")
    S.prop(G, PC.tent_ridge, (18.0, 12.0, 0), yaw=95, collide=((4.4, 2.6, 1.4), (0, 0, 0.7)))
    # the workshop lean-to
    ws = S.lean_to(G, "workshop", 6.0, 3.5, 2.8, 2.3, loc=(-15.0, 5.5, 0), seed=56, walls=("back", "left", "right"), wall_kind="plates", floor=False)
    S.table(ws, (0.0, 1.0, 0), length=2.6, benches=False, seed=57)
    S.tyres(ws, (2.2, 0.6, 0), seed=58)
    S.drum(ws, (-2.3, 1.1, 0), col="brick")
    S.bulb(ws, (0.0, -1.4, 2.62), arm=0.0, energy=30)
    # the fire, benches round it
    S.fire_ring(G, (-4.5, -10.5, 0), seed=59)
    S.tripod(G, (-4.5, -10.5, 0))
    S.prop(G, PC.bench_plank, (-6.6, -10.2, 0), yaw=80)
    S.prop(G, PC.bench_plank, (-4.2, -12.6, 0), yaw=-10)
    # hay by the way in, junk and stores about the yard
    for x0 in (-18.0, 18.0):
        S.big_bale(G, (x0 - 1.25, -18.0, 0), rnd=rnd)
        S.big_bale(G, (x0 + 1.25, -18.2, 0), yaw=4, rnd=rnd)
        S.big_bale(G, (x0, -18.1, 1.2), yaw=-3, rnd=rnd)
        solid(G, "bales", (5.0, 1.4, 2.4), (x0, -18.1, 1.2))
    S.junk_pile(G, (21.5, 3.5, 0), r=2.0, h=1.2, seed=60)
    S.junk_pile(G, (-21.5, -5.0, 0), r=1.6, h=1.0, seed=61)
    S.sacks(G, (3.0, 6.0, 0), n=3, yaw=20, seed=62)
    S.crate(G, (-2.6, 6.4, 0), yaw=-10, shade=1)
    S.crate(G, (-2.6, 6.4, 0.5), size=(0.6, 0.5, 0.45), yaw=5, shade=3)
    S.prop(G, PS.scrap_windmill_turbine, (12.0, 6.5, 0), yaw=30, collide=((0.6, 0.6, 6.0), (0, 0, 3.0)))
    # the back corners: a watchtower and a water tank on its stand; fences round the back and sides
    S.prop(G, PS.watchtower_wood, (-21.5, 17.0, 0), yaw=15, collide=((3.4, 3.4, 9.0), (0, 0, 4.5)))
    S.prop(G, PB.water_tower_small, (21.5, 17.5, 0), yaw=-20, collide=((2.0, 2.0, 5.0), (0, 0, 2.5)))
    S.fence(G, "fence_back_w", (-26.0, 21.0), (-1.5, 21.0), kind="light", seed=63)
    S.fence(G, "fence_back_e", (1.5, 21.0), (26.0, 21.0), kind="light", seed=64)
    S.fence(G, "fence_w", (-26.0, -16.0), (-26.0, 21.0), kind="light", seed=65, gate=(16.0, 20.0))
    S.fence(G, "fence_e", (26.0, 21.0), (26.0, -16.0), kind="light", seed=66, gate=(17.0, 21.0))
    return _finish(P)


def farmers_hideout():
    """The farmers' hideout (40 x 30 m): a walled yard of plate walls with a gate in the front, a big half-dome barn
    with a loft on the left, a scaffold lookout tower at the back right with a railed deck at 10 m, a bunkhouse lean-to
    at the front right, the trade counter by the gate, crop patches, a generator shed and water gear in the yard."""
    rnd = random.Random(20)
    P = _root("farmers_hideout")
    S.pad(P, 48.0, 38.0, PAD, edge=1.0)
    G = group(P, "on_pad", (0, 0, PAD))
    # the wall round the yard, 2.75 m of plates on a timber frame, the gate in the front
    hw, hd, wh = 20.0, 15.0, 2.75
    S.plate_wall(G, "wall_front_w", -hw, -3.0, 0.0, wh, loc=(0, -hd, 0), seed=21)
    S.plate_wall(G, "wall_front_e", 3.0, hw, 0.0, wh, loc=(0, -hd, 0), seed=22)
    S.plate_wall(G, "wall_back", -hw, hw, 0.0, wh, loc=(0, hd, 0), yaw=180, seed=23)
    S.plate_wall(G, "wall_w", -hd, hd, 0.0, wh, loc=(-hw, 0, 0), yaw=-90, seed=24)
    S.plate_wall(G, "wall_e", -hd, hd, 0.0, wh, loc=(hw, 0, 0), yaw=90, seed=25)
    for x, y in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd), (-3.15, -hd), (3.15, -hd)):
        S.board(G, "wall_post", (0.3, 0.3, wh + 0.5), (x, y, (wh + 0.5) / 2), S.timber(3))
        solid(G, "wall_post", (0.3, 0.3, wh + 0.5), (x, y, (wh + 0.5) / 2))
    S.board(G, "gate_head", (6.6, 0.3, 0.3), (0, -hd, wh + 1.6), S.timber(2))
    for sx in (-1, 1):
        S.board(G, "gate_tall_post", (0.3, 0.3, wh + 1.9), (sx * 3.15, -hd - 0.05, (wh + 1.9) / 2), S.timber(3))
        leaf = group(G, "gate_leaf", (sx * 3.0, -hd - 0.1, 0.05), -sx * 105)
        S.plate_wall(leaf, "gate", 0.0 if sx < 0 else -2.9, 2.9 if sx < 0 else 0.0, 0.1, 2.4, seed=26 + sx, collide=False)
        S.bulb(G, (sx * 3.15, -hd - 0.3, wh + 1.3), arm=0.15, energy=40)
    S.board(G, "gate_sign", (2.2, 0.08, 0.9), (0, -hd - 0.2, wh + 1.05), S.plate("sea"))
    # a sheaf and a cog on the board: farmers who trade
    for k in range(3):
        S.bar(G, "sign_sheaf", (-0.5 + (k - 1) * 0.05, -hd - 0.26, wh + 0.75), (-0.5 + (k - 1) * 0.18, -hd - 0.26, wh + 1.35), 0.05, S.plate("mustard"))
    quiet(K.torus("sign_cog", 0.22, 0.05, S.plate("bone"), loc=(0.5, -hd - 0.26, wh + 1.05), rot=(90, 0, 0), segs=12, parent=G))
    # the barn: a big half dome with a loft at the back, its doorway open to the yard
    barn = S.half_dome(G, "barn", 6.0, 16.0, loc=(-9.0, 4.0, 0), seed=27, knee=0.5, row=2.0, cols=9,
                       front={"open": (-2.0, 2.0, 3.5), "door": (3.6, 1.25, 2.5, "planks"), "port": [(-3.6, 2.6)]},
                       back={"window": [(-2.0, 1.8), (2.0, 1.8)]})
    S.deck(barn, "loft", -4.5, 4.5, 4.0, 7.9, 3.0, rails=(("y0", -2.75, 4.45),), seed=28)
    S.stair(barn, "loft_stair", (-3.4, 0.0, 0.05), (-3.4, 4.0, 3.0), w=1.25, rails=(False, True), seed=29)
    for k in range(4):
        S.big_bale(barn, (-1.5 + k * 1.3 if k < 3 else 1.6, 6.4, 3.0), yaw=90 + rnd.uniform(-3, 3), rnd=rnd, size=(2.4, 1.2, 1.0))
    S.sacks(barn, (3.0, 4.6, 3.0), n=4, yaw=0, seed=30)
    S.prop(barn, PS.junk_shelf, (5.0, -3.0, 0.05), yaw=-90, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.table(barn, (2.6, 1.2, 0.05), length=2.4, yaw=90, benches=False, seed=31)
    S.drum(barn, (4.6, 2.4, 0.05), col="mustard")
    S.drum(barn, (4.7, 3.1, 0.05), col="sea")
    S.crate(barn, (-4.4, -3.0, 0.05), yaw=8)
    S.bulb(barn, (0.0, 0.0, 4.6), arm=0.0, energy=60)
    S.fan_box(G, (-9.0, 11.0, 6.2), yaw=180)
    S.stove_pipe(G, (-5.2, 9.5, 5.2), 1.8)

    # the lookout tower at the back right
    def mast(D):
        S.pipe(D, "mast", (1.6, 1.6, 10.0), (1.6, 1.6, 15.0), 0.05, S.zinc(), segs=8)
        S.bulb_rack(D, (1.6, 1.6, 13.0), w=1.0, n=2, yaw=45)
        quiet(K.sphere("dish", 0.6, S.plate("bone"), loc=(1.3, 1.9, 14.2), scale=(1, 1, 0.3), parent=D))
    S.scaffold_tower(G, "lookout", (14.0, 10.0, 0), size=4.5, h=10.0, seed=32, top=mast)
    # the bunkhouse at the front right, its door on the yard
    bh = S.lean_to(G, "bunkhouse", 8.0, 5.0, 3.0, 2.5, loc=(10.0, -9.0, 0), yaw=180, seed=33, walls=("back", "left", "right"), wall_kind="planks",
                   floor=True)
    S.plate_wall(bh, "bunk_front", -4.0, 4.0, 0.0, 2.95, loc=(0, -2.5, 0), seed=34, holes=[(-2.2, -0.8, 0.0, 2.6), (0.8, 2.6, 1.0, 1.9)])
    S.door(group(bh, "bunk_door", (0, -2.5, 0)), -1.5, 0.05, kind="hatch", seed=35)
    S.window(group(bh, "bunk_win", (0, -2.5, 0)), 1.7, 1.0, w=1.6, h=0.9, y=-0.06, seed=36)
    for k in range(3):
        S.bedroll(bh, (-3.0 + k * 1.1, 1.3, 0.06), yaw=0, col=("#6f7a5a", "#7d6b52", "#5f7a8a")[k])
    S.stove(bh, (3.1, 1.7, 0.06), yaw=180, pipe_h=2.6)
    S.table(bh, (2.0, -0.6, 0.06), length=1.8, benches=True, seed=37)
    # the counter by the gate, facing the way in
    S.trade_counter(G, "counter", loc=(-6.0, -11.5, 0), yaw=90, w=2.5, seed=38)
    # the yard
    S.crops(G, -1.5, 5.0, 2.0, 9.0, rnd, rows=4, name="crops_a")
    S.crops(G, -1.5, 5.0, -6.5, -1.5, rnd, rows=3, kind="beans", name="crops_b")
    S.prop(G, PS.generator_shed, (17.5, 1.5, 0), yaw=-90, collide=((2.8, 3.2, 2.1), (-0.15, -0.6, 1.05)))
    S.tank_on_stand(G, (17.4, -3.4, 0), r=0.8, h=1.4, stand=2.0, col="bone", name="still_tank")
    S.pipe_run(G, [(17.4, -2.6, 2.6), (17.4, -0.6, 2.6), (17.4, -0.6, 1.4)], r=0.06, col="copper")
    S.prop(G, PS.rain_catcher, (8.0, 4.0, 0), yaw=0, collide=((0.6, 0.6, 2.4), (0, 0, 1.2)))
    S.tank_on_stand(G, (-17.0, -12.0, 0), r=1.0, h=1.8, stand=2.5, col="sea")
    S.junk_pile(G, (16.5, -11.0, 0), r=1.8, h=1.0, seed=39)
    S.drum(G, (-1.8, -12.9, 0), col="brick")
    S.drum(G, (-1.1, -13.3, 0), col="sea", lying=True, yaw=30)
    S.lamp_pole(G, (6.5, -3.5, 0), h=3.5, yaw=200)
    S.lamp_pole(G, (-2.5, 10.5, 0), h=3.5, yaw=90)
    S.prop(G, PS.scrap_windmill_turbine, (-17.0, 12.5, 0), yaw=40, collide=((0.6, 0.6, 6.0), (0, 0, 3.0)))
    return _finish(P)


# ================================================================== the scrapyards
def farmbot_scrapyard():
    """The Farmbot scrapyard (60 x 50 m): six heaps of scrap on burnt ground where the loot markers had their boxes,
    a sheet-metal fence round the back and sides (open to the south, where the signs and cones are), a lean-to over
    the shelves against the back fence, a domed office hut, a derrick with its magnet over a heap, and lamps."""
    rnd = random.Random(32)
    P = _root("farmbot_scrapyard")
    S.pad(P, 68.0, 58.0, PAD, edge=1.0, mat=K.mat("burnt_earth", "#5f5248", rough=1.0, grime=0.4, noise_scale=1.5, ao=0.3))
    G = group(P, "on_pad", (0, 0, PAD))
    # the heaps (the placeholder's boxes, POI z flipped to Blender y)
    for k, (s, x, y) in enumerate(((10, -18, 12), (8, 6, 16), (12, 16, -6), (7, -8, -10), (9, -22, -14), (6, 24, 18))):
        S.junk_pile(G, (x, y, 0), r=0.45 * s, h=0.25 * s, seed=70 + k, yaw=(x * 7) % 45)
    # fences: back, sides, the two ends of the open front
    S.fence(G, "fence_back", (30.4, 25.4), (-30.4, 25.4), kind="sheet", h=2.2, seed=71, posts="pipe")
    S.fence(G, "fence_e", (30.4, -25.5), (30.4, 25.4), kind="sheet", h=2.2, seed=72, posts="pipe")
    S.fence(G, "fence_w", (-30.4, 25.4), (-30.4, -25.5), kind="sheet", h=2.2, seed=73, posts="pipe")
    S.fence(G, "fence_front_w", (-30.4, -25.5), (-13.0, -25.5), kind="mixed", h=2.0, seed=74, posts="pipe")
    S.fence(G, "fence_front_e", (13.0, -25.5), (30.4, -25.5), kind="mixed", h=2.0, seed=75, posts="pipe")
    # the shelves' lean-to against the back fence
    S.lean_to(G, "shelter", 8.0, 3.5, 2.8, 2.4, loc=(-4.0, 23.45, 0), seed=76, walls=("left", "right"), wall_kind="plates")
    S.bulb(G, (-4.0, 22.2, 2.45), arm=0.0, energy=30)
    # the office: a dome hut, its door on the yard
    hut = S.dome_hut(G, "office", loc=(-23.0, -6.0, 0), yaw=90, seed=77, kind="hatch")
    S.table(hut, (0.9, 0.8, 0.05), length=1.6, yaw=90, benches=False, seed=78)
    S.crate(hut, (-1.2, 1.5, 0.05), yaw=8)
    S.fan_box(G, (-23.0, -6.0, 2.9), yaw=90, n=1)
    # the derrick, its magnet over the north heap
    S.derrick(G, "derrick", (14.0, 21.0, 0), yaw=-58, h=13.0, jib=9.0, seed=79)
    # lamps and odds
    S.lamp_pole(G, (2.5, -3.5, 0), h=4.0, yaw=200)
    S.lamp_pole(G, (-10.5, 5.0, 0), h=4.0, yaw=60)
    S.lamp_pole(G, (20.5, -16.0, 0), h=4.0, yaw=150)
    S.drum(G, (-26.5, -2.2, 0), col="brick")
    S.drum(G, (-25.8, -2.9, 0), col="sea", lying=True, yaw=40)
    S.tyres(G, (9.5, -12.5, 0), seed=80)
    S.tyres(G, (-12.0, 18.5, 0), seed=81)
    S.prop(G, PS.battery_rack_settler, (-20.5, -2.4, 0), yaw=90, collide=((2.4, 1.0, 1.8), (0.4, 0, 0.9)))
    # a capsule stripped for its plates in the back corner, plates stacked by it; a heap of tyres by the way in
    S.capsule(G, "stripped_capsule", (-25.5, 19.5, 0), yaw=150, state="stripped", seed=82)
    S.junk_pile(G, (-25.0, 12.5, 0), r=1.6, h=0.9, seed=83)
    for k, (x, y) in enumerate(((-20.8, 20.6), (-19.2, 21.6))):
        Pl = group(G, "plate_stack", (x, y, 0), rnd.uniform(-15, 15))
        for j in range(rnd.randint(4, 7)):
            quiet(K.box("stack_plate", (1.5, 1.1, 0.04), S.plate(rnd.choice(("bone", "bone", "steel"))), loc=(0, 0, 0.04 + j * 0.045),
                        rot=(0, 0, rnd.uniform(-5, 5)), bev=0, parent=Pl))
        solid(Pl, "stack", (1.5, 1.1, 0.35), (0, 0, 0.17))
    S.tyres(G, (15.5, -22.0, 0), seed=84)
    S.tyres(G, (17.0, -22.6, 0), n=2, seed=85)
    for k, (x, y) in enumerate(((-11.5, -20.5), (-10.8, -21.3), (-12.3, -21.2))):
        S.drum(G, (x, y, 0), col=("sea", "rust", "brick")[k], lying=k == 2, yaw=30 * k)
    return _finish(P)


CAPSULES = {(-16, -10): "hut", (-8, -10): "stripped", (0, -10): "whole", (8, -10): "stripped", (16, -10): "whole",
            (-16, 10): "whole", (-8, 10): "whole", (0, 10): "stripped", (8, 10): "whole", (16, 10): "stripped"}


def capsule_scrapyard():
    """The capsule scrapyard (50 x 40 m): ten colony drop capsules in two rows along an aisle, some whole, some
    stripped to their ribs for plate, one re-skinned as a hut; a gantry crane over the aisle, plates stacked for
    trade, a lean-to over the shelves at the back fence, fences round it with a gate between the signs."""
    rnd = random.Random(41)
    P = _root("capsule_scrapyard")
    S.pad(P, 58.0, 48.0, PAD, edge=1.0)
    G = group(P, "on_pad", (0, 0, PAD))
    for k, ((x, y), state) in enumerate(sorted(CAPSULES.items())):
        S.capsule(G, f"capsule{k}", (x, y, 0), yaw=180 if y < 0 else 0, state=state, seed=90 + k)
    # inside the hut capsule
    S.bedroll(G, (-16.6, -11.3, 0.05), yaw=90)
    S.stove(G, (-15.0, -11.0, 0.05), yaw=-90, pipe_h=0.1)
    S.crate(G, (-17.6, -9.6, 0.05), yaw=8)
    # the gantry over the aisle, a plate hung on its hook
    S.gantry(G, "gantry", -12.0, -5.5, 5.5, h=6.5, hook_at=0.45)
    quiet(K.box("hung_plate", (1.6, 0.05, 1.2), S.plate("bone"), loc=(-12.0, -0.55, 1.5), rot=(0, 4, 0), bev=0, parent=G))
    # stripped plates stacked for trade
    for k, (x, y) in enumerate(((-4.0, 4.0), (4.0, -4.0), (12.0, 4.6))):
        Pl = group(G, "plate_stack", (x, y, 0), rnd.uniform(-10, 10))
        for j in range(4):
            quiet(K.box("stack_bearer", (1.4, 0.1, 0.1), S.timber(3), loc=(0, -0.45 + j * 0.3, 0.05), bev=0, parent=Pl))
        for j in range(rnd.randint(5, 9)):
            quiet(K.box("stack_plate", (1.5 + rnd.uniform(-0.1, 0.1), 1.1, 0.04), S.plate(rnd.choice(("bone", "bone", "steel", "zinc"))),
                        loc=(rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), 0.12 + j * 0.045), rot=(0, 0, rnd.uniform(-4, 4)), bev=0, parent=Pl))
        solid(Pl, "stack", (1.5, 1.1, 0.5), (0, 0, 0.25))
    # the back: shelves' lean-to against the fence, a work table
    S.lean_to(G, "shelter", 16.0, 3.0, 2.8, 2.4, loc=(0.0, 18.85, 0), seed=95, walls=("left", "right"), wall_kind="planks")
    S.bulb(G, (0.0, 17.8, 2.5), arm=0.0, energy=40)
    S.prop(G, PC.workbench_settler, (-2.6, 19.4, 0), yaw=180)
    # fences round the yard, the gate between the signs
    S.fence(G, "fence_back", (25.4, 20.4), (-25.4, 20.4), kind="sheet", h=2.2, seed=96, posts="pipe")
    S.fence(G, "fence_e", (25.4, -21.8), (25.4, 20.4), kind="mixed", h=2.0, seed=97)
    S.fence(G, "fence_w", (-25.4, 20.4), (-25.4, -21.8), kind="mixed", h=2.0, seed=98)
    S.fence(G, "fence_front", (-25.4, -21.8), (25.4, -21.8), kind="sheet", h=2.0, seed=99, posts="pipe", gate=(19.9, 30.9))
    S.lamp_pole(G, (-6.0, -20.9, 0), h=3.8, yaw=180)
    S.lamp_pole(G, (6.0, -20.9, 0), h=3.8, yaw=180)
    S.drum(G, (21.6, 2.1, 0), col="mustard")
    S.drum(G, (-21.4, -2.3, 0), col="sea")
    S.tyres(G, (20.5, -16.5, 0), seed=100)
    S.junk_pile(G, (-21.0, 16.0, 0), r=1.8, h=1.0, seed=101)
    S.junk_pile(G, (21.0, -14.0, 0), r=1.5, h=0.9, seed=102)
    return _finish(P)


# ================================================================== the haymaze
MAZE_N, MAZE_CELL = 12, 5.0
MAZE_HEART = {(1, 6), (2, 6), (1, 7), (2, 7)}       # cells x -25..-15, y 0..10: round the chest at (-20, 7)
MAZE_ENTRY = (5, 0)                                  # the front wall's gap, x -5..0
MAZE_FORCED = [((9, 4), (10, 4))]                    # open past the crate at (20, -8)


def _maze(seed, loops=10):
    """A maze on MAZE_N x MAZE_N cells: a random depth-first spanning tree with the heart's four cells as one room,
    a few extra openings so it has loops. Returns the set of open cell pairs."""
    rnd = random.Random(seed)
    n = MAZE_N

    def node(c):
        return "H" if c in MAZE_HEART else c

    def cells(nd):
        return sorted(MAZE_HEART) if nd == "H" else [nd]

    def adj(c):
        x, y = c
        return [(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if 0 <= x + dx < n and 0 <= y + dy < n]

    opened = {frozenset(p) for p in MAZE_FORCED}
    for a in MAZE_HEART:
        for b in adj(a):
            if b in MAZE_HEART:
                opened.add(frozenset((a, b)))
    start = node(MAZE_ENTRY)
    seen = {start}
    stack = [start]
    while stack:
        nd = stack[-1]
        cand = [(a, b) for a in cells(nd) for b in adj(a) if node(b) not in seen]
        if not cand:
            stack.pop()
            continue
        a, b = rnd.choice(cand)
        opened.add(frozenset((a, b)))
        seen.add(node(b))
        stack.append(node(b))
    inner = [frozenset(((x, y), (x + 1, y))) for x in range(n - 1) for y in range(n)] + [frozenset(((x, y), (x, y + 1))) for x in range(n) for y in range(n - 1)]
    shut = [p for p in inner if p not in opened]
    for p in rnd.sample(shut, loops):
        opened.add(p)
    return opened


def _maze_depth(opened):
    """Cells walked from the entry to the heart (shortest way)."""
    from collections import deque  # noqa: PLC0415
    q = deque([(MAZE_ENTRY, 0)])
    seen = {MAZE_ENTRY}
    while q:
        c, d = q.popleft()
        if c in MAZE_HEART:
            return d
        x, y = c
        for b in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if b not in seen and frozenset((c, b)) in opened:
                seen.add(b)
                q.append((b, d + 1))
    return -1


MAZE_SEED = max(range(200), key=lambda s: _maze_depth(_maze(s)))


def haymaze():
    """The haymaze (60 x 60 m): a maze of 5 m cells walled two big bales high (2.4 m: over a player's eyes), a third
    of the walls scrap fence instead, bale stacks at every corner. One way in at the front; the way to the heart (a
    room of four cells round the chest, a wind pump turning over it as the landmark) is the longest of 200 seeded
    mazes, with a few loops to get lost in. Scarecrows stand in dead ends, a lamp here and there."""
    rnd = random.Random(42)
    P = _root("haymaze")
    S.pad(P, 68.0, 68.0, PAD, edge=1.0, mat=K.mat("hay_earth", "#8a7a5a", rough=1.0, grime=0.25, noise_scale=1.5, ao=0.3))
    G = group(P, "on_pad", (0, 0, PAD))
    opened = _maze(MAZE_SEED)
    n, c0, cs = MAZE_N, -MAZE_N * MAZE_CELL / 2, MAZE_CELL
    walls = []          # ((x0, y0), (x1, y1)) along grid lines, junction to junction
    for i in range(n):
        for k in range(n + 1):
            # horizontal wall on line y = c0 + k cs, over cell column i
            border = k in (0, n)
            shut = border and not (k == 0 and i == MAZE_ENTRY[0])
            if not border:
                shut = frozenset(((i, k - 1), (i, k))) not in opened
            if shut:
                walls.append(((c0 + i * cs, c0 + k * cs), (c0 + (i + 1) * cs, c0 + k * cs)))
            # vertical wall on line x = c0 + k cs, over cell row i
            shut = k in (0, n) or frozenset(((k - 1, i), (k, i))) not in opened
            if shut:
                walls.append(((c0 + k * cs, c0 + i * cs), (c0 + k * cs, c0 + (i + 1) * cs)))
    posts = set()
    for a, b in walls:
        posts.add(a)
        posts.add(b)
    for k, (x, y) in enumerate(sorted(posts)):
        B = group(G, "bale_post", (x, y, 0))
        for row in range(2):
            S.big_bale(B, (rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), row * 1.2), yaw=90 * ((k + row) % 2) + rnd.uniform(-3, 3),
                       size=(1.2, 1.2, 1.2), rnd=rnd)
        solid(B, "bale_post", (1.2, 1.2, 2.4), (0, 0, 1.2))
    h = 0.6
    for k, (a, b) in enumerate(walls):
        horiz = abs(a[1] - b[1]) < 1e-6
        d = (1, 0) if horiz else (0, 1)
        pa = (a[0] + d[0] * h, a[1] + d[1] * h)
        pb = (b[0] - d[0] * h, b[1] - d[1] * h)
        if rnd.random() < 0.68:
            S.bale_wall(G, pa, pb, rows=2, seed=200 + k)
            if rnd.random() < 0.12:                      # a bale tipped on top
                mx, my = (pa[0] + pb[0]) / 2, (pa[1] + pb[1]) / 2
                S.big_bale(G, (mx, my, 2.4), yaw=(0 if horiz else 90) + rnd.uniform(-20, 20), size=(1.8, 1.2, 0.9), rnd=rnd)
        else:
            kind = rnd.choice(("sheet", "sheet", "pallet", "planks", "plates", "doors"))
            S.fence(G, "scrap_wall", pa, pb, kind=kind, h=2.4, seed=300 + k)
    # the heart: the wind pump, a trough, a bench and lamps round the chest
    S.prop(G, PA.wind_pump, (-23.0, 2.0, 0), yaw=0, collide=((2.2, 2.2, 7.0), (0.25, 0.6, 3.5)))
    S.prop(G, PC.bench_plank, (-16.6, 8.4, 0), yaw=90)
    S.string_lights(G, (-24.3, 9.3, 2.6), (-15.7, 9.3, 2.6), n=7)
    S.lamp_pole(G, (-16.0, 1.2, 0), h=3.5, yaw=135)
    S.scarecrow(G, (-17.0, 4.6, 0), yaw=200, seed=43)
    # scarecrows in dead ends, lamps in a few cells
    deg = {}
    for p in opened:
        for c in p:
            deg[c] = deg.get(c, 0) + 1
    ends = [c for c in sorted(deg) if deg[c] == 1 and c not in MAZE_HEART and c != MAZE_ENTRY and c != (10, 4)]
    for k, c in enumerate(rnd.sample(ends, min(7, len(ends)))):
        S.scarecrow(G, (c0 + (c[0] + 0.5) * cs + rnd.uniform(-0.4, 0.4), c0 + (c[1] + 0.5) * cs + rnd.uniform(-0.4, 0.4), 0),
                    yaw=rnd.uniform(0, 360), seed=500 + k)
    # the way in: a sign, lamps, bales stacked by the gap
    ex = c0 + (MAZE_ENTRY[0] + 0.5) * cs
    for sx in (-1, 1):
        S.lamp_pole(G, (ex + sx * 3.2, c0 - 1.4, 0), h=3.5, yaw=180)
    S.prop(G, PS.signboard_painted, (ex + 6.0, c0 - 2.0, 0), yaw=0)
    S.big_bale(G, (ex - 6.0, c0 - 2.0, 0), yaw=8, rnd=rnd)
    S.big_bale(G, (ex - 6.2, c0 - 2.1, 1.2), yaw=-4, rnd=rnd)
    solid(G, "entry_bales", (2.6, 1.4, 2.4), (ex - 6.1, c0 - 2.0, 1.2))
    return _finish(P)


# ================================================================== the Scrap City garage
def scrap_city_garage():
    """The Scrap City garage (60 x 40 m): three barrel-vault bays of mismatched plate on rusty arched ribs, 20 m each,
    walled in odd corrugated sheet, an 8 x 6 m vehicle door in each bay's front with its leaf slid aside, a back
    door. Inside: a mezzanine with a stair in the left bay (a bunk and stores up there), a gantry crane over a car on
    stands in the middle bay, benches, tool walls, tyre stacks and the trade counter in the right bay. A tank sits on
    the right vault; fans, pipes and lamps are bolted on. The forecourt, and the east side where the wheels and the
    hauler stand, stay clear."""
    rnd = random.Random(50)
    P = _root("scrap_city_garage")
    S.pad(P, 68.0, 48.0, PAD, edge=1.0)
    G = group(P, "on_pad", (0, 0, PAD))
    W, D, spring, rise = 60.0, 40.0, 6.5, 5.0
    hd = D / 2
    quiet(K.box("garage_floor", (W, D, 0.05), K.mat("garage_slab", "#77736c", rough=0.9, grime=0.5, cracks=0.4, noise_scale=2.0),
                loc=(0, 0, 0.025), bev=0, parent=G))
    solid(G, "garage_floor", (W, D, 0.05), (0, 0, 0.025))
    bays = (-20.0, 0.0, 20.0)
    R = zc = 0.0
    for k, cx in enumerate(bays):
        _, R, zc = S.vault(G, f"vault{k}", 20.0, D, spring, rise, loc=(cx, 0, 0), seed=51 + k)

    def top(x):
        return zc + math.sqrt(max(R * R - x * x, 0.0))
    # walls: the front with its three vehicle doors, the back with a door, the sides with high windows
    doors = [(cx - 4.0, cx + 4.0, 0.0, 6.0) for cx in bays]
    S.clad_wall(G, "front", -W / 2, W / 2, 0.0, spring, loc=(0, -hd, 0), holes=doors, seed=52)
    back_door = (-0.625, 0.625, 0.0, 2.55)
    S.clad_wall(G, "back", -W / 2, W / 2, 0.0, spring, loc=(0, hd, 0), yaw=180, holes=[back_door], seed=53)
    S.door(group(G, "back_door", (0, hd, 0), 180), 0.0, 0.05, kind="hatch", seed=54)
    side_win = [(y - 1.0, y + 1.0, 4.0, 5.2) for y in (-12.5, -2.5, 7.5)]
    S.clad_wall(G, "side_w", -hd, hd, 0.0, spring, loc=(-W / 2, 0, 0), yaw=-90, holes=side_win, seed=55)
    S.clad_wall(G, "side_e", -hd, hd, 0.0, spring, loc=(W / 2, 0, 0), yaw=90, holes=side_win, seed=56)
    for side, yaw in ((-1, -90), (1, 90)):
        Wg = group(G, "side_windows", (side * W / 2, 0, 0), yaw)
        for y0, y1, z0, z1 in side_win:
            for zz in (z0 + 0.4,):
                quiet(K.box("win_glass", (y1 - y0, 0.03, z1 - z0), S.glass(), loc=((y0 + y1) / 2, 0.08, (z0 + z1) / 2), bev=0, parent=Wg))
                for xx in ((y0 + y1) / 2,):
                    S.board(Wg, "win_bar", (0.08, 0.06, z1 - z0), (xx, 0.04, (z0 + z1) / 2), S.rib())
    for s, yaw in ((-1, 0), (1, 180)):
        for k, cx in enumerate(bays):
            ports = [(-0.9, 0.9, 8.6, 10.2)] if s < 0 else []
            S.gable(G, f"gable{k}", -10.0, 10.0, spring, top, loc=(cx, s * hd, 0), yaw=yaw, seed=60 + k + (5 if s > 0 else 0), holes=ports)
            if s < 0:
                S.porthole(group(G, "gable_port", (cx, -hd, 0)), 0.0, 9.4, 0.8, y=-0.06)
    # the vehicle doors: leaves slid aside inside, their track, a bulb rack over each
    for k, cx in enumerate(bays):
        Lf = group(G, "door_leaf", (cx + 4.05, -hd + 0.35, 0.05))
        S.clad_wall(Lf, "door_leaf", 0.0, 4.1, 0.0, 5.95, seed=70 + k, tier=6.0, patches=0.3, t=0.1)
        S.board(G, "door_track", (16.4, 0.12, 0.16), (cx + 4.0, -hd + 0.3, 6.12), S.iron())
        for sx in (-1, 1):
            S.board(G, "door_jamb", (0.3, 0.35, 6.0), (cx + sx * 4.15, -hd + 0.05, 3.0), S.plate("mustard"))
        S.bulb_rack(G, (cx, -hd - 0.05, 6.35), w=3.0, n=3)
    # columns and valley beams between the bays
    for xv in (-10.0, 10.0):
        for k in range(9):
            y = -hd + 0.4 + (D - 0.8) * k / 8
            S.board(G, "column", (0.3, 0.3, spring), (xv, y, spring / 2), S.rib())
            solid(G, "column", (0.35, 0.35, spring), (xv, y, spring / 2))
        S.board(G, "valley_beam", (0.35, D, 0.5), (xv, 0, spring - 0.25), S.rib())
        S.pipe(G, "valley_gutter", (xv, -hd - 0.3, spring + 0.05), (xv, hd + 0.3, spring + 0.05), 0.15, S.zinc(), segs=8)
    # the left bay: a mezzanine along the back with a stair, a bunk and stores on it, benches and a tool wall under it
    lb = -20.0
    S.deck(G, "mezzanine", -29.7, -11.75, 13.0, 19.7, 3.0, rails=(("y0", -29.65, -13.2), "x1"), seed=80)
    S.stair(G, "mezz_stair", (-12.5, 5.6, 0.05), (-12.5, 13.0, 3.0), w=1.25, rails=(True, True), seed=81)
    S.bedroll(G, (-28.5, 18.3, 3.0), yaw=90)
    S.stove(G, (-26.6, 19.0, 3.0), yaw=180, pipe_h=5.6)
    S.sacks(G, (-22.0, 18.8, 3.0), n=5, seed=82)
    S.crate(G, (-18.0, 18.9, 3.0), yaw=6)
    S.crate(G, (-17.2, 18.9, 3.0), yaw=-4, shade=2)
    S.crate(G, (-17.6, 18.9, 3.5), size=(0.6, 0.5, 0.45), shade=3)
    S.bulb(G, (-20.0, 16.0, 6.2), arm=0.0, energy=60)
    S.prop(G, PC.workbench_settler, (lb - 6.0, 11.9, 0.05), yaw=180)
    S.prop(G, PC.tool_wall, (-29.5, 4.0, 0.05), yaw=-90)
    S.prop(G, PC.workbench_settler, (-29.2, 0.0, 0.05), yaw=-90)
    S.prop(G, PC.grindstone, (-26.5, -3.5, 0.05), yaw=30)
    S.prop(G, PC.anvil_stump, (-24.8, -1.6, 0.05), yaw=-20)
    S.tyres(G, (-27.6, -12.0, 0.05), seed=83)
    S.tyres(G, (-26.0, -14.6, 0.05), seed=84)
    # the middle bay: the gantry over a car on stands (a bare chassis waiting for its wheels)
    S.gantry(G, "gantry", 0.0, -10.0, 12.0, h=6.6, hook_at=0.55)
    Ch = group(G, "chassis", (0.0, 2.1, 0.05))
    for sx in (-1, 1):
        S.bar(Ch, "chassis_rail", (sx * 0.7, -2.2, 0.75), (sx * 0.7, 2.2, 0.75), 0.14, S.plate("brick"))
        for sy in (-1, 1):
            S.drum(Ch, (sx * 0.7, sy * 1.6, 0), col="rust", r=0.28, h=0.6)
    for sy in (-1.6, 0.0, 1.6):
        S.bar(Ch, "chassis_cross", (-0.75, sy, 0.75), (0.75, sy, 0.75), 0.12, S.plate("brick"))
    quiet(K.box("engine_block", (0.8, 0.9, 0.6), S.iron(), loc=(0.0, 1.3, 1.1), bev=0.03, parent=Ch))
    solid(Ch, "chassis", (1.8, 4.6, 1.0), (0, 0, 0.5))
    S.bulb(G, (0.0, 0.0, 9.5), arm=0.0, energy=80)
    S.prop(G, PC.sawhorse_pair, (5.5, -6.0, 0.05), yaw=10)
    # the right bay: the trade counter facing the door, tyre stacks, shelves, drums
    S.trade_counter(G, "counter", (20.0, 6.0, 0.05), yaw=0, w=3.0, seed=85)
    S.prop(G, PS.junk_shelf, (27.0, 18.9, 0.05), yaw=180, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.prop(G, PS.junk_shelf, (24.6, 18.9, 0.05), yaw=180, collide=((1.8, 0.6, 1.9), (0, 0, 0.95)))
    S.tyres(G, (28.2, 12.0, 0.05), seed=86)
    S.tyres(G, (27.4, 14.4, 0.05), seed=87)
    for k, (x, y) in enumerate(((13.0, 17.5), (13.7, 18.2), (14.4, 17.6))):
        S.drum(G, (x, y, 0.05), col=("sea", "mustard", "brick")[k])
    S.bulb(G, (20.0, 0.0, 9.5), arm=0.0, energy=80)
    # outside: the tank on the right vault, fans and stove pipes on the roofs, pipes down the back, lamps by the doors
    S.tank_on_stand(G, (20.0, 10.0, spring + rise - 0.05), r=1.5, h=2.4, stand=1.2, col="mustard")
    for cx in (-20.0, 0.0):
        S.fan_box(G, (cx + 3.0, 6.0, top(3.0) - 0.15), yaw=180)
    S.stove_pipe(G, (-26.6, 19.1, top(-6.6) - 0.1), 1.6)
    S.pipe_run(G, [(-8.0, hd + 0.25, spring + 3.5), (-8.0, hd + 0.25, 0.4), (-4.0, hd + 0.25, 0.4)], r=0.1, col="rust")
    for x in (-29.2, 29.2):
        S.lamp_pole(G, (x, -hd - 1.2, 0), h=4.0, yaw=0)
    return _finish(P)


SETTLERS = {
    "farmer_shack": farmer_shack,
    "campsite": campsite,
    "camp": camp,
    "large_camp": large_camp,
    "farmers_hideout": farmers_hideout,
    "farmbot_scrapyard": farmbot_scrapyard,
    "capsule_scrapyard": capsule_scrapyard,
    "haymaze": haymaze,
    "scrap_city_garage": scrap_city_garage,
}

# Spots a player must reach (Blender frame), per building: checked by walk_settlers.py on the exported collider.
WALK = {
    "farmer_shack": {"targets": {"inside the dome": (-0.5, 1.0, 0.35), "at the back of the dome": (-0.5, 3.4, 0.35),
                                 "in the store lean-to": (4.25, 0.6, 0.36), "at the counter": (-4.85, -5.4, 0.3),
                                 "in the garden": (1.5, 6.5, 0.3)}},
    "campsite": {"targets": {"in the hut": (0.0, -6.6, 0.06), "at the fire": (1.6, 0.0, 0.0)}},
    "camp": {"targets": {"in the lean-to": (-4.0, 3.6, 0.06), "at the fire": (1.2, -2.0, 0.0)}},
    "large_camp": {"targets": {"inside the hall": (0.0, 1.0, 0.35), "at the porch counter": (0.0, -6.0, 0.3),
                               "in the front hut": (18.0, -12.0, 0.35), "in the back hut": (-18.0, 12.0, 0.35),
                               "in the lean-to hut": (-18.0, -11.7, 0.36), "at the west stall": (-14.0, -4.3, 0.3),
                               "in the workshop": (-15.0, 5.0, 0.3), "by the watchtower": (-18.5, 17.0, 0.3)}},
    "farmers_hideout": {"targets": {"inside the gate": (0.0, -12.0, 0.3), "in the barn": (-9.0, 2.0, 0.35),
                                    "up in the barn loft": (-8.0, 10.0, 3.3), "at the counter": (-4.4, -11.5, 0.3),
                                    "in the bunkhouse": (10.0, -8.6, 0.36), "on the lookout deck": (15.0, 9.5, 10.3),
                                    "behind the barn": (-9.0, 13.6, 0.3)}},
    "farmbot_scrapyard": {"targets": {"in the office": (-23.0, -6.0, 0.35), "under the shelter": (-4.0, 23.0, 0.3),
                                      "by the derrick": (11.0, 18.0, 0.3)}},
    "capsule_scrapyard": {"targets": {"in the hut capsule": (-16.0, -10.0, 0.35), "under the shelter": (3.0, 19.0, 0.3),
                                      "down the aisle": (18.0, 0.0, 0.3)}},
    "haymaze": {"targets": {"in the heart": (-20.0, 5.0, 0.3), "by the crate": (21.5, -7.5, 0.3)}},
    "scrap_city_garage": {"targets": {"in the left bay": (-20.0, 0.0, 0.35), "up on the mezzanine": (-20.0, 16.0, 3.35),
                                      "at the counter": (20.0, 5.0, 0.35), "by the chassis": (2.0, 2.0, 0.35),
                                      "out of the back door": (0.0, 21.5, 0.3)}},
}
