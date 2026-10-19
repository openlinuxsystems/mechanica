"""Build the Bloom Pods: the glowing loot pods scattered over the land, broken with the Sledgehammer for their loot.

Chase approved the design on 2026-10-19 (the "Bloom Pod" in the world map thread): a heavy octagonal plinth with clawed
feet; four fixed dark corner pillars, each with a lens and a glowing strip; four armoured petals hinged on the plinth that
close between the pillars, each with a tip that folds in over a glowing crystal held in a dark cradle; and a clawed cap
with the emblem (a bolt head in an octagon ring) that drops onto the folded tips. 1.0 m across and 0.95 m tall.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/world/bloom_pod_green.glb``, ``bloom_pod_purple.glb`` and ``bloom_pod_gold.glb`` - one pod per
  rarity (Common, Epic, Legendary loot), the same model in the rarity's colour. The origin is the ground point under
  the middle of the plinth; Blender +Z (the game's +Y) is up and the first petal faces -Y.
  The moving pieces are their own nodes: ``Petal0``..``Petal3`` (each with its ``Tip``), ``Pillar0``..``Pillar3``,
  ``Cap``, ``Crystal`` and ``Shard0``..``Shard9`` (hidden until the break). glTF clips (see docs/world-generation.md,
  ``mesh.clips``):

  - ``settle`` (1.6 s): from open (petals laid back, tips flat, the cap floating above) the petals swing shut with a
    small overshoot, the tips fold in and the cap spins down and locks. Played when the pod comes into view.
  - ``idle`` (2.4 s, looped by the game): the crystal sways and bobs, the tips breathe.
  - ``hit`` (0.5 s): the petals jolt open and spring back, the cap hops.
  - ``break`` (1.6 s): the crystal bursts into shards, and the petals, pillars and cap fly off and land round the
    plinth, where they stay until the game shrinks the wreck away.

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/bloom_pod/build_bloom_pod.py                  # writes the three .glb files
    python tools/bloom_pod/build_bloom_pod.py --render OUT_DIR # also renders review shots into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)
import bmesh
from mathutils import Euler, Matrix, Vector

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "gas_engine"))
import build_gas_engine as ge  # noqa: E402  (its imports put the bearing_engine and lift helpers on the path)

bbe, bl, lt, pt = ge.bbe, ge.bl, ge.lt, ge.pt
REPO = HERE.parents[1]
MESHES = REPO / "mods" / "vanilla" / "meshes" / "world"
FPS = bbe.FPS
OFF = ge.OFF
Geo, box, cylinder = bl.Geo, bl.box, bl.cylinder

# ---- Bloom Pod (metres; the ground point at the origin, +Z up, the first petal facing -Y) ----------------------
BASE_TOP = 0.17                  # top of the plinth, where the petals and pillars are hinged
PETAL_R = 0.36                   # hinge line of each petal from the middle
PILLAR_R = 0.47                  # foot of each pillar from the middle (on the diagonals)
LOWER, UPPER, THICK = 0.40, 0.25, 0.14   # petal plate height, tip height, plate thickness
CLOSED = math.radians(-12.0)     # petal tilt when shut (negative leans in)
OPEN = math.radians(58.0)        # petal tilt when open, before the settle
TIP_CLOSED = math.radians(-40.0)  # how far a tip folds in over the crystal
TIP_OPEN = math.radians(10.0)
CRYSTAL_Z = 0.44
CAP_TOP = BASE_TOP + LOWER * math.cos(CLOSED) + UPPER * math.cos(CLOSED + TIP_CLOSED) + 0.04
SHARDS = 10

SETTLE, IDLE, HIT, BREAK = 1.6, 2.4, 0.5, 1.6

RARITIES = {
    # body, deep (armour panels, the rarity band), crystal; the chevrons and strips glow warm white on all three
    "green": ("#4cc72a", "#2a7a16", "#8dff5a"),
    "purple": ("#8a4fe0", "#4b2296", "#d49bff"),
    "gold": ("#f2c230", "#a8790e", "#ffe680"),
}
GLOW = "#ffd9a8"


# =====================================================================================================
# Materials
# =====================================================================================================

def make_materials(rarity):
    body, deep, crystal = RARITIES[rarity]
    return {
        "body": pt.plain(f"pod_body_{rarity}", body, 0.42, 0.05),
        "deep": pt.plain(f"pod_deep_{rarity}", deep, 0.5, 0.1),
        "core": pt.plain(f"pod_core_{rarity}", crystal, 0.25, 0.0, emission=crystal, strength=3.0),
        "glow": pt.plain(f"pod_glow_{rarity}", GLOW, 0.4, 0.0, emission=GLOW, strength=4.0),
        "dark": pt.plain(f"pod_dark_{rarity}", "#1c1d20", 0.45, 0.55),
        "steel": pt.plain(f"pod_steel_{rarity}", "#8d9296", 0.32, 0.9),
        "lens": pt.plain(f"pod_lens_{rarity}", "#a9b3ba", 0.12, 0.3),
    }


# =====================================================================================================
# Geometry helpers
# =====================================================================================================

def prism(g, profile, depth, mat, bevel=0.0, origin=(0, 0, 0)):
    """A slab from a 2D (x, z) outline, `depth` thick along +Y from y = origin.y, its edges bevelled."""
    bm = bmesh.new()
    ox, oy, oz = origin
    a = [bm.verts.new((ox + u, oy, oz + v)) for u, v in profile]
    b = [bm.verts.new((ox + u, oy + depth, oz + v)) for u, v in profile]
    bm.faces.new(a)
    bm.faces.new(list(reversed(b)))
    for i in range(len(profile)):
        j = (i + 1) % len(profile)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    verts, faces = bl._from_bmesh(bm)
    return g.add(verts, faces, mat)


def octagon(g, z0, z1, r, mat, bevel=0.0):
    """A flat octagonal block (flat sides facing the axes), z0 to z1."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=r, radius2=r, depth=z1 - z0)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 8, 3, "Z"))
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, (z0 + z1) / 2))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    verts, faces = bl._from_bmesh(bm)
    return g.add(verts, faces, mat)


