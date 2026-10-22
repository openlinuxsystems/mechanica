"""Kit for the parts map's batches 11 and 12: stairs, railings, fences, windows, windshields and signs.

The look follows docs/art-direction.md. The stair parts, railings, fences, house windows and road signs are
**Colony Works** (steel-blue ``#3E6A8C`` frames and posts, bone ``#D9D1BC`` panels, mustard ``#E0B530`` handrails and
nosings, graphite ``#2B2F35`` hardware, signal red ``#C8423A``); the vehicle glass (windshields, the Side Window), the
Antenna and the Concave Wedge are **Salvager Tech** (hull, gunmetal, copper, rubber seals); the farm signs and the
Billboard are **Settler Patchwork** (scrap planks, zinc, rust, faded sea teal and brick red). Never orange: Scrap
Mechanic's staircase parts and traffic cone are orange, ours are steel blue and mustard, and signal red.

Every part is one level and has no clips. Its model is centred on the part's footprint (Blender: x right, y the
part's front, z up; the part data's ``size`` is x, z, y here), so a sign or a window shows its face on +y.

What it holds:

* ``materials()``: the batch's materials by role name, made the first time a model asks for one.
* Shapes: ``prism`` (a flat polygon extruded through y, its front face decalled), ``bar`` (a box between two points),
  ``grating`` (a deck of flat bars), ``post`` (a square Colony post on a bolted foot plate), ``frame`` (a window frame
  with corner blocks), ``pane`` (a glass sheet).
* ``decal`` / ``glow_decal``: a material from a PIL image (a sign face, a poster).
* ``run(part, build, ...)``: builds one part, writes ``meshes/parts/<part>.glb`` and ``textures/items/<part>_icon.png``
  and, with ``--render OUT_DIR``, its review sheet (tools/part_kit/render_kit.py).

The part scripts are ``tools/parts_building/build_*.py``; ``signs_kit.py`` holds the sign faces and sign models.
"""

from __future__ import annotations

import math

import bpy
import numpy as np
from mathutils import Matrix, Vector

import part_kit as K
from part_kit import Geo, box, cylinder, dome, hexnut, sweep, lathe, lt, pt

INK = "#2b2f35"
COLONY = {"bone": "#d9d1bc", "steel_blue": "#3e6a8c", "mustard": "#e0b530", "graphite": "#2b2f35", "signal": "#c8423a"}
SETTLER = {"sea_teal": "#4f9a93", "brick": "#a8473b", "rust": "#7a4a30", "plank": "#9c7650", "zinc": "#9aa3ab"}
ICON_VIEW = K.ICON_VIEW


# =====================================================================================================
# Materials
# =====================================================================================================

def _glass(name, color, alpha, rough=0.05):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*lt.srgb(color) ** 2.2, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Alpha"].default_value = alpha
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "BLENDED"
    return mat


def _paint(seed, color, under="#4a4e54"):
    return K.half_res(lt.paint_surface(seed, color, under))


def _metal(seed, color, rough, metal, wear=0.7, edge=0.3):
    return K.half_res(lt.metal_surface(seed, color, rough, metal, wear=wear, edge_light=edge))


