"""Build the Gasoline gas tank and the Crude Oil canister, and their icons (Fuel and Oil Design Spec of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/gasoline.glb`` - the Gas Tank, a 2 x 2 x 2 block item (0.5 m cube centred on the origin,
  front -Y, Z up). A rounded jerrycan in gas-can red over a gunmetal base skirt, with gunmetal corner guards held by
  copper rivets, an X pressing on each side, a gunmetal label plate on the front (flame and GASOLINE), a teal level
  strip behind glass on the right side, a three-post carrying handle on top and a copper screw cap on a tilted spout at
  the front left corner.
* ``mods/vanilla/meshes/items/crude_oil.glb`` - the Oil Canister, a 2 x 2 x 2 block item: a squat oily gunmetal drum with
  rolled rims, two copper hoops, two lifting lugs on the shoulder, a big red screw cap on a copper collar in the
  middle of the lid, a front window onto the dark oil and a teal droplet and CRUDE OIL stencil, drips running down.
* ``mods/vanilla/textures/items/gasoline_icon.png`` and ``crude_oil_icon.png`` - the hotbar, inventory and
  Fabricator pictures, 256 x 256 on a clear background, three-quarter from the front right.

Both stay inside their 0.5 m cube, which is the collision box.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/fuel_oil/build_fuel_oil.py                  # writes the .glb files and the icons
    python tools/fuel_oil/build_fuel_oil.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, ring, dome, sweep = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.sweep

HALF = 0.25                  # the item's half size: 2 x 2 x 2 blocks
FLOOR = -HALF

# ---- Gas tank (metres) -----------------------------------------------------------------------------------------------
TANK_HALF = (0.212, 0.19, 0.19)      # the red body (the handle and cap fill the rest of the cube)
TANK_Z = FLOOR + 0.012 + TANK_HALF[2]
TANK_TOP = TANK_Z + TANK_HALF[2]
STRIP_Y = -0.085                     # the level strip's centre along Y on the +X side
STRIP_Z0, STRIP_Z1 = TANK_Z - 0.13, TANK_Z + 0.12
SPOUT_BASE = Vector((-0.115, -0.1, TANK_TOP - 0.01))
SPOUT_AXIS = Vector((-0.32, -0.45, 1.0)).normalized()

# ---- Oil canister (metres) -------------------------------------------------------------------------------------------
DRUM_R = 0.214
DRUM_Z0, DRUM_Z1 = FLOOR + 0.004, 0.17   # body bottom and lid; the cap and lugs fill the rest of the cube
HOOPS_Z = (-0.125, 0.05)
CAP_R = 0.082
WINDOW_DEG = 22.0                        # the oil window's bearing from +X (the stencil band runs round the front)


# =====================================================================================================
# Materials
# =====================================================================================================

def oily(surface, seed, amount):
    """Dark oil drips running down a surface (v is up in the box-projected UVs), glossier where wet."""
    albedo, height, rough, metal = surface
    drip = lt.fnoise(seed, 36.0, aniso=(1.0, 0.07))
    gate = np.clip(lt.fnoise(seed + 1, 5.0, aniso=(1.0, 0.3)) * 2.4 - 0.5, 0, 1)
    wet = np.clip((drip - 0.1) * 3.2, 0, 1) * gate * amount
    stain = np.clip(lt.fbm(seed + 2, 3.0) * 1.6 - 0.2, 0, 1) * 0.5 * amount
    oil = np.maximum(wet, stain)
    albedo = albedo * (1 - 0.7 * oil[..., None]) + np.asarray(lt.srgb("#140e08"))[None, None, :] * 0.7 * oil[..., None]
    return albedo, height + 0.3 * wet, rough * (1 - 0.6 * wet), metal * (1 - 0.5 * wet)


def half(surface):
    return gc.half_res(surface)


def make_materials():
    return {
        "hole": pt.plain("fo_hole", "#0c0d0f", 0.9, 0.2),
        "glass": gc.glass_material(),
        "teal": pt.plain("fo_teal", "#2b8a92", 0.32, 0.35),
        "liquid": pt.plain("fo_liquid", "#0e8577", 0.2, 0.0, emission="#13b5a2", strength=1.2),
        "oil": pt.plain("fo_oil", "#3a200c", 0.05, 0.0),
        "oil_sheen": pt.plain("fo_oil_sheen", "#8a5a26", 0.04, 0.2),
        "red": lt.textured("fo_red", half(lt.paint_surface(501, "#c0261c", "#4a4d52")), normal_strength=0.8),
        "red_dark": lt.textured("fo_red_dark", half(lt.paint_surface(503, "#8e1a14", "#3c3f43")), normal_strength=0.8),
        "gun": lt.textured("fo_gun", half(lt.metal_surface(505, "#55585d", 0.46, 0.75, wear=0.5, edge_light=0.3)), normal_strength=0.7),
        "gun_dark": lt.textured("fo_gun_dark", half(lt.metal_surface(507, "#3f4246", 0.52, 0.72, wear=0.7, edge_light=0.25)), normal_strength=0.7),
        "drum": lt.textured("fo_drum", half(oily(lt.metal_surface(509, "#34373b", 0.42, 0.7, wear=0.9, edge_light=0.3), 511, 1.0)),
                            normal_strength=0.8),
        "drum_rim": lt.textured("fo_drum_rim", half(oily(lt.metal_surface(513, "#4a4d52", 0.4, 0.8, wear=1.0, edge_light=0.4), 515, 0.6)),
                                normal_strength=0.7),
        "copper": lt.textured("fo_copper", half(lt.metal_surface(517, "#b5673a", 0.34, 0.95, wear=0.6, edge_light=0.35)), normal_strength=0.7),
        "copper_oily": lt.textured("fo_copper_oily", half(oily(lt.metal_surface(519, "#a85f36", 0.36, 0.95, wear=0.8, edge_light=0.35), 521, 0.7)),
                                   normal_strength=0.7),
        "cap_red": lt.textured("fo_cap_red", half(lt.paint_surface(523, "#c3281e", "#4a4d52")), normal_strength=0.8),
        "fuel_label": lt.decal("fo_fuel_label", fuel_label(), rough=0.55),
        "oil_label": lt.decal("fo_oil_label", oil_label(), rough=0.6),
    }


# =====================================================================================================
# Decals
# =====================================================================================================

def _flame(d, cx, cy, s, fill, inner):
    """A two-tongue flame, s pixels tall, its base centred on (cx, cy)."""
    d.polygon([(cx - 0.3 * s, cy), (cx - 0.36 * s, cy - 0.45 * s), (cx - 0.12 * s, cy - 0.28 * s), (cx, cy - s),
               (cx + 0.16 * s, cy - 0.5 * s), (cx + 0.3 * s, cy - 0.66 * s), (cx + 0.34 * s, cy - 0.2 * s), (cx + 0.3 * s, cy)], fill=fill)
    d.polygon([(cx - 0.14 * s, cy), (cx - 0.12 * s, cy - 0.3 * s), (cx + 0.02 * s, cy - 0.58 * s), (cx + 0.1 * s, cy - 0.34 * s),
               (cx + 0.16 * s, cy)], fill=inner)
    d.ellipse([cx - 0.3 * s, cy - 0.3 * s, cx + 0.3 * s, cy + 0.08 * s], fill=fill)
    d.ellipse([cx - 0.14 * s, cy - 0.2 * s, cx + 0.16 * s, cy + 0.04 * s], fill=inner)


def _grunge(img, seed, amount):
    a = np.asarray(img, dtype=np.float32) / 255.0
    h, w, _ = a.shape
    n = lt.fbm(seed, 6.0)
    n = np.asarray(Image.fromarray(((n - n.min()) / (np.ptp(n) + 1e-6) * 255).astype(np.uint8)).resize((w, h)), np.float32) / 255.0
    sc = lt.scratches(seed + 1, 60)
    sc = np.asarray(Image.fromarray((np.clip(sc, 0, 1) * 255).astype(np.uint8)).resize((w, h)), np.float32) / 255.0
    a = a * (1 - amount * 0.35 * n[..., None]) + amount * 0.3 * sc[..., None] * (0.55 - a)
    return np.clip(a, 0, 1)


def fuel_label():
    """The Gas Tank's front plate: a red flame in a teal-rimmed diamond and GASOLINE under it, on dark gunmetal."""
    w, h = 512, 384
    img = Image.new("RGB", (w, h), (52, 55, 59))
    d = ImageDraw.Draw(img)
    d.rectangle([10, 10, w - 11, h - 11], outline=(43, 138, 146), width=8)
    cx, cy, r = w // 2, 150, 112
    d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=(43, 138, 146))
    r2 = r - 16
    d.polygon([(cx, cy - r2), (cx + r2, cy), (cx, cy + r2), (cx - r2, cy)], fill=(30, 32, 35))
    _flame(d, cx, cy + 52, 120, (205, 44, 32), (240, 150, 60))
    import render_lift as rl  # noqa: PLC0415
    f = rl.font(66, True)
    text = "GASOLINE"
    d.text(((w - d.textlength(text, font=f)) / 2, 285), text, fill=(214, 216, 218), font=f)
    return _grunge(img, 531, 1.0)


