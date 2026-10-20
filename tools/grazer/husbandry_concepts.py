"""Concept renders for the Grazer taming gear (husbandry PR 2): the collar with its bell and name tag, the Lead and the
Feeding Trough. Review renders only; nothing here is game output yet.

    python tools/grazer/husbandry_concepts.py OUT_DIR
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[0] / "lift"))
import build_grazer as bg  # noqa: E402
import build_scuttler as sc  # noqa: E402
import lift_textures as lt  # noqa: E402
import render_lift as rl  # noqa: E402

V = Vector
SAMPLES = 40


# ---- materials -----------------------------------------------------------------------------------
def mats():
    return {
        "leather": lt.textured("hb_leather", lt.metal_surface(301, "#5a3a22", 0.62, 0.0, wear=0.5, edge_light=0.2), 1.2),
        "brass": lt.textured("hb_brass", lt.metal_surface(303, "#c49a3c", 0.32, 1.0, wear=0.6), 1.0),
        "bell_in": bg.plain("hb_bell_in", "#2a2018", 0.6),
        "gunmetal": lt.textured("hb_gunmetal", lt.metal_surface(307, "#4a5058", 0.42, 0.9, wear=0.8), 2.0),
        "copper": lt.textured("hb_copper", lt.metal_surface(309, "#b06a3c", 0.38, 1.0, wear=0.5), 1.0),
        "wood": lt.textured("hb_wood", lt.metal_surface(311, "#9a7448", 0.7, 0.0, wear=0.3, edge_light=0.1), 2.5),
        "wood_dark": lt.textured("hb_wood_dark", lt.metal_surface(313, "#6e5232", 0.75, 0.0, wear=0.3), 2.5),
        "hay": lt.textured("hb_hay", lt.metal_surface(317, "#d9b860", 0.85, 0.0, wear=1.5, edge_light=0.4), 4.0),
        "carrot": bg.plain("hb_carrot", "#e07a2a", 0.5),
        "leaf": bg.plain("hb_leaf", "#5f8f3a", 0.6),
        "rope": lt.textured("hb_rope", lt.metal_surface(319, "#c8a46a", 0.8, 0.0, wear=2.0, edge_light=0.3), 3.0),
        "grip": bg.plain("hb_grip", "#e0a81c", 0.45),
        "cyan": bg.plain("hb_cyan", "#3fc6d0", 0.3),
    }


MATS = {}


def obj(name, geo, parent=None):
    """An object from an sc.Geo (world space), parented without moving it."""
    me = bpy.data.meshes.new(name)
    used = list(dict.fromkeys(geo.m))
    me.from_pydata(geo.v, [], geo.f)
    for m in used:
        me.materials.append(MATS[m])
    me.polygons.foreach_set("material_index", [used.index(m) for m in geo.m])
    me.validate()
    for p in me.polygons:
        p.use_smooth = True
    _uvs(me)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if parent is not None:
        mw = ob.matrix_world.copy()
        ob.parent = parent
        ob.matrix_parent_inverse = parent.matrix_world.inverted()
        ob.matrix_world = mw
    return ob


def _uvs(me):
    uv = me.uv_layers.new()
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            u, v = [co[i] for i in range(3) if i != ax]
            uv.data[li].uv = (u * 1.6, v * 1.6)


def piece(fn):
    """Runs `fn` with sc's builders writing into a fresh Geo; returns it."""
    sc.GEO.clear()
    sc.at("_")
    fn()
    return sc.GEO.get("_", sc.Geo())


def band(pts, normals, width, thick, mat):
    """A closed flat strap through `pts` (a ring on a surface), `width` along the ring's axis, `thick` outwards."""
    bm = bmesh.new()
    n = len(pts)
    axis = (pts[1] - pts[0]).cross(pts[2] - pts[1]).normalized()
    rings = []
    for p, nr in zip(pts, normals):
        rings.append([bm.verts.new(p + nr * dz + axis * dw) for dz, dw in
                      ((0, -width / 2), (thick, -width / 2), (thick, width / 2), (0, width / 2))])
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    sc.g().add_bm(bm, mat)