MAKERS = {
    # Colony Works paints over graphite steel
    "bone": lambda: lt.textured("b_bone", _paint(601, COLONY["bone"])),
    "steel_blue": lambda: lt.textured("b_steel_blue", _paint(603, COLONY["steel_blue"])),
    "mustard": lambda: lt.textured("b_mustard", _paint(605, COLONY["mustard"])),
    "signal": lambda: lt.textured("b_signal", _paint(607, COLONY["signal"])),
    "graphite": lambda: lt.textured("b_graphite", _metal(609, COLONY["graphite"], 0.5, 0.6, wear=0.6)),
    "galv": lambda: lt.textured("b_galv", _metal(611, "#9aa3ab", 0.42, 0.8, wear=0.9)),
    "grate": lambda: lt.textured("b_grate", _metal(613, "#5b6168", 0.45, 0.8, wear=1.0, edge=0.4)),
    "concrete": lambda: lt.textured("b_concrete", _metal(615, "#a4a29b", 0.85, 0.0, wear=0.3, edge=0.1)),
    # Salvager Tech (the batch 4 kit's)
    "hull": lambda: lt.textured("b_hull", _metal(401, "#24282e", 0.5, 0.65, wear=0.7, edge=0.25)),
    "gun": lambda: lt.textured("b_gun", _metal(403, "#5a6068", 0.44, 0.78, wear=0.8)),
    "gun_block": lambda: lt.textured("b_gun_block", _metal(419, "#5a6068", 0.42, 0.78, wear=0.35), normal_strength=0.8),
    "gun_dark": lambda: lt.textured("b_gun_dark", _metal(405, "#3f444a", 0.5, 0.72, wear=0.7, edge=0.25)),
    "steel": lambda: lt.textured("b_steel", _metal(407, "#8d9196", 0.3, 0.9, wear=0.6)),
    "copper": lambda: lt.textured("b_copper", _metal(409, "#b8733a", 0.32, 0.95, wear=0.6, edge=0.35)),
    "rubber": lambda: lt.textured("b_rubber", K.half_res(lt.rubber_surface(417)), normal_strength=2.0),
    # Settler Patchwork
    "plank": lambda: lt.textured("b_plank", K.half_res(K.plank_surface(445, SETTLER["plank"])), normal_strength=1.5),
    "plank_dark": lambda: lt.textured("b_plank_dark", K.half_res(K.plank_surface(447, "#7d5c3e")), normal_strength=1.5),
    "plank_grey": lambda: lt.textured("b_plank_grey", K.half_res(K.plank_surface(449, "#8a8170")), normal_strength=1.5),
    "zinc": lambda: lt.textured("b_zinc", _metal(449, SETTLER["zinc"], 0.42, 0.8, wear=0.9)),
    "rust": lambda: lt.textured("b_rust", K.half_res(pt.rust_surface(453, "#7a4a30", "#5a3420", "#4a4644", wear=0.6, streaks=0.6))),
    "sea_teal": lambda: lt.textured("b_sea_teal", _paint(459, SETTLER["sea_teal"])),
    "brick_red": lambda: lt.textured("b_brick_red", _paint(461, SETTLER["brick"])),
    # plain ones
    "gun_plain": lambda: pt.plain("b_gun_plain", "#5a6068", 0.42, 0.75),
    "hole": lambda: pt.plain("b_hole", "#0c0d0f", 0.9, 0.2),
    "white": lambda: pt.plain("b_white", "#e8e4d8", 0.45, 0.0),
    "red_tip": lambda: pt.plain("b_red_tip", COLONY["signal"], 0.35, 0.0),
    "cyan_glow": lambda: pt.plain("b_cyan_glow", "#38c8ff", 0.3, 0.0, emission="#38c8ff", strength=3.0),
    "warm_glow": lambda: pt.plain("b_warm_glow", "#fff1cc", 0.3, 0.0, emission="#fff1cc", strength=2.0),
    # glass: house windows clear with a faint green-grey, vehicle glass darker and bluer
    "glass": lambda: _glass("b_glass", "#9fb8b4", 0.22),
    "glass_tint": lambda: _glass("b_glass_tint", "#22343c", 0.6, rough=0.03),
    "glass_dark": lambda: pt.plain("b_glass_dark", "#1b2a30", 0.08, 0.2),
}


class Mats(dict):
    """The materials by role name, each made the first time it is asked for (a part uses a handful)."""

    def __missing__(self, name):
        mat = self[name] = MAKERS[name]()
        return mat


def materials():
    return Mats()


def decal(mats, name, image, rough=0.55, metal=0.0):
    """A material showing `image` (a PIL RGB image) across a face's 0..1 UVs."""
    if name not in mats:
        rgb = np.asarray(image.convert("RGB"), dtype=np.float32) / 255.0
        mat = lt.decal("b_" + name, rgb, rough=rough)
        mat.node_tree.nodes.get("Principled BSDF").inputs["Metallic"].default_value = metal
        mats[name] = mat
    return name


