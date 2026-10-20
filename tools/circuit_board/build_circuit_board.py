"""Build the Circuit Board item and its icon (after Chase's Scrap Mechanic reference, 2026-10-20).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/circuit_board.glb`` - a 1 x 1 x 1 block item (0.25 m cube centred on the origin, front
  -Y, Z up): a green board screwed onto a low gunmetal tray. On the board: a tall capacitor with a cyan band at the
  back left, a dark chip with steel legs, a white relay with a red cap, a mustard connector with copper pins at the
  front right edge, an orange and a blue wire, a lit cyan LED, small parts and copper traces between them.
* ``mods/vanilla/textures/items/circuit_board_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256
  on a clear background, three-quarter from the front right.

It stays inside its 0.25 m cube, which is the collision box.

    pip install bpy==5.2.2 numpy pillow
    python tools/circuit_board/build_circuit_board.py                  # writes the .glb and the icon
    python tools/circuit_board/build_circuit_board.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, dome = bl.Geo, bl.box, bl.cylinder, bl.dome

HALF = 0.125                  # the item's half size: one block
FLOOR = -HALF
TRAY = 0.11                   # half size of the gunmetal tray
TRAY_TOP = FLOOR + 0.002 + 0.02
BOARD = 0.102                 # half size of the board
BOARD_TOP = TRAY_TOP + 0.007
TOP = BOARD_TOP               # for the renderer: the face the parts stand on


def make_materials():
    return {
        "board": pt.plain("cb_board", "#2f7d3c", 0.55, 0.0),
        "mask": pt.plain("cb_mask", "#3c9548", 0.5, 0.0),
        "chip": pt.plain("cb_chip", "#1c1e21", 0.6, 0.1),
        "white": pt.plain("cb_white", "#d8dbd5", 0.5, 0.0),
        "red": pt.plain("cb_red", "#b8322b", 0.5, 0.0),
        "mustard": pt.plain("cb_mustard", "#d6a12a", 0.5, 0.0),
        "orange": pt.plain("cb_orange_wire", "#d4661a", 0.45, 0.0),
        "blue": pt.plain("cb_blue_wire", "#2f6fc4", 0.45, 0.0),
        "purple": pt.plain("cb_purple", "#5b3f8f", 0.55, 0.0),
        "tan": pt.plain("cb_tan", "#b89a6a", 0.6, 0.0),
        "sleeve": pt.plain("cb_sleeve", "#2d3b42", 0.45, 0.2),
        "led": pt.plain("cb_led", "#1c8fa6", 0.2, 0.0, emission="#22c7e6", strength=2.0),
        "glow": pt.plain("cb_glow", "#1c8fa6", 0.3, 0.0, emission="#22c7e6", strength=0.6),
        "solder": pt.plain("cb_solder", "#c9ccd0", 0.3, 1.0),
        "gun": lt.textured("cb_gun", gc.half_res(lt.metal_surface(703, "#4f5257", 0.46, 0.75, wear=0.6, edge_light=0.3)),
                           normal_strength=0.7),
        "steel": lt.textured("cb_steel", gc.half_res(lt.metal_surface(707, "#b9bcc1", 0.3, 0.9, wear=0.5, edge_light=0.4)),
                             normal_strength=0.6),
        "copper": lt.textured("cb_copper", gc.half_res(lt.metal_surface(709, "#c07a42", 0.32, 0.95, wear=0.5, edge_light=0.35)),
                              normal_strength=0.5),
    }


def trace(g, pts, w=0.0022):
    """A copper trace along a polyline on the board, with a pad at each end."""
    z = BOARD_TOP + 0.0004
    for k, ((x0, y0), (x1, y1)) in enumerate(zip(pts, pts[1:])):
        cx, cy, hx, hy = (x0 + x1) / 2, (y0 + y1) / 2, abs(x1 - x0) / 2 + w, abs(y1 - y0) / 2 + w
        box(g, (cx, cy, z + k * 0.00015), (hx, hy, 0.0004), "copper")  # each leg a hair higher: no flicker at turns
    for x, y in (pts[0], pts[-1]):
        cylinder(g, (x, y, BOARD_TOP), (x, y, BOARD_TOP + 0.0012), 0.0045, "solder", segs=10)


def wire(g, a, b, lift, mat, r=0.0038):
    """An insulated wire arching from a to b on the board, its ends in steel ferrules."""
    z = BOARD_TOP + 0.004
    pa, pb = (a[0], a[1], z), (b[0], b[1], z)
    pts = bl.bezier(pa, (a[0], a[1], z + lift), (b[0], b[1], z + lift), pb, n=18)
    bl.sweep(g, pts, r, mat, segs=10)
    for x, y in (a, b):
        cylinder(g, (x, y, BOARD_TOP), (x, y, BOARD_TOP + 0.009), r + 0.0016, "steel", segs=10)


def build_board():
    g = Geo()
    # the gunmetal tray with screws in its corners, and the board on it
    box(g, (0, 0, (FLOOR + 0.002 + TRAY_TOP) / 2), (TRAY, TRAY, (TRAY_TOP - FLOOR - 0.002) / 2), "gun", bevel=0.008)
    box(g, (0, 0, TRAY_TOP + 0.0035), (BOARD, BOARD, 0.0035), "board", bevel=0.0015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = (sx * (BOARD - 0.011), sy * (BOARD - 0.011))
            cylinder(g, (*p, BOARD_TOP), (*p, BOARD_TOP + 0.0008), 0.0085, "solder", segs=16)
            bl.hexnut(g, (*p, BOARD_TOP), (0, 0, 1), 0.006, 0.0045, "steel")
    # a lighter solder-mask panel under the chip
    box(g, (0.042, 0.03, BOARD_TOP + 0.0002), (0.045, 0.04, 0.0003), "mask")
    # copper traces between the parts
    for pts in (((-0.085, 0.0), (-0.085, -0.03), (-0.045, -0.03)),
                ((-0.02, 0.05), (-0.02, 0.012), (0.004, 0.012)),
                ((0.006, -0.012), (0.006, -0.04), (0.035, -0.04)),
                ((0.088, 0.075), (0.088, 0.02)),
                ((-0.06, 0.088), (0.02, 0.088)),
                ((-0.03, -0.088), (-0.03, -0.06), (-0.005, -0.06)),
                ((0.065, -0.002), (0.088, -0.002), (0.088, -0.02))):
        trace(g, pts)
    # the capacitor: a tall can at the back left with a cyan band and a scored steel top
    cx, cy, r, h = -0.052, 0.048, 0.03, 0.072
    cylinder(g, (cx, cy, BOARD_TOP), (cx, cy, BOARD_TOP + 0.004), r + 0.004, "chip", segs=28)
    cylinder(g, (cx, cy, BOARD_TOP + 0.004), (cx, cy, BOARD_TOP + h - 0.006), r, "sleeve", segs=28)
    cylinder(g, (cx, cy, BOARD_TOP + h - 0.018), (cx, cy, BOARD_TOP + h - 0.012), r + 0.0008, "glow", segs=28)
    cylinder(g, (cx, cy, BOARD_TOP + h - 0.006), (cx, cy, BOARD_TOP + h), r - 0.002, "steel", segs=28)
    for rot in (0, math.pi / 2):
        c, s = math.cos(rot), math.sin(rot)
        box(g, (cx, cy, BOARD_TOP + h + 0.0002), (abs(c) * 0.02 + 0.0012, abs(s) * 0.02 + 0.0012, 0.0006), "chip")
    # the chip: a dark package with steel legs down its long sides and a pin-one dot
    x, y, hx, hy = 0.045, 0.032, 0.03, 0.022
    box(g, (x, y, BOARD_TOP + 0.0075), (hx, hy, 0.0065), "chip", bevel=0.0015)
    for k in range(7):
        px = x - hx + 0.006 + k * (2 * hx - 0.012) / 6
        for sy in (-1, 1):
            box(g, (px, y + sy * (hy + 0.003), BOARD_TOP + 0.004), (0.0018, 0.0035, 0.0018), "steel", bevel=0.0006)
    dome(g, (x - hx + 0.007, y - hy + 0.007, BOARD_TOP + 0.014), (0, 0, 1), 0.0025, "white", segs=10)
    # the relay: a white block with a red end cap
    x, y = 0.012, 0.072
    box(g, (x, y, BOARD_TOP + 0.013), (0.026, 0.015, 0.013), "white", bevel=0.003)
    box(g, (x + 0.026, y, BOARD_TOP + 0.013), (0.006, 0.0155, 0.0135), "red", bevel=0.003)
    # the connector: a mustard housing on the front right edge, copper pins sticking out past the board
    x, y = 0.07, -0.052
    box(g, (x, y, BOARD_TOP + 0.011), (0.022, 0.03, 0.011), "mustard", bevel=0.003)
    box(g, (x + 0.022, y, BOARD_TOP + 0.011), (0.0015, 0.024, 0.006), "chip")
    for k in range(4):
        py = y - 0.018 + k * 0.012
        box(g, (x + 0.031, py, BOARD_TOP + 0.011), (0.009, 0.0016, 0.0016), "copper", bevel=0.0005)
    # wires: an orange one looping up over the front left and a blue one across the middle
    wire(g, (-0.086, 0.008), (-0.035, -0.07), 0.045, "orange")
    wire(g, (-0.012, -0.012), (0.05, -0.028), 0.025, "blue")
    # small parts: a purple driver chip, tan capacitors, a resistor row, a lit LED
    box(g, (0.025, -0.082, BOARD_TOP + 0.004), (0.014, 0.009, 0.004), "purple", bevel=0.0012)
    for x, y in ((-0.072, -0.052), (-0.0, 0.036), (-0.016, 0.03), (0.082, 0.052)):
        box(g, (x, y, BOARD_TOP + 0.0025), (0.004, 0.0065, 0.0025), "tan", bevel=0.001)
        for sy in (-1, 1):
            box(g, (x, y + sy * 0.0065, BOARD_TOP + 0.0025), (0.0042, 0.0012, 0.0026), "solder")
    for k in range(3):
        x = -0.085 + k * 0.011
        cylinder(g, (x, -0.066, BOARD_TOP + 0.004), (x, -0.086, BOARD_TOP + 0.004), 0.0028, "tan", segs=10)
        for y in (-0.064, -0.088):
            cylinder(g, (x, y, BOARD_TOP), (x, y, BOARD_TOP + 0.004), 0.0008, "steel", segs=6)
    cylinder(g, (0.086, 0.08, BOARD_TOP), (0.086, 0.08, BOARD_TOP + 0.004), 0.0055, "chip", segs=14)
    dome(g, (0.086, 0.08, BOARD_TOP + 0.004), (0, 0, 1), 0.0045, "led", segs=12, height=0.006)
    return g


def build_item(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("CircuitBoard", location=offset)}
    bl.make_object("Board", build_board(), mats, rig["root"])
    return rig


ICON_VIEW = (1.25, -1.6, 1.45)


def main():
    bbe.reset_scene()
    mats = make_materials()
    item = build_item(mats)
    bbe.export(item["root"], MESHES / "circuit_board.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(item["root"]), ICONS / "circuit_board_icon.png", view=ICON_VIEW, ortho=0.34,
                    target=(0.0, 0.0, -0.075))
    for p in (MESHES / "circuit_board.glb", ICONS / "circuit_board_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_circuit_board
        render_circuit_board.render_all(item, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
