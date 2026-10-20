"""Build the three Threshers (Chase approved the three concepts on 2026-10-20).

Threshers are 3.5 m farm machines come alive: melee-immune bosses at scrapyards and in the top raid tiers. Each is a
different machine with its own attacks. They share the Scuttlers' battle-worn paint and one big eye under a cab roof
(green while calm; the game turns it red while engaged):

* Harvester (``thresher``, grey and mustard): a walking combine. A grain-tank body on two digitigrade legs with
  track feet, a turning reel header across the chest, a cab head, a sickle on the right arm (``swipe``), a sprayer
  on the left fed from a pesticide tank on its back (``pesticide``), a track-foot stomp (``kick``), two exhaust
  stacks and the unloading auger arched over its shoulder.
* Tiller (``thresher_tiller``, rust red and cream): a four-legged tractor on lugged-wheel feet. A long hood, a cab
  over the rear legs, a ballast block, and a rotary tiller drum on a hitch at the front that drops to grind parts
  (``till``); it rears up and slams its front wheels down in a shockwave (``stomp``).
* Duster (``thresher_duster``, olive and hazard yellow): a crop sprayer up on two tall stilt legs with narrow wheel
  feet. A round chemical tank for a body, a small cab head with a beacon, spray booms folded back along the tank
  that swing out wide into a pesticide cloud (``pesticide``), and a canister mortar on its back (``canister``).

What it writes: ``mods/vanilla/meshes/bots/<id>.glb``. Feet are at the origin and it faces -Z, as bots do. The
nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the pelvis or chassis), ``Torso`` (not on the Tiller), ``Head``, ``EyeLens`` (the glowing eye);
* legs ``Leg<side>_Hip`` / ``_Knee`` / ``_Ankle`` / ``_Foot`` (``_Foot`` is an empty on the ground under the foot);
  the Harvester and Duster have ``L`` and ``R``, the Tiller ``FL``, ``FR``, ``BL`` and ``BR``;
* Harvester: ``Reel``, ``ArmR_Shoulder`` / ``ArmR_Elbow`` / ``ArmR_Wrist`` (the sickle), ``ArmL_Shoulder`` /
  ``ArmL_Elbow`` (the sprayer) and ``Mist`` (the spray, shrunk away until it sprays);
* Tiller: ``Hitch`` / ``Drum`` (the tiller), ``StackCap`` and ``Leg<side>_Wheel``;
* Duster: ``Boom<L|R>_Shoulder`` / ``_Elbow`` with ``Boom<L|R>_Mist0`` / ``_Mist1`` under them, ``Beacon``,
  ``Mortar`` and ``Shot`` (the canister in the mortar, which the game hides and flies to what the ``shot`` effect
  hit), ``Leg<L|R>_Wheel``.

Clips (seconds; the loops loop): ``idle``, ``patrol`` and ``chase`` (gaits; their ground speeds at speed scale 1 are
printed and go in the bot's ``model.clip_speeds``), ``alert``, ``lose_interest``, ``hit``, ``death``, ``jump``,
``fall``, ``land``, and the attacks: Harvester ``track_stomp`` (kick), ``sickle_swipe`` (swipe), ``spray_sweep``
(pesticide); Tiller ``rear_stomp`` (stomp), ``till_grind`` (till); Duster ``boom_spray`` (pesticide),
``mortar_shot`` (canister). The host deals an attack's damage as it starts, so each blow lands early in its clip.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/thresher/build_thresher.py [--variant harvester ...]              # writes the .glb files
    python tools/thresher/build_thresher.py --render OUT_DIR [--variant ...]       # review renders and sheets

The output is committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up and the model is built facing -Y, then turned to face -Z in glTF. The build is
deterministic. Geometry helpers, paint, clip baking and export are the Scuttler build's (tools/scuttler); the rig,
pose and clip baking follow the Spooler build's (tools/spooler).
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "lift"))
sys.path.insert(0, str(REPO / "tools" / "scuttler"))
import lift_textures as lt  # noqa: E402
import build_scuttler as sc  # noqa: E402
from build_scuttler import at, disc, env, pulse, rod, sphere  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "bots"
FPS = sc.FPS
SAMPLES = int(os.environ.get("THRESHER_SAMPLES", "48"))
X = Vector((1, 0, 0))
Y = Vector((0, 1, 0))
Z = Vector((0, 0, 1))
UP = Z
V = Vector

VARIANTS = {
    "harvester": dict(id="thresher", name="Harvester", paint="#7a7f86", trim="#d9a520",
                      attacks={"kick": ["track_stomp"], "swipe": ["sickle_swipe"], "pesticide": ["spray_sweep"]}),
    "tiller": dict(id="thresher_tiller", name="Tiller", paint="#a8402a", trim="#e2d6b8",
                   attacks={"stomp": ["rear_stomp"], "till": ["till_grind"]}),
    "duster": dict(id="thresher_duster", name="Duster", paint="#6b7a2e", trim="#e8b820",
                   attacks={"pesticide": ["boom_spray"], "canister": ["mortar_shot"]}),
}
ATTACK_CLIPS = {"track_stomp": 1.1, "sickle_swipe": 1.5, "spray_sweep": 1.8, "rear_stomp": 1.7, "till_grind": 1.5,
                "boom_spray": 2.0, "mortar_shot": 1.1}
VARIANT = ["harvester"]
MIST_REST = 0.01
SHOT_SCALE = 1.8          # how much bigger the flying canister is than the one drawn in the mortar          # the spray nodes' rest scale: shrunk into the nozzles until they spray


def cfg():
    return VARIANTS[VARIANT[0]]


def mirror(v, sx):
    return V((v.x * sx, v.y, v.z))


# =====================================================================================================
# Shared pieces
# =====================================================================================================
def box(size, c, mat, bevel=0.02, rx=0.0, rz=0.0):
    sc.box(size, c, mat, bevel=bevel, rot=Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(rx, 4, "X"))


def limb(a, b, r_in, r_out, armour=True):
    """A gunmetal limb with a joint ball at its top and, by default, an octagonal armour sleeve."""
    rod(a, b, r_in, "gun")
    if armour:
        rod(a.lerp(b, 0.12), a.lerp(b, 0.82), r_out, "paint", segs=8)
    sphere(a, r_in * 1.6, "steel")


def ram_cylinder(a, b, r):
    """The cylinder half of a hydraulic ram from a to b (it sits on the node at a)."""
    rod(a, a.lerp(b, 0.55), r, "trim")


def ram_rod(a, b, r):
    """The rod half of the ram (on the node at b): it slides in and out of the cylinder as the joint bends."""
    rod(a.lerp(b, 0.45), b, r * 0.5, "steel")


def eye(head, c, r, axis=Y):
    """The bezel on `head`, the glowing lens on EyeLens."""
    at(head)
    disc(c, r + 0.04, 0.08, axis, "bezel")
    at("EyeLens")
    sphere(c - axis * 0.03, r, "glow", scale=(1, 0.45, 1))


def hazard(c, w, h, n, rx=0.0):
    for i in range(n):
        box((w / n, 0.02, h), c + V((-w / 2 + w / n * (i + 0.5), 0, 0)), "haz_y" if i % 2 == 0 else "haz_k", bevel=0.002, rx=rx)


def hose(pts, r):
    """An open hose through `pts`: rods with a ball at each bend."""
    for a, b in zip(pts, pts[1:]):
        rod(a, b, r, "rubber", segs=10)
        sphere(b, r, "rubber", segs=10)


def track_foot(c, w=0.42, ln=0.95):
    """A tractor-track foot: a rubber track round two road wheels, a lug row on top, a steel frame over it."""
    box((w, ln, 0.26), c + V((0, 0, 0.13)), "tread", bevel=0.1)
    for k in range(11):
        y = -ln / 2 + 0.06 + k * (ln - 0.12) / 10
        box((w + 0.02, 0.04, 0.035), c + V((0, y, 0.262)), "tread", bevel=0.005)
        box((w + 0.02, 0.04, 0.035), c + V((0, y, -0.002)), "tread", bevel=0.005)
    for dy in (-ln / 2 + 0.14, ln / 2 - 0.14):
        disc(c + V((0, dy, 0.13)), 0.11, w + 0.05, X, "steel")
        disc(c + V((0, dy, 0.13)), 0.05, w + 0.09, X, "trim")
    box((w * 0.6, ln * 0.6, 0.12), c + V((0, 0, 0.3)), "paint", bevel=0.03)


def lug_wheel(c, r, w, lugs, hub_sx):
    """A tyre with chevron lugs and a trim hub on its outer face (hub_sx: which side faces out)."""
    disc(c, r, w, X, "tread", segs=28)
    disc(c + V((hub_sx * w / 2, 0, 0)), r * 0.5, 0.04, X, "trim", segs=20)
    disc(c + V((hub_sx * (w / 2 + 0.03), 0, 0)), r * 0.16, 0.05, X, "steel", segs=10)
    for k in range(lugs):
        a = 2 * math.pi * k / lugs
        for s in (-1, 1):
            box((w * 0.5, 0.07, 0.05), c + V((s * w * 0.24, (r + 0.01) * math.cos(a), (r + 0.01) * math.sin(a))), "tread",
                bevel=0.01, rx=a + s * 0.0, rz=s * 0.35)


def mist_puffs(node, start, d, n, r0, dr, spread=0.08):
    """A spray cloud along d from start: overlapping soft balls that grow away from the nozzle."""
    at(node)
    u = d.cross(Z).normalized()
    w = d.cross(u).normalized()
    for k in range(n):
        for j in range(3 if k > 2 else 1):
            a = 2.1 * j + 1.3 * k
            off = (u * math.cos(a) + w * math.sin(a)) * (spread + 0.07 * k) * (1 if j else 0.3)
            sphere(start + d * (k * 0.24) + off, r0 + k * dr, "mist", segs=10)


# =====================================================================================================
# A: Harvester
# =====================================================================================================
H_HIP = V((0.5, 0.15, 1.65))
H_KNEE = V((0.62, -0.38, 1.05))
H_ANKLE = V((0.58, 0.18, 0.42))
H_SOLE = V((0.58, -0.05, 0.0))
H_PELVIS = V((0, 0.12, 1.72))
H_WAIST = V((0, 0.15, 1.95))
H_NECK = V((0, -0.15, 2.95))
H_REEL = V((0, -0.95, 2.15))
H_SH_R, H_EL_R, H_WR_R = V((0.95, 0.1, 2.65)), V((1.25, -0.35, 2.05)), V((1.2, -0.85, 1.65))
H_SH_L, H_EL_L, H_WR_L = V((-0.95, 0.1, 2.65)), V((-1.3, -0.25, 2.15)), V((-1.25, -0.6, 1.75))


def h_spray_dir():
    return (H_WR_L - H_EL_L).normalized()


def h_nozzle():
    return H_WR_L + h_spray_dir() * 0.34


def harvester_geometry():
    for side, sx in (("L", -1), ("R", 1)):
        hip, knee, ankle, sole = (mirror(p, sx) for p in (H_HIP, H_KNEE, H_ANKLE, H_SOLE))
        at(f"Leg{side}_Hip")
        limb(hip, knee, 0.15, 0.24)
        ram_cylinder(hip + V((sx * 0.12, 0.22, -0.05)), knee + V((sx * 0.14, 0.05, 0.05)), 0.055)
        at(f"Leg{side}_Knee")
        limb(knee, ankle, 0.14, 0.21)
        sphere(knee, 0.2, "trim", scale=(0.9, 1.1, 0.9))
        ram_rod(hip + V((sx * 0.12, 0.22, -0.05)), knee + V((sx * 0.14, 0.05, 0.05)), 0.055)
        ram_cylinder(knee + V((0, -0.12, -0.1)), ankle + V((0, 0.1, 0.12)), 0.045)
        at(f"Leg{side}_Ankle")
        sphere(ankle, 0.14, "gun")
        ram_rod(knee + V((0, -0.12, -0.1)), ankle + V((0, 0.1, 0.12)), 0.045)
        rod(ankle, V((ankle.x, sole.y, 0.3)), 0.08, "gun")
        track_foot(V((ankle.x, sole.y, 0.0)))
    at("Body")
    box((1.2, 0.8, 0.42), H_PELVIS, "gun", bevel=0.05)
    for sx in (-1, 1):
        box((0.12, 0.6, 0.3), H_PELVIS + V((sx * 0.62, 0, 0)), "trim", bevel=0.03)
    at("Torso")
    # the grain-tank body: slab-sided, patched, a mustard band round the top, a hazard strip on the front skirt
    box((1.55, 1.15, 1.05), V((0, 0.15, 2.4)), "paint", bevel=0.07)
    box((1.6, 1.2, 0.08), V((0, 0.15, 2.92)), "trim", bevel=0.02)
    box((1.6, 1.2, 0.06), V((0, 0.15, 1.92)), "gun", bevel=0.02)
    for k, (x, z) in enumerate(((-0.55, 2.2), (0.5, 2.58), (0.12, 2.12), (-0.2, 2.62))):
        box((0.3 - 0.05 * (k % 2), 0.02, 0.22), V((x, -0.435, z)), "steel", bevel=0.01)
        for dx in (-1, 1):
            sphere(V((x + dx * 0.11, -0.45, z + 0.07)), 0.014, "steel", segs=6)
    for sx in (-1, 1):
        for k in range(3):
            box((0.02, 0.9, 0.04), V((sx * 0.785, 0.15, 2.05 + 0.25 * k)), "gun", bevel=0.005)
    # back: the pesticide tank in its cradle, two exhaust stacks, the unloading auger over the left shoulder
    disc(V((0, 0.95, 2.45)), 0.42, 1.2, X, "tank", segs=28)
    for sx in (-1, 1):
        disc(V((sx * 0.62, 0.95, 2.45)), 0.3, 0.04, X, "tank", segs=20)
        disc(V((sx * 0.35, 0.95, 2.45)), 0.435, 0.06, X, "steel", segs=28)
    hazard(V((0, 1.38, 2.45)), 0.5, 0.12, 5)
    box((0.9, 0.2, 0.1), V((0, 0.85, 2.0)), "gun", bevel=0.02)
    for k, x in enumerate((0.42, 0.6)):
        top = 3.8 - k * 0.15
        rod(V((x, 0.55, 2.9)), V((x, 0.6, top)), 0.065, "gun")
        rod(V((x, 0.6, top)), V((x, 0.62, top + 0.08)), 0.085, "gun")
        disc(V((x, 0.57, 3.1)), 0.085, 0.05, Z, "trim", segs=16)
    pts = [V((-0.45, 0.55, 2.9)), V((-0.6, 0.45, 3.35)), V((-0.8, 0.1, 3.55)), V((-1.0, -0.35, 3.45)), V((-1.05, -0.6, 3.25))]
    for a, b in zip(pts, pts[1:]):
        rod(a, b, 0.09, "trim", segs=16)
        sphere(b, 0.095, "trim", segs=16)
        rod(a.lerp(b, 0.5) + V((0, 0, 0.0)), a.lerp(b, 0.52), 0.1, "gun", segs=16)
    disc(pts[-1] + V((0, 0, -0.08)), 0.12, 0.12, Z, "gun")
    rod(V((-0.45, 0.6, 2.75)), pts[0], 0.11, "gun", segs=16)
    # the reel header's frame: a trough with a hazard lip, a back plate, side plates and arms up to the body
    hc = H_REEL
    box((1.9, 0.5, 0.18), hc + V((0, 0.05, -0.42)), "trim", bevel=0.03)
    box((1.9, 0.08, 0.7), hc + V((0, 0.33, -0.1)), "paint", bevel=0.02)
    hazard(hc + V((0, -0.21, -0.42)), 1.9, 0.12, 10)
    rod(hc + V((-0.9, 0.05, -0.36)), hc + V((0.9, 0.05, -0.36)), 0.07, "steel", segs=12)
    for sx in (-1, 1):
        box((0.06, 0.75, 0.75), hc + V((sx * 0.97, 0.05, -0.05)), "paint", bevel=0.02)
        disc(hc + V((sx * 1.0, 0, 0)), 0.08, 0.06, X, "trim", segs=14)
        rod(hc + V((sx * 0.85, 0.35, 0.1)), V((sx * 0.6, -0.3, 2.35)), 0.06, "gun")
    at("Reel")
    rod(hc + V((-0.94, 0, 0)), hc + V((0.94, 0, 0)), 0.04, "steel")
    for k in range(6):
        a = 2 * math.pi * k / 6
        p = hc + V((0, 0.38 * math.cos(a), 0.38 * math.sin(a)))
        rod(p + V((-0.9, 0, 0)), p + V((0.9, 0, 0)), 0.028, "trim", segs=8)
        for j in range(8):
            q = p + V((-0.8 + j * 0.23, 0, 0))
            tip = q + V((0, 0.1 * math.cos(a + 1.2), 0.1 * math.sin(a + 1.2)))
            rod(q, tip, 0.009, "steel", r2=0.004, segs=6)
        for sx in (-1, 1):
            rod(hc + V((sx * 0.88, 0, 0)), p + V((sx * 0.88, 0, 0)), 0.018, "gun", segs=6)
    # the cab head: glass front, a mustard roof with work lamps, one big eye under it, a vent on top
    at("Head")
    box((0.85, 0.7, 0.6), V((0, -0.15, 3.25)), "paint", bevel=0.05)
    box((0.75, 0.04, 0.32), V((0, -0.51, 3.3)), "glass", bevel=0.01)
    box((1.0, 0.85, 0.08), V((0, -0.17, 3.6)), "trim", bevel=0.02)
    box((0.3, 0.3, 0.1), V((0.15, -0.05, 3.68)), "gun", bevel=0.02)
    box((0.7, 0.5, 0.12), V((0, -0.1, 2.97)), "gun", bevel=0.03)
    for sx in (-1, 1):
        disc(V((sx * 0.38, -0.6, 3.6)), 0.05, 0.06, Y, "glow")
        box((0.03, 0.04, 0.3), V((sx * 0.38, -0.53, 3.3)), "gun", bevel=0.005)
    eye("Head", V((0, -0.53, 3.28)), 0.13)
    # right arm: shoulder and pauldron, upper arm; forearm; the wrist carries the sickle on edge
    at("ArmR_Shoulder")
    sphere(H_SH_R, 0.2, "paint")
    box((0.4, 0.5, 0.12), H_SH_R + V((0.08, 0, 0.18)), "trim", bevel=0.03)
    hazard(H_SH_R + V((0.08, -0.26, 0.18)), 0.4, 0.1, 4)
    limb(H_SH_R, H_EL_R, 0.12, 0.19)
    at("ArmR_Elbow")
    limb(H_EL_R, H_WR_R, 0.11, 0.17)
    at("ArmR_Wrist")
    sphere(H_WR_R, 0.15, "gun")
    sickle(H_WR_R, H_EL_R)
    # left arm: the sprayer, fed by a hose from the tank
    at("ArmL_Shoulder")
    sphere(H_SH_L, 0.2, "paint")
    box((0.4, 0.5, 0.12), H_SH_L + V((-0.08, 0, 0.18)), "trim", bevel=0.03)
    limb(H_SH_L, H_EL_L, 0.12, 0.19)
    hose([H_SH_L + V((0.05, 0.2, 0.05)), H_SH_L.lerp(H_EL_L, 0.5) + V((-0.05, 0.18, 0.0)), H_EL_L + V((0, 0.12, 0.0))], 0.035)
    at("ArmL_Elbow")
    limb(H_EL_L, H_WR_L, 0.11, 0.17)
    d = h_spray_dir()
    head = H_WR_L + d * 0.2
    rod(H_WR_L, head, 0.16, "gun")
    disc(H_WR_L + d * 0.04, 0.17, 0.05, d, "trim", segs=20)
    for k in range(5):
        a = 2 * math.pi * k / 5
        u = d.cross(Z).normalized()
        w = d.cross(u).normalized()
        off = u * 0.08 * math.cos(a) + w * 0.08 * math.sin(a)
        rod(head + off, head + off + d * 0.14, 0.025, "trim", segs=8)
    hose([H_EL_L + V((0, 0.12, 0.0)), H_EL_L.lerp(H_WR_L, 0.5) + V((-0.12, 0.05, 0.05)), H_WR_L + V((-0.1, 0.0, 0.08))], 0.035)
    at("Torso")
    hose([V((-0.4, 1.0, 2.1)), V((-0.75, 0.75, 2.4)), V((-0.9, 0.3, 2.7))], 0.035)
    mist_puffs("Mist", h_nozzle() + d * 0.15, d, 11, 0.07, 0.025)


def sickle(wr, el):
    """A sickle held on edge: a long crescent blade that curves down at the tip, a mustard spine on top and a row of
    saw teeth along the cutting (lower) edge."""
    dh = V(((wr - el).x, (wr - el).y, 0)).normalized()
    base = wr + dh * 0.15
    rod(wr, base, 0.09, "gun")
    box((0.12, 0.2, 0.3), base + V((0, 0, -0.05)), "trim", bevel=0.02, rz=math.atan2(-dh.x, dh.y))
    pts = [base + dh * (1.55 * i / 12) + V((0, 0, -0.55 * (i / 12) ** 2.2)) for i in range(13)]
    for i in range(12):
        a, b = pts[i], pts[i + 1]
        seg = b - a
        ang = math.atan2(-seg.x, seg.y)
        pitch = math.atan2(seg.z, V((seg.x, seg.y, 0)).length)
        h = 0.34 - 0.24 * (i / 12)
        c = (a + b) / 2
        box((0.04, seg.length + 0.03, h), c + V((0, 0, -h / 2 + 0.06)), "blade", bevel=0.006, rz=ang, rx=pitch)
        box((0.08, seg.length + 0.03, 0.07), c + V((0, 0, 0.06)), "trim", bevel=0.012, rz=ang, rx=pitch)
        for j in range(2):
            q = a.lerp(b, 0.25 + 0.5 * j) + V((0, 0, -h + 0.06))
            rod(q, q + V((0, 0, -0.09)) - dh * 0.03, 0.03, "blade", r2=0.002, segs=4)


def harvester_pivots():
    p = {"Body": (None, H_PELVIS), "Torso": ("Body", H_WAIST), "Reel": ("Torso", H_REEL), "Head": ("Torso", H_NECK),
         "EyeLens": ("Head", H_NECK), "ArmR_Shoulder": ("Torso", H_SH_R), "ArmR_Elbow": ("ArmR_Shoulder", H_EL_R),
         "ArmR_Wrist": ("ArmR_Elbow", H_WR_R), "ArmL_Shoulder": ("Torso", H_SH_L), "ArmL_Elbow": ("ArmL_Shoulder", H_EL_L),
         "Mist": ("ArmL_Elbow", h_nozzle())}
    for side, sx in (("L", -1), ("R", 1)):
        p[f"Leg{side}_Hip"] = ("Body", mirror(H_HIP, sx))
        p[f"Leg{side}_Knee"] = (f"Leg{side}_Hip", mirror(H_KNEE, sx))
        p[f"Leg{side}_Ankle"] = (f"Leg{side}_Knee", mirror(H_ANKLE, sx))
        p[f"Leg{side}_Foot"] = (f"Leg{side}_Ankle", mirror(H_SOLE, sx))
    return p


# =====================================================================================================
# B: Tiller
# =====================================================================================================
T_Z = 1.35
T_LEGS = {f"{fb}{side}": (sx, sy, ly) for fb, sy, ly in (("F", -1, -0.7), ("B", 1, 0.75)) for side, sx in (("L", -1), ("R", 1))}
T_HITCH = V((0, -1.1, T_Z - 0.1))
T_DRUM = V((0, -2.0, 0.95))
T_CAB = V((0, 0.8, T_Z + 0.48))
T_STACK = V((0.35, -0.55, T_Z + 2.0))
T_WHEEL_R = 0.4


def t_leg(name):
    sx, sy, ly = T_LEGS[name]
    hip = V((sx * 0.72, ly, T_Z - 0.1))
    knee = V((sx * 1.1, ly + sy * 0.15, T_Z - 0.35))
    foot = V((sx * 1.05, ly + sy * 0.05, T_WHEEL_R + 0.02))
    return hip, knee, foot


def tiller_geometry():
    z = T_Z
    for name, (sx, sy, ly) in T_LEGS.items():
        hip, knee, foot = t_leg(name)
        at(f"Leg{name}_Hip")
        limb(hip, knee, 0.12, 0.18)
        box((0.3, 0.4, 0.08), hip.lerp(knee, 0.45) + V((0, 0, 0.17)), "trim", bevel=0.02, rx=0, rz=0)
        ram_cylinder(hip + V((-sx * 0.1, 0, 0.2)), foot + V((0, 0, 0.3)), 0.06)
        at(f"Leg{name}_Knee")
        limb(knee, foot, 0.11, 0.16, armour=False)
        box((0.18, 0.26, 0.5), knee.lerp(foot, 0.4), "paint", bevel=0.03)
        ram_rod(hip + V((-sx * 0.1, 0, 0.2)), foot + V((0, 0, 0.3)), 0.06)
        at(f"Leg{name}_Ankle")
        sphere(foot, 0.12, "gun")
        for s2 in (-1, 1):
            rod(foot, foot + V((s2 * 0.2, 0, 0)), 0.05, "gun")
            box((0.05, 0.16, 0.28), foot + V((s2 * 0.21, 0, 0.1)), "gun", bevel=0.015)
        box((0.5, 0.5, 0.06), foot + V((0, 0, T_WHEEL_R + 0.12)), "paint", bevel=0.02)
        at(f"Leg{name}_Wheel")
        lug_wheel(foot, T_WHEEL_R, 0.34, 12, sx)
    at("Body")
    box((1.3, 2.3, 0.45), V((0, 0, z)), "gun", bevel=0.05)
    for sx in (-1, 1):
        box((0.06, 2.0, 0.2), V((sx * 0.66, 0, z - 0.05)), "trim", bevel=0.01)
    # hood: a long tapered engine cover with louvres, a grille, headlamps and a cream stripe
    sc.prism_z([(-0.55, -1.25), (0.55, -1.25), (0.6, 0.3), (-0.6, 0.3)], z + 0.22, z + 0.85, "paint", bevel=0.06, top_scale=0.93)
    box((1.12, 1.4, 0.05), V((0, -0.47, z + 0.86)), "trim", bevel=0.01)
    box((0.9, 0.06, 0.5), V((0, -1.27, z + 0.5)), "gun", bevel=0.02)
    for k in range(7):
        box((0.9, 0.03, 0.03), V((0, -1.31, z + 0.3 + k * 0.065)), "steel", bevel=0.004)
    for sx in (-1, 1):
        for k in range(4):
            box((0.02, 0.22, 0.04), V((sx * 0.6, -0.6 + k * 0.25, z + 0.55)), "gun", bevel=0.003)
        disc(V((sx * 0.38, -1.29, z + 0.75)), 0.08, 0.06, Y, "glow")
        disc(V((sx * 0.38, -1.27, z + 0.75)), 0.1, 0.05, Y, "bezel")
    box((0.5, 0.1, 0.16), V((0, -1.32, z + 0.12)), "trim", bevel=0.02)
    rod(V((0.35, -0.55, z + 0.85)), T_STACK, 0.08, "gun")
    disc(V((0.35, -0.55, z + 1.25)), 0.1, 0.06, Z, "trim", segs=16)
    at("StackCap")
    disc(T_STACK + V((0, 0, 0.03)), 0.11, 0.03, Z, "gun", segs=16)
    rod(T_STACK + V((0, 0.1, 0)), T_STACK + V((0, 0.12, 0.05)), 0.02, "steel", segs=6)
    at("Body")
    # ballast block on the back with a hazard edge, the cab's floor
    box((1.1, 0.35, 0.5), V((0, 1.35, z - 0.05)), "trim", bevel=0.04)
    for k in range(4):
        box((0.24, 0.06, 0.4), V((-0.39 + k * 0.26, 1.54, z - 0.05)), "gun", bevel=0.02)
    hazard(V((0, 1.58, z + 0.24)), 1.1, 0.08, 8)
    box((1.2, 1.0, 0.25), V((0, 0.75, z + 0.35)), "paint", bevel=0.04)
    # the cab over the rear legs: roll cage, glass, one eye in a visor
    at("Head")
    box((1.0, 0.85, 0.75), V((0, 0.8, z + 0.85)), "paint", bevel=0.05)
    box((0.85, 0.04, 0.38), V((0, 0.36, z + 0.95)), "glass", bevel=0.01)
    for sx in (-1, 1):
        box((0.04, 0.6, 0.35), V((sx * 0.5, 0.82, z + 0.95)), "glass", bevel=0.01)
    box((1.15, 1.0, 0.08), V((0, 0.8, z + 1.27)), "trim", bevel=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            rod(V((sx * 0.52, 0.8 + sy * 0.45, z + 0.5)), V((sx * 0.52, 0.8 + sy * 0.45, z + 1.25)), 0.035, "steel")
    box((0.5, 0.12, 0.22), V((0, 0.33, z + 1.15)), "gun", bevel=0.02)
    eye("Head", V((0, 0.26, z + 1.13)), 0.1)
    # the hitch arms, rams and the tiller drum's housing
    dc = T_DRUM
    at("Hitch")
    for sx in (-1, 1):
        rod(V((sx * 0.5, -1.1, z - 0.1)), dc + V((sx * 0.85, 0.15, 0.25)), 0.07, "gun")
        sphere(V((sx * 0.5, -1.1, z - 0.1)), 0.1, "steel")
        ram_cylinder(V((sx * 0.35, -1.2, z + 0.25)), dc + V((sx * 0.7, 0.1, 0.4)), 0.055)
        ram_rod(V((sx * 0.35, -1.2, z + 0.25)), dc + V((sx * 0.7, 0.1, 0.4)), 0.055)
        box((0.06, 0.6, 0.6), dc + V((sx * 0.95, 0.0, 0.05)), "paint", bevel=0.02)
        disc(dc + V((sx * 0.99, 0, 0)), 0.09, 0.05, X, "trim", segs=14)
    rod(V((-0.5, -1.1, z - 0.1)), V((0.5, -1.1, z - 0.1)), 0.06, "steel")
    box((1.95, 0.75, 0.08), dc + V((0, 0.0, 0.4)), "paint", bevel=0.02, rx=-0.2)
    hazard(dc + V((0, -0.38, 0.33)), 1.9, 0.1, 10, rx=-0.2)
    at("Drum")
    rod(dc + V((-0.92, 0, 0)), dc + V((0.92, 0, 0)), 0.12, "steel")
    for j in range(8):
        x = -0.82 + j * 0.235
        disc(dc + V((x, 0, 0)), 0.16, 0.03, X, "gun", segs=16)
        for k in range(4):
            a = 2 * math.pi * k / 4 + j * 0.6
            p0 = dc + V((x, 0.15 * math.cos(a), 0.15 * math.sin(a)))
            p1 = dc + V((x, 0.36 * math.cos(a), 0.36 * math.sin(a)))
            p2 = p1 + V((0.08 * (1 if k % 2 else -1), -0.1 * math.sin(a), 0.1 * math.cos(a)))
            rod(p0, p1, 0.024, "blade", segs=6)
            rod(p1, p2, 0.024, "blade", r2=0.008, segs=6)


def tiller_pivots():
    p = {"Body": (None, V((0, 0, T_Z))), "Head": ("Body", T_CAB), "EyeLens": ("Head", T_CAB), "StackCap": ("Body", T_STACK),
         "Hitch": ("Body", T_HITCH), "Drum": ("Hitch", T_DRUM)}
    for name in T_LEGS:
        hip, knee, foot = t_leg(name)
        p[f"Leg{name}_Hip"] = ("Body", hip)
        p[f"Leg{name}_Knee"] = (f"Leg{name}_Hip", knee)
        p[f"Leg{name}_Ankle"] = (f"Leg{name}_Knee", foot)
        p[f"Leg{name}_Wheel"] = (f"Leg{name}_Ankle", foot)
        p[f"Leg{name}_Foot"] = (f"Leg{name}_Ankle", V((foot.x, foot.y, 0)))
    return p


# =====================================================================================================
# C: Duster
# =====================================================================================================
D_HIP = V((0.55, 0.0, 2.75))
D_KNEE = V((0.95, -0.25, 1.6))
D_ANKLE = V((0.85, 0.15, 0.62))
D_WHEEL = V((0.85, 0.05, 0.42))
D_PELVIS = V((0, 0, 2.78))
D_WAIST = V((0, 0.05, 2.95))
D_NECK = V((0, -0.45, 3.6))
D_SH = V((0.72, -0.1, 3.3))
D_BOOM = (V((0.3, 0.9, 0.1)), V((0.38, 1.9, 0.18)))        # the boom's elbow and tip from the shoulder, folded back
D_MORTAR = V((0, 0.75, 3.55))
D_AIM = V((0, 0.35, 1.0)).normalized()
D_BEACON = V((0, -0.76, 4.03))


def d_shot():
    return D_MORTAR + D_AIM * 0.62


def duster_geometry():
    for side, sx in (("L", -1), ("R", 1)):
        hip, knee, ankle, wheel = (mirror(p, sx) for p in (D_HIP, D_KNEE, D_ANKLE, D_WHEEL))
        at(f"Leg{side}_Hip")
        limb(hip, knee, 0.08, 0.12)
        ram_cylinder(hip + V((sx * 0.1, 0.15, -0.1)), knee + V((sx * 0.06, 0.1, 0.05)), 0.045)
        at(f"Leg{side}_Knee")
        limb(knee, ankle, 0.07, 0.1, armour=False)
        sphere(knee, 0.12, "trim")
        ram_rod(hip + V((sx * 0.1, 0.15, -0.1)), knee + V((sx * 0.06, 0.1, 0.05)), 0.045)
        ram_cylinder(knee + V((0, 0.08, -0.05)), ankle + V((0, 0.1, 0.15)), 0.035)
        at(f"Leg{side}_Ankle")
        ram_rod(knee + V((0, 0.08, -0.05)), ankle + V((0, 0.1, 0.15)), 0.035)
        sphere(ankle, 0.08, "gun")
        for s2 in (-1, 1):
            rod(ankle, wheel + V((s2 * 0.12, 0, 0)), 0.04, "gun")
            disc(wheel + V((s2 * 0.12, 0, 0)), 0.06, 0.04, X, "steel", segs=12)
        box((0.32, 0.5, 0.04), wheel + V((0, -0.05, 0.47)), "trim", bevel=0.01, rx=0.15)
        at(f"Leg{side}_Wheel")
        lug_wheel(wheel, 0.42, 0.16, 14, sx)
    at("Body")
    box((1.05, 0.55, 0.3), D_PELVIS, "gun", bevel=0.04)
    for sx in (-1, 1):
        box((0.1, 0.4, 0.2), D_PELVIS + V((sx * 0.5, 0, 0)), "trim", bevel=0.02)
    at("Torso")
    # the tank body: a big round tank with paint bands, a hazard band, a sight gauge and a filler cap
    sphere(V((0, 0.05, 3.35)), 0.72, "tank", scale=(1.0, 0.9, 0.85), segs=32)
    rod(V((0, 0.05, 3.33)), V((0, 0.05, 3.42)), 0.73, "paint", segs=40)
    rod(V((0, 0.05, 3.0)), V((0, 0.05, 3.06)), 0.6, "paint", segs=40)
    hazard(V((0, -0.62, 3.2)), 0.6, 0.1, 6)
    box((0.1, 0.05, 0.42), V((0.35, -0.57, 3.35)), "gun", bevel=0.01)
    box((0.06, 0.04, 0.34), V((0.35, -0.6, 3.33)), "chem", bevel=0.01)
    rod(V((0.2, 0.2, 3.9)), V((0.2, 0.2, 4.0)), 0.09, "trim")
    disc(V((0.2, 0.2, 4.01)), 0.11, 0.03, Z, "gun", segs=16)
    rod(V((0, 0.05, 2.62)), V((0, 0.05, 2.9)), 0.2, "gun", segs=16)
    # the canister rack and the mortar's mount on the back
    box((0.5, 0.12, 0.08), V((-0.33, 0.78, 3.0)), "gun", bevel=0.01)
    for k in range(3):
        disc(V((-0.45 + k * 0.12, 0.72, 3.15)), 0.06, 0.28, Z, "chem" if k == 1 else "haz_y", segs=14)
        disc(V((-0.45 + k * 0.12, 0.72, 3.3)), 0.065, 0.03, Z, "gun", segs=14)
    rod(D_MORTAR + V((0, 0, -0.3)), D_MORTAR, 0.14, "gun")
    for sx in (-1, 1):
        box((0.04, 0.24, 0.3), D_MORTAR + V((sx * 0.17, 0, 0)), "trim", bevel=0.01)
    at("Mortar")
    rod(D_MORTAR, D_MORTAR + D_AIM * 0.8, 0.13, "gun")
    rod(D_MORTAR + D_AIM * 0.75, D_MORTAR + D_AIM * 0.85, 0.16, "trim")
    rod(D_MORTAR + D_AIM * 0.3, D_MORTAR + D_AIM * 0.36, 0.145, "steel")
    disc(D_MORTAR, 0.1, 0.38, X, "steel", segs=14)
    at("Shot")
    canister(d_shot(), D_AIM)
    # the cab head out in front on a neck, its beacon on the roof
    at("Head")
    rod(V((0, -0.38, 3.58)), V((0, -0.7, 3.65)), 0.09, "gun")
    box((0.55, 0.45, 0.42), V((0, -0.75, 3.75)), "paint", bevel=0.04)
    box((0.45, 0.03, 0.2), V((0, -0.98, 3.8)), "glass", bevel=0.01)
    box((0.66, 0.55, 0.06), V((0, -0.76, 4.0)), "trim", bevel=0.02)
    for sx in (-1, 1):
        box((0.03, 0.3, 0.16), V((sx * 0.28, -0.76, 3.82)), "glass", bevel=0.005)
    eye("Head", V((0, -1.0, 3.66)), 0.08)
    at("Head")
    disc(D_BEACON, 0.06, 0.03, Z, "gun", segs=14)
    at("Beacon")
    rod(D_BEACON, D_BEACON + V((0, 0, 0.09)), 0.045, "glow", segs=14)
    box((0.02, 0.1, 0.1), D_BEACON + V((0, 0, 0.05)), "steel", bevel=0.004)
    # spray booms, folded back along the tank: a trussed bar in two lengths with a row of nozzles under each
    for side, sx in (("L", -1), ("R", 1)):
        sh = mirror(D_SH, sx)
        pts = [sh, sh + mirror(D_BOOM[0], sx), sh + mirror(D_BOOM[1], sx)]
        for i in range(2):
            a, b = pts[i], pts[i + 1]
            at(f"Boom{side}_{'Shoulder' if i == 0 else 'Elbow'}")
            if i == 0:
                sphere(sh, 0.14, "gun")
            rod(a, b, 0.05, "trim", segs=10)
            rod(a + V((0, 0, 0.12)), b + V((0, 0, 0.06)), 0.02, "steel", segs=6)
            for k in range(4):
                p = a.lerp(b, (k + 0.5) / 4)
                rod(p, p + V((0, 0, 0.11 - 0.015 * k)), 0.012, "steel", segs=6)
                rod(p, p + V((0, -0.02, -0.1)), 0.018, "steel", segs=6)
                disc(p + V((0, -0.02, -0.11)), 0.025, 0.02, Z, "haz_y", segs=8)
            sphere(b, 0.07, "gun", segs=12)
            at(f"Boom{side}_Mist{i}")
            for k in range(4):
                p = a.lerp(b, (k + 0.5) / 4) + V((0, -0.02, -0.12))
                # a curtain that billows wider and lower away from each nozzle
                for j in range(4):
                    jig = V((0.1 * math.sin(3 * k + 2 * j), 0.12 * math.cos(2 * k + j), 0))
                    sphere(p + jig + V((0, 0, -0.1 - 0.28 * j)), 0.08 + 0.07 * j, "mist", segs=10)


def canister(c, axis):
    """A fertiliser canister: a yellow drum with dark bands and a green chemical window."""
    rod(c - axis * 0.13, c + axis * 0.13, 0.08, "haz_y", segs=14)
    for t in (-0.11, 0.11):
        rod(c + axis * (t - 0.015), c + axis * (t + 0.015), 0.085, "haz_k", segs=14)
    rod(c - axis * 0.04, c + axis * 0.04, 0.082, "chem", segs=14)
    rod(c + axis * 0.13, c + axis * 0.16, 0.04, "steel", segs=10)


def duster_pivots():
    p = {"Body": (None, D_PELVIS), "Torso": ("Body", D_WAIST), "Head": ("Torso", D_NECK), "EyeLens": ("Head", D_NECK),
         "Beacon": ("Head", D_BEACON), "Mortar": ("Torso", D_MORTAR), "Shot": ("Mortar", d_shot())}
    for side, sx in (("L", -1), ("R", 1)):
        sh = mirror(D_SH, sx)
        p[f"Boom{side}_Shoulder"] = ("Torso", sh)
        p[f"Boom{side}_Elbow"] = (f"Boom{side}_Shoulder", sh + mirror(D_BOOM[0], sx))
        p[f"Boom{side}_Mist0"] = (f"Boom{side}_Shoulder", sh + mirror(D_BOOM[0], sx) * 0.5)
        p[f"Boom{side}_Mist1"] = (f"Boom{side}_Elbow", sh + mirror(D_BOOM[0].lerp(D_BOOM[1], 0.5), sx))
        p[f"Leg{side}_Hip"] = ("Body", mirror(D_HIP, sx))
        p[f"Leg{side}_Knee"] = (f"Leg{side}_Hip", mirror(D_KNEE, sx))
        p[f"Leg{side}_Ankle"] = (f"Leg{side}_Knee", mirror(D_ANKLE, sx))
        p[f"Leg{side}_Wheel"] = (f"Leg{side}_Ankle", mirror(D_WHEEL, sx))
        p[f"Leg{side}_Foot"] = (f"Leg{side}_Ankle", mirror(V((D_WHEEL.x, D_WHEEL.y, 0)), sx))
    return p


GEOMETRY = {"harvester": harvester_geometry, "tiller": tiller_geometry, "duster": duster_geometry}
PIVOTS = {"harvester": harvester_pivots, "tiller": tiller_pivots, "duster": duster_pivots}


def pivots():
    return PIVOTS[VARIANT[0]]()


def mist_nodes():
    return [n for n in pivots() if "Mist" in n]


# =====================================================================================================
# Materials
# =====================================================================================================
def materials():
    v = cfg()
    key = VARIANT[0]
    return {
        "paint": lambda: lt.textured(f"thresher_{key}_paint", sc.battle_paint(401, v["paint"])),
        "trim": lambda: lt.textured(f"thresher_{key}_trim", sc.battle_paint(409, v["trim"])),
        "gun": lambda: lt.textured("thresher_gunmetal", lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)),
        "steel": lambda: lt.textured("thresher_steel", lt.metal_surface(97, "#8d9096", 0.32, 0.9, wear=0.7, edge_light=0.2)),
        "blade": lambda: lt.textured("thresher_blade", lt.metal_surface(131, "#c8ccd2", 0.2, 1.0, wear=0.3, edge_light=0.4)),
        "rubber": lambda: lt.textured("thresher_rubber", lt.rubber_surface(101), normal_strength=2.0),
        "tread": lambda: lt.textured("thresher_tread", lt.rubber_surface(103), normal_strength=2.5),
        "tank": lambda: lt.textured("thresher_tank", lt.paint_surface(419, "#c9c7bc", "#4a4e54")),
        "haz_y": lambda: lt.textured("thresher_hazard_yellow", sc.battle_paint(131, "#e8b820")),
        "haz_k": lambda: lt.textured("thresher_hazard_black", sc.battle_paint(137, "#191a1c")),
        "bezel": lambda: lt.textured("thresher_bezel", lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)),
        "glass": glass_material,
        "chem": lambda: plain_material("thresher_chem", (0.28, 0.62, 0.08), 0.3),
        "mist": mist_material,
        "glow": glow_material,
    }


def plain_material(name, rgb, rough, alpha=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
        mat.surface_render_method = "BLENDED"
    return mat


def glass_material():
    return plain_material("thresher_glass", (0.03, 0.05, 0.06), 0.06)


def mist_material():
    """The pesticide spray: a pale poisonous green, half see-through (not glowing, so it keeps its colour when the
    eye turns red)."""
    return plain_material("thresher_mist", (0.55, 0.85, 0.2), 0.9, alpha=0.4)


def glow_material():
    """The eye and lamps: the game sets their colour (green calm, red engaged); the file holds them green."""
    mat = bpy.data.materials.new("thresher_glow")
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.05, 0.6, 0.08, 1)
    b.inputs["Roughness"].default_value = 0.15
    b.inputs["Emission Color"].default_value = (0.1, 1.0, 0.15, 1)
    b.inputs["Emission Strength"].default_value = 3.0
    return mat


def build():
    """Builds the rig; returns (root, nodes by name)."""
    sc.GEO.clear()
    GEOMETRY[VARIANT[0]]()
    if "Shot" in sc.GEO:
        # the canister is drawn to fit the mortar and grown so it reads in flight from across a base (the one in the
        # mortar stays hidden)
        c = d_shot()
        sc.GEO["Shot"].v = [tuple(c + (V(v) - c) * SHOT_SCALE) for v in sc.GEO["Shot"].v]
    makers = materials()
    order = list(makers)
    mats = {}
    root = bpy.data.objects.new("Thresher", None)
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
        if "Mist" in name:
            ob.scale = (MIST_REST,) * 3
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
        lt.box_uvs(me, [None] * len(me.polygons))
        mesh_ob = bpy.data.objects.new("EyeLensMesh" if name == "EyeLens" else f"{name}_mesh", me)
        bpy.context.scene.collection.objects.link(mesh_ob)
        mesh_ob.parent = ob
        if hasattr(me, "set_sharp_from_angle"):
            me.set_sharp_from_angle(angle=math.radians(40))
    missing = set(sc.GEO) - set(piv)
    if missing:
        raise SystemExit(f"pieces on nodes the rig lacks: {sorted(missing)}")
    bpy.context.view_layer.update()
    return root, nodes


# =====================================================================================================
# Poses. Angles in degrees. About X a positive turn tips a part back (+Y) at its bottom: a hanging limb swings back,
# the Body or Torso pitches forward. About Z (UP) a positive turn swings the front to the bot's right (+X).
# =====================================================================================================
def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


class Pose:
    def __init__(self, nodes):
        self.nodes = nodes
        self.rot = {n: Quaternion() for n in nodes}
        self.off = {n: Vector() for n in nodes}
        self.scl = {n: (MIST_REST if "Mist" in n else 1.0) for n in nodes}

    def turn(self, name, axis, deg):
        if name in self.rot:
            self.rot[name] = q_axis(axis, deg) @ self.rot[name]

    def move(self, name, v):
        if name in self.off:
            self.off[name] += Vector(v)

    def grow(self, name, k):
        """Shows a spray node at k (0 hidden in the nozzles, 1 full size)."""
        if name in self.scl:
            self.scl[name] = MIST_REST + (1 - MIST_REST) * max(0.0, k)

    def leg(self, side, swing=0.0, lift=0.0, out=0.0, flat=True):
        """swing: the foot forward (deg about the hip); lift: raise the foot by bending the knee (deg). Forward knees:
        the thigh comes forward and the shin folds back. The ankle keeps the foot level when `flat`."""
        sx = -1 if "L" in side else 1
        self.turn(f"Leg{side}_Hip", Y, -sx * out)
        hip = -swing - 0.6 * lift
        knee = 1.3 * lift
        self.turn(f"Leg{side}_Hip", X, hip)
        self.turn(f"Leg{side}_Knee", X, knee)
        if flat:
            self.turn(f"Leg{side}_Ankle", X, -(hip + knee))

    def world(self, name, piv):
        """The node's transform in model space (feet at the origin, built facing -Y)."""
        parent, pivot = piv[name]
        local = Matrix.Translation(pivot - (piv[parent][1] if parent else Vector()) + self.off[name]) @ self.rot[name].to_matrix().to_4x4()
        return (self.world(parent, piv) if parent else Matrix.Identity(4)) @ local

    def ground_feet(self):
        """Bends any knee whose foot the pose has pushed into the ground until the foot stands on it (a body that
        drops onto its legs crouches on them rather than sinking)."""
        piv = pivots()
        for side in legs():
            foot = f"Leg{side}_Foot"
            if self.world(foot, piv).translation.z > -0.005:
                continue
            saved = {n: self.rot[n].copy() for n in (f"Leg{side}_Hip", f"Leg{side}_Knee", f"Leg{side}_Ankle")}
            lo, hi = 0.0, 80.0
            for _ in range(14):
                mid = (lo + hi) / 2
                self.rot.update({n: q.copy() for n, q in saved.items()})
                self.leg(side, 0, mid)
                if self.world(foot, piv).translation.z < 0:
                    lo = mid
                else:
                    hi = mid
            self.rot.update({n: q.copy() for n, q in saved.items()})
            self.leg(side, 0, hi)

    def apply(self):
        piv = pivots()
        for n, ob in self.nodes.items():
            parent, pivot = piv[n]
            ob.rotation_quaternion = self.rot[n]
            ob.location = pivot - (piv[parent][1] if parent else Vector()) + self.off[n]
            ob.scale = (self.scl[n],) * 3


