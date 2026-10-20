"""Build the three Rakehands (Chase picked all three concepts on 2026-10-19, the Field Hand without its hat).

Rakehands are lanky farmhand bots on two legs whose forearms end in steel pitchfork tines instead of hands:

* Field Hand (``rakehand``): a milk-churn torso in a riveted overalls bib, a boxy lamp head with an antenna, an
  exhaust stack; a 4-tine fork on the right arm and a 3-tine fork on the left;
* Baler (``rakehand_baler``): hunched and heavy, a slatted baler-box body slung forward on bent legs, a cab head with
  a visor and two lamps, a twine-bound bale drum on its back, long arms that drag 5-tine hay forks;
* Scarecrow (``rakehand_scarecrow``): a wooden field-post spine with a shoulder yoke, a caged lantern for a head,
  burlap rags hanging off the yoke, stilt legs with claw feet and 3-tine forks. It stands like a scarecrow, arms
  out, until a player comes close.

The paint is the Scuttlers' battle wear: chipped through to rust and bare steel, scratched and grimy.

What it writes: ``mods/vanilla/meshes/bots/<id>.glb``. Feet are at the origin and it faces -Z, as bots do. The
nodes are the rig (the game plays the clips on them and plants the feet):

* ``Body`` (the pelvis), ``Torso``, ``Head`` and ``EyeLens`` (the glowing eye, which the game colours green while
  calm and red while engaged; the Baler's ``LampGlow`` lamps too);
* ``Arm<L|R>_Shoulder`` / ``_Elbow`` / ``_Wrist`` (the wrist carries the fork);
* ``Leg<L|R>_Hip`` / ``_Knee`` / ``_Ankle`` / ``_Foot`` (``_Foot`` is an empty under the sole);
* Field Hand ``Antenna``; Baler ``Drum``; Scarecrow ``Rag0`` … ``Rag5``.

Clips (seconds; the loops loop): ``idle`` 2.4, ``patrol`` and ``chase`` (gaits; their ground speeds at speed
scale 1 are printed and go in the bot's ``model.clip_speeds``), ``alert`` 0.8, ``lose_interest`` 0.7, ``hit`` 0.35,
``death`` 1.6, ``jump`` 0.35, ``fall`` 0.6, ``land`` 0.35, and the attacks: Field Hand ``fork_jab`` and
``fork_sweep``, Baler ``pitch`` and ``haymaker``, Scarecrow ``lunge_jab`` and ``spin_sweep``. Strikes land about
0.2 s in: the host deals the damage as an attack starts.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/rakehand/build_rakehand.py [--variant baler ...]                # writes the .glb files
    python tools/rakehand/build_rakehand.py --render OUT_DIR [--variant ...]     # review renders and sheets

The output is committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up and the model is built facing -Y, then turned to face -Z in glTF. The build is
deterministic. Geometry helpers, paint, clip baking and export are the Scuttler build's (tools/scuttler).
"""

from __future__ import annotations

import math
import os
import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "lift"))
sys.path.insert(0, str(REPO / "tools" / "scuttler"))
import lift_textures as lt  # noqa: E402
import build_scuttler as sc  # noqa: E402
from build_scuttler import at, box, disc, env, pulse, rod, smooth, sphere, strike_early  # noqa: E402

MESHES = REPO / "mods" / "vanilla" / "meshes" / "bots"
FPS = sc.FPS
SAMPLES = int(os.environ.get("RAKEHAND_SAMPLES", "48"))
UP = Vector((0, 0, 1))
X = Vector((1, 0, 0))
Y = Vector((0, 1, 0))
V = Vector

# The skeleton of each variant (model space, front is -Y): hip, knee, ankle and sole of the right leg (the left
# mirrors it), the waist, shoulder, elbow and wrist of the right arm, the neck, the fork's tines, length and width.
VARIANTS = {
    "field": dict(
        id="rakehand", name="Rakehand", paint="#3f7a3a", trim="#e2b23a",
        hip=V((0.13, 0, 0.86)), knee=V((0.14, -0.04, 0.47)), ankle=V((0.14, 0.0, 0.11)), sole=V((0.14, -0.05, 0.0)),
        waist=V((0, 0, 0.92)), shoulder=V((0.25, 0, 1.33)), elbow=V((0.31, -0.04, 1.06)), wrist=V((0.34, -0.12, 0.82)),
        neck=V((0, -0.01, 1.42)), tines={"R": 4, "L": 3}, fork=(0.42, 0.26),
        attacks={"poke": ["fork_jab"], "swing": ["fork_sweep"]}),
    "baler": dict(
        id="rakehand_baler", name="Baler Rakehand", paint="#a33122", trim="#e8e2d2",
        hip=V((0.18, 0.05, 0.72)), knee=V((0.21, -0.12, 0.4)), ankle=V((0.2, 0.02, 0.11)), sole=V((0.2, -0.03, 0.0)),
        waist=V((0, 0.02, 0.78)), shoulder=V((0.34, -0.06, 1.26)), elbow=V((0.42, -0.18, 0.96)), wrist=V((0.45, -0.3, 0.7)),
        neck=V((0, -0.17, 1.33)), tines={"R": 5, "L": 5}, fork=(0.5, 0.34),
        attacks={"poke": ["pitch"], "swing": ["haymaker"]}),
    "scarecrow": dict(
        id="rakehand_scarecrow", name="Scarecrow Rakehand", paint="#5b3a5c", trim="#c9a02a",
        hip=V((0.1, 0, 0.9)), knee=V((0.12, -0.06, 0.5)), ankle=V((0.12, 0.0, 0.12)), sole=V((0.12, -0.02, 0.0)),
        waist=V((0, 0, 0.94)), shoulder=V((0.31, 0, 1.32)), elbow=V((0.4, -0.03, 1.07)), wrist=V((0.43, -0.12, 0.83)),
        neck=V((0, 0, 1.47)), tines={"R": 3, "L": 3}, fork=(0.44, 0.22),
        attacks={"poke": ["lunge_jab"], "swing": ["spin_sweep"]}),
}
ATTACK_CLIPS = {"fork_jab": 0.7, "fork_sweep": 1.0, "pitch": 0.9, "haymaker": 1.2, "lunge_jab": 0.55, "spin_sweep": 1.0}
VARIANT = ["field"]
SIDES = (("L", -1), ("R", 1))


