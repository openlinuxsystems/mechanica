"""Models of the parts audit's batch D5: the Workshop Sign (``outpost_sign``), the Warehouse Key Sign, the Speed Limit
Sign and the Barrier Tape.

All four are **Colony Works** site props and use the batch 12a sign kit (tools/parts_building/signs.py on
tools/part_kit/building_kit.py): a galvanised plate on a graphite post with a bolted foot, its face drawn with PIL and
decalled on, pictograms only (art rule 7). The only words are the Workshop sign's WORKSHOP name plate (as on the Workshop's
gable), the speed number and the keycard's WH. Never orange: the tape is signal red and
bone. Each builder takes the materials and returns the root; the model's face is on +y (building_kit.run turns it
toward the builder).
"""

from __future__ import annotations

import math

from PIL import Image, ImageDraw

import building_kit as B
import signs as S
from part_kit import Geo, box, cylinder, lathe

BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL = S.BONE, S.STEEL, S.MUSTARD, S.GRAPHITE, S.SIGNAL
WHITE = (236, 233, 224)          # the speed sign's face: a clean reflective white, a little warmer than paper
COPPER = (166, 112, 72)          # the key's contact chip, a dull worn copper (never orange)
PLATE_Z = S.PLATE_Z


# =====================================================================================================
# Pictograms
# =====================================================================================================

def cog(d, cx, cy, r, fill, hole, teeth=8):
    """A cog of `teeth` square teeth round (cx, cy), outer radius `r`, its hub cut out in `hole`."""
    pts = []
    inner, outer = r * 0.78, r
    for k in range(teeth):
        a0 = 2 * math.pi * k / teeth
        for da, rr in ((-0.5, inner), (-0.3, outer), (0.3, outer), (0.5, inner)):
            a = a0 + da * 2 * math.pi / teeth * 0.5 * 1.6
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(pts, fill=fill)
    d.ellipse([cx - r * 0.34, cy - r * 0.34, cx + r * 0.34, cy + r * 0.34], fill=hole)


def wrench(d, a, b, w, fill, bg, outline=None, ow=0):
    """An open-ended spanner from a (the jaw) to b (the ring end), its handle `w` pixels wide."""
    ax, ay = a
    bx, by = b
    ln = math.hypot(bx - ax, by - ay)
    ux, uy = (bx - ax) / ln, (by - ay) / ln
    nx, ny = -uy, ux

    def handle(width, col):
        h = width / 2
        d.polygon([(ax + nx * h, ay + ny * h), (bx + nx * h, by + ny * h), (bx - nx * h, by - ny * h), (ax - nx * h, ay - ny * h)], fill=col)

    def ends(grow, col):
        rj, rr = w * 1.25 + grow, w * 0.95 + grow
        d.ellipse([ax - rj, ay - rj, ax + rj, ay + rj], fill=col)
        d.ellipse([bx - rr, by - rr, bx + rr, by + rr], fill=col)

    if outline is not None:
        handle(w + 2 * ow, outline)
        ends(ow, outline)
    handle(w, fill)
    ends(0, fill)
    # the jaw opening: a slot cut in from the end, along the handle
    jw, jd = w * 0.55, w * 1.5
    tip = (ax - ux * w * 1.4, ay - uy * w * 1.4)
    d.polygon([(tip[0] + nx * jw, tip[1] + ny * jw), (tip[0] - nx * jw, tip[1] - ny * jw),
               (tip[0] - nx * jw + ux * jd, tip[1] - ny * jw + uy * jd), (tip[0] + nx * jw + ux * jd, tip[1] + ny * jw + uy * jd)],
              fill=bg)
    # the ring end's hole
    rh = w * 0.42
    d.ellipse([bx - rh, by - rh, bx + rh, by + rh], fill=bg)


