"""Design review renders for the carrot and the potato (Food design thread, 2026-10-20), in the tomato's language:
harvested produce and its bites, icons, seed packets, the plant through its stages, wilting, growing, harvested, and
held and eaten.

    python tools/food/render_roots_design.py OUT_DIR [section ...]   # sections: produce packets plants growth harvest eat

Cycles on the CPU; nothing is downloaded. Needs the bpy 5.2.2 wheel, numpy, pillow and ffmpeg.
"""

from __future__ import annotations

import math
import os
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


# Held in the palm (the shared hold_food pose) and eaten with the shared two-bite clip. The mouth meets the palm about
# (d -0.026, w -0.01, n +0.059) from the grip, so each crop lies with its bitten end there: the carrot across the palm,
# tip past the little-finger edge and fronds out past the thumb; the potato with its bitten end toward the wrist.
HOLD = {"carrot": dict(axes=("-w", "d"), at=(0.004, carrot.L / 2 - 0.05, 0.012)),
        "potato": dict(axes=("-d", "w"), at=(0.02, 0.0, 0.012))}
CRUMBS = {"carrot": carrot.CORTEX, "potato": potato.FLESH_LIGHT}


def hold_matrix(bc, crop):
    """Rest-pose placement of the crop's root (lying, base at z = 0) in the right palm."""
    d, w, n = (Vector(v) for v in bc.hand_frame("r"))
    frame = {"d": d, "w": w, "n": n, "-d": -d, "-w": -w}
    x, y = (frame[a] for a in HOLD[crop]["axes"])
    z = n
    y = z.cross(x)              # keep it right-handed whatever the second axis says
    a, b, c = HOLD[crop]["at"]
    origin = Vector(bc.grip_point("r")) + d * a + w * b + n * c
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = origin
    return m


def eat(out):
    import build_characters as bc
    import eat_poses as ep

    # first person as the game frames it: the eye at FP_EYE along FP_LOOK, 75 degrees high, 16:9, only forearms drawn
    look = Vector(bc.FP_LOOK)
    views = {"3q": dict(view=(0.9, -1.4, 0.25), ortho=0.75, target=(-0.1, -0.15, 1.38)),
             "fp": dict(view=tuple(-look), target=tuple(Vector(bc.FP_EYE) + look), distance=1.0, ground=False,
                        clip_start=0.05, fov=75, aspect=16 / 9)}
    if os.environ.get("EAT_VIEWS"):
        views = {k: v for k, v in views.items() if k in os.environ["EAT_VIEWS"].split(",")}
    keys = [("hold", None, 0.0), ("raise", ep.clip_eat_food, 0.22), ("bite", ep.clip_eat_food, 0.3),
            ("second bite", ep.clip_eat_food, 0.52), ("pull away", ep.clip_eat_food, 0.62), ("chew", ep.clip_eat_food, 0.95)]
    for crop, mod in CROPS.items():
        fl.reset()
        bpy.ops.import_scene.gltf(filepath=str(bc.OUT_DIR / "scrapper_male.glb"))
        sc = bpy.context.scene
        arm = next(o for o in sc.objects if o.type == "ARMATURE")
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
        body = [o for o in sc.objects if o.type == "MESH" and "forearm_r" in o.vertex_groups]
        for o in sc.objects:
            if o.type == "MESH" and o not in body:
                o.hide_render = True
        states = [mod.build(f"S{k}", bites=k, location=(5 + k, 0, 0)) for k in range(3)]
        place = hold_matrix(bc, crop)
        lying = [_lying(s_) for s_ in states]
        crumb_mat = fl.material(crop + "_crumb", CRUMBS[crop], rough=0.5)

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
            s_ = states[state]
            s_.matrix_world = posed @ rest.inverted() @ place @ lying[state]
            bpy.context.view_layer.update()
            return s_

        def crumbs(amount, seed):
            made = []
            if amount <= 0:
                return made
            rnd = random.Random(seed)
            pb = arm.pose.bones["hand_r"]
            c = (arm.matrix_world @ pb.matrix) @ (arm.matrix_world @ pb.bone.matrix_local).inverted() \
                @ (Vector(bc.grip_point("r")) + Vector(bc.hand_frame("r")[2]) * 0.06)
            for k in range(7):
                bm = bmesh.new()
                bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rnd.uniform(0.002, 0.0035))
                off = Vector((rnd.uniform(-0.03, 0.03), rnd.uniform(-0.05, -0.015), rnd.uniform(-0.03, 0.01))) * amount
                bmesh.ops.translate(bm, verts=bm.verts, vec=c + off)
                made.append(fl.from_bm("Crumb", bm, [crumb_mat], smooth=False))
            return made

        def state_at(t):
            m = ep.EAT_MARKS
            return 0 if t < m["bite1"] + 0.02 else 1 if t < m["bite2"] + 0.02 else 2 if t < m["gone"] else None

        for vname, kw in views.items():
            for o in body:
                for mm in [mm for mm in o.modifiers if mm.name == "fp"]:
                    o.modifiers.remove(mm)
                if vname == "fp":
                    _forearms_only(o)
            files = []
            for label, fn, t in keys:
                hold = ep.clip_hold_food_fp if vname == "fp" else ep.clip_hold_food
                if fn and vname == "fp":
                    fn = ep.clip_eat_food_fp
                pose(fn or hold, t)
                food = show(state_at(t) if fn else 0)
                cc = crumbs(1.0 if label in ("bite", "second bite") else 0, len(label))
                f = out / f"{crop}_eat_{vname}_{label.replace(' ', '_')}.png"
                fl.render_view(body + (fl.tree(food) if food else []) + cc, f, kw["view"], ortho=kw.get("ortho"),
                               target=kw["target"], size=600, samples=24, lens=kw.get("lens"),
                               distance=kw.get("distance", 6.0), ground=kw.get("ground", True),
                               clip_start=kw.get("clip_start", 0.01), fov=kw.get("fov"), aspect=kw.get("aspect", 1.0))
                for o in cc:
                    bpy.data.objects.remove(o)
                files.append((f, f"{label}  ({t:.2f} s)" if fn else "holding"))
            fl.sheet([f for f, _ in files], [l for _, l in files], out / f"sheet_{crop}_3_eat_{vname}.png", 3,
                     f"{crop.upper()}  -  eating, " + ("third person" if vname == "3q" else "first person"))


def _forearms_only(o):
    """Masks the character down to its forearms and hands, all the game draws in first person."""
    keep = o.vertex_groups.get("fp") or o.vertex_groups.new(name="fp")
    idx = {o.vertex_groups[n].index for n in o.vertex_groups.keys() if n.split("_")[0] in ("forearm", "hand", "fingers", "thumb")}
    for v in o.data.vertices:
        if sum(g.weight for g in v.groups if g.group in idx) > 0.5:
            keep.add([v.index], 1.0, "REPLACE")
    o.modifiers.new("fp", "MASK").vertex_group = "fp"


def _lying(root):
    """The crop root's own lying pose (rotation and lift from ``build``) without its build location."""
    m = root.matrix_basis.copy()
    m.translation.x = m.translation.x - round(m.translation.x)    # builds sit at whole-metre x to keep states apart
    return m


SECTIONS = {"produce": produce, "packets": packets, "plants": plants, "growth": growth, "harvest": harvest, "eat": eat}


def main(argv):
    out = Path(argv[0])
    out.mkdir(parents=True, exist_ok=True)
    for name in argv[1:] or list(SECTIONS):
        SECTIONS[name](out)
        print(f"== {name} done", flush=True)


if __name__ == "__main__":
    main(sys.argv[1:])
