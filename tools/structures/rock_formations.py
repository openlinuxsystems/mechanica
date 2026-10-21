"""Natural rock formations for the open country (docs/world-generation.md, Rock formations), in the rock of
docs/art-direction.md: the Old Stone grey with its darker beds, the same stone as the island's rocks and deposits.

Rock arches a player walks, drives or flies under (a medium arch, a large one, a double arch and a fallen one), a rock
fin with a window through it, a sea stack, a cluster of desert hoodoos and a balanced rock. They are weathered rock,
not built: each is one surface sculpted from rough masses (merged by a voxel remesh), worn by noise, cut into beds
that stand proud or weather back, and faceted down to a triangle budget, so it reads as the faceted rock of the
deposits rather than as blocks. Loose blocks of the deposits' chiselled rock lie round their feet.

Each builder returns an empty with the formation under it, its origin on the ground at the formation's centre (Blender
Z up, front towards -Y). The rock runs a little below the ground so it beds in on uneven ground. The arches, the window
and the hoodoos are POI models (meshes/world/formations/, with a collider mesh so the openings stay open); the sea
stack and the balanced rock are world objects whose collider is the object's own.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector, noise

import kit as K
from families import STONE, STONE_D
from props import _root
from props_nature import crag, boulder_points, hull
from props_stone import empty

BASE = -1.2         # how far the rock runs under the ground


# ---------------------------------------------------------------- materials
def rock():
    """The formations' stone: the Old Stone grey, less moss than the ruins (it stands in the desert too)."""
    return K.mat("stone_formation", STONE, rough=0.9, grime=0.3, moss=0.3, cracks=0.7, noise_scale=3.0)


def rock_dark():
    """The darker, harder beds."""
    return K.mat("stone_formation_d", STONE_D, rough=0.9, grime=0.25, moss=0.0, cracks=0.8, noise_scale=3.0)


def _object_space(m, scale):
    """Review renders only: drive the material's noise from object space, so its mottling and cracks keep the same size
    on a 40 m arch as on a boulder (the game's detail texture does the same with box UVs)."""
    if K.EXPORT or m.get("object_space"):
        return
    nt = m.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "TEX_VORONOI") and not n.inputs["Vector"].is_linked:
            nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
    m["object_space"] = True


# ---------------------------------------------------------------- rough masses
def segment(c0, c1, t0, t1, d0, d1, rnd, jag=0.1):
    """Hull points of a length of rock from c0 to c1 (centres in the XZ plane): t across the line in XZ, d deep in Y."""
    pts = []
    c0, c1 = Vector(c0), Vector(c1)
    tng = (c1 - c0).normalized()
    nrm = Vector((-tng.z, 0, tng.x))
    for c, t, d in ((c0, t0, d0), (c1, t1, d1)):
        for a, b in ((1, 0.55), (0.55, 1), (-0.55, 1), (-1, 0.55), (-1, -0.55), (-0.55, -1), (0.55, -1), (1, -0.55)):
            j = rnd.uniform(1 - jag, 1 + jag * 0.5)
            pts.append(tuple(c + nrm * a * t / 2 * j + Vector((0, b * d / 2 * j, 0))))
    return pts


def rib(G, pts, th, dp, rnd, jag=0.1, overlap=0.6):
    """A run of rock along centre points pts, thickness th[i] and depth dp[i] at each, as overlapping hull lengths."""
    for i in range(len(pts) - 1):
        c0, c1 = Vector(pts[i]), Vector(pts[i + 1])
        tng = (c1 - c0).normalized()
        hull(G, "rib", segment(c0 - tng * overlap, c1 + tng * overlap, th[i], th[i + 1], dp[i], dp[i + 1], rnd, jag), rock(), bev=0)


def arch_line(span, rise, t_leg, t_top, n=14, lean=0.0):
    """Centre points and thicknesses of an arch rib whose opening is span wide at the ground and rise high: legs from
    below the ground up over the top. lean shifts the crown sideways (an arch is rarely even)."""
    rx = span / 2 + t_leg / 2
    pts, th = [], []
    for k in range(n + 1):
        a = math.pi * (1 - k / n)
        c = math.cos(a)
        t = t_top + (t_leg - t_top) * abs(c) ** 1.6
        rz = rise + t_top / 2
        x = rx * c + lean * (1 - c * c)
        pts.append((x, 0.0, rz * math.sin(a)))
        th.append(t)
    pts.insert(0, (pts[0][0] * 1.02, 0.0, BASE - 0.3))
    th.insert(0, t_leg * 1.1)
    pts.append((pts[-1][0] * 1.02, 0.0, BASE - 0.3))
    th.append(t_leg * 1.1)
    return pts, th


