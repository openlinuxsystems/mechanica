"""Review renders of the cliff rocks (cliff_rocks.py): python3 render_cliffs.py OUT [views...]

Views: ``kit`` (every piece on a sheet, labelled, with a 1.8 m player), and a mock-up of one 8 m cliff step about 60 m
long as the terrain makes it (a heightmap slope painted with the rock ground where it is steep), dressed with a row of
walls, the corner, low bands where the face turns gentle at its ends and scree at its foot: ``mock_34`` (three-quarter
view from above), ``mock_eye`` (eye level from the lower plateau), and ``bare_34`` and ``bare_eye``, the same slope
undressed. SAMPLES (default 32) and SCALE (resolution percent, default 100) control quality; renders are 1600 px wide
JPEGs.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
import bpy  # noqa: E402  (bpy first: it makes bmesh importable)
import bmesh  # noqa: E402
import numpy as np  # noqa: E402

import kit as K  # noqa: E402
import frame as FR  # noqa: E402
import cliff_rocks as C  # noqa: E402
from families import M, GRAPHITE  # noqa: E402

out = sys.argv[1]
views = sys.argv[2:] or ["kit", "mock_34", "mock_eye", "bare_34", "bare_eye"]
os.makedirs(out, exist_ok=True)
MAN = K.MESH / "characters/scrapper_male.glb"
TEX = K.REPO / "mods/vanilla/textures/terrain"


def scene(ground=True, color="#7f9a5c"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K._protos.clear()
    K.setup_render(int(os.environ.get("SAMPLES", "32")))
    sc = bpy.context.scene
    sc.render.resolution_percentage = int(os.environ.get("SCALE", "100"))
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    K.sky(1.0, top="#86b4d8", horizon="#dce8ee")
    K.sun((52, 0, -38), 4.2)
    if ground:
        K.ground(2000, color=color)


def shoot(name, loc, target, lens, w=1600, h=900):
    cam = K.camera(name, loc, target, lens=lens)
    K.render(cam, f"{out}/{name}.jpg", w, h)


def man(loc, yaw=0.0):
    return K.import_glb(MAN, loc, yaw=yaw)


def tag(text, loc, size=1.3):
    FR.label(None, text, loc, size, M(GRAPHITE, wear=0, grime=0, scratches=0), rot=(0, 0, 0), extrude=0.005)


def put(name, loc, yaw=0.0, mirror=False):
    P = C.PROPS[name]()
    P.location = loc
    P.rotation_euler = (0, 0, math.radians(yaw))
    if mirror:
        P.scale = (-1, 1, 1)
    return P


def kit_sheet():
    """The pieces in two rows on a pale pad, each named on the ground in front of it."""
    scene(color="#7f8b6c")
    K.box("sheet_pad", (92.0, 50.0, 0.06), M("#b9b6ac", wear=0, grime=0, scratches=0), loc=(2.0, 6.0, 0.03), bev=0)
    z = 0.06
    row1 = (("cliff_wall_a", -30.0), ("cliff_wall_b", -11.5), ("cliff_wall_c", 6.5), ("cliff_corner", 28.0))
    for name, x in row1:
        put(name, (x, 4.0, z))
        tag(name.upper().replace("_", " "), (x, -2.5, z + 0.01))
    row2 = (("cliff_low_a", -30.0), ("cliff_low_b", -14.0), ("scree_a", 4.0), ("scree_b", 22.0))
    for name, x in row2:
        put(name, (x, -16.0, z))
        tag(name.upper().replace("_", " "), (x, -22.5, z + 0.01))
    man((-21.5, 2.5, z), 200)
    man((-23.0, -18.0, z), 200)
    shoot("kit_sheet", (-8.0, -78.0, 42.0), (0.0, -4.0, 2.0), 30, 1600, 1000)
    shoot("kit_walls_ab", (-21.0, -14.0, 5.0), (-21.0, 4.0, 4.2), 20, 1600, 800)
    shoot("kit_wall_c_corner", (17.0, -14.0, 5.0), (17.0, 4.0, 4.2), 20, 1600, 800)
    shoot("kit_low_scree", (-4.0, -50.0, 14.0), (-4.0, -16.0, 1.0), 36, 1600, 700)


# ---------------------------------------------------------------- the mock-up step
FOOT = C.bend(8.0, 60.0)            # the corner's own foot, run on straight both ways


def heading(s):
    x0, y0, _ = FOOT(s - 0.05, 0, 0)
    x1, y1, _ = FOOT(s + 0.05, 0, 0)
    return math.degrees(math.atan2(y1 - y0, x1 - x0))


def face_width(s):
    """Metres of ground the face takes: sheer (6 m) along the middle, easing to a gentle 18 m at the ends."""
    e = max(C._smooth(-s, 17.0, 24.0), C._smooth(s, 36.0, 42.0))
    return 6.0 + 12.0 * e


_SS = np.concatenate([np.arange(-1300.0, -80.0, 2.0), np.arange(-80.0, 80.0, 0.3), np.arange(80.0, 1300.0, 2.0)])
_FOOTS = np.array([FOOT(s, 0, 0)[:2] for s in _SS])
_TANG = np.gradient(_FOOTS, axis=0)
_TANG /= np.linalg.norm(_TANG, axis=1)[:, None]


def heights(P):
    """The step's height at points P (n by 2): 8 m times a smooth step over the face width, across the signed distance
    from the foot, with a gentle wave as the heightmap's own relief."""
    Z = np.zeros(len(P))
    for i0 in range(0, len(P), 3000):
        p = P[i0:i0 + 3000]
        d2 = ((p[:, None, :] - _FOOTS[None, :, :]) ** 2).sum(2)
        k = d2.argmin(1)
        rel = p - _FOOTS[k]
        side = np.sign(_TANG[k, 0] * rel[:, 1] - _TANG[k, 1] * rel[:, 0])
        d = np.sqrt(d2[np.arange(len(p)), k]) * side
        w = np.array([face_width(s) for s in _SS[k]])
        t = np.clip(d / w, 0, 1)
        Z[i0:i0 + 3000] = 8.0 * t * t * (3 - 2 * t)
    return Z + 0.12 * np.sin(P[:, 0] * 0.31 + 1.0) * np.sin(P[:, 1] * 0.27)


