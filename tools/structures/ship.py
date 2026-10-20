"""The SV-04 Salvager, the player's ship, walk-in and broken in two at the crash site.

The fore section (26 m with its nose) holds the bridge (the Logbook), crew quarters and engineering (the Master
Battery); the aft section (16 m) holds the cargo hold and the engine room. The hull is a chamfered box (an octagon
section) of framed plates with rings every 4 m, like the part models' cases. Inside it is 6 m tall with a deck
0.3 m up; the starboard airlock and the port cargo door open down to the deck so a player walks in.

Origin: the ship's centre on the ground, nose towards +Y (Blender), so -Z in glTF/Godot.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, glow, fire, HULL, GUNMETAL, CYAN, COPPER, MUSTARD, BONE, STEEL, GRAPHITE, WOOD, WARM

W, H, C = 5.0, 6.0, 1.5   # half width, height, chamfer
T = 0.25                  # plate thickness
ROOF = []                 # roof and upper chamfer plates, hidden for the cutaway


def octagon(w=W, h=H, c=C, inset=0.0, z0=0.0):
    w, h, c = w - inset, h - inset, c
    return [(-w + c, z0 + inset), (w - c, z0 + inset), (w, z0 + inset + c), (w, z0 + h - c), (w - c, z0 + h),
            (-w + c, z0 + h), (-w, z0 + h - c), (-w, z0 + inset + c)]


def prism(name, poly, y0, y1, material, parent, cutters=()):
    bm = bmesh.new()
    a = [bm.verts.new((x, y0, z)) for x, z in poly]
    b = [bm.verts.new((x, y1, z)) for x, z in poly]
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)
    for i in range(len(poly)):
        j = (i + 1) % len(poly)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, material)
    ob.parent = parent
    K.bevel(ob, 0.03, segs=2, angle=40)
    for cut in cutters:
        mod = ob.modifiers.new("cut", "BOOLEAN")
        mod.object = cut
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        ob.modifiers.move(len(ob.modifiers) - 1, 0)
    return ob


def cutter(parent, loc, size):
    c = K.box("cutter", size, K.mat("cut", "#ff00ff"), loc=loc, bev=0, parent=parent)
    c.hide_render = True
    c.hide_viewport = True
    return c


def hull_run(P, y0, y1, hm, gm, skip=(), rnd=None, seg=2.0):
    """Plates between y0 and y1, segment by segment; skip holds (side, segment index) openings."""
    n = int(round((y1 - y0) / seg))
    poly = octagon()
    sides = list(zip(poly, poly[1:] + poly[:1]))
    for i in range(n):
        ya, yb = y0 + i * seg + 0.02, y0 + (i + 1) * seg - 0.02
        for k, ((xa, za), (xb, zb)) in enumerate(sides):
            if (k, i) in skip:
                continue
            mid = Vector(((xa + xb) / 2, 0, (za + zb) / 2))
            d = Vector((xb - xa, 0, zb - za))
            length = d.length
            ang = math.atan2(d.z, d.x)
            outward = Vector((math.sin(ang), 0, -math.cos(ang)))
            pm = gm if rnd and rnd.random() < 0.15 else hm
            plate = K.box("plate", (length + 0.02, yb - ya, T), pm, loc=mid + outward * (T / 2) + Vector((0, (ya + yb) / 2, 0)),
                          rot=(0, -math.degrees(ang), 0), bev=0.03, parent=P)
            if k in (3, 4, 5) or (k in (2, 6) and False):
                ROOF.append(plate)
    # framed rings every other segment: they stand proud of the plates like the part cases' rails
    for i in range(0, n + 1, 2):
        y = y0 + i * seg
        for k, ((xa, za), (xb, zb)) in enumerate(sides):
            mid = Vector(((xa + xb) / 2, y, (za + zb) / 2))
            d = Vector((xb - xa, 0, zb - za))
            ang = math.atan2(d.z, d.x)
            outward = Vector((math.sin(ang), 0, -math.cos(ang)))
            rail = K.box("ring", (d.length + 0.3, 0.35, 0.42), gm, loc=mid + outward * 0.22, rot=(0, -math.degrees(ang), 0), bev=0.05, parent=P)
            if k in (3, 4, 5):
                ROOF.append(rail)


def bulkhead(P, y, mat, door=(0.0, 1.3, 2.6), name="bulk"):
    dx, dw, dh = door
    cut = cutter(P, (dx, y, 0.3 + dh / 2), (dw, 1.0, dh))
    return prism(name, octagon(inset=0.15), y - 0.12, y + 0.12, mat, P, cutters=[cut])


def deck(P, y0, y1, mat):
    K.box("deck", (2 * W - 0.4, y1 - y0, 0.3), mat, loc=(0, (y0 + y1) / 2, 0.15 + T * 0), bev=0.02, parent=P)
    for k in range(int(y1 - y0)):  # grating strips
        K.box("grate", (2 * W - 2 * C - 0.4, 0.06, 0.04), K.mat("grate", "#1d2024", rough=0.8), loc=(0, y0 + k + 0.5, 0.31), bev=0, parent=P)


def ceiling_light(P, y, x=0.0, on=True):
    K.box("clight", (1.6, 0.25, 0.08), glow("#bfefff", 6) if on else K.mat("deadlight", "#3a4044", rough=0.4), loc=(x, y, H - 0.32), bev=0.02, parent=P)
    if on:
        K.point("clight_pt", (x, y, H - 0.7), "#d6f4ff", 320, 0.6).parent = P


def torn_end(P, y, dirn, hm, gm, rnd):
    poly = octagon()
    for k in range(18):
        t = k / 18
        idx = int(t * 8)
        (xa, za), (xb, zb) = poly[idx], poly[(idx + 1) % 8]
        f = t * 8 - idx
        x, z = xa + (xb - xa) * f, za + (zb - za) * f
        K.box("torn", (0.9, 0.9 + rnd.uniform(0, 1.0), 0.14), hm if k % 3 else gm, loc=(x, y + dirn * rnd.uniform(0.1, 0.5), z),
              rot=(rnd.uniform(-35, 35), rnd.uniform(-30, 30), rnd.uniform(-30, 30)), bev=0.02, parent=P)
    cu = K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)
    for k in range(5):
        K.torus("gut", rnd.uniform(0.4, 0.8), 0.08, cu, loc=(rnd.uniform(-3, 3), y, rnd.uniform(H - 1.4, H - 0.6)),
                rot=(rnd.uniform(0, 90), rnd.uniform(0, 90), 0), arc=200, parent=P)


def nacelle(P, c, hm, gm):
    """An engine nacelle along y centred on c: banded cylinder, nozzle ring and a cyan core at the back (-y)."""
    x, y, z = c
    K.cyl("nacelle", 1.6, 6.5, hm, loc=(x, y, z), rot=(90, 0, 0), bev=0.06, parent=P)
    for k in range(3):
        K.cyl("nacelle_band", 1.66, 0.3, gm, loc=(x, y + 2.1 - k * 1.8, z), rot=(90, 0, 0), bev=0.04, parent=P)
    K.torus("nozzle", 1.35, 0.25, gm, loc=(x, y - 3.3, z), rot=(90, 0, 0), parent=P)
    K.cyl("nozzle_core", 1.2, 0.12, glow(CYAN, 2.0), loc=(x, y - 3.15, z), rot=(90, 0, 0), bev=0, parent=P)


def torn_stub(P, c, gm, rnd):
    """Jagged plates and copper guts where something was torn off a pylon, facing -x."""
    x, y, z = c
    cu = K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)
    for k in range(6):
        K.box("stub_plate", (0.08, rnd.uniform(0.4, 1.0), rnd.uniform(0.3, 0.7)), gm,
              loc=(x - rnd.uniform(0.0, 0.25), y + rnd.uniform(-1.2, 1.2), z + rnd.uniform(-0.3, 0.3)), rot=(rnd.uniform(-25, 25), 0, rnd.uniform(-30, 30)), bev=0.01, parent=P)
    for k in range(3):
        K.cyl("stub_cable", 0.07, 0.6, cu, loc=(x - 0.25, y - 0.6 + k * 0.6, z + rnd.uniform(-0.2, 0.2)), rot=(0, 90 + rnd.uniform(-20, 20), 0), bev=0, parent=P)


def lost_nacelle(name="nacelle", seed=13):
    """The port nacelle that tore off in flight, lying where it fell: origin on the ground under its middle, burnt and
    dented, with its pylon stump and copper guts at the torn side (+x)."""
    rnd = random.Random(seed)
    root = K.link(bpy.data.objects.new(name, None))
    hm = M("#4c5159", metal=0.35, rough=0.6, wear=0.7, grime=0.6)
    gm = M("#2f343b", metal=0.4, rough=0.45, grime=0.4)
    body = K.link(bpy.data.objects.new("nacelle_body", None))
    body.parent = root
    body.location = (0, 0, 1.45)
    body.rotation_euler = (math.radians(4), math.radians(-12), 0)  # rolled onto its side, nose dug in a little
    nacelle(body, (0, 0, 0), hm, gm)
    FR.framed_box(body, "pylon_stump", (1.75, 0.0, 0.0), (0.6, 2.4, 0.8), hm, gm, fw=0.08)
    torn_stub(body, (2.1, 0.0, 0.0), gm, rnd)
    soot = K.mat("soot", "#1d1b19", rough=1.0)
    for k in range(5):
        K.sphere("soot", rnd.uniform(0.5, 0.9), soot, loc=(rnd.uniform(-1.2, 1.2), rnd.uniform(-2.5, 2.5), 1.6 + rnd.uniform(-0.2, 0.3)), scale=(1, 1, 0.12), parent=body)
    return root


def bunk(P, loc, yaw=0):
    x, y, z = loc
    fr, mt = M(GUNMETAL, metal=0.5), K.mat("mattress", "#4f6b62", rough=0.9, ao=0.4)
    for zz in (0.55, 1.75):
        K.box("bunk_frame", (2.1, 0.95, 0.12), fr, loc=(x, y, z + zz), rot=(0, 0, yaw), bev=0.02, parent=P)
        K.box("mattress", (1.95, 0.85, 0.16), mt, loc=(x, y, z + zz + 0.14), rot=(0, 0, yaw), bev=0.05, parent=P)
    for dx in (-1.0, 1.0):
        for dy in (-0.42, 0.42):
            K.box("bunk_post", (0.08, 0.08, 2.2), fr, loc=(x + dx * math.cos(math.radians(yaw)) - dy * math.sin(math.radians(yaw)),
                                                       y + dx * math.sin(math.radians(yaw)) + dy * math.cos(math.radians(yaw)), z + 1.1), bev=0.01, parent=P)


def console(P, loc, yaw=0, w=1.8):
    x, y, z = loc
    root = K.link(bpy.data.objects.new("console", None))
    root.parent = P
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(yaw))
    FR.framed_box(root, "con", (0, 0, 0.5), (w, 0.8, 1.0), M(GRAPHITE, metal=0.3), M(GUNMETAL, metal=0.5), fw=0.08)
    K.box("con_top", (w, 0.6, 0.1), M(GUNMETAL, metal=0.5), loc=(0, 0.1, 1.05), rot=(-25, 0, 0), bev=0.02, parent=root)
    FR.screen(root, (0, 0.2, 1.08), w * 0.7, 0.35, M(GRAPHITE), glow(CYAN, 2.5), rot=(-25, 0, 0))
    for k in range(4):
        K.cyl("knob", 0.05, 0.05, M(MUSTARD), loc=(-w * 0.3 + k * w * 0.2, -0.22, 0.98), rot=(-25, 0, 0), bev=0.005, parent=root)
    return root


def scrap_pile(P, loc, rnd, kind="metal", n=7):
    x, y, z = loc
    for k in range(n):
        if kind == "metal":
            K.box("scrap_metal", (rnd.uniform(0.4, 0.8), rnd.uniform(0.3, 0.6), 0.06), M(rnd.choice(["#6d6f6e", "#7a4a30", "#5a6068"]), wear=0.8, metal=0.5),
                  loc=(x + rnd.uniform(-0.4, 0.4), y + rnd.uniform(-0.4, 0.4), z + 0.05 + k * 0.06), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180)), bev=0.01, parent=P)
        else:
            K.box("scrap_wood", (1.2, 0.18, 0.08), K.mat(f"wood{k % 3}", ["#9c7650", "#8a6644", "#a9845a"][k % 3], rough=0.8, grime=0.3, noise_scale=2.0),
                  loc=(x + rnd.uniform(-0.2, 0.2), y + rnd.uniform(-0.3, 0.3), z + 0.05 + k * 0.08), rot=(0, 0, rnd.uniform(-20, 20)), bev=0.01, parent=P)


def cargo_crate(P, loc, yaw, kind, rnd):
    x, y, z = loc
    if kind == "colony":
        FR.framed_box(P, "ccrate", (x, y, z + 0.6), (1.2, 1.2, 1.2), M(STEEL, metal=0.3), M(GRAPHITE, metal=0.3), fw=0.1)
    else:
        K.box("crate", (1.0, 0.8, 0.8), K.mat("wood", WOOD, rough=0.8, grime=0.3, noise_scale=2.0), loc=(x, y, z + 0.4), rot=(0, 0, yaw), bev=0.04, parent=P)


def salvager_ship(o=(0, 0, 0), yaw=0, name="ship", seed=11, scar=True, fires=True, game=False, nacelles=False):
    """The wreck under an empty: its 'fore' and 'aft' children are the two sections. game leaves out what the game
    places itself (the scar is ground paint, the fires, loot crates and scrap piles are POI markers, the debris).
    The port nacelle is gone (see lost_nacelle) unless nacelles is set."""
    rnd = random.Random(seed)
    ROOF.clear()
    root = K.link(bpy.data.objects.new(name, None))
    root.location = o
    root.rotation_euler = (0, 0, math.radians(yaw))
    hm = M("#59606a", metal=0.35, rough=0.5, wear=0.45)
    gm = M("#2f343b", metal=0.4, rough=0.45)

    inner = M("#4a5058", metal=0.3, wear=0.2)
    floor = M("#3b4046", metal=0.4, wear=0.4)
    dark = K.mat("hazk", "#15171a", rough=0.6)
    if scar and not game:
        K.disc("scorch_ring", 26, K.mat("charred", "#5d5a2c", rough=1.0, noise_scale=1.2), (0, -4), z=0.015, wobble=0.2, seed=seed + 3).parent = root
        K.disc("scar", 20, K.mat("burnt", "#3d342b", rough=1.0, noise_scale=1.5), (0, -4), z=0.025, wobble=0.22, seed=seed).parent = root
        K.disc("trench", 8, K.mat("burnt2", "#2a241f", rough=1.0), (0.5, 20), z=0.035, wobble=0.15, seed=seed + 1).parent = root

    # ================================ fore section: y -8 .. 14 hull, nose to 19
    fore = K.link(bpy.data.objects.new("fore", None))
    fore.parent = root
    fore.location = (0, 0, 0.0)
    fore.rotation_euler = (math.radians(-1.5), math.radians(2), 0)
    # openings: the airlock in the starboard wall and lower chamfer (sides 2 and 1) at segment 6 (y 4..6), down to the deck
    hull_run(fore, -8, 14, hm, gm, skip={(1, 6), (2, 6)}, rnd=rnd)
    deck(fore, -8, 14, floor)
    K.box("inner_wall_l", (0.1, 22, H - 2 * C), inner, loc=(-W + 0.05, 3, H / 2), bev=0, parent=fore)
    # nose: a solid tapered prism with the canopy on its upper slope
    bm = bmesh.new()
    a = [bm.verts.new((x, 14, z)) for x, z in octagon()]
    b = [bm.verts.new((x * 0.55, 19.5, z * 0.55 + 0.6)) for x, z in octagon()]
    bm.faces.new(list(reversed(a)))
    bm.faces.new(b)
    for i in range(8):
        j = (i + 1) % 8
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    nose = K.from_bm("nose", bm, hm)
    nose.parent = fore
    K.bevel(nose, 0.06, segs=2, angle=30)
    glass = K.mat("canopy", "#1d4a5c", rough=0.08, metal=0.2, emit=CYAN, emit_strength=0.5)
    K.box("canopy", (6.0, 3.2, 0.12), glass, loc=(0, 16.0, H - 0.75), rot=(-33, 0, 0), bev=0.15, parent=fore)
    for x in (-1.5, 1.5):
        K.box("canopy_mullion", (0.22, 3.3, 0.18), gm, loc=(x, 16.0, H - 0.68), rot=(-33, 0, 0), bev=0.03, parent=fore)
    # bridge front wall with a window band (seen from inside)
    cutw = cutter(fore, (0, 14, 3.9), (6.0, 1.0, 1.4))
    prism("bridge_front", octagon(inset=0.15), 13.85, 14.1, inner, fore, cutters=[cutw])
    K.box("bridge_glass", (6.0, 0.06, 1.4), glass, loc=(0, 13.95, 3.9), bev=0, parent=fore)
    # bulkheads: bridge | crew | engineering
    bulkhead(fore, 10.0, inner)
    bulkhead(fore, 2.0, inner)
    # ---- bridge (y 10..14)
    console(fore, (-2.2, 12.6, 0.3), yaw=0)
    console(fore, (2.2, 12.6, 0.3), yaw=0)
    for x in (-2.2, 2.2):
        K.box("seat", (0.7, 0.7, 0.5), M("#4f6b62", wear=0.1), loc=(x, 11.4, 0.85), bev=0.08, parent=fore)
        K.box("seat_back", (0.7, 0.15, 0.9), M("#4f6b62", wear=0.1), loc=(x, 11.05, 1.4), bev=0.06, parent=fore)
    K.box("logbook_stand", (0.5, 0.5, 1.0), gm, loc=(0, 11.6, 0.8), bev=0.04, parent=fore)
    K.box("logbook", (0.35, 0.45, 0.08), M(MUSTARD, wear=0.2), loc=(0, 11.6, 1.35), rot=(0, 0, 15), bev=0.02, parent=fore)
    ceiling_light(fore, 12.0)
    # ---- crew quarters (y 2..10): corridor with two bunk rooms
    for side in (-1, 1):
        cutd = cutter(fore, (side * 1.4, 6.0, 1.6), (1.0, 1.3, 2.6))
        prism(f"crew_wall{side}", [(-0.1, 0.3), (0.1, 0.3), (0.1, H - 0.4), (-0.1, H - 0.4)], 2.1, 9.9, inner, fore, cutters=[cutd]).location.x = side * 1.4
        for yy in (3.4, 8.4):
            bunk(fore, (side * 3.4, yy, 0.3), yaw=0)
        FR.framed_box(fore, "locker", (side * 3.9, 6.0, 1.3), (0.9, 1.6, 2.0), M(STEEL, metal=0.3), gm, fw=0.07)
    ceiling_light(fore, 6.0)
    ceiling_light(fore, 4.0, x=3.3, on=False)
    # ---- engineering (y -8..2): the Master Battery, pipes, a generator
    FR.framed_box(fore, "master_battery", (0, -2.5, 1.5), (1.8, 1.4, 2.4), M(GRAPHITE, metal=0.3), gm, fw=0.12, screws=gm)
    cu = K.mat("copper", COPPER, rough=0.35, metal=0.8, wear=0.3)
    for x in (-0.5, 0.5):
        K.cyl("coil_core", 0.22, 1.6, gm, loc=(x, -3.25, 1.5), bev=0.02, parent=fore)
        for k in range(8):
            K.torus("coil", 0.26, 0.05, cu, loc=(x, -3.25, 0.85 + k * 0.18), parent=fore)
    FR.screen(fore, (0, -1.76, 2.2), 1.0, 0.45, M(GRAPHITE), glow(CYAN, 1.2))
    FR.label(fore, "MASTER CELL", (0, -1.85, 1.75), 0.14, M(BONE, wear=0, scratches=0, grime=0))
    FR.framed_box(fore, "generator", (-3.2, -6.0, 1.2), (2.2, 2.4, 1.8), M(STEEL, metal=0.3), gm, fw=0.1)
    FR.vent(fore, (-3.2, -7.22, 1.2), 1.4, 1.0, gm, M(GRAPHITE))
    for k in range(3):
        K.cyl("pipe_run", 0.12, 9.5, cu if k == 1 else gm, loc=(2.8 + k * 0.35, -3.0, H - 1.3), rot=(90, 0, 0), bev=0, parent=fore)
    FR.hazard(fore, (0, -0.9, 0.32), 2.6, 0.02, M(MUSTARD), dark, n=10)
    ceiling_light(fore, -1.0)
    ceiling_light(fore, -5.0, on=False)
    torn_end(fore, -8.0, -1, hm, gm, rnd)
    # exterior: hazard band and stencil, running lights, emblem, airlock door hanging open
    for k in range(10):
        K.box("haz", (0.06, 0.5, 0.7), glow(CYAN, 0.9) if k % 2 == 0 else dark, loc=(W + 0.27, 10.6 + k * 0.5, 1.6), bev=0.01, parent=fore)
    FR.label(fore, "SV-04 SALVAGER", (W + 0.3, -1.5, 3.6), 0.55, M(BONE, wear=0.4, grime=0.2), rot=(90, 0, 90))
    K.box("run_light", (0.1, 18.0, 0.12), glow(CYAN, 3), loc=(W + 0.3, 3.0, H - C + 0.1), bev=0.02, parent=fore)
    K.cyl("emblem", 0.8, 0.1, gm, loc=(W + 0.3, 8.0, 3.4), rot=(0, 90, 0), bev=0.02, parent=fore)
    K.torus("emblem_ring", 0.6, 0.07, glow(CYAN, 3), loc=(W + 0.36, 8.0, 3.4), rot=(0, 90, 0), parent=fore)
    K.box("airlock_door", (0.2, 1.9, 3.0), gm, loc=(W + 2.6, 6.6, 0.35), rot=(0, 78, 25), bev=0.05, parent=fore)
    K.box("airlock_step", (1.4, 1.9, 0.16), gm, loc=(W + 0.2, 5.0, 0.12), bev=0.02, parent=fore)
    for y in (4.0, 6.0):  # the airlock frame
        K.box("airlock_post", (0.5, 0.3, 4.6), gm, loc=(W - 0.1, y, 2.3), bev=0.04, parent=fore)
    FR.hazard(fore, (W + 0.02, 5.0, 4.45), 1.7, 0.2, M(MUSTARD), dark, n=6, axis="x")
    K.cyl("antenna", 0.06, 3.0, gm, loc=(2.0, 6.0, H + 1.4), rot=(15, 0, 0), parent=fore)

    # ================================ aft section: local y -10 .. 6, broken end at +6
    aft = K.link(bpy.data.objects.new("aft", None))
    aft.parent = root
    aft.location = (-2.5, -17.5, 0.1)
    aft.rotation_euler = (math.radians(1.0), math.radians(-3), math.radians(-16))
    # cargo door: the port wall and lower chamfer (sides 6 and 7) open over segments 4..5, the door dropped as a ramp
    hull_run(aft, -10, 6, hm, gm, skip={(6, 4), (6, 5), (7, 4), (7, 5)}, rnd=rnd)
    deck(aft, -10, 6, floor)
    bulkhead(aft, -4.0, inner, door=(0.0, 1.6, 2.6))
    K.box("ramp", (3.6, 4.0, 0.2), gm, loc=(-W - 1.75, 0.0, 0.17), rot=(0, 4.5, 0), bev=0.04, parent=aft)
    FR.hazard(aft, (-W - 1.75, -1.95, 0.3), 3.4, 0.08, glow(CYAN, 0.8), dark, n=8)
    # cargo hold (y -4..6): racks, crates, starting scrap
    for x in (3.4,):
        for k in range(3):
            FR.framed_box(aft, "rack", (x, -2.5 + k * 2.6, 1.6), (1.4, 2.4, 2.8), K.mat("rackin", "#2a2e33", rough=0.8), gm, fw=0.1)
    for k, (x, y) in enumerate(((-2.5, 1.0), (-1.2, 3.8), (1.0, 4.5), (-3.0, -2.5), (1.5, -2.0))):
        if not game or k % 2:  # in the game the wooden ones are loot crates (POI markers)
            cargo_crate(aft, (x, y, 0.3), rnd.uniform(0, 40), "colony" if k % 2 else "wood", rnd)
    if not game:
        scrap_pile(aft, (0.0, 1.0, 0.3), rnd, "metal")
        scrap_pile(aft, (-1.5, -1.0, 0.3), rnd, "wood")
    ceiling_light(aft, 0.0)
    ceiling_light(aft, 3.5, on=False)
    torn_end(aft, 6.0, 1, hm, gm, rnd)
    # engine room (y -10..-4) and the nacelles outside
    FR.framed_box(aft, "engine_block", (0, -7.2, 1.6), (3.2, 3.6, 2.6), M(GRAPHITE, metal=0.3), gm, fw=0.14, screws=gm)
    for k in range(4):
        K.cyl("eng_coil", 0.18, 2.6, cu, loc=(-1.2 + k * 0.8, -5.3, 1.6), bev=0.02, parent=aft)
    ceiling_light(aft, -7.0, on=False)
    K.point("engine_glow", (0, -7.0, 3.6), CYAN, 80, 0.5).parent = aft
    for side in (-1, 1):
        # the port nacelle tore off in flight (the opening cinematic) and lies in the woods: its pylon ends in a stump
        if side == -1 and not nacelles:
            FR.framed_box(aft, "pylon", (side * (W + 0.35), -7.5, 2.4), (1.1, 3.0, 0.9), hm, gm, fw=0.1)
            torn_stub(aft, (side * (W + 0.95), -7.5, 2.4), gm, rnd)
            continue
        nacelle(aft, (side * (W + 1.7), -7.5, 2.4), hm, gm)
        FR.framed_box(aft, "pylon", (side * (W + 0.6), -7.5, 2.4), (1.6, 3.0, 0.9), hm, gm, fw=0.1)
    K.box("fin", (0.4, 4.0, 3.0), hm, loc=(0, -8.0, H + 1.4), rot=(15, 0, 0), bev=0.06, parent=aft)
    K.box("fin_light", (0.45, 0.4, 0.3), glow(CYAN, 6), loc=(0, -9.6, H + 2.6), bev=0.04, parent=aft)

    if game:
        return root
    # ================================ debris round the wreck
    for k in range(50):
        a = rnd.uniform(0, 2 * math.pi)
        d = rnd.uniform(9, 22)
        K.box("debris", (rnd.uniform(0.3, 1.4), rnd.uniform(0.3, 1.2), 0.12), hm if k % 3 else gm,
              loc=(d * math.cos(a) * 0.8, d * math.sin(a) * 1.3 - 4, 0.08), rot=(rnd.uniform(-15, 15), rnd.uniform(-15, 15), rnd.uniform(0, 360)), bev=0.02, parent=root)
    if fires:
        fire(fore, (-1.0, -5.0, 0.3), 0.8)        # one in engineering
        fire(root, (6.5, 2.0, 0.0), 1.1)          # one by the airlock
        fire(root, (-6.5, -13.0, 0.0), 1.0)       # one at the break
    return root
