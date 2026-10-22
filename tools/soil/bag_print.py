"""The Soil Bag's printed artwork (used by soil_textures.PrintedBag): a retail garden-soil bag from an invented brand,
Rootwell, so it reads at hotbar size as a bright yellow bag with a green end band, big green lettering and red produce.

Each face of the bag is drawn flat at 1 px per millimetre with Pillow (shapes and the game's own Barlow fonts, no
downloads), then sampled by world position. ``FACES`` names them; every image is RGB, origin top left, as the face is
seen from outside the bag with +Z up (the top and bottom: +Y up the image for the top, -Y for the bottom).

* top - the front of the pack: the green Rootwell band at the +X end with the sprout badge, "GARDEN SOIL" in dark green,
  "Flowers & Vegetables", a red 25 L tag, and our three crops (tomatoes, a carrot, a potato) in leaves on a bed of soil.
* sides (-Y front, +Y back) - the band, "GARDEN SOIL" and the badge again, smaller.
* ends - plain band green (+X) and bag yellow (-X).
* bottom - yellow with the band and a white info panel (barcode, ruled lines; no readable small print).

Deterministic: fixed shapes, seeded jitter only.
"""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONTS = Path(__file__).resolve().parents[2] / "game" / "fonts"
SS = 2                                     # supersampling while drawing

# ---- Palette (sRGB) ---------------------------------------------------------------------------------
YELLOW = (236, 196, 46)                    # a printed mustard, brighter than the structures' #E0B530
YELLOW_DEEP = (214, 164, 30)
GREEN = (46, 128, 58)
GREEN_DARK = (24, 78, 38)
INK = (28, 74, 36)                         # the lettering
WHITE = (250, 248, 238)
RED = (204, 54, 44)
RED_DARK = (140, 28, 26)
SOIL = (74, 50, 32)
SOIL_DARK = (44, 29, 19)
LEAF, LEAF_DARK, LEAF_LIGHT = (68, 150, 52), (30, 92, 34), (130, 196, 84)
CARROT, CARROT_DARK = (226, 112, 36), (170, 70, 22)
POTATO, POTATO_DARK = (196, 156, 102), (138, 102, 60)

HALF = (0.625, 0.375, 0.125)               # the bag's box (metres)
BAND_X = 0.275                             # the green band starts here along +X (it wraps every face)


def band_edge(y):
    """Where the band starts along X at depth y (a gentle curve, like a printed swoosh)."""
    return BAND_X + 0.03 * np.sin((np.asarray(y, float) / HALF[1]) * 1.4)


def font(size, weight="Bold", condensed=False):
    name = ("BarlowCondensed-" if condensed else "Barlow-") + weight + ".ttf"
    return ImageFont.truetype(str(FONTS / name), int(size * SS))


# =====================================================================================================
# Drawing helpers (all coordinates in millimetres; they are scaled by SS)
# =====================================================================================================

class Canvas:
    def __init__(self, w_mm, h_mm, fill):
        self.w, self.h = w_mm, h_mm
        self.img = Image.new("RGB", (w_mm * SS, h_mm * SS), fill)
        self.d = ImageDraw.Draw(self.img)

    def s(self, *v):
        return [c * SS for c in v]

    def paste_mask(self, mask, colour):
        """Composite `colour` through a float mask (H x W at the supersampled size)."""
        a = np.asarray(self.img, float)
        m = np.clip(mask, 0, 1)[..., None]
        self.img = Image.fromarray((a * (1 - m) + np.asarray(colour, float)[None, None] * m).astype(np.uint8))
        self.d = ImageDraw.Draw(self.img)

    def paste_rgb(self, rgb, mask):
        a = np.asarray(self.img, float)
        m = np.clip(mask, 0, 1)[..., None]
        self.img = Image.fromarray((a * (1 - m) + rgb * m).astype(np.uint8))
        self.d = ImageDraw.Draw(self.img)

    def grid(self):
        jj, ii = np.mgrid[0:self.h * SS, 0:self.w * SS]
        return (ii + 0.5) / SS, (jj + 0.5) / SS          # millimetres

    def text(self, xy, s, f, fill, anchor="la", stroke=0, stroke_fill=None):
        self.d.text(self.s(*xy), s, font=f, fill=fill, anchor=anchor, stroke_width=int(stroke * SS),
                    stroke_fill=stroke_fill)

    def done(self):
        return self.img.resize((self.w, self.h), Image.LANCZOS)


