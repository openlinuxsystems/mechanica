"""Shared kit for the parts map's structural batches 8 to 10 (beams, frames, pipes, valves, ducts, conduits, cables).

These are **Colony Works** pieces (docs/art-direction.md): the colony's own steelwork and services, as the world's
pipe runs, I-beams and valve housings (tools/structures). Palette: steel blue ``#3E6A8C`` primer on beams and frames,
bone ``#D9D1BC`` on the big pipes, mustard ``#E0B530`` bands and conduits, graphite ``#2B2F35`` trim and cables,
galvanised zinc ``#9AA3AB`` ducts and small pipes, signal red ``#C8423A`` only on valve wheels and the tower mast.
Edges get small bevels, round only where real (pipes, cables, tubes), and the hardware shows (bolts, clamps, flanges).

What it holds, on top of ``part_kit`` (read-only, shared) and the Lift's geometry:

* ``materials()``: the Colony Works materials by role name.
* ``sweep`` / ``sweep_hollow``: a closed 2D profile (or a profile with a bore) swept along a path, with a fixed `up`
  vector, so one call makes a straight run or a bend: I-beams, square ducts and tubes, round pipes and elbows.
* Profiles: ``i_profile``, ``circle``, ``rect``; paths: ``line``, ``arc``.
* ``pipe_run`` / ``pipe_elbow`` / ``flange`` / ``bolt_ring`` / ``grate``: the pipe kit, built so two parts meet at a
  block face into one full flange.
* ``run_parts``: builds every part of a batch in one session, writes ``meshes/parts/<id>.glb`` and
  ``textures/items/<id>_icon.png``, and with ``--render OUT_DIR`` each part's review sheet (render_kit) plus a batch
  overview sheet.

Blender axes: x right, y the part's front (glTF -z, the game's depth axis), z up. A part's model is centred on its
footprint: an ``[sx, sy, sz]`` part (the game's x, up, depth in blocks) spans x +-sx/8, y +-sz/8, z +-sy/8 metres.
Run ``tools/structural/build_structural.py``, not this file.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import part_kit as K  # noqa: E402  (first: it imports bpy)
from mathutils import Matrix, Vector  # noqa: E402

Geo, box, cylinder, hexnut, dome = K.Geo, K.box, K.cylinder, K.hexnut, K.dome
lt, pt = K.lt, K.pt
BLOCK = 0.25
EPS = 0.0015                # how far a face stays inside the footprint, so neighbours never z-fight


# =====================================================================================================
# Materials
# =====================================================================================================

def colony_paint(seed, color, wear=0.45):
    """The Lift's chipped paint over steel, toned down: `wear` scales the chips and scratches (structure paint is
    worn, not wrecked)."""
    albedo, height, rough, metal = lt.paint_surface(seed, color, "#4a4e54")
    flat = lt.srgb(color)[None, None, :] * (1 + 0.05 * lt.fbm(seed + 9, 3.0)[..., None])
    albedo = flat * (1 - wear) + albedo * wear
    return albedo, height * wear, rough * wear + 0.55 * (1 - wear), metal * wear


def materials():
    tex, metal, half = lt.textured, lt.metal_surface, K.half_res
    return {
        "blue": tex("s_blue", half(colony_paint(501, "#3e6a8c")), normal_strength=1.5),
        "blue_dark": tex("s_blue_dark", half(colony_paint(502, "#2f526d")), normal_strength=1.5),
        "bone": tex("s_bone", half(colony_paint(503, "#d9d1bc", 0.35)), normal_strength=1.2),
        "mustard": tex("s_mustard", half(colony_paint(505, "#e0b530", 0.4)), normal_strength=1.2),
        "signal": tex("s_signal", half(colony_paint(507, "#c8423a", 0.4)), normal_strength=1.2),
        "graphite": tex("s_graphite", half(metal(509, "#2b2f35", 0.5, 0.6, wear=0.5, edge_light=0.25))),
        "zinc": tex("s_zinc", half(metal(511, "#9aa3ab", 0.42, 0.8, wear=0.6, edge_light=0.25)), normal_strength=2.0),
        "zinc_dark": tex("s_zinc_dark", half(metal(512, "#7d868e", 0.46, 0.8, wear=0.5, edge_light=0.25)), normal_strength=2.0),
        "steel": tex("s_steel", half(metal(513, "#8d9196", 0.32, 0.9, wear=0.5, edge_light=0.25)), normal_strength=2.0),
        "rubber": tex("s_rubber", half(lt.rubber_surface(515)), normal_strength=2.0),
        "hole": pt.plain("s_hole", "#0c0d0f", 0.9, 0.2),
        "lens": pt.plain("s_lens", "#6a1712", 0.15, 0.0),
    }


# =====================================================================================================
# Frames, profiles and paths
# =====================================================================================================

def frame(t, up):
    """(u, v) across a path whose tangent is `t`: v is `up` made perpendicular to t, u = v x t (so u, v, t is
    right-handed and a counter-clockwise profile faces out)."""
    t = Vector(t).normalized()
    v = Vector(up) - t * Vector(up).dot(t)
    v.normalize()
    return v.cross(t), v


def circle(r, n=24, phase=0.0):
    return [(r * math.cos(2 * math.pi * k / n + phase), r * math.sin(2 * math.pi * k / n + phase)) for k in range(n)]


def rect(hu, hv, chamfer=0.0):
    """A rectangle (half sizes along u and v), its corners cut by `chamfer`, counter-clockwise."""
    if chamfer <= 0:
        return [(-hu, -hv), (hu, -hv), (hu, hv), (-hu, hv)]
    c = chamfer
    return [(-hu + c, -hv), (hu - c, -hv), (hu, -hv + c), (hu, hv - c), (hu - c, hv), (-hu + c, hv), (-hu, hv - c), (-hu, -hv + c)]


def i_profile(hf, hh, tw, tf):
    """An I-beam section: flanges `2 hf` wide and `tf` thick, `2 hh` tall overall, web `2 tw` thick. Returns the
    outline and its convex pieces (for the end caps)."""
    pts = [(-hf, -hh), (hf, -hh), (hf, -hh + tf), (tw, -hh + tf), (tw, hh - tf), (hf, hh - tf), (hf, hh), (-hf, hh),
           (-hf, hh - tf), (-tw, hh - tf), (-tw, -hh + tf), (-hf, -hh + tf)]
    pieces = [(0, 1, 2, 3, 10, 11), (10, 3, 4, 9), (9, 4, 5, 6, 7, 8)]
    return pts, pieces


def u_profile(hu, hv, t):
    """A channel open towards +v: a back `t` thick along -v and two legs."""
    pts = [(-hu, -hv), (hu, -hv), (hu, hv), (hu - t, hv), (hu - t, -hv + t), (-hu + t, -hv + t), (-hu + t, hv), (-hu, hv)]
    pieces = [(0, 1, 4, 5), (1, 2, 3, 4), (0, 5, 6, 7)]
    return pts, pieces


def line(a, b, n=1):
    a, b = Vector(a), Vector(b)
    return [a.lerp(b, k / n) for k in range(n + 1)]


def arc(center, r, a0, a1, n=16, axis="z"):
    """Points on a circle of radius r about `center` in the plane normal to `axis` ("x", "y" or "z"), from angle a0 to
    a1 (radians; for "z" measured from +x towards +y, for "x" from +y towards +z, for "y" from +z towards +x)."""
    c = Vector(center)
    out = []
    for k in range(n + 1):
        a = a0 + (a1 - a0) * k / n
        ca, sa = math.cos(a), math.sin(a)
        d = {"z": (ca, sa, 0), "x": (0, ca, sa), "y": (sa, 0, ca)}[axis]
        out.append(c + Vector(d) * r)
    return out


def _tangents(path):
    out = []
    for i in range(len(path)):
        a, b = path[max(i - 1, 0)], path[min(i + 1, len(path) - 1)]
        out.append((b - a).normalized())
    return out


def _rings(path, prof, up):
    rings = []
    for p, t in zip(path, _tangents(path)):
        u, v = frame(t, up)
        rings.append([p + u * x + v * y for x, y in prof])
    return rings


def sweep(g, prof, path, up, mat, pieces=None, caps=(True, True), cap_mat=None):
    """A solid with section `prof` (counter-clockwise (u, v) points) swept along `path`. The end caps use the convex
    `pieces` (index tuples) when the profile is not convex."""
    path = [Vector(p) for p in path]
    rings = _rings(path, prof, up)
    n = len(prof)
    verts = [q for r in rings for q in r]
    faces = []
    for i in range(len(rings) - 1):
        for k in range(n):
            k1 = (k + 1) % n
            faces.append((i * n + k, i * n + k1, (i + 1) * n + k1, (i + 1) * n + k))
    g.add(verts, faces, mat)
    pieces = pieces or [tuple(range(n))]
    last = (len(rings) - 1) * n
    cm = cap_mat or mat
    if caps[0]:
        g.add(rings[0], [tuple(reversed(pc)) for pc in pieces], cm)
    if caps[1]:
        g.add([q for q in rings[-1]], [tuple(pc) for pc in pieces], cm)
    return last


def sweep_hollow(g, outer, inner, path, up, mat, inner_mat="hole", rims=(True, True), rim_mat=None):
    """A tube: `outer` and `inner` sections (same point count, counter-clockwise) swept along `path`, the bore lined
    with `inner_mat` and each end closed by a rim."""
    path = [Vector(p) for p in path]
    ro, ri = _rings(path, outer, up), _rings(path, inner, up)
    n = len(outer)
    m = len(path)

    def ring_faces(flip):
        f = []
        for i in range(m - 1):
            for k in range(n):
                k1 = (k + 1) % n
                q = (i * n + k, i * n + k1, (i + 1) * n + k1, (i + 1) * n + k)
                f.append(tuple(reversed(q)) if flip else q)
        return f

    g.add([q for r in ro for q in r], ring_faces(False), mat)
    g.add([q for r in ri for q in r], ring_faces(True), inner_mat)
    rm = rim_mat or mat
    for end, flip in ((0, True), (m - 1, False)):
        if not rims[0 if end == 0 else 1]:
            continue
        verts = ro[end] + ri[end]
        faces = []
        for k in range(n):
            k1 = (k + 1) % n
            q = (k, k1, n + k1, n + k)
            faces.append(tuple(reversed(q)) if flip else q)
        g.add(verts, faces, rm)


def to(d, up=(0, 0, 1)):
    """The 3x3 rotation that takes +z to `d` (and +y towards `up` where it can)."""
    d = Vector(d).normalized()
    u, v = frame(d, up if abs(Vector(up).normalized().dot(d)) < 0.99 else (1, 0, 0))
    return Matrix((u, v, d)).transposed()


# =====================================================================================================
# Hardware
# =====================================================================================================

def bolt(g, p, n, r=0.008, mat="zinc", washer=True):
    """A hex bolt head (with a washer) at p, its top along n."""
    p, n = Vector(p), Vector(n).normalized()
    if washer:
        cylinder(g, p - n * 0.0005, p + n * r * 0.18, r * 1.45, mat, segs=12)
    hexnut(g, p + n * r * 0.15, n, r, r * 0.65, mat)


def bolt_ring(g, center, n, radius, count, r=0.008, mat="zinc", phase=0.0):
    c, n = Vector(center), Vector(n).normalized()
    u, v = frame(n, (0, 0, 1) if abs(n.z) < 0.9 else (1, 0, 0))
    for k in range(count):
        a = 2 * math.pi * (k + 0.5 + phase) / count
        bolt(g, c + (u * math.cos(a) + v * math.sin(a)) * radius, n, r, mat)


def flange(g, center, d, r_in, r_out, thick, mat="steel", bolts=8, bolt_mat="zinc", bolt_r=None):
    """A half flange at a block face: a ring `thick` deep from `center` back along -d (d points out of the part, at
    the face), its bolt heads on the inner side so a neighbour's half flange makes one bolted joint."""
    c, d = Vector(center), Vector(d).normalized()
    path = [c - d * thick, c]
    sweep_hollow(g, circle(r_out, 28), circle(r_in, 28), path, _up_for(d), mat, inner_mat=mat)
    if bolts:
        bolt_ring(g, c - d * thick, -d, (r_in + r_out) / 2, bolts, bolt_r or max(0.006, (r_out - r_in) * 0.22), bolt_mat)


