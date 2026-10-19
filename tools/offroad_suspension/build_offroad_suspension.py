"""Build the Off-Road Suspension, levels 1 to 5 (design spec of 2026-10-18).

One master body shared by every level, 3 blocks long (0.75 m) and 0.24 m across, lying along Blender +X: a round
gunmetal flange at each end (a ring of bolts, a boss with a bore on the outer face), a slotted lower housing on the
base end and a slimmer slotted sleeve on the mount end that telescopes into it, with copper bands where they meet, and a
long teal coil and damper inside, seen through the slots.

The levels change finish and add trim, never the envelope or the mounting (see ``LEVELS``): level 1 is clean gunmetal
with two copper bands; level 2 adds reinforcing bands; level 3 vents, bronze bolts and a glossier coil; level 4 riveted
plates, gold accents, a tighter coil and lit rings; level 5 black chrome, heavy-duty flanges and a glowing coil. Pips
on each flange show the level, as on the Sport Suspension.

What it writes: ``mods/vanilla/meshes/parts/offroad_suspension_<level>.glb`` and
``mods/vanilla/textures/items/offroad_suspension_<level>_icon.png`` for levels 1 to 5, with the same three nodes as the
Sport Suspension's models: ``Stator`` (the base flange), ``mount`` (the wheel flange, moved with what is attached there)
and ``mountFlex`` (housing, sleeve, coil and damper, skewed by the game to reach the moving flange).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/offroad_suspension/build_offroad_suspension.py                    # writes the .glb files and the icons
    python tools/offroad_suspension/build_offroad_suspension.py --render OUT_DIR   # review renders and sheet into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result.

Units are metres; in Blender Z is up. The build is deterministic.
"""

from __future__ import annotations

import math
import os
import shutil
import sys
import tempfile
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "sport_suspension"))
import build_sport_suspension as sp  # noqa: E402  (shared materials and mesh helpers)

LENGTH = 0.75
HALF = LENGTH / 2
WIDTH = 0.24
SAMPLES = int(os.environ.get("SUSP_SAMPLES", "64"))

X_BOSS = HALF
X_FLANGE_OUT = HALF - 0.016
X_FLANGE_IN = X_FLANGE_OUT - 0.032
R_FLANGE = 0.118
R_LOWER = 0.100       # the base end's slotted housing
R_UPPER = 0.086       # the mount end's slotted sleeve
X_MEET = -0.03        # where the sleeve goes into the housing
COIL_R = 0.068

LEVELS = {
    1: dict(coil=(0.02, 0.20, 0.22), coil_rough=0.55, coil_metal=0.5, wire=0.0145, turns=7.5, flake=0.0, coat=0.0,
            glow=0.0, cap=(0.20, 0.21, 0.22), cap_rough=0.6, wear=1.0, trim="copper", bands=0, vents=False,
            plates=False, bolts="cap", lit=0, heavy=False, rod=(0.62, 0.63, 0.64), body=(0.30, 0.31, 0.32), stop=None),
    2: dict(coil=(0.0, 0.30, 0.33), coil_rough=0.3, coil_metal=0.75, wire=0.015, turns=7.5, flake=1.0, coat=0.0,
            glow=0.0, cap=(0.32, 0.33, 0.35), cap_rough=0.3, wear=0.35, trim="copper", bands=2, vents=False,
            plates=False, bolts="cap", lit=0, heavy=False, rod=(0.78, 0.79, 0.80), body=(0.26, 0.27, 0.28), stop=None),
    3: dict(coil=(0.0, 0.17, 0.30), coil_rough=0.15, coil_metal=0.45, wire=0.0155, turns=7.5, flake=0.4, coat=1.0,
            glow=0.12, cap=(0.11, 0.115, 0.125), cap_rough=0.34, wear=0.2, trim="bronze", bands=2, vents=True,
            plates=False, bolts="bronze", lit=0, heavy=False, rod=(0.85, 0.86, 0.87), body=(0.03, 0.03, 0.035),
            stop=(0.86, 0.86, 0.84)),
    4: dict(coil=(0.0, 0.36, 0.40), coil_rough=0.14, coil_metal=0.3, wire=0.016, turns=9.0, flake=0.6, coat=1.0,
            glow=0.0, cap=(0.07, 0.072, 0.08), cap_rough=0.3, wear=0.0, trim="gold", bands=3, vents=True,
            plates=True, bolts="gold", lit=1, heavy=False, rod=(0.9, 0.9, 0.9), body=(0.02, 0.02, 0.025),
            stop=(0.86, 0.86, 0.84)),
    5: dict(coil=(0.0, 0.42, 0.52), coil_rough=0.1, coil_metal=0.3, wire=0.0165, turns=9.0, flake=0.6, coat=1.0,
            glow=1.0, cap=(0.025, 0.026, 0.03), cap_rough=0.12, wear=0.0, trim="copper_bright", bands=3, vents=True,
            plates=True, bolts="silver", lit=2, heavy=True, rod=(0.95, 0.95, 0.96), body=(0.012, 0.012, 0.016),
            stop=(0.05, 0.6, 0.65)),
}


