"""Build the game files of the crops from crops batch K3 on (crops map): Beetroot, Banana and Chili (K3), Blueberry,
Orange, Broccoli and Pineapple (K4), as build_roots.py does the carrot's and the potato's.

For each crop (``beetroot``, ``banana``, ``chili``, ``blueberry``, ``orange``, ``broccoli``, ``pineapple``) it writes, procedurally (no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/<crop>.glb`` - the crop on the ground or placed, lying, origin at the middle of its base.
* ``mods/vanilla/meshes/items/<crop>_held.glb`` - in the hand: ``eat_0`` (whole), ``eat_1`` and ``eat_2`` (after the
  first and second bite), which the game swaps while the eat gesture plays.
* ``mods/vanilla/meshes/items/<crop>_seed.glb`` - the shared seed packet with the crop's label.
* ``mods/vanilla/meshes/crops/<crop>_plant.glb`` - the plant with its ``grow``, ``wilt`` and ``sway`` clips
  (crop_rig.py), stake and crop tag included.
* ``mods/vanilla/textures/items/<crop>_icon.png`` and ``<crop>_seed_icon.png`` - 256 x 256 icons.
* the ``held`` entries for <crop>.json and <crop>_seed.json, printed (they go in the consumables batch).

    pip install bpy==5.2.2 numpy pillow
    python tools/food/build_crops.py [crop ...]                                   # the game files (every crop when none is named)
    python tools/food/build_crops.py [crop ...] --render OUT_DIR                  # also review sheets: the crop (three-quarter,
                                                                                # back, icon, bites, in hand, first person),
                                                                                # its packet and the plant's stages and wilt

The outputs are committed; re-run this script after changing it and commit the result.
"""

from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))
sys.path.insert(0, str(HERE.parent / "consumables"))

import food_lib as fl  # noqa: E402,I001  (first: it imports bpy, which makes bmesh and mathutils importable)
import banana  # noqa: E402
import beetroot  # noqa: E402
import blueberry  # noqa: E402
import broccoli  # noqa: E402
import build_tomato as bt  # noqa: E402
import chili  # noqa: E402
import crop_rig  # noqa: E402
import orange  # noqa: E402
import pineapple  # noqa: E402
import seed_packet as sp  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

CROPS = {"beetroot": beetroot, "banana": banana, "chili": chili,
         "blueberry": blueberry, "orange": orange, "broccoli": broccoli, "pineapple": pineapple}
NAMES = {"beetroot": "Beetroot", "banana": "Banana", "chili": "Chili",
         "blueberry": "Blueberry", "orange": "Orange", "broccoli": "Broccoli", "pineapple": "Pineapple"}
BATCH = {"beetroot": "K3", "banana": "K3", "chili": "K3", "blueberry": "K4", "orange": "K4", "broccoli": "K4", "pineapple": "K4"}

# How each lies in the right palm (render_roots_design.hold_matrix's frame): the lying model's +X and +Y along these hand
# axes, its base on the palm, at (along d, along w, off the palm) from the grip point. The mouth meets the palm about
# (d -0.026, w -0.01, n +0.059) from the grip, so the bitten side or end goes there: the beetroot's bitten side toward the
# wrist like the potato, the banana and the chili across the palm with their bitten tips past the little finger, like
# the carrot. Batch K4: the blueberry and the orange sit in the palm with their bitten side toward the wrist like the
# beetroot; the broccoli and the pineapple lie across the palm, the bitten dome or base past the little finger.
HOLD = {"beetroot": (("-d", "w"), (0.018, -0.02, 0.012)),
        "banana": (("-w", "d"), (0.004, banana.L / 2 - 0.06, 0.012)),
        "chili": (("-w", "d"), (0.004, chili.L / 2 - 0.045, 0.012)),
        "blueberry": (("-d", "w"), (0.012, -0.012, 0.006)),
        "orange": (("-d", "w"), (0.014, -0.014, 0.006)),
        "broccoli": (("-w", "d"), (0.004, 0.0, 0.01)),
        "pineapple": (("-w", "d"), (0.004, 0.02, 0.01))}


def hold_matrix(crop):
    """Rest-pose (Blender world) placement of the crop's lying model in the right palm."""
    import build_characters as bc
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    frame = {"d": d, "w": w, "n": n, "-d": -d, "-w": -w}
    axes, (a, b, c) = HOLD[crop]
    x = frame[axes[0]]
    z = n
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(bc.grip_point("r")) + d * a + w * b + n * c
    return m


def centred(root):
    """The crop as ``build`` lays it, centred on its footprint (base at z = 0)."""
    import bpy
    bpy.context.view_layer.update()
    c, _ = fl.bounds(fl.tree(root))
    root.location.x -= c.x
    root.location.y -= c.y
    bpy.context.view_layer.update()
    return root


def label_path(crop):
    label = Path(tempfile.mkdtemp()) / f"label_{crop}.png"
    sp.label_image(bt.ICONS / f"{crop}_icon.png", CROPS[crop].LABEL_COLOR, label)
    return label


