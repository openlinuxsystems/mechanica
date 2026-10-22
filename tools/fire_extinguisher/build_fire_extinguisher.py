"""Build the Fire Extinguisher item (Consumables batch C2a), its held model and its icon.

What it builds (procedurally, from scratch: no .blend inputs, no downloads):

* ``mods/vanilla/meshes/items/fire_extinguisher.glb`` - the item as it lies in the world and in the stations: a 1 x 1 x 2
  block item (0.25 x 0.25 x 0.5 m centred on the origin, front -Y, Z up). Colony Works (``docs/art-direction.md``): a
  signal red (``#C8423A``, never orange) steel cylinder with a domed shoulder on a graphite rubber foot, a white label
  band with the colony's steel-blue strip and a graphite pictogram (a foam cone over a flame, no text), a steel valve
  head with a pressure gauge (bone face, a mustard arc), a carry handle and a squeeze lever with graphite grips, a
  safety pin with a mustard tag, and a black rubber hose down its left side, clipped to the body, ending in a flared
  black nozzle.
* ``mods/vanilla/meshes/items/fire_extinguisher_held.glb`` - the same extinguisher with its hose taken off the clip and
  brought forward over the top, the nozzle aimed along -Y, for the hand (``held.mesh``), its origin at the carry handle's
  grip. An empty named ``nozzle`` sits at the nozzle's mouth: the foam spray (``held.spray``) leaves from there.
* ``mods/vanilla/textures/items/fire_extinguisher_icon.png`` - the hotbar, inventory and Fabricator picture, 256 x 256 on a
  clear background, three-quarter from the front left.

    pip install bpy==5.2.2 numpy pillow
    python tools/fire_extinguisher/build_fire_extinguisher.py                  # writes the .glb files and the icon
    python tools/fire_extinguisher/build_fire_extinguisher.py --render OUT_DIR # also renders review images into OUT_DIR

The outputs are committed; re-run this script after changing it and commit the result. The build is deterministic.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "part_kit"))
import part_kit as K  # noqa: E402  (first: it imports bpy)
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

bl, bbe, lt, pt = K.bl, K.bbe, K.lt, K.pt
Geo, box, cylinder, ring, dome, sweep, bezier = K.Geo, K.box, K.cylinder, K.ring, K.dome, K.sweep, K.bezier
lathe = K.lathe

REPO = K.REPO
MESHES = REPO / "mods" / "vanilla" / "meshes" / "items"
ICONS = REPO / "mods" / "vanilla" / "textures" / "items"

HALF = (0.125, 0.125, 0.25)     # the item's half size: 1 x 1 x 2 blocks
FLOOR = -HALF[2]
R = 0.07                        # the cylinder's radius
Z_FOOT = FLOOR + 0.004          # the bottom of the rubber foot
Z_BODY = FLOOR + 0.018          # where the red body starts above the foot
Z_SHOULDER = 0.105              # where the cylinder starts to round over
Z_NECK = 0.158                  # the top of the dome, where the neck collar sits
R_NECK = 0.024
Z_HEAD = 0.19                   # the middle of the valve head
LABEL = (-0.105, 0.045)         # the label band's bottom and top
LABEL_SPAN = math.radians(118)  # how far round the front the label wraps
GRIP = Vector((0.0, 0.085, Z_HEAD - 0.012))  # the middle of the carry handle's grip: the held model's origin


# =====================================================================================================
# Materials and the label
# =====================================================================================================

def label_image():
    """The label: white with a steel-blue colony strip along the top and a graphite pictogram of a foam cone put over a
    flame, between thin graphite rules. Shapes only (no fonts), so it is the same on every machine."""
    w, h = 512, 256
    im = Image.new("RGB", (w, h), (238, 236, 228))
    d = ImageDraw.Draw(im)
    blue, ink, red, mustard = (62, 106, 140), (43, 47, 53), (200, 66, 58), (224, 181, 48)
    d.rectangle([0, 0, w, 40], fill=blue)
    for k in range(5):                                   # the colony mark: a row of small bone squares on the strip
        d.rectangle([w // 2 - 54 + k * 24, 14, w // 2 - 42 + k * 24, 26], fill=(217, 209, 188))
    d.rectangle([0, h - 18, w, h], fill=ink)
    d.rectangle([0, h - 30, w, h - 24], fill=mustard)
    # the pictogram, centred: a flame on the right, a foam cone from a nozzle on the left smothering it
    cx, cy = w // 2, 140
    flame = [(cx + 40, cy + 70), (cx + 8, cy + 40), (cx + 14, cy + 6), (cx + 30, cy - 20), (cx + 34, cy + 10),
             (cx + 48, cy - 44), (cx + 66, cy - 6), (cx + 74, cy + 18), (cx + 84, cy - 4), (cx + 90, cy + 34),
             (cx + 74, cy + 70)]
    d.polygon(flame, fill=red)
    d.polygon([(cx + 34, cy + 64), (cx + 26, cy + 44), (cx + 40, cy + 18), (cx + 48, cy + 40), (cx + 60, cy + 30),
               (cx + 64, cy + 64)], fill=(238, 236, 228))
    d.polygon([(cx - 96, cy - 8), (cx - 60, cy - 14), (cx - 60, cy + 14), (cx - 96, cy + 8)], fill=ink)  # nozzle
    d.polygon([(cx - 60, cy - 14), (cx + 34, cy - 58), (cx + 40, cy + 2), (cx - 60, cy + 14)], fill=ink)  # foam cone
    for k, (dx, dy, r) in enumerate(((10, -36, 9), (28, -24, 8), (20, -6, 7), (-6, -20, 6), (40, -44, 7))):
        d.ellipse([cx + dx - r, cy + dy - r, cx + dx + r, cy + dy + r], fill=(238, 236, 228))
    d.rectangle([cx - 120, cy - 72, cx + 120, cy + 84], outline=ink, width=6)
    return np.asarray(im, float) / 255.0


def gauge_image():
    """The pressure gauge's face: bone, graphite ticks, a mustard arc where it should read, a red needle in it."""
    s = 128
    im = Image.new("RGB", (s, s), (43, 47, 53))
    d = ImageDraw.Draw(im)
    d.ellipse([6, 6, s - 7, s - 7], fill=(217, 209, 188))
    d.arc([20, 20, s - 21, s - 21], 250, 290, fill=(224, 181, 48), width=12)
    for k in range(9):
        a = math.radians(210 - k * 30)
        d.line([(64 + 40 * math.cos(a), 64 - 40 * math.sin(a)), (64 + 52 * math.cos(a), 64 - 52 * math.sin(a))],
               fill=(43, 47, 53), width=4)
    a = math.radians(90)
    d.line([(64, 64), (64 + 44 * math.cos(a), 64 - 44 * math.sin(a))], fill=(200, 66, 58), width=6)
    d.ellipse([56, 56, 72, 72], fill=(43, 47, 53))
    return np.asarray(im, float) / 255.0