# Gaits per variant: (clip length s, swing degrees, lift, lean, bob m)
GAITS = {
    "harvester": {"patrol": (1.6, 13, 30, 2, 0.05), "chase": (1.0, 22, 48, 8, 0.1)},
    "tiller": {"patrol": (1.3, 14, 26, 1, 0.03), "chase": (0.8, 22, 40, 4, 0.06)},
    "duster": {"patrol": (1.5, 11, 22, 2, 0.06), "chase": (0.9, 17, 34, 6, 0.1)},
}
SHARED = {"idle": 3.0, "alert": 1.1, "lose_interest": 1.0, "hit": 0.45, "death": 2.4, "jump": 0.45, "fall": 0.8, "land": 0.5}
CLIPS = {}


def use(variant):
    VARIANT[0] = variant
    CLIPS.clear()
    CLIPS.update(SHARED)
    for gait_name, g in GAITS[variant].items():
        CLIPS[gait_name] = g[0]
    for clips in VARIANTS[variant]["attacks"].values():
        for clip in clips:
            CLIPS[clip] = ATTACK_CLIPS[clip]


def hip_height():
    return {"harvester": H_HIP.z, "tiller": T_Z - 0.1, "duster": D_HIP.z}[VARIANT[0]]


def gait_speed(clip):
    """Ground speed at speed scale 1: each stance sweeps the foot through 2 * swing about the hip."""
    length, swing = GAITS[VARIANT[0]][clip][:2]
    return 2 * hip_height() * math.sin(math.radians(swing)) / (length / 2)


