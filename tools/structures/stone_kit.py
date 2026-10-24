"""The Old Stone structure kit (docs/art-direction.md): the pieces the ruins are assembled from.

Whoever lived here before the colony built in dressed stone: coursed walls of squared blocks on a plinth, square
pillars and round columns, arches of wedge stones, stone stairs and flagstone floors. Time has cut all of it: walls
stand whole, broken to a ragged top or collapsed to a stub, pillars snap, blocks fall and lie in the grass, rubble
heaps up against what still stands, moss grows on every top face and grass at every foot. The only light is the
cyan of the colony's probe crystals growing out of the stone.

Pieces:

* ``wall`` - a run of courses between two points, its top a height profile (``whole``, ``broken``, ``collapsed`` or
  any function), with door and window openings (lintel or arched head), a plinth course, grass along its foot.
* ``pillar`` (square, stacked blocks, plinth and cap, whole or broken) and ``column`` (round drums on a base, whole or
  broken, its drums lying beside it).
* ``arch`` - a free-standing arch of wedge stones on two piers (or broken, the ring fallen in).
* ``stair`` - a flight of stone steps (walked as a ramp), ``spiral`` - a square stair winding up inside a tower.
* ``floor`` - flagstones over a bed, a few lifted, cracked or gone; ``slab`` - an upper floor of big stones on
  corbels with a broken edge.
* ``rubble`` - a heap of fallen blocks on a gravel bank (a way up when it leans on a wall), ``fallen`` - a single block
  lying where it fell, ``moss``, ``grass``, ``crystals`` (with their light).
* ``house`` - a stone house of one or two storeys from the above (walls, openings, an upper floor and its stair),
  and ``tower`` - a square tower or keep with a stair inside to a top floor behind a crenellated parapet.

How it is built: the drawn stone goes into a few big meshes per site (``Site``), not thousands of objects. Coursed
blocks are plain boxes a hand's width apart over a darker mortar core, so the joints read without bevels; loose and
top blocks, pillars, steps and arches are bevelled (5 cm, the building rule). Nothing drawn collides: each piece adds
plain boxes and ramps to the site's collider (one box per course run of a wall, one per floor, a ramp per flight), so
the player never snags on a block. Sizes sit on the 0.25 m grid: courses 0.5 m, risers 0.25 m, doors 1.5 by 2.5 m,
storeys 3.5 m.

Frame: Blender Z up, front towards -Y (the POI's +Z), as everywhere in tools/structures.
"""
import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

import kit as K
from families import CYAN, STONE, STONE_D, glow

COURSE = 0.5            # course height
STOREY = 3.5            # floor to floor
RISE = 0.25             # stair riser
DOOR_W, DOOR_H = 1.5, 2.5
GAP = 0.03              # joint between blocks
MORTAR = "#55594f"
EARTH = "#6b5d4a"
GROUND = "#727a56"      # the trodden, mossy earth between the stones
GRASS = ("#6d8f3c", "#5d8a3a", "#83994a")
MOSS = "#6f9a3a"
RAMP_MAX = 40.0         # a flight no steeper than this collides as a ramp


# ---------------------------------------------------------------- materials
def wall_stone():
    """The family's stone (families.stone) for walls tens of metres long: its crack lines toned down, as the
    landmarks' rock (the blocks' joints already break the faces up); moss on every top face."""
    return K.mat("ruin_stone", STONE, rough=0.9, wear=0.0, grime=0.35, moss=1.0, cracks=0.25, noise_scale=3.0)


def dark():
    """The dark stone of odd blocks, bands and lintels."""
    return K.mat("ruin_stone_d", STONE_D, rough=0.9, grime=0.3, moss=0.6, cracks=0.3, noise_scale=3.0)


def flag():
    """Stone walked on: moss only in patches."""
    return K.mat("ruin_flagstone", STONE, rough=0.9, wear=0.0, grime=0.35, moss=0.15, cracks=0.3, noise_scale=3.0)


def mortar():
    return K.mat("stone_mortar", MORTAR, rough=1.0, grime=0.3, moss=0.0, cracks=0.1, noise_scale=6.0, ao=0.7)


def ground():
    return K.mat("stone_ground", GROUND, rough=1.0, grime=0.0, moss=0.0, cracks=0.25, noise_scale=1.5, ao=0.3)


def gravel():
    return K.mat("stone_gravel", "#6d7068", rough=1.0, grime=0.3, noise_scale=12.0, cracks=0.3)


def moss_mat():
    return K.mat("moss_pad", MOSS, rough=1.0, noise_scale=10.0)


def grass_mat(k=0):
    return K.mat(f"ruin_grass{k}", GRASS[k % 3], rough=0.85, grime=0.0, noise_scale=5.0, ao=0.2)


# ---------------------------------------------------------------- batches
class Batch:
    """Many boxes and faces in one mesh (one object, a few materials): what makes a ruin of thousands of blocks cheap to
    build and to draw. ``bev`` puts one bevel on all of it; ``collider`` makes it collision only (never drawn)."""

    def __init__(self, name, bev=0.0, collider=False):
        self.name, self.bev, self.collider = name, bev, collider
        self.bm = bmesh.new()
        self.mats = []
        self.count = 0

    def _mi(self, mat):
        if self.collider or mat is None:
            return 0
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def box(self, size, loc, rot=(0, 0, 0), mat=None, faces="btfrkl"):
        """A box size (x, y, z) centred at loc, turned by rot (degrees, XYZ Euler like kit.box). ``faces`` leaves out
        sides nobody sees (a coursed block bedded between others): b/t bottom and top, f/k -y and +y, l/r -x and +x."""
        mi = self._mi(mat)
        R = Euler([math.radians(a) for a in rot]).to_matrix()
        c = Vector(loc)
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        vs = [self.bm.verts.new(c + R @ Vector((sx * hx, sy * hy, sz * hz)))
              for sx, sy, sz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
        for key, f in zip("btfrkl", ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7))):
            if key in faces:
                self.bm.faces.new([vs[i] for i in f]).material_index = mi
        self.count += 1

    def wedge(self, c, yaw, r0, r1, a0, a1, t, mat=None):
        """A wedge stone of an arch: the ring sector r0..r1 between angles a0 and a1 (radians, 0 along the wall's +u,
        pi/2 straight up), t thick, centred on the springing point c, the arch's plane turned yaw degrees."""
        mi = self._mi(mat)
        R = Matrix.Rotation(math.radians(yaw), 3, "Z")
        c = Vector(c)
        pts = []
        for y in (-t / 2, t / 2):
            for r, a in ((r0, a0), (r1, a0), (r1, a1), (r0, a1)):
                pts.append(self.bm.verts.new(c + R @ Vector((r * math.cos(a), y, r * math.sin(a)))))
        for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            self.bm.faces.new([pts[i] for i in f]).material_index = mi
        self.count += 1

    def poly(self, pts, mat=None):
        """One face through pts (3D), wound as given."""
        self.bm.faces.new([self.bm.verts.new(p) for p in pts]).material_index = self._mi(mat)

    def prism(self, pts, z0, z1, mat=None):
        """An outline in XY (counter-clockwise) stood from z0 to z1."""
        mi = self._mi(mat)
        b = [self.bm.verts.new((x, y, z0)) for x, y in pts]
        t = [self.bm.verts.new((x, y, z1)) for x, y in pts]
        self.bm.faces.new(list(reversed(b))).material_index = mi
        self.bm.faces.new(t).material_index = mi
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((b[i], b[j], t[j], t[i])).material_index = mi

    def ramp(self, a, b, width, base=0.4):
        """A slab whose top runs straight from a to b, width across, base thick (a flight walked as a ramp)."""
        a, b = Vector(a), Vector(b)
        run = Vector((b.x - a.x, b.y - a.y, 0))
        u = Vector((-run.y, run.x, 0)).normalized() * width / 2
        top = [self.bm.verts.new(p) for p in (a - u, b - u, b + u, a + u)]
        bot = [self.bm.verts.new(v.co - Vector((0, 0, base))) for v in top]
        self.bm.faces.new(top)
        self.bm.faces.new(list(reversed(bot)))
        for i in range(4):
            j = (i + 1) % 4
            self.bm.faces.new((top[j], top[i], bot[i], bot[j]))

    def finish(self, P):
        if not self.bm.faces:
            self.bm.free()
            return None
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in (self.mats or [None]):
            me.materials.append(m)
        ob = K.link(bpy.data.objects.new(self.name, me))
        ob.parent = P
        if self.collider:
            return K.collider_only(ob)
        if self.bev:
            K.bevel(ob, self.bev, segs=2, angle=40)
        ob["collide"] = "never"
        return ob


