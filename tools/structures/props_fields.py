"""Farmland props for the scenes round farms (docs/world-generation.md, Scenes): the crop fields and orchards the
settlers who went back to the land work, in the Settler Patchwork family of docs/art-direction.md.

Fields are 8 x 8 m patches a scene tiles side by side: bare furrows, wheat, corn, cabbages, sunflowers, potatoes,
carrots, a weedy fallow, a stubble field with its straw rows and a field gone wild. Their rows run along x and
their troughs fall on the patch edges, so two patches meet without a seam. Orchards: an apple, a pear and a plum tree,
a dead pruned tree, a staked sapling, a tripod picking ladder, a stack of fruit crates and windfalls in the grass.
Field edges: an 8 m irrigation ditch (tiles along x) and its sluice gate, a marker post, a hand cart of crops, an 8 m
windbreak hedge (tiles along x) and a pair of gate posts.

Crops are cheap on purpose: each field's plants are a few merged meshes (row masses with ragged tops, cards and small
spikes), not hundreds of objects, so a patch stays under about 4000 triangles.

Each builder returns an empty with the prop under it, its origin on the ground at the prop's centre (Blender Z up,
front towards -Y). They are world objects, so their models carry no collider: the object's data gives one.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import kit as K
from families import M, BONE, MUSTARD, GRAPHITE, BRICK, RUST, ZINC, CONCRETE
from props import _root, wood, plank, bolt, STRAW, STRAW_D
from props_settler_a import rod, beam, lump, sack, tin, rope_mat, SACK
from props_stone import bark, heart, rock, WATER
from props_nature import tube, leaves, leaf_cluster, foliage, leaf, bez, GRASS
from props_farm import _grp, _iron, crate, spoked_wheel, MUD_D

TILTH, TILTH_D, DRY = "#5e4330", "#3f2d1f", "#7a6448"          # turned soil, its damp troughs, sun-dried soil
WHEAT, WHEAT_D, EAR = "#b59c40", "#8a7a44", "#9a8030"
STUBBLE = "#bfa76c"
CORN_L, CORN_D, CORN_S, TASSEL, HUSK = "#6f9440", "#567a32", "#8c8f4a", "#c2ac6a", "#a9b170"
CABBAGE, CABBAGE_D = "#b2c78e", "#6a8f5a"
PETAL, SEED_HEAD, SUN_LEAF = MUSTARD, "#4a3322", "#5f8a38"
POT_LEAF, POT_FLOWER = "#4f7b33", "#e6e1ec"
CARROT_TOP = "#6f9f42"
WEED, WEED_D, THISTLE = "#7a9a44", "#5a7a36", "#8a5d8a"
APPLE, APPLE_D, PEAR, PLUM, ROTTEN = "#b8443a", "#8f3a30", "#b9c26e", "#55355a", "#5a4430"
LIME = "#e4dfd2"                 # whitewash on orchard trunks
DEAD = "#8a8278"                 # bark gone grey
REED = "#7f9444"


# ---------------------------------------------------------------- helpers
class Mesh:
    """Many simple pieces in one mesh with a material per face, so a field of crops costs a few objects."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, mat):
        for i, m in enumerate(self.mats):
            if m is mat:
                return i
        self.mats.append(mat)
        return len(self.mats) - 1

    def face(self, vs, mat):
        f = self.bm.faces.new(vs)
        f.material_index = self.mi(mat)
        return f

    def v(self, p):
        return self.bm.verts.new(p)

    def build(self, P, name, smooth=True, sharp=45):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        if smooth:
            for p in me.polygons:
                p.use_smooth = True
            me.set_sharp_from_angle(angle=math.radians(sharp))
        return K.place(K.link(bpy.data.objects.new(name, me)), parent=P)

    def row(self, y, x0, x1, n, prof, mats, top=None, width=None, sway=None, caps=True):
        """A crop row along x from x0 to x1 in n steps: prof is (z, half width) from the bottom up, mats one per band
        and one for the top; top(x) lifts the top (the lift fades towards the bottom), width(x) scales it and sway(x)
        leans it across."""
        m = len(prof)
        rings = []
        for i in range(n + 1):
            x = x0 + (x1 - x0) * i / n
            dz = top(x) if top else 0.0
            s = width(x) if width else 1.0
            dy = sway(x) if sway else 0.0
            L, R = [], []
            for k, (z, hw) in enumerate(prof):
                f = k / (m - 1)
                L.append(self.v((x, y + dy * f + hw * s, z + dz * f)))
                R.append(self.v((x, y + dy * f - hw * s, z + dz * f)))
            rings.append((L, R))
        for i in range(n):
            (L0, R0), (L1, R1) = rings[i], rings[i + 1]
            for k in range(m - 1):
                self.face((L0[k], L0[k + 1], L1[k + 1], L1[k]), mats[k])
                self.face((R0[k], R1[k], R1[k + 1], R0[k + 1]), mats[k])
            self.face((L0[-1], R0[-1], R1[-1], L1[-1]), mats[-1])
        if caps:
            L, R = rings[0]
            self.face(list(reversed(L + R[::-1])), mats[0])
            L, R = rings[-1]
            self.face(L + R[::-1], mats[0])

    def spike(self, base, d, h, r, mat, n=3, rnd=None):
        """A small pyramid from base along d: an ear of wheat, a tassel, a flower, a cut stalk."""
        b, d = Vector(base), Vector(d).normalized()
        s = d.orthogonal().normalized()
        t = d.cross(s)
        a0 = rnd.uniform(0, 6.28) if rnd else 0.0
        ring = [self.v(b + (s * math.cos(a0 + 2 * math.pi * k / n) + t * math.sin(a0 + 2 * math.pi * k / n)) * r) for k in range(n)]
        tip = self.v(b + d * h)
        fs = [self.face(ring[::-1], mat)] + [self.face((ring[k], ring[(k + 1) % n], tip), mat) for k in range(n)]
        bmesh.ops.recalc_face_normals(self.bm, faces=fs)

    def ball(self, c, r, mat, scale=(1, 1, 1), u=6, v=4):
        mx = Matrix.Translation(c) @ Matrix.Diagonal((*scale, 1))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=r, matrix=mx)
        for f in {f for vt in res["verts"] for f in vt.link_faces}:
            f.material_index = self.mi(mat)

    def turn(self, prof, mats, segs=6, mx=None, jitter=None, rnd=None):
        """A solid of revolution: (r, z) from bottom to top about local z, r 0 for a point; mats one per band (or one);
        mx places it; jitter scales each ring's radii unevenly (lobed leaves)."""
        mx = mx or Matrix()
        if not isinstance(mats, (list, tuple)):
            mats = [mats] * (len(prof) - 1)
        rings = []
        for k, (r, z) in enumerate(prof):
            if r < 1e-5:
                rings.append([self.v(mx @ Vector((0, 0, z)))])
                continue
            ring = []
            for j in range(segs):
                a = 2 * math.pi * j / segs
                rr = r * (1 + (jitter[k] * rnd.uniform(-1, 1) if jitter and rnd else 0) + (jitter[k] * (1 if j % 2 else -1) if jitter and not rnd else 0))
                ring.append(self.v(mx @ Vector((rr * math.cos(a), rr * math.sin(a), z))))
            rings.append(ring)
        fs = []
        for k, (a, b) in enumerate(zip(rings, rings[1:])):
            mat = mats[min(k, len(mats) - 1)]
            for j in range(segs):
                j2 = (j + 1) % segs
                if len(a) == 1 and len(b) > 1:
                    fs.append(self.face((a[0], b[j2], b[j]), mat))
                elif len(b) == 1 and len(a) > 1:
                    fs.append(self.face((a[j], a[j2], b[0]), mat))
                elif len(a) > 1:
                    fs.append(self.face((a[j], a[j2], b[j2], b[j]), mat))
        if len(rings[0]) > 1:
            fs.append(self.face(rings[0][::-1], mats[0]))
        if len(rings[-1]) > 1:
            fs.append(self.face(rings[-1], mats[-1]))
        bmesh.ops.recalc_face_normals(self.bm, faces=fs)

    def card(self, pts, w, mat, t=0.006, taper=1.6):
        """A strap leaf along pts, w wide at its base narrowing to its tip, faced both sides."""
        pts = [Vector(p) for p in pts]
        n = len(pts)
        rows = []
        for i in range(n):
            T = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            S = T.cross(Vector((0, 0, 1)))
            S = S.normalized() if S.length > 1e-4 else Vector((1, 0, 0))
            N = S.cross(T)
            ww = max(w * (1 - (i / (n - 1)) ** taper), 0.004) / 2
            rows.append(([self.v(pts[i] + S * s * ww + N * t / 2) for s in (1, -1)], [self.v(pts[i] + S * s * ww - N * t / 2) for s in (1, -1)], N))
        for i in range(n - 1):
            (a, a2, N), (b, b2, _) = rows[i], rows[i + 1]
            for q, want in (((a[0], a[1], b[1], b[0]), N), ((a2[0], b2[0], b2[1], a2[1]), -N)):
                f = self.face(q, mat)
                f.normal_update()
                if f.normal.dot(want) < 0:
                    f.normal_flip()

    def leaf(self, base, yaw, pitch, L, W, mat, t=0.005):
        """A pointed leaf (a diamond) from base, faced both sides."""
        b = Vector(base)
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.cos(y) * math.cos(p), math.sin(y) * math.cos(p), math.sin(p)))
        s = Vector((-math.sin(y), math.cos(y), 0))
        n = s.cross(d) if s.cross(d).z >= 0 else d.cross(s)
        ring = [b, b + d * L * 0.45 + s * W * 0.5, b + d * L, b + d * L * 0.45 - s * W * 0.5]
        for o in (1, -1):
            f = self.face([self.v(q + n * o * t / 2) for q in ring], mat)
            f.normal_update()
            if f.normal.dot(n * o) < 0:
                f.normal_flip()

    def tuft(self, loc, rnd, mat, n=6, h=0.35, w=0.035, spread=0.1):
        """A tuft of grass or weed blades leaning out from loc."""
        for _ in range(n):
            yaw = rnd.uniform(0, 2 * math.pi)
            lean = rnd.uniform(0.15, 0.6)
            L = h * rnd.uniform(0.6, 1.1)
            b = (loc[0] + math.cos(yaw) * spread * rnd.random(), loc[1] + math.sin(yaw) * spread * rnd.random(), loc[2] - 0.02)
            d = (math.cos(yaw), math.sin(yaw))
            tip = (b[0] + d[0] * L * lean * 1.3, b[1] + d[1] * L * lean * 1.3, b[2] + L * (1 - lean * 0.5))
            c = (b[0] + d[0] * L * lean * 0.2, b[1] + d[1] * L * lean * 0.2, b[2] + L * 0.75)
            self.card(bez(b, c, tip, 3), w, mat)


