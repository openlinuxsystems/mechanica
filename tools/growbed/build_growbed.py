"""Builds the Growbed (parts map batch 17b; tools/part_kit/farming_kit.py has the design).

    python3 tools/growbed/build_growbed.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/growbed.glb`` (its ``on`` and ``off`` clips, the cyan power pip) and
``mods/vanilla/textures/items/growbed_icon.png``; with ``--render`` the review views of each state, the sheet, and the
bed planted at a few crop stages (``growbed_planted.png`` and its frames) for the batch lineup. Blender's Python module
(bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import farming_kit as F  # noqa: E402
import station_kit as S  # noqa: E402
import working_kit as W  # noqa: E402

TITLE = "Growbed (5 x 2 x 5 blocks)"
LINES = ["Settler Patchwork: a raised bed of mismatched scrap planks on dark timber posts, a faded sea-teal colony board,",
         "a zinc patch, an iron strap and runners, a zinc junction box in the soil; its pip is cyan only while powered and growing."]

# The planted review: (label, crop plant model, stage). The tomato's four stages, then a ripe carrot and potato.
PLANTED = [("EMPTY", None, 0), ("TOMATO STAGE 1", "tomato_plant.glb", 1), ("TOMATO STAGE 2", "tomato_plant.glb", 2),
           ("TOMATO STAGE 3", "tomato_plant.glb", 3), ("TOMATO RIPE", "tomato_plant.glb", 4),
           ("CARROT RIPE", "carrot_plant.glb", 4), ("POTATO RIPE", "potato_plant.glb", 4), ("COTTON RIPE", "cotton_plant.glb", 4)]


def main():
    K.reset_scene()
    m = F.materials()
    rig = F.build_growbed(m)
    root = rig["root"]
    rig["rest"]()
    S.bake(rig)
    info = K.export(root, K.MESHES / "growbed.glb", animations=True)
    K.report(K.MESHES / "growbed.glb", info)
    K.strip_animation(root)
    W.reset(rig)
    rig["states"][-1][1]()
    icon = K.icon(root, K.ICONS / "growbed_icon.png", S.FRONT_ICON, 1.9)
    out = K.args_out()
    if out is not None:
        S.review("growbed", TITLE, LINES, rig, icon, info, out, (F.HALF, F.HALF, 0.25))
        planted(rig, out)


def planted(rig, out):
    """The bed (powered) with a crop at each stage, from the three-quarter view, one frame per entry."""
    import render_kit as rk
    from mathutils import Vector
    rl = rk.rl
    W.reset(rig)
    rig["states"][-1][1]()
    root = rig["root"]
    lo, _hi = rk._bounds(root)
    target = Vector((0, 0, lo.z + 0.45))
    cam = rl.camera("P", target + Vector((1.0, -1.25, 0.9)).normalized() * 3.6, target, lens=50)
    frames = []
    top = F.SOIL_Z + 0.016
    for label, glb, stage in PLANTED:
        made = []
        if glb is not None:
            _roots, made = F.plant(glb, stage, (0, 0, top))
            F.bake_pose(made)
        p = out / "frames" / f"growbed_planted_{label.lower().replace(' ', '_')}.png"
        p.parent.mkdir(parents=True, exist_ok=True)
        rl.render(cam, p, rk.W, rk.H)
        frames.append((label, p))
        for ob in made:
            bpy_remove(ob)
    bpy_remove(cam)
    row = rk.row(frames, "PLANTED", True)
    row.save(out / "growbed_planted.png")
    print(f"rendered the planted Growbed into {out / 'growbed_planted.png'}")


def bpy_remove(ob):
    import bpy
    bpy.data.objects.remove(ob, do_unlink=True)


if __name__ == "__main__":
    main()
