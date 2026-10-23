"""Shared kit for the parts map's batch 4 models (seats, saddles, Controller, Sensor, Toilet, Bathtub).

The look is the part models' look (docs/art-direction.md): **Salvager Tech** for the powered and craftable parts
(hull dark ``#24282E``, gunmetal ``#5A6068``, copper ``#B8733A``, cyan ``#38C8FF`` only on powered lights, the
Switch and Button's teal on logic trim) and **Settler Patchwork** for the Bathtub (bone enamel, scrap wood, zinc,
rust); the Toilet is plain white porcelain and chrome. Built on the Lift's geometry and texture kit (``tools/lift``) and the Gas
Engine's materials, like the Switch.

What it holds:

* ``materials()``: every material the batch's models use, by role name (Salvager metals and fabrics per finish,
  Settler enamel, wood and zinc, the glows).
* Shapes on top of ``build_lift``'s: ``squircle_loft`` (a cushion or saddle lofted through rounded sections),
  ``pips`` (one copper pip per level, cyan and lit at level 5, the Sport Suspension's rule), ``link_box`` (where a
  seat's links plug in, in the Switch's plate style: gunmetal, a teal band, copper screws and a teal-ringed socket),
  ``logic_plate`` (the Switch and Button's base plate, for the 1-block logic parts).
* ``bake_seat_clips``: the seat clips the seat script plays from its ``clip`` display (``enter``, ``seated_loop``,
  ``seated_rattle_loop``, ``exit``), moving a ``Shell`` node and sinking the ``Cushion`` and ``Backrest`` nodes.
* ``bake_pose_clips``: ``on`` and ``off`` clips that swap glows (the Controller and Sensor lights, as the Switch).
* ``export`` / ``info`` / ``icon``: write a .glb, read its triangles and bounds back, render a 256 px icon.

Run the part scripts (``tools/<part>/build_<part>.py``), not this file.
"""

from __future__ import annotations

import math
import struct
import sys
import json
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Euler, Matrix, Vector

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
for sub in ("lift", "bearing_engine", "gas_engine"):
    sys.path.insert(0, str(TOOLS / sub))
import build_bearing_engine as bbe  # noqa: E402  (lathe, disc, export, icon render)
import build_lift as bl  # noqa: E402  (Geo, primitives, objects, NLA tracks)
import lift_textures as lt  # noqa: E402
import part_textures as pt  # noqa: E402

REPO = TOOLS.parent
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = 60
LEVELS = (1, 2, 3, 4, 5)

Geo, box, cylinder, ring, dome, hexnut, sweep, bezier, quad = (
    bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep, bl.bezier, bl.quad)
lathe, disc = bbe.lathe, bbe.disc
OFF = 0.01                 # scale of a glow that is off (hidden inside its housing)


# =====================================================================================================
# Materials
# =====================================================================================================

def half_res(surface):
    """A 256-pixel copy of a 512-pixel tileable surface (every other pixel keeps it tiling), to keep the files small."""
    return tuple(a[::2, ::2] for a in surface)