class Site:
    """A ruin under construction: its root, a seeded random and the batches its pieces draw into. ``done`` turns the
    batches into objects under the root."""

    def __init__(self, name, seed=1, root=None):
        self.P = root if root is not None else K.link(bpy.data.objects.new(name, None))
        self.name = name
        self.rnd = random.Random(seed)
        self.blocks = Batch(f"{name}_blocks")                  # coursed blocks and cores: no bevel
        self.cut = Batch(f"{name}_cut", bev=0.05)              # loose, top and carved stones: bevelled
        self.fine = Batch(f"{name}_fine", bev=0.02)            # small stones: a finer bevel
        self.flat = Batch(f"{name}_flat")                      # flagstone tops, grass blades: faces only
        self.col = Batch(f"{name}_col", collider=True)         # what collides
        self.s, self.sd, self.fl, self.mo = wall_stone(), dark(), flag(), mortar()
        self.loose = self.cut                                  # a wall's loosened top blocks and plinth (a big site draws them plain)
        self.scatter = 1.0                                     # how thickly rubble heaps are strewn with blocks
        self.course, self.block = COURSE, (0.75, 1.35)         # a house wall's courses and block lengths

    def stone_or_dark(self, p=0.15):
        return self.sd if self.rnd.random() < p else self.s

    def done(self):
        for b in (self.blocks, self.cut, self.fine, self.flat, self.col):
            b.finish(self.P)
        return self.P


# ---------------------------------------------------------------- frames along a line
class Line:
    """A wall's frame: u runs from a to b along the wall, v across it (+v to the left looking from a to b)."""

    def __init__(self, a, b):
        self.a = Vector((a[0], a[1], 0))
        d = Vector((b[0] - a[0], b[1] - a[1], 0))
        self.L = d.length
        self.d = d.normalized()
        self.n = Vector((-self.d.y, self.d.x, 0))
        self.yaw = math.degrees(math.atan2(self.d.y, self.d.x))

    def at(self, u, v=0.0, z=0.0):
        p = self.a + self.d * u + self.n * v
        return (p.x, p.y, z)


# ---------------------------------------------------------------- height profiles (top of a wall along u)
def whole(h):
    return lambda u: h


def slope(L, h0, h1):
    return lambda u: h0 + (h1 - h0) * max(0.0, min(1.0, u / max(L, 1e-3)))


def broken(L, hi, lo, rnd, points=4, breach=0.0):
    """A ragged top between lo and hi, highest somewhere along it; breach > 0 cuts one gap down to that height."""
    us = [0.0] + sorted(rnd.uniform(0.1, 0.9) * L for _ in range(points - 2)) + [L]
    hs = [rnd.uniform(lo, hi) for _ in us]
    hs[rnd.randrange(len(hs))] = hi
    gap = None
    if breach:
        c = rnd.uniform(0.3, 0.7) * L
        gap = (c - rnd.uniform(0.8, 1.6), c + rnd.uniform(0.8, 1.6))

    def top(u):
        for i in range(len(us) - 1):
            if us[i] <= u <= us[i + 1]:
                f = (u - us[i]) / max(us[i + 1] - us[i], 1e-3)
                h = hs[i] + (hs[i + 1] - hs[i]) * f
                break
        else:
            h = hs[-1]
        if gap and gap[0] < u < gap[1]:
            h = min(h, breach)
        return h
    return top


def collapsed(L, rnd, hi=1.25):
    return broken(L, hi, 0.4, rnd, points=5)


# ---------------------------------------------------------------- walls
def _cut(parts, h0, h1):
    return [(p2, q2) for p, q in parts for p2, q2 in ((p, min(q, h0)), (max(p, h1), q)) if q2 - p2 > 0.12]


