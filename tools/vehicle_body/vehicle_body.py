"""The vehicle body parts (parts audit D3): Front Bumper, Rear Bumper, Mud Flap, Exhaust Pipe, License Plate and Roof
Rack, in the Salvager Tech look the craftable parts share (gunmetal and hull, deep copper hardware, mustard and graphite
safety marks, docs/art-direction.md). build_vehicle_body.py runs them; tools/part_kit/decor_kit.py has the shared
materials, shapes and runner, and tools/decor/industry.py the solid, loft and tube helpers.

Axes as decor_kit's: Blender Z up, the part's front towards -Y, centred on its grid box. Every part mounts by its
back face (+Y): bumper brackets, flap bracket, pipe flange, plate holder and the rack's feet (bottom) meet the box's
faces, so the part sits flush on whatever it is placed against.
"""

from __future__ import annotations

import math

from mathutils import Matrix, Vector
from PIL import Image, ImageDraw

import decor_kit as D
from decor_kit import Geo, Part, box, cylinder, dome, ring
import industry as I

TAU = 2 * math.pi
# The heat tint along the pipe's last span, from the gunmetal into straw, bronze, violet and blue at the mouth: dull and
# dark so it reads as blued steel, not chrome or paint.
HEAT = ("#6a6658", "#857150", "#76594a", "#5c4f6a", "#4a5674", "#3f5677")
GRAPHITE = (43, 47, 53)
MUSTARD = (224, 181, 48)

# =====================================================================================================
# Shapes
# =====================================================================================================


def inset(outline, b):
    """A convex counter-clockwise (u, v) outline moved inward by `b` (each corner along its bisector)."""
    out = []
    n = len(outline)
    for i in range(n):
        p0, p1, p2 = (Vector(outline[(i + k) % n]) for k in (-1, 0, 1))
        e1, e2 = (p1 - p0).normalized(), (p2 - p1).normalized()
        n1, n2 = Vector((-e1.y, e1.x)), Vector((-e2.y, e2.x))
        out.append(tuple(p1 + (n1 + n2) * (b / (1 + n1.dot(n2)))))
    return out


def bevel_prism(g, outline, w0, w1, b, mat, to3=lambda u, v, w: (u, v, w)):
    """A convex (u, v) outline extruded along w from w0 to w1, its two cap edges bevelled by `b`; `to3` maps
    (u, v, w) into the part's axes (default u = x, v = y, w = z)."""
    rings = []
    for w, pts in ((w0, inset(outline, b)), (w0 + b, outline), (w1 - b, outline), (w1, inset(outline, b))):
        rings.append([to3(u, v, w) for u, v in pts])
    return I.loft(g, rings, mat)


def ball(g, c, r, mat, segs=16, rings=8):
    """A UV sphere (the tow ball)."""
    c = Vector(c)
    verts = [tuple(c + Vector((0, 0, -r)))]
    for j in range(1, rings):
        ph = -math.pi / 2 + math.pi * j / rings
        for i in range(segs):
            th = TAU * i / segs
            verts.append(tuple(c + Vector((math.cos(ph) * math.cos(th), math.cos(ph) * math.sin(th), math.sin(ph))) * r))
    verts.append(tuple(c + Vector((0, 0, r))))
    faces = [(0, 1 + (i + 1) % segs, 1 + i) for i in range(segs)]
    for j in range(rings - 2):
        a, b = 1 + j * segs, 1 + (j + 1) * segs
        faces += [(a + i, a + (i + 1) % segs, b + (i + 1) % segs, b + i) for i in range(segs)]
    top, last = len(verts) - 1, 1 + (rings - 2) * segs
    faces += [(last + i, last + (i + 1) % segs, top) for i in range(segs)]
    return I.solid(g, verts, faces, mat)


