"""The three Colony Works warehouses (docs/art-direction.md), assembled from colony_kit.py: exteriors, with floors and
stairs inside only (their interiors come with the Warehouse gameplay).

They keep the placeholder prefabs' layout (tools/author-poi-placeholders.py, warehouse()), so the POIs' markers still
fit: the footprint, a floor every 4 m (slab tops at 0.3, 4.3, 8.3 ...), the upper floors stopping 8 m short of the
front wall (the stairwell), the door in the middle of the front wall (6 m wide; its roller door stands 3 m open) and
the roof at 4 m a floor. Blender frame: origin on the ground at the footprint's centre, front towards -Y (the POI's
+Z).

* ``warehouse_small`` - 2 floors, 40 x 30 m. A water tower on the roof (the colony's 12 m tower, props_utility.py).
* ``warehouse`` - 3 floors, 55 x 40 m. A 32 m flue stack at the back corner, ducted into the side wall.
* ``warehouse_large`` - 4 floors, 70 x 50 m. A 38 m tower crane in the yard, its jib over the roof.

All three: ribbed bone cladding in steel-blue posts and belts on a concrete plinth, framed corners with hazard-banded
feet, windows on every floor (some broken), a big painted number for each floor beside the stair, the colony logo
over the door, a fire escape up the right side to the roof, a switchback stair inside, a closed roller door at the
back, downpipes, rooftop air handlers, vents, ducts, skylights and a capsule tank. Round each, part of its model, the
yard (yard()): a fenced gravel lot with its corners cut to stay on the flattened pad (YARD_FLAT, the POI's flatten
radius), a sliding gate open in front where the road comes in and another at the back, floodlight masts, containers,
pallets, drums, cable drums and beams, a dead forklift, a fuel tank, the transformer and a power pole. WALK lists the
spots walk_settlers.py checks a player reaches.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import colony_kit as C
from families import M, BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL, SEA

# name: (floors, width x, depth y, store code, seed)
WAREHOUSES = {
    "warehouse_small": (2, 40.0, 30.0, "STORES 03", 3),
    "warehouse": (3, 55.0, 40.0, "STORES 07", 7),
    "warehouse_large": (4, 70.0, 50.0, "STORES 12", 12),
}
STAIRWELL = 8.0          # the upper floors stop this far short of the front wall
DOOR_W, DOOR_H, DOOR_OPEN = 6.0, 3.75, 3.0
ESCAPE_AT = 11.0         # the fire escape's start, from the front corner along the right wall
ESCAPE_DOOR_BAY = 3      # the right wall's bay its doors open from
HEAD_H = 4.5             # the stair's head house over the front left corner of the roof


def front_bays(w):
    """5 m bays either side of a wider door bay (10 or 15 m) in the middle."""
    k = int((w - 8.0) // 10)
    return [C.BAY] * k + [w - 10.0 * k] + [C.BAY] * k


def warehouse(name="warehouse", floors=None, w=None, d=None, code=None, seed=None, with_yard=True):
    f0, w0, d0, c0, s0 = WAREHOUSES[name]
    floors, w, d, code, seed = floors or f0, w or w0, d or d0, code or c0, seed or s0
    rnd = random.Random(seed)
    root = K.link(bpy.data.objects.new(name, None))
    P = root
    hw, hd = w / 2, d / 2
    H = C.STOREY * floors + C.SLAB          # the roof deck's top
    top = H + C.PARAPET

    # ---- ground slab and the ramp up to the door
    K.box("ground_slab", (w, d, C.SLAB), C.concrete(), loc=(0, 0, C.SLAB / 2), bev=0.03, parent=P)
    C.solid(P, "ground_slab", (w, d, C.SLAB), (0, 0, C.SLAB / 2))
    K.box("door_ramp", (DOOR_W + 2.0, 1.6, 0.3), C.concrete(), loc=(0, -hd - 0.8, 0.0), rot=(-10.6, 0, 0), bev=0.02, parent=P)
    C.ramp(P, "door_ramp", (0, -hd - 1.6, 0.0), (0, -hd, C.SLAB), DOOR_W + 2.0, base=0.3)

    def chance(p):
        return rnd.random() < p

    # ---- walls
    fb = front_bays(w)
    door_bay = len(fb) // 2

    def upper(b, f, blank):
        """An upper floor's bay: windows in runs, broken now and then, with a blank bay (louvred on alternate floors)
        between runs so a long wall is not one grid of windows."""
        if blank:
            return "louvre" if f % 2 else "plain"
        return "broken" if chance(0.3) else "window"

    def front_plan(b, f):
        if b == 0:
            return "plain"                      # the stair core and the floor numbers
        if b == door_bay:
            return "plain" if f <= 1 else upper(b, f, False)
        if f == 0:
            return ("window", "louvre", "fan", "window")[b % 4] if b not in (door_bay - 1, door_bay + 1) else "plain"
        return upper(b, f, abs(b - door_bay) % 3 == 0 and len(fb) > 7)
    front = C.wall(P, "front_wall", w, floors, loc=(0, -hd, 0), bays=fb, plan=front_plan,
                   holes=[(-DOOR_W / 2, DOOR_W / 2, 0.0, C.SLAB + DOOR_H)], seed=seed)
    C.roller_door(front, (0, 0, C.SLAB), DOOR_W, DOOR_H, DOOR_OPEN, lamp=False)
    C.canopy(front, (0, C.CREST - 0.02, C.SLAB + DOOR_H + 1.05), DOOR_W + 3.0, depth=3.0, ties=False)

    def back_plan(b, f):
        if b == door_bay:
            return "plain" if f == 0 else "louvre"
        if f == 0:
            return "fan" if b in (1, len(fb) - 2) else "plain"
        return upper(b, f, abs(b - door_bay) % 3 == 0)
    back = C.wall(P, "back_wall", w, floors, loc=(0, hd, 0), yaw=180, bays=fb, plan=back_plan,
                  holes=[(-DOOR_W / 2, DOOR_W / 2, 0.0, C.SLAB + DOOR_H)], seed=seed + 1)
    C.roller_door(back, (0, 0, C.SLAB), DOOR_W, DOOR_H, 0.0, lamp=False, name="back_door")
    nb = int(round(d / C.BAY))
    # shut truck doors in a side wall (local x along that wall), each under its own canopy
    trucks = {"warehouse": ("left", (-5.0, 5.0)), "warehouse_large": ("right", (5.0, 15.0))}.get(name, (None, ()))

    def truck_holes(side):
        return [(c - DOOR_W / 2, c + DOOR_W / 2, 0.0, C.SLAB + DOOR_H) for c in trucks[1]] if trucks[0] == side else []

    def by_truck(side, b):
        return any(c - DOOR_W / 2 - 0.1 < -d / 2 + C.BAY * (b + 1) and -d / 2 + C.BAY * b < c + DOOR_W / 2 + 0.1 for c in trucks[1]) and trucks[0] == side

    def left_plan(b, f):
        # local bays run from the back (b = 0) to the front; the last two are the stair core's
        if b >= nb - 2:
            return "plain"
        if f == 0:
            return "plain" if by_truck("left", b) else ("louvre" if b % 3 == 1 else "plain")
        return upper(b, f, b % 3 == 1 and nb > 6)
    left = C.wall(P, "left_wall", d, floors, loc=(-hw, 0, 0), yaw=-90, plan=left_plan, holes=truck_holes("left"), seed=seed + 2)

    def right_plan(b, f):
        if b == ESCAPE_DOOR_BAY:
            return "door"
        if f == 0:
            return "plain" if by_truck("right", b) else ("fan" if b == nb - 2 else ("window" if b % 2 else "plain"))
        return upper(b, f, b % 3 == 0 and nb > 6 and b != 0)
    e0 = -hd + ESCAPE_AT
    gap = (e0 + 6.25, e0 + 7.5, H + 0.15, top + 1.0)        # the fire escape's way onto the roof
    right = C.wall(P, "right_wall", d, floors, loc=(hw, 0, 0), yaw=90, plan=right_plan, holes=[gap] + truck_holes("right"), seed=seed + 3)
    C.fire_escape(P, (hw, e0, 0), floors, yaw=90, doors=False)
    for c in trucks[1]:
        g = left if trucks[0] == "left" else right
        C.roller_door(g, (c, 0, C.SLAB), DOOR_W, DOOR_H, 0.0, lamp=False, name="truck_door")
        C.canopy(g, (c, C.CREST - 0.02, C.SLAB + DOOR_H + 1.05), DOOR_W + 3.0, depth=3.0)

    for x, y in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
        C.corner_post(P, (x, y, 0), H + HEAD_H - 0.3 if (x, y) == (-hw, -hd) else top)
    # downpipes beside the posts nearest each corner
    for g, L in ((front, w), (back, w)):
        for x in (-L / 2 + fb[0] + 0.55, L / 2 - fb[-1] - 0.55):
            C.downpipe(g, (x, 0, 0), top)
    for x in (-d / 2 + C.BAY + 0.55, d / 2 - 2 * C.BAY - 0.55):
        C.downpipe(left, (x, 0, 0), top)
    C.downpipe(right, (d / 2 - C.BAY - 0.55, 0, 0), top)

    # ---- the big painted floor numbers beside the stair, the logo over the door
    for f in range(floors):
        C.floor_number(front, f + 1, (-w / 2 + fb[0] / 2, 0, C.STOREY * f + C.SLAB + (1.1 if f else 1.6)), size=2.0)
    logo_w = min(fb[door_bay] - 2.0, 11.0)
    C.logo_panel(front, (0, C.CREST - 0.02, C.STOREY + C.SLAB + 1.45), w=logo_w, h=2.25 if logo_w < 10 else 2.5, code=code)
    C.intake_head(left, d / 2 - 2.5, 6.0, y=C.CREST - 0.3)

    # ---- inside: the upper floors, the stair, and the roof
    for f in range(1, floors):
        z = C.STOREY * f + C.SLAB
        y0 = -hd + STAIRWELL
        C.floor_slab(P, -hw + 0.03, hw - 0.03, y0, hd - 0.03, z)
        K.box("slab_edge", (w - 0.06, 0.2, 0.5), C.steel(), loc=(0, y0 + 0.1, z - 0.25), bev=0.02, parent=P)
        K.box("slab_edge_paint", (w - 0.06, 0.02, 0.2), C.mustard(0.5), loc=(0, y0 - 0.01, z - 0.12), bev=0, parent=P)
        C.railing(P, (-hw + 0.35 + 4.3, y0 + 0.12, z), (hw - 0.4, y0 + 0.12, z), posts=2.5)
    C.stair_core(P, (-hw + 0.35, -hd + 0.35, C.SLAB), floors)
    C.roof_deck(P, -hw + 0.03, hw - 0.03, -hd + 0.03, hd - 0.03, H)
    C.head_house(P, -hw, -hw + fb[0], -hd, -hd + 2 * C.BAY, H, HEAD_H, code.split()[-1], seed=seed)
    roof(P, name, hw, hd, H, rnd)
    weather(P, hw, hd, rnd)
    if with_yard:
        yard(P, name)
    C.quiet_all(P)
    if K.EXPORT:
        C.lighten(P)
    return root


def roof(P, name, hw, hd, H, rnd):
    """What stands on each roof: the silhouette piece, the signature tank, air handlers, ducts, vents and skylights."""
    if name == "warehouse_small":
        import props_utility as PU  # noqa: PLC0415
        t = PU.water_tower_colony()
        t.parent = P
        t.location = (-hw + 9.0, hd - 8.0, H)
        t.rotation_euler = (0, 0, math.radians(15))
        C.solid(P, "water_tower", (4.4, 4.4, 11.0), (-hw + 9.0, hd - 8.0, H + 5.5))
        acs = [(6.0, 4.0, 0), (13.0, 4.0, 0)]
        skylights = [(-2.0, -1.0), (8.0, -1.0)]
        tank = None
        vents = [(-12, -6), (-6, -9), (2, 9), (16, -9), (17, 8), (-15, 2)]
    elif name == "warehouse":
        C.stack(P, (hw + 6.0, hd - 7.0, 0), h=32.0, r=1.2)
        C.duct(P, [(hw + 4.6, hd - 7.0, 6.5), (hw + 0.5, hd - 7.0, 6.5)], w=1.2, h=1.2, name="breeching")
        C.pipe_run(P, [(hw + 4.8, hd - 9.0, 0.3), (hw + 4.8, hd - 9.0, 3.0), (hw + 0.6, hd - 9.0, 3.0)], r=0.2, mat=M(SIGNAL, wear=0.4, grime=0.4))
        tank = (-hw + 8.0, hd - 8.0, 3.0, 5.5, "T-2")
        acs = [(4.0, 6.0, 0), (12.0, 6.0, 0), (20.0, -2.0, 90)]
        skylights = [(-14.0, 0.0), (-4.0, -2.0), (6.0, -2.0)]
        vents = [(-20, -10), (-10, -10), (0, 12), (15, -12), (22, 12), (-22, 4), (18, 4)]
    else:
        C.tower_crane(P, (-hw - 5.5, hd - 10.0, 0), mast=36.0, jib=32.0, counter=11.0, yaw=-25, hook=14.0)
        tank = (-hw + 9.0, hd - 9.0, 3.25, 6.0, "T-4")
        acs = [(2.0, 8.0, 0), (10.0, 8.0, 0), (18.0, 8.0, 0), (26.0, -4.0, 90)]
        skylights = [(-18.0, 0.0), (-8.0, -3.0), (2.0, -3.0), (12.0, -3.0)]
        vents = [(-26, -14), (-14, -14), (-2, 16), (16, -15), (28, 16), (-28, 5), (24, 6), (6, 16)]
        C.roof_tank(P, (-hw + 18.0, hd - 7.0, H), r=2.25, h=4.5, code="T-3")
        C.catwalk(P, (-hw + 11.8, hd - 9.0), (-hw + 15.4, hd - 7.4), width=1.0, z=H + 1.6, brackets=False)
    if tank:
        x, y, r, h, code = tank
        C.roof_tank(P, (x, y, H), r=r, h=h, code=code)
        # the tank's feed pipe down the roof to the left wall and down
        C.pipe_run(P, [(x + r + 0.4, y, H + 1.0), (x + r + 0.4, y, H + 0.4), (x + r + 0.4, hd - 2.5, H + 0.4), (-hw + 0.6, hd - 2.5, H + 0.4),
                       (-hw + 0.6, hd - 2.5, H + 1.3), (-hw - 0.75, hd - 2.5, H + 1.3), (-hw - 0.75, hd - 2.5, 0.8)], r=0.16)
    for k, (x, y, yaw) in enumerate(acs):
        C.ac_unit(P, (x, y, H), yaw=yaw)
    # one duct from the first air handler to the front parapet, down through the roof
    x, y, _ = acs[0]
    C.duct(P, [(x - 1.5, y, H + 0.9), (x - 4.0, y, H + 0.9), (x - 4.0, -hd + 6.0, H + 0.9), (x - 4.0, -hd + 6.0, H + 0.2)], w=0.8, h=0.6)
    for x, y in [(x - 4.0, yy) for yy in range(int(-hd + 8), int(y), 4)]:
        K.box("duct_stand", (1.0, 0.12, 0.6), C.graphite(), loc=(x, y, H + 0.3), bev=0.01, parent=P)
    for k, (x, y) in enumerate(skylights):
        C.skylight(P, (x, y, H), l=8.0, w=2.5, broken={(rnd.randrange(8), rnd.choice((-1, 1)))} | ({(rnd.randrange(8), 1)} if k % 2 else set()))
    for x, y in vents:
        C.mushroom_vent(P, (x, y, H), r=rnd.choice((0.3, 0.35, 0.45)))
    C.roof_hatch(P, (hw - 6.0, hd - 5.0, H))


def weather(P, hw, hd, rnd):
    """Dirt drifted against the plinth and a few weeds where the slab meets the ground (the paint's own grime, chips
    and rust streaks are in the materials and the kit)."""
    dirt = K.mat("drift_dirt", "#8c7f66", rough=1.0, ao=0)
    weed = K.mat("weed", "#6d8f3c", rough=0.8, ao=0)
    spots = []
    for _ in range(26):
        side = rnd.randrange(4)
        if side < 2:
            x, y = rnd.uniform(-hw + 2, hw - 2), (-hd - 0.5 if side == 0 else hd + 0.5)
            if side == 0 and abs(x) < 5:
                continue
            sx, sy = rnd.uniform(1.0, 2.6), rnd.uniform(0.35, 0.6)
        else:
            x, y = (-hw - 0.5 if side == 2 else hw + 0.5), rnd.uniform(-hd + 2, hd - 2)
            sx, sy = rnd.uniform(0.35, 0.6), rnd.uniform(1.0, 2.6)
        if any(math.hypot(x - a, y - b) < 3.0 for a, b in spots):
            continue
        spots.append((x, y))
        dsc = K.disc("drift", 1.0, dirt, (x, y), z=0.012 + 0.0004 * len(spots), segs=20, wobble=0.25, seed=len(spots))
        dsc.scale = (sx, sy, 1)
        dsc.parent = P
        for k in range(rnd.randint(3, 7)):
            h = rnd.uniform(0.15, 0.45)
            blade = C.quad(P, "weed", [(-0.025, 0.0), (0.025, 0.0), (0.0, h)], weed)
            blade.location = (x + rnd.uniform(-sx, sx) * 0.7, y + rnd.uniform(-sy, sy) * 0.7, 0.0)
            blade.rotation_euler = (math.radians(rnd.uniform(-20, 20)), math.radians(rnd.uniform(-20, 20)), math.radians(rnd.uniform(0, 360)))


# ================================================================== the yard
# The fenced yard round each warehouse, part of its model: fence lines (x0, x1, y0, y1) in Blender metres. Its corners
# are cut so every fence post stands on the flattened pad: author-poi-placeholders.py gives the warehouses a flatten
# radius (YARD_FLAT) that reaches past the fence; yard() checks the fence fits inside it.
YARD = {
    "warehouse_small": (-30.0, 30.0, -31.0, 22.0),
    "warehouse": (-37.5, 43.5, -36.0, 28.0),
    "warehouse_large": (-49.0, 45.0, -41.0, 33.0),
}
YARD_FLAT = {"warehouse_small": 47.0, "warehouse": 56.0, "warehouse_large": 65.0}
GATE = 5.0          # half the front gate's opening (the road in, centred on the door)
BACK_GATE = 4.0     # half the back gate's opening (behind the back roller door)


def yard_outline(name):
    """The yard fence's corners (x, y), counter-clockwise from the front left, each corner cut by enough (3 m at
    least) to keep it 1.5 m inside the flattened pad."""
    x0, x1, y0, y1 = YARD[name]
    R = YARD_FLAT[name] - 1.5

    def cut(cx, cy):
        need = max(abs(cx) - math.sqrt(max(0.0, R * R - cy * cy)), abs(cy) - math.sqrt(max(0.0, R * R - cx * cx)))
        return max(3.0, math.ceil((need + 0.5) * 2) / 2)
    c = [cut(x0, y0), cut(x1, y0), cut(x1, y1), cut(x0, y1)]
    pts = [(x0 + c[0], y0), (x1 - c[1], y0), (x1, y0 + c[1]), (x1, y1 - c[2]), (x1 - c[2], y1), (x0 + c[3], y1), (x0, y1 - c[3]), (x0, y0 + c[0])]
    assert all(math.hypot(x, y) <= R + 1e-6 for x, y in pts), f"{name}: the yard fence leaves the flattened pad"
    return pts


def _pallets(P, loc, n, yaw, rnd):
    """A stack of n empty pallets, each three runners under five deck boards (plain boxes: a stack is seen from the
    yard, not up close), colliding as one block."""
    G = C.group(P, "pallet_stack", loc, yaw)
    for j in range(n):
        Q = C.group(G, "pallet", (rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), j * 0.144), rnd.uniform(-4, 4))
        wood = K.mat(f"pallet_wood{j % 3}", ("#9a7f5c", "#8c7353", "#a58a64")[j % 3], rough=0.9, grime=0.3, noise_scale=4)
        for x in (-0.53, 0.0, 0.53):
            K.box("pallet_runner", (0.14, 1.0, 0.122), wood, loc=(x, 0, 0.061), bev=0, parent=Q)
        for k in range(5):
            K.box("pallet_deck", (1.2, 0.14, 0.022), wood, loc=(0, -0.43 + k * 0.215, 0.133), bev=0, parent=Q)
    C.quiet_all(G)
    C.solid(G, "pallets", (1.2, 1.0, 0.144 * n), (0, 0, 0.072 * n))
    return G


def yard(P, name):
    """The yard round the warehouse (part of its model): a gravel lot inside a welded-mesh fence with its corners cut,
    a sliding gate open in front of the door where the road comes in and a smaller one behind the back roller door, a
    worn concrete apron with parking bays before the front, floodlight masts in the cut corners, shipping containers
    (one stacked) along the left, loaded pallets, empty pallet stacks and drums by the door, a dead forklift, cable
    drums (and on the large one steel beams) on the right, a fuel tank and the transformer at the back, a power pole by the intake on
    the left wall. The way from the gate to the door, the markers' ground in front of it and the fire escape's foot are
    kept clear; every prop collides as its box."""
    import props_colony_a as CA  # noqa: PLC0415
    import props_colony_b as CB  # noqa: PLC0415
    import props_industry as PI  # noqa: PLC0415
    floors, w, d, _, seed = WAREHOUSES[name]
    rnd = random.Random(seed + 50)
    hw, hd = w / 2, d / 2
    x0, x1, y0, y1 = YARD[name]
    pts = yard_outline(name)
    G = C.group(P, "yard")
    # the ground: gravel inside the fence, the apron before the front, the road out through the gate, bay lines
    bm = bmesh.new()
    bm.faces.new([bm.verts.new((x, y, 0.012)) for x, y in pts])
    gravel = K.from_bm("yard_gravel", bm, K.mat("gravel", "#8f8a7e", rough=1.0, noise_scale=3, ao=0.2))
    gravel.parent = G
    C.quiet(gravel)
    asphalt = K.mat("asphalt", "#5d5f60", rough=0.95, noise_scale=2, ao=0.2)
    C.quiet(K.box("yard_road", (9.0, 9.0, 0.02), asphalt, loc=(0.0, y0 - 3.5, 0.01), bev=0, parent=G))
    C.quiet(K.box("yard_drive", (9.0, -hd - 2.0 - y0, 0.02), asphalt, loc=(0.0, (y0 - hd - 2.0) / 2, 0.022), bev=0, parent=G))
    C.quiet(K.box("yard_apron", (w + 6.0, 9.0, 0.02), K.mat("worn_concrete", "#a9a79e", rough=0.9, grime=0.2, noise_scale=2, ao=0.3), loc=(0, -hd - 4.5, 0.018), bev=0, parent=G))
    for x in (-hw + 2.0, -hw + 5.0, -hw + 8.0, hw - 8.0, hw - 5.0, hw - 2.0):
        C.quiet(K.box("bay_line", (0.15, 6.0, 0.01), C.mustard(0.6), loc=(x, y0 + 6.0, 0.03), bev=0, parent=G))
    # the fence: the front side split round the gate, the back round its gate, the rest whole
    F = C.group(G, "yard_fence")
    sides = [(pts[k], pts[(k + 1) % 8]) for k in range(8)]
    for k, (a, b) in enumerate(sides):
        if k == 0:
            C.mesh_fence(F, a, (-GATE, y0), lite=True)
            C.mesh_fence(F, (GATE, y0), (-GATE, y0), gate=(0.0, 1.0), name="front_gate", lite=True)
            C.mesh_fence(F, (GATE, y0), b, lite=True)
        elif k == 4:
            C.mesh_fence(F, a, (BACK_GATE, y1), lite=True)
            C.mesh_fence(F, (-BACK_GATE, y1), (BACK_GATE, y1), gate=(0.0, 1.0), name="back_gate", lite=True)
            C.mesh_fence(F, (-BACK_GATE, y1), b, lite=True)
        else:
            C.mesh_fence(F, a, b, lite=True)
    for x in (-GATE - 0.3, GATE + 0.3):
        C.quiet(K.box("gate_pier", (0.4, 0.4, 2.6), C.steel(), loc=(x, y0, 1.3), bev=0.03, parent=F))
        C.hazard_band(C.group(F, "gate_haz", (x, y0 - 0.21, 0)), (0, 0, 0.3), 0.4, 1.0)
        C.solid(F, "gate_pier", (0.4, 0.4, 2.6), (x, y0, 1.3))
    # floodlight masts in the cut corners, facing the yard
    for k in (1, 3, 5, 7):
        (ax, ay), (bx, by) = sides[k]
        mx, my = (ax + bx) / 2, (ay + by) / 2
        u = Vector((-mx, -my, 0)).normalized()
        C.floodlight_mast(G, (mx + u.x * 1.5, my + u.y * 1.5, 0), h=12.0, yaw=math.degrees(math.atan2(u.x, -u.y)), lit=False)
    # by the door: loaded pallets and empty stacks either side, clear of the signs, lights and cones
    for k, (x, y, yaw) in enumerate(((-11.0, -hd - 3.0, 5), (11.0, -hd - 3.0, 0))):
        C.prop(G, lambda k=k: CB.pallet(21 + k), (x, y, 0), yaw=yaw)
    for x, y, n in ((-13.0, -hd - 3.0, 4), (-14.5, -hd - 4.4, 2), (13.2, -hd - 3.2, 3)):
        _pallets(G, (x, y, 0), n, rnd.uniform(-6, 6), rnd)
    C.prop(G, lambda: PI.chemical_drums(64), (hw - 4.0, -hd - 6.5, 0), yaw=-30)
    C.prop(G, lambda: CB.forklift_wreck(22), (hw - 9.0, -hd - 9.5, 0), yaw=200)
    # containers along the left (behind the truck doors' way for the medium warehouse), cable drums and beams right
    cy = (hd - 16.5, hd - 9.0) if name == "warehouse" else tuple(-hd + 3.0 + k * 7.5 for k in range(3 if floors > 2 else 2))
    for k, y in enumerate(cy):
        C.prop(G, lambda k=k: CA.shipping_container_open(27) if k == 1 else CA.shipping_container(26), (-hw - 6.5, y, 0), yaw=90 + rnd.uniform(-3, 3))
    C.prop(G, lambda: CA.shipping_container(29), (-hw - 6.5, cy[0], 2.6), yaw=88)
    C.prop(G, lambda: PI.cable_drum_stack(62), (hw + 5.5, -hd - 4.0, 0), yaw=20)
    if name == "warehouse_large":
        C.prop(G, lambda: PI.steel_beam_stack(59), (hw + 5.5, hd + 3.0 if name == "warehouse_large" else hd - 4.0, 0), yaw=90)
    C.prop(G, lambda: CA.fuel_tank(28), (-hw - 6.0, hd + 3.0, 0), yaw=0)
    C.prop(G, lambda: CA.transformer_box(31), (-hw - 2.5, -hd + 4.0 if name == "warehouse" else hd - 6.0, 0), yaw=90)
    # power in to the intake on the left wall
    C.prop(G, lambda: CA.power_pole(22), (x0 + 2.0, -hd - 6.0, 0), yaw=90, collide=((0.5, 0.5, 9.0), (0, 0, 4.5)))
    return G


# Spots walk_settlers.py checks a player reaches on each warehouse's collider (Blender frame), and the ground it walks.
WALK = {}
for _n, (_f, _w, _d, _c, _s) in WAREHOUSES.items():
    _x0, _x1, _y0, _y1 = YARD[_n]
    WALK[_n] = {"half": (max(-_x0, _x1) + 8.0, max(-_y0, _y1) + 8.0),
                "targets": {"outside the front gate": (0.0, _y0 - 1.5, 0.0), "through the back gate": (0.0, _y1 + 1.5, 0.0),
                            "inside the door": (0.0, -_d / 2 + 4.0, 0.3), "on the roof": (-_w / 2 + 10.0, -_d / 2 + 3.0, 4.0 * _f + 0.3),
                            "by the containers": (-_w / 2 - 3.5, YARD[_n][2] + 8.0 if _n != "warehouse" else _d / 2 - 12.0, 0.0)}}