def fabric_surface(seed, color, thread=None, quilt=0.0):
    """Woven seat fabric: a fine basket weave, soft mottling and wear on the high spots; `quilt` adds stitched
    diamond quilting (the level 5 seats)."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    weave = 0.5 * np.sin(xx * 2 * math.pi / 6) * np.sign(np.sin(yy * 2 * math.pi / 12)) + 0.5 * np.sin(yy * 2 * math.pi / 6)
    mottle = lt.fbm(seed, 4.0)
    wear = np.clip(lt.fbm(seed + 1, 6.0) * 1.6 - 0.35, 0, 1)
    base = lt.srgb(color)[None, None, :]
    albedo = base * (1 + 0.07 * weave[..., None] + 0.10 * mottle[..., None]) * (1 + 0.18 * wear[..., None])
    height = 0.25 * weave + 0.3 * mottle
    if quilt > 0:
        period = size // 4
        d1 = np.abs(((xx + yy) % period) - period / 2) / (period / 2)
        d2 = np.abs(((xx - yy) % period) - period / 2) / (period / 2)
        seam = np.clip(1 - np.minimum(d1, d2) * 14, 0, 1)
        stitch = seam * (np.sin((xx + yy) * 2 * math.pi / 8) > 0)
        albedo = albedo * (1 - 0.35 * seam[..., None])
        if thread is not None:
            t = lt.srgb(thread)[None, None, :]
            albedo = albedo * (1 - stitch[..., None]) + t * stitch[..., None]
        puff = 1 - np.minimum(d1, d2)
        height = height + quilt * (puff - 1.2 * seam)
    rough = 0.82 + 0.08 * mottle - 0.1 * wear
    return albedo, height, rough, np.zeros((size, size))


def enamel_surface(seed, color, chips=1.0):
    """Settler bathroom enamel: glossy off-white over dark iron, chipped at random and grimy low down."""
    size = lt.SIZE
    mottle = lt.fbm(seed, 5.0)
    chip = np.clip((lt.fbm(seed + 1, 9.0) + 0.4 * lt.fnoise(seed + 2, 70.0) - 0.62) * 14 * chips, 0, 1)
    grime = np.clip(lt.fbm(seed + 3, 2.5) * 1.4 - 0.2, 0, 1)
    craze = lt.scratches(seed + 4, 60, bias=0.1) * 0.5
    enamel = lt.srgb(color)[None, None, :] * (1 + 0.04 * mottle[..., None]) * (1 - 0.18 * grime[..., None])
    enamel = enamel * (1 - 0.25 * craze[..., None])
    iron = lt.srgb("#2f2b28")[None, None, :] * (1 + 0.2 * mottle[..., None])
    albedo = enamel * (1 - chip[..., None]) + iron * chip[..., None]
    height = 0.5 * (1 - chip) + 0.05 * mottle - 0.2 * craze
    rough = (0.18 + 0.12 * grime) * (1 - chip) + 0.7 * chip
    metal = 0.6 * chip
    return albedo, height, rough, metal


def porcelain_surface(seed, color, wear=1.0):
    """Glossy vitreous china: near-flat white with a faint mottle, a little grime and the odd hairline craze."""
    size = lt.SIZE
    mottle = lt.fbm(seed, 3.0)
    grime = np.clip(lt.fbm(seed + 1, 2.0) * 1.3 - 0.45, 0, 1) * wear
    craze = lt.scratches(seed + 2, 24, bias=0.1) * 0.25 * wear
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.02 * mottle[..., None]) * (1 - 0.08 * grime[..., None])
    albedo = albedo * (1 - 0.12 * craze[..., None])
    height = 0.03 * mottle - 0.08 * craze
    rough = 0.07 + 0.08 * grime + 0.02 * mottle
    return albedo, height, rough, np.zeros((size, size))


def plank_surface(seed, color):
    """Weathered scrap planks: grain along the image's u (box UVs run it along a plank's long side)."""
    size = lt.SIZE
    grain = lt.fnoise(seed, 60.0, aniso=(0.06, 1.0))
    rings = np.sin((lt.fnoise(seed + 1, 8.0, aniso=(0.2, 1.0)) * 6 + np.mgrid[0:size, 0:size][0] / size * 22) * math.pi)
    mottle = lt.fbm(seed + 2, 3.0)
    base = lt.srgb(color)[None, None, :]
    albedo = base * (1 + 0.16 * grain[..., None] + 0.08 * rings[..., None] + 0.12 * mottle[..., None])
    return albedo, 0.5 * grain + 0.2 * rings, 0.8 + 0.05 * mottle, np.zeros((size, size))


def materials():
    tex, metal = lt.textured, lt.metal_surface
    m = {
        # Salvager Tech metals
        "hull": tex("k_hull", half_res(metal(401, "#24282e", 0.5, 0.65, wear=0.7, edge_light=0.25))),
        "gun": tex("k_gun", half_res(metal(403, "#5a6068", 0.44, 0.78, wear=0.8, edge_light=0.3))),
        "gun_dark": tex("k_gun_dark", half_res(metal(405, "#3f444a", 0.5, 0.72, wear=0.7, edge_light=0.25))),
        "steel": tex("k_steel", half_res(metal(407, "#8d9196", 0.3, 0.9, wear=0.6, edge_light=0.3))),
        "copper": tex("k_copper", half_res(metal(409, "#b8733a", 0.32, 0.95, wear=0.6, edge_light=0.35))),
        "copper_bright": tex("k_copper_bright", half_res(metal(411, "#cc834e", 0.2, 1.0, wear=0.3, edge_light=0.2))),
        "chrome": tex("k_chrome", half_res(lt.chrome_surface(413)), normal_strength=1.0),
        "black_chrome": tex("k_black_chrome", half_res(metal(415, "#1c1e22", 0.16, 1.0, wear=0.25, edge_light=0.15)), normal_strength=1.0),
        "rubber": tex("k_rubber", half_res(lt.rubber_surface(417)), normal_strength=2.0),
        # logic trim and lights (the Switch and Button's)
        "teal": pt.plain("k_teal", "#2b8a92", 0.32, 0.35),
        "teal_glow": pt.plain("k_teal_glow", "#3fdcea", 0.3, 0.0, emission="#3fdcea", strength=1.4),
        "cyan_glow": pt.plain("k_cyan_glow", "#38c8ff", 0.3, 0.0, emission="#38c8ff", strength=3.0),
        "red_glow": pt.plain("k_red_glow", "#d0241c", 0.3, 0.0, emission="#ff1a10", strength=1.2),
        "hole": pt.plain("k_hole", "#0c0d0f", 0.9, 0.2),
        "lens": pt.plain("k_lens", "#14181c", 0.08, 0.0),
        "glass": pt.plain("k_glass", "#2a4650", 0.05, 0.2),
        "screen": pt.plain("k_screen", "#0b1418", 0.25, 0.0),
        # seat fabrics (cotton): charcoal at levels 1 to 3, deep slate at 4, black quilted at 5; teal piping and webbing
        "fabric": tex("k_fabric", half_res(fabric_surface(421, "#33383f")), normal_strength=1.5),
        "fabric_dark": tex("k_fabric_dark", half_res(fabric_surface(423, "#24292f")), normal_strength=1.5),
        "fabric_slate": tex("k_fabric_slate", half_res(fabric_surface(425, "#2c3a44")), normal_strength=1.5),
        "fabric_quilt": tex("k_fabric_quilt", half_res(fabric_surface(427, "#1d2024", thread="#38c8ff", quilt=0.6)), normal_strength=2.5),
        "piping": tex("k_piping", half_res(fabric_surface(429, "#2b8a92")), normal_strength=1.0),
        "webbing": tex("k_webbing", half_res(fabric_surface(431, "#1f6f78")), normal_strength=1.0),
        # Settler Patchwork
        "enamel": tex("k_enamel", half_res(enamel_surface(441, "#d9d1bc")), normal_strength=1.2),
        "enamel_in": tex("k_enamel_in", half_res(enamel_surface(443, "#e4ddcb", chips=0.4)), normal_strength=0.8),
        "plank": tex("k_plank", half_res(plank_surface(445, "#9c7650")), normal_strength=1.5),
        "plank_dark": tex("k_plank_dark", half_res(plank_surface(447, "#7d5c3e")), normal_strength=1.5),
        "zinc": tex("k_zinc", half_res(metal(449, "#9aa3ab", 0.42, 0.8, wear=0.9, edge_light=0.3))),
        "graphite": tex("k_graphite", half_res(metal(451, "#2b2f35", 0.5, 0.6, wear=0.6, edge_light=0.3))),
        "rust": tex("k_rust", half_res(pt.rust_surface(453, "#7a4a30", "#5a3420", "#4a4644", wear=0.6, streaks=0.6))),
        "brass": tex("k_brass", half_res(metal(455, "#a8875a", 0.35, 0.95, wear=0.5, edge_light=0.3))),
        "mustard": tex("k_mustard", half_res(fabric_surface(457, "#e0b530")), normal_strength=1.2),
        "sea_teal": tex("k_sea_teal", half_res(lt.paint_surface(459, "#4f9a93", "#4a4e54"))),
        "water": pt.plain("k_water", "#4f7f86", 0.05, 0.0),
        # the Toilet's porcelain and its plastic seat
        "porcelain": tex("k_porcelain", half_res(porcelain_surface(461, "#f2f1ec")), normal_strength=0.5),
        "seat_white": pt.plain("k_seat_white", "#e9e7e1", 0.3, 0.0),
    }
    return m


# Each level's finish, by role: levels 1 to 4 are worn gunmetal with copper, 5 black chrome with polished copper.
FINISH = {
    1: {"frame": "gun", "dark": "hull", "trim": "copper", "bolt": "copper", "fabric": "fabric", "pip": "copper"},
    2: {"frame": "gun", "dark": "hull", "trim": "copper", "bolt": "copper", "fabric": "fabric", "pip": "copper"},
    3: {"frame": "steel", "dark": "gun_dark", "trim": "copper", "bolt": "steel", "fabric": "fabric_dark", "pip": "copper"},
    4: {"frame": "gun", "dark": "hull", "trim": "copper_bright", "bolt": "steel", "fabric": "fabric_slate", "pip": "copper_bright"},
    5: {"frame": "black_chrome", "dark": "black_chrome", "trim": "copper_bright", "bolt": "chrome", "fabric": "fabric_quilt", "pip": "cyan_glow"},
}


# =====================================================================================================
# Shapes
# =====================================================================================================

def squircle(n_pts, half_w, half_h, power=4.0):
    """Points round a rounded rectangle ("squircle") in its plane (u right, v up), counter-clockwise from the right."""
    out = []
    e = 2.0 / power
    for k in range(n_pts):
        a = 2 * math.pi * k / n_pts
        c, s = math.cos(a), math.sin(a)
        out.append((half_w * math.copysign(abs(c) ** e, c), half_h * math.copysign(abs(s) ** e, s)))
    return out


def squircle_loft(g, sections, mat, ring_pts=24, power=4.0, matrix=None, caps=True):
    """A soft solid lofted along Y through `sections`: (y, x_centre, half_width, z_bottom, z_top). Each section is a
    squircle in the x-z plane; the ends are capped. `matrix` (4x4) places it."""
    verts, faces = [], []
    for (y, xc, hw, zb, zt) in sections:
        zc, hh = (zb + zt) / 2, (zt - zb) / 2
        for u, v in squircle(ring_pts, hw, hh, power):
            verts.append((xc + u, y, zc + v))
    n = len(sections)
    for i in range(n - 1):
        for k in range(ring_pts):
            k1 = (k + 1) % ring_pts
            a, b = i * ring_pts + k, i * ring_pts + k1
            faces.append((a, a + ring_pts, b + ring_pts, b))
    if caps:
        faces.append(tuple(range(ring_pts)))
        faces.append(tuple((n - 1) * ring_pts + k for k in reversed(range(ring_pts))))
    if matrix is not None:
        verts = [tuple(matrix @ Vector(p)) for p in verts]
    return g.add(verts, faces, mat)


def loft_edge(sections, angle_deg, side, power=4.0):
    """Points along a squircle loft's surface at `angle_deg` from the right (+x) on `side` (+1 right, -1 left)."""
    e = 2.0 / power
    a = math.radians(angle_deg)
    c, s = math.cos(a), math.sin(a)
    out = []
    for (y, xc, hw, zb, zt) in sections:
        zc, hh = (zb + zt) / 2, (zt - zb) / 2
        out.append(Vector((xc + side * hw * abs(c) ** e, y, zc + hh * math.copysign(abs(s) ** e, s))))
    return out


