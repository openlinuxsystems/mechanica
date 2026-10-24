"""The Field Kitchen's burgers (consumables batch C4): the Garden Burger and the Colony Burger, whole and bitten.

Both are cooked food in the produce's look (food_lib materials, vertex-coloured, real models with bites), told apart at a
glance:

* **Garden Burger** - a wholemeal bun dusted with oat flakes, a green-flecked veggie patty, two tomato slices and a
  ruffled lettuce leaf hanging out all round;
* **Colony Burger** - a glossy brioche bun with sesame seeds, a thick grilled steak patty with grill marks, a melted
  mustard-yellow cheese slice drooping at its corners, a tomato slice and onion rings, and a wooden pick through the top
  with the colony's flag (steel blue, the Colony Works hexagon in bone) on it.

Z up, metres, the burger's origin at the middle of its base. ``build(kind, name, bites)`` makes one under an empty; each
layer is its own mesh, and a bite (``bites`` of 0 to 2) is three tooth-sized scallops cut through every layer at the +X
edge (the edge toward the mouth in hand, as the potato's).
"""

from __future__ import annotations

import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import food_lib as fl

R = 0.055                    # the bun's radius: an 11 cm burger
# Where each bite goes round the burger (radians from +X) and how deep its middle tooth goes (m in from the bun's edge).
BITES = ((-0.28, 0.021), (0.36, 0.019))
JUICE = {"garden_burger": "#d9b779", "colony_burger": "#c99a62"}


def mats():
    return {
        "bun": fl.material("burger_bun", "#FFFFFF", rough=0.55, vertex_color=True),
        "brioche": fl.material("burger_brioche", "#FFFFFF", rough=0.32, coat=0.35, vertex_color=True),
        "crumb": fl.material("burger_crumb", "#EAD6A6", rough=0.8),
        "veg_patty": fl.material("burger_veg_patty", "#FFFFFF", rough=0.75, vertex_color=True),
        "veg_cut": fl.material("burger_veg_cut", "#8A8048", rough=0.8),
        "steak": fl.material("burger_steak", "#FFFFFF", rough=0.6, vertex_color=True),
        "steak_cut": fl.material("burger_steak_cut", "#7A4A36", rough=0.7),
        "tomato": fl.material("burger_tomato", "#FFFFFF", rough=0.3, sss=0.1, vertex_color=True),
        "tomato_cut": fl.material("burger_tomato_cut", "#D8584A", rough=0.25),
        "lettuce": fl.material("burger_lettuce", "#FFFFFF", rough=0.45, sss=0.15, vertex_color=True),
        "cheese": fl.material("burger_cheese", "#E9BC3C", rough=0.35, sss=0.05),
        "onion": fl.material("burger_onion", "#EDE6D6", rough=0.4, sss=0.2),
        "oat": fl.material("burger_oat", "#E6D3A2", rough=0.8),
        "sesame": fl.material("burger_sesame", "#F2E7C8", rough=0.5),
        "pick": fl.material("burger_pick", "#D8BE8A", rough=0.7),
        "flag": fl.material("burger_flag", "#3E6A8C", rough=0.6),
        "flag_mark": fl.material("burger_flag_mark", "#DDD5C0", rough=0.6),
    }


# ---------------------------------------------------------------------------------------------------------------------
# Mesh helpers

def lathe_bm(profile, segs=56, shape=None):
    """A closed solid of revolution: ``profile`` is (r, z) from the bottom pole (r = 0) round to the top pole (r = 0).
    ``shape(theta, i, r, z) -> (r, z)`` may bend each ring (an uneven edge)."""
    bm = bmesh.new()
    rings = []
    for i, (r, z) in enumerate(profile[1:-1], start=1):
        ring = []
        for k in range(segs):
            th = 2 * math.pi * k / segs
            rr, zz = shape(th, i, r, z) if shape else (r, z)
            ring.append(bm.verts.new((rr * math.cos(th), rr * math.sin(th), zz)))
        rings.append(ring)
    bottom = bm.verts.new((0, 0, profile[0][1]))
    top = bm.verts.new((0, 0, profile[-1][1]))
    for k in range(segs):
        k1 = (k + 1) % segs
        bm.faces.new((bottom, rings[0][k1], rings[0][k]))
        for a, b in zip(rings, rings[1:]):
            bm.faces.new((a[k], a[k1], b[k1], b[k]))
        bm.faces.new((rings[-1][k], rings[-1][k1], top))
    bm.normal_update()
    return bm