def cage(name, r, x0, x1, bars, bar_w, mat, parent, plates=False, plate_mat=None, rivet_mat=None):
    """A slotted sleeve: rings at both ends joined by longitudinal bars (the slots between them show the coil)."""
    sp.cylinder(f"{name}EndA", r, x0, x0 + 0.03, mat, parent, 64)
    sp.cylinder(f"{name}EndB", r, x1 - 0.03, x1, mat, parent, 64)
    # inner wall so the sleeve reads as a tube through the slots
    for k in range(bars):
        a = 2 * math.pi * (k + 0.5) / bars
        thick = 0.014
        bar = sp.box(f"{name}Bar", (x1 - x0 - 0.05, bar_w, thick), ((x0 + x1) / 2, 0, 0), mat, parent, 0.003)
        for v in bar.data.vertices:
            v.co.z += r - thick / 2
        bar.rotation_euler = (a, 0, 0)
        if plates:
            plate = sp.box(f"{name}Plate", (x1 - x0 - 0.09, bar_w * 1.25, 0.006), ((x0 + x1) / 2, 0, 0), plate_mat, parent, 0.002)
            for v in plate.data.vertices:
                v.co.z += r + 0.002
            plate.rotation_euler = (a, 0, 0)
            for xx in (x0 + 0.06, x1 - 0.06):
                rv = sp.cylinder(f"{name}Rivet", 0.0045, 0, 0.004, rivet_mat, parent, 12)
                rv.rotation_euler = (a, math.radians(-90), 0)
                rv.location = (xx, -math.sin(a) * (r + 0.005), math.cos(a) * (r + 0.005))