def smooth_sections(keys, steps=4):
    """Sections between key sections (y, xc, hw, zb, zt), eased (Catmull-Rom on each value)."""
    keys = [np.asarray(k, float) for k in keys]
    out = []
    for i in range(len(keys) - 1):
        p0, p1, p2, p3 = keys[max(i - 1, 0)], keys[i], keys[i + 1], keys[min(i + 2, len(keys) - 1)]
        for k in range(steps):
            t = k / steps
            t2, t3 = t * t, t * t * t
            p = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append(tuple(p))
    out.append(tuple(keys[-1]))
    return out


def screw(g, p, n=(0, 0, 1), r=0.0075, mat="copper"):
    """A button-head screw with a dark hex socket (the Switch's)."""
    p, n = Vector(p), Vector(n).normalized()
    cylinder(g, p - n * 0.001, p + n * r * 0.3, r, mat, segs=10, r1=r * 0.82)
    hexnut(g, p + n * r * 0.2, n, r * 0.42, r * 0.12, "hole")


def pips(g, level, start, step, normal, finish, r=0.0065):
    """One pip per level along `step` from `start`, flush on a face facing `normal` (lit cyan at level 5)."""
    start, step, n = Vector(start), Vector(step), Vector(normal).normalized()
    for k in range(level):
        p = start + step * k
        cylinder(g, p - n * 0.002, p + n * 0.0025, r + 0.0018, "hole", segs=12)
        cylinder(g, p - n * 0.001, p + n * 0.0032, r, finish["pip"], segs=12)