def slab_bm(half, n, z_of, t):
    """A thin closed slab over the square [-half, half]^2: its top at z_of(x, y), ``t`` thick."""
    bm = bmesh.new()
    top, bot = {}, {}
    for j in range(n + 1):
        for i in range(n + 1):
            x, y = -half + 2 * half * i / n, -half + 2 * half * j / n
            z = z_of(x, y)
            top[i, j] = bm.verts.new((x, y, z))
            bot[i, j] = bm.verts.new((x, y, z - t))
    for j in range(n):
        for i in range(n):
            bm.faces.new((top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]))
            bm.faces.new((bot[i, j + 1], bot[i + 1, j + 1], bot[i + 1, j], bot[i, j]))
    edge = [(i, 0) for i in range(n)] + [(n, j) for j in range(n)] + [(i, n) for i in range(n, 0, -1)] + [(0, j) for j in range(n, 0, -1)]
    for a, b in zip(edge, edge[1:] + edge[:1]):
        bm.faces.new((bot[a], bot[b], top[b], top[a]))
    bm.normal_update()
    return bm


def paint(bm, color_of):
    return fl.vertex_colors(bm, color_of)


def noise(p, scale, seed):
    return 0.5 + 0.5 * fl.noise3(p, scale, seed)


# ---------------------------------------------------------------------------------------------------------------------
# Layers

def bun_bottom(m, mat, base, toast):
    prof = [(0.0, 0.0), (R * 0.86, 0.0), (R * 0.96, 0.003), (R, 0.009), (R * 0.99, 0.016), (R * 0.96, 0.019), (0.0, 0.019)]
    bm = lathe_bm(prof)
    light, dark = fl.srgb(base), fl.srgb(toast)
    paint(bm, lambda co, n: fl.mix(light, dark, 0.35 * noise(co, 70, 3) + 0.4 * fl.smoothstep(0.012, 0.0, co.z)))
    return fl.from_bm("BunBottom", bm, [m[mat]])


def bun_top(m, mat, base, toast, z0, height):
    prof = [(0.0, z0), (R * 0.95, z0), (R * 1.0, z0 + 0.005), (R * 0.99, z0 + 0.012), (R * 0.93, z0 + height * 0.45),
            (R * 0.78, z0 + height * 0.72), (R * 0.52, z0 + height * 0.92), (R * 0.22, z0 + height * 0.995), (0.0, z0 + height)]
    rnd = random.Random(7)
    lumps = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(-0.04, 0.04)) for _ in range(5)]

    def shape(th, i, r, z):
        k = 1 + sum(a * math.cos(th - p) for p, a in lumps) * 0.25
        return r * k, z
    bm = lathe_bm(prof, shape=shape)
    light, dark = fl.srgb(base), fl.srgb(toast)
    # toasted dark on the crown, paler round the side and the cut base
    paint(bm, lambda co, n: fl.mix(light, dark, min(1.0, 0.75 * fl.smoothstep(z0 + height * 0.3, z0 + height, co.z)
                                                      + 0.25 * noise(co, 60, 5))))
    return fl.from_bm("BunTop", bm, [m[mat]])


def veg_patty(m, z0):
    h = 0.016
    prof = [(0.0, z0), (R * 0.98, z0), (R * 1.04, z0 + h * 0.3), (R * 1.05, z0 + h * 0.65), (R * 1.0, z0 + h), (0.0, z0 + h)]
    rnd = random.Random(11)
    bumps = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(0.01, 0.03)) for _ in range(7)]
    bm = lathe_bm(prof, shape=lambda th, i, r, z: (r * (1 + sum(a * math.cos(3 * th - p) for p, a in bumps) * 0.5), z))
    base, crust, pea, corn = fl.srgb("#7E6E3E"), fl.srgb("#5A4A2A"), fl.srgb("#6FA040"), fl.srgb("#D9C24A")

    def col(co, n):
        c = fl.mix(base, crust, 0.5 * noise(co, 90, 13) + 0.3 * abs(n.z))
        f = fl.noise3(co, 420, 17)
        if f > 0.62:
            c = fl.mix(c, pea, 0.9)
        elif f < -0.7:
            c = fl.mix(c, corn, 0.8)
        return c
    paint(bm, col)
    return fl.from_bm("Patty", bm, [m["veg_patty"]])


