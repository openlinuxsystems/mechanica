"""Sculpted rock that dresses the island's cliff terraces (the 8 m steps of worldgen.json's terrain.cliffs), in place of
bare rock-textured slopes: the same Old Stone grey, beds and wear as the rock formations (rock_formations.py) and the
island's rocks, so a cliff reads as the stone the arches were cut from.

Pieces (meshes/world/cliffs/), each a voxel-remeshed mass worn by noise and faceted to a triangle budget:

* ``cliff_wall_a``, ``cliff_wall_b``, ``cliff_wall_c`` - bedded walls with 13 to 16 m of full face (15.5 to 18.5 m
  overall with their tapered ends) that stand up one 8 m step and roll over its top at about 8.5 to 9.5 m: a
  face leaning back 72 to 79 degrees, horizontal beds that stand proud or weather back, a few vertical joints, small
  ledges, and a lip that rolls over onto the top. Their ends recede into the hill and drop, so neighbours in a row
  overlap cleanly; each has its own skyline so a row does not repeat.
* ``cliff_corner`` - a wall whose foot turns 60 degrees round an 8 m radius, for a convex bend (the upper plateau a
  promontory; a concave bend is two straight walls overlapping).
* ``cliff_low_a``, ``cliff_low_b`` - outcrop bands 4 to 5 m tall, for gentle faces and the ends of the steep ones.
* ``scree_a``, ``scree_b`` - fans of rubble and broken blocks 6 to 10 m across and about 2 m high, for the foot of a face.

Every piece has its origin on the ground at the foot of its face, the middle of its length (Blender Z up, the exposed
face towards -Y, which is the game's +Z; the length along X). Rock runs BURY metres below the foot so the piece beds in
on uneven ground, and the back (+Y) runs into the hillside, where the terrain hides it. Each model carries a low-poly
``collider`` mesh (the visual is drawn only): a coarse copy of the rock, plus the loose blocks of a scree fan.
"""
import math
import random

import bpy
from mathutils import Vector

import kit as K
import rock_formations as RF
from props import _root
from props_nature import crag, boulder_points, hull
from props_stone import empty

BURY = 2.0          # rock under the foot of a face
STEP = 8.0          # the height of one cliff terrace (worldgen.json terrain.cliffs.step)


# ---------------------------------------------------------------- plan
def straight(u, v, z):
    """A straight foot along X: u along it, v back into the hill (+Y)."""
    return (u, v, z)


def bend(radius, turn):
    """A foot that turns turn degrees towards the hill round an arc of radius, its middle at the origin heading +X, with
    straight runs on past the arc: u along the foot, v back into the hill."""
    half = math.radians(turn) / 2
    arc = radius * half

    def f(u, v, z):
        if abs(u) <= arc:
            a = u / radius
            return (math.sin(a) * (radius - v), radius - math.cos(a) * (radius - v), z)
        s = math.copysign(1.0, u)
        a = s * half
        px, py = math.sin(a) * radius, radius - math.cos(a) * radius
        d = abs(u) - arc
        tx, ty = math.cos(a) * s, math.sin(a) * s
        return (px + tx * d - math.sin(a) * v, py + ty * d + math.cos(a) * v, z)
    return f


# ---------------------------------------------------------------- the rough masses
def _smooth(x, a, b):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def _beds(rnd, top, beds):
    """Bed boundaries from under the ground to top: a toe bed at the ground, then beds of thickness beds (lo, hi)."""
    zs = [-BURY - 0.3, rnd.uniform(0.3, 0.8)]
    while zs[-1] < top - 0.4:
        zs.append(zs[-1] + rnd.uniform(*beds))
    zs[-1] = max(zs[-1], top + 0.6)
    return zs


