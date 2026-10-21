"""Build the Feeding Trough and its icon (husbandry PR 2; the concept Chase approved on 2026-10-20 in the Mob list
thread: tools/grazer/husbandry_concepts.py).

Salvager Tech, like the Fabricator: a gunmetal frame with copper caps and a cyan window, round a slatted wooden tub.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/parts/feeding_trough.glb`` - the Feeding Trough, 6 x 2 x 3 blocks (1.5 m long along X, 0.75 m
  deep along Y, 0.5 m tall, centred on the origin, Z up): four splayed legs with copper caps, rails and a cross brace,
  a tub of sloping planks between two end boards, and a copper plate with a cyan window on the front (-Y).

  The feed is four nodes, ``Fill1`` (a thin layer of hay) to ``Fill4`` (heaped, with carrots on top), one showing at
  a time. One glTF clip, ``fill`` (1 s, scrubbed by the ``fill`` readout of ``scripts/feeding_trough.lua``), picks
  it: nothing at 0, ``Fill1`` up to a quarter, ``Fill4`` from three quarters.

* ``mods/vanilla/textures/items/feeding_trough_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256
  on a clear background, three-quarter from the front.

    pip install bpy==5.2.2 numpy pillow
    python tools/husbandry/build_trough.py                  # writes the .glb and the icon
    python tools/husbandry/build_trough.py --render OUT_DIR # also renders review images

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
import numpy as np
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
for sub in ("", "../gas_container", "../scrap_seat", "../rocks", "../soil"):
    sys.path.insert(0, str((HERE / sub).resolve()))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)
import build_scrap_seat as ss  # noqa: E402  (planks with their grain along them, and their UVs)
import seat_textures as st  # noqa: E402

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "parts"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, dome, hexnut = bl.Geo, bl.box, bl.cylinder, bl.dome, bl.hexnut
V = Vector

L, D, H = 1.5, 0.75, 0.5        # 6 x 3 x 2 blocks
FLOOR = -H / 2
TOP = FLOOR + H - 0.04           # the rails' top
BOT = FLOOR + 0.16               # the tub's bottom board
SLOPE = 0.62                     # how far the tub's sides lean out per metre up
LEVELS = 4                       # fill steps
SHOWN = 3                        # the level the renders and the icon show


# =====================================================================================================
# Materials
# =====================================================================================================

def wood(seed, base, light, dark):
    """The seat's pallet wood in another palette (warmer and darker: a feed tub, not a pallet)."""
    keep = st.WOOD, st.WOOD_LIGHT, st.WOOD_DARK
    st.WOOD, st.WOOD_LIGHT, st.WOOD_DARK = st.srgb(base), st.srgb(light), st.srgb(dark)
    try:
        return st.pallet_wood(seed)
    finally:
        st.WOOD, st.WOOD_LIGHT, st.WOOD_DARK = keep


def hay_surface(seed):
    """Loose straw: long pale strands every which way over a darker bed."""
    yy, xx = np.mgrid[0:lt.SIZE, 0:lt.SIZE] / lt.SIZE
    a = lt.fnoise(seed, 60.0, aniso=(12.0, 1.0))
    b = lt.fnoise(seed + 1, 60.0, aniso=(1.0, 12.0))
    strands = np.clip(np.maximum(a, b) * 1.6 - 0.35, 0, 1)
    base, light, dark = st.srgb("#c9a456"), st.srgb("#ecd38a"), st.srgb("#8a6a32")
    col = base[None, None] + (light - base)[None, None] * strands[..., None]
    shade = np.clip(lt.fbm(seed + 2, 6.0) * 1.2, 0, 1)
    col = col + (dark - col) * (0.45 * (1 - strands) * shade)[..., None]
    return np.clip(col, 0, 1), 0.8 * strands, np.full((lt.SIZE, lt.SIZE), 0.85), np.zeros((lt.SIZE, lt.SIZE))


def make_materials():
    m = gc.make_materials()
    m.update({
        "wood": lt.textured("ft_wood", gc.half_res(wood(611, "#9a7448", "#b48f62", "#6e5232")), normal_strength=0.8),
        "wood_dark": lt.textured("ft_wood_dark", gc.half_res(wood(613, "#7c5c3a", "#94724c", "#56402a")), normal_strength=0.8),
        "hay": lt.textured("ft_hay", gc.half_res(hay_surface(617)), normal_strength=1.2),
        "straw": pt.plain("ft_straw", "#e3c46e", 0.7, 0.0),
        "straw_dark": pt.plain("ft_straw_dark", "#b08a42", 0.75, 0.0),
        "carrot": pt.plain("ft_carrot", "#e07a2a", 0.5, 0.0),
        "carrot_top": pt.plain("ft_carrot_top", "#cf6a22", 0.55, 0.0),
        "leaf": pt.plain("ft_leaf", "#5f8f3a", 0.6, 0.0),
        "cyan": pt.plain("ft_cyan", "#3fc6d0", 0.3, 0.0, emission="#2de8d2", strength=2.5),
    })
    return m


