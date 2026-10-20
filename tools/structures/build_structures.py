"""Build the crash site's structure models (docs/art-direction.md) as .glb files under mods/vanilla/meshes/.

What it builds (procedurally, from scratch: no .blend inputs, no downloads), each with its origin on the ground at the
structure's centre (glTF +Y up, front towards +Z):

* ``meshes/structures/sv04_fore.glb`` and ``sv04_aft.glb`` - the SV-04 Salvager broken in two (Salvager Tech): the
  fore section with the bridge, crew quarters and engineering, and the aft section with the cargo hold and the
  engine room. Both share the ship's origin, so the crash site places them at the same point. The port nacelle tore
  off in flight: ``sv04_nacelle.glb`` is it, lying where it fell (the opening cinematic shows it go).
* ``meshes/structures/relay_beacon_dead.glb`` - a dead Colony relay beacon (Colony Works).
* ``meshes/structures/settler_shack.glb`` - the farmer's half-dome shack with its trade counter and fenced garden
  (Settler Patchwork).
* ``meshes/structures/stone_arch.glb``, ``stone_arch_broken.glb``, ``stone_pillar_{tall,mid,short}.glb`` and
  ``stone_court.glb`` - Old Stone ruins: arches, pillars, and a flagstone court with steps.
* ``meshes/structures/workshop.glb`` - the Workshop (Colony Works): a vehicle repair shop with two always-open bays,
  an office and a worn forecourt. Every Workshop POI uses it; its Fabricator is a part the POI places (a ``part``
  marker), not part of the model.
* ``meshes/structures/cargo_crate_colony.glb`` - a framed Colony cargo crate (decoration in the cargo spill).
* ``meshes/world/scrap_pile_metal.glb`` and ``scrap_pile_wood.glb`` - the starting scrap piles (world objects).
* ``meshes/world/props/*.glb`` - the props the world's scenes lay out between places (props.py): settler fences,
  carts, hay and camps, Colony pipe runs, barriers and a harvester wreck, and a scorched trailer (world objects).

A structure model is one mesh of every part with a few shared detail textures (textures.py), a ``collider`` mesh of
its larger parts (the game collides with that and does not draw it), and its point lights. World object models have
no collider (the object's own collider is used).

How to run (Blender as a Python module, no Blender UI needed):

    pip install bpy==5.2.2 numpy pillow
    python tools/structures/build_structures.py          # everything
    python tools/structures/build_structures.py props    # only the scene props

The outputs are committed; re-run this script after changing it (or kit.py, frame.py, families.py, ship.py,
workshop.py, props.py, textures.py) and commit the result. The build is deterministic (seeded noise only).
"""

from __future__ import annotations

import random
import sys
from pathlib import Path

import bpy  # noqa: I001  (bpy first: it makes bmesh and mathutils importable)

sys.dont_write_bytecode = True                      # no __pycache__ next to the script
sys.path.insert(0, str(Path(__file__).resolve().parent))
import kit as K  # noqa: E402

K.EXPORT = True
import families as F  # noqa: E402
import frame as FR  # noqa: E402
import props as PR  # noqa: E402
import ship as S  # noqa: E402
import workshop as W  # noqa: E402

STRUCTURES = K.MESH / "structures"
WORLD = K.MESH / "world"


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K._finish_images.clear()


def report(name, counts):
    print(f"{name}: {counts[0]} triangles, collider {counts[1]}")


def ship():
    fresh()
    root = S.salvager_ship(game=True)
    fore = next(c for c in root.children if c.name.startswith("fore"))
    aft = next(c for c in root.children if c.name.startswith("aft"))
    report("sv04_fore", K.export(fore, STRUCTURES / "sv04_fore.glb", frame=root))
    report("sv04_aft", K.export(aft, STRUCTURES / "sv04_aft.glb", frame=root))
    single("sv04_nacelle", lambda: S.lost_nacelle())


def single(name, build, folder=STRUCTURES, collider=True):
    fresh()
    root = build()
    report(name, K.export(root, folder / f"{name}.glb", collide_min=0.6 if collider else 1e9))


def court():
    root = K.link(bpy.data.objects.new("court", None))
    s = F.stone()
    K.disc("court_floor", 10.5, K.mat("flag", "#85877f", rough=0.9, cracks=0.6, noise_scale=2), (0, 0), z=0.06, segs=8).parent = root
    for k in range(3):
        K.box(f"court_step{k}", (6 - k * 1.5, 4 - k, 0.35), s, loc=(0, 2, 0.18 + k * 0.35), bev=0.08, parent=root)
    return root


def crate():
    root = K.link(bpy.data.objects.new("crate", None))
    FR.framed_box(root, "ccrate", (0, 0, 0.6), (1.2, 1.2, 1.2), F.M(F.STEEL, metal=0.3), F.M(F.GRAPHITE, metal=0.3), fw=0.1)
    return root


def pile(kind):
    def build():
        root = K.link(bpy.data.objects.new(kind, None))
        S.scrap_pile(root, (0, 0, 0), random.Random(7 if kind == "metal" else 8), kind)
        return root
    return build


def props():
    for name, build in PR.PROPS.items():
        single(name, build, WORLD / "props", collider=False)


def main():
    if sys.argv[1:] == ["props"]:
        props()
        return
    ship()
    single("relay_beacon_dead", lambda: F.dead_beacon())
    single("settler_shack", lambda: F.settler_shack())
    single("stone_arch", lambda: F.old_stone_arch(seed=5))
    single("stone_arch_broken", lambda: F.old_stone_arch(seed=9, broken=True))
    for name, h, seed in (("stone_pillar_tall", 5.0, 21), ("stone_pillar_mid", 3.4, 23), ("stone_pillar_short", 2.6, 22)):
        single(name, lambda h=h, seed=seed: F.stone_pillar((0, 0, 0), h, seed))
    single("stone_court", court)
    single("workshop", lambda: W.workshop())
    single("cargo_crate_colony", crate)
    single("scrap_pile_metal", pile("metal"), WORLD, collider=False)
    single("scrap_pile_wood", pile("wood"), WORLD, collider=False)
    props()


if __name__ == "__main__":
    main()