def wall_masses(G, rnd, length, height, depth, lean, plan=straight, beds=(0.9, 1.6), joints=(3.4, 6.0), taper=3.0,
                end_drop=1.2, end_back=1.4, ledges=2, ledge=0.55, wave=0.5, skyline=0.5, crest=0.7, toe=0.35):
    """The beds of a cliff wall as overlapping hulls, one per bed per column between vertical joints: each bed's face
    sits on a plane leaning back lean degrees, set proud or back by its own amount (ledges are a proud bed under a
    recessed one), waving in plan by up to wave metres; the skyline wanders by skyline metres; the ends drop end_drop and
    recede end_back over the last taper metres. The top bed rolls back over a crest crest metres deep."""
    cot = 1.0 / math.tan(math.radians(lean))
    ph = [rnd.uniform(0, 6.28) for _ in range(6)]
    half = length / 2

    def end(u):
        return _smooth(abs(u), half - taper, half + 0.4)

    def top(u):
        return (height + skyline * (0.6 * math.sin(u * 0.42 + ph[0]) + 0.4 * math.sin(u * 1.05 + ph[1]))
                - end_drop * end(u) ** 1.3)

    def front(u):
        return wave * (0.6 * math.sin(u * 0.37 + ph[2]) + 0.4 * math.sin(u * 0.9 + ph[3])) + end_back * end(u)

    zs = _beds(rnd, height + skyline, beds)
    n = len(zs) - 1
    off = [rnd.uniform(-0.2, 0.3) for _ in range(n)]
    off[0] = -toe
    for k in rnd.sample(range(2, max(3, n - 1)), min(ledges, max(0, n - 3))):
        off[k] -= ledge
        off[k + 1] += ledge * 0.7
    cuts = [-half - 0.6]
    while cuts[-1] < half - joints[0]:
        cuts.append(cuts[-1] + rnd.uniform(*joints))
    cuts[-1] = half + 0.6
    # how deep each joint is worn into each bed (0: the bed runs on unbroken over it)
    jd = [[0.0 if (i in (0, len(cuts) - 1) or rnd.random() < 0.45) else rnd.uniform(0.2, 0.55) for _ in range(n)]
          for i in range(len(cuts))]
    # each block of a bed sits a little in or out of the bed's line, so the face is not a stack of bricks
    jog = [[rnd.uniform(-0.18, 0.18) for _ in range(n)] for _ in range(len(cuts))]
    zero = [0.0] * n
    for c in range(len(cuts) - 1):
        # a block between two joints, in lengths of at most 2.5 m so its top follows the skyline
        k = max(1, math.ceil((cuts[c + 1] - cuts[c]) / 2.5))
        for i in range(k):
            ua = cuts[c] + (cuts[c + 1] - cuts[c]) * i / k
            ub = cuts[c] + (cuts[c + 1] - cuts[c]) * (i + 1) / k
            ja = jd[c] if i == 0 else zero
            jb = jd[c + 1] if i == k - 1 else zero
            for s in range(n):
                pts = _block(rnd, plan, s, zs, ua, ub, ja[s], jb[s], top, front, off[s] + jog[c][s], cot, depth, crest)
                if pts:
                    hull(G, "bed", pts, RF.rock(), bev=0)
    return zs


def _block(rnd, plan, s, zs, ua, ub, ja, jb, top, front, off, cot, depth, crest):
    """Hull points of bed s from ua to ub along the foot, or None where the bed is above the top. A bed that would end
    less than a metre under the top joins the one below it (a thin top bed wears away), and where the top drops
    below a bed at one end the bed thins out to a wedge there."""
    za = zs[s]
    tops = [top(ua), top(ub)]
    if s > 0 and all(za >= tz - 1.0 for tz in tops):
        return None
    pts = []
    for side, u, j, tz in ((0, ua, ja, tops[0]), (1, ub, jb, tops[1])):
        last = zs[s + 1] >= tz - 1.0
        zb = max(tz, za + 0.3) if last else zs[s + 1]
        g = 0.05 if j > 0 else 0.0
        uu = u + g if side == 0 else u - g
        um = u + min(0.9, (ub - ua) / 3) * (1 if side == 0 else -1)
        dz = rnd.uniform(-0.06, 0.06)
        for z in (za, zb + dz):
            f = z * cot + front(u) + off + rnd.uniform(-0.1, 0.1)
            rolled = crest if (last and z > za) else 0.0
            pts.append(plan(uu, f + j + rolled, z))
            pts.append(plan(um, f + rolled * 0.8, z))
            back = depth * rnd.uniform(0.95, 1.05)
            zbk = max(za, z - 1.3) if last and z > za else z
            ux = u - 0.5 if side == 0 else u + 0.5
            pts.append(plan(ux, back, zbk))
            if last and z > za:
                # the top: flat for a couple of metres behind the crest, then down into the hillside
                pts.append(plan(um, f + crest + rnd.uniform(1.4, 2.4), z + rnd.uniform(0.0, 0.2)))
    return pts


def boulders(G, rnd, spots):
    """Rounded blocks half sunk into a mass (x, y, z, size)."""
    for x, y, z, s in spots:
        hull(G, "lump", boulder_points((s, s * rnd.uniform(0.7, 1.0), s * rnd.uniform(0.6, 0.85)), rnd, n=24, power=2.2,
                                       jitter=0.12), RF.rock(), (x, y, z), (rnd.uniform(-10, 10), rnd.uniform(-10, 10), rnd.uniform(0, 360)), bev=0)