def oil_label():
    """The Oil Canister's stencil band: a teal droplet with a hazard triangle and CRUDE OIL, on oily dark metal."""
    w, h = 1024, 256
    img = Image.new("RGB", (w, h), (40, 43, 46))
    d = ImageDraw.Draw(img)
    import render_lift as rl  # noqa: PLC0415
    teal = (54, 170, 178)
    # droplet
    cx, cy = 130, 140
    d.ellipse([cx - 62, cy - 30, cx + 62, cy + 94], fill=teal)
    d.polygon([(cx - 56, cy + 8), (cx, cy - 104), (cx + 56, cy + 8)], fill=teal)
    d.ellipse([cx - 30, cy + 8, cx - 10, cy + 54], fill=(40, 43, 46))
    f = rl.font(110, True)
    d.text((230, 64), "CRUDE OIL", fill=teal, font=f)
    # small hazard triangle on the right
    tx, ty = 920, 140
    d.polygon([(tx - 58, ty + 52), (tx, ty - 60), (tx + 58, ty + 52)], fill=teal)
    d.polygon([(tx - 40, ty + 40), (tx, ty - 36), (tx + 40, ty + 40)], fill=(40, 43, 46))
    d.rectangle([tx - 6, ty - 14, tx + 6, ty + 18], fill=teal)
    d.ellipse([tx - 7, ty + 24, tx + 7, ty + 36], fill=teal)
    a = _grunge(img, 541, 1.4)
    # stencil gaps: thin vertical breaks through the lettering
    for x in (300, 420, 560, 700, 790):
        a[60:200, x:x + 5] = np.asarray((40, 43, 46)) / 255.0
    return a


