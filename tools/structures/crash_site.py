"""The SV-04 after the crash: the cargo ship (cargo_ship.py) broken into four pieces in its furrow, the containers it
lost in the sky burst open across the landing zone, and the fires the player puts out to get in.

The story, as the opening shows it: the ship blows a hole in its spine in the air, the containers tear off the racks
and fall, each bursting where it lands and spilling its cargo (cargo_ship.CARGO says which colour carries what). The
ship comes down tail first, ploughs a furrow and breaks: the aft block, the spine in two, and the bow, which ends up
furthest along. Four containers stay crushed in their racks.

Origin: where the ship's centre would be, on the ground; the crash runs towards +Y (Blender), the way the ship flew.
"""
import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

import kit as K
import frame as FR
import cargo_ship as S
from families import M, glow, GUNMETAL, CYAN, COPPER, MUSTARD, BONE, STEEL, GRAPHITE, WOOD

SPINE_BREAK = 1.0
KEEP = {"SV4-112", "SV4-131", "SV4-215", "SV4-309"}      # the containers left crushed in their racks

# Each piece turns about its pivot (its middle on the ship's deck line) and comes to rest with the pivot at the given
# world position, rotated (deg, XYZ). The aft block hit first and stopped soonest; the bow slid on furthest.
PIVOTS = {"aft": (0.0, -21.0, 0.0), "spine_a": (0.0, -5.5, 0.0), "spine_b": (0.0, 7.5, 0.0), "bow": (0.0, 23.0, 0.0)}
PIECES = {
    "aft": ((-7.0, -27.0, 1.75), (1.5, -9.0, 16.0)),
    "spine_a": ((-2.0, -9.5, 1.3), (-3.0, -38.0, 38.0)),
    "spine_b": ((3.5, 4.0, 1.05), (2.0, 14.0, -22.0)),
    "bow": ((6.0, 21.5, 1.45), (-4.0, 8.0, -9.0)),
}
# The containers lost in the sky: (code, x, y, yaw, roll) where each burst open, around the furrow and back along
# the flight path (-Y). In the game these are their own models with loot around them.
SCATTER = [
    ("SV4-118", -38.0, -58.0, 30.0, 8.0), ("SV4-120", 22.0, -64.0, -50.0, -6.0), ("SV4-207", -60.0, -110.0, 75.0, 12.0),
    ("SV4-233", 48.0, -118.0, 10.0, -90.0), ("SV4-240", -14.0, -150.0, -20.0, 5.0), ("SV4-301", 70.0, -36.0, 120.0, 4.0),
    ("SV4-316", -72.0, -18.0, 200.0, -10.0), ("SV4-322", 30.0, -190.0, 60.0, 90.0), ("SV4-405", -36.0, 40.0, -70.0, 7.0),
    ("SV4-411", 54.0, 30.0, 160.0, -5.0), ("SV4-420", -96.0, -170.0, 15.0, 9.0), ("SV4-438", 92.0, -140.0, -110.0, 6.0),
]
# Fires (on the ground or on the wreck, world positions): the ones at the three ways in must be put out to get in.
FIRES = [
    ("airlock", None, (7.6, 16.6, 0.0), 1.2),       # outside the bow's starboard airlock (bow-local)
    ("mess_door", None, (8.4, -15.0, 0.0), 1.1),    # outside the aft block's starboard side door (aft-local)
    ("spine_end", None, (0.0, -11.2, 0.0), 1.3),    # in the aft block's torn front, where the spine was (aft-local)
    ("engine_room", None, (2.5, -27.5, 0.0), 0.9),  # inside, by an engine feed (aft-local)
    ("bridge", None, (3.0, 23.6, 0.0), 0.7),        # inside, by the starboard wall stations (bow-local)
    ("spine_a", None, (0.0, -6.0, 0.0), 0.9),       # in the corridor of the first spine piece (spine_a-local)
]
FIRE_PIECE = {"airlock": "bow", "mess_door": "aft", "spine_end": "aft", "engine_room": "aft", "bridge": "bow", "spine_a": "spine_a"}


