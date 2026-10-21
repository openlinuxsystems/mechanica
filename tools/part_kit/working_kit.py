"""The parts map's batch 6 working parts (see part_kit.py and light_kit.py): one look for the Thruster, Water Cannon,
Mounted Potato Blaster, Plasma Drill, Explosive Canisters, Alarm Clock, Sticky Wheel, Cash Register and Scuttler Head.

They share the batch 4 and 5 vocabulary so they read as one family on a creation:

* **Salvager Tech** (``docs/art-direction.md``) for the powered machines: hull-dark and gunmetal cases on a screwed
  base plate, copper trim, cooling rings and pipes, steel barrels, one copper pip per level on the Thruster;
* a **logic jack** (``part_kit.link_box``, scaled by ``light_kit.jack``) wherever links plug in;
* **cyan only on powered indicators**: a pip or running light that is lit while the part works; burner and plasma
  light is a pale blue-white, the canister lamp signal red, the Scuttler's eye its own green, never orange;
* the **Colony Works** signal red and yellow-black hazard paint on the Explosive Canisters, **Settler Patchwork**
  (bone enamel, zinc, brass keys, scrap wood) on the Cash Register, and the **Scuttler's** worn green battle paint
  (``tools/scuttler/build_scuttler.py``) on the Scuttler Head, so it is plainly the bot's eye head.

Axes are Blender's: x right, y the part's front (glTF -z), z up; each part stands on its base socket at the bottom.
Each ``build_<part>(mats[, level])`` returns a rig: ``root``, ``clips`` ({name: (seconds, loop, pose(t))}), ``rest``
(the pose the file rests in) and ``states`` ([(label, pose)] for the review renders and the icon: the last state is
the icon's). ``run`` writes ``meshes/parts/<part>.glb`` (or ``<part>_<level>.glb``) with its clips and the icons, and
with ``--render OUT_DIR`` the review views of every state and the part's sheet. Run the part scripts
(``tools/<part>/build_<part>.py``), not this file.
"""

from __future__ import annotations

import math
import sys

import bpy
from mathutils import Matrix, Vector

import part_kit as K
import light_kit as L
from part_kit import Geo, box, cylinder, ring

lt = K.lt
pt = K.pt
sys.path.insert(0, str(K.TOOLS / "scuttler"))
import build_scuttler as bs  # noqa: E402  (the Scuttler's battle paint and hazard stripes)

SAL = L.SAL
OFF = K.OFF
PLASMA = "#d6ecff"          # burner and plasma light: pale blue-white


def materials():
    m = L.materials()
    tex = lt.textured
    m.update({
        "scuttler_paint": tex("w_scuttler_paint", K.half_res(bs.battle_paint(71, "#5fae3e")), normal_strength=1.3),
        "scuttler_gun": tex("w_scuttler_gun", K.half_res(lt.metal_surface(83, "#2e3136", 0.45, 0.78, wear=0.9, edge_light=0.3)), normal_strength=1.3),
        "scuttler_bezel": tex("w_scuttler_bezel", K.half_res(lt.metal_surface(127, "#141416", 0.35, 0.6, wear=0.4, edge_light=0.15)), normal_strength=1.3),
        "hazard": tex("w_hazard", K.half_res(bs.hazard_surface(113)), normal_strength=1.0),
        "scuttler_glow": pt.plain("w_scuttler_glow", "#2fa83a", 0.15, 0.0, emission="#1aff26", strength=3.0),
        "plasma_glow": pt.plain("w_plasma_glow", PLASMA, 0.3, 0.0, emission=PLASMA, strength=6.0),
        "gel": pt.plain("w_gel", "#5fbf3a", 0.08, 0.0),
        "gel_glow": pt.plain("w_gel_glow", "#9cff6a", 0.08, 0.0, emission="#8cff50", strength=1.6),
        "dial": tex("w_dial", K.half_res(K.enamel_surface(461, "#ece6d4", chips=0.3)), normal_strength=0.6),
        "potato": pt.plain("w_potato", "#b08a52", 0.8, 0.0),
    })
    return m


# =====================================================================================================
# Shared pieces
# =====================================================================================================

def base_plate(g, floor, hx, hy, f, t=0.012, inset=0.022):
    """The screwed Salvager base plate the machines stand on; returns its top."""
    L.plate(g, (0, 0, floor + t), (hx, hy, t), f["dark"], f["bolt"], n=(0, 0, 1), inset=inset, r=0.008, bevel=0.006)
    return floor + 2 * t


def turret_foot(g, top, r, h, f):
    """A short round pedestal with a copper collar (the mounted guns and the drill)."""
    cylinder(g, (0, 0, top), (0, 0, top + h), r, f["frame"], segs=32)
    ring(g, top + h * 0.35, top + h * 0.55, r - 0.002, r + 0.006, f["trim"], segs=32)
    cylinder(g, (0, 0, top + h), (0, 0, top + h + 0.008), r + 0.012, f["dark"], segs=32)
    return top + h + 0.008


def y_cyl(g, y0, y1, r, mat, x=0.0, z=0.0, segs=24, r1=None):
    cylinder(g, (x, y0, z), (x, y1, z), r, mat, segs=segs, r1=r1)


def y_ring(g, y, depth, r_in, r_out, mat, x=0.0, z=0.0, segs=32):
    t = Geo()
    ring(t, -depth / 2, depth / 2, r_in, r_out, mat, segs=segs)
    m = L.axis_matrix((x, y, z), (0, 1, 0))
    g.extend(t, m)


def glow_obj(m, root, name, g, at=(0, 0, 0)):
    return K.make_object(name, g, m, root, at)


def keep(rig, *objects):
    """Remembers the rest transform of objects a pose moves."""
    rest = rig.setdefault("_rest", {})
    for ob in objects:
        rest[ob.name] = (ob, ob.location.copy(), ob.rotation_euler.copy(), ob.scale.copy())


def reset(rig):
    for ob, loc, rot, scale in rig.get("_rest", {}).values():
        ob.location, ob.rotation_euler, ob.scale = loc.copy(), rot.copy(), scale.copy()


def lit(ob, u):
    s = OFF + (1 - OFF) * u
    ob.scale = (s, s, s)