# =====================================================================================================
# Shared shapes
# =====================================================================================================

def arc_patch(g, r, a0, a1, z0, z1, mat, segs=16, uv=True):
    """A curved strip of a cylinder about Z (a label on a drum), facing out, with UVs 0..1 across and up."""
    verts, faces, uvs = [], [], []
    for k in range(segs + 1):
        a = a0 + (a1 - a0) * k / segs
        verts += [(r * math.cos(a), r * math.sin(a), z0), (r * math.cos(a), r * math.sin(a), z1)]
    for k in range(segs):
        i = 2 * k
        faces.append((i, i + 2, i + 3, i + 1))
        u0, u1 = k / segs, (k + 1) / segs
        uvs.append(((u0, 0), (u1, 0), (u1, 1), (u0, 1)))
    return g.add(verts, faces, mat, uvs=uvs if uv else None)


def torus(g, center, axis, R, r, mat, segs=32, tube=10):
    """A ring of round section (a rolled rim, a lifting eye) about `axis` through `center`."""
    c = Vector(center)
    u, w = bl.frame_of(axis)
    n = Vector(axis).normalized()
    verts, faces = [], []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        radial = u * math.cos(a) + w * math.sin(a)
        for j in range(tube):
            b = 2 * math.pi * j / tube
            verts.append(tuple(c + radial * (R + r * math.cos(b)) + n * r * math.sin(b)))
    for i in range(segs):
        i1 = (i + 1) % segs
        for j in range(tube):
            j1 = (j + 1) % tube
            faces.append((i * tube + j, i1 * tube + j, i1 * tube + j1, i * tube + j1))
    return g.add(verts, faces, mat)


