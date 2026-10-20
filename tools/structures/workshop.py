"""The Workshop (Colony Works): a vehicle repair shop, the players' first base (docs/art-direction.md).

A framed steel hall with a front gable, two oversized garage bays whose roll-up doors are always up (rolled into their
housings). The left bay is a drive-through, with a second door in the back wall; the right bay is clear to drive a
creation into, with the Fabricator against its back wall (placed by the POI, not part of this model). A one-storey office
on the left, tool wall, bench, tyres, drums and a compressor inside; lampposts, bay lines and tyre stacks outside. Origin
at the hall's centre, 0.3 m under the slab's top (the POI sinks it so the slab is flush with the ground and creations
drive straight on), front towards -Y (the bays open onto the forecourt).
"""
import math
import random

import bmesh
import bpy

import families as F
import frame as FR
import kit as K

# Hall extents (metres): x across, y front (-) to back (+), walls up to the eaves, then the gable to the ridge.
HALF_W, FRONT, BACK = 11.0, -7.0, 9.0
EAVE, RIDGE = 7.0, 8.8
DOOR_H = 2.1                 # walk-in doors (office), metres
OFFICE_DOOR = (-1.6, -0.4)   # the doorway from the hall into the office, along the left wall
BAY_H = 5.8
BAYS = ((-9.5, -1.0), (1.0, 9.5))  # x of each opening's sides
THROUGH = BAYS[0]                  # the bay with a door in the back wall too
# Where the Fabricator stands (Blender frame, its back to the back wall of the right bay); the POI's marker matches.
FABRICATOR = (5.25, 7.6)


def prism(name, pts, y0, y1, material, parent, bev=0.03):
    """An extruded outline in the x-z plane, from y0 to y1 (the gable's triangle)."""
    bm = bmesh.new()
    front = [bm.verts.new((x, y0, z)) for x, z in pts]
    back = [bm.verts.new((x, y1, z)) for x, z in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], back[i], back[j], front[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, material)
    if bev:
        K.bevel(ob, bev)
    return K.place(ob, (0, 0, 0), (0, 0, 0), parent)


def holed_wall(P, name, along, at, a0, a1, h, t, holes, material):
    """A wall of boxes from a0 to a1 along x (at y=at) or y (at x=at), from the floor (0.3) to h above it, around
    rectangular holes (a0, a1, z0, z1) for doors and windows, so the collider has real openings."""
    z_lo, z_hi = 0.3, h + 0.3
    cuts = sorted(holes)

    def piece(b0, b1, z0, z1):
        if b1 - b0 < 1e-3 or z1 - z0 < 1e-3:
            return
        c, cz = (b0 + b1) / 2, (z0 + z1) / 2
        size, loc = ((b1 - b0, t, z1 - z0), (c, at, cz)) if along == "x" else ((t, b1 - b0, z1 - z0), (at, c, cz))
        K.box(name, size, material, loc=loc, bev=0.02, parent=P)
    edge = a0
    for b0, b1, z0, z1 in cuts:
        piece(edge, b0, z_lo, z_hi)
        piece(b0, b1, z_lo, z0)
        piece(b0, b1, z1, z_hi)
        edge = b1
    piece(edge, a1, z_lo, z_hi)


