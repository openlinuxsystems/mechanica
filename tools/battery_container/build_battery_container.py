"""Build the Battery Container and its icon (after Chase's Scrap Mechanic reference, 2026-10-20).

The Gas Container's design language (tools/gas_container/build_gas_container.py): a dark gunmetal box with chamfered
corner blocks and riveted edge rails, recessed riveted panels, copper frames and bolts, teal light behind glass.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/battery.glb`` - the Battery Container, 2 x 2 x 2 blocks (0.5 m cube centred on the
  origin, front -Y, Z up). On the front a charge gauge: five teal cells stacked behind glass in a gunmetal frame, a
  label plate with a copper bolt mark and a round copper power socket. On the top a hatch, two copper terminal posts
  (the + one with a red collar) and a red breaker lever. Copper frames on the ends; the +X end has a second power
  socket. Everything stays inside the 2 x 2 x 2 box.

  The cells are their own nodes, ``Cell0`` (bottom) to ``Cell4``, each a lit and a dark piece, so a later charge
  readout can light them.

* ``mods/vanilla/textures/items/battery_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the gauge side.

    pip install bpy==5.2.2 numpy pillow
    python tools/battery_container/build_battery_container.py                  # writes the .glb and the icon
    python tools/battery_container/build_battery_container.py --render OUT_DIR # also renders review images

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, ring, dome, hexnut = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut
rivets = gc.rivets

HALF = 0.25                   # the part's half size: 2 x 2 x 2 blocks
FLOOR = -HALF
BX = BY = 0.235               # the body's half width and depth (the sockets fill the rest to 0.25)
Z_BOT, Z_TOP = -0.25, 0.19    # the body's bottom and top (the terminals and lever fill the rest to 0.25)
ZC, HZ = (Z_BOT + Z_TOP) / 2, (Z_TOP - Z_BOT) / 2
RAIL = 0.02                   # edge rails' half thickness
CORNER = 0.033                # corner blocks' half size
FRONT = -(BY - 0.016)         # the front plate's face
GAUGE_X = 0.085               # the gauge's centre along X
WIN_HX = 0.06                 # the window's half width
WIN_Z0, WIN_Z1 = -0.2, 0.08   # the window's bottom and top
CELLS = 5
CHARGE = 3                    # cells lit in the renders and the icon
SOCKET_R = 0.036


def make_materials():
    m = gc.make_materials()
    m.update({
        "cell": pt.plain("bc_cell", "#0e8577", 0.2, 0.0, emission="#13e0c4", strength=2.2),
        "cell_off": pt.plain("bc_cell_off", "#1f2a2c", 0.5, 0.2),
        "red": lt.textured("bc_red", gc.half_res(lt.paint_surface(811, "#b8322b", "#4a4d52")), normal_strength=0.7),
        "dark": pt.plain("bc_dark", "#202326", 0.5, 0.4),
        # the Gas Container's metals with a softer normal map: at half its size the dents read too deep
        "gun": lt.textured("bc_gun", gc.half_res(lt.metal_surface(401, "#56595e", 0.46, 0.75, wear=0.4, edge_light=0.3)),
                           normal_strength=0.25),
        "gun_dark": lt.textured("bc_gun_dark", gc.half_res(lt.metal_surface(403, "#46494d", 0.52, 0.72, wear=0.7,
                                                                           edge_light=0.25)), normal_strength=0.25),
        "plate": lt.textured("bc_plate", gc.half_res(lt.metal_surface(405, "#4e5155", 0.5, 0.7, wear=0.6, edge_light=0.3)),
                             normal_strength=0.25),
    })
    return m


def build_housing():
    g = Geo()
    box(g, (0, 0, ZC), (BX - 0.016, BY - 0.016, HZ - 0.016), "gun_dark", bevel=0.004)
    ext = (BX - RAIL, BY - RAIL, HZ - RAIL)
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0 in (-1, 1):
            for s1 in (-1, 1):
                c = [0.0, 0.0, ZC]
                c[others[0]] += s0 * ext[others[0]]
                c[others[1]] += s1 * ext[others[1]]
                half = [RAIL, RAIL, RAIL]
                half[axis] = ext[axis] - CORNER + 0.003
                box(g, c, half, "gun", bevel=0.007)
                for o, s in ((others[0], s0), (others[1], s1)):
                    n = [0.0, 0.0, 0.0]
                    n[o] = s
                    a = list(c)
                    a[o] += s * RAIL
                    b = list(a)
                    a[axis] -= half[axis] - 0.022
                    b[axis] += half[axis] - 0.022
                    rivets(g, a, b, max(2, int(round(2 * half[axis] / 0.07)) + 1), n, r=0.0048)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * (BX - CORNER), sy * (BY - CORNER), ZC + sz * (HZ - CORNER))
                box(g, c, (CORNER, CORNER, CORNER), "gun", bevel=0.012)
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                    hexnut(g, Vector(c) + Vector(n) * CORNER, n, 0.0075, 0.004, "bolt")
    build_front(g)
    build_back(g)
    build_top(g)
    for sx in (-1, 1):
        end_frame(g, sx)
    return g


def socket(g, p, n):
    """A round copper power socket: a flange with bolts, a collar and a dark bore with teal pins."""
    p, n = Vector(p), Vector(n)
    u, w = bl.frame_of(n)
    cylinder(g, p, p + n * 0.008, SOCKET_R + 0.014, "copper", segs=32)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        hexnut(g, p + n * 0.008 + (u * math.cos(a) + w * math.sin(a)) * (SOCKET_R + 0.006), n, 0.0055, 0.003, "bolt")
    cylinder(g, p + n * 0.008, p + n * 0.016, SOCKET_R, "copper", segs=32)
    cylinder(g, p + n * 0.012, p + n * 0.0165, SOCKET_R - 0.009, "hole", segs=32)
    for k in range(3):
        a = math.pi / 2 + k * 2 * math.pi / 3
        q = p + (u * math.cos(a) + w * math.sin(a)) * 0.012
        cylinder(g, q + n * 0.012, q + n * 0.0168, 0.0035, "glow_soft", segs=10)


def build_front(g):
    """The front: a recessed riveted panel, the charge gauge's frame, a label plate with a bolt mark, a power socket."""
    y = FRONT
    n = (0, -1, 0)
    px0, px1 = -BX + 0.05, BX - 0.05
    pz0, pz1 = Z_BOT + 0.05, Z_TOP - 0.05
    pcx, pcz, phx, phz = (px0 + px1) / 2, (pz0 + pz1) / 2, (px1 - px0) / 2, (pz1 - pz0) / 2
    box(g, (pcx, y - 0.004, pcz), (phx, 0.004, phz), "gun", bevel=0.004)
    box(g, (pcx, y - 0.006, pcz), (phx - 0.018, 0.004, phz - 0.018), "plate", bevel=0.003)
    yy = y - 0.009
    rivets(g, (px0 + 0.009, yy, pz0 + 0.009), (px1 - 0.009, yy, pz0 + 0.009), 7, n, r=0.0042)
    rivets(g, (px0 + 0.009, yy, pz1 - 0.009), (px1 - 0.009, yy, pz1 - 0.009), 7, n, r=0.0042)
    # the gauge frame as four bars round the window, a bezel, a dark backing, bolts
    fx, fz0, fz1 = WIN_HX + 0.024, WIN_Z0 - 0.022, WIN_Z1 + 0.022
    fy = y - 0.011
    wz, wh = (WIN_Z0 + WIN_Z1) / 2, (WIN_Z1 - WIN_Z0) / 2
    box(g, (GAUGE_X, fy, (fz1 + WIN_Z1 + 0.008) / 2), (fx, 0.007, (fz1 - WIN_Z1 - 0.008) / 2), "gun", bevel=0.005)
    box(g, (GAUGE_X, fy, (fz0 + WIN_Z0 - 0.008) / 2), (fx, 0.007, (WIN_Z0 - 0.008 - fz0) / 2), "gun", bevel=0.005)
    for s in (-1, 1):
        bw = (fx - WIN_HX - 0.008) / 2
        box(g, (GAUGE_X + s * (WIN_HX + 0.008 + bw), fy, wz), (bw, 0.007, wh + 0.008), "gun", bevel=0.004)
        box(g, (GAUGE_X + s * (WIN_HX + 0.004), y - 0.016, wz), (0.004, 0.004, wh + 0.008), "gun_dark", bevel=0.0015)
        box(g, (GAUGE_X, y - 0.016, wz + s * (wh + 0.004)), (WIN_HX + 0.008, 0.004, 0.004), "gun_dark", bevel=0.0015)
    box(g, (GAUGE_X, y - 0.0105, wz), (WIN_HX, 0.0006, wh), "backing")
    for dx in (-fx + 0.012, fx - 0.012):
        for dz in (fz0 + 0.011, fz1 - 0.011):
            dome(g, (GAUGE_X + dx, y - 0.018, dz), n, 0.0045, "bolt")
    # dividers between the cells, on the backing
    step = (WIN_Z1 - WIN_Z0) / CELLS
    for k in range(1, CELLS):
        box(g, (GAUGE_X, y - 0.0125, WIN_Z0 + k * step), (WIN_HX, 0.0014, 0.0025), "backing")
    # the label plate with a copper bolt mark, top left; the power socket under it
    lx, lz = -0.105, 0.045
    box(g, (lx, y - 0.012, lz), (0.06, 0.004, 0.04), "gun", bevel=0.003)
    box(g, (lx, y - 0.0162, lz), (0.051, 0.0008, 0.031), "gun_dark")
    for dx in (-0.05, 0.05):
        for dz in (-0.03, 0.03):
            dome(g, (lx + dx, y - 0.016, lz + dz), n, 0.0035, "bolt")
    bolt = [(0.006, 0.024), (-0.014, -0.002), (-0.001, -0.002), (-0.007, -0.024), (0.014, 0.004), (0.001, 0.004)]
    verts = [(lx + x, y - 0.0172, lz + z) for x, z in bolt] + [(lx + x, y - 0.0185, lz + z) for x, z in bolt]
    m = len(bolt)
    faces = [tuple(range(m, 2 * m)), tuple(reversed(range(m)))]
    faces += [(i, (i + 1) % m, m + (i + 1) % m, m + i) for i in range(m)]
    g.add(verts, faces, "copper")
    socket(g, (-0.105, y - 0.008, -0.11), n)