def steak_patty(m, z0):
    h = 0.022
    prof = [(0.0, z0), (R * 0.98, z0), (R * 1.05, z0 + h * 0.25), (R * 1.07, z0 + h * 0.6), (R * 1.03, z0 + h * 0.92),
            (R * 0.96, z0 + h), (0.0, z0 + h)]
    rnd = random.Random(19)
    bumps = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(0.01, 0.025)) for _ in range(6)]
    bm = lathe_bm(prof, shape=lambda th, i, r, z: (r * (1 + sum(a * math.cos(2 * th - p) for p, a in bumps) * 0.5), z))
    meat, sear, mark = fl.srgb("#6A3A26"), fl.srgb("#4A2618"), fl.srgb("#1E120C")
    a = math.radians(35)

    def col(co, n):
        c = fl.mix(meat, sear, 0.55 * noise(co, 110, 23) + 0.25 * (1 - abs(n.z)))
        if n.z > 0.6:                                         # grill marks across the top
            u = co.x * math.cos(a) + co.y * math.sin(a)
            if (u % 0.019) < 0.005:
                c = fl.mix(c, mark, 0.85)
        return c
    paint(bm, col)
    return fl.from_bm("Patty", bm, [m["steak"]])


def tomato_slice(m, centre, z0, r=0.031, name="Tomato", turn=0.0):
    h = 0.0065
    prof = [(0.0, z0), (r * 0.97, z0), (r, z0 + h * 0.5), (r * 0.97, z0 + h), (0.0, z0 + h)]
    bm = lathe_bm(prof, segs=40)
    skin, flesh, seed = fl.srgb("#C8302A"), fl.srgb("#E0584A"), fl.srgb("#F0C870")

    def col(co, n):
        rr = math.hypot(co.x, co.y) / r
        th = math.atan2(co.y, co.x) + turn
        c = flesh if rr < 0.86 else skin
        if 0.3 < rr < 0.75 and math.cos(3 * th) > 0.55 and abs(n.z) > 0.5:     # three seed chambers
            c = fl.mix(c, seed, 0.6)
        if rr < 0.22:
            c = fl.mix(c, fl.srgb("#F07A6A"), 0.5)
        return c
    paint(bm, col)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((centre[0], centre[1], 0)))
    return fl.from_bm(name, bm, [m["tomato"]])


def lettuce(m, z0):
    h = 0.003
    rr = R * 1.16
    prof = [(0.0, z0), (rr * 0.5, z0), (rr, z0 - 0.001), (rr, z0 + h - 0.001), (rr * 0.5, z0 + h), (0.0, z0 + h)]

    def shape(th, i, r, z):
        if r < rr * 0.9:
            return r, z
        out = 1 + 0.05 * math.sin(9 * th) + 0.035 * math.sin(14 * th + 1.0)
        return r * out, z - 0.006 * (0.5 + 0.5 * math.sin(11 * th)) - 0.003
    bm = lathe_bm(prof, segs=120, shape=shape)
    leaf, pale = fl.srgb("#5E9E34"), fl.srgb("#A8D06A")
    paint(bm, lambda co, n: fl.mix(pale, leaf, fl.smoothstep(R * 0.6, R * 1.1, math.hypot(co.x, co.y)) * 0.8 + 0.2 * noise(co, 150, 29)))
    return fl.from_bm("Lettuce", bm, [m["lettuce"]])


def cheese(m, z0):
    half = 0.05

    def z_of(x, y):
        d = max(abs(x), abs(y))
        rr = math.hypot(x, y)
        over = max(0.0, rr - R * 0.98)                        # past the patty's edge it melts down over it
        return z0 + 0.002 - 2.2 * over ** 1.15 - 0.002 * (d / half) ** 4
    bm = slab_bm(half, 24, z_of, 0.0022)
    bmesh.ops.rotate(bm, verts=bm.verts, matrix=Matrix.Rotation(math.radians(20), 3, "Z"), cent=(0, 0, 0))
    return fl.from_bm("Cheese", bm, [m["cheese"]])


def onion_rings(m, z0):
    obs = []
    for k, (x, y, r) in enumerate(((0.014, 0.012, 0.018), (-0.02, -0.006, 0.015), (0.004, -0.024, 0.012))):
        bm = torus_bm(r, 0.0024, (x, y, z0 + 0.0024))
        obs.append(fl.from_bm(f"Onion{k}", bm, [m["onion"]]))
    return obs


def torus_bm(r, t, centre, segs=36, tube=8):
    bm = bmesh.new()
    vs = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ring = []
        for j in range(tube):
            b = 2 * math.pi * j / tube
            rr = r + t * math.cos(b)
            ring.append(bm.verts.new((centre[0] + rr * math.cos(a), centre[1] + rr * math.sin(a), centre[2] + t * math.sin(b) * 0.8)))
        vs.append(ring)
    for i in range(segs):
        for j in range(tube):
            a, b = vs[i], vs[(i + 1) % segs]
            bm.faces.new((a[j], b[j], b[(j + 1) % tube], a[(j + 1) % tube]))
    bm.normal_update()
    return bm