def cfg():
    return VARIANTS[VARIANT[0]]


def mirror(v, sx):
    return V((v.x * sx, v.y, v.z))


def leg_points(sx):
    c = cfg()
    return [mirror(c[k], sx) for k in ("hip", "knee", "ankle", "sole")]


def arm_points(sx):
    c = cfg()
    return [mirror(c[k], sx) for k in ("shoulder", "elbow", "wrist")]


def fork_frame(sx):
    """The fork's pointing direction, its crossbar direction and its 'forward' (where the tines curl)."""
    _, el, wr = arm_points(sx)
    d = ((wr - el).normalized() + V((0, -0.35, 0))).normalized()
    across = d.cross(Y).normalized()
    if across.x * sx < 0:
        across = -across
    fwd = across.cross(d).normalized()
    if fwd.y > 0:
        fwd = -fwd
    return d, across, fwd


# =====================================================================================================
# Geometry
# =====================================================================================================
def hay(c, d, n, seed, length=0.12, spread=0.6):
    """A tuft of stray hay: `n` thin straws from `c`, fanning out round direction `d`."""
    rng = random.Random(seed)
    d = Vector(d).normalized()
    for _ in range(n):
        j = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * spread
        s = (d + j).normalized()
        a = Vector(c) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.012
        ln = length * rng.uniform(0.55, 1.15)
        bend = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), -0.6)).normalized() * ln * 0.18
        m = a + s * ln * 0.55
        rod(a, m, 0.0035, "hay", r2=0.0032, segs=5)
        rod(m, m + s * ln * 0.45 + bend, 0.0032, "hay", r2=0.0012, segs=5)


def limb(a, b, r, mat="paint", armour=True, armour_r=None):
    rod(a, b, r, "gun")
    if armour:
        rod(a.lerp(b, 0.15), a.lerp(b, 0.82), armour_r or r * 1.45, mat, segs=10)


def fork(sx, side):
    c = cfg()
    _, el, wr = arm_points(sx)
    d, across, fwd = fork_frame(sx)
    length, width = c["fork"]
    n = c["tines"][side]
    sock = wr + d * 0.1
    sphere(wr, 0.045, "gun")
    rod(wr, sock, 0.046, "gun", r2=0.04)
    rod(sock - d * 0.025, sock, 0.052, "steel")
    bar = sock + d * 0.03
    rod(bar - across * width / 2, bar + across * width / 2, 0.022, "steel", segs=12)
    for s in (-1, 1):
        sphere(bar + across * s * width / 2, 0.025, "steel", segs=12)
    for k in range(n):
        t = k / (n - 1) - 0.5
        base = bar + across * t * width
        mid = base + d * length * 0.55 + across * t * 0.03
        tip = base + d * length + fwd * 0.1 + across * t * 0.05
        rod(base, mid, 0.014, "steel", segs=10)
        sphere(mid, 0.014, "steel", segs=8)
        rod(mid, tip, 0.014, "steel", r2=0.003, segs=10)
        if k in (0, n - 2):
            hay(base.lerp(mid, 0.7 + 0.1 * k / n), across * (1 if k else -1) + fwd * 0.5, 7, 300 + 10 * k + sx, 0.14)


def arms(armour_fore=True, shoulder_r=0.075, shoulder="paint"):
    for side, sx in SIDES:
        sh, el, wr = arm_points(sx)
        at(f"Arm{side}_Shoulder")
        sphere(sh, shoulder_r, shoulder)
        limb(sh, el, 0.035, armour_r=0.05)
        rod(sh.lerp(el, 0.4) + V((0, 0.05, 0)), sh.lerp(el, 0.95) + V((0, 0.05, 0)), 0.024, "gun")
        at(f"Arm{side}_Elbow")
        sphere(el, 0.05, "steel")
        limb(el, wr, 0.032, armour=armour_fore, armour_r=0.045)
        rod(el.lerp(wr, 0.05) + V((0, 0.045, 0)), el.lerp(wr, 0.7) + V((0, 0.045, 0)), 0.012, "steel")
        at(f"Arm{side}_Wrist")
        fork(sx, side)


def legs(boot=True, armour=True, shin="paint"):
    for side, sx in SIDES:
        hip, knee, ankle, sole = leg_points(sx)
        at(f"Leg{side}_Hip")
        sphere(hip, 0.06, "gun")
        limb(hip, knee, 0.045, armour=armour, armour_r=0.066)
        at(f"Leg{side}_Knee")
        sphere(knee, 0.055, "steel")
        limb(knee, ankle, 0.04, mat=shin, armour=armour, armour_r=0.056)
        at(f"Leg{side}_Ankle")
        sphere(ankle, 0.05, "gun")
        if boot:
            box((0.15, 0.27, 0.09), V((ankle.x, sole.y + 0.0, 0.05)), "rubber", bevel=0.02)
            box((0.155, 0.08, 0.06), V((ankle.x, sole.y - 0.12, 0.035)), "steel", bevel=0.015)
            rod(ankle, V((ankle.x, ankle.y, 0.09)), 0.045, "gun")
            hay(V((ankle.x + sx * 0.04, ankle.y + 0.05, 0.1)), (sx, 0.5, 0.6), 5, 20 + sx, 0.09)
        else:
            disc(V((ankle.x, ankle.y, 0.06)), 0.06, 0.05, (0, 0, 1), "gun")
            rod(ankle, V((ankle.x, ankle.y, 0.08)), 0.03, "gun")
            for k in range(3):
                a = math.radians(-90 + (k - 1) * 38)
                tip = V((ankle.x + 0.15 * math.cos(a), ankle.y + 0.15 * math.sin(a), 0.008))
                rod(V((ankle.x, ankle.y, 0.05)), tip, 0.018, "steel", r2=0.004, segs=10)
            rod(V((ankle.x, ankle.y, 0.05)), V((ankle.x, ankle.y + 0.1, 0.008)), 0.016, "steel", r2=0.004, segs=10)


