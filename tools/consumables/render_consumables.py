"""Review renders of consumables batches C2 and C3 (build_consumables.py): for each item a sheet with the model three-quarter
from the front and from the back, its icon on a dark and a light tile, and the item in hand (third person and first
person, as the game frames them); and for the drinks a strip of the drink clip, third and first person.

    python tools/consumables/render_consumables.py OUT_DIR [bloom_tea] [grazer_shake] [fertilizer] [grazer_milk] [grazer_steak] [--no-drink]

Cycles on the CPU; nothing is downloaded. Writes PNGs into OUT_DIR.
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import build_consumables as bc_items  # noqa: E402,I001  (puts the tool folders on the path)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

import build_characters as bc  # noqa: E402
import food_lib as fl  # noqa: E402
import render_roots_design as rrd  # noqa: E402

# The pose each item is held in (hold clip, first-person clip) and the clip that uses it.
HOLD = {"bloom_tea": (bc.clip_hold_drink, bc.clip_hold_drink_fp), "grazer_shake": (bc.clip_hold_drink, bc.clip_hold_drink_fp),
        "fertilizer": (bc.clip_hold_item, bc.clip_hold_item), "grazer_milk": (bc.clip_hold_drink, bc.clip_hold_drink_fp),
        "grazer_steak": (bc.clip_hold_item, bc.clip_hold_item)}
DRINKS = ("bloom_tea", "grazer_shake", "grazer_milk")
LOOK = Vector(bc.FP_LOOK)
VIEWS = {"3q": dict(view=(0.9, -1.4, 0.25), ortho=0.75, target=(-0.1, -0.15, 1.38)),
         "fp": dict(view=tuple(-LOOK), target=tuple(Vector(bc.FP_EYE) + LOOK), distance=1.0, ground=False, clip_start=0.05,
                    fov=75, aspect=16 / 9)}
DRINK_KEYS = (("hold", 0.0), ("up", 0.12), ("tip", 0.2), ("first gulp", 0.25), ("second gulp", 0.4), ("down", 0.58))


def character():
    bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "scrapper_male.glb"))
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    body = [o for o in sc.objects if o.type == "MESH" and "forearm_r" in o.vertex_groups]
    for o in sc.objects:
        if o.type == "MESH" and o not in body:
            o.hide_render = True
    return arm, body


def pose(arm, fn, t):
    base = bc.clip_idle(0.0)
    base.update(fn(t))
    bc.apply_pose(arm, bc.build_pose(base))
    bpy.context.view_layer.update()


def attach(arm, root, place):
    """Puts the held model (base at the origin) where the posed right hand holds it."""
    pb = arm.pose.bones["hand_r"]
    rest = arm.matrix_world @ pb.bone.matrix_local
    posed = arm.matrix_world @ pb.matrix
    if "base" not in root:
        root["base"] = [v for row in root.matrix_basis for v in row]
    from mathutils import Matrix
    base = Matrix([root["base"][i * 4:i * 4 + 4] for i in range(4)])
    root.matrix_world = posed @ rest.inverted() @ place @ base
    bpy.context.view_layer.update()


def first_person(body, on):
    for o in body:
        for mm in [mm for mm in o.modifiers if mm.name == "fp"]:
            o.modifiers.remove(mm)
        if on:
            rrd._forearms_only(o)


def shot(objs, path, vname, size=600, samples=32):
    kw = VIEWS[vname]
    fl.render_view(objs, path, kw["view"], ortho=kw.get("ortho"), target=kw["target"], size=size, samples=samples,
                   distance=kw.get("distance", 6.0), ground=kw.get("ground", True), clip_start=kw.get("clip_start", 0.01),
                   fov=kw.get("fov"), aspect=kw.get("aspect", 1.0))
    return path


def held_root(item):
    bpy.ops.import_scene.gltf(filepath=str(bc_items.MESHES / f"{item}_held.glb"))
    return next(o for o in bpy.context.selected_objects if o.parent is None)


def sheet(item, title, out):
    bc_items.bbe.reset_scene()
    root, objs, (lo, hi, tris) = bc_items.build(item)
    c = (lo + hi) / 2
    size = max(hi - lo)
    model = [fl.render_view(objs, out / f"{item}_front3q.png", (1, -1.4, 0.7), ortho=size * 1.3, target=tuple(c), size=700),
             fl.render_view(objs, out / f"{item}_back3q.png", (-1, 1.4, 0.6), ortho=size * 1.3, target=tuple(c), size=700)]
    fl.reset()
    arm, body = character()
    held = held_root(item)
    hold, hold_fp = HOLD[item]
    place = bc_items.placement(item)
    hands = []
    for vname, fn in (("3q", hold), ("fp", hold_fp)):
        first_person(body, vname == "fp")
        pose(arm, fn, 0.0)
        attach(arm, held, place)
        hands.append(shot(body + fl.tree(held), out / f"{item}_held_{vname}.png", vname))
    W, H = 2900, 1500
    im = Image.new("RGB", (W, H), (245, 245, 246))
    d = ImageDraw.Draw(im)
    F = bc_items.bm.font
    d.text((40, 24), f"{title.upper()}: design review (consumables batch {'C3' if item in ('grazer_milk', 'grazer_steak') else 'C2'})", fill=(30, 30, 34), font=F(44))
    d.text((40, 84), f"{item}.glb: {(hi - lo).x * 100:.1f} x {(hi - lo).y * 100:.1f} x {(hi - lo).z * 100:.1f} cm in the 25 cm "
           f"block, {tris} triangles", fill=(30, 30, 34), font=F(24, False))
    x = 40
    for p, label in zip(model, ("THREE-QUARTER", "BACK")):
        im.paste(Image.open(p).convert("RGB").resize((560, 560), Image.LANCZOS), (x, 150))
        d.text((x + 10, 158), label, fill=(30, 30, 34), font=F(22))
        x += 580
    icon = Image.open(bc_items.ICONS / f"{item}_icon.png").convert("RGBA")
    d.text((x, 130), "ICON", fill=(30, 30, 34), font=F(22))
    for k, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        im.paste(tile, (x, 160 + k * 290))
    x += 290
    im.paste(Image.open(hands[0]).convert("RGB").resize((560, 560), Image.LANCZOS), (x, 150))
    d.text((x + 10, 158), "IN HAND", fill=(30, 30, 34), font=F(22))
    # first person on its own row under the rest
    fp = Image.open(hands[1]).convert("RGB").resize((1240, 698), Image.LANCZOS)
    im.paste(fp, (40, 730))
    d.text((50, 740), "HELD, FIRST PERSON (as the game frames it: 75 degrees, 16:9)", fill=(250, 250, 250), font=F(22))
    im = im.crop((0, 0, W, 730 + 698 + 30))
    path = out / f"{item}_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def drink(item, out):
    fl.reset()
    arm, body = character()
    held = held_root(item)
    place = bc_items.placement(item)
    for vname in ("3q", "fp"):
        first_person(body, vname == "fp")
        files = []
        for label, t in DRINK_KEYS:
            fn = bc.clip_drink_fp if vname == "fp" else bc.clip_drink
            pose(arm, fn, t)
            attach(arm, held, place)
            files.append(shot(body + fl.tree(held), out / f"{item}_drink_{vname}_{label.replace(' ', '_')}.png", vname,
                              size=500, samples=24))
        fl.sheet(files, [f"{label}  ({t:.2f} s)" for label, t in DRINK_KEYS], out / f"{item}_drink_{vname}.png", 3,
                 f"{item.upper()}  -  drinking, " + ("third person" if vname == "3q" else "first person"))


def main(argv):
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    only = [a for a in argv[1:] if a in bc_items.ITEMS] or list(bc_items.ITEMS)
    for item in only:
        sheet(item, bc_items.ITEMS[item][0], out)
        if item in DRINKS and "--no-drink" not in argv:
            drink(item, out)


if __name__ == "__main__":
    main(sys.argv[1:])
