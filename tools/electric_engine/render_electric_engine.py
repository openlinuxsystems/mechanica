"""Review renders of the Electric Engine's five levels (build_electric_engine.py --render OUT_DIR).

Writes, into OUT_DIR: each level three-quarter from the front right as the references show it (lit as its icon), the
back left of levels 1 and 5, level 5 in a few frames of its start, run and gear clips, and review_sheet.png with the
five levels side by side over their icons. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

from pathlib import Path

import bpy
from PIL import Image

import render_lift as rl


def render_all(rigs, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    ground.location.z = -0.25
    sc = bpy.context.scene
    shots = []
    for rig in rigs:
        x = rig["root"].location.x
        cam = rl.camera(f"level{rig['level']}", (x - 1.25, 1.65, 0.95), (x, 0, -0.02), ortho=0.86)
        shots.append(rl.render(cam, out / f"electric_engine_{rig['level']}.png", 800, 800))
    for rig in (rigs[0], rigs[-1]):
        x = rig["root"].location.x
        cam = rl.camera(f"back{rig['level']}", (x + 1.35, -1.55, 0.95), (x, 0, -0.02), ortho=0.86)
        rl.render(cam, out / f"electric_engine_{rig['level']}_back.png", 800, 800)
    # clip frames of level 5: NLA on, one track at a time
    rig = rigs[-1]
    x = rig["root"].location.x
    cam = rl.camera("clip", (x - 1.25, 1.65, 0.95), (x, 0, -0.02), ortho=0.86)
    objs = [*build.clip_objects(rig), rig["slider"], rig["fill"], *rig["heat"]]
    for clip, times, heat in (("start", (0.1, 0.35), "heat"), ("run_hard_loop", (0.6,), "heat"), ("gear", (0.05,), "heat")):
        for ob in objs:
            ob.animation_data.use_nla = True
            for track in ob.animation_data.nla_tracks:
                track.mute = track.name not in (clip, heat, "lever")
        for t in times:
            sc.frame_set(int(round(t * build.FPS)))
            rl.render(cam, out / f"electric_engine_5_{clip}_{t:.2f}.png", 600, 600)
    for ob in objs:
        ob.animation_data.use_nla = False
    sheet = Image.new("RGB", (800 * 5, 800 + 256), (255, 255, 255))
    for i, p in enumerate(shots):
        sheet.paste(Image.open(p).convert("RGB"), (i * 800, 0))
        icon = Image.open(build.ICONS / f"electric_engine_{i + 1}_icon.png").convert("RGBA")
        sheet.paste(icon, (i * 800 + 272, 800), icon)
    sheet.save(out / "review_sheet.png")