def _bake(objs):
    """Applies every modifier (the booleans need it before pieces move apart) and drops the cutters."""
    dg = bpy.context.evaluated_depsgraph_get()
    cutters = set()
    for ob in objs:
        for mod in ob.modifiers:
            if mod.type == "BOOLEAN" and mod.object:
                cutters.add(mod.object)
    for ob in objs:
        if ob.type == "MESH" and any(m.type == "BOOLEAN" for m in ob.modifiers):
            me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
            ob.modifiers.clear()
            ob.data = me
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)


def _centroid(ob):
    if ob.type == "MESH":
        pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        return sum(pts, Vector()) / 8
    return ob.matrix_world.translation.copy()


def _piece_of(y):
    if y < S.A1:
        return "aft"
    if y < SPINE_BREAK:
        return "spine_a"
    if y < S.B0:
        return "spine_b"
    return "bow"


def torn_rim(P, y, hw, zb, zt, cb, ct, dirn, mats, rnd, n=22):
    """Jagged plates bent outward round an octagon section at y (dirn: which way the torn edge faces), with copper
    cables and pipe ends hanging out."""
    poly = S.octagon(hw, zb, zt, cb, ct)
    per = [Vector((x, z)) for x, z in poly]
    L = sum((per[(i + 1) % 8] - per[i]).length for i in range(8))
    for k in range(n):
        t = (k + rnd.random() * 0.5) / n * L
        i = 0
        while t > (per[(i + 1) % 8] - per[i]).length:
            t -= (per[(i + 1) % 8] - per[i]).length
            i = (i + 1) % 8
        a, b = per[i], per[(i + 1) % 8]
        p = a + (b - a).normalized() * t
        K.box("torn", (rnd.uniform(0.5, 1.1), rnd.uniform(0.6, 1.6), 0.12), mats["hull"] if k % 3 else mats["dark"],
              loc=(p.x, y + dirn * rnd.uniform(0.2, 0.7), p.y), rot=(rnd.uniform(-40, 40), rnd.uniform(-35, 35), rnd.uniform(-30, 30)), bev=0.02, parent=P)
    cu = K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)
    for k in range(6):
        K.torus("gut", rnd.uniform(0.3, 0.7), 0.05, cu, loc=(rnd.uniform(-hw * 0.6, hw * 0.6), y + dirn * 0.2, rnd.uniform(zt - 1.6, zt - 0.4)),
                rot=(rnd.uniform(0, 90), rnd.uniform(0, 90), 0), arc=200, parent=P)
    for k in range(3):
        K.cyl("pipe_end", 0.1, rnd.uniform(0.6, 1.4), mats["trim"], loc=(rnd.uniform(-1.8, -1.4), y + dirn * 0.4, rnd.uniform(2.8, 3.2)), rot=(90 + rnd.uniform(-20, 20), 0, rnd.uniform(-20, 20)), segs=10, bev=0, parent=P)


def soot(P, loc, r, rnd, n=4):
    m = K.mat("soot", "#141312", rough=1.0)
    x, y, z = loc
    for k in range(n):
        K.sphere("soot", r * rnd.uniform(0.6, 1.0), m, loc=(x + rnd.uniform(-r, r) * 0.6, y + rnd.uniform(-r, r) * 0.6, z + rnd.uniform(-r, r) * 0.4), scale=(1, 1, 0.25), parent=P)


def _vol(name, colour, density, emit=None, strength=0.0):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        vol = nt.nodes.new("ShaderNodeVolumePrincipled")
        vol.inputs["Color"].default_value = (*K.lin(colour)[:3], 1)
        vol.inputs["Density"].default_value = density
        if emit:
            vol.inputs["Emission Color"].default_value = (*K.lin(emit)[:3], 1)
            vol.inputs["Emission Strength"].default_value = strength
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(vol.outputs[0], out.inputs["Volume"])
    return m


