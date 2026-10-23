"""Build the four Spoolers (Chase approved the fourth concept pass on 2026-10-20).

Spoolers are 1 m ranged bots that shoot tape. They share one chassis:

* a boxy head with a camera-lens eye under a hood, a ranging lamp, a grille mouth, a screw-cap ear on the right and
  a reel of tape on an axle off the left side (in a guard hoop), an antenna with a tape pennant;
* a chest with a tape-left gauge, an A-frame rack on the back that carries a roll;
* reverse-knee legs standing on reel feet (a small spool with a rubber tread, a toe plate and a heel spur);
* a tape gun on the right arm (carry handle, tape pad, a roller at the muzzle) and a rotary cutter on the left.

Each colour adds one piece: Blue (``spooler_blue``) blue-and-white chevron printed tape; Green (``spooler_green``)
bubble-wrap bundles, shipping stickers and a bubble-wrap roller; Yellow (``spooler_yellow``) a stack of electrical
tape rolls, a rack of coloured spares and a twin-roller gun (it fires fast); Red (``spooler_red``) yellow-and-black
hazard tape, a long ribbed barrel with a scope and an armour plate (long range).

The paint is the Scuttlers' battle wear: chipped through to rust and bare steel, scratched and grimy.

What it writes: ``mods/vanilla/meshes/bots/spooler_<colour>.glb``. Feet are at the origin and it faces -Z, as bots
do. The nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the pelvis), ``Torso``, ``Head``, ``EyeLens`` (the glowing eye, which the game colours green while calm
  and red while engaged), ``HeadReel`` (the side reel), ``Antenna`` and ``BackRoll``;
* ``ArmR_Shoulder`` / ``ArmR_Gun`` and ``Roller`` (the gun's muzzle roller; not on Red);
* ``Shot`` (on the gun, past the muzzle: the fired tape or bubble wrap, which the game hides and flies to what the
  ``shot`` effect hit);
* ``ArmL_Shoulder`` / ``ArmL_Elbow`` and ``Cutter`` (the rotary blade);
* ``Leg<L|R>_Hip`` / ``_Knee`` / ``_Ankle`` / ``_Foot`` (``_Foot`` is an empty under the reel foot).

Clips (seconds; the loops loop): ``idle`` 2.4, ``patrol`` and ``chase`` (gaits; their ground speeds at speed scale
1 are printed and go in the bot's ``model.clip_speeds``), ``alert`` 0.8, ``lose_interest`` 0.7, ``hit`` 0.35,
``death`` 1.6, ``jump`` 0.35, ``fall`` 0.6, ``land`` 0.35, and the shot: Blue ``tape_shot`` 0.7, Green
``bubble_shot`` 0.7, Yellow ``rapid_shot`` 0.5 (two quick shots), Red ``heavy_shot`` 1.0. The shot leaves the gun
about 0.15 s in: the host deals the damage as an attack starts.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/spooler/build_spooler.py [--variant blue ...]                # writes the .glb files
    python tools/spooler/build_spooler.py --render OUT_DIR [--variant ...]    # review renders and sheets

The output is committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up and the model is built facing -Y, then turned to face -Z in glTF. The build is
deterministic. Geometry helpers, paint, clip baking and export are the Scuttler build's (tools/scuttler); the rig and
pose helpers follow the Rakehand build's (tools/rakehand).
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
from build_scuttler import at, box, disc, env, pulse, retimed, rod, sphere  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "bots"
FPS = sc.FPS
SAMPLES = int(os.environ.get("SPOOLER_SAMPLES", "48"))
UP = Vector((0, 0, 1))
X = Vector((1, 0, 0))
Y = Vector((0, 1, 0))
Z = Vector((0, 0, 1))
V = Vector

# tape: "chevron" or "diagonal" printed in two colours, or "bands" (a stack of plain electrical-tape rolls)
VARIANTS = {
    "blue": dict(id="spooler_blue", name="Blue Spooler", paint="#3e7fd0", accent="#e0a020",
                 tape=("chevron", "#f2f4f6", "#1d3f9a"), attacks={"tape": ["tape_shot"]}),
    "green": dict(id="spooler_green", name="Green Spooler", paint="#5fae3e", accent="#c06a2a",
                  tape=("bubble", "#8fd0f0", None), attacks={"bubble": ["bubble_shot"]}),
    "yellow": dict(id="spooler_yellow", name="Yellow Spooler", paint="#e0c030", accent="#3a3f46",
                   tape=("bands", "#d8262a", "#1f1f22", "#2f7fe0", "#f2f2ee"), attacks={"tape": ["rapid_shot"]}),
    "red": dict(id="spooler_red", name="Red Spooler", paint="#d0402e", accent="#9aa0a8",
                tape=("diagonal", "#f0c020", "#141416"), attacks={"tape": ["heavy_shot"]}),
}
ATTACK_CLIPS = {"tape_shot": 0.9, "bubble_shot": 0.9, "rapid_shot": 0.62, "heavy_shot": 1.32}
# The aim is held longer so the shot (it hits as it fires) leaves at the attack's windup (bots/*.json):
# {clip: (shot as written, shot, length as written)}.
TELEGRAPH = {"tape_shot": (0.15, 0.35, 0.7), "bubble_shot": (0.15, 0.35, 0.7), "rapid_shot": (0.12, 0.24, 0.5),
             "heavy_shot": (0.18, 0.5, 1.0)}
VARIANT = ["blue"]
SIDES = (("L", -1), ("R", 1))

# The skeleton (model space, front is -Y). Right side; the left mirrors the legs.
HIP = V((0.1, 0.0, 0.5))
KNEE = V((0.1, 0.09, 0.32))
ANKLE = V((0.1, -0.02, 0.13))
FOOT_C = V((0.1, -0.02, 0.06))          # the reel foot's axle
SOLE = V((0.1, -0.02, 0.0))
WAIST = V((0, 0.0, 0.57))
NECK = V((0, -0.01, 0.82))
SH_R, EL_R = V((0.18, 0.0, 0.72)), V((0.24, 0.0, 0.64))
SH_L, EL_L = V((-0.18, 0.0, 0.72)), V((-0.22, -0.06, 0.61))
GUN_PITCH = 0.3                          # the gun's rest pitch, down-forward (rad)
BACK = V((0, 0.17, 0.7))
HEAD_SCALE = 1.3
HEAD_C = V((0, -0.02, 0.93))             # the head box's centre before it is grown about the neck


def cfg():
    return VARIANTS[VARIANT[0]]


def mirror(v, sx):
    return V((v.x * sx, v.y, v.z))


def rx(a, v):
    return Matrix.Rotation(a, 3, "X") @ V(v)


def hs(v):
    """A head point: the head is drawn at concept size and grown about the neck (the approved head is big)."""
    pivot = V((0, -0.01, 0.84))
    return pivot + (V(v) - pivot) * HEAD_SCALE


def gun_point(v):
    """A point on the gun, given in the gun's frame (muzzle along -Y), at its rest pitch."""
    return EL_R + rx(GUN_PITCH, v)


