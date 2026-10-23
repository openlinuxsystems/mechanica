"""Build the Grazer (Chase approved the fourth-pass concept on 2026-10-20, from his reference sheet).

The Grazer is the passive grazing animal of meadows and fields (and, with husbandry, the one players tame, milk and
breed). It is an animal, not a bot: stylised but natural. A round, heavy-set body on short thick legs, a big round head
nearly as wide as the body with a long broad snout ending in a flat pink nose, big friendly open eyes (white, a brown
iris, catchlights, a raised brow and the cheek pushed up in a smile), wide droopy ears, soft fleshy pink horns (a big
curl and a short stub each side), cream socks over pink hooves, a long tail with a cream tuft and a hinged lower jaw
that crops grass, chews and moos.

Three coats share the rig: slate with cream spots (the reference colours, ``grazer.glb``), fawn
(``grazer_fawn.glb``) and chocolate (``grazer_chocolate.glb``). The bot's ``model.meshes`` picks one per Grazer.

What it writes: ``mods/vanilla/meshes/bots/grazer[_coat].glb``. Feet are at the origin and it faces -Z, as bots do.
The nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the barrel), ``Neck`` (lowers and turns at the shoulders), ``Collar`` (on the neck: leather, a bell and a
  name tag; the game shows it only on a tamed Grazer, the bot's ``model.kept_nodes``), ``LeadRing`` (an empty under the
  collar's D-ring, where the rope of a Lead clips on: the bot's ``model.lead_node``), ``Head`` (turns on the neck), ``Jaw`` (the lower jaw, hinged under the cheeks),
  ``EarL`` / ``EarR``, ``Tail1`` / ``Tail2``;
* legs ``Leg<FL|FR|BL|BR>_Hip`` / ``_Knee`` / ``_Foot`` (``_Foot`` is an empty on the ground under the hoof).

Clips (seconds; the loops loop): ``idle``, ``patrol`` (walk) and ``chase`` (the gallop it flees at; their ground
speeds at speed scale 1 are printed and go in the bot's ``model.clip_speeds``), ``alert`` (startled), ``lose_interest``,
``hit``, ``death``, ``jump``, ``fall``, ``land``; the idles the game plays now and then while it stands (the bot's
``model.idles``): ``graze``, ``chew``, ``look``, ``tail_flick``, ``moo``; and for husbandry: ``eat`` (from a trough or
a hand), ``happy``, ``lie_down``, ``sleep`` and ``get_up``.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/grazer/build_grazer.py [--coat slate ...]              # writes the .glb files
    python tools/grazer/build_grazer.py --render OUT_DIR [--coat ...]   # review renders and sheets

The output is committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up and the model is built facing -Y, then turned to face -Z in glTF. The build is
deterministic. The soft shapes are metaballs baked to meshes and decimated; features (eyes, ears, horns, nose, tail)
sit on the skin by ray casts. Clip baking and export are the Scuttler build's (tools/scuttler).
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "lift"))
sys.path.insert(0, str(REPO / "tools" / "scuttler"))
import lift_textures as lt  # noqa: E402
import build_scuttler as sc  # noqa: E402
from build_scuttler import at, env, pulse, rod, sphere  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "bots"
FPS = sc.FPS
SAMPLES = int(os.environ.get("GRAZER_SAMPLES", "48"))
V = Vector
X = Vector((1, 0, 0))
Y = Vector((0, 1, 0))
Z = Vector((0, 0, 1))

# coat: (file id, main colour, lighter mottle, spots, darker mask round the eyes)
COATS = {
    "slate": ("grazer", "#4c5e70", "#5a6d80", "#e6dcc4", "#3d4a56"),
    "fawn": ("grazer_fawn", "#9a6a42", "#ab7a50", "#efe4cc", "#6a4628"),
    "chocolate": ("grazer_chocolate", "#4a3326", "#5a4030", "#e8e0c8", "#33231a"),
}
COAT = ["slate"]
CREAM = "#e4d9bf"
FLESH = "#e8907c"
COAT_TILE = 1.2          # metres per coat texture tile (the spots are about 0.2 m across)

HEAD = V((0, -0.66, 0.92))             # middle of the head (the concept's numbers)
SHOULDER = V((0, -0.22, 0.8))          # where the neck lowers and turns, inside the body
NECK = V((0, -0.42, 0.86))             # where the head turns on the neck
HINGE = HEAD + V((0, -0.12, -0.28))    # the jaw's hinge under the cheeks
HIP_Z, KNEE_Z = 0.5, 0.29
LEGS = {"FL": (-0.22, -0.26), "FR": (0.22, -0.26), "BL": (-0.23, 0.42), "BR": (0.23, 0.42)}

# Proportions, the adult's; calf() changes them for the calf (built at adult size, then scaled down by SCALE).
BODY_LIFT = 0.0      # how far the barrel (and the neck and head on it) sits above the adult's
BODY = 1.0           # the barrel's size
BODY_LENGTH = 1.0    # its length on top of that
SNOUT = 1.0          # the snout's length
EYE = 1.0            # the eyes' size
HORNS = "curl"       # "curl" (a big curl and a stub) or "buds" (two small nubs)
LEG_R = 1.0          # the legs' thickness
TAIL = 1.0           # the tail's length
SCALE = 1.0          # the whole model, in the game
CALF = [False]


def calf():
    """A calf's proportions: leggy, a short round body, a big head with a short snout, big eyes, horn buds."""
    global HEAD, SHOULDER, NECK, HINGE, HIP_Z, KNEE_Z, LEGS, BODY_LIFT, BODY, BODY_LENGTH, SNOUT, EYE, HORNS, LEG_R, TAIL, SCALE
    BODY_LIFT = 0.16
    lift = V((0, 0, BODY_LIFT))
    HEAD = V((0, -0.6, 1.07)) + lift
    SHOULDER = V((0, -0.2, 0.8)) + lift
    NECK = V((0, -0.38, 0.93)) + lift
    SNOUT = 0.72
    HINGE = HEAD + V((0, -0.12 * SNOUT, -0.28))
    HIP_Z, KNEE_Z = 0.5 + BODY_LIFT, 0.33
    LEGS = {"FL": (-0.19, -0.2), "FR": (0.19, -0.2), "BL": (-0.2, 0.34), "BR": (0.2, 0.34)}
    BODY, BODY_LENGTH = 0.84, 0.86
    EYE = 1.2
    HORNS = "buds"
    LEG_R = 0.78
    TAIL = 0.7
    SCALE = 0.55
    CALF[0] = True