def eye(c, r=0.06, depth=0.03):
    at("Head")
    disc(c, r + 0.018, depth, (0, 1, 0), "bezel")
    at("EyeLens")
    sphere(c + V((0, -depth / 2, 0)), r, "glow", scale=(1, 0.45, 1))


def field_geometry():
    legs(shin="trim")
    at("Body")
    rod(V((-0.17, 0, 0.86)), V((0.17, 0, 0.86)), 0.065, "gun")
    rod(V((0, 0, 0.82)), V((0, 0, 0.95)), 0.13, "gun", r2=0.15, segs=20)
    at("Torso")
    # milk-churn torso: wide belly tapering to the shoulders, three steel hoops
    rod(V((0, 0, 0.92)), V((0, 0, 1.3)), 0.2, "paint", r2=0.17, segs=32)
    rod(V((0, 0, 1.3)), V((0, 0, 1.4)), 0.17, "paint", r2=0.12, segs=32)
    for z in (0.95, 1.12, 1.28):
        rod(V((0, 0, z)), V((0, 0, z + 0.025)), 0.207 - (z - 0.9) * 0.08, "trim", segs=32)
    # overalls bib: a riveted denim plate with a pocket, straps over the shoulders to the back
    box((0.27, 0.04, 0.3), V((0, -0.19, 1.1)), "denim", bevel=0.012)
    box((0.12, 0.014, 0.09), V((0, -0.213, 1.07)), "denim", bevel=0.004)
    for sx in (-1, 1):
        box((0.05, 0.03, 0.26), V((sx * 0.1, -0.13, 1.31)), "denim", rot=Matrix.Rotation(-0.6, 4, "X"))
        box((0.05, 0.03, 0.3), V((sx * 0.1, 0.17, 1.2)), "denim", rot=Matrix.Rotation(0.25, 4, "X"))
        sphere(V((sx * 0.1, -0.215, 1.23)), 0.017, "steel", segs=10)
        sphere(V((sx * 0.1, -0.215, 0.98)), 0.014, "steel", segs=10)
    hay(V((0, -0.22, 1.11)), (0, -0.4, 1), 9, 11, 0.12)
    hay(V((0.17, -0.1, 0.97)), (1, -0.6, 0.2), 6, 12, 0.1)
    hay(V((-0.12, 0.15, 1.29)), (-0.6, 0.5, 0.8), 6, 13, 0.11)
    # exhaust stack on the back
    rod(V((0.08, 0.17, 1.0)), V((0.1, 0.205, 1.56)), 0.03, "gun")
    rod(V((0.1, 0.205, 1.56)), V((0.104, 0.212, 1.61)), 0.045, "gun")
    for z in (1.08, 1.32):
        box((0.08, 0.06, 0.02), V((0.085, 0.175, z)), "steel", bevel=0.004)
    rod(V((0, 0, 1.4)), V((0, -0.01, 1.47)), 0.04, "steel")
    for k in range(2):
        rod(V((0, 0, 1.415 + k * 0.025)), V((0, 0, 1.425 + k * 0.025)), 0.052, "gun")
    at("Head")
    hc = V((0, -0.03, 1.56))
    box((0.21, 0.19, 0.17), hc, "paint", bevel=0.022)
    box((0.24, 0.07, 0.03), hc + V((0, -0.09, 0.075)), "trim", bevel=0.01, rot=Matrix.Rotation(math.radians(-10), 4, "X"))
    box((0.17, 0.045, 0.04), hc + V((0, -0.085, -0.07)), "gun", bevel=0.008)
    for k in range(4):
        box((0.012, 0.012, 0.028), hc + V((-0.045 + k * 0.03, -0.11, -0.07)), "steel", bevel=0)
    for sx in (-1, 1):
        disc(hc + V((sx * 0.112, 0, 0)), 0.04, 0.025, (1, 0, 0), "gun")
        disc(hc + V((sx * 0.125, 0, 0)), 0.018, 0.012, (1, 0, 0), "steel")
    eye(hc + V((0, -0.098, 0.005)))
    at("Head")
    hay(hc + V((-0.05, 0.02, 0.085)), (-0.3, 0.2, 1), 8, 14, 0.1)
    at("Antenna")
    base = hc + V((0.06, 0.04, 0.085))
    rod(base, base + V((0, 0, 0.03)), 0.022, "gun")
    rod(base, base + V((0.01, 0.02, 0.2)), 0.007, "steel")
    sphere(base + V((0.01, 0.02, 0.21)), 0.017, "trim", segs=10)
    arms(shoulder="trim")