def column(G, r, h, rnd, loc=(0, 0), bulges=3, waist=0.25, top=0.8, segs=12, rings=14, lean=(0.0, 0.0)):
    """A hoodoo's or stack's column: rings of a wavering radius (soft beds weather to waists), from below the ground."""
    bm = bmesh.new()
    ph = rnd.uniform(0, 6.28)
    lobes = [rnd.uniform(0, 6.28) for _ in range(3)]
    prev = first = None
    for k in range(rings + 1):
        f = k / rings
        z = BASE + (h - BASE) * f
        rr = r * (1 - (1 - top) * f) * (1 - waist * 0.5 * (1 + math.sin(f * bulges * 2 * math.pi + ph)))
        if f < 0.12:
            rr *= 1 + (0.12 - f) * 3.0      # the foot flares into its scree
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            q = rr * (1 + 0.1 * math.sin(2 * a + lobes[0]) + 0.07 * math.sin(3 * a + lobes[1] + f * 2))
            ring.append(bm.verts.new((loc[0] + lean[0] * f + q * math.cos(a), loc[1] + lean[1] * f + q * math.sin(a), z)))
        if prev:
            for i in range(segs):
                j = (i + 1) % segs
                bm.faces.new((prev[i], prev[j], ring[j], ring[i]))
        else:
            first = ring
        prev = ring
    bm.faces.new(prev)
    bm.faces.new(list(reversed(first)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K.place(K.from_bm("column", bm, rock()), parent=G)


def mound(G, rx, ry, h, rnd, loc=(0, 0)):
    """A low heap of rock (the scree a formation stands in), its top h above the ground."""
    pts = boulder_points((rx * 2, ry * 2, h - BASE), rnd, n=40, power=2.0, jitter=0.1)
    return hull(G, "mound", pts, rock(), (loc[0], loc[1], BASE), (0, 0, rnd.uniform(0, 360)), bev=0)


# ---------------------------------------------------------------- the sculpt
def _smooth(f, a, b):
    t = min(1.0, max(0.0, (f - a) / (b - a)))
    return t * t * (3 - 2 * t)


def _remesh(ob, voxel):
    """ob's mesh merged into one closed surface by a voxel remesh, as a new mesh (ob keeps its own)."""
    mod = ob.modifiers.new("remesh", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = voxel
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    ob.modifiers.remove(mod)
    return me


def sculpt(P, name, G, seed, voxel, tris, bed=1.3, inset=0.22, amp=0.35, freq=0.22, dark=(3, 1), cut=None, scale=0.4):
    """Merges the rough masses under G into one weathered rock under P and removes G: a voxel remesh of size voxel,
    worn by noise (amp metres at freq per metre), cut into beds bed metres thick that weather back inset metres
    at their foot, then faceted down to about tris triangles. Every dark[0]-th bed (from dark[1]) is the darker
    stone. cut is an object subtracted before the remesh (a window), removed too."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    for ob in G.children_recursive:
        if ob.type != "MESH":
            continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        me.transform(ob.matrix_world)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    tmp = K.from_bm(name + "_raw", bm, rock())
    me = _remesh(tmp, voxel)
    if cut is not None:
        # the masses overlap, so they are merged before the window is cut, and merged again after
        tmp.data = me
        mod = tmp.modifiers.new("cut", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = cut
        cut.hide_render = True
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        tmp.modifiers.clear()
        tmp.data = me
        me = _remesh(tmp, voxel)
    bm = bmesh.new()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)
    bm.normal_update()
    noise.seed_set(seed)
    off = Vector((seed * 7.31, seed * 3.17, seed * 5.53))
    moves = []
    for v in bm.verts:
        p = v.co
        n = v.normal
        wear = amp * noise.fractal(p * freq + off, 0.6, 2.0, 3) + amp * 0.8 * noise.noise(p * freq * 0.3 + off)
        side = max(0.0, 1.0 - abs(n.z) * 1.15)
        wob = bed * 0.3 * noise.noise(Vector((p.x * 0.05, p.y * 0.05, 0.0)) + off)
        f = (p.z + wob) / bed
        f -= math.floor(f)
        groove = -inset * (1.0 - _smooth(f, 0.0, 0.45))
        moves.append(n * (wear + groove * side))
    for v, d in zip(bm.verts, moves):
        if v.co.z > BASE + 0.05:
            v.co += d
    bm.to_mesh(tmp.data)
    bm.free()
    tmp.modifiers.clear()
    faces = sum(len(p.vertices) - 2 for p in tmp.data.polygons) * 1.0
    dec = tmp.modifiers.new("facet", "DECIMATE")
    dec.decimate_type = "COLLAPSE"
    dec.ratio = min(1.0, tris / max(faces, 1.0))
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    me.name = name
    bpy.data.objects.remove(tmp)
    if cut is not None:
        bpy.data.objects.remove(cut)
    for ob in list(G.children_recursive) + [G]:
        bpy.data.objects.remove(ob)
    me.materials.clear()
    me.materials.append(rock())
    me.materials.append(rock_dark())
    for m in me.materials:
        _object_space(m, scale)
    for poly in me.polygons:
        poly.use_smooth = False
        c = poly.center
        wob = bed * 0.3 * noise.noise(Vector((c.x * 0.05, c.y * 0.05, 0.0)) + off)
        k = math.floor((c.z + wob) / bed)
        poly.material_index = 1 if (k - dark[1]) % dark[0] == 0 and poly.normal.z < 0.75 else 0
    ob = K.link(bpy.data.objects.new(name, me))
    ob.parent = P
    return ob


def scree(P, rnd, n, ring, size, h=(0.5, 1.0), mat=None):
    """Loose chiselled blocks round a formation's feet: n of them, ring (r0, r1) from the centre or a list of (x, y)."""
    out = []
    for k in range(n):
        if isinstance(ring, list):
            x, y = ring[k % len(ring)]
            x += rnd.uniform(-0.8, 0.8)
            y += rnd.uniform(-0.8, 0.8)
        else:
            a = rnd.uniform(0, 2 * math.pi)
            r = rnd.uniform(*ring)
            x, y = math.cos(a) * r, math.sin(a) * r
        s = rnd.uniform(*size)
        out.append(crag(P, f"scree{k}", (s, s * rnd.uniform(0.7, 1.0), s * rnd.uniform(*h)), (x, y, -0.15), rnd,
                        rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 360)), mat=mat or rock(), taper=0.8, bev=0))
    return out


# ================================================================== arches
def _arch(G, rnd, span, rise, t_leg, t_top, d_leg, d_top, lean=0.0, x0=0.0, n=14):
    pts, th = arch_line(span, rise, t_leg, t_top, n=n, lean=lean)
    pts = [(x + x0, y, z) for x, y, z in pts]
    dp = [d_top + (d_leg - d_top) * (abs(p[0] - x0) / (span / 2 + t_leg / 2)) ** 2 for p in pts]
    rib(G, pts, th, dp, rnd)
    return pts, th


def rock_arch_medium(seed=301):
    """An arch of weathered rock about 8 m across and 6 m high under its span: walk or drive under it."""
    rnd = random.Random(seed)
    P = _root("rock_arch_medium")
    G = empty(P, "rough")
    _arch(G, rnd, span=8.0, rise=6.2, t_leg=4.2, t_top=2.3, d_leg=5.0, d_top=3.2, lean=0.5)
    # buttresses: the fin the arch was cut from still stands either side, broader at the foot
    for sx in (-1, 1):
        hull(G, "buttress", segment((sx * 7.2, 0, BASE), (sx * 6.6, 0, 4.6 + sx * 0.4), 4.2, 2.6, 5.6, 3.6, rnd), rock(), bev=0)
        hull(G, "foot", boulder_points((4.0, 5.6, 2.6 - BASE), rnd, n=30), rock(), (sx * 8.6, rnd.uniform(-0.4, 0.4), BASE), bev=0)
    hull(G, "cap", segment((-3.5, 0, 7.4), (3.0, 0, 7.6), 1.3, 1.1, 3.2, 2.8, rnd), rock(), bev=0)
    sculpt(P, "rock", G, seed, voxel=0.2, tris=2600, bed=1.1, inset=0.2, amp=0.28, freq=0.3)
    scree(P, rnd, 7, [(-9.5, -2.8), (10.0, 2.5), (-6.5, 3.5), (5.5, -3.6), (11, -1.5), (-10.8, 1.2), (0.6, 3.2)], (0.7, 1.4))
    return P


def rock_arch_large(seed=302):
    """A great arch about 22 m across and 15 m high under its span: room to fly through."""
    rnd = random.Random(seed)
    P = _root("rock_arch_large")
    G = empty(P, "rough")
    _arch(G, rnd, span=22.0, rise=15.0, t_leg=9.0, t_top=4.2, d_leg=11.0, d_top=6.0, lean=1.5, n=16)
    for sx in (-1, 1):
        hull(G, "buttress", segment((sx * 16.5, 0, BASE), (sx * 15.0, 0, 11.0 + sx * 1.5), 8.0, 5.0, 12.0, 7.5, rnd), rock(), bev=0)
        hull(G, "foot", boulder_points((8.0, 12.0, 5.0 - BASE), rnd, n=30), rock(), (sx * 19.5, rnd.uniform(-1, 1), BASE), bev=0)
    hull(G, "cap", segment((-9.0, 0, 18.4), (7.5, 0, 18.9), 2.6, 2.2, 6.4, 5.2, rnd), rock(), bev=0)
    sculpt(P, "rock", G, seed, voxel=0.42, tris=4400, bed=1.9, inset=0.4, amp=0.6, freq=0.16)
    scree(P, rnd, 9, [(-22, -6), (23, 5), (-17, 7.5), (13, -8.5), (25, -3), (-24.5, 3), (2, 7.5), (-4, -8), (19, 8)], (1.2, 2.6))
    return P


def rock_arch_double(seed=303):
    """One rock fin with two openings side by side, a wide one about 10 m and a narrow one about 6 m, on a shared pier."""
    rnd = random.Random(seed)
    P = _root("rock_arch_double")
    G = empty(P, "rough")
    _arch(G, rnd, span=10.0, rise=8.5, t_leg=4.8, t_top=2.8, d_leg=6.4, d_top=4.0, lean=0.6, x0=-5.6)
    _arch(G, rnd, span=6.0, rise=6.0, t_leg=4.8, t_top=2.6, d_leg=6.0, d_top=3.8, lean=-0.3, x0=7.4)
    # the long back the openings were worn through, highest over the big one
    hull(G, "back", segment((-12.5, 0, 9.5), (2.0, 0, 11.0), 2.4, 2.6, 4.0, 4.2, rnd), rock(), bev=0)
    hull(G, "back2", segment((1.5, 0, 10.6), (12.0, 0, 7.4), 2.6, 2.2, 4.2, 3.6, rnd), rock(), bev=0)
    hull(G, "pier", segment((1.0, 0, BASE), (1.2, 0, 9.0), 4.6, 3.4, 6.6, 4.6, rnd), rock(), bev=0)
    for sx, x in ((-1, -13.8), (1, 13.2)):
        hull(G, "buttress", segment((x, 0, BASE), (x - sx * 0.8, 0, 6.5), 4.8, 3.0, 6.6, 4.2, rnd), rock(), bev=0)
        hull(G, "foot", boulder_points((4.6, 6.6, 2.8 - BASE), rnd, n=30), rock(), (x + sx * 1.6, rnd.uniform(-0.5, 0.5), BASE), bev=0)
    sculpt(P, "rock", G, seed, voxel=0.26, tris=3600, bed=1.3, inset=0.25, amp=0.35, freq=0.25)
    scree(P, rnd, 8, [(-16, -3), (16, 2.5), (-9, 4.2), (4.5, -4.2), (-3, -4.4), (12, -3.8), (17, -1.5), (8.5, 4.0)], (0.8, 1.6))
    return P


def rock_arch_collapsed(seed=304):
    """An arch that fell: two broken legs leaning in towards each other and the span lying between them in pieces."""
    rnd = random.Random(seed)
    P = _root("rock_arch_collapsed")
    G = empty(P, "rough")
    pts, th = arch_line(13.0, 9.0, 4.8, 3.0, n=14, lean=0.4)
    dp = [4.0 + 2.2 * (abs(p[0]) / 9.0) ** 2 for p in pts]
    # the left leg stands to well up its curve, the right one broke off low
    rib(G, pts[:7], th[:7], dp[:7], rnd)
    rib(G, pts[-5:], th[-5:], dp[-5:], rnd)
    for sx in (-1, 1):
        hull(G, "foot", boulder_points((4.6, 6.4, 2.6 - BASE), rnd, n=30), rock(), (sx * 10.4, rnd.uniform(-0.5, 0.5), BASE), bev=0)
    sculpt(P, "rock", G, seed, voxel=0.24, tris=2400, bed=1.2, inset=0.22, amp=0.32, freq=0.28)
    # the span: big pieces where it hit, the largest still showing its curve, smaller ones thrown further
    H = empty(P, "fallen")
    for k, (x, y, yaw, size, tilt) in enumerate(((-0.8, 1.2, 20, (6.0, 3.6, 2.8), 14), (3.4, -0.6, -35, (4.4, 3.2, 2.6), -10),
                                                (0.8, -3.4, 60, (3.0, 2.6, 2.0), 8), (5.8, 2.6, 10, (2.6, 2.4, 1.8), -6))):
        hull(H, "piece", boulder_points((size[0], size[1], size[2] - BASE * 0.5), rnd, n=26, power=2.2, jitter=0.12), rock(),
             (x, y, BASE * 0.5), (rnd.uniform(-4, 4), tilt, yaw), bev=0)
    sculpt(P, "fallen", H, seed + 1, voxel=0.2, tris=1100, bed=1.2, inset=0.18, amp=0.25, freq=0.3, dark=(3, 0))
    scree(P, rnd, 9, (2.0, 9.0), (0.6, 1.4))
    return P


def rock_window(seed=305):
    """A thin fin of rock 21 m long and 12 m high with a window worn through it, about 5 m across."""
    rnd = random.Random(seed)
    P = _root("rock_window")
    G = empty(P, "rough")
    tops = [(-10.5, 2.5), (-8.0, 8.5), (-4.5, 10.4), (-1.5, 12.6), (2.0, 11.0), (5.0, 12.2), (8.0, 8.0), (10.5, 3.0)]
    for (x0, z0), (x1, z1) in zip(tops, tops[1:]):
        pts = [(x0 - 0.4, rnd.uniform(-0.2, 0.2), BASE), (x1 + 0.4, rnd.uniform(-0.2, 0.2), BASE),
               (x0 - 0.4, 0, z0), (x1 + 0.4, 0, z1)]
        cloud = []
        for x, y, z in pts:
            w = 1.5 if z > 0 else 3.0
            for dy in (-1, 1):
                cloud.append((x, y + dy * w * rnd.uniform(0.85, 1.05), z))
            if z > 0:
                cloud.append((x, y, z + rnd.uniform(0.2, 0.6)))
        hull(G, "fin", cloud, rock(), bev=0)
    hull(G, "foot", boulder_points((22.0, 8.0, 2.4 - BASE), rnd, n=40, power=2.2), rock(), (0, 0, BASE), bev=0)
    cut = K.cyl("window", 2.9, 12.0, rock(), loc=(-0.4, 0, 5.8), rot=(90, 0, 8), segs=14, bev=0, r2=2.5)
    for v in cut.data.vertices:
        v.co.x *= 1.0 + 0.12 * math.sin(3 * math.atan2(v.co.y, v.co.x))
    sculpt(P, "rock", G, seed, voxel=0.22, tris=2800, bed=1.2, inset=0.2, amp=0.3, freq=0.28, cut=cut)
    scree(P, rnd, 7, [(-10, -4.5), (9, 4.2), (-4, 4.6), (4, -4.8), (-11.5, 3), (11.5, -3), (0, -5)], (0.7, 1.5))
    return P


# ================================================================== pillars
def sea_stack(seed=306):
    """An 18 m stack of bedded rock the sea cut from the cliffs, a notch worn round its foot, a smaller stump beside it."""
    rnd = random.Random(seed)
    P = _root("sea_stack")
    G = empty(P, "rough")
    column(G, 4.4, 18.0, rnd, bulges=3, waist=0.08, top=0.62, segs=7, rings=18, lean=(0.6, -0.3))
    hull(G, "notch_foot", boulder_points((10.5, 9.0, 1.4 - BASE), rnd, n=30), rock(), (0, 0, BASE), bev=0)
    column(G, 1.9, 5.5, rnd, loc=(6.6, 3.2), bulges=1, waist=0.15, top=0.6, segs=9, rings=6)
    sculpt(P, "rock", G, seed, voxel=0.24, tris=2600, bed=2.1, inset=0.22, amp=0.6, freq=0.15, dark=(3, 0))
    scree(P, rnd, 6, (5.0, 8.5), (0.7, 1.6))
    return P


def hoodoo_cluster(seed=307):
    """Four slender hoodoos of soft rock, 5 to 11 m tall, each under a cap of harder dark rock, on a shared heap of scree."""
    rnd = random.Random(seed)
    P = _root("hoodoo_cluster")
    G = empty(P, "rough")
    spots = ((0.0, 0.0, 1.5, 11.0), (4.2, 2.0, 1.2, 8.2), (-3.8, 2.6, 1.1, 7.0), (2.0, -3.6, 0.95, 5.2), (-2.6, -2.8, 0.8, 4.0))
    for x, y, r, h in spots:
        column(G, r, h, rnd, loc=(x, y), bulges=rnd.choice((2, 3)), waist=0.3, top=0.7, segs=9, rings=12,
               lean=(rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)))
    mound(G, 6.5, 5.5, 1.1, rnd)
    sculpt(P, "rock", G, seed, voxel=0.16, tris=2300, bed=0.9, inset=0.13, amp=0.2, freq=0.45, dark=(4, 2))
    # the caps: slabs of the harder bed, wider than the column under them, tipped where the soft rock wore unevenly
    H = empty(P, "rough_caps")
    for k, (x, y, r, h) in enumerate(spots):
        hull(H, "cap", boulder_points((r * 2.5, r * 2.1, r * 0.9), rnd, n=24, power=2.0, jitter=0.15, sink=0.0), rock(),
             (x + rnd.uniform(-0.2, 0.2), y + rnd.uniform(-0.2, 0.2), h - 0.35), (rnd.uniform(-7, 7), rnd.uniform(-7, 7), rnd.uniform(0, 360)), bev=0)
    sculpt(P, "caps", H, seed + 1, voxel=0.12, tris=600, bed=5.0, inset=0.0, amp=0.1, freq=0.6, dark=(1, 0))
    scree(P, rnd, 6, (4.0, 7.5), (0.5, 1.0))
    return P