def mat_of(name, color, rough=0.9, **kw):
    return K.mat(name, color, rough=rough, **kw)


def _per(x, k, ph):
    """A wave that repeats every 8 m (k whole waves), so a patch's ends match its neighbour's."""
    return math.sin(2 * math.pi * k * x / 8.0 + ph)


def wobble(rnd, amp, ks=(2, 5, 11)):
    """A height or width wobble along x that tiles every 8 m."""
    ph = [rnd.uniform(0, 6.28) for _ in ks]
    ws = [1.0, 0.6, 0.35][:len(ks)]
    tot = sum(ws)
    return lambda x: amp * sum(w * _per(x, k, p) for w, k, p in zip(ws, ks, ph)) / tot


def soil(P, rnd, pitch, ridge, trough=0.03, ny_per=4, nx=8, bump=0.018, light=None, dark=None, size=8.0):
    """The ground of a field patch, size x size m: ridges along x every pitch metres with their troughs on the patch
    edges, the troughs damp and dark, an uneven tilth that repeats every 8 m, and a skirt down into the ground."""
    light = light or mat_of("tilth", TILTH, rough=1.0, noise_scale=3.0, cracks=0.15)
    dark = dark or mat_of("tilth_d", TILTH_D, rough=1.0, noise_scale=4.0)
    h = size / 2
    rows = round(size / pitch)
    ny = rows * ny_per
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]

    def z(x, y):
        u = (y + h) / pitch
        base = trough + ridge * (0.5 - 0.5 * math.cos(2 * math.pi * u))
        n = bump * (math.sin(2 * math.pi * 2 * x / size + ph[0]) * math.sin(2 * math.pi * 3 * y / size + ph[1])
                    + 0.6 * math.sin(2 * math.pi * 5 * x / size + ph[2]) * math.cos(2 * math.pi * 2 * y / size + ph[3]))
        return base + n

    S = Mesh()
    G = [[S.v((-h + size * i / nx, -h + size * j / ny, z(-h + size * i / nx, -h + size * j / ny))) for j in range(ny + 1)] for i in range(nx + 1)]
    for i in range(nx):
        for j in range(ny):
            q = (G[i][j], G[i + 1][j], G[i + 1][j + 1], G[i][j + 1])
            zz = sum(v.co.z for v in q) / 4
            S.face(q, dark if zz < trough + ridge * 0.3 else light)
    lo = -0.45                  # deep enough to hide the gap where a patch stands on gently sloping ground
    B = {}

    def under(v):
        if v not in B:
            B[v] = S.v((v.co.x, v.co.y, lo))
        return B[v]

    for i in range(nx):
        a, b = G[i][0], G[i + 1][0]
        S.face((a, under(a), under(b), b), dark)
        a, b = G[i][ny], G[i + 1][ny]
        S.face((a, b, under(b), under(a)), dark)
    for j in range(ny):
        a, b = G[0][j], G[0][j + 1]
        S.face((a, b, under(b), under(a)), dark)
        a, b = G[nx][j], G[nx][j + 1]
        S.face((a, under(a), under(b), b), dark)
    S.build(P, "soil", sharp=60)
    return z


def clods(P, rnd, zf, n, r=0.07, mat=None, half=3.8):
    mat = mat or mat_of("clod", TILTH, rough=1.0, noise_scale=6.0)
    for k in range(n):
        x, y = rnd.uniform(-half, half), rnd.uniform(-half, half)
        s = r * rnd.uniform(0.6, 1.4)
        lump(P, f"clod{k}", s, (x, y, zf(x, y) + s * 0.2), (1.3, 1.0, 0.7), mat, rnd, jitter=0.2, rot=(0, 0, rnd.uniform(0, 180)), u=6, v=4, flat=0.4)


def stones(P, rnd, zf, n, half=3.7):
    for k in range(n):
        x, y = rnd.uniform(-half, half), rnd.uniform(-half, half)
        rock(P, f"stone{k}", rnd.uniform(0.07, 0.14), 0.07, (x, y, zf(x, y) + 0.02), rnd)


def row_ys(pitch, size=8.0):
    return [-size / 2 + pitch * (k + 0.5) for k in range(round(size / pitch))]


# ================================================================== field patches
def field_furrows(seed=41):
    """An 8 x 8 m patch of freshly turned soil: ten ridges along x, damp dark troughs, clods and a few field stones."""
    rnd = random.Random(seed)
    P = _root("furrows")
    zf = soil(P, rnd, 0.8, 0.22, ny_per=4, nx=10, bump=0.025)
    clods(P, rnd, zf, 26)
    stones(P, rnd, zf, 7)
    return P


def field_wheat(seed=42):
    """An 8 x 8 m patch of ripe wheat a metre tall in ten rows: pale stalks, a golden band of ears with a ragged top and
    ears standing out of it, dark soil between the rows."""
    rnd = random.Random(seed)
    P = _root("wheat")
    zf = soil(P, rnd, 0.8, 0.1, ny_per=2, nx=8)
    stalk = mat_of("wheat_stalk", WHEAT_D, noise_scale=14.0, grime=0.2)
    gold = mat_of("wheat", WHEAT, noise_scale=9.0)
    ear = mat_of("wheat_ear", EAR, noise_scale=20.0)
    C = Mesh()
    for k, y in enumerate(row_ys(0.8)):
        top = wobble(rnd, 0.08, (3, 7, 13))
        wid = wobble(rnd, 0.12, (2, 5))
        sw = wobble(rnd, 0.05, (2, 3))
        jag = wobble(rnd, 0.05, (19, 29))
        C.row(y, -4, 4, 12, [(0.06, 0.17), (0.5, 0.28), (0.8, 0.31), (0.95, 0.24)], [stalk, gold, ear, ear],
              top=lambda x, t=top, j=jag: t(x) + j(x), width=lambda x, w=wid: 1 + w(x), sway=sw)
        for e in range(38):
            x = -3.88 + 7.76 * (e + rnd.random()) / 38
            yy = y + sw(x) + rnd.uniform(-0.2, 0.2)
            zt = 0.86 + top(x) + rnd.uniform(-0.03, 0.04)
            C.spike((x, yy, zt), (rnd.uniform(-0.35, 0.35), rnd.uniform(-0.5, 0.25), 1), rnd.uniform(0.2, 0.28), 0.036, ear, rnd=rnd)
    C.build(P, "wheat", sharp=25)
    return P


def field_corn(seed=43):
    """An 8 x 8 m patch of corn 2.2 m tall in eight rows: a leafy band of lower leaves, and out of it the stalks, each
    with long leaves arching out both ways, a husked cob and a pale tassel on top."""
    rnd = random.Random(seed)
    P = _root("corn")
    zf = soil(P, rnd, 1.0, 0.14, ny_per=2, nx=8)
    stalk = mat_of("corn_stalk", CORN_S, noise_scale=12.0, grime=0.25)
    dark = mat_of("corn_d", CORN_D, noise_scale=6.0)
    light = mat_of("corn_l", CORN_L, noise_scale=6.0)
    tassel = mat_of("tassel", TASSEL, noise_scale=15.0)
    husk = mat_of("husk", HUSK, noise_scale=10.0)
    C = Mesh()
    for k, y in enumerate(row_ys(1.0)):
        top = wobble(rnd, 0.15, (3, 7, 11))
        wid = wobble(rnd, 0.2, (3, 7))
        jag = wobble(rnd, 0.12, (17, 23))
        C.row(y, -4, 4, 10, [(0.05, 0.06), (0.35, 0.13), (0.9, 0.27), (1.3, 0.15)], [stalk, dark, light, light],
              top=lambda x, t=top, j=jag: t(x) + j(x), width=lambda x, w=wid: 1 + w(x))
        for i in range(8):
            x = -4 + 8 / 8 * (i + 0.5) + rnd.uniform(-0.15, 0.15)
            yy = y + rnd.uniform(-0.06, 0.06)
            h = rnd.uniform(2.0, 2.25)
            C.turn([(0.03, 0.4), (0.016, h)], stalk, segs=3, mx=Matrix.Translation((x, yy, 0)))
            C.spike((x, yy, h - 0.02), (rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 1), rnd.uniform(0.25, 0.35), 0.035, tassel, rnd=rnd)
            # leaves in the plane of the row's cross-section, alternating sides, arching out and drooping
            yaw = math.radians(90 + rnd.uniform(-35, 35))
            for j, z0 in enumerate((1.15, 1.5, 1.82)):
                s = 1 if (j + i) % 2 else -1
                d = Vector((math.cos(yaw) * s, math.sin(yaw) * s, 0))
                L = rnd.uniform(0.6, 0.8) * (1 - 0.18 * j)
                b = Vector((x, yy, z0))
                C.card([tuple(b), tuple(b + d * L * 0.45 + Vector((0, 0, L * 0.35))), tuple(b + d * L + Vector((0, 0, -L * 0.1)))], 0.1, light if j % 2 else dark)
            if i % 2 == 0:
                s = 1 if i % 4 else -1
                C.spike((x, yy + s * 0.03, 1.0), (0.1, s * 0.5, 1), 0.24, 0.045, husk, n=5, rnd=rnd)
    C.build(P, "corn", sharp=30)
    return P


def _cabbage(C, loc, rnd, outer, head, s=1.0):
    mx = Matrix.Translation(loc) @ Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Z") @ Matrix.Diagonal((s, s, s, 1))
    C.turn([(0.12, 0.0), (0.3, 0.09), (0.17, 0.13), (0.19, 0.22), (0.0, 0.29)], [outer, outer, head, head], segs=5,
           mx=mx, jitter=[0.1, 0.22, 0.1, 0.06, 0], rnd=rnd)


