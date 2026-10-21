"""Batch 9a and 9b of the parts map: pipes and valves, in three bores (Colony Works services).

* Small Pipe, 1 x 1 bore: galvanised zinc with steel couplings; fittings are cast-steel bodies with a stub per outlet.
* Large Pipe, 2 x 2 bore (ours, size kept): bone pipe, steel half flanges at every face, a mustard band. Scrap
  Mechanic's middle "Pipe" family is this bore, so it folds in here instead of making near-duplicates.
* Main Pipe, 3 x 3 bore (new: Scrap Mechanic's Large Pipes): bone, heavier flanges, a steel-blue band, plus the Cap,
  Mount, Extension (an expansion joint) and the Compressor.

Two parts that meet at a block face make one bolted flange (each carries a half). Built by build_structural.py.
"""

from __future__ import annotations

import math

import structural_kit as S
from mathutils import Matrix, Vector

Geo, box, cylinder = S.Geo, S.box, S.cylinder
B = S.BLOCK
E = S.EPS
AXES = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}


def lathe_along(g, profile, center, d, mat, steps=32):
    """A lathe of an (r, t) profile about the axis through `center` along `d` (t measured along d)."""
    tmp = Geo()
    S.K.lathe(tmp, profile, mat, steps=steps)
    m = S.to(d).to_4x4()
    m.translation = Vector(center)
    g.extend(tmp, m)


# =====================================================================================================
# Small pipes (1 x 1)
# =====================================================================================================

SR, SW, SC = 0.058, 0.009, 0.074     # pipe radius, wall, coupling radius
SCL = 0.016                          # half a coupling's length (at each face)


def small_coupling(g, face, d):
    """Half a threaded coupling at a block face, with a bore."""
    face, d = Vector(face), Vector(d)
    S.sweep_hollow(g, S.hexagon(SC, 3), S.circle(SR - SW, 18), [face - d * SCL, face], S._up_for(d),
                   "steel", inner_mat="hole")
    S.sweep_hollow(g, S.circle(SC * 0.93, 20), S.circle(SR - SW, 20), [face - d * (SCL + 0.006), face - d * SCL],
                   S._up_for(d), "steel", inner_mat="hole")


