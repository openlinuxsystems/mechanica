"""Builds the Creative nature props of the parts audit's batch D6 (Boulder, Large Rock, Small Rock, Bush, Grass Tuft,
Flower Patch, Fallen Log and Tree Trunk) from the world's own meshes, and their icons.

    pip install bpy==5.2.2 numpy pillow
    python tools/nature_parts/build_nature_parts.py                      # every part: the .glb files and the icons
    python tools/nature_parts/build_nature_parts.py --only bush          # these
    python tools/nature_parts/build_nature_parts.py --render OUT_DIR     # and the review sheets and lineup (JPEG)

Needs the .NET SDK too: the bush, grass and flower meshes are the built-in flora models (Mechanica.Core FloraModels),
which tools/nature_parts/flora_dump writes out for this script. Writes ``mods/vanilla/meshes/parts/<id>.glb`` and
``mods/vanilla/textures/items/<id>_icon.png``; the parts' data is in ``mods/vanilla/editor/batches/decor.json``
(re-export it after changing a size). The models are in nature_parts.py; tools/part_kit/decor_kit.py has the runner.
Re-run this after the world's rock, tree or flora models change, and commit the result.
"""

import json
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
sys.path.insert(0, str(HERE))
import decor_kit as D  # noqa: E402  (first: it imports bpy)
import nature_parts as N  # noqa: E402

NAMES = {
    "boulder": "Boulder", "large_rock": "Large Rock", "small_rock": "Small Rock", "bush": "Bush", "grass_tuft": "Grass Tuft",
    "flower_patch": "Flower Patch", "fallen_log": "Fallen Log", "tree_trunk": "Tree Trunk",
}


def load_flora():
    """The flora models' meshes, straight from Mechanica.Core."""
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "flora.json"
        subprocess.run(["dotnet", "run", "--project", str(HERE / "flora_dump"), "--", str(out), *N.FLORA_MODELS],
                       check=True, cwd=D.REPO)
        N.FLORA.update(json.loads(out.read_text()))


if __name__ == "__main__":
    render = sys.argv.index("--render") + 1 if "--render" in sys.argv else None
    out = Path(sys.argv[render]) if render else None
    if render:
        del sys.argv[render - 1:render + 1]      # decor_kit.run would render with the decor layout; ours follows
    load_flora()
    for pid, part in N.PARTS.items():
        part.name = NAMES[pid]
    infos = D.run(N.PARTS, title="Nature props (audit D6)")
    if out is not None:
        import bpy
        import render_nature_parts

        roots = {pid: bpy.data.objects["".join(w.capitalize() for w in pid.split("_"))] for pid in infos}
        render_nature_parts.render_all(N.PARTS, roots, infos, out)
