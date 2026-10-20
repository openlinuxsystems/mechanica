"""Design review renders for the tomato (Food design thread, 2026-10-20): fruit, seed packets, plant growth, holding
and eating, as review sheets and animated previews.

    python tools/food/render_tomato_design.py OUT_DIR [section ...]     # sections: fruit packets plant eat anim

Cycles on the CPU; nothing is downloaded. Needs the bpy 5.2.2 wheel, numpy, pillow and ffmpeg.
"""

from __future__ import annotations

import math
import random
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "characters"))

import bpy  # noqa: E402,I001
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import food_lib as fl  # noqa: E402
import plant  # noqa: E402
import produce  # noqa: E402
import seed_packet as sp  # noqa: E402
import tomato  # noqa: E402

ICON_VIEW = (1.25, -1.6, 1.5)
BITE_A = 1.2           # where the bites go round the fruit (radians): toward the mouth when it is held
BITE_B = 2.4


def bitten(name, n, location=(0, 0, 0)):
    t = tomato.build(name, location=location)
    x, y, _ = location
    for k, a in enumerate((BITE_A, BITE_B)[:n]):
        tomato.bite(t, (x + 0.044 * math.cos(a), y + 0.044 * math.sin(a), 0.05 - 0.005 * k), name=f"{name}Bite{k}")
    return t


def hide(objs, hidden=True):
    for o in objs:
        o.hide_render = hidden


# ---------------------------------------------------------------------------------------------------------------------

def fruit(out):
    fl.reset()
    t = tomato.build("Tomato")
    objs = fl.tree(t)
    fl.auto_icon(objs, out / "tomato_icon.png", ICON_VIEW, fill=1.2)
    tgt = (0, 0, 0.034)
    fl.render_view(objs, out / "tomato_front3q.png", (1, -1.4, 0.8), ortho=0.13, target=tgt, size=800)
    fl.render_view(objs, out / "tomato_side.png", (1, 0, 0.05), ortho=0.13, target=tgt, size=800)
    fl.render_view(objs, out / "tomato_top.png", (0, -0.01, 1), ortho=0.13, target=tgt, size=800)
    hide(objs)
    for n in (1, 2):
        b = bitten(f"Bitten{n}", n, location=(n * 0.5, 0, 0))
        a = (BITE_A + BITE_B) / 2 if n == 2 else BITE_A
        fl.render_view(fl.tree(b), out / f"tomato_bite{n}.png", (math.cos(a), math.sin(a), 0.7), ortho=0.13,
                       target=(n * 0.5, 0, 0.034), size=800)
        hide(fl.tree(b))
    fl.sheet([out / f for f in ("tomato_icon.png", "tomato_front3q.png", "tomato_side.png", "tomato_top.png",
                                "tomato_bite1.png", "tomato_bite2.png")],
             ["Icon (256 px)", "Three-quarter", "Side", "Top", "First bite", "Second bite"],
             out / "sheet_1_tomato.png", 3, "TOMATO  -  harvested fruit, 8.8 cm")


