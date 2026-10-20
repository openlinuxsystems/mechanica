"""The design language's families (docs/art-direction.md): palette and the structures built from it.

Colony Works (the Workshop Outpost, the dead relay beacon), Settler Patchwork (the farmer's shack and its garden) and Old
Stone (arches and pillars). The Salvager Tech ship is in ship.py. Each builder returns an empty with the structure
parented under it, its origin on the ground at the structure's centre (Blender Z up, front towards -Y).
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR

# ---------------------------------------------------------------- palette
BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL = "#d9d1bc", "#3e6a8c", "#e0b530", "#2b2f35", "#c8423a"
SEA, BRICK, RUST, WOOD, ZINC = "#4f9a93", "#a8473b", "#7a4a30", "#9c7650", "#9aa3ab"
STONE, STONE_D = "#8f9289", "#5f645c"
HULL, GUNMETAL, CYAN, COPPER = "#24282e", "#5a6068", "#38c8ff", "#b8733a"
CONCRETE = "#a9a79e"
WARM = "#fff1cc"


def M(color, **kw):
    kw.setdefault("wear", 0.3)
    kw.setdefault("grime", 0.3)
    kw.setdefault("scratches", 0.3)
    kw.setdefault("rough", 0.55)
    return K.mat(f"m{color}", color, **kw)


def glow(color, strength=6):
    return K.mat(f"glow{color}{strength}", color, rough=0.3, emit=color, emit_strength=strength)


def shell_panel(name, R, a0, a1, y0, y1, material, t=0.08, parent=None, loc=(0, 0, 0)):
    bm = bmesh.new()
    n = 6
    outer0, outer1, inner0, inner1 = [], [], [], []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        for (lst, rr, yy) in ((outer0, R, y0), (outer1, R, y1), (inner0, R - t, y0), (inner1, R - t, y1)):
            lst.append(bm.verts.new((rr * math.cos(a), yy, rr * math.sin(a))))
    for i in range(n):
        bm.faces.new((outer0[i], outer0[i + 1], outer1[i + 1], outer1[i]))
        bm.faces.new((inner0[i], inner1[i], inner1[i + 1], inner0[i + 1]))
    bm.faces.new((outer0[0], outer1[0], inner1[0], inner0[0]))
    bm.faces.new((outer0[-1], inner0[-1], inner1[-1], outer1[-1]))
    for i in range(n):
        bm.faces.new((outer0[i], inner0[i], inner0[i + 1], outer0[i + 1]))
        bm.faces.new((outer1[i], outer1[i + 1], inner1[i + 1], inner1[i]))
    ob = K.from_bm(name, bm, material, smooth=True)
    return K.place(ob, loc, (0, 0, 0), parent)


# ================================================================== Settler Patchwork
def settler_shack(o=(0, 0, 0), yaw=0, name="settler", seed=3, garden=True):
    rnd = random.Random(seed)
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    P = root
    R, L = 3.4, 7.0
    pal = [BONE, SEA, BRICK, MUSTARD, ZINC, RUST, STEEL]
    rows = 4
    cols = 6
    for r in range(rows):
        for c in range(cols):
            a0 = math.pi * c / cols - 0.03
            a1 = math.pi * (c + 1) / cols + 0.03
            y0 = -L / 2 + L * r / rows - 0.05
            y1 = -L / 2 + L * (r + 1) / rows + 0.05
            col = rnd.choice(pal)
            rr = R + rnd.uniform(-0.03, 0.08) + (0.05 if (r + c) % 2 else 0)
            p = shell_panel(f"plate{r}{c}", rr, a0, a1, y0, y1, M(col, wear=0.7, grime=0.4), parent=P)
            p.rotation_euler[1] = rnd.uniform(-0.03, 0.03)
            # rivets along the panel's lower edge
            for k in range(3):
                a = a0 + (a1 - a0) * (k + 0.5) / 3
                K.sphere(f"rivet{r}{c}{k}", 0.06, M(GUNMETAL, wear=0.8), loc=((rr + 0.02) * math.cos(a), y0 + 0.15, (rr + 0.02) * math.sin(a)), parent=P)
    # ribs
    for r in range(rows + 1):
        K.torus(f"rib{r}", R + 0.1, 0.09, M(RUST, wear=0.3), loc=(0, -L / 2 + L * r / rows, 0), rot=(90, 0, 0), arc=180, parent=P)
    # front wall of planks
    wood = K.mat("wood", WOOD, rough=0.8, wear=0.0, grime=0.3, noise_scale=2.0, cracks=0.0)
    x = -R + 0.15
    while x < R - 0.1:
        h = math.sqrt(max(R * R - x * x, 0)) - 0.05
        K.box("plank", (0.32, 0.12, h), K.mat(f"wood{rnd.randint(0, 2)}", rnd.choice([WOOD, "#8a6644", "#a9845a"]), rough=0.8, grime=0.3, noise_scale=2.0),
              loc=(x, -L / 2 - 0.05, h / 2), rot=(0, rnd.uniform(-1.5, 1.5), 0), bev=0.03, parent=P)
        x += 0.34
    # door with bone frame and a hand-painted seed sign
    K.box("door", (1.25, 0.15, 2.5), M(SEA, wear=0.8), loc=(0.6, -L / 2 - 0.15, 1.25), bev=0.04, parent=P)
    K.box("door_frame", (1.6, 0.12, 2.8), M(BONE, wear=0.5), loc=(0.6, -L / 2 - 0.12, 1.4), bev=0.05, parent=P)
    K.box("sign_board", (1.3, 0.1, 0.8), wood, loc=(-1.8, -L / 2 - 0.2, 1.8), rot=(0, 4, 0), bev=0.04, parent=P)
    K.sphere("sign_leaf_a", 0.18, M("#6fbf3a", wear=0, grime=0), loc=(-1.95, -L / 2 - 0.27, 1.85), scale=(1.4, 0.3, 0.7), parent=P)
    K.sphere("sign_leaf_b", 0.18, M("#6fbf3a", wear=0, grime=0), loc=(-1.62, -L / 2 - 0.27, 1.92), scale=(1.4, 0.3, 0.7), parent=P)
    K.box("sign_stem", (0.06, 0.05, 0.4), M("#3d6b25", wear=0, grime=0), loc=(-1.8, -L / 2 - 0.27, 1.62), bev=0.01, parent=P)
    # bulb rack over the door
    K.box("rack", (3.2, 0.14, 0.14), M(GRAPHITE), loc=(0.2, -L / 2 - 0.55, 3.0), bev=0.03, parent=P)
    K.box("rack_arm_a", (0.12, 0.6, 0.12), M(GRAPHITE), loc=(-1.3, -L / 2 - 0.25, 3.0), bev=0.02, parent=P)
    K.box("rack_arm_b", (0.12, 0.6, 0.12), M(GRAPHITE), loc=(1.7, -L / 2 - 0.25, 3.0), bev=0.02, parent=P)
    for k, bx in enumerate((-1.0, -0.2, 0.6, 1.4)):
        K.sphere(f"bulb{k}", 0.22, glow("#ffe9a8", 14), loc=(bx, -L / 2 - 0.6, 3.25), parent=P)
        K.cyl(f"bulb_cap{k}", 0.13, 0.15, M(GUNMETAL), loc=(bx, -L / 2 - 0.6, 3.07), parent=P, bev=0)
    # twin fan box on top
    K.box("fanbox", (2.2, 1.0, 1.2), M(BONE, wear=0.6), loc=(-0.4, -L / 2 + 0.6, R + 0.3), bev=0.1, parent=P)
    for k, fx in enumerate((-0.95, 0.15)):
        K.torus(f"fanring{k}", 0.42, 0.07, M(GRAPHITE), loc=(fx, -L / 2 + 0.07, R + 0.3), rot=(90, 0, 0), parent=P)
        for bl in range(4):
            K.box(f"fanblade{k}{bl}", (0.7, 0.03, 0.16), M(BRICK, wear=0.5), loc=(fx, -L / 2 + 0.05, R + 0.3), rot=(0, 45 * bl + 10, 0), bev=0.02, parent=P)
    # red pipe arching over the dome
    K.torus("pipe_arch", R + 0.35, 0.16, M(SIGNAL, wear=0.5), loc=(0, -L / 2 + 1.8, 0), rot=(90, 0, 0), arc=180, parent=P)
    K.torus("pipe_arch2", R + 0.35, 0.16, M(SIGNAL, wear=0.5), loc=(0, L / 2 - 1.2, 0), rot=(90, 0, 0), arc=180, parent=P)
    # crates and barrels by the door
    K.box("crate", (0.9, 0.9, 0.8), wood, loc=(2.6, -L / 2 - 1.0, 0.4), rot=(0, 0, 12), bev=0.05, parent=P)
    K.cyl("barrel", 0.4, 1.1, M(SEA), loc=(-1.75, -L / 2 - 0.6, 0.55), parent=P)
    # trade counter: an open stall the friendly farmer stands in (the crash site's npc marker), behind the counter
    sea, bone = M(SEA, wear=0.6), M(BONE, wear=0.5)
    bx, by = -3.6, -L / 2 - 1.2  # stall centre; 2.6 m wide, 2.0 m deep, 2.5 m tall
    FR.framed_box(P, "booth_back", (bx, by + 0.94, 1.25), (2.6, 0.12, 2.5), sea, bone, fw=0.12)
    for sx in (-1, 1):
        FR.framed_box(P, "booth_side", (bx + sx * 1.24, by, 1.25), (0.12, 2.0, 2.5), sea, bone, fw=0.12)
    FR.framed_box(P, "booth_roof", (bx, by, 2.44), (2.6, 2.0, 0.12), sea, bone, fw=0.12)
    FR.framed_box(P, "booth_front", (bx, by - 0.94, 0.525), (2.6, 0.12, 1.05), sea, bone, fw=0.12)
    # inside: a shelf of jars on the back wall and a bare bulb
    K.box("booth_shelf", (2.2, 0.3, 0.06), wood, loc=(bx, by + 0.72, 1.55), bev=0.01, parent=P)
    for k in range(5):
        K.cyl(f"jar{k}", 0.09, 0.24, M(rnd.choice(("#6fbf3a", "#c8423a", "#e0b530", "#4f9a93")), wear=0.1, scratches=0),
              loc=(bx - 0.8 + k * 0.4, by + 0.72, 1.7), parent=P, bev=0.01)
    K.sphere("booth_bulb", 0.1, glow("#ffe9a8", 14), loc=(bx, by, 2.2), parent=P)
    K.point("booth_light", (bx, by, 2.1), WARM, 60, 0.1).parent = P
    K.box("counter", (2.4, 0.5, 0.1), K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0), loc=(-3.6, -L / 2 - 2.4, 1.1), bev=0.02, parent=P)
    for k in range(7):  # striped awning
        K.box("awning", (0.42, 1.2, 0.05), M(BONE if k % 2 else BRICK, wear=0.3, scratches=0), loc=(-4.86 + k * 0.42, -L / 2 - 2.6, 2.45), rot=(-20, 0, 0), bev=0.01, parent=P)
    for k, (c, px) in enumerate((("#6fbf3a", -4.6), ("#c8423a", -4.3), ("#e0b530", -2.75))):  # seed packets at the counter's ends
        K.box("packet", (0.25, 0.08, 0.32), M(c, wear=0.1, scratches=0), loc=(px, -L / 2 - 2.45, 1.32), bev=0.02, parent=P)
    if garden:
        garden_plot(P, (0, -L / 2 - 6.5, 0), rnd)
    return root


def garden_plot(P, c, rnd, w=8.0, d=5.0):
    wood = K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0)
    soil = K.mat("soil", "#5a3f2a", rough=1.0, noise_scale=3.0)
    cx, cy, _ = c
    for k in range(5):
        K.box(f"row{k}", (w - 1.0, 0.6, 0.2), soil, loc=(cx, cy - d / 2 + 0.7 + k * 0.95, 0.1), bev=0.08, parent=P)
        for j in range(8):
            for leaf in range(5):
                a = leaf * 72 + rnd.uniform(-15, 15)
                K.cyl("leaf", 0.09, 0.55, M(rnd.choice(["#5fae32", "#6fbf3a", "#4f9a2a"]), wear=0, grime=0, scratches=0),
                      loc=(cx - w / 2 + 1.0 + j * 0.85 + 0.1 * math.cos(math.radians(a)), cy - d / 2 + 0.7 + k * 0.95 + 0.1 * math.sin(math.radians(a)), 0.45),
                      rot=(35 * math.sin(math.radians(a)), -35 * math.cos(math.radians(a)), 0), segs=5, bev=0, parent=P, r2=0.01)
    posts = []
    for i in range(9):
        posts.append((cx - w / 2 + i * w / 8, cy - d / 2))
        posts.append((cx - w / 2 + i * w / 8, cy + d / 2))
    for j in range(1, 6):
        posts.append((cx - w / 2, cy - d / 2 + j * d / 6))
        posts.append((cx + w / 2, cy - d / 2 + j * d / 6))
    for i, (px, py) in enumerate(posts):
        if abs(px - cx) < 0.6 and py < cy:  # gate gap
            continue
        K.box(f"post{i}", (0.14, 0.14, 1.0), wood, loc=(px, py, 0.5), rot=(0, 0, rnd.uniform(-4, 4)), bev=0.02, parent=P)
    for z in (0.45, 0.85):
        K.box("rail_l", (0.08, d, 0.1), wood, loc=(cx - w / 2, cy, z), bev=0.02, parent=P)
        K.box("rail_r", (0.08, d, 0.1), wood, loc=(cx + w / 2, cy, z), bev=0.02, parent=P)
        K.box("rail_b", (w, 0.08, 0.1), wood, loc=(cx, cy + d / 2, z), bev=0.02, parent=P)
        K.box("rail_f1", (w / 2 - 0.6, 0.08, 0.1), wood, loc=(cx - w / 4 - 0.3, cy - d / 2, z), bev=0.02, parent=P)
        K.box("rail_f2", (w / 2 - 0.6, 0.08, 0.1), wood, loc=(cx + w / 4 + 0.3, cy - d / 2, z), bev=0.02, parent=P)


# ================================================================== Old Stone
def stone():
    return K.mat("stone", STONE, rough=0.9, wear=0.0, grime=0.35, moss=1.0, cracks=0.7, noise_scale=3.0)


def old_stone_arch(o=(0, 0, 0), yaw=0, name="oldstone", seed=5, broken=False):
    rnd = random.Random(seed)
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    P = root
    s = stone()
    span, ph = 4.0, 4.5
    for side in (-1, 1):
        z = 0
        for k in range(5):
            h = rnd.uniform(0.8, 1.0)
            K.box(f"pblock{side}{k}", (1.4 + rnd.uniform(-0.1, 0.1), 1.4, h), s, loc=(side * (span / 2 + 0.7) + rnd.uniform(-0.05, 0.05), 0, z + h / 2),
                  rot=(0, 0, rnd.uniform(-4, 4)), bev=0.1, parent=P)
            z += h
    R = span / 2 + 1.4
    n = 9
    for k in range(n):
        if broken and k in (6, 7, 8):
            continue
        a0 = math.pi * k / n + 0.012
        a1 = math.pi * (k + 1) / n - 0.012
        v = shell_panel(f"vous{k}", R, a0, a1, -0.65, 0.65, s, t=1.4, parent=P, loc=(0, 0, 4.3))
        v.data.polygons.foreach_set("use_smooth", [False] * len(v.data.polygons))
        K.bevel(v, 0.08, segs=2, angle=30)
    K.box("key", (0.9, 1.5, 1.6), s, loc=(0, 0, 4.3 + R - 0.6), bev=0.12, parent=P)
    for k in range(3):
        K.box(f"step{k}", (span + 4 - k * 1.2, 1.2, 0.3), s, loc=(0, -1.6 + k * 0.35, 0.15 + k * 0.3), bev=0.06, parent=P)
    K.box("fallen", (1.4, 1.3, 0.8), s, loc=(R + 1.2, -1.8, 0.4), rot=(8, 0, 33), bev=0.1, parent=P)
    # colony probe crystal growing out of the stone
    for k in range(4):
        cr = K.cyl(f"crystal{k}", 0.16, 0.9 + k * 0.2, glow(CYAN, 3), loc=(-R - 0.2 + k * 0.15, -1.0, 0.4), rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), 0), segs=6, bev=0, parent=P, r2=0.02)
    return root


def stone_pillar(o, h=4.0, seed=1, name="pillar"):
    rnd = random.Random(seed)
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    s = stone()
    z = 0
    while z < h:
        bh = rnd.uniform(0.7, 1.0)
        K.box("pb", (1.3, 1.3, bh), s, loc=(rnd.uniform(-0.06, 0.06), 0, z + bh / 2), rot=(0, 0, rnd.uniform(-6, 6)), bev=0.1, parent=root)
        z += bh
    K.box("cap", (1.6, 1.6, 0.4), s, loc=(0, 0, z + 0.2), rot=(0, rnd.uniform(-5, 5), 0), bev=0.1, parent=root)
    return root


def fire(P, loc, scale=1.0, light=True):
    x, y, z = loc
    fm = K.mat("flame", "#ffb347", rough=0.5, emit="#ffa13a", emit_strength=18)
    fm2 = K.mat("flame2", "#ffe08a", rough=0.5, emit="#ffd36a", emit_strength=25)
    for k in range(5):
        K.cyl(f"flame{k}", 0.35 * scale, (1.0 + 0.3 * k) * scale, fm if k % 2 else fm2, loc=(x + 0.2 * math.cos(k * 1.3) * scale, y + 0.2 * math.sin(k * 1.3) * scale, z + 0.5 * scale),
              rot=(0, 0, 0), segs=6, bev=0, parent=P, r2=0.02)
    if light:
        K.point("firelight", (x, y, z + 1.2 * scale), "#ffa64d", 900 * scale, 0.5).parent = P


# ================================================================== Colony Works
def workshop_outpost(o=(0, 0, 0), yaw=0, name="outpost"):
    """Mostly framed boxes like the part models, with one rounded signature volume (the office drum) and round
    things only where they are round in life: tanks, pipes, portholes, lamps."""
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    P = root
    bone, steel, mus, gra = M(BONE), M(STEEL, metal=0.3), M(MUSTARD), M(GRAPHITE, metal=0.3)
    gun = M(GUNMETAL, metal=0.5)
    conc = K.mat("concrete", CONCRETE, rough=0.9, grime=0.2, noise_scale=3, ao=0.4)
    K.box("pad", (24, 17, 0.3), conc, loc=(0, -1, 0.15), bev=0.06, parent=P)
    for k in range(4):  # parking bay lines
        K.box("bayline", (0.15, 4.5, 0.02), M(MUSTARD, wear=0.6, scratches=0), loc=(-3 + k * 3.2, -7.0, 0.31), bev=0, parent=P)
    # garage hall
    FR.framed_box(P, "hall", (1.5, 1.0, 3.3), (12, 9, 6), bone, steel, fw=0.35, screws=gun)
    # wall panels: posts every 3 m and a mid rail, like the part cases' frames
    for x in (-1.5, 6.0):
        K.box("wall_post", (0.28, 0.3, 5.6), M(STEEL, metal=0.3), loc=(x, -3.55, 3.3), bev=0.03, parent=P)
    for x in (-4.0, -1.0, 2.0, 5.0):
        K.box("side_post", (0.3, 0.28, 5.6), M(STEEL, metal=0.3), loc=(7.55, x + 3.0, 3.3), bev=0.03, parent=P)
    K.box("mid_rail", (12.1, 0.25, 0.25), M(STEEL, metal=0.3), loc=(1.5, -3.55, 5.0), bev=0.03, parent=P)
    K.box("side_rail", (0.25, 9.1, 0.25), M(STEEL, metal=0.3), loc=(7.55, 1.0, 5.0), bev=0.03, parent=P)
    # conduit along the wall
    K.cyl("conduit", 0.08, 9.0, gun, loc=(7.7, 1.0, 4.6), rot=(90, 0, 0), bev=0, parent=P)
    K.cyl("conduit2", 0.08, 4.0, gun, loc=(-3.4, -3.65, 2.8), bev=0, parent=P)
    # roll-up door bay (half open, dark inside)
    K.box("bay", (6.4, 0.6, 4.6), K.mat("bayin", "#1a1c1f", rough=0.9), loc=(2.5, -3.35, 2.6), bev=0.02, parent=P)
    for k in range(9):
        K.box("door_slat", (6.2, 0.12, 0.26), gun, loc=(2.5, -3.62, 4.75 - k * 0.27), bev=0.03, parent=P)
    for side in (-1, 1):
        K.box("door_post", (0.4, 0.5, 4.9), gra, loc=(2.5 + side * 3.4, -3.6, 2.75), bev=0.04, parent=P)
        for k in range(8):
            K.box("post_haz", (0.42, 0.52, 0.3), M(MUSTARD) if k % 2 == 0 else gra, loc=(2.5 + side * 3.4, -3.6, 0.6 + k * 0.3), bev=0.01, parent=P)
    K.box("door_head", (7.2, 0.6, 0.5), gra, loc=(2.5, -3.6, 5.2), bev=0.04, parent=P)
    # powered access panel by the door
    FR.screen(P, (-1.6, -3.55, 2.0), 0.7, 0.9, gra, glow(CYAN, 2.5))
    FR.label(P, "WORKSHOP", (-1.6, -3.66, 2.75), 0.22, M(BONE, wear=0, scratches=0, grime=0))
    FR.vent(P, (-2.8, -3.55, 4.2), 1.4, 0.9, gra, gun)
    FR.vent(P, (7.56, 2.0, 1.4), 1.6, 1.0, gra, gun, axis="x")
    # roof with overhang and the warm light bar
    K.box("roof", (13.0, 10.0, 0.4), gra, loc=(1.5, 0.8, 6.5), bev=0.05, parent=P)
    K.box("lightbar", (11.0, 0.15, 0.16), glow(WARM, 10), loc=(1.5, -4.1, 6.15), bev=0.03, parent=P)
    # roof sign on posts: wrench pictogram and stencil name
    for x in (-0.4, 3.4):
        K.box("sign_post", (0.2, 0.2, 1.2), gra, loc=(x, 0.0, 7.3), bev=0.02, parent=P)
    FR.framed_box(P, "sign", (1.5, 0.0, 8.4), (5.6, 0.4, 1.6), M(MUSTARD), gra, fw=0.14)
    FR.label(P, "OUTPOST 7", (2.2, -0.22, 8.4), 0.6, gra)
    K.box("wrench_shaft", (0.22, 0.06, 0.95), gra, loc=(-0.45, -0.24, 8.35), rot=(0, 40, 0), bev=0.03, parent=P)
    K.torus("wrench_jaw", 0.2, 0.07, gra, loc=(-0.2, -0.24, 8.72), rot=(90, 0, 0), arc=260, parent=P).rotation_euler[1] = math.radians(140)
    # roof AC unit with a fan
    FR.framed_box(P, "ac", (5.0, 3.0, 7.3), (2.4, 2.0, 1.2), M(BONE), gun, fw=0.12)
    K.torus("fan_ring", 0.5, 0.06, gun, loc=(5.0, 3.0, 7.92), rot=(0, 0, 0), parent=P)
    # office drum: the one rounded signature volume
    K.cyl("drum", 3.4, 4.4, bone, loc=(-6.8, 0.4, 2.5), segs=40, bev=0.08, parent=P)
    for z in (0.6, 4.55):
        K.cyl("drum_band", 3.5, 0.35, steel, loc=(-6.8, 0.4, z), segs=40, bev=0.05, parent=P)
    K.cyl("drum_cap", 3.6, 0.35, gra, loc=(-6.8, 0.4, 4.9), segs=40, bev=0.06, parent=P)
    for k, a in enumerate((200, 230, 260)):
        r = math.radians(a)
        px, py = -6.8 + 3.42 * math.cos(r), 0.4 + 3.42 * math.sin(r)
        K.torus("port_rim", 0.48, 0.1, gra, loc=(px, py, 2.9), rot=(90, 0, a + 90), parent=P)
        K.cyl("port_glass", 0.46, 0.06, glow("#7fd8ff", 1.0), loc=(px, py, 2.9), rot=(90, 0, a + 90), bev=0, parent=P)
    # tank on framed legs, piped into the wall
    tm = M(MUSTARD)
    K.cyl("tank", 1.1, 3.6, tm, loc=(9.6, 1.5, 2.2), rot=(90, 0, 0), bev=0, parent=P)
    K.sphere("tank_a", 1.1, tm, loc=(9.6, -0.3, 2.2), scale=(1, 0.45, 1), parent=P)
    K.sphere("tank_b", 1.1, tm, loc=(9.6, 3.3, 2.2), scale=(1, 0.45, 1), parent=P)
    for y in (0.3, 2.7):
        FR.framed_box(P, "tank_leg", (9.6, y, 0.75), (1.9, 0.4, 1.2), gra, gun, fw=0.1)
        K.cyl("tank_band", 1.15, 0.25, gra, loc=(9.6, y, 2.2), rot=(90, 0, 0), bev=0.02, parent=P)
    K.cyl("pipe", 0.16, 1.6, gun, loc=(8.3, 1.5, 3.0), rot=(0, 90, 0), bev=0, parent=P)
    K.cyl("flange", 0.26, 0.1, M(MUSTARD), loc=(7.6, 1.5, 3.0), rot=(0, 90, 0), bev=0.01, parent=P)
    # square lampposts with a hooked head
    for x in (-9.5, 10.5):
        lamppost2(P, (x, -7.5, 0.3))
    return root


def lamppost2(P, loc, h=5.5):
    x, y, z = loc
    gra, gun = M(GRAPHITE, metal=0.3), M(GUNMETAL, metal=0.5)
    FR.framed_box(P, "lp_base", (x, y, z + 0.4), (0.7, 0.7, 0.8), M(STEEL), gra, fw=0.08)
    K.box("lp_pole", (0.22, 0.22, h), gra, loc=(x, y, z + h / 2), bev=0.03, parent=P)
    K.box("lp_arm", (1.4, 0.18, 0.18), gra, loc=(x + 0.6, y, z + h), bev=0.03, parent=P)
    K.box("lp_head", (0.7, 0.45, 0.3), M(BONE), loc=(x + 1.2, y, z + h - 0.18), bev=0.06, parent=P)
    K.box("lp_lens", (0.55, 0.32, 0.04), glow(WARM, 25), loc=(x + 1.2, y, z + h - 0.35), bev=0.01, parent=P)
    K.point("lp_light", (x + 1.2, y, z + h - 0.7), WARM, 150, 0.2).parent = P


def dead_beacon(o=(0, 0, 0), yaw=0, name="beacon"):
    """A Colony relay beacon, dark since the fall: a framed mast, a dish and a dead lamp."""
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    P = root
    gra, gun = M(GRAPHITE, metal=0.3), M(GUNMETAL, metal=0.5)
    K.box("pad", (3.2, 3.2, 0.3), K.mat("concrete", CONCRETE, rough=0.9, grime=0.2, noise_scale=3, ao=0.4), loc=(0, 0, 0.15), bev=0.04, parent=P)
    FR.framed_box(P, "cab", (0, 0, 1.0), (1.6, 1.2, 1.4), M(BONE), M(STEEL), fw=0.12, screws=gun)
    FR.vent(P, (0, -0.62, 1.0), 0.9, 0.6, gra, gun)
    for k in range(4):  # lattice mast
        for side in ((-0.35, -0.35), (0.35, -0.35), (-0.35, 0.35), (0.35, 0.35)):
            K.box("mast_leg", (0.1, 0.1, 2.2), gra, loc=(side[0], side[1], 2.8 + k * 2.2), bev=0.01, parent=P)
        K.box("mast_ring", (0.8, 0.8, 0.12), M(STEEL), loc=(0, 0, 1.75 + k * 2.2), bev=0.01, parent=P)
        K.box("brace", (0.06, 0.06, 2.5), gun, loc=(0, -0.36, 2.8 + k * 2.2), rot=(0, 15 if k % 2 else -15, 0), bev=0, parent=P)
    dish = K.sphere("dish", 1.0, M(BONE), loc=(0.6, -0.4, 9.2), scale=(1, 1, 0.3), parent=P)
    dish.rotation_euler = (math.radians(50), 0, math.radians(30))
    K.cyl("lamp", 0.3, 0.4, K.mat("deadlamp", "#5a3a3a", rough=0.3), loc=(0, 0, 10.9), parent=P)
    for k in range(4):
        K.box("haz", (0.8, 0.06, 0.18), M(MUSTARD) if k % 2 == 0 else gra, loc=(0, -0.62, 1.82 - k * 0.0), bev=0.01, parent=P) if k == 0 else None
    return root
