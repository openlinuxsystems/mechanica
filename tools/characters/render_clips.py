"""Review renders of the player animation clips, straight from the clip code in build_characters.py (no export, no game).

    python tools/characters/render_clips.py OUT_DIR clip [clip ...] [--view side|front|threeq|back] [--fps 15] [--size 400]

Poses the committed ``scrapper_male.glb`` with each clip and renders it with Cycles on the CPU: OUT_DIR/<clip>/0000.png ...
and OUT_DIR/<clip>.gif (ffmpeg). An upper-body clip plays over ``idle``. ``CLIP_SAMPLES`` sets the samples (default 8).
For how a clip looks in game, with the blending and the procedural layers, use tools/capture-animations.sh.
"""

from __future__ import annotations

import math
import os
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
import build_characters as bc  # noqa: E402

VIEWS = {"side": (90.0, 8.0), "front": (0.0, 8.0), "threeq": (35.0, 14.0), "back": (180.0, 8.0)}


def option(argv, name, default):
    if name in argv:
        i = argv.index(name)
        value = argv[i + 1]
        del argv[i:i + 2]
        return value
    return default


def scene(view, size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "scrapper_male.glb"))
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = int(os.environ.get("CLIP_SAMPLES", "8"))
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x = sc.render.resolution_y = size
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.8, 0.85, 0.9, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    # a checker floor, so steps read against it
    floor = bpy.data.meshes.new("Floor")
    s = 6
    floor.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    ob = bpy.data.objects.new("Floor", floor)
    sc.collection.objects.link(ob)
    mat = bpy.data.materials.new("floor")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    checker = nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = 24.0
    checker.inputs["Color1"].default_value = (0.55, 0.58, 0.55, 1)
    checker.inputs["Color2"].default_value = (0.42, 0.45, 0.42, 1)
    mat.node_tree.links.new(checker.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    floor.materials.append(mat)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.lens = 50
    sc.collection.objects.link(cam)
    sc.camera = cam
    az, el = VIEWS[view]
    # the character faces -Y; azimuth 0 looks at its front
    d = 3.4
    a, e = math.radians(az), math.radians(el)
    cam.location = (d * math.sin(a) * math.cos(e), -d * math.cos(a) * math.cos(e), 0.95 + d * math.sin(e))
    direction = bc.Vector((0, 0, 0.9)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return sc, arm


def spec_at(name, t):
    fn, _, _, upper = bc.CLIPS[name]
    spec = fn(t)
    if upper:
        base = bc.clip_idle(t % 3.0)
        base.update(spec)
        spec = base
    return spec


def render(out, name, view, fps, size):
    sc, arm = scene(view, size)
    _, duration, loop, _ = bc.CLIPS[name]
    frames = max(2, int(round(duration * fps)) + (0 if loop else 1))
    folder = out / name
    folder.mkdir(parents=True, exist_ok=True)
    for i in range(frames):
        t = min(duration, i / fps)
        bc.apply_pose(arm, bc.build_pose(spec_at(name, t)))
        sc.render.filepath = str(folder / f"{i:04d}.png")
        bpy.ops.render.render(write_still=True)
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(fps), "-i", str(folder / "%04d.png"), "-vf",
                    "split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse", str(out / f"{name}.gif")], check=True)
    print(f"{name}: {frames} frames")


def main(argv):
    view = option(argv, "--view", "side")
    fps = int(option(argv, "--fps", "15"))
    size = int(option(argv, "--size", "400"))
    out = Path(argv[0])
    for name in argv[1:]:
        render(out, name, view, fps, size)


if __name__ == "__main__":
    main(sys.argv[1:])
