"""Review renders of the Colony Works kit and the three warehouses: python3 render_warehouses.py OUT [views...]

Views: ``kit`` (every module of colony_kit.py laid out on a sheet: the whole sheet, then each row of it close up), and
for each of warehouse_small, warehouse and warehouse_large ``<name>_34`` (three-quarter view from the front right, its
yard round it), ``<name>_yard`` (over the yard from a front corner of the fence, a 1.8 m player at the gate, and from the back corner), ``<name>_front`` (eye level from the front), ``<name>_far`` (500 m off: the silhouette),
``<name>_roof`` (across the roof) and ``<name>_cut`` (the front wall left out: the floors and the stair); ``scale`` is
the medium warehouse's door with a 1.8 m player beside it. SAMPLES (default 32) and SCALE (resolution percent, default
100) control quality; renders are 1600 px wide JPEGs.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True
import bpy  # noqa: E402

import kit as K  # noqa: E402
import colony_kit as C  # noqa: E402
import warehouses as W  # noqa: E402
import frame as FR  # noqa: E402
from families import M, GRAPHITE  # noqa: E402

out = sys.argv[1]
views = sys.argv[2:] or ["kit"] + [f"{n}_{v}" for n in W.WAREHOUSES for v in ("34", "front")] + ["scale"]
os.makedirs(out, exist_ok=True)


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


def tag(P, text, loc, size=0.6):
    """A label lying on the ground in front of a module."""
    FR.label(P, text, loc, size, M(GRAPHITE, wear=0, grime=0, scratches=0), rot=(0, 0, 0), extrude=0.005)


def kit_sheet():
    """Every module in four rows (walls and doors; floors, stairs and roof gear; the silhouette pieces; paint and
    the yard pieces), each with its name on the ground in front of it."""
    scene()
    K.ground(600, color="#7f8b6c")
    P = K.link(bpy.data.objects.new("sheet", None))
    K.box("sheet_pad", (104.0, 118.0, 0.06), M("#b9b6ac", wear=0, grime=0, scratches=0), loc=(2.0, 18.0, 0.03), bev=0, parent=P)
    rows = [C.group(P, f"row{k}") for k in range(4)]
    Y = [-34.0, -6.0, 26.0, 62.0]
    z = 0.06

    # ---- row 1: walls and doors
    R, y = rows[0], Y[0]
    for k, (kind, label) in enumerate((("plain", "WALL BAY"), ("window", "WINDOW BAY"), ("broken", "BROKEN WINDOWS"), ("louvre", "LOUVRE BAY"),
                                       ("fan", "FAN BAY"), ("door", "DOOR BAY"))):
        x = -44.0 + k * 7.0
        G = C.group(R, f"bay_{kind}", (x, y, z))
        g = C.wall(G, "bay", 5.0, 1, plan=lambda b, f, kind=kind: kind, seed=k + 3)
        for s in (-1, 1):
            C.post(g, s * 2.5, C.STOREY + C.SLAB + C.PARAPET)
        tag(R, label, (x, y - 2.0, z + 0.01))
    G = C.group(R, "corner", (-1.0, y, z))
    C.corner_post(G, (0, 0, 0), C.STOREY * 2 + C.SLAB + C.PARAPET)
    tag(R, "CORNER POST", (-1.0, y - 2.0, z + 0.01))
    G = C.group(R, "pipe", (5.0, y, z))
    C.downpipe(G, (0, 0, 0), 6.0)
    C.coping(G, 3.0, 6.0)
    K.box("pipe_wall", (3.0, 0.3, 6.0), C.bone(), loc=(0, 0.2, 3.0), bev=0.02, parent=G)
    tag(R, "DOWNPIPE, COPING", (5.0, y - 2.0, z + 0.01))
    G = C.group(R, "rdoor", (17.0, y, z))
    g = C.wall(G, "door_wall", 11.0, 2, bays=[2.5, 6.0, 2.5], holes=[(-3.0, 3.0, 0.0, 4.05)], seed=5, plan=lambda b, f: "plain" if b == 1 else "window")
    C.roller_door(g, (0, 0, C.SLAB), 6.0, 3.75, 3.0, lamp=False)
    C.canopy(g, (0, C.CREST - 0.02, C.SLAB + 3.75 + 1.05), 9.0, depth=3.0)
    tag(R, "ROLLER DOOR, CANOPY", (17.0, y - 4.2, z + 0.01))
    G = C.group(R, "rdoor2", (31.0, y, z))
    g = C.wall(G, "door_wall", 10.0, 1, bays=[2.0, 6.0, 2.0], holes=[(-3.0, 3.0, 0.0, 4.05)], seed=6)
    C.roller_door(g, (0, 0, C.SLAB), 6.0, 3.75, 0.0)
    tag(R, "ROLLER DOOR SHUT", (31.0, y - 2.6, z + 0.01))
    G = C.group(R, "hh", (45.0, y + 2.0, z - 9.3))
    K.box("hh_stand", (4.8, 9.8, 10.44), M("#9b988e", wear=0, grime=0.3, scratches=0), loc=(2.5, 5.0, 5.22), bev=0.02, parent=G)
    C.head_house(G, 0.0, 5.0, 0.0, 10.0, 9.3, 4.5, "07")
    tag(R, "STAIR HEAD HOUSE", (47.0, y - 2.0, z + 0.01))

    # ---- row 2: floors, stairs, catwalks, roof gear
    R, y = rows[1], Y[1]
    G = C.group(R, "slab", (-44.0, y, z))
    C.floor_slab(G, -3.0, 3.0, -2.0, 2.0, 1.5, open_edges=("y0",))
    for x in (-2.8, 2.8):
        K.box("slab_leg", (0.3, 0.3, 1.2), C.steel(), loc=(x, 1.8, 0.6), bev=0.02, parent=G)
    tag(R, "FLOOR SLAB, EDGE", (-44.0, y - 3.4, z + 0.01))
    G = C.group(R, "core", (-36.0, y - 4.0, z))
    C.stair_core(G, (0, 0, 0.3), 3)
    K.box("core_wall", (0.2, 8.0, 12.0), C.bone(), loc=(-0.45, 4.0, 6.0), bev=0.02, parent=G)
    tag(R, "STAIR CORE", (-34.0, y - 5.4, z + 0.01))
    G = C.group(R, "escape", (-23.0, y - 4.0, z), 90)
    C.wall(G, "esc_wall", 10.0, 2, seed=8, plan=lambda b, f: "door" if b == 1 else "window")
    C.fire_escape(G, (-3.75, 0, 0), 2, doors=False)
    tag(R, "FIRE ESCAPE", (-20.0, y - 6.4, z + 0.01))
    G = C.group(R, "cw", (-9.0, y, z))
    C.catwalk(G, (-3.0, 0.0), (3.0, 0.0), width=1.0, z=2.5)
    for x in (-2.8, 2.8):
        K.box("cw_leg", (0.2, 0.2, 2.3), C.steel(), loc=(x, 0.6, 1.15), bev=0.02, parent=G)
    tag(R, "CATWALK, RAILING", (-9.0, y - 2.0, z + 0.01))
    G = C.group(R, "ladder", (-2.0, y, z))
    C.ladder_caged(G, (0, 0, 0), 6.0)
    tag(R, "CAGED LADDER", (-2.0, y - 2.0, z + 0.01))
    G = C.group(R, "roof", (8.0, y, z))
    C.roof_deck(G, -5.0, 5.0, -3.0, 3.0, 0.4)
    C.skylight(G, (0, 0.8, 0.4), l=6.0, w=2.0, broken={(2, 1), (4, -1)})
    C.roof_hatch(G, (-3.6, -1.8, 0.4))
    C.mushroom_vent(G, (3.8, -2.0, 0.4))
    tag(R, "ROOF, SKYLIGHT, HATCH, VENT", (8.0, y - 4.0, z + 0.01), 0.5)
    G = C.group(R, "ac", (20.0, y, z))
    C.ac_unit(G, (0, 1.0, 0))
    C.duct(G, [(-1.5, 1.0, 0.9), (-3.0, 1.0, 0.9), (-3.0, -2.0, 0.9), (-3.0, -2.0, 0.2)])
    tag(R, "AIR HANDLER, DUCT", (19.0, y - 3.4, z + 0.01))
    G = C.group(R, "pipes", (30.0, y, z))
    C.pipe_run(G, [(-2.5, 0, 0.3), (-2.5, 0, 3.0), (2.5, 0, 3.0), (2.5, 0, 0.3)], r=0.16)
    C.pipe_run(G, [(-2.0, 0.6, 0.3), (-2.0, 0.6, 2.4), (2.0, 0.6, 2.4), (2.0, 0.6, 0.3)], r=0.1, mat=M("#c8423a", wear=0.4, grime=0.4))
    tag(R, "PIPE RUNS", (30.0, y - 2.0, z + 0.01))
    G = C.group(R, "tank", (42.0, y + 1.0, z))
    C.roof_tank(G, (0, 0, 0), r=2.5, h=5.0, code="T-1")
    tag(R, "CAPSULE TANK", (42.0, y - 3.4, z + 0.01))

    # ---- row 3: the silhouette pieces
    R, y = rows[2], Y[2]
    G = C.group(R, "stack", (-34.0, y, z))
    C.stack(G, (0, 0, 0), h=32.0, r=1.2)
    tag(R, "FLUE STACK 32 M", (-34.0, y - 9.0, z + 0.01), 0.8)
    G = C.group(R, "crane", (-14.0, y + 2.0, z))
    C.tower_crane(G, (0, 0, 0), mast=36.0, jib=32.0, counter=11.0, yaw=-20, hook=14.0)
    tag(R, "TOWER CRANE 38 M", (-10.0, y - 6.0, z + 0.01), 0.8)
    import props_utility as PU  # noqa: PLC0415
    t = PU.water_tower_colony()
    t.parent = R
    t.location = (26.0, y + 2.0, z)
    tag(R, "WATER TOWER (PROP)", (26.0, y - 3.4, z + 0.01), 0.8)

    # ---- row 4: paint and the yard pieces
    R, y = rows[3], Y[3]
    G = C.group(R, "numbers", (-36.0, y, z))
    K.box("num_wall", (11.0, 0.3, 3.0), C.bone(), loc=(0, 0.2, 1.5), bev=0.02, parent=G)
    for k in range(4):
        C.floor_number(G, k + 1, (-3.9 + k * 2.6, 0.05, 0.4), size=2.0, ribbed=False)
    tag(R, "FLOOR NUMBERS", (-36.0, y - 2.0, z + 0.01))
    G = C.group(R, "logo", (-22.0, y, z))
    K.box("logo_wall", (10.0, 0.3, 4.0), C.bone(), loc=(0, 0.25, 2.0), bev=0.02, parent=G)
    C.logo_panel(G, (0, 0.1, 0.6), w=8.0, h=2.25, code="STORES 07")
    tag(R, "COLONY LOGO PANEL", (-22.0, y - 2.0, z + 0.01))
    G = C.group(R, "haz", (-9.0, y, z))
    K.box("haz_wall", (6.0, 0.3, 3.0), C.bone(), loc=(0, 0.2, 1.5), bev=0.02, parent=G)
    C.hazard_band(G, (0, 0.04, 0.4), 5.0, 0.6)
    C.hazard_band(G, (0, 0.04, 1.5), 5.0, 0.25)
    C.hazard_band(G, (0, 0.04, 2.2), 2.0, 0.45)
    tag(R, "HAZARD BANDS", (-9.0, y - 2.0, z + 0.01))
    G = C.group(R, "intake", (0.0, y, z))
    K.box("intake_wall", (3.0, 0.3, 6.0), C.bone(), loc=(0, 0.2, 3.0), bev=0.02, parent=G)
    C.intake_head(G, 0, 5.0, y=0.05)
    tag(R, "POWER INTAKE", (0.0, y - 2.0, z + 0.01))
    G = C.group(R, "flood", (10.0, y, z))
    C.floodlight_mast(G, (0, 0, 0), h=10.0, lit=True)
    tag(R, "FLOODLIGHT MAST", (10.0, y - 2.0, z + 0.01))
    G = C.group(R, "fence", (24.0, y, z))
    C.mesh_fence(G, (-7.5, 0.0), (7.5, 0.0), gate=(0.66, 1.01))
    tag(R, "MESH FENCE, SLIDING GATE", (24.0, y - 2.0, z + 0.01))

    shoot("kit_sheet", (0, -112.0, 86.0), (2.0, 14.0, 2.0), 30, 1600, 1100)

    hidden = {ob.name for ob in P.children_recursive if ob.hide_render}

    bpy.context.view_layer.update()

    def only(k, x0=-1e9, x1=1e9):
        """Shows row k's modules (by where each stands) between x0 and x1."""
        for i, r in enumerate(rows):
            for g in r.children:
                x = g.matrix_world.translation.x
                for ob in [g] + list(g.children_recursive):
                    ob.hide_render = ob.name in hidden or i != k or not (x0 <= x <= x1)
    for k, (x0, x1, nm, cam, tgt, lens, hh) in enumerate((
            (-60, 2, "kit_1a_walls", (-23.0, Y[0] - 30.0, 7.0), (-23.0, Y[0], 3.0), 30, 700),
            (2, 60, "kit_1b_doors_corners", (24.0, Y[0] - 34.0, 9.0), (24.0, Y[0], 4.0), 30, 700),
            (-60, -15, "kit_2a_floors_stairs", (-31.0, Y[1] - 30.0, 11.0), (-31.0, Y[1], 4.0), 32, 900),
            (-15, 60, "kit_2b_catwalk_roof_gear", (15.0, Y[1] - 32.0, 12.0), (15.0, Y[1], 1.5), 30, 700),
            (-60, 60, "kit_3_silhouettes", (-4.0, Y[2] - 70.0, 16.0), (-4.0, Y[2], 16.0), 30, 1000),
            (-60, -3, "kit_4a_paint", (-22.0, Y[3] - 24.0, 5.0), (-22.0, Y[3], 2.0), 34, 600),
            (-3, 60, "kit_4b_yard_pieces", (14.0, Y[3] - 24.0, 7.0), (14.0, Y[3], 4.0), 34, 700))):
        only(int(nm[4]) - 1, x0, x1)
        shoot(nm, cam, tgt, lens, 1600, hh)
    for ob in P.children_recursive:
        ob.hide_render = ob.name in hidden