def baler_geometry():
    legs()
    at("Body")
    rod(V((-0.22, 0.05, 0.72)), V((0.22, 0.05, 0.72)), 0.075, "gun")
    box((0.44, 0.38, 0.14), V((0, 0.02, 0.8)), "gun", bevel=0.02)
    at("Torso")
    # the baler box, leaning forward a touch, slats across its front and a hazard band
    lean = Matrix.Rotation(math.radians(8), 4, "X")
    box((0.54, 0.44, 0.5), V((0, -0.02, 1.08)), "paint", bevel=0.03, rot=lean)
    for k in range(5):
        box((0.5, 0.025, 0.025), V((0, -0.265 + 0.0 * k, 0.9 + k * 0.07)) + V((0, -0.02 * (k - 2) * 0.35, 0)), "trim", bevel=0.004)
    box((0.42, 0.02, 0.06), V((0, -0.26, 1.27)), "hazard", bevel=0.004)
    for k, x in enumerate((-0.16, 0.05, 0.19)):
        hay(V((x, -0.27, 0.95 + 0.07 * k)), (0, -1, -0.3), 7, 50 + k, 0.1)
    for sx in (-1, 1):
        for z in (0.88, 1.27):
            sphere(V((sx * 0.25, -0.24, z)), 0.016, "steel", segs=10)
        # shoulder pauldrons
        box((0.2, 0.22, 0.06), V((sx * 0.35, -0.06, 1.37)), "trim", bevel=0.015, rot=Matrix.Rotation(sx * math.radians(-12), 4, "Y"))
    # the bale drum's cradle and axle
    for sx in (-1, 1):
        box((0.04, 0.12, 0.3), V((sx * 0.25, 0.28, 1.15)), "gun", bevel=0.01)
    rod(V((-0.28, 0.33, 1.15)), V((0.28, 0.33, 1.15)), 0.03, "steel")
    at("Head")
    cab = V((0, -0.22, 1.42))
    box((0.27, 0.21, 0.2), cab, "paint", bevel=0.02)
    box((0.31, 0.11, 0.03), cab + V((0, -0.07, 0.11)), "trim", bevel=0.008)
    box((0.22, 0.03, 0.05), cab + V((0, -0.105, -0.065)), "gun", bevel=0.006)
    for k in range(5):
        box((0.012, 0.012, 0.04), cab + V((-0.08 + k * 0.04, -0.12, -0.065)), "steel", bevel=0)
    for sx in (-1, 1):
        disc(cab + V((sx * 0.1, -0.105, -0.0)), 0.032, 0.02, (0, 1, 0), "bezel")
    eye(cab + V((0, -0.105, 0.01)), r=0.055, depth=0.03)
    at("LampGlow")
    for sx in (-1, 1):
        disc(cab + V((sx * 0.1, -0.115, 0.0)), 0.021, 0.02, (0, 1, 0), "glow")
    at("Drum")
    dc = V((0, 0.33, 1.15))
    disc(dc, 0.22, 0.44, (1, 0, 0), "burlap", segs=32)
    for sx in (-1, 1):
        disc(dc + V((sx * 0.225, 0, 0)), 0.23, 0.03, (1, 0, 0), "gun", segs=32)
        for k in range(6):
            a = 2 * math.pi * k / 6
            box((0.012, 0.03, 0.2), dc + V((sx * 0.243, 0.1 * math.cos(a), 0.1 * math.sin(a))), "steel", bevel=0,
                rot=Matrix.Rotation(-a, 4, "X"))
    for x in (-0.11, 0.11):
        disc(dc + V((x, 0, 0)), 0.226, 0.022, (1, 0, 0), "rope", segs=32)
    for k, (y, z) in enumerate(((0.12, 0.1), (-0.05, 0.2), (0.2, -0.08), (0.0, -0.21))):
        hay(dc + V((0.0, y, z)), (0, y, z), 9, 40 + k, 0.13, spread=0.5)
    for sx in (-1, 1):
        hay(dc + V((sx * 0.24, 0.05, 0.05)), (sx, 0.2, 0.3), 10, 44 + sx, 0.12)
    arms(shoulder_r=0.1)


RAGS = [(-0.27, 0.34), (-0.19, 0.4), (-0.09, 0.3), (0.07, 0.42), (0.17, 0.31), (0.26, 0.38)]


def scarecrow_geometry():
    legs(boot=False)
    at("Body")
    rod(V((-0.14, 0, 0.9)), V((0.14, 0, 0.9)), 0.05, "gun")
    at("Torso")
    rod(V((0, 0, 0.86)), V((0, 0, 1.46)), 0.06, "wood", segs=8)
    for z in (1.0, 1.2, 1.4):
        rod(V((0, 0, z)), V((0, 0, z + 0.03)), 0.068, "steel", segs=16)
    rod(V((-0.32, 0, 1.32)), V((0.32, 0, 1.32)), 0.045, "wood", segs=8)
    for sx in (-1, 1):
        rod(V((sx * 0.04, 0, 1.3)), V((sx * 0.2, 0, 1.33)), 0.05, "steel", segs=12)
        box((0.03, 0.12, 0.12), V((sx * 0.06, 0, 1.32)), "gun", bevel=0.006)
    # a small painted control box strapped to the post, with a second eye-lamp
    box((0.24, 0.16, 0.22), V((0, -0.03, 1.12)), "trim", bevel=0.015)
    box((0.26, 0.02, 0.04), V((0, -0.03, 1.2)), "rope", bevel=0.004)
    box((0.26, 0.02, 0.04), V((0, -0.03, 1.04)), "rope", bevel=0.004)
    disc(V((0, -0.115, 1.12)), 0.042, 0.02, (0, 1, 0), "bezel")
    for i, (x, ln) in enumerate(RAGS):
        at(f"Rag{i}")
        box((0.06, 0.008, ln), V((x, -0.06 if i % 2 else 0.06, 1.3 - ln / 2)), "rag", bevel=0,
            rot=Matrix.Rotation(0.2 * math.sin(i * 3), 4, "Z"))
    for i, (x, ln) in enumerate(RAGS):
        at(f"Rag{i}")
        if i % 2 == 0:
            hay(V((x, 0.06, 1.3 - ln)), (0, 0, -1), 6, 60 + i, 0.1)
    at("Torso")
    for sx in (-1, 1):
        hay(V((sx * 0.3, 0, 1.32)), (sx, 0, -0.3), 10, 70 + sx, 0.13)
    hay(V((0, -0.11, 1.0)), (0, -0.6, -1), 8, 73, 0.12)
    hay(V((0, 0.05, 1.46)), (0, 0.4, 1), 8, 74, 0.1)
    at("Head")
    rod(V((0, 0, 1.46)), V((0, 0, 1.51)), 0.03, "steel")
    disc(V((0, 0, 1.525)), 0.09, 0.04, (0, 0, 1), "trim")
    disc(V((0, 0, 1.725)), 0.09, 0.04, (0, 0, 1), "trim")
    rod(V((0, 0, 1.745)), V((0, 0, 1.83)), 0.1, "paint", r2=0.02, segs=16)
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        rod(V((0.086 * math.cos(a), 0.086 * math.sin(a), 1.53)), V((0.086 * math.cos(a), 0.086 * math.sin(a), 1.72)), 0.007, "steel")
    rod(V((0, 0, 1.83)), V((0, 0, 1.88)), 0.012, "steel")
    sphere(V((0, 0, 1.895)), 0.02, "steel", segs=10)
    at("EyeLens")
    sphere(V((0, 0, 1.625)), 0.062, "glow", scale=(1, 1, 1.25))
    sphere(V((0, -0.125, 1.12)), 0.03, "glow", scale=(1, 0.5, 1))
    arms(armour_fore=False, shoulder_r=0.06)


