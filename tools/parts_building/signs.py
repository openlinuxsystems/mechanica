"""Models of the parts map's batch 12: road and site signs, the farm signs, the Exit Sign, the Antenna, the Traffic
Cone, the Banner Holder and the Billboard.

Road and site signs are Colony Works: a pressed plate on a graphite post with a bolted foot, its face a pictogram
(art rule 7: no long text; the Stop Sign keeps STOP and the Exit Sign EXIT, one short word each). The farm signs and
the Billboard are Settler Patchwork: scrap planks on stakes, painted by hand, worn. The faces are drawn here with PIL
and decalled on the plates (tools/part_kit/building_kit.py). Each builder takes the materials and returns the root.
"""

from __future__ import annotations

import math
import random

import numpy as np
from mathutils import Vector
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import building_kit as B
import part_kit as K
from part_kit import Geo, box, cylinder

PX = 512
BONE, STEEL, MUSTARD, GRAPHITE, SIGNAL = (217, 209, 188), (62, 106, 140), (224, 181, 48), (43, 47, 53), (200, 66, 58)
TEAL, BRICK, CYAN = (79, 154, 147), (168, 71, 59), (56, 200, 255)


def font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
              "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default()


# =====================================================================================================
# Faces (PIL): the image covers the plate's bounds as seen from the front
# =====================================================================================================

