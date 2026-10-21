"""Shared kit for the parts map's furniture and decor batches (13a-15b): office, home, farm, shack, industrial and
site decor. The models are in ``tools/decor/`` (one module per family of batches); this file holds what they share.

The families are docs/art-direction.md's: **Colony Works** for the office, warehouse and industrial pieces (bone
panels in steel-blue rails, graphite trim, mustard accents, cyan only on what still has power), **Settler
Patchwork** for the farm and shack pieces (scrap wood, faded sea teal, zinc, rust, cotton) and **Salvager Tech** for
the SV-04's own pieces (hull, gunmetal, copper). The materials are the furniture containers'
(``tools/containers/build_furniture.py``) plus a few of our own: cotton ticking, paper, cardboard, cast concrete,
rebar and the crops' colours.

What it holds:

* ``materials()``: every material the decor models use, by role name;
* shapes on top of the Lift's, the containers' and batch 4's (``box``, ``cylinder``, ``lathe``, ``Face``, ``squircle_loft``,
  ``basin`` ...): ``panel`` (a bone
  panel held in a rail frame), ``perforated`` (an upright with a row of punched slots), ``bolts``, ``image_mat``
  (a material from a drawn PIL image, for labels and prints) and ``decal`` (a quad that carries one);
* ``run``: builds parts from a registry, writes ``meshes/parts/<id>.glb`` and ``textures/items/<id>_icon.png``,
  checks every model stays inside its grid box, and with ``--render OUT_DIR`` renders a review sheet per part.

Axes (as the furniture containers): Blender Z up, the part's front towards -Y, centred on the origin. The exporter
turns that into glTF +Y up with the front towards +Z. Sizes are the part data's: blocks across (x), tall (y), deep (z).
"""

from __future__ import annotations

import itertools
import math
import struct
import json
import sys
from pathlib import Path

import bpy  # noqa: F401  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
sys.path.insert(0, str(TOOLS / "containers"))
sys.path.insert(0, str(TOOLS / "lift"))
import build_furniture as fm  # noqa: E402  (Colony Works / Settler materials and shapes; puts the Lift kit on the path)
import part_kit as K  # noqa: E402  (squircle lofts)
import furniture_kit as F  # noqa: E402  (basins and bent pipe runs)

bbe, bl, lt, pt, gc = fm.bbe, fm.bl, fm.lt, fm.pt, fm.gc
REPO = TOOLS.parent
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
BLOCK = 0.25

Geo, box, cylinder, ring, dome, hexnut, sweep, quad = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep, bl.quad
lathe, disc, Face, edge_frame, hinge = bbe.lathe, bbe.disc, fm.Face, fm.edge_frame, fm.hinge
squircle_loft, basin, bend = K.squircle_loft, F.basin, F.bend
BONE, STEEL, GRAPHITE, MUSTARD, SIGNAL = fm.BONE, fm.STEEL, fm.GRAPHITE, fm.MUSTARD, fm.SIGNAL
HULL, GUNMETAL, COPPER, CYAN = fm.HULL, fm.GUNMETAL, fm.COPPER, fm.CYAN
WOOD, SEA, ZINC, RUST = fm.WOOD, "#4f9a93", fm.ZINC, "#7a4a30"
BRICK = "#a8473b"


# =====================================================================================================
# Materials
# =====================================================================================================