def keycard(size, angle):
    """The Warehouse Key (tools/materials/build_materials.py) as a pictogram: a bone card with a steel-blue grip end,
    the lanyard grommet, WH on the grip, the copper chip and the roll-up door print, tilted `angle` degrees. RGBA."""
    w, h = int(size * 1.5), int(size)
    pad = int(size * 0.45)
    im = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    r = h * 0.1
    ow = max(4, int(h * 0.045))
    d.rounded_rectangle([x0 - ow, y0 - ow, x1 + ow, y1 + ow], radius=r + ow, fill=GRAPHITE + (255,))
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=BONE + (255,))
    gx1 = x0 + w * 0.3
    d.rounded_rectangle([x0, y0, gx1, y1], radius=r, fill=STEEL + (255,))
    d.rectangle([gx1 - r, y0, gx1, y1], fill=STEEL + (255,))
    # the grommet, and WH under it
    gc = (x0 + w * 0.14, y0 + h * 0.25)
    gr = h * 0.1
    d.ellipse([gc[0] - gr * 1.45, gc[1] - gr * 1.45, gc[0] + gr * 1.45, gc[1] + gr * 1.45], fill=GRAPHITE + (255,))
    d.ellipse([gc[0] - gr, gc[1] - gr, gc[0] + gr, gc[1] + gr], fill=BONE + (255,))
    f = S.font(int(h * 0.26))
    d.text((x0 + w * 0.15, y0 + h * 0.66), "WH", font=f, fill=BONE + (255,), anchor="mm")
    # the copper chip
    cx0, cy0 = x0 + w * 0.38, y0 + h * 0.5
    d.rounded_rectangle([cx0, cy0, cx0 + w * 0.16, cy0 + h * 0.3], radius=h * 0.03, fill=COPPER + (255,))
    for k in (1, 2):
        xx = cx0 + w * 0.16 * k / 3
        d.line([(xx, cy0 + h * 0.04), (xx, cy0 + h * 0.26)], fill=(90, 56, 30, 255), width=max(2, int(h * 0.015)))
    d.line([(cx0 + w * 0.02, cy0 + h * 0.15), (cx0 + w * 0.14, cy0 + h * 0.15)], fill=(90, 56, 30, 255), width=max(2, int(h * 0.015)))
    # the warehouse print: a roof over a roll-up door
    px0, px1, py0, py1 = x0 + w * 0.62, x0 + w * 0.92, y0 + h * 0.18, y0 + h * 0.84
    ink = GRAPHITE + (255,)
    mid = (px0 + px1) / 2
    d.polygon([(px0 - w * 0.02, py0 + h * 0.2), (mid, py0), (px1 + w * 0.02, py0 + h * 0.2)], fill=ink)
    d.rectangle([px0, py0 + h * 0.2, px1, py1], fill=ink)
    d.rectangle([px0 + w * 0.04, py0 + h * 0.32, px1 - w * 0.04, py1], fill=BONE + (255,))
    for k in range(4):
        yy = py0 + h * (0.37 + k * 0.11)
        d.rectangle([px0 + w * 0.055, yy, px1 - w * 0.055, yy + h * 0.045], fill=ink)
    return im.rotate(angle, resample=Image.BICUBIC, expand=False)


# =====================================================================================================
# The signs
# =====================================================================================================

FRAME_HW, FRAME_HH, RAIL = 0.33, 0.215, 0.032      # the Workshop sign's panel (half width, half height) and frame rail


