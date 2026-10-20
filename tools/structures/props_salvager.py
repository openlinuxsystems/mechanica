"""Salvager Tech props for the scenes between places (docs/world-generation.md, Scenes): the SV-04's gear scattered
along its way down, in the Salvager Tech family of docs/art-direction.md (hull-grey plates in gunmetal frames, cyan
running lights gone dim, copper on the machinery, framed cases like the part models).

An escape pod on its legs, a torn-off landing leg, a cargo container, a stack of crates, spilled fuel cells, a cracked
power core, a comm dish, a sensor buoy, a trail of hull panels, an engine nacelle, a landing strut with its wheel, a
cockpit section, an ejection seat, a med crate, a supply drop under its chute, a navigation beacon, a flare post, a
radiator panel, a coolant tank, a torn cable bundle, an airlock door, a hull rib, a solar wing, a thruster cluster and
a scanner drone.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import kit as K
import frame as FR
from families import M, glow, shell_panel, BONE, MUSTARD, GRAPHITE, SIGNAL, ZINC, CYAN, HULL
from props import _root
from props_colony_b import (label, _grp, beam, rod, cable, plate_xz, lathe, tyre, mound, _hm, _gm, _copper,
                            DARK, GLASS, CELL, SOIL, SCORCH)

CHAR = "#2a2622"           # heat-shield char and burnt-out flare ends
MEDIC = "#dfe2de"          # the med crate's white enamel
CUSHION = "#4f6b62"        # seat padding, as in the drop pod
CHUTE = "#cfd3cf"          # parachute nylon, sun-faded
CABLE = "#2b2d30"


def _dark():
    return K.mat("hazk", DARK, rough=0.6)


def _char():
    return K.mat("char", CHAR, rough=0.95, grime=0.2, cracks=0.6, noise_scale=3.0)


def _glass():
    return K.mat("darkglass", GLASS, rough=0.15, metal=0.2)


def _paint(col=CYAN):
    """Cyan as paint (stripes, crosses), not as a light."""
    return M(col, metal=0.2, wear=0.2, grime=0.25, rough=0.5)


def _bone():
    return M(BONE, wear=0.4, grime=0.3)


def _zinc():
    return M(ZINC, metal=0.6, wear=0.3)


def _settle(G, z=0.0):
    """Drop (or lift) G so the lowest point of its meshes sits at height z."""
    bpy.context.view_layer.update()
    lo = min((ob.matrix_world @ Vector(c)).z for ob in G.children_recursive if ob.type in ("MESH", "FONT") for c in ob.bound_box)
    G.location.z += z - lo
    bpy.context.view_layer.update()
    return G


def sweep(P, name, path, prof, mat, up=(0, 0, 1), smooth=True, caps=True):
    """A closed profile (a, b) swept along a path: a is across the path (perpendicular to up), b along up's side."""
    bm = bmesh.new()
    path = [Vector(p) for p in path]
    n = len(path)
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        u = Vector(up)
        if abs(t.dot(u)) > 0.98:
            u = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
        side = t.cross(u).normalized()
        nrm = side.cross(t).normalized()
        rings.append([bm.verts.new(p + side * a + nrm * b) for a, b in prof])
    m = len(prof)
    for i in range(n - 1):
        for j in range(m):
            k = (j + 1) % m
            bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]))
    if caps:
        bm.faces.new(rings[0])
        bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm(name, bm, mat, smooth=smooth)
    return K.place(ob, parent=P)


def _circle(r, n=8):
    return [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n)) for k in range(n)]


def drape(P, name, c, w, d, nx, ny, support, mats, rnd, amp=0.08, gore=0, hole=None, loose=0.0):
    """A sheet of cloth (a parachute) lying over support(x, y) and the ground: folds of up to amp, gores of
    alternating material every `gore` columns, faces where hole(u, v) is true torn out, edges frayed by `loose`."""
    bm = bmesh.new()
    cx, cy = c
    ph = [rnd.uniform(0, 6.3) for _ in range(4)]
    grid = []
    for i in range(nx + 1):
        col = []
        for j in range(ny + 1):
            u, v = i / nx, j / ny
            edge = i in (0, nx) or j in (0, ny)
            jx = rnd.uniform(-loose, loose) if edge else 0.0
            jy = rnd.uniform(-loose, loose) if edge else 0.0
            x, y = cx - w / 2 + w * u + jx, cy - d / 2 + d * v + jy
            f = (0.5 + 0.5 * math.sin(x * 4.1 + y * 1.3 + ph[0])) * (0.6 + 0.4 * math.sin(y * 2.7 - x * 0.9 + ph[1]))
            f += 0.35 * (0.5 + 0.5 * math.sin(x * 9.0 - y * 5.0 + ph[2]))
            col.append(bm.verts.new((x, y, support(x, y) + 0.012 + amp * f)))
        grid.append(col)
    for i in range(nx):
        for j in range(ny):
            if hole and hole((i + 0.5) / nx, (j + 0.5) / ny):
                continue
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            f.material_index = (i // gore) % len(mats) if gore else 0
    loose_v = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose_v, context="VERTS")
    ob = K.from_bm(name, bm, mats[0], smooth=True)
    for m in mats[1:]:
        ob.data.materials.append(m)
    ob.modifiers.new("thick", "SOLIDIFY").thickness = 0.008
    return K.place(ob, parent=P)


def jagged(P, name, loc, rot, w, h, mat, rnd, n=4, t=0.03):
    """A torn plate: a strip whose free edge is ragged (plate_xz outline), for torn ends and rips."""
    pts = [(-w / 2, 0), (w / 2, 0)]
    for k in range(n, -1, -1):
        pts.append((-w / 2 + w * k / n + rnd.uniform(-0.03, 0.03) * w, h * rnd.uniform(0.45, 1.0)))
    return plate_xz(name, pts, t, mat, loc=loc, rot=rot, parent=P, bev=0.006)


# ================================================================== the ship's own craft
def escape_pod(seed=41):
    """An intact two-seat escape pod standing on its four legs, hatch shut: an octagonal frustum of hull plates on a
    charred heat shield, a docking collar on top, a step under the hatch and a dim cyan status strip."""
    rnd = random.Random(seed)
    P = _root("escapepod")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    zb = 0.62
    r0, r1, h = 1.35, 0.9, 2.15
    zs = zb + 0.28
    rot8 = (0, 0, 22.5)

    def rad(z):
        return r0 + (r1 - r0) * (z - zs) / h

    def ap(z):
        return rad(z) * math.cos(math.radians(22.5))

    K.cyl("shield", r0 + 0.04, 0.28, _char(), loc=(0, 0, zb + 0.14), rot=rot8, segs=8, r2=r0 + 0.08, bev=0.03, parent=P)
    K.cyl("shield_plate", 0.8, 0.06, gm, loc=(0, 0, zb - 0.02), rot=rot8, segs=8, bev=0.01, parent=P)
    K.cyl("body", r0, h, hm, loc=(0, 0, zs + h / 2), rot=rot8, segs=8, r2=r1, bev=0.03, parent=P)
    K.cyl("cap", r1 + 0.03, 0.28, gm, loc=(0, 0, zs + h + 0.14), rot=rot8, segs=8, r2=0.55, bev=0.03, parent=P)
    K.cyl("dock", 0.45, 0.16, gm, loc=(0, 0, zs + h + 0.36), segs=16, bev=0.02, parent=P)
    K.cyl("dock_lid", 0.36, 0.05, hm, loc=(0, 0, zs + h + 0.465), segs=16, bev=0.01, parent=P)
    for k in range(6):
        a = 2 * math.pi * k / 6
        K.cyl("nut", 0.025, 0.03, _zinc(), loc=(math.cos(a) * 0.41, math.sin(a) * 0.41, zs + h + 0.445), segs=6, bev=0, parent=P)
    K.cyl("lamp", 0.06, 0.1, glow(CYAN, 1.2), loc=(0.62, 0.0, zs + h + 0.2), segs=10, bev=0.01, parent=P)
    for z in (zs + 0.3, zs + h - 0.4):
        K.cyl(f"ring{z:.1f}", rad(z) + 0.035, 0.12, gm, loc=(0, 0, z), rot=rot8, segs=8, r2=rad(z + 0.12) + 0.035, bev=0.015, parent=P)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        p0 = Vector((math.cos(a) * rad(zs + 0.05), math.sin(a) * rad(zs + 0.05), zs + 0.05))
        p1 = Vector((math.cos(a) * rad(zs + h - 0.05), math.sin(a) * rad(zs + h - 0.05), zs + h - 0.05))
        beam(P, f"edge{k}", p0, p1, 0.04, gm)
    slant = math.degrees(math.atan((r0 - r1) / h))
    # the hatch on the -y face, shut: framed door, a porthole, a handle, hinges, a cyan status strip
    zc, hh, hw = zs + 1.0, 1.15, 0.6
    H = _grp(P, "hatch", (0, -ap(zc), zc), (-slant, 0, 0))
    K.box("door", (hw, 0.06, hh), hm, loc=(0, -0.02, 0), bev=0.015, parent=H)
    K.box("door_seal", (hw + 0.04, 0.02, hh + 0.04), dark, loc=(0, 0.0, 0), bev=0, parent=H)
    for s in (-1, 1):
        K.box(f"frame_v{s}", (0.07, 0.07, hh + 0.14), gm, loc=(s * (hw / 2 + 0.035), -0.03, 0), bev=0.012, parent=H)
        K.box(f"frame_h{s}", (hw, 0.07, 0.07), gm, loc=(0, -0.03, s * (hh / 2 + 0.035)), bev=0.012, parent=H)
        K.cyl(f"hinge{s}", 0.035, 0.14, gm, loc=(hw / 2 + 0.08, -0.05, s * 0.38), segs=8, bev=0, parent=H)
    K.cyl("porthole_rim", 0.15, 0.04, gm, loc=(0, -0.06, 0.25), rot=(90, 0, 0), segs=16, bev=0.01, parent=H)
    K.cyl("porthole", 0.11, 0.05, _glass(), loc=(0, -0.065, 0.25), rot=(90, 0, 0), segs=16, bev=0, parent=H)
    K.box("handle", (0.05, 0.06, 0.26), gm, loc=(-hw / 2 + 0.1, -0.08, -0.1), bev=0.01, parent=H)
    K.box("door_rib", (hw - 0.12, 0.03, 0.05), gm, loc=(0, -0.055, -0.25), bev=0.008, parent=H)
    K.box("status", (0.05, 0.03, 0.4), glow(CYAN, 0.8), loc=(-hw / 2 - 0.13, -0.035, 0.1), bev=0.005, parent=H)
    FR.hazard(H, (0, -0.05, -hh / 2 - 0.15), hw + 0.1, 0.08, glow(CYAN, 0.6), dark, n=6)
    label(H, "EP-2", (0.0, -0.065, hh / 2 + 0.17), 0.1, _bone())
    # the step under the hatch
    K.box("step", (0.6, 0.3, 0.05), gm, loc=(0, -ap(zs) - 0.22, zs - 0.15), bev=0.01, parent=P)
    for s in (-1, 1):
        beam(P, f"step_arm{s}", (s * 0.25, -ap(zs) + 0.02, zs + 0.08), (s * 0.25, -ap(zs) - 0.3, zs - 0.16), 0.04, gm)
    # four legs on the diagonal faces, pistons into broad pads
    for k in range(4):
        a = math.radians(45 + 90 * k)
        d = Vector((math.cos(a), math.sin(a), 0))
        top = d * (ap(zs + 0.45) + 0.04) + Vector((0, 0, zs + 0.45))
        knee = d * 1.75 + Vector((0, 0, 0.55))
        foot = d * 2.0 + Vector((0, 0, 0.14))
        K.box(f"leg_mount{k}", (0.24, 0.24, 0.3), gm, loc=top - d * 0.02, rot=(0, 0, math.degrees(a)), bev=0.02, parent=P)
        beam(P, f"leg{k}", top, knee, 0.13, gm)
        rod(P, f"piston{k}", knee, foot, 0.05, _zinc(), segs=10)
        K.cyl(f"piston_sleeve{k}", 0.085, 0.22, gm, loc=knee + (foot - knee).normalized() * 0.1, segs=10, bev=0.01, parent=P).rotation_euler = \
            Vector((0, 0, 1)).rotation_difference(foot - knee).to_euler()
        beam(P, f"strut{k}", d * 0.85 + Vector((0, 0, zb + 0.05)), knee - Vector((0, 0, 0.05)), 0.07, gm)
        K.cyl(f"pad{k}", 0.27, 0.07, gm, loc=foot - Vector((0, 0, 0.1)), segs=12, r2=0.22, bev=0.015, parent=P)
        K.cyl(f"ball{k}", 0.08, 0.12, gm, loc=foot, segs=8, bev=0.02, parent=P)
    # copper lines down the side faces, the ship's name on the +x face
    for k in (0, 2):
        n = Vector((1, 0, 0)) if k == 0 else Vector((-1, 0, 0))
        rod(P, f"line{k}", n * (ap(zs + 0.45) + 0.04) + Vector((0, 0.3, zs + 0.45)), n * (ap(zs + h - 0.45) + 0.04) + Vector((0, 0.3, zs + h - 0.45)), 0.03, cu)
    label(P, "SV-04", (ap(zs + 1.3) + 0.006, -0.12, zs + 1.3), 0.2, _bone(), rot=(90 - slant, 0, 90))
    FR.vent(P, (-0.0, ap(zs + 1.4) + 0.03, zs + 1.4), 0.5, 0.36, gm, dark)
    return P