def build(level: int, root_name=None):
    L = LEVELS[level]
    root = bpy.data.objects.new(root_name or f"OffroadSuspension{level}", None)
    bpy.context.scene.collection.objects.link(root)
    tag = f"O{level}"
    cap = sp.metal(f"cap{tag}", L["cap"], L["cap_rough"], wear=L["wear"], coat=0.6 if level >= 4 else 0.0)
    trim = sp.metal(f"trim{tag}", *sp.TRIM[L["trim"]])
    bolt = cap if L["bolts"] == "cap" else sp.metal(f"bolt{tag}", *sp.TRIM[L["bolts"]])
    coil = sp.metal(f"coil{tag}", L["coil"], L["coil_rough"], metallic=L["coil_metal"], coat=0.5 * L["coat"],
                    flake=L["flake"], glow=0.25 * L["glow"] + (0.12 if level == 3 else 0.0),
                    emission=(0.0, 0.75, 0.9) if L["glow"] else None)
    if level >= 4:
        cb = coil.node_tree.nodes["Principled BSDF"]
        cb.inputs["Thin Film Thickness"].default_value = 300 if level == 4 else 420
        cb.inputs["Thin Film IOR"].default_value = 1.45
    rod = sp.metal(f"rod{tag}", L["rod"], 0.1)
    body = sp.metal(f"body{tag}", L["body"], 0.3, metallic=0.6 if level >= 3 else 1.0, coat=0.8 if level >= 3 else 0.0)
    dark = sp.plain(f"bore{tag}", (0.01, 0.01, 0.012), 0.6)
    lit = sp.plain(f"lit{tag}", sp.GLOW, 0.3, emission=sp.GLOW, strength=6.0)
    plate = sp.metal(f"plate{tag}", tuple(c * 1.6 for c in L["cap"]), 0.25, coat=0.5) if L["plates"] else None

    for s in (1, -1):
        nm = "Mount" if s > 0 else "Base"

        def X(x):
            return s * x
        thick = 0.012 if L["heavy"] else 0.0
        sp.cylinder(f"{nm}Flange", R_FLANGE, X(X_FLANGE_IN - thick), X(X_FLANGE_OUT), cap, root, 64)
        sp.ring(f"{nm}FlangeEdge", R_FLANGE - 0.004, 0.004, X(X_FLANGE_OUT - 0.004), trim if level >= 2 else cap, root)
        for k in range(8):
            a = 2 * math.pi * (k + 0.5) / 8
            y, z = 0.098 * math.cos(a), 0.098 * math.sin(a)
            sp.cylinder(f"{nm}Bolt", 0.0105, X(X_FLANGE_IN - thick), X(X_FLANGE_IN - thick - 0.011), bolt, root, 6, y, z)
            if L["heavy"]:
                sp.cylinder(f"{nm}BoltOut", 0.0095, X(X_FLANGE_OUT), X(X_FLANGE_OUT + 0.006), bolt, root, 6, y, z)
        sp.cylinder(f"{nm}Boss", 0.06, X(X_FLANGE_OUT), X(X_BOSS), cap, root)
        sp.cylinder(f"{nm}Bore", 0.022, X(X_BOSS - 0.004), X(X_BOSS + 0.0005), dark, root, 32)
        sp.ring(f"{nm}BossRing", 0.06, 0.0035, X(X_BOSS - 0.004), trim if level >= 2 else cap, root)
        # level pips round the flange's rim, on top
        for i in range(level):
            a = math.radians(90 + (i - (level - 1) / 2) * 7.5)
            pip = sp.cylinder(f"{nm}Pip", 0.0045, 0, 0.003, lit if L["lit"] else trim, root, 16)
            pip.rotation_euler = (a - math.radians(90), math.radians(-90), 0)
            pip.location = (X((X_FLANGE_IN + X_FLANGE_OUT) / 2 - thick / 2), R_FLANGE * math.cos(a), R_FLANGE * math.sin(a) - 0.0005)

    x_in = X_FLANGE_IN - (0.012 if L["heavy"] else 0.0)
    # lower housing on the base end, sleeve on the mount end, overlapping at X_MEET
    cage("BaseHousing", R_LOWER, -x_in, X_MEET + 0.03, 6, 0.044, cap, root, L["plates"], plate, bolt)
    cage("MountSleeve", R_UPPER, X_MEET - 0.02, x_in, 6, 0.038, cap, root, L["plates"], plate, bolt)
    # copper bands where they meet and at the ends; reinforcing bands at higher levels
    sp.ring("BaseBand", R_LOWER + 0.002, 0.006, X_MEET + 0.02, trim, root)
    sp.ring("MountBand", R_UPPER + 0.002, 0.005, X_MEET - 0.01, trim, root)
    extra = {0: [], 2: [(-0.24, R_LOWER), (0.20, R_UPPER)], 3: [(-0.24, R_LOWER), (-0.14, R_LOWER), (0.20, R_UPPER)]}[L["bands"]]
    for k, (xx, r) in enumerate(extra):
        sp.ring(f"{'Base' if xx < 0 else 'Mount'}Reinforce{k}", r + 0.002, 0.0055, xx, trim, root)
    if L["lit"]:
        for xx, r in ([(X_MEET + 0.035, R_LOWER)] if L["lit"] == 1 else [(X_MEET + 0.035, R_LOWER), (0.12, R_UPPER), (-0.19, R_LOWER)]):
            sp.ring(f"{'Base' if xx < 0 else 'Mount'}Glow", r + 0.001, 0.0035, xx, lit, root)
    if L["vents"]:
        # rows of round vents on the housing's end collars, between the bars
        for s, r in ((-1, R_LOWER), (1, R_UPPER)):
            x0 = s * (x_in - 0.015)
            for k in range(6):
                a = 2 * math.pi * k / 6
                bm = bmesh.new()
                bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.0065, radius2=0.0065, depth=0.004)
                rot = Vector((0, math.cos(a), math.sin(a))).to_track_quat("Z", "X").to_matrix()
                bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=rot)
                bmesh.ops.translate(bm, verts=bm.verts, vec=(x0, r * math.cos(a), r * math.sin(a)))
                sp._object(f"{'Base' if s < 0 else 'Mount'}Vent", bm, lit if L["lit"] >= 2 else dark, root)
    # damper: body on the base end, rod into the mount end
    sp.cylinder("ShockBody", 0.036, -x_in, -0.02, body, root, 40)
    sp.cylinder("Rod", 0.017, -0.02, x_in, rod, root, 32)
    if L["stop"]:
        sp.cylinder("BumpStop", 0.027, x_in - 0.035, x_in, sp.plain(f"stop{tag}", L["stop"], 0.55,
                    emission=sp.GLOW if level == 5 else None, strength=1.5), root, 32)
    # coil between the flanges, closed end turns
    turns, n = L["turns"], 450 if sp.GAME else 900
    x0, x1 = -x_in + L["wire"], x_in - L["wire"]
    pts = []
    for i in range(n + 1):
        t = i / n
        ang = 2 * math.pi * turns * t
        ease = 0.04
        if t < ease:
            u = ease * (t / ease) ** 2 * 0.5
        elif t > 1 - ease:
            u = 1 - ease * ((1 - t) / ease) ** 2 * 0.5
        else:
            u = ease * 0.5 + (t - ease) * (1 - ease) / (1 - 2 * ease)
        pts.append(Vector((x0 + (x1 - x0) * u, COIL_R * math.cos(ang), COIL_R * math.sin(ang))))
    sp.tube("Coil", pts, L["wire"], coil, root, segs=12 if sp.GAME else 16)
    return root