def legs():
    return list(T_LEGS) if VARIANT[0] == "tiller" else ["L", "R"]


def phase_of(side):
    """Each leg's place in the gait: the bipeds alternate; the Tiller trots (diagonal pairs together)."""
    return {"L": 0.0, "R": 0.5, "FL": 0.0, "BR": 0.0, "FR": 0.5, "BL": 0.5}[side]


def walk(p, clip, u):
    _, swing, lift, lean, bob = GAITS[VARIANT[0]][clip]
    w = 2 * math.pi * u
    for side in legs():
        ph = w + 2 * math.pi * phase_of(side)
        p.leg(side, swing * math.sin(ph), lift * max(0.0, math.cos(ph)) ** 1.5)
    # a heavy body drops onto each footfall
    p.move("Body", (0, 0, -bob * abs(math.sin(w)) ** 0.7))
    return w, lean


def spin_wheels(p, clip, u, turns=1.0):
    """Wheel feet roll forward through the stance (the Tiller's and Duster's)."""
    for side in legs():
        p.turn(f"Leg{side}_Wheel", X, -360 * turns * u)


def smooth_shake(t, amp, hz, seed=0.0):
    return amp * (math.sin(t * hz * 2 * math.pi + seed) * 0.6 + math.sin(t * hz * 3.7 + seed * 2) * 0.4)


