"""Build the standard IO port face (design review of 2026-10-19).

Every machine that takes items or fluids in or sends them out carries this face where a tube connects. It is not an
item: machines embed it on a 3 x 3 block face.

* A flush plate 3 x 3 blocks (0.75 x 0.75 m) and 0.05 m deep, chamfered corners, dark gunmetal with a teal band
  round its edge and faint grooves on the 0.25 m grid.
* A teal inlay that runs round the face just inside the edge and steps inward at each corner, clear of the four
  copper corner bolts.
* A raised collar in the middle: gunmetal ring, teal ring, gunmetal lip and a dark bore where the tube seats.
* Flow arrows: teal arrows pointing out mark an output, copper arrows pointing in mark an input. A two-way port
  carries both (teal on the vertical axis, copper on the horizontal one).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/io_port/build_io_port.py --render OUT_DIR   # review renders and sheet into OUT_DIR

Units are metres; in Blender Z is up and the port face looks along +Z. The build is deterministic.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Matrix, Vector

SIZE = 0.75                  # 3 blocks
HALF = SIZE / 2
DEPTH = 0.05
CHAMFER = 0.035              # corner chamfer of the plate outline
BAND_Z = (0.021, 0.029)      # teal band round the plate edge
INLAY = 0.325                # teal inlay line distance from the centre
INLAY_W = 0.02
BOLT = 0.322                 # corner bolt centres (x and y)
COLLAR_R = 0.175             # raised collar
TEAL_RING = (0.106, 0.138)
LIP_R = 0.106
BORE_R = 0.088
SAMPLES = int(os.environ.get("IO_SAMPLES", "64"))

GUNMETAL = (0.06, 0.064, 0.07)
TEAL = (0.0, 0.17, 0.15)
COPPER = (0.72, 0.36, 0.20)


# =====================================================================================================
# Materials
# =====================================================================================================
def _principled(name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    return m, m.node_tree.nodes["Principled BSDF"], m.node_tree


def metal(name, color, rough, metallic=1.0, wear=0.0):
    m, b, t = _principled(name)
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if wear > 0:
        # worn, cast-metal look: blotchy roughness and a fine bump
        tc = t.nodes.new("ShaderNodeTexCoord")
        n = t.nodes.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = 40
        n.inputs["Detail"].default_value = 8
        t.links.new(tc.outputs["Object"], n.inputs["Vector"])
        mr = t.nodes.new("ShaderNodeMapRange")
        mr.inputs["To Min"].default_value = max(0.0, rough - 0.2 * wear)
        mr.inputs["To Max"].default_value = min(1.0, rough + 0.2 * wear)
        t.links.new(n.outputs["Fac"], mr.inputs["Value"])
        t.links.new(mr.outputs["Result"], b.inputs["Roughness"])
        bump = t.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.15 * wear
        bump.inputs["Distance"].default_value = 0.0006
        t.links.new(n.outputs["Fac"], bump.inputs["Height"])
        t.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    return m


def materials():
    return dict(
        body=metal("io_gunmetal", GUNMETAL, 0.48, 0.85, wear=0.6),
        collar=metal("io_collar", (0.09, 0.094, 0.1), 0.36, 0.9, wear=0.5),
        teal=metal("io_teal", TEAL, 0.38, 0.2),
        copper=metal("io_copper", COPPER, 0.32, 1.0, wear=0.4),
        groove=metal("io_groove", (0.035, 0.037, 0.04), 0.7, 0.6),
        bore=metal("io_bore", (0.008, 0.008, 0.009), 0.85, 0.3),
    )


# =====================================================================================================
# Mesh helpers (outlines in XY, extruded along Z)
# =====================================================================================================
def _object(name, bm, mat, parent, smooth=False):
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


def _bevel(ob, width, segments=2):
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    return ob


def prism(name, outline, z0, z1, mat, parent, bevel=0.0):
    """Extrude an XY outline (list of (x, y), counter-clockwise) from z0 to z1."""
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new((x, y, z0)) for x, y in outline])
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in ext["geom"]:
        if isinstance(v, bmesh.types.BMVert):
            v.co.z = z1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _object(name, bm, mat, parent)
    return _bevel(ob, bevel) if bevel else ob


def annulus(name, r_in, r_out, z0, z1, mat, parent, segs=96, bevel=0.0):
    """A flat ring (washer) from r_in to r_out, z0 to z1."""
    bm = bmesh.new()
    rings = []
    for r, z in ((r_out, z0), (r_out, z1), (r_in, z1), (r_in, z0)):
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z))
                      for k in range(segs)])
    for i in range(4):
        a, b = rings[i], rings[(i + 1) % 4]
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _object(name, bm, mat, parent, smooth=True)
    for p in ob.data.polygons:
        p.use_smooth = abs(p.normal.z) < 0.5
    return _bevel(ob, bevel) if bevel else ob


def cylinder(name, r, z0, z1, mat, parent, segs=64, x=0.0, y=0.0, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=z1 - z0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(x, y, (z0 + z1) / 2))
    ob = _object(name, bm, mat, parent, smooth=segs > 8)
    for p in ob.data.polygons:
        p.use_smooth = segs > 8 and abs(p.normal.z) < 0.5
    return _bevel(ob, bevel) if bevel else ob


def chamfered_square(half, chamfer):
    h, c = half, chamfer
    return [(-h + c, -h), (h - c, -h), (h, -h + c), (h, h - c), (h - c, h), (-h + c, h), (-h, h - c), (-h, -h + c)]


def ribbon(name, points, width, z0, z1, mat, parent):
    """A closed flat strip of the given width along a closed XY polyline (mitred corners)."""
    n = len(points)
    pts = [Vector((x, y)) for x, y in points]
    inner, outer = [], []
    for i in range(n):
        a, p, c = pts[i - 1], pts[i], pts[(i + 1) % n]
        d0 = (p - a).normalized()
        d1 = (c - p).normalized()
        n0 = Vector((d0.y, -d0.x))
        n1 = Vector((d1.y, -d1.x))
        m = (n0 + n1).normalized()
        k = (width / 2) / max(0.3, m.dot(n0))
        outer.append(p + m * k)
        inner.append(p - m * k)
    bm = bmesh.new()
    layers = [[bm.verts.new((v.x, v.y, z)) for v in loop] for loop, z in
              ((outer, z0), (outer, z1), (inner, z1), (inner, z0))]
    for i in range(4):
        a, b = layers[i], layers[(i + 1) % 4]
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _object(name, bm, mat, parent)


def arrow(name, center, angle, length, width, z0, z1, mat, parent):
    """A flat arrow centred at center, pointing along angle (radians from +X)."""
    hl = length * 0.45        # head length
    sw = width * 0.42         # shaft width
    outline = [(-length / 2, -sw / 2), (length / 2 - hl, -sw / 2), (length / 2 - hl, -width / 2), (length / 2, 0),
               (length / 2 - hl, width / 2), (length / 2 - hl, sw / 2), (-length / 2, sw / 2)]
    ca, sa = math.cos(angle), math.sin(angle)
    pts = [(center[0] + x * ca - y * sa, center[1] + x * sa + y * ca) for x, y in outline]
    return prism(name, pts, z0, z1, mat, parent, bevel=0.0008)


def inlay_path():
    """The teal inlay: a square loop just inside the edge, stepping inward at each corner round the bolt."""
    i = INLAY
    out = []
    # one quadrant (top right), from the top edge round to the right edge; the others are rotations
    quad = [(i - 0.135, i), (i - 0.09, i - 0.055), (i - 0.055, i - 0.055), (i - 0.055, i - 0.09), (i, i - 0.135)]
    for k in range(4):
        a = math.pi / 2 * k
        ca, sa = math.cos(a), math.sin(a)
        # quadrant k goes counter-clockwise: rotate the reversed top-right quadrant
        for x, y in reversed(quad):
            out.append((x * ca - y * sa, x * sa + y * ca))
    return out


# =====================================================================================================
# The port
# =====================================================================================================
def build(mode="both", name="IOPort", mats=None):
    """mode: "both" (two-way), "out" (output only: teal arrows out) or "in" (input only: copper arrows in)."""
    m = mats or materials()
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)

    # plate: lower slab, teal band, upper slab (chamfered rim on top)
    prism(f"{name}_base", chamfered_square(HALF, CHAMFER), 0.0, BAND_Z[0], m["body"], root, bevel=0.003)
    prism(f"{name}_band", chamfered_square(HALF - 0.003, CHAMFER - 0.001), BAND_Z[0] - 0.001, BAND_Z[1] + 0.001,
          m["teal"], root)
    prism(f"{name}_top", chamfered_square(HALF, CHAMFER), BAND_Z[1], DEPTH, m["body"], root, bevel=0.007)

    # 0.25 m grid grooves on the face (behind the collar)
    zf = DEPTH
    for c in (-0.125, 0.125):
        prism(f"{name}_gridx{c}", [(-HALF + 0.05, c - 0.0018), (HALF - 0.05, c - 0.0018), (HALF - 0.05, c + 0.0018),
                                   (-HALF + 0.05, c + 0.0018)], zf - 0.001, zf + 0.0003, m["groove"], root)
        prism(f"{name}_gridy{c}", [(c - 0.0018, -HALF + 0.05), (c + 0.0018, -HALF + 0.05), (c + 0.0018, HALF - 0.05),
                                   (c - 0.0018, HALF - 0.05)], zf - 0.001, zf + 0.0003, m["groove"], root)

    # teal inlay with a dark groove round it
    path = inlay_path()
    ribbon(f"{name}_inlayGroove", path, INLAY_W + 0.008, zf - 0.001, zf + 0.0004, m["groove"], root)
    ribbon(f"{name}_inlay", path, INLAY_W, zf - 0.001, zf + 0.0012, m["teal"], root)

    # corner bolts: copper washer, hex head, dark socket
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * BOLT, sy * BOLT
            cylinder(f"{name}_washer", 0.026, zf - 0.001, zf + 0.004, m["copper"], root, x=x, y=y, bevel=0.0012)
            head = cylinder(f"{name}_bolt", 0.018, zf + 0.004, zf + 0.012, m["copper"], root, segs=6, x=x, y=y,
                            bevel=0.0015)
            head.rotation_euler.z = 0.0
            cylinder(f"{name}_socket", 0.0075, zf + 0.0115, zf + 0.0124, m["bore"], root, segs=6, x=x, y=y)

    # collar: gunmetal ring, teal ring, gunmetal lip, bore
    annulus(f"{name}_collarSeat", COLLAR_R + 0.006, COLLAR_R + 0.012, zf - 0.001, zf + 0.0003, m["groove"], root)
    annulus(f"{name}_collar", TEAL_RING[1] - 0.002, COLLAR_R, zf - 0.001, zf + 0.014, m["collar"], root, bevel=0.003)
    annulus(f"{name}_tealRing", TEAL_RING[0], TEAL_RING[1], zf - 0.001, zf + 0.012, m["teal"], root, bevel=0.0015)
    annulus(f"{name}_lip", BORE_R, LIP_R + 0.001, zf - 0.001, zf + 0.017, m["collar"], root, bevel=0.003)
    annulus(f"{name}_boreWall", BORE_R - 0.003, BORE_R + 0.0005, 0.008, zf + 0.016, m["bore"], root)
    cylinder(f"{name}_boreFloor", BORE_R, 0.006, 0.009, m["bore"], root)

    # flow arrows: teal pointing out on the vertical axis, copper pointing in on the horizontal axis
    axes = []
    if mode in ("both", "out"):
        axes += [(math.pi / 2, "out", m["teal"]), (-math.pi / 2, "out", m["teal"])]
        if mode == "out":
            axes += [(0.0, "out", m["teal"]), (math.pi, "out", m["teal"])]
    if mode in ("both", "in"):
        axes += [(0.0, "in", m["copper"]), (math.pi, "in", m["copper"])]
        if mode == "in":
            axes += [(math.pi / 2, "in", m["copper"]), (-math.pi / 2, "in", m["copper"])]
    for k, (a, way, mat) in enumerate(axes):
        point = a if way == "out" else a + math.pi
        # big arrow on the face, small arrow on the collar
        r = 0.255
        arrow(f"{name}_arrow{k}", (r * math.cos(a), r * math.sin(a)), point, 0.075, 0.06, zf - 0.001, zf + 0.0022,
              mat, root)
        r = (COLLAR_R + TEAL_RING[1]) / 2
        arrow(f"{name}_tick{k}", (r * math.cos(a), r * math.sin(a)), point, 0.032, 0.03, zf + 0.012, zf + 0.0162,
              mat, root)
    return root


# =====================================================================================================
# Review renders
# =====================================================================================================
def _machine_box(mats, parent):
    """A 5 x 5 x 4 block stand-in machine to show the port mounted flush on a face."""
    body = metal("io_machine", (0.30, 0.31, 0.32), 0.55, 0.6, wear=1.0)
    ob = prism("Machine", chamfered_square(0.625, 0.01), 0.0, 1.0, body, parent, bevel=0.006)
    return ob


def render_review(out: Path):
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lift"))
    import render_lift as rl  # noqa: PLC0415
    from PIL import Image, ImageDraw  # noqa: PLC0415

    rl.SAMPLES = SAMPLES
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rl.setup_world()
    sc = bpy.context.scene
    sc.cycles.samples = SAMPLES
    mats = materials()

    groups = {}

    def group(key, root):
        groups[key] = [root, *root.children_recursive]
        return root

    def show(key):
        for k, obs in groups.items():
            for ob in obs:
                ob.hide_render = k != key

    group("both", build("both", "Both", mats))
    for k, mode in (("out", "out"), ("in", "in")):
        group(k, build(mode, mode.title(), mats))

    # mounted: a 3 x 3 x 3 machine with an output port on its front and an input port on its side
    mroot = bpy.data.objects.new("Mounted", None)
    sc.collection.objects.link(mroot)
    mroot.location = (0, 0, 0)
    _machine_box(mats, mroot)
    p_out = build("out", "MOut", mats)
    p_out.parent = mroot
    p_out.rotation_euler = (math.radians(90), 0, 0)
    p_out.location = (0, -0.625, 0.5)
    p_in = build("in", "MIn", mats)
    p_in.parent = mroot
    p_in.rotation_euler = (math.radians(90), 0, math.radians(90))
    p_in.location = (0.625, 0, 0.5)
    group("mounted", mroot)

    shots = {}
    show("both")
    shots["front"] = rl.render(rl.camera("front", (0, -0.0001, 3), (0, 0, 0), ortho=0.86), out / "io_port_front.png",
                               1000, 1000)
    shots["iso"] = rl.render(rl.camera("iso", (1.25, -1.55, 1.25), (0, 0, 0.0), ortho=1.0), out / "io_port_iso.png",
                             1200, 900)
    shots["side"] = rl.render(rl.camera("side", (0, -3, DEPTH / 2), (0, 0, DEPTH / 2), ortho=0.86),
                              out / "io_port_side.png", 1200, 240)
    shots["close"] = rl.render(rl.camera("close", (0.32, -0.42, 0.34), (0, 0, 0.03), lens=55),
                               out / "io_port_close.png", 1000, 800)
    for k in ("out", "in"):
        show(k)
        shots[k] = rl.render(rl.camera(f"front_{k}", (0, -0.0001, 3), (0, 0, 0), ortho=0.86), out / f"io_port_{k}.png",
                             700, 700)
    show("mounted")
    shots["mounted"] = rl.render(rl.camera("mounted", (2.6, -3.3, 2.4), (0, 0, 0.45), ortho=2.3),
                                 out / "io_port_mounted.png", 1200, 1000)

    # sheet
    F = rl.font
    INK = rl.INK
    TEALINK = (14, 120, 112)
    W, H = 2560, 2240
    sheet = Image.new("RGB", (W, H), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((60, 36), "IO PORT - STANDARD 3 x 3 BLOCK FACE (design review)", fill=INK, font=F(46, True))
    d.text((60, 100), "Size 3 x 3 blocks (0.75 x 0.75 m), depth 0.05 m, mounted flush on any machine face. "
           "Every machine with an input or output uses this face so tubes always meet the same thing.",
           fill=INK, font=F(24))

    def panel(path, x, y, w, h, label):
        im = Image.open(path).convert("RGB")
        im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
        if im.height > h:
            top = (im.height - h) // 2
            im = im.crop((0, top, w, top + h))
        sheet.paste(im, (x, y))
        d.rectangle([x, y, x + w - 1, y + im.height - 1], outline=(190, 190, 190), width=2)
        d.text((x + 14, y + 10), label, fill=INK, font=F(26, True))
        return im.height

    # 1 front with dimensions
    panel(shots["front"], 60, 170, 900, 900, "1  FRONT (TWO-WAY PORT)")
    s = 900 / 0.86
    a = 60 + 450 - 0.375 * s
    b = 60 + 450 + 0.375 * s
    rl.dim_line(d, (a, 1090), (b, 1090), "0.75 m (3 blocks)", F(24))
    rl.dim_line(d, (985, 170 + 450 - 0.375 * s), (985, 170 + 450 + 0.375 * s), "0.75 m\n(3 blocks)", F(24),
                vertical=True)
    # 2 side
    panel(shots["side"], 1180, 170, 1320, 264, "2  SIDE (PROFILE)")
    d.text((1190, 450), "0.05 m deep plate (collar stands 17 mm proud); teal band round the edge.", fill=INK, font=F(22))
    # 3 iso
    panel(shots["iso"], 1180, 500, 1320, 990, "3  ISOMETRIC")
    # 4 close-up
    panel(shots["close"], 60, 1170, 900, 720, "4  COLLAR CLOSE-UP")
    # 5 variants
    d.text((1180, 1510), "5  DIRECTION VARIANTS", fill=INK, font=F(26, True))
    x = 1180
    for k, label in (("out", "OUTPUT: teal arrows point out"), ("in", "INPUT: copper arrows point in")):
        panel(shots[k], x, 1550, 420, 420, "")
        d.text((x, 1980), label, fill=INK, font=F(22))
        x += 450
    d.text((x, 1560), "TWO-WAY:", fill=INK, font=F(22, True))
    d.text((x, 1592), "both (panel 1):", fill=INK, font=F(22))
    d.text((x, 1624), "teal out, vertical", fill=INK, font=F(22))
    d.text((x, 1656), "copper in, horizontal", fill=INK, font=F(22))
    # legend
    y = 1930
    for col, text in (((40, 42, 46), "DARK GUNMETAL (plate, collar)"), (TEALINK, "TEAL (band, inlay, ring, out arrows)"),
                      ((184, 92, 51), "COPPER (bolts, in arrows)")):
        d.rectangle([60, y, 100, y + 30], fill=col)
        d.text((116, y + 2), text, fill=INK, font=F(22))
        y += 44
    d.text((60, 2080), "Flow direction is read from the arrows: teal out = output, copper in = input.",
           fill=INK, font=F(22))
    d.text((60, 2114), "Logic links (Connect Tool) stay point-based and do not use this face.", fill=INK, font=F(22))
    sheet.save(out / "review_sheet.png")

    # second sheet: mounted on a machine
    im = Image.open(shots["mounted"]).convert("RGB")
    m2 = Image.new("RGB", (im.width + 120, im.height + 220), (246, 246, 244))
    m2.paste(im, (60, 160))
    dd = ImageDraw.Draw(m2)
    dd.text((60, 36), "IO PORT MOUNTED ON A MACHINE", fill=INK, font=F(40, True))
    dd.text((60, 96), "Output port on the front, input port on the side of a 5 x 5 x 4 block stand-in machine (plain grey box).",
            fill=INK, font=F(22))
    m2.save(out / "review_mounted.png")
    print(f"rendered the review sheets into {out}")


def main(argv):
    if "--render" in argv:
        render_review(Path(argv[argv.index("--render") + 1]))
    else:
        print(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
