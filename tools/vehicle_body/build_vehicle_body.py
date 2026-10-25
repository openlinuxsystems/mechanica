"""Builds the vehicle body parts of the parts audit's batch D3 (Front Bumper, Rear Bumper, Mud Flap, Exhaust Pipe,
License Plate and Roof Rack) and their icons.

    pip install bpy==5.2.2 numpy pillow
    python tools/vehicle_body/build_vehicle_body.py                      # every part: the .glb files and the icons
    python tools/vehicle_body/build_vehicle_body.py --only mud_flap      # these
    python tools/vehicle_body/build_vehicle_body.py --render OUT_DIR     # and the review sheets and lineup (JPEG)

Writes ``mods/vanilla/meshes/parts/<id>.glb`` and ``mods/vanilla/textures/items/<id>_icon.png``; the parts' data is
in ``mods/vanilla/editor/batches/decor.json`` (re-export it after changing a size). The models are in vehicle_body.py;
tools/part_kit/decor_kit.py has the shared materials, shapes and runner. The outputs are committed; re-run this after
changing a model and commit the result. The build is deterministic (seeded noise only).
"""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE.parent / "decor"))
sys.path.insert(0, str(HERE))
import decor_kit as D  # noqa: E402  (first: it imports bpy)
import vehicle_body as V  # noqa: E402

NAMES = {
    "bumper_front": "Front Bumper", "bumper_rear": "Rear Bumper", "mud_flap": "Mud Flap", "exhaust_pipe": "Exhaust Pipe",
    "license_plate": "License Plate", "roof_rack": "Roof Rack",
}

if __name__ == "__main__":
    render = sys.argv.index("--render") + 1 if "--render" in sys.argv else None
    out = Path(sys.argv[render]) if render else None
    if render:
        del sys.argv[render - 1:render + 1]      # decor_kit.run would render with the decor layout; ours follows
    for pid, part in V.PARTS.items():
        part.name = NAMES[pid]
    infos = D.run(V.PARTS, title="Vehicle body (audit D3)", extra_materials=[V.materials])
    if out is not None:
        import bpy
        import render_vehicle_body

        roots = {pid: bpy.data.objects["".join(w.capitalize() for w in pid.split("_"))] for pid in infos}
        render_vehicle_body.render_all(V.PARTS, roots, infos, out)