def wall(site, a, b, top, t=0.7, z0=0.0, holes=(), ch=COURSE, block=(0.75, 1.35), plinth=True, grass=True, ext=0.0,
         lintels=True, bond=0):
    """A coursed wall along a-b (its centreline, extended ext at both ends), standing on z0, its top at top(u)
    (absolute height; a number for a level top). holes are openings (u0, u1, z_bottom, z_top) in the wall's frame,
    from a: a door, a window, a gap; ``arch`` holes (u0, u1, z_bottom, z_spring, 'arch') get a round head of wedge
    stones. Returns the wall's Line."""
    rnd = site.rnd
    if not callable(top):
        top = whole(top)
    ln = Line(a, b)
    L = ln.L + 2 * ext
    u_of = lambda u: u - ext              # wall-run u (from the extended start) to the line's u
    T = lambda u: top(min(max(u_of(u), 0.0), ln.L))
    holes = [((h[0] + ext, h[1] + ext) + tuple(h[2:])) for h in holes]
    # each rectangular hole also cuts the band of its lintel; an arched one the ring of its head
    bands = []
    for h in holes:
        u0, u1, hz0, hz1 = h[:4]
        if len(h) > 4 and h[4] == "arch":
            r = (u1 - u0) / 2
            bands.append(("arch", (u0 + u1) / 2, hz1, r, r + 0.45))
        elif lintels and hz1 < T((u0 + u1) / 2) - 0.2:
            bands.append(("rect", u0 - 0.35, u1 + 0.35, hz1, hz1 + ch))
    z = z0
    if plinth:
        # a plinth course, a little proud of the wall and only as tall as a step
        x = 0.0
        while x < L - 0.05:
            Lb = min(rnd.uniform(1.0, 1.6), L - x)
            parts = [(x, x + Lb)]
            for h in holes:
                if h[2] <= z0 + 0.05:
                    parts = _cut(parts, h[0], h[1])
            for p, q in parts:
                if T((p + q) / 2) > z0 + 0.1:
                    site.loose.box((q - p - GAP, t + 0.12, 0.25), ln.at(u_of((p + q) / 2), 0, z0 + 0.125), (0, 0, ln.yaw), site.stone_or_dark(0.1),
                                   faces="btfrkl" if site.loose.bev else "tfrkl")
            x += Lb
        runs = _runs_of(holes, L, z0, z0 + 0.25, T, z0 + 0.1)
        for p, q in runs:
            site.col.box((q - p, t + 0.12, 0.25), ln.at(u_of((p + q) / 2), 0, z0 + 0.125), (0, 0, ln.yaw))
        z += 0.25
    k = 0
    while True:
        zt = z + ch
        if not any(T(u) >= zt - 0.01 for u in _samples(L)):
            break
        x = 0.0
        first = True
        course = []
        bedded = []
        while x < L - 0.05:
            Lb = rnd.uniform(*block) * (0.5 if (first and (k + bond) % 2) else 1.0)
            first = False
            Lb = min(Lb, L - x)
            if L - x - Lb < 0.3:
                Lb = L - x
            p0, q0 = x, x + Lb
            x += Lb
            mid = (p0 + q0) / 2
            if zt > T(mid) + 0.01:
                continue
            parts = [(p0, q0)]
            for h in holes:
                if zt > h[2] + 0.02 and z < h[3] - 0.02:
                    parts = _cut(parts, h[0], h[1])
            for bd in bands:
                if bd[0] == "rect":
                    _, u0, u1, hz0, hz1 = bd
                    if zt > hz0 + 0.02 and z < hz1 - 0.02:
                        parts = _cut(parts, u0, u1)
                else:
                    _, c, zs, r, R = bd
                    zm = z + ch / 2
                    if zm > zs - 0.02 and zm < zs + R:
                        w = math.sqrt(max(R * R - (zm - zs) ** 2, 0.0))
                        parts = _cut(parts, c - w, c + w)
            for p, q in parts:
                course.append((p, q))
                loose = zt + ch > T((p + q) / 2) + 0.01            # nothing above it: a top block, loosened a little
                m = site.stone_or_dark(0.14)
                dv = rnd.uniform(-0.015, 0.02)
                if loose:
                    site.loose.box((q - p - GAP, t + rnd.uniform(-0.04, 0.02), ch - GAP), ln.at(u_of((p + q) / 2), dv, z + ch / 2),
                                 (rnd.uniform(-1.5, 1.5), rnd.uniform(-2, 2), ln.yaw + rnd.uniform(-3, 3)), m)
                    if q - p > 0.7 and rnd.random() < 0.08:
                        moss(site, ln.at(u_of((p + q) / 2), rnd.uniform(-0.1, 0.1), zt), min(0.3, (q - p) * 0.3))
                else:
                    bedded.append((p, q, dv, m))
        runs = _merge(course)
        ends = {round(e, 3) for r in runs for e in r}
        for p, q, dv, m in bedded:
            # a block bedded in the wall shows only its faces; its ends at a run's end, its top and bottom where a
            # broken top or an opening leaves them open
            over = lambda u0, u1: u0 - 0.05 < q and p < u1 + 0.05
            f = "fk"
            if round(p, 3) in ends or round(q, 3) in ends:
                f += "lr"
            arch = any(bd[0] == "arch" and over(bd[1] - bd[4], bd[1] + bd[4]) and bd[2] - ch < zt and z < bd[2] + bd[4] + ch for bd in bands)
            if arch or any(over(h[0], h[1]) and abs(z - h[3]) < ch for h in holes):
                f += "b"
            if arch or any(over(h[0], h[1]) and abs(zt - h[2]) < ch for h in holes) or \
                    min(T(p + 0.02), T(q - 0.02), T((p + q) / 2)) < zt + ch + 0.01:
                f += "t"
            site.blocks.box((q - p - GAP, t + dv * 2, ch - GAP), ln.at(u_of((p + q) / 2), 0, z + ch / 2), (0, 0, ln.yaw), m, faces=f)
        # the mortar core behind each run of blocks, and the run's collider
        for p, q in runs:
            open_top = min(T(p + 0.02), T(q - 0.02), T((p + q) / 2)) < zt + ch + 0.01
            site.blocks.box((q - p - 0.02, t - 0.08, ch), ln.at(u_of((p + q) / 2), 0, z + ch / 2), (0, 0, ln.yaw), site.mo,
                            faces="fklrt" if open_top else "fklr")
            site.col.box((q - p, t, ch), ln.at(u_of((p + q) / 2), 0, z + ch / 2), (0, 0, ln.yaw))
        z = zt
        k += 1
        if k > 200:
            break
    # lintels and arched heads
    for bd in bands:
        if bd[0] == "rect":
            _, u0, u1, hz0, hz1 = bd
            if T((u0 + u1) / 2) >= hz1 - 0.05:
                site.cut.box((u1 - u0, t + 0.04, ch - GAP), ln.at(u_of((u0 + u1) / 2), 0, hz0 + ch / 2), (0, rnd.uniform(-0.6, 0.6), ln.yaw), site.sd)
                site.col.box((u1 - u0, t, ch), ln.at(u_of((u0 + u1) / 2), 0, hz0 + ch / 2), (0, 0, ln.yaw))
        else:
            _, c, zs, r, R = bd
            arch_ring(site, ln, u_of(c), zs, r, R - r, t + 0.06, T(c))
    if grass:
        grass_along(site, ln, -ext, ln.L + ext, t / 2 + 0.25, z0, holes=[(h[0] - ext, h[1] - ext) for h in holes if h[2] <= z0 + 0.05])
        grass_along(site, ln, -ext, ln.L + ext, -t / 2 - 0.25, z0, holes=[(h[0] - ext, h[1] - ext) for h in holes if h[2] <= z0 + 0.05])
    return ln


def _samples(L, step=0.25):
    n = max(2, int(L / step) + 1)
    return [L * i / (n - 1) for i in range(n)]


def _merge(parts, tol=0.06):
    out = []
    for p, q in sorted(parts):
        if out and p - out[-1][1] < tol:
            out[-1] = (out[-1][0], max(out[-1][1], q))
        else:
            out.append((p, q))
    return out


def _runs_of(holes, L, z0, z1, T, zmin):
    parts = [(0.0, L)]
    for h in holes:
        if z1 > h[2] + 0.02 and z0 < h[3] - 0.02:
            parts = _cut(parts, h[0], h[1])
    out = []
    for p, q in parts:
        # split where the wall is gone
        s = None
        for u in _samples(q - p, 0.5):
            ok = T(p + u) > zmin
            if ok and s is None:
                s = p + u
            if not ok and s is not None:
                out.append((s, p + u))
                s = None
        if s is not None:
            out.append((s, q))
    return [(p, q) for p, q in out if q - p > 0.2]