def chevron(g, centre, size, mat, depth=0.012, w=0.034):
    """A 'V' on the -Y face of a petal (local x across, z up), its point down: the pod's eye-catching marking."""
    cx, cy, cz = centre
    for side in (-1, 1):
        ang = math.radians(45) * side
        rot = Matrix.Rotation(ang, 3, "Y")
        mid = Vector((cx + side * size * 0.26, cy, cz + size * 0.1))
        box(g, tuple(mid), (w / 2, depth / 2, size * 0.36), mat, rot=rot)


def crystal(g, r, mat):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
    verts, faces = bl._from_bmesh(bm)
    return g.add(verts, faces, mat)


def radial(a, r, z=0.0):
    return Vector((math.sin(a) * r, -math.cos(a) * r, z))


# =====================================================================================================
# Pieces
# =====================================================================================================

def build_base():
    """The plinth: two octagon steps with a band of the rarity colour, clawed feet with lenses, the crystal's cradle."""
    g = Geo()
    octagon(g, 0.0, 0.09, 0.5, "dark", bevel=0.015)
    octagon(g, 0.088, 0.115, 0.47, "deep")
    octagon(g, 0.11, BASE_TOP, 0.43, "dark", bevel=0.012)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        m = Matrix.Rotation(a, 3, "Z")
        foot = Geo()
        prism(foot, [(-0.1, 0), (0.1, 0), (0.06, 0.13), (-0.06, 0.13)], 0.16, "dark", bevel=0.012, origin=(0, -0.56, 0))
        cylinder(foot, (0, -0.568, 0.07), (0, -0.555, 0.07), 0.03, "lens", segs=16)
        g.extend(foot, m)
        # the cradle's prongs
        p = radial(a, 0.17)
        box(g, (p.x, p.y, 0.24), (0.02, 0.02, 0.07), "dark", bevel=0.004)
    # the cradle ring the crystal sits in
    bm = Geo()
    bl.ring(bm, 0.28, 0.32, 0.15, 0.2, "dark", segs=8)
    g.extend(bm, Matrix.Rotation(math.pi / 8, 3, "Z"))
    return g


def build_pillar():
    """A fixed corner pillar about its foot, leaning in with the petals: the frame the petals close into."""
    g = Geo()
    box(g, (0, 0.03, 0.235), (0.075, 0.07, 0.235), "dark", bevel=0.018)
    box(g, (0, 0.03, 0.47), (0.085, 0.08, 0.03), "dark", bevel=0.014)
    box(g, (0, -0.043, 0.2), (0.011, 0.006, 0.11), "glow")
    cylinder(g, (0, -0.04, 0.39), (0, -0.055, 0.39), 0.032, "lens", segs=16)
    for x in (-0.05, 0.05):
        for z in (0.05, 0.47):
            cylinder(g, (x, -0.04, z), (x, -0.052, z), 0.012, "steel", segs=8)
    return g