def field_cabbage(seed=44):
    """An 8 x 8 m patch of cabbages: eight low ridges, eleven heads to a row, each a pale heart in a ruff of dark outer
    leaves, a couple of gaps where one was cut or eaten."""
    rnd = random.Random(seed)
    P = _root("cabbage")
    zf = soil(P, rnd, 1.0, 0.12, ny_per=3, nx=8)
    outer = mat_of("cabbage_d", CABBAGE_D, rough=0.8, noise_scale=7.0)
    head = mat_of("cabbage", CABBAGE, rough=0.7, noise_scale=6.0)
    C = Mesh()
    gaps = {(1, 4), (5, 8), (6, 2)}
    for r, y in enumerate(row_ys(1.0)):
        for i in range(11):
            if (r, i) in gaps:
                continue
            x = -4 + 8 / 11 * (i + 0.5) + rnd.uniform(-0.06, 0.06)
            yy = y + rnd.uniform(-0.05, 0.05)
            _cabbage(C, (x, yy, zf(x, yy) - 0.02), rnd, outer, head, rnd.uniform(0.85, 1.12))
    C.build(P, "cabbages", sharp=60)
    clods(P, rnd, zf, 8)
    return P


def field_sunflower(seed=45):
    """An 8 x 8 m patch of sunflowers two metres tall: eight rows of plants, each a rough stalk with big drooping leaves
    over a low leafy band, and the heads on top all turned the same way (to -y) and nodding a little."""
    rnd = random.Random(seed)
    P = _root("sunflower")
    zf = soil(P, rnd, 1.0, 0.12, ny_per=3, nx=8)
    stalk = mat_of("sun_stalk", CORN_S, noise_scale=10.0, grime=0.2)
    lf = mat_of("sun_leaf", SUN_LEAF, noise_scale=6.0)
    lf_d = mat_of("sun_leaf_d", CORN_D, noise_scale=6.0)
    petal = mat_of("petal", PETAL, rough=0.7, noise_scale=10.0)
    eye = mat_of("seedhead", SEED_HEAD, rough=0.95, noise_scale=25.0, cracks=0.3)
    back = mat_of("head_back", CORN_D, noise_scale=8.0)
    C = Mesh()
    for r, y in enumerate(row_ys(1.0)):
        top = wobble(rnd, 0.12, (3, 7))
        wid = wobble(rnd, 0.25, (3, 7))
        jag = wobble(rnd, 0.12, (13, 19))
        C.row(y, -4, 4, 8, [(0.05, 0.1), (0.55, 0.27), (1.0, 0.1)], [lf_d, lf, lf], top=lambda x, t=top, j=jag: t(x) + j(x), width=lambda x, w=wid: 1 + w(x))
        for i in range(6):
            x = -4 + 8 / 6 * (i + 0.5) + rnd.uniform(-0.2, 0.2)
            yy = y + rnd.uniform(-0.08, 0.08)
            hz = rnd.uniform(1.85, 2.1)
            hc = Vector((x, yy - 0.12, hz))
            C.turn([(0.03, 0.5), (0.02, hz - 0.05)], stalk, segs=3, mx=Matrix.Translation((x, yy, 0)))
            for j, z0 in enumerate((1.05, 1.45)):
                a = rnd.uniform(0, 360)
                C.leaf((x, yy, z0), a, rnd.uniform(-25, -5), rnd.uniform(0.38, 0.46), 0.3, lf if j else lf_d)
            # the head: a disc whose axis points along -y, nodding, petals round a dark domed eye
            mx = Matrix.Translation(hc) @ Matrix.Rotation(math.radians(rnd.uniform(-15, 15)), 4, "Z") @ Matrix.Rotation(math.radians(rnd.uniform(65, 80)), 4, "X")
            s = rnd.uniform(0.9, 1.15)
            star = [(0.0, -0.07 * s), (0.32 * s, 0.0), (0.13 * s, 0.012), (0.0, 0.05 * s)]
            C.turn(star, [back, petal, eye], segs=12, mx=mx, jitter=[0, 0.42, 0, 0])
    C.build(P, "sunflowers", sharp=30)
    return P


def field_potato(seed=46):
    """An 8 x 8 m patch of potatoes: tall hilled ridges and on them a bushy row of dark leaves, plant by plant, with
    small pale flowers."""
    rnd = random.Random(seed)
    P = _root("potato")
    zf = soil(P, rnd, 0.8, 0.26, ny_per=4, nx=8)
    lf = mat_of("pot_leaf", POT_LEAF, rough=0.8, noise_scale=7.0)
    lf_d = mat_of("pot_leaf_d", CORN_D, rough=0.8, noise_scale=7.0)
    fl = mat_of("pot_flower", POT_FLOWER, rough=0.6)
    C = Mesh()
    for r, y in enumerate(row_ys(0.8)):
        top = wobble(rnd, 0.06, (4, 9))
        # one bulge per plant: 12 to the row, sampled at their crowns and between them
        C.row(y, -4, 4, 24, [(0.16, 0.2), (0.42, 0.33), (0.62, 0.12)], [lf_d, lf, lf], top=lambda x, t=top: t(x) + 0.07 * _per(x, 12, 1.5708),
              width=lambda x: 1 + 0.2 * _per(x, 12, 1.5708))
        for i in range(10):
            x = -3.8 + 7.6 * (i + rnd.random()) / 10
            C.spike((x, y + rnd.uniform(-0.1, 0.1), 0.6 + top(x)), (0, 0, 1), 0.08, 0.045, fl, n=4, rnd=rnd)
    C.build(P, "potatoes", sharp=30)
    return P


def field_carrot(seed=47):
    """An 8 x 8 m patch of carrots: twelve narrow beds of low feathery tops, fine fronds standing out of each row."""
    rnd = random.Random(seed)
    P = _root("carrot")
    pitch = 8 / 12
    zf = soil(P, rnd, pitch, 0.1, ny_per=3, nx=8)
    top_m = mat_of("carrot_top", CARROT_TOP, rough=0.8, noise_scale=9.0)
    top_d = mat_of("carrot_top_d", CORN_D, rough=0.8, noise_scale=9.0)
    C = Mesh()
    for r, y in enumerate(row_ys(pitch)):
        jag = lambda x: 0.06 * _per(x, 31, 0.3 * r) + 0.04 * _per(x, 17, r)   # noqa: E731
        C.row(y, -4, 4, 12, [(0.1, 0.06), (0.22, 0.13), (0.3, 0.05)], [top_d, top_m, top_m], top=jag)
        # the fronds: fine tall fans standing out of the row
        for i in range(34):
            x = -3.88 + 7.76 * (i + rnd.random()) / 34
            C.spike((x, y + rnd.uniform(-0.08, 0.08), 0.2), (rnd.uniform(-0.7, 0.7), rnd.uniform(-0.7, 0.7), 1), rnd.uniform(0.22, 0.32), 0.03, top_m if i % 2 else top_d, rnd=rnd)
    C.build(P, "carrots", sharp=30)
    return P


def _weeds(C, rnd, n, half, mats, zf=None, h=(0.25, 0.5)):
    for k in range(n):
        x, y = rnd.uniform(-half, half), rnd.uniform(-half, half)
        C.tuft((x, y, (zf(x, y) if zf else 0.0) + 0.02), rnd, mats[k % len(mats)], n=rnd.randint(4, 7), h=rnd.uniform(*h))


def _thistle(C, loc, rnd, stem, head, h=0.9):
    x, y, z = loc
    C.turn([(0.012, z), (0.008, z + h)], stem, segs=3, mx=Matrix.Translation((x, y, 0)))
    for k in range(3):
        a = rnd.uniform(0, 360)
        C.leaf((x, y, z + 0.15 + k * 0.2), a, rnd.uniform(-10, 25), 0.22 - k * 0.04, 0.08, stem)
    C.ball((x, y, z + h + 0.02), 0.035, head, u=5, v=3)


def _rosette(C, x, y, z, rnd, mat, n=6, L=0.2):
    """A flat rosette of weed leaves (dock, dandelion) lying on the soil."""
    a0 = rnd.uniform(0, 360)
    for k in range(n):
        C.leaf((x, y, z + 0.015), a0 + 360 * k / n + rnd.uniform(-15, 15), rnd.uniform(4, 18), L * rnd.uniform(0.75, 1.2), L * 0.45, mat)


def field_fallow(seed=48):
    """An 8 x 8 m patch left fallow: old ridges slumped almost flat and dried pale, clods, rosettes of dock and
    dandelion, grass and weed tufts coming back and a few thistles."""
    rnd = random.Random(seed)
    P = _root("fallow")
    zf = soil(P, rnd, 1.0, 0.06, ny_per=2, nx=8, light=mat_of("dry_soil", DRY, rough=1.0, noise_scale=3.0, cracks=0.3),
              dark=mat_of("dry_soil_d", TILTH, rough=1.0, noise_scale=3.0))
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    wd = mat_of("weed_d", WEED_D, rough=0.85, noise_scale=6.0)
    g = mat_of("fgrass", GRASS, rough=0.85, noise_scale=6.0)
    C = Mesh()
    for k in range(30):
        x, y = rnd.uniform(-3.7, 3.7), rnd.uniform(-3.7, 3.7)
        _rosette(C, x, y, zf(x, y), rnd, (w, wd)[k % 2], n=rnd.randint(5, 7), L=rnd.uniform(0.14, 0.26))
    # tufts gather in patches, as weeds come back
    for c in range(9):
        cx, cy = rnd.uniform(-3.2, 3.2), rnd.uniform(-3.2, 3.2)
        for k in range(rnd.randint(3, 6)):
            x, y = cx + rnd.uniform(-0.5, 0.5), cy + rnd.uniform(-0.5, 0.5)
            C.tuft((x, y, zf(x, y) + 0.02), rnd, (g, w, wd)[k % 3], n=rnd.randint(4, 6), h=rnd.uniform(0.25, 0.5))
    _weeds(C, rnd, 8, 3.7, (w, g), zf)
    for k in range(5):
        x, y = rnd.uniform(-3.4, 3.4), rnd.uniform(-3.4, 3.4)
        _thistle(C, (x, y, zf(x, y)), rnd, wd, mat_of("thistle", THISTLE, rough=0.8), h=rnd.uniform(0.6, 0.95))
    C.build(P, "weeds")
    clods(P, rnd, zf, 12, mat=mat_of("dry_clod", DRY, rough=1.0, noise_scale=6.0))
    stones(P, rnd, zf, 5)
    return P