def build_back(g):
    y = BY - 0.016
    n = (0, 1, 0)
    px, pz = BX - 0.05, (Z_TOP - Z_BOT) / 2 - 0.05
    box(g, (0, y + 0.004, ZC), (px, 0.004, pz), "gun", bevel=0.004)
    box(g, (0, y + 0.006, ZC), (px - 0.018, 0.004, pz - 0.018), "plate", bevel=0.003)
    rivets(g, (-px + 0.009, y + 0.009, ZC - pz + 0.009), (px - 0.009, y + 0.009, ZC - pz + 0.009), 7, n, r=0.0042)
    rivets(g, (-px + 0.009, y + 0.009, ZC + pz - 0.009), (px - 0.009, y + 0.009, ZC + pz - 0.009), 7, n, r=0.0042)
    box(g, (0, y + 0.0105, ZC + 0.05), (px - 0.03, 0.0008, 0.0018), "hole")


def end_frame(g, sx):
    """A copper frame round the end face over a gunmetal inset; the +X end carries a second power socket."""
    x = sx * (BX - 0.003)
    fy, fz = BY - 0.04, HZ - 0.04
    t = 0.016
    for s in (-1, 1):
        box(g, (x + sx * 0.006, s * (fy - t), ZC), (0.006, t, fz), "copper", bevel=0.004)
        box(g, (x + sx * 0.006, 0, ZC + s * (fz - t)), (0.006, fy, t), "copper", bevel=0.004)
    box(g, (x + sx * 0.002, 0, ZC), (0.003, fy - 2 * t, fz - 2 * t), "plate", bevel=0.002)
    for s in (-1, 1):
        for k in range(4):
            zz = ZC - fz + 0.03 + k * (2 * fz - 0.06) / 3
            dome(g, (x + sx * 0.012, s * (fy - t), zz), (sx, 0, 0), 0.0042, "bolt")
    if sx > 0:
        socket(g, (x + 0.002, 0, ZC - 0.02), (1, 0, 0))
    else:
        box(g, (x - 0.008, 0, ZC + 0.03), (0.003, 0.07, 0.035), "gun", bevel=0.003)
        for k in range(3):
            box(g, (x - 0.0115, 0, ZC + 0.012 + k * 0.018), (0.0008, 0.055, 0.0025), "hole")


