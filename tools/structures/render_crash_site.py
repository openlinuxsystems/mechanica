"""Review renders of the crashed SV-04: python3 render_crash_site.py OUT [views...]

SAMPLES (default 24) and SCALE (resolution percent) control quality.
"""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import kit as K  # noqa: E402
import crash_site as C  # noqa: E402

out = sys.argv[1]
views = sys.argv[2:] or ["aerial", "hero", "airlock", "aft_front", "rear", "container", "engineering", "bridge", "plan"]
os.makedirs(out, exist_ok=True)
K.clear()
K.setup_render(int(os.environ.get("SAMPLES", "24")))
bpy.context.scene.render.resolution_percentage = int(os.environ.get("SCALE", "100"))
K.sky(1.0)
K.sun((52, 0, -30), 4.0)
K.ground(700, color="#5f9a3e")
root, pieces = C.wreck()
C.scatter(root, random.Random(5))
man = K.import_glb(K.MESH / "characters/scrapper_male.glb", (14.0, 24.0, 0), yaw=150)
for t in ((-30, 30), (35, -20), (-40, -40), (30, 45), (-25, 60)):
    K.import_glb(K.MESH / "world/burnt_tree.glb", (t[0], t[1], 0), yaw=t[0] * 7)


def local(piece, p):
    """A point given in the intact ship's coordinates, where it ended up on that piece."""
    return tuple(pieces[piece].matrix_world @ (Vector(p) - Vector(C.PIVOTS[piece])))


cams = {
    "aerial": (("ae", (120, -260, 170), (0, -70, 0), 30), 1600, 900),
    "hero": (("he", (52, 46, 22), (-2, -6, 2), 28), 1600, 900),
    "rear": (("re", (-46, -66, 18), (-3, -10, 2), 30), 1600, 900),
    "airlock": (("al", local("bow", (17, 8, 1.8)), local("bow", (6.8, 17.5, 1.2)), 28), 1600, 900),
    "aft_front": (("af", local("aft", (4, 2, 2.0)), local("aft", (0, -12, 1.5)), 26), 1600, 900),
    "container": (("co", (-26, -40, 6), (-37, -56, 1), 30), 1600, 900),
    "engineering": (("en", local("aft", (-2.0, -18.6, 1.8)), local("aft", (-5.0, -22.8, 1.0)), 16), 1600, 900),
    "bridge": (("br", local("bow", (-1.6, 20.6, 2.0)), local("bow", (0.6, 28.5, 1.2)), 18), 1600, 900),
    "plan": (("pl", (0, -2, 120), (0, -2, 0), 0, 80), 1200, 1500),
}
for v in views:
    spec, w, h = cams[v]
    name, loc, target, lens = spec[:4]
    cam = K.camera(name, loc, target, lens=lens or 35, ortho=spec[4] if len(spec) > 4 else None)
    K.render(cam, f"{out}/crash_{v}.png", w, h)
