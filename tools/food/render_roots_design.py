"""Design review renders for the carrot and the potato (Food design thread, 2026-10-20), in the tomato's language:
harvested produce and its bites, icons, seed packets, the plant through its stages, wilting, growing, harvested, and
held and eaten.

    python tools/food/render_roots_design.py OUT_DIR [section ...]   # sections: produce packets plants growth harvest eat

Cycles on the CPU; nothing is downloaded. Needs the bpy 5.2.2 wheel, numpy, pillow and ffmpeg.
"""

from __future__ import annotations

import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))

import bpy  # noqa: E402,I001
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import carrot  # noqa: E402
import crop_rig  # noqa: E402
import food_lib as fl  # noqa: E402
import potato  # noqa: E402
import render_tomato_design as rtd  # noqa: E402
import seed_packet as sp  # noqa: E402

ICON_VIEW = (1.25, -1.6, 1.5)
CROPS = {"carrot": carrot, "potato": potato}
PLANT_VIEW = dict(view=(0.55, -1.6, 0.75), ortho=1.25)


def hide(objs, hidden=True):
    for o in objs:
        o.hide_render = hidden


def produce(out):
    for crop, mod in CROPS.items():
        fl.reset()
        whole = mod.build(crop.title())
        objs = fl.tree(whole)
        fl.auto_icon(objs, out / f"{crop}_icon.png", ICON_VIEW, fill=1.15)
        c, size = fl.bounds(objs)
        fl.render_view(objs, out / f"{crop}_front3q.png", (1, -1.4, 0.8), ortho=size * 1.35, target=tuple(c), size=800)
        fl.render_view(objs, out / f"{crop}_side.png", (0.05, -1, 0.15), ortho=size * 1.35, target=tuple(c), size=800)
        hide(objs)
        files = [out / f"{crop}_icon.png", out / f"{crop}_front3q.png", out / f"{crop}_side.png"]
        for n in (1, 2):
            b = mod.build(f"Bitten{n}", bites=n, location=(n * 0.6, 0, 0))
            bo = fl.tree(b)
            c, size = fl.bounds(bo)
            view = (1.3, -0.9, 0.9) if crop == "carrot" else (1.2, -0.5, 1.0)
            fl.render_view(bo, out / f"{crop}_bite{n}.png", view, ortho=size * 1.35, target=tuple(c), size=800)
            hide(bo)
            files.append(out / f"{crop}_bite{n}.png")
        fl.sheet(files, ["Icon (256 px)", "Three-quarter", "Side", "First bite", "Second bite"],
                 out / f"sheet_{crop}_1_harvested.png", 3,
                 f"{crop.upper()}  -  harvested, " + ("17 cm root" if crop == "carrot" else "10.5 cm tuber"))


def label_of(out, crop):
    path = out / f"label_{crop}.png"
    if not path.exists():
        sp.label_image(out / f"{crop}_icon.png", CROPS[crop].LABEL_COLOR, path)
    return path