def gun_muzzle():
    return gun_point((0, -0.28 - (0.16 if VARIANT[0] == "red" else 0.0), -0.08))


SHOT_SCALE = 1.7


def shot_point():
    """Where the fired tape (the hidden ``Shot`` node) sits on the gun, just past the muzzle."""
    red = VARIANT[0] == "red"
    return gun_point((0, -0.34 - (0.16 if red else 0.0), -0.02 if red else -0.08))


def cutter_hub():
    return EL_L + rx(0.5, (0, -0.17, 0)) + V((-0.03, 0, 0))


# =====================================================================================================
# Geometry
# =====================================================================================================
ROLLS = []          # (node, centre, radius, width, face material, side material): cylindrical UVs for tape


def strip(pts, r, mat):
    """An open run of rod segments with ball joints."""
    for i in range(len(pts) - 1):
        rod(pts[i], pts[i + 1], r, mat, segs=8)
        if i:
            sphere(pts[i], r, mat, segs=8)


def screw_cap(c, axis, r):
    a = V(axis)
    disc(c, r, 0.03, a, "steel", segs=24)
    box((0.004, r * 1.5, r * 0.25), c + a * 0.016, "bezel", bevel=0, rot=Matrix.Rotation(0.6, 4, "X"))


def hex_nut(c, axis, r, mat="accent"):
    disc(c, r, 0.025, axis, mat, segs=6)
    disc(c + V(axis) * 0.014, r * 0.4, 0.012, axis, "steel", segs=16)


def tape_roll(node, c, r, width, bands=None, core=0.45, loose_end=True):
    """A roll of tape on a cardboard core (axis X): a printed face and wound-layer sides, both UV-mapped round the
    roll (see ROLLS), and a loose end peeling off the back. `bands` draws it as narrow rolls of those materials."""
    c = V(c)
    bands = bands or ["tape"]
    n = len(bands)
    bw = width / n
    for k, mat in enumerate(bands):
        bc = c + X * (-width / 2 + bw * (k + 0.5))
        rk = r * (1 - 0.04 * (k % 2))
        at(node)
        disc(bc, rk, bw * 0.96, X, mat, segs=40)
        ROLLS.append((node, bc, rk, bw * 0.96, mat))
        for s in (-1, 1):
            disc(bc + X * s * bw * 0.48, rk * 0.99, 0.002, X, mat + "side", segs=40)
    at(node)
    disc(c, r * core, width + 0.008, X, "core", segs=24)
    disc(c, r * core * 0.8, width + 0.012, X, "bezel", segs=24)
    if loose_end:
        w = min(bw * 0.9, 0.09)
        e0 = c + V((width / 2 - bw / 2 if n > 1 else 0, 0.0, -r))
        for i, (dy, dz, ang) in enumerate(((0.025, -0.012, -0.5), (0.045, -0.04, -1.1), (0.05, -0.075, -1.6))):
            box((w, 0.032, 0.003), e0 + V((0, dy, dz)), bands[-1], bevel=0, rot=Matrix.Rotation(ang, 4, "X"))


def bubble_bundle(node, c, r, width):
    at(node)
    disc(c, r, width, X, "bubble", segs=32)
    for i in range(5):
        for j in range(10):
            ang = 2 * math.pi * (j + 0.5 * (i % 2)) / 10
            sphere(V(c) + V((-width / 2 + width * (i + 0.5) / 5, r * math.cos(ang), r * math.sin(ang))), 0.013, "bubble", segs=8)


def tape_mats():
    t = cfg()["tape"]
    if t[0] == "bands":
        return [f"band{k}" for k in range(len(t) - 1)]
    return None


def legs():
    for side, sx in SIDES:
        hip, knee, ankle, fc = (mirror(p, sx) for p in (HIP, KNEE, ANKLE, FOOT_C))
        at(f"Leg{side}_Hip")
        screw_cap(hip + V((sx * 0.05, 0, 0)), X, 0.035)
        rod(hip, knee, 0.026, "gun")
        d = knee - hip
        box((0.07, 0.075, 0.13), hip.lerp(knee, 0.5), "paint", bevel=0.012,
            rot=Matrix.Rotation(-math.atan2(d.y, -d.z), 4, "X"))
        at(f"Leg{side}_Knee")
        disc(knee, 0.042, 0.08, X, "paint", segs=24)
        screw_cap(knee + V((sx * 0.042, 0, 0)), X, 0.026)
        rod(knee, ankle, 0.022, "steel")
        rod(knee.lerp(ankle, 0.15) + V((0, 0.02, 0)), knee.lerp(ankle, 0.8) + V((0, 0.02, 0)), 0.01, "gun")
        for k in range(2):
            disc(ankle.lerp(knee, 0.18 + k * 0.1), 0.03, 0.014, (knee - ankle).normalized(), "rubber", segs=20)
        at(f"Leg{side}_Ankle")
        # the reel foot: a small spool on its side, a rubber tread between accent flanges, toe plate, heel spur
        rod(ankle, fc, 0.016, "gun")
        disc(fc, 0.05, 0.06, X, "rubber", segs=32)
        for k in range(10):
            a = 2 * math.pi * k / 10
            box((0.058, 0.012, 0.008), fc + V((0, 0.051 * math.cos(a), 0.051 * math.sin(a))), "rubber", bevel=0,
                rot=Matrix.Rotation(a + math.pi / 2, 4, "X"))
        for s2 in (-1, 1):
            disc(fc + V((s2 * 0.035, 0, 0)), 0.06, 0.012, X, "accent", segs=32)
        disc(fc + V((sx * 0.044, 0, 0)), 0.018, 0.012, X, "steel", segs=6)
        box((0.075, 0.06, 0.02), fc + V((0, -0.065, -0.045)), "steel", bevel=0.006)
        rod(fc + V((0, 0.04, -0.01)), fc + V((0, 0.09, -0.055)), 0.012, "steel", r2=0.005)


