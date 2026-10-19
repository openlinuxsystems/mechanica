"""Materials and procedural textures for the Scrap Driver's Seat (used by build_scrap_seat.py).

The reference sheet's palette (Scrap Driver's Seat Design Spec, 2026-10-18): weathered gray-brown pipe with light rust,
light raw pallet wood, muted olive-tan canvas with clean, simple patches, and light metal reinforcements. A cleaner
scrapyard look: worn and patched, never dark, torn or grimy.

* Tileable surfaces (pipe, fittings, plates, bolts, the brass terminals) are 512 px images per TILE metres made in
  numpy from seeded, FFT-filtered noise (the Lift's helpers). Meshes get box-projected UVs in metres.
* Wood has its grain along each plank: planks carry their own UVs (u along the plank), so one tile serves them all.
* The two cushions get a baked box-chart atlas each (the Soil Bag's method): an even canvas weave, stitch rows beside
  the piped seams, soft creases where the cushion sags, and the patches with their stitched borders.
"""

from __future__ import annotations

import math

import numpy as np

import lift_textures as lt
import rock_textures as rt
from lift_textures import SIZE, blur, fbm, fnoise, scratches
from rock_textures import smoothstep, srgb

TILE = 0.5                # metres per tile of the tileable surfaces
WOOD_TILE = (1.6, 0.4)    # metres along and across the grain per wood tile

# ---- Palette (the sheet's colour and material guide) ------------------------------------------------
PIPE = "#8a837a"          # weathered gray-brown metal, light rust, matte
RUST = srgb("#8b5a3c")
FITTING = "#79736b"
PLATE = "#aeb1b2"         # light metal reinforcements
BOLT = "#5f6164"
BRASS = "#a8875a"
WOOD, WOOD_LIGHT, WOOD_DARK = srgb("#b39c7e"), srgb("#cbb79a"), srgb("#8a765e")
CANVAS, CANVAS_LIGHT, CANVAS_DARK = srgb("#8b8763"), srgb("#a29e7d"), srgb("#6c6849")
PATCHES = (srgb("#9a9674"), srgb("#7e7a58"), srgb("#a09878"))
THREAD = srgb("#c9c3a0")


# =====================================================================================================
# Tileable metal and wood
# =====================================================================================================

def rusty_metal(seed, color, rough=0.62, metal=0.55, rust=0.22):
    """Gray-brown pipe metal with light rust blooms (matte), a few scratches showing brighter metal."""
    albedo, height, roughness, metallic = lt.metal_surface(seed, color, rough, metal, wear=0.5, edge_light=0.25)
    bloom = fbm(seed + 50, 3.0) + 0.35 * fnoise(seed + 51, 30.0)
    mask = np.clip((bloom - (0.62 - rust)) * 5.0, 0, 1)
    speck = np.clip((fnoise(seed + 52, 140.0) - 0.5) * 6, 0, 1) * 0.5
    m = np.clip(mask + speck * (1 - mask), 0, 1)
    tint = RUST[None, None, :] * (0.9 + 0.2 * fbm(seed + 53, 12.0)[..., None])
    albedo = albedo * (1 - 0.7 * m[..., None]) + tint * (0.7 * m[..., None])
    height = height + 0.5 * m * fnoise(seed + 54, 90.0)
    roughness = np.clip(roughness + 0.25 * m, 0, 1)
    metallic = metallic * (1 - 0.7 * m)
    return albedo, height, roughness, metallic


def galvanized(seed):
    """Light zinc-plated sheet: big soft spangle, a little white rust along the scratches."""
    spangle = np.round(fnoise(seed, 14.0) * 3) / 3
    albedo = np.asarray(srgb(PLATE))[None, None, :] * (1 + 0.05 * spangle[..., None] + 0.04 * fbm(seed + 1, 4.0)[..., None])
    sc = scratches(seed + 2, 120, bias=0.4) * 0.6
    albedo = albedo + 0.12 * sc[..., None] * (0.95 - albedo)
    dull = np.clip(fbm(seed + 3, 2.5) * 1.4 - 0.2, 0, 1)
    albedo = albedo * (1 - 0.10 * dull[..., None])
    height = 0.2 * spangle - 0.6 * sc
    roughness = 0.42 + 0.12 * dull + 0.05 * spangle
    return albedo, height, roughness, np.full((SIZE, SIZE), 0.72)


def brass(seed):
    tarnish = np.clip(fbm(seed, 3.0) * 1.3, 0, 1)
    albedo = np.asarray(srgb(BRASS))[None, None, :] * (1 - 0.25 * tarnish[..., None])
    return albedo, 0.1 * fnoise(seed + 1, 80.0), 0.38 + 0.2 * tarnish, np.full((SIZE, SIZE), 0.9)