def mix(a, b, t):
    a, b = np.asarray(a, float), np.asarray(b, float)
    return a + (b - a) * np.asarray(t, float)[..., None]


def shaded_blob(cv, cx, cy, rx, ry, base, dark, light, rot=0.0, gloss=0.0):
    """A shaded ellipse (a printed illustration of a round fruit): lit from the top left, dark rim, optional gloss."""
    X, Y = cv.grid()
    c, s = np.cos(rot), np.sin(rot)
    u = ((X - cx) * c + (Y - cy) * s) / rx
    v = (-(X - cx) * s + (Y - cy) * c) / ry
    r = np.hypot(u, v)
    mask = np.clip((1 - r) * min(rx, ry) * SS * 0.5, 0, 1)
    lit = np.clip(0.55 - 0.45 * (u * 0.6 + v * 0.8), 0, 1)
    col = mix(dark, base, np.clip(lit * 1.6, 0, 1))
    col = mix(col, light, np.clip(lit - 0.75, 0, 1) * 2.5)
    col = mix(col, dark, np.clip((r - 0.82) / 0.18, 0, 1) * 0.7)
    if gloss:
        g = np.hypot(u + 0.38, v + 0.42)
        col = mix(col, (255, 252, 240), np.clip(1 - g / 0.2, 0, 1) * gloss)
    cv.paste_rgb(col, mask)


def leaf(cv, x0, y0, x1, y1, width, base=LEAF, dark=LEAF_DARK, light=LEAF_LIGHT):
    """A pointed leaf from (x0, y0) to (x1, y1), `width` mm across, with a lighter midrib."""
    X, Y = cv.grid()
    L = np.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    t = ((X - x0) * ux + (Y - y0) * uy) / L
    n = (-(X - x0) * uy + (Y - y0) * ux)
    half = width / 2 * np.sin(np.pi * np.clip(t, 0, 1)) ** 0.8
    inside = np.clip((half - np.abs(n)) * SS * 0.7, 0, 1) * (t > 0) * (t < 1)
    col = mix(dark, base, np.clip(0.5 + n / (width + 1e-6) * 1.4 + 0.25, 0, 1))
    col = mix(col, light, np.clip(1 - np.abs(n) / 1.6, 0, 1) * (t < 0.92) * 0.8)
    cv.paste_rgb(col, inside)


def tomato(cv, cx, cy, r):
    shaded_blob(cv, cx, cy, r * 1.08, r, RED, RED_DARK, (240, 104, 84), gloss=0.85)
    for k in range(5):                                       # the calyx: a green star on top
        a = -np.pi / 2 + k * 2 * np.pi / 5 + 0.3
        leaf(cv, cx, cy - r * 0.62, cx + np.cos(a) * r * 0.55, cy - r * 0.62 + np.sin(a) * r * 0.32, r * 0.2)


def carrot(cv, x0, y0, x1, y1, w):
    X, Y = cv.grid()
    L = np.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    t = ((X - x0) * ux + (Y - y0) * uy) / L
    n = -(X - x0) * uy + (Y - y0) * ux
    half = w / 2 * (1 - np.clip(t, 0, 1)) ** 0.9 * np.clip(t * 8, 0, 1) ** 0.3
    inside = np.clip((half - np.abs(n)) * SS * 0.7, 0, 1) * (t > 0) * (t < 1)
    col = mix(CARROT_DARK, CARROT, np.clip(0.75 - n / (w + 1e-6) * 1.2, 0, 1))
    rings = (np.sin(t * 60) > 0.85) * (np.abs(n) > half * 0.3) * 0.5
    col = mix(col, CARROT_DARK, rings)
    cv.paste_rgb(col, inside)
    for a in (-0.5, 0.0, 0.45):                              # the tops
        c, s = np.cos(a), np.sin(a)
        dx, dy = -ux * c + uy * s, -uy * c - ux * s
        leaf(cv, x0, y0, x0 + dx * w * 1.6, y0 + dy * w * 1.6, w * 0.32)


