"""Review renders of the Weld Tool prop, laid out like its reference sheet (front, side, top, rear and a three-quarter
view), with the clamping arms open as it is held and closed round the tip as a weld registers. Renders the committed
``hand_tools.glb``, so run ``build_characters.py props`` first.

    python tools/characters/render_weld_tool.py OUT_DIR

Cycles on the CPU; nothing is downloaded. Writes one PNG per view and ``weld_tool_sheet.png``.
"""

from __future__ import annotations

import math
import os
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_characters as bc  # noqa: E402

SAMPLES = int(os.environ.get("WELD_SAMPLES", "48"))
# name: (camera direction from the tool in the sheet's frame, label). The tool lies with its tip to -X, its top to +Z
# and its left side to -Y, as the reference sheet's side view shows it.
VIEWS = {
    "front": ((-1.0, 0.0, 0.0), "FRONT"),
    "side": ((0.0, -1.0, 0.0), "SIDE"),
    "top": ((0.0, 0.0, 1.0), "TOP"),
    "rear": ((1.0, 0.0, 0.0), "REAR"),
    "iso": ((-0.75, -1.0, 0.55), "THREE-QUARTER"),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x = 700
    sc.render.resolution_y = 700
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.97, 0.97, 0.98, 1)
    bg.inputs["Strength"].default_value = 0.9
    for name, loc, energy in (("key", (-1.0, -1.4, 1.6), 60), ("fill", (1.4, -0.8, 0.6), 22), ("rim", (0.6, 1.6, 1.2), 35),
                              ("low", (-0.6, -0.4, -1.4), 10)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = 1.2
        ob = bpy.data.objects.new(name, light)
        ob.location = loc
        sc.collection.objects.link(ob)
        ob.rotation_euler = (Vector((0, 0, 0)) - ob.location).to_track_quat("-Z", "Y").to_euler()


def load(closed):
    bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "hand_tools.glb"))
    tool = bpy.data.objects["weld_tool"]
    keep = {tool} | set(tool.children_recursive)
    for ob in list(bpy.data.objects):
        if ob.type in ("MESH", "EMPTY") and ob not in keep:
            bpy.data.objects.remove(ob)
    # grip frame -> gun frame (undo the rake) -> sheet frame (u to -X, v to +Z, w to +Y), centred on the housing
    gun = np.linalg.inv(bc._weld_rot())
    sheet = np.array([[-1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, 1.0, 0.0]]).T
    m3 = sheet @ gun
    centre = sheet @ np.array([0.057, bc.WELD_BODY_V - 0.02, 0.0])
    M = Matrix.Identity(4)
    for r in range(3):
        for c in range(3):
            M[r][c] = m3[r, c]
        M[r][3] = -centre[r]
    tool.parent = None
    tool.matrix_world = M
    if closed:
        for name, side in (("weld_tool_arm_l", -1), ("weld_tool_arm_r", 1)):
            arm = bpy.data.objects[name]
            axis, ang = bc.weld_arm_close(side)
            arm.matrix_basis = arm.matrix_basis @ Matrix.Rotation(math.radians(ang), 4, Vector(bc.weld_to_grip(axis)))
    return tool


def render(view, closed, out):
    reset()
    load(closed)
    direction, _ = VIEWS[view]
    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 0.27
    ob = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    ob.location = Vector(direction).normalized() * 2.0
    up = "Y" if view != "top" else "Y"
    ob.rotation_euler = (Vector((0, 0, 0)) - ob.location).to_track_quat("-Z", up).to_euler()
    if view == "top":
        ob.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)


def make_sheet(out_dir):
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    big = ImageFont.truetype(font_path, 30) if Path(font_path).exists() else ImageFont.load_default()
    small = ImageFont.truetype(font_path, 20) if Path(font_path).exists() else ImageFont.load_default()
    cell = 420
    img = Image.new("RGB", (cell * len(VIEWS), cell * 2 + 120), (247, 247, 249))
    d = ImageDraw.Draw(img)
    for row, (closed, title) in enumerate(((False, "WELD TOOL - ARMS OPEN (HELD)"), (True, "ARMS CLOSED (WELDING)"))):
        y0 = 50 + row * (cell + 60)
        d.text((16, y0 - 44), title, fill=(40, 40, 44), font=big)
        for col, view in enumerate(VIEWS):
            tile = Image.open(out_dir / f"weld_{view}_{'closed' if closed else 'open'}.png").convert("RGB").resize((cell, cell))
            img.paste(tile, (col * cell, y0))
            d.text((col * cell + 14, y0 + cell - 32), VIEWS[view][1], fill=(70, 70, 76), font=small)
    img.save(out_dir / "weld_tool_sheet.png")


def main(out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    for closed in (False, True):
        for view in VIEWS:
            render(view, closed, out_dir / f"weld_{view}_{'closed' if closed else 'open'}.png")
    make_sheet(out_dir)


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