def workshop(o=(0, 0, 0), yaw=0, name="workshop"):
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    P = root
    M = F.M
    bone, steel, mus, gra = M(F.BONE, wear=0.45, grime=0.55), M(F.STEEL, metal=0.3, wear=0.4), M(F.MUSTARD), M(F.GRAPHITE, metal=0.3)
    gun, sig = M(F.GUNMETAL, metal=0.5), M(F.SIGNAL)
    conc = K.mat("worn_concrete", F.CONCRETE, rough=0.9, grime=0.35, noise_scale=2, ao=0.4)
    paint = M(F.MUSTARD, wear=0.6, scratches=0)

    # ---- forecourt and floor
    K.box("pad", (34.0, 30.0, 0.3), conc, loc=(-2.0, -3.0, 0.15), bev=0.06, parent=P)
    for x0, x1 in BAYS:  # bay outlines inside and lead-in lines on the forecourt
        cx = (x0 + x1) / 2
        for sx in (x0 + 0.4, x1 - 0.4):  # worn through where the wheels run over it
            for a, b in segments(-13.0, 11.0 if (x0, x1) == THROUGH else 8.0, random.Random(int(sx * 10)), gap=(0.3, 1.2)):
                K.box("bay_line", (0.15, b - a, 0.02), paint, loc=(sx, (a + b) / 2, 0.31), bev=0, parent=P)
        if (x0, x1) != THROUGH:
            K.box("bay_stop", (x1 - x0 - 0.8, 0.15, 0.02), paint, loc=(cx, 6.6, 0.31), bev=0, parent=P)
        for k in range(3):  # chevrons pointing in
            for s in (-1, 1):
                K.box("chevron", (1.4, 0.3, 0.02), paint, loc=(cx + s * 0.5, -10.5 + k * 1.6, 0.31), rot=(0, 0, s * 35), bev=0, parent=P)

    # ---- walls: bone panels in steel posts and rails, like the part cases
    def wall_x(x, sign, door=None):
        if door:  # a doorway (y0, y1) through to the office, under the low rail
            y0, y1 = door
            for a, b in ((FRONT, y0), (y1, BACK)):
                K.box("side_wall", (0.3, b - a, EAVE), bone, loc=(x, (a + b) / 2, EAVE / 2 + 0.3), bev=0.03, parent=P)
            K.box("side_wall", (0.3, y1 - y0, EAVE - DOOR_H), bone, loc=(x, (y0 + y1) / 2, (EAVE + DOOR_H) / 2 + 0.3), bev=0.03, parent=P)
            for y in (y0, y1):
                K.box("door_jamb", (0.4, 0.12, DOOR_H), gra, loc=(x, y, DOOR_H / 2 + 0.3), bev=0.02, parent=P)
        else:
            K.box("side_wall", (0.3, BACK - FRONT, EAVE), bone, loc=(x, (FRONT + BACK) / 2, EAVE / 2 + 0.3), bev=0.03, parent=P)
        for y in (FRONT, -3.0, 1.0, 5.0, BACK):
            K.box("side_post", (0.36, 0.36, EAVE), steel, loc=(x + sign * 0.1, y, EAVE / 2 + 0.3), bev=0.04, parent=P)
        for z in (2.2, 4.8):
            K.box("side_rail", (0.3, BACK - FRONT, 0.22), steel, loc=(x + sign * 0.08, (FRONT + BACK) / 2, z + 0.3), bev=0.03, parent=P)
        for y0 in (FRONT, -3.0, 1.0, 5.0):  # clerestory windows between the posts
            K.box("clerestory", (0.34, 3.0, 1.0), K.mat("glass", "#9cc7d8", rough=0.15, metal=0.2), loc=(x, y0 + 2.0, 5.9 + 0.3), bev=0.02, parent=P)
            K.box("clerestory_frame", (0.38, 3.2, 0.14), gra, loc=(x + sign * 0.02, y0 + 2.0, 6.5 + 0.3), bev=0.02, parent=P)
    wall_x(-HALF_W, -1, door=OFFICE_DOOR)
    wall_x(HALF_W, 1)
    # back wall: solid but for the through bay's door, which has a header over it like the front
    tx0, tx1 = THROUGH
    for a, b in ((-HALF_W, tx0), (tx1, HALF_W)):
        K.box("back_wall", (b - a, 0.3, EAVE), bone, loc=((a + b) / 2, BACK, EAVE / 2 + 0.3), bev=0.03, parent=P)
        for z in (2.2, 4.8):
            K.box("back_rail", (b - a, 0.3, 0.22), steel, loc=((a + b) / 2, BACK + 0.08, z + 0.3), bev=0.03, parent=P)
    K.box("back_header", (tx1 - tx0, 0.3, EAVE - BAY_H), bone, loc=((tx0 + tx1) / 2, BACK, (BAY_H + EAVE) / 2 + 0.3), bev=0.03, parent=P)
    K.box("back_header_rail", (tx1 - tx0 + 0.4, 0.36, 0.3), steel, loc=((tx0 + tx1) / 2, BACK + 0.08, BAY_H + 0.3 + 0.15), bev=0.04, parent=P)
    for x in (-HALF_W, tx0 - 0.2, tx1 + 0.2, 5.5, HALF_W):
        K.box("back_post", (0.36, 0.36, EAVE), steel, loc=(x, BACK + 0.1, EAVE / 2 + 0.3), bev=0.04, parent=P)
    prism("back_gable", [(-HALF_W, EAVE + 0.3), (HALF_W, EAVE + 0.3), (HALF_W, EAVE + 0.48), (0, RIDGE + 0.5), (-HALF_W, EAVE + 0.48)], BACK - 0.15, BACK + 0.15, bone, P)

    # ---- front: three piers, a header over the bays and the gable with the sign
    piers = ((-HALF_W, BAYS[0][0]), (BAYS[0][1], BAYS[1][0]), (BAYS[1][1], HALF_W))
    for x0, x1 in piers:
        K.box("pier", (x1 - x0, 0.6, EAVE), steel, loc=((x0 + x1) / 2, FRONT, EAVE / 2 + 0.3), bev=0.05, parent=P)
    K.box("header", (2 * HALF_W, 0.5, EAVE - BAY_H), bone, loc=(0, FRONT, (BAY_H + EAVE) / 2 + 0.3), bev=0.04, parent=P)
    K.box("header_rail", (2 * HALF_W + 0.2, 0.62, 0.3), steel, loc=(0, FRONT, BAY_H + 0.3 + 0.15), bev=0.04, parent=P)
    prism("gable", [(-HALF_W, EAVE + 0.3), (HALF_W, EAVE + 0.3), (HALF_W, EAVE + 0.48), (0, RIDGE + 0.5), (-HALF_W, EAVE + 0.48)], FRONT - 0.2, FRONT + 0.2, bone, P)
    def bay_door(x0, x1, face, d, jamb):
        """A roll-up door, all the way up, in the wall at y=face; d is +1 when the hall is towards +y (the front), -1 at
        the back; jamb is the depth of the wall it is cut through."""
        cx, w = (x0 + x1) / 2, x1 - x0
        FR.framed_box(P, "door_housing", (cx, face + d * 0.75, BAY_H - 0.15 + 0.3), (w + 0.2, 0.9, 0.9), M(F.STEEL, metal=0.3), gra, fw=0.1)
        K.box("door_lip", (w - 0.1, 0.12, 0.28), gun, loc=(cx, face + d * 0.3, BAY_H - 0.05 + 0.3), bev=0.03, parent=P)
        FR.hazard(P, (cx, face + d * 0.22, BAY_H + 0.08 + 0.3), w - 0.2, 0.12, mus, gra, n=16)
        for s in (-1, 1):
            jx = cx + s * (w / 2)
            K.box("door_track", (0.2, 0.3, BAY_H), gra, loc=(jx - s * 0.12, face + d * 0.4, BAY_H / 2 + 0.3), bev=0.03, parent=P)
            for k in range(7):  # hazard stripes on the jambs
                K.box("jamb_haz", (0.08, jamb + 0.02, 0.3), mus if k % 2 == 0 else gra, loc=(jx + s * 0.03, face, 0.45 + k * 0.3 + 0.3), bev=0.005, parent=P)
            K.cyl("bollard", 0.16, 1.1, mus, loc=(jx + s * 0.45, face - d * 0.65, 0.85), bev=0.04, parent=P)
            K.cyl("bollard_cap", 0.17, 0.1, gra, loc=(jx + s * 0.45, face - d * 0.65, 1.42), bev=0.02, parent=P)
        K.box("bay_light", (w - 1.0, 0.18, 0.12), F.glow(F.WARM, 10), loc=(cx, face - d * 0.32, BAY_H + 0.62 + 0.3), bev=0.03, parent=P)
    for x0, x1 in BAYS:
        bay_door(x0, x1, FRONT, 1, 0.6)
    bay_door(*THROUGH, BACK, -1, 0.3)
    # sign on the gable: WORKSHOP and a wrench, on a mustard board in a graphite frame
    FR.framed_box(P, "sign", (0.0, FRONT - 0.32, 7.9), (8.4, 0.3, 1.5), mus, gra, fw=0.14)
    FR.label(P, "WORKSHOP", (0.8, FRONT - 0.5, 7.88), 0.95, gra)
    K.box("wrench_shaft", (0.24, 0.08, 1.05), gra, loc=(-3.2, FRONT - 0.5, 7.85), rot=(0, 40, 0), bev=0.03, parent=P)
    jaw = K.torus("wrench_jaw", 0.22, 0.08, gra, loc=(-2.92, FRONT - 0.5, 8.25), rot=(90, 0, 0), arc=260, parent=P)
    jaw.rotation_euler[1] = math.radians(140)
    K.cyl("vent_ring", 0.5, 0.12, gra, loc=(0, FRONT - 0.2, 8.85), rot=(90, 0, 0), bev=0.02, parent=P)
    FR.vent(P, (0, FRONT - 0.27, 8.85), 0.7, 0.5, gra, gun, n=4)

    # ---- roof: two pitched sheets with standing seams, overhanging at the front, a gutter on each eave
    pitch = math.atan2(RIDGE - EAVE, HALF_W)
    run = math.hypot(HALF_W + 0.5, RIDGE - EAVE + 0.5 * math.tan(pitch))
    length = BACK - FRONT + 1.4
    for s in (-1, 1):
        cx = s * (HALF_W + 0.5) / 2
        cz = (EAVE + RIDGE) / 2 + 0.3 + 0.2 - 0.25 * math.tan(pitch)
        K.box("roof", (run, length, 0.22), gra, loc=(cx, (FRONT + BACK) / 2 - 0.5, cz), rot=(0, s * math.degrees(pitch), 0), bev=0.04, parent=P)
        for k in range(int(length / 1.2)):
            y = FRONT - 1.2 + 0.6 + k * 1.2
            K.box("seam", (run, 0.07, 0.08), gun, loc=(cx, y, cz + 0.14), rot=(0, s * math.degrees(pitch), 0), bev=0.01, parent=P)
        K.cyl("gutter", 0.14, length, steel, loc=(s * (HALF_W + 0.55), (FRONT + BACK) / 2 - 0.5, EAVE + 0.28), rot=(90, 0, 0), bev=0, parent=P)
        K.cyl("downpipe", 0.08, EAVE, steel, loc=(s * (HALF_W + 0.55), FRONT - 0.9, EAVE / 2 + 0.3), bev=0, parent=P)
    K.box("ridge_cap", (0.5, length, 0.2), steel, loc=(0, (FRONT + BACK) / 2 - 0.5, RIDGE + 0.55), bev=0.04, parent=P)
    FR.framed_box(P, "ac", (5.5, 3.0, RIDGE - 0.2), (2.4, 2.0, 1.1), bone, gun, fw=0.12)
    K.torus("fan_ring", 0.5, 0.06, gun, loc=(5.5, 3.0, RIDGE + 0.37), parent=P)

    # ---- trusses and hanging lamps inside
    for y in (-3.0, 1.0, 5.0):
        K.box("tie_beam", (2 * HALF_W - 0.4, 0.25, 0.3), gra, loc=(0, y, EAVE + 0.15), bev=0.03, parent=P)
        K.box("king_post", (0.22, 0.22, RIDGE - EAVE), gra, loc=(0, y, (EAVE + RIDGE) / 2 + 0.3), bev=0.02, parent=P)
        for s in (-1, 1):
            K.box("strut", (HALF_W * 0.55, 0.18, 0.18), gra, loc=(s * HALF_W * 0.3, y, EAVE + 0.75), rot=(0, -s * 12, 0), bev=0.02, parent=P)
            # strip lamp on chains over each bay
            lx = s * 5.25
            K.box("lamp_body", (3.2, 0.36, 0.16), bone, loc=(lx, y + 1.5, EAVE - 0.9), bev=0.04, parent=P)
            K.box("lamp_tube", (3.0, 0.24, 0.05), F.glow(F.WARM, 14), loc=(lx, y + 1.5, EAVE - 1.0), bev=0.01, parent=P)
            for c in (-1.2, 1.2):
                K.box("lamp_chain", (0.03, 0.03, 0.9), gun, loc=(lx + c, y + 1.5, EAVE - 0.4), bev=0, parent=P)
            K.point("lamp", (lx, y + 1.5, EAVE - 1.3), F.WARM, 300, 0.4).parent = P

    # ---- inside the walls: posts and a rail on the inner faces, a dark wainscot, bay stencils, pipes, oil stains
    for x, sign in ((-HALF_W, 1), (HALF_W, -1)):
        for y in (-3.0, 1.0, 5.0):
            K.box("in_post", (0.3, 0.3, EAVE), steel, loc=(x + sign * 0.25, y, EAVE / 2 + 0.3), bev=0.03, parent=P)
        spans = ((FRONT + 0.3, OFFICE_DOOR[0] - 0.1), (OFFICE_DOOR[1] + 0.1, BACK - 0.3)) if sign > 0 else ((FRONT + 0.3, BACK - 0.3),)
        for a, b in spans:
            K.box("in_wainscot", (0.06, b - a, 1.2), M(F.GRAPHITE, wear=0.5), loc=(x + sign * 0.18, (a + b) / 2, 0.9), bev=0.01, parent=P)
        K.box("in_rail", (0.2, BACK - FRONT - 0.6, 0.18), steel, loc=(x + sign * 0.22, (FRONT + BACK) / 2, 4.2), bev=0.02, parent=P)
    for x in (0.0,):
        K.box("in_back_post", (0.3, 0.3, EAVE), steel, loc=(x, BACK - 0.25, EAVE / 2 + 0.3), bev=0.03, parent=P)
    for a, b in ((-HALF_W + 0.3, tx0), (tx1, HALF_W - 0.3)):
        K.box("in_back_wainscot", (b - a, 0.06, 1.2), M(F.GRAPHITE, wear=0.5), loc=((a + b) / 2, BACK - 0.18, 0.9), bev=0.01, parent=P)
        K.box("in_back_rail", (b - a, 0.2, 0.18), steel, loc=((a + b) / 2, BACK - 0.22, 4.2), bev=0.02, parent=P)
    stencil = M(F.BONE, wear=0.3, scratches=0, grime=0)
    for k, (x0, x1) in enumerate(BAYS):  # the through bay's board hangs on its back door's housing
        y = BACK - 1.25 if (x0, x1) == THROUGH else BACK - 0.24
        z = BAY_H + 0.15 if (x0, x1) == THROUGH else 5.3
        FR.framed_box(P, "bay_board", ((x0 + x1) / 2, y, z), (2.6, 0.08, 0.8 if (x0, x1) == THROUGH else 1.0), M(F.STEEL, metal=0.3), gra, fw=0.08)
        FR.label(P, f"BAY {k + 1}", ((x0 + x1) / 2, y - 0.06, z), 0.5 if (x0, x1) == THROUGH else 0.55, stencil)
    for z in (6.75, 7.0):  # pipe run along the back wall, over the door housing
        K.cyl("back_pipe", 0.07, 2 * HALF_W - 0.8, gun if z < 6.9 else M(F.SEA), loc=(0, BACK - 0.4, z), rot=(0, 90, 0), bev=0, parent=P)
    K.box("drain", (0.5, 6.0, 0.02), gun, loc=(0.0, 1.0, 0.31), bev=0, parent=P)
    for k in range(10):
        K.box("drain_slot", (0.36, 0.06, 0.025), M(F.HULL), loc=(0.0, -1.7 + k * 0.6, 0.315), bev=0, parent=P)
    oil = K.mat("oil", "#3a3630", rough=0.35, metal=0.0)
    for k, (x, y, r) in enumerate(((-5.0, 0.2, 1.1), (-4.0, 3.2, 0.6), (4.2, -1.5, 0.8), (6.8, 2.8, 0.5))):
        K.disc(f"oil{k}", r, oil, (x, y), z=0.305, wobble=0.25, seed=k + 2).parent = P
    # parts shelving along the right wall: framed uprights, three shelves of boxes and cans
    sx = HALF_W - 0.75
    for y in (-2.6, -0.6, 1.4):
        K.box("shelf_post", (0.08, 0.08, 2.6), gra, loc=(sx - 0.4, y, 1.6), bev=0.01, parent=P)
        K.box("shelf_post", (0.08, 0.08, 2.6), gra, loc=(sx + 0.4, y, 1.6), bev=0.01, parent=P)
    for z in (0.7, 1.55, 2.4):
        K.box("shelf", (0.9, 4.1, 0.06), M(F.STEEL, metal=0.3), loc=(sx, -0.6, z), bev=0.01, parent=P)
        for k in range(5):
            c = (F.MUSTARD, F.SIGNAL, F.SEA, F.BONE, F.ZINC)[(k + int(z * 10)) % 5]
            if (k + int(z * 3)) % 3 == 0:
                K.cyl("can", 0.14, 0.32, M(c), loc=(sx, -2.3 + k * 0.85, z + 0.19), bev=0.01, parent=P)
            else:
                K.box("parts_box", (0.6, 0.6, 0.4), M(c, wear=0.4), loc=(sx, -2.3 + k * 0.85, z + 0.23), rot=(0, 0, (k * 17) % 9 - 4), bev=0.03, parent=P)
    # engine hoist by the left wall
    K.box("hoist_leg", (0.14, 1.8, 0.14), M(F.SIGNAL), loc=(-9.2, -3.6, 0.42), bev=0.02, parent=P)
    K.box("hoist_mast", (0.18, 0.18, 2.2), M(F.SIGNAL), loc=(-9.2, -4.4, 1.45), bev=0.02, parent=P)
    K.box("hoist_boom", (0.16, 1.8, 0.16), M(F.SIGNAL), loc=(-9.2, -3.7, 2.45), rot=(-12, 0, 0), bev=0.02, parent=P)
    K.box("hoist_chain", (0.03, 0.03, 0.8), gun, loc=(-9.2, -2.9, 2.05), bev=0, parent=P)
    K.box("hoist_hook", (0.1, 0.06, 0.16), gun, loc=(-9.2, -2.9, 1.62), bev=0.01, parent=P)

    # ---- left wall, between the office doorway and the back: tool wall, bench with a vice, tyre racks; tool chests by bay 2
    wx = -HALF_W + 0.4   # just off the wall's inner posts
    FR.framed_box(P, "pegboard", (wx - 0.05, 3.0, 2.8), (0.1, 3.6, 2.0), M("#b9b29d", wear=0.4), gra, fw=0.08)
    tools = [(0.5, 0.9), (0.18, 0.7), (0.9, 0.18), (0.25, 1.0), (0.6, 0.35), (0.14, 0.6), (0.4, 0.4), (0.8, 0.22)]
    for k, (w, h) in enumerate(tools):
        K.box("tool", (0.05, w, h), gun if k % 3 else sig, loc=(wx + 0.03, 1.45 + k * 0.44, 2.85 + (0.25 if k % 2 else -0.2)), rot=((k * 23) % 40 - 20, 0, 0), bev=0.01, parent=P)
    FR.framed_box(P, "bench", (wx + 0.45, 3.0, 0.75), (0.9, 3.6, 0.9), M(F.STEEL, metal=0.3), gra, fw=0.08)
    K.box("bench_top", (1.0, 3.8, 0.08), M(F.WOOD), loc=(wx + 0.45, 3.0, 1.24), bev=0.02, parent=P)
    K.box("vice", (0.3, 0.35, 0.25), M(F.STEEL, metal=0.4), loc=(wx + 0.75, 1.6, 1.42), bev=0.03, parent=P)
    K.cyl("vice_screw", 0.03, 0.4, gun, loc=(wx + 1.05, 1.6, 1.38), rot=(0, 90, 0), bev=0, parent=P)
    for k, x in enumerate((1.8, 2.6)):
        FR.framed_box(P, "tool_chest", (x, BACK - 0.6, 0.85 + 0.05 * k), (0.8, 0.65, 1.1 + 0.1 * k), sig, gra, fw=0.06)
        for d in range(4):
            K.box("drawer", (0.66, 0.03, 0.04), M(F.ZINC, metal=0.6), loc=(x, BACK - 0.95, 0.55 + d * 0.24), bev=0.005, parent=P)
    for k in range(4):  # tyre stacks by the left wall, clear of the back door
        K.torus("tyre", 0.45, 0.17, M(F.HULL, wear=0.1), loc=(-10.1, 6.2, 0.48 + k * 0.34), parent=P)
    for k in range(3):
        K.torus("tyre", 0.45, 0.17, M(F.HULL, wear=0.1), loc=(-10.1, 7.4, 0.48 + k * 0.34), parent=P)

    # ---- right bay: clear for a creation, the Fabricator at the back; drums, compressor and a hose reel by the wall
    fx, fy = FABRICATOR
    K.box("fab_plinth", (3.4, 3.0, 0.06), M(F.GRAPHITE, wear=0.5), loc=(fx, fy - 0.1, 0.33), bev=0.01, parent=P)
    FR.hazard(P, (fx, fy - 1.62, 0.33), 3.4, 0.06, mus, gra, n=10)
    for k, (x, y) in enumerate(((10.0, 8.2), (10.0, 7.3), (9.2, 7.8))):
        K.cyl("drum", 0.32, 0.95, M((F.STEEL, F.SIGNAL, F.MUSTARD)[k]), loc=(x, y, 0.78), bev=0.02, parent=P)
        K.torus("drum_rim", 0.32, 0.03, gun, loc=(x, y, 1.24), parent=P)
    K.cyl("compressor", 0.45, 1.8, M(F.SIGNAL), loc=(10.1, 3.8, 0.85), rot=(90, 0, 0), bev=0, parent=P)
    for dy in (-0.9, 0.9):
        K.sphere("compressor_end", 0.45, M(F.SIGNAL), loc=(10.1, 3.8 + dy, 0.85), scale=(1, 0.35, 1), parent=P)
    FR.framed_box(P, "compressor_motor", (10.1, 3.8, 1.55), (0.7, 0.8, 0.5), gra, gun, fw=0.06)
    K.torus("hose_reel", 0.35, 0.09, M(F.HULL), loc=(HALF_W - 0.3, -3.6, 2.2), rot=(0, 90, 0), parent=P)
    K.cyl("hose_hub", 0.18, 0.2, mus, loc=(HALF_W - 0.3, -3.6, 2.2), rot=(0, 90, 0), bev=0.02, parent=P)

    # ---- office on the left: one storey you can walk into, from the forecourt or from the hall; a big window, a desk
    ox0, ox1, oy0, oy1, oh = -16.0, -HALF_W - 0.15, -7.0, 1.0, 3.8
    ocx, ocy = (ox0 + ox1) / 2, (oy0 + oy1) / 2
    t = 0.2
    win = (-15.7, -13.1, 1.3, 2.8)                  # x0, x1, z0, z1 of the front window
    door = (-12.45, -11.35)
    holed_wall(P, "office_front", "x", oy0 + t / 2, ox0, ox1, oh, t, [win, (door[0], door[1], 0.3, DOOR_H + 0.3)], bone)
    holed_wall(P, "office_back", "x", oy1 - t / 2, ox0, ox1, oh, t, [], bone)
    holed_wall(P, "office_side", "y", ox0 + t / 2, oy0, oy1, oh, t, [(-4.2, -1.8, 1.5, 2.7)], bone)
    glass = K.mat("office_glass", "#7f9aa8", rough=0.08, metal=0.2)
    K.box("office_window", (win[1] - win[0], 0.04, win[3] - win[2]), glass, loc=((win[0] + win[1]) / 2, oy0 + t / 2, (win[2] + win[3]) / 2), bev=0, parent=P)
    K.box("office_side_window", (0.04, 2.4, 1.2), glass, loc=(ox0 + t / 2, -3.0, 2.1), bev=0, parent=P)
    for (a, b, z0, z1), y in ((win, oy0 - 0.02), ((door[0], door[1], 0.3, DOOR_H + 0.3), oy0 - 0.02)):
        for x in (a, b):
            K.box("office_jamb", (0.12, t + 0.1, z1 - z0), gra, loc=(x, y + t / 2 + 0.01, (z0 + z1) / 2), bev=0.02, parent=P)
        K.box("office_head", (b - a + 0.24, t + 0.1, 0.12), gra, loc=((a + b) / 2, y + t / 2 + 0.01, z1), bev=0.02, parent=P)
        if z0 > 0.5:
            K.box("office_sill", (b - a + 0.3, t + 0.2, 0.08), gra, loc=((a + b) / 2, y + t / 2 - 0.03, z0), bev=0.02, parent=P)
    for y in (-4.2, -1.8):
        K.box("office_jamb", (t + 0.1, 0.12, 1.2), gra, loc=(ox0 + t / 2, y, 2.1), bev=0.02, parent=P)
    # framed corners and a top rail, like the hall
    for x, y in ((ox0, oy0), (ox0, oy1)):
        K.box("office_post", (0.3, 0.3, oh), steel, loc=(x + 0.1, y + (0.1 if y == oy0 else -0.1), oh / 2 + 0.3), bev=0.04, parent=P)
    K.box("office_rail", (ox1 - ox0, 0.26, 0.2), steel, loc=(ocx, oy0 + 0.05, oh + 0.2), bev=0.03, parent=P)
    K.box("office_rail", (0.26, oy1 - oy0, 0.2), steel, loc=(ox0 + 0.05, ocy, oh + 0.2), bev=0.03, parent=P)
    K.box("office_roof", (ox1 - ox0 + 0.6, oy1 - oy0 + 0.6, 0.25), gra, loc=(ocx, ocy, oh + 0.42), bev=0.04, parent=P)
    K.box("office_ceiling", (ox1 - ox0 - 0.3, oy1 - oy0 - 0.3, 0.05), bone, loc=(ocx, ocy, oh + 0.27), bev=0, parent=P)
    # the door stands open, swung in against the front wall
    K.box("office_door", (0.06, 1.05, DOOR_H - 0.05), M(F.STEEL, metal=0.3), loc=(door[0] + 0.05, oy0 + t + 0.55, DOOR_H / 2 + 0.32), bev=0.02, parent=P)
    K.box("door_handle", (0.06, 0.16, 0.04), gun, loc=(door[0] + 0.12, oy0 + t + 0.95, 1.3), bev=0.01, parent=P)
    FR.vent(P, (ox0 - 0.02, ocy + 2.2, 2.9), 1.0, 0.6, gra, gun, axis="x")
    FR.screen(P, (ox1 - 1.6, oy0 - 0.06, 1.75), 0.45, 0.32, gra, F.glow(F.CYAN, 2.5))
    K.box("awning", (4.4, 1.3, 0.1), mus, loc=(ocx - 0.3, oy0 - 0.65, 3.4), rot=(-12, 0, 0), bev=0.02, parent=P)
    # inside: tiled floor, a desk with a terminal and a chair, filing cabinets, a notice board, a lamp
    K.box("office_floor", (ox1 - ox0 - 2 * t, oy1 - oy0 - 2 * t, 0.03), K.mat("office_tiles", "#8f9a8c", rough=0.6, noise_scale=10),
          loc=(ocx + t / 2, ocy, 0.315), bev=0, parent=P)
    FR.framed_box(P, "desk", (-14.6, -5.9, 0.68), (2.2, 0.9, 0.08), M(F.WOOD), gra, fw=0.05)
    for dx in (-1.0, 1.0):
        K.box("desk_leg", (0.08, 0.8, 0.66), gra, loc=(-14.6 + dx, -5.9, 0.64), bev=0.01, parent=P)
    FR.framed_box(P, "terminal", (-14.9, -6.1, 1.0), (0.6, 0.35, 0.45), gra, gun, fw=0.04)
    FR.screen(P, (-14.9, -5.92, 1.02), 0.42, 0.28, gra, F.glow(F.CYAN, 2.0))
    K.box("papers", (0.4, 0.3, 0.03), M(F.BONE, wear=0), loc=(-14.0, -5.8, 0.77), rot=(0, 0, 12), bev=0, parent=P)
    K.cyl("mug", 0.05, 0.11, M(F.SIGNAL), loc=(-13.7, -6.1, 0.82), bev=0.01, parent=P)
    K.box("chair_seat", (0.5, 0.5, 0.08), M(F.HULL), loc=(-14.6, -5.1, 0.78), bev=0.03, parent=P)
    K.box("chair_back", (0.5, 0.08, 0.55), M(F.HULL), loc=(-14.6, -4.82, 1.12), rot=(-8, 0, 0), bev=0.03, parent=P)
    K.cyl("chair_post", 0.04, 0.4, gun, loc=(-14.6, -5.1, 0.55), bev=0, parent=P)
    for k in range(5):
        a = math.radians(72 * k)
        K.box("chair_foot", (0.32, 0.05, 0.04), gun, loc=(-14.6 + 0.16 * math.cos(a), -5.1 + 0.16 * math.sin(a), 0.36), rot=(0, 0, 72 * k), bev=0.01, parent=P)
    for k in range(2):
        FR.framed_box(P, "filing_cabinet", (-15.45, 0.0 - k * 0.7, 0.95), (0.6, 0.6, 1.3), M(F.STEEL, metal=0.3), gra, fw=0.05)
        for d in range(3):
            K.box("cabinet_handle", (0.03, 0.2, 0.03), M(F.ZINC, metal=0.6), loc=(-15.12, 0.0 - k * 0.7, 0.6 + d * 0.38), bev=0.005, parent=P)
    FR.framed_box(P, "notice_board", (-13.4, oy1 - t - 0.04, 2.0), (2.2, 0.06, 1.2), M("#b9a27a", wear=0.2), gra, fw=0.06)
    for k, (dx, dz, c) in enumerate(((-0.7, 0.2, F.BONE), (-0.2, -0.25, F.MUSTARD), (0.3, 0.25, F.BONE), (0.75, -0.2, "#9cc7d8"))):
        K.box("notice", (0.36, 0.02, 0.44), M(c, wear=0), loc=(-13.4 + dx, oy1 - t - 0.09, 2.0 + dz), rot=(0, k * 7 - 10, 0), bev=0, parent=P)
    for k in range(3):  # lockers against the hall wall
        y = -4.6 + k * 0.62
        FR.framed_box(P, "locker", (ox1 - 0.32, y, 1.2), (0.5, 0.6, 1.8), M(F.STEEL, metal=0.3, wear=0.4), gra, fw=0.04)
        for z in (1.75, 1.85):
            K.box("locker_vent", (0.02, 0.3, 0.03), gra, loc=(ox1 - 0.58, y, z), bev=0, parent=P)
        K.box("locker_handle", (0.03, 0.04, 0.16), M(F.ZINC, metal=0.6), loc=(ox1 - 0.59, y + 0.18, 1.2), bev=0.005, parent=P)
    K.box("couch_seat", (0.75, 1.9, 0.4), M("#6a4a3a", wear=0.3), loc=(ox0 + t + 0.42, -3.0, 0.55), bev=0.06, parent=P)
    K.box("couch_back", (0.22, 1.9, 0.55), M("#6a4a3a", wear=0.3), loc=(ox0 + t + 0.14, -3.0, 0.98), bev=0.06, parent=P)
    for dy in (-1.0, 1.0):
        K.box("couch_arm", (0.75, 0.18, 0.55), M("#6a4a3a", wear=0.3), loc=(ox0 + t + 0.42, -3.0 + dy, 0.62), bev=0.05, parent=P)
    FR.framed_box(P, "cooler_base", (ox0 + t + 0.3, -1.55, 0.8), (0.4, 0.4, 0.9), bone, gra, fw=0.04)
    K.cyl("cooler_jug", 0.15, 0.4, K.mat("cooler_water", "#8fc4dc", rough=0.1, metal=0.1), loc=(ox0 + t + 0.3, -1.55, 1.45), bev=0.02, parent=P)
    K.box("rug", (2.0, 2.8, 0.01), M("#7a5a44", wear=0.2), loc=(-13.6, -3.2, 0.335), bev=0, parent=P)
    K.box("binder_shelf", (0.3, 1.4, 0.04), M(F.WOOD), loc=(ox0 + t + 0.15, -0.35, 2.3), bev=0.01, parent=P)
    for k in range(7):
        K.box("binder", (0.24, 0.07, 0.3), M((F.SIGNAL, F.STEEL, F.MUSTARD, F.BONE)[k % 4], wear=0.2), loc=(ox0 + t + 0.15, -0.9 + k * 0.12, 2.47), bev=0.005, parent=P)
    K.cyl("clock", 0.2, 0.04, bone, loc=(ox1 - 0.18, -1.0, 2.95), rot=(0, 90, 0), bev=0.01, parent=P)
    K.torus("clock_rim", 0.2, 0.025, gra, loc=(ox1 - 0.2, -1.0, 2.95), rot=(0, 90, 0), parent=P)
    K.box("poster", (0.7, 0.02, 0.95), M("#9cc7d8", wear=0.2), loc=(-11.85, oy1 - t - 0.02, 1.9), bev=0, parent=P)
    K.box("office_lamp", (1.2, 0.3, 0.06), F.glow(F.WARM, 12), loc=(ocx, ocy, oh + 0.22), bev=0.01, parent=P)
    K.point("office_light", (ocx, ocy, oh - 0.2), F.WARM, 120, 0.3).parent = P

    # ---- outside: lampposts, a tyre stack, a fuel tank on framed legs by the right wall
    for x in (-17.0, 14.0):
        F.lamppost2(P, (x, -11.5, 0.3))
    for k in range(5):
        K.torus("tyre_out", 0.45, 0.17, M(F.HULL, wear=0.1), loc=(-14.0 + (k % 2) * 0.2, -9.0, 0.48 + k * 0.34), parent=P)
    tm = M(F.MUSTARD)
    K.cyl("tank", 1.0, 3.4, tm, loc=(13.4, 4.0, 2.0), rot=(90, 0, 0), bev=0, parent=P)
    for dy in (-1.7, 1.7):
        K.sphere("tank_end", 1.0, tm, loc=(13.4, 4.0 + dy, 2.0), scale=(1, 0.42, 1), parent=P)
    for y in (2.9, 5.1):
        FR.framed_box(P, "tank_leg", (13.4, y, 0.8), (1.7, 0.4, 1.0), gra, gun, fw=0.1)
        K.cyl("tank_band", 1.05, 0.22, gra, loc=(13.4, y, 2.0), rot=(90, 0, 0), bev=0.02, parent=P)
    K.cyl("tank_pipe", 0.14, 1.9, gun, loc=(12.2, 4.0, 2.6), rot=(0, 90, 0), bev=0, parent=P)
    wear(P, bone)
    return root