def toppings(m, bun, kind, rnd):
    """Seeds or oat flakes stuck on the top bun's crown: small flat ellipsoids on its surface."""
    import mathutils.bvhtree
    dg = bpy.context.evaluated_depsgraph_get()
    tree = mathutils.bvhtree.BVHTree.FromObject(bun, dg)
    mat = m["sesame"] if kind == "colony_burger" else m["oat"]
    bm_all = bmesh.new()
    n = 46 if kind == "colony_burger" else 34
    for _ in range(n):
        th = rnd.uniform(0, 2 * math.pi)
        rr = R * 0.82 * math.sqrt(rnd.uniform(0, 1))
        hit = tree.ray_cast(Vector((rr * math.cos(th), rr * math.sin(th), 0.3)), Vector((0, 0, -1)))
        if hit[0] is None:
            continue
        p, nrm = hit[0], hit[1]
        one = bmesh.new()
        bmesh.ops.create_uvsphere(one, u_segments=8, v_segments=5, radius=1)
        size = (0.0028, 0.0016, 0.0009) if kind == "colony_burger" else (0.0034, 0.0026, 0.0008)
        rot = nrm.to_track_quat("Z", "Y").to_matrix().to_4x4() @ Matrix.Rotation(rnd.uniform(0, math.pi), 4, "Z")
        bmesh.ops.transform(one, verts=one.verts, matrix=Matrix.Translation(p + nrm * 0.0004) @ rot @ Matrix.Diagonal((*size, 1)))
        tmp = bpy.data.meshes.new("tmp")
        one.to_mesh(tmp)
        one.free()
        bm_all.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    return fl.from_bm("Seeds" if kind == "colony_burger" else "Oats", bm_all, [mat])


