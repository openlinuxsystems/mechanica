"""Build the Battery Container and its icon (after Chase's Scrap Mechanic reference, 2026-10-20).

The Gas Container's design language (tools/gas_container/build_gas_container.py): a dark gunmetal box with chamfered
corner blocks and riveted edge rails, recessed riveted panels, copper frames and bolts, teal light behind glass.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/battery.glb`` - the Battery Container, 4 x 3 x 3 blocks like the Gas Container
  (1.0 x 0.75 x 0.75 m centred on the origin, long along X, Z up), but told apart from it at a glance: a mustard
  painted lid instead of the plates and carrying handle, two big copper terminals (the + one with a red collar) and a
  red breaker on it; on each long side a wide charge gauge of five teal cells side by side (not a fuel column) next to
  a big copper lightning-bolt plate, and a row of vent slots; on the ends gunmetal cooling fins instead of copper
  frames and pipe sockets, the +X end with one round power socket. Everything stays inside the 4 x 3 x 3 box.

  The cells are their own nodes, ``CellL0`` / ``CellR0`` (toward -X) to ``CellL4`` / ``CellR4``, each a dark piece
  with a lit one in front. One glTF clip, ``charge`` (1 s, scrubbed by the ``charge`` readout of
  ``scripts/battery.lua``), lights them: cell k comes on at k/5 s, so a sliver of charge shows one cell and full shows
  five.

* ``mods/vanilla/textures/items/battery_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the -Y gauge side with the socket end on the right.

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

HALF_X, HALF_Y, HALF_Z = 0.5, 0.375, 0.375  # the part's half sizes: 4 x 3 x 3 blocks
FLOOR = -HALF_Z
BX, BY = 0.465, 0.35          # the body's half length and depth (the fins and socket fill the rest)
Z_BOT, Z_TOP = -0.375, 0.285  # the body's bottom and top (the terminals and breaker fill the rest to 0.375)
ZC, HZ = (Z_BOT + Z_TOP) / 2, (Z_TOP - Z_BOT) / 2
RAIL = 0.028                  # edge rails' half thickness (as the Gas Container)
CORNER = 0.046                # corner blocks' half size
SIDE = BY - 0.022             # the long sides' plate faces
GAUGE_X0, GAUGE_X1 = -0.07, 0.36  # the charge window's ends along X
GAUGE_Z, GAUGE_HZ = -0.03, 0.085  # its centre and half height
CELLS = 5
CHARGE = 3                    # cells lit in the renders and the icon
SOCKET_R = 0.056
TOP = Z_TOP


def make_materials():
    m = gc.make_materials()
    m.update({
        "cell": pt.plain("bc_cell", "#0e8577", 0.2, 0.0, emission="#13e0c4", strength=2.2),
        "cell_off": pt.plain("bc_cell_off", "#1f2a2c", 0.5, 0.2),
        "mustard": lt.textured("bc_mustard", gc.half_res(lt.paint_surface(813, "#d29a22", "#4a4d52")), normal_strength=0.5),
        "red": lt.textured("bc_red", gc.half_res(lt.paint_surface(811, "#b8322b", "#4a4d52")), normal_strength=0.5),
        "dark": pt.plain("bc_dark", "#202326", 0.5, 0.4),
    })
    return m


def build_housing():
    g = Geo()
    box(g, (0, 0, ZC), (BX - 0.022, BY - 0.022, HZ - 0.022), "gun_dark", bevel=0.004)
    ext = (BX - RAIL, BY - RAIL, HZ - RAIL)
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for s0 in (-1, 1):
            for s1 in (-1, 1):
                c = [0.0, 0.0, ZC]
                c[others[0]] += s0 * ext[others[0]]
                c[others[1]] += s1 * ext[others[1]]
                half = [RAIL, RAIL, RAIL]
                half[axis] = ext[axis] - CORNER + 0.004
                box(g, c, half, "gun", bevel=0.009)
                for o, s in ((others[0], s0), (others[1], s1)):
                    n = [0.0, 0.0, 0.0]
                    n[o] = s
                    a = list(c)
                    a[o] += s * RAIL
                    b = list(a)
                    a[axis] -= half[axis] - 0.03
                    b[axis] += half[axis] - 0.03
                    rivets(g, a, b, max(2, int(round(2 * half[axis] / 0.09)) + 1), n, r=0.0058)
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * (BX - CORNER), sy * (BY - CORNER), ZC + sz * (HZ - CORNER))
                box(g, c, (CORNER, CORNER, CORNER), "gun", bevel=0.016)
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                    hexnut(g, Vector(c) + Vector(n) * CORNER, n, 0.0095, 0.005, "bolt")
    for sy in (-1, 1):
        long_side(g, sy)
    for sx in (-1, 1):
        end_fins(g, sx)
    build_top(g)
    return g


def socket(g, p, n):
    """A round copper power socket: a flange with bolts, a collar and a dark bore with three teal pins."""
    p, n = Vector(p), Vector(n)
    u, w = bl.frame_of(n)
    cylinder(g, p, p + n * 0.012, SOCKET_R + 0.022, "copper", segs=36)
    for k in range(6):
        a = math.pi / 6 + k * math.pi / 3
        hexnut(g, p + n * 0.012 + (u * math.cos(a) + w * math.sin(a)) * (SOCKET_R + 0.01), n, 0.0065, 0.004, "bolt")
    cylinder(g, p + n * 0.012, p + n * 0.026, SOCKET_R, "copper", segs=36)
    cylinder(g, p + n * 0.02, p + n * 0.0265, SOCKET_R - 0.013, "hole", segs=36)
    for k in range(3):
        a = math.pi / 2 + k * 2 * math.pi / 3
        q = p + (u * math.cos(a) + w * math.sin(a)) * 0.02
        cylinder(g, q + n * 0.02, q + n * 0.0275, 0.0055, "glow_soft", segs=12)


def bolt_mark(g, c, y, sy, size, mat):
    """A lightning bolt standing proud of a plate at y, facing sy."""
    pts = [(0.25, 1.0), (-0.55, -0.08), (-0.05, -0.08), (-0.3, -1.0), (0.55, 0.16), (0.05, 0.16)]
    m = len(pts)
    verts = [(c[0] + x * size, y, c[1] + z * size) for x, z in pts] + \
            [(c[0] + x * size, y + sy * 0.004, c[1] + z * size) for x, z in pts]
    outer, inner = tuple(range(m, 2 * m)), tuple(reversed(range(m)))
    if sy > 0:
        outer, inner = tuple(reversed(outer)), tuple(reversed(inner))
    faces = [outer, inner] + [(i, (i + 1) % m, m + (i + 1) % m, m + i)[::-sy] for i in range(m)]
    g.add(verts, faces, mat)


def long_side(g, sy):
    """A long side: a riveted panel, a big lightning-bolt plate, the wide charge gauge, a row of vent slots."""
    y = sy * SIDE
    n = (0, sy, 0)
    px0, px1 = -BX + 0.07, BX - 0.07
    pz0, pz1 = Z_BOT + 0.065, Z_TOP - 0.065
    pcx, pcz, phx, phz = (px0 + px1) / 2, (pz0 + pz1) / 2, (px1 - px0) / 2, (pz1 - pz0) / 2
    box(g, (pcx, y + sy * 0.006, pcz), (phx, 0.006, phz), "gun", bevel=0.005)
    box(g, (pcx, y + sy * 0.008, pcz), (phx - 0.024, 0.006, phz - 0.024), "plate", bevel=0.004)
    yy = y + sy * 0.012
    rivets(g, (px0 + 0.012, yy, pz0 + 0.012), (px1 - 0.012, yy, pz0 + 0.012), 11, n, r=0.0052)
    rivets(g, (px0 + 0.012, yy, pz1 - 0.012), (px1 - 0.012, yy, pz1 - 0.012), 11, n, r=0.0052)
    # the lightning-bolt plate toward -X: a gunmetal plate, a dark field, a copper bolt
    lx, lz = -0.25, GAUGE_Z + 0.005
    box(g, (lx, y + sy * 0.017, lz), (0.085, 0.004, 0.1), "gun", bevel=0.004)
    box(g, (lx, y + sy * 0.0212, lz), (0.072, 0.0008, 0.087), "dark")
    bolt_mark(g, (lx, lz), y + sy * 0.022, sy, 0.068, "copper")
    for dx in (-0.073, 0.073):
        for dz in (-0.088, 0.088):
            dome(g, (lx + dx, y + sy * 0.021, lz + dz), n, 0.0045, "bolt")
    # the charge gauge: a frame of four bars, a bezel, a backing with dividers between the five cells
    gx, ghx = (GAUGE_X0 + GAUGE_X1) / 2, (GAUGE_X1 - GAUGE_X0) / 2
    f = 0.03
    fy = y + sy * 0.017
    for s in (-1, 1):
        box(g, (gx, fy, GAUGE_Z + s * (GAUGE_HZ + 0.008 + f / 2)), (ghx + 0.008 + f, 0.007, f / 2), "gun", bevel=0.006)
        box(g, (gx + s * (ghx + 0.008 + f / 2), fy, GAUGE_Z), (f / 2, 0.007, GAUGE_HZ + 0.008), "gun", bevel=0.006)
        box(g, (gx + s * (ghx + 0.004), y + sy * 0.023, GAUGE_Z), (0.004, 0.004, GAUGE_HZ + 0.008), "gun_dark", bevel=0.0015)
        box(g, (gx, y + sy * 0.023, GAUGE_Z + s * (GAUGE_HZ + 0.004)), (ghx + 0.008, 0.004, 0.004), "gun_dark", bevel=0.0015)
    box(g, (gx, y + sy * 0.0145, GAUGE_Z), (ghx, 0.0006, GAUGE_HZ), "backing")
    step = 2 * ghx / CELLS
    for k in range(1, CELLS):
        box(g, (GAUGE_X0 + k * step, y + sy * 0.0165, GAUGE_Z), (0.003, 0.0016, GAUGE_HZ), "backing")
    for dx in (-ghx - 0.023, ghx + 0.023):
        for dz in (-GAUGE_HZ - 0.023, GAUGE_HZ + 0.023):
            dome(g, (gx + dx, y + sy * 0.024, GAUGE_Z + dz), n, 0.0055, "bolt")
    # vent slots along the bottom
    for k in range(14):
        x = -0.35 + k * 0.7 / 13
        box(g, (x, y + sy * 0.0145, Z_BOT + 0.115), (0.012, 0.0008, 0.022), "hole")


def end_fins(g, sx):
    """An end: gunmetal cooling fins across the face; the +X end has the power socket in a gap between them."""
    x = sx * (BX - 0.004)
    box(g, (x + sx * 0.002, 0, ZC), (0.004, BY - 0.05, HZ - 0.05), "plate", bevel=0.003)
    zs = [ZC - HZ + 0.075 + k * (2 * HZ - 0.15) / 10 for k in range(11)]
    for z in zs:
        if sx > 0 and abs(z - ZC) < SOCKET_R + 0.04:
            continue
        box(g, (x + sx * 0.016, 0, z), (0.014, BY - 0.065, 0.006), "gun", bevel=0.003)
    for s in (-1, 1):
        box(g, (x + sx * 0.018, s * (BY - 0.07), ZC), (0.016, 0.01, HZ - 0.06), "gun", bevel=0.004)
        for z in (ZC - HZ + 0.08, ZC + HZ - 0.08):
            hexnut(g, (x + sx * 0.034, s * (BY - 0.07), z), (sx, 0, 0), 0.007, 0.004, "bolt")
    if sx > 0:
        socket(g, (x + 0.004, 0, ZC), (1, 0, 0))


def build_top(g):
    """The top: a mustard lid with a hatch, two copper terminals (+ with a red collar) and a red breaker lever."""
    z = Z_TOP - 0.022
    box(g, (0, 0, z + 0.007), (BX - 0.07, BY - 0.07, 0.007), "mustard", bevel=0.006)
    rivets(g, (-BX + 0.085, -BY + 0.085, z + 0.014), (BX - 0.085, -BY + 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    rivets(g, (-BX + 0.085, BY - 0.085, z + 0.014), (BX - 0.085, BY - 0.085, z + 0.014), 11, (0, 0, 1), r=0.005)
    zt = z + 0.014
    # a gunmetal hatch with a slot in the middle of the lid
    box(g, (0, 0.05, zt + 0.006), (0.12, 0.1, 0.006), "gun", bevel=0.005)
    box(g, (0, 0.05, zt + 0.011), (0.102, 0.082, 0.005), "gun_dark", bevel=0.003)
    box(g, (0, -0.01, zt + 0.0162), (0.045, 0.008, 0.0006), "hole")
    # terminals toward the ends: an insulator, a collar (red for +), a copper post with a big nut
    for x, collar in ((0.3, "red"), (-0.3, "dark")):
        cylinder(g, (x, -0.06, zt), (x, -0.06, zt + 0.018), 0.05, "dark", segs=32)
        cylinder(g, (x, -0.06, zt + 0.018), (x, -0.06, zt + 0.03), 0.042, collar, segs=32)
        cylinder(g, (x, -0.06, zt + 0.03), (x, -0.06, 0.37), 0.02, "copper", segs=24)
        hexnut(g, (x, -0.06, zt + 0.05), (0, 0, 1), 0.032, 0.016, "copper")
    # + and - marks, painted on the lid
    zz = zt + 0.0006
    box(g, (0.3, -0.155, zz), (0.016, 0.004, 0.0008), "red")
    box(g, (0.3, -0.155, zz), (0.004, 0.016, 0.0008), "red")
    box(g, (-0.3, -0.155, zz), (0.016, 0.004, 0.0008), "dark")
    # the breaker: a copper hinge block and a red lever folded back, its tip a grip
    hx, hy = -0.29, 0.13
    box(g, (hx, hy, zt + 0.016), (0.04, 0.03, 0.016), "copper", bevel=0.005)
    cylinder(g, (hx - 0.045, hy, zt + 0.028), (hx + 0.045, hy, zt + 0.028), 0.012, "steel", segs=16)
    lever = Matrix.Rotation(math.radians(-62), 3, "X")
    d = lever @ Vector((0, 0, 1))
    base = Vector((hx, hy, zt + 0.028))
    box(g, tuple(base + d * 0.05), (0.015, 0.01, 0.05), "red", bevel=0.004, rot=lever)
    box(g, tuple(base + d * 0.105), (0.024, 0.016, 0.018), "red", bevel=0.007, rot=lever)


def build_glass():
    g = Geo()
    gx, ghx = (GAUGE_X0 + GAUGE_X1) / 2, (GAUGE_X1 - GAUGE_X0) / 2
    for sy in (-1, 1):
        box(g, (gx, sy * (SIDE + 0.0215), GAUGE_Z), (ghx + 0.004, 0.0008, GAUGE_HZ + 0.004), "glass")
    return g


def cell(lit):
    g = Geo()
    step = (GAUGE_X1 - GAUGE_X0) / CELLS
    box(g, (0, 0, 0), (step / 2 - 0.009, 0.0009, GAUGE_HZ - 0.009), "cell" if lit else "cell_off", bevel=0.0008)
    return g


def build_battery(mats, offset=(0, 0, 0)):
    rig = {"root": bl.make_empty("Battery", location=offset)}
    root = rig["root"]
    bl.make_object("Housing", build_housing(), mats, root)
    bl.make_object("Glass", build_glass(), mats, root)
    step = (GAUGE_X1 - GAUGE_X0) / CELLS
    rig["cells"] = []
    for sy, side in ((1, "L"), (-1, "R")):
        for k in range(CELLS):
            node = bl.make_empty(f"Cell{side}{k}", root, (GAUGE_X0 + (k + 0.5) * step, sy * (SIDE + 0.0165), GAUGE_Z))
            bl.make_object(f"Cell{side}{k}Off", cell(False), mats, node)
            on = bl.make_object(f"Cell{side}{k}On", cell(True), mats, node, (0, sy * 0.0011, 0))
            rig["cells"].append((k, on))
    set_charge(rig, CHARGE / CELLS)
    return rig


OFF = 0.001                   # scale of a cell that is off (hidden in its dark piece)


def cell_on(k, charge):
    return charge > k / CELLS


def set_charge(rig, charge):
    for k, ob in rig["cells"]:
        ob.scale = (1.0, 1.0, 1.0) if cell_on(k, charge) else (OFF, OFF, OFF)


def bake(rig):
    """The scrubbed charge clip: each lit cell keyed off just before its fifth of the second and on just after."""
    fps = bbe.FPS
    for k, ob in rig["cells"]:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"charge_{ob.name}")
        ob.animation_data.action = act
        frames = sorted({0, max(0, k * fps // CELLS), k * fps // CELLS + 1, fps})
        for f in frames:
            on = f > k * fps / CELLS
            ob.scale = (1.0, 1.0, 1.0) if on else (OFF, OFF, OFF)
            ob.keyframe_insert("scale", frame=f)
        lt.linear_keys(act)
        bl.push_track(ob, act, "charge")
        ob.animation_data.use_nla = False
    set_charge(rig, CHARGE / CELLS)


def strip_animation(rig):
    for _, ob in rig["cells"]:
        ob.animation_data.action = None


ICON_VIEW = (1.25, -2.1, 0.95)     # as the Gas Container's icon: from the -Y gauge side, the socket end on the right
ICON_ORTHO = 1.42


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_battery(mats)
    bake(rig)
    bbe.export(rig["root"], MESHES / "battery.glb", animations=True)
    bpy.context.scene.frame_set(0)
    strip_animation(rig)
    set_charge(rig, CHARGE / CELLS)
    bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / "battery_icon.png",
                    view=ICON_VIEW, ortho=ICON_ORTHO, target=(0, 0, -0.01))
    for p in (MESHES / "battery.glb", ICONS / "battery_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_battery_container
        render_battery_container.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