def packets(out):
    fl.reset()
    c = produce.carrot()
    fl.auto_icon(fl.tree(c), out / "carrot_standin_icon.png", ICON_VIEW)
    hide(fl.tree(c))
    p = produce.potato()
    fl.auto_icon(fl.tree(p), out / "potato_standin_icon.png", ICON_VIEW)
    hide(fl.tree(p))
    labels = {}
    for crop, col, icon in (("tomato", "#C8423A", out / "tomato_icon.png"),
                            ("carrot", "#E0B530", out / "carrot_standin_icon.png"),
                            ("potato", "#7A4A30", out / "potato_standin_icon.png")):
        labels[crop] = sp.label_image(icon, col, out / f"label_{crop}.png")
    pk = sp.build("TomatoSeeds", labels["tomato"])
    objs = fl.tree(pk)
    fl.auto_icon(objs, out / "tomato_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)
    fl.render_view(objs, out / "packet_front3q.png", (1, -1.6, 0.6), ortho=0.19, target=(0, 0, 0.058), size=800)
    fl.render_view(objs, out / "packet_back3q.png", (-1, 1.6, 0.6), ortho=0.19, target=(0, 0, 0.058), size=800)
    hide(objs)
    row = []
    for k, crop in enumerate(("tomato", "carrot", "potato")):
        row += fl.tree(sp.build(crop + "Pk", labels[crop], location=(1 + k * 0.11, 0, 0), seed=3 + k))
    fl.render_view(row, out / "packets_row.png", (0.5, -1.6, 0.55), ortho=0.36, target=(1.11, 0, 0.06), size=1000)
    hide(row)
    ic = []
    for crop in ("carrot", "potato"):
        o = sp.build(crop + "Ic", labels[crop], location=(3, 0, 0))
        fl.auto_icon(fl.tree(o), out / f"{crop}_seed_icon.png", (1.0, -1.7, 1.0), fill=1.15)
        hide(fl.tree(o))
    # the same label on the stake tag
    pl = plant.build(1.0, name="TagPlant", location=(5, 0, 0), label=labels["tomato"])
    soil = fl.soil((5, 0, 0))
    fl.render_view(fl.tree(pl) + soil, out / "stake_tag.png", (0.4, -1.6, 0.4), ortho=0.5, target=(5.05, 0, 0.66),
                   size=800)
    strip = Path(out / "seed_icons_row.png")
    from PIL import Image
    im = Image.new("RGBA", (3 * 256, 256), (255, 255, 255, 255))
    for k, crop in enumerate(("tomato", "carrot", "potato")):
        im.alpha_composite(Image.open(out / f"{crop}_seed_icon.png").convert("RGBA"), (k * 256, 0))
    im.save(strip)
    fl.sheet([out / f for f in ("tomato_seed_icon.png", "packet_front3q.png", "packet_back3q.png", "packets_row.png",
                                "seed_icons_row.png", "stake_tag.png")],
             ["Tomato Seed icon", "Packet, front", "Packet, back", "One packet, one label per crop",
              "Seed icons: tomato, carrot*, potato*", "Same label on the planted stake"],
             out / "sheet_2_seed_packets.png", 3,
             "SEED PACKETS  -  kraft pouch, tin tie, crop label (*carrot/potato art is a stand-in)")


def plant_sheet(out):
    fl.reset()
    label = out / "label_tomato.png"
    names = ["0  Seedling", "1  Young plant", "2  Staked and tied", "3  Flowering, green fruit", "4  Ripe"]
    files = []
    for st in range(5):
        x = st * 3.0
        objs = fl.tree(plant.build(float(st), name=f"P{st}", location=(x, 0, 0), label=label)) + fl.soil((x, 0, 0))
        f = out / f"plant_stage{st}.png"
        fl.render_view(objs, f, (0.55, -1.6, 0.75), ortho=1.25, target=(x, 0, 0.32), size=800)
        hide(objs)
        files.append(f)
    objs = fl.tree(plant.build(3.0, name="Dry", location=(20, 0, 0), label=label, dry=1.0)) + fl.soil((20, 0, 0), wet=False)
    fl.render_view(objs, out / "plant_dry.png", (0.55, -1.6, 0.75), ortho=1.25, target=(20, 0, 0.32), size=800)
    hide(objs)
    files.append(out / "plant_dry.png")
    names.append("Dry soil: the plant wilts and stops")
    fl.sheet(files, names, out / "sheet_3_growth_stages.png", 3, "TOMATO PLANT  -  growth stages on a soil mound (0.75 m)")
    # all five in a row, as a player sees a field
    row = []
    for st in range(5):
        row += fl.tree(plant.build(float(st), name=f"R{st}", location=(40 + st * 0.85, 0, 0), label=label))
        row += fl.soil((40 + st * 0.85, 0, 0))
    fl.render_view(row, out / "plants_row.png", (0.3, -1.6, 0.55), ortho=4.0, target=(41.7, 0, 0.3), size=1400)


def growth_anim(out, frames=48, size=520):
    folder = out / "growth_frames"
    folder.mkdir(parents=True, exist_ok=True)
    label = out / "label_tomato.png"
    for i in range(frames + 1):
        fl.reset()
        g = 4.0 * i / frames
        objs = fl.tree(plant.build(g, name="G", label=label)) + fl.soil()
        fl.render_view(objs, folder / f"{i:04d}.png", (0.55, -1.6, 0.75), ortho=1.25, target=(0, 0, 0.32), size=size,
                       samples=16)
    for k in range(12):      # hold on ripe
        (folder / f"{frames + 1 + k:04d}.png").write_bytes((folder / f"{frames:04d}.png").read_bytes())
    gif(folder, out / "growth_timelapse.gif", 12)


def harvest_anim(out, size=520):
    folder = out / "harvest_frames"
    folder.mkdir(parents=True, exist_ok=True)
    label = out / "label_tomato.png"
    keys = 14
    for i in range(keys):
        t = i / (keys - 1)
        fl.reset()
        root = plant.build(4.0, name="H", label=label)
        objs = fl.tree(root) + fl.soil()
        pop = fl.smoothstep(0.0, 0.35, t)
        sink = fl.smoothstep(0.25, 0.8, t)
        for o in root.children:
            if "_Fruit" in o.name:
                o.location += Vector((0, -0.25 * pop, 0.12 * pop + 0.25 * pop * pop))
                o.scale = (1 + 0.15 * math.sin(math.pi * min(1, t / 0.35)),) * 3
                if t > 0.55:
                    o.scale = (0.0001,) * 3                # collected
            elif o.type == "MESH":
                o.scale = (1 - 0.95 * sink, 1 - 0.95 * sink, 1 - sink)
            elif o.type == "EMPTY":
                o.scale = (1 - 0.95 * sink,) * 3
        if 0.2 < t < 0.85:
            rnd = random.Random(i)
            puff = 1 - abs(t - 0.5) / 0.35
            for k in range(14):
                bm = bmesh.new()
                bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=rnd.uniform(0.008, 0.02) * puff)
                a = rnd.uniform(0, 6.3)
                r = rnd.uniform(0.03, 0.12) * (0.5 + t)
                bmesh.ops.translate(bm, verts=bm.verts, vec=(r * math.cos(a), r * math.sin(a), rnd.uniform(0.0, 0.08) * t))
                objs.append(fl.from_bm("Puff", bm, [fl.material("dirt_puff", "#6B5038", rough=1.0)]))
        fl.render_view(objs, folder / f"{i:04d}.png", (0.55, -1.6, 0.75), ortho=1.25, target=(0, 0, 0.32), size=size,
                       samples=16)
    for k in range(6):
        (folder / f"{keys + k:04d}.png").write_bytes((folder / f"{keys - 1:04d}.png").read_bytes())
    gif(folder, out / "harvest.gif", 12)


