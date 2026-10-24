"""The Settler Patchwork kit sheet for render_settlers.py: every piece of settler_kit.py laid out in four rows (walls,
roofs and shells; doors, counters, decks and cranes; fences and bolted-on gear; junk, bales and clutter), each
named on the ground in front of it. sheet(scene, shoot) renders the whole sheet and then each row close up."""
import random

import bpy

import kit as K
import frame as FR
import settler_kit as S
from colony_kit import group
from families import M, GRAPHITE

Y = [0.0, 18.0, 36.0, 50.0]


def tag(P, text, loc, size=0.55):
    FR.label(P, text, loc, size, M(GRAPHITE, wear=0, grime=0, scratches=0), rot=(0, 0, 0), extrude=0.005)


def build(P):
    rows = [group(P, f"row{k}", (0, Y[k], 0.06)) for k in range(4)]
    rnd = random.Random(1)
    # row 0: walls, roofs and shells
    R = rows[0]
    S.plate_wall(R, "plate_wall", -2.5, 2.5, 0.0, 3.0, loc=(-40.0, 0, 0), seed=1)
    tag(R, "PLATE WALL", (-40.0, -3.0, 0.01))
    S.plank_wall(R, "plank_wall", -2.5, 2.5, 0.0, 3.0, loc=(-33.0, 0, 0), seed=2)
    tag(R, "PLANK WALL", (-33.0, -3.0, 0.01))
    S.clad_wall(R, "clad_wall", -2.5, 2.5, 0.0, 4.0, loc=(-26.0, 0, 0), seed=3, tier=2.0)
    tag(R, "SHEET CLADDING", (-26.0, -3.0, 0.01))
    S.half_dome(R, "half_dome", 2.5, 5.0, loc=(-17.0, 0, 0), seed=4, front={"door": (0.6, 1.25, 2.5, "hatch"), "port": [(-1.2, 2.0)]})
    tag(R, "HALF DOME ON RIBS", (-17.0, -4.5, 0.01))
    S.lean_to(R, "lean_to", 4.0, 3.0, 2.8, 2.3, loc=(-8.5, 0, 0), seed=5, floor=True)
    tag(R, "LEAN-TO", (-8.5, -3.5, 0.01))
    S.dome_hut(R, "dome_hut", loc=(-1.5, 0, 0), seed=6)
    tag(R, "DOME HUT", (-1.5, -4.0, 0.01))
    for k, st in enumerate(("whole", "stripped", "hut")):
        S.capsule(R, f"capsule_{st}", (6.5 + k * 7.5, 0, 0), state=st, seed=7 + k)
        tag(R, f"CAPSULE: {st.upper()}", (6.5 + k * 7.5, -4.2, 0.01))
    S.vault(R, "vault", 10.0, 5.0, 3.0, 2.5, loc=(34.0, 0, 0), seed=10)
    S.gable(R, "gable", -5.0, 5.0, 3.0, lambda x: -1.75 + (6.25 ** 2 - x * x) ** 0.5 if abs(x) < 6.25 else 0.0,
            loc=(34.0, -2.5, 0), seed=11)
    tag(R, "VAULT AND GABLE", (34.0, -4.5, 0.01))
    # row 1: doors, counters, decks, stair, scaffold, cranes
    R = rows[1]
    holes = [(-3.4, -2.15, 0.0, 2.55), (-0.625, 0.625, 0.0, 2.55), (2.15, 3.4, 0.0, 2.55)]
    Wd = S.plank_wall(R, "door_wall", -4.0, 4.0, 0.0, 3.0, loc=(-38.0, 0, 0), seed=12, holes=holes)
    for (x0, x1, _, _), kind in zip(holes, ("hatch", "planks", "sheet")):
        S.door(Wd, (x0 + x1) / 2, 0.05, y=-0.04, kind=kind, ajar=60, seed=13)
    tag(R, "DOORS: HATCH, PLANKS, SHEET", (-38.0, -3.0, 0.01))
    S.trade_counter(R, "counter", (-29.0, 0, 0), seed=14)
    tag(R, "TRADE COUNTER", (-29.0, -3.4, 0.01))
    S.trade_counter(R, "bare_counter", (-23.0, 0, 0), seed=15, booth=False)
    tag(R, "BARE COUNTER", (-23.0, -3.4, 0.01))
    Wa = S.plate_wall(R, "awning_wall", -2.0, 2.0, 0.0, 3.0, loc=(-16.5, 0, 0), seed=16, holes=[(-1.4, -0.2, 1.0, 1.8)])
    S.window(Wa, -0.8, 1.0, w=1.2, h=0.8, y=-0.04, seed=17)
    S.awning(Wa, 0.0, 2.6, 3.6, 1.4, kind="tin")
    S.porthole(Wa, 1.2, 1.6)
    tag(R, "WINDOW, PORTHOLE, TIN AWNING", (-16.5, -3.0, 0.01))
    S.deck(R, "deck", -11.0, -6.0, -1.0, 2.0, 1.5, rails=("y1", "x0"), seed=18)
    S.stair(R, "stair", (-5.0, -2.2, 0.0), (-5.0, 1.2, 1.5), seed=19)
    S.ladder(R, (-9.0, -1.4, 0.0), 1.6)
    tag(R, "DECK, STAIR, LADDER", (-8.0, -3.4, 0.01))
    S.scaffold_tower(R, "scaffold", (2.5, 0, 0), size=4.5, h=6.25, seed=20)
    tag(R, "SCAFFOLD LOOKOUT", (2.5, -3.6, 0.01))
    S.derrick(R, "derrick", (13.0, 1.0, 0), h=10.0, jib=7.0, seed=21)
    tag(R, "DERRICK", (13.0, -3.0, 0.01))
    S.gantry(R, "gantry", 24.0, -2.5, 2.5, h=5.0)
    tag(R, "GANTRY", (24.0, -4.0, 0.01))
    S.tank_on_stand(R, (31.5, 0, 0))
    tag(R, "TANK ON STAND", (31.5, -2.4, 0.01))
    S.fan_box(R, (38.0, 0.6, 0), n=2)
    tag(R, "FAN BOX", (38.0, -1.6, 0.01))
    # row 2: fences and bolted-on gear
    R = rows[2]
    x = -44.0
    for kind in ("mixed", "sheet", "plates", "planks", "doors", "pallet"):
        S.fence(R, f"fence_{kind}", (x, 0.0), (x + 5.0, 0.0), kind=kind, seed=22 + len(kind), posts="pipe" if kind == "sheet" else "timber")
        tag(R, f"FENCE: {kind.upper()}", (x + 2.5, -1.6, 0.01), size=0.45)
        x += 6.0
    Wg = S.plate_wall(R, "gear_wall", -2.5, 2.5, 0.0, 3.0, loc=(-5.0, 0, 0), seed=30)
    S.wall_fan(Wg, -1.3, 1.6, y=-0.02)
    S.bulb_rack(Wg, (0.9, -0.02, 2.4), w=1.8, n=3)
    S.pipe_run(Wg, [(-2.3, -0.2, 2.7), (2.3, -0.2, 2.7), (2.3, -0.2, 0.3)])
    S.bulb(Wg, (0.6, -0.02, 1.4))
    tag(R, "WALL FAN, BULB RACK, PIPE RUN, BULB", (-5.0, -1.8, 0.01), size=0.45)
    S.lamp_pole(R, (1.5, 0, 0))
    tag(R, "LAMP POLE", (1.5, -1.8, 0.01), size=0.45)
    S.string_lights(R, (4.0, 0.0, 2.8), (9.0, 0.0, 2.8))
    for xx in (4.0, 9.0):
        S.board(R, "string_post", (0.12, 0.12, 3.0), (xx, 0.0, 1.5), S.timber(3))
    tag(R, "STRING LIGHTS", (6.5, -1.8, 0.01), size=0.45)
    S.stove_pipe(R, (11.0, 0.0, 0.0), 2.4)
    tag(R, "STOVE PIPE", (11.0, -1.8, 0.01), size=0.45)
    S.junk_pile(R, (16.0, 0.5, 0), r=2.2, h=1.2, seed=31)
    tag(R, "JUNK PILE", (16.0, -2.4, 0.01), size=0.45)
    S.bale_wall(R, (20.5, 0.0), (26.5, 0.0), rows=2, seed=32)
    S.big_bale(R, (23.5, -2.6, 0), yaw=10, rnd=rnd)
    tag(R, "BALE WALL, BIG BALE", (23.5, -4.2, 0.01), size=0.45)
    S.scarecrow(R, (29.0, 0, 0), seed=33)
    tag(R, "SCARECROW", (29.0, -1.6, 0.01), size=0.45)
    S.crops(R, 31.5, 37.0, -1.5, 2.5, rnd, rows=3, name="crops")
    S.crops(R, 38.0, 43.0, -1.5, 2.5, rnd, rows=3, kind="beans", name="beans")
    tag(R, "CROP PATCHES", (37.0, -2.4, 0.01), size=0.45)
    # row 3: clutter
    R = rows[3]
    items = [
        ("DRUMS", lambda G: (S.drum(G, (0, 0, 0)), S.drum(G, (0.8, 0.2, 0), col="brick", lying=True, yaw=30))),
        ("CRATES", lambda G: (S.crate(G, (0, 0, 0)), S.crate(G, (0, 0, 0.5), size=(0.6, 0.5, 0.45), shade=3))),
        ("SACKS", lambda G: S.sacks(G, (0, 0, 0), n=5)),
        ("BEDROLL", lambda G: S.bedroll(G, (0, 0, 0), yaw=90)),
        ("LOG PILE", lambda G: S.log_pile(G, (0, 0, 0))),
        ("TYRES", lambda G: S.tyres(G, (0, 0, 0))),
        ("FIRE RING, TRIPOD", lambda G: (S.fire_ring(G, (0, 0, 0)), S.tripod(G, (0, 0, 0)))),
        ("STOVE", lambda G: S.stove(G, (0, 0, 0), pipe_h=1.2)),
        ("TABLE, BENCHES", lambda G: S.table(G, (0, 0, 0), yaw=90)),
        ("PALLET", lambda G: S.pallet(G, (0, 0, 0), 1.2, rnd)),
    ]
    for k, (name, fn) in enumerate(items):
        G = group(R, name.lower(), (-36.0 + k * 5.0, 0, 0))
        fn(G)
        tag(R, name, (-36.0 + k * 5.0, -1.8, 0.01), size=0.4)
    return rows