def cloth_surface(seed, color, stripe=None, period=24, tuft=0):
    """Cotton cloth: a fine weave, soft mottle and grime; `stripe` paints ticking stripes along v every `period`
    pixels; `tuft` darkens a grid of button dimples (that many per side)."""
    size = lt.SIZE
    yy, xx = np.mgrid[0:size, 0:size]
    weave = 0.5 * np.sin(xx * 2 * math.pi / 4) * np.sign(np.sin(yy * 2 * math.pi / 8)) + 0.5 * np.sin(yy * 2 * math.pi / 4)
    mottle = lt.fbm(seed, 4.0)
    grime = np.clip(lt.fbm(seed + 1, 2.0) * 1.3 - 0.3, 0, 1)
    albedo = np.broadcast_to(lt.srgb(color)[None, None, :], (size, size, 3)).copy()
    if stripe is not None:
        band = ((xx % period) < period * 0.28)[..., None]
        albedo = np.where(band, lt.srgb(stripe)[None, None, :], albedo)
    albedo = albedo * (1 + 0.06 * weave[..., None] + 0.08 * mottle[..., None]) * (1 - 0.16 * grime[..., None])
    height = 0.2 * weave + 0.25 * mottle
    if tuft:
        cell = size / tuft
        d = np.hypot((xx % cell) - cell / 2, (yy % cell) - cell / 2) / (cell / 2)
        dimple = np.clip(1 - d, 0, 1) ** 2
        albedo = albedo * (1 - 0.25 * dimple[..., None])
        height = height + 0.8 * (d.clip(0, 1) - 1)
    rough = 0.86 + 0.06 * mottle
    return albedo, height, rough, np.zeros((size, size))


def paper_surface(seed, color, fibres=0.5):
    """Paper and card: flat colour, faint fibres and a little handling grime."""
    size = lt.SIZE
    fib = lt.fnoise(seed, 80.0)
    mottle = lt.fbm(seed + 1, 3.0)
    grime = np.clip(lt.fbm(seed + 2, 2.0) * 1.4 - 0.5, 0, 1)
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.04 * fibres * fib[..., None] + 0.05 * mottle[..., None])
    albedo = albedo * (1 - 0.15 * grime[..., None])
    return albedo, 0.1 * fib, 0.9 - 0.04 * mottle, np.zeros((size, size))


def concrete_surface(seed, color):
    """Cast concrete: speckled aggregate, pores and grime."""
    size = lt.SIZE
    speck = lt.fnoise(seed, 120.0)
    mottle = lt.fbm(seed + 1, 5.0)
    pores = np.clip((lt.fnoise(seed + 2, 90.0) - 0.7) * 8, 0, 1)
    grime = np.clip(lt.fbm(seed + 3, 2.0) * 1.3 - 0.3, 0, 1)
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.1 * speck[..., None] + 0.1 * mottle[..., None])
    albedo = albedo * (1 - 0.5 * pores[..., None]) * (1 - 0.2 * grime[..., None])
    return albedo, 0.4 * speck - 0.8 * pores + 0.3 * mottle, 0.92 + 0.04 * speck, np.zeros((size, size))


def straw_surface(seed, color):
    """Baled straw: long stalks along u, light and dark, with gaps between them."""
    size = lt.SIZE
    stalks = lt.fnoise(seed, 140.0, aniso=(0.03, 1.0))
    broad = lt.fnoise(seed + 1, 20.0, aniso=(0.1, 1.0))
    gaps = np.clip((lt.fnoise(seed + 2, 90.0, aniso=(0.05, 1.0)) - 0.66) * 6, 0, 1)
    albedo = lt.srgb(color)[None, None, :] * (1 + 0.28 * stalks[..., None] + 0.12 * broad[..., None]) * (1 - 0.55 * gaps[..., None])
    return albedo, 0.7 * stalks - 0.8 * gaps, 0.85 + 0.05 * broad, np.zeros((size, size))