class Face:
    """A canvas over a plate's (x, z) bounds, with the plate outline in pixels."""

    def __init__(self, pts, bg=(0, 0, 0)):
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        self.x0, self.x1, self.z0, self.z1 = min(xs), max(xs), min(zs), max(zs)
        w, h = self.x1 - self.x0, self.z1 - self.z0
        s = PX / max(w, h)
        self.w, self.h = int(round(w * s)), int(round(h * s))
        self.im = Image.new("RGB", (self.w, self.h), bg)
        self.d = ImageDraw.Draw(self.im)
        self.outline = [self.px(p) for p in pts]

    def px(self, p):
        x, z = p
        return ((self.x1 - x) / (self.x1 - self.x0) * self.w, (self.z1 - z) / (self.z1 - self.z0) * self.h)

    def inset(self, k):
        cx = sum(p[0] for p in self.outline) / len(self.outline)
        cy = sum(p[1] for p in self.outline) / len(self.outline)
        return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in self.outline]

    def plate(self, fill, border, k=0.88):
        self.d.polygon(self.outline, fill=border)
        self.d.polygon(self.inset(k), fill=fill)

    def text(self, s, size, fill, cy=0.5, cx=0.5):
        f = font(size)
        l, t, r, b = self.d.textbbox((0, 0), s, font=f)
        self.d.text((self.w * cx - (r - l) / 2 - l, self.h * cy - (b - t) / 2 - t), s, font=f, fill=fill)

    def worn(self, seed, base, amount=0.08):
        """Scuffs and grime over the face (worn, not filthy)."""
        rng = np.random.default_rng(seed)
        a = np.asarray(self.im, dtype=np.float32)
        h, w = a.shape[:2]
        noise = Image.fromarray((rng.random((h // 8, w // 8)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        n = np.asarray(noise, dtype=np.float32)[..., None] / 255.0
        a = a * (1 - amount * n)
        for _ in range(60):
            x, y = rng.integers(0, w), rng.integers(0, h)
            ln, ang = rng.integers(6, 40), rng.uniform(0, math.pi)
            dx, dy = math.cos(ang) * ln, math.sin(ang) * ln
            img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
            ImageDraw.Draw(img).line([(x, y), (x + dx, y + dy)], fill=base, width=1)
            a = np.asarray(img, dtype=np.float32)
        self.im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
        self.d = ImageDraw.Draw(self.im)
        return self.im


def figure(d, x, y, s, fill, pose="stand"):
    """A pictogram person, `s` pixels tall, feet at (x, y)."""
    w = max(3, int(s * 0.11))
    head = s * 0.09
    hip = (x, y - s * 0.45)
    neck = (x, y - s * 0.78)
    if pose == "dig":
        neck = (x - s * 0.12, y - s * 0.72)
        d.line([hip, (x - s * 0.18, y), ], fill=fill, width=w)
        d.line([hip, (x + s * 0.2, y), ], fill=fill, width=w)
        d.line([hip, neck], fill=fill, width=w)
        d.line([neck, (x + s * 0.18, y - s * 0.5)], fill=fill, width=w)
        d.line([(x + s * 0.02, y - s * 0.75), (x + s * 0.5, y - s * 0.05)], fill=fill, width=max(2, w // 2))
        d.polygon([(x + s * 0.42, y - s * 0.12), (x + s * 0.6, y + s * 0.02), (x + s * 0.48, y + s * 0.06)], fill=fill)
    elif pose == "run":
        neck = (x + s * 0.08, y - s * 0.76)
        d.line([hip, (x - s * 0.25, y - s * 0.2), (x - s * 0.32, y)], fill=fill, width=w, joint="curve")
        d.line([hip, (x + s * 0.16, y - s * 0.22), (x + s * 0.32, y - s * 0.12)], fill=fill, width=w, joint="curve")
        d.line([hip, neck], fill=fill, width=w)
        d.line([neck, (x - s * 0.18, y - s * 0.5)], fill=fill, width=w)
        d.line([neck, (x + s * 0.3, y - s * 0.6)], fill=fill, width=w)
    else:   # arms up, shielding the head
        d.line([hip, (x - s * 0.15, y)], fill=fill, width=w)
        d.line([hip, (x + s * 0.15, y)], fill=fill, width=w)
        d.line([hip, neck], fill=fill, width=w)
        d.line([neck, (x - s * 0.22, y - s * 0.95)], fill=fill, width=w)
        d.line([neck, (x + s * 0.22, y - s * 0.95)], fill=fill, width=w)
    hx, hy = neck[0], neck[1] - head * 1.3
    d.ellipse([hx - head, hy - head, hx + head, hy + head], fill=fill)


def arrow(d, cx, cy, size, fill, angle):
    """A block arrow pointing `angle` degrees (0 right, 90 up) round (cx, cy)."""
    pts = [(-0.5, -0.14), (0.1, -0.14), (0.1, -0.34), (0.5, 0.0), (0.1, 0.34), (0.1, 0.14), (-0.5, 0.14)]
    a = math.radians(angle)
    c, s = math.cos(a), math.sin(a)
    d.polygon([(cx + size * (px * c - py * s), cy - size * (px * s + py * c)) for px, py in pts], fill=fill)


def thresher(d, cx, cy, s, fill, bg):
    """The Thresher pictogram: a boxy harvester bot on wheels with its reel of blades out front."""
    d.rounded_rectangle([cx - s * 0.35, cy - s * 0.25, cx + s * 0.25, cy + s * 0.15], radius=s * 0.06, fill=fill)
    d.rectangle([cx - s * 0.2, cy - s * 0.45, cx + s * 0.05, cy - s * 0.25], fill=fill)
    d.ellipse([cx - s * 0.14, cy - s * 0.4, cx - s * 0.02, cy - s * 0.3], fill=bg)
    for wx in (-0.25, 0.1):
        d.ellipse([cx + s * (wx - 0.11), cy + s * 0.08, cx + s * (wx + 0.11), cy + s * 0.3], fill=fill)
        d.ellipse([cx + s * (wx - 0.04), cy + s * 0.15, cx + s * (wx + 0.04), cy + s * 0.23], fill=bg)
    d.line([(cx + s * 0.25, cy - s * 0.05), (cx + s * 0.42, cy - s * 0.05)], fill=fill, width=int(s * 0.05))
    d.ellipse([cx + s * 0.3, cy - s * 0.2, cx + s * 0.56, cy + s * 0.1], outline=fill, width=int(s * 0.04))
    for k in range(6):
        a = k * math.pi / 3
        d.line([(cx + s * 0.43, cy - s * 0.05), (cx + s * 0.43 + s * 0.16 * math.cos(a), cy - s * 0.05 + s * 0.16 * math.sin(a))],
               fill=fill, width=int(s * 0.03))


def skull(d, cx, cy, s, fill, bg):
    """A skull over two crossed wrenches."""
    for sgn in (-1, 1):
        a = (cx - sgn * s * 0.45, cy - s * 0.3)
        b = (cx + sgn * s * 0.45, cy + s * 0.45)
        d.line([a, b], fill=fill, width=int(s * 0.09))
        for p in (a, b):
            r = s * 0.1
            d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)
            d.ellipse([p[0] - r * 0.45, p[1] - r * 0.45, p[0] + r * 0.45, p[1] + r * 0.45], fill=bg)
    d.ellipse([cx - s * 0.3, cy - s * 0.42, cx + s * 0.3, cy + s * 0.12], fill=fill)
    d.rounded_rectangle([cx - s * 0.17, cy, cx + s * 0.17, cy + s * 0.26], radius=s * 0.04, fill=fill)
    for ex in (-0.12, 0.12):
        d.ellipse([cx + s * (ex - 0.08), cy - s * 0.2, cx + s * (ex + 0.08), cy - s * 0.04], fill=bg)
    d.polygon([(cx, cy - s * 0.02), (cx - s * 0.04, cy + s * 0.06), (cx + s * 0.04, cy + s * 0.06)], fill=bg)
    for tx in (-0.08, 0.0, 0.08):
        d.line([(cx + s * tx, cy + s * 0.14), (cx + s * tx, cy + s * 0.26)], fill=bg, width=int(s * 0.02))


# =====================================================================================================
# Colony road and site signs
# =====================================================================================================

PLATE_Z = 0.42      # the plate's centre on a 1.5 m tall sign


def road_sign(mats, name, pts, face_name, face_img):
    """A Colony road sign: the plate `pts` (x, z round the plate centre) on a graphite post with a bolted foot."""
    g = Geo()
    B.post(g, 0, -0.04, -0.75, PLATE_Z + 0.12, half=0.024, mat="graphite", cap="graphite", foot_half=0.06)
    for dz in (-0.12, 0.12):
        box(g, (0, -0.04, PLATE_Z + dz), (0.04, 0.03, 0.018), "galv", bevel=0.004)
        for sx in (-1, 1):
            B.bolt(g, (sx * 0.03, 0.0005, PLATE_Z + dz), (0, 1, 0), 0.006, "galv")
    plate = [(x, z + PLATE_Z) for x, z in pts]
    B.prism(g, plate, -0.01, 0.0, "galv", front=B.decal(mats, face_name, face_img, rough=0.5))
    return B.one(name, g, mats)


def _plate_face(pts):
    return Face([(x, z + PLATE_Z) for x, z in pts])


OCTAGON = B.regular(8, 0.3, phase=math.pi / 8)
TRIANGLE = [(0.0, 0.3), (-0.34, -0.27), (0.34, -0.27)]
DISC = B.regular(32, 0.3)
WIDE = B.rounded_rect(0.34, 0.2, 0.03)
SQUARE = B.rounded_rect(0.28, 0.28, 0.03)
DIAMOND = B.regular(4, 0.32, phase=math.pi / 2)


def stop_sign(mats):
    f = _plate_face(OCTAGON)
    f.plate(SIGNAL, BONE, 0.9)
    f.text("STOP", 150, BONE)
    return road_sign(mats, "StopSign", OCTAGON, "stop_face", f.worn(1, (160, 60, 52)))


def no_entry_sign(mats):
    f = _plate_face(DISC)
    f.plate(SIGNAL, BONE, 0.92)
    f.d.rounded_rectangle([f.w * 0.18, f.h * 0.42, f.w * 0.82, f.h * 0.58], radius=10, fill=BONE)
    return road_sign(mats, "NoEntrySign", DISC, "no_entry_face", f.worn(2, (160, 60, 52)))


def caution_sign(mats):
    f = _plate_face(TRIANGLE)
    f.plate(MUSTARD, GRAPHITE, 0.8)
    f.d.polygon([(f.w * 0.46, f.h * 0.36), (f.w * 0.54, f.h * 0.36), (f.w * 0.525, f.h * 0.7), (f.w * 0.475, f.h * 0.7)], fill=GRAPHITE)
    f.d.ellipse([f.w * 0.46, f.h * 0.74, f.w * 0.54, f.h * 0.82], fill=GRAPHITE)
    return road_sign(mats, "CautionSign", TRIANGLE, "caution_face", f.worn(3, (180, 150, 50)))


def danger_sign(mats):
    """Danger: a bone plate, a signal-red band with a bone warning triangle and a lightning bolt, hazard stripes."""
    f = _plate_face(WIDE)
    f.plate(BONE, GRAPHITE, 0.95)
    d = f.d
    d.rectangle([f.w * 0.06, f.h * 0.1, f.w * 0.94, f.h * 0.6], fill=SIGNAL)
    d.polygon([(f.w * 0.5, f.h * 0.14), (f.w * 0.36, f.h * 0.56), (f.w * 0.64, f.h * 0.56)], fill=BONE)
    d.polygon([(f.w * 0.51, f.h * 0.24), (f.w * 0.46, f.h * 0.4), (f.w * 0.505, f.h * 0.4), (f.w * 0.48, f.h * 0.53),
               (f.w * 0.55, f.h * 0.34), (f.w * 0.505, f.h * 0.34), (f.w * 0.53, f.h * 0.24)], fill=SIGNAL)
    top, bot = f.h * 0.68, f.h * 0.88
    d.rectangle([f.w * 0.06, top, f.w * 0.94, bot], fill=MUSTARD)
    for k in range(-2, 12):
        x = f.w * (0.06 + k * 0.09)
        d.polygon([(x, bot), (x + f.w * 0.045, bot), (x + f.w * 0.045 + (bot - top), top), (x + (bot - top), top)], fill=GRAPHITE)
    d.rectangle([0, top, f.w * 0.06, bot], fill=BONE)
    d.rectangle([f.w * 0.94, top, f.w, bot], fill=BONE)
    d.polygon(f.outline, outline=GRAPHITE, width=12)
    return road_sign(mats, "DangerSign", WIDE, "danger_face", f.worn(4, (150, 140, 120)))


def arrow_sign(mats, name, angle):
    f = _plate_face(WIDE)
    f.plate(STEEL, BONE, 0.92)
    arrow(f.d, f.w / 2, f.h / 2, f.h * 0.62 if angle != 90 else f.h * 0.6, BONE, angle)
    return road_sign(mats, name, WIDE, f"{name}_face", f.worn(5 + angle, (90, 120, 145)))


def direction_arrow_left(mats):
    return arrow_sign(mats, "ArrowLeft", 180)


def direction_arrow_straight(mats):
    return arrow_sign(mats, "ArrowStraight", 90)


def direction_arrow_right(mats):
    return arrow_sign(mats, "ArrowRight", 0)


def construction_sign(mats):
    """Construction: a mustard diamond with a digging worker over a mound."""
    f = _plate_face(DIAMOND)
    f.plate(MUSTARD, GRAPHITE, 0.86)
    figure(f.d, f.w * 0.44, f.h * 0.68, f.h * 0.42, GRAPHITE, "dig")
    f.d.polygon([(f.w * 0.5, f.h * 0.72), (f.w * 0.62, f.h * 0.6), (f.w * 0.76, f.h * 0.72)], fill=GRAPHITE)
    return road_sign(mats, "ConstructionSign", DIAMOND, "construction_face", f.worn(10, (180, 150, 50)))


def falling_objects_sign(mats):
    """Falling objects: a mustard triangle, a person shielding their head from blocks coming down."""
    f = _plate_face(TRIANGLE)
    f.plate(MUSTARD, GRAPHITE, 0.8)
    figure(f.d, f.w * 0.44, f.h * 0.8, f.h * 0.33, GRAPHITE, "shield")
    for (bx, by, a) in ((0.56, 0.4, 20), (0.5, 0.28, -15), (0.6, 0.55, 40)):
        s = f.h * 0.045
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        f.d.polygon([(f.w * bx + s * (px * c - py * sn), f.h * by + s * (px * sn + py * c))
                     for px, py in ((-1, -1), (1, -1), (1, 1), (-1, 1))], fill=GRAPHITE)
    return road_sign(mats, "FallingObjectsSign", TRIANGLE, "falling_face", f.worn(11, (180, 150, 50)))


def beware_bots_sign(mats):
    """Beware of bots: a bone plate in a signal-red border, a Thresher in graphite."""
    f = _plate_face(SQUARE)
    f.plate(BONE, SIGNAL, 0.88)
    thresher(f.d, f.w * 0.47, f.h * 0.52, f.w * 0.7, GRAPHITE, BONE)
    return road_sign(mats, "BewareBotsSign", SQUARE, "beware_face", f.worn(12, (150, 140, 120)))


def skull_sign(mats):
    """Skull: a graphite plate in a mustard border, a mustard skull over crossed wrenches."""
    f = _plate_face(SQUARE)
    f.plate(GRAPHITE, MUSTARD, 0.9)
    skull(f.d, f.w / 2, f.h / 2, f.w * 0.75, MUSTARD, GRAPHITE)
    return road_sign(mats, "SkullSign", SQUARE, "skull_face", f.worn(13, (90, 90, 80), 0.12))


def exit_sign(mats):
    """Exit Sign, 3 x 1 x 1: a graphite lightbox on a wall bracket (its back), a cyan running figure and EXIT lit on
    its dark face: cyan, because it still has power."""
    g = Geo()
    box(g, (0, -0.105, 0), (0.08, 0.02, 0.05), "graphite", bevel=0.004)
    for sx in (-1, 1):
        B.bolt(g, (sx * 0.055, -0.085, 0), (0, 1, 0), 0.006)
    box(g, (0, -0.03, 0), (0.33, 0.06, 0.1), "graphite", bevel=0.008)
    box(g, (0, -0.03, 0.1), (0.31, 0.055, 0.006), "steel_blue", bevel=0.002)
    pts = B.rounded_rect(0.31, 0.08, 0.012)
    f = Face(pts, (10, 18, 22))
    glow = Image.new("RGB", (f.w, f.h), (0, 0, 0))
    for im, col in ((f.im, CYAN), (glow, CYAN)):
        d = ImageDraw.Draw(im)
        figure(d, f.w * 0.17, f.h * 0.86, f.h * 0.72, col, "run")
        arrow(d, f.w * 0.88, f.h * 0.5, f.h * 0.5, col, 0)
    for im in (f.im, glow):
        d = ImageDraw.Draw(im)
        fnt = font(int(f.h * 0.62))
        l, t, r, b = d.textbbox((0, 0), "EXIT", font=fnt)
        d.text((f.w * 0.53 - (r - l) / 2 - l, f.h * 0.5 - (b - t) / 2 - t), "EXIT", font=fnt, fill=CYAN)
    glow = glow.filter(ImageFilter.GaussianBlur(1.5))
    B.prism(g, pts, 0.03, 0.032, "graphite", front=B.glow_decal(mats, "exit_face", f.im, glow, 3.0))
    return B.one("ExitSign", g, mats)


# =====================================================================================================
# Settler Patchwork signs
# =====================================================================================================

def board_image(w_m, h_m, seed, paint=None, planks=3, bare_at=0.72):
    """Scrap planks painted over by hand: grain, plank seams, faded paint with the wood showing through."""
    s = PX / max(w_m, h_m)
    w, h = int(w_m * s), int(h_m * s)
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    grain = np.sin(xx / w * 40 + np.sin(yy / 7.0 + rng.uniform(0, 6)) * 1.5 + rng.uniform(0, 6)) * 0.5 + 0.5
    base = np.array(paint or (156, 118, 80), dtype=np.float32)
    wood = np.array((140, 104, 70), dtype=np.float32)
    a = base[None, None, :] * (0.88 + 0.12 * grain[..., None])
    if paint is not None:
        bare = Image.fromarray((rng.random((h // 16 + 1, w // 16 + 1)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        m = np.clip((np.asarray(bare, np.float32) / 255 - bare_at) * 6, 0, 1)[..., None]
        a = a * (1 - m) + wood[None, None, :] * (0.85 + 0.15 * grain[..., None]) * m
    for k in range(1, planks):
        y = int(h * k / planks)
        a[max(0, y - 3):y + 3] *= 0.45
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def wear_paint(im, seed, wood=(140, 104, 70), amount=0.25):
    """Rubs painted letters back to the wood in patches."""
    rng = np.random.default_rng(seed)
    w, h = im.size
    m = Image.fromarray((rng.random((h // 10 + 1, w // 10 + 1)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    m = np.clip((np.asarray(m, np.float32) / 255 - (1 - amount)) * 5, 0, 1)[..., None]
    a = np.asarray(im, np.float32)
    return Image.fromarray(np.clip(a * (1 - 0.6 * m) + np.array(wood) * 0.6 * m, 0, 255).astype(np.uint8))


def plank_board(g, mats, hw, hh, z, face, back=None, planks=3, t=0.022):
    """A board of `planks` planks (x hw, z hh round z), its front decalled with `face` (and back with `back`)."""
    rnd = random.Random(int(hw * 1000 + hh * 100))
    ph = hh / planks
    for k in range(planks):
        zc = z - hh + ph * (2 * k + 1)
        box(g, (rnd.uniform(-0.01, 0.01), -t / 2, zc), (hw, t / 2, ph * 0.97), "plank", bevel=0.003)
        for sx in (-1, 1):
            cylinder(g, (sx * (hw - 0.08), 0.001, zc), (sx * (hw - 0.08), 0.004, zc), 0.006, "zinc", segs=8)
    pts = [(hw, z - hh), (-hw, z - hh), (-hw, z + hh), (hw, z + hh)]
    B.prism(g, pts, -t - 0.0015, 0.0015, "plank", front=face, back=back)


def stakes(g, xs, z0, z1, y=-0.04):
    for x in xs:
        box(g, (x, y, (z0 + z1) / 2), (0.03, 0.018, (z1 - z0) / 2), "plank_dark", bevel=0.004)
        dz = 0.06
        cylinder(g, (x, y + 0.02, z1 - dz), (x, y + 0.025, z1 - dz), 0.008, "zinc", segs=8)


def hard_work_sign(mats):
    """Hard Work Sign, 4 x 3 x 1: a plank board on two stakes, a mustard hammer and wrench crossed over a sea-teal
    rising sun (hard work, a new day)."""
    g = Geo()
    hw, hh, z = 0.46, 0.17, 0.18
    im = board_image(2 * hw, 2 * hh, 21)
    d = ImageDraw.Draw(im)
    w, h = im.size
    cx, cy = w / 2, h * 0.62
    d.pieslice([cx - h * 0.42, cy - h * 0.42, cx + h * 0.42, cy + h * 0.42], 180, 360, fill=TEAL)
    for k in range(9):
        a = math.pi + math.pi * (k + 0.5) / 9
        d.line([(cx + h * 0.5 * math.cos(a), cy + h * 0.5 * math.sin(a)), (cx + h * 0.62 * math.cos(a), cy + h * 0.62 * math.sin(a))],
               fill=TEAL, width=8)
    d.rectangle([w * 0.1, cy, w * 0.9, cy + 8], fill=TEAL)
    # the hammer and the wrench, crossed
    d.line([(cx - h * 0.45, h * 0.9), (cx + h * 0.35, h * 0.15)], fill=MUSTARD, width=16)
    d.polygon([(cx + h * 0.22, h * 0.02), (cx + h * 0.55, h * 0.3), (cx + h * 0.45, h * 0.4), (cx + h * 0.12, h * 0.12)], fill=MUSTARD)
    d.line([(cx + h * 0.45, h * 0.9), (cx - h * 0.3, h * 0.2)], fill=MUSTARD, width=14)
    d.ellipse([cx - h * 0.46, h * 0.04, cx - h * 0.14, h * 0.36], fill=MUSTARD)
    d.rectangle([cx - h * 0.36, h * 0.0, cx - h * 0.24, h * 0.2], fill=(140, 104, 70))
    im = wear_paint(im, 22)
    plank_board(g, mats, hw, hh, z, B.decal(mats, "hard_work_face", im, rough=0.8))
    stakes(g, (-0.3, 0.3), -0.375, z + hh - 0.02)
    return B.one("HardWorkSign", g, mats)


def sale_sign(mats):
    """Sale Sign, 4 x 3 x 1: two-sided, as at the farmer's trade counter: SALE in brick red on bone paint on one side,
    a price tag with a coin on the other."""
    g = Geo()
    hw, hh, z = 0.44, 0.16, 0.19
    front = board_image(2 * hw, 2 * hh, 31, paint=BONE, bare_at=0.85)
    d = ImageDraw.Draw(front)
    w, h = front.size
    fnt = font(int(h * 0.7))
    l, t, r, b = d.textbbox((0, 0), "SALE", font=fnt)
    d.text((w / 2 - (r - l) / 2 - l, h / 2 - (b - t) / 2 - t), "SALE", font=fnt, fill=BRICK)
    front = wear_paint(front, 32, amount=0.12)
    back = board_image(2 * hw, 2 * hh, 33, paint=BONE, bare_at=0.85)
    d = ImageDraw.Draw(back)
    d.polygon([(w * 0.3, h * 0.25), (w * 0.62, h * 0.25), (w * 0.72, h * 0.5), (w * 0.62, h * 0.75), (w * 0.3, h * 0.75)], fill=BRICK)
    d.ellipse([w * 0.6, h * 0.44, w * 0.64, h * 0.56], fill=BONE)
    d.ellipse([w * 0.37, h * 0.32, w * 0.55, h * 0.68], fill=MUSTARD)
    d.ellipse([w * 0.41, h * 0.38, w * 0.51, h * 0.62], outline=(170, 130, 30), width=6)
    back = wear_paint(back, 34)
    # the back decal is mapped from the front; mirror it so it reads the right way from behind
    back = back.transpose(Image.FLIP_LEFT_RIGHT)
    plank_board(g, mats, hw, hh, z, B.decal(mats, "sale_front", front, rough=0.8), B.decal(mats, "sale_back", back, rough=0.8))
    stakes(g, (-0.3, 0.3), -0.375, z + hh - 0.02, y=-0.011)
    return B.one("SaleSign", g, mats)


def welcome_sign(mats):
    """Welcome Sign, 6 x 3 x 1: a sea-teal painted plank board, WELCOME in bone with a mustard flower each side, on
    two stakes."""
    g = Geo()
    hw, hh, z = 0.7, 0.13, 0.22
    im = board_image(2 * hw, 2 * hh, 41, paint=TEAL, planks=2)
    d = ImageDraw.Draw(im)
    w, h = im.size
    fnt = font(int(h * 0.62))
    l, t, r, b = d.textbbox((0, 0), "WELCOME", font=fnt)
    d.text((w / 2 - (r - l) / 2 - l, h / 2 - (b - t) / 2 - t), "WELCOME", font=fnt, fill=BONE)
    for fx in (0.07, 0.93):
        cx, cy = w * fx, h * 0.5
        for k in range(6):
            a = k * math.pi / 3
            px, py = cx + h * 0.16 * math.cos(a), cy + h * 0.16 * math.sin(a)
            d.ellipse([px - h * 0.1, py - h * 0.1, px + h * 0.1, py + h * 0.1], fill=MUSTARD)
        d.ellipse([cx - h * 0.08, cy - h * 0.08, cx + h * 0.08, cy + h * 0.08], fill=BRICK)
    im = wear_paint(im, 42, wood=(140, 104, 70), amount=0.2)
    plank_board(g, mats, hw, hh, z, B.decal(mats, "welcome_face", im, rough=0.8), planks=2)
    stakes(g, (-0.55, 0.55), -0.375, z + hh - 0.02)
    return B.one("WelcomeSign", g, mats)


# =====================================================================================================
# Signs players write on (their words are drawn in game from the part's text setting, display.text)
# =====================================================================================================

def signboard(mats):
    """Signboard, 6 x 4 x 1: a Settler board of three scrap planks painted bone, left blank to be written on, a
    graphite paint drip and a brick-red corner mark, nailed to two stakes. The game paints its words on the bone."""
    g = Geo()
    hw, hh, z = 0.68, 0.25, 0.2
    im = board_image(2 * hw, 2 * hh, 51, paint=BONE, bare_at=0.86)
    d = ImageDraw.Draw(im)
    w, h = im.size
    # a painted border just inside the edge, rubbed away in places, and a maker's mark in one corner
    m = h * 0.05
    d.rectangle([m, m, w - m, h - m], outline=GRAPHITE, width=max(4, int(h * 0.018)))
    d.polygon([(w - m * 3.6, h - m * 1.6), (w - m * 1.6, h - m * 1.6), (w - m * 1.6, h - m * 3.6)], fill=BRICK)
    im = wear_paint(im, 52, amount=0.1)
    plank_board(g, mats, hw, hh, z, B.decal(mats, "signboard_face", im, rough=0.85))
    stakes(g, (-0.5, 0.5), -0.5, z + hh - 0.02)
    return B.one("Signboard", g, mats)


def colony_sign(mats):
    """Colony Sign, 4 x 6 x 1: a wide Colony plate on two graphite posts, a bone face inside a steel-blue band with
    a mustard rule at the top, for place names and distances. The game prints its words on the bone."""
    g = Geo()
    hw, hh = 0.44, 0.27
    for x in (-0.3, 0.3):
        B.post(g, x, -0.04, -0.75, PLATE_Z + hh - 0.04, half=0.022, mat="graphite", cap="graphite", foot_half=0.055)
        for dz in (-0.14, 0.14):
            box(g, (x, -0.04, PLATE_Z + dz), (0.035, 0.03, 0.016), "galv", bevel=0.004)
            B.bolt(g, (x, 0.0005, PLATE_Z + dz), (0, 1, 0), 0.006, "galv")
    pts = [(x, z + PLATE_Z) for x, z in B.rounded_rect(hw, hh, 0.035)]
    f = Face(pts)
    f.plate(STEEL, STEEL)
    f.d.polygon(f.inset(0.9), fill=BONE)
    band = f.h * 0.09
    f.d.rectangle([f.w * 0.08, f.h * 0.06, f.w * 0.92, f.h * 0.06 + band * 0.55], fill=MUSTARD)
    f.worn(53, BONE, amount=0.06)
    B.prism(g, pts, -0.01, 0.0, "galv", front=B.decal(mats, "colony_sign_face", f.im, rough=0.5))
    return B.one("ColonySign", g, mats)


def paper_note(mats):
    """Paper Note, 1 x 1 x 1: a sheet of ruled notepaper, its top corner curling, held flat to a wall, a door or a
    vehicle by a strip of tape. The game writes its words on it in pencil."""
    g = Geo()
    pw, ph = 0.09, 0.115          # half width and height of the sheet
    y = -0.12                     # its back against the cell's back face
    s = PX / (2 * ph)
    w, h = int(2 * pw * s), int(2 * ph * s)
    rng = np.random.default_rng(61)
    paper = np.array((236, 230, 212), np.float32)
    a = np.ones((h, w, 3), np.float32) * paper * (0.96 + 0.04 * rng.random((h, w, 1)))
    im = Image.fromarray(a.astype(np.uint8))
    d = ImageDraw.Draw(im)
    for k in range(2, 14):
        yy = int(h * k / 14)
        d.line([(int(w * 0.05), yy), (int(w * 0.95), yy)], fill=(176, 196, 214), width=2)
    d.line([(int(w * 0.14), 0), (int(w * 0.14), h)], fill=(214, 150, 140), width=2)
    pts = [(pw, -ph), (-pw, -ph), (-pw, ph - 0.025), (-pw + 0.025, ph), (pw, ph)]
    B.prism(g, [(x, z) for x, z in pts], y, y + 0.0015, "white",
            front=B.decal(mats, "paper_note_face", im, rough=0.9), uv_box=(-pw, -ph, pw, ph))
    # the curled corner, folded forward
    B.prism(g, [(-pw, ph - 0.025), (-pw + 0.025, ph), (-pw, ph)], y + 0.0015, y + 0.006, "white")
    box(g, (0.0, y + 0.002, ph - 0.006), (0.03, 0.0008, 0.014), "glass_tint")
    return B.one("PaperNote", g, mats)


def billboard(mats):
    """Billboard, 8 x 8 x 2: a Settler farm billboard, a poster on a plank frame with a zinc back on two braced
    legs: a big tomato and a potato under a mustard sun over sea-teal fields (our own art, no brand), its top corner
    peeling."""
    g = Geo()
    hw, z0, z1 = 0.92, -0.05, 0.93
    zc, hh = (z0 + z1) / 2, (z1 - z0) / 2
    for sx in (-1, 1):
        x = sx * 0.62
        box(g, (x, -0.06, -0.5), (0.045, 0.035, 0.5 - 0.005), "plank_dark", bevel=0.005)
        B.bar(g, (x, -0.06, -0.15), (x, -0.23, -0.495), 0.02, 0.012, "plank_dark", 0.003, up=(1, 0, 0))
        box(g, (x, -0.06, -0.48), (0.07, 0.06, 0.02), "rust", bevel=0.004)
    box(g, (0, -0.045, zc), (hw, 0.01, hh), "zinc", bevel=0.003)
    for sz in (-1, 1):
        box(g, (0, -0.02, zc + sz * (hh + 0.03)), (hw + 0.05, 0.03, 0.03), "plank", bevel=0.004)
    for sx in (-1, 1):
        box(g, (sx * (hw + 0.02), -0.02, zc), (0.03, 0.03, hh), "plank", bevel=0.004)
    # the poster
    pw, ph = 2 * hw - 0.04, 2 * hh - 0.04
    s = PX / pw
    w, h = int(pw * s), int(ph * s)
    im = Image.new("RGB", (w, h), (196, 222, 214))
    d = ImageDraw.Draw(im)
    d.ellipse([w * 0.62, h * 0.06, w * 0.86, h * 0.06 + w * 0.24], fill=MUSTARD)
    for k in range(5):
        y = h * (0.62 + k * 0.08)
        d.polygon([(0, y), (w, y - h * 0.05), (w, h), (0, h)], fill=tuple(int(c * (0.9 - 0.05 * k)) for c in TEAL))
    for k in range(9):
        x = w * (0.05 + k * 0.11)
        d.line([(x, h * 0.98), (w * 0.5 + (x - w * 0.5) * 0.4, h * 0.62)], fill=(60, 120, 110), width=3)
    cx, cy, r = w * 0.36, h * 0.5, h * 0.24
    d.ellipse([cx - r, cy - r, cx + r, cy + r * 0.92], fill=BRICK)
    d.ellipse([cx - r * 0.6, cy - r * 0.7, cx - r * 0.2, cy - r * 0.3], fill=(214, 120, 100))
    for k in range(5):
        a = -math.pi / 2 + (k - 2) * 0.55
        d.polygon([(cx, cy - r * 0.92), (cx + r * 0.5 * math.cos(a - 0.2), cy - r * 0.92 + r * 0.5 * math.sin(a - 0.2) + r * 0.25),
                   (cx + r * 0.5 * math.cos(a + 0.2), cy - r * 0.92 + r * 0.5 * math.sin(a + 0.2) + r * 0.25)], fill=(70, 130, 60))
    # a potato beside it, with its eyes
    d.ellipse([w * 0.55, h * 0.4, w * 0.75, h * 0.78], fill=(176, 132, 84))
    d.ellipse([w * 0.58, h * 0.44, w * 0.66, h * 0.54], fill=(198, 156, 104))
    for ex, ey in ((0.62, 0.62), (0.69, 0.5), (0.66, 0.7)):
        d.ellipse([w * ex - 4, h * ey - 3, w * ex + 4, h * ey + 3], fill=(120, 88, 56))
    d.rectangle([0, 0, w - 1, h - 1], outline=BONE, width=10)
    poster = wear_paint(im, 51, wood=(200, 196, 180), amount=0.18)
    pts = [(hw - 0.02, zc - hh + 0.02), (-hw + 0.02, zc - hh + 0.02), (-hw + 0.02, zc + hh - 0.02), (hw - 0.02, zc + hh - 0.02)]
    B.prism(g, pts, -0.037, -0.034, "zinc", front=B.decal(mats, "billboard_poster", poster, rough=0.75))
    # the peeled corner (top left as you face it), curling forward
    cx, cz = hw - 0.12, z1 - 0.12
    for k in range(3):
        B.bar(g, (cx - 0.06 + k * 0.02, -0.03 + k * 0.015, cz + 0.06 - k * 0.02), (cx + 0.08, -0.03 + k * 0.03, cz - 0.08 + k * 0.02),
              0.03 - k * 0.006, 0.0015, "white", 0, up=(0, 1, 0))
    return B.one("Billboard", g, mats)


# =====================================================================================================
# The Antenna, the Traffic Cone and the Banner Holder
# =====================================================================================================

def antenna(mats):
    """Antenna, 1 x 6 x 1: a Salvager whip antenna: a hull base with copper screws, a copper loading coil, a steel
    whip and a signal-red tip."""
    g = Geo()
    zb = -0.75
    cylinder(g, (0, 0, zb), (0, 0, zb + 0.012), 0.1, "hull", segs=24)
    cylinder(g, (0, 0, zb + 0.012), (0, 0, zb + 0.05), 0.06, "gun", segs=20, r1=0.045)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        K.screw(g, (0.08 * math.cos(a), 0.08 * math.sin(a), zb + 0.012), (0, 0, 1), 0.0065, "copper")
    cylinder(g, (0, 0, zb + 0.05), (0, 0, zb + 0.2), 0.016, "rubber", segs=14)
    coil = [(0.019 * math.cos(t), 0.019 * math.sin(t), zb + 0.07 + t / (2 * math.pi) * 0.011)
            for t in np.linspace(0, 2 * math.pi * 11, 180)]
    K.sweep(g, coil, 0.0032, "copper", segs=6)
    cylinder(g, (0, 0, zb + 0.2), (0, 0, zb + 0.23), 0.012, "gun", segs=12)
    cylinder(g, (0, 0, zb + 0.23), (0, 0, 0.72), 0.006, "steel", segs=10, r1=0.0035)
    K.dome(g, (0, 0, 0.715), (0, 0, 1), 0.012, "red_tip", segs=10, height=0.02)
    cylinder(g, (0, 0, 0.7), (0, 0, 0.716), 0.012, "red_tip", segs=10)
    return B.one("Antenna", g, mats)


def traffic_cone(mats):
    """Traffic Cone, 2 x 3 x 2: signal red (never orange), two bone reflective bands, a graphite rubber base."""
    g = Geo()
    zb = -0.375
    box(g, (0, 0, zb + 0.015), (0.2, 0.2, 0.015), "rubber", bevel=0.02)
    r0, r1, z0, z1 = 0.165, 0.028, zb + 0.03, 0.355

    def r_at(z):
        return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    K.lathe(g, [(0, z0), (r0, z0), (r1, z1), (0.0, z1 + 0.004)], "signal", steps=32)
    for (za, zb2) in ((-0.13, -0.03), (0.08, 0.15)):
        K.lathe(g, [(r_at(za) + 0.0025, za), (r_at(zb2) + 0.0025, zb2), (r_at(zb2), zb2 + 0.002), (r_at(za), za - 0.002)], "white", steps=32)
    return B.one("TrafficCone", g, mats)


def banner_holder(mats):
    """Banner Holder, 4 x 1 x 1: a Colony bar that a banner hangs from: steel-blue wall clamps at its ends, a graphite
    bar, a signal-red sleeve with bone bands in the middle and hooks under it."""
    g = Geo()
    for s in (-1, 1):
        box(g, (s * 0.44, -0.08, 0), (0.05, 0.045, 0.09), "steel_blue", bevel=0.008)
        for sz in (-1, 1):
            B.bolt(g, (s * 0.44, -0.035, sz * 0.055), (0, 1, 0), 0.007)
        cylinder(g, (s * 0.44, -0.035, 0), (s * 0.44, 0.02, 0), 0.026, "steel_blue", segs=14)
    cylinder(g, (-0.47, 0.02, 0), (0.47, 0.02, 0), 0.02, "graphite", segs=16)
    cylinder(g, (-0.3, 0.02, 0), (0.3, 0.02, 0), 0.034, "signal", segs=20)
    for x in (-0.28, -0.1, 0.1, 0.28):
        cylinder(g, (x - 0.012, 0.02, 0), (x + 0.012, 0.02, 0), 0.037, "bone", segs=20)
    for x in (-0.36, -0.12, 0.12, 0.36):
        K.sweep(g, [(x, 0.02, -0.02), (x, 0.02, -0.06), (x, 0.04, -0.08), (x, 0.06, -0.065)], 0.004, "galv", segs=6)
    return B.one("BannerHolder", g, mats)


def _post_title(name):
    return f"{name} (3 x 6 x 1 blocks)"


PARTS = [
    ("antenna", antenna, (0.125, 0.125, 0.75), 1.75, "Antenna (1 x 6 x 1 blocks)",
     ["Salvager whip antenna: hull base, copper loading coil, steel whip, signal-red tip."]),
    ("direction_arrow_left", direction_arrow_left, (0.375, 0.125, 0.75), 1.75, _post_title("Direction Arrow (Left)"),
     ["Colony road sign: steel-blue plate, bone border and arrow, on a graphite post with a bolted foot."]),
    ("direction_arrow_straight", direction_arrow_straight, (0.375, 0.125, 0.75), 1.75, _post_title("Direction Arrow (Straight)"),
     ["As the Left arrow, pointing up (straight on)."]),
    ("direction_arrow_right", direction_arrow_right, (0.375, 0.125, 0.75), 1.75, _post_title("Direction Arrow (Right)"),
     ["As the Left arrow, pointing right."]),
    ("caution_sign", caution_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Caution Sign"),
     ["A mustard warning triangle with a graphite border and an exclamation mark."]),
    ("danger_sign", danger_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Danger Sign"),
     ["Bone plate: a signal-red band with a warning triangle and lightning bolt over mustard hazard stripes."]),
    ("stop_sign", stop_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Stop Sign"),
     ["A signal-red octagon with a bone border and STOP (one short word, art rule 7)."]),
    ("no_entry_sign", no_entry_sign, (0.375, 0.125, 0.75), 1.75, _post_title("No Entry Sign"),
     ["A signal-red disc with a bone bar."]),
    ("traffic_cone", traffic_cone, (0.25, 0.25, 0.375), 1.0, "Traffic Cone (2 x 3 x 2 blocks)",
     ["Signal red, not orange, with two bone reflective bands on a graphite rubber base."]),
    ("banner_holder", banner_holder, (0.5, 0.125, 0.125), 1.2, "Banner Holder (4 x 1 x 1 blocks)",
     ["A Colony bar a banner hangs from: steel-blue wall clamps, a signal-red sleeve with bone bands, hooks."]),
    ("billboard", billboard, (1.0, 0.25, 1.0), 2.4, "Billboard (8 x 8 x 2 blocks)",
     ["Settler farm billboard: a hand-painted produce poster on a plank frame, zinc back, braced legs, a peeling corner."]),
    ("beware_bots_sign", beware_bots_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Beware of Bots Sign"),
     ["Bone plate, signal-red border, a Thresher pictogram."]),
    ("construction_sign", construction_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Construction Sign"),
     ["A mustard diamond: a worker digging into a mound."]),
    ("exit_sign", exit_sign, (0.375, 0.125, 0.125), 0.95, "Exit Sign (3 x 1 x 1 blocks)",
     ["A graphite lightbox on a wall bracket: a running figure, EXIT and an arrow lit cyan (still powered)."]),
    ("falling_objects_sign", falling_objects_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Falling Objects Sign"),
     ["A mustard triangle: a person shielding their head from falling blocks."]),
    ("hard_work_sign", hard_work_sign, (0.5, 0.125, 0.375), 1.3, "Hard Work Sign (4 x 3 x 1 blocks)",
     ["Settler plank board on stakes: a mustard hammer and wrench over a sea-teal rising sun."]),
    ("sale_sign", sale_sign, (0.5, 0.125, 0.375), 1.3, "Sale Sign (4 x 3 x 1 blocks)",
     ["Two-sided plank board: SALE in brick red on one side, a price tag and coin on the other."]),
    ("skull_sign", skull_sign, (0.375, 0.125, 0.75), 1.75, _post_title("Skull Sign"),
     ["Graphite plate, mustard border, a mustard skull over crossed wrenches."]),
    ("welcome_sign", welcome_sign, (0.75, 0.125, 0.375), 1.8, "Welcome Sign (6 x 3 x 1 blocks)",
     ["A sea-teal painted plank board: WELCOME in bone, a mustard flower each side, on two stakes."]),
    ("signboard", signboard, (0.75, 0.125, 0.5), 1.8, "Signboard (6 x 4 x 1 blocks)",
     ["Settler board painted bone, blank: players write up to 4 lines on it."]),
    ("colony_sign", colony_sign, (0.5, 0.125, 0.75), 1.75, "Colony Sign (4 x 6 x 1 blocks)",
     ["Colony plate on two posts: bone face, steel-blue band, mustard rule; 3 lines of words."]),
    ("paper_note", paper_note, (0.125, 0.125, 0.125), 0.4, "Paper Note (1 x 1 x 1 block)",
     ["Ruled notepaper taped flat to a wall, door or vehicle; 6 lines in pencil."]),
]