# ---- Harvester clips ---------------------------------------------------------------------------------
def h_arms(p, r_raise=0.0, r_out=0.0, r_elbow=0.0, r_sweep=0.0, r_wrist=0.0, l_raise=0.0, l_out=0.0, l_elbow=0.0, l_sweep=0.0):
    """r_*: the sickle arm; l_*: the sprayer arm. raise: forward and up (deg); out: away from the body; sweep:
    round the shoulder (positive swings it back); elbow: bend; wrist: roll the sickle's tip down."""
    p.turn("ArmR_Shoulder", Y, -r_out)
    p.turn("ArmR_Shoulder", X, -r_raise)
    p.turn("ArmR_Shoulder", UP, r_sweep)
    p.turn("ArmR_Elbow", X, -r_elbow)
    p.turn("ArmR_Wrist", X, r_wrist)
    p.turn("ArmL_Shoulder", Y, l_out)
    p.turn("ArmL_Shoulder", X, -l_raise)
    p.turn("ArmL_Shoulder", UP, -l_sweep)
    p.turn("ArmL_Elbow", X, -l_elbow)


def harvester_pose(p, clip, t, length):
    u = t / length
    w = 2 * math.pi * u
    if clip == "idle":
        # breathing on its haunches, the reel ticking round, the cab looking over the field
        p.move("Body", (0, 0, -0.02 * (1 - math.cos(w)) / 2))
        p.turn("Torso", X, 2 * math.sin(w))
        p.turn("Head", UP, 22 * math.sin(w))
        p.turn("Head", X, 4 * math.sin(2 * w))
        p.turn("Reel", X, -360 * u)
        h_arms(p, r_raise=3 * math.sin(w), l_raise=3 * math.sin(w + 1), r_wrist=4 * math.sin(w))
        for side, sgn in (("L", -1), ("R", 1)):
            p.leg(side, 0, 4 + 2 * sgn * math.sin(w))
    elif clip in ("patrol", "chase"):
        w, lean = walk(p, clip, u)
        chase = clip == "chase"
        p.turn("Torso", X, lean)
        p.turn("Torso", UP, (7 if chase else 4) * math.sin(w))
        p.turn("Body", Y, 3 * math.sin(w))
        p.turn("Head", X, -lean * 0.6)
        p.turn("Head", UP, -4 * math.sin(w))
        p.turn("Reel", X, -360 * (3 if chase else 1) * u)
        if chase:
            # the sickle up and back, ready to swing; the sprayer forward
            h_arms(p, r_raise=35 + 5 * math.sin(2 * w), r_out=25, r_elbow=30, r_wrist=-20,
                   l_raise=30 + 5 * math.sin(2 * w + 1), l_out=10, l_elbow=15)
        else:
            h_arms(p, r_raise=10 * math.sin(w) + 4, l_raise=-10 * math.sin(w) + 4)
    elif clip == "alert":
        # it rears back, the sickle flung up, then slams down into a crouch with the reel whirring
        rear = pulse(t, 0.0, 0.3, 0.6)
        slam = env(t, 0.45, 0.6) * (1 - 0.4 * env(t, 0.7, 1.1))
        p.move("Body", (0, 0.05 * rear, 0.08 * rear - 0.14 * slam))
        p.turn("Torso", X, -14 * rear + 10 * slam)
        p.turn("Head", X, -16 * rear + 6 * slam)
        p.turn("Reel", X, -900 * env(t, 0.1, 1.1))
        h_arms(p, r_raise=80 * rear + 35 * slam, r_out=30 * rear + 25 * slam, r_elbow=20 * rear + 30 * slam, r_wrist=-20 * slam,
               l_raise=50 * rear + 30 * slam, l_out=25 * rear + 10 * slam, l_elbow=15 * slam)
        for side in ("L", "R"):
            p.leg(side, -6 * rear, 8 * rear + 22 * slam)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 1.0)
        p.turn("Head", UP, 30 * math.sin(math.pi * u))
        p.turn("Reel", X, -200 * env(t, 0, 1.0))
        h_arms(p, r_raise=35 * k, r_out=25 * k, r_elbow=30 * k, l_raise=30 * k, l_out=10 * k)
        for side in ("L", "R"):
            p.leg(side, 0, 14 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.08, 0.45)
        p.move("Body", (0, 0.06 * k, -0.03 * k))
        p.turn("Torso", X, -10 * k)
        p.turn("Torso", UP, 8 * k)
        p.turn("Head", X, -14 * k)
        p.turn("Head", Y, 8 * k)
        h_arms(p, r_raise=-8 * k, r_out=16 * k, l_raise=-8 * k, l_out=16 * k)
        for side in ("L", "R"):
            p.leg(side, -4 * k, 10 * k)
    elif clip == "death":
        # the engine dies: knees buckle, it lurches, tips forward onto its header and the reel winds down
        buckle = env(t, 0.1, 0.6)
        lurch = pulse(t, 0.5, 0.75, 1.0)
        fall = env(t, 0.75, 1.6)
        bounce = pulse(t, 1.55, 1.7, 1.95)
        settle = env(t, 1.6, 2.3)
        p.move("Body", (0, -0.45 * fall, -0.3 * buckle - 0.55 * fall + 0.06 * bounce))
        p.turn("Body", X, 48 * fall - 6 * lurch - 3 * bounce)
        p.turn("Body", Y, 6 * lurch)
        p.turn("Torso", X, 8 * buckle - 6 * bounce)
        p.turn("Head", X, 18 * buckle + 20 * settle)
        p.turn("Head", UP, -14 * settle)
        p.turn("Reel", X, -540 * (1 - (1 - env(t, 0, 1.8)) ** 2))
        h_arms(p, r_raise=-10 * buckle + 60 * fall, r_out=55 * settle, r_elbow=-10 * fall, r_wrist=30 * settle,
               l_raise=-10 * buckle + 25 * fall, l_out=45 * settle)
        for side in ("L", "R"):
            p.leg(side, 6 * buckle - 30 * fall, 50 * buckle + 20 * fall, flat=False)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.2, 0.4)
        push = env(t, 0.2, 0.45)
        p.move("Body", (0, 0, -0.18 * crouch))
        for side in ("L", "R"):
            p.leg(side, 10 * crouch - 6 * push, 40 * crouch - 10 * push)
        h_arms(p, r_raise=-10 * crouch + 40 * push, r_out=15 * push, l_raise=-10 * crouch + 40 * push, l_out=15 * push)
    elif clip == "fall":
        for side, sgn in (("L", 1), ("R", -1)):
            p.leg(side, 10 + 8 * sgn * math.sin(w), 25 + 8 * math.sin(w + sgn))
        h_arms(p, r_raise=40 + 8 * math.sin(w), r_out=35, l_raise=40 + 8 * math.sin(w + 1), l_out=35)
        p.turn("Torso", X, -5)
        p.turn("Reel", X, -360 * u)
    elif clip == "land":
        k = pulse(t, 0.0, 0.1, 0.5)
        p.move("Body", (0, 0, -0.22 * k))
        for side in ("L", "R"):
            p.leg(side, 6 * k, 40 * k)
        h_arms(p, r_raise=25 * k, r_out=15 * k, l_raise=25 * k, l_out=15 * k)
        p.turn("Torso", X, 12 * k)
    elif clip == "track_stomp":
        # it hauls its right track up high and drives it down; the whole machine drops onto it
        up = env(t, 0.0, 0.18) * (1 - env(t, 0.18, 0.3))
        hit = pulse(t, 0.22, 0.3, 0.7)
        back = env(t, 0.6, 1.1)
        p.leg("R", 26 * up - 4 * hit, 70 * up)
        p.leg("L", -6 * up, 10 * up + 30 * hit)
        p.move("Body", (0, 0.05 * up, 0.06 * up - 0.2 * hit))
        p.turn("Body", Y, -6 * up + 4 * hit)
        p.turn("Torso", X, -10 * up + 14 * hit)
        p.turn("Head", X, 10 * hit)
        p.turn("Reel", X, -240 * env(t, 0.2, 1.0))
        h_arms(p, r_raise=40 * up - 10 * hit, r_out=30 * up + 20 * hit * (1 - back), l_raise=40 * up - 10 * hit,
               l_out=30 * up + 20 * hit * (1 - back))
    elif clip == "sickle_swipe":
        # a big wind-up with the sickle cocked back over its shoulder, then one wide flat swing across its front
        # (the blow lands as the clip starts on the host, so the swing comes quickly) and a heavy follow-through
        wind = env(t, 0.0, 0.16) * (1 - env(t, 0.16, 0.36))
        swing = env(t, 0.16, 0.36) * (1 - env(t, 0.75, 1.4))
        p.turn("Torso", UP, 28 * wind - 34 * swing)
        p.turn("Torso", X, -6 * wind + 8 * swing)
        p.turn("Head", UP, -14 * wind + 16 * swing)
        p.move("Body", (0, 0, -0.08 * swing))
        h_arms(p, r_raise=70 * wind + 70 * swing, r_out=50 * wind + 25 * swing, r_sweep=50 * wind - 85 * swing,
               r_elbow=50 * wind - 10 * swing, r_wrist=-30 * wind + 25 * swing,
               l_raise=20 * wind + 25 * swing, l_out=25 * wind + 30 * swing)
        p.leg("R", 8 * swing, 18 * wind)
        p.leg("L", -10 * swing, 10 * swing)
        p.turn("Reel", X, -540 * env(t, 0.1, 1.2))
    elif clip == "spray_sweep":
        # sprayer up, then a long sweep across the ground in front, poison billowing out of the nozzles
        aim = env(t, 0.0, 0.15) * (1 - env(t, 1.35, 1.75))
        spray = env(t, 0.1, 0.3) * (1 - env(t, 1.15, 1.45))
        sweep = math.sin(math.pi * 2 * env(t, 0.15, 1.3)) if t < 1.3 else 0.0
        p.turn("Torso", UP, -18 * sweep * aim)
        p.turn("Torso", X, 6 * aim)
        p.turn("Head", UP, -12 * sweep * aim)
        h_arms(p, l_raise=55 * aim, l_out=20 * aim, l_elbow=-10 * aim, l_sweep=30 * sweep * aim,
               r_raise=15 * aim, r_out=20 * aim, r_elbow=20 * aim)
        p.grow("Mist", spray)
        p.turn("Mist", Y, 20 * math.sin(t * 9))
        p.turn("Reel", X, -360 * env(t, 0.1, 1.7))
        for side in ("L", "R"):
            p.leg(side, 0, 10 * aim)
    else:
        raise ValueError(clip)