def make_materials():
    m = K.materials()
    m.update({
        "red": lt.textured("fx_red", K.half_res(lt.paint_surface(911, "#c8423a", "#4a4e54")), normal_strength=0.5),
        "black": lt.textured("fx_black", K.half_res(lt.rubber_surface(913)), normal_strength=1.5),
        "label": lt.decal("fx_label", label_image(), rough=0.45),
        "gauge": lt.decal("fx_gauge", gauge_image(), rough=0.2),
        "glass": pt.plain("fx_glass", "#d8e2e6", 0.05, 0.0),
        "tag": lt.textured("fx_tag", K.half_res(lt.paint_surface(915, "#e0b530", "#4a4e54")), normal_strength=0.4),
    })
    return m


# =====================================================================================================
# Shapes
# =====================================================================================================

def band(g, r, z0, z1, a0, a1, mat, steps=32, uv=True):
    """A thin curved band round the Z axis (the label), from angle a0 to a1, with UVs across it."""
    verts, faces, uvs = [], [], []
    for k in range(steps + 1):
        a = a0 + (a1 - a0) * k / steps
        verts += [(r * math.cos(a), r * math.sin(a), z0), (r * math.cos(a), r * math.sin(a), z1)]
    for k in range(steps):
        i = 2 * k
        faces.append((i, i + 2, i + 3, i + 1))
        # u runs left to right as seen from outside (a decreasing from the viewer's left)
        u0, u1 = 1 - k / steps, 1 - (k + 1) / steps
        uvs.append(((u0, 0), (u1, 0), (u1, 1), (u0, 1)))
    return g.add(verts, faces, mat, uvs=uvs if uv else None)


