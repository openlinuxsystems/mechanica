"""Build the knockout bag: the bag a knocked-out player's items lie in (Chase's reference sheet, 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/knockout_bag.glb`` - a mustard-yellow canvas drawstring bag about 0.40 m across and 0.42 m
  high, standing on its origin (the bottom at y = 0, centred in x and z), its front facing glTF +Z:

  - a rounded body, a little wider low down, tapering to a neck gathered into soft folds by a drawstring, with a
    short ruffle and a rolled, stitched hem above it and the dark inside showing through the opening;
  - ten dark metal eyelets round the neck, a twisted beige rope threaded through them, tied in a knot at the front
    with two frayed tails hanging down;
  - a burnt-orange leather base band with stitch rows along its top and foot;
  - a front pocket in the same leather with stitched corner patches, a flap over it and a metal snap;
  - a dark brown leather tab with two rivets on each side under the neck;
  - stitched seams down the back and both sides.

  Seven materials: the canvas (inside included), the base band, the pocket, the flap, the side tabs, the metal and
  the rope.

Units are metres; in Blender Z is up and the exporter turns it into glTF's Y up (Blender -Y, the bag's front, becomes
glTF +Z).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/knockout_bag/build_knockout_bag.py                  # writes the .glb
    python tools/knockout_bag/build_knockout_bag.py --render OUT_DIR # also renders a review sheet into OUT_DIR

The output is committed; re-run this script after changing it and commit the result. The geometry and textures are
deterministic (seeded noise only).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "rocks"))
sys.path.insert(0, str(HERE.parent / "soil"))
import build_rocks as br  # noqa: E402  (the shared image, material and export helpers)
import build_soil as bs  # noqa: E402  (tubes along a path, mesh objects with per-corner UVs)
import knockout_bag_textures as kt  # noqa: E402
import rock_textures as rt  # noqa: E402
import soil_textures as st  # noqa: E402

REPO = HERE.parents[1]
OUT = REPO / "mods" / "vanilla" / "meshes" / "knockout_bag.glb"

# ---- Shape (metres, Blender axes: x right, -y front, z up) ------------------------------------------
SUPER = 2.5                       # the cross-section is a superellipse: a rounded square
COLUMNS = 100                     # samples round the bag
FOLDS = 10                        # gathers round the neck, one eyelet on each crest
# the canvas profile (radius, height), from inside the base band up the outside, over the hem and down inside
CANVAS_PROFILE = [(0.180, 0.050), (0.186, 0.072), (0.190, 0.100), (0.193, 0.140), (0.191, 0.200), (0.185, 0.250),
                  (0.173, 0.290), (0.155, 0.322), (0.135, 0.344), (0.122, 0.358), (0.118, 0.370), (0.121, 0.383),
                  (0.125, 0.395), (0.126, 0.404), (0.121, 0.411), (0.114, 0.411), (0.110, 0.404), (0.107, 0.390),
                  (0.103, 0.372), (0.096, 0.356), (0.080, 0.346), (0.050, 0.339), (0.020, 0.336)]
# the base band, from the middle of the bottom out, up its side and over the lip tucked into the canvas
BAND_PROFILE = [(0.030, 0.000), (0.170, 0.000), (0.190, 0.005), (0.197, 0.017), (0.199, 0.036), (0.198, 0.058),
                (0.194, 0.069), (0.186, 0.073), (0.181, 0.067)]
EYELET_Z = 0.360
HEM_Z = 0.397
SEAM_TOP_Z = 0.348
BAND_TOP_Z = 0.072
# how deep the folds are by height on the outside (z, metres)
FOLD_Z = [(0.220, 0.0), (0.280, 0.002), (0.320, 0.005), (0.350, 0.007), (0.365, 0.006), (0.385, 0.010), (0.440, 0.010)]
FOLD_INSIDE = (0.010, 0.0)      # at the rim inside, and at the bottom of the opening

POCKET = (0.105, 0.083, 0.026)    # half width, half height, corner radius
POCKET_Z = 0.164
POCKET_RINGS = [(0.0, -0.003), (0.0, 0.006), (0.002, 0.0095), (0.005, 0.0125), (0.009, 0.0145), (0.014, 0.0157),
                (0.022, 0.0163), (0.035, 0.0166), (0.06, 0.0168)]
FLAP = (0.110, 0.041, 0.028)
FLAP_Y = 0.046                    # its centre above the pocket's
FLAP_RINGS = [(0.0, -0.002), (0.0, 0.0075), (0.0025, 0.0098), (0.007, 0.0108), (0.016, 0.0113), (0.041, 0.0115)]
SNAP_Y = 0.024                    # above the pocket's centre
STRAP = (0.0165, 0.058, 0.0165)
STRAP_Z = 0.290
STRAP_RINGS = [(0.0, -0.002), (0.0, 0.003), (0.0015, 0.0042), (0.004, 0.0047), (0.0165, 0.0048)]
RIVET_Y = (0.030, -0.030)
ROPE_R = 0.0056
CANVAS_TEX = (1536, 768)


def superellipse(theta):
    """Unit direction of the cross-section at angle theta: theta 0 is the back (+y), pi/2 the right side (+x), pi the
    front (-y)."""
    s, c = np.sin(theta), np.cos(theta)
    e = 2.0 / SUPER
    return np.stack([np.sign(s) * np.abs(s) ** e, np.sign(c) * np.abs(c) ** e], -1)


def smooth_profile(points, dense=1600):
    """A Catmull-Rom curve through the profile points, resampled densely: radius, height and length along it."""
    p = np.asarray(points, float)
    ext = np.vstack([2 * p[0] - p[1], p, 2 * p[-1] - p[-2]])
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for t in np.linspace(0, 1, 40, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(p[-1])
    out = np.array(out)
    seg = np.linalg.norm(np.diff(out, axis=0), axis=1)
    along = np.concatenate([[0], np.cumsum(seg)])
    t = np.linspace(0, along[-1], dense)
    return np.interp(t, along, out[:, 0]), np.interp(t, along, out[:, 1]), t


def rows_by_curvature(r, z, t, count, weight=0.012):
    """`count` lengths along the profile, closer together where it bends (the hem, the neck)."""
    d = np.stack([np.gradient(r), np.gradient(z)], -1)
    ang = np.unwrap(np.arctan2(d[:, 1], d[:, 0]))
    kappa = np.abs(np.gradient(ang)) / np.maximum(np.gradient(t), 1e-9)
    cost = np.concatenate([[0], np.cumsum(np.diff(t) * (1 + weight * kappa[1:]))])
    return np.interp(np.linspace(0, cost[-1], count), cost, t)


class CanvasShape:
    """The canvas: its profile, the folds the drawstring gathers and where the seams and eyelets are."""

    def __init__(self):
        self.r, self.z, self.t = smooth_profile(CANVAS_PROFILE)
        self.length = float(self.t[-1])
        top = int(np.argmax(self.z))
        self.top = top
        self.t_rim = float(self.t[top])
        self.rho0 = 0.19 * 1.04
        self.t_band = self.t_of_z(BAND_TOP_Z)
        self.t_seam_top = self.t_of_z(SEAM_TOP_Z)
        self.t_hem = self.t_of_z(HEM_Z)
        self.t_eyelet = self.t_of_z(EYELET_Z)
        step = 2 * math.pi / FOLDS
        self.eyelets = [(math.pi + (k + 0.5) * step) % (2 * math.pi) for k in range(FOLDS)]
        self.phase = math.pi / 2 - FOLDS * self.eyelets[0]
        self.seams = [0.0, math.pi / 2, 3 * math.pi / 2]
        self.noise = rt.Noise3(613)

    def v_of(self, t):
        """Texture v of length t: the profile spans the texture's height inside a small margin, so filtering at its
        ends never wraps round to the other end."""
        return 0.004 + 0.992 * np.asarray(t, float) / self.length

    def t_of_z(self, z):
        """Length along the profile at height z on the outside."""
        return np.interp(z, self.z[:self.top + 1], self.t[:self.top + 1])

    def radius(self, t):
        return np.interp(t, self.t, self.r)

    def height(self, t):
        return np.interp(t, self.t, self.z)

    def rho(self, t):
        """Metres per radian round the bag at length t."""
        return np.maximum(self.radius(t), 0.02) * 1.04

    def amplitude(self, t):
        t = np.asarray(t, float)
        outside = np.interp(self.height(t), [p[0] for p in FOLD_Z], [p[1] for p in FOLD_Z])
        inside = np.interp(t, [self.t_rim, self.length], FOLD_INSIDE)
        return np.where(t <= self.t_rim, outside, inside)

    def fold(self, theta, t):
        """The folds: a value in about [-1, 1] (crests positive) and how deep they are there (0..1)."""
        wave = 0.8 * np.sin(FOLDS * theta + self.phase) + 0.2 * np.sin(2 * FOLDS * theta + 2 * self.phase + 1.3)
        wave = wave * (1 + 0.25 * np.sin(3 * theta + 0.7) + 0.15 * np.sin(5 * theta + 2.0))
        return wave, self.amplitude(t) / 0.010

    def point(self, theta, t):
        """Points on the canvas (N x 3) at angles theta and lengths t (broadcast)."""
        theta, t = np.broadcast_arrays(np.asarray(theta, float), np.asarray(t, float))
        wave, amp = self.fold(theta, t)
        r = self.radius(t) + wave * amp * 0.010
        z = self.height(t)
        d = superellipse(theta)
        p = np.stack([d[..., 0] * 0.19, d[..., 1] * 0.19, z * 2.0], -1).reshape(-1, 3)
        lump = self.noise.fbm(p, 9.0, octaves=3, scale=(1.0, 1.0, 0.35)).reshape(theta.shape)
        r = r + 0.0022 * lump * (r > 0.03)
        return np.stack([d[..., 0] * r, d[..., 1] * r, z], -1)

    def on_surface(self, theta, z, h):
        """Points h metres out from the outside of the canvas at angles theta and heights z, with the surface normal
        and the direction of increasing theta."""
        t = self.t_of_z(z)
        e = 1e-4
        p = self.point(theta, t)
        dth = self.point(theta + e, t) - self.point(theta - e, t)
        dz = self.point(theta, self.t_of_z(z + e)) - self.point(theta, self.t_of_z(z - e))
        n = np.cross(dz, dth)
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        dth /= np.linalg.norm(dth, axis=-1, keepdims=True)
        return p + n * np.asarray(h, float)[..., None], n, dth


class BandShape:
    """The leather base band's profile, for its texture."""

    def __init__(self):
        self.r, self.z, self.t = smooth_profile(BAND_PROFILE)
        self.length = float(self.t[-1])
        self.rho0 = 0.195 * 1.04
        side = np.argmax(self.r)
        self.t_foot = float(np.interp(0.005, self.z[:side + 1], self.t[:side + 1]))
        lip = int(np.argmax(self.z))
        self.t_lip = float(self.t[lip])
        up = slice(side, lip + 1)
        self.stitch_rows = [float(np.interp(z, self.z[up], self.t[up])) for z in (0.016, 0.066)]


