"""Review renders of the furniture containers (build_furniture.py --render OUT_DIR).

Writes, into OUT_DIR, for each container: three-quarter from the front right and from the back left, front and top
views with the part's box drawn round them, and <id>_review_sheet.png with those and the icon; then lineup.png, all
nine standing in a row beside a 25 cm block. Cycles on the CPU; LIFT_SAMPLES sets the samples.
"""

from __future__ import annotations

import itertools
from pathlib import Path

import bpy
import render_lift as rl
from mathutils import Vector
from PIL import Image, ImageDraw

NOTES = {
    "fridge": "4 x 7 x 3 blocks (100 x 175 x 75 cm). Colony Works: bone panels in steel-blue rails, graphite caps and "
              "plinth, two doors with bar handles, header grille, cyan status light, condenser coil at the back.",
    "locker": "2 x 7 x 2 blocks (50 x 175 x 50 cm). Colony Works: steel-blue locker, pressed vent slots, name card and "
              "mustard number tag, lift latch with a padlocked hasp, three hinges.",
    "file_cabinet": "2 x 5 x 3 blocks (50 x 125 x 75 cm). Colony Works: steel-blue case, four bone drawers with bar "
                    "pulls and label cards, lock in the top drawer.",
    "tool_cabinet": "3 x 4 x 2 blocks (75 x 100 x 50 cm). Salvager Tech in signal red: gunmetal rails and hull corner "
                    "blocks, five drawers, lidded top tray with the SV-04 plate, side push bar, four casters.",
    "barrel": "2 x 4 x 2 blocks (50 x 100 x 50 cm). Steel-blue drum with rolled hoops, beads and chimes, two bungs, "
              "faded CW-07 stencil and fill band.",
    "broken_microwave": "2 x 2 x 2 blocks (50 cm cube). The SV-04's galley microwave, dead: bone casing, door hanging "
                        "off its bottom hinge with a cracked window, broken turntable, dead screen, soot.",
    "wooden_crate": "3 x 3 x 3 blocks (75 cm cube). Settler Patchwork: mismatched scrap-wood slats (one patched in "
                    "faded teal), diagonal braces, zinc and rusty corner brackets, OP-7 stencil.",
    "metal_crate": "3 x 3 x 3 blocks (75 cm cube). Colony Works: ribbed steel-blue crate, graphite corner castings and "
                   "rails, mustard band with MC-3, two latches, lid seam and hinges.",
    "dispenser": "3 x 6 x 2 blocks (75 x 150 x 50 cm). Colony Works supply locker: lit glass-fronted bay with stock, "
                 "header pictogram, cyan screen and buttons, dispensing hatch with a mustard pull.",
}


def show_only(roots, keep):
    for cid, root in roots.items():
        for ob in [root, *root.children_recursive]:
            ob.hide_render = cid not in keep


