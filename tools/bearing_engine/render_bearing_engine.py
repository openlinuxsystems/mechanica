"""Review renders of the Bearing and the Scrap Gas Engine (build_bearing_engine.py --render OUT_DIR).

Writes, into OUT_DIR: the bearing from the front, the side and three-quarter (as the reference sheet shows it), the
bearing on a block with its axle and flange, the engine from the front three-quarter and the back three-quarter, and
the engine in a few frames of its start, run and stop clips. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Euler, Vector
from PIL import Image

import render_lift as rl


def render_all(bearing, rig, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    sc = bpy.context.scene
    ground.location.z = -0.25
    shots = []
    # the bearing alone, lifted off the ground, then on a block
    for ob in [bearing, *bearing.children_recursive]:
        ob.hide_render = False
    for ob in [rig["root"], *rig["root"].children_recursive]:
        ob.hide_render = True
    block = bpy.data.meshes.new("Block")
    import bmesh  # noqa: PLC0415
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.25)
    bm.to_mesh(block)
    bm.free()
    bob = bpy.data.objects.new("RenderOnlyBlock", block)
    bob.location = (0, 0, -0.25)
    mat = bpy.data.materials.new("block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.35, 0.36, 0.38, 1)
    block.materials.append(mat)
    sc.collection.objects.link(bob)
    for name, loc, target in (("bearing_front", (0.0, -0.05, 1.2), (0, 0, -0.05)),
                              ("bearing_three_quarter", (0.7, -0.8, 0.55), (0, 0, -0.04)),
                              ("bearing_side", (0.0, -1.2, -0.02), (0, 0, -0.02))):
        cam = rl.camera(name, loc, target, ortho=0.42)
        shots.append(rl.render(cam, out / f"{name}.png", 900, 900))
    for ob in [bearing, *bearing.children_recursive]:
        ob.hide_render = True
    for ob in [rig["root"], *rig["root"].children_recursive]:
        ob.hide_render = False
    bob.hide_render = True
    ground.location.z = -0.25
    for name, loc in (("engine_front", (1.3, 1.6, 0.9)), ("engine_back", (-1.4, -1.5, 0.95)), ("engine_panel", (0.0, 1.8, 0.25))):
        cam = rl.camera(name, loc, (0, 0, -0.02), ortho=0.85)
        shots.append(rl.render(cam, out / f"{name}.png", 900, 900))
    # clip frames: switch NLA on and pick a track at a time
    cam = rl.camera("engine_clip", (1.3, 1.6, 0.9), (0, 0, -0.02), ortho=0.85)
    objs = [rig[k] for k in build.CLIP_OBJECTS]
    for clip, times in (("start", (0.15, 0.4)), ("run_hard_loop", (0.7,)), ("stop", (0.6,))):
        for ob in objs:
            ob.animation_data.use_nla = True
            for track in ob.animation_data.nla_tracks:
                track.mute = track.name != clip
        for t in times:
            sc.frame_set(int(round(t * build.FPS)))
            shots.append(rl.render(cam, out / f"engine_{clip}_{t:.2f}.png", 600, 600))
    for ob in objs:
        ob.animation_data.use_nla = False
    sheet = Image.new("RGB", (900 * 3, 900 * 2), (255, 255, 255))
    for i, p in enumerate(shots[:6]):
        sheet.paste(Image.open(p).convert("RGB"), ((i % 3) * 900, (i // 3) * 900))
    sheet.save(out / "review_sheet.png")