def field_stubble(seed=49):
    """An 8 x 8 m patch after the harvest: short pale stubble in rows on dry ground, two loose straw windrows left
    along it, and a few wisps of straw."""
    rnd = random.Random(seed)
    P = _root("stubble")
    zf = soil(P, rnd, 0.8, 0.04, ny_per=2, nx=8, light=mat_of("dry_soil", DRY, rough=1.0, noise_scale=3.0, cracks=0.3),
              dark=mat_of("stubble_soil", TILTH, rough=1.0, noise_scale=3.0))
    stub = mat_of("stubble", STUBBLE, noise_scale=20.0)
    stub_d = mat_of("stubble_d", WHEAT_D, noise_scale=20.0)
    straw = mat_of("windrow", STRAW, noise_scale=14.0)
    straw_d = mat_of("windrow_d", STRAW_D, noise_scale=14.0)
    C = Mesh()
    rows = row_ys(0.8)
    for r, y in enumerate(rows):
        if r in (2, 7):
            continue  # under the windrows
        jag = lambda x, r=r: 0.035 * _per(x, 37, r) + 0.025 * _per(x, 23, 2 * r)   # noqa: E731
        C.row(y, -4, 4, 28, [(0.03, 0.16), (0.14, 0.14)], [stub_d, stub], top=jag)
    for r in (2, 7):
        y = rows[r]
        top = wobble(rnd, 0.06, (3, 8, 13))
        wid = wobble(rnd, 0.2, (2, 5, 9))
        sw = wobble(rnd, 0.15, (1, 2))
        C.row(y, -4, 4, 20, [(0.02, 0.5), (0.18, 0.46), (0.32, 0.25)], [straw_d, straw, straw], top=top, width=lambda x, w=wid: 1 + w(x), sway=sw)
        for i in range(10):
            x = -3.8 + 7.6 * (i + rnd.random()) / 10
            C.spike((x, y + sw(x) + rnd.uniform(-0.3, 0.3), 0.22 + top(x)), (rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0.3), 0.3, 0.025, straw, rnd=rnd)
    for k in range(8):
        x, y = rnd.uniform(-3.6, 3.6), rnd.uniform(-3.6, 3.6)
        C.spike((x, y, 0.05), (rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0.1), 0.35, 0.03, straw, rnd=rnd)
    C.build(P, "stubble")
    return P


def _corn_plant(C, x, y, h, rnd, mats, lean=(0.0, 0.0), broken=False):
    """One corn plant: a stalk leaning by lean (dx, dy per metre), three arching leaves, a tassel unless broken, when
    its top hangs folded over instead."""
    stalk, dark, light, tassel = mats
    base = Vector((x, y, 0.0))
    up = Vector((lean[0], lean[1], 1.0)).normalized()
    hb = h * (0.6 if broken else 1.0)
    top = base + up * hb
    C.turn([(0.028, 0.0), (0.016, hb)], stalk, segs=3, mx=Matrix.Translation(base) @ up.to_track_quat("Z", "Y").to_matrix().to_4x4())
    if broken:
        d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), -0.6)).normalized()
        C.card([tuple(top), tuple(top + d * 0.3 + Vector((0, 0, 0.05))), tuple(top + d * 0.6)], 0.05, tassel)
    else:
        C.spike(tuple(top), (rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 1), 0.3, 0.035, tassel, rnd=rnd)
    yaw = rnd.uniform(0, 6.28)
    for j, f in enumerate((0.45, 0.65, 0.85)):
        s = 1 if j % 2 else -1
        d = Vector((math.cos(yaw) * s, math.sin(yaw) * s, 0))
        L = rnd.uniform(0.55, 0.75) * (1 - 0.15 * j)
        b = base + up * hb * f
        C.card([tuple(b), tuple(b + d * L * 0.45 + Vector((0, 0, L * 0.3))), tuple(b + d * L + Vector((0, 0, -L * 0.2)))], 0.1, light if j % 2 else dark)


def field_overgrown(seed=50):
    """An 8 x 8 m patch of crops gone wild: the rows buried in rank grass and weed, corn self-sown at every height and
    some of it broken over, a few sunflowers nodding out of it, thistles and tall tufts."""
    rnd = random.Random(seed)
    P = _root("overgrown")
    zf = soil(P, rnd, 1.0, 0.07, ny_per=2, nx=8, light=mat_of("dry_soil_o", TILTH, rough=1.0, noise_scale=3.0, cracks=0.2))
    stalk = mat_of("corn_stalk", CORN_S, noise_scale=12.0, grime=0.25)
    dark = mat_of("corn_d", CORN_D, noise_scale=6.0)
    light = mat_of("corn_l", CORN_L, noise_scale=6.0)
    pale = mat_of("corn_dry", HUSK, noise_scale=8.0)
    tassel = mat_of("tassel", TASSEL, noise_scale=15.0)
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    g = mat_of("fgrass", GRASS, rough=0.85, noise_scale=6.0)
    wd = mat_of("weed_d", WEED_D, rough=0.85, noise_scale=6.0)
    C = Mesh()
    ys = row_ys(1.0)
    for r, y in enumerate(ys):
        top = wobble(rnd, 0.22, (3, 5, 11))
        wid = wobble(rnd, 0.35, (2, 5, 9))
        sw = wobble(rnd, 0.15, (2, 3))
        jag = wobble(rnd, 0.14, (19, 29))
        C.row(y, -4, 4, 14, [(0.02, 0.26), (0.3, 0.33), (0.52, 0.12)], [wd, g if r % 2 else w, w if r % 2 else g],
              top=lambda x, t=top, j=jag: t(x) + j(x), width=lambda x, ww=wid: max(0.4, 1 + ww(x)), sway=sw)
    for k in range(30):
        x, y = rnd.uniform(-3.8, 3.8), ys[rnd.randrange(8)] + rnd.uniform(-0.2, 0.2)
        h = rnd.uniform(1.0, 2.2)
        _corn_plant(C, x, y, h, rnd, (stalk, dark if k % 3 else pale, light, tassel), lean=(rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15)), broken=k % 5 == 0)
    _weeds(C, rnd, 10, 3.7, (w, g), zf, h=(0.5, 0.9))
    petal = mat_of("petal", PETAL, rough=0.7, noise_scale=10.0)
    eye = mat_of("seedhead", SEED_HEAD, rough=0.95, noise_scale=25.0, cracks=0.3)
    for k in range(4):
        x, y = rnd.uniform(-3.3, 3.3), ys[rnd.randrange(8)] + rnd.uniform(-0.2, 0.2)
        hz = rnd.uniform(1.5, 2.0)
        C.turn([(0.025, 0.0), (0.018, hz - 0.03)], stalk, segs=3, mx=Matrix.Translation((x, y, 0)))
        for j in range(2):
            C.leaf((x, y, 0.7 + j * 0.4), rnd.uniform(0, 360), rnd.uniform(-20, 10), 0.36, 0.26, dark)
        mx = Matrix.Translation((x, y - 0.08, hz)) @ Matrix.Rotation(math.radians(rnd.uniform(-60, 60)), 4, "Z") @ Matrix.Rotation(math.radians(rnd.uniform(70, 110)), 4, "X")
        C.turn([(0.0, -0.05), (0.24, 0.0), (0.1, 0.01), (0.0, 0.04)], [dark, petal, eye], segs=10, mx=mx, jitter=[0, 0.4, 0, 0])
    for k in range(4):
        x, y = rnd.uniform(-3.4, 3.4), rnd.uniform(-3.4, 3.4)
        _thistle(C, (x, y, zf(x, y)), rnd, wd, mat_of("thistle", THISTLE, rough=0.8), h=rnd.uniform(0.9, 1.3))
    C.build(P, "wild", sharp=30)
    return P


# ================================================================== orchard
def _grass_ring(P, rnd, r, n, name="grass"):
    C = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-0.3, 0.3)
        rr = r * rnd.uniform(0.6, 1.3)
        C.tuft((math.cos(a) * rr, math.sin(a) * rr, 0.0), rnd, g if k % 3 else w, n=5, h=rnd.uniform(0.2, 0.35))
    return C.build(P, name)


def fruit_tree(P, rnd, trunk_h, crown, radii, n_limbs, fruit, n_fruit, shade=0, lime=True, n_blobs=11, fruit_r=1.0, rb=None):
    """A pruned fruit tree: a short trunk (limed at the foot), scaffold limbs opening out of it, a crown of leaf masses
    spread over an ellipsoid of radii round crown, and fruit hung on its outside, mostly low and on the -y side."""
    b = bark(0.15)
    cx, cy, cz = crown
    rx, ry, rz = radii
    tp = [(0, 0, -0.1), (0.03, 0.01, trunk_h * 0.4), (-0.02, 0.03, trunk_h * 0.8), (cx * 0.2, cy * 0.2, trunk_h)]
    tube(P, "trunk", tp, [0.16, 0.13, 0.12, 0.11], b, sides=8)
    if lime:
        tube(P, "lime", [(0.0, 0.0, -0.05), (0.02, 0.01, 0.4), (0.025, 0.012, 0.75)], [0.168, 0.15, 0.142],
             K.mat("lime", LIME, rough=0.95, grime=0.4, noise_scale=12.0, cracks=0.2), sides=8)
    top = Vector(tp[-1])
    rb = rb or min(rx, ry, rz) * 0.55
    # leaf masses: a ring low round the crown, a ring higher up set in, one on top and one in the middle
    blobs = [(Vector((cx, cy, cz)), rb * 1.3)]
    ring1 = max(4, n_blobs // 2)
    a0 = rnd.uniform(0, 6.28)
    for k in range(ring1):
        a = a0 + 2 * math.pi * k / ring1 + rnd.uniform(-0.15, 0.15)
        blobs.append((Vector((cx + math.cos(a) * (rx - rb * 0.8), cy + math.sin(a) * (ry - rb * 0.8), cz - rz * 0.18 + rnd.uniform(-0.1, 0.1))), rb * rnd.uniform(0.9, 1.1)))
    ring2 = max(3, n_blobs - ring1 - 2)
    for k in range(ring2):
        a = a0 + 0.6 + 2 * math.pi * k / ring2
        blobs.append((Vector((cx + math.cos(a) * (rx - rb) * 0.6, cy + math.sin(a) * (ry - rb) * 0.6, cz + rz * 0.42)), rb * rnd.uniform(0.85, 1.0)))
    blobs.append((Vector((cx + rnd.uniform(-0.1, 0.1), cy, cz + rz - rb * 0.8)), rb * 0.9))
    for k in range(n_limbs):
        c, r = blobs[1 + k * ring1 // n_limbs]
        end = c - (c - Vector((cx, cy, c.z))) * 0.25
        mid = top.lerp(end, 0.5) + Vector((0, 0, -0.12 * rz))
        tube(P, f"limb{k}", bez(tuple(top), tuple(mid), tuple(end), 5), (0.085, 0.035), b, sides=6)
    for k in range(3):
        c, r = blobs[ring1 + 1 + k % ring2]
        tip = c + Vector((rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), r + 0.45))
        tube(P, f"twig{k}", [tuple(c), tuple(c.lerp(tip, 0.6) + Vector((0.05, 0, 0))), tuple(tip)], (0.02, 0.006), b, sides=4)
    items = [[], []]
    for k, (c, r) in enumerate(blobs):
        foliage(P, f"crown{k}", r, tuple(c), rnd, shade=shade + k % 2, scale=(1.05, 1.0, 0.85), u=8, v=6)
        if k:
            leaf_cluster(items[k % 2], tuple(c), r * 1.05, int(16 * r + 6), rnd, size=0.16)
    leaves(P, "leafy", items[0], leaf(shade))
    leaves(P, "leafy_d", items[1], leaf(shade + 1))
    F = Mesh()
    outer = blobs[1:]
    for k in range(n_fruit):
        c, r = outer[k % len(outer)]
        a = rnd.uniform(-2.9, -0.2) if k % 3 else rnd.uniform(0, 6.28)
        el = rnd.uniform(-0.8, 0.3)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el) * 0.85))
        fruit(F, c + d * r * 1.0, rnd, fruit_r)
    F.build(P, "fruit")
    return blobs