def build_petal():
    """An armoured petal plate about its hinge: outer face at y = 0 facing -Y, rising +Z."""
    g = Geo()
    prism(g, [(-0.27, 0), (0.27, 0), (0.235, LOWER), (-0.235, LOWER)], THICK, "body", bevel=0.03)
    prism(g, [(-0.2, 0.08), (0.2, 0.08), (0.175, LOWER - 0.06), (-0.175, LOWER - 0.06)], 0.03, "deep", bevel=0.006,
          origin=(0, -0.02, 0))
    chevron(g, (0, -0.026, 0.25), 0.16, "glow")
    chevron(g, (0, -0.026, 0.16), 0.12, "glow")
    for sx in (-1, 1):
        for z in (0.06, LOWER - 0.05):
            cylinder(g, (sx * (0.24 - z * 0.08), 0.0, z), (sx * (0.24 - z * 0.08), -0.008, z), 0.014, "steel", segs=8)
        # the glowing seam along each edge, where the crystal's light shows between petal and pillar
        box(g, (sx * 0.242, THICK / 2, LOWER / 2), (0.006, THICK * 0.3, LOWER / 2 - 0.03), "core")
    cylinder(g, (-0.22, 0.05, 0), (0.22, 0.05, 0), 0.05, "steel", segs=16)            # hinge barrel
    for sx in (-1, 0, 1):
        box(g, (sx * 0.17, 0.05, 0.02), (0.045, 0.06, 0.05), "dark", bevel=0.012)
    return g


def build_tip():
    """The petal's tip about its knuckle, folding in over the crystal."""
    g = Geo()
    prism(g, [(-0.235, 0), (0.235, 0), (0.13, UPPER), (-0.13, UPPER)], THICK, "body", bevel=0.03)
    prism(g, [(-0.17, 0.04), (0.17, 0.04), (0.09, UPPER - 0.05), (-0.09, UPPER - 0.05)], 0.025, "deep", bevel=0.005,
          origin=(0, -0.016, 0))
    box(g, (0, 0.05, 0.0), (0.15, 0.05, 0.025), "dark", bevel=0.012)
    return g


def emblem(g, z, scale=1.0):
    """Our mark on the cap: a bolt head with a glowing cross slot inside a glowing octagon ring."""
    octagon(g, z, z + 0.02, 0.17 * scale, "dark")
    ring = Geo()
    bl.ring(ring, z + 0.015, z + 0.032, 0.12 * scale, 0.15 * scale, "glow", segs=8)
    g.extend(ring, Matrix.Rotation(math.pi / 8, 3, "Z"))
    cylinder(g, (0, 0, z + 0.01), (0, 0, z + 0.042), 0.07 * scale, "deep", segs=6, phase=math.pi / 6)
    for a in (45, 135):
        box(g, (0, 0, z + 0.043), (0.05 * scale, 0.009 * scale, 0.004), "glow", rot=Matrix.Rotation(math.radians(a), 3, "Z"))


def build_cap():
    """The clawed cap about its middle: an octagon with the emblem and four claws reaching down to the pillars."""
    g = Geo()
    octagon(g, -0.04, 0.04, 0.24, "dark", bevel=0.014)
    octagon(g, 0.035, 0.065, 0.2, "deep")
    emblem(g, 0.06, 0.95)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        claw = Geo()
        box(claw, (0, -0.25, -0.06), (0.04, 0.1, 0.03), "dark", bevel=0.012, rot=Matrix.Rotation(math.radians(32), 3, "X"))
        cylinder(claw, (0, -0.3, -0.06), (0, -0.3, -0.035), 0.02, "lens", segs=12)
        g.extend(claw, Matrix.Rotation(a, 3, "Z"))
    return g


def build_crystal(mat="core"):
    g = Geo()
    crystal(g, 0.2, mat)
    return g


def build_shard(r):
    g = Geo()
    crystal(g, r, "core")
    return g


# =====================================================================================================
# Scene and clips
# =====================================================================================================