def torso(back_roll=True):
    at("Body")
    box((0.2, 0.12, 0.06), V((0, 0.0, 0.53)), "gun", bevel=0.01)
    at("Torso")
    box((0.1, 0.08, 0.06), V((0, 0.0, 0.575)), "gun", bevel=0.008)
    box((0.28, 0.21, 0.18), V((0, 0.0, 0.68)), "paint", bevel=0.02)
    box((0.18, 0.012, 0.07), V((0, -0.103, 0.65)), "accent", bevel=0.006)
    for sx in (-1, 1):
        disc(V((sx * 0.1, -0.103, 0.735)), 0.01, 0.006, Y, "steel", segs=12)
    # a tape gauge on the chest: how much tape is left
    disc(V((0.0, -0.11, 0.72)), 0.034, 0.014, Y, "steel", segs=28)
    disc(V((0.0, -0.118, 0.72)), 0.028, 0.004, Y, "white", segs=28)
    for k, mat in enumerate(("lamp", "accent", "glowgreen")):
        box((0.016, 0.002, 0.005), V((-0.016 + k * 0.016, -0.121, 0.733 - abs(k - 1) * 0.004)), mat, bevel=0)
    box((0.003, 0.002, 0.024), V((0.006, -0.122, 0.728)), "bezel", bevel=0)
    # back rack: an A-frame of bars that carries the back roll
    for sx in (-1, 1):
        rod(V((sx * 0.12, 0.1, 0.6)), V((sx * 0.05, 0.13, 0.82)), 0.011, "gun")
    rod(V((-0.05, 0.13, 0.82)), V((0.05, 0.13, 0.82)), 0.011, "gun")
    rod(V((-0.12, 0.1, 0.6)), V((0.12, 0.1, 0.6)), 0.011, "gun")
    if back_roll:
        rod(BACK - X * 0.08, BACK + X * 0.08, 0.012, "steel")
        tape_roll("BackRoll", BACK, 0.085, 0.1, bands=tape_mats() and tape_mats()[1:3])
    at("Torso")
    rod(V((0, 0.0, 0.76)), V((0, -0.01, 0.84)), 0.022, "gun")
    sphere(V((0, -0.01, 0.8)), 0.03, "gun")


def head(roll=True):
    """The head, drawn at concept size about HEAD_C; build() grows the Head, EyeLens, HeadReel and Antenna pieces."""
    c = HEAD_C
    at("Head")
    box((0.2, 0.18, 0.17), c, "paint", bevel=0.015)
    # camera-lens eye: a stepped barrel out of the face, a hood over it
    e = c + V((0.02, -0.09, 0.015))
    disc(e, 0.058, 0.016, Y, "gun", segs=40)
    disc(e + V((0, -0.022, 0)), 0.048, 0.03, Y, "steel", segs=40)
    disc(e + V((0, -0.026, 0)), 0.051, 0.008, Y, "rubber", segs=40)
    disc(e + V((0, -0.04, 0)), 0.042, 0.008, Y, "gun", segs=40)
    box((0.12, 0.05, 0.012), e + V((0, -0.03, 0.058)), "accent", bevel=0.003, rot=Matrix.Rotation(0.2, 4, "X"))
    disc(c + V((-0.065, -0.092, 0.05)), 0.012, 0.01, Y, "lamp", segs=16)
    # mouth: a grille of slats in a dark recess
    box((0.13, 0.008, 0.035), c + V((0, -0.091, -0.06)), "bezel", bevel=0.003)
    for k in range(6):
        box((0.008, 0.012, 0.03), c + V((-0.05 + k * 0.02, -0.093, -0.06)), "steel", bevel=0.001)
    screw_cap(c + V((0.112, 0.0, 0.0)), X, 0.045)
    rc = c + V((-0.155, 0.01, 0.02))
    rod(c + V((-0.1, 0.01, 0.02)), rc + V((-0.06, 0, 0)), 0.012, "steel")
    strip([c + V((-0.1, -0.06, 0.07)), rc + V((-0.02, -0.07, 0.07)), rc + V((-0.03, 0.0, 0.1)),
           rc + V((-0.02, 0.07, 0.07)), c + V((-0.1, 0.07, 0.06))], 0.008, "gun")
    at("EyeLens")
    sphere(e + V((0, -0.03, 0)), 0.034, "glow", scale=(1, 0.5, 1), segs=24)
    at("Antenna")
    rod(c + V((0.06, 0.04, 0.085)), c + V((0.06, 0.04, 0.2)), 0.006, "steel")
    box((0.003, 0.05, 0.025), c + V((0.06, 0.066, 0.185)), "pennant", bevel=0)
    if roll:
        at("HeadReel")
        disc(rc + V((-0.055, 0, 0)), 0.02, 0.012, X, "accent", segs=6)
        tape_roll("HeadReel", rc, 0.07, 0.09, bands=tape_mats())
    return rc


def tape_gun(barrel=0.0, scope=False, twin=False, roller=True):
    """Right arm: shoulder, upper arm, then the gun (carry handle, tape pad, the muzzle roller or a barrel)."""
    at("ArmR_Shoulder")
    disc(SH_R, 0.06, 0.06, X, "paint", segs=32)
    screw_cap(SH_R + V((0.035, 0, 0)), X, 0.04)
    box((0.06, 0.06, 0.12), SH_R.lerp(EL_R, 0.5) + V((0.01, 0, 0)), "gun", bevel=0.008)
    sphere(EL_R, 0.035, "steel")
    at("ArmR_Gun")
    P = gun_point
    rot = Matrix.Rotation(GUN_PITCH, 4, "X")
    w = 0.11 if twin else 0.08
    box((w, 0.26, 0.1), P((0, -0.1, 0)), "accent", bevel=0.012, rot=rot)
    box((w + 0.01, 0.12, 0.05), P((0, -0.06, -0.06)), "steel", bevel=0.008, rot=rot)
    box((w - 0.01, 0.12, 0.025), P((0, -0.14, 0.062)), "pad", bevel=0.004, rot=rot)
    rod(P((0, -0.02, 0.05)), P((0, -0.02, 0.11)), 0.01, "steel")
    rod(P((0, -0.02, 0.11)), P((0, 0.1, 0.11)), 0.014, "rubber")
    for sx in (-1, 1):
        box((0.012, 0.1, 0.03), P((sx * (w / 2 + 0.006), -0.24, -0.05)), "steel", bevel=0.003,
            rot=Matrix.Rotation(GUN_PITCH + 0.5, 4, "X"))
    if barrel:
        rod(P((0, -0.22, 0.0)), P((0, -0.24 - barrel, -0.02)), 0.032, "gun", segs=20)
        for k in range(3):
            rod(P((0, -0.26 - k * barrel / 3, -0.005)), P((0, -0.275 - k * barrel / 3, -0.005)), 0.038, "steel", segs=20)
        box((0.09, 0.03, 0.06), P((0, -0.25 - barrel, -0.02)), "accent", bevel=0.008, rot=rot)
        box((0.06, 0.004, 0.01), P((0, -0.267 - barrel, -0.02)), "bezel", bevel=0, rot=rot)
    if scope:
        rod(P((0.0, 0.0, 0.14)), P((0.0, -0.16, 0.14)), 0.018, "gun")
        disc(P((0.0, -0.162, 0.14)), 0.016, 0.004, rx(GUN_PITCH, (0, -1, 0)), "lamp", segs=16)
        rod(P((0, -0.08, 0.11)), P((0, -0.08, 0.14)), 0.008, "steel")
    if roller and not barrel:
        at("Roller")
        rc = gun_muzzle()
        for k, dx in enumerate((-0.03, 0.03) if twin else (0.0,)):
            disc(rc + V((dx, 0, 0)), 0.036, 0.05 if twin else 0.085, X, "roller", segs=24)
            for j in range(8):
                a = 2 * math.pi * j / 8
                box((0.05 if twin else 0.08, 0.008, 0.008), rc + V((dx, 0.037 * math.cos(a), 0.037 * math.sin(a))), "roller",
                    bevel=0, rot=Matrix.Rotation(a, 4, "X"))