def balanced_rock(seed=308):
    """A boulder the size of a van perched on a waisted pedestal 5 m high that the wind wore from under it."""
    rnd = random.Random(seed)
    P = _root("balanced_rock")
    G = empty(P, "rough")
    column(G, 1.7, 5.4, rnd, bulges=1, waist=0.42, top=0.8, segs=10, rings=10)
    mound(G, 3.6, 3.2, 0.8, rnd)
    sculpt(P, "pedestal", G, seed, voxel=0.15, tris=1300, bed=0.9, inset=0.16, amp=0.12, freq=0.5, dark=(3, 1))
    H = empty(P, "rough_top")
    hull(H, "boulder", boulder_points((5.6, 4.2, 3.6), rnd, n=48, power=2.3, jitter=0.08), rock(), (0.5, 0.0, 5.0), (4, -7, 20), bev=0)
    sculpt(P, "boulder", H, seed + 1, voxel=0.16, tris=1100, bed=1.0, inset=0.0, amp=0.25, freq=0.4, dark=(100, 50))
    scree(P, rnd, 5, (3.0, 5.5), (0.5, 1.1))
    return P


PROPS = {
    "rock_arch_medium": rock_arch_medium,
    "rock_arch_large": rock_arch_large,
    "rock_arch_double": rock_arch_double,
    "rock_arch_collapsed": rock_arch_collapsed,
    "rock_window": rock_window,
    "sea_stack": sea_stack,
    "hoodoo_cluster": hoodoo_cluster,
    "balanced_rock": balanced_rock,
}

# The formations a player passes under or through: their models carry a collider mesh (POI models). The rest are world
# objects with the object's own collider.
COLLIDE = ("rock_arch_medium", "rock_arch_large", "rock_arch_double", "rock_arch_collapsed", "rock_window", "hoodoo_cluster")