GEOMETRY = {"field": field_geometry, "baler": baler_geometry, "scarecrow": scarecrow_geometry}


# =====================================================================================================
# Rig
# =====================================================================================================
def pivots():
    c = cfg()
    p = {"Body": (None, mirror(c["hip"], 0) + V((0, 0, 0)))}
    p["Torso"] = ("Body", c["waist"])
    p["Head"] = ("Torso", c["neck"])
    p["EyeLens"] = ("Head", c["neck"])
    for side, sx in SIDES:
        sh, el, wr = arm_points(sx)
        p[f"Arm{side}_Shoulder"] = ("Torso", sh)
        p[f"Arm{side}_Elbow"] = (f"Arm{side}_Shoulder", el)
        p[f"Arm{side}_Wrist"] = (f"Arm{side}_Elbow", wr)
        hip, knee, ankle, sole = leg_points(sx)
        p[f"Leg{side}_Hip"] = ("Body", hip)
        p[f"Leg{side}_Knee"] = (f"Leg{side}_Hip", knee)
        p[f"Leg{side}_Ankle"] = (f"Leg{side}_Knee", ankle)
        p[f"Leg{side}_Foot"] = (f"Leg{side}_Ankle", sole)
    v = VARIANT[0]
    if v == "field":
        p["Antenna"] = ("Head", V((0.06, 0.01, 1.645)))
    elif v == "baler":
        p["LampGlow"] = ("Head", V((0, -0.33, 1.42)))
        p["Drum"] = ("Torso", V((0, 0.33, 1.15)))
    else:
        for i, (x, _) in enumerate(RAGS):
            p[f"Rag{i}"] = ("Torso", V((x, -0.06 if i % 2 else 0.06, 1.3)))
    return p


def materials():
    paint = cfg()["paint"]
    return {
        "paint": lambda: lt.textured("rakehand_paint", sc.battle_paint(211, paint)),
        "trim": lambda: lt.textured("rakehand_trim", sc.battle_paint(217, cfg()["trim"])),
        "gun": lambda: lt.textured("rakehand_gunmetal", lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)),
        "steel": lambda: lt.textured("rakehand_steel", lt.metal_surface(97, "#8d9096", 0.32, 0.9, wear=0.7, edge_light=0.2)),
        "rubber": lambda: lt.textured("rakehand_rubber", lt.rubber_surface(101), normal_strength=2.0),
        "bezel": lambda: lt.textured("rakehand_bezel", lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)),
        "glow": glow_material,
        "denim": lambda: lt.textured("rakehand_denim", sc.cloth_surface(223, "#3d5470")),
        "hazard": lambda: lt.textured("rakehand_hazard", sc.hazard_surface(113)),
        "burlap": lambda: lt.textured("rakehand_burlap", sc.cloth_surface(163, "#8a6a3e")),
        "rope": lambda: lt.textured("rakehand_rope", sc.cloth_surface(167, "#6b5434")),
        "rag": lambda: lt.textured("rakehand_rag", sc.cloth_surface(229, "#5f4e36")),
        "wood": lambda: lt.textured("rakehand_wood", wood_surface(233)),
        "hay": lambda: lt.textured("rakehand_hay", sc.cloth_surface(239, "#d9b85a")),
    }


def wood_surface(seed):
    """Weathered post wood: grain streaks along one axis, grey where the sun bleached it."""
    import numpy as np  # noqa: PLC0415
    size = lt.SIZE
    grain = lt.fnoise(seed, 30.0, aniso=(0.08, 1.0))
    bleach = np.clip(lt.fbm(seed + 1, 3.0) * 1.4 + 0.3, 0, 1)
    base = lt.srgb("#6a4a2c")[None, None, :] * (0.8 + 0.25 * grain[..., None])
    grey = lt.srgb("#8a8478")[None, None, :] * (0.85 + 0.2 * grain[..., None])
    albedo = base * (1 - 0.6 * bleach[..., None]) + grey * 0.6 * bleach[..., None]
    return np.clip(albedo, 0, 1), 0.5 + 0.4 * grain, 0.85 + 0.05 * grain, np.zeros((size, size))


def glow_material():
    """The eye (and lamps): the game sets their colour (green calm, red engaged); the file holds them green."""
    mat = bpy.data.materials.new("rakehand_glow")
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
    makers = materials()
    order = list(makers)
    mats = {}
    root = bpy.data.objects.new("Rakehand", None)
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
        lt.box_uvs(me, [None] * len(me.polygons))
        mesh_ob = bpy.data.objects.new(f"{name}Mesh" if name in ("EyeLens", "LampGlow") else f"{name}_mesh", me)
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
# Poses. Angles in degrees. A leg or arm hangs down, so about X a negative turn swings it forward (-Y); a positive
# knee turn folds the shin back, a negative elbow turn folds the forearm forward; a positive Torso or Body turn
# about X pitches it forward.
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
        self.off[name] += Vector(v)

    def leg(self, side, hip=0.0, knee=0.0, out=0.0, flat=True):
        """hip: forward swing; knee: fold; out: splay sideways. The ankle keeps the sole level when `flat`."""
        sx = -1 if side == "L" else 1
        self.turn(f"Leg{side}_Hip", Y, -sx * out)
        self.turn(f"Leg{side}_Hip", X, -hip)
        self.turn(f"Leg{side}_Knee", X, knee)
        if flat:
            self.turn(f"Leg{side}_Ankle", X, hip - knee)

    def arm(self, side, fwd=0.0, out=0.0, sweep=0.0, elbow=0.0, wrist=0.0):
        """fwd: raise forward; out: raise sideways; sweep: swing a raised arm round to the front; elbow: fold the
        forearm forward; wrist: tip the fork up."""
        sx = -1 if side == "L" else 1
        self.turn(f"Arm{side}_Shoulder", Y, -sx * out)
        self.turn(f"Arm{side}_Shoulder", X, -fwd)
        self.turn(f"Arm{side}_Shoulder", UP, -sx * sweep)
        self.turn(f"Arm{side}_Elbow", X, -elbow)
        self.turn(f"Arm{side}_Wrist", X, -wrist)

    def apply(self):
        piv = pivots()
        for n, ob in self.nodes.items():
            parent, pivot = piv[n]
            ob.rotation_quaternion = self.rot[n]
            ob.location = pivot - (piv[parent][1] if parent else Vector()) + self.off[n]