def link_box(g, at, facing, finish, scale=1.0):
    """Where a part's links plug in: a small gunmetal box with a teal band, copper screws and a teal-ringed socket
    on its face (the Switch's plate style). `at` is the bottom centre, `facing` the way its face looks (x or y)."""
    at = Vector(at)
    f = Vector(facing).normalized()
    side = Vector((0, 0, 1)).cross(f).normalized()
    s = scale
    h = 0.036 * s
    m = Matrix((side, f, (0, 0, 1))).transposed()
    box(g, tuple(at + Vector((0, 0, h))), (0.042 * s, 0.026 * s, h), finish["dark"], bevel=0.005 * s, rot=m)
    box(g, tuple(at + Vector((0, 0, h * 0.35))), (0.044 * s, 0.028 * s, 0.004 * s), "teal", bevel=0.0015 * s, rot=m)
    face = at + f * 0.026 * s + Vector((0, 0, h * 1.15))
    cylinder(g, face - f * 0.002, face + f * 0.004 * s, 0.014 * s, "teal", segs=20)
    cylinder(g, face, face + f * 0.0045 * s, 0.009 * s, "hole", segs=16)
    for sx in (-1, 1):
        for sz in (0.45, 1.75):
            screw(g, face - f * 0.0 + side * sx * 0.030 * s + Vector((0, 0, (sz - 1.15) * h)), f, 0.0045 * s, finish["bolt"])


