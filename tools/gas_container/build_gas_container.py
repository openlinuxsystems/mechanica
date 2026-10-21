"""Build the Gas Container, its clips and its icon (Gas Container Design Spec of 2026-10-18).

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/gas_container.glb`` - the Gas Container, 4 x 3 x 3 blocks (1.0 x 0.75 x 0.75 m), its long
  axis along Blender X (the game's X). A dark gunmetal box with chamfered corner blocks and riveted edge rails, a
  recessed panel, label plate and round access hatch on each long side, and beside them a gauge window (a teal fuel
  column behind glass, with five tick marks, one a slot). On top two plates, a hatch and a carrying handle (a steel bar
  with a copper grip on copper brackets). The +X end has a copper frame and the standard two-way IO port
  (docs/io-ports.md, ``tools/io_port``: centred on the end 0.375 m above the base, its collar face on x = 0.5, on a
  gunmetal mounting block); the -X end a copper frame and the filler cap. The handle and port stay inside the
  4 x 3 x 3 box.

  Moving pieces are their own nodes: ``LiquidL`` / ``LiquidR`` (the fuel columns; they scale up from the window's
  bottom), and under each the ``Pulse`` and ``Glint`` glows, and ``SocketGlow0`` in the IO port's bore.
  glTF clips (the script ``scripts/gas_container.lua`` picks them from its ``clip`` readout):

  - ``rest`` (0.1 s): every glow off (empty or full).
  - ``idle_loop`` (4 s): a faint glint drifts up the fuel column and fades (partly full).
  - ``pulse`` (0.4 s): the fuel column glows and fades (fuel went in).
  - ``supply`` (0.6 s): a glint runs down the column, the column glows softly and the port's bore lights up (a linked engine
    or thruster drew fuel).
  - ``fill`` (1 s, scrubbed): the fuel columns from empty (0 s) to full (1 s); the ``fill`` readout drives it.

* ``mods/vanilla/textures/items/gas_container_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the gauge side with the sockets on the right (as the reference), 60 % full.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/gas_container/build_gas_container.py                  # writes the .glb and the icon
    python tools/gas_container/build_gas_container.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "bearing_engine"))
sys.path.insert(0, str(HERE.parent / "lift"))
sys.path.insert(0, str(HERE.parent / "io_port"))
import build_bearing_engine as bbe  # noqa: E402  (export, icon render)
import build_io_port as io  # noqa: E402
import build_lift as bl  # noqa: E402
import lift_textures as lt  # noqa: E402
import part_textures as pt  # noqa: E402

REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"
FPS = bbe.FPS

Geo, box, cylinder, ring, dome, hexnut, sweep = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep

# ---- Container (metres; the 1.0 x 0.75 x 0.75 m box centred on the origin, long along X, Z up) ----------------------
BX = 0.41                    # the body's half length: the sockets and filler cap fill the rest to 0.5
BY = 0.335                   # half depth
Z_BOT, Z_TOP = -0.375, 0.265 # the body's bottom and top: the handle fills the rest to 0.375
ZC = (Z_BOT + Z_TOP) / 2
HZ = (Z_TOP - Z_BOT) / 2
RAIL = 0.028                 # edge rails' half thickness
CORNER = 0.046               # corner blocks' half size
SIDE = BY - 0.022            # the long sides' plate faces
GAUGE_X = 0.245              # the gauge's centre along X
WIN_HALF_X = 0.036           # the window's half width
WIN_Z0, WIN_Z1 = -0.245, 0.14  # the window's bottom and top (the fuel column's range)
SOCKET_R = 0.071             # sizes the glow ring in the IO port's bore (inside its wall)
PORT_CENTER = (0.5, 0.0, 0.0)  # the standard IO port: on the +X end, 0.375 m above the base, collar face on x = 0.5
PORT_GLOW_X = 0.496          # just inside the port's mouth, so it shows from an angle
OFF = 0.01                   # scale of a glow that is off (hidden inside the column)


# =====================================================================================================
# Materials
# =====================================================================================================

def half_res(surface):
    """A 256-pixel copy of a 512-pixel tileable surface (every other pixel keeps it tiling), to keep the file small."""
    return tuple(a[::2, ::2] for a in surface)


def glass_material():
    mat = bpy.data.materials.new("gc_glass")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.04, 0.07, 0.08, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.08
    bsdf.inputs["Alpha"].default_value = 0.15
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "BLENDED"
    return mat


def make_materials():
    return {
        "hole": pt.plain("gc_hole", "#0d0e0f", 0.9, 0.2),
        "backing": pt.plain("gc_backing", "#1a1d1f", 0.6, 0.3),
        "tick": pt.plain("gc_tick", "#16181a", 0.5, 0.4),
        "liquid": pt.plain("gc_liquid", "#0e8577", 0.2, 0.0, emission="#13b5a2", strength=1.2),
        "glow": pt.plain("gc_glow", "#5ff2df", 0.3, 0.0, emission="#2de8d2", strength=6.0),
        "glow_soft": pt.plain("gc_glow_soft", "#8af5e8", 0.3, 0.0, emission="#40e0cc", strength=5.0),
        "glass": glass_material(),
        "gun": lt.textured("gc_gun", half_res(lt.metal_surface(401, "#56595e", 0.46, 0.75, wear=0.4, edge_light=0.3)), normal_strength=0.7),
        "gun_dark": lt.textured("gc_gun_dark", half_res(lt.metal_surface(403, "#46494d", 0.52, 0.72, wear=0.7, edge_light=0.25)), normal_strength=0.7),
        "plate": lt.textured("gc_plate", half_res(lt.metal_surface(405, "#4e5155", 0.5, 0.7, wear=0.6, edge_light=0.3)), normal_strength=0.7),
        "copper": lt.textured("gc_copper", half_res(lt.metal_surface(407, "#b5673a", 0.34, 0.95, wear=0.6, edge_light=0.35)), normal_strength=0.7),
        "steel": lt.textured("gc_steel", half_res(lt.metal_surface(409, "#8d9196", 0.3, 0.9, wear=0.6, edge_light=0.3)), normal_strength=0.7),
        "bolt": lt.textured("gc_bolt", half_res(lt.metal_surface(411, "#3c3f43", 0.45, 0.8, wear=0.5, edge_light=0.3)), normal_strength=0.7),
    }


# =====================================================================================================
# Housing
# =====================================================================================================

def rivets(g, a, b, n, normal, r=0.0062, mat="bolt"):
    a, b = Vector(a), Vector(b)
    for k in range(n):
        dome(g, a.lerp(b, k / max(n - 1, 1)), normal, r, mat)


def build_housing():
    g = Geo()
    core = (BX - 0.022, BY - 0.022, HZ - 0.022)
    box(g, (0, 0, ZC), core, "gun_dark", bevel=0.004)
    # edge rails: chamfered square bars along the twelve edges, with rivets
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
                # rivets on both outer faces of the rail
                for o, s in ((others[0], s0), (others[1], s1)):
                    n = [0.0, 0.0, 0.0]
                    n[o] = s
                    a = list(c)
                    a[o] += s * RAIL
                    b = list(a)
                    a[axis] -= half[axis] - 0.03
                    b[axis] += half[axis] - 0.03
                    count = max(2, int(round(2 * half[axis] / 0.09)) + 1)
                    rivets(g, a, b, count, n, r=0.0058)
    # corner blocks: big chamfers, a bolt on each face
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = (sx * (BX - CORNER), sy * (BY - CORNER), ZC + sz * (HZ - CORNER))
                box(g, c, (CORNER, CORNER, CORNER), "gun", bevel=0.016)
                for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                    p = Vector(c) + Vector(n) * CORNER
                    hexnut(g, p, n, 0.0095, 0.005, "bolt")
    for sy in (-1, 1):
        long_side(g, sy)
    build_top(g)
    return g


def long_side(g, sy):
    """One long side: a recessed panel with a rivet border, a label plate, a round access hatch, the gauge's frame."""
    y = sy * SIDE
    n = (0, sy, 0)
    # the big panel: a raised border plate and the recessed field inside it
    px0, px1 = -BX + 0.07, 0.135
    pz0, pz1 = Z_BOT + 0.065, Z_TOP - 0.065
    pcx, pcz = (px0 + px1) / 2, (pz0 + pz1) / 2
    phx, phz = (px1 - px0) / 2, (pz1 - pz0) / 2
    box(g, (pcx, y + sy * 0.006, pcz), (phx, 0.006, phz), "gun", bevel=0.005)
    box(g, (pcx, y + sy * 0.008, pcz), (phx - 0.024, 0.006, phz - 0.024), "plate", bevel=0.004)
    yy = y + sy * 0.012
    rivets(g, (px0 + 0.012, yy, pz0 + 0.012), (px1 - 0.012, yy, pz0 + 0.012), 9, n, r=0.0052)
    rivets(g, (px0 + 0.012, yy, pz1 - 0.012), (px1 - 0.012, yy, pz1 - 0.012), 9, n, r=0.0052)
    rivets(g, (px0 + 0.012, yy, pz0 + 0.06), (px0 + 0.012, yy, pz1 - 0.06), 6, n, r=0.0052)
    rivets(g, (px1 - 0.012, yy, pz0 + 0.06), (px1 - 0.012, yy, pz1 - 0.06), 6, n, r=0.0052)
    # a seam across the panel
    box(g, (pcx, y + sy * 0.0145, pz1 - 0.12), (phx - 0.03, 0.0008, 0.002), "hole")
    # label plate, lower toward the far end, and the round access hatch
    lx, lz = px0 + 0.11, pz0 + 0.075
    box(g, (lx, y + sy * 0.017, lz), (0.07, 0.004, 0.034), "gun", bevel=0.003)
    box(g, (lx, y + sy * 0.0205, lz), (0.06, 0.0008, 0.025), "gun_dark")
    for dx in (-0.058, 0.058):
        for dz in (-0.024, 0.024):
            dome(g, (lx + dx, y + sy * 0.021, lz + dz), n, 0.004, "bolt")
    hx, hz = px1 - 0.1, pz0 + 0.075
    cylinder(g, (hx, y + sy * 0.014, hz), (hx, y + sy * 0.021, hz), 0.05, "gun", segs=40)
    cylinder(g, (hx, y + sy * 0.021, hz), (hx, y + sy * 0.0235, hz), 0.044, "plate", segs=40)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        dome(g, (hx + 0.034 * math.cos(a), y + sy * 0.0235, hz + 0.034 * math.sin(a)), n, 0.0038, "bolt")
    # the gauge: a chamfered frame round the window, a bezel, a dark backing behind the column, five ticks
    gz0, gz1 = Z_BOT + 0.06, Z_TOP - 0.06
    gcz, ghz = (gz0 + gz1) / 2, (gz1 - gz0) / 2
    fx = 0.082
    fy = y + sy * 0.009
    wz = (WIN_Z0 + WIN_Z1) / 2
    wh = (WIN_Z1 - WIN_Z0) / 2
    # the frame as four bars (the window stays open)
    box(g, (GAUGE_X, fy, gz1 - (gz1 - WIN_Z1 - 0.012) / 2), (fx, 0.009, (gz1 - WIN_Z1 - 0.012) / 2), "gun", bevel=0.007)
    box(g, (GAUGE_X, fy, gz0 + (WIN_Z0 - 0.012 - gz0) / 2), (fx, 0.009, (WIN_Z0 - 0.012 - gz0) / 2), "gun", bevel=0.007)
    for s in (-1, 1):
        bw = (fx - WIN_HALF_X - 0.012) / 2
        box(g, (GAUGE_X + s * (WIN_HALF_X + 0.012 + bw), fy, wz), (bw, 0.009, wh + 0.012), "gun", bevel=0.006)
    # bezel round the window
    by = y + sy * 0.017
    for s in (-1, 1):
        box(g, (GAUGE_X + s * (WIN_HALF_X + 0.006), by, wz), (0.006, 0.006, wh + 0.012), "gun_dark", bevel=0.002)
        box(g, (GAUGE_X, by, wz + s * (wh + 0.006)), (WIN_HALF_X + 0.012, 0.006, 0.006), "gun_dark", bevel=0.002)
    box(g, (GAUGE_X, y - sy * 0.002, wz), (WIN_HALF_X, 0.003, wh), "backing")
    # bolts on the frame's corners and middles
    for dz in (gz1 - 0.025, gz0 + 0.025):
        for dx in (-0.055, 0.055):
            dome(g, (GAUGE_X + dx, y + sy * 0.018, dz), n, 0.0055, "bolt")
    for dx in (-0.062, 0.062):
        for dz in (wz - wh * 0.6, wz + wh * 0.6):
            dome(g, (GAUGE_X + dx, y + sy * 0.018, dz), n, 0.0055, "bolt")
    # ticks on the glass, one each fifth (a slot), at the window's viewer-left edge
    gy = y + sy * 0.0128
    for k in range(1, 5):
        z = WIN_Z0 + (WIN_Z1 - WIN_Z0) * k / 5
        box(g, (GAUGE_X - WIN_HALF_X + 0.012, gy + sy * 0.0008, z), (0.012, 0.0006, 0.0018), "tick")
        for j in range(1, 3):
            zz = z - (WIN_Z1 - WIN_Z0) / 5 * j / 3
            box(g, (GAUGE_X - WIN_HALF_X + 0.007, gy + sy * 0.0008, zz), (0.007, 0.0006, 0.0012), "tick")