# =====================================================================================================
# Meshes
# =====================================================================================================

def lathe(points_at, rows, uv_v, centre=None, centre_uv_v=None):
    """A mesh swept round the bag: points_at(theta, row) gives a ring per row; quads between rings run so their
    normals face out of the profile's left side (up the outside, into the bag inside). With `centre` the last
    (or, for a band, the first) ring closes on a fan round that point."""
    th = np.linspace(0, 2 * math.pi, COLUMNS, endpoint=False)
    verts = []
    for i in range(len(rows)):
        verts += [tuple(map(float, p)) for p in points_at(th, rows[i])]
    faces, uvs = [], []
    for i in range(len(rows) - 1):
        for j in range(COLUMNS):
            k = (j + 1) % COLUMNS
            q = [i * COLUMNS + j, (i + 1) * COLUMNS + j, (i + 1) * COLUMNS + k, i * COLUMNS + k]
            faces.append(q)
            uvs.append([(j / COLUMNS, uv_v[i]), (j / COLUMNS, uv_v[i + 1]), ((j + 1) / COLUMNS, uv_v[i + 1]),
                        ((j + 1) / COLUMNS, uv_v[i])])
    if centre is not None:
        c = len(verts)
        verts.append(tuple(map(float, centre)))
        last = centre_uv_v[0]
        ring = 0 if last == "first" else len(rows) - 1
        for j in range(COLUMNS):
            k = (j + 1) % COLUMNS
            a, b = ring * COLUMNS + j, ring * COLUMNS + k
            if last == "first":
                faces.append([c, a, b])
            else:
                faces.append([a, c, b])
            uvs.append([((j + 0.5) / COLUMNS, centre_uv_v[1]), (j / COLUMNS, uv_v[ring]), ((j + 1) / COLUMNS, uv_v[ring])]
                       if last == "first" else
                       [(j / COLUMNS, uv_v[ring]), ((j + 0.5) / COLUMNS, centre_uv_v[1]), ((j + 1) / COLUMNS, uv_v[ring])])
    return verts, faces, uvs