def shot_piece():
    """The fired shot, on its own node (``Shot``; the game hides it and flies a copy of it to what the attack hit):
    a balled-up wad of the variant's tape trailing a crinkled ribbon, or a ball of bubble wrap. Its front is -Y."""
    at("Shot")
    c = shot_point()
    if VARIANT[0] == "green":
        sphere(c, 0.042, "bubble", segs=16)
        n = 18
        for i in range(n):
            z = 1 - 2 * (i + 0.5) / n
            a = math.pi * (3 - math.sqrt(5)) * i
            rr = math.sqrt(1 - z * z)
            sphere(c + V((rr * math.cos(a), rr * math.sin(a), z)) * 0.043, 0.01, "bubble", segs=8)
        for i, (dy, dz, ang) in enumerate(((0.05, 0.004, 0.3), (0.085, -0.006, -0.3))):
            box((0.05, 0.04, 0.003), c + V((0, dy, dz)), "bubble", bevel=0, rot=Matrix.Rotation(ang, 4, "X"))
        return
    red = VARIANT[0] == "red"
    mats = tape_mats() or ["tape"]
    r = 0.042 if red else 0.032
    w = 0.06 if red else 0.048
    sphere(c, r, mats[0], scale=(1.0, 1.15, 0.85), segs=12)
    for k in range(3):     # creases on the wad
        a = 2 * math.pi * k / 3
        box((w * 0.9, 0.004, r * 0.9), c + V((0, r * 0.2 * math.cos(a), r * 0.6 * math.sin(a))), mats[k % len(mats)],
            bevel=0, rot=Matrix.Rotation(a, 4, "Y"))
    for i in range(4):     # the ribbon flapping out behind
        p = c + V((0.004 * (i % 2), r * 0.9 + 0.042 * i + 0.02, 0.012 * math.sin(i * 1.7)))
        box((w, 0.046, 0.003), p, mats[min(i, len(mats) - 1)], bevel=0,
            rot=Matrix.Rotation(0.35 * (1 if i % 2 else -1), 4, "X") @ Matrix.Rotation(0.06 * i, 4, "Y"))


def cutter():
    """Left arm: a rotary cutter, a toothed circular blade in a fork with a half guard."""
    at("ArmL_Shoulder")
    disc(SH_L, 0.06, 0.06, X, "paint", segs=32)
    screw_cap(SH_L + V((-0.035, 0, 0)), (-1, 0, 0), 0.04)
    box((0.06, 0.06, 0.11), SH_L.lerp(EL_L, 0.5) + V((-0.02, 0, 0)), "gun", bevel=0.008, rot=Matrix.Rotation(0.5, 4, "X"))
    at("ArmL_Elbow")
    hub = cutter_hub()
    box((0.06, 0.12, 0.06), EL_L.lerp(hub, 0.4) + V((-0.01, 0, 0)), "paint", bevel=0.012, rot=Matrix.Rotation(0.5, 4, "X"))
    for s2 in (-1, 1):
        rod(EL_L.lerp(hub, 0.6) + V((-0.03 + s2 * 0.022, 0, 0)), hub + V((s2 * 0.022, 0, 0)), 0.009, "steel")
    pts = [hub + V((-0.016, 0.09 * math.cos(t), 0.09 * math.sin(t))) for t in (math.radians(d) for d in range(20, 181, 32))]
    strip(pts, 0.012, "accent")
    at("Cutter")
    disc(hub, 0.075, 0.006, X, "blade", segs=48)
    for k in range(16):
        t = 2 * math.pi * k / 16
        box((0.006, 0.016, 0.016), hub + V((0, 0.078 * math.cos(t), 0.078 * math.sin(t))), "blade", bevel=0,
            rot=Matrix.Rotation(t + math.pi / 4, 4, "X"))
    hex_nut(hub + V((-0.006, 0, 0)), (-1, 0, 0), 0.022)


def blue_geometry():
    torso()
    head()
    tape_gun()


def green_geometry():
    torso(back_roll=False)
    rc = head(roll=False)
    bubble_bundle("HeadReel", rc, 0.065, 0.09)
    at("Torso")
    rod(BACK - X * 0.12 + V((0, 0.01, 0)), BACK + X * 0.12 + V((0, 0.01, 0)), 0.012, "steel")
    bubble_bundle("BackRoll", BACK + V((0, 0.01, 0)), 0.11, 0.2)
    at("Torso")
    for k, (p, rz) in enumerate(((V((0.075, -0.106, 0.64)), 0.3), (V((-0.085, -0.106, 0.7)), -0.2))):
        box((0.045, 0.004, 0.045), p, "sticker", bevel=0, rot=Matrix.Rotation(rz * 0.2, 4, "Y"))
    at("Head")
    box((0.045, 0.004, 0.045), HEAD_C + V((-0.06, -0.093, -0.03)), "sticker", bevel=0, rot=Matrix.Rotation(0.1, 4, "Y"))
    tape_gun(roller=False)
    at("Roller")
    rc = gun_muzzle()
    disc(rc, 0.045, 0.09, X, "bubble", segs=24)
    for j in range(8):
        a = 2 * math.pi * j / 8
        for i in range(3):
            sphere(rc + V((-0.03 + i * 0.03, 0.045 * math.cos(a), 0.045 * math.sin(a))), 0.01, "bubble", segs=8)


