"""Materials for the Bearing and the Scrap Gas Engine (used by build_bearing_engine.py).

Built on the Lift's texture kit (tools/lift/lift_textures.py): tileable numpy surfaces from seeded, FFT-filtered
noise, box-projected in metres. Adds what the two parts need on top of it: the bearing's gunmetal casing, matte black
band, teal accent and machined silver hub (Bearing Design Spec palette), and the engine's mismatched rusty plates
with oil streaks, silver duct tape, crude copper and black pipes, wire insulation and its panel decals. Everything is
seeded, so the output is the same on every run.
"""

from __future__ import annotations

import math

import bpy
import numpy as np
from PIL import Image, ImageDraw

import lift_textures as lt

SIZE = lt.SIZE


def rust_surface(seed, base, deep, bare, wear=1.0, streaks=1.0):
    """Rusty plate: mottled orange-brown rust over dark bare metal, pitted, with oil streaks running down (image v)."""
    mottle = lt.fbm(seed, 7.0)
    blotch = np.clip(lt.fbm(seed + 1, 12.0) * 1.8 + 0.25, 0, 1)
    speck = np.clip((lt.fnoise(seed + 8, 140.0) - 0.35) * 4, 0, 1)
    flakes = np.clip((lt.fbm(seed + 2, 10.0) + 0.3 * lt.fnoise(seed + 3, 90.0) - 0.36) * 7 * wear, 0, 1)
    pits = (lt.fnoise(seed + 4, 220.0) > 0.5).astype(float)
    # oil streaks: noise stretched along v, gated so only some columns run
    drip = lt.fnoise(seed + 5, 40.0, aniso=(1.0, 0.08))
    gate = np.clip(lt.fnoise(seed + 6, 6.0, aniso=(1.0, 0.25)) * 2.2 - 0.4, 0, 1)
    oil = np.clip((drip - 0.15) * 3.5, 0, 1) * gate * streaks
    sc = lt.scratches(seed + 7, int(120 * wear), bias=0.3) * 0.8
    rust = lt.srgb(base)[None, None, :] * (1 - np.clip(blotch, 0, 1)[..., None]) + lt.srgb(deep)[None, None, :] * np.clip(blotch, 0, 1)[..., None]
    rust = rust * (1 + 0.28 * mottle[..., None]) * (1 - 0.35 * pits[..., None]) * (1 - 0.45 * speck[..., None])
    metal = lt.srgb(bare)[None, None, :] * (1 + 0.1 * mottle[..., None])
    albedo = rust * (1 - flakes[..., None]) + metal * flakes[..., None]
    albedo = albedo + 0.35 * sc[..., None] * (0.42 - albedo)
    albedo = albedo * (1 - 0.62 * oil[..., None])
    height = 0.5 * mottle - 0.6 * speck - 0.9 * pits * (1 - flakes) - 0.6 * flakes + 0.25 * blotch - 0.5 * sc
    roughness = (0.86 + 0.06 * mottle) * (1 - flakes) + 0.5 * flakes - 0.45 * oil
    metallic = 0.12 * (1 - flakes) + 0.75 * flakes
    return albedo, height, roughness, metallic


def tape_surface(seed):
    """Silver duct tape: fine cloth weave, soft wrinkles and grime."""
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    weave = 0.5 * np.sin(xx * 2 * math.pi / 4) + 0.5 * np.sin(yy * 2 * math.pi / 4)
    wrinkle = lt.fnoise(seed, 14.0, aniso=(0.3, 1.0))
    grime = np.clip(lt.fbm(seed + 1, 3.0) * 1.3, 0, 1)
    albedo = lt.srgb("#9a9ea2")[None, None, :] * (1 + 0.05 * weave[..., None] + 0.12 * wrinkle[..., None]) * (1 - 0.3 * grime[..., None])
    return albedo, 0.08 * weave + 0.9 * wrinkle, 0.5 + 0.15 * grime, np.full((SIZE, SIZE), 0.35)