def glow_decal(mats, name, image, glow_image, strength=2.5):
    """A material showing `image`, glowing with `glow_image` (black does not glow): a lit sign face."""
    if name not in mats:
        mat = lt.decal("b_" + name, np.asarray(image.convert("RGB"), dtype=np.float32) / 255.0, rough=0.3)
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        ig = nt.nodes.new("ShaderNodeTexImage")
        ig.image = lt.make_image("b_" + name + "_glow", np.asarray(glow_image.convert("RGB"), dtype=np.float32) / 255.0, "sRGB")
        nt.links.new(ig.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = strength
        mats[name] = mat
    return name


# =====================================================================================================
# Shapes
# =====================================================================================================

def _tri(g, a, b, c, mat, want, uvs=None):
    """One triangle facing `want` (its winding is flipped to match)."""
    a, b, c = Vector(a), Vector(b), Vector(c)
    if (b - a).cross(c - a).dot(Vector(want)) < 0:
        b, c = c, b
        if uvs is not None:
            uvs = (uvs[0], uvs[2], uvs[1])
    g.add([a, b, c], [(0, 1, 2)], mat, uvs=None if uvs is None else [uvs])


def _quad(g, a, b, c, d, mat, want):
    a, b, c, d = map(Vector, (a, b, c, d))
    if (b - a).cross(c - a).dot(Vector(want)) < 0:
        a, b, c, d = a, d, c, b
    g.add([a, b, c, d], [(0, 1, 2, 3)], mat)


def prism(g, pts, y0, y1, side, front=None, back=None, uv_box=None):
    """The (x, z) polygon `pts` (convex, either winding) extruded from y0 (back) to y1 (front). `front` (a decal) is
    mapped over `uv_box` (x0, z0, x1, z1; the polygon's bounds by default) as seen from the front, so its image reads
    the right way round to someone facing the part; `back` defaults to `side`."""
    n = len(pts)
    xs, zs = [p[0] for p in pts], [p[1] for p in pts]
    x0, z0, x1, z1 = uv_box or (min(xs), min(zs), max(xs), max(zs))
    cx, cz = sum(xs) / n, sum(zs) / n

    def uv(x, z):
        return ((x1 - x) / (x1 - x0), (z - z0) / (z1 - z0))

    for k in range(n):
        (ax, az), (bx, bz) = pts[k], pts[(k + 1) % n]
        out = Vector(((ax + bx) / 2 - cx, 0, (az + bz) / 2 - cz))
        _quad(g, (ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az), side, out)
        if front is not None:
            _tri(g, (cx, y1, cz), (ax, y1, az), (bx, y1, bz), front, (0, 1, 0), (uv(cx, cz), uv(ax, az), uv(bx, bz)))
        else:
            _tri(g, (cx, y1, cz), (ax, y1, az), (bx, y1, bz), side, (0, 1, 0))
        _tri(g, (cx, y0, cz), (ax, y0, az), (bx, y0, bz), back or side, (0, -1, 0))
    return g


def prism_x(g, pts, x0, x1, mat, cap=None, center=None):
    """The (y, z) polygon `pts` extruded along x from x0 to x1 (a wedge's or a stringer's profile). The caps are fanned
    from `center` (the centroid by default), so a polygon need only be star-shaped about it."""
    n = len(pts)
    cy, cz = center or (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n)
    for k in range(n):
        (ay, az), (by, bz) = pts[k], pts[(k + 1) % n]
        out = Vector((0, (ay + by) / 2 - cy, (az + bz) / 2 - cz))
        _quad(g, (x0, ay, az), (x0, by, bz), (x1, by, bz), (x1, ay, az), mat, out)
        _tri(g, (x0, cy, cz), (x0, ay, az), (x0, by, bz), cap or mat, (-1, 0, 0))
        _tri(g, (x1, cy, cz), (x1, ay, az), (x1, by, bz), cap or mat, (1, 0, 0))
    return g


def regular(n, r, cx=0.0, cz=0.0, phase=0.0, sx=1.0):
    """A regular polygon's (x, z) points."""
    return [(cx + sx * r * math.cos(phase + 2 * math.pi * k / n), cz + r * math.sin(phase + 2 * math.pi * k / n)) for k in range(n)]


def rounded_rect(hw, hh, r, cx=0.0, cz=0.0, steps=4):
    pts = []
    for (sx, sz, a0) in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        ox, oz = cx + sx * (hw - r), cz + sz * (hh - r)
        for k in range(steps + 1):
            a = math.radians(a0 + 90 * k / steps)
            pts.append((ox + r * math.cos(a), oz + r * math.sin(a)))
    return pts


def bar(g, a, b, half_w, half_h, mat, bevel=0.003, up=(0, 0, 1)):
    """A box from point a to point b, `half_w` across and `half_h` in the `up` direction."""
    a, b = Vector(a), Vector(b)
    d = b - a
    ln = d.length
    x = d.normalized()
    u = Vector(up)
    if abs(x.dot(u.normalized())) > 0.99:
        u = Vector((0, 1, 0)) if abs(x.y) < 0.9 else Vector((1, 0, 0))
    y = u.cross(x).normalized()
    z = x.cross(y)
    rot = Matrix((x, y, z)).transposed()
    return box(g, tuple((a + b) / 2), (ln / 2, half_w, half_h), mat, bevel=bevel, rot=rot)


def screw(g, p, n=(0, 0, 1), r=0.007, mat="graphite"):
    K.screw(g, p, n, r, mat)


def bolt(g, p, n=(0, 0, 1), r=0.009, mat="graphite"):
    """A hex bolt head with a washer (the Colony hardware, slightly oversized)."""
    p, n = Vector(p), Vector(n).normalized()
    cylinder(g, p - n * 0.0005, p + n * 0.002, r * 1.45, mat, segs=12)
    hexnut(g, p + n * 0.002, n, r, r * 0.7, mat)


def post(g, x, y, z0, z1, half=0.022, mat="steel_blue", foot="graphite", cap="graphite", foot_half=0.05):
    """A square Colony post from z0 to z1 at (x, y) on a bolted foot plate, a cap on top."""
    box(g, (x, y, z0 + 0.004), (foot_half, foot_half, 0.004), foot, bevel=0.002)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bolt(g, (x + sx * (foot_half - 0.013), y + sy * (foot_half - 0.013), z0 + 0.008), r=0.0055, mat=foot)
    box(g, (x, y, (z0 + 0.008 + z1 - 0.008) / 2), (half, half, (z1 - z0 - 0.016) / 2), mat, bevel=0.004)
    box(g, (x, y, z1 - 0.006), (half + 0.004, half + 0.004, 0.006), cap, bevel=0.003)


def grating(g, x0, x1, y0, y1, z, mat="grate", pitch=0.05, w=0.004, depth=0.012, cross=0.15):
    """A deck of flat bars along y every `pitch` with cross rods every `cross`, its top at z."""
    nx = max(2, int(round((x1 - x0) / pitch)))
    for k in range(nx + 1):
        x = x0 + (x1 - x0) * k / nx
        box(g, (x, (y0 + y1) / 2, z - depth / 2), (w / 2, (y1 - y0) / 2, depth / 2), mat)
    ny = max(1, int(round((y1 - y0) / cross)))
    for k in range(1, ny):
        y = y0 + (y1 - y0) * k / ny
        box(g, ((x0 + x1) / 2, y, z - 0.003), ((x1 - x0) / 2, 0.003, 0.003), mat)


def frame(g, hw, hh, y0, y1, rail, mat, corner="graphite", screws=True):
    """A rectangular window frame round a hw x hh opening (x, z), `rail` wide, from y0 to y1, with corner blocks."""
    t = rail / 2
    yc, hy = (y0 + y1) / 2, (y1 - y0) / 2
    for sz in (-1, 1):
        box(g, (0, yc, sz * (hh + t)), (hw, hy, t), mat, bevel=0.004)
    for sx in (-1, 1):
        box(g, (sx * (hw + t), yc, 0), (t, hy, hh), mat, bevel=0.004)
    for sx in (-1, 1):
        for sz in (-1, 1):
            box(g, (sx * (hw + t), yc, sz * (hh + t)), (t, hy + 0.003, t), corner, bevel=0.004)
            if screws:
                screw(g, (sx * (hw + t), y1 + 0.003, sz * (hh + t)), (0, 1, 0), 0.0055, "graphite")


def pane(g, hw, hh, y, mat="glass", t=0.003):
    box(g, (0, y, 0), (hw, t, hh), mat)


def gasket(g, hw, hh, y, w=0.006, mat="rubber"):
    """A rubber seal round a pane's edge."""
    for sz in (-1, 1):
        box(g, (0, y, sz * hh), (hw + w, w, w), mat)
    for sx in (-1, 1):
        box(g, (sx * hw, y, 0), (w, w, hh), mat)


# =====================================================================================================
# The runner
# =====================================================================================================

def run(part, build, *, half, ortho, icon_target=(0, 0, 0), title="", lines=(), view=ICON_VIEW, faces_builder=False):
    """Builds `part` with build(mats) -> root, writes its .glb and icon and, with --render OUT_DIR, its review sheet.
    Returns the model's info (part_kit.info).

    Models are built with their face on +y (the views and icon look at it from there). A part placed in the world
    turns its local +Z (glTF) toward the player building it (BuildAim.Orient), so a part whose face should greet its
    builder (a sign, a window) is written turned half round (`faces_builder`); vehicle glass keeps +y as the
    vehicle's front, as the seats do."""
    K.reset_scene()
    mats = materials()
    root = build(mats)
    if faces_builder:
        root.rotation_euler = (0, 0, math.pi)
        bpy.context.view_layer.update()
    i = K.export(root, K.MESHES / f"{part}.glb", animations=False)
    root.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    K.report(K.MESHES / f"{part}.glb", i)
    ic = K.icon(root, K.ICONS / f"{part}_icon.png", view, ortho, icon_target)
    out = K.args_out()
    if out is not None:
        import render_kit as rk
        rk.review(part, title or part, list(lines), {None: root}, [(None, ic)], [rk.fmt_stats(None, i)], out, half)
    return i


def one(name, g, mats):
    """A part made of one mesh under a root empty."""
    root = K.make_empty(name)
    K.make_object(name + "Mesh", g, mats, root)
    return root


def selected(argv, parts):
    """The parts named after --only (comma separated), or all of them."""
    if "--only" in argv:
        want = set(argv[argv.index("--only") + 1].split(","))
        return [p for p in parts if p[0] in want]
    return parts