def body(g):
    """The red cylinder with its domed shoulder, the rubber foot and the neck collar."""
    profile = [(0.0, Z_BODY), (R - 0.006, Z_BODY), (R - 0.001, Z_BODY + 0.004), (R, Z_BODY + 0.012)]
    profile += [(R, Z_SHOULDER)]
    for k in range(1, 9):
        t = k / 8
        a = t * math.pi / 2
        profile.append((R_NECK + (R - R_NECK) * math.cos(a), Z_SHOULDER + (Z_NECK - Z_SHOULDER) * math.sin(a)))
    profile += [(0.0, Z_NECK)]
    lathe(g, profile, "red", steps=48)
    # two pressed rings round the body where the seams are
    for z in (Z_BODY + 0.05, Z_SHOULDER - 0.004):
        ring(g, z - 0.003, z + 0.003, R - 0.002, R + 0.0016, "red", segs=48)
    # the graphite rubber foot it stands on
    lathe(g, [(0.0, Z_FOOT - 0.004), (R - 0.004, Z_FOOT - 0.004), (R + 0.004, Z_FOOT + 0.002),
              (R + 0.005, Z_BODY + 0.014), (R - 0.004, Z_BODY + 0.016), (0.0, Z_BODY + 0.016)], "black", steps=48)
    # the steel neck collar and the valve's threaded boss
    cylinder(g, (0, 0, Z_NECK - 0.004), (0, 0, Z_NECK + 0.012), R_NECK + 0.004, "steel", segs=32)
    cylinder(g, (0, 0, Z_NECK + 0.012), (0, 0, Z_NECK + 0.02), R_NECK - 0.002, "steel", segs=6, phase=math.pi / 6)


def label(g):
    """The white label wrapped round the front, a hair proud of the paint."""
    mid = -math.pi / 2
    band(g, R + 0.0012, LABEL[0], LABEL[1], mid + LABEL_SPAN / 2, mid - LABEL_SPAN / 2, "label")


def head(g):
    """The valve head: a steel block on the neck, a pressure gauge on the front, the carry handle and the squeeze lever
    out the back, the safety pin through them with a mustard tag. Returns the hose outlet and its direction."""
    zc = Z_HEAD
    box(g, (0, 0.004, zc), (0.02, 0.026, 0.016), "steel", bevel=0.004)
    cylinder(g, (0, 0.004, zc + 0.016), (0, 0.004, zc + 0.024), 0.012, "steel", segs=24)
    # the gauge on the front: a steel bezel, its face and a glass
    gy = -0.03
    cylinder(g, (0, -0.02, zc + 0.002), (0, gy, zc + 0.002), 0.017, "steel", segs=28)
    gz = zc + 0.002
    face = [(0.0145 * math.cos(2 * math.pi * k / 28), gy - 0.0006, gz + 0.0145 * math.sin(2 * math.pi * k / 28)) for k in range(28)]
    g.add(face, [tuple(range(28))], "gauge", uvs=[[(0.5 + 0.5 * math.cos(2 * math.pi * k / 28) * 0.9,
                                                      0.5 + 0.5 * math.sin(2 * math.pi * k / 28) * 0.9) for k in range(28)]])
    # the carry handle (lower, fixed) and the squeeze lever (upper, pivoted at the front), out the back (+Y)
    box(g, (0, 0.06, zc - 0.006), (0.011, 0.045, 0.004), "steel", bevel=0.002)
    box(g, (0, 0.085, zc - 0.012), (0.012, 0.03, 0.006), "graphite", bevel=0.003)          # its grip
    box(g, (0, 0.056, zc + 0.022), (0.01, 0.05, 0.004), "steel", bevel=0.002,
        rot=((1, 0, 0), (0, math.cos(0.12), -math.sin(0.12)), (0, math.sin(0.12), math.cos(0.12))))
    cylinder(g, (-0.012, 0.012, zc + 0.018), (0.012, 0.012, zc + 0.018), 0.004, "steel", segs=12)  # the lever's pivot
    # the safety pin through the head, its ring on the right and a mustard tag hanging from it
    cylinder(g, (-0.024, 0.026, zc + 0.008), (0.03, 0.026, zc + 0.008), 0.0018, "steel", segs=8)
    pts = [(0.034, 0.026 + 0.009 * math.sin(2 * math.pi * k / 16), zc - 0.001 + 0.009 * math.cos(2 * math.pi * k / 16))
           for k in range(17)]
    sweep(g, pts, 0.0014, "steel", segs=6)
    box(g, (0.036, 0.026, zc - 0.018), (0.0008, 0.009, 0.007), "tag", bevel=0.0005)
    # the hose outlet on the left side of the head
    out = Vector((-0.02, -0.004, zc - 0.004))
    cylinder(g, out, out + Vector((-0.014, 0, 0)), 0.006, "steel", segs=12)
    hexnut_at = out + Vector((-0.012, 0, 0))
    K.bl.hexnut(g, hexnut_at, (-1, 0, 0), 0.0085, 0.008, "steel")
    return out + Vector((-0.02, 0, 0)), Vector((-1, 0, 0))