# =====================================================================================================
# Geometry
# =====================================================================================================

def plank(g, c, half, mat, seed, tilt=None):
    """A plank with its grain along X, turned by `tilt` (3x3) about its middle."""
    tmp = Geo()
    ss.wood_box(tmp, (0, 0, 0), half, 0, seed, mat)
    m = tilt if tilt is not None else Matrix.Identity(3)
    g.add([tuple(m @ V(p) + V(c)) for p in tmp.v], tmp.f, mat, tmp.uv)


def blob(g, c, r, scale, mat, segs=12):
    """A squashed sphere (a tuft of hay, a carrot's shoulder)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(6, segs // 2), radius=r)
    for v in bm.verts:
        v.co = V((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2])) + V(c)
    verts, faces = bl._from_bmesh(bm)
    g.add(verts, faces, mat)


def beam(g, a, b, half, mat, bevel=0.008):
    """A square bar from a to b, `half` its half width."""
    a, b = V(a), V(b)
    d = b - a
    rot = V((0, 0, 1)).rotation_difference(d).to_matrix()
    box(g, (a + b) / 2, (half, half, d.length / 2), mat, bevel=bevel, rot=rot)


def build_frame():
    g = Geo()
    for sx in (-1, 1):
        for sy in (-1, 1):
            foot = V((sx * (L / 2 - 0.035), sy * (D / 2 - 0.035), FLOOR))
            head = V((sx * (L / 2 - 0.08), sy * (D / 2 - 0.06), TOP))
            beam(g, foot + V((0, 0, 0.02)), head, 0.025, "gun")
            box(g, foot + V((0, 0, 0.015)), (0.035, 0.035, 0.015), "gun_dark", bevel=0.006)
            box(g, head + V((0, 0, 0.022)), (0.036, 0.036, 0.017), "copper", bevel=0.008)
            hexnut(g, head + V((0, 0, 0.039)), (0, 0, 1), 0.012, 0.006, "bolt")
        # the cross bar low at each end, and the rails along the top of the long sides
        box(g, (sx * (L / 2 - 0.12), 0, FLOOR + 0.1), (0.02, D / 2 - 0.08, 0.02), "gun", bevel=0.006)
    box(g, (0, 0, FLOOR + 0.1), (L / 2 - 0.12, 0.02, 0.02), "gun", bevel=0.006)
    for sy in (-1, 1):
        box(g, (0, sy * (D / 2 - 0.06), TOP + 0.02), (L / 2 - 0.08, 0.025, 0.025), "gun", bevel=0.008)
        for k in range(9):
            x = -L / 2 + 0.16 + k * (L - 0.32) / 8
            dome(g, (x, sy * (D / 2 - 0.06), TOP + 0.045), (0, 0, 1), 0.0065, "bolt")
    # the copper plate with a cyan window on the front rail's face
    y = -(D / 2 - 0.035)
    box(g, (0, y - 0.006, TOP - 0.035), (0.11, 0.006, 0.04), "copper", bevel=0.004)
    box(g, (0, y - 0.0105, TOP - 0.035), (0.07, 0.0015, 0.013), "cyan")
    for dx in (-0.095, 0.095):
        for dz in (-0.025, 0.025):
            dome(g, (dx, y - 0.012, TOP - 0.035 + dz), (0, -1, 0), 0.005, "bolt")
    return g


def build_tub():
    g = Geo()
    for sy in (-1, 1):
        tilt = Matrix.Rotation(sy * math.radians(-32), 3, "X")
        for k in range(4):
            z = BOT + 0.03 + k * 0.075
            y = sy * (0.12 + (z - BOT) * SLOPE)
            plank(g, (0, y, z), (L / 2 - 0.11, 0.015, 0.043), "wood" if k % 2 == 0 else "wood_dark", 620 + k + 4 * (sy > 0), tilt)
    plank(g, (0, 0, BOT), (L / 2 - 0.11, 0.13, 0.015), "wood_dark", 630)
    for sx in (-1, 1):
        # end boards: the sloping tub's outline, cut from a plank turned across
        x = sx * (L / 2 - 0.12)
        top = BOT + 0.3
        half_top, half_bot = 0.12 + 0.3 * SLOPE + 0.03, 0.14
        verts = [(x - 0.015, -half_bot, BOT - 0.015), (x - 0.015, half_bot, BOT - 0.015), (x - 0.015, half_top, top), (x - 0.015, -half_top, top),
                 (x + 0.015, -half_bot, BOT - 0.015), (x + 0.015, half_bot, BOT - 0.015), (x + 0.015, half_top, top), (x + 0.015, -half_top, top)]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        g.add(verts, faces, "wood")
        for z in (BOT + 0.06, BOT + 0.22):
            hexnut(g, (x + sx * 0.015, 0, z), (sx, 0, 0), 0.01, 0.005, "bolt")
    return g


def build_fill(level):
    """The feed at `level` of LEVELS: hay up the sloping sides, carrots on the higher levels."""
    g = Geo()
    f = level / LEVELS
    z = BOT + 0.02 + f * 0.24
    half = 0.1 + (z - BOT) * SLOPE
    rng = np.random.default_rng(700 + level)
    # a bed that fills the tub's width at this height, then tufts over it
    blob(g, (0, 0, z - 0.03), 1.0, (L / 2 - 0.16, half - 0.01, 0.035 + 0.01 * level), "hay", segs=20)
    for _ in range(14 + 6 * level):
        x = rng.uniform(-L / 2 + 0.2, L / 2 - 0.2)
        y = rng.uniform(-0.75, 0.75) * (half - 0.08)
        blob(g, (x, y, z - 0.005), 0.07, (1.5 * rng.uniform(0.8, 1.2), 0.9, 0.45), "hay", segs=10)
    # loose straws lying every which way over the bed, so it reads as hay
    for _ in range(60 + 30 * level):
        x = rng.uniform(-L / 2 + 0.16, L / 2 - 0.16)
        y = rng.uniform(-0.9, 0.9) * (half - 0.05)
        a, tilt, n = rng.uniform(0, math.pi), rng.uniform(-0.35, 0.35), rng.uniform(0.05, 0.09)
        d = V((math.cos(a), math.sin(a), tilt)).normalized()
        c = V((x, y, z + 0.012 + rng.uniform(0, 0.02)))
        cylinder(g, c - d * n, c + d * n, 0.0035, "straw" if rng.uniform() < 0.7 else "straw_dark", segs=4)
    for i in range(max(0, level * 3 - 4)):
        x = rng.uniform(-L / 2 + 0.25, L / 2 - 0.25)
        y = rng.uniform(-0.5, 0.5) * (half - 0.1)
        c = V((x, y, z + 0.04))
        d = V((math.cos(i * 2.1 + level), math.sin(i * 2.1 + level), 0.12)).normalized()
        cylinder(g, c - d * 0.09, c + d * 0.09, 0.028, "carrot", segs=12, r1=0.007)
        blob(g, c - d * 0.09, 0.028, (1, 1, 1), "carrot_top", segs=12)
        for k in range(3):
            leaf = (d * -1 + V((0.25 * math.cos(k * 2.1), 0.25 * math.sin(k * 2.1), 0.5))).normalized()
            cylinder(g, c - d * 0.095, c - d * 0.095 + leaf * 0.09, 0.007, "leaf", segs=6, r1=0.002)
    return g


def build_trough(mats):
    rig = {"root": bl.make_empty("FeedingTrough")}
    root = rig["root"]
    ss.make_object("Frame", build_frame(), mats, root)
    ss.make_object("Tub", build_tub(), mats, root)
    rig["fills"] = []
    for k in range(1, LEVELS + 1):
        node = bl.make_empty(f"Fill{k}", root)
        ss.make_object(f"Fill{k}Feed", build_fill(k), mats, node)
        rig["fills"].append((k, node))
    set_fill(rig, SHOWN / LEVELS)
    return rig


OFF = 0.001                   # scale of a level that is not showing


def level_of(fill):
    return 0 if fill <= 0 else min(LEVELS, math.ceil(fill * LEVELS - 1e-6))


def set_fill(rig, fill):
    for k, ob in rig["fills"]:
        ob.scale = (1.0, 1.0, 1.0) if level_of(fill) == k else (OFF, OFF, OFF)


def bake(rig):
    """The scrubbed fill clip: each level keyed on for its quarter of the second and off either side of it."""
    fps = bbe.FPS
    step = fps // LEVELS
    for k, ob in rig["fills"]:
        ob.animation_data_create()
        act = bpy.data.actions.new(f"fill_{ob.name}")
        ob.animation_data.action = act
        frames = sorted({0, (k - 1) * step, (k - 1) * step + 1, k * step, min(fps, k * step + 1), fps})
        for f in frames:
            ob.scale = (1.0, 1.0, 1.0) if level_of(f / fps) == k else (OFF, OFF, OFF)
            ob.keyframe_insert("scale", frame=f)
        lt.linear_keys(act)
        bl.push_track(ob, act, "fill")
        ob.animation_data.use_nla = False
    set_fill(rig, SHOWN / LEVELS)


def strip_animation(rig):
    for _, ob in rig["fills"]:
        ob.animation_data.action = None


ICON_VIEW = (1.0, -2.3, 1.45)
ICON_ORTHO = 1.75


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build_trough(mats)
    bake(rig)
    bbe.export(rig["root"], MESHES / "feeding_trough.glb", animations=True)
    bpy.context.scene.frame_set(0)
    strip_animation(rig)
    set_fill(rig, SHOWN / LEVELS)
    bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / "feeding_trough_icon.png", view=ICON_VIEW, ortho=ICON_ORTHO, target=(0, 0, -0.04))
    for p in (MESHES / "feeding_trough.glb", ICONS / "feeding_trough_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_husbandry
        render_husbandry.render_trough(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