def bake(rig):
    """Bakes rig["clips"]: each {name: (seconds, loop, pose(t))} keyed on every moving object, as NLA tracks."""
    obs = [v[0] for v in rig.get("_rest", {}).values()]
    for ob in obs:
        ob.animation_data_create()
        ob.rotation_mode = "XYZ"
    for name, (seconds, loop, pose) in rig["clips"].items():
        acts = {}
        for ob in obs:
            acts[ob.name] = ob.animation_data.action = bpy.data.actions.new(f"{name}_{ob.name}")
        frames = max(2, int(round(seconds * K.FPS)))
        for fr in range(frames + 1):
            reset(rig)
            # a loop's last key equals its first, so it wraps without a jump
            pose((fr % frames if loop else fr) / K.FPS)
            for ob in obs:
                ob.keyframe_insert("location", frame=fr)
                ob.keyframe_insert("rotation_euler", frame=fr)
                ob.keyframe_insert("scale", frame=fr)
        for ob in obs:
            lt.linear_keys(acts[ob.name])
            K.push_track(ob, acts[ob.name], name)
    for ob in obs:
        ob.animation_data.use_nla = False
    reset(rig)
    rig["rest"]()


# =====================================================================================================
# Thruster (1 x 1 x 1, five levels): a jet on the Switch's base plate, its exhaust bell opening upward (+z)
# =====================================================================================================

def build_thruster(m, level):
    f = K.FINISH[level]
    root = K.make_empty("Thruster")
    g = Geo()
    floor = -0.125
    top = K.logic_plate(g, floor, (0.112, 0.112), f)
    # a square collar the chamber stands in, pips on its front
    box(g, (0, 0, top + 0.012), (0.09, 0.09, 0.012), f["frame"], bevel=0.006)
    K.pips(g, level, (-0.012 * (level - 1) / 2, 0.0905, top + 0.012), (0.012, 0, 0), (0, 1, 0), f, r=0.0042)
    c0 = top + 0.024
    bell_r = 0.086 + 0.006 * (level - 1)          # the bell widens with the power steps it unlocks
    throat = c0 + 0.085
    lip = 0.121
    # chamber, throat and bell in one turned shell; the bell's inside is dark
    K.lathe(g, [(0.0, c0), (0.068, c0), (0.072, c0 + 0.006), (0.072, throat - 0.022), (0.046, throat), (0.044, throat + 0.006),
                (bell_r, lip - 0.003), (bell_r - 0.004, lip), (bell_r - 0.009, lip - 0.002), (0.036, throat + 0.01), (0.0, throat + 0.01)],
            "gun_dark" if level < 5 else "black_chrome", steps=40)
    K.lathe(g, [(0.0355, throat + 0.0105), (bell_r - 0.0095, lip - 0.0025), (bell_r - 0.0098, lip - 0.0015), (0.035, throat + 0.0115)], "hole",
            steps=40)
    # copper cooling rings on the chamber
    for k in range(2 + (level >= 2)):
        z = c0 + 0.016 + k * 0.022
        ring(g, z, z + 0.008, 0.07, 0.078, f["trim"], segs=40)
    ring(g, lip - 0.007, lip - 0.001, bell_r - 0.004, bell_r + 0.002, "chrome" if level >= 5 else f["trim"], segs=48)
    if level >= 3:
        for k in range(8):
            a = 2 * math.pi * k / 8 + math.pi / 8
            d = Vector((math.cos(a), math.sin(a), 0))
            box(g, tuple(d * 0.079 + Vector((0, 0, c0 + 0.04))), (0.007, 0.0018, 0.032), "steel" if level < 5 else "chrome", bevel=0.001,
                rot=Matrix.Rotation(a, 3, "Z"))
    if level >= 4:
        ring(g, throat + 0.012, throat + 0.02, bell_r * 0.55, bell_r * 0.55 + 0.008, f["trim"], segs=40)
    # fuel line from the collar into the chamber, and the logic jack on the back corner
    K.sweep(g, K.bezier((0.075, -0.06, top + 0.02), (0.1, -0.06, top + 0.05), (0.09, -0.04, c0 + 0.07), (0.07, -0.03, c0 + 0.07)), 0.0055,
            "copper_bright" if level >= 4 else "copper", segs=8)
    L.jack(g, (-0.062, -0.074, top + 0.024), (0, -1, 0), f, scale=0.5)
    K.make_object("Body", g, m, root)
    rig = {"root": root, "glows": []}
    rig["glows"].append(glow_obj(m, root, "Throat", K.glow_disc(0.034, (0, 0, 1), "plasma_glow", depth=0.002, segs=24), (0, 0, throat + 0.012)))
    shell = Geo()
    K.lathe(shell, [(0.035, 0.0), (bell_r - 0.0105, lip - throat - 0.013), (bell_r - 0.0125, lip - throat - 0.013), (0.033, 0.0)], "plasma_glow",
            steps=40)
    rig["glows"].append(glow_obj(m, root, "Burn", shell, (0, 0, throat + 0.0115)))
    if level >= 4:
        # a cyan running light along the collar's front, over the pips, lit while it fires
        rig["glows"].append(K.make_object("RunningLight", K.glow_box((0.07, 0.0008, 0.0015), "cyan_glow"), m, root, (0, 0.0912, top + 0.0205)))
    return rig


def thruster_bake(rig):
    K.bake_pose_clips(rig["glows"], L.glow_pose(rig))


def run_levels(part, build, bake_levels, *, half, ortho, icon_target=(0, 0, 0), title="", lines=()):
    """A part with five levels whose glows light while it works (the Thruster): writes ``<part>_<level>.glb`` with its
    clips and a lit icon per level, and with ``--render OUT_DIR`` one sheet with every level at rest and level 1 and 5
    working."""
    K.reset_scene()
    m = materials()
    roots, rigs, infos, icons = {}, {}, {}, []
    for level in K.LEVELS:
        rig = build(m, level)
        root = rig["root"]
        bake_levels(rig)
        stem = f"{part}_{level}"
        infos[level] = K.export(root, K.MESHES / f"{stem}.glb", animations=True)
        K.report(K.MESHES / f"{stem}.glb", infos[level])
        K.strip_animation(root)
        L.glow_pose(rig)(1.0)
        icons.append((level, K.icon(root, K.ICONS / f"{stem}_icon.png", K.ICON_VIEW, ortho, icon_target)))
        L.glow_pose(rig)(0.0)
        for ob in [root, *root.children_recursive]:
            ob.name = f"{ob.name}_L{level}"
        roots[level], rigs[level] = root, rig
    out = K.args_out()
    if out is None:
        return infos
    import render_kit as rk
    rl = rk.rl
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    lo, _ = rk._bounds(roots[1])
    ground.location.z = lo.z - 0.0005
    size = max(half) * 2
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    cams = {
        "three_quarter": rl.camera("Q", target + Vector((-1.0, 1.25, 0.75)).normalized() * size * 3.4, target, lens=50),
        "front": rl.camera("F", target + Vector((0, 20, 0)), target, ortho=size * 1.32),
        "side": rl.camera("S", target + Vector((-20, 0, 0)), target, ortho=size * 1.32),
    }
    shots = [(f"LEVEL {lv}", lv, 0.0) for lv in K.LEVELS] + [("L1 FIRING", 1, 1.0), ("L5 FIRING", 5, 1.0)]
    paths = {v: [] for v in rk.VIEWS}
    for label, lv, u in shots:
        rk._show(roots.values(), roots[lv])
        L.glow_pose(rigs[lv])(u)
        for view in rk.VIEWS:
            p = out / "frames" / f"{part}_{label.lower().replace(' ', '_')}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, rk.W, rk.H)
            paths[view].append((label, p))
        L.glow_pose(rigs[lv])(0.0)
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    stats = [rk.fmt_stats(lv, infos[lv]) for lv in K.LEVELS]
    rk.sheet(part, title, list(lines), paths, icons, stats, out)
    return infos