def lander_leg(seed=42):
    """A landing leg torn off at its hip: a gunmetal strut and chrome piston, copper hydraulic lines, the foot pad still
    on its ball joint and the mounting bracket ripped out with its bolts."""
    rnd = random.Random(seed)
    P = _root("landerleg")
    hm, gm, cu = _hm(), _gm(), _copper()
    G = _grp(P, "leg", (0, 0, 0), (0, 0, 14))
    G.scale = (1.3, 1.3, 1.3)   # modelled at 1:1.3; the SV-04 stands on legs 4 m long
    a, k, b = Vector((-1.35, 0, 0.32)), Vector((0.15, 0, 0.3)), Vector((1.15, 0, 0.27))
    rod(G, "strut", a, k, 0.14, gm, segs=14)
    for x in (-0.9, -0.2):
        K.cyl(f"band{x}", 0.16, 0.08, hm, loc=(x, 0, 0.315), rot=(0, 90, 0), segs=14, bev=0.01, parent=G)
    K.cyl("gland", 0.17, 0.12, gm, loc=(0.18, 0, 0.3), rot=(0, 90, 0), segs=14, bev=0.015, parent=G)
    rod(G, "piston", k, b, 0.085, _zinc(), segs=12)
    # the drag brace, folded along the strut and snapped at its upper end
    beam(G, "brace", (-0.95, 0.22, 0.42), (0.6, 0.2, 0.3), 0.08, gm)
    K.box("brace_clevis", (0.16, 0.12, 0.14), gm, loc=(0.6, 0.12, 0.3), bev=0.012, parent=G)
    jagged(G, "brace_snap", (-1.05, 0.22, 0.42), (0, -90, 0), 0.08, 0.14, gm, rnd, n=3, t=0.08)
    # the foot: ball joint, clevis and a ribbed pad lying flat
    K.box("clevis", (0.22, 0.2, 0.2), gm, loc=(1.22, 0, 0.27), bev=0.02, parent=G)
    K.sphere("ball", 0.1, gm, loc=(1.36, 0, 0.2), parent=G)
    K.cyl("pad", 0.5, 0.1, gm, loc=(1.45, 0, 0.05), segs=16, r2=0.42, bev=0.02, parent=G)
    K.cyl("pad_top", 0.3, 0.04, hm, loc=(1.45, 0, 0.12), segs=16, bev=0.01, parent=G)
    for j in range(6):
        ang = j * 60
        K.box(f"pad_rib{j}", (0.36, 0.04, 0.06), gm, loc=(1.45 + math.cos(math.radians(ang)) * 0.24, math.sin(math.radians(ang)) * 0.24, 0.13),
              rot=(0, 0, ang), bev=0.005, parent=G)
    # the hip: a framed bracket ripped off the hull, its bolts sheared
    FR.framed_box(G, "hip", (-1.55, 0, 0.3), (0.42, 0.5, 0.5), hm, gm, fw=0.06)
    for j in range(3):
        jagged(G, f"hip_plate{j}", (-1.8, -0.18 + j * 0.18, 0.3 + rnd.uniform(-0.1, 0.1)), (rnd.uniform(-20, 20), -90 + rnd.uniform(-25, 25), rnd.uniform(-20, 20)),
               0.2, 0.3, hm if j % 2 else gm, rnd, n=3, t=0.02)
    for j in range(4):
        y, z = (-0.15, 0.15)[j % 2], (0.15, 0.45)[j // 2]
        K.cyl(f"stud{j}", 0.025, 0.12, _zinc(), loc=(-1.79, y, z), rot=(0, 90, 0), segs=6, bev=0, parent=G)
    # hydraulic lines along the strut, torn loose at the hip
    for j, y in enumerate((-0.16, -0.2)):
        cable(G, f"hyd{j}", [(1.1, y + 0.04, 0.3), (0.2, y, 0.42), (-1.3, y, 0.46), (-1.75, y - 0.1, 0.42 - 0.1 * j), (-2.05, y - 0.3 - 0.15 * j, 0.04)], 0.025, cu)
    for x in (-0.6, 0.5):
        K.box(f"clip{x}", (0.06, 0.1, 0.06), gm, loc=(x, -0.18, 0.43), bev=0.005, parent=G)
    label(G, "L3", (-0.5, -0.135, 0.33), 0.12, _bone(), rot=(90, 0, 0))
    _settle(G)
    return P


def cargo_container_salvager(seed=43):
    """A Salvager cargo container, 4 x 2.2 x 2.3 m: a framed case of hull-grey panels with ribbed sides, a cyan stripe
    down its length, two locked doors at the end and the ship's stencil."""
    P = _root("svcontainer")
    hm, gm, dark = _hm(), _gm(), _dark()
    L, W, H = 4.0, 2.2, 2.3
    z0 = 0.1
    FR.framed_box(P, "box", (0, 0, z0 + H / 2), (L, W, H), hm, gm, fw=0.16)
    for x in (-L / 2 + 0.08, L / 2 - 0.08):
        for y in (-W / 2 + 0.08, W / 2 - 0.08):
            K.box("foot", (0.22, 0.22, z0), gm, loc=(x, y, z0 / 2), bev=0.01, parent=P)
    for s in (-1, 1):
        for k in range(6):
            x = -L / 2 + 0.55 + k * 0.58
            K.box(f"rib{s}{k}", (0.07, 0.05, H - 0.3), gm, loc=(x, s * (W / 2 - 0.01), z0 + H / 2), bev=0.01, parent=P)
        K.box(f"stripe{s}", (L - 0.34, 0.02, 0.2), _paint(), loc=(0, s * (W / 2 + 0.02), z0 + H - 0.55), bev=0.003, parent=P)
        K.box(f"stripe_t{s}", (L - 0.34, 0.02, 0.05), dark, loc=(0, s * (W / 2 + 0.02), z0 + H - 0.4), bev=0, parent=P)
    label(P, "SV-04", (-0.9, -W / 2 - 0.035, z0 + 1.1), 0.36, _bone())
    label(P, "CARGO 07", (0.9, -W / 2 - 0.035, z0 + 1.1), 0.2, _bone())
    FR.hazard(P, (-1.3, -W / 2 - 0.03, z0 + 0.35), 0.8, 0.14, M(MUSTARD), dark, n=6)
    # the doors on the +x end: two leaves, lock bars and handles, a status light
    xe = L / 2 + 0.01
    for s in (-1, 1):
        K.box(f"leaf{s}", (0.04, W / 2 - 0.2, H - 0.36), hm, loc=(xe, s * (W / 4 - 0.02), z0 + H / 2), bev=0.01, parent=P)
        for y in (0.2, 0.62):
            yy = s * y
            K.cyl(f"lockbar{s}{y}", 0.03, H - 0.3, gm, loc=(xe + 0.05, yy, z0 + H / 2), segs=8, bev=0, parent=P)
            K.box(f"handle{s}{y}", (0.06, 0.2, 0.05), M(MUSTARD, wear=0.5), loc=(xe + 0.09, yy - s * 0.08, z0 + 1.0), bev=0.008, parent=P)
            for z in (0.35, H - 0.35):
                K.box(f"keeper{s}{y}{z}", (0.06, 0.09, 0.08), gm, loc=(xe + 0.05, yy, z0 + z), bev=0.008, parent=P)
    K.box("door_gap", (0.045, 0.02, H - 0.36), dark, loc=(xe + 0.002, 0, z0 + H / 2), bev=0, parent=P)
    K.box("status", (0.03, 0.18, 0.06), glow(CYAN, 0.9), loc=(xe + 0.02, -W / 2 + 0.35, z0 + H - 0.25), bev=0.005, parent=P)
    return P


def _crate(P, name, loc, yaw, seed, w=1.0, d=0.7, h=0.55, tag=None):
    """A Salvager crate: framed hull-grey case, a cyan band, latches on the front and handles on the ends."""
    rnd = random.Random(seed)
    hm, gm, dark = _hm(), _gm(), _dark()
    G = _grp(P, name, loc, (0, 0, yaw))
    FR.framed_box(G, f"{name}_case", (0, 0, h / 2), (w, d, h), hm, gm, fw=0.06, bev=0.012)
    K.box(f"{name}_band", (w - 0.14, 0.015, 0.06), _paint(), loc=(0, -d / 2 + 0.03, h * 0.3), bev=0, parent=G)
    for x in (-w * 0.3, w * 0.3):
        K.box(f"{name}_latch", (0.08, 0.04, 0.1), gm, loc=(x, -d / 2 + 0.01, h - 0.1), bev=0.006, parent=G)
    for s in (-1, 1):
        K.box(f"{name}_handle", (0.035, 0.26, 0.035), gm, loc=(s * (w / 2 + 0.035), 0, h * 0.6), bev=0.006, parent=G)
    if tag:
        label(G, tag, (0, -d / 2 + 0.018, h * 0.58), 0.09, _bone())
    return G


def crate_salvager_stack(seed=44):
    """Three Salvager crates, two side by side and one on top skewed, as they came off the cargo net."""
    P = _root("svcrates")
    _crate(P, "crate_a", (-0.53, 0.0, 0), 2, seed, tag="SV-04")
    _crate(P, "crate_b", (0.53, 0.04, 0), -3, seed + 1)
    _crate(P, "crate_c", (0.12, -0.02, 0.555), 14, seed + 2, w=0.9, d=0.65, h=0.5, tag="TOOLS")
    return P


def _fuel_cell(P, name, loc, rot, L=0.85, r=0.15, crack=False):
    """A fuel cell: a gunmetal canister with a cyan sight window, framed end caps and copper terminals."""
    gm, hm, cu = _gm(), _hm(), _copper()
    G = _grp(P, name, loc, rot)   # axis along x
    if crack:
        for s in (-1, 1):
            K.cyl(f"{name}_half{s}", r, L / 2 - 0.08, gm, loc=(s * (L / 4 + 0.04), 0, 0), rot=(0, 90, 0), segs=12, bev=0.01, parent=G)
            K.cyl(f"{name}_inner{s}", r - 0.02, 0.01, glow(CYAN, 1.2), loc=(s * 0.08, 0, 0), rot=(0, 90, 0), segs=12, bev=0, parent=G)
    else:
        K.cyl(f"{name}_body", r, L - 0.12, gm, rot=(0, 90, 0), segs=12, bev=0.01, parent=G)
        K.box(f"{name}_window", (L * 0.5, 0.03, r * 0.6), glow(CYAN, 0.7), loc=(0, -r + 0.01, 0), bev=0.004, parent=G)
    for s in (-1, 1):
        K.cyl(f"{name}_cap{s}", r + 0.02, 0.08, hm, loc=(s * (L / 2 - 0.04), 0, 0), rot=(0, 90, 0), segs=12, bev=0.012, parent=G)
        K.cyl(f"{name}_term{s}", 0.035, 0.06, cu, loc=(s * (L / 2 + 0.02), 0, 0), rot=(0, 90, 0), segs=8, bev=0, parent=G)
    return G


def fuel_cell_spill(seed=45):
    """A carrier of fuel cells tipped out on the ground: two still racked, three rolled loose, one split in half and
    its cyan coolant spreading in a pool."""
    rnd = random.Random(seed)
    P = _root("fuelcells")
    gm = _gm()
    # the carrier, tipped on its side: an open frame of square bar
    C = _grp(P, "carrier", (-0.55, 0.25, 0.0), (0, 0, 8))
    w, d, h = 1.0, 0.75, 0.38
    for y in (-d / 2, d / 2):
        for z in (0.03, h):
            beam(C, f"rail{y}{z}", (-w / 2, y, z), (w / 2, y, z), 0.05, gm)
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            beam(C, f"post{x}{y}", (x, y, 0.03), (x, y, h), 0.05, gm)
        beam(C, f"end{x}", (x, -d / 2, 0.03), (x, d / 2, 0.03), 0.05, gm)
        beam(C, f"end_t{x}", (x, -d / 2, h), (x, d / 2, h), 0.05, gm)
    for k in range(3):
        K.box(f"saddle{k}", (w - 0.06, 0.04, 0.04), gm, loc=(0, -0.22 + k * 0.22, 0.06), bev=0, parent=C)
    _fuel_cell(C, "cell_a", (0, 0.12, 0.2), (0, 0, 0))
    _fuel_cell(C, "cell_b", (0.02, -0.18, 0.2), (0, 0, 0))
    _fuel_cell(P, "cell_c", (0.6, -0.3, 0.15), (0, 0, 35))
    _fuel_cell(P, "cell_d", (0.25, 0.65, 0.15), (0, 0, -70))
    _fuel_cell(P, "cell_e", (0.95, 0.35, 0.15), (0, 0, 12), crack=True)
    # the split cell's spill: a glassy cyan pool, darker where it soaked in
    K.disc("spill", 0.65, K.mat("spill", CYAN, rough=0.05, metal=0.1, emit=CYAN, emit_strength=0.35), (1.15, 0.1), z=0.008, segs=28, wobble=0.35,
           seed=seed).parent = P
    K.disc("spill_wet", 0.95, K.mat("wet_soil", "#3c4a48", rough=0.3), (1.15, 0.1), z=0.004, segs=28, wobble=0.3, seed=seed + 2).parent = P
    for k in range(3):
        K.box(f"shard{k}", (0.12, 0.08, 0.01), gm, loc=(0.95 + rnd.uniform(-0.3, 0.3), 0.35 + rnd.uniform(-0.3, 0.2), 0.012), rot=(0, 0, rnd.uniform(0, 180)), bev=0,
              parent=P)
    return P


def power_core_cracked(seed=46):
    """The SV-04's power core, 1.9 m across and 2.6 m tall, cracked open down its front: curved hull plates in a
    gunmetal frame, copper coils round its head, a dim cyan column inside and plate shards in the grass."""
    rnd = random.Random(seed)
    P = _root("powercore")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    G = _grp(P, "core", (0, 0, 0), (2, -3, 34))
    R, Hc, z0 = 0.95, 2.1, 0.3
    K.cyl("base", R + 0.12, z0, gm, loc=(0, 0, z0 / 2), segs=16, bev=0.03, parent=G)
    K.cyl("head", R + 0.1, 0.28, gm, loc=(0, 0, z0 + Hc + 0.14), segs=16, bev=0.03, parent=G)
    K.cyl("dome", R * 0.8, 0.22, hm, loc=(0, 0, z0 + Hc + 0.39), segs=16, r2=R * 0.45, bev=0.03, parent=G)
    K.cyl("vent", 0.3, 0.12, gm, loc=(0, 0, z0 + Hc + 0.55), segs=12, bev=0.015, parent=G)
    # the shell: 8 columns x 3 rows of curved plates; the front ones (around -y) blown out
    S = _grp(G, "shell", (0, 0, z0), (90, 0, 0))   # shell_panel's y is the core's height
    cols, rows = 8, 3
    gone = {(2, 1), (1, 1), (2, 2), (2, 0)}
    for r in range(rows):
        for c in range(cols):
            if (c, r) in gone:
                continue
            a0 = 2 * math.pi * c / cols + 0.02
            a1 = 2 * math.pi * (c + 1) / cols - 0.02
            y0, y1 = Hc * r / rows + 0.02, Hc * (r + 1) / rows - 0.02
            p = shell_panel(f"plate{r}{c}", R, a0, a1, y0, y1, hm, t=0.06, parent=S)
            if (c, r) in ((1, 2), (3, 1)):
                p.rotation_euler = (0, rnd.uniform(-0.12, 0.12), 0)
    for c in range(cols):   # frame posts between the columns
        a = 2 * math.pi * c / cols
        beam(G, f"post{c}", (math.cos(a) * (R + 0.02), -math.sin(a) * (R + 0.02), z0), (math.cos(a) * (R + 0.02), -math.sin(a) * (R + 0.02), z0 + Hc), 0.08, gm)
    for k in (1, 2):
        K.torus(f"hoop{k}", R + 0.03, 0.035, gm, loc=(0, 0, z0 + Hc * k / rows), arc=360, segs=32, parent=G)
    # the torn edges of the gap: bent plate tongues
    for k, (a, z) in enumerate(((math.radians(-112), 1.0), (math.radians(-68), 1.25), (math.radians(-100), 1.8), (math.radians(-60), 0.55))):
        d = Vector((math.cos(a), math.sin(a), 0))
        jg = jagged(G, f"tongue{k}", d * (R + 0.04) + Vector((0, 0, z0 + z)), (0, 0, 0), 0.32, 0.4, hm, rnd, n=3, t=0.04)
        jg.rotation_euler = (math.radians(rnd.uniform(-40, 40)), 0, a + math.pi / 2)
    # inside: the glowing column with dark containment rings and a crack across it
    K.cyl("column", 0.5, Hc - 0.1, glow(CYAN, 1.4), loc=(0, 0, z0 + Hc / 2), segs=16, bev=0.02, parent=G)
    K.cyl("liner", R - 0.08, Hc - 0.05, dark, loc=(0, 0.25, z0 + Hc / 2), segs=16, bev=0, parent=G).scale = (1, 0.55, 1)
    for k in range(4):
        K.torus(f"cring{k}", 0.52, 0.04, gm, loc=(0, 0, z0 + 0.35 + k * 0.48), segs=16, parent=G)
    for k in range(3):
        K.box(f"crack{k}", (0.03, 0.03, rnd.uniform(0.3, 0.6)), dark, loc=(rnd.uniform(-0.2, 0.2), -0.5, z0 + 0.6 + k * 0.45), rot=(0, rnd.uniform(-35, 35), 0),
              bev=0, parent=G)
    # copper coils round the head and cables at the base
    for k in range(3):
        K.torus(f"coil{k}", R + 0.06, 0.03, cu, loc=(0, 0, z0 + Hc - 0.15 - k * 0.07), segs=24, parent=G)
    label(G, "CORE", (math.cos(math.radians(-22)) * (R + 0.01), math.sin(math.radians(-22)) * (R + 0.01) - 0.0, z0 + 1.75), 0.2, _bone(), rot=(90, 0, 68))
    FR.hazard(G, (0.55, -R + 0.15, 0.15), 0.6, 0.12, M(MUSTARD), dark, n=5)
    K.point("core_light", (0, -0.6, z0 + 1.1), CYAN, 25, 0.2).parent = G
    for k in range(4):
        K.box(f"shard{k}", (rnd.uniform(0.3, 0.55), rnd.uniform(0.25, 0.45), 0.05), hm if k % 2 else gm,
              loc=(rnd.uniform(-1.0, 0.8), -1.3 - rnd.uniform(0, 0.6), 0.04), rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180)), bev=0.01, parent=P)
    return P