def sprout_badge(cv, cx, cy, r):
    """The Rootwell badge: a dark green disc in a white ring with a yellow sprout (the game's sprout pictogram)."""
    import soil_textures as st                               # the sprout shape the old burlap bag was stamped with
    X, Y = cv.grid()
    d = np.hypot(X - cx, Y - cy)
    cv.paste_mask(np.clip((r - d) * SS * 0.7, 0, 1), WHITE)
    cv.paste_mask(np.clip((r * 0.86 - d) * SS * 0.7, 0, 1), GREEN_DARK)
    k = r / 70.0                                             # sprout units are metres for a ~0.12 m stamp
    a = (X - cx) / (1000 * k) * 1.05
    b = -(Y - cy - r * 0.32) / (1000 * k) * 1.05
    sd = st.sprout_sdf_up(a, b) * 1000 * k
    cv.paste_mask(np.clip(-sd * SS * 0.7, 0, 1), YELLOW)


def band(cv, x_of, y_of):
    """Paints the green band where world x (from the canvas grid through x_of/y_of) passes band_edge, with a thin
    white keyline along its edge."""
    X, Y = cv.grid()
    wx, wy = x_of(X, Y), y_of(X, Y)
    e = band_edge(wy)
    cv.paste_mask(np.clip((wx - (e - 0.006)) * 1000 * SS * 0.7, 0, 1), WHITE)
    cv.paste_mask(np.clip((wx - e) * 1000 * SS * 0.7, 0, 1), GREEN)
    # a darker green toward the very end, like the reference's folded top
    cv.paste_mask(np.clip((wx - 0.52) / 0.1, 0, 1) * 0.35, GREEN_DARK)


# =====================================================================================================
# The faces
# =====================================================================================================

def draw_top():
    W, H = 1250, 750
    cv = Canvas(W, H, YELLOW)
    X, Y = cv.grid()
    # a soft warm sunburst behind the produce, so the yellow is not flat
    glow = np.clip(1 - np.hypot(X - 640, Y - 610) / 520, 0, 1) ** 1.5
    cv.paste_mask(glow * 0.55, (248, 222, 96))
    band(cv, lambda X, Y: X / 1000 - HALF[0], lambda X, Y: HALF[1] - Y / 1000)

    # the bed of soil the crops sit in, along the bottom of the yellow area
    soil_top = 640 - 26 * np.sin(X / 70) * np.cos(X / 190) - 30 * np.exp(-((X - 620) / 170) ** 2)
    inside = np.clip((Y - soil_top) * SS * 0.7, 0, 1) * np.clip((905 - X) * SS * 0.7, 0, 1)
    rng = np.random.default_rng(5)
    speck = rng.random(X.shape)
    col = mix(SOIL, SOIL_DARK, np.clip((Y - soil_top) / 90, 0, 1) * 0.7 + (speck > 0.9) * 0.3)
    col = mix(col, (130, 98, 66), (speck < 0.04) * 0.8)
    cv.paste_rgb(col, inside)

    # the crops: leaves behind, then a potato, a carrot and three tomatoes
    for (x0, y0, x1, y1, w) in ((470, 610, 520, 545, 50), (520, 590, 560, 400, 80), (760, 600, 860, 440, 74),
                                (700, 590, 700, 420, 66), (600, 600, 510, 480, 60), (820, 610, 900, 530, 52)):
        leaf(cv, x0, y0, x1, y1, w)
    shaded_blob(cv, 400, 620, 80, 56, POTATO, POTATO_DARK, (230, 200, 150), rot=-0.2)
    for ex, ey in ((370, 600), (420, 640), (440, 595)):
        shaded_blob(cv, ex, ey, 6, 4, POTATO_DARK, (100, 72, 40), POTATO_DARK)
    carrot(cv, 300, 640, 80, 712, 60)
    tomato(cv, 545, 585, 92)
    tomato(cv, 690, 615, 78)
    tomato(cv, 610, 690, 56)

    # lettering: brand line, GARDEN SOIL, the sub line
    cv.text((66, 62), "GARDEN", font(180, "Bold", True), INK, stroke=4, stroke_fill=WHITE)
    cv.text((66, 222), "SOIL", font(220, "Bold", True), INK, stroke=4, stroke_fill=WHITE)
    cv.text((72, 456), "Flowers & Vegetables", font(44, "SemiBold"), GREEN_DARK)

    # the red tag, tilted, over the corner of the lettering
    tag = Image.new("RGBA", (300 * SS, 130 * SS), (0, 0, 0, 0))
    td = ImageDraw.Draw(tag)
    td.rounded_rectangle([4 * SS, 4 * SS, 296 * SS, 126 * SS], radius=22 * SS, fill=WHITE)
    td.rounded_rectangle([12 * SS, 12 * SS, 288 * SS, 118 * SS], radius=16 * SS, fill=RED)
    td.text((150 * SS, 66 * SS), "25 L", font=font(88, "Bold", True), fill=WHITE, anchor="mm")
    tag = tag.rotate(9, resample=Image.BICUBIC, expand=True)
    cv.img.paste(tag, (500 * SS, 270 * SS), tag)
    cv.d = ImageDraw.Draw(cv.img)

    # the badge in the band
    sprout_badge(cv, 1080, 250, 120)
    cv.text((1080, 430), "ROOTWELL", font(58, "Bold", True), WHITE, anchor="mm")
    return cv.done()