# =====================================================================================================
# Water Cannon (2 x 2 x 3): a pressure tank on a turret foot, a nozzle barrel out of its front
# =====================================================================================================

def build_water_cannon(m):
    f = SAL
    root = K.make_empty("WaterCannon")
    g = Geo()
    floor = -0.25
    top = base_plate(g, floor, 0.235, 0.36, f)
    top = turret_foot(g, top, 0.15, 0.045, f)
    # the body: a hull case with the tank's glass window on each side showing the water
    zc = top + 0.11
    box(g, (0, -0.05, zc), (0.165, 0.24, 0.11), f["frame"], bevel=0.02)
    for sx in (-1, 1):
        box(g, (sx * 0.162, -0.06, zc + 0.01), (0.006, 0.15, 0.06), f["dark"], bevel=0.004)
        box(g, (sx * 0.166, -0.06, zc + 0.01), (0.003, 0.135, 0.048), "glass", bevel=0.002)
        box(g, (sx * 0.164, -0.06, zc - 0.012), (0.002, 0.13, 0.025), "water", bevel=0.001)
        for k in range(4):
            K.screw(g, (sx * 0.168, -0.06 + (k % 2 * 2 - 1) * 0.14, zc + 0.01 + (k // 2 * 2 - 1) * 0.052), (sx, 0, 0), 0.006, f["bolt"])
    # a top hatch, a pressure gauge, the hose to the back and copper bands
    box(g, (0, -0.1, zc + 0.112), (0.09, 0.08, 0.008), f["dark"], bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            K.screw(g, (sx * 0.075, -0.1 + sy * 0.065, zc + 0.12), (0, 0, 1), 0.006, f["bolt"])
    cylinder(g, (0.08, 0.08, zc + 0.11), (0.08, 0.08, zc + 0.135), 0.028, f["trim"], segs=24)
    K.disc(g, (0.08, 0.08, zc + 0.1355), (0, 0, 1), 0.022, "dial", segs=24)
    box(g, (0.086, 0.084, zc + 0.137), (0.012, 0.0015, 0.001), "hole")
    for y in (-0.22, 0.12):
        box(g, (0, y, zc), (0.168, 0.012, 0.112), f["trim"], bevel=0.004)
    K.sweep(g, K.bezier((-0.1, -0.29, zc - 0.04), (-0.12, -0.36, zc - 0.06), (-0.16, -0.33, top - 0.02), (-0.18, -0.25, top - 0.03)), 0.014, "rubber",
            segs=10)
    L.jack(g, (0.09, -0.335, top - 0.033), (0, -1, 0), f, scale=0.6)
    K.make_object("Body", g, m, root)
    # the barrel, which kicks back as it fires
    bz = zc + 0.02
    b = Geo()
    y_cyl(b, 0.0, 0.17, 0.05, f["dark"], segs=28)
    y_cyl(b, 0.17, 0.2, 0.06, f["trim"], segs=28)
    y_cyl(b, 0.2, 0.235, 0.05, "steel", segs=28, r1=0.036)
    y_ring(b, 0.06, 0.012, 0.05, 0.056, f["trim"])
    y_ring(b, 0.12, 0.012, 0.05, 0.056, f["trim"])
    K.disc(b, (0, 0.2355, 0), (0, 1, 0), 0.03, "hole", segs=20)
    barrel = K.make_object("Barrel", b, m, root, (0, 0.138, bz))
    # a burst of water out of the nozzle, grown by the fire clip
    s = Geo()
    L.lathe_on(s, [(0.0, 0.0), (0.03, 0.0), (0.075, 0.18), (0.05, 0.26), (0.0, 0.27)], "water", (0, 0, 0), (0, 1, 0), steps=24)
    spray = K.make_object("Spray", s, m, root, (0, 0.375, bz))
    rig = {"root": root}
    keep(rig, barrel, spray)

    def fire(t):
        kick = math.exp(-t / 0.06) * math.sin(math.pi * min(t / 0.03, 1)) if t < 0.45 else 0.0
        barrel.location.y = 0.138 - 0.035 * kick
        burst = K.smooth(t / 0.05) * (1 - K.smooth((t - 0.15) / 0.2))
        lit(spray, burst)
    rig["rest"] = lambda: lit(spray, 0)
    rig["clips"] = {"fire": (0.5, False, fire)}
    rig["states"] = [("IDLE", lambda: lit(spray, 0)), ("FIRING", lambda: fire(0.1))]
    return rig


# =====================================================================================================
# Mounted Potato Blaster (2 x 2 x 3): an air tank and breech on a turret foot, the potato hopper on top
# =====================================================================================================

def build_potato_blaster_mounted(m):
    f = SAL
    root = K.make_empty("PotatoBlaster")
    g = Geo()
    floor = -0.25
    top = base_plate(g, floor, 0.235, 0.36, f)
    top = turret_foot(g, top, 0.14, 0.04, f)
    zc = top + 0.085
    # the breech block, a steel air tank under it along the gun, side grips
    box(g, (0, -0.04, zc + 0.02), (0.11, 0.17, 0.085), f["frame"], bevel=0.016)
    y_cyl(g, -0.28, 0.12, 0.065, "steel", z=zc - 0.005, x=0.0, segs=28)
    for y in (-0.28, 0.12):
        K.dome(g, (0, y + (0.0 if y > 0 else 0.0), zc - 0.005), (0, 1 if y > 0 else -1, 0), 0.065, "steel", segs=20, height=0.03)
    for y in (-0.2, -0.05, 0.08):
        y_ring(g, y, 0.012, 0.065, 0.071, f["trim"], z=zc - 0.005)
    for sx in (-1, 1):
        box(g, (sx * 0.125, -0.06, zc + 0.04), (0.016, 0.06, 0.012), "rubber", bevel=0.006)
        box(g, (sx * 0.112, -0.06, zc + 0.04), (0.006, 0.07, 0.02), f["dark"], bevel=0.003)
    # the hopper the linked containers feed, with a potato showing in its window
    cylinder(g, (0, -0.1, zc + 0.1), (0, -0.1, zc + 0.2), 0.055, f["dark"], segs=24, r1=0.075)
    ring(g, zc + 0.19, zc + 0.205, 0.07, 0.08, f["trim"], segs=28, center=(0, -0.1))
    box(g, (0, -0.1 + 0.06, zc + 0.15), (0.03, 0.006, 0.03), "glass", bevel=0.002)
    K.dome(g, (0, -0.1, zc + 0.2), (0, 0, 1), 0.05, "potato", segs=14, height=0.02)
    K.sweep(g, K.bezier((-0.05, -0.17, zc + 0.15), (-0.12, -0.25, zc + 0.15), (-0.15, -0.32, top + 0.05), (-0.15, -0.3, top - 0.02)), 0.012, "rubber",
            segs=10)
    L.jack(g, (0.1, -0.335, top - 0.028), (0, -1, 0), f, scale=0.6)
    K.make_object("Body", g, m, root)
    b = Geo()
    y_cyl(b, 0.0, 0.28, 0.034, "steel", segs=24)
    y_cyl(b, 0.0, 0.05, 0.048, f["dark"], segs=24)
    for k in range(3):
        y_ring(b, 0.07 + k * 0.06, 0.008, 0.034, 0.041, f["trim"])
    y_cyl(b, 0.25, 0.3, 0.045, f["frame"], segs=24)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        box(b, (math.cos(a) * 0.044, 0.275, math.sin(a) * 0.044), (0.006, 0.015, 0.006), "hole")
    K.disc(b, (0, 0.3005, 0), (0, 1, 0), 0.026, "hole", segs=18)
    bz = zc + 0.03
    barrel = K.make_object("Barrel", b, m, root, (0, 0.073, bz))
    flash = K.make_object("Puff", K.glow_disc(0.05, (0, 1, 0), "frost", depth=0.004, segs=18), m, root, (0, 0.38, bz))
    rig = {"root": root}
    keep(rig, barrel, flash)

    def fire(t):
        kick = math.exp(-t / 0.025) * math.sin(math.pi * min(t / 0.012, 1))
        barrel.location.y = 0.073 - 0.03 * kick
        lit(flash, K.smooth(t / 0.01) * (1 - K.smooth((t - 0.02) / 0.05)))
    rig["rest"] = lambda: lit(flash, 0)
    rig["clips"] = {"fire": (0.1, False, fire)}
    rig["states"] = [("IDLE", lambda: lit(flash, 0)), ("FIRING", lambda: fire(0.02))]
    return rig


# =====================================================================================================
# Plasma Drill (2 x 2 x 3): an emitter in copper coils on a turret foot, throwing its beam out of the front
# =====================================================================================================

def build_plasma_drill(m):
    f = SAL
    root = K.make_empty("PlasmaDrill")
    g = Geo()
    floor = -0.25
    top = base_plate(g, floor, 0.235, 0.36, f)
    top = turret_foot(g, top, 0.13, 0.04, f)
    zc = top + 0.1
    # the capacitor case at the back with vents, the emitter barrel and its coil rings, a lens at the front
    box(g, (0, -0.17, zc), (0.13, 0.13, 0.1), f["dark"], bevel=0.018)
    for k in range(5):
        box(g, (0.1305, -0.22 + k * 0.025, zc + 0.02), (0.001, 0.008, 0.05), "hole")
        box(g, (-0.1305, -0.22 + k * 0.025, zc + 0.02), (0.001, 0.008, 0.05), "hole")
    box(g, (0, -0.17, zc + 0.102), (0.1, 0.1, 0.006), f["frame"], bevel=0.003)
    y_cyl(g, -0.05, 0.3, 0.06, f["frame"], z=zc, segs=32)
    y_cyl(g, 0.3, 0.33, 0.075, f["dark"], z=zc, segs=32)
    L.bezel(g, (0, 0.345, zc), (0, 1, 0), 0.04, 0.075, 0.016, f["trim"])
    L.lens(g, (0, 0.333, zc), (0, 1, 0), 0.042, 0.012, "lens")
    coil_y = [0.02 + k * 0.05 for k in range(5)]
    for y in coil_y:
        y_ring(g, y, 0.026, 0.06, 0.088, f["trim"], z=zc, segs=36)
    for sx in (-1, 1):
        y_cyl(g, -0.05, 0.27, 0.012, "copper_bright", x=sx * 0.095, z=zc - 0.03, segs=10)
    # status running light on the case, the battery feed jack at the back
    L.pip_socket(g, (0.09, -0.04, zc + 0.065), (0, 1, 0), r=0.007)
    L.jack(g, (0.0, -0.335, top - 0.028), (0, -1, 0), f, scale=0.6)
    K.make_object("Body", g, m, root)
    rig = {"root": root, "glows": []}
    for i, y in enumerate(coil_y):
        t = Geo()
        y_ring(t, 0.0, 0.0285, 0.0885, 0.0905, "plasma_glow", segs=36)
        rig["glows"].append(glow_obj(m, root, f"Coil{i}", t, (0, y, zc)))
    lens_glow = L.lens_glow(0.042, 0.012, (0, 1, 0), mat="plasma_glow")
    rig["glows"].append(glow_obj(m, root, "Emitter", lens_glow, (0, 0.333, zc)))
    beam = Geo()
    y_cyl(beam, 0.0, 0.55, 0.012, "plasma_glow", segs=12, r1=0.006)
    rig["glows"].append(glow_obj(m, root, "Beam", beam, (0, 0.35, zc)))
    rig["glows"].append(L.pip_glow(m, root, "Pip", (0.09, -0.04, zc + 0.065), (0, 1, 0), r=0.007))
    keep(rig, *rig["glows"])
    beam_ob = rig["glows"][-2]

    def power(u):
        for ob in rig["glows"]:
            lit(ob, u)

    def on(t):
        power(K.smooth(t / 0.12))

    def off(t):
        power(1 - K.smooth(t / 0.12))
    rig["rest"] = lambda: power(0)
    rig["clips"] = {"off": (0.12, False, off), "on": (0.12, False, on)}
    rig["states"] = [("OFF", lambda: power(0)), ("ON", lambda: power(1))]
    rig["beam"] = beam_ob
    return rig


# =====================================================================================================
# Explosive Canisters: a signal-red fuel drum with a hazard band and a fuse cap whose red lamp blinks
# =====================================================================================================

def build_canister(m, large):
    f = SAL
    root = K.make_empty("Canister")
    g = Geo()
    h = 0.375 if large else 0.25
    r = 0.215 if large else 0.1
    floor = -h
    s = 2.0 if large else 1.0
    z0, z1 = floor + 0.02 * s, h - 0.07 * s
    # foot ring, body, rolled rims, hazard band(s), top dome and the fuse cap
    ring(g, floor, floor + 0.022 * s, r - 0.02, r - 0.004, f["dark"], segs=40)
    cylinder(g, (0, 0, z0), (0, 0, z1), r, "signal_red", segs=40)
    for z in (z0 + 0.004, z1 - 0.004):
        ring(g, z - 0.007 * s, z + 0.007 * s, r - 0.003, r + 0.006, "steel", segs=40)
    bands = (0.42, 0.62) if large else (0.5,)
    for b in bands:
        zb = z0 + (z1 - z0) * b
        ring(g, zb - 0.026 * s, zb + 0.026 * s, r - 0.002, r + 0.002, "hazard", segs=40)
    if large:
        for zr in (0.25, 0.8):
            zz = z0 + (z1 - z0) * zr
            ring(g, zz - 0.006, zz + 0.006, r - 0.002, r + 0.008, "steel", segs=40)
    K.lathe(g, [(0.0, z1), (r - 0.004, z1), (r * 0.82, z1 + 0.03 * s), (r * 0.42, z1 + 0.045 * s), (0.0, z1 + 0.045 * s)], "signal_red", steps=40)
    cap_z = z1 + 0.045 * s
    cylinder(g, (0, 0, cap_z - 0.004), (0, 0, cap_z + 0.022 * s), 0.034 * s, f["frame"], segs=24)
    hexnut_z = cap_z + 0.022 * s
    cylinder(g, (0, 0, hexnut_z), (0, 0, hexnut_z + 0.006 * s), 0.022 * s, f["trim"], segs=6)
    # the fuse lamp's cage beside the cap, and a warning plate on the front
    lamp = Vector((r * 0.55, 0, cap_z + 0.004))
    cylinder(g, lamp, lamp + Vector((0, 0, 0.004 * s)), 0.017 * s, f["dark"], segs=16)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        cylinder(g, lamp + Vector((math.cos(a) * 0.014 * s, math.sin(a) * 0.014 * s, 0)),
                 lamp + Vector((math.cos(a) * 0.012 * s, math.sin(a) * 0.012 * s, 0.026 * s)), 0.0018 * s, "steel", segs=6)
    L.lens(g, tuple(lamp + Vector((0, 0, 0.004 * s))), (0, 0, 1), 0.011 * s, 0.014 * s, "red_lens", steps=20)
    zp = z0 + (z1 - z0) * (0.78 if large else 0.76)
    plate = Geo()
    box(plate, (0, 0, 0), (0.05 * s, 0.003, 0.03 * s), "mustard_paint", bevel=0.002)
    # a black burst on the plate: a star of thin bars
    for k in range(4):
        box(plate, (0, 0.0032, 0), (0.024 * s, 0.0012, 0.003 * s), "hole", rot=Matrix.Rotation(k * math.pi / 4, 3, "Y"))
    g.extend(plate, Matrix.Translation((0, r + 0.001, zp)))
    if large:
        for sx in (-1, 1):
            K.sweep(g, [(sx * r * 0.55, -0.03, z1 + 0.01), (sx * r * 0.6, -0.03, z1 + 0.06), (sx * r * 0.6, 0.03, z1 + 0.06),
                        (sx * r * 0.55, 0.03, z1 + 0.01)], 0.008, "steel", segs=8)
    K.make_object("Body", g, m, root)
    glow = glow_obj(m, root, "FuseLamp", L.lens_glow(0.011 * s, 0.014 * s, (0, 0, 1), mat="red_lamp_glow", steps=20),
                    tuple(lamp + Vector((0, 0, 0.004 * s))))
    rig = {"root": root}
    keep(rig, glow)

    def fuse(t):
        lit(glow, 1.0 if (t % 0.4) < 0.2 else 0.0)
    rig["rest"] = lambda: lit(glow, 0)
    rig["clips"] = {"fuse_loop": (0.4, True, fuse)}
    rig["states"] = [("IDLE", lambda: lit(glow, 0)), ("FUSE LIT", lambda: lit(glow, 1))]
    return rig


# =====================================================================================================
# Alarm Clock (2 x 2 x 1): a round dial case on two feet, twin bells and a hammer on top, a cyan armed pip
# =====================================================================================================

def build_alarm_clock(m):
    f = SAL
    root = K.make_empty("AlarmClock")
    floor = -0.25
    g = Geo()
    top = base_plate(g, floor, 0.2, 0.1, f, t=0.008, inset=0.018)
    # two splayed feet from the plate to the case
    for sx in (-1, 1):
        cylinder(g, (sx * 0.12, 0, top), (sx * 0.08, 0, top + 0.07), 0.016, f["frame"], segs=12, r1=0.012)
        K.dome(g, (sx * 0.12, 0, top), (0, 0, -1), 0.02, f["trim"], segs=12, height=0.006)
    K.make_object("Base", g, m, root)
    c = Geo()
    zc = top + 0.06 + 0.155
    dial_r = 0.145
    y_cyl(c, -0.075, 0.06, dial_r + 0.014, f["frame"], z=0.0, segs=48)
    y_ring(c, 0.065, 0.016, dial_r - 0.002, dial_r + 0.016, f["trim"], segs=48)
    K.disc(c, (0, 0.061, 0), (0, 1, 0), dial_r, "dial", segs=48)
    for k in range(12):
        a = 2 * math.pi * k / 12
        long = k % 3 == 0
        rr = dial_r - (0.022 if long else 0.016)
        box(c, (math.sin(a) * rr, 0.0625, math.cos(a) * rr), (0.003 if long else 0.002, 0.0008, 0.011 if long else 0.006), "hull",
            rot=Matrix.Rotation(-a, 3, "Y"))
    # the hands at ten past ten, the alarm hand (copper) at seven
    for ang, length, w, mat in ((-60, 0.07, 0.006, "hull"), (60, 0.105, 0.004, "hull"), (150, 0.09, 0.0025, "copper")):
        a = math.radians(ang)
        mid = Vector((math.sin(a) * length / 2, 0.064, math.cos(a) * length / 2))
        box(c, tuple(mid), (w, 0.001, length / 2), mat, rot=Matrix.Rotation(-a, 3, "Y"))
    y_cyl(c, 0.06, 0.068, 0.01, f["trim"], segs=16)
    L.lens(c, (0, 0.066, 0), (0, 1, 0), dial_r - 0.002, 0.012, "glass", steps=48)
    y_cyl(c, -0.08, -0.075, 0.08, f["dark"], segs=32)
    for k in range(2):
        K.screw(c, ((k * 2 - 1) * 0.05, -0.081, 0.05), (0, -1, 0), 0.006, f["bolt"])
    # the winding key and the alarm knob at the back, the logic jack under them
    y_cyl(c, -0.11, -0.08, 0.008, "steel", x=-0.05, z=-0.03, segs=10)
    box(c, (-0.05, -0.115, -0.03), (0.022, 0.004, 0.012), "brass", bevel=0.003)
    y_cyl(c, -0.1, -0.08, 0.016, f["trim"], x=0.05, z=-0.03, segs=16)
    L.jack(c, (0.0, -0.085, -0.15), (0, -1, 0), f, scale=0.55)
    # the bell posts and the armed pip on top
    for sx in (-1, 1):
        cylinder(c, (sx * 0.06, 0, dial_r + 0.006), (sx * 0.1, 0, dial_r + 0.035), 0.006, "steel", segs=8)
    cylinder(c, (0, -0.01, dial_r + 0.006), (0, -0.01, dial_r + 0.035), 0.005, "steel", segs=8)
    L.pip_socket(c, (0, 0.02, dial_r + 0.012), (0, 0, 1), r=0.007)
    case = K.make_object("Case", c, m, root, (0, 0, zc))
    bells = Geo()
    for sx in (-1, 1):
        a = math.radians(-40 * sx)
        bell = Geo()
        K.lathe(bell, [(0.0, 0.0), (0.06, 0.0), (0.058, 0.02), (0.045, 0.048), (0.02, 0.06), (0.0, 0.062)], "copper_bright", steps=32)
        mat_ = Matrix.Translation((sx * 0.1, 0, dial_r + 0.028)) @ Matrix.Rotation(a, 4, "Y")
        bells.extend(bell, mat_)
    bells_ob = K.make_object("Bells", bells, m, case)
    hm = Geo()
    cylinder(hm, (0, 0, 0), (0, 0, 0.05), 0.004, "steel", segs=8)
    cylinder(hm, (-0.012, 0, 0.05), (0.012, 0, 0.05), 0.009, f["trim"], segs=12)
    hammer = K.make_object("Hammer", hm, m, case, (0, -0.01, dial_r + 0.035))
    pip = L.pip_glow(m, case, "Pip", (0, 0.02, dial_r + 0.012), (0, 0, 1), r=0.007)
    rig = {"root": root}
    keep(rig, case, hammer, pip, bells_ob)

    def armed(u):
        lit(pip, u)

    def ring_(t):
        lit(pip, 1)
        hammer.rotation_euler.y = math.radians(28) * math.sin(2 * math.pi * 15 * t)
        case.rotation_euler.y = math.radians(1.2) * math.sin(2 * math.pi * 15 * t + 0.6)
        case.location.z = zc + 0.002 * abs(math.sin(2 * math.pi * 7.5 * t))
    rig["rest"] = lambda: armed(0)
    rig["clips"] = {"off": (0.12, False, lambda t: armed(1 - K.smooth(t / 0.12))),
                    "armed": (0.12, False, lambda t: armed(K.smooth(t / 0.12))),
                    "ring_loop": (0.4, True, ring_)}
    rig["states"] = [("OFF", lambda: armed(0)), ("RINGING", lambda: ring_(0.016)), ("ARMED", lambda: armed(1))]
    return rig


# =====================================================================================================
# Cash Register (3 x 2 x 2): Settler Patchwork: a bone-enamel till with brass keys, a sale-flag tower and a drawer
# =====================================================================================================

def build_cash_register(m):
    SET = L.SET
    root = K.make_empty("CashRegister")
    floor = -0.25
    g = Geo()
    # the drawer cabinet, scrap-wood sides, zinc kick plate
    box(g, (0, -0.02, floor + 0.09), (0.36, 0.22, 0.09), "plank_dark", bevel=0.01)
    box(g, (0, -0.02, floor + 0.012), (0.37, 0.23, 0.012), "zinc", bevel=0.004)
    box(g, (0, 0.2, floor + 0.09), (0.3, 0.012, 0.065), "hole")
    # the till: an enamel body sloping toward the front with three rows of brass keys
    zb = floor + 0.18
    body = Geo()
    L.polygon(body, [(-0.33, zb), (0.33, zb), (0.33, zb + 0.08), (-0.33, zb + 0.08)], 0.2, "enamel", depth=0.4)
    g.extend(body, Matrix.Translation((0, 0, 0)))
    slope = Geo()
    box(slope, (0, 0, 0), (0.31, 0.12, 0.012), "enamel_in", bevel=0.006)
    for row in range(3):
        for col in range(7):
            x = -0.24 + col * 0.08
            yk = -0.07 + row * 0.07
            cylinder(slope, (x, yk, 0.012), (x, yk, 0.03), 0.018, "brass", segs=14)
            K.disc(slope, (x, yk, 0.0305), (0, 0, 1), 0.014, "dial", segs=14)
    g.extend(slope, Matrix.Translation((0, 0.03, zb + 0.11)) @ Matrix.Rotation(math.radians(-18), 4, "X"))
    box(g, (0, -0.1, zb + 0.13), (0.33, 0.1, 0.05), "enamel", bevel=0.01)
    # zinc trim and a rusty seam strip, the crank on the right
    for x in (-0.335, 0.335):
        box(g, (x, 0.0, zb + 0.06), (0.006, 0.2, 0.06), "zinc", bevel=0.003)
    box(g, (0, 0.205, zb + 0.004), (0.335, 0.006, 0.008), "rust", bevel=0.002)
    # the sale-flag tower: a box on top with number tabs in its window
    box(g, (0, -0.12, zb + 0.22), (0.12, 0.06, 0.05), "enamel", bevel=0.01)
    box(g, (0, -0.058, zb + 0.225), (0.1, 0.003, 0.028), "glass", bevel=0.002)
    for k in range(4):
        box(g, (-0.06 + k * 0.04, -0.056, zb + 0.225), (0.014, 0.002, 0.02), "dial", bevel=0.001)
        box(g, (-0.06 + k * 0.04, -0.0545, zb + 0.225), (0.003, 0.001, 0.012), "hole")
    L.jack(g, (0.2, -0.235, floor + 0.024), (0, -1, 0), {"dark": "graphite", "bolt": "zinc"}, scale=0.6)
    K.make_object("Body", g, m, root)
    cr = Geo()
    cylinder(cr, (0, 0, 0), (0.03, 0, 0), 0.024, "zinc", segs=16)
    box(cr, (0.034, 0, 0.04), (0.005, 0.008, 0.045), "brass", bevel=0.003)
    cylinder(cr, (0.034, 0, 0.085), (0.06, 0, 0.085), 0.008, "plank", segs=10)
    crank = K.make_object("Crank", cr, m, root, (0.312, -0.05, zb + 0.06))
    d = Geo()
    box(d, (0, 0, 0), (0.29, 0.21, 0.058), "zinc", bevel=0.004)
    box(d, (0, 0.21, 0), (0.3, 0.008, 0.064), "plank", bevel=0.004)
    cylinder(d, (-0.06, 0.218, 0.0), (-0.06, 0.232, 0.0), 0.008, "brass", segs=10)
    cylinder(d, (0.06, 0.218, 0.0), (0.06, 0.232, 0.0), 0.008, "brass", segs=10)
    box(d, (0, 0.232, 0.0), (0.07, 0.006, 0.008), "brass", bevel=0.003)
    for k in range(4):
        box(d, (-0.2 + k * 0.133, 0.0, 0.03), (0.06, 0.18, 0.004), "plank_dark", bevel=0.002)
    for k in range(3):
        box(d, (-0.133 + k * 0.133, 0.0, 0.04), (0.003, 0.19, 0.016), "zinc")
    drawer = K.make_object("Drawer", d, m, root, (0, -0.02, floor + 0.09))
    rig = {"root": root}
    keep(rig, drawer, crank)

    def at(u):
        drawer.location.y = -0.02 + 0.17 * u
        crank.rotation_euler.x = -math.radians(120) * u

    def opening(t):
        at(K.sink(t, 0.1))

    def closing(t):
        at(1 - K.smooth(t / 0.25))
    rig["rest"] = lambda: at(0)
    rig["clips"] = {"open": (0.4, False, opening), "close": (0.25, False, closing)}
    rig["states"] = [("CLOSED", lambda: at(0)), ("OPEN", lambda: at(1))]
    return rig


# =====================================================================================================
# Scuttler Head (2 x 2 x 2): the Scuttler's eye head and the stump of its stalk on a salvaged mount
# =====================================================================================================

def build_scuttler_head(m):
    f = SAL
    root = K.make_empty("ScuttlerHead")
    floor = -0.25
    g = Geo()
    top = base_plate(g, floor, 0.2, 0.2, {"dark": "scuttler_gun", "bolt": "steel"}, t=0.01, inset=0.022)
    # the torn-off stalk: a steel neck with the gunmetal collars, cut cables at its back
    cylinder(g, (0, 0, top), (0, -0.01, top + 0.09), 0.045, "steel", segs=20)
    for k in range(3):
        cylinder(g, (0, -0.003 * k, top + 0.02 + k * 0.022), (0, -0.003 * k, top + 0.036 + k * 0.022), 0.062, "scuttler_gun", segs=20)
    for sx in (-1, 1):
        K.sweep(g, K.bezier((sx * 0.03, -0.04, top + 0.07), (sx * 0.06, -0.12, top + 0.06), (sx * 0.1, -0.14, top + 0.02), (sx * 0.12, -0.12, top)),
                0.008, "rubber" if sx < 0 else "copper", segs=8)
    for i in range(5):
        box(g, (-0.1 + i * 0.05, 0.19, top + 0.012), (0.015, 0.006, 0.012), "hazard", bevel=0.002)
    L.jack(g, (0.11, -0.17, top), (0, -1, 0), {"dark": "scuttler_gun", "bolt": "steel"}, scale=0.6)
    K.make_object("Mount", g, m, root)
    # the eye head (as the Scuttler's EyeHead, 2.1 times its size): a painted drum along the look, a dark bezel, a
    # visor over the front, a gunmetal dome behind, and the green lens
    s = 2.1
    h = Geo()
    y_cyl(h, -0.055 * s, 0.055 * s, 0.085 * s, "scuttler_paint", segs=40)
    y_cyl(h, 0.05 * s, 0.063 * s, 0.07 * s, "scuttler_bezel", segs=40)
    vis = Geo()
    box(vis, (0, 0, 0), (0.1 * s, 0.04 * s, 0.0125 * s), "scuttler_paint", bevel=0.008 * s)
    h.extend(vis, Matrix.Translation((0, 0.05 * s, 0.085 * s)) @ Matrix.Rotation(math.radians(12), 4, "X"))
    back = Geo()
    K.lathe(back, [(0.0, 0.0), (0.06 * s, 0.0)] + [(0.06 * s * math.cos(math.radians(a)), 0.036 * s * math.sin(math.radians(a))) for a in range(10, 91, 10)],
            "scuttler_gun", steps=32)
    h.extend(back, L.axis_matrix((0, -0.055 * s, 0), (0, -1, 0)))
    for k in range(8):
        a = 2 * math.pi * k / 8
        K.dome(h, (math.cos(a) * 0.075 * s, 0.063 * s, math.sin(a) * 0.075 * s), (0, 1, 0), 0.005 * s, "steel", segs=8)
    lens = Geo()
    L.lens(lens, (0, 0, 0), (0, 1, 0), 0.05 * s, 0.024 * s, "scuttler_glow", steps=32)
    h.extend(lens, Matrix.Translation((0, 0.063 * s, 0)))
    zc = top + 0.09 + 0.085 * s - 0.02
    head = K.make_object("Head", h, m, root, (0, 0.0, zc))
    flash = glow_obj(m, head, "Flash", L.lens_glow(0.05 * s, 0.024 * s, (0, 1, 0), mat="scuttler_glow", steps=32), (0, 0.063 * s, 0))
    rig = {"root": root}
    keep(rig, head, flash)

    def note(t):
        p = math.exp(-t / 0.12) * K.smooth(t / 0.02)
        s_ = OFF + (1.18 - OFF) * p
        flash.scale = (s_, s_, s_)
        head.rotation_euler.x = math.radians(5) * math.exp(-t / 0.1) * math.sin(2 * math.pi * 6 * t)
        head.location.z = zc + 0.006 * p
    rig["rest"] = lambda: lit(flash, 0)
    rig["clips"] = {"note": (0.5, False, note)}
    rig["states"] = [("IDLE", lambda: lit(flash, 0)), ("NOTE", lambda: note(0.03))]
    return rig


# =====================================================================================================
# Sticky Wheel (3 x 1 x 3): a gel-tread tyre on a Salvager rim, a chemical reservoir and pump on its outer face
# =====================================================================================================

def build_sticky_wheel(m):
    f = SAL
    root = K.make_empty("StickyWheel")
    g = Geo()
    zb, zt = -0.125, 0.125
    R, Ri = 0.365, 0.25
    w = 0.104
    # the tyre: a rounded rubber section with tread ribs, then the gel film over its tread
    prof = [(Ri, -w + 0.004), (R - 0.03, -w), (R - 0.008, -w + 0.02), (R, -w + 0.05), (R, w - 0.05), (R - 0.008, w - 0.02), (R - 0.03, w),
            (Ri, w - 0.004)]
    K.lathe(g, prof, "rubber", steps=72)
    for k in range(30):
        a = 2 * math.pi * k / 30
        d = Vector((math.cos(a), math.sin(a), 0))
        box(g, tuple(d * (R + 0.004)), (0.006, 0.02, w - 0.03), "rubber", bevel=0.002, rot=Matrix.Rotation(a, 3, "Z"))
    # the rim: a gunmetal dish with spokes, a copper ring and the hub plate the bearing takes
    K.lathe(g, [(0.06, -w + 0.01), (Ri + 0.004, -w + 0.01), (Ri + 0.004, w - 0.01), (Ri - 0.012, w - 0.005), (Ri - 0.02, w - 0.02),
                (0.06, w - 0.03)], f["frame"], steps=48)
    ring(g, w - 0.03, w - 0.018, Ri - 0.05, Ri - 0.036, f["trim"], segs=48)
    for k in range(6):
        a = 2 * math.pi * k / 6
        d = Vector((math.cos(a), math.sin(a), 0))
        box(g, tuple(d * 0.15 + Vector((0, 0, w - 0.03))), (0.09, 0.012, 0.012), f["dark"], bevel=0.004, rot=Matrix.Rotation(a, 3, "Z"))
    cylinder(g, (0, 0, zb), (0, 0, zb + 0.03), 0.07, f["dark"], segs=32)
    for k in range(6):
        a = 2 * math.pi * k / 6
        K.screw(g, (math.cos(a) * 0.05, math.sin(a) * 0.05, zb), (0, 0, -1), 0.006, f["bolt"])
    # the chemical reservoir (a glass drum showing the green gel) and pump on the hub, feed pipes out to the tread
    cylinder(g, (0, 0, w - 0.03), (0, 0, w + 0.0), 0.075, f["frame"], segs=32)
    cylinder(g, (0, 0, w), (0, 0, w + 0.008), 0.068, f["trim"], segs=32)
    ring(g, w + 0.008, w + 0.016, 0.05, 0.062, f["dark"], segs=32)
    K.disc(g, (0, 0, w + 0.0165), (0, 0, 1), 0.05, "glass", segs=32)
    K.disc(g, (0, 0, w + 0.012), (0, 0, 1), 0.048, "gel", segs=32)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.4
        d = Vector((math.cos(a), math.sin(a), 0))
        K.sweep(g, [tuple(d * 0.07 + Vector((0, 0, w - 0.01))), tuple(d * 0.16 + Vector((0, 0, w - 0.01))), tuple(d * (Ri - 0.03) + Vector((0, 0, w - 0.02))),
                    tuple(d * (Ri + 0.002) + Vector((0, 0, w - 0.03)))], 0.006, "copper", segs=8)
    L.pip_socket(g, (0.0, -0.072 + 0.04, w + 0.002), (0, 0, 1), r=0.006)
    L.jack(g, (0.0, 0.12, w - 0.022), (0, 1, 0), f, scale=0.5)
    K.make_object("Wheel", g, m, root)
    gel = Geo()
    K.lathe(gel, [(R + 0.0015, -w + 0.05), (R + 0.006, -w + 0.06), (R + 0.006, w - 0.06), (R + 0.0015, w - 0.05)], "gel", steps=72)
    for k in range(48):
        a = 2 * math.pi * (k + 0.5) / 48
        d = Vector((math.cos(a), math.sin(a), 0))
        K.dome(gel, tuple(d * (R + 0.005) + Vector((0, 0, ((k * 7) % 5 - 2) * 0.012))), tuple(d), 0.008, "gel", segs=6, height=0.005)
    K.make_object("Gel", gel, m, root)
    sheen = Geo()
    K.lathe(sheen, [(R + 0.0065, -w + 0.055), (R + 0.0085, -w + 0.06), (R + 0.0085, w - 0.06), (R + 0.0065, w - 0.055)], "gel_glow", steps=72)
    glow = K.make_object("Sheen", sheen, m, root)
    pip = L.pip_glow(m, root, "Pip", (0.0, -0.032, w + 0.002), (0, 0, 1), r=0.006)
    rig = {"root": root}
    keep(rig, glow, pip)

    def power(u):
        lit(glow, u)
        lit(pip, u)
    rig["rest"] = lambda: power(0)
    rig["clips"] = {"off": (0.12, False, lambda t: power(1 - K.smooth(t / 0.12))), "on": (0.12, False, lambda t: power(K.smooth(t / 0.12)))}
    rig["states"] = [("OFF", lambda: power(0)), ("ON", lambda: power(1))]
    return rig


# =====================================================================================================
# Runner and review renders
# =====================================================================================================

def run(part, build, *, half, ortho, icon_target=(0, 0, 0), title="", lines=()):
    """Builds `part`, bakes its clips, writes ``meshes/parts/<part>.glb`` and its icon (in its last state), and with
    ``--render OUT_DIR`` the review views of every state and the sheet."""
    K.reset_scene()
    m = materials()
    rig = build(m)
    root = rig["root"]
    rig["rest"]()
    bake(rig)
    info = K.export(root, K.MESHES / f"{part}.glb", animations=True)
    K.report(K.MESHES / f"{part}.glb", info)
    K.strip_animation(root)
    for ob in [root, *root.children_recursive]:
        if ob.animation_data:
            ob.animation_data.use_nla = False
    reset(rig)
    rig["states"][-1][1]()
    icon = K.icon(root, K.ICONS / f"{part}_icon.png", K.ICON_VIEW, ortho, icon_target)
    out = K.args_out()
    if out is not None:
        review(part, title or part, list(lines), rig, icon, info, out, half)
    return info


def review(name, title, lines, rig, icon, info, out, half):
    import render_kit as rk
    rl = rk.rl
    out.mkdir(parents=True, exist_ok=True)
    sc = bpy.context.scene
    root = rig["root"]
    ground = rl.setup_world()
    sc.cycles.samples = rk.SAMPLES
    reset(rig)
    rig["rest"]()
    lo, hi = rk._bounds(root)
    size = max(half) * 2
    ground.location.z = lo.z - 0.0005
    target = Vector((0, 0, lo.z + half[2] * 0.95))
    cams = {
        "three_quarter": rl.camera("Q", target + Vector((-1.0, 1.25, 0.75)).normalized() * size * 3.4, target, lens=50),
        "front": rl.camera("F", target + Vector((0, 20, 0)), target, ortho=size * 1.32),
        "side": rl.camera("S", target + Vector((-20, 0, 0)), target, ortho=size * 1.32),
    }
    paths = {v: [] for v in rk.VIEWS}
    for label, pose in rig["states"]:
        reset(rig)
        pose()
        for view in rk.VIEWS:
            p = out / "frames" / f"{name}_{label.lower().replace(' ', '_')}_{view}.png"
            p.parent.mkdir(parents=True, exist_ok=True)
            rl.render(cams[view], p, rk.W, rk.H)
            paths[view].append((label, p))
    for cam in cams.values():
        bpy.data.objects.remove(cam)
    rk.sheet(name, title, lines, paths, [(rig["states"][-1][0], icon)], [rk.fmt_stats(None, info)], out)