# Gaits: (clip length s, hip swing degrees, knee fold, arm swing, lean, bob m)
GAITS = {
    "field": {"patrol": (1.0, 22, 38, 16, 2, 0.02), "chase": (0.6, 32, 70, 0, 12, 0.035)},
    "baler": {"patrol": (1.2, 20, 34, 12, 8, 0.025), "chase": (0.75, 28, 58, 0, 16, 0.04)},
    "scarecrow": {"patrol": (0.9, 22, 36, 10, 0, 0.02), "chase": (0.5, 34, 74, 0, 14, 0.03)},
}
SHARED_CLIPS = {"idle": 2.4, "alert": 0.8, "lose_interest": 0.7, "hit": 0.35, "death": 1.6, "jump": 0.35, "fall": 0.6, "land": 0.35}
CLIPS = {}


def use(variant):
    VARIANT[0] = variant
    CLIPS.clear()
    CLIPS.update(SHARED_CLIPS)
    for clip, g in GAITS[variant].items():
        CLIPS[clip] = g[0]
    for clips in VARIANTS[variant]["attacks"].values():
        for clip in clips:
            CLIPS[clip] = ATTACK_CLIPS[clip]


def gait_speed(clip):
    """Ground speed at speed scale 1: each stance sweeps the foot through 2 * swing about the hip."""
    length, swing = GAITS[VARIANT[0]][clip][:2]
    hip, _, _, sole = leg_points(1)
    reach = hip.z - sole.z
    return 2 * reach * math.sin(math.radians(swing)) / (length / 2)


def gait(p, clip, u):
    _, swing, fold, arm_swing, lean, bob = GAITS[VARIANT[0]][clip]
    w = 2 * math.pi * u
    chase = clip == "chase"
    for side, sgn in (("L", 1), ("R", -1)):
        ph = w if side == "L" else w + math.pi
        hip = swing * math.sin(ph)
        knee = fold * max(0.0, math.cos(ph)) ** 1.5 + (6 if chase else 3)
        p.leg(side, hip, knee)
    p.move("Body", (0, 0, -bob * (1 - math.cos(2 * w)) / 2 - (0.05 if chase else 0.0)))
    p.turn("Body", (0, 1, 0), 2.5 * math.sin(w))
    p.turn("Torso", (1, 0, 0), lean)
    p.turn("Torso", UP, 5 * math.sin(w))
    p.turn("Head", (1, 0, 0), -lean * 0.6)
    for side, sgn in (("L", -1), ("R", 1)):
        if chase:
            # forks held up and forward, bouncing with the run
            p.arm(side, fwd=45 + 6 * math.sin(2 * w), out=8, elbow=35, wrist=10)
        else:
            p.arm(side, fwd=arm_swing * math.sin(w) * sgn + 6, elbow=12)