def pallet_wood(seed):
    """Light raw pallet wood, grain along u: long fibres, growth rings, a few knots and nail stains, sun-bleached."""
    yy, xx = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    rings = fnoise(seed, 3.0, aniso=(8.0, 1.0)) * 2.5
    grain = np.sin(2 * np.pi * (yy * 14 + rings)) * 0.5 + 0.5
    fibre = fnoise(seed + 1, 200.0, aniso=(14.0, 1.0))
    streak = fnoise(seed + 2, 30.0, aniso=(10.0, 1.0))
    knots = np.zeros((SIZE, SIZE))
    rng = np.random.default_rng(seed + 3)
    for _ in range(3):
        cx, cy, r = rng.uniform(0, 1), rng.uniform(0, 1), rng.uniform(0.012, 0.025)
        dx = (xx - cx + 0.5) % 1.0 - 0.5
        dy = (yy - cy + 0.5) % 1.0 - 0.5
        d = np.sqrt((dx / 1.8) ** 2 + dy ** 2)
        knots = np.maximum(knots, np.clip(1 - d / r, 0, 1) ** 0.6)
    col = WOOD[None, None] * (0.93 + 0.1 * streak[..., None] + 0.05 * fibre[..., None])
    col = col + (WOOD_DARK - col) * (0.35 * smoothstep(0.55, 1.0, grain) + 0.8 * knots)[..., None]
    col = col + (WOOD_LIGHT - col) * (0.3 * smoothstep(0.5, 0.0, grain) * (0.5 + 0.5 * fbm(seed + 4, 2.0)))[..., None]
    weather = np.clip(fbm(seed + 5, 3.0) * 1.2, 0, 1)
    col = col + (srgb("#a69a88") - col) * (0.25 * weather)[..., None]           # sun-greyed in places
    height = 0.35 * grain + 0.4 * fibre - 0.6 * knots
    return np.clip(col, 0, 1), height, 0.78 + 0.1 * weather, np.zeros((SIZE, SIZE))


def make_materials():
    return {
        "pipe": lt.textured("scrap_seat_pipe", rusty_metal(301, PIPE), normal_strength=2.5),
        "fitting": lt.textured("scrap_seat_fitting", rusty_metal(313, FITTING, rough=0.58, rust=0.3), normal_strength=2.5),
        "plate": lt.textured("scrap_seat_plate", galvanized(327), normal_strength=2.0),
        "bolt": lt.textured("scrap_seat_bolt", lt.metal_surface(331, BOLT, 0.5, 0.8, wear=0.4, edge_light=0.2)),
        "brass": lt.textured("scrap_seat_brass", brass(337), normal_strength=1.0),
        "wood": lt.textured("scrap_seat_wood", pallet_wood(341), normal_strength=4.0),
    }


# =====================================================================================================
# Canvas cushions (baked per cushion on its box charts)
# =====================================================================================================

def _patch_sdf(a, b, patch):
    """Signed distance (m) to a rounded, slightly turned square patch {c: (a, b), half: (ha, hb), turn: deg}."""
    ca, cb = patch["c"]
    t = math.radians(patch.get("turn", 0.0))
    da, db = a - ca, b - cb
    ra = da * math.cos(t) + db * math.sin(t)
    rb = -da * math.sin(t) + db * math.cos(t)
    ha, hb = patch["half"]
    r = 0.006
    qa, qb = np.abs(ra) - (ha - r), np.abs(rb) - (hb - r)
    return np.hypot(np.maximum(qa, 0), np.maximum(qb, 0)) + np.minimum(np.maximum(qa, qb), 0) - r, ra, rb