def build_glass():
    g = Geo()
    wz = (WIN_Z0 + WIN_Z1) / 2
    wh = (WIN_Z1 - WIN_Z0) / 2
    for sy in (-1, 1):
        box(g, (GAUGE_X, sy * (SIDE + 0.0125), wz), (WIN_HALF_X + 0.004, 0.0012, wh + 0.004), "glass")
    return g


def build_top(g):
    """The top: two plates either side of a hatch, the handle's brackets."""
    z = Z_TOP - 0.022
    for sx in (-1, 1):
        box(g, (sx * 0.245, 0, z + 0.006), (0.12, BY - 0.075, 0.006), "plate", bevel=0.004)
        rivets(g, (sx * 0.245 - 0.105, -(BY - 0.09), z + 0.012), (sx * 0.245 + 0.105, -(BY - 0.09), z + 0.012), 5, (0, 0, 1), r=0.005)
        rivets(g, (sx * 0.245 - 0.105, BY - 0.09, z + 0.012), (sx * 0.245 + 0.105, BY - 0.09, z + 0.012), 5, (0, 0, 1), r=0.005)
    # the hatch: a raised frame and a darker lid with a slot
    box(g, (0, 0, z + 0.007), (0.1, 0.13, 0.007), "gun", bevel=0.005)
    box(g, (0, 0, z + 0.012), (0.082, 0.112, 0.005), "gun_dark", bevel=0.003)
    box(g, (0, -0.07, z + 0.0172), (0.04, 0.008, 0.0006), "hole")
    # handle brackets (copper blocks with a bolt each side), the bar is build_handle()
    for sx in (-1, 1):
        x = sx * 0.17
        box(g, (x, 0, Z_TOP + 0.006), (0.034, 0.042, 0.012), "copper", bevel=0.005)
        box(g, (x, 0, Z_TOP + 0.026), (0.022, 0.026, 0.014), "copper", bevel=0.004)
        for sy in (-1, 1):
            hexnut(g, (x - sx * 0.002, sy * 0.042, Z_TOP + 0.006), (0, sy, 0), 0.007, 0.004, "bolt")


