"""The Settler Patchwork kit (docs/art-direction.md): the pieces survivors build their shacks, camps and yards from.

Colony parts rebuilt from salvage: walls of mismatched painted plates bolted over a timber frame, plank walls, half
domes (the colony's hut shells, re-skinned plate by plate on rusty ribs) and lean-tos of odd corrugated sheets,
salvaged doors (a colony hatch, a plank door, a car-door hatch), striped awnings, the trade counter (the farmer's
stall at the crash site, as a kit piece), timber decks, stairs, scaffolds and ladders, fences of whatever came to hand,
bolted-on fans, bulb racks, lamps, stove pipes and pipe runs, junk piles, a raised yard pad and walls of big hay
bales. The palette is the faded colony colours (bone, sea teal, brick red, mustard, steel blue) with rust, scrap wood
and zinc; never orange.

Every piece is placed by loc (its origin) and yaw (degrees about +Z) under a parent and returns its group. Walls are
built in a local frame where the wall runs along +x, its outer face is the plane y = 0 and outside is -y (as in
colony_kit.py). Sizes sit on the 0.25 m build grid. Drawn parts never collide: each piece adds plain collision-only
boxes and ramps (colony_kit.solid and ramp) for what a player stands on or bumps into, so nothing snags. Stairs are
walked as ramps no steeper than 40 degrees. settlers.py assembles the settler buildings from these.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import colony_kit as C
from colony_kit import group, quiet, quiet_all, solid, ramp
from families import M, glow, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA, BRICK, RUST, WOOD, ZINC, GUNMETAL, WARM
from props_colony_b import beam as _bar, rod, plate_xz as _plate_xz

STEP = 0.25
BULB = "#ffe9a8"
FADED = {          # the colony colours after years of sun: a little paler and greyer than new paint
    "bone": "#d3cbb6", "sea": "#5f9a94", "brick": "#a5534a", "mustard": "#d2ad45", "steel": "#557a96",
    "zinc": "#9aa3ab", "rust": "#7a4a30",
}
PLATE_COLS = ("bone", "sea", "brick", "mustard", "steel", "zinc", "bone", "sea", "rust")
WOODS = ("#9c7650", "#8a6644", "#a9845a", "#7d5c3e", "#8f8170")     # the last one silvered by the weather
STRAW = "#cdb36a"
EARTH = "#8c7b5c"
CANVAS = "#c9b88e"
VOID = "#17191c"


# ---------------------------------------------------------------- materials
def plate(col):
    """A painted steel plate off some colony building: faded paint, chipped edges, dirt and scratches."""
    c = FADED.get(col, col)
    if col == "rust":
        return M(c, metal=0.35, wear=0.1, grime=0.35, rough=0.85, scratches=0.1)
    if col == "zinc":
        return M(c, metal=0.45, wear=0.15, grime=0.45, scratches=0.4)
    return M(c, metal=0.3, wear=0.45, grime=0.45, scratches=0.45)


def rib():
    """Rusty steel: the ribs, frames and pipe the settlers reuse as they found them."""
    return M(RUST, metal=0.35, wear=0.15, grime=0.3, rough=0.85, scratches=0.1)


def timber(shade=0):
    return K.mat(f"swood{shade % len(WOODS)}", WOODS[shade % len(WOODS)], rough=0.85, wear=0.2, grime=0.35, noise_scale=2.0, scratches=0.25)


def zinc():
    return M(ZINC, metal=0.45, wear=0.15, grime=0.45, scratches=0.4)


def iron():
    return M(GRAPHITE, metal=0.5, wear=0.2, grime=0.4)


def canvas(col=CANVAS):
    return K.mat(f"canvas{col}", col, rough=0.95, wear=0.0, grime=0.35, scratches=0.0, noise_scale=5.0)


def earth():
    return K.mat("packed_earth", EARTH, rough=1.0, wear=0.0, grime=0.25, noise_scale=1.5, ao=0.3)


def straw(dark=False):
    return K.mat("straw_d" if dark else "straw", "#b89b52" if dark else STRAW, rough=0.95, grime=0.25, noise_scale=12.0, scratches=0)


def void():
    return K.mat("void", VOID, rough=1.0, ao=0)


def glass():
    return K.mat("oldglass", "#5f7880", rough=0.15, metal=0.2, grime=0.3)


# ---------------------------------------------------------------- small helpers
def bolt(G, loc, axis="y", out=None):
    """A bolt head: a low square pyramid standing out of the face towards -y (or -x, or +z; or along the vector out,
    for a curved shell), six triangles, so hundreds of them stay cheap."""
    bm = bmesh.new()
    a, h = 0.03, 0.025
    base = [bm.verts.new((a * math.cos(t), 0.0, a * math.sin(t))) for t in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2)]
    tip = bm.verts.new((0.0, -h, 0.0))
    bm.faces.new(base)
    for i in range(4):
        bm.faces.new((base[(i + 1) % 4], base[i], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm("bolt", bm, M(GUNMETAL, metal=0.5, wear=0.3))
    if out is not None:
        ob.location = loc
        ob.rotation_euler = Vector((0, -1, 0)).rotation_difference(Vector(out).normalized()).to_euler()
        ob.parent = G
        return quiet(ob)
    rot = {"y": (0, 0, 0), "x": (0, 0, -90), "z": (-90, 0, 0)}[axis]
    return quiet(K.place(ob, loc, rot, G))


def board(G, name, size, loc, mat, rot=(0, 0, 0)):
    return quiet(K.box(name, size, mat, loc=loc, rot=rot, bev=0.006, parent=G))


def bar(G, name, a, b, w, mat, d=None):
    return quiet(_bar(G, name, a, b, w, mat, d=d, bev=0))


def pipe(G, name, a, b, r, mat, segs=8):
    return quiet(rod(G, name, a, b, r, mat, segs=segs))


def sheet(G, name, w, l, mat, loc, rot=(0, 0, 0), pitch=0.25, depth=0.035):
    """A corrugated sheet w wide and l long, ribs along its length, in its own frame facing local -y (rot turns it:
    rot (-90, 0, 0) lays it face up along +y)."""
    S = group(G, name, loc)
    S.rotation_euler = [math.radians(a) for a in rot]
    C.corrugated(S, name, -w / 2, w / 2, -l / 2, l / 2, mat, y=0.0, pitch=pitch, depth=depth, t=0.012)
    return S


def arc_plate(G, name, R, a0, a1, y0, y1, mat, t=0.06, n=3, zc=0.0, xc=0.0):
    """A curved plate of a shell round the y axis (angles from +x towards +z), centred at (xc, zc)."""
    bm = bmesh.new()
    o0, o1, i0, i1 = [], [], [], []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        ca, sa = math.cos(a), math.sin(a)
        for lst, rr, yy in ((o0, R, y0), (o1, R, y1), (i0, R - t, y0), (i1, R - t, y1)):
            lst.append(bm.verts.new((xc + rr * ca, yy, zc + rr * sa)))
    for i in range(n):
        bm.faces.new((o0[i], o0[i + 1], o1[i + 1], o1[i]))
        bm.faces.new((i0[i], i1[i], i1[i + 1], i0[i + 1]))
        bm.faces.new((o0[i], i0[i], i0[i + 1], o0[i + 1]))
        bm.faces.new((o1[i], o1[i + 1], i1[i + 1], i1[i]))
    bm.faces.new((o0[0], o1[0], i1[0], i0[0]))
    bm.faces.new((o0[-1], i0[-1], i1[-1], o1[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.parent = G
    return quiet(ob)


def arc_rib(G, name, R, a0, a1, y, w, mat, n=10, zc=0.0, xc=0.0, d=None):
    """A rusty steel rib bent round the y axis from angle a0 to a1 (square section, in straight lengths)."""
    pts = [(xc + R * math.cos(a0 + (a1 - a0) * i / n), y, zc + R * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    for i in range(n):
        bar(G, name, pts[i], pts[i + 1], w, mat, d=d)
    return pts


def arc_solid(G, name, R, a0, a1, y0, y1, zc=0.0, xc=0.0, n=10, t=0.15):
    """Collision for a shell round the y axis: n flat boxes along the arc."""
    for i in range(n):
        aa, ab = a0 + (a1 - a0) * i / n, a0 + (a1 - a0) * (i + 1) / n
        am = (aa + ab) / 2
        chord = 2 * R * math.sin((ab - aa) / 2) + 0.04
        rc = R * math.cos((ab - aa) / 2) - t / 2
        solid(G, f"{name}{i}", (chord, y1 - y0, t), (xc + rc * math.cos(am), (y0 + y1) / 2, zc + rc * math.sin(am)),
              rot=(0, 90 - math.degrees(am), 0))


def streaks(G, rnd, x, z, y=0.0, n=1):
    """Rust run down from a bolt or an edge."""
    for _ in range(n):
        C.streak(G, x + rnd.uniform(-0.05, 0.05), z, rnd.uniform(0.3, 0.8), y, w=rnd.uniform(0.06, 0.12), seed=rnd.randrange(999))


def prop(P, build, loc, yaw=0.0, collide=None):
    """Places a world prop (a props*.py builder) in a structure, drawn only; collide is an optional collision box
    (size, centre offset) in the prop's frame."""
    root = build()
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    quiet_all(root)
    if collide:
        size, off = collide
        solid(root, "prop", size, off)
    return root


