"""Materials and procedural textures for the Lift (used by build_lift.py).

Every surface material is a tileable set of images (baseColor, tangent-space normal, metallic-roughness) made
in numpy from seeded, FFT-filtered noise: worn gunmetal plate, dark steel, chipped safety-orange paint,
hazard striping, chrome, rubber. Meshes get box-projected UVs in metres (one tile per TILE metres), so the
patterns keep the same scale on every part. Decals (warning label, gauge face) have fixed UVs and their own
small images drawn with Pillow from shapes only (no fonts), so the output is the same on every machine.
"""

from __future__ import annotations

import math

import bpy
import numpy as np
from PIL import Image, ImageDraw

SIZE = 512
TILE = 0.5          # metres per texture tile (hazard stripes use HAZARD_TILE)
HAZARD_TILE = 0.32


# =====================================================================================================
# Noise
# =====================================================================================================

def fnoise(seed, cutoff, size=SIZE, aniso=(1.0, 1.0)):
    """Tileable noise in about [-1, 1]: white noise low-passed in the frequency domain. cutoff in cycles per tile."""
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None] * size / aniso[1]
    fx = np.fft.fftfreq(size)[None, :] * size / aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * np.exp(-(f / cutoff) ** 2)))
    return out / (np.abs(out).max() + 1e-9)


def fbm(seed, base=4.0, octaves=5):
    total = np.zeros((SIZE, SIZE))
    amp, norm = 1.0, 0.0
    for k in range(octaves):
        total += amp * fnoise(seed + k, base * 2 ** k)
        norm += amp
        amp *= 0.5
    return total / norm


def scratches(seed, count, length=(8, 60), bias=0.0, width=1):
    """A mask of thin wrapped line segments (scratches), mostly along `bias` radians."""
    rng = np.random.default_rng(seed)
    mask = np.zeros((SIZE, SIZE))
    for _ in range(count):
        x, y = rng.uniform(0, SIZE, 2)
        a = bias + rng.normal(0, 0.6)
        n = int(rng.uniform(*length))
        strength = rng.uniform(0.35, 1.0)
        for s in range(n):
            px = int(x + math.cos(a) * s) % SIZE
            py = int(y + math.sin(a) * s) % SIZE
            mask[py, px] = max(mask[py, px], strength)
            if width > 1:
                mask[(py + 1) % SIZE, px] = max(mask[(py + 1) % SIZE, px], strength * 0.5)
    return mask


def blur(x, radius):
    """Tileable box blur."""
    out = x.copy()
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for d in range(-radius, radius + 1):
            acc += np.roll(out, d, axis=axis)
        out = acc / (2 * radius + 1)
    return out


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def srgb(hexstr):
    h = hexstr.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


# =====================================================================================================
# Surfaces: each returns (albedo sRGB HxWx3, height HxW, roughness HxW, metallic HxW)
# =====================================================================================================

def metal_surface(seed, color, rough, metal, wear=1.0, edge_light=0.35):
    mottle = fbm(seed, 3.0)
    grain = fnoise(seed + 10, 120.0, aniso=(1.0, 0.15))
    grime = np.clip(fbm(seed + 20, 2.0) * 1.4 - 0.2, 0, 1)
    sc = blur(scratches(seed + 30, int(260 * wear), bias=0.15), 0) * wear
    pits = (fnoise(seed + 40, 200.0) > 0.55).astype(float) * 0.6
    albedo = np.asarray(srgb(color))[None, None, :] * (1.0 + 0.10 * mottle[..., None] + 0.04 * grain[..., None])
    albedo = albedo * (1 - 0.30 * grime[..., None]) * (1 - 0.25 * pits[..., None])
    albedo = albedo + edge_light * sc[..., None] * (0.55 - albedo)
    height = 0.4 * grain - 0.8 * sc - 0.5 * pits + 0.15 * mottle
    roughness = rough + 0.08 * mottle + 0.14 * grime - 0.18 * sc
    metallic = np.full((SIZE, SIZE), metal) + 0.1 * sc
    return albedo, height, roughness, metallic


