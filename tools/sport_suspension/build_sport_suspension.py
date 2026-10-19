"""Build the Sport Suspension, levels 1 to 5 (design spec of 2026-10-19).

One master body shared by every level, 2 blocks long (0.50 m) and 0.22 m across, lying along Blender +X:

* a gunmetal mounting flange at each end (chamfered square, four corner bolts, a boss with a bore on the outer face),
  a ribbed collar inboard of it and a spring seat ringed in copper;
* a damper through the middle: the shock body on the base end, a polished rod into the mount end, a rubber bump stop;
* the coil (5 turns) between the two seats.

The levels change finish and add small trim, never the envelope or the mounting (see ``LEVELS``): level 1 is a plain
matte teal coil on worn gunmetal; level 5 is a glowing cyan coil on black chrome with a drilled shroud, copper and
silver hardware and lit rings. Each flange carries one pip per level on its top flat so the level reads at a glance.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/sport_suspension/build_sport_suspension.py --render OUT_DIR   # review renders and icons into OUT_DIR

Units are metres; in Blender Z is up. The build is deterministic.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Matrix, Vector

LENGTH = 0.50
WIDTH = 0.22
HALF = LENGTH / 2
SAMPLES = int(os.environ.get("SUSP_SAMPLES", "64"))

# x positions along the part (mount end is +X; the base end mirrors it)
X_BOSS = HALF                  # outer face of the boss
X_FLANGE_OUT = HALF - 0.016    # outer face of the flange
X_FLANGE_IN = X_FLANGE_OUT - 0.034
X_COLLAR_IN = X_FLANGE_IN - 0.05
X_SEAT = X_COLLAR_IN - 0.012   # inner face of the spring seat
COIL_R = 0.074

# Each level: coil colour / finish, wire thickness, end-cap finish and the trim that appears.
LEVELS = {
    1: dict(coil=(0.02, 0.20, 0.22), coil_rough=0.55, coil_metal=0.5, wire=0.015, flake=0.0, coat=0.0, glow=0.0,
            cap=(0.20, 0.21, 0.22), cap_rough=0.62, cap_wear=1.0, rod=(0.62, 0.63, 0.64), rod_rough=0.35,
            body=(0.30, 0.31, 0.32), trim="copper", extra_rings=0, accent="copper", shroud=False, lit=0,
            bolts="cap", stop=(0.06, 0.06, 0.06), band=None),
    2: dict(coil=(0.0, 0.30, 0.33), coil_rough=0.3, coil_metal=0.75, wire=0.016, flake=1.0, coat=0.0, glow=0.0,
            cap=(0.30, 0.31, 0.33), cap_rough=0.32, cap_wear=0.35, rod=(0.78, 0.79, 0.80), rod_rough=0.15,
            body=(0.26, 0.27, 0.28), trim="copper", extra_rings=1, accent="copper", shroud=False, lit=0,
            bolts="cap", stop=(0.06, 0.06, 0.06), band=(0.02, 0.36, 0.38)),
    3: dict(coil=(0.0, 0.17, 0.30), coil_rough=0.15, coil_metal=0.45, wire=0.017, flake=0.4, coat=1.0, glow=0.0,
            cap=(0.11, 0.115, 0.125), cap_rough=0.34, cap_wear=0.2, rod=(0.85, 0.86, 0.87), rod_rough=0.08,
            body=(0.03, 0.03, 0.035), trim="bronze", extra_rings=1, accent="bronze", shroud=False, lit=0,
            bolts="bronze", stop=(0.86, 0.86, 0.84), band=(0.55, 0.36, 0.16)),
    4: dict(coil=(0.0, 0.36, 0.40), coil_rough=0.14, coil_metal=0.3, wire=0.018, flake=0.6, coat=1.0, glow=0.0,
            cap=(0.07, 0.072, 0.08), cap_rough=0.3, cap_wear=0.0, rod=(0.9, 0.9, 0.9), rod_rough=0.05,
            body=(0.02, 0.02, 0.025), trim="gold", extra_rings=2, accent="gold", shroud=True, lit=1,
            bolts="gold", stop=(0.86, 0.86, 0.84), band=None),
    5: dict(coil=(0.0, 0.42, 0.52), coil_rough=0.1, coil_metal=0.3, wire=0.019, flake=0.6, coat=1.0, glow=1.0,
            cap=(0.025, 0.026, 0.03), cap_rough=0.12, cap_wear=0.0, rod=(0.95, 0.95, 0.96), rod_rough=0.02,
            body=(0.012, 0.012, 0.016), trim="copper_bright", extra_rings=2, accent="silver", shroud=True, lit=2,
            bolts="silver", stop=(0.05, 0.6, 0.65), band=None),
}

TRIM = {
    "copper": ((0.72, 0.36, 0.20), 0.35),
    "copper_bright": ((0.95, 0.50, 0.30), 0.12),
    "bronze": ((0.62, 0.42, 0.20), 0.3),
    "gold": ((0.95, 0.62, 0.30), 0.15),
    "silver": ((0.92, 0.92, 0.94), 0.08),
}
GLOW = (0.1, 0.95, 1.0)


# =====================================================================================================
# Materials
# =====================================================================================================
def _principled(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    return m, m.node_tree.nodes["Principled BSDF"], m.node_tree


def metal(name, color, rough, metallic=1.0, wear=0.0, coat=0.0, flake=0.0, glow=0.0, emission=None):
    m, b, t = _principled(name)
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = 0.03
    tc = t.nodes.new("ShaderNodeTexCoord")
    if wear > 0:
        # worn, cast-metal look: blotchy roughness and a fine bump
        n = t.nodes.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = 60
        n.inputs["Detail"].default_value = 8
        t.links.new(tc.outputs["Object"], n.inputs["Vector"])
        mr = t.nodes.new("ShaderNodeMapRange")
        mr.inputs["To Min"].default_value = max(0.0, rough - 0.18 * wear)
        mr.inputs["To Max"].default_value = min(1.0, rough + 0.18 * wear)
        t.links.new(n.outputs["Fac"], mr.inputs["Value"])
        t.links.new(mr.outputs["Result"], b.inputs["Roughness"])
        bump = t.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.12 * wear
        bump.inputs["Distance"].default_value = 0.0006
        t.links.new(n.outputs["Fac"], bump.inputs["Height"])
        t.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    if flake > 0:
        # metallic flake: tiny cells that vary brightness and roughness under the clear coat
        v = t.nodes.new("ShaderNodeTexVoronoi")
        v.inputs["Scale"].default_value = 600
        t.links.new(tc.outputs["Object"], v.inputs["Vector"])
        mix = t.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.inputs["A"].default_value = (*[c * 0.75 for c in color], 1)
        mix.inputs["B"].default_value = (*[min(1, c * 1.35 + 0.04) for c in color], 1)
        sep = t.nodes.new("ShaderNodeSeparateColor")
        t.links.new(v.outputs["Color"], sep.inputs["Color"])
        fac = t.nodes.new("ShaderNodeMath")
        fac.operation = "MULTIPLY"
        fac.inputs[1].default_value = flake
        t.links.new(sep.outputs["Red"], fac.inputs[0])
        t.links.new(fac.outputs["Value"], mix.inputs["Factor"])
        t.links.new(mix.outputs["Result"], b.inputs["Base Color"])
    if emission is not None:
        b.inputs["Emission Color"].default_value = (*emission, 1)
        b.inputs["Emission Strength"].default_value = glow
    return m


def plain(name, color, rough, emission=None, strength=0.0):
    m, b, _ = _principled(name)
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if emission is not None:
        b.inputs["Emission Color"].default_value = (*emission, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m


# =====================================================================================================
# Mesh helpers (all built along X)
# =====================================================================================================
def _object(name, bm, mat, parent, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = parent
    return ob


def _smooth_sides(bm, axis=Vector((1, 0, 0))):
    for f in bm.faces:
        f.smooth = abs(f.normal.dot(axis)) < 0.5


def cylinder(name, r, x0, x1, mat, parent, segs=48, y=0.0, z=0.0, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=abs(x1 - x0))
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    bmesh.ops.translate(bm, verts=bm.verts, vec=((x0 + x1) / 2, y, z))
    bm.normal_update()
    _smooth_sides(bm)
    me = _object(name, bm, mat, parent)
    for p in me.data.polygons:
        p.use_smooth = abs(p.normal.x) < 0.5
    return me


def tube(name, points, r, mat, parent, segs=16, closed=False):
    """Sweep a circle of radius r along points (a list of Vectors)."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for i, p in enumerate(points):
        a = points[(i - 1) % n] if (closed or i > 0) else points[i]
        c = points[(i + 1) % n] if (closed or i < n - 1) else points[i]
        tng = (c - a).normalized()
        ref = Vector((1, 0, 0)) if abs(tng.x) < 0.9 else Vector((0, 0, 1))
        u = tng.cross(ref).normalized()
        w = tng.cross(u).normalized()
        rings.append([bm.verts.new(p + r * (math.cos(2 * math.pi * k / segs) * u + math.sin(2 * math.pi * k / segs) * w))
                      for k in range(segs)])
    last = n if closed else n - 1
    for i in range(last):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(segs):
            bm.faces.new((r0[k], r0[(k + 1) % segs], r1[(k + 1) % segs], r1[k]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _object(name, bm, mat, parent)


def ring(name, radius, minor, x, mat, parent, segs=64):
    pts = [Vector((x, radius * math.cos(2 * math.pi * i / segs), radius * math.sin(2 * math.pi * i / segs))) for i in range(segs)]
    return tube(name, pts, minor, mat, parent, segs=12, closed=True)


def prism(name, outline, x0, x1, mat, parent, bevel=0.003):
    """Extrude a YZ outline (list of (y, z)) from x0 to x1, with a small bevel."""
    bm = bmesh.new()
    vs = [bm.verts.new((x0, y, z)) for y, z in outline]
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.x = x1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _object(name, bm, mat, parent, smooth=False)
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = bevel
    mod.segments = 2
    mod.limit_method = "ANGLE"
    return ob


def box(name, size, center, mat, parent, bevel=0.002):
    sx, sy, sz = size
    cx, cy, cz = center
    outline = [(cy - sy / 2, cz - sz / 2), (cy + sy / 2, cz - sz / 2), (cy + sy / 2, cz + sz / 2), (cy - sy / 2, cz + sz / 2)]
    return prism(name, outline, cx - sx / 2, cx + sx / 2, mat, parent, bevel)


def chamfered_square(half, chamfer):
    h, c = half, chamfer
    return [(-h + c, -h), (h - c, -h), (h, -h + c), (h, h - c), (h - c, h), (-h + c, h), (-h, h - c), (-h, -h + c)]


# =====================================================================================================
# Build one level
# =====================================================================================================
def build(level: int, root_name=None):
    L = LEVELS[level]
    root = bpy.data.objects.new(root_name or f"SportSuspension{level}", None)
    bpy.context.scene.collection.objects.link(root)
    tag = f"L{level}"
    cap = metal(f"cap{tag}", L["cap"], L["cap_rough"], wear=L["cap_wear"], coat=0.6 if level >= 4 else 0.0)
    trim_c, trim_r = TRIM[L["trim"]]
    trim = metal(f"trim{tag}", trim_c, trim_r)
    acc_c, acc_r = TRIM[L["accent"]]
    accent = metal(f"accent{tag}", acc_c, acc_r)
    bolt_mat = cap if L["bolts"] == "cap" else metal(f"bolt{tag}", *TRIM[L["bolts"]])
    coil = metal(f"coil{tag}", L["coil"], L["coil_rough"], metallic=L["coil_metal"], coat=0.5 * L["coat"], flake=L["flake"],
                 glow=0.25 * L["glow"], emission=(0.0, 0.75, 0.9) if L["glow"] else None)
    if level >= 4:
        cb = coil.node_tree.nodes["Principled BSDF"]
        cb.inputs["Thin Film Thickness"].default_value = 300 if level == 4 else 420
        cb.inputs["Thin Film IOR"].default_value = 1.45
    rod = metal(f"rod{tag}", L["rod"], L["rod_rough"])
    body = metal(f"body{tag}", L["body"], 0.25 if level >= 3 else 0.45, metallic=0.6 if level >= 3 else 1.0,
                 coat=0.8 if level >= 3 else 0.0, wear=0.5 if level == 1 else 0.0)
    dark = plain(f"bore{tag}", (0.01, 0.01, 0.012), 0.6)
    stop = plain(f"stop{tag}", L["stop"], 0.55, emission=GLOW if level == 5 else None, strength=1.5)
    lit = plain(f"lit{tag}", GLOW, 0.3, emission=GLOW, strength=6.0)
    band = metal(f"band{tag}", L["band"], 0.3, metallic=0.8, coat=0.5) if L["band"] else None

    for side in (1, -1):
        s = side
        nm = "Mount" if s > 0 else "Base"

        def X(x):
            return s * x
        # flange
        prism(f"{nm}Flange", chamfered_square(WIDTH / 2, 0.035), X(X_FLANGE_IN), X(X_FLANGE_OUT), cap, root, 0.004)
        # corner lugs and bolts on the inboard face
        for y in (-1, 1):
            for z in (-1, 1):
                p = (0.078 * y, 0.078 * z)
                cylinder(f"{nm}Washer", 0.016, X(X_FLANGE_IN), X(X_FLANGE_IN - 0.004), cap, root, 24, *p)
                cylinder(f"{nm}Bolt", 0.0115, X(X_FLANGE_IN - 0.004), X(X_FLANGE_IN - 0.013), bolt_mat, root, 6, *p)
                cylinder(f"{nm}BoltOut", 0.0105, X(X_FLANGE_OUT), X(X_FLANGE_OUT + 0.006), bolt_mat, root, 6, *p)
        # boss with bore on the outer face
        cylinder(f"{nm}Boss", 0.058, X(X_FLANGE_OUT), X(X_BOSS), cap, root)
        cylinder(f"{nm}Bore", 0.021, X(X_BOSS - 0.004), X(X_BOSS + 0.0005), dark, root, 32)
        ring(f"{nm}BossRing", 0.058, 0.0035, X(X_BOSS - 0.004), accent if level >= 2 else cap, root)
        # collar (ribbed) or drilled shroud
        if L["shroud"]:
            cylinder(f"{nm}Shroud", 0.093, X(X_COLLAR_IN), X(X_FLANGE_IN), cap, root, 64)
            rows = 3
            for row in range(rows):
                xx = X_COLLAR_IN + 0.011 + row * 0.0135
                for k in range(14):
                    a = 2 * math.pi * (k + 0.5 * (row % 2)) / 14
                    hole_y, hole_z = 0.093 * math.cos(a), 0.093 * math.sin(a)
                    bm = bmesh.new()
                    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.0058, radius2=0.0058, depth=0.004)
                    rot = Vector((0, math.cos(a), math.sin(a))).to_track_quat("Z", "X").to_matrix()
                    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=rot)
                    bmesh.ops.translate(bm, verts=bm.verts, vec=(X(xx), hole_y, hole_z))
                    _object(f"{nm}Hole", bm, lit if (L["lit"] >= 2 and row == 1) else dark, root)
            ring(f"{nm}ShroudLip", 0.093, 0.004, X(X_COLLAR_IN + 0.002), accent, root)
        else:
            collar = band if level == 2 else cap
            cylinder(f"{nm}Collar", 0.082, X(X_COLLAR_IN), X(X_FLANGE_IN), collar, root, 64)
            for k in range(16):
                a = 2 * math.pi * k / 16
                rib = box(f"{nm}Rib", (0.036, 0.012, 0.012), (X(X_COLLAR_IN + 0.025), 0, 0), collar, root, 0.0015)
                rib.rotation_euler = (a, 0, 0)
                rib.location = (0, 0, 0)
                for v in rib.data.vertices:
                    v.co.z += 0.083
            if band is not None and level != 2:
                cylinder(f"{nm}Band", 0.0835, X(X_COLLAR_IN + 0.006), X(X_COLLAR_IN + 0.014), band, root, 64)
        # spring seat, copper ring(s)
        cylinder(f"{nm}Seat", 0.094, X(X_SEAT), X(X_COLLAR_IN), cap, root, 64)
        ring(f"{nm}SeatRing", 0.094, 0.0045, X(X_SEAT + 0.006), trim, root)
        for e in range(L["extra_rings"]):
            ring(f"{nm}ExtraRing", 0.0825 if not L["shroud"] else 0.093, 0.0035,
                 X(X_FLANGE_IN - 0.006 - e * 0.012) if not L["shroud"] else X(X_FLANGE_IN - 0.004 - e * 0.0), trim, root)
            if L["shroud"]:
                break
        if L["lit"] >= 1:
            ring(f"{nm}GlowRing", 0.094, 0.0032, X(X_SEAT + 0.0015), lit, root)
        # level pips on the flange's top flat
        for i in range(level):
            yy = (i - (level - 1) / 2) * 0.016
            pip = cylinder(f"{nm}Pip", 0.0045, 0, 0.003, lit if L["lit"] else accent, root, 16)
            pip.rotation_euler = (0, math.radians(-90), 0)
            pip.location = (X((X_FLANGE_IN + X_FLANGE_OUT) / 2), yy, WIDTH / 2 - 0.0005)

    # damper: shock body on the base end, rod into the mount end, bump stop
    x_seat = X_SEAT
    cylinder("ShockBody", 0.04, -x_seat, 0.03, body, root, 48)
    ring("ShockLip", 0.04, 0.004, 0.028, trim, root)
    if L["lit"] >= 1:
        for xx in ((-0.03,) if L["lit"] == 1 else (-0.06, -0.03)):
            cylinder("ShockGlow", 0.0412, xx, xx + 0.006, lit, root, 48)
    cylinder("Rod", 0.019, 0.03, x_seat, rod, root, 40)
    cylinder("BumpStop", 0.03, x_seat - 0.03, x_seat, stop, root, 40)
    if level >= 3:
        cylinder("BumpStop2", 0.03, x_seat - 0.05, x_seat - 0.038, stop, root, 40)

    # coil: 5.5 turns between the seats, closed (flat) end turns
    turns, n = 5.0, 600
    x0, x1 = -x_seat + L["wire"], x_seat - L["wire"]
    pts = []
    for i in range(n + 1):
        t = i / n
        ang = 2 * math.pi * turns * t
        # pitch eases at both ends so the end turns sit flat on the seats
        u = t
        ease = 0.06
        if t < ease:
            u = ease * (t / ease) ** 2 * 0.5
        elif t > 1 - ease:
            u = 1 - ease * ((1 - t) / ease) ** 2 * 0.5
        else:
            u = ease * 0.5 + (t - ease) * (1 - ease) / (1 - 2 * ease)
        pts.append(Vector((x0 + (x1 - x0) * u, COIL_R * math.cos(ang), COIL_R * math.sin(ang))))
    tube("Coil", pts, L["wire"], coil, root, segs=20)
    return root


