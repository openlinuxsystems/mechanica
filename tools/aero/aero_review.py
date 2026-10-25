"""Review sheets for the flight parts (``build_aero.py --render OUT_DIR``): one JPEG per part, 1600 px wide.

Row 1: the three-quarter view (front right, from above) of each of the part's states (the Hover Jet off and running, the
Rotor's two blade pitches, the Control Surface level and deflected). Row 2: the back three-quarter view (back left), the
view from below, and the part beside a 0.25 m block for scale. Row 3: the icon on the game's dark panel and on a light one.
Cycles on the CPU (``KIT_SAMPLES``, default 32); nothing is downloaded.
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

import part_kit as K
import working_kit as W

sys.path.insert(0, str(K.HERE))
import render_kit as rk  # noqa: E402

rl = rk.rl
CELL_W, CELL_H = 520, 440
DARK, LIGHT = (30, 34, 39), (226, 229, 233)


def _cube(at, size=0.25):
    me = bpy.data.meshes.new("ScaleBlock")
    h = size / 2
    vs = [(at[0] + x * h, at[1] + y * h, at[2] + h + z * h) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    me.from_pydata(vs, [], [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)])
    ob = bpy.data.objects.new("RenderOnlyScaleBlock", me)
    mat = bpy.data.materials.new("scale_block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.32, 0.34, 0.36, 1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
    me.materials.append(mat)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def review(name, title, lines, rig, icon, info, out: Path, half):
    out.mkdir(parents=True, exist_ok=True)
    frames = out / "frames"
    frames.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    W.reset(rig)
    rig["rest"]()
    lo, hi = rk._bounds(root)
    size = max(half) * 2
    ground.location.z = lo.z - 0.0005
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    dist = size * 2.1
    cams = {
        "front": rl.camera("Q", target + Vector((-1.0, 1.25, 0.75)).normalized() * dist, target, lens=50),
        "side": rl.camera("R", target + Vector((3.0, 0, 0)), target, ortho=size * 0.75),
        "back": rl.camera("B", target + Vector((1.0, -1.25, 0.75)).normalized() * dist, target, lens=50),
    }
    shots = []
    seen = set()
    for label, pose in rig["states"]:
        if label.strip() in seen:
            continue
        seen.add(label.strip())
        W.reset(rig)
        pose()
        p = frames / f"{name}_{label.strip().lower().replace(' ', '_').replace(chr(176), '')}.png"
        view = rig.get("state_view", "front")
        rl.render(cams[view], p, CELL_W, CELL_H)
        shots.append((label.strip() + (" (from the tip)" if view == "side" else ""), p))
    W.reset(rig)
    rig["rest"]()
    back = rl.render(cams["back"], frames / f"{name}_back.png", CELL_W, CELL_H)
    # from below (the ground hidden): the face a hover jet or a wing shows the ground
    ground.hide_render = True
    under_cam = rl.camera("U", target + Vector((-1.0, 1.25, -0.9)).normalized() * dist, target, lens=50)
    under = rl.render(under_cam, frames / f"{name}_under.png", CELL_W, CELL_H)
    ground.hide_render = False
    block = _cube((hi.x + 0.2, 0.0, lo.z))
    mid = Vector(((hi.x + 0.33) / 2 - 0.0, 0, target.z))
    wide = rl.camera("S", mid + Vector((-0.6, 1.25, 0.55)).normalized() * dist * 1.3, mid, lens=50)
    scale = rl.render(wide, frames / f"{name}_scale.png", CELL_W, CELL_H)
    bpy.data.objects.remove(block)
    for cam in (*cams.values(), under_cam, wide):
        bpy.data.objects.remove(cam)

    width = 1600
    pad = 26
    head = 70 + 34 * len(lines)
    rows = [shots, [("BACK (three-quarter, back left)", back), ("FROM BELOW", under), ("BESIDE A 0.25 M BLOCK", scale)]]
    height = head + len(rows) * (CELL_H + 50) + 330 + 60
    sh = Image.new("RGB", (width, height), (246, 246, 244))
    d = ImageDraw.Draw(sh)
    d.text((pad, 20), title, fill=rl.INK, font=rl.font(36, True))
    for i, t in enumerate(lines):
        d.text((pad, 70 + 34 * i), t, fill=rl.INK, font=rl.font(20))
    y = head
    for row in rows:
        for i, (label, p) in enumerate(row):
            x = pad + i * (CELL_W + 10)
            d.text((x, y), label, fill=rl.INK, font=rl.font(20, True))
            sh.paste(Image.open(p).convert("RGB"), (x, y + 30))
        y += CELL_H + 50
    d.text((pad, y), "ICON (dark panel, light panel)", fill=rl.INK, font=rl.font(20, True))
    ic = Image.open(icon).convert("RGBA")
    for i, bg in enumerate((DARK, LIGHT)):
        tile = Image.new("RGBA", (ic.width + 40, ic.height + 40), bg + (255,))
        tile.alpha_composite(ic, (20, 20))
        sh.paste(tile.convert("RGB"), (pad + i * (ic.width + 60), y + 30))
    stats = rk.fmt_stats(None, info)
    d.text((pad + 2 * (ic.width + 60) + 20, y + 40), "MODEL", fill=rl.INK, font=rl.font(20, True))
    for k, part in enumerate([stats[i:i + 70] for i in range(0, len(stats), 70)]):
        d.text((pad + 2 * (ic.width + 60) + 20, y + 74 + 28 * k), part, fill=rl.INK, font=rl.font(18))
    path = out / f"{name}_sheet.jpg"
    sh.save(path, quality=88)
    print(f"rendered {name} review sheet {path}")
    return path
