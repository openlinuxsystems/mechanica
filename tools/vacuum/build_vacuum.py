"""Builds the item transport parts (parts map batch 17c; tools/part_kit/transport_kit.py has the design): the Vacuum
Pipe, the Vacuum Pipe Corner, the Vacuum Pump and the Resource Collector.

    python3 tools/vacuum/build_vacuum.py [--render OUT_DIR] [--lineup OUT_DIR]

Writes ``mods/vanilla/meshes/parts/<part>.glb`` (the pump's ``on`` and ``off`` clips) and
``mods/vanilla/textures/items/<part>_icon.png`` for each; with ``--render`` the review views of each state and each
part's sheet; with ``--lineup`` the batch lineup (the four parts, and a small piped setup: a Vacuum Pump sucking a
pile through Vacuum Pipes into a Small Chest) as ``batch17c_lineup.png``. Blender's Python module (bpy) runs it;
nothing is downloaded. The build is deterministic.
"""

import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import transport_kit as T  # noqa: E402

PARTS = {
    "vacuum_pipe": dict(build=T.build_vacuum_pipe, half=(0.125, 0.125, 0.125), ortho=0.42,
                        title="Vacuum Pipe (1 x 1 x 1 block)",
                        lines=["Salvager Tech: tinted glass in a gunmetal cage with a screwed band; each end is the standard IO port's collar.",
                               "Chains end to end (no variable-length placement); scrap bits lie in the glass."]),
    "vacuum_pipe_corner": dict(build=T.build_vacuum_pipe_corner, half=(0.125, 0.125, 0.125), ortho=0.42, icon_view=(-0.7, -1.0, 1.5),
                               title="Vacuum Pipe Corner (1 x 1 x 1 block)",
                               lines=["A quarter bend of the same glass and cage, back to right side, with port collars and a screwed band."]),
    "vacuum_pump": dict(build=T.build_vacuum_pump, half=(0.375, 0.5, 0.375), ortho=1.3,
                        title="Vacuum Pump (3 x 3 x 4 blocks)",
                        lines=["Salvager Tech canister: two-way IO port on the back, a glass chamber with items swirling, a finned motor and logic jack on top,",
                               "a flared mouth with a grille. On (logic input): the cyan mouth ring and pip light; off: dark."]),
    "resource_collector": dict(build=T.build_resource_collector, half=(0.5, 0.5, 0.5), ortho=1.6,
                               title="Resource Collector (4 x 4 x 4 blocks)",
                               lines=["Salvager Tech hull bin: a cyclone intake funnel on top with vanes, a level window and hatch on the front,",
                                      "a canvas filter sock on the back (it burns) and the standard output port on the right."]),
}


def build_all():
    for part, spec in PARTS.items():
        T.run(part, spec["build"], half=spec["half"], ortho=spec["ortho"], title=spec["title"], lines=spec["lines"],
              icon_view=spec.get("icon_view", T.S.FRONT_ICON))