def blaze(P, loc, scale=1.0, rnd=None, light=True):
    """A review-render fire: glowing volume tongues (deep orange outside, yellow core), embers, a glow on the ground.
    The game draws its own fire at the site's fire markers."""
    rnd = rnd or random.Random(3)
    x, y, z = loc
    outer = _vol("flame_o", "#000000", 0.0, "#ff5200", 3.0)
    core = _vol("flame_c", "#000000", 0.0, "#ffa018", 7.0)
    ember = K.mat("ember", "#ffc860", rough=0.5, emit="#ffb83a", emit_strength=9)
    for k in range(7):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(0.0, 0.5) * scale
        h = rnd.uniform(0.9, 1.9) * scale
        r = rnd.uniform(0.3, 0.45) * scale
        K.sphere("flame", 1.0, outer, loc=(x + d * math.cos(a), y + d * math.sin(a), z + h * 0.45), scale=(r, r, h / 2), parent=P)
    for k in range(3):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(0.0, 0.25) * scale
        h = rnd.uniform(0.6, 1.0) * scale
        K.sphere("flame", 1.0, core, loc=(x + d * math.cos(a), y + d * math.sin(a), z + h * 0.4), scale=(0.22 * scale, 0.22 * scale, h / 2), parent=P)
    for k in range(10):
        K.sphere("ember", 0.03, ember, loc=(x + rnd.uniform(-0.8, 0.8) * scale, y + rnd.uniform(-0.8, 0.8) * scale, z + rnd.uniform(1.5, 3.5) * scale), parent=P)
    K.disc("fire_glow", 1.3 * scale, K.mat("embers_bed", "#16110e", rough=0.95, emit="#ff3a00", emit_strength=0.12), (x, y), z=z + 0.03, wobble=0.3, seed=rnd.randint(0, 99)).parent = P
    if light:
        K.point("firelight", (x, y, z + 1.4 * scale), "#ff8a3a", 700 * scale, 0.6).parent = P


def smoke(P, loc, h=14.0, rnd=None):
    """A leaning column of soft grey smoke: overlapping volume puffs (review renders; the game draws its own smoke)."""
    rnd = rnd or random.Random(1)
    m = _vol("smoke", "#6e6a66", 0.22)
    x, y, z = loc
    for k in range(22):
        t = k / 21
        r = 0.7 + 2.2 * t
        K.sphere("smoke", r, m, loc=(x + 5.0 * t * t + rnd.uniform(-0.6, 0.6), y + 2.5 * t * t + rnd.uniform(-0.6, 0.6), z + 1.8 + h * t), parent=P)


