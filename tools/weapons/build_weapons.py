"""Build the potato weapons and the Popper as items, with their icons (Creative inventory fix, 2026-10-20).

The three potato weapons share one family look with the Potato Blaster the player already holds (its rig prop in
``tools/characters/build_characters.py``, ``prop_potato_blaster``): a brightly painted pressure tank (blue, red or yellow), dark metal end caps
and bands, a steel barrel and an orange muzzle ring, a rubber grip. Each variant changes the barrel end so they read
apart at icon size:

* ``potato_blaster`` - the rig prop as an item: one long steel barrel.
* ``potato_scattergun`` - a short tank and three stubby barrels in a triangle, tied by a dark muzzle plate, with a
  pump fore-grip under them and a potato hopper on top.
* ``potato_launcher`` - a fat shoulder tube over a long tank, a second grip forward, a shoulder pad at the back and a
  flared orange muzzle.
* ``popper`` - a hand-thrown corn cob: rows of yellow kernels on a cob, a dark metal cap with a pull ring and an orange
  fuse band round its waist.

Each is written to ``mods/vanilla/meshes/items/<id>.glb`` (front +X, up +Z in Blender; +Y up in glTF) with its icon in
``mods/vanilla/textures/items/<id>_icon.png``. The meshes are what the inventory, the hotbar and a dropped item show.

    pip install bpy==5.2.2 numpy pillow
    python tools/weapons/build_weapons.py                  # writes the .glb files and the icons
    python tools/weapons/build_weapons.py --render OUT_DIR # also renders review images into OUT_DIR

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
# Each weapon's body paint: bright, so the three read apart at a glance (Chase, 2026-10-20: "more colorful").
PAINT = {"potato_blaster": "#2b8fe0", "potato_scattergun": "#e0412f", "potato_launcher": "#f2b51d"}
X = (1, 0, 0)
UP = (0, 0, 1)
PAINT_KEY = ["paint_potato_blaster"]   # the body paint of the weapon being built


def make_materials():
    return {
        **{f"paint_{k}": lt.textured(f"wp_paint_{k}", gc.half_res(lt.paint_surface(701 + i, c, "#4a4d52")), normal_strength=0.8)
           for i, (k, c) in enumerate(PAINT.items())},
        "dark": lt.textured("wp_dark", gc.half_res(lt.metal_surface(703, "#33363b", 0.5, 0.8, wear=0.6, edge_light=0.3)),
                            normal_strength=0.7),
        "steel": lt.textured("wp_steel", gc.half_res(lt.metal_surface(705, "#b4b8be", 0.3, 0.9, wear=0.5, edge_light=0.4)),
                             normal_strength=0.6),
        "orange": lt.textured("wp_orange", gc.half_res(lt.paint_surface(707, "#d4661a", "#4a4d52")), normal_strength=0.8),
        "rubber": lt.textured("wp_rubber", gc.half_res(lt.rubber_surface(709)), normal_strength=0.5),
        "bore": pt.plain("wp_bore", "#0c0d0f", 0.9, 0.2),
        "kernel": pt.plain("wp_kernel", "#f2c230", 0.45, 0.0),
        "cob": pt.plain("wp_cob", "#c9a25a", 0.7, 0.0),
        "husk": pt.plain("wp_husk", "#7e9a4a", 0.6, 0.0),
    }


def band(g, x, r, width, mat):
    """A dark band round the X axis at x (a strap on the tank)."""
    return cylinder(g, (x - width / 2, 0, 0), (x + width / 2, 0, 0), r, mat, segs=24)


def tank(g, x0, x1, r, bands):
    """The painted pressure tank along X with dark end caps and bands."""
    cylinder(g, (x0 + 0.02, 0, 0), (x1, 0, 0), r, PAINT_KEY[0], segs=24)
    cylinder(g, (x0, 0, 0), (x0 + 0.02, 0, 0), r + 0.003, "dark", segs=24)
    dome(g, (x0, 0, 0), (-1, 0, 0), r * 0.7, "dark", segs=16, height=0.012)
    for x in bands:
        band(g, x, r + 0.003, 0.006, "dark")


def grip(g, x, z, lean=0.12, length=0.11):
    """A rubber pistol grip hanging from (x, z), leaning back, with an orange trigger in front of it."""
    top = Vector((x, 0, z))
    bottom = top + Vector((-lean * length, 0, -length))
    cylinder(g, bottom, top, 0.019, "rubber", segs=12)
    dome(g, bottom, (bottom - top).normalized(), 0.019, "rubber", segs=12, height=0.008)
    cylinder(g, (x + 0.028, 0, z - 0.012), (x + 0.034, 0, z - 0.045), 0.007, "orange", segs=10)
    box(g, (x + 0.026, 0, z - 0.05), (0.024, 0.006, 0.003), "dark")


def muzzle(g, x, r, z=0.0, y=0.0, flare=None):
    """An orange muzzle ring at x with a dark bore."""
    r1 = flare or r
    cylinder(g, (x - 0.03, y, z), (x, y, z), r, "orange", segs=20, r1=r1)
    cylinder(g, (x - 0.005, y, z), (x + 0.0005, y, z), r1 * 0.62, "bore", segs=20, cap0=False)


def build_blaster():
    """The Potato Blaster: the rig prop's shapes (tank, collar, barrel, muzzle, sights), in the item's frame."""
    g = Geo()
    tank(g, -0.19, 0.06, 0.047, (-0.12, -0.02))
    cylinder(g, (0.06, 0, 0), (0.08, 0, 0), 0.04, "dark", segs=20)
    cylinder(g, (0.08, 0, 0), (0.44, 0, 0), 0.026, "steel", segs=18)
    muzzle(g, 0.47, 0.032)
    box(g, (-0.03, 0, 0.051), (0.03, 0.008, 0.006), "dark", bevel=0.002)
    box(g, (0.3, 0, 0.032), (0.004, 0.003, 0.008), "orange")
    grip(g, -0.006, -0.04)
    return g, (0.14, 0.0, -0.035)


def build_scattergun():
    """The Potato Scattergun: a short tank feeding three stubby barrels, a pump fore-grip and a hopper on top."""
    g = Geo()
    tank(g, -0.16, 0.02, 0.052, (-0.1,))
    cylinder(g, (0.02, 0, 0), (0.05, 0, 0), 0.056, "dark", segs=24, r1=0.05)
    for k in range(3):
        a = math.pi / 2 + 2 * math.pi * k / 3
        y, z = 0.026 * math.cos(a), 0.026 * math.sin(a)
        cylinder(g, (0.05, y, z), (0.2, y, z), 0.019, "steel", segs=16)
        muzzle(g, 0.23, 0.022, z=z, y=y, flare=0.025)
    cylinder(g, (0.13, 0, 0), (0.145, 0, 0), 0.054, "dark", segs=24)
    # pump fore-grip under the barrels
    box(g, (0.11, 0, -0.054), (0.05, 0.02, 0.013), "rubber", bevel=0.006)
    for x in (0.08, 0.1, 0.12, 0.14):
        box(g, (x, 0, -0.068), (0.004, 0.021, 0.002), "dark")
    # potato hopper on top: a short painted drum with a dark lid
    cylinder(g, (-0.07, 0, 0.04), (-0.07, 0, 0.095), 0.034, PAINT_KEY[0], segs=20)
    cylinder(g, (-0.07, 0, 0.095), (-0.07, 0, 0.104), 0.037, "dark", segs=20)
    dome(g, (-0.07, 0, 0.104), UP, 0.012, "orange", segs=10, height=0.006)
    grip(g, -0.03, -0.045)
    return g, (0.035, 0.0, -0.01)


def build_launcher():
    """The Potato Launcher: a fat shoulder tube over a long tank, two grips, a shoulder pad and a flared muzzle."""
    g = Geo()
    zt = 0.07                                       # the tube's axis, above the tank
    tank(g, -0.24, 0.12, 0.045, (-0.17, -0.02, 0.07))
    # the tube, its back end open on a dark ring, its front flared orange
    cylinder(g, (-0.3, 0, zt), (0.42, 0, zt), 0.052, PAINT_KEY[0], segs=28)
    cylinder(g, (-0.32, 0, zt), (-0.3, 0, zt), 0.058, "dark", segs=28)
    cylinder(g, (-0.3205, 0, zt), (-0.3195, 0, zt), 0.04, "bore", segs=20, cap1=False)
    for x in (-0.18, 0.05, 0.3):
        band(g, x, 0.055, 0.012, "dark")
    muzzle(g, 0.47, 0.056, z=zt, flare=0.072)
    cylinder(g, (0.42, 0, zt), (0.44, 0, zt), 0.054, "dark", segs=28)
    # the tank's feed into the tube
    cylinder(g, (0.12, 0, 0), (0.12, 0, zt), 0.018, "dark", segs=14)
    # shoulder pad
    box(g, (-0.27, 0, zt - 0.055), (0.035, 0.03, 0.018), "rubber", bevel=0.01)
    # sight rail and a small scope
    box(g, (-0.02, 0, zt + 0.058), (0.08, 0.008, 0.005), "dark", bevel=0.002)
    cylinder(g, (-0.07, 0, zt + 0.078), (0.03, 0, zt + 0.078), 0.014, "dark", segs=14)
    cylinder(g, (0.03, 0, zt + 0.078), (0.036, 0, zt + 0.078), 0.016, "orange", segs=14)
    cylinder(g, (0.0355, 0, zt + 0.078), (0.0365, 0, zt + 0.078), 0.011, "bore", segs=14, cap0=False)
    for x in (-0.05, 0.01):
        box(g, (x, 0, zt + 0.066), (0.006, 0.006, 0.008), "dark")
    grip(g, -0.06, -0.04)
    grip(g, 0.24, zt - 0.05, lean=0.0, length=0.1)
    return g, (0.07, 0.0, 0.0)


def build_popper():
    """The Popper: a corn cob of yellow kernels, a dark cap with a pull ring on top and an orange fuse band."""
    g = Geo()
    h0, h1 = -0.06, 0.05                            # the cob's ends (Z)
    bbe.lathe(g, [(0.0, h0 - 0.008), (0.022, h0 - 0.004), (0.03, h0 + 0.02), (0.031, h1 - 0.03), (0.024, h1),
                  (0.0, h1 + 0.004)], "cob", steps=24)
    rows, per_row = 12, 10
    for i in range(per_row):
        z = h0 + 0.006 + (h1 - h0 - 0.02) * i / (per_row - 1)
        r = 0.031 if h0 + 0.02 <= z <= h1 - 0.03 else 0.026
        for k in range(rows):
            a = 2 * math.pi * (k + 0.5 * (i % 2)) / rows
            n = Vector((math.cos(a), math.sin(a), 0))
            dome(g, Vector((0, 0, z)) + n * (r - 0.001), n, 0.0088, "kernel", segs=8, height=0.006)
    # fuse band round the waist and the husk leaves at the bottom
    cylinder(g, (0, 0, -0.012), (0, 0, 0.004), 0.0375, "orange", segs=28)
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        n = Vector((math.cos(a), math.sin(a), 0))
        rot = Matrix.Rotation(a, 3, "Z") @ Matrix.Rotation(-0.35, 3, "Y")
        box(g, Vector((0, 0, h0 + 0.004)) + n * 0.026, (0.004, 0.012, 0.024), "husk", bevel=0.003, rot=rot)
    # cap, lever and pull ring
    cylinder(g, (0, 0, h1 - 0.006), (0, 0, h1 + 0.014), 0.024, "dark", segs=24)
    dome(g, (0, 0, h1 + 0.014), UP, 0.024, "dark", segs=20, height=0.008)
    box(g, (0.03, 0, h1 - 0.012), (0.004, 0.007, 0.028), "steel", bevel=0.002, rot=Matrix.Rotation(0.12, 3, "Y"))
    ring = Geo()
    steps = 20
    for k in range(steps):
        a0, a1 = 2 * math.pi * k / steps, 2 * math.pi * (k + 1) / steps
        p0 = Vector((-0.018 + 0.014 * math.cos(a0), 0, h1 + 0.03 + 0.014 * math.sin(a0)))
        p1 = Vector((-0.018 + 0.014 * math.cos(a1), 0, h1 + 0.03 + 0.014 * math.sin(a1)))
        cylinder(ring, p0, p1, 0.0022, "steel", segs=8)
    g.extend(ring)
    cylinder(g, (-0.008, 0, h1 + 0.02), (0, 0, h1 + 0.016), 0.0022, "steel", segs=8)
    return g, (0.0, 0.0, -0.005)


ITEMS = {
    "potato_blaster": (build_blaster, (0.55, -1.6, 0.7), 0.74),
    "potato_scattergun": (build_scattergun, (0.55, -1.6, 0.7), 0.46),
    "potato_launcher": (build_launcher, (0.55, -1.6, 0.7), 0.92),
    "popper": (build_popper, (1.0, -1.6, 0.9), 0.2),
}


def build_item(item_id, mats):
    build, _, _ = ITEMS[item_id]
    PAINT_KEY[0] = f"paint_{item_id}"
    g, centre = build()
    root = bl.make_empty(item_id)
    bl.make_object(item_id.title().replace("_", ""), g, mats, root, location=tuple(-c for c in centre))
    return root


def main():
    out = Path(sys.argv[sys.argv.index("--render") + 1]) if "--render" in sys.argv else None
    roots = {}
    for item_id, (_, view, ortho) in ITEMS.items():
        bbe.reset_scene()
        mats = make_materials()
        root = build_item(item_id, mats)
        bbe.export(root, MESHES / f"{item_id}.glb", animations=False)
        bbe.render_icon(bbe.mesh_tree(root), ICONS / f"{item_id}_icon.png", view=view, ortho=ortho)
        for p in (MESHES / f"{item_id}.glb", ICONS / f"{item_id}_icon.png"):
            print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
        roots[item_id] = root
    if out is not None:
        import render_weapons
        render_weapons.render_all(out, sys.modules[__name__])


if __name__ == "__main__":
    main()