# ---------------------------------------------------------------- finishing
def collider(P, ob, tris):
    """A coarse copy of ob, collision only (the 'collider' mesh), about tris triangles; ob itself is drawn only."""
    col = ob.copy()
    col.data = ob.data.copy()
    K.link(col)
    col.name = ob.name + "_collider"
    faces = sum(len(p.vertices) - 2 for p in col.data.polygons)
    dec = col.modifiers.new("coarse", "DECIMATE")
    dec.decimate_type = "COLLAPSE"
    dec.ratio = min(1.0, tris / max(faces, 1))
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(col.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    col.modifiers.clear()
    old = col.data
    col.data = me
    bpy.data.meshes.remove(old)
    col.parent = P
    col.matrix_world = ob.matrix_world.copy()
    K.collider_only(col)
    K.no_collide(ob)
    return col


def sculpted(P, G, seed, tris, col_tris, voxel=0.24, **kw):
    ob = RF.sculpt(P, "rock", G, seed, voxel=voxel, tris=tris, **kw)
    collider(P, ob, col_tris)
    return ob


def _sink(P):
    """Nothing of a piece reaches further under the ground than BURY (hull jitter can)."""
    for ob in P.children:
        if ob.type == "MESH":
            for v in ob.data.vertices:
                v.co.z = max(v.co.z, -BURY - 0.3)


# ================================================================== walls
def _cliff_wall(name, seed, length, height, depth, lean, tris=3400, col=520, plan=straight, lumps=(), **kw):
    rnd = random.Random(seed)
    P = _root(name)
    G = empty(P, "rough")
    wall_masses(G, rnd, length, height, depth, lean, plan=plan, **kw)
    boulders(G, rnd, [(*plan(u, v, 0)[:2], z, s) for u, v, z, s in lumps])
    sculpted(P, G, seed, tris, col, voxel=0.26, bed=1.2, inset=0.2, amp=0.42, freq=0.2)
    _sink(P)
    return P


def cliff_wall_a(seed=401):
    """A 15 m wall, its skyline rising to the right, two ledges, a joint-cut middle."""
    return _cliff_wall("cliff_wall_a", seed, 15.0, 8.9, 6.2, 76, ledges=2, skyline=0.5, wave=0.5,
                       lumps=[(-4.0, -0.4, -0.3, 1.8), (5.5, -0.2, -0.4, 1.4)])


def cliff_wall_b(seed=402):
    """A 13 m wall, steeper, with one broad ledge half way up and closely jointed beds."""
    return _cliff_wall("cliff_wall_b", seed, 13.0, 8.8, 5.6, 79, joints=(2.6, 4.4), ledges=1, ledge=0.8, skyline=0.35,
                       wave=0.35, lumps=[(2.0, -0.4, -0.3, 1.6)])


def cliff_wall_c(seed=403):
    """A 16 m wall leaning back further, its skyline dipping in the middle, beds of uneven thickness."""
    return _cliff_wall("cliff_wall_c", seed, 16.0, 8.9, 6.8, 72, beds=(0.7, 2.0), joints=(4.0, 7.0), ledges=3,
                       skyline=0.7, wave=0.7, tris=3600, col=560,
                       lumps=[(-6.0, -0.5, -0.4, 1.6), (0.5, -0.3, -0.3, 2.0), (6.5, -0.3, -0.4, 1.3)])


def cliff_corner(seed=404):
    """A wall turning 60 degrees towards the hill round an 8 m radius: a convex bend, the upper plateau a promontory."""
    return _cliff_wall("cliff_corner", seed, 17.4, 8.9, 6.0, 76, plan=bend(8.0, 60.0), ledges=2, skyline=0.45,
                       wave=0.4, tris=3800, col=600, lumps=[(-5.5, -0.4, -0.3, 1.5), (4.8, -0.3, -0.4, 1.7)])


def cliff_low_a(seed=405):
    """A 10 m band of outcrop 4.5 m tall, for a gentle face: broken beds sinking into the slope at both ends."""
    return _cliff_wall("cliff_low_a", seed, 10.0, 4.5, 5.0, 66, beds=(0.7, 1.3), joints=(2.6, 4.2), taper=3.2,
                       end_drop=2.0, end_back=1.6, ledges=1, ledge=0.45, skyline=0.45, wave=0.5, crest=0.9, tris=2000,
                       col=320, lumps=[(-3.2, -0.3, -0.2, 1.2)])


def cliff_low_b(seed=406):
    """An 8 m band 4 m tall, rounder and more broken, for the edges of the faces."""
    return _cliff_wall("cliff_low_b", seed, 8.0, 4.0, 4.4, 62, beds=(0.6, 1.1), joints=(2.2, 3.6), taper=3.0,
                       end_drop=1.8, end_back=1.5, ledges=1, ledge=0.4, skyline=0.5, wave=0.45, crest=1.0, tris=1700,
                       col=280, lumps=[(2.4, -0.3, -0.2, 1.3), (-1.5, -0.2, -0.3, 1.0)])


# ================================================================== scree
def _fan(name, seed, lobes, lumps, blocks, tris=1500, col=260):
    """A fan of rubble: lobes (x, y, reach, width, height) of cones heaped against the face at y = 0, lumps of buried
    blocks (x, y, size) worn into it, then blocks (n, size) of loose chiselled rock lying on it and beyond it."""
    rnd = random.Random(seed)
    P = _root(name)
    G = empty(P, "rough")
    for x, y, reach, width, h in lobes:
        pts = [(x + rnd.uniform(-0.3, 0.3), 1.2, h), (x + rnd.uniform(-0.6, 0.6), 0.2, h * 0.92)]
        for k in range(14):
            a = math.pi * (0.95 + 1.1 * k / 13)       # round the front, from the face on one side to the other
            r = rnd.uniform(0.9, 1.05)
            pts.append((x + math.cos(a) * width / 2 * r, min(1.6, y + math.sin(a) * reach * r), -0.5))
            pts.append((x + math.cos(a) * width / 4 * r, min(1.4, y * 0.5 + math.sin(a) * reach * 0.45 * r), h * 0.55))
        pts += [(x - width / 2, 1.8, -0.5), (x + width / 2, 1.8, -0.5), (x, 1.8, h * 1.05)]
        hull(G, "lobe", pts, RF.rock(), bev=0)
    for x, y, s in lumps:
        hull(G, "lump", boulder_points((s, s * rnd.uniform(0.7, 1.0), s * 0.7), rnd, n=20, power=2.0, jitter=0.15), RF.rock(),
             (x, y, -0.3), (rnd.uniform(-15, 15), rnd.uniform(-15, 15), rnd.uniform(0, 360)), bev=0)
    fan = sculpted(P, G, seed, tris, col, voxel=0.16, bed=0.7, inset=0.06, amp=0.22, freq=0.55, dark=(4, 1), scale=0.5)
    bpy.context.view_layer.update()
    inv = fan.matrix_world.inverted()
    n, size = blocks
    for k in range(n):
        # most lie on the fan, a few rolled out past its edge
        a = rnd.uniform(math.pi * 1.05, math.pi * 1.95)
        lobe = rnd.choice(lobes)
        r = rnd.uniform(0.15, 1.2) if k % 4 else rnd.uniform(1.0, 1.35)
        x, y = lobe[0] + math.cos(a) * lobe[3] / 2 * r, lobe[1] + math.sin(a) * lobe[2] * r
        hit, loc, _n, _i = fan.ray_cast(inv @ Vector((x, y, 10.0)), Vector((0, 0, -1)))
        z = max(0.0, loc.z) if hit else 0.0
        s = rnd.uniform(*size) * (0.6 if r > 1 else 1.0)
        crag(P, f"block{k}", (s, s * rnd.uniform(0.6, 0.95), s * rnd.uniform(0.45, 0.8)), (x, y, z - s * 0.18), rnd,
             rot=(rnd.uniform(-20, 20), rnd.uniform(-20, 20), rnd.uniform(0, 360)), mat=RF.rock() if k % 3 else RF.rock_dark(),
             taper=0.85, bev=0)
    _sink(P)
    return P


def scree_a(seed=407):
    """A broad fan about 9 m across and 2.2 m high, heaped highest at one side."""
    return _fan("scree_a", seed, [(-1.2, -0.2, 4.0, 6.0, 2.2), (2.4, 0.0, 3.0, 5.0, 1.5)],
                [(-2.0, -1.4, 1.3), (1.0, -2.2, 1.0), (-0.4, -0.4, 1.5), (3.0, -1.0, 1.1)], (16, (0.5, 1.5)))


def scree_b(seed=408):
    """A narrower, steeper fan about 6.5 m across and 2 m high, with bigger blocks fallen from the face."""
    return _fan("scree_b", seed, [(0.0, 0.0, 3.4, 5.4, 2.0), (-1.6, -0.4, 2.2, 3.0, 1.1)],
                [(0.6, -1.0, 1.4), (-1.3, -1.6, 1.0)], (11, (0.7, 1.8)), tris=1300, col=240)


PROPS = {
    "cliff_wall_a": cliff_wall_a,
    "cliff_wall_b": cliff_wall_b,
    "cliff_wall_c": cliff_wall_c,
    "cliff_corner": cliff_corner,
    "cliff_low_a": cliff_low_a,
    "cliff_low_b": cliff_low_b,
    "scree_a": scree_a,
    "scree_b": scree_b,
}
