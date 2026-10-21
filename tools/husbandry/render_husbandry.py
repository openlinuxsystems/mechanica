"""Review renders of the Feeding Trough and the Lead (build_trough.py / build_lead.py --render OUT_DIR).

Writes the views into OUT_DIR and a review sheet with them and the icon (trough_sheet.png, lead_sheet.png).
Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

import render_lift as rl


def _icons(sheet, icon_path, x, y):
    icon = Image.open(icon_path).convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        sheet.paste(tile, (x, y + i * 280))


def render_trough(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for view, loc, target, ortho in (("three_quarter", (1.4, -2.6, 1.6), (0, 0, -0.05), 1.85),
                                     ("back", (-1.7, 2.4, 1.3), (0, 0, -0.05), 1.85),
                                     ("end", (-3, -0.0001, 0.4), (0, 0, -0.03), 1.0),
                                     ("top", (0, -0.0001, 3), (0, 0, 0), 1.7)):
        cam = rl.camera(f"ft_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"trough_{view}.png", 800, 700)
    for level in range(build.LEVELS + 1):
        build.set_fill(rig, level / build.LEVELS)
        cam = rl.camera(f"ft_fill{level}", (0.6, -2.2, 2.0), (0, 0, -0.05), ortho=1.7)
        shots[f"fill{level}"] = rl.render(cam, out / f"trough_fill{level}.png", 600, 500)
    build.set_fill(rig, build.SHOWN / build.LEVELS)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2400, 1500), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "FEEDING TROUGH: game model", fill=INK, font=F(44, True))
    d.text((40, 82), "6 x 3 x 2 blocks. Gunmetal frame with copper caps and a cyan window round a slatted wooden tub. "
           "The feed shows how full it is in four steps (empty, then 1 to 4).", fill=INK, font=F(22))
    for i, view in enumerate(("three_quarter", "back", "end")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((720, 630), Image.LANCZOS), (40 + i * 750, 130))
        d.text((54 + i * 750, 140), view.replace("_", "-").upper(), fill=INK, font=F(24, True))
    y = 800
    for level in range(build.LEVELS + 1):
        sheet.paste(Image.open(shots[f"fill{level}"]).convert("RGB").resize((380, 317), Image.LANCZOS), (40 + level * 400, y))
        d.text((52 + level * 400, y + 8), "EMPTY" if level == 0 else f"FILL {level}/{build.LEVELS}", fill=INK, font=F(22, True))
    d.text((2080, y - 30), "ICON", fill=INK, font=F(22, True))
    _icons(sheet, build.ICONS / "feeding_trough_icon.png", 2080, y)
    sheet.save(out / "trough_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")


def render_lead(rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = build.FLOOR
    shots = {}
    for view, loc, target, ortho in (("front", (0.5, -2.4, 0.3), (0, 0, -0.22), 0.56),
                                     ("side", (2.4, 0.3, 0.2), (0, 0, -0.22), 0.56),
                                     ("grip", (0.4, -1.2, 0.5), (0, 0, -0.01), 0.18),
                                     ("hook", (0.5, -1.4, -0.2), (0.0, 0, -0.38), 0.14)):
        cam = rl.camera(f"ld_{view}", loc, target, ortho=ortho)
        shots[view] = rl.render(cam, out / f"lead_{view}.png", 700, 700)

    F, INK = rl.font, rl.INK
    sheet = Image.new("RGB", (2400, 900), (246, 246, 244))
    d = ImageDraw.Draw(sheet)
    d.text((40, 24), "THE LEAD: game model", fill=INK, font=F(44, True))
    d.text((40, 82), "A coil of twisted rope tied off on a yellow grip (held in the palm), with a copper swivel and a gunmetal snap hook.",
           fill=INK, font=F(22))
    for i, view in enumerate(("front", "side", "grip", "hook")):
        sheet.paste(Image.open(shots[view]).convert("RGB").resize((500, 500), Image.LANCZOS), (40 + i * 520, 130))
        d.text((54 + i * 520, 140), view.upper(), fill=INK, font=F(24, True))
    d.text((40, 650), "ICON", fill=INK, font=F(22, True))
    icon = Image.open(build.ICONS / "lead_icon.png").convert("RGBA")
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (200, 200), bg)
        tile.paste(icon.resize((200, 200), Image.LANCZOS), (0, 0), icon.resize((200, 200), Image.LANCZOS))
        sheet.paste(tile, (40 + i * 220, 680))
    sheet.save(out / "lead_sheet.png")
    ground.hide_render = True
    print(f"rendered the review sheet into {out}")