def rivet(g, p, n, r=0.0075, mat="copper"):
    dome(g, p, n, r, mat, segs=10)


# =====================================================================================================
# Gas tank
# =====================================================================================================

def build_tank():
    g = Geo()
    hx, hy, hz = TANK_HALF
    # the red body, well rounded, on a gunmetal base skirt
    box(g, (0, 0, TANK_Z), TANK_HALF, "red", bevel=0.055)
    box(g, (0, 0, FLOOR + 0.022), (hx + 0.008, hy + 0.008, 0.022), "gun_dark", bevel=0.016)
    # a gunmetal seam band round the middle, as a welded can has
    box(g, (0, 0, TANK_Z + 0.06), (hx + 0.004, hy + 0.004, 0.012), "gun", bevel=0.008)
    # corner guards on the eight corners, each held by three copper rivets
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * (hx - 0.022), sy * (hy - 0.022), TANK_Z + sz * (hz - 0.024))
                box(g, c, (0.04, 0.04, 0.036), "gun", bevel=0.018)
                o = 0.041
                rivet(g, (c[0] + sx * o, c[1] - sy * 0.008, c[2] - sz * 0.004), (sx, 0, 0))
                rivet(g, (c[0] - sx * 0.008, c[1] + sy * o, c[2] - sz * 0.004), (0, sy, 0))
                rivet(g, (c[0] - sx * 0.008, c[1] - sy * 0.008, c[2] + sz * 0.037), (0, 0, sz))
    # X pressings on both sides: a raised panel with two crossing ribs
    for sx in (-1, 1):
        x = sx * (hx + 0.004)
        py, pz = (hy - 0.07, hz - 0.075)
        y0 = 0.035 if sx > 0 else 0.0          # the +X side leaves room for the level strip at the front
        box(g, (x, y0, TANK_Z - 0.012), (0.006, py - abs(y0) + 0.0, pz), "red_dark", bevel=0.012)
        ang = math.atan2(2 * pz, 2 * (py - abs(y0)))
        for s in (-1, 1):
            rot = Matrix.Rotation(s * ang, 3, "X")
            length = math.hypot(2 * pz, 2 * (py - abs(y0))) / 2 - 0.02
            box(g, (x + sx * 0.007, y0, TANK_Z - 0.012), (0.006, length, 0.012), "red", bevel=0.005, rot=rot)
        rivet(g, (x + sx * 0.013, y0, TANK_Z - 0.012), (sx, 0, 0), r=0.009)
    # the level strip on the +X side: a gunmetal frame, a dark well, glass; the teal column is its own node
    x = hx + 0.006
    zc, zh = (STRIP_Z0 + STRIP_Z1) / 2, (STRIP_Z1 - STRIP_Z0) / 2
    box(g, (x, STRIP_Y, zc), (0.01, 0.034, zh + 0.02), "gun", bevel=0.008)
    box(g, (x + 0.007, STRIP_Y, zc), (0.005, 0.02, zh), "hole")
    for k in range(5):
        z = STRIP_Z0 + (k + 1) * (STRIP_Z1 - STRIP_Z0) / 6
        box(g, (x + 0.0125, STRIP_Y + 0.024, z), (0.0015, 0.009, 0.0022), "copper")
    box(g, (x + 0.0125, STRIP_Y, zc), (0.0008, 0.02, zh), "glass")
    for z in (STRIP_Z0 - 0.012, STRIP_Z1 + 0.012):
        rivet(g, (x + 0.01, STRIP_Y, z), (1, 0, 0), r=0.006)
    # the front label plate (flame and GASOLINE) in a gunmetal frame, low on the front
    y = -(hy + 0.004)
    lw, lh, lz = 0.12, 0.09, TANK_Z - 0.058
    box(g, (0, y + 0.002, lz), (lw + 0.016, 0.006, lh + 0.016), "gun", bevel=0.008)
    bl.quad(g, [(-lw, y - 0.0045, lz - lh), (lw, y - 0.0045, lz - lh), (lw, y - 0.0045, lz + lh), (-lw, y - 0.0045, lz + lh)],
            "fuel_label")
    for sx in (-1, 1):
        for sz in (-1, 1):
            rivet(g, (sx * (lw + 0.008), y - 0.004, lz + sz * (lh + 0.008)), (0, -1, 0), r=0.006)
    # the carrying handle: three gunmetal posts and a bar along X, a copper grip over the middle
    hy0 = 0.075
    for px in (-0.12, 0.0, 0.12):
        box(g, (px, hy0, TANK_TOP + 0.022), (0.014, 0.022, 0.032), "gun", bevel=0.008)
    cylinder(g, (-0.145, hy0, TANK_TOP + 0.058), (0.145, hy0, TANK_TOP + 0.058), 0.016, "gun", segs=16)
    for x0, x1 in ((-0.105, -0.016), (0.016, 0.105)):
        cylinder(g, (x0, hy0, TANK_TOP + 0.058), (x1, hy0, TANK_TOP + 0.058), 0.02, "copper", segs=16)
        for k in range(1, 4):
            xr = x0 + (x1 - x0) * k / 4
            cylinder(g, (xr - 0.003, hy0, TANK_TOP + 0.058), (xr + 0.003, hy0, TANK_TOP + 0.058), 0.022, "gun_dark", segs=16)
    # the spout: a gunmetal neck tilted to the front left, a copper collar and the knurled copper screw cap
    b, n = SPOUT_BASE, SPOUT_AXIS
    cylinder(g, b - n * 0.03, b + n * 0.04, 0.042, "gun", segs=24)
    cylinder(g, b + n * 0.04, b + n * 0.05, 0.05, "copper", segs=24)
    cylinder(g, b + n * 0.05, b + n * 0.088, 0.047, "copper", segs=24)
    for k in range(12):
        a = 2 * math.pi * k / 12
        u, w = bl.frame_of(n)
        d = (u * math.cos(a) + w * math.sin(a)) * 0.047
        box(g, tuple(b + n * 0.069 + d), (0.005, 0.005, 0.017), "copper",
            rot=n.to_track_quat("Z", "Y").to_matrix() @ Matrix.Rotation(a, 3, "Z"))
    cylinder(g, b + n * 0.088, b + n * 0.094, 0.036, "gun_dark", segs=24)
    dome(g, b + n * 0.094, n, 0.012, "copper", segs=12)
    # a tether from the cap to the body
    sweep(g, [b + n * 0.06 + Vector((0.05, 0.0, 0.0)), Vector((-0.04, -0.12, TANK_TOP + 0.03)), Vector((0.0, -0.13, TANK_TOP + 0.004))],
          0.004, "gun_dark", segs=8)
    return g