def render_review(out: Path, icons_only=False):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

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

    shots, views, icons = {}, {}, {}
    for lv in ([] if icons_only else LEVELS):
        show(lv)
        cam = rl.camera(f"tq{lv}", (0.85, -1.05, 0.7), (0, 0, WIDTH / 2), ortho=0.9)
        shots[lv] = rl.render(cam, out / f"offroad_suspension_{lv}.png", 900, 900)
    for lv in ([] if icons_only else (1, 5)):
        show(lv)
        for name, loc, scale, w in (("side", (0, -2, WIDTH / 2), 0.9, 900), ("top", (0, -0.0001, 2), 0.9, 900),
                                    ("end", (2, 0, WIDTH / 2), 0.36, 500)):
            tgt = (0, 0, WIDTH / 2) if name != "top" else (0, 0, 0)
            cam = rl.camera(f"{name}{lv}", loc, tgt, ortho=scale)
            views[(lv, name)] = (rl.render(cam, out / f"offroad_suspension_{lv}_{name}.png", w, 500), cam, w, 500)
    ground.hide_render = True
    sc.render.film_transparent = True
    for lv in LEVELS:
        show(lv)
        cam = rl.camera(f"icon{lv}", (0.75, -0.95, 0.85), (0, 0, WIDTH / 2), ortho=0.85)
        p = rl.render(cam, out / f"offroad_suspension_{lv}_icon_raw.png", 512, 512)
        im = Image.open(p).convert("RGBA")
        im = im.crop(im.getbbox())
        side = max(im.size)
        pad = int(side * 0.06)
        sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (0, 0, 0, 0))
        sq.paste(im, ((sq.width - im.width) // 2, (sq.height - im.height) // 2))
        icons[lv] = sq.resize((256, 256), Image.LANCZOS)
        icons[lv].save(out / f"offroad_suspension_{lv}_icon.png")
        os.remove(p)
    if icons_only:
        return

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (5 * 760 + 120, 2520), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "OFF-ROAD SUSPENSION - LEVELS 1 TO 5 (design review)", fill=INK, font=F(46, True))
    d.text((60, 98), "Same body at every level: 3 blocks long (0.75 m), 0.24 m across, round mounting flanges on both ends, "
           "a slotted housing and sleeve that telescope over a long coil. The pips on each flange show the level.", fill=INK, font=F(24))
    notes = {
        1: ["Clean gunmetal housing and sleeve", "Matte teal coil seen through slots", "Two copper bands, 1 pip", "Stiffness 4"],
        2: ["Brighter, polished metal", "Flaked teal coil", "Extra copper reinforcing bands", "Stiffness 7"],
        3: ["Vents on the end collars", "Bronze bolts and bands", "Glossy coil with a faint glow", "Stiffness 10"],
        4: ["Riveted plates on every bar", "Gold accents, lit band", "Tighter coil (9 turns)", "Stiffness 13"],
        5: ["Black chrome, heavy-duty flanges", "Glowing coil, lit vents and bands", "Silver bolts, bright copper", "Stiffness 20"],
    }
    for i, lv in enumerate(LEVELS):
        x = 60 + i * 760
        sheet.paste(Image.open(shots[lv]).convert("RGB").resize((740, 740), Image.LANCZOS), (x, 190))
        d.rectangle([x, 190, x + 739, 929], outline=(190, 190, 190), width=2)
        d.text((x + 16, 202), f"LEVEL {lv}", fill=INK, font=F(34, True))
        for k, line in enumerate(notes[lv]):
            d.text((x + 10, 946 + k * 32), line, fill=INK, font=F(24))
        ic = icons[lv].resize((170, 170), Image.LANCZOS)
        tile = Image.new("RGB", (180, 180), (40, 42, 46))
        tile.paste(ic, (5, 5), ic)
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
                            "75 cm (3 blocks)", F(22))
                a = rl.project(cam, (HALF + 0.02, 0, 0), w, h)
                b = rl.project(cam, (HALF + 0.02, 0, WIDTH), w, h)
                rl.dim_line(dd, (xx + a[0] * scale, y + a[1] * scale), (xx + b[0] * scale, y + b[1] * scale), "24 cm",
                            F(22), vertical=True)
            xx += im.width + 30
        y += 520
    sheet.save(out / "review_sheet.png")
    strip = Image.new("RGBA", (5 * 256 + 6 * 16, 288), (40, 42, 46, 255))
    for i, lv in enumerate(LEVELS):
        strip.paste(icons[lv], (16 + i * 272, 16), icons[lv])
    strip.save(out / "hotbar_icons.png")
    print(f"rendered the review sheet into {out}")