def build_pod(mats):
    rig = {"petals": [], "tips": [], "pillars": [], "shards": []}
    root = rig["root"] = bl.make_empty("BloomPod")
    bl.make_object("Base", build_base(), mats, root)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        ob = bl.make_object(f"Pillar{k}", build_pillar(), mats, root, radial(a, PILLAR_R, BASE_TOP))
        ob.rotation_mode = "XYZ"
        rig["pillars"].append(ob)
    for k in range(4):
        a = k * math.pi / 2
        petal = bl.make_object(f"Petal{k}", build_petal(), mats, root, radial(a, PETAL_R, BASE_TOP))
        tip = bl.make_object(f"Tip{k}", build_tip(), mats, petal, (0, 0, LOWER))
        petal.rotation_mode = tip.rotation_mode = "XYZ"
        rig["petals"].append(petal)
        rig["tips"].append(tip)
    rig["crystal"] = bl.make_object("Crystal", build_crystal(), mats, root, (0, 0, CRYSTAL_Z))
    rig["crystal"].rotation_mode = "XYZ"
    rig["cap"] = bl.make_object("Cap", build_cap(), mats, root, (0, 0, CAP_TOP))
    rig["cap"].rotation_mode = "XYZ"
    rng = random.Random(11)
    for i in range(SHARDS):
        ob = bl.make_object(f"Shard{i}", build_shard(rng.uniform(0.035, 0.065)), mats, root, (0, 0, CRYSTAL_Z))
        ob.rotation_mode = "XYZ"
        rig["shards"].append(ob)
    rest(rig)
    return rig


def moving(rig):
    return [*rig["petals"], *rig["tips"], *rig["pillars"], rig["crystal"], rig["cap"], *rig["shards"]]


def rest(rig):
    """Shut and still: every moving piece at its settled pose, the shards hidden in the crystal."""
    for k, ob in enumerate(rig["petals"]):
        a = k * math.pi / 2
        ob.location = radial(a, PETAL_R, BASE_TOP)
        ob.rotation_euler = Euler((CLOSED, 0, a))
    for ob in rig["tips"]:
        ob.location = (0, 0, LOWER)
        ob.rotation_euler = Euler((TIP_CLOSED, 0, 0))
    for k, ob in enumerate(rig["pillars"]):
        a = k * math.pi / 2 + math.pi / 4
        ob.location = radial(a, PILLAR_R, BASE_TOP)
        ob.rotation_euler = Euler((CLOSED, 0, a))
    rig["crystal"].location = (0, 0, CRYSTAL_Z)
    rig["crystal"].rotation_euler = Euler((0.3, 0.2, 0.5))
    rig["crystal"].scale = (1, 1, 1)
    rig["cap"].location = (0, 0, CAP_TOP)
    rig["cap"].rotation_euler = Euler((0, 0, 0))
    for ob in rig["shards"]:
        ob.location = (0, 0, CRYSTAL_Z)
        ob.rotation_euler = Euler((0, 0, 0))
        ob.scale = (OFF, OFF, OFF)


def clamp(x, lo=0.0, hi=1.0):
    return max(lo, min(hi, x))


def smooth(u):
    u = clamp(u)
    return u * u * (3 - 2 * u)


def spring(u, rate=7.0):
    """0 to 1 with a small overshoot (about 4 %) that rings out."""
    u = clamp(u)
    return 1 - math.exp(-rate * u) * math.cos(rate * u) if u < 1 else 1.0


def pose_settle(rig, t):
    rest(rig)
    u = t / SETTLE
    s = spring(u)
    for ob in rig["petals"]:
        ob.rotation_euler.x = OPEN + (CLOSED - OPEN) * s
    st = spring((u - 0.25) / 0.75)
    for ob in rig["tips"]:
        ob.rotation_euler.x = TIP_OPEN + (TIP_CLOSED - TIP_OPEN) * st
    c = smooth(u / 0.8)
    land = (u - 0.8) / 0.2
    bounce = 0.025 * math.sin(math.pi * clamp(land)) if land > 0 else 0.0
    rig["cap"].location.z = CAP_TOP + 0.65 * (1 - c) + bounce
    rig["cap"].rotation_euler.z = math.pi / 2 * (1 - c)
    rig["crystal"].rotation_euler.z = 0.5 + 1.6 * (1 - smooth(u))


def pose_idle(rig, t):
    rest(rig)
    w = 2 * math.pi * t / IDLE
    rig["crystal"].rotation_euler.z = 0.5 + 0.25 * math.sin(w)
    rig["crystal"].location.z = CRYSTAL_Z + 0.012 * math.sin(2 * w)
    for ob in rig["tips"]:
        ob.rotation_euler.x = TIP_CLOSED + math.radians(1.5) * (0.5 - 0.5 * math.cos(w))