# ================================================================== ground
def pad(P, w, d, h=0.3, edge=1.0, mat=None, loc=(0, 0, 0), name="pad"):
    """A raised yard of packed earth: its top (w x d) at h, its sides ramped down to the ground over edge metres, so
    the yard is walked onto from any side. Drawn and collided as itself."""
    G = group(P, name, loc)
    bm = bmesh.new()
    top = [bm.verts.new((sx * w / 2, sy * d / 2, h)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bot = [bm.verts.new((sx * (w / 2 + edge), sy * (d / 2 + edge), -0.05)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat or earth())
    ob.parent = G
    return G


def edging(P, pts, rnd, z=0.3, name="edging"):
    """A kerb of odd boards and stones along the top edge of a pad (decor)."""
    G = group(P, name)
    for a, b in zip(pts[:-1], pts[1:]):
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        d = (b - a).normalized()
        t = 0.0
        while t < L - 0.3:
            ln = min(rnd.uniform(1.2, 2.6), L - t)
            p = a + d * (t + ln / 2)
            yaw = math.degrees(math.atan2(d.y, d.x)) + rnd.uniform(-2, 2)
            if rnd.random() < 0.7:
                board(G, "edge_board", (ln - 0.05, 0.12, 0.1), (p.x, p.y, z + 0.02), timber(rnd.randrange(5)), rot=(0, 0, yaw))
            t += ln + rnd.uniform(0.0, 0.6)
    return G


def straw_patch(P, loc, r, rnd, z=0.3):
    """Loose straw trodden flat on a yard."""
    dsc = K.disc("straw_patch", r, straw(rnd.random() < 0.5), loc[:2], z=z + 0.012 + rnd.uniform(0, 0.004), segs=10, wobble=0.3, seed=rnd.randrange(99))
    dsc.parent = P
    dsc.scale = (rnd.uniform(0.7, 1.3), rnd.uniform(0.7, 1.3), 1)
    dsc.rotation_euler = (0, 0, rnd.uniform(0, 6.28))
    return quiet(dsc)


# ================================================================== walls
def plate_wall(P, name, x0, x1, z0, z1, loc=(0, 0, 0), yaw=0.0, holes=(), seed=1, cols=PLATE_COLS, frame=True, collide=True,
               corrugated=0.3, t=0.2):
    """A wall of mismatched plates bolted over a timber frame: rows of painted plates of odd sizes and colours, each
    standing a little proud of the last and a hair out of true, some of them corrugated sheet, bolts at their corners
    with rust run down from a few. holes (x0, x1, z0, z1) are cut through (doors, windows). The frame (posts every
    2.5 m or less, rails at the foot and head) shows inside. Collides as boxes t thick round the holes."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    holes = list(holes)
    z = z0
    rows = []
    while z < z1 - 0.05:
        h = rnd.uniform(0.75, 1.3)
        if z1 - (z + h) < 0.45:
            h = z1 - z
        rows.append((z, z + h))
        z += h
    for ri, (za, zb) in enumerate(rows):
        x = x0 - (rnd.uniform(0.0, 0.5) if ri % 2 else 0.0)
        k = 0
        while x < x1 - 0.05:
            w = rnd.uniform(0.85, 1.7)
            if x1 - (x + w) < 0.45:
                w = x1 - x
            col = rnd.choice(cols)
            off = -0.015 - 0.025 * ((k + ri) % 2)
            ra = (max(x, x0) - 0.02, min(x + w, x1) + 0.02, za - 0.03, zb + 0.03)
            ra = (max(ra[0], x0), min(ra[1], x1), max(ra[2], z0), min(ra[3], z1))
            ribbed = rnd.random() < corrugated
            for r in C.rect_minus(*ra, holes):
                rw, rh = r[1] - r[0], r[3] - r[2]
                if rw < 0.05 or rh < 0.05:
                    continue
                cx, cz = (r[0] + r[1]) / 2, (r[2] + r[3]) / 2
                if ribbed:
                    C.corrugated(G, "plate_sheet", r[0], r[1], r[2], r[3], plate("zinc" if col in ("bone", "mustard") else col), y=off + 0.01, pitch=0.25, depth=0.03, t=0.012)
                else:
                    quiet(K.box("plate", (rw, 0.02, rh), plate(col), loc=(cx, off, cz), rot=(0, rnd.uniform(-0.6, 0.6), 0), bev=0.006, parent=G))
                if rw > 0.4 and rh > 0.4:
                    for bx, bz in ((r[0] + 0.07, r[3] - 0.07), (r[1] - 0.07, r[2] + 0.07)) if rnd.random() < 0.5 else ((r[1] - 0.07, r[3] - 0.07), (r[0] + 0.07, r[2] + 0.07)):
                        bolt(G, (bx, off - 0.025, bz))
                    if rnd.random() < 0.3:
                        streaks(G, rnd, r[0] + 0.07 if rnd.random() < 0.5 else r[1] - 0.07, r[3] - 0.1, y=off - 0.012)
            x += w
            k += 1
    if frame:
        _frame(G, x0, x1, z0, z1, holes, rnd)
    if collide:
        for a, b, c, d in C.holes_cut(x0, x1, z0, z1, holes):
            solid(G, "wall", (b - a, t, d - c), ((a + b) / 2, t / 2 - 0.05, (c + d) / 2))
    return G


def _frame(G, x0, x1, z0, z1, holes, rnd):
    """The timber frame behind a wall: posts at the ends, beside every hole and every 2.5 m or less, a sole plate and
    a head rail."""
    xs = {x0 + 0.06, x1 - 0.06}
    for h in holes:
        xs |= {h[0] - 0.06, h[1] + 0.06}
    n = max(1, math.ceil((x1 - x0) / 2.5))
    xs |= {x0 + (x1 - x0) * k / n for k in range(1, n)}
    xs = sorted(x for x in xs if x0 + 0.05 <= x <= x1 - 0.05)
    for x in xs:
        if any(h[0] < x < h[1] for h in holes):
            continue
        board(G, "stud", (0.12, 0.1, z1 - z0), (x, 0.06, (z0 + z1) / 2), timber(3))
    for z in (z0 + 0.06, z1 - 0.06):
        for a, b, _, _ in C.holes_cut(x0, x1, z - 0.06, z + 0.06, holes):
            board(G, "rail", (b - a, 0.1, 0.12), ((a + b) / 2, 0.06, z), timber(2))
    for h in holes:
        if h[3] < z1 - 0.2:
            board(G, "lintel", (h[1] - h[0] + 0.24, 0.12, 0.16), ((h[0] + h[1]) / 2, 0.06, h[3] + 0.08), timber(3))


def plank_wall(P, name, x0, x1, z0, z1, loc=(0, 0, 0), yaw=0.0, holes=(), seed=1, top=None, collide=True, painted=0.15, t=0.2,
               battens=True):
    """A wall of scrap boards nailed upright over rails: boards of odd widths and shades, their tops ragged, now and
    then one painted in a faded colony colour, two battens across. top(x) can cut the boards' tops to a line (a
    gable or a dome's end). Collides as boxes round the holes, under the top."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    holes = list(holes)
    x = x0
    k = 0
    while x < x1 - 0.02:
        w = min(rnd.uniform(0.17, 0.3), x1 - x)
        zt = z1 - rnd.uniform(0.0, 0.05)
        if top:
            zt = min(zt, top(x) , top(x + w)) - 0.02
        if zt > z0 + 0.05:
            m = plate(rnd.choice(("sea", "brick", "bone", "mustard"))) if rnd.random() < painted else timber(rnd.randrange(5))
            if top:
                zt = max(top(x), top(x + w)) - 0.02
            for r in C.rect_minus(x + 0.006, x + w - 0.006, z0, zt, holes):
                yb = -0.02 - 0.008 * (k % 2)
                if top and abs(r[3] - zt) < 1e-6:
                    # cut to the line above it (a gable, a dome's curve)
                    pts = [(r[0], r[2]), (r[1], r[2]), (r[1], top(r[1]) - 0.02), (r[0], top(r[0]) - 0.02)]
                    if pts[2][1] > r[2] + 0.02 and pts[3][1] > r[2] + 0.02:
                        quiet(_plate_xz("plank", pts, 0.03, m, loc=(0, yb, 0), parent=G, bev=0))
                    continue
                quiet(K.box("plank", (r[1] - r[0], 0.03, r[3] - r[2]), m, loc=((r[0] + r[1]) / 2, yb, (r[2] + r[3]) / 2),
                            bev=0, parent=G))
        x += w
        k += 1
    if battens:
        for zb in (z0 + 0.35, min(z1, z0 + 2.1)):
            for a, b, _, _ in C.holes_cut(x0, x1, zb - 0.05, zb + 0.05, holes):
                if top and top((a + b) / 2) < zb + 0.1:
                    continue
                board(G, "batten", (b - a - 0.04, 0.03, 0.1), ((a + b) / 2, -0.048, zb), timber(3), rot=(0, rnd.uniform(-0.8, 0.8), 0))
    _frame(G, x0, x1, z0, (z1 if not top else min(z1, top((x0 + x1) / 2))), holes, rnd) if not top else None
    if collide:
        if top:
            n = max(2, math.ceil((x1 - x0) / 0.5))
            for i in range(n):
                a, b = x0 + (x1 - x0) * i / n, x0 + (x1 - x0) * (i + 1) / n
                zt = min(top(a), top(b))
                for r in C.rect_minus(a, b, z0, zt, holes):
                    solid(G, "wall", (r[1] - r[0], t, r[3] - r[2]), ((r[0] + r[1]) / 2, t / 2 - 0.05, (r[2] + r[3]) / 2))
        else:
            for a, b, c, d in C.holes_cut(x0, x1, z0, z1, holes):
                solid(G, "wall", (b - a, t, d - c), ((a + b) / 2, t / 2 - 0.05, (c + d) / 2))
    return G


def porthole(G, x, z, r=0.35, y=0.0):
    """A colony porthole reused as a window: a steel-blue ring with bolts round smoky glass."""
    quiet(K.cyl("port_glass", r, 0.03, glass(), loc=(x, y + 0.01, z), rot=(90, 0, 0), segs=16, bev=0, parent=G))
    quiet(K.torus("port_ring", r + 0.03, 0.06, plate("steel"), loc=(x, y - 0.03, z), rot=(90, 0, 0), segs=18, parent=G))
    for k in range(6):
        a = k * math.pi / 3 + 0.3
        bolt(G, (x + (r + 0.03) * math.cos(a), y - 0.09, z + (r + 0.03) * math.sin(a)))


def window(G, x, z, w=1.0, h=0.8, y=0.0, shutter=True, seed=0):
    """A small framed window of old glass in a timber frame, a plank shutter hung open beside it."""
    rnd = random.Random(seed)
    quiet(K.box("win_glass", (w, 0.02, h), glass(), loc=(x, y + 0.03, z + h / 2), bev=0, parent=G))
    for sx in (-1, 1):
        board(G, "win_jamb", (0.08, 0.1, h + 0.16), (x + sx * (w / 2 + 0.04), y - 0.04, z + h / 2), timber(1))
    board(G, "win_head", (w + 0.3, 0.12, 0.08), (x, y - 0.05, z + h + 0.08), timber(1))
    board(G, "win_sill", (w + 0.3, 0.16, 0.06), (x, y - 0.06, z - 0.03), timber(0))
    board(G, "win_mullion", (0.04, 0.03, h), (x, y - 0.0, z + h / 2), timber(1))
    if shutter:
        s = 1 if rnd.random() < 0.5 else -1
        quiet(K.box("shutter", (w / 2, 0.03, h + 0.1), plate(rnd.choice(("sea", "brick", "bone"))), loc=(x + s * (w * 0.75 + 0.1), y - 0.08, z + h / 2),
                    rot=(0, 0, s * rnd.uniform(-8, 8)), bev=0.005, parent=G))


# ================================================================== doors
def door(G, x, z, y=0.0, w=1.25, h=2.5, kind="hatch", ajar=100.0, seed=0, lamp=True):
    """A salvaged door in an opening w x h (bottom centre at x, z on the wall face y), its leaf swung ajar degrees
    into the room against the inside wall so the doorway stays clear: a colony hatch (steel-blue framed leaf with
    a porthole), a plank door with Z braces, or a patched sheet door. A threshold board, hinges, a pull, and a bare
    bulb over it."""
    rnd = random.Random(seed)
    fw = 0.12
    for sx in (-1, 1):
        board(G, "door_jamb", (fw, 0.22, h + fw), (x + sx * (w / 2 + fw / 2), y + 0.02, z + (h + fw) / 2), timber(3))
    board(G, "door_head", (w + 2 * fw + 0.1, 0.24, fw), (x, y + 0.02, z + h + fw / 2), timber(3))
    board(G, "door_sill", (w + 0.1, 0.3, 0.04), (x, y + 0.02, z + 0.02), timber(1))
    L = group(G, "door_leaf", (x - w / 2 + 0.03, y + 0.1, z + 0.02), ajar)
    lw, lh = w - 0.06, h - 0.06
    if kind == "hatch":
        C.framed(L, "hatch", (lw / 2, 0.0, lh / 2), (lw, 0.07, lh), plate("steel"), M(GRAPHITE, metal=0.3, wear=0.3), fw=0.08)
        quiet(*[c for c in L.children])
        porthole(L, lw / 2, lh * 0.72, 0.16, y=-0.04)
        quiet(K.box("hatch_wheel", (0.3, 0.04, 0.04), iron(), loc=(lw * 0.8, -0.06, lh * 0.45), bev=0, parent=L))
    elif kind == "planks":
        n = 5
        for k in range(n):
            quiet(K.box("door_board", (lw / n - 0.01, 0.035, lh), timber(rnd.randrange(5)), loc=(lw * (k + 0.5) / n, 0.0, lh / 2), bev=0, parent=L))
        for zz in (0.3, lh - 0.3):
            quiet(K.box("door_ledge", (lw - 0.06, 0.03, 0.1), timber(3), loc=(lw / 2, -0.03, zz), bev=0, parent=L))
        bar(L, "door_brace", (0.1, -0.03, 0.35), (lw - 0.1, -0.03, lh - 0.35), 0.09, timber(3), d=0.03)
    else:
        C.corrugated(L, "door_sheet", 0.0, lw, 0.0, lh, plate(rnd.choice(("zinc", "sea", "rust"))), y=0.0, pitch=0.25, depth=0.03)
        for zz in (0.25, lh / 2, lh - 0.25):
            quiet(K.box("door_strap", (lw, 0.02, 0.06), iron(), loc=(lw / 2, -0.04, zz), bev=0, parent=L))
    for zz in (0.3, lh - 0.3):
        quiet(K.box("hinge", (0.18, 0.03, 0.06), iron(), loc=(0.06, -0.045, zz), bev=0, parent=L))
    quiet(K.box("door_pull", (0.04, 0.05, 0.2), iron(), loc=(lw - 0.1, -0.06, 1.0), bev=0, parent=L))
    if lamp:
        bulb(G, (x, y - 0.25, z + h + 0.35), arm=0.25)
    return G


def bulb(G, loc, arm=0.3, light=True, energy=40):
    """A bare bulb on a bent bracket out from a wall (towards -y), warm and lit."""
    x, y, z = loc
    bar(G, "bulb_arm", (x, y + arm, z + 0.12), (x, y, z + 0.12), 0.04, iron())
    pipe(G, "bulb_flex", (x, y, z + 0.12), (x, y, z + 0.05), 0.01, iron(), segs=4)
    quiet(K.cyl("bulb_cap", 0.04, 0.06, M(GUNMETAL), loc=(x, y, z + 0.03), segs=8, bev=0, parent=G))
    quiet(K.sphere("bulb", 0.07, glow(BULB, 14), loc=(x, y, z - 0.04), parent=G))
    if light:
        K.point("bulb_light", (x, y, z - 0.2), WARM, energy, 0.1).parent = G


# ================================================================== roofs and shells
def half_dome(P, name, R, L, loc=(0, 0, 0), yaw=0.0, seed=1, knee=0.5, front=None, back=None, floor=True, ribs=True,
              cols=7, row=1.75, inner=True):
    """A colony hut shell rebuilt plate by plate: a half cylinder of radius R along local y (-L/2 to L/2) springing
    from knee walls knee high, its plates mismatched in colour and radius between rusty ribs, the ends walled in
    planks. front and back describe the ends: {"door": (x, w, h, kind), "port": [(x, z)], "window": [(x, z)],
    "open": (x0, x1, z1)} - an opening with no door. Plank floor inside (top 0.05 above loc), collides as the shell, its
    knee walls, its ends round their openings and the floor."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    zs = 0.05 + knee              # springing line (the arc's centre)
    rows = max(2, round(L / row))
    a_pad = 0.025
    for r in range(rows):
        y0, y1 = -L / 2 + L * r / rows - 0.04, -L / 2 + L * (r + 1) / rows + 0.04
        for c in range(cols):
            a0, a1 = math.pi * c / cols - a_pad, math.pi * (c + 1) / cols + a_pad
            col = rnd.choice(PLATE_COLS)
            rr = R + 0.02 + rnd.uniform(0.0, 0.05) + (0.03 if (r + c) % 2 else 0.0)
            arc_plate(G, "shell_plate", rr, max(a0, 0.0), min(a1, math.pi), y0, y1, plate(col), t=0.05, n=2, zc=zs)
            if rnd.random() < 0.5:
                am = (a0 + a1) / 2
                bolt(G, ((rr + 0.01) * math.cos(am), y0 + 0.08, zs + (rr + 0.01) * math.sin(am)), out=(math.cos(am), 0, math.sin(am)))
    if ribs:
        for r in range(rows + 1):
            y = -L / 2 + L * r / rows
            y = max(-L / 2 + 0.05, min(L / 2 - 0.05, y))
            arc_rib(G, "rib", R + 0.12, 0.0, math.pi, y, 0.1, rib(), n=12, zc=zs)
            for sx in (-1, 1):
                bar(G, "rib_foot", (sx * (R + 0.12), y, 0.0), (sx * (R + 0.12), y, zs), 0.1, rib())
    if knee > 0.05:
        for sx in (-1, 1):
            plate_wall(G, f"{name}_knee", -L / 2, L / 2, 0.0, zs + 0.05, loc=(sx * R, 0, 0), yaw=90 * sx, seed=seed + 7 + sx,
                       frame=False, collide=False, corrugated=0.2)
            solid(G, "knee", (0.2, L, zs + 0.1), (sx * (R - 0.05), 0, (zs + 0.1) / 2))
    arc_solid(G, "shell", R - 0.02, 0.0, math.pi, -L / 2, L / 2, zc=zs, n=12)

    def end(spec, s):
        spec = spec or {}
        holes = []
        y = s * L / 2
        E = group(G, f"{name}_end", (0, y, 0), 0 if s < 0 else 180)
        if "door" in spec:
            dx, dw, dh, kind = spec["door"]
            holes.append((dx - dw / 2, dx + dw / 2, 0.0, 0.05 + dh))
        if "open" in spec:
            ox0, ox1, oz1 = spec["open"]
            holes.append((ox0, ox1, 0.0, oz1))
        for wx, wz in spec.get("window", []):
            holes.append((wx - 0.5, wx + 0.5, wz, wz + 0.8))
        for px, pz in spec.get("port", []):
            holes.append((px - 0.3, px + 0.3, pz - 0.3, pz + 0.3))

        def top(x):
            return zs + math.sqrt(max(R * R - x * x, 0.0))
        plank_wall(E, f"{name}_end_wall", -R + 0.02, R - 0.02, 0.0, zs + R, holes=holes, seed=seed + (11 if s < 0 else 13), top=top, t=0.15)
        if "door" in spec:
            dx, dw, dh, kind = spec["door"]
            door(E, dx, 0.05, w=dw, h=dh, kind=kind, seed=seed, y=-0.04)
        for wx, wz in spec.get("window", []):
            window(E, wx, wz, y=-0.04, seed=seed + int(wx * 10))
        for px, pz in spec.get("port", []):
            porthole(E, px, pz, 0.3, y=-0.05)
        if "open" in spec:
            ox0, ox1, oz1 = spec["open"]
            board(E, "open_head", (ox1 - ox0 + 0.3, 0.2, 0.2), ((ox0 + ox1) / 2, 0.0, oz1 + 0.1), timber(3))
            for xx in (ox0 - 0.08, ox1 + 0.08):
                board(E, "open_post", (0.16, 0.2, oz1), (xx, 0.0, oz1 / 2), timber(3))
        return E
    end(front, -1)
    end(back, 1)
    if floor:
        n = max(3, round(2 * R / 0.3))
        for k in range(n):
            x = -R + 0.05 + (2 * R - 0.1) * (k + 0.5) / n
            quiet(K.box("floor_board", ((2 * R - 0.1) / n - 0.01, L - 0.1, 0.05), timber(rnd.randrange(5)), loc=(x, 0, 0.025), bev=0, parent=G))
        solid(G, "floor", (2 * R, L, 0.05), (0, 0, 0.025))
    if inner:
        # the frame showing inside: purlins along the shell
        for a in (0.55, 1.2, 1.94, 2.59):
            bar(G, "purlin", ((R - 0.12) * math.cos(a), -L / 2 + 0.1, zs + (R - 0.12) * math.sin(a)),
                ((R - 0.12) * math.cos(a), L / 2 - 0.1, zs + (R - 0.12) * math.sin(a)), 0.08, timber(4))
    return G


def lean_to(P, name, w, d, hf, hb, loc=(0, 0, 0), yaw=0.0, seed=1, walls=("back", "left", "right"), wall_kind="plates",
            floor=False, posts=None, gutter=True, holes=None, back_posts=True):
    """A lean-to: a mono-pitch roof of odd corrugated sheets on rafters, from hf at the front (y = -d/2) to hb at the
    back, on timber posts, walled on the sides named in walls with plates or planks (holes per wall name). Its front is
    open. Collides as its roof, walls and posts."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    holes = holes or {}
    yf, yb = -d / 2, d / 2

    def roof(y):
        return hf + (hb - hf) * (y - yf) / (yb - yf)
    slope = math.degrees(math.atan2(hf - hb, d))
    # posts and beams
    n = max(1, math.ceil(w / 3.0))
    xs = posts if posts is not None else [-w / 2 + 0.08 + (w - 0.16) * k / n for k in range(n + 1)]
    for x in xs:
        board(G, "post", (0.14, 0.14, roof(yf + 0.1) - 0.05), (x, yf + 0.1, (roof(yf + 0.1) - 0.05) / 2), timber(3))
        solid(G, "post", (0.2, 0.2, roof(yf + 0.1)), (x, yf + 0.1, roof(yf + 0.1) / 2))
    for x in (-w / 2 + 0.08, w / 2 - 0.08) if back_posts else ():
        board(G, "post_b", (0.14, 0.14, roof(yb - 0.1) - 0.05), (x, yb - 0.1, (roof(yb - 0.1) - 0.05) / 2), timber(3))
    board(G, "beam_f", (w + 0.2, 0.14, 0.18), (0, yf + 0.1, roof(yf + 0.1) - 0.09), timber(2))
    board(G, "beam_b", (w + 0.2, 0.14, 0.18), (0, yb - 0.1, roof(yb - 0.1) - 0.09), timber(2))
    nr = max(2, round(w / 0.9))
    Lr = (d + 0.5) / math.cos(math.radians(slope))
    for k in range(nr + 1):
        x = -w / 2 + 0.1 + (w - 0.2) * k / nr
        board(G, "rafter", (0.08, Lr, 0.12), (x, 0.0, roof(0.0) + 0.04), timber(4), rot=(-slope, 0, 0))
    # sheets, overlapping, mismatched
    x = -w / 2 - 0.15
    while x < w / 2 + 0.1:
        sw = rnd.uniform(0.85, 1.15)
        col = rnd.choice(("zinc", "zinc", "rust", "sea", "brick", "steel"))
        cx = x + sw / 2
        sheet(G, "roof_sheet", sw, Lr + rnd.uniform(-0.1, 0.1), plate(col), (cx, rnd.uniform(-0.05, 0.05), roof(0.0) + 0.14 + rnd.uniform(0, 0.02)),
              rot=(-90 - slope, rnd.uniform(-0.8, 0.8), 0))
        x += sw - 0.08
    for k in range(rnd.randint(2, 4)):          # stones and a tyre holding the sheets down
        y = rnd.uniform(yf + 0.3, yb - 0.3)
        quiet(K.box("roof_stone", (0.3, 0.25, 0.16), K.mat("fieldstone", "#80827a", rough=0.9, grime=0.3, moss=0.4, cracks=0.3, noise_scale=4.0),
                    loc=(rnd.uniform(-w / 2 + 0.4, w / 2 - 0.4), y, roof(y) + 0.24), rot=(-slope, 0, rnd.uniform(0, 90)), bev=0.04, parent=G))
    solid(G, "roof", (w + 0.3, Lr, 0.12), (0, 0.0, roof(0.0) + 0.12), rot=(-slope, 0, 0))
    if gutter:
        pipe(G, "gutter", (-w / 2 - 0.1, yb + 0.25, roof(yb + 0.25) + 0.02), (w / 2 + 0.1, yb + 0.25, roof(yb + 0.25) + 0.02), 0.07, zinc())
    kind = plank_wall if wall_kind == "planks" else plate_wall
    for side in walls:
        if side == "back":
            kind(G, f"{name}_back", -w / 2, w / 2, 0.0, roof(yb), loc=(0, yb, 0), yaw=180, seed=seed + 3, holes=holes.get("back", ()))
        elif side in ("left", "right"):
            sx = -1 if side == "left" else 1

            def top(x, sx=sx):
                # the side wall's local x runs from the back to the front on the left, front to back on the right
                y = -x if sx < 0 else x
                return roof(y)
            lo, hi = min(hf, hb), max(hf, hb)
            if kind is plank_wall:
                plank_wall(G, f"{name}_{side}", -d / 2, d / 2, 0.0, hi, loc=(sx * w / 2, 0, 0), yaw=90 * sx, seed=seed + 5 + sx,
                           holes=holes.get(side, ()), top=top)
            else:
                # plates up to the low eave, planks cut to the slope above them
                plate_wall(G, f"{name}_{side}", -d / 2, d / 2, 0.0, lo, loc=(sx * w / 2, 0, 0), yaw=90 * sx, seed=seed + 5 + sx,
                           holes=holes.get(side, ()))
                if hi - lo > 0.1:
                    plank_wall(G, f"{name}_{side}_gable", -d / 2, d / 2, lo, hi, loc=(sx * w / 2, 0, 0), yaw=90 * sx, seed=seed + 8 + sx,
                               top=top, battens=False)
    if floor:
        quiet(K.box("deck", (w - 0.1, d - 0.1, 0.06), timber(1), loc=(0, 0, 0.03), bev=0, parent=G))
        solid(G, "deck", (w, d, 0.06), (0, 0, 0.03))
    return G


def awning(G, x, z, w, depth, y=0.0, slope=18.0, kind="stripes", cols=("bone", "brick"), stays=True, name="awning"):
    """An awning out from a wall (towards -y) over a counter or a door: striped canvas on a timber bar (or odd
    corrugated sheets), stayed with poles or rods. Too high to bump into, so it does not collide."""
    A = group(G, name, (x, y, z))
    L = depth / math.cos(math.radians(slope))
    zf = -depth * math.tan(math.radians(slope))
    board(A, "awning_bar", (w + 0.1, 0.1, 0.1), (0, 0.0, 0.0), timber(2))
    if kind == "stripes":
        n = max(3, round(w / 0.42))
        for k in range(n):
            quiet(K.box("awning_stripe", (w / n + 0.005, L, 0.02), canvas(FADED[cols[k % 2]] if cols[k % 2] in FADED else cols[k % 2]),
                        loc=(-w / 2 + w * (k + 0.5) / n, -depth / 2, zf / 2 + 0.03), rot=(slope, 0, 0), bev=0, parent=A))
        # the scalloped valance
        for k in range(n):
            quiet(K.box("awning_valance", (w / n - 0.02, 0.02, 0.2), canvas(FADED[cols[k % 2]] if cols[k % 2] in FADED else cols[k % 2]),
                        loc=(-w / 2 + w * (k + 0.5) / n, -depth - 0.01, zf - 0.07), bev=0, parent=A))
    else:
        xx = -w / 2
        rnd = random.Random(int(w * 100 + depth * 10))
        while xx < w / 2 - 0.1:
            sw = min(rnd.uniform(0.8, 1.1), w / 2 - xx + 0.05)
            sheet(A, "awning_sheet", sw, L + 0.15, plate(rnd.choice(("zinc", "rust", "sea"))), (xx + sw / 2, -depth / 2, zf / 2 + 0.05),
                  rot=(-90 + slope, 0, 0), pitch=0.25)
            xx += sw - 0.06
    board(A, "awning_front", (w + 0.1, 0.06, 0.08), (0, -depth, zf), timber(3))
    if stays:
        for sx in (-1, 1):
            pipe(A, "awning_stay", (sx * (w / 2 - 0.05), 0.0, -0.9), (sx * (w / 2 - 0.05), -depth, zf - 0.02), 0.02, iron(), segs=6)
    return A


# ================================================================== the trade counter
def trade_counter(P, name="counter", loc=(0, 0, 0), yaw=0.0, w=2.5, seed=1, light=True, goods=True, booth=True):
    """The settlers' trade counter (the farmer's stall at the crash site, as a kit piece): a booth of sea-teal panels
    in bone frames with a plank counter 1 m high across its front, a striped awning over it, a back shelf of jars and
    seed packets, a hanging scale and a bare bulb. The trader stands inside (local y 0 to 1.5); customers at the
    front (y < -0.35). Collides as the counter, the booth's walls and its roof."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    sea, bone = plate("sea"), M(BONE, wear=0.5, grime=0.35)
    D = 1.6
    H = 2.5
    if not booth:
        return _bare_counter(G, w, rnd, goods, light)
    # booth: back and sides of framed panels, a roof
    C.framed(G, "booth_back", (0, D - 0.06, H / 2), (w, 0.12, H), sea, bone, fw=0.1, screws=zinc())
    for sx in (-1, 1):
        C.framed(G, "booth_side", (sx * (w / 2 - 0.06), D / 2 - 0.15, H / 2), (0.12, D + 0.1, H), sea, bone, fw=0.1)
    C.framed(G, "booth_roof", (0, D / 2 - 0.1, H + 0.06), (w + 0.2, D + 0.4, 0.12), plate("bone"), bone, fw=0.1)
    # counter: odd boards in front, a thick top 1.0 m up
    x = -w / 2 + 0.12
    k = 0
    while x < w / 2 - 0.12:
        bw = min(rnd.uniform(0.18, 0.28), w / 2 - 0.12 - x)
        m = plate(rnd.choice(("sea", "brick"))) if k % 5 == 3 else timber(rnd.randrange(5))
        quiet(K.box("counter_board", (bw - 0.01, 0.03, 0.94), m, loc=(x + bw / 2, -0.25, 0.47), bev=0, parent=G))
        x += bw
        k += 1
    quiet(K.box("counter_top", (w - 0.1, 0.55, 0.06), timber(1), loc=(0, -0.05, 0.97), bev=0.01, parent=G))
    quiet(K.box("counter_back", (w - 0.24, 0.03, 0.9), timber(4), loc=(0, 0.2, 0.47), bev=0, parent=G))
    solid(G, "counter", (w - 0.1, 0.55, 1.0), (0, -0.05, 0.5))
    for sx in (-1, 1):
        solid(G, "booth_side", (0.15, D + 0.1, H), (sx * (w / 2 - 0.06), D / 2 - 0.15, H / 2))
    solid(G, "booth_back", (w, 0.15, H), (0, D - 0.06, H / 2))
    solid(G, "booth_roof", (w + 0.2, D + 0.4, 0.15), (0, D / 2 - 0.1, H + 0.06))
    # awning over the customers
    awning(G, 0, H - 0.05, w + 0.4, 1.2, y=-0.3, slope=20, stays=False, name="counter_awning")
    if goods:
        # back shelf of jars, sacks under the counter, seed packets on top, a scale hung from the roof
        board(G, "shelf", (w - 0.4, 0.28, 0.04), (0, D - 0.3, 1.5), timber(0))
        board(G, "shelf2", (w - 0.4, 0.28, 0.04), (0, D - 0.3, 0.8), timber(2))
        for j in range(5):
            jx = -w / 2 + 0.45 + j * (w - 0.9) / 4
            quiet(K.cyl("jar", 0.07, 0.22, M(rnd.choice(("#6fbf3a", "#c8423a", "#e0b530", "#4f9a93")), wear=0.1, scratches=0, rough=0.4),
                        loc=(jx, D - 0.3, 1.63), segs=8, bev=0, parent=G))
        for j in range(2):
            quiet(K.box("sack", (0.5, 0.3, 0.3), K.mat("sack", "#b49a6c", rough=0.95, grime=0.35, noise_scale=9.0),
                        loc=(-0.5 + j * 1.0, D - 0.3, 0.97), rot=(0, 0, rnd.uniform(-8, 8)), bev=0.08, parent=G))
        for j, (c, px) in enumerate((("#6fbf3a", -w / 2 + 0.35), ("#c8423a", -w / 2 + 0.62), ("#e0b530", w / 2 - 0.4))):
            quiet(K.box("packet", (0.22, 0.08, 0.3), M(c, wear=0.1, scratches=0), loc=(px, -0.15, 1.15), rot=(-8, 0, 0), bev=0.01, parent=G))
        pipe(G, "scale_chain", (w / 2 - 0.6, 0.4, H), (w / 2 - 0.6, 0.4, 1.75), 0.008, iron(), segs=4)
        quiet(K.cyl("scale_pan", 0.18, 0.04, zinc(), loc=(w / 2 - 0.6, 0.4, 1.72), segs=12, bev=0.01, parent=G))
        quiet(K.box("price_slate", (0.6, 0.03, 0.45), M("#33363a", wear=0.2, rough=0.9), loc=(-w / 2 + 0.5, -0.27, 1.5), rot=(0, 3, 0), bev=0.01, parent=G))
        # chalk pictograms on the slate: a leaf and a cog for "seeds and parts"
        quiet(K.sphere("chalk_leaf", 0.07, M("#e6e2d6", wear=0, grime=0, scratches=0), loc=(-w / 2 + 0.38, -0.29, 1.52), scale=(1.4, 0.2, 0.7), parent=G))
        quiet(K.torus("chalk_cog", 0.07, 0.015, M("#e6e2d6", wear=0, grime=0, scratches=0), loc=(-w / 2 + 0.64, -0.29, 1.52), rot=(90, 0, 0), segs=10, parent=G))
    if light:
        quiet(K.sphere("booth_bulb", 0.08, glow(BULB, 14), loc=(0, D / 2 - 0.2, H - 0.2), parent=G))
        K.point("booth_light", (0, D / 2 - 0.4, H - 0.4), WARM, 60, 0.1).parent = G
    return G


def _bare_counter(G, w, rnd, goods, light):
    """The counter alone, under a roof that is already there (a porch): boards in front, a thick top, the slate and
    a few goods, a bulb hung over it."""
    x = -w / 2 + 0.12
    k = 0
    while x < w / 2 - 0.12:
        bw = min(rnd.uniform(0.18, 0.28), w / 2 - 0.12 - x)
        m = plate(rnd.choice(("sea", "brick"))) if k % 5 == 3 else timber(rnd.randrange(5))
        quiet(K.box("counter_board", (bw - 0.01, 0.03, 0.94), m, loc=(x + bw / 2, -0.25, 0.47), bev=0, parent=G))
        x += bw
        k += 1
    quiet(K.box("counter_top", (w - 0.1, 0.55, 0.06), timber(1), loc=(0, -0.05, 0.97), bev=0.01, parent=G))
    for sx in (-1, 1):
        quiet(K.box("counter_end", (0.06, 0.5, 0.94), timber(3), loc=(sx * (w / 2 - 0.08), -0.05, 0.47), bev=0, parent=G))
    solid(G, "counter", (w - 0.1, 0.55, 1.0), (0, -0.05, 0.5))
    if goods:
        for j, (c, px) in enumerate((("#6fbf3a", -w / 2 + 0.35), ("#c8423a", -w / 2 + 0.62), ("#e0b530", w / 2 - 0.4))):
            quiet(K.box("packet", (0.22, 0.08, 0.3), M(c, wear=0.1, scratches=0), loc=(px, -0.15, 1.15), rot=(-8, 0, 0), bev=0.01, parent=G))
        for j in range(3):
            quiet(K.cyl("jar", 0.07, 0.22, M(rnd.choice(("#6fbf3a", "#c8423a", "#e0b530", "#4f9a93")), wear=0.1, scratches=0, rough=0.4),
                        loc=(-0.3 + j * 0.25, 0.05, 1.11), segs=8, bev=0, parent=G))
        quiet(K.box("price_slate", (0.6, 0.03, 0.45), M("#33363a", wear=0.2, rough=0.9), loc=(0.6, -0.27, 0.62), rot=(0, 3, 0), bev=0.01, parent=G))
    if light:
        pipe(G, "counter_flex", (0, -0.1, 2.3), (0, -0.1, 1.95), 0.01, iron(), segs=4)
        quiet(K.cyl("counter_shade", 0.2, 0.12, zinc(), loc=(0, -0.1, 1.9), segs=10, r2=0.05, bev=0, parent=G))
        quiet(K.sphere("counter_bulb", 0.07, glow(BULB, 14), loc=(0, -0.1, 1.8), parent=G))
        K.point("counter_light", (0, -0.4, 1.6), WARM, 50, 0.1).parent = G
    return G


def table(G, loc, length=2.4, yaw=0.0, benches=True, seed=1, name="table"):
    """A long trestle table of scrap boards, with plank benches along it."""
    rnd = random.Random(seed)
    T = group(G, name, loc, yaw)
    for k in range(4):
        quiet(K.box("table_board", (length, 0.2, 0.04), timber(rnd.randrange(5)), loc=(0, -0.3 + k * 0.2, 0.78), bev=0, parent=T))
    for sx in (-1, 1):
        quiet(K.box("trestle", (0.08, 0.7, 0.74), timber(3), loc=(sx * (length / 2 - 0.25), 0, 0.37), bev=0, parent=T))
    if benches:
        for sy in (-1, 1):
            quiet(K.box("bench_seat", (length - 0.2, 0.28, 0.04), timber(rnd.randrange(5)), loc=(0, sy * 0.68, 0.45), bev=0, parent=T))
            for sx in (-1, 1):
                quiet(K.box("bench_leg", (0.06, 0.24, 0.43), timber(3), loc=(sx * (length / 2 - 0.35), sy * 0.68, 0.215), bev=0, parent=T))
    quiet(K.cyl("mug", 0.04, 0.1, zinc(), loc=(rnd.uniform(-0.6, 0.6), 0.05, 0.85), segs=8, bev=0, parent=T))
    solid(T, "table", (length, 0.8, 0.8), (0, 0, 0.4))
    return T


def dome_hut(P, name, loc=(0, 0, 0), yaw=0.0, seed=1, L=4.5, kind="planks"):
    """A small half-dome hut (R 2.25 m) a player walks into by its front door: a bedroll, a crate and a bulb."""
    G = group(P, name, loc, yaw)
    half_dome(G, f"{name}_dome", 2.25, L, seed=seed, knee=0.6, front={"door": (0.0, 1.25, 2.5, kind)}, back={"port": [(0.0, 1.7)]}, row=1.5, cols=6)
    bedroll(G, (-1.0, 0.6, 0.05), yaw=0)
    crate(G, (1.2, 1.4, 0.05), yaw=8, shade=seed)
    bulb(G, (0.0, 0.2, 2.45), arm=0.0, energy=25)
    return G


# ================================================================== decks, stairs, scaffolds, ladders
def deck(P, name, x0, x1, y0, y1, z, loc=(0, 0, 0), yaw=0.0, seed=1, posts=True, rails=(), legs="timber"):
    """A plank deck, its top at z, on posts (timber or scaffold tube) with braces, and a timber railing on the edges
    named in rails ('x0', 'x1', 'y0', 'y1', or (edge, a, b) for part of one). Collides as its deck and rails."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    w, d = x1 - x0, y1 - y0
    n = max(2, round(w / 0.3))
    for k in range(n):
        quiet(K.box("deck_board", (w / n - 0.015, d, 0.05), timber(rnd.randrange(5)), loc=(x0 + w * (k + 0.5) / n, (y0 + y1) / 2, z - 0.025), bev=0, parent=G))
    for yy in (y0 + 0.08, y1 - 0.08):
        board(G, "joist", (w, 0.12, 0.2), ((x0 + x1) / 2, yy, z - 0.15), timber(3))
    solid(G, "deck", (w, d, 0.25), ((x0 + x1) / 2, (y0 + y1) / 2, z - 0.125))
    if posts and z > 0.4:
        nx, ny = max(1, math.ceil(w / 2.5)), max(1, math.ceil(d / 2.5))
        for i in range(nx + 1):
            for j in range(ny + 1):
                if 0 < i < nx and 0 < j < ny:
                    continue
                px, py = x0 + 0.1 + (w - 0.2) * i / nx, y0 + 0.1 + (d - 0.2) * j / ny
                if legs == "tube":
                    pipe(G, "leg", (px, py, 0.0), (px, py, z - 0.25), 0.05, zinc(), segs=8)
                else:
                    board(G, "leg", (0.16, 0.16, z - 0.25), (px, py, (z - 0.25) / 2), timber(3))
                solid(G, "leg", (0.2, 0.2, z - 0.25), (px, py, (z - 0.25) / 2))
        for i in range(nx):
            a = (x0 + 0.1 + (w - 0.2) * i / nx, y0 + 0.1, 0.2)
            b = (x0 + 0.1 + (w - 0.2) * (i + 1) / nx, y0 + 0.1, z - 0.3)
            bar(G, "brace", a, b, 0.06, timber(4) if legs != "tube" else zinc(), d=0.04)
    for e in rails:
        edge, a, b = (e, None, None) if isinstance(e, str) else e
        if edge in ("y0", "y1"):
            yy = y0 + 0.05 if edge == "y0" else y1 - 0.05
            rail(G, (a if a is not None else x0 + 0.05, yy, z), (b if b is not None else x1 - 0.05, yy, z))
        else:
            xx = x0 + 0.05 if edge == "x0" else x1 - 0.05
            rail(G, (xx, a if a is not None else y0 + 0.05, z), (xx, b if b is not None else y1 - 0.05, z))
    return G


def rail(G, a, b, h=1.05, posts=1.5, collide=True):
    """A timber railing from a to b (its foot): posts, a top rail and a mid rail; collides as a thin wall."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    n = max(1, round(L / posts))
    for k in range(n + 1):
        p = a + (b - a) * (k / n)
        board(G, "rail_post", (0.09, 0.09, h), (p.x, p.y, p.z + h / 2), timber(3))
    for f, wd in ((1.0, 0.1), (0.5, 0.07)):
        bar(G, "rail_bar", a + Vector((0, 0, h * f)), b + Vector((0, 0, h * f)), wd, timber(2), d=0.05)
    if collide:
        d = b - a
        yaw = math.degrees(math.atan2(d.y, d.x))
        flat = Vector((d.x, d.y, 0)).length
        pitch = math.degrees(math.atan2(d.z, flat))
        solid(G, "rail", (L, 0.1, h), tuple((a + b) / 2 + Vector((0, 0, h / 2))), rot=(0, -pitch, yaw))


def stair(P, name, a, b, w=1.25, rails=(True, True), seed=1):
    """A timber stair from its foot a to its top b (both on the walking surface, b higher): plank treads of at most
    0.25 m rise on two stringers, railings on the sides in rails (left, right going up). Walked as a ramp; keep it
    no steeper than 40 degrees."""
    G = group(P, name)
    rnd = random.Random(seed)
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    Lr, H = run.length, b.z - a.z
    n = max(1, math.ceil(H / 0.25 - 1e-6))
    dvec = run.normalized()
    side = Vector((-dvec.y, dvec.x, 0))
    yaw = math.degrees(math.atan2(dvec.y, dvec.x))
    tread = Lr / n
    for i in range(n):
        p = a + dvec * (tread * (i + 0.5)) + Vector((0, 0, H * (i + 1) / n))
        quiet(K.box("tread", (tread + 0.06, w - 0.1, 0.05), timber(rnd.randrange(5)), loc=tuple(p - Vector((0, 0, 0.025))), rot=(0, 0, yaw), bev=0, parent=G))
    for s in (-1, 1):
        o = side * s * (w / 2 - 0.04)
        bar(G, "stringer", a + o - Vector((0, 0, 0.12)), b + o - Vector((0, 0, 0.12)), 0.06, timber(3), d=0.25)
        if rails[0 if s > 0 else 1]:
            rail(G, a + o * 1.0 + side * s * 0.02, b + o + side * s * 0.02, h=1.0, posts=1.4)
    ramp(G, name, a + Vector((0, 0, min(0.12, H / n / 2))), b, w - 0.1, base=0.3)
    ramp(G, f"{name}_lip", b, b + dvec * 0.3, w - 0.1, base=0.3)
    return G


def ladder(G, a, h, yaw=0.0, w=0.5, mat=None, name="ladder"):
    """A wooden ladder leaning on something (decor: it is not climbed), its feet at a."""
    L = group(G, name, a, yaw)
    mat = mat or timber(4)
    lean = 0.18
    for s in (-1, 1):
        bar(L, "ladder_rail", (s * w / 2, 0, 0), (s * w / 2, lean * h, h), 0.06, mat, d=0.05)
    for k in range(1, int(h / 0.3)):
        t = k * 0.3 / h
        bar(L, "ladder_rung", (-w / 2, lean * h * t, h * t), (w / 2, lean * h * t, h * t), 0.035, mat)
    return L


def scaffold_tower(P, name, loc, size=4.5, h=10.0, yaw=0.0, seed=1, top=None):
    """A lookout of scaffold tube and planks: four tube legs with guard ledgers every metre and braces, a square stair
    climbing round the inside of the frame (flights of 1.25 m rise along each side, landings in the corners, a rail on
    the well side), a railed deck on top at h over the half of the frame the last flights do not climb under, and a
    lean-to roof. The way in is at the foot of the first flight, on the -x face by the -y corner. Each face collides as
    a screen (bar that doorway), so nobody falls out between the ledgers. top is called with the deck's group."""
    G = group(P, name, loc, yaw)
    s = size / 2
    zinc_m = zinc()
    for sx in (-1, 1):
        for sy in (-1, 1):
            pipe(G, "leg", (sx * s, sy * s, 0.0), (sx * s, sy * s, h + 1.1), 0.06, zinc_m, segs=8)
            quiet(K.box("leg_plate", (0.3, 0.3, 0.04), iron(), loc=(sx * s, sy * s, 0.02), bev=0, parent=G))
    faces = (((-s, -s), (s, -s)), ((s, -s), (s, s)), ((s, s), (-s, s)), ((-s, s), (-s, -s)))
    z = 1.0
    while z < h + 1.15:
        for k, (a, b) in enumerate(faces):
            if k == 3 and z < 2.6:                # the doorway
                continue
            pipe(G, "ledger", (a[0], a[1], z), (b[0], b[1], z), 0.04, zinc_m, segs=6)
        z += 1.0
    for k, (a, b) in enumerate(faces):
        for lv in range(int(h / 4) + 1):
            z0 = lv * 4.0 + (0.2 if k % 2 else 2.2)
            if z0 + 1.7 < h + 1.1 and not (k == 3 and z0 < 2.6):
                pipe(G, "brace", (a[0], a[1], z0), (b[0], b[1], z0 + 1.7), 0.035, rib(), segs=6)
    # screens: each face collides to the rail height over the deck, the -x face with a doorway at its foot
    for k, (a, b) in enumerate(faces):
        a3, b3 = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        mid = (a3 + b3) / 2
        yaw_f = math.degrees(math.atan2(b3.y - a3.y, b3.x - a3.x))
        if k == 3:
            # runs from (-s, s) to (-s, -s): the doorway is its last 1.3 m
            L = size
            solid(G, "screen", (L - 1.3, 0.1, h + 1.1), (-s, s - (L - 1.3) / 2, (h + 1.1) / 2), rot=(0, 0, yaw_f))
            solid(G, "screen_head", (1.3, 0.1, h + 1.1 - 2.6), (-s, -s + 0.65, 2.6 + (h + 1.1 - 2.6) / 2), rot=(0, 0, yaw_f))
        else:
            solid(G, "screen", (size, 0.1, h + 1.1), (mid.x, mid.y, (h + 1.1) / 2), rot=(0, 0, yaw_f))
    # wind sheets on the lower half of three faces
    rnd = random.Random(seed)
    for k, (a, b) in enumerate(faces[:3]):
        S = group(G, "tower_sheets", (a[0], a[1], 0), math.degrees(math.atan2(b[1] - a[1], b[0] - a[0])))
        xx = 0.1
        while xx < size - 0.2:
            sw = min(rnd.uniform(0.8, 1.1), size - 0.1 - xx)
            C.corrugated(S, "tower_sheet", xx, xx + sw + 0.03, 0.3, 2.0 + rnd.uniform(-0.2, 0.2), plate(rnd.choice(("zinc", "rust", "sea", "brick"))),
                         y=-0.08 - 0.01 * (int(xx * 3) % 2), pitch=0.25, depth=0.03)
            xx += sw
    # the stair round the inside
    lw = 1.0
    inner = s - 0.1
    cs = inner - lw / 2
    corners = [(-cs, -cs), (cs, -cs), (cs, cs), (-cs, cs)]
    rise = 1.25
    z = 0.0
    k = 0
    while z + rise <= h + 1e-6:
        c0, c1 = corners[k % 4], corners[(k + 1) % 4]
        d = Vector((c1[0] - c0[0], c1[1] - c0[1], 0)).normalized()
        a = Vector((c0[0], c0[1], z)) + d * (lw / 2)
        b = Vector((c1[0], c1[1], z + rise)) - d * (lw / 2)
        sdir = Vector((-d.y, d.x, 0))
        outward = Vector((a.x, a.y, 0)).dot(sdir) > 0
        stair(G, f"{name}_flight{k}", a, b, w=lw, rails=(not outward, outward), seed=seed + k)
        cx, cy = c1
        if z + rise < h - 1e-6:
            deck(G, f"{name}_landing{k}", cx - lw / 2, cx + lw / 2, cy - lw / 2, cy + lw / 2, z + rise, posts=False, seed=seed + k)
        z += rise
        k += 1
    # the top deck: the corner the last flight arrives at, and the half over the first two sides (the last two flights
    # climb under the other half, which stays open, railed off)
    e = s + 0.2
    D = deck(G, f"{name}_top", -inner + lw, e, -e, inner - lw, h, posts=False, seed=seed,
             rails=("x1", "y0", ("x0", -inner + lw, inner - lw), ("y1", -inner + lw, e - 0.05)))
    deck(G, f"{name}_top_corner", -e, -inner + lw, -e, -inner + lw, h, posts=False, seed=seed + 1, rails=("x0", "y0"))
    roof_h = h + 2.6
    lean_to(G, f"{name}_roof", size + 0.8, size + 0.8, roof_h, roof_h - 0.5, walls=(), seed=seed + 9, posts=[], gutter=False, back_posts=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            pipe(G, "roof_leg", (sx * s, sy * s, h + 1.1), (sx * s, sy * s, roof_h - (0.5 if sy > 0 else 0.0) - 0.15), 0.05, zinc_m, segs=8)
    if top:
        top(D)
    return G


# ================================================================== fences
def fence(P, name, a, b, kind="mixed", h=2.0, seed=1, gate=None, posts="timber"):
    """A fence of whatever came to hand from a to b: posts every 2.5 m or less (timber or rusty pipe), and bays of
    corrugated sheet, plates, planks, car doors or pallets ('mixed' picks bay by bay, 'light' the same without car
    doors). gate is a gap (t0, t1) in
    metres along it. Collides as a thin wall round the gap."""
    a3, b3 = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    L = (b3 - a3).length
    yaw = math.degrees(math.atan2(b3.y - a3.y, b3.x - a3.x))
    G = group(P, name, tuple(a3), yaw)
    rnd = random.Random(seed)
    n = max(1, math.ceil(L / 2.5))
    bw = L / n
    import props_salvage as PS  # noqa: PLC0415
    for i in range(n):
        x0, x1 = i * bw, (i + 1) * bw
        if gate and x0 >= gate[0] - 0.01 and x1 <= gate[1] + 0.01:
            continue
        if gate and x0 < gate[1] and x1 > gate[0]:
            if x0 < gate[0]:
                x1 = gate[0]
            else:
                x0 = gate[1]
        k = kind
        if kind == "mixed":
            k = rnd.choice(("sheet", "sheet", "plates", "planks", "doors", "pallet"))
        elif kind == "light":          # mixed, but no car doors (the dearest bays)
            k = rnd.choice(("sheet", "sheet", "plates", "planks", "pallet"))
        hh = h + rnd.uniform(-0.12, 0.1)
        if k == "sheet":
            xx = x0
            while xx < x1 - 0.05:
                sw = min(rnd.uniform(0.8, 1.1), x1 - xx)
                C.corrugated(G, "fence_sheet", xx, xx + sw + 0.04, 0.05, hh, plate(rnd.choice(("zinc", "zinc", "rust", "sea", "brick"))),
                             y=-0.02 - 0.015 * (int(xx * 3) % 2), pitch=0.25, depth=0.03, t=0.012)
                xx += sw
        elif k == "plates":
            plate_wall(G, "fence_plates", x0, x1, 0.05, hh, seed=seed + i, frame=False, collide=False)
        elif k == "planks":
            plank_wall(G, "fence_planks", x0, x1, 0.05, hh, seed=seed + i, collide=False)
        elif k == "doors":
            for row in range(2):
                for j in range(2):
                    dx = x0 + 0.1 + j * (bw - 0.2) / 2
                    if dx + 1.05 > x1:
                        continue
                    PS.car_door(G, "fence_door", (dx, -0.09, 0.05 + row * 0.95), (0, 0, rnd.uniform(-2, 2)),
                                rnd.choice((FADED["sea"], FADED["brick"], FADED["bone"], FADED["mustard"], FADED["steel"])), glass=rnd.choice((0, 0, 2)), frame=row == 1)
            plank_wall(G, "fence_backing", x0, x1, 0.05, min(hh, 1.9), seed=seed + i, collide=False, battens=False, painted=0.0)
        else:
            for row in range(int(hh / 1.0)):
                px = (x0 + x1) / 2
                pallet(G, (px, -0.06, 0.05 + row * 1.0), x1 - x0 - 0.1, rnd)
        streaks(G, rnd, rnd.uniform(x0 + 0.3, x1 - 0.3), hh - 0.1, y=-0.06)
    xs = [i * bw for i in range(n + 1)]
    for x in xs:
        if gate and gate[0] + 0.05 < x < gate[1] - 0.05:
            continue
        hp = h + 0.15
        if posts == "pipe":
            pipe(G, "fence_post", (x, 0.04, -0.1), (x, 0.04, hp), 0.05, rib(), segs=8)
        else:
            board(G, "fence_post", (0.14, 0.14, hp + 0.1), (x, 0.06, hp / 2 - 0.05), timber(3))
    if gate:
        for gx in gate:
            board(G, "gate_post", (0.2, 0.2, h + 0.5), (gx, 0.06, (h + 0.5) / 2), timber(3))
    for x0, x1, _, _ in C.holes_cut(0.0, L, 0.0, h, [(gate[0], gate[1], 0.0, h + 1.0)] if gate else []):
        solid(G, "fence", (x1 - x0, 0.2, h), ((x0 + x1) / 2, 0.04, h / 2))
    return G


def pallet(G, loc, w, rnd):
    """A wooden pallet stood on its edge in a fence: three stringers, slats across."""
    x, y, z = loc
    w = min(w, 1.25)
    for k in range(7):
        quiet(K.box("pallet_slat", (w, 0.025, 0.1), timber(rnd.randrange(5)), loc=(x, y, z + 0.08 + k * 0.135), bev=0, parent=G))
    for sx in (-1, 0, 1):
        quiet(K.box("pallet_block", (0.09, 0.09, 0.95), timber(3), loc=(x + sx * (w / 2 - 0.05), y + 0.06, z + 0.48), bev=0, parent=G))


# ================================================================== bolted-on gear
def fan_box(G, loc, yaw=0.0, n=2, name="fan_box"):
    """A box of salvaged extract fans bolted on a roof or wall (blowing towards local -y): bone case, guard rings,
    brick-red blades."""
    F = group(G, name, loc, yaw)
    w = 0.95 * n + 0.3
    C.framed(F, "fan_case", (0, 0.35, 0.55), (w, 0.7, 1.1), plate("bone"), M(GRAPHITE, metal=0.3, wear=0.3), fw=0.08)
    for k in range(n):
        fx = -w / 2 + 0.6 + k * 0.95
        quiet(K.cyl("fan_well", 0.4, 0.04, void(), loc=(fx, -0.01, 0.55), rot=(90, 0, 0), segs=16, bev=0, parent=F))
        quiet(K.torus("fan_ring", 0.42, 0.05, M(GRAPHITE, metal=0.3), loc=(fx, -0.04, 0.55), rot=(90, 0, 0), segs=16, parent=F))
        for bl in range(4):
            quiet(K.box("fan_blade", (0.7, 0.025, 0.15), plate("brick"), loc=(fx, -0.05, 0.55), rot=(0, 45 * bl + 10 * k, 0), bev=0, parent=F))
    return F


def wall_fan(G, x, z, r=0.45, y=0.0, col="mustard"):
    """A single fan in a square plate bolted over a hole in a wall (facing -y)."""
    quiet(K.box("fan_plate", (2 * r + 0.3, 0.04, 2 * r + 0.3), plate(col), loc=(x, y - 0.03, z), bev=0.01, parent=G))
    quiet(K.cyl("fan_well", r, 0.03, void(), loc=(x, y - 0.05, z), rot=(90, 0, 0), segs=16, bev=0, parent=G))
    quiet(K.torus("fan_guard", r, 0.03, zinc(), loc=(x, y - 0.08, z), rot=(90, 0, 0), segs=16, parent=G))
    for bl in range(3):
        quiet(K.box("fan_blade", (2 * r * 0.85, 0.02, 0.14), M(GUNMETAL, metal=0.5), loc=(x, y - 0.07, z), rot=(0, 60 * bl + 15, 0), bev=0, parent=G))
    for sx in (-1, 1):
        for sz in (-1, 1):
            bolt(G, (x + sx * (r + 0.08), y - 0.06, z + sz * (r + 0.08)))


def bulb_rack(G, loc, w=2.5, n=4, yaw=0.0, name="bulb_rack", light=True):
    """A graphite bar out from a wall on two arms (towards local -y), bare bulbs along it under tin caps."""
    R = group(G, name, loc, yaw)
    board(R, "rack", (w, 0.1, 0.1), (0, -0.55, 0.0), M(GRAPHITE, metal=0.3))
    for sx in (-1, 1):
        board(R, "rack_arm", (0.08, 0.55, 0.08), (sx * (w / 2 - 0.2), -0.28, 0.0), M(GRAPHITE, metal=0.3))
    for k in range(n):
        bx = -w / 2 + 0.3 + (w - 0.6) * k / max(1, n - 1)
        quiet(K.cyl("rack_cap", 0.09, 0.08, zinc(), loc=(bx, -0.55, -0.1), segs=8, r2=0.05, bev=0, parent=R))
        quiet(K.sphere("rack_bulb", 0.08, glow(BULB, 14), loc=(bx, -0.55, -0.2), parent=R))
    if light:
        K.point("rack_light", (0, -0.9, -0.4), WARM, 90, 0.2).parent = R
    return R


def lamp_pole(P, loc, h=3.5, yaw=0.0, name="lamp_pole", light=True):
    """A timber pole with an arm and a bare bulb under a tin shade, its cable stapled down the pole."""
    L = group(P, name, loc, yaw)
    board(L, "pole", (0.14, 0.14, h + 0.4), (0, 0, h / 2 - 0.2), timber(3))
    board(L, "pole_arm", (0.08, 0.9, 0.1), (0, -0.4, h - 0.1), timber(2))
    bar(L, "pole_brace", (0, -0.05, h - 0.6), (0, -0.55, h - 0.15), 0.05, timber(2))
    quiet(K.cyl("shade", 0.25, 0.14, zinc(), loc=(0, -0.8, h - 0.25), segs=12, r2=0.06, bev=0, parent=L))
    quiet(K.sphere("pole_bulb", 0.08, glow(BULB, 14), loc=(0, -0.8, h - 0.36), parent=L))
    pipe(L, "pole_cable", (0.08, 0, 0.2), (0.08, 0, h - 0.1), 0.012, iron(), segs=4)
    solid(L, "pole", (0.2, 0.2, h), (0, 0, h / 2))
    if light:
        K.point("pole_light", (0, -0.8, h - 0.6), WARM, 120, 0.2).parent = L
    return L


def string_lights(P, a, b, n=6, sag=0.35, name="string_lights", light=True):
    """A cable of bare bulbs strung between two points."""
    G = group(P, name)
    a, b = Vector(a), Vector(b)
    m = 8
    pts = [a + (b - a) * (i / m) - Vector((0, 0, sag * 4 * (i / m) * (1 - i / m))) for i in range(m + 1)]
    for i in range(m):
        pipe(G, "string_cable", pts[i], pts[i + 1], 0.01, iron(), segs=4)
    for k in range(n):
        t = (k + 0.5) / n
        p = a + (b - a) * t - Vector((0, 0, sag * 4 * t * (1 - t) + 0.08))
        quiet(K.sphere("string_bulb", 0.06, glow(BULB, 12), loc=tuple(p), parent=G))
    if light:
        mid = (a + b) / 2 - Vector((0, 0, sag + 0.3))
        K.point("string_light", tuple(mid), WARM, 60, 0.2).parent = G
    return G


def stove_pipe(P, loc, h, r=0.1, name="stove_pipe"):
    """A stove pipe going up through a roof, banded, under a conical rain cap."""
    G = group(P, name, loc)
    m = M(GRAPHITE, metal=0.4, wear=0.2, grime=0.6)
    quiet(K.cyl("stove_pipe", r, h, m, loc=(0, 0, h / 2), segs=10, bev=0, parent=G))
    for z in (h * 0.3, h * 0.7):
        quiet(K.cyl("pipe_band", r + 0.02, 0.06, zinc(), loc=(0, 0, z), segs=10, bev=0, parent=G))
    quiet(K.cyl("rain_cap", r * 2.2, 0.18, zinc(), loc=(0, 0, h + 0.2), segs=10, r2=0.02, bev=0, parent=G))
    for k in range(3):
        a = k * 2.09
        pipe(G, "cap_leg", (r * math.cos(a), r * math.sin(a), h), (r * 1.6 * math.cos(a), r * 1.6 * math.sin(a), h + 0.12), 0.012, zinc(), segs=4)
    return G


def pipe_run(P, pts, r=0.08, col="rust", name="pipe_run"):
    """Salvaged pipe bolted along a wall or roof, elbows at the bends, clamps every couple of metres."""
    mat = plate(col) if col != "copper" else K.mat("copper_old", "#8e6a4e", rough=0.45, metal=0.75, wear=0.2, grime=0.5)
    return C.pipe_run(P, pts, r=r, mat=mat, name=name, brackets=1.8)


def tank_on_stand(P, loc, r=1.0, h=1.8, stand=2.5, col="sea", name="tank_stand"):
    """A salvaged colony tank (a round capsule, banded) on a timber stand, a pipe down from it."""
    G = group(P, name, loc)
    for sx in (-1, 1):
        for sy in (-1, 1):
            board(G, "stand_leg", (0.18, 0.18, stand), (sx * r * 0.7, sy * r * 0.7, stand / 2), timber(3))
    for z in (0.6, stand - 0.1):
        for sx in (-1, 1):
            board(G, "stand_rail", (0.1, 2 * r * 0.7 + 0.2, 0.12), (sx * r * 0.7, 0, z), timber(2))
            board(G, "stand_rail", (2 * r * 0.7 + 0.2, 0.1, 0.12), (0, sx * r * 0.7, z), timber(2))
    body = plate(col)
    quiet(K.cyl("tank", r, h, body, loc=(0, 0, stand + h / 2), segs=20, bev=0.02, parent=G))
    quiet(K.sphere("tank_cap", r, body, loc=(0, 0, stand + h), scale=(1, 1, 0.35), parent=G))
    quiet(K.sphere("tank_foot", r, body, loc=(0, 0, stand), scale=(1, 1, 0.2), parent=G))
    for z in (stand + 0.25, stand + h - 0.25):
        quiet(K.cyl("tank_band", r + 0.03, 0.12, rib(), loc=(0, 0, z), segs=20, bev=0, parent=G))
    pipe(G, "tank_drop", (r * 0.6, 0, stand), (r * 0.6, 0, 0.5), 0.05, zinc(), segs=8)
    quiet(K.cyl("tank_tap", 0.06, 0.2, iron(), loc=(r * 0.6, -0.1, 0.5), rot=(90, 0, 0), segs=6, bev=0, parent=G))
    solid(G, "stand", (2 * r * 0.7 + 0.2, 2 * r * 0.7 + 0.2, stand + h), (0, 0, (stand + h) / 2))
    return G


# ================================================================== junk
def junk_pile(P, loc, r=2.0, h=1.2, seed=1, yaw=0.0, name="junk", collide=True):
    """A heap of mixed scrap: a mound of rust and dirt with plates, sheet, beams, tyres, drums and a car door in it.
    Collides as a low mound a player can scramble onto (no slope over 40 degrees)."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    import props_salvage as PS  # noqa: PLC0415
    from props_settler_a import lump  # noqa: PLC0415
    mound = M("#62483a", metal=0.25, wear=0.05, grime=0.35, rough=0.95, scratches=0)
    quiet(lump(G, "junk_mound", 1.0, (0, 0, 0), (r, r * 0.85, h), mound, rnd, jitter=0.22, flat=0.0, u=12, v=6))
    if r > 2.5:
        # big heaps: long plates and girders stuck in at angles, so the heap reads as scrap from afar
        for k in range(int(r * 1.5)):
            a = rnd.uniform(0, 2 * math.pi)
            d = rnd.uniform(0.2, 0.7)
            x, y = math.cos(a) * d * r, math.sin(a) * d * r * 0.85
            z = h * math.sqrt(max(0.0, 1 - d * d)) * 0.8
            if k % 2:
                quiet(K.box("junk_slab", (rnd.uniform(1.4, 2.6), rnd.uniform(0.8, 1.4), 0.04), plate(rnd.choice(PLATE_COLS)), loc=(x, y, z + 0.3),
                            rot=(rnd.uniform(-60, 60), rnd.uniform(-50, 50), math.degrees(a)), bev=0, parent=G))
            else:
                bar(G, "junk_girder", (x, y, z - 0.3), (x + rnd.uniform(-1.2, 1.2), y + rnd.uniform(-1.2, 1.2), z + rnd.uniform(1.0, 2.2)), 0.16, rib())
    for k in range(int(6 + r * 4 + r * r * 0.8)):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(0.1, 0.85)
        x, y = math.cos(a) * d * r, math.sin(a) * d * r * 0.85
        z = h * math.sqrt(max(0.0, 1 - d * d)) * 0.9
        kind = rnd.random()
        if kind < 0.35:
            quiet(K.box("junk_plate", (rnd.uniform(0.6, 1.4), rnd.uniform(0.5, 1.0), 0.03), plate(rnd.choice(PLATE_COLS)),
                        loc=(x, y, z), rot=(rnd.uniform(-35, 35), rnd.uniform(-35, 35), rnd.uniform(0, 180)), bev=0.005, parent=G))
        elif kind < 0.5:
            sheet(G, "junk_sheet", rnd.uniform(0.7, 1.0), rnd.uniform(1.0, 2.0), plate(rnd.choice(("zinc", "rust", "sea"))), (x, y, z),
                  rot=(-90 + rnd.uniform(-30, 30), rnd.uniform(-30, 30), rnd.uniform(0, 180)))
        elif kind < 0.65:
            bar(G, "junk_beam", (x - 0.8, y, z), (x + 0.8, y + rnd.uniform(-0.6, 0.6), z + rnd.uniform(-0.4, 0.4)), 0.12, rib())
        elif kind < 0.8:
            quiet(PS.tyre_ring(G, "junk_tyre", (x, y, z + 0.05), rot=(rnd.uniform(-40, 40), rnd.uniform(-40, 40), 0), segs=12))
        elif kind < 0.92:
            quiet(K.cyl("junk_drum", 0.3, 0.9, plate(rnd.choice(("sea", "brick", "rust", "steel"))), loc=(x, y, z + 0.2),
                        rot=(rnd.choice((0, 90)) + rnd.uniform(-15, 15), 0, rnd.uniform(0, 180)), segs=12, bev=0.02, parent=G))
        else:
            dg = PS.car_door(G, "junk_door", (x, y, z), (rnd.uniform(-60, -20), 0, rnd.uniform(0, 180)), rnd.choice((FADED["sea"], FADED["brick"], FADED["bone"])))
            quiet_all(dg)
    if collide:
        # a stepped mound: a low wide box and a smaller one on it, each lip under the step height, slopes as ramps
        for i, (f, zt) in enumerate(((0.95, min(0.3, h * 0.3)), (0.65, min(0.6, h * 0.55)), (0.4, h * 0.8))):
            solid(G, f"junk{i}", (2 * r * f, 2 * r * f * 0.85, zt), (0, 0, zt / 2))
    return G


def big_bale(G, loc, yaw=0.0, size=(2.4, 1.2, 1.2), rnd=None, name="bale"):
    """A big square bale of straw with two twine bands."""
    sx, sy, sz = size
    B = group(G, name, loc, yaw)
    dark = rnd is not None and rnd.random() < 0.35
    quiet(K.box("bale", size, straw(dark), loc=(0, 0, sz / 2), bev=0.08, parent=B))
    for t in (-0.25, 0.25):
        quiet(K.box("bale_twine", (0.03, sy + 0.012, sz + 0.012), M("#c8423a" if not dark else "#4f9a93", rough=0.7, wear=0, scratches=0),
                    loc=(t * sx, 0, sz / 2), bev=0, parent=B))
    return B


def bale_wall(P, a, b, rows=2, seed=1, name="bales", gaps=()):
    """A wall of big square bales from a to b, rows high, joints staggered, each bale a little out of line. Collides
    as one box. gaps: (t0, t1) stretches with no bales."""
    a3, b3 = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    L = (b3 - a3).length
    yaw = math.degrees(math.atan2(b3.y - a3.y, b3.x - a3.x))
    G = group(P, name, tuple(a3), yaw)
    rnd = random.Random(seed)
    for row in range(rows):
        x = -0.6 if row % 2 else 0.0
        while x < L - 0.1:
            ln = min(2.4, L - x) if row % 2 == 0 else min(2.4, L - x)
            x0 = max(0.0, x)
            x1 = min(L, x + 2.4)
            if x1 - x0 > 0.5 and not any(g[0] < (x0 + x1) / 2 < g[1] for g in gaps):
                big_bale(G, ((x0 + x1) / 2 + rnd.uniform(-0.04, 0.04), rnd.uniform(-0.06, 0.06), row * 1.2), yaw=rnd.uniform(-1.5, 1.5),
                         size=(x1 - x0 - 0.04, 1.2, 1.2), rnd=rnd)
            x += 2.4
    for x0, x1, _, _ in C.holes_cut(0.0, L, 0.0, rows * 1.2, [(g[0], g[1], 0.0, 9.0) for g in gaps]):
        solid(G, "bales", (x1 - x0, 1.2, rows * 1.2), ((x0 + x1) / 2, 0.0, rows * 0.6))
    return G


def scarecrow(P, loc, yaw=0.0, seed=1, name="scarecrow"):
    """A scarecrow of a coat on a cross of poles, a sack head, a tin-can hat (decor; it marks a dead end)."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    board(G, "sc_pole", (0.08, 0.08, 2.3), (0, 0, 1.05), timber(3))
    board(G, "sc_arms", (1.5, 0.07, 0.07), (0, 0, 1.6), timber(3))
    quiet(K.box("sc_coat", (0.55, 0.28, 0.75), canvas(rnd.choice((FADED["sea"], FADED["brick"], "#6f7a5a"))), loc=(0, 0, 1.3), bev=0.06, parent=G))
    for sx in (-1, 1):
        quiet(K.box("sc_sleeve", (0.5, 0.18, 0.16), canvas("#6f7a5a"), loc=(sx * 0.5, 0, 1.58), rot=(0, sx * 8, 0), bev=0.04, parent=G))
    quiet(K.sphere("sc_head", 0.2, K.mat("sack", "#b49a6c", rough=0.95, grime=0.35, noise_scale=9.0), loc=(0, 0, 1.92), scale=(1, 0.9, 1.1), parent=G))
    quiet(K.cyl("sc_hat", 0.12, 0.2, zinc(), loc=(0, 0, 2.16), segs=10, bev=0.01, parent=G))
    quiet(K.cyl("sc_brim", 0.22, 0.02, zinc(), loc=(0, 0, 2.07), segs=12, bev=0, parent=G))
    return G


# ================================================================== yard and room clutter (cheap: a camp has dozens)
def drum(G, loc, col="sea", r=0.3, h=0.88, yaw=0.0, lying=False, name="drum"):
    """A steel drum: two rolling hoops, a lid with its bung."""
    x, y, z = loc
    rot = (90, 0, yaw) if lying else (0, 0, yaw)
    zc = z + (r if lying else h / 2)
    D = group(G, name, (x, y, zc), 0)
    D.rotation_euler = [math.radians(a) for a in rot]
    quiet(K.cyl("drum", r, h, plate(col), loc=(0, 0, 0), segs=12, bev=0.015, parent=D))
    for t in (-0.3, 0.3):
        quiet(K.cyl("drum_hoop", r + 0.012, 0.03, plate(col), loc=(0, 0, t * h), segs=12, bev=0, parent=D))
    quiet(K.cyl("drum_bung", 0.04, 0.02, iron(), loc=(r * 0.5, 0, h / 2 + 0.005), segs=6, bev=0, parent=D))
    return D


def crate(G, loc, size=(0.7, 0.55, 0.5), yaw=0.0, shade=0, name="crate"):
    """A slatted wooden crate."""
    x, y, z = loc
    B = group(G, name, (x, y, z), yaw)
    sx, sy, sz = size
    quiet(K.box("crate", (sx - 0.04, sy - 0.04, sz), timber(shade), loc=(0, 0, sz / 2), bev=0.01, parent=B))
    for zz in (0.12, sz - 0.1):
        quiet(K.box("crate_slat", (sx, sy, 0.06), timber(shade + 2), loc=(0, 0, zz), bev=0, parent=B))
    return B


def sacks(G, loc, n=3, yaw=0.0, seed=1, name="sacks"):
    """Grain sacks slumped in a heap."""
    rnd = random.Random(seed)
    B = group(G, name, loc, yaw)
    for k in range(n):
        row = 1 if k >= 3 else 0
        quiet(K.box("sack", (0.62, 0.4, 0.28), K.mat("sack", "#b49a6c", rough=0.95, grime=0.35, noise_scale=9.0),
                    loc=((k % 3) * 0.55 - 0.55 + row * 0.27, rnd.uniform(-0.05, 0.05), 0.14 + row * 0.26),
                    rot=(0, rnd.uniform(-6, 6), rnd.uniform(-12, 12)), bev=0.1, parent=B))
    return B


def bedroll(G, loc, yaw=0.0, col="#6f7a5a", name="bedroll"):
    """A bedroll laid out on a pallet of boards, a blanket rolled at its head."""
    B = group(G, name, loc, yaw)
    for k in range(4):
        quiet(K.box("bed_board", (0.82, 0.2, 0.04), timber(k), loc=(0, -0.4 + k * 0.27, 0.06), bev=0, parent=B))
    for x in (-0.3, 0.3):
        quiet(K.box("bed_bearer", (0.08, 1.95, 0.06), timber(3), loc=(x, 0, 0.03), bev=0, parent=B))
    quiet(K.box("bed_mat", (0.72, 1.8, 0.08), canvas(col), loc=(0, 0.0, 0.12), bev=0.03, parent=B))
    quiet(K.cyl("bed_roll", 0.13, 0.7, canvas(FADED["brick"]), loc=(0, 0.78, 0.25), rot=(0, 90, 0), segs=8, bev=0.02, parent=B))
    return B


def log_pile(G, loc, n=7, yaw=0.0, seed=1, name="logs"):
    """Split firewood stacked between two stakes."""
    rnd = random.Random(seed)
    B = group(G, name, loc, yaw)
    bark = K.mat("bark", "#5b4632", rough=0.95, grime=0.3, noise_scale=8.0, cracks=0.5)
    k = 0
    for row in range(3):
        for c in range(3 - row):
            if k >= n:
                break
            quiet(K.cyl("log", 0.11, 0.9, bark, loc=(-0.24 + c * 0.24 + row * 0.12, rnd.uniform(-0.04, 0.04), 0.11 + row * 0.19),
                        rot=(90, 0, rnd.uniform(-4, 4)), segs=7, bev=0, parent=B))
            k += 1
    for sx in (-1, 1):
        board(B, "stake", (0.06, 0.06, 0.8), (sx * 0.5, 0, 0.35), timber(3))
    return B


def tyres(G, loc, n=3, seed=1, name="tyres"):
    """Old tyres stacked and one leaning on them."""
    import props_salvage as PS  # noqa: PLC0415
    rnd = random.Random(seed)
    B = group(G, name, loc)
    for k in range(n - 1):
        quiet(PS.tyre_ring(B, "tyre", (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), 0.1 + k * 0.2), rot=(0, 0, rnd.uniform(0, 90)), segs=12))
    quiet(PS.tyre_ring(B, "tyre", (0.62, 0.1, 0.32), rot=(80, 0, 20), segs=12))
    return B


def fire_ring(G, loc, r=0.6, seed=1, name="fire_ring"):
    """A ring of field stones round charred logs in their ash (cold)."""
    from props_settler_a import lump  # noqa: PLC0415
    rnd = random.Random(seed)
    B = group(G, name, loc)
    stone = K.mat("fieldstone", "#80827a", rough=0.9, grime=0.3, moss=0.4, cracks=0.3, noise_scale=4.0)
    n = 9
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.1, 0.1)
        quiet(lump(B, "ring_stone", 0.17, (r * math.cos(a), r * math.sin(a), 0.05), (1.2, 1.0, 0.75), stone, rnd, u=6, v=4, flat=0.3))
    ash = K.disc("ash", r * 0.85, K.mat("ash", "#4a4744", rough=1.0, ao=0), (0, 0), z=0.02, segs=12, wobble=0.15, seed=seed)
    ash.parent = B
    quiet(ash)
    char = K.mat("charred", "#2a2522", rough=0.95, ao=0)
    for k in range(3):
        a = k * 2.1 + 0.4
        quiet(K.cyl("char_log", 0.07, 0.75, char, loc=(0.1 * math.cos(a), 0.1 * math.sin(a), 0.1), rot=(70, 0, math.degrees(a)), segs=6, bev=0, parent=B))
    return B


def tripod(G, loc, name="tripod"):
    """Three lashed poles over a fire, a pot hung on a chain."""
    B = group(G, name, loc)
    top = (0, 0, 1.55)
    for k in range(3):
        a = k * 2.094 + 0.3
        pipe(B, "tripod_pole", (0.75 * math.cos(a), 0.75 * math.sin(a), 0.0), top, 0.03, timber(3), segs=6)
    pipe(B, "tripod_chain", top, (0, 0, 0.85), 0.01, iron(), segs=4)
    quiet(K.cyl("pot", 0.18, 0.24, iron(), loc=(0, 0, 0.72), segs=10, r2=0.15, bev=0.01, parent=B))
    return B


def stove(G, loc, yaw=0.0, pipe_h=2.0, name="stove"):
    """A box stove of riveted plate on legs, its pipe going up."""
    B = group(G, name, loc, yaw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            quiet(K.box("stove_leg", (0.06, 0.06, 0.2), iron(), loc=(sx * 0.25, sy * 0.2, 0.1), bev=0, parent=B))
    quiet(K.box("stove", (0.65, 0.55, 0.6), M("#33302c", metal=0.5, wear=0.3, grime=0.5), loc=(0, 0, 0.5), bev=0.02, parent=B))
    quiet(K.box("stove_door", (0.3, 0.03, 0.25), iron(), loc=(0, -0.29, 0.48), bev=0, parent=B))
    quiet(K.box("stove_glow", (0.2, 0.01, 0.06), glow("#ffb347", 3), loc=(0, -0.31, 0.4), bev=0, parent=B))
    quiet(K.cyl("stove_pipe", 0.08, pipe_h, M("#33302c", metal=0.5, wear=0.2, grime=0.6), loc=(0.12, 0.1, 0.8 + pipe_h / 2), segs=8, bev=0, parent=B))
    solid(B, "stove", (0.7, 0.6, 0.8), (0, 0, 0.4))
    return B


def crops(P, x0, x1, y0, y1, rnd, rows=4, kind="leafy", name="crops", fence_h=0.9, gate=None):
    """A fenced vegetable patch: raised soil rows across y, clumps of leaves (or staked beans) along them, a low
    rail fence of scrap wood round it with a gap for the gate (gate: its x0, x1 on the y0 side). The fence collides."""
    from props_settler_a import lump  # noqa: PLC0415
    G = group(P, name)
    soil = K.mat("soil", "#5a3f2a", rough=1.0, noise_scale=3.0)
    leaf = [M(c, wear=0, grime=0, scratches=0, rough=0.7) for c in ("#5fae32", "#6fbf3a", "#4f9a2a")]
    for k in range(rows):
        y = y0 + 0.7 + (y1 - y0 - 1.4) * k / max(1, rows - 1)
        quiet(K.box("soil_row", (x1 - x0 - 0.8, 0.55, 0.16), soil, loc=((x0 + x1) / 2, y, 0.08), bev=0.06, parent=G))
        n = int((x1 - x0 - 1.0) / 0.6)
        for j in range(n):
            x = x0 + 0.7 + j * (x1 - x0 - 1.4) / max(1, n - 1)
            if kind == "beans" and j % 2 == 0:
                pipe(G, "bean_stake", (x, y, 0.1), (x + rnd.uniform(-0.05, 0.05), y, 1.4), 0.015, timber(3), segs=4)
            quiet(lump(G, "crop", 0.2, (x, y, 0.2), (1.0, 1.0, 0.8 if kind != "beans" else 1.6), leaf[rnd.randrange(3)], rnd, jitter=0.25, u=6, v=4))
    gx = gate or ((x0 + x1) / 2 - 0.6, (x0 + x1) / 2 + 0.6)
    for (a, b) in (((x0, y1), (x1, y1)), ((x0, y0), (x0, y1)), ((x1, y0), (x1, y1)), ((x0, y0), (gx[0], y0)), ((gx[1], y0), (x1, y0))):
        L = math.dist(a, b)
        yaw = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
        n = max(1, math.ceil(L / 1.5))
        for k in range(n + 1):
            p = (a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n)
            board(G, "crop_post", (0.1, 0.1, fence_h + 0.1), (p[0], p[1], (fence_h + 0.1) / 2 - 0.05), timber(3))
        for zz in (0.45, fence_h - 0.05):
            board(G, "crop_rail", (L, 0.05, 0.09), ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, zz), timber(rnd.randrange(5)), rot=(0, 0, yaw))
        solid(G, "crop_fence", (L, 0.15, fence_h), ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, fence_h / 2), rot=(0, 0, yaw))
    return G


# ================================================================== colony salvage: capsules, vaults, cranes
def dome_cap(G, name, r, hz, mat, loc=(0, 0, 0), segs=16, rings=4):
    """The top half of an ellipsoid (radius r, hz high), its rim at loc: a capsule's or tank's domed end."""
    bm = bmesh.new()
    rows = []
    for j in range(rings):
        a = (math.pi / 2) * j / rings
        rows.append([bm.verts.new((r * math.cos(a) * math.cos(2 * math.pi * i / segs), r * math.cos(a) * math.sin(2 * math.pi * i / segs),
                                   hz * math.sin(a))) for i in range(segs)])
    apex = bm.verts.new((0, 0, hz))
    for j in range(rings - 1):
        for i in range(segs):
            k = (i + 1) % segs
            bm.faces.new((rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i]))
    for i in range(segs):
        bm.faces.new((rows[-1][i], rows[-1][(i + 1) % segs], apex))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=True)
    ob.parent = G
    ob.location = loc
    return quiet(ob)