def outpost_sign(mats):
    """Workshop Sign, 3 x 6 x 1: a bone panel in a steel-blue frame with graphite corner blocks on a graphite post, the
    Workshop pictogram (a steel-blue spanner over a graphite cog, a mustard rule under it) and a WORKSHOP name plate
    bolted to the post below."""
    g = Geo()
    top = PLATE_Z + FRAME_HH + RAIL
    B.post(g, 0, -0.045, -0.75, top - 0.01, half=0.024, mat="graphite", cap="graphite", foot_half=0.06)
    for dz in (-0.13, 0.13):
        box(g, (0, -0.04, PLATE_Z + dz), (0.04, 0.03, 0.018), "galv", bevel=0.004)
    # the frame round the panel, its corner blocks screwed on
    g_frame = Geo()
    B.frame(g_frame, FRAME_HW, FRAME_HH, -0.022, 0.012, RAIL, "steel_blue", corner="graphite")
    for v in range(len(g_frame.v)):
        x, y, z = g_frame.v[v]
        g_frame.v[v] = (x, y, z + PLATE_Z)
    g.extend(g_frame)
    # the panel: bone, the pictogram
    pts = [(FRAME_HW, PLATE_Z - FRAME_HH), (-FRAME_HW, PLATE_Z - FRAME_HH), (-FRAME_HW, PLATE_Z + FRAME_HH), (FRAME_HW, PLATE_Z + FRAME_HH)]
    f = S.Face(pts, BONE)
    d = f.d
    cx, cy = f.w * 0.5, f.h * 0.46
    cog(d, cx + f.h * 0.08, cy + f.h * 0.02, f.h * 0.32, GRAPHITE, BONE, teeth=8)
    wrench(d, (cx - f.h * 0.42, cy + f.h * 0.3), (cx + f.h * 0.42, cy - f.h * 0.3), f.h * 0.1, STEEL, BONE, outline=BONE, ow=f.h * 0.035)
    d.rectangle([f.w * 0.1, f.h * 0.86, f.w * 0.9, f.h * 0.9], fill=MUSTARD)
    f.worn(71, (150, 140, 120), amount=0.06)
    B.prism(g, pts, -0.012, -0.004, "galv", front=B.decal(mats, "workshop_face", f.im, rough=0.55))
    # the name plate under the panel: WORKSHOP stencilled in mustard on graphite (a number would run into the bolts)
    cz, chw, chh = PLATE_Z - FRAME_HH - RAIL - 0.085, 0.13, 0.035
    cpts = B.rounded_rect(chw, chh, 0.008, cz=cz)
    cf = S.Face(cpts, GRAPHITE)
    cf.d.rectangle([cf.w * 0.03, cf.h * 0.1, cf.w * 0.97, cf.h * 0.9], outline=MUSTARD, width=max(3, int(cf.h * 0.05)))
    cf.text("WORKSHOP", int(cf.h * 0.42), MUSTARD)
    cf.worn(72, (80, 80, 76), amount=0.08)
    box(g, (0, -0.035, cz), (0.035, 0.012, 0.022), "galv", bevel=0.003)
    B.prism(g, cpts, -0.024, -0.016, "graphite", front=B.decal(mats, "workshop_plate", cf.im, rough=0.5))
    for sx in (-1, 1):
        B.bolt(g, (sx * (chw - 0.018), -0.016, cz), (0, 1, 0), 0.005, "galv")
    return B.one("WorkshopSign", g, mats)


def warehouse_key_sign(mats):
    """Warehouse Key Sign, 3 x 6 x 1: a mustard plate in a graphite border on the road-sign post, the Warehouse Key
    keycard drawn on it."""
    f = S._plate_face(S.SQUARE)
    f.plate(MUSTARD, GRAPHITE, 0.88)
    card = keycard(f.w * 0.48, 14)
    f.im.paste(card, (int(f.w / 2 - card.width / 2), int(f.h / 2 - card.height / 2)), card)
    f.d = ImageDraw.Draw(f.im)
    return S.road_sign(mats, "WarehouseKeySign", S.SQUARE, "warehouse_key_face", f.worn(73, (180, 150, 50)))


def speed_limit_sign(mats):
    """Speed Limit Sign, 3 x 6 x 1: a round white plate with a signal-red rim and a graphite 30 on the road-sign
    post."""
    f = S._plate_face(S.DISC)
    f.plate(WHITE, SIGNAL, 0.78)
    f.d.polygon(f.outline, outline=(170, 168, 160), width=4)
    f.text("30", int(f.h * 0.4), GRAPHITE, cy=0.52)
    return S.road_sign(mats, "SpeedLimitSign", S.DISC, "speed_limit_face", f.worn(74, (170, 165, 155), amount=0.06))


# =====================================================================================================
# The Barrier Tape
# =====================================================================================================

def tape_image(length_m, width_m):
    """Signal-red and bone diagonal stripes along the tape, a thin red edge each side."""
    h = 48
    w = int(h * length_m / width_m)
    im = Image.new("RGB", (w, h), BONE)
    d = ImageDraw.Draw(im)
    step = h * 2.2
    k = -2
    while k * step < w + h:
        x = k * step
        d.polygon([(x, h), (x + step / 2, h), (x + step / 2 + h, 0), (x + h, 0)], fill=SIGNAL)
        k += 1
    d.rectangle([0, 0, w, 2], fill=SIGNAL)
    d.rectangle([0, h - 3, w, h], fill=SIGNAL)
    return im


def tape(g, mat, xa, xb, z, sag, half_w, steps=24):
    """A ribbon from x=xa to x=xb hanging `sag` m at its middle, in the x-z plane, decalled on both sides."""
    pts = []
    for k in range(steps + 1):
        t = k / steps
        x = xa + (xb - xa) * t
        pts.append((x, z - sag * 4 * t * (1 - t)))
    for k in range(steps):
        (x0, z0), (x1, z1) = pts[k], pts[k + 1]
        u0, u1 = k / steps, (k + 1) / steps
        for y, flip in ((0.001, False), (-0.001, True)):
            quad = [(x0, y, z0 - half_w), (x1, y, z1 - half_w), (x1, y, z1 + half_w), (x0, y, z0 + half_w)]
            uv = [(u0, 0.0), (u1, 0.0), (u1, 1.0), (u0, 1.0)]
            if flip:
                quad, uv = quad[::-1], uv[::-1]
            g.add(quad, [(0, 1, 2, 3)], mat, uvs=[uv])