def build_handle():
    g = Geo()
    rad = 0.0145
    top = 0.352
    # a loop from one bracket up, across and down to the other, with rounded bends
    pts = [Vector((-0.17, 0, Z_TOP + 0.03)), Vector((-0.17, 0, top - 0.045))]
    for k in range(1, 7):
        a = math.pi / 2 * k / 6
        pts.append(Vector((-0.17 + 0.045 * (1 - math.cos(a)), 0, top - 0.045 + 0.045 * math.sin(a))))
    for k in range(0, 7):
        a = math.pi / 2 * k / 6
        pts.append(Vector((0.125 + 0.045 * math.sin(a), 0, top - 0.045 * (1 - math.cos(a)))))
    pts.append(Vector((0.17, 0, Z_TOP + 0.03)))
    sweep(g, pts, rad, "steel", segs=16)
    # the copper grip over the middle, with rims
    cylinder(g, (-0.105, 0, top), (0.105, 0, top), rad + 0.0055, "copper", segs=20)
    for x in (-0.105, 0.105):
        cylinder(g, (x - 0.006, 0, top), (x + 0.006, 0, top), rad + 0.0075, "copper", segs=20)
    return g


# =====================================================================================================
# Ends
# =====================================================================================================

def end_frame(g, sx):
    """A copper frame round the end face, over a gunmetal inset panel."""
    x = sx * (BX - 0.004)
    fy, fz = BY - 0.05, HZ - 0.05
    t = 0.022
    for s in (-1, 1):
        box(g, (x + sx * 0.008, s * (fy - t), ZC), (0.008, t, fz), "copper", bevel=0.005)
        box(g, (x + sx * 0.008, 0, ZC + s * (fz - t)), (0.008, fy, t), "copper", bevel=0.005)
    box(g, (x + sx * 0.003, 0, ZC), (0.004, fy - 2 * t, fz - 2 * t), "plate", bevel=0.003)
    for s in (-1, 1):
        for zz in np.linspace(ZC - fz + 0.04, ZC + fz - 0.04, 5):
            dome(g, (x + sx * 0.016, s * (fy - t), zz), (sx, 0, 0), 0.0055, "bolt")