def lineup(out: Path):
    """The batch lineup: each part on its own, the pump running, and a pump piped to a Small Chest sucking up a pile."""
    import bpy
    from mathutils import Vector
    from PIL import Image, ImageDraw
    import render_kit as rk
    rl = rk.rl
    out.mkdir(parents=True, exist_ok=True)
    frames = []

    def shot(name, label, roots, target, eye, ortho=None, lens=50):
        sc = bpy.context.scene
        ground = rl.setup_world()
        sc.cycles.samples = rk.SAMPLES
        lo = min(rk._bounds(r)[0].z for r in roots)
        ground.location.z = lo - 0.0005
        cam = rl.camera("Cam", Vector(target) + Vector(eye), Vector(target), ortho=ortho, lens=lens)
        p = out / "frames" / f"lineup_{name}.png"
        p.parent.mkdir(parents=True, exist_ok=True)
        rl.render(cam, p, 840, 640)
        frames.append((label, p))

    single = [("vacuum_pipe", "Vacuum Pipe", 0.42), ("vacuum_pipe_corner", "Vacuum Pipe Corner", 0.42),
              ("vacuum_pump", "Vacuum Pump, on", 1.0), ("resource_collector", "Resource Collector", 1.25)]
    for part, label, size in single:
        T.fresh()
        m = T.materials()
        root, _rig = T.build_into(m, PARTS[part]["build"], (0, 0, 0))
        view = PARTS[part].get("icon_view", (1.0, -1.25, 0.8))
        shot(part, label, [root], (0, 0, 0), Vector(view).normalized() * size * 2.8)

    # the piped setup: a Small Chest's +x port, three pipes along x, the pump turned so its back port faces the chest
    T.fresh()
    m = T.materials()
    bpy.ops.import_scene.gltf(filepath=str(K.MESHES / "small_chest.glb"))
    chest = [o for o in bpy.context.selected_objects if o.parent is None]
    for o in chest:
        o.location = (0, 0, 0)
    roots = list(chest)
    for k in range(3):
        root, _ = T.build_into(m, T.build_vacuum_pipe, (0.5 + 0.25 * k, 0, 0), math.pi / 2)
        roots.append(root)
    pump, _ = T.build_into(m, T.build_vacuum_pump, (1.125 + 0.5, 0, 0), math.pi / 2)
    roots.append(pump)
    # a pile of scrap and wood in front of the pump's mouth
    g = K.Geo()
    pile = []
    for a in range(26):
        rr = 0.16 * math.sqrt((a + 0.5) / 26)
        t = a * 2.39996
        pile.append((2.4 + rr * math.cos(t), rr * math.sin(t), -0.35 + 0.05 * (1 - rr / 0.16)))
    T.bits(g, pile, 1761, size=0.03)
    roots.append(K.make_object("Pile", g, m, None))
    shot("setup", "Small Chest <- Vacuum Pipes <- Vacuum Pump", roots, (1.1, 0, 0), Vector((0.9, -2.2, 1.35)) * 1.25, lens=40)

    # a turn: chest, pipe, corner, pipe, pump around a corner
    T.fresh()
    m = T.materials()
    bpy.ops.import_scene.gltf(filepath=str(K.MESHES / "small_chest.glb"))
    chest = [o for o in bpy.context.selected_objects if o.parent is None]
    roots = list(chest)
    roots.append(T.build_into(m, T.build_vacuum_pipe, (0.5, 0, 0), math.pi / 2)[0])
    # the corner turned +90 degrees about z: its back (+y) faces -x (the pipe), its side (+x) faces +y
    roots.append(T.build_into(m, T.build_vacuum_pipe_corner, (0.75, 0, 0), math.pi / 2)[0])
    roots.append(T.build_into(m, T.build_vacuum_pipe, (0.75, 0.25, 0), 0.0)[0])
    roots.append(T.build_into(m, T.build_vacuum_pipe, (0.75, 0.5, 0), 0.0)[0])
    # the pump turned round: its back port (+y) faces the pipe run (-y), its mouth +y
    roots.append(T.build_into(m, T.build_vacuum_pump, (0.75, 0.625 + 0.5, 0), math.pi)[0])
    shot("turn", "Turning with a Vacuum Pipe Corner", roots, (0.5, 0.45, 0), Vector((1.7, -1.6, 1.7)) * 1.2, lens=40)

    # sheet
    F = rl.font
    cols, cw, ch, pad = 3, 840, 640, 20
    rows = (len(frames) + cols - 1) // cols
    W, H = pad + cols * (cw + pad), 90 + rows * (ch + 70)
    sheet = Image.new("RGB", (W, H), (255, 255, 255))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 24), "PARTS BATCH 17C: ITEM TRANSPORT (VACUUM PIPES, PUMP, COLLECTOR)", fill=rl.INK, font=F(40, True))
    for i, (label, p) in enumerate(frames):
        x, y = pad + (i % cols) * (cw + pad), 90 + (i // cols) * (ch + 70)
        sheet.paste(Image.open(p).convert("RGB"), (x, y))
        tw = d.textlength(label, font=F(26))
        d.text((x + (cw - tw) / 2, y + ch + 16), label, fill=rl.INK, font=F(26))
    sheet.save(out / "batch17c_lineup.png")
    print(f"wrote {out / 'batch17c_lineup.png'}")


SHEET = [
    ("vacuum_pipe", "Vacuum Pipe (new)",
     "now: 4.5 kg, dur 3, fric 0.2, buoy 0.6, not flammable, stack 20, 1x1x1; conduit, ports back and front",
     "glass tube in a gunmetal cage, IO port collars at both ends; chains end to end (no variable length, E12)",
     "now: Fabricator: 2 Metal Block + 5 Glass Block, 5 s (1776)"),
    ("vacuum_pipe_corner", "Vacuum Pipe Corner (new)",
     "now: 4.5 kg, dur 3, fric 0.2, buoy 0.6, not flammable, stack 20, 1x1x1; conduit, ports back and right",
     "a quarter bend of the same glass and cage",
     "now: Fabricator: 2 Metal Block + 5 Glass Block, 5 s (1777)"),
    ("vacuum_pump", "Vacuum Pump (new)",
     "now: 7.5 kg, dur 7, fric 0.5, buoy 0.7, not flammable, stack 10, 3x3x4; two-way port back, intake 2 m",
     "logic input; Suck (piles, loose resources, ripe crops, water) or Eject (E cycles); cyan ring and pip while on",
     "now: Fabricator: 20 Metal Block + 1 Kit + 1 Circuit Board + 3 Glue, 30 s (1778)"),
    ("resource_collector", "Resource Collector (new)",
     "now: 7.5 kg, dur 5, fric 0.6, buoy 0.3, flammable, stack 5, 4x4x4, 10 slots; output port right",
     "pulls loose resources and piles within 12 blocks of its top; a Refiner piped to it refines from it",
     "now: Fabricator: 40 Metal Block, 11 s (1775; a Dispenserbot item in SM)"),
    ("small_chest", "Containers and stations (data)",
     "now: io_ports on the Small, Large and Cargo Chests, the five 4x3x3 containers, Freezer, Refiner, Fabricator",
     "the ports their models already had, so pipes join them; stations take inputs and send outputs through them",
     "recipes unchanged"),
]


def sheet(out: Path):
    """The batch's old -> new sheet: each part's icon, stats and recipe (none of the four existed before)."""
    from PIL import Image, ImageDraw
    import render_kit as rk
    rl = rk.rl
    F = rl.font
    W, row = 1900, 120
    im = Image.new("RGB", (W, 100 + row * len(SHEET)), (255, 255, 255))
    d = ImageDraw.Draw(im)
    d.text((18, 14), "PARTS BATCH 17C: ITEM TRANSPORT (OLD -> NEW)", fill=rl.INK, font=F(30, True))
    d.text((18, 58), "Wiki bars and recipes (Craftbot = Fabricator). Real models and icons; pipes connect IO ports into a network "
           "(docs/io-ports.md).", fill=(110, 110, 110), font=F(15))
    blue, grey = (30, 90, 150), (110, 110, 110)
    for i, (part, name, stats, note, recipe) in enumerate(SHEET):
        y = 90 + i * row
        if i % 2 == 0:
            d.rectangle([0, y, W, y + row], fill=(240, 241, 243))
        icon = Image.open(K.ICONS / f"{part}_icon.png").convert("RGBA").resize((100, 100))
        im.paste(icon, (12, y + 10), icon)
        d.text((125, y + 8), name, fill=rl.INK, font=F(21, True))
        d.text((125, y + 38), "was: (new part)" if "(new)" in name else "was: no io_ports in data", fill=grey, font=F(15))
        d.text((125, y + 60), stats, fill=blue, font=F(15))
        d.text((145, y + 82), note, fill=blue, font=F(15))
        d.text((1180, y + 14), "recipe was: (none: new part)" if "(new)" in name else "", fill=grey, font=F(15))
        d.text((1180, y + 36), recipe, fill=blue, font=F(15))
    im.save(out / "batch17c_sheet.png")
    print(f"wrote {out / 'batch17c_sheet.png'}")


if __name__ == "__main__":
    if "--lineup" in sys.argv:
        lineup(Path(sys.argv[sys.argv.index("--lineup") + 1]))
    elif "--sheet" in sys.argv:
        sheet(Path(sys.argv[sys.argv.index("--sheet") + 1]))
    else:
        build_all()