def materials():
    m = fm.make_materials()
    tex, paint, metal = lt.textured, fm.paint, fm.metal
    m.update({
        # Colony Works, beyond the containers'
        "bone_dark": paint("dk_bone_dark", 1001, "#bfb59c"),
        "steel_dark": paint("dk_steel_dark", 1003, "#2f526d"),
        "fabric_bone": tex("dk_fabric_bone", gc.half_res(cloth_surface(1005, "#cfc6ae")), normal_strength=1.2),
        "fabric_steel": tex("dk_fabric_steel", gc.half_res(cloth_surface(1007, "#5a7a92")), normal_strength=1.2),
        "chrome_bright": metal("dk_chrome", 1009, "#c4c8cc", 0.18, 1.0, wear=0.3),
        "plastic_dark": pt.plain("dk_plastic_dark", "#2a2d31", 0.45, 0.0),
        "plastic_bone": pt.plain("dk_plastic_bone", "#e2dccb", 0.4, 0.0),
        "enamel_steel": pt.plain("dk_enamel_steel", STEEL, 0.25, 0.0),
        "coffee": pt.plain("dk_coffee", "#2a1a10", 0.08, 0.0),
        "porcelain": pt.plain("dk_porcelain", "#efede6", 0.12, 0.0),
        "water": pt.plain("dk_water", "#4f7f86", 0.05, 0.0),
        # Settler Patchwork, beyond the containers'
        "ticking": tex("dk_ticking", gc.half_res(cloth_surface(1011, "#d8cfb8", stripe="#6f9e98", period=32, tuft=4)), normal_strength=1.6),
        "ticking_plain": tex("dk_ticking_plain", gc.half_res(cloth_surface(1013, "#d8cfb8", stripe="#6f9e98", period=32)), normal_strength=1.2),
        "cotton": tex("dk_cotton", gc.half_res(cloth_surface(1015, "#e6e0d0")), normal_strength=1.2),
        "cotton_mustard": tex("dk_cotton_mustard", gc.half_res(cloth_surface(1017, "#d9ad3a")), normal_strength=1.2),
        "cotton_brick": tex("dk_cotton_brick", gc.half_res(cloth_surface(1019, BRICK)), normal_strength=1.2),
        "cotton_sea": tex("dk_cotton_sea", gc.half_res(cloth_surface(1021, "#6f9e98")), normal_strength=1.2),
        "paper": tex("dk_paper", gc.half_res(paper_surface(1023, "#ece6d6")), normal_strength=0.4),
        "card": tex("dk_card", gc.half_res(paper_surface(1025, "#b08a5c", fibres=1.0)), normal_strength=0.5),
        "brick": paint("dk_brick", 1027, BRICK, under="#8a6644"),
        "sea_paint": paint("dk_sea", 1029, SEA, under="#4a4e54"),
        # site
        "concrete": tex("dk_concrete", gc.half_res(concrete_surface(1031, "#b9b4a8")), normal_strength=1.4),
        "concrete_break": tex("dk_concrete_break", gc.half_res(concrete_surface(1033, "#a39d90")), normal_strength=2.0),
        "rebar": tex("dk_rebar", gc.half_res(pt.rust_surface(1035, "#8a5232", "#5a3420", "#4a4644", wear=0.8, streaks=0.3)), normal_strength=1.0),
        "rubber": tex("dk_rubber", gc.half_res(lt.rubber_surface(1037)), normal_strength=1.5),
        # farm and shack
        "straw": tex("dk_straw", gc.half_res(straw_surface(1039, "#d4b25e")), normal_strength=2.0),
        "twine": tex("dk_twine", gc.half_res(cloth_surface(1041, "#8f7a52")), normal_strength=1.0),
        "soil": tex("dk_soil", gc.half_res(concrete_surface(1043, "#4a3626")), normal_strength=2.0),
        "grass": pt.plain("dk_grass", "#5d8c38", 0.8, 0.0),
        "grass_dry": pt.plain("dk_grass_dry", "#8a9a48", 0.85, 0.0),
        "leather": tex("dk_leather", gc.half_res(paper_surface(1045, "#6e4a30", fibres=1.5)), normal_strength=1.0),
        "lace": pt.plain("dk_lace", "#cfc3a4", 0.8, 0.0),
        "duck_paint": paint("dk_duck", 1047, "#e8c23a", under=WOOD),
        "duck_wing": paint("dk_duck_wing", 1049, "#d4a92e", under=WOOD),
        "beak_paint": paint("dk_beak", 1051, BRICK, under=WOOD),
        "burlap": tex("dk_burlap", gc.half_res(cloth_surface(1053, "#a68a5e")), normal_strength=2.0),
        "plastic_sea": pt.plain("dk_plastic_sea", SEA, 0.5, 0.0),
        "plastic_sea_dark": pt.plain("dk_plastic_sea_dark", "#3d7a74", 0.5, 0.0),
        "webbing": tex("dk_webbing", gc.half_res(cloth_surface(1055, MUSTARD)), normal_strength=1.0),
        "bottle": pt.plain("dk_bottle", "#f1ede2", 0.25, 0.0),
        "cyan_glow": pt.plain("dk_cyan_glow", "#7fe0ff", 0.3, 0.0, emission=CYAN, strength=4.0),
    })
    return m