def _up_for(d):
    d = Vector(d)
    return (0, 0, 1) if abs(d.normalized().z) < 0.9 else (1, 0, 0)


def grate(g, center, d, r, bars=4, mat="graphite", depth=0.03):
    """A dark bore end with a few flat bars across it, set `depth` back from `center` against -d."""
    c, d = Vector(center), Vector(d).normalized()
    u, v = frame(d, _up_for(d))
    back = c - d * depth
    K.disc(g, back - d * 0.004, d, r, "hole", segs=24)
    w = r * 2 / (bars * 2 + 1)
    for k in range(bars):
        off = -r + w * (2 * k + 1.5)
        half_len = math.sqrt(max(r * r - off * off, 0)) * 0.98
        m = Matrix((u, v, d)).transposed()
        box(g, tuple(back + v * off), (half_len, w * 0.32, 0.006), mat, rot=m)


def pipe_run(g, a, b, r, mat, up=None, wall=0.012, inner="hole", rims=(True, True), n=24):
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    sweep_hollow(g, circle(r, n), circle(r - wall, n), [a, b], up or _up_for(d), mat, inner_mat=inner, rims=rims)


def pipe_elbow(g, center, R, r, a0, a1, axis, mat, up, wall=0.012, steps=14, n=24):
    """A bent pipe of section r on a centre line of radius R about `center` (see ``arc``); `up` is the bend's plane
    normal."""
    sweep_hollow(g, circle(r, n), circle(r - wall, n), arc(center, R, a0, a1, steps, axis), up, mat)