# ---- Tiller clips ------------------------------------------------------------------------------------
def tiller_pose(p, clip, t, length):
    u = t / length
    w = 2 * math.pi * u
    if clip == "idle":
        # idling engine: a fast shudder through the chassis, the stack cap rattling, the cab looking round
        p.move("Body", (0, 0, 0.008 * math.sin(w * 24) - 0.01 * (1 - math.cos(w)) / 2))
        p.turn("Body", Y, 0.6 * math.sin(w * 18))
        p.turn("StackCap", X, -18 * abs(math.sin(w * 12)))
        p.turn("Head", UP, 20 * math.sin(w))
        p.turn("Drum", X, -360 * u)
        p.turn("Hitch", X, 2 * math.sin(w))
        for side in legs():
            p.leg(side, 0, 3 + 1.5 * math.sin(w + phase_of(side) * 6))
    elif clip in ("patrol", "chase"):
        w, lean = walk(p, clip, u)
        chase = clip == "chase"
        p.turn("Body", X, lean * 0.3)
        p.turn("Body", Y, 2.5 * math.sin(2 * w))
        p.turn("Head", UP, 3 * math.sin(w))
        p.turn("StackCap", X, -(25 if chase else 12) * abs(math.sin(w * 4)))
        p.turn("Drum", X, -360 * (4 if chase else 1) * u)
        p.turn("Hitch", X, (-12 if chase else 0) + 3 * math.sin(2 * w))
        spin_wheels(p, clip, u, turns=1 if chase else 1)
    elif clip == "alert":
        # it revs: the nose bucks up, the drum spins up and the hitch drops ready to till
        buck = pulse(t, 0.0, 0.25, 0.55)
        ready = env(t, 0.35, 0.8)
        p.turn("Body", X, -9 * buck + 3 * ready)
        p.move("Body", (0, 0.04 * buck, 0.06 * buck - 0.08 * ready + 0.012 * math.sin(t * 60) * ready))
        for side in ("FL", "FR"):
            p.leg(side, -5 * buck, 30 * buck + 8 * ready)
        for side in ("BL", "BR"):
            p.leg(side, 0, 20 * ready)
        p.turn("Hitch", X, -14 * ready)
        p.turn("Drum", X, -1080 * env(t, 0.1, 1.1))
        p.turn("StackCap", X, -40 * abs(math.sin(t * 30)) * env(t, 0, 0.3))
        p.turn("Head", X, -8 * buck)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 1.0)
        p.turn("Head", UP, 30 * math.sin(math.pi * u))
        p.turn("Hitch", X, -14 * k)
        p.turn("Drum", X, -300 * env(t, 0, 1.0))
        p.move("Body", (0, 0, -0.08 * k))
        for side in legs():
            p.leg(side, 0, 18 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.08, 0.45)
        p.move("Body", (0, 0.08 * k, -0.04 * k))
        p.turn("Body", X, -6 * k)
        p.turn("Body", Y, 4 * k)
        p.turn("Head", X, -10 * k)
        p.turn("Hitch", X, 8 * k)
        p.turn("StackCap", X, -30 * k)
        for side in legs():
            p.leg(side, -4 * k, 10 * k)
    elif clip == "death":
        # its legs splay out from under it, the chassis crashes down onto its wheels, the drum digs in and stalls
        splay = env(t, 0.1, 0.9)
        crash = env(t, 0.55, 1.0)
        bounce = pulse(t, 0.95, 1.1, 1.4)
        settle = env(t, 1.3, 2.2)
        p.move("Body", (0, 0, -0.75 * crash + 0.08 * bounce))
        p.turn("Body", X, 8 * crash - 4 * bounce + 4 * settle)
        p.turn("Body", Y, 7 * settle)
        for side in legs():
            sx = -1 if "L" in side else 1
            p.turn(f"Leg{side}_Hip", Y, -sx * 50 * splay)
            p.turn(f"Leg{side}_Knee", Y, sx * 40 * splay)
        p.turn("Hitch", X, 22 * crash)
        p.turn("Drum", X, -720 * (1 - (1 - env(t, 0, 1.4)) ** 2))
        p.turn("StackCap", X, -60 * env(t, 0.9, 1.2))
        p.turn("Head", X, 10 * settle)
        p.turn("Head", UP, -12 * settle)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.2, 0.4)
        push = env(t, 0.2, 0.45)
        p.move("Body", (0, 0, -0.2 * crouch))
        for side in legs():
            p.leg(side, 6 * crouch, 30 * crouch - 6 * push)
        p.turn("Hitch", X, 6 * push)
    elif clip == "fall":
        for side in legs():
            p.leg(side, 6 * math.sin(w + phase_of(side) * 6), 18 + 5 * math.sin(w))
        p.turn("Hitch", X, 10)
        p.turn("Drum", X, -360 * u)
    elif clip == "land":
        k = pulse(t, 0.0, 0.1, 0.5)
        p.move("Body", (0, 0, -0.25 * k))
        for side in legs():
            p.leg(side, 0, 34 * k)
        p.turn("Hitch", X, 10 * k)
        p.turn("StackCap", X, -50 * k)
    elif clip == "rear_stomp":
        # it rocks back onto its hind wheels, lifting its nose and front legs high, then slams them down: a
        # shockwave (the effect shows it as the clip starts) and a heavy bounce
        rear = env(t, 0.0, 0.2) * (1 - env(t, 0.24, 0.36))
        slam = pulse(t, 0.3, 0.36, 0.9)
        recover = env(t, 0.9, 1.5)
        pitch = -30 * rear + 5 * slam
        # it pitches about its hind axle: the chassis rises by what the pitch drops the rear by
        p.turn("Body", X, pitch)
        p.move("Body", (0, 0.05 * rear, 0.75 * math.sin(math.radians(-pitch)) - 0.16 * slam))
        for side in ("FL", "FR"):
            p.leg(side, 30 * rear, 60 * rear + 10 * slam)
        for side in ("BL", "BR"):
            p.leg(side, pitch, 0)
        p.turn("Hitch", X, 12 * rear - 8 * slam)
        p.turn("Drum", X, -720 * env(t, 0.0, 1.5))
        p.turn("StackCap", X, -60 * slam)
        p.turn("Head", X, 10 * rear - 8 * slam)
    elif clip == "till_grind":
        # the hitch slams the drum down, it lunges onto it grinding (sparks of speed in the drum) and shakes, then lifts
        drop = env(t, 0.0, 0.14) * (1 - env(t, 0.95, 1.35))
        lunge = env(t, 0.08, 0.22) * (1 - env(t, 0.9, 1.3))
        grind = drop * lunge
        p.turn("Hitch", X, -26 * drop)
        p.turn("Drum", X, -2160 * env(t, 0.0, 1.4))
        p.turn("Body", X, 7 * lunge + smooth_shake(t, 1.5, 9) * grind)
        p.turn("Body", Y, smooth_shake(t, 2.0, 7, 1.0) * grind)
        p.move("Body", (0, -0.25 * lunge, -0.1 * lunge + 0.03 * math.sin(t * 70) * grind))
        for side in ("FL", "FR"):
            p.leg(side, 8 * lunge, 18 * lunge)
        for side in ("BL", "BR"):
            p.leg(side, -8 * lunge, 4 * lunge)
        p.turn("StackCap", X, -35 * abs(math.sin(t * 25)) * grind)
        p.turn("Head", X, 6 * lunge)
    else:
        raise ValueError(clip)