def build_ends():
    g = Geo()
    end_frame(g, 1)
    end_frame(g, -1)
    # the IO port's mounting block on +X (mount_io_port adds the port itself)
    port_block(g, BX)
    # the filler cap on -X: a boss and a ribbed cap
    z = ZC + 0.11
    cylinder(g, (-BX - 0.005, 0, z), (-BX - 0.024, 0, z), 0.058, "copper", segs=36)
    cylinder(g, (-BX - 0.024, 0, z), (-0.493, 0, z), 0.044, "gun", segs=36)
    for k in range(12):
        a = 2 * math.pi * k / 12
        box(g, (-0.462, 0.044 * math.cos(a), z + 0.044 * math.sin(a)), (0.03, 0.004, 0.004), "gun",
            rot=Matrix.Rotation(a, 3, "X"))
    cylinder(g, (-0.493, 0, z), (-0.497, 0, z), 0.03, "copper", segs=24)
    return g


def port_block(g, x_wall, top=0.462):
    """A gunmetal block from an end wall at x_wall to just behind the IO port's plate face (the plate sinks into it),
    a little smaller than the plate so it hides behind it."""
    h = io.PORT_SIZE / 2 - 0.006
    box(g, ((x_wall + top) / 2, 0, PORT_CENTER[2]), ((top - x_wall) / 2 + 0.002, h, h), "gun", bevel=0.006)