def band(g, center, d, r, width, mat):
    """A paint band (a thin sleeve) round a pipe."""
    c, d = Vector(center), Vector(d).normalized()
    sweep(g, circle(r, 24), [c - d * width / 2, c + d * width / 2], _up_for(d), mat)


# =====================================================================================================
# Running a batch
# =====================================================================================================

class Part:
    """One part to build: its id, size in blocks (the game's x, up, depth), a builder build(g) -> {mesh name: Geo}
    (or a Geo), a review title and notes."""

    def __init__(self, pid, size, build, title, lines=()):
        self.id, self.size, self.build, self.title, self.lines = pid, size, build, title, list(lines)

    @property
    def half(self):
        sx, sy, sz = self.size
        return (sx * BLOCK / 2, sz * BLOCK / 2, sy * BLOCK / 2)


def make(part, mats):
    """The part's objects under one root empty named after it."""
    root = K.make_empty(part.id)
    built = part.build()
    if isinstance(built, Geo):
        built = {"Body": built}
    for name, g in built.items():
        K.make_object(f"{name}", g, mats, parent=root)
    return root


def check_fits(part, info, slack=0.004):
    """Fails when the model pokes out of its footprint (glTF axes: x, y up, z depth)."""
    sx, sy, sz = (s * BLOCK / 2 + slack for s in part.size)
    lo, hi = info["min"], info["max"]
    for axis, h in zip(range(3), (sx, sy, sz)):
        if lo[axis] < -h or hi[axis] > h:
            raise SystemExit(f"{part.id}: model {lo.tolist()}..{hi.tolist()} leaves its {part.size} footprint")