def box_outline(d, cam, half, w, h, color=(200, 60, 50)):
    """The part's grid box projected into an image, as a thin outline (what the model must stay inside)."""
    corners = [Vector((sx * half[0], sy * half[1], sz * half[2])) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    pix = [rl.project(cam, p, w, h) for p in corners]
    for i, j in itertools.combinations(range(8), 2):
        if sum(a != b for a, b in zip(format(i, "03b"), format(j, "03b"))) == 1:
            d.line([pix[i], pix[j]], fill=color, width=2)


def render_one(cid, out, build):
    hx, hy, hz = build.half_size(cid)
    _, ortho = build.icon_framing(cid)
    shots = {}
    for view, loc, ortho_k in (("three_quarter", Vector(build.gc.ICON_VIEW), 1.18), ("back", Vector((-1.3, 2.1, 0.95)), 1.18)):
        cam = rl.camera(f"{cid}_{view}", tuple(loc.normalized() * 6), (0, 0, 0), ortho=ortho * ortho_k)
        shots[view] = (rl.render(cam, out / f"{cid}_{view}.png", 700, 700), cam)
    for view, loc, size in (("front", (0, -6, 0), max(hx, hz)), ("top", (0, -0.0001, 6), max(hx, hy))):
        cam = rl.camera(f"{cid}_{view}", loc, (0, 0, 0), ortho=2 * size * 1.3)
        shots[view] = (rl.render(cam, out / f"{cid}_{view}.png", 600, 600), cam)
    # the grid box over the orthographic views
    for view in ("front", "top"):
        p, cam = shots[view]
        im = Image.open(p).convert("RGB")
        box_outline(ImageDraw.Draw(im), cam, (hx, hy, hz), 600, 600)
        im.save(p)
    return shots


def sheet(cid, shots, out, build):
    F, INK = rl.font, rl.INK
    im = Image.new("RGB", (2120, 1380), (246, 246, 244))
    d = ImageDraw.Draw(im)
    d.text((40, 24), f"{cid.upper().replace('_', ' ')}: design review", fill=INK, font=F(44, True))
    d.text((40, 82), NOTES[cid], fill=INK, font=F(21))
    for i, view in enumerate(("three_quarter", "back")):
        im.paste(Image.open(shots[view][0]).convert("RGB"), (40 + i * 720, 130))
        d.text((54 + i * 720, 140), {"three_quarter": "THREE-QUARTER", "back": "BACK"}[view], fill=INK, font=F(24, True))
    icon = Image.open(build.ICONS / f"{cid}_icon.png").convert("RGBA")
    d.text((1480, 130), "ICON (256 px)", fill=INK, font=F(22, True))
    for i, bg in enumerate(((40, 42, 46), (226, 228, 230))):
        tile = Image.new("RGB", (256, 256), bg)
        tile.paste(icon, (0, 0), icon)
        im.paste(tile, (1480 + i * 280, 165))
        small = Image.new("RGB", (64, 64), bg)
        ic = icon.resize((64, 64), Image.LANCZOS)
        small.paste(ic, (0, 0), ic)
        im.paste(small, (1480 + i * 280, 440))
    d.text((1480, 512), "and at 64 px", fill=INK, font=F(18))
    for i, view in enumerate(("front", "top")):
        tile = Image.open(shots[view][0]).convert("RGB").resize((300, 300), Image.LANCZOS)
        im.paste(tile, (1480 + i * 320, 560))
        d.text((1490 + i * 320, 566), view.upper(), fill=INK, font=F(20, True))
    d.text((1480, 870), "Red: the part's grid box.", fill=INK, font=F(18))
    im.save(out / f"{cid}_review_sheet.png")
    return out / f"{cid}_review_sheet.png"


def lineup(roots, out, build):
    """All nine on the floor in a row, with a 25 cm block for scale."""
    ids = list(roots)
    x = 0.0
    placed = {}
    for cid in ids:
        hx, _, hz = build.half_size(cid)
        placed[cid] = (x + hx, hz)
        x += 2 * hx + 0.25
    total = x - 0.25 + 0.25 + 0.25
    shift = total / 2
    for cid, (px, pz) in placed.items():
        roots[cid].location = (px - shift, 0, pz)
    me = bpy.data.meshes.new("ScaleBlock")
    s = 0.125
    verts = [(sx * s, sy * s, sz * s + s) for sx, sy, sz in itertools.product((-1, 1), repeat=3)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    me.from_pydata(verts, [], faces)
    me.validate()
    mat = bpy.data.materials.new("scale_block")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.72, 0.62, 0.48, 1)
    me.materials.append(mat)
    block = bpy.data.objects.new("RenderOnlyScaleBlock", me)
    block.location = (total - 0.125 - shift, 0, 0)
    bpy.context.scene.collection.objects.link(block)
    show_only(roots, set(ids))
    cam = rl.camera("lineup", (1.6, -7.5, 2.4), (0, 0, 0.6), ortho=total * 1.06)
    p = rl.render(cam, out / "lineup.png", 2600, 900)
    im = Image.open(p).convert("RGB")
    d = ImageDraw.Draw(im)
    F = rl.font
    for cid, (px, pz) in placed.items():
        a = rl.project(cam, (px - shift, -0.6, -0.05), 2600, 900)
        label = cid.replace("_", " ")
        d.text((a[0] - d.textlength(label, font=F(22)) / 2, a[1] + 8), label, fill=rl.INK, font=F(22))
    a = rl.project(cam, (total - 0.125 - shift, -0.6, -0.05), 2600, 900)
    d.text((a[0] - 40, a[1] + 8), "25 cm block", fill=rl.INK, font=F(22))
    im.save(p)
    bpy.data.objects.remove(block)
    for root in roots.values():
        root.location = (0, 0, 0)
    return p


def render_all(roots, out: Path, build):
    out.mkdir(parents=True, exist_ok=True)
    ground = rl.setup_world()
    sheets = []
    for cid in roots:
        show_only(roots, {cid})
        ground.location.z = -build.half_size(cid)[2]
        shots = render_one(cid, out, build)
        sheets.append(sheet(cid, shots, out, build))
        print(f"rendered {cid}")
    if len(roots) > 1:
        ground.location.z = 0.0
        lineup(roots, out, build)
    show_only(roots, set(roots))
    ground.hide_render = True
    print(f"rendered the review sheets into {out}")