def capsule(P, name, loc=(0, 0, 0), yaw=0.0, r=2.5, h=3.4, state="whole", seed=1, legs=0.7):
    """A colony drop capsule standing on its landing legs: a drum of bone plates between steel-blue bands under a
    domed cap, a hatch on its front (-y). state 'whole' keeps its skin; 'stripped' has had most plates taken (the
    settlers' walls are made of them), showing its rusty ribs and hoops; 'hut' sits sunk on the ground, re-skinned in
    mismatched plates, with a plank door (1.25 x 2.5 m) at the front, a floor, a bulb and a stove pipe: walk-in.
    Collides as a solid drum (the hut as its shell round the door, its floor and roof)."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    z0 = 0.0 if state == "hut" else legs
    cols = 12
    rows = 3
    hz = r * 0.62
    # the shell: arc plates round local y, turned upright (local y -> z, angles from +x towards -y)
    Sh = group(G, f"{name}_shell", (0, 0, z0))
    Sh.rotation_euler = (math.radians(90), 0, 0)
    band = plate("steel")
    for rw in range(rows):
        y0, y1 = h * rw / rows - 0.02, h * (rw + 1) / rows + 0.02
        for c in range(cols):
            a0, a1 = 2 * math.pi * c / cols - 0.015, 2 * math.pi * (c + 1) / cols + 0.015
            am = (a0 + a1) / 2
            if state == "hut" and c == 2 and y0 < 2.6:
                continue                                    # the doorway (column 2 spans 60..90 degrees: see the door)
            if state == "stripped" and rnd.random() < 0.7:
                continue
            col = rnd.choice(PLATE_COLS) if state == "hut" else ("bone" if rnd.random() < 0.85 else rnd.choice(("bone", "steel", "zinc")))
            arc_plate(Sh, "cap_plate", r + rnd.uniform(0.0, 0.03), a0, a1, y0, y1, plate(col), t=0.05, n=2)
            if rnd.random() < 0.3:
                bolt(Sh, ((r + 0.01) * math.cos(am), y0 + 0.1, (r + 0.01) * math.sin(am)), out=(math.cos(am), 0, math.sin(am)))
    for zz in (0.05, h * 0.5, h - 0.05):
        quiet(K.torus("cap_band", r + 0.04, 0.06, band if state != "stripped" else rib(), loc=(0, 0, z0 + zz), segs=24, parent=G))
    if state != "whole":
        for c in range(cols):
            if state == "hut" and c in (2, 3):
                continue
            a = -2 * math.pi * c / cols
            bar(G, "cap_rib", ((r - 0.08) * math.cos(a), (r - 0.08) * math.sin(a), z0), ((r - 0.08) * math.cos(a), (r - 0.08) * math.sin(a), z0 + h), 0.1, rib())
    if state == "stripped":
        for k in range(6):
            Rb = group(G, "cap_ribs", (0, 0, z0 + h))
            Rb.rotation_euler = (math.radians(90), 0, math.pi * k / 6)
            pts = [(r * math.cos(t), hz * math.sin(t)) for t in (math.pi * i / 10 for i in range(11))]
            for i in range(10):
                bar(Rb, "cap_arc", (pts[i][0], pts[i][1], 0), (pts[i + 1][0], pts[i + 1][1], 0), 0.1, rib())
        for t in range(3):
            if rnd.random() < 0.6:
                arc = rnd.uniform(0, 2 * math.pi)
                quiet(K.box("cap_scrap", (1.2, 0.9, 0.04), plate("bone"), loc=(r * 0.5 * math.cos(arc), r * 0.5 * math.sin(arc), z0 + h + hz * 0.75),
                            rot=(rnd.uniform(-30, 30), rnd.uniform(-30, 30), math.degrees(arc)), bev=0, parent=G))
    else:
        dome_cap(G, "cap_dome", r + 0.02, hz, plate("bone") if state == "whole" else plate(rnd.choice(("sea", "bone", "brick"))), loc=(0, 0, z0 + h))
        quiet(K.cyl("cap_vent", 0.35, 0.3, band, loc=(0, 0, z0 + h + hz - 0.05), segs=12, bev=0.02, parent=G))
    if z0 > 0:
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            ca, sa = math.cos(a), math.sin(a)
            bar(G, "cap_leg", (r * 0.85 * ca, r * 0.85 * sa, z0 + 0.6), ((r + 0.7) * ca, (r + 0.7) * sa, 0.08), 0.16, plate("steel") if state == "whole" else rib())
            quiet(K.cyl("cap_foot", 0.35, 0.08, iron(), loc=((r + 0.7) * ca, (r + 0.7) * sa, 0.04), segs=10, bev=0, parent=G))
        quiet(K.cyl("cap_floor", r - 0.05, 0.12, iron(), loc=(0, 0, z0 + 0.06), segs=16, bev=0, parent=G))
    if state == "hut":
        # the doorway spans 60..90 degrees off +x towards -y; turn the door to face out of its middle (75 degrees)
        D = group(G, f"{name}_door", (r * 0.966 * math.cos(math.radians(-75)), r * 0.966 * math.sin(math.radians(-75)), 0), 15)
        door(D, 0.0, 0.05, w=1.25, h=2.5, kind="planks", seed=seed)
        quiet(K.cyl("cap_boards", r - 0.06, 0.05, timber(1), loc=(0, 0, 0.025), segs=16, bev=0, parent=G))
        for k in range(-3, 4):
            quiet(K.box("floor_seam", (0.02, 2 * math.sqrt(max(0.0, (r - 0.1) ** 2 - (k * 0.6) ** 2)), 0.004), timber(3), loc=(k * 0.6, 0, 0.051), bev=0, parent=G))
        for rot in (0, 45):
            solid(G, "floor", (2 * r * 0.84, 2 * r * 0.84, 0.05), (0, 0.0, 0.025), rot=(0, 0, rot))
        arc_solid(Sh, "cap_wall", r, math.radians(90), math.radians(420), 0.0, h, n=11)
        arc_solid(Sh, "cap_wall_head", r, math.radians(60), math.radians(90), 2.6, h, n=1)
        solid(G, "cap_roof", (2 * r, 2 * r, 0.3), (0, 0, h + 0.15))
        bulb(G, (0.0, 0.3, h - 0.15), arm=0.0, energy=30)
        stove_pipe(G, (-r * 0.45, r * 0.45, h + hz * 0.55), 1.2)
    else:
        Hd = group(G, f"{name}_hatch", (0, -r - 0.03, z0))
        C.framed(Hd, "cap_hatch", (0, 0, 1.25), (1.2, 0.08, 2.0), plate("steel"), M(GRAPHITE, metal=0.3, wear=0.3), fw=0.08)
        quiet(*[c for c in Hd.children])
        porthole(Hd, 0.0, 1.75, 0.18, y=-0.05)
        top = z0 + h + hz * 0.6
        solid(G, "capsule", (2 * r * 0.84, 2 * r * 0.84, top), (0, 0, top / 2))
        solid(G, "capsule_b", (2 * r * 0.84, 2 * r * 0.84, top), (0, 0, top / 2), rot=(0, 0, 45))
    return G


def lattice(G, name, a, b, w, mat=None, bays=None):
    """A square lattice girder from a to b (its centre line), w across: four chords and zigzag lacing."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    ax = (b - a).normalized()
    up = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(up).normalized()
    v = ax.cross(u).normalized()
    mat = mat or rib()
    hw = w / 2
    corners = [u * hw + v * hw, -u * hw + v * hw, -u * hw - v * hw, u * hw - v * hw]
    for c in corners:
        bar(G, f"{name}_chord", a + c, b + c, 0.1, mat)
    n = bays or max(1, round(L / w))
    for k in range(n):
        p0, p1 = a + ax * (L * k / n), a + ax * (L * (k + 1) / n)
        for i in range(4):
            c0, c1 = corners[i], corners[(i + 1) % 4]
            bar(G, f"{name}_lace", p0 + (c0 if k % 2 == 0 else c1), p1 + (c1 if k % 2 == 0 else c0), 0.05, mat)
    return G


