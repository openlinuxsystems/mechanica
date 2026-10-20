"""Review renders of the Sledgehammer prop: front, side, top-down onto the head, a three-quarter view and a close-up of
the head. Renders the committed ``hand_tools.glb``, so run ``build_characters.py props`` first.

    python tools/characters/render_sledgehammer.py OUT_DIR

Cycles on the CPU; nothing is downloaded. Writes one PNG per view and ``sledgehammer_sheet.png``.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_characters as bc  # noqa: E402
import render_weld_tool as rw  # noqa: E402

rw.SAMPLES = int(os.environ.get("SLEDGE_SAMPLES", "48"))
# name: (camera direction, ortho scale, look-at height along the handle, label). The handle stands along +Z, the
# striking faces point along X and the cheeks along Y.
VIEWS = {
    "front": ((0.0, -1.0, 0.0), 1.15, -0.06, "FRONT"),
    "side": ((1.0, 0.0, 0.0), 1.15, -0.06, "SIDE"),
    "iso": ((0.8, -1.0, 0.5), 1.15, -0.06, "THREE-QUARTER"),
    "head": ((0.7, -1.0, 0.45), 0.5, bc.SLEDGE_HEAD_Y, "HEAD"),
    "head_low": ((-0.9, -1.0, -0.35), 0.5, bc.SLEDGE_HEAD_Y, "HEAD FROM BELOW"),
}


def load():
    bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "hand_tools.glb"))
    tool = bpy.data.objects["sledgehammer"]
    keep = {tool} | set(tool.children_recursive)
    for ob in list(bpy.data.objects):
        if ob.type in ("MESH", "EMPTY") and ob not in keep:
            bpy.data.objects.remove(ob)
    # grip frame (handle along y) to the sheet frame (handle along z)
    tool.parent = None
    tool.matrix_world = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
    return tool


def render(view, out):
    rw.reset()
    load()
    direction, scale, height, _ = VIEWS[view]
    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    cam.ortho_scale = scale
    ob = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    target = Vector((0, 0, height))
    ob.location = target + Vector(direction).normalized() * 3.0
    ob.rotation_euler = (target - ob.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = str(out)
    bpy.ops.render.render(write_still=True)


def make_sheet(out_dir):
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    small = ImageFont.truetype(font_path, 20) if Path(font_path).exists() else ImageFont.load_default()
    cell = 420
    img = Image.new("RGB", (cell * len(VIEWS), cell), (247, 247, 249))
    d = ImageDraw.Draw(img)
    for col, view in enumerate(VIEWS):
        img.paste(Image.open(out_dir / f"sledge_{view}.png").convert("RGB").resize((cell, cell)), (col * cell, 0))
        d.text((col * cell + 14, cell - 32), VIEWS[view][3], fill=(70, 70, 76), font=small)
    img.save(out_dir / "sledgehammer_sheet.png")


def main(out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    for view in VIEWS:
        render(view, out_dir / f"sledge_{view}.png")
    make_sheet(out_dir)


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