# =====================================================================================================
# Soft shapes: metaballs baked to meshes
# =====================================================================================================
class Meta:
    """One metaball family (capsules and ellipsoids that melt into one surface)."""

    count = [0]

    def __init__(self, res=0.022):
        Meta.count[0] += 1
        self.name = f"mb{Meta.count[0]:03d}x"
        self.mb = bpy.data.metaballs.new(self.name)
        self.mb.resolution = self.mb.render_resolution = res
        self.mb.threshold = 0.6
        self.ob = bpy.data.objects.new(self.name, self.mb)
        bpy.context.scene.collection.objects.link(self.ob)

    def cap(self, a, b, r, stiff=2.0):
        a, b = V(a), V(b)
        e = self.mb.elements.new()
        e.type = "CAPSULE"
        e.co = (a + b) / 2
        e.radius = r
        e.size_x = max(1e-4, (b - a).length / 2)
        e.rotation = X.rotation_difference((b - a).normalized()) if (b - a).length > 1e-6 else Quaternion()
        e.stiffness = stiff

    def ell(self, c, r, size=(1, 1, 1), stiff=2.0, neg=False):
        e = self.mb.elements.new()
        e.type = "ELLIPSOID"
        e.co = V(c)
        e.radius = r
        e.size_x, e.size_y, e.size_z = size
        e.stiffness = stiff
        e.use_negative = neg

    def bmesh(self, ratio=1.0):
        """The surface as a bmesh (model space), decimated to `ratio` of its faces."""
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(self.ob.evaluated_get(dg))
        bpy.data.objects.remove(self.ob)
        tmp = bpy.data.objects.new(self.name + "_tmp", me)
        bpy.context.scene.collection.objects.link(tmp)
        if ratio < 1:
            m = tmp.modifiers.new("dec", "DECIMATE")
            m.ratio = ratio
        dg = bpy.context.evaluated_depsgraph_get()
        bm = bmesh.new()
        bm.from_object(tmp.evaluated_get(dg), dg)
        bpy.data.objects.remove(tmp)
        bpy.data.meshes.remove(me)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        return bm


def add(bm, mat):
    """Puts a bmesh on the current node with material `mat`."""
    sc.g().add_bm(bm, mat)


def soft(pts, r0, r1, mat, ratio=0.3):
    """A smooth tapering tube through pts (horns, the tail)."""
    mb = Meta(res=min(0.02, r1 * 0.35))
    n = len(pts) - 1
    for i in range(n):
        mb.cap(pts[i], pts[i + 1], (r0 + (r1 - r0) * (i + 0.5) / n) / 0.78)
    add(mb.bmesh(ratio), mat)


def tuft(pts, mat, r=0.05, stretch=2.2):
    """A clump of hanging fur (the tail tip)."""
    mb = Meta(res=0.012)
    for p in pts:
        mb.ell(p, r, (0.8, 0.8, stretch), stiff=1.5)
    add(mb.bmesh(0.3), mat)


def curve(a, ctrl, b, n=8):
    a, ctrl, b = V(a), V(ctrl), V(b)
    return [a * (1 - t) ** 2 + ctrl * 2 * t * (1 - t) + b * t * t for t in (i / n for i in range(n + 1))]


class Skin:
    """Ray casts onto a soft shape's surface, to seat features on it."""

    def __init__(self, bm):
        self.bvh = BVHTree.FromBMesh(bm)

    def hit(self, towards, frm):
        frm, towards = V(frm), V(towards)
        loc, n, _, _ = self.bvh.ray_cast(frm, (towards - frm).normalized())
        if loc is None:
            raise SystemExit(f"no skin between {tuple(frm)} and {tuple(towards)}")
        return loc, n


# =====================================================================================================
# Geometry
# =====================================================================================================
def body_geometry():
    """The barrel and the tops of the legs (the legs' own pieces turn inside them)."""
    at("Body")
    mb = Meta(0.03)
    k, ky, dz = BODY, BODY * BODY_LENGTH, BODY_LIFT
    mb.ell((0, 0.1 * ky, 0.68 + dz), 0.62 * k, (0.85, 1.0, 0.85))
    mb.ell((0, 0.36 * ky, 0.72 + dz), 0.48 * k, (0.9, 0.8, 0.88))
    mb.ell((0, -0.2 * ky, 0.7 + dz), 0.5 * k, (0.9, 0.8, 0.9))
    for x, y in LEGS.values():
        mb.cap((x, y, 0.66 + dz), (x, y, HIP_Z - 0.04), 0.21 * LEG_R)
    bm = mb.bmesh(0.2)
    skin = Skin(bm)
    add(bm, "coat")
    return skin


def neck_geometry():
    """The thick neck from deep in the shoulders up into the back of the head (on Neck), so the head stays on it."""
    at("Neck")
    mb = Meta(0.03)
    mb.cap(SHOULDER + V((0, 0.12, -0.06)), NECK + V((0, -0.08, 0.0)), 0.38)
    bm = mb.bmesh(0.2)
    skin = Skin(bm)
    add(bm, "coat")
    return skin


def band(pts, outs, width, thick, mat):
    """A closed flat strap through `pts` (a ring on the skin), `width` across the ring, `thick` out along `outs`."""
    bm = bmesh.new()
    n = len(pts)
    axis = (pts[1] - pts[0]).cross(pts[2] - pts[1]).normalized()
    rings = [[bm.verts.new(p + o * dz + axis * dw) for dz, dw in ((0, -width / 2), (thick, -width / 2), (thick, width / 2), (0, width / 2))]
             for p, o in zip(pts, outs)]
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    add(bm, mat)


LEAD_RING = []               # where a Lead's rope clips on (the bottom of the collar's D-ring), set by collar_geometry


def collar_geometry(skins):
    """
    The collar a tamed Grazer wears (on Collar, which the game shows only while it is kept): a leather strap round
    the narrowest part of the neck, a brass buckle on its left, and a brass bell and a round name tag under the throat.
    """
    at("Collar")
    a, b = SHOULDER + V((0, 0.12, -0.06)), NECK + V((0, -0.08, 0))
    axis = (b - a).normalized()
    u = axis.cross(X).normalized()

    def ring(t, n=48):
        c = a.lerp(b, t)
        pts, outs = [], []
        for k in range(n):
            ang = 2 * math.pi * k / n
            d = math.cos(ang) * u + math.sin(ang) * X
            hits = [skin.bvh.ray_cast(c + d * 1.2, -d) for skin in skins]
            loc = min((h[0] for h in hits if h[0] is not None), key=lambda p: (p - (c + d * 1.2)).length)
            pts.append(loc + d * 0.004)
            outs.append(d)
        return pts, outs, sum((p - c).length for p in pts) / n

    pts, outs, _ = min((ring(t / 20) for t in range(4, 18)), key=lambda r: r[2])
    band(pts, outs, 0.085, 0.02, "leather")
    band(pts, outs, 0.012, 0.025, "leather")      # the stitched ridge
    side = min(range(len(pts)), key=lambda i: pts[i].x)
    p, d = pts[side], outs[side]
    sc.box((0.024, 0.09, 0.09), p + d * 0.02, "brass", bevel=0.006, rot=d.to_track_quat("X", "Z").to_matrix().to_4x4())
    low = min(range(len(pts)), key=lambda i: pts[i].z)
    p, d = pts[low], outs[low]
    hang = p + d * 0.03 + V((0, 0, -0.03))
    rod(hang + V((0.02, 0, 0)), hang - V((0.02, 0, 0)), 0.012, "brass", segs=10)
    bell = hang + V((0, -0.02, -0.075))
    rod(bell + V((0, 0, 0.045)), bell + V((0, 0, -0.035)), 0.026, "brass", r2=0.05, segs=24)
    sphere(bell + V((0, 0, 0.045)), 0.027, "brass", segs=16)
    sc.disc(bell + V((0, 0, -0.04)), 0.044, 0.008, (0, 0, 1), "bell_in")
    sphere(bell + V((0, 0, -0.05)), 0.014, "brass")
    # the lead's D-ring beside the bell's hanger: the rope of a Lead clips on here (LeadRing)
    ring_at = hang + V((-0.07, -0.01, -0.012))
    rod(hang - V((0.02, 0, 0)), ring_at + V((0, 0, 0.02)), 0.006, "brass", segs=8)
    pts = [ring_at + V((0, 0.024 * math.sin(a), 0.024 * math.cos(a))) for a in [2 * math.pi * k / 20 for k in range(21)]]
    for p0, p1 in zip(pts, pts[1:]):
        rod(p0, p1, 0.0055, "brass", segs=8)
    LEAD_RING[:] = [ring_at - V((0, 0, 0.024))]
    tag = hang + V((0.07, -0.03, -0.05))
    sc.disc(tag, 0.034, 0.006, (0.3, -1, 0.1), "brass", segs=28)
    rod(hang + V((0.03, 0, 0)), tag + V((0, 0, 0.03)), 0.005, "brass", segs=6)