def build_tank_level():
    """The teal fuel column in the level strip, its bottom at the origin; the game scales it on Z."""
    g = Geo()
    box(g, (0, 0, (STRIP_Z1 - STRIP_Z0) / 2), (0.003, 0.018, (STRIP_Z1 - STRIP_Z0) / 2), "liquid")
    return g


# =====================================================================================================
# Oil canister
# =====================================================================================================

def build_drum():
    g = Geo()
    r = DRUM_R
    # the body: straight wall with slightly rounded shoulders into the lid; the lid sits a little below the rim
    profile = [(0.0, DRUM_Z0 + 0.012), (r - 0.012, DRUM_Z0 + 0.012), (r, DRUM_Z0 + 0.03), (r, DRUM_Z1 - 0.03),
               (r - 0.01, DRUM_Z1 - 0.008), (r - 0.022, DRUM_Z1 - 0.012), (0.0, DRUM_Z1 - 0.012)]
    bbe.lathe(g, profile, "drum", steps=64)
    # rolled rims top and bottom
    torus(g, (0, 0, DRUM_Z0 + 0.016), (0, 0, 1), r - 0.002, 0.014, "drum_rim", segs=64)
    torus(g, (0, 0, DRUM_Z1 - 0.004), (0, 0, 1), r - 0.006, 0.013, "drum_rim", segs=64)
    # two copper hoops
    for z in HOOPS_Z:
        ring(g, z - 0.014, z + 0.014, r - 0.004, r + 0.01, "copper_oily", segs=64)
        torus(g, (0, 0, z), (0, 0, 1), r + 0.01, 0.006, "copper_oily", segs=64)
    # the stencil band between the hoops, round the front left, clear of the window
    z0, z1 = HOOPS_Z[0] + 0.03, HOOPS_Z[1] - 0.03
    arc_patch(g, r + 0.0015, math.radians(-136), math.radians(-16), z0, z1, "oil_label", segs=24)
    # the window on the right: a gunmetal frame, dark well, glass; the oil column is its own node
    a = math.radians(WINDOW_DEG)
    nrm = Vector((math.cos(a), math.sin(a), 0))
    rot = Matrix.Rotation(a + math.pi / 2, 3, "Z")
    zc = (HOOPS_Z[0] + HOOPS_Z[1]) / 2
    c = nrm * (r + 0.004)
    box(g, tuple(c + Vector((0, 0, zc))), (0.036, 0.012, 0.082), "gun", bevel=0.008, rot=rot)
    box(g, tuple(nrm * (r + 0.011) + Vector((0, 0, zc))), (0.022, 0.006, 0.068), "hole", rot=rot)
    box(g, tuple(nrm * (r + 0.0165) + Vector((0, 0, zc))), (0.022, 0.0008, 0.068), "glass", rot=rot)
    for sz in (-1, 1):
        rivet(g, tuple(nrm * (r + 0.016) + Vector((0, 0, zc + sz * 0.074))), nrm, r=0.006)
    # on the lid: a copper collar and a pressed ring
    top = DRUM_Z1 - 0.012
    ring(g, top - 0.002, top + 0.006, 0.12, 0.135, "drum_rim", segs=48)
    cylinder(g, (0, 0, top - 0.002), (0, 0, top + 0.016), CAP_R + 0.018, "copper_oily", segs=40)
    # two lifting lugs on the shoulder at +-X, each an eye on a plate
    for sx in (-1, 1):
        x = sx * (r - 0.05)
        box(g, (x, 0, top + 0.006), (0.03, 0.022, 0.008), "gun", bevel=0.005)
        torus(g, (x, 0, top + 0.034), (0, 1, 0), 0.022, 0.0075, "gun", segs=24, tube=8)
        for sy in (-1, 1):
            rivet(g, (x, sy * 0.016, top + 0.014), (0, 0, 1), r=0.005)
    # oil pooled round the collar
    cylinder(g, (0, 0, top), (0, 0, top + 0.0015), CAP_R + 0.04, "oil_sheen", segs=40, r1=CAP_R + 0.035)
    return g