def build_canvas(shape):
    r, z, t = shape.r, shape.z, shape.t
    rows = rows_by_curvature(r, z, t, 58)
    verts, faces, uvs = lathe(lambda th, ti: shape.point(th, np.full_like(th, ti)), rows, shape.v_of(rows),
                              centre=(0, 0, shape.height(shape.length) - 0.001), centre_uv_v=("last", shape.v_of(shape.length)))
    albedo, height, px = kt.canvas_maps(*CANVAS_TEX, shape)
    albedo = kt.lit(albedo, height, px, shade=0.3)
    normal = kt.normal_from_height(height, px, strength=1.0)
    mat = br.make_material("knockout_bag_canvas", albedo, normal, rough=0.9)
    return bs.mesh_object("KnockoutBag", verts, faces, uvs, mat, smooth_angle=80)


def build_band(band):
    rows = rows_by_curvature(band.r, band.z, band.t, 26, weight=0.02)
    noise = rt.Noise3(707)

    def ring(th, ti):
        rr, zz = np.interp(ti, band.t, band.r), np.interp(ti, band.t, band.z)
        d = superellipse(th)
        p = np.stack([d[:, 0] * 0.19, d[:, 1] * 0.19, np.full(len(th), zz * 2)], -1)
        rr = rr + 0.0012 * noise.fbm(p, 8.0, octaves=2)
        return np.stack([d[:, 0] * rr, d[:, 1] * rr, np.full(len(th), zz)], -1)

    verts, faces, uvs = lathe(ring, rows, rows / band.length, centre=(0, 0, 0.0), centre_uv_v=("first", 0.0))
    albedo, height, px = kt.band_maps(2048, 256, band)
    albedo = kt.lit(albedo, height, px, shade=0.3)
    mat = br.make_material("knockout_bag_band", albedo, kt.normal_from_height(height, px), rough=0.88)
    return bs.mesh_object("KnockoutBagBand", verts, faces, uvs, mat, smooth_angle=70)