def _apple(F, p, rnd, k=1.0):
    mats = (mat_of("apple", APPLE, rough=0.4, noise_scale=8.0), mat_of("apple_d", APPLE_D, rough=0.45, noise_scale=8.0))
    F.ball(p, rnd.uniform(0.045, 0.055) * k, mats[rnd.randrange(2)], scale=(1, 1, 0.9))


def _pear(F, p, rnd, k=1.0):
    m = mat_of("pear", PEAR, rough=0.5, noise_scale=8.0)
    s = rnd.uniform(0.9, 1.1) * k
    mx = Matrix.Translation(p) @ Matrix.Rotation(math.radians(180 + rnd.uniform(-15, 15)), 4, "X") @ Matrix.Diagonal((s, s, s, 1))
    F.turn([(0.0, -0.03), (0.04, -0.015), (0.042, 0.02), (0.024, 0.06), (0.012, 0.085), (0.0, 0.09)], m, segs=6, mx=mx)


def _plum(F, p, rnd, k=1.0):
    F.ball(p, rnd.uniform(0.03, 0.036) * k, mat_of("plum", PLUM, rough=0.35, noise_scale=10.0), scale=(0.9, 0.9, 1.1))


def _windfalls(P, rnd, fruit, n, r):
    F = Mesh()
    for k in range(n):
        a = rnd.uniform(0, 6.28)
        rr = r * math.sqrt(rnd.uniform(0.15, 1.0))
        fruit(F, Vector((math.cos(a) * rr, math.sin(a) * rr, 0.035)), rnd)
    F.build(P, "windfalls")


def orchard_apple(seed=51):
    """An apple tree 4.5 m tall, pruned open: a limed trunk, four scaffold limbs, a round crown hung with red apples, a
    few windfalls in the grass under it."""
    rnd = random.Random(seed)
    P = _root("apple")
    fruit_tree(P, rnd, 1.0, (0, 0, 2.8), (2.0, 1.9, 1.35), 4, _apple, 40, fruit_r=1.25)
    _windfalls(P, rnd, _apple, 7, 1.6)
    _grass_ring(P, rnd, 0.4, 8)
    return P


def orchard_pear(seed=52):
    """A pear tree 5 m tall, narrower and more upright than the apple: a tall crown of glossier leaves and pale green
    pears hanging from it."""
    rnd = random.Random(seed)
    P = _root("pear")
    blobs = fruit_tree(P, rnd, 1.2, (0, 0, 3.1), (1.3, 1.25, 1.7), 4, _pear, 30, shade=1, n_blobs=9, fruit_r=1.2)
    # the leader: a top knot of leaves that makes the crown taller than wide
    foliage(P, "leader", 0.7, (0.05, 0.0, 4.35), rnd, shade=1, scale=(0.9, 0.9, 1.1), u=10, v=7)
    tube(P, "leader_twig", [(0.0, 0.0, 3.6), (0.05, 0.02, 4.4), (0.08, 0.0, 5.0)], (0.035, 0.008), bark(0.1), sides=5)
    _windfalls(P, rnd, _pear, 5, 1.2)
    _grass_ring(P, rnd, 0.4, 8)
    return P


def orchard_plum(seed=53):
    """A plum tree 4 m tall and spreading wider than it is tall: low limbs, a flat-topped crown of darker leaves and
    clusters of purple plums."""
    rnd = random.Random(seed)
    P = _root("plum")
    fruit_tree(P, rnd, 0.8, (0, 0, 2.4), (2.3, 2.1, 1.1), 5, _plum, 36, shade=1, n_blobs=16, fruit_r=1.3, rb=0.82)
    _windfalls(P, rnd, _plum, 8, 2.0)
    _grass_ring(P, rnd, 0.4, 8)
    return P


def orchard_tree_dead(seed=54):
    """A dead fruit tree, 2.9 m: grey bark gone loose, the old pruned limbs ending in pale saw cuts, crooked water
    shoots that died too, lichen on top of the limbs and two shrivelled fruit still hanging."""
    rnd = random.Random(seed)
    P = _root("deadfruit")
    b = K.mat("deadbark", DEAD, rough=0.95, grime=0.35, noise_scale=8.0, cracks=0.6, moss=0.3)
    cut = heart()
    tp = [(0, 0, -0.1), (0.04, 0.0, 0.5), (-0.03, 0.04, 0.9), (0.02, 0.02, 1.15)]
    tube(P, "trunk", tp, [0.17, 0.14, 0.13, 0.12], b, sides=8)
    top = Vector(tp[-1])
    lich = K.mat("lichen", "#9aa36a", rough=1.0, noise_scale=12.0)
    for k in range(4):
        a = 2 * math.pi * k / 4 + rnd.uniform(-0.3, 0.3)
        end = top + Vector((math.cos(a) * 1.3, math.sin(a) * 1.2, rnd.uniform(1.0, 1.5)))
        mid = top.lerp(end, 0.45) + Vector((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), -0.15))
        pts = bez(tuple(top), tuple(mid), tuple(end), 5)
        tube(P, f"limb{k}", pts, (0.09, 0.05), b, sides=6)
        # the saw cut at the end of the pruned limb
        d = (Vector(pts[-1]) - Vector(pts[-2])).normalized()
        cap = K.cyl(f"cut{k}", 0.05, 0.012, cut, segs=6, bev=0, parent=P)
        cap.location = Vector(pts[-1]) + d * 0.004
        cap.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        # dead side shoots, crooked and thin
        for j in range(3):
            s = Vector(pts[2 + j % 2]) + Vector((0, 0, 0.02))
            aa = a + rnd.uniform(-1.0, 1.0)
            e = s + Vector((math.cos(aa) * rnd.uniform(0.3, 0.6), math.sin(aa) * rnd.uniform(0.3, 0.6), rnd.uniform(0.4, 0.9)))
            m = s.lerp(e, 0.5) + Vector((rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), 0))
            tube(P, f"shoot{k}{j}", [tuple(s), tuple(m), tuple(e)], (0.025, 0.006), b, sides=4)
        K.sphere(f"lichen{k}", 0.07, lich, loc=tuple(Vector(pts[2]) + Vector((0, 0, 0.05))), scale=(1.6, 1.2, 0.35), parent=P)
    # loose bark peeling off the trunk
    for k in range(3):
        a = rnd.uniform(0, 360)
        K.box(f"bark_flake{k}", (0.12, 0.012, 0.22), b, loc=(math.cos(math.radians(a)) * 0.14, math.sin(math.radians(a)) * 0.14, 0.35 + k * 0.22),
              rot=(rnd.uniform(-10, 10), 8, a + 90), bev=0.004, parent=P)
    F = Mesh()
    rot = mat_of("mummy", ROTTEN, rough=0.9, noise_scale=12.0, cracks=0.5)
    for k in range(2):
        a = 2 * math.pi * k / 2 + 0.4
        p = top + Vector((math.cos(a) * 0.8, math.sin(a) * 0.75, 0.55))
        F.ball(p, 0.035, rot, scale=(1, 1, 0.8), u=6, v=4)
    F.build(P, "mummies")
    _grass_ring(P, rnd, 0.45, 10)
    return P


