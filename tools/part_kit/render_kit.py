"""Review renders for the batch 4 part models (run through ``tools/<part>/build_<part>.py --render OUT_DIR``).

For a part it renders the three-quarter, front and side views of every level (all five in a row for a part with
levels), and lays them out with the level icons and each level's triangles and size on one labelled sheet:

* ``OUT_DIR/<part>_three_quarter.png``, ``<part>_front.png``, ``<part>_side.png`` - one view, every level in a row;
* ``OUT_DIR/<part>_sheet.png`` - the part's review sheet.

Cycles on the CPU (``KIT_SAMPLES``, default 32); nothing is downloaded.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "lift"))
import render_lift as rl  # noqa: E402  (stage, cameras, fonts)

SAMPLES = int(os.environ.get("KIT_SAMPLES", "32"))
W, H = 520, 460
VIEWS = ("three_quarter", "front", "side")
TITLES = {"three_quarter": "THREE-QUARTER (front right)", "front": "FRONT", "side": "SIDE (right)"}


def _bounds(root):
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for ob in [root, *root.children_recursive]:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            p = ob.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def _show(roots, only):
    for root in roots:
        for ob in [root, *root.children_recursive]:
            ob.hide_render = root is not only


def render_levels(name, roots, out: Path, half):
    """Renders each root (a level, all at the origin) from the three views. `half` is the part's half-size (m), so
    every level of a part is framed the same. Returns {view: [paths by level]}."""
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    for root in roots.values():
        for ob in [root, *root.children_recursive]:
            if ob.animation_data:
                ob.animation_data.action = None
    ground = rl.setup_world()
    sc.cycles.samples = SAMPLES
    lo, _ = _bounds(next(iter(roots.values())))
    ground.location.z = lo.z - 0.0005
    size = max(half) * 2
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    cams = {
        "three_quarter": rl.camera("Q", target + Vector((-1.0, 1.25, 0.75)).normalized() * size * 3.4, target, lens=50),
        "front": rl.camera("F", target + Vector((0, 20, 0)), target, ortho=size * 1.32),
        "side": rl.camera("S", target + Vector((-20, 0, 0)), target, ortho=size * 1.32),
    }
    paths = {v: [] for v in VIEWS}
    for level, root in roots.items():
        _show(roots.values(), root)
        for view in VIEWS:
            p = out / "frames" / f"{name}_{level}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, W, H)
            paths[view].append((level, p))
    _show(roots.values(), None)
    for root in roots.values():
        for ob in [root, *root.children_recursive]:
            ob.hide_render = False
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    return paths


def row(paths, title, label_levels):
    """One view of every level side by side, labelled."""
    n = len(paths)
    head = 44
    im = Image.new("RGB", (n * W, H + head), (246, 246, 244))
    d = ImageDraw.Draw(im)
    for i, (level, p) in enumerate(paths):
        im.paste(Image.open(p).convert("RGB"), (i * W, head))
        if label_levels:
            d.text((i * W + 14, 10), f"LEVEL {level}", fill=rl.INK, font=rl.font(24, True))
    return im


def sheet(name, title, lines, paths, icons, stats, out: Path):
    """The part's review sheet: title, notes, the three view rows, the icons and each level's numbers."""
    leveled = len(paths["front"]) > 1
    rows = {v: row(paths[v], TITLES[v], leveled) for v in VIEWS}
    for v, im in rows.items():
        im.save(out / f"{name}_{v}.png")
    width = max(1600, rows["front"].width + 80)
    top = 60 + 40 * (1 + len(lines))
    icon_h = 300
    height = top + sum(im.height + 60 for im in rows.values()) + icon_h + 40 * (len(stats) + 2)
    sh = Image.new("RGB", (width, height), (246, 246, 244))
    d = ImageDraw.Draw(sh)
    d.text((40, 30), title, fill=rl.INK, font=rl.font(44, True))
    for i, t in enumerate(lines):
        d.text((40, 96 + 40 * i), t, fill=rl.INK, font=rl.font(24))
    y = top
    for v in VIEWS:
        d.text((40, y), TITLES[v], fill=rl.INK, font=rl.font(26, True))
        sh.paste(rows[v], (40, y + 36))
        y += rows[v].height + 60
    d.text((40, y), "ICONS (hotbar, inventory, Fabricator)", fill=rl.INK, font=rl.font(26, True))
    for i, (level, p) in enumerate(icons):
        ic = Image.open(p).convert("RGBA")
        bg = Image.new("RGBA", ic.size, (225, 228, 232, 255))
        bg.alpha_composite(ic)
        sh.paste(bg.convert("RGB"), (40 + i * (W if leveled else 300), y + 40))
    y += icon_h
    d.text((40, y), "MODEL" if not leveled else "MODELS BY LEVEL", fill=rl.INK, font=rl.font(26, True))
    for i, s in enumerate(stats):
        d.text((40, y + 40 + 36 * i), s, fill=rl.INK, font=rl.font(22))
    sh.save(out / f"{name}_sheet.png")
    print(f"rendered {name} review sheet into {out}")
    return out / f"{name}_sheet.png"


def review(name, title, lines, roots, icons, stats, out: Path, half):
    paths = render_levels(name, roots, out, half)
    return sheet(name, title, lines, paths, icons, stats, out)


def fmt_stats(level, i):
    """One line of a level's numbers from part_kit.info: triangles, size and bounds (glTF axes, m)."""
    lo, hi = i["min"], i["max"]
    size = hi - lo
    lv = f"Level {level}: " if level else ""
    return (f"{lv}{i['tris']} triangles, {i['kib']:.0f} KiB, {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f} m "
            f"(x {lo[0]:.3f}..{hi[0]:.3f}, y {lo[1]:.3f}..{hi[1]:.3f}, z {lo[2]:.3f}..{hi[2]:.3f})"
            + (f", clips {', '.join(i['clips'])}" if i["clips"] else ""))
