"""Review renders of the SV-04 cargo ship: python3 render_cargo_ship.py OUT [views...]

SAMPLES (default 32) and SCALE (resolution percent, default 100) control quality. PALETTE picks the hull colours.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402

import kit as K  # noqa: E402
import cargo_ship as S  # noqa: E402

out = sys.argv[1]
views = sys.argv[2:] or ["hero", "side", "rear", "top", "plan", "bridge", "spine", "mess", "engineering", "cargo"]
os.makedirs(out, exist_ok=True)
K.clear()
K.setup_render(int(os.environ.get("SAMPLES", "32")))
bpy.context.scene.render.resolution_percentage = int(os.environ.get("SCALE", "100"))
K.sky(1.0, top="#9fb4c4", horizon="#e4e9ec")
K.sun((48, 0, -40), 4.0)
ship = S.cargo_ship(palette=os.environ.get("PALETTE", "light"))
K.ground(400, color="#6a8f4e") if os.environ.get("GROUND") else None
ship.location.z = 6.0     # flying in the exterior views
man = K.import_glb(K.MESH / "characters/scrapper_male.glb", (7.6, 16.6 + 1.2, 6.0 - 0.15), yaw=90)
cams = {
    # the reference's angle: from the front-right, looking down the length
    "hero": (("he", (46, 54, 26), (0, 0, 5), 28), 1600, 900),
    "side": (("si", (62, 0, 7), (0, 1.5, 6.5), 0, 72), 1600, 700),
    "rear": (("re", (-26, -58, 18), (0, -10, 6), 30), 1600, 900),
    "top": (("to", (0, 1.5, 90), (0, 1.5, 0), 0, 70), 900, 1500),
    "plan": (("pl", (0, 1.5, 90), (0, 1.5, 0), 0, 70), 900, 1500),
    "bridge": (("br", (-1.6, 20.6, 8.0), (0.6, 28.5, 7.2), 18), 1600, 900),
    "spine": (("sp", (0.8, -10.5, 7.7), (-0.3, 6.0, 7.2), 16), 1600, 900),
    "mess": (("me", (1.6, -12.6, 8.4), (5.6, -17.0, 6.6), 18), 1600, 900),
    "engineering": (("en", (-2.0, -18.6, 8.8), (-5.0, -22.8, 7.0), 16), 1600, 900),
    "cargo": (("ca", (5.6, 15.0, 8.6), (-4.0, 19.0, 7.2), 16), 1600, 900),
    "nose": (("no", (14, 44, 2), (0, 28, 5), 30), 1200, 800),
    "crew": (("cr", (2.0, -18.6, 8.8), (5.5, -22.5, 6.8), 16), 1600, 900),
}
ground = None
for v in views:
    spec, w, h = cams[v]
    name, loc, target, lens = spec[:4]
    cam = K.camera(name, loc, target, lens=lens or 35, ortho=spec[4] if len(spec) > 4 else None)
    if v == "plan":
        cam.data.clip_start = cam.location.z - (ship.location.z + 2.9)
    for ob in ship.children:
        if ob.get("tier") == "upper":
            for c in [ob] + list(ob.children_recursive):
                c.hide_render = v == "plan"
    K.render(cam, f"{out}/ship_{v}.png", w, h)
