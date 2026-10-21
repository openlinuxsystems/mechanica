"""Build the five chests and their icons: Scrap Chest, Small Chest, Large Chest, Cargo Chest and Ruin Chest.

The Gas Container's approach (tools/gas_container/build_gas_container.py): procedural, from scratch (no .blend inputs, no
downloads), on the Lift's geometry and texture kit and the Bearing's export and icon render. Sizes are in 0.25 m blocks,
X wide x Y tall x Z deep in the game; in Blender X is the width, Z is up and the front (the latch side; the lid hinges at
the back) looks along -Y, which the exporter turns into the game's +Z. Each chest is centred on the origin and fits
inside its box.

* ``chest`` - Scrap Chest, 3 x 2 x 3 (0.75 x 0.5 x 0.75 m). Settler Patchwork: a crate-chest of mismatched scrap wood
  planks (#9C7650) on dark corner posts, patched with rusty and zinc straps held by mismatched bolts and nuts, zinc
  corner brackets, a bolted zinc patch over a broken plank, cut hand holds, strap hinges and a simple hasp and staple
  with a bent pin. No item port: it is the scrap tier.
* ``small_chest`` - Small Chest, 3 x 3 x 3 (a 0.75 m cube, tall enough for a port at the standard height). Salvager Tech (hull #24282E, gunmetal #5A6068, copper #B8733A, cyan
  #38C8FF): a framed steel case (edge rails with rivets, chamfered corner blocks with nuts) with recessed riveted
  panels, an inset lid on copper hinges with a pull slot, a copper latch with a cyan lamp, a grip on the -X side and
  one two-way item port (docs/io-ports.md) on the +X side.
* ``large_chest`` - Large Chest, 5 x 3 x 4 (1.25 x 0.75 x 1.0 m). The same case, bigger: an inset lid with stiffening
  ribs and a cyan light bar, three hinges, two latches, a label plate with a cyan status bar, and three item ports
  (both ends and the back).
* ``cargo_chest`` - Cargo Chest, 8 x 6 x 6 (2.0 x 1.5 x 1.5 m), the 100-slot bin. A ribbed cargo bin on a forklift
  skid base (fork pockets front and back, steel shoes), corner posts, a belt rail over a row of ten item ports (three
  front, three back, two each end, all at the standard height), a mustard-and-hull hazard band under a sunken lid
  with ribs, two lift handles and four lifting lugs, a cyan slot screen and cyan running lights.
* ``ruin_chest`` - Ruin Chest, 4 x 3 x 3 (1.0 x 0.75 x 0.75 m). Old Stone (stone #8F9289, dark stone #5F645C, dark
  cracks, moss on the top edges): a dressed-stone chest on a plinth with corner pilasters, carved frames and glyphs,
  a heavy hipped stone lid with a dark band and a round seal whose ring glyph still glows faint cyan (loot).

Each model is one root node with ``Body`` and ``Lid`` meshes (the lid's origin is on its hinge line, so the game can
swing it later) and, on the Salvager chests, one ``IOPort*`` node per port, all sharing one mesh. Every port is the standard face (tools/io_port, docs/io-ports.md), placed by its
``mount`` in mode "both": its centre 0.375 m above the chest's floor and on a block centre, the collar's face on the
footprint boundary, the wall behind the plate 22 mm in. Static: no clips.

Also writes ``mods/vanilla/textures/items/<id>_icon.png``: 256 x 256 on a clear background, three-quarter from the
front right, framed as the Gas Container's icon (the same view, the box filling the frame the same way).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/containers/build_chests.py                  # writes the five .glb files and icons
    python tools/containers/build_chests.py --render OUT_DIR # also renders review sheets into OUT_DIR
    python tools/containers/build_chests.py --only small_chest,cargo_chest   # just those

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic
(seeded noise only).
"""

from __future__ import annotations

import json
import math
import random
import struct
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_container"))
sys.path.insert(0, str(HERE.parent / "io_port"))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)
import build_io_port as io  # noqa: E402

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, ring, dome, hexnut, sweep = bl.Geo, bl.box, bl.cylinder, bl.ring, bl.dome, bl.hexnut, bl.sweep
rivets = gc.rivets

B = 0.25                      # one block
FB = 0.004                    # the frame's outer faces sit this far inside the box, so rivet and nut heads stay in it
SIDE_IN = 0.022               # a Salvager case's walls sit this far inside the box (the port plate stands 1 cm proud)
ICON_VIEW = gc.ICON_VIEW      # the Gas Container's icon view: three-quarter from the front right
GAS_BOX = (0.5, 0.375, 0.375)  # the Gas Container's half sizes and icon ortho, to frame every chest the same way
GAS_ORTHO = gc.ICON_ORTHO


# =====================================================================================================
# Materials
# =====================================================================================================

half_res = gc.half_res


def wood_surface(seed, color, grey=0.0):
    """Scrap wood: grain along the image's u (the plank's length), broad bands, a few knots, grey weathering, dirt."""
    S = lt.SIZE
    lines = lt.fnoise(seed, 150.0, aniso=(0.025, 1.0))
    bands = lt.fnoise(seed + 1, 28.0, aniso=(0.07, 1.0))
    mott = lt.fbm(seed + 2, 3.0)
    weather = np.clip(lt.fbm(seed + 3, 4.0) * 1.6 + grey, 0, 1)
    dirt = np.clip(lt.fbm(seed + 4, 2.0) * 1.5 - 0.15, 0, 1)
    yy, xx = np.mgrid[0:S, 0:S].astype(float)
    rng = np.random.default_rng(seed + 5)
    knot = np.zeros((S, S))
    for _ in range(4):
        cx, cy, r = rng.uniform(0, S), rng.uniform(0, S), rng.uniform(5, 11)
        dx = np.minimum(np.abs(xx - cx), S - np.abs(xx - cx)) * 0.45
        dy = np.minimum(np.abs(yy - cy), S - np.abs(yy - cy))
        knot = np.maximum(knot, np.exp(-((dx * dx + dy * dy) / (r * r))))
    sc = lt.scratches(seed + 6, 70, bias=0.0) * 0.6
    base = lt.srgb(color)[None, None, :]
    albedo = base * (1 + 0.2 * lines[..., None] + 0.12 * bands[..., None] + 0.08 * mott[..., None])
    albedo = albedo * (1 - 0.55 * knot[..., None])
    grey_c = lt.srgb("#8a7a64")[None, None, :] * (1 + 0.12 * lines[..., None])
    w = 0.28 * weather[..., None]
    albedo = albedo * (1 - w) + grey_c * w
    albedo = albedo * (1 - 0.3 * dirt[..., None]) + 0.25 * sc[..., None] * (0.75 - albedo)
    height = 0.7 * lines + 0.35 * bands - 0.9 * knot - 0.4 * sc
    rough = 0.8 + 0.08 * weather - 0.05 * knot
    return albedo, height, rough, np.zeros((S, S))


def stone_surface(seed, color, cracks=1.0):
    """Dressed stone: mottled, speckled, pale lichen spots and a network of dark hairline cracks."""
    S = lt.SIZE
    mott = lt.fbm(seed, 3.0)
    fine = lt.fnoise(seed + 1, 90.0)
    speck = (lt.fnoise(seed + 2, 210.0) > 0.55).astype(float)
    warp = lt.fbm(seed + 3, 5.0)
    c1 = np.abs(lt.fnoise(seed + 4, 6.0) + 0.3 * warp)
    c2 = np.abs(lt.fnoise(seed + 5, 11.0) + 0.2 * warp)
    gate = np.clip(lt.fnoise(seed + 6, 3.0) * 2.5 - 0.4, 0, 1)
    crack = np.clip(np.maximum(1 - c1 / 0.016, (1 - c2 / 0.011) * 0.6), 0, 1) * gate * cracks
    lichen = np.clip((lt.fbm(seed + 7, 7.0) - 0.42) * 5, 0, 1)
    base = lt.srgb(color)[None, None, :]
    albedo = base * (1 + 0.16 * mott[..., None] + 0.06 * fine[..., None]) * (1 - 0.18 * speck[..., None])
    albedo = albedo * (1 - 0.2 * lichen[..., None]) + lt.srgb("#8f9a78")[None, None, :] * 0.2 * lichen[..., None]
    albedo = albedo * (1 - 0.6 * crack[..., None])
    height = 0.45 * mott + 0.3 * fine - 1.4 * crack - 0.3 * speck
    rough = 0.88 + 0.05 * mott
    return albedo, height, rough, np.zeros((S, S))


def moss_surface(seed):
    S = lt.SIZE
    clump = lt.fnoise(seed, 60.0)
    mott = lt.fbm(seed + 1, 5.0)
    dry = np.clip(lt.fbm(seed + 2, 4.0) * 1.8 - 0.2, 0, 1)
    albedo = lt.srgb("#6f9a3a")[None, None, :] * (0.78 + 0.3 * clump[..., None] + 0.15 * mott[..., None])
    albedo = albedo * (1 - 0.35 * dry[..., None]) + lt.srgb("#5c6a30")[None, None, :] * 0.35 * dry[..., None]
    return albedo, 1.0 * clump + 0.3 * mott, np.full((S, S), 0.96), np.zeros((S, S))