def build_drum_cap():
    """The big red screw cap on the collar: a ribbed skirt, a domed top with a raised arrow (turn to open)."""
    g = Geo()
    h = 0.05
    cylinder(g, (0, 0, 0), (0, 0, h), CAP_R, "cap_red", segs=48)
    for k in range(24):
        a = 2 * math.pi * k / 24
        box(g, (CAP_R * math.cos(a), CAP_R * math.sin(a), h * 0.48), (0.006, 0.006, h * 0.42), "cap_red",
            bevel=0.002, rot=Matrix.Rotation(a, 3, "Z"))
    bbe.lathe(g, [(0.0, h - 0.001), (CAP_R - 0.004, h - 0.001), (CAP_R - 0.012, h + 0.012), (CAP_R - 0.03, h + 0.02),
                  (0.0, h + 0.022)], "cap_red", steps=48)
    ring(g, h + 0.016, h + 0.022, 0.022, CAP_R - 0.024, "gun_dark", segs=48)
    # the arrow: a raised red triangle pointing to the front
    z = h + 0.022
    tri = [(-0.03, 0.02), (0.03, 0.02), (0.0, -0.035)]
    verts = [(x, y, z) for x, y in tri] + [(x, y, z + 0.008) for x, y in tri]
    faces = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
    g.add(verts, faces, "red_dark")
    return g