# ---- Duster clips ------------------------------------------------------------------------------------
def booms(p, out=0.0, droop=0.0, flex=0.0):
    """out: 0 folded back along the tank, 1 swung out wide; droop: tips down (deg); flex: the elbows bend back."""
    for side, sx in (("L", -1), ("R", 1)):
        p.turn(f"Boom{side}_Shoulder", Y, sx * droop)
        p.turn(f"Boom{side}_Shoulder", UP, -sx * 84 * out)
        p.turn(f"Boom{side}_Elbow", UP, sx * flex)


def duster_pose(p, clip, t, length):
    u = t / length
    w = 2 * math.pi * u
    p.turn("Beacon", UP, 360 * u * max(1, round(length / 0.7)))
    if clip == "idle":
        # swaying up on its stilts, the booms bobbing, the cab peering about
        p.turn("Body", Y, 2.5 * math.sin(w))
        p.turn("Torso", X, 2 * math.sin(w + 1))
        p.turn("Head", UP, 26 * math.sin(w))
        p.turn("Head", X, 5 * math.sin(2 * w))
        booms(p, droop=3 * math.sin(2 * w), flex=3 * math.sin(w))
        p.turn("Mortar", X, 3 * math.sin(w))
        for side, sgn in (("L", -1), ("R", 1)):
            p.leg(side, 0, 3 + 2 * sgn * math.sin(w))
    elif clip in ("patrol", "chase"):
        w, lean = walk(p, clip, u)
        chase = clip == "chase"
        p.turn("Torso", X, lean)
        p.turn("Body", Y, 4 * math.sin(w))
        p.turn("Torso", UP, 5 * math.sin(w))
        p.turn("Head", X, -lean * 0.8)
        p.turn("Head", UP, -5 * math.sin(w))
        booms(p, droop=(6 if chase else 3) * math.sin(2 * w), flex=4 * math.sin(w))
        spin_wheels(p, clip, u)
    elif clip == "alert":
        # booms snap out wide, the beacon whirls, it drops into a crouch and the mortar swings forward
        snap = env(t, 0.05, 0.35)
        crouch = pulse(t, 0.0, 0.2, 0.6)
        p.move("Body", (0, 0, -0.2 * crouch))
        p.turn("Torso", X, -10 * crouch)
        p.turn("Head", X, -12 * crouch)
        booms(p, out=snap * (1 - env(t, 0.7, 1.1)), droop=-8 * pulse(t, 0.25, 0.35, 0.6), flex=-12 * pulse(t, 0.25, 0.35, 0.7))
        p.turn("Mortar", X, 25 * snap * (1 - env(t, 0.7, 1.1)))
        for side in ("L", "R"):
            p.leg(side, 0, 22 * crouch)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 1.0)
        p.turn("Head", UP, 30 * math.sin(math.pi * u))
        booms(p, droop=5 * k)
        for side in ("L", "R"):
            p.leg(side, 0, 10 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.08, 0.45)
        p.move("Body", (0, 0.08 * k, -0.04 * k))
        p.turn("Torso", X, -10 * k)
        p.turn("Body", Y, 6 * k)
        p.turn("Head", X, -16 * k)
        booms(p, droop=-10 * k, flex=14 * k)
        for side in ("L", "R"):
            p.leg(side, -4 * k, 12 * k)
    elif clip == "death":
        # the left stilt folds, the tank lurches and topples over sideways, booms flailing, onto the ground
        fold = env(t, 0.1, 0.7)
        topple = env(t, 0.5, 1.5) ** 1.6
        bounce = pulse(t, 1.45, 1.6, 1.9)
        settle = env(t, 1.6, 2.3)
        p.turn("Body", Y, -78 * topple + 4 * bounce)
        p.turn("Body", X, 10 * fold)
        p.move("Body", (-1.3 * topple, 0, -0.35 * fold - 1.55 * topple + 0.08 * bounce))
        p.leg("L", 10 * fold, 70 * fold, flat=False)
        p.leg("R", -10 * fold, 20 * fold + 30 * topple, out=20 * topple, flat=False)
        booms(p, out=0.5 * pulse(t, 0.4, 1.0, 1.8) + 0.25 * settle, droop=20 * topple, flex=30 * settle)
        p.turn("Head", X, 25 * settle)
        p.turn("Head", UP, 15 * settle)
        p.turn("Mortar", X, -30 * topple)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.2, 0.4)
        push = env(t, 0.2, 0.45)
        p.move("Body", (0, 0, -0.3 * crouch))
        for side in ("L", "R"):
            p.leg(side, 8 * crouch, 30 * crouch - 6 * push)
        booms(p, droop=-10 * push)
    elif clip == "fall":
        for side, sgn in (("L", 1), ("R", -1)):
            p.leg(side, 8 * sgn * math.sin(w), 16 + 6 * math.sin(w + sgn))
        booms(p, out=0.35, droop=-10 + 6 * math.sin(2 * w))
    elif clip == "land":
        k = pulse(t, 0.0, 0.12, 0.5)
        p.move("Body", (0, 0, -0.3 * k))
        for side in ("L", "R"):
            p.leg(side, 0, 32 * k)
        booms(p, droop=12 * k, flex=8 * k)
    elif clip == "boom_spray":
        # booms swing out wide and drop low, the poison cloud pours out of every nozzle as it turns to sweep the
        # ground, then the booms fold away again
        out = env(t, 0.0, 0.25) * (1 - env(t, 1.55, 1.95))
        spray = env(t, 0.15, 0.4) * (1 - env(t, 1.35, 1.7))
        sweep = math.sin(2 * math.pi * env(t, 0.3, 1.5)) if t < 1.5 else 0.0
        p.turn("Body", UP, 16 * sweep * out)
        p.move("Body", (0, 0, -0.25 * out))
        booms(p, out=out, droop=8 * out + 3 * math.sin(t * 11) * spray, flex=-5 * out)
        for side in ("L", "R"):
            p.leg(side, 0, 18 * out)
            for i in (0, 1):
                n = f"Boom{side}_Mist{i}"
                p.grow(n, spray * (0.85 + 0.15 * math.sin(t * 13 + i)))
                p.turn(n, X, 8 * math.sin(t * 7 + i))
        p.turn("Head", X, 10 * out)
        p.turn("Beacon", UP, 720 * u)
    elif clip == "mortar_shot":
        # it crouches, the mortar tips forward to aim, then a hard thump: the tube kicks down into its mount and
        # the stilts take the recoil
        aim = env(t, 0.0, 0.12) * (1 - env(t, 0.6, 1.0))
        kick = pulse(t, 0.15, 0.19, 0.45)
        p.turn("Mortar", X, 38 * aim - 10 * kick)
        p.move("Mortar", (0, 0.05 * kick, -0.12 * kick))
        p.move("Body", (0, 0.05 * kick, -0.12 * aim - 0.22 * kick))
        p.turn("Torso", X, 6 * aim - 8 * kick)
        p.turn("Head", X, 8 * aim - 6 * kick)
        booms(p, droop=-6 * kick, flex=10 * kick)
        for side in ("L", "R"):
            p.leg(side, 0, 12 * aim + 18 * kick)
    else:
        raise ValueError(clip)