def yellow_geometry():
    torso(back_roll=False)
    head()
    tape_gun(twin=True)
    at("Torso")
    rod(V((-0.13, 0.16, 0.66)), V((0.13, 0.16, 0.66)), 0.01, "steel")
    for k, x in enumerate((-0.09, -0.03, 0.03, 0.09)):
        tape_roll("Torso", V((x, 0.16, 0.66)), 0.05, 0.045, bands=[f"band{(k + 1) % 4}"], loose_end=False)
    at("Torso")
    rod(V((0.12, 0.13, 0.7)), V((0.2, 0.0, 0.7)), 0.008, "band0")


def red_geometry():
    torso()
    head()
    tape_gun(barrel=0.16, scope=True)
    at("Torso")
    box((0.27, 0.03, 0.06), V((0, -0.11, 0.72)), "accent", bevel=0.008)


GEOMETRY = {"blue": blue_geometry, "green": green_geometry, "yellow": yellow_geometry, "red": red_geometry}
HEAD_NODES = ("Head", "EyeLens", "HeadReel", "Antenna")


# =====================================================================================================
# Rig
# =====================================================================================================
def pivots():
    p = {"Body": (None, V((0, 0, HIP.z)))}
    p["Torso"] = ("Body", WAIST)
    p["Head"] = ("Torso", NECK)
    p["EyeLens"] = ("Head", NECK)
    p["HeadReel"] = ("Head", hs(HEAD_C + V((-0.155, 0.01, 0.02))))
    p["Antenna"] = ("Head", hs(HEAD_C + V((0.06, 0.04, 0.085))))
    p["BackRoll"] = ("Torso", BACK + (V((0, 0.01, 0)) if VARIANT[0] == "green" else V()))
    p["ArmR_Shoulder"] = ("Torso", SH_R)
    p["ArmR_Gun"] = ("ArmR_Shoulder", EL_R)
    if VARIANT[0] != "red":
        p["Roller"] = ("ArmR_Gun", gun_muzzle())
    p["Shot"] = ("ArmR_Gun", shot_point())
    p["ArmL_Shoulder"] = ("Torso", SH_L)
    p["ArmL_Elbow"] = ("ArmL_Shoulder", EL_L)
    p["Cutter"] = ("ArmL_Elbow", cutter_hub())
    for side, sx in SIDES:
        p[f"Leg{side}_Hip"] = ("Body", mirror(HIP, sx))
        p[f"Leg{side}_Knee"] = (f"Leg{side}_Hip", mirror(KNEE, sx))
        p[f"Leg{side}_Ankle"] = (f"Leg{side}_Knee", mirror(ANKLE, sx))
        p[f"Leg{side}_Foot"] = (f"Leg{side}_Ankle", mirror(SOLE, sx))
    if VARIANT[0] == "yellow":
        del p["BackRoll"]
    return p


def plastic_surface(color_a, color_b=None, pattern=None, seed=301):
    """Glossy printed tape: two colours in chevrons or diagonals (one tile spans the roll's width), a faint sheen."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    a = lt.srgb(color_a)[None, None, :]
    if pattern == "chevron":
        s = (((np.abs(xx - size / 2) + yy) // (size // 8)) % 2).astype(float)
    elif pattern == "diagonal":
        s = (((xx + yy) // (size // 8)) % 2).astype(float)
    else:
        s = np.zeros((size, size))
    b = lt.srgb(color_b or color_a)[None, None, :]
    grime = np.clip(lt.fbm(seed, 3.0) * 1.2, 0, 1)[..., None]
    albedo = (a * (1 - s[..., None]) + b * s[..., None]) * (1 - 0.18 * grime)
    height = 0.05 * lt.fnoise(seed + 1, 80.0)
    rough = 0.22 + 0.15 * grime[..., 0]
    return np.clip(albedo, 0, 1), height, rough, np.zeros((size, size))


def side_surface(color, seed=307):
    """A roll's side: fine wound layers as rings round the middle of the tile, a little darker than the face."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    rad = np.hypot(xx - size / 2, yy - size / 2) / (size / 2)
    ring = 0.5 + 0.5 * np.sin(rad * 2 * np.pi * 24)
    base = lt.srgb(color)[None, None, :]
    albedo = base * (0.55 + 0.3 * ring[..., None])
    return np.clip(albedo, 0, 1), 0.3 * ring, 0.25 + 0.1 * ring, np.zeros((size, size))


def materials():
    v = cfg()
    t = v["tape"]
    m = {
        "paint": lambda: lt.textured("spooler_paint", sc.battle_paint(311, v["paint"])),
        "accent": lambda: lt.textured("spooler_accent", sc.battle_paint(317, v["accent"])),
        "gun": lambda: lt.textured("spooler_gunmetal", lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)),
        "steel": lambda: lt.textured("spooler_steel", lt.metal_surface(97, "#8d9096", 0.32, 0.9, wear=0.7, edge_light=0.2)),
        "blade": lambda: lt.textured("spooler_blade", lt.metal_surface(131, "#c8ccd2", 0.2, 1.0, wear=0.3, edge_light=0.4)),
        "rubber": lambda: lt.textured("spooler_rubber", lt.rubber_surface(101), normal_strength=2.0),
        "roller": lambda: lt.textured("spooler_roller", sc.cloth_surface(137, "#2c55a8")),
        "bezel": lambda: lt.textured("spooler_bezel", lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)),
        "white": lambda: lt.textured("spooler_white", lt.paint_surface(139, "#e8e6de", "#4a4e54")),
        "core": lambda: lt.textured("spooler_core", sc.cloth_surface(149, "#b08a58")),
        "glow": glow_material,
        "glowgreen": lambda: lamp_material("spooler_gauge_green", (0.1, 1.0, 0.15)),
        "lamp": lambda: lamp_material("spooler_lamp", (1.0, 0.08, 0.02)),
    }
    if t[0] == "bubble":
        m["bubble"] = bubble_material
        m["sticker"] = lambda: lt.textured("spooler_sticker", sc.cloth_surface(151, "#e2c040"))
        m["pennant"] = lambda: lt.textured("spooler_pennant", plastic_surface(t[1]))
        m["pad"] = m["pennant"]
    elif t[0] == "bands":
        for k, col in enumerate(t[1:]):
            m[f"band{k}"] = lambda col=col, k=k: lt.textured(f"spooler_band{k}", plastic_surface(col, seed=301 + k))
            m[f"band{k}side"] = lambda col=col, k=k: lt.textured(f"spooler_band{k}_side", side_surface(col, seed=307 + k))
        m["pennant"] = m["band0"]
        m["pad"] = m["band3"]
    else:
        m["tape"] = lambda: lt.textured("spooler_tape", plastic_surface(t[1], t[2], t[0]))
        m["tapeside"] = lambda: lt.textured("spooler_tape_side", side_surface(t[1]))
        m["pennant"] = m["tape"]
        m["pad"] = lambda: lt.textured("spooler_pad", plastic_surface(t[1]))
    return m


