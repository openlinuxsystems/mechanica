"""Builds the flight parts' models and icons (parts audit batch D1; tools/aero/aero_kit.py has the designs).

    python3 tools/aero/build_aero.py [wing control_surface rotor hover_jet] [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<part>.glb`` (the Rotor with its ``cw`` and ``ccw`` clips, the Hover Jet with
``run_loop`` and ``off``) and ``mods/vanilla/textures/items/<part>_icon.png`` for the parts named (all four by default);
with ``--render`` a JPEG review sheet per part: three-quarter views of each state, the back, a 0.25 m block for scale and
the icon on dark and light. Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
Then re-run ``tools/author-vanilla.sh`` (the ``aero.json`` batch points at these files).
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import bpy  # noqa: E402
import working_kit as W  # noqa: E402
import aero_kit as A  # noqa: E402
import aero_review as R  # noqa: E402

PARTS = {
    # part: (build, half size (m), icon ortho, icon target, title, notes)
    "wing": (A.build_wing, (0.5, 0.5, 0.125), 1.25, (0, 0, -0.02), "Wing (4 x 1 x 4 blocks, 1 m x 1 m)",
             ["A cambered aluminium aerofoil panel over raised ribs and rivet lines, a steel leading-edge cuff (the part's front),",
              "a gunmetal root rib with copper fittings on the attach edge (left) and a mustard tip stripe. No moving parts."]),
    "control_surface": (A.build_control_surface, (0.5, 0.25, 0.125), 1.15, (0, 0, -0.01), "Control Surface (4 x 1 x 2 blocks)",
                        ["A 1 m x 0.5 m aluminium flap behind a steel hinge bar with gunmetal knuckles at the hinge sockets (front),",
                         "a copper control horn and the logic jack on top. The game turns the whole model about the hinge, up to 10 degrees."]),
    "rotor": (A.build_rotor, (0.375, 0.375, 0.125), 0.95, (0, 0, -0.02), "Rotor (3 x 1 x 3 blocks)",
              ["Three tapered aluminium blades with hazard tips on a copper hub and chrome spinner over a squat finned gunmetal motor.",
               "The game spins the whole model; its cw and ccw clips (the blades readout) pitch the blades for its Spin setting."]),
    "hover_jet": (A.build_hover_jet, (0.25, 0.25, 0.125), 0.78, (0, 0, -0.01), "Hover Jet (2 x 1 x 2 blocks)",
                  ["A ducted fan on a screwed base plate with four corner mounting blocks: louvred intakes, copper bands, a steel exhaust",
                   "grille on top (its jet), a seven-blade fan inside. Running (pose run_loop) the fan spins and the cyan lip ring lights."]),
}


FLAT_VIEW = (-1.0, 1.25, 0.55)


def run(part):
    build, half, ortho, target, title, lines = PARTS[part]
    K.reset_scene()
    m = A.materials()
    rig = build(m)
    root = rig["root"]
    rig["rest"]()
    W.bake(rig)
    info = K.export(root, K.MESHES / f"{part}.glb", animations=bool(rig["clips"]))
    K.report(K.MESHES / f"{part}.glb", info)
    K.strip_animation(root)
    for ob in [root, *root.children_recursive]:
        if ob.animation_data:
            ob.animation_data.use_nla = False
    W.reset(rig)
    rig["states"][-1][1]()
    # the flat panels from a little lower, so their light skin does not face the key light square on
    # (and a little darker: the icon lights are set for gunmetal, and a light top face otherwise burns out to white)
    flat = part in ("wing", "control_surface")
    bpy.context.scene.view_settings.exposure = -0.8 if flat else 0.0
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", FLAT_VIEW if flat else K.ICON_VIEW, ortho, target)
    bpy.context.scene.view_settings.exposure = 0.0
    out = K.args_out()
    if out is not None:
        R.review(part, title, lines, rig, icon, info, out, half)
    return info


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    names = [a for i, a in enumerate(argv) if a in PARTS and (i == 0 or argv[i - 1] != "--render")]
    for name in names or list(PARTS):
        run(name)