# ---- the collar -----------------------------------------------------------------------------------
def collar(nodes):
    """Collar, buckle, bell and name tag round the Grazer's neck (on the Neck node)."""
    dg = bpy.context.evaluated_depsgraph_get()
    bms = []
    for ob in bpy.data.objects:
        if ob.type == "MESH" and ob.name.split("_")[0] in ("Neck", "Head", "Body", "Jaw"):
            bm = bmesh.new()
            bm.from_object(ob.evaluated_get(dg), dg)
            bm.transform(ob.matrix_world)
            bms.append(bm)
    bm = bmesh.new()
    for b in bms:
        tmp = bpy.data.meshes.new("tmp")
        b.to_mesh(tmp)
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    bvh = BVHTree.FromBMesh(bm)
    a, b = bg.SHOULDER + V((0, 0.12, -0.06)), bg.NECK + V((0, -0.08, 0))
    axis = (b - a).normalized()
    u = axis.cross(V((1, 0, 0))).normalized()
    w = axis.cross(u).normalized()

    def ring(t, n=48):
        c = a.lerp(b, t)
        pts, nrs = [], []
        for k in range(n):
            ang = 2 * math.pi * k / n
            d = math.cos(ang) * u + math.sin(ang) * V((1, 0, 0))
            loc, nr, _, _ = bvh.ray_cast(c + d * 1.2, -d)
            pts.append(loc)
            nrs.append(d)
        return pts, nrs, sum((p - c).length for p in pts) / n

    best = min((ring(t / 20) for t in range(4, 18)), key=lambda r: r[2])
    pts, nrs, _ = best
    pts = [p + d * 0.004 for p, d in zip(pts, nrs)]

    def build():
        band(pts, nrs, 0.085, 0.02, "leather")
        band(pts, nrs, 0.012, 0.025, "leather")      # stitched ridge
        # buckle on the left side
        side = max(range(len(pts)), key=lambda i: -pts[i].x)
        p, d = pts[side], nrs[side]
        sc.box((0.024, 0.09, 0.09), p + d * 0.02, "brass", bevel=0.006,
               rot=d.to_track_quat("X", "Z").to_matrix().to_4x4())
        # bell and tag under the throat
        low = min(range(len(pts)), key=lambda i: pts[i].z)
        p, d = pts[low], nrs[low]
        ring_c = p + d * 0.03 + V((0, 0, -0.03))
        sc.rod(ring_c + V((0.02, 0, 0)), ring_c - V((0.02, 0, 0)), 0.012, "brass", segs=10)
        bell = ring_c + V((0, -0.02, -0.075))
        sc.rod(bell + V((0, 0, 0.045)), bell + V((0, 0, -0.035)), 0.026, "brass", r2=0.05, segs=24)
        sc.sphere(bell + V((0, 0, 0.045)), 0.027, "brass", segs=16)
        sc.disc(bell + V((0, 0, -0.04)), 0.044, 0.008, (0, 0, 1), "bell_in")
        sc.sphere(bell + V((0, 0, -0.05)), 0.014, "brass")
        tag = ring_c + V((0.07, -0.03, -0.05))
        sc.disc(tag, 0.034, 0.006, (0.3, -1, 0.1), "brass", segs=28)
        sc.rod(ring_c + V((0.03, 0, 0)), tag + V((0, 0, 0.03)), 0.005, "brass", segs=6)

    obj("Collar", piece(build), nodes["Neck"])


# ---- the Lead ---------------------------------------------------------------------------------------
def lead(origin, coiled=True):
    """The Lead: a coil of rope with a yellow grip loop and a snap hook."""
    o = V(origin)

    def build():
        if coiled:
            for k in range(5):
                c = o + V((0, 0, 0.05 + k * 0.022))
                sc.tube([c + V((math.cos(t) * (0.15 - k * 0.006), math.sin(t) * (0.15 - k * 0.006), 0.012 * math.sin(3 * t + k)))
                         for t in [2 * math.pi * i / 40 for i in range(40)]], 0.014, "rope", segs=8)
            sc.rod(o + V((-0.17, 0, 0.02)), o + V((-0.17, 0, 0.18)), 0.03, "grip", segs=12)
            sc.rod(o + V((-0.17, 0, 0.18)), o + V((-0.06, 0, 0.18)), 0.012, "rope", segs=8)
            hook = o + V((0.2, 0.05, 0.06))
        else:
            hook = o
        sc.tube([hook + V((0.03 * math.cos(t), 0, 0.055 * math.sin(t))) for t in [2 * math.pi * i / 24 for i in range(24)]],
                0.006, "gunmetal", segs=8)
        sc.rod(hook + V((0, 0, 0.055)), hook + V((0, 0, 0.1)), 0.012, "copper", segs=10)
        sc.box((0.008, 0.012, 0.045), hook + V((0.026, 0, 0.0)), "gunmetal", bevel=0.002)

    return obj("Lead", piece(build))


def rope(a, b, sag=0.25, r=0.012):
    a, b = V(a), V(b)
    pts = [a.lerp(b, i / 30) - V((0, 0, sag * 4 * (i / 30) * (1 - i / 30))) for i in range(31)]

    def build():
        for p, q in zip(pts, pts[1:]):
            sc.rod(p, q, r, "rope", segs=8)
        sc.rod(b + V((0, 0, -0.04)), b + V((0, 0, 0.14)), 0.03, "grip", segs=12)

    return obj("LeadRope", piece(build))


