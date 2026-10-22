"""The parts map's batch 17b farming part (see part_kit.py and station_kit.py): the Growbed, soil built onto a Creation.

**Settler Patchwork** (``docs/art-direction.md``: survivor salvage, stylised, not cartoony, framed construction, small
bevels): a raised bed of mismatched scrap planks held by dark timber corner posts, one faded sea-teal colony board and
a zinc patch plate among them, a dark iron strap round its middle on zinc screws, a plank rim, two runners underneath, and a
salvaged zinc junction box on its right side where the Battery Container's link plugs in. The box's pip is the only
cyan (art rule: cyan only on what has power): it lights while the bed is powered and its crop grows. A zinc drip line
runs round the inside of the rim from the box. The soil fill is the soil mound's dry texture on a lumpy, slightly
crowned heightfield, its mesh named ``Soil`` so the game darkens only it as it gets wet and stands the crop on its top
(``SoilLooks.SoilMeshes``).

Axes are Blender's: x right, z up and the part's front along -y (glTF +z). The part is 5 x 2 x 5 blocks (1.25 x 0.5 x
1.25 m) and stands on the bottom of that box. ``build_growbed(m)`` returns a rig for ``station_kit.run``: ``on`` and
``off`` clips on its ``pose`` readout (the pip), and the unpowered and powered states for the review (the icon shows it
powered). ``review_planted`` renders it planted at a few crop stages with the crops' own plant models
(``meshes/crops/*_plant.glb``, their ``grow`` clip at the stage) for the batch lineup.

Run ``tools/growbed/build_growbed.py``, not this file.
"""

from __future__ import annotations

import math
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

import part_kit as K
import station_kit as S
import working_kit as W
from part_kit import Geo, box, cylinder

lt = K.lt
sys.path.insert(0, str(K.TOOLS / "soil"))
sys.path.insert(0, str(K.TOOLS / "rocks"))
import build_rocks as br  # noqa: E402  (make_material, as the soil mound's)
import rock_textures as rt  # noqa: E402
import soil_textures as st  # noqa: E402

HALF = 0.625                 # half the bed's width and depth (5 blocks)
FLOOR, TOP = -0.25, 0.25     # the part's bottom and top (2 blocks)
POST = 0.035                 # half a corner post's side
WALL = 0.013                 # half a wall plank's thickness
INNER = HALF - 2 * WALL - 0.01   # half the soil's square
SOIL_Z = TOP - 0.055         # the soil's level surface (its clods stand a little proud)
SOIL_TEX = 512
CROPS = K.REPO / "mods" / "vanilla" / "meshes" / "crops"


def materials():
    m = S.materials()
    albedo, height = st.mound_maps(SOIL_TEX, INNER, 963, lambda x, z: np.full_like(x, 0.06))
    px = SOIL_TEX / (2 * INNER)
    albedo = st.lit(albedo, height, px, shade=0.45)
    # weathered grey boards and darker timber for the posts, so the salvaged planks read as mismatched
    m["plank_grey"] = lt.textured("f_plank_grey", K.half_res(K.plank_surface(1711, "#857865")), normal_strength=1.5)
    m["timber"] = lt.textured("f_timber", K.half_res(K.plank_surface(1713, "#5e4530")), normal_strength=1.5)
    m["soil"] = br.make_material("growbed_soil", albedo, st.normal_from_height(height, px, strength=1.2), rough=0.95)
    return m


# =====================================================================================================
# Pieces
# =====================================================================================================