def build_drum_level():
    g = Geo()
    box(g, (0, 0, 0.068), (0.02, 0.003, 0.068), "oil")
    box(g, (0, 0.0, 0.134), (0.02, 0.0034, 0.0025), "oil_sheen")
    return g


# =====================================================================================================
# Scene
# =====================================================================================================

def build_gasoline(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("Gasoline", location=offset)}
    bl.make_object("Tank", build_tank(), mats, rig["root"])
    rig["level"] = bl.make_object("Level", build_tank_level(), mats, rig["root"],
                                  (TANK_HALF[0] + 0.0155, STRIP_Y, STRIP_Z0))
    set_level(rig, 0.75)
    return rig


def build_crude_oil(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("CrudeOil", location=offset)}
    bl.make_object("Drum", build_drum(), mats, rig["root"])
    bl.make_object("Cap", build_drum_cap(), mats, rig["root"], (0, 0, DRUM_Z1 + 0.002))
    a = math.radians(WINDOW_DEG)
    zc = (HOOPS_Z[0] + HOOPS_Z[1]) / 2
    lv = rig["level"] = bl.make_object("Level", build_drum_level(), mats, rig["root"],
                                       (math.cos(a) * (DRUM_R + 0.0155), math.sin(a) * (DRUM_R + 0.0155), zc - 0.068))
    lv.rotation_euler = (0, 0, a + math.pi / 2)
    set_level(rig, 0.8)
    return rig


def set_level(rig, fill):
    rig["level"].scale = (1.0, 1.0, max(0.01, fill))


ICON_VIEW = (1.25, -1.6, 1.05)


def icon(rig, name):
    return bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / f"{name}_icon.png", view=ICON_VIEW, ortho=0.78,
                           target=(0.0, 0.0, -0.015))


def main():
    bbe.reset_scene()
    mats = make_materials()
    tank = build_gasoline(mats)
    drum = build_crude_oil(mats, offset=(3, 0, 0))
    bbe.export(tank["root"], MESHES / "gasoline.glb", animations=False)
    drum["root"].location = (0, 0, 0)
    tank["root"].location = (3, 0, 0)
    bbe.export(drum["root"], MESHES / "crude_oil.glb", animations=False)
    icon(drum, "crude_oil")
    drum["root"].location = (3, 0, 0)
    tank["root"].location = (0, 0, 0)
    icon(tank, "gasoline")
    for p in (MESHES / "gasoline.glb", MESHES / "crude_oil.glb", ICONS / "gasoline_icon.png", ICONS / "crude_oil_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_fuel_oil
        render_fuel_oil.render_all(tank, drum, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
