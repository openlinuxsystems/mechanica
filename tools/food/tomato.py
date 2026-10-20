"""The tomato: the harvested fruit (whole and bitten), and the green and ripening fruit the plant carries.

Z up, metres, the fruit's origin at the middle of its base.
"""

from __future__ import annotations

import math
import random

import bmesh
from mathutils import Matrix, Vector

import food_lib as fl

R = 0.044            # radius at the shoulders: a big, readable beefsteak-style tomato (8.8 cm across)
SQUASH = 0.80        # height / width

# Ripeness: skin (shoulder, body, base) and calyx colours.
SKINS = {
    "ripe": ("#E2502C", "#D6331F", "#A9201A"),
    "turning": ("#E9A23A", "#E06A2C", "#C2412A"),
    "green": ("#9CC45A", "#7DAF45", "#5E8F36"),
}
CALYX = "#4C8A2E"
STEM = "#5E8C34"


def mats(ripeness="ripe"):
    sh, body, base = SKINS[ripeness]
    return {
        "skin": fl.material(f"tomato_skin_{ripeness}", "#FFFFFF", rough=0.34, coat=0.3, sss=0.05, vertex_color=True),
        "calyx": fl.material("tomato_calyx", CALYX, rough=0.6, sss=0.1),
        "stem": fl.material("tomato_stem", STEM, rough=0.65),
        "cut": fl.material("tomato_stem_cut", "#B7C98A", rough=0.8),
        "flesh": fl.flesh_material("tomato_flesh", "#E0503C", "#F08C6E", "#E9D58A"),
    }


def _mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def body_bm(ripeness="ripe", scale=1.0, seed=1, segs=(56, 36)):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0)
    ph0 = rnd.uniform(0, 2 * math.pi)
    lobes = 6
    for v in bm.verts:
        x, y, z = v.co
        rho = math.hypot(x, y)
        phi = math.atan2(y, x)
        rib = 1 + 0.045 * math.cos(lobes * phi + ph0 + 0.35 * math.sin(2 * phi)) * rho ** 2 * (1 if z > -0.2 else 0.6)
        zz = z * SQUASH
        if z > 0:
            zz -= 0.13 * math.exp(-(rho / 0.32) ** 2) * z          # the stem well
            zz += 0.05 * rho ** 2 * z                                # rounded shoulders
        else:
            zz += 0.06 * math.exp(-(rho / 0.3) ** 2) * -z            # a soft, flat blossom end
        lean = 1 + 0.012 * math.sin(phi + ph0)                        # never perfectly round
        v.co = Vector((x * rib * lean, y * rib * lean, zz)) * R * scale
    lo = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= lo
    # vertex colours: shoulders, body, base
    sh, body, base = (fl.lin(c)[:3] for c in SKINS[ripeness])
    col = bm.loops.layers.color.new("Col")
    h = max(v.co.z for v in bm.verts)
    for f in bm.faces:
        for lp in f.loops:
            t = lp.vert.co.z / h
            c = _mix(base, body, min(1, t / 0.45)) if t < 0.45 else _mix(body, sh, (t - 0.45) / 0.55 * 0.7)
            lp[col] = (*c, 1.0)
    return bm


def sepals(bvh, top_z, n=5, length=0.03, width=0.0085, seed=3, droop=0.0):
    rnd = random.Random(seed)
    out = bmesh.new()
    a0 = rnd.uniform(0, 2 * math.pi)
    for k in range(n):
        a = a0 + 2 * math.pi * k / n + rnd.uniform(-0.15, 0.15)
        bend = rnd.uniform(-0.35, 0.35)
        L = length * rnd.uniform(0.85, 1.1)
        pts, ws, ns = [], [], []
        N = 12
        for i in range(N + 1):
            s = i / N
            aa = a + bend * s * s
            r = 0.003 + L * s
            x, y = r * math.cos(aa), r * math.sin(aa)
            z = fl.surface_z(bvh, x, y, top_z + 0.05) + 0.0012 + 0.0035 * math.sin(math.pi * s) + 0.006 * s ** 4 - droop * s * s
            pts.append((x, y, z))
            ws.append(width * min(1, 0.45 + s * 4) * (1 - s) ** 0.85 + 0.0004)
            ns.append((0, 0, 1))
        sb = fl.strip(pts, ws, ns, fold=0.25)
        me_tmp = bmesh.ops.duplicate(sb, geom=sb.verts[:] + sb.edges[:] + sb.faces[:])
        # merge into out
        tmp = __import__("bpy").data.meshes.new("tmp")
        sb.to_mesh(tmp)
        sb.free()
        out.from_mesh(tmp)
        __import__("bpy").data.meshes.remove(tmp)
    return out