def paint_surface(seed, color, under_color):
    a_m, h_m, r_m, m_m = metal_surface(seed + 100, under_color, 0.45, 0.75, wear=0.6)
    chips = fbm(seed, 6.0) + 0.35 * fnoise(seed + 1, 60.0)
    chip_mask = np.clip((chips - 0.52) * 12, 0, 1)
    sc = scratches(seed + 2, 140, bias=0.0)
    dirt = np.clip(fbm(seed + 3, 2.5) * 1.5 - 0.1, 0, 1)
    paint = np.asarray(srgb(color))[None, None, :] * (1 + 0.06 * fbm(seed + 4, 8.0)[..., None]) * (1 - 0.28 * dirt[..., None])
    paint = paint + 0.25 * sc[..., None] * (0.85 - paint)
    m = np.maximum(chip_mask, sc * 0.7)[..., None]
    albedo = paint * (1 - m) + a_m * m
    height = 0.6 * (1 - np.maximum(chip_mask, sc * 0.7)) + 0.1 * h_m
    roughness = (0.52 + 0.12 * dirt) * (1 - m[..., 0]) + r_m * m[..., 0]
    metallic = 0.0 * (1 - m[..., 0]) + m_m * m[..., 0]
    return albedo, height, roughness, metallic


def hazard_surface(seed):
    """Diagonal orange and black stripes (period divides the tile, so it tiles), worn."""
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    period = SIZE // 4
    stripe = (((xx + yy) // (period // 2)) % 2).astype(float)
    a_o, h_o, r_o, m_o = paint_surface(seed, "#e0731c", "#4a4e54")
    a_b, h_b, r_b, m_b = paint_surface(seed + 7, "#191a1c", "#4a4e54")
    s = stripe[..., None]
    return a_o * s + a_b * (1 - s), h_o * stripe + h_b * (1 - stripe), r_o * stripe + r_b * (1 - stripe), m_o * stripe + m_b * (1 - stripe)


def chrome_surface(seed):
    streak = fnoise(seed, 140.0, aniso=(0.05, 1.0))
    smudge = np.clip(fbm(seed + 1, 3.0), 0, 1)
    albedo = np.asarray(srgb("#c9ccd0"))[None, None, :] * (1 + 0.05 * streak[..., None]) * (1 - 0.12 * smudge[..., None])
    return albedo, 0.15 * streak, 0.13 + 0.10 * smudge + 0.02 * streak, np.ones((SIZE, SIZE))


def rubber_surface(seed):
    grain = fnoise(seed, 160.0)
    dust = np.clip(fbm(seed + 1, 3.0) * 1.3, 0, 1)
    albedo = np.asarray(srgb("#1d1d1f"))[None, None, :] * (1 + 0.08 * grain[..., None]) + 0.06 * dust[..., None]
    return albedo, 0.5 * grain, 0.82 + 0.08 * dust, np.zeros((SIZE, SIZE))


# =====================================================================================================
# Decals
# =====================================================================================================

def decal_warning():
    s = 256
    im = Image.new("RGB", (s, int(s * 0.75)), (224, 115, 28))
    d = ImageDraw.Draw(im)
    w, h = im.size
    d.rectangle([6, 6, w - 7, h - 7], outline=(20, 20, 22), width=8)
    cx, top, base = w * 0.5, 26, h - 30
    d.polygon([(cx, top), (cx + 70, base), (cx - 70, base)], fill=(20, 20, 22))
    d.polygon([(cx, top + 26), (cx + 48, base - 12), (cx - 48, base - 12)], fill=(240, 200, 40))
    d.rectangle([cx - 6, top + 50, cx + 6, base - 46], fill=(20, 20, 22))
    d.rectangle([cx - 6, base - 36, cx + 6, base - 24], fill=(20, 20, 22))
    return np.asarray(im, float) / 255.0


def decal_label():
    s = 256
    im = Image.new("RGB", (s, s * 54 // 140), (205, 205, 200))
    d = ImageDraw.Draw(im)
    w, h = im.size
    d.rectangle([0, 0, w - 1, h - 1], outline=(25, 25, 25), width=5)
    d.rectangle([10, 10, 70, h - 11], fill=(224, 115, 28))
    for k in range(4):
        d.rectangle([20 + k * 12, 18, 25 + k * 12, h - 19], fill=(25, 25, 25))
    rng = np.random.default_rng(7)
    y = 20
    for row in range(4):
        x = 84
        while x < w - 20:
            ln = int(rng.integers(8, 26))
            d.rectangle([x, y, min(x + ln, w - 18), y + 7], fill=(40, 40, 40))
            x += ln + 6
        y += 18
    return np.asarray(im, float) / 255.0


def decal_gauge():
    s = 128
    im = Image.new("RGB", (s, s), (20, 20, 22))
    d = ImageDraw.Draw(im)
    d.ellipse([6, 6, s - 7, s - 7], fill=(230, 228, 220))
    for k in range(11):
        a = math.radians(225 - k * 27)
        r0, r1 = 44, 56 if k % 5 == 0 else 50
        c = (200, 60, 30) if k >= 8 else (25, 25, 25)
        d.line([(64 + r0 * math.cos(a), 64 - r0 * math.sin(a)), (64 + r1 * math.cos(a), 64 - r1 * math.sin(a))], fill=c, width=4)
    a = math.radians(225 - 4.2 * 27)
    d.line([(64, 64), (64 + 46 * math.cos(a), 64 - 46 * math.sin(a))], fill=(200, 40, 20), width=5)
    d.ellipse([56, 56, 72, 72], fill=(25, 25, 25))
    return np.asarray(im, float) / 255.0


# =====================================================================================================
# Blender images and materials
# =====================================================================================================

def make_image(name, rgb, colorspace):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
    img.colorspace_settings.name = colorspace
    px = np.ones((h, w, 4), np.float32)
    # Blender images are bottom-up; quantise so the stored bytes do not depend on the encoder's rounding
    px[..., :3] = np.round(np.clip(rgb[::-1], 0, 1) * 255.0) / 255.0
    img.pixels.foreach_set(px.ravel())
    img.pack()
    return img


def textured(name, surface, normal_strength=3.0):
    albedo, height, rough, metal = surface
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    ia = nt.nodes.new("ShaderNodeTexImage")
    ia.image = make_image(name + "_color", albedo, "sRGB")
    inn = nt.nodes.new("ShaderNodeTexImage")
    inn.image = make_image(name + "_normal", normal_from_height(height, normal_strength), "Non-Color")
    mr = np.stack([np.zeros_like(rough), np.clip(rough, 0.03, 1), np.clip(metal, 0, 1)], -1)
    ir = nt.nodes.new("ShaderNodeTexImage")
    ir.image = make_image(name + "_mr", mr, "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(ia.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(inn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(ir.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    return mat


def decal(name, rgb, rough=0.5):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    ia = nt.nodes.new("ShaderNodeTexImage")
    ia.image = make_image(name + "_color", rgb, "sRGB")
    nt.links.new(ia.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def light_material():
    """The amber lights: the game drives their emission; the file holds them at full glow."""
    mat = bpy.data.materials.new("lift_light")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1.0, 0.45, 0.05, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.2
    bsdf.inputs["Emission Color"].default_value = (1.0, 0.42, 0.04, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 4.0
    return mat


def make_materials():
    return {
        "gunmetal": textured("gunmetal", metal_surface(11, "#4b4f55", 0.48, 0.72)),
        "dark_steel": textured("dark_steel", metal_surface(23, "#2b2d31", 0.42, 0.8, wear=0.7, edge_light=0.25)),
        "orange": textured("orange", paint_surface(37, "#e0731c", "#4a4e54")),
        "hazard": textured("hazard", hazard_surface(41)),
        "chrome": textured("chrome", chrome_surface(53), normal_strength=1.0),
        "rubber": textured("rubber", rubber_surface(67), normal_strength=2.0),
        "lift_light": light_material(),
        "decal_warning": decal("decal_warning", decal_warning()),
        "decal_label": decal("decal_label", decal_label()),
        "decal_gauge": decal("decal_gauge", decal_gauge(), rough=0.25),
    }


# =====================================================================================================
# UVs and keys
# =====================================================================================================

def box_uvs(me, fixed):
    """Box-projected UVs in metres (TILE per image; hazard faces HAZARD_TILE), or each face's fixed UVs."""
    uv = me.uv_layers.new(name="UVMap")
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [m.name for m in me.materials]
    out = np.zeros((len(me.loops), 2))
    for poly in me.polygons:
        loops = list(poly.loop_indices)
        if fixed[poly.index] is not None:
            for k, li in enumerate(loops):
                out[li] = fixed[poly.index][k]
            continue
        n = np.abs(np.asarray(poly.normal))
        axis = int(np.argmax(n))
        scale = HAZARD_TILE if names[poly.material_index] == "hazard" else TILE
        for li in loops:
            p = co[me.loops[li].vertex_index]
            if axis == 0:
                out[li] = (p[1] / scale, p[2] / scale)
            elif axis == 1:
                out[li] = (p[0] / scale, p[2] / scale)
            else:
                out[li] = (p[0] / scale, p[1] / scale)
    uv.data.foreach_set("uv", out.ravel())


def linear_keys(action):
    """Linear interpolation on every key of the action (Blender 5 keeps fcurves in layered channelbags)."""
    curves = []
    if hasattr(action, "layers") and action.layers:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    curves.extend(bag.fcurves)
    else:
        curves = list(action.fcurves)
    for fc in curves:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
