#!/usr/bin/env python3
"""Draws the Handbook's diagrams (docs/images/*.png) in the blue UI palette (UI spec §13).

The Handbook shows any image on a line of its own in a docs page, scaled to fit the page. Re-run this after changing
a diagram: python3 tools/docs/make_diagrams.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[2] / "docs" / "images"
DEEP = (13, 21, 32)
NAVY = (26, 35, 50)
PANEL = (33, 46, 66)
LINE = (52, 73, 102)
BLUE = (46, 134, 222)
CYAN = (0, 212, 255)
TEXT = (230, 238, 246)
DIM = (150, 168, 190)
GREY = (96, 108, 124)
SCALE = 2


def font(size, bold=False):
    name = "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    return ImageFont.truetype(f"/usr/share/fonts/truetype/dejavu/{name}", size * SCALE)


def canvas(w, h):
    image = Image.new("RGB", (w * SCALE, h * SCALE), DEEP)
    return image, ImageDraw.Draw(image)


def s(*v):
    return [x * SCALE for x in v]


def box(d, x, y, w, h, fill=PANEL, outline=LINE, width=2, radius=6):
    d.rounded_rectangle(s(x, y, x + w, y + h), radius=radius * SCALE, fill=fill, outline=outline, width=width * SCALE)


def text(d, x, y, words, size=14, colour=TEXT, bold=False, anchor="la"):
    d.text(s(x, y), words, fill=colour, font=font(size, bold), anchor=anchor)


def diamond(d, x, y, r, colour, outline=DEEP):
    d.polygon(s(x, y - r - 2, x + r + 2, y, x, y + r + 2, x - r - 2, y), fill=outline)
    d.polygon(s(x, y - r, x + r, y, x, y + r, x - r, y), fill=colour)


def callout(d, x, y, tx, ty, words):
    d.line(s(x, y, tx, ty), fill=CYAN, width=2 * SCALE)
    d.ellipse(s(x - 4, y - 4, x + 4, y + 4), fill=CYAN)
    text(d, tx + (6 if tx >= x else -6), ty, words, 14, TEXT, anchor="lm" if tx >= x else "rm")


def save(image, name):
    OUT.mkdir(parents=True, exist_ok=True)
    image.save(OUT / name, optimize=True)


def hud():
    image, d = canvas(800, 470)
    box(d, 120, 30, 560, 315, fill=(30, 52, 78), outline=LINE, radius=4)
    # Compass bar.
    box(d, 288, 50, 224, 18, fill=NAVY, radius=3)
    for i, mark in enumerate(["W", "NW", "N", "NE", "E"]):
        text(d, 304 + i * 48, 59, mark, 10, TEXT if len(mark) == 1 else DIM, anchor="mm")
    diamond(d, 430, 59, 5, CYAN)
    # Health.
    box(d, 135, 48, 120, 14, fill=NAVY, radius=3)
    d.rounded_rectangle(s(137, 50, 220, 60), radius=2 * SCALE, fill=BLUE)
    # Quest tracker.
    box(d, 545, 48, 120, 52, fill=NAVY, radius=4)
    for row in range(3):
        d.rectangle(s(553, 57 + row * 13, 553 + (90 if row != 1 else 70), 63 + row * 13), fill=DIM if row else TEXT)
    # Crosshair.
    d.line(s(392, 196, 408, 196), fill=TEXT, width=2 * SCALE)
    d.line(s(400, 188, 400, 204), fill=TEXT, width=2 * SCALE)
    # Chat.
    box(d, 135, 255, 130, 50, fill=NAVY, radius=4)
    for row in range(3):
        d.rectangle(s(143, 264 + row * 13, 143 + (100 - row * 20), 270 + row * 13), fill=DIM)
    # Hotbar.
    for slot in range(10):
        x = 285 + slot * 23
        box(d, x, 310, 20, 20, fill=NAVY, outline=CYAN if slot == 2 else LINE, radius=3)
    callout(d, 195, 55, 60, 120, "Health")
    callout(d, 400, 50, 400, 20, "Compass")
    callout(d, 665, 74, 740, 120, "Quests")
    callout(d, 408, 196, 560, 200, "Crosshair")
    callout(d, 135, 280, 60, 280, "Chat")
    callout(d, 400, 330, 400, 390, "Hotbar (1 to 0)")
    text(d, 400, 440, "Hide everything with Alt+Z", 13, DIM, anchor="mm")
    save(image, "hud.png")


def logbook():
    image, d = canvas(800, 440)
    box(d, 30, 20, 740, 400, fill=NAVY)
    d.rounded_rectangle(s(30, 20, 770, 52), radius=6 * SCALE, fill=BLUE)
    text(d, 46, 36, "Logbook", 15, TEXT, bold=True, anchor="lm")
    text(d, 752, 36, "✕", 15, TEXT, anchor="mm")
    # Quest list.
    text(d, 48, 72, "Quests", 14, TEXT, bold=True)
    box(d, 48, 94, 220, 28, fill=PANEL, outline=CYAN)
    text(d, 58, 108, "◆ Workshop Outpost", 13, TEXT, anchor="lm")
    text(d, 58, 134, "Reach the outpost", 11, DIM)
    box(d, 48, 156, 220, 28, fill=PANEL)
    text(d, 58, 170, "First Vehicle", 13, TEXT, anchor="lm")
    text(d, 58, 196, "✓ Getting Started", 13, GREY)
    text(d, 48, 226, "Your markers", 14, TEXT, bold=True)
    box(d, 48, 248, 184, 28, fill=PANEL)
    text(d, 58, 262, "Marker 1", 13, TEXT, anchor="lm")
    box(d, 238, 248, 30, 28, fill=PANEL)
    text(d, 253, 262, "✕", 13, TEXT, anchor="mm")
    # Map.
    d.rectangle(s(290, 66, 752, 380), fill=(22, 40, 62))
    d.ellipse(s(330, 90, 720, 360), fill=(40, 66, 98))
    d.ellipse(s(400, 130, 640, 320), fill=(58, 88, 124))
    d.ellipse(s(470, 170, 580, 270), fill=(78, 110, 148))
    diamond(d, 600, 150, 9, CYAN)
    diamond(d, 430, 280, 7, BLUE)
    diamond(d, 660, 290, 6, BLUE)
    text(d, 614, 150, "Workshop Outpost", 11, TEXT, anchor="lm")
    d.polygon(s(500, 205, 510, 228, 500, 222, 490, 228), fill=CYAN)
    d.line(s(304, 366, 364, 366), fill=TEXT, width=2 * SCALE)
    text(d, 304, 350, "500 m", 10, TEXT)
    text(d, 290, 398, "Drag to pan, scroll to zoom, click a marker to follow it, right-click to mark a place.", 11, DIM, anchor="lm")
    text(d, 48, 300, "Click a quest, place or marker", 12, CYAN)
    text(d, 48, 318, "to follow it on the compass.", 12, CYAN)
    callout(d, 496, 222, 420, 230, "You")
    save(image, "logbook.png")


def logic():
    image, d = canvas(800, 300)

    def part(x, y, title, sub):
        box(d, x, y, 150, 90, fill=PANEL, radius=8)
        text(d, x + 75, y + 36, title, 15, TEXT, bold=True, anchor="mm")
        text(d, x + 75, y + 60, sub, 11, DIM, anchor="mm")

    part(60, 90, "Switch", "on / off")
    part(325, 90, "Logic Gate", "AND, OR, NOT…")
    part(590, 90, "Engine", "runs while on")
    for (a, b, colour) in [(210, 325, CYAN), (475, 590, BLUE)]:
        d.line(s(a, 135, b, 135), fill=colour, width=3 * SCALE)
        for x in range(a + 30, b - 10, 40):
            d.polygon(s(x, 129, x + 9, 135, x, 141), fill=colour)
        d.ellipse(s(a - 7, 128, a + 7, 142), fill=colour, outline=DEEP, width=2 * SCALE)
        d.ellipse(s(b - 7, 128, b + 7, 142), fill=colour, outline=DEEP, width=2 * SCALE)
    text(d, 267, 116, "on", 12, CYAN, anchor="mm")
    text(d, 532, 116, "off", 12, BLUE, anchor="mm")
    text(d, 400, 230, "Connect Tool: drag from an output dot to an input dot.", 14, TEXT, anchor="mm")
    text(d, 400, 256, "Arrows point the way the signal flows; a lit (cyan) line carries an on signal.", 12, DIM, anchor="mm")
    save(image, "logic-chain.png")


if __name__ == "__main__":
    hud()
    logbook()
    logic()