def burst_container(code, colour, o, yaw, roll, rnd, name=None):
    """A container that fell and burst: half dug in, doors blown open (one leaf torn off), its cargo spilled out of the
    doors in a fan. Origin on the ground under its middle."""
    root = K.link(bpy.data.objects.new(name or f"burst_{code}", None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    body = K.link(bpy.data.objects.new("body", None))
    body.parent = root
    body.location = (0, 0, 1.05)
    body.rotation_euler = (math.radians(rnd.uniform(-6, 6)), math.radians(roll), 0)
    c = S.container(body, (0, 0, 0), colour, code, rnd)
    # hollow it and open the door end
    core = next(ch for ch in c.children if ch.name.startswith("cont_body"))
    hole = K.box("cut", (2.2, 5.8, 2.2), K.mat("cut", "#ff00ff"), loc=(0, 0.3, 0), bev=0, parent=c)
    hole.hide_render = hole.hide_viewport = True
    S.cut(core, hole)
    for ch in list(c.children):
        if ch.name.startswith(("cont_lockbar", "cont_seam")):
            bpy.data.objects.remove(ch, do_unlink=True)
    K.box("cont_inside", (2.2, 0.05, 2.2), K.mat("cont_dark", "#1b1c1e", rough=0.9), loc=(0, -2.7, 0), bev=0, parent=c)
    col = S.CARGO[colour][0]
    leaf = M(col, metal=0.25, wear=0.5)
    K.box("door_leaf", (1.2, 0.08, 2.3), leaf, loc=(-1.75, 3.35, 0), rot=(0, 0, 70), bev=0.02, parent=c)
    K.box("door_leaf_off", (1.2, 0.08, 2.3), leaf, loc=(rnd.uniform(-1, 1), 6.5, -0.95), rot=(88, rnd.uniform(-10, 10), rnd.uniform(-40, 40)), bev=0.02, parent=root)
    # dents: a crumpled corner of dark plates
    for k in range(5):
        K.box("dent", (0.6, 0.6, 0.06), M(col, metal=0.25, wear=0.7), loc=(1.2, -2.6 + k * 0.15, 0.9 - k * 0.1), rot=(rnd.uniform(-30, 30), rnd.uniform(-30, 30), 0), bev=0.01, parent=c)
    # the dirt it threw up, the spill fan in front of the doors
    K.disc("crater", 4.2, K.mat("dirt", "#5b4a36", rough=1.0, noise_scale=2.0), (0, 0), z=0.02, wobble=0.25, seed=rnd.randint(0, 99)).parent = root
    spill(root, colour, rnd)
    return root


def spill(P, colour, rnd, n=14):
    """Cargo of one kind fanned out from the doors (+Y)."""
    for k in range(n):
        a = rnd.uniform(-0.8, 0.8)
        d = rnd.uniform(3.3, 8.0)
        x, y = d * math.sin(a), d * math.cos(a)
        yaw = rnd.uniform(0, 360)
        if colour == "red":      # scrap metal sheets, metal blocks, bearings
            r = rnd.random()
            if r < 0.5:
                K.box("scrap_sheet", (rnd.uniform(0.5, 0.9), rnd.uniform(0.4, 0.7), 0.05), M(rnd.choice(["#6d6f6e", "#7a4a30", "#5a6068"]), wear=0.8, metal=0.5), loc=(x, y, 0.05), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), yaw), bev=0.01, parent=P)
            elif r < 0.8:
                K.box("metal_block", (0.25, 0.25, 0.25), M("#8a9096", metal=0.6, wear=0.4), loc=(x, y, 0.125), rot=(0, 0, yaw), bev=0.02, parent=P)
            else:
                K.cyl("bearing", 0.16, 0.12, M(GUNMETAL, metal=0.6), loc=(x, y, 0.06), bev=0.02, parent=P)
        elif colour == "blue":   # component kits, circuit boards, battery packs
            r = rnd.random()
            if r < 0.4:
                FR.framed_box(P, "component_kit", (x, y, 0.15), (0.45, 0.32, 0.3), M("#3d79b3", metal=0.2), M(GRAPHITE), fw=0.04)
            elif r < 0.75:
                K.box("circuit_board", (0.3, 0.22, 0.03), M("#2f7a4a", wear=0.2), loc=(x, y, 0.03), rot=(0, 0, yaw), bev=0.005, parent=P)
                K.box("chip", (0.08, 0.08, 0.02), M(GRAPHITE), loc=(x, y, 0.055), rot=(0, 0, yaw), bev=0, parent=P)
            else:
                K.box("battery_pack", (0.3, 0.2, 0.25), M(GRAPHITE), loc=(x, y, 0.125), rot=(0, 0, yaw), bev=0.03, parent=P)
                K.box("battery_band", (0.31, 0.21, 0.05), glow(CYAN, 2), loc=(x, y, 0.2), rot=(0, 0, yaw), bev=0, parent=P)
        elif colour == "yellow":  # gas canisters and chemical drums
            if rnd.random() < 0.6:
                K.box("gas_can", (0.35, 0.18, 0.45), M("#c8423a", wear=0.4), loc=(x, y, 0.225), rot=(0, rnd.choice([0, 90]), yaw), bev=0.04, parent=P)
            else:
                K.cyl("chem_drum", 0.3, 0.9, M("#d9ae2f", wear=0.4), loc=(x, y, 0.3 if rnd.random() < 0.5 else 0.45), rot=(90 if rnd.random() < 0.5 else 0, 0, yaw), bev=0.03, parent=P)
                K.disc("chem_puddle", 0.8, glow("#9fe35a", 0.6), (x + 0.4, y + 0.3), z=0.015, wobble=0.3, seed=k).parent = P
        elif colour == "green":  # seed sacks, fertiliser bags, food crates
            r = rnd.random()
            if r < 0.5:
                K.sphere("seed_sack", 0.3, K.mat("sack", "#b39a6b", rough=0.95), loc=(x, y, 0.18), scale=(1.2, 0.9, 0.6), parent=P)
            elif r < 0.8:
                K.box("fert_bag", (0.5, 0.35, 0.15), M("#5c8f47", wear=0.2), loc=(x, y, 0.08), rot=(0, 0, yaw), bev=0.06, parent=P)
            else:
                K.box("food_crate", (0.5, 0.35, 0.3), K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0), loc=(x, y, 0.15), rot=(0, 0, yaw), bev=0.02, parent=P)
                K.sphere("tomato", 0.07, M("#c8423a"), loc=(x + 0.3, y, 0.07), parent=P)
        else:                     # tan: planks, concrete blocks, glass panes
            r = rnd.random()
            if r < 0.5:
                K.box("plank", (1.4, 0.2, 0.08), K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0), loc=(x, y, 0.04 + (k % 3) * 0.08), rot=(0, 0, yaw), bev=0.01, parent=P)
            elif r < 0.8:
                K.box("concrete", (0.25, 0.25, 0.25), M("#a9a79e", wear=0.4), loc=(x, y, 0.125), rot=(0, 0, yaw), bev=0.02, parent=P)
            else:
                K.box("glass_pane", (0.6, 0.4, 0.02), K.mat("glass_shard", "#9fd2e2", rough=0.05, emit="#9fd2e2", emit_strength=0.1), loc=(x, y, 0.02), rot=(0, 0, yaw), bev=0, parent=P)