_IO_MATS = {}


def mount_io_port(parent, center=PORT_CENTER, normal=(1, 0, 0), mode="both"):
    """The standard IO port (docs/io-ports.md) under parent, baked into one mesh node ``IOPort`` (bevels applied, in
    parent space, game materials), as the Fabricator does."""
    io.GAME = True
    if not _IO_MATS:
        _IO_MATS.update(io.materials())
    port = io.mount(parent, mode, center, normal, _IO_MATS, "IOPortParts")
    # roll it about its normal so its own +Y (the two-way port's teal "out" axis) stands up, as docs/io-ports.md shows
    n = Vector(normal).normalized()
    q = port.rotation_euler.to_quaternion()
    up = Vector((0, 0, 1))
    if abs(n.dot(up)) < 0.9:
        v = q @ Vector((0, 1, 0))
        roll = math.atan2(n.dot(v.cross(up)), v.dot(up))
        port.rotation_euler = (Quaternion(n, roll) @ q).to_euler()
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    inv = parent.matrix_world.inverted()
    parts = []
    for ob in port.children_recursive:
        if ob.type != "MESH":
            continue
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        me.transform(inv @ ob.matrix_world)
        part = bpy.data.objects.new("IOPort", me)
        bpy.context.scene.collection.objects.link(part)
        parts.append(part)
    for ob in [*port.children_recursive, port]:
        bpy.data.objects.remove(ob)
    with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, selected_objects=parts):
        bpy.ops.object.join()
    ob = parts[0]
    ob.name = ob.data.name = "IOPort"
    ob.parent = parent
    ob.matrix_parent_inverse.identity()
    ob.location = (0, 0, 0)
    lt.box_uvs(ob.data, [None] * len(ob.data.polygons))
    return ob