def soil_height(x, y):
    """The fill's height over SOIL_Z: a low crown, clods, and a lip where it meets the planks."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    crown = 0.016 * np.clip(1 - (x * x + y * y) / (INNER * 1.05) ** 2, 0, 1)
    f1, _ = st.worley2(x, y, 0.05, 971)
    g1, _ = st.worley2(x + 0.3, y, 0.026, 977)
    clods = 0.008 * np.clip(1 - (f1 / 0.034) ** 2, 0, 1) + 0.004 * np.clip(1 - (g1 / 0.018) ** 2, 0, 1)
    edge = np.minimum(INNER - np.abs(x), INNER - np.abs(y))
    lip = 0.006 * np.clip(1 - edge / 0.03, 0, 1)
    wob = rt.Noise3(983).fbm(np.stack([x.ravel(), y.ravel(), np.zeros(x.size)], -1), 7.0, octaves=3).reshape(x.shape)
    return crown + clods + lip + 0.003 * wob


def soil_fill(m, root):
    """The soil: a heightfield over the inner square, its sides running down to the bed's floor, one mesh named Soil."""
    n = 64
    g = np.linspace(-INNER, INNER, n)
    X, Y = np.meshgrid(g, g)
    H = soil_height(X, Y)
    geo = Geo()
    verts = [(float(X[j, i]), float(Y[j, i]), float(SOIL_Z + H[j, i])) for j in range(n) for i in range(n)]
    faces, uvs = [], []

    def uv(k):
        x, y, _ = verts[k]
        return ((x + INNER) / (2 * INNER), 1.0 - (y + INNER) / (2 * INNER))

    for j in range(n - 1):
        for i in range(n - 1):
            q = (j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i)
            faces.append(q)
            uvs.append([uv(k) for k in q])
    geo.add(verts, faces, "soil", uvs)
    K.make_object("Soil", geo, m, root)


def plank(g, centre, half, mat, along_x):
    """One wall plank: a board with a small bevel; its grain runs along its length (box UVs)."""
    box(g, centre, half if along_x else (half[1], half[0], half[2]), mat, bevel=0.0035)


def walls(g):
    """Three courses of mismatched planks on each side between the posts, a faded colony board and a plank rim."""
    rng = np.random.default_rng(1707)
    span = HALF - 2 * POST
    courses = [(FLOOR + 0.05, FLOOR + 0.19), (FLOOR + 0.195, FLOOR + 0.345), (FLOOR + 0.35, TOP - 0.03)]
    # (side name, outward normal); the front is -y
    sides = [("front", Vector((0, -1, 0))), ("back", Vector((0, 1, 0))), ("left", Vector((-1, 0, 0))), ("right", Vector((1, 0, 0)))]
    for side, nrm in sides:
        along_x = abs(nrm.y) > 0.5
        for c, (z0, z1) in enumerate(courses):
            mat = ("plank", "plank_grey", "plank_dark")[(c + {"front": 0, "right": 1, "back": 2, "left": 1}[side]) % 3]
            if side == "left" and c == 1:
                mat = "sea_teal"            # one faded colony board, salvaged
            gap = 0.003 + 0.003 * rng.random()
            zc = (z0 + z1) / 2 + 0.002 * rng.uniform(-1, 1)
            hz = (z1 - z0) / 2 - gap
            # boards sit a little proud or shy of each other, as patched-up walls do
            out = HALF - WALL - 0.002 * rng.random()
            c3 = nrm * out + Vector((0, 0, zc))
            plank(g, tuple(c3), (span + 0.004 * rng.uniform(-1, 1), WALL, hz), mat, along_x)
    # the rim: a cap board round the top, mitred by overlapping at the posts
    for side, nrm in sides:
        along_x = abs(nrm.y) > 0.5
        c3 = nrm * (HALF - 0.028) + Vector((0, 0, TOP - 0.012))
        plank(g, tuple(c3), (HALF - 0.004, 0.028, 0.012), "plank", along_x)
    # an inner liner at the soil line so no gap shows between the boards from above
    for side, nrm in sides:
        along_x = abs(nrm.y) > 0.5
        c3 = nrm * (INNER + 0.004) + Vector((0, 0, (FLOOR + 0.06 + SOIL_Z) / 2))
        plank(g, tuple(c3), (INNER + 0.004, 0.004, (SOIL_Z - FLOOR - 0.06) / 2), "plank_dark", along_x)
    # the bed's floor boards under the soil
    box(g, (0, 0, FLOOR + 0.055), (INNER + 0.01, INNER + 0.01, 0.008), "plank_dark", bevel=0.002)


