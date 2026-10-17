"""Build the Lift's model: the industrial hydraulic lift platform, with its deploy and extend animations.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/lift/lift.glb`` - the Lift, standing on its origin, with two glTF animations:

  - ``deploy`` (1.9 s, played once when a Lift is put down): the unit settles, the four outriggers swing
    out and down and lock with their pins, the column rises from its transport height to the resting
    2-unit height, the platform thuds into place and the amber lights flash, then hold.
  - ``extend`` (scrubbed, never played): the telescoping column. **Time in seconds = the height of the
    platform's deck above the ground, in metres**, so the game seeks it to the deck height it shows.
    It covers 0.45 m (transport) to 5.5 m (22 units).

  Two empty nodes carry values the game reads instead of transforms: ``ColumnDrive`` (its height is the
  deck height the deploy clip wants) and ``LightDrive`` (its height is the lights' brightness, 0 to 1).
  ``Deck`` marks the top of the grating, the surface a held Creation rests on. Every surface whose
  material is ``lift_light`` is a light the game brightens and dims.

Scale: 1 unit = 0.25 m. Collapsed footprint 4 x 4 units (1 m x 1 m), resting height 2 units (0.5 m).
Units are metres; in Blender Z is up, the front faces -Y, the base stands on Z=0.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/lift/build_lift.py                  # writes the .glb
    python tools/lift/build_lift.py --render OUT_DIR # also renders the review sheet into OUT_DIR

The .glb is committed; re-run this script after changing it and commit the result. The build is
deterministic (seeded noise only), so running it twice gives the same file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Euler, Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
sys.path.insert(0, str(Path(__file__).resolve().parent))
import lift_textures as lt  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "mods" / "vanilla" / "meshes" / "lift" / "lift.glb"
FPS = 30
UNIT = 0.25

# ---- Dimensions (metres) ----------------------------------------------------------------------------
H_TRANSPORT = 0.45          # deck height before the column comes up
H_REST = 0.50               # deck height at level 0 (2 units)
H_MAX = 5.50                # deck height at full extension (22 units)
STAGE_BOTTOM = 0.03         # where the nested stages stand inside the base
DECK_ABOVE_STAGE = 0.105    # head plate (0.015) + platform (0.09) above the top stage's top
STAGE_LEN = H_TRANSPORT - DECK_ABOVE_STAGE - STAGE_BOTTOM   # 0.315
STAGES = 20
STAGE_TRAVEL = (H_MAX - H_TRANSPORT) / STAGES               # 0.2525
R0, DR = 0.125, 0.0042      # outermost stage radius and the step between stages
BASE_HALF = 0.40            # housing half-width
DECK_Z = 0.215              # top of the base's deck plate
HINGE = (0.46, 0.46, 0.19)  # outrigger hinge of the back-right corner (the others are turned copies)
ARM_LEN = 0.40
DEPLOY_SECONDS = 1.9

CORNERS = {  # name: quarter turns (clockwise) from the back-right corner
    "BR": 0, "FR": 1, "FL": 2, "BL": 3,
}


# =====================================================================================================
# Geometry accumulation
# =====================================================================================================

class Geo:
    """Vertices, faces and per-face material names for one object, plus optional fixed UVs per face."""

    def __init__(self):
        self.v: list = []
        self.f: list = []
        self.m: list = []
        self.uv: list = []

    def add(self, verts, faces, mat, uvs=None):
        base = len(self.v)
        self.v.extend([tuple(map(float, p)) for p in verts])
        for i, face in enumerate(faces):
            self.f.append(tuple(base + k for k in face))
            self.m.append(mat)
            self.uv.append(None if uvs is None else uvs[i])
        return self

    def _replace(self, v, f, m, uv):
        self.v, self.f, self.m, self.uv = v, f, m, uv
        return self

    def extend(self, other: "Geo", matrix=None):
        verts = other.v if matrix is None else [tuple(matrix @ Vector(p)) for p in other.v]
        base = len(self.v)
        self.v.extend(verts)
        self.f.extend(tuple(base + k for k in face) for face in other.f)
        self.m.extend(other.m)
        self.uv.extend(other.uv)


def _from_bmesh(bm):
    bm.verts.ensure_lookup_table()
    verts = [tuple(v.co) for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return verts, faces


def box(g, c, half, mat, bevel=0.0, rot=None):
    """An axis-aligned (or turned by the 3x3 `rot`) box, its edges bevelled by `bevel` metres."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 2 * half[0], v.co.y * 2 * half[1], v.co.z * 2 * half[2]))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, 0.45 * min(half) * 2), segments=2,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    verts, faces = _from_bmesh(bm)
    m = Matrix(rot) if rot is not None else Matrix.Identity(3)
    verts = [tuple(m @ Vector(p) + Vector(c)) for p in verts]
    return g.add(verts, faces, mat)


def frame_of(axis):
    """Two unit vectors perpendicular to `axis` (and to each other)."""
    a = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    u = a.cross(ref).normalized()
    return u, a.cross(u).normalized()