def pose_hit(rig, t):
    rest(rig)
    a = math.exp(-7 * t) * math.sin(22 * t)
    for ob in rig["petals"]:
        ob.rotation_euler.x = CLOSED + math.radians(9) * max(0.0, a) + math.radians(2) * min(0.0, a)
    for ob in rig["tips"]:
        ob.rotation_euler.x = TIP_CLOSED + math.radians(6) * max(0.0, a)
    rig["cap"].location.z = CAP_TOP + 0.04 * max(0.0, a)
    rig["cap"].rotation_euler.z = 0.08 * a
    rig["crystal"].rotation_euler.x = 0.3 + 0.2 * a


# Where each flying piece lands: its outward distance, turn and final tilt (seeded, so every pod breaks the same way).
_FLY = random.Random(5)
PETAL_FLY = [(_FLY.uniform(0.9, 1.3), _FLY.uniform(-0.6, 0.6), math.radians(_FLY.uniform(84, 96))) for _ in range(4)]
PILLAR_FLY = [math.radians(_FLY.uniform(55, 80)) for _ in range(4)]
SHARD_FLY = [(_FLY.uniform(0, 2 * math.pi), _FLY.uniform(0.5, 1.4), _FLY.uniform(0.4, 1.0), _FLY.uniform(0, 6)) for _ in range(SHARDS)]


def pose_break(rig, t):
    rest(rig)
    u = smooth(t / 1.2) if t < 1.2 else 1.0
    lin = clamp(t / 1.2)
    arc = 4 * lin * (1 - lin)
    rig["crystal"].scale = (max(OFF, 1 - t / 0.08),) * 3
    for i, ob in enumerate(rig["shards"]):
        a, d, h, spin = SHARD_FLY[i]
        ob.scale = (1.0,) * 3 if t > 0 else (OFF,) * 3
        ob.location = (math.cos(a) * d * u, math.sin(a) * d * u, CRYSTAL_Z + (0.04 - CRYSTAL_Z) * u + h * arc)
        ob.rotation_euler = Euler((spin * lin, 1.0 * lin, 8 * lin))
    for k, ob in enumerate(rig["petals"]):
        a = k * math.pi / 2
        dist, turn, flat = PETAL_FLY[k]
        out = Vector((math.sin(a), -math.cos(a), 0))
        p = radial(a, PETAL_R, BASE_TOP) + out * dist * u
        p.z = BASE_TOP + (0.04 - BASE_TOP) * u + 0.7 * arc
        ob.location = p
        ob.rotation_euler = Euler((CLOSED + (flat - CLOSED) * clamp(lin * 1.3), 0, a + turn * u))
    for ob in rig["tips"]:
        ob.rotation_euler.x = TIP_CLOSED * (1 - u)
    for k, ob in enumerate(rig["pillars"]):
        ob.rotation_euler.x = CLOSED + (PILLAR_FLY[k] - CLOSED) * u
    cap = rig["cap"]
    cap.location = (0.9 * u, 0.5 * u, CAP_TOP + (0.05 - CAP_TOP) * u + 1.6 * arc)
    cap.rotation_euler = Euler((math.pi * u, 0.4 * u, 5 * lin))


CLIPS = (("settle", SETTLE, pose_settle), ("idle", IDLE, pose_idle), ("hit", HIT, pose_hit), ("break", BREAK, pose_break))


def bake(rig):
    objs = moving(rig)
    for ob in objs:
        ob.animation_data_create()
    for name, seconds, pose in CLIPS:
        acts = {}
        for ob in objs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        frames = int(round(seconds * FPS))
        for f in range(frames + 1):
            pose(rig, f / FPS)
            for ob in objs:
                ob.keyframe_insert("location", frame=f)
                ob.keyframe_insert("rotation_euler", frame=f)
                ob.keyframe_insert("scale", frame=f)
        for ob in objs:
            lt.linear_keys(acts[ob.name])
            bl.push_track(ob, acts[ob.name], name)
    for ob in objs:
        ob.animation_data.use_nla = False
    rest(rig)


def main():
    for rarity in RARITIES:
        bbe.reset_scene()
        rig = build_pod(make_materials(rarity))
        bake(rig)
        path = MESHES / f"bloom_pod_{rarity}.glb"
        bbe.export(rig["root"], path, animations=True)
        print(f"wrote {path.relative_to(REPO)} ({path.stat().st_size // 1024} KiB)")
        if "--render" in sys.argv:
            import render_bloom_pod
            render_bloom_pod.render_all(rig, Path(sys.argv[sys.argv.index("--render") + 1]), rarity, sys.modules[__name__])


if __name__ == "__main__":
    main()