# =====================================================================================================
# Moving pieces: fuel columns and glows
# =====================================================================================================

def column(mat, half_y, inset=0.0):
    """A fuel column from z 0 (the window's bottom) up to the window's top, so scaling it in Z fills the window."""
    g = Geo()
    h = WIN_Z1 - WIN_Z0
    box(g, (0, 0, h / 2), (WIN_HALF_X - 0.002 - inset, half_y, h / 2 - inset), mat)
    return g


def glint():
    g = Geo()
    box(g, (0, 0, 0), (WIN_HALF_X - 0.004, 0.0006, 0.012), "glow_soft")
    return g


def socket_glow():
    g = Geo()
    ring(g, -0.001, 0.001, SOCKET_R - 0.026, SOCKET_R - 0.015, "glow", segs=36)
    out = Geo()
    out.extend(g, Matrix.Rotation(math.pi / 2, 4, "Y"))
    return out


def build_container(mats):
    rig = {}
    root = rig["root"] = bl.make_empty("GasContainer")
    bl.make_object("Housing", build_housing(), mats, root)
    bl.make_object("Ends", build_ends(), mats, root)
    bl.make_object("Handle", build_handle(), mats, root)
    bl.make_object("Glass", build_glass(), mats, root)
    rig["liquid"], rig["pulse"], rig["glint"] = [], [], []
    liquid, pulse = column("liquid", 0.0035), column("glow_soft", 0.0006, inset=0.002)
    for sy, side in ((1, "L"), (-1, "R")):
        y = sy * (SIDE - 0.002 + 0.0035)
        lq = bl.make_object(f"Liquid{side}", liquid, mats, root, (GAUGE_X, y, WIN_Z0))
        rig["liquid"].append(lq)
        rig["pulse"].append(bl.make_object(f"Pulse{side}", pulse, mats, lq, (0, sy * 0.0045, 0)))
        rig["glint"].append(bl.make_object(f"Glint{side}", glint(), mats, lq, (0, sy * 0.0055, 0)))
    mount_io_port(root)
    rig["sockets"] = [bl.make_object("SocketGlow0", socket_glow(), mats, root, (PORT_GLOW_X, 0, 0))]
    for ob in [*rig["liquid"], *rig["pulse"], *rig["glint"], *rig["sockets"]]:
        ob.rotation_mode = "XYZ"
    apply_pose(rig, rest_pose())
    set_fill(rig, 0.6)
    return rig


# =====================================================================================================
# Clips
# =====================================================================================================

def rest_pose():
    return {"pulse": OFF, "glint": OFF, "glint_at": 0.5, "sockets": OFF}