def antenna_dish_salvager(seed=47):
    """The SV-04's comm dish, 2.6 m, torn off its mast and lying on its rim: ribbed back, a hub with the snapped yoke,
    a feed on three struts (one bent) and copper cables pulled out of the mount."""
    rnd = random.Random(seed)
    P = _root("svdish")
    hm, gm, cu = _hm(), _gm(), _copper()
    from props_colony_a import dish_mesh
    R, depth, t = 1.3, 0.38, 0.04
    D = _grp(P, "dish", (0, 0, 0), (50, 0, 18))   # on its rim, tipped back, the face up towards the front
    K.place(dish_mesh("reflector", R, depth, t, hm), (0, 0, 0), (0, 0, 0), D)
    K.torus("rim", R + 0.01, 0.035, gm, loc=(0, 0, depth - 0.01), segs=40, parent=D)
    K.torus("seam", 0.75, 0.012, _dark(), loc=(0, 0, depth * (0.75 / R) ** 2 + 0.004), segs=32, parent=D)
    for k in range(8):   # ribs behind
        a = 2 * math.pi * k / 8
        c, s = math.cos(a), math.sin(a)
        p = [Vector((c * rr, s * rr, depth * (rr / R) ** 2 - t - 0.04)) for rr in (0.25, 0.8, R - 0.05)]
        for i in range(2):
            beam(D, f"rib{k}{i}", p[i], p[i + 1], 0.05, gm, d=0.08)
    K.cyl("hub", 0.3, 0.25, gm, loc=(0, 0, -0.14), segs=16, bev=0.02, parent=D)
    K.box("yoke", (0.5, 0.18, 0.3), gm, loc=(0, 0, -0.38), bev=0.02, parent=D)
    rod(D, "shaft", (0, 0, -0.45), (0.06, 0.04, -0.85), 0.07, _zinc(), segs=10)
    jagged(D, "shaft_tear", (0.06, 0.04, -0.87), (180, 0, 0), 0.16, 0.1, gm, rnd, n=3, t=0.12)
    for k in range(3):
        cable(D, f"lead{k}", [(0.1 - k * 0.1, 0.12, -0.3), (0.2 - k * 0.15, 0.25, -0.75), (0.1 - k * 0.2, 0.3 + k * 0.1, -1.0 - k * 0.1)], 0.025, cu)
    # the feed on three struts, one buckled
    f = 1.0
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        p0 = Vector((math.cos(a) * (R - 0.05), math.sin(a) * (R - 0.05), depth))
        if k == 1:
            mid = (p0 + Vector((0, 0, f))) / 2 + Vector((0.25, 0, 0))
            rod(D, f"strut{k}a", p0, mid, 0.035, gm)
            rod(D, f"strut{k}b", mid, Vector((0, 0, f - 0.05)), 0.035, gm)
        else:
            rod(D, f"strut{k}", p0, Vector((0, 0, f - 0.05)), 0.035, gm)
    K.cyl("feed", 0.11, 0.3, gm, loc=(0, 0, f + 0.08), segs=14, bev=0.015, parent=D)
    K.cyl("horn", 0.15, 0.12, hm, loc=(0, 0, f - 0.12), segs=14, r2=0.1, bev=0.01, parent=D)
    K.box("feed_led", (0.04, 0.02, 0.04), glow(CYAN, 1.0), loc=(0, -0.11, f + 0.12), bev=0.003, parent=D)
    label(D, "SV-04", (0.0, -0.55, depth * 0.18 + 0.012), 0.16, _bone(), rot=(0, 0, 0))
    _settle(D)
    return P


def sensor_buoy(seed=48):
    """A sensor buoy the SV-04 dropped on the way down, driven nose-first into the ground at a lean: banded body,
    tail fins, a dark sensor dome, a ring of dim cyan lamps and a whip antenna."""
    P = _root("buoy")
    hm, gm, dark = _hm(), _gm(), _dark()
    B = _grp(P, "buoy", (0, 0, -0.45), (16, -9, 0))
    r = 0.3
    K.cyl("nose", r, 0.55, gm, loc=(0, 0, 0.275), segs=16, r2=0.06, bev=0.01, parent=B).rotation_euler = (math.pi, 0, 0)
    K.cyl("body", r, 1.5, hm, loc=(0, 0, 1.3), segs=16, bev=0.02, parent=B)
    for z in (0.7, 1.3, 1.95):
        K.cyl(f"band{z}", r + 0.025, 0.1, gm, loc=(0, 0, z), segs=16, bev=0.012, parent=B)
    K.cyl("collar", r + 0.06, 0.16, gm, loc=(0, 0, 2.1), segs=16, bev=0.02, parent=B)
    for k in range(4):   # swept tail fins round the top
        a = k * 90 + 45
        plate_xz(f"fin{k}", [(r - 0.02, 0.0), (r + 0.16, 0.12), (r + 0.16, 0.36), (r - 0.02, 0.5)], 0.03, hm,
                 loc=(0, 0, 1.5), rot=(0, 0, a), parent=B, bev=0.008)
    K.cyl("head", 0.24, 0.2, gm, loc=(0, 0, 2.28), segs=16, bev=0.02, parent=B)
    K.sphere("dome", 0.2, _glass(), loc=(0, 0, 2.38), scale=(1, 1, 0.75), parent=B)
    for k in range(6):
        a = 2 * math.pi * k / 6
        K.box(f"lamp{k}", (0.05, 0.03, 0.04), glow(CYAN, 1.0), loc=(math.cos(a) * 0.245, math.sin(a) * 0.245, 2.25), rot=(0, 0, math.degrees(a) + 90), bev=0.004,
              parent=B)
    rod(B, "antenna", (0.12, 0.1, 2.35), (0.16, 0.13, 3.25), 0.008, _zinc(), segs=6)
    K.sphere("antenna_tip", 0.018, gm, loc=(0.16, 0.13, 3.25), parent=B)
    K.box("hatch", (0.22, 0.02, 0.32), hm, loc=(0, -r - 0.005, 1.0), bev=0.006, parent=B)
    for z in (0.88, 1.12):
        FR.screw(B, (0.08, -r - 0.016, z), (90, 0, 0), r=0.018, mat=gm)
    label(B, "SB-3", (0, -r - 0.004, 1.58), 0.1, _bone())
    FR.hazard(B, (0, -r - 0.012, 0.75), 0.36, 0.06, glow(CYAN, 0.6), dark, n=5)
    mound(P, "dirt", (0.05, -0.2), 0.42, 0.18, seed=seed, n=7)
    return P


