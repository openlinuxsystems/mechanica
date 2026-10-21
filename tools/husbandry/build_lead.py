"""Build the Lead and its icon (husbandry PR 2; the concept Chase approved on 2026-10-20 in the Mob list thread:
tools/grazer/husbandry_concepts.py).

A coil of twisted rope tied off on a yellow grip, with a gunmetal snap hook and a copper swivel on its free end.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/lead.glb`` - the Lead as it lies on the ground and as a player holds it: the grip runs
  along X through the origin (the palm), the coil hangs below it in the XZ plane, the hook hangs from the coil.
* ``mods/vanilla/textures/items/lead_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a clear
  background.
* ``rope_texture()`` - the twisted rope's colour, also drawn by the game for the rope between a player and the animal
  they lead (``game/scripts/Bots/BotLeads.cs`` makes its own from the same numbers: ROPE, ROPE_DARK).

    pip install bpy==5.2.2 numpy pillow
    python tools/husbandry/build_lead.py                  # writes the .glb and the icon
    python tools/husbandry/build_lead.py --render OUT_DIR # also renders review images

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import bpy  # noqa: F401,I001  (bpy first: it makes bmesh and mathutils importable)
import numpy as np
from mathutils import Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
for sub in ("", "../gas_container", "../scrap_seat", "../rocks", "../soil"):
    sys.path.insert(0, str((HERE / sub).resolve()))
import build_gas_container as gc  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)
import build_scrap_seat as ss  # noqa: E402  (objects that keep per-face UVs)
import seat_textures as st  # noqa: E402

bbe, bl, lt, pt = gc.bbe, gc.bl, gc.lt, gc.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

Geo, box, cylinder, dome, hexnut = bl.Geo, bl.box, bl.cylinder, bl.dome, bl.hexnut
V = Vector

ROPE, ROPE_DARK = "#c8a46a", "#8a6a3c"
ROPE_R = 0.011              # the rope's radius, m
TWIST = 0.05                # metres of rope per turn of its three strands
COIL_R = 0.12               # the coil's radius
LOOPS = 4
FLOOR = -0.47               # the lowest point (the hook), for the renders' ground


# =====================================================================================================
# Materials
# =====================================================================================================

def rope_texture():
    """Three strands twisting round the rope: u runs along it (one tile a TWIST), v round it."""
    n = lt.SIZE
    yy, xx = np.mgrid[0:n, 0:n] / n
    phase = (xx + 3 * yy) % 1.0
    ridge = np.abs(np.sin(np.pi * phase)) ** 0.6
    fibre = lt.fnoise(5, 180.0, aniso=(1.0, 6.0))
    base, dark = st.srgb(ROPE), st.srgb(ROPE_DARK)
    col = dark[None, None] + (base - dark)[None, None] * (0.25 + 0.75 * ridge)[..., None]
    col = col * (0.92 + 0.12 * fibre[..., None])
    return np.clip(col, 0, 1), 0.8 * ridge + 0.2 * fibre, np.full((n, n), 0.85), np.zeros((n, n))


def make_materials():
    m = gc.make_materials()
    m.update({
        "rope": lt.textured("ld_rope", gc.half_res(rope_texture()), normal_strength=2.0),
        "grip": pt.plain("ld_grip", "#e0a81c", 0.45, 0.0),
    })
    return m


# =====================================================================================================
# Geometry
# =====================================================================================================

def rope(g, points, r=ROPE_R, segs=10):
    """A rope through `points`, with UVs along it (u) and round it (v) so the strands twist along its length."""
    pts = [V(p) for p in points]
    tangents = [(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(len(pts))]
    u, _ = bl.frame_of(tangents[0])
    verts, along = [], [0.0]
    for a, b in zip(pts, pts[1:]):
        along.append(along[-1] + (b - a).length)
    for p, t in zip(pts, tangents):
        u = (u - t * u.dot(t)).normalized()
        w = t.cross(u)
        for k in range(segs):
            a = 2 * math.pi * k / segs
            verts.append(tuple(p + (u * math.cos(a) + w * math.sin(a)) * r))
    faces, uvs = [], []
    for i in range(len(pts) - 1):
        for k in range(segs):
            k1 = k + 1
            faces.append((i * segs + k, i * segs + k1 % segs, (i + 1) * segs + k1 % segs, (i + 1) * segs + k))
            s0, s1 = along[i] / TWIST, along[i + 1] / TWIST
            uvs.append([(s0, k / segs), (s0, k1 / segs), (s1, k1 / segs), (s1, k / segs)])
    base = len(g.v)
    g.add(verts, faces, "rope", uvs)
    for end, i in ((0, 0), (1, len(pts) - 1)):
        ring = [i * segs + k for k in range(segs)]
        cap = tuple(reversed(ring)) if end == 0 else tuple(ring)
        g.add([verts[k] for k in cap], [tuple(range(segs))], "rope")
    return base


def coil_points():
    """The coil hanging under the grip in the XZ plane: LOOPS turns, each a little off the last, from the grip round
    and back up, then the tail that drops out of the bottom to the hook."""
    c = V((0, 0, -0.035 - COIL_R))
    pts = []
    steps = 40
    for i in range(int((LOOPS + 0.5) * steps) + 1):
        t = i / steps
        a = math.pi / 2 + 2 * math.pi * t          # start at the top, under the grip; end at the bottom
        r = COIL_R * (1 - 0.04 * t)
        y = 0.012 * (t - LOOPS / 2) + 0.004 * math.sin(3 * a)
        pts.append(c + V((math.cos(a) * r, y, math.sin(a) * r * 1.08)))
    top = pts[0]
    # the tail: straight down from the bottom of the coil to the hook
    end = pts[-1]
    tail = [end + V((0, 0.004 * k, -0.012 * k)) for k in range(1, 6)]
    return [V((0, 0, -0.02)), top] + pts[1:] + tail


def hook(g, at):
    """A swivel snap hook hanging from `at`: a copper swivel eye, a gunmetal hook with its gate."""
    at = V(at)
    cylinder(g, at + V((0, 0, 0.01)), at - V((0, 0, 0.02)), 0.016, "copper", segs=14)
    eye = [at - V((0, 0, 0.02)) + V((0.014 * math.cos(t), 0, -0.016 + 0.016 * math.sin(t))) for t in
           [2 * math.pi * k / 16 for k in range(17)]]
    bl.sweep(g, eye, 0.004, "copper", segs=8)
    top = at - V((0, 0, 0.05))
    cylinder(g, top + V((0, 0, 0.012)), top - V((0, 0, 0.01)), 0.009, "gun_dark", segs=12)
    # the hook: a J from the swivel down and round, the gate closing it
    j = [top + V((0, 0, -0.01 - 0.06 * k / 8)) for k in range(9)]
    bot = j[-1]
    j += [bot + V((0.022 - 0.022 * math.cos(a), 0, -0.022 * math.sin(a))) for a in [math.pi * k / 10 for k in range(1, 11)]]
    j += [j[-1] + V((0, 0, 0.018))]
    bl.sweep(g, j, 0.0065, "gun", segs=10)
    # the spring gate: from the hook's tip back up to the shank, closing it
    tip, shank = j[-1] + V((-0.003, 0, 0.004)), top + V((0.004, 0, -0.03))
    d = shank - tip
    box(g, (tip + shank) / 2, (0.0035, 0.0045, d.length / 2), "steel", bevel=0.0015,
        rot=V((0, 0, 1)).rotation_difference(d).to_matrix())
    return bot


def build_lead():
    g = Geo()
    # the grip: yellow, along X through the palm, with copper ferrules at both ends; the rope is tied round its middle
    cylinder(g, (-0.055, 0, 0), (0.055, 0, 0), 0.019, "grip", segs=20)
    for s in (-1, 1):
        cylinder(g, (s * 0.055, 0, 0), (s * 0.066, 0, 0), 0.021, "copper", segs=20)
        dome(g, (s * 0.066, 0, 0), (s, 0, 0), 0.016, "copper", segs=12)
    for k in range(3):
        x = -0.012 + 0.012 * k
        wrap = [V((x + 0.002 * math.sin(a), 0.022 * math.cos(a), 0.022 * math.sin(a))) for a in [2 * math.pi * i / 16 for i in range(17)]]
        rope(g, wrap, r=0.006, segs=8)
    pts = coil_points()
    rope(g, pts)
    hook(g, pts[-1])
    return g


def build(mats):
    root = bl.make_empty("Lead")
    ss.make_object("Lead_mesh", build_lead(), mats, root)
    return {"root": root}


ICON_VIEW = (0.6, -2.2, 0.35)
ICON_ORTHO = 0.52


def main():
    bbe.reset_scene()
    mats = make_materials()
    rig = build(mats)
    bbe.export(rig["root"], MESHES / "lead.glb", animations=False)
    bbe.render_icon(bbe.mesh_tree(rig["root"]), ICONS / "lead_icon.png", view=ICON_VIEW, ortho=ICON_ORTHO, target=(0, 0, -0.22))
    for p in (MESHES / "lead.glb", ICONS / "lead_icon.png"):
        print(f"wrote {p.relative_to(REPO)} ({p.stat().st_size // 1024} KiB)")
    if "--render" in sys.argv:
        import render_husbandry
        render_husbandry.render_lead(rig, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