def ease(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def clip_rest():
    """Every glow off; the hidden glint slides back to the column's foot (a clip that changes nothing is not exported)."""
    return 0.1, lambda t: {"glint_at": 0.5 - 4.0 * t}


def clip_idle():
    """A faint glint drifting up the column, fading in and out, every 4 s."""
    def pose(t):
        u = t / 4.0
        if u > 0.75:
            return {}
        k = u / 0.75
        return {"glint": max(OFF, 0.55 * math.sin(math.pi * k)), "glint_at": 0.1 + 0.8 * k}
    return 4.0, pose


def clip_pulse():
    def pose(t):
        return {"pulse": max(OFF, math.sin(math.pi * min(1.0, t / 0.3)) if t < 0.3 else OFF)}
    return 0.4, pose


def clip_supply():
    def pose(t):
        k = min(1.0, t / 0.5)
        return {"glint": max(OFF, math.sin(math.pi * k)), "glint_at": 0.95 - 0.9 * ease(k),
                "pulse": max(OFF, 0.45 * math.sin(math.pi * min(1.0, t / 0.4)) if t < 0.4 else OFF),
                "sockets": max(OFF, math.sin(math.pi * min(1.0, t / 0.6)))}
    return 0.6, pose


def apply_pose(rig, p):
    for ob in rig["pulse"]:
        v = p["pulse"]
        ob.scale = (v, v, 1.0 if v > OFF else OFF)
    h = WIN_Z1 - WIN_Z0
    for ob in rig["glint"]:
        v = p["glint"]
        ob.scale = (v, 1.0, v)
        ob.location.z = h * p["glint_at"]
    for ob in rig["sockets"]:
        v = p["sockets"]
        ob.scale = (v, v, v)


def set_fill(rig, f):
    for ob in rig["liquid"]:
        ob.scale = (1.0, 1.0, max(0.002, f))


def clip_objects(rig):
    return [*rig["pulse"], *rig["glint"], *rig["sockets"]]


def bake(rig):
    """Keys every clip on every glow (each clip keys them all), then the scrubbed fill on the fuel columns."""
    clips = {"rest": clip_rest(), "idle_loop": clip_idle(), "pulse": clip_pulse(), "supply": clip_supply()}
    objs = clip_objects(rig)
    for ob in objs:
        ob.animation_data_create()
    for name, (length, pose) in clips.items():
        actions = {}
        for ob in objs:
            act = bpy.data.actions.new(f"{name}_{ob.name}")
            ob.animation_data.action = act
            actions[ob.name] = act
        frames = int(round(length * FPS))
        for f in range(frames + 1):
            p = rest_pose()
            loop_end = name.endswith("_loop") and f == frames
            p.update(pose(0.0 if loop_end else f / FPS))
            apply_pose(rig, p)
            for ob in objs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(actions[ob.name])
            bl.push_track(ob, actions[ob.name], name)
    for ob in rig["liquid"]:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"fill_{ob.name}")
        ob.animation_data.action = act
        for f in (0, FPS):
            ob.scale = (1.0, 1.0, max(0.002, f / FPS))
            ob.keyframe_insert("scale", frame=f)
        lt.linear_keys(act)
        bl.push_track(ob, act, "fill")
    for ob in [*objs, *rig["liquid"]]:
        ob.animation_data.use_nla = False
    apply_pose(rig, rest_pose())
    set_fill(rig, 0.6)


def strip_animation(rig):
    for ob in bbe.mesh_tree(rig["root"]):
        if ob.animation_data:
            ob.animation_data.action = None


# =====================================================================================================
# Main
# =====================================================================================================

ICON_VIEW = (1.25, -2.1, 0.95)     # three-quarter from the -Y gauge side, the +X sockets on the right
ICON_ORTHO = 1.42


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_container(mats)
    bake(rig)
    path = MESHES / "gas_container.glb"
    bbe.export(rig["root"], path, animations=True)
    bpy.context.scene.frame_set(0)
    strip_animation(rig)
    apply_pose(rig, rest_pose())
    set_fill(rig, 0.6)
    icon = ICONS / "gas_container_icon.png"
    bbe.render_icon(bbe.mesh_tree(rig["root"]), icon, view=ICON_VIEW, ortho=ICON_ORTHO, target=(0, 0, -0.01))
    for p in (path, icon):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_gas_container
        render_gas_container.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