def logic_plate(g, floor, half_xy, finish, screws=True):
    """The Switch and Button's base plate for a 1-block logic part: dark gunmetal, chamfered, a thin teal line round
    its sides and copper screws in the corners. Returns the plate's top height."""
    hx, hy = half_xy
    t = 0.0095
    box(g, (0, 0, floor + t), (hx, hy, t), finish["dark"], bevel=0.006)
    box(g, (0, 0, floor + t * 0.9), (hx + 0.0008, hy + 0.0008, 0.0016), "teal", bevel=0.0008)
    top = floor + 2 * t
    if screws:
        for sx in (-1, 1):
            for sy in (-1, 1):
                screw(g, (sx * (hx - 0.016), sy * (hy - 0.016), top), (0, 0, 1), 0.0072, finish["bolt"])
    return top


def glow_box(half, mat):
    g = Geo()
    box(g, (0, 0, 0), half, mat)
    return g


def glow_disc(r, n, mat, depth=0.002, segs=24):
    g = Geo()
    n = Vector(n)
    cylinder(g, n * -depth / 2, n * depth / 2, r, mat, segs=segs)
    return g


def glow_ring(r_in, r_out, mat, z=0.0, h=0.002):
    g = Geo()
    ring(g, z - h / 2, z + h / 2, r_in, r_out, mat, segs=36)
    return g


# =====================================================================================================
# Scene, objects, clips
# =====================================================================================================

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start = 0
    sc.frame_end = 2 * FPS


make_object, make_empty, push_track = bl.make_object, bl.make_empty, bl.push_track


def smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def sink(t, rise=0.16):
    """0 to 1 over `rise` seconds with a little overshoot, then settled."""
    if t <= 0:
        return 0.0
    u = t / rise
    return 1.0 - math.exp(-3.2 * u) * math.cos(4.0 * u) if u < 3 else 1.0


def shake(t, amp, seed, freqs=(11.0, 13.0, 9.0)):
    rng = np.random.default_rng(seed)
    ph = rng.uniform(0, 2 * math.pi, 3)
    return [amp * math.sin(2 * math.pi * f * t + p) for f, p in zip(freqs, ph)]


SEAT_CLIPS = {"enter": 0.8, "seated_loop": 2.0, "seated_rattle_loop": 2.0, "exit": 0.7}


