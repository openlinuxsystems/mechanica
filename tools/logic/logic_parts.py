"""Shared kit for the audit's D2 logic and sound parts: the Horn, the Radio, the Timer and the Logic Gate.

The look is the part models' look (docs/art-direction.md), Salvager Tech like the Switch, the Button, the Controller
and the Sensor: framed gunmetal cases with small bevels, copper on the hardware, the logic teal on trim and cyan only
on what is powered. It reuses the shared part kit (``tools/part_kit``: materials, ``logic_plate``, screws, glows) and
adds:

* ``materials()``: the part kit's materials plus the decals (stencils, dial faces, the gate's mode pictures drawn by
  ``tools/icons/gate_icons.py``), a clear glass and the lit (emissive) decals.
* ``build_cube(mats, name, code)``: the logic cube the Timer and the Logic Gate share. One block (0.25 m) on the
  Switch's base plate: a framed gunmetal case (corner posts and rails round inset panels, a teal band), an inset
  top deck for the face, two teal-ringed link sockets on the front stencilled IN and OUT, vents and the model code
  on the back.
* ``bake_clip`` / ``hide``: clips keyed on location, rotation and scale; a lit glow is hidden by shrinking it into the
  case below its face (its node sits under the surface and its mesh is offset up to it).
* ``run``: export the .glb, render the icon (the part lit) and, with ``--render OUT_DIR``, the review sheet
  (``render_logic.py``).

The parts are drawn with their front towards Blender's +y, as the batch 4 kit draws them, and ``run`` turns the root
half a turn before it exports: the front then looks along the game's +Z, the side placement turns towards the player
(``BuildAim.Orient``), as the Switch's does.

Run the part scripts (``tools/logic/build_<part>.py``), not this file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE.parent / "icons"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image, ImageDraw, ImageFilter, ImageFont  # noqa: E402

import gate_icons  # noqa: E402

bl, lt, pt = K.bl, K.lt, K.pt
Geo, box, cylinder, ring, quad = K.Geo, K.box, K.cylinder, K.ring, K.quad
OFF = K.OFF
FPS = K.FPS
GATE_MODES = ("and", "or", "xor", "nand", "nor", "xnor")

# ---- The logic cube (metres; the block is -0.125..0.125, mounted on z = -0.125, front +y) ------------------------
FLOOR = -0.125
CASE = 0.1                    # half width of the case (corner posts and rails)
CORE = 0.094                  # half width of the inset panels
CASE_TOP = 0.085              # the top deck (the rails stand 4 mm above it)
SOCKET_Z = -0.008             # the link sockets on the front
SOCKET_X = 0.046
ICON_VIEW = (1.0, -1.25, 0.8)     # front right, from above, once the root is turned


# =====================================================================================================
# Decals
# =====================================================================================================

def font(size, bold=True):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", "LiberationSans-Bold.ttf"):
        for root in ("/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/truetype/liberation"):
            p = Path(root) / name
            if p.exists():
                return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def _rgb(im):
    return np.asarray(im.convert("RGB"), np.float32) / 255.0


def _hex(c):
    return tuple(int(c[i:i + 2], 16) for i in (1, 3, 5))


def stencil(text, w=192, h=64, bg="#2b2f35", ink="#d9d1bc", arrow=None):
    """A stencilled label plate: bone letters on graphite, an optional teal chevron (``"in"`` before the text points
    into the part, ``"out"`` after it points away)."""
    im = Image.new("RGB", (w, h), _hex(bg))
    d = ImageDraw.Draw(im)
    d.rectangle([2, 2, w - 3, h - 3], outline=tuple(int(v * 0.75) for v in _hex(ink)), width=2)
    f = font(int(h * 0.56))
    tw = d.textlength(text, font=f)
    chev = h * 0.42 if arrow else 0
    x = (w - tw - chev * 1.2) / 2 + (chev * 1.2 if arrow == "in" else 0)
    d.text((x, h * 0.18), text, fill=_hex(ink), font=f)
    if arrow:
        cx = x - chev * 0.75 if arrow == "in" else x + tw + chev * 0.55
        cy = h / 2
        d.polygon([(cx - chev * 0.35, cy - chev * 0.5), (cx + chev * 0.4, cy), (cx - chev * 0.35, cy + chev * 0.5)], fill=(63, 220, 234))
    return _rgb(im)


SCREEN = (11, 22, 26)         # a dark screen
SCREEN_INK = (120, 214, 222)  # the logic teal, light, as on the Button's lit ring


def timer_dial(size=256):
    """The Timer's round dial: twelve ticks round a dark screen (12 o'clock at the image's top), a teal ring."""
    s = size * 2
    im = Image.new("RGB", (s, s), SCREEN)
    d = ImageDraw.Draw(im)
    c = s / 2
    d.ellipse([c - s * 0.47, c - s * 0.47, c + s * 0.47, c + s * 0.47], outline=(40, 120, 128), width=6)
    for k in range(60):
        a = math.radians(90 - k * 6)
        major = k % 5 == 0
        r0, r1 = s * (0.36 if major else 0.40), s * 0.44
        col = SCREEN_INK if major else (52, 110, 118)
        d.line([(c + r0 * math.cos(a), c - r0 * math.sin(a)), (c + r1 * math.cos(a), c - r1 * math.sin(a))], fill=col,
               width=10 if major else 4)
    # the delay sector: a faint wedge from 12 o'clock, as a timer dial shows it
    d.pieslice([c - s * 0.3, c - s * 0.3, c + s * 0.3, c + s * 0.3], start=-90, end=0, fill=(18, 44, 50))
    return _rgb(im.resize((size, size), Image.LANCZOS))


