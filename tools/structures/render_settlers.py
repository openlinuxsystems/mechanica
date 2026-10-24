"""Review renders of the Settler Patchwork kit and buildings: python3 render_settlers.py OUT [views...]

Views: ``kit`` (every piece of settler_kit.py laid out on a sheet, labelled), and for each building of settlers.py
``<name>_34`` (three-quarter view from the front right) and ``<name>_eye`` (eye level, 1.7 m up, in front of its way
in), each with a 1.8 m player standing by the entrance; ``<name>_top`` looks straight down (the haymaze's plan).
SAMPLES (default 32) and SCALE (resolution percent, default 100) control quality; renders are 1600 px wide JPEGs.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
import bpy  # noqa: E402

import kit as K  # noqa: E402
import settlers as ST  # noqa: E402

out = sys.argv[1]
os.makedirs(out, exist_ok=True)

# name: (3/4 camera, target), (eye camera, target), (player at, yaw)
VIEWS = {
    "farmer_shack": (((17.0, -19.0, 9.0), (0.0, 0.5, 1.8)), ((5.5, -15.0, 1.7), (-0.8, -2.0, 2.0)), ((1.6, -4.4, 0.3), 200)),
    "campsite": (((17.0, -19.0, 9.5), (0.0, -0.5, 1.0)), ((3.5, -15.0, 1.7), (-0.5, -4.0, 1.4)), ((1.6, -3.8, 0.0), 160)),
    "camp": (((12.0, -14.0, 7.0), (0.0, 0.5, 1.0)), ((2.5, -11.0, 1.7), (-1.0, 1.0, 1.3)), ((-2.0, 0.6, 0.0), 200)),
    "large_camp": (((40.0, -44.0, 22.0), (0.0, -2.0, 1.5)), ((6.0, -27.0, 1.7), (0.0, -4.0, 2.5)), ((1.5, -9.0, 0.3), 200)),
    "farmers_hideout": (((34.0, -38.0, 21.0), (0.0, 0.0, 2.0)), ((5.0, -27.0, 1.7), (0.0, -10.0, 3.5)), ((1.2, -17.0, 0.3), 190)),
    "farmbot_scrapyard": (((48.0, -52.0, 28.0), (0.0, 0.0, 1.0)), ((8.0, -34.0, 1.7), (0.0, -5.0, 3.0)), ((1.5, -23.5, 0.3), 200)),
    "capsule_scrapyard": (((40.0, -44.0, 22.0), (0.0, 0.0, 2.0)), ((5.0, -30.0, 1.7), (0.0, 0.0, 3.0)), ((1.5, -18.5, 0.3), 200)),
    "haymaze": (((48.0, -56.0, 30.0), (0.0, 0.0, 0.0)), ((1.5, -40.0, 1.7), (-2.5, -20.0, 2.5)), ((-1.0, -31.5, 0.3), 190)),
    "scrap_city_garage": (((48.0, -56.0, 24.0), (0.0, 0.0, 4.0)), ((9.0, -36.0, 1.7), (0.0, -18.0, 5.0)), ((3.0, -24.0, 0.3), 200)),
}
TOP = {"haymaze": 70.0}       # plan views: ortho size


def scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K._protos.clear()
    K.setup_render(int(os.environ.get("SAMPLES", "32")))
    sc = bpy.context.scene
    sc.render.resolution_percentage = int(os.environ.get("SCALE", "100"))
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    K.sky(1.0, top="#86b4d8", horizon="#dce8ee")
    K.sun((52, 0, -38), 4.2)


def shoot(name, loc, target, lens, w=1600, h=900):
    cam = K.camera(name, loc, target, lens=lens)
    K.render(cam, f"{out}/{name}.jpg", w, h)


def relax(root):
    """Lowers the imported player's arms out of the bind pose (they hang a little away from the body)."""
    from mathutils import Vector  # noqa: PLC0415
    bpy.context.view_layer.update()
    for arm in [o for o in root.children_recursive if o.type == "ARMATURE"]:
        inv = arm.matrix_world.to_quaternion().inverted()
        for side, sx in (("l", 1), ("r", -1)):
            for bone_name in (f"upper_arm_{side}",):
                b = arm.data.bones.get(bone_name)
                if b is None:
                    continue
                rest = b.matrix_local.to_quaternion()
                d_rest = (b.tail_local - b.head_local).normalized()
                world_down = Vector((0, 0, -1))
                d_target = (inv @ world_down)
                # keep a little of the original sideways direction so the hands clear the hips
                d_target = (d_target * 0.94 + d_rest * 0.12).normalized()
                q_arm = d_rest.rotation_difference(d_target)
                pb = arm.pose.bones[bone_name]
                pb.rotation_mode = "QUATERNION"
                pb.rotation_quaternion = rest.inverted() @ q_arm @ rest


def building(name):
    scene()
    K.ground(600, color="#7f9a5c")
    root = ST.SETTLERS[name]()
    (c34, t34), (ceye, teye), (man, yaw) = VIEWS[name]
    relax(K.import_glb(K.MESH / "characters/scrapper_male.glb", man, yaw=yaw))
    return root, c34, t34, ceye, teye


views = sys.argv[2:] or ["kit"] + [f"{n}_{v}" for n in VIEWS for v in ("34", "eye")] + [f"{n}_top" for n in TOP]
for v in views:
    if v == "kit":
        import render_settler_kit  # noqa: PLC0415
        render_settler_kit.sheet(scene, shoot)
        continue
    name, kind = v.rsplit("_", 1)
    root, c34, t34, ceye, teye = building(name)
    if kind == "34":
        shoot(f"{name}_34", c34, t34, 30)
    elif kind == "eye":
        shoot(f"{name}_eye", ceye, teye, 26)
    elif kind == "top":
        cam = K.camera("top", (0.0, 0.0, 120.0), (0.0, 0.001, 0.0), ortho=TOP.get(name, 60.0))
        K.render(cam, f"{out}/{name}_top.jpg", 1600, 1600)