def small_run(length):
    def build():
        g = Geo()
        L = length * B / 2 - E
        S.pipe_run(g, (0, -L + SCL, 0), (0, L - SCL, 0), SR, "zinc", wall=SW)
        for s in (-1, 1):
            small_coupling(g, (0, s * L, 0), (0, s, 0))
        # a stencilled flow band every metre
        for k in range(max(1, length // 4)):
            y = -L + (k + 0.5) * (2 * L / max(1, length // 4))
            S.band(g, (0, y, 0), (0, 1, 0), SR + 0.0015, 0.03, "mustard")
        return g
    return build


def small_fitting(outlets):
    """A cast-steel body at the centre and a stub and half coupling per outlet ("+x", "-y", ...)."""
    def build():
        g = Geo()
        box(g, (0, 0, 0), (0.078, 0.078, 0.078), "steel", bevel=0.026)
        for o in outlets:
            d = Vector(AXES[o])
            face = d * (B / 2 - E)
            S.pipe_run(g, d * 0.06, face - d * SCL, SR, "zinc", wall=SW, rims=(False, True))
            small_coupling(g, face, d)
        # casting ribs on the faces with no outlet
        for o, d in AXES.items():
            if o not in outlets:
                d = Vector(d)
                S.K.disc(g, d * 0.0785, d, 0.035, "zinc_dark", segs=16)
        return g
    return build


def small_bend():
    g = Geo()
    c = Vector((B / 2, -B / 2, 0))
    R = B / 2
    a0 = math.pi - math.asin(SCL / R)
    a1 = math.pi / 2 + math.asin(SCL / R)
    S.pipe_elbow(g, c, R, SR, a0, a1, "z", "zinc", (0, 0, 1), wall=SW, steps=16)
    small_coupling(g, (0, -B / 2 + E, 0), (0, -1, 0))
    small_coupling(g, (B / 2 - E, 0, 0), (1, 0, 0))
    return g


def valve():
    """A gate valve on a small pipe: cast body, bonnet and a signal-red hand wheel on top."""
    g = Geo()
    L = B / 2 - E
    S.pipe_run(g, (0, -L + SCL, 0), (0, L - SCL, 0), SR, "zinc", wall=SW)
    for s in (-1, 1):
        small_coupling(g, (0, s * L, 0), (0, s, 0))
    lathe_along(g, [(0.0, -0.062), (0.05, -0.062), (0.075, -0.035), (0.078, 0.0), (0.075, 0.035), (0.05, 0.062), (0.0, 0.062)],
                (0, 0, 0), (0, 1, 0), "graphite", steps=24)
    for s in (-1, 1):
        cylinder(g, (0, s * 0.055, 0), (0, s * 0.07, 0), 0.07, "graphite", segs=6, phase=math.pi / 6)
    cylinder(g, (0, 0, 0.05), (0, 0, 0.075), 0.045, "graphite", segs=6, phase=math.pi / 6)
    cylinder(g, (0, 0, 0.075), (0, 0, 0.1), 0.022, "graphite", segs=12)
    cylinder(g, (0, 0, 0.1), (0, 0, 0.112), 0.006, "steel", segs=8)
    zr = 0.104
    S.sweep(g, S.circle(0.0075, 10), S.arc((0, 0, zr), 0.07, 0, 2 * math.pi, n=32, axis="z"), (0, 0, 1), "signal")
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        cylinder(g, (0.012 * math.cos(a), 0.012 * math.sin(a), zr), (0.066 * math.cos(a), 0.066 * math.sin(a), zr), 0.006, "signal", segs=8)
    cylinder(g, (0, 0, zr - 0.009), (0, 0, zr + 0.009), 0.016, "signal", segs=12)
    hexnut_top = zr + 0.009
    S.hexnut(g, (0, 0, hexnut_top), (0, 0, 1), 0.008, 0.006, "steel")
    return g


# =====================================================================================================
# Large (2 x 2) and Main (3 x 3) pipes
# =====================================================================================================

class Bore:
    def __init__(self, cells, r, wall, flange_r, flange_t, bolts, band_mat, bolt_r):
        self.half = cells * B / 2 - E
        self.r, self.wall, self.fr, self.ft, self.bolts, self.band, self.bolt_r = r, wall, flange_r, flange_t, bolts, band_mat, bolt_r


LARGE = Bore(2, 0.185, 0.016, 0.238, 0.024, 8, "mustard", 0.011)
MAIN = Bore(3, 0.29, 0.02, 0.36, 0.03, 12, "blue", 0.014)


def end_flange(g, bore, face, d, open_end=True):
    S.flange(g, face, d, bore.r - bore.wall, bore.fr, bore.ft, "steel", bolts=bore.bolts, bolt_r=bore.bolt_r)
    if open_end:
        S.grate(g, face, d, bore.r - bore.wall, bars=3 if bore is LARGE else 4, depth=0.045)


def big_run(bore, length):
    def build():
        g = Geo()
        L = length * B / 2 - E
        S.pipe_run(g, (0, -L + bore.ft, 0), (0, L - bore.ft, 0), bore.r, "bone", wall=bore.wall, rims=(False, False))
        for s in (-1, 1):
            end_flange(g, bore, (0, s * L, 0), (0, s, 0))
        n = max(1, round(length * B / 1.0))
        for k in range(n):
            y = -L + (k + 0.5) * 2 * L / n
            S.band(g, (0, y, 0), (0, 1, 0), bore.r + 0.002, 0.07 if bore is LARGE else 0.1, bore.band)
        return g
    return build


def big_elbow(bore):
    def build():
        g = Geo()
        h = bore.half
        c = Vector((h, -h, 0))
        R = h
        da = bore.ft / R
        S.pipe_elbow(g, c, R, bore.r, math.pi - da, math.pi / 2 + da, "z", "bone", (0, 0, 1), wall=bore.wall, steps=20)
        end_flange(g, bore, (0, -h, 0), (0, -1, 0))
        end_flange(g, bore, (h, 0, 0), (1, 0, 0))
        a = math.radians(135)
        p = c + Vector((math.cos(a), math.sin(a), 0)) * R
        t = Vector((math.sin(a), -math.cos(a), 0))
        S.band(g, p, t, bore.r + 0.002, 0.07 if bore is LARGE else 0.1, bore.band)
        return g
    return build


def big_tee(bore):
    def build():
        g = Geo()
        h = bore.half
        S.pipe_run(g, (0, -h + bore.ft, 0), (0, h - bore.ft, 0), bore.r, "bone", wall=bore.wall, rims=(False, False))
        S.pipe_run(g, (bore.r * 0.6, 0, 0), (h - bore.ft, 0, 0), bore.r * 0.98, "bone", wall=bore.wall, rims=(False, False))
        # the weld collar round the branch
        lathe_along(g, [(bore.r * 0.98, 0.0), (bore.r * 1.06, 0.0), (bore.r * 1.06, 0.03), (bore.r * 0.98, 0.05)],
                    (bore.r * 0.9, 0, 0), (1, 0, 0), "steel", steps=28)
        for s in (-1, 1):
            end_flange(g, bore, (0, s * h, 0), (0, s, 0))
        end_flange(g, bore, (h, 0, 0), (1, 0, 0))
        S.band(g, (-0.0, -h * 0.55, 0), (0, 1, 0), bore.r + 0.002, 0.07 if bore is LARGE else 0.1, bore.band)
        return g
    return build


def main_extension():
    """An expansion joint: flanged stubs, a steel bellows between them and four tie rods."""
    g = Geo()
    bore = MAIN
    h = bore.half
    for s in (-1, 1):
        end_flange(g, bore, (0, s * h, 0), (0, s, 0))
        S.pipe_run(g, (0, s * (h - bore.ft), 0), (0, s * 0.2, 0), bore.r, "bone", wall=bore.wall, rims=(False, False))
        S.flange(g, (0, s * 0.17, 0), (0, -s, 0), bore.r - bore.wall, 0.345, 0.03, "blue", bolts=0)
    prof = [(bore.r - bore.wall, -0.17)]
    n = 7
    for k in range(n + 1):
        t = -0.17 + 0.34 * k / n
        prof.append((bore.r + (0.035 if k % 2 else 0.005), t))
    prof.append((bore.r - bore.wall, 0.17))
    lathe_along(g, prof, (0, 0, 0), (0, 1, 0), "steel", steps=40)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = Vector((math.cos(a), 0, math.sin(a))) * 0.325
        cylinder(g, p + Vector((0, -0.215, 0)), p + Vector((0, 0.215, 0)), 0.012, "graphite", segs=10)
        for s in (-1, 1):
            S.hexnut(g, p + Vector((0, s * 0.2, 0)), (0, s, 0), 0.02, 0.016, "zinc")
    return g


def main_mount():
    """A 3 x 3 x 1 pipe support: a split clamp round the pipe, bolted lugs, and a saddle web down to a foot plate."""
    g = Geo()
    w = B / 2 - E
    h = MAIN.half
    r0, r1 = MAIN.r + 0.002, MAIN.r + 0.03
    S.sweep_hollow(g, S.circle(r1, 32), S.circle(r0, 32), [(0, -0.05, 0), (0, 0.05, 0)], (0, 0, 1), "blue", inner_mat="blue_dark")
    for s in (-1, 1):
        box(g, (s * (r1 + 0.02), 0, 0), (0.026, 0.05, 0.012), "blue", bevel=0.004)
        for y in (-0.025, 0.025):
            S.bolt(g, (s * (r1 + 0.026), y, 0.012), (0, 0, 1), 0.009)
            S.hexnut(g, (s * (r1 + 0.026), y, -0.012), (0, 0, -1), 0.009, 0.008, "zinc")
    zb = -h
    box(g, (0, 0, zb + 0.014), (0.33, w, 0.014), "graphite", bevel=0.005)
    box(g, (0, 0, (zb + 0.028 - r1 * 0.7) / 2), (0.016, 0.04, (-r1 * 0.7 - zb - 0.028) / 2), "blue", bevel=0.003)
    for s in (-1, 1):
        a = Vector((s * 0.28, 0, zb + 0.028))
        b = Vector((s * r1 * 0.72, 0, -r1 * 0.69))
        S.sweep(g, S.rect(0.04, 0.008), [a, b], (0, 1, 0), "blue")
        for y in (-0.08, 0.08):
            S.bolt(g, (s * 0.29, y, zb + 0.028), (0, 0, 1), 0.012)
    return g


def main_cap():
    """A 3 x 3 x 1 blind end: a bolted flange at its back face and a dished bone cover with a hatch and handle."""
    g = Geo()
    w = B / 2 - E
    S.flange(g, (0, -w, 0), (0, -1, 0), MAIN.r - MAIN.wall, MAIN.fr, MAIN.ft, "steel", bolts=MAIN.bolts, bolt_r=MAIN.bolt_r)
    S.K.disc(g, (0, -w + 0.002, 0), (0, -1, 0), MAIN.r - MAIN.wall, "hole", segs=28)
    y0 = -w + MAIN.ft
    prof = [(0.0, y0), (MAIN.r + 0.02, y0), (MAIN.r + 0.02, y0 + 0.03), (MAIN.r - 0.02, y0 + 0.08), (0.2, y0 + 0.13),
            (0.0, y0 + 0.15)]
    lathe_along(g, prof, (0, 0, 0), (0, 1, 0), "bone", steps=40)
    top = y0 + 0.15
    box(g, (0, top - 0.004, 0), (0.09, 0.012, 0.07), "blue", bevel=0.006)
    for x in (-0.07, 0.07):
        for z in (-0.05, 0.05):
            S.bolt(g, (x, top + 0.008, z), (0, 1, 0), 0.006)
    S.sweep(g, S.circle(0.009, 10), [Vector(p) for p in S.K.bezier((-0.05, top + 0.008, 0), (-0.05, top + 0.05, 0),
                                                                    (0.05, top + 0.05, 0), (0.05, top + 0.008, 0), 10)],
            (0, 0, 1), "graphite")
    S.band(g, (0, y0 + 0.012, 0), (0, 1, 0), MAIN.r + 0.022, 0.016, "blue")
    return g


def main_compressor():
    """A 5 x 5 x 8 inline compressor on the Main Pipe: a finned steel-blue drum between two bells, a graphite motor
    with a fan guard on top, saddle feet, flanged pipe stubs at both ends."""
    g = Geo()
    hx, hy, hz = 0.625 - E, 1.0 - E, 0.625 - E
    bore = MAIN
    for s in (-1, 1):
        end_flange(g, bore, (0, s * hy, 0), (0, s, 0))
        S.pipe_run(g, (0, s * (hy - bore.ft), 0), (0, s * 0.62, 0), bore.r, "bone", wall=bore.wall, rims=(False, False))
        S.flange(g, (0, s * 0.62, 0), (0, -s, 0), bore.r - bore.wall, 0.36, 0.03, "steel", bolts=12, bolt_r=0.013)
    lathe_along(g, [(0.0, -0.6), (0.36, -0.6), (0.5, -0.46), (0.5, 0.46), (0.36, 0.6), (0.0, 0.6)], (0, 0, 0), (0, 1, 0),
                "blue", steps=48)
    for k in range(9):
        y = -0.4 + 0.1 * k
        lathe_along(g, [(0.5, y - 0.012), (0.56, y - 0.008), (0.56, y + 0.008), (0.5, y + 0.012)], (0, 0, 0), (0, 1, 0),
                    "blue_dark", steps=24)
    S.band(g, (0, -0.47, 0), (0, 1, 0), 0.47, 0.03, "mustard")
    S.band(g, (0, 0.47, 0), (0, 1, 0), 0.47, 0.03, "mustard")
    # motor on top
    box(g, (0, 0.05, 0.5), (0.2, 0.28, 0.07), "graphite", bevel=0.02)
    for k in range(6):
        box(g, (0, -0.15 + 0.07 * k, 0.575), (0.18, 0.01, 0.008), "graphite", bevel=0.003)
    lathe_along(g, [(0.0, 0.0), (0.105, 0.0), (0.105, 0.025), (0.0, 0.025)], (0, -0.24, 0.5), (0, -1, 0), "graphite", steps=28)
    for k in range(3):
        S.sweep(g, S.circle(0.004, 6), S.arc((0, -0.27, 0.5), 0.03 + 0.03 * k, 0, 2 * math.pi, n=24, axis="y"),
                (0, 1, 0), "steel")
    box(g, (0.21, 0.05, 0.5), (0.008, 0.08, 0.04), "bone", bevel=0.003)
    # saddle feet
    for s in (-1, 1):
        y = s * 0.3
        box(g, (0, y, -hz + 0.02), (0.55, 0.08, 0.02), "graphite", bevel=0.006)
        box(g, (0, y, -0.52), (0.4, 0.03, 0.08), "blue", bevel=0.006)
        for x in (-0.48, 0.48):
            S.bolt(g, (x, y, -hz + 0.04), (0, 0, 1), 0.014)
    return g


# =====================================================================================================
# The batches
# =====================================================================================================

def parts_9a():
    P = S.Part
    return [
        P("small_pipe_short", (1, 1, 2), small_run(2), "Small Pipe (Short) (1 x 1 x 2 blocks)",
          ["Ours. Galvanised pipe with a half coupling at each end: two pipes make one coupling."]),
        P("small_pipe_long", (1, 1, 4), small_run(4), "Small Pipe (Long) (1 x 1 x 4 blocks)",
          ["Ours. A mustard flow band in the middle."]),
        P("small_pipe_bend", (1, 1, 1), small_bend, "Small Pipe (Bend) (1 x 1 x 1 blocks)",
          ["New: a swept 90-degree bend."]),
        P("small_pipe_corner", (1, 1, 1), small_fitting(["-y", "+x", "+z"]), "Small Pipe (Corner) (1 x 1 x 1 blocks)",
          ["Ours, now Scrap Mechanic's corner: three outlets on three axes (back, right, up)."]),
        P("small_pipe_tee", (1, 1, 1), small_fitting(["-y", "+y", "+x"]), "Small Pipe (Tee) (1 x 1 x 1 blocks)",
          ["Ours. A cast-steel tee."]),
        P("small_pipe_cross", (1, 1, 1), small_fitting(["-y", "+y", "-x", "+x"]), "Small Pipe (Cross) (1 x 1 x 1 blocks)",
          ["Ours (Scrap Mechanic's Small Pipe Four Way)."]),
        P("small_pipe_four_way_tee", (1, 1, 1), small_fitting(["-y", "+y", "+x", "+z"]),
          "Small Pipe (Four Way Tee) (1 x 1 x 1 blocks)", ["New: a tee with an outlet up."]),
        P("small_pipe_five_way", (1, 1, 1), small_fitting(["-y", "+y", "-x", "+x", "+z"]),
          "Small Pipe (Five Way) (1 x 1 x 1 blocks)", ["New: a cross with an outlet up."]),
        P("small_pipe_six_way", (1, 1, 1), small_fitting(list(AXES)), "Small Pipe (Six Way) (1 x 1 x 1 blocks)",
          ["New: an outlet on every face."]),
        P("valve", (1, 1, 1), valve, "Valve (1 x 1 x 1 blocks)",
          ["New: a gate valve on a small pipe, its hand wheel signal red (the world's valve housings' colour)."]),
    ]


def parts_9b():
    P = S.Part
    return [
        P("large_pipe_short", (2, 2, 2), big_run(LARGE, 2), "Large Pipe (Short) (2 x 2 x 2 blocks)",
          ["Ours, size kept. Bone pipe, half flanges (8 bolts) at both faces, a mustard band, grated bore."]),
        P("large_pipe_long", (2, 2, 4), big_run(LARGE, 4), "Large Pipe (Long) (2 x 2 x 4 blocks)",
          ["Ours, size kept. Scrap Mechanic's Pipe Long and Short are this bore."]),
        P("large_pipe_corner", (2, 2, 2), big_elbow(LARGE), "Large Pipe (Corner) (2 x 2 x 2 blocks)",
          ["Ours, size kept: a 90-degree elbow."]),
        P("large_pipe_tee", (2, 2, 2), big_tee(LARGE), "Large Pipe (Tee) (2 x 2 x 2 blocks)",
          ["Ours, size kept: a welded tee with a collar on the branch."]),
        P("main_pipe_short", (3, 3, 3), big_run(MAIN, 3), "Main Pipe (Short) (3 x 3 x 3 blocks)",
          ["New: Scrap Mechanic's Large Pipe Short, the 3 x 3 bore. 12-bolt flanges, a steel-blue band."]),
        P("main_pipe_long", (3, 3, 6), big_run(MAIN, 6), "Main Pipe (Long) (3 x 3 x 6 blocks)",
          ["New: Scrap Mechanic's Large Pipe Long."]),
        P("main_pipe_corner", (3, 3, 3), big_elbow(MAIN), "Main Pipe (Corner) (3 x 3 x 3 blocks)",
          ["New: Scrap Mechanic's Large Pipe Corner."]),
        P("main_pipe_tee", (3, 3, 3), big_tee(MAIN), "Main Pipe (Tee) (3 x 3 x 3 blocks)",
          ["New: Scrap Mechanic's Large Pipe Join."]),
        P("main_pipe_extension", (3, 3, 3), main_extension, "Main Pipe (Extension) (3 x 3 x 3 blocks)",
          ["New: Scrap Mechanic's Large Pipe Extension, made an expansion joint: bellows and four tie rods."]),
        P("main_pipe_mount", (3, 3, 1), main_mount, "Main Pipe (Mount) (3 x 3 x 1 blocks)",
          ["New: a split clamp on a saddle web and foot plate, holding a Main Pipe up off the floor."]),
        P("main_pipe_cap", (3, 3, 1), main_cap, "Main Pipe (Cap) (3 x 3 x 1 blocks)",
          ["New: a blind end with a dished cover, a bolted hatch and a handle."]),
        P("main_pipe_compressor", (5, 5, 8), main_compressor, "Pipe Compressor (5 x 5 x 8 blocks)",
          ["New: Scrap Mechanic's Large Pipe Compressor. A finned drum and motor inline on a Main Pipe."]),
    ]