def head_geometry():
    """The head on its neck stub, the snout with the mouth carved under it, nose, eyes, horns."""
    at("Head")
    mb = Meta(0.022)
    mb.cap(NECK + V((0, 0.12, -0.02)), HEAD + V((0, 0.1, -0.02)), 0.36)   # neck stub, hidden in the body
    mb.ell(HEAD, 0.52, (1.0, 0.95, 0.95))
    mb.cap(HEAD + V((0, -0.1, -0.06)), HEAD + V((0, -0.4 * SNOUT, -0.24)), 0.33)
    mb.ell(HEAD + V((0, -0.44 * SNOUT, -0.26)), 0.36, (1.05, 0.72, 0.9))
    for sx in (-1, 1):
        mb.ell(HEAD + V((sx * 0.18, -0.2, 0.08)), 0.2)      # brows round the eyes
        mb.ell(HEAD + V((sx * 0.2, -0.1, -0.16)), 0.2)      # cheeks
    mb.ell(HEAD + V((0, -0.4 * SNOUT, -0.5)), 0.3, (1.15, 1.25 * SNOUT, 0.55), neg=True)   # the slot the jaw sits in
    bm = mb.bmesh(0.25)
    skin = Skin(bm)
    add(bm, "coat")
    # the nose: a flat pink pad on the end of the snout with two deep nostrils
    tip, _ = skin.hit(HEAD + V((0, -0.4 * SNOUT, -0.27)), HEAD + V((0, -2, -0.27)))
    nose = tip + V((0, 0.03, 0))
    sphere(nose, 0.21, "nose", scale=(1.12, 0.36, 0.85), segs=24)
    for sx in (-1, 1):
        sphere(nose + V((sx * 0.08, -0.07, 0.01)), 0.042, "nostril", scale=(0.9, 0.5, 1.2), segs=10)
    for sx in (-1, 1):
        # wide open friendly eyes: white, a brown iris looking forward, catchlights, a raised brow and the cheek
        # pushed up under the eye in a smile
        p, n = skin.hit(HEAD + V((sx * 0.18, -0.2, 0.1)), HEAD + V((sx * 0.9, -1.4, 0.1)))
        e = EYE
        sphere(p - n * 0.06 * e, 0.12 * e, "mask", segs=18)
        sphere(p, 0.095 * e, "white", segs=20)
        fwd = (n + V((0, -0.9, 0))).normalized()
        sphere(p + fwd * 0.058 * e, 0.07 * e, "iris", scale=(1, 1, 1.05), segs=18)
        sphere(p + fwd * 0.083 * e, 0.044 * e, "pupil", segs=14)
        sphere(p + (fwd * 0.112 + V((0, 0, 0.032)) + n * 0.01) * e, 0.018 * e, "glint", segs=8)
        sphere(p + (fwd * 0.115 + V((0, 0, -0.022)) - n * 0.012) * e, 0.008 * e, "glint", segs=6)
        sphere(p + (V((0, 0, -0.098)) - n * 0.01) * e, 0.09 * e, "mask", scale=(1.1, 1.0, 0.55), segs=18)
        sphere(p + (V((0, 0, 0.14)) - n * 0.04) * e, 0.065 * e, "mask", scale=(1.0, 1.0, 0.36), segs=14)
        # soft fleshy horns: a big curl up, out and over, and a short stub in front of it
        a, _ = skin.hit(HEAD + V((sx * 0.14, 0, 0.2)), HEAD + V((sx * 0.14, 0, 1.5)))
        a = a - V((0, 0, 0.04))
        if HORNS == "buds":
            # a calf's horns: two soft pink nubs just breaking through the coat
            soft(curve(a, a + V((sx * 0.01, 0, 0.06)), a + V((sx * 0.04, -0.01, 0.1)), 4), 0.06, 0.042, "flesh")
            continue
        soft(curve(a, a + V((sx * 0.04, 0.04, 0.42)), a + V((sx * 0.42, 0, 0.2)), 12), 0.085, 0.062, "flesh")
        b, _ = skin.hit(HEAD + V((sx * 0.1, -0.16, 0.2)), HEAD + V((sx * 0.1, -0.16, 1.5)))
        b = b - V((0, 0, 0.03))
        soft(curve(b, b + V((sx * 0.02, -0.05, 0.12)), b + V((sx * 0.12, -0.14, 0.14)), 6), 0.058, 0.048, "flesh")
    return skin


def ear_points(skin, sx):
    p, _ = skin.hit(HEAD + V((sx * 0.3, 0.04, 0.12)), HEAD + V((sx * 1.2, 0.04, 0.2)))
    return p, p + V((sx * 0.24, 0.02, -0.1))


def ears_geometry(skin):
    """Wide droopy ears from behind the eyes, sideways and down, pink inside (on EarL / EarR)."""
    for side, sx in (("L", -1), ("R", 1)):
        _, mid = ear_points(skin, sx)
        at(f"Ear{side}")
        sphere(mid, 0.26, "coat", scale=(1.0, 0.28, 0.5), segs=20)
        sphere(mid + V((0, -0.05, 0)), 0.22, "ear_in", scale=(0.95, 0.15, 0.42), segs=18)


def jaw_geometry():
    """The lower jaw, a pink mouth over it and a tongue (hidden while the mouth is shut)."""
    at("Jaw")
    h = HINGE
    mb = Meta(0.018)
    mb.cap(h + V((0, -0.02, -0.02)), h + V((0, -0.3 * SNOUT, -0.06)), 0.17)
    mb.ell(h + V((0, -0.33 * SNOUT, -0.06)), 0.19, (1.1, 0.75, 0.55))
    for sx in (-1, 1):
        mb.ell(h + V((sx * 0.12, -0.06, 0)), 0.12)
    add(mb.bmesh(0.35), "coat")
    sphere(h + V((0, -0.24 * SNOUT, -0.02)), 0.15, "mouth", scale=(1.05, 1.15 * SNOUT, 0.28), segs=18)
    sphere(h + V((0, -0.3 * SNOUT, 0.005)), 0.1, "tongue", scale=(0.95, 1.2 * SNOUT, 0.3), segs=16)
    sphere(h + V((0, -0.41 * SNOUT, -0.04)), 0.12, "nose", scale=(1.25, 0.4, 0.42), segs=16)