def image_mat(name, image: Image.Image, rough=0.7, emission=0.0):
    """A material from a drawn image (labels, prints, screens); `emission` > 0 makes it glow in its own colours."""
    rgb = np.asarray(image.convert("RGB"), np.float32) / 255.0
    mat = lt.decal(name, rgb, rough)
    if emission > 0:
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def decal(g, center, u, v, half_u, half_v, mat, out=(0, 0, 0)):
    """A quad carrying a whole image, centred at `center`, across `u` (image right) and `v` (image up)."""
    c, u, v = Vector(center) + Vector(out), Vector(u).normalized(), Vector(v).normalized()
    corners = [tuple(c - u * half_u - v * half_v), tuple(c + u * half_u - v * half_v),
               tuple(c + u * half_u + v * half_v), tuple(c - u * half_u + v * half_v)]
    return quad(g, corners, mat)


def font(size, bold=True):
    import render_lift as rl
    return rl.font(size, bold)


# =====================================================================================================
# Shapes
# =====================================================================================================

def bolts(g, points, n, r=0.006, mat="bolt"):
    for p in points:
        dome(g, p, n, r, mat, segs=8)


def panel(g, c, half, axis, rail, rail_mat, panel_mat, bevel=0.006, inset=0.004, corner_mat=None):
    """A flat panel of `panel_mat` held in a rail frame of `rail_mat` (`rail` wide), lying across the two axes that
    are not `axis` (0 x, 1 y, 2 z); `half` is the whole panel's half size. Corner blocks in `corner_mat` if given."""
    c = Vector(c)
    others = [i for i in range(3) if i != axis]
    ph = list(half)
    ph[others[0]] -= rail * 0.5
    ph[others[1]] -= rail * 0.5
    ph[axis] = max(half[axis] - inset, 0.002)
    box(g, tuple(c), tuple(ph), panel_mat, bevel=min(bevel, ph[axis] * 0.8))
    for k, o in enumerate(others):
        other = others[1 - k]
        for s in (-1, 1):
            p = c.copy()
            p[o] += s * (half[o] - rail / 2)
            h = list(half)
            h[o] = rail / 2
            if corner_mat is not None:
                h[other] -= rail
            box(g, tuple(p), tuple(h), rail_mat, bevel=bevel)
    if corner_mat is not None:
        for s0, s1 in itertools.product((-1, 1), repeat=2):
            p = c.copy()
            p[others[0]] += s0 * (half[others[0]] - rail / 2)
            p[others[1]] += s1 * (half[others[1]] - rail / 2)
            h = [rail / 2 + 0.002] * 3
            h[axis] = half[axis] + 0.002
            box(g, tuple(p), tuple(h), corner_mat, bevel=bevel)