def lamp_material(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*(c * 0.5 for c in rgb), 1)
    b.inputs["Roughness"].default_value = 0.2
    b.inputs["Emission Color"].default_value = (*rgb, 1)
    b.inputs["Emission Strength"].default_value = 2.0
    return mat


def bubble_material():
    """Bubble wrap: pale blue and glossy."""
    mat = bpy.data.materials.new("spooler_bubble")
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.42, 0.72, 0.9, 1)
    b.inputs["Roughness"].default_value = 0.08
    b.inputs["Coat Weight"].default_value = 0.6
    return mat


def glow_material():
    """The eye: the game sets its colour (green calm, red engaged); the file holds it green."""
    mat = bpy.data.materials.new("spooler_glow")
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.05, 0.6, 0.08, 1)
    b.inputs["Roughness"].default_value = 0.15
    b.inputs["Emission Color"].default_value = (0.1, 1.0, 0.15, 1)
    b.inputs["Emission Strength"].default_value = 3.0
    return mat


def roll_uvs(me, name, pivot):
    """Fixed UVs for tape faces: round the roll for its face (u across the roll, v along the arc, a tile per roll
    width), centred on the roll for its sides. Other faces keep box UVs (None)."""
    mats = [m.name for m in me.materials]
    rolls = [r for r in ROLLS if r[0] == name]
    fixed = [None] * len(me.polygons)
    if not rolls:
        return fixed
    co = [V(v.co) + pivot for v in me.vertices]
    for poly in me.polygons:
        mname = mats[poly.material_index]
        if not mname.startswith("spooler_tape") and not mname.startswith("spooler_band"):
            continue
        cen = sum((co[i] for i in poly.vertices), V()) / len(poly.vertices)
        roll = min(rolls, key=lambda r: abs(cen.x - r[1].x) + abs((cen - r[1]).yz.length - r[2]) * 0.5)
        _, c, r, w, _ = roll
        pts = [co[i] for i in poly.vertices]
        if abs(poly.normal.x) > 0.7:
            fixed[poly.index] = [((p.y - c.y) / (2 * r) + 0.5, (p.z - c.z) / (2 * r) + 0.5) for p in pts]
        else:
            angs = [math.atan2(p.z - c.z, p.y - c.y) for p in pts]
            base = angs[0]
            angs = [base + ((a - base + math.pi) % (2 * math.pi) - math.pi) for a in angs]
            fixed[poly.index] = [((p.x - c.x) / w + 0.5, a * r / w) for p, a in zip(pts, angs)]
    # loop order: Blender's polygon loops follow poly.vertices
    return fixed


def build():
    """Builds the rig; returns (root, nodes by name)."""
    sc.GEO.clear()
    ROLLS.clear()
    legs()
    cutter()
    shot_piece()
    GEOMETRY[VARIANT[0]]()
    # grow the head about the neck (the concept's head was drawn small and approved at 1.3x)
    for n in HEAD_NODES:
        if n in sc.GEO:
            sc.GEO[n].v = [tuple(hs(v)) for v in sc.GEO[n].v]
    # the shot is drawn at the gun's scale and grown so it reads in flight from across a base
    c = shot_point()
    sc.GEO["Shot"].v = [tuple(c + (V(v) - c) * SHOT_SCALE) for v in sc.GEO["Shot"].v]
    for i, r in enumerate(ROLLS):
        if r[0] in HEAD_NODES:
            ROLLS[i] = (r[0], hs(r[1]), r[2] * HEAD_SCALE, r[3] * HEAD_SCALE, r[4])
    makers = materials()
    order = list(makers)
    mats = {}
    made = {}
    root = bpy.data.objects.new("Spooler", None)
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
                # aliases (the pennant and pad reuse a tape) share one material
                if id(makers[m]) not in made:
                    made[id(makers[m])] = makers[m]()
                mats[m] = made[id(makers[m])]
            me.materials.append(mats[m])
        me.polygons.foreach_set("material_index", [used.index(m) for m in geo.m])
        me.validate()
        for poly in me.polygons:
            poly.use_smooth = True
        lt.box_uvs(me, roll_uvs(me, name, pivot))
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
# the Torso or Body pitches forward... see each helper.
# =====================================================================================================
def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


class Pose:
    def __init__(self, nodes):
        self.nodes = nodes
        self.rot = {n: Quaternion() for n in nodes}
        self.off = {n: Vector() for n in nodes}

    def turn(self, name, axis, deg):
        if name in self.rot:
            self.rot[name] = q_axis(axis, deg) @ self.rot[name]

    def move(self, name, v):
        if name in self.off:
            self.off[name] += Vector(v)

    def leg(self, side, swing=0.0, lift=0.0, out=0.0, flat=True):
        """swing: the foot forward (deg about the hip); lift: raise the foot by folding the reverse knee (deg).
        The ankle keeps the reel foot level when `flat`."""
        sx = -1 if side == "L" else 1
        self.turn(f"Leg{side}_Hip", Y, -sx * out)
        hip = -swing + 0.6 * lift            # the thigh tips back as the knee folds
        knee = -1.5 * lift                   # a reverse knee folds by swinging the shin forward
        self.turn(f"Leg{side}_Hip", X, hip)
        self.turn(f"Leg{side}_Knee", X, knee)
        if flat:
            self.turn(f"Leg{side}_Ankle", X, -(hip + knee))

    def gun(self, raise_=0.0, out=0.0, pitch=None, recoil=0.0):
        """raise_: lift the gun arm forward; pitch: where the gun points (deg up from level; by default it rides
        with the arm); recoil: kick back along the gun (m)."""
        self.turn("ArmR_Shoulder", Y, -out)
        self.turn("ArmR_Shoulder", X, -raise_)
        if pitch is not None:
            self.turn("ArmR_Gun", X, -(pitch - raise_ + math.degrees(GUN_PITCH)))
        self.move("ArmR_Gun", (0, recoil, 0))

    def cutter(self, raise_=0.0, out=0.0, elbow=0.0):
        self.turn("ArmL_Shoulder", Y, out)
        self.turn("ArmL_Shoulder", X, -raise_)
        self.turn("ArmL_Elbow", X, -elbow)

    def apply(self):
        piv = pivots()
        for n, ob in self.nodes.items():
            parent, pivot = piv[n]
            ob.rotation_quaternion = self.rot[n]
            ob.location = pivot - (piv[parent][1] if parent else Vector()) + self.off[n]