def outline(a, b, r, step=0.009):
    """A rounded rectangle's outline, counter-clockwise from the bottom left: per sample its corner-centre point
    on the inner rectangle and its outward direction."""
    ia, ib = a - r, b - r
    out = []

    def side(p0, p1, n):
        k = max(1, int(math.ceil(np.linalg.norm(np.subtract(p1, p0)) / step)))
        for s in np.linspace(0, 1, k, endpoint=False):
            out.append((np.add(p0, np.subtract(p1, p0) * s), np.array(n, float)))

    def corner(c, a0):
        for ang in np.linspace(a0, a0 + 90, 7, endpoint=False):
            out.append((np.array(c, float), np.array([math.cos(math.radians(ang)), math.sin(math.radians(ang))])))

    side((-ia, -ib), (ia, -ib), (0, -1))
    corner((ia, -ib), -90)
    side((ia, -ib), (ia, ib), (1, 0))
    corner((ia, ib), 0)
    side((ia, ib), (-ia, ib), (0, 1))
    corner((-ia, ib), 90)
    side((-ia, ib), (-ia, -ib), (-1, 0))
    corner((-ia, -ib), 180)
    return out


def patch(a, b, r, rings, place, uv_rect):
    """A raised patch (pocket, flap, tab) on the bag: rings of its rounded-rectangle outline inset by d and raised
    by h (`rings`, from the edge in), down to a line in the middle. place(x, y, h) puts local points (x right, y up,
    h out) on the bag. uv_rect = (u0, u1) of the atlas it is mapped into (planar, as patch_grid paints it)."""
    samples = outline(a, b, r)
    n = len(samples)
    if rings[-1][0] < min(a, b):                     # always close up to the middle line
        rings = rings + [(min(a, b), rings[-1][1])]
    xs, ys, hs = [], [], []
    for d, h in rings:
        d = min(d, min(a, b))
        lim = np.array([a - d, b - d])
        for c, nn in samples:
            p = np.clip(c + max(r - d, 0) * nn, -lim, lim)
            xs.append(p[0])
            ys.append(p[1])
            hs.append(h)
    xs, ys, hs = map(np.array, (xs, ys, hs))
    verts = [tuple(map(float, p)) for p in place(xs, ys, hs)]
    m = 0.004
    u0, u1 = uv_rect
    uv = [(u0 + (u1 - u0) * (x + a + m) / (2 * (a + m)), (y + b + m) / (2 * (b + m))) for x, y in zip(xs, ys)]
    faces, fuv = [], []
    for k in range(len(rings) - 1):
        for i in range(n):
            j = (i + 1) % n
            q = [k * n + i, k * n + j, (k + 1) * n + j, (k + 1) * n + i]
            faces.append(q)
            fuv.append([uv[v] for v in q])
    return verts, faces, fuv