def pose(p, clip, t, length):
    v = VARIANT[0]
    if clip in ("fork_sweep", "haymaker", "spin_sweep"):
        t = strike_early(t, 0.45, 0.2) if length >= 0.9 else t
    u = t / length
    w = 2 * math.pi * u
    if clip == "idle":
        if v == "scarecrow":
            # stock still on its post, arms out like a scarecrow; only a creak in the wind
            for side in "LR":
                p.arm(side, out=82 + 2 * math.sin(w), fwd=4, elbow=6 + 3 * math.sin(w + 1), wrist=-20)
            p.turn("Torso", (0, 1, 0), 1.5 * math.sin(w))
            p.turn("Head", (0, 1, 0), 9 + 2 * math.sin(w))
            p.turn("Head", (1, 0, 0), 14)
            for side in "LR":
                p.leg(side, 0, 2)
        else:
            heavy = 1.6 if v == "baler" else 1.0
            p.move("Body", (0, 0, -0.008 * heavy * (1 - math.cos(w)) / 2))
            p.turn("Torso", (1, 0, 0), (8 if v == "baler" else 0) + 2 * heavy * math.sin(w))
            p.turn("Head", UP, 26 * math.sin(w))
            p.turn("Head", (1, 0, 0), 5 * math.sin(2 * w))
            p.turn("Body", (0, 1, 0), 2 * math.sin(w))
            for side, sgn in (("L", -1), ("R", 1)):
                p.arm(side, fwd=6 + 2 * math.sin(w + sgn), elbow=12, wrist=4 * max(0, math.sin(3 * w + sgn)))
                p.leg(side, 0, 3 + 2 * sgn * math.sin(w))
    elif clip in ("patrol", "chase"):
        gait(p, clip, u)
    elif clip == "alert":
        k = env(t, 0.0, 0.22) - 0.35 * env(t, 0.45, 0.8)
        jolt = pulse(t, 0.0, 0.08, 0.25)
        p.move("Body", (0, 0, -0.04 * k))
        p.turn("Torso", (1, 0, 0), -8 * jolt + (10 if v == "baler" else 4) * k)
        p.turn("Head", (1, 0, 0), -10 * jolt)
        for side in "LR":
            p.arm(side, fwd=50 * k, out=(30 if v == "scarecrow" else 14) * k + (60 * (1 - env(t, 0, 0.2)) if v == "scarecrow" else 0),
                  elbow=40 * k, wrist=20 * k)
            p.leg(side, 8 * k, 16 * k)
    elif clip == "lose_interest":
        k = 1 - env(t, 0.0, 0.7)
        p.turn("Head", UP, 30 * math.sin(math.pi * u))
        p.turn("Torso", (1, 0, 0), (10 if v == "baler" else 4) * k)
        for side in "LR":
            p.arm(side, fwd=34 * k + 6, elbow=26 * k + 12, wrist=10 * k)
            p.leg(side, 0, 10 * k)
    elif clip == "hit":
        k = pulse(t, 0.0, 0.06, 0.35)
        p.move("Body", (0, 0.05 * k, -0.01 * k))
        p.turn("Torso", (1, 0, 0), -12 * k)
        p.turn("Torso", UP, 8 * k)
        p.turn("Head", (1, 0, 0), -16 * k)
        for side in "LR":
            p.arm(side, fwd=-10 * k, out=18 * k, elbow=20 * k)
            p.leg(side, -4 * k, 10 * k)
    elif clip == "death":
        # knees buckle, it folds forward onto its forks and face, then the arms flop out
        buckle = env(t, 0.05, 0.45)
        topple = env(t, 0.35, 0.95)
        flop = env(t, 0.9, 1.4)
        rest_on = {"baler": 0.3}.get(v, 0.12)        # how high its chest lies once down
        p.move("Body", (0, -0.25 * topple, -0.35 * buckle - (v_height() - 0.35 - rest_on) * topple))
        p.turn("Body", (1, 0, 0), 84 * topple)
        p.turn("Torso", (1, 0, 0), 10 * buckle - 6 * topple)
        p.turn("Head", (1, 0, 0), 20 * buckle - 30 * topple)
        p.turn("Head", UP, 18 * flop)
        for side, sx in SIDES:
            p.leg(side, 70 * buckle - 66 * topple, 110 * buckle - 100 * topple, flat=False)
            p.arm(side, fwd=30 * buckle + 120 * topple, out=30 * flop, elbow=20 * buckle - 10 * flop, wrist=-20 * topple)
        for i in range(len(RAGS)):
            p.turn(f"Rag{i}", (1, 0, 0), -60 * topple)
    elif clip == "jump":
        crouch = pulse(t, 0.0, 0.15, 0.3)
        push = env(t, 0.15, 0.35)
        p.move("Body", (0, 0, -0.12 * crouch))
        for side in "LR":
            p.leg(side, 30 * crouch - 10 * push, 60 * crouch - 10 * push)
            p.arm(side, fwd=-20 * crouch + 60 * push, elbow=20)
    elif clip == "fall":
        for side, sgn in (("L", 1), ("R", -1)):
            p.leg(side, 20 + 10 * sgn * math.sin(w * 2), 40 + 12 * math.sin(w * 2 + sgn))
            p.arm(side, fwd=30 + 10 * math.sin(w * 2 + sgn), out=40 + 8 * math.sin(w * 3), elbow=30)
        p.turn("Torso", (1, 0, 0), -6)
    elif clip == "land":
        k = pulse(t, 0.0, 0.08, 0.35)
        p.move("Body", (0, 0, -0.14 * k))
        for side in "LR":
            p.leg(side, 24 * k, 50 * k)
            p.arm(side, fwd=24 * k, out=16 * k, elbow=20 * k)
        p.turn("Torso", (1, 0, 0), 12 * k)
    elif clip == "fork_jab":
        # the right fork draws back and stabs straight ahead; the body steps into it
        draw = pulse(t, 0.0, 0.08, 0.16)
        stab = pulse(t, 0.08, 0.18, 0.55)
        p.arm("R", fwd=-25 * draw + 85 * stab, elbow=70 * draw - 10 * stab, wrist=10 * stab)
        p.arm("L", fwd=20 + 10 * stab, out=10, elbow=30)
        p.turn("Torso", UP, 15 * draw - 25 * stab)
        p.turn("Torso", (1, 0, 0), 10 * stab)
        p.move("Body", (0, -0.12 * stab, -0.04 * stab))
        p.leg("R", 24 * stab, 22 * stab)
        p.leg("L", -12 * stab, 8 * stab)
    elif clip == "fork_sweep":
        # winds the left fork out to the side and back, then sweeps it round in front at waist height
        wind = env(t, 0.0, 0.45) * (1 - env(t, 0.45, 0.6))
        sweep = env(t, 0.45, 0.62) * (1 - env(t, 0.75, 1.0))
        swing_ = -60 * wind + 140 * env(t, 0.45, 0.62)
        p.arm("L", out=75 * (wind + sweep), sweep=swing_ * (1 - env(t, 0.75, 1.0)), elbow=10, wrist=-10 * sweep)
        p.arm("R", fwd=20 + 15 * sweep, out=12, elbow=40)
        p.turn("Torso", UP, -30 * wind + 40 * sweep)
        p.turn("Torso", (1, 0, 0), 8 * sweep)
        p.move("Body", (0, -0.05 * sweep, -0.05 * (wind + sweep)))
        for side in "LR":
            p.leg(side, 8 * (wind + sweep), 16 * (wind + sweep))
    elif clip == "pitch":
        # both hay forks rear back over the shoulders, then drive down and forward together like pitching a bale
        rear = pulse(t, 0.0, 0.12, 0.2)
        drive = pulse(t, 0.12, 0.24, 0.8)
        for side in "LR":
            p.arm(side, fwd=110 * rear + 70 * drive, out=10, elbow=60 * rear - 10 * drive, wrist=-30 * rear + 20 * drive)
            p.leg(side, (24 if side == "R" else -14) * drive, (24 if side == "R" else 8) * drive)
        p.turn("Torso", (1, 0, 0), -14 * rear + 26 * drive)
        p.move("Body", (0, -0.14 * drive, -0.06 * drive))
        p.turn("Head", (1, 0, 0), 10 * rear - 12 * drive)
    elif clip == "haymaker":
        # a huge two-handed sweep: both forks wind off to the right, then it spins its whole top half through the front
        wind = env(t, 0.0, 0.45) * (1 - env(t, 0.45, 0.62))
        hit = env(t, 0.45, 0.62) * (1 - env(t, 0.8, 1.2))
        twist = (55 * env(t, 0.0, 0.45) - 130 * env(t, 0.45, 0.62)) * (1 - env(t, 0.8, 1.2))
        p.turn("Torso", UP, twist)
        p.turn("Torso", (1, 0, 0), 12 * hit)
        for side in "LR":
            p.arm(side, fwd=60 * (wind + hit), out=30 * (wind + hit), elbow=20, wrist=-10 * hit)
            p.leg(side, 10 * (wind + hit), 22 * (wind + hit))
        p.move("Body", (0, -0.04 * hit, -0.07 * (wind + hit)))
    elif clip == "lunge_jab":
        # drops into a crouch and springs at you, both forks thrust together
        crouch = pulse(t, 0.0, 0.07, 0.14)
        lunge = pulse(t, 0.07, 0.16, 0.5)
        p.move("Body", (0, -0.25 * lunge, -0.12 * crouch + 0.03 * lunge))
        p.turn("Torso", (1, 0, 0), 25 * crouch + 20 * lunge)
        for side in "LR":
            p.arm(side, fwd=-10 * crouch + 90 * lunge, out=20 * crouch, elbow=60 * crouch, wrist=10 * lunge)
        p.leg("R", 40 * lunge + 25 * crouch, 30 * lunge + 50 * crouch)
        p.leg("L", -20 * lunge + 25 * crouch, 10 * lunge + 50 * crouch)
        p.turn("Head", (1, 0, 0), -20 * lunge)
    elif clip == "spin_sweep":
        # arms snap out like a scarecrow and the whole post spins a full turn on its hips, forks slashing round
        out = env(t, 0.0, 0.12) * (1 - env(t, 0.8, 1.0))
        spin_ = 360 * env(t, 0.12, 0.62)
        p.turn("Torso", UP, -spin_)
        for side in "LR":
            p.arm(side, out=85 * out, elbow=6 * out, wrist=-15 * out)
            p.leg(side, 0, 20 * out)
        p.move("Body", (0, 0, -0.06 * out))
        p.turn("Head", (0, 1, 0), 20 * out)
    else:
        raise ValueError(clip)
    gear(p, clip, t, length)