def arch_ring(site, ln, c, zs, r, ring, t, top, n=9):
    """The round head of an opening: n wedge stones from the springing line zs, the keystone proud. Stones above
    the wall's top have fallen."""
    for k in range(n):
        a0 = math.pi * k / n + 0.01
        a1 = math.pi * (k + 1) / n - 0.01
        am = (a0 + a1) / 2
        if zs + (r + ring) * math.sin(am) > top + 0.35:
            continue
        site.cut.wedge(ln.at(c, 0, zs), ln.yaw, r, r + ring + (0.08 if k == n // 2 else 0), a0, a1, t + (0.06 if k == n // 2 else 0),
                       site.s if k != n // 2 else site.sd)
    # a collider over the head so nothing walks through the ring where it stands
    if top > zs + r + ring - 0.1:
        site.col.box((2 * (r + ring), t, ring), ln.at(c, 0, zs + r + ring / 2), (0, 0, ln.yaw))


def door(u, z0, w=DOOR_W, h=DOOR_H, arch=False):
    """An opening for wall(): a doorway centred at u on the floor z0."""
    if arch:
        return (u - w / 2, u + w / 2, z0 - 0.3, z0 + h - w / 2, "arch")
    return (u - w / 2, u + w / 2, z0 - 0.3, z0 + h)


def window(u, z0, w=1.0, h=1.25, sill=1.0, arch=False):
    if arch:
        return (u - w / 2, u + w / 2, z0 + sill, z0 + sill + h - w / 2, "arch")
    return (u - w / 2, u + w / 2, z0 + sill, z0 + sill + h)


def sills(site, ln, holes, t, top):
    """A projecting sill under each window opening that still has wall under it."""
    for h in holes:
        if h[2] > 0.6 and top((h[0] + h[1]) / 2) > h[2] + 0.3:
            site.cut.box((h[1] - h[0] + 0.3, t + 0.14, 0.12), ln.at((h[0] + h[1]) / 2, 0, h[2] - 0.06), (0, 0, ln.yaw), site.s)


# ---------------------------------------------------------------- grass and moss
def grass(site, loc, rnd=None, n=None, h=None, shade=None):
    """A clump of grass blades (one triangle each, leaning out)."""
    rnd = rnd or site.rnd
    n = n or rnd.randint(4, 7)
    h = h or rnd.uniform(0.25, 0.55)
    m = grass_mat(shade if shade is not None else rnd.randrange(3))
    x, y, z = loc
    for _ in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        lean = rnd.uniform(0.1, 0.5)
        L = h * rnd.uniform(0.6, 1.1)
        bx, by = x + math.cos(a) * rnd.uniform(0, 0.12), y + math.sin(a) * rnd.uniform(0, 0.12)
        w = rnd.uniform(0.03, 0.06)
        px, py = -math.sin(a) * w, math.cos(a) * w
        tip = (bx + math.cos(a) * L * lean, by + math.sin(a) * L * lean, z + L)
        site.flat.poly([(bx - px, by - py, z - 0.02), (bx + px, by + py, z - 0.02), tip], m)


def grass_along(site, ln, u0, u1, v, z, holes=(), every=1.6):
    rnd = site.rnd
    u = u0 + rnd.uniform(0, every)
    while u < u1:
        if not any(h0 - 0.3 < u < h1 + 0.3 for h0, h1 in holes):
            grass(site, ln.at(u, v + rnd.uniform(-0.15, 0.15), z))
        u += rnd.uniform(0.5, 1.6) * every


def moss(site, loc, r, flat=0.3, stretch=1.3):
    """A cushion of moss on a top face (cheap: a squashed icosphere)."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
    ob = K.from_bm("moss", bm, moss_mat(), smooth=True)
    ob.scale = (stretch, 1.0, flat)
    ob.rotation_euler = (0, 0, site.rnd.uniform(0, 6.28))
    ob.location = loc
    ob.parent = site.P
    ob["collide"] = "never"
    return ob


def crystals(site, loc, n=5, h=1.4, light=True, energy=600):
    """A cluster of cyan probe crystals growing out of the stone, and its light."""
    from props_ruins_a import crystal  # noqa: PLC0415
    rnd = site.rnd
    for k in range(n):
        ob = crystal(site.P, "crystal", (loc[0] + rnd.uniform(-0.3, 0.3), loc[1] + rnd.uniform(-0.3, 0.3), loc[2] - 0.05), rnd.uniform(0, 360),
                     rnd.uniform(55, 88), h * rnd.uniform(0.45, 1.0), h * rnd.uniform(0.08, 0.13), glow(CYAN, 3))
        ob["collide"] = "never"
    if light:
        K.point("crystal_light", (loc[0], loc[1], loc[2] + h * 0.8), CYAN, energy, 0.4).parent = site.P


# ---------------------------------------------------------------- pillars, columns, arches
def pillar(site, loc, h, w=0.9, broken_at=None, cap=True, yaw=0.0):
    """A square pillar of stacked blocks on a plinth with a cap; broken_at stops it short with its top block tipped
    off beside it."""
    rnd = site.rnd
    x, y, z0 = loc
    site.cut.box((w + 0.3, w + 0.3, 0.3), (x, y, z0 + 0.15), (0, 0, yaw), site.s)
    z = z0 + 0.3
    stop = broken_at if broken_at else h - (0.45 if cap else 0)
    while z < stop - 0.2:
        bh = min(rnd.choice((0.75, 1.0, 1.0, 1.25)), stop - z)
        site.cut.box((w, w, bh - 0.02), (x + rnd.uniform(-0.03, 0.03), y + rnd.uniform(-0.03, 0.03), z + bh / 2), (0, 0, yaw + rnd.uniform(-4, 4)), site.stone_or_dark(0.12))
        z += bh
    if broken_at:
        a = rnd.uniform(0, 6.28)
        fallen(site, (x + math.cos(a) * (w + 0.6), y + math.sin(a) * (w + 0.6), z0), (w, w, 1.0), lean=rnd.uniform(60, 85), yaw=math.degrees(a))
    elif cap:
        site.cut.box((w + 0.3, w + 0.3, 0.45), (x, y, z + 0.225), (0, rnd.uniform(-2, 2), yaw + rnd.uniform(-3, 3)), site.s)
        z += 0.45
        moss(site, (x, y, z + 0.02), w * 0.3)
    site.col.box((w + 0.2, w + 0.2, z - z0), (x, y, (z + z0) / 2), (0, 0, yaw))
    grass(site, (x + w * 0.7, y - w * 0.6, z0))
    return z


def column(site, loc, h, r=0.45, broken_at=None, drums=None, base=True):
    """A round column: a square base, drums, a capital; broken_at stops it short with its upper drums lying beside it."""
    rnd = site.rnd
    x, y, z0 = loc
    z = z0
    if base:
        site.cut.box((2.4 * r, 2.4 * r, 0.3), (x, y, z0 + 0.15), (0, 0, 0), site.s)
        z += 0.3
    top = broken_at or (h - 0.5)
    n = drums or max(2, round((top - z) / 1.1))
    dh = (top - z) / n
    for k in range(n):
        rr = r * (1 - 0.06 * (z - z0) / h)
        ob = K.cyl("drum", rr, dh - 0.02, site.s if k % 3 else site.sd, loc=(x, y, z + dh / 2), rot=(0, 0, rnd.uniform(0, 30)), segs=12, bev=0.03, parent=site.P)
        ob["collide"] = "never"
        z += dh
    if broken_at:
        a = rnd.uniform(0, 6.28)
        for k in range(rnd.randint(1, 3)):
            d = 2 * r + 0.4 + k * (dh + 0.1)
            ob = K.cyl("drum_fallen", r * 0.95, dh * 0.9, site.s, loc=(x + math.cos(a) * d, y + math.sin(a) * d, z0 + r * 0.95),
                       rot=(90, 0, math.degrees(a) + 90 + rnd.uniform(-12, 12)), segs=12, bev=0.03, parent=site.P)
            ob["collide"] = "never"
            site.col.box((dh * 0.9, 2 * r * 0.9, 2 * r * 0.9), (x + math.cos(a) * d, y + math.sin(a) * d, z0 + r * 0.95), (0, 0, math.degrees(a)))
    else:
        site.cut.box((2.3 * r, 2.3 * r, 0.22), (x, y, z + 0.11), (0, 0, 0), site.sd)
        site.cut.box((2.6 * r, 2.6 * r, 0.28), (x, y, z + 0.36), (0, 0, rnd.uniform(-2, 2)), site.s)
        z += 0.5
    site.col.box((1.8 * r, 1.8 * r, z - z0), (x, y, (z + z0) / 2))
    return z


def fallen(site, loc, size, lean=0.0, yaw=0.0, mat=None, collide=True):
    """A block lying where it fell: its base on the ground at loc, tipped lean degrees over towards yaw."""
    x, y, z0 = loc
    sx, sy, sz = size
    # tipped over about its long axis: lying on a side face, a little buried
    if lean > 45:
        sx, sz = sz, sx
        lean -= 90
    zc = z0 + sz / 2 - 0.05
    site.cut.box((sx, sy, sz), (x, y, zc), (lean * 0.3, lean, yaw), mat or site.stone_or_dark(0.2))
    if collide and max(sx, sy, sz) >= 0.6:
        site.col.box((sx, sy, sz), (x, y, zc), (lean * 0.3, lean, yaw))


def arch(site, loc, span=3.0, spring=3.0, depth=1.0, ring=0.6, yaw=0.0, broken=False, pier=1.0):
    """A free-standing arch: two piers of blocks, a ring of wedge stones from the spring height, a keystone. Broken: the
    ring has fallen in on one side and its stones lie below."""
    rnd = site.rnd
    x0, y0, z0 = loc
    ln = Line((x0, y0), (x0 + math.cos(math.radians(yaw)), y0 + math.sin(math.radians(yaw))))
    for s in (-1, 1):
        u = s * (span / 2 + pier / 2)
        site.cut.box((pier + 0.3, depth + 0.3, 0.3), ln.at(u, 0, z0 + 0.15), (0, 0, yaw), site.s)
        z = z0 + 0.3
        while z < z0 + spring - 0.1:
            bh = min(rnd.choice((0.5, 0.75)), z0 + spring - z)
            site.cut.box((pier, depth, bh - 0.02), ln.at(u + rnd.uniform(-0.02, 0.02), 0, z + bh / 2), (0, 0, yaw + rnd.uniform(-2, 2)), site.stone_or_dark(0.12))
            z += bh
        site.cut.box((pier + 0.15, depth + 0.1, 0.18), ln.at(u - s * 0.05, 0, z0 + spring - 0.09), (0, 0, yaw), site.sd)
        site.col.box((pier, depth, spring), ln.at(u, 0, z0 + spring / 2), (0, 0, yaw))
    R = span / 2
    n = 9
    for k in range(n):
        if broken and k >= 6:
            continue
        a0 = math.pi * k / n + 0.012
        a1 = math.pi * (k + 1) / n - 0.012
        site.cut.wedge(ln.at(0, 0, z0 + spring), yaw, R, R + ring, a0, a1, depth, site.stone_or_dark(0.12))
    if broken:
        for k in range(3):
            fallen(site, ln.at(span / 2 + rnd.uniform(-1.0, 1.0), rnd.uniform(-1.6, 1.6), z0), (0.7, depth, ring), lean=rnd.uniform(10, 80), yaw=yaw + rnd.uniform(0, 90))
    else:
        site.cut.box((0.5, depth + 0.12, ring + 0.3), ln.at(0, 0, z0 + spring + R + ring / 2 - 0.05), (0, 2, yaw), site.s)
        moss(site, ln.at(0, 0, z0 + spring + R + ring + 0.12), 0.25)
        # the ring's crown collides as a box, so a player who climbs it stands on stone
        site.col.box((span * 0.7, depth, ring), ln.at(0, 0, z0 + spring + R + ring / 2), (0, 0, yaw))
    return ln


# ---------------------------------------------------------------- stairs, floors
def stair(site, a, b, width, rise=RISE, base=None, mat=None):
    """A flight of stone steps from its foot a to its top b (both on the walking surface), cut as one block that beds
    down base metres (default: to the foot's level, so it stands solid). Walked as a smooth ramp from half a step up
    at the foot to a lip past the top."""
    a, b = Vector(a), Vector(b)
    run = Vector((b.x - a.x, b.y - a.y, 0))
    L, H = run.length, b.z - a.z
    n = max(1, round(H / rise))
    r, h = L / n, H / n
    base = H if base is None else base
    prof = [(0.0, 0.0)]
    for i in range(n):
        prof += [(i * r, (i + 1) * h), ((i + 1) * r, (i + 1) * h)]
    # under the steps: down to the foot's level (solid), or a soffit base metres under the top running back to the foot
    prof += [(L, max(H - base, 0.0)), (0.0, -0.05)] if base < H else [(L, -0.05), (0.0, -0.05)]
    bm = bmesh.new()
    f = [bm.verts.new((x, -width / 2, z)) for x, z in prof]
    k = [bm.verts.new((x, width / 2, z)) for x, z in prof]
    bm.faces.new(f)
    bm.faces.new(list(reversed(k)))
    for i in range(len(prof)):
        j = (i + 1) % len(prof)
        bm.faces.new((f[j], f[i], k[i], k[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K.from_bm("stair", bm, mat or site.s)
    yaw = math.degrees(math.atan2(run.y, run.x))
    K.place(ob, tuple(a), (0, 0, yaw), site.P)
    ob["collide"] = "never"
    ang = math.degrees(math.atan2(H, L))
    d = run.normalized()
    if ang <= RAMP_MAX:
        site.col.ramp(a + Vector((0, 0, h / 2)), b, width, base=min(base, 0.6) + h / 2)
        site.col.ramp(b, b + d * 0.5, width, base=0.5)
    else:
        for i in range(n):
            p = a + d * (r * (i + 0.5))
            site.col.box((r, width, (i + 1) * h), (p.x, p.y, a.z + (i + 1) * h / 2), (0, 0, yaw))
    return ang


def floor(site, x0, x1, y0, y1, z, size=(1.0, 1.6), gone=0.05, lifted=0.04, keep=(), bed=0.3, grass_in_gaps=True,
          mat=None, collide=True, bed_mat=None, holes=(), thick=0.0):
    """A flagstone floor over x0..x1, y0..y1 with its top at z: rows of slabs over a bed (a darker layer that shows in
    the joints and where a flag has gone), some lifted at a tilt or cracked, some gone with grass in the hole. keep
    lists (x, y, r) circles kept clear of loose flags (where a crate or chest stands); holes lists circles with no flag
    at all (bare ground, for a floor flush with the ground whose markers stand on the ground). bed 0: flags only."""
    rnd = site.rnd
    m = mat or site.fl
    if bed:
        site.blocks.box((x1 - x0, y1 - y0, bed), ((x0 + x1) / 2, (y0 + y1) / 2, z - 0.025 - bed / 2), (0, 0, 0), bed_mat or site.mo)
    if collide and bed:
        site.col.box((x1 - x0, y1 - y0, bed), ((x0 + x1) / 2, (y0 + y1) / 2, z - bed / 2))
    y = y0
    while y < y1 - 0.1:
        rh = min(rnd.uniform(*size), y1 - y)
        if y1 - y - rh < 0.4:
            rh = y1 - y
        x = x0 + rnd.uniform(-0.4, 0)
        while x < x1 - 0.05:
            w = rnd.uniform(size[0], size[1] * 1.2)
            a, b = max(x, x0), min(x + w, x1)
            x += w
            if b - a < 0.15:
                continue
            cx, cy = (a + b) / 2, y + rh / 2
            if any(max(abs(cx - hx) - (b - a) / 2, 0) ** 2 + max(abs(cy - hy) - rh / 2, 0) ** 2 < (hr + 0.3) ** 2 for hx, hy, hr in holes):
                continue
            clear = not any(math.hypot(cx - kx, cy - ky) < kr + 0.8 for kx, ky, kr in keep)
            roll = rnd.random()
            if roll < gone and clear:
                if grass_in_gaps:
                    grass(site, (cx, cy, z - 0.02))
                continue
            if roll < gone + lifted and clear and b - a > 0.6:
                tilt = (rnd.uniform(-6, 6), rnd.uniform(-6, 6), rnd.uniform(-5, 5))
                site.fine.box((b - a - 0.04, rh - 0.04, 0.12), (cx, cy, z - 0.02), tilt, m)
                continue
            zz = z + rnd.uniform(-0.01, 0.005)
            j = 0.015
            if thick:
                # loose on the ground: slabs with a visible edge
                site.blocks.box((b - a - 2 * j, rh - 2 * j, thick), (cx, cy, zz - thick / 2), (0, 0, rnd.uniform(-1.5, 1.5)), m if rnd.random() > 0.12 else site.sd)
                continue
            site.flat.poly([(a + j, y + j, zz), (b - j, y + j, zz), (b - j, y + rh - j, zz), (a + j, y + rh - j, zz)], m if rnd.random() > 0.12 else site.sd)
        y += rh


def slab(site, x0, x1, y0, y1, z, t=0.4, ragged=None, below=None):
    """An upper floor of big stone slabs with its top at z, laid on corbels along the walls. ragged = (side, depth): the
    edge on that side ('x0', 'x1', 'y0', 'y1') has broken back up to depth metres in uneven steps."""
    rnd = site.rnd
    pieces = [(x0, x1, y0, y1)]
    if ragged:
        side, dep = ragged
        n = max(2, int((x1 - x0 if side in ("y0", "y1") else y1 - y0) / 1.25))
        pieces = []
        for i in range(n):
            cut = rnd.uniform(0, dep)
            if side in ("y0", "y1"):
                a = x0 + (x1 - x0) * i / n
                b = x0 + (x1 - x0) * (i + 1) / n
                pieces.append((a, b, y0 + (cut if side == "y0" else 0), y1 - (cut if side == "y1" else 0)))
            else:
                a = y0 + (y1 - y0) * i / n
                b = y0 + (y1 - y0) * (i + 1) / n
                pieces.append((x0 + (cut if side == "x0" else 0), x1 - (cut if side == "x1" else 0), a, b))
    for a, b, c, d in pieces:
        site.cut.box((b - a - 0.03, d - c - 0.03, t), ((a + b) / 2, (c + d) / 2, z - t / 2), (0, 0, 0), site.fl if rnd.random() > 0.15 else site.sd)
        site.col.box((b - a, d - c, t), ((a + b) / 2, (c + d) / 2, z - t / 2))
    if ragged and below is not None:
        side, dep = ragged
        for k in range(3):
            # bits of the broken floor lie below its edge
            if side in ("y0", "y1"):
                px, py = rnd.uniform(x0 + 0.8, x1 - 0.8), (y0 - 0.9 if side == "y0" else y1 + 0.9)
            else:
                px, py = (x0 - 0.9 if side == "x0" else x1 + 0.9), rnd.uniform(y0 + 0.8, y1 - 0.8)
            fallen(site, (px, py, below), (rnd.uniform(0.6, 1.1), rnd.uniform(0.5, 0.9), t), lean=rnd.uniform(5, 30), yaw=rnd.uniform(0, 180), collide=False)
    return pieces


def rubble(site, loc, rx, ry, h, n=None, walk=True, gravel_bed=True, yaw=0.0):
    """A heap of fallen blocks on a gravel bank rx by ry across and h high, level for its middle fifth and easing down:
    walk=True makes the bank collide as itself, a way up when it leans on a wall (steepest about 1.9 h / r)."""
    rnd = site.rnd
    x0, y0, z0 = loc
    if gravel_bed:
        segs, rings = 14, 5
        bm = bmesh.new()
        ph = [rnd.uniform(0, 6.28) for _ in range(3)]
        top = bm.verts.new((0, 0, h))
        prev = None
        for k in range(1, rings + 1):
            f = k / rings
            q = min(1.0, max(0.0, (f - 0.2) / 0.8))
            z = h * (1 - q * q * (3 - 2 * q)) - (0.06 if k == rings else 0)
            ring = []
            for i in range(segs):
                a = 2 * math.pi * i / segs
                wob = 1 + 0.06 * (math.sin(2 * a + ph[0]) + 0.6 * math.sin(3 * a + ph[1]) + 0.4 * math.sin(5 * a + ph[2]))
                ring.append(bm.verts.new((math.cos(a) * rx * f * wob, math.sin(a) * ry * f * wob, z)))
            for i in range(segs):
                j = (i + 1) % segs
                bm.faces.new((top, ring[i], ring[j]) if prev is None else (prev[i], ring[i], ring[j], prev[j]))
            prev = ring
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        ob = K.from_bm("rubble_bank", bm, gravel(), smooth=True)
        K.place(ob, (x0, y0, z0), (0, 0, yaw), site.P)
        if not walk:
            ob["collide"] = "never"
    n = n or int((rx * ry * 1.6 + 4) * site.scatter)
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    for k in range(n):
        r = math.sqrt(rnd.random()) * 0.92
        a = rnd.uniform(0, 2 * math.pi)
        lx, ly = math.cos(a) * r * rx, math.sin(a) * r * ry
        x, y = x0 + lx * c - ly * s, y0 + lx * s + ly * c
        hz = h * max(0.0, 1 - max(0.0, (r - 0.2) / 0.8) ** 2 * (3 - 2 * max(0.0, (r - 0.2) / 0.8)))
        sz = (rnd.uniform(0.4, 1.0), rnd.uniform(0.35, 0.6), rnd.choice((0.3, 0.4, 0.5)))
        (site.loose if max(sz) > 0.6 else site.fine).box(sz, (x, y, z0 + hz - sz[2] * 0.25), (rnd.uniform(-20, 20), rnd.uniform(-20, 20), rnd.uniform(0, 180)), site.stone_or_dark(0.2))
    for k in range(4):
        a = rnd.uniform(0, 2 * math.pi)
        grass(site, (x0 + math.cos(a) * rx * 0.9, y0 + math.sin(a) * ry * 0.9, z0))


def rail(site, a, b, h=0.9, t=0.35):
    """A low stone parapet standing on the line a-b (a flight's or a floor's edge), h high: a row of blocks following
    the line, colliding as a sloped slab."""
    a, b = Vector(a), Vector(b)
    d = b - a
    run = Vector((d.x, d.y, 0))
    yaw = math.degrees(math.atan2(run.y, run.x))
    pitch = -math.degrees(math.atan2(d.z, run.length))
    n = max(1, round(d.length / 1.1))
    for k in range(n):
        c = a + d * ((k + 0.5) / n)
        site.cut.box((d.length / n - 0.03, t, h), (c.x, c.y, c.z + h / 2), (0, pitch, yaw), site.stone_or_dark(0.1))
    site.col.ramp(a + Vector((0, 0, h)), b + Vector((0, 0, h)), t, base=h + 0.3)


# ---------------------------------------------------------------- stair inside a square
def spiral(site, cx, cy, half, z0, z1, width, flights=4, start=0, rails=True):
    """A square stair winding up inside a tower: flights along the inner faces of its walls between corner landings,
    turning counter-clockwise seen from above. The square inside the walls is cx +- half, cy +- half; start picks the
    corner the first flight leaves from (0 south-west, 1 south-east, 2 north-east, 3 north-west). The first flight is
    solid to the floor; the rest are soffits built into the walls, with a low parapet on the well side. Returns the
    flights as (corner from, corner to, z foot, z top) and the corners' landing centres."""
    m = half - width / 2
    corners = [(cx - m, cy - m), (cx + m, cy - m), (cx + m, cy + m), (cx - m, cy + m)]
    rise = (z1 - z0) / flights
    out = []
    centre = Vector((cx, cy, 0))
    for k in range(flights):
        c0, c1 = corners[(start + k) % 4], corners[(start + k + 1) % 4]
        za, zb = z0 + rise * k, z0 + rise * (k + 1)
        d = Vector((c1[0] - c0[0], c1[1] - c0[1], 0)).normalized()
        foot = Vector((c0[0], c0[1], za)) + d * (width / 2)
        top = Vector((c1[0], c1[1], zb)) - d * (width / 2)
        stair(site, foot, top, width - 0.05, base=(zb - z0) if k == 0 else 0.75)
        out.append((c0, c1, za, zb))
        if k < flights - 1:
            # the corner landing at the top of this flight, built into the walls
            site.cut.box((width, width, 0.5), (c1[0], c1[1], zb - 0.25), (0, 0, 0), site.s)
            site.col.box((width, width, 0.5), (c1[0], c1[1], zb - 0.25))
        if rails:
            nrm = Vector((-d.y, d.x, 0))
            if nrm.dot(centre - Vector((c0[0], c0[1], 0))) < 0:
                nrm = -nrm
            off = nrm * (width / 2 + 0.17)
            if k == 0:
                # the ground flight's parapet starts where it is a step up off the floor
                f = 2.0 / (top - foot).length
                rail(site, foot.lerp(top, f) + off, top + off, h=0.95)
            else:
                rail(site, foot + off, top + off, h=0.95)
            if k < flights - 1:
                # a post on the landing's corner over the well
                q = Vector((c1[0], c1[1], 0)) + (centre - Vector((c1[0], c1[1], 0))).normalized() * (width / 2 + 0.2) * 1.414
                site.cut.box((0.45, 0.45, 0.95), (q.x, q.y, zb + 0.475), (0, 0, 45), site.s)
                site.col.box((0.45, 0.45, 0.95), (q.x, q.y, zb + 0.475), (0, 0, 45))
    return out, corners


# ---------------------------------------------------------------- house and tower
def house(site, x0, y0, x1, y1, z0=0.3, storeys=1, t=0.7, doors=(("s", 0.5),), ruin="half", upper=None, keep=(),
          windows=True, interior="flags", seed=None, arch_doors=False, low=None):
    """A stone house over x0..x1, y0..y1 (its outer faces) with its floor at z0: four coursed walls with doors (side
    's', 'n', 'w' or 'e', and where along it from 0 at its west or south end to 1) and windows. ruin: 'whole' (every
    wall to the eaves, a little ragged), 'half' (one or two walls, or those in low, broken well down), 'shell' (stubs
    about the house). upper: ('y', ys) puts an upper floor over ys..the back wall with a stair up the west wall to its
    broken front edge, ('x', xs) one over xs..the east wall with a stair along the back wall; the walls then stand two
    storeys. keep: (x, y, r) spots the floor keeps clear of loose flags (a crate stands there)."""
    rnd = random.Random(seed) if seed is not None else site.rnd
    old = site.rnd
    site.rnd = rnd
    H = z0 + STOREY * storeys + 0.25
    walls = {"s": ((x0, y0 + t / 2), (x1, y0 + t / 2)), "n": ((x1, y1 - t / 2), (x0, y1 - t / 2)),
             "w": ((x0 + t / 2, y1 - t), (x0 + t / 2, y0 + t)), "e": ((x1 - t / 2, y0 + t), (x1 - t / 2, y1 - t))}
    if low is None:
        low = set(rnd.sample(["s", "n", "w", "e"], rnd.choice((1, 2)))) if ruin == "half" else set()
    stair_wall = None
    if upper and storeys > 1:
        stair_wall = "w" if upper[0] == "y" else "n"
        low = set(low) - {stair_wall}
    for side, (a, b) in walls.items():
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        if ruin == "shell" or side in low:
            top = broken(L, min(H, z0 + 2.75), z0 + 0.5, rnd, points=5, breach=z0 if L > 5 and rnd.random() < 0.4 else 0.0)
        elif ruin == "whole":
            top = broken(L, H, H - 1.0, rnd, points=4)
        else:
            top = broken(L, H, z0 + 1.5, rnd, points=4)
        if side == stair_wall:
            top = broken(L, H, z0 + STOREY + 1.0, rnd, points=3)
        holes = []
        for s_, u in doors:
            if s_ == side:
                uu = (1 - u) * L if side in ("n", "w") else u * L
                holes.append(door(uu, z0, arch=arch_doors))
        if windows:
            for f in range(storeys):
                zf = z0 + STOREY * f
                n = max(0, int((L - 1.5) / 3.0))
                for i in range(n):
                    uu = L * (i + 1) / (n + 1)
                    if any(abs(uu - (h[0] + h[1]) / 2) < 1.8 for h in holes):
                        continue
                    if f == 0 and side == stair_wall:
                        continue
                    holes.append(window(uu, zf, w=1.0, h=1.25, arch=(f == storeys - 1 and rnd.random() < 0.3)))
        ln = wall(site, a, b, top, t=t, z0=z0 - 0.3, holes=holes, bond=0 if side in ("s", "n") else 1, ch=site.course, block=site.block)
        sills(site, ln, holes, t, top)
    ix0, ix1, iy0, iy1 = x0 + t, x1 - t, y0 + t, y1 - t
    if interior == "flags":
        floor(site, ix0, ix1, iy0, iy1, z0, keep=keep, gone=0.08, lifted=0.05)
    else:
        site.blocks.box((ix1 - ix0, iy1 - iy0, 0.3), ((ix0 + ix1) / 2, (iy0 + iy1) / 2, z0 - 0.15), (0, 0, 0), ground())
        site.col.box((ix1 - ix0, iy1 - iy0, 0.3), ((ix0 + ix1) / 2, (iy0 + iy1) / 2, z0 - 0.15))
    if stair_wall:
        zu = z0 + STOREY
        run = 4.5
        if upper[0] == "y":
            ys = upper[1]
            slab(site, ix0, ix1, ys, iy1, zu, ragged=("y0", 0.6), below=z0)
            stair(site, (ix0 + 0.65, ys - run, z0), (ix0 + 0.65, ys + 0.02, zu), 1.25)
        else:
            xs = upper[1]
            slab(site, xs, ix1, iy0, iy1, zu, ragged=("x0", 0.6), below=z0)
            stair(site, (xs - run, iy1 - 0.65, z0), (xs + 0.02, iy1 - 0.65, zu), 1.25)
    site.rnd = old
    return (ix0, ix1, iy0, iy1)


def tower(site, cx, cy, w, z0, top_floor, t=1.0, width=1.5, start=0, door_side="s", parapet=2.0, broken_sides=(),
          spike=None, oculus=0.0, slits=True, big=False):
    """A square tower w across (outer) on its floor z0: a stair winds up inside (spiral) to a top floor at top_floor,
    behind a crenellated parapet. The door opens onto the first flight's foot. broken_sides lists sides ('s', 'e',
    'n', 'w') whose parapet has fallen to the floor; spike = (side, height) leaves a stretch of that wall standing that
    high. oculus opens a square of that size in the middle of the top floor. Returns the top floor's rectangle and the
    stairhead (the hole over the last flight)."""
    rnd = site.rnd
    hw = w / 2
    half = hw - t
    flights, corners = spiral(site, cx, cy, half, z0, top_floor, width, flights=4, start=start)
    zt = top_floor + parapet
    sides = {"s": ((cx - hw, cy - hw + t / 2), (cx + hw, cy - hw + t / 2)), "e": ((cx + hw - t / 2, cy - hw + t), (cx + hw - t / 2, cy + hw - t)),
             "n": ((cx + hw, cy + hw - t / 2), (cx - hw, cy + hw - t / 2)), "w": ((cx - hw + t / 2, cy + hw - t), (cx - hw + t / 2, cy - hw + t))}
    for side, (a, b) in sides.items():
        ln = Line(a, b)
        L = ln.L
        holes = []
        # the crenels: gaps a metre wide every two metres along the parapet, over a sill 0.9 m above the floor
        u = 1.0 if side in ("s", "n") else 1.0 - t
        while u < L - 1.2:
            if u > 0.4:
                holes.append((u, u + 1.0, top_floor + 0.9, zt + 3.0))
            u += 2.0
        if side == door_side:
            c = corners[start]
            uu = (Vector((c[0], c[1], 0)) - ln.a).dot(ln.d)
            holes.append(door(uu, z0, w=1.25, h=2.75, arch=True))
        if slits:
            # arrow slits half a metre wide, too narrow to fall through, at each flight's middle
            for k in range(4):
                zz = z0 + (top_floor - z0) * (k + 0.55) / 4
                if zz + 1.25 < top_floor - 1.0:
                    holes.append((L / 2 - 0.25, L / 2 + 0.25, zz, zz + 1.25))
        if side in broken_sides:
            top = broken(L, zt - 0.5, top_floor + 0.25, rnd, points=4)
        elif spike and spike[0] == side:
            sh = spike[1]
            c0 = spike[2] * L if len(spike) > 2 else rnd.uniform(0.2, 0.35) * L
            top = lambda u, c0=c0, sh=sh: max(zt + 0.25 * math.sin(u * 2.3), sh - abs(u - c0) * 1.4 - 0.6 * (abs(math.sin(u * 3.7))))
        else:
            top = broken(L, zt + 0.25, zt - 0.25, rnd, points=3)
        wall(site, a, b, top, t=t, z0=z0 - 0.3, holes=holes, bond=0 if side in ("s", "n") else 1, ch=0.75 if big else COURSE,
             block=(1.0, 1.75) if big else (0.75, 1.35), lintels=False)
        ext = 0.0 if side in ("s", "n") else t + 0.05
        # string courses: a band of dark stone round the outside every few metres
        for zb in [z0 + 4.5 * k for k in range(1, int((top_floor - z0 - 1.5) / 4.5) + 1)]:
            site.blocks.box((L + 2 * ext, 0.12, 0.25), ln.at(L / 2, -t / 2 - 0.05, zb), (0, 0, ln.yaw), site.sd)
        # the parapet's corbel table: corbels every metre under a projecting course, just below the top floor
        zc = top_floor - 0.35
        site.cut.box((L + 2 * ext + (0.7 if side in ("s", "n") else 0), 0.45, 0.35), ln.at(L / 2, -t / 2 - 0.12, zc), (0, 0, ln.yaw), site.sd)
        u = 0.5 - ext
        while u < L + ext - 0.3:
            site.blocks.box((0.35, 0.4, 0.5), ln.at(u, -t / 2 - 0.15, zc - 0.45), (0, 0, ln.yaw), site.s)
            u += 1.0
    # the top floor, open over the last flight (the stairhead)
    c0, c1, za, zb = flights[-1]
    x_lo, x_hi, y_lo, y_hi = cx - half, cx + half, cy - half, cy + half
    if abs(c0[0] - c1[0]) < 1e-3:
        hx = (x_lo, x_lo + width) if c0[0] < cx else (x_hi - width, x_hi)
        hy = (min(c0[1], c1[1]) + width / 2, max(c0[1], c1[1]) - width / 2)
        hole = (hx[0], hx[1], hy[0], hy[1])
    else:
        hy = (y_lo, y_lo + width) if c0[1] < cy else (y_hi - width, y_hi)
        hx = (min(c0[0], c1[0]) + width / 2, max(c0[0], c1[0]) - width / 2)
        hole = (hx[0], hx[1], hy[0], hy[1])
    tz = 0.5
    cut = [hole] + ([(cx - oculus / 2, cx + oculus / 2, cy - oculus / 2, cy + oculus / 2)] if oculus else [])
    for a0, a1, b0, b1 in _rect_minus(x_lo, x_hi, y_lo, y_hi, cut):
        site.blocks.box((a1 - a0, b1 - b0, tz), ((a0 + a1) / 2, (b0 + b1) / 2, top_floor - tz / 2 - 0.01), (0, 0, 0), site.mo)
        site.col.box((a1 - a0, b1 - b0, tz), ((a0 + a1) / 2, (b0 + b1) / 2, top_floor - tz / 2))
        _flag_top(site, a0, a1, b0, b1, top_floor)
    # a parapet along the stairhead's well side and across its far end (the flight comes up at the near end)
    h0, h1, k0, k1 = hole
    if abs(c0[0] - c1[0]) < 1e-3:
        xr = h1 + 0.18 if c0[0] < cx else h0 - 0.18
        rail(site, (xr, k0 - 0.0, top_floor), (xr, k1, top_floor))
        yf = k1 + 0.18 if c1[1] < c0[1] else k0 - 0.18
        rail(site, (h0, yf, top_floor), (h1 + (0.35 if c0[0] < cx else 0), yf, top_floor))
    else:
        yr = k1 + 0.18 if c0[1] < cy else k0 - 0.18
        rail(site, (h0, yr, top_floor), (h1, yr, top_floor))
        xf = h1 + 0.18 if c1[0] < c0[0] else h0 - 0.18
        rail(site, (xf, k0, top_floor), (xf, k1, top_floor))
    if oculus:
        o = oculus / 2 + 0.18
        pts = [(cx - o, cy - o), (cx + o, cy - o), (cx + o, cy + o), (cx - o, cy + o)]
        for i in range(4):
            p, q = pts[i], pts[(i + 1) % 4]
            rail(site, (p[0], p[1], top_floor), (q[0], q[1], top_floor), h=0.8)
    floor(site, x_lo, x_hi, y_lo, y_hi, z0, gone=0.04, lifted=0.03)
    return (x_lo, x_hi, y_lo, y_hi), hole


def _flag_top(site, a0, a1, b0, b1, z, size=1.2):
    """Faces only: the joints of a floor of big stones on a slab's top."""
    rnd = site.rnd
    y = b0
    while y < b1 - 0.1:
        rh = min(rnd.uniform(size * 0.8, size * 1.3), b1 - y)
        x = a0
        while x < a1 - 0.1:
            w = min(rnd.uniform(size * 0.8, size * 1.5), a1 - x)
            j = 0.02
            site.flat.poly([(x + j, y + j, z), (x + w - j, y + j, z), (x + w - j, y + rh - j, z), (x + j, y + rh - j, z)],
                           site.fl if rnd.random() > 0.12 else site.sd)
            x += w
        y += rh


def _rect_minus(x0, x1, y0, y1, holes):
    """The rectangle less some rectangular holes, as rectangles (columns split at every hole edge)."""
    xs = sorted({x0, x1} | {v for h in holes for v in (h[0], h[1]) if x0 < v < x1})
    out = []
    for a, b in zip(xs[:-1], xs[1:]):
        m = (a + b) / 2
        spans = [(y0, y1)]
        for h in holes:
            if h[0] < m < h[1]:
                spans = [(p2, q2) for p, q in spans for p2, q2 in ((p, min(q, h[2])), (max(p, h[3]), q)) if q2 - p2 > 1e-3]
        out += [(a, b, p, q) for p, q in spans]
    return out