def split_path(path, lengths):
    """Cuts a polyline at the given arc lengths from its start; returns the pieces (each shares its end point with the
    next one's start)."""
    pts = [Vector(p) for p in path]
    pieces, cur, run = [], [pts[0]], 0.0
    cuts = list(lengths)
    for a, b in zip(pts, pts[1:]):
        seg = (b - a).length
        while cuts and run + seg >= cuts[0]:
            p = a + (b - a) * ((cuts[0] - run) / seg)
            cur.append(p)
            pieces.append(cur)
            cur = [p]
            cuts.pop(0)
        cur.append(b)
        run += seg
    pieces.append(cur)
    return [[tuple(p) for p in piece] for piece in pieces if len(piece) > 1]


def path_length(path):
    pts = [Vector(p) for p in path]
    return sum((b - a).length for a, b in zip(pts, pts[1:]))


def chevron_image(pointing_right=True):
    """Mustard chevrons on graphite, pointing out to the bumper's end."""
    w, h = 256, 128
    im = Image.new("RGB", (w, h), GRAPHITE)
    d = ImageDraw.Draw(im)
    band = 30
    for k in range(-1, 5):
        x = 18 + k * 64
        d.polygon([(x, 0), (x + band, 0), (x + band + h / 2, h / 2), (x + band, h), (x, h), (x + h / 2, h / 2)], fill=MUSTARD)
    return im if pointing_right else im.transpose(Image.FLIP_LEFT_RIGHT)


def emblem(g, c, r, mat, t=0.004):
    """The colony's mark (tools/structures/colony_kit.py emblem) in relief on a face looking -Y: a ring round a rising
    chevron over a bar."""
    x, y, z = c
    I.torus(g, (x, y, z), (0, 1, 0), r * 0.82, r * 0.09, mat, segs=32, tsegs=6)
    for s in (-1, 1):
        rot = Matrix.Rotation(math.radians(s * 38), 3, "Y")
        box(g, (x + s * r * 0.24, y - t / 2, z + r * 0.12), (r * 0.39, t / 2, r * 0.1), mat, rot=rot)
    box(g, (x, y - t / 2, z - r * 0.36), (r * 0.5, t / 2, r * 0.07), mat)


# =====================================================================================================
# Bumpers
# =====================================================================================================

BAR_Z = 0.075            # the bar's half height
BAR_HALF_X = 0.995


def bar_outline(yf, yb, c=(0.07, 0.05)):
    """The bumper bar in plan (x, y): the front at yf, the back at yb, its front corners chamfered back (the chamfered
    ends), counter-clockwise."""
    hx, cx, cy, k = BAR_HALF_X, c[0], c[1], 0.012
    return [(-hx + cx, yf), (hx - cx, yf), (hx, yf + cy), (hx, yb - k), (hx - k, yb), (-hx + k, yb), (-hx, yb - k),
            (-hx, yf + cy)]


def bumper_bar(g, yf, yb, pads, bracket_x=0.5):
    """The shared bumper: a chamfered gunmetal bar from yf (front) to yb, a hull backing channel, rubber pad strips
    over the x spans in `pads`, mustard chevrons at both ends and two copper brackets bolted to the mounting face."""
    bevel_prism(g, bar_outline(yf, yb), -BAR_Z, BAR_Z, 0.012, "gun")
    # the backing channel, a rail behind the bar
    box(g, (0, yb + 0.01, 0), (0.86, 0.0102, 0.05), "hull", bevel=0.004)
    for x0, x1 in pads:
        box(g, ((x0 + x1) / 2, yf - 0.0105, 0), ((x1 - x0) / 2, 0.011, 0.028), "rubber", bevel=0.006)
    # chevrons on the flat of the front face, out to the chamfers
    for s, mat in ((1, "chev_r"), (-1, "chev_l")):
        D.decal(g, (s * 0.795, yf - 0.0006, 0), (1, 0, 0), (0, 0, 1), 0.115, 0.058, mat)
    # brackets: an arm from the backing channel to a flange on the mounting face, with its bolts
    for s in (-1, 1):
        x = s * bracket_x
        y0 = yb + 0.02
        box(g, (x, (y0 + 0.107) / 2, 0), (0.032, (0.107 - y0) / 2, 0.046), "copper", bevel=0.005)
        box(g, (x, 0.1155, 0), (0.075, 0.0095, 0.075), "copper", bevel=0.004)
        for dx in (-0.05, 0.05):
            for dz in (-0.05, 0.05):
                D.hexnut(g, (x + dx, 0.106, dz), (0, -1, 0), 0.01, 0.008, "bolt")
        # the through-bolts on the bar's face, above and below the pad
        for dz in (-0.05, 0.05):
            dome(g, (x, yf, dz), (0, -1, 0), 0.009, "bolt", segs=10)