def debris_trail_panels(seed=49):
    """A trail of five hull panels shed in a line as the ship came down, 8 m long: the biggest dug in at the end of a
    torn-up furrow, the smaller ones skipping ahead of it."""
    rnd = random.Random(seed)
    P = _root("trail")
    hm, gm, dark = _hm(), _gm(), _dark()
    soil = K.mat("soil", SOIL, rough=1.0, noise_scale=3.0)
    K.strip("furrow", [(-4.2, -0.1), (-2.5, 0.05), (-0.8, -0.05), (1.0, 0.1), (3.0, 0.0), (4.2, 0.08)], 0.55, soil, z=0.008).parent = P
    sizes = [(0.7, 0.5), (0.95, 0.65), (1.2, 0.8), (1.45, 1.0), (1.8, 1.25)]
    xs = [-3.6, -2.0, -0.4, 1.3, 3.2]
    for k, ((w, h), x) in enumerate(zip(sizes, xs)):
        tilt = rnd.uniform(28, 42) if k < 4 else 55
        G = _grp(P, f"panel{k}", (x, rnd.uniform(-0.15, 0.15), 0), (tilt - 90, rnd.uniform(-10, 10), rnd.uniform(-35, 35)))
        pts = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2 - rnd.uniform(0, 0.1), h * 0.1), (w / 2 - rnd.uniform(0.05, 0.15), h / 2),
               (rnd.uniform(-0.1, 0.1), h / 2 - rnd.uniform(0, 0.12)), (-w / 2, h / 2)]
        plate_xz(f"skin{k}", pts, 0.05, hm, parent=G, bev=0.012)
        K.box(f"rail{k}", (w, 0.06, 0.07), gm, loc=(0, 0.05, -h / 2 + 0.04), bev=0.01, parent=G)
        K.box(f"rib{k}", (0.06, 0.08, h * 0.85), gm, loc=(-w * 0.2, 0.06, 0), bev=0.01, parent=G)
        K.box(f"seam{k}", (0.025, 0.008, h * 0.8), dark, loc=(w * 0.15, -0.03, 0), bev=0, parent=G)
        for j in range(2):
            K.cyl("rivet", 0.018, 0.02, gm, loc=(w * 0.15 + 0.05, -0.032, -h * 0.25 + j * h * 0.5), rot=(90, 0, 0), segs=6, bev=0, parent=G)
        if k in (1, 4):
            K.box(f"light{k}", (w * 0.6, 0.015, 0.04), glow(CYAN, 0.5), loc=(-w * 0.1, -0.03, h * 0.3), bev=0.003, parent=G)
        if k == 4:
            label(G, "04", (-w * 0.2, -0.031, -h * 0.05), 0.3, _bone())
            FR.hazard(G, (w * 0.25, -0.035, -h * 0.32), 0.45, 0.1, M(MUSTARD), dark, n=4)
        _settle(G, -0.05 if k < 4 else -0.15)
        if k % 2 == 0:
            mound(P, f"dirt{k}", (x - 0.1, G.location.y), 0.25 + 0.12 * k, 0.1 + 0.03 * k, seed=seed + k, n=3 + k // 2, sx=1.4)
    return P


def engine_nacelle_small(seed=50):
    """A small engine nacelle, 3 m long, fallen on its side: a cowled body of hull plates, the intake fan behind its
    lip, the nozzle and back third scorched black, the pylon torn off the top."""
    rnd = random.Random(seed)
    P = _root("nacelle")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    char = _char()
    soot = K.mat("soot", "#1d1b19", rough=1.0)
    G = _grp(P, "nacelle", (0, 0, 0.6), (68, 3, 140))   # rolled so the pylon stub points up and back, intake to the front
    A = _grp(G, "axis", (-1.42, 0, 0), (0, 90, 0))   # the lathe's z becomes the nacelle's x
    # the outside: lip, cowl and a scorched aft cowl to the nozzle, hollow inside
    fore = [(0.47, 0.0), (0.55, 0.08), (0.6, 0.35), (0.62, 1.0), (0.62, 1.7), (0.56, 1.7), (0.56, 0.35), (0.44, 0.12), (0.43, 0.0)]
    lathe("cowl", fore, hm, segs=20, parent=A)
    aft = [(0.62, 1.7), (0.6, 2.3), (0.46, 2.85), (0.4, 2.85), (0.5, 2.3), (0.56, 1.7)]
    lathe("aft", aft, char, segs=20, parent=A)
    K.torus("lip", 0.5, 0.045, gm, loc=(0, 0, 0.02), segs=28, parent=A)
    K.torus("joint", 0.625, 0.04, gm, loc=(0, 0, 1.7), segs=28, parent=A)
    K.torus("nozzle_ring", 0.44, 0.035, gm, loc=(0, 0, 2.84), segs=24, parent=A)
    # the fan behind the intake, a spinner, and the exhaust plug
    K.cyl("fan_disc", 0.42, 0.04, dark, loc=(0, 0, 0.42), segs=20, bev=0, parent=A)
    for k in range(10):
        K.box(f"blade{k}", (0.38, 0.07, 0.015), gm, loc=(math.cos(k * 0.628) * 0.22, math.sin(k * 0.628) * 0.22, 0.38), rot=(25, 0, math.degrees(k * 0.628)),
              bev=0, parent=A)
    K.cyl("spinner", 0.13, 0.22, hm, loc=(0, 0, 0.27), segs=14, r2=0.02, bev=0.01, parent=A).rotation_euler = (math.pi, 0, 0)
    K.cyl("plug", 0.26, 0.5, char, loc=(0, 0, 2.7), segs=14, r2=0.05, bev=0.01, parent=A)
    # panel lines and rivets on the cowl, an access hatch, copper feed lines
    for k in range(4):
        a = math.radians(30 + 90 * k)
        n = Vector((math.cos(a), math.sin(a), 0))
        beam(A, f"panel{k}", n * 0.615 + Vector((0, 0, 0.4)), n * 0.625 + Vector((0, 0, 1.65)), 0.025, dark, d=0.01, bev=0)
    a = math.radians(-60)
    n = Vector((math.cos(a), math.sin(a), 0))
    hp = K.box("hatch", (0.4, 0.02, 0.5), hm, loc=n * 0.63 + Vector((0, 0, 1.05)), bev=0.006, parent=A)
    hp.rotation_euler = n.to_track_quat("Y", "Z").to_euler()
    for k in range(5):   # soot streaks back from the nozzle
        a = 2 * math.pi * k / 5 + 0.3
        n = Vector((math.cos(a), math.sin(a), 0))
        st = K.box(f"streak{k}", (0.2, 0.5, 0.005), soot, loc=n * 0.615 + Vector((0, 0, 1.95)), bev=0, parent=A)
        st.rotation_euler = n.to_track_quat("Z", "Y").to_euler()
    # the pylon stub on top, torn: framed root, ragged skin, copper lines hanging out
    FR.framed_box(A, "pylon", (0.0, 0.7, 1.1), (0.25, 0.22, 0.9), hm, gm, fw=0.05)
    for k in range(3):
        jg = jagged(A, f"pylon_tear{k}", (rnd.uniform(-0.08, 0.08), 0.82, 0.75 + k * 0.3), (0, 0, 0), 0.24, 0.18, hm if k % 2 else gm, rnd, n=3, t=0.02)
        jg.rotation_euler = (math.radians(-90 + rnd.uniform(-25, 25)), 0, math.radians(90))
    for k in range(2):
        cable(A, f"line{k}", [(0.06 - k * 0.12, 0.75, 1.3), (0.1 - k * 0.2, 1.0, 1.45 + k * 0.1), (0.25 - k * 0.5, 1.15, 1.25)], 0.025, cu)
    _settle(G)
    K.disc("burn", 1.0, K.mat("burnt", SCORCH, rough=1.0, noise_scale=1.5), (-1.7, 1.4), z=0.01, wobble=0.3, seed=seed).parent = P
    return P


def landing_strut(seed=51):
    """A main landing strut torn out of its bay and bent at the knee: the wheel still on its fork and upright, the
    chrome oleo bent, the trunnion end lying in the grass with sheared bolts."""
    rnd = random.Random(seed)
    P = _root("strut")
    hm, gm, cu = _hm(), _gm(), _copper()
    G = _grp(P, "strut", (0, 0, 0), (0, 0, -10))
    rw = 0.5
    hub = Vector((1.05, 0, rw))
    tyre(G, "wheel", tuple(hub), rw, 0.32, flat=0.04, rim=M(GRAPHITE, metal=0.4, wear=0.4), axis="y")
    for s in (-1, 1):
        K.torus(f"wheel_groove{s}", rw - 0.005, 0.012, K.mat("tyre", "#26282a", rough=0.9, grime=0.4), loc=tuple(hub + Vector((0, s * 0.07, 0))), rot=(90, 0, 0),
                segs=24, parent=G)
    # the fork: two plates either side of the wheel joined in a crown above it
    crown = Vector((0.75, 0, 1.15))
    for s in (-1, 1):
        beam(G, f"fork{s}", hub + Vector((0, s * 0.24, 0)), crown + Vector((0, s * 0.24, 0)), 0.09, gm, d=0.05)
        K.cyl(f"axle_nut{s}", 0.07, 0.06, _zinc(), loc=tuple(hub + Vector((0, s * 0.28, 0))), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    K.box("crown", (0.28, 0.58, 0.16), gm, loc=tuple(crown), bev=0.02, parent=G)
    # the oleo: chrome piston up from the crown to a bent cylinder, then the trunnion on the ground
    knee = Vector((0.1, 0.0, 1.5))
    rod(G, "piston", crown, crown + (knee - crown) * 0.45, 0.07, _zinc(), segs=12)
    rod(G, "cylinder", crown + (knee - crown) * 0.42, knee, 0.12, gm, segs=14)
    K.sphere("kink", 0.13, gm, loc=tuple(knee), parent=G)
    top = Vector((-1.1, 0.05, 0.3))
    rod(G, "upper", knee, top, 0.13, gm, segs=14)
    K.torus("crease", 0.135, 0.02, M(GRAPHITE, metal=0.4), loc=tuple(knee + (top - knee).normalized() * 0.15), parent=G).rotation_euler = \
        Vector((0, 0, 1)).rotation_difference(top - knee).to_euler()
    K.cyl("gland", 0.15, 0.1, gm, loc=tuple(crown + (knee - crown) * 0.45), segs=14, bev=0.01, parent=G).rotation_euler = \
        Vector((0, 0, 1)).rotation_difference(knee - crown).to_euler()
    # torque links: a scissor on the front
    mid = crown + (knee - crown) * 0.5 + Vector((0.12, -0.2, 0.15))
    beam(G, "link_a", crown + Vector((0, -0.16, 0.05)), mid, 0.07, gm, d=0.04)
    beam(G, "link_b", mid, crown + (knee - crown) * 0.8 + Vector((0, -0.16, 0)), 0.07, gm, d=0.04)
    K.cyl("link_pin", 0.04, 0.1, _zinc(), loc=tuple(mid), rot=(90, 0, 0), segs=8, bev=0, parent=G)
    # the trunnion: a cross tube with its lugs ripped off the hull
    K.cyl("trunnion", 0.11, 0.8, gm, loc=tuple(top + Vector((-0.12, 0, 0))), rot=(90, 0, 0), segs=12, bev=0.015, parent=G)
    for s in (-1, 1):
        K.box(f"lug{s}", (0.3, 0.1, 0.26), hm, loc=tuple(top + Vector((-0.2, s * 0.38, 0.05))), bev=0.015, parent=G)
        jagged(G, f"lug_tear{s}", tuple(top + Vector((-0.38, s * 0.38, 0.05))), (rnd.uniform(-20, 20), -90, 0), 0.24, 0.12, hm, rnd, n=3, t=0.1)
    cable(G, "brake_line", [(1.0, -0.2, rw + 0.05), (0.78, -0.18, 1.0), (0.2, -0.16, 1.42), (-0.6, -0.16, 0.8), (-1.05, -0.25, 0.25), (-1.2, -0.45, 0.03)], 0.022, cu)
    label(G, "MLG-R", (-0.45, -0.13, 1.05), 0.09, _bone(), rot=(90, -48, 0))
    return P


def cockpit_section(seed=52):
    """The SV-04's cockpit torn off at its rear bulkhead and lying on its belly: a hull-plate tub with a chamfered nose,
    a framed canopy of dark glass with one pane gone, the console's screens still faintly cyan inside."""
    rnd = random.Random(seed)
    P = _root("cockpit")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    glass = _glass()
    G = _grp(P, "cockpit", (0, 0, 0), (4, -6, -18))
    W = 2.2
    # the tub: side profile (y, z) extruded across x
    prof = [(-1.75, 0.45), (-1.35, 0.1), (-0.9, 0.0), (1.3, 0.0), (1.3, 1.0), (-0.95, 1.0)]
    bm = bmesh.new()
    a = [bm.verts.new((-W / 2, y, z)) for y, z in prof]
    b = [bm.verts.new((W / 2, y, z)) for y, z in prof]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(len(prof)):
        j = (i + 1) % len(prof)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    tub = K.from_bm("tub", bm, hm)
    K.bevel(tub, 0.04, segs=2)
    K.place(tub, parent=G)
    K.box("tub_top", (W - 0.25, 2.1, 0.06), dark, loc=(0, 0.2, 1.0), bev=0, parent=G)
    for s in (-1, 1):
        K.box(f"sill{s}", (0.12, 2.3, 0.12), gm, loc=(s * (W / 2 - 0.02), 0.15, 1.0), bev=0.02, parent=G)
        K.box(f"stripe{s}", (0.02, 2.2, 0.1), _paint(), loc=(s * (W / 2 + 0.005), 0.2, 0.62), bev=0, parent=G)
        K.box(f"chine{s}", (0.1, 2.6, 0.1), gm, loc=(s * (W / 2 + 0.01), 0.0, 0.1), bev=0.02, parent=G)
    # the canopy: four frame hoops with curved glass between, the third pane gone
    C = _grp(G, "canopy", (0, 0, 1.0), (0, 0, 0))
    C.scale = (1.0, 1.0, 0.72)
    Rc = 1.02
    hoops = [-0.95, -0.35, 0.25, 0.85]
    for k, y in enumerate(hoops):
        K.torus(f"hoop{k}", Rc + 0.02, 0.05, gm, loc=(0, y, 0), rot=(90, 0, 0), arc=180, segs=24, parent=C)
    for k in range(3):
        if k == 2:
            continue
        shell_panel(f"pane{k}", Rc, 0.06, math.pi - 0.06, hoops[k] + 0.03, hoops[k + 1] - 0.03, glass, t=0.03, parent=C)
    beam(C, "spine", (0, hoops[0], Rc + 0.02), (0, hoops[-1], Rc + 0.02), 0.08, gm)
    for k in range(4):   # stubs of the lost pane's glass still in the frame
        a0 = math.radians(20 + k * 40)
        jg = jagged(C, f"shard{k}", (math.cos(a0) * Rc, hoops[2] + 0.12, math.sin(a0) * Rc), (0, 0, 0), 0.22, 0.25, glass, rnd, n=3, t=0.015)
        jg.rotation_euler = (math.radians(90), -a0 + math.pi / 2, 0)
    # the windscreen down the nose
    K.box("windscreen", (1.6, 0.03, 0.62), glass, loc=(0, -1.335 - 0.565 * 0.03, 0.75 + 0.825 * 0.03), rot=(-55.6, 0, 0), bev=0.01, parent=G)
    for s in (-1, 1):
        beam(G, f"screen_frame{s}", (s * 0.84, -1.68, 0.53), (s * 0.84, -0.98, 1.01), 0.08, gm)
    # inside: the console with dim cyan screens and two seat backs
    K.box("console", (1.6, 0.4, 0.35), gm, loc=(0, -0.65, 1.08), rot=(-25, 0, 0), bev=0.02, parent=G)
    for x in (-0.4, 0.4):
        K.box(f"screen{x}", (0.42, 0.02, 0.18), glow(CYAN, 0.9), loc=(x, -0.47, 1.17), rot=(-25, 0, 0), bev=0.004, parent=G)
        K.box(f"seat{x}", (0.5, 0.14, 0.75), K.mat("cushion", CUSHION, rough=0.9, ao=0.4), loc=(x * 1.2, 0.35, 1.2), rot=(-12, 0, 0), bev=0.04, parent=G)
    # the torn rear: ragged plates, rib ends and cables
    for k in range(6):
        x = -W / 2 + 0.2 + k * 0.36
        jagged(G, f"tear{k}", (x, 1.32, 0.15 + rnd.uniform(0, 0.4)), (rnd.uniform(-90, -60), rnd.uniform(-15, 15), rnd.uniform(-10, 10)), 0.34, 0.35,
               hm if k % 2 else gm, rnd, n=3, t=0.03)
    for k in range(3):
        K.box(f"rib_end{k}", (0.08, 0.3, 0.08), gm, loc=(-0.7 + k * 0.7, 1.42, 0.85), rot=(rnd.uniform(-20, 20), 0, rnd.uniform(-15, 15)), bev=0.01, parent=G)
        cable(G, f"wire{k}", [(-0.3 + k * 0.3, 1.25, 0.7), (-0.35 + k * 0.35, 1.55, 0.55), (-0.5 + k * 0.5, 1.85, 0.03)], 0.03, cu)
    label(G, "SV-04", (W / 2 + 0.012, -0.35, 0.35), 0.24, _bone(), rot=(90, 0, 90))
    FR.vent(G, (W / 2 + 0.03, 0.75, 0.35), 0.5, 0.3, gm, dark, axis="x")
    _settle(G)
    return P


def seat_ejected(seed=53):
    """An ejection seat lying on its back where it came down, its torn chute dragged out across the grass on its
    shroud lines: framed seat, worn padding, the headbox's pull handle and the burnt catapult tube behind."""
    rnd = random.Random(seed)
    P = _root("ejectseat")
    hm, gm, cu = _hm(), _gm(), _copper()
    pad = K.mat("cushion", CUSHION, rough=0.9, ao=0.4)
    # built upright (back towards +y), then laid on its back, the headbox towards +y
    S = _grp(P, "seat", (-0.6, -0.4, 0), (-78, 6, 22))
    for s in (-1, 1):
        K.box(f"rail{s}", (0.06, 0.08, 1.35), gm, loc=(s * 0.3, 0.28, 0.85), bev=0.01, parent=S)
        K.box(f"side{s}", (0.04, 0.55, 0.22), hm, loc=(s * 0.3, 0.0, 0.5), bev=0.01, parent=S)
        K.box(f"leg_guard{s}", (0.04, 0.08, 0.35), gm, loc=(s * 0.3, -0.3, 0.3), bev=0.008, parent=S)
    K.box("pan", (0.56, 0.56, 0.1), gm, loc=(0, 0.0, 0.42), bev=0.015, parent=S)
    K.box("cushion", (0.5, 0.5, 0.1), pad, loc=(0, -0.01, 0.5), bev=0.04, parent=S)
    K.box("back", (0.5, 0.1, 0.72), pad, loc=(0, 0.22, 0.9), bev=0.04, parent=S)
    K.box("headbox", (0.5, 0.28, 0.32), hm, loc=(0, 0.28, 1.44), bev=0.02, parent=S)
    K.box("headpad", (0.32, 0.06, 0.2), pad, loc=(0, 0.13, 1.4), bev=0.02, parent=S)
    K.torus("pull", 0.12, 0.018, M(MUSTARD, wear=0.4), loc=(0, 0.2, 1.6), rot=(0, 90, 0), arc=180, segs=12, parent=S)
    K.box("chute_door", (0.46, 0.02, 0.26), _paint(), loc=(0, 0.425, 1.44), bev=0.005, parent=S)
    K.cyl("catapult", 0.07, 1.1, gm, loc=(0, 0.4, 0.75), segs=10, bev=0.01, parent=S)
    K.cyl("nozzle", 0.06, 0.12, _char(), loc=(0, 0.4, 0.16), segs=10, r2=0.09, bev=0, parent=S)
    K.box("handle", (0.24, 0.05, 0.05), M(MUSTARD, wear=0.4), loc=(0, -0.29, 0.5), bev=0.01, parent=S)
    label(S, "SV-04", (0, 0.43, 1.0), 0.07, _bone(), rot=(90, 0, 180))
    _settle(S)
    # the chute, dragged out behind the headbox, one side torn away
    head = S.matrix_world @ Vector((0, 0.32, 1.6))
    cmid = (0.9, 1.4)
    ch = drape(P, "chute", cmid, 2.8, 2.2, 14, 11, lambda x, y: 0.0, [M(CHUTE, wear=0.1, rough=0.9, scratches=0.0), M(CHUTE, wear=0.1, rough=0.9, scratches=0.0), _paint()], rnd, amp=0.14, gore=2,
               hole=lambda u, v: (u > 0.72 and v > 0.62) or (u > 0.86 and v > 0.4), loose=0.08)
    for k in range(5):
        e = Vector((cmid[0] - 1.3 + k * 0.1, cmid[1] - 1.0 + k * 0.45, 0.04))
        rod(P, f"line{k}", head, e, 0.006, M(BONE, wear=0.0), segs=4)
    return P


def med_kit_crate(seed=54):
    """A medical crate from the SV-04's sickbay: white enamel panels in a gunmetal frame, a cyan cross on the lid and
    the front, latches, end handles and a stencilled MED."""
    P = _root("medcrate")
    gm, dark = _gm(), _dark()
    white = M(MEDIC, wear=0.25, grime=0.35, scratches=0.3)
    w, d, h = 0.9, 0.55, 0.42
    for s in (-1, 1):
        K.box(f"skid{s}", (w, 0.08, 0.06), gm, loc=(0, s * (d / 2 - 0.07), 0.03), bev=0.008, parent=P)
    FR.framed_box(P, "case", (0, 0, 0.06 + h / 2), (w, d, h), white, gm, fw=0.05, bev=0.01)
    zt = 0.06 + h
    FR.framed_box(P, "lid", (0, 0, zt + 0.06), (w + 0.03, d + 0.03, 0.12), white, gm, fw=0.045, bev=0.01)
    K.box("seal", (w - 0.02, d - 0.02, 0.02), dark, loc=(0, 0, zt + 0.002), bev=0, parent=P)
    cy = _paint()
    K.box("cross_a", (0.28, 0.08, 0.01), cy, loc=(0, 0, zt + 0.125), bev=0, parent=P)
    K.box("cross_b", (0.08, 0.28, 0.01), cy, loc=(0, 0, zt + 0.125), bev=0, parent=P)
    K.box("front_a", (0.18, 0.01, 0.055), cy, loc=(-0.2, -d / 2 - 0.003, 0.3), bev=0, parent=P)
    K.box("front_b", (0.055, 0.01, 0.18), cy, loc=(-0.2, -d / 2 - 0.003, 0.3), bev=0, parent=P)
    label(P, "MED", (0.14, -d / 2 - 0.003, 0.29), 0.1, M(GRAPHITE, wear=0.2))
    K.box("band", (w - 0.12, 0.01, 0.03), cy, loc=(0, -d / 2 - 0.003, 0.14), bev=0, parent=P)
    for x in (-0.28, 0.28):
        K.box(f"latch{x}", (0.07, 0.035, 0.1), gm, loc=(x, -d / 2 - 0.02, zt - 0.02), bev=0.006, parent=P)
    for s in (-1, 1):
        K.box(f"handle{s}", (0.035, 0.24, 0.035), gm, loc=(s * (w / 2 + 0.06), 0, 0.33), bev=0.006, parent=P)
        for y in (-0.1, 0.1):
            K.box(f"handle_post{s}{y}", (0.06, 0.03, 0.03), gm, loc=(s * (w / 2 + 0.03), y, 0.33), bev=0, parent=P)
    return P


def supply_drop_parachute(seed=55):
    """A supply pod come down under its chute: a banded hull-grey canister on a crushed landing skirt, its chute
    slumped over the top and spilling onto the grass beside it, shroud lines to the harness ring."""
    rnd = random.Random(seed)
    P = _root("supplydrop")
    hm, gm, dark = _hm(), _gm(), _dark()
    r, z0, Hp = 0.55, 0.18, 1.35
    K.cyl("skirt", r + 0.06, z0, _char(), loc=(0, 0, z0 / 2), segs=16, r2=r, bev=0.02, parent=P)
    K.cyl("body", r, Hp, hm, loc=(0, 0, z0 + Hp / 2), segs=16, bev=0.02, parent=P)
    for z in (z0 + 0.1, z0 + Hp / 2, z0 + Hp - 0.1):
        K.cyl(f"band{z:.2f}", r + 0.025, 0.1, gm, loc=(0, 0, z), segs=16, bev=0.012, parent=P)
    zt = z0 + Hp
    K.cyl("cap", r, 0.18, gm, loc=(0, 0, zt + 0.09), segs=16, r2=r * 0.6, bev=0.02, parent=P)
    K.torus("harness", 0.12, 0.025, _zinc(), loc=(0, 0, zt + 0.22), rot=(90, 0, 0), segs=12, parent=P)
    for k in range(4):   # latches down the seams
        a = math.radians(45 + 90 * k)
        K.box(f"latch{k}", (0.05, 0.08, 0.14), gm, loc=(math.cos(a) * (r + 0.02), math.sin(a) * (r + 0.02), z0 + 0.95), rot=(0, 0, math.degrees(a)), bev=0.006, parent=P)
    K.box("status", (0.2, 0.02, 0.05), glow(CYAN, 0.9), loc=(-0.2, -r + 0.005, z0 + 1.1), rot=(0, 0, -20), bev=0.004, parent=P)
    label(P, "SUPPLY", (0.0, -r - 0.004, z0 + 0.45), 0.11, _bone())
    FR.hazard(P, (0.0, -r - 0.004, z0 + 0.25), 0.4, 0.06, glow(CYAN, 0.6), dark, n=6)

    def support(x, y):
        d = math.hypot(x, y)
        top = zt + 0.25 - 0.3 * (d / r) ** 2
        if d < r + 0.05:
            return top
        return max(0.0, zt + 0.25 - 0.3 - (d - r - 0.05) * 3.2)

    # the chute slumped over the back of the pod's top and spread out behind it
    nylon = M(CHUTE, wear=0.1, rough=0.9, scratches=0.0)
    drape(P, "chute", (1.25, 1.15), 3.4, 2.5, 20, 15, support, [nylon, nylon, _paint()], rnd, amp=0.12, gore=2,
          hole=lambda u, v: math.hypot(u - 0.92, v - 0.85) < 0.1, loose=0.06)
    return P


# ================================================================== field gear
def navigation_beacon(seed=56):
    """A Salvager navigation beacon, 4.4 m: a framed battery case on a ground plate, a square mast with a small solar
    panel, a caged lamp burning dim cyan and a whip antenna, held up by three guy wires."""
    P = _root("navbeacon")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    K.box("plate", (1.0, 1.0, 0.06), gm, loc=(0, 0, 0.03), bev=0.01, parent=P)
    for x in (-0.42, 0.42):
        for y in (-0.42, 0.42):
            FR.screw(P, (x, y, 0.065), (0, 0, 0), r=0.03, mat=_zinc())
    FR.framed_box(P, "case", (0, 0, 0.4), (0.7, 0.6, 0.68), hm, gm, fw=0.06)
    FR.screen(P, (0.12, -0.32, 0.5), 0.2, 0.1, gm, glow(CYAN, 1.0))
    label(P, "NAV", (-0.15, -0.302, 0.5), 0.1, _bone())
    FR.hazard(P, (0, -0.31, 0.2), 0.5, 0.07, glow(CYAN, 0.6), dark, n=6)
    Hm = 4.0
    K.box("mast", (0.14, 0.14, Hm - 0.74), gm, loc=(0, 0, 0.74 + (Hm - 0.74) / 2), bev=0.015, parent=P)
    for z in (1.6, 2.8, Hm - 0.05):
        K.box(f"collar{z}", (0.22, 0.22, 0.1), hm, loc=(0, 0, z), bev=0.015, parent=P)
    rod(P, "conduit", (0.09, 0.0, 0.74), (0.09, 0.0, Hm - 0.1), 0.02, cu)
    # the solar panel on an arm
    S = _grp(P, "solar", (0.0, -0.32, 2.35), (-40, 0, 0))
    K.box("solar_frame", (0.6, 0.45, 0.04), _zinc(), bev=0.006, parent=S)
    K.box("solar_cells", (0.54, 0.39, 0.02), K.mat("cell", CELL, rough=0.15, metal=0.4, grime=0.25), loc=(0, 0, 0.015), bev=0, parent=S)
    beam(P, "solar_arm", (0, -0.07, 2.3), (0, -0.25, 2.3), 0.05, gm)
    # the head: lamp in a cage, antenna
    zt = Hm + 0.05
    K.box("head", (0.3, 0.3, 0.12), gm, loc=(0, 0, zt + 0.06), bev=0.02, parent=P)
    K.cyl("lens", 0.1, 0.24, glow(CYAN, 1.6), loc=(0, 0, zt + 0.24), segs=14, bev=0.01, parent=P)
    K.cyl("lamp_cap", 0.13, 0.05, gm, loc=(0, 0, zt + 0.385), segs=14, bev=0.01, parent=P)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        K.box(f"cage{k}", (0.018, 0.018, 0.26), gm, loc=(math.cos(a) * 0.125, math.sin(a) * 0.125, zt + 0.24), bev=0, parent=P)
    K.point("lamp_light", (0, 0, zt + 0.24), CYAN, 20, 0.05).parent = P
    rod(P, "antenna", (0.1, 0.1, zt + 0.1), (0.12, 0.12, zt + 1.1), 0.008, _zinc(), segs=6)
    # three guy wires to staked anchors
    for k in range(3):
        a = math.radians(90 + 120 * k)
        anchor = (math.cos(a) * 2.0, math.sin(a) * 2.0, 0.05)
        rod(P, f"guy{k}", (math.cos(a) * 0.08, math.sin(a) * 0.08, 3.2), anchor, 0.008, _zinc(), segs=4)
        K.box(f"stake{k}", (0.06, 0.06, 0.2), gm, loc=(anchor[0], anchor[1], 0.05), rot=(0, 0, math.degrees(a)), bev=0.008, parent=P)
        K.torus(f"eye{k}", 0.04, 0.012, gm, loc=(anchor[0], anchor[1], 0.16), rot=(90, 0, math.degrees(a)), segs=10, parent=P)
    return P


def signal_flare_post(seed=57):
    """A marker post the survivors stuck in the ground, 1.4 m: a gunmetal spike with a cyan reflector band and a
    clamp holding a burnt-out flare, soot down the post, a second spent flare in the ash at its foot."""
    P = _root("flarepost")
    hm, gm = _hm(), _gm()
    char, red = _char(), M(SIGNAL, wear=0.4, grime=0.4, rough=0.7)
    soot = K.mat("soot", "#1d1b19", rough=1.0)
    K.cyl("spike", 0.04, 0.2, gm, loc=(0, 0, 0.05), segs=8, r2=0.01, bev=0, parent=P).rotation_euler = (math.pi, 0, 0)
    K.box("post", (0.07, 0.07, 1.3), gm, loc=(0, 0, 0.65), rot=(0, 3, 0), bev=0.01, parent=P)
    K.box("foot", (0.22, 0.22, 0.03), hm, loc=(0, 0, 0.12), bev=0.006, parent=P)
    K.box("reflector", (0.075, 0.075, 0.1), _paint(), loc=(0.006, 0, 1.0), rot=(0, 3, 0), bev=0.004, parent=P)
    # the clamp and its burnt flare
    K.box("clamp", (0.12, 0.1, 0.08), gm, loc=(0.05, -0.06, 1.24), rot=(0, 3, 0), bev=0.01, parent=P)
    K.cyl("flare", 0.035, 0.3, red, loc=(0.06, -0.12, 1.33), rot=(-6, 0, 0), segs=10, bev=0.005, parent=P)
    K.cyl("flare_burnt", 0.033, 0.08, char, loc=(0.06, -0.128, 1.52), rot=(-6, 0, 0), segs=10, r2=0.025, bev=0.004, parent=P)
    K.box("soot", (0.075, 0.08, 0.45), soot, loc=(0.03, -0.008, 1.05), rot=(0, 3, 0), bev=0, parent=P).scale = (1.02, 1.0, 1.0)
    K.box("tag", (0.1, 0.005, 0.07), M(MUSTARD, wear=0.4), loc=(0.0, -0.04, 0.8), rot=(0, 0, 0), bev=0, parent=P)
    # ash and the old flare at its foot
    K.disc("ash", 0.35, K.mat("ash", "#3a3530", rough=1.0), (0.15, -0.25), z=0.006, segs=14, wobble=0.25, seed=seed).parent = P
    K.cyl("spent", 0.035, 0.3, red, loc=(0.25, -0.3, 0.035), rot=(0, 90, 30), segs=10, bev=0.005, parent=P)
    K.cyl("spent_end", 0.033, 0.07, char, loc=(0.25 + 0.17 * math.cos(math.radians(30)), -0.3 + 0.17 * math.sin(math.radians(30)), 0.035), rot=(0, 90, 30), segs=10,
          bev=0, parent=P)
    K.cyl("cap", 0.04, 0.05, gm, loc=(-0.12, -0.15, 0.02), rot=(0, 90, 70), segs=10, bev=0.004, parent=P)
    return P


def radiator_panel(seed=58):
    """A radiator panel off the SV-04's flank, 3 x 1.7 m, leaning on a snapped strut: a gunmetal frame of thin hull
    fins between copper manifolds, one fin bent out and the mount brackets torn at one end."""
    rnd = random.Random(seed)
    P = _root("radiator")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    G = _grp(P, "panel", (0, 0.2, 0), (-28, 0, 8))   # stands up from y = 0, leaning back
    Lp, Hp = 3.0, 1.7
    for s in (-1, 1):
        K.box(f"stile{s}", (0.1, 0.1, Hp), gm, loc=(s * Lp / 2, 0, Hp / 2), bev=0.015, parent=G)
    for z, tag in ((0.08, "lo"), (Hp - 0.08, "hi")):
        K.cyl(f"manifold_{tag}", 0.07, Lp - 0.05, cu, loc=(0, 0, z), rot=(0, 90, 0), segs=12, bev=0.01, parent=G)
        K.box(f"rail_{tag}", (Lp, 0.06, 0.04), gm, loc=(0, 0.08, z), bev=0.006, parent=G)
    n = 18
    for k in range(n):
        x = -Lp / 2 + 0.15 + k * (Lp - 0.3) / (n - 1)
        bent = k == 12
        fin = K.box(f"fin{k}", (0.025, 0.16, Hp - 0.2), hm, loc=(x, -0.02, Hp / 2), bev=0, parent=G)
        if bent:
            fin.rotation_euler = (0, 0, math.radians(35))
            fin.location.y -= 0.04
        K.cyl(f"tube{k}", 0.018, Hp - 0.16, gm, loc=(x, 0.06, Hp / 2), segs=6, bev=0, parent=G)
    K.box("backing", (Lp - 0.06, 0.03, Hp - 0.1), M(HULL, metal=0.3, wear=0.2), loc=(0, 0.1, Hp / 2), bev=0.008, parent=G)
    for x in (-0.5, 0.5):
        K.box(f"stiffener{x}", (0.08, 0.06, Hp - 0.12), gm, loc=(x * Lp / 2, 0.14, Hp / 2), bev=0.01, parent=G)
    K.box("light", (0.03, 0.02, 0.6), glow(CYAN, 0.6), loc=(-Lp / 2 - 0.055, -0.04, Hp / 2), bev=0.003, parent=G)
    label(G, "RAD-2", (Lp / 2 - 0.004, -0.12, Hp - 0.25), 0.09, _bone(), rot=(90, 0, 0))
    # torn mount brackets on the right stile
    for z in (0.4, 1.3):
        K.box(f"bracket{z}", (0.2, 0.18, 0.12), gm, loc=(Lp / 2 + 0.12, 0.05, z), bev=0.01, parent=G)
        jagged(G, f"bracket_tear{z}", (Lp / 2 + 0.24, 0.05, z), (rnd.uniform(-15, 15), 90, 0), 0.12, 0.12, gm, rnd, n=3, t=0.16)
    # the snapped strut it leans on
    _settle(G, -0.03)
    beam(P, "prop_strut", (-0.9, 1.2, 0.0), (-0.9, 0.75, 0.95), 0.08, gm)
    jagged(P, "strut_tear", (-0.9, 1.2, 0.0), (0, 0, 0), 0.1, 0.1, gm, rnd, n=2, t=0.08)
    beam(P, "prop_strut2", (1.1, 1.25, 0.0), (1.1, 0.9, 0.7), 0.08, gm)
    return P


def coolant_tank(seed=59):
    """A spherical coolant tank, 1.7 m across, on its framed cradle: an equator band and a cyan painted band, a valve
    on top and copper lines down to a pump box at its foot."""
    P = _root("coolant")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    Rt, zc = 0.85, 1.25
    K.sphere("tank", Rt, hm, loc=(0, 0, zc), parent=P)
    K.torus("equator", Rt + 0.01, 0.04, gm, loc=(0, 0, zc), segs=28, parent=P)
    lat = math.radians(32)
    K.torus("band", Rt * math.cos(lat) + 0.003, 0.05, _paint(), loc=(0, 0, zc + Rt * math.sin(lat)), segs=24, parent=P).scale = (1, 1, 0.5)
    K.cyl("top_flange", 0.18, 0.08, gm, loc=(0, 0, zc + Rt + 0.0), segs=14, bev=0.01, parent=P)
    K.cyl("valve", 0.08, 0.2, gm, loc=(0, 0, zc + Rt + 0.12), segs=10, bev=0.01, parent=P)
    K.torus("valve_wheel", 0.13, 0.02, M(MUSTARD, wear=0.4), loc=(0, 0, zc + Rt + 0.24), segs=16, parent=P)
    # the cradle: a ring under the tank on four legs, rails between the feet
    zr = zc - math.sqrt(Rt ** 2 - 0.62 ** 2)
    K.torus("cradle_ring", 0.64, 0.05, gm, loc=(0, 0, zr), segs=22, parent=P)
    feet = []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        top = (math.cos(a) * 0.64, math.sin(a) * 0.64, zr)
        foot = (math.cos(a) * 0.95, math.sin(a) * 0.95, 0.03)
        feet.append(foot)
        beam(P, f"leg{k}", foot, top, 0.09, gm)
        K.box(f"foot{k}", (0.22, 0.22, 0.05), hm, loc=(foot[0], foot[1], 0.025), rot=(0, 0, math.degrees(a)), bev=0.008, parent=P)
    for k in range(4):
        beam(P, f"rail{k}", feet[k], feet[(k + 1) % 4], 0.07, gm)
    # label plate on the front rail, a pump box and copper lines
    FR.framed_box(P, "pump", (0.65, -0.1, 0.25), (0.4, 0.35, 0.4), hm, gm, fw=0.04)
    K.box("pump_led", (0.04, 0.02, 0.04), glow(CYAN, 1.0), loc=(0.75, -0.28, 0.36), bev=0.004, parent=P)
    cable(P, "line_a", [(0.0, 0.0, zc + Rt + 0.05), (0.25, -0.05, zc + Rt + 0.05), (Rt * 0.9, -0.1, zc + 0.35), (Rt + 0.05, -0.1, zc - 0.3), (0.7, -0.1, 0.45)],
          0.03, cu)
    cable(P, "line_b", [(0.45, -0.05, 0.25), (0.15, -0.3, 0.2), (0.0, -0.45, zr - 0.25), (0.0, -Rt * 0.82, zc - 0.45)], 0.03, cu)
    K.box("plate", (0.5, 0.02, 0.16), M(BONE, wear=0.4, grime=0.3), loc=(0, -0.68, 0.32), rot=(0, 0, 0), bev=0.004, parent=P)
    beam(P, "plate_arm", (-0.2, -0.672, 0.03), (-0.2, -0.672, 0.32), 0.04, gm)
    beam(P, "plate_arm2", (0.2, -0.672, 0.03), (0.2, -0.672, 0.32), 0.04, gm)
    label(P, "COOLANT", (0, -0.692, 0.32), 0.07, M(GRAPHITE, wear=0.2))
    FR.hazard(P, (0, -0.68, 0.18), 0.5, 0.05, M(MUSTARD), dark, n=6)
    return P


def cable_bundle_torn(seed=60):
    """A thick power bundle from the ship's spine, coiled where it fell: three turns of black cable with cyan marker
    sleeves, a connector on one end and the other end ripped apart into splayed copper."""
    rnd = random.Random(seed)
    P = _root("cablecoil")
    blk = K.mat("cable", CABLE, rough=0.6)
    cu, gm = _copper(), _gm()
    rc = 0.08
    turns = [(0.72, 0.0, 0.0), (0.68, 0.05, 0.03), (0.7, -0.04, 0.06)]
    for k, (Rr, ox, oy) in enumerate(turns):
        tt = K.torus(f"turn{k}", Rr, rc, blk, loc=(ox, oy, rc + k * 0.13), segs=30, parent=P)
        tt.rotation_euler = (math.radians(rnd.uniform(-3, 3)), math.radians(rnd.uniform(-3, 3)), 0)
        for j in range(3):
            a = 2 * math.pi * (j + 0.3 * k) / 3
            K.cyl(f"sleeve{k}{j}", rc + 0.012, 0.1, _paint(), loc=(ox + math.cos(a) * Rr, oy + math.sin(a) * Rr, rc + k * 0.13), rot=(90, 0, math.degrees(a)),
                  segs=10, bev=0, parent=P)
    # the end running off the coil across the grass to the tear
    path = [(0.7, -0.05, rc + 0.26), (0.95, -0.4, 0.2), (1.25, -0.75, rc), (1.8, -0.9, rc), (2.3, -0.7, rc)]
    sweep(P, "tail", path, _circle(rc, 8), blk)
    K.cyl("ferrule", rc + 0.015, 0.12, gm, loc=(2.32, -0.69, rc), rot=(0, 90, 22), segs=10, bev=0.01, parent=P)
    for k in range(9):
        a = 2 * math.pi * k / 9
        o = Vector((2.36, -0.68, rc)) + Vector((0, math.cos(a) * 0.04, math.sin(a) * 0.04))
        e = o + Vector((rnd.uniform(0.15, 0.3), math.cos(a) * rnd.uniform(0.06, 0.15), math.sin(a) * rnd.uniform(0.03, 0.08)))
        e.z = max(e.z, 0.012)
        rod(P, f"strand{k}", o, e, 0.012, cu, segs=4)
    # the other end, with its connector
    path2 = [(-0.66, 0.2, rc), (-1.0, 0.35, rc), (-1.35, 0.25, rc)]
    sweep(P, "lead", path2, _circle(rc, 8), blk)
    FR.framed_box(P, "plug", (-1.5, 0.22, 0.13), (0.3, 0.26, 0.26), _hm(), gm, fw=0.04)
    K.box("plug_face", (0.02, 0.18, 0.18), K.mat("hazk", DARK, rough=0.6), loc=(-1.655, 0.22, 0.13), bev=0, parent=P)
    for y in (-0.05, 0.05):
        for z in (-0.05, 0.05):
            K.cyl("pin", 0.015, 0.06, cu, loc=(-1.67, 0.22 + y, 0.13 + z), rot=(0, 90, 0), segs=6, bev=0, parent=P)
    return P


def airlock_door(seed=61):
    """The SV-04's round airlock door, 2.2 m across, blown off its hinges and lying flat on a clod: a thick hull-grey
    disc in a gunmetal ring, locking dogs round its edge, a hand wheel, a porthole and the torn hinge arms."""
    rnd = random.Random(seed)
    P = _root("airlock")
    hm, gm, dark = _hm(), _gm(), _dark()
    G = _grp(P, "door", (0, 0, 0), (7, -3, 25))
    R = 1.1
    K.cyl("ring", R, 0.24, gm, loc=(0, 0, 0.12), segs=32, bev=0.03, parent=G)
    K.cyl("leaf", R - 0.14, 0.06, hm, loc=(0, 0, 0.26), segs=32, bev=0.015, parent=G)
    K.cyl("boss", 0.4, 0.06, gm, loc=(0, 0, 0.3), segs=24, bev=0.012, parent=G)
    for k in range(12):   # locking dogs
        a = 2 * math.pi * k / 12
        K.box(f"dog{k}", (0.18, 0.08, 0.06), gm, loc=(math.cos(a) * (R - 0.08), math.sin(a) * (R - 0.08), 0.27), rot=(0, 0, math.degrees(a)), bev=0.01, parent=G)
    for k in range(16):   # a cyan and dark hazard ring round the leaf
        a = 2 * math.pi * (k + 0.5) / 16
        K.box(f"haz{k}", (0.07, 0.3, 0.008), _paint() if k % 2 == 0 else dark, loc=(math.cos(a) * 0.82, math.sin(a) * 0.82, 0.292), rot=(0, 0, math.degrees(a)),
              bev=0, parent=G)
    # the hand wheel and spokes
    K.torus("wheel", 0.3, 0.03, M(MUSTARD, wear=0.4), loc=(0, 0, 0.44), segs=28, parent=G)
    K.cyl("hub", 0.07, 0.14, gm, loc=(0, 0, 0.38), segs=10, bev=0.01, parent=G)
    for k in range(3):
        K.box(f"spoke{k}", (0.6, 0.04, 0.03), gm, loc=(0, 0, 0.44), rot=(0, 0, k * 60), bev=0, parent=G)
    # porthole and label
    K.cyl("port_rim", 0.18, 0.05, gm, loc=(0.0, 0.58, 0.3), segs=20, bev=0.01, parent=G)
    K.cyl("port", 0.13, 0.055, _glass(), loc=(0.0, 0.58, 0.305), segs=20, bev=0, parent=G)
    label(G, "AIRLOCK 2", (0, -0.55, 0.297), 0.11, _bone(), rot=(0, 0, 0))
    # hinge arms sheared off at +x
    for y in (-0.45, 0.45):
        K.box(f"hinge{y}", (0.5, 0.18, 0.16), gm, loc=(R + 0.15, y, 0.14), bev=0.015, parent=G)
        jagged(G, f"hinge_tear{y}", (R + 0.4, y, 0.14), (rnd.uniform(-15, 15), 90, 0), 0.18, 0.12, gm, rnd, n=3, t=0.16)
        K.cyl(f"pin{y}", 0.05, 0.22, _zinc(), loc=(R + 0.25, y, 0.24), segs=8, bev=0, parent=G)
    _settle(G, -0.02)
    K.box("clod", (0.5, 0.4, 0.25), K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), loc=(-0.9, 0.2, 0.06), rot=(10, -8, 30), bev=0.1, parent=P)
    return P


def hull_rib_arch(seed=62):
    """One of the SV-04's hull ribs, 5 m across and 4 m high, standing out of the ground where the hull was torn
    away: a curved I-section with lightening holes, stringer stubs and a few plates still riveted on, a cable hanging."""
    rnd = random.Random(seed)
    P = _root("hullrib")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    G = _grp(P, "rib", (0, 0, 0), (0, 5, 3))
    a, b = 2.5, 4.0
    ts = [-0.1 + (math.pi + 0.2) * i / 24 for i in range(25)]
    path = []
    for t in ts:
        bend = 0.18 * max(0.0, math.sin((t - 2.0) * 2.2)) if 2.0 < t < 3.4 else 0.0
        path.append((a * math.cos(t), bend, b * math.sin(t)))
    fw, ft, dw, wt = 0.3, 0.05, 0.4, 0.04   # flange width (along y), flange thickness, depth, web thickness
    ih = dw / 2
    prof = [(-ih, -fw / 2), (-ih, fw / 2), (-ih + ft, fw / 2), (-ih + ft, wt / 2), (ih - ft, wt / 2), (ih - ft, fw / 2), (ih, fw / 2), (ih, -fw / 2),
            (ih - ft, -fw / 2), (ih - ft, -wt / 2), (-ih + ft, -wt / 2), (-ih + ft, -fw / 2)]
    sweep(G, "rib", path, prof, gm, up=(0, 1, 0), smooth=False)
    # lightening holes in the web, stringer stubs on the outside, a few plates left on
    for k, t in enumerate([0.35 + 0.3 * i for i in range(9)]):
        p = Vector((a * math.cos(t), 0, b * math.sin(t)))
        tan = Vector((-a * math.sin(t), 0, b * math.cos(t))).normalized()
        out = Vector((tan.z, 0, -tan.x))
        if out.dot(p) < 0:
            out = -out
        for s in (-1, 1):
            K.cyl(f"hole{k}{s}", 0.09, 0.01, dark, loc=tuple(p + Vector((0, s * (wt / 2 + 0.003), 0))), rot=(90, 0, 0), segs=12, bev=0, parent=G)
        if k % 2 == 0:
            q = p + out * (ih + 0.05)
            beam(G, f"stringer{k}", q + Vector((0, -0.35, 0)), q + Vector((0, 0.35 + rnd.uniform(0, 0.3), 0)), 0.08, gm, d=0.1)
        if k in (1, 6):
            pl = jagged(G, f"plate{k}", tuple(p + out * (ih + 0.04) - tan * 0.3), (0, 0, 0), 0.9, 0.7, hm, rnd, n=4, t=0.04)
            pl.rotation_euler = out.to_track_quat("Y", "Z").to_euler()
            for j in range(3):
                FR.screw(G, tuple(p + out * (ih + 0.07) + Vector((0, -0.3 + j * 0.3, 0))), (0, 0, 0), r=0.025, mat=_zinc())
    # a running light strip still on the inner flange and a cable hanging from the crown
    t = math.pi * 0.62
    p = Vector((a * math.cos(t), 0, b * math.sin(t)))
    K.box("runlight", (0.3, 0.06, 0.03), glow(CYAN, 0.5), loc=tuple(p * (1 - 0.06)), rot=(0, -math.degrees(math.atan2(p.z, p.x)) + 90, 0), bev=0.004, parent=G)
    top = Vector((0.3, 0.1, b - ih - 0.02))
    cable(G, "cable", [top, top + Vector((0.1, -0.05, -0.8)), top + Vector((0.05, -0.1, -1.7)), top + Vector((0.15, -0.15, -2.4))], 0.03, cu)
    label(G, "F14", (-a + 0.01, -fw / 2 - 0.004, 1.2), 0.18, _bone(), rot=(90, 0, 0))
    for s in (-1, 1):
        mound(P, f"dirt{s}", (s * a, 0), 0.35, 0.18, seed=seed + s, n=5)
    return P


def solar_wing_torn(seed=63):
    """A solar wing torn off the SV-04 at its boom: three hinged panels of dark cells in zinc frames, the first flat in
    the grass, the second kinked up on its hinge, the last ripped across and hanging to the ground."""
    rnd = random.Random(seed)
    P = _root("solarwing")
    gm, hm, cu = _gm(), _hm(), _copper()
    cell = K.mat("cell", CELL, rough=0.15, metal=0.4, grime=0.25)
    line = K.mat("cell_line", "#8fa3b0", rough=0.4, metal=0.5)
    crack = K.mat("crack", "#c9d6dc", rough=0.3)
    D = 1.3   # panel depth (y)

    def panel(Q, name, Lx, cracked=False, torn=False):
        if torn:
            pts = [(0, -D / 2), (Lx * 0.8, -D / 2), (Lx * 0.55, -0.1), (Lx * 0.75, 0.15), (Lx * 0.5, D / 2), (0, D / 2)]
            plate_xz(f"{name}_cells", [(x, y) for x, y in pts], 0.02, cell, rot=(90, 0, 0), parent=Q, bev=0).location = (0, 0, 0.025)
            for k in range(3):
                K.box(f"{name}_rag{k}", (0.04, 0.2, 0.03), _zinc(), loc=(Lx * (0.55 + 0.1 * k), -0.4 + k * 0.4, 0.02), rot=(0, 0, rnd.uniform(-40, 40)), bev=0, parent=Q)
            K.box(f"{name}_fr0", (0.04, D, 0.045), _zinc(), loc=(0.02, 0, 0.02), bev=0.006, parent=Q)
            K.box(f"{name}_fr1", (Lx * 0.8, 0.04, 0.045), _zinc(), loc=(Lx * 0.4, -D / 2, 0.02), bev=0.006, parent=Q)
            K.box(f"{name}_fr2", (Lx * 0.5, 0.04, 0.045), _zinc(), loc=(Lx * 0.25, D / 2, 0.02), bev=0.006, parent=Q)
            return
        K.box(f"{name}_frame", (Lx, D, 0.045), _zinc(), loc=(Lx / 2, 0, 0.0), bev=0.008, parent=Q)
        K.box(f"{name}_cells", (Lx - 0.06, D - 0.06, 0.012), cell, loc=(Lx / 2, 0, 0.022), bev=0, parent=Q)
        for i in range(1, 4):
            K.box(f"{name}_gv", (0.008, D - 0.07, 0.004), line, loc=(0.03 + i * (Lx - 0.06) / 4, 0, 0.029), bev=0, parent=Q)
        for i in range(1, 5):
            K.box(f"{name}_gh", (Lx - 0.07, 0.008, 0.004), line, loc=(Lx / 2, -D / 2 + 0.03 + i * (D - 0.06) / 5, 0.029), bev=0, parent=Q)
        if cracked:
            for j in range(5):
                a = rnd.uniform(0, 180) + j * 36
                L = rnd.uniform(0.3, 0.6)
                K.box(f"{name}_crack{j}", (L, 0.01, 0.004), crack, loc=(Lx * 0.6 + math.cos(math.radians(a)) * L / 2, 0.2 + math.sin(math.radians(a)) * L / 2, 0.031),
                      rot=(0, 0, a), bev=0, parent=Q)

    G = _grp(P, "wing", (0, 0, 0.02), (0, 0, 8))
    x0 = -2.0
    # the boom stub: a square truss arm ending in a torn yoke, cables out of it
    beam(G, "boom", (x0 - 0.9, 0.05, 0.12), (x0 - 0.05, 0, 0.06), 0.12, gm)
    K.box("yoke", (0.12, D * 0.6, 0.1), gm, loc=(x0 - 0.04, 0, 0.06), bev=0.012, parent=G)
    jagged(G, "boom_tear", (x0 - 0.95, 0.05, 0.12), (0, 90, 0), 0.14, 0.14, gm, rnd, n=3, t=0.14)
    for k in range(3):
        cable(G, f"wire{k}", [(x0 - 0.6, 0.0 + k * 0.05, 0.16), (x0 - 1.0, -0.15 + k * 0.15, 0.2), (x0 - 1.3, -0.35 + k * 0.3, 0.03)], 0.02, cu)
    Q1 = _grp(G, "p1", (x0, 0, 0.0), (0, 2, 0))
    panel(Q1, "p1", 1.45)
    L1 = 1.45
    Q2 = _grp(Q1, "p2", (L1 + 0.05, 0, 0.0), (0, -22, 0))
    panel(Q2, "p2", 1.45, cracked=True)
    for y in (-0.4, 0.4):
        K.cyl(f"hinge{y}", 0.03, 0.15, gm, loc=(L1 + 0.025, y, 0.0), rot=(90, 0, 0), segs=8, bev=0, parent=Q1)
        K.cyl(f"hinge2{y}", 0.03, 0.15, gm, loc=(1.475, y, 0.0), rot=(90, 0, 0), segs=8, bev=0, parent=Q2)
    Q3 = _grp(Q2, "p3", (1.5, 0, 0.0), (0, 52, 0))
    panel(Q3, "p3", 1.2, torn=True)
    label(Q1, "SV-04", (0.35, -D / 2 + 0.12, 0.033), 0.1, _bone(), rot=(0, 0, 0))
    return P


def thruster_cluster(seed=64):
    """A manoeuvring thruster block torn from the hull: three small bells in a framed gunmetal mount, scorched inside,
    copper feed lines behind and the block's mounting flange ripped."""
    rnd = random.Random(seed)
    P = _root("rcs")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    char = _char()
    G = _grp(P, "block", (0, 0, 0), (0, 14, -50))
    FR.framed_box(G, "mount", (0, 0, 0.5), (0.6, 1.2, 0.95), hm, gm, fw=0.08, screws=None)
    K.box("face", (0.06, 1.05, 0.8), gm, loc=(0.31, 0, 0.5), bev=0.01, parent=G)
    Re, Rt, Lb = 0.27, 0.1, 0.55
    n = 5
    for k, (y, z) in enumerate(((-0.28, 0.72), (0.28, 0.72), (0.0, 0.27))):
        A = _grp(G, f"bell{k}", (0.34, y, z), (0, 90, 0))
        outer = [(Rt + (Re - Rt) * (i / n) ** 1.5 + 0.02, Lb * i / n) for i in range(n + 1)]
        inner = [(Rt + (Re - Rt) * (i / n) ** 1.5, Lb * i / n) for i in range(n, -1, -1)]
        lathe(f"bell{k}", outer + inner, gm, segs=12, parent=A)
        liner = [(Rt + (Re - Rt) * (i / n) ** 1.5 - 0.002, Lb * i / n) for i in range(n + 1)] + [(Rt + (Re - Rt) * (i / n) ** 1.5 - 0.012, Lb * i / n) for i in range(n, -1, -1)]
        lathe(f"liner{k}", liner, char, segs=12, parent=A)
        K.torus(f"lip{k}", Re + 0.01, 0.022, hm, loc=(0, 0, Lb), segs=14, parent=A)
        K.cyl(f"throat{k}", 0.13, 0.08, gm, loc=(0, 0, 0.02), segs=12, bev=0.01, parent=A)
    # feed lines out of the back, the flange torn
    for k in range(3):
        cable(G, f"feed{k}", [(-0.3, -0.3 + k * 0.3, 0.75), (-0.5, -0.32 + k * 0.32, 0.6), (-0.6, -0.4 + k * 0.4, 0.15)], 0.03, cu)
    K.box("flange", (0.06, 1.35, 1.05), gm, loc=(-0.33, 0, 0.52), bev=0.012, parent=G)
    for k in range(3):
        jagged(G, f"tear{k}", (-0.35, -0.5 + k * 0.5, 1.05), (rnd.uniform(-20, 20), rnd.uniform(-30, 30), 90), 0.3, 0.18, hm if k % 2 else gm, rnd, n=3, t=0.03)
    label(G, "RCS-4", (0.0, -0.605, 0.5), 0.1, _bone(), rot=(90, 0, 0))
    K.box("status", (0.18, 0.02, 0.04), glow(CYAN, 0.6), loc=(0.05, -0.605, 0.3), bev=0.004, parent=G)
    _settle(G)
    K.disc("burn", 0.9, K.mat("burnt", SCORCH, rough=1.0, noise_scale=1.5), (0.75, -0.85), z=0.01, wobble=0.3, seed=seed).parent = P
    return P


def scanner_drone_down(seed=65):
    """A Salvager scanner drone come down on its side: a framed body, a cracked sensor eye still faintly lit, three
    ducted rotors on their arms and the fourth snapped off and lying a stride away."""
    rnd = random.Random(seed)
    P = _root("drone")
    hm, gm, dark, cu = _hm(), _gm(), _dark(), _copper()
    G = _grp(P, "drone", (0, 0, 0), (-12, 30, 30))   # nosed over onto one duct

    def duct(Q, name, loc, rot=(0, 0, 0)):
        D = _grp(Q, name, loc, rot)
        K.cyl(f"{name}_duct", 0.3, 0.12, hm, segs=20, bev=0.01, parent=D)
        K.cyl(f"{name}_inner", 0.26, 0.13, dark, segs=20, bev=0, parent=D)
        K.torus(f"{name}_lip", 0.3, 0.02, gm, loc=(0, 0, 0.06), segs=14, parent=D)
        K.cyl(f"{name}_motor", 0.06, 0.1, gm, segs=10, bev=0.01, parent=D)
        for k in range(3):
            K.box(f"{name}_blade{k}", (0.24, 0.05, 0.012), gm, loc=(math.cos(k * 2.09) * 0.13, math.sin(k * 2.09) * 0.13, 0.04), rot=(15, 0, math.degrees(k * 2.09)),
                  bev=0, parent=D)
        for k in range(2):
            K.box(f"{name}_strut{k}", (0.52, 0.025, 0.02), gm, loc=(0, 0, 0.0), rot=(0, 0, 45 + 90 * k), bev=0, parent=D)
        return D

    FR.framed_box(G, "body", (0, 0, 0.0), (0.7, 0.5, 0.26), hm, gm, fw=0.045, bev=0.01)
    K.box("lid", (0.5, 0.36, 0.03), gm, loc=(0, 0, 0.14), bev=0.008, parent=G)
    # the sensor eye at the front
    K.cyl("eye_housing", 0.12, 0.12, gm, loc=(0, -0.29, -0.02), rot=(90, 0, 0), segs=14, bev=0.01, parent=G)
    K.cyl("eye", 0.08, 0.03, glow(CYAN, 1.0), loc=(0, -0.345, -0.02), rot=(90, 0, 0), segs=14, bev=0, parent=G)
    for k in range(2):
        K.box(f"eye_crack{k}", (0.15, 0.005, 0.008), dark, loc=(0, -0.362, -0.02), rot=(0, 30 + 80 * k, 0), bev=0, parent=G)
    label(G, "SCN-1", (0.2, -0.252, 0.02), 0.05, _bone())
    K.box("status", (0.12, 0.015, 0.025), glow(CYAN, 0.8), loc=(-0.2, -0.252, 0.03), bev=0.003, parent=G)
    # arms to three ducts; the fourth arm snapped
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        a = Vector((sx * 0.3, sy * 0.2, 0.0))
        e = Vector((sx * 0.62, sy * 0.52, 0.02))
        if k == 3:
            m = a + (e - a) * 0.35
            beam(G, "arm_stub", a, m, 0.05, gm)
            jagged(G, "arm_tear", tuple(m), (90, 0, 45), 0.06, 0.08, gm, rnd, n=2, t=0.05)
            continue
        beam(G, f"arm{k}", a, e, 0.05, gm)
        duct(G, f"duct{k}", tuple(e))
    for s in (-1, 1):   # skids, one bent
        beam(G, f"skid_leg{s}", (s * 0.22, -0.15, -0.13), (s * 0.24, -0.15, -0.28), 0.025, gm)
        beam(G, f"skid_leg2{s}", (s * 0.22, 0.15, -0.13), (s * 0.24 + (0.08 if s > 0 else 0), 0.15, -0.28 + (0.06 if s > 0 else 0)), 0.025, gm)
        K.cyl(f"skid{s}", 0.018, 0.55, gm, loc=(s * 0.24, 0, -0.28), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    rod(G, "antenna", (-0.2, 0.15, 0.13), (-0.25, 0.25, 0.45), 0.006, _zinc(), segs=4)
    _settle(G, -0.05)
    # the broken-off duct and arm lying a stride away
    duct(P, "lost", (0.9, 0.95, 0.18), (70, 0, 40))
    beam(P, "lost_arm", (0.75, 0.75, 0.05), (0.55, 0.55, 0.04), 0.05, gm)
    K.strip("scrape", [(-1.4, -0.5), (-0.8, -0.3), (-0.3, -0.1)], 0.35, K.mat("soil", SOIL, rough=1.0, noise_scale=3.0), z=0.006).parent = P
    return P


PROPS = {
    "escape_pod": escape_pod,
    "lander_leg": lander_leg,
    "cargo_container_salvager": cargo_container_salvager,
    "crate_salvager_stack": crate_salvager_stack,
    "fuel_cell_spill": fuel_cell_spill,
    "power_core_cracked": power_core_cracked,
    "antenna_dish_salvager": antenna_dish_salvager,
    "sensor_buoy": sensor_buoy,
    "debris_trail_panels": debris_trail_panels,
    "engine_nacelle_small": engine_nacelle_small,
    "landing_strut": landing_strut,
    "cockpit_section": cockpit_section,
    "seat_ejected": seat_ejected,
    "med_kit_crate": med_kit_crate,
    "supply_drop_parachute": supply_drop_parachute,
    "navigation_beacon": navigation_beacon,
    "signal_flare_post": signal_flare_post,
    "radiator_panel": radiator_panel,
    "coolant_tank": coolant_tank,
    "cable_bundle_torn": cable_bundle_torn,
    "airlock_door": airlock_door,
    "hull_rib_arch": hull_rib_arch,
    "solar_wing_torn": solar_wing_torn,
    "thruster_cluster": thruster_cluster,
    "scanner_drone_down": scanner_drone_down,
}