def wreck(o=(0, 0, 0), yaw=0, name="sv04_wreck", seed=21, scene=True):
    """The broken ship: empties 'aft', 'spine_a', 'spine_b', 'bow' (each a model in the game) under one root.
    scene adds what the game places itself: the furrow and scorch, fires, smoke, debris (review renders)."""
    rnd = random.Random(seed)
    ship = S.cargo_ship(name="sv04_parts", lights=False, spine_break=SPINE_BREAK, keep=KEEP, seed=7)
    ship.location = (0, 0, 0)
    bpy.context.view_layer.update()
    pal = S.PALETTES["dark"]
    mats = {k: M(v, metal=0.3, rough=0.5, wear=0.45, grime=0.5) for k, v in pal.items()}
    mats["trim"] = M(pal["trim"], metal=0.45, rough=0.45, wear=0.5)
    _bake(list(ship.children_recursive))
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    pieces = {}
    for key in PIECES:
        e = K.link(bpy.data.objects.new(key, None))
        e.parent = root
        pieces[key] = e
    bpy.context.view_layer.update()
    for ob in list(ship.children):
        y = _centroid(ob).y
        mw = ob.matrix_world.copy()
        ob.parent = pieces[_piece_of(y)]
        ob.matrix_world = mw
    bpy.data.objects.remove(ship, do_unlink=True)

    # ---- damage before the pieces move: lost plates and the port housing, torn ends, soot, dead lights, debris
    for key, P in pieces.items():
        for ob in list(P.children):
            if ob.name.startswith("plate") and not ob.name.startswith("plate_gap"):
                r = rnd.random()
                if r < 0.22:
                    bpy.data.objects.remove(ob, do_unlink=True)
                elif r < 0.32:
                    ob.rotation_euler.x += math.radians(rnd.uniform(-25, 25))
                    ob.rotation_euler.y += math.radians(rnd.uniform(-15, 15))
    aft, sa, sb, bow = (pieces[k] for k in ("aft", "spine_a", "spine_b", "bow"))
    # the port engine housing tore off: everything up there on the port side goes to its own piece on the ground
    lost = K.link(bpy.data.objects.new("lost_housing", None))
    lost.parent = root
    for ob in list(aft.children):
        c = _centroid(ob)
        if c.x < -2.3 and c.z > 4.85 and c.y < -16.5:
            mw = ob.matrix_world.copy()
            ob.parent = lost
            ob.matrix_world = mw
    lost.location = (0, 0, 0)
    torn_rim(aft, S.A1 + 0.1, S.SHW + 0.3, -1.4, 4.2, 0.6, 0.5, 1, mats, rnd)
    torn_rim(sa, S.S0 - 0.1, S.SHW, -1.4, 4.2, 0.6, 0.5, -1, mats, rnd)
    torn_rim(sa, SPINE_BREAK, S.SHW, -1.4, 4.2, 0.6, 0.5, 1, mats, rnd)
    torn_rim(sb, SPINE_BREAK, S.SHW, -1.4, 4.2, 0.6, 0.5, -1, mats, rnd)
    torn_rim(sb, S.S1 + 0.1, S.SHW, -1.4, 4.2, 0.6, 0.5, 1, mats, rnd)
    torn_rim(bow, S.B0 - 0.1, S.SHW + 0.3, -1.4, 4.2, 0.6, 0.5, -1, mats, rnd)
    # the scar where the housing was: a dark, jagged patch and torn pipe stubs
    K.box("housing_scar", (3.4, 13.0, 0.1), K.mat("burnt_metal", "#1e1c1a", rough=0.9), loc=(-4.3, -24.0, 4.92), bev=0, parent=aft)
    for k in range(6):
        K.box("scar_plate", (rnd.uniform(0.6, 1.2), rnd.uniform(0.6, 1.4), 0.1), mats["hull"], loc=(-4.3 + rnd.uniform(-1.5, 1.5), -29 + k * 2.2, 5.1), rot=(rnd.uniform(-40, 40), rnd.uniform(-40, 40), 0), bev=0.02, parent=aft)
    # a smashed engine: the starboard bell is gone, the middle one crumpled
    for ob in list(aft.children):
        if ob.name.startswith(("engine_bell", "engine_lip", "engine_core")):
            c = _centroid(ob)
            if c.x > 3.0:
                bpy.data.objects.remove(ob, do_unlink=True)
            elif abs(c.x) < 1.0:
                ob.rotation_euler.z += math.radians(14)
                ob.rotation_euler.x += math.radians(8)
    # soot round the breaks and the fires
    for P, pts in ((aft, [(0, -12.0, 2.5), (6.5, -16, 3.0), (-6.0, -24, 4.8), (0, -31, 1.5)]), (bow, [(6.0, 15.6, 2.8), (0, 14.0, 2.0), (5.0, 24, 2.6)]),
                   (sa, [(0, -11.8, 2), (0, 1.0, 2)]), (sb, [(0, 1.0, 2), (0, 14, 2)])):
        for p in pts:
            soot(P, p, 1.6, rnd)
    # inside: emergency lights, fallen ceiling panels, hanging cables, sparks
    red = glow("#ff3b2f", 4)
    for P, pts in ((aft, [(0, -18, 3.8), (-4.4, -21, 3.9), (4.4, -15, 3.9), (0, -27, 3.9), (4.4, -21, 3.9), (-4.4, -15, 3.9)]),
                   (bow, [(0, 17, 3.7), (0, 24, 3.6)]), (sa, [(0, -5.5, 3.2)]), (sb, [(0, 7.5, 3.2)])):
        for x, y, z in pts:
            K.box("emergency_light", (0.3, 0.15, 0.1), red, loc=(x + 0.9, y, z), bev=0.02, parent=P)
            K.point("emergency", (x + 0.9, y, z - 0.4), "#ff4a3a", 60, 0.3).parent = P
            K.box("fallen_panel", (1.2, 0.8, 0.05), mats["inner"], loc=(x + rnd.uniform(-1, 1), y + rnd.uniform(-1, 1), 0.15), rot=(rnd.uniform(-20, 20), rnd.uniform(-10, 10), rnd.uniform(0, 180)), bev=0.01, parent=P)
            K.torus("hanging_cable", 0.6, 0.03, K.mat("cable", "#1a1a1a", rough=0.6), loc=(x - 0.8, y + 0.5, z - 0.6), rot=(90, 0, rnd.uniform(0, 180)), arc=180, parent=P)
            K.sphere("spark", 0.04, glow("#ffe08a", 30), loc=(x - 0.8, y + 0.5, z - 1.2), parent=P)
    # bridge: a cracked window panel, spilled mug
    K.box("crack_star", (0.02, 0.9, 0.7), K.mat("crack", "#d9eef5", rough=0.4, emit="#ffffff", emit_strength=0.2), loc=(S.bow_at(28)[0] - 0.12, 28.0, 2.0), rot=(0, 0, -20), bev=0, parent=bow)

    # ---- the pieces come to rest: shift each piece's children so its pivot is its origin, then place it
    for key, (loc, rot) in PIECES.items():
        P = pieces[key]
        for ch in P.children:
            ch.location -= Vector(PIVOTS[key])
        P.location = loc
        P.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
    # the lost housing lies upside-down off the port side of the furrow
    lost.matrix_world = Matrix.Translation((-20.0, -26.0, 9.6)) @ Euler((math.radians(170), math.radians(12), math.radians(40))).to_matrix().to_4x4()
    bpy.context.view_layer.update()

    if not scene:
        return root, pieces
    # ---- what the game places itself: furrow, scorch, debris, fires, smoke
    K.strip("furrow", [(-8, -70), (-7, -45), (-5, -25), (-1, -5), (3, 10), (5, 30)], 9.0, K.mat("furrow", "#3f3226", rough=1.0)).parent = root
    K.disc("scorch", 26, K.mat("scorched", "#3a3128", rough=1.0, noise_scale=1.5), (0, -2), z=0.012, wobble=0.25, seed=seed).parent = root
    for k in range(70):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(8, 34)
        K.box("debris", (rnd.uniform(0.3, 1.4), rnd.uniform(0.3, 1.2), 0.1), mats["hull"] if k % 3 else mats["dark"],
              loc=(d * math.cos(a) * 0.7, d * math.sin(a) * 1.4 - 10, 0.07), rot=(rnd.uniform(-15, 15), rnd.uniform(-15, 15), rnd.uniform(0, 360)), bev=0.02, parent=root)
    for key, _, loc, sc in FIRES:
        P = pieces[FIRE_PIECE[key]]
        f = K.link(bpy.data.objects.new(f"fire_{key}", None))
        f.parent = P
        f.location = Vector(loc) - Vector(PIVOTS[FIRE_PIECE[key]])
        bpy.context.view_layer.update()
        w = f.matrix_world.translation.copy()
        f.parent = root
        f.location = (w.x, w.y, max(0.0, w.z) if key in ("airlock", "mess_door") else w.z)
        f.rotation_euler = (0, 0, 0)
        blaze(f, (0, 0, 0), sc, rnd)
        if key in ("airlock", "mess_door", "spine_end"):
            smoke(root, (w.x, w.y, 0.0), rnd=rnd)
    return root, pieces


def scatter(root, rnd, only=None):
    """The burst containers round the site, with a fire on every third one."""
    by_code = {c["code"]: c for c in S.CONTAINERS}
    out = []
    for i, (code, x, y, yaw, roll) in enumerate(SCATTER):
        if only and code not in only:
            continue
        c = by_code[code]
        b = burst_container(code, c["colour"], (x, y, 0), yaw, roll, rnd)
        b.parent = root
        if i % 3 == 0:
            blaze(b, (1.0, 5.0, 0.0), 0.7, rnd)
        out.append(b)
    return out