def bumper_front(m):
    """8 x 1 x 1: a gunmetal bar with chamfered ends, a rubber pad strip along its face, two copper brackets bolted to
    the mounting face behind it, and mustard hazard chevrons at both ends."""
    g = Geo()
    bumper_bar(g, -0.1, -0.02, [(-0.66, 0.66)])
    return g


def bumper_rear(m):
    """8 x 1 x 1: the front bumper's bar set back a little, its pad split round a tow hitch: a square receiver under
    the bar, a ball mount with its pin and clip, and two copper safety-chain eyes."""
    g = Geo()
    yf, yb = -0.07, 0.005
    bumper_bar(g, yf, yb, [(-0.66, -0.14), (0.14, 0.66)])
    # receiver: a square tube under the middle of the bar, from the backing channel forward
    zc = -0.099
    box(g, (0, -0.012, zc), (0.036, 0.058, 0.0235), "hull", bevel=0.004)
    box(g, (0, -0.068, zc), (0.04, 0.004, 0.026), "gun", bevel=0.002)           # the receiver's lip
    # ball mount: the shank out of the receiver, the tongue, the ball on its neck
    box(g, (0, -0.096, zc), (0.026, 0.026, 0.017), "gun", bevel=0.004)
    cylinder(g, (0, -0.1, zc + 0.017), (0, -0.1, zc + 0.026), 0.017, "bolt", segs=6, phase=math.pi / 6)
    cylinder(g, (0, -0.1, zc + 0.026), (0, -0.1, zc + 0.044), 0.011, "gun", segs=14)
    ball(g, (0, -0.1, zc + 0.064), 0.024, "gun", segs=18, rings=10)
    # hitch pin across the receiver, its clip ring on the right
    cylinder(g, (-0.046, -0.03, zc), (0.046, -0.03, zc), 0.0065, "bolt", segs=10)
    I.torus(g, (0.05, -0.03, zc), (1, 0, 0), 0.012, 0.0025, "copper", segs=16, tsegs=5)
    # safety-chain eyes under the bar
    for s in (-1, 1):
        box(g, (s * 0.075, -0.03, -BAR_Z - 0.004), (0.014, 0.012, 0.004), "copper", bevel=0.002)
        I.torus(g, (s * 0.075, -0.03, -BAR_Z - 0.021), (0, 1, 0), 0.014, 0.004, "copper", segs=16, tsegs=6)
    return g


# =====================================================================================================
# Mud Flap
# =====================================================================================================