def segments(a, b, rnd, gap=(0.3, 1.0), run=(1.5, 4.0)):
    """Pieces of a line from a to b with a few worn-away gaps."""
    out, x = [], a
    while x < b:
        y = min(b, x + rnd.uniform(*run))
        out.append((x, y))
        x = y + rnd.uniform(*gap)
    return out


def tuft(P, x, y, rnd, mat):
    """A small clump of weeds growing out of a crack."""
    for k in range(rnd.randint(4, 7)):
        a = rnd.uniform(0, 360)
        h = rnd.uniform(0.15, 0.35)
        K.box("weed", (0.03, 0.012, h), mat, loc=(x + rnd.uniform(-0.08, 0.08), y + rnd.uniform(-0.08, 0.08), 0.3 + h / 2),
              rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), a), bev=0, parent=P)


def wear(P, bone):
    """Use and weather, but kept tidy: saw-cut joints and hairline cracks in the slab, tyre scuffs into the bays, patched
    slabs, oil drips, dirt drifted against the walls and a few weeds in the cracks (the walls' grime is in their material)."""
    rnd = random.Random(42)
    joint = K.mat("slab_joint", "#6f6d66", rough=0.95, ao=0)
    crack = K.mat("slab_crack", "#605d56", rough=0.95, ao=0)
    scuff = K.mat("tyre_scuff", "#8a877e", rough=0.85, ao=0)
    scuff_dark = K.mat("tyre_scuff_dark", "#78756d", rough=0.8, ao=0)
    stain = K.mat("slab_stain", "#97948a", rough=0.9, ao=0)
    patch = K.mat("worn_concrete_patch", "#b7b5ab", rough=0.9, grime=0.1, noise_scale=4, ao=0)
    dirt = K.mat("drift_dirt", "#8c7f66", rough=1.0, ao=0)
    oil = K.mat("oil", "#3a3630", rough=0.35, metal=0.0)
    weed = K.mat("weed", "#6d8f3c", rough=0.8, ao=0)
    x0, x1, y0, y1 = -19.0, 15.0, -18.0, 12.0  # the pad
    # saw-cut control joints in a grid
    for x in (-14.75, -10.5, -6.0, -1.0, 4.0, 9.0):
        K.box("joint", (0.035, y1 - y0 - 0.2, 0.004), joint, loc=(x, (y0 + y1) / 2, 0.302), bev=0, parent=P)
    for y in (-13.0, -7.5, -2.5, 2.5, 7.5):
        K.box("joint", (x1 - x0 - 0.2, 0.035, 0.004), joint, loc=((x0 + x1) / 2, y, 0.302), bev=0, parent=P)
    # hairline cracks running off the joints and the pad's edge
    starts = [(-6.0, -13.0), (4.0, -7.5), (-10.5, -2.5), (9.0, -13.0), (-1.0, -17.9), (12.0, -17.9), (-14.75, -13.0),
              (-18.9, -5.0), (14.9, -10.0), (-6.0, 7.5), (4.0, 2.5), (6.5, -17.9), (-12.0, -17.9), (1.5, 7.5)]
    cracks = []
    for k, (cx, cy) in enumerate(starts):
        a = rnd.uniform(0, 2 * math.pi)
        pts = [(cx, cy)]
        for _ in range(rnd.randint(4, 8)):
            a += rnd.uniform(-0.7, 0.7)
            step = rnd.uniform(0.25, 0.5)
            cx, cy = max(x0 + 0.1, min(x1 - 0.1, cx + step * math.cos(a))), max(y0 + 0.1, min(y1 - 0.1, cy + step * math.sin(a)))
            pts.append((cx, cy))
        K.strip(f"crack{k}", pts, rnd.uniform(0.02, 0.04), crack, z=0.304).parent = P
        cracks.append(pts)
    # tyre scuffs: darker bands where wheels roll in, darkest in the middle of each track
    for bx in (-5.25, 5.25):
        for side in (-1, 1):
            tx = bx + side * 0.95
            pts = [(tx + 0.08 * math.sin(y * 0.4 + bx), y) for y in [-17.5 + 1.0 * k for k in range(30 if bx < 0 else 23)]]
            K.strip("scuff", pts, 0.55, scuff, z=0.303).parent = P
            for a, b in segments(-16.0, 4.5, rnd, gap=(0.5, 2.0), run=(1.0, 3.0)):
                inner = [(tx + 0.08 * math.sin(y * 0.4 + bx), y) for y in (a, (a + b) / 2, b)]
                K.strip("scuff_dark", inner, 0.22, scuff_dark, z=0.3035).parent = P
    # soft stains where water stood and where things were dragged, mostly in front of the bays
    placed = []
    while len(placed) < 14:  # apart, so no two flat decals overlap (they would flicker)
        x, y = rnd.uniform(-17.5, 13.5), rnd.uniform(-17.0, 8.0)
        if all(math.hypot(x - a, y - b) > 3.8 for a, b in placed):
            placed.append((x, y))
    for k, (x, y) in enumerate(placed):
        d = K.disc(f"stain{k}", 1.0, stain, (x, y), z=0.3015, segs=28, wobble=0.3, seed=k + 80)
        d.scale = (rnd.uniform(0.6, 1.8), rnd.uniform(0.5, 1.4), 1)
        d.parent = P
    # patched slabs
    for px, py, w, d in ((-12.6, -15.3, 3.8, 4.4), (6.5, -10.2, 4.6, 4.8), (1.5, -5.0, 4.6, 4.6)):
        K.box("patch", (w, d, 0.006), patch, loc=(px, py, 0.303), bev=0, parent=P)
    # oil: drips into bay 1 and where cars stand, spots on the forecourt
    for k, (x, y, r) in enumerate(((-5.25, 1.8, 0.9), (-5.6, -9.0, 0.35), (-5.1, -12.5, 0.25), (6.0, -14.0, 0.4), (-4.7, -5.5, 0.3),
                                   (5.0, 5.2, 0.45), (-8.8, -15.5, 0.2), (2.4, -3.2, 0.25))):
        K.disc(f"oil_more{k}", r, oil, (x, y), z=0.3045, wobble=0.3, seed=k + 20).parent = P
    drips = []
    while len(drips) < 18:
        x, y = rnd.uniform(-7.0, -3.5), rnd.uniform(-16.0, 0.0)
        if all(math.hypot(x - a, y - b) > 0.25 for a, b in drips):
            drips.append((x, y))
    for k, (x, y) in enumerate(drips):
        K.disc(f"drip{k}", rnd.uniform(0.04, 0.1), oil, (x, y), z=0.3055, segs=10, wobble=0.2, seed=k + 40).parent = P
    # dirt drifted against walls and into corners, outside only
    drifts = [(-11.0, -7.4, 1.6, 0.4), (11.0, -7.4, 1.2, 0.35), (0.0, -7.4, 0.9, 0.3), (-16.4, -6.0, 0.5, 1.8), (-16.4, 0.5, 0.45, 1.2),
              (11.5, 8.5, 0.5, 2.2), (-11.5, 8.0, 0.45, 2.6), (6.0, 9.5, 2.4, 0.45), (-10.3, 9.5, 0.6, 0.35), (1.5, 9.5, 0.9, 0.3)]
    for k, (x, y, sx, sy) in enumerate(drifts):
        d = K.disc(f"drift{k}", 1.0, dirt, (x, y), z=0.305, segs=24, wobble=0.25, seed=k + 60)
        d.scale = (sx, sy, 1)
        d.parent = P
    # weeds in a few cracks and along the pad's edge
    for pts in cracks[::3]:
        x, y = pts[len(pts) // 2]
        tuft(P, x, y, rnd, weed)
    for x, y in ((-18.7, -10.0), (-18.7, 4.0), (14.7, -3.0), (14.7, 9.0), (-3.0, 11.7), (8.0, 11.7), (-11.0, -17.7), (10.5, -17.7),
                 (-16.2, -3.0), (11.4, 6.5)):
        tuft(P, x, y, rnd, weed)