def build(crop):
    mod = CROPS[crop]
    fl.reset()
    whole = centred(mod.build(NAMES[crop]))
    bt.export(fl.tree(whole), bt.MESHES / "items" / f"{crop}.glb")
    fl.auto_icon(fl.tree(whole), bt.ICONS / f"{crop}_icon.png", bt.ICON_VIEW, fill=1.12)

    fl.reset()
    held = fl.empty(NAMES[crop] + "Held")
    shift = None
    for k in range(3):
        stage = mod.build(f"eat_{k}", bites=k)
        if shift is None:
            before = stage.location.copy()
            shift = centred(stage).location - before
        else:
            stage.location += shift     # every stage where the whole crop is, so the bites do not move it in the hand
        stage.parent = held
    bt.export(fl.tree(held), bt.MESHES / "items" / f"{crop}_held.glb")

    fl.reset()
    label = label_path(crop)
    packet = sp.build(NAMES[crop] + "Seed", label)
    bt.export(fl.tree(packet), bt.MESHES / "items" / f"{crop}_seed.glb")
    fl.auto_icon(fl.tree(packet), bt.ICONS / f"{crop}_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)

    fl.reset()
    rig = mod.build_rig(label)
    crop_rig.bake(rig)
    bt.export(fl.tree(rig.root), bt.MESHES / "crops" / f"{crop}_plant.glb", animations=True)

    print(f"held {crop}:", json.dumps(bt.held_entry(hold_matrix(crop))))
    print(f"held {crop}_seed:", json.dumps(bt.held_entry(bt.packet_placement())))


# ---------------------------------------------------------------------------------------------------------------------
# Review sheets

def _font(size, bold=True):
    from PIL import ImageFont
    p = Path("/usr/share/fonts/truetype/dejavu") / ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf")
    return ImageFont.truetype(str(p), size) if p.exists() else ImageFont.load_default()


def render_sheet(crop, out):
    """The crop: three-quarter, back, icon on dark and light, the bite stages, in hand and in first person."""
    import bpy
    import build_characters as bc
    import render_consumables as rc
    from PIL import Image, ImageDraw
    mod = CROPS[crop]
    fl.reset()
    objs = fl.tree(centred(mod.build(NAMES[crop])))
    c, size = fl.bounds(objs)
    tris = sum(len(o.data.polygons) for o in objs if o.type == "MESH")
    model = [fl.render_view(objs, out / f"{crop}_front3q.png", (1, -1.4, 0.8), ortho=size * 1.3, target=tuple(c), size=700),
             fl.render_view(objs, out / f"{crop}_back.png", (-1, 1.4, 0.6), ortho=size * 1.3, target=tuple(c), size=700)]
    stages = []
    fl.reset()
    held = rc.held_root(crop)
    hobjs = fl.tree(held)
    for k in range(3):
        for ch in held.children:
            for o in fl.tree(ch):
                o.hide_render = ch.name != f"eat_{k}"
        shown = [o for o in hobjs if not o.hide_render]
        stages.append(fl.render_view(shown, out / f"{crop}_eat_{k}.png", (1.3, -0.7, 1.0), ortho=size * 1.3, target=tuple(c),
                                     size=500))
    fl.reset()
    arm, body = rc.character()
    held = rc.held_root(crop)
    for ch in held.children:
        for o in fl.tree(ch):
            o.hide_render = ch.name != "eat_0"
    hands = []
    for vname, fn in (("3q", bc.clip_hold_food), ("fp", bc.clip_hold_food_fp)):
        rc.first_person(body, vname == "fp")
        rc.pose(arm, fn, 0.0)
        rc.attach(arm, held, hold_matrix(crop))
        hands.append(rc.shot(body + [o for o in fl.tree(held) if not o.hide_render], out / f"{crop}_held_{vname}.png", vname))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    W, H = 2900, 1540
    im = Image.new("RGB", (W, H), (245, 245, 246))
    d = ImageDraw.Draw(im)
    d.text((40, 24), f"{NAMES[crop].upper()}: design review (crops batch {BATCH[crop]})", fill=(30, 30, 34), font=_font(44))
    d.text((40, 84), f"{crop}.glb: about {size * 100:.1f} cm at most across, {tris} faces; held model {crop}_held.glb (eat_0..2)",
           fill=(30, 30, 34), font=_font(24, False))
    x = 40
    for p, label in zip(model, ("THREE-QUARTER", "BACK")):
        im.paste(Image.open(p).convert("RGB").resize((560, 560)), (x, 150))
        d.text((x + 10, 158), label, fill=(30, 30, 34), font=_font(22))
        x += 580
    icon = Image.open(bt.ICONS / f"{crop}_icon.png").convert("RGBA")
    d.text((x, 130), "ICON", fill=(30, 30, 34), font=_font(22))
    for k, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        im.paste(tile, (x, 160 + k * 290))
    x += 290
    im.paste(Image.open(hands[0]).convert("RGB").resize((560, 560)), (x, 150))
    d.text((x + 10, 158), "IN HAND", fill=(30, 30, 34), font=_font(22))
    im.paste(Image.open(hands[1]).convert("RGB").resize((1240, 698)), (40, 760))
    d.text((50, 770), "HELD, FIRST PERSON (as the game frames it: 75 degrees, 16:9)", fill=(250, 250, 250), font=_font(22))
    for k, p in enumerate(stages):
        xx = 1320 + k * 520
        im.paste(Image.open(p).convert("RGB").resize((500, 500)), (xx, 790))
        d.text((xx + 10, 760), ("WHOLE (eat_0)", "ONE BITE (eat_1)", "TWO BITES (eat_2)")[k], fill=(30, 30, 34), font=_font(22))
    path = out / f"{crop}_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def render_plant(crop, out):
    """The plant on a soil mound at stages 0 to 4, wilted on dry soil, and close up ripe."""
    from PIL import Image, ImageDraw
    mod = CROPS[crop]
    fl.reset()
    rig = mod.build_rig(label_path(crop))
    soil = fl.soil()
    objs = fl.tree(rig.root) + soil
    files = []
    for g in (0.0, 1.0, 2.0, 3.0, 4.0):
        crop_rig.pose(rig, g)
        files.append(fl.render_view(objs, out / f"{crop}_stage{int(g)}.png", (1.0, -1.6, 0.9), ortho=1.0, target=(0, 0, 0.32),
                                    size=700))
    crop_rig.pose(rig, 4.0, 1.0)
    for o in soil:
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(fl.material("soil_dry", "#8A6E52", rough=0.95))
    files.append(fl.render_view(objs, out / f"{crop}_wilted.png", (1.0, -1.6, 0.9), ortho=1.0, target=(0, 0, 0.32), size=700))
    labels = ["0  planted", "1", "2", "3", "4  ripe", "ripe, dry soil: wilted"]
    im = Image.new("RGB", (3 * 720 + 40, 2 * 760 + 90), (245, 245, 246))
    d = ImageDraw.Draw(im)
    d.text((20, 18), f"{NAMES[crop].upper()} PLANT: grow stages on a soil mound (0.75 m), stake and tag", fill=(30, 30, 34), font=_font(36))
    for i, (f, lab) in enumerate(zip(files, labels)):
        x, y = 20 + (i % 3) * 720, 80 + (i // 3) * 760
        im.paste(Image.open(f).convert("RGB"), (x, y))
        d.text((x + 10, y + 706), lab, fill=(30, 30, 34), font=_font(26, False))
    path = out / f"{crop}_plant_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def render_packets(crops, out):
    """The seed packets in a row with the older crops', and their icons."""
    fl.reset()
    row = []
    older = {"tomato": bt.LABEL_COLOR, "carrot": "#E0B530", "potato": "#7A4A30"}
    names = list(older) + list(crops)
    for k, crop in enumerate(names):
        label = Path(tempfile.mkdtemp()) / f"label_{crop}.png"
        sp.label_image(bt.ICONS / f"{crop}_icon.png", older.get(crop) or CROPS[crop].LABEL_COLOR, label)
        row += fl.tree(sp.build(crop + "Pk", label, location=(k * 0.11, 0, 0), seed=3 + k))
    mid = (len(names) - 1) * 0.11 / 2
    packets = fl.render_view(row, out / "packets_row.png", (0.5, -1.6, 0.55), ortho=0.11 * len(names) + 0.06, target=(mid, 0, 0.06),
                             size=1400)
    compose_packets(crops, packets, out)


def compose_packets(crops, packets, out):
    """The packets sheet: the band of the row render that holds the packets, then each new packet's icon on dark and light."""
    from PIL import Image, ImageDraw
    band = Image.open(packets).convert("RGB").crop((0, 470, 1400, 930))
    width = max(1400, 40 + len(crops) * 560)
    im = Image.new("RGB", (width, 70 + band.height + 340), (245, 245, 246))
    im.paste(band, ((width - band.width) // 2, 70))
    d = ImageDraw.Draw(im)
    d.text((20, 20), "SEED PACKETS: tomato, carrot, potato (for comparison), then " + ", ".join(crops), fill=(30, 30, 34), font=_font(28))
    top = 70 + band.height + 20
    for k, crop in enumerate(crops):
        ic = Image.open(bt.ICONS / f"{crop}_seed_icon.png").convert("RGBA")
        for j, bg in enumerate(((40, 42, 46), (226, 228, 230))):
            tile = Image.new("RGB", (256, 256), bg)
            tile.paste(ic, (0, 0), ic)
            im.paste(tile, (40 + k * 560 + j * 270, top))
        d.text((40 + k * 560, top + 266), f"{crop}_seed icon", fill=(30, 30, 34), font=_font(24, False))
    path = out / "packets_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def main(argv):
    out = Path(argv[argv.index("--render") + 1]) if "--render" in argv else None
    crops = [a for a in argv if a in CROPS] or list(CROPS)
    for crop in crops:
        build(crop)
    if out:
        out.mkdir(parents=True, exist_ok=True)
        for crop in crops:
            render_sheet(crop, out)
            render_plant(crop, out)
        render_packets(crops, out)


if __name__ == "__main__":
    main(sys.argv[1:])