def orchard_sapling(seed=55):
    """A young fruit tree 1.9 m tall tied to its stake with figure-of-eight ties, a spiral guard round the stem against
    rabbits, a fresh mulch ring and a tin tag hung on a branch."""
    rnd = random.Random(seed)
    P = _root("sapling")
    b = bark(0.0)
    K.disc("mulch", 0.45, mat_of("mulch", TILTH, rough=1.0, noise_scale=6.0), (0, 0), z=0.012, segs=14, wobble=0.12, seed=seed).parent = P
    lump(P, "mound", 0.42, (0, 0, 0.0), (1, 1, 0.12), mat_of("mulch", TILTH, rough=1.0, noise_scale=6.0), rnd, jitter=0.1, u=10, v=5, flat=0.0)
    K.box("stake", (0.05, 0.05, 1.75), wood(3, wear=0.3), loc=(0.09, 0.02, 0.775), rot=(0, 1.5, 0), bev=0.008, parent=P)
    tp = [(0, 0, -0.05), (0.01, 0.0, 0.6), (-0.01, 0.01, 1.2), (0.0, 0.0, 1.65)]
    tube(P, "stem", tp, (0.03, 0.015), b, sides=6)
    rope = rope_mat()
    for z in (0.55, 1.2):
        K.torus(f"tie{z}", 0.055, 0.01, mat_of("tie", "#3a3a36", rough=0.7), loc=(0.045, 0.01, z), rot=(0, 90, 0), segs=12, parent=P)
        K.box(f"tie_x{z}", (0.06, 0.03, 0.02), mat_of("tie", "#3a3a36", rough=0.7), loc=(0.045, 0.01, z), bev=0.004, parent=P)
    items = [[], []]
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        z0 = 0.95 + 0.15 * k
        s = Vector((0.0, 0.0, z0))
        e = s + Vector((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.35))
        tube(P, f"branch{k}", [tuple(s), tuple(s.lerp(e, 0.5) + Vector((0, 0, -0.03))), tuple(e)], (0.012, 0.005), b, sides=4)
        for t in (0.45, 0.75, 1.0):
            leaf_cluster(items[k % 2], tuple(s.lerp(e, t)), 0.14, 6, rnd, size=0.11)
    leaf_cluster(items[0], (0.0, 0.0, 1.7), 0.15, 8, rnd, size=0.1)
    leaves(P, "leafy", items[0], leaf(0))
    leaves(P, "leafy_d", items[1], leaf(2))
    # the spiral guard: a pale plastic strip wound round the foot of the stem
    pts = [(math.cos(t * 0.5) * 0.045, math.sin(t * 0.5) * 0.045, 0.02 + t * 0.012) for t in range(36)]
    tube(P, "guard", pts, 0.012, mat_of("guard", BONE, rough=0.6, grime=0.4), sides=4, flat=2.2)
    tag = K.box("tag", (0.05, 0.004, 0.03), tin(ZINC, wear=0.3), loc=(0.2, -0.24, 1.0), rot=(0, 0, -20), bev=0.002, parent=P)
    rod(P, "tag_wire", (0.18, -0.22, 1.12), (0.2, -0.24, 1.02), 0.002, _iron(), segs=4)
    _grass_ring(P, rnd, 0.55, 8)
    return P


def orchard_ladder(seed=56):
    """A three-legged orchard ladder 3 m tall: two rails tapering to a narrow top, nine round rungs, a single pole leg
    hinged at the top and a canvas picking bag hooked on a rung with a few apples in it."""
    rnd = random.Random(seed)
    P = _root("ladder")
    H = 3.0
    rail = wood(1, wear=0.45)
    base_y, top_y, pole_y = -0.55, 0.15, 1.05
    bw, tw = 0.36, 0.07
    for s in (-1, 1):
        beam(P, f"rail{s}", (s * bw, base_y, 0.0), (s * tw, top_y, H), 0.05, 0.085, rail)
    for k in range(9):
        t = (k + 0.7) / 10
        y = base_y + (top_y - base_y) * t
        w = bw + (tw - bw) * t
        rod(P, f"rung{k}", (-w - 0.02, y, H * t), (w + 0.02, y, H * t), 0.018, wood(2, wear=0.6), segs=8)
    K.box("top_block", (0.2, 0.09, 0.12), wood(3), loc=(0, top_y, H + 0.02), rot=(-13, 0, 0), bev=0.01, parent=P)
    beam(P, "pole", (0, top_y + 0.06, H + 0.0), (0.02, pole_y, 0.0), 0.06, 0.06, wood(0, wear=0.4))
    K.cyl("hinge", 0.018, 0.26, _iron(), loc=(0, top_y + 0.04, H + 0.02), rot=(0, 90, 0), segs=8, bev=0, parent=P)
    for s in (-1, 1):
        K.cyl(f"hinge_nut{s}", 0.025, 0.02, _iron(), loc=(s * 0.13, top_y + 0.04, H + 0.02), rot=(0, 90, 0), segs=6, bev=0, parent=P)
        K.box(f"shoe{s}", (0.07, 0.1, 0.06), _iron(RUST), loc=(s * bw, base_y + 0.005, 0.03), bev=0.006, parent=P)
    K.box("pole_shoe", (0.08, 0.08, 0.06), _iron(RUST), loc=(0.02, pole_y, 0.03), bev=0.006, parent=P)
    # the picking bag on the fourth rung
    t = 4.7 / 10
    y = base_y + (top_y - base_y) * t - 0.08
    canvas = K.mat("canvas", SACK, rough=0.95, grime=0.4, noise_scale=10.0)
    K.box("bag_hook", (0.012, 0.06, 0.012), _iron(), loc=(0.0, y + 0.04, H * t - 0.02), bev=0, parent=P)
    lump(P, "bag", 0.2, (0.0, y - 0.08, H * t - 0.3), (1.0, 0.8, 1.2), canvas, rnd, jitter=0.06, u=10, v=7)
    rod(P, "bag_strap", (-0.12, y - 0.04, H * t - 0.12), (0.0, y + 0.04, H * t - 0.02), 0.01, canvas, segs=4)
    rod(P, "bag_strap2", (0.12, y - 0.04, H * t - 0.12), (0.0, y + 0.04, H * t - 0.02), 0.01, canvas, segs=4)
    F = Mesh()
    for k in range(4):
        _apple(F, Vector((rnd.uniform(-0.1, 0.1), y - 0.08 + rnd.uniform(-0.05, 0.05), H * t - 0.04)), rnd)
    F.build(P, "apples")
    return P


def _fruit_fill(G, kind, rnd, z=0.26):
    m = {"apple": APPLE, "pear": PEAR, "plum": PLUM}[kind]
    K.box("fill", (0.55, 0.35, 0.02), mat_of(f"{kind}_fill", m, rough=0.5, noise_scale=8.0), loc=(0, 0, z - 0.02), bev=0, parent=G)
    F = Mesh()
    for k in range(9):
        p = Vector((rnd.uniform(-0.22, 0.22), rnd.uniform(-0.12, 0.12), z))
        {"apple": _apple, "pear": lambda f, q, r: _pear(f, q + Vector((0, 0, 0.0)), r), "plum": _plum}[kind](F, p, rnd)
    F.build(G, "fruit")


def fruit_crate_stack(seed=57):
    """Picked fruit waiting by the trees: a stack of five slatted crates of apples and pears, two high with one on top,
    a sixth set down beside them and a few apples rolled into the grass."""
    rnd = random.Random(seed)
    P = _root("fruitcrates")
    spots = [((-0.33, 0.0, 0.0), 2), ((0.33, 0.02, 0.0), -1), ((-0.31, 0.01, 0.3), 3), ((0.34, -0.01, 0.3), -2), ((0.0, 0.03, 0.6), 4)]
    kinds = ["apple", "pear", "apple", "apple", "pear"]
    for k, ((x, y, z), yaw) in enumerate(spots):
        G = crate(P, f"crate{k}", (x, y, z), yaw=yaw, shade=k)
        if k in (2, 3, 4):
            _fruit_fill(G, kinds[k], rnd)
    G = crate(P, "crate_low", (0.95, -0.55, 0.0), yaw=-24, shade=1)
    _fruit_fill(G, "plum", rnd)
    F = Mesh()
    for k in range(5):
        _apple(F, Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.75, -0.35), 0.045)), rnd)
    F.build(P, "loose")
    return P


def fallen_fruit(seed=58):
    """Windfalls in the grass under a tree, about 3.5 m across: a scatter of apples, some fresh, some bruised and some
    gone brown, a few fallen leaves and the grass tufts they rolled into. Walk-through."""
    rnd = random.Random(seed)
    P = _root("windfall")
    C = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    rot = mat_of("rotten", ROTTEN, rough=0.9, noise_scale=10.0, cracks=0.4)
    bruise = mat_of("apple_bruised", "#7d4a34", rough=0.6, noise_scale=8.0)
    apples = (mat_of("apple", APPLE, rough=0.4, noise_scale=8.0), mat_of("apple_d", APPLE_D, rough=0.45, noise_scale=8.0))
    for k in range(16):
        a, r = rnd.uniform(0, 6.28), 1.7 * math.sqrt(rnd.random())
        C.tuft((math.cos(a) * r, math.sin(a) * r, 0.0), rnd, g if k % 3 else w, n=5, h=rnd.uniform(0.15, 0.3))
    for k in range(30):
        a, r = rnd.uniform(0, 6.28), 1.6 * math.sqrt(rnd.random())
        p = Vector((math.cos(a) * r, math.sin(a) * r, 0.04))
        m = rot if k % 5 == 0 else bruise if k % 7 == 0 else apples[k % 2]
        C.ball(p, rnd.uniform(0.042, 0.055), m, scale=(1, 1, 0.85 if m is not rot else 0.6))
        if k % 4 == 0:
            C.spike(p + Vector((0, 0, 0.04)), (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 1), 0.025, 0.004, mat_of("fstem", "#5a4630"), rnd=rnd)
    lf = mat_of("fallen_leaf", "#8a8a42", rough=0.9, noise_scale=6.0)
    for k in range(20):
        a, r = rnd.uniform(0, 6.28), 1.7 * math.sqrt(rnd.random())
        C.leaf((math.cos(a) * r, math.sin(a) * r, 0.012), rnd.uniform(0, 360), rnd.uniform(-4, 4), 0.11, 0.06, leaf(k % 3) if k % 2 else lf)
    C.build(P, "windfalls")
    return P