def seat_pose(rig, clip, t):
    """The seat clips (as the scrap seats'): `Shell` jolts and rattles, `Cushion` (scale z) and `Backrest` (scale on its
    own `back_axis`) sink under the rider and breathe."""
    shell, cushion, back = rig["shell"], rig.get("cushion"), rig.get("back")
    pan_k, back_k = rig.get("pan_sink", 0.12), rig.get("back_sink", 0.08)
    loc, rot = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]
    pan, bk = 1.0, 1.0
    if clip == "enter":
        pan = 1 - pan_k * sink(t)
        bk = 1 - back_k * sink(t - 0.08)
        decay = math.exp(-max(t - 0.05, 0) / 0.16) * smooth(t / 0.05) * (1 - smooth((t - 0.62) / 0.18))
        rx, ry, rz = shake(t, math.radians(0.5) * decay, 11)
        rot = [rx, ry, rz * 0.5]
        bump = math.sin(math.pi * min(max(t - 0.03, 0) / 0.42, 1)) * math.exp(-max(t - 0.03, 0) / 0.3)
        loc = [0.0, 0.0, -0.004 * bump]
    elif clip in ("seated_loop", "seated_rattle_loop"):
        breath = math.sin(2 * math.pi * t / 2.0)
        pan = 1 - pan_k * (1 + 0.1 * breath)
        bk = 1 - back_k * (1 + 0.12 * math.sin(2 * math.pi * t / 2.0 + 0.4))
        if clip == "seated_rattle_loop":
            rx, ry, rz = shake(t, math.radians(0.18), 13, (9.0, 13.0, 7.5))
            rot = [rx, ry, rz * 0.6]
            loc = [v * 0.001 for v in shake(t, 1.0, 14, (11.0, 7.0, 13.0))]
            pan += 0.01 * math.sin(2 * math.pi * 6.5 * t)
    elif clip == "exit":
        pan = 1 - pan_k * (1 - sink(t, 0.12))
        bk = 1 - back_k * (1 - sink(t - 0.04, 0.12))
        decay = math.exp(-t / 0.14) * (1 - smooth((t - 0.5) / 0.2))
        rx, ry, rz = shake(t, math.radians(0.4) * decay, 15)
        rot = [rx, ry, rz * 0.5]
        bump = math.sin(math.pi * min(t / 0.4, 1)) * math.exp(-t / 0.25)
        loc = [0.0, 0.0, 0.002 * bump]
    shell.location = Vector(rig["shell_at"]) + Vector(loc)
    shell.rotation_euler = Euler(rot)
    if cushion is not None:
        cushion.scale = (1, 1, pan)
    if back is not None:
        s = [1.0, 1.0, 1.0]
        s[rig.get("back_axis", 1)] = bk
        back.scale = s


def seat_rest(rig):
    rig["shell"].location = rig["shell_at"]
    rig["shell"].rotation_euler = (0, 0, 0)
    for k in ("cushion", "back"):
        if rig.get(k) is not None:
            rig[k].scale = (1, 1, 1)


def bake_seat_clips(rig):
    obs = [o for o in (rig["shell"], rig.get("cushion"), rig.get("back")) if o is not None]
    for ob in obs:
        ob.animation_data_create()
    for clip, seconds in SEAT_CLIPS.items():
        acts = {}
        for ob in obs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{clip}_{ob.name}")
        frames = int(round(seconds * FPS))
        for f in range(frames + 1):
            # a loop's last key equals its first, so it wraps without a jump
            seat_pose(rig, clip, (f % frames if clip.startswith("seated") else f) / FPS)
            for ob in obs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in obs:
            lt.linear_keys(acts[ob.name])
            push_track(ob, acts[ob.name], clip)
    for ob in obs:
        ob.animation_data.use_nla = False
    seat_rest(rig)
    bake_scrubs(rig)


def turner(ob, axis, degrees):
    """apply(u) for bake_scrubs: turns `ob` about `axis` (its parent's frame, through its origin) by degrees(u)."""
    def apply(u):
        ob.rotation_mode = "XYZ"
        ob.rotation_euler = Matrix.Rotation(math.radians(degrees(u)), 3, axis).to_euler("XYZ", ob.rotation_euler)
    return apply


SCRUB_SECONDS = 1.0


def bake_scrubs(rig):
    """The clips the game holds at the point a readout sets instead of playing (part data display.clips.scrub):
    rig["scrubs"] maps a clip name to (objects, apply(u), rest), apply posing them for u from 0 (the clip's start) to
    1 (its end); each is keyed every frame over SCRUB_SECONDS, then left at apply(rest)."""
    for clip, (objects, apply, rest) in rig.get("scrubs", {}).items():
        acts = {}
        for ob in objects:
            ob.animation_data_create()
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{clip}_{ob.name}")
        frames = int(round(SCRUB_SECONDS * FPS))
        for f in range(frames + 1):
            apply(f / frames)
            for ob in objects:
                ob.keyframe_insert("rotation_euler", frame=f)
        for ob in objects:
            lt.linear_keys(acts[ob.name])
            push_track(ob, acts[ob.name], clip)
            ob.animation_data.use_nla = False
        apply(rest)


SNAP = 0.12