# ---- the Feeding Trough -------------------------------------------------------------------------------
L, D, H = 1.5, 0.75, 0.5        # 6 x 3 x 2 blocks


def trough(origin, fill):
    """A slatted wooden tub in a gunmetal frame; `fill` 0..1 of hay and carrots."""
    o = V(origin)

    def build():
        top, bot = H - 0.04, 0.16
        # frame: four splayed legs, rails, cross braces, copper corner caps
        for sx in (-1, 1):
            for sy in (-1, 1):
                foot = o + V((sx * (L / 2 - 0.02), sy * (D / 2 - 0.01), 0.0))
                head = o + V((sx * (L / 2 - 0.08), sy * (D / 2 - 0.06), top))
                sc.box((0.05, 0.05, (head - foot).length + 0.02), (foot + head) / 2, "gunmetal", bevel=0.008,
                       rot=V((0, 0, 1)).rotation_difference(head - foot).to_matrix().to_4x4())
                sc.box((0.07, 0.07, 0.03), foot + V((0, 0, 0.015)), "gunmetal", bevel=0.006)
                sc.box((0.07, 0.07, 0.03), head + V((0, 0, 0.02)), "copper", bevel=0.008)
            sc.box((0.04, D - 0.12, 0.04), o + V((sx * (L / 2 - 0.12), 0, 0.1)), "gunmetal", bevel=0.006)
        sc.box((L - 0.2, 0.04, 0.04), o + V((0, 0, 0.1)), "gunmetal", bevel=0.006)
        for sy in (-1, 1):
            sc.box((L - 0.12, 0.05, 0.05), o + V((0, sy * (D / 2 - 0.06), top + 0.02)), "gunmetal", bevel=0.008)
        # the tub: slatted sloping sides, a bottom and two end boards
        for sy in (-1, 1):
            for k in range(4):
                z = bot + 0.03 + k * 0.075
                y = sy * (0.12 + (z - bot) * 0.62)
                tilt = Matrix.Rotation(sy * math.radians(-32), 4, "X")
                sc.box((L - 0.22, 0.03, 0.07), o + V((0, y, z)), "wood" if k % 2 == 0 else "wood_dark", bevel=0.006, rot=tilt)
        sc.box((L - 0.22, 0.26, 0.03), o + V((0, 0, bot)), "wood_dark", bevel=0.006)
        # end boards
        for sx in (-1, 1):
            sc.box((0.03, 0.5, 0.3), o + V((sx * (L / 2 - 0.12), 0, bot + 0.16)), "wood", bevel=0.006)
        # brass plate with a cyan fill window on the front
        sc.box((0.18, 0.012, 0.07), o + V((0, -(D / 2 - 0.035), top - 0.06)), "copper", bevel=0.004)
        sc.box((0.12, 0.014, 0.022), o + V((0, -(D / 2 - 0.035), top - 0.06)), "cyan", bevel=0.002)
        if fill > 0:
            fz = bot + 0.04 + fill * 0.24
            half = 0.1 + (fz - bot) * 0.62
            for i in range(int(36 * fill) + 6):
                x = -L / 2 + 0.2 + (i * 0.37 % 1.0) * (L - 0.4)
                y = ((i * 0.61) % 1.0 - 0.5) * half
                sc.sphere(o + V((x, y, fz - 0.03)), 0.08, "hay", scale=(1.5, 0.9, 0.45), segs=10)
            for i in range(int(7 * fill) + 1):
                x = -L / 2 + 0.3 + (i * 0.53 % 1.0) * (L - 0.6)
                y = ((i * 0.29) % 1.0 - 0.5) * half
                c = o + V((x, y, fz + 0.04))
                d = V((math.cos(i * 2.1), math.sin(i * 2.1), 0.15))
                sc.rod(c - d * 0.07, c + d * 0.07, 0.022, "carrot", r2=0.006, segs=10)
                sc.rod(c - d * 0.07, c - d * 0.12 + V((0, 0, 0.03)), 0.008, "leaf", segs=6)

    return obj(f"Trough{int(fill * 100)}", piece(build))


# ---- renders ------------------------------------------------------------------------------------------
def scene(coat="slate"):
    bg.use(coat)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    for ob in bpy.data.objects:
        if ob.type == "LIGHT":
            ob.data.energy *= 0.7
    bpy.context.scene.cycles.samples = SAMPLES
    MATS.clear()
    MATS.update(mats())
    root, nodes = bg.build()
    root.rotation_euler = (0, 0, 0)
    bg.Pose(nodes).apply()
    bpy.context.view_layer.update()
    collar(nodes)
    return root, nodes


