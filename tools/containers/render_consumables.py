"""Review renders of the Chemical, Water and Potato Ammo Containers (build_consumables.py --render OUT_DIR).

Writes, into OUT_DIR, for each container: three-quarter, back three-quarter, front, top and end views and a gauge
close-up; then the five consumable containers side by side (Gas, Battery, Chemical, Water, Potato Ammo, the first two
built by their own scripts); and <id>_review_sheet.png for each with its views, its icon and the line-up.
Cycles on the CPU; LIFT_SAMPLES sets the samples, CONSUMABLES_REUSE=1 keeps renders already in OUT_DIR.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import bpy  # noqa: F401
from PIL import Image, ImageDraw

import render_lift as rl

TITLES = {
    "chemical_container": ("CHEMICAL CONTAINER: design review",
                           "4 x 3 x 3 blocks, holds 5 stacks of chemicals. A sealed drum in the family case: violet lid with a bolted manway "
                           "and relief valve, violet end frames, a chemical hazard plate and a round sight tube with the violet column (3 of 5)."),
    "water_container": ("WATER CONTAINER: design review",
                        "4 x 3 x 3 blocks, holds 5 full water buckets. Blue lid with a big ribbed filler cap, blue end frames, a copper porthole "
                        "gauge with the blue water level (3 of 5), a water-drop plate, and a gate-valve tap with a blue hand wheel on -X."),
    "potato_ammo_container": ("POTATO AMMO CONTAINER: design review",
                              "4 x 3 x 3 blocks, holds 5 stacks of potatoes. An ammo box: a hinged lid with a mustard SPUD AMMO stencil band, "
                              "copper lever latches on the front, hinges on the back, carry handles on both ends and a window full of potatoes."),
}
GAUGE = {"chemical_container": lambda b: (b.Chem.TUBE_X, -b.Chem.BY, -0.04),
         "water_container": lambda b: (b.Water.PORT_X, -b.Water.BY, b.Water.PORT_Z),
         "potato_ammo_container": lambda b: (b.Potato.WIN_X - 0.05, -b.Potato.BY, 0.0)}


REUSE = os.environ.get("CONSUMABLES_REUSE") == "1"   # lay the sheets out again from renders already in OUT_DIR


def shoot(cam, path, w, h):
    return path if REUSE and path.exists() else rl.render(cam, path, w, h)


def show(objs, on):
    for o in objs:
        o.hide_render = not on


def render_all(roots, mats, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    trees = {cid: build.tree(r) for cid, r in roots.items()}
    shots = {}
    for cid in roots:
        for other, t in trees.items():
            show(t, other == cid)
        s = shots[cid] = {}
        g = GAUGE[cid](build)
        for view, loc, target, ortho, w, h in (
                ("three_quarter", (1.55, -2.6, 1.05), (0, 0, -0.03), 1.45, 800, 800),
                ("back", (-1.75, 2.4, 1.05), (0, 0, -0.03), 1.45, 800, 800),
                ("front", (0, -3, 0), (0, 0, 0), 1.2, 800, 640),
                ("top", (0, -0.0001, 3), (0, 0, 0), 1.2, 800, 640),
                ("end", (3, 0, 0), (0, 0, 0), 0.95, 640, 640),
                ("gauge", (g[0] + 0.55, -1.9, g[2] + 0.3), g, 0.62, 640, 640)):
            cam = rl.camera(f"{cid}_{view}", loc, target, ortho=ortho)
            s[view] = shoot(cam, out / f"{cid}_{view}.png", w, h)
    # the five side by side: Gas, Battery, Chemical, Water, Potato Ammo
    sys.path.insert(0, str(build.HERE.parent / "battery_container"))
    import build_battery_container as bc
    gas = build.gc.build_container(mats)["root"]
    bat = bc.build_battery(bc.make_materials())["root"]
    row = [("Gas", gas), ("Battery", bat), ("Chemical", roots["chemical_container"]), ("Water", roots["water_container"]),
           ("Potato Ammo", roots["potato_ammo_container"])]
    for i, (_, r) in enumerate(row):
        r.location = ((i - 2) * 1.2, 0, 0)
        show(build.tree(r), True)
    cam = rl.camera("lineup", (2.2, -6.2, 2.6), (0, 0, -0.1), ortho=6.4)
    lineup = shoot(cam, out / "consumables_lineup.png", 2000, 800)
    labels = [(name, rl.project(cam, (r.location.x, -0.45, -0.375), 2000, 800)) for name, r in row]
    for _, r in row:
        r.location = (0, 0, 0)
    show(build.tree(gas) + build.tree(bat), False)

    F, INK = rl.font, rl.INK
    for cid in roots:
        s = shots[cid]
        sheet = Image.new("RGB", (2560, 2470), (246, 246, 244))
        d = ImageDraw.Draw(sheet)
        title, desc = TITLES[cid]
        d.text((40, 24), title, fill=INK, font=F(44, True))
        d.text((40, 82), desc, fill=INK, font=F(21))
        for i, view in enumerate(("three_quarter", "back", "gauge")):
            x = 40 + i * 830
            im = Image.open(s[view]).convert("RGB").resize((800, 800), Image.LANCZOS)
            sheet.paste(im, (x, 130))
            d.text((x + 14, 140), {"three_quarter": "THREE-QUARTER (FRONT, +X ON THE RIGHT)", "back": "BACK THREE-QUARTER",
                                   "gauge": "GAUGE CLOSE-UP"}[view], fill=INK, font=F(24, True))
        y = 960
        x = 40
        for view in ("front", "top", "end"):
            im = Image.open(s[view]).convert("RGB")
            im = im.resize((int(im.width * 0.62), int(im.height * 0.62)), Image.LANCZOS)
            sheet.paste(im, (x, y))
            d.text((x + 12, y + 8), view.upper(), fill=INK, font=F(22, True))
            x += im.width + 24
        icon = Image.open(build.ICONS / f"{cid}_icon.png").convert("RGBA")
        d.text((x, y - 2), "ICON (256 px)", fill=INK, font=F(22, True))
        for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
            tile = Image.new("RGB", (256, 256), bg)
            tile.paste(icon, (0, 0), icon)
            sheet.paste(tile, (x + i * 276, y + 30))
        y = 1400
        d.text((40, y), "THE FIVE CONSUMABLE CONTAINERS", fill=INK, font=F(26, True))
        im = Image.open(lineup).convert("RGB").resize((2480, 992), Image.LANCZOS)
        sheet.paste(im, (40, y + 40))
        dd = ImageDraw.Draw(sheet)
        for name, (px, py) in labels:
            lx, ly = 40 + px * 1.24, y + 40 + 992 - 70      # under each one, in a row along the bottom
            tw = dd.textlength(name, font=F(26, True))
            dd.text((lx - tw / 2, ly), name, fill=INK, font=F(26, True))
        sheet.save(out / f"{cid}_review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheets into {out}")