# Gaits: (clip length s, swing degrees, lift, lean, bob m)
GAITS = {"patrol": (0.7, 20, 34, 3, 0.015), "chase": (0.42, 32, 52, 10, 0.03)}
SHARED_CLIPS = {"idle": 2.4, "patrol": GAITS["patrol"][0], "chase": GAITS["chase"][0], "alert": 0.8, "lose_interest": 0.7,
                "hit": 0.35, "death": 1.6, "jump": 0.35, "fall": 0.6, "land": 0.35}
CLIPS = {}


def use(variant):
    VARIANT[0] = variant
    CLIPS.clear()
    CLIPS.update(SHARED_CLIPS)
    for clips in VARIANTS[variant]["attacks"].values():
        for clip in clips:
            CLIPS[clip] = ATTACK_CLIPS[clip]


def gait_speed(clip):
    """Ground speed at speed scale 1: each stance sweeps the foot through 2 * swing about the hip."""
    length, swing = GAITS[clip][:2]
    reach = HIP.z - SOLE.z
    return 2 * reach * math.sin(math.radians(swing)) / (length / 2)


def gait(p, clip, u):
    _, swing, lift, lean, bob = GAITS[clip]
    w = 2 * math.pi * u
    chase = clip == "chase"
    for side in "LR":
        ph = w if side == "L" else w + math.pi
        # stance: the foot sweeps back along the ground; swing: it lifts and comes forward
        p.leg(side, swing * math.sin(ph), lift * max(0.0, math.cos(ph)) ** 1.5)
    p.move("Body", (0, 0, -bob * (1 - math.cos(2 * w)) / 2 - (0.03 if chase else 0.0)))
    p.turn("Body", Y, 3 * math.sin(w))
    p.turn("Torso", X, lean)
    p.turn("Torso", UP, 6 * math.sin(w))
    p.turn("Head", X, -lean * 0.7)
    p.turn("Head", UP, -4 * math.sin(w))
    if chase:
        # gun up and level as it runs to get in range
        p.gun(raise_=40 + 4 * math.sin(2 * w), pitch=2 * math.sin(2 * w))
        p.cutter(raise_=25 + 8 * math.sin(w), out=10, elbow=10)
    else:
        p.gun(raise_=8 * math.sin(w) + 4)
        p.cutter(raise_=-8 * math.sin(w) + 4)
    p.turn("Antenna", X, (14 if chase else 7) * math.sin(2 * w))


def shot(p, t, fire_at=0.15, kick=1.0, hold=0.0):
    """Snap the gun up level, fire (a recoil kick at `fire_at`), then come back down."""
    up = env(t, 0.0, fire_at * 0.8) * (1 - env(t, fire_at + 0.25 + hold, fire_at + 0.5 + hold))
    k = pulse(t, fire_at, fire_at + 0.03, fire_at + 0.18) * kick
    rest = -math.degrees(GUN_PITCH)
    p.gun(raise_=55 * up - 8 * k, pitch=rest * (1 - up) + 10 * k, recoil=0.04 * k)
    p.turn("Torso", UP, 10 * up - 4 * k)
    p.turn("Torso", X, -6 * k)
    p.move("Body", (0, 0.02 * k, -0.01 * k))
    p.cutter(raise_=20 * up, out=14 * up, elbow=20 * up)
    p.turn("Head", UP, -8 * up)
    for side in "LR":
        p.leg(side, (8 if side == "R" else -6) * up, 6 * up)
    return k