def packets(out):
    fl.reset()
    tomato_label = out / "label_tomato.png"
    if not tomato_label.exists():
        sp.label_image(fl.REPO / "mods" / "vanilla" / "textures" / "items" / "tomato_icon.png", "#C8423A", tomato_label)
    labels = {"tomato": tomato_label, "carrot": label_of(out, "carrot"), "potato": label_of(out, "potato")}
    for crop in ("carrot", "potato"):
        o = sp.build(crop + "Ic", labels[crop], location=(3, 0, 0))
        fl.auto_icon(fl.tree(o), out / f"{crop}_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)
        hide(fl.tree(o))
    row = []
    for k, crop in enumerate(("tomato", "carrot", "potato")):
        row += fl.tree(sp.build(crop + "Pk", labels[crop], location=(1 + k * 0.11, 0, 0), seed=3 + k))
    fl.render_view(row, out / "packets_row.png", (0.5, -1.6, 0.55), ortho=0.36, target=(1.11, 0, 0.06), size=1000)
    from PIL import Image
    im = Image.new("RGBA", (3 * 256, 256), (255, 255, 255, 255))
    tomato_seed = fl.REPO / "mods" / "vanilla" / "textures" / "items" / "tomato_seed_icon.png"
    for k, f in enumerate((tomato_seed, out / "carrot_seed_icon.png", out / "potato_seed_icon.png")):
        im.alpha_composite(Image.open(f).convert("RGBA").resize((256, 256)), (k * 256, 0))
    im.save(out / "seed_icons_row.png")
    im = Image.new("RGBA", (3 * 256, 256), (255, 255, 255, 255))
    tomato_icon = fl.REPO / "mods" / "vanilla" / "textures" / "items" / "tomato_icon.png"
    for k, f in enumerate((tomato_icon, out / "carrot_icon.png", out / "potato_icon.png")):
        im.alpha_composite(Image.open(f).convert("RGBA").resize((256, 256)), (k * 256, 0))
    im.save(out / "crop_icons_row.png")


def rig_of(crop, out):
    return CROPS[crop].build_rig(label_of(out, crop))


def plants(out):
    for crop in CROPS:
        fl.reset()
        rig = rig_of(crop, out)
        soil = fl.soil()
        objs = fl.tree(rig.root) + soil
        names = ["0  Planted", "1  Young", "2  Growing", "3  Nearly there", "4  Ripe"]
        files = []
        for st in range(5):
            crop_rig.pose(rig, float(st))
            f = out / f"{crop}_stage{st}.png"
            fl.render_view(objs, f, PLANT_VIEW["view"], ortho=PLANT_VIEW["ortho"], target=(0, 0, 0.32), size=800)
            files.append(f)
        crop_rig.pose(rig, 3.0, 1.0)
        for o in soil:
            if o.type == "MESH":
                o.data.materials.clear()
                o.data.materials.append(fl.material("soil_dry", "#8A6E52", rough=0.95))
        f = out / f"{crop}_dry.png"
        fl.render_view(objs, f, PLANT_VIEW["view"], ortho=PLANT_VIEW["ortho"], target=(0, 0, 0.32), size=800)
        files.append(f)
        names.append("Dry soil: it wilts and stops")
        fl.sheet(files, names, out / f"sheet_{crop}_2_growth.png", 3,
                 f"{crop.upper()} PLANT  -  growth stages on a soil mound (0.75 m)")
        crop_rig.pose(rig, 4.0)
        f = out / f"{crop}_ripe_close.png"
        fl.render_view(objs, f, (0.7, -1.6, 0.45), ortho=0.6, target=(-0.02, 0, 0.08), size=800)


def growth(out, frames=40, size=480):
    for crop in CROPS:
        fl.reset()
        rig = rig_of(crop, out)
        objs = fl.tree(rig.root) + fl.soil()
        folder = out / f"{crop}_growth_frames"
        folder.mkdir(parents=True, exist_ok=True)
        for i in range(frames + 1):
            crop_rig.pose(rig, 4.0 * i / frames)
            fl.render_view(objs, folder / f"{i:04d}.png", PLANT_VIEW["view"], ortho=PLANT_VIEW["ortho"], target=(0, 0, 0.32),
                           size=size, samples=12)
        for k in range(10):
            (folder / f"{frames + 1 + k:04d}.png").write_bytes((folder / f"{frames:04d}.png").read_bytes())
        rtd.gif(folder, out / f"{crop}_growth.gif", 12)


def harvest(out, size=480):
    """The ripe crop popping up out of the soil toward the player, then the plant sinking away in a puff of dirt."""
    for crop in CROPS:
        fl.reset()
        rig = rig_of(crop, out)
        crop_rig.pose(rig, 4.0)
        soil = fl.soil()
        produce_obs = [r for _, r, *_ in rig.crowns] if crop == "carrot" else [t for t, _, _ in rig.tubers]
        base = {o: (o.matrix_world.copy()) for o in produce_obs}
        folder = out / f"{crop}_harvest_frames"
        folder.mkdir(parents=True, exist_ok=True)
        keys = 16
        for i in range(keys):
            t = i / (keys - 1)
            pop = fl.smoothstep(0.0, 0.4, t)
            sink = fl.smoothstep(0.3, 0.8, t)
            for o in produce_obs:
                m = base[o]
                lift = Matrix.Translation((0, -0.25 * pop, 0.18 * pop + 0.25 * pop * pop))
                o.matrix_world = lift @ m
                o.hide_render = t > 0.6
            rig.sway.scale = (1 - 0.95 * sink, 1 - 0.95 * sink, max(1e-3, 1 - sink))
            bpy.context.view_layer.update()
            objs = fl.tree(rig.root) + soil
            puffs = []
            if 0.25 < t < 0.9:
                rnd = random.Random(i)
                amount = 1 - abs(t - 0.55) / 0.35
                for k in range(16):
                    bm = bmesh.new()
                    bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=rnd.uniform(0.008, 0.02) * amount)
                    a = rnd.uniform(0, 6.3)
                    r = rnd.uniform(0.03, 0.13) * (0.5 + t)
                    bmesh.ops.translate(bm, verts=bm.verts, vec=(r * math.cos(a), r * math.sin(a), rnd.uniform(0, 0.08) * t))
                    puffs.append(fl.from_bm("Puff", bm, [fl.material("dirt_puff", "#6B5038", rough=1.0)]))
            fl.render_view(objs + puffs, folder / f"{i:04d}.png", PLANT_VIEW["view"], ortho=PLANT_VIEW["ortho"],
                           target=(0, 0, 0.32), size=size, samples=12)
            for p in puffs:
                bpy.data.objects.remove(p)
        for k in range(6):
            (folder / f"{keys + k:04d}.png").write_bytes((folder / f"{keys - 1:04d}.png").read_bytes())
        rtd.gif(folder, out / f"{crop}_harvest.gif", 12)


SECTIONS = {"produce": produce, "packets": packets, "plants": plants, "growth": growth, "harvest": harvest}


def main(argv):
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    for name in argv[1:] or list(SECTIONS):
        SECTIONS[name](out)
        print(f"== {name} done", flush=True)


if __name__ == "__main__":
    main(sys.argv[1:])