def gate_face(mode, size=256):
    """The Logic Gate's screen: the mode's gate symbol (tools/icons/gate_icons.py) in the logic teal on a dark screen."""
    pic = gate_icons.draw(mode)
    # the menu's strokes, a little bolder so they read on a 12 cm screen from a few metres
    alpha = np.asarray(pic.getchannel("A").filter(ImageFilter.MaxFilter(5)), np.float32)[..., None] / 255.0
    bg = np.ones((pic.height, pic.width, 3), np.float32) * (np.asarray(SCREEN, np.float32) / 255.0)
    ink = np.asarray(SCREEN_INK, np.float32) / 255.0
    rgb = bg * (1 - alpha) + ink * alpha
    im = Image.fromarray((rgb * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    d.rectangle([4, 4, pic.width - 5, pic.height - 5], outline=(40, 120, 128), width=4)
    return _rgb(im.resize((size, size), Image.LANCZOS))


def radio_dial(lit, w=512, h=192):
    """The Radio's tuning scale: a band of ticks with the six station marks (1 to 6). Unlit: bone on a dark screen;
    lit: the dial's cyan backlight with dark ink."""
    bg, ink, faint = ((20, 170, 235), (6, 26, 36), (12, 80, 104)) if lit else ((16, 24, 28), (205, 198, 178), (90, 96, 98))
    im = Image.new("RGB", (w, h), bg)
    d = ImageDraw.Draw(im)
    d.rectangle([5, 5, w - 6, h - 6], outline=faint, width=4)
    x0, x1 = w * 0.1, w * 0.9
    for k in range(51):
        x = x0 + (x1 - x0) * k / 50
        tall = k % 10 == 0
        d.line([(x, h * 0.62), (x, h * (0.40 if tall else 0.52))], fill=ink if tall else faint, width=5 if tall else 3)
    f = font(int(h * 0.26))
    for k in range(6):
        x = x0 + (x1 - x0) * k / 5
        t = str(k + 1)
        d.text((x - d.textlength(t, font=f) / 2, h * 0.08), t, fill=ink, font=f)
    d.line([(x0, h * 0.64), (x1, h * 0.64)], fill=ink, width=4)
    small = font(int(h * 0.15), bold=False)
    for k, t in enumerate(("FM", "MHz")):
        d.text((w * (0.1 if k == 0 else 0.82), h * 0.74), t, fill=faint if not lit else ink, font=small)
    return _rgb(im)


def grille_dots(w=256, h=256):
    """The speaker cloth behind the Radio's slats: a fine dark perforation."""
    im = Image.new("RGB", (w, h), (20, 22, 24))
    d = ImageDraw.Draw(im)
    for y in range(6, h, 12):
        for x in range(6 + (y // 12 % 2) * 6, w, 12):
            d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(8, 9, 10))
    return _rgb(im)


# =====================================================================================================
# Materials
# =====================================================================================================

def decal_material(name, rgb, rough=0.5, emission=0.0):
    """A decal from an RGB image (0..1, top row first); with `emission` > 0 it also glows with its own colours."""
    mat = lt.decal(name, rgb, rough)
    if emission > 0:
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def materials():
    m = K.materials()
    # the metals with a gentler surface than the batch 4 kit's: the cube's and the radio's flat panels are larger than
    # the Controller's, and at full strength their worn-metal normals read as crumpled foil (the Switch and Button are
    # clean); the horn's bell is plain polished copper, as box-projected texture stretches on its flare
    for key, seed, color, rough, metal in (("gun", 403, "#5a6068", 0.44, 0.78), ("gun_dark", 405, "#3f444a", 0.5, 0.72),
                                           ("hull", 401, "#24282e", 0.5, 0.65), ("copper", 409, "#b8733a", 0.32, 0.95),
                                           ("copper_bright", 411, "#cc834e", 0.2, 1.0)):
        m[key] = lt.textured(f"d2_{key}", K.half_res(lt.metal_surface(seed, color, rough, metal, wear=0.6, edge_light=0.3)),
                             normal_strength=0.4)
    m["bell_copper"] = pt.plain("d2_bell_copper", "#b8733a", 0.28, 1.0)
    glass = pt.plain("d2_glass", "#a9cdd2", 0.04, 0.0)
    b = glass.node_tree.nodes.get("Principled BSDF")
    b.inputs["Alpha"].default_value = 0.22
    b.inputs["IOR"].default_value = 1.45
    glass.surface_render_method = "BLENDED"
    m["clear_glass"] = glass
    m["bone"] = pt.plain("d2_bone", "#d9d1bc", 0.45, 0.0)
    m["grille"] = decal_material("d2_grille", grille_dots(), 0.9)
    m["label_in"] = decal_material("d2_label_in", stencil("IN", arrow="in"), 0.6)
    m["label_out"] = decal_material("d2_label_out", stencil("OUT", arrow="out"), 0.6)
    m["code_timer"] = decal_material("d2_code_timer", stencil("TMR-2"), 0.6)
    m["code_gate"] = decal_material("d2_code_gate", stencil("LGC-6"), 0.6)
    m["code_horn"] = decal_material("d2_code_horn", stencil("HN-1", w=160, h=64), 0.6)
    m["code_radio"] = decal_material("d2_code_radio", stencil("CR-6", w=192, h=64, bg="#24282e"), 0.6)
    m["timer_dial"] = decal_material("d2_timer_dial", timer_dial(), 0.7, emission=0.5)
    for mode in GATE_MODES:
        m[f"gate_{mode}"] = decal_material(f"d2_gate_{mode}", gate_face(mode), 0.7, emission=0.5)
    m["radio_dial"] = decal_material("d2_radio_dial", radio_dial(False), 0.7)
    m["radio_dial_lit"] = decal_material("d2_radio_dial_lit", radio_dial(True), 0.6, emission=0.9)
    return m


# =====================================================================================================
# Shapes
# =====================================================================================================

def decal_quad(g, center, right, up, w, h, mat):
    """A flat w x h decal at `center`, the image's x along `right` and its y along `up` (faces right x up)."""
    c, r, u = Vector(center), Vector(right).normalized() * (w / 2), Vector(up).normalized() * (h / 2)
    quad(g, [tuple(c - r - u), tuple(c + r - u), tuple(c + r + u), tuple(c - r + u)], mat)


def uv_disc(g, center, r, mat, segs=48, up=(0, -1, 0), right=(-1, 0, 0)):
    """A flat round decal facing +z with the image mapped onto it (the image's top along `up`)."""
    c, u, rt = Vector(center), Vector(up), Vector(right)
    verts = [tuple(c)]
    for k in range(segs):
        a = 2 * math.pi * k / segs
        verts.append(tuple(c + (rt * math.cos(a) + u * math.sin(a)) * r))
    faces, uvs = [], []
    for k in range(segs):
        k1 = (k + 1) % segs
        a0, a1 = 2 * math.pi * k / segs, 2 * math.pi * k1 / segs
        faces.append((0, 1 + k, 1 + k1))
        uvs.append(((0.5, 0.5), (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0)),
                    (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1))))
    # make every triangle face +z
    n = (Vector(verts[1]) - c).cross(Vector(verts[2]) - c)
    if n.z < 0:
        faces = [(f[0], f[2], f[1]) for f in faces]
        uvs = [(t[0], t[2], t[1]) for t in uvs]
    return g.add(verts, faces, mat, uvs=uvs)


def link_socket(g, at, normal, finish):
    """A teal-ringed link socket (the link box's) flush on a face."""
    at, n = Vector(at), Vector(normal).normalized()
    cylinder(g, at - n * 0.003, at + n * 0.003, 0.0135, "teal", segs=20)
    cylinder(g, at - n * 0.002, at + n * 0.0036, 0.0085, "hole", segs=16)
    cylinder(g, at - n * 0.002, at + n * 0.0012, 0.0045, finish["trim"], segs=10)


def lathe_along(g, profile, mat, at, axis, steps=40):
    """``part_kit.lathe`` about local z, turned so local z runs along `axis` (+y or -y, +x ...) from `at`."""
    sub = Geo()
    K.lathe(sub, profile, mat, steps=steps)
    z = Vector(axis).normalized()
    m = z.to_track_quat("Z", "Y").to_matrix().to_4x4()
    m.translation = Vector(at)
    g.extend(sub, m)


def build_cube(mats, name, code):
    """The logic cube the Timer and the Logic Gate share. Returns (rig, body Geo); the part adds its face on the deck
    (z = CASE_TOP) and makes the Body object with ``finish_cube``."""
    f = K.FINISH[1]
    root = K.make_empty(name)
    g = Geo()
    top = K.logic_plate(g, FLOOR, (0.112, 0.112), f)
    zc, hz = (top + CASE_TOP) / 2, (CASE_TOP - top) / 2
    # inset panels, then the frame: four corner posts, rails round the top and the foot
    box(g, (0, 0, zc), (CORE, CORE, hz), "gun_dark", bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (sx * (CASE - 0.012), sy * (CASE - 0.012), zc + 0.002), (0.012, 0.012, hz + 0.002), "gun", bevel=0.004)
    for z, hh in ((CASE_TOP - 0.003, 0.007), (top + 0.008, 0.008)):
        for s in (-1, 1):
            box(g, (0, s * (CASE - 0.012), z), (CASE - 0.02, 0.012, hh), "gun", bevel=0.003)
            box(g, (s * (CASE - 0.012), 0, z), (0.012, CASE - 0.02, hh), "gun", bevel=0.003)
    # the teal band round the panels, under the sockets
    box(g, (0, 0, top + 0.03), (CORE + 0.0012, CORE + 0.0012, 0.0022), "teal", bevel=0.0008)
    # copper screws in the top rail's corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.screw(g, (sx * (CASE - 0.012), sy * (CASE - 0.012), CASE_TOP + 0.004), (0, 0, 1), 0.0055, f["bolt"])
    # front: the IN and OUT link sockets, stencilled under them (IN on the viewer's left)
    for sx, label in ((1, "label_in"), (-1, "label_out")):
        link_socket(g, (sx * SOCKET_X, CORE + 0.0005, SOCKET_Z + 0.018), (0, 1, 0), f)
        decal_quad(g, (sx * SOCKET_X, CORE + 0.0007, SOCKET_Z - 0.016), (-1, 0, 0), (0, 0, 1), 0.052, 0.0175, label)
    # back: three vent slots and the model code
    for k in range(3):
        box(g, (0, -CORE - 0.0004, 0.03 - k * 0.013), (0.052, 0.001, 0.0022), "hole")
    decal_quad(g, (0, -CORE - 0.0007, -0.03), (1, 0, 0), (0, 0, 1), 0.06, 0.02, code)
    # sides: a screw in each panel's corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                K.screw(g, (sx * (CORE + 0.0004), sy * 0.062, zc + sz * 0.055), (sx, 0, 0), 0.0042, f["bolt"])
    return {"root": root, "finish": f}, g


def deck_bezel_square(g, half, z0=CASE_TOP, h=0.006):
    """A square gunmetal bezel on the deck round a face `half` across, with a teal line inside it."""
    t = 0.008
    for s in (-1, 1):
        box(g, (0, s * (half + t / 2), z0 + h / 2), (half + t, t / 2, h / 2), "gun", bevel=0.0015)
        box(g, (s * (half + t / 2), 0, z0 + h / 2), (t / 2, half, h / 2), "gun", bevel=0.0015)
    for s in (-1, 1):
        box(g, (0, s * (half + 0.0008), z0 + 0.0022), (half + 0.0016, 0.0008, 0.0012), "teal")
        box(g, (s * (half + 0.0008), 0, z0 + 0.0022), (0.0008, half, 0.0012), "teal")
    box(g, (0, 0, z0 + 0.0008), (half, half, 0.0008), "screen")


# =====================================================================================================
# Glows and clips
# =====================================================================================================

SINK = 0.014    # how far below its surface a glow's node sits, so a shrunk glow hides inside the case


def sunk(make_geo, *args, offset=(0, 0, SINK), **kw):
    """A glow's geometry raised by `offset`, for a node placed `offset` below the surface it lights."""
    g = make_geo(*args, **kw)
    o = Vector(offset)
    g.v = [tuple(Vector(p) + o) for p in g.v]
    return g


def lit(objects, u):
    s = OFF + (1 - OFF) * u
    for ob in objects:
        ob.scale = (s, s, s)


def bake_clip(objects, name, seconds, apply):
    """One clip from apply(t), t from 0 to 1 over `seconds`, keyed on location, rotation and scale of `objects`
    every frame (for a ``_loop`` clip apply(1) equals apply(0), so it wraps without a jump)."""
    frames = max(2, int(round(seconds * FPS)))
    acts = {}
    for ob in objects:
        ob.animation_data_create()
        ob.rotation_mode = "XYZ"
        acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
    for fr in range(frames + 1):
        apply(fr / frames)
        for ob in objects:
            ob.keyframe_insert("location", frame=fr)
            ob.keyframe_insert("rotation_euler", frame=fr)
            ob.keyframe_insert("scale", frame=fr)
    for ob in objects:
        lt.linear_keys(acts[ob.name])
        K.push_track(ob, acts[ob.name], name)


def end_clips(objects):
    for ob in objects:
        if ob.animation_data:
            ob.animation_data.use_nla = False
            ob.animation_data.action = None


# =====================================================================================================
# Runner
# =====================================================================================================

def run(part, build, *, ortho, icon_target=(0, 0, 0), icon_state=None, title="", lines=(), half=(0.125, 0.125, 0.125)):
    """Builds the part with build(mats) -> rig, which bakes its own clips; writes ``meshes/parts/<part>.glb`` and
    ``textures/items/<part>_icon.png`` (posed by rig["states"][icon_state]); with ``--render OUT_DIR`` the review
    sheet (render_logic.py)."""
    K.reset_scene()
    mats = materials()
    rig = build(mats)
    root = rig["root"]
    root.rotation_euler = (0, 0, math.pi)      # front to the game's +Z (see the module notes)
    i = K.export(root, K.MESHES / f"{part}.glb", animations=True)
    K.report(K.MESHES / f"{part}.glb", i)
    K.strip_animation(root)
    for ob in [root, *root.children_recursive]:
        if ob.animation_data:
            ob.animation_data.use_nla = False
    if icon_state:
        rig["states"][icon_state]()
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", ICON_VIEW, ortho, icon_target)
    print(f"wrote {icon.relative_to(K.REPO)}")
    out = K.args_out()
    if out is not None:
        import render_logic
        render_logic.review(part, title or part, list(lines), rig, icon, i, out, half)
    return i