def tail_points(body):
    t0, _ = body.hit((0, 0.5, 0.95 + BODY_LIFT), (0, 2, 0.95 + BODY_LIFT))
    pts = curve(t0 - V((0, 0.03, 0)), t0 + V((0, 0.34 * TAIL, 0.06)), t0 + V((0, 0.42 * TAIL, -0.46 * TAIL)), 10)
    return pts


def tail_geometry(body):
    pts = tail_points(body)
    at("Tail1")
    soft(pts[:6], 0.035, 0.03, "coat")
    at("Tail2")
    soft(pts[5:], 0.03, 0.024, "coat")
    e = pts[-1]
    tuft([e + V((0.015 * math.cos(i * 1.3), 0.015 * math.sin(i * 1.3), -0.04 - 0.01 * i)) for i in range(8)], "cream", r=0.075)


def legs_geometry():
    """Each leg: a coat upper on the hip, a cream sock on the knee and a pink hoof."""
    for name, (x, y) in LEGS.items():
        at(f"Leg{name}_Hip")
        mb = Meta(0.02)
        mb.cap((x, y, HIP_Z + 0.04), (x, y, KNEE_Z), 0.19 * LEG_R)
        mb.ell((x, y, KNEE_Z), 0.17 * LEG_R)
        add(mb.bmesh(0.3), "coat")
        at(f"Leg{name}_Knee")
        mb = Meta(0.02)
        mb.cap((x, y, KNEE_Z + 0.02), (x, y, 0.12), 0.185 * LEG_R)
        mb.ell((x, y, 0.13), 0.16 * LEG_R, (1, 1, 0.8))
        add(mb.bmesh(0.3), "cream")
        rod((x, y - 0.01, 0), (x, y, 0.1), 0.15 * LEG_R, "hoof", r2=0.14 * LEG_R, segs=20)
        # the cleft between the two toes, at the front
        rod((x, y - 0.12 * LEG_R, 0.002), (x, y - 0.12 * LEG_R, 0.085), 0.012, "nostril", segs=6)


def pivots(skins=None):
    """Node -> (parent, pivot in model space)."""
    body, head = skins or SKINS
    tail = tail_points(body)
    piv = {"Body": (None, V((0, 0.1, 0.68 + BODY_LIFT))), "Neck": ("Body", SHOULDER), "Collar": ("Neck", NECK), "LeadRing": ("Collar", LEAD_RING[0]), "Head": ("Neck", NECK), "Jaw": ("Head", HINGE)}
    for side, sx in (("L", -1), ("R", 1)):
        piv[f"Ear{side}"] = ("Head", ear_points(head, sx)[0])
    piv["Tail1"] = ("Body", tail[0])
    piv["Tail2"] = ("Tail1", tail[5])
    for name, (x, y) in LEGS.items():
        piv[f"Leg{name}_Hip"] = ("Body", V((x, y, HIP_Z)))
        piv[f"Leg{name}_Knee"] = (f"Leg{name}_Hip", V((x, y, KNEE_Z)))
        piv[f"Leg{name}_Foot"] = (f"Leg{name}_Knee", V((x, y, 0)))
    return piv


SKINS = []


def geometry():
    sc.GEO.clear()
    Meta.count[0] = 0
    body = body_geometry()
    neck = neck_geometry()
    head = head_geometry()
    collar_geometry([body, neck, head])
    SKINS[:] = [body, head]
    ears_geometry(head)
    jaw_geometry()
    tail_geometry(body)
    legs_geometry()


# =====================================================================================================
# Materials
# =====================================================================================================
def fur_surface(seed, base, light, spots=None, spot_cut=0.5):
    """Short fur: two tones mottled, fine vertical strands in the height, optional soft-edged spots."""
    mottle = lt.fbm(seed, 2.0)
    strands = lt.fnoise(seed + 5, 90.0, aniso=(1.0, 0.12))
    fine = lt.fnoise(seed + 9, 160.0)
    a, b = lt.srgb(base), lt.srgb(light)
    k = np.clip(mottle * 0.9 + 0.5, 0, 1)[..., None]
    albedo = a[None, None, :] * (1 - k) + b[None, None, :] * k
    albedo = albedo * (1 + 0.06 * strands[..., None])
    if spots:
        s = lt.fnoise(seed + 21, 3.2) + 0.25 * lt.fnoise(seed + 23, 12.0)
        m = np.clip((s - spot_cut) * 9, 0, 1)
        m = lt.blur(m, 2)[..., None]
        sp = lt.srgb(spots)[None, None, :] * (1 + 0.04 * strands[..., None])
        albedo = albedo * (1 - m) + sp * m
    height = 0.6 * strands + 0.2 * fine
    rough = np.full((lt.SIZE, lt.SIZE), 0.82) + 0.05 * mottle
    return albedo, height, rough, np.zeros((lt.SIZE, lt.SIZE))


def soft_surface(seed, color, rough):
    """Skin (nose, horns, hooves, mouth): a gentle mottle and a soft grain."""
    mottle = lt.fbm(seed, 3.0)
    grain = lt.fnoise(seed + 3, 120.0)
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.05 * mottle[..., None] + 0.02 * grain[..., None])
    return albedo, 0.15 * grain + 0.1 * mottle, np.full((lt.SIZE, lt.SIZE), rough) + 0.04 * mottle, np.zeros((lt.SIZE, lt.SIZE))


def plain(name, hexc, rough):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*(lt.srgb(hexc) ** 2.2), 1)
    b.inputs["Roughness"].default_value = rough
    return mat


def materials():
    _, base, light, spots, mask = COATS[COAT[0]]
    key = COAT[0]
    return {
        "coat": lambda: lt.textured(f"grazer_{key}_coat", fur_surface(211, base, light, spots, 0.42), normal_strength=1.5),
        "mask": lambda: lt.textured(f"grazer_{key}_mask", fur_surface(223, mask, base), normal_strength=1.0),
        "cream": lambda: lt.textured("grazer_cream", fur_surface(229, CREAM, "#efe6d0"), normal_strength=2.0),
        "ear_in": lambda: lt.textured("grazer_ear_in", soft_surface(233, "#c88a7a", 0.6), normal_strength=1.0),
        "nose": lambda: lt.textured("grazer_nose", soft_surface(239, "#e48e7a", 0.38), normal_strength=1.0),
        "flesh": lambda: lt.textured("grazer_flesh", soft_surface(241, FLESH, 0.45), normal_strength=1.0),
        "hoof": lambda: lt.textured("grazer_hoof", soft_surface(251, "#b8766c", 0.42), normal_strength=1.0),
        "mouth": lambda: plain("grazer_mouth", "#6a2a2c", 0.5),
        "tongue": lambda: lt.textured("grazer_tongue", soft_surface(257, "#d0707a", 0.35), normal_strength=1.0),
        "nostril": lambda: plain("grazer_nostril", "#4a2420", 0.6),
        "white": lambda: plain("grazer_eye_white", "#f4f0e8", 0.15),
        "iris": lambda: plain("grazer_iris", "#4a3220", 0.1),
        "pupil": lambda: plain("grazer_pupil", "#0a0908", 0.05),
        "glint": lambda: plain("grazer_glint", "#ffffff", 0.05),
        "leather": lambda: lt.textured("grazer_leather", lt.metal_surface(301, "#5a3a22", 0.62, 0.0, wear=0.5, edge_light=0.2), normal_strength=1.2),
        "brass": lambda: lt.textured("grazer_brass", lt.metal_surface(303, "#c49a3c", 0.32, 1.0, wear=0.6), normal_strength=1.0),
        "bell_in": lambda: plain("grazer_bell_in", "#2a2018", 0.6),
    }