def cylinder(g, p0, p1, r, mat, segs=16, cap0=True, cap1=True, r1=None, phase=0.0):
    """A cylinder (or cone frustum with r1) from p0 to p1."""
    p0, p1 = Vector(p0), Vector(p1)
    u, w = frame_of(p1 - p0)
    r1 = r if r1 is None else r1
    verts, faces = [], []
    for k in range(segs):
        a = 2 * math.pi * k / segs + phase
        d = u * math.cos(a) + w * math.sin(a)
        verts.append(p0 + d * r)
        verts.append(p1 + d * r1)
    for k in range(segs):
        a, b = 2 * k, 2 * ((k + 1) % segs)
        faces.append((a, b, b + 1, a + 1))
    if cap0:
        faces.append(tuple(2 * k for k in reversed(range(segs))))
    if cap1:
        faces.append(tuple(2 * k + 1 for k in range(segs)))
    return g.add(verts, faces, mat)


def ring(g, z0, z1, r_in, r_out, mat, segs=32, center=(0, 0)):
    """A thick vertical ring (washer/collar) around the Z axis through `center`."""
    cx, cy = center
    verts, faces = [], []
    for k in range(segs):
        a = 2 * math.pi * k / segs
        c, s = math.cos(a), math.sin(a)
        for r, z in ((r_in, z0), (r_out, z0), (r_out, z1), (r_in, z1)):
            verts.append((cx + r * c, cy + r * s, z))
    for k in range(segs):
        a, b = 4 * k, 4 * ((k + 1) % segs)
        faces += [(a, b, b + 1, a + 1), (a + 1, b + 1, b + 2, a + 2), (a + 2, b + 2, b + 3, a + 3), (a + 3, b + 3, b, a)]
    return g.add(verts, faces, mat)


def dome(g, p, n, r, mat, segs=8, height=None):
    """A low dome (rivet head) at p facing along n."""
    p, n = Vector(p), Vector(n).normalized()
    u, w = frame_of(n)
    h = r * 0.6 if height is None else height
    verts = [tuple(p + n * h)]
    rings = [(1.0, 0.0), (0.72, 0.7)]
    for rf, hf in rings:
        for k in range(segs):
            a = 2 * math.pi * k / segs
            verts.append(tuple(p + (u * math.cos(a) + w * math.sin(a)) * r * rf + n * h * hf))
    faces = []
    for k in range(segs):
        k1 = (k + 1) % segs
        faces.append((1 + k, 1 + k1, 1 + segs + k1, 1 + segs + k))
        faces.append((1 + segs + k, 1 + segs + k1, 0))
    return g.add(verts, faces, mat)


def hexnut(g, p, n, r, h, mat):
    p, n = Vector(p), Vector(n).normalized()
    return cylinder(g, p, p + n * h, r, mat, segs=6, phase=math.pi / 6)


def sweep(g, points, r, mat, segs=10):
    """A tube through `points` (a hose), with parallel-transported frames and capped ends."""
    pts = [Vector(p) for p in points]
    tangents = []
    for i in range(len(pts)):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        tangents.append((b - a).normalized())
    u, _ = frame_of(tangents[0])
    verts, faces = [], []
    for i, (p, t) in enumerate(zip(pts, tangents)):
        u = (u - t * u.dot(t)).normalized()
        w = t.cross(u)
        for k in range(segs):
            a = 2 * math.pi * k / segs
            verts.append(tuple(p + (u * math.cos(a) + w * math.sin(a)) * r))
    for i in range(len(pts) - 1):
        for k in range(segs):
            k1 = (k + 1) % segs
            faces.append((i * segs + k, i * segs + k1, (i + 1) * segs + k1, (i + 1) * segs + k))
    faces.append(tuple(reversed(range(segs))))
    faces.append(tuple((len(pts) - 1) * segs + k for k in range(segs)))
    return g.add(verts, faces, mat)