def v_height():
    return cfg()["hip"].z


def gear(p, clip, t, length):
    """Each variant's loose pieces, on top of the clip."""
    u = t / length
    w = 2 * math.pi * u
    v = VARIANT[0]
    if v == "field":
        swing = {"patrol": 8 * math.sin(2 * w), "chase": 14 * math.sin(2 * w), "hit": 25 * pulse(t, 0, 0.05, 0.35),
                 "idle": 3 * math.sin(w), "alert": 18 * pulse(t, 0, 0.1, 0.5), "fork_jab": 14 * pulse(t, 0.1, 0.2, 0.6)}.get(clip, 0)
        p.turn("Antenna", (1, 0, 0), swing)
    elif v == "baler":
        # the bale drum rolls over as it walks (whole turns, so loops still loop)
        turns = {"patrol": 1, "chase": 1, "idle": 0, "haymaker": 1}.get(clip, 0)
        p.turn("Drum", (1, 0, 0), -360 * turns * u)
    else:
        for i in range(len(RAGS)):
            flap = {"patrol": 8 * math.sin(w * 2 + i), "chase": -25 + 10 * math.sin(w * 4 + i), "idle": 6 * math.sin(w * 2 + i * 1.3),
                    "spin_sweep": 50 * env(t, 0.1, 0.4) * (1 - env(t, 0.6, 1.0)), "lunge_jab": -30 * pulse(t, 0.05, 0.16, 0.5),
                    "hit": 20 * pulse(t, 0, 0.06, 0.35)}.get(clip, 0)
            p.turn(f"Rag{i}", (1, 0, 0), flap)
            if clip == "spin_sweep":
                x = RAGS[i][0]
                p.turn(f"Rag{i}", (0, 1, 0), (1 if x > 0 else -1) * 40 * env(t, 0.1, 0.4) * (1 - env(t, 0.6, 1.0)))


def set_pose(nodes, clip, t, length):
    p = Pose(nodes)
    pose(p, clip, t, length)
    # A hair of motion (a quarter degree) on every node, so no channel holds still through a clip.
    for i, n in enumerate(nodes):
        p.turn(n, (1, 0, 0), 0.25 * math.sin(2 * math.pi * t / length) * (1 if i % 2 else -1))
    p.apply()


def bake_clips(nodes):
    """Keys every rig node every frame of every clip (see build_scuttler.bake_clips)."""
    import numpy as np  # noqa: PLC0415
    anim = [ob for n, ob in nodes.items() if not n.endswith("_Foot") and n not in ("EyeLens", "LampGlow")]
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
        speeds = ", ".join(f"{clip} {gait_speed(clip):.2f}" for clip in GAITS[variant])
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
    c = (0, 0, 0.92)
    shots = []

    def shot(name, clip, t, cam):
        if clip is None:
            Pose(nodes).apply()
        else:
            set_pose(nodes, clip, t, CLIPS[clip])
        bpy.context.view_layer.update()
        path = rl.render(cam, out / f"{variant}_{name}.png", 800, 900)
        shots.append((name, path))

    tq = rl.camera("tq", (-3.0, -4.2, 2.2), c, ortho=2.3)
    front = rl.camera("front", (0, -5, 1.0), c, ortho=2.2)
    side = rl.camera("side", (5, 0, 1.0), c, ortho=2.2)
    shot("rest", None, 0, tq)
    shot("front", None, 0, front)
    shot("side", None, 0, side)
    shot("rear", None, 0, rl.camera("rear", (3.2, 3.8, 2.0), c, ortho=2.3))
    shot("idle", "idle", 0.6, tq)
    shot("patrol", "patrol", CLIPS["patrol"] * 0.25, side)
    shot("chase", "chase", CLIPS["chase"] * 0.25, tq)
    shot("alert", "alert", 0.3, tq)
    poke, swing = VARIANTS[variant]["attacks"]["poke"][0], VARIANTS[variant]["attacks"]["swing"][0]
    shot(poke + "_wind", poke, 0.08, side)
    shot(poke, poke, 0.18, tq)
    shot(swing + "_wind", swing, 0.15, tq)
    shot(swing, swing, 0.25, rl.camera("top", (0.01, -0.6, 5), c, ortho=2.6))
    shot("hit", "hit", 0.06, tq)
    shot("death_fall", "death", 0.6, side)
    shot("death", "death", 1.6, tq)

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
