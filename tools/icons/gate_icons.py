#!/usr/bin/env python3
"""Draws the Logic Gate's mode pictures (mods/vanilla/textures/logic/gate_*.png): the usual gate symbols in the
theme's light text colour on a transparent square, shown large when E cycles the gate (UI spec section 12).
Needs Pillow. Run from anywhere: python3 tools/icons/gate_icons.py"""
import math
import os
from PIL import Image, ImageDraw

SIZE = 256
SCALE = 4  # drawn large and shrunk, for smooth edges
INK = (230, 237, 245, 255)  # the theme's light text
WIDTH = 9

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "mods", "vanilla", "textures", "logic")


def pt(x, y):
    return (x * SCALE, y * SCALE)


def line(d, a, b):
    d.line([pt(*a), pt(*b)], fill=INK, width=WIDTH * SCALE)


def curve(d, points):
    d.line([pt(x, y) for x, y in points], fill=INK, width=WIDTH * SCALE, joint="curve")


def bezier(p0, p1, p2, steps=40):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / steps for i in range(steps + 1))]


def and_body(d):
    # A D: flat back, round front.
    curve(d, [(70, 60), (130, 60)])
    curve(d, [(70, 196), (130, 196)])
    line(d, (70, 60), (70, 196))
    arc = [(130 + 68 * math.cos(a), 128 + 68 * math.sin(a)) for a in (math.radians(-90 + i * 4.5) for i in range(41))]
    curve(d, arc)
    return 198


def or_body(d, shift=0):
    back = bezier((60 + shift, 60), (100 + shift, 128), (60 + shift, 196))
    curve(d, back)
    curve(d, bezier((60 + shift, 60), (160 + shift, 60), (200, 128)))
    curve(d, bezier((60 + shift, 196), (160 + shift, 196), (200, 128)))
    return 200


def inputs(d, x):
    line(d, (24, 92), (x, 92))
    line(d, (24, 164), (x, 164))


def bubble(d, x):
    r = 13
    d.ellipse([pt(x, 128 - r), pt(x + 2 * r, 128 + r)], outline=INK, width=WIDTH * SCALE)
    return x + 2 * r


def draw(mode):
    image = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    if mode in ("and", "nand"):
        end = and_body(d)
        inputs(d, 70)
    else:
        shift = 0
        if mode in ("xor", "xnor"):
            curve(d, bezier((46, 60), (86, 128), (46, 196)))
            shift = 6
        end = or_body(d, shift)
        inputs(d, 80 + shift)
    if mode in ("nand", "nor", "xnor"):
        end = bubble(d, end)
    line(d, (end, 128), (236, 128))
    return image.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    os.makedirs(ROOT, exist_ok=True)
    for mode in ("and", "or", "xor", "nand", "nor", "xnor"):
        draw(mode).save(os.path.join(ROOT, f"gate_{mode}.png"), optimize=True)


if __name__ == "__main__":
    main()