def derrick(P, name, loc, yaw=0.0, h=12.0, jib=9.0, seed=1):
    """A scrapyard derrick: a lattice mast on a plate cab, a jib out towards -y and its electromagnet hung on a cable
    over the heaps, a back-stay to a counterweight of drums. Collides as its cab and mast."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    Cb = group(G, "derrick_cab")
    C.framed(Cb, "derrick_cab", (0, 0.6, 1.3), (2.6, 2.4, 2.6), plate("mustard"), M(GRAPHITE, metal=0.3, wear=0.3), fw=0.1)
    quiet(*[c for c in Cb.children])
    quiet(K.box("cab_window", (1.6, 0.04, 0.8), glass(), loc=(0, -0.62, 1.8), bev=0, parent=G))
    solid(G, "derrick_cab", (2.6, 2.4, 2.6), (0, 0.6, 1.3))
    lattice(G, "mast", (0, 0.6, 2.6), (0, 0.6, h), 1.0)
    solid(G, "mast", (1.1, 1.1, h - 2.6), (0, 0.6, (h + 2.6) / 2))
    tip = Vector((0, 0.6 - jib, h - 1.5))
    lattice(G, "jib", (0, 0.6, h - 1.0), tuple(tip), 0.7)
    bar(G, "jib_stay", (0, 0.6, h + 1.2), tuple(tip + Vector((0, 0, 0.3))), 0.05, iron())
    bar(G, "back_stay", (0, 0.6, h + 1.2), (0, 3.4, 1.0), 0.05, iron())
    pipe(G, "mast_head", (0, 0.6, h), (0, 0.6, h + 1.3), 0.08, rib())
    for k in range(4):
        quiet(K.cyl("counterweight", 0.3, 0.88, plate(rnd.choice(("sea", "brick", "rust"))), loc=(-0.35 + 0.7 * (k % 2), 3.1 + 0.65 * (k // 2), 0.44),
                    segs=12, bev=0.02, parent=G))
    solid(G, "counterweight", (1.4, 1.4, 0.9), (0, 3.4, 0.45))
    drop = h - 5.0
    pipe(G, "magnet_cable", tuple(tip), tuple(tip - Vector((0, 0, drop))), 0.02, iron(), segs=4)
    quiet(K.cyl("magnet", 0.8, 0.45, M(GRAPHITE, metal=0.5, wear=0.3, grime=0.5), loc=tuple(tip - Vector((0, 0, drop + 0.2))), segs=16, bev=0.03, parent=G))
    for k in range(3):
        quiet(K.box("magnet_scrap", (rnd.uniform(0.6, 1.0), 0.5, 0.04), plate(rnd.choice(PLATE_COLS)),
                    loc=tuple(tip - Vector((rnd.uniform(-0.3, 0.3), 0, drop + 0.5 + 0.06 * k))),
                    rot=(rnd.uniform(-20, 20), 0, rnd.uniform(0, 180)), bev=0, parent=G))
    bulb_rack(G, (0, 0.2, h + 0.3), w=1.0, n=2)
    return G


def gantry(P, name, x, y0, y1, h=6.5, hook_at=0.4):
    """A gantry crane of salvage girders across an aisle: an A-frame of rusty tube at each end (y0 and y1), a lattice
    beam between them at h and a trolley with its hook block hung hook_at along it. Collides as its legs."""
    G = group(P, name, (x, 0, 0))
    m = rib()
    for y in (y0, y1):
        for sx in (-1, 1):
            pipe(G, "gantry_leg", (sx * 1.4, y, 0.0), (0, y, h), 0.12, m, segs=8)
            quiet(K.box("gantry_foot", (0.5, 0.5, 0.06), iron(), loc=(sx * 1.4, y, 0.03), bev=0, parent=G))
            solid(G, "gantry_leg", (0.3, 0.3, math.hypot(1.4, h)), (sx * 0.7, y, h / 2), rot=(0, -sx * math.degrees(math.atan2(1.4, h)), 0))
        bar(G, "gantry_tie", (-1.1, y, 1.2), (1.1, y, 1.2), 0.1, m)
    lattice(G, "gantry_beam", (0, y0, h + 0.4), (0, y1, h + 0.4), 0.8, mat=plate("steel"))
    ty = y0 + (y1 - y0) * hook_at
    quiet(K.box("trolley", (1.2, 0.9, 0.5), plate("mustard"), loc=(0, ty, h - 0.2), bev=0.02, parent=G))
    pipe(G, "hook_chain", (0, ty, h - 0.45), (0, ty, 2.7), 0.02, iron(), segs=4)
    quiet(K.box("hook_block", (0.3, 0.2, 0.45), plate("mustard"), loc=(0, ty, 2.5), bev=0.02, parent=G))
    quiet(K.torus("hook", 0.15, 0.04, iron(), loc=(0, ty, 2.15), rot=(90, 0, 0), segs=12, arc=270, parent=G))
    return G


def vault(P, name, w, L, spring, rise, loc=(0, 0, 0), seed=1, rib_every=5.0, cols=8):
    """A barrel vault of mismatched plates on rusty arched ribs: span w across x, length L along y, springing at
    spring, its crown rise above that (a segmental arch). The ends are left open for walls. Collides as its shell.
    Returns the group, the arch radius and its centre's height."""
    G = group(P, name, loc)
    rnd = random.Random(seed)
    hw = w / 2
    R = (hw * hw + rise * rise) / (2 * rise)
    zc = spring + rise - R
    a0 = math.atan2(spring - zc, hw)
    a1 = math.pi - a0
    rows = max(2, round(L / rib_every))
    for rw in range(rows):
        y0, y1 = -L / 2 + L * rw / rows - 0.04, -L / 2 + L * (rw + 1) / rows + 0.04
        for c in range(cols):
            p0, p1 = a0 + (a1 - a0) * c / cols - 0.006, a0 + (a1 - a0) * (c + 1) / cols + 0.006
            col = rnd.choice(("zinc", "zinc", "steel", "sea", "bone", "rust", "brick", "zinc"))
            arc_plate(G, "vault_plate", R + 0.03 + 0.04 * ((rw + c) % 2), p0, p1, y0, y1, plate(col), t=0.05, n=2, zc=zc)
    for rw in range(rows + 1):
        y = max(-L / 2 + 0.15, min(L / 2 - 0.15, -L / 2 + L * rw / rows))
        arc_rib(G, "vault_rib", R - 0.15, a0, a1, y, 0.25, rib(), n=10, zc=zc, d=0.4)
    arc_solid(G, "vault", R, a0, a1, -L / 2, L / 2, zc=zc, n=10)
    return G, R, zc