def gif(folder, path, fps):
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(fps), "-i", str(folder / "%04d.png"), "-vf",
                    "split[a][b];[a]palettegen=max_colors=160[p];[b][p]paletteuse", str(path)], check=True)
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", str(fps), "-i", str(folder / "%04d.png"),
                    "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", str(path.with_suffix(".mp4"))],
                   check=True)


# ---------------------------------------------------------------------------------------------------------------------

def eat(out, anim=True):
    import build_characters as bc
    import eat_poses as ep

    fl.reset()
    bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "scrapper_male.glb"))
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    body = [o for o in sc.objects if o.type == "MESH"]
    d, w, n = bc.hand_frame("r")
    centre = Vector(bc.grip_point("r") + n * 0.034 + d * 0.004)
    states = [tomato.build("Whole", location=(5, 0, 0)), bitten("B1", 1, (6, 0, 0)), bitten("B2", 2, (7, 0, 0))]
    for s_ in states:
        s_.location = (0, 0, 0)
    H = 0.07
    juice_mat = fl.material("juice", "#E8452C", rough=0.1, sss=0.3)

    def pose(fn, t):
        base = bc.clip_idle(0.0)
        base.update(fn(t))
        bc.apply_pose(arm, bc.build_pose(base))
        bpy.context.view_layer.update()

    def show(state):
        for k, s_ in enumerate(states):
            hide(fl.tree(s_), k != state)
        if state is None:
            return None
        pb = arm.pose.bones["hand_r"]
        rest = arm.matrix_world @ pb.bone.matrix_local
        posed = arm.matrix_world @ pb.matrix
        rp = Matrix.Translation(centre) @ Matrix.Rotation(math.pi, 4, "X") @ Matrix.Translation((0, 0, -H / 2))
        states[state].matrix_world = posed @ rest.inverted() @ rp
        bpy.context.view_layer.update()
        return states[state]

    def juice(fruit, amount, seed):
        made = []
        if amount <= 0:
            return made
        rnd = random.Random(seed)
        c = fruit.matrix_world @ Vector((0, 0, H / 2))
        for k in range(8):
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=rnd.uniform(0.002, 0.004))
            off = Vector((rnd.uniform(-0.035, 0.035), rnd.uniform(-0.06, -0.02), rnd.uniform(-0.01, 0.035))) * amount
            bmesh.ops.translate(bm, verts=bm.verts, vec=c + off)
            made.append(fl.from_bm("Juice", bm, [juice_mat]))
        return made

    def state_at(t):
        m = ep.EAT_MARKS
        return 0 if t < m["bite1"] + 0.02 else 1 if t < m["bite2"] + 0.02 else 2 if t < m["gone"] else None

    eye = Vector((0.0, -0.16, 1.68))
    fpv = Vector((0.12, 1.0, 0.75)).normalized()
    views = {"3q": dict(view=(0.9, -1.4, 0.25), ortho=0.75, target=(-0.1, -0.15, 1.38)),
             "fp": dict(view=tuple(fpv), target=tuple(eye - fpv), lens=24, distance=1.0, ground=False, clip_start=0.07)}
    keys = [("hold", None, 0.0), ("raise", ep.clip_eat_food, 0.22), ("bite", ep.clip_eat_food, 0.3),
            ("pull", ep.clip_eat_food, 0.4), ("second bite", ep.clip_eat_food, 0.52), ("pull away", ep.clip_eat_food, 0.62),
            ("chew", ep.clip_eat_food, 0.95)]
    for vname, kw in views.items():
        files = []
        for label, fn, t in keys:
            hold = ep.clip_hold_food_fp if vname == "fp" else ep.clip_hold_food
            pose(fn or hold, t)
            fruit = show(state_at(t) if fn else 0)
            jj = juice(fruit, 1.0 if label in ("bite", "second bite") else 0, len(label)) if fruit else []
            f = out / f"eat_{vname}_{label.replace(' ', '_')}.png"
            fl.render_view(body + (fl.tree(fruit) if fruit else []) + jj, f, kw["view"], ortho=kw.get("ortho"),
                           target=kw["target"], size=600, samples=24, lens=kw.get("lens"),
                           distance=kw.get("distance", 6.0), ground=kw.get("ground", True),
                           clip_start=kw.get("clip_start", 0.01))
            for j in jj:
                bpy.data.objects.remove(j)
            files.append((f, f"{label}  ({t:.2f} s)" if fn else "holding"))
        fl.sheet([f for f, _ in files][:6], [l for _, l in files][:6], out / f"sheet_4_eat_{vname}.png", 3,
                 "EATING  -  " + ("third person" if vname == "3q" else "first person") + ": hold, two bites, chew")
    if not anim:
        return
    for vname in ("3q", "fp"):
        folder = out / f"eat_{vname}_frames"
        folder.mkdir(parents=True, exist_ok=True)
        fps = 15
        hold_frames = 6
        i = 0
        hold = ep.clip_hold_food_fp if vname == "fp" else ep.clip_hold_food
        kw = views[vname]
        total = hold_frames + int(1.4 * fps) + 1 + hold_frames
        for k in range(total):
            if k < hold_frames:
                pose(hold, k / fps)
                fruit = show(0)
                t = -1
            elif k < hold_frames + int(1.4 * fps) + 1:
                t = (k - hold_frames) / fps
                pose(ep.clip_eat_food if vname == "3q" else ep.clip_eat_food_fp, t)
                fruit = show(state_at(t))
            else:
                pose(hold, 0.0)
                fruit = show(None)
                t = -1
            m = ep.EAT_MARKS
            jamt = max(0.0, 1 - abs(t - m["bite1"]) / 0.08) + max(0.0, 1 - abs(t - m["bite2"]) / 0.08)
            jj = juice(fruit, jamt * (1 + 0.5 * (t - m["bite1"] > 0)), k) if fruit and t >= 0 else []
            fl.render_view(body + (fl.tree(fruit) if fruit else []) + jj, folder / f"{i:04d}.png", kw["view"],
                           ortho=kw.get("ortho"), target=kw["target"], size=420, samples=10, lens=kw.get("lens"),
                           distance=kw.get("distance", 6.0), ground=kw.get("ground", True),
                           clip_start=kw.get("clip_start", 0.01))
            for j in jj:
                bpy.data.objects.remove(j)
            i += 1
        gif(folder, out / f"eat_{vname}.gif", fps)


SECTIONS = {"fruit": fruit, "packets": packets, "plant": plant_sheet, "growth": growth_anim, "harvest": harvest_anim,
            "eat": eat}


def main(argv):
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    for name in argv[1:] or list(SECTIONS):
        SECTIONS[name](out)
        print(f"== {name} done", flush=True)


if __name__ == "__main__":
    main(sys.argv[1:])