# ================================================================== field edges
def irrigation_ditch(seed=59):
    """An 8 m run of irrigation ditch along x (tiles along x): an earth channel between two grassed spoil banks, a
    hand's depth of still water in it, stones in the bed, grass on the banks and a clump of reeds."""
    rnd = random.Random(seed)
    P = _root("ditch")
    prof = [(-1.55, -0.12), (-1.5, 0.0), (-1.2, 0.17), (-0.85, 0.36), (-0.62, 0.32), (-0.42, 0.13), (-0.26, 0.03), (-0.1, 0.0),
            (0.1, 0.0), (0.26, 0.03), (0.42, 0.13), (0.62, 0.32), (0.85, 0.36), (1.2, 0.17), (1.5, 0.0), (1.55, -0.12)]
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    nx = 16
    grass = mat_of("bank_grass", GRASS, rough=0.95, noise_scale=4.0, moss=0.2)
    dirt = mat_of("bank_earth", "#6b5a48", rough=1.0, noise_scale=5.0, cracks=0.3)
    mud = mat_of("ditch_mud", MUD_D, rough=0.6, noise_scale=4.0)
    S = Mesh()
    G = []
    for i in range(nx + 1):
        x = -4 + 8 * i / nx
        col = []
        for j, (y, z) in enumerate(prof):
            dz = 0.04 * (_per(x, 2, ph[0]) + 0.6 * _per(x, 5, ph[1])) * (1 if abs(y) > 0.5 else 0.3)
            dy = 0.06 * (_per(x, 3, ph[2] + (0 if y < 0 else 2)) + 0.5 * _per(x, 7, ph[3]))
            col.append(S.v((x, y + (dy if 0.2 < abs(y) < 1.45 else 0), z + (dz if 0 < z else 0))))
        G.append(col)
    for i in range(nx):
        for j in range(len(prof) - 1):
            q = (G[i][j], G[i + 1][j], G[i + 1][j + 1], G[i][j + 1])
            ya = (prof[j][0] + prof[j + 1][0]) / 2
            m = mud if abs(ya) < 0.35 else dirt if abs(ya) < 0.6 else grass
            S.face(q, m)
    S.build(P, "banks", sharp=50)
    wat = K.mat("ditch_water", WATER, rough=0.05, metal=0.0)
    K.box("water", (8.0, 0.86, 0.02), wat, loc=(0, 0, 0.1), bev=0, parent=P)
    C = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    w = mat_of("weed", WEED, rough=0.85, noise_scale=6.0)
    for k in range(18):
        x = -3.7 + 7.4 * (k + rnd.random()) / 18
        s = 1 if k % 2 else -1
        C.tuft((x, s * rnd.uniform(0.6, 1.1), 0.3), rnd, g if k % 3 else w, n=5, h=rnd.uniform(0.25, 0.45))
    reed = mat_of("reed", REED, rough=0.8, noise_scale=6.0)
    for k in range(9):
        x = 1.6 + rnd.uniform(-0.35, 0.35)
        y = 0.32 + rnd.uniform(-0.08, 0.08)
        C.card([(x, y, 0.0), (x + rnd.uniform(-0.1, 0.1), y + rnd.uniform(-0.05, 0.1), 0.6), (x + rnd.uniform(-0.25, 0.25), y + rnd.uniform(-0.1, 0.2), 1.1)], 0.04, reed)
    C.build(P, "plants")
    for k in range(5):
        rock(P, f"bedstone{k}", rnd.uniform(0.08, 0.14), 0.08, (rnd.uniform(-3.5, 3.5), rnd.uniform(-0.3, 0.3), 0.06), rnd)
    return P


def irrigation_sluice(seed=60):
    """A small sluice gate to set in the irrigation ditch: two oak posts sunk in the banks with steel guides, a plank
    gate wound up a hand's breadth on a threaded spindle by a cast handwheel, a concrete sill and plank wings holding
    the banks."""
    rnd = random.Random(seed)
    P = _root("sluice")
    W = 0.5
    K.box("sill", (0.32, 1.25, 0.1), K.mat("sill", CONCRETE, rough=0.9, grime=0.6, cracks=0.4, moss=0.3), loc=(0, 0, 0.03), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box(f"post{s}", (0.16, 0.16, 1.55), wood(3, wear=0.5), loc=(0, s * (W + 0.08), 0.575), bev=0.015, parent=P)
        K.box(f"guide{s}", (0.1, 0.03, 1.1), _iron(RUST), loc=(0, s * (W - 0.005), 0.55), bev=0.005, parent=P)
        for z in (0.25, 0.8):
            bolt(P, (0.0, s * (W + 0.165), z), rot=(90, 0, 0))
        # plank wings into the banks, upstream side
        for k in range(3):
            plank(P, f"wing{s}{k}", (0.7, 0.03, 0.14), (-0.33, s * (W + 0.32), 0.1 + k * 0.15), rot=(0, 0, s * -55), shade=k)
        lump(P, f"bankfill{s}", 0.35, (0.0, s * (W + 0.3), 0.0), (1.2, 0.8, 0.7), mat_of("bank_earth", "#6b5a48", rough=1.0, noise_scale=5.0), rnd,
             jitter=0.15, u=8, v=5, flat=0.0)
    K.box("crossbeam", (0.18, 1.5, 0.16), wood(1, wear=0.5), loc=(0, 0, 1.3), bev=0.015, parent=P)
    for s in (-1, 1):
        K.box(f"strap{s}", (0.2, 0.18, 0.04), _iron(), loc=(0, s * (W + 0.08), 1.22), bev=0.004, parent=P)
    # the gate, raised a little so the water runs under it
    for k in range(4):
        plank(P, f"gate{k}", (0.05, 2 * W - 0.02, 0.15), (0, 0, 0.21 + k * 0.15), shade=k + 1)
    K.box("gate_band", (0.07, 2 * W - 0.1, 0.05), _iron(), loc=(0, 0, 0.74), bev=0.005, parent=P)
    rod(P, "spindle", (0, 0, 0.74), (0, 0, 1.68), 0.018, M(ZINC, metal=0.6, wear=0.2, grime=0.4), segs=8)
    for k in range(10):
        K.torus(f"thread{k}", 0.019, 0.004, M(ZINC, metal=0.6, wear=0.2), loc=(0, 0, 1.42 + k * 0.025), segs=8, parent=P)
    K.cyl("nut_block", 0.06, 0.08, _iron(), loc=(0, 0, 1.42), segs=6, bev=0.005, parent=P)
    hw = _iron(GRAPHITE)
    K.torus("wheel", 0.2, 0.016, hw, loc=(0, 0, 1.58), segs=20, parent=P)
    K.cyl("wheel_hub", 0.04, 0.06, hw, loc=(0, 0, 1.58), segs=8, bev=0.005, parent=P)
    for k in range(5):
        a = 2 * math.pi * k / 5
        rod(P, f"spoke{k}", (0, 0, 1.58), (math.cos(a) * 0.2, math.sin(a) * 0.2, 1.58), 0.01, hw, segs=5)
    K.cyl("handle", 0.014, 0.1, hw, loc=(0.2, 0, 1.63), segs=6, bev=0, parent=P)
    return P


def field_marker_post(seed=61):
    """A field marker: a split stake 1.4 m tall with its top painted mustard and white, a strip of red rag tied under
    it, a punched tin tag nailed on and field stones heaped round its foot."""
    rnd = random.Random(seed)
    P = _root("marker")
    G = _grp(P, "lean", (0, 0, 0), (2.5, -3, 0))
    K.box("stake", (0.09, 0.08, 1.55), wood(2, wear=0.5), loc=(0, 0, 0.62), bev=0.01, parent=G)
    K.box("tip_paint", (0.096, 0.086, 0.16), M(MUSTARD, wear=0.45, grime=0.3), loc=(0, 0, 1.3), bev=0.01, parent=G)
    K.box("tip_white", (0.096, 0.086, 0.06), M(BONE, wear=0.45, grime=0.3), loc=(0, 0, 1.19), bev=0.008, parent=G)
    K.cyl("tip_cap", 0.06, 0.06, M(MUSTARD, wear=0.45), loc=(0, 0, 1.41), segs=4, r2=0.01, rot=(0, 0, 45), bev=0.004, parent=G)
    rag = K.mat("rag", BRICK, rough=0.95, grime=0.3, noise_scale=10.0)
    K.box("rag_knot", (0.11, 0.1, 0.04), rag, loc=(0, 0, 1.05), bev=0.01, parent=G)
    for k, (dy, dz, a) in enumerate(((-0.12, -0.05, 25), (-0.1, -0.12, 50))):
        K.box(f"rag_tail{k}", (0.04, 0.2, 0.006), rag, loc=(0.03, dy, 1.05 + dz), rot=(a, 0, 8), bev=0.002, parent=G)
    K.box("tag", (0.07, 0.006, 0.1), tin(ZINC, wear=0.25), loc=(0, -0.044, 0.85), bev=0.003, parent=G)
    for k in range(5):
        K.cyl("punch", 0.006, 0.004, K.mat("hole", "#1a1816"), loc=(-0.024 + 0.012 * k, -0.048, 0.86 - 0.02 * (k % 2)), rot=(90, 0, 0), segs=5, bev=0, parent=G)
    for k in range(2):
        K.cyl("nail", 0.008, 0.01, _iron(), loc=(0, -0.049, 0.89 - k * 0.08), rot=(90, 0, 0), segs=6, bev=0, parent=G)
    for k in range(6):
        a = 2 * math.pi * k / 6 + rnd.uniform(-0.2, 0.2)
        rock(P, f"stone{k}", rnd.uniform(0.09, 0.13), 0.1, (math.cos(a) * 0.17, math.sin(a) * 0.17, 0.05), rnd)
    rock(P, "stone_top", 0.1, 0.09, (0.03, -0.12, 0.13), rnd)
    C = Mesh()
    C.tuft((0.0, 0.0, 0.0), rnd, mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0), n=7, h=0.3, spread=0.25)
    C.build(P, "grass")
    return P