def bezier(p0, p1, p2, p3, n=14):
    p0, p1, p2, p3 = map(np.asarray, (p0, p1, p2, p3))
    t = np.linspace(0, 1, n)[:, None]
    return list((1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1 + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3)


def quad(g, corners, mat, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
    """One quad with fixed UVs (decals)."""
    return g.add(corners, [(0, 1, 2, 3)], mat, uvs=[uv])


def rivet_row(g, a, b, n, normal, r=0.0055, mat="dark_steel"):
    a, b = Vector(a), Vector(b)
    for k in range(n):
        t = k / max(n - 1, 1)
        dome(g, a.lerp(b, t), normal, r, mat)


def rz(quarter_turns):
    return Matrix.Rotation(-math.pi / 2 * quarter_turns, 4, "Z")


# =====================================================================================================
# Parts
# =====================================================================================================

def side_plate(g, face, kind):
    """An armour plate on one face of the base housing, built for the +X face and turned onto `face`
    (quarter turns clockwise). kind: 'armor', 'hatch' or 'vent'."""
    local = Geo()
    x = BASE_HALF
    z0, z1 = 0.062, 0.176
    w = 0.29
    if kind == "hatch":
        # recessed access hatch with bolts, a handle and the warning decal beside it
        box(local, (x + 0.004, -0.07, (z0 + z1) / 2), (0.006, 0.19, (z1 - z0) / 2), "gunmetal", bevel=0.004)
        box(local, (x + 0.011, -0.07, (z0 + z1) / 2), (0.004, 0.15, 0.040), "gunmetal", bevel=0.003)
        for (dy, dz) in ((-0.205, 0.07), (0.065, 0.07), (-0.205, 0.168), (0.065, 0.168)):
            hexnut(local, (x + 0.009, dy, dz), (1, 0, 0), 0.007, 0.006, "dark_steel")
        box(local, (x + 0.022, -0.07, 0.122), (0.006, 0.05, 0.006), "dark_steel", bevel=0.002)
        for dy in (-0.115, -0.025):
            box(local, (x + 0.017, dy, 0.122), (0.006, 0.005, 0.006), "dark_steel")
        # warning decal
        y0, y1, dz0, dz1 = 0.135, 0.255, 0.075, 0.165
        xd = x + 0.0035
        quad(local, [(xd, y0, dz0), (xd, y1, dz0), (xd, y1, dz1), (xd, y0, dz1)], "decal_warning")
        box(local, (x + 0.001, (y0 + y1) / 2, (dz0 + dz1) / 2), (0.002, (y1 - y0) / 2 + 0.006, (dz1 - dz0) / 2 + 0.006), "dark_steel")
    else:
        box(local, (x + 0.006, 0, (z0 + z1) / 2), (0.006, w, (z1 - z0) / 2), "gunmetal", bevel=0.004)
        # riveted border
        rivet_row(local, (x + 0.012, -w + 0.02, z0 + 0.012), (x + 0.012, w - 0.02, z0 + 0.012), 9, (1, 0, 0))
        rivet_row(local, (x + 0.012, -w + 0.02, z1 - 0.012), (x + 0.012, w - 0.02, z1 - 0.012), 9, (1, 0, 0))
        if kind == "vent":
            for k in range(7):
                yy = -0.13 + k * 0.043
                box(local, (x + 0.013, yy, (z0 + z1) / 2), (0.003, 0.012, 0.035), "dark_steel", bevel=0.002)
        else:
            # small label plate
            y0, y1, dz0, dz1 = -0.07, 0.07, 0.092, 0.146
            xd = x + 0.0125
            box(local, (x + 0.0115, 0, (dz0 + dz1) / 2), (0.001, 0.075, 0.03), "dark_steel")
            quad(local, [(xd + 0.0006, y0, dz0), (xd + 0.0006, y1, dz0), (xd + 0.0006, y1, dz1), (xd + 0.0006, y0, dz1)], "decal_label")
    # hazard band along the top edge
    box(local, (x + 0.004, 0, 0.191), (0.004, BASE_HALF - 0.045, 0.008), "hazard")
    g.extend(local, rz(face))


def build_base():
    g = Geo()
    box(g, (0, 0, 0.02), (0.43, 0.43, 0.02), "dark_steel", bevel=0.006)               # plinth
    box(g, (0, 0, 0.12), (BASE_HALF, BASE_HALF, 0.08), "gunmetal", bevel=0.008)       # housing
    box(g, (0, 0, 0.2075), (0.39, 0.39, 0.0075), "dark_steel", bevel=0.003)           # deck plate
    for face, kind in ((0, "armor"), (1, "hatch"), (2, "armor"), (3, "vent")):
        side_plate(g, face, kind)
    for q in range(4):
        corner = Geo()
        cx = cy = 0.385
        box(corner, (cx, cy, 0.128), (0.045, 0.045, 0.098), "gunmetal", bevel=0.007)  # corner post
        box(corner, (cx, cy, 0.231), (0.04, 0.04, 0.005), "orange", bevel=0.002)
        cylinder(corner, (cx, cy, 0.236), (cx, cy, 0.245), 0.02, "dark_steel", segs=16)
        dome(corner, (cx, cy, 0.245), (0, 0, 1), 0.016, "lift_light", segs=12, height=0.014)
        rivet_row(corner, (cx + 0.046, cy - 0.03, 0.06), (cx + 0.046, cy - 0.03, 0.2), 4, (1, 0, 0))
        rivet_row(corner, (cx - 0.03, cy + 0.046, 0.06), (cx - 0.03, cy + 0.046, 0.2), 4, (0, 1, 0))
        # outrigger swivel boss and its gusset
        hx, hy, hz = HINGE
        box(corner, ((cx + hx) / 2 + 0.005, (cy + hy) / 2 + 0.005, hz - 0.045), (0.05, 0.05, 0.012), "gunmetal", bevel=0.004,
            rot=Matrix.Rotation(math.pi / 4, 3, "Z"))
        cylinder(corner, (hx, hy, hz - 0.06), (hx, hy, hz - 0.032), 0.036, "dark_steel", segs=20)
        g.extend(corner, rz(q))
    # sleeve with its bolted flange, top collar and ribs
    cylinder(g, (0, 0, DECK_Z), (0, 0, 0.330), 0.135, "gunmetal", segs=40)
    ring(g, DECK_Z, DECK_Z + 0.02, 0.13, 0.178, "dark_steel", segs=40)
    ring(g, 0.326, 0.345, 0.127, 0.146, "dark_steel", segs=40)
    for k in range(10):
        a = 2 * math.pi * (k + 0.5) / 10
        hexnut(g, (0.162 * math.cos(a), 0.162 * math.sin(a), DECK_Z + 0.02), (0, 0, 1), 0.0065, 0.006, "dark_steel")
    for k in range(4):
        a = 2 * math.pi * k / 4
        rib = Matrix.Rotation(a, 3, "Z")
        box(g, tuple(rib @ Vector((0.15, 0, 0.268))), (0.016, 0.005, 0.03), "gunmetal", rot=rib)
    # drive rams' barrels on the diagonals
    for q in range(4):
        a = math.pi / 4 + q * math.pi / 2
        x, y = 0.22 * math.cos(a), 0.22 * math.sin(a)
        cylinder(g, (x, y, DECK_Z), (x, y, 0.262), 0.024, "gunmetal", segs=20)
        ring(g, 0.255, 0.27, 0.0125, 0.027, "dark_steel", segs=20, center=(x, y))
        ring(g, DECK_Z, DECK_Z + 0.012, 0.02, 0.034, "dark_steel", segs=20, center=(x, y))
        # pressure line from the pump to the barrel's foot
        side = Vector((-math.sin(a), math.cos(a), 0))
        start = Vector((0.09 * (1 if x > 0 else -1), 0.27, 0.27))
        end = Vector((x, y, 0.24)) - Vector((math.cos(a), math.sin(a), 0)) * 0.022
        hexnut(g, end, -Vector((math.cos(a), math.sin(a), 0)), 0.011, 0.012, "dark_steel")
        mid = (start + end) / 2 + Vector((0, 0, 0.05)) + side * 0.02
        sweep(g, bezier(start, start + Vector((0, -0.02, 0.05)), mid, end - Vector((math.cos(a), math.sin(a), 0)) * 0.04), 0.0085, "rubber")
    # hydraulic pump and reservoir at the back
    box(g, (0, 0.30, 0.255), (0.12, 0.05, 0.04), "gunmetal", bevel=0.006)
    for k in range(6):
        box(g, (-0.09 + k * 0.036, 0.352, 0.255), (0.006, 0.004, 0.032), "dark_steel")
    cylinder(g, (-0.11, 0.30, 0.275), (0.11, 0.30, 0.275), 0.03, "orange", segs=20)
    for x in (-0.09, 0.09):
        hexnut(g, (x, 0.27, 0.27), (0, -1, 0), 0.012, 0.014, "dark_steel")
    # valve block with a gauge at the front
    box(g, (0.0, -0.29, 0.245), (0.07, 0.04, 0.03), "gunmetal", bevel=0.005)
    cylinder(g, (0.0, -0.33, 0.25), (0.0, -0.336, 0.25), 0.022, "dark_steel", segs=20)
    quad(g, [(-0.018, -0.3365, 0.232), (0.018, -0.3365, 0.232), (0.018, -0.3365, 0.268), (-0.018, -0.3365, 0.268)], "decal_gauge")
    for x in (-0.05, 0.05):
        cylinder(g, (x, -0.29, 0.275), (x, -0.29, 0.29), 0.01, "orange", segs=12)
    sweep(g, bezier((0.04, -0.27, 0.25), (0.08, -0.15, 0.30), (0.10, 0.10, 0.30), (0.05, 0.25, 0.27)), 0.008, "rubber")
    return g


def build_stage(i):
    """Stage i of the telescoping column, origin at its bottom."""
    g = Geo()
    r = R0 - i * DR
    L = STAGE_LEN
    cylinder(g, (0, 0, 0), (0, 0, L - 0.02), r, "chrome", segs=40, cap0=False, cap1=False)
    ring(g, L - 0.022, L, r - 0.003, r + 0.0062, "dark_steel", segs=40)          # locking collar
    for k in range(2):
        a = math.pi * k + (math.pi / 2 if i % 2 else 0)
        c = Matrix.Rotation(a, 3, "Z")
        box(g, tuple(c @ Vector((r + 0.007, 0, L - 0.011))), (0.004, 0.009, 0.012), "dark_steel", rot=c)  # guide shoes
    for k in range(4):
        a = 2 * math.pi * (k + 0.5) / 4
        dome(g, (r * math.cos(a) * 1.0 + 0.0062 * math.cos(a), (r + 0.0062) * math.sin(a), L - 0.011),
             (math.cos(a), math.sin(a), 0), 0.0035, "chrome", segs=6)
    if i == 0:
        # the drive yoke the four rams push, and their rods
        for q in range(4):
            a = math.pi / 4 + q * math.pi / 2
            rot = Matrix.Rotation(a, 3, "Z")
            box(g, tuple(rot @ Vector((0.175, 0, L - 0.055))), (0.065, 0.024, 0.011), "gunmetal", bevel=0.004, rot=rot)
            x, y = 0.22 * math.cos(a), 0.22 * math.sin(a)
            cylinder(g, (x, y, L - 0.068), (x, y, L - 0.042), 0.022, "dark_steel", segs=16)
            cylinder(g, (x, y, L - 0.068 - 0.30), (x, y, L - 0.068), 0.012, "chrome", segs=16, cap0=False)
        ring(g, L - 0.066, L - 0.044, r, r + 0.03, "gunmetal", segs=40)
    return g


def build_platform():
    """The platform, origin at the top stage's top; the deck (top of the grating) is at DECK_ABOVE_STAGE."""
    g = Geo()
    deck = DECK_ABOVE_STAGE
    bottom = 0.015
    half = 0.5
    cylinder(g, (0, 0, 0), (0, 0, bottom), 0.10, "dark_steel", segs=32)                 # head plate
    for k in range(6):
        a = 2 * math.pi * k / 6
        hexnut(g, (0.082 * math.cos(a), 0.082 * math.sin(a), 0.0), (0, 0, -1), 0.007, 0.006, "dark_steel")
    # cross beams under the grating and gussets down to the head plate
    for rot in (Matrix.Identity(3), Matrix.Rotation(math.pi / 2, 3, "Z")):
        box(g, tuple(rot @ Vector((0, 0, 0.045))), (0.43, 0.022, 0.03), "dark_steel", bevel=0.003, rot=rot)
    for q in range(4):
        a = math.pi / 4 + q * math.pi / 2
        rot = Matrix.Rotation(a, 3, "Z") @ Matrix.Rotation(math.radians(-24), 3, "Y")
        box(g, tuple(Matrix.Rotation(a, 3, "Z") @ Vector((0.17, 0, 0.045))), (0.11, 0.008, 0.012), "gunmetal", rot=rot)
    # grating: bars both ways between the frame beams
    inner = half - 0.07
    n = 20
    pitch = 2 * inner / n
    for k in range(1, n):
        c = -inner + k * pitch
        box(g, (c, 0, deck - 0.015), (0.0028, inner, 0.015), "dark_steel")
        box(g, (0, c, deck - 0.012), (inner, 0.0028, 0.012), "dark_steel")
    # centre plate with its raised X, like the sheet's top view
    box(g, (0, 0, deck - 0.006), (0.11, 0.11, 0.008), "gunmetal", bevel=0.003)
    for a in (math.pi / 4, -math.pi / 4):
        rot = Matrix.Rotation(a, 3, "Z")
        box(g, (0, 0, deck + 0.004), (0.135, 0.008, 0.004), "dark_steel", bevel=0.0015, rot=rot)
    for (x, y) in ((0.09, 0.09), (-0.09, 0.09), (0.09, -0.09), (-0.09, -0.09)):
        hexnut(g, (x, y, deck + 0.002), (0, 0, 1), 0.006, 0.004, "dark_steel")
    # frame beams with the raised hazard-striped safety lip, rivets on the outer faces
    for q in range(4):
        side = Geo()
        box(side, (half - 0.035, 0, (bottom + deck) / 2), (0.035, half - 0.06, (deck - bottom) / 2), "gunmetal", bevel=0.005)
        box(side, (half - 0.026, 0, deck + 0.011), (0.026, half - 0.065, 0.011), "hazard", bevel=0.003)
        box(side, (half - 0.074, 0, deck - 0.004), (0.010, half - 0.065, 0.004), "dark_steel")
        rivet_row(side, (half + 0.0005, -0.36, bottom + 0.018), (half + 0.0005, 0.36, bottom + 0.018), 10, (1, 0, 0))
        rivet_row(side, (half + 0.0005, -0.36, deck - 0.018), (half + 0.0005, 0.36, deck - 0.018), 10, (1, 0, 0))
        # corner block with an amber light on its outer corner
        cx = cy = half - 0.065
        box(side, (cx, cy, (0.008 + deck + 0.032) / 2), (0.065, 0.065, (deck + 0.032 - 0.008) / 2), "gunmetal", bevel=0.01)
        box(side, (cx, cy, deck + 0.034), (0.052, 0.052, 0.004), "orange", bevel=0.002)
        for (dx, dy) in ((-0.035, -0.035), (0.035, -0.035), (-0.035, 0.035)):
            hexnut(side, (cx + dx, cy + dy, deck + 0.037), (0, 0, 1), 0.006, 0.004, "dark_steel")
        d = Vector((1, 1, 0)).normalized()
        lamp = Vector((cx, cy, deck - 0.03)) + d * (0.065 * math.sqrt(2) - 0.012)
        box(side, tuple(lamp), (0.022, 0.006, 0.014), "dark_steel", rot=Matrix.Rotation(-math.pi / 4, 3, "Z"))
        box(side, tuple(lamp + d * 0.006), (0.017, 0.004, 0.009), "lift_light", rot=Matrix.Rotation(-math.pi / 4, 3, "Z"))
        g.extend(side, rz(q))
    return g


# ---- Outriggers (built for the back-right corner; the hinge is the origin) --------------------------

def arm_points():
    """Arm-local (x along the arm, z up) points: the knee, the tip (foot pivot)."""
    knee = Vector((0.27, 0, 0))
    bend = math.radians(-32)
    tip = knee + Vector((math.cos(bend), 0, math.sin(bend))) * 0.14
    return knee, tip


FOOT_DROP = 0.064            # foot pivot to the bottom of the pad


def deployed_tilt():
    """Tilt (radians, positive = down) at which the pad stands on the ground."""
    _, tip = arm_points()
    hz = HINGE[2]
    lo, hi = 0.0, 0.8
    for _ in range(60):
        a = (lo + hi) / 2
        z = hz + (Matrix.Rotation(a, 3, "Y") @ tip).z - FOOT_DROP
        if z > 0:
            lo = a
        else:
            hi = a
    return (lo + hi) / 2


def build_swivel():
    g = Geo()
    cylinder(g, (0, 0, -0.03), (0, 0, 0.03), 0.03, "gunmetal", segs=20)              # swivel drum
    for y in (-0.042, 0.042):                                                          # clevis cheeks
        box(g, (0.02, y, 0.0), (0.04, 0.007, 0.03), "gunmetal", bevel=0.003)
        cylinder(g, (0.0, y, 0), (0.0, y + (0.012 if y > 0 else -0.012), 0), 0.016, "dark_steel", segs=16)
    box(g, (0.02, 0, 0.06), (0.018, 0.016, 0.012), "gunmetal", bevel=0.003)          # ram lug
    box(g, (-0.03, 0, 0.05), (0.014, 0.016, 0.02), "orange", bevel=0.003)             # pin guide
    return g


def build_pin():
    g = Geo()
    cylinder(g, (-0.03, 0, -0.02), (-0.03, 0, 0.09), 0.0065, "chrome", segs=12)
    cylinder(g, (-0.03, 0, 0.09), (-0.03, 0, 0.098), 0.012, "orange", segs=14)
    return g


def build_arm():
    g = Geo()
    knee, tip = arm_points()
    # tapered box beam to the knee, then the bent end
    bm_len = knee.x
    box(g, (bm_len / 2 + 0.01, 0, 0.0), (bm_len / 2 + 0.01, 0.03, 0.028), "gunmetal", bevel=0.006)
    bend = math.atan2(tip.z - knee.z, tip.x - knee.x)
    mid = (knee + tip) / 2
    box(g, tuple(mid), (0.085, 0.027, 0.025), "gunmetal", bevel=0.006, rot=Matrix.Rotation(-bend, 3, "Y"))
    cylinder(g, (knee.x, -0.031, 0), (knee.x, 0.031, 0), 0.029, "gunmetal", segs=20)
    # orange side panels and a hazard stripe on top
    for y in (-0.031, 0.031):
        box(g, (0.15, y, 0.0), (0.09, 0.002, 0.016), "orange", bevel=0.001)
        rivet_row(g, (0.05, y * 1.07, 0.018), (0.25, y * 1.07, 0.018), 5, (0, 1 if y > 0 else -1, 0), r=0.0045)
        rivet_row(g, (0.05, y * 1.07, -0.018), (0.25, y * 1.07, -0.018), 5, (0, 1 if y > 0 else -1, 0), r=0.0045)
    box(g, (0.17, 0, 0.0285), (0.06, 0.02, 0.002), "hazard")
    cylinder(g, (0, -0.036, 0), (0, 0.036, 0), 0.022, "dark_steel", segs=20)           # hinge knuckle
    box(g, (0.17, 0, 0.034), (0.012, 0.012, 0.008), "gunmetal")                        # ram lug
    cylinder(g, (tip.x, -0.03, tip.z), (tip.x, 0.03, tip.z), 0.022, "dark_steel", segs=16)
    return g


def build_foot():
    g = Geo()
    dome(g, (0, 0, -0.012), (0, 0, 1), 0.022, "chrome", segs=12, height=0.016)
    cylinder(g, (0, 0, -0.012), (0, 0, -0.034), 0.016, "dark_steel", segs=14)
    cylinder(g, (0, 0, -0.034), (0, 0, -0.044), 0.05, "orange", segs=24, r1=0.042)
    cylinder(g, (0, 0, -0.064), (0, 0, -0.044), 0.054, "rubber", segs=24)
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        hexnut(g, (0.03 * math.cos(a), 0.03 * math.sin(a), -0.0355), (0, 0, 1), 0.005, 0.004, "dark_steel")
    return g


RAM_A = Vector((0.02, 0, 0.06))      # ram eye on the swivel
RAM_B = Vector((0.17, 0, 0.036))     # ram eye on the arm (arm-local)
ROD_LEN = 0.10


def build_ram():
    g = Geo()
    cylinder(g, (-0.012, 0, 0), (0.085, 0, 0), 0.012, "gunmetal", segs=14)
    cylinder(g, (-0.012, -0.012, 0), (-0.012, 0.012, 0), 0.01, "dark_steel", segs=12)
    ring_rot = Matrix.Rotation(math.pi / 2, 4, "Y")
    gland = Geo()
    ring(gland, -0.008, 0.0, 0.006, 0.0135, "dark_steel", segs=14)
    g.extend(gland, Matrix.Translation((0.085, 0, 0)) @ ring_rot.inverted() @ Matrix.Rotation(math.pi, 4, "Y"))
    return g


def build_rod():
    g = Geo()
    cylinder(g, (0, 0, 0), (ROD_LEN, 0, 0), 0.006, "chrome", segs=12)
    cylinder(g, (ROD_LEN, -0.01, 0), (ROD_LEN, 0.01, 0), 0.009, "dark_steel", segs=12)
    return g


# =====================================================================================================
# Poses
# =====================================================================================================

def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def window(t, a, b):
    return smooth((t - a) / (b - a))


def keyed(t, keys):
    """Piecewise smooth interpolation through (time, value) keys."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * smooth((t - t0) / (t1 - t0))
    return keys[-1][1]


TILT = None


def deploy_pose(t):
    """Every deploy-animated value at time t (seconds)."""
    global TILT
    if TILT is None:
        TILT = deployed_tilt()
    pose = {}
    pose["chassis_z"] = keyed(t, [(0.0, 0.05), (0.22, 0.0), (0.27, 0.006), (0.33, 0.0), (0.88, 0.0), (0.91, -0.004),
                                  (0.97, 0.0), (1.47, 0.0), (1.50, -0.003), (1.56, 0.0),
                                  (1.64, 0.0018), (1.69, -0.0012), (1.74, 0.0006), (1.80, 0.0)])
    pose["swing"] = window(t, 0.25, 0.75)
    pose["tilt"] = TILT * window(t, 0.42, 0.86)
    pose["pin"] = 0.045 * (1 - window(t, 0.86, 0.95))
    pose["column"] = keyed(t, [(0.0, H_TRANSPORT), (0.98, H_TRANSPORT), (1.40, H_REST + 0.004), (1.48, H_REST)])
    pose["light"] = keyed(t, [(0.0, 0.0), (1.47, 0.0), (1.50, 1.0), (1.55, 1.0), (1.58, 0.1), (1.62, 1.0), (1.66, 1.0),
                              (1.69, 0.1), (1.74, 0.85)])
    return pose


def stage_extension(i, h):
    """How far stage i stands out of its parent at deck height h (the outermost stage goes first)."""
    total = max(0.0, h - H_TRANSPORT)
    return min(STAGE_TRAVEL, max(0.0, total - i * STAGE_TRAVEL))


def outrigger_transforms(q, swing, tilt, pin):
    """Local transforms (swivel yaw, arm tilt, foot counter-tilt, ram angle, rod slide, pin drop)."""
    yaw0 = math.radians(-90 - 90 * q)
    yaw1 = math.radians(45 - 90 * q)
    yaw = yaw0 + (yaw1 - yaw0) * swing
    b = Matrix.Rotation(tilt, 3, "Y") @ RAM_B
    d = b - RAM_A
    ram_angle = -math.atan2(d.z, d.x)
    return yaw, tilt, -tilt, ram_angle, d.length - ROD_LEN, pin


# =====================================================================================================
# Scene
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = int(math.ceil(H_MAX * FPS))


def make_object(name, g: Geo, materials, parent=None, location=(0, 0, 0)):
    # fan-triangulate n-gons (caps): the exporter's tangents need triangles and quads
    faces, mats, uvs = [], [], []
    for face, mat, uv in zip(g.f, g.m, g.uv):
        if len(face) <= 4:
            faces.append(face), mats.append(mat), uvs.append(uv)
            continue
        for k in range(1, len(face) - 1):
            faces.append((face[0], face[k], face[k + 1])), mats.append(mat), uvs.append(None)
    g = Geo()._replace(g.v, faces, mats, uvs)
    me = bpy.data.meshes.new(name)
    me.from_pydata(g.v, [], g.f)
    me.validate(clean_customdata=False)
    names = list(dict.fromkeys(g.m))
    for n in names:
        me.materials.append(materials[n])
    me.polygons.foreach_set("material_index", [names.index(n) for n in g.m])
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(38))
    lt.box_uvs(me, g.uv)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location = location
    return ob


def make_empty(name, parent=None, location=(0, 0, 0)):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    ob.location = location
    return ob


def build_scene():
    reset_scene()
    mats = lt.make_materials()
    rig = {}
    root = rig["root"] = make_empty("Lift")
    chassis = rig["chassis"] = make_empty("Chassis", root)
    make_object("Base", build_base(), mats, chassis)
    parent = chassis
    stages = []
    for i in range(STAGES):
        ob = make_object(f"Stage{i:02d}", build_stage(i), mats, parent, (0, 0, STAGE_BOTTOM if i == 0 else 0))
        stages.append(ob)
        parent = ob
    rig["stages"] = stages
    platform = rig["platform"] = make_object("Platform", build_platform(), mats, parent, (0, 0, STAGE_LEN))
    make_empty("Deck", platform, (0, 0, DECK_ABOVE_STAGE))
    rig["outriggers"] = []
    swivel_g, arm_g, foot_g, ram_g, rod_g, pin_g = build_swivel(), build_arm(), build_foot(), build_ram(), build_rod(), build_pin()
    _, tip = arm_points()
    for name, q in CORNERS.items():
        at = rz(q) @ Vector(HINGE)
        swivel = make_object(f"Outrigger{name}", swivel_g, mats, chassis, tuple(at))
        swivel.rotation_mode = "XYZ"
        arm = make_object(f"OutriggerArm{name}", arm_g, mats, swivel)
        foot = make_object(f"OutriggerFoot{name}", foot_g, mats, arm, tuple(tip))
        ram = make_object(f"OutriggerRam{name}", ram_g, mats, swivel, tuple(RAM_A))
        rod = make_object(f"OutriggerRod{name}", rod_g, mats, ram)
        pin = make_object(f"OutriggerPin{name}", pin_g, mats, swivel)
        rig["outriggers"].append((q, swivel, arm, foot, ram, rod, pin))
    rig["column_drive"] = make_empty("ColumnDrive", root, (0, 0, H_REST))
    rig["light_drive"] = make_empty("LightDrive", root, (0, 0, 0.85))
    return rig, mats


def apply_deploy(rig, t):
    p = deploy_pose(t)
    rig["chassis"].location.z = p["chassis_z"]
    for (q, swivel, arm, foot, ram, rod, pin) in rig["outriggers"]:
        yaw, tilt, foot_tilt, ram_angle, rod_x, pin_z = outrigger_transforms(q, p["swing"], p["tilt"], p["pin"])
        swivel.rotation_euler = Euler((0, 0, yaw))
        arm.rotation_euler = Euler((0, tilt, 0))
        foot.rotation_euler = Euler((0, foot_tilt, 0))
        ram.rotation_euler = Euler((0, ram_angle, 0))
        rod.location = (rod_x, 0, 0)
        pin.location = (0, 0, pin_z)
    rig["column_drive"].location.z = p["column"]
    rig["light_drive"].location.z = p["light"]
    return p


def apply_extend(rig, h):
    for i, ob in enumerate(rig["stages"]):
        ob.location.z = (STAGE_BOTTOM if i == 0 else 0.0) + stage_extension(i, h)


def deploy_objects(rig):
    obs = [rig["chassis"], rig["column_drive"], rig["light_drive"]]
    for (_, swivel, arm, foot, ram, rod, pin) in rig["outriggers"]:
        obs += [swivel, arm, foot, ram, rod, pin]
    return obs


def push_track(ob, action, name):
    ad = ob.animation_data
    track = ad.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 0, action)
    strip.action_frame_start, strip.action_frame_end = action.frame_range
    ad.action = None


def bake(rig):
    """Keys the deploy clip every frame and the extend clip at each stage's start and end."""
    deploy = deploy_objects(rig)
    actions = {}
    for ob in deploy:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"deploy_{ob.name}")
        ob.animation_data.action = act
        actions[ob.name] = act
    frames = int(round(DEPLOY_SECONDS * FPS))
    for f in range(frames + 1):
        apply_deploy(rig, f / FPS)
        for ob in deploy:
            ob.keyframe_insert("location", frame=f)
            ob.keyframe_insert("rotation_euler", frame=f)
    for ob in deploy:
        lt.linear_keys(ob.animation_data.action)
        push_track(ob, actions[ob.name], "deploy")
    apply_deploy(rig, DEPLOY_SECONDS)

    for i, ob in enumerate(rig["stages"]):
        ob.animation_data_create()
        act = bpy.data.actions.new(f"extend_{ob.name}")
        ob.animation_data.action = act
        times = sorted({0.0, H_TRANSPORT + i * STAGE_TRAVEL, H_TRANSPORT + (i + 1) * STAGE_TRAVEL, H_MAX})
        for h in times:
            apply_extend(rig, h)
            ob.keyframe_insert("location", frame=h * FPS)
        lt.linear_keys(act)
        push_track(ob, act, "extend")
    apply_extend(rig, H_REST)


def export(rig, path):
    for ob in bpy.context.scene.objects:
        ob.select_set(ob.name not in ("RenderOnly",))
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_animations=True, export_animation_mode="NLA_TRACKS", export_force_sampling=False,
        export_optimize_animation_size=False, export_anim_slide_to_zero=False, export_def_bones=False,
        export_morph=False, export_skins=False, export_draco_mesh_compression_enable=False, export_tangents=True,
        export_image_format="JPEG", export_jpeg_quality=90, export_image_quality=90, export_extras=False,
        export_cameras=False, export_lights=False)


def main():
    rig, mats = build_scene()
    bake(rig)
    export(rig, OUT)
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in bpy.context.scene.objects if ob.type == "MESH")
    print(f"wrote {OUT.relative_to(REPO)} ({OUT.stat().st_size // 1024} KiB, {tris} triangles, {STAGES} stages, "
          f"tilt {math.degrees(deployed_tilt()):.1f} deg)")
    if "--render" in sys.argv:
        import render_lift
        render_lift.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