def warehouse_scene(name, with_yard=True, man=None):
    scene()
    K.ground(1600, color="#7f9a5c")
    root = W.warehouse(name, with_yard=with_yard)
    if man:
        K.import_glb(K.MESH / "characters/scrapper_male.glb", man[0], yaw=man[1])
    return root


for v in views:
    if v == "kit":
        kit_sheet()
        continue
    if v == "scale":
        _, w, d, _, _ = W.WAREHOUSES["warehouse"]
        hd = d / 2
        warehouse_scene("warehouse", man=((-1.4, -hd - 2.4, 0.0), 200))
        shoot("scale_warehouse_door", (5.5, -hd - 12.0, 2.2), (-0.8, -hd, 2.9), 26)
        continue
    name, kind = v.rsplit("_", 1)
    floors, w, d, _, _ = W.WAREHOUSES[name]
    hw, hd = w / 2, d / 2
    H = 4 * floors + 0.3
    x0, x1, y0, y1 = W.YARD[name]
    man = ((W.GATE + 1.5, y0 - 1.5, 0.0), 160) if kind == "yard" else None
    root = warehouse_scene(name, with_yard=kind != "cut", man=man)
    if kind == "yard":
        shoot(f"{name}_yard", (x1 + 9.0, y0 - 11.0, 13.0), (x0 * 0.3, (y0 + y1) * 0.3, 1.0), 24)
        shoot(f"{name}_yard_back", (x0 - 4.0, y1 + 6.0, 11.0), (x1 * 0.35, (y0 + y1) * 0.3, 1.0), 22)
    elif kind == "34":
        dist = max(w, d) * 1.45
        shoot(f"{name}_34", (hw + dist * 0.62, -hd - dist * 0.85, H + dist * 0.38), (0, 0, H * 0.75), 32)
    elif kind == "front":
        shoot(f"{name}_front", (0.0, -hd - max(w * 0.95, 38.0), 1.7), (0, -hd, H * 0.6), 24)
    elif kind == "far":
        shoot(f"{name}_far", (220.0, -460.0, 6.0), (0, 0, H * 0.7), 85)
    elif kind == "roof":
        shoot(f"{name}_roof", (hw + 3.0, -hd - 3.0, H + 10.0), (-hw * 0.3, hd * 0.3, H), 24)
    elif kind == "cut":
        for g in root.children:
            if g.name.startswith("front_wall"):
                for ob in g.children_recursive:
                    ob.hide_render = True
        shoot(f"{name}_cut", (-hw * 0.35, -hd - 16.0, H * 0.75), (-hw * 0.55, -hd + 6.0, H * 0.35), 24)