def crop_cart(seed=62):
    """A two-wheeled hand cart of the harvest resting on its prop leg: a plank bed heaped with cabbages, a bundle of
    corn cobs and a sack, the handles towards -x."""
    rnd = random.Random(seed)
    P = _root("cropcart")
    B = _grp(P, "bed", (0.15, 0, 0.62), (0, -4, 0))
    L, Wd, H = 1.3, 0.86, 0.32
    for k in range(5):
        plank(B, f"floor{k}", (L, Wd / 5 - 0.01, 0.03), (0, -Wd / 2 + Wd / 5 * (k + 0.5), 0), shade=k)
    for s in (-1, 1):
        for k in range(2):
            plank(B, f"side{s}{k}", (L, 0.03, 0.14), (0, s * (Wd / 2 + 0.015), 0.09 + k * 0.15), shade=k + 2)
        K.box(f"stake{s}a", (0.05, 0.05, H + 0.04), wood(3), loc=(L / 2 - 0.05, s * (Wd / 2 + 0.045), H / 2), bev=0.006, parent=B)
        K.box(f"stake{s}b", (0.05, 0.05, H + 0.04), wood(3), loc=(-L / 2 + 0.05, s * (Wd / 2 + 0.045), H / 2), bev=0.006, parent=B)
        beam(B, f"handle{s}", (L / 2 - 0.1, s * 0.36, -0.06), (-L / 2 - 0.85, s * 0.3, -0.06), 0.06, 0.05, wood(1, wear=0.6))
        K.cyl(f"grip{s}", 0.027, 0.22, wood(0, wear=0.8), loc=(-L / 2 - 0.75, s * 0.3, -0.06), rot=(0, 90, 0), segs=8, bev=0.005, parent=B)
    for k in range(2):
        x = (k - 0.5) * (L - 0.04)
        plank(B, f"end{k}", (0.03, Wd + 0.06, 0.28), (x, 0, 0.15), shade=k)
    K.box("axle", (0.08, Wd + 0.36, 0.08), _iron(), loc=(0.05, 0, -0.08), bev=0.01, parent=B)
    for s in (-1, 1):
        spoked_wheel(P, f"wheel{s}", (0.2, s * 0.64, 0.5), 0.5, n=10, yaw=90)
    beam(P, "prop_leg", (-0.98, 0, 0.6), (-1.0, 0.0, 0.0), 0.05, 0.05, wood(2))
    # the load: a heap of cabbages, a bundle of cobs on top at the back and a sack in front
    C = Mesh()
    outer = mat_of("cabbage_d", CABBAGE_D, rough=0.8, noise_scale=7.0)
    head = mat_of("cabbage", CABBAGE, rough=0.7, noise_scale=6.0)
    heap = [(x, y, 0.02) for x in (0.42, 0.15, -0.12) for y in (-0.24, 0.0, 0.24)]
    heap += [(0.3, -0.12, 0.17), (0.3, 0.13, 0.17), (0.04, -0.1, 0.17), (0.05, 0.16, 0.19), (0.18, 0.02, 0.31)]
    for k, (x, y, z) in enumerate(heap):
        _cabbage(C, Vector((x + rnd.uniform(-0.03, 0.03), y + rnd.uniform(-0.03, 0.03), z)), rnd, outer, head, 0.78)
    husk = mat_of("husk", HUSK, noise_scale=10.0)
    corn = mat_of("cob", "#d8bf5a", rough=0.6, noise_scale=20.0)
    for k in range(7):
        y = -0.3 + k * 0.1
        mx = Matrix.Translation((-0.42, y, 0.12 + 0.07 * (k % 2) + (0.08 if 2 <= k <= 4 else 0))) @ Matrix.Rotation(math.radians(90 + rnd.uniform(-10, 10)), 4, "Y") @ Matrix.Rotation(rnd.uniform(0, 6), 4, "Z")
        C.turn([(0.0, -0.13), (0.035, -0.1), (0.04, 0.05), (0.02, 0.14), (0.0, 0.17)], [husk, corn, corn, corn], segs=6, mx=mx)
    C.build(B, "load")
    sack(B, "sack", (0.46, 0.32, 0.26), (0.5, -0.05, 0.3), (0, 0, 95), K.mat("sacking", SACK, rough=0.95, grime=0.4, noise_scale=10.0), rnd)
    return P


def windbreak_hedge(seed=63):
    """An 8 m hedgerow along x, 2 m tall (tiles along x): a dense bank of small-leaved shrub, billowing along its top and
    sides, the woody stems showing at its foot with grass in front."""
    rnd = random.Random(seed)
    P = _root("hedge")
    base = mat_of("hedge_base", "#3a5226", rough=0.95, noise_scale=6.0)
    C = Mesh()
    top = wobble(rnd, 0.18, (3, 7, 13))
    wid = wobble(rnd, 0.1, (2, 5, 9))
    sw = wobble(rnd, 0.08, (2, 3))
    C.row(0, -4, 4, 16, [(-0.05, 0.42), (0.35, 0.56), (1.0, 0.6), (1.65, 0.56), (1.95, 0.36)], [base, leaf(1), leaf(0), leaf(1), leaf(0)],
          top=top, width=lambda x: 1 + wid(x), sway=sw)
    C.build(P, "hedge", sharp=30)
    # billows on its top and sides, kept inside the run so the tile ends stay clean
    items = [[], []]
    n = 0
    for k in range(9):
        x = -3.45 + 6.9 * (k + rnd.uniform(0.2, 0.8)) / 9
        c = (x, sw(x) + rnd.uniform(-0.15, 0.15), 1.72 + top(x) + rnd.uniform(0.0, 0.15))
        foliage(P, f"top{k}", rnd.uniform(0.56, 0.68), c, rnd, shade=k % 2, scale=(1.2, 1.05, 0.8), u=8, v=6)
        leaf_cluster(items[k % 2], c, 0.5, 16, rnd, size=0.12)
    for k in range(8):
        x = -3.4 + 6.8 * (k + rnd.uniform(0.2, 0.8)) / 8
        s = 1 if k % 2 else -1
        z = rnd.uniform(0.7, 1.4)
        c = (x, sw(x) * z / 2 + s * 0.5 * (1 + wid(x)), z)
        foliage(P, f"side{k}", rnd.uniform(0.42, 0.52), c, rnd, shade=(k + 1) % 2, scale=(1.3, 0.8, 1.0), u=8, v=6)
        leaf_cluster(items[(k + 1) % 2], c, 0.38, 10, rnd, size=0.12)
    for k in range(60):
        x = rnd.uniform(-3.85, 3.85)
        s = rnd.choice((-1, 1))
        z = rnd.uniform(0.35, 1.85)
        y = s * 0.6 * (1 + wid(x)) + sw(x) * z / 2
        leaf_cluster(items[k % 2], (x, y, z), 0.1, 3, rnd, size=0.12)
    leaves(P, "leafy", items[0], leaf(0))
    leaves(P, "leafy_d", items[1], leaf(1))
    st = bark(0.2)
    for k in range(8):
        x = -3.5 + 7.0 * (k + rnd.random()) / 8
        y = rnd.uniform(-0.3, 0.3)
        tube(P, f"stem{k}", [(x, y, -0.05), (x + rnd.uniform(-0.1, 0.1), y - 0.08, 0.25), (x + rnd.uniform(-0.15, 0.15), y - 0.1 * rnd.random(), 0.5)], (0.045, 0.025), st, sides=5)
    G = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    for k in range(10):
        x = -3.7 + 7.4 * (k + rnd.random()) / 10
        s = 1 if k % 2 else -1
        G.tuft((x, s * rnd.uniform(0.6, 0.8), 0.0), rnd, g, n=5, h=rnd.uniform(0.25, 0.4))
    G.build(P, "grass")
    return P


def field_gate_post_pair(seed=64):
    """The two gate posts of a field entrance, 3 m apart: squared oak posts 1.6 m tall with chamfered caps, the hinge
    pins on one and the latch keeper on the other, packed stones round their feet and the stubs of the fence rails."""
    rnd = random.Random(seed)
    P = _root("gateposts")
    X = 1.6
    for s in (-1, 1):
        G = _grp(P, f"post{s}", (s * X, 0, 0), (rnd.uniform(-1.5, 1.5), s * rnd.uniform(0.5, 2.0), 0))
        K.box("post", (0.22, 0.22, 1.85), wood(3, wear=0.55), loc=(0, 0, 0.67), bev=0.02, parent=G)
        K.cyl("cap", 0.16, 0.08, wood(3, wear=0.6), loc=(0, 0, 1.62), rot=(0, 0, 45), segs=4, r2=0.02, bev=0.01, parent=G)
        K.box("cap_band", (0.235, 0.235, 0.04), M(BONE, wear=0.6, grime=0.4), loc=(0, 0, 1.48), bev=0.008, parent=G)
        # rail stubs going away from the opening
        for k, z in enumerate((0.45, 0.9, 1.3)):
            L = 0.9 if not (s == 1 and k == 1) else 0.45
            plank(G, f"rail{k}", (L, 0.05, 0.12), (s * (0.11 + L / 2), 0.0, z), rot=(0, (6 if s == 1 and k == 1 else 0), 0), shade=k)
            bolt(G, (s * 0.14, -0.03, z))
        for k in range(5):
            a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
            rock(P, f"stone{s}{k}", rnd.uniform(0.09, 0.13), 0.1, (s * X + math.cos(a) * 0.22, math.sin(a) * 0.22, 0.04), rnd)
    iron = _iron(RUST)
    for z in (0.35, 1.2):
        K.cyl("pintle", 0.018, 0.08, iron, loc=(-X + 0.12, -0.05, z + 0.04), segs=8, bev=0, parent=P)
        K.box("pintle_plate", (0.04, 0.06, 0.14), iron, loc=(-X + 0.115, -0.05, z), bev=0.005, parent=P)
    K.box("keeper", (0.03, 0.08, 0.12), iron, loc=(X - 0.12, -0.04, 0.95), bev=0.005, parent=P)
    K.box("keeper_lip", (0.06, 0.08, 0.02), iron, loc=(X - 0.15, -0.04, 1.0), bev=0.004, parent=P)
    C = Mesh()
    g = mat_of("tgrass", GRASS, rough=0.85, noise_scale=6.0)
    for s in (-1, 1):
        for k in range(3):
            C.tuft((s * X + rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 0.0), rnd, g, n=5, h=0.3)
    C.build(P, "grass")
    # a trodden bare strip through the opening
    return P


PROPS = {
    "field_furrows": field_furrows,
    "field_wheat": field_wheat,
    "field_corn": field_corn,
    "field_cabbage": field_cabbage,
    "field_sunflower": field_sunflower,
    "field_potato": field_potato,
    "field_carrot": field_carrot,
    "field_fallow": field_fallow,
    "field_stubble": field_stubble,
    "field_overgrown": field_overgrown,
    "orchard_apple": orchard_apple,
    "orchard_pear": orchard_pear,
    "orchard_plum": orchard_plum,
    "orchard_tree_dead": orchard_tree_dead,
    "orchard_sapling": orchard_sapling,
    "orchard_ladder": orchard_ladder,
    "fruit_crate_stack": fruit_crate_stack,
    "fallen_fruit": fallen_fruit,
    "irrigation_ditch": irrigation_ditch,
    "irrigation_sluice": irrigation_sluice,
    "field_marker_post": field_marker_post,
    "crop_cart": crop_cart,
    "windbreak_hedge": windbreak_hedge,
    "field_gate_post_pair": field_gate_post_pair,
}