def perforated(g, c, half, mat, holes=8, face=(0, -1, 0), hole_half=(0.006, 0.014), along=2):
    """An upright (box) with a row of punched slots on the face looking along `face` (the Colony rack posts)."""
    box(g, c, half, mat, bevel=0.004)
    c, n = Vector(c), Vector(face)
    span = half[along] * 0.88
    a = [i for i in range(3) if n[i] != 0][0]
    for k in range(holes):
        t = -span + (2 * span) * (k + 0.5) / holes
        p = c.copy()
        p[along] += t
        p[a] += n[a] * (half[a] + 0.0004)
        h = [0.0, 0.0, 0.0]
        h[a] = 0.0006
        rest = [i for i in range(3) if i not in (a, along)][0]
        h[rest] = hole_half[0]
        h[along] = hole_half[1]
        box(g, tuple(p), tuple(h), "hole")


def foot(g, p, r, h, mat="rubber"):
    """A round rubber foot under a leg, its top at p."""
    p = Vector(p)
    cylinder(g, p - Vector((0, 0, h)), p, r, mat, segs=12)


# Letters the containers' stencil font lacks, in its 5 x 7 cells (added, never replacing one of its own).
for _ch, _rows in {
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "I": ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
}.items():
    fm.GLYPHS.setdefault(_ch, _rows)


def pixel_text(g, text, origin, u, v, px, mat, t=0.0008, gap=1):
    """The containers' stencil pixel font on a flat face, `origin` the text's centre, reading along `u` with `v` up."""
    o, u, v = Vector(origin), Vector(u).normalized(), Vector(v).normalized()
    n = u.cross(v)
    rot = Matrix((u, v, n)).transposed()
    for (x0, x1, row), w, h in fm.glyph_runs(text, gap):
        cx = (x0 + x1 + 1) / 2 - w / 2
        cy = h / 2 - row - 0.5
        box(g, tuple(o + u * cx * px + v * cy * px + n * t / 2), ((x1 - x0 + 1) * px / 2, px / 2, t / 2), mat, rot=rot)


# =====================================================================================================
# Build, export, icons and review
# =====================================================================================================

class Part:
    """One registry entry: the part's grid size (x across, y tall, z deep, in blocks), its builder
    build(mats) -> Geo or [(name, Geo)], and the review notes."""

    def __init__(self, size, build, note, name=None):
        self.size, self.build, self.note, self.name = size, build, note, name

    @property
    def half(self):
        x, y, z = self.size
        return (x * BLOCK / 2, z * BLOCK / 2, y * BLOCK / 2)


def build_root(pid, part, mats):
    name = "".join(w.capitalize() for w in pid.split("_"))
    root = bl.make_empty(name)
    out = part.build(mats)
    pieces = [("Body", out)] if isinstance(out, Geo) else out
    for piece in pieces:
        if isinstance(piece, bpy.types.Object):
            piece.parent = root
            continue
        bl.make_object(f"{name}{piece[0]}", piece[1], mats, root)
    return root


_ASSETS = {}


def asset(path, triangles=None, keep=None):
    """A model from a .glb (a crop, the Grazer's head) as one hidden mesh object in the part frame (Blender axes),
    every node's transform applied, decimated to about `triangles`; `keep(name)` picks the nodes (and their
    children) to keep. Loaded once per run; place copies with ``place``."""
    key = (str(path), triangles, keep)
    if key in _ASSETS:
        return _ASSETS[key]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    bpy.context.view_layer.update()

    def kept(o):
        while o is not None:
            if keep is None or keep(o.name):
                return True
            o = o.parent
        return False

    meshes = [o for o in new if o.type == "MESH" and kept(o)]
    for o in new:
        o.animation_data_clear()
    bpy.context.view_layer.update()
    data = []
    for o in meshes:
        me = o.data.copy()
        me.transform(o.matrix_world)
        data.append(me)
    for o in new:
        bpy.data.objects.remove(o)
    joined = bpy.data.objects.new(f"Asset_{Path(path).stem}", data[0])
    bpy.context.scene.collection.objects.link(joined)
    for me in data[1:]:
        o = bpy.data.objects.new("tmp", me)
        bpy.context.scene.collection.objects.link(o)
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        joined.select_set(True)
        bpy.context.view_layer.objects.active = joined
        bpy.ops.object.join()
    tris = sum(len(p.vertices) - 2 for p in joined.data.polygons)
    if triangles and tris > triangles:
        mod = joined.modifiers.new("dec", "DECIMATE")
        mod.ratio = triangles / tris
        bpy.context.view_layer.objects.active = joined
        bpy.ops.object.modifier_apply(modifier=mod.name)
    joined.hide_render = True
    joined.hide_set(True)
    _ASSETS[key] = joined
    return joined