def draw_side(sign):
    """A long side; sign -1 is the front (-Y, X runs right), +1 the back (X runs left)."""
    W, H = 1250, 250
    cv = Canvas(W, H, YELLOW)
    xs = (lambda X, Y: X / 1000 - HALF[0]) if sign < 0 else (lambda X, Y: HALF[0] - X / 1000)
    band(cv, xs, lambda X, Y: np.full(X.shape, sign * HALF[1]))
    end = 1100 if sign < 0 else 150                         # the band's middle on the image
    mid = 450 if sign < 0 else 800
    sprout_badge(cv, end, 125, 62)
    cv.text((mid, 128), "GARDEN SOIL", font(112, "Bold", True), INK, anchor="mm")
    return cv.done()


def draw_end(sign):
    return Image.new("RGB", (750, 250), GREEN if sign > 0 else YELLOW_DEEP)


def draw_bottom():
    W, H = 1250, 750
    cv = Canvas(W, H, YELLOW_DEEP)
    band(cv, lambda X, Y: X / 1000 - HALF[0], lambda X, Y: Y / 1000 - HALF[1])
    cv.d.rounded_rectangle(cv.s(120, 200, 760, 560), radius=20 * SS, fill=WHITE)
    for k in range(7):                                       # ruled lines standing in for small print
        cv.d.rectangle(cv.s(150, 230 + k * 30, 150 + (440 if k % 3 else 300), 240 + k * 30), fill=(150, 150, 140))
    rng = np.random.default_rng(11)
    x = 470
    while x < 730:                                           # the barcode
        w = int(rng.integers(2, 7))
        cv.d.rectangle(cv.s(x, 450, x + w, 535), fill=(30, 30, 30))
        x += w + int(rng.integers(2, 6))
    return cv.done()


@lru_cache(maxsize=None)
def faces():
    """{name: float RGB array 0..1 (sRGB)} of every face, drawn once."""
    out = {"top": draw_top(), "front": draw_side(-1), "back": draw_side(1), "end_pos": draw_end(1),
           "end_neg": draw_end(-1), "bottom": draw_bottom()}
    return {k: np.asarray(v.filter(ImageFilter.SMOOTH_MORE), float) / 255.0 for k, v in out.items()}


def sample(img, u, v):
    """Bilinear lookup of `img` at u, v in 0..1 (u right, v down), clamped."""
    h, w = img.shape[:2]
    x = np.clip(u * w - 0.5, 0, w - 1.001)
    y = np.clip(v * h - 0.5, 0, h - 1.001)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    a, b = img[y0, x0], img[y0, x0 + 1]
    c, d = img[y0 + 1, x0], img[y0 + 1, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def print_colour(P, n):
    """The printed colour (sRGB 0..1) at points P (N x 3, on the bag's box) of the face with outward normal n."""
    hx, hy, hz = HALF
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    f = faces()
    k = int(np.argmax(np.abs(n)))
    if k == 2 and n[2] > 0:
        return sample(f["top"], (x + hx) / (2 * hx), (hy - y) / (2 * hy))
    if k == 2:
        return sample(f["bottom"], (x + hx) / (2 * hx), (y + hy) / (2 * hy))
    if k == 1 and n[1] < 0:
        return sample(f["front"], (x + hx) / (2 * hx), (hz - z) / (2 * hz))
    if k == 1:
        return sample(f["back"], (hx - x) / (2 * hx), (hz - z) / (2 * hz))
    if n[0] > 0:
        return sample(f["end_pos"], (y + hy) / (2 * hy), (hz - z) / (2 * hz))
    return sample(f["end_neg"], (hy - y) / (2 * hy), (hz - z) / (2 * hz))


if __name__ == "__main__":                                   # quick look: python tools/soil/bag_print.py OUT_DIR
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "rocks"))
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    for name, arr in faces().items():
        Image.fromarray((arr * 255).astype(np.uint8)).save(out / f"print_{name}.png")