def hazard_surface(seed):
    """Diagonal mustard and hull stripes (no orange: the art direction keeps it off), chipped."""
    S = lt.SIZE
    yy, xx = np.mgrid[0:S, 0:S]
    stripe = (((xx + yy) // (S // 8)) % 2).astype(float)
    a_y, h_y, r_y, m_y = lt.paint_surface(seed, "#d8aa2c", "#5a6068")
    a_b, h_b, r_b, m_b = lt.paint_surface(seed + 7, "#24282e", "#5a6068")
    s = stripe[..., None]
    return (a_y * s + a_b * (1 - s), h_y * stripe + h_b * (1 - stripe), r_y * stripe + r_b * (1 - stripe),
            m_y * stripe + m_b * (1 - stripe))


def make_materials():
    def metal(name, seed, color, rough, metal_, wear=0.5, edge=0.3):
        return lt.textured(name, half_res(lt.metal_surface(seed, color, rough, metal_, wear=wear, edge_light=edge)),
                           normal_strength=0.7)

    io.GAME = True                  # the port's plain game materials (as the Fabricator ships them)
    m = {f"io_{k}": v for k, v in io.materials().items()}
    m.update({
        # Salvager Tech
        "hull": metal("ch_hull", 501, "#24282e", 0.5, 0.65, wear=0.5, edge=0.25),
        "panel": metal("ch_panel", 503, "#2c3138", 0.48, 0.7, wear=0.6, edge=0.3),
        "gun": metal("ch_gun", 505, "#5a6068", 0.42, 0.8, wear=0.45, edge=0.3),
        "gun_dark": metal("ch_gun_dark", 507, "#3e444c", 0.46, 0.78, wear=0.6, edge=0.25),
        "copper": metal("ch_copper", 509, "#b8733a", 0.34, 0.95, wear=0.6, edge=0.35),
        "steel": metal("ch_steel", 511, "#8d9196", 0.3, 0.9, wear=0.6),
        "bolt": metal("ch_bolt", 513, "#2f3339", 0.45, 0.8, wear=0.5),
        "hazard": lt.textured("ch_hazard", half_res(hazard_surface(515)), normal_strength=0.5),
        "hole": pt.plain("ch_hole", "#0d0e0f", 0.9, 0.2),
        "screen": pt.plain("ch_screen", "#0a1216", 0.25, 0.1),
        "cyan": pt.plain("ch_cyan", "#38c8ff", 0.3, 0.0, emission="#38c8ff", strength=4.0),
        "cyan_off": pt.plain("ch_cyan_off", "#163440", 0.35, 0.1),
        # Settler Patchwork
        "wood": lt.textured("ch_wood", half_res(wood_surface(601, "#9c7650")), normal_strength=1.2),
        "wood_b": lt.textured("ch_wood_b", half_res(wood_surface(611, "#8a6644", grey=0.1)), normal_strength=1.2),
        "wood_c": lt.textured("ch_wood_c", half_res(wood_surface(621, "#9a7b58", grey=0.35)), normal_strength=1.2),
        "wood_dark": lt.textured("ch_wood_dark", half_res(wood_surface(631, "#5e4630", grey=0.05)), normal_strength=1.2),
        "inside": pt.plain("ch_inside", "#241b13", 0.9, 0.0),
        "rust": lt.textured("ch_rust", half_res(pt.rust_surface(641, "#7a4a30", "#55301c", "#4a4642", wear=1.1)), normal_strength=1.0),
        "zinc": metal("ch_zinc", 651, "#9aa3ab", 0.42, 0.85, wear=1.2, edge=0.3),
        "iron": metal("ch_iron", 661, "#3b3936", 0.55, 0.75, wear=0.9),
        # Old Stone
        "stone": lt.textured("ch_stone", half_res(stone_surface(701, "#8f9289")), normal_strength=1.0),
        "stone_b": lt.textured("ch_stone_b", half_res(stone_surface(711, "#868980", cracks=0.6)), normal_strength=1.0),
        "stone_dark": lt.textured("ch_stone_dark", half_res(stone_surface(721, "#5f645c", cracks=0.5)), normal_strength=1.0),
        "crack": pt.plain("ch_crack", "#1f221e", 0.95, 0.0),
        "moss": lt.textured("ch_moss", half_res(moss_surface(731)), normal_strength=1.5),
        "rune": pt.plain("ch_rune", "#38c8ff", 0.4, 0.0, emission="#38c8ff", strength=1.6),
    })
    return m


# =====================================================================================================
# Shape helpers
# =====================================================================================================

def uv_axes(axis):
    """The face's in-plane axes (u, v): X faces run along Y and Z, Y faces along X and Z, Z faces along X and Y."""
    return {0: (1, 2), 1: (0, 2), 2: (0, 1)}[axis]


def at(axis, n, u, v):
    """A point from its coordinate n along `axis` and (u, v) in the face's plane."""
    p = [0.0, 0.0, 0.0]
    ua, va = uv_axes(axis)
    p[axis], p[ua], p[va] = n, u, v
    return tuple(p)


def half3(axis, hn, hu, hv):
    h = [0.0, 0.0, 0.0]
    ua, va = uv_axes(axis)
    h[axis], h[ua], h[va] = hn, hu, hv
    return tuple(h)


def face_frame(axis, sign, angle=0.0):
    """Unit vectors (along, across, normal), right-handed, for a shape on a face turned `angle` in its plane."""
    ua, va = uv_axes(axis)
    U, V, N = Vector((0, 0, 0)), Vector((0, 0, 0)), Vector((0, 0, 0))
    U[ua], V[va], N[axis] = 1.0, 1.0, float(sign)
    e0 = U * math.cos(angle) + V * math.sin(angle)
    e1 = N.cross(e0)
    return e0, e1, N


def slab(g, axis, sign, plane, u, v, hu, hv, d0, d1, mat, bevel=0.0, angle=0.0):
    """A box lying on a face: (u, v) its centre in the face plane, from d0 to d1 out of the plane at `plane`."""
    n = plane + sign * (d0 + d1) / 2
    if angle == 0.0:
        return box(g, at(axis, n, u, v), half3(axis, (d1 - d0) / 2, hu, hv), mat, bevel=bevel)
    e0, e1, N = face_frame(axis, sign, angle)
    rot = Matrix((e0, e1, N)).transposed()
    return box(g, at(axis, n, u, v), (hu, hv, (d1 - d0) / 2), mat, bevel=bevel, rot=rot)


def on_face(g, axis, sign, plane, u, v, d, shape, angle=0.0):
    """Places a Geo built on the XY plane looking along +Z onto a face at (u, v), d out of the plane."""
    e0, e1, N = face_frame(axis, sign, angle)
    m = Matrix((e0, e1, N)).transposed().to_4x4()
    m.translation = Vector(at(axis, plane + sign * d, u, v))
    g.extend(shape, m)


def flat_ring(r_in, r_out, t, mat, segs=32):
    s = Geo()
    ring(s, 0.0, t, r_in, r_out, mat, segs=segs)
    return s


def hull_of(g, points, mat):
    """The convex hull of the points (a frustum, a wedge)."""
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in points]
    bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    verts, faces = bl._from_bmesh(bm)
    return g.add(verts, faces, mat)


def ellipsoid(g, c, r, mat, floor=None, clamp=None, subdiv=2, seed=0):
    """A lumpy flattened blob (moss cushion): radii r, cut flat at z=floor, clamped inside the box `clamp`."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    rng = random.Random(seed)
    ph = [rng.uniform(0, 6.28) for _ in range(3)]
    for v in bm.verts:
        d = v.co.normalized()
        f = 1 + 0.12 * math.sin(4 * d.x + ph[0]) * math.cos(3 * d.y + ph[1]) + 0.08 * math.sin(6 * d.z + ph[2])
        p = Vector((c[0] + d.x * r[0] * f, c[1] + d.y * r[1] * f, c[2] + d.z * r[2] * f))
        if floor is not None:
            p.z = max(p.z, floor)
        if clamp is not None:
            p = Vector([min(max(p[i], -clamp[i]), clamp[i]) for i in range(3)])
        v.co = p
    verts, faces = bl._from_bmesh(bm)
    return g.add(verts, faces, mat)


def segments(lo, hi, gaps):
    """[lo, hi] with the gap intervals taken out."""
    out = [(lo, hi)]
    for a, b in gaps:
        out = [(p, q) for s, e in out for p, q in ((s, min(e, a)), (max(s, b), e)) if q - p > 1e-6]
    return out


def jitter(g, start, amp, box_half, z_floor, seed):
    """Nudges the vertices from index `start` inward by up to amp (hand-cut, weathered stone). Vertices at the same place
    move together, so no seams open; the floor stays flat."""
    out = []
    k = (12.9898 + seed, 78.233, 37.719)
    for i in range(start, len(g.v)):
        x, y, z = g.v[i]
        h = math.sin(round(x, 4) * k[0] + round(y, 4) * k[1] + round(z, 4) * k[2]) * 43758.5453
        n = h - math.floor(h)
        h2 = math.sin(round(x, 4) * 39.3 + round(y, 4) * 11.7 + round(z, 4) * 71.1 + seed) * 24634.6345
        n2 = h2 - math.floor(h2)
        a = amp * n
        x = x - math.copysign(a, x) * (abs(x) > box_half[0] - 0.05)
        y = y - math.copysign(a, y) * (abs(y) > box_half[1] - 0.05)
        if z > z_floor + 0.002:
            z = max(z_floor, z - amp * 0.6 * n2)
        out.append((x, y, z))
    g.v[start:] = out


# =====================================================================================================
# The item port: the standard face (tools/io_port), as the Fabricator ships it, one mesh shared by every port
# =====================================================================================================

class Ports:
    """Mounts the standard two-way port with build_io_port.mount (as the Fabricator does): the collar's face on the
    footprint boundary, the plate 11 mm inside it. mount() builds a port from some thirty pieces; each port's pieces are
    joined into one mesh (unchanged: the game gets the same unbevelled geometry the Fabricator's export writes), and
    every port of a chest shares the first port's mesh."""

    def __init__(self, mats):
        self.io = {k[3:]: v for k, v in mats.items() if k.startswith("io_")}
        self.mesh = None

    def mount(self, root, name, center, normal):
        port = io.mount(root, "both", center, normal, self.io, name)
        parts = [o for o in port.children if o.type == "MESH"]
        for o in parts:
            o.modifiers.clear()
        with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, object=parts[0]):
            bpy.ops.object.join()
        joined = parts[0]
        joined.name = f"{name}_face"
        if self.mesh is None:
            self.mesh = joined.data
            self.mesh.name = "IOPort"
        else:
            old = joined.data
            joined.data = self.mesh
            bpy.data.meshes.remove(old)
        return port


# =====================================================================================================
# Salvager Tech case: rails and corner blocks round a hull core, panels, an inset lid
# =====================================================================================================

class Case:
    """A framed case filling the box x +-hx, y +-hy, z0..z1. The rails' and corners' outer faces sit FB inside the box
    (the bottom on z0); the walls SIDE_IN inside. Face coordinates: d is measured out from the wall."""

    def __init__(self, hx, hy, z0, z1, rail, corner):
        self.hx, self.hy, self.z0, self.z1 = hx, hy, z0, z1
        self.bx, self.by, self.zt = hx - FB, hy - FB, z1 - FB
        self.cz, self.hz = (z0 + self.zt) / 2, (self.zt - z0) / 2
        self.rail, self.corner = rail, corner

    def wall(self, axis, sign):
        """The wall plane's coordinate along axis."""
        if axis == 2:
            return self.z1 - SIDE_IN if sign > 0 else self.z0 + SIDE_IN
        return sign * ((self.hx, self.hy)[axis] - SIDE_IN)

    def slab(self, g, axis, sign, u, v, hu, hv, d0, d1, mat, bevel=0.0, angle=0.0):
        return slab(g, axis, sign, self.wall(axis, sign), u, v, hu, hv, d0, d1, mat, bevel, angle)

    def point(self, axis, sign, u, v, d):
        return at(axis, self.wall(axis, sign) + sign * d, u, v)

    def frame(self, g, gaps=None, skip=(), rivet_step=0.09, core="hull", core_sink=0.0):
        """The core (its top core_sink lower, under a sunken lid), the edge rails (less the gaps and skipped ones,
        keyed (axis, sign, sign) of the other two axes) and the corner blocks."""
        gaps = gaps or {}
        r, c = self.rail, self.corner
        self.core_top = self.z1 - SIDE_IN - core_sink
        cb = self.z0 + SIDE_IN
        box(g, (0, 0, (cb + self.core_top) / 2), (self.hx - SIDE_IN, self.hy - SIDE_IN, (self.core_top - cb) / 2), core, bevel=0.004)
        ext = (self.bx, self.by, self.hz)
        for axis in range(3):
            others = [i for i in range(3) if i != axis]
            for s0 in (-1, 1):
                for s1 in (-1, 1):
                    if (axis, s0, s1) in skip:
                        continue
                    cc = [0.0, 0.0, self.cz]
                    cc[others[0]] += s0 * (ext[others[0]] - r)
                    cc[others[1]] += s1 * (ext[others[1]] - r)
                    base = self.cz if axis == 2 else 0.0
                    lo, hi = base - ext[axis] + 2 * c - 0.004, base + ext[axis] - 2 * c + 0.004
                    for a, b in segments(lo, hi, gaps.get((axis, s0, s1), ())):
                        p = list(cc)
                        p[axis] = (a + b) / 2
                        half = [r, r, r]
                        half[axis] = (b - a) / 2
                        box(g, p, half, "gun", bevel=min(0.009, r * 0.3))
                        if b - a < 0.06:
                            continue
                        for o, s in ((others[0], s0), (others[1], s1)):
                            if o == 2 and s < 0:
                                continue
                            nrm = [0.0, 0.0, 0.0]
                            nrm[o] = s
                            q0 = list(p)
                            q0[o] += s * r
                            q1 = list(q0)
                            q0[axis] = a + 0.025
                            q1[axis] = b - 0.025
                            rivets(g, q0, q1, max(2, int(round((b - a) / rivet_step)) + 1), nrm, r=0.0055)
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    p = (sx * (self.bx - c), sy * (self.by - c), self.cz + sz * (self.hz - c))
                    box(g, p, (c, c, c), "gun", bevel=min(0.016, c * 0.35))
                    for nrm in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                        if nrm[2] < 0:
                            continue
                        hexnut(g, Vector(p) + Vector(nrm) * c, nrm, c * 0.2, 0.0035, "bolt")

    def panel(self, g, axis, sign, u0, u1, v0, v1, n_u=None, n_v=None):
        """A riveted border plate round a recessed field, between u0..u1 and v0..v1 on a wall."""
        uc, vc, hu, hv = (u0 + u1) / 2, (v0 + v1) / 2, (u1 - u0) / 2, (v1 - v0) / 2
        self.slab(g, axis, sign, uc, vc, hu, hv, 0.0, 0.012, "gun_dark", bevel=0.004)
        self.slab(g, axis, sign, uc, vc, hu - 0.02, hv - 0.02, 0.0, 0.0145, "panel", bevel=0.003)
        nrm = [0, 0, 0]
        nrm[axis] = sign
        e = 0.01
        n_u = n_u or max(2, int(round(2 * hu / 0.07)) + 1)
        n_v = n_v or max(2, int(round(2 * hv / 0.07)) + 1)
        rivets(g, self.point(axis, sign, u0 + e, v0 + e, 0.012), self.point(axis, sign, u1 - e, v0 + e, 0.012), n_u, nrm, r=0.0048)
        rivets(g, self.point(axis, sign, u0 + e, v1 - e, 0.012), self.point(axis, sign, u1 - e, v1 - e, 0.012), n_u, nrm, r=0.0048)
        if n_v > 2:
            sv = (v1 - v0 - 2 * e) / (n_v - 1)
            rivets(g, self.point(axis, sign, u0 + e, v0 + e + sv, 0.012), self.point(axis, sign, u0 + e, v1 - e - sv, 0.012), n_v - 2, nrm, r=0.0048)
            rivets(g, self.point(axis, sign, u1 - e, v0 + e + sv, 0.012), self.point(axis, sign, u1 - e, v1 - e - sv, 0.012), n_v - 2, nrm, r=0.0048)

    def lid_rect(self, gap=0.004):
        return self.bx - 2 * self.rail - gap, self.by - 2 * self.rail - gap

    def inset_lid(self, g, sink=0.0, thick=0.034, border=0.03):
        """The lid inside the top rails: a hull slab with a raised gunmetal border; a dark seam round it. Returns its top."""
        ix, iy = self.lid_rect()
        top = self.zt - 0.002 - sink
        box(g, (0, 0, self.core_top + 0.0006), (self.bx - 2 * self.rail + 0.002, self.by - 2 * self.rail + 0.002, 0.0008), "hole")
        box(g, (0, 0, top - 0.006 - thick / 2), (ix, iy, thick / 2), "panel", bevel=0.004)
        for s in (-1, 1):
            box(g, (0, s * (iy - border / 2), top - 0.006), (ix, border / 2, 0.006), "gun_dark", bevel=0.003)
            box(g, (s * (ix - border / 2), 0, top - 0.006), (border / 2, iy - border, 0.006), "gun_dark", bevel=0.003)
        nx = max(2, int(round(2 * ix / 0.08)) + 1)
        ny = max(2, int(round(2 * iy / 0.08)) + 1)
        for s in (-1, 1):
            rivets(g, (-ix + 0.015, s * (iy - 0.015), top), (ix - 0.015, s * (iy - 0.015), top), nx, (0, 0, 1), r=0.0045)
            rivets(g, (s * (ix - 0.015), -iy + 0.015 + 2 * iy / (ny - 1), top), (s * (ix - 0.015), iy - 0.015 - 2 * iy / (ny - 1), top),
                   max(2, ny - 2), (0, 0, 1), r=0.0045)
        self.lid_field, self.lid_border, self.lid_bw, self.lid_sink = top - 0.006, top, border, sink
        return top - 0.006

    def hinge(self, g_lid, g_body, x, half_len=0.045, r=0.011):
        """A copper hinge at the lid's back edge: knuckles on the hinge line, a leaf on the lid's border, a leaf on the
        back rail (on its top, or on its inner face when the lid is sunk). Returns the hinge line's height."""
        ix, iy = self.lid_rect()
        sunk = self.lid_sink > 0
        hz = self.lid_border + r if sunk else self.zt - 0.001 - r
        hy = iy + 0.002
        cylinder(g_body, (x - half_len, hy, hz), (x - half_len / 3, hy, hz), r, "copper", segs=16)
        cylinder(g_body, (x + half_len / 3, hy, hz), (x + half_len, hy, hz), r, "copper", segs=16)
        cylinder(g_lid, (x - half_len / 3 + 0.002, hy, hz), (x + half_len / 3 - 0.002, hy, hz), r, "copper", segs=16)
        cylinder(g_body, (x - half_len - 0.004, hy, hz), (x + half_len + 0.004, hy, hz), r * 0.4, "steel", segs=10)
        bw = self.lid_bw
        box(g_lid, (x, hy - bw / 2 - 0.004, self.lid_border + 0.002), (half_len / 3 - 0.002, bw / 2, 0.002), "copper", bevel=0.001)
        dome(g_lid, (x, hy - bw / 2 - 0.006, self.lid_border + 0.004), (0, 0, 1), 0.0038, "bolt", height=0.0016)
        if sunk:
            box(g_body, (x, iy + 0.007, hz), (half_len, 0.003, r + 0.008), "copper", bevel=0.0012)
        else:
            box(g_body, (x, hy + r + 0.012, self.zt - 0.0025), (half_len, 0.014, 0.0025), "copper", bevel=0.0012)
            for dx in (-half_len * 0.6, half_len * 0.6):
                dome(g_body, (x + dx, hy + r + 0.014, self.zt), (0, 0, 1), 0.004, "bolt", height=0.0018)
        return hz

    def latch(self, g_lid, g_body, x, drop=0.07, w=0.03, lamp=True):
        """A copper latch: a hasp on the lid's front border, down through a gap in the front top rail to a lock body
        on the front wall with a cyan lamp and a keyhole."""
        ix, iy = self.lid_rect()
        r, bw = self.rail, self.lid_bw
        top = self.lid_border + 0.004
        box(g_lid, (x, -iy + bw / 2 - 0.002, top - 0.002), (w, bw / 2 + 0.002, 0.002), "copper", bevel=0.001)
        z0 = self.zt - 2 * r + 0.004
        box(g_lid, (x, -self.by + r, (z0 + top) / 2), (w, r, (top - z0) / 2), "copper", bevel=0.003)
        hexnut(g_lid, (x, -iy + bw / 2, top), (0, 0, 1), 0.006, 0.0016, "bolt")
        zl = self.zt - 2 * r - drop / 2
        self.slab(g_body, 1, -1, x, zl, w + 0.008, drop / 2, 0.0, 0.0165, "gun_dark", bevel=0.004)
        self.slab(g_lid, 1, -1, x, self.zt - 2 * r - 0.012, w - 0.004, 0.012, 0.0, 0.0175, "copper", bevel=0.002)
        if lamp:
            self.slab(g_body, 1, -1, x, zl - drop * 0.12, w * 0.55, 0.006, 0.0165, 0.0178, "cyan")
        self.slab(g_body, 1, -1, x, zl + drop * 0.18, 0.003, 0.008, 0.0165, 0.0172, "hole")

    def latch_gap(self, x, w=0.03):
        return (x - w - 0.003, x + w + 0.003)

    def grip(self, g, axis, sign, u, v, hu, hv):
        """A recessed hand grip: a dark pocket with a copper bar across it on two brackets."""
        self.slab(g, axis, sign, u, v, hu + 0.012, hv + 0.012, 0.0, 0.006, "gun_dark", bevel=0.003)
        self.slab(g, axis, sign, u, v, hu, hv, 0.006, 0.0068, "hole")
        a = Vector(self.point(axis, sign, u - hu + 0.012, v, 0.012))
        b = Vector(self.point(axis, sign, u + hu - 0.012, v, 0.012))
        cylinder(g, a, b, 0.0055, "copper", segs=12)
        for p in (a, b):
            self.slab(g, axis, sign, *[p[i] for i in uv_axes(axis)], 0.006, 0.008, 0.006, 0.016, "copper", bevel=0.002)


def finish(name, body, lid, hinge, mats):
    """The model's root with its Body and Lid nodes (the lid's origin on its hinge line)."""
    root = bl.make_empty(name)
    bl.make_object("Body", body, mats, root)
    moved = Geo()
    moved.extend(lid, Matrix.Translation(-Vector(hinge)))
    bl.make_object("Lid", moved, mats, root, tuple(hinge))
    return root


# =====================================================================================================
# Small Chest: 3 x 2 x 3, one port on +X
# =====================================================================================================

def build_small(mats):
    hx, hy, hz = 0.375, 0.375, 0.375
    cs = Case(hx, hy, -hz, hz, rail=0.026, corner=0.042)
    body, lid = Geo(), Geo()
    cs.frame(body, gaps={(0, -1, 1): [cs.latch_gap(0.0)]})
    # front: two panels either side of the latch, a label plate with a cyan pip
    zc = cs.cz - 0.03
    for s in (-1, 1):
        cs.panel(body, 1, -1, s * 0.07 if s > 0 else -0.29, 0.29 if s > 0 else -0.07, -hz + 0.075, hz - 0.11)
    cs.slab(body, 1, -1, 0, zc - 0.05, 0.04, 0.025, 0.0, 0.012, "gun", bevel=0.003)
    cs.slab(body, 1, -1, 0, zc - 0.05, 0.032, 0.017, 0.012, 0.0135, "screen")
    cs.slab(body, 1, -1, -0.012, zc - 0.05, 0.012, 0.004, 0.0135, 0.0145, "cyan")
    # lid: border, a pull slot toward the front, a copper hinge each side
    top = cs.inset_lid(lid)
    box(lid, (0, -0.17, top + 0.0005), (0.07, 0.012, 0.0008), "hole")
    box(lid, (0, -0.17, top - 0.0002), (0.082, 0.022, 0.0012), "gun_dark", bevel=0.001)
    for x in (-0.18, 0.18):
        hz_line = cs.hinge(lid, body, x, half_len=0.042)
    cs.latch(lid, body, 0.0, drop=0.07)
    # back: one wide panel; -X: a panel under a grip
    cs.panel(body, 1, 1, -0.29, 0.29, -hz + 0.075, hz - 0.11)
    cs.panel(body, 0, -1, -0.29, 0.29, -hz + 0.075, 0.13)
    cs.grip(body, 0, -1, 0.0, 0.23, 0.11, 0.03)
    # the +X wall round the port: strips either side of the plate, a strip over it
    for s in (-1, 1):
        cs.slab(body, 0, 1, s * 0.288, 0, 0.02, 0.24, 0.0, 0.012, "gun_dark", bevel=0.003)
    cs.slab(body, 0, 1, 0, 0.29, 0.25, 0.018, 0.0, 0.012, "gun_dark", bevel=0.003)
    ix, iy = cs.lid_rect()
    root = finish("SmallChest", body, lid, (0, iy + 0.002, hz_line), mats)
    ports = Ports(mats)
    ports.mount(root, "IOPort", (hx, 0.0, 0.0), (1, 0, 0))
    return root


# =====================================================================================================
# Large Chest: 5 x 3 x 4, ports on both ends and the back
# =====================================================================================================

def build_large(mats):
    hx, hy, hz = 0.625, 0.5, 0.375
    cs = Case(hx, hy, -hz, hz, rail=0.028, corner=0.046)
    body, lid = Geo(), Geo()
    latches = (-0.33, 0.33)
    cs.frame(body, gaps={(0, -1, 1): [cs.latch_gap(x, 0.034) for x in latches]})
    port_y = -0.125                      # 4 blocks deep: half a block toward the front from the face's middle
    # front: three panels, the middle one a label plate with a cyan status bar
    v0, v1 = -hz + 0.075, hz - 0.12
    cs.panel(body, 1, -1, -0.54, -0.2, v0, v1)
    cs.panel(body, 1, -1, 0.2, 0.54, v0, v1)
    cs.slab(body, 1, -1, 0, (v0 + v1) / 2, 0.15, (v1 - v0) / 2, 0.0, 0.012, "gun_dark", bevel=0.004)
    cs.slab(body, 1, -1, 0, (v0 + v1) / 2 + 0.07, 0.12, 0.06, 0.0, 0.0145, "gun", bevel=0.003)
    cs.slab(body, 1, -1, 0, (v0 + v1) / 2 + 0.07, 0.105, 0.045, 0.0145, 0.0155, "screen")
    for k in range(5):
        cs.slab(body, 1, -1, -0.076 + k * 0.038, (v0 + v1) / 2 + 0.07, 0.014, 0.024, 0.0155, 0.0165,
                "cyan" if k < 3 else "cyan_off")
    for k in range(4):
        cs.slab(body, 1, -1, 0, (v0 + v1) / 2 - 0.04 - k * 0.03, 0.1, 0.006, 0.012, 0.0128, "hole")
    # ends: a port each, a narrow panel toward the back
    for s in (-1, 1):
        cs.panel(body, 0, s, 0.17, 0.43, -hz + 0.075, hz - 0.075, n_u=4)
        cs.slab(body, 0, s, -0.415, 0, 0.028, hz - 0.1, 0.0, 0.012, "gun_dark", bevel=0.003)
    # back: panels either side of the port
    for s in (-1, 1):
        cs.panel(body, 1, 1, s * 0.3 if s > 0 else -0.55, 0.55 if s > 0 else -0.3, -hz + 0.075, hz - 0.12)
    # lid: border, two stiffening ribs, a cyan light bar along its front edge, three hinges
    top = cs.inset_lid(lid)
    ix, iy = cs.lid_rect()
    for x in (-0.2, 0.2):
        box(lid, (x, 0.02, top - 0.0005), (0.022, iy - 0.06, 0.0055), "gun", bevel=0.003)
        rivets(lid, (x, -iy + 0.08, top + 0.005), (x, iy - 0.04, top + 0.005), 7, (0, 0, 1), r=0.004)
    box(lid, (0, -iy + 0.065, top - 0.001), (0.15, 0.014, 0.004), "gun_dark", bevel=0.002)
    box(lid, (0, -iy + 0.065, top + 0.0026), (0.135, 0.006, 0.0008), "cyan")
    for x in (-0.42, 0.0, 0.42):
        hz_line = cs.hinge(lid, body, x, half_len=0.05)
    for x in latches:
        cs.latch(lid, body, x, drop=0.075, w=0.034)
    root = finish("LargeChest", body, lid, (0, iy + 0.002, hz_line), mats)
    ports = Ports(mats)
    ports.mount(root, "IOPortRight", (hx, port_y, 0.0), (1, 0, 0))
    ports.mount(root, "IOPortLeft", (-hx, port_y, 0.0), (-1, 0, 0))
    ports.mount(root, "IOPortBack", (0.0, hy, 0.0), (0, 1, 0))
    return root


# =====================================================================================================
# Cargo Chest: 8 x 6 x 6, the 100-slot bin with ten ports
# =====================================================================================================

CARGO_PORTS_FRONT = (-0.625, -0.125, 0.375)    # x; the back's are turned half round (-x), so the layout turns with it
CARGO_PORTS_END = (-0.375, 0.125)              # y; 6 blocks deep: half a block toward the front


def build_cargo(mats):
    hx, hy, hz = 1.0, 0.75, 0.75
    floor = -hz
    base_top = floor + 0.115                   # the skid base; the ports' plates start 1 cm above it
    port_z = floor + 0.375                     # the standard port height
    cs = Case(hx, hy, base_top, hz, rail=0.036, corner=0.05)
    body, lid = Geo(), Geo()
    latches = (-0.55, 0.55)
    # no bottom rails: the skid base is the bottom frame, and the ports' plates come down to just above it
    cs.frame(body, gaps={(0, -1, 1): [cs.latch_gap(x, 0.04) for x in latches]},
             skip={(0, -1, -1), (0, 1, -1), (1, -1, -1), (1, 1, -1)}, rivet_step=0.1, core_sink=0.08)
    # --- the skid base: perimeter beams with fork pockets front and back, steel shoes at the corners, runners under
    bx, by = cs.bx, cs.by
    bh = (base_top - floor) / 2
    bz = floor + bh
    for s in (-1, 1):
        box(body, (0, s * (by - 0.04), bz), (bx, 0.04, bh), "gun_dark", bevel=0.006)
        box(body, (s * (bx - 0.04), 0, bz), (0.04, by - 0.08, bh), "gun_dark", bevel=0.006)
    for x in (-0.33, 0.33):
        box(body, (x, 0, bz - 0.01), (0.06, by - 0.08, bh - 0.01), "gun_dark", bevel=0.005)      # runners under
    box(body, (0, 0, base_top - 0.012), (bx - 0.06, by - 0.06, 0.012), "gun_dark")             # the deck on them
    for s in (-1, 1):
        for x in (-0.6, 0.6):
            # a fork pocket: a dark opening framed by a steel lip
            slab(body, 1, s, s * by, x, bz + 0.002, 0.17, 0.034, -0.08, 0.0004, "hole")
            for k in (-1, 1):
                slab(body, 1, s, s * by, x, bz + 0.002 + k * 0.041, 0.188, 0.007, -0.002, 0.0, "steel", bevel=0.002)
                slab(body, 1, s, s * by, x + k * 0.18, bz + 0.002, 0.008, 0.034, -0.002, 0.0, "steel", bevel=0.002)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(body, (sx * (bx - 0.07), sy * (by - 0.07), floor + 0.008), (0.07, 0.07, 0.008), "steel", bevel=0.003)
            for nrm in ((sx, 0, 0), (0, sy, 0)):
                p = Vector((sx * (bx - 0.07), sy * (by - 0.07), bz + 0.01)) + Vector(nrm) * 0.07
                hexnut(body, p - Vector(nrm) * 0.0035 + Vector(nrm) * 0.0, nrm, 0.008, 0.0035, "bolt")
    # --- walls: a belt rail over the ports, vertical ribs above it, the hazard band under the top rails
    belt_z = port_z + 0.25 + 0.03
    belt_h = 0.022
    band_z0, band_z1 = cs.zt - 2 * cs.rail - 0.11, cs.zt - 2 * cs.rail - 0.006
    rib_z0, rib_z1 = belt_z + belt_h + 0.01, band_z0 - 0.012
    for axis, sign, half in ((1, -1, bx), (1, 1, bx), (0, -1, by), (0, 1, by)):
        span = half - 2 * cs.corner
        cs.slab(body, axis, sign, 0, belt_z, span, belt_h, 0.0, 0.0165, "gun", bevel=0.005)
        rivets(body, cs.point(axis, sign, -span + 0.03, belt_z, 0.0165), cs.point(axis, sign, span - 0.03, belt_z, 0.0165),
               int(span / 0.05) + 1, at(axis, sign, 0, 0), r=0.0045)
        cs.slab(body, axis, sign, 0, (band_z0 + band_z1) / 2, span, (band_z1 - band_z0) / 2, 0.0, 0.01, "hazard", bevel=0.003)
        cs.slab(body, axis, sign, 0, band_z0 - 0.004, span, 0.006, 0.0, 0.0125, "gun_dark", bevel=0.002)
        cs.slab(body, axis, sign, 0, band_z1 + 0.003, span, 0.005, 0.0, 0.0125, "gun_dark", bevel=0.002)
        n_ribs = int(round(2 * span / 0.25))
        screen = axis == 1 and sign < 0
        for k in range(n_ribs + 1):
            u = -span + 0.02 + k * (2 * span - 0.04) / n_ribs
            if screen and abs(u) < 0.3:
                continue
            cs.slab(body, axis, sign, u, (rib_z0 + rib_z1) / 2, 0.017, (rib_z1 - rib_z0) / 2, 0.0, 0.0165, "gun_dark", bevel=0.006)
        # the lower wall between the ports: plain hull, with a vent plate where a face has room left over
    # the front's cyan slot screen between the ribs
    sv = (rib_z0 + rib_z1) / 2 + 0.03
    cs.slab(body, 1, -1, 0, sv, 0.26, 0.15, 0.0, 0.012, "gun_dark", bevel=0.005)
    cs.slab(body, 1, -1, 0, sv + 0.025, 0.21, 0.09, 0.0, 0.0145, "gun", bevel=0.004)
    cs.slab(body, 1, -1, 0, sv + 0.025, 0.195, 0.076, 0.0145, 0.0155, "screen")
    for k in range(10):
        cs.slab(body, 1, -1, -0.171 + k * 0.038, sv + 0.025, 0.013, 0.055, 0.0155, 0.0165, "cyan" if k < 6 else "cyan_off")
    for k in range(4):
        cs.slab(body, 1, -1, -0.14 + k * 0.093, sv - 0.1, 0.035, 0.008, 0.012, 0.0128, "hole")
    for dx in (-0.245, 0.245):
        for dz in (-0.13, 0.13):
            hexnut(body, cs.point(1, -1, dx, sv + dz, 0.012), (0, -1, 0), 0.006, 0.0035, "bolt")
    # vent plates in the free corner of the front and back port rows
    for sign in (-1, 1):
        u = 0.79 if sign < 0 else -0.79
        cs.slab(body, 1, sign, u, port_z, 0.1, 0.19, 0.0, 0.012, "gun_dark", bevel=0.004)
        for k in range(7):
            cs.slab(body, 1, sign, u, port_z - 0.15 + k * 0.05, 0.075, 0.009, 0.012, 0.0128, "hole")
        for dz in (-0.175, 0.175):
            dome(body, cs.point(1, sign, u, port_z + dz, 0.012), (0, sign, 0), 0.0045, "bolt")
    # --- the lid, sunk into the top rails: ribs across it, two lift handles, four lifting lugs, hinges at the back
    top = cs.inset_lid(lid, sink=0.036, thick=0.04, border=0.04)
    ix, iy = cs.lid_rect()
    for x in (-0.6, -0.2, 0.2, 0.6):
        box(lid, (x, 0, top + 0.004), (0.025, iy - 0.05, 0.008), "gun", bevel=0.004)
    for x in (-0.4, 0.4):
        y = -0.12
        for dx in (-0.09, 0.09):
            box(lid, (x + dx, y, top + 0.012), (0.014, 0.022, 0.012), "copper", bevel=0.003)
        cylinder(lid, (x - 0.1, y, top + 0.024), (x + 0.1, y, top + 0.024), 0.009, "steel", segs=14)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (ix - 0.06), sy * (iy - 0.06)
            box(lid, (x, y, top + 0.004), (0.035, 0.035, 0.004), "copper", bevel=0.003)
            eye = Geo()
            ring(eye, -0.004, 0.004, 0.009, 0.019, "copper", segs=20)
            lid.extend(eye, Matrix.Translation((x, y, top + 0.027)) @ Matrix.Rotation(math.pi / 2, 4, "Y" if sx * sy > 0 else "X"))
    box(lid, (0, -iy + 0.075, top + 0.001), (0.3, 0.012, 0.004), "gun_dark", bevel=0.002)
    box(lid, (0, -iy + 0.075, top + 0.0054), (0.28, 0.006, 0.0008), "cyan")
    for x in (-0.65, 0.0, 0.65):
        hz_line = cs.hinge(lid, body, x, half_len=0.06, r=0.013)
    for x in latches:
        cs.latch(lid, body, x, drop=0.05, w=0.04, lamp=False)
    hinge_line = (0, iy + 0.002, hz_line)
    root = finish("CargoChest", body, lid, hinge_line, mats)
    ports = Ports(mats)
    for k, x in enumerate(CARGO_PORTS_FRONT):
        ports.mount(root, f"IOPortFront{k}", (x, -hy, port_z), (0, -1, 0))
        ports.mount(root, f"IOPortBack{k}", (-x, hy, port_z), (0, 1, 0))
    for k, y in enumerate(CARGO_PORTS_END):
        ports.mount(root, f"IOPortRight{k}", (hx, y, port_z), (1, 0, 0))
        ports.mount(root, f"IOPortLeft{k}", (-hx, y, port_z), (-1, 0, 0))
    return root


# =====================================================================================================
# Scrap Chest: 3 x 2 x 3 Settler Patchwork crate
# =====================================================================================================

def build_scrap(mats):
    rng = random.Random(17)
    H = 0.375                                  # half width and depth
    z0, z1 = -0.25, 0.25
    lid_z = 0.075                              # the lid's bottom edge
    post_out, plank_out = 0.006, 0.010         # how far inside the box the posts' and planks' faces sit
    body, lid = Geo(), Geo()
    woods = ("wood", "wood_b", "wood_c", "wood", "wood_b")
    pc = H - post_out - 0.028                  # posts: 5.6 cm square
    box(body, (0, 0, (z0 + lid_z) / 2), (H - 0.03, H - 0.03, (lid_z - z0) / 2 - 0.004), "inside")
    box(lid, (0, 0, (lid_z + z1) / 2), (H - 0.03, H - 0.03, (z1 - lid_z) / 2 - 0.016), "inside")
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(body, (sx * pc, sy * pc, (z0 + lid_z - 0.003) / 2), (0.028, 0.028, (lid_z - 0.003 - z0) / 2), "wood_dark", bevel=0.005)
            box(lid, (sx * pc, sy * pc, (lid_z + 0.003 + z1 - 0.022) / 2), (0.028, 0.028, (z1 - 0.022 - lid_z - 0.003) / 2),
                "wood_dark", bevel=0.005)

    def planks(g, axis, sign, v0, v1, n, seed_mat):
        plane = sign * (H - plank_out)
        span = pc - 0.026
        hv_all = (v1 - v0) / n
        for k in range(n):
            vc = v0 + hv_all * (k + 0.5)
            hv = hv_all / 2 - 0.004 - rng.uniform(0, 0.004)
            sink = rng.uniform(0, 0.003)
            slab(g, axis, sign, plane, rng.uniform(-0.004, 0.004), vc + rng.uniform(-0.003, 0.003), span, hv,
                 -0.02 - sink, -sink, woods[(seed_mat + k) % len(woods)], bevel=0.004, angle=math.radians(rng.uniform(-1.2, 1.2)))

    for axis, sign, k in ((1, -1, 0), (1, 1, 1), (0, -1, 2), (0, 1, 3)):
        planks(body, axis, sign, z0 + 0.012, lid_z - 0.004, 3, k)
        planks(lid, axis, sign, lid_z + 0.004, z1 - 0.024, 1, k + 2)
    box(body, (0, 0, z0 + 0.01), (pc, pc, 0.01), "wood_dark")
    # the lid's top: five boards along X, of mixed woods and thicknesses
    n = 5
    w = (2 * (H - plank_out)) / n
    plank_top = z1 - 0.0075                    # the straps (4 mm) and their bolt heads (3.5 mm) above it
    for k in range(n):
        y = -(H - plank_out) + w * (k + 0.5)
        t = rng.uniform(0.0, 0.003)
        box(lid, (rng.uniform(-0.003, 0.003), y, plank_top - t - 0.0095), (H - plank_out - 0.002, w / 2 - 0.004, 0.0095),
            woods[(k + 1) % len(woods)], bevel=0.004, rot=Matrix.Rotation(math.radians(rng.uniform(-0.8, 0.8)), 3, "Z"))
    top = plank_top + 0.004
    # straps: two over the lid and down the front and back, one rusty, one patched with zinc; nails and bolts on them
    def bolt_on(g, p, nrm, kind):
        if kind == 0:
            hexnut(g, p, nrm, 0.0065, 0.0035, "zinc")
        elif kind == 1:
            dome(g, p, nrm, 0.0065, "rust", height=0.0035)
        elif kind == 2:
            box(g, Vector(p) + Vector(nrm) * 0.0016, tuple(0.0016 if a else 0.0055 for a in nrm), "iron", bevel=0.001)
        else:
            dome(g, p, nrm, 0.0045, "iron", height=0.0025)

    for i, x in enumerate((-0.2, 0.2)):
        mat = "rust" if i == 0 else "iron"
        sw = 0.022
        box(lid, (x, 0, top - 0.002), (sw, H - plank_out + 0.004, 0.002), mat, bevel=0.0008)  # over the top
        for s in (-1, 1):
            yy = s * (H - plank_out + 0.002)
            box(lid, (x, yy, (lid_z + 0.006 + top) / 2), (sw, 0.002, (top - lid_z - 0.006) / 2), mat, bevel=0.0008)
            body_mat = mat if not (i == 1 and s < 0) else "zinc"
            box(body, (x, yy, (z0 + 0.02 + lid_z - 0.008) / 2), (sw, 0.002, (lid_z - 0.008 - z0 - 0.02) / 2), body_mat, bevel=0.0008)
            for z in (lid_z + 0.04, -0.02, -0.2):
                g = lid if z > lid_z else body
                bolt_on(g, (x + rng.uniform(-0.006, 0.006), s * (H - plank_out + 0.004), z), (0, s, 0), rng.randrange(4))
            if i == 1 and s < 0:          # the zinc patch's overlap onto the rusty strap above it
                box(body, (x, yy + s * 0.0008, lid_z - 0.03), (sw + 0.004, 0.0025, 0.02), "zinc", bevel=0.0008)
                for dx in (-0.013, 0.013):
                    bolt_on(body, (x + dx, s * (H - plank_out + 0.0049), lid_z - 0.03), (0, s, 0), 0)
        for y in (-0.25, 0.0, 0.25):
            bolt_on(lid, (x + rng.uniform(-0.005, 0.005), y, top), (0, 0, 1), rng.randrange(4))
    # zinc corner brackets on the posts, top and bottom of the body and on the lid's top corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            for z, g in ((z0 + 0.045, body), (lid_z - 0.04, body), (lid_z + 0.06, lid)):
                for axis, sign in ((0, sx), (1, sy)):
                    u = (sy if axis == 0 else sx) * (pc - 0.006)
                    slab(g, axis, sign, sign * (H - post_out), u, z, 0.034, 0.03, 0.0, 0.0022, "zinc" if (sx + sy + int(z * 10)) % 2 else "rust", bevel=0.001)
                    bolt_on(g, at(axis, sign * (H - post_out + 0.0022), u, z + 0.012), at(axis, sign, 0, 0), rng.randrange(4))
                    bolt_on(g, at(axis, sign * (H - post_out + 0.0022), u, z - 0.012), at(axis, sign, 0, 0), rng.randrange(4))
    # a bolted zinc patch over a split plank on +X, a rusty one on the back
    for axis, sign, u, v, hu, hv, ang, mat in ((0, 1, 0.08, -0.13, 0.09, 0.06, 7, "zinc"), (1, 1, -0.05, -0.04, 0.07, 0.045, -5, "rust")):
        plane = sign * (H - plank_out)
        slab(body, axis, sign, plane, u, v, hu, hv, 0.0, 0.0022, mat, bevel=0.001, angle=math.radians(ang))
        e0, e1, nrm = face_frame(axis, sign, math.radians(ang))
        c = Vector(at(axis, plane + sign * 0.0022, u, v))
        for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            bolt_on(body, c + e0 * a * (hu - 0.012) + e1 * b * (hv - 0.012), nrm, rng.randrange(4))
    # hand holds cut in the sides' top planks
    for sign in (-1, 1):
        slab(body, 0, sign, sign * (H - plank_out), 0, lid_z - 0.058, 0.07, 0.014, 0.0, 0.0006, "inside")
    # the hasp: a zinc plate hung from the lid's front over a staple on the body, a bent pin through it
    fy = -(H - plank_out)
    slab(lid, 1, -1, fy, 0, lid_z + 0.012, 0.026, 0.05, 0.0, 0.0025, "zinc", bevel=0.001)
    slab(lid, 1, -1, fy, 0, lid_z + 0.055, 0.014, 0.006, 0.0025, 0.0035, "rust", bevel=0.001)     # its hinge pin
    for dx in (-0.013, 0.013):
        bolt_on(lid, (dx, fy - 0.0025, lid_z + 0.04), (0, -1, 0), 1)
    slab(lid, 1, -1, fy, 0, lid_z - 0.015, 0.006, 0.012, 0.0025, 0.0028, "hole")                  # its slot
    slab(body, 1, -1, fy, 0, lid_z - 0.015, 0.016, 0.02, 0.0, 0.0018, "iron", bevel=0.0008)        # the staple's plate
    for dx in (-0.004, 0.004):
        slab(body, 1, -1, fy, dx, lid_z - 0.015, 0.0015, 0.009, 0.0018, 0.0072, "iron")            # its loop
    slab(body, 1, -1, fy, 0, lid_z - 0.015, 0.0055, 0.0015, 0.0058, 0.0072, "iron")
    cylinder(body, (-0.022, fy - 0.0068, lid_z - 0.017), (0.018, fy - 0.0068, lid_z - 0.013), 0.0018, "steel", segs=8)  # the pin
    cylinder(body, (0.018, fy - 0.0068, lid_z - 0.013), (0.024, fy - 0.0068, lid_z - 0.03), 0.0018, "steel", segs=8)
    # strap hinges at the back: a knuckle on the lid line where each strap meets it
    by = H - plank_out
    for x in (-0.2, 0.2):
        cylinder(body, (x - 0.034, by + 0.003, lid_z), (x + 0.034, by + 0.003, lid_z), 0.0055, "iron", segs=12)
    return finish("ScrapChest", body, lid, (0, by + 0.003, lid_z), mats)


# =====================================================================================================
# Ruin Chest: 4 x 3 x 3 Old Stone
# =====================================================================================================

def glyph(g, axis, sign, plane, u, v, s, kind, mat="crack", d=0.0012):
    """A carved glyph about s across on a face (as the ruins' props: ring, steps, fork, bars, chevron)."""
    w = max(0.008, s * 0.12)

    def bar(bu, bv, length, ang):
        slab(g, axis, sign, plane, u + bu, v + bv, length / 2, w / 2, -0.001, d, mat, angle=math.radians(ang))

    if kind == "ring":
        on_face(g, axis, sign, plane, u, v, -0.001, flat_ring(s * 0.4 - w / 2, s * 0.4 + w / 2, d + 0.001, mat))
        slab(g, axis, sign, plane, u, v, w * 0.8, w * 0.8, -0.001, d, mat)
    elif kind == "steps":
        bar(-s * 0.3, -s * 0.1, s * 0.4, 90)
        bar(-s * 0.15, s * 0.1, s * 0.3, 0)
        bar(0.0, s * 0.25, s * 0.3, 90)
        bar(s * 0.15, s * 0.4, s * 0.3, 0)
    elif kind == "fork":
        bar(0, -s * 0.12, s * 0.7, 90)
        bar(-s * 0.15, s * 0.32, s * 0.38, 55)
        bar(s * 0.15, s * 0.32, s * 0.38, 125)
    elif kind == "bars":
        for i, length in enumerate((0.9, 0.65, 0.4)):
            bar(0, s * (0.3 - 0.3 * i), s * length, 0)
    else:  # chevron
        bar(-s * 0.15, 0.05 * s, s * 0.5, 55)
        bar(s * 0.15, 0.05 * s, s * 0.5, -55)
        slab(g, axis, sign, plane, u, v - s * 0.3, w, w, -0.001, d, mat)


def crack_line(g, axis, sign, plane, u, v, length, rng, ang0, mat="crack"):
    """A wandering hairline crack of a few segments."""
    a = math.radians(ang0)
    for _ in range(4):
        seg = length / 4 * rng.uniform(0.7, 1.3)
        a += math.radians(rng.uniform(-35, 35))
        du, dv = math.cos(a) * seg, math.sin(a) * seg
        slab(g, axis, sign, plane, u + du / 2, v + dv / 2, seg / 2 + 0.002, 0.0022, -0.001, 0.0008, mat, angle=a)
        u, v = u + du, v + dv


def build_ruin(mats):
    rng = random.Random(29)
    hx, hy, hz = 0.5, 0.375, 0.375
    floor = -hz
    body, lid = Geo(), Geo()
    out = 0.003                                     # the outermost stone faces sit this far inside the box
    plinth_top = floor + 0.07
    body_top = 0.12
    lid_top = 0.285
    # plinth, body, corner pilasters (weathered: their vertices nudged inward)
    s0 = len(body.v)
    box(body, (0, 0, floor + 0.035), (hx - out, hy - out, 0.035), "stone_b", bevel=0.014)
    box(body, (0, 0, (plinth_top + body_top) / 2), (hx - 0.045, hy - 0.045, (body_top - plinth_top) / 2), "stone", bevel=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(body, (sx * (hx - out - 0.052), sy * (hy - out - 0.052), (plinth_top + body_top) / 2 - 0.002),
                (0.052, 0.052, (body_top - plinth_top) / 2 - 0.002), "stone_b", bevel=0.012)
    jitter(body, s0, 0.004, (hx, hy), floor, 3)
    # carved frames and glyphs: front, sides; joint lines on the back
    fy = -(hy - 0.045)
    vc = (plinth_top + body_top) / 2
    for v in (body_top - 0.035, plinth_top + 0.035):
        slab(body, 1, -1, fy, 0, v, 0.32, 0.0045, -0.001, 0.0012, "crack")
    for u in (-0.32, 0.32):
        slab(body, 1, -1, fy, u, vc, 0.0045, (body_top - plinth_top) / 2 - 0.035, -0.001, 0.0012, "crack")
    glyph(body, 1, -1, fy, 0, vc, 0.15, "ring")
    glyph(body, 1, -1, fy, -0.19, vc - 0.02, 0.12, "steps")
    glyph(body, 1, -1, fy, 0.19, vc, 0.12, "fork")
    for sign, kind in ((-1, "bars"), (1, "chevron")):
        px = sign * (hx - 0.045)
        for v in (body_top - 0.04, plinth_top + 0.04):
            slab(body, 0, sign, px, 0, v, 0.17, 0.0045, -0.001, 0.0012, "crack")
        for u in (-0.17, 0.17):
            slab(body, 0, sign, px, u, vc, 0.0045, (body_top - plinth_top) / 2 - 0.04, -0.001, 0.0012, "crack")
        glyph(body, 0, sign, px, 0, vc, 0.13, kind)
    by = hy - 0.045
    for v in (vc - 0.07, vc + 0.07):
        slab(body, 1, 1, by, 0, v, 0.34, 0.003, -0.001, 0.0008, "crack")
    for u, v in ((-0.12, vc + 0.14), (0.2, vc + 0.14), (0.05, vc), (-0.27, vc), (-0.1, vc - 0.14), (0.25, vc - 0.14)):
        slab(body, 1, 1, by, u, v, 0.003, 0.068, -0.001, 0.0008, "crack")
    # cracks: a few wandering hairlines across the faces and pilasters
    crack_line(body, 1, -1, -(hy - out), -(hx - out - 0.05), body_top - 0.04, 0.14, rng, -70)
    crack_line(body, 1, -1, fy, 0.3, body_top - 0.01, 0.16, rng, -110)
    crack_line(body, 0, 1, hx - 0.045, -0.12, plinth_top + 0.01, 0.2, rng, 60)
    crack_line(body, 1, 1, by, 0.3, plinth_top + 0.02, 0.15, rng, 100)
    # --- the heavy lid: a slab with a dark band, a hipped top, a round seal with a faint cyan ring glyph
    s0 = len(lid.v)
    box(lid, (0, 0, (body_top + lid_top) / 2), (hx - out, hy - out, (lid_top - body_top) / 2), "stone", bevel=0.016)
    w0, d0 = hx - out - 0.035, hy - out - 0.035
    w1, d1 = w0 - 0.15, d0 - 0.15
    zt = hz - 0.014
    hull_of(lid, [(sx * w0, sy * d0, lid_top - 0.004) for sx in (-1, 1) for sy in (-1, 1)] +
            [(sx * w1, sy * d1, zt) for sx in (-1, 1) for sy in (-1, 1)], "stone_b")
    jitter(lid, s0, 0.004, (hx, hy), -1.0, 5)
    zb = body_top + 0.035
    for s in (-1, 1):
        box(lid, (0, s * (hy - 0.0015 - 0.0005), zb), (hx - 0.012, 0.0015, 0.016), "stone_dark")
        box(lid, (s * (hx - 0.0015 - 0.0005), 0, zb), (0.0015, hy - 0.012, 0.016), "stone_dark")
    cylinder(lid, (0, 0, zt - 0.004), (0, 0, hz - 0.004), 0.1, "stone_dark", segs=36)
    rr = Geo()
    ring(rr, 0.0, 0.0015, 0.058, 0.07, "rune", segs=36)
    lid.extend(rr, Matrix.Translation((0, 0, hz - 0.004)))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        box(lid, (0.085 * math.cos(a), 0.085 * math.sin(a), hz - 0.0035), (0.009, 0.004, 0.0008), "rune",
            rot=Matrix.Rotation(a, 3, "Z"))
    box(lid, (0, 0, hz - 0.0035), (0.012, 0.012, 0.0008), "rune", rot=Matrix.Rotation(math.pi / 4, 3, "Z"))
    for k, (ax, sg, u, v, ln, ang) in enumerate(((1, -1, -0.32, lid_top - 0.03, 0.12, -80), (0, -1, 0.15, lid_top - 0.02, 0.1, -95),
                                                  (1, 1, 0.25, lid_top - 0.04, 0.13, -70))):
        plane = sg * ((hy, hx)[ax == 0] - out)
        crack_line(lid, ax, sg, plane, u, v, ln, rng, ang)
    # moss: cushions along the lid's top edges and on the hip, a tuft on the plinth
    clamp = (hx - 0.001, hy - 0.001, hz - 0.001)
    # moss: clumps gathered on the lid's top edges (thickest at the back left), on the hip, and a tuft on the plinth
    sites = ((-0.34, -0.355, 1, 0), (0.46, -0.24, 0, 1), (0.2, 0.355, 1, 0), (-0.46, 0.2, 0, 1), (-0.36, 0.33, 1, 0))
    for k, (x, y, ax, ay) in enumerate(sites):
        for j in range(4):
            t = rng.uniform(-1, 1)
            c = (x + ax * t * 0.1 + rng.uniform(-0.012, 0.012), y + ay * t * 0.1 + rng.uniform(-0.012, 0.012), lid_top - 0.004)
            r = (rng.uniform(0.03, 0.05) * (1 + ax), rng.uniform(0.03, 0.05) * (1 + ay), rng.uniform(0.014, 0.022))
            # keep each clump on the lid (a clump squashed flat against the box's side shows its inside)
            c = (max(min(c[0], hx - out - 1.1 * r[0]), -(hx - out - 1.1 * r[0])),
                 max(min(c[1], hy - out - 1.1 * r[1]), -(hy - out - 1.1 * r[1])), c[2])
            ellipsoid(lid, c, r, "moss", floor=lid_top - 0.03, clamp=clamp, seed=10 * k + j)
    for j, (x, y) in enumerate(((-0.33, 0.27), (-0.26, 0.29), (-0.4, 0.22))):
        ellipsoid(lid, (x, y, lid_top + 0.024), (0.045, 0.035, 0.02), "moss", floor=lid_top + 0.004, clamp=clamp, seed=70 + j)
    for j, (x, y) in enumerate(((-0.45, -0.33), (-0.41, -0.345), (0.4, 0.34))):
        ellipsoid(body, (x, y, plinth_top), (0.035, 0.025, 0.02), "moss", floor=plinth_top - 0.01, clamp=clamp, seed=80 + j)
    return finish("RuinChest", body, lid, (0, hy - out, body_top), mats)


# =====================================================================================================
# Main
# =====================================================================================================

CHESTS = {
    # id: (name, blocks X x Y x Z, builder, family)
    "chest": ("Scrap Chest", (3, 2, 3), build_scrap, "Settler Patchwork"),
    "small_chest": ("Small Chest", (3, 3, 3), build_small, "Salvager Tech"),
    "large_chest": ("Large Chest", (5, 3, 4), build_large, "Salvager Tech"),
    "cargo_chest": ("Cargo Chest", (8, 6, 6), build_cargo, "Salvager Tech"),
    "ruin_chest": ("Ruin Chest", (4, 3, 3), build_ruin, "Old Stone"),
}


def half_size(cid):
    x, y, z = CHESTS[cid][1]
    return (x * B / 2, z * B / 2, y * B / 2)          # Blender: X wide, Y deep, Z up


def icon_ortho(half):
    """The ortho scale that frames a box of these half sizes as the Gas Container's icon frames its own."""
    v = Vector(ICON_VIEW).normalized()
    right = (-v).cross(Vector((0, 0, 1))).normalized()
    up = right.cross(-v)

    def extent(h):
        pts = [Vector((sx * h[0], sy * h[1], sz * h[2])) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        return max(max(p.dot(right) for p in pts) - min(p.dot(right) for p in pts),
                   max(p.dot(up) for p in pts) - min(p.dot(up) for p in pts))

    return GAS_ORTHO * extent(half) / extent(GAS_BOX)


def blender_bounds(root):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    bpy.context.view_layer.update()
    for ob in bbe.mesh_tree(root):
        if ob.type != "MESH":
            continue
        m = ob.matrix_world
        for v in ob.data.vertices:
            p = m @ v.co
            lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
    return lo, hi


def glb_info(path):
    """Bounds (glTF axes, with each node's transform), triangle count and image count, read back from the .glb."""
    data = path.read_bytes()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glb"
    ln, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + ln])
    lo, hi, tris = np.full(3, np.inf), np.full(3, -np.inf), 0

    def local(node):
        t = Matrix.Translation(node.get("translation", (0, 0, 0)))
        q = node.get("rotation", (0, 0, 0, 1))
        r = Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4()
        s = node.get("scale", (1, 1, 1))
        return t @ r @ Matrix.Diagonal((*s, 1.0))

    def walk(i, parent):
        nonlocal lo, hi, tris
        node = doc["nodes"][i]
        m = parent @ local(node)
        if "mesh" in node:
            for prim in doc["meshes"][node["mesh"]]["primitives"]:
                acc = doc["accessors"][prim["attributes"]["POSITION"]]
                a, b = acc["min"], acc["max"]
                for c in ((x, y, z) for x in (a[0], b[0]) for y in (a[1], b[1]) for z in (a[2], b[2])):
                    p = np.array(m @ Vector(c))
                    lo, hi = np.minimum(lo, p), np.maximum(hi, p)
                tris += doc["accessors"][prim["indices"]]["count"] // 3
        for c in node.get("children", ()):
            walk(c, m)

    for i in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        walk(i, Matrix.Identity(4))
    images = [im for im in doc.get("images", []) if "bufferView" in im]
    return {"min": lo, "max": hi, "size": hi - lo, "tris": tris, "images": len(images), "nodes": len(doc["nodes"]),
            "meshes": len(doc["meshes"]), "materials": len(doc.get("materials", [])), "kib": len(data) / 1024}


def build_all(only=None):
    bbe.reset_scene()
    mats = make_materials()
    built = {}
    for cid, (name, blocks, builder, family) in CHESTS.items():
        if only and cid not in only:
            continue
        root = builder(mats)
        h = half_size(cid)
        lo, hi = blender_bounds(root)
        over = max(max(lo[i] * -1 - h[i], hi[i] - h[i]) for i in range(3))
        path = MESHES / f"{cid}.glb"
        bbe.export(root, path, animations=False)
        info = glb_info(path)
        icon = ICONS / f"{cid}_icon.png"
        bbe.render_icon(bbe.mesh_tree(root), icon, view=ICON_VIEW, ortho=icon_ortho(h), target=(0, 0, 0))
        built[cid] = root
        print(f"{cid}: {name}, {blocks[0]} x {blocks[1]} x {blocks[2]} blocks; box half {h}; "
              f"blender bounds {tuple(round(x, 4) for x in lo)} .. {tuple(round(x, 4) for x in hi)} "
              f"(worst overhang {over * 1000:+.1f} mm)")
        print(f"  wrote {path.relative_to(REPO)} ({info['kib']:.0f} KiB, {info['tris']} triangles, {info['nodes']} nodes, "
              f"{info['meshes']} meshes, {info['materials']} materials, {info['images']} images) glTF bounds "
              f"{np.round(info['min'], 4).tolist()} .. {np.round(info['max'], 4).tolist()} size {np.round(info['size'], 4).tolist()}")
        print(f"  wrote {icon.relative_to(REPO)}")
    return built, mats


def main():
    only = None
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))
    built, mats = build_all(only)
    if "--render" in sys.argv:
        import render_chests
        render_chests.render_all(built, mats, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
