"""Build the Component Kit item and its icon (after Chase's Scrap Mechanic reference, 2026-10-20).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/component_kit.glb`` - a 1 x 1 x 1 block item (0.25 m cube centred on the origin, front
  -Y, Z up): a squat square hard case. A gunmetal shell with a dark rubber band round its middle and rubber guards on
  its eight corners, an orange-painted lid frame round a glass window, and under the glass a cyan gear with a smaller
  copper one meshing with it, bolted to a dark tray. Two steel latches and a small label plate on the front.
* ``mods/vanilla/textures/items/component_kit_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256
  on a clear background, three-quarter from the front right.

It stays inside its 0.25 m cube, which is the collision box.

    pip install bpy==5.2.2 numpy pillow
    python tools/component_kit/build_component_kit.py                  # writes the .glb and the icon
    python tools/component_kit/build_component_kit.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Matrix, Vector

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
CASE = (0.11, 0.11, 0.07)  # the case's half sizes; it stands on the floor
CASE_Z = FLOOR + 0.002 + CASE[2]
TOP = CASE_Z + CASE[2]
WINDOW = 0.064                # half size of the window in the lid
WELL = 0.032                  # depth of the tray under the glass


def make_materials():
    return {
        "hole": pt.plain("ck_hole", "#0c0d0f", 0.9, 0.2),
        "glass": gc.glass_material(),
        "tray": pt.plain("ck_tray", "#24272b", 0.7, 0.4),
        "gear": pt.plain("ck_gear", "#1c8fa6", 0.3, 0.6, emission="#22c7e6", strength=0.5),
        "orange": lt.textured("ck_orange", gc.half_res(lt.paint_surface(601, "#d4661a", "#4a4d52")), normal_strength=0.8),
        "gun": lt.textured("ck_gun", gc.half_res(lt.metal_surface(603, "#4f5257", 0.46, 0.75, wear=0.6, edge_light=0.3)),
                           normal_strength=0.7),
        "rubber": lt.textured("ck_rubber", gc.half_res(lt.rubber_surface(605)), normal_strength=0.5),
        "steel": lt.textured("ck_steel", gc.half_res(lt.metal_surface(607, "#b9bcc1", 0.3, 0.9, wear=0.5, edge_light=0.4)),
                             normal_strength=0.6),
        "copper": lt.textured("ck_copper", gc.half_res(lt.metal_surface(609, "#b5673a", 0.34, 0.95, wear=0.6, edge_light=0.35)),
                              normal_strength=0.7),
    }


def gear(g, c, z0, z1, r_root, r_tip, teeth, r_hole, mat, phase=0.0):
    """A spur gear about Z: trapezoid teeth on a rim, a round hole in the middle, flat faces top and bottom."""
    pts = []
    for k in range(teeth):
        a = 2 * math.pi * k / teeth + phase
        w = math.pi / teeth
        for da, r in ((-0.55 * w, r_root), (-0.3 * w, r_tip), (0.3 * w, r_tip), (0.55 * w, r_root)):
            pts.append((r, a + da))
    n = len(pts)
    cx, cy = c
    outer = [(cx + r * math.cos(a), cy + r * math.sin(a)) for r, a in pts]
    inner = [(cx + r_hole * math.cos(a), cy + r_hole * math.sin(a)) for _, a in pts]
    verts = [(x, y, z0) for x, y in outer] + [(x, y, z1) for x, y in outer] + \
            [(x, y, z0) for x, y in inner] + [(x, y, z1) for x, y in inner]
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))                         # outer wall
        faces.append((3 * n + i, 3 * n + j, 2 * n + j, 2 * n + i))  # hole wall
        faces.append((n + i, n + j, 3 * n + j, 3 * n + i))          # top face
        faces.append((2 * n + i, 2 * n + j, j, i))                  # bottom face
    return g.add(verts, faces, mat)


def build_case():
    g = Geo()
    hx, hy, hz = CASE
    # the shell: a gunmetal lower half and the lid above the band, solid up to the tray under the window
    z_tray = TOP - WELL
    box(g, (0, 0, CASE_Z - hz / 2), (hx, hy, hz / 2), "gun", bevel=0.01)
    lid0 = CASE_Z + 0.004
    box(g, (0, 0, (lid0 + z_tray) / 2), (hx - 0.002, hy - 0.002, (z_tray - lid0) / 2), "gun", bevel=0.008)
    # the rubber band round the middle where the lid closes
    box(g, (0, 0, CASE_Z + 0.004), (hx + 0.003, hy + 0.003, 0.008), "rubber", bevel=0.003)
    # the lid frame: orange paint round the window, as four bars, a little proud of the shell's top edge
    t = TOP
    w = WINDOW
    fw = (hx - 0.002 - w) / 2
    zc, zh = (z_tray - 0.006 + t) / 2, (t - z_tray + 0.006) / 2
    for sx in (-1, 1):
        box(g, (sx * (w + fw), 0, zc), (fw, hy - 0.002, zh), "orange", bevel=0.007)
        box(g, (0, sx * (w + fw), zc), (w + 0.002, fw, zh), "orange", bevel=0.007)
    # an inner lip round the glass
    for sx in (-1, 1):
        box(g, (sx * (w + 0.003), 0, t + 0.002), (0.003, w + 0.006, 0.003), "gun", bevel=0.0015)
        box(g, (0, sx * (w + 0.003), t + 0.002), (w + 0.006, 0.003, 0.003), "gun", bevel=0.0015)
    # rubber guards on the eight corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z in (FLOOR + 0.002 + 0.019, TOP - 0.017):
                box(g, (sx * (hx - 0.013), sy * (hy - 0.013), z), (0.017, 0.017, 0.019), "rubber", bevel=0.007)
    # bolts at the window's corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = (sx * (w + fw), sy * (w + fw), t)
            bl.hexnut(g, p, (0, 0, 1), 0.0055, 0.004, "steel")
    # the window: a dark well cut into the lid (its floor is the tray), the glass on top
    box(g, (0, 0, z_tray - 0.002), (w, w, 0.002), "tray")
    for sx in (-1, 1):
        box(g, (sx * w, 0, z_tray + WELL / 2), (0.0015, w, WELL / 2), "hole")
        box(g, (0, sx * w, z_tray + WELL / 2), (w, 0.0015, WELL / 2), "hole")
    box(g, (0, 0, t - 0.002), (w, w, 0.0008), "glass")
    # inside: a cyan gear and a smaller copper one meshing with it, on posts; a couple of loose bolts
    zg = z_tray + 0.004
    gear(g, (-0.012, 0.008), zg, zg + 0.008, 0.04, 0.05, 12, 0.012, "gear")
    cylinder(g, (-0.012, 0.008, z_tray), (-0.012, 0.008, zg + 0.011), 0.011, "steel", segs=16)
    gear(g, (0.042, -0.04), zg, zg + 0.007, 0.019, 0.027, 8, 0.006, "copper", phase=0.2)
    cylinder(g, (0.042, -0.04, z_tray), (0.042, -0.04, zg + 0.01), 0.006, "steel", segs=12)
    for x, y, a in ((-0.05, -0.05, 0.4), (0.048, 0.045, 1.3)):
        rot = Matrix.Rotation(a, 3, "Z") @ Matrix.Rotation(math.pi / 2, 3, "Y")
        d = rot @ Vector((0, 0, 1))
        p = Vector((x, y, z_tray + 0.004))
        cylinder(g, p - d * 0.012, p + d * 0.008, 0.003, "steel", segs=8)
        bl.hexnut(g, p + d * 0.008, d, 0.0055, 0.004, "steel")
    # the front: two steel latches across the band and a small label plate between them
    y = -hy - 0.004
    for x in (-0.06, 0.06):
        box(g, (x, y + 0.001, CASE_Z - 0.012), (0.014, 0.003, 0.01), "gun", bevel=0.002)
        box(g, (x, y - 0.002, CASE_Z + 0.006), (0.011, 0.003, 0.018), "steel", bevel=0.003)
        cylinder(g, (x - 0.012, y - 0.001, CASE_Z - 0.012), (x + 0.012, y - 0.001, CASE_Z - 0.012), 0.003, "steel", segs=10)
    box(g, (0, y + 0.001, CASE_Z - 0.022), (0.03, 0.002, 0.011), "steel", bevel=0.002)
    for x in (-0.024, 0.024):
        dome(g, (x, y - 0.001, CASE_Z - 0.022), (0, -1, 0), 0.003, "gun", segs=8)
    return g


def build_kit(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("ComponentKit", location=offset)}
    bl.make_object("Case", build_case(), mats, rig["root"])
    return rig


ICON_VIEW = (1.25, -1.6, 1.3)


def main():
    bbe.reset_scene()
    mats = make_materials()
    kit = build_kit(mats)
    bbe.export(kit["root"], MESHES / "component_kit.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(kit["root"]), ICONS / "component_kit_icon.png", view=ICON_VIEW, ortho=0.36,
                    target=(0.0, 0.0, -0.03))
    for p in (MESHES / "component_kit.glb", ICONS / "component_kit_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_component_kit
        render_component_kit.render_all(kit, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