def bake_pose_clips(objects, apply_pose):
    """`on` and `off` clips (0.12 s each, holding their last frame) from apply_pose(on in 0..1); objects are keyed on
    location, rotation and scale."""
    for ob in objects:
        ob.animation_data_create()
        ob.rotation_mode = "XYZ"
    frames = max(2, int(round(SNAP * FPS)))
    for name, start, end in (("off", 1.0, 0.0), ("on", 0.0, 1.0)):
        acts = {}
        for ob in objects:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        for f in range(frames + 1):
            u = smooth(f / frames)
            apply_pose(start + (end - start) * u)
            for ob in objects:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objects:
            lt.linear_keys(acts[ob.name])
            push_track(ob, acts[ob.name], name)
    for ob in objects:
        ob.animation_data.use_nla = False
    apply_pose(0.0)


def strip_animation(root):
    for ob in [root, *root.children_recursive]:
        if ob.animation_data:
            ob.animation_data.action = None


# =====================================================================================================
# Export, stats and icons
# =====================================================================================================

def export(root, path, animations=True):
    bbe.export(root, path, animations)
    i = info(path)
    i["min"], i["max"] = world_bounds(root)
    return i


def world_bounds(root):
    """The model's bounds at rest in the part's frame, glTF axes (x right, y up, z back), from every vertex."""
    bpy.context.view_layer.update()
    pts = []
    for ob in [root, *root.children_recursive]:
        if ob.type == "MESH":
            mw = ob.matrix_world
            pts += [tuple(mw @ v.co) for v in ob.data.vertices]
    p = np.asarray(pts)
    gl = np.stack([p[:, 0], p[:, 2], -p[:, 1]], axis=1)
    return gl.min(axis=0), gl.max(axis=0)


def info(path):
    """Bounds (glTF axes), triangles, images and size of a written .glb."""
    data = Path(path).read_bytes()
    ln, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            lo, hi = np.minimum(lo, acc["min"]), np.maximum(hi, acc["max"])
            tris += doc["accessors"][prim["indices"]]["count"] // 3
    return {"min": lo, "max": hi, "tris": tris, "kib": len(data) / 1024, "clips": [a["name"] for a in doc.get("animations", [])]}


def report(path, i):
    print(f"wrote {Path(path).relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles) bounds "
          f"{np.round(i['min'], 3).tolist()} .. {np.round(i['max'], 3).tolist()} clips {i['clips']}")


def icon(root, path, view, ortho, target=(0, 0, 0)):
    return bbe.render_icon([root, *root.children_recursive], path, view=view, ortho=ortho, target=target)


def args_out():
    """The --render OUT_DIR argument, or None."""
    return Path(sys.argv[sys.argv.index("--render") + 1]) if "--render" in sys.argv else None


# =====================================================================================================
# The part scripts' runner
# =====================================================================================================

ICON_VIEW = (-1.0, 1.25, 0.8)     # front right, from above (Blender's +y is the part's front)


def run(part, build, levels=LEVELS, *, half, ortho, icon_target=(0, 0, 0), title="", lines=(), bake=None):
    """Builds `part` at each level with build(mats, level) -> rig (rig["root"] the root empty), bakes its clips with
    bake(rig), writes ``meshes/parts/<part>_<level>.glb`` and ``textures/items/<part>_<level>_icon.png`` (just
    ``<part>.glb`` and ``<part>_icon.png`` when `levels` is (None,)), and with ``--render OUT_DIR`` the review views
    and sheet (render_kit). Returns {level: info}."""
    reset_scene()
    mats = materials()
    roots, infos, icons = {}, {}, []
    for level in levels:
        rig = build(mats, level)
        root = rig["root"]
        if bake is not None:
            bake(rig)
        stem = f"{part}_{level}" if level is not None else part
        i = infos[level] = export(root, MESHES / f"{stem}.glb", animations=bake is not None)
        report(MESHES / f"{stem}.glb", i)
        icons.append((level, icon(root, ICONS / f"{stem}_icon.png", ICON_VIEW, ortho, icon_target)))
        # free the names for the next level (the file is written)
        for ob in [root, *root.children_recursive]:
            ob.name = f"{ob.name}_L{level}"
        roots[level] = root
    out = args_out()
    if out is not None:
        import render_kit as rk
        stats = [rk.fmt_stats(lv, infos[lv]) for lv in levels]
        rk.review(part, title or part, list(lines), roots, icons, stats, out, half)
    return infos
