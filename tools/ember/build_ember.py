"""Build the Ember item and its icon (block list batch 1, after the Scrap Mechanic wiki's Ember, 2026-10-20).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/ember.glb`` - a 1 x 1 x 1 block item (0.25 m cube centred on the origin, front -Y,
  Z up): a short log of charcoal lying along X, its crust split into plates over a glowing orange core, held between
  two bolted gunmetal end caps with copper rings (a salvager's way of carrying fire).
* ``mods/vanilla/textures/items/ember_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a clear
  background, three-quarter from the front right.

It stays inside its 0.25 m cube, which is the collision box.

    pip install bpy==5.2.2 numpy pillow
    python tools/ember/build_ember.py                  # writes the .glb and the icon
    python tools/ember/build_ember.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Matrix

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder = bl.Geo, bl.box, bl.cylinder

HALF = 0.125                  # the item's half size: one block
FLOOR = -HALF
R = 0.058                     # the log's radius
LEN = 0.085                   # the log's half length (between the caps)
CAP = 0.014                   # end cap thickness
ZC = FLOOR + R + 0.01         # the log's axis height: it lies on its caps


def make_materials():
    return {
        "char": lt.textured("em_char", gc.half_res(lt.metal_surface(811, "#0f0d0c", 0.9, 0.0, wear=1.0, edge_light=0.1)),
                            normal_strength=1.4),
        "ash": pt.plain("em_ash", "#5e5853", 0.9, 0.0),
        "core": pt.plain("em_core", "#ff4a0a", 0.6, 0.0, emission="#ff5a12", strength=9.0),
        "hot": pt.plain("em_hot", "#ffb347", 0.5, 0.0, emission="#ffb347", strength=4.0),
        "gun": lt.textured("em_gun", gc.half_res(lt.metal_surface(813, "#4f5257", 0.46, 0.75, wear=0.6, edge_light=0.3)),
                           normal_strength=0.7),
        "copper": lt.textured("em_copper", gc.half_res(lt.metal_surface(817, "#c07a42", 0.32, 0.95, wear=0.5,
                                                                          edge_light=0.35)), normal_strength=0.5),
        "steel": lt.textured("em_steel", gc.half_res(lt.metal_surface(819, "#b9bcc1", 0.3, 0.9, wear=0.5, edge_light=0.4)),
                             normal_strength=0.6),
    }


def build_ember():
    g = Geo()
    rng = random.Random(23)
    # the glowing core, just under the crust, so the cracks between the plates glow
    cylinder(g, (-LEN, 0, ZC), (LEN, 0, ZC), R - 0.008, "core", segs=24)
    # the charcoal crust: plates round the log in three rings, each its own size, tilt and thickness
    facets, rings = 11, 4
    seg = 2 * LEN / rings
    for i in range(rings):
        x = -LEN + seg * (i + 0.5)
        turn = rng.uniform(0, 2 * math.pi / facets)
        for k in range(facets):
            a = turn + 2 * math.pi * k / facets
            thick = rng.uniform(0.005, 0.013)
            r = R - thick + rng.uniform(-0.002, 0.003)
            width = R * math.sin(math.pi / facets) * rng.uniform(0.7, 0.95)
            length = seg / 2 * rng.uniform(0.7, 0.98)
            rot = Matrix.Rotation(a, 3, "X") @ Matrix.Rotation(rng.uniform(-0.12, 0.12), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.1, 0.1), 3, "Y")
            c = (x + rng.uniform(-0.002, 0.002), -math.sin(a) * r, ZC + math.cos(a) * r)
            box(g, c, (length, width, thick), "char" if rng.random() > 0.06 else "ash", bevel=0.004, rot=rot)
    # a few bright hot spots in the deepest cracks
    for _ in range(9):
        a = rng.uniform(0, 2 * math.pi)
        x = rng.uniform(-LEN + 0.01, LEN - 0.01)
        c = (x, -math.sin(a) * (R - 0.009), ZC + math.cos(a) * (R - 0.009))
        box(g, c, (0.006, 0.004, 0.004), "hot", rot=Matrix.Rotation(a, 3, "X"))
    # the end caps: a gunmetal disc, a copper ring, four bolts, a steel boss
    for s in (-1, 1):
        x0 = s * LEN
        x1 = s * (LEN + CAP)
        cylinder(g, (x0, 0, ZC), (x1, 0, ZC), R + 0.006, "gun", segs=32)
        cylinder(g, (x0 - s * 0.006, 0, ZC), (x0, 0, ZC), R + 0.002, "copper", segs=32)
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            p = (x1, -math.sin(a) * (R - 0.012), ZC + math.cos(a) * (R - 0.012))
            bl.hexnut(g, p, (s, 0, 0), 0.0065, 0.004, "steel")
        cylinder(g, (x1, 0, ZC), (x1 + s * 0.008, 0, ZC), 0.018, "steel", segs=20)
    return g


def build_item(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("Ember", location=offset)}
    bl.make_object("Ember", build_ember(), mats, rig["root"])
    return rig


ICON_VIEW = (1.25, -1.6, 1.0)


def main():
    bbe.reset_scene()
    mats = make_materials()
    item = build_item(mats)
    bbe.export(item["root"], MESHES / "ember.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(item["root"]), ICONS / "ember_icon.png", view=ICON_VIEW, ortho=0.3,
                    target=(0.0, 0.0, ZC))
    for p in (MESHES / "ember.glb", ICONS / "ember_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        out = Path(sys.argv[sys.argv.index("--render") + 1])
        out.mkdir(parents=True, exist_ok=True)
        import render_lift as rl
        ground = rl.setup_world()
        ground.location.z = FLOOR
        for view, loc, ortho in (("three_quarter", (0.7, -1.0, 0.55), 0.34), ("end", (1.2, -0.2, 0.25), 0.3),
                                 ("top", (0.0, -0.0001, 1.2), 0.32)):
            rl.render(rl.camera(f"em_{view}", loc, (0, 0, ZC), ortho=ortho), out / f"ember_{view}.png", 700, 700)


if __name__ == "__main__":
    main()