def posts(g):
    """Dark timber corner posts with zinc angle caps on top."""
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (HALF - POST), sy * (HALF - POST)
            box(g, (x, y, (FLOOR + 0.03 + TOP) / 2), (POST, POST, (TOP - FLOOR - 0.03) / 2), "timber", bevel=0.006)
            # zinc cap folded over the post's top and down its two outer faces
            box(g, (x, y, TOP + 0.002), (POST + 0.003, POST + 0.003, 0.003), "zinc", bevel=0.0015)
            box(g, (x + sx * (POST + 0.002), y, TOP - 0.03), (0.002, POST + 0.003, 0.03), "zinc", bevel=0.001)
            box(g, (x, y + sy * (POST + 0.002), TOP - 0.03), (POST + 0.003, 0.002, 0.03), "zinc", bevel=0.001)
            K.screw(g, (x + sx * (POST + 0.004), y, TOP - 0.03), (sx, 0, 0), 0.0055, "rust")
            K.screw(g, (x, y + sy * (POST + 0.004), TOP - 0.03), (0, sy, 0), 0.0055, "rust")


def runners(g):
    """Two dark iron runners under the bed (it slides onto a deck or a trailer like a pallet)."""
    for sx in (-1, 1):
        x = sx * (HALF - 0.16)
        box(g, (x, 0, FLOOR + 0.022), (0.035, HALF - 0.02, 0.022), "graphite", bevel=0.004)
        for sy in (-1, 1):
            K.screw(g, (x, sy * (HALF - 0.06), FLOOR + 0.045), (0, 0, 1), 0.006, "zinc")


def strap(g):
    """A dark iron strap round the bed's middle course, screwed to each post."""
    z = FLOOR + 0.27
    for nrm in (Vector((0, -1, 0)), Vector((0, 1, 0)), Vector((-1, 0, 0)), Vector((1, 0, 0))):
        along_x = abs(nrm.y) > 0.5
        c3 = nrm * (HALF + 0.001) + Vector((0, 0, z))
        half = (HALF + 0.001, 0.0025, 0.016) if along_x else (0.0025, HALF + 0.001, 0.016)
        box(g, tuple(c3), half, "graphite", bevel=0.001)
        side = Vector((1, 0, 0)) if along_x else Vector((0, 1, 0))
        for s in (-1, 1):
            K.screw(g, tuple(c3 + nrm * 0.003 + side * s * (HALF - POST)), tuple(nrm), 0.0055, "zinc")


def patch(g):
    """A zinc plate patched over a split in the front's top course, four rusty screws."""
    c = Vector((-0.24, -HALF - 0.0005, FLOOR + 0.40))
    box(g, tuple(c), (0.11, 0.0015, 0.045), "zinc", bevel=0.0012)
    for sx in (-1, 1):
        for sz in (-1, 1):
            K.screw(g, tuple(c + Vector((sx * 0.092, -0.0015, sz * 0.031))), (0, -1, 0), 0.005, "rust")