def ring_height(rings, d):
    """The height of a patch at inset d (the same piecewise-linear profile its rings make)."""
    ds = [p[0] for p in rings[1:]]
    hs = [p[1] for p in rings[1:]]
    return np.where(np.asarray(d) > 0, np.interp(d, ds, hs), 0.0)


def place_on(shape, theta_c, z_c):
    def place(x, y, h):
        z = z_c + y
        rho = shape.rho(shape.t_of_z(z))
        p, _, _ = shape.on_surface(theta_c - x / rho, z, h)
        return p
    return place


def pocket_height(x, y):
    """The pocket's own height (above the canvas) at pocket coordinates x, y."""
    return ring_height(POCKET_RINGS, kt.rounded_rect_sdf(x, y, *POCKET))


def join(objects):
    for o in bpy.context.scene.objects:
        o.select_set(o in objects)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    return objects[0]


def merge_meshes(parts):
    verts, faces, uvs = [], [], []
    for v, f, uv in parts:
        base = len(verts)
        verts += v
        faces += [[i + base for i in q] for q in f]
        uvs += uv
    return verts, faces, uvs


def leather_material(name, maps, rough):
    albedo, height, px = maps
    return br.make_material(name, kt.lit(albedo, height, px, shade=0.3), kt.normal_from_height(height, px), rough=rough)


def build_pocket(shape):
    front = math.pi
    pv, pf, puv = patch(*POCKET, POCKET_RINGS, place_on(shape, front, POCKET_Z), (0.0, 1.0))
    pmat = leather_material("knockout_bag_pocket", kt.pocket_maps(512, 408, POCKET, FLAP_Y - FLAP[1]), 0.86)
    pocket = bs.mesh_object("KnockoutBagPocket", pv, pf, puv, pmat, smooth_angle=70)

    base = place_on(shape, front, POCKET_Z)

    def on_pocket(x, y, h):
        return base(x, y + FLAP_Y, h + pocket_height(x, y + FLAP_Y))

    fv, ff, fuv = patch(*FLAP, FLAP_RINGS, on_pocket, (0.0, 1.0))
    fmat = leather_material("knockout_bag_flap", kt.flap_maps(512, 200, FLAP), 0.86)
    flap = bs.mesh_object("KnockoutBagFlap", fv, ff, fuv, fmat, smooth_angle=70)
    return pocket, flap, on_pocket


def build_straps(shape):
    parts, places = [], []
    for side in (math.pi / 2, 3 * math.pi / 2):
        place = place_on(shape, side, STRAP_Z)
        parts.append(patch(*STRAP, STRAP_RINGS, place, (0.0, 1.0)))
        places.append(place)
    mat = leather_material("knockout_bag_tab", kt.strap_maps(128, 448, STRAP), 0.7)
    return bs.mesh_object("KnockoutBagTabs", *merge_meshes(parts), mat, smooth_angle=70), places