# =====================================================================================================
# Review renders
# =====================================================================================================
def render_review(out: Path):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    rl.SAMPLES = SAMPLES
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ground = rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    roots = {lv: build(lv) for lv in LEVELS}
    for r in roots.values():
        r.location.z = WIDTH / 2

    def show(only):
        for lv, r in roots.items():
            for ob in [r, *r.children_recursive]:
                ob.hide_render = lv != only

    tq = (0.62, -0.78, 0.52)
    shots = {}
    for lv in LEVELS:
        show(lv)
        cam = rl.camera(f"tq{lv}", tq, (0, 0, WIDTH / 2), ortho=0.64)
        shots[lv] = rl.render(cam, out / f"sport_suspension_{lv}.png", 900, 900)
    # level 1 and level 5 multi-views (side, top, end)
    views = {}
    for lv in (1, 5):
        show(lv)
        for name, loc, tgt, scale in (("side", (0, -2, WIDTH / 2), (0, 0, WIDTH / 2), 0.62),
                                      ("top", (0, -0.0001, 2), (0, 0, WIDTH / 2), 0.62),
                                      ("end", (2, 0, WIDTH / 2), (0, 0, WIDTH / 2), 0.34)):
            cam = rl.camera(f"{name}{lv}", loc, tgt, ortho=scale)
            w = 900 if name != "end" else 500
            h = 500
            views[(lv, name)] = (rl.render(cam, out / f"sport_suspension_{lv}_{name}.png", w, h), cam, w, h)
    # hotbar icons: clear background, three-quarter, fitted
    ground.hide_render = True
    sc.render.film_transparent = True
    icons = {}
    for lv in LEVELS:
        show(lv)
        cam = rl.camera(f"icon{lv}", (0.55, -0.72, 0.62), (0, 0, WIDTH / 2), ortho=0.6)
        p = rl.render(cam, out / f"sport_suspension_{lv}_icon_raw.png", 512, 512)
        im = Image.open(p).convert("RGBA")
        bbox = im.getbbox()
        im = im.crop(bbox)
        side = max(im.size)
        pad = int(side * 0.06)
        sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
        sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2))
        icons[lv] = sq.resize((256, 256), Image.LANCZOS)
        icons[lv].save(out / f"sport_suspension_{lv}_icon.png")
        os.remove(p)

    # sheet
    F = rl.font
    INK = rl.INK
    W = 5 * 760 + 120
    sheet = Image.new("RGB", (W, 2520), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "SPORT SUSPENSION - LEVELS 1 TO 5 (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "Same body at every level: 2 blocks long (0.50 m), 0.22 m across, mounting flanges on both ends. "
           "Each level changes finish and trim; the pips on top of each flange show the level.", fill=INK, font=F(24))
    notes = {
        1: ["Matte teal coil, lightest wire", "Worn cast gunmetal ends", "Copper seat rings, 1 pip", "Stiffness 4"],
        2: ["Brighter teal, metallic flake", "Polished ends, teal-painted collars", "Extra copper rings, copper boss", "Stiffness 7"],
        3: ["Deep glossy cyan-teal coil", "Black shock body, white bump stops", "Bronze bolts, band and rings", "Stiffness 10"],
        4: ["Electric teal, iridescent edge sheen", "Dark gunmetal, drilled shroud", "Gold-copper hardware, lit ring", "Stiffness 13"],
        5: ["Luminous cyan coil, faint glow", "Black chrome ends, drilled shroud", "Silver bolts, bright copper, lit", "Stiffness 20"],
    }
    for i, lv in enumerate(LEVELS):
        x = 60 + i * 760
        im = Image.open(shots[lv]).convert("RGB").resize((740, 740), Image.LANCZOS)
        sheet.paste(im, (x, 190))
        d.rectangle([x, 190, x + 739, 929], outline=(190, 190, 190), width=2)
        d.text((x + 16, 202), f"LEVEL {lv}", fill=INK, font=F(34, True))
        for k, line in enumerate(notes[lv]):
            d.text((x + 10, 946 + k * 32), line, fill=INK, font=F(24))
        ic = icons[lv]
        tile = Image.new("RGB", (180, 180), (40, 42, 46))
        tile.paste(ic.resize((170, 170), Image.LANCZOS), (5, 5), ic.resize((170, 170), Image.LANCZOS))
        sheet.paste(tile, (x + 280, 1090))
        d.text((x + 236, 1276), "hotbar icon (256 px)", fill=INK, font=F(20))
    d.text((60, 1340), "LEVEL 1 AND LEVEL 5 - SIDE, TOP AND END VIEWS", fill=INK, font=F(30, True))
    y = 1390
    for lv in (1, 5):
        xx = 60
        for name in ("side", "top", "end"):
            p, cam, w, h = views[(lv, name)]
            im = Image.open(p).convert("RGB")
            scale = 0.95
            im = im.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
            sheet.paste(im, (xx, y))
            dd = ImageDraw.Draw(sheet)
            dd.rectangle([xx, y, xx + im.width - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
            dd.text((xx + 12, y + 10), f"L{lv} {name.upper()}", fill=INK, font=F(24, True))
            if name == "side":
                a = rl.project(cam, (-HALF, -0.2, 0.0), w, h)
                b = rl.project(cam, (HALF, -0.2, 0.0), w, h)
                rl.dim_line(dd, (xx + a[0] * scale, y + a[1] * scale + 8), (xx + b[0] * scale, y + b[1] * scale + 8),
                            "50 cm (2 blocks)", F(22))
                a = rl.project(cam, (HALF + 0.02, 0, 0), w, h)
                b = rl.project(cam, (HALF + 0.02, 0, WIDTH), w, h)
                rl.dim_line(dd, (xx + a[0] * scale, y + a[1] * scale), (xx + b[0] * scale, y + b[1] * scale), "22 cm",
                            F(22), vertical=True)
            xx += im.width + 30
        y += 520
    sheet.save(out / "review_sheet.png")
    strip = Image.new("RGBA", (5 * 256 + 6 * 16, 288), (40, 42, 46, 255))
    for i, lv in enumerate(LEVELS):
        strip.paste(icons[lv], (16 + i * 272, 16), icons[lv])
    strip.save(out / "hotbar_icons.png")
    print(f"rendered the review sheet into {out}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--render" in args:
        render_review(Path(args[args.index("--render") + 1]))
