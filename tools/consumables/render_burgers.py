"""Review renders of the Field Kitchen's burgers (consumables batch C4, build_burgers.py): for each a sheet with the model
three-quarter from the front and from the back, its icon on a dark and a light tile, the burger held on the palm (third
and first person, as the game frames them) and its three bite stages (eat_0, eat_1, eat_2 from the held model).

    python tools/consumables/render_burgers.py OUT_DIR [garden_burger] [colony_burger]

Cycles on the CPU; nothing is downloaded. Writes PNGs into OUT_DIR.
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import build_burgers as bb  # noqa: E402,I001  (puts the tool folders on the path)
import bpy  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

import build_characters as bc  # noqa: E402
import burger  # noqa: E402
import food_lib as fl  # noqa: E402
import render_consumables as rc  # noqa: E402


def font(size, bold=True):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",):
        p = Path("/usr/share/fonts/truetype/dejavu") / name
        if p.exists():
            from PIL import ImageFont
            return ImageFont.truetype(str(p), size)
    from PIL import ImageFont
    return ImageFont.load_default()


def sheet(item, out):
    fl.reset()
    root = burger.build(item)
    objs = fl.tree(root)
    c, size = fl.bounds(objs)
    tris = sum(len(o.data.polygons) for o in objs if o.type == "MESH")
    model = [fl.render_view(objs, out / f"{item}_front3q.png", (1, -1.4, 0.7), ortho=size * 1.3, target=tuple(c), size=700),
             fl.render_view(objs, out / f"{item}_back3q.png", (-1, 1.4, 0.6), ortho=size * 1.3, target=tuple(c), size=700)]
    # the bite stages, from the +X side (the bitten edge) and above
    stages = []
    for k in range(3):
        fl.reset()
        s = burger.build(item, f"eat_{k}", bites=k)
        so = fl.tree(s)
        stages.append(fl.render_view(so, out / f"{item}_eat_{k}.png", (1.5, -0.6, 1.1), ortho=size * 1.3, target=tuple(c), size=500))
    fl.reset()
    arm, body = rc.character()
    held = rc.held_root(item)
    for ch in held.children:                                   # in hand it is whole until the first bite
        for o in fl.tree(ch):
            o.hide_render = ch.name != "eat_0"
    place = bb.placement(item)
    hands = []
    for vname, fn in (("3q", bc.clip_hold_food), ("fp", bc.clip_hold_food_fp)):
        rc.first_person(body, vname == "fp")
        rc.pose(arm, fn, 0.0)
        rc.attach(arm, held, place)
        hands.append(rc.shot(body + fl.tree(held), out / f"{item}_held_{vname}.png", vname))
    W, H = 2900, 1540
    im = Image.new("RGB", (W, H), (245, 245, 246))
    d = ImageDraw.Draw(im)
    d.text((40, 24), f"{bb.ITEMS[item].upper()}: design review (consumables batch C4)", fill=(30, 30, 34), font=font(44))
    d.text((40, 84), f"{item}.glb: about {size * 100:.1f} cm at most across, {tris} faces; a heal-only food (30) cooked at the Field Kitchen",
           fill=(30, 30, 34), font=font(24, False))
    x = 40
    for p, label in zip(model, ("THREE-QUARTER", "BACK")):
        im.paste(Image.open(p).convert("RGB").resize((560, 560), Image.LANCZOS), (x, 150))
        d.text((x + 10, 158), label, fill=(30, 30, 34), font=font(22))
        x += 580
    icon = Image.open(bb.ICONS / f"{item}_icon.png").convert("RGBA")
    d.text((x, 130), "ICON", fill=(30, 30, 34), font=font(22))
    for k, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        im.paste(tile, (x, 160 + k * 290))
    x += 290
    im.paste(Image.open(hands[0]).convert("RGB").resize((560, 560), Image.LANCZOS), (x, 150))
    d.text((x + 10, 158), "IN HAND", fill=(30, 30, 34), font=font(22))
    fp = Image.open(hands[1]).convert("RGB").resize((1240, 698), Image.LANCZOS)
    im.paste(fp, (40, 760))
    d.text((50, 770), "HELD, FIRST PERSON (as the game frames it: 75 degrees, 16:9)", fill=(250, 250, 250), font=font(22))
    for k, p in enumerate(stages):
        xx = 1320 + k * 520
        im.paste(Image.open(p).convert("RGB").resize((500, 500), Image.LANCZOS), (xx, 790))
        d.text((xx + 10, 760), ("WHOLE (eat_0)", "ONE BITE (eat_1)", "TWO BITES (eat_2)")[k], fill=(30, 30, 34), font=font(22))
    path = out / f"{item}_sheet.png"
    im.save(path)
    print(f"wrote {path}")


def main(argv):
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    for item in [a for a in argv[1:] if a in bb.ITEMS] or list(bb.ITEMS):
        sheet(item, out)


if __name__ == "__main__":
    main(sys.argv[1:])