def clad_wall(P, name, x0, x1, z0, z1, loc=(0, 0, 0), yaw=0.0, holes=(), seed=1, tier=3.4, pitch=0.5, patches=0.15, t=0.25):
    """A big wall of odd corrugated sheets for a shed too large to plate by hand: sheets of mixed colours a metre or
    so wide in tiers about tier high, overlapping a little, a painted plate bolted over here and there (patches: the
    share of sheets that get one), steel girts showing inside. Cheap for its size. Collides as boxes round the holes."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    holes = list(holes)
    ntier = max(1, round((z1 - z0) / tier))
    for ti in range(ntier):
        za = z0 + (z1 - z0) * ti / ntier - (0.08 if ti else 0.0)
        zb = z0 + (z1 - z0) * (ti + 1) / ntier
        x = x0 - (rnd.uniform(0.2, 0.6) if ti % 2 else 0.0)
        k = 0
        while x < x1 - 0.05:
            w = rnd.uniform(0.9, 1.3)
            if x1 - (x + w) < 0.4:
                w = x1 - x
            col = rnd.choice(("zinc", "zinc", "zinc", "steel", "sea", "rust", "brick", "rust"))
            off = -0.01 - 0.02 * ((k + ti) % 2)
            for r in C.rect_minus(max(x, x0), min(x + w + 0.05, x1), za, zb, holes):
                if r[1] - r[0] < 0.05 or r[3] - r[2] < 0.05:
                    continue
                C.corrugated(G, "clad_sheet", r[0], r[1], r[2], r[3], plate(col), y=off, pitch=pitch, depth=0.04, t=0.012)
                if rnd.random() < patches and r[1] - r[0] > 0.7 and r[3] - r[2] > 1.0:
                    pw, ph = rnd.uniform(0.6, min(1.1, r[1] - r[0])), rnd.uniform(0.6, min(1.2, r[3] - r[2]))
                    cx, cz = rnd.uniform(r[0] + pw / 2, r[1] - pw / 2), rnd.uniform(r[2] + ph / 2, r[3] - ph / 2)
                    quiet(K.box("clad_patch", (pw, 0.02, ph), plate(rnd.choice(("bone", "mustard", "sea", "brick"))), loc=(cx, off - 0.06, cz),
                                rot=(0, rnd.uniform(-3, 3), 0), bev=0, parent=G))
                    for sx in (-1, 1):
                        bolt(G, (cx + sx * (pw / 2 - 0.07), off - 0.08, cz + sx * (ph / 2 - 0.07)))
            if rnd.random() < 0.25 and not any(h[0] - 0.3 < x + w / 2 < h[1] + 0.3 and h[2] < zb - 0.9 for h in holes):
                streaks(G, rnd, x + w / 2, zb - 0.1, y=off - 0.05)
            x += w
            k += 1
    # girts and posts inside
    n = max(1, math.ceil((x1 - x0) / 5.0))
    for kx in range(n + 1):
        xx = x0 + 0.1 + (x1 - x0 - 0.2) * kx / n
        if any(h[0] - 0.1 < xx < h[1] + 0.1 for h in holes):
            continue
        board(G, "clad_post", (0.2, 0.2, z1 - z0), (xx, 0.12, (z0 + z1) / 2), rib())
    for zz in [z0 + (z1 - z0) * (i + 1) / (ntier * 2) for i in range(ntier * 2 - 1)]:
        for a, b, _, _ in C.holes_cut(x0, x1, zz - 0.06, zz + 0.06, holes):
            board(G, "clad_girt", (b - a, 0.08, 0.14), ((a + b) / 2, 0.06, zz), rib())
    for a, b, c, d in C.holes_cut(x0, x1, z0, z1, holes):
        solid(G, "wall", (b - a, t, d - c), ((a + b) / 2, t / 2 - 0.05, (c + d) / 2))
    return G


def gable(P, name, x0, x1, z0, top, loc=(0, 0, 0), yaw=0.0, seed=1, holes=(), w=1.1):
    """The end of a vault or a pitched roof above its wall: painted sheets standing side by side, each cut to the line
    top(x) above it (sampled across the sheet, so a curve stays smooth), a batten across. Out of reach, so it does
    not collide."""
    G = group(P, name, loc, yaw)
    rnd = random.Random(seed)
    x = x0
    k = 0
    while x < x1 - 0.05:
        sw = min(w * rnd.uniform(0.85, 1.15), x1 - x)
        col = rnd.choice(("zinc", "steel", "sea", "bone", "brick", "zinc", "rust"))
        m = plate(col)
        yb = -0.02 - 0.015 * (k % 2)
        for r in C.rect_minus(x, x + sw + 0.03, z0, max(top(x), top(x + sw)), list(holes)):
            if r[3] >= min(top(r[0]), top(r[1])) - 1e-6:
                xs = [r[0] + (r[1] - r[0]) * i / 3 for i in range(4)]
                pts = [(r[0], r[2]), (r[1], r[2])] + [(xx, top(xx) - 0.02) for xx in reversed(xs)]
                if min(p[1] for p in pts[2:]) > r[2] + 0.05:
                    quiet(_plate_xz("gable_sheet", pts, 0.03, m, loc=(0, yb, 0), parent=G, bev=0))
            else:
                quiet(K.box("gable_sheet", (r[1] - r[0], 0.03, r[3] - r[2]), m, loc=((r[0] + r[1]) / 2, yb, (r[2] + r[3]) / 2), bev=0, parent=G))
        if rnd.random() < 0.4:
            bolt(G, (x + 0.1, yb - 0.03, z0 + 0.15))
        x += sw
        k += 1
    zb = z0 + 0.5
    board(G, "gable_batten", (x1 - x0, 0.06, 0.14), ((x0 + x1) / 2, 0.03, zb), rib())
    return G