def sheet(scene, shoot):
    scene()
    K.ground(600, color="#7f8b6c")
    P = K.link(bpy.data.objects.new("sheet", None))
    K.box("sheet_pad", (100.0, 64.0, 0.06), M("#b9b6ac", wear=0, grime=0, scratches=0), loc=(0.0, 24.0, 0.03), bev=0, parent=P)
    rows = build(P)
    shoot("kit_sheet", (0.0, -62.0, 58.0), (0.0, 25.0, 0.0), 30, 1600, 1100)
    hidden = {ob.name for ob in P.children_recursive if ob.hide_render}

    def only(k, x0, x1):
        for i, r in enumerate(rows):
            for g in r.children:
                x = g.matrix_world.translation.x
                for ob in [g] + list(g.children_recursive):
                    ob.hide_render = ob.name in hidden or i != k or not (x0 <= x <= x1)
    bpy.context.view_layer.update()
    for k, x0, x1, nm, cam, tgt, lens in (
            (0, -50, -5, "kit_1a_walls_roofs", (-21.0, -22.0, 7.0), (-21.0, 0.0, 2.0), 32),
            (0, -5, 50, "kit_1b_shells_capsules", (21.0, -24.0, 8.0), (21.0, 0.0, 2.5), 32),
            (1, -50, -4, "kit_2a_doors_counters_decks", (-24.0, Y[1] - 22.0, 6.0), (-24.0, Y[1], 1.6), 32),
            (1, -4, 50, "kit_2b_scaffold_cranes", (19.0, Y[1] - 26.0, 9.0), (19.0, Y[1], 3.5), 32),
            (2, -50, 0, "kit_3a_fences", (-25.0, Y[2] - 20.0, 6.0), (-25.0, Y[2], 1.2), 32),
            (2, 0, 50, "kit_3b_gear_junk_bales", (20.0, Y[2] - 22.0, 7.0), (20.0, Y[2], 1.2), 32),
            (3, -50, 50, "kit_4_clutter", (-13.5, Y[3] - 14.0, 5.0), (-13.5, Y[3], 0.4), 34)):
        only(k, x0, x1)
        shoot(nm, cam, tgt, lens, 1600, 700)
    for ob in P.children_recursive:
        ob.hide_render = ob.name in hidden