def flag_pick(m, z_top):
    """The colony's flag on a wooden pick through the bun: steel blue with the Colony Works hexagon in bone."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.0016, radius2=0.0016, depth=0.085)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, z_top - 0.03 + 0.0425)))
    point = bmesh.new()
    bmesh.ops.create_cone(point, cap_ends=True, segments=10, radius1=0.0016, radius2=0.0, depth=0.006)
    bmesh.ops.translate(point, verts=point.verts, vec=Vector((0, 0, z_top + 0.055 + 0.003)))
    tmp = bpy.data.meshes.new("tmp")
    point.to_mesh(tmp)
    point.free()
    bm.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)
    pick = fl.from_bm("Pick", bm, [m["pick"]])
    # the flag: a rectangle of steel-blue cloth with a slight wave, off the pick toward -X
    fb = bmesh.new()
    w, hh, nn = 0.034, 0.022, 10
    zf = z_top + 0.036
    top, bot = [], []
    for i in range(nn + 1):
        u = i / nn
        x = -0.0016 - w * u
        y = 0.0025 * math.sin(u * math.pi * 1.5)
        top.append(fb.verts.new((x, y + 0.0006, zf + hh / 2)))
        bot.append(fb.verts.new((x, y + 0.0006, zf - hh / 2)))
    back_t = [fb.verts.new((v.co.x, v.co.y - 0.0012, v.co.z)) for v in top]
    back_b = [fb.verts.new((v.co.x, v.co.y - 0.0012, v.co.z)) for v in bot]
    for i in range(nn):
        fb.faces.new((bot[i], bot[i + 1], top[i + 1], top[i]))
        fb.faces.new((back_t[i], back_t[i + 1], back_b[i + 1], back_b[i]))
        fb.faces.new((top[i], top[i + 1], back_t[i + 1], back_t[i]))
        fb.faces.new((back_b[i], back_b[i + 1], bot[i + 1], bot[i]))
    fb.faces.new((top[0], back_t[0], back_b[0], bot[0]))
    fb.faces.new((bot[-1], back_b[-1], back_t[-1], top[-1]))
    fb.normal_update()
    flag = fl.from_bm("Flag", fb, [m["flag"]], smooth=False)
    # the bone hexagon mark with its dark core, on both faces of the flag
    marks = bmesh.new()
    for side in (1, -1):
        u = 0.5
        cx = -0.0016 - w * u
        cy = 0.0025 * math.sin(u * math.pi * 1.5) + (0.0012 if side > 0 else -0.0024)
        hexa = bmesh.new()
        bmesh.ops.create_cone(hexa, cap_ends=True, segments=6, radius1=0.0068, radius2=0.0068, depth=0.0006)
        bmesh.ops.transform(hexa, verts=hexa.verts, matrix=Matrix.Translation((cx, cy, zf)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
        tmp = bpy.data.meshes.new("tmp")
        hexa.to_mesh(tmp)
        hexa.free()
        marks.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    mark = fl.from_bm("FlagMark", marks, [m["flag_mark"]], smooth=False)
    return [pick, flag, mark]


# ---------------------------------------------------------------------------------------------------------------------
# Bites

CUT = {"BunBottom": "crumb", "BunTop": "crumb", "Patty": None, "Tomato": "tomato_cut", "Tomato2": "tomato_cut",
       "Lettuce": None, "Cheese": None}


def bite(objs, k, m, kind):
    """Bite k: three overlapping tooth scallops through every layer at the +X edge."""
    a, depth = BITES[k]
    cut_of = dict(CUT, Patty="veg_cut" if kind == "garden_burger" else "steak_cut")
    for j, (da, dr, rr) in enumerate(((-0.27, 0.003, 0.019), (0.0, 0.0, 0.023), (0.27, 0.004, 0.018))):
        r = rr
        d = R + r - depth + dr
        c = Vector((math.cos(a + da) * d, math.sin(a + da) * d, 0.05))
        for ob in objs:
            if ob.name.split(".")[0].rstrip("0123456789_") in ("Pick", "Flag", "FlagMark"):
                continue
            base = ob.name.split(".")[0]
            key = next((kk for kk in cut_of if base.endswith(kk)), None)
            cutter_bm = bmesh.new()
            bmesh.ops.create_uvsphere(cutter_bm, u_segments=20, v_segments=12, radius=1)
            bmesh.ops.transform(cutter_bm, verts=cutter_bm.verts, matrix=Matrix.Translation(c) @ Matrix.Diagonal((r, r * 0.9, 0.09, 1)))
            mat = m[cut_of[key]] if key and cut_of[key] else ob.data.materials[0]
            cutter = fl.from_bm(ob.name + f"_Cut{j}", cutter_bm, [mat])
            cutter.hide_render = True
            mod = ob.modifiers.new("Bite", "BOOLEAN")
            mod.operation = "DIFFERENCE"
            mod.solver = "MANIFOLD"
            mod.material_mode = "TRANSFER"
            mod.object = cutter
            fl.apply_modifiers(ob)
            bpy.data.objects.remove(cutter)


# ---------------------------------------------------------------------------------------------------------------------

def build(kind, name=None, bites=0, m=None, parent=None, location=(0, 0, 0)):
    """The ``kind`` (``garden_burger`` or ``colony_burger``) burger under an empty named ``name``, with ``bites`` taken."""
    m = m or mats()
    root = fl.empty(name or kind.title().replace("_", ""), parent, location)
    rnd = random.Random(3 if kind == "garden_burger" else 5)
    layers = []
    if kind == "garden_burger":
        layers.append(bun_bottom(m, "bun", "#D2A266", "#9C6A38"))
        layers.append(veg_patty(m, 0.019))
        layers.append(tomato_slice(m, (0.017, -0.012), 0.035, name="Tomato", turn=0.3))
        layers.append(tomato_slice(m, (-0.017, 0.013), 0.0352, r=0.029, name="Tomato2", turn=1.2))
        layers.append(lettuce(m, 0.0425))
        top = bun_top(m, "bun", "#C99558", "#8A5A2C", 0.0455, 0.04)
    else:
        layers.append(bun_bottom(m, "brioche", "#D49A58", "#A0602E"))
        layers.append(steak_patty(m, 0.019))
        layers.append(cheese(m, 0.0412))
        layers.append(tomato_slice(m, (0.006, 0.004), 0.0425, r=0.03, name="Tomato", turn=0.7))
        layers.extend(onion_rings(m, 0.049))
        top = bun_top(m, "brioche", "#C8823E", "#7E4418", 0.0535, 0.042)
    layers.append(top)
    for ob in layers:
        ob.parent = root
    bpy.context.view_layer.update()
    seeds = toppings(m, top, kind, rnd)
    seeds.parent = root
    layers.append(seeds)
    if kind == "colony_burger":
        for ob in flag_pick(m, 0.0535 + 0.042):
            ob.parent = root
    for k in range(bites):
        bite([o for o in root.children if o.type == "MESH"], k, m, kind)
    return root