def place(template, matrix, name=None):
    """A copy of an ``asset`` with `matrix` (4x4) baked into its vertices, ready to go under a part's root."""
    me = template.data.copy()
    me.transform(Matrix(matrix))
    ob = bpy.data.objects.new(name or template.name.replace("Asset_", ""), me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def glb_info(path):
    data = Path(path).read_bytes()
    ln, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            lo, hi = np.minimum(lo, acc["min"]), np.maximum(hi, acc["max"])
            tris += doc["accessors"][prim["indices"]]["count"] // 3
    return {"min": lo, "max": hi, "tris": tris, "kib": len(data) / 1024}


ICON_VIEW = gc.ICON_VIEW          # three-quarter from the front right, as the containers' icons


def icon_framing(half, view=ICON_VIEW):
    """The containers' icon framing: the ortho scale that frames the part's box as the Gas Container icon does."""
    hx, hy, hz = half
    v = Vector(view).normalized()
    side = v.cross(Vector((0, 0, 1))).normalized()
    up = side.cross(v)
    pts = [Vector((sx * hx, sy * hy, sz * hz)) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    w = max(p.dot(side) for p in pts) - min(p.dot(side) for p in pts)
    hh = max(p.dot(up) for p in pts) - min(p.dot(up) for p in pts)
    return 1.14 * max(w, hh)


def run(registry, title="Decor", extra_materials=()):
    """Builds every part of `registry` ({id: Part}, narrowed by ``--only a,b``), writes its model and icon, prints
    its triangles and bounds, fails if a model leaves its grid box or passes 10k triangles; ``--render OUT_DIR``
    also writes a review sheet per part and a batch lineup. `extra_materials` are callables that add a batch's own
    materials (drawn labels and prints) to the dict."""
    only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else None
    ids = [p for p in registry if only is None or p in only]
    bbe.reset_scene()
    mats = materials()
    for extra in extra_materials:
        extra(mats)
    roots, infos, bad = {}, {}, []
    for pid in ids:
        part = registry[pid]
        roots[pid] = root = build_root(pid, part, mats)
        path = MESHES / f"{pid}.glb"
        bbe.export(root, path, animations=False)
        i = infos[pid] = glb_info(path)
        lim = np.array(part.half)[[0, 2, 1]]
        inside = bool(np.all(i["min"] >= -lim - 1e-4) and np.all(i["max"] <= lim + 1e-4))
        if not inside or i["tris"] > 10000:
            bad.append(pid)
        print(f"wrote {path.relative_to(REPO)} ({i['kib']:.0f} KiB, {i['tris']} triangles) bounds "
              f"{np.round(i['min'], 3).tolist()} .. {np.round(i['max'], 3).tolist()} "
              f"{'inside' if inside else 'OUTSIDE'} +-{np.round(lim, 3).tolist()}")
    for pid in ids:
        icon = ICONS / f"{pid}_icon.png"
        bbe.render_icon(bbe.mesh_tree(roots[pid]), icon, view=ICON_VIEW, ortho=icon_framing(registry[pid].half))
    if "--render" in sys.argv:
        import render_decor
        render_decor.render_all(registry, roots, infos, Path(sys.argv[sys.argv.index("--render") + 1]), title)
    if bad:
        raise SystemExit(f"outside the grid box or over 10k triangles: {', '.join(bad)}")
    return infos
