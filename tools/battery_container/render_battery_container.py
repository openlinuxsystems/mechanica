"""Review renders of the Battery Container (build_battery.py --render OUT_DIR).

Writes, into OUT_DIR: the container three-quarter from the front right and from the back left, a close-up of the front,
front and top views, the container beside the Gas Container, and review_sheet.png with them and the icon.
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image, ImageDraw

import render_lift as rl


def render_all(kit, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for view, loc, target, ortho in (("three_quarter", (1.55, -2.6, 1.05), (0, 0, -0.03), 1.45),
                                     ("back", (-1.75, 2.4, 1.05), (0, 0, -0.03), 1.45),
                                     ("window", (0.55, -1.9, 0.3), (0.1, -build.BY, -0.04), 0.66),
                                     ("front", (0, -3, 0), (0, 0, 0), 1.25),
                                     ("top", (0, -0.0001, 3), (0, 0, 0), 1.25)):
        cam = rl.camera(f"bc_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"battery_{view}.png", 700, 700)
    # beside the Gas Container, to check the two are told apart at a glance
    import build_gas_container as gc
    kit["root"].location = (0.6, 0, 0)
    gas = gc.build_container(build.make_materials())
    gas["root"].location = (-0.6, 0, 0)
    cam = rl.camera("bc_pair", (1.6, -3.4, 1.5), (0, 0, -0.05), ortho=2.4)
    shots["pair"] = rl.render(cam, out / "battery_pair.png", 900, 700)
    for o in [gas["root"], *gas["root"].children_recursive]:
        o.hide_render = True
    kit["root"].location = (0, 0, 0)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2200, 1560), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "BATTERY CONTAINER: design review", fill=INK, font=F(44, True))
    d.text((40, 82), "4 x 3 x 3 blocks like the Gas Container, told apart by a mustard lid with big copper terminals and a red breaker, "
           "a wide five-cell charge gauge (3 of 5 lit) and lightning-bolt plate, vent slots, and cooling fins on the ends.", fill=INK, font=F(22))
    for i, view in enumerate(("three_quarter", "back", "window")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((680, 680), Image.LANCZOS), (40 + i * 710, 130))
        d.text((54 + i * 710, 140), {"three_quarter": "THREE-QUARTER", "back": "BACK", "window": "FRONT CLOSE-UP"}[view], fill=INK,
               font=F(24, True))
    y = 840
    for i, view in enumerate(("front", "top")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((420, 420), Image.LANCZOS), (40 + i * 440, y))
        d.text((52 + i * 440, y + 8), view.upper(), fill=INK, font=F(22, True))
    sheet.paste(Image.open(shots["pair"]).convert("RGB").resize((700, 544), Image.LANCZOS), (940, y))
    d.text((952, y + 8), "BESIDE THE GAS CONTAINER (LEFT)", fill=INK, font=F(22, True))
    icon = Image.open(build.ICONS / "battery_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        sheet.paste(tile, (1680, y + i * 280))
    d.text((1680, y - 30), "ICON", fill=INK, font=F(22, True))
    sheet.save(out / "review_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