def build_top(g):
    """The top: plates and a hatch, two copper terminals (+ with a red collar) and a red breaker lever."""
    z = Z_TOP - 0.016
    box(g, (0, 0, z + 0.005), (BX - 0.055, BY - 0.055, 0.005), "plate", bevel=0.003)
    rivets(g, (-BX + 0.07, -BY + 0.07, z + 0.01), (BX - 0.07, -BY + 0.07, z + 0.01), 7, (0, 0, 1), r=0.0042)
    rivets(g, (-BX + 0.07, BY - 0.07, z + 0.01), (BX - 0.07, BY - 0.07, z + 0.01), 7, (0, 0, 1), r=0.0042)
    # the hatch toward the back
    box(g, (0, 0.06, z + 0.012), (0.1, 0.07, 0.005), "gun", bevel=0.004)
    box(g, (0, 0.06, z + 0.0165), (0.084, 0.054, 0.004), "gun_dark", bevel=0.0025)
    box(g, (0, 0.022, z + 0.0207), (0.03, 0.006, 0.0006), "hole")
    # terminals toward the front: an insulator, a copper post with a nut, a collar (red for +)
    for x, collar in ((0.1, "red"), (-0.1, "dark")):
        p = (x, -0.08, z + 0.01)
        cylinder(g, p, (x, -0.08, z + 0.022), 0.03, "dark", segs=28)
        cylinder(g, (x, -0.08, z + 0.022), (x, -0.08, z + 0.03), 0.025, collar, segs=28)
        cylinder(g, (x, -0.08, z + 0.03), (x, -0.08, Z_TOP + 0.05), 0.012, "copper", segs=20)
        hexnut(g, (x, -0.08, z + 0.044), (0, 0, 1), 0.019, 0.01, "copper")
    # + and - marks beside them on the plate
    zz = z + 0.0108
    box(g, (0.1, -0.135, zz), (0.009, 0.0022, 0.0006), "red")
    box(g, (0.1, -0.135, zz), (0.0022, 0.009, 0.0006), "red")
    box(g, (-0.1, -0.135, zz), (0.009, 0.0022, 0.0006), "dark")
    # the breaker: a copper hinge block and a red lever folded back, its tip a grip
    hx, hy = -0.13, 0.08
    box(g, (hx, hy, z + 0.022), (0.026, 0.02, 0.012), "copper", bevel=0.004)
    cylinder(g, (hx - 0.03, hy, z + 0.03), (hx + 0.03, hy, z + 0.03), 0.008, "steel", segs=14)
    lever = Matrix.Rotation(math.radians(-62), 3, "X")
    d = lever @ Vector((0, 0, 1))
    base = Vector((hx, hy, z + 0.03))
    box(g, tuple(base + d * 0.035), (0.01, 0.007, 0.035), "red", bevel=0.003, rot=lever)
    box(g, tuple(base + d * 0.072), (0.016, 0.011, 0.012), "red", bevel=0.005, rot=lever)