def terrain():
    """The step as a heightmap mesh: half-metre cells round the step, coarse ones out to the horizon."""
    far = [np.arange(-900.0, -300.0, 30.0), np.arange(-300.0, -70.0, 5.0)]
    xs = np.concatenate(far + [np.arange(-70.0, 70.0, 0.5)] + [-a[::-1] for a in far[::-1]])
    ys = np.concatenate(far[:1] + [np.arange(-300.0, -50.0, 5.0), np.arange(-50.0, 70.0, 0.5), np.arange(70.0, 300.0, 5.0),
                                   np.arange(300.0, 900.01, 30.0)])
    X, Y = np.meshgrid(xs, ys)
    P = np.stack([X.ravel(), Y.ravel()], 1)
    Z = heights(P)
    bm = bmesh.new()
    nx = len(xs)
    vs = [bm.verts.new((p[0], p[1], z)) for p, z in zip(P, Z)]
    for j in range(len(ys) - 1):
        for i in range(nx - 1):
            a = j * nx + i
            bm.faces.new((vs[a], vs[a + 1], vs[a + nx + 1], vs[a + nx]))
    ob = K.from_bm("terrain", bm, ground_mat())
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def ground_mat():
    """The terrain's look: grass on the flat, the rock ground on slopes steeper than 0.7 to 1.1 (worldgen.json
    slope_ground), each tiled as the game tiles it (4 m and 5 m)."""
    m = bpy.data.materials.new("terrain")
    m.use_nodes = True
    nt = m.node_tree
    n, L = nt.nodes, nt.links
    b = n["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.95
    tc = n.new("ShaderNodeTexCoord")
    cols = []
    for name, tile in (("grass", 4.0), ("rock", 5.0)):
        mp = n.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (1 / tile, 1 / tile, 1 / tile)
        L.new(tc.outputs["Object"], mp.inputs["Vector"])
        im = n.new("ShaderNodeTexImage")
        im.image = bpy.data.images.load(str(TEX / f"{name}.png"))
        im.projection = "BOX"
        im.projection_blend = 0.3
        L.new(mp.outputs["Vector"], im.inputs["Vector"])
        cols.append(im.outputs["Color"])
    geo = n.new("ShaderNodeNewGeometry")
    sep = n.new("ShaderNodeSeparateXYZ")
    L.new(geo.outputs["Normal"], sep.inputs[0])
    # slope (rise over run) 0.7 to 1.1 is a normal's z of 0.819 to 0.673
    mr = n.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 0.819
    mr.inputs["From Max"].default_value = 0.673
    L.new(sep.outputs["Z"], mr.inputs["Value"])
    mix = n.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    L.new(mr.outputs["Result"], mix.inputs["Factor"])
    L.new(cols[0], mix.inputs[6])
    L.new(cols[1], mix.inputs[7])
    L.new(mix.outputs[2], b.inputs["Base Color"])
    return m


# where the pieces go: (piece, arc length along the foot, metres up the slope from the foot, mirrored)
DRESS = (
    ("cliff_corner", 0.0, 0.0, False),
    ("cliff_wall_a", 14.7, 0.0, False),
    ("cliff_wall_c", 28.7, 0.0, False),
    ("cliff_low_b", 39.2, 0.0, False),
    ("cliff_low_a", 46.0, 5.0, True),
    ("cliff_wall_b", -13.7, 0.0, False),
    ("cliff_low_a", -23.7, 1.5, False),
    ("cliff_low_b", -30.5, 4.0, True),
    ("scree_a", 1.0, 0.0, False),
    ("scree_b", 16.5, 0.0, False),
    ("scree_a", -11.0, 0.0, True),
    ("scree_b", 30.5, 0.0, True),
)


def mock(view, dressed):
    scene(ground=False)
    terrain()
    if dressed:
        for name, s, up, mirror in DRESS:
            x, y, _ = FOOT(s, up, 0)
            z = float(heights(np.array([[x, y]]))[0])
            put(name, (x, y, z), heading(s), mirror)
    man((3.0, -12.0, 0.0), 200)
    if view == "34":
        shoot(f"{'mock' if dressed else 'bare'}_34", (-34.0, -62.0, 34.0), (4.0, 8.0, 3.0), 30)
    else:
        shoot(f"{'mock' if dressed else 'bare'}_eye", (8.0, -30.0, 1.7), (2.0, 4.0, 5.0), 24)


for v in views:
    if v == "kit":
        kit_sheet()
    else:
        kind, view = v.split("_")
        mock(view, kind == "mock")
