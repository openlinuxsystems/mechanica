"""Review renders of the Colony Works sites: python3 render_colony.py OUT [views...]

Views: ``kit`` (the pieces colony_kit.py gained for the sites laid out on a sheet, labelled: the whole sheet, then
each half close up), and for each site of colony_sites.py ``<name>_34`` (three-quarter view from the front right)
and ``<name>_eye`` (eye level, 1.7 m up, before its way in), each with a 1.8 m player by the entrance. The chemical
lake facility's views show its pool as a flat disc at the basin. SAMPLES (default 32) and SCALE (resolution
percent, default 100) control quality; renders are 1600 px wide JPEGs.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
import bpy  # noqa: E402

import kit as K  # noqa: E402
import colony_kit as C  # noqa: E402
import colony_sites as CO  # noqa: E402
import frame as FR  # noqa: E402
from families import M, GRAPHITE  # noqa: E402

out = sys.argv[1]
os.makedirs(out, exist_ok=True)

# name: (3/4 camera, target, lens), (eye camera, target), (player at, yaw)
VIEWS = {
    "packing_station_vegetable": (((40.0, -46.0, 24.0), (0.0, 0.0, 3.0), 30), ((9.0, -36.0, 1.7), (-2.0, -12.0, 3.5)), ((-6.0, -17.0, 0.3), 200)),
    "packing_station_fruit": (((40.0, -46.0, 24.0), (0.0, 0.0, 3.0), 30), ((9.0, -36.0, 1.7), (-2.0, -12.0, 3.5)), ((-6.0, -17.0, 0.3), 200)),
    "silo_cluster": (((44.0, -50.0, 30.0), (0.0, 0.0, 8.0), 30), ((-24.0, -30.0, 1.7), (-8.0, 0.0, 8.0)), ((-14.5, -1.5, 0.3), 230)),
    "silo_district": (((100.0, -108.0, 62.0), (0.0, 0.0, 8.0), 30), ((10.0, -58.0, 1.7), (0.0, -10.0, 9.0)), ((2.0, -27.0, 0.3), 200)),
    "growlab": (((34.0, -40.0, 20.0), (1.0, 0.0, 3.0), 30), ((4.0, -27.0, 1.7), (-2.0, 0.0, 3.0)), ((-6.5, -9.0, 0.3), 200)),
    "chemical_lake_facility": (((52.0, -66.0, 30.0), (0.0, -14.0, 1.0), 30), ((20.0, -24.0, 1.7), (-5.0, -6.0, 3.0)), ((-4.0, -16.0, 0.15), 230)),
    "roadside_kiosk": (((10.0, 12.0, 5.5), (0.0, 0.0, 1.6), 30), ((3.0, 10.0, 1.7), (0.0, 0.0, 1.8)), ((1.6, 3.0, 0.2), 20)),
}


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
    """Lowers the imported player's arms out of the bind pose (as render_settlers.py does)."""
    from mathutils import Vector  # noqa: PLC0415
    bpy.context.view_layer.update()
    for arm in [o for o in root.children_recursive if o.type == "ARMATURE"]:
        inv = arm.matrix_world.to_quaternion().inverted()
        for side in ("l", "r"):
            b = arm.data.bones.get(f"upper_arm_{side}")
            if b is None:
                continue
            rest = b.matrix_local.to_quaternion()
            d_rest = (b.tail_local - b.head_local).normalized()
            d_target = ((inv @ Vector((0, 0, -1))) * 0.94 + d_rest * 0.12).normalized()
            pb = arm.pose.bones[f"upper_arm_{side}"]
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = rest.inverted() @ d_rest.rotation_difference(d_target) @ rest


def tag(P, text, loc, size=0.6):
    """A label lying on the ground in front of a piece."""
    FR.label(P, text, loc, size, M(GRAPHITE, wear=0, grime=0, scratches=0), rot=(0, 0, 0), extrude=0.005)