def barrier_post(g, x, z0, z1, head_dir):
    """A steel-blue stanchion on a weighted foot (a graphite rubber ring under a steel-blue cast weight), its graphite
    head the tape's cassette with a slot facing `head_dir` (+1 or -1 along x)."""
    cylinder(g, (x, 0, z0), (x, 0, z0 + 0.018), 0.115, "rubber", segs=28)
    lathe(g, [(0.0, z0 + 0.018), (0.105, z0 + 0.018), (0.1, z0 + 0.04), (0.07, z0 + 0.065), (0.035, z0 + 0.08), (0.0, z0 + 0.08)],
          "steel_blue", steps=28, center=(x, 0))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        B.bolt(g, (x + 0.08 * math.cos(a), 0.08 * math.sin(a), z0 + 0.032), (0, 0, 1), 0.006, "graphite")
    cylinder(g, (x, 0, z0 + 0.075), (x, 0, z1 - 0.07), 0.024, "steel_blue", segs=18)
    cylinder(g, (x, 0, z0 + 0.075), (x, 0, z0 + 0.1), 0.03, "graphite", segs=18)
    # the cassette head: a graphite drum with a galvanised slot plate toward the tape, a mustard cap
    cylinder(g, (x, 0, z1 - 0.075), (x, 0, z1 - 0.012), 0.042, "graphite", segs=22)
    cylinder(g, (x, 0, z1 - 0.012), (x, 0, z1), 0.036, "mustard", segs=22)
    box(g, (x + head_dir * 0.042, 0, z1 - 0.045), (0.006, 0.022, 0.03), "galv", bevel=0.002)
    box(g, (x + head_dir * 0.0475, 0, z1 - 0.045), (0.0015, 0.004, 0.026), "hole")


def barrier_tape(mats):
    """Barrier Tape, 8 x 4 x 1: two steel-blue stanchions on weighted feet, a signal-red and bone striped tape pulled
    from one cassette head and hooked into the other, sagging a little."""
    g = Geo()
    z0, z1 = -0.5, 0.42
    xs = 0.86
    barrier_post(g, -xs, z0, z1, 1)
    barrier_post(g, xs, z0, z1, -1)
    xa, xb, zt, hw = -xs + 0.048, xs - 0.048, z1 - 0.045, 0.022
    tape(g, B.decal(mats, "barrier_tape", tape_image(xb - xa, 2 * hw), rough=0.45), xa, xb, zt, 0.035, hw)
    # the hook end clipped into the far head
    box(g, (xb - 0.012, 0, zt), (0.012, 0.004, hw + 0.004), "graphite", bevel=0.002)
    return B.one("BarrierTape", g, mats)


def _title(name, size):
    return f"{name} ({size} blocks)"


# (id, builder, half size (m), icon ortho, title, notes) as tools/parts_building's PARTS
PARTS = [
    ("outpost_sign", outpost_sign, (0.375, 0.125, 0.75), 1.75, _title("Workshop Sign", "3 x 6 x 1"),
     ["Bone panel in a steel-blue frame on a graphite post: a spanner over a cog, WORKSHOP name plate."]),
    ("warehouse_key_sign", warehouse_key_sign, (0.375, 0.125, 0.75), 1.75, _title("Warehouse Key Sign", "3 x 6 x 1"),
     ["Mustard plate, graphite border: the Warehouse Key keycard (steel-blue grip, WH, chip, door print)."]),
    ("speed_limit_sign", speed_limit_sign, (0.375, 0.125, 0.75), 1.75, _title("Speed Limit Sign", "3 x 6 x 1"),
     ["Round white plate with a signal-red rim and a graphite 30, on the batch 12a sign post."]),
    ("barrier_tape", barrier_tape, (1.0, 0.125, 0.5), 2.3, _title("Barrier Tape", "8 x 4 x 1"),
     ["Two steel-blue stanchions on weighted feet, a signal-red and bone striped tape between them (no orange)."]),
]
