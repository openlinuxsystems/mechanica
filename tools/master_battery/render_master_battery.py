"""Review renders of the Master Battery (run through ``build_master_battery.py --render OUT_DIR``).

Writes, into OUT_DIR: the cell (three-quarter front and back, with a 1.5 m figure for scale), the fallen cell in the
grass, the Power Core in the ship's engineering room empty, at stages of the install clip and powered, and
review_sheet.png laying them out. ``MB_VIDEO=1`` also renders the install clip's frames (bay_install_NNN.png, MB_VIDEO_FPS, default 12:
one second empty, the clip, one second powered) for ffmpeg. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lift"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "structures"))
import render_lift as rl  # noqa: E402


def cell_shots(out, build):
    build.bbe.reset_scene()
    mats = build.make_materials()
    root = build.bl.make_empty("Cell", location=(0, 0, -build.Z0 + 0.008))
    build.bl.make_object("Case", build.build_cell(), mats, root)
    build.bl.make_object("Charge", build.build_charge_glow(), mats, root)
    rl.setup_world()
    rl.silhouette(0.75, 0.25)
    shots = []
    for name, loc, target, lens in (("cell_front", (1.5, 1.9, 1.15), (0.2, 0.0, 0.45), 40),
                                    ("cell_back", (-1.2, -1.6, 0.9), (0, 0, 0.22), 55)):
        cam = rl.camera(name, loc, target, lens=lens)
        shots.append((name, rl.render(cam, out / f"{name}.png", 900, 700)))
    return shots


def fallen_shots(out, build):
    build.bbe.reset_scene()
    mats = build.make_materials()
    rig = build.build_fallen(mats)
    build.pose_fallen_idle(rig, 0.3)
    rl.setup_world()
    ground = bpy.data.objects["RenderOnlyGround"]
    m = ground.data.materials[0].node_tree.nodes["Principled BSDF"]
    m.inputs["Base Color"].default_value = (0.11, 0.24, 0.05, 1)
    cam = rl.camera("fallen", (1.8, 2.4, 1.4), (0.0, -0.4, 0.15), lens=35)
    return [("fallen", rl.render(cam, out / "fallen.png", 900, 700))]


def bay_scene(build):
    """The review ship (structure kit materials) with the bay in its engineering room; returns the bay rig."""
    import kit as K  # noqa: PLC0415
    import ship as S  # noqa: PLC0415
    bpy.ops.wm.read_factory_settings(use_empty=True)
    K._mats.clear()
    K.setup_render(int(os.environ.get("LIFT_SAMPLES", "48")))
    K.sky(1.0)
    K.sun((50, 0, -35), 4.0)
    K.ground(300, color="#5fae3a")
    S.salvager_ship((0, 0, 0), fires=False)
    fore = bpy.data.objects["fore"]
    mats = build.make_materials()
    rig = build.build_bay(mats, fore)
    lamp = K.point("review_fill", (0.0, 0.0, 0.0), "#dff4ff", 60, 0.4)   # a soft fill so the bay reads in review shots
    lamp.parent = fore
    lamp.location = (0.6, 0.4, 2.6)
    return rig, K


SHOTS = (  # name, title, pose, time, camera
    ("bay_empty", "EMPTY (the cell thrown clear)", "empty", 0.2, "room"),
    ("bay_empty_close", "EMPTY, CLOSE", "empty", 0.2, "close"),
    ("bay_push", "INSTALL 0.5 s: PUSHED IN", "install", 0.5, "close"),
    ("bay_clamps", "INSTALL 1.3 s: CLAMPS", "install", 1.3, "close"),
    ("bay_hatch", "INSTALL 1.8 s: HATCH", "install", 1.8, "close"),
    ("bay_boot", "INSTALL 2.7 s: BOOTING", "install", 2.7, "close"),
    ("bay_powered", "POWERED", "powered", 0.3, "room"),
)


def cameras(K):
    """Cameras placed in the fore section's frame (the crew door at y 2, the Power Core's face at y -1.8)."""
    bpy.context.view_layer.update()
    m = bpy.data.objects["fore"].matrix_world

    def cam(name, loc, target, lens):
        return K.camera(name, tuple(m @ Vector(loc)), tuple(m @ Vector(target)), lens=lens)

    return {"room": cam("room", (1.3, 1.6, 1.85), (-0.1, -2.2, 1.2), 20),
            "close": cam("close", (0.85, -0.25, 1.25), (-0.05, -1.95, 0.75), 24)}


def bay_shots(out, build):
    rig, K = bay_scene(build)
    cams = cameras(K)
    poses = {"empty": build.pose_empty, "install": build.pose_install, "powered": build.pose_powered}
    shots = []
    for name, title, pose, t, cam in SHOTS:
        poses[pose](rig, t)
        bpy.context.view_layer.update()
        K.render(cams[cam], out / f"{name}.png", 1000, 634)
        shots.append((title, out / f"{name}.png"))
    if os.environ.get("MB_VIDEO"):
        frames = []
        fps = int(os.environ.get("MB_VIDEO_FPS", "12"))
        frames += [("empty", k / fps) for k in range(fps)]
        frames += [("install", k / fps) for k in range(int(build.INSTALL * fps) + 1)]
        frames += [("powered", k / fps) for k in range(fps)]
        bpy.context.scene.cycles.samples = int(os.environ.get("MB_VIDEO_SAMPLES", "16"))
        for i, (pose, t) in enumerate(frames):
            poses[pose](rig, t)
            bpy.context.view_layer.update()
            K.render(cams["close"], out / f"bay_install_{i:03d}.png", 640, 406)
    return shots


def sheet(path, shots, cols, w, h):
    rows = math.ceil(len(shots) / cols)
    img = Image.new("RGB", (w * cols, (h + 50) * rows), (255, 255, 255))
    d = ImageDraw.Draw(img)
    for i, (title, p) in enumerate(shots):
        x, y = (i % cols) * w, (i // cols) * (h + 50)
        img.paste(Image.open(p).convert("RGB").resize((w, h)), (x, y + 50))
        d.text((x + 16, y + 12), title.upper(), fill=rl.INK, font=rl.font(26, bold=True))
    img.save(path)


def render_all(out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    shots = []
    if os.environ.get("MB_SKIP_PROPS") is None:
        shots += [(t.replace("_", " "), p) for t, p in cell_shots(out, build)]
        shots += [("fallen in the grass", p) for _, p in fallen_shots(out, build)]
        sheet(out / "cell_sheet.png", shots + [("icon", build.ICONS / "master_battery_icon.png")], 3, 700, 545)
    bays = bay_shots(out, build)
    sheet(out / "bay_sheet.png", bays, 2, 900, 570)