def mud_flap(m):
    """2 x 2 x 1: a black rubber flap hung from a gunmetal L-bracket (mounting plate on top, flange on the back), held in
    a clamp bar with three bolts, moulded ribs at the foot and the colony's mark stamped in the middle."""
    g = Geo()
    fy0, fy1 = -0.046, -0.034                        # the flap's front and back faces
    top, foot = 0.19, -0.245
    outline = I.rrect(0.22, (top - foot) / 2, 0.03, steps=3)
    zc = (top + foot) / 2
    outline = [(u, v + zc) for u, v in outline]
    bevel_prism(g, outline, fy0, fy1, 0.003, "rubber", to3=lambda u, v, w: (u, w, v))
    # moulded ribs across the foot
    for z in (-0.205, -0.18, -0.155):
        box(g, (0, fy0 - 0.002, z), (0.185, 0.0025, 0.005), "rubber", bevel=0.002)
    emblem(g, (0, fy0, -0.015), 0.085, "rubber_hi")
    # clamp: a bar each side of the flap's top edge, bolted through
    box(g, (0, fy0 - 0.006, top - 0.016), (0.215, 0.006, 0.02), "gun", bevel=0.003)
    box(g, (0, fy1 + 0.006, top + 0.012), (0.215, 0.006, 0.048), "gun", bevel=0.003)
    for x in (-0.15, 0.0, 0.15):
        D.hexnut(g, (x, fy0 - 0.012, top - 0.016), (0, -1, 0), 0.009, 0.007, "bolt")
    # L-bracket: the plate along the top to the mounting face, the flange down it, a gusset each side
    box(g, (0, (fy1 + 0.125) / 2, 0.2375), (0.2, (0.125 - fy1) / 2, 0.0075), "gun", bevel=0.003)
    box(g, (0, 0.1175, 0.185), (0.2, 0.0075, 0.06), "gun", bevel=0.003)
    for s in (-1, 1):
        I.solid(g, [(s * 0.19 - 0.004, 0.11, 0.23), (s * 0.19 - 0.004, 0.06, 0.23), (s * 0.19 - 0.004, 0.11, 0.15),
                    (s * 0.19 + 0.004, 0.11, 0.23), (s * 0.19 + 0.004, 0.06, 0.23), (s * 0.19 + 0.004, 0.11, 0.15)],
                [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "gun")
    for x in (-0.12, 0.12):
        dome(g, (x, 0.07, 0.245), (0, 0, 1), 0.009, "bolt", segs=10, height=0.0045)
        D.hexnut(g, (x, 0.11, 0.165), (0, -1, 0), 0.009, 0.007, "bolt")
    return g


# =====================================================================================================
# Exhaust Pipe
# =====================================================================================================


def exhaust_pipe(m):
    """1 x 3 x 1: a gunmetal tailpipe out of a copper flange on the mounting face, bent up into a stack and over at the
    top, its last span heat-blued (straw, bronze, violet, blue) round a sooty mouth, held by a copper clamp on a bracket."""
    g = Geo()
    r = 0.031
    zb = -0.29
    path = D.bend([(0, 0.1, zb), (0, 0.0, zb), (0, 0.0, 0.235), (0, -0.07, 0.318)], 0.07, 8)
    total = path_length(path)
    tip = 0.13
    bands = len(HEAT)
    pieces = split_path(path, [total - tip * (1 - k / bands) for k in range(bands)])
    I.tube(g, pieces[0], r, "gun", segs=16)
    for k, piece in enumerate(pieces[1:]):
        I.tube(g, piece, r, f"heat_{k}", segs=16)
    # the mouth: a rolled lip and soot inside
    end, before = Vector(path[-1]), Vector(path[-2])
    n = (end - before).normalized()
    cylinder(g, end - n * 0.012, end + n * 0.004, r + 0.004, f"heat_{bands - 1}", segs=16)
    D.disc(g, end + n * 0.0045, n, r - 0.004, "soot", segs=16)
    # flange and mounting plate at the bottom of the back face
    box(g, (0, 0.1165, zb), (0.07, 0.0085, 0.07), "gun", bevel=0.004)
    cylinder(g, (0, 0.094, zb), (0, 0.108, zb), 0.056, "copper", segs=20)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        D.hexnut(g, (0.04 * math.cos(a), 0.094, zb + 0.04 * math.sin(a)), (0, -1, 0), 0.0085, 0.007, "bolt")
    # clamp: a copper band round the stack, two ears and a bolt, a strap back to its own plate
    zc = 0.02
    ring(g, zc - 0.018, zc + 0.018, r - 0.002, r + 0.008, "copper", segs=20)
    for s in (-1, 1):
        box(g, (s * 0.011, r + 0.016, zc), (0.004, 0.012, 0.016), "copper", bevel=0.0015)
    cylinder(g, (-0.03, r + 0.018, zc), (0.03, r + 0.018, zc), 0.005, "bolt", segs=10)
    D.hexnut(g, (0.016, r + 0.018, zc), (1, 0, 0), 0.008, 0.007, "bolt")
    box(g, (0, (r + 0.03 + 0.108) / 2, zc), (0.02, (0.108 - r - 0.03) / 2, 0.012), "gun", bevel=0.003)
    box(g, (0, 0.1165, zc), (0.045, 0.0085, 0.045), "copper", bevel=0.003)
    for dz in (-0.028, 0.028):
        D.hexnut(g, (0, 0.108, zc + dz), (0, -1, 0), 0.0075, 0.006, "bolt")
    return g


# =====================================================================================================
# License Plate
# =====================================================================================================


def license_plate(m):
    """2 x 1 x 1: a bone plate with a graphite border and SV-04 stamped in, four copper rivets and a small mustard
    sticker, on a gunmetal holder that sits on the mounting face."""
    g = Geo()
    hy = 0.125
    box(g, (0, hy - 0.009, 0), (0.24, 0.009, 0.104), "gun", bevel=0.004)
    D.hexnut(g, (-0.215, hy - 0.018, 0.08), (0, -1, 0), 0.008, 0.005, "bolt")
    D.hexnut(g, (0.215, hy - 0.018, 0.08), (0, -1, 0), 0.008, 0.005, "bolt")
    fy, by = hy - 0.026, hy - 0.018           # the plate's face and back
    outline = I.rrect(0.205, 0.088, 0.016, steps=3)
    bevel_prism(g, outline, fy, by, 0.002, "plate", to3=lambda u, v, w: (u, w, v))
    # the stamped border, raised and painted graphite
    t = 0.0016
    for z in (-0.074, 0.074):
        box(g, (0, fy - t / 2, z), (0.19, t / 2, 0.0035), "graphite")
    for x in (-0.19, 0.19):
        box(g, (x, fy - t / 2, 0), (0.0035, t / 2, 0.0775), "graphite")
    D.pixel_text(g, "SV-04", (0, fy, -0.006), (1, 0, 0), (0, 0, 1), 0.0105, "graphite", t=0.0022)
    box(g, (-0.13, fy - 0.0006, 0.052), (0.018, 0.0006, 0.013), "mustard")
    for x in (-0.175, 0.175):
        for z in (-0.056, 0.056):
            dome(g, (x, fy, z), (0, -1, 0), 0.008, "copper", segs=10)
    return g


# =====================================================================================================
# Roof Rack
# =====================================================================================================


def roof_rack(m):
    """6 x 1 x 8: a rail frame of bent gunmetal tube (round-cornered perimeter and five cross tubes) on six feet with
    rubber pads, and eight mustard tie-down eyes round the frame."""
    g = Geo()
    hx, hy, zr, r = 0.69, 0.94, 0.05, 0.023
    loop = D.bend([(0, -hy, zr), (hx, -hy, zr), (hx, hy, zr), (-hx, hy, zr), (-hx, -hy, zr), (0, -hy, zr)], 0.12, 8)
    I.tube(g, loop, r, "gun", segs=14)
    cylinder(g, (-0.03, -hy, zr), (0.03, -hy, zr), r + 0.004, "hull", segs=14)     # the splice sleeve
    for y in (-0.62, -0.31, 0.0, 0.31, 0.62):
        cylinder(g, (-hx, y, zr), (hx, y, zr), 0.018, "gun", segs=12)
        for s in (-1, 1):
            cylinder(g, (s * (hx - 0.038), y, zr), (s * (hx - 0.005), y, zr), 0.022, "hull", segs=12)
    # feet: a riser under the side rail with a saddle round it, on a rubber pad
    for s in (-1, 1):
        for y in (-0.78, 0.0, 0.78):
            x = s * hx
            box(g, (x, y, -0.04), (0.022, 0.035, 0.075), "gun", bevel=0.005)
            box(g, (x, y, zr - 0.006), (0.03, 0.042, 0.024), "hull", bevel=0.005)
            box(g, (x, y, -0.1165), (0.042, 0.062, 0.0085), "rubber", bevel=0.003)
            box(g, (x, y, -0.104), (0.034, 0.052, 0.006), "gun", bevel=0.003)
            D.hexnut(g, (x + s * 0.03, y, zr - 0.006), (s, 0, 0), 0.008, 0.006, "bolt")
    # tie-down eyes: a mustard saddle on the rail with a ring standing up out of it, in the rail's plane
    eyes = [((s * hx, y), "y") for s in (-1, 1) for y in (-0.44, 0.44)]
    eyes += [((x, s * hy), "x") for s in (-1, 1) for x in (-0.36, 0.36)]
    for (x, y), rail in eyes:
        half = (0.03, 0.02, 0.007) if rail == "x" else (0.02, 0.03, 0.007)
        box(g, (x, y, zr + r - 0.002), half, "mustard", bevel=0.003)
        I.torus(g, (x, y, zr + r + 0.023), (0, 1, 0) if rail == "x" else (1, 0, 0), 0.02, 0.0055, "mustard", segs=18, tsegs=6)
    return g


# =====================================================================================================
# Materials and the registry
# =====================================================================================================


def materials(m):
    # copper kept deep and brown so it never reads orange (as industry.materials_15)
    m["copper"] = D.fm.metal("vb_copper", 1201, "#7e5444", 0.34, 0.95, wear=0.5)
    m["chev_r"] = D.image_mat("vb_chevron_r", chevron_image(True), rough=0.6)
    m["chev_l"] = D.image_mat("vb_chevron_l", chevron_image(False), rough=0.6)
    m["rubber_hi"] = D.pt.plain("vb_rubber_hi", "#3b3d40", 0.85, 0.0)
    m["plate"] = D.fm.paint("vb_plate", 1203, "#e2dac4")
    # heat tint on the pipe's last span, dull and dark so it reads as blued steel, not chrome or paint
    for k, color in enumerate(HEAT):
        m[f"heat_{k}"] = D.fm.metal(f"vb_heat_{k}", 1205 + 2 * k, color, 0.4, 0.85, wear=0.3)


PARTS = {
    "bumper_front": Part((8, 1, 1), bumper_front, "Gunmetal bar with chamfered ends, rubber pad strip, two copper bolt brackets, mustard hazard chevrons at the ends."),
    "bumper_rear": Part((8, 1, 1), bumper_rear, "As the front bumper, the pad split round a tow hitch: receiver, ball mount with pin and clip, copper chain eyes."),
    "mud_flap": Part((2, 2, 1), mud_flap, "Black rubber flap on a gunmetal L-bracket and clamp bar, moulded ribs, the colony's mark stamped in."),
    "exhaust_pipe": Part((1, 3, 1), exhaust_pipe, "Gunmetal tailpipe bent up into a stack, heat-blued tip with a sooty mouth, copper flange and clamp. Not chrome."),
    "license_plate": Part((2, 1, 1), license_plate, "Bone plate stamped SV-04 with a graphite border, copper rivets, a mustard sticker, on a gunmetal holder."),
    "roof_rack": Part((6, 1, 8), roof_rack, "Rail frame of bent gunmetal tube with five cross tubes on six rubber-padded feet, eight mustard tie-down eyes."),
}