def main(out: Path):
    from PIL import Image, ImageDraw  # noqa: PLC0415
    out.mkdir(parents=True, exist_ok=True)
    shots = []

    def shot(name, cam, w=800, h=800):
        shots.append((name, rl.render(cam, out / f"{name}.png", w, h)))

    # 1. the tamed Grazer: collar, bell and tag
    root, nodes = scene("slate")
    shot("collar_tq", rl.camera("a", (-3.6, -4.0, 2.0), (0, -0.2, 0.6), ortho=2.4))
    shot("collar_close", rl.camera("b", (-1.6, -2.6, 1.1), (0, -0.55, 0.7), ortho=0.95))
    shot("collar_front", rl.camera("c", (0, -6, 0.75), (0, -0.4, 0.7), ortho=1.6))
    bg.set_pose(nodes, "eat", 0.4 * bg.CLIPS["eat"], bg.CLIPS["eat"])
    shot("collar_eating", rl.camera("d", (-6, -0.4, 0.7), (0, -0.2, 0.55), ortho=2.6))
    # 2. on the Lead
    bg.Pose(nodes).apply()
    bpy.context.view_layer.update()
    bell = min((v for ob in bpy.data.objects if ob.name == "Collar" for v in (ob.matrix_world @ x.co for x in ob.data.vertices)),
               key=lambda p: p.z)
    rope(bell + V((0, 0, 0.09)), (1.6, -1.4, 1.05), sag=0.3)
    shot("on_lead", rl.camera("e", (-2.5, -5.5, 2.2), (0.6, -0.6, 0.6), ortho=3.2))

    # 3. the Lead item and the Feeding Trough at three fills
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    bpy.context.scene.cycles.samples = SAMPLES
    MATS.clear()
    MATS.update(mats())
    lead((0, 0, 0))
    shot("lead_item", rl.camera("f", (-1.2, -1.3, 1.1), (0, 0, 0.08), ortho=0.75))
    for ob in list(bpy.data.objects):
        if ob.name.startswith("Lead"):
            bpy.data.objects.remove(ob)
    for i, f in enumerate((0.0, 0.5, 1.0)):
        trough((i * 2.2 - 2.2, 0, 0), f)
    shot("trough_fills", rl.camera("g", (-3.4, -6.2, 3.6), (0, 0, 0.25), ortho=6.4), 1600, 800)
    for ob in list(bpy.data.objects):
        if ob.name.startswith("Trough") and ob.name != "Trough100":
            bpy.data.objects.remove(ob)
    shot("trough_tq", rl.camera("h", (0.2, -2.6, 1.6), (2.2, 0, 0.25), ortho=1.9))
    shot("trough_end", rl.camera("i", (-1.8, 0, 0.3), (2.2, 0, 0.25), ortho=1.2))

    # sheet
    from render_lift import INK, font as F  # noqa: PLC0415
    sheet = Image.new("RGB", (60 + 4 * 416 + 40, 130 + 2 * 416 + 824 + 40), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 30), "GRAZER TAMING GEAR (concepts)", fill=INK, font=F(40, True))
    d.text((60, 86), "Collar with bell and name tag, the Lead, and the Feeding Trough (6 x 3 x 2 blocks) empty, half and full.",
           fill=INK, font=F(20))
    slots = {"collar_tq": (0, 0), "collar_close": (1, 0), "collar_front": (2, 0), "collar_eating": (3, 0),
             "on_lead": (0, 1), "lead_item": (1, 1), "trough_tq": (2, 1), "trough_end": (3, 1)}
    for name, path in shots:
        if name == "trough_fills":
            im = Image.open(path).convert("RGB").resize((1648, 824), Image.LANCZOS)
            x, y = 60, 130 + 2 * 416
            sheet.paste(im, (x, y))
            d.rectangle([x, y, x + 1647, y + 823], outline=(190, 190, 190), width=2)
            d.text((x + 10, y + 8), "TROUGH: EMPTY, HALF, FULL", fill=INK, font=F(16, True))
            continue
        cx, cy = slots[name]
        x, y = 60 + cx * 416, 130 + cy * 416
        sheet.paste(Image.open(path).convert("RGB").resize((400, 400), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + 399, y + 399], outline=(190, 190, 190), width=2)
        d.text((x + 10, y + 8), name.replace("_", " ").upper(), fill=INK, font=F(16, True))
    sheet.save(out / "husbandry_concepts_sheet.png")
    print("wrote", out / "husbandry_concepts_sheet.png")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