# =====================================================================================================
# Game models and icons
# =====================================================================================================
# The flanges and what is bolted to them stay square; the housing, sleeve, coil and damper between them skew together
# so the body leans as the wheel end moves (the game's Flex mesh, see build_sport_suspension.py).
FLANGE_PIECES = ("Flange", "Bolt", "Boss", "Bore", "Pip")


def export_level(level: int):
    sp.GAME = True
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = build(level, "OffroadSuspension")
    parts = list(root.children)

    def flange(o, side):
        return o.name.startswith(side) and any(k in o.name for k in FLANGE_PIECES)
    groups = {"Stator": [o for o in parts if flange(o, "Base")], "mount": [o for o in parts if flange(o, "Mount")]}
    groups["mountFlex"] = [o for o in parts if o not in groups["Stator"] and o not in groups["mount"]]
    for name, objects in groups.items():
        sp.join(objects, name).parent = root
    path = sp.VANILLA / "meshes" / "parts" / f"offroad_suspension_{level}.glb"
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root, *root.children_recursive]:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_animations=False, export_skins=False, export_morph=False, export_draco_mesh_compression_enable=False,
        export_tangents=False, export_extras=False, export_cameras=False, export_lights=False)
    sp.GAME = False
    print(f"wrote {path}")


def write_icons():
    with tempfile.TemporaryDirectory() as tmp:
        render_review(Path(tmp), icons_only=True)
        for level in LEVELS:
            dest = sp.VANILLA / "textures" / "items" / f"offroad_suspension_{level}_icon.png"
            shutil.copyfile(Path(tmp) / f"offroad_suspension_{level}_icon.png", dest)
            print(f"wrote {dest}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--render" in args:
        render_review(Path(args[args.index("--render") + 1]))
    else:
        for lv in LEVELS:
            export_level(lv)
        write_icons()