def kit_sheet():
    """The pieces the sites added to colony_kit.py, in two rows: the walls' new parts and the small pieces in front,
    the big volumes behind."""
    scene()
    K.ground(600, color="#7f9a5c")
    P = C.group(None, "kit")
    # front row: the small pieces
    S, _, _ = C.shed(P, "kit_shed", 7.0, 5.0, loc=(-26.0, 0.0, 0.0), plans={"front": lambda b, f: "porthole"},
                     doorways=(("right", 0.0),), gear=False)
    tag(P, "porthole wall, doorway", (-26.0, -4.2, 0.01))
    C.lamppost(P, (-18.5, 0.0, 0.0), board="K-4")
    tag(P, "lamppost", (-18.5, -4.2, 0.01))
    C.pictogram_board(P, (-13.0, 0.0, 0.0), "vegetable", size=2.5)
    C.pictogram_board(P, (-9.0, 0.0, 0.0), "fruit", size=2.5)
    tag(P, "produce boards", (-11.0, -4.2, 0.01))
    C.belt_conveyor(P, (-4.5, -1.0, 0.8), (1.5, 1.0, 3.0))
    tag(P, "belt conveyor", (-1.5, -4.2, 0.01))
    C.fence_ring(P, [(4.0, -2.0), (12.0, -2.0), (12.0, 3.0), (4.0, 3.0)], gates={0: (0.35, 0.7)}, name="kit_fence")
    tag(P, "lite mesh fence, sliding gate", (8.0, -4.2, 0.01))
    C.silo(P, (19.0, 0.5, 0.0), r=3.0, h=8.0, code="K-1", seed=3, gap=(90, 30, 3.5), ladder=-90)
    tag(P, "silo", (19.0, -4.8, 0.01))
    # back row: the stair tower and gallery, the shed, the glass vault
    _, _, H = C.stair_tower(P, (-30.0, 12.0, 0.0), 2, exits=(("right", 0.0),), code="K-2")
    C.gallery(P, -24.2, -12.0, 15.0, H, doors=[(-16.0, -1)], ends=(False, True), trestles=(-14.0,))
    tag(P, "stair tower, gallery", (-22.0, 9.0, 0.01))
    C.shed(P, "kit_box_shed", 10.0, 7.0, loc=(-3.0, 15.0, 0.0), rollers=(("front", -2.0, 3.0, 3.0, 2.0),), doorways=(("front", 2.5),))
    tag(P, "shed: roller door, doorway", (-3.0, 10.0, 0.01))
    C.glass_vault(P, "kit_vault", 10.0, 7.0, 2.5, loc=(13.0, 15.0, 0.0), doors=(-1,), gables=(-1, 1))
    tag(P, "glass vault", (13.0, 10.0, 0.01))
    for nm, cam, tgt, lens in (("kit_colony_sites", (6.0, -46.0, 24.0), (-4.0, 6.0, 2.5), 30),
                               ("kit_colony_sites_a", (-14.0, -26.0, 10.0), (-14.0, 4.0, 2.5), 30),
                               ("kit_colony_sites_b", (10.0, -26.0, 10.0), (10.0, 6.0, 3.0), 30)):
        shoot(nm, cam, tgt, lens)


def site(name):
    scene()
    K.ground(1600, color="#7f9a5c")
    root = CO.SITES[name]()
    if name == "chemical_lake_facility":
        lx, ly = CO.LAKE
        C.quiet(K.cyl("pool", 16.0, 0.02, K.mat("chemical", "#2a8c12", rough=0.12, emit="#2a8c12", emit_strength=0.15), loc=(lx, ly, 0.02), segs=48, bev=0))
    (c34, t34, lens), (ceye, teye), (man, yaw) = VIEWS[name]
    relax(K.import_glb(K.MESH / "characters/scrapper_male.glb", man, yaw=yaw))
    return root, c34, t34, lens, ceye, teye


views = sys.argv[2:] or ["kit"] + [f"{n}_{v}" for n in VIEWS for v in ("34", "eye")]
for v in views:
    if v == "kit":
        kit_sheet()
        continue
    name, kind = v.rsplit("_", 1)
    root, c34, t34, lens, ceye, teye = site(name)
    if kind == "34":
        shoot(f"{name}_34", c34, t34, lens)
    elif kind == "eye":
        shoot(f"{name}_eye", ceye, teye, 26)