def frame_at(place, x, y, h):
    """Origin, outward normal and right / up axes of the bag's surface (or a patch's) at local x, y, raised h."""
    e = 1e-4
    o = place(np.array([x]), np.array([y]), np.array([h]))[0]
    dx = place(np.array([x + e]), np.array([y]), np.array([h]))[0] - place(np.array([x - e]), np.array([y]), np.array([h]))[0]
    dy = place(np.array([x]), np.array([y + e]), np.array([h]))[0] - place(np.array([x]), np.array([y - e]), np.array([h]))[0]
    right = dx / np.linalg.norm(dx)
    n = np.cross(right, dy)
    n /= np.linalg.norm(n)
    up = np.cross(n, right)
    return o, n, right, up


def torus(o, n, e1, e2, big, small, seg=16, ring=6):
    verts, faces, uvs = [], [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        for j in range(ring):
            b = 2 * math.pi * j / ring
            p = o + (big + small * math.cos(b)) * (math.cos(a) * e1 + math.sin(a) * e2) + small * math.sin(b) * n
            verts.append(tuple(map(float, p)))
    for i in range(seg):
        for j in range(ring):
            a, b = i * ring + j, i * ring + (j + 1) % ring
            c, d = ((i + 1) % seg) * ring + (j + 1) % ring, ((i + 1) % seg) * ring + j
            faces.append([a, d, c, b])
            uvs.append([(i / seg, j / ring), ((i + 1) / seg, j / ring), ((i + 1) / seg, (j + 1) / ring), (i / seg, (j + 1) / ring)])
    return verts, faces, uvs


def dome(o, n, e1, e2, radius, height, seg=14, rings=5):
    """A rivet head or snap: a low dome with a rolled edge, sunk a little into what it sits on."""
    verts = []
    prof = [(radius * 1.02, -0.0015)] + [(radius * math.sin(p), height * math.cos(p))
                                          for p in np.linspace(math.pi / 2, 0.12, rings)]
    for rr, hh in prof:
        for i in range(seg):
            a = 2 * math.pi * i / seg
            verts.append(tuple(map(float, o + rr * (math.cos(a) * e1 + math.sin(a) * e2) + hh * n)))
    faces, uvs = [], []
    for k in range(len(prof) - 1):
        for i in range(seg):
            j = (i + 1) % seg
            faces.append([k * seg + i, k * seg + j, (k + 1) * seg + j, (k + 1) * seg + i])
            uvs.append([(i / seg, k / rings), ((i + 1) / seg, k / rings), ((i + 1) / seg, (k + 1) / rings), (i / seg, (k + 1) / rings)])
    c = len(verts)
    verts.append(tuple(map(float, o + height * n)))
    last = (len(prof) - 1) * seg
    for i in range(seg):
        faces.append([last + i, last + (i + 1) % seg, c])
        uvs.append([(i / seg, 1), ((i + 1) / seg, 1), ((i + 0.5) / seg, 1)])
    return verts, faces, uvs


def build_metal(shape, on_pocket, strap_places):
    parts = []
    for th in shape.eyelets:
        o, n, e1, e2 = shape_frame(shape, th, EYELET_Z, 0.0016)
        parts.append(torus(o, n, e1, e2, 0.0088, 0.0029, seg=12, ring=5))
    for place in strap_places:
        for y in RIVET_Y:
            o, n, e1, e2 = frame_at(place, 0.0, y, STRAP_RINGS[-1][1])
            parts.append(dome(o, n, e1, e2, 0.0058, 0.0032))
    o, n, e1, e2 = frame_at(on_pocket, 0.0, SNAP_Y - FLAP_Y, FLAP_RINGS[-1][1])
    parts.append(dome(o, n, e1, e2, 0.0088, 0.0042, seg=18))
    albedo, height, px = kt.metal_maps()
    mat = br.make_material("knockout_bag_metal", albedo, kt.normal_from_height(height, px), rough=0.42, metal=0.85)
    return bs.mesh_object("KnockoutBagMetal", *merge_meshes(parts), mat, smooth_angle=60)


def shape_frame(shape, theta, z, h):
    p, n, dth = shape.on_surface(np.array([theta]), np.array([z]), np.array([h]))
    n, right = n[0], -dth[0]
    up = np.cross(n, right)
    return p[0], n, right, up


def rope_paths(shape):
    """The drawstring through the eyelets (outside between every other pair, behind the cloth between the rest),
    the knot at the front and its two tails with frayed ends."""
    paths = []
    step = 2 * math.pi / FOLDS
    th = np.linspace(0, 2 * math.pi, 241)
    s = np.mod((th - shape.eyelets[0]) / step, FOLDS)
    seg = np.floor(s).astype(int)
    frac = s - seg
    out = np.where(seg % 2 == 1, 1.0, -1.0)
    h = 0.004 + 0.011 * np.sin(np.pi * frac) * out
    p, _, _ = shape.on_surface(th, np.full_like(th, EYELET_Z), h)
    paths.append((p, ROPE_R, None, True))

    knot_z = EYELET_Z - 0.006
    o, n, right, up = shape_frame(shape, math.pi, knot_z, 0.0)
    t = np.linspace(0, 2 * math.pi, 121)
    k = 0.0125
    knot = (o[None] + right[None] * (k * (np.sin(t) + 2 * np.sin(2 * t)))[:, None]
            + up[None] * (k * (np.cos(t) - 2 * np.cos(2 * t)) * 0.8)[:, None]
            + n[None] * (0.019 + 0.5 * k * -np.sin(3 * t))[:, None])
    paths.append((knot, ROPE_R * 0.98, None, True))

    for side, length, splay in ((1, 0.098, 0.026), (-1, 0.086, 0.022)):
        u = np.linspace(0, 1, 22)
        x = side * (0.006 + splay * u ** 1.3) + 0.003 * np.sin(u * 4 * side)
        z = knot_z - 0.008 - length * u
        rho = shape.rho(shape.t_of_z(z))
        hh = ROPE_R * 1.05 + 0.017 * (1 - u) ** 2
        pts, _, _ = shape.on_surface(math.pi - x / rho, z, hh)
        paths.append((pts, ROPE_R * 0.92, lambda v: 1.0 - 0.3 * rt.smoothstep(0.8, 1.0, v), False))
        # the frayed end: a few loose strands fanning out below the tail
        tip = pts[-1]
        tn, tr = shape_frame(shape, math.pi - x[-1] / rho[-1], z[-1], 0.0)[1:3]
        down = np.cross(tr, tn)
        down = -down / np.linalg.norm(down) if down[2] > 0 else down / np.linalg.norm(down)
        rng = np.random.default_rng(97 + side)
        for f in range(6):
            fan = rng.uniform(-0.6, 0.6)
            ln = rng.uniform(0.008, 0.016)
            v = np.linspace(0, 1, 6)[:, None]
            strand = (tip[None] + down[None] * (v * ln) + tr[None] * (v * ln * fan)
                      + tn[None] * (v * rng.uniform(-0.002, 0.003)) + tr[None] * rng.uniform(-0.0025, 0.0025))
            paths.append((strand, 0.0011, lambda q: 1.0 - 0.5 * q, False))
    return paths


def build_rope(shape):
    rgb, height = st.rope_texture(seed=53, light=kt.ROPE_LIGHT, dark=kt.ROPE_DARK, strands=3)
    mat = br.make_material("knockout_bag_rope", rgb, st.normal_from_height(height * 0.0012, 4600), rough=0.85)
    parts = []
    for path, radius, taper, closed in rope_paths(shape):
        parts.append(bs.tube(path, radius, ring=7 if radius > 0.002 else 3, taper=taper, closed=closed))
    return bs.mesh_object("KnockoutBagRope", *merge_meshes(parts), mat, smooth_angle=80)


def build_bag():
    shape, band = CanvasShape(), BandShape()
    canvas = build_canvas(shape)
    base = build_band(band)
    pocket, flap, on_pocket = build_pocket(shape)
    tabs, places = build_straps(shape)
    metal = build_metal(shape, on_pocket, places)
    rope = build_rope(shape)
    return join([canvas, base, pocket, flap, tabs, metal, rope])


def main():
    br.reset_scene()
    bag = build_bag()
    outputs = {"knockout_bag": (bag, OUT)}
    br.export(bag, OUT)
    i = br.glb_info(OUT)
    print(f"wrote {OUT.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles, {i['images']} images, "
          f"{i['materials']} materials) bounds {np.round(i['min'], 4).tolist()} .. {np.round(i['max'], 4).tolist()}")
    if "--render" in sys.argv:
        import render_knockout_bag
        render_knockout_bag.render_all(outputs, Path(sys.argv[sys.argv.index("--render") + 1]), br)


if __name__ == "__main__":
    main()