class CanvasSurface:
    """Surface callback for rock_textures.bake: one cushion's olive-tan canvas.

    `patches` maps a face (axis index, sign) to a list of patches in that face's (a, b) coordinates (a and b are the
    two other axes in increasing order). `seam_inset` is how far in from each face edge the stitch row runs."""

    def __init__(self, half, seed, patches, seam_inset=0.014):
        self.half = np.asarray(half, float)
        self.noise = rt.Noise3(seed)
        self.fine = rt.Noise3(seed + 1)
        self.patches = patches
        self.inset = seam_inset

    def __call__(self, P, n, edge_d):
        k = int(np.argmax(np.abs(n)))
        sign = 1 if n[k] > 0 else -1
        ia, ib = [i for i in range(3) if i != k]
        a, b = P[:, ia], P[:, ib]

        # ---- the weave: a fine, even plain weave, a little irregular --------------------------------------
        lam = 0.0021
        wob = self.noise.fbm(P, 30.0, octaves=2) * 0.0004
        ga, gb = (a + wob) / lam, (b - wob) / lam
        fa, fb = ga - np.floor(ga), gb - np.floor(gb)
        over = ((np.floor(ga) + np.floor(gb)) % 2) == 0
        thread = np.where(over, np.sin(np.pi * fa), np.sin(np.pi * fb)) ** 0.7
        height = 0.00018 * thread
        mott = self.noise.fbm(P, 4.0, octaves=3)
        fibre = self.fine.fbm(P, 260.0, octaves=2)
        col = CANVAS[None] * (0.97 + 0.07 * mott[:, None] + 0.04 * fibre[:, None])
        col = col + (CANVAS_DARK - col) * (0.18 * (1 - thread))[:, None]
        col = col + (CANVAS_LIGHT - col) * (0.12 * thread * (0.5 + 0.5 * fibre))[:, None]

        # ---- soft wear: lighter on the top's middle, faint creases radiating from the sag -------------
        if k == 2 and sign > 0 or k == 1 and sign > 0:
            centre = np.exp(-((a / (self.half[ia] * 0.55)) ** 2 + (b / (self.half[ib] * 0.55)) ** 2))
            col = col + (CANVAS_LIGHT - col) * (0.22 * centre)[:, None]
            ang = np.arctan2(b, a)
            crease = np.clip(np.sin(ang * 7 + 2 * self.noise.fbm(P, 3.0, octaves=2)), 0, 1) ** 8 * (1 - centre) * 0.6
            col = col + (CANVAS_DARK - col) * (0.25 * crease)[:, None]
            height = height - 0.0008 * crease

        # ---- a stitch row beside the piping on every face edge ---------------------------------------
        de_a = self.half[ia] - np.abs(a)
        de_b = self.half[ib] - np.abs(b)
        stitch = np.zeros(len(P))
        for de, along in ((de_a, b), (de_b, a)):
            row = 1 - smoothstep(0.0006, 0.0013, np.abs(de - self.inset))
            dash = smoothstep(0.25, 0.35, (along / 0.007) % 1.0) * (1 - smoothstep(0.75, 0.85, (along / 0.007) % 1.0))
            stitch = np.maximum(stitch, row * dash)
        col = col + (CANVAS_DARK * 0.85 - col) * (0.55 * stitch)[:, None]
        height = height - 0.0004 * stitch

        # ---- the patches: a slightly different canvas, a sewn border, a raised edge ------------------
        for i, patch in enumerate(self.patches.get((k, sign), [])):
            d, ra, rb = _patch_sdf(a, b, patch)
            inside = 1 - smoothstep(-0.0006, 0.0006, d)
            tone = PATCHES[patch.get("tone", i) % len(PATCHES)]
            ptone = tone[None] * (0.97 + 0.05 * fibre[:, None] + 0.05 * self.fine.fbm(P + 3.1, 9.0, octaves=2)[:, None])
            ptone = ptone + (ptone * 0.86 - ptone) * (0.2 * (1 - thread))[:, None]
            col = col * (1 - inside[:, None]) + ptone * inside[:, None]
            hem = (1 - smoothstep(0.0, 0.003, np.abs(d + 0.0015))) * inside
            col = col + (col * 0.82 - col) * (0.6 * hem)[:, None]
            edge = np.maximum(np.abs(ra) - patch["half"][0], np.abs(rb) - patch["half"][1])
            row = 1 - smoothstep(0.0005, 0.0011, np.abs(d + 0.0055))
            along = np.where(np.abs(ra) - patch["half"][0] > np.abs(rb) - patch["half"][1], rb, ra)
            dash = smoothstep(0.2, 0.3, (along / 0.006) % 1.0) * (1 - smoothstep(0.7, 0.8, (along / 0.006) % 1.0))
            st = row * dash * inside
            col = col + (THREAD - col) * (0.75 * st)[:, None]
            height = height + 0.0007 * inside + 0.0004 * hem - 0.0003 * st + 0.0 * edge
        return {"albedo": np.clip(col, 0, 1), "height": height}


def piping_texture(w=128, h=256, seed=351):
    """The piped seam cord round each cushion, in a darker canvas: u round the cord, v along it."""
    jj, ii = np.mgrid[0:h, 0:w]
    u, v = (ii + 0.5) / w, (jj + 0.5) / h
    weave = np.sin(2 * np.pi * (u * 18)) * np.sin(2 * np.pi * (v * 24))
    noise = rt.Noise3(seed).fbm(np.stack([u.ravel() * 3, v.ravel() * 3, np.zeros(u.size)], -1), 3.0, octaves=3).reshape(h, w)
    col = CANVAS_DARK[None, None] * (1.0 + 0.06 * weave[..., None] + 0.06 * noise[..., None])
    return np.clip(col, 0, 1), 0.5 + 0.3 * weave
