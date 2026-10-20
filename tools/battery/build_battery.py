"""Build the Battery item (id battery_pack) and its icon (after Chase's Scrap Mechanic reference, 2026-10-20: "more of a
car battery design").

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/battery_pack.glb`` - a 1 x 1 x 1 block item (0.25 m cube centred on the origin, front
  -Y, Z up): a car battery. A dark gunmetal case with ribbed ends and a mustard band round its top (the Battery
  Container's lid colour), a recessed mustard lid with a row of vent caps, two copper terminal posts (the + one with a
  red collar, the - one dark) on raised bosses, a lit teal charge eye, a folding steel carry strap, and a label plate
  with a copper lightning bolt on the front.
* ``mods/vanilla/textures/items/battery_pack_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the front right.

It stays inside its 0.25 m cube, which is the collision box.

    pip install bpy==5.2.2 numpy pillow
    python tools/battery/build_battery.py                  # writes the .glb and the icon
    python tools/battery/build_battery.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
sys.path.insert(0, str(HERE.parent / "battery_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)
import build_battery_container as bc  # noqa: E402  (the lightning bolt mark)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, dome, hexnut, sweep = bl.Geo, bl.box, bl.cylinder, bl.dome, bl.hexnut, bl.sweep

HALF = 0.125                  # the item's half size: one block
FLOOR = -HALF
CX, CY = 0.115, 0.08          # the case's half length and depth (long along X like a car battery)
Z0, Z1 = FLOOR + 0.002, 0.045 # the case's bottom and the lid's top
TOP = Z1


def make_materials():
    m = gc.make_materials()
    m.update({
        "mustard": lt.textured("bt_mustard", gc.half_res(lt.paint_surface(823, "#d29a22", "#4a4d52")), normal_strength=0.4),
        "red": lt.textured("bt_red", gc.half_res(lt.paint_surface(821, "#b8322b", "#4a4d52")), normal_strength=0.4),
        "dark": pt.plain("bt_dark", "#202326", 0.5, 0.4),
        "case": pt.plain("bt_case", "#2b2e33", 0.55, 0.15),
        "eye": pt.plain("bt_eye", "#0e8577", 0.2, 0.0, emission="#13e0c4", strength=2.5),
    })
    return m


def build_case():
    g = Geo()
    # the case: a dark body, a mustard band round the top, a recessed mustard lid
    zb = Z1 - 0.03
    box(g, (0, 0, (Z0 + zb) / 2), (CX, CY, (zb - Z0) / 2), "case", bevel=0.006)
    box(g, (0, 0, zb + 0.011), (CX + 0.002, CY + 0.002, 0.011), "mustard", bevel=0.005)
    box(g, (0, 0, Z1 - 0.004), (CX - 0.012, CY - 0.012, 0.004), "mustard", bevel=0.003)
    # a foot rim round the bottom and ribs up the ends
    box(g, (0, 0, Z0 + 0.008), (CX + 0.003, CY + 0.003, 0.008), "gun_dark", bevel=0.004)
    for sx in (-1, 1):
        for k in range(4):
            y = -CY + 0.025 + k * (2 * CY - 0.05) / 3
            box(g, (sx * (CX + 0.002), y, (Z0 + zb) / 2 + 0.006), (0.004, 0.006, (zb - Z0) / 2 - 0.016), "gun", bevel=0.002)
        # strap lugs on the ends, and the strap folded down over the lid
        box(g, (sx * (CX + 0.003), 0, zb - 0.004), (0.005, 0.016, 0.01), "gun", bevel=0.003)
    pts = [Vector((-CX - 0.004, 0, zb - 0.004)), Vector((-CX - 0.004, 0, Z1 + 0.004))]
    pts += [Vector((x, 0, Z1 + 0.008)) for x in (-CX + 0.01, -0.04, 0.04, CX - 0.01)]
    pts += [Vector((CX + 0.004, 0, Z1 + 0.004)), Vector((CX + 0.004, 0, zb - 0.004))]
    sweep(g, bl.bezier(pts[0], pts[1], pts[2], pts[3], n=8) + bl.bezier(pts[3], pts[4], pts[5], pts[6], n=8)[1:] +
          [pts[7]], 0.0035, "steel", segs=10)
    box(g, (0, 0, Z1 + 0.0085), (0.03, 0.007, 0.0045), "dark", bevel=0.003)
    # a row of vent caps across the back of the lid
    for k in range(6):
        x = -0.075 + k * 0.03
        cylinder(g, (x, 0.042, Z1), (x, 0.042, Z1 + 0.005), 0.0085, "dark", segs=16)
        box(g, (x, 0.042, Z1 + 0.0052), (0.006, 0.0012, 0.0004), "hole")
    # terminals toward the front corners: a boss, a collar (red for +), a copper post and nut
    for x, collar in ((0.078, "red"), (-0.078, "dark")):
        cylinder(g, (x, -0.04, Z1), (x, -0.04, Z1 + 0.008), 0.02, "dark", segs=24)
        cylinder(g, (x, -0.04, Z1 + 0.008), (x, -0.04, Z1 + 0.013), 0.017, collar, segs=24)
        cylinder(g, (x, -0.04, Z1 + 0.013), (x, -0.04, Z1 + 0.04), 0.009, "copper", segs=16, r1=0.0075)
        hexnut(g, (x, -0.04, Z1 + 0.02), (0, 0, 1), 0.013, 0.007, "copper")
    zz = Z1 + 0.0004
    box(g, (0.078, -0.067, zz), (0.007, 0.0018, 0.0006), "red")
    box(g, (0.078, -0.067, zz + 0.0002), (0.0018, 0.007, 0.0006), "red")  # a hair higher: no flicker where they cross
    box(g, (-0.078, -0.067, zz), (0.007, 0.0018, 0.0006), "dark")
    # the charge eye: a bezel and a lit teal lens in the middle front of the lid
    cylinder(g, (0, -0.03, Z1), (0, -0.03, Z1 + 0.004), 0.013, "gun", segs=24)
    dome(g, (0, -0.03, Z1 + 0.004), (0, 0, 1), 0.0085, "eye", segs=16, height=0.004)
    # the label plate on the front with a copper lightning bolt, bolted at its corners
    y = -CY - 0.001
    lz = (Z0 + zb) / 2 + 0.008
    box(g, (0, y - 0.002, lz), (0.07, 0.003, 0.03), "gun", bevel=0.003)
    box(g, (0, y - 0.0052, lz), (0.062, 0.0006, 0.023), "dark")
    bc.bolt_mark(g, (0.0, lz), y - 0.0056, -1, 0.019, "copper")
    for dx in (-0.062, 0.062):
        for dz in (-0.022, 0.022):
            dome(g, (dx, y - 0.005, lz + dz), (0, -1, 0), 0.0026, "bolt")
    # the same plate on the back, plain
    box(g, (0, CY + 0.003, lz), (0.07, 0.003, 0.03), "gun", bevel=0.003)
    return g


def build_item(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("Battery", location=offset)}
    bl.make_object("Case", build_case(), mats, rig["root"])
    return rig


ICON_VIEW = (1.3, -1.7, 1.2)


def main():
    bbe.reset_scene()
    mats = make_materials()
    item = build_item(mats)
    bbe.export(item["root"], MESHES / "battery_pack.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(item["root"]), ICONS / "battery_pack_icon.png", view=ICON_VIEW, ortho=0.3,
                    target=(0.0, 0.0, -0.035))
    for p in (MESHES / "battery_pack.glb", ICONS / "battery_pack_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_battery
        render_battery.render_all(item, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