POSES = {"harvester": harvester_pose, "tiller": tiller_pose, "duster": duster_pose}


def set_pose(nodes, clip, t, length):
    p = Pose(nodes)
    POSES[VARIANT[0]](p, clip, t, length)
    p.ground_feet()
    # A hair of motion (a quarter degree) on every node, so no channel holds still through a clip.
    for i, n in enumerate(nodes):
        p.turn(n, X, 0.25 * math.sin(2 * math.pi * t / length) * (1 if i % 2 else -1))
    # and on the spray's scale, which would otherwise hold at rest and come back at full size (Godot drops a held
    # track and blends the node to scale 1)
    for n in p.scl:
        if "Mist" in n:
            p.scl[n] *= 1 + 0.05 * math.sin(2 * math.pi * t / length)
    p.apply()


def bake_clips(nodes):
    """Keys every rig node every frame of every clip (see build_scuttler.bake_clips). Spray nodes also key their
    scale."""
    anim = [ob for n, ob in nodes.items() if not n.endswith("_Foot") and n not in ("EyeLens", "Shot")]
    for ob in anim:
        ob.animation_data_create()
    rest = Pose(nodes)
    rest.apply()
    rest_tf = {ob.name: (tuple(ob.location), tuple(ob.rotation_quaternion)) for ob in anim}
    held = []
    for clip, length in CLIPS.items():
        acts = {}
        for ob in anim:
            acts[ob] = bpy.data.actions.new(f"{clip}_{ob.name}")
            ob.animation_data.action = acts[ob]
        frames = int(round(length * FPS))
        seen = {ob.name: [] for ob in anim}
        for f in range(frames + 1):
            set_pose(nodes, clip, f / FPS, length)
            for ob in anim:
                ob.keyframe_insert("rotation_quaternion", frame=f)
                ob.keyframe_insert("location", frame=f)
                if "Mist" in ob.name:
                    ob.keyframe_insert("scale", frame=f)
                seen[ob.name].append((tuple(ob.location), tuple(ob.rotation_quaternion)))
        for ob, act in acts.items():
            lt.linear_keys(act)
            sc.push_track(ob, act, clip)
        for name, values in seen.items():
            for k in (0, 1):
                vals = [v[k] for v in values]
                if all(np.allclose(v, vals[0], atol=1e-6) for v in vals) and not np.allclose(vals[0], rest_tf[name][k], atol=1e-4):
                    held.append(f"{clip}: {name} holds {'location' if k == 0 else 'rotation'} away from rest")
    if held:
        raise SystemExit("\n".join(held))
    rest.apply()


