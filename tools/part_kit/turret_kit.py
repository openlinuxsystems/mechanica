"""The Turret Seat of the parts map's batch 17 (row 143, engine item E8), at its five levels (see part_kit.py).

``build_turret_seat(mats, level)`` - a Salvager Tech gunner's seat, 4 x 6 x 4 blocks (1 x 1.5 x 1 m): a vehicle seat,
not an office chair. Three stages, which the game turns separately (the part's aimed socket ``turret``):

* ``Base`` (fixed to the Creation): a framed mounting plate and on it a low, wide armoured ring, the turret's lower
  bearing race (0.38 to 0.48 m round): no stem, no post, the rider's feet sit down inside the ring;
* ``turretYaw`` (turns about the upright through the hips): the upper race with its gear teeth and two yoke cheeks
  rising from it to the trunnion bosses at hip height;
* ``turretPitch`` (tilts about the trunnions, under ``turretYaw``): the cradle hung on the trunnions, the moulded
  bucket shell of the batch 4 seats on its seat rails (bolsters, reclined back, belts), a footrest, the gunner's
  spade grips on two side arms, and a roll hoop over the rider's head carrying the top hardpoint plate, where what
  is built on the turret sits (the top face of the part).

Both turning nodes sit at the pivot (the cushion point, 0.28 m below the part's centre) unrotated, as the game wants
(``<socket>Yaw`` and ``<socket>Pitch``). The seat clips (``K.bake_seat_clips``) move ``Shell`` and sink ``Cushion`` and
``Backrest`` inside the pitch stage.

Levels (Chase's rule: each level looks better), the seats' finishes (part_kit.FINISH):

1. worn gunmetal and hull dark, charcoal fabric, plain cheeks, a lap belt, one copper pip;
2. teal piping, taller bolsters, lightening holes in the cheeks, rubber grip sleeves, a copper race line, two pips;
3. steel frame, quilting, a four-point harness, cheek braces, a gunner's sight on the grips (lens lit cyan), three pips;
4. armour plates on the cheeks and hoop, copper collars, a five-point harness, a cyan running light round the ring;
5. black chrome and polished copper, black quilted fabric stitched in cyan, a lamp on the hoop, cyan under-glow.

Coordinates are Blender's (x right, y forward, z up), the part's centre at the origin; the part data's ``seat`` and
``joint.pivot`` use the game's (x, z up, -y).
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector

import part_kit as K
import seat_kit as S
from part_kit import Geo, box, cylinder, lathe, ring, sweep

HALF = (0.5, 0.5, 0.75)          # half size (m): 4 x 4 blocks across, 6 blocks tall
PIVOT_Z = -0.28                  # the pivot: the cushion point, hips centred over the ring
HIPS_Y = 0.0
FEET_Z = PIVOT_Z - S.CUSHION_H   # the footrest's top: a vehicle seat's cushion height above the feet
SHELL_TOP = PIVOT_Z + 0.53       # the top of the shell's headrest (the batch 4 seats' height above the cushion)
BOTTOM = -HALF[2]
PLATE_TOP = BOTTOM + 0.02        # the mounting plate's top
RACE_IN, RACE_OUT = 0.38, 0.48   # the ring's bearing race
RACE_LOW_TOP = BOTTOM + 0.09     # the fixed race's top
RACE_TOP = RACE_LOW_TOP + 0.03   # the turning race's top
CHEEK_X = 0.43                   # the yoke cheeks' middles
BEAM_X, BEAM_Z = 0.30, PIVOT_Z - 0.19     # the cradle's side beams
RAIL_TOP = PIVOT_Z - 0.13        # the seat rails' top (the batch 4 seats' rails, under the shell's brackets)
HOOP_Y = -0.35                   # the roll hoop's posts, behind the reclined back
MOUNT_Z = HALF[2] - 0.05         # the hardpoint plate's underside (its top is the part's top face)
GRIP_HUB = (HIPS_Y + 0.338, PIVOT_Z + 0.324)    # where the "wheel" hands go (seat_kit.HUB_FROM_HIPS from the hips)
GRIP_X = 0.17


def prism_x(g, profile, x0, x1, mat):
    """A plate: the convex (y, z) `profile` (counter-clockwise seen from +x) extruded from x0 to x1, wound outward."""
    n = len(profile)
    verts = [(x0, y, z) for y, z in profile] + [(x1, y, z) for y, z in profile]
    faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    for k in range(n):
        k1 = (k + 1) % n
        faces.append((k, k1, n + k1, n + k))
    g.add(verts, faces, mat)


def _local(g, origin):
    """`g` moved so `origin` (absolute) is its zero: geometry for a node placed at `origin`."""
    out = Geo()
    out.extend(g, Matrix.Translation(-Vector(origin)))
    return out


# =====================================================================================================
# Base: the mounting plate and the fixed race
# =====================================================================================================

def base(g, level, f):
    hx = HALF[0] - 0.02
    # the mounting plate: a dark plate in a gunmetal frame, chamfered corners, bolted down
    S.cbox(g, (0, 0, BOTTOM + 0.009), (hx - 0.012, hx - 0.012, 0.009), f["dark"], bevel=0.02)
    for sx in (-1, 1):
        S.cbox(g, (sx * (hx - 0.014), 0, BOTTOM + 0.011), (0.014, hx - 0.05, 0.011), f["frame"], bevel=0.004)
        S.cbox(g, (0, sx * (hx - 0.014), BOTTOM + 0.011), (hx - 0.05, 0.014, 0.011), f["frame"], bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = Vector((sx * (hx - 0.045), sy * (hx - 0.045), PLATE_TOP))
            S.cbox(g, tuple(c - Vector((0, 0, 0.008))), (0.04, 0.04, 0.01), f["frame"], bevel=0.008)
            S.bolt(g, tuple(c + Vector((0, 0, 0.002))), (0, 0, 1), 0.009, f["bolt"])
    # the fixed race: a low, wide armoured ring with a lip, its armour in segments, bolted round
    lathe(g, [(RACE_IN, PLATE_TOP - 0.002), (RACE_OUT, PLATE_TOP - 0.002), (RACE_OUT, RACE_LOW_TOP - 0.012),
              (RACE_OUT - 0.012, RACE_LOW_TOP), (RACE_IN + 0.01, RACE_LOW_TOP), (RACE_IN, RACE_LOW_TOP - 0.01)],
          f["frame"], steps=64)
    n = 12
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n
        d = Vector((math.cos(a), math.sin(a), 0))
        t = Vector((-d.y, d.x, 0))
        rot = Matrix((t, d, (0, 0, 1))).transposed()
        mid = (PLATE_TOP + RACE_LOW_TOP) / 2 - 0.004
        S.cbox(g, tuple(d * (RACE_OUT + 0.008) + Vector((0, 0, mid))), (0.105, 0.01, 0.026), f["dark"], bevel=0.005, rot=rot)
        for s in (-1, 1):
            S.bolt(g, tuple(d * (RACE_OUT + 0.018) + t * s * 0.08 + Vector((0, 0, mid))), tuple(d), 0.0065, f["bolt"])
    # the hole's floor and its drain slots
    S.cbox(g, (0, 0, PLATE_TOP - 0.001), (0.2, 0.2, 0.001), "hole", bevel=0.0)
    if level >= 3:
        ring(g, RACE_LOW_TOP - 0.03, RACE_LOW_TOP - 0.022, RACE_OUT + 0.0, RACE_OUT + 0.004, f["trim"], segs=64)
    # pips on the plate's front edge, the link box on its front left corner
    K.pips(g, level, (-0.04 * (level - 1) / 2, hx + 0.0005, BOTTOM + 0.011), (0.04, 0, 0), (0, 1, 0), f)
    K.link_box(g, (-(hx - 0.1), hx - 0.1, PLATE_TOP), (0, 1, 0), f)


# =====================================================================================================
# The yaw stage: upper race and yoke cheeks
# =====================================================================================================

def yaw_stage(g, level, f):
    race = "chrome" if level >= 5 else "steel"
    lathe(g, [(RACE_IN, RACE_LOW_TOP + 0.001), (RACE_OUT - 0.008, RACE_LOW_TOP + 0.001), (RACE_OUT - 0.008, RACE_TOP - 0.006),
              (RACE_OUT - 0.014, RACE_TOP), (RACE_IN + 0.006, RACE_TOP), (RACE_IN, RACE_TOP - 0.006)],
          f["dark"] if level < 5 else "black_chrome", steps=64)
    # the race's bright running face and its gear teeth round the inside
    ring(g, RACE_LOW_TOP - 0.004, RACE_LOW_TOP + 0.004, RACE_OUT - 0.012, RACE_OUT - 0.004, race, segs=64)
    for k in range(40):
        a = 2 * math.pi * k / 40
        d = Vector((math.cos(a), math.sin(a), 0))
        rot = Matrix((Vector((-d.y, d.x, 0)), d, (0, 0, 1))).transposed()
        S.cbox(g, tuple(d * (RACE_IN - 0.006) + Vector((0, 0, RACE_TOP - 0.016))), (0.012, 0.007, 0.012), f["frame"], rot=rot)
    if level >= 2:
        ring(g, RACE_TOP - 0.001, RACE_TOP + 0.001, RACE_IN + 0.03, RACE_IN + 0.036, f["trim"], segs=64)
    # the yoke cheeks: tapered plates from the race to the trunnion bosses
    t = 0.0125
    for sx in (-1, 1):
        x = sx * CHEEK_X
        z0, z1 = RACE_TOP, PIVOT_Z + 0.065
        prism_x(g, [(-0.2, z0), (0.2, z0), (0.075, z1), (-0.075, z1)], x - t, x + t, f["frame"])
        # a foot flange bolted to the race
        S.cbox(g, (x, 0, RACE_TOP + 0.008), (0.035, 0.21, 0.008), f["frame"], bevel=0.004)
        for y in (-0.16, -0.06, 0.06, 0.16):
            S.bolt(g, (x + sx * 0.022, y, RACE_TOP + 0.016), (0, 0, 1), 0.0065, f["bolt"])
        # the trunnion boss and its cap
        cylinder(g, (x - sx * 0.025, 0, PIVOT_Z), (x + sx * 0.03, 0, PIVOT_Z), 0.055, f["frame"], segs=24)
        cylinder(g, (x + sx * 0.03, 0, PIVOT_Z), (x + sx * 0.036, 0, PIVOT_Z), 0.042, f["trim"], segs=24)
        for k in range(6):
            a = 2 * math.pi * k / 6
            S.bolt(g, (x + sx * 0.036, 0.03 * math.cos(a), PIVOT_Z + 0.03 * math.sin(a)), (sx, 0, 0), 0.005, f["bolt"])
        if level >= 2:
            # lightening holes down the cheek
            for zc, r in ((RACE_TOP + 0.09, 0.045), (RACE_TOP + 0.2, 0.035)):
                cylinder(g, (x - t - 0.001, 0, zc), (x + t + 0.001, 0, zc), r, "hole", segs=16)
        if level >= 3:
            # a brace from the race up the cheek's outside
            for sy in (-1, 1):
                cylinder(g, (x + sx * 0.02, sy * 0.19, RACE_TOP + 0.01), (x + sx * 0.02, sy * 0.06, PIVOT_Z - 0.03), 0.011, f["frame"], segs=10)
        if level >= 4:
            # an armour plate on the cheek's outside, copper bolted
            S.cbox(g, (x + sx * (t + 0.006), 0, (z0 + z1) / 2 - 0.03), (0.005, 0.13, 0.09), f["dark"], bevel=0.004)
            for sy in (-1, 1):
                for sz in (-1, 1):
                    S.bolt(g, (x + sx * (t + 0.012), sy * 0.105, (z0 + z1) / 2 - 0.03 + sz * 0.07), (sx, 0, 0), 0.006, f["trim"])


# =====================================================================================================
# The pitch stage: cradle, footrest, grips, roll hoop and hardpoint plate
# =====================================================================================================

def cradle(g, level, f):
    tube = 0.019
    y0, y1 = HOOP_Y, 0.36
    for sx in (-1, 1):
        x = sx * BEAM_X
        # the side beam and the trunnion upright from it to the axle
        S.cbox(g, (x, (y0 + y1) / 2, BEAM_Z), (tube, (y1 - y0) / 2, tube), f["frame"], bevel=0.005)
        S.cbox(g, (x, 0, (BEAM_Z + PIVOT_Z) / 2), (0.012, 0.06, (PIVOT_Z - BEAM_Z) / 2 + 0.03), f["frame"], bevel=0.006)
        cylinder(g, (x, 0, PIVOT_Z), (sx * (CHEEK_X - 0.026), 0, PIVOT_Z), 0.03, "chrome" if level >= 5 else "steel", segs=18)
        cylinder(g, (x - sx * 0.01, 0, PIVOT_Z), (x + sx * 0.02, 0, PIVOT_Z), 0.045, f["trim"], segs=20)
    for yy, w in ((y1, BEAM_X), (0.16, BEAM_X), (-0.12, BEAM_X), (y0, BEAM_X)):
        S.cbox(g, (0, yy, BEAM_Z), (w + tube, tube * 0.9, tube * 0.9), f["frame"], bevel=0.005)
    # the seat rails on the cross tubes (the batch 4 seats' rails)
    slider = "chrome" if level >= 5 else "steel"
    ry0, ry1 = HIPS_Y - 0.17, HIPS_Y + 0.27
    rz = RAIL_TOP - 0.04
    for sx in (-1, 1):
        x = sx * S.RAIL_X
        S.cbox(g, (x, (ry0 + ry1) / 2, rz + 0.011), (0.022, (ry1 - ry0) / 2, 0.011), f["frame"], bevel=0.003)
        S.cbox(g, (x, (ry0 + ry1) / 2 + 0.015, rz + 0.031), (0.014, (ry1 - ry0) / 2 - 0.035, 0.009), slider, bevel=0.002)
        for yy in (0.16, -0.12):
            S.cbox(g, (x, yy, (BEAM_Z + rz) / 2 + 0.01), (0.014, 0.016, (rz - BEAM_Z) / 2 + 0.01), f["frame"], bevel=0.003)
    # the footrest: a ribbed plate on two struts from the front cross tube
    fy0, fy1 = 0.16, 0.33
    S.cbox(g, (0, (fy0 + fy1) / 2, FEET_Z - 0.008), (0.15, (fy1 - fy0) / 2, 0.008), f["frame"], bevel=0.004)
    for k in range(5):
        S.cbox(g, (0, fy0 + 0.025 + k * (fy1 - fy0 - 0.05) / 4, FEET_Z + 0.0015), (0.13, 0.006, 0.0015),
               "rubber" if level >= 2 else f["dark"])
    for sx in (-1, 1):
        cylinder(g, (sx * BEAM_X, y1 - 0.01, BEAM_Z), (sx * 0.13, fy1 - 0.01, FEET_Z - 0.01), 0.012, f["frame"], segs=10)
        cylinder(g, (sx * BEAM_X, 0.16, BEAM_Z), (sx * 0.13, fy0 + 0.01, FEET_Z - 0.01), 0.01, f["frame"], segs=10)
    grips(g, level, f)
    hoop(g, level, f)


def grips(g, level, f):
    """The gunner's spade grips on a cross bar, carried by two side arms from the cradle's beams (no column)."""
    gy, gz = GRIP_HUB
    bar_z = gz - 0.05
    arm = "chrome" if level >= 5 else f["frame"]
    for sx in (-1, 1):
        pts = [Vector((sx * BEAM_X, 0.3, BEAM_Z + 0.01)), Vector((sx * BEAM_X, 0.38, bar_z - 0.1)),
               Vector((sx * 0.26, gy + 0.02, bar_z)), Vector((sx * (GRIP_X + 0.04), gy, bar_z))]
        sweep(g, S.bend(pts, 0.06, steps=4), 0.012, arm, segs=10)
    cylinder(g, (-GRIP_X - 0.05, gy, bar_z), (GRIP_X + 0.05, gy, bar_z), 0.014, f["frame"], segs=12)
    for sx in (-1, 1):
        x = sx * GRIP_X
        # a spade grip standing up off the bar, rubber sleeved, a trigger guard in front
        cylinder(g, (x, gy, bar_z), (x, gy - 0.01, gz + 0.06), 0.017, "rubber", segs=14)
        cylinder(g, (x, gy - 0.01, gz + 0.06), (x, gy - 0.012, gz + 0.075), 0.02, f["trim"], segs=14)
        if level >= 2:
            for z in (gz - 0.01, gz + 0.03):
                cylinder(g, (x, gy - 0.004, z - 0.004), (x, gy - 0.005, z + 0.004), 0.0195, "webbing" if level < 5 else "fabric_quilt", segs=14)
        sweep(g, [Vector((x, gy + 0.005, bar_z + 0.01)), Vector((x, gy + 0.04, gz - 0.005)), Vector((x, gy + 0.01, gz + 0.04))],
              0.004, f["frame"], segs=6)
    # the firing box between the grips
    S.cbox(g, (0, gy + 0.005, bar_z + 0.022), (0.05, 0.025, 0.02), f["dark"], bevel=0.006)
    S.cbox(g, (0, gy - 0.021, bar_z + 0.024), (0.04, 0.0015, 0.012), "screen")
    if level >= 3:
        # the gunner's sight on a post at the bar's middle; its lens is lit (powered)
        cylinder(g, (0, gy + 0.01, bar_z + 0.042), (0, gy + 0.01, gz + 0.1), 0.008, f["frame"], segs=8)
        S.cbox(g, (0, gy + 0.01, gz + 0.115), (0.022, 0.035, 0.018), f["frame"], bevel=0.005)


def hoop(g, level, f):
    """The roll hoop behind the shell, over the rider's head, carrying the hardpoint plate on top."""
    r = 0.02
    top = MOUNT_Z - r
    for sx in (-1, 1):
        x = sx * BEAM_X
        pts = [Vector((x, HOOP_Y, BEAM_Z)), Vector((x, HOOP_Y, top - 0.12)), Vector((x, HOOP_Y + 0.1, top)),
               Vector((x, 0.22, top))]
        sweep(g, S.bend(pts, 0.1, steps=6), r, f["frame"], segs=10)
        # a brace from the beam's front up to the hoop's knee
        cylinder(g, (x, 0.12, BEAM_Z + 0.01), (x, HOOP_Y + 0.03, top - 0.16), 0.011, f["frame"], segs=8)
        if level >= 4:
            for z in (BEAM_Z + 0.06, top - 0.16):
                cylinder(g, (x, HOOP_Y, z - 0.02), (x, HOOP_Y, z + 0.02), r + 0.008, f["trim"], segs=12)
    for yy in (HOOP_Y + 0.08, 0.2):
        cylinder(g, (-BEAM_X, yy, top), (BEAM_X, yy, top), r * 0.85, f["frame"], segs=10)
    # the hardpoint plate: the top face of the part, a bolt grid for what is built on it
    hp = 0.245
    zc = (MOUNT_Z + HALF[2]) / 2
    S.cbox(g, (0, 0, zc), (hp, hp, (HALF[2] - MOUNT_Z) / 2 - 0.001), f["dark"], bevel=0.008)
    S.cbox(g, (0, 0, MOUNT_Z + 0.006), (hp + 0.006, hp + 0.006, 0.006), f["frame"], bevel=0.003)
    for i in range(-1, 2):
        for j in range(-1, 2):
            p = (i * 0.125, j * 0.125, HALF[2] - 0.001)
            cylinder(g, Vector(p) - Vector((0, 0, 0.004)), Vector(p) + Vector((0, 0, 0.0005)), 0.011, "hole", segs=10)
            S.bolt(g, Vector(p) + Vector((0, 0, -0.0035)), (0, 0, 1), 0.0075, f["trim"] if (i + j) % 2 == 0 else f["bolt"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder(g, (sx * 0.2, sy * 0.2, MOUNT_Z + 0.002), (sx * BEAM_X, sy * 0.2 if sy > 0 else HOOP_Y + 0.08, top), 0.01, f["frame"], segs=8)
    if level >= 4:
        # armour cheeks on the hoop beside the rider's head
        for sx in (-1, 1):
            S.cbox(g, (sx * (BEAM_X + 0.022), HOOP_Y + 0.07, top - 0.2), (0.004, 0.06, 0.12), f["dark"], bevel=0.003)
    if level >= 5:
        S.cbox(g, (0, HOOP_Y + 0.08, top - 0.03), (0.06, 0.012, 0.016), f["frame"], bevel=0.004)


# =====================================================================================================
# The bucket shell (the batch 4 seats' shell, cushion, backrest and belts, at this seat's heights)
# =====================================================================================================

def shell_parts(mats, level, f, parent, origin):
    """The moulded shell with its brackets onto the rails, the cushion, the backrest and the belts, built as the batch 4
    seats' (seat_kit.build_seat) under `parent`, a node at the absolute point `origin`. Returns the rig entries."""
    rig = {}
    H, C = HIPS_Y, PIVOT_Z
    saved = S.SHELL_TOP
    S.SHELL_TOP = SHELL_TOP
    try:
        path = S.seat_path(H, C)
        idx = range(len(path))
        secs = [S.shell_sec(p, H, level) for p in path]
    finally:
        S.SHELL_TOP = saved
    pan = [i for i in idx if path[i]["y"] > H - 0.12 and path[i]["h"] < -0.035]
    back = [i for i in idx if path[i]["y"] < H - 0.15 and -0.02 < path[i]["h"] < 0.3]
    head = [i for i in idx if path[i]["h"] > 0.355 and path[i]["p"].z < SHELL_TOP - 0.03]
    shell_at = Vector((0, 0, RAIL_TOP)) - Vector(origin)
    shell = rig["shell"] = K.make_empty("Shell", parent, tuple(shell_at))
    rig["shell_at"] = tuple(shell_at)
    s = Geo()
    S.bucket_loft(s, path, secs, f["dark"], ring=20, power=4.0)
    for yy in (H + 0.18, H - 0.07):
        i = S.nearest(path, pan, y=yy)
        for sx in (-1, 1):
            under = S.surface(path[i], secs[i], sx * S.RAIL_X, top=False)
            zt = under.z + 0.006
            S.cbox(s, (sx * S.RAIL_X, path[i]["y"], (RAIL_TOP + zt) / 2), (0.016, 0.026, (zt - RAIL_TOP) / 2), f["frame"], bevel=0.004)
    i_anchor = S.nearest(path, pan, y=H - 0.1)
    for sx in (-1, 1):
        hw = secs[i_anchor][0]
        p = path[i_anchor]["p"] + Vector((sx * (hw - 0.012), 0, 0)) + path[i_anchor]["n"] * (secs[i_anchor][3] * 0.75)
        S.bolt(s, p + Vector((sx * 0.004, 0, 0)), (sx, 0, 0), 0.009, f["bolt"])
    x_axis = Vector((1, 0, 0))
    if level >= 3:
        i_slot = S.nearest(path, idx, h=0.335)
        pt = path[i_slot]
        tan = (path[i_slot + 1]["p"] - path[i_slot - 1]["p"]).normalized()
        rot = Matrix((x_axis, -tan, pt["n"])).transposed()
        for sx in (-1, 1):
            S.cbox(s, tuple(S.surface(pt, secs[i_slot], sx * 0.075, lift=-0.004)), (0.026, 0.007, 0.006), "hole", rot=rot)
        i = S.nearest(path, back, h=0.12)
        for sx in (-1, 1):
            p = S.surface(path[i], secs[i], sx * secs[i][0] * 0.97, top=False)
            S.bolt(s, p + Vector((sx * 0.004, 0, 0)), (sx, 0, 0), 0.0065, f["bolt"])
    K.make_object("ShellFrame", _local(s, (0, 0, RAIL_TOP)), mats, shell)

    piping = "piping" if level < 5 else "cyan_glow"
    seam = "hole"
    # the cushion: the pan pad, bolsters, piping, quilting and the lap belt or the harness's lower straps
    i_h = S.nearest(path, pan, y=H)
    pad_t = C - path[i_h]["p"].z
    bol = 0.032 if level >= 2 else 0.012
    pad_secs = {i: (secs[i][0] * 0.8, -0.012, pad_t - (0.014 if i == pan[0] else 0.0), bol, 0.0) for i in pan}
    c = Geo()
    S.bucket_loft(c, [path[i] for i in pan], [pad_secs[i] for i in pan], f["fabric"], ring=16, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            sweep(c, [S.surface(path[i], pad_secs[i], sx * pad_secs[i][0] * 0.62, lift=0.003) for i in pan[1:]], 0.0045, piping, segs=5)
    if level >= 3:
        for yy in (H - 0.06, H + 0.06, H + 0.17):
            i = S.nearest(path, pan, y=yy)
            w = pad_secs[i][0] * 0.55
            sweep(c, [S.surface(path[i], pad_secs[i], u, lift=0.0005) for u in (-w, -w / 2, 0, w / 2, w)], 0.0025, seam, segs=4)
    up = Vector((0, 0, 1))
    for sx in (-1, 1):
        pts = []
        for u in (0.2, 0.16, 0.12, 0.08, 0.045, 0.02):
            yy = H - 0.10 + (0.2 - u) * 0.55
            i = S.nearest(path, pan, y=yy)
            pt = dict(path[i])
            pt["p"] = Vector((0, yy, pt["p"].z))
            pts.append(S.surface(pt, pad_secs[i], sx * u, lift=0.004))
        S.strap(c, pts, up)
    i_b = S.nearest(path, pan, y=H)
    buckle = S.surface(path[i_b], pad_secs[i_b], 0.0, lift=0.006)
    if level >= 3:
        for sx in (-1, 1):
            pts = []
            for yy, u in ((H - 0.13, 0.085), (H - 0.08, 0.07), (H - 0.03, 0.045)):
                i = S.nearest(path, pan, y=yy)
                pts.append(S.surface(path[i], pad_secs[i], sx * u, lift=0.0045))
            S.strap(c, pts + [buckle], up)
        if level >= 4:
            pts = [S.surface(path[S.nearest(path, pan, y=yy)], pad_secs[S.nearest(path, pan, y=yy)], 0.0, lift=0.0045)
                   for yy in (H + 0.24, H + 0.17, H + 0.1, H + 0.04)]
            S.strap(c, pts, up)
        cylinder(c, buckle - up * 0.003, buckle + up * 0.006, 0.033, "chrome" if level >= 5 else f["trim"], segs=14)
        cylinder(c, buckle + up * 0.006, buckle + up * 0.0085, 0.019, f["dark"], segs=10)
    else:
        S.cbox(c, tuple(buckle), (0.032, 0.02, 0.007), "steel" if level == 1 else f["trim"], bevel=0.003)
    pan_bottom = min(v[2] for v in c.v)
    rig["cushion"] = K.make_object("Cushion", _local(c, (0, 0, pan_bottom)), mats, shell, (0, 0, pan_bottom - RAIL_TOP))

    # the backrest: the back pad, the headrest pad, piping, quilting and the shoulder straps
    r = math.radians(S.RECLINE)
    bup = Vector((0, -math.sin(r), math.cos(r)))
    fwd = Vector((0, math.cos(r), math.sin(r)))
    origin_b = path[back[0]]["p"].copy()
    m = Matrix((Vector((-1, 0, 0)), bup, fwd)).transposed()
    back_secs = {}
    for i in back:
        h = path[i]["h"]
        t = 0.05 + 0.028 * math.sin(math.pi * min(max(h / 0.3, 0), 1)) - (0.012 if i == back[-1] else 0.0)
        back_secs[i] = (secs[i][0] * 0.8, -0.012, t, 0.035 if level >= 2 else 0.014, 0.0)
    bk = Geo()
    S.bucket_loft(bk, [path[i] for i in back], [back_secs[i] for i in back], f["fabric"], ring=16, power=4.0)
    head_secs = {i: (0.105, -0.01, 0.04, 0.012 if level >= 3 else 0.0, 0.0) for i in head}
    if len(head) >= 2:
        S.bucket_loft(bk, [path[i] for i in head], [head_secs[i] for i in head], f["fabric"], ring=16, power=4.0)
    if level >= 2:
        for sx in (-1, 1):
            sweep(bk, [S.surface(path[i], back_secs[i], sx * back_secs[i][0] * 0.62, lift=0.003) for i in back], 0.0045, piping, segs=5)
    if level >= 3:
        for h in (0.08, 0.15, 0.22):
            i = S.nearest(path, back, h=h)
            w = back_secs[i][0] * 0.55
            sweep(bk, [S.surface(path[i], back_secs[i], u, lift=0.0005) for u in (-w, -w / 2, 0, w / 2, w)], 0.0025, seam, segs=4)
        i_slot = S.nearest(path, idx, h=0.335)
        for sx in (-1, 1):
            pts = [S.surface(path[i_slot], secs[i_slot], sx * 0.075, lift=0.002)]
            for h, u in ((0.28, 0.08), (0.2, 0.085), (0.12, 0.09), (0.04, 0.09), (0.0, 0.09)):
                i = S.nearest(path, back, h=h)
                pts.append(S.surface(path[i], back_secs[i], sx * u, lift=0.0045))
            S.strap(bk, pts, fwd)
            if level >= 4:
                i = S.nearest(path, back, h=0.16)
                p = S.surface(path[i], back_secs[i], sx * 0.087, lift=0.008)
                S.cbox(bk, tuple(p), (0.024, 0.008, 0.004), f["trim"], bevel=0.002, rot=m)
    to_back = (Matrix.Translation(origin_b) @ m.to_4x4()).inverted()
    bl_geo = Geo()
    bl_geo.extend(bk, to_back)
    ob = K.make_object("Backrest", bl_geo, mats, shell, tuple(origin_b - Vector((0, 0, RAIL_TOP))))
    ob.rotation_mode = "XYZ"
    ob.rotation_euler = m.to_euler()
    rig["back"] = ob
    rig["back_axis"] = 2
    rig["pan_sink"], rig["back_sink"] = 0.16, 0.1
    return rig


# =====================================================================================================
# The part
# =====================================================================================================

def build_turret_seat(mats, level):
    f = K.FINISH[level]
    root = K.make_empty("TurretSeat")
    rig = {"root": root}
    pivot = (0, HIPS_Y, PIVOT_Z)

    g = Geo()
    base(g, level, f)
    K.make_object("Base", g, mats, root)
    if level >= 4:
        lg = Geo()
        ring(lg, PLATE_TOP + 0.012, PLATE_TOP + 0.016, RACE_OUT + 0.017, RACE_OUT + 0.02, "cyan_glow", segs=64)
        K.make_object("RunningLight", lg, mats, root)
    if level >= 5:
        for sx in (-1, 1):
            S.running_light(root, mats, f"UnderGlow{'R' if sx > 0 else 'L'}", (sx * (HALF[0] - 0.02 + 0.003), 0, BOTTOM + 0.012),
                            (0.002, HALF[1] - 0.1, 0.004))

    yaw = rig["yaw"] = K.make_empty("turretYaw", root, pivot)
    yg = Geo()
    yaw_stage(yg, level, f)
    K.make_object("YawStage", _local(yg, pivot), mats, yaw)

    pitch = rig["pitch"] = K.make_empty("turretPitch", yaw, (0, 0, 0))
    pg = Geo()
    cradle(pg, level, f)
    K.make_object("Cradle", _local(pg, pivot), mats, pitch)
    if level >= 3:
        gy, gz = GRIP_HUB
        lens = Geo()
        S.cbox(lens, (0, 0, 0), (0.014, 0.0015, 0.01), "cyan_glow")
        K.make_object("SightLens", lens, mats, pitch, (0, gy + 0.01 - 0.036 - HIPS_Y, gz + 0.115 - PIVOT_Z))
    if level >= 5:
        S.running_light(pitch, mats, "HoopLamp", (0, HOOP_Y + 0.08 + 0.0125 - HIPS_Y, MOUNT_Z - 0.02 - 0.03 - PIVOT_Z), (0.05, 0.0015, 0.008))
    rig.update(shell_parts(mats, level, f, pitch, pivot))
    return rig
