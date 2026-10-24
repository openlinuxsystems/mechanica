"""Builds the Field Kitchen (consumables batch C4; tools/part_kit/station_kit.py has the design).

    python3 tools/field_kitchen/build_field_kitchen.py [--render OUT_DIR]

Writes ``mods/vanilla/meshes/parts/field_kitchen.glb`` (its ``idle_loop``, ``start``, ``work_loop`` and ``complete`` clips)
and ``mods/vanilla/textures/items/field_kitchen_icon.png``; with ``--render`` the review views of each state, the sheet,
and a second sheet (``field_kitchen_review.png``): three-quarter front and back (the port side), front and back, idle and
cooking, beside a 25 cm block. Blender's Python module (bpy) runs it; nothing is downloaded. The build is deterministic.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402,F401  (first: it imports bpy)
import station_kit as S  # noqa: E402

TITLE = "Field Kitchen (3 x 4 x 3 blocks)"
LINES = ["Salvager Tech: a hull case in a gunmetal frame, an oven door with a window (burgers on a rack, a fan, heating rods),",
         "a hot plate with two burners and a skillet, a console (screen, knobs, timer, cyan pip), a copper flue on the back,",
         "utensils on the left and the standard two-way IO port on the right. Cooking: the rods and burners glow, the pip lights."]


def review_sheet(out):
    """Three-quarter front and back, front and back, idle and cooking, with a 25 cm block beside the kitchen."""
    import bpy
    from mathutils import Vector
    from PIL import Image, ImageDraw
    import render_kit as rk
    import working_kit as W
    rl = rk.rl
    root = bpy.data.objects["FieldKitchen"]
    rig = {"root": root, "_rest": {}}
    for name in ("Fan", "Needle", "OvenGlow", "Pip", "BurnerL", "BurnerR"):
        ob = bpy.data.objects[name]
        rig["_rest"][name] = (ob, ob.location.copy(), ob.rotation_euler.copy(), ob.scale.copy())
    block = bpy.data.objects.new("Block25", bpy.data.meshes.new("Block25"))
    bpy.context.scene.collection.objects.link(block)
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(block.data)
    block.data.materials.append(bpy.data.materials.get("k_steel") or bpy.data.materials[0])
    block.location = (0.375 + 0.3, -0.2, -0.5 + 0.125)
    target = Vector((0.1, 0, -0.05))
    cams = {
        "3/4 FRONT": rl.camera("RQ", target + Vector((1.0, -1.25, 0.75)).normalized() * 3.2, target, lens=50),
        "3/4 BACK (port side)": rl.camera("RB", target + Vector((1.1, 1.2, 0.7)).normalized() * 3.2, target, lens=50),
        "FRONT": rl.camera("RF", Vector((0.1, -20, -0.05)), Vector((0.1, 0, -0.05)), ortho=1.55),
        "BACK": rl.camera("RK", Vector((0.1, 20, -0.05)), Vector((0.1, 0, -0.05)), ortho=1.55),
    }
    states = {"IDLE": lambda: rig_pose(rig, 0), "COOKING": lambda: rig_pose(rig, 1)}
    tiles = []
    for label, pose in states.items():
        pose()
        for view, cam in cams.items():
            p = out / "frames" / f"field_kitchen_review_{label.lower()}_{len(tiles)}.png"
            rl.render(cam, p, 640, 560)
            tiles.append((f"{view}, {label.lower()}", p))
    W_, H_ = 640, 560
    sheet = Image.new("RGB", (40 + 4 * (W_ + 20), 200 + 2 * (H_ + 50) + 320), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), TITLE + ": design review (consumables batch C4)", fill=rl.INK, font=rl.font(40, True))
    for i, t in enumerate(LINES):
        d.text((40, 80 + 34 * i), t, fill=rl.INK, font=rl.font(22))
    for i, (label, p) in enumerate(tiles):
        x, y = 40 + (i % 4) * (W_ + 20), 200 + (i // 4) * (H_ + 50)
        d.text((x, y), label.upper(), fill=rl.INK, font=rl.font(22, True))
        sheet.paste(Image.open(p).convert("RGB"), (x, y + 30))
    y = 200 + 2 * (H_ + 50)
    d.text((40, y), "ICON (cooking)   -   the grey cube beside it is one 25 cm block", fill=rl.INK, font=rl.font(22, True))
    ic = Image.open(K.ICONS / "field_kitchen_icon.png").convert("RGBA")
    for k, bg in enumerate(((40, 42, 46, 255), (225, 228, 232, 255))):
        tile = Image.new("RGBA", ic.size, bg)
        tile.alpha_composite(ic)
        sheet.paste(tile.convert("RGB"), (40 + k * 280, y + 36))
    sheet.save(out / "field_kitchen_review.png")
    bpy.data.objects.remove(block)
    print(f"wrote {out / 'field_kitchen_review.png'}")
    W.reset(rig)


def rig_pose(rig, cooking):
    import math
    import working_kit as W
    W.reset(rig)
    objs = {k: v[0] for k, v in rig["_rest"].items()}
    objs["Fan"].rotation_euler.y = 0.4 * cooking
    objs["Needle"].rotation_euler.y = math.radians(90 if cooking else -150)
    for name in ("OvenGlow", "BurnerL", "BurnerR", "Pip"):
        W.lit(objs[name], cooking)


if __name__ == "__main__":
    S.run("field_kitchen", S.build_field_kitchen, half=(0.375, 0.375, 0.5), ortho=1.25, icon_target=(0, 0, 0),
          title=TITLE, lines=LINES)
    out = K.args_out()
    if out is not None:
        review_sheet(out)