def nozzle(g, tip, direction, mat="black"):
    """The flared horn at the hose's end, from `tip - direction * length` to its mouth at `tip`; a steel ferrule behind it."""
    d = Vector(direction).normalized()
    root = Vector(tip) - d * 0.05
    cylinder(g, root - d * 0.014, root, 0.0085, "steel", segs=16)
    cylinder(g, root, Vector(tip), 0.007, mat, segs=20, r1=0.0145, cap1=False)
    cylinder(g, Vector(tip) - d * 0.002, Vector(tip), 0.0145, mat, segs=20, r1=0.0155, cap0=False)
    bbe.disc(g, Vector(tip) - d * 0.03, d, 0.008, "hole", segs=16)


def hose_stowed(g, start, out):
    """The hose down the left side of the body to the clip, the nozzle pointing down beside the foot."""
    s = Vector(start)
    p = [s, s + out * 0.02 + Vector((0, 0, -0.005)), Vector((-R - 0.016, -0.006, 0.07)), Vector((-R - 0.012, -0.004, 0.02))]
    path = bezier(p[0], p[1], p[2], p[3], n=12)
    path += bezier(Vector((-R - 0.012, -0.004, 0.02)), Vector((-R - 0.01, -0.003, -0.03)), Vector((-R - 0.011, -0.002, -0.07)),
                   Vector((-R - 0.012, 0.0, -0.11)), n=10)[1:]
    sweep(g, path, 0.0065, "black", segs=10)
    # the clip strap round the body holding the nozzle
    ring(g, -0.118, -0.106, R + 0.0005, R + 0.0035, "steel", segs=48)
    box(g, (-R - 0.012, 0.0, -0.112), (0.006, 0.012, 0.009), "steel", bevel=0.002)
    nozzle(g, (-R - 0.012, 0.0, -0.19), (0, 0, -1))


def hose_held(g, start, out):
    """The hose off its clip: up from the outlet, over the top and forward, the nozzle aimed along -Y. Returns the mouth."""
    s = Vector(start)
    tip = Vector((-0.03, -0.2, Z_HEAD + 0.03))
    d = Vector((0, -1, 0))
    root = tip - d * 0.064
    p = [s, s + out * 0.04, root + Vector((0.0, 0.07, 0.03)), root]
    sweep(g, bezier(p[0], p[1], p[2], p[3], n=18), 0.0065, "black", segs=10)
    ring(g, -0.118, -0.106, R + 0.0005, R + 0.0035, "steel", segs=48)
    box(g, (-R - 0.012, 0.0, -0.112), (0.006, 0.012, 0.009), "steel", bevel=0.002)
    nozzle(g, tip, d)
    return tip


def build_item(mats, held=False, name="FireExtinguisher"):
    rig = {"root": bl.make_empty(name)}
    g = Geo()
    body(g)
    label(g)
    start, out = head(g)
    if held:
        tip = hose_held(g, start, out)
    else:
        hose_stowed(g, start, out)
    # The held model's origin is the carry handle's grip, where the palm closes on it; the item's is its middle.
    shift = -GRIP if held else Vector((0, 0, 0))
    bl.make_object(name + "Body", g, mats, rig["root"], location=tuple(shift))
    if held:
        rig["nozzle"] = bl.make_empty("nozzle", rig["root"], location=tuple(tip + shift))
    return rig


ICON_VIEW = (-1.2, -1.6, 0.75)


def main():
    K.reset_scene()
    mats = make_materials()
    item = build_item(mats)
    i = K.export(item["root"], MESHES / "fire_extinguisher.glb", animations=False)
    K.report(MESHES / "fire_extinguisher.glb", i)
    bbe.render_icon(bbe.mesh_tree(item["root"]), ICONS / "fire_extinguisher_icon.png", view=ICON_VIEW, ortho=0.56,
                    target=(0.0, 0.0, -0.005))
    for ob in [item["root"], *item["root"].children_recursive]:
        ob.hide_render = True
        ob.hide_viewport = True
    held = build_item(mats, held=True, name="FireExtinguisherHeld")
    i = K.export(held["root"], MESHES / "fire_extinguisher_held.glb", animations=False)
    K.report(MESHES / "fire_extinguisher_held.glb", i)
    print(f"wrote {(ICONS / 'fire_extinguisher_icon.png').relative_to(REPO)}")
    if "--render" in sys.argv:
        for ob in [item["root"], *item["root"].children_recursive]:
            ob.hide_render = False
            ob.hide_viewport = False
        import render_fire_extinguisher
        render_fire_extinguisher.render_all(item, held, Path(sys.argv[sys.argv.index("--render") + 1]), sys.modules[__name__])


if __name__ == "__main__":
    main()