def build_glass():
    g = Geo()
    wz, wh = (WIN_Z0 + WIN_Z1) / 2, (WIN_Z1 - WIN_Z0) / 2
    box(g, (GAUGE_X, FRONT - 0.0165, wz), (WIN_HX + 0.003, 0.0008, wh + 0.003), "glass")
    return g


def cell(lit):
    g = Geo()
    step = (WIN_Z1 - WIN_Z0) / CELLS
    box(g, (0, 0, 0), (WIN_HX - 0.006, 0.0009, step / 2 - 0.006), "cell" if lit else "cell_off", bevel=0.0006)
    return g


def build_battery(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("Battery", location=offset)}
    root = rig["root"]
    bl.make_object("Housing", build_housing(), mats, root)
    bl.make_object("Glass", build_glass(), mats, root)
    step = (WIN_Z1 - WIN_Z0) / CELLS
    rig["cells"] = []
    for k in range(CELLS):
        p = (GAUGE_X, FRONT - 0.012, WIN_Z0 + (k + 0.5) * step)
        node = bl.make_empty(f"Cell{k}", root, p)
        bl.make_object(f"Cell{k}Off", cell(False), mats, node)
        on = bl.make_object(f"Cell{k}On", cell(True), mats, node, (0, -0.0011, 0))
        on.hide_render = on.hide_viewport = k >= CHARGE
        rig["cells"].append(on)
    return rig


ICON_VIEW = (1.3, -1.9, 1.15)


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_battery(mats)
    bbe.export(rig["root"], MESHES / "battery.glb", animations=False)
    bbe.render_icon([o for o in bbe.mesh_tree(rig["root"]) if not o.hide_render], ICONS / "battery_icon.png",
                    view=ICON_VIEW, ortho=0.72, target=(0.0, 0.0, -0.02))
    for p in (MESHES / "battery.glb", ICONS / "battery_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_battery_container
        render_battery_container.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