def run_parts(parts, out=None, sheet_name="batch", sheet_title=""):
    """Builds, exports and icons every part; with `out` also the review sheets. Returns {id: info}."""
    K.reset_scene()
    mats = materials()
    infos, roots, icons = {}, {}, {}
    for part in parts:
        root = make(part, mats)
        path = K.MESHES / f"{part.id}.glb"
        i = infos[part.id] = K.export(root, path, animations=False)
        check_fits(part, i)
        if i["tris"] > 10000:
            raise SystemExit(f"{part.id}: {i['tris']} triangles (the budget is 10k)")
        K.report(path, i)
        ortho = max(part.half) * 2 * 1.45
        icons[part.id] = K.icon(root, K.ICONS / f"{part.id}_icon.png", K.ICON_VIEW, ortho)
        roots[part.id] = root
        for ob in [root, *root.children_recursive]:
            ob.hide_render = True
    if out is not None:
        import render_kit as rk
        out.mkdir(parents=True, exist_ok=True)
        sheets = []
        for part in parts:
            root = roots[part.id]
            for ob in [root, *root.children_recursive]:
                ob.hide_render = False
            sheets.append(rk.review(part.id, part.title, part.lines, {None: root}, [(None, icons[part.id])],
                                    [rk.fmt_stats(None, infos[part.id])], out, part.half))
            for ob in [root, *root.children_recursive]:
                ob.hide_render = True
            # render_kit leaves its ground and lights; the next part's stage replaces them
            for ob in list(bpy_objects()):
                if ob.name.startswith("RenderOnlyGround") or ob.type == "LIGHT":
                    import bpy
                    bpy.data.objects.remove(ob)
        overview(parts, out, sheet_name, sheet_title, infos)
    return infos


def bpy_objects():
    import bpy
    return list(bpy.context.scene.objects)


def overview(parts, out, name, title, infos):
    """One sheet of every part's three-quarter view, labelled with its name, size and triangles (for the phone)."""
    from PIL import Image, ImageDraw
    import render_kit as rk
    rl = rk.rl
    cols = 3
    cw, ch = 520, 460 + 90
    rows = (len(parts) + cols - 1) // cols
    sh = Image.new("RGB", (cols * cw + 40, rows * ch + 110), (246, 246, 244))
    d = ImageDraw.Draw(sh)
    d.text((20, 24), title, fill=rl.INK, font=rl.font(40, True))
    for k, part in enumerate(parts):
        x, y = 20 + (k % cols) * cw, 100 + (k // cols) * ch
        im = Image.open(out / f"{part.id}_three_quarter.png").convert("RGB")
        im = im.crop((0, 44, im.width, im.height))
        sh.paste(im, (x, y))
        sx, sy, sz = part.size
        d.text((x + 8, y + im.height + 6), part.title.rsplit(" (", 1)[0], fill=rl.INK, font=rl.font(26, True))
        d.text((x + 8, y + im.height + 40), f"{part.id}  {sx}x{sy}x{sz}  {infos[part.id]['tris']} tris", fill=rl.INK,
               font=rl.font(20))
    sh.save(out / f"{name}.png")
    print(f"wrote overview {out / (name + '.png')}")