def pose(p, clip, t, length):
    u = t / length
    w = 2 * math.pi * u
    if clip == "idle":
        p.move("Body", (0, 0, -0.006 * (1 - math.cos(w)) / 2))
        p.turn("Torso", X, 2 * math.sin(w))
        p.turn("Head", UP, 24 * math.sin(w))
        p.turn("Head", X, 5 * math.sin(2 * w))
        p.gun(raise_=4 + 2 * math.sin(w + 1))
        p.cutter(raise_=4 + 2 * math.sin(w))
        p.turn("Cutter", X, -360 * u)
        for side, sgn in (("L", -1), ("R", 1)):
            p.leg(side, 0, 3 + 2 * sgn * math.sin(w))
        p.turn("Antenna", X, 4 * math.sin(w))
    elif clip in ("patrol", "chase"):
        gait(p, clip, u)
    elif clip == "alert":
        k = env(t, 0.0, 0.22) - 0.3 * env(t, 0.45, 0.8)
        jolt = pulse(t, 0.0, 0.08, 0.25)
        p.move("Body", (0, 0, -0.03 * k))
        p.turn("Torso", X, -8 * jolt)
        p.turn("Head", X, -12 * jolt)
        p.gun(raise_=50 * k, pitch=-math.degrees(GUN_PITCH) * (1 - k) + 5 * k)
        p.cutter(raise_=30 * k, out=12 * k, elbow=30 * k)
        p.turn("Cutter", X, -720 * env(t, 0.1, 0.8))
        p.turn("Antenna", X, 25 * pulse(t, 0, 0.1, 0.5))
        for side in "LR":
            p.leg(side, 6 * k, 14 * k)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 0.7)
        p.turn("Head", UP, 30 * math.sin(math.pi * u))
        p.gun(raise_=40 * k + 4, pitch=-math.degrees(GUN_PITCH) * (1 - k) - 4)
        p.cutter(raise_=20 * k + 4)
        for side in "LR":
            p.leg(side, 0, 8 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.06, 0.35)
        p.move("Body", (0, 0.04 * k, -0.01 * k))
        p.turn("Torso", X, -14 * k)
        p.turn("Torso", UP, 8 * k)
        p.turn("Head", X, -18 * k)
        p.turn("Head", Y, 10 * k)
        p.gun(raise_=-10 * k, out=14 * k)
        p.cutter(raise_=-10 * k, out=14 * k)
        p.turn("Antenna", X, 30 * k)
        for side in "LR":
            p.leg(side, -4 * k, 10 * k)
    elif clip == "death":
        # the reverse knees give, it sits back onto its heels, then topples forward onto its face; the reel spins loose
        buckle = env(t, 0.05, 0.45)
        topple = env(t, 0.4, 1.0)
        flop = env(t, 0.95, 1.4)
        p.move("Body", (0, -0.18 * topple, -0.2 * buckle - 0.14 * topple))
        p.turn("Body", X, 80 * topple - 10 * buckle * (1 - topple))
        p.turn("Head", X, 20 * buckle - 25 * topple)
        p.turn("Head", UP, 16 * flop)
        p.gun(raise_=20 * buckle + 70 * topple, out=30 * flop)
        p.cutter(raise_=20 * buckle + 60 * topple, out=35 * flop)
        for side in "LR":
            p.leg(side, 20 * buckle - 60 * topple, 70 * buckle - 50 * topple, flat=False)
        p.turn("HeadReel", X, -540 * env(t, 0.3, 1.5))
        p.turn("Antenna", X, -40 * topple + 20 * flop)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.15, 0.3)
        push = env(t, 0.15, 0.35)
        p.move("Body", (0, 0, -0.08 * crouch))
        for side in "LR":
            p.leg(side, 10 * crouch - 6 * push, 40 * crouch - 10 * push)
        p.gun(raise_=-10 * crouch + 40 * push)
        p.cutter(raise_=-10 * crouch + 40 * push, out=10 * push)
    elif clip == "fall":
        for side, sgn in (("L", 1), ("R", -1)):
            p.leg(side, 10 + 8 * sgn * math.sin(w * 2), 30 + 10 * math.sin(w * 2 + sgn))
        p.gun(raise_=40 + 10 * math.sin(w * 2), out=30)
        p.cutter(raise_=40 + 10 * math.sin(w * 2 + 1), out=30)
        p.turn("Torso", X, -6)
        p.turn("Antenna", X, 15 * math.sin(w * 3))
    elif clip == "land":
        k = pulse(t, 0.0, 0.08, 0.35)
        p.move("Body", (0, 0, -0.09 * k))
        for side in "LR":
            p.leg(side, 6 * k, 40 * k)
        p.gun(raise_=20 * k, out=10 * k)
        p.cutter(raise_=20 * k, out=10 * k)
        p.turn("Torso", X, 10 * k)
    elif clip in ("tape_shot", "bubble_shot"):
        shot(p, t)
        p.turn("Roller", X, -360 * env(t, 0.1, 0.5))
        p.turn("HeadReel", X, -180 * env(t, 0.1, 0.6))
    elif clip == "rapid_shot":
        # two quick shots, the twin rollers whirring
        shot(p, t, fire_at=0.12, kick=0.7, hold=-0.05)
        k2 = pulse(t, 0.27, 0.3, 0.42) * 0.7
        p.turn("ArmR_Gun", X, -8 * k2)
        p.move("ArmR_Gun", (0, 0.03 * k2, 0))
        p.turn("Roller", X, -720 * env(t, 0.05, 0.45))
        p.turn("HeadReel", X, -360 * env(t, 0.05, 0.5))
    elif clip == "heavy_shot":
        # it plants its feet, sights down the scope, and the long barrel kicks hard
        shot(p, t, fire_at=0.18, kick=1.6, hold=0.35)
        sight = env(t, 0.05, 0.18) * (1 - env(t, 0.55, 0.85))
        p.turn("Head", X, 8 * sight)
        p.turn("Head", UP, -10 * sight)
        p.move("Body", (0, 0, -0.03 * sight))
        p.turn("HeadReel", X, -240 * env(t, 0.15, 0.8))
    else:
        raise ValueError(clip)


def set_pose(nodes, clip, t, length):
    p = Pose(nodes)
    pose(p, clip, *retimed(TELEGRAPH, clip, t, length))
    # A hair of motion (a quarter degree) on every node, so no channel holds still through a clip.
    for i, n in enumerate(nodes):
        p.turn(n, X, 0.25 * math.sin(2 * math.pi * t / length) * (1 if i % 2 else -1))
    p.apply()


def bake_clips(nodes):
    """Keys every rig node every frame of every clip (see build_scuttler.bake_clips)."""
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
        speeds = ", ".join(f"{clip} {gait_speed(clip):.2f}" for clip in GAITS)
        print(f"wrote {out.relative_to(REPO)} (clip speeds, m/s: {speeds})")


# =====================================================================================================
# Review renders
# =====================================================================================================
def render_review(out: Path, variant):
    use(variant)
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    bpy.context.scene.cycles.samples = SAMPLES
    root, nodes = build()
    root.rotation_euler = (0, 0, 0)          # renders keep the build's -Y front
    for ob in nodes["Shot"].children:        # the game hides the shot until it flies
        ob.hide_render = True
    c = (0, 0, 0.58)
    shots = []

    def take(name, clip, t, cam):
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t, CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cam, out / f"{variant}_{name}.png", 800, 900)
        shots.append((name, path))

    tq = rl.camera("tq", (-2.0, -2.8, 1.5), c, ortho=1.45)
    side = rl.camera("side", (4, 0, 0.6), c, ortho=1.4)
    take("rest", None, 0, tq)
    take("front", None, 0, rl.camera("front", (0, -4, 0.6), c, ortho=1.4))
    take("side", None, 0, side)
    take("rear", None, 0, rl.camera("rear", (2.2, 2.6, 1.4), c, ortho=1.45))
    take("idle", "idle", 0.6, tq)
    take("patrol", "patrol", CLIPS["patrol"] * 0.25, side)
    take("chase", "chase", CLIPS["chase"] * 0.25, tq)
    take("alert", "alert", 0.3, tq)
    clip = VARIANTS[variant]["attacks"][next(iter(VARIANTS[variant]["attacks"]))][0]
    take("aim", clip, 0.1, side)
    take("fire", clip, {"heavy_shot": 0.2}.get(clip, 0.16), tq)
    take("hit", "hit", 0.06, tq)
    take("land", "land", 0.08, tq)
    take("jump", "jump", 0.15, side)
    take("death_fall", "death", 0.6, side)
    take("death", "death", 1.6, tq)

    from render_lift import INK, font as F  # noqa: PLC0415
    w, h = 400, 450
    cols = 5
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (60 + cols * (w + 16) + 40, 150 + rows * (h + 16) + 20), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), f"{VARIANTS[variant]['name'].upper()} (game model and clips)", fill=INK, font=F(40, True))
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
    if "--render" in args:
        for key in chosen:
            render_review(Path(args[args.index("--render") + 1]), key)
    else:
        build_game(chosen)