def plain(name, color, rough, metal, emission=None, strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*lt.srgb(color) ** 2.2, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*lt.srgb(emission) ** 2.2, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def decal_gauge():
    """The engine's small pressure gauge: a cream dial in a dark bezel, ticks, a red band, no needle (a separate mesh)."""
    s = 128
    im = Image.new("RGB", (s, s), (28, 26, 24))
    d = ImageDraw.Draw(im)
    d.ellipse([5, 5, s - 6, s - 6], fill=(214, 206, 184))
    d.ellipse([10, 10, s - 11, s - 11], outline=(150, 140, 118), width=2)
    for k in range(9):
        a = math.radians(210 - k * 30)
        r0, r1 = 40, 54 if k % 2 == 0 else 49
        c = (170, 40, 26) if k >= 7 else (40, 36, 32)
        d.line([(64 + r0 * math.cos(a), 64 - r0 * math.sin(a)), (64 + r1 * math.cos(a), 64 - r1 * math.sin(a))], fill=c, width=4)
    d.arc([18, 18, s - 19, s - 19], start=-30 - 10, end=-30 + 25, fill=(170, 40, 26), width=5)
    # a grimy smear across the glass
    rng = np.random.default_rng(3)
    for _ in range(40):
        x, y = rng.uniform(20, 108, 2)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(170, 160, 136))
    return np.asarray(im, float) / 255.0


def make_materials():
    """Every material the two parts use, by name."""
    return {
        # Bearing (Bearing Design Spec palette: #4A4A4A-#5C5C5C, #1A1A1A, #2A8F8F, #B0B0B0-#C8C8C8)
        "gunmetal": plain("bearing_gunmetal", "#555555", 0.52, 0.75),
        "band": plain("bearing_band", "#1a1a1a", 0.9, 0.0),
        "teal": plain("bearing_teal", "#2a8f8f", 0.35, 0.1, emission="#2a8f8f", strength=0.08),
        "silver": plain("bearing_silver", "#bdbdbd", 0.3, 0.95),
        "hole": plain("bearing_hole", "#0c0c0d", 0.9, 0.2),
        # Scrap Gas Engine
        "rust_a": lt.textured("scrap_rust_a", rust_surface(201, "#7a3a1a", "#4a2210", "#33302d", wear=1.3)),
        "rust_b": lt.textured("scrap_rust_b", rust_surface(211, "#80401c", "#522611", "#3d3a36", wear=1.1)),
        "rust_c": lt.textured("scrap_rust_c", rust_surface(221, "#5e2f17", "#331a0c", "#2a2826", wear=1.5, streaks=1.4)),
        "panel": lt.textured("scrap_panel", rust_surface(231, "#6e3a1c", "#45230f", "#403d39", wear=1.2, streaks=0.6)),
        "steel": lt.textured("scrap_steel", lt.metal_surface(241, "#5b5854", 0.6, 0.75, wear=1.4, edge_light=0.3)),
        "dark": lt.textured("scrap_dark", lt.metal_surface(251, "#2c2a28", 0.55, 0.7, wear=0.9, edge_light=0.25)),
        "tape": lt.textured("scrap_tape", tape_surface(261), normal_strength=2.0),
        "copper": lt.textured("scrap_copper", rust_surface(271, "#9a5a32", "#6a3a1e", "#b4744a", wear=1.3, streaks=0.3)),
        "black_pipe": lt.textured("scrap_black_pipe", lt.metal_surface(281, "#262422", 0.6, 0.6, wear=1.0, edge_light=0.3)),
        "knob": plain("scrap_knob", "#6a2a22", 0.45, 0.1),
        "wire_red": plain("scrap_wire_red", "#9a2a22", 0.6, 0.0),
        "wire_green": plain("scrap_wire_green", "#3e7a34", 0.6, 0.0),
        "wire_blue": plain("scrap_wire_blue", "#2c4f8a", 0.6, 0.0),
        "wire_yellow": plain("scrap_wire_yellow", "#b08f2a", 0.6, 0.0),
        "glass": plain("scrap_glass", "#1d2b24", 0.15, 0.0),
        "lamp_green": plain("scrap_lamp_green", "#2e5a2a", 0.3, 0.0),
        "lamp_red": plain("scrap_lamp_red", "#6a2420", 0.3, 0.0),
        # The lit lamps and the startup flash: shown by scaling their meshes up in the clips (the file holds them lit).
        "glow_green": plain("scrap_glow_green", "#6cff5a", 0.3, 0.0, emission="#6cff5a", strength=6.0),
        "glow_red": plain("scrap_glow_red", "#ff4a2a", 0.3, 0.0, emission="#ff4a2a", strength=6.0),
        "glow_flash": plain("scrap_glow_flash", "#ffb040", 0.3, 0.0, emission="#ffb040", strength=8.0),
        "needle": plain("scrap_needle", "#a8281a", 0.4, 0.2),
        "decal_gauge": lt.decal("scrap_decal_gauge", decal_gauge(), rough=0.25),
    }
