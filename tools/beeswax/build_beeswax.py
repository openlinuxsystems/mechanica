"""Build Beeswax and the rock beehive it comes from (block list batch 2: Worn Tile is set with beeswax).

What it builds (procedurally, from scratch; it shares the food models' helpers in tools/food):

* ``mods/vanilla/meshes/items/beeswax.glb`` - a slab of comb cut from a hive: golden wax, its top a field of capped
  hexagonal cells, the cut sides showing the cell walls. Lying, origin at the middle of its base.
* ``mods/vanilla/textures/items/beeswax_icon.png`` - the 256 x 256 icon.
* ``mods/vanilla/meshes/world/props/rock_beehive.glb`` - the wild hive: lobes of wax comb clinging to the side of a
  rock, sheets of open comb hanging under it, a few bees. Its back is flat against x = 0 (the rock's side) and it hangs
  from 0.35 to 1.25 m above its ground point, so the ``rock_hive`` scene can set it against a ``hive_rock``.

    pip install bpy==5.2.2 numpy pillow
    python tools/beeswax/build_beeswax.py                  # the game files
    python tools/beeswax/build_beeswax.py --render OUT_DIR # also review renders

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "food"))

import bpy  # noqa: E402,F401,I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh  # noqa: E402
import build_tomato as bt  # noqa: E402
import food_lib as fl  # noqa: E402
from mathutils import Vector  # noqa: E402

WAX = "#E7A92A"


def mats():
    return {
        "wax": fl.material("beeswax", WAX, rough=0.42, sss=0.2),
        "wax_old": fl.material("beeswax_old", "#B9792A", rough=0.55, sss=0.1),
        "cap": fl.material("beeswax_cap", "#F2C14E", rough=0.3, sss=0.25, coat=0.4),
        "dark": fl.material("hive_dark", "#2A1A0E", rough=0.9),
        "bee": fl.material("bee_body", "#E8B020", rough=0.5),
        "bee_band": fl.material("bee_band", "#1E1A16", rough=0.6),
        "wing": fl.material("bee_wing", "#DCE6EA", rough=0.2, coat=0.6),
    }


def hex_prism(z0, z1, c, r, mat=0):
    """An upright hexagonal prism (pointy side along X) from z0 to z1, centred at c (x, y)."""
    bm = bmesh.new()
    lo = [bm.verts.new((c[0] + r * math.cos(math.pi / 6 + k * math.pi / 3), c[1] + r * math.sin(math.pi / 6 + k * math.pi / 3), z0))
          for k in range(6)]
    hi = [bm.verts.new((v.co.x, v.co.y, z1)) for v in lo]
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    for k in range(6):
        bm.faces.new((lo[k], lo[(k + 1) % 6], hi[(k + 1) % 6], hi[k]))
    for f in bm.faces:
        f.material_index = mat
    return bm


def comb(width, depth, top, cell, seed, inside=None):
    """Capped cells over a rectangle: a wax wall per cell up to ``top`` and a domed cap set a little inside it."""
    rnd = random.Random(seed)
    walls, caps = [], []
    dx = cell * math.sqrt(3)
    dy = cell * 1.5
    rows = int(depth / dy) + 2
    cols = int(width / dx) + 2
    for j in range(-rows // 2, rows // 2 + 1):
        for i in range(-cols // 2, cols // 2 + 1):
            x = i * dx + (j % 2) * dx / 2
            y = j * dy
            if abs(x) > width / 2 - cell * 0.6 or abs(y) > depth / 2 - cell * 0.6:
                continue
            if inside and not inside(x, y):
                continue
            h = top + rnd.uniform(-0.0015, 0.0015)
            walls.append(hex_prism(0, h, (x, y), cell * 0.98))
            cap = bmesh.new()
            bmesh.ops.create_icosphere(cap, subdivisions=2, radius=cell * 0.8)
            for v in cap.verts:
                v.co = Vector((v.co.x + x, v.co.y + y, h - 0.0012 + max(0.0, v.co.z) * 0.35))
            if rnd.random() > 0.12:                               # a few cells left open
                caps.append(cap)
            else:
                cap.free()
                walls[-1] = fl.join([walls[-1], hex_prism(h - 0.004, h + 0.0002, (x, y), cell * 0.78, mat=1)])
    return walls, caps


def build_wax(name="Beeswax", m=None):
    m = m or mats()
    root = fl.empty(name)
    W, D, H = 0.17, 0.12, 0.04
    base = bmesh.new()
    bmesh.ops.create_cube(base, size=1.0)
    for v in base.verts:
        v.co = Vector((v.co.x * W, v.co.y * D, (v.co.z + 0.5) * H * 0.75))
    bmesh.ops.bevel(base, geom=base.edges[:], offset=0.008, segments=3)
    for v in base.verts:
        v.co += Vector((0, 0, 0.0012 * fl.noise3(v.co, 60.0, 3)))
    fl.from_bm(name + "_Block", base, [m["wax_old"]], root)
    walls, caps = comb(W * 0.96, D * 0.96, H, 0.0105, seed=5)
    fl.from_bm(name + "_Cells", fl.join(walls), [m["wax"], m["dark"]], root, smooth=False)
    fl.from_bm(name + "_Caps", fl.join(caps), [m["cap"]], root)
    return root


def build_hive(name="RockBeehive", m=None):
    """The wild hive against a rock's side (the rock is at -X)."""
    m = m or mats()
    root = fl.empty(name)
    # open comb hanging from the rock's side, the way wild bees build it: a few tongue-shaped sheets edge on to
    # the rock, capped cells on both faces, under a thin band of old wax where they hold on
    top = 1.25
    for k, (y, w, h) in enumerate(((-0.08, 0.26, 0.5), (0.0, 0.32, 0.65), (0.08, 0.24, 0.44))):
        x0 = 0.04 + w / 2

        def tongue(cx, cy, w=w, h=h):
            return cy < 0 or (cx / (w / 2)) ** 2 + (cy / (h / 2)) ** 2 < 0.92

        sheet = []
        for face in (1, -1):
            walls, caps = comb(w, h, 0.012, 0.0125, seed=60 + 10 * k + face, inside=tongue)
            for bm in walls + caps:
                for v in bm.verts:
                    cx, cy, cz = v.co
                    # comb x -> out from the rock (hive x), comb y -> down the sheet, comb z -> out of the face (hive y)
                    v.co = Vector((x0 + cx, y + face * cz, top - h / 2 - cy))
                sheet.append(bm)
        fl.from_bm(f"{name}_Sheet{k}", fl.join(sheet), [m["wax"], m["dark"]], root, smooth=False)
    band = bmesh.new()
    bmesh.ops.create_uvsphere(band, u_segments=24, v_segments=12, radius=1.0)
    for v in band.verts:
        n = v.co.copy()
        v.co = Vector((max(n.x, -0.3) * 0.08 + 0.05, n.y * 0.2, n.z * 0.045 + top + 0.02)) \
            * 1.0 + Vector((0, 0, 0.008 * fl.noise3(n, 6.0, 21)))
    fl.from_bm(name + "_Band", band, [m["wax_old"]], root)
    # bees: a few round bodies with dark bands and clear wings, about the hive
    rnd = random.Random(7)
    for k in range(5):
        p = Vector((0.38 + rnd.uniform(0, 0.15), rnd.uniform(-0.25, 0.25), rnd.uniform(0.7, 1.2)))
        yaw = rnd.uniform(0, 2 * math.pi)
        d = Vector((math.cos(yaw), math.sin(yaw), 0))
        side = Vector((-d.y, d.x, 0))
        body = bmesh.new()
        bmesh.ops.create_uvsphere(body, u_segments=12, v_segments=8, radius=1.0)
        for v in body.verts:
            v.co = p + d * v.co.x * 0.016 + side * v.co.y * 0.011 + Vector((0, 0, v.co.z * 0.011))
        bands = []
        for t in (-0.004, 0.005):
            b = bmesh.new()
            bmesh.ops.create_uvsphere(b, u_segments=12, v_segments=6, radius=1.0)
            for v in b.verts:
                v.co = p + d * (t + v.co.x * 0.0028) + side * v.co.y * 0.0118 + Vector((0, 0, v.co.z * 0.0118))
            bands.append(b)
        fl.from_bm(f"{name}_Bee{k}", body, [m["bee"]], root)
        fl.from_bm(f"{name}_Bee{k}_Bands", fl.join(bands), [m["bee_band"]], root)
        wings = []
        for s in (1, -1):
            w = bmesh.new()
            bmesh.ops.create_uvsphere(w, u_segments=10, v_segments=6, radius=1.0)
            for v in w.verts:
                v.co = p + Vector((0, 0, 0.011)) + side * s * 0.012 + side * s * v.co.y * 0.012 + d * v.co.x * 0.007 \
                    + Vector((0, 0, v.co.z * 0.0012))
            wings.append(w)
        fl.from_bm(f"{name}_Bee{k}_Wings", fl.join(wings), [m["wing"]], root)
    return root


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    fl.reset()
    wax = build_wax()
    c, _ = fl.bounds(fl.tree(wax))
    wax.location.x -= c.x
    wax.location.y -= c.y
    bt.export(fl.tree(wax), bt.MESHES / "items" / "beeswax.glb")
    fl.auto_icon(fl.tree(wax), bt.ICONS / "beeswax_icon.png", bt.ICON_VIEW, fill=1.15)
    if out:
        out.mkdir(parents=True, exist_ok=True)
        fl.render_view(fl.tree(wax), out / "beeswax.png", (0.5, -0.7, 0.55), ortho=0.26, target=(0, 0, 0.02), size=700)
    fl.reset()
    hive = build_hive()
    bt.export(fl.tree(hive), bt.MESHES / "world" / "props" / "rock_beehive.glb")
    if out:
        rock = bmesh.new()
        bmesh.ops.create_icosphere(rock, subdivisions=3, radius=1.1)
        for v in rock.verts:
            v.co = v.co * (1 + 0.05 * fl.noise3(v.co.normalized(), 4.0, 1)) + Vector((-1.1, 0, 0.7))
        r = fl.from_bm("ReviewRock", rock, [fl.material("review_rock", "#85837C", rough=0.9)], None)
        fl.render_view(fl.tree(hive) + [r], out / "rock_beehive.png", (2.2, -2.6, 1.4), ortho=2.4, target=(-0.3, 0, 0.8),
                       size=700)
        fl.render_view(fl.tree(hive), out / "rock_beehive_close.png", (1.6, -1.3, 0.9), ortho=0.9, target=(0.18, 0, 0.95),
                       size=700)
        bpy.data.objects.remove(r)


if __name__ == "__main__":
    main(sys.argv[1:])