def junction_box(m, g, root):
    """The salvaged zinc junction box set into the soil in the front right corner, inside the footprint: where the power
    link plugs in (a socket on its front) and the cyan pip on its lid. Returns the pip's glow node."""
    c = Vector((INNER - 0.085, -(INNER - 0.075), SOIL_Z + 0.022))
    hz = 0.026
    box(g, tuple(c), (0.07, 0.06, hz), "zinc", bevel=0.005)
    box(g, tuple(c + Vector((0, 0, hz + 0.001))), (0.061, 0.051, 0.0025), "graphite", bevel=0.0015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.screw(g, tuple(c + Vector((sx * 0.053, sy * 0.042, hz + 0.0035))), (0, 0, 1), 0.0045, "rust")
    # the link socket on its lid (the Switch's style: a dark hole in a graphite ring)
    face = c + Vector((-0.022, 0.0, hz + 0.0035))
    cylinder(g, face + Vector((0, 0, -0.002)), face + Vector((0, 0, 0.004)), 0.014, "graphite", segs=18)
    cylinder(g, face, face + Vector((0, 0, 0.0055)), 0.009, "hole", segs=16)
    pip = S.status_pip(m, g, root, "Pip", tuple(c + Vector((0.026, 0.0, hz + 0.0036))), (0, 0, 1), r=0.011)
    # the zinc drip line from the box round the inside of the rim, lying on the soil's edge, with drip holes
    line_z = SOIL_Z + 0.014
    inset = INNER - 0.02
    start = c + Vector((-0.07, 0.0, -0.004))
    pts = [start, Vector((start.x - 0.03, start.y, line_z)), Vector((-inset, start.y, line_z)), Vector((-inset, inset, line_z)),
           Vector((inset, inset, line_z)), Vector((inset, c.y + 0.075, line_z)), Vector((c.x + 0.02, c.y + 0.075, line_z)),
           Vector((c.x + 0.02, c.y + 0.06, c.z - 0.004))]
    K.sweep(g, pts, 0.0055, "zinc", segs=8)
    for a, b in zip(pts[2:5], pts[3:6]):
        for k in range(1, 7):
            p = a.lerp(b, k / 7)
            cylinder(g, p + Vector((0, 0, 0.0045)), p + Vector((0, 0, 0.0062)), 0.0017, "hole", segs=6)
    # clips holding the line down at the corners
    for p in pts[2:6]:
        box(g, tuple(p + Vector((0, 0, 0.002))), (0.009, 0.009, 0.0035), "rust", bevel=0.001)
    return pip


# =====================================================================================================
# The Growbed (5 x 2 x 5)
# =====================================================================================================

def build_growbed(m):
    root = K.make_empty("Growbed")
    g = Geo()
    posts(g)
    walls(g)
    runners(g)
    strap(g)
    patch(g)
    pip = junction_box(m, g, root)
    K.make_object("Bed", g, m, root)
    soil_fill(m, root)
    rig = {"root": root}
    W.keep(rig, pip)

    def at(power):
        W.lit(pip, power)
    rig["rest"] = lambda: at(0)
    rig["clips"] = {
        "on": (0.25, False, lambda t: at(K.smooth(t / 0.25))),
        "off": (0.25, False, lambda t: at(1 - K.smooth(t / 0.25))),
    }
    rig["states"] = [("UNPOWERED", lambda: at(0)), ("POWERED", lambda: at(1))]
    return rig


# =====================================================================================================
# Planted review renders
# =====================================================================================================

def plant(seed_glb, stage, at, turn=0.0):
    """Imports a crop's plant model (meshes/crops/<crop>_plant.glb) posed at `stage` (its grow clip, second n is
    stage n) standing at `at`. Returns its root objects."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(CROPS / seed_glb))
    new = [o for o in bpy.data.objects if o not in before]
    fps = bpy.context.scene.render.fps
    for ob in new:
        ad = ob.animation_data
        if ad is None:
            continue
        grow = next((t for t in ad.nla_tracks if t.name == "grow"), None)
        for t in ad.nla_tracks:
            t.mute = t is not grow
        if grow is not None and grow.strips:
            strip = grow.strips[0]
            ad.action = strip.action
            if hasattr(ad, "action_slot") and getattr(strip, "action_slot", None) is not None:
                ad.action_slot = strip.action_slot
        ad.use_nla = False
    bpy.context.scene.frame_set(int(round(stage * fps)))
    roots = [o for o in new if o.parent is None]
    for r in roots:
        r.location = Vector(at)
        r.rotation_euler.z = turn
    bpy.context.view_layer.update()
    return roots, new


def bake_pose(objects):
    """Freezes imported objects at the current frame (their clips would move them when the review changes frames)."""
    held = [(ob, ob.matrix_basis.copy()) for ob in objects]
    for ob, basis in held:
        if ob.animation_data is not None:
            ob.animation_data_clear()
        ob.matrix_basis = basis