def build(name="Tomato", ripeness="ripe", scale=1.0, seed=1, stem=True, parent=None, location=(0, 0, 0), m=None,
          segs=(56, 36)):
    """A tomato object tree: Body (skin), Calyx and Stem under an empty named ``name``."""
    import bpy
    m = m or mats(ripeness)
    root = fl.empty(name, parent, location)
    body = fl.from_bm(name + "_Body", body_bm(ripeness, scale, seed, segs), [m["skin"]], root)
    bvh = fl.bvh_of(body)
    top = max(v.co.z for v in body.data.vertices)
    cb = sepals(bvh, top, length=0.03 * scale, width=0.0085 * scale, seed=seed + 7)
    calyx = fl.from_bm(name + "_Calyx", cb, [m["calyx"]], root)
    fl.solidify(calyx, 0.0012 * scale, offset=1)
    # the hub the sepals grow from
    hub = bmesh.new()
    bmesh.ops.create_uvsphere(hub, u_segments=14, v_segments=8, radius=0.0055 * scale)
    for v in hub.verts:
        v.co.z *= 0.55
        v.co.z += fl.surface_z(bvh, 0, 0, top + 0.05) + 0.0012
    fl.from_bm(name + "_Hub", hub, [m["calyx"]], root)
    if stem:
        z0 = fl.surface_z(bvh, 0, 0, top + 0.05) + 0.002
        pts = fl.bezier((0, 0, z0), (0, 0, z0 + 0.008 * scale), (0.002 * scale, 0, z0 + 0.014 * scale),
                        (0.006 * scale, 0.001, z0 + 0.019 * scale), n=8)
        radii = [0.0034 * scale * (1 - 0.18 * i / 8) for i in range(9)]
        sb = fl.tube(pts, radii, segs=10, mat=0, cap_mat=1)
        fl.from_bm(name + "_Stem", sb, [m["stem"], m["cut"]], root)
    return root


def bite(root, where, size=1.0, name="Bite", m=None):
    """Takes a bite out of the tomato body: a mouth-sized hollow showing the flesh and seeds."""
    import bpy
    m = m or mats()
    body = next(o for o in root.children if o.name.endswith("_Body"))
    where = Vector(where)
    centre = Vector((0, 0, body.dimensions.z * 0.5))
    out = (where - centre).normalized()
    cutters = []
    # the main hollow: an ellipsoid wider than tall, half in the fruit
    main = bmesh.new()
    bmesh.ops.create_uvsphere(main, u_segments=24, v_segments=16, radius=1)
    rot = out.to_track_quat("Z", "Y").to_matrix().to_4x4()
    c0 = where + out * 0.013 * size
    bmesh.ops.transform(main, verts=main.verts, matrix=Matrix.Translation(c0) @ rot
                        @ Matrix.Diagonal((0.026 * size, 0.019 * size, 0.032 * size, 1)))
    cutters.append(fl.from_bm(name + "_Cut", main, [m["flesh"]], None))
    # one cutter: the hollow with the tooth grooves unioned on, then one cut
    cutter = cutters[0]
    for c in cutters[1:]:
        mod = cutter.modifiers.new(c.name, "BOOLEAN")
        mod.operation = "UNION"
        mod.solver = "MANIFOLD"
        mod.object = c
    for c in cutters:
        c.hide_render = True
        c.hide_viewport = c is not cutter
    fl.apply_modifiers(cutter)
    mod = body.modifiers.new("Bite", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "MANIFOLD"
    mod.material_mode = "TRANSFER"
    mod.object = cutter
    fl.apply_modifiers(body)
    for c in cutters:
        bpy.data.objects.remove(c)
    # cut faces keep the skin's vertex colours: whiten them so the flesh shows its own colour
    col = body.data.color_attributes.get("Col")
    flesh_idx = list(body.data.materials).index(m["flesh"]) if m["flesh"].name in body.data.materials else None
    if col is not None and flesh_idx is not None:
        # light, wet middle and a darker rim where the wall of the fruit was cut: reads as a hollow
        loops = body.data.loops
        verts = body.data.vertices
        rs = []
        for poly in body.data.polygons:
            if poly.material_index == flesh_idx:
                for li in poly.loop_indices:
                    q = verts[loops[li].vertex_index].co - c0
                    rs.append((li, (q - out * q.dot(out)).length))
        rmax = max((r for _, r in rs), default=1.0)
        light, rim = fl.lin("#F0907A")[:3], fl.lin("#C83A2A")[:3]
        for li, r in rs:
            k = (r / rmax) ** 2.2
            col.data[li].color = (*_mix(light, rim, k), 1)
    # a crisp lip where skin meets flesh
    me = body.data
    sharp = me.attributes.get("sharp_edge") or me.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
    faces_of = {}
    for poly in me.polygons:
        for ek in poly.edge_keys:
            faces_of.setdefault(ek, set()).add(poly.material_index)
    for e in me.edges:
        if len(faces_of.get(e.key, ())) > 1:
            sharp.data[e.index].value = True
    return body
