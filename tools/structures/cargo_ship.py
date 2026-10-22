"""The SV-04 Salvager: a salvage freighter, walk-in, with coloured cargo containers racked along its spine.

Three hull blocks in a row, nose towards +Y (Blender), so -Z in glTF/Godot:

- the aft block (y -30 .. -12, 15 m wide): engine room, engineering (the Power Core and the Master Battery), crew
  quarters, the mess and the medbay off a centre corridor; two dorsal engine housings and a landing pad on top, three
  main engines at the back and two fuel tanks under the belly;
- the spine (y -12 .. 14, 5.2 m wide): the cargo corridor, with sixteen containers racked two high on each side, a
  crane gantry and hex hatches on top;
- the bow (y 14 .. 33): cargo control with the airlock, then the bridge (the Logbook) under a sloped canopy.

The deck is z 0. The hull is framed construction at ship scale: belly, walls and roof are separate solids so a cutaway
hides the roof (ROOF) and shows the rooms. Container colours say what they carry (CARGO).
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, glow, GUNMETAL, CYAN, COPPER, MUSTARD, BONE, STEEL, GRAPHITE, WOOD, WARM

T = 0.2          # wall thickness
ROOF = []        # roof pieces and everything on them, hidden for the cutaway
ROOMS = {}       # room name -> (x, y) centre, for labelling plans
CONTAINERS = []  # every container slot: code, colour, rack position, tier

# What each container colour carries: the opening scatters it where the containers land.
CARGO = {
    "red": ("#b5473c", "Metal", "scrap metal, metal blocks, bearings"),
    "blue": ("#3d79b3", "Electronics", "components, circuit boards, batteries"),
    "yellow": ("#d9ae2f", "Fuel and chemicals", "gasoline, chemicals"),
    "green": ("#5c8f47", "Farming", "seeds, fertiliser, food"),
    "tan": ("#a27c55", "Building", "wood, concrete, glass"),
}
# the racks, aft to fore; each row is (port lower, port upper, starboard lower, starboard upper)
RACKS = [("red", "tan", "blue", "red"), ("green", "red", "yellow", "tan"),
         ("blue", "yellow", "red", "green"), ("tan", "blue", "green", "yellow")]

PALETTES = {
    # the reference look: light grey hull, gunmetal frames
    "light": dict(hull="#c4c8cb", hull2="#a4aaaf", trim="#727980", dark="#33383e", inner="#8d949a", floor="#3b4046"),
    # Salvager Tech as in docs/art-direction.md: dark hull
    "dark": dict(hull="#4b525b", hull2="#3e444c", trim="#2c3036", dark="#1e2126", inner="#5f666e", floor="#2f343a"),
}


# ------------------------------------------------------------------ geometry helpers
def loft(name, sections, material, parent, bev=0.04, cap=True):
    """A solid through cross-sections [(y, [(x, z), ...]), ...] that all have the same number of points."""
    bm = bmesh.new()
    rings = [[bm.verts.new((x, y, z)) for x, z in poly] for y, poly in sections]
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, material)
    ob.parent = parent
    if bev:
        K.bevel(ob, bev, segs=2, angle=30)
    return ob


def cut(ob, *cutters):
    for c in cutters:
        mod = ob.modifiers.new("cut", "BOOLEAN")
        mod.object = c
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        ob.modifiers.move(len(ob.modifiers) - 1, 0)
    return ob


def cutter(P, loc, size, rot=(0, 0, 0)):
    c = K.box("cutter", size, K.mat("cut", "#ff00ff"), loc=loc, rot=rot, bev=0, parent=P)
    c.hide_render = True
    c.hide_viewport = True
    return c


def door_cut(P, x, y, w=1.4, h=2.4, axis="y", depth=1.0):
    """A cutter for a doorway in a wall across y (axis y: the door's width runs along x) or along y (axis x)."""
    size = (w, depth, h) if axis == "y" else (depth, w, h)
    return cutter(P, (x, y, h / 2 - 0.05), size)


def wall(P, name, a, b, h, mat, doors=(), t=T):
    """A straight interior wall from (x, y) a to b, standing on the deck, with doorway cutters."""
    (xa, ya), (xb, yb) = a, b
    d = Vector((xb - xa, yb - ya, 0))
    ob = K.box(name, (d.length, t, h), mat, loc=((xa + xb) / 2, (ya + yb) / 2, h / 2), rot=(0, 0, math.degrees(math.atan2(d.y, d.x))), bev=0.02, parent=P)
    return cut(ob, *doors)


def panel_lines(P, x, y0, y1, z0, z1, mat, step=2.0, side=1, w=0.07):
    """Raised seams on a flat side wall at x, between y0 and y1."""
    y = y0 + step
    while y < y1 - 0.3:
        K.box("seam", (0.05, w, z1 - z0), mat, loc=(x + side * 0.025, y, (z0 + z1) / 2), bev=0.01, parent=P)
        y += step


def door_frame(P, x, y, w, h, frame, axis="y", lamp=None):
    """Posts and a lintel round a doorway, with an optional status lamp over it."""
    if axis == "y":
        for s in (-1, 1):
            K.box("door_post", (0.18, 0.34, h + 0.1), frame, loc=(x + s * (w / 2 + 0.09), y, h / 2), bev=0.02, parent=P)
        K.box("door_lintel", (w + 0.54, 0.34, 0.22), frame, loc=(x, y, h + 0.1), bev=0.02, parent=P)
        if lamp:
            K.box("door_lamp", (0.3, 0.38, 0.1), lamp, loc=(x, y, h + 0.3), bev=0.02, parent=P)
    else:
        for s in (-1, 1):
            K.box("door_post", (0.34, 0.18, h + 0.1), frame, loc=(x, y + s * (w / 2 + 0.09), h / 2), bev=0.02, parent=P)
        K.box("door_lintel", (0.34, w + 0.54, 0.22), frame, loc=(x, y, h + 0.1), bev=0.02, parent=P)
        if lamp:
            K.box("door_lamp", (0.38, 0.3, 0.1), lamp, loc=(x, y, h + 0.3), bev=0.02, parent=P)


def sign(P, text, loc, rot, size=0.22):
    """A room sign: a dark plate with bone stencil text."""
    x, y, z = loc
    plate = K.box("sign_plate", (len(text) * size * 0.62 + 0.25, 0.04, size + 0.16), M(GRAPHITE, metal=0.3), loc=loc, rot=rot, bev=0.01, parent=P)
    rz = math.radians(rot[2])
    off = Vector((-math.sin(rz), math.cos(rz), 0)) * -0.03
    FR.label(P, text, (x + off.x, y + off.y, z), size, M(BONE, wear=0.2), rot=(90, 0, rot[2]), extrude=0.005)
    return plate


def ceiling_light(P, loc, h, on=True, length=1.6):
    x, y = loc
    K.box("clight", (length, 0.24, 0.07), glow("#d6f2ff", 5) if on else K.mat("deadlight", "#3a4044", rough=0.4), loc=(x, y, h - 0.06), bev=0.02, parent=P)
    if on:
        K.point("clight_pt", (x, y, h - 0.6), "#e2f5ff", 180, 0.6).parent = P



# ------------------------------------------------------------------ hull shells
def octagon(hw, zb, zt, cb, ct):
    return [(-hw + cb, zb), (hw - cb, zb), (hw, zb + cb), (hw, zt - ct), (hw - ct, zt), (-hw + ct, zt), (-hw, zt - ct), (-hw, zb + cb)]


def keyed(keys, y):
    """Linear interpolation through [(y, a, b, ...), ...] keys sorted by y."""
    if y <= keys[0][0]:
        return keys[0][1:]
    for a, b in zip(keys, keys[1:]):
        if y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(p + (q - p) * t for p, q in zip(a[1:], b[1:]))
    return keys[-1][1:]


def shell(P, name, outer, inner, material, cutters=(), bev=0.05):
    """A hollow hull block: the outer loft minus the inner one (the rooms) and any cutters (doors, windows, bays)."""
    ob = loft(name, outer, material, P, bev=0)
    hole = loft(f"{name}_inner", inner, K.mat("cut", "#ff00ff"), P, bev=0)
    hole.hide_render = hole.hide_viewport = True
    cut(ob, hole, *cutters)
    if bev:
        mod = ob.modifiers.new("bevel", "BEVEL")
        mod.width, mod.segments, mod.limit_method, mod.angle_limit = bev, 1 if K.EXPORT else 2, "ANGLE", math.radians(30)
    return ob


def plates(P, side, x, y0, y1, z0, z1, mats, rnd, skip=(), length=(1.6, 3.2), proud=0.06):
    """Proud armour plates on a flat side at x (facing side), in a band z0..z1, leaving the skip y-ranges clear."""
    y = y0 + 0.1
    while y < y1 - 0.4:
        L = min(rnd.uniform(*length), y1 - 0.1 - y)
        a, b = y, y + L
        for s0, s1 in skip:
            if a < s1 and b > s0:
                if a < s0 - 0.5:
                    b = s0 - 0.1
                else:
                    a = None
                    y = s1 + 0.1
                break
        if a is None:
            continue
        m = mats["hull2"] if rnd.random() < 0.3 else mats["hull"]
        K.box("plate", (proud, b - a, z1 - z0), m, loc=(x + side * proud / 2, (a + b) / 2, (z0 + z1) / 2), bev=0.03, parent=P)
        y = b + 0.12
    # a dark seam behind the plates reads as the gaps
    K.box("plate_gap", (0.02, y1 - y0, z1 - z0 + 0.05), mats["dark"], loc=(x + side * 0.005, (y0 + y1) / 2, (z0 + z1) / 2), bev=0, parent=P)


def plates_line(P, a, b, z0, z1, side, mats, rnd, length=(1.4, 2.6), proud=0.06):
    """Proud plates along a slanted side wall from (x, y) a to b (side: which way it faces in x)."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b - a
    L = d.length
    u = d / L
    n = Vector((u.y, -u.x, 0)) if side > 0 else Vector((-u.y, u.x, 0))
    yaw = math.degrees(math.atan2(u.y, u.x))
    t = 0.1
    while t < L - 0.4:
        l = min(rnd.uniform(*length), L - 0.1 - t)
        c = a + u * (t + l / 2) + n * proud / 2
        m = mats["hull2"] if rnd.random() < 0.3 else mats["hull"]
        K.box("plate", (l, proud, z1 - z0), m, loc=(c.x, c.y, (z0 + z1) / 2), rot=(0, 0, yaw), bev=0.03, parent=P)
        t += l + 0.12
    c = (a + b) / 2 + n * 0.005
    K.box("plate_gap", (L, 0.02, z1 - z0 + 0.05), mats["dark"], loc=(c.x, c.y, (z0 + z1) / 2), rot=(0, 0, yaw), bev=0, parent=P)


def edge_rail(P, pts, mat, r=0.09):
    """A trim rail along a polyline of (x, y, z) points: the hard edges of the hull."""
    for p, q in zip(pts, pts[1:]):
        p, q = Vector(p), Vector(q)
        d = q - p
        ob = K.box("edge_rail", (r * 2, d.length + r, r * 2), mat, loc=(p + q) / 2, bev=0.02, parent=P)
        ob.rotation_euler = d.to_track_quat("Y", "Z").to_euler()


def hull_ribs(P, x, y0, y1, h, frame, dark, step=2.0, sides=True, skip=()):
    """Inside a block: frame ribs up the walls and beams across the ceiling, a dark skirting and a trim line."""
    y = y0
    while y <= y1 + 0.01:
        if sides and not any(abs(y - k) < 1.0 for k in skip):
            for s in (-1, 1):
                K.box("rib", (0.18, 0.22, h - 0.5), frame, loc=(s * (x - 0.09), y, (h - 0.5) / 2 + 0.05), bev=0.03, parent=P)
        K.box("ceiling_beam", (2 * x, 0.22, 0.2), frame, loc=(0, y, h - 0.12), bev=0.03, parent=P)
        y += step
    for s in (-1, 1):
        K.box("skirting", (0.04, y1 - y0 + 0.4, 0.18), dark, loc=(s * (x - 0.02), (y0 + y1) / 2, 0.12), bev=0, parent=P)
        K.box("trim_line", (0.03, y1 - y0 + 0.4, 0.06), frame, loc=(s * (x - 0.015), (y0 + y1) / 2, 1.15), bev=0, parent=P)


def greebles(P, side, x, y0, y1, z, mats, rnd, n=6, skip=()):
    """Small machinery on a side: boxes, vents and pipe stubs, keeping out of the skip y-ranges."""
    for _ in range(n * 3):
        if n <= 0:
            break
        y = rnd.uniform(y0 + 0.5, y1 - 0.5)
        if any(s0 - 0.6 < y < s1 + 0.6 for s0, s1 in skip):
            continue
        n -= 1
        kind = rnd.random()
        if kind < 0.4:
            w, h = rnd.uniform(0.4, 1.2), rnd.uniform(0.3, 0.6)
            K.box("greeble", (0.18, w, h), mats["trim"], loc=(x + side * 0.15, y, z + rnd.uniform(-0.2, 0.2)), bev=0.03, parent=P)
            K.box("greeble_slot", (0.02, w * 0.7, h * 0.3), mats["dark"], loc=(x + side * 0.25, y, z), bev=0, parent=P)
        elif kind < 0.7:
            FR.vent(P, (x + side * 0.1, y, z), 1.0, 0.5, mats["trim"], mats["dark"], axis="x") if side < 0 else \
                FR.vent(P, (x + side * 0.1, y, z), 1.0, 0.5, mats["trim"], mats["dark"], axis="x")
        else:
            K.cyl("greeble_tank", 0.18, rnd.uniform(0.8, 1.6), mats["trim"], loc=(x + side * 0.22, y, z), rot=(90, 0, 0), segs=12, bev=0.02, parent=P)
# ------------------------------------------------------------------ furniture
def bunk(P, loc, yaw=0):
    x, y, z = loc
    fr, mt = M(GUNMETAL, metal=0.5), K.mat("mattress", "#4f6b62", rough=0.9, ao=0.4)
    root = K.link(bpy.data.objects.new("bunk", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    for zz in (0.45, 1.55):
        K.box("bunk_frame", (2.0, 0.9, 0.1), fr, loc=(0, 0, zz), bev=0.02, parent=root)
        K.box("mattress", (1.9, 0.82, 0.14), mt, loc=(0, 0, zz + 0.12), bev=0.05, parent=root)
        K.box("pillow", (0.4, 0.6, 0.1), K.mat("pillow", "#d6d2c4", rough=0.9), loc=(-0.72, 0, zz + 0.24), bev=0.04, parent=root)
        K.box("blanket", (1.1, 0.86, 0.06), K.mat("blanket", "#3e6a8c", rough=0.9), loc=(0.35, 0, zz + 0.2), bev=0.03, parent=root)
    for dx in (-0.98, 0.98):
        for dy in (-0.42, 0.42):
            K.box("bunk_post", (0.07, 0.07, 2.0), fr, loc=(dx, dy, 1.0), bev=0.01, parent=root)
    K.box("bunk_rail", (1.2, 0.04, 0.2), fr, loc=(0.2, 0.44, 1.95), bev=0.01, parent=root)
    return root


def console(P, loc, yaw=0, w=1.8, screen_col=CYAN):
    root = K.link(bpy.data.objects.new("console", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    FR.framed_box(root, "con", (0, 0, 0.45), (w, 0.8, 0.9), M(GRAPHITE, metal=0.3), M(GUNMETAL, metal=0.5), fw=0.07)
    K.box("con_top", (w, 0.62, 0.08), M(GUNMETAL, metal=0.5), loc=(0, 0.08, 0.95), rot=(-22, 0, 0), bev=0.02, parent=root)
    FR.screen(root, (0, 0.24, 1.25), w * 0.75, 0.45, M(GRAPHITE), glow(screen_col, 2.5), rot=(-12, 0, 0))
    for k in range(5):
        K.cyl("knob", 0.04, 0.05, M(MUSTARD), loc=(-w * 0.32 + k * w * 0.16, -0.12, 0.98), rot=(-22, 0, 0), bev=0.005, parent=root)
    K.box("con_keys", (w * 0.4, 0.18, 0.03), glow("#9fe3ff", 1.2), loc=(0, 0.1, 1.0), rot=(-22, 0, 0), bev=0.005, parent=root)
    return root


def chair(P, loc, yaw=0, col="#4f6b62", big=False):
    root = K.link(bpy.data.objects.new("chair", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    s = 1.25 if big else 1.0
    gm = M(GUNMETAL, metal=0.5)
    K.cyl("chair_post", 0.07, 0.45, gm, loc=(0, 0, 0.22), parent=root)
    K.cyl("chair_foot", 0.3, 0.06, gm, loc=(0, 0, 0.03), parent=root)
    K.box("chair_seat", (0.6 * s, 0.6 * s, 0.14), M(col, wear=0.1), loc=(0, 0, 0.5), bev=0.06, parent=root)
    K.box("chair_back", (0.6 * s, 0.14, 0.8 * s), M(col, wear=0.1), loc=(0, -0.3 * s, 0.95), rot=(-8, 0, 0), bev=0.06, parent=root)
    if big:
        for sx in (-1, 1):
            K.box("chair_arm", (0.1, 0.6, 0.1), gm, loc=(sx * 0.42, 0, 0.75), bev=0.03, parent=root)
            K.box("chair_pad", (0.16, 0.3, 0.06), glow(CYAN, 1.0), loc=(sx * 0.42, 0.12, 0.82), bev=0.02, parent=root)
    return root


def locker_row(P, loc, n, yaw=0, col=STEEL):
    root = K.link(bpy.data.objects.new("lockers", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    for k in range(n):
        x = (k - (n - 1) / 2) * 0.62
        FR.framed_box(root, "locker", (x, 0, 1.0), (0.6, 0.55, 2.0), M(col, metal=0.3), M(GUNMETAL, metal=0.5), fw=0.05)
        for z in (1.6, 1.68, 1.76):
            K.box("locker_slot", (0.3, 0.02, 0.03), M(GRAPHITE), loc=(x, -0.29, z), bev=0, parent=root)
        K.box("locker_handle", (0.04, 0.04, 0.2), M(BONE), loc=(x + 0.2, -0.3, 1.1), bev=0.01, parent=root)
    return root


def table(P, loc, size, yaw=0, top="#7a8088"):
    x, y, z = loc
    sx, sy = size
    gm = M(GUNMETAL, metal=0.5)
    K.box("table_top", (sx, sy, 0.08), M(top, metal=0.3), loc=(x, y, 0.78), rot=(0, 0, yaw), bev=0.03, parent=P)
    K.box("table_leg", (0.2, sy * 0.6, 0.7), gm, loc=(x, y, 0.38), rot=(0, 0, yaw), bev=0.02, parent=P)


def bench(P, loc, length, yaw=0, col="#4f6b62"):
    x, y, z = loc
    K.box("bench", (length, 0.45, 0.12), M(col, wear=0.15), loc=(x, y, 0.48), rot=(0, 0, yaw), bev=0.04, parent=P)
    K.box("bench_base", (length - 0.2, 0.3, 0.42), M(GUNMETAL, metal=0.5), loc=(x, y, 0.21), rot=(0, 0, yaw), bev=0.02, parent=P)


def crate(P, loc, size=(0.9, 0.7, 0.6), yaw=0, col=WOOD):
    x, y, z = loc
    if col == WOOD:
        K.box("crate", size, K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0), loc=(x, y, z + size[2] / 2), rot=(0, 0, yaw), bev=0.03, parent=P)
    else:
        FR.framed_box(P, "crate", (x, y, z + size[2] / 2), size, M(col, metal=0.2), M(GRAPHITE, metal=0.3), fw=0.06)


def shelf(P, loc, length, yaw, rnd, levels=3, h=2.0):
    """A wall shelf unit along its local x, stocked with boxes, cans and tins."""
    root = K.link(bpy.data.objects.new("shelf", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    gm = M(GUNMETAL, metal=0.5)
    for sx in (-1, 1):
        K.box("shelf_post", (0.06, 0.5, h), gm, loc=(sx * length / 2, 0, h / 2), bev=0.01, parent=root)
    for k in range(levels):
        z = 0.25 + k * (h - 0.3) / max(1, levels - 1)
        K.box("shelf_board", (length, 0.5, 0.04), gm, loc=(0, 0, z), bev=0.01, parent=root)
        x = -length / 2 + 0.15
        while x < length / 2 - 0.3:
            if rnd.random() < 0.5:
                w = rnd.uniform(0.25, 0.5)
                K.box("stock_box", (w, 0.35, rnd.uniform(0.2, 0.35)), M(rnd.choice([WOOD, "#3d79b3", BONE, "#5c8f47", MUSTARD]), wear=0.3),
                      loc=(x + w / 2, 0, z + 0.14), bev=0.02, parent=root)
                x += w + 0.06
            else:
                for j in range(rnd.randint(2, 4)):
                    K.cyl("stock_can", 0.06, 0.16, M(rnd.choice(["#c8423a", "#d9ae2f", "#7a8088", "#3e6a8c"])), loc=(x + 0.07 + j * 0.13, rnd.uniform(-0.1, 0.1), z + 0.1), segs=10, bev=0, parent=root)
                x += 0.6
    return root


def pipe_run(P, x, y0, y1, z, r, mat, axis="y"):
    L = y1 - y0
    if axis == "y":
        K.cyl("pipe", r, L, mat, loc=(x, (y0 + y1) / 2, z), rot=(90, 0, 0), segs=12, bev=0, parent=P)
    else:
        K.cyl("pipe", r, L, mat, loc=((y0 + y1) / 2, x, z), rot=(0, 90, 0), segs=12, bev=0, parent=P)


# ------------------------------------------------------------------ containers
def container(P, center, colour, code, rnd, length=6.0, w=2.5, h=2.5, open_side=None):
    """A ribbed freight container along y: corner posts and castings, corrugated sides, lock bars on the doors (+y),
    its code stencilled on the outer side."""
    x, y, z = center
    col = CARGO[colour][0]
    body = M(col, metal=0.25, rough=0.55, wear=0.35)
    rib = M(col, metal=0.25, rough=0.5, wear=0.5)
    frame = M("#3a3e44", metal=0.5, rough=0.45, wear=0.3)
    root = K.link(bpy.data.objects.new(f"container_{colour}", None))
    root.parent = P
    root.location = center
    K.box("cont_body", (w - 0.12, length - 0.12, h - 0.12), body, bev=0.02, parent=root)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.box("cont_post", (0.16, 0.16, h), frame, loc=(sx * (w / 2 - 0.08), sy * (length / 2 - 0.08), 0), bev=0.02, parent=root)
            for sz in (-1, 1):
                K.box("cont_cast", (0.22, 0.22, 0.2), frame, loc=(sx * (w / 2 - 0.09), sy * (length / 2 - 0.09), sz * (h / 2 - 0.1)), bev=0.02, parent=root)
        for sz in (-1, 1):
            K.box("cont_rail", (0.12, length - 0.3, 0.14), frame, loc=(sx * (w / 2 - 0.07), 0, sz * (h / 2 - 0.08)), bev=0.02, parent=root)
    for sz in (-1, 1):
        for sy in (-1, 1):
            K.box("cont_rail", (w - 0.3, 0.12, 0.14), frame, loc=(0, sy * (length / 2 - 0.07), sz * (h / 2 - 0.08)), bev=0.02, parent=root)
    # corrugation on the long sides and the roof
    n = int(length / 0.42)
    for k in range(n):
        yy = -length / 2 + 0.35 + k * (length - 0.7) / (n - 1)
        for sx in (-1, 1):
            K.box("cont_rib", (0.06, 0.16, h - 0.4), rib, loc=(sx * (w / 2 - 0.05), yy, 0), bev=0.015, parent=root)
        K.box("cont_rib", (w - 0.4, 0.16, 0.05), rib, loc=(0, yy, h / 2 - 0.05), bev=0.015, parent=root)
    # doors at +y: two leaves, four lock bars
    for k in range(4):
        K.cyl("cont_lockbar", 0.03, h - 0.35, M(GRAPHITE, metal=0.6), loc=(-w / 2 + 0.35 + k * (w - 0.7) / 3, length / 2 - 0.02, 0), segs=8, bev=0, parent=root)
    K.box("cont_seam", (0.03, 0.03, h - 0.3), frame, loc=(0, length / 2 - 0.04, 0), bev=0, parent=root)
    # code on both long sides, and a white band
    for sx in (-1, 1):
        FR.label(root, code, (sx * (w / 2 + 0.005), -length * 0.18, h * 0.22), 0.34, M("#f2efe6", wear=0.3), rot=(90, 0, 90 * sx))
        K.box("cont_band", (0.02, 1.2, 0.3), M("#f2efe6", wear=0.4), loc=(sx * (w / 2 + 0.002), length * 0.25, h * 0.25), bev=0, parent=root)
    return root


def rack(P, ys, x_in, x_out, z0, z1, frame, mats):
    """The rack the containers ride in on one side: posts between the bays, clamp arms, top and bottom rails."""
    sx = 1 if x_out > 0 else -1
    for y in ys:
        K.box("rack_post", (abs(x_out - x_in), 0.4, z1 - z0), frame, loc=((x_in + x_out) / 2, y, (z0 + z1) / 2), bev=0.04, parent=P)
        for z in (z0 + 0.6, (z0 + z1) / 2, z1 - 0.6):
            K.box("rack_clamp", (0.5, 0.7, 0.3), mats["dark"], loc=(x_out - sx * 0.15, y, z), bev=0.04, parent=P)
        K.box("rack_cap", (abs(x_out - x_in) + 0.2, 0.6, 0.3), frame, loc=((x_in + x_out) / 2, y, z1 + 0.1), bev=0.05, parent=P)
    K.box("rack_rail_top", (0.3, ys[-1] - ys[0], 0.25), frame, loc=(x_out - sx * 0.15, (ys[0] + ys[-1]) / 2, z1 - 0.05), bev=0.04, parent=P)
    K.box("rack_rail_bot", (abs(x_out - x_in), ys[-1] - ys[0], 0.25), frame, loc=((x_in + x_out) / 2, (ys[0] + ys[-1]) / 2, z0 + 0.05), bev=0.04, parent=P)




# ------------------------------------------------------------------ the ship
A0, A1, AHW = -30.0, -12.0, 7.5            # aft block
S0, S1, SHW = -12.0, 14.0, 2.6             # spine
B0, BW1, BTIP = 14.0, 20.0, 33.8           # bow: cargo control to y 20, the bridge on to the tip
ACEIL, SCEIL, BCEIL = 4.3, 3.5, 4.0
# bow outer keys: y, half width, belly, roof, lower and upper chamfers; the belly rises to the tip (an undercut prow)
BOW = [(14.0, 6.8, -1.8, 4.8, 1.0, 0.8), (20.0, 6.8, -1.8, 4.8, 1.0, 0.8), (25.0, 5.8, -1.6, 4.5, 1.0, 0.8),
       (29.0, 4.3, -1.2, 4.0, 0.9, 0.7), (32.0, 2.7, -0.2, 3.4, 0.6, 0.6), (33.8, 1.4, 0.6, 2.9, 0.4, 0.4)]
WIN0, WIN1 = 1.3, 2.6                      # the bridge window band


def bow_at(y):
    return keyed(BOW, y)


def canopy_glass():
    """Tinted glass; in review renders it lets the sky through."""
    m = K.mat("canopy", "#9fd2e2", rough=0.05, metal=0.0, emit=CYAN, emit_strength=0.05)
    if not K.EXPORT and m.node_tree and not m.get("clear"):
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Transmission Weight"].default_value = 0.85
        b.inputs["IOR"].default_value = 1.05
        m["clear"] = True
    return m


def dash(P, a, b, n, z0, mats, rnd, screen_col=CYAN):
    """A run of dashboard from (x, y) a to b along the window, its front facing n (into the room): a framed body,
    a sloped top with screens and button rows, a sill shelf behind."""
    a, b, n = Vector((*a, 0)), Vector((*b, 0)), Vector((*n, 0)).normalized()
    d = b - a
    L = d.length
    u = d / L
    yaw = math.degrees(math.atan2(u.y, u.x))
    left = Vector((-u.y, u.x, 0))
    tilt = -28 if left.dot(n) > 0 else 28
    c = (a + b) / 2
    gm = M(GUNMETAL, metal=0.5)
    gra = M(GRAPHITE, metal=0.3)
    K.box("dash_body", (L, 0.7, 0.8), gra, loc=(c.x, c.y, z0 + 0.4), rot=(0, 0, yaw), bev=0.03, parent=P)
    K.box("dash_kick", (L, 0.06, 0.12), gm, loc=(*(c + n * 0.36).xy, z0 + 0.06), rot=(0, 0, yaw), bev=0.01, parent=P)
    top = c + n * 0.05
    K.box("dash_top", (L, 0.8, 0.08), gm, loc=(top.x, top.y, z0 + 0.88), rot=(tilt, 0, yaw), bev=0.02, parent=P)
    k = max(1, int(L / 0.9))
    for i in range(k):
        t = (i + 0.5) / k
        p = a + d * t - n * 0.08
        if i % 2 == 0:
            q = p - n * 0.12
            face = math.degrees(math.atan2(n.x, -n.y))
            FR.screen(P, (q.x, q.y, z0 + 1.22), min(0.8, L / k * 0.75), 0.42, gra, glow(screen_col if rnd.random() < 0.7 else "#7dffc9", 0.9), rot=(-12, 0, face))
            K.box("dash_stalk", (0.06, 0.06, 0.3), gm, loc=(q.x, q.y, z0 + 0.95), bev=0, parent=P)
        else:
            for j in range(6):
                q = a + d * (t - 0.35 / k + j * 0.14 / k) + n * 0.12
                col = rnd.choice([MUSTARD, "#c8423a", "#57e39a", BONE, CYAN])
                K.box("dash_button", (0.07, 0.07, 0.03), glow(col, 1.5) if col in ("#57e39a", CYAN) else M(col), loc=(q.x, q.y, z0 + 0.9), rot=(tilt, 0, yaw), bev=0.01, parent=P)
    sh = c - n * 0.3
    K.box("dash_shelf", (L, 0.3, 0.1), gm, loc=(sh.x, sh.y, WIN0 - 0.12), rot=(0, 0, yaw), bev=0.02, parent=P)
    K.box("dash_glow", (L * 0.95, 0.03, 0.03), glow(CYAN, 3), loc=(*(c + n * 0.37).xy, z0 + 0.82), rot=(0, 0, yaw), bev=0, parent=P)


def bridge(P, rnd, mats, lights=True):
    gm = M(GUNMETAL, metal=0.5)
    gra = M(GRAPHITE, metal=0.3)
    trim, dark = mats["trim"], mats["dark"]
    must = M(MUSTARD, wear=0.25)
    DAIS = 0.25
    # the raised helm deck: a dais from y 27.2 to the nose, one step, mustard nosing
    hw0, hw1 = bow_at(27.2)[0] - 0.32, bow_at(30.85)[0] - 0.32
    bm = bmesh.new()
    pts = [(-hw0, 27.2), (hw0, 27.2), (bow_at(29)[0] - 0.32, 29.0), (hw1, 30.85), (-hw1, 30.85), (-bow_at(29)[0] + 0.32, 29.0)]
    bot = [bm.verts.new((x, y, 0.0)) for x, y in pts]
    top = [bm.verts.new((x, y, DAIS)) for x, y in pts]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    d = K.from_bm("dais", bm, mats["floor"])
    d.parent = P
    K.box("dais_nosing", (2 * hw0, 0.08, 0.04), must, loc=(0, 27.22, DAIS + 0.01), bev=0, parent=P)
    K.box("dais_step", (2.4, 0.4, DAIS / 2), mats["floor"], loc=(0, 27.0, DAIS / 4), bev=0.01, parent=P)
    K.box("step_nosing", (2.4, 0.06, 0.03), must, loc=(0, 26.82, DAIS / 2), bev=0, parent=P)
    # the wraparound dash under the window band: port side, nose, starboard side
    y_a, y_b, y_c = 27.4, 29.3, 30.45
    for s in (-1, 1):
        a = (s * (bow_at(y_a)[0] - 0.75), y_a)
        b = (s * (bow_at(y_b)[0] - 0.75), y_b)
        c = (s * 1.2, y_c)
        nab = Vector((-(b[1] - a[1]), b[0] - a[0])) * (-s)
        dash(P, a, b, nab.normalized().xy, DAIS, mats, rnd, screen_col="#7dffc9" if s < 0 else CYAN)
        nbc = Vector((-(c[1] - b[1]), c[0] - b[0])) * (-s)
        dash(P, b, c, nbc.normalized().xy, DAIS, mats, rnd)
    dash(P, (-1.2, y_c), (1.2, y_c), (0, -1), DAIS, mats, rnd)
    # helm controls: yoke, throttle quadrant, a pilot's screen pair on a stalk
    K.box("yoke_column", (0.12, 0.12, 0.5), gm, loc=(0, 29.9, DAIS + 0.75), rot=(-30, 0, 0), bev=0.02, parent=P)
    K.torus("yoke", 0.2, 0.035, M(GRAPHITE), loc=(0, 29.7, DAIS + 1.0), rot=(70, 0, 0), arc=200, parent=P).rotation_euler.y = math.radians(-10)
    for k in range(3):
        K.box("throttle", (0.05, 0.05, 0.28), M(MUSTARD if k == 1 else BONE), loc=(0.75 + k * 0.1, 29.9, DAIS + 1.05), rot=(-15 - k * 8, 0, 0), bev=0.01, parent=P)
    K.box("throttle_base", (0.42, 0.3, 0.1), gm, loc=(0.85, 29.95, DAIS + 0.9), bev=0.02, parent=P)
    for x in (-0.45, 0.45):
        FR.screen(P, (x, 30.05, DAIS + 1.35), 0.6, 0.38, gra, glow(CYAN if x < 0 else "#ffb347", 2.2), rot=(-15, 0, 0))
    K.box("screen_stalk", (0.08, 0.08, 0.3), gm, loc=(0, 30.15, DAIS + 1.1), bev=0.01, parent=P)
    # seats on the dais: pilot at the nose, two officers on the wings, facing their part of the dash
    chair(P, (0, 29.2, DAIS), yaw=0, col="#3e6a8c")
    for s in (-1, 1):
        chair(P, (s * 2.7, 28.3, DAIS), yaw=-s * 55)
    # overhead panel above the pilot: a slab of switches hung from the ceiling on two rods
    zc = min(BCEIL, bow_at(29.0)[2] - 0.7)
    K.box("overhead", (1.8, 0.9, 0.12), gra, loc=(0, 29.6, zc - 0.55), rot=(20, 0, 0), bev=0.03, parent=P)
    for k in range(18):
        col = rnd.choice(["#57e39a", "#ffb347", CYAN, "#c8423a", BONE])
        K.box("ovh_switch", (0.06, 0.08, 0.04), glow(col, 2) if col != BONE else M(BONE), loc=(-0.75 + (k % 9) * 0.19, 29.4 + (k // 9) * 0.3, zc - 0.63 + (k // 9) * 0.1), rot=(20, 0, 0), bev=0.01, parent=P)
    for x in (-0.7, 0.7):
        K.box("ovh_rod", (0.05, 0.05, 0.5), gm, loc=(x, 29.6, zc - 0.25), bev=0, parent=P)
    # the captain's chair on a pedestal, armrest screens
    K.cyl("captain_plinth", 0.6, 0.12, gm, loc=(0, 25.4, 0.06), bev=0.03, parent=P)
    chair(P, (0, 25.4, 0.12), yaw=0, col="#2c3036", big=True)
    # nav table: the island's map as a raised holo relief, a beacon ring at the crash-site-to-be
    FR.framed_box(P, "nav_table", (0, 22.6, 0.5), (2.2, 1.3, 1.0), gra, gm, fw=0.07)
    K.box("nav_holo", (1.9, 1.0, 0.03), glow("#0f5a78", 1.0), loc=(0, 22.6, 1.02), bev=0.01, parent=P)
    for k in range(26):
        x, y = rnd.uniform(-0.8, 0.8), rnd.uniform(-0.4, 0.4)
        h = 0.03 + 0.12 * max(0.0, 1 - math.hypot(x / 0.8, y / 0.4)) * rnd.uniform(0.5, 1.2)
        K.box("holo_relief", (0.12, 0.12, h), glow("#5fd0ff", 1.4), loc=(x, 22.6 + y, 1.04 + h / 2), bev=0, parent=P)
    K.torus("holo_ring", 0.12, 0.012, glow("#ffb347", 5), loc=(0.35, 22.75, 1.2), parent=P)
    K.cyl("holo_emitter", 0.05, 0.6, gm, loc=(0, 22.6, 2.9), bev=0, parent=P)
    # wall stations down both sides: screens, a cabinet, the Logbook lectern, an extinguisher
    for s in (-1, 1):
        x = s * (6.8 - 0.32)
        for k, y in enumerate((21.4, 23.6)):
            hw = bow_at(y)[0] - 0.32 if y > BW1 else 6.48
            ang = 90 - s * 11.3 if y > BW1 else 90
            K.box("wall_screen_frame", (1.62, 0.06, 0.97), gra, loc=(s * (hw - 0.04), y, 2.1), rot=(0, 0, ang), bev=0.02, parent=P)
            K.box("wall_screen", (1.5, 0.04, 0.85), glow(rnd.choice([CYAN, "#7dffc9"]), 1.6), loc=(s * (hw - 0.09), y, 2.1), rot=(0, 0, ang), bev=0.005, parent=P)
        K.box("floor_strip", (0.05, 9.5, 0.02), glow(CYAN, 2), loc=(s * 1.3, 25.5, 0.03), bev=0, parent=P)
    K.box("lectern", (0.5, 0.45, 1.05), gm, loc=(-4.9, 21.0, 0.52), bev=0.04, parent=P)
    K.box("lectern_top", (0.6, 0.5, 0.06), gra, loc=(-4.9, 20.98, 1.1), rot=(-20, 0, 0), bev=0.02, parent=P)
    K.box("logbook", (0.36, 0.46, 0.07), M(MUSTARD, wear=0.2), loc=(-4.9, 20.98, 1.16), rot=(-20, 0, 12), bev=0.02, parent=P)
    FR.framed_box(P, "bridge_cab", (5.6, 21.2, 0.8), (1.2, 1.8, 1.6), M(STEEL, metal=0.3), gm, fw=0.06)
    K.cyl("extinguisher", 0.11, 0.6, M("#c8423a", wear=0.2), loc=(6.25, 22.6, 0.75), segs=12, bev=0.02, parent=P)
    K.box("helmet_shelf", (0.4, 1.2, 0.05), gm, loc=(6.25, 21.2, 2.0), bev=0.01, parent=P)
    for k in range(2):
        K.sphere("helmet", 0.17, M(BONE, wear=0.3), loc=(6.25, 20.85 + k * 0.6, 2.18), parent=P)
    K.box("mug_bridge", (0.08, 0.08, 0.1), M(MUSTARD), loc=(1.6, 29.8, DAIS + 1.03), bev=0.02, parent=P)
    # ceiling: cable tray and a roof hatch
    K.box("bridge_tray", (0.4, 9.0, 0.08), gm, loc=(-1.8, 25.0, BCEIL - 0.3), bev=0.01, parent=P)
    K.cyl("roof_hatch_in", 0.45, 0.1, trim, loc=(2.6, 21.6, BCEIL - 0.05), segs=16, bev=0.02, parent=P)
    if lights:
        ceiling_light(P, (0, 22.6), BCEIL)
        ceiling_light(P, (-2.6, 25.5), BCEIL - 0.1, length=1.0)
        ceiling_light(P, (2.6, 25.5), BCEIL - 0.1, length=1.0)
        K.point("dash_glow_pt", (0, 29.6, 1.4), "#7fd6ff", 60, 0.8).parent = P
        K.point("bridge_fill", (0, 26.0, 3.2), "#e2f5ff", 250, 1.5).parent = P


def cargo_ship(o=(0, 0, 0), yaw=0, name="sv04", palette="dark", seed=7, lights=True, spine_break=None, keep=None):
    """The ship under an empty. spine_break (a y) builds the spine in two pieces; keep (container codes) leaves the
    other containers out. CONTAINERS lists all sixteen afterwards: code, colour, rack position, tier."""
    rnd = random.Random(seed)
    ROOF.clear()
    ROOMS.clear()
    pal = PALETTES[palette]
    mats = {k: M(v, metal=0.3, rough=0.5, wear=0.25, grime=0.15) for k, v in pal.items()}
    mats["trim"] = M(pal["trim"], metal=0.45, rough=0.45, wear=0.3)
    hull, trim, dark, inner, floor = (mats[k] for k in ("hull", "trim", "dark", "inner", "floor"))
    gm = M(GUNMETAL, metal=0.5, rough=0.45)
    cu = K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)
    must = M(MUSTARD, wear=0.25)
    haz_k = K.mat("hazk", "#15171a", rough=0.6)
    grate = K.mat("grate", "#24282d", rough=0.8, metal=0.4)
    cyan = glow(CYAN, 3)
    glass = K.mat("porthole", "#1f4f63", rough=0.06, metal=0.1, emit="#9fdcff", emit_strength=0.5)
    red_l, green_l = glow("#ff5a4a", 6), glow("#57e39a", 6)

    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))

    # ============================================================ aft block: y -30 .. -12
    aft_keys = [(A0, 7.0, -1.6, 4.6, 1.0, 0.9), (A0 + 1.5, AHW, -2.0, 4.9, 1.2, 0.9), (A1, AHW, -2.0, 4.9, 1.2, 0.9)]
    outer = [(y, octagon(*keyed(aft_keys, y))) for y in (A0, A0 + 1.5, A1)]
    inner_s = [(y, octagon(AHW - 0.25, 0.0, ACEIL, 0.05, 0.4)) for y in (A0 + 0.25, A1 - 0.25)]
    port = [(-AHW, y) for y in (-22.5, -20.0, -13.6)]
    stbd = [(AHW, y) for y in (-22.5, -20.0, -16.8)]
    cuts = [door_cut(root, s * AHW, -15.0, w=1.6, h=2.4, axis="x") for s in (-1, 1)]
    cuts += [cutter(root, (x, y, 2.1), (1.0, 1.1, 0.7)) for x, y in port + stbd]
    cuts.append(door_cut(root, 0, A1, w=1.6, h=2.5))
    for s in (-1, 1):   # machinery bays in the belly's front corners, beside the lower containers
        cuts.append(cutter(root, (s * 6.4, A1 + 0.6, -1.0), (2.2, 2.0, 1.6)))
    shell(root, "aft_hull", outer, inner_s, hull, cuts)
    for x, y in port + stbd:
        s = 1 if x > 0 else -1
        K.box("porthole", (0.05, 1.0, 0.6), glass, loc=(x - s * 0.12, y, 2.1), bev=0, parent=root)
        rim = K.box("porthole_rim", (0.14, 1.4, 1.0), trim, loc=(x + s * 0.05, y, 2.1), bev=0.04, parent=root)
        cut(rim, cutter(root, (x + s * 0.05, y, 2.1), (0.4, 1.0, 0.6)))
    # interior: engine room wall, the corridor and the four rooms off it
    wall(root, "engine_bulk", (-AHW, -24.0), (AHW, -24.0), ACEIL, inner, doors=[door_cut(root, 0, -24.0, w=1.8, h=2.6)])
    door_frame(root, 0, -24.0, 1.8, 2.6, trim, lamp=glow("#ffb347", 3))
    for side in (-1, 1):
        wall(root, f"corr{side}", (side * 1.3, -24.0), (side * 1.3, A1), ACEIL, inner,
             doors=[door_cut(root, side * 1.3, -21.0, w=1.2, h=2.3, axis="x"), door_cut(root, side * 1.3, -15.0, w=1.2, h=2.3, axis="x")])
        for y in (-21.0, -15.0):
            door_frame(root, side * 1.3, y, 1.2, 2.3, trim, axis="x", lamp=glow(CYAN, 2))
        wall(root, f"cross{side}", (side * 1.3, -18.0), (side * AHW, -18.0), ACEIL, inner)
    sign(root, "ENGINE ROOM", (0, -23.88, 2.95), (0, 0, 180))
    sign(root, "ENGINEERING", (-1.42, -21.0, 2.75), (0, 0, -90), size=0.16)
    sign(root, "MEDBAY", (-1.42, -15.0, 2.75), (0, 0, -90), size=0.16)
    sign(root, "CREW", (1.42, -21.0, 2.75), (0, 0, 90), size=0.16)
    sign(root, "MESS", (1.42, -15.0, 2.75), (0, 0, 90), size=0.16)
    sign(root, "CARGO SPINE", (0, A1 - 0.27, 2.95), (0, 0, 180))
    door_frame(root, 0, A1 - 0.1, 1.6, 2.5, trim, lamp=glow(CYAN, 3))
    K.box("aft_deck", (2 * AHW - 0.5, A1 - A0 - 0.5, 0.04), floor, loc=(0, (A0 + A1) / 2, 0.02), bev=0, parent=root)
    for k in range(17):
        K.box("corr_grate", (2.2, 0.05, 0.03), grate, loc=(0, -23.5 + k * 0.7, 0.05), bev=0, parent=root)

    hull_ribs(root, AHW - 0.25, A0 + 0.5, A1 - 0.4, ACEIL, trim, dark, skip=(-22.5, -20.0, -16.8, -15.0, -13.6))
    for x, y, c in ((1.48, -16.0, "#9fe3ff"), (-7.12, -17.0, "#7dffc9")):   # wall screens: the mess's news feed, the medbay's vitals
        FR.screen(root, (x, y, 2.3), 1.2, 0.7, M(GRAPHITE), glow(c, 1.4), rot=(0, 0, 90))

    # ---- engine room (y -30..-24): the reactor and the three engine feeds
    ROOMS["Engine room"] = (0, -27.0)
    K.cyl("reactor", 1.3, 3.2, M(GRAPHITE, metal=0.4), loc=(0, -27.0, 1.7), bev=0.06, parent=root)
    for k in range(6):
        K.torus("reactor_coil", 1.36, 0.08, cu, loc=(0, -27.0, 0.6 + k * 0.42), parent=root)
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        K.box("reactor_window", (0.5, 0.05, 1.6), glow(CYAN, 4), loc=(1.33 * math.cos(ang), -27.0 + 1.33 * math.sin(ang), 1.7), rot=(0, 0, math.degrees(ang) + 90), bev=0, parent=root)
    K.cyl("reactor_cap", 1.45, 0.3, gm, loc=(0, -27.0, 3.4), bev=0.04, parent=root)
    K.cyl("reactor_stack", 0.5, 0.9, gm, loc=(0, -27.0, 3.9), bev=0.03, parent=root)
    for x in (-4.4, 4.4):
        FR.framed_box(root, "feed", (x, -27.6, 1.3), (2.4, 3.8, 2.6), M(STEEL, metal=0.3), gm, fw=0.1, screws=gm)
        FR.vent(root, (x, -25.68, 1.5), 1.6, 1.4, gm, M(GRAPHITE))
        K.cyl("feed_pipe", 0.2, abs(x) - 2.4, cu, loc=(x / 2, -27.0, 3.0), rot=(0, 90, 0), segs=12, bev=0, parent=root)
    for x in (-1.9, 1.9):
        K.box("catwalk_rail", (0.06, 5.0, 0.06), must, loc=(x, -27.0, 1.05), bev=0, parent=root)
        for y in (-29.0, -27.0, -25.0):
            K.box("catwalk_post", (0.06, 0.06, 1.05), must, loc=(x, y, 0.52), bev=0, parent=root)
    for k in range(10):
        K.box("haz_floor", (0.36, 0.3, 0.02), must if k % 2 == 0 else haz_k, loc=(-1.8 + k * 0.4, -24.35, 0.05), rot=(0, 0, 35), bev=0, parent=root)
    if lights:
        K.point("reactor_glow", (0, -27.0, 2.0), CYAN, 260, 0.8).parent = root
        ceiling_light(root, (-4.4, -27.0), ACEIL)
        ceiling_light(root, (4.4, -27.0), ACEIL)

    # ---- engineering (port aft): the Power Core with the Master Battery's empty bay, a workbench and tool wall
    ROOMS["Engineering"] = (-4.4, -21.0)
    shelf(root, (-4.0, -18.35, 0), 2.6, 180, rnd)
    FR.framed_box(root, "power_core", (-4.6, -23.2, 1.3), (2.2, 1.2, 2.6), M(GRAPHITE, metal=0.3), gm, fw=0.1, screws=gm)
    FR.screen(root, (-4.6, -22.58, 2.1), 1.2, 0.5, M(GRAPHITE), glow("#ff6a4d", 1.6))   # dead: no battery
    K.box("battery_bay", (0.9, 0.5, 0.9), K.mat("bay_hole", "#0b0c0e", rough=0.9), loc=(-4.6, -22.62, 0.9), bev=0.02, parent=root)
    FR.hazard(root, (-4.6, -22.56, 1.45), 1.2, 0.1, must, haz_k, n=8)
    for x in (-6.2, -3.0):
        K.cyl("core_coil_core", 0.22, 2.2, gm, loc=(x, -23.3, 1.1), bev=0.02, parent=root)
        for k in range(9):
            K.torus("core_coil", 0.27, 0.05, cu, loc=(x, -23.3, 0.3 + k * 0.2), parent=root)
    for k in range(3):
        K.cyl("core_pipe", 0.11, 5.4, cu if k == 1 else gm, loc=(-7.0, -21.0, 3.2 + k * 0.3), rot=(90, 0, 0), segs=12, bev=0, parent=root)
    table(root, (-6.6, -19.6, 0), (1.4, 1.6))
    K.box("vice", (0.3, 0.2, 0.2), M("#3e6a8c", metal=0.4), loc=(-6.7, -19.1, 0.92), bev=0.03, parent=root)
    K.box("toolboard", (0.06, 2.4, 1.2), M(GRAPHITE), loc=(-7.2, -19.6, 2.0), bev=0.02, parent=root)
    for k in range(7):
        K.box("tool", (0.05, 0.08, rnd.uniform(0.3, 0.6)), M(rnd.choice([MUSTARD, "#c8423a", STEEL, GUNMETAL])), loc=(-7.14, -20.6 + k * 0.32, 2.0), bev=0.01, parent=root)
    crate(root, (-2.3, -19.0, 0), (0.8, 0.8, 0.8), col="#3d79b3")
    crate(root, (-2.3, -19.0, 0.8), (0.6, 0.6, 0.5), col="#d9ae2f")
    if lights:
        ceiling_light(root, (-4.4, -21.0), ACEIL)

    # ---- medbay (port fore)
    ROOMS["Medbay"] = (-4.4, -15.0)
    K.box("medbed", (0.9, 2.0, 0.12), M("#d6d2c4", wear=0.1), loc=(-5.6, -16.2, 0.85), bev=0.05, parent=root)
    K.box("medbed_base", (0.6, 1.4, 0.75), gm, loc=(-5.6, -16.2, 0.4), bev=0.03, parent=root)
    K.box("med_scanner", (1.1, 0.5, 0.15), M(GRAPHITE), loc=(-5.6, -16.2, 2.4), bev=0.04, parent=root)
    K.box("med_scanner_glow", (0.9, 0.36, 0.03), glow("#7dffc9", 2), loc=(-5.6, -16.2, 2.32), bev=0, parent=root)
    K.box("med_arm", (0.1, 0.1, 1.8), gm, loc=(-5.6, -16.2, 3.35), bev=0.02, parent=root)
    FR.framed_box(root, "med_cabinet", (-6.95, -12.9, 1.1), (0.6, 0.9, 2.0), M(BONE, wear=0.2), gm, fw=0.05)
    K.box("med_cross_v", (0.04, 0.12, 0.4), glow("#57e39a", 2), loc=(-6.63, -12.9, 1.6), bev=0, parent=root)
    K.box("med_cross_h", (0.04, 0.4, 0.12), glow("#57e39a", 2), loc=(-6.63, -12.9, 1.6), bev=0, parent=root)
    console(root, (-2.6, -16.6, 0), yaw=-90, w=1.2, screen_col="#7dffc9")
    if lights:
        ceiling_light(root, (-4.4, -15.0), ACEIL)

    # ---- crew quarters (starboard aft): four bunks, lockers, a table
    ROOMS["Crew quarters"] = (4.4, -21.0)
    shelf(root, (3.6, -18.35, 1.4), 1.6, 180, rnd, levels=2, h=0.9)
    bunk(root, (6.0, -23.2, 0), yaw=0)
    bunk(root, (6.0, -18.9, 0), yaw=0)
    locker_row(root, (3.2, -23.45, 0), 3, yaw=0)
    table(root, (3.6, -20.4, 0), (1.0, 1.0))
    chair(root, (3.0, -20.4, 0), yaw=90, col="#3e6a8c")
    K.box("poster", (0.03, 0.7, 0.9), M("#c8423a", wear=0.2), loc=(AHW - 0.28, -21.25, 3.2), bev=0, parent=root)
    K.box("poster2", (0.03, 0.5, 0.7), M("#e0b530", wear=0.2), loc=(AHW - 0.28, -21.25, 1.1), bev=0, parent=root)
    if lights:
        ceiling_light(root, (4.4, -21.0), ACEIL)

    # ---- mess (starboard fore): table and benches, galley counter, fridge, coffee machine
    ROOMS["Mess"] = (4.4, -15.0)
    shelf(root, (4.6, -17.7, 0), 2.6, 0, rnd)
    table(root, (4.2, -15.6, 0), (1.0, 2.4))
    for dx in (-0.85, 0.85):
        bench(root, (4.2 + dx, -15.6, 0), 2.2, yaw=90)
    FR.framed_box(root, "galley", (2.1, -13.2, 0.5), (1.4, 1.6, 1.0), M(BONE, wear=0.2), gm, fw=0.06)
    K.box("galley_top", (1.5, 1.7, 0.06), M("#7a8088", metal=0.5), loc=(2.1, -13.2, 1.03), bev=0.01, parent=root)
    FR.framed_box(root, "fridge", (2.1, -17.3, 1.0), (1.0, 0.9, 2.0), M("#d6d2c4", wear=0.15), gm, fw=0.05)
    K.box("coffee", (0.3, 0.3, 0.4), M("#c8423a", wear=0.2), loc=(2.0, -12.9, 1.26), bev=0.04, parent=root)
    for k in range(4):
        K.cyl("mug", 0.05, 0.1, M(rnd.choice([BONE, "#3e6a8c", MUSTARD])), loc=(4.2 + rnd.uniform(-0.3, 0.3), -15.6 + rnd.uniform(-1.0, 1.0), 0.87), segs=10, bev=0, parent=root)
    if lights:
        ceiling_light(root, (4.4, -15.0), ACEIL)
        ceiling_light(root, (0, -18.0), ACEIL, length=1.0)

    # ---- aft exterior: armour plates, stripes, labels, doors; machinery bays; housings; engines; tanks; legs
    for side in (-1, 1):
        x = side * AHW
        doors = [(-16.0, -14.0)]
        holes = [(y - 0.7, y + 0.7) for _, y in (port if side < 0 else stbd)] + doors
        plates(root, side, x, A0 + 1.6, A1, -0.75, 1.55, mats, rnd, skip=doors)
        plates(root, side, x, A0 + 1.6, A1, 2.6, 3.95, mats, rnd, skip=holes)
        K.box("stripe", (0.04, A1 - A0 - 2.0, 0.22), must, loc=(x + side * 0.08, (A0 + A1) / 2 + 0.6, 2.45), bev=0, parent=root)
        K.box("run_light", (0.08, 0.6, 0.12), green_l if side > 0 else red_l, loc=(x + side * 0.1, A1 - 0.5, 4.0), bev=0.02, parent=root)
        FR.label(root, "SALVAGE FREIGHTER   SV-04", (x + side * 0.1, -25.0, 1.95), 0.32, M("#2c3036", wear=0.2), rot=(90, 0, 90 * side))
        door_frame(root, x, -15.0, 1.6, 2.4, trim, axis="x", lamp=glow(CYAN, 3))
        K.box("door_step", (0.8, 1.8, 0.14), trim, loc=(x + side * 0.4, -15.0, -0.1), bev=0.02, parent=root)
        FR.hazard(root, (x + side * 0.1, -15.0, 2.75), 1.8, 0.15, must, haz_k, n=8, axis="x")
        greebles(root, side, x + side * 0.06, A0 + 2, A1 - 0.5, -1.35, mats, rnd, n=6)
        # the machinery bay: pumps and pipework in the belly's front corner
        bx = side * 6.4
        for k in range(2):
            K.cyl("bay_pump", 0.45, 1.6, gm, loc=(bx + side * (0.5 - k * 0.9), A1 + 0.4, -1.0), rot=(90, 0, 0), segs=16, bev=0.03, parent=root)
            K.torus("bay_ring", 0.48, 0.05, cu, loc=(bx + side * (0.5 - k * 0.9), A1 + 1.05, -1.0), rot=(90, 0, 0), parent=root)
        K.cyl("bay_pipe", 0.1, 2.0, cu, loc=(bx, A1 + 0.9, -0.35), rot=(0, 90, 0), segs=10, bev=0, parent=root)
        K.box("bay_lamp", (0.4, 0.1, 0.08), glow("#ffb347", 3), loc=(bx, A1 - 0.32, -0.3), bev=0.01, parent=root)
        # dorsal engine housing (the reference's tall aft blocks)
        hx = side * 4.3
        hk = [(-30.4, 1.8, 4.6, 8.4, 0.3, 0.6), (-21.0, 1.8, 4.6, 8.4, 0.3, 0.6), (-17.0, 1.6, 4.6, 6.0, 0.3, 0.5)]
        hs = loft(f"housing{side}", [(y, octagon(*keyed(hk, y))) for y in (-30.4, -21.0, -17.0)], hull, root)
        hs.location.x = hx
        ROOF.append(hs)
        for y in (-29.4, -26.4, -23.4):
            ROOF.append(K.box("housing_band", (3.8, 0.3, 0.22), trim, loc=(hx, y, 8.45), bev=0.04, parent=root))
        for k in range(6):
            ROOF.append(K.box("radiator_fin", (0.1, 0.9, 1.6), trim, loc=(hx + side * 1.85, -27.8 + k * 1.1, 6.6), bev=0.02, parent=root))
        ROOF.append(K.box("radiator_back", (0.04, 6.6, 1.8), dark, loc=(hx + side * 1.8, -25.05, 6.6), bev=0, parent=root))
        ROOF.append(K.box("housing_stripe", (0.04, 5.0, 0.3), must, loc=(hx + side * 1.82, -20.2, 7.7), bev=0, parent=root))
        ROOF.append(FR.label(root, "SV-04", (hx + side * 1.82, -19.6, 6.3), 0.9, M("#2c3036", wear=0.2), rot=(90, 0, 90 * side)))
        ROOF.append(K.box("intake", (2.6, 0.7, 1.0), dark, loc=(hx, -17.8, 5.5), rot=(-50, 0, 0), bev=0.04, parent=root))
        for k in range(4):
            ROOF.append(K.box("intake_slat", (2.4, 0.06, 0.6), trim, loc=(hx, -17.6 + 0.0, 5.2 + k * 0.22), rot=(-50, 0, 0), bev=0, parent=root))
        ROOF.append(K.box("housing_top", (2.4, 5.0, 0.3), mats["hull2"], loc=(hx, -25.0, 8.55), bev=0.05, parent=root))
        for k in range(2):
            ROOF.append(K.box("exhaust", (0.9, 1.1, 0.6), dark, loc=(hx + (k - 0.5) * 1.2, -30.6, 7.6), bev=0.05, parent=root))
        ROOF.append(K.box("roof_rail", (0.1, A1 - A0 - 1.5, 0.1), trim, loc=(side * 6.3, (A0 + A1) / 2 + 0.7, 4.85), bev=0, parent=root))
    # landing pad between the housings, comm mast and dish
    ROOF.append(K.box("pad", (4.6, 6.0, 0.3), trim, loc=(0, -24.0, 5.0), bev=0.05, parent=root))
    ring = K.torus("pad_ring", 1.6, 0.08, must, loc=(0, -24.0, 5.17), parent=root)
    ring.scale.z = 0.3
    ROOF.append(ring)
    for k in range(6):
        a = k * math.pi / 3
        ROOF.append(K.box("pad_light", (0.2, 0.2, 0.08), cyan, loc=(2.0 * math.cos(a), -24.0 + 2.6 * math.sin(a), 5.18), bev=0.02, parent=root))
    ROOF.append(K.cyl("mast", 0.1, 3.0, trim, loc=(0, -18.5, 6.4), parent=root))
    ROOF.append(K.cyl("dish", 0.8, 0.25, trim, loc=(0, -18.5, 7.9), rot=(30, 0, 0), r2=0.2, parent=root))
    ROOF.append(K.box("mast_light", (0.14, 0.14, 0.14), red_l, loc=(0, -18.5, 8.05), bev=0.02, parent=root))
    for y in (-27.0, -21.0, -15.0):
        ROOF.append(K.box("roof_seam", (2 * AHW - 2.0, 0.1, 0.06), trim, loc=(0, y, 4.92), bev=0.01, parent=root))
    ROOF.append(K.box("roof_hatch", (1.4, 1.4, 0.2), trim, loc=(-3.0, -14.0, 4.95), bev=0.04, parent=root))
    ROOF.append(K.box("roof_hatch", (1.4, 1.4, 0.2), trim, loc=(3.0, -14.0, 4.95), bev=0.04, parent=root))
    # three main engines at the back and the engine frame
    for x in (-4.2, 0.0, 4.2):
        K.cyl("engine_mount", 1.75, 1.0, trim, loc=(x, A0 - 0.4, 1.2), rot=(90, 0, 0), bev=0.05, parent=root)
        K.cyl("engine_bell", 1.15, 2.2, gm, loc=(x, A0 - 1.9, 1.2), rot=(90, 0, 0), r2=1.55, segs=32, bev=0.03, parent=root)
        K.cyl("engine_core", 1.05, 0.1, glow(CYAN, 5), loc=(x, A0 - 1.2, 1.2), rot=(90, 0, 0), bev=0, parent=root)
        K.torus("engine_lip", 1.55, 0.1, trim, loc=(x, A0 - 3.0, 1.2), rot=(90, 0, 0), parent=root)
        for k in range(8):
            a = k * math.pi / 4
            K.box("engine_actuator", (0.12, 1.4, 0.12), dark, loc=(x + 1.45 * math.cos(a), A0 - 1.3, 1.2 + 1.45 * math.sin(a)), bev=0.01, parent=root)
    K.box("engine_frame", (13.0, 0.8, 0.5), trim, loc=(0, A0 - 0.4, 3.3), bev=0.05, parent=root)
    K.box("engine_frame", (13.0, 0.8, 0.5), trim, loc=(0, A0 - 0.4, -0.9), bev=0.05, parent=root)
    # belly tanks with striped end caps
    for side in (-1, 1):
        x = side * 4.3
        K.cyl("tank", 1.25, 8.0, M("#d9d6cf", metal=0.2, wear=0.3), loc=(x, -22.0, -2.9), rot=(90, 0, 0), segs=32, bev=0.05, parent=root)
        for yy in (-25.0, -19.0):
            K.cyl("tank_band", 1.3, 0.3, trim, loc=(x, yy, -2.9), rot=(90, 0, 0), segs=32, bev=0.03, parent=root)
        K.cyl("tank_cap", 1.2, 0.3, M("#c8423a", wear=0.3), loc=(x, -17.9, -2.9), rot=(90, 0, 0), segs=32, bev=0.08, parent=root)
        for k in range(5):
            K.box("tank_haz", (0.22, 0.04, 1.7), must if k % 2 else haz_k, loc=(x - 0.5 + k * 0.25, -17.73, -2.9), rot=(0, 30, 0), bev=0, parent=root)
        K.box("tank_strap", (0.3, 0.5, 0.9), trim, loc=(x, -21.0, -1.9), bev=0.03, parent=root)
    # landing legs, stowed
    for x, y in ((-6.0, -27.5), (6.0, -27.5), (-6.0, -14.5), (6.0, -14.5)):
        K.box("leg_strut", (0.4, 2.2, 0.4), gm, loc=(x, y, -2.3), rot=(25, 0, 0), bev=0.04, parent=root)
        K.cyl("leg_pad", 0.6, 0.2, trim, loc=(x, y - 0.9, -2.85), bev=0.04, parent=root)

    # ============================================================ spine: y -12 .. 14, the cargo corridor
    # In the wreck the spine snaps at the middle rack post (spine_break): every run along it is built per segment.
    segs = [(S0, S1)] if spine_break is None else [(S0, spine_break), (spine_break, S1)]
    posts = [S0 + 0.2 + k * 6.4 for k in range(5)]
    centres = [(posts[k] + posts[k + 1]) / 2 for k in range(4)]
    ROOMS["Cargo spine"] = (0, 1.0)
    for g0, g1 in segs:
        e0, e1 = (g0 - 0.1 if g0 == S0 else g0), (g1 + 0.1 if g1 == S1 else g1)
        outer = [(y, octagon(SHW, -1.4, 4.2, 0.6, 0.5)) for y in (e0, e1)]
        inner_s = [(y, octagon(SHW - 0.2, 0.0, SCEIL, 0.05, 0.3)) for y in (e0 - 0.3, e1 + 0.3)]
        shell(root, "spine_hull", outer, inner_s, hull)
        K.box("spine_deck", (2 * SHW - 0.4, g1 - g0, 0.04), floor, loc=(0, (g0 + g1) / 2, 0.02), bev=0, parent=root)
        y = g0 + 0.35
        while y < g1 - 0.1:
            K.box("spine_grate", (1.6, 0.05, 0.03), grate, loc=(0, y, 0.05), bev=0, parent=root)
            y += 0.7
        ps = [p for p in posts if g0 - 0.3 <= p <= g1 + 0.3]
        for side in (-1, 1):
            rack(root, ps, side * SHW, side * 5.4, -1.6, 4.0, trim, mats)
        hull_ribs(root, SHW - 0.2, g0 + 0.6, g1 - 0.4, SCEIL, trim, dark, step=3.2, sides=False)
        pipe_run(root, -1.9, g0, g1, 3.0, 0.12, cu)
        pipe_run(root, -1.6, g0, g1, 3.15, 0.09, gm)
        K.box("cable_tray", (0.5, g1 - g0, 0.08), gm, loc=(1.8, (g0 + g1) / 2, 3.1), bev=0.01, parent=root)
        L = g1 - g0
        for side in (-1, 1):
            ROOF.append(K.box("gantry_rail", (0.3, L, 0.3), trim, loc=(side * 1.6, (g0 + g1) / 2, 4.4), bev=0.04, parent=root))
            ROOF.append(K.box("gantry_chord", (0.18, L, 0.18), gm, loc=(side * 1.3, (g0 + g1) / 2, 5.6), bev=0.03, parent=root))
            ROOF.append(K.box("spine_stripe", (0.06, L, 0.15), must, loc=(side * 2.2, (g0 + g1) / 2, 4.25), bev=0, parent=root))
        ROOF.append(K.box("gantry_chord", (0.18, L, 0.18), gm, loc=(0, (g0 + g1) / 2, 5.6), bev=0.03, parent=root))
        for k in range(int(L)):
            y = g0 + 0.5 + k
            for side in (-1, 1):
                ROOF.append(K.box("truss_post", (0.1, 0.1, 1.2), gm, loc=(side * 1.3, y, 5.0), bev=0.01, parent=root))
                ROOF.append(K.box("truss_diag", (0.08, 1.0, 0.08), gm, loc=(side * 1.3, y + 0.5, 5.0), rot=(55 if k % 2 else -55, 0, 0), bev=0.01, parent=root))
            ROOF.append(K.box("truss_cross", (2.6, 0.08, 0.08), gm, loc=(0, y, 5.6), bev=0.01, parent=root))
    CONTAINERS.clear()
    codes = iter(f"SV4-{n:03d}" for n in (112, 118, 120, 131, 207, 215, 233, 240, 301, 309, 316, 322, 405, 411, 420, 438))
    for k, (pl, pu, sl, su) in enumerate(RACKS):
        y = centres[k]
        for side, lo, up in ((-1, pl, pu), (1, sl, su)):
            x = side * 3.95
            for tier, col, z in (("lower", lo, -0.3), ("upper", up, 2.35)):
                code = next(codes)
                CONTAINERS.append(dict(code=code, colour=col, at=(x, y, z), tier=tier))
                if keep is None or code in keep:
                    container(root, (x, y, z), col, code, rnd)["tier"] = tier
            hatch_x = side * (SHW - 0.22)
            K.box("hatch", (0.06, 1.4, 2.2), gm, loc=(hatch_x, y, 1.15), bev=0.03, parent=root)
            K.box("hatch_wheel", (0.08, 0.5, 0.5), trim, loc=(hatch_x - side * 0.05, y, 1.2), bev=0.1, parent=root)
            for j, c in enumerate((lo, up)):
                K.box("bay_lamp", (0.06, 0.26, 0.16), glow(CARGO[c][0], 4) if lights else dark, loc=(hatch_x - side * 0.04, y - 0.25 + j * 0.5, 2.55), bev=0.02, parent=root)
            sign(root, f"BAY {k + 1}{'P' if side < 0 else 'S'}", (hatch_x - side * 0.04, y, 2.9), (0, 0, 90 * side), size=0.14)
        if lights:
            ceiling_light(root, (0, y), SCEIL)
    if spine_break is None:
        ROOF.append(K.box("crane", (3.6, 1.4, 0.8), M(MUSTARD, wear=0.35), loc=(0, 3.0, 5.9), bev=0.06, parent=root))
        ROOF.append(K.box("crane_cab", (1.0, 1.0, 0.7), M(GRAPHITE), loc=(1.0, 3.0, 6.5), bev=0.05, parent=root))
        ROOF.append(K.box("crane_glass", (0.7, 0.04, 0.4), glass, loc=(1.0, 3.52, 6.55), bev=0, parent=root))
        ROOF.append(K.cyl("crane_cable", 0.03, 1.6, gm, loc=(-0.8, 3.0, 4.9), segs=6, bev=0, parent=root))
    for y in centres:
        for side in (-1, 1):
            ROOF.append(K.cyl("hex_hatch", 0.8, 0.22, trim, loc=(side * 3.95, y + (1.6 if side > 0 else -1.6), 3.72), segs=6, bev=0.05, parent=root))
            ROOF.append(K.box("lid_strip", (0.25, 5.4, 0.08), dark, loc=(side * 5.25, y, 3.95), bev=0, parent=root))

    # ============================================================ bow: y 14 .. 33.8
    bys = [k[0] for k in BOW]
    outer = [(y, octagon(*bow_at(y))) for y in bys]
    iys = [B0 + 0.25, 20.0, 25.0, 29.0, 30.9]
    inner_s = []
    for y in iys:
        hw, zb, zt, cb, ct = bow_at(y)
        inner_s.append((y, octagon(hw - 0.3, 0.0, min(BCEIL, zt - 0.7), 0.05, 0.4)))
    cuts = [door_cut(root, 6.8, 16.6, w=1.8, h=2.6, axis="x"), door_cut(root, -6.8, 16.6, w=1.4, h=2.3, axis="x"), door_cut(root, 0, B0, w=1.6, h=2.5)]
    band = []
    for y in (26.5, 29.0, 32.0, 35.0):
        hw = bow_at(y)[0] + 1.0
        band.append((y, [(-hw, WIN0), (hw, WIN0), (hw, WIN1), (-hw, WIN1)]))
    wcut = loft("window_cut", band, K.mat("cut", "#ff00ff"), root, bev=0)
    wcut.hide_render = wcut.hide_viewport = True
    cuts.append(wcut)
    for s in (-1, 1):
        cuts.append(cutter(root, (s * 6.1, B0 - 0.4, -1.0), (1.4, 2.0, 1.4)))
    shell(root, "bow_hull", outer, inner_s, hull, cuts)
    # the window glass, mullions round the band, a brow over it and a sill under it
    gl = []
    for y in (26.5, 29.0, 32.0, BTIP):
        hw = bow_at(y)[0] - (0.12 if y < BTIP else 0.4)
        gl.append((y, [(-hw, WIN0), (hw, WIN0), (hw, WIN1), (-hw, WIN1)]))
    canopy_g = canopy_glass()
    g = loft("bridge_glass", gl, canopy_g, root, bev=0)
    gi = []
    for y in (26.3, 29.0, 32.0, BTIP - 0.06):
        hw = bow_at(y)[0] - (0.18 if y < BTIP - 0.1 else 0.46)
        gi.append((y, [(-hw, WIN0 - 0.1), (hw, WIN0 - 0.1), (hw, WIN1 + 0.1), (-hw, WIN1 + 0.1)]))
    gcut = loft("glass_in", gi, K.mat("cut", "#ff00ff"), root, bev=0)
    gcut.hide_render = gcut.hide_viewport = True
    cut(g, gcut)
    for lip, z0, z1, d in (("brow", WIN1, WIN1 + 0.3, 0.25), ("sill", WIN0 - 0.18, WIN0, 0.12)):
        sec = []
        for y in (26.2, 29.0, 32.0, BTIP + d):
            hw = bow_at(min(y, BTIP))[0] + d
            sec.append((y, [(-hw, z0), (hw, z0), (hw, z1), (-hw, z1)]))
        ob = loft(f"window_{lip}", sec, trim, root, bev=0.03)
        hole = []
        for y in (25.9, 29.0, 32.0, BTIP - 0.25):
            hw = bow_at(y)[0] - 0.2
            hole.append((y, [(-hw, z0 - 0.2), (hw, z0 - 0.2), (hw, z1 + 0.2), (-hw, z1 + 0.2)]))
        hc = loft(f"{lip}_hole", hole, K.mat("cut", "#ff00ff"), root, bev=0)
        hc.hide_render = hc.hide_viewport = True
        cut(ob, hc)
    for y in (27.2, 28.4, 29.6, 30.8, 32.0, 33.0):
        hw = bow_at(y)[0]
        for s in (-1, 1):
            K.box("mullion", (0.18, 0.16, WIN1 - WIN0), trim, loc=(s * (hw - 0.05), y, (WIN0 + WIN1) / 2), rot=(0, 0, s * -25 if y > 29 else 0), bev=0.02, parent=root)
    for x in (-0.6, 0.6):
        K.box("mullion", (0.16, 0.2, WIN1 - WIN0), trim, loc=(x, BTIP - 0.3, (WIN0 + WIN1) / 2), bev=0.02, parent=root)
    # interior: the bridge partition, signs, deck
    wall(root, "bridge_bulk", (-6.6, BW1), (6.6, BW1), BCEIL, inner, doors=[door_cut(root, 0, BW1, w=1.8, h=2.5)])
    door_frame(root, 0, BW1, 1.8, 2.5, trim, lamp=glow(CYAN, 3))
    sign(root, "BRIDGE", (0, BW1 - 0.13, 2.95), (0, 0, 180))
    sign(root, "CARGO CONTROL", (0, S1 + 0.08, 2.95), (0, 0, 0))
    door_frame(root, 0, B0 + 0.1, 1.6, 2.5, trim, lamp=glow(CYAN, 3))
    bm = bmesh.new()
    pts = [(-6.4, B0 + 0.3), (6.4, B0 + 0.3), (6.4, BW1), (bow_at(25)[0] - 0.35, 25.0), (bow_at(29)[0] - 0.35, 29.0),
           (bow_at(30.8)[0] - 0.35, 30.8), (-bow_at(30.8)[0] + 0.35, 30.8), (-bow_at(29)[0] + 0.35, 29.0), (-bow_at(25)[0] + 0.35, 25.0), (-6.4, BW1)]
    bm.faces.new([bm.verts.new((x, y, 0.03)) for x, y in pts])
    K.from_bm("bow_deck", bm, floor).parent = root

    hull_ribs(root, 6.5, B0 + 0.6, BW1 - 0.2, BCEIL, trim, dark, step=2.0, skip=(16.6,))
    for y in (21.5, 23.5, 25.5):
        hw = bow_at(y)[0] - 0.3
        K.box("ceiling_beam", (2 * hw, 0.25, 0.25), trim, loc=(0, y, min(BCEIL, bow_at(y)[2] - 0.7) - 0.15), bev=0.03, parent=root)

    # ---- cargo control (y 14..20): the airlock, the manifest board, EVA suits
    ROOMS["Cargo control"] = (0, 17.0)
    shelf(root, (2.6, 14.5, 0), 3.0, 180, rnd)
    FR.framed_box(root, "manifest", (-3.6, 19.75, 2.0), (3.6, 0.2, 1.6), M(GRAPHITE), gm, fw=0.07)
    for k, (pl, pu, sl, su) in enumerate(RACKS):  # a lamp per container, in rack order (port pair, starboard pair)
        for j, c in enumerate((pu, pl, su, sl)):
            K.box("manifest_lamp", (0.26, 0.05, 0.22), glow(CARGO[c][0], 3), loc=(-5.0 + k * 0.82 + (j // 2) * 0.34, 19.62, 2.45 - (j % 2) * 0.32), bev=0.01, parent=root)
    FR.label(root, "MANIFEST", (-3.6, 19.62, 1.5), 0.18, M(BONE, wear=0.2), rot=(90, 0, 0))
    console(root, (-3.6, 18.4, 0), yaw=180, w=2.0)
    chair(root, (-3.6, 17.3, 0), yaw=0)
    door_frame(root, 6.45, 16.6, 1.8, 2.6, trim, axis="x", lamp=glow("#ffb347", 4))
    for k in range(8):
        K.box("airlock_haz", (0.8, 0.22, 0.02), must if k % 2 == 0 else haz_k, loc=(5.9, 15.75 + k * 0.24, 0.05), rot=(0, 0, 35), bev=0, parent=root)
    sign(root, "AIRLOCK", (6.3, 16.6, 3.05), (0, 0, 90), size=0.18)
    for y in (14.9, 15.8):
        FR.framed_box(root, "suit_locker", (-6.05, y, 1.1), (0.7, 0.85, 2.2), M(STEEL, metal=0.3), gm, fw=0.05)
        K.box("suit_visor", (0.03, 0.4, 0.3), glow(CYAN, 1.5), loc=(-5.69, y, 1.75), bev=0.02, parent=root)
    crate(root, (3.8, 18.8, 0), (1.0, 0.8, 0.7))
    crate(root, (4.8, 18.9, 0), (0.6, 0.6, 0.5), col="#5c8f47")
    if lights:
        ceiling_light(root, (0, 17.0), BCEIL)
        ceiling_light(root, (-3.6, 17.0), BCEIL, length=1.0)

    # ---- bridge (y 20..31): a raised helm deck behind a wraparound dash, overhead panel, captain's chair, nav
    # table with the island's map, wall stations and the Logbook
    ROOMS["Bridge"] = (0, 25.0)
    bridge(root, rnd, mats, lights)

    # ---- bow exterior: plates, retro thruster pods, foredeck, mast, stripes, labels
    for side in (-1, 1):
        x = side * 6.8
        plates(root, side, x, B0 + 0.1, BW1, -0.65, 1.55, mats, rnd, skip=[(15.4, 17.8)])
        plates(root, side, x, B0 + 0.1, BW1, 2.75, 3.95, mats, rnd)
        K.box("bow_stripe", (0.04, BW1 - B0 - 0.4, 0.22), must, loc=(x + side * 0.08, (B0 + BW1) / 2, 2.45), bev=0, parent=root)
        K.box("bow_run", (0.08, 0.6, 0.12), green_l if side > 0 else red_l, loc=(x + side * 0.1, B0 + 0.5, 4.0), bev=0.02, parent=root)
        door_frame(root, x, 16.6, 1.8 if side > 0 else 1.4, 2.6 if side > 0 else 2.3, trim, axis="x", lamp=glow(CYAN, 3) if side > 0 else None)
        K.box("airlock_step", (0.9, 1.8, 0.14), trim, loc=(x + side * 0.5, 16.6, -0.1), bev=0.02, parent=root)
        FR.hazard(root, (x + side * 0.1, 16.6, 2.95), 2.0, 0.15, must, haz_k, n=8, axis="x")
        # retro thruster pod: a tapered nacelle under the shoulder with three nozzles facing forward
        px = side * 7.7
        pod = loft(f"pod{side}", [(13.6, octagon(0.8, -0.9, 0.7, 0.3, 0.3)), (15.0, octagon(1.0, -1.2, 0.9, 0.35, 0.35)),
                                  (21.0, octagon(1.0, -1.2, 0.9, 0.35, 0.35)), (22.0, octagon(0.9, -1.0, 0.8, 0.3, 0.3))], hull, root)
        pod.location.x = px
        K.box("pod_band", (2.1, 0.25, 2.2), trim, loc=(px, 15.2, -0.15), bev=0.04, parent=root)
        K.box("pod_band", (2.1, 0.25, 2.2), trim, loc=(px, 20.8, -0.15), bev=0.04, parent=root)
        K.box("pod_pylon", (1.0, 4.0, 0.7), trim, loc=(side * 6.9, 18.0, 0.4), bev=0.04, parent=root)
        for k in range(3):
            xx = px + (k - 1) * 0.55
            K.cyl("retro", 0.24, 0.6, gm, loc=(xx, 22.2, -0.15), rot=(90, 0, 0), segs=16, bev=0.02, parent=root)
            K.cyl("retro_core", 0.16, 0.05, glow(CYAN, 2), loc=(xx, 22.52, -0.15), rot=(90, 0, 0), segs=16, bev=0, parent=root)
        FR.label(root, "SV-04", (side * (bow_at(23.0)[0] + 0.02), 23.0, 0.35), 0.75, M("#2c3036", wear=0.2), rot=(90, 0, side * (90 + 11)))
        # belly machinery bays at the back of the bow (beside the lower containers)
        K.cyl("bay_pump", 0.4, 1.4, gm, loc=(side * 6.1, B0 - 0.5, -1.0), rot=(90, 0, 0), segs=16, bev=0.03, parent=root)
        K.box("bay_lamp", (0.4, 0.1, 0.08), glow("#ffb347", 3), loc=(side * 6.1, B0 + 0.62, -0.4), bev=0.01, parent=root)
    for side in (-1, 1):
        for ya, yb in ((20.0, 25.0), (25.0, 29.0)):
            ha, hb = bow_at(ya)[0], bow_at(yb)[0]
            plates_line(root, (side * ha, ya + 0.1), (side * hb, yb - 0.1), -0.45, 0.9, side, mats, rnd)
            plates_line(root, (side * ha, ya + 0.1), (side * hb, yb - 0.1), 2.85, 3.75 if yb < 26 else 3.35, side, mats, rnd)
        # hard edges: where the sides meet the chamfers
        top = [(side * bow_at(y)[0], y, bow_at(y)[2] - bow_at(y)[4]) for y in (B0, BW1, 25.0, 29.0, 32.0)]
        low = [(side * bow_at(y)[0], y, bow_at(y)[1] + bow_at(y)[3]) for y in (B0, BW1, 25.0, 29.0, 32.0)]
        edge_rail(root, top, trim)
        edge_rail(root, low, trim)
        edge_rail(root, [(side * AHW, A0 + 1.5, 4.0), (side * AHW, A1, 4.0)], trim)
        edge_rail(root, [(side * AHW, A0 + 1.5, -0.8), (side * AHW, A1, -0.8)], trim)
        edge_rail(root, [(side * SHW, S0, 4.2 - 0.5), (side * SHW, S1, 4.2 - 0.5)], trim)
    # a dark keel with vents under the bow and the aft block
    K.box("keel", (2.4, BW1 + 9 - B0, 0.2), dark, loc=(0, (B0 + BW1 + 9) / 2, -1.85), bev=0.04, parent=root)
    K.box("keel", (3.0, A1 - A0 - 3, 0.2), dark, loc=(0, (A0 + A1) / 2, -2.05), bev=0.04, parent=root)
    # foredeck slats down the sloping roof, a centre ridge, the sensor mast
    for k in range(7):
        y = 20.6 + k * 1.3
        hw, zb, zt, cb, ct = bow_at(y)
        dz = (bow_at(y + 0.5)[2] - bow_at(y - 0.5)[2])
        ROOF.append(K.box("foredeck_slat", (2 * (hw - ct) - 0.6, 1.0, 0.1), trim if k % 2 else mats["hull2"], loc=(0, y, zt + 0.03),
                          rot=(math.degrees(math.atan2(dz, 1.0)), 0, 0), bev=0.02, parent=root))
    ROOF.append(loft("ridge", [(14.5, octagon(0.6, 4.7, 5.3, 0.1, 0.25)), (BW1, octagon(0.6, 4.7, 5.3, 0.1, 0.25)), (31.5, octagon(0.4, 3.1, 3.6, 0.1, 0.15))], mats["hull2"], root, bev=0.03))
    ROOF.append(K.cyl("sensor_mast", 0.08, 2.4, trim, loc=(2.0, 16.0, 6.0), parent=root))
    ROOF.append(K.sphere("sensor_ball", 0.35, M(BONE, wear=0.2), loc=(2.0, 16.0, 7.2), parent=root))
    ROOF.append(K.cyl("antenna", 0.04, 3.5, trim, loc=(-2.0, 15.0, 6.5), parent=root))
    for s in (-1, 1):
        ROOF.append(K.box("bow_roof_vent", (1.6, 2.4, 0.25), trim, loc=(s * 3.6, 16.8, 4.9), bev=0.04, parent=root))
        ROOF.append(K.box("bow_roof_slots", (1.3, 2.0, 0.05), dark, loc=(s * 3.6, 16.8, 5.04), bev=0, parent=root))
    # cockpit exterior: a sensor blister under the chin, nav lights and whip antennas on the brow, shutter rails
    K.sphere("chin_blister", 0.5, M(GRAPHITE, metal=0.3), loc=(0, 31.6, -0.25), scale=(1, 1, 0.6), parent=root)
    K.sphere("chin_eye", 0.18, glow("#ff5a4a", 3), loc=(0, 32.05, -0.3), parent=root)
    for s_ in (-1, 1):
        hw = bow_at(30.5)[0]
        K.box("brow_light", (0.18, 0.4, 0.12), glow("#57e39a" if s_ > 0 else "#ff5a4a", 6), loc=(s_ * (hw + 0.1), 30.5, WIN1 + 0.2), bev=0.02, parent=root)
        K.cyl("whip", 0.025, 1.6, trim, loc=(s_ * 1.6, 28.6, bow_at(28.6)[2] + 0.75), rot=(-15, 0, 0), segs=6, bev=0, parent=root)
        for y in (27.0, 28.5):
            K.box("shutter_rail", (0.12, 0.12, WIN1 - WIN0 + 0.5), dark, loc=(s_ * (bow_at(y)[0] + 0.12), y, (WIN0 + WIN1) / 2), bev=0.02, parent=root)
        K.box("cheek_vent", (0.1, 1.6, 0.5), dark, loc=(s_ * (bow_at(27.5)[0] + 0.03), 27.5, 0.4), rot=(0, 0, s_ * -20), bev=0.02, parent=root)
    K.cyl("probe_launcher", 0.12, 2.0, trim, loc=(1.6, BTIP - 0.6, 0.4), rot=(90, 0, 0), parent=root)
    K.box("nose_light", (1.4, 0.1, 0.2), glow("#e2f5ff", 8), loc=(0, BTIP - 0.05, 0.85), bev=0.02, parent=root)
    for x in (-2.2, 2.2):
        K.box("bow_leg", (0.4, 1.8, 0.4), gm, loc=(x, 25.0, -1.75), rot=(20, 0, 0), bev=0.04, parent=root)
        K.cyl("bow_leg_pad", 0.5, 0.2, trim, loc=(x, 24.3, -2.2), bev=0.04, parent=root)
    return root