def box_uvs(me):
    """Box-projected UVs in metres: COAT_TILE per image on fur, the shared TILE elsewhere."""
    uv = me.uv_layers.new(name="UVMap")
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [m.name for m in me.materials]
    out = np.zeros((len(me.loops), 2))
    for poly in me.polygons:
        axis = int(np.argmax(np.abs(np.asarray(poly.normal))))
        scale = COAT_TILE if "coat" in names[poly.material_index] or "cream" in names[poly.material_index] else lt.TILE
        for li in poly.loop_indices:
            p = co[me.loops[li].vertex_index]
            out[li] = (p[1], p[2]) if axis == 0 else (p[0], p[2]) if axis == 1 else (p[0], p[1])
            out[li] /= scale
    uv.data.foreach_set("uv", out.ravel())


def build():
    """Builds the rig; returns (root, nodes by name)."""
    geometry()
    makers = materials()
    order = list(makers)
    mats = {}
    root = bpy.data.objects.new("Grazer", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler = (0, 0, math.pi)          # built facing -Y; bots face -Z in glTF
    piv = pivots()
    nodes = {}
    for name, (parent, pivot) in piv.items():
        ob = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(ob)
        ob.rotation_mode = "QUATERNION"
        ob.parent = nodes[parent] if parent else root
        ob.location = pivot - (piv[parent][1] if parent else Vector())
        nodes[name] = ob
        geo = sc.GEO.get(name)
        if geo is None:
            continue
        me = bpy.data.meshes.new(f"{name}_mesh")
        used = sorted(set(geo.m), key=order.index)
        me.from_pydata([tuple(Vector(v) - pivot) for v in geo.v], [], geo.f)
        for m in used:
            if m not in mats:
                mats[m] = makers[m]()
            me.materials.append(mats[m])
        me.polygons.foreach_set("material_index", [used.index(m) for m in geo.m])
        me.validate()
        for poly in me.polygons:
            poly.use_smooth = True
        box_uvs(me)
        mesh_ob = bpy.data.objects.new(f"{name}_mesh", me)
        bpy.context.scene.collection.objects.link(mesh_ob)
        mesh_ob.parent = ob
    missing = set(sc.GEO) - set(piv)
    if missing:
        raise SystemExit(f"pieces on nodes the rig lacks: {sorted(missing)}")
    bpy.context.view_layer.update()
    return root, nodes


# =====================================================================================================
# Poses. Angles in degrees. About X a positive turn tips a part back (+Y) at its bottom: a hanging leg swings back,
# the Head or Body pitches down at the front. About Z a positive turn swings the front to the animal's left (-X... the
# right-hand rule about +Z: -Y goes to +X); about Y a positive turn rolls the top to +X.
# =====================================================================================================
def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


class Pose:
    def __init__(self, nodes):
        self.nodes = nodes
        self.rot = {n: Quaternion() for n in nodes}
        self.off = {n: Vector() for n in nodes}

    def turn(self, name, axis, deg):
        self.rot[name] = q_axis(axis, deg) @ self.rot[name]

    def move(self, name, v):
        self.off[name] += Vector(v)

    def leg(self, name, swing=0.0, lift=0.0):
        """swing: the hoof forward (deg about the hip); lift: fold the lower leg to raise the hoof (deg). Front knees fold
        the hoof back, hind hocks fold it forward."""
        self.turn(f"Leg{name}_Hip", X, -swing)
        self.turn(f"Leg{name}_Knee", X, lift if name[0] == "F" else -lift)

    def head(self, pitch=0.0, yaw=0.0, roll=0.0):
        """pitch: the nose down (deg); yaw: the nose to the animal's right; roll: the top to the right. The neck takes
        most of a pitch or turn at the shoulders and the head the rest, so the head stays on the neck."""
        self.turn("Neck", Z, -yaw * 0.6)
        self.turn("Neck", X, pitch * 0.65)
        self.turn("Head", Y, roll)
        self.turn("Head", Z, -yaw * 0.4)
        self.turn("Head", X, pitch * 0.35)

    def jaw(self, open_deg=0.0, side=0.0):
        self.turn("Jaw", Z, side)
        self.turn("Jaw", X, open_deg)

    def ears(self, droop=0.0, back=0.0, flick_l=0.0, flick_r=0.0):
        """droop: ears down (deg); back: ears swept back; flick: a quick twitch up."""
        for side, sx, flick in (("L", -1, flick_l), ("R", 1, flick_r)):
            self.turn(f"Ear{side}", Y, sx * (droop - flick))
            self.turn(f"Ear{side}", Z, -sx * back)

    def tail(self, swing=0.0, raise_=0.0, curl=0.0):
        """swing: side to side (deg); raise_: the tail lifted back; curl: the tip swings further."""
        self.turn("Tail1", Z, swing)
        self.turn("Tail1", X, -raise_)
        self.turn("Tail2", Z, swing * 0.8 + curl)
        self.turn("Tail2", X, -raise_ * 0.6)

    def world(self, name, piv):
        parent, pivot = piv[name]
        local = Matrix.Translation(pivot - (piv[parent][1] if parent else Vector()) + self.off[name]) @ self.rot[name].to_matrix().to_4x4()
        return (self.world(parent, piv) if parent else Matrix.Identity(4)) @ local

    def ground_feet(self):
        """Folds any leg whose hoof the pose pushed into the ground until it stands on it."""
        piv = pivots()
        for name in LEGS:
            foot = f"Leg{name}_Foot"
            if self.world(foot, piv).translation.z > -0.005:
                continue
            hip, knee = f"Leg{name}_Hip", f"Leg{name}_Knee"
            saved = {n: self.rot[n].copy() for n in (hip, knee)}
            lo, hi = 0.0, 80.0
            for _ in range(14):
                mid = (lo + hi) / 2
                self.rot.update({n: q.copy() for n, q in saved.items()})
                # fold at the knee and bring the upper leg the other way, so the hoof stays under the hip
                self.leg(name, mid * 0.5 if name[0] == "F" else -mid * 0.5, mid)
                if self.world(foot, piv).translation.z < 0:
                    lo = mid
                else:
                    hi = mid
            self.rot.update({n: q.copy() for n, q in saved.items()})
            self.leg(name, hi * 0.5 if name[0] == "F" else -hi * 0.5, hi)

    def apply(self):
        piv = pivots()
        for n, ob in self.nodes.items():
            parent, pivot = piv[n]
            ob.rotation_quaternion = self.rot[n]
            ob.location = pivot - (piv[parent][1] if parent else Vector()) + self.off[n]


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def wave(t, hz, phase=0.0):
    return math.sin(2 * math.pi * (hz * t + phase))


# Gaits: (clip length s, swing degrees, lift degrees, stance share, phase per leg, bob m, body pitch rock deg)
GAITS = {
    # a four-beat walk (back left, front left, back right, front right)
    "patrol": (0.9, 20, 55, 0.62, {"BL": 0.0, "FL": 0.25, "BR": 0.5, "FR": 0.75}, 0.02, 1.5),
    # a gallop: the hind pair then the front pair, a little apart
    "chase": (0.5, 32, 75, 0.45, {"BL": 0.0, "BR": 0.08, "FL": 0.45, "FR": 0.53}, 0.06, 6.0),
}
CLIPS = {
    "idle": 3.0, "patrol": GAITS["patrol"][0], "chase": GAITS["chase"][0], "alert": 1.0, "lose_interest": 1.2,
    "hit": 0.5, "death": 2.2, "jump": 0.45, "fall": 0.8, "land": 0.5,
    "graze": 4.0, "chew": 3.0, "look": 2.6, "tail_flick": 1.4, "moo": 2.2,
    "eat": 2.4, "happy": 1.8, "frolic": 1.6, "lie_down": 1.6, "sleep": 4.0, "get_up": 1.6,
}


def gait_speed(clip):
    """Ground speed at speed scale 1: each stance sweeps the hoof through 2 * swing about the hip."""
    length, swing, _, duty = GAITS[clip][:4]
    return 2 * HIP_Z * math.sin(math.radians(swing)) / (duty * length)


def gait(p, clip, u):
    length, swing, lift, duty, phases, bob, rock = GAITS[clip]
    for name, ph in phases.items():
        k = (u + ph) % 1.0
        if k < duty:                                  # stance: the hoof sweeps back along the ground
            s = swing * (1 - 2 * k / duty)
            p.leg(name, s, 0)
        else:                                         # swing: fold, lift and bring it forward
            q = (k - duty) / (1 - duty)
            s = -swing + 2 * swing * smooth(q)
            p.leg(name, s, lift * math.sin(math.pi * q))
    w = 2 * math.pi * u
    if CALF[0]:
        bob *= 2.2                                    # a calf bounces along on its long legs
    p.move("Body", (0, 0, bob * math.cos(2 * w)))
    p.turn("Body", X, -rock * math.cos(w) if clip == "chase" else rock * math.sin(2 * w) * 0.5)
    # the barrel rolls toward the hind leg in stance and swings a little side to side with the hips; the neck
    # takes the swing back out, so the head stays on its line
    p.turn("Body", Y, 2.5 * math.sin(w))
    if clip == "patrol":
        p.turn("Body", Z, 2.0 * math.sin(w + 0.4))
        p.turn("Neck", Z, -1.6 * math.sin(w + 0.4))


def pose(p, clip, t, length):
    u = t / length
    w = 2 * math.pi * u
    breathe = 0.008 * math.sin(2 * math.pi * t / 1.5)
    if clip == "idle":
        p.move("Body", (0, 0, breathe))
        p.head(4 + 2 * math.sin(w), 3 * math.sin(w + 0.5))
        p.ears(6 + 3 * math.sin(w), 0, 12 * pulse(u, 0.55, 0.6, 0.68))
        p.tail(10 * math.sin(w), 2)
    elif clip in GAITS:
        gait(p, clip, u)
        if clip == "patrol":
            # the head nods down as each front hoof lands (FR at u 0.25, FL at 0.75); ears and tail swing a beat
            # behind the body
            nod = 5 if CALF[0] else 4
            p.head(6 + nod * math.sin(2 * w - 0.6), 2 * math.sin(w))
            p.ears(8 + 4 * math.sin(2 * w - 1.2), 0, 6 * pulse(u, 0.3, 0.34, 0.4) if CALF[0] else 0)
            p.tail(14 * math.sin(w - 0.9), 16 if CALF[0] else 4, 8 * math.sin(w - 1.6))
        else:
            # fleeing: head forward and down, ears pinned back, tail up and streaming
            p.head(14 + 6 * math.sin(w), 0)
            p.ears(-6, 40)
            p.tail(8 * math.sin(2 * w), 35, 8 * math.sin(2 * w + 1))
            p.jaw(6 + 4 * math.sin(w))
    elif clip == "alert":
        # startled: a hop back with the head up and ears pricked, then braced to bolt
        k = pulse(u, 0.0, 0.25, 0.75)
        p.move("Body", (0, 0.1 * env(u, 0, 0.3), 0.12 * math.sin(math.pi * min(1.0, u / 0.35))))
        p.turn("Body", X, -8 * k)
        for name in LEGS:
            p.leg(name, -10 * k if name[0] == "F" else 6 * k, 20 * pulse(u, 0.05, 0.18, 0.35))
        p.head(-22 * k, 8 * math.sin(math.pi * u))
        p.ears(-14 * k, 10 * k)
        p.tail(0, 30 * k)
        p.jaw(10 * pulse(u, 0.05, 0.2, 0.5))
    elif clip == "lose_interest":
        k = pulse(u, 0.0, 0.3, 1.0)
        p.head(6 + 10 * k, 18 * math.sin(3 * w) * (1 - u))
        p.ears(10 * k)
        p.tail(12 * math.sin(2 * w), 4)
    elif clip == "hit":
        # a flinch: the body jolts sideways, the head jerks up, eyes squeeze, ears flat
        k = pulse(u, 0.0, 0.15, 1.0)
        p.move("Body", (0.06 * k, 0.04 * k, -0.04 * k))
        p.turn("Body", Y, 8 * k)
        p.head(-18 * k, -10 * k, 6 * k)
        p.ears(20 * k, 30 * k)
        p.tail(20 * k, 25 * k)
        p.jaw(14 * k)
    elif clip == "death":
        # the legs buckle, it sinks and rolls onto its side, head down
        k1 = env(u, 0.0, 0.45)
        k2 = env(u, 0.35, 0.8)
        p.move("Body", (0.32 * k2, 0, -0.36 * k1 - 0.04 * k2))
        p.turn("Body", Y, 82 * k2)
        p.turn("Body", X, 6 * k1)
        for name in LEGS:
            front = name[0] == "F"
            p.leg(name, (30 if front else -20) * k1 * (1 - k2), 90 * k1 * (1 - k2) + 25 * k2)
        p.head(20 * k1 - 10 * k2, 0, 30 * k2)
        p.ears(30 * k2)
        p.tail(30 * k2, 0)
        p.jaw(16 * env(u, 0.5, 0.8))
    elif clip == "jump":
        k = env(u, 0, 1)
        p.turn("Body", X, -10 * k)
        for name in LEGS:
            p.leg(name, 18 * k if name[0] == "F" else -18 * k, 60 * k)
        p.head(-8 * k)
        p.ears(-6 * k, 20 * k)
        p.tail(0, 25 * k)
    elif clip == "fall":
        for name in LEGS:
            p.leg(name, 10 if name[0] == "F" else -10, 30 + 6 * math.sin(w))
        p.ears(-12 + 6 * math.sin(2 * w), 25)
        p.tail(10 * math.sin(2 * w), 30)
        p.head(-6)
        p.jaw(8 + 4 * math.sin(w))
    elif clip == "land":
        k = pulse(u, 0.0, 0.25, 1.0)
        p.move("Body", (0, 0, -0.09 * k))
        p.turn("Body", X, 4 * k)
        p.ears(18 * k)
        p.head(10 * k)
        p.tail(0, 10 * k)
    elif clip == "graze":
        # head down to the grass, crop (jaw), a little step forward, chew, head back up
        down = env(u, 0.05, 0.25) * (1 - env(u, 0.82, 0.98))
        p.turn("Body", X, 5 * down)
        p.head(30 * down + 2 * math.sin(3 * w) * down, 6 * math.sin(w) * down)
        crop = down * max(0.0, math.sin(2 * math.pi * 3.5 * t))
        p.jaw(16 * crop, 4 * math.sin(2 * math.pi * 1.75 * t) * down)
        step = pulse(u, 0.45, 0.52, 0.6)
        p.leg("FR", 14 * step, 40 * step)
        p.ears(14 * down + 6 * math.sin(w), 6 * down, 0, 10 * pulse(u, 0.7, 0.73, 0.78))
        p.tail(12 * math.sin(w * 2), 2)
        p.move("Body", (0, 0, breathe))
    elif clip == "chew":
        # chewing the cud: head level, the jaw grinding side to side, slow blinks of the ears
        p.move("Body", (0, 0, breathe))
        p.head(2 + 1.5 * math.sin(w), 0, 1.5 * math.sin(2 * math.pi * 1.5 * t))
        p.jaw(5 + 4 * math.sin(2 * math.pi * 1.5 * t), 7 * math.sin(2 * math.pi * 1.5 * t + 0.6))
        p.ears(10, 0, 8 * pulse(u, 0.3, 0.33, 0.38), 8 * pulse(u, 0.62, 0.65, 0.7))
        p.tail(8 * math.sin(w), 2)
    elif clip == "look":
        # looks round: left, then right, ears following
        yaw = 34 * (pulse(u, 0.05, 0.25, 0.5) - pulse(u, 0.45, 0.7, 0.95))
        p.head(-6 * math.sin(math.pi * u), yaw, -0.15 * yaw)
        p.ears(4, -0.4 * abs(yaw), 10 * pulse(u, 0.2, 0.24, 0.3), 10 * pulse(u, 0.65, 0.69, 0.74))
        p.tail(6 * math.sin(w), 2)
        p.move("Body", (0, 0, breathe))
        p.turn("Body", Z, 0.12 * yaw)
    elif clip == "tail_flick":
        # swats a fly: a big tail swish, a shiver over the back, ear flicks, a hind hoof stamps
        sw = math.sin(2 * math.pi * 2.2 * t) * pulse(u, 0.0, 0.2, 1.0)
        p.tail(48 * sw, 18 * pulse(u, 0, 0.2, 1), 30 * sw)
        p.turn("Body", Y, 2 * math.sin(2 * math.pi * 7 * t) * pulse(u, 0.2, 0.3, 0.5))
        p.ears(8, 0, 22 * pulse(u, 0.1, 0.14, 0.2), 22 * pulse(u, 0.35, 0.39, 0.45))
        stamp = pulse(u, 0.5, 0.58, 0.7)
        p.leg("BR", -8 * stamp, 50 * stamp)
        p.head(4, -8 * pulse(u, 0.2, 0.4, 0.8))
    elif clip == "moo":
        # stretches the neck up and out and moos: the jaw opens wide and holds, the body heaves
        k = env(u, 0.05, 0.3) * (1 - env(u, 0.78, 0.98))
        p.head(-26 * k, 0, 0)
        p.jaw(30 * env(u, 0.22, 0.32) * (1 - env(u, 0.7, 0.85)) + 3 * math.sin(2 * math.pi * 6 * t) * k)
        p.move("Body", (0, -0.02 * k, 0.03 * k * math.sin(math.pi * u)))
        p.turn("Body", X, -3 * k)
        p.ears(-10 * k, 14 * k)
        p.tail(0, 10 * k)
    elif clip == "frolic":
        # a calf's play: two bucking hops, the hind legs kicking out behind, the head tossed, the tail straight up
        for start in (0.1, 0.5):
            hop = pulse(u, start, start + 0.12, start + 0.26)
            kick = pulse(u, start + 0.08, start + 0.16, start + 0.28)
            p.move("Body", (0, 0, 0.16 * hop))
            p.turn("Body", X, 10 * kick - 6 * hop)
            for name in LEGS:
                if name[0] == "B":
                    p.leg(name, -36 * kick, 30 * hop)
                else:
                    p.leg(name, 18 * hop, 50 * hop)
            p.head(-14 * hop + 10 * kick, 12 * kick * (1 if start < 0.3 else -1), 10 * kick)
        p.tail(10 * math.sin(2 * math.pi * 3 * t), 45 * env(u, 0.05, 0.15) * (1 - env(u, 0.8, 0.95)))
        p.ears(-8, 20 * env(u, 0.05, 0.15) * (1 - env(u, 0.8, 0.95)), 14 * pulse(u, 0.3, 0.34, 0.4))
        p.jaw(8 * pulse(u, 0.2, 0.3, 0.5))
    elif clip == "eat":
        # eats from a trough or a hand: head forward and down to about knee height, three bites, chews
        k = env(u, 0.0, 0.18) * (1 - env(u, 0.85, 1.0))
        p.head(22 * k, 0)
        p.move("Head", (0, -0.06 * k, 0))
        bites = max(0.0, math.sin(2 * math.pi * 2.2 * t)) * k
        p.jaw(22 * bites, 5 * math.sin(2 * math.pi * 1.1 * t) * k)
        p.ears(12 * k, 0, 10 * pulse(u, 0.4, 0.44, 0.5))
        p.tail(18 * math.sin(2 * w), 4)
        p.turn("Body", X, 3 * k)
    elif clip == "happy":
        # pleased (fed, tamed, milked): a little hop, the tail wags hard, it nuzzles up with ears perked
        hop = pulse(u, 0.05, 0.18, 0.32)
        p.move("Body", (0, 0, 0.1 * hop))
        for name in LEGS:
            p.leg(name, 0, 30 * hop)
        p.tail(40 * math.sin(2 * math.pi * 4 * t), 25 * env(u, 0, 0.1))
        nuzzle = math.sin(2 * math.pi * 1.6 * t) * env(u, 0.3, 0.45)
        p.head(-14 * env(u, 0.1, 0.3) + 10 * nuzzle, 10 * nuzzle, 8 * nuzzle)
        p.ears(-10, 0, 14 * pulse(u, 0.2, 0.25, 0.35), 14 * pulse(u, 0.22, 0.27, 0.37))
        p.jaw(6 * pulse(u, 0.1, 0.25, 0.5))
    elif clip == "lie_down":
        # front knees first, then the back end drops
        kf = env(u, 0.0, 0.55)
        kb = env(u, 0.35, 0.95)
        lie_partial(p, kf, kb)
        p.tail(10 * math.sin(w), 0)
    elif clip == "sleep":
        lie_partial(p, 1, 1)
        p.move("Body", (0, 0, 0.012 * math.sin(2 * math.pi * t / 2.0)))
        p.head(2 * math.sin(2 * math.pi * t / 2.0), 0, 8)
        p.ears(24)
        p.tail(4 * math.sin(w), -4)
    elif clip == "get_up":
        # back end first, then the front, as cattle do
        kb = 1 - env(u, 0.0, 0.5)
        kf = 1 - env(u, 0.3, 0.95)
        lie_partial(p, kf, kb)
        p.tail(10 * math.sin(w), 4)


def lie_partial(p, kf, kb):
    """Lying down by kf at the front and kb at the back (0 standing, 1 lying). The legs fold under."""
    p.move("Body", (0, 0, -0.2 * kf - 0.2 * kb))
    p.turn("Body", X, 9 * kf - 9 * kb)
    for name in LEGS:
        k = kf if name[0] == "F" else kb
        p.leg(name, (40 if name[0] == "F" else -40) * k, 120 * k)
    p.head(-4 * kf + 10 * kf * kb, 0)
    p.ears(10 * kf * kb)


def set_pose(nodes, clip, t, length):
    p = Pose(nodes)
    pose(p, clip, t, length)
    if clip not in ("death", "lie_down", "sleep", "get_up", "jump", "fall"):
        p.ground_feet()
    # a hair of motion on every node, so no channel holds one value through a clip (Godot drops such a track and
    # blends the node back to rest)
    for i, n in enumerate(nodes):
        if not n.endswith("_Foot"):
            p.turn(n, X, 0.2 * math.sin(2 * math.pi * t / length) * (1 if i % 2 else -1))
    p.apply()


def bake_clips(nodes):
    anim = [ob for n, ob in nodes.items() if not n.endswith("_Foot")]
    for ob in anim:
        ob.animation_data_create()
    rest = Pose(nodes)
    rest.apply()
    for clip, length in CLIPS.items():
        acts = {}
        for ob in anim:
            acts[ob] = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = acts[ob]
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            set_pose(nodes, clip, f / FPS, length)
            for ob in anim:
                ob.keyframe_insert("rotation_quaternion", frame=f)
                ob.keyframe_insert("location", frame=f)
        for ob, act in acts.items():
            lt.linear_keys(act)
            sc.push_track(ob, act, clip)
    rest.apply()


def use(coat):
    COAT[0] = coat


def build_game(coats):
    for coat in coats:
        use(coat)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root, nodes = build()
        tris = sum(len(ob.data.polygons) for ob in root.children_recursive if ob.type == "MESH")
        bake_clips(nodes)
        root.scale = (SCALE, SCALE, SCALE)
        out = MESHES / f"{COATS[coat][0]}{'_calf' if CALF[0] else ''}.glb"
        sc.export(root, out)
        speeds = ", ".join(f"{clip} {gait_speed(clip) * SCALE:.2f}" for clip in GAITS)
        print(f"wrote {out.relative_to(REPO)} ({tris} faces; clip speeds, m/s: {speeds})")


# =====================================================================================================
# Review renders
# =====================================================================================================
REVIEW = [("patrol", 0.25, "side"), ("chase", 0.2, "side"), ("chase", 0.6, "tq"), ("alert", 0.25, "tq"),
          ("hit", 0.08, "tq"), ("graze", 0.3, "tq"), ("graze", 0.5, "side"), ("chew", 0.6, "face"),
          ("look", 0.25, "tq"), ("tail_flick", 0.2, "rear"), ("moo", 0.45, "tq"), ("eat", 0.4, "side"),
          ("happy", 0.16, "tq"), ("lie_down", 0.7, "tq"), ("sleep", 0.5, "tq"), ("death", 1.0, "tq")]


def render_review(out: Path, coat, only=None):
    use(coat)
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    for ob in bpy.data.objects:
        if ob.type == "LIGHT":
            ob.data.energy *= 0.7
    bpy.context.scene.cycles.samples = SAMPLES
    root, nodes = build()
    root.rotation_euler = (0, 0, 0)          # renders keep the build's -Y front
    c = (0, -0.2, 0.6)
    cams = {
        "tq": rl.camera("tq", (-3.6, -4.0, 2.0), c, ortho=2.4),
        "side": rl.camera("side", (-6, -0.2, 0.7), c, ortho=2.6),
        "front": rl.camera("front", (0, -6, 0.7), c, ortho=2.3),
        "rear": rl.camera("rear", (3.4, 4.2, 1.8), c, ortho=2.4),
        "face": rl.camera("face", (-2.4, -3.6, 1.5), (0, -1.0, 0.8), ortho=1.35),
    }
    shots = []

    def take(name, clip, t, cam):
        if only and name not in only:
            return
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t * CLIPS[clip], CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cams[cam], out / f"{coat}{'_calf' if CALF[0] else ''}_{name}.png", 800, 900)
        shots.append((name, path))

    take("rest", None, 0, "tq")
    take("front", None, 0, "front")
    take("side", None, 0, "side")
    take("rear", None, 0, "rear")
    for clip, t, cam in REVIEW:
        take(f"{clip}_{t:g}", clip, t, cam)
    if only:
        return
    from render_lift import INK, font as F  # noqa: PLC0415
    w, h = 400, 450
    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (60 + cols * (w + 16) + 40, 150 + rows * (h + 16) + 20), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), f"GRAZER{' CALF' if CALF[0] else ''}, {coat.upper()} COAT (game model and clips)", fill=INK, font=F(40, True))
    d.text((60, 86), "Final game model with its fur textures, in its rest pose and at key frames of its clips.",
           fill=INK, font=F(20))
    for i, (name, path) in enumerate(shots):
        x, y = 60 + (i % cols) * (w + 16), 130 + (i // cols) * (h + 16)
        sheet.paste(Image.open(path).convert("RGB").resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(190, 190, 190), width=2)
        d.text((x + 10, y + 8), name.replace("_", " ").upper(), fill=INK, font=F(16, True))
    sheet.save(out / f"{coat}{'_calf' if CALF[0] else ''}_review_sheet.png")
    print(f"rendered the review sheet into {out}")


if __name__ == "__main__":
    args = sys.argv[1:]
    chosen = [args[i + 1] for i, a in enumerate(args) if a == "--coat"] or list(COATS)
    only = [args[i + 1] for i, a in enumerate(args) if a == "--only"]
    if "--calf" in args:
        calf()
    if "--render" in args:
        for key in chosen:
            render_review(Path(args[args.index("--render") + 1]), key, only)
    else:
        build_game(chosen)