def build_game(variants):
    for variant in variants:
        use(variant)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        root, nodes = build()
        bake_clips(nodes)
        out = MESHES / f"{VARIANTS[variant]['id']}.glb"
        sc.export(root, out)
        speeds = ", ".join(f"{clip} {gait_speed(clip):.2f}" for clip in GAITS[variant])
        print(f"wrote {out.relative_to(REPO)} (clip speeds, m/s: {speeds})")


# =====================================================================================================
# Review renders
# =====================================================================================================
REVIEW = {
    "harvester": [("alert", 0.3), ("sickle_swipe", 0.16), ("sickle_swipe", 0.36), ("spray_sweep", 0.7), ("track_stomp", 0.18)],
    "tiller": [("alert", 0.6), ("rear_stomp", 0.22), ("rear_stomp", 0.36), ("till_grind", 0.5), ("chase", 0.2)],
    "duster": [("alert", 0.4), ("boom_spray", 0.5), ("boom_spray", 1.0), ("mortar_shot", 0.12), ("mortar_shot", 0.19)],
}


def render_review(out: Path, variant, only=None):
    use(variant)
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    bpy.context.scene.cycles.samples = SAMPLES
    root, nodes = build()
    root.rotation_euler = (0, 0, 0)          # renders keep the build's -Y front
    if "Shot" in nodes:
        for ob in nodes["Shot"].children:    # the game hides the shot until it flies
            ob.hide_render = True
    c = (0, -0.2, 1.9)
    shots = []

    def take(name, clip, t, cam):
        if only and name not in only:
            return
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t, CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cam, out / f"{variant}_{name}.png", 800, 900)
        shots.append((name, path))

    tq = rl.camera("tq", (-6.5, -8.5, 4.6), c, ortho=5.6)
    side = rl.camera("side", (11, 0, 2.0), c, ortho=5.6)
    take("rest", None, 0, tq)
    take("front", None, 0, rl.camera("front", (0, -11, 2.0), c, ortho=5.4))
    take("side", None, 0, side)
    take("rear", None, 0, rl.camera("rear", (7, 8, 4.2), c, ortho=5.6))
    take("patrol", "patrol", CLIPS["patrol"] * 0.25, side)
    take("chase", "chase", CLIPS["chase"] * 0.25, tq)
    for clip, t in REVIEW[variant]:
        take(f"{clip}_{t:g}", clip, t, tq)
    take("hit", "hit", 0.08, tq)
    take("land", "land", 0.1, tq)
    take("death_mid", "death", 1.0, side)
    take("death", "death", CLIPS["death"], tq)
    if only:
        return

    from render_lift import INK, font as F  # noqa: PLC0415
    w, h = 400, 450
    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (60 + cols * (w + 16) + 40, 150 + rows * (h + 16) + 20), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), f"{VARIANTS[variant]['name'].upper()} THRESHER (game model and clips)", fill=INK, font=F(40, True))
    d.text((60, 86), "Final game model with worn textures, in its rest pose and at key frames of its clips. "
           "The eye shows green; the game turns it red while engaged.", fill=INK, font=F(20))
    for i, (name, path) in enumerate(shots):
        x, y = 60 + (i % cols) * (w + 16), 130 + (i // cols) * (h + 16)
        sheet.paste(Image.open(path).convert("RGB").resize((w, h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(190, 190, 190), width=2)
        d.text((x + 10, y + 8), name.replace("_", " ").upper(), fill=INK, font=F(16, True))
    sheet.save(out / f"{variant}_review_sheet.png")
    print(f"rendered the review sheet into {out}")


if __name__ == "__main__":
    args = sys.argv[1:]
    chosen = [args[i + 1] for i, a in enumerate(args) if a == "--variant"] or list(VARIANTS)
    only = [args[i + 1] for i, a in enumerate(args) if a == "--only"]
    if "--render" in args:
        for key in chosen:
            render_review(Path(args[args.index("--render") + 1]), key, only)
    else:
        build_game(chosen)
